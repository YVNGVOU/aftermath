// Entitlements: the single place every gated feature checks. Loaded once
// (lazily) from the verified license on disk; defaults to Free when no
// valid license is present. Never fabricates a capability - a tier either
// has a feature or it doesn't, checked here, never faked in the UI.

using System;

namespace Aftermath
{
    public class Entitlements
    {
        private static Entitlements _current;
        private static readonly object Lock = new object();

        public LicenseTier Tier { get; private set; }

        // Real functional gate on the license's "owner" seat (see License.cs's
        // DisplayTierLabel comment for where seat comes from) - previously
        // cosmetic only. Independent of Tier: a seat is a per-license flag, not
        // a rung on the Free/Plus/Pro/Max/Enterprise ladder, so this is never
        // folded into AtLeast().
        public bool IsOwner { get; private set; }

        public static Entitlements Current
        {
            get
            {
                lock (Lock)
                {
                    if (_current == null) _current = BuildFromLicense();
                    return _current;
                }
            }
        }

        public static void Reload()
        {
            lock (Lock)
            {
                _current = BuildFromLicense();
            }
        }

        private static Entitlements BuildFromLicense()
        {
            var ent = new Entitlements();
            var info = LicenseStore.Load();
            ent.Tier = info == null ? LicenseTier.Free : info.Tier;
            ent.IsOwner = info != null && string.Equals(info.Seat, "owner", StringComparison.OrdinalIgnoreCase);
            return ent;
        }

        private bool AtLeast(LicenseTier min)
        {
            return (int)Tier >= (int)min;
        }

        public bool HasArtifactsPages { get { return AtLeast(LicenseTier.Plus); } }
        public bool HasRawExport { get { return AtLeast(LicenseTier.Plus); } }
        public bool HasScheduledDrift { get { return AtLeast(LicenseTier.Pro); } }
        public bool HasPdfExport { get { return AtLeast(LicenseTier.Pro); } }
        public bool HasUnlimitedHistory { get { return AtLeast(LicenseTier.Pro); } }
        public bool HasCorrelationTimeline { get { return AtLeast(LicenseTier.Max); } }
        public bool HasCustomScanProfiles { get { return AtLeast(LicenseTier.Max); } }

        public int MaxSweepHosts
        {
            get
            {
                if (Tier == LicenseTier.Enterprise) return int.MaxValue;
                if (Tier == LicenseTier.Max) return 5;
                return 1;
            }
        }
    }
}
