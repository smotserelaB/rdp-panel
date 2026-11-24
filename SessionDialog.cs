using System;
using System.Drawing;
using System.Windows.Forms;

namespace RdpPanel
{
    public class SessionDialog : Form
    {
        readonly SessionConfig sc;
        readonly TextBox txtName = new TextBox();
        readonly TextBox txtNote = new TextBox();
        readonly TextBox txtHost = new TextBox();
        readonly TextBox txtUser = new TextBox();
        readonly TextBox txtPass = new TextBox { UseSystemPasswordChar = true };
        readonly TextBox txtLink = new TextBox();
        readonly TextBox txtAdmin = new TextBox();
        readonly TextBox txtAdminPass = new TextBox { UseSystemPasswordChar = true };
        readonly TextBox txtMac = new TextBox();
        readonly NumericUpDown numW = new NumericUpDown { Minimum = 200, Maximum = 4096 };
        readonly NumericUpDown numH = new NumericUpDown { Minimum = 200, Maximum = 4096 };
        readonly CheckBox chkAuto = new CheckBox { Text = "Connect automatically at startup", AutoSize = true };
        readonly NumericUpDown numCpuWarn = new NumericUpDown { Minimum = 0, Maximum = 100 };
        readonly NumericUpDown numRamWarn = new NumericUpDown { Minimum = 0, Maximum = 100 };
        readonly NumericUpDown numFrozen = new NumericUpDown { Minimum = 0, Maximum = 240 };
        readonly CheckBox chkFrozenRe = new CheckBox { Text = "Reconnect by itself when frozen", AutoSize = true };
        readonly CheckBox chkMon = new CheckBox { Text = "Show on the Monitoring page and in the performance overview", AutoSize = true };
        readonly Button btnColor;
        string colorHex;

