// "How to fix this" text for DetectionWorkspace. Matched against the real
// Category/Title strings the Scanner methods in Program.cs/Deep.cs/
// Exposure.cs/ExternalAv.cs actually emit (grep `new Finding(` across those
// files before touching this - if a title pattern here stops matching what
// the scanners produce, the section silently falls back to generic advice
// instead of erroring, so it's easy for this file to drift out of sync).
//
// Same honesty rule as the scanners themselves: never claim Aftermath knows
// more about a finding than it does. Where nothing specific can be said,
// fall back to real-but-generic steps rather than inventing false certainty.

using System;
using System.Collections.Generic;

namespace Aftermath
{
    public static class RemediationGuide
    {
        public static List<string> StepsFor(Finding f)
        {
            var steps = new List<string>();
            if (f == null) return steps;

            string cat = f.Category ?? "";
            string title = f.Title ?? "";
            string tl = title.ToLowerInvariant();

            if (f.Removable)
            {
                steps.Add("Click \"Quarantine this item\" above - it's the fastest safe first step. Quarantined files are moved out of the way, not permanently deleted, so this can be undone if you were wrong.");
            }

            switch (cat)
            {
                case "Detections":
                    StepsForDetections(f, tl, steps);
                    break;

                case "Persistence":
                    StepsForPersistence(f, tl, steps);
                    break;

                case "Startup":
                    StepsForStartup(f, tl, steps);
                    break;

                case "Network":
                    StepsForNetwork(f, tl, steps);
                    break;

                case "System":
                    StepsForSystem(f, tl, steps);
                    break;

                case "Exposure":
                    StepsForExposure(f, tl, steps);
                    break;

                case "External AV":
                    StepsForExternalAv(f, tl, steps);
                    break;

                case "Artifacts":
                    StepsForArtifacts(f, tl, steps);
                    break;

                case "History":
                    StepsForHistory(f, tl, steps);
                    break;

                default:
                    StepsGeneric(steps);
                    break;
            }

            return steps;
        }

        // ---------- Detections (Windows Defender's own findings) ----------

        private static void StepsForDetections(Finding f, string tl, List<string> steps)
        {
            if (tl.IndexOf("could not read defender data") >= 0)
            {
                steps.Add("Aftermath could not query Defender - confirm Defender itself is running (Windows Security app -> Virus & threat protection).");
                steps.Add("If Defender was recently disabled or is missing entirely, that is itself worth investigating.");
                return;
            }

            steps.Add("This is Windows Defender's own detection, surfaced here for one place to review everything.");
            if (f.Removable)
                steps.Add("The file still exists on disk, so Quarantine (above) is the direct fix.");
            else
                steps.Add("The file no longer exists at its recorded path - Defender already removed or you already deleted it, so there's nothing left to quarantine here.");
            steps.Add("Run a full Windows Defender scan afterward (Windows Security -> Virus & threat protection -> Scan options -> Full scan) - Defender's own history log only shows what it already caught, not what might still be sitting undetected elsewhere.");
            steps.Add("If this keeps reappearing after removal, something is re-dropping it - check Startup, Persistence, and Network findings in this same scan for the mechanism.");
        }

        // ---------- Persistence (services / scheduled tasks / WMI) ----------

        private static void StepsForPersistence(Finding f, string tl, List<string> steps)
        {
            if (tl.StartsWith("service:"))
            {
                steps.Add("Open Services (services.msc) and locate the service by the name shown in the title.");
                steps.Add("Right-click -> Stop, then Properties -> set Startup type to Disabled.");
                steps.Add("Note the executable path from Evidence above, then delete that file (or quarantine it if Aftermath flagged it as Removable) once the service is stopped - disabling the service alone leaves the file behind.");
                steps.Add("Reboot and re-run a scan to confirm the service does not recreate itself; if it does, another persistence mechanism (a scheduled task or another service) is restoring it.");
            }
            else if (tl.StartsWith("wmi consumer:"))
            {
                steps.Add("This is a WMI event subscription (CommandLineEventConsumer / ActiveScriptEventConsumer) - a fileless technique that runs code when a system event fires, with no file on disk to quarantine.");
                steps.Add("Open an elevated PowerShell and inspect it: Get-WmiObject -Namespace root\\subscription -Class __EventConsumer");
                steps.Add("Remove the matching consumer, filter, and binding (all three, in root\\subscription), e.g. via Get-WmiObject ... | Remove-WmiObject for each of __EventConsumer, __EventFilter, and __FilterToConsumerBinding entries that reference it.");
                steps.Add("This is flagged Watchlist because it's rarely innocent - if you don't recognize it, treat it as an active compromise and consider a full Defender scan plus the steps for any other findings in this scan before trusting the machine again.");
            }
            else if (tl.StartsWith("scheduled task:"))
            {
                steps.Add("Open Task Scheduler (taskschd.msc).");
                steps.Add("Locate the task by the name shown in the title (it may be nested under a subfolder in Task Scheduler Library, not at the root).");
                steps.Add("Right-click -> Disable to confirm it stops the intended thing before permanently deleting; once confirmed, right-click -> Delete.");
                steps.Add("The action here runs a program from AppData or Temp - open the task's Actions tab first to see exactly what it launches, and quarantine or delete that target file too.");
                steps.Add("If it reappears after deletion, another persistence mechanism (a service, another task, or a WMI subscription) is recreating it - check the other Persistence findings in this scan.");
            }
            else
            {
                steps.Add("This is a persistence finding - something configured to survive a reboot or relaunch itself.");
                steps.Add("Note the exact name and path shown above before touching anything.");
                steps.Add("Search that exact name online or against a clean reference machine to confirm it isn't a known Windows or vendor component before removing it.");
                steps.Add("When in doubt, disable rather than delete first - that's reversible, permanent deletion isn't.");
            }
        }

