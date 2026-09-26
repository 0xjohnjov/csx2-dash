// Race-like stress test of CSX2 Dash's wheel code: after each page change (Csx2Device.ShowPage, the code
// SimHub runs) it streams changing values into every widget plus moving rev-LED patterns for 3 s, like a
// live session, then flips to the next page. Works with any number of UGT layouts. At the end it flips back
// and forth between the two layouts that use the most fonts (the heaviest page change). Press wheel buttons / pull paddles while it runs. Run with UGT Manager and SimHub CLOSED.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CSX2Dash;

class LiveStressTest
{
    static StreamWriter log;
    static void Log(string s) { string l = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s; Console.WriteLine(l); log.WriteLine(l); log.Flush(); }
    static bool Present() { var x = new Csx2Device(); bool ok = x.Open(); x.Close(); return ok; }

    static int Main()
    {
        log = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "livestresstest.log"), true);
        bool allLeds = Environment.GetCommandLineArgs().Contains("--all-leds");
        Log("==== LiveStressTest" + (allLeds ? " (ALL LEDs ON)" : "") + " ====");
        var ugt = UgtFiles.Load();

        Log("Waiting for the wheel to be unplugged ...");
        while (Present()) Thread.Sleep(200);
        Log("Unplugged. Waiting for it to come back ...");
        while (!Present()) Thread.Sleep(100);
        Log("Plugged in.");
        Thread.Sleep(1500);

        var d = new Csx2Device();
        for (int i = 0; i < 10 && !d.Open(); i++) Thread.Sleep(300);
        if (!d.IsOpen) { Log("open failed: " + d.LastError); return 1; }
        Log("fw: " + (d.FirmwareVersion() ?? "(no reply)"));
        int lic = d.LicenceHandshake();
        Log("licence: " + lic + "/15");
        if (lic < 15) return 2;
        d.LedBrightness(1, 52); d.LedBrightness(2, 26); d.LedBrightness(3, 52); d.LedBrightness(4, 52); d.LcdBrightness(9);

        // Same order as clicking "next page" twice round, then the two font-heaviest layouts hammered.
        if (ugt.Layouts.Count == 0) { Log("No UGT layouts found in " + UgtFiles.Root); return 1; }
        var order = new List<int>();
        for (int r = 0; r < 2; r++) order.AddRange(ugt.Layouts.Select(l => l.Id));
        var heavy = ugt.Layouts.OrderByDescending(l => l.Fonts.Where(f => f > 2).Distinct().Count()).Take(2).Select(l => l.Id).ToList();
        Log("Layouts: " + ugt.Layouts.Count + ", heaviest page change: " + string.Join(" <-> ", heavy));
        for (int r = 0; r < 5; r++) order.AddRange(heavy);

        if (allLeds) d.SetLeds(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        var fontRefs = new Dictionary<int, int>();
        var rnd = new Random();
        int n = 0, tick = 0;
        foreach (int id in order)
        {
            var l = ugt.Layouts.FirstOrDefault(x => x.Id == id);
            if (l == null) continue;
            n++;
            var sw = Stopwatch.StartNew();
            d.ShowPage(l, fontRefs, w => Log("    warn: " + w));
            long pageMs = sw.ElapsedMilliseconds;
            if (!d.IsOpen) { Log(string.Format(">>> WHEEL DIED during page change {0} (to layout {1}) - {2}", n, id, d.LastError)); return 3; }

            // 3 s of live-like traffic: every widget redrawn with a changing value, LEDs sweeping.
            int packets = 0;
            sw.Restart();
            while (sw.ElapsedMilliseconds < 3000)
            {
                tick++;
                int lit = tick % 16;
                var mask = new byte[5];
                for (int b = 0; b < lit && b < 40; b++) mask[b / 8] |= (byte)(1 << (b % 8));
                if (allLeds) for (int b = 0; b < 5; b++) mask[b] = 0xFF;
                foreach (var w in l.Widgets)
                {
                    if (w.X > 480 || w.Y > 272) continue;
                    int fontRef = w.Font <= 2 ? Math.Max(1, w.Font) : (fontRefs.ContainsKey(w.Font) ? fontRefs[w.Font] : 2);
                    string s = rnd.Next(2) == 0 ? (tick % 1000).ToString() : "1:2" + (tick % 10) + "." + rnd.Next(100, 999);
                    if (!d.DrawText(mask, w.X, w.Y, 0xFFFF, w.WMul, w.HMul, fontRef, s)) break;
                    packets++;
                }
                if (!d.IsOpen) break;
                Thread.Sleep(15);
            }
            if (!d.IsOpen) { Log(string.Format(">>> WHEEL DIED while streaming live data on layout {0} after {1} packets - {2}", id, packets, d.LastError)); return 4; }
            Log(string.Format("  page change {0,2} -> layout {1}: {2} ms, {3} fonts loaded; then {4} live packets OK", n, id, pageMs, fontRefs.Count, packets));
        }
        string fw = d.FirmwareVersion();
        Log(fw != null ? "ALL OK - " + n + " page changes with live traffic." : ">>> wheel not answering at the end");
        d.Close();
        return fw != null ? 0 : 5;
    }
}