        public SessionDialog(SessionConfig sc, bool allowDelete)
        {
            this.sc = sc;
            UI.Init(this, "Session settings", 520, 630);

            int y = 16;
            Row("Name", txtName, ref y, 310);
            Row("Label (short, optional)", txtNote, ref y, 310);
            Row("Host (IP or IP:port)", txtHost, ref y, 310);
            Row("Username", txtUser, ref y, 310);
            Row("Password", txtPass, ref y, 310);
            Row("Administrator (CPU/RAM, restart)", txtAdmin, ref y, 310);
            Row("Administrator password", txtAdminPass, ref y, 310);
            UI.Lbl(this, "Optional: only for another PC. The account must be an administrator on that PC.", 190, y - 8, 320, true).Font = Theme.UI(8.25f);
            y += 10;
            Row("Roblox/Bee Swarm link", txtLink, ref y, 310);
            UI.Lbl(this, "Opened on the other PC when a new session starts.", 190, y - 8, 320, true).Font = Theme.UI(8.25f);
            y += 10;
            Row("MAC address (Wake-on-LAN)", txtMac, ref y, 210);
            var find = UI.Btn(this, "Search", 408, y - 38, 92, false);
            find.Click += (s, e) =>
            {
                var h = new SessionConfig { Host = txtHost.Text }.GetHost();
                var mac = string.IsNullOrWhiteSpace(h) ? "" : NetTools.GetMac(h);
                if (mac == "") MessageBox.Show(this, "Not found. The PC must be on and on the same network.", "MAC address", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else txtMac.Text = mac;
            };

            UI.Lbl(this, "Color", 16, y + 4, 168, true);
            btnColor = UI.Btn(this, "Choose color", 190, y, 120, false);
            var clear = UI.Btn(this, "None", 318, y, 70, false);
            btnColor.Click += (s, e) =>
            {
                using (var cd = new ColorDialog { FullOpen = true })
                    if (cd.ShowDialog(this) == DialogResult.OK) { colorHex = ColorTranslator.ToHtml(cd.Color); ShowColor(); }
            };
            clear.Click += (s, e) => { colorHex = ""; ShowColor(); };
            y += 38;

            Row("Width (px)", numW, ref y, 90);
            Row("Height (px)", numH, ref y, 90);
            chkAuto.Location = new Point(190, y + 2);
            chkAuto.ForeColor = Theme.Fg;
            Controls.Add(chkAuto);
            y += 40;

            UI.Lbl(this, "Alerts and monitoring of this session", 16, y, 400, false).Font = Theme.UI(10f, FontStyle.Bold);
            y += 30;
            Row("Warn at CPU from (%)", numCpuWarn, ref y, 90);
            UI.Lbl(this, "0 = the general value (Settings)", 290, y - 34, 220, true).Font = Theme.UI(8.25f);
            Row("Warn at RAM from (%)", numRamWarn, ref y, 90);
            UI.Lbl(this, "0 = the general value (Settings)", 290, y - 34, 220, true).Font = Theme.UI(8.25f);
            Row("Frozen after (minutes)", numFrozen, ref y, 90);
            UI.Lbl(this, "0 = off (needs the FPS display)", 290, y - 34, 220, true).Font = Theme.UI(8.25f);
            y += 8;
            chkFrozenRe.Location = new Point(190, y);
            chkFrozenRe.ForeColor = Theme.Fg;
            Controls.Add(chkFrozenRe);
            y += 26;
            var frozenHint = UI.Lbl(this, "An idle desktop also shows 0 FPS, so use this only for sessions where something always moves (a game).", 190, y, 310, true);
            frozenHint.Height = 34;
            frozenHint.Font = Theme.UI(8.25f);
            y += 40;
            chkMon.Location = new Point(190, y);
            chkMon.ForeColor = Theme.Fg;
            Controls.Add(chkMon);
            y += 34;
            int by = y + 6;   // y of the buttons

            txtName.Text = sc.Name;
            txtNote.Text = sc.Note;
            txtHost.Text = sc.Host;
            txtUser.Text = sc.User;
            txtPass.Text = sc.Password;
            txtLink.Text = sc.LaunchLink;
            txtAdmin.Text = sc.AdminUser;
            txtAdminPass.Text = sc.AdminPassword;
            txtMac.Text = sc.Mac;
            numW.Value = sc.Width;
            numH.Value = sc.Height;
            chkAuto.Checked = sc.AutoConnect;
            numCpuWarn.Value = Math.Max(0, Math.Min(100, sc.CpuWarn));
            numRamWarn.Value = Math.Max(0, Math.Min(100, sc.RamWarn));
            numFrozen.Value = Math.Max(0, Math.Min(240, sc.FrozenMinutes));
            chkFrozenRe.Checked = sc.FrozenReconnect;
            chkMon.Checked = !sc.HideInMonitoring;
            colorHex = sc.ColorHex;
            ShowColor();

            var ok = UI.Btn(this, "Save", 300, by, 100, true);
            var cancel = UI.Btn(this, "Cancel", 408, by, 96, false);
            cancel.DialogResult = DialogResult.Cancel;
            ok.Click += (s, e) => { Apply(); DialogResult = DialogResult.OK; };
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(ClientSize.Width, by + 52);

            if (allowDelete)
            {
                var del = UI.Btn(this, "Remove", 16, by, 100, false);
                del.ForeColor = Theme.Bad;
                del.Click += (s, e) => { DialogResult = DialogResult.Abort; };
            }
        }

        void ShowColor()
        {
            Color c;
            if (Gfx.TryColor(colorHex, out c)) { btnColor.BackColor = c; btnColor.ForeColor = Color.White; }
            else { btnColor.BackColor = Theme.Raised; btnColor.ForeColor = Theme.Fg; }
        }

        void Row(string label, Control c, ref int y, int width)
        {
            UI.Lbl(this, label, 16, y + 3, 168, true);
            UI.Put(this, c, 190, y, width);
            y += 38;
        }

        void Apply()
        {
            sc.Name = txtName.Text.Trim();
            sc.Note = txtNote.Text.Trim();
            sc.Host = txtHost.Text.Trim();
            sc.User = txtUser.Text.Trim();
            sc.Password = txtPass.Text;
            sc.LaunchLink = txtLink.Text.Trim();
            sc.AdminUser = txtAdmin.Text.Trim();
            sc.AdminPassword = txtAdminPass.Text;
            sc.Mac = txtMac.Text.Trim();
            sc.ColorHex = colorHex ?? "";
            sc.Width = (int)numW.Value;
            sc.Height = (int)numH.Value;
            sc.AutoConnect = chkAuto.Checked;
            sc.CpuWarn = (int)numCpuWarn.Value;
            sc.RamWarn = (int)numRamWarn.Value;
            sc.FrozenMinutes = (int)numFrozen.Value;
            sc.FrozenReconnect = chkFrozenRe.Checked;
            sc.HideInMonitoring = !chkMon.Checked;
        }
    }
}
