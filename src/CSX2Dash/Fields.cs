// Widget text + colour for every UGT layout key, reproducing UGT Manager 1.6.819's formatting for Assetto Corsa
// (docs\UGT-widget-formatting.md). Keys UGT left as "NA" on AC are filled from SimHub where SimHub has the data.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GameReaderCommon;
using SimHub.Plugins;

namespace CSX2Dash
{
    public class Fields
    {
        public bool Metric = true;
        public PluginManager PM;
        public int SubInfo;            // 0 Best / 1 Last / 2 Curr   (Extra_Info_Cycle)
        public int SubPage;            // opponent table page         (Sub_Page_Cycle)

        // UGT palette (RGB565)
        const ushort Neutral = 0xFF35, Best = 0x0F00, PBest = 0x3ABF, SBest = 0x814F, Inv = 0xFBA0, Plus = 0xFF00, Plus05 = 0xF900;
        const ushort TLow = 0x03D5, TNorm = 0x0F00, THigh = 0xF900;
        const double PlusVal = 0.5;

        // ---- per-frame state (written by DataUpdate thread, read by worker)
        volatile bool isRace;
        double oldBest, oldPb, lastSeenLast;
        long blinkLapStart = -1, blinkPbStart = -1;
        double blinkLapDiff, blinkPbDiff;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        long scrollBase;
        string trackKey;
        readonly double[] pbSector = new double[3];
        readonly PbStore pbStore = new PbStore();

        // ------------------------------------------------------------ frame hook (SimHub thread)

        public void OnFrame(GameData g)
        {
            var d = g.NewData;
            if (d == null) return;
            isRace = SessionWord(d.SessionTypeName) == "Race";

            string key = (d.CarId ?? d.CarModel) + "|" + (d.TrackIdWithConfig ?? d.TrackName) + "|" + SessionWord(d.SessionTypeName);
            if (key != trackKey) { trackKey = key; pbStore.Load(key, pbSector); oldBest = 0; oldPb = 0; lastSeenLast = 0; }

            double last = d.LastLapTime.TotalSeconds;
            if (last > 0 && last != lastSeenLast)
            {
                lastSeenLast = last;
                // New best -> UGT blinks the diff to the previous best (full lap time if there was none).
                if (!d.LapInvalidated && (oldBest <= 0 || last <= oldBest)) { blinkLapDiff = last - oldBest; blinkLapStart = clock.ElapsedMilliseconds; oldBest = last; }
                double pb = d.AllTimeBest.TotalSeconds;
                if (pb > 0 && Math.Abs(pb - last) < 0.0005 && (oldPb <= 0 || last < oldPb)) { blinkPbDiff = last - oldPb; blinkPbStart = clock.ElapsedMilliseconds; }
                if (pb > 0) oldPb = pb;
                // Personal-best sectors (SimHub has no property for these): keep our own per car/track/session.
                var s = new[] { d.Sector1LastLapTime, d.Sector2LastLapTime, d.Sector3LastLapTime };
                bool changed = false;
                for (int i = 0; i < 3; i++)
                    if (s[i].HasValue && s[i].Value.TotalSeconds > 0 && (pbSector[i] <= 0 || s[i].Value.TotalSeconds < pbSector[i]))
                    { pbSector[i] = s[i].Value.TotalSeconds; changed = true; }
                if (changed) pbStore.Save(key, pbSector);
            }
            if (oldBest <= 0 && d.BestLapTime.TotalSeconds > 0) oldBest = d.BestLapTime.TotalSeconds;
        }

        public void ResetScroll() { scrollBase = clock.ElapsedMilliseconds + 4000; }

        int ScrollNum()
        {
            long t = clock.ElapsedMilliseconds - scrollBase;
            return t < 0 ? 0 : (int)((t / 150) % 81);
        }

