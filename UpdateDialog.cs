using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RdpPanel
{
    // the pop-up that shows when a newer version exists
    // result: Yes = update / download (InstallerPath is set when the installer was downloaded and checked here),
    //         Ignore = skip this version, Cancel = remind me later
    class UpdateDialog : Form
    {
        // set when "Update now" downloaded and verified the installer: the caller starts it
        public string InstallerPath;

        readonly UpdateInfo info;
        readonly bool selfInstall;
        readonly Label hint;
        readonly ProgressBar bar;
        CancellationTokenSource cts;
        bool selfInstallFailed;

        public UpdateDialog(UpdateInfo info)
        {
            this.info = info;
            // the app can update itself when it was installed with the installer and the files are online
            selfInstall = info.FilesReady && UpdateInstaller.CanSelfInstall;

            UI.Init(this, "RDP Panel update", 500, 430);
            ShowInTaskbar = true;
            TopMost = true;   // also visible above the session windows at startup

            // accent header
            var head = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Theme.Accent };
            var icon = new Label
            {
                Text = "⬇",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Symbol", 26f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(70, 96),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            var title = new Label
            {
                Text = "A new version is available!",
                ForeColor = Color.White,
                Font = Theme.UI(15f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(390, 34),
                Location = new Point(90, 18),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            var ver = new Label
            {
                Text = "v" + AppInfo.Version + "   →   v" + info.Version,
                ForeColor = Color.White,
                Font = Theme.UI(11f),
                AutoSize = false,
                Size = new Size(390, 26),
                Location = new Point(90, 52),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            head.Controls.Add(icon);
            head.Controls.Add(title);
            head.Controls.Add(ver);
            Controls.Add(head);

            var what = new Label
            {
                Text = "What's new",
                ForeColor = Theme.Fg,
                Font = Theme.UI(10.5f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(452, 24),
                Location = new Point(24, 112),
                BackColor = Color.Transparent
            };
            Controls.Add(what);

            var notes = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Well,
                ForeColor = Theme.Fg,
                Font = Theme.UI(9.5f),
                Location = new Point(24, 140),
                Size = new Size(452, 190),
                Text = CleanNotes(info.Notes),
                TabStop = false
            };
            Controls.Add(notes);

            hint = new Label
            {
                Text = selfInstall
                    ? "RDP Panel closes, updates and starts again. Your sessions and settings are kept."
                    : "Your sessions and settings are kept when you update.",
                ForeColor = Theme.FgMuted,
                AutoSize = false,
                Size = new Size(452, 20),
                Location = new Point(24, 338),
                BackColor = Color.Transparent
            };
            Controls.Add(hint);
            bar = new ProgressBar { Location = new Point(24, 360), Size = new Size(452, 10), Minimum = 0, Maximum = 100, Visible = false };
            Controls.Add(bar);

            var btnGet = UI.Btn(this, selfInstall ? "Update now" : "Download now", 24, 380, 150, true);
            var btnLater = UI.Btn(this, "Remind me later", 184, 380, 140, false);
            var btnSkip = UI.Btn(this, "Skip this version", 334, 380, 142, false);
            btnGet.Click += async (s, e) =>
            {
                if (!selfInstall || selfInstallFailed) { DialogResult = DialogResult.Yes; Close(); return; }

                btnGet.Enabled = false;
                btnSkip.Enabled = false;
                btnLater.Text = "Cancel";
                bar.Value = 0;
                bar.Visible = true;
                hint.ForeColor = Theme.FgMuted;
                hint.Text = "Downloading v" + info.Version + "…";
                cts = new CancellationTokenSource();
                var token = cts.Token;

                var res = await UpdateInstaller.DownloadAsync(info, p =>
                {
                    if (!IsDisposed && !token.IsCancellationRequested) { bar.Value = Math.Max(0, Math.Min(100, p)); hint.Text = "Downloading v" + info.Version + "…  " + p + "%"; }
                }, token);

                if (IsDisposed || token.IsCancellationRequested) return;
                if (res.Path != null)
                {
                    InstallerPath = res.Path;
                    DialogResult = DialogResult.Yes;
                    Close();
                    return;
                }
                // failed: offer the download page instead
                bar.Visible = false;
                hint.ForeColor = Theme.Bad;
                hint.Text = "Could not update here: " + res.Error;
                btnLater.Text = "Remind me later";
                btnSkip.Enabled = true;
                btnGet.Text = "Open download page";
                btnGet.Enabled = true;
                selfInstallFailed = true;
            };
            btnLater.Click += (s, e) => { if (cts != null) cts.Cancel(); DialogResult = DialogResult.Cancel; Close(); };
            btnSkip.Click += (s, e) => { DialogResult = DialogResult.Ignore; Close(); };
            AcceptButton = btnGet;
            CancelButton = btnLater;
            notes.Select(0, 0);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (cts != null) cts.Cancel();
            base.OnFormClosing(e);
        }

        // GitHub release notes are markdown: make them readable as plain text
        static string CleanNotes(string md)
        {
            if (string.IsNullOrWhiteSpace(md)) return "No release notes for this version.";
            var sb = new StringBuilder();
            foreach (var raw in md.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("**Full Changelog**", StringComparison.OrdinalIgnoreCase)) continue;
                line = line.TrimStart('#', ' ');
                if (raw.TrimStart().StartsWith("* ") || raw.TrimStart().StartsWith("- "))
                    line = "•  " + raw.TrimStart().Substring(2);
                line = line.Replace("**", "");
                sb.Append(line).Append("\r\n");
            }
            var t = sb.ToString().Trim();
            if (t.Length > 2000) t = t.Substring(0, 2000) + "…";
            return t.Length == 0 ? "No release notes for this version." : t;
        }
    }
}
