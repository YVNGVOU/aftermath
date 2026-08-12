// Minimal hand-rolled JSON reader for the couple of flat {"key":"value"}
// responses this app parses (see AccountClient.cs for the older,
// not-yet-refactored copy of the same two methods - this is the shared
// version new callers like UpdateChecker use). No third-party JSON
// package: everything this app talks to returns small, flat, known-shape
// objects, so a real parser would be a dependency for something a dozen
// lines already covers correctly.

using System;
using System.Text;

namespace Aftermath
{
    internal static class Json
    {
        public static bool GetBool(string json, string key)
        {
            string marker = "\"" + key + "\"";
            int k = json.IndexOf(marker, StringComparison.Ordinal);
            if (k < 0) return false;
            int colon = json.IndexOf(':', k + marker.Length);
            if (colon < 0) return false;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            return json.Length - i >= 4 && json.Substring(i, 4) == "true";
        }

        public static string GetString(string json, string key)
        {
            string marker = "\"" + key + "\"";
            int k = json.IndexOf(marker, StringComparison.Ordinal);
            if (k < 0) return "";
            int colon = json.IndexOf(':', k + marker.Length);
            if (colon < 0) return "";
            int firstQuote = json.IndexOf('"', colon + 1);
            if (firstQuote < 0) return "";
            int i = firstQuote + 1;
            var sb = new StringBuilder();
            while (i < json.Length && json[i] != '"')
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    i++;
                    if (json[i] == 'n') sb.Append('\n');
                    else if (json[i] == 'r') sb.Append('\r');
                    else if (json[i] == 't') sb.Append('\t');
                    else sb.Append(json[i]);
                }
                else sb.Append(json[i]);
                i++;
            }
            return sb.ToString();
        }
    }
}
