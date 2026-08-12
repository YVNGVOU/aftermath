// Custom controls for Aftermath.
//
// The findings list is painted from scratch rather than using ListView. ListView
// repaints rows under the cursor with its own theming, which kept erasing the
// severity colours on hover. Owning the paint loop makes that impossible: hover
// changes the row BACKGROUND only, and text colour is always chosen explicitly.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Aftermath
{
    public static class Draw
    {
        public static void RoundRect(Graphics g, Rectangle r, int radius, Color fill)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var path = new GraphicsPath())
            {
                int d = Math.Max(2, radius * 2);
                if (d > r.Width) d = r.Width;
                if (d > r.Height) d = r.Height;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                g.SmoothingMode = old;
            }
        }

        // Stroke-only twin of RoundRect - traces the same path a fill call at the
        // same rectangle/radius would use, so an outline laid over an existing
        // RoundRect fill lines up with it exactly. 1px lines only, no glow/blur.
        public static void RoundRectOutline(Graphics g, Rectangle r, int radius, Color color)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var path = new GraphicsPath())
            {
                int d = Math.Max(2, radius * 2);
                if (d > r.Width) d = r.Width;
                if (d > r.Height) d = r.Height;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(color)) g.DrawPath(pen, path);
                g.SmoothingMode = old;
            }
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    // Collapsible left navigation.
    public class Sidebar : Panel
    {
        public class Item
        {
            public string Key;
            public string Label;
            public string Glyph;
            public int Count;
            public bool Alarm;
            public bool IsHeader;   // quiet section caption - not selectable, no glyph or badge
        }

        public const int Wide = 208;
        public const int Narrow = 56;

        private readonly List<Item> items = new List<Item>();
        private int selected = 0;
        private int hover = -1;
        private const int RowH = 44;
        private const int HeaderH = 26;           // section captions are shorter than a nav row
        private const int HeaderHCollapsed = 10;  // collapsed: just a thin divider, no label fits
        private const int TopPad = 12;

        public bool Collapsed { get; private set; }
        public event EventHandler<string> Navigated;

        public Sidebar()
        {
            Dock = DockStyle.Left;
            Width = Wide;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Add(string key, string label, string glyph)
        {
            var it = new Item();
            it.Key = key; it.Label = label; it.Glyph = glyph; it.Count = 0;
            items.Add(it);
            Invalidate();
        }

        // Quiet, non-clickable group caption between sections - no Key, so it can
        // never be Select()ed or SetCount()ed, and IndexAt below skips it entirely.
        public void AddSection(string label)
        {
            var it = new Item();
            it.Label = label; it.IsHeader = true;
            items.Add(it);
            Invalidate();
        }

        public string SelectedKey
        {
            get { return (selected >= 0 && selected < items.Count) ? items[selected].Key : null; }
        }

        public void Select(string key)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Key == key)
                {
                    selected = i;
                    Invalidate();
                    if (Navigated != null) Navigated(this, key);
                    return;
                }
            }
        }

        public void SetCount(string key, int n, bool alarm)
        {
            foreach (var it in items)
            {
                if (it.Key != key) continue;
                it.Count = n;
                it.Alarm = alarm;
                Invalidate();
                return;
            }
        }

        public void ClearCounts()
        {
            foreach (var it in items) { it.Count = 0; it.Alarm = false; }
            Invalidate();
        }

        // Empties the item list so the SAME Sidebar instance can be repopulated
        // with a different item set (Aftermath vs Settings) on workspace switch -
        // added because Add/AddSection were append-only with no way to rebuild.
        // Does not touch Collapsed - collapse state is a shell preference, not
        // part of either workspace's item set.
        public void Clear()
        {
            items.Clear();
            selected = 0;
            hover = -1;
            Invalidate();
        }

        public void Toggle()
        {
            Collapsed = !Collapsed;
            Width = Collapsed ? Narrow : Wide;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = IndexAt(e.Y);
            if (idx != hover) { hover = idx; Invalidate(); }
            Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int idx = IndexAt(e.Y);
            if (idx >= 0)
            {
                selected = idx;
                Invalidate();
                if (Navigated != null) Navigated(this, items[idx].Key);
            }
            base.OnMouseDown(e);
        }

        // Header rows are shorter than nav rows (and shorter still when collapsed),
        // so row position can no longer be computed as a fixed i*RowH - it has to
        // walk the heights of the rows above it.
        private int RowHeightAt(int i)
        {
            if (!items[i].IsHeader) return RowH;
            return Collapsed ? HeaderHCollapsed : HeaderH;
        }

        private int IndexAt(int y)
        {
            if (y < TopPad) return -1;
            int top = TopPad;
            for (int i = 0; i < items.Count; i++)
            {
                int h = RowHeightAt(i);
                if (y < top + h) return items[i].IsHeader ? -1 : i;
                top += h;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.P;
            var g = e.Graphics;
            g.Clear(p.Panel);

            int top = TopPad;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                int h = RowHeightAt(i);

                if (it.IsHeader)
                {
                    if (!Collapsed)
                    {
                        var headerRect = new Rectangle(18, top, Width - 24, h);
                        TextRenderer.DrawText(g, it.Label.ToUpperInvariant(), new Font("Segoe UI", 7.5f, FontStyle.Bold),
                            headerRect, p.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    }
                    else
                    {
                        // No room for a label once collapsed - a thin divider still
                        // marks the group boundary without an empty-looking gap.
                        int lineY = top + h / 2;
                        using (var pen = new Pen(Draw.Mix(p.Panel, p.Text, 0.14)))
                            g.DrawLine(pen, 14, lineY, Width - 14, lineY);
                    }
                    top += h;
                    continue;
                }

                var row = new Rectangle(6, top, Width - 12, h - 4);

                // Toned down from an earlier, heavier mix - the accent stripe two
                // lines below already marks the selected item unambiguously, so the
                // fill only needs to be a whisper, not another solid block of the
                // same red competing with the buttons above it.
                if (i == selected)
                {
                    // Extended to Width instead of the usual 6px right margin, so
                    // the fill's straight right edge lands exactly on the sidebar's
                    // BorderStrong divider (drawn at Width-1 below) - the selected
                    // row reads as physically meeting the content area, not just
                    // sitting near it.
                    var selRow = new Rectangle(6, top, Width - 6, h - 4);
                    Draw.RoundRect(g, selRow, 8, Draw.Mix(p.Panel, p.Accent, Theme.IsDark ? 0.20 : 0.11));
                }
                else if (i == hover)
                    Draw.RoundRect(g, row, 8, Draw.Mix(p.Panel, p.Text, 0.07));

                // Accent bar on the selected item.
                if (i == selected)
                    Draw.RoundRect(g, new Rectangle(row.X, row.Y + 8, 3, row.Height - 16), 2, p.Accent);

                Color fg = (i == selected) ? p.Text : p.TextDim;

                var glyphRect = new Rectangle(row.X + 12, row.Y, 26, row.Height);
                TextRenderer.DrawText(g, it.Glyph, new Font("Segoe UI Symbol", 10f), glyphRect, fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

                if (!Collapsed)
                {
                    var textRect = new Rectangle(row.X + 44, row.Y, row.Width - 44 - 44, row.Height);
                    TextRenderer.DrawText(g, it.Label,
                        new Font("Segoe UI", 9.5f, i == selected ? FontStyle.Bold : FontStyle.Regular),
                        textRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                }

                if (it.Count > 0)
                {
                    Color badge = it.Alarm ? p.Danger : p.Warn;
                    string txt = it.Count.ToString();
                    int bw = Math.Max(20, TextRenderer.MeasureText(txt, new Font("Segoe UI", 7.5f, FontStyle.Bold)).Width + 10);
                    var br = Collapsed
                        ? new Rectangle(row.Right - 20, row.Y + 6, 14, 14)
                        : new Rectangle(row.Right - bw - 10, row.Y + (row.Height - 18) / 2, bw, 18);
                    Draw.RoundRect(g, br, 9, Color.FromArgb(Theme.IsDark ? 62 : 40, badge));
                    if (!Collapsed)
                        TextRenderer.DrawText(g, txt, new Font("Segoe UI", 7.5f, FontStyle.Bold), br, badge,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                }

                top += h;
            }

            // Structural divider between sidebar and content - visible in both
            // Collapsed states and on every page, since it is drawn last, outside
            // the per-item loop above.
            using (var pen = new Pen(p.BorderStrong))
                g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        }
    }

    // Scrollable, fully custom-painted findings list.
    public class FindingList : Control
    {
        private List<Finding> items = new List<Finding>();
        private readonly VScrollBar sb = new VScrollBar();
        private int hover = -1;
        private const int RowH = 68;

        public bool Checkable;
        private readonly HashSet<int> ticked = new HashSet<int>();
        public event EventHandler CheckedChanged;

        // Raised on double-click of a row - the "open this one finding" trigger the
        // Detection Workspace hooks into. Additive: does not touch Checkable/context-
        // menu behaviour, and fires regardless of Checkable so every FindingList in
        // the app (Detections/Warnings/System/Activity, even Cleanup) can opt in just
        // by subscribing.
        public event EventHandler<Finding> ItemOpened;

        public string EmptyText = "Nothing to show yet. Run a triage.";

        public FindingList()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            sb.Dock = DockStyle.Right;
            sb.Width = 14;
            sb.SmallChange = RowH;
            sb.LargeChange = RowH * 3;
            sb.ValueChanged += delegate { Invalidate(); };
            Controls.Add(sb);
        }

        public void SetItems(IEnumerable<Finding> f)
        {
            items = new List<Finding>(f);
            ticked.Clear();
            sb.Value = 0;
            Resync();
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }

        public List<Finding> CheckedItems
        {
            get
            {
                var outp = new List<Finding>();
                foreach (var i in ticked) if (i >= 0 && i < items.Count) outp.Add(items[i]);
                return outp;
            }
        }

        private void Resync()
        {
            int total = items.Count * RowH;
            int visible = Math.Max(1, ClientSize.Height);
            if (total <= visible) { sb.Visible = false; sb.Maximum = 0; sb.Value = 0; }
            else
            {
                sb.Visible = true;
                sb.Minimum = 0;
                sb.Maximum = total - visible + sb.LargeChange;
                if (sb.Value > total - visible) sb.Value = Math.Max(0, total - visible);
            }
        }

        protected override void OnResize(EventArgs e) { Resync(); base.OnResize(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (sb.Visible)
            {
                int v = sb.Value - Math.Sign(e.Delta) * RowH;
                sb.Value = Math.Max(sb.Minimum, Math.Min(sb.Maximum - sb.LargeChange + 1, v));
            }
            base.OnMouseWheel(e);
        }

        private int IndexAt(int y)
        {
            int i = (y + sb.Value) / RowH;
            return (i >= 0 && i < items.Count) ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i != hover) { hover = i; Invalidate(); }
            Cursor = (Checkable && i >= 0) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // Right-click used to toggle the checkbox too (any button reached this
            // code), so a right-click on Cleanup would silently tick the row before
            // the context menu even opened. Left button only, now.
            if (!Checkable || e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
            int i = IndexAt(e.Y);
            if (i >= 0)
            {
                if (ticked.Contains(i)) ticked.Remove(i); else ticked.Add(i);
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int i = IndexAt(e.Y);
                if (i >= 0 && !string.IsNullOrEmpty(items[i].Path))
                    FileMenu.Build(items[i].Path).Show(this, e.Location);
            }
            base.OnMouseUp(e);
        }

        // Double-click opens the row into the Detection Workspace. Reuses the same
        // IndexAt row-hit-testing OnMouseMove/OnMouseDown/OnMouseUp already rely on,
        // so it can never disagree with what is drawn under the cursor. Left button
        // only, same convention as the checkbox toggle above.
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int i = IndexAt(e.Y);
                if (i >= 0 && ItemOpened != null) ItemOpened(this, items[i]);
            }
            base.OnMouseDoubleClick(e);
        }

        private Color SevColor(Sev s)
        {
            var p = Theme.P;
            if (s == Sev.Bad) return p.Danger;
            if (s == Sev.Warn) return p.Warn;
            if (s == Sev.Ok) return p.OkColor;
            return p.TextDim;
        }

        private static string SevText(Sev s)
        {
            if (s == Sev.Bad) return "SERIOUS";
            if (s == Sev.Warn) return "CHECK";
            if (s == Sev.Ok) return "OK";
            return "INFO";
        }

        public static string When(DateTime t)
        {
            if (t == DateTime.MinValue) return "";
            var span = DateTime.Now - t;
            string rel;
            if (span.TotalMinutes < 60) rel = (int)Math.Max(1, span.TotalMinutes) + "m ago";
            else if (span.TotalHours < 24) rel = (int)span.TotalHours + "h ago";
            else if (span.TotalDays < 30) rel = (int)span.TotalDays + "d ago";
            else if (span.TotalDays < 365) rel = (int)(span.TotalDays / 30) + "mo ago";
            else rel = (int)(span.TotalDays / 365) + "y ago";
            return t.ToString("dd MMM yyyy  HH:mm") + "     " + rel;
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

            int w = ClientSize.Width - (sb.Visible ? sb.Width : 0);
            int first = Math.Max(0, sb.Value / RowH);
            int last = Math.Min(items.Count - 1, (sb.Value + ClientSize.Height) / RowH);

            for (int i = first; i <= last; i++)
            {
                var f = items[i];
                int y = i * RowH - sb.Value;
                var card = new Rectangle(10, y + 4, w - 20, RowH - 8);

                Color fg = SevColor(f.Severity);

                // Hover and checked change the BACKGROUND only. Text colour below is
                // always set explicitly, so nothing can wash it out.
                Color bg = p.SurfaceElevated;
                if (i == hover) bg = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.11 : 0.07);
                if (Checkable && ticked.Contains(i)) bg = Draw.Mix(p.Bg, p.Accent, Theme.IsDark ? 0.26 : 0.14);

                Draw.RoundRect(g, card, 8, bg);
                // Subtle full-perimeter outline so the card reads as a bounded
                // surface rather than a tinted blob - severity stripe unchanged below.
                Draw.RoundRectOutline(g, card, 8, p.BorderStandard);

                // Severity stripe down the left edge of the card.
                Draw.RoundRect(g, new Rectangle(card.X, card.Y + 6, 3, card.Height - 12), 2, fg);

                int x = card.X + 16;

                if (Checkable)
                {
                    var box = new Rectangle(x, card.Y + (card.Height - 16) / 2, 16, 16);
                    Draw.RoundRect(g, box, 4, ticked.Contains(i) ? p.Accent : Draw.Mix(p.Bg, p.Text, 0.18));
                    if (ticked.Contains(i))
                        TextRenderer.DrawText(g, "✓", new Font("Segoe UI", 8f, FontStyle.Bold), box,
                            Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    x += 26;
                }

                // Severity chip.
                var chip = new Rectangle(x, card.Y + (card.Height - 20) / 2, 74, 20);
                Draw.RoundRect(g, chip, 6, Color.FromArgb(Theme.IsDark ? 52 : 34, fg));
                TextRenderer.DrawText(g, SevText(f.Severity), new Font("Segoe UI", 7f, FontStyle.Bold), chip, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += 86;

                // Watchlist marker: a single quiet dot, not a second colour - this app
                // just had its "too much red" problem fixed, so this stays restrained.
                if (f.Watchlist)
                {
                    var dot = new Rectangle(x, card.Y + card.Height / 2 - 3, 6, 6);
                    using (var b = new SolidBrush(fg)) g.FillEllipse(b, dot);
                    x += 12;
                }

                string whenTxt = When(f.When);
                int whenW = string.IsNullOrEmpty(whenTxt) ? 0 : 210;

                var titleRect = new Rectangle(x, card.Y + 9, Math.Max(60, card.Right - x - whenW - 16), 20);
                TextRenderer.DrawText(g, f.Title, new Font("Segoe UI", 9.5f, FontStyle.Bold), titleRect, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                if (whenW > 0)
                {
                    var wr = new Rectangle(card.Right - whenW - 12, card.Y + 9, whenW, 20);
                    TextRenderer.DrawText(g, whenTxt, new Font("Segoe UI", 8f), wr, p.TextDim,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }

                string second = f.Detail == null ? "" : f.Detail.Replace("\r\n", "  ");
                if (!string.IsNullOrEmpty(f.Path)) second = second + "      " + f.Path;
                var detRect = new Rectangle(x, card.Y + 31, Math.Max(60, card.Right - x - 16), 30);
                TextRenderer.DrawText(g, second, new Font("Segoe UI", 8.5f), detRect, p.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // Quarantine page list: same painted-card idiom as FindingList (RoundRect body,
    // thin left-edge stripe, Mix-derived hover state), but each row carries a
    // Restore / Delete permanently action instead of a checkbox, since quarantine
    // items are actioned one at a time rather than bulk-ticked. The stripe is
    // always the theme accent colour - quarantined items are neither pass nor
    // fail, they are "held".
    public class QuarantineList : Control
    {
        private List<QuarantineEntry> items = new List<QuarantineEntry>();
        private readonly VScrollBar sb = new VScrollBar();
        private int hover = -1;
        private int hoverBtn = -1;   // 0 = restore, 1 = delete, -1 = none
        private const int RowH = 92;
        private const int BtnW = 118;
        private const int BtnH = 26;

        public string EmptyText = "Nothing in quarantine.";
        public event EventHandler<QuarantineEntry> RestoreClicked;
        public event EventHandler<QuarantineEntry> DeleteClicked;

        public QuarantineList()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            sb.Dock = DockStyle.Right;
            sb.Width = 14;
            sb.SmallChange = RowH;
            sb.LargeChange = RowH * 3;
            sb.ValueChanged += delegate { Invalidate(); };
            Controls.Add(sb);
        }

        public void SetItems(IEnumerable<QuarantineEntry> e)
        {
            items = new List<QuarantineEntry>(e);
            sb.Value = 0;
            Resync();
            Invalidate();
        }

        private void Resync()
        {
            int total = items.Count * RowH;
            int visible = Math.Max(1, ClientSize.Height);
            if (total <= visible) { sb.Visible = false; sb.Maximum = 0; sb.Value = 0; }
            else
            {
                sb.Visible = true;
                sb.Minimum = 0;
                sb.Maximum = total - visible + sb.LargeChange;
                if (sb.Value > total - visible) sb.Value = Math.Max(0, total - visible);
            }
        }

        protected override void OnResize(EventArgs e) { Resync(); base.OnResize(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (sb.Visible)
            {
                int v = sb.Value - Math.Sign(e.Delta) * RowH;
                sb.Value = Math.Max(sb.Minimum, Math.Min(sb.Maximum - sb.LargeChange + 1, v));
            }
            base.OnMouseWheel(e);
        }

        private int IndexAt(int y)
        {
            int i = (y + sb.Value) / RowH;
            return (i >= 0 && i < items.Count) ? i : -1;
        }

        private int ListWidth
        {
            get { return ClientSize.Width - (sb.Visible ? sb.Width : 0); }
        }

        private Rectangle CardFor(int i)
        {
            int y = i * RowH - sb.Value;
            return new Rectangle(10, y + 4, ListWidth - 20, RowH - 8);
        }

        private Rectangle DeleteRectFor(int i)
        {
            var card = CardFor(i);
            int y = card.Y + (card.Height - BtnH) / 2;
            return new Rectangle(card.Right - (BtnW * 2) - 24, y, BtnW, BtnH);
        }

        private Rectangle RestoreRectFor(int i)
        {
            var card = CardFor(i);
            int y = card.Y + (card.Height - BtnH) / 2;
            return new Rectangle(card.Right - BtnW - 14, y, BtnW, BtnH);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            int btn = -1;
            if (i >= 0)
            {
                if (RestoreRectFor(i).Contains(e.Location)) btn = 0;
                else if (DeleteRectFor(i).Contains(e.Location)) btn = 1;
            }
            if (i != hover || btn != hoverBtn) { hover = i; hoverBtn = btn; Invalidate(); }
            Cursor = (btn >= 0) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; hoverBtn = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // Same class of bug as FindingList: this fired on any button, so a
            // right-click landing on the Restore rectangle triggered a restore.
            if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
            int i = IndexAt(e.Y);
            if (i >= 0 && i < items.Count)
            {
                if (RestoreRectFor(i).Contains(e.Location))
                {
                    if (RestoreClicked != null) RestoreClicked(this, items[i]);
                }
                else if (DeleteRectFor(i).Contains(e.Location))
                {
                    if (DeleteClicked != null) DeleteClicked(this, items[i]);
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int i = IndexAt(e.Y);
                // The quarantined copy's current location - the original path is
                // gone by definition, that is what "quarantined" means.
                if (i >= 0 && !string.IsNullOrEmpty(items[i].QuarantinedPath))
                    FileMenu.Build(items[i].QuarantinedPath).Show(this, e.Location);
            }
            base.OnMouseUp(e);
        }

        private static DateTime ParseWhen(string iso)
        {
            DateTime t;
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t))
                return t.ToLocalTime();
            return DateTime.MinValue;
        }

        private static string SizeText(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0") + " GB";
            if (bytes >= 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.0") + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return bytes + " B";
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

            int first = Math.Max(0, sb.Value / RowH);
            int last = Math.Min(items.Count - 1, (sb.Value + ClientSize.Height) / RowH);
            int btnZone = (BtnW * 2) + 34;

            for (int i = first; i <= last; i++)
            {
                var q = items[i];
                var card = CardFor(i);

                Color fg = p.Accent;   // held, not pass/fail - always the accent stripe

                Color bg = p.SurfaceElevated;
                if (i == hover) bg = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.11 : 0.07);

                Draw.RoundRect(g, card, 8, bg);
                Draw.RoundRectOutline(g, card, 8, p.BorderStandard);
                Draw.RoundRect(g, new Rectangle(card.X, card.Y + 6, 3, card.Height - 12), 2, fg);

                int x = card.X + 16;
                int textW = Math.Max(60, card.Width - 32 - btnZone);

                var titleRect = new Rectangle(x, card.Y + 9, textW, 20);
                TextRenderer.DrawText(g, q.OriginalFileName, new Font("Segoe UI", 9.5f, FontStyle.Bold), titleRect, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                var pathRect = new Rectangle(x, card.Y + 30, textW, 18);
                TextRenderer.DrawText(g, q.OriginalPath, new Font("Segoe UI", 8.5f), pathRect, p.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

                string meta = FindingList.When(ParseWhen(q.QuarantinedAt)) + "      " + q.Reason + "      " + SizeText(q.SizeBytes);
                var metaRect = new Rectangle(x, card.Y + 49, textW, 18);
                TextRenderer.DrawText(g, meta, new Font("Segoe UI", 8.5f), metaRect, p.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

                var restoreRect = RestoreRectFor(i);
                var deleteRect = DeleteRectFor(i);

                Draw.RoundRect(g, restoreRect, 6,
                    (i == hover && hoverBtn == 0) ? Draw.Mix(p.Bg, p.Accent, 0.7) : p.Accent);
                TextRenderer.DrawText(g, "Restore", new Font("Segoe UI", 8.5f, FontStyle.Bold), restoreRect,
                    Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                Color delFill = (i == hover && hoverBtn == 1)
                    ? Draw.Mix(p.Bg, p.Danger, 0.4)
                    : Draw.Mix(p.Bg, p.Text, 0.14);
                Draw.RoundRect(g, deleteRect, 6, delFill);
                TextRenderer.DrawText(g, "Delete permanently", new Font("Segoe UI", 8f), deleteRect,
                    p.Danger, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // Sweep page's per-host status list: same painted-card idiom as
    // QuarantineList (RoundRect body, thin left-edge stripe, Mix-derived hover
    // state), but each row is a single click target - clicking selects the host
    // and raises HostSelected so the page can swap a FindingList to that host's
    // results - rather than carrying per-row action buttons.
    public class SweepList : Control
    {
        private List<SweepTarget> items = new List<SweepTarget>();
        private readonly VScrollBar sb = new VScrollBar();
        private int hover = -1;
        private int selected = -1;
        private const int RowH = 60;

        public string EmptyText = "Enter hosts above and click Sweep Sweep.";
        public event EventHandler<SweepTarget> HostSelected;

        public SweepList()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            sb.Dock = DockStyle.Right;
            sb.Width = 14;
            sb.SmallChange = RowH;
            sb.LargeChange = RowH * 3;
            sb.ValueChanged += delegate { Invalidate(); };
            Controls.Add(sb);
        }

        public void SetItems(IEnumerable<SweepTarget> e)
        {
            items = new List<SweepTarget>(e);
            selected = -1;
            sb.Value = 0;
            Resync();
            Invalidate();
        }

        // Called while a sweep is running: the same SweepTarget objects already
        // in `items` are being mutated in place by the background sweep, so
        // there is nothing to re-fetch - just repaint with their current state.
        public void RefreshStatuses()
        {
            Invalidate();
        }

        private void Resync()
        {
            int total = items.Count * RowH;
            int visible = Math.Max(1, ClientSize.Height);
            if (total <= visible) { sb.Visible = false; sb.Maximum = 0; sb.Value = 0; }
            else
            {
                sb.Visible = true;
                sb.Minimum = 0;
                sb.Maximum = total - visible + sb.LargeChange;
                if (sb.Value > total - visible) sb.Value = Math.Max(0, total - visible);
            }
        }

        protected override void OnResize(EventArgs e) { Resync(); base.OnResize(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (sb.Visible)
            {
                int v = sb.Value - Math.Sign(e.Delta) * RowH;
                sb.Value = Math.Max(sb.Minimum, Math.Min(sb.Maximum - sb.LargeChange + 1, v));
            }
            base.OnMouseWheel(e);
        }

        private int IndexAt(int y)
        {
            int i = (y + sb.Value) / RowH;
            return (i >= 0 && i < items.Count) ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i != hover) { hover = i; Invalidate(); }
            Cursor = (i >= 0) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
            int i = IndexAt(e.Y);
            if (i >= 0)
            {
                selected = i;
                Invalidate();
                if (HostSelected != null) HostSelected(this, items[i]);
            }
            base.OnMouseDown(e);
        }

        // Failed = Danger. Done clean = OkColor. Done with findings = Warn or
        // Danger depending on the worst severity actually found. Anything still
        // in flight (Pending/Copying/Running/Collecting) stays TextDim - it has
        // not resolved to a verdict yet.
        private Color StatusColor(SweepTarget t)
        {
            var p = Theme.P;
            if (t.Status == SweepStatus.Failed) return p.Danger;
            if (t.Status == SweepStatus.Done)
            {
                if (t.Result != null)
                {
                    bool bad = t.Result.Findings.Exists(delegate (Finding f) { return f.Severity == Sev.Bad; });
                    if (bad) return p.Danger;
                    bool warn = t.Result.Findings.Exists(delegate (Finding f) { return f.Severity == Sev.Warn; });
                    if (warn) return p.Warn;
                }
                return p.OkColor;
            }
            return p.TextDim;
        }

        private static string StatusText(SweepTarget t)
        {
            if (t.Status == SweepStatus.Failed) return "Failed - " + (string.IsNullOrEmpty(t.Error) ? "unknown error" : t.Error);
            if (t.Status == SweepStatus.Done && t.Result != null)
            {
                int bad = 0, warn = 0;
                foreach (var f in t.Result.Findings)
                {
                    if (f.Severity == Sev.Bad) bad++;
                    else if (f.Severity == Sev.Warn) warn++;
                }
                if (bad == 0 && warn == 0) return "Done - clean";
                return "Done - " + bad + " serious, " + warn + " to check";
            }
            return t.Status.ToString();
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

            int w = ClientSize.Width - (sb.Visible ? sb.Width : 0);
            int first = Math.Max(0, sb.Value / RowH);
            int last = Math.Min(items.Count - 1, (sb.Value + ClientSize.Height) / RowH);

            for (int i = first; i <= last; i++)
            {
                var t = items[i];
                int y = i * RowH - sb.Value;
                var card = new Rectangle(10, y + 4, w - 20, RowH - 8);

                Color fg = StatusColor(t);

                Color bg = p.SurfaceElevated;
                if (i == hover) bg = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.11 : 0.07);
                if (i == selected) bg = Draw.Mix(p.Bg, p.Accent, Theme.IsDark ? 0.26 : 0.14);

                Draw.RoundRect(g, card, 8, bg);
                Draw.RoundRectOutline(g, card, 8, p.BorderStandard);
                Draw.RoundRect(g, new Rectangle(card.X, card.Y + 6, 3, card.Height - 12), 2, fg);

                int x = card.X + 16;
                var titleRect = new Rectangle(x, card.Y + 8, Math.Max(60, card.Width - 32), 20);
                TextRenderer.DrawText(g, t.Host, new Font("Segoe UI", 9.5f, FontStyle.Bold), titleRect, p.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                var statusRect = new Rectangle(x, card.Y + 30, Math.Max(60, card.Width - 32), 22);
                TextRenderer.DrawText(g, StatusText(t), new Font("Segoe UI", 8.5f), statusRect, fg,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // Small headline number for the overview page. Optionally clickable - see
    // Clickable below - in which case it gets a hand cursor and a quiet hover
    // fill, reusing the same Draw.Mix idiom as every other hoverable row in
    // this app rather than inventing a second visual language for it.
    public class StatTile : Control
    {
        public string Caption = "";
        public string Value = "0";
        public Color Tint = Color.Gray;

        private bool clickable;
        private bool hover;

        public bool Clickable
        {
            get { return clickable; }
            set { clickable = value; Cursor = value ? Cursors.Hand : Cursors.Default; }
        }

        public StatTile()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(150, 88);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            if (clickable) { hover = true; Invalidate(); }
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover) { hover = false; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.P;
            var g = e.Graphics;
            g.Clear(p.Bg);

            // Idle fill now shares SurfaceElevated with the FindingList/
            // QuarantineList/SweepList cards (was a slightly different ad-hoc
            // Mix t) - see design brief Part 6. Hover keeps its own stronger tint.
            Color fill = p.SurfaceElevated;
            if (clickable && hover) fill = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.12 : 0.08);

            var card = new Rectangle(0, 0, Width - 1, Height - 1);
            Draw.RoundRect(g, card, 10, fill);
            Draw.RoundRectOutline(g, card, 10, p.BorderStandard);
            Draw.RoundRect(g, new Rectangle(0, 0, 4, Height - 1), 2, Tint);

            TextRenderer.DrawText(g, Value, new Font("Segoe UI", 22f, FontStyle.Bold),
                new Rectangle(16, 8, Width - 24, 40), Tint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, Caption, new Font("Segoe UI", 8.5f),
                new Rectangle(18, 52, Width - 26, 24), p.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Small filled dot for the Overview hero, coloured to match the verdict -
    // paired with the eyebrow label rather than the tiles below, which already
    // carry their own tint. Static, not animated: this app just had its "too
    // much red" problem fixed, so nothing here pulses or draws extra attention.
    public class StatusDot : Control
    {
        public Color DotColor = Color.Gray;

        public StatusDot()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            Size = new Size(10, 10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(DotColor)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
            g.SmoothingMode = old;
        }
    }

    // "Open file location" / "Copy path" - built fresh on every right-click so it
    // always reflects the current theme, rather than a cached menu going stale
    // after a theme switch.
    public static class FileMenu
    {
        public static ContextMenuStrip Build(string path)
        {
            var p = Theme.P;
            var menu = new ContextMenuStrip();
            menu.Renderer = new ThemedMenuRenderer();
            menu.BackColor = p.Panel;
            menu.Font = Brand.F(9f);

            bool exists = !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

            var open = new ToolStripMenuItem(exists ? "Open file location" : "File no longer on disk");
            open.ForeColor = exists ? p.Text : p.TextDim;
            open.Enabled = exists;
            open.Click += delegate { ShowInFolder(path); };
            menu.Items.Add(open);

            var copy = new ToolStripMenuItem("Copy path");
            copy.ForeColor = p.Text;
            copy.Click += delegate { try { Clipboard.SetText(path); } catch { } };
            menu.Items.Add(copy);

            return menu;
        }

        public static void ShowInFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
                else MessageBox.Show("This file is no longer on disk.", "Aftermath");
            }
            catch { }
        }
    }

    public class ThemedMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.P.Panel; } }
        public override Color MenuBorder { get { return Theme.P.Border; } }
        public override Color MenuItemBorder { get { return Theme.P.Accent; } }
        public override Color ImageMarginGradientBegin { get { return Theme.P.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.P.Panel; } }
        public override Color ImageMarginGradientEnd { get { return Theme.P.Panel; } }
        public override Color SeparatorDark { get { return Theme.P.Border; } }
        public override Color SeparatorLight { get { return Theme.P.Border; } }
    }

    public class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        public ThemedMenuRenderer() : base(new ThemedMenuColors()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var p = Theme.P;
            var rect = new Rectangle(Point.Empty, e.Item.Size);
            Color bg = e.Item.Selected ? Draw.Mix(p.Panel, p.Accent, Theme.IsDark ? 0.30 : 0.18) : p.Panel;
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, rect);
        }
    }

    // Small tier badge next to the wordmark - reuses the severity-chip visual
    // language (a tinted pill) rather than inventing new chrome.
    public class PlanPill : Control
    {
        public string Label = "FREE";

        public PlanPill()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(56, 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.P;
            var g = e.Graphics;
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
            var chip = new Rectangle(0, 1, Width - 1, Height - 2);
            // Neutral (Cendre), not Accent: this pill sits a few pixels from the
            // wordmark's own accent hairline, and a tier label competing with the
            // brand's one red hairline for the same colour reads as noise, not
            // hierarchy. The severity system already reserves red for "serious" -
            // a plan badge is neither serious nor an accent moment.
            Draw.RoundRect(g, chip, 5, Color.FromArgb(Theme.IsDark ? 40 : 26, p.TextDim));
            TextRenderer.DrawText(g, Label, new Font(Brand.BodyFace, 7.2f, FontStyle.Bold), chip, p.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // A quiet callout: title, body, one optional action button, thin accent
    // stripe. Deliberately flatter than a FindingList card - this explains
    // something, it is not a finding - so it should not look like one.
    public class InfoCard : Panel
    {
        private readonly Label lblTitle = new Label();
        private readonly Label lblBody = new Label();
        public Button Action;
        private bool compact;

        public InfoCard()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            lblTitle.AutoSize = true;
            lblTitle.Font = Brand.F(11f, FontStyle.Bold);
            lblTitle.Location = new Point(20, 12);
            Controls.Add(lblTitle);

            lblBody.AutoSize = false;
            lblBody.Location = new Point(22, 36);
            lblBody.Height = 54;
            Controls.Add(lblBody);

            Resize += delegate { Reflow(); };
        }

        public string Title { get { return lblTitle.Text; } set { lblTitle.Text = value; } }
        public string Body { get { return lblBody.Text; } set { lblBody.Text = value; } }

        // Slim single-line strip (title + action, no body) instead of the full
        // title-and-paragraph card - for callouts that should read as a footnote,
        // not a pitch. The two-line layout stays the default for anything else
        // that wants it.
        public bool Compact
        {
            get { return compact; }
            set
            {
                compact = value;
                lblTitle.Font = value ? Brand.F(9f, FontStyle.Regular) : Brand.F(11f, FontStyle.Bold);
                lblTitle.AutoSize = !value;
                lblTitle.AutoEllipsis = value;
                if (value) lblTitle.Height = 18;
                Reflow();
                Invalidate();
            }
        }

        public void SetAction(Button b)
        {
            Action = b;
            Controls.Add(b);
            Reflow();
        }

        private void Reflow()
        {
            int reserve = (Action != null) ? Action.Width + 30 : 0;

            if (compact)
            {
                lblBody.Visible = false;
                lblTitle.Width = Math.Max(80, ClientSize.Width - 40 - reserve);
                lblTitle.Location = new Point(20, (ClientSize.Height - lblTitle.Height) / 2);
            }
            else
            {
                lblBody.Visible = true;
                lblTitle.Location = new Point(20, 12);
                lblBody.Width = Math.Max(120, ClientSize.Width - 44 - reserve);
            }

            if (Action != null)
                Action.Location = new Point(ClientSize.Width - Action.Width - 20, (ClientSize.Height - Action.Height) / 2);
        }

        public void Restyle()
        {
            var p = Theme.P;
            // Was its own slightly different ad-hoc Mix t - now shares the same
            // elevated-surface tint as the other cards (design brief Part 6).
            BackColor = p.SurfaceElevated;
            lblTitle.ForeColor = p.Text;
            lblBody.ForeColor = p.TextDim;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var p = Theme.P;
            Draw.RoundRect(e.Graphics, new Rectangle(0, 6, 3, Height - 12), 2, p.Accent);
            // Panel's fill is a plain rectangle (not RoundRect), so the added
            // outline is a plain rectangle too - subtle full-perimeter border in
            // addition to the existing fill/left-stripe.
            using (var pen = new Pen(p.BorderStandard))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    // ---------- Settings workspace widgets ----------
    // Reusable pieces for the Settings pages, built from the same paint idioms
    // as the rest of the app (Draw.RoundRect/RoundRectOutline, Theme.P, Metrics
    // constants) rather than new one-off chrome.

    // Themed on/off switch. A plain flat Button reads as "click me", not as a
    // state - this draws a sliding track+knob instead, the one shape that always
    // reads as a toggle regardless of colour. Only OnMouseDown raises
    // CheckedChanged - setting the Checked property programmatically (used to
    // sync the control to the real on-disk/registered state on refresh) never
    // fires it, so refreshing the UI can never re-trigger the action that state
    // came from.
    public class Toggle : Control
    {
        private bool isOn;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isOn; }
            set { if (isOn != value) { isOn = value; Invalidate(); } }
        }

        public Toggle()
        {
            Size = new Size(46, 24);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Enabled && e.Button == MouseButtons.Left)
            {
                isOn = !isOn;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.P;
            var g = e.Graphics;
            // Always drawn on the plain page background (Theme.P.Bg), same
            // assumption StatTile already makes - both Toggle instances in this
            // app sit in a SettingRow on a flat Settings page, never inside a
            // DangerZone card, so there is no second surface colour to track.
            g.Clear(p.Bg);

            var track = new Rectangle(0, 0, Width - 1, Height - 1);
            Color onFill = Enabled ? p.Accent : Draw.Mix(p.Bg, p.Accent, 0.35);
            Color offFill = Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.16 : 0.12);
            Draw.RoundRect(g, track, track.Height / 2, isOn ? onFill : offFill);
            Draw.RoundRectOutline(g, track, track.Height / 2, p.BorderStandard);

            int knobD = track.Height - 6;
            int knobX = isOn ? track.Right - knobD - 3 : track.X + 3;
            var knob = new Rectangle(knobX, 3, knobD, knobD);
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var kb = new SolidBrush(Enabled ? Color.White : Draw.Mix(Color.White, p.Bg, 0.3)))
                g.FillEllipse(kb, knob);
            g.SmoothingMode = old;
        }
    }

    // LABEL / description / hosted control, laid out consistently across every
    // Settings row - the hosted control can be a Toggle, a plain TextBox, a
    // Button, or a small composite Panel of controls (e.g. a number box plus a
    // Save button) for the one row that needs more than a single control.
    public class SettingRow : Panel
    {
        private readonly Label lblLabel = new Label();
        private readonly Label lblDesc = new Label();
        private Control hosted;

        public SettingRow()
        {
            Height = 64;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Padding = new Padding(Metrics.SpacingUnit * 2, Metrics.SpacingUnit, Metrics.SpacingUnit * 2, Metrics.SpacingUnit);

            lblLabel.Font = Brand.F(10f, FontStyle.Bold);
            lblLabel.AutoSize = true;
            lblLabel.Location = new Point(Padding.Left, 10);
            Controls.Add(lblLabel);

            lblDesc.AutoSize = false;
            lblDesc.Height = 18;
            lblDesc.Location = new Point(Padding.Left, 33);
            Controls.Add(lblDesc);

            Resize += delegate { Reflow(); };
        }

        public string Label { get { return lblLabel.Text; } set { lblLabel.Text = value; } }
        public string Description { get { return lblDesc.Text; } set { lblDesc.Text = value; Reflow(); } }

        public void SetControl(Control c)
        {
            hosted = c;
            Controls.Add(c);
            Reflow();
        }

        private void Reflow()
        {
            int reserve = (hosted != null) ? hosted.Width + Metrics.SpacingUnit * 3 : 0;
            int textW = Math.Max(80, ClientSize.Width - Padding.Left - Padding.Right - reserve);
            lblLabel.MaximumSize = new Size(textW, 0);
            lblDesc.Width = textW;

            if (hosted != null)
                hosted.Location = new Point(ClientSize.Width - Padding.Right - hosted.Width,
                    (ClientSize.Height - hosted.Height) / 2);
        }

        // Only the two labels - not BackColor. A row can sit directly on a plain
        // page (Theme.P.Bg) or inside a DangerZone card (Theme.P.SurfaceElevated),
        // so the caller sets BackColor itself, same as every other Panel in this
        // app already does, rather than this control guessing which surface it
        // is on.
        public void Restyle()
        {
            var p = Theme.P;
            lblLabel.ForeColor = p.Text;
            lblDesc.ForeColor = p.TextDim;
            Invalidate();
        }

        // Thin BorderSubtle rule under the row - the same "barely there" divider
        // role Theme.cs already defines, so a stack of rows reads as one list
        // rather than a stack of separately-bordered cards.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var p = Theme.P;
            using (var pen = new Pen(p.BorderSubtle))
                e.Graphics.DrawLine(pen, Padding.Left, Height - 1, Width - Padding.Right, Height - 1);
        }
    }

    // Danger-coloured left stripe on a section, matching the severity-stripe
    // language FindingList/QuarantineList already use for "this matters" - used
    // around the one Settings control (quarantine retention) that permanently
    // destroys data on save. Not used anywhere without a genuinely destructive
    // action inside it.
    public class DangerZone : Panel
    {
        public DangerZone()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            // Left padding clears the stripe; a thin margin on the other three
            // sides too, so the 1px outline drawn in OnPaint below is not
            // entirely hidden under a Dock=Fill child that would otherwise
            // reach every edge.
            Padding = new Padding(Metrics.SpacingUnit * 2, 4, 4, 4);
        }

        public void Restyle()
        {
            BackColor = Theme.P.SurfaceElevated;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var p = Theme.P;
            Draw.RoundRect(e.Graphics, new Rectangle(0, 6, 3, Height - 12), 2, p.Danger);
            using (var pen = new Pen(p.BorderStandard))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    // Service name, a status dot (Connected = OkColor, Not connected = TextDim -
    // no other state exists, since Aftermath has no partial/pending connection
    // state), description text, and one optional action button. Built once for
    // the CONNECTIONS settings page's honest empty state, but generic enough to
    // show a real connection later without changing this class.
    public class ConnectionCard : Panel
    {
        private readonly StatusDot dot = new StatusDot();
        private readonly Label lblName = new Label();
        private readonly Label lblStatus = new Label();
        private readonly Label lblDesc = new Label();
        private Button action;
        private bool connected;

        public ConnectionCard()
        {
            Height = 84;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            dot.Location = new Point(20, 26);
            Controls.Add(dot);

            lblName.Font = Brand.F(10.5f, FontStyle.Bold);
            lblName.AutoSize = true;
            lblName.Location = new Point(38, 14);
            Controls.Add(lblName);

            lblStatus.AutoSize = true;
            lblStatus.Font = Brand.F(8f, FontStyle.Bold);
            lblStatus.Location = new Point(38, 34);
            Controls.Add(lblStatus);

            lblDesc.AutoSize = false;
            lblDesc.Height = 18;
            lblDesc.Location = new Point(38, 54);
            Controls.Add(lblDesc);

            Resize += delegate { Reflow(); };
        }

        public string ServiceName { get { return lblName.Text; } set { lblName.Text = value; } }
        public string Description { get { return lblDesc.Text; } set { lblDesc.Text = value; } }

        public bool Connected
        {
            get { return connected; }
            set { connected = value; Restyle(); }
        }

        public void SetAction(Button b)
        {
            action = b;
            Controls.Add(b);
            Reflow();
        }

        private void Reflow()
        {
            int reserve = (action != null) ? action.Width + Metrics.SpacingUnit * 3 : Metrics.SpacingUnit * 2;
            lblDesc.Width = Math.Max(100, ClientSize.Width - 38 - reserve);
            if (action != null)
                action.Location = new Point(ClientSize.Width - action.Width - 20,
                    (ClientSize.Height - action.Height) / 2);
        }

        public void Restyle()
        {
            var p = Theme.P;
            BackColor = p.SurfaceElevated;
            dot.BackColor = BackColor;
            dot.DotColor = connected ? p.OkColor : p.TextDim;
            lblName.ForeColor = p.Text;
            lblStatus.ForeColor = dot.DotColor;
            lblStatus.Text = connected ? "CONNECTED" : "NOT CONNECTED";
            lblDesc.ForeColor = p.TextDim;
            dot.Invalidate();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.P.BorderStandard))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    // Clickable row for the Settings landing page - title, one-line description,
    // hover state, accent stripe. Same card language as StatTile/FindingList
    // (Draw.RoundRect fill, RoundRectOutline border, Metrics.CornerRadiusCard)
    // but full-width and click-to-navigate instead of a fixed-size number tile.
    public class SettingsCard : Control
    {
        public string Title = "";
        public string Description = "";
        public event EventHandler Clicked;
        private bool hover;

        public SettingsCard()
        {
            Height = 64;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true; Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Clicked != null) Clicked(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.P;
            var g = e.Graphics;
            // Always drawn on the plain Settings landing page background, same
            // assumption StatTile already makes for Overview - no caller-set
            // BackColor to track.
            g.Clear(p.Bg);

            var card = new Rectangle(0, 2, Width - 1, Height - 4);
            Color fill = hover ? Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.11 : 0.07) : p.SurfaceElevated;
            Draw.RoundRect(g, card, Metrics.CornerRadiusCard, fill);
            Draw.RoundRectOutline(g, card, Metrics.CornerRadiusCard, p.BorderStandard);
            Draw.RoundRect(g, new Rectangle(card.X, card.Y + 6, 3, card.Height - 12), 2, p.Accent);

            var titleRect = new Rectangle(card.X + 20, card.Y + 10, card.Width - 40, 20);
            TextRenderer.DrawText(g, Title, new Font("Segoe UI", 10f, FontStyle.Bold), titleRect, p.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            var descRect = new Rectangle(card.X + 20, card.Y + 32, card.Width - 40, 20);
            TextRenderer.DrawText(g, Description, new Font("Segoe UI", 8.5f), descRect, p.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // A caller-supplied group of filter checkboxes shown together under one
    // heading in the FilterBar popover, e.g. Label="Severity", Options=["Serious","Check","OK"].
    public class FilterGroup
    {
        public string Label;
        public List<string> Options;
    }

    // Compact toolbar strip that sits above a list/table: a Filter button that
    // opens a borderless popover of grouped checkboxes (same owner-drawn
    // dropdown idiom as ThemedMenuRenderer's ContextMenuStrip, but custom-drawn
    // since checkboxes-by-group don't fit a ToolStripMenuItem), removable chips
    // for each active filter (same tinted-pill language as FindingList's
    // severity chip), a "Clear all" link, and a custom-painted sort dropdown -
    // a plain ComboBox would look system-themed, same reason this app bans
    // TabControl/ListView.
    public class FilterBar : Control
    {
        private List<FilterGroup> groups = new List<FilterGroup>();
        private List<string> sortOptions = new List<string>();
        private readonly HashSet<string> activeFilters = new HashSet<string>();
        private string activeSort;

        public event EventHandler FiltersChanged;

        private const int BtnH = 30;
        private const int ChipH = 26;
        private const int Gap = 8;

        private Rectangle filterBtnRect;
        private Rectangle clearRect;
        private Rectangle sortRect;
        private bool filterBtnHover;
        private bool clearHover;
        private bool sortHover;
        private int hoverChip = -1;         // index into activeFilters ordering, for chip-body hover
        private int hoverChipX = -1;        // index for the little "x" close glyph specifically

        private FilterPopover openPopover;
        private SortPopover openSortPopover;

        // Cached per-paint layout of chips, rebuilt every OnPaint so hit-testing
        // in OnMouseMove/OnMouseDown always matches what was last drawn.
        private readonly List<Rectangle> chipRects = new List<Rectangle>();
        private readonly List<Rectangle> chipCloseRects = new List<Rectangle>();
        private readonly List<string> chipKeys = new List<string>();

        public FilterBar()
        {
            Height = 44;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetFilterGroups(List<FilterGroup> g)
        {
            groups = g ?? new List<FilterGroup>();
            Invalidate();
        }

        public void SetSortOptions(List<string> options)
        {
            sortOptions = options ?? new List<string>();
            if (activeSort == null && sortOptions.Count > 0) activeSort = sortOptions[0];
            Invalidate();
        }

        public HashSet<string> ActiveFilters { get { return activeFilters; } }

        public string ActiveSort
        {
            get { return activeSort; }
            set { activeSort = value; Invalidate(); }
        }

        private void RaiseChanged()
        {
            Invalidate();
            if (FiltersChanged != null) FiltersChanged(this, EventArgs.Empty);
        }

        private static string KeyFor(string groupLabel, string option)
        {
            return groupLabel + ":" + option;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool fb = filterBtnRect.Contains(e.Location);
            bool cb = clearRect.Contains(e.Location);
            bool sb2 = sortRect.Contains(e.Location);
            int hc = -1, hcx = -1;
            for (int i = 0; i < chipRects.Count; i++)
            {
                if (chipCloseRects[i].Contains(e.Location)) { hc = i; hcx = i; break; }
                if (chipRects[i].Contains(e.Location)) { hc = i; break; }
            }
            if (fb != filterBtnHover || cb != clearHover || sb2 != sortHover || hc != hoverChip || hcx != hoverChipX)
            {
                filterBtnHover = fb; clearHover = cb; sortHover = sb2; hoverChip = hc; hoverChipX = hcx;
                Invalidate();
            }
            Cursor = (fb || cb || sb2 || hc >= 0) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            filterBtnHover = clearHover = sortHover = false;
            hoverChip = hoverChipX = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }

            if (filterBtnRect.Contains(e.Location))
            {
                CloseSortPopover();
                ToggleFilterPopover();
            }
            else if (clearRect.Contains(e.Location) && activeFilters.Count > 0)
            {
                activeFilters.Clear();
                RaiseChanged();
            }
            else if (sortRect.Contains(e.Location))
            {
                CloseFilterPopover();
                ToggleSortPopover();
            }
            else
            {
                for (int i = 0; i < chipRects.Count; i++)
                {
                    if (chipCloseRects[i].Contains(e.Location))
                    {
                        activeFilters.Remove(chipKeys[i]);
                        RaiseChanged();
                        break;
                    }
                }
            }
            base.OnMouseDown(e);
        }

        private void ToggleFilterPopover()
        {
            if (openPopover != null) { CloseFilterPopover(); return; }
            var loc = PointToScreen(new Point(filterBtnRect.Left, filterBtnRect.Bottom + 4));
            openPopover = new FilterPopover(groups, activeFilters);
            openPopover.OptionToggled += delegate (object s, string key)
            {
                if (activeFilters.Contains(key)) activeFilters.Remove(key); else activeFilters.Add(key);
                RaiseChanged();
                openPopover.Refresh();
            };
            openPopover.Deactivate += delegate { CloseFilterPopover(); };
            openPopover.Location = loc;
            openPopover.Show(this);
        }

        private void CloseFilterPopover()
        {
            if (openPopover == null) return;
            var p = openPopover;
            openPopover = null;
            p.Close();
        }

        private void ToggleSortPopover()
        {
            if (openSortPopover != null) { CloseSortPopover(); return; }
            var loc = PointToScreen(new Point(sortRect.Left, sortRect.Bottom + 4));
            openSortPopover = new SortPopover(sortOptions, activeSort);
            openSortPopover.OptionPicked += delegate (object s, string opt)
            {
                activeSort = opt;
                RaiseChanged();
                CloseSortPopover();
            };
            openSortPopover.Deactivate += delegate { CloseSortPopover(); };
            openSortPopover.Location = loc;
            openSortPopover.Show(this);
        }

        private void CloseSortPopover()
        {
            if (openSortPopover == null) return;
            var p = openSortPopover;
            openSortPopover = null;
            p.Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.P;
            var g = e.Graphics;
            g.Clear(p.Bg);

            chipRects.Clear();
            chipCloseRects.Clear();
            chipKeys.Clear();

            int x = 0;
            int cy = (Height - BtnH) / 2;

            // Filter button.
            string filterLabel = activeFilters.Count > 0 ? "Filter (" + activeFilters.Count + ")" : "Filter";
            int filterW = TextRenderer.MeasureText(filterLabel, new Font("Segoe UI", 8.5f, FontStyle.Bold)).Width + 34;
            filterBtnRect = new Rectangle(x, cy, filterW, BtnH);
            Color filterFill = (filterBtnHover || openPopover != null)
                ? Draw.Mix(p.Bg, p.Accent, Theme.IsDark ? 0.30 : 0.18)
                : Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.10 : 0.06);
            Draw.RoundRect(g, filterBtnRect, 7, filterFill);
            Draw.RoundRectOutline(g, filterBtnRect, 7, p.BorderStandard);
            TextRenderer.DrawText(g, filterLabel, new Font("Segoe UI", 8.5f, FontStyle.Bold), filterBtnRect,
                activeFilters.Count > 0 ? p.Accent : p.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += filterW + Gap;

            // Chips.
            foreach (var key in activeFilters)
            {
                string label = ChipLabel(key);
                int textW = TextRenderer.MeasureText(label, new Font("Segoe UI", 8f, FontStyle.Bold)).Width;
                int chipW = textW + 34;
                var chip = new Rectangle(x, cy + (BtnH - ChipH) / 2, chipW, ChipH);

                int idx = chipRects.Count;
                bool hov = hoverChip == idx;
                Color chipFill = Color.FromArgb(hov ? (Theme.IsDark ? 66 : 46) : (Theme.IsDark ? 52 : 34), p.Accent);
                Draw.RoundRect(g, chip, ChipH / 2, chipFill);

                var textRect = new Rectangle(chip.X + 12, chip.Y, chip.Width - 32, chip.Height);
                TextRenderer.DrawText(g, label, new Font("Segoe UI", 8f, FontStyle.Bold), textRect, p.Accent,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                var closeRect = new Rectangle(chip.Right - 22, chip.Y + (chip.Height - 16) / 2, 16, 16);
                bool closeHov = hoverChipX == idx;
                if (closeHov) Draw.RoundRect(g, closeRect, 8, Color.FromArgb(Theme.IsDark ? 90 : 60, p.Accent));
                TextRenderer.DrawText(g, "x", new Font("Segoe UI", 8f, FontStyle.Bold), closeRect, p.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                chipRects.Add(chip);
                chipCloseRects.Add(closeRect);
                chipKeys.Add(key);

                x += chipW + Gap;
            }

            // Clear all - only when something is active.
            if (activeFilters.Count > 0)
            {
                string clearLabel = "Clear all";
                int clearW = TextRenderer.MeasureText(clearLabel, new Font("Segoe UI", 8.5f)).Width + 8;
                clearRect = new Rectangle(x, 0, clearW, Height);
                TextRenderer.DrawText(g, clearLabel, new Font("Segoe UI", 8.5f, clearHover ? FontStyle.Underline : FontStyle.Regular),
                    clearRect, p.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                x += clearW + Gap;
            }
            else
            {
                clearRect = Rectangle.Empty;
            }

            // Sort dropdown, right-aligned.
            string sortLabel = "Sort: " + (activeSort ?? "-");
            int sortW = TextRenderer.MeasureText(sortLabel, new Font("Segoe UI", 8.5f)).Width + 40;
            sortRect = new Rectangle(Width - sortW, cy, sortW, BtnH);
            if (sortRect.Left < x) sortRect = new Rectangle(x, cy, sortW, BtnH);
            Color sortFill = (sortHover || openSortPopover != null)
                ? Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.14 : 0.09)
                : p.SurfaceElevated;
            Draw.RoundRect(g, sortRect, 7, sortFill);
            Draw.RoundRectOutline(g, sortRect, 7, p.BorderStandard);
            var sortTextRect = new Rectangle(sortRect.X + 12, sortRect.Y, sortRect.Width - 26, sortRect.Height);
            TextRenderer.DrawText(g, sortLabel, new Font("Segoe UI", 8.5f), sortTextRect, p.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            var caretRect = new Rectangle(sortRect.Right - 20, sortRect.Y, 16, sortRect.Height);
            TextRenderer.DrawText(g, "▾", new Font("Segoe UI", 8f), caretRect, p.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // "GroupLabel:Option" -> "Option" for chip display; falls back to the
        // raw key if it doesn't contain the expected separator.
        private static string ChipLabel(string key)
        {
            int i = key.IndexOf(':');
            return i >= 0 && i < key.Length - 1 ? key.Substring(i + 1) : key;
        }

        // Borderless owner-drawn popover for grouped filter checkboxes - same
        // "float below the button, close on deactivate" pattern as a
        // ContextMenuStrip, but hand-painted since ToolStripMenuItem cannot host
        // per-group checkbox lists.
        private class FilterPopover : Form
        {
            private readonly List<FilterGroup> groups;
            private readonly HashSet<string> active;
            private readonly List<Rectangle> rowRects = new List<Rectangle>();
            private readonly List<string> rowKeys = new List<string>();
            private int hoverRow = -1;
            public event EventHandler<string> OptionToggled;

            public FilterPopover(List<FilterGroup> groups, HashSet<string> active)
            {
                this.groups = groups;
                this.active = active;

                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                DoubleBuffered = true;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

                int rowH = 26;
                int headerH = 22;
                int height = 12;
                int width = 220;
                foreach (var grp in groups)
                {
                    height += headerH;
                    height += (grp.Options != null ? grp.Options.Count : 0) * rowH;
                }
                if (groups.Count == 0) height += 26;
                Size = new Size(width, Math.Max(40, height));

                MouseMove += FilterPopover_MouseMove;
                MouseLeave += delegate { hoverRow = -1; Invalidate(); };
                MouseDown += FilterPopover_MouseDown;
                Paint += FilterPopover_Paint;
            }

            private void FilterPopover_MouseMove(object sender, MouseEventArgs e)
            {
                int idx = -1;
                for (int i = 0; i < rowRects.Count; i++)
                    if (rowRects[i].Contains(e.Location)) { idx = i; break; }
                if (idx != hoverRow) { hoverRow = idx; Invalidate(); }
                Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
            }

            private void FilterPopover_MouseDown(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                for (int i = 0; i < rowRects.Count; i++)
                {
                    if (rowRects[i].Contains(e.Location))
                    {
                        if (OptionToggled != null) OptionToggled(this, rowKeys[i]);
                        return;
                    }
                }
            }

            private void FilterPopover_Paint(object sender, PaintEventArgs e)
            {
                var p = Theme.P;
                var g = e.Graphics;
                g.Clear(p.Panel);

                rowRects.Clear();
                rowKeys.Clear();

                int y = 6;
                int rowH = 26;
                int headerH = 22;

                if (groups.Count == 0)
                {
                    TextRenderer.DrawText(g, "No filters available", new Font("Segoe UI", 8.5f),
                        new Rectangle(10, y, Width - 20, 26), p.TextDim, TextFormatFlags.VerticalCenter);
                }

                foreach (var grp in groups)
                {
                    var headerRect = new Rectangle(10, y, Width - 20, headerH);
                    TextRenderer.DrawText(g, (grp.Label ?? "").ToUpperInvariant(), new Font("Segoe UI", 7.5f, FontStyle.Bold),
                        headerRect, p.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    y += headerH;

                    if (grp.Options != null)
                    {
                        foreach (var opt in grp.Options)
                        {
                            string key = KeyFor(grp.Label, opt);
                            var row = new Rectangle(4, y, Width - 8, rowH);
                            int idx = rowRects.Count;
                            if (idx == hoverRow)
                                Draw.RoundRect(g, row, 6, Draw.Mix(p.Panel, p.Text, Theme.IsDark ? 0.10 : 0.06));

                            var box = new Rectangle(row.X + 10, row.Y + (rowH - 16) / 2, 16, 16);
                            bool on = active.Contains(key);
                            Draw.RoundRect(g, box, 4, on ? p.Accent : Draw.Mix(p.Panel, p.Text, 0.18));
                            if (on)
                                TextRenderer.DrawText(g, "✓", new Font("Segoe UI", 8f, FontStyle.Bold), box,
                                    Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                            var textRect = new Rectangle(box.Right + 10, row.Y, row.Width - (box.Right + 10) - 8, rowH);
                            TextRenderer.DrawText(g, opt, new Font("Segoe UI", 8.5f), textRect, p.Text,
                                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                            rowRects.Add(row);
                            rowKeys.Add(key);
                            y += rowH;
                        }
                    }
                }

                using (var pen = new Pen(p.BorderStrong))
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            protected override bool ShowWithoutActivation { get { return false; } }
        }

        // Borderless owner-drawn popover of plain sort option rows, same visual
        // family as FilterPopover but a flat single-select list (no checkboxes).
        private class SortPopover : Form
        {
            private readonly List<string> options;
            private readonly string current;
            private readonly List<Rectangle> rowRects = new List<Rectangle>();
            private int hoverRow = -1;
            public event EventHandler<string> OptionPicked;

            public SortPopover(List<string> options, string current)
            {
                this.options = options;
                this.current = current;

                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                DoubleBuffered = true;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

                int rowH = 28;
                Size = new Size(180, Math.Max(30, options.Count * rowH + 8));

                MouseMove += delegate (object s, MouseEventArgs e)
                {
                    int idx = -1;
                    for (int i = 0; i < rowRects.Count; i++)
                        if (rowRects[i].Contains(e.Location)) { idx = i; break; }
                    if (idx != hoverRow) { hoverRow = idx; Invalidate(); }
                    Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
                };
                MouseLeave += delegate { hoverRow = -1; Invalidate(); };
                MouseDown += delegate (object s, MouseEventArgs e)
                {
                    if (e.Button != MouseButtons.Left) return;
                    for (int i = 0; i < rowRects.Count; i++)
                        if (rowRects[i].Contains(e.Location))
                        {
                            if (OptionPicked != null) OptionPicked(this, options[i]);
                            return;
                        }
                };
                Paint += SortPopover_Paint;
            }

            private void SortPopover_Paint(object sender, PaintEventArgs e)
            {
                var p = Theme.P;
                var g = e.Graphics;
                g.Clear(p.Panel);

                rowRects.Clear();
                int y = 4;
                int rowH = 28;
                for (int i = 0; i < options.Count; i++)
                {
                    var row = new Rectangle(4, y, Width - 8, rowH);
                    bool isCurrent = options[i] == current;
                    if (i == hoverRow)
                        Draw.RoundRect(g, row, 6, Draw.Mix(p.Panel, p.Text, Theme.IsDark ? 0.10 : 0.06));
                    else if (isCurrent)
                        Draw.RoundRect(g, row, 6, Draw.Mix(p.Panel, p.Accent, Theme.IsDark ? 0.20 : 0.12));

                    var textRect = new Rectangle(row.X + 12, row.Y, row.Width - 24, rowH);
                    TextRenderer.DrawText(g, options[i], new Font("Segoe UI", 8.5f, isCurrent ? FontStyle.Bold : FontStyle.Regular),
                        textRect, isCurrent ? p.Accent : p.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                    rowRects.Add(row);
                    y += rowH;
                }

                using (var pen = new Pen(p.BorderStrong))
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            protected override bool ShowWithoutActivation { get { return false; } }
        }
    }

    // Horizontal row of custom-painted tabs, replacing a themed TabControl.
    // Presentational only - it does not own or touch any page Panel visibility;
    // the caller listens to TabSelected and swaps its own panels, exactly like
    // ShowPage already does for Sidebar.Navigated.
    public class TabStrip : Control
    {
        private class TabItem
        {
            public string Key;
            public string Label;
        }

        private readonly List<TabItem> tabs = new List<TabItem>();
        private readonly List<Rectangle> tabRects = new List<Rectangle>();
        private int selected = -1;
        private int hover = -1;

        public event EventHandler<string> TabSelected;

        public TabStrip()
        {
            Height = 40;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void AddTab(string key, string label)
        {
            tabs.Add(new TabItem { Key = key, Label = label });
            if (selected == -1) selected = 0;
            Invalidate();
        }

        public void ClearTabs()
        {
            tabs.Clear();
            selected = -1;
            hover = -1;
            Invalidate();
        }

        public string SelectedKey
        {
            get { return (selected >= 0 && selected < tabs.Count) ? tabs[selected].Key : null; }
            set
            {
                for (int i = 0; i < tabs.Count; i++)
                {
                    if (tabs[i].Key == value)
                    {
                        if (selected != i) { selected = i; Invalidate(); }
                        return;
                    }
                }
            }
        }

        // Selects the tab and raises TabSelected, mirroring Sidebar.Select - used
        // when the caller wants the click side-effect (e.g. programmatic nav from
        // elsewhere in the UI), as opposed to SelectedKey's silent set used to
        // sync display state without re-triggering the page switch that state
        // came from.
        public void Select(string key)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i].Key == key)
                {
                    selected = i;
                    Invalidate();
                    if (TabSelected != null) TabSelected(this, key);
                    return;
                }
            }
        }

        private int IndexAt(Point pt)
        {
            for (int i = 0; i < tabRects.Count; i++)
                if (tabRects[i].Contains(pt)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            if (i != hover) { hover = i; Invalidate(); }
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
            int i = IndexAt(e.Location);
            if (i >= 0 && i != selected)
            {
                selected = i;
                Invalidate();
                if (TabSelected != null) TabSelected(this, tabs[i].Key);
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.P;
            var g = e.Graphics;
            g.Clear(p.Bg);

            tabRects.Clear();

            int x = 0;
            int h = Height - 1;   // leave room for the bottom divider line
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                int w = TextRenderer.MeasureText(t.Label, new Font("Segoe UI", 9.5f, FontStyle.Bold)).Width + 32;
                var rect = new Rectangle(x, 0, w, h);
                tabRects.Add(rect);

                bool isSel = i == selected;
                Color fg = isSel ? p.Text : p.TextDim;

                if (!isSel && i == hover)
                    Draw.RoundRect(g, new Rectangle(rect.X + 2, rect.Y + 4, rect.Width - 4, rect.Height - 8), 6,
                        Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.08 : 0.05));

                TextRenderer.DrawText(g, t.Label, new Font("Segoe UI", 9.5f, isSel ? FontStyle.Bold : FontStyle.Regular),
                    rect, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // Same selected-state convention as Sidebar: an accent bar marking
                // the active item - here along the bottom edge instead of the left,
                // matching a horizontal tab strip's reading direction.
                if (isSel)
                    Draw.RoundRect(g, new Rectangle(rect.X + 10, h - 3, rect.Width - 20, 3), 1, p.Accent);

                x += w;
            }

            using (var pen = new Pen(p.BorderStandard))
                g.DrawLine(pen, 0, h, Width, h);
        }
    }
}
