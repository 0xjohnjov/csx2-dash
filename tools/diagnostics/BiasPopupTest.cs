// Shows UGT's brake bias pop-up (special page 501) on the wheel the way CSX2 Dash does in a race:
// your first normal page, then page 501 with a changing brake bias value, then back.
// Waits for a replug (or --now), licenses the wheel. Run with UGT Manager and SimHub CLOSED.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CSX2Dash;

class BiasPopupTest
{
    static StreamWriter log;
    static void Log(string s) { string l = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s; Console.WriteLine(l); log.WriteLine(l); log.Flush(); }
    static bool Present() { var x = new Csx2Device(); bool ok = x.Open(); x.Close(); return ok; }

    static void Draw(Csx2Device d, Layout l, Dictionary<int, int> fontRefs, Func<Widget, string> text)
    {
        foreach (var w in l.Widgets)
        {
            if (w.X > 480 || w.Y > 272) continue;
            int fontRef = w.Font <= 2 ? Math.Max(1, w.Font) : (fontRefs.ContainsKey(w.Font) ? fontRefs[w.Font] : 2);
            d.DrawText(new byte[5], w.X, w.Y, w.Colour, w.WMul, w.HMul, fontRef, text(w));
        }
    }

    static int Main(string[] args)
    {
        log = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "biaspopuptest.log"), true);
        Log("==== BiasPopupTest ====");
        var ugt = UgtFiles.Load();
        var page = ugt.Layouts.FirstOrDefault();
        var popup = ugt.Specials.FirstOrDefault(l => l.Id == 501);
        Log("normal page: " + (page == null ? "none" : page.ToString()) + ", pop-up 501: " + (popup == null ? "NOT FOUND (layouts\\Special\\501.xml)" : popup + ", background " + (popup.BackgroundSector.HasValue ? "yes" : "no")));
        if (page == null || popup == null) return 1;

        if (!(args.Length > 0 && args[0] == "--now"))
        {
            Log("Waiting for the wheel to be unplugged ...");
            while (Present()) Thread.Sleep(200);
            Log("Unplugged. Waiting for it to come back ...");
            while (!Present()) Thread.Sleep(100);
            Log("Plugged in.");
            Thread.Sleep(1500);
        }
        var d = new Csx2Device();
        for (int i = 0; i < 10 && !d.Open(); i++) Thread.Sleep(300);
        if (!d.IsOpen) { Log("open failed: " + d.LastError); return 2; }
        Log("fw: " + (d.FirmwareVersion() ?? "(no reply)") + ", licence " + d.LicenceHandshake() + "/15");
        d.LcdBrightness(12);

        var fontRefs = new Dictionary<int, int>();
        Log("Normal page " + page.Id + " for 3 s ...");
        d.ShowPage(page, fontRefs, w => Log("  warn: " + w));
        Draw(d, page, fontRefs, w => "88");
        Thread.Sleep(3000);

        Log("Brake bias pop-up (501): 56.0 -> 56.5 -> 57.0 -> 57.5, 1 s each ...");
        d.ShowPage(popup, fontRefs, w => Log("  warn: " + w));
        foreach (double b in new[] { 56.0, 56.5, 57.0, 57.5 })
        {
            Draw(d, popup, fontRefs, w => w.Key == "BBias" ? Fields.Number(b, w.Digits, 1, false).PadLeft(w.Digits) : "");
            Thread.Sleep(1000);
        }

        Log("Back to page " + page.Id + " ...");
        d.ShowPage(page, fontRefs, w => Log("  warn: " + w));
        Draw(d, page, fontRefs, w => "88");
        string fw = d.FirmwareVersion();
        Log("wheel after test: " + (fw ?? "NO REPLY"));
        d.Close();
        return fw != null ? 0 : 3;
    }
}