        public LedInput LedInput(StatusDataBase d)
        {
            return new LedInput
            {
                Rpm = d.Rpms, MaxRpm = d.MaxRpm > 0 ? d.MaxRpm : d.CarSettings_MaxRPM,
                PitLimiter = d.PitLimiterOn != 0, Neutral = d.Gear == "N",
                FlagYellow = d.Flag_Yellow != 0, FlagBlue = d.Flag_Blue != 0, FlagRed = d.Flag_Black != 0
            };
        }

        // ------------------------------------------------------------ per widget (worker thread)

        public string Format(Widget w, StatusDataBase d, out ushort colour)
        {
            colour = w.Colour;
            int D = w.Digits;
            string k = w.Key ?? "";
            try
            {
                if (k.StartsWith("D_")) return Opponent(w, d, ref colour);
                bool invalid = d.LapInvalidated || d.IsInPitLane != 0;
                double dBest = d.DeltaToSessionBest ?? 0, dPb = d.DeltaToAllTimeBest ?? 0;
                int sector = Math.Max(1, d.CurrentSectorIndex);
                int sectors = d.SectorsCount ?? 3;

                switch (k)
                {
                    case "SPD": return Align(Number((double)Convert.ToInt32(Metric ? d.SpeedKmh : d.SpeedMph), D, D, 0), D);
                    case "RPM": return Align(Number((double)(int)d.Rpms, D, D, 0), D);
                    case "Gear":
                        {
                            int g;
                            if (d.Gear == "N" || string.IsNullOrEmpty(d.Gear)) return Align("N", D);
                            if (d.Gear == "R") return Align("R", D);
                            return int.TryParse(d.Gear, out g) ? Align(Number((double)g, D, 0, 0), D) : Align(d.Gear, D);
                        }
                    case "DRS": colour = d.DRSEnabled != 0 ? Best : Neutral; return Align("DRS", D);
                    case "Pos": return Align("P" + d.Position, D, "right", false);
                    case "Sect": return Align("S" + sector, D, "right", false);
                    case "Laps": return Align(d.CurrentLap + "/" + (d.TotalLaps > 0 ? d.TotalLaps.ToString() : "--"), D, "center");
                    case "Damage":
                        {
                            // SimHub reports damage as a percentage; UGT used AC's raw scale (0 / <=1 / >1).
                            double m = d.CarDamagesMax;
                            return Align(m <= 0 ? "None" : m <= 25 ? "Minor" : "Major", D);
                        }
                    case "Kers":
                        {
                            if (d.ERSMax <= 0) return Align("NA", D);
                            double pct = d.ERSPercent;
                            colour = Three(pct / 100.0, 0.2, 0.7, Plus05, Plus, Best);
                            return Align(Number(pct, D, D, 0), D);
                        }
                    case "Fuel":
                        if (d.Fuel < 5.0) colour = 0xF900;
                        return Align(Number(d.Fuel, D, 2, false), D);
                    case "FlLpsDelt": return Align(Number(FuelLapsDelta(d), D, 2), D);
                    case "FLTyretemp": return Temp(d.TyreTemperatureFrontLeftMiddle, 70, 100, D, ref colour);
                    case "FRTyretemp": return Temp(d.TyreTemperatureFrontRightMiddle, 70, 100, D, ref colour);
                    case "RLTyretemp": return Temp(d.TyreTemperatureRearLeftMiddle, 70, 100, D, ref colour);
                    case "RRTyretemp": return Temp(d.TyreTemperatureRearRightMiddle, 70, 100, D, ref colour);
                    case "FLBrakeTemp": return Temp(d.BrakeTemperatureFrontLeft, 300, 700, D, ref colour);
                    case "FRBrakeTemp": return Temp(d.BrakeTemperatureFrontRight, 300, 700, D, ref colour);
                    case "RLBrakeTemp": return Temp(d.BrakeTemperatureRearLeft, 300, 700, D, ref colour);
                    case "RRBrakeTemp": return Temp(d.BrakeTemperatureRearRight, 300, 700, D, ref colour);
                    case "WaterTemp": colour = Three(d.WaterTemperature, 90, 110, TLow, TNorm, THigh); return Align(Number(TempUnit(d.WaterTemperature), D, 1, false), D);
                    case "OilTemp": colour = Three(d.OilTemperature, 95, 120, TLow, TNorm, THigh); return Align(Number(TempUnit(d.OilTemperature), D, 1, false), D);

                    // ---- laps
                    case "CurLpt":
                        colour = CurrTimeColour(dBest, dPb, invalid, false);
                        return Align(ToClock(d.CurrentLapTime.TotalSeconds, D, false, false, 1), D);
                    case "LastLpt":
                        {
                            string b = Blink(blinkLapStart, blinkLapDiff, D); if (b != null) return b;
                            double t = d.LastLapTime.TotalSeconds;
                            colour = TimeColour(t, d.BestLapTime.TotalSeconds, d.AllTimeBest.TotalSeconds, false, 0);
                            return Align(ToClock(t, D), D);
                        }
                    case "BestLpt":
                        {
                            string b = Blink(blinkLapStart, blinkLapDiff, D); if (b != null) return b;
                            colour = Best; return Align(ToClock(d.BestLapTime.TotalSeconds, D), D);
                        }
                    case "PBlpt":
                        {
                            string b = Blink(blinkPbStart, blinkPbDiff, D); if (b != null) return b;
                            colour = PBest; return Align(ToClock(d.AllTimeBest.TotalSeconds, D), D);
                        }
                    case "SBlpt": colour = SBest; return Align(ToClock(SessionBestLap(d), D), D);
                    case "DffBest": colour = CurrTimeColour(dBest, dPb, invalid, true); return Align(ToClock(dBest, D, true), D);
                    case "DffPB": colour = CurrTimeColourPb(dPb, invalid); return Align(ToClock(dPb, D, true), D);
                    case "PitDelt": return Align(ToClock(d.LastPitStopDuration, D), D);

                    // ---- session strings
                    case "Session": return Align(SessionWord(d.SessionTypeName), D, "scroll");
                    case "Track": return Align(d.TrackName ?? "", D, "scroll");
                    case "Car": return Align(d.CarModel ?? "", D, "scroll");
                    case "CarClass": return Align(d.CarClass ?? "", D, "scroll");
                    case "InfoState": return Align(SubInfo == 0 ? "Best" : SubInfo == 1 ? "Last" : "Curr", D, "scroll");
                }

                // ---- sectors: CurSn / LastSn / BestSn / PBSn / SBSn
                int n = k.Length > 0 ? k[k.Length - 1] - '0' : 0;
                string stem = k.Length > 1 ? k.Substring(0, k.Length - 1) : k;
                if (n >= 1 && n <= 4 && (stem == "CurS" || stem == "LastS" || stem == "BestS" || stem == "PBS" || stem == "SBS"))
                {
                    if (n > sectors || n == 4) return Align("NA", D);
                    double bestN = Sec(n == 1 ? d.Sector1BestTime : n == 2 ? d.Sector2BestTime : d.Sector3BestTime);
                    double pbN = pbSector[n - 1];
                    double sbN = SessionBestSector(d, n, bestN);
                    switch (stem)
                    {
                        case "CurS":
                            {
                                double s1 = Sec(d.Sector1Time), s2 = Sec(d.Sector2Time), cur = d.CurrentLapTime.TotalSeconds;
                                if (sector == n)
                                {
                                    double run = n == 1 ? cur : n == 2 ? (s1 > 0 ? cur - s1 : 0) : (s1 > 0 && s2 > 0 ? cur - s1 - s2 : 0);
                                    colour = invalid ? Inv : Neutral;
                                    return Align(ToClock(run, D, false, false, 1), D);
                                }
                                double done = sector > n ? (n == 1 ? s1 : n == 2 ? s2 : 0) : 0;
                                colour = TimeColour(done, bestN, pbN, false, sbN);
                                return Align(ToClock(done, D), D);
                            }
                        case "LastS":
                            {
                                string b = Blink(blinkLapStart, 0, D, true); if (b != null) return b;
                                double t = Sec(n == 1 ? d.Sector1LastLapTime : n == 2 ? d.Sector2LastLapTime : d.Sector3LastLapTime);
                                colour = TimeColour(t, bestN, pbN, false, sbN);
                                return Align(ToClock(t, D), D);
                            }
                        case "BestS": colour = Best; return Align(ToClock(bestN, D), D);
                        case "PBS": colour = PBest; return Align(ToClock(pbN, D), D);
                        case "SBS": colour = SBest; return Align(ToClock(sbN, D), D);
                    }
                }
                return Align("???", D);
            }
            catch { return Align("", D); }
        }

