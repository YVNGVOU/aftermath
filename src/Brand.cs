// SINVAUX branding.
//
// Assets are copied verbatim from SINVAUX-BRAND/exports and embedded into the
// executable, so the app stays a single file with no loose image folder.
// Per the brand rules, exports are never hand-edited - to change a mark, edit
// the generator in SINVAUX-BRAND/source/generation and re-run build.py.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Aftermath
{
    public static class Brand
    {
        public const string Company = "SINVAUX";
        public const string Product = "Aftermath";              // branded house: SINVAUX <plain noun>
        public const string FullName = "SINVAUX Aftermath";

        // Bumped by hand on every release that gets uploaded to the update
        // archive - see UpdateChecker.cs. No build system stamps this
        // automatically (no csproj/AssemblyInfo in this project), so it is
        // the one thing to remember to change before running publish-release.
        public const string Version = "1.1.0";

        // Supporting typeface per the design system; Segoe UI if Inter is absent.
        private static string bodyFace;
        public static string BodyFace
        {
            get
            {
                if (bodyFace == null)
                {
                    bodyFace = "Segoe UI";
                    try
                    {
                        using (var c = new System.Drawing.Text.InstalledFontCollection())
                        {
                            foreach (var f in c.Families)
                            {
                                if (f.Name == "Inter") { bodyFace = "Inter"; break; }
                            }
                        }
                    }
                    catch { }
                }
                return bodyFace;
            }
        }

        public static Font F(float size) { return new Font(BodyFace, size); }
        public static Font F(float size, FontStyle style) { return new Font(BodyFace, size, style); }

        private static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();

        private static Image Load(string name)
        {
            if (cache.ContainsKey(name)) return cache[name];
            Image img = null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream(name))
                {
                    if (s != null)
                    {
                        var ms = new MemoryStream();
                        s.CopyTo(ms);
                        ms.Position = 0;
                        img = Image.FromStream(ms);
                    }
                }
            }
            catch { }
            cache[name] = img;
            return img;
        }

        // "light"/"dark" describe the INK, not the background: light ink for dark UI.
        public static Image Wordmark() { return Load(Theme.IsDark ? "wordmark-light.png" : "wordmark-dark.png"); }
        public static Image Monogram() { return Load(Theme.IsDark ? "monogram-light.png" : "monogram-dark.png"); }

        public static Icon AppIcon()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("aftermath.ico"))
                {
                    if (s != null) return new Icon(s);
                }
            }
            catch { }
            return null;
        }
    }

    // Header lockup: the hex monogram, then "AFTERMATH" as the primary label -
    // this is Aftermath's own product window, not a SINVAUX-branded shell, so
    // the SINVAUX wordmark (the company's own name, spelled out) no longer
    // belongs here. SINVAUX is credited on the Settings > About page instead.
    // A Grenat hairline + SubLabel (e.g. "Settings") appears only when a
    // workspace actually needs to say so - it used to always repeat the
    // product name a second time next to the old wordmark, which reads as
    // redundant now that "AFTERMATH" itself is the primary label. Grenat is
    // capped at roughly 10% of any surface, so it appears here only as the
    // rule and nowhere else in the band.
    public class BrandMark : Control
    {
        // Text shown after the accent hairline when set - e.g. "Settings"
        // while that workspace is active. Null/empty means no second label:
        // "AFTERMATH" alone already says what this window is.
        public string SubLabel;

        public BrandMark()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Height = 34;
            Width = 300;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.P;
            var g = e.Graphics;
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);

            int x = 0;
            var mark = Brand.Monogram();
            if (mark != null)
            {
                // Monogram is square; scale to a 22px box and keep ratio.
                int h = 22;
                int w = (int)Math.Round(mark.Width * (h / (double)mark.Height));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(mark, new Rectangle(x, (Height - h) / 2, w, h));
                x += w + 10;
            }

            var productFont = Brand.F(13f, FontStyle.Bold);
            TextRenderer.DrawText(g, Brand.Product.ToUpperInvariant(), productFont,
                new Rectangle(x, 0, 200, Height), p.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            x += TextRenderer.MeasureText(Brand.Product.ToUpperInvariant(), productFont).Width + 14;

            if (!string.IsNullOrEmpty(SubLabel))
            {
                using (var pen = new Pen(p.Accent, 1.5f))
                    g.DrawLine(pen, x, (Height / 2) - 9, x, (Height / 2) + 9);
                x += 14;

                TextRenderer.DrawText(g, SubLabel, Brand.F(12.5f, FontStyle.Regular),
                    new Rectangle(x, 0, Math.Max(40, Width - x), Height), p.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
