// CSX2 Dash - SimHub plugin that drives the Cube Controls CSX2 wheel's LCD and LEDs using the user's
// existing UGT Manager layouts, replacing UGT Manager during races.
//
// Safety: only display commands are sent (clear, draw text, show SD image, LED mask, brightness, font load).
// Nothing is written to the wheel's EEPROM or SD card. The plugin never talks to the wheel while
// UGT Manager is running (two programs on the wheel's command channel is what caused the FanaLEDs lock-ups).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using GameReaderCommon;
using SimHub.Plugins;

namespace CSX2Dash
{
    public class Csx2Settings
    {
        public bool Enabled = true;
        public int PageLayoutId = 1;
        public int LcdBrightness = 9;        // 1..16 (UGT Manager's default LCD level)
        public int LedBrightness = 52;       // red/blue/yellow on-time; green uses half (UGT default ratio)
        public bool ShiftFlash = false;
        public bool FlagLeds = true;
        public bool ScreenOffWhenIdle = true;  // backlight + LEDs off when no game is live and when SimHub closes
        public bool BrakeBiasPopup = true;     // show UGT pop-up page 501 when brake bias changes
        public int BrakeBiasPopupMs = 1000;    // how long it stays up after the last change (UGT default Display_BBias)
    }

    [PluginDescription("Drives the Cube Controls CSX2 (UGT) wheel screen and shift lights from SimHub, using your UGT Manager layouts")]
    [PluginAuthor("0xjohnjov")]
    [PluginName("CSX2 Dash")]
    public class Csx2DashPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        public PluginManager PluginManager { get; set; }
        public Csx2Settings Settings;
        public UgtFiles Ugt;
        public volatile string Status = "starting";
        public string FirmwareVersion;

        public ImageSource PictureIcon { get { return Icon.Make(); } }
        public string LeftMenuTitle { get { return "CSX2 Dash"; } }

        readonly Csx2Device dev = new Csx2Device();
        readonly ShiftLights leds = new ShiftLights();
        Thread worker;
        volatile bool running;
        bool dark;   // wheel backlight is off (worker thread only)
        volatile bool pageDirty = true, settingsDirty = true, reloadRequested;

        // Latest SimHub frame, handed from DataUpdate to the worker thread.
        volatile StatusDataBase frame;
        volatile bool gameLive;
        readonly Fields fields = new Fields();

        public void Init(PluginManager pluginManager)
        {
            Settings = this.ReadCommonSettings<Csx2Settings>("CSX2DashSettings", () => new Csx2Settings());
            Reload();

            this.AttachDelegate("Status", () => Status);
            this.AttachDelegate("Page", () => Settings.PageLayoutId);
            this.AddAction("NextPage", (a, b) => StepPage(+1));
            this.AddAction("PreviousPage", (a, b) => StepPage(-1));
            this.AddAction("ToggleShiftFlash", (a, b) => { Settings.ShiftFlash = !Settings.ShiftFlash; settingsDirty = true; });
            // Leaderboard pages (layouts with driver rows): UGT's Extra_Info_Cycle and Sub_Page_Cycle.
            this.AddAction("CycleDriverInfo", (a, b) => { fields.SubInfo = (fields.SubInfo + 1) % 3; pageDirty = true; });
            this.AddAction("NextDriverPage", (a, b) =>
            {
                var f = frame; int cars = f != null && f.Opponents != null ? f.Opponents.Count : 0;
                fields.SubPage = cars > (fields.SubPage + 1) * 8 ? fields.SubPage + 1 : 0; pageDirty = true;
            });
            fields.PM = pluginManager;

            running = true;
            worker = new Thread(WorkerLoop) { IsBackground = true, Name = "CSX2Dash" };
            worker.Start();
            SimHub.Logging.Current.Info("CSX2 Dash started: " + Ugt.Layouts.Count + " UGT layouts, TOC " + (Ugt.TocFile ?? "none"));
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            gameLive = data.GameRunning && data.NewData != null && !data.GameReplay;
            frame = data.NewData;
            fields.OnFrame(data);
        }

        public void End(PluginManager pluginManager)
        {
            running = false;
            if (worker != null) worker.Join(3000);
            this.SaveCommonSettings("CSX2DashSettings", Settings);
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsView(this);
        }

        // ------------------------------------------------------------ called from the settings UI

        public void Reload() { Ugt = UgtFiles.Load(); leds.Table = Ugt.ShiftTable; leds.LimiterMode = Ugt.PitLimiterMode; fields.Metric = Ugt.Metric; reloadRequested = false; pageDirty = true; }
        public void RequestReload() { reloadRequested = true; }
        public void SettingsChanged() { settingsDirty = true; pageDirty = true; }

