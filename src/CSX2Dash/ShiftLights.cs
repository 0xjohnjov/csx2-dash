// Rev / status LED mask, reproducing UGT Manager's ProcessLEDs (see docs\UGT-telemetry-protocol.md section 4).

using System;

namespace CSX2Dash
{
    public class LedInput
    {
        public double Rpm, MaxRpm;
        public bool Neutral, PitLimiter;
        public bool FlagYellow, FlagBlue, FlagRed;
    }

    public class ShiftLights
    {
        // {byte, mask} for Rev_1..Rev_15 (Rev_1 lights first).
        static readonly int[,] Rev = { {3,0x20},{3,0x40},{3,0x80},{3,0x01},{3,0x02},{2,0x04},{2,0x02},{2,0x01},
                                       {0,0x40},{0,0x80},{0,0x08},{0,0x04},{0,0x02},{0,0x01},{0,0x10} };
        static readonly int[] YellowL = { 3, 0x10 }, RedL = { 3, 0x04 }, BlueL = { 3, 0x08 };
        static readonly int[] YellowR = { 4, 0x20 }, RedR = { 4, 0x01 }, BlueR = { 4, 0x02 };

        public int[] Table;                // 16 per-mille values from the user's UGT settings
        public string LimiterMode = "Alternate_Blinking";
        public bool ShiftFlash;            // UGT rewrote the user's OSP setting to Off, so default false
        public bool FlagLeds = true;

        const long SlowBlinkMs = 500, FlashMs = 60, FlagBlinkMs = 250;

        public byte[] Compute(LedInput d, long nowMs)
        {
            var m = new byte[5];
            if (d == null || d.MaxRpm <= 0 || Table == null) return m;

            bool slow = (nowMs / SlowBlinkMs) % 2 == 0;
            if (d.PitLimiter && LimiterMode != "Off")
            {
                for (int i = 0; i < 15; i++)
                {
                    bool on = LimiterMode == "All_Blinking" ? slow : ((i % 2 == 0) == slow);
                    if (on) Set(m, Rev[i, 0], Rev[i, 1]);
                }
            }
            else
            {
                for (int i = 0; i < 15; i++)
                    if (d.Rpm > Threshold(d.MaxRpm, Table[i])) Set(m, Rev[i, 0], Rev[i, 1]);

                // Over-shift point: UGT forces the last five rev LEDs (Rev_11..15) off on alternate flash ticks.
                if (ShiftFlash && d.Rpm > Threshold(d.MaxRpm, Table[15]) && (nowMs / FlashMs) % 2 == 0)
                    for (int i = 10; i < 15; i++) Clear(m, Rev[i, 0], Rev[i, 1]);
            }

            if (FlagLeds)
            {
                bool blink = (nowMs / FlagBlinkMs) % 2 == 0;
                if (d.FlagYellow && blink) { Set(m, YellowL[0], YellowL[1]); Set(m, YellowR[0], YellowR[1]); }
                if (d.FlagBlue && blink) { Set(m, BlueL[0], BlueL[1]); Set(m, BlueR[0], BlueR[1]); }
                if (d.FlagRed) { Set(m, RedL[0], RedL[1]); Set(m, RedR[0], RedR[1]); }
            }
            return m;
        }

        static double Threshold(double maxRpm, int perMille) { return (short)Math.Round(maxRpm * perMille / 1000.0); }
        static void Set(byte[] m, int b, int mask) { m[b] |= (byte)mask; }
        static void Clear(byte[] m, int b, int mask) { m[b] &= (byte)~mask; }
    }
}
