// Agentless sweep sweep: push this same exe to a list of hosts the admin typed
// in themselves, run the exact local triage on each one via --headless, and
// pull the report back over the admin share.
//
// Two rules enforced HERE, in code, not just in the UI copy:
//   1. The only hosts this ever touches are the ones passed into RunSweep's
//      `hosts` argument by the caller - there is no discovery of any kind
//      anywhere below: no ping, no ARP read, no subnet enumeration.
//   2. `username`/`password` are plain local parameters, passed straight into
//      ConnectionOptions and WNetAddConnection2 and never assigned to a field,
//      a static, or written to any file - see RunOneHost. They live only for
//      the duration of one host's steps and go out of scope with the method
//      that used them. SweepAudit below records host/time/outcome only.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Aftermath
{
    public enum SweepStatus { Pending, Copying, Running, Collecting, Done, Failed }

    public class SweepTarget
    {
        public string Host = "";
        public SweepStatus Status = SweepStatus.Pending;
        public string Error;             // null when there is none
        public TriageResult Result;      // null until Status == Done
    }

    public static class SweepRunner
    {
        // At most this many hosts are contacted at once. Plain Thread + a
        // counting Semaphore, matching this codebase's existing threading style
        // (Thread with IsBackground=true) rather than reaching for TPL.
        private const int MaxConcurrent = 5;
        private const int RemoteTimeoutMinutes = 5;
        private const int RESOURCETYPE_DISK = 0x00000001;

        [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int WNetAddConnection2(ref NETRESOURCE lpNetResource, string lpPassword, string lpUsername, int dwFlags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int WNetCancelConnection2(string lpName, int dwFlags, bool fForce);

        [StructLayout(LayoutKind.Sequential)]
        private struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        // Runs the sweep and returns when every host has reached Done or Failed.
        // `onProgress` is called from whichever worker thread finishes a step -
        // same contract as the Action<string> log callbacks Scanner/Deep already
        // use: this method does not know or care about UI marshalling, the
        // caller's callback is responsible for that (see MainForm.OnSweepProgress
        // using BeginInvoke, same pattern as SetStatus).
        public static List<SweepTarget> RunSweep(List<string> hosts, string username, string password, Action<SweepTarget> onProgress)
        {
            var targets = new List<SweepTarget>();
            foreach (var h in hosts)
            {
                var t = new SweepTarget();
                t.Host = h;
                targets.Add(t);
            }

            SweepAudit.SweepStart(hosts);

            var gate = new Semaphore(MaxConcurrent, MaxConcurrent);
            var threads = new List<Thread>();

            foreach (var target in targets)
            {
                var localTarget = target;   // own copy per closure
                var th = new Thread(delegate ()
                {
                    gate.WaitOne();
                    try { RunOneHost(localTarget, username, password, onProgress); }
                    finally { gate.Release(); }
                });
                th.IsBackground = true;
                threads.Add(th);
                th.Start();
            }

            foreach (var th in threads) th.Join();

            return targets;
        }

        private static void RunOneHost(SweepTarget target, string username, string password, Action<SweepTarget> onProgress)
        {
            string step = "connecting";
            string share = @"\\" + target.Host + @"\C$";
            bool shareConnected = false;
            string remoteExeUnc = share + @"\Windows\Temp\Aftermath.exe";
            string remoteReportUnc = share + @"\Windows\Temp\aftermath-report.json";

            try
            {
                target.Status = SweepStatus.Copying;
                Report(target, onProgress);

                step = "connecting to " + target.Host + " over WMI";
                var options = new ConnectionOptions();
                options.Username = username;
                options.Password = password;
                options.Impersonation = ImpersonationLevel.Impersonate;
                options.EnablePrivileges = true;
                var scope = new ManagementScope(@"\\" + target.Host + @"\root\cimv2", options);
                scope.Connect();

                step = "authenticating to the admin share " + share;
                var nr = new NETRESOURCE();
                nr.dwType = RESOURCETYPE_DISK;
                nr.lpRemoteName = share;
                int rc = WNetAddConnection2(ref nr, password, username, 0);
                if (rc != 0) throw new Exception("WNetAddConnection2 failed with error " + rc);
                shareConnected = true;

                step = "copying Aftermath.exe to " + remoteExeUnc;
                string localExe = Process.GetCurrentProcess().MainModule.FileName;
                File.Copy(localExe, remoteExeUnc, true);

                step = "launching the remote scan (Win32_Process.Create)";
                target.Status = SweepStatus.Running;
                Report(target, onProgress);

                uint pid;
                using (var procClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null))
                {
                    var inParams = procClass.GetMethodParameters("Create");
                    inParams["CommandLine"] = "\"C:\\Windows\\Temp\\Aftermath.exe\" --headless --out \"C:\\Windows\\Temp\\aftermath-report.json\"";
                    var outParams = procClass.InvokeMethod("Create", inParams, null);
                    uint retVal = Convert.ToUInt32(outParams["ReturnValue"]);
                    if (retVal != 0) throw new Exception("Win32_Process.Create returned error code " + retVal);
                    pid = Convert.ToUInt32(outParams["ProcessId"]);
                }

                step = "waiting for the remote scan on " + target.Host + " to finish";
                DateTime deadline = DateTime.UtcNow.AddMinutes(RemoteTimeoutMinutes);
                bool exited = false;
                while (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(3000);
                    bool stillRunning = false;
                    using (var searcher = new ManagementObjectSearcher(scope,
                        new ObjectQuery("SELECT ProcessId FROM Win32_Process WHERE ProcessId=" + pid.ToString(CultureInfo.InvariantCulture))))
                    {
                        foreach (ManagementObject mo in searcher.Get()) stillRunning = true;
                    }
                    if (!stillRunning) { exited = true; break; }
                }
                if (!exited) throw new Exception("the remote scan did not finish within " + RemoteTimeoutMinutes + " minutes");

                step = "collecting the report from " + remoteReportUnc;
                target.Status = SweepStatus.Collecting;
                Report(target, onProgress);

                if (!File.Exists(remoteReportUnc))
                    throw new Exception("no report file was found on " + target.Host + " after the scan finished");

                string localDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Aftermath\Sweep");
                if (!Directory.Exists(localDir)) Directory.CreateDirectory(localDir);
                string localReport = Path.Combine(localDir, SafeFileName(target.Host) + "-report.json");
                File.Copy(remoteReportUnc, localReport, true);
                target.Result = ReportIO.LoadTriageResult(localReport);

                target.Status = SweepStatus.Done;
                target.Error = null;
            }
            catch (Exception ex)
            {
                target.Status = SweepStatus.Failed;
                target.Error = "Failed at: " + step + " - " + ex.Message;
            }
            finally
            {
                // Best-effort cleanup, attempted no matter how far this host got or
                // whether it succeeded - a failed sweep should not leave a copy of
                // Aftermath sitting in the remote host's Temp folder. Never fails
                // the sweep for this host, and never stops the others.
                if (shareConnected)
                {
                    try { if (File.Exists(remoteExeUnc)) File.Delete(remoteExeUnc); } catch { }
                    try { if (File.Exists(remoteReportUnc)) File.Delete(remoteReportUnc); } catch { }
                    try { WNetCancelConnection2(share, 0, true); } catch { }
                }
            }

            SweepAudit.HostResult(target.Host, target.Status, target.Result != null ? target.Result.Findings.Count : 0);
            Report(target, onProgress);
        }

        private static void Report(SweepTarget target, Action<SweepTarget> onProgress)
        {
            if (onProgress != null) onProgress(target);
        }

        private static string SafeFileName(string host)
        {
            var sb = new StringBuilder();
            foreach (char c in host)
            {
                bool bad = false;
                foreach (char inv in Path.GetInvalidFileNameChars()) { if (c == inv) { bad = true; break; } }
                sb.Append(bad ? '_' : c);
            }
            return sb.ToString();
        }
    }

    // Append-only, human-readable record of every sweep: which hosts were
    // targeted, when, and what happened to each one. Deliberately carries no
    // credential of any kind - only host identity, timing and outcome, so this
    // file is safe to hand to anyone auditing what Aftermath touched.
    public static class SweepAudit
    {
        private static readonly object Lock = new object();

        private static string LogPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Aftermath\Sweep\audit.log");
            }
        }

        private static void EnsureDir()
        {
            string dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        public static void SweepStart(List<string> hosts)
        {
            lock (Lock)
            {
                try
                {
                    EnsureDir();
                    var sb = new StringBuilder();
                    sb.Append(Stamp());
                    sb.Append("  sweep-start  targets=" + hosts.Count + "  hosts=" + string.Join(",", hosts.ToArray()));
                    sb.Append(Environment.NewLine);
                    File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }

        public static void HostResult(string host, SweepStatus status, int findingCount)
        {
            lock (Lock)
            {
                try
                {
                    EnsureDir();
                    var sb = new StringBuilder();
                    sb.Append(Stamp());
                    sb.Append("  host=" + host + "  result=" + status.ToString() + "  findings=" + findingCount);
                    sb.Append(Environment.NewLine);
                    File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
