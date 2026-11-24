using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RdpPanel
{
    // what the panel is doing right now with every session
    class ActivityForm : Form
    {
        readonly MainForm main;
        readonly ListView lv = UI.NewList();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };

        public ActivityForm(MainForm main)
        {
            this.main = main;
            UI.Init(this, "RDP Panel – activity", 860, 420);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterParent;

            lv.Dock = DockStyle.Fill;
            lv.Columns.Add("Session", 200);
            lv.Columns.Add("What it does now", 170);
            lv.Columns.Add("Details", 460);
            Controls.Add(lv);

            Resize += (s, e) => { if (lv.Columns.Count == 3) lv.Columns[2].Width = Math.Max(120, lv.ClientSize.Width - 390); };
            timer.Tick += (s, e) => Fill();
            timer.Start();
            FormClosed += (s, e) => timer.Dispose();
            Fill();
        }

        void Fill()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var t in main.Tiles.OrderBy(x => main.IsHiddenByProfile(x) ? 1 : 0))
            {
                bool hid = main.IsHiddenByProfile(t);
                var it = new ListViewItem((t.Config.Pinned ? "★ " : "") + t.Config.Name + (hid ? "  (hidden by profile)" : "")) { UseItemStyleForSubItems = false };
                var st = it.SubItems.Add(t.ActivityState);
                st.ForeColor = t.Online ? Theme.Good : t.StatusLabel == "Dropped" ? Theme.Warn : Theme.FgMuted;
                it.SubItems.Add(t.ActivityDetail);
                if (hid) it.ForeColor = Theme.FgMuted;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
        }
    }
}
