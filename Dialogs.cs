using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace RdpPanel
{
    static class UI
    {
        public static void Init(Form f, string title, int w, int h)
        {
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.BackColor = Theme.Surface;
            f.ForeColor = Theme.Fg;
            f.Font = Theme.UI(9.5f);
            f.ClientSize = new Size(w, h);
            f.HandleCreated += (s, e) => Theme.TitleBar(f.Handle);
        }

        public static Label Lbl(Control p, string text, int x, int y, int w, bool muted)
        {
            var l = new Label
            {
                Text = text,
                Location = new Point(x, y),
                Width = w,
                Height = 20,
                ForeColor = muted ? Theme.FgMuted : Theme.Fg,
                BackColor = Color.Transparent
            };
            p.Controls.Add(l);
            return l;
        }

        public static T Put<T>(Control p, T c, int x, int y, int w) where T : Control
        {
            Theme.StyleInput(c);
            c.Location = new Point(x, y);
            c.Width = w;
            p.Controls.Add(c);
            return c;
        }

        public static Button Btn(Control p, string text, int x, int y, int w, bool accent)
        {
            var b = Theme.MakeButton(text, accent);
            b.AutoSize = false;
            b.Size = new Size(w, 30);
            b.Location = new Point(x, y);
            p.Controls.Add(b);
            return b;
        }

        public static ListView List(Control p, Rectangle r)
        {
            var lv = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Fg,
                Font = Theme.UI(9.5f),
                Bounds = r
            };
            p.Controls.Add(lv);
            return lv;
        }

        public static ListView NewList()
        {
            return new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Fg,
                Font = Theme.UI(9.5f)
            };
        }

        // opens a web link in the default browser (only http/https)
        public static void OpenUrl(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                url = url.Trim();
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        public static string Dur(int secs)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, secs));
            return (int)t.TotalHours + "h " + t.Minutes.ToString("00") + "m";
        }
    }

    // small input dialog with one text field; returns null on Cancel
    public class PromptDialog : Form
    {
        readonly TextBox txt = new TextBox();
        string result;

        PromptDialog(string title, string label, string initial)
        {
            UI.Init(this, title, 520, 150);
            UI.Lbl(this, label, 16, 14, 488, true);
            UI.Put(this, txt, 16, 40, 488);
            txt.Text = initial ?? "";
            var ok = UI.Btn(this, "Save", 300, 100, 100, true);
            var cancel = UI.Btn(this, "Cancel", 408, 100, 96, false);
            cancel.DialogResult = DialogResult.Cancel;
            ok.Click += (s, e) => { result = txt.Text; DialogResult = DialogResult.OK; };
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public static string Ask(IWin32Window owner, string title, string label, string initial)
        {
            using (var d = new PromptDialog(title, label, initial))
                return d.ShowDialog(owner) == DialogResult.OK ? d.result : null;
        }
    }

    // save profile: name + which sessions are visible
    public class ProfileDialog : Form
    {
        readonly TextBox txtName = new TextBox();
        readonly CheckedListBox list = new CheckedListBox();

        public string ProfileName { get { return txtName.Text.Trim(); } }
        public List<string> SelectedSessions { get { return list.CheckedItems.Cast<string>().ToList(); } }

        public ProfileDialog(IEnumerable<string> names, ICollection<string> preChecked, string defaultName)
        {
            UI.Init(this, "Save profile", 460, 420);
            UI.Lbl(this, "Profile name", 16, 14, 420, true);
            UI.Put(this, txtName, 16, 38, 428);
            txtName.Text = defaultName;
            UI.Lbl(this, "Which sessions belong to it? (columns and grouping are saved too)", 16, 74, 428, true);
            UI.Put(this, list, 16, 98, 428);
            list.Height = 250;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.CheckOnClick = true;
            foreach (var n in names) list.Items.Add(n, preChecked.Contains(n));

            var ok = UI.Btn(this, "Save", 236, 372, 100, true);
            var cancel = UI.Btn(this, "Cancel", 344, 372, 100, false);
            cancel.DialogResult = DialogResult.Cancel;
            ok.Click += (s, e) =>
            {
                if (ProfileName.Length == 0) { MessageBox.Show(this, "Give the profile a name."); return; }
                if (list.CheckedItems.Count == 0) { MessageBox.Show(this, "Choose at least one session."); return; }
                DialogResult = DialogResult.OK;
            };
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    // find PCs with an open RDP port on the network
    public class ScanDialog : Form
    {
        public readonly List<NetTools.Found> Selected = new List<NetTools.Found>();
        public string DefaultUser { get { return txtUser.Text.Trim(); } }
        public string DefaultPass { get { return txtPass.Text; } }

        readonly ListView lv;
        readonly Label status;
        readonly Button btnScan;
        readonly TextBox txtUser = new TextBox();
        readonly TextBox txtPass = new TextBox { UseSystemPasswordChar = true };
        readonly HashSet<string> existing;

        public ScanDialog(IEnumerable<string> existingHosts)
        {
            existing = new HashSet<string>(existingHosts, StringComparer.OrdinalIgnoreCase);
            UI.Init(this, "Find PCs on the network", 560, 500);

            btnScan = UI.Btn(this, "Start scan", 16, 14, 130, true);
            status = UI.Lbl(this, "Looks for PCs with Remote Desktop (port 3389) turned on.", 156, 20, 390, true);

            lv = UI.List(this, new Rectangle(16, 54, 528, 250));
            lv.CheckBoxes = true;
            lv.Columns.Add("Address", 140);
            lv.Columns.Add("Name", 270);
            lv.Columns.Add("", 100);

            UI.Lbl(this, "User for the new sessions (optional)", 16, 316, 400, true);
            UI.Put(this, txtUser, 16, 340, 250);
            UI.Lbl(this, "Password", 280, 316, 200, true);
            UI.Put(this, txtPass, 280, 340, 264);

            var add = UI.Btn(this, "Add", 340, 452, 100, true);
            var cancel = UI.Btn(this, "Close", 448, 452, 96, false);
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;

            btnScan.Click += async (s, e) =>
            {
                btnScan.Enabled = false;
                lv.Items.Clear();
                status.Text = "Scanning… 0%";
                var prog = new Progress<int>(p => status.Text = "Scanning… " + p + "%");
                List<NetTools.Found> found;
                try { found = await NetTools.ScanAsync(prog); }
                catch (Exception ex) { status.Text = "Failed: " + ex.Message; btnScan.Enabled = true; return; }

                foreach (var f in found)
                {
                    bool have = existing.Contains(f.Ip);
                    var item = new ListViewItem(f.Ip) { Tag = f, Checked = !have };
                    item.SubItems.Add(f.Name);
                    item.SubItems.Add(have ? "already added" : "");
                    lv.Items.Add(item);
                }
                status.Text = found.Count + " PC(s) found.";
                btnScan.Enabled = true;
            };

            add.Click += (s, e) =>
            {
                foreach (ListViewItem it in lv.CheckedItems) Selected.Add((NetTools.Found)it.Tag);
                if (Selected.Count == 0) { MessageBox.Show(this, "Tick at least one PC."); return; }
                DialogResult = DialogResult.OK;
            };
        }
    }

    // paste users and passwords at once and turn them into sessions
    public class ImportDialog : Form
    {
        public readonly List<SessionConfig> Result = new List<SessionConfig>();
        readonly TextBox txtHost = new TextBox();
        readonly TextBox txtPrefix = new TextBox();
        readonly TextBox txtPaste = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };

        static readonly Regex Both = new Regex(@"(?:user(?:name)?)\s*[:=]\s*(\S+).*?(?:pass(?:word)?)\s*[:=]\s*(\S+)", RegexOptions.IgnoreCase);
        static readonly Regex OnlyUser = new Regex(@"^\s*(?:user(?:name)?)\s*[:=]\s*(\S+)\s*$", RegexOptions.IgnoreCase);
        static readonly Regex OnlyPass = new Regex(@"^\s*(?:pass(?:word)?)\s*[:=]\s*(\S+)\s*$", RegexOptions.IgnoreCase);

        public ImportDialog()
        {
            UI.Init(this, "Import users", 560, 520);
            UI.Lbl(this, "Host of the PC (IP address)", 16, 14, 250, true);
            UI.Put(this, txtHost, 16, 38, 250);
            UI.Lbl(this, "Prefix for the name (e.g. 'PC 1')", 290, 14, 254, true);
            UI.Put(this, txtPrefix, 290, 38, 254);
            UI.Lbl(this, "Paste the users and passwords here (one per line). Recognised: 'name password', 'name:password',", 16, 76, 528, true);
            UI.Lbl(this, "'name;password', 'Username: x' with 'Password: y', or both on one line.", 16, 96, 528, true);
            UI.Put(this, txtPaste, 16, 124, 528);
            txtPaste.Height = 320;

            var add = UI.Btn(this, "Create sessions", 316, 470, 120, true);
            var cancel = UI.Btn(this, "Cancel", 444, 470, 100, false);
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;

            add.Click += (s, e) =>
            {
                string host = txtHost.Text.Trim();
                if (host.Length == 0) { MessageBox.Show(this, "Enter the IP address of the PC first."); return; }
                var pairs = Parse(txtPaste.Text);
                if (pairs.Count == 0)
                {
                    MessageBox.Show(this, "No users recognised. Put 'user password' on each line.\r\n(Passwords with spaces or colons are not recognised.)");
                    return;
                }
                string prefix = txtPrefix.Text.Trim().Length > 0 ? txtPrefix.Text.Trim() : host;
                foreach (var p in pairs)
                    Result.Add(new SessionConfig { Name = prefix + " – " + p.Key, Host = host, User = p.Key, Password = p.Value });
                DialogResult = DialogResult.OK;
            };
        }

        static List<KeyValuePair<string, string>> Parse(string text)
        {
            var list = new List<KeyValuePair<string, string>>();
            string pendingUser = null;
            foreach (var raw in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                var m = Both.Match(line);
                if (m.Success) { list.Add(Pair(m.Groups[1].Value, m.Groups[2].Value)); pendingUser = null; continue; }

                m = OnlyUser.Match(line);
                if (m.Success) { pendingUser = m.Groups[1].Value; continue; }

                m = OnlyPass.Match(line);
                if (m.Success && pendingUser != null) { list.Add(Pair(pendingUser, m.Groups[1].Value)); pendingUser = null; continue; }

                var parts = Regex.Split(line, @"[\s;,:|]+").Where(x => x.Length > 0).ToArray();
                if (parts.Length == 2) list.Add(Pair(parts[0], parts[1]));
            }
            return list;
        }

        static KeyValuePair<string, string> Pair(string user, string pass)
        {
            return new KeyValuePair<string, string>(user.Trim('"', '\''), pass.Trim('"', '\''));
        }
    }
}
