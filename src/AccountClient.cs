// Calls the SINVAUX website's login API so a user can sign in with the same
// email/password they used to buy a plan, instead of copy-pasting a license
// key by hand. This is a convenience layer only - LicenseStore.TryVerify
// still does the actual offline signature check on whatever key comes back,
// so a compromised or unreachable website can never grant a tier it did not
// legitimately sign for.

using System;
using System.IO;
using System.Net;
using System.Text;

namespace Aftermath
{
    public class AccountLoginResult
    {
        public bool Success;
        public bool HasLicense;
        public string LicenseKey;
        public string ErrorMessage;
    }

    public static class AccountClient
    {
        // Running on Fly's free *.fly.dev subdomain until there's enough
        // revenue/growth to justify buying a custom domain (svaftermath.net
        // is the planned name, not purchased yet - see DEPLOY.md step 7).
        // Swap this one constant once that domain is live.
        // Aftermath is served under /aftermath/* on the merged SINVAUX site
        // (formerly its own sinvaux-website deployment) - see the api path below.
        public const string BaseUrl = "https://sinvaux-main.fly.dev";

        static AccountClient()
        {
            // .NET Framework's default SecurityProtocol on older Windows
            // installs is SSL3/TLS1.0 only - Fly.io's edge requires TLS1.2+,
            // so without this the handshake fails silently and Login() surfaces
            // it as "could not reach the website" even though the URL and the
            // server are both fine. Cast to int rather than referencing
            // SecurityProtocolType.Tls12 directly - that enum member doesn't
            // exist in the .NET 4.0 reference assembly build.cmd compiles
            // against, but the underlying value works at runtime regardless of
            // which .NET Framework is actually installed.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }
            catch { /* best effort - nothing else to do on a runtime with no TLS1.2 support at all */ }
        }

        public static AccountLoginResult Login(string email, string password)
        {
            return Login(BaseUrl, email, password);
        }

        // baseUrl is a separate parameter (rather than only ever reading the
        // constant) so a local dev server can be exercised in tests without
        // touching the real endpoint.
        public static AccountLoginResult Login(string baseUrl, string email, string password)
        {
            var result = new AccountLoginResult();
            try
            {
                string body = "{\"email\":\"" + JsonEsc(email) + "\",\"password\":\"" + JsonEsc(password) + "\"}";
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

                var req = (HttpWebRequest)WebRequest.Create(baseUrl.TrimEnd('/') + "/aftermath/api/login");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.ContentLength = bodyBytes.Length;
                req.Timeout = 15000;

                using (var stream = req.GetRequestStream())
                    stream.Write(bodyBytes, 0, bodyBytes.Length);

                string responseText;
                try
                {
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        responseText = reader.ReadToEnd();
                }
                catch (WebException wex)
                {
                    // 401 for wrong credentials still carries a JSON body with
                    // the real reason - read it instead of just failing blind.
                    if (wex.Response != null)
                    {
                        using (var reader = new StreamReader(wex.Response.GetResponseStream(), Encoding.UTF8))
                            responseText = reader.ReadToEnd();
                    }
                    else
                    {
                        result.ErrorMessage = "Could not reach " + baseUrl + " - check your internet connection.";
                        return result;
                    }
                }

                bool ok = ParseBool(responseText, "ok");
                if (!ok)
                {
                    result.ErrorMessage = ParseString(responseText, "error");
                    if (string.IsNullOrEmpty(result.ErrorMessage)) result.ErrorMessage = "Login failed.";
                    return result;
                }

                result.Success = true;
                result.HasLicense = ParseBool(responseText, "hasLicense");
                if (result.HasLicense)
                    result.LicenseKey = ParseString(responseText, "licenseKey");
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "Could not reach the SINVAUX website: " + ex.Message;
                return result;
            }
        }

        private static string JsonEsc(string s)
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

        private static bool ParseBool(string json, string key)
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

        private static string ParseString(string json, string key)
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
