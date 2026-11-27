using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RdpPanel
{
    // the page "Discord": what is sent, to whom, how it looks, and tests
    class DiscordPage : Panel
    {
        readonly MainForm main;
        readonly Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
        readonly Label lblSaved = new Label();

        readonly CheckBox chkDrop = new CheckBox { Text = "Drops (a session lost its connection), with a picture", AutoSize = true };
        readonly CheckBox chkProblem = new CheckBox { Text = "Problems: a PC does not respond, a session stopped reconnecting", AutoSize = true };
        readonly CheckBox chkLoad = new CheckBox { Text = "High CPU or memory", AutoSize = true };
        readonly CheckBox chkFrozen = new CheckBox { Text = "A frozen session", AutoSize = true };

        readonly TextBox txtCrit = new TextBox { UseSystemPasswordChar = true };
        readonly TextBox txtMention = new TextBox();
        readonly NumBox numRepeat = new NumBox { Minimum = 0, Maximum = 1440 };
        readonly NumBox numBundle = new NumBox { Minimum = 0, Maximum = 600 };

        readonly CheckBox chkReport = new CheckBox { Text = "Send the daily report", AutoSize = true };
        readonly DateTimePicker dtpReport = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
        readonly CheckBox[] days = new CheckBox[7];   // index = DayOfWeek
        readonly CheckBox chkWeekly = new CheckBox { Text = "Send a weekly report", AutoSize = true };
        readonly ComboBox cmbWeekDay = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly DateTimePicker dtpWeek = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };

        readonly TextBox txtReportHook = new TextBox { UseSystemPasswordChar = true };
        readonly TextBox txtCmdChan = new TextBox();
        readonly TextBox txtReportChan = new TextBox();

        readonly TextBox txtTplAlert = new TextBox { Multiline = true };
        readonly TextBox txtTplDrop = new TextBox { Multiline = true };
        readonly TextBox txtTplRem = new TextBox { Multiline = true };
        readonly TextBox txtTplBundle = new TextBox { Multiline = true };
        readonly Label lblPreview = new Label();

        readonly CheckBox chkTestCrit = new CheckBox { Text = "Send the test to the second (critical) webhook", AutoSize = true };
        readonly Label lblTest = new Label();

        public DiscordPage(MainForm main)
        {
            this.main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;

            var bar = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Theme.Bg };
            bar.Controls.Add(new Label { Text = "💬  Discord messages", Font = Theme.UI(13f, FontStyle.Bold), ForeColor = Theme.Fg, AutoSize = true, Location = new Point(16, 14), BackColor = Color.Transparent });
            var save = UI.Btn(bar, "Save", 300, 12, 110, true);
            save.Click += (s, e) => SaveAll();
            lblSaved.AutoSize = true;
            lblSaved.Location = new Point(424, 20);
            lblSaved.ForeColor = Theme.Good;
            lblSaved.BackColor = Color.Transparent;
            bar.Controls.Add(lblSaved);

            int y = 6;
            const int X = 16;
            Action<string> head = t => { UI.Lbl(scroll, t, X, y, 700, false).Font = Theme.UI(10.5f, FontStyle.Bold); y += 30; };
            Action<string, int> note = (t, h) => { var l = UI.Lbl(scroll, t, X, y, 740, true); l.Height = h; y += h + 6; };
            Action<CheckBox> chk = c => { c.Location = new Point(X, y); c.ForeColor = Theme.Fg; scroll.Controls.Add(c); y += 28; };

            // ---- what is sent
            head("What is sent");
            note("Choose which kinds of messages go to Discord. The pause, quiet hours and the limit per hour (Settings) still apply to all of them.", 36);
            chk(chkDrop); chk(chkProblem); chk(chkLoad); chk(chkFrozen);
            y += 14;

            // ---- critical
            head("A second webhook for important messages");
            note("Drops, PCs that do not respond and frozen sessions are also sent to this webhook, with a mention if you want one. Use it for a channel that you do notice (a phone alert).", 36);
            UI.Lbl(scroll, "Webhook link", X, y + 4, 140, false);
            UI.Put(scroll, txtCrit, X + 150, y, 440);
            var showCrit = UI.Btn(scroll, "Show", X + 600, y - 2, 80, false);
            showCrit.Click += (s, e) => { txtCrit.UseSystemPasswordChar = !txtCrit.UseSystemPasswordChar; showCrit.Text = txtCrit.UseSystemPasswordChar ? "Show" : "Hide"; };
            y += 38;
            UI.Lbl(scroll, "Mention", X, y + 4, 140, false);
            UI.Put(scroll, txtMention, X + 150, y, 440);
            y += 34;
            note("Examples: role:123456789 (a role), user:123456789 (a person), @here, @everyone. Separate several with a space. In Discord, switch on developer mode and right-click a role or person to copy the ID.", 36);
            y += 8;

            // ---- reminder and bundling
            head("Reminder and bundling");
            UI.Lbl(scroll, "Remind me once when a problem lasts (minutes, 0 = off)", X, y + 4, 420, false);
            UI.Put(scroll, numRepeat, X + 430, y, 90);
            y += 38;
            UI.Lbl(scroll, "Bundle messages that come within (seconds, 0 = off)", X, y + 4, 420, false);
            UI.Put(scroll, numBundle, X + 430, y, 90);
            y += 34;
            note("When many sessions fail at the same time (for example when a router stops), you get one message with the list instead of one for every session.", 36);
            y += 8;

            // ---- reports
            head("Reports");
            chk(chkReport);
            UI.Lbl(scroll, "At", X + 24, y + 4, 40, false);
            UI.Put(scroll, dtpReport, X + 66, y, 80);
            y += 38;
            UI.Lbl(scroll, "On these days", X + 24, y + 2, 120, false);
            string[] names = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            int[] order = { 1, 2, 3, 4, 5, 6, 0 };
            for (int i = 0; i < 7; i++)
            {
                int d = order[i];
                days[d] = new CheckBox { Text = names[d], AutoSize = true, ForeColor = Theme.Fg, Location = new Point(X + 150 + i * 62, y) };
                scroll.Controls.Add(days[d]);
            }
            y += 36;
            chk(chkWeekly);
            UI.Lbl(scroll, "Every", X + 24, y + 4, 50, false);
            foreach (var n in new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" }) cmbWeekDay.Items.Add(n);
            UI.Put(scroll, cmbWeekDay, X + 78, y, 140);
            UI.Lbl(scroll, "at", X + 230, y + 4, 30, false);
            UI.Put(scroll, dtpWeek, X + 262, y, 80);
            y += 40;
            note("The weekly report covers the last 7 days and compares them with the 7 days before.", 22);
            y += 8;

            // ---- channels
            head("Where the reports and commands go");
            UI.Lbl(scroll, "Webhook for the reports", X, y + 4, 200, false);
            UI.Put(scroll, txtReportHook, X + 210, y, 380);
            y += 38;
            note("Empty = the normal webhook from Settings. For the bot: the channel of the alerts is set in Settings (Channel ID). Here you can add other channels. Empty = the alert channel.", 36);
            UI.Lbl(scroll, "Bot: channel for commands", X, y + 4, 200, false);
            UI.Put(scroll, txtCmdChan, X + 210, y, 220);
            y += 36;
            UI.Lbl(scroll, "Bot: channel for the reports", X, y + 4, 200, false);
            UI.Put(scroll, txtReportChan, X + 210, y, 220);
            y += 36;
            note("A session can have its own webhook and its own bot channel too (in its settings). Commands and buttons work in the alert channel, the command channel and the channels of the sessions.", 36);
            y += 8;

            // ---- texts
            head("The text of the messages");
            note("Change how a message looks. You can use: {session} {host} {message} {reason} {time} {date} {minutes} {count} {list}", 22);
            foreach (var t in new[] { new Tuple<string, TextBox>("Warning", txtTplAlert), new Tuple<string, TextBox>("Drop", txtTplDrop), new Tuple<string, TextBox>("Reminder", txtTplRem), new Tuple<string, TextBox>("Bundle", txtTplBundle) })
            {
                UI.Lbl(scroll, t.Item1, X, y + 6, 110, false);
                t.Item2.Height = 54;
                UI.Put(scroll, t.Item2, X + 120, y, 560);
                t.Item2.TextChanged += (s, e) => UpdatePreview();
                y += 66;
            }
            var reset = UI.Btn(scroll, "Back to the standard texts", X + 120, y, 240, false);
            reset.Click += (s, e) => ResetTexts();
            y += 44;
            UI.Lbl(scroll, "Preview", X, y, 110, false);
            lblPreview.Location = new Point(X + 120, y);
            lblPreview.Size = new Size(580, 150);
            lblPreview.ForeColor = Theme.FgMuted;
            lblPreview.BackColor = Color.Transparent;
            scroll.Controls.Add(lblPreview);
            y += 160;

            // ---- tests
            head("Test");
            note("Sends a test message made with the texts above (pressing a test button also saves the page).", 22);
            chk(chkTestCrit);
            int bx = X;
            foreach (var t in new[] { new[] { "alert", "Warning" }, new[] { "load", "CPU/RAM" }, new[] { "frozen", "Frozen" }, new[] { "drop", "Drop" }, new[] { "reminder", "Reminder" }, new[] { "bundle", "Bundle" } })
            {
                string kind = t[0];
                var b = UI.Btn(scroll, "Test: " + t[1], bx, y, 112, false);
                b.Click += async (s, e) => await RunTest(kind);
                bx += 118;
            }
            y += 44;
            lblTest.Location = new Point(X, y);
            lblTest.Size = new Size(700, 40);
            lblTest.BackColor = Color.Transparent;
            lblTest.ForeColor = Theme.FgMuted;
            scroll.Controls.Add(lblTest);
            y += 44;
            var last = UI.Btn(scroll, "Last messages…", X, y, 200, false);
            last.Click += (s, e) => { using (var f = new DiscordMessagesForm()) f.ShowDialog(main); };
            y += 60;

            scroll.AutoScrollMinSize = new Size(0, y + 20);
            Controls.Add(scroll);
            Controls.Add(bar);
            HelpMarks.Add(scroll);
            LoadAll();
        }

        static string Show(string s) { return (s ?? "").Replace("\n", "\r\n"); }
        static string Store(string s) { return (s ?? "").Replace("\r\n", "\n"); }

        public void LoadAll()
        {
            var c = main.Cfg;
            chkDrop.Checked = c.DiscordOnDrop;
            chkProblem.Checked = c.DcProblem;
            chkLoad.Checked = c.DcLoad;
            chkFrozen.Checked = c.DcFrozen;
            txtCrit.Text = c.CritWebhookUrl;
            txtMention.Text = c.CritMention;
            numRepeat.Value = c.RepeatMinutes;
            numBundle.Value = c.BundleSeconds;
            chkReport.Checked = c.ReportEnabled;
            dtpReport.Value = DateTime.Today.Add(ParseTime(c.ReportTime, 8));
            for (int d = 0; d < 7; d++) days[d].Checked = (c.ReportDays & (1 << d)) != 0;
            chkWeekly.Checked = c.WeeklyOn;
            cmbWeekDay.SelectedIndex = Math.Max(0, Math.Min(6, c.WeeklyDay));
            dtpWeek.Value = DateTime.Today.Add(ParseTime(c.WeeklyTime, 8));
            txtReportHook.Text = c.ReportWebhookUrl;
            txtCmdChan.Text = c.BotCmdChannelId;
            txtReportChan.Text = c.BotReportChannelId;
            txtTplAlert.Text = Show(c.TplAlert);
            txtTplDrop.Text = Show(c.TplDrop);
            txtTplRem.Text = Show(c.TplReminder);
            txtTplBundle.Text = Show(c.TplBundle);
            UpdatePreview();
        }

        static TimeSpan ParseTime(string s, int defaultHour)
        {
            TimeSpan t;
            return TimeSpan.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out t) ? t : new TimeSpan(defaultHour, 0, 0);
        }

        void SaveAll()
        {
            var c = main.Cfg;
            c.DiscordOnDrop = chkDrop.Checked;
            c.DcProblem = chkProblem.Checked;
            c.DcLoad = chkLoad.Checked;
            c.DcFrozen = chkFrozen.Checked;
            c.CritWebhookUrl = txtCrit.Text.Trim();
            c.CritMention = txtMention.Text.Trim();
            c.RepeatMinutes = (int)numRepeat.Value;
            c.BundleSeconds = (int)numBundle.Value;
            c.ReportEnabled = chkReport.Checked;
            c.ReportTime = dtpReport.Value.ToString("HH:mm");
            int mask = 0;
            for (int d = 0; d < 7; d++) if (days[d].Checked) mask |= 1 << d;
            c.ReportDays = mask;
            c.WeeklyOn = chkWeekly.Checked;
            c.WeeklyDay = Math.Max(0, cmbWeekDay.SelectedIndex);
            c.WeeklyTime = dtpWeek.Value.ToString("HH:mm");
            c.ReportWebhookUrl = txtReportHook.Text.Trim();
            c.BotCmdChannelId = txtCmdChan.Text.Trim();
            c.BotReportChannelId = txtReportChan.Text.Trim();
            c.TplAlert = Store(txtTplAlert.Text);
            c.TplDrop = Store(txtTplDrop.Text);
            c.TplReminder = Store(txtTplRem.Text);
            c.TplBundle = Store(txtTplBundle.Text);
            c.Save();
            lblSaved.Text = "Saved ✓  " + DateTime.Now.ToString("HH:mm:ss");
        }

        void ResetTexts()
        {
            var d = new AppConfig();
            txtTplAlert.Text = Show(d.TplAlert);
            txtTplDrop.Text = Show(d.TplDrop);
            txtTplRem.Text = Show(d.TplReminder);
            txtTplBundle.Text = Show(d.TplBundle);
        }

        void UpdatePreview()
        {
            var vars = new Dictionary<string, string>
            {
                { "session", "Farm 3" }, { "host", "192.168.1.30" }, { "message", "The PC does not respond" }, { "reason", "The connection was lost" },
                { "time", DateTime.Now.ToString("HH:mm:ss") }, { "date", DateTime.Now.ToString("dd-MM-yyyy") }, { "minutes", "15" },
                { "count", "3" }, { "list", "• **Farm 1**: no response\n• **Farm 2**: no response\n• **Farm 3**: dropped" }
            };
            Func<TextBox, string> r = t => DiscordHub.Render(Store(t.Text), vars).Replace("**", "");
            lblPreview.Text = r(txtTplAlert) + "\r\n\r\n" + r(txtTplDrop).Replace("\n", "\r\n") + "\r\n\r\n" + r(txtTplRem) + "\r\n\r\n" + r(txtTplBundle).Replace("\n", "\r\n");
        }

        async System.Threading.Tasks.Task RunTest(string kind)
        {
            SaveAll();
            lblTest.ForeColor = Theme.FgMuted;
            lblTest.Text = "Sending…";
            string err = await main.Hub.SendTest(kind, chkTestCrit.Checked);
            lblTest.ForeColor = err == null ? Theme.Good : Theme.Bad;
            lblTest.Text = err == null ? "Sent ✓  Look in your Discord channel." : "Failed: " + err;
        }
    }

    // the list of the last messages that were sent, with the result
    class DiscordMessagesForm : Form
    {
        public DiscordMessagesForm()
        {
            UI.Init(this, "Last Discord messages", 900, 480);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            ShowInTaskbar = true;

            var lv = UI.NewList();
            lv.Dock = DockStyle.Fill;
            lv.Columns.Add("Time", 130);
            lv.Columns.Add("Kind", 120);
            lv.Columns.Add("Session", 130);
            lv.Columns.Add("To", 130);
            lv.Columns.Add("Result", 90);
            lv.Columns.Add("Message", 280);
            Controls.Add(lv);
            foreach (var m in DiscordMessages.All())
            {
                var it = new ListViewItem(m.Time.ToString("dd-MM HH:mm:ss")) { UseItemStyleForSubItems = false };
                it.SubItems.Add(m.Kind);
                it.SubItems.Add(m.Session);
                it.SubItems.Add(m.Destination);
                var res = it.SubItems.Add(m.Error == null ? "sent" : "failed");
                res.ForeColor = m.Error == null ? Theme.Good : Theme.Bad;
                it.SubItems.Add((m.Error == null ? "" : m.Error + "  –  ") + m.Text.Replace("\n", " "));
                lv.Items.Add(it);
            }
        }
    }
}