        public void StepPage(int dir)
        {
            var ids = Ugt.Layouts.Select(l => l.Id).ToList();
            if (ids.Count == 0) return;
            int i = ids.IndexOf(Settings.PageLayoutId);
            i = i < 0 ? 0 : (i + dir + ids.Count) % ids.Count;
            Settings.PageLayoutId = ids[i];
            pageDirty = true;
        }

        // ------------------------------------------------------------ worker

        enum Mode { None, Idle, Live }

        void WorkerLoop()
        {
            var clock = Stopwatch.StartNew();
            long nextOpenTry = 0;
            Mode shown = Mode.None;
            Layout current = null;                             // the user's selected page
            Layout onScreen = null;                            // what's drawn now: current, or a UGT pop-up page
            double lastBias = double.NaN;                      // brake bias pop-up (UGT special page 501)
            long popupUntil = 0;
            var fontRefs = new Dictionary<int, int>();          // SD font number -> slot 3..17
            var sent = new Dictionary<Widget, string>();
            byte[] lastLeds = null;

            while (running)
            {
                try
                {
                    if (reloadRequested) { Reload(); current = null; }

                    if (!Settings.Enabled) { Release(ref shown); Status = "disabled"; Thread.Sleep(500); continue; }
                    if (Process.GetProcessesByName("UGTManager").Length > 0)
                    {
                        Release(ref shown); Status = "paused - UGT Manager is running (close it to let SimHub drive the wheel)";
                        Thread.Sleep(2000); continue;
                    }

                    if (!dev.IsOpen)
                    {
                        if (shown != Mode.None)
                        {
                            string why = "lost the wheel (" + (dev.LastError ?? "?") + ") while " + shown + (current != null ? ", page " + current.Id : "");
                            SimHub.Logging.Current.Warn("CSX2 Dash: " + why + " - packet log: " + (dev.DumpRecorder(why) ?? "(could not write)"));
                        }
                        shown = Mode.None;
                        if (clock.ElapsedMilliseconds < nextOpenTry) { Thread.Sleep(200); continue; }
                        nextOpenTry = clock.ElapsedMilliseconds + 3000;
                        if (!dev.Open()) { Status = dev.LastError; continue; }
                        FirmwareVersion = dev.FirmwareVersion();
                        if (FirmwareVersion == null || !FirmwareVersion.StartsWith("UGT"))
                        {
                            if (!Status.StartsWith("wheel not responding")) SimHub.Logging.Current.Warn("CSX2 Dash: wheel not responding (" + (dev.LastError ?? "?") + ")");
                            Status = "wheel not responding - run the CSX2 Recovery shortcut";
                            dev.Close(); continue;
                        }
                        int licence = dev.LicenceHandshake();   // without this the wheel freezes 30-60 s after power-up
                        if (licence < 15) SimHub.Logging.Current.Warn("CSX2 Dash: licence handshake failed at round " + licence);
                        fontRefs.Clear(); settingsDirty = true; pageDirty = true; dark = false;
                        SimHub.Logging.Current.Info("CSX2 Dash connected, firmware " + FirmwareVersion + ", licence " + (licence == 15 ? "OK" : "FAILED"));
                    }

                    if (settingsDirty)
                    {
                        settingsDirty = false;
                        int v = Settings.LedBrightness;
                        dev.LedBrightness(1, v); dev.LedBrightness(2, Math.Max(1, v / 2)); dev.LedBrightness(3, v); dev.LedBrightness(4, v);
                        if (!dark) dev.LcdBrightness(Settings.LcdBrightness);   // keep a dark idle screen dark
                        leds.ShiftFlash = Settings.ShiftFlash; leds.FlagLeds = Settings.FlagLeds;
                        if (shown == Mode.Idle) shown = Mode.None;                // re-apply the idle screen (setting may have changed)
                    }

                    var d = frame;
                    if (!gameLive || d == null)
                    {
                        if (shown != Mode.Idle)
                        {
                            if (Settings.ScreenOffWhenIdle) Blank();
                            else
                            {
                                if (dark) { dev.LcdBrightness(Settings.LcdBrightness); dark = false; }
                                dev.ClearScreen(); dev.BrandScreen(); dev.SetLeds(new byte[5]);
                            }
                            shown = Mode.Idle; lastLeds = null;
                        }
                        lastBias = double.NaN; popupUntil = 0; onScreen = null;
                        Status = "connected (" + FirmwareVersion + ") - waiting for a game" + (dark ? " (wheel screen off)" : "");
                        Thread.Sleep(250);
                        continue;
                    }

                    // ---- live
                    if (dark) { dev.LcdBrightness(Settings.LcdBrightness); dark = false; }
                    if (shown != Mode.Live || pageDirty || current == null)
                    {
                        pageDirty = false;
                        current = Ugt.Layouts.FirstOrDefault(l => l.Id == Settings.PageLayoutId) ?? Ugt.Layouts.FirstOrDefault();
                        if (current == null) { Status = "no UGT layouts found"; Thread.Sleep(1000); continue; }
                        onScreen = null;                               // force a redraw below
                    }

                    // Brake bias pop-up, as UGT's BrakeBias_Change (L28876): each change (re)starts the timer.
                    // Ignore 0 so a session start (SimHub reporting 0 before the real value) doesn't trigger it.
                    var biasPage = Settings.BrakeBiasPopup ? Ugt.Specials.FirstOrDefault(l => l.Id == 501) : null;
                    double bias = Fields.BrakeBiasPercent(d);
                    if (biasPage != null && lastBias > 0 && bias > 0 && Math.Abs(bias - lastBias) > 0.01)
                        popupUntil = clock.ElapsedMilliseconds + Settings.BrakeBiasPopupMs;
                    lastBias = bias;
                    var target = biasPage != null && clock.ElapsedMilliseconds < popupUntil ? biasPage : current;

                    if (target != onScreen)
                    {
                        ShowPage(target, fontRefs);
                        onScreen = target;
                        sent.Clear(); lastLeds = null; shown = Mode.Live;
                        fields.ResetScroll();
                    }

                    var mask = leds.Compute(fields.LedInput(d), clock.ElapsedMilliseconds);
                    bool ledsChanged = lastLeds == null || !mask.SequenceEqual(lastLeds);
                    bool anyText = false;
                    foreach (var w in onScreen.Widgets)
                    {
                        if (w.X > 480 || w.Y > 272) continue;
                        ushort colour;
                        string s = fields.Format(w, d, out colour);
                        string sig = s + "\u0001" + colour;
                        string old;
                        if (sent.TryGetValue(w, out old) && old == sig) continue;
                        int fontRef = w.Font <= 2 ? Math.Max(1, w.Font) : (fontRefs.ContainsKey(w.Font) ? fontRefs[w.Font] : 2);
                        if (!dev.DrawText(mask, w.X, w.Y, colour, w.WMul, w.HMul, fontRef, s)) break;
                        sent[w] = sig; anyText = true;
                    }
                    if (ledsChanged && !anyText) dev.SetLeds(mask);
                    if (ledsChanged || anyText) lastLeds = mask;

                    Status = (onScreen == current ? "live - page " + current.Id : "live - brake bias pop-up") + " (" + FirmwareVersion + ")";
                    Thread.Sleep(15);
                }
                catch (Exception e)
                {
                    Status = "error: " + e.Message;
                    SimHub.Logging.Current.Error("CSX2 Dash: " + e);
                    Thread.Sleep(1000);
                }
            }
            Release(ref shown);
        }

