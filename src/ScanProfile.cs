// ScanProfile: user-supplied extra folders to fold into the Artifacts hunt,
// on top of the fixed special-folder roots Scanner.Artifacts already walks.
// Max+ feature - Scanner.Artifacts itself checks Entitlements before reading
// this, so a downgraded license silently stops extending the scan rather
// than erroring.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Aftermath
{
    public static class ScanProfileStore
    {
        private static readonly object Lock = new object();

        private static string StorePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Aftermath\\scan-profile.json");
            }
        }

        // Format: a plain JSON array of strings, e.g. ["C:\\Foo","D:\\Bar"].
        public static List<string> Load()
        {
            lock (Lock)
            {
                var result = new List<string>();
                try
                {
                    if (!File.Exists(StorePath)) return result;
                    string json = File.ReadAllText(StorePath, Encoding.UTF8);

                    bool inStr = false;
                    var current = new StringBuilder();
                    for (int i = 0; i < json.Length; i++)
                    {
                        char c = json[i];
                        if (inStr)
                        {
                            if (c == '\\' && i + 1 < json.Length)
                            {
                                i++;
                                char n = json[i];
                                if (n == 'n') current.Append('\n');
                                else if (n == 'r') current.Append('\r');
                                else if (n == 't') current.Append('\t');
                                else current.Append(n);   // handles \" and \\
                                continue;
                            }
                            if (c == '"')
                            {
                                inStr = false;
                                result.Add(current.ToString());
                                current.Length = 0;
                                continue;
                            }
                            current.Append(c);
                        }
                        else if (c == '"') inStr = true;
                    }
                    return result;
                }
                catch { return new List<string>(); }
            }
        }

        public static bool Save(List<string> paths)
        {
            lock (Lock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(StorePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var sb = new StringBuilder();
                    sb.Append("[");
                    for (int i = 0; i < paths.Count; i++)
                    {
                        sb.Append("\"" + Esc(paths[i]) + "\"");
                        if (i < paths.Count - 1) sb.Append(",");
                    }
                    sb.Append("]\n");

                    string tmp = StorePath + ".tmp";
                    File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                    if (File.Exists(StorePath)) File.Delete(StorePath);
                    File.Move(tmp, StorePath);
                    return true;
                }
                catch { return false; }
            }
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
