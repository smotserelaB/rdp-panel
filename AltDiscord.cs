using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RdpPanel
{
    // asks where the alt accounts go (which webhook), which alts and what to include
    class AltDiscordDialog : Form
    {
        class Target
        {
            public string Name = "";
            public string Url = "";
            public DiscordTarget Saved;
            public override string ToString() { return Name; }
        }

        readonly MainForm main;
        readonly ComboBox cmbTarget = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckedListBox clb = new CheckedListBox { CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false };
        readonly CheckBox chkPics = new CheckBox { Text = "Include the hive pictures", AutoSize = true, Checked = true };
        readonly CheckBox chkOverview = new CheckBox { Text = "Add an overview first and a total at the end", AutoSize = true, Checked = true };
        readonly List<AltAccount> alts;

        public string Url = "";
        public List<AltAccount> Chosen = new List<AltAccount>();
        public bool WithPictures;
        public bool WithOverview;

        public AltDiscordDialog(MainForm main)
        {
            this.main = main;
            alts = main.Cfg.Alts.ToList();
            UI.Init(this, "Alt accounts to Discord", 600, 590);

            UI.Lbl(this, "1. Where should it go?", 16, 14, 400, false).Font = Theme.UI(10f, FontStyle.Bold);
            UI.Put(this, cmbTarget, 16, 46, 340);
            var add = UI.Btn(this, "Add…", 368, 43, 100, false);
            add.Click += (s, e) => AddTarget();
            var rem = UI.Btn(this, "Remove", 476, 43, 100, false);
            rem.Click += (s, e) => RemoveTarget();
            UI.Lbl(this, "Add the webhook link of any channel (Discord: channel settings, Integrations, Webhooks). The links are saved on this PC, encrypted.", 16, 84, 560, true).Height = 38;
            FillTargets();

            UI.Lbl(this, "2. Which alt accounts?", 16, 134, 400, false).Font = Theme.UI(10f, FontStyle.Bold);
            clb.BackColor = Theme.Raised;
            clb.ForeColor = Theme.Fg;
            clb.Font = Theme.UI(9.5f);
            clb.SetBounds(16, 164, 440, 180);
            foreach (var a in alts) clb.Items.Add(a.Name + (string.IsNullOrWhiteSpace(a.Kind) ? "" : "   (" + a.Kind + ")"), true);
            Controls.Add(clb);
            var all = UI.Btn(this, "All", 468, 164, 108, false);
            all.Click += (s, e) => { for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, true); };
            var none = UI.Btn(this, "None", 468, 200, 108, false);
            none.Click += (s, e) => { for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, false); };

            UI.Lbl(this, "3. What to send?", 16, 360, 400, false).Font = Theme.UI(10f, FontStyle.Bold);
            chkPics.Location = new Point(16, 392);
            chkPics.ForeColor = Theme.Fg;
            Controls.Add(chkPics);
            chkOverview.Location = new Point(16, 420);
            chkOverview.ForeColor = Theme.Fg;
            Controls.Add(chkOverview);
            UI.Lbl(this, "Every alt becomes one card in the chat with its numbers, goals with a progress bar, note and picture.", 16, 456, 560, true).Height = 38;

            var send = UI.Btn(this, "Send", 360, 520, 100, true);
            var cancel = UI.Btn(this, "Cancel", 468, 520, 108, false);
            send.Click += (s, e) => Done();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            AcceptButton = send;
            CancelButton = cancel;
        }

        // the webhook of Settings and the saved ones
        void FillTargets()
        {
            string keep = main.Cfg.AltTargetName ?? "";
            cmbTarget.Items.Clear();
            if (Discord.IsValid(main.Cfg.WebhookUrl))
                cmbTarget.Items.Add(new Target { Name = "Panel webhook (from Settings)", Url = main.Cfg.WebhookUrl });
            foreach (var t in main.Cfg.DiscordTargets)
                cmbTarget.Items.Add(new Target { Name = t.Name, Url = t.Url, Saved = t });
            int idx = 0;
            for (int i = 0; i < cmbTarget.Items.Count; i++)
                if (((Target)cmbTarget.Items[i]).Name == keep) idx = i;
            if (cmbTarget.Items.Count > 0) cmbTarget.SelectedIndex = idx;
        }

        void AddTarget()
        {
            string name = PromptDialog.Ask(this, "New destination", "A name for this destination (for example: Alts channel)", "");
            if (string.IsNullOrWhiteSpace(name)) return;
            string url = PromptDialog.Ask(this, "New destination", "The webhook link of that channel", "");
            if (url == null) return;
            url = url.Trim();
            if (!Discord.IsValid(url))
            {
                MessageBox.Show(this, "That is not a Discord webhook link. It starts with https://discord.com/api/webhooks/", "Alt accounts to Discord", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var t = new DiscordTarget { Name = name.Trim() };
            t.Url = url;
            main.Cfg.DiscordTargets.Add(t);
            main.Cfg.AltTargetName = t.Name;
            main.Cfg.Save();
            FillTargets();
        }

        void RemoveTarget()
        {
            var t = cmbTarget.SelectedItem as Target;
            if (t == null || t.Saved == null)
            {
                MessageBox.Show(this, "Only destinations you added here can be removed. The panel webhook is set in Settings.", "Alt accounts to Discord", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "Remove the destination '" + t.Name + "'?", "Alt accounts to Discord", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            main.Cfg.DiscordTargets.Remove(t.Saved);
            main.Cfg.Save();
            FillTargets();
        }

        void Done()
        {
            var t = cmbTarget.SelectedItem as Target;
            if (t == null)
            {
                MessageBox.Show(this, "Choose where it should go, or add a destination with Add.", "Alt accounts to Discord", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Chosen.Clear();
            for (int i = 0; i < clb.Items.Count; i++) if (clb.GetItemChecked(i)) Chosen.Add(alts[i]);
            if (Chosen.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one alt account.", "Alt accounts to Discord", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Url = t.Url;
            WithPictures = chkPics.Checked;
            WithOverview = chkOverview.Checked;
            main.Cfg.AltTargetName = t.Name;
            main.Cfg.Save();
            DialogResult = DialogResult.OK;
        }
    }
}
