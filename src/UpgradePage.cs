// UpgradePage: the five-tier ladder (Free/Plus/Pro/Max/Enterprise). Reuses
// the same Theme.P-driven paint idioms as every other card in the app
// (Draw.RoundRect/RoundRectOutline, Brand.F, Metrics.CornerRadiusCard) -
// no new UI paradigm. Visible to every tier, including Enterprise, so a
// user can always see the ladder. Never fabricates a capability: a tier's
// feature list here must match Entitlements.cs exactly.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace Aftermath
{
    public class UpgradePage : Panel
    {
        private class TierCard
        {
            public Panel Card;
            public Label Title;
            public Label Price;
            public Label Badge;
            public Label Features;
            public Button Action;
        }

        private readonly Label lblWordmark = new Label();
        private readonly Label lblTitle = new Label();
        private readonly Panel hairline = new Panel();
        private readonly TierCard[] cards = new TierCard[5];

        private static readonly LicenseTier[] TierOrder = new LicenseTier[]
        {
            LicenseTier.Free, LicenseTier.Plus, LicenseTier.Pro, LicenseTier.Max, LicenseTier.Enterprise
        };

        private static readonly string[] TierFeatures = new string[]
        {
            "Detections, Exposure, History\nCleanup and Quarantine",
            "+ Artifacts, Startup, Persistence,\nNetwork, System pages\n+ CSV/JSON export",
            "+ Scheduled Drift\n+ Branded PDF export\n+ Unlimited history",
            "+ Correlation timeline\n+ Custom scan profiles\n+ Sweep up to 5 hosts",
            "+ Unlimited Sweep hosts\n+ Fleet management\n+ Priority support"
        };

        public UpgradePage()
        {
            Dock = DockStyle.Fill;
            DoubleBuffered = true;
            Padding = new Padding(22, 10, 22, 10);

            lblWordmark.Text = "SINVAUX";
            lblWordmark.Font = Brand.F(9f, FontStyle.Bold);
            lblWordmark.AutoSize = true;
            lblWordmark.Location = new Point(22, 8);
            Controls.Add(lblWordmark);

            hairline.Height = 3;
            hairline.Location = new Point(22, 28);
            Controls.Add(hairline);

            lblTitle.Text = "Choose your tier";
            lblTitle.Font = Brand.F(15f, FontStyle.Bold);
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(22, 40);
            Controls.Add(lblTitle);

            for (int i = 0; i < TierOrder.Length; i++)
            {
                var tc = new TierCard();
                tc.Card = new Panel();
                tc.Card.Height = 260;
                var tierForBorder = TierOrder[i];
                tc.Card.Paint += delegate (object sender, PaintEventArgs pe)
                {
                    var p = Theme.P;
                    Color border = (tierForBorder == LicenseTier.Pro) ? p.Accent : p.BorderStandard;
                    var r = new Rectangle(0, 0, tc.Card.Width - 1, tc.Card.Height - 1);
                    Draw.RoundRectOutline(pe.Graphics, r, Metrics.CornerRadiusCard, border);
                };
                Controls.Add(tc.Card);

                tc.Badge = new Label();
                tc.Badge.Text = "MOST POPULAR";
                tc.Badge.Font = Brand.F(7.5f, FontStyle.Bold);
                tc.Badge.AutoSize = true;
                tc.Badge.Location = new Point(14, 10);
                tc.Badge.Visible = (TierOrder[i] == LicenseTier.Pro);
                tc.Card.Controls.Add(tc.Badge);

                tc.Title = new Label();
                tc.Title.Text = TierOrder[i].ToString();
                tc.Title.Font = Brand.F(12f, FontStyle.Bold);
                tc.Title.AutoSize = true;
                tc.Title.Location = new Point(14, 30);
                tc.Card.Controls.Add(tc.Title);

                tc.Price = new Label();
                tc.Price.Text = PriceLabel(TierOrder[i]);
                tc.Price.Font = Brand.F(9f, FontStyle.Regular);
                tc.Price.AutoSize = true;
                tc.Price.Location = new Point(14, 52);
                tc.Card.Controls.Add(tc.Price);

                tc.Features = new Label();
                tc.Features.Text = TierFeatures[i];
                tc.Features.Font = Brand.F(8.5f, FontStyle.Regular);
                tc.Features.AutoSize = false;
                tc.Features.Location = new Point(14, 82);
                tc.Features.Size = new Size(170, 130);
                tc.Card.Controls.Add(tc.Features);

                tc.Action = new Button();
                tc.Action.FlatStyle = FlatStyle.Flat;
                tc.Action.FlatAppearance.BorderSize = 1;
                tc.Action.Height = 28;
                tc.Action.Location = new Point(14, 220);
                tc.Action.Cursor = Cursors.Hand;
                var tier = TierOrder[i];
                tc.Action.Click += delegate { OnActionClicked(tier); };
                tc.Card.Controls.Add(tc.Action);

                cards[i] = tc;
            }

            Resize += delegate { Reflow(); };
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

        // Re-reads Entitlements.Current so buttons reflect the live tier - called
        // by ShowPage every time this page is navigated to (as RefreshTier, not
        // Refresh, to avoid hiding Control.Refresh), so a key activated through
        // Settings -> Connections updates the ladder without a restart.
        public void RefreshTier()
        {
            LicenseTier current = Entitlements.Current.Tier;
            for (int i = 0; i < TierOrder.Length; i++)
            {
                var tc = cards[i];
                LicenseTier tier = TierOrder[i];
                if (tier == current)
                {
                    tc.Action.Text = "Current plan";
                    tc.Action.Enabled = false;
                }
                else if (tier == LicenseTier.Enterprise)
                {
                    tc.Action.Text = "Contact us";
                    tc.Action.Enabled = true;
                }
                else if ((int)tier > (int)current)
                {
                    tc.Action.Text = "Get " + tier;
                    tc.Action.Enabled = true;
                }
                else
                {
                    // Below the current tier - cannot be selected (no downgrade flow).
                    tc.Action.Text = tier.ToString();
                    tc.Action.Enabled = false;
                }
            }
            Restyle();
        }

        private void OnActionClicked(LicenseTier tier)
        {
            try
            {
                if (tier == LicenseTier.Enterprise)
                {
                    Process.Start("mailto:sales@sinvaux.com?subject=Aftermath%20Enterprise");
                }
                else
                {
                    string machineId = AnonymizedMachineId();
                    string url = "https://sinvaux-main.fly.dev/aftermath/buy?tier=" + tier.ToString().ToLowerInvariant() +
                        "&machine=" + machineId;
                    Process.Start(url);
                }
            }
            catch
            {
                // Process.Start can throw if no browser/mail client is registered -
                // nothing useful to recover to here, so this is a silent no-op
                // rather than a fabricated success message.
            }
        }

        // A SHA-256 hash of the machine name, truncated - lets the purchase page
        // correlate a license to a machine without the real machine name ever
        // leaving the device.
        private static string AnonymizedMachineId()
        {
            using (SHA256 sha = new SHA256CryptoServiceProvider())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Environment.MachineName));
                var sb = new StringBuilder();
                for (int i = 0; i < 8 && i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private void Reflow()
        {
            int w = Math.Max(200, ClientSize.Width - 44);
            hairline.Width = w;

            int gap = 12;
            int cardW = Math.Max(140, (w - gap * (cards.Length - 1)) / cards.Length);
            int x = 22;
            int y = 74;
            foreach (var tc in cards)
            {
                tc.Card.Location = new Point(x, y);
                tc.Card.Width = cardW;
                tc.Features.Width = cardW - 28;
                tc.Action.Width = cardW - 28;
                x += cardW + gap;
            }
        }

        public void Restyle()
        {
            var p = Theme.P;
            BackColor = p.Bg;
            lblWordmark.ForeColor = p.TextDim;
            lblTitle.ForeColor = p.Text;
            hairline.BackColor = p.Accent;

            LicenseTier current = Entitlements.Current.Tier;
            for (int i = 0; i < TierOrder.Length; i++)
            {
                var tc = cards[i];
                bool isCurrent = (TierOrder[i] == current);
                bool unlocked = (int)TierOrder[i] <= (int)current;

                tc.Card.BackColor = p.SurfaceElevated;
                tc.Title.ForeColor = p.Text;
                tc.Price.ForeColor = p.TextDim;
                tc.Badge.ForeColor = p.Accent;
                // Dimmed for tiers the user has not unlocked yet - never a fake
                // "enabled" preview, just a visual cue on the text itself.
                tc.Features.ForeColor = unlocked ? p.Text : p.TextDim;

                tc.Action.BackColor = isCurrent
                    ? Draw.Mix(p.Bg, p.Text, Theme.IsDark ? 0.16 : 0.10)
                    : p.Accent;
                tc.Action.ForeColor = isCurrent ? p.TextDim : Color.White;
                tc.Action.FlatAppearance.BorderColor = p.BorderStandard;
            }

            InvalidateCards();
            Invalidate();
        }

        private void InvalidateCards()
        {
            foreach (var tc in cards) tc.Card.Invalidate();
        }
    }
}
