// CSX2Recovery - recovers a Cube Controls CSX2 ("UGT Sim LCD and CONTROLS V2", USB 04D8:F4C3)
// whose firmware has locked up (buttons/paddles dead, wheel still listed in Windows).
//
// Flow: close UGT Manager -> health check -> escalating resets (health check after each) -> relaunch UGT Manager.
// Every run is appended to logs\recovery.log (next to the exe) so we can learn which step actually clears the lock-up.
// Build: build.cmd (uses the C# compiler that ships with Windows / .NET Framework 4).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class CSX2Recovery
{
    const string WheelUsbPrefix = @"USB\VID_04D8&PID_F4C3\";
    const string WheelHidFilter = "vid_04d8&pid_f4c3&col02";   // UGT vendor command channel
    const string UgtProcess = "UGTManager";
    const string UgtExe = @"C:\Program Files (x86)\Ultimate Game Tech\UGT Manager\UGTManager.exe";

    // This tool runs as admin: always start system tools by full path, never by bare name
    // (a bare name is looked up in this exe's own folder first, which may be user-writable).
    static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    static readonly string PnpUtilExe = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
    static readonly string ExplorerExe = Path.Combine(WinDir, "explorer.exe");

    // Commands decoded from UGT Manager 1.6.819 (see README).
    const byte CmdReset = 0x94;          // Reset_Device: firmware reboot
    const byte CmdFirmwareVersion = 0x98; // reply: "UGT G4:CC01/01/20rC"
    const byte CmdEnablePadData = 0xA1;   // Enable_Pad_Data: button/paddle reports on

    static StreamWriter logw;

    static int Main(string[] args)
    {
        string root = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        string logDir = Path.Combine(root, "logs"), logFile = Path.Combine(logDir, "recovery.log");
        Directory.CreateDirectory(logDir);
        // Running as admin: don't follow a symlink/junction planted in place of the log (it could redirect our writes).
        bool linked = IsLink(logDir) || (File.Exists(logFile) && IsLink(logFile));
        logw = linked ? StreamWriter.Null : new StreamWriter(logFile, true, Encoding.UTF8);
        if (linked) Console.WriteLine("Log disabled: " + logFile + " is a link.");
        Console.Title = "CSX2 Recovery";
        Log("==================== CSX2 Recovery started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====================");
        bool checkOnly = Array.IndexOf(args, "--check") >= 0;
        int rc;
        try { rc = checkOnly ? CheckOnly() : Recover(); }
        catch (Exception e) { Log("UNEXPECTED ERROR: " + e); rc = 99; }
        Log("==================== finished, exit code " + rc + " ====================\n");
        logw.Dispose();
        if (Array.IndexOf(args, "--no-pause") < 0)
        {
            Console.WriteLine("\nThis window closes in 15 seconds (or press any key).");
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 15 && !Console.KeyAvailable) Thread.Sleep(100);
        }
        return rc;
    }

    static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch { return true; }
    }

    static int CheckOnly()
    {
        string inst = FindWheelInstance();
        if (inst == null) { Say(ConsoleColor.Red, "Wheel not connected."); return 2; }
        Log("Wheel: " + inst);
        string fw;
        if (IsHealthy(out fw)) { Say(ConsoleColor.Green, "Wheel is responding (" + fw + ")."); return 0; }
        Say(ConsoleColor.Red, "Wheel is NOT responding (firmware locked up)."); return 1;
    }

    static int Recover()
    {
        bool ugtWasRunning = CloseUgt();

        string inst = FindWheelInstance();
        if (inst == null)
        {
            Say(ConsoleColor.Yellow, "The wheel is not connected at all. Plug it in (both USB leads) ...");
            if (!WaitForReplug(TimeSpan.FromMinutes(2), false)) { Say(ConsoleColor.Red, "Wheel never appeared. Check cables/power."); return 2; }
            inst = FindWheelInstance();
        }
        Log("Wheel USB instance: " + inst);

        string fw;
        string fixedBy = null;
        if (IsHealthy(out fw))
        {
            Log("Wheel firmware is responding (" + fw + "). It was not locked up; re-enabling pad data only.");
            fixedBy = "none needed (wheel was responding)";
        }
        else
        {
            Say(ConsoleColor.Yellow, "Wheel firmware is not responding - starting recovery.");
            var steps = new List<KeyValuePair<string, Func<bool>>>();
            steps.Add(new KeyValuePair<string, Func<bool>>("1. UGT reset command (0x94)", () => SendCommand(CmdReset)));
            steps.Add(new KeyValuePair<string, Func<bool>>("2. Restart wheel USB device", () => PnpUtil("/restart-device \"" + FindWheelInstance() + "\"")));
            steps.Add(new KeyValuePair<string, Func<bool>>("3. Restart the wheel's private USB hub", () => HubAction("restart")));
            steps.Add(new KeyValuePair<string, Func<bool>>("4. Disable + re-enable the wheel's private USB hub", () => HubAction("cycle")));
            foreach (var step in steps)
            {
                Say(ConsoleColor.Cyan, "Trying " + step.Key + " ...");
                bool ran = false;
                try { ran = step.Value(); } catch (Exception e) { Log("  step error: " + e.Message); }
                if (!ran) { Log("  step could not be performed, skipping"); continue; }
                if (WaitHealthy(TimeSpan.FromSeconds(15), out fw)) { fixedBy = step.Key; break; }
                Log("  wheel still not responding");
            }
            if (fixedBy == null)
            {
                Say(ConsoleColor.Yellow, "\nSoftware reset did not clear the lock-up.\n>>> UNPLUG the wheel's USB (both leads), wait 5 seconds, plug it back in. Waiting up to 3 minutes ...");
                if (WaitForReplug(TimeSpan.FromMinutes(3), true) && WaitHealthy(TimeSpan.FromSeconds(15), out fw)) fixedBy = "5. Manual unplug/replug";
            }
        }

        if (fixedBy == null)
        {
            Say(ConsoleColor.Red, "RESULT: wheel still not responding. Try another USB port / powered hub.");
            if (ugtWasRunning) StartUgt();
            return 1;
        }

        SendCommand(CmdEnablePadData);
        Log("Sent Enable_Pad_Data (0xA1).");
        Say(ConsoleColor.Green, "RESULT: wheel OK (" + fw + "). Fixed by: " + fixedBy);
        StartUgt();
        Say(ConsoleColor.Green, "Done. If Assetto Corsa is running, it may need you to re-select the wheel in Controls or restart the session.");
        return 0;
    }

    // ---------------------------------------------------------------- UGT Manager

    static bool CloseUgt()
    {
        // FanaLEDs sends Fanatec LED commands to the CSX2 and locks up its firmware (confirmed 26/09/2026).
        foreach (var p in Process.GetProcessesByName("FanaLEDs"))
        {
            Log("FanaLEDs is running - closing it (it sends Fanatec LED commands to the CSX2 and locks it up).");
            try { p.Kill(); p.WaitForExit(5000); } catch (Exception e) { Log("  could not close FanaLEDs: " + e.Message); }
        }
        var procs = Process.GetProcessesByName(UgtProcess);
        if (procs.Length == 0) { Log("UGT Manager not running."); return false; }
        Log("Closing UGT Manager (" + procs.Length + " process(es)) so it releases the wheel ...");
        foreach (var p in procs)
        {
            try { p.CloseMainWindow(); if (!p.WaitForExit(4000)) { p.Kill(); p.WaitForExit(5000); } }
            catch (Exception e) { Log("  could not close pid " + p.Id + ": " + e.Message); }
        }
        Thread.Sleep(1000);
        return true;
    }

    static void StartUgt()
    {
        if (!File.Exists(UgtExe)) { Log("UGT Manager not found at " + UgtExe); return; }
        // Launch through Explorer so UGT runs as the normal (non-admin) user, like it would from the Start menu.
        Process.Start(ExplorerExe, "\"" + UgtExe + "\"");
        Log("Relaunched UGT Manager.");
    }

    // ---------------------------------------------------------------- health / commands

    static bool WaitHealthy(TimeSpan timeout, out string fw)
    {
        fw = null;
        var sw = Stopwatch.StartNew();
        Thread.Sleep(2000);   // give the wheel time to (re)enumerate
        while (sw.Elapsed < timeout)
        {
            if (FindWheelInstance() != null && IsHealthy(out fw)) return true;
            Thread.Sleep(1000);
        }
        return false;
    }

    static bool IsHealthy(out string fw)
    {
        fw = null;
        byte[] reply = Transact(CmdFirmwareVersion, true);
        if (reply == null) return false;
        fw = Encoding.ASCII.GetString(reply, 1, 19).TrimEnd('\0');
        return fw.StartsWith("UGT");
    }

    static bool SendCommand(byte cmd) { return Transact(cmd, false) != null; }

    // Writes one 64-byte command (report ID 2) to the vendor channel; optionally waits for the reply.
    // Uses overlapped I/O with timeouts so a locked-up wheel can never hang this program.
    static byte[] Transact(byte cmd, bool wantReply)
    {
        string path = Hid.FindPath(WheelHidFilter);
        if (path == null) { Log("  vendor channel not found"); return null; }
        using (SafeFileHandle h = Hid.Open(path))
        {
            if (h.IsInvalid) { Log("  cannot open vendor channel, error " + Marshal.GetLastWin32Error()); return null; }
            using (var fs = new FileStream(h, FileAccess.ReadWrite, 1, true))
            {
                var req = new byte[65]; req[0] = 2; req[1] = cmd;
                var buf = new byte[65];
                IAsyncResult rd = wantReply ? fs.BeginRead(buf, 0, 65, null, null) : null;
                IAsyncResult wr = fs.BeginWrite(req, 0, 65, null, null);
                if (!wr.AsyncWaitHandle.WaitOne(2000)) { Hid.CancelIoEx(h, IntPtr.Zero); Log(string.Format("  0x{0:X2}: write timed out (wheel not accepting commands)", cmd)); return null; }
                try { fs.EndWrite(wr); } catch (Exception e) { Log(string.Format("  0x{0:X2}: write failed: {1}", cmd, e.Message)); Hid.CancelIoEx(h, IntPtr.Zero); return null; }
                if (!wantReply) return req;
                if (!rd.AsyncWaitHandle.WaitOne(2000)) { Hid.CancelIoEx(h, IntPtr.Zero); Log(string.Format("  0x{0:X2}: no reply within 2 s", cmd)); return null; }
                try { fs.EndRead(rd); } catch (Exception e) { Log("  read failed: " + e.Message); return null; }
                return buf;
            }
        }
    }

    // ---------------------------------------------------------------- PnP resets

    static string FindWheelInstance()
    {
        foreach (string id in Pnp.PresentInstances(WheelUsbPrefix)) return id;
        return null;
    }

    static bool HubAction(string action)
    {
        string wheel = FindWheelInstance();
        if (wheel == null) { Log("  wheel not present"); return false; }
        string hub = Pnp.Parent(wheel);
        if (hub == null || !hub.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase)) { Log("  parent is not an external/onboard hub (" + hub + "), skipping"); return false; }
        var children = Pnp.Children(hub);
        Log("  wheel hub: " + hub + " (" + children.Count + " device(s) attached)");
        if (children.Count != 1)
        {
            // Never reset a hub shared with pedals / wheelbase / other devices.
            foreach (var c in children) Log("    shared with: " + c);
            Log("  hub is shared with other devices - skipping to protect them");
            return false;
        }
        if (action == "restart") return PnpUtil("/restart-device \"" + hub + "\"");
        bool ok = PnpUtil("/disable-device \"" + hub + "\"");
        Thread.Sleep(3000);
        return PnpUtil("/enable-device \"" + hub + "\"") && ok;
    }

    static bool PnpUtil(string args)
    {
        Log("  pnputil " + args);
        var psi = new ProcessStartInfo(PnpUtilExe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using (var p = Process.Start(psi))
        {
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            if (!p.WaitForExit(30000)) { Log("  pnputil timed out"); return false; }
            foreach (var line in o.Split('\n')) if (line.Trim().Length > 0) Log("    " + line.Trim());
            Log("  pnputil exit code " + p.ExitCode);
            return p.ExitCode == 0 || p.ExitCode == 3010;
        }
    }

    static bool WaitForReplug(TimeSpan timeout, bool requireUnplugFirst)
    {
        var sw = Stopwatch.StartNew();
        bool gone = !requireUnplugFirst;
        while (sw.Elapsed < timeout)
        {
            bool present = FindWheelInstance() != null;
            if (!present && !gone) { gone = true; Log("  wheel unplugged - now plug it back in"); }
            if (present && gone) { Log("  wheel detected"); return true; }
            Thread.Sleep(300);
        }
        return false;
    }

    // ---------------------------------------------------------------- logging

    static void Log(string s)
    {
        string line = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
        Console.WriteLine(line);
        logw.WriteLine(line); logw.Flush();
    }

    static void Say(ConsoleColor c, string s) { Console.ForegroundColor = c; Log(s); Console.ResetColor(); }
}

