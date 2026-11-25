using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RdpPanel
{
    // change one setting of several sessions at once: tick the sessions, tick what to change
    class MultiEditDialog : Form
    {
        readonly List<SessionConfig> sessions;
        readonly CheckedListBox list = new CheckedListBox { CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false };
        readonly List<Tuple<CheckBox, Action<SessionConfig>>> changes = new List<Tuple<CheckBox, Action<SessionConfig>>>();
        int y = 232;

        public MultiEditDialog(List<SessionConfig> sessions)
        {
            this.sessions = sessions;
            UI.Init(this, "Edit several sessions", 600, 780);
            AutoScroll = true;

            UI.Lbl(this, "1. Which sessions?", 16, 12, 300, false).Font = Theme.UI(10f, FontStyle.Bold);
            list.BackColor = Theme.Surface;
            list.ForeColor = Theme.Fg;
            list.Font = Theme.UI(9.5f);
            list.SetBounds(16, 40, 400, 150);
            foreach (var sc in sessions) list.Items.Add(sc.Name, true);
            Controls.Add(list);
            var all = UI.Btn(this, "All", 428, 40, 70, false);
            all.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };
            var none = UI.Btn(this, "None", 506, 40, 70, false);
            none.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };

            UI.Lbl(this, "2. What to change? (only the ticked settings are changed)", 16, 200, 560, false).Font = Theme.UI(10f, FontStyle.Bold);

            var color = new TextBox();
            AddRow("Color (#RRGGBB, empty = default)", color, 150, sc => sc.ColorHex = color.Text.Trim());
            var auto = YesNo();
            AddRow("Connect automatically at startup", auto, 150, sc => sc.AutoConnect = auto.SelectedIndex == 0);
            var pin = YesNo();
            AddRow("Pin to the top", pin, 150, sc => sc.Pinned = pin.SelectedIndex == 0);
            var cpu = Num(0, 100);
            AddRow("Warn at CPU from (%, 0 = general)", cpu, 90, sc => sc.CpuWarn = (int)cpu.Value);
            var ram = Num(0, 100);
            AddRow("Warn at RAM from (%, 0 = general)", ram, 90, sc => sc.RamWarn = (int)ram.Value);
            var frozen = Num(0, 240);
            AddRow("Frozen after (minutes, 0 = off)", frozen, 90, sc => sc.FrozenMinutes = (int)frozen.Value);
            var frozenRe = YesNo();
            AddRow("Reconnect by itself when frozen", frozenRe, 150, sc => sc.FrozenReconnect = frozenRe.SelectedIndex == 0);
            var w = Num(640, 7680);
            w.Value = 1366;
            AddRow("Screen width", w, 90, sc => sc.Width = (int)w.Value);
            var h = Num(480, 4320);
            h.Value = 768;
            AddRow("Screen height", h, 90, sc => sc.Height = (int)h.Value);
            var mon = YesNo();
            AddRow("Show on Monitoring and performance overview", mon, 150, sc => sc.HideInMonitoring = mon.SelectedIndex != 0);
            var sum = YesNo();
            AddRow("Show in the Daily summary", sum, 150, sc => sc.HideInDailySummary = sum.SelectedIndex != 0);
            var pc = YesNo();
            AddRow("Show in PC management", pc, 150, sc => sc.HideInPcList = pc.SelectedIndex != 0);

            y += 10;
            var ok = UI.Btn(this, "Apply", 380, y, 100, true);
            var cancel = UI.Btn(this, "Cancel", 488, y, 90, false);
            ok.Click += (s, e) =>
            {
                var chosen = new List<SessionConfig>();
                for (int i = 0; i < list.Items.Count; i++) if (list.GetItemChecked(i)) chosen.Add(this.sessions[i]);
                var todo = changes.Where(c => c.Item1.Checked).ToList();
                if (chosen.Count == 0) { MessageBox.Show(this, "Tick at least one session.", "Edit several sessions", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                if (todo.Count == 0) { MessageBox.Show(this, "Tick at least one setting to change.", "Edit several sessions", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                foreach (var sc in chosen) foreach (var c in todo) c.Item2(sc);
                DialogResult = DialogResult.OK;
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            AcceptButton = ok;
            CancelButton = cancel;
            AutoScrollMinSize = new Size(0, y + 60);
        }

        static ComboBox YesNo()
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            c.Items.Add("Yes");
            c.Items.Add("No");
            c.SelectedIndex = 0;
            return c;
        }

        static NumericUpDown Num(int min, int max)
        {
            return new NumericUpDown { Minimum = min, Maximum = max };
        }

        void AddRow(string label, Control c, int width, Action<SessionConfig> apply)
        {
            var chk = new CheckBox { Text = label, AutoSize = false, Size = new Size(300, 24), Location = new Point(16, y + 2), ForeColor = Theme.Fg };
            Controls.Add(chk);
            UI.Put(this, c, 330, y, width);
            c.Enabled = false;
            chk.CheckedChanged += (s, e) => c.Enabled = chk.Checked;
            changes.Add(Tuple.Create(chk, apply));
            y += 34;
        }
    }
}
