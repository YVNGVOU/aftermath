// License: offline-verifiable, RSA-2048-signed tier entitlement. The
// license key format is base64(payload-json) + "." + base64(signature).
// Verification needs only the embedded public key - no network call, so
// Free/Plus/Pro/Max keep working with the machine fully offline. Enterprise
// is the one tier that later validates online against Fleet's backend
// instead (not built yet - see design doc).

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Aftermath
{
    public enum LicenseTier { Free, Plus, Pro, Max, Enterprise }

    public class LicenseInfo
    {
        public LicenseTier Tier;
        public DateTime ExpiresUtc;
        public string Seat = "";
    }

    public static class LicenseStore
    {
        // Public half only. Generated once via .spikes/license-test/KeyGen.cs;
        // the private half never ships in this binary.
        public const string PublicKeyXml =
            "<RSAKeyValue><Modulus>yuLN1BKneuWdDiqLP3BUr+/Ff+mpSQk71BD6ez1GKln+2Jo8GV8sd8g/1QmR/nBRhWcsv8DtJh5wZsdkaABDJ7dfDMLVHuoqOSwcigAOGGJRY0OO8e1CV86ZanmrlIIvJYtXlcayMJNNQzI/7p8FMnmqYVym5A7uDvZDVvaPr8hNbmAFd3sHrGztBqvhNbSmPt02r+CWTX/A+FUd2i+GKFkuhxRFlDE2lRnwDe7aXvmk1HrlUeEVFPrEl8pURiUeBQqJRTbsRf9Ub2g0J8mb4mtEqJrb9kO81W8p897IIuHdRblcknmIIxfLtYdiantVZFRHmjbVKqbVjuUlXWEu+Q==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        private static readonly object Lock = new object();

        private static string StorePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Aftermath\\license.json");
            }
        }

        public static bool TryVerify(string licenseKey, out LicenseInfo info)
        {
            info = null;
            if (string.IsNullOrEmpty(licenseKey)) return false;

            string[] parts = licenseKey.Split('.');
            if (parts.Length != 2) return false;

            byte[] payloadBytes;
            byte[] sigBytes;
            try
            {
                payloadBytes = Convert.FromBase64String(parts[0]);
                sigBytes = Convert.FromBase64String(parts[1]);
            }
            catch { return false; }

            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(PublicKeyXml);
                bool verified;
                try { verified = rsa.VerifyData(payloadBytes, new SHA256CryptoServiceProvider(), sigBytes); }
                catch { return false; }
                if (!verified) return false;
            }

            string json;
            try { json = Encoding.UTF8.GetString(payloadBytes); }
            catch { return false; }

            string tierStr = ParseString(json, "tier");
            string expiresStr = ParseString(json, "expires");
            string seat = ParseString(json, "seat");

            LicenseTier tier;
            if (!Enum.TryParse(tierStr, true, out tier)) return false;

            DateTime expires;
            if (!DateTime.TryParse(expiresStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out expires))
                return false;
            if (expires < DateTime.UtcNow) return false;

            info = new LicenseInfo();
            info.Tier = tier;
            info.ExpiresUtc = expires;
            info.Seat = seat;
            return true;
        }

        public static bool SaveVerified(string licenseKey)
        {
            LicenseInfo info;
            if (!TryVerify(licenseKey, out info)) return false;

            lock (Lock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(StorePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    string json = "{\n  \"key\": \"" + Esc(licenseKey) + "\"\n}\n";
                    string tmp = StorePath + ".tmp";
                    File.WriteAllText(tmp, json, Encoding.UTF8);
                    if (File.Exists(StorePath)) File.Delete(StorePath);
                    File.Move(tmp, StorePath);
                    return true;
                }
                catch { return false; }
            }
        }

        public static LicenseInfo Load()
        {
            lock (Lock)
            {
                try
                {
                    if (!File.Exists(StorePath)) return null;
                    string json = File.ReadAllText(StorePath, Encoding.UTF8);
                    string key = ParseString(json, "key");
                    LicenseInfo info;
                    if (!TryVerify(key, out info)) return null;
                    return info;
                }
                catch { return null; }
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

        private static string ParseString(string obj, string key)
        {
            string marker = "\"" + key + "\"";
            int k = obj.IndexOf(marker, StringComparison.Ordinal);
            if (k < 0) return "";
            int colon = obj.IndexOf(':', k + marker.Length);
            if (colon < 0) return "";
            int firstQuote = obj.IndexOf('"', colon + 1);
            if (firstQuote < 0) return "";
            int i = firstQuote + 1;
            var sb = new StringBuilder();
            while (i < obj.Length && obj[i] != '"')
            {
                if (obj[i] == '\\' && i + 1 < obj.Length)
                {
                    i++;
                    if (obj[i] == 'n') sb.Append('\n');
                    else if (obj[i] == 'r') sb.Append('\r');
                    else if (obj[i] == 't') sb.Append('\t');
                    else sb.Append(obj[i]);
                }
                else sb.Append(obj[i]);
                i++;
            }
            return sb.ToString();
        }
    }
}
