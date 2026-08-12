// Blocking login gate shown before MainForm when no verified license exists
// on disk. Aftermath now requires an account for every tier, including
// Free - a fresh install (or a logged-out one) cannot reach the app itself
// until AccountClient.Login succeeds and the returned key verifies. The
// server auto-issues a signed Free-tier license on first login for an
// account that never bought anything (see server.js /api/login), so "an
// account" is really "a verified license on disk" underneath - the same
// tamper-proof mechanism a paid tier already uses, not a separate unsigned
// marker.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Aftermath
{
    public static class AccountGate
    {
        // Returns true once a verified license is on disk - either it was
        // already there, or the user just logged in successfully. False
        // means the user closed the gate without logging in; the caller
        // must not start MainForm in that case.
        public static bool EnsureSignedIn()
        {
            if (LicenseStore.Load() != null) return true;

            using (var gate = new AccountGateForm())
            {
                return gate.ShowDialog() == DialogResult.OK && LicenseStore.Load() != null;
            }
        }
    }

    internal class AccountGateForm : Form
    {
        private TextBox txtEmail, txtPassword;
        private Button btnLogin, btnCreateAccount;
        private Label lblStatus, lblIntro;
        private BrandMark mark;

        public AccountGateForm()
        {
            var p = Theme.P;

            Text = Brand.FullName;
            var ico = Brand.AppIcon();
            if (ico != null) Icon = ico;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 320);
            BackColor = p.Bg;
            Font = Brand.F(9f);

            mark = new BrandMark();
            mark.Location = new Point(24, 24);
            mark.Size = new Size(300, 28);
            mark.BackColor = p.Bg;
            Controls.Add(mark);

            lblIntro = new Label();
            lblIntro.Text = "Sign in to use Aftermath. Free is still free - it just needs an account now.";
            lblIntro.ForeColor = p.TextDim;
            lblIntro.Location = new Point(24, 64);
            lblIntro.Size = new Size(352, 36);
            Controls.Add(lblIntro);

            var lblEmail = new Label();
            lblEmail.Text = "Email";
            lblEmail.ForeColor = p.TextDim;
            lblEmail.Location = new Point(24, 112);
            lblEmail.AutoSize = true;
            Controls.Add(lblEmail);

            txtEmail = new TextBox();
            txtEmail.Location = new Point(24, 132);
            txtEmail.Size = new Size(352, 24);
            Controls.Add(txtEmail);

            var lblPassword = new Label();
            lblPassword.Text = "Password";
            lblPassword.ForeColor = p.TextDim;
            lblPassword.Location = new Point(24, 166);
            lblPassword.AutoSize = true;
            Controls.Add(lblPassword);

            txtPassword = new TextBox();
            txtPassword.Location = new Point(24, 186);
            txtPassword.Size = new Size(352, 24);
            txtPassword.PasswordChar = '*';
            Controls.Add(txtPassword);

            btnLogin = Flat("Log in", 110, 32, p.Accent, Color.White);
            btnLogin.Location = new Point(24, 224);
            btnLogin.Click += OnLogin;
            Controls.Add(btnLogin);
            AcceptButton = btnLogin;

            btnCreateAccount = Flat("Create account", 150, 32, p.Panel, p.Text);
            btnCreateAccount.Location = new Point(142, 224);
            btnCreateAccount.Click += delegate
            {
                try { Process.Start(AccountClient.BaseUrl + "/register"); } catch { }
            };
            Controls.Add(btnCreateAccount);

            lblStatus = new Label();
            lblStatus.Location = new Point(24, 268);
            lblStatus.Size = new Size(352, 36);
            Controls.Add(lblStatus);
        }

        private Button Flat(string text, int w, int h, Color back, Color fore)
        {
            var b = new Button();
            b.Text = text;
            b.Width = w; b.Height = h;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = fore;
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void OnLogin(object sender, EventArgs e)
        {
            string email = txtEmail.Text.Trim();
            string password = txtPassword.Text;
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                SetStatus("Enter your email and password.", Theme.P.Danger);
                return;
            }

            btnLogin.Enabled = false;
            btnCreateAccount.Enabled = false;
            SetStatus("Logging in...", Theme.P.TextDim);

            var t = new Thread(delegate ()
            {
                var result = AccountClient.Login(email, password);
                BeginInvoke(new Action<AccountLoginResult>(OnLoginComplete), result);
            });
            t.IsBackground = true;
            t.Start();
        }

        private void OnLoginComplete(AccountLoginResult result)
        {
            btnLogin.Enabled = true;
            btnCreateAccount.Enabled = true;

            if (!result.Success)
            {
                SetStatus(result.ErrorMessage, Theme.P.Danger);
                return;
            }

            // The server now always issues a Free-tier license on first
            // login (see server.js /api/login), so !HasLicense here means
            // the two are genuinely out of sync, not just "no purchase yet".
            if (!result.HasLicense || !LicenseStore.SaveVerified(result.LicenseKey))
            {
                SetStatus("Logged in, but no verifiable license came back. Try again in a moment.", Theme.P.Danger);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void SetStatus(string text, Color color)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }
    }
}