        void ShowPage(Layout l, Dictionary<int, int> fontRefs)
        {
            dev.ShowPage(l, fontRefs, w => SimHub.Logging.Current.Warn("CSX2 Dash: " + w));
        }

        // UGT's "turn off": clear, LEDs off, backlight level 0. UGT Manager resets its cached LCD level to 0 on
        // connect (UGTManager.cs L21013), so it always turns the backlight back on when it takes over.
        void Blank()
        {
            dev.ClearScreen(); dev.SetLeds(new byte[5]); dev.LcdOff();
            dark = true;
        }

        // Hand the wheel back. On SimHub exit (running == false) blank it if the user wants that; when pausing for
        // UGT Manager or when disabled, leave the brand screen at normal brightness.
        void Release(ref Mode shown)
        {
            if (dev.IsOpen)
            {
                if (!running && Settings.ScreenOffWhenIdle) Blank();
                else if (shown != Mode.None || dark)
                {
                    if (dark) dev.LcdBrightness(Settings.LcdBrightness);
                    dev.ClearScreen(); dev.BrandScreen(); dev.SetLeds(new byte[5]);
                }
                dev.Close();
            }
            dark = false;
            shown = Mode.None;
        }
    }

    static class Icon
    {
        static ImageSource cached;
        public static ImageSource Make()
        {
            if (cached != null) return cached;
            // 24x24 steering-wheel-ish ring, white on transparent (SimHub tints menu icons).
            var g = new GeometryGroup();
            g.Children.Add(new EllipseGeometry(new System.Windows.Point(12, 12), 10, 10));
            g.Children.Add(new EllipseGeometry(new System.Windows.Point(12, 12), 3, 3));
            g.Children.Add(new LineGeometry(new System.Windows.Point(2, 12), new System.Windows.Point(9, 12)));
            g.Children.Add(new LineGeometry(new System.Windows.Point(15, 12), new System.Windows.Point(22, 12)));
            g.Children.Add(new LineGeometry(new System.Windows.Point(12, 15), new System.Windows.Point(12, 22)));
            var d = new GeometryDrawing(null, new Pen(Brushes.White, 2), g);
            var img = new DrawingImage(d); img.Freeze();
            cached = img;
            return img;
        }
    }
}
