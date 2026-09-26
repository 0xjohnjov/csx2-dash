// Tests UGT's "turn off" sequence (UGTManager.cs L23840-23872): clear, LEDs off, LCD level 0 (0x61 0x00).
// Waits for a replug, licenses the wheel, blanks it for 10 s, checks it's still alive, then restores.
// Run with UGT Manager and SimHub CLOSED.
using System;
using System.IO;
using System.Threading;
using CSX2Dash;

class LcdOffTest
{
    static StreamWriter log;
    static void Log(string s) { string l = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s; Console.WriteLine(l); log.WriteLine(l); log.Flush(); }
    static bool Present() { var x = new Csx2Device(); bool ok = x.Open(); x.Close(); return ok; }

    static int Main()
    {
        log = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lcdofftest.log"), true);
        Log("==== LcdOffTest ====");
        bool now = Environment.GetCommandLineArgs().Length > 1 && Environment.GetCommandLineArgs()[1] == "--now";
        if (!now) {
        Log("Waiting for the wheel to be unplugged ...");
        while (Present()) Thread.Sleep(200);
        Log("Unplugged. Waiting for it to come back ...");
        while (!Present()) Thread.Sleep(100);
        Log("Plugged in.");
        }
        Thread.Sleep(1500);

        var d = new Csx2Device();
        for (int i = 0; i < 10 && !d.Open(); i++) Thread.Sleep(300);
        if (!d.IsOpen) { Log("open failed: " + d.LastError); return 1; }
        Log("fw: " + (d.FirmwareVersion() ?? "(no reply)"));
        int lic = d.LicenceHandshake();
        Log("licence: " + lic + "/15");
        if (lic < 15) return 2;

        Log("Blanking: clear screen, LEDs off, LCD level 0 ...");
        d.ClearScreen();
        d.SetLeds(new byte[5]);
        bool sent = d.LcdOff();
        Log("  sent: " + sent + " - screen should now be DARK (backlight off) with no LEDs, for 10 s");
        Thread.Sleep(5000);
        string fw = d.FirmwareVersion();
        Log("  alive while dark: " + (fw ?? "NO REPLY"));
        Thread.Sleep(5000);

        Log("Restoring: LCD 12, brand screen ...");
        d.LcdBrightness(12);
        d.BrandScreen();
        fw = d.FirmwareVersion();
        Log("  alive after restore: " + (fw ?? "NO REPLY"));
        d.Close();
        return fw != null ? 0 : 3;
    }
}
