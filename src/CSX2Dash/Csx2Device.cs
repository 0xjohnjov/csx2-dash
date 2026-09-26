// HID link to the CSX2's vendor command channel (col02, report ID 2, 64-byte reports).
// All I/O is overlapped with timeouts so a locked-up wheel can never hang SimHub.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CSX2Dash
{
    public sealed class Csx2Device : IDisposable
    {
        const string InterfaceFilter = "vid_04d8&pid_f4c3&col02";

        // Timings: UGT Manager's "Resistant" delay profile (ms).
        public const int DataItemDelay = 15, ClearScreenDelay = 20, DisplayImageDelay = 35, BrightnessDelay = 30;
        public const int PageChangeDelay = 200;
        const int PadChangePre = 15, PadChangePost = 35, LoadFontPre = 80, LoadFontPost = 20;
        const int UnloadFontPre = 60, UnloadFontPost = 20, FontErrorDelay = 150;

        SafeFileHandle handle;
        FileStream stream;
        public bool IsOpen { get { return stream != null; } }
        public string LastError { get; private set; }

        public bool Open()
        {
            Close();
            string path = Native.FindHidPath(InterfaceFilter);
            if (path == null) { LastError = "CSX2 not connected"; return false; }
            handle = Native.CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            if (handle.IsInvalid) { LastError = "cannot open CSX2 (error " + Marshal.GetLastWin32Error() + ")"; handle = null; return false; }
            stream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
            LastError = null;
            return true;
        }

        public void Close()
        {
            try { if (stream != null) stream.Dispose(); } catch { }
            stream = null; handle = null;
        }

        public void Dispose() { Close(); }

        /// <summary>Payload of up to 63 bytes (byte 0 = command). Returns false and closes the link on failure.</summary>
        public bool Send(byte[] payload, int delayAfterMs = DataItemDelay)
        {
            if (stream == null) return false;
            var report = new byte[64];
            report[0] = 2;
            Array.Copy(payload, 0, report, 1, Math.Min(63, payload.Length));
            Record("> " + Describe(payload));
            try
            {
                IAsyncResult w = stream.BeginWrite(report, 0, 64, null, null);
                if (!w.AsyncWaitHandle.WaitOne(1000)) { Native.CancelIoEx(handle, IntPtr.Zero); Fail("write timed out - wheel not accepting data"); return false; }
                stream.EndWrite(w);
            }
            catch (Exception e) { Fail("write failed: " + e.Message); return false; }
            if (delayAfterMs > 0) Thread.Sleep(delayAfterMs);
            return true;
        }

        /// <summary>Pad data (button/paddle reports) off or on, with UGT's settle delays.</summary>
        public bool PadData(bool on)
        {
            if (on) return Send(Filled(0xA1), PadChangePost);                   // Enable_Pad_Data
            Thread.Sleep(PadChangePre);
            return Send(Filled(0xA0), PadChangePost);                           // Disable_Pad_Data
        }

        /// <summary>Request/response exchange, wrapped in pad-data off/on exactly like UGT's SendData.</summary>
        public byte[] Request(byte[] payload, int timeoutMs = 2000)
        {
            if (!PadData(false)) return null;
            byte[] reply = RequestRaw(payload, timeoutMs);
            PadData(true);
            return reply;
        }

        /// <summary>Request/response without the pad-data toggle; the caller has already turned pad data off.</summary>
        public byte[] RequestRaw(byte[] payload, int timeoutMs = 2000)
        {
            if (stream == null) return null;
            byte[] reply = null;
            try
            {
                Native.HidD_FlushQueue(handle);
                // Read buffer must be at least the input report length (report ID + 64), as in CSX2Recovery.
                var buf = new byte[65];
                IAsyncResult r = stream.BeginRead(buf, 0, 65, null, null);
                if (Send(payload, 0))
                {
                    if (r.AsyncWaitHandle.WaitOne(timeoutMs)) { stream.EndRead(r); reply = buf; Record("< " + BitConverter.ToString(buf, 0, 20)); }
                    else { Native.CancelIoEx(handle, IntPtr.Zero); Record("< NO REPLY within " + timeoutMs + " ms"); }
                }
            }
            catch (Exception e) { LastError = "request failed: " + e.Message; }
            return reply;
        }

        void Fail(string why) { LastError = why; Record("FAIL " + why); Close(); }

        // ------------------------------------------------------------ flight recorder
        // The last packets sent/received, dumped to a file when the wheel stops responding.

        readonly Queue<string> recorder = new Queue<string>();

        void Record(string what)
        {
            lock (recorder)
            {
                recorder.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + what);
                while (recorder.Count > 300) recorder.Dequeue();
            }
        }

        static string Describe(byte[] p)
        {
            if (p[0] == 0x51 && p.Length > 15 && (p[6] != 0xFF || p[7] != 0xFF))
            {
                int end = 15; while (end < p.Length && p[end] != 0 && p[end] != 0xFF) end++;
                return string.Format("51 text x={0} y={1} col={2:X4} mul={3}x{4} font={5} leds={6} \"{7}\"",
                    (p[6] << 8) | p[7], (p[8] << 8) | p[9], (p[10] << 8) | p[11], p[12], p[13], p[14],
                    BitConverter.ToString(p, 1, 5), Encoding.ASCII.GetString(p, 15, end - 15));
            }
            return BitConverter.ToString(p, 0, Math.Min(26, p.Length));
        }

        /// <summary>Writes the recorder to %APPDATA%\CSX2Dash\freeze-*.log and returns the path (null on failure).</summary>
        public string DumpRecorder(string reason)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CSX2Dash");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "freeze-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                lock (recorder) File.WriteAllLines(file, new[] { "Reason: " + reason, "Last " + recorder.Count + " packets (oldest first):" }.Concat(recorder));
                return file;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------ commands

        public static byte[] Filled(byte cmd)
        {
            var p = new byte[63];
            for (int i = 0; i < p.Length; i++) p[i] = 0xFF;
            p[0] = cmd;
            return p;
        }

        public string FirmwareVersion()
        {
            var r = Request(Filled(0x98));
            return r == null ? null : Encoding.ASCII.GetString(r, 1, 19).TrimEnd('\0');
        }

        /// <summary>
        /// Device-licence challenge (0x90), as UGT Manager runs on connect (UGTManager.cs L21066).
        /// The firmware locks up 30-60 s after power-up unless this is answered. Returns rounds passed (15 = OK).
        /// </summary>
        public int LicenceHandshake()
        {
            const string key = "6e7151d9-c5d9-439d-b81e-2bea345c3d55";
            var rnd = new Random();
            for (int k = 0; k < 15; k++)
            {
                var p = new byte[63];
                rnd.NextBytes(p);
                p[0] = 0x90;
                int idx = rnd.Next(1, 35);
                p[4] = (byte)idx;
                var r = Request(p);
                if (r == null || r[8] != (byte)key[idx]) return k;
            }
            return 15;
        }

        public bool ClearScreen() { return Send(Filled(0x44), ClearScreenDelay); }

        public bool BrandScreen() { return Send(Filled(0x37), DisplayImageDelay); }

        public bool ShowImage(uint sectorOffset, int x, int y)
        {
            var p = new byte[63];
            uint addr = sectorOffset * 512;
            p[0] = 0x35;
            p[1] = (byte)(addr >> 24); p[2] = (byte)(addr >> 16); p[3] = (byte)(addr >> 8); p[4] = (byte)addr;
            p[5] = (byte)(x >> 8); p[6] = (byte)x; p[7] = (byte)(y >> 8); p[8] = (byte)y;
            return Send(p, DisplayImageDelay);
        }

        public bool DrawText(byte[] leds, int x, int y, ushort rgb565, int wMul, int hMul, int fontRef, string text)
        {
            var p = Filled(0x51);
            Array.Copy(leds, 0, p, 1, 5);
            p[6] = (byte)(x >> 8); p[7] = (byte)x; p[8] = (byte)(y >> 8); p[9] = (byte)y;
            if (rgb565 == 0) rgb565 = 0xFFFF;                             // UGT substitutes white for 0
            p[10] = (byte)(rgb565 >> 8); p[11] = (byte)rgb565;
            p[12] = (byte)Math.Max(1, wMul); p[13] = (byte)Math.Max(1, hMul); p[14] = (byte)fontRef;
            var b = Encoding.ASCII.GetBytes(text ?? "");
            Array.Copy(b, 0, p, 15, Math.Min(b.Length, 47));
            return Send(p);
        }

        public bool SetLeds(byte[] leds)
        {
            var p = Filled(0x51);
            Array.Copy(leds, 0, p, 1, 5);
            return Send(p);
        }

        public bool Rectangle(bool filled, int x1, int y1, int x2, int y2, ushort rgb565)
        {
            var p = new byte[63];
            p[0] = 0x22; p[1] = (byte)(filled ? 0x04 : 0x03);
            int[] v = { x1, y1, x2, y2 };
            for (int i = 0; i < 4; i++) { p[2 + i * 2] = (byte)(v[i] >> 8); p[3 + i * 2] = (byte)v[i]; }
            p[10] = (byte)(rgb565 >> 8); p[11] = (byte)rgb565;
            return Send(p);
        }

        /// <summary>colour: 1=red 2=green 3=blue 4=yellow LED group; value 0..255.</summary>
        public bool LedBrightness(int colour, int value)
        {
            var p = Filled(0x66); p[1] = (byte)colour; p[2] = (byte)Math.Max(0, Math.Min(255, value));
            return Send(p, BrightnessDelay);
        }

        /// <summary>Backlight off: UGT's "turn off" command, LCD level 0 (UGTManager.cs L23840-23872).</summary>
        public bool LcdOff()
        {
            var p = new byte[63]; p[0] = 0x61; p[1] = 0;
            for (int i = 2; i < 16; i++) p[i] = 0xFF;
            return Send(p, BrightnessDelay);
        }

        /// <summary>LCD backlight 1..16.</summary>
        public bool LcdBrightness(int level)
        {
            var p = new byte[63]; p[0] = 0x61; p[1] = (byte)Math.Max(1, Math.Min(16, level));
            for (int i = 2; i < 16; i++) p[i] = 0xFF;
            return Send(p, BrightnessDelay);
        }

        /// <summary>
        /// Loads SD font "fontNNNN" into slot refNum (3..17). Returns true if the wheel answered ONEFONT.
        /// Pad data must already be off (UGT's ChangePage wraps all of a page's font loads in one 0xA0/0xA1).
        /// </summary>
        public bool LoadFont(int fontNumber, int refNum)
        {
            var p = Filled(0x46);
            p[1] = (byte)refNum;
            PutName(p, 2, string.Format("font{0:0000}.dat", fontNumber));
            PutName(p, 14, string.Format("font{0:0000}.gci", fontNumber));
            Thread.Sleep(LoadFontPre);
            var r = RequestRaw(p);
            bool ok = r != null && r[0] == 0x02 && Encoding.ASCII.GetString(r, 1, 7) == "ONEFONT";
            Thread.Sleep(ok ? LoadFontPost : FontErrorDelay + LoadFontPost);
            return ok;
        }

        /// <summary>Unloads the font in slot refNum (0x42). Pad data must already be off. True if the wheel confirmed.</summary>
        public bool UnloadFont(int refNum)
        {
            var p = Filled(0x42);
            p[1] = (byte)refNum;
            Thread.Sleep(UnloadFontPre);
            var r = RequestRaw(p);
            bool ok = r != null && r[0] == 0x02 && Encoding.ASCII.GetString(r, 1, 7) != "ONEFONT";
            Thread.Sleep(ok ? UnloadFontPost : FontErrorDelay + UnloadFontPost);
            return ok;
        }

        // Mirrors UGT's ChangePage (UGTManager.cs L18200): settle, unload ALL fonts if the new ones won't fit,
        // load the missing fonts inside ONE pad-data off/on, show the background, settle again.
        // fontRefs maps font number -> wheel slot (3..17) and is updated in place.
        public void ShowPage(Layout l, Dictionary<int, int> fontRefs, Action<string> warn)
        {
            Thread.Sleep(PageChangeDelay / 2);
            var missing = l.Fonts.Where(f => f > 2 && !fontRefs.ContainsKey(f)).Distinct().ToList();
            if (missing.Count > 0)
            {
                if (!PadData(false)) return;
                if (missing.Count > 15 - fontRefs.Count)
                {
                    foreach (var kv in fontRefs.ToList())
                    {
                        if (!IsOpen) return;
                        if (!UnloadFont(kv.Value)) warn("font slot " + kv.Value + " did not confirm unload");
                        fontRefs.Remove(kv.Key);
                    }
                    missing = l.Fonts.Where(f => f > 2).Distinct().ToList();
                }
                foreach (int f in missing.Take(15))
                {
                    if (!IsOpen) return;
                    var used = new HashSet<int>(fontRefs.Values);
                    int slot = Enumerable.Range(3, 15).First(s => !used.Contains(s));
                    if (LoadFont(f, slot)) fontRefs[f] = slot;
                    else warn("font " + f + " failed to load, using built-in font");
                }
                if (!PadData(true)) return;
            }
            if (l.BackgroundSector.HasValue) ShowImage(l.BackgroundSector.Value, 0, 0);
            else ClearScreen();
            Thread.Sleep(PageChangeDelay / 2);
        }

        static void PutName(byte[] p, int offset, string name)
        {
            // 12-byte field, right-aligned, zero-padded on the left (as UGT's LoadFont_New_v3).
            var b = Encoding.ASCII.GetBytes(name);
            int pad = 12 - b.Length;
            for (int i = 0; i < 12; i++) p[offset + i] = i < pad ? (byte)0 : b[i - pad];
        }
    }

    static class Native
    {
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] public static extern bool HidD_FlushQueue(SafeFileHandle h);
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, uint f);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA dd);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA dd, IntPtr buf, uint sz, out uint req, IntPtr di);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
        [DllImport("kernel32.dll")] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr o);
        [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }

        public static string FindHidPath(string filter)
        {
            Guid g; HidD_GetHidGuid(out g);
            IntPtr s = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
            try
            {
                var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
                for (uint i = 0; SetupDiEnumDeviceInterfaces(s, IntPtr.Zero, ref g, i, ref d); i++)
                {
                    uint req; SetupDiGetDeviceInterfaceDetail(s, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    IntPtr b = Marshal.AllocHGlobal((int)req);
                    try
                    {
                        Marshal.WriteInt32(b, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(s, ref d, b, req, out req, IntPtr.Zero))
                        {
                            string p = Marshal.PtrToStringUni(new IntPtr(b.ToInt64() + 4));
                            if (p.ToLowerInvariant().Contains(filter)) return p;
                        }
                    }
                    finally { Marshal.FreeHGlobal(b); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(s); }
            return null;
        }
    }
}
