// Quarantine: moves risky files aside instead of destroying them, with a restore
// path. Files land in %LOCALAPPDATA%\Aftermath\Quarantine renamed to a GUID, and
// a single hand-rolled manifest.json tracks where each one came from so Restore
// can put it back. No System.Text.Json here - the in-box csc targets an old
// framework that does not ship it, so the (de)serializer below is hand-written.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace Aftermath
{
    // One quarantined item: where it came from, where it lives now, and whether it
    // has already been put back.
    public class QuarantineEntry
    {
        public string OriginalPath = "";
        public string QuarantinedPath = "";
        public string QuarantinedAt = "";      // ISO 8601, UTC
        public string OriginalFileName = "";
        public long SizeBytes;
        public string Reason = "";              // the Finding.Title that caused this
        public bool Restored;
    }

    // Result of a single QuarantineStore operation - mirrors Remover.Outcome so the
    // two removal paths (quarantine vs. permanent delete) feel like one family.
    public class QuarantineOutcome
    {
        public bool Success;
        public string Message = "";
        public QuarantineEntry Entry;
    }

    // Holds quarantined files on disk plus the one manifest.json describing them.
    // This is the "undo" for what Remover.Delete used to do unconditionally.
    public static class QuarantineStore
    {
        // Directories are zipped, and the zip is named "<guid>.dirzip" rather than
        // "<guid>.zip" - purely so Restore can tell "this is a zipped folder" apart
        // from "this is a plain file whose own extension happened to be .zip"
        // without needing an extra manifest field. The blob itself is still an
        // ordinary zip archive underneath.
        private const string DirExt = ".dirzip";

        private static readonly object Lock = new object();

        private static string RootDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Aftermath\\Quarantine");
            }
        }

        private static string ManifestPath
        {
            get { return Path.Combine(RootDir, "manifest.json"); }
        }

        private static void EnsureDir()
        {
            if (!Directory.Exists(RootDir)) Directory.CreateDirectory(RootDir);
        }

        // Moves a file or folder into quarantine. Folders are zipped into a single
        // archive first; the original is then removed (best-effort - if it cannot be
        // removed because something has it locked, the copy in quarantine is still
        // kept and reported, since a stray leftover is safer than losing the backup).
        public static QuarantineOutcome Add(string originalPath, string reason)
        {
            var outp = new QuarantineOutcome();
            lock (Lock)
            {
                try
                {
                    if (string.IsNullOrEmpty(originalPath))
                    {
                        outp.Message = "No path given.";
                        return outp;
                    }
                    if (Guard.IsProtected(originalPath))
                    {
                        outp.Message = "Refused: protected system location. Aftermath will not quarantine this.";
                        return outp;
                    }

                    bool isDir = Directory.Exists(originalPath);
                    bool isFile = File.Exists(originalPath);
                    if (!isDir && !isFile)
                    {
                        outp.Message = "Already gone.";
                        return outp;
                    }

                    EnsureDir();

                    string id = Guid.NewGuid().ToString("N");
                    string originalName = isDir ? new DirectoryInfo(originalPath).Name : Path.GetFileName(originalPath);
                    string quarantinedPath;
                    long size = 0;
                    string extraNote = "";

                    if (isDir)
                    {
                        quarantinedPath = Path.Combine(RootDir, id + DirExt);
                        ZipFile.CreateFromDirectory(originalPath, quarantinedPath, CompressionLevel.Fastest, false);
                        try { size = new FileInfo(quarantinedPath).Length; } catch { }

                        try { Directory.Delete(originalPath, true); }
                        catch (Exception ex) { extraNote = " (original folder could not be removed: " + ex.Message + ")"; }
                    }
                    else
                    {
                        string ext = Path.GetExtension(originalPath);
                        quarantinedPath = Path.Combine(RootDir, id + ext);
                        try { size = new FileInfo(originalPath).Length; } catch { }
                        File.Move(originalPath, quarantinedPath);
                    }

                    var entry = new QuarantineEntry();
                    entry.OriginalPath = originalPath;
                    entry.QuarantinedPath = quarantinedPath;
                    entry.QuarantinedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    entry.OriginalFileName = originalName;
                    entry.SizeBytes = size;
                    entry.Reason = reason == null ? "" : reason;
                    entry.Restored = false;

                    var all = LoadManifest();
                    all.Add(entry);
                    SaveManifest(all);

                    outp.Success = true;
                    outp.Entry = entry;
                    outp.Message = "Quarantined." + extraNote;
                    return outp;
                }
                catch (Exception ex)
                {
                    outp.Message = "Could not quarantine: " + ex.Message;
                    return outp;
                }
            }
        }

        // All quarantined items that have not been restored, newest first.
        public static List<QuarantineEntry> List()
        {
            lock (Lock)
            {
                try
                {
                    return LoadManifest()
                        .Where(e => !e.Restored)
                        .OrderByDescending(e => e.QuarantinedAt, StringComparer.Ordinal)
                        .ToList();
                }
                catch { return new List<QuarantineEntry>(); }
            }
        }

        // Moves a quarantined item back to OriginalPath. Refuses to overwrite -
        // if something else now occupies that location, the conflict is reported
        // instead so nothing is silently clobbered.
        public static QuarantineOutcome Restore(string quarantinedPath)
        {
            var outp = new QuarantineOutcome();
            lock (Lock)
            {
                try
                {
                    var all = LoadManifest();
                    var entry = all.FirstOrDefault(e =>
                        !e.Restored && string.Equals(e.QuarantinedPath, quarantinedPath, StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                    {
                        outp.Message = "Not found in quarantine.";
                        return outp;
                    }

                    if (File.Exists(entry.OriginalPath) || Directory.Exists(entry.OriginalPath))
                    {
                        outp.Message = "Cannot restore: something already exists at " + entry.OriginalPath + ". Move or rename it first, then try again.";
                        outp.Entry = entry;
                        return outp;
                    }

                    try
                    {
                        string parent = Path.GetDirectoryName(entry.OriginalPath);
                        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                            Directory.CreateDirectory(parent);

                        if (IsZippedDir(entry))
                        {
                            ZipFile.ExtractToDirectory(entry.QuarantinedPath, entry.OriginalPath);
                            File.Delete(entry.QuarantinedPath);
                        }
                        else
                        {
                            File.Move(entry.QuarantinedPath, entry.OriginalPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        outp.Message = "Restore failed: " + ex.Message;
                        outp.Entry = entry;
                        return outp;
                    }

                    entry.Restored = true;
                    SaveManifest(all);

                    outp.Success = true;
                    outp.Entry = entry;
                    outp.Message = "Restored to " + entry.OriginalPath;
                    return outp;
                }
                catch (Exception ex)
                {
                    outp.Message = "Could not restore: " + ex.Message;
                    return outp;
                }
            }
        }

        // Permanently deletes one quarantined item (the blob in the quarantine
        // folder, not the long-gone original) and drops it from the manifest.
        // Used by the per-item "Delete permanently" action in the UI.
        public static QuarantineOutcome DeletePermanently(string quarantinedPath)
        {
            var outp = new QuarantineOutcome();
            lock (Lock)
            {
                try
                {
                    var all = LoadManifest();
                    var entry = all.FirstOrDefault(e =>
                        !e.Restored && string.Equals(e.QuarantinedPath, quarantinedPath, StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                    {
                        outp.Message = "Not found in quarantine.";
                        return outp;
                    }

                    try { if (File.Exists(entry.QuarantinedPath)) File.Delete(entry.QuarantinedPath); }
                    catch (Exception ex)
                    {
                        outp.Message = "Could not delete: " + ex.Message;
                        outp.Entry = entry;
                        return outp;
                    }

                    all.Remove(entry);
                    SaveManifest(all);

                    outp.Success = true;
                    outp.Entry = entry;
                    outp.Message = "Permanently deleted.";
                    return outp;
                }
                catch (Exception ex)
                {
                    outp.Message = "Could not delete: " + ex.Message;
                    return outp;
                }
            }
        }

        // Permanently deletes every non-restored entry older than the given number
        // of days. This is only the mechanism - retention length is a policy
        // decision for the caller, not something this method hardcodes.
        public static int PurgeOlderThan(int days)
        {
            int purged = 0;
            lock (Lock)
            {
                try
                {
                    var all = LoadManifest();
                    var cutoff = DateTime.UtcNow.AddDays(-days);
                    var keep = new List<QuarantineEntry>();

                    foreach (var e in all)
                    {
                        DateTime when;
                        bool parsed = DateTime.TryParse(e.QuarantinedAt, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out when);

                        if (!e.Restored && parsed && when < cutoff)
                        {
                            try { if (File.Exists(e.QuarantinedPath)) File.Delete(e.QuarantinedPath); }
                            catch { }
                            purged++;
                            continue;
                        }
                        keep.Add(e);
                    }

                    if (purged > 0) SaveManifest(keep);
                }
                catch { }
            }
            return purged;
        }

        private static bool IsZippedDir(QuarantineEntry e)
        {
            return string.Equals(Path.GetExtension(e.QuarantinedPath), DirExt, StringComparison.OrdinalIgnoreCase);
        }

        // ---------- hand-rolled manifest.json ----------
        // One flat JSON array of flat objects - string/number/bool fields only, no
        // nesting - so a small purpose-built reader/writer is enough. Not meant to
        // read arbitrary JSON, only the shape SaveManifest itself writes.

        private static List<QuarantineEntry> LoadManifest()
        {
            var list = new List<QuarantineEntry>();
            try
            {
                if (!File.Exists(ManifestPath)) return list;
                string text = File.ReadAllText(ManifestPath, Encoding.UTF8);

                int i = 0;
                while (true)
                {
                    int open = text.IndexOf('{', i);
                    if (open < 0) break;
                    int close = FindObjectEnd(text, open);
                    if (close < 0) break;

                    string obj = text.Substring(open + 1, close - open - 1);
                    var entry = ParseEntry(obj);
                    if (entry != null) list.Add(entry);
                    i = close + 1;
                }
            }
            catch { }
            return list;
        }

        private static void SaveManifest(List<QuarantineEntry> entries)
        {
            EnsureDir();
            var sb = new StringBuilder();
            sb.Append("[\n");
            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append("  ");
                sb.Append(Serialize(entries[i]));
                if (i < entries.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("]\n");

            // Write-then-rename so a crash mid-write cannot leave a half-written
            // manifest behind - the old one stays intact until the new one lands.
            string tmp = ManifestPath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(ManifestPath)) File.Delete(ManifestPath);
            File.Move(tmp, ManifestPath);
        }

        private static string Serialize(QuarantineEntry e)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"OriginalPath\":\"" + Esc(e.OriginalPath) + "\",");
            sb.Append("\"QuarantinedPath\":\"" + Esc(e.QuarantinedPath) + "\",");
            sb.Append("\"QuarantinedAt\":\"" + Esc(e.QuarantinedAt) + "\",");
            sb.Append("\"OriginalFileName\":\"" + Esc(e.OriginalFileName) + "\",");
            sb.Append("\"SizeBytes\":" + e.SizeBytes.ToString(CultureInfo.InvariantCulture) + ",");
            sb.Append("\"Reason\":\"" + Esc(e.Reason) + "\",");
            sb.Append("\"Restored\":" + (e.Restored ? "true" : "false"));
            sb.Append("}");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // Finds the '}' that closes the object opened at 'open', skipping over
        // braces that appear inside quoted strings.
        private static int FindObjectEnd(string text, int open)
        {
            bool inStr = false;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') inStr = true;
                else if (c == '}') return i;
            }
            return -1;
        }

        private static QuarantineEntry ParseEntry(string obj)
        {
            try
            {
                var e = new QuarantineEntry();
                e.OriginalPath = ParseString(obj, "OriginalPath");
                e.QuarantinedPath = ParseString(obj, "QuarantinedPath");
                e.QuarantinedAt = ParseString(obj, "QuarantinedAt");
                e.OriginalFileName = ParseString(obj, "OriginalFileName");
                e.Reason = ParseString(obj, "Reason");

                long size;
                long.TryParse(ParseRaw(obj, "SizeBytes"), NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
                e.SizeBytes = size;

                e.Restored = ParseRaw(obj, "Restored") == "true";
                return e;
            }
            catch { return null; }
        }

        private static string ParseString(string obj, string key)
        {
            string marker = "\"" + key + "\"";
            int k = obj.IndexOf(marker, StringComparison.Ordinal);
            if (k < 0) return "";
            int colon = obj.IndexOf(':', k + marker.Length);
            if (colon < 0) return "";
            int q1 = obj.IndexOf('"', colon + 1);
            if (q1 < 0) return "";

            var sb = new StringBuilder();
            int i = q1 + 1;
            while (i < obj.Length)
            {
                char c = obj[i];
                if (c == '\\' && i + 1 < obj.Length)
                {
                    char n = obj[i + 1];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == 't') sb.Append('\t');
                    else sb.Append(n);
                    i += 2;
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static string ParseRaw(string obj, string key)
        {
            string marker = "\"" + key + "\"";
            int k = obj.IndexOf(marker, StringComparison.Ordinal);
            if (k < 0) return "";
            int colon = obj.IndexOf(':', k + marker.Length);
            if (colon < 0) return "";

            int start = colon + 1;
            int end = start;
            while (end < obj.Length && obj[end] != ',' && obj[end] != '}') end++;
            return obj.Substring(start, end - start).Trim();
        }
    }

    // Persists the admin-chosen "days to keep quarantined items" value used by
    // the Data settings page's retention control. PurgeOlderThan itself takes no
    // stored configuration of its own - this is the minimum plumbing needed to
    // give the UI a real, saved number to call it with. Mirrors Theme.Load/Save's
    // HKCU registry pattern rather than inventing a second settings format.
    public static class QuarantineRetentionSettings
    {
        private const string RegPath = @"Software\Aftermath\Quarantine";
        public const int DefaultDays = 30;

        public static int Load()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (k != null)
                    {
                        var v = k.GetValue("RetentionDays");
                        if (v != null)
                        {
                            int days = Convert.ToInt32(v);
                            if (days >= 1) return days;
                        }
                    }
                }
            }
            catch { }
            return DefaultDays;
        }

        public static void Save(int days)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    if (k != null) k.SetValue("RetentionDays", days, RegistryValueKind.DWord);
                }
            }
            catch { }
        }
    }
}