        // ---------- Startup (Run keys / startup folder entries) ----------

        private static void StepsForStartup(Finding f, string tl, List<string> steps)
        {
            steps.Add("Open Task Manager -> Startup tab (or msconfig -> Startup on older Windows) and find the entry matching the name shown above.");
            steps.Add("Right-click -> Disable there first - it's reversible and stops it launching without touching the registry directly.");
            if (!string.IsNullOrEmpty(f.Path))
            {
                steps.Add("The underlying registry Run key or startup shortcut is: " + f.Path + " - once you've confirmed disabling it is safe, remove that entry directly with regedit or by deleting the shortcut.");
            }
            else
            {
                steps.Add("If it doesn't appear in the Startup tab, check the registry directly: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run and the matching HKLM key, plus the Startup folder (shell:startup).");
            }
            steps.Add("Evidence above notes the file's signature status (valid / unsigned / hash-mismatch / missing) - an unsigned or tampered signature on something you don't recognize is the strongest reason to remove it; a validly-signed, recognizable app is usually fine to leave or just disable.");
        }

        // ---------- Network (connections / listeners) ----------

        private static void StepsForNetwork(Finding f, string tl, List<string> steps)
        {
            if (tl.IndexOf("active connection") >= 0 || tl.IndexOf("snapshot only") >= 0)
            {
                steps.Add("This is a summary line, not an individual finding to act on - look at the other Network findings in this scan for specific flagged connections.");
                steps.Add("Remember this is a point-in-time snapshot: malware that already ran and exited won't show up here even if it did damage. Pair this with the Startup, Persistence, and Detections findings for the fuller picture.");
                return;
            }

            steps.Add("Open Resource Monitor (resmon.exe) -> Network tab, or run 'netstat -ano' in an elevated command prompt, and match the PID/port shown there to confirm what's currently connected.");
            if (!string.IsNullOrEmpty(f.Path))
                steps.Add("The offending process is at: " + f.Path + " - end the process in Task Manager (right-click -> End task), then quarantine or delete that file.");
            else
                steps.Add("Aftermath couldn't resolve a file path for this process - use the PID from netstat/Resource Monitor to find it in Task Manager's Details tab, right-click -> Open file location.");
            if (tl.StartsWith("listening:"))
                steps.Add("This process is accepting inbound connections from outside this PC. If you don't recognize it, block it at the firewall immediately (Windows Defender Firewall -> Advanced Settings -> Inbound Rules -> New Rule -> block the program or port) in addition to removing the file.");
            else
                steps.Add("If you don't recognize the remote address, block outbound traffic to it as a stopgap (Windows Defender Firewall -> Advanced Settings -> Outbound Rules) while you finish removing the underlying program.");
            steps.Add("It's running unsigned from a user-writable folder, which is unusual for legitimate software - that alone is reason to treat it as suspicious even before considering what it's connecting to.");
        }

        // ---------- System (proxy / hosts / updates / Defender exclusions / admin) ----------