        // ------------------------------------------------------------ opponent table (layouts with D_n_* keys)

        string Opponent(Widget w, StatusDataBase d, ref ushort colour)
        {
            int D = w.Digits;
            var parts = w.Key.Split('_');                 // D, n, Field
            int n; if (parts.Length < 3 || !int.TryParse(parts[1], out n)) return Align("???", D);
            string field = parts[2];
            int pos = n + 1 + SubPage * 8;
            var list = d.Opponents;
            var o = list == null ? null : list.FirstOrDefault(x => x.Position == pos);
            if (o == null) return Align("", D);
            bool me = o.IsPlayer;
            double myPb = d.AllTimeBest.TotalSeconds, sb = SessionBestLap(d);
            switch (field)
            {
                case "Pos": return Align(Number((double)pos, D, D, 0), D);
                case "Name": colour = me ? PBest : Neutral; return Align(o.Name ?? "", D, "scroll");
                case "Lap":
                    {
                        double best = o.BestLapTime.TotalSeconds, last = o.LastLapTime.TotalSeconds;
                        if (SubInfo == 0) { colour = TimeColour(best, best, myPb, false, sb, !me); return Align(ToClock(best, D), D); }
                        if (SubInfo == 1) { colour = TimeColour(last, best, myPb, false, sb, !me); return Align(ToClock(last, D), D); }
                        colour = Neutral;
                        return Align(ToClock(o.CurrentLapTime.HasValue ? o.CurrentLapTime.Value.TotalSeconds : 0, D, false, false, 1), D);
                    }
                case "S1": case "S2": case "S3":
                    {
                        int s = field[1] - '0';
                        double v = SubInfo == 0 ? (o.BestSectorSplits == null ? 0 : Sec(o.BestSectorSplits.GetSectorSplit(s)))
                                                : (o.LastLapSectorTimes == null ? 0 : Sec(o.LastLapSectorTimes.GetSectorSplit(s)));
                        colour = Neutral;
                        return Align(ToClock(v, D), D);
                    }
                case "info1":
                    {
                        if (SubInfo == 2) { colour = Neutral; return Align(o.CarName ?? "", D, "scroll"); }
                        double gap = (SubInfo == 0 ? o.GaptoLeader : o.GaptoPlayer) ?? 0;
                        double laps = (SubInfo == 0 ? o.LapsToLeader : o.LapsToPlayer) ?? 0;
                        colour = GapColour(laps, gap);
                        string v = Math.Abs(laps) >= 1 ? Number(laps, D - 1, 1, true) + "L" : ToClock(gap, D, true);
                        return Align(v, D);
                    }
                case "info2":
                    {
                        if (o.DidNotFinish == true) { colour = Plus05; return Align("DNF", D, "scroll"); }
                        if (SubInfo == 0) { bool pit = o.IsCarInPit || o.IsCarInPitLane; colour = pit ? Inv : Neutral; return Align(pit ? "Pit" : "", D, "scroll"); }
                        colour = Neutral;
                        if (SubInfo == 1) return Align((o.PitCount ?? 0) + "ST", D, "scroll");
                        return Align("", D, "scroll");                              // penalties: not available for AC
                    }
            }
            return Align("???", D);
        }

