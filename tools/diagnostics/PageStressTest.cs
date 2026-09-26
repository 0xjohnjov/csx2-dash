// Stress-tests CSX2 Dash's page change (Csx2Device.ShowPage - the exact code SimHub runs):
// waits for a replug, runs the licence handshake, then flips through every UGT layout 3 times
// back to back, checking the wheel is alive after each page. Run with UGT Manager and SimHub CLOSED.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CSX2Dash;

class PageStressTest
{
    static StreamWriter log;
    static void Log(string s) { string l = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s; Console.WriteLine(l); log.WriteLine(l); log.Flush(); }
    static bool Present() { var x = new Csx2Device(); bool ok = x.Open(); x.Close(); return ok; }

    static int Main()
    {
        log = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pagestresstest.log"), true);
        Log("==== PageStressTest ====");
        var ugt = UgtFiles.Load();
        foreach (var l in ugt.Layouts) Log("  " + l + ", fonts " + string.Join(",", l.Fonts.Where(f => f > 2).Distinct()) + ", background " + (l.BackgroundSector.HasValue ? "yes" : "no"));

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

        var fontRefs = new Dictionary<int, int>();
        int n = 0;
        for (int round = 1; round <= 3; round++)
        {
            foreach (var l in ugt.Layouts)
            {
                n++;
                var t0 = DateTime.Now;
                d.ShowPage(l, fontRefs, w => Log("    warn: " + w));
                double ms = (DateTime.Now - t0).TotalMilliseconds;
                string fw = d.IsOpen ? d.FirmwareVersion() : null;
                if (fw == null) { Log(string.Format(">>> WHEEL DIED on flip {0} (round {1}, layout {2}) - {3}", n, round, l.Id, d.LastError)); return 3; }
                Log(string.Format("  flip {0,2}: round {1} layout {2} ok in {3:0} ms, {4} fonts loaded", n, round, l.Id, ms, fontRefs.Count));
            }
        }
        Log("PASSED: " + n + " page changes. Idle check for 60 s ...");
        for (int t = 0; t < 6; t++)
        {
            Thread.Sleep(10000);
            if (d.FirmwareVersion() == null) { Log(">>> wheel died while idle after the page test"); return 4; }
        }
        Log("ALL OK.");
        d.Close();
        return 0;
    }
}
