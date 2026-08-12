// Registers a Scheduled Task that launches Aftermath automatically at sign-in,
// elevated. Same schtasks-with-an-XML-definition technique AutoTrigger.cs
// already uses - see that file's header comment for why schtasks needs XML
// here instead of a one-line /create.
//
// This is ONE toggle covering both "run on startup" and "run as admin"
// rather than two, because they can't honestly be separated: a plain
// HKCU\...\Run entry launches unelevated (most of Aftermath's checks need
// Administrator to mean anything), and there is no way to silently
// re-elevate an unelevated launch without a UAC prompt short of an
// embedded manifest baked in at compile time - which can't be a runtime
// Settings toggle. A logon-triggered task with RunLevel HighestAvailable is
// the one real mechanism that launches already-elevated with no prompt,
// so that's what this offers - not a second, half-working toggle that
// would just nag with UAC every sign-in.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Aftermath
{
    public static class LaunchAtLogon
    {
        public const string TaskName = "AftermathLaunchAtLogon";

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
                xmlPath = Path.Combine(Path.GetTempPath(), "aftermath_logon_task_" + Guid.NewGuid().ToString("N") + ".xml");
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
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n");
            sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n");
            sb.Append("  <Triggers>\r\n");
            sb.Append("    <LogonTrigger>\r\n");
            sb.Append("      <Enabled>true</Enabled>\r\n");
            sb.Append("    </LogonTrigger>\r\n");
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
            sb.Append("    </Exec>\r\n");
            sb.Append("  </Actions>\r\n");
            sb.Append("</Task>\r\n");
            return sb.ToString();
        }
    }
}
