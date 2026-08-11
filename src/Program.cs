// Aftermath - post-infection triage for Windows
// Single-file WinForms app. Compiled with the in-box .NET Framework csc.exe,
// so the output runs on any Windows 10/11 machine with no install and no runtime download.
// C# 5 syntax only (no interpolation, no null-conditional) - the in-box compiler is old.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Aftermath
{
    // ---------- data model ----------

    public enum Sev { Ok, Info, Warn, Bad }

    public class Finding
    {
        public string Category;
        public string Title;
        public string Detail;
        public string Path;
        public Sev Severity;
        public bool Removable;
        public DateTime When = DateTime.MinValue;
        public int Count = 1;
        public bool Watchlist = false;   // "almost never changed innocently" - gets a marker in the UI

        public Finding(string cat, string title, string detail, string path, Sev sev, bool removable)
        {
            Category = cat; Title = title; Detail = detail;
            Path = path; Severity = sev; Removable = removable;
        }

        public string SevLabel
        {
            get
            {
                if (Severity == Sev.Bad) return "SERIOUS";
                if (Severity == Sev.Warn) return "CHECK";
                if (Severity == Sev.Ok) return "OK";
                return "";
            }
        }
    }

    public class TriageResult
    {
        public List<Finding> Findings = new List<Finding>();
        public List<DateTime> RecentDetectionTimes = new List<DateTime>();
        public bool RealtimeProtection = true;
        public int RecentSerious = 0;
        public List<DriftEntry> Drift = new List<DriftEntry>();   // filled by BaselineStore.Diff before rendering
        public bool FirstBaseline = false;                        // true when no earlier baseline.json existed

        public void Add(Finding f) { Findings.Add(f); }

        public IEnumerable<Finding> ByCategory(string c)
        {
            return Findings.Where(f => f.Category == c);
        }
    }

    // ---------- authenticode ----------

    public static class Signature
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private const uint TRUST_E_NOSIGNATURE = 0x800B0100;
        private const uint TRUST_E_BAD_DIGEST = 0x80096010;
        private const uint CERT_E_UNTRUSTEDROOT = 0x800B0109;
        private const uint CERT_E_EXPIRED = 0x800B0101;
        private const uint TRUST_E_SUBJECT_FORM_UNKNOWN = 0x800B0003;

        // Embedded-signature check only. FAST, and correct for the thing we care
        // about most: HashMismatch (signed, then modified).
        //
        // It CANNOT see catalog signatures, which is how most Windows system files
        // are signed - those come back "NotSigned" here. Never report that verdict
        // to a user directly; route it through Catalog.Resolve first.
        public static string CheckEmbedded(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Missing";
            if (!File.Exists(path)) return "Missing";

            var fileInfo = new WINTRUST_FILE_INFO();
            fileInfo.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            fileInfo.pcwszFilePath = path;
            fileInfo.hFile = IntPtr.Zero;
            fileInfo.pgKnownSubject = IntPtr.Zero;

            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            Marshal.StructureToPtr(fileInfo, pFile, false);

            var data = new WINTRUST_DATA();
            data.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
            data.dwUIChoice = 2;            // WTD_UI_NONE
            data.fdwRevocationChecks = 0;   // skip revocation: offline-safe and much faster
            data.dwUnionChoice = 1;         // WTD_CHOICE_FILE
            data.pFile = pFile;
            data.dwStateAction = 0;
            data.dwProvFlags = 0x00000010;  // WTD_SAFER_FLAG

            IntPtr pData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA)));
            Marshal.StructureToPtr(data, pData, false);

            uint result;
            try { result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, pData); }
            catch { return "Unknown"; }
            finally
            {
                Marshal.FreeHGlobal(pData);
                Marshal.FreeHGlobal(pFile);
            }

            if (result == 0) return "Valid";
            if (result == TRUST_E_NOSIGNATURE) return "NotSigned";
            if (result == TRUST_E_SUBJECT_FORM_UNKNOWN) return "NotSigned";
            if (result == TRUST_E_BAD_DIGEST) return "HashMismatch";
            if (result == CERT_E_UNTRUSTEDROOT) return "UntrustedRoot";
            if (result == CERT_E_EXPIRED) return "Expired";
            return "Unknown";
        }
    }

    // Catalog signatures live in separate .cat files, so an embedded-signature check
    // reports most Windows system binaries as unsigned. Rather than reimplement
    // catalog verification with fragile P/Invoke, ask Windows' own implementation
    // (Get-AuthenticodeSignature) - batched into ONE call so it stays fast.
    public static class Catalog
    {
        public static Dictionary<string, string> Resolve(IEnumerable<string> paths)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var list = paths.Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0) return map;

            string listFile = null, outFile = null;
            try
            {
                listFile = Path.Combine(Path.GetTempPath(), "aftermath_paths_" + Guid.NewGuid().ToString("N") + ".txt");
                outFile = Path.Combine(Path.GetTempPath(), "aftermath_sigs_" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllLines(listFile, list.ToArray(), Encoding.UTF8);

                string script =
                    "Get-Content -LiteralPath '" + listFile + "' -Encoding UTF8 | ForEach-Object { " +
                    "  $p = $_; " +
                    "  try { $s = Get-AuthenticodeSignature -LiteralPath $p -ErrorAction Stop; " +
                    "        $st = $s.Status.ToString(); " +
                    "        $sn = ''; if ($s.SignerCertificate) { $sn = $s.SignerCertificate.Subject } " +
                    "        \"$p`t$st`t$sn\" } " +
                    "  catch { \"$p`tUnknown`t\" } " +
                    "} | Set-Content -LiteralPath '" + outFile + "' -Encoding UTF8";

                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    if (!proc.WaitForExit(60000)) { try { proc.Kill(); } catch { } return map; }
                }

                if (File.Exists(outFile))
                {
                    foreach (var line in File.ReadAllLines(outFile, Encoding.UTF8))
                    {
                        var parts = line.Split('\t');
                        if (parts.Length < 2) continue;
                        string signer = parts.Length > 2 ? parts[2] : "";
                        map[parts[0]] = parts[1] + "\u001F" + signer;
                    }
                }
            }
            catch { }
            finally
            {
                try { if (listFile != null) File.Delete(listFile); } catch { }
                try { if (outFile != null) File.Delete(outFile); } catch { }
            }
            return map;
        }

        public static string FriendlyName(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return "";
            foreach (var part in subject.Split(','))
            {
                var t = part.Trim();
                if (t.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    return t.Substring(3).Trim('"');
            }
            return "";
        }
    }

    // ---------- protected paths ----------

    public static class Guard
    {
        // Refuses to recurse-delete these regardless of what the UI asks.
        // Born from a real incident: a pirated Office 2019 was layered on top of a
        // legitimate Microsoft 365 install sharing the same folder. Deleting the
        // folder would have destroyed software the user had paid for.
        private static readonly string[] Protected = new string[]
        {
            @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)",
            @"C:\ProgramData", @"C:\Users", @"C:\"
        };

        public static bool IsProtected(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            string full;
            try { full = Path.GetFullPath(path).TrimEnd('\\'); }
            catch { return true; }

            foreach (var p in Protected)
                if (string.Equals(full, p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;

            string userRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
            if (string.Equals(full, userRoot, StringComparison.OrdinalIgnoreCase)) return true;

            if (full.IndexOf(@"\Microsoft Office", StringComparison.OrdinalIgnoreCase) >= 0 &&
                full.IndexOf("Program Files", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            // Never offer to delete inside Windows itself.
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (full.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }
    }

    // ---------- scanners ----------

    public static class Scanner
    {
        private const string DefenderNs = @"root\Microsoft\Windows\Defender";
        private const int RecentDays = 30;

        private class Det
        {
            public string Key;
            public string Resources;
            public string PrimaryPath;
            public string ThreatId;
            public DateTime Latest;
            public int Count;
        }

        public static void Defender(TriageResult r, Action<string> log)
        {
            log("Reading Defender detection history...");

            var names = new Dictionary<string, string>();
            var sevs = new Dictionary<string, string>();

            try
            {
                var scope = new ManagementScope(DefenderNs);
                scope.Connect();

                // Threat catalogue first, so detections can be shown by NAME rather than a bare ID.
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpThreat")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        try
                        {
                            var id = Convert.ToString(mo["ThreatID"]);
                            var nm = Convert.ToString(mo["ThreatName"]);
                            var sv = Convert.ToString(mo["SeverityID"]);
                            if (!string.IsNullOrEmpty(id))
                            {
                                names[id] = nm;
                                sevs[id] = sv;
                            }
                        }
                        catch { }
                    }
                }

                // Detections, deduplicated. Defender records one row per re-scan, so the
                // same file can appear a dozen times; raw output buries the real picture.
                var grouped = new Dictionary<string, Det>();

                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpThreatDetection")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        DateTime when = DateTime.MinValue;
                        try
                        {
                            var raw = mo["InitialDetectionTime"];
                            if (raw != null) when = ManagementDateTimeConverter.ToDateTime(raw.ToString());
                        }
                        catch { }

                        string resources = "";
                        try
                        {
                            var res = mo["Resources"] as string[];
                            if (res != null) resources = string.Join(" | ", res);
                        }
                        catch { }

                        string tid = "";
                        try { tid = Convert.ToString(mo["ThreatID"]); }
                        catch { }

                        string primary = FirstPath(resources);
                        // Windows paths are case-insensitive, and Defender records them
                        // with whatever casing the caller used - "D:\Setup.exe" and
                        // "D:\setup.exe" are one file, not two.
                        string key = tid + "||" + (primary == null ? resources : primary).ToLowerInvariant();

                        Det d;
                        if (!grouped.TryGetValue(key, out d))
                        {
                            d = new Det();
                            d.Key = key; d.ThreatId = tid; d.Resources = resources;
                            d.PrimaryPath = primary; d.Latest = when; d.Count = 0;
                            grouped[key] = d;
                        }
                        d.Count++;
                        if (when > d.Latest) d.Latest = when;
                    }
                }

                var cutoff = DateTime.Now.AddDays(-RecentDays);

                foreach (var d in grouped.Values.OrderByDescending(x => x.Latest))
                {
                    string name = "Threat " + d.ThreatId;
                    if (!string.IsNullOrEmpty(d.ThreatId) && names.ContainsKey(d.ThreatId))
                        name = names[d.ThreatId];

                    bool recent = d.Latest >= cutoff;
                    bool highSev = false;
                    if (!string.IsNullOrEmpty(d.ThreatId) && sevs.ContainsKey(d.ThreatId))
                    {
                        var sv = sevs[d.ThreatId];
                        highSev = (sv == "4" || sv == "5");
                    }

                    // Recency matters. A PUA bundler from last year is history, not an emergency.
                    Sev sev;
                    if (recent && highSev) sev = Sev.Bad;
                    else if (recent) sev = Sev.Warn;
                    else if (highSev) sev = Sev.Warn;
                    else sev = Sev.Info;

                    var detail = new StringBuilder();
                    detail.Append(d.Latest == DateTime.MinValue ? "unknown time" : d.Latest.ToString("g", CultureInfo.CurrentCulture));
                    if (!recent) detail.Append("  (historical)");
                    if (d.Count > 1) detail.Append("  -  seen " + d.Count + " times");
                    detail.Append("  -  ");
                    detail.Append(d.Resources);

                    bool exists = d.PrimaryPath != null && (File.Exists(d.PrimaryPath) || Directory.Exists(d.PrimaryPath));

                    var f = new Finding("Detections", name, detail.ToString(), d.PrimaryPath, sev, exists);
                    f.When = d.Latest;
                    f.Count = d.Count;
                    r.Add(f);

                    if (recent && highSev)
                    {
                        r.RecentSerious++;
                        r.RecentDetectionTimes.Add(d.Latest);
                    }
                }

                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpComputerStatus")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        bool rtp = true;
                        try { rtp = Convert.ToBoolean(mo["RealTimeProtectionEnabled"]); } catch { }
                        r.RealtimeProtection = rtp;
                    }
                }
            }
            catch (Exception ex)
            {
                r.Add(new Finding("Detections", "Could not read Defender data", ex.Message, null, Sev.Warn, false));
            }
        }

        private static string FirstPath(string resources)
        {
            if (string.IsNullOrEmpty(resources)) return null;
            foreach (var part in resources.Split('|'))
            {
                var p = part.Trim();
                int idx = p.IndexOf("file:_", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) p = p.Substring(idx + 6).Trim();
                else continue;
                if (p.Length > 3 && p[1] == ':') return p;
            }
            return null;
        }

        // Only these extensions can carry a payload. Restricting keyword matching to
        // them removes an entire class of false positive - preset files, logs and
        // documents with words like "crack" in their names.
        private static readonly string[] RiskyExts = new string[]
        {
            ".exe", ".msi", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".scr",
            ".zip", ".rar", ".7z", ".iso", ".img", ".vhd", ".vhdx"
        };

        private static readonly string[] ImageExts = new string[] { ".iso", ".img", ".vhd", ".vhdx" };

        private static readonly string[] Keywords = new string[]
        {
            "crack", "keygen", "kms", "autokms", "ratiborus", "repack",
            "activator", "keymaker", "genp", "nulled", "warez"
        };

        // A keyword only counts when it stands as its own word. Without this,
        // "TexturePacker" matches "repack" (tex-turepack-er) and every Visual Studio
        // log matches "activat" via "AlwaysActivate".
        private static bool WordMatch(string haystack, string needle)
        {
            int start = 0;
            while (true)
            {
                int i = haystack.IndexOf(needle, start, StringComparison.Ordinal);
                if (i < 0) return false;
                bool leftOk = (i == 0) || !char.IsLetter(haystack[i - 1]);
                int end = i + needle.Length;
                bool rightOk = (end >= haystack.Length) || !char.IsLetter(haystack[end]);
                if (leftOk && rightOk) return true;
                start = i + 1;
            }
        }

        private static readonly string[] SkipDirs = new string[]
        {
            "node_modules", "\\.git", "\\image-line", "\\vslogs", "\\packages",
            "\\.vs", "\\site-packages", "\\dist-info", "\\.cache", "\\wallpaper_engine"
        };

        private static bool ShouldSkipDir(string dir)
        {
            var low = dir.ToLowerInvariant();
            foreach (var s in SkipDirs) if (low.Contains(s)) return true;
            return false;
        }

        public static void Artifacts(TriageResult r, Action<string> log)
        {
            log("Hunting cracked-software artifacts...");

            var roots = new List<string>();
            foreach (var f in new Environment.SpecialFolder[]
            {
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.MyVideos
            })
            {
                try { roots.Add(Environment.GetFolderPath(f)); } catch { }
            }
            try { roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")); }
            catch { }
            try { roots.Add(Path.GetTempPath()); } catch { }

            // Custom scan profiles (Max+): user-added folders on top of the fixed
            // roots above. Silently skipped below Max rather than erroring - a
            // downgraded license just stops extending the scan.
            if (Entitlements.Current.HasCustomScanProfiles)
            {
                foreach (var p in ScanProfileStore.Load()) roots.Add(p);
            }

            DateTime? focus = null;
            if (r.RecentDetectionTimes.Count > 0) focus = r.RecentDetectionTimes.Max();

            int found = 0;
            foreach (var root in roots.Distinct())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                log("  scanning " + root);
                Walk(root, 3, r, focus, ref found);
            }

            if (found == 0)
                r.Add(new Finding("Artifacts", "No cracked-software artifacts found",
                    "No disk images, crack/keygen filenames, or tampered installers in your user folders.",
                    null, Sev.Ok, false));
        }

        private static void Walk(string dir, int depth, TriageResult r, DateTime? focus, ref int found)
        {
            if (depth < 0) return;
            if (ShouldSkipDir(dir)) return;

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { return; }

            foreach (var f in files)
            {
                string name, ext;
                try
                {
                    name = Path.GetFileName(f).ToLowerInvariant();
                    ext = Path.GetExtension(f).ToLowerInvariant();
                }
                catch { continue; }

                if (Array.IndexOf(RiskyExts, ext) < 0) continue;   // only payload-capable files

                if (Array.IndexOf(ImageExts, ext) >= 0)
                {
                    r.Add(new Finding("Artifacts", "Disk image",
                        "Disk images are how pirated software is usually delivered. Mounting one runs its installer.",
                        f, Sev.Warn, true));
                    found++;
                    continue;
                }

                bool keywordHit = false;
                foreach (var k in Keywords) { if (WordMatch(name, k)) { keywordHit = true; break; } }

                if (keywordHit)
                {
                    r.Add(new Finding("Artifacts", "Crack / keygen filename",
                        "Filename matches a known crack, keygen or repack pattern.",
                        f, Sev.Warn, true));
                    found++;
                    continue;
                }

                if (ext == ".exe" || ext == ".msi" || ext == ".dll")
                {
                    bool nearFocus = true;
                    if (focus.HasValue)
                    {
                        try { nearFocus = Math.Abs((File.GetCreationTime(f) - focus.Value).TotalDays) < 3; }
                        catch { }
                    }
                    if (nearFocus && Signature.CheckEmbedded(f) == "HashMismatch")
                    {
                        r.Add(new Finding("Artifacts", "Tampered signed binary",
                            "Carries a vendor signature, but the file was modified after signing. Antivirus may not flag this. Treat as hostile.",
                            f, Sev.Bad, true));
                        found++;
                    }
                }
            }

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { return; }
            foreach (var s in subs)
            {
                try
                {
                    var di = new DirectoryInfo(s);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { continue; }
                Walk(s, depth - 1, r, focus, ref found);
            }
        }

        public static void Startup(TriageResult r, Action<string> log)
        {
            log("Auditing startup entries...");

            var entries = new List<string[]>();   // {label, command, exePath}

            var keys = new List<string[]>
            {
                new string[] { "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run" },
                new string[] { "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" },
                new string[] { "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run" },
                new string[] { "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" },
                new string[] { "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" }
            };

            foreach (var k in keys)
            {
                RegistryKey key = null;
                try
                {
                    key = (k[0] == "HKCU")
                        ? Registry.CurrentUser.OpenSubKey(k[1])
                        : Registry.LocalMachine.OpenSubKey(k[1]);
                    if (key == null) continue;

                    foreach (var valueName in key.GetValueNames())
                    {
                        var cmd = Convert.ToString(key.GetValue(valueName));
                        entries.Add(new string[] { k[0] + "\\" + valueName, cmd, ExtractPath(cmd) });
                    }
                }
                catch { }
                finally { if (key != null) key.Dispose(); }
            }

            foreach (var sf in new Environment.SpecialFolder[]
            {
                Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup
            })
            {
                try
                {
                    var dir = Environment.GetFolderPath(sf);
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        var n = Path.GetFileName(f);
                        // desktop.ini is folder metadata, not a startup program.
                        if (string.Equals(n, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        entries.Add(new string[] { "Startup folder\\" + n, f, f });
                    }
                }
                catch { }
            }

            log("Verifying signatures (including catalog-signed system files)...");
            var sigMap = Catalog.Resolve(entries.Select(e => e[2]));

            foreach (var e in entries)
            {
                string status = "Unknown", signer = "";
                string exe = e[2];

                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) status = "Missing";
                else if (sigMap.ContainsKey(exe))
                {
                    var parts = sigMap[exe].Split('\u001F');
                    status = parts[0];
                    signer = Catalog.FriendlyName(parts.Length > 1 ? parts[1] : "");
                }
                else status = Signature.CheckEmbedded(exe);

                Sev sev = Sev.Info;
                string note;

                if (status == "Missing")
                {
                    sev = Sev.Warn;
                    note = "Points at a file that no longer exists. Dead entry - safe to remove.";
                }
                else if (status == "HashMismatch")
                {
                    sev = Sev.Bad;
                    note = "Signed, but modified after signing. Suspicious.";
                }
                else if (status == "Valid")
                {
                    sev = Sev.Ok;
                    note = string.IsNullOrEmpty(signer) ? "Signature valid." : "Signed by " + signer + ".";
                }
                else if (status == "NotSigned" || status == "UnknownError")
                {
                    sev = Sev.Info;
                    note = "Unsigned. Common for indie tools (game mods, RGB software, sideloaders) - not suspicious on its own.";
                }
                else
                {
                    sev = Sev.Info;
                    note = "Signature status: " + status + ".";
                }

                var find = new Finding("Startup", e[0], note + "   ->  " + e[1], null, sev, false);
                r.Add(find);
            }
        }

        private static string ExtractPath(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            cmd = cmd.Trim();
            if (cmd.StartsWith("\""))
            {
                int close = cmd.IndexOf('"', 1);
                if (close > 1) return cmd.Substring(1, close - 1);
            }
            int space = cmd.IndexOf(' ');
            if (space > 0) return cmd.Substring(0, space);
            return cmd;
        }

        public static void SystemChecks(TriageResult r, Action<string> log)
        {
            log("Checking proxy and hosts file...");

            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    if (k != null)
                    {
                        var enable = Convert.ToString(k.GetValue("ProxyEnable"));
                        var server = Convert.ToString(k.GetValue("ProxyServer"));
                        var pac = Convert.ToString(k.GetValue("AutoConfigURL"));

                        if (!string.IsNullOrEmpty(pac))
                        {
                            var fPac = new Finding("System", "Proxy auto-config URL is set",
                                "AutoConfigURL = " + pac + "  -  a classic traffic-interception trick. If you did not set this, it is hostile.",
                                null, Sev.Bad, false);
                            fPac.Watchlist = true;   // proxy hijack - almost never set innocently
                            r.Add(fPac);
                        }
                        else if (enable == "1" && !string.IsNullOrEmpty(server))
                            r.Add(new Finding("System", "A proxy is enabled",
                                "ProxyServer = " + server + "  -  make sure you set this yourself.", null, Sev.Warn, false));
                        else
                            r.Add(new Finding("System", "Proxy settings clean",
                                "No proxy and no auto-config URL. Your traffic is not being redirected.", null, Sev.Ok, false));
                    }
                }
            }
            catch (Exception ex)
            {
                r.Add(new Finding("System", "Proxy check failed", ex.Message, null, Sev.Warn, false));
            }

            try
            {
                var hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
                if (File.Exists(hosts))
                {
                    var bad = new List<string>();
                    foreach (var line in File.ReadAllLines(hosts))
                    {
                        var t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        var low = t.ToLowerInvariant();
                        if (low.Contains("microsoft.com") || low.Contains("windowsupdate") ||
                            low.Contains("defender") || low.Contains("avast") || low.Contains("malwarebytes"))
                            bad.Add(t);
                    }
                    if (bad.Count > 0)
                    {
                        var fHosts = new Finding("System", "Hosts file blocks security domains",
                            "Malware does this to stop you updating or scanning:  " + string.Join("   ", bad.ToArray()),
                            hosts, Sev.Bad, false);
                        fHosts.Watchlist = true;   // hosts blocking a security domain - almost never innocent
                        r.Add(fHosts);
                    }
                    else
                        r.Add(new Finding("System", "Hosts file clean",
                            "No Windows Update or antivirus domains are blocked.", hosts, Sev.Ok, false));
                }
            }
            catch (Exception ex)
            {
                r.Add(new Finding("System", "Hosts check failed", ex.Message, null, Sev.Warn, false));
            }

            var fRtp = new Finding("System",
                r.RealtimeProtection ? "Defender real-time protection is ON" : "Defender real-time protection is OFF",
                r.RealtimeProtection
                    ? "Defender is watching this machine."
                    : "Real-time protection is disabled. Malware often turns this off - switch it back on.",
                null, r.RealtimeProtection ? Sev.Ok : Sev.Bad, false);
            if (!r.RealtimeProtection) fRtp.Watchlist = true;   // RTP switching off is almost never innocent
            r.Add(fRtp);
        }
    }

    // ---------- the escalation ladder ----------

    public static class Remover
    {
        public class Outcome
        {
            public bool Success;
            public string Method = "none";
            public string Message = "";
            public bool NeedsReboot;
        }

        // Anti-malware filter drivers can make file operations hang forever instead of
        // returning an error. Every step here is bounded so the UI can never freeze.
        private static bool RunBounded(Action action, int millis)
        {
            Exception captured = null;
            var done = new ManualResetEvent(false);
            var t = new Thread(delegate ()
            {
                try { action(); }
                catch (Exception ex) { captured = ex; }
                finally { done.Set(); }
            });
            t.IsBackground = true;
            t.Start();

            if (!done.WaitOne(millis)) return false;   // hung - move to the next rung
            return captured == null;
        }

        private static bool Gone(string path)
        {
            return !File.Exists(path) && !Directory.Exists(path);
        }

        public static Outcome Delete(string path, int timeoutMs)
        {
            var o = new Outcome();

            if (string.IsNullOrEmpty(path)) { o.Message = "No path given."; return o; }
            if (Guard.IsProtected(path))
            {
                o.Message = "Refused: protected system location. Aftermath will not delete this.";
                return o;
            }
            if (Gone(path))
            {
                o.Success = true; o.Method = "already gone"; o.Message = "Already gone.";
                return o;
            }

            bool isDir = Directory.Exists(path);

            // Rung 1: ordinary delete.
            RunBounded(delegate () { if (isDir) Directory.Delete(path, true); else File.Delete(path); }, timeoutMs);
            if (Gone(path)) { o.Success = true; o.Method = "direct delete"; o.Message = "Deleted."; return o; }

            // Rung 2: clear read-only / hidden / system attributes, retry.
            RunBounded(delegate ()
            {
                if (isDir)
                {
                    foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                        File.SetAttributes(f, FileAttributes.Normal);
                }
                else File.SetAttributes(path, FileAttributes.Normal);
            }, 10000);

            RunBounded(delegate () { if (isDir) Directory.Delete(path, true); else File.Delete(path); }, timeoutMs);
            if (Gone(path)) { o.Success = true; o.Method = "attribute reset"; o.Message = "Deleted after clearing attributes."; return o; }

            // Rung 3: robocopy mirror-from-empty. Beats long paths, odd names, deep trees.
            if (isDir)
            {
                string empty = Path.Combine(Path.GetTempPath(), "aftermath_empty_" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(empty);
                    var psi = new ProcessStartInfo("robocopy",
                        "\"" + empty + "\" \"" + path + "\" /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    var proc = Process.Start(psi);
                    if (proc != null && !proc.WaitForExit(timeoutMs)) { try { proc.Kill(); } catch { } }
                    RunBounded(delegate () { Directory.Delete(path, true); }, 15000);
                }
                catch { }
                finally { try { Directory.Delete(empty, true); } catch { } }

                if (Gone(path)) { o.Success = true; o.Method = "robocopy mirror"; o.Message = "Deleted using robocopy mirror-wipe."; return o; }
            }

            // Rung 4: schedule for next logon. Nothing holds the file at boot.
            // This is what beats filter-driver locks, where delete hangs forever
            // instead of failing.
            try
            {
                string cmdPath = Path.Combine(Path.GetTempPath(),
                    "aftermath_cleanup_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".cmd");
                string taskName = "AftermathCleanup_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("timeout /t 20 /nobreak >nul");
                sb.AppendLine(isDir ? "rmdir /s /q \"" + path + "\"" : "del /f /q \"" + path + "\"");
                sb.AppendLine("schtasks /delete /tn \"" + taskName + "\" /f");
                sb.AppendLine("del /f /q \"%~f0\"");
                File.WriteAllText(cmdPath, sb.ToString(), Encoding.ASCII);

                var psi = new ProcessStartInfo("schtasks",
                    "/create /tn \"" + taskName + "\" /tr \"cmd.exe /c \\\"" + cmdPath + "\\\"\" /sc onlogon /f");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(20000);
                    if (proc.ExitCode == 0)
                    {
                        o.Success = true;
                        o.NeedsReboot = true;
                        o.Method = "scheduled for next logon";
                        o.Message = "Locked by a driver. Scheduled for deletion at next logon - restart to finish. The task removes itself afterwards.";
                        return o;
                    }
                }
            }
            catch (Exception ex)
            {
                o.Message = "Scheduling failed: " + ex.Message;
                return o;
            }

            o.Message = "Could not delete. The file is locked and scheduling failed.";
            return o;
        }
    }
}

