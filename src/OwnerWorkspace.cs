// Owner Workspace: the one page gated by LicenseInfo.Seat == "owner" rather
// than by LicenseTier. Every tab below is built from data Aftermath already
// has on disk - the license itself, and SweepAudit's plain-text audit.log
// (see Sweep.cs) - parsed here with a line parser that matches exactly what
// SweepAudit.SweepStart/HostResult write, not a new structured-log format.
// No invented users, no fake device telemetry: this app has a single-account
// license model, so "Organization" here means "this license's real history",
// never a fabricated multi-user roster. Same paint idioms as everywhere else
// (Draw.RoundRect/RoundRectOutline, Theme.P read live, Brand.F, TabStrip from
// Widgets.cs) - no new UI paradigm introduced for this page.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Aftermath
{
    public class OwnerWorkspace : Panel
    {
        private readonly TabStrip tabs = new TabStrip();
        private readonly Panel body = new Panel();

        // ---- Overview tab ----
        private readonly Panel tabOverview = new Panel();
        private readonly Label ovTier = new Label();
        private readonly Label ovExpiry = new Label();
        private readonly Label ovHosts = new Label();
        private readonly Label ovEntries = new Label();
        private readonly Label ovLastActivity = new Label();
        private readonly Label ovEmpty = new Label();

        // ---- Devices tab ----
        private readonly Panel tabDevices = new Panel();
        private readonly OwnerHostList hostList = new OwnerHostList();

        // ---- Audit tab ----
        private readonly Panel tabAudit = new Panel();
        private readonly TextBox auditBox = new TextBox();

        // ---- Policies tab ----
        private readonly Panel tabPolicies = new Panel();
        private readonly Label polAutoTrigger = new Label();
        private readonly Label polScheduledDrift = new Label();
        private readonly Label polScanProfile = new Label();
        private readonly Label polSingleAccountNote = new Label();
        private Button btnPolSettings;

        // ---- Billing tab ----
        private readonly Panel tabBilling = new Panel();
        private readonly Label billTier = new Label();
        private readonly Label billPrice = new Label();
        private readonly Label billExpiry = new Label();
        private Button btnBillUpgrade;

        // Raised when Policies' "Go to Scan Behavior" is clicked - Ui.cs owns
        // the actual workspace/nav switch, same delegation pattern as
        // DetectionWorkspace's BackRequested/QuarantineRequested.
        public event EventHandler SettingsRequested;
        public event EventHandler UpgradeRequested;

        public OwnerWorkspace()
        {
            Dock = DockStyle.Fill;
            DoubleBuffered = true;

            tabs.Dock = DockStyle.Top;
            tabs.AddTab("Overview", "Overview");
            tabs.AddTab("Devices", "Devices");
            tabs.AddTab("Audit", "Audit");
            tabs.AddTab("Policies", "Policies");
            tabs.AddTab("Billing", "Billing");
            tabs.TabSelected += delegate (object s, string key) { ShowTab(key); };
            Controls.Add(tabs);

            body.Dock = DockStyle.Fill;
            Controls.Add(body);

            BuildOverviewTab();
            BuildDevicesTab();
            BuildAuditTab();
            BuildPoliciesTab();
            BuildBillingTab();

            foreach (var t in new Panel[] { tabOverview, tabDevices, tabAudit, tabPolicies, tabBilling })
            {
                t.Dock = DockStyle.Fill;
                t.Visible = false;
                body.Controls.Add(t);
            }

            // Fill panels added above; tabs docked Top was added first, so it
            // still resolves on top per WinForms' reverse-order docking rule.
        }

        private Label Header(string text)
        {
            var l = new Label();
            l.Text = text;
            l.Font = Brand.F(8f, FontStyle.Bold);
            l.AutoSize = true;
            return l;
        }

        private void ShowTab(string key)
        {
            tabOverview.Visible = (key == "Overview");
            tabDevices.Visible = (key == "Devices");
            tabAudit.Visible = (key == "Audit");
            tabPolicies.Visible = (key == "Policies");
            tabBilling.Visible = (key == "Billing");
        }

        // Populates every tab from the live license + audit log. Called by
        // Ui.cs's ShowPage exactly like UpgradePage.RefreshTier() - re-reads
        // live state on every navigation rather than caching stale figures.
        public void RefreshData()
        {
            var info = LicenseStore.Load();
            var log = SweepAuditReader.Read();

            // ---- Overview ----
            ovTier.Text = "Tier: " + LicenseStore.DisplayTierLabel();
            ovExpiry.Text = "License expires: " + (info != null ? info.ExpiresUtc.ToLocalTime().ToString("yyyy-MM-dd") : "unknown");

            bool hasLog = log.Entries.Count > 0;
            ovEmpty.Visible = !hasLog;
            ovHosts.Visible = hasLog;
            ovEntries.Visible = hasLog;
            ovLastActivity.Visible = hasLog;
            if (hasLog)
            {
                ovHosts.Text = "Distinct hosts ever swept: " + log.Hosts.Count;
                ovEntries.Text = "Total audit log entries: " + log.Entries.Count;
                ovLastActivity.Text = "Last activity: " + (log.LastEntryTime.HasValue
                    ? log.LastEntryTime.Value.ToString("yyyy-MM-dd HH:mm:ss")
                    : "unknown");
            }

            // ---- Devices ----
            hostList.SetItems(log.HostSummaries);

            // ---- Audit ----
            auditBox.Lines = log.RawLinesNewestFirst.ToArray();

            // ---- Policies ----
            polAutoTrigger.Text = "Auto-trigger after Defender scans: " + (AutoTrigger.IsRegistered() ? "ON" : "OFF");
            polScheduledDrift.Text = "Scheduled Drift baseline: " + (DriftScheduler.IsRegistered() ? "ON" : "OFF");
            polScanProfile.Text = "Custom scan profile: " + ScanProfileStore.Load().Count + " path(s) configured";

            // ---- Billing ----
            billTier.Text = "Current tier: " + LicenseStore.DisplayTierLabel();
            billPrice.Text = "Price: " + PriceLabel(info != null ? info.Tier : LicenseTier.Free);
            billExpiry.Text = "Expires: " + (info != null ? info.ExpiresUtc.ToLocalTime().ToString("yyyy-MM-dd") : "unknown");

            LayoutContent();
        }

        private static string PriceLabel(LicenseTier t)
        {
            switch (t)
            {
                case LicenseTier.Free: return "$0";
                case LicenseTier.Plus: return "Paid";
                case LicenseTier.Pro: return "Paid";
                case LicenseTier.Max: return "Paid";
                default: return "Custom";
            }
        }

        private void BuildOverviewTab()
        {
            int y = 16;
            ovTier.Font = Brand.F(10.5f, FontStyle.Bold);
            ovTier.AutoSize = true;
            ovTier.Location = new Point(22, y);
            tabOverview.Controls.Add(ovTier);
            y += 30;

            ovExpiry.AutoSize = true;
            ovExpiry.Location = new Point(22, y);
            tabOverview.Controls.Add(ovExpiry);
            y += 26;

            ovHosts.AutoSize = true;
            ovHosts.Location = new Point(22, y);
            tabOverview.Controls.Add(ovHosts);
            y += 26;

            ovEntries.AutoSize = true;
            ovEntries.Location = new Point(22, y);
            tabOverview.Controls.Add(ovEntries);
            y += 26;

            ovLastActivity.AutoSize = true;
            ovLastActivity.Location = new Point(22, y);
            tabOverview.Controls.Add(ovLastActivity);
            y += 26;

            // Same honest-empty-state tone driftList/cleanupList already use:
            // says what to do next, never implies something broke.
            ovEmpty.Text = "No organization activity yet - run a Sweep to start building history.";
            ovEmpty.AutoSize = false;
            ovEmpty.Location = new Point(22, y);
            ovEmpty.Height = 40;
            ovEmpty.Width = 480;
            tabOverview.Controls.Add(ovEmpty);

            // Single-account acknowledgement, one honest sentence, no fake roster.
            var single = new Label();
            single.Text = "Aftermath is single-account today - organization-wide user management isn't built yet.";
            single.AutoSize = false;
            single.Location = new Point(22, y + 50);
            single.Height = 40;
            single.Width = 480;
            single.Tag = "dim";
            tabOverview.Controls.Add(single);
        }

        private void BuildDevicesTab()
        {
            var header = Header("HOSTS SEEN BY SWEEP");
            header.Location = new Point(22, 14);
            tabDevices.Controls.Add(header);

            hostList.Location = new Point(20, 40);
            hostList.EmptyText = "No hosts swept yet - run a Sweep to populate this list.";
            tabDevices.Controls.Add(hostList);

            tabDevices.Resize += delegate { LayoutContent(); };
        }

        private void BuildAuditTab()
        {
            var header = Header("RAW AUDIT LOG (MOST RECENT FIRST)");
            header.Location = new Point(22, 14);
            tabAudit.Controls.Add(header);

            auditBox.Multiline = true;
            auditBox.ReadOnly = true;
            auditBox.WordWrap = false;
            auditBox.ScrollBars = ScrollBars.Both;
            auditBox.Font = new Font("Consolas", 8.5f);
            auditBox.Location = new Point(20, 40);
            auditBox.BorderStyle = BorderStyle.FixedSingle;
            tabAudit.Controls.Add(auditBox);

            tabAudit.Resize += delegate { LayoutContent(); };
        }

        private void BuildPoliciesTab()
        {
            int y = 16;
            var header = Header("REAL SETTINGS, SUMMARY ONLY - EDIT IN SETTINGS > SCAN BEHAVIOR");
            header.Location = new Point(22, y);
            tabPolicies.Controls.Add(header);
            y += 26;

            polAutoTrigger.AutoSize = true;
            polAutoTrigger.Location = new Point(22, y);
            tabPolicies.Controls.Add(polAutoTrigger);
            y += 26;

            polScheduledDrift.AutoSize = true;
            polScheduledDrift.Location = new Point(22, y);
            tabPolicies.Controls.Add(polScheduledDrift);
            y += 26;

            polScanProfile.AutoSize = true;
            polScanProfile.Location = new Point(22, y);
            tabPolicies.Controls.Add(polScanProfile);
            y += 34;

            btnPolSettings = Flat("Go to Scan Behavior settings", 220, 30);
            btnPolSettings.Location = new Point(22, y);
            btnPolSettings.Click += delegate { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); };
            tabPolicies.Controls.Add(btnPolSettings);
            y += 44;

            polSingleAccountNote.Text = "This tab is a summary view only - Settings > Scan Behavior remains the one real place these get edited.";
            polSingleAccountNote.AutoSize = false;
            polSingleAccountNote.Location = new Point(22, y);
            polSingleAccountNote.Width = 480;
            polSingleAccountNote.Height = 36;
            tabPolicies.Controls.Add(polSingleAccountNote);
        }

        private void BuildBillingTab()
        {
            int y = 16;
            billTier.Font = Brand.F(10.5f, FontStyle.Bold);
            billTier.AutoSize = true;
            billTier.Location = new Point(22, y);
            tabBilling.Controls.Add(billTier);
            y += 30;

            billPrice.AutoSize = true;
            billPrice.Location = new Point(22, y);
            tabBilling.Controls.Add(billPrice);
            y += 26;

            billExpiry.AutoSize = true;
            billExpiry.Location = new Point(22, y);
            tabBilling.Controls.Add(billExpiry);
            y += 34;

            btnBillUpgrade = Flat("See Upgrade", 140, 30);
            btnBillUpgrade.Location = new Point(22, y);
            btnBillUpgrade.Click += delegate { if (UpgradeRequested != null) UpgradeRequested(this, EventArgs.Empty); };
            tabBilling.Controls.Add(btnBillUpgrade);
        }

        private Button Flat(string text, int w, int h)
        {
            var b = new Button();
            b.Text = text;
            b.Width = w; b.Height = h;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void LayoutContent()
        {
            int w = Math.Max(200, tabDevices.ClientSize.Width - 40);
            hostList.Width = w;
            hostList.Height = Math.Max(80, tabDevices.ClientSize.Height - 60);

            auditBox.Width = Math.Max(200, tabAudit.ClientSize.Width - 40);
            auditBox.Height = Math.Max(80, tabAudit.ClientSize.Height - 60);
        }

        public void Restyle()
        {
            var p = Theme.P;
            BackColor = p.Bg;
            body.BackColor = p.Bg;
            foreach (var t in new Panel[] { tabOverview, tabDevices, tabAudit, tabPolicies, tabBilling })
                t.BackColor = p.Bg;

            foreach (var l in new Label[] { ovTier, billTier })
                l.ForeColor = p.Text;
            foreach (var l in new Label[] { ovExpiry, ovHosts, ovEntries, ovLastActivity, ovEmpty,
                polAutoTrigger, polScheduledDrift, polScanProfile, polSingleAccountNote,
                billPrice, billExpiry })
                l.ForeColor = p.TextDim;

            foreach (Control c in tabOverview.Controls)
                if (c is Label && "dim".Equals(c.Tag)) c.ForeColor = p.TextDim;

            foreach (Control c in tabDevices.Controls) if (c is Label) c.ForeColor = p.TextDim;
            foreach (Control c in tabAudit.Controls) if (c is Label) c.ForeColor = p.TextDim;
            foreach (Control c in tabPolicies.Controls) if (c is Label && c != polSingleAccountNote) c.ForeColor = p.TextDim;

            auditBox.BackColor = p.SurfaceElevated;
            auditBox.ForeColor = p.Text;

            Color soft = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.16 : 0.10);
            Color softBorder = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.34 : 0.24);
            Color softHover = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.24 : 0.17);
            foreach (var b in new Button[] { btnPolSettings, btnBillUpgrade })
            {
                b.BackColor = soft;
                b.ForeColor = p.Text;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = softBorder;
                b.FlatAppearance.MouseOverBackColor = softHover;
            }

            hostList.BackColor = p.Bg;
            hostList.Invalidate();
            Invalidate(true);
            LayoutContent();
        }

        // One row per host ever seen in SweepAudit's log. Read-only, no per-row
        // actions - not Finding data, so FindingList's checkbox/severity-chip
        // machinery does not apply, but it reuses the exact same painted-card
        // idiom (RoundRect body, thin left-edge stripe, Mix-derived hover) as
        // FindingList/QuarantineList/SweepList.
        private class OwnerHostList : Control
        {
            private List<HostSummary> items = new List<HostSummary>();
            private int hover = -1;
            private const int RowH = 60;

            public string EmptyText = "No hosts yet.";

            public OwnerHostList()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            public void SetItems(IEnumerable<HostSummary> items2)
            {
                items = new List<HostSummary>(items2);
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int i = IndexAt(e.Y);
                if (i != hover) { hover = i; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (hover != -1) { hover = -1; Invalidate(); }
                base.OnMouseLeave(e);
            }

            private int IndexAt(int y)
            {
                int i = y / RowH;
                return (i >= 0 && i < items.Count) ? i : -1;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var p = Theme.P;
                var g = e.Graphics;
                g.Clear(p.Bg);

                if (items.Count == 0)
                {
                    TextRenderer.DrawText(g, EmptyText, new Font("Segoe UI", 10f),
                        new Rectangle(0, 0, ClientSize.Width, 120), p.TextDim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    return;
                }

                int w = ClientSize.Width;
                for (int i = 0; i < items.Count; i++)
                {
                    var h = items[i];
                    int y = i * RowH;
                    if (y > ClientSize.Height) break;
                    var card = new Rectangle(10, y + 4, w - 20, RowH - 8);

                    Color fg = p.Accent;
                    Color bg = p.SurfaceElevated;
                    if (i == hover) bg = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.11 : 0.07);

                    Draw.RoundRect(g, card, 8, bg);
                    Draw.RoundRectOutline(g, card, 8, p.BorderStandard);
                    Draw.RoundRect(g, new Rectangle(card.X, card.Y + 6, 3, card.Height - 12), 2, fg);

                    int x = card.X + 16;
                    var titleRect = new Rectangle(x, card.Y + 8, Math.Max(60, card.Width - 32), 20);
                    TextRenderer.DrawText(g, h.Host, new Font("Segoe UI", 9.5f, FontStyle.Bold), titleRect, p.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                    string meta = "Last seen: " + h.LastSeen.ToString("yyyy-MM-dd HH:mm:ss") +
                        "      Last status: " + h.LastStatus;
                    var metaRect = new Rectangle(x, card.Y + 30, Math.Max(60, card.Width - 32), 22);
                    TextRenderer.DrawText(g, meta, new Font("Segoe UI", 8.5f), metaRect, p.TextDim,
                        TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                }
            }
        }
    }

    // One host's de-duplicated summary, derived purely from SweepAudit's
    // "host=" log lines - never fabricated.
    public class HostSummary
    {
        public string Host = "";
        public DateTime LastSeen;
        public string LastStatus = "";
    }

    // Everything OwnerWorkspace needs out of SweepAudit's log, in one pass.
    public class SweepAuditData
    {
        public List<string> Entries = new List<string>();              // one per parsed line (any kind)
        public List<string> RawLinesNewestFirst = new List<string>();  // raw text, newest first
        public HashSet<string> Hosts = new HashSet<string>();
        public List<HostSummary> HostSummaries = new List<HostSummary>();
        public DateTime? LastEntryTime;
    }

    // Reads and parses SweepAudit's plain-text audit.log directly - no new
    // structured-log format, just a line parser matching the exact two line
    // shapes SweepAudit.SweepStart/HostResult write:
    //   "yyyy-MM-dd HH:mm:ss  sweep-start  targets=N  hosts=h1,h2,..."
    //   "yyyy-MM-dd HH:mm:ss  host=HOST  result=STATUS  findings=N"
    public static class SweepAuditReader
    {
        private static string LogPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Aftermath\Sweep\audit.log");
            }
        }

        public static SweepAuditData Read()
        {
            var data = new SweepAuditData();
            string[] lines;
            try
            {
                if (!File.Exists(LogPath)) return data;
                lines = File.ReadAllLines(LogPath, Encoding.UTF8);
            }
            catch { return data; }

            // host -> running summary, overwritten as later lines are seen so
            // the final value is always the most recent one for that host.
            var byHost = new Dictionary<string, HostSummary>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                data.Entries.Add(raw);

                DateTime stamp;
                string rest;
                if (!TrySplitStamp(raw, out stamp, out rest)) continue;

                if (!data.LastEntryTime.HasValue || stamp > data.LastEntryTime.Value)
                    data.LastEntryTime = stamp;

                int hostIdx = rest.IndexOf("host=", StringComparison.Ordinal);
                if (rest.StartsWith("host=", StringComparison.Ordinal) || (hostIdx >= 0 && !rest.StartsWith("sweep-start", StringComparison.Ordinal)))
                {
                    string host = ExtractField(rest, "host=");
                    string status = ExtractField(rest, "result=");
                    if (!string.IsNullOrEmpty(host))
                    {
                        data.Hosts.Add(host);
                        HostSummary hs;
                        if (!byHost.TryGetValue(host, out hs))
                        {
                            hs = new HostSummary();
                            hs.Host = host;
                            byHost[host] = hs;
                        }
                        // Lines are appended in chronological order, so the last
                        // one processed for this host is always the newest.
                        hs.LastSeen = stamp;
                        hs.LastStatus = string.IsNullOrEmpty(status) ? "unknown" : status;
                    }
                }
                else if (rest.StartsWith("sweep-start", StringComparison.Ordinal))
                {
                    string hostsField = ExtractField(rest, "hosts=");
                    if (!string.IsNullOrEmpty(hostsField))
                        foreach (var h in hostsField.Split(','))
                            if (!string.IsNullOrEmpty(h)) data.Hosts.Add(h);
                }
            }

            foreach (var kv in byHost) data.HostSummaries.Add(kv.Value);
            data.HostSummaries.Sort(delegate (HostSummary a, HostSummary b) { return b.LastSeen.CompareTo(a.LastSeen); });

            for (int i = data.Entries.Count - 1; i >= 0; i--)
                data.RawLinesNewestFirst.Add(data.Entries[i]);

            return data;
        }

        private static bool TrySplitStamp(string line, out DateTime stamp, out string rest)
        {
            stamp = DateTime.MinValue;
            rest = "";
            // Stamp is always the first 19 characters: "yyyy-MM-dd HH:mm:ss".
            if (line.Length < 19) return false;
            string head = line.Substring(0, 19);
            if (!DateTime.TryParseExact(head, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out stamp)) return false;
            rest = line.Length > 19 ? line.Substring(19).TrimStart() : "";
            return true;
        }

        private static string ExtractField(string s, string marker)
        {
            int i = s.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return "";
            i += marker.Length;
            int end = s.IndexOf(' ', i);
            return end < 0 ? s.Substring(i) : s.Substring(i, end - i);
        }
    }
}
