// In-app update check + self-replace. Checks a small version.json the
// website serves, and if newer than Brand.Version, downloads the new build
// and swaps it in.
//
// The running exe cannot overwrite itself while it's still the process
// holding the file open, so the swap happens via a tiny generated .bat:
// this process exits, the batch waits for it to actually be gone, replaces
// the exe, relaunches it, then deletes itself. No installer, no admin
// rights needed - Aftermath already writes to its own install folder or it
// wouldn't have been runnable from there in the first place.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace Aftermath
{
    public class UpdateInfo
    {
        public string Version;
        public string Url;
        public string Notes;
    }

    public static class UpdateChecker
    {
        private const string VersionUrl = AccountClient.BaseUrl + "/aftermath/version.json";

        static UpdateChecker()
        {
            // Same reasoning as AccountClient's static constructor - force
            // TLS1.2 so this doesn't fail on older Windows/.NET defaults
            // independently of whether AccountClient has already run.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }
            catch { }
        }

        // Null on any failure (offline, bad response, etc.) or when already
        // on the latest version - callers treat null as "nothing to do",
        // never as an error to surface, since a failed background check
        // should never interrupt someone using the app.
        public static UpdateInfo CheckForUpdate()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(VersionUrl);
                req.Method = "GET";
                req.Timeout = 10000;
                string body;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    body = reader.ReadToEnd();

                string latest = Json.GetString(body, "version");
                if (string.IsNullOrEmpty(latest)) return null;

                Version latestV, currentV;
                if (!Version.TryParse(latest, out latestV)) return null;
                if (!Version.TryParse(Brand.Version, out currentV)) return null;
                if (latestV.CompareTo(currentV) <= 0) return null;

                return new UpdateInfo
                {
                    Version = latest,
                    Url = Json.GetString(body, "url"),
                    Notes = Json.GetString(body, "notes")
                };
            }
            catch
            {
                return null;
            }
        }

        // Downloads the new build and stages the swap-and-relaunch batch,
        // launching it suspended-on-wait. Does NOT exit the process itself -
        // Application.Exit() must run on the UI thread, and this method is
        // meant to be called from a background thread (a WebClient download
        // blocking the UI thread would freeze the window) - so the caller
        // marshals the exit back after this returns successfully. onStatus
        // is invoked on the calling thread; callers running this from a
        // background thread must marshal it themselves, same convention
        // AccountClient's SetStatus-style callers already follow elsewhere.
        public static void DownloadAndApply(UpdateInfo info, Action<string> onStatus)
        {
            if (string.IsNullOrEmpty(info.Url))
                throw new InvalidOperationException("No download URL in the update response.");

            string currentExe = Application.ExecutablePath;
            string tempExe = Path.Combine(Path.GetTempPath(), "Aftermath-" + info.Version + ".exe");

            onStatus("Downloading update...");
            using (var client = new WebClient())
                client.DownloadFile(info.Url, tempExe);

            var newInfo = new FileInfo(tempExe);
            if (!newInfo.Exists || newInfo.Length < 1024)
                throw new InvalidOperationException("Downloaded file looks incomplete.");

            onStatus("Restarting to apply update...");

            string batPath = Path.Combine(Path.GetTempPath(), "aftermath-update-" + Guid.NewGuid().ToString("N") + ".bat");
            string script =
                "@echo off\r\n" +
                ":wait\r\n" +
                "tasklist /fi \"PID eq " + Process.GetCurrentProcess().Id + "\" | find \"" + Process.GetCurrentProcess().Id + "\" >nul\r\n" +
                "if not errorlevel 1 (\r\n" +
                "  timeout /t 1 /nobreak >nul\r\n" +
                "  goto wait\r\n" +
                ")\r\n" +
                "copy /y \"" + tempExe + "\" \"" + currentExe + "\" >nul\r\n" +
                "del \"" + tempExe + "\" >nul 2>&1\r\n" +
                "start \"\" \"" + currentExe + "\"\r\n" +
                "del \"%~f0\"\r\n";
            File.WriteAllText(batPath, script);

            var psi = new ProcessStartInfo("cmd.exe", "/c \"" + batPath + "\"");
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }
    }
}
