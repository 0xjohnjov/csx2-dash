// Reads the user's existing UGT Manager data (read-only): layouts, shift-light tables, SD-card index (TOC).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace CSX2Dash
{
    public class Widget
    {
        public string Key;
        public int X, Y, WMul = 1, HMul = 1, Font, Digits;
        public ushort Colour;
    }

    public class Layout
    {
        public int Id;
        public List<int> Fonts = new List<int>();
        public List<Widget> Widgets = new List<Widget>();
        public uint? BackgroundSector;          // from TOC entry BGI0000N
        public override string ToString() { return "Layout " + Id + " (" + Widgets.Count + " fields)"; }
    }

    public class UgtFiles
    {
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UltimateGameTech");

        public List<Layout> Layouts = new List<Layout>();
        public List<Layout> Specials = new List<Layout>();   // UGT pop-up pages, layouts\Special\5xx.xml (501 = brake bias)
        public int[] ShiftTable;                // 16 per-mille entries; [0..14] rev LEDs, [15] shift point
        public string ShiftTableName;
        public string PitLimiterMode;           // Alternate_Blinking / All_Blinking / Off
        public int LcdContrast = 9;          // UGT default when General.xml has none
        public bool Metric = true;
        public string TocFile;
        public List<string> Warnings = new List<string>();

        // DTDs off and no external resolver: never let a layout/settings file pull in other files or expand entities.
        static XDocument LoadXml(string path)
        {
            var s = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var r = XmlReader.Create(path, s)) return XDocument.Load(r);
        }

        public static UgtFiles Load()
        {
            var u = new UgtFiles();
            u.LoadGeneral();
            var toc = u.LoadToc();
            u.LoadLayouts(Path.Combine(Root, "layouts"), u.Layouts, toc, true);
            u.LoadLayouts(Path.Combine(Root, "layouts", "Special"), u.Specials, toc, false);
            return u;
        }

        void LoadGeneral()
        {
            string f = Path.Combine(Root, "Settings_xml", "General.xml");
            ShiftTableName = "Single";
            ShiftTable = new[] { 820, 840, 860, 870, 880, 890, 900, 910, 915, 920, 925, 930, 935, 938, 940, 980 };
            PitLimiterMode = "Alternate_Blinking";
            if (!File.Exists(f)) { Warnings.Add("General.xml not found - using UGT default shift lights"); return; }
            var x = LoadXml(f).Root;
            ShiftTableName = (string)x.Element("Shift_Lights_Type") ?? "Single";
            PitLimiterMode = (string)x.Element("SPD_Limit_Type") ?? PitLimiterMode;
            int c; if (int.TryParse((string)x.Element("LCD_Contrast"), out c)) LcdContrast = c;
            bool m; if (bool.TryParse((string)x.Element("Metric"), out m)) Metric = m;
            var tbl = x.Element("RpmTables");
            // UGT's Green_Red_Blue option uses the same sequential lighting as Single (see docs), so any 16-entry table works.
            var t = tbl == null ? null : tbl.Element(ShiftTableName.Replace("/", "_"));
            if (t != null)
            {
                var vals = t.Elements("int").Select(e => (int)e).ToArray();
                if (vals.Length == 16) ShiftTable = vals;
                else Warnings.Add("Shift table '" + ShiftTableName + "' has " + vals.Length + " entries - using Single");
            }
        }

        // TOC_List.csv is UGT's cached copy of the SD card index; the most recently updated one matches the wheel.
        Dictionary<string, uint> LoadToc()
        {
            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            string sd = Path.Combine(Root, "SDCard");
            if (!Directory.Exists(sd)) { Warnings.Add("No UGT SD-card index found - backgrounds disabled"); return map; }
            var newest = Directory.GetFiles(sd, "TOC_List.csv", SearchOption.AllDirectories)
                                  .OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
            if (newest == null) { Warnings.Add("No TOC_List.csv - backgrounds disabled"); return map; }
            TocFile = newest;
            foreach (var line in File.ReadLines(newest).Skip(1))
            {
                var c = line.Split(';');
                if (c.Length < 6 || !c[2].Contains("_1_Active")) continue;
                uint sect;
                if (uint.TryParse(c[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out sect))
                    map[c[3] + "." + c[4]] = sect;      // e.g. "BGI00001.gci" -> 12206
            }
            return map;
        }

        void LoadLayouts(string dir, List<Layout> into, Dictionary<string, uint> toc, bool required)
        {
            if (!Directory.Exists(dir)) { if (required) Warnings.Add("UGT layouts folder not found"); return; }
            foreach (var f in Directory.GetFiles(dir, "*.xml"))
            {
                try
                {
                    var x = LoadXml(f).Root;
                    var l = new Layout { Id = (int)x.Element("ID") };
                    var uf = x.Element("UsedFonts");
                    if (uf != null) l.Fonts = uf.Elements("int").Select(e => (int)e).ToList();
                    var ws = x.Element("Widgets");
                    if (ws != null)
                        foreach (var w in ws.Elements("Widget"))
                            l.Widgets.Add(new Widget
                            {
                                Key = (string)w.Element("KeyShort"),
                                X = (int)w.Element("Xpos"), Y = (int)w.Element("Ypos"),
                                Colour = (ushort)(((int)w.Element("ColMsb") << 8) | (int)w.Element("ColLsb")),
                                WMul = (int?)w.Element("W_Multiplier") ?? 1, HMul = (int?)w.Element("H_Multiplier") ?? 1,
                                Font = (int?)w.Element("Font") ?? 2, Digits = (int?)w.Element("Digits") ?? 3,
                            });
                    uint s;
                    if (toc.TryGetValue(string.Format("BGI{0:00000}.gci", l.Id), out s)) l.BackgroundSector = s;
                    into.Add(l);
                }
                catch (Exception e) { Warnings.Add(Path.GetFileName(f) + ": " + e.Message); }
            }
            into.Sort((a, b) => a.Id.CompareTo(b.Id));
        }
    }
}
