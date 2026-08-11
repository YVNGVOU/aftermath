// Aftermath shell: collapsible sidebar, one page per area, overview dashboard.
// No TabControl and no ListView anywhere - both draw with system colours and
// cannot be themed reliably.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Aftermath
{
    public class MainForm : Form
    {
        private Button btnScan, btnElevate, btnMenu, btnQuarantine, btnExport, btnPro, btnWorkspace;
        private LinkLabel lnkPermDelete;
        private Label lblStatus, lblPageTitle, lblPageHelp;
        private BrandMark brandMark;
        private PlanPill planPill;
        private InfoCard proCard;

        // Points at the published feature roadmap - what free covers today and
        // what Pro adds. Kept as one constant so it is easy to repoint later.
        private const string RoadmapUrl = "https://claude.ai/code/artifact/1f64caa8-a291-43a4-98b4-a40c65fc8906";
        private Label lblVerdict, lblVerdictSub, lblLastScan, lblEyebrow;
        private StatusDot statusDot;
        private Panel header, pageHead, bottom, contentHost, overview;
        // Settings workspace pages - hidden/shown by ShowPage exactly like the
        // Aftermath pages above, just a second set of Panels in the same
        // contentHost rather than a second content area.
        private Panel settingsHome, pgAppearance, pgAdmin, pgScanBehavior, pgData, pgConnections, pgAbout;
        private UpgradePage pgUpgrade;
        private Sidebar nav;
        private ProgressBar bar;
        private Dictionary<string, FindingList> lists = new Dictionary<string, FindingList>();
        private FindingList cleanupList;
        private FindingList driftList;
        private QuarantineList quarantineList;
        private Panel sweepPage, sweepInputs, sweepResultsHost;
        private TextBox txtSweepHosts, txtSweepUser, txtSweepPass;
        private Button btnSweep;
        private Label lblSweepHostsCaption, lblSweepHelp, lblSweepUserCaption, lblSweepPassCaption,
            lblSweepSummary, lblSweepHostsListCaption, lblSweepFindingsCaption;
        private SweepList sweepList;
        private FindingList sweepFindingsList;
        private List<SweepTarget> sweepTargets = new List<SweepTarget>();
        private StatTile tSerious, tCheck, tOk, tRemovable;
        private TriageResult last;
        private DateTime lastScan = DateTime.MinValue;
        private bool busy;
        // True only while a scan's background thread is running - lets SetStatus
        // mirror its log lines onto the Overview hero without also doing that
        // during quarantine/delete, which reuse the same busy flag and status bar.
        private bool scanning;
        private bool autoScanOnLoad;

        // True once the header's workspace-switch control has been used to enter
        // Settings - drives which item set nav shows and which page set is live.
        private bool inSettings;

        // Settings workspace controls - see BuildSettings*() below. Kept as
        // fields only where a handler or a Refresh* method needs to reach them
        // again later, same rule the rest of this class already follows.
        private Toggle toggleTheme, toggleAutoTrigger, toggleScheduledDrift;
        private SettingRow rowAppearance, rowAdmin, rowScanBehavior, rowScheduledDrift, rowRetention;
        private Label lblStoragePaths;
        private Panel retentionControls;
        private TextBox txtRetentionDays;
        private Button btnSaveRetention;
        private DangerZone zoneRetention;
        private ConnectionCard connCard;
        private TextBox txtLicenseKey;
        private Button btnActivateLicense;
        private Label lblLicenseStatus;
        private Label lblSettingsIntro, lblStorageCaption, lblConnectionsCaption, lblAboutName,
            lblAboutBody, lblRetentionDaysCaption;

        private const string Overview = "Overview";
        private const string Cleanup = "Cleanup";
        private const string Quarantine = "Quarantine";
        private const string Drift = "Drift";
        private const string Sweep = "Sweep";
        private const string Upgrade = "Upgrade";

        // Settings workspace page keys. Same convention as the Aftermath keys
        // above: the string IS both the Sidebar item key and, via ShowPage, the
        // literal page-header title - so each one is written the way it should
        // read on screen rather than as a separate label.
        private const string SettingsHome = "Settings";
        private const string SetAppearance = "Appearance";
        private const string SetAdmin = "Administrator";
        private const string SetScanBehavior = "Scan Behavior";
        private const string SetData = "Data";
        private const string SetConnections = "Connections";
        private const string SetAbout = "About";

        // Exact count of sequential steps OnScan's background thread runs through -
        // the 9 named scanner/deep calls plus the inline baseline-compare step.
        // Drives the determinate progress bar; never rounded or estimated.
        private const int ScanStepCount = 10;

        private static readonly string[] Cats = new string[]
            { "Detections", "Exposure", "History", "Artifacts", "Startup", "Persistence", "Network", "System" };

        private static readonly Dictionary<string, string> Glyphs = new Dictionary<string, string>
        {
            { Overview,     "⌂" },
            { "Detections", "⚠" },
            { "Exposure",   "🔑" },
            { "History",    "🕘" },
            { "Artifacts",  "📦" },
            { "Startup",    "⏻" },
            { "Persistence","⚙" },
            { "Network",    "🌐" },
            { "System",     "🛡" },
            { Drift,        "⇵" },
            { Cleanup,      "🗑" },
            { Quarantine,   "🔒" },
            { Sweep,        "🛰" },
            { Upgrade,      "⭐" }
        };

        private static readonly Dictionary<string, string> Help = new Dictionary<string, string>
        {
            { Overview,      "A summary of this machine, and what to do next." },
            { "Detections",  "What your antivirus has already caught here. Recent and serious items first." },
            { "Exposure",    "What a password stealer could have reached, and what to change first. Nothing here is opened or read - only listed." },
            { "History",     "What actually ran on this PC and where it was downloaded from. These records outlive the files themselves." },
            { "Artifacts",   "Leftovers from pirated software: disk images, crack and keygen files, installers modified after signing." },
            { "Startup",     "Everything that launches when you sign in, checked by digital signature rather than by name." },
            { "Persistence", "Hiding places malware uses to survive a reboot: services, scheduled tasks, WMI event subscriptions." },
            { "Network",     "What is talking to the internet right now, and which program owns each connection." },
            { "System",      "Settings malware likes to change: proxy redirection, blocked updates, hidden antivirus exclusions." },
            { Drift,         "What changed since your last scan: new startup entries, persistence, listening ports, and settings tracked by Baseline." },
            { Cleanup,       "Tick what you want gone, then quarantine it. Quarantined items are moved aside, not destroyed - you can restore them from the Quarantine page. Protected system locations are refused no matter what." },
            { Quarantine,    "Items you have quarantined. Restore one back to where it came from, or delete the quarantined copy permanently." },
            { Sweep,         "Push this same triage out to hosts you list below and pull the results back. Aftermath only ever touches a host you have typed into the list yourself - there is no scan-the-network button." },
            { Upgrade,       "Compare tiers and see what each one unlocks." },
            { SettingsHome,   "Configure how SINVAUX operates on this machine." },
            { SetAppearance,  "Choose between a dark or light colour scheme." },
            { SetAdmin,       "Check whether Aftermath is running with Administrator rights, and relaunch elevated if not." },
            { SetScanBehavior,"Choose whether Aftermath triages automatically right after Windows Defender scans." },
            { SetData,        "Where SINVAUX stores its files on this machine, and how long quarantined items are kept." },
            { SetConnections, "External services SINVAUX connects to." },
            { SetAbout,       "Product information." }
        };

        public MainForm() : this(false) { }

        // autoScan is set when launched with --auto (the post-Defender-scan
        // scheduled task). The window still opens normally - this only starts a
        // triage automatically once it has loaded, rather than the user having to
        // click Run Triage themselves.
        public MainForm(bool autoScan)
        {
            autoScanOnLoad = autoScan;
            Theme.Load();

            Text = Brand.FullName;
            Width = 1240;
            Height = 840;
            MinimumSize = new Size(900, 620);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Brand.F(9f);
            DoubleBuffered = true;

            var ico = Brand.AppIcon();
            if (ico != null) Icon = ico;

            BuildHeader();
            BuildBottom();
            BuildSidebar();
            BuildContent();

            // Fill first, then edges: docking resolves in reverse order of addition.
            Controls.Add(contentHost);
            Controls.Add(nav);
            Controls.Add(bottom);
            Controls.Add(header);

            // btnScan/btnWorkspace are positioned entirely by PositionHeaderButtons,
            // wired to header.Resize - but WinForms does not run a real layout pass
            // (header.ClientSize reflecting the actual window width) until the Form
            // itself is shown. Calling this anywhere in the constructor, even after
            // Controls.Add, still sees a stale/default width and misplaces both
            // buttons off to the left. The Form's own Load event is the first point
            // both controls are guaranteed real, final sizes.
            Load += delegate { PositionHeaderButtons(); };

            ApplyTheme();
            RefreshQuarantine();
            RefreshDriftEmptyText();
            RefreshAutoTriggerToggle();
            RefreshScheduledDriftToggle();
            nav.Select(Overview);

            if (autoScanOnLoad) Load += delegate { OnScan(this, EventArgs.Empty); };
        }

        // Before any scan this session, the Drift page's empty state depends on
        // whether a baseline.json from an earlier run already exists on disk.
        private void RefreshDriftEmptyText()
        {
            driftList.EmptyText = (BaselineStore.Load() == null)
                ? "Run a triage to establish a baseline. Next time, this page will show what changed."
                : "No changes since your last scan.";
        }

        // ---------- construction ----------

        private void BuildHeader()
        {
            header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 62;

            btnMenu = Flat("☰", 40, 32);
            btnMenu.Location = new Point(12, 15);
            btnMenu.Font = new Font("Segoe UI Symbol", 11f);
            btnMenu.Click += delegate { nav.Toggle(); };
            header.Controls.Add(btnMenu);

            brandMark = new BrandMark();
            brandMark.Location = new Point(62, 14);
            brandMark.Size = new Size(320, 34);
            header.Controls.Add(brandMark);

            planPill = new PlanPill();
            planPill.Location = new Point(388, 21);
            planPill.Label = Entitlements.Current.Tier.ToString().ToUpperInvariant();
            header.Controls.Add(planPill);

            btnScan = Flat("Run Triage", 140, 34);
            btnScan.Top = 14;
            btnScan.Click += OnScan;
            header.Controls.Add(btnScan);

            // The one control that switches workspaces - reads "-> Settings" in
            // Aftermath and "<- Aftermath" in Settings, rather than two separate
            // buttons for the two directions. btnTheme and btnElevate used to live
            // here; both moved to Settings pages (Appearance, Administrator) -
            // see BuildSettings*() - since neither is a global, always-relevant
            // action the way Run Triage and the workspace switch are.
            btnWorkspace = Flat("⚙ Settings", 116, 28);
            btnWorkspace.Top = 17;
            btnWorkspace.Click += delegate { SwitchWorkspace(!inSettings); };
            header.Controls.Add(btnWorkspace);

            header.Resize += delegate { PositionHeaderButtons(); };

            header.Paint += HeaderPaint;
        }

        // Right-aligns btnScan (only in the Aftermath workspace - Settings has no
        // scan to run) and btnWorkspace next to it, or alone at the right edge
        // when btnScan is hidden. Called from header.Resize and again from
        // SwitchWorkspace, since toggling Visible does not raise Resize.
        private void PositionHeaderButtons()
        {
            int right = header.ClientSize.Width - 18;
            if (btnScan.Visible)
            {
                btnScan.Left = right - btnScan.Width;
                btnWorkspace.Left = btnScan.Left - btnWorkspace.Width - 10;
            }
            else
            {
                btnWorkspace.Left = right - btnWorkspace.Width;
            }
        }

        // Structural divider between header and content, drawn along the header's
        // bottom edge - none of the header's child controls reach this far down,
        // so it paints on bare panel background. Carries the one deliberate
        // accent-on-a-structural-line moment in the app: a short Grenat segment
        // directly under the wordmark, never the full width.
        private void HeaderPaint(object sender, PaintEventArgs e)
        {
            var p = Theme.P;
            var g = e.Graphics;
            int y = header.Height - 1;
            int w = header.ClientSize.Width;

            using (var pen = new Pen(p.BorderStrong))
                g.DrawLine(pen, 0, y, w, y);

            int ax = brandMark.Left;
            int aw = Math.Min(48, Math.Max(0, w - ax));
            if (aw > 0)
                using (var pen = new Pen(p.Accent))
                    g.DrawLine(pen, ax, y, ax + aw, y);
        }

        private void BuildBottom()
        {
            bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 34;

            lblStatus = new Label();
            lblStatus.Text = "Ready.";
            lblStatus.Location = new Point(16, 9);
            lblStatus.AutoSize = true;
            bottom.Controls.Add(lblStatus);

            bar = new ProgressBar();
            bar.Top = 11;
            bar.Width = 150;
            bar.Height = 8;
            bar.Style = ProgressBarStyle.Marquee;
            bar.Visible = false;
            bottom.Controls.Add(bar);

            bottom.Resize += delegate { bar.Left = bottom.ClientSize.Width - bar.Width - 18; };

            // Structural divider between content and the status bar - top edge only.
            bottom.Paint += delegate (object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.P.BorderStrong))
                    e.Graphics.DrawLine(pen, 0, 0, bottom.ClientSize.Width, 0);
            };
        }

        private void BuildSidebar()
        {
            nav = new Sidebar();
            PopulateAftermathNav();
            nav.Navigated += delegate (object s, string key) { ShowPage(key); };
        }

        // Builds the Aftermath item set. Extracted out of BuildSidebar so both
        // the initial app startup and SwitchWorkspace's "back to Aftermath" path
        // call the same code rather than keeping two copies in sync. Sidebar has
        // no rebuild support beyond Clear() (append-only Add/AddSection), so a
        // workspace switch always clears first and repopulates from scratch.
        private void PopulateAftermathNav()
        {
            nav.Add(Overview, Overview, Glyphs[Overview]);

            // Cats is ordered investigation-first, system-second (see its
            // declaration above), so the grouping below is a straight split of
            // the same array rather than a second list of page keys to keep in
            // sync with it.
            nav.AddSection("Investigation");
            // Cats[0..2] (Detections/Exposure/History) are Free-tier; Cats[3]
            // (Artifacts) is Plus+ per Entitlements.HasArtifactsPages - gated
            // fully absent below Plus, never a fake disabled preview.
            for (int i = 0; i < 3; i++) nav.Add(Cats[i], Cats[i], Glyphs[Cats[i]]);
            if (Entitlements.Current.HasArtifactsPages)
                nav.Add(Cats[3], Cats[3], Glyphs[Cats[3]]);
            nav.Add(Drift, Drift, Glyphs[Drift]);

            if (Entitlements.Current.HasArtifactsPages)
            {
                nav.AddSection("System");
                for (int i = 4; i < Cats.Length; i++) nav.Add(Cats[i], Cats[i], Glyphs[Cats[i]]);
            }

            nav.AddSection("Response");
            nav.Add(Cleanup, Cleanup, Glyphs[Cleanup]);
            nav.Add(Quarantine, Quarantine, Glyphs[Quarantine]);
            nav.Add(Sweep, Sweep, Glyphs[Sweep]);

            // Upgrade is visible to every tier, including Enterprise, so users
            // can always see the ladder.
            nav.AddSection("Account");
            nav.Add(Upgrade, Upgrade, Glyphs[Upgrade]);
        }

        // Builds the Settings item set - real capabilities only, per the audit:
        // Appearance (theme), Administrator (elevation), Scan Behavior
        // (auto-trigger), Data (storage paths + quarantine retention),
        // Connections (honest empty state), About (product info).
        private void PopulateSettingsNav()
        {
            nav.Add(SettingsHome, SettingsHome, "⚙");
            nav.AddSection("General");
            nav.Add(SetAppearance, SetAppearance, "◐");
            nav.AddSection("Security");
            nav.Add(SetAdmin, SetAdmin, "🛡");
            nav.AddSection("Scanning");
            nav.Add(SetScanBehavior, SetScanBehavior, "⏱");
            nav.AddSection("Data");
            nav.Add(SetData, SetData, "🗄");
            nav.AddSection("Connections");
            nav.Add(SetConnections, SetConnections, "🔗");
            nav.AddSection("About");
            nav.Add(SetAbout, SetAbout, "ℹ");
        }

        // The single control that swaps the one Sidebar instance between the
        // Aftermath and Settings item sets, and the one page set in contentHost
        // between the two matching page sets - see ShowPage, which this relies
        // on rather than a second visibility mechanism.
        private void SwitchWorkspace(bool toSettings)
        {
            inSettings = toSettings;
            brandMark.SubLabel = toSettings ? "Settings" : null;
            btnWorkspace.Text = toSettings ? "← Aftermath" : "⚙ Settings";
            btnScan.Visible = !toSettings;
            brandMark.Invalidate();
            PositionHeaderButtons();

            nav.Clear();
            if (toSettings)
            {
                PopulateSettingsNav();
                nav.Select(SettingsHome);
            }
            else
            {
                PopulateAftermathNav();
                nav.Select(Overview);
            }
        }

        private void BuildContent()
        {
            contentHost = new Panel();
            contentHost.Dock = DockStyle.Fill;

            pageHead = new Panel();
            pageHead.Dock = DockStyle.Top;
            pageHead.Height = 66;

            lblPageTitle = new Label();
            lblPageTitle.Font = Brand.F(13f, FontStyle.Bold);
            lblPageTitle.Location = new Point(22, 12);
            lblPageTitle.AutoSize = true;
            pageHead.Controls.Add(lblPageTitle);

            lblPageHelp = new Label();
            lblPageHelp.Location = new Point(24, 39);
            lblPageHelp.AutoSize = false;
            lblPageHelp.Height = 20;
            pageHead.Controls.Add(lblPageHelp);
            pageHead.Resize += delegate { lblPageHelp.Width = pageHead.ClientSize.Width - 40; };

            // one list per category
            foreach (var c in Cats)
            {
                var fl = new FindingList();
                fl.Dock = DockStyle.Fill;
                fl.Visible = false;
                lists[c] = fl;
                contentHost.Controls.Add(fl);
            }

            // cleanup page
            cleanupList = new FindingList();
            cleanupList.Dock = DockStyle.Fill;
            cleanupList.Checkable = true;
            cleanupList.Visible = false;
            cleanupList.EmptyText = "Nothing to remove. Either nothing was found, or it is already gone.";
            cleanupList.CheckedChanged += delegate
            {
                int n = cleanupList.CheckedItems.Count;
                btnQuarantine.Enabled = n > 0 && !busy;
                btnQuarantine.Text = n > 0 ? "Quarantine " + n + " item(s)" : "Quarantine Checked";
                // lnkPermDelete stays enabled at all times - see its construction
                // comment for why toggling Enabled on a LinkLabel is worth avoiding.
            };
            contentHost.Controls.Add(cleanupList);

            // drift page - reuses FindingList to display DriftEntry objects
            // converted to Findings; EmptyText is set after construction, once
            // BaselineStore can be consulted.
            driftList = new FindingList();
            driftList.Dock = DockStyle.Fill;
            driftList.Visible = false;
            contentHost.Controls.Add(driftList);

            // quarantine page
            quarantineList = new QuarantineList();
            quarantineList.Dock = DockStyle.Fill;
            quarantineList.Visible = false;
            quarantineList.RestoreClicked += OnRestoreQuarantine;
            quarantineList.DeleteClicked += OnDeleteQuarantinePermanently;
            contentHost.Controls.Add(quarantineList);

            BuildSweep();
            contentHost.Controls.Add(sweepPage);

            BuildOverview();
            contentHost.Controls.Add(overview);

            pgUpgrade = new UpgradePage();
            pgUpgrade.Visible = false;
            contentHost.Controls.Add(pgUpgrade);

            BuildSettingsHome();
            contentHost.Controls.Add(settingsHome);
            BuildSettingsAppearance();
            contentHost.Controls.Add(pgAppearance);
            BuildSettingsAdmin();
            contentHost.Controls.Add(pgAdmin);
            BuildSettingsScanBehavior();
            contentHost.Controls.Add(pgScanBehavior);
            BuildSettingsData();
            contentHost.Controls.Add(pgData);
            BuildSettingsConnections();
            contentHost.Controls.Add(pgConnections);
            BuildSettingsAbout();
            contentHost.Controls.Add(pgAbout);

            contentHost.Controls.Add(pageHead);
        }

        // Sweep page: target hosts (typed only, never discovered), credentials
        // that live only for the duration of one sweep, a live per-host status
        // list, and a details area reusing FindingList for the selected host's
        // findings. See Sweep.cs for the two hard rules this UI cannot violate.
        private void BuildSweep()
        {
            sweepPage = new Panel();
            sweepPage.Dock = DockStyle.Fill;
            sweepPage.Visible = false;

            var inputs = new Panel();
            sweepInputs = inputs;
            inputs.Dock = DockStyle.Top;
            inputs.Height = 260;
            inputs.Padding = new Padding(22, 10, 22, 0);

            lblSweepHostsCaption = new Label();
            lblSweepHostsCaption.Text = "Target hosts";
            lblSweepHostsCaption.Font = Brand.F(9.5f, FontStyle.Bold);
            lblSweepHostsCaption.Location = new Point(22, 8);
            lblSweepHostsCaption.AutoSize = true;
            inputs.Controls.Add(lblSweepHostsCaption);

            // Starts completely empty on purpose - see the hard rule at the top
            // of Sweep.cs. Nothing populates this box except what the admin types
            // or pastes.
            txtSweepHosts = new TextBox();
            txtSweepHosts.Multiline = true;
            txtSweepHosts.ScrollBars = ScrollBars.Vertical;
            txtSweepHosts.Location = new Point(22, 30);
            txtSweepHosts.Size = new Size(420, 96);
            inputs.Controls.Add(txtSweepHosts);

            lblSweepHelp = new Label();
            lblSweepHelp.Text = "Enter hostnames or IPs, one per line. Aftermath never scans a host you have not listed here.";
            lblSweepHelp.Location = new Point(22, 130);
            lblSweepHelp.AutoSize = false;
            lblSweepHelp.Size = new Size(420, 36);
            inputs.Controls.Add(lblSweepHelp);

            lblSweepUserCaption = new Label();
            lblSweepUserCaption.Text = "Username (with administrator rights on the targets)";
            lblSweepUserCaption.Location = new Point(460, 8);
            lblSweepUserCaption.AutoSize = true;
            inputs.Controls.Add(lblSweepUserCaption);

            txtSweepUser = new TextBox();
            txtSweepUser.Location = new Point(460, 30);
            txtSweepUser.Width = 240;
            inputs.Controls.Add(txtSweepUser);

            lblSweepPassCaption = new Label();
            lblSweepPassCaption.Text = "Password";
            lblSweepPassCaption.Location = new Point(460, 64);
            lblSweepPassCaption.AutoSize = true;
            inputs.Controls.Add(lblSweepPassCaption);

            // Plain TextBox with UseSystemPasswordChar - no need for anything
            // fancier. Cleared once the sweep finishes; see AfterSweepSweep.
            // The real "never touches disk" guarantee lives in Sweep.cs, not here.
            txtSweepPass = new TextBox();
            txtSweepPass.Location = new Point(460, 86);
            txtSweepPass.Width = 240;
            txtSweepPass.UseSystemPasswordChar = true;
            inputs.Controls.Add(txtSweepPass);

            btnSweep = Flat("Sweep Sweep", 160, 34);
            btnSweep.Location = new Point(460, 122);
            btnSweep.Click += OnSweepSweep;
            inputs.Controls.Add(btnSweep);

            lblSweepSummary = new Label();
            lblSweepSummary.Text = "";
            lblSweepSummary.Font = Brand.F(9.5f, FontStyle.Bold);
            lblSweepSummary.Location = new Point(22, 180);
            lblSweepSummary.AutoSize = false;
            lblSweepSummary.Size = new Size(660, 24);
            inputs.Controls.Add(lblSweepSummary);

            lblSweepHostsListCaption = new Label();
            lblSweepHostsListCaption.Text = "Hosts";
            lblSweepHostsListCaption.Font = Brand.F(9.5f, FontStyle.Bold);
            lblSweepHostsListCaption.Location = new Point(22, 214);
            lblSweepHostsListCaption.AutoSize = true;
            inputs.Controls.Add(lblSweepHostsListCaption);

            var resultsHost = new Panel();
            sweepResultsHost = resultsHost;
            resultsHost.Dock = DockStyle.Fill;
            resultsHost.Padding = new Padding(22, 0, 22, 10);

            // Fill added first, Top docks added after - same ordering rule the
            // MainForm constructor already documents, so the last-added Top
            // control (sweepList) ends up outermost/topmost.
            sweepFindingsList = new FindingList();
            sweepFindingsList.Dock = DockStyle.Fill;
            sweepFindingsList.EmptyText = "Select a host above to see its findings.";
            resultsHost.Controls.Add(sweepFindingsList);

            lblSweepFindingsCaption = new Label();
            lblSweepFindingsCaption.Text = "Findings for selected host";
            lblSweepFindingsCaption.Dock = DockStyle.Top;
            lblSweepFindingsCaption.Height = 24;
            resultsHost.Controls.Add(lblSweepFindingsCaption);

            sweepList = new SweepList();
            sweepList.Dock = DockStyle.Top;
            sweepList.Height = 220;
            sweepList.HostSelected += OnSweepHostSelected;
            resultsHost.Controls.Add(sweepList);

            sweepPage.Controls.Add(resultsHost);
            sweepPage.Controls.Add(inputs);
        }

        private void BuildOverview()
        {
            overview = new Panel();
            overview.Dock = DockStyle.Fill;
            overview.Visible = false;
            overview.Padding = new Padding(22, 10, 22, 10);

            // Eyebrow + dot + verdict form one hero block. The dot repeats the same
            // colour lblVerdict already carries (set in UpdateOverview) so status
            // reads at a glance even before the headline text is read.
            lblEyebrow = new Label();
            lblEyebrow.Text = "MACHINE STATUS";
            lblEyebrow.Font = Brand.F(7.5f, FontStyle.Bold);
            lblEyebrow.Location = new Point(22, 8);
            lblEyebrow.AutoSize = true;
            overview.Controls.Add(lblEyebrow);

            statusDot = new StatusDot();
            statusDot.Location = new Point(24, 33);
            overview.Controls.Add(statusDot);

            lblVerdict = new Label();
            lblVerdict.Text = "Not scanned yet";
            lblVerdict.Font = Brand.F(19f, FontStyle.Bold);
            lblVerdict.Location = new Point(40, 24);
            lblVerdict.AutoSize = true;
            overview.Controls.Add(lblVerdict);

            lblVerdictSub = new Label();
            lblVerdictSub.Text = "Click Run Triage. It takes a few seconds - there is no full scan to sit through.";
            lblVerdictSub.Location = new Point(24, 60);
            lblVerdictSub.AutoSize = false;
            lblVerdictSub.Height = 36;
            overview.Controls.Add(lblVerdictSub);

            tSerious = new StatTile(); tSerious.Caption = "Need attention"; tSerious.Location = new Point(22, 108);
            tCheck = new StatTile(); tCheck.Caption = "Worth a look"; tCheck.Location = new Point(186, 108);
            tOk = new StatTile(); tOk.Caption = "Checks passed"; tOk.Location = new Point(350, 108);
            tRemovable = new StatTile(); tRemovable.Caption = "Can be removed"; tRemovable.Location = new Point(514, 108);

            // tOk has no natural destination page - "checks passed" is not a
            // findings list - so it stays non-interactive rather than routing
            // somewhere arbitrary.
            tSerious.Clickable = true;
            tSerious.Click += delegate { nav.Select("Detections"); };
            tCheck.Clickable = true;
            tCheck.Click += delegate { nav.Select("Detections"); };
            tRemovable.Clickable = true;
            tRemovable.Click += delegate { nav.Select(Cleanup); };

            overview.Controls.Add(tSerious);
            overview.Controls.Add(tCheck);
            overview.Controls.Add(tOk);
            overview.Controls.Add(tRemovable);

            lblLastScan = new Label();
            lblLastScan.Location = new Point(24, 212);
            lblLastScan.AutoSize = false;
            lblLastScan.Height = 60;
            overview.Controls.Add(lblLastScan);

            btnExport = Flat("Export Report", 150, 34);
            btnExport.Location = new Point(22, 276);
            btnExport.Enabled = false;
            btnExport.Click += OnExport;
            overview.Controls.Add(btnExport);

            btnQuarantine = Flat("Quarantine Checked", 160, 34);
            btnQuarantine.Location = new Point(186, 276);
            btnQuarantine.Enabled = false;
            btnQuarantine.Click += OnQuarantine;
            overview.Controls.Add(btnQuarantine);

            // Left enabled permanently rather than toggled with the selection count:
            // WinForms paints a disabled LinkLabel by redrawing the text twice, offset
            // one pixel, in a light/shadow pair tuned for the stock grey Control face -
            // on our dark custom background that produced a broken double-ghosted
            // strikethrough look. OnDelete already guards the empty-selection case
            // with its own message, so nothing is lost by leaving this clickable.
            lnkPermDelete = new LinkLabel();
            lnkPermDelete.Text = "Permanently delete instead";
            lnkPermDelete.AutoSize = true;
            lnkPermDelete.Location = new Point(362, 286);
            lnkPermDelete.LinkClicked += delegate { if (!busy) OnDelete(this, EventArgs.Empty); };
            overview.Controls.Add(lnkPermDelete);

            // Free never hides a result it already produced - Pro is sold as more
            // checks and continuous monitoring, not as a lock on this page's findings.
            // Compact per direct feedback that the two-line card read as an ad
            // inside the app - same message and the same working link, just a
            // slim strip instead of a headline-sized callout.
            proCard = new InfoCard();
            proCard.Compact = true;
            proCard.Title = "Free reads Windows Defender. Pro reads everything installed.";
            proCard.Body = "Pro cross-references every antivirus and browser protection on this PC into one timeline, adds a full-disk forensic sweep, and watches for what changes between visits - across every machine you look after.";
            proCard.Location = new Point(22, 330);
            proCard.Height = 44;
            proCard.Width = 700;
            overview.Controls.Add(proCard);

            btnPro = Flat("See what's in Pro", 168, 32);
            btnPro.Click += delegate
            {
                try { Process.Start(RoadmapUrl); } catch { }
            };
            proCard.SetAction(btnPro);

            // The auto-trigger control that used to live here moved to the
            // Settings > Scan Behavior page - see BuildSettingsScanBehavior() -
            // it is configuration, not something this dashboard needs to host.

            overview.Resize += delegate
            {
                lblVerdictSub.Width = Math.Max(200, overview.ClientSize.Width - 60);
                lblLastScan.Width = Math.Max(200, overview.ClientSize.Width - 60);
                proCard.Width = Math.Max(320, overview.ClientSize.Width - 44);
            };

            overview.Paint += OverviewPaint;
        }

        // Two BorderStandard rules marking the three conceptual groups on this
        // page - machine status, stat tiles, and the actions/Pro area below -
        // per the design brief's "status - tiles - actions" rhythm. Not one
        // after every label: these sit in the whitespace gaps that already
        // exist between the groups, so no other layout numbers move.
        private void OverviewPaint(object sender, PaintEventArgs e)
        {
            var p = Theme.P;
            var g = e.Graphics;
            int w = overview.ClientSize.Width;
            using (var pen = new Pen(p.BorderStandard))
            {
                g.DrawLine(pen, 22, 100, w - 22, 100);   // hero -> stat tiles
                g.DrawLine(pen, 22, 204, w - 22, 204);   // stat tiles -> actions below
            }
        }

        // Same gating logic the old btnAutoTrigger button used - event-triggered
        // scheduled tasks need Administrator, so this stays disabled with an
        // inline reason otherwise rather than failing silently on click. Setting
        // toggleAutoTrigger.Checked here never raises CheckedChanged (see
        // Toggle), so re-syncing the switch to the real registered state can
        // never itself trigger a Register()/Unregister() call.
        private void RefreshAutoTriggerToggle()
        {
            if (!Elevation.IsAdmin())
            {
                toggleAutoTrigger.Enabled = false;
                toggleAutoTrigger.Checked = false;
                rowScanBehavior.Description = "Requires Administrator - see Administrator in Security.";
                return;
            }

            toggleAutoTrigger.Enabled = true;
            bool on = AutoTrigger.IsRegistered();
            toggleAutoTrigger.Checked = on;
            rowScanBehavior.Description = on
                ? "On - Aftermath will triage automatically after Windows Defender finds or removes something."
                : "Off - turn on to triage automatically after Windows Defender finds or removes something.";
        }

        // Scheduled Drift baselines is Pro+. Same Administrator-gate pattern as
        // RefreshAutoTriggerToggle, plus an entitlement gate on top - a Free/Plus
        // user sees why the toggle is off rather than a silently disabled control.
        private void RefreshScheduledDriftToggle()
        {
            if (!Entitlements.Current.HasScheduledDrift)
            {
                toggleScheduledDrift.Enabled = false;
                toggleScheduledDrift.Checked = false;
                rowScheduledDrift.Description = "Requires Pro or higher - see Upgrade.";
                return;
            }

            if (!Elevation.IsAdmin())
            {
                toggleScheduledDrift.Enabled = false;
                toggleScheduledDrift.Checked = false;
                rowScheduledDrift.Description = "Requires Administrator - see Administrator in Security.";
                return;
            }

            toggleScheduledDrift.Enabled = true;
            bool on = DriftScheduler.IsRegistered();
            toggleScheduledDrift.Checked = on;
            rowScheduledDrift.Description = on
                ? "On - Aftermath captures a fresh Drift baseline daily at 09:00, headlessly."
                : "Off - turn on to capture a fresh Drift baseline daily, without opening the app.";
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

        // ---------- settings workspace pages ----------
        // One BuildSettingsXxx() per category in the design brief, same
        // construction style as BuildOverview/BuildSweep above: a Panel added to
        // contentHost, Visible = false until ShowPage picks it, explicit child
        // Location/Size with a Resize handler to keep widths filling the page.

        // Navigation hub, not a form - one clickable SettingsCard per category,
        // reusing the Draw.RoundRect/RoundRectOutline card language rather than a
        // stacked list of input fields.
        private void BuildSettingsHome()
        {
            settingsHome = new Panel();
            settingsHome.Dock = DockStyle.Fill;
            settingsHome.Visible = false;
            settingsHome.Padding = new Padding(22, 10, 22, 10);

            lblSettingsIntro = new Label();
            lblSettingsIntro.Text = "Only real, working settings appear here - nothing below is a placeholder for a capability SINVAUX does not have yet.";
            lblSettingsIntro.Location = new Point(22, 8);
            lblSettingsIntro.AutoSize = false;
            lblSettingsIntro.Height = 20;
            settingsHome.Controls.Add(lblSettingsIntro);

            var cardAppearance = new SettingsCard();
            cardAppearance.Title = "General";
            cardAppearance.Description = "Appearance - switch between dark and light mode.";
            cardAppearance.Location = new Point(22, 40);
            cardAppearance.Clicked += delegate { nav.Select(SetAppearance); };
            settingsHome.Controls.Add(cardAppearance);

            var cardAdmin = new SettingsCard();
            cardAdmin.Title = "Security";
            cardAdmin.Description = "Administrator - check elevation status, relaunch if needed.";
            cardAdmin.Location = new Point(22, 112);
            cardAdmin.Clicked += delegate { nav.Select(SetAdmin); };
            settingsHome.Controls.Add(cardAdmin);

            var cardScan = new SettingsCard();
            cardScan.Title = "Scanning";
            cardScan.Description = "Scan behavior - run automatically after Windows Defender scans.";
            cardScan.Location = new Point(22, 184);
            cardScan.Clicked += delegate { nav.Select(SetScanBehavior); };
            settingsHome.Controls.Add(cardScan);

            var cardData = new SettingsCard();
            cardData.Title = "Data";
            cardData.Description = "Where SINVAUX stores its files, and how long quarantined items are kept.";
            cardData.Location = new Point(22, 256);
            cardData.Clicked += delegate { nav.Select(SetData); };
            settingsHome.Controls.Add(cardData);

            var cardConn = new SettingsCard();
            cardConn.Title = "Connections";
            cardConn.Description = "External services SINVAUX connects to.";
            cardConn.Location = new Point(22, 328);
            cardConn.Clicked += delegate { nav.Select(SetConnections); };
            settingsHome.Controls.Add(cardConn);

            var cardAbout = new SettingsCard();
            cardAbout.Title = "About";
            cardAbout.Description = "Product information.";
            cardAbout.Location = new Point(22, 400);
            cardAbout.Clicked += delegate { nav.Select(SetAbout); };
            settingsHome.Controls.Add(cardAbout);

            var cards = new SettingsCard[] { cardAppearance, cardAdmin, cardScan, cardData, cardConn, cardAbout };
            settingsHome.Resize += delegate
            {
                int w = Math.Max(200, settingsHome.ClientSize.Width - 44);
                lblSettingsIntro.Width = w;
                foreach (var c in cards) c.Width = w;
            };
        }

        // GENERAL > Appearance - the dark/light toggle that used to live in the
        // header, moved here with its logic unchanged: same Theme.IsDark flag,
        // same Theme.Save(), same ApplyTheme() refresh.
        private void BuildSettingsAppearance()
        {
            pgAppearance = new Panel();
            pgAppearance.Dock = DockStyle.Fill;
            pgAppearance.Visible = false;
            pgAppearance.Padding = new Padding(22, 10, 22, 10);

            rowAppearance = new SettingRow();
            rowAppearance.Label = "Dark mode";
            rowAppearance.Description = "Use a dark colour scheme throughout SINVAUX.";
            rowAppearance.Location = new Point(22, 8);

            toggleTheme = new Toggle();
            toggleTheme.Checked = Theme.IsDark;
            toggleTheme.CheckedChanged += delegate
            {
                Theme.IsDark = toggleTheme.Checked;
                Theme.Save();
                ApplyTheme();
            };
            rowAppearance.SetControl(toggleTheme);
            pgAppearance.Controls.Add(rowAppearance);

            pgAppearance.Resize += delegate { rowAppearance.Width = Math.Max(200, pgAppearance.ClientSize.Width - 44); };
        }

        // SECURITY > Administrator - the elevation status+button that used to sit
        // in the header. Elevation.IsAdmin() cannot change mid-session (a
        // successful Relaunch() exits this process via Application.Exit()), so -
        // exactly like the header button it replaces - this is checked once at
        // construction rather than kept live.
        private void BuildSettingsAdmin()
        {
            pgAdmin = new Panel();
            pgAdmin.Dock = DockStyle.Fill;
            pgAdmin.Visible = false;
            pgAdmin.Padding = new Padding(22, 10, 22, 10);

            bool admin = Elevation.IsAdmin();

            rowAdmin = new SettingRow();
            rowAdmin.Label = "Administrator privileges";
            rowAdmin.Description = admin
                ? "Running as Administrator. All checks are available."
                : "Not running as Administrator. Some checks (like reading Defender exclusions) are unavailable.";
            rowAdmin.Location = new Point(22, 8);

            btnElevate = Flat("Run as Admin", 116, 28);
            btnElevate.Visible = !admin;
            btnElevate.Click += delegate { if (Elevation.Relaunch()) Application.Exit(); };
            rowAdmin.SetControl(btnElevate);
            pgAdmin.Controls.Add(rowAdmin);

            pgAdmin.Resize += delegate { rowAdmin.Width = Math.Max(200, pgAdmin.ClientSize.Width - 44); };
        }

        // SCANNING > Scan Behavior - the AftermathPostScan auto-trigger toggle
        // that used to live on Overview. Register()/Unregister() and the
        // Administrator gate are exactly the logic RefreshAutoTriggerToggle
        // already carries; this page only supplies the Toggle it drives.
        private void BuildSettingsScanBehavior()
        {
            pgScanBehavior = new Panel();
            pgScanBehavior.Dock = DockStyle.Fill;
            pgScanBehavior.Visible = false;
            pgScanBehavior.Padding = new Padding(22, 10, 22, 10);

            rowScanBehavior = new SettingRow();
            rowScanBehavior.Label = "Run automatically after Defender scans";
            rowScanBehavior.Location = new Point(22, 8);

            toggleAutoTrigger = new Toggle();
            toggleAutoTrigger.CheckedChanged += delegate
            {
                bool wantOn = toggleAutoTrigger.Checked;
                bool ok = wantOn ? AutoTrigger.Register() : AutoTrigger.Unregister();
                if (!ok) MessageBox.Show(this, "Could not update the scheduled task.", "Aftermath");
                RefreshAutoTriggerToggle();
            };
            rowScanBehavior.SetControl(toggleAutoTrigger);
            pgScanBehavior.Controls.Add(rowScanBehavior);

            rowScheduledDrift = new SettingRow();
            rowScheduledDrift.Label = "Scheduled Drift baselines (daily)";
            rowScheduledDrift.Location = new Point(22, 8 + rowScanBehavior.Height + 10);

            toggleScheduledDrift = new Toggle();
            toggleScheduledDrift.CheckedChanged += delegate
            {
                bool wantOn = toggleScheduledDrift.Checked;
                bool ok = wantOn ? DriftScheduler.Register() : DriftScheduler.Unregister();
                if (!ok) MessageBox.Show(this, "Could not update the scheduled task.", "Aftermath");
                RefreshScheduledDriftToggle();
            };
            rowScheduledDrift.SetControl(toggleScheduledDrift);
            pgScanBehavior.Controls.Add(rowScheduledDrift);

            pgScanBehavior.Resize += delegate
            {
                rowScanBehavior.Width = Math.Max(200, pgScanBehavior.ClientSize.Width - 44);
                rowScheduledDrift.Width = Math.Max(200, pgScanBehavior.ClientSize.Width - 44);
            };
        }

        // DATA > Storage (read-only paths the audit found - not editable, since
        // nothing in this codebase supports changing them) and Quarantine
        // retention (wired to the real QuarantineStore.PurgeOlderThan - see
        // OnSaveRetention). Retention sits in a DangerZone: purging quarantined
        // items permanently is genuinely destructive, so it gets the same
        // stripe-and-border treatment FindingList/QuarantineList use for
        // severity, rather than looking like any other setting on the page.
        private void BuildSettingsData()
        {
            pgData = new Panel();
            pgData.Dock = DockStyle.Fill;
            pgData.Visible = false;
            pgData.Padding = new Padding(22, 10, 22, 10);

            lblStorageCaption = new Label();
            lblStorageCaption.Text = "Storage locations";
            lblStorageCaption.Font = Brand.F(9.5f, FontStyle.Bold);
            lblStorageCaption.Location = new Point(22, 8);
            lblStorageCaption.AutoSize = true;
            pgData.Controls.Add(lblStorageCaption);

            // Resolved to the real path on this machine rather than shown as a raw
            // %LOCALAPPDATA% token. Mirrors RootDir/BaselinePath/LogPath in
            // Quarantine.cs/Baseline.cs/Sweep.cs exactly - nothing here is editable
            // because none of those classes support changing where they live.
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aftermath");
            lblStoragePaths = new Label();
            lblStoragePaths.Text =
                "Quarantine:        " + Path.Combine(root, "Quarantine") + "\r\n" +
                "Baseline:          " + Path.Combine(root, "baseline.json") + "\r\n" +
                "Sweep reports:     " + Path.Combine(root, "Sweep") + "\r\n" +
                "Sweep audit log:   " + Path.Combine(root, @"Sweep\audit.log");
            lblStoragePaths.Location = new Point(22, 32);
            lblStoragePaths.AutoSize = false;
            lblStoragePaths.Height = 84;
            pgData.Controls.Add(lblStoragePaths);

            zoneRetention = new DangerZone();
            zoneRetention.Location = new Point(22, 128);
            zoneRetention.Height = 74;

            rowRetention = new SettingRow();
            rowRetention.Label = "Quarantine retention";
            rowRetention.Dock = DockStyle.Fill;
            UpdateRetentionDescription(QuarantineRetentionSettings.Load(), 0, false);

            retentionControls = new Panel();
            retentionControls.Size = new Size(190, 28);

            txtRetentionDays = new TextBox();
            txtRetentionDays.Location = new Point(0, 2);
            txtRetentionDays.Width = 50;
            txtRetentionDays.Text = QuarantineRetentionSettings.Load().ToString(CultureInfo.InvariantCulture);
            retentionControls.Controls.Add(txtRetentionDays);

            lblRetentionDaysCaption = new Label();
            lblRetentionDaysCaption.Text = "days";
            lblRetentionDaysCaption.Location = new Point(54, 6);
            lblRetentionDaysCaption.AutoSize = true;
            retentionControls.Controls.Add(lblRetentionDaysCaption);

            btnSaveRetention = Flat("Save", 78, 26);
            btnSaveRetention.Location = new Point(retentionControls.Width - 78, 1);
            btnSaveRetention.Click += OnSaveRetention;
            retentionControls.Controls.Add(btnSaveRetention);

            rowRetention.SetControl(retentionControls);
            zoneRetention.Controls.Add(rowRetention);
            pgData.Controls.Add(zoneRetention);

            pgData.Resize += delegate
            {
                int w = Math.Max(200, pgData.ClientSize.Width - 44);
                lblStoragePaths.Width = w;
                zoneRetention.Width = w;
            };
        }

        // Refreshes rowRetention's description to show the currently saved
        // retention length and, right after a save, what PurgeOlderThan actually
        // did - so the control reports a real result rather than just accepting
        // a number silently.
        private void UpdateRetentionDescription(int days, int justPurged, bool showPurgeResult)
        {
            string desc = "Permanently deletes quarantined copies older than this many days - this cannot be undone. "
                + "Currently keeping items for " + days + " day(s).";
            if (showPurgeResult)
                desc += justPurged > 0 ? "  Just purged " + justPurged + " item(s)." : "  Nothing was old enough to purge just now.";
            rowRetention.Description = desc;
        }

        // The only Settings control with its own Save button rather than
        // auto-applying on change (per the design brief: everything else here
        // is safe to apply immediately, this one permanently deletes files, so
        // it gets one explicit confirmation step instead of a page-wide save bar).
        private void OnSaveRetention(object sender, EventArgs e)
        {
            int days;
            if (!int.TryParse(txtRetentionDays.Text.Trim(), out days) || days < 1)
            {
                MessageBox.Show(this, "Enter a whole number of days, 1 or more.", "Aftermath");
                return;
            }

            QuarantineRetentionSettings.Save(days);
            int purged = QuarantineStore.PurgeOlderThan(days);
            UpdateRetentionDescription(days, purged, true);
            RefreshQuarantine();
        }

        // CONNECTIONS - now the License section: paste a license key, Activate
        // verifies + persists it via LicenseStore, then Entitlements.Reload()
        // picks it up live (no restart). Failure shows the real reason, never a
        // fabricated success state.
        private void BuildSettingsConnections()
        {
            pgConnections = new Panel();
            pgConnections.Dock = DockStyle.Fill;
            pgConnections.Visible = false;
            pgConnections.Padding = new Padding(22, 10, 22, 10);

            lblConnectionsCaption = new Label();
            lblConnectionsCaption.Text = "Paste a license key to unlock a higher tier.";
            lblConnectionsCaption.Location = new Point(22, 8);
            lblConnectionsCaption.AutoSize = false;
            lblConnectionsCaption.Height = 20;
            pgConnections.Controls.Add(lblConnectionsCaption);

            connCard = new ConnectionCard();
            LicenseInfo current = LicenseStore.Load();
            connCard.ServiceName = "License";
            connCard.Connected = (current != null);
            connCard.Description = current != null
                ? "Licensed - " + current.Tier + " tier, expires " + current.ExpiresUtc.ToString("yyyy-MM-dd")
                : "No license activated yet. SINVAUX runs at the Free tier until one is added.";
            connCard.Location = new Point(22, 40);
            pgConnections.Controls.Add(connCard);

            txtLicenseKey = new TextBox();
            txtLicenseKey.Location = new Point(22, 132);
            txtLicenseKey.Height = 24;
            pgConnections.Controls.Add(txtLicenseKey);

            btnActivateLicense = Flat("Activate", 90, 26);
            btnActivateLicense.Location = new Point(22, 164);
            btnActivateLicense.Click += OnActivateLicense;
            pgConnections.Controls.Add(btnActivateLicense);

            lblLicenseStatus = new Label();
            lblLicenseStatus.Location = new Point(120, 168);
            lblLicenseStatus.AutoSize = false;
            lblLicenseStatus.Height = 20;
            pgConnections.Controls.Add(lblLicenseStatus);

            pgConnections.Resize += delegate
            {
                int w = Math.Max(200, pgConnections.ClientSize.Width - 44);
                lblConnectionsCaption.Width = w;
                connCard.Width = w;
                txtLicenseKey.Width = Math.Max(160, w - 200);
                lblLicenseStatus.Width = Math.Max(80, w - 98);
            };
        }

        // Verifies the pasted key offline via LicenseStore.TryVerify (through
        // SaveVerified), persists it only if valid, then reloads Entitlements so
        // every gated check across the app (Sidebar, Sweep host cap, exports)
        // reflects the new tier immediately - no restart required.
        private void OnActivateLicense(object sender, EventArgs e)
        {
            string key = txtLicenseKey.Text.Trim();
            if (LicenseStore.SaveVerified(key))
            {
                Entitlements.Reload();
                LicenseInfo info = LicenseStore.Load();
                lblLicenseStatus.ForeColor = Theme.P.OkColor;
                lblLicenseStatus.Text = "Activated - " + (info != null ? info.Tier.ToString() : "") + " tier.";
                connCard.Connected = true;
                connCard.Description = info != null
                    ? "Licensed - " + info.Tier + " tier, expires " + info.ExpiresUtc.ToString("yyyy-MM-dd")
                    : connCard.Description;
                planPill.Label = Entitlements.Current.Tier.ToString().ToUpperInvariant();
                planPill.Invalidate();
                RefreshScheduledDriftToggle();
                // Sidebar's Aftermath item set is rebuilt from Entitlements.Current
                // every time SwitchWorkspace(false) runs (see PopulateAftermathNav),
                // so the newly unlocked pages appear as soon as the user returns to
                // the Aftermath workspace - no separate rebuild needed while still
                // inside Settings, which would otherwise duplicate the current
                // Settings item set (Sidebar.Add is append-only).
            }
            else
            {
                lblLicenseStatus.ForeColor = Theme.P.Danger;
                lblLicenseStatus.Text = "Invalid or expired license key.";
            }
        }

        // ABOUT - product name and a short static description only. No version
        // number: nothing in this codebase tracks one (no AssemblyVersion, no
        // build-date constant), and the brief is explicit that one must not be
        // invented.
        private void BuildSettingsAbout()
        {
            pgAbout = new Panel();
            pgAbout.Dock = DockStyle.Fill;
            pgAbout.Visible = false;
            pgAbout.Padding = new Padding(22, 10, 22, 10);

            lblAboutName = new Label();
            lblAboutName.Text = Brand.FullName;
            lblAboutName.Font = Brand.F(15f, FontStyle.Bold);
            lblAboutName.Location = new Point(22, 8);
            lblAboutName.AutoSize = true;
            pgAbout.Controls.Add(lblAboutName);

            lblAboutBody = new Label();
            lblAboutBody.Text = "Post-infection triage for Windows. Reads Windows Defender's own detection "
                + "history and checks signatures, startup entries, persistence, and configuration for signs "
                + "of tampering. Aftermath has no detection engine of its own - a clean result is not proof "
                + "a machine is uninfected.";
            lblAboutBody.Location = new Point(22, 44);
            lblAboutBody.AutoSize = false;
            lblAboutBody.Height = 72;
            pgAbout.Controls.Add(lblAboutBody);

            pgAbout.Resize += delegate { lblAboutBody.Width = Math.Max(200, pgAbout.ClientSize.Width - 44); };
        }

        // ---------- navigation ----------

        private void ShowPage(string key)
        {
            overview.Visible = (key == Overview);
            cleanupList.Visible = (key == Cleanup);
            driftList.Visible = (key == Drift);
            quarantineList.Visible = (key == Quarantine);
            sweepPage.Visible = (key == Sweep);
            pgUpgrade.Visible = (key == Upgrade);
            if (key == Upgrade) pgUpgrade.RefreshTier();
            foreach (var kv in lists) kv.Value.Visible = (kv.Key == key);

            // Settings pages - same pattern, just a second set of keys. Keys never
            // collide across the two workspaces, so no extra "which workspace" gate
            // is needed here beyond the key match itself.
            settingsHome.Visible = (key == SettingsHome);
            pgAppearance.Visible = (key == SetAppearance);
            pgAdmin.Visible = (key == SetAdmin);
            pgScanBehavior.Visible = (key == SetScanBehavior);
            pgData.Visible = (key == SetData);
            pgConnections.Visible = (key == SetConnections);
            pgAbout.Visible = (key == SetAbout);

            lblPageTitle.Text = key;
            lblPageHelp.Text = Help.ContainsKey(key) ? Help[key] : "";
            pageHead.Visible = true;
        }

        // ---------- theme ----------

        private void ApplyTheme()
        {
            var p = Theme.P;

            BackColor = p.Bg;
            header.BackColor = p.Panel;
            bottom.BackColor = p.Panel;
            contentHost.BackColor = p.Bg;
            pageHead.BackColor = p.Bg;
            overview.BackColor = p.Bg;
            sweepPage.BackColor = p.Bg;
            sweepInputs.BackColor = p.Bg;
            sweepResultsHost.BackColor = p.Bg;

            foreach (var pg in new Panel[] { settingsHome, pgAppearance, pgAdmin, pgScanBehavior, pgData, pgConnections, pgAbout })
                pg.BackColor = p.Bg;

            brandMark.BackColor = p.Panel;
            brandMark.Invalidate();
            planPill.BackColor = p.Panel;
            planPill.Invalidate();
            proCard.Restyle();
            lblPageTitle.ForeColor = p.Text;
            lblPageHelp.ForeColor = p.TextDim;
            lblStatus.ForeColor = p.TextDim;
            lblVerdictSub.ForeColor = p.TextDim;
            lblLastScan.ForeColor = p.TextDim;
            lblEyebrow.ForeColor = p.TextDim;
            statusDot.BackColor = p.Bg;

            btnScan.BackColor = p.Accent; btnScan.ForeColor = Color.White;
            btnQuarantine.BackColor = p.Accent;
            btnQuarantine.ForeColor = Color.White;
            // Deliberately NOT p.Danger: it sits inches from the Quarantine button,
            // which already uses the brand accent, and from the FREE badge. Three
            // things glowing red in one glance is what read as "clashing" - a
            // destructive-but-secondary action is also better UX kept visually quiet
            // rather than competing with the recommended primary action for attention.
            lnkPermDelete.LinkColor = p.TextDim;
            lnkPermDelete.ActiveLinkColor = p.Text;
            lnkPermDelete.VisitedLinkColor = p.TextDim;

            // The old fill was a fixed off-palette blue-grey (56,60,68 / 228,232,238).
            // In light mode that sits only a few points of luminance away from the
            // warm cream page background, so flat, borderless buttons nearly vanished
            // into it. Deriving the fill from the actual background - the same
            // Draw.Mix idiom every card in this app already uses - guarantees a real,
            // in-palette contrast step no matter what the button sits on, and a
            // visible border backs that up rather than relying on fill alone.
            Color soft = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.16 : 0.10);
            Color softBorder = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.34 : 0.24);
            Color softHover = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.24 : 0.17);
            foreach (var b in new Button[] { btnWorkspace, btnElevate, btnExport, btnMenu, btnPro, btnSweep, btnSaveRetention, btnActivateLicense })
            {
                b.BackColor = soft;
                b.ForeColor = p.Text;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = softBorder;
                b.FlatAppearance.MouseOverBackColor = softHover;
            }

            // Settings widgets - Toggle/DangerZone/ConnectionCard read Theme.P
            // live in their own OnPaint, so restyling them is Restyle() (for the
            // ones with child Labels whose ForeColor cannot be read live) plus
            // Invalidate() to repaint with the new palette. SettingRow needs its
            // BackColor set explicitly by the caller, same as any other Panel -
            // three rows sit directly on a plain page (Bg), the retention row
            // sits inside the DangerZone card (SurfaceElevated), so it is styled
            // to match that card instead.
            foreach (var row in new SettingRow[] { rowAppearance, rowAdmin, rowScanBehavior, rowScheduledDrift })
            {
                row.BackColor = p.Bg;
                row.Restyle();
            }
            rowRetention.BackColor = p.SurfaceElevated;
            rowRetention.Restyle();
            retentionControls.BackColor = p.SurfaceElevated;
            toggleTheme.Checked = Theme.IsDark;
            toggleTheme.Invalidate();
            toggleAutoTrigger.Invalidate();
            toggleScheduledDrift.Invalidate();
            zoneRetention.Restyle();
            connCard.Restyle();
            pgUpgrade.Restyle();
            txtLicenseKey.BackColor = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.08 : 0.05);
            txtLicenseKey.ForeColor = p.Text;
            txtLicenseKey.BorderStyle = BorderStyle.FixedSingle;
            lblLicenseStatus.ForeColor = p.TextDim;
            foreach (var lbl in new Label[] { lblSettingsIntro, lblConnectionsCaption, lblAboutBody, lblRetentionDaysCaption })
                lbl.ForeColor = p.TextDim;
            foreach (var lbl in new Label[] { lblStorageCaption, lblAboutName })
                lbl.ForeColor = p.Text;
            lblStoragePaths.ForeColor = p.TextDim;
            txtRetentionDays.BackColor = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.08 : 0.05);
            txtRetentionDays.ForeColor = p.Text;
            txtRetentionDays.BorderStyle = BorderStyle.FixedSingle;

            foreach (var fl in lists.Values) fl.BackColor = p.Bg;
            cleanupList.BackColor = p.Bg;
            quarantineList.BackColor = p.Bg;

            foreach (var lbl in new Label[] { lblSweepHostsCaption, lblSweepUserCaption, lblSweepPassCaption,
                lblSweepHostsListCaption, lblSweepFindingsCaption })
                lbl.ForeColor = p.Text;
            lblSweepHelp.ForeColor = p.TextDim;
            lblSweepSummary.ForeColor = p.Text;

            foreach (var tb in new TextBox[] { txtSweepHosts, txtSweepUser, txtSweepPass })
            {
                tb.BackColor = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.08 : 0.05);
                tb.ForeColor = p.Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
            }

            sweepList.BackColor = p.Bg;
            sweepFindingsList.BackColor = p.Bg;

            if (last != null) UpdateOverview();
            else
            {
                lblVerdict.ForeColor = p.Text;
                statusDot.DotColor = p.TextDim;
                statusDot.Invalidate();
            }

            nav.Invalidate();
            Invalidate(true);
            foreach (Control c in contentHost.Controls) c.Invalidate();
        }

        private Color SevColor(Sev s)
        {
            var p = Theme.P;
            if (s == Sev.Bad) return p.Danger;
            if (s == Sev.Warn) return p.Warn;
            if (s == Sev.Ok) return p.OkColor;
            return p.TextDim;
        }

        private void SetStatus(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(SetStatus), s); return; }
            lblStatus.Text = s;
            // Mirror the same step message onto the Overview hero while a scan is
            // running, so progress reads there too rather than only in the bottom
            // bar - but only during a scan, not during quarantine/delete, which
            // call SetStatus with unrelated per-file messages.
            if (scanning) lblVerdictSub.Text = s;
        }

        // Bumps the determinate scan progress bar by one real, completed step.
        // Always marshalled to the UI thread, same as SetStatus - OnScan's steps
        // run on a background thread.
        private void ScanStep()
        {
            if (InvokeRequired) { BeginInvoke(new Action(ScanStep)); return; }
            if (bar.Value < bar.Maximum) bar.Value++;
        }

        // ---------- scan ----------

        private void OnScan(object sender, EventArgs e)
        {
            if (busy) return;
            busy = true;
            scanning = true;
            btnScan.Enabled = false;
            btnQuarantine.Enabled = false;
            btnExport.Enabled = false;
            bar.Style = ProgressBarStyle.Continuous;
            bar.Maximum = ScanStepCount;
            bar.Value = 0;
            bar.Visible = true;
            nav.ClearCounts();

            lblVerdict.Text = "Working...";
            lblVerdict.ForeColor = Theme.P.Text;
            statusDot.DotColor = Theme.P.TextDim;
            statusDot.Invalidate();
            lblVerdictSub.Text = "Reading antivirus records and checking this machine.";

            var t = new Thread(delegate ()
            {
                var r = new TriageResult();
                try
                {
                    Scanner.Defender(r, SetStatus); ScanStep();
                    Scanner.SystemChecks(r, SetStatus); ScanStep();
                    Deep.Sabotage(r, SetStatus); ScanStep();
                    Scanner.Startup(r, SetStatus); ScanStep();
                    Deep.Persistence(r, SetStatus); ScanStep();
                    Deep.Network(r, SetStatus); ScanStep();
                    Scanner.Artifacts(r, SetStatus); ScanStep();
                    Exposure.Scan(r, SetStatus); ScanStep();
                    History.Scan(r, SetStatus); ScanStep();

                    // Diff against the PREVIOUS baseline before Save overwrites it -
                    // Save is destructive to that comparison point.
                    SetStatus("Comparing against last scan...");
                    var previous = BaselineStore.Load();
                    r.Drift = BaselineStore.Diff(previous, r);
                    BaselineStore.Save(r);
                    ScanStep();
                }
                catch (Exception ex)
                {
                    r.Add(new Finding("System", "Scan error", ex.Message, null, Sev.Warn, false));
                }
                BeginInvoke(new Action<TriageResult>(Render), r);
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Render(TriageResult r)
        {
            // Cleared before cleanupList.SetItems below, which fires CheckedChanged -
            // that handler recomputes btnQuarantine.Enabled and needs busy to already
            // read false, or a fresh scan's results would render as permanently disabled.
            busy = false;
            scanning = false;
            last = r;
            lastScan = DateTime.Now;

            foreach (var c in Cats)
            {
                var ordered = r.ByCategory(c)
                               .OrderByDescending(x => (int)x.Severity)
                               .ThenByDescending(x => x.When)
                               .ToList();
                lists[c].SetItems(ordered);

                int bad = ordered.Count(x => x.Severity == Sev.Bad);
                int warn = ordered.Count(x => x.Severity == Sev.Warn);
                nav.SetCount(c, bad + warn, bad > 0);
            }

            var removable = r.Findings.Where(f =>
                f.Removable && !string.IsNullOrEmpty(f.Path) &&
                !Guard.IsProtected(f.Path) &&
                (File.Exists(f.Path) || Directory.Exists(f.Path))).ToList();

            cleanupList.SetItems(removable);
            nav.SetCount(Cleanup, removable.Count, false);

            // nav.ClearCounts() at the start of OnScan wipes every badge, including
            // Quarantine's - put it back since a scan does not change what is quarantined.
            RefreshQuarantine();

            // Drift entries have no natural Sev of their own - Added/Changed read as
            // Warn (worth a glance), Removed as Info (usually a good sign).
            var driftFindings = new List<Finding>();
            foreach (var d in r.Drift)
            {
                Sev sev = (d.ChangeType == "Removed") ? Sev.Info : Sev.Warn;
                string detail = string.IsNullOrEmpty(d.OldValue)
                    ? d.NewValue
                    : (string.IsNullOrEmpty(d.NewValue) ? d.OldValue : d.OldValue + "   ->   " + d.NewValue);
                var f = new Finding("Drift", d.ChangeType + ": " + d.Title, detail, d.Path, sev, false);
                driftFindings.Add(f);
            }
            driftList.SetItems(driftFindings.OrderByDescending(x => (int)x.Severity).ToList());
            nav.SetCount(Drift, r.Drift.Count, false);
            RefreshDriftEmptyText();

            UpdateOverview();

            SetStatus("Done. " + removable.Count + " item(s) can be removed.");
            bar.Visible = false;
            btnScan.Enabled = true;
            btnExport.Enabled = true;
        }

        private void UpdateOverview()
        {
            if (last == null) return;
            var p = Theme.P;

            int serious = last.Findings.Count(x => x.Severity == Sev.Bad);
            int check = last.Findings.Count(x => x.Severity == Sev.Warn);
            int ok = last.Findings.Count(x => x.Severity == Sev.Ok);
            int rem = cleanupList.CheckedItems.Count;

            tSerious.Value = serious.ToString(); tSerious.Tint = serious > 0 ? p.Danger : p.TextDim;
            tCheck.Value = check.ToString(); tCheck.Tint = check > 0 ? p.Warn : p.TextDim;
            tOk.Value = ok.ToString(); tOk.Tint = p.OkColor;
            tRemovable.Value = nav.SelectedKey == null ? "0" : CountRemovable().ToString();
            tRemovable.Tint = CountRemovable() > 0 ? p.Accent : p.TextDim;

            foreach (var t in new StatTile[] { tSerious, tCheck, tOk, tRemovable }) t.Invalidate();

            if (serious > 0)
            {
                lblVerdict.Text = serious + " thing" + (serious == 1 ? "" : "s") + " need your attention";
                lblVerdict.ForeColor = p.Danger;
                statusDot.DotColor = p.Danger;
                lblVerdictSub.Text = "Open the pages marked in red on the left. If a password stealer ran here, change your passwords from a DIFFERENT device.";
            }
            else if (check > 0)
            {
                lblVerdict.Text = "Nothing serious, " + check + " worth a look";
                lblVerdict.ForeColor = p.Warn;
                statusDot.DotColor = p.Warn;
                lblVerdictSub.Text = "No active threats found. Amber items are usually old detections or leftover files.";
            }
            else
            {
                lblVerdict.Text = "Nothing found";
                lblVerdict.ForeColor = p.OkColor;
                statusDot.DotColor = p.OkColor;
                lblVerdictSub.Text = "These checks came back clean. That is not proof this PC is uninfected - Aftermath has no detection engine of its own.";
            }
            statusDot.Invalidate();

            int watchlistHits = last.Findings.Count(x => x.Watchlist);
            if (last.Drift.Count > 0)
                lblVerdictSub.Text += "  " + last.Drift.Count + " thing" + (last.Drift.Count == 1 ? "" : "s")
                    + " changed since your last scan" + (watchlistHits > 0 ? ", including a security setting" : "") + " - see Drift.";

            lblLastScan.Text = "Last checked " + lastScan.ToString("dd MMM yyyy  HH:mm")
                + (Elevation.IsAdmin() ? "   -   running as Administrator, all checks available."
                                       : "   -   not running as Administrator, so Defender exclusions could not be read.");
        }

        private int CountRemovable()
        {
            if (last == null) return 0;
            return last.Findings.Count(f => f.Removable && !string.IsNullOrEmpty(f.Path) &&
                !Guard.IsProtected(f.Path) && (File.Exists(f.Path) || Directory.Exists(f.Path)));
        }

        // ---------- actions ----------

        // Default path: moves checked items into quarantine instead of destroying
        // them. Reversible - see the Quarantine page.
        private void OnQuarantine(object sender, EventArgs e)
        {
            if (busy) return;
            var chosen = cleanupList.CheckedItems;
            if (chosen.Count == 0)
            {
                nav.Select(Cleanup);
                MessageBox.Show(this, "Tick the items you want quarantined on the Cleanup page first.", "Aftermath");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Quarantine these " + chosen.Count + " item(s)?");
            sb.AppendLine();
            foreach (var f in chosen) sb.AppendLine("   " + f.Path);
            sb.AppendLine();
            sb.AppendLine("Quarantined items are moved aside, not destroyed - restore them any time from the Quarantine page.");

            if (MessageBox.Show(this, sb.ToString(), "Confirm quarantine",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            busy = true;
            btnQuarantine.Enabled = false;
            // OnScan leaves the bar in Continuous mode - this flow has no fixed
            // step count of its own, so it goes back to the indeterminate marquee.
            bar.Style = ProgressBarStyle.Marquee;
            bar.Visible = true;

            var t = new Thread(delegate ()
            {
                var report = new StringBuilder();
                foreach (var f in chosen)
                {
                    SetStatus("Quarantining " + f.Path);
                    var o = QuarantineStore.Add(f.Path, f.Title);
                    report.AppendLine((o.Success ? "OK     " : "FAILED ") + f.Path);
                    report.AppendLine("        " + o.Message);
                }
                BeginInvoke(new Action<string>(AfterQuarantine), report.ToString());
            });
            t.IsBackground = true;
            t.Start();
        }

        private void AfterQuarantine(string report)
        {
            busy = false;
            bar.Visible = false;
            SetStatus("Quarantine finished.");
            MessageBox.Show(this, report, "Quarantine result");
            RefreshQuarantine();
            OnScan(null, EventArgs.Empty);
        }

        // Secondary path: the old escalation ladder, kept working exactly as before
        // for the rare hostile-locked-file case that quarantine cannot move.
        private void OnDelete(object sender, EventArgs e)
        {
            if (busy) return;
            var chosen = cleanupList.CheckedItems;
            if (chosen.Count == 0)
            {
                nav.Select(Cleanup);
                MessageBox.Show(this, "Tick the items you want removed on the Cleanup page first.", "Aftermath");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Permanently delete these " + chosen.Count + " item(s)?");
            sb.AppendLine();
            foreach (var f in chosen) sb.AppendLine("   " + f.Path);
            sb.AppendLine();
            sb.AppendLine("This cannot be undone.");

            if (MessageBox.Show(this, sb.ToString(), "Confirm deletion",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            busy = true;
            btnQuarantine.Enabled = false;
            bar.Style = ProgressBarStyle.Marquee;
            bar.Visible = true;

            var t = new Thread(delegate ()
            {
                var report = new StringBuilder();
                bool reboot = false;
                foreach (var f in chosen)
                {
                    SetStatus("Removing " + f.Path);
                    var o = Remover.Delete(f.Path, 90000);
                    if (o.NeedsReboot) reboot = true;
                    report.AppendLine((o.Success ? "OK     " : "FAILED ") + f.Path);
                    report.AppendLine("        " + o.Method + " - " + o.Message);
                }
                BeginInvoke(new Action<string, bool>(AfterDelete), report.ToString(), reboot);
            });
            t.IsBackground = true;
            t.Start();
        }

        private void AfterDelete(string report, bool reboot)
        {
            busy = false;
            bar.Visible = false;
            SetStatus("Cleanup finished.");
            var msg = report;
            if (reboot) msg += "\r\nOne or more items were locked and are scheduled for deletion.\r\nRestart your PC to finish.";
            MessageBox.Show(this, msg, "Cleanup result");
            OnScan(null, EventArgs.Empty);
        }

        // ---------- quarantine page ----------

        private void RefreshQuarantine()
        {
            var q = QuarantineStore.List();
            quarantineList.SetItems(q);
            nav.SetCount(Quarantine, q.Count, false);
        }

        private void OnRestoreQuarantine(object sender, QuarantineEntry entry)
        {
            if (MessageBox.Show(this, "Restore \"" + entry.OriginalFileName + "\" to " + entry.OriginalPath + "?",
                "Confirm restore", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            var o = QuarantineStore.Restore(entry.QuarantinedPath);
            MessageBox.Show(this, o.Message, o.Success ? "Restored" : "Restore failed");
            RefreshQuarantine();
        }

        private void OnDeleteQuarantinePermanently(object sender, QuarantineEntry entry)
        {
            if (MessageBox.Show(this,
                "Permanently delete the quarantined copy of \"" + entry.OriginalFileName + "\"?\r\n\r\nThis cannot be undone.",
                "Confirm permanent deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            var o = QuarantineStore.DeletePermanently(entry.QuarantinedPath);
            if (!o.Success) MessageBox.Show(this, o.Message, "Aftermath");
            RefreshQuarantine();
        }

        // ---------- sweep page ----------

        private void OnSweepSweep(object sender, EventArgs e)
        {
            if (busy) return;

            // The only place host names come from, anywhere in this app: split
            // straight out of what the admin typed into txtSweepHosts. Nothing
            // else ever adds to this list - no discovery, no ping sweep.
            var hosts = new List<string>();
            foreach (var line in txtSweepHosts.Lines)
            {
                var h = line.Trim();
                if (h.Length > 0) hosts.Add(h);
            }

            if (hosts.Count == 0)
            {
                MessageBox.Show(this, "Enter at least one hostname or IP address, one per line.", "Aftermath");
                return;
            }
            if (string.IsNullOrEmpty(txtSweepUser.Text))
            {
                MessageBox.Show(this, "Enter a username with administrator rights on the target hosts.", "Aftermath");
                return;
            }

            string username = txtSweepUser.Text;
            string password = txtSweepPass.Text;

            busy = true;
            btnSweep.Enabled = false;
            lblSweepSummary.Text = "Sweeping " + hosts.Count + " host(s)...";
            bar.Style = ProgressBarStyle.Marquee;
            bar.Visible = true;

            sweepTargets = new List<SweepTarget>();
            foreach (var h in hosts)
            {
                var t = new SweepTarget();
                t.Host = h;
                sweepTargets.Add(t);
            }
            sweepList.SetItems(sweepTargets);
            sweepFindingsList.SetItems(new List<Finding>());

            // username/password are captured by this closure only for the life of
            // this background thread - SweepRunner passes them straight into
            // WMI/WNetAddConnection2 and never stores them anywhere. Once the
            // thread finishes, both go out of scope and are eligible for GC; see
            // AfterSweepSweep, which also clears the password box on screen.
            var t2 = new Thread(delegate ()
            {
                var results = SweepRunner.RunSweep(hosts, username, password,
                    delegate (SweepTarget target) { BeginInvoke(new Action<SweepTarget>(OnSweepProgress), target); });
                BeginInvoke(new Action<List<SweepTarget>>(AfterSweepSweep), results);
            });
            t2.IsBackground = true;
            t2.Start();
        }

        // Fires from a background sweep thread (via BeginInvoke) whenever a
        // host's Status changes. The SweepTarget instance is already in
        // sweepList's items - it was mutated in place - so this only repaints.
        private void OnSweepProgress(SweepTarget target)
        {
            sweepList.RefreshStatuses();
        }

        private void AfterSweepSweep(List<SweepTarget> results)
        {
            busy = false;
            bar.Visible = false;
            btnSweep.Enabled = true;
            txtSweepPass.Clear();   // no reason to keep it sitting on screen once the sweep is done

            sweepTargets = results;
            sweepList.SetItems(sweepTargets);

            int clean = 0, attention = 0, failed = 0;
            foreach (var t in results)
            {
                if (t.Status == SweepStatus.Failed) { failed++; continue; }
                bool needsAttention = t.Result != null &&
                    t.Result.Findings.Exists(delegate (Finding f) { return f.Severity == Sev.Bad || f.Severity == Sev.Warn; });
                if (needsAttention) attention++; else clean++;
            }

            lblSweepSummary.Text = results.Count + " machine" + (results.Count == 1 ? "" : "s") + " swept, "
                + clean + " clean, " + attention + " need attention"
                + (failed > 0 ? ", " + failed + " could not be reached" : "") + ".";

            SetStatus("Sweep sweep finished.");
        }

        private void OnSweepHostSelected(object sender, SweepTarget target)
        {
            if (target.Result != null)
            {
                sweepFindingsList.SetItems(target.Result.Findings);
            }
            else
            {
                sweepFindingsList.EmptyText = (target.Status == SweepStatus.Failed && !string.IsNullOrEmpty(target.Error))
                    ? target.Error
                    : "No results yet for this host.";
                sweepFindingsList.SetItems(new List<Finding>());
            }
        }

        private void OnExport(object sender, EventArgs e)
        {
            if (last == null) return;
            var sfd = new SaveFileDialog();
            sfd.Filter = "Text report|*.txt";
            sfd.FileName = "aftermath-report.txt";
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            var sb = new StringBuilder();
            sb.AppendLine("Aftermath triage report");
            sb.AppendLine("Generated " + DateTime.Now.ToString("F"));
            sb.AppendLine("Machine: " + Environment.MachineName + "   User: " + Environment.UserName);
            sb.AppendLine("Administrator: " + (Elevation.IsAdmin() ? "yes" : "no"));
            sb.AppendLine(new string('=', 76));
            sb.AppendLine();
            sb.AppendLine("VERDICT: " + lblVerdict.Text);
            sb.AppendLine(lblVerdictSub.Text);

            foreach (var c in Cats)
            {
                sb.AppendLine();
                sb.AppendLine("[" + c.ToUpperInvariant() + "]  " + Help[c]);
                sb.AppendLine(new string('-', 76));
                foreach (var f in last.ByCategory(c)
                                     .OrderByDescending(x => (int)x.Severity)
                                     .ThenByDescending(x => x.When))
                {
                    string tag = f.Severity == Sev.Bad ? "[SERIOUS]" :
                                 f.Severity == Sev.Warn ? "[CHECK]  " :
                                 f.Severity == Sev.Ok ? "[OK]     " : "[INFO]   ";
                    string when = f.When == DateTime.MinValue ? "" : "   " + f.When.ToString("dd MMM yyyy HH:mm");
                    sb.AppendLine(tag + " " + f.Title + when);
                    if (!string.IsNullOrEmpty(f.Detail)) sb.AppendLine("          " + f.Detail);
                    if (!string.IsNullOrEmpty(f.Path)) sb.AppendLine("          path: " + f.Path);
                }
            }

            sb.AppendLine();
            sb.AppendLine(new string('=', 76));
            sb.AppendLine("Aftermath has no detection engine. Serious findings come from Windows");
            sb.AppendLine("Defender's own records, or from signature and configuration checks.");
            sb.AppendLine("A clean report does not prove a machine is uninfected.");

            try
            {
                File.WriteAllText(sfd.FileName, sb.ToString());
                SetStatus("Report saved to " + sfd.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save: " + ex.Message, "Aftermath");
            }
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            var args = Environment.GetCommandLineArgs();

            bool auto = false;
            bool headless = false;
            string outPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--auto", StringComparison.OrdinalIgnoreCase)) auto = true;
                else if (string.Equals(args[i], "--headless", StringComparison.OrdinalIgnoreCase)) headless = true;
                else if (string.Equals(args[i], "--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    outPath = args[i + 1];
            }

            // --headless and --auto are different modes and never conflated: --auto
            // still shows the window and only auto-triggers OnScan once it has
            // loaded (see MainForm's constructor); --headless never touches
            // System.Windows.Forms at all.
            if (headless)
            {
                RunHeadless(outPath);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(auto));
        }

        // Runs the exact same steps OnScan's background thread runs, but
        // synchronously on the main thread - no Form, no Application.Run, no
        // Thread/BeginInvoke, because there is no UI to keep responsive and no
        // message loop to pump. This matters specifically because SweepRunner
        // launches this via remote WMI Win32_Process.Create, which runs in
        // Session 0 (non-interactive) - a WinForms message loop with no visible
        // window in that session can hang or behave unpredictably.
        private static void RunHeadless(string outPath)
        {
            if (string.IsNullOrEmpty(outPath))
            {
                try
                {
                    string exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                    outPath = Path.Combine(exeDir, "aftermath-report.json");
                }
                catch { outPath = "aftermath-report.json"; }
            }

            var r = new TriageResult();
            Action<string> log = delegate (string s) { };   // no UI to show progress on

            try
            {
                Scanner.Defender(r, log);
                Scanner.SystemChecks(r, log);
                Deep.Sabotage(r, log);
                Scanner.Startup(r, log);
                Deep.Persistence(r, log);
                Deep.Network(r, log);
                Scanner.Artifacts(r, log);
                Exposure.Scan(r, log);
                History.Scan(r, log);

                // Same ordering as OnScan: diff against the PREVIOUS baseline
                // before Save overwrites it.
                var previous = BaselineStore.Load();
                r.Drift = BaselineStore.Diff(previous, r);
                BaselineStore.Save(r);
            }
            catch (Exception ex)
            {
                r.Add(new Finding("System", "Scan error", ex.Message, null, Sev.Warn, false));
            }

            try { ReportIO.SaveTriageResult(r, outPath); }
            catch { }

            Environment.Exit(0);
        }
    }
}