        // ------------------------------------------------------------ helpers

        static double Sec(TimeSpan? t) { return t.HasValue ? t.Value.TotalSeconds : 0; }

        static string SessionWord(string s)
        {
            s = s ?? "";
            string l = s.ToLowerInvariant();
            if (l.Contains("practice")) return "Practice";
            if (l.Contains("qual")) return "Qualy";
            if (l.Contains("race")) return "Race";
            if (l.Contains("hotlap")) return "Hotlap";
            if (l.Contains("time")) return "TimeAttack";
            if (l.Contains("drift")) return "Drift";
            if (l.Contains("drag")) return "Drag";
            return s.Length > 0 ? s : "Unknown";
        }

        double SessionBestLap(StatusDataBase d)
        {
            double best = d.BestLapTime.TotalSeconds;
            if (d.Opponents != null)
                foreach (var o in d.Opponents)
                {
                    double t = o.BestLapTime.TotalSeconds;
                    if (t > 0 && (best <= 0 || t < best)) best = t;
                }
            return best;
        }

        static double SessionBestSector(StatusDataBase d, int n, double own)
        {
            double best = own;
            if (d.Opponents != null)
                foreach (var o in d.Opponents)
                {
                    double t = o.BestSectorSplits == null ? 0 : Sec(o.BestSectorSplits.GetSectorSplit(n));
                    if (t > 0 && (best <= 0 || t < best)) best = t;
                }
            return best;
        }

