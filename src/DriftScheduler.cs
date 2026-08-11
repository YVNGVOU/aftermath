// Registers a daily Scheduled Task that runs Aftermath headlessly so Drift has
// a fresh baseline to compare against without the user remembering to scan.
// Pro+ feature - callers must check Entitlements.Current.HasScheduledDrift
// before offering this; this class itself does not gate, it only executes.
// Mirrors AutoTrigger.cs's schtasks/XML pattern, swapping the event trigger
// for a daily time trigger and --auto for --headless.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Aftermath
{
    public static class DriftScheduler
    {
        public const string TaskName = "AftermathDriftBaseline";

        public static bool IsRegistered()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks", "/query /tn \"" + TaskName + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(10000);
                return proc.ExitCode == 0;
            }
            catch { return false; }
        }

        public static bool Register()
        {
            string exe;
            try { exe = Process.GetCurrentProcess().MainModule.FileName; }
            catch { return false; }

            string xmlPath = null;
            try
            {
                xmlPath = Path.Combine(Path.GetTempPath(), "aftermath_drift_task_" + Guid.NewGuid().ToString("N") + ".xml");
                File.WriteAllText(xmlPath, BuildXml(exe), Encoding.Unicode);

                var psi = new ProcessStartInfo("schtasks",
                    "/create /tn \"" + TaskName + "\" /xml \"" + xmlPath + "\" /f");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(15000);
                return proc.ExitCode == 0;
            }
            catch { return false; }
            finally
            {
                try { if (xmlPath != null) File.Delete(xmlPath); } catch { }
            }
        }

        public static bool Unregister()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks", "/delete /tn \"" + TaskName + "\" /f");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(15000);
                return proc.ExitCode == 0;
            }
            catch { return false; }
        }

        private static string BuildXml(string exePath)
        {
            // Fires once a day at 09:00 local time; StartWhenAvailable catches the
            // machine up on the next logon/idle moment if it was off at 09:00.
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n");
            sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n");
            sb.Append("  <Triggers>\r\n");
            sb.Append("    <CalendarTrigger>\r\n");
            sb.Append("      <StartBoundary>2026-01-01T09:00:00</StartBoundary>\r\n");
            sb.Append("      <Enabled>true</Enabled>\r\n");
            sb.Append("      <ScheduleByDay>\r\n");
            sb.Append("        <DaysInterval>1</DaysInterval>\r\n");
            sb.Append("      </ScheduleByDay>\r\n");
            sb.Append("    </CalendarTrigger>\r\n");
            sb.Append("  </Triggers>\r\n");
            sb.Append("  <Principals>\r\n");
            sb.Append("    <Principal id=\"Author\">\r\n");
            sb.Append("      <RunLevel>HighestAvailable</RunLevel>\r\n");
            sb.Append("    </Principal>\r\n");
            sb.Append("  </Principals>\r\n");
            sb.Append("  <Settings>\r\n");
            sb.Append("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n");
            sb.Append("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n");
            sb.Append("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n");
            sb.Append("    <StartWhenAvailable>true</StartWhenAvailable>\r\n");
            sb.Append("  </Settings>\r\n");
            sb.Append("  <Actions Context=\"Author\">\r\n");
            sb.Append("    <Exec>\r\n");
            sb.Append("      <Command>\"" + exePath + "\"</Command>\r\n");
            sb.Append("      <Arguments>--headless</Arguments>\r\n");
            sb.Append("    </Exec>\r\n");
            sb.Append("  </Actions>\r\n");
            sb.Append("</Task>\r\n");
            return sb.ToString();
        }
    }
}
