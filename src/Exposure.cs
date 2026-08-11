// Two questions a post-infection tool should answer but almost none do:
//
//   1. What could have been stolen, so the user knows what to change first.
//   2. What actually ran, and where did it come from - even after the file is gone.
//
// Both are read-only. Aftermath never opens or decrypts a credential store; it only
// reports that one exists and when it was last touched.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace Aftermath
{
    public static class Exposure
    {
        private class Target
        {
            public string Name;
            public string RelPath;
            public bool UnderRoaming;   // %APPDATA% vs %LOCALAPPDATA%
            public string Advice;

            public Target(string name, string rel, bool roaming, string advice)
            {
                Name = name; RelPath = rel; UnderRoaming = roaming; Advice = advice;
            }
        }

        // Ordered roughly by how much damage losing it does.
        private static readonly Target[] Targets = new Target[]
        {
            new Target("Chrome saved passwords",  @"Google\Chrome\User Data\Default\Login Data", false, "Change passwords for anything saved in Chrome."),
            new Target("Chrome cookies",          @"Google\Chrome\User Data\Default\Network\Cookies", false, "Sign out everywhere, so stolen session cookies stop working."),
            new Target("Edge saved passwords",    @"Microsoft\Edge\User Data\Default\Login Data", false, "Change passwords saved in Edge."),
            new Target("Edge cookies",            @"Microsoft\Edge\User Data\Default\Network\Cookies", false, "Sign out everywhere."),
            new Target("Brave saved passwords",   @"BraveSoftware\Brave-Browser\User Data\Default\Login Data", false, "Change passwords saved in Brave."),
            new Target("Opera GX passwords",      @"Programs\Opera GX\..\..\Opera Software\Opera GX Stable\Login Data", false, "Change passwords saved in Opera GX."),
            new Target("Firefox logins",          @"Mozilla\Firefox\Profiles", true,  "Change passwords saved in Firefox, and set a Primary Password."),
            new Target("Discord token store",     @"discord\Local Storage\leveldb", true,  "Change your Discord password - that also invalidates the stolen token."),
            new Target("Steam login sessions",    @"..\..\Program Files (x86)\Steam\config", true, "Steam Guard should catch reuse, but change your password."),
            new Target("Exodus wallet",           @"Exodus", true,  "Move funds to a new wallet immediately. Assume the seed is compromised."),
            new Target("Electrum wallet",         @"Electrum\wallets", true, "Move funds to a new wallet immediately."),
            new Target("SSH private keys",        @"..\.ssh", true,  "Rotate these keys and remove the old public keys from every server."),
            new Target("AWS credentials",         @"..\.aws\credentials", true, "Rotate these access keys in the AWS console now."),
            new Target("FileZilla saved sites",   @"FileZilla\sitemanager.xml", true, "FileZilla stores FTP passwords in plain text. Change them all."),
        };

        public static void Scan(TriageResult r, Action<string> log)
        {
            log("Checking what a password stealer could have reached...");

            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            DateTime? infection = null;
            if (r.RecentDetectionTimes.Count > 0) infection = r.RecentDetectionTimes.Max();

            int present = 0, touched = 0;

            foreach (var t in Targets)
            {
                string full;
                try { full = Path.GetFullPath(Path.Combine(t.UnderRoaming ? roaming : local, t.RelPath)); }
                catch { continue; }

                bool exists = File.Exists(full) || Directory.Exists(full);
                if (!exists) continue;
                present++;

                DateTime modified = DateTime.MinValue;
                try
                {
                    modified = Directory.Exists(full)
                        ? Directory.GetLastWriteTime(full)
                        : File.GetLastWriteTime(full);
                }
                catch { }

                // "Touched around the infection" is suggestive, not proof: browsers
                // rewrite these constantly. Say so rather than implying certainty.
                bool nearInfection = false;
                if (infection.HasValue && modified != DateTime.MinValue)
                    nearInfection = Math.Abs((modified - infection.Value).TotalHours) < 24;

                if (nearInfection) touched++;

                var detail = new StringBuilder();
                detail.Append("Present on this PC");
                if (modified != DateTime.MinValue)
                    detail.Append(", last changed " + modified.ToString("g", CultureInfo.CurrentCulture));
                if (nearInfection) detail.Append("  -  within a day of the infection");
                detail.Append(".   ");
                detail.Append(t.Advice);

                var fx = new Finding("Exposure", t.Name, detail.ToString(), full,
                    nearInfection ? Sev.Warn : Sev.Info, false);
                fx.When = modified;
                r.Add(fx);
            }

            if (present == 0)
            {
                r.Add(new Finding("Exposure", "No common credential stores found",
                    "None of the usual browser password stores, wallets or key files are on this PC.",
                    null, Sev.Ok, false));
                return;
            }

            string headline = present + " credential store(s) exist on this PC";
            string advice = infection.HasValue
                ? "If something ran here, assume everything above was readable. Change those passwords from a DIFFERENT device - a stealer on this machine can capture the new one as you type it."
                : "No recent infection detected, so this is informational: these are simply what a stealer would go after.";

            if (touched > 0)
                headline = present + " credential store(s), " + touched + " changed around the infection";

            r.Add(new Finding("Exposure", headline, advice, null,
                infection.HasValue ? Sev.Bad : Sev.Info, false));
        }
    }

    public static class History
    {
        // Windows keeps several records of what has been executed. These survive the
        // file being deleted, which is exactly when you need them.
        public static void Scan(TriageResult r, Action<string> log)
        {
            log("Reading execution history and download origins...");

            RecentlyRun(r);
            DownloadOrigins(r);
            RecentInstalls(r);
        }

        private static void RecentlyRun(TriageResult r)
        {
            int n = 0;

            // Compatibility Assistant remembers programs Windows has run. Readable
            // without Administrator, unlike Prefetch or BAM.
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store"))
                {
                    if (k != null)
                    {
                        foreach (var name in k.GetValueNames())
                        {
                            if (string.IsNullOrEmpty(name) || name.Length < 4) continue;
                            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                            bool gone = !File.Exists(name);
                            var low = name.ToLowerInvariant();
                            bool sketchy = low.Contains("\\temp\\") || low.Contains("\\downloads\\") ||
                                           low.Contains("crack") || low.Contains("keygen") ||
                                           low.Contains("setup") || low.Contains("\\appdata\\");

                            if (!sketchy) continue;

                            n++;
                            DateTime when = DateTime.MinValue;
                            long size = 0;
                            if (!gone)
                            {
                                try { when = File.GetLastWriteTime(name); size = new FileInfo(name).Length; }
                                catch { }
                            }

                            var det = new StringBuilder();
                            det.Append(gone ? "No longer on disk. " : "Still on disk. ");
                            if (size > 0) det.Append("Size " + Human(size) + ". ");
                            det.Append("Windows recorded this program executing.   " + name);

                            var fRun = new Finding("History", "Ran: " + Path.GetFileName(name),
                                det.ToString(), gone ? null : name, gone ? Sev.Info : Sev.Warn, !gone);
                            fRun.When = when;
                            r.Add(fRun);
                        }
                    }
                }
            }
            catch { }

            // UserAssist records what the user launched from Explorer. Value names are
            // ROT13-obfuscated, which is why this data is usually overlooked.
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist"))
                {
                    if (root != null)
                    {
                        foreach (var guid in root.GetSubKeyNames())
                        {
                            using (var count = root.OpenSubKey(guid + @"\Count"))
                            {
                                if (count == null) continue;
                                foreach (var enc in count.GetValueNames())
                                {
                                    var name = Rot13(enc);
                                    var low = name.ToLowerInvariant();
                                    if (!low.EndsWith(".exe")) continue;

                                    bool interesting = low.Contains("crack") || low.Contains("keygen") ||
                                                       low.Contains("activator") || low.Contains("setup") ||
                                                       low.Contains("\\temp\\") || low.Contains("\\downloads\\");
                                    if (!interesting) continue;

                                    // Win7+ UserAssist layout: run count at offset 4,
                                    // last-executed FILETIME at offset 60.
                                    int runs = 0;
                                    DateTime lastRun = DateTime.MinValue;
                                    try
                                    {
                                        var blob = count.GetValue(enc) as byte[];
                                        if (blob != null && blob.Length >= 68)
                                        {
                                            runs = BitConverter.ToInt32(blob, 4);
                                            long ft = BitConverter.ToInt64(blob, 60);
                                            if (ft > 0) lastRun = DateTime.FromFileTime(ft);
                                        }
                                    }
                                    catch { }

                                    n++;
                                    var det = new StringBuilder();
                                    if (runs > 0) det.Append("Opened " + runs + " time" + (runs == 1 ? "" : "s") + ". ");
                                    det.Append("Launched from Explorer by you.   " + name);

                                    var fUa = new Finding("History", "Launched: " + Path.GetFileName(name),
                                        det.ToString(), null, Sev.Info, false);
                                    fUa.When = lastRun;
                                    r.Add(fUa);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            if (n == 0)
                r.Add(new Finding("History", "No suspicious execution history",
                    "Nothing recorded as run from Temp, Downloads, or with a crack-style name.",
                    null, Sev.Ok, false));
        }

        public static string Human(long bytes)
        {
            if (bytes >= 1073741824) return (bytes / 1073741824.0).ToString("0.#") + " GB";
            if (bytes >= 1048576) return (bytes / 1048576.0).ToString("0.#") + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0") + " KB";
            return bytes + " B";
        }

        private static string Rot13(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c >= 'a' && c <= 'z') sb.Append((char)('a' + (c - 'a' + 13) % 26));
                else if (c >= 'A' && c <= 'Z') sb.Append((char)('A' + (c - 'A' + 13) % 26));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // Windows tags downloaded files with the URL they came from, in an alternate
        // data stream. It survives renaming and usually outlives browser history.
        private static void DownloadOrigins(TriageResult r)
        {
            var roots = new List<string>();
            try { roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")); }
            catch { }
            try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)); }
            catch { }

            var risky = new string[] { ".exe", ".msi", ".zip", ".rar", ".7z", ".iso", ".img" };
            int n = 0;

            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                string[] files;
                try { files = Directory.GetFiles(root); }
                catch { continue; }

                foreach (var f in files)
                {
                    string ext;
                    try { ext = Path.GetExtension(f).ToLowerInvariant(); }
                    catch { continue; }
                    if (Array.IndexOf(risky, ext) < 0) continue;

                    string url = ReadZone(f);
                    if (string.IsNullOrEmpty(url)) continue;

                    var low = url.ToLowerInvariant();
                    bool shady = low.Contains("download") && !low.Contains("microsoft") ||
                                 low.Contains("torrent") || low.Contains("crack") ||
                                 low.Contains("repack") || low.Contains("warez") ||
                                 low.Contains("4download") || low.Contains("nulled");

                    n++;
                    DateTime got = DateTime.MinValue;
                    long size = 0;
                    try { got = File.GetCreationTime(f); size = new FileInfo(f).Length; }
                    catch { }

                    var det = new StringBuilder();
                    det.Append(Path.GetFileName(f));
                    if (size > 0) det.Append("  (" + Human(size) + ")");
                    det.Append("   <-   " + url);

                    var fDl = new Finding("History", "Downloaded from " + Host(url),
                        det.ToString(), f, shady ? Sev.Warn : Sev.Info, shady);
                    fDl.When = got;
                    r.Add(fDl);
                }
            }

            if (n == 0)
                r.Add(new Finding("History", "No download origins recorded",
                    "No installers in Downloads or on the Desktop carry a source URL.",
                    null, Sev.Info, false));
        }

        private static string Host(string url)
        {
            try
            {
                var u = new Uri(url);
                return u.Host;
            }
            catch { return url.Length > 40 ? url.Substring(0, 40) : url; }
        }

        private static string ReadZone(string path)
        {
            try
            {
                // Alternate data stream. .NET passes the path straight to CreateFile,
                // so the "file:stream" syntax works.
                var text = File.ReadAllText(path + ":Zone.Identifier");
                foreach (var line in text.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.StartsWith("HostUrl=", StringComparison.OrdinalIgnoreCase))
                        return t.Substring(8);
                }
                foreach (var line in text.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.StartsWith("ReferrerUrl=", StringComparison.OrdinalIgnoreCase))
                        return t.Substring(12);
                }
            }
            catch { }
            return null;
        }

        private static void RecentInstalls(TriageResult r)
        {
            DateTime? infection = null;
            if (r.RecentDetectionTimes.Count > 0) infection = r.RecentDetectionTimes.Max();
            if (!infection.HasValue) return;

            string stamp = infection.Value.ToString("yyyyMMdd");
            var hives = new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser };
            var paths = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            int n = 0;
            foreach (var hive in hives)
            {
                foreach (var p in paths)
                {
                    try
                    {
                        using (var k = hive.OpenSubKey(p))
                        {
                            if (k == null) continue;
                            foreach (var sub in k.GetSubKeyNames())
                            {
                                using (var s = k.OpenSubKey(sub))
                                {
                                    if (s == null) continue;
                                    var date = Convert.ToString(s.GetValue("InstallDate"));
                                    if (date != stamp) continue;
                                    var name = Convert.ToString(s.GetValue("DisplayName"));
                                    if (string.IsNullOrEmpty(name)) continue;
                                    n++;
                                    r.Add(new Finding("History", "Installed on the day of the infection: " + name,
                                        "Publisher: " + Convert.ToString(s.GetValue("Publisher")) +
                                        "   -   check you meant to install this.",
                                        null, Sev.Warn, false));
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            if (n == 0)
                r.Add(new Finding("History", "Nothing installed on the infection date",
                    "No programs registered an install date matching the detection.", null, Sev.Ok, false));
        }
    }
}
