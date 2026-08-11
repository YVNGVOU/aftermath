// Hand-rolled JSON (de)serializer for a full TriageResult. Same idiom as
// Quarantine.cs / Baseline.cs - no JSON library ships with the old in-box
// compiler this project targets - but TriageResult is not flat like those two
// stores' manifests: Findings and Drift are nested lists, so the file has two
// array-of-object sections layered on the same brace/bracket-matching
// primitives, plus a handful of top-level scalar fields written before them.
//
// This is what --headless mode writes when a scan finishes, and what
// SweepRunner reads back after pulling a report from a remote host.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Aftermath
{
    public static class ReportIO
    {
        public static void SaveTriageResult(TriageResult r, string path)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("\"RealtimeProtection\":" + (r.RealtimeProtection ? "true" : "false") + ",\n");
            sb.Append("\"RecentSerious\":" + r.RecentSerious.ToString(CultureInfo.InvariantCulture) + ",\n");
            sb.Append("\"FirstBaseline\":" + (r.FirstBaseline ? "true" : "false") + ",\n");

            sb.Append("\"RecentDetectionTimes\":[");
            for (int i = 0; i < r.RecentDetectionTimes.Count; i++)
            {
                sb.Append("\"" + Esc(r.RecentDetectionTimes[i].ToString("o", CultureInfo.InvariantCulture)) + "\"");
                if (i < r.RecentDetectionTimes.Count - 1) sb.Append(",");
            }
            sb.Append("],\n");

            sb.Append("\"Findings\":[\n");
            for (int i = 0; i < r.Findings.Count; i++)
            {
                sb.Append("  ");
                sb.Append(SerializeFinding(r.Findings[i]));
                if (i < r.Findings.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("],\n");

            sb.Append("\"Drift\":[\n");
            for (int i = 0; i < r.Drift.Count; i++)
            {
                sb.Append("  ");
                sb.Append(SerializeDrift(r.Drift[i]));
                if (i < r.Drift.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("]\n");
            sb.Append("}\n");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // Write-then-rename, same as QuarantineStore/BaselineStore - a crash or a
            // killed remote process mid-write cannot leave a half-written report behind.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static TriageResult LoadTriageResult(string path)
        {
            var r = new TriageResult();
            try
            {
                if (!File.Exists(path)) return r;
                string text = File.ReadAllText(path, Encoding.UTF8);

                r.RealtimeProtection = ParseRaw(text, "RealtimeProtection") == "true";

                int recentSerious;
                int.TryParse(ParseRaw(text, "RecentSerious"), NumberStyles.Integer, CultureInfo.InvariantCulture, out recentSerious);
                r.RecentSerious = recentSerious;

                r.FirstBaseline = ParseRaw(text, "FirstBaseline") == "true";

                r.RecentDetectionTimes = ParseDetectionTimes(text);

                foreach (var obj in ExtractObjectsFromArray(text, "Findings"))
                {
                    var f = ParseFinding(obj);
                    if (f != null) r.Findings.Add(f);
                }

                foreach (var obj in ExtractObjectsFromArray(text, "Drift"))
                {
                    var d = ParseDrift(obj);
                    if (d != null) r.Drift.Add(d);
                }
            }
            catch { }
            return r;
        }

        // ---------- Finding ----------

        private static string SerializeFinding(Finding f)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"Category\":\"" + Esc(f.Category) + "\",");
            sb.Append("\"Title\":\"" + Esc(f.Title) + "\",");
            sb.Append("\"Detail\":\"" + Esc(f.Detail) + "\",");
            sb.Append("\"Path\":\"" + Esc(f.Path) + "\",");
            sb.Append("\"Severity\":\"" + f.Severity.ToString() + "\",");
            sb.Append("\"Removable\":" + (f.Removable ? "true" : "false") + ",");
            sb.Append("\"When\":\"" + Esc(f.When.ToString("o", CultureInfo.InvariantCulture)) + "\",");
            sb.Append("\"Count\":" + f.Count.ToString(CultureInfo.InvariantCulture) + ",");
            sb.Append("\"Watchlist\":" + (f.Watchlist ? "true" : "false"));
            sb.Append("}");
            return sb.ToString();
        }

        private static Finding ParseFinding(string obj)
        {
            try
            {
                string cat = ParseString(obj, "Category");
                string title = ParseString(obj, "Title");
                string detail = ParseString(obj, "Detail");
                string path = ParseString(obj, "Path");

                Sev sev = Sev.Info;
                try { sev = (Sev)Enum.Parse(typeof(Sev), ParseString(obj, "Severity")); }
                catch { }

                bool removable = ParseRaw(obj, "Removable") == "true";

                var f = new Finding(cat, title, detail, string.IsNullOrEmpty(path) ? null : path, sev, removable);

                DateTime when;
                if (DateTime.TryParse(ParseString(obj, "When"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out when))
                    f.When = when;

                int count = 1;
                int.TryParse(ParseRaw(obj, "Count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
                f.Count = count;

                f.Watchlist = ParseRaw(obj, "Watchlist") == "true";
                return f;
            }
            catch { return null; }
        }

        // ---------- DriftEntry ----------
        // Same six flat string fields Baseline.cs's DriftEntry already declares -
        // reused as-is rather than duplicated.

        private static string SerializeDrift(DriftEntry d)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"ChangeType\":\"" + Esc(d.ChangeType) + "\",");
            sb.Append("\"Category\":\"" + Esc(d.Category) + "\",");
            sb.Append("\"Title\":\"" + Esc(d.Title) + "\",");
            sb.Append("\"OldValue\":\"" + Esc(d.OldValue) + "\",");
            sb.Append("\"NewValue\":\"" + Esc(d.NewValue) + "\",");
            sb.Append("\"Path\":\"" + Esc(d.Path) + "\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static DriftEntry ParseDrift(string obj)
        {
            try
            {
                var d = new DriftEntry();
                d.ChangeType = ParseString(obj, "ChangeType");
                d.Category = ParseString(obj, "Category");
                d.Title = ParseString(obj, "Title");
                d.OldValue = ParseString(obj, "OldValue");
                d.NewValue = ParseString(obj, "NewValue");
                d.Path = ParseString(obj, "Path");
                return d;
            }
            catch { return null; }
        }

        // ---------- array-of-string field (RecentDetectionTimes) ----------

        private static List<DateTime> ParseDetectionTimes(string text)
        {
            var list = new List<DateTime>();
            string marker = "\"RecentDetectionTimes\":[";
            int k = text.IndexOf(marker, StringComparison.Ordinal);
            if (k < 0) return list;
            int open = k + marker.Length - 1;
            int close = FindArrayEnd(text, open);
            if (close < 0) return list;

            int i = open + 1;
            while (true)
            {
                int q1 = text.IndexOf('"', i);
                if (q1 < 0 || q1 > close) break;
                int q2 = q1 + 1;
                var sb = new StringBuilder();
                while (q2 < close && text[q2] != '"')
                {
                    if (text[q2] == '\\' && q2 + 1 < close) { sb.Append(text[q2 + 1]); q2 += 2; continue; }
                    sb.Append(text[q2]);
                    q2++;
                }
                DateTime dt;
                if (DateTime.TryParse(sb.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                    list.Add(dt);
                i = q2 + 1;
            }
            return list;
        }

        // ---------- array-of-object field (Findings, Drift) ----------

        private static List<string> ExtractObjectsFromArray(string text, string key)
        {
            var list = new List<string>();
            string marker = "\"" + key + "\":[";
            int k = text.IndexOf(marker, StringComparison.Ordinal);
            if (k < 0) return list;
            int open = k + marker.Length - 1;
            int close = FindArrayEnd(text, open);
            if (close < 0) return list;

            int i = open + 1;
            while (true)
            {
                int ob = text.IndexOf('{', i);
                if (ob < 0 || ob > close) break;
                int cb = FindObjectEnd(text, ob);
                if (cb < 0 || cb > close) break;
                list.Add(text.Substring(ob + 1, cb - ob - 1));
                i = cb + 1;
            }
            return list;
        }

        // ---------- shared hand-rolled JSON primitives ----------
        // Same brace-matching, escaping and value-reading idiom as
        // Quarantine.cs/Baseline.cs, plus one addition (FindArrayEnd) those two
        // never needed because their JSON has no nested arrays.

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

        // Same brace-skipping-quoted-strings idea as FindObjectEnd, but tracking
        // bracket depth instead of a single close - the array can (and here does)
        // contain object literals of its own with no bracket characters inside
        // them worth tracking, since Finding/DriftEntry are flat.
        private static int FindArrayEnd(string text, int open)
        {
            bool inStr = false;
            int depth = 0;
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
                else if (c == '[') depth++;
                else if (c == ']') { depth--; if (depth == 0) return i; }
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
}
