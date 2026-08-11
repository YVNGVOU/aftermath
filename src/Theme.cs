// Theme support for Aftermath.
// WinForms draws TabControl, ListView headers and several other controls with
// system colours and ignores BackColor, which is why the app cannot simply be
// "set to dark". Anything that will not theme cleanly is replaced with a
// custom-drawn control instead.

using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Aftermath
{
    public class Palette
    {
        public Color Bg;          // window background
        public Color Panel;       // header / footer bands
        public Color ListBg;      // list background
        public Color RowAlt;      // alternating row
        public Color Text;
        public Color TextDim;
        public Color Border;
        public Color Accent;      // primary button
        public Color Danger;
        public Color Warn;
        public Color OkColor;
        public Color Selection;

        // Three-level divider system (design pass: surface layering). Subtle is
        // a barely-there micro divider inside a card, Standard is the existing
        // Border role (component-to-component), Strong is the one used between
        // major app regions - header/content, sidebar/content, content/status.
        public Color BorderSubtle;
        public Color BorderStandard;
        public Color BorderStrong;

        // One rung up from Panel on the elevation ladder: cards sitting on a
        // panel sitting on the window background. Replaces several near-
        // identical ad-hoc Draw.Mix(Bg, Text, t) calls with one shared value.
        public Color SurfaceElevated;
    }

    public static class Theme
    {
        public static bool IsDark = true;

        private const string RegPath = @"Software\Aftermath";

        // SINVAUX palette v1.2:
        //   Encre     #14110F  near-black
        //   Parchemin #EFE7D9  warm paper
        //   Grenat    #7C2E3A  wine accent, capped at ~10% of any surface
        //   Cendre    #8F8778  support grey
        //
        // Severity colours are functional, not decorative, so they are derived from
        // the palette rather than imported from outside it: SERIOUS lifts Grenat to
        // stay legible on Encre, CHECK is a warm ochre drawn between Cendre and
        // Parchemin, and OK is plain Cendre. No green - the system has no green, and
        // inventing one would break the four-colour rule.
        public static readonly Color Encre = Color.FromArgb(0x14, 0x11, 0x0F);
        public static readonly Color Parchemin = Color.FromArgb(0xEF, 0xE7, 0xD9);
        public static readonly Color Grenat = Color.FromArgb(0x7C, 0x2E, 0x3A);
        public static readonly Color Cendre = Color.FromArgb(0x8F, 0x87, 0x78);

        public static readonly Palette Dark = new Palette
        {
            Bg = Encre,
            Panel = Color.FromArgb(0x1A, 0x16, 0x14),
            ListBg = Color.FromArgb(0x1F, 0x1B, 0x18),
            RowAlt = Color.FromArgb(0x23, 0x1E, 0x1B),
            Text = Parchemin,
            TextDim = Cendre,
            Border = Color.FromArgb(0x2E, 0x28, 0x24),
            BorderSubtle = Draw.Mix(Encre, Parchemin, 0.05),    // a whisper - barely there
            BorderStandard = Color.FromArgb(0x2E, 0x28, 0x24),  // same value as Border - already fits this role
            BorderStrong = Draw.Mix(Encre, Parchemin, 0.22),    // unmistakable, still not bright
            Accent = Grenat,
            Danger = Color.FromArgb(0xC2, 0x63, 0x6E),   // Grenat lifted for legibility on Encre
            Warn = Color.FromArgb(0xC9, 0xA4, 0x6A),   // warm ochre between Cendre and Parchemin
            OkColor = Cendre,
            Selection = Color.FromArgb(0x3A, 0x22, 0x25),
            SurfaceElevated = Draw.Mix(Encre, Parchemin, 0.05)  // matches the FindingList/QuarantineList/SweepList idle-card tint
        };

        public static readonly Palette Light = new Palette
        {
            Bg = Parchemin,
            Panel = Color.FromArgb(0xF7, 0xF1, 0xE7),
            ListBg = Color.FromArgb(0xF7, 0xF2, 0xE9),
            RowAlt = Color.FromArgb(0xE8, 0xDF, 0xCF),
            Text = Encre,
            TextDim = Color.FromArgb(0x6E, 0x67, 0x5C),   // Cendre darkened for contrast on paper
            Border = Color.FromArgb(0xDB, 0xD2, 0xC2),
            BorderSubtle = Draw.Mix(Parchemin, Encre, 0.04),    // a whisper - barely there
            BorderStandard = Color.FromArgb(0xDB, 0xD2, 0xC2),  // same value as Border - already fits this role
            BorderStrong = Draw.Mix(Parchemin, Encre, 0.18),    // unmistakable, still not bright
            Accent = Grenat,
            Danger = Grenat,
            Warn = Color.FromArgb(0x8A, 0x66, 0x24),
            OkColor = Color.FromArgb(0x6B, 0x63, 0x57),
            Selection = Color.FromArgb(0xE2, 0xD3, 0xD2),
            SurfaceElevated = Draw.Mix(Parchemin, Encre, 0.03)  // matches the FindingList/QuarantineList/SweepList idle-card tint
        };

        public static Palette P { get { return IsDark ? Dark : Light; } }

        public static void Load()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (k != null)
                    {
                        var v = Convert.ToString(k.GetValue("Theme"));
                        if (string.Equals(v, "light", StringComparison.OrdinalIgnoreCase)) IsDark = false;
                        else if (string.Equals(v, "dark", StringComparison.OrdinalIgnoreCase)) IsDark = true;
                        return;
                    }
                }
                // No saved preference - follow Windows' own app theme.
                using (var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        var v = k.GetValue("AppsUseLightTheme");
                        if (v != null) IsDark = (Convert.ToInt32(v) == 0);
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    if (k != null) k.SetValue("Theme", IsDark ? "dark" : "light");
                }
            }
            catch { }
        }
    }

    // Shared layout constants for the divider/surface pass. Existing hand-
    // placed Location/Size numbers throughout Ui.cs are untouched - these are
    // only for new spacing and radii introduced going forward.
    // Named Metrics, not Layout: every custom Control subclass inherits an
    // event literally called Control.Layout, so an unqualified "Layout.X"
    // written inside one of those classes silently resolved to the inherited
    // event instead of this class and failed to compile.
    public static class Metrics
    {
        // 8 was already the most common Draw.RoundRect radius in the app
        // (Sidebar rows, FindingList/QuarantineList/SweepList cards), so it is
        // kept as the canonical value rather than picking a new one.
        public const int CornerRadiusCard = 8;

        // Base spacing unit new divider/section spacing in this pass is a
        // multiple of.
        public const int SpacingUnit = 8;
    }
}
