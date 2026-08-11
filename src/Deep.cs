// Deeper checks: live network connections, non-obvious persistence, and
// configuration sabotage that survives deleting the malware itself.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Aftermath
{
    public static class Elevation
    {
        public static bool IsAdmin()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    var p = new WindowsPrincipal(id);
                    return p.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        public static bool Relaunch()
        {
            try
            {
                var psi = new ProcessStartInfo(Application_ExecutablePath());
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                return true;
            }
            catch { return false; }   // user declined the UAC prompt
        }

        private static string Application_ExecutablePath()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }
    }

    public static class Net
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen,
            bool sort, int ipVersion, int tblClass, int reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;   // bytes reversed
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        public class Conn
        {
            public string Local;
            public string Remote;
            public int Pid;
            public uint State;
        }

        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_ALL = 5;

        public static List<Conn> Tcp()
        {
            var list = new List<Conn>();
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buf, ref size, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                    return list;

                int count = Marshal.ReadInt32(buf);
                IntPtr row = (IntPtr)((long)buf + 4);
                int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));

                for (int i = 0; i < count; i++)
                {
                    var r = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(row, typeof(MIB_TCPROW_OWNER_PID));
                    var c = new Conn();
                    c.Pid = (int)r.owningPid;
                    c.State = r.state;
                    c.Local = Ip(r.localAddr) + ":" + Port(r.localPort);
                    c.Remote = Ip(r.remoteAddr) + ":" + Port(r.remotePort);
                    list.Add(c);
                    row = (IntPtr)((long)row + rowSize);
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
            return list;
        }

        private static string Ip(uint addr) { return new IPAddress(addr).ToString(); }

        private static int Port(uint p)
        {
            // Ports arrive in network byte order packed into a uint.
            return ((int)(p & 0xFF) << 8) | (int)((p >> 8) & 0xFF);
        }

        public const uint ESTABLISHED = 5;
        public const uint LISTEN = 2;
    }

    public static class Deep
    {
        // Locations a normal program has no business launching itself from.
        private static bool UserWritable(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var low = path.ToLowerInvariant();
            return low.Contains("\\appdata\\") || low.Contains("\\temp\\") ||
                   low.Contains("\\programdata\\") || low.Contains("\\users\\public\\") ||
                   low.Contains("\\downloads\\");
        }

        public static void Network(TriageResult r, Action<string> log)
        {
            log("Checking network connections...");

            var procPaths = new Dictionary<int, string[]>();   // pid -> {name, path}
            foreach (var p in Process.GetProcesses())
            {
                string path = "";
                try { path = p.MainModule != null ? p.MainModule.FileName : ""; }
                catch { path = ""; }   // access denied for elevated/other-user processes
                try { procPaths[p.Id] = new string[] { p.ProcessName, path }; }
                catch { }
            }

            var conns = Net.Tcp();
            int flagged = 0;

            var established = conns.Where(c => c.State == Net.ESTABLISHED &&
                                               !c.Remote.StartsWith("127.") &&
                                               !c.Remote.StartsWith("0.0.0.0")).ToList();

            foreach (var c in established)
            {
                string name = "(unknown)", path = "";
                if (procPaths.ContainsKey(c.Pid))
                {
                    name = procPaths[c.Pid][0];
                    path = procPaths[c.Pid][1];
                }

                bool odd = UserWritable(path);
                string sig = "";
                if (odd && !string.IsNullOrEmpty(path))
                {
                    sig = Signature.CheckEmbedded(path);
                    // Plenty of legitimate apps install to AppData (Discord, Slack,
                    // browsers). A valid signature clears them.
                    if (sig == "Valid") odd = false;
                }

                if (odd)
                {
                    flagged++;
                    r.Add(new Finding("Network", name + " -> " + c.Remote,
                        "Unsigned program running from a user-writable folder is talking to the internet. "
                        + "Signature: " + (string.IsNullOrEmpty(sig) ? "unknown" : sig) + "   " + path,
                        path, Sev.Warn, false));
                }
            }

            var listeners = conns.Where(c => c.State == Net.LISTEN &&
                                             !c.Local.StartsWith("127.")).ToList();
            foreach (var c in listeners)
            {
                string name = "(unknown)", path = "";
                if (procPaths.ContainsKey(c.Pid))
                {
                    name = procPaths[c.Pid][0];
                    path = procPaths[c.Pid][1];
                }
                if (UserWritable(path) && Signature.CheckEmbedded(path) != "Valid")
                {
                    flagged++;
                    r.Add(new Finding("Network", "Listening: " + name + " on " + c.Local,
                        "Accepting incoming connections from outside this PC, from a user-writable folder.   " + path,
                        path, Sev.Warn, false));
                }
            }

            r.Add(new Finding("Network",
                established.Count + " active connection(s), " + listeners.Count + " listening port(s)",
                flagged == 0
                    ? "Nothing suspicious: every connection belongs to a recognisable, properly installed program."
                    : flagged + " connection(s) need a look - listed above.",
                null, flagged == 0 ? Sev.Ok : Sev.Info, false));

            r.Add(new Finding("Network", "Snapshot only",
                "This is a point-in-time view. Malware that already ran and exited leaves nothing to see here.",
                null, Sev.Info, false));
        }

        public static void Persistence(TriageResult r, Action<string> log)
        {
            log("Auditing services, tasks and WMI subscriptions...");
            int flagged = 0;

            // Services running from user-writable locations.
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, PathName, State FROM Win32_Service"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string path = Convert.ToString(mo["PathName"]);
                        string name = Convert.ToString(mo["Name"]);
                        if (string.IsNullOrEmpty(path)) continue;

                        string exe = path.Trim();
                        if (exe.StartsWith("\""))
                        {
                            int close = exe.IndexOf('"', 1);
                            if (close > 1) exe = exe.Substring(1, close - 1);
                        }
                        else
                        {
                            int sp = exe.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
                            if (sp > 0) exe = exe.Substring(0, sp + 4);
                        }

                        if (!UserWritable(exe)) continue;

                        var sig = Signature.CheckEmbedded(exe);
                        if (sig == "Valid") continue;   // signed apps in ProgramData are normal

                        flagged++;
                        r.Add(new Finding("Persistence", "Service: " + name,
                            "Runs from a user-writable folder and is not validly signed (" + sig + ").   " + exe,
                            null, Sev.Warn, false));
                    }
                }
            }
            catch (Exception ex)
            {
                r.Add(new Finding("Persistence", "Could not read services", ex.Message, null, Sev.Info, false));
            }

            // WMI event subscriptions - a favourite fileless persistence trick.
            try
            {
                var scope = new ManagementScope(@"root\subscription");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM __EventConsumer")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string cls = Convert.ToString(mo["__CLASS"]);
                        string nm = Convert.ToString(mo["Name"]);

                        // These two can execute arbitrary code on an event trigger.
                        bool dangerous = cls.IndexOf("CommandLineEventConsumer", StringComparison.OrdinalIgnoreCase) >= 0
                                      || cls.IndexOf("ActiveScriptEventConsumer", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (dangerous)
                        {
                            flagged++;
                            string extra = "";
                            try
                            {
                                var cl = Convert.ToString(mo["CommandLineTemplate"]);
                                if (!string.IsNullOrEmpty(cl)) extra = "  ->  " + cl;
                            }
                            catch { }
                            var fWmi = new Finding("Persistence", "WMI consumer: " + nm,
                                "A " + cls + " runs code when a system event fires. This is a known fileless persistence technique and is rare on normal machines." + extra,
                                null, Sev.Bad, false);
                            fWmi.Watchlist = true;   // CommandLine/ActiveScript event consumer - almost never innocent
                            r.Add(fWmi);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                r.Add(new Finding("Persistence", "Could not read WMI subscriptions", ex.Message, null, Sev.Info, false));
            }

            // Scheduled tasks pointing at user-writable locations.
            try
            {
                var psi = new ProcessStartInfo("schtasks", "/query /fo csv /v");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                var proc = Process.Start(psi);
                string outp = "";
                if (proc != null)
                {
                    outp = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(30000);
                }

                foreach (var line in outp.Split('\n'))
                {
                    var low = line.ToLowerInvariant();
                    if (low.IndexOf("\\appdata\\") < 0 && low.IndexOf("\\temp\\") < 0) continue;
                    if (low.IndexOf("opera") >= 0 || low.IndexOf("powertoys") >= 0 ||
                        low.IndexOf("onedrive") >= 0 || low.IndexOf("google") >= 0 ||
                        low.IndexOf("aftermathcleanup") >= 0) continue;

                    var cells = line.Split(',');
                    string taskName = cells.Length > 1 ? cells[1].Trim('"') : "(unnamed)";
                    flagged++;
                    r.Add(new Finding("Persistence", "Scheduled task: " + taskName,
                        "Runs a program from AppData or Temp at a scheduled time. Worth confirming you recognise it.",
                        null, Sev.Info, false));
                }
            }
            catch { }

            if (flagged == 0)
                r.Add(new Finding("Persistence", "No suspicious persistence found",
                    "No rogue services, no WMI event-consumer backdoors, no scheduled tasks running from temporary folders.",
                    null, Sev.Ok, false));
        }

        // Malware often sabotages update mechanisms so its damage cannot be patched
        // away. Deleting the malware does NOT undo this - it has to be checked for
        // separately.
        public static void Sabotage(TriageResult r, Action<string> log)
        {
            log("Checking for disabled updates...");

            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration"))
                {
                    if (k != null)
                    {
                        var enabled = Convert.ToString(k.GetValue("UpdatesEnabled"));
                        var url = Convert.ToString(k.GetValue("UpdateUrl"));
                        var products = Convert.ToString(k.GetValue("ProductReleaseIds"));

                        bool off = string.Equals(enabled, "False", StringComparison.OrdinalIgnoreCase);
                        bool oddUrl = !string.IsNullOrEmpty(url) &&
                                      url.IndexOf("officecdn.microsoft.com", StringComparison.OrdinalIgnoreCase) < 0;

                        if (off || oddUrl)
                            r.Add(new Finding("System", "Office updates are disabled or redirected",
                                "UpdatesEnabled=" + enabled + "  UpdateUrl=" + url +
                                "  -  pirated Office installers do this so the crack is not patched away. Your Office will never receive security fixes.",
                                null, Sev.Bad, false));
                        else
                            r.Add(new Finding("System", "Office updates are healthy",
                                "Updates enabled and pointing at Microsoft's own servers." +
                                (string.IsNullOrEmpty(products) ? "" : "  Products: " + products),
                                null, Sev.Ok, false));
                    }
                }
            }
            catch { }

            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"))
                {
                    if (k != null)
                    {
                        var no = Convert.ToString(k.GetValue("NoAutoUpdate"));
                        if (no == "1")
                            r.Add(new Finding("System", "Windows Update automatic updates are switched off",
                                "A policy is set to stop Windows updating itself. If you did not set this, treat it as tampering.",
                                null, Sev.Bad, false));
                    }
                }
            }
            catch { }

            if (!Elevation.IsAdmin())
                r.Add(new Finding("System", "Running without Administrator",
                    "Some checks cannot run: Defender's exclusion list, and programs owned by other users. Restart as Administrator for full coverage.",
                    null, Sev.Info, false));
            else
                DefenderExclusions(r);
        }

        private static void DefenderExclusions(TriageResult r)
        {
            try
            {
                var scope = new ManagementScope(@"root\Microsoft\Windows\Defender");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpPreference")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        var paths = mo["ExclusionPath"] as string[];
                        if (paths != null && paths.Length > 0)
                        {
                            var suspicious = paths.Where(p => UserWritable(p) ||
                                                              p.TrimEnd('\\').Equals("C:", StringComparison.OrdinalIgnoreCase)).ToList();
                            if (suspicious.Count > 0)
                            {
                                var fExcl = new Finding("System", "Defender has suspicious exclusions",
                                    "Antivirus is told to ignore: " + string.Join("   ", suspicious.ToArray()) +
                                    "  -  malware adds exclusions so it cannot be detected.",
                                    null, Sev.Bad, false);
                                fExcl.Watchlist = true;   // new Defender exclusion - almost never innocent
                                r.Add(fExcl);
                            }
                            else
                                r.Add(new Finding("System", paths.Length + " Defender exclusion(s), none suspicious",
                                    string.Join("   ", paths), null, Sev.Info, false));
                        }
                        else
                        {
                            r.Add(new Finding("System", "No Defender exclusions",
                                "Nothing has been hidden from your antivirus.", null, Sev.Ok, false));
                        }
                        break;
                    }
                }
            }
            catch { }
        }
    }
}
