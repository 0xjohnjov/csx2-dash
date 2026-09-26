using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class Probe
{
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, uint f);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA dd);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA dd, IntPtr buf, uint sz, out uint req, IntPtr di);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }

    static List<string> Paths(string filter)
    {
        var r = new List<string>(); Guid g; HidD_GetHidGuid(out g);
        IntPtr s = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
        for (uint i = 0; SetupDiEnumDeviceInterfaces(s, IntPtr.Zero, ref g, i, ref d); i++)
        {
            uint req; SetupDiGetDeviceInterfaceDetail(s, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
            IntPtr b = Marshal.AllocHGlobal((int)req); Marshal.WriteInt32(b, IntPtr.Size == 8 ? 8 : 6);
            if (SetupDiGetDeviceInterfaceDetail(s, ref d, b, req, out req, IntPtr.Zero))
            {
                string p = Marshal.PtrToStringUni(new IntPtr(b.ToInt64() + 4));
                if (p.ToLowerInvariant().Contains(filter)) r.Add(p);
            }
            Marshal.FreeHGlobal(b);
        }
        SetupDiDestroyDeviceInfoList(s); return r;
    }

    static void Send(string path, byte cmd)
    {
        using (var h = CreateFile(path, 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            if (h.IsInvalid) { Console.WriteLine("open col02 failed " + Marshal.GetLastWin32Error()); return; }
            using (var fs = new FileStream(h, FileAccess.Write, 1, false))
            { var b = new byte[65]; b[0] = 2; b[1] = cmd; fs.Write(b, 0, 65); }
            Console.WriteLine("sent 0x" + cmd.ToString("X2"));
        }
    }

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "guided") return Guided();
        if (args.Length > 0 && args[0] == "query") return Query(args);
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 5;
        var paths = Paths("vid_04d8&pid_f4c3");
        Console.WriteLine("found " + paths.Count + " interfaces");
        string col2 = paths.Find(p => p.Contains("col02"));
        for (int i = 1; i < args.Length; i++) Send(col2, Convert.ToByte(args[i], 16));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var streams = new List<FileStream>();
        foreach (var p in paths)
        {
            var h = CreateFile(p, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) { Console.WriteLine("open failed " + p); continue; }
            var fs = new FileStream(h, FileAccess.Read, 1, false); streams.Add(fs);
            string tag = p.Substring(p.IndexOf("col"), 5);
            var t = new Thread(() =>
            {
                var buf = new byte[65]; int count = 0; string last = null; int printed = 0;
                try
                {
                    while (true)
                    {
                        int n = fs.Read(buf, 0, buf.Length); count++;
                        string hex = BitConverter.ToString(buf, 0, n);
                        if (hex != last && printed++ < 60) Console.WriteLine("{0,6}ms {1} #{2} {3}", sw.ElapsedMilliseconds, tag, count, hex);
                        last = hex;
                    }
                }
                catch { Console.WriteLine(tag + " total reports: " + count); }
            });
            t.IsBackground = true; t.Start();
        }
        Thread.Sleep(seconds * 1000);
        foreach (var s in streams) s.Dispose();
        Thread.Sleep(500);
        return 0;
    }

    // Send a request (hex bytes) on col02 and wait up to 2 s for the reply, like UGT's SendData.
    static int Query(string[] args)
    {
        string col2 = Paths("vid_04d8&pid_f4c3").Find(p => p.Contains("col02"));
        using (var h = CreateFile(col2, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        using (var fs = new FileStream(h, FileAccess.ReadWrite, 1, false))
        {
            var req = new byte[65]; req[0] = 2;
            for (int i = 1; i < args.Length; i++) req[i] = Convert.ToByte(args[i], 16);
            var wt = new Thread(() => fs.Write(req, 0, 65)); wt.IsBackground = true; wt.Start();
            if (!wt.Join(2000)) { Console.WriteLine("write blocked for 2 s - device not accepting commands"); Environment.Exit(2); }
            Console.WriteLine("sent  " + BitConverter.ToString(req, 0, Math.Max(2, args.Length)));
            var buf = new byte[65];
            var t = new Thread(() => { try { int n = fs.Read(buf, 0, 65); Console.WriteLine("reply " + BitConverter.ToString(buf, 0, n)); Console.WriteLine("ascii " + System.Text.Encoding.ASCII.GetString(buf, 1, n - 1).Replace('\0', '.')); } catch (Exception e) { Console.WriteLine("read error " + e.Message); } });
            t.IsBackground = true; t.Start();
            if (!t.Join(2000)) { Console.WriteLine("no reply within 2 s"); Environment.Exit(1); }
        }
        return 0;
    }

    // Interactive test: phase 1 without any command, phase 2 after sending Enable_Pad_Data (0xA1).
    static TextWriter logw;
    static readonly object lk = new object();
    static int phase = 0;
    static int[] counts = new int[4];
    static void Log(string s) { lock (lk) { Console.WriteLine(s); logw.WriteLine(s); logw.Flush(); } }

    static int Guided()
    {
        logw = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "guided.log"), false);
        var paths = Paths("vid_04d8&pid_f4c3");
        Log("found " + paths.Count + " interfaces");
        string col2 = paths.Find(p => p.Contains("col02"));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var p in paths)
        {
            var h = CreateFile(p, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) { Log("open failed " + p + " err " + Marshal.GetLastWin32Error()); continue; }
            var fs = new FileStream(h, FileAccess.Read, 1, false);
            string tag = p.Substring(p.IndexOf("col"), 5);
            var t = new Thread(() =>
            {
                var buf = new byte[65]; string last = null; int printed = 0;
                try
                {
                    while (true)
                    {
                        int n = fs.Read(buf, 0, buf.Length);
                        lock (lk) counts[phase * 2 + (tag == "col02" ? 1 : 0)]++;
                        string hex = BitConverter.ToString(buf, 0, n);
                        if (hex != last && printed++ < 300) Log(string.Format("P{0} {1,6}ms {2} {3}", phase + 1, sw.ElapsedMilliseconds, tag, hex));
                        last = hex;
                    }
                }
                catch (Exception e) { Log(tag + " read ended: " + e.Message); }
            });
            t.IsBackground = true; t.Start();
        }
        Console.WriteLine("\nMake sure UGT Manager is CLOSED. Sit at the wheel, then press ENTER here to start phase 1.");
        Console.ReadLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n=== PHASE 1 (20 s): press several wheel buttons and pull BOTH clutch paddles now ===\n");
        Console.ResetColor();
        Thread.Sleep(20000);
        Log(string.Format("PHASE 1 done: joystick reports={0}, vendor reports={1}", counts[0], counts[1]));
        lock (lk) phase = 1;
        Send(col2, 0xA1);
        Log("sent Enable_Pad_Data (0xA1)");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n=== PHASE 2 (20 s): press the SAME buttons and pull BOTH clutch paddles again ===\n");
        Console.ResetColor();
        Thread.Sleep(20000);
        Log(string.Format("PHASE 2 done: joystick reports={0}, vendor reports={1}", counts[2], counts[3]));
        Console.WriteLine("\nAll done - this window closes in 5 seconds.");
        Thread.Sleep(5000);
        return 0;
    }
}