        double FuelLapsDelta(StatusDataBase d)
        {
            double avg = 0;
            try { avg = Convert.ToDouble(PM.GetPropertyValue("DataCorePlugin.Computed.Fuel_LitersPerLap") ?? 0.0, CultureInfo.InvariantCulture); } catch { }
            if (avg <= 0) return 0;
            double frac = d.TrackPositionPercent;
            if (frac > 1) frac /= 100.0;
            double remaining = isRace && d.TotalLaps > 0 ? d.TotalLaps - (d.CompletedLaps + frac) : 1 - frac;
            return (d.Fuel - 1) / avg - remaining;
        }

        double TempUnit(double c) { return Metric ? c : c * 1.8 + 32; }

        string Temp(double v, double low, double high, int D, ref ushort colour)
        {
            colour = Three(v, low, high, TLow, TNorm, THigh);
            return Align(Number((double)(int)TempUnit(v), D, 0, false), D);
        }

        static ushort Three(double v, double low, double high, ushort cLow, ushort cNorm, ushort cHigh)
        {
            return v < low ? cLow : v > high ? cHigh : cNorm;
        }

        static ushort TimeColour(double time, double best, double pb, bool invalid, double sb, bool ignorePb = false)
        {
            if (invalid) return Inv;
            if (time > 0)
            {
                if (time <= sb && sb > 0) return SBest;
                if (time <= pb && pb > 0 && !ignorePb) return PBest;
                if (best > 0) return time > best + PlusVal ? Plus05 : !(time <= best) ? Plus : Best;
            }
            return Neutral;
        }

        static ushort CurrTimeColour(double dBest, double dPb, bool invalid, bool ignorePb)
        {
            if (invalid) return Inv;
            if (dPb < 0 && !ignorePb) return PBest;
            if (dBest > PlusVal) return Plus05;
            if (dBest < 0) return Best;
            if (dBest > 0) return Plus;
            return Neutral;
        }

        static ushort CurrTimeColourPb(double dPb, bool invalid)
        {
            if (invalid) return Inv;
            if (dPb > PlusVal) return Plus05;
            if (dPb < 0) return Best;
            if (dPb > 0) return Plus;
            return Neutral;
        }

        ushort GapColour(double laps, double gap)
        {
            double plus = isRace ? 5 : 0.5, plus30 = isRace ? 30 : 5;
            if (laps > 1 || laps < -1) return Plus05;
            if (gap > plus30 || gap < -plus30) return Plus05;
            if (gap > plus || gap < -plus) return Plus;
            if (gap != 0) return Best;
            return Neutral;
        }

