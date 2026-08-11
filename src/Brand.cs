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

    // Header lockup: SINVAUX wordmark, a Grenat hairline, then the product name.
    // Grenat is capped at roughly 10% of any surface, so it appears here only as
    // the rule and nowhere else in the band.
    public class BrandMark : Control
    {
        // Text shown after the accent hairline - defaults to the product name
        // (Brand.Product) but can be swapped so the header reads "Aftermath" or
        // "Settings" depending on which workspace is active. Null/empty falls
        // back to Brand.Product, so existing callers that never touch this see
        // no change at all.
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
            var mark = Brand.Wordmark();
            if (mark != null)
            {
                // Wordmark is 512x115; scale to a 19px cap height and keep ratio.
                int h = 19;
                int w = (int)Math.Round(mark.Width * (h / (double)mark.Height));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(mark, new Rectangle(x, (Height - h) / 2, w, h));
                x += w + 14;
            }
            else
            {
                // Fallback if the resource is missing: set in the body face, letterspaced.
                var txt = Brand.Company;
                TextRenderer.DrawText(g, txt, Brand.F(12f, FontStyle.Bold),
                    new Rectangle(x, 0, 160, Height), p.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                x += TextRenderer.MeasureText(txt, Brand.F(12f, FontStyle.Bold)).Width + 14;
            }

            using (var pen = new Pen(p.Accent, 1.5f))
                g.DrawLine(pen, x, (Height / 2) - 9, x, (Height / 2) + 9);
            x += 14;

            string label = string.IsNullOrEmpty(SubLabel) ? Brand.Product : SubLabel;
            TextRenderer.DrawText(g, label, Brand.F(12.5f, FontStyle.Regular),
                new Rectangle(x, 0, Math.Max(40, Width - x), Height), p.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
