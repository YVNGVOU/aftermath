// Baseline and drift: on every run, compare the current configuration surfaces
// (startup entries, persistence, proxy/hosts/Defender/update settings, listening
// ports) against the previous run and report only what changed. Same hand-rolled
// JSON idiom as QuarantineStore's manifest.json - no external JSON library ships
// with the old in-box compiler this project targets.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Aftermath
{
    // One configuration surface captured at a point in time. Identity is stable
    // across runs (Category + a fixed topic, or Category + the entry's own key) -
    // never a timestamp or a count, since those change every run and would just
    // be diff noise.
    public class BaselineItem
    {
        public string Category = "";
        public string Identity = "";
        public string Title = "";
        public string Path = "";
        public string Value = "";
        public string CapturedAt = "";   // ISO 8601 UTC - duplicated on every item so the store stays one flat shape
    }

    public class BaselineSnapshot
    {
        public DateTime CapturedAt = DateTime.MinValue;
        public List<BaselineItem> Items = new List<BaselineItem>();
    }

    public class DriftEntry
    {
        public string ChangeType = "";   // Added / Removed / Changed
        public string Category = "";
        public string Title = "";
        public string OldValue = "";
        public string NewValue = "";
        public string Path = "";
    }

    public static class BaselineStore
    {
        private static readonly object Lock = new object();

        private static string RootDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Aftermath");
            }
        }

        private static string BaselinePath
        {
            get { return Path.Combine(RootDir, "baseline.json"); }
        }

        private static void EnsureDir()
        {
            if (!Directory.Exists(RootDir)) Directory.CreateDirectory(RootDir);
        }

        // Null means "no baseline yet" - either this is genuinely the first run,
        // or the file is missing/unreadable. Both are treated as first-run by the
        // caller, which is the safe choice: it costs one skipped diff, not a
        // false drift report built from garbage.
        public static BaselineSnapshot Load()
        {
            lock (Lock)
            {
                try
                {
                    if (!File.Exists(BaselinePath)) return null;
                    string text = File.ReadAllText(BaselinePath, Encoding.UTF8);
                    var items = LoadItems(text);
                    if (items.Count == 0) return null;

                    var snap = new BaselineSnapshot();
                    snap.Items = items;
                    DateTime dt;
                    if (DateTime.TryParse(items[0].CapturedAt, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out dt))
                        snap.CapturedAt = dt;
                    return snap;
                }
                catch { return null; }
            }
        }

        // Builds a fresh snapshot from a completed scan and writes it, replacing
        // whatever was there before. Callers must diff against the OLD baseline
        // first - this call is destructive to that comparison point.
        public static void Save(TriageResult r)
        {
            lock (Lock)
            {
                try
                {
                    var snap = BuildSnapshot(r);
                    EnsureDir();

                    var sb = new StringBuilder();
                    sb.Append("[\n");
                    for (int i = 0; i < snap.Items.Count; i++)
                    {
                        sb.Append("  ");
                        sb.Append(SerializeItem(snap.Items[i]));
                        if (i < snap.Items.Count - 1) sb.Append(",");
                        sb.Append("\n");
                    }
                    sb.Append("]\n");

                    // Write-then-rename, same as QuarantineStore - a crash mid-write
                    // cannot leave a half-written baseline behind.
                    string tmp = BaselinePath + ".tmp";
                    File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                    if (File.Exists(BaselinePath)) File.Delete(BaselinePath);
                    File.Move(tmp, BaselinePath);
                }
                catch { }
            }
        }

        // Added = identity present now, absent in the previous snapshot.
        // Removed = identity present in the previous snapshot, absent now.
        // Changed = same identity, different value string.
        public static List<DriftEntry> Diff(BaselineSnapshot previous, TriageResult current)
        {
            var result = new List<DriftEntry>();
            if (previous == null) return result;   // nothing to compare against yet

            var currentSnap = BuildSnapshot(current);

            var prevMap = new Dictionary<string, BaselineItem>(StringComparer.Ordinal);
            foreach (var it in previous.Items)
                if (!prevMap.ContainsKey(it.Identity)) prevMap[it.Identity] = it;

            var curMap = new Dictionary<string, BaselineItem>(StringComparer.Ordinal);
            foreach (var it in currentSnap.Items)
                if (!curMap.ContainsKey(it.Identity)) curMap[it.Identity] = it;

            foreach (var kv in curMap)
            {
                BaselineItem prevItem;
                if (!prevMap.TryGetValue(kv.Key, out prevItem))
                {
                    var d = new DriftEntry();
                    d.ChangeType = "Added";
                    d.Category = kv.Value.Category; d.Title = kv.Value.Title; d.Path = kv.Value.Path;
                    d.OldValue = ""; d.NewValue = kv.Value.Value;
                    result.Add(d);
                }
                else if (!string.Equals(prevItem.Value, kv.Value.Value, StringComparison.Ordinal))
                {
                    var d = new DriftEntry();
                    d.ChangeType = "Changed";
                    d.Category = kv.Value.Category; d.Title = kv.Value.Title; d.Path = kv.Value.Path;
                    d.OldValue = prevItem.Value; d.NewValue = kv.Value.Value;
                    result.Add(d);
                }
            }

            foreach (var kv in prevMap)
            {
                if (!curMap.ContainsKey(kv.Key))
                {
                    var d = new DriftEntry();
                    d.ChangeType = "Removed";
                    d.Category = kv.Value.Category; d.Title = kv.Value.Title; d.Path = kv.Value.Path;
                    d.OldValue = kv.Value.Value; d.NewValue = "";
                    result.Add(d);
                }
            }

            return result;
        }

        // ---------- what counts as "configuration" ----------
        // Startup, Persistence and System findings are configuration state, worth
        // baselining. Detections and download/execution History are events - they
        // are expected to differ on every run and would just be diff noise.

        private static BaselineSnapshot BuildSnapshot(TriageResult r)
        {
            var snap = new BaselineSnapshot();
            snap.CapturedAt = DateTime.UtcNow;
            string stamp = snap.CapturedAt.ToString("o", CultureInfo.InvariantCulture);

            foreach (var f in r.Findings)
            {
                string identity = null;

                if (f.Category == "Startup")
                    identity = "Startup|" + f.Title;
                else if (f.Category == "Persistence")
                    identity = PersistenceIdentity(f);
                else if (f.Category == "System")
                    identity = SystemIdentity(f);
                else if (f.Category == "Network" && f.Title.StartsWith("Listening: ", StringComparison.Ordinal))
                    identity = "Network|" + f.Title;

                if (identity == null) continue;

                var item = new BaselineItem();
                item.Category = f.Category;
                item.Identity = identity;
                item.Title = f.Title;
                item.Path = f.Path == null ? "" : f.Path;
                item.Value = ShortValue(f);
                item.CapturedAt = stamp;
                snap.Items.Add(item);
            }

            return snap;
        }

        // Individual persistence findings only - not the "no suspicious
        // persistence found" summary or read-error findings, neither of which is
        // a configuration surface with a stable identity to diff.
        private static string PersistenceIdentity(Finding f)
        {
            if (f.Title.StartsWith("Service: ", StringComparison.Ordinal)) return "Persistence|" + f.Title;
            if (f.Title.StartsWith("WMI consumer: ", StringComparison.Ordinal)) return "Persistence|" + f.Title;
            if (f.Title.StartsWith("Scheduled task: ", StringComparison.Ordinal)) return "Persistence|" + f.Title;
            return null;
        }

        // System findings carry the state IN the title (e.g. "Proxy settings
        // clean" vs "A proxy is enabled"), so the title itself cannot be the
        // identity - it would turn every state flip into an Added+Removed pair
        // instead of a Changed. Each topic gets one fixed identity instead.
        private static string SystemIdentity(Finding f)
        {
            string t = f.Title;
            if (t.IndexOf("proxy", StringComparison.OrdinalIgnoreCase) >= 0) return "System|Proxy";
            if (t.StartsWith("Hosts file", StringComparison.Ordinal)) return "System|Hosts";
            if (t.IndexOf("real-time protection", StringComparison.OrdinalIgnoreCase) >= 0) return "System|RealtimeProtection";
            if (t.StartsWith("Office updates", StringComparison.Ordinal)) return "System|OfficeUpdates";
            if (t.IndexOf("Windows Update automatic updates", StringComparison.OrdinalIgnoreCase) >= 0) return "System|WindowsUpdateAU";
            if (t.IndexOf("Defender exclusion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("Defender has suspicious exclusions", StringComparison.OrdinalIgnoreCase) >= 0)
                return "System|DefenderExclusions";
            return null;   // "check failed" / "Running without Administrator" - not configuration
        }

        private static string ShortValue(Finding f)
        {
            string v = f.Title + "  " + (f.Detail == null ? "" : f.Detail);
            if (v.Length > 400) v = v.Substring(0, 400);
            return v;
        }

        // ---------- hand-rolled baseline.json ----------
        // One flat JSON array of flat objects, same shape every time - identical
        // idiom to QuarantineStore's manifest.json, not shared with it, since each
        // store here owns its own on-disk format end to end.

        private static List<BaselineItem> LoadItems(string text)
        {
            var list = new List<BaselineItem>();
            int i = 0;
            while (true)
            {
                int open = text.IndexOf('{', i);
                if (open < 0) break;
                int close = FindObjectEnd(text, open);
                if (close < 0) break;

                string obj = text.Substring(open + 1, close - open - 1);
                var item = ParseItem(obj);
                if (item != null) list.Add(item);
                i = close + 1;
            }
            return list;
        }

        private static string SerializeItem(BaselineItem it)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"Category\":\"" + Esc(it.Category) + "\",");
            sb.Append("\"Identity\":\"" + Esc(it.Identity) + "\",");
            sb.Append("\"Title\":\"" + Esc(it.Title) + "\",");
            sb.Append("\"Path\":\"" + Esc(it.Path) + "\",");
            sb.Append("\"Value\":\"" + Esc(it.Value) + "\",");
            sb.Append("\"CapturedAt\":\"" + Esc(it.CapturedAt) + "\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static BaselineItem ParseItem(string obj)
        {
            try
            {
                var it = new BaselineItem();
                it.Category = ParseString(obj, "Category");
                it.Identity = ParseString(obj, "Identity");
                it.Title = ParseString(obj, "Title");
                it.Path = ParseString(obj, "Path");
                it.Value = ParseString(obj, "Value");
                it.CapturedAt = ParseString(obj, "CapturedAt");
                if (string.IsNullOrEmpty(it.Identity)) return null;
                return it;
            }
            catch { return null; }
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
    }
}
