// Tests the "licence watchdog" theory: the CSX2 locks up 30-60 s after power-up unless the host runs
// UGT Manager's device-licence challenge (0x90, UGTManager.cs L21066). Waits for the wheel to be
// (re)plugged, runs the challenge immediately, then checks the wheel is alive every 10 s for 3 minutes.
// Run with UGT Manager and SimHub CLOSED. Output also goes to licencetest.log next to the exe.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CSX2Dash;

class LicenceTest
{
    const string Key = "6e7151d9-c5d9-439d-b81e-2bea345c3d55";
    static StreamWriter log;
    static void Log(string s) { string l = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s; Console.WriteLine(l); log.WriteLine(l); log.Flush(); }

    static bool Present() { var x = new Csx2Device(); bool ok = x.Open(); x.Close(); return ok; }

    static int Main(string[] args)
    {
        log = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "licencetest.log"), true);
        bool skipLicence = args.Length > 0 && args[0] == "--no-licence";
        Log("==== LicenceTest" + (skipLicence ? " (CONTROL: no licence handshake)" : "") + " ====");

        Log("Waiting for the wheel to be unplugged ...");
        while (Present()) Thread.Sleep(200);
        Log("Unplugged. Waiting for it to come back ...");
        while (!Present()) Thread.Sleep(100);
        var sincePlug = Stopwatch.StartNew();
        Log("Plugged in.");
        Thread.Sleep(1500);   // let the firmware finish enumerating

        var d = new Csx2Device();
        for (int i = 0; i < 10 && !d.Open(); i++) Thread.Sleep(300);
        if (!d.IsOpen) { Log("open failed: " + d.LastError); return 1; }
        Log("fw: " + (d.FirmwareVersion() ?? "(no reply)") + " at " + sincePlug.Elapsed.TotalSeconds.ToString("0.0") + " s");

        if (!skipLicence)
        {
            var rnd = new Random();
            int pass = 0;
            for (int k = 0; k < 15; k++)
            {
                var p = new byte[63];
                rnd.NextBytes(p);
                p[0] = 0x90;
                int idx = rnd.Next(1, 35);
                p[4] = (byte)idx;
                byte[] r = d.Request(p);
                if (r == null) { Log("  round " + k + ": no reply (" + d.LastError + ")"); break; }
                bool ok = r[8] == (byte)Key[idx];
                Log(string.Format("  round {0}: idx {1} expect '{2}' got 0x{3:X2} -> {4}", k, idx, Key[idx], r[8], ok ? "ok" : "MISMATCH"));
                if (!ok) break;
                pass++;
            }
            Log("Licence handshake: " + pass + "/15 rounds passed, at " + sincePlug.Elapsed.TotalSeconds.ToString("0.0") + " s after plug-in");
        }

        for (int t = 10; t <= 180; t += 10)
        {
            while (sincePlug.Elapsed.TotalSeconds < t) Thread.Sleep(100);
            string fw = d.IsOpen ? d.FirmwareVersion() : null;
            if (fw == null) { Log(">>> WHEEL DIED between " + (t - 10) + " and " + t + " s after plug-in (" + d.LastError + ")"); return 3; }
            Log("  " + t + " s: alive");
        }
        Log("SURVIVED 3 minutes.");
        d.Close();
        return 0;
    }
}

