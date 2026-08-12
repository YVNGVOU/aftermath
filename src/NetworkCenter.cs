// Network page: promoted out of the System tab group into its own sidebar
// page (Plus+, same gate as the System page) so live network state gets room
// to breathe instead of sharing a TabStrip slot. Everything here is real,
// on-demand data - no fabricated enforcement, no "blocked" numbers invented
// when Windows Firewall logging is simply off (the common case).
//
//   - Status header: which network profile(s) Windows has stored, read from
//     the registry (NetworkList\Profiles). "Current" is a best-effort guess -
//     the most recently connected profile - not a live COM query, and is
//     labelled that way rather than asserted as fact.
//   - Live connections: Net.Tcp() (Deep.cs) called fresh on demand, joined to
//     process name/path exactly like Deep.Network() already does, run through
//     the same UserWritable + Signature.CheckEmbedded heuristic.
//   - Trusted networks: every profile Windows has stored, not just the
//     current one - simple read-only cards, not Finding data.
//   - Blocked connections: best-effort read of the Windows Firewall's own
//     security event log (event ID 5157). Most machines have this logging
//     switched off, so an empty result is the expected common case.
//
// Read-only throughout. Aftermath cannot block or allow traffic - it can
// only show what Windows already recorded.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Aftermath
{
    public class NetworkCenter : Panel
    {
        // ---------- status header ----------
        private readonly ProfileCard pnlStatus = new ProfileCard();

        // ---------- trusted networks ----------
        private readonly Label lblTrustedTitle = new Label();
        private readonly TrustedNetworksCard pnlTrusted = new TrustedNetworksCard();

        // ---------- blocked connections ----------
        private readonly Label lblBlockedTitle = new Label();
        private readonly FindingList blockedList = new FindingList();

        // ---------- live connections ----------
        private readonly Panel connHead = new Panel();
        private readonly Label lblConnTitle = new Label();
        private Button btnRefreshConn;
        private readonly FilterBar connFilterBar = new FilterBar();
        private readonly FindingList connList = new FindingList();

        private readonly Label lblNote = new Label();

        private List<Finding> allConn = new List<Finding>();

        // Locations a normal program has no business launching itself from -
        // same rule Deep.cs's UserWritable uses; duplicated here rather than
        // made public on Deep.Deep since that class keeps everything private
        // except the two entry points Ui.cs already calls.
        private static bool UserWritable(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var low = path.ToLowerInvariant();
            return low.Contains("\\appdata\\") || low.Contains("\\temp\\") ||
                   low.Contains("\\programdata\\") || low.Contains("\\users\\public\\") ||
                   low.Contains("\\downloads\\");
        }

        public NetworkCenter()
        {
            Dock = DockStyle.Fill;
            DoubleBuffered = true;
            Padding = new Padding(22, 10, 22, 10);

            connList.Dock = DockStyle.Fill;
            connList.EmptyText = "No active connections or listening ports right now.";
            connList.ItemOpened += delegate { };
            Controls.Add(connList);

            connFilterBar.Dock = DockStyle.Top;
            connFilterBar.FiltersChanged += delegate { ApplyConnFilter(); };
            Controls.Add(connFilterBar);

            connHead.Dock = DockStyle.Top;
            connHead.Height = 34;
            lblConnTitle.Text = "Live connections";
            lblConnTitle.Font = Brand.F(9.5f, FontStyle.Bold);
            lblConnTitle.Location = new Point(0, 8);
            lblConnTitle.AutoSize = true;
            connHead.Controls.Add(lblConnTitle);
            btnRefreshConn = Flat("↻ Refresh", 110, 26);
            btnRefreshConn.Location = new Point(140, 4);
            btnRefreshConn.Click += delegate { RefreshConnections(); };
            connHead.Controls.Add(btnRefreshConn);
            connHead.Resize += delegate { };
            Controls.Add(connHead);

            blockedList.Dock = DockStyle.Top;
            blockedList.Height = 190;
            blockedList.EmptyText = "No blocked connections logged. Windows Firewall logging may not be enabled.";
            Controls.Add(blockedList);

            lblBlockedTitle.Text = "Blocked connections (last 30 days)";
            lblBlockedTitle.Font = Brand.F(9.5f, FontStyle.Bold);
            lblBlockedTitle.Dock = DockStyle.Top;
            lblBlockedTitle.Height = 26;
            lblBlockedTitle.Padding = new Padding(0, 10, 0, 0);
            Controls.Add(lblBlockedTitle);

            pnlTrusted.Dock = DockStyle.Top;
            pnlTrusted.Height = 110;
            Controls.Add(pnlTrusted);

            lblTrustedTitle.Text = "Networks Windows has stored on this machine";
            lblTrustedTitle.Font = Brand.F(9.5f, FontStyle.Bold);
            lblTrustedTitle.Dock = DockStyle.Top;
            lblTrustedTitle.Height = 26;
            lblTrustedTitle.Padding = new Padding(0, 10, 0, 0);
            Controls.Add(lblTrustedTitle);

            lblNote.Text = "Read-only: Aftermath can show you what is connected and what Windows Firewall has logged, but it cannot block or allow traffic itself.";
            lblNote.Dock = DockStyle.Top;
            lblNote.Height = 24;
            lblNote.Padding = new Padding(0, 6, 0, 0);
            Controls.Add(lblNote);

            pnlStatus.Dock = DockStyle.Top;
            pnlStatus.Height = 96;
            Controls.Add(pnlStatus);
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

        public void Restyle()
        {
            var p = Theme.P;
            BackColor = p.Bg;
            connHead.BackColor = p.Bg;
            connList.BackColor = p.Bg;
            blockedList.BackColor = p.Bg;
            connFilterBar.BackColor = p.Bg;
            connFilterBar.Invalidate();

            lblConnTitle.ForeColor = p.Text;
            lblTrustedTitle.ForeColor = p.Text;
            lblBlockedTitle.ForeColor = p.Text;
            lblNote.ForeColor = p.TextDim;

            Color soft = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.16 : 0.10);
            Color softBorder = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.34 : 0.24);
            Color softHover = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.24 : 0.17);
            btnRefreshConn.BackColor = soft;
            btnRefreshConn.ForeColor = p.Text;
            btnRefreshConn.FlatAppearance.BorderSize = 1;
            btnRefreshConn.FlatAppearance.BorderColor = softBorder;
            btnRefreshConn.FlatAppearance.MouseOverBackColor = softHover;

            pnlStatus.Restyle();
            pnlTrusted.Restyle();
            Invalidate(true);
        }

        // Called from Ui.cs's ShowPage every time this page is navigated to -
        // a live snapshot rather than something that only refreshes on scan.
        public void RefreshData()
        {
            RefreshProfile();
            RefreshTrusted();
            RefreshBlocked();
            RefreshConnections();
        }

        // ---------- status header ----------

        private void RefreshProfile()
        {
            var profiles = ReadProfiles();
            if (profiles.Count == 0)
            {
                pnlStatus.SetUnknown();
                return;
            }
            // Best-effort "current" guess: the profile Windows most recently
            // recorded a connection for. Real data (DateLastConnected, a
            // value Windows itself writes), but a guess about "now" rather
            // than a live query - labelled as such in ProfileCard's paint.
            var current = profiles.OrderByDescending(x => x.LastConnected).First();
            pnlStatus.SetCurrent(current.Name, CategoryText(current.Category));
        }

        private static string CategoryText(int cat)
        {
            if (cat == 0) return "Public";
            if (cat == 1) return "Private";
            if (cat == 2) return "Domain";
            return "Unknown";
        }

        private class ProfileEntry
        {
            public string Name;
            public int Category;
            public DateTime LastConnected;
        }

        private List<ProfileEntry> ReadProfiles()
        {
            var outp = new List<ProfileEntry>();
            try
            {
                using (var root = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles"))
                {
                    if (root == null) return outp;
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = root.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                string name = Convert.ToString(k.GetValue("ProfileName"));
                                if (string.IsNullOrEmpty(name)) name = "(unnamed network)";
                                int cat = 0;
                                try { cat = Convert.ToInt32(k.GetValue("Category")); } catch { }
                                DateTime last = DateTime.MinValue;
                                var bytes = k.GetValue("DateLastConnected") as byte[];
                                if (bytes != null && bytes.Length >= 8)
                                {
                                    try { last = new DateTime(BitConverter.ToInt64(bytes, 0), DateTimeKind.Utc).ToLocalTime(); }
                                    catch { }
                                }
                                outp.Add(new ProfileEntry { Name = name, Category = cat, LastConnected = last });
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return outp;
        }

        // ---------- trusted networks ----------

        private void RefreshTrusted()
        {
            var profiles = ReadProfiles();
            if (profiles.Count == 0)
            {
                pnlTrusted.SetEmpty("Could not read stored networks from the registry.");
                return;
            }
            var rows = profiles.OrderByDescending(x => x.LastConnected)
                                .Select(x => x.Name + "   -   " + CategoryText(x.Category))
                                .ToList();
            pnlTrusted.SetRows(rows);
            pnlTrusted.Height = Math.Max(60, Math.Min(200, 34 + rows.Count * 24));
        }

        // ---------- blocked connections ----------

        private void RefreshBlocked()
        {
            var findings = new List<Finding>();
            try
            {
                // Real channel name for Windows Firewall's own security log.
                // Not present/enabled on most machines - that is the expected
                // common case (see EmptyText above), not an error.
                using (var log = new EventLog("Microsoft-Windows-Windows Firewall With Advanced Security/Firewall"))
                {
                    var cutoff = DateTime.Now.AddDays(-30);
                    var entries = log.Entries;
                    int scanned = 0;
                    for (int i = entries.Count - 1; i >= 0 && scanned < 2000; i--, scanned++)
                    {
                        EventLogEntry e;
                        try { e = entries[i]; } catch { continue; }
                        if (e.TimeGenerated < cutoff) break;
                        if (e.InstanceId != 5157) continue;   // blocked connection

                        string msg = e.Message ?? "";
                        var f = new Finding("Network", "Blocked connection", msg, null, Sev.Info, false);
                        f.When = e.TimeGenerated;
                        findings.Add(f);
                    }
                }
            }
            catch
            {
                // Best-effort, same silent-skip pattern ExternalAv.cs uses for
                // its own event log reading - firewall logging off, channel
                // absent, or access denied are all legitimate "nothing to
                // show" cases, not scan failures.
            }
            blockedList.SetItems(findings.OrderByDescending(f => f.When));
        }

        // ---------- live connections ----------

        private void RefreshConnections()
        {
            var procPaths = new Dictionary<int, string[]>();
            foreach (var p in Process.GetProcesses())
            {
                string path = "";
                try { path = p.MainModule != null ? p.MainModule.FileName : ""; }
                catch { path = ""; }
                try { procPaths[p.Id] = new string[] { p.ProcessName, path }; }
                catch { }
            }

            var conns = Net.Tcp();
            var outp = new List<Finding>();

            var established = conns.Where(c => c.State == Net.ESTABLISHED &&
                                               !c.Remote.StartsWith("127.") &&
                                               !c.Remote.StartsWith("0.0.0.0")).ToList();
            foreach (var c in established)
            {
                string name = "(unknown)", path = "";
                if (procPaths.ContainsKey(c.Pid)) { name = procPaths[c.Pid][0]; path = procPaths[c.Pid][1]; }

                bool odd = UserWritable(path);
                string sig = "";
                if (odd && !string.IsNullOrEmpty(path))
                {
                    sig = Signature.CheckEmbedded(path);
                    if (sig == "Valid") odd = false;
                }

                var f = new Finding("Network", name + " -> " + c.Remote,
                    "Local " + c.Local + "   Remote " + c.Remote +
                    (string.IsNullOrEmpty(path) ? "" : "   " + path) +
                    (odd ? "   Signature: " + (string.IsNullOrEmpty(sig) ? "unknown" : sig) : ""),
                    path, odd ? Sev.Warn : Sev.Info, false);
                f.Count = 0;   // Established tag for the State filter below.
                f.Category = "Network:Established";
                outp.Add(f);
            }

            var listeners = conns.Where(c => c.State == Net.LISTEN && !c.Local.StartsWith("127.")).ToList();
            foreach (var c in listeners)
            {
                string name = "(unknown)", path = "";
                if (procPaths.ContainsKey(c.Pid)) { name = procPaths[c.Pid][0]; path = procPaths[c.Pid][1]; }

                bool odd = UserWritable(path) && Signature.CheckEmbedded(path) != "Valid";

                var f = new Finding("Network", "Listening on " + c.Local + " (" + name + ")",
                    "Accepting incoming connections." + (string.IsNullOrEmpty(path) ? "" : "   " + path),
                    path, odd ? Sev.Warn : Sev.Info, false);
                f.Category = "Network:Listening";
                outp.Add(f);
            }

            allConn = outp;

            connFilterBar.SetFilterGroups(new List<FilterGroup> {
                new FilterGroup { Label = "Severity", Options = new List<string> { "Check", "Info" } },
                new FilterGroup { Label = "State", Options = new List<string> { "Established", "Listening" } }
            });
            connFilterBar.SetSortOptions(new List<string> { "Severity", "Name" });
            ApplyConnFilter();
        }

        private void ApplyConnFilter()
        {
            var active = connFilterBar.ActiveFilters;
            IEnumerable<Finding> q = allConn;

            bool wantWarn = active.Contains("Severity:Check");
            bool wantInfo = active.Contains("Severity:Info");
            if (wantWarn || wantInfo)
                q = q.Where(f => (wantWarn && f.Severity == Sev.Warn) || (wantInfo && f.Severity == Sev.Info));

            bool wantEst = active.Contains("State:Established");
            bool wantListen = active.Contains("State:Listening");
            if (wantEst || wantListen)
                q = q.Where(f => (wantEst && f.Category == "Network:Established") ||
                                  (wantListen && f.Category == "Network:Listening"));

            if (connFilterBar.ActiveSort == "Name") q = q.OrderBy(f => f.Title);
            else q = q.OrderByDescending(f => (int)f.Severity).ThenBy(f => f.Title);

            connList.SetItems(q.ToList());
        }

        // ---------- small painted cards ----------

        private class ProfileCard : Panel
        {
            private string title = "Network profile";
            private string sub = "Reading...";

            public ProfileCard() { DoubleBuffered = true; }

            public void SetCurrent(string name, string category)
            {
                title = name + "   -   " + category;
                sub = "Most recently connected profile Windows has recorded. Not a live query.";
                Invalidate();
            }

            public void SetUnknown()
            {
                title = "Could not determine network profile";
                sub = "Windows did not have a stored network profile to read.";
                Invalidate();
            }

            public void Restyle() { Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var p = Theme.P;
                var g = e.Graphics;
                g.Clear(p.Bg);
                var card = new Rectangle(0, 4, Width - 1, Height - 12);
                Draw.RoundRect(g, card, 10, p.SurfaceElevated);
                Draw.RoundRectOutline(g, card, 10, p.BorderStandard);
                Draw.RoundRect(g, new Rectangle(card.X, card.Y + 8, 3, card.Height - 16), 2, p.Accent);

                TextRenderer.DrawText(g, title, Brand.F(11f, FontStyle.Bold),
                    new Rectangle(card.X + 20, card.Y + 14, card.Width - 40, 24), p.Text,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, sub, Brand.F(8.5f),
                    new Rectangle(card.X + 20, card.Y + 42, card.Width - 40, 36), p.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.WordBreak);
            }
        }

        private class TrustedNetworksCard : Panel
        {
            private List<string> rows = new List<string>();
            private string empty = "";

            public TrustedNetworksCard() { DoubleBuffered = true; }

            public void SetRows(List<string> r) { rows = r; empty = ""; Invalidate(); }
            public void SetEmpty(string text) { rows = new List<string>(); empty = text; Invalidate(); }
            public void Restyle() { Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var p = Theme.P;
                var g = e.Graphics;
                g.Clear(p.Bg);
                var card = new Rectangle(0, 0, Width - 1, Height - 8);
                Draw.RoundRect(g, card, 10, p.SurfaceElevated);
                Draw.RoundRectOutline(g, card, 10, p.BorderStandard);

                if (rows.Count == 0)
                {
                    TextRenderer.DrawText(g, string.IsNullOrEmpty(empty) ? "No stored networks found." : empty,
                        Brand.F(8.5f), new Rectangle(card.X + 20, card.Y + 10, card.Width - 40, card.Height - 20),
                        p.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                    return;
                }

                int y = card.Y + 8;
                foreach (var row in rows)
                {
                    if (y > card.Bottom - 20) break;
                    TextRenderer.DrawText(g, row, Brand.F(9f),
                        new Rectangle(card.X + 20, y, card.Width - 40, 22), p.Text,
                        TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                    y += 24;
                }
            }
        }
    }
}
