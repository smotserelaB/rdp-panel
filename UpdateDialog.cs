using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace RdpPanel
{
    // the pop-up that shows when a newer version exists
    // result: Yes = download now, Ignore = skip this version, Cancel = remind me later
    class UpdateDialog : Form
    {
        public UpdateDialog(UpdateInfo info)
        {
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

            var hint = new Label
            {
                Text = "Your sessions and settings are kept when you update.",
                ForeColor = Theme.FgMuted,
                AutoSize = false,
                Size = new Size(452, 20),
                Location = new Point(24, 338),
                BackColor = Color.Transparent
            };
            Controls.Add(hint);

            var btnGet = UI.Btn(this, "Download now", 24, 380, 150, true);
            var btnLater = UI.Btn(this, "Remind me later", 184, 380, 140, false);
            var btnSkip = UI.Btn(this, "Skip this version", 334, 380, 142, false);
            btnGet.Click += (s, e) => { DialogResult = DialogResult.Yes; Close(); };
            btnLater.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            btnSkip.Click += (s, e) => { DialogResult = DialogResult.Ignore; Close(); };
            AcceptButton = btnGet;
            CancelButton = btnLater;
            notes.Select(0, 0);
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
