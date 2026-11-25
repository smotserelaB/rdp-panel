using System;
using System.Drawing;
using System.Windows.Forms;

namespace RdpPanel
{
    // "switch to this profile by itself" between two times, on chosen days
    class ScheduleDialog : Form
    {
        readonly Profile p;
        readonly CheckBox chkOn = new CheckBox { Text = "Switch to this profile by itself", AutoSize = true };
        readonly DateTimePicker dtpFrom = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
        readonly DateTimePicker dtpTo = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
        readonly CheckBox[] days = new CheckBox[7];   // index = DayOfWeek (0 = Sunday)

        public ScheduleDialog(Profile p)
        {
            this.p = p;
            UI.Init(this, "Schedule of '" + p.Name + "'", 460, 330);

            chkOn.Location = new Point(20, 18);
            chkOn.ForeColor = Theme.Fg;
            chkOn.Checked = p.ScheduleOn;
            Controls.Add(chkOn);

            UI.Lbl(this, "From", 20, 60, 50, false);
            UI.Put(this, dtpFrom, 74, 56, 80);
            UI.Lbl(this, "until", 170, 60, 50, false);
            UI.Put(this, dtpTo, 220, 56, 80);
            TimeSpan f, t;
            if (!TimeSpan.TryParse(p.ScheduleFrom, out f)) f = new TimeSpan(8, 0, 0);
            if (!TimeSpan.TryParse(p.ScheduleTo, out t)) t = new TimeSpan(18, 0, 0);
            dtpFrom.Value = DateTime.Today.Add(f);
            dtpTo.Value = DateTime.Today.Add(t);

            UI.Lbl(this, "On these days", 20, 104, 200, false);
            string[] names = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            int[] order = { 1, 2, 3, 4, 5, 6, 0 };   // show Monday first
            for (int i = 0; i < 7; i++)
            {
                int d = order[i];
                days[d] = new CheckBox { Text = names[d], AutoSize = true, ForeColor = Theme.Fg, Checked = (p.ScheduleDays & (1 << d)) != 0, Location = new Point(20 + i * 58, 134) };
                Controls.Add(days[d]);
            }

            UI.Lbl(this, "At the start time the panel switches to this profile once. If you choose another profile yourself later, it stays until the next switch. An end time past midnight (for example 22:00 until 06:00) works too.", 20, 176, 420, true).Height = 60;

            var ok = UI.Btn(this, "Save", 240, 262, 100, true);
            var cancel = UI.Btn(this, "Cancel", 348, 262, 92, false);
            ok.Click += (s, e) =>
            {
                p.ScheduleOn = chkOn.Checked;
                p.ScheduleFrom = dtpFrom.Value.ToString("HH:mm");
                p.ScheduleTo = dtpTo.Value.ToString("HH:mm");
                int mask = 0;
                for (int d = 0; d < 7; d++) if (days[d].Checked) mask |= 1 << d;
                p.ScheduleDays = mask;
                DialogResult = DialogResult.OK;
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