        // New-best blink: blank 270 ms, diff 810 ms, blank 270 ms, then two more diff/blank cycles (~3.5 s).
        string Blink(long start, double diff, int D, bool blankOnly = false)
        {
            if (start < 0) return null;
            long t = clock.ElapsedMilliseconds - start;
            if (t >= 3510) return null;
            long[] edges = { 270, 1080, 1350, 2160, 2430, 3240, 3510 };
            int seg = 0; while (seg < edges.Length && t >= edges[seg]) seg++;
            bool showDiff = seg == 1 || seg == 3 || seg == 5;
            if (blankOnly) return null;       // sector blink needs per-sector diffs; keep the value instead
            return Align(showDiff ? ToClock(diff, D) : "", D);
        }

        // ------------------------------------------------------------ UGT Transform, ported 1:1

        public static string ToClock(double time, int Totaldigits, bool prefix = false, bool leadZero = false, int trailDigits = 3)
        {
            bool flag = true;
            string text = "0";
            string text2;
            if (leadZero) text = "00";
            switch (trailDigits) { case 0: text2 = ""; break; case 1: text2 = ".0"; break; case 2: text2 = ".00"; break; default: text2 = ".000"; break; }
            if (text.Length + 2 > Totaldigits) text2 = "";
            if (double.IsInfinity(time) || double.IsNaN(time)) time = 0.0;
            if (time != 0.0)
            {
                string text3 = "";
                if (time < 0.0) text3 = "-";
                else if (prefix) text3 = "+";
                double num = Math.Abs(time);
                if (num > 0.0)
                {
                    int num2 = Convert.ToInt16(Math.Floor(num / 86400.0));
                    double num3 = num - (double)(num2 * 86400);
                    int num4 = Convert.ToInt16(Math.Floor(num3 / 3600.0));
                    num3 -= (double)(num4 * 3600);
                    int num5 = Convert.ToInt16(Math.Floor(num3 / 60.0));
                    num3 -= (double)(num5 * 60);
                    int num6 = Convert.ToInt16(Math.Floor(num3));
                    num3 -= (double)num6;
                    string text4 = "";
                    if (num2 > 0) text4 = num2 + "d";
                    text = text3 + text4;
                    if (text.Length + 2 > Totaldigits) flag = false;
                    if ((num4 > 0 || num2 > 0) && flag)
                    {
                        text = ((!(num2 > 0 || leadZero) || num4 >= 10) ? (text + num4) : (text + "0" + num4));
                        if (text.Length + 1 < Totaldigits) text += "h";
                        else { text += "h"; flag = false; }
                    }
                    if (num5 > 0 || num2 > 0 || (num4 > 0 && flag))
                    {
                        text = ((!(num2 > 0 || num4 > 0 || leadZero) || num5 >= 10) ? (text + num5) : (text + "0" + num5));
                        if (text.Length + 1 < Totaldigits) text += ":";
                        else { text += "m"; flag = false; }
                    }
                    if (flag)
                        text = ((!(num2 > 0 || num4 > 0 || num5 > 0 || leadZero) || num6 >= 10) ? (text + num6) : (text + "0" + num6));
                    if (text.Length + 1 < Totaldigits && trailDigits > 0)
                    {
                        switch (trailDigits)
                        {
                            case 1: text2 = "." + (int)Convert.ToInt16(Math.Floor(num3 * 10.0)); break;
                            case 2: { int num7 = Convert.ToInt16(Math.Floor(num3 * 100.0)); text2 = ((num7 >= 10) ? ("." + num7) : (".0" + num7)); break; }
                            default: { int num7 = Convert.ToInt16(Math.Floor(num3 * 1000.0)); text2 = ((num7 >= 10) ? ((num7 >= 100) ? ("." + num7) : (".0" + num7)) : (".00" + num7)); break; }
                        }
                    }
                    else text2 = "";
                }
            }
            string text5 = text + text2;
            int num8 = Totaldigits - text5.Length;
            for (int i = 0; i < num8; i++) text5 = " " + text5;
            if (num8 < 0) text5 = text5.Substring(0, text5.Length + num8);
            return text5;
        }