static class Hid
{
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, uint f);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA dd);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA dd, IntPtr buf, uint sz, out uint req, IntPtr di);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
    [DllImport("kernel32.dll")] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr o);
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }

    public static SafeFileHandle Open(string path)
    {
        // GENERIC_READ|GENERIC_WRITE, share read/write, OPEN_EXISTING, FILE_FLAG_OVERLAPPED
        return CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
    }

    public static string FindPath(string filter)
    {
        Guid g; HidD_GetHidGuid(out g);
        IntPtr s = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);   // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
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

static class Pnp
{
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Locate_DevNode(out uint dn, string id, uint flags);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out uint dn, uint child, uint flags);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_Child(out uint dn, uint parent, uint flags);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_Sibling(out uint dn, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID(uint dn, StringBuilder buf, int len, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_List_Size(out int len, string filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_List(string filter, char[] buf, int len, uint flags);

    static string Id(uint dn) { var sb = new StringBuilder(400); return CM_Get_Device_ID(dn, sb, sb.Capacity, 0) == 0 ? sb.ToString() : null; }

    public static List<string> PresentInstances(string prefix)
    {
        var r = new List<string>();
        const uint CM_GETIDLIST_FILTER_PRESENT = 0x100, CM_GETIDLIST_FILTER_ENUMERATOR = 0x1;
        int len;
        if (CM_Get_Device_ID_List_Size(out len, "USB", CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT) != 0) return r;
        var buf = new char[len];
        if (CM_Get_Device_ID_List("USB", buf, len, CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT) != 0) return r;
        foreach (var id in new string(buf).Split('\0'))
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) r.Add(id);
        return r;
    }

    public static string Parent(string id)
    {
        uint dn, p;
        if (CM_Locate_DevNode(out dn, id, 0) != 0 || CM_Get_Parent(out p, dn, 0) != 0) return null;
        return Id(p);
    }

    public static List<string> Children(string id)
    {
        var r = new List<string>(); uint dn, c;
        if (CM_Locate_DevNode(out dn, id, 0) != 0 || CM_Get_Child(out c, dn, 0) != 0) return r;
        do { r.Add(Id(c)); } while (CM_Get_Sibling(out c, c, 0) == 0);
        return r;
    }
}
