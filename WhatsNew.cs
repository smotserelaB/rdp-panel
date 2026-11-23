using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace RdpPanel
{
    // the release notes of a version, baked into the exe (docs/release-notes/<version>.md), so they work offline
    static class ReleaseNotes
    {
        public static string Get(string version)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ReleaseNotes." + version + ".md"))
                {
                    if (s == null) return null;
                    using (var r = new StreamReader(s, System.Text.Encoding.UTF8)) return r.ReadToEnd();
                }
            }
            catch { return null; }
        }

        // true when "now" is a newer version than "before" (an empty or unreadable "before" counts as older)
        public static bool IsNewer(string now, string before)
        {
            Version a, b;
            if (!Version.TryParse(now, out a)) return false;
            if (!Version.TryParse(before, out b)) return true;
            return a > b;
        }
    }

    // shown once at the first start after an update (not on a fresh install)
    class WhatsNewDialog : Form
    {
        public WhatsNewDialog(string version, string notes)
        {
            UI.Init(this, "RDP Panel – what's new", 500, 430);
            ShowInTaskbar = true;

            var head = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Theme.Accent };
            head.Controls.Add(new Label
            {
                Text = "\u2728",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Symbol", 26f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(70, 96),
                Location = new Point(16, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            });
            head.Controls.Add(new Label
            {
                Text = "What's new in v" + version,
                ForeColor = Color.White,
                Font = Theme.UI(15f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(390, 34),
                Location = new Point(90, 18),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            });
            head.Controls.Add(new Label
            {
                Text = "RDP Panel was updated.",
                ForeColor = Color.White,
                Font = Theme.UI(11f),
                AutoSize = false,
                Size = new Size(390, 26),
                Location = new Point(90, 52),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            });
            Controls.Add(head);

            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Well,
                ForeColor = Theme.Fg,
                Font = Theme.UI(9.5f),
                Location = new Point(24, 112),
                Size = new Size(452, 220),
                Text = Clean(notes),
                TabStop = false
            };
            Controls.Add(box);

            var btnOk = UI.Btn(this, "Got it", 24, 350, 150, true);
            var btnAll = UI.Btn(this, "All release notes", 184, 350, 170, false);
            btnOk.Click += (s, e) => Close();
            btnAll.Click += (s, e) => UI.OpenUrl(AppInfo.ReleasesUrl);
            AcceptButton = btnOk;
            CancelButton = btnOk;
            box.Select(0, 0);
        }

        // the notes are markdown: show them as readable plain text
        static string Clean(string md)
        {
            if (string.IsNullOrWhiteSpace(md)) return "No release notes for this version.";
            var sb = new System.Text.StringBuilder();
            foreach (var raw in md.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                var t = raw.TrimStart();
                line = line.TrimStart('#', ' ');
                if (t.StartsWith("* ") || t.StartsWith("- ")) line = "\u2022  " + t.Substring(2);
                line = line.Replace("**", "");
                sb.Append(line).Append("\r\n");
            }
            var r = sb.ToString().Trim();
            return r.Length == 0 ? "No release notes for this version." : r;
        }
    }
}