        private static void StepsForSystem(Finding f, string tl, List<string> steps)
        {
            if (tl.IndexOf("proxy auto-config") >= 0 || tl.IndexOf("proxy is enabled") >= 0)
            {
                steps.Add("Open Settings -> Network & Internet -> Proxy.");
                steps.Add("Turn off \"Use setup script\" (clears the auto-config URL) and/or \"Use a proxy server\", unless you deliberately configured this yourself (e.g. for work VPN or a legitimate ad-blocking proxy).");
                steps.Add("An auto-config URL you didn't set is a classic traffic-interception technique - after clearing it, check your browser's saved passwords (see Exposure findings) since traffic may have been intercepted while it was active.");
            }
            else if (tl.IndexOf("office updates") >= 0 && tl.IndexOf("disabled") >= 0)
            {
                steps.Add("This is a hallmark of pirated Office installers - the crack disables updates so Microsoft can't patch it back to genuine.");
                steps.Add("Open an elevated command prompt in the Office ClickToRun folder and run: officec2rclient.exe /update user - or reinstall Office from a legitimate source if the installation itself is pirated.");
                steps.Add("Re-enabling updates alone doesn't fix a pirated installation; if you didn't legitimately license this Office install, plan to replace it.");
            }
            else if (tl.IndexOf("windows update automatic updates are switched off") >= 0)
            {
                steps.Add("Open Registry Editor (regedit) and go to HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU.");
                steps.Add("Delete the NoAutoUpdate value (or set it to 0), then reopen Settings -> Windows Update to confirm updates are no longer blocked by policy.");
                steps.Add("If you don't manage this PC via a company/school policy that legitimately sets this, treat it as tampering and check what else changed around the same time (see Persistence and Startup findings).");
            }
            else if (tl.IndexOf("defender has suspicious exclusions") >= 0)
            {
                steps.Add("Open Windows Security -> Virus & threat protection -> Manage settings -> Add or remove exclusions.");
                steps.Add("Remove every exclusion pointing at the paths listed in Evidence above - malware commonly adds itself as an exclusion so Defender stops scanning it.");
                steps.Add("After removing the exclusions, run a full Defender scan - anything hiding behind that exclusion has never actually been scanned.");
            }
            else if (tl.IndexOf("defender exclusion") >= 0 || tl.IndexOf("no defender exclusions") >= 0)
            {
                steps.Add("Informational only - the exclusions listed didn't match anything suspicious (user-writable folders or a bare drive root). No action needed unless you don't recognize why they're there.");
            }
            else if (tl.IndexOf("running without administrator") >= 0)
            {
                steps.Add("Restart Aftermath as Administrator (right-click -> Run as administrator) to unlock the Defender-exclusions and other-users checks this scan skipped.");
            }
            else if (tl.IndexOf("hosts file blocks security domains") >= 0)
            {
                steps.Add("Open C:\\Windows\\System32\\drivers\\etc\\hosts in Notepad (as Administrator).");
                steps.Add("Delete or comment out (prefix with #) the lines listed in Evidence above that redirect Microsoft, Windows Update, Defender, or AV vendor domains.");
                steps.Add("Save the file, then confirm Windows Update and your antivirus can reach the internet again - this is a common way malware blocks you from updating or scanning it away.");
            }
            else if (tl.IndexOf("defender real-time protection is off") >= 0)
            {
                steps.Add("Open Windows Security -> Virus & threat protection -> Manage settings, and turn Real-time protection back on.");
                steps.Add("If it immediately turns itself off again, or the toggle is greyed out, that's a strong sign something (malware, or an unmanaged policy) is actively suppressing it - a Group Policy or MDM conflict is the benign explanation, malware tampering is the other.");
            }
            else if (tl.IndexOf("proxy") >= 0 && tl.IndexOf("clean") >= 0)
            {
                steps.Add("Informational only - no proxy or auto-config redirection found. No action needed.");
            }
            else if (tl.IndexOf("hosts file clean") >= 0)
            {
                steps.Add("Informational only - no security-domain blocking found in the hosts file. No action needed.");
            }
            else
            {
                StepsGeneric(steps);
            }
        }

        // ---------- Exposure (credential stores potentially reachable by a stealer) ----------

        private static void StepsForExposure(Finding f, string tl, List<string> steps)
        {
            if (tl.IndexOf("no common credential stores found") >= 0)
            {
                steps.Add("Informational only - none of the usual browser password stores, wallets, or key files exist on this PC. No action needed.");
                return;
            }

            // Per-store headline findings carry their own tailored advice already,
            // written into Detail by Exposure.Scan - surface it explicitly here
            // rather than re-deriving separate copy that could drift from it.
            if (!string.IsNullOrEmpty(f.Detail))
                steps.Add("This finding's own guidance: " + f.Detail);

            steps.Add("Rotate any affected passwords/keys from a DIFFERENT, clean device - if this PC is still infected, typing the new password here lets a stealer capture it again immediately.");
            steps.Add("Enable two-factor authentication anywhere it's offered for these accounts, so a stolen password alone isn't enough.");
            steps.Add("For browser-saved passwords specifically: after changing them, also sign out of all sessions in the browser's account settings (e.g. Google Account -> Security -> \"Sign out of all sessions\") to invalidate any stolen session cookies, not just the password.");
            steps.Add("This finding only reports that the credential store exists and when it was last touched - Aftermath never opens or reads its contents, so treat \"present\" as \"assume potentially exposed,\" not confirmation anything was actually taken.");
        }

