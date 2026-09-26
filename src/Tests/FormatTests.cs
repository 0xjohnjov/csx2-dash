// Checks the ported UGT formatters against outputs documented in docs\UGT-widget-formatting.md.
using System;
using System.IO;
using System.Reflection;

static class FormatTests
{
    static int fails;

    static void Eq(string name, string got, string want)
    {
        bool ok = got == want;
        if (!ok) fails++;
        Console.WriteLine("{0} {1,-34} got [{2}] want [{3}]", ok ? "PASS" : "FAIL", name, got, want);
    }

    static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string sh = Environment.GetEnvironmentVariable("SIMHUB") ?? @"C:\Program Files (x86)\SimHub";
            string f = Path.Combine(sh, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(f) ? Assembly.LoadFrom(f) : null;
        };
        Run();
        Console.WriteLine(fails == 0 ? "\nALL PASS" : "\n" + fails + " FAILURE(S)");
        return fails;
    }

    static void Run()
    {
        // toClock
        Eq("lap 83.456, D8", CSX2Dash.Fields.ToClock(83.456, 8), "1:23.456");
        Eq("FP quirk 83.1, D8", CSX2Dash.Fields.ToClock(83.1, 8), "1:23.099");
        Eq("zero, D8", CSX2Dash.Fields.ToClock(0, 8), "   0.000");
        Eq("zero 1-dec, D6", CSX2Dash.Fields.ToClock(0, 6, false, false, 1), "   0.0");
        // Same FP-truncation quirk as UGT: 23.456 - 23 = 0.45599999..., floored to .455.
        Eq("under a minute (UGT FP quirk), D6", CSX2Dash.Fields.ToClock(23.456, 6), "23.455");
        Eq("under a minute, D6", CSX2Dash.Fields.ToClock(23.5, 6), "23.500");
        Eq("cut right D7", CSX2Dash.Fields.ToClock(83.456, 7), "1:23.45");
        Eq("cut right D6", CSX2Dash.Fields.ToClock(83.456, 6), "1:23.4");
        Eq("delta +0.123, D6", CSX2Dash.Fields.ToClock(0.123, 6, true), "+0.123");
        Eq("delta -1.234, D6", CSX2Dash.Fields.ToClock(-1.234, 6, true), "-1.234");
        Eq("current lap 1-dec D6", CSX2Dash.Fields.ToClock(83.46, 6, false, false, 1), "1:23.4");
        Eq("seconds padded after min", CSX2Dash.Fields.ToClock(65.123, 8), "1:05.123");
        // Number
        Eq("speed 87 zero-padded D3", CSX2Dash.Fields.Number(87, 3, 3, 0), "087");
        Eq("fuel 45.678 D5", CSX2Dash.Fields.Number(45.678, 5, 2, false), "45.68");
        Eq("fuel 45.678 D4", CSX2Dash.Fields.Number(45.678, 4, 2, false), "45.7");
        Eq("fuel 45.678 D3", CSX2Dash.Fields.Number(45.678, 3, 2, false), "46");
        Eq("fuel-laps +1.25 D5", CSX2Dash.Fields.Number(1.25, 5, 2), "+1.25");
        Eq("fuel-laps -0.5 D5", CSX2Dash.Fields.Number(-0.5, 5, 2), "-0.50");
        Eq("fuel-laps 0 D5", CSX2Dash.Fields.Number(0, 5, 2), "0.00");
        Eq("gear 3 D1", CSX2Dash.Fields.Number(3, 1, 0, 0), "3");
        Eq("temp 85 D3", CSX2Dash.Fields.Number(85, 3, 0, false), "85");
        Eq("laps gap +2.3L (D6-1)", CSX2Dash.Fields.Number(2.3, 5, 1, true) + "L", "+2.3L");
        // BBias (UGT L201477: Number(v, D, 1, prefix:false)); pop-up page 501 uses Digits 4
        Eq("brake bias 56 D4", CSX2Dash.Fields.Number(56.0, 4, 1, false), "56.0");
        Eq("brake bias 57.5 D4", CSX2Dash.Fields.Number(57.5, 4, 1, false), "57.5");
        Eq("brake bias 100 D4", CSX2Dash.Fields.Number(100.0, 4, 1, false), "100");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Eq("bias fraction -> percent", CSX2Dash.Fields.BiasToPercent(0.565).ToString("0.0", inv), "56.5");
        Eq("bias percent stays percent", CSX2Dash.Fields.BiasToPercent(56.5).ToString("0.0", inv), "56.5");
        Eq("bias NaN -> 0", CSX2Dash.Fields.BiasToPercent(double.NaN).ToString("0.0", inv), "0.0");
    }
}
