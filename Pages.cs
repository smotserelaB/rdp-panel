using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace RdpPanel
{
    // ---- Monitoring: large charts per session ----
    class MonitoringPage : Panel
    {
        readonly MainForm main;
        readonly FlowLayoutPanel flow = new FlowLayoutPanel();
        readonly List<Tuple<SessionTile, Card, Label>> items = new List<Tuple<SessionTile, Card, Label>>();

        public MonitoringPage(MainForm main)
        {
            this.main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(16);
            flow.Dock = DockStyle.Fill;
            flow.AutoScroll = true;
            flow.BackColor = Theme.Bg;
            Controls.Add(flow);
        }

        public void Rebuild()
        {
            flow.SuspendLayout();
            flow.Controls.Clear();
            items.Clear();
            foreach (var t in main.Tiles)
            {
                var card = new Card(t.Config.Name) { Width = 470, Height = 240, Margin = new Padding(0, 0, 14, 14) };
                var vals = new Label
                {
                    Dock = DockStyle.Bottom,
                    Height = 24,
                    ForeColor = Theme.FgMuted,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.Transparent
                };
                var chart = new Spark { Dock = DockStyle.Fill, Grid = true, MaxPoints = 360, BackColor = Theme.Surface };
                chart.Lines.Add(new Spark.Series { V = t.CpuHist, C = Theme.Accent });
                chart.Lines.Add(new Spark.Series { V = t.RamHist, C = Color.FromArgb(160, 110, 255) });
                card.Body.Controls.Add(chart);
                card.Body.Controls.Add(vals);
                flow.Controls.Add(card);
                items.Add(Tuple.Create(t, card, vals));
            }
            flow.ResumeLayout();
        }

        static string Fmt(double v, string unit)
        {
            return double.IsNaN(v) ? "–" : v.ToString("0") + unit;
        }

        public void RefreshData()
        {
            foreach (var it in items)
            {
                var t = it.Item1;
                it.Item2.Title.Text = t.Config.Name + "   " + t.Config.Host;
                it.Item3.Text = "CPU " + Fmt(t.Cpu, "%") + "   RAM " + Fmt(t.Ram, "%") + "   free " + Fmt(t.FreeMb, " MB")
                    + (main.Cfg.ShowFps ? "   FPS " + Fmt(t.Fps, "") : "")
                    + "     (blue = CPU, purple = RAM)";
                foreach (Control c in it.Item2.Body.Controls) c.Invalidate();
            }
        }
    }

    // ---- Log and daily summary ----
    class LogPage : Panel
    {
        readonly MainForm main;
        readonly DateTimePicker dtp = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 130 };
        readonly ListView lvSum = UI.NewList();
        readonly ListView lvLog = UI.NewList();

        public LogPage(MainForm main)
        {
            this.main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(16);

            lvSum.Columns.Add("Session", 220);
            lvSum.Columns.Add("Connected time", 140);
            lvSum.Columns.Add("Times connected", 130);
            lvSum.Columns.Add("Drops / failed", 130);
            lvSum.Dock = DockStyle.Top;
            lvSum.Height = 200;

            lvLog.Columns.Add("Time", 150);
            lvLog.Columns.Add("Session", 170);
            lvLog.Columns.Add("Event", 120);
            lvLog.Columns.Add("Details", 520);
            lvLog.Dock = DockStyle.Fill;

            var lblLog = new Label { Text = "Events", Dock = DockStyle.Top, Height = 32, ForeColor = Theme.Fg, Font = Theme.UI(10f, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
            var lblSum = new Label { Text = "Summary per session", Dock = DockStyle.Top, Height = 32, ForeColor = Theme.Fg, Font = Theme.UI(10f, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
            var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Bg };
            UI.Lbl(top, "Day:", 0, 10, 40, true);
            dtp.Location = new Point(44, 6);
            Theme.StyleInput(dtp);
            top.Controls.Add(dtp);
            var shots = UI.Btn(top, "Open screenshots", 200, 3, 210, false);
            shots.Click += (s, e) => OpenShots();

            Controls.Add(lvLog);
            Controls.Add(lblLog);
            Controls.Add(lvSum);
            Controls.Add(lblSum);
            Controls.Add(top);

            dtp.ValueChanged += (s, e) => RefreshData();
        }

        static void OpenShots()
        {
            try
            {
                System.IO.Directory.CreateDirectory(LogService.ShotDir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + LogService.ShotDir + "\"");
            }
            catch { }
        }

        public void RefreshData()
        {
            var day = dtp.Value.Date;
            bool today = day == DateTime.Today;
            var running = main.RunningSeconds();
            var entries = LogService.ReadAll().Where(e => e.Time.Date == day).ToList();

            var names = entries.Select(e => e.Session).Where(n => n.Length > 0).Distinct().ToList();
            if (today) foreach (var k in running.Keys) if (!names.Contains(k)) names.Add(k);

            lvSum.BeginUpdate();
            lvSum.Items.Clear();
            foreach (var n in names.OrderBy(x => x))
            {
                int secs = entries.Where(e => e.Session == n && e.Event == "Disconnected").Sum(e => e.Seconds);
                if (today && running.ContainsKey(n)) secs += running[n];
                int times = entries.Count(e => e.Session == n && e.Event == "Connected");
                int drops = entries.Count(e => e.Session == n &&
                    ((e.Event == "Disconnected" && e.Detail != DiscReasons.ByUser && e.Detail != "Reconnect") || e.Event == "Failed"));
                var it = new ListViewItem(n);
                it.SubItems.Add(UI.Dur(secs));
                it.SubItems.Add(times.ToString());
                it.SubItems.Add(drops.ToString());
                lvSum.Items.Add(it);
            }
            lvSum.EndUpdate();

            lvLog.BeginUpdate();
            lvLog.Items.Clear();
            foreach (var e in entries.OrderByDescending(x => x.Time))
            {
                var it = new ListViewItem(e.Time.ToString("dd-MM-yyyy HH:mm:ss"));
                it.SubItems.Add(e.Session);
                it.SubItems.Add(e.Event);
                it.SubItems.Add(e.Detail + (e.Seconds > 0 ? "  (" + UI.Dur(e.Seconds) + ")" : ""));
                lvLog.Items.Add(it);
            }
            lvLog.EndUpdate();
        }
    }

    // ---- PC management ----
    class PcPage : Panel
    {
        readonly MainForm main;
        readonly ListView lv = UI.NewList();

        public PcPage(MainForm main)
        {
            this.main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(16);

            lv.Columns.Add("Session", 170);
            lv.Columns.Add("Host", 160);
            lv.Columns.Add("MAC address", 150);
            lv.Columns.Add("Ping", 80);
            lv.Columns.Add("Status", 120);
            lv.Columns.Add("Label", 180);
            lv.Dock = DockStyle.Fill;

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg };
            int x = 0;
            AddBtn(bar, "Turn on (Wake-on-LAN)", ref x, 190, () => { var t = Sel(); if (t != null) t.DoWake(); });
            AddBtn(bar, "↻ Restart", ref x, 120, () => { var t = Sel(); if (t != null) t.DoPower(true); });
            AddBtn(bar, "Shut down", ref x, 100, () => { var t = Sel(); if (t != null) t.DoPower(false); }).ForeColor = Theme.Bad;
            AddBtn(bar, "Reconnect", ref x, 150, () => { var t = Sel(); if (t != null) t.Reconnect(); });
            AddBtn(bar, "Search network…", ref x, 150, () => main.ScanNetwork());
            AddBtn(bar, "Import users…", ref x, 180, () => main.ImportUsers());

            Controls.Add(lv);
            Controls.Add(bar);
        }

        static Button AddBtn(Control p, string text, ref int x, int w, Action a)
        {
            var b = UI.Btn(p, text, x, 14, w, false);
            b.Click += (s, e) => a();
            x += w + 8;
            return b;
        }

        SessionTile Sel()
        {
            if (lv.SelectedItems.Count > 0) return lv.SelectedItems[0].Tag as SessionTile;
            return main.SelectedTile;
        }

        public void RefreshData()
        {
            var sel = Sel();
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var t in main.Tiles)
            {
                var it = new ListViewItem(t.Config.Name) { Tag = t };
                it.SubItems.Add(t.Config.Host);
                it.SubItems.Add(t.Config.Mac);
                it.SubItems.Add(t.PingMs >= 0 ? t.PingMs + " ms" : "—");
                it.SubItems.Add(t.StatusLabel == "Good" ? "Connected" : t.StatusLabel);
                it.SubItems.Add(t.Config.Note);
                if (t == sel) it.Selected = true;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
        }
    }

    // ---- Profiles ----
    class ProfilesPage : Panel
    {
        readonly MainForm main;
        readonly ListView lv = UI.NewList();
        readonly Label lblActive = new Label();

        public ProfilesPage(MainForm main)
        {
            this.main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(16);

            lblActive.Dock = DockStyle.Top;
            lblActive.Height = 36;
            lblActive.ForeColor = Theme.Fg;
            lblActive.Font = Theme.UI(10.5f, FontStyle.Bold);
            lblActive.TextAlign = ContentAlignment.MiddleLeft;

            lv.Columns.Add("Profile", 220);
            lv.Columns.Add("Sessions", 100);
            lv.Columns.Add("Columns", 100);
            lv.Columns.Add("Group by PC", 140);
            lv.Dock = DockStyle.Fill;

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg };
            int x = 0;
            Add(bar, "Save current view…", ref x, 200, () => main.SaveCurrentAsProfile());
            Add(bar, "Apply", ref x, 110, () => { var p = Sel(); if (p != null) main.ApplyProfile(p); });
            Add(bar, "Show all sessions", ref x, 150, () => main.ApplyProfile(null));
            Add(bar, "Remove", ref x, 110, () => { var p = Sel(); if (p != null) main.DeleteProfile(p); }).ForeColor = Theme.Bad;

            Controls.Add(lv);
            Controls.Add(bar);
            Controls.Add(lblActive);
        }

        static Button Add(Control p, string text, ref int x, int w, Action a)
        {
            var b = UI.Btn(p, text, x, 14, w, false);
            b.Click += (s, e) => a();
            x += w + 8;
            return b;
        }

        Profile Sel()
        {
            return lv.SelectedItems.Count > 0 ? lv.SelectedItems[0].Tag as Profile : null;
        }

        public void RefreshData()
        {
            lblActive.Text = "Active profile: " + (string.IsNullOrEmpty(main.Cfg.ActiveProfile) ? "(all sessions)" : main.Cfg.ActiveProfile);
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var p in main.Cfg.Profiles)
            {
                var it = new ListViewItem(p.Name) { Tag = p };
                it.SubItems.Add(p.Sessions.Count.ToString());
                it.SubItems.Add(p.Columns.ToString());
                it.SubItems.Add(p.GroupByHost ? "yes" : "no");
                lv.Items.Add(it);
            }
            lv.EndUpdate();
        }
    }

    // ---- Settings ----
    class SettingsPage : Panel
    {
        readonly MainForm main;
        readonly NumericUpDown numAfter = new NumericUpDown { Minimum = 10, Maximum = 3600 };
        readonly NumericUpDown numCpu = new NumericUpDown { Minimum = 30, Maximum = 100 };
        readonly NumericUpDown numRam = new NumericUpDown { Minimum = 30, Maximum = 100 };
        readonly NumericUpDown numFree = new NumericUpDown { Minimum = 0, Maximum = 64000, Increment = 100 };
        readonly CheckBox chkSound = new CheckBox { Text = "Sound on a notification", AutoSize = true };
        readonly CheckBox chkLight = new CheckBox { Text = "Light theme (the panel restarts)", AutoSize = true };
        readonly CheckBox chkStartup = new CheckBox { Text = "Start the panel automatically with Windows and connect the sessions", AutoSize = true };
        readonly CheckBox chkMin = new CheckBox { Text = "Start minimised (only when starting with Windows)", AutoSize = true };
        readonly CheckBox chkBg = new CheckBox { Text = "Background mode: the minimise button hides the panel but keeps it working (open it again from the tray icon)", AutoSize = true };
        readonly TextBox txtHook = new TextBox { UseSystemPasswordChar = true };
        readonly CheckBox chkDiscDrop = new CheckBox { Text = "Send a screenshot to Discord when a session drops", AutoSize = true };
        readonly CheckBox chkDiscAlerts = new CheckBox { Text = "Also send all notifications as text to Discord", AutoSize = true };
        readonly NumericUpDown numDiscMin = new NumericUpDown { Minimum = 0, Maximum = 1440 };
        readonly ComboBox cmbShotMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox txtBotToken = new TextBox { UseSystemPasswordChar = true };
        readonly CheckBox chkBot = new CheckBox { Text = "Discord bot on (commands and buttons)", AutoSize = true };
        readonly TextBox txtBotChannel = new TextBox();
        readonly TextBox txtBotAllowed = new TextBox();
        readonly Label lblBotState = new Label();
        readonly CheckBox chkShot = new CheckBox { Text = "Save a screenshot when a session drops", AutoSize = true };
        readonly CheckBox chkFps = new CheckBox { Text = "Show FPS in the tiles (screen updates per second)", AutoSize = true };
        readonly CheckBox chkReport = new CheckBox { Text = "Send the daily report to Discord every morning", AutoSize = true };
        readonly DateTimePicker dtpReport = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
        readonly Label lblReportState = new Label { AutoSize = true };

        public SettingsPage(MainForm main)
        {
            this.main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(16);
            AutoScroll = true;

            var box = new Panel { Dock = DockStyle.Top, Height = 1230, BackColor = Theme.Bg };
            int y = 8;
            Row(box, "Notify when a session has been gone for more than … seconds", numAfter, ref y);
            Row(box, "Warn at CPU usage from … %", numCpu, ref y);
            Row(box, "Warn at RAM usage from … %", numRam, ref y);
            Row(box, "Warn when less than … MB of memory is free", numFree, ref y);
            chkSound.Location = new Point(420, y);
            chkSound.ForeColor = Theme.Fg;
            box.Controls.Add(chkSound);
            y += 36;
            chkLight.Location = new Point(420, y);
            chkLight.ForeColor = Theme.Fg;
            box.Controls.Add(chkLight);
            y += 36;
            foreach (var c in new[] { chkStartup, chkMin, chkBg, chkShot, chkFps })
            {
                c.Location = new Point(420, y);
                c.ForeColor = Theme.Fg;
                box.Controls.Add(c);
                y += 34;
            }
            var btnTest = UI.Btn(box, "Test screenshot", 420, y, 200, false);
            btnTest.Click += (s, e) => main.TestScreenshot();
            y += 56;

            UI.Lbl(box, "Discord webhook (the link from your channel settings, under Integrations)", 0, y, 700, false);
            UI.Put(box, txtHook, 0, y + 26, 530);
            var test = UI.Btn(box, "Send test", 540, y + 24, 110, false);
            test.Click += async (s, e) =>
            {
                string url = txtHook.Text.Trim();
                if (!Discord.IsValid(url))
                {
                    MessageBox.Show(main, "This is not a valid Discord webhook link.\r\nIt starts with https://discord.com/api/webhooks/", "Discord");
                    return;
                }
                test.Enabled = false;
                string err = await Discord.SendAsync(url, "✅ Test from RDP Panel – the webhook works.", null);
                test.Enabled = true;
                MessageBox.Show(main, err == null ? "Test sent. Check your Discord channel." : "Failed: " + err, "Discord");
            };
            y += 66;
            chkDiscDrop.Location = new Point(0, y);
            chkDiscDrop.ForeColor = Theme.Fg;
            box.Controls.Add(chkDiscDrop);
            y += 34;
            chkDiscAlerts.Location = new Point(0, y);
            chkDiscAlerts.ForeColor = Theme.Fg;
            box.Controls.Add(chkDiscAlerts);
            y += 40;
            UI.Lbl(box, "Send an overview with images every … minutes (0 = off)", 0, y + 4, 410, false);
            UI.Put(box, numDiscMin, 420, y, 110);
            y += 44;
            UI.Lbl(box, "Which image is sent with an overview or drop?", 0, y, 520, false);
            cmbShotMode.Items.AddRange(new object[] { "The whole panel (like the overview)", "A separate image per session" });
            cmbShotMode.FlatStyle = FlatStyle.Flat;
            UI.Put(box, cmbShotMode, 0, y + 26, 380);
            y += 74;

            UI.Lbl(box, "Discord bot", 0, y, 300, false).Font = Theme.UI(10.5f, FontStyle.Bold);
            y += 28;
            UI.Lbl(box, "Create a bot at discord.com/developers (New Application, Bot, Reset Token) and invite it with the", 0, y, 700, true);
            UI.Lbl(box, "scopes 'bot' and 'applications.commands' and the permissions Send Messages and Attach Files.", 0, y + 20, 700, true);
            y += 52;
            UI.Lbl(box, "Bot token", 0, y + 4, 200, false);
            UI.Put(box, txtBotToken, 210, y, 320);
            y += 38;
            UI.Lbl(box, "Channel ID (optional)", 0, y + 4, 200, false);
            UI.Put(box, txtBotChannel, 210, y, 320);
            y += 26;
            UI.Lbl(box, "Notifications with buttons appear here, and commands only work in this channel. Empty = everywhere.", 210, y, 520, true).Font = Theme.UI(8.25f);
            y += 28;
            UI.Lbl(box, "Allowed users (optional)", 0, y + 4, 200, false);
            UI.Put(box, txtBotAllowed, 210, y, 320);
            y += 26;
            UI.Lbl(box, "Discord user IDs, separated by commas. Empty = everyone in the channel may use the bot.", 210, y, 520, true).Font = Theme.UI(8.25f);
            y += 28;
            chkBot.Location = new Point(210, y);
            chkBot.ForeColor = Theme.Fg;
            box.Controls.Add(chkBot);
            y += 36;
            lblBotState.Location = new Point(210, y + 4);
            lblBotState.Width = 400;
            lblBotState.ForeColor = Theme.FgMuted;
            box.Controls.Add(lblBotState);
            var refresh = UI.Btn(box, "Refresh status", 0, y, 160, false);
            refresh.Click += (s, e) => lblBotState.Text = "Status: " + main.BotState;
            y += 60;

            UI.Lbl(box, "Daily report", 0, y, 300, false).Font = Theme.UI(10.5f, FontStyle.Bold);
            y += 28;
            UI.Lbl(box, "One message every morning with, per session: the uptime of the day before, the number of drops and the average CPU, RAM and FPS.", 0, y, 760, true);
            y += 22;
            UI.Lbl(box, "It goes to the Discord webhook above (or to the bot channel when there is no webhook).", 0, y, 760, true);
            y += 30;
            chkReport.Location = new Point(0, y);
            chkReport.ForeColor = Theme.Fg;
            box.Controls.Add(chkReport);
            y += 36;
            UI.Lbl(box, "Time of day", 0, y + 4, 200, false);
            UI.Put(box, dtpReport, 210, y, 110);
            y += 40;
            var sendNow = UI.Btn(box, "Send report now", 0, y, 160, false);
            lblReportState.Location = new Point(172, y + 6);
            lblReportState.ForeColor = Theme.FgMuted;
            lblReportState.BackColor = Color.Transparent;
            box.Controls.Add(lblReportState);
            sendNow.Click += async (s, e) =>
            {
                sendNow.Enabled = false;
                lblReportState.Text = "Sending…";
                string res;
                try { res = await main.SendReport(true); }
                catch (Exception ex) { res = ex.Message; }
                sendNow.Enabled = true;
                if (res == null) lblReportState.Text = "Report for today so far sent. Check your Discord channel.";
                else if (res == DailyReport.NoData) lblReportState.Text = "No data for today yet. Let the panel run for a minute or two and try again.";
                else lblReportState.Text = "Failed: " + res;
            };
            y += 28;
            UI.Lbl(box, "Press Save first when you changed the webhook. \"Send report now\" shows today so far, the real report covers the whole previous day.", 0, y, 760, true).Font = Theme.UI(8.25f);
            y += 40;

            UI.Lbl(box, "Diagnostics", 0, y, 300, false).Font = Theme.UI(10.5f, FontStyle.Bold);
            y += 28;
            UI.Lbl(box, "Something wrong? One click makes a zip with the log and system information, without passwords, tokens or links.", 0, y, 760, true);
            y += 30;
            var diag = UI.Btn(box, "Create diagnostic file…", 0, y, 200, false);
            diag.Click += (s, e) => DiagnosticFile.RunWithDialog(main);
            y += 60;

            var save = UI.Btn(box, "Save", 420, y, 110, true);
            save.Click += (s, e) => Save();
            box.Height = y + 70;
            Controls.Add(box);
        }

        static void Row(Control p, string label, Control c, ref int y)
        {
            UI.Lbl(p, label, 0, y + 4, 410, false);
            UI.Put(p, c, 420, y, 110);
            y += 40;
        }

        public void Load()
        {
            var c = main.Cfg;
            numAfter.Value = Math.Max(numAfter.Minimum, Math.Min(numAfter.Maximum, c.AlertAfterSeconds));
            numCpu.Value = Math.Max(numCpu.Minimum, Math.Min(numCpu.Maximum, c.CpuWarn));
            numRam.Value = Math.Max(numRam.Minimum, Math.Min(numRam.Maximum, c.RamWarn));
            numFree.Value = Math.Max(numFree.Minimum, Math.Min(numFree.Maximum, c.MinFreeMb));
            chkSound.Checked = c.AlertSound;
            chkLight.Checked = c.LightTheme;
            chkStartup.Checked = StartupHelper.IsEnabled();
            chkMin.Checked = c.StartMinimized;
            chkBg.Checked = c.BackgroundMode;
            chkShot.Checked = c.ScreenshotOnDrop;
            chkFps.Checked = c.ShowFps;
            chkReport.Checked = c.ReportEnabled;
            TimeSpan at;
            if (!TimeSpan.TryParse(c.ReportTime, CultureInfo.InvariantCulture, out at)) at = new TimeSpan(8, 0, 0);
            dtpReport.Value = DateTime.Today.Add(at);
            lblReportState.Text = string.IsNullOrEmpty(c.LastReportDate) ? "" : "Last daily report was sent on " + c.LastReportDate + ".";
            txtHook.Text = c.WebhookUrl;
            chkDiscDrop.Checked = c.DiscordOnDrop;
            chkDiscAlerts.Checked = c.DiscordAlerts;
            numDiscMin.Value = Math.Max(numDiscMin.Minimum, Math.Min(numDiscMin.Maximum, c.DiscordIntervalMin));
            cmbShotMode.SelectedIndex = c.DiscordShotMode == 1 ? 1 : 0;
            txtBotToken.Text = c.BotToken;
            txtBotChannel.Text = c.BotChannelId;
            txtBotAllowed.Text = c.BotAllowedUsers;
            chkBot.Checked = c.BotEnabled;
            lblBotState.Text = "Status: " + main.BotState;
        }

        void Save()
        {
            var c = main.Cfg;
            string hook = txtHook.Text.Trim();
            if (hook.Length > 0 && !Discord.IsValid(hook))
            {
                MessageBox.Show(main, "The Discord webhook link is not valid. It starts with https://discord.com/api/webhooks/", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string allowed = txtBotAllowed.Text.Trim();
            if (allowed.Length > 0 && !allowed.All(ch => char.IsDigit(ch) || ch == ',' || ch == ';' || ch == ' '))
            {
                MessageBox.Show(main, "User IDs consist of digits only, separated by commas (Discord: Settings, Advanced, Developer Mode, right-click a user, Copy ID).", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string chan = txtBotChannel.Text.Trim();
            if (chan.Length > 0 && !chan.All(char.IsDigit))
            {
                MessageBox.Show(main, "The channel ID consists of digits only (right-click the channel in Discord, Copy Channel ID).", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            c.BotToken = txtBotToken.Text.Trim();
            c.BotChannelId = chan;
            c.BotAllowedUsers = txtBotAllowed.Text.Trim();
            c.BotEnabled = chkBot.Checked;
            c.WebhookUrl = hook;
            c.DiscordOnDrop = chkDiscDrop.Checked;
            c.DiscordAlerts = chkDiscAlerts.Checked;
            c.DiscordIntervalMin = (int)numDiscMin.Value;
            c.DiscordShotMode = cmbShotMode.SelectedIndex == 1 ? 1 : 0;
            bool themeChanged = chkLight.Checked != c.LightTheme;
            c.AlertAfterSeconds = (int)numAfter.Value;
            c.CpuWarn = (int)numCpu.Value;
            c.RamWarn = (int)numRam.Value;
            c.MinFreeMb = (int)numFree.Value;
            c.AlertSound = chkSound.Checked;
            c.LightTheme = chkLight.Checked;
            c.StartMinimized = chkMin.Checked;
            c.BackgroundMode = chkBg.Checked;
            c.ScreenshotOnDrop = chkShot.Checked;
            c.ShowFps = chkFps.Checked;
            c.ReportEnabled = chkReport.Checked;
            c.ReportTime = dtpReport.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
            c.StartWithWindows = chkStartup.Checked;
            bool startupOk = StartupHelper.Set(chkStartup.Checked);
            c.Save();
            main.ApplyBotSettings();
            if (!startupOk) MessageBox.Show(main, "Setting up automatic start with Windows failed.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (themeChanged) main.RestartForTheme();
            else MessageBox.Show(main, "Settings saved.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
