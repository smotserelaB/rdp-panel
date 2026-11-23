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
            colorHex = sc.ColorHex;
            ShowColor();

            var ok = UI.Btn(this, "Save", 300, 588, 100, true);
            var cancel = UI.Btn(this, "Cancel", 408, 588, 96, false);
            cancel.DialogResult = DialogResult.Cancel;
            ok.Click += (s, e) => { Apply(); DialogResult = DialogResult.OK; };
            AcceptButton = ok;
            CancelButton = cancel;

            if (allowDelete)
            {
                var del = UI.Btn(this, "Remove", 16, 588, 100, false);
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
        }
    }
}
