// Registers a Scheduled Task that launches Aftermath automatically once Windows
// Defender finishes a scan, so triage runs right after the antivirus does rather
// than needing the user to remember. schtasks cannot express an event trigger on
// its own command line, so this hands it a small XML task definition instead.
// Needs Administrator - creating an event-triggered task does.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Aftermath
{
    public static class AutoTrigger
    {
        public const string TaskName = "AftermathPostScan";

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

        // Fires on Defender's Operational log, event ID 1116 (threat detected) or
        // 1117 (action taken). The action re-launches this same exe with --auto.
        public static bool Register()
        {
            string exe;
            try { exe = Process.GetCurrentProcess().MainModule.FileName; }
            catch { return false; }

            string xmlPath = null;
            try
            {
                xmlPath = Path.Combine(Path.GetTempPath(), "aftermath_task_" + Guid.NewGuid().ToString("N") + ".xml");
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
            var sb = new StringBuilder();
            string query = "&lt;QueryList&gt;&lt;Query Id=\"0\" Path=\"Microsoft-Windows-Windows Defender/Operational\"&gt;"
                + "&lt;Select Path=\"Microsoft-Windows-Windows Defender/Operational\"&gt;"
                + "*[System[(EventID=1116 or EventID=1117)]]&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;";

            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n");
            sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n");
            sb.Append("  <Triggers>\r\n");
            sb.Append("    <EventTrigger>\r\n");
            sb.Append("      <Enabled>true</Enabled>\r\n");
            sb.Append("      <Subscription>" + query + "</Subscription>\r\n");
            sb.Append("    </EventTrigger>\r\n");
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
            sb.Append("      <Arguments>--auto</Arguments>\r\n");
            sb.Append("    </Exec>\r\n");
            sb.Append("  </Actions>\r\n");
            sb.Append("</Task>\r\n");
            return sb.ToString();
        }
    }
}