        public static string Number(double value, int Totaldigits, int trailDigits = 0, bool prefix = true)
        {
            return Number(value, Totaldigits, 0, trailDigits, prefix);
        }

        public static string Number(double value, int Totaldigits, int LeadDigitsZero, int trailDigits, bool prefix, bool TrimRight = true)
        {
            if (double.IsInfinity(value) || double.IsNaN(value)) value = 0.0;
            string text = "0";
            if (LeadDigitsZero > 1) text = ((!(value < 0.0 || prefix)) ? new string('0', LeadDigitsZero) : new string('0', LeadDigitsZero - 1));
            if (value > 0.0 && prefix) text = "+" + text;
            string text2 = value.ToString(text);
            if (text2.Length >= Totaldigits)
            {
                if (TrimRight && text2.Length > Totaldigits) return text2.Substring(0, Totaldigits);
                return text2;
            }
            if (trailDigits <= 0) return text2;
            int num = Totaldigits - (text2.Length + 1);
            if (num <= 0) return text2;
            if (trailDigits > num) trailDigits = num;
            text = text + "." + new string('0', trailDigits);
            return value.ToString(text, CultureInfo.InvariantCulture);
        }

        // Overload used by UGT for Number(v, D, D, 0) / Number(v, D, 0, 0): LeadDigitsZero, trail, prefix=false.
        public static string Number(double value, int Totaldigits, int LeadDigitsZero, int trailDigits)
        {
            return Number(value, Totaldigits, LeadDigitsZero, trailDigits, false);
        }

        string Align(string line, int TotalLength, string align = "right", bool cutright = true)
        {
            string text = "", text2 = "", result = line;
            int num = line == null ? 0 : line.Length;
            int num2 = TotalLength - num;
            if (num2 > 0)
            {
                switch (align)
                {
                    case "left": text = new string(' ', num2); break;
                    case "center":
                    case "scroll":
                        {
                            int num3 = num2 / 2;
                            text2 = ((num2 % 2 == 0) ? new string(' ', num3) : new string(' ', num3 + 1));
                            text = new string(' ', num3);
                            break;
                        }
                    default: text2 = new string(' ', num2); break;
                }
                result = text2 + line + text;
            }
            else if (num2 < 0)
            {
                if (align != "scroll")
                    result = ((!cutright) ? line.Substring(Math.Abs(num2)) : line.Substring(0, num + num2));
                else
                {
                    int num4 = ScrollNum();
                    if (line.Length < num4 + num)
                    {
                        line = line + "      " + line;
                        if (line.Length < num4 + num) num4 = 0;
                    }
                    result = line.Substring(num4, TotalLength);
                }
            }
            return result;
        }
    }

    // Personal-best sector times per car|track|session (SimHub has no property for these).
    class PbStore
    {
        readonly string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CSX2Dash", "pb_sectors.txt");
        readonly Dictionary<string, double[]> all = new Dictionary<string, double[]>();
        bool loaded;

        void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!File.Exists(file)) return;
                foreach (var line in File.ReadAllLines(file))
                {
                    var p = line.Split('\t');
                    if (p.Length == 4)
                        all[p[0]] = new[] { double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture), double.Parse(p[3], CultureInfo.InvariantCulture) };
                }
            }
            catch { }
        }

        public void Load(string key, double[] into)
        {
            EnsureLoaded();
            double[] v;
            for (int i = 0; i < 3; i++) into[i] = all.TryGetValue(key, out v) ? v[i] : 0;
        }

        public void Save(string key, double[] v)
        {
            EnsureLoaded();
            all[key] = (double[])v.Clone();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllLines(file, all.Select(kv => kv.Key + "\t" + string.Join("\t", kv.Value.Select(x => x.ToString("R", CultureInfo.InvariantCulture)))));
            }
            catch { }
        }
    }
}
