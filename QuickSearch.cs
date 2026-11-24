using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RdpPanel
{
    // Ctrl+K: type a few letters, pick with the arrow keys, Enter opens
    class QuickSearchForm : Form
    {
        readonly List<Tuple<string, Action>> entries;
        readonly TextBox txt = new TextBox();
        readonly ListBox list = new ListBox();

        public QuickSearchForm(List<Tuple<string, Action>> entries)
        {
            this.entries = entries;
            UI.Init(this, "Search", 560, 360);
            ShowInTaskbar = false;

            Theme.StyleInput(txt);
            txt.Font = Theme.UI(11f);
            txt.SetBounds(16, 16, 528, 30);
            Controls.Add(txt);

            list.SetBounds(16, 56, 528, 288);
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Surface;
            list.ForeColor = Theme.Fg;
            list.Font = Theme.UI(10.5f);
            list.IntegralHeight = false;
            Controls.Add(list);

            txt.TextChanged += (s, e) => Fill();
            txt.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down) { Move(1); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up) { Move(-1); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { Run(); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            list.DoubleClick += (s, e) => Run();
            Shown += (s, e) => txt.Focus();
            Fill();
        }

        void Move(int d)
        {
            if (list.Items.Count == 0) return;
            list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, list.SelectedIndex + d));
        }

        void Fill()
        {
            var words = txt.Text.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var en in entries)
            {
                string t = en.Item1.ToLowerInvariant();
                if (words.All(w => t.Contains(w))) list.Items.Add(en);
            }
            list.DisplayMember = "Item1";
            list.EndUpdate();
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }

        void Run()
        {
            var en = list.SelectedItem as Tuple<string, Action>;
            if (en == null) return;
            Close();
            en.Item2();
        }
    }
}