        // ---------- External AV (third-party antivirus visibility) ----------

        private static void StepsForExternalAv(Finding f, string tl, List<string> steps)
        {
            steps.Add("Aftermath can only see what Windows Security Center and the Application event log already know about this product - it cannot configure or repair another vendor's software directly.");

            if (tl.IndexOf("is installed but not active") >= 0)
            {
                steps.Add("Open that product's own application (not Windows Security) and check whether real-time/on-access protection is toggled off.");
                steps.Add("If you didn't disable it yourself, treat this as suspicious - disabling third-party AV on purpose is a common step malware takes right after it lands. Re-enable it, then run that product's own full scan.");
            }
            else if (tl.IndexOf("definitions are out of date") >= 0)
            {
                steps.Add("Open that product's own application and manually trigger a definitions/database update - it may be silently failing to auto-update (blocked network access, expired license, or a corrupted update service).");
                steps.Add("If updates keep failing, check the Network and System findings in this scan - a hijacked proxy or hosts-file block can be the reason it can't reach the vendor's update servers.");
            }
            else if (tl.IndexOf("is active and up to date") >= 0)
            {
                steps.Add("Informational only - Windows Security Center reports this product healthy. No action needed.");
            }
            else if (tl.IndexOf("scan error") >= 0)
            {
                steps.Add("Aftermath's own attempt to read AV status failed (see the error in Evidence above) - this is a limitation of this scan, not necessarily a problem with your antivirus itself.");
            }
            else
            {
                // Event-log entries: Title is the vendor's own event Source, quoted
                // verbatim in Detail - Aftermath doesn't interpret third-party text.
                steps.Add("This is quoted directly from that product's own entry in the Windows Application event log (Evidence above is its exact message, unedited) - review it inside that product's own console/history for full context and any recommended action it offers.");
            }
        }

        // ---------- Artifacts (cracked-software indicators on disk) ----------

        private static void StepsForArtifacts(Finding f, string tl, List<string> steps)
        {
            if (tl.IndexOf("disk image") >= 0)
            {
                steps.Add("Do not mount this file. Disk images (.iso/.img etc.) are a common way pirated software is distributed - mounting one and running its installer is the actual infection step.");
                steps.Add("Quarantine (above) removes the image itself without mounting it.");
            }
            else if (tl.IndexOf("crack") >= 0 || tl.IndexOf("keygen") >= 0)
            {
                steps.Add("The filename matches a known crack/keygen/repack naming pattern. These are one of the most common malware delivery vectors regardless of whether the crack itself \"worked.\"");
                steps.Add("Quarantine (above) is the direct fix. If you ran this file, also check the Startup, Persistence, and Detections findings in this scan for anything it may have dropped.");
            }
            else if (tl.IndexOf("tampered signed binary") >= 0)
            {
                steps.Add("This file carries a legitimate vendor signature, but its contents were modified after signing - the signature check passes at a glance while the file itself is not what the vendor shipped. Antivirus signature-allowlisting can miss this.");
                steps.Add("Quarantine (above) rather than trusting the signature. Re-download the genuine version from the vendor's own site if you need this program.");
            }
            else
            {
                steps.Add("Informational only - no cracked-software artifacts found in your user folders. No action needed.");
            }
        }

        // ---------- History (execution/download/install history - informational) ----------

        private static void StepsForHistory(Finding f, string tl, List<string> steps)
        {
            steps.Add("This is historical/informational - a record that something ran, was downloaded, or was installed, kept by Windows even after the file itself is gone. There is nothing here for Aftermath to remove.");
            steps.Add("The only \"fix\" is your own judgment: does this match something you actually did? If yes, no action needed.");
            steps.Add("If you don't recognize it, note the name, source, and date shown above and search for what it is before assuming the worst - then check whether it also shows up as a Detections, Startup, or Persistence finding elsewhere in this scan, which would mean it's still active rather than just history.");
        }

        // ---------- Fallback for anything not matched above ----------

        private static void StepsGeneric(List<string> steps)
        {
            steps.Add("Note the exact path and name shown above.");
            steps.Add("Search that exact file or registry name to check whether it's a known Windows or vendor component before removing anything.");
            steps.Add("When in doubt, quarantine rather than permanently delete - quarantine can be restored if you were wrong.");
        }
    }
}
