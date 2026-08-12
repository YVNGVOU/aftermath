// Detection Workspace: a dedicated full-page view for investigating ONE
// Finding, reached by double-clicking a row in any FindingList. Replaces the
// old "click item -> tiny panel -> another click" flow with a real
// investigation surface, built entirely from fields that actually exist on
// Finding - no invented Confidence score, no fake network-process detail,
// no resolution history. Same paint idioms as everywhere else in this app:
// Draw.RoundRect/RoundRectOutline, Theme.P read live, Brand.F, Metrics.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Aftermath
{
    public class DetectionWorkspace : Panel
    {
        // How far either side of the current finding's timestamp counts as
        // "around the same time" on the Timeline Context section.
        private static readonly TimeSpan TimelineWindow = TimeSpan.FromMinutes(10);

        private Finding current;
        private TriageResult context;

        public event EventHandler BackRequested;
        // Raised when the user clicks "Quarantine this item" - Ui.cs owns the
        // actual QuarantineStore.Add call (same code path OnQuarantine already
        // uses), this control only asks for it.
        public event EventHandler<Finding> QuarantineRequested;

        private readonly Label lblBack = new Label();
        private readonly Label lblTitle = new Label();
        private readonly SeverityChip chip = new SeverityChip();
        private readonly Label lblMeta = new Label();          // Source / When / Watchlist, one line
        private readonly Panel divider1 = new Panel();

        private readonly Label lblEvidenceHeader = new Label();
        private readonly Label lblDetail = new Label();
        private readonly Label lblPath = new Label();
        private Button btnCopyPath;
        private Button btnOpenFolder;
        private readonly Panel divider2 = new Panel();

        private readonly Label lblActionHeader = new Label();
        private Button btnQuarantine;
        private readonly Label lblInfoOnly = new Label();
        private readonly Panel divider3 = new Panel();

        private readonly Label lblFixHeader = new Label();
        private readonly Label lblFixSteps = new Label();
        private readonly Panel divider2b = new Panel();

        private readonly Label lblRelatedHeader = new Label();
        private readonly FindingList relatedList = new FindingList();
        private readonly Panel divider4 = new Panel();

        private readonly Label lblTimelineHeader = new Label();
        private readonly FindingList timelineListCtl = new FindingList();

        public DetectionWorkspace()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            DoubleBuffered = true;

            lblBack.Text = "← Back";
            lblBack.Font = Brand.F(9.5f, FontStyle.Bold);
            lblBack.AutoSize = true;
            lblBack.Cursor = Cursors.Hand;
            lblBack.Location = new Point(22, 10);
            lblBack.Click += delegate { if (BackRequested != null) BackRequested(this, EventArgs.Empty); };
            Controls.Add(lblBack);

            lblTitle.Font = Brand.F(16f, FontStyle.Bold);
            lblTitle.AutoSize = false;
            lblTitle.Location = new Point(22, 38);
            lblTitle.Height = 30;
            Controls.Add(lblTitle);

            chip.Size = new Size(90, 22);
            Controls.Add(chip);

            lblMeta.AutoSize = false;
            lblMeta.Height = 20;
            Controls.Add(lblMeta);

            divider1.Height = 1;
            Controls.Add(divider1);

            lblEvidenceHeader.Text = "EVIDENCE";
            lblEvidenceHeader.Font = Brand.F(8f, FontStyle.Bold);
            lblEvidenceHeader.AutoSize = true;
            Controls.Add(lblEvidenceHeader);

            lblDetail.AutoSize = false;
            lblDetail.Height = 70;
            Controls.Add(lblDetail);

            lblPath.AutoSize = false;
            lblPath.Height = 20;
            Controls.Add(lblPath);

            btnCopyPath = Flat("Copy path", 110, 28);
            btnCopyPath.Click += delegate { try { if (current != null && !string.IsNullOrEmpty(current.Path)) Clipboard.SetText(current.Path); } catch { } };
            Controls.Add(btnCopyPath);

            btnOpenFolder = Flat("Open containing folder", 172, 28);
            // Same helper FileMenu's context-menu "Open file location" item and
            // Cleanup's lnkPermDelete flow both rely on - not a second implementation.
            btnOpenFolder.Click += delegate { if (current != null) FileMenu.ShowInFolder(current.Path); };
            Controls.Add(btnOpenFolder);

            divider2.Height = 1;
            Controls.Add(divider2);

            lblActionHeader.Text = "RECOMMENDED ACTION";
            lblActionHeader.Font = Brand.F(8f, FontStyle.Bold);
            lblActionHeader.AutoSize = true;
            Controls.Add(lblActionHeader);

            btnQuarantine = Flat("Quarantine this item", 180, 34);
            btnQuarantine.Click += delegate { if (current != null && QuarantineRequested != null) QuarantineRequested(this, current); };
            Controls.Add(btnQuarantine);

            lblInfoOnly.Text = "Not independently removable - informational finding.";
            lblInfoOnly.AutoSize = false;
            lblInfoOnly.Height = 20;
            Controls.Add(lblInfoOnly);

            divider3.Height = 1;
            Controls.Add(divider3);

            lblFixHeader.Text = "HOW TO FIX THIS";
            lblFixHeader.Font = Brand.F(8f, FontStyle.Bold);
            lblFixHeader.AutoSize = true;
            Controls.Add(lblFixHeader);

            // Height is recomputed per-finding in Show()/LayoutContent since the
            // number of steps varies - same reason lblDetail already gets a fixed
            // Height guess rather than AutoSize, but this one is re-measured live
            // because step counts vary far more than a single Detail paragraph does.
            lblFixSteps.AutoSize = false;
            Controls.Add(lblFixSteps);

            divider2b.Height = 1;
            Controls.Add(divider2b);

            lblRelatedHeader.Font = Brand.F(8f, FontStyle.Bold);
            lblRelatedHeader.AutoSize = true;
            Controls.Add(lblRelatedHeader);

            relatedList.Height = 190;
            relatedList.EmptyText = "No other findings from this source.";
            relatedList.ItemOpened += delegate (object s, Finding f) { Show(f, context); };
            Controls.Add(relatedList);

            divider4.Height = 1;
            Controls.Add(divider4);

            lblTimelineHeader.Text = "AROUND THE SAME TIME";
            lblTimelineHeader.Font = Brand.F(8f, FontStyle.Bold);
            lblTimelineHeader.AutoSize = true;
            Controls.Add(lblTimelineHeader);

            timelineListCtl.Height = 190;
            timelineListCtl.EmptyText = "No other findings with a known timestamp near this one.";
            timelineListCtl.ItemOpened += delegate (object s, Finding f) { Show(f, context); };
            Controls.Add(timelineListCtl);

            Resize += delegate { LayoutContent(); };
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

        // Populates the whole page for one Finding. context is the full current
        // TriageResult so Related/Timeline can cross-reference every other
        // finding, not just the ones already loaded into whichever list the
        // user opened this from.
        public void Show(Finding f, TriageResult ctx)
        {
            current = f;
            context = ctx;

            lblTitle.Text = f.Title;
            chip.Sev = f.Severity;
            chip.Invalidate();

            string meta = "Source: " + f.Category;
            if (f.When != DateTime.MinValue) meta += "      When: " + FindingList.When(f.When);
            if (f.Watchlist) meta += "      ● Watchlist";
            lblMeta.Text = meta;

            lblDetail.Text = f.Detail ?? "";

            bool hasPath = !string.IsNullOrEmpty(f.Path);
            lblPath.Visible = hasPath;
            btnCopyPath.Visible = hasPath;
            btnOpenFolder.Visible = hasPath;
            lblPath.Text = hasPath ? f.Path : "";

            btnQuarantine.Visible = f.Removable;
            lblInfoOnly.Visible = !f.Removable;

            var fixSteps = RemediationGuide.StepsFor(f);
            var fixText = new System.Text.StringBuilder();
            for (int i = 0; i < fixSteps.Count; i++)
            {
                if (i > 0) fixText.Append("\n\n");
                fixText.Append((i + 1) + ". " + fixSteps[i]);
            }
            lblFixSteps.Text = fixText.ToString();

            var all = (ctx != null) ? ctx.Findings : new List<Finding>();

            var related = all.Where(x => x != f && x.Category == f.Category).ToList();
            lblRelatedHeader.Text = "RELATED FINDINGS (" + f.Category + ")";
            relatedList.SetItems(related);

            List<Finding> nearby;
            if (f.When == DateTime.MinValue)
            {
                nearby = new List<Finding>();
            }
            else
            {
                nearby = all.Where(x => x != f && x.When != DateTime.MinValue &&
                                         Math.Abs((x.When - f.When).TotalMinutes) <= TimelineWindow.TotalMinutes)
                             .OrderBy(x => x.When)
                             .ToList();
            }
            timelineListCtl.SetItems(nearby);

            LayoutContent();
            Restyle();
            AutoScrollPosition = new Point(0, 0);
        }

        // Manual vertical stacking, same approach the Settings pages use (fixed
        // Locations recomputed on Resize) rather than a layout panel, since the
        // sections mix labels, buttons, and two full FindingLists.
        private void LayoutContent()
        {
            int w = Math.Max(360, ClientSize.Width - (AutoScroll && VerticalScroll.Visible ? 40 : 22) - 22);
            int y = 38 + 30 + 8;

            chip.Location = new Point(22, y);
            lblMeta.Location = new Point(22 + chip.Width + 12, y + 2);
            lblMeta.Width = w - chip.Width - 12;
            y += chip.Height + 14;

            divider1.Location = new Point(22, y);
            divider1.Width = w;
            y += 18;

            lblEvidenceHeader.Location = new Point(22, y);
            y += 22;

            lblDetail.Location = new Point(22, y);
            lblDetail.Width = w;
            y += lblDetail.Height + 6;

            if (lblPath.Visible)
            {
                lblPath.Location = new Point(22, y);
                lblPath.Width = Math.Max(120, w - 300);
                btnCopyPath.Location = new Point(22 + lblPath.Width + 10, y - 4);
                btnOpenFolder.Location = new Point(btnCopyPath.Right + 10, y - 4);
                y += 34;
            }
            y += 8;

            divider2.Location = new Point(22, y);
            divider2.Width = w;
            y += 18;

            lblActionHeader.Location = new Point(22, y);
            y += 22;
            btnQuarantine.Location = new Point(22, y);
            lblInfoOnly.Location = new Point(22, y);
            lblInfoOnly.Width = w;
            y += 40;

            divider3.Location = new Point(22, y);
            divider3.Width = w;
            y += 18;

            lblFixHeader.Location = new Point(22, y);
            y += 22;

            lblFixSteps.Location = new Point(22, y);
            lblFixSteps.Width = w;
            // Measured, not AutoSize, because the step count/length varies per
            // finding and we need an exact height to keep everything below it
            // from overlapping - same reasoning as the fixed-then-measured
            // approach the rest of this manual layout already uses.
            Size fixSize = TextRenderer.MeasureText(lblFixSteps.Text, lblFixSteps.Font, new Size(w, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.Left);
            lblFixSteps.Height = fixSize.Height + 4;
            y += lblFixSteps.Height + 12;

            divider2b.Location = new Point(22, y);
            divider2b.Width = w;
            y += 18;

            lblRelatedHeader.Location = new Point(22, y);
            y += 22;
            relatedList.Location = new Point(22, y);
            relatedList.Width = w;
            y += relatedList.Height + 16;

            divider4.Location = new Point(22, y);
            divider4.Width = w;
            y += 18;

            lblTimelineHeader.Location = new Point(22, y);
            y += 22;
            timelineListCtl.Location = new Point(22, y);
            timelineListCtl.Width = w;
            y += timelineListCtl.Height + 20;

            AutoScrollMinSize = new Size(0, y);
        }

        public void Restyle()
        {
            var p = Theme.P;
            BackColor = p.Bg;
            lblBack.ForeColor = p.TextDim;
            lblTitle.ForeColor = p.Text;
            lblMeta.ForeColor = p.TextDim;
            lblEvidenceHeader.ForeColor = p.TextDim;
            lblDetail.ForeColor = p.Text;
            lblPath.ForeColor = p.TextDim;
            lblActionHeader.ForeColor = p.TextDim;
            lblInfoOnly.ForeColor = p.TextDim;
            lblFixHeader.ForeColor = p.TextDim;
            lblFixSteps.ForeColor = p.Text;
            lblRelatedHeader.ForeColor = p.TextDim;
            lblTimelineHeader.ForeColor = p.TextDim;

            foreach (var d in new Panel[] { divider1, divider2, divider2b, divider3, divider4 })
                d.BackColor = p.BorderStandard;

            Color soft = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.16 : 0.10);
            Color softBorder = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.34 : 0.24);
            Color softHover = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.24 : 0.17);
            foreach (var b in new Button[] { btnCopyPath, btnOpenFolder })
            {
                b.BackColor = soft;
                b.ForeColor = p.Text;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = softBorder;
                b.FlatAppearance.MouseOverBackColor = softHover;
            }

            btnQuarantine.BackColor = p.Accent;
            btnQuarantine.ForeColor = Color.White;

            relatedList.BackColor = p.Bg;
            timelineListCtl.BackColor = p.Bg;
            chip.Invalidate();
            Invalidate(true);
        }

        // Small severity pill, same tinted-chip visual language and colour
        // mapping FindingList's own severity chip already uses - reused here
        // rather than re-deriving a second colour rule.
        private class SeverityChip : Control
        {
            public Sev Sev;

            public SeverityChip()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            private Color SevColor()
            {
                var p = Theme.P;
                if (Sev == Sev.Bad) return p.Danger;
                if (Sev == Sev.Warn) return p.Warn;
                if (Sev == Sev.Ok) return p.OkColor;
                return p.TextDim;
            }

            private static string SevText(Sev s)
            {
                if (s == Sev.Bad) return "SERIOUS";
                if (s == Sev.Warn) return "CHECK";
                if (s == Sev.Ok) return "OK";
                return "INFO";
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var p = Theme.P;
                var g = e.Graphics;
                g.Clear(p.Bg);
                Color fg = SevColor();
                var chip = new Rectangle(0, 0, Width - 1, Height - 1);
                Draw.RoundRect(g, chip, 6, Color.FromArgb(Theme.IsDark ? 52 : 34, fg));
                TextRenderer.DrawText(g, SevText(Sev), new Font("Segoe UI", 7.5f, FontStyle.Bold), chip, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
