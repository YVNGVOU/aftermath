// Third-party antivirus visibility - same honesty rule as every other scanner
// in this app: we read what Windows itself already knows, we never invent a
// verdict. Two real, standard, vendor-agnostic sources:
//
//   1. root\SecurityCenter2 / AntiVirusProduct - the same WMI class Windows
//      Security Center itself reads. Any AV that wants Windows to stop
//      nagging about "no antivirus installed" has to register here, so this
//      covers virtually every consumer/business AV product without us having
//      to know anything vendor-specific.
//   2. The Application event log, filtered to sources that match a known AV
//      vendor name. We quote the vendor's own message verbatim - Aftermath
//      does not interpret or summarize a third party's own detection text.
//
// Deliberately NOT built: per-vendor proprietary log/quarantine parsers.
// Getting those right for every vendor, and staying right as vendors change
// their formats, isn't feasible here - a wrong parse would be a silently
// wrong result, which is worse than not showing it at all.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace Aftermath
{
    public static class ExternalAv
    {
        private const string SecurityCenterNs = @"root\SecurityCenter2";
        private const int EventLookbackDays = 30;

        // Vendor keywords matched against Event Log Source strings. Not
        // exhaustive - any vendor absent from this list still shows up (or
        // not) via the SecurityCenter2 registration check above.
        private static readonly string[] VendorKeywords = new string[]
        {
            "Norton", "McAfee", "Avast", "AVG", "Malwarebytes", "ESET",
            "Kaspersky", "Bitdefender", "Trend Micro", "Sophos", "Webroot",
            "Symantec", "F-Secure", "Avira", "Panda"
        };

        public static void ThirdPartyAv(TriageResult r, Action<string> log)
        {
            try
            {
                log("Checking for third-party antivirus...");
                ScanRegisteredProducts(r);
                ScanEventLog(r);
            }
            catch (Exception ex)
            {
                r.Add(new Finding("External AV", "Scan error", ex.Message, null, Sev.Warn, false));
            }
        }

        // ---------- SecurityCenter2 registration ----------

        private static void ScanRegisteredProducts(TriageResult r)
        {
            try
            {
                var scope = new ManagementScope(SecurityCenterNs);
                scope.Connect();

                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM AntiVirusProduct")))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string name = "";
                        try { name = Convert.ToString(mo["displayName"]); } catch { }
                        if (string.IsNullOrEmpty(name)) continue;

                        // Defender is already covered by Scanner.Defender - listing it
                        // again here would be a duplicate, not new information.
                        if (name.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                        int state = 0;
                        try { state = Convert.ToInt32(mo["productState"]); } catch { }

                        bool enabled = IsRealTimeEnabled(state);
                        bool upToDate = IsDefinitionsUpToDate(state);

                        if (!enabled)
                        {
                            r.Add(new Finding("External AV", name + " is installed but not active",
                                "Windows reports this product's real-time protection as disabled. " +
                                "Malware often turns this off on purpose - if you did not do this yourself, treat it as suspicious.",
                                null, Sev.Warn, false));
                        }
                        else if (!upToDate)
                        {
                            r.Add(new Finding("External AV", name + " definitions are out of date",
                                "Windows reports this product's virus definitions as out of date. " +
                                "An AV with stale definitions can miss recent threats even while running.",
                                null, Sev.Warn, false));
                        }
                        else
                        {
                            r.Add(new Finding("External AV", name + " is active and up to date",
                                "Windows Security Center reports real-time protection on and definitions current.",
                                null, Sev.Ok, false));
                        }
                    }
                }
            }
            catch
            {
                // SecurityCenter2 can be unreachable (e.g. Server SKUs don't ship it).
                // That's a legitimate "nothing to report" case, not a scan failure -
                // Defender's own coverage is scanned separately and unaffected.
            }
        }

        // productState is a 3-byte bitmask historically undocumented by Microsoft but
        // long reverse-engineered and stable: byte 1 (bits 8-15) is the "WSC status"
        // byte where 0x10 = real-time protection enabled; byte 0 (bits 0-7) is the
        // definitions status where 0x00 = up to date.
        private static bool IsRealTimeEnabled(int productState)
        {
            int middleByte = (productState >> 8) & 0xFF;
            return (middleByte & 0x10) != 0;
        }

        private static bool IsDefinitionsUpToDate(int productState)
        {
            int lowByte = productState & 0xFF;
            return lowByte == 0x00;
        }

        // ---------- Application event log ----------

        private static void ScanEventLog(TriageResult r)
        {
            try
            {
                EventLog appLog = null;
                foreach (var el in EventLog.GetEventLogs())
                {
                    if (string.Equals(el.Log, "Application", StringComparison.OrdinalIgnoreCase)) { appLog = el; break; }
                    el.Dispose();
                }
                if (appLog == null) return;

                using (appLog)
                {
                    var cutoff = DateTime.Now.AddDays(-EventLookbackDays);

                    // EventLogEntryCollection has no server-side date/source filter, so we
                    // walk newest-first and stop once entries fall outside the window -
                    // that keeps this "last 30 days only" instead of the whole log.
                    var entries = appLog.Entries;
                    for (int i = entries.Count - 1; i >= 0; i--)
                    {
                        EventLogEntry e;
                        try { e = entries[i]; } catch { continue; }

                        if (e.TimeGenerated < cutoff) break;

                        if (e.EntryType != EventLogEntryType.Warning && e.EntryType != EventLogEntryType.Error) continue;

                        string source = e.Source ?? "";
                        string matchedVendor = VendorKeywords.FirstOrDefault(
                            k => source.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (matchedVendor == null) continue;

                        Sev sev = e.EntryType == EventLogEntryType.Error ? Sev.Bad : Sev.Warn;

                        // Quoted verbatim - this is the vendor's own record, not
                        // Aftermath's interpretation of it.
                        var f = new Finding("External AV", e.Source, e.Message, null, sev, false);
                        f.When = e.TimeGenerated;
                        r.Add(f);
                    }
                }
            }
            catch
            {
                // Best-effort/bonus signal, not a required check like Defender - an
                // inaccessible Application log (permissions, missing log, etc.) is
                // silently skipped rather than surfaced as a scary "could not scan".
            }
        }
    }
}
