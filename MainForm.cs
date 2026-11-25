using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RdpPanel
{
    public class MainForm : Form
    {
        const int Gap = 8;

        internal readonly AppConfig Cfg;
        internal readonly List<SessionTile> Tiles = new List<SessionTile>();
        internal SessionTile SelectedTile;

        readonly HashSet<SessionTile> hidden = new HashSet<SessionTile>();
        readonly List<Label> groupLabels = new List<Label>();
        List<List<SessionTile>> lastGroups = new List<List<SessionTile>>();
        SessionTile focused;
        string filter = "";
        int page;
        bool fullscreen;
        FormWindowState prevState;
        int tick;
        DateTime warnAt = DateTime.MinValue;
        int totalToday;
        bool loadingProfiles;
        bool themeBusy;
        readonly bool autoStartLaunch;
        DateTime lastDiscord = DateTime.Now;
        DiscordBot bot;
        byte[] lastPanelJpeg;
        DateTime lastPanelAt = DateTime.MinValue;
        internal DateTime MuteUntil = DateTime.MinValue;   // /quiet via the bot

        // layout
        readonly Panel top = new Panel();
        readonly Panel side = new Panel();
        readonly Panel content = new Panel();
        readonly Panel footer = new Panel();

        // update banner (shown when a newer release exists on GitHub)
        readonly Panel updateBar = new Panel();
        readonly Label lblUpdate = new Label();
        UpdateInfo pendingUpdate;
        string updatedFromVersion;   // set at the first start after an update; the bot channel is told once the bot is connected
        bool updateChecking;
        DateTime updateRetryAt = DateTime.MinValue;   // while a new release is still being built (or the check failed): try again then

        // top bar
        readonly Label lblTitle = new Label();
        readonly Label lblCount = new Label();
        readonly Label lblNet = new Label();
        readonly ComboBox cmbProfile = new ComboBox();
        readonly NumericUpDown numCols = new NumericUpDown { Minimum = 1, Maximum = 12, Width = 54 };
        readonly Label lblRows = new Label();
        readonly Button btnGroup = Theme.MakeButton("Group by PC");
        readonly Button btnAdd = Theme.MakeButton("+ Session", true);
        readonly Button btnOverview = Theme.MakeButton("← Back to overview");
        readonly TextBox txtSearch = new TextBox();

        // sidebar
        readonly List<Button> nav = new List<Button>();
        readonly ToggleSwitch swTheme = new ToggleSwitch();
        readonly Label lblProfileName = new Label();

        // pages
        readonly Panel pgOverview = new Panel();
        readonly Panel host = new Panel();
        readonly Panel strip = new Panel();
        MonitoringPage pgMon;
        AltPage pgAlt;
        HealthPage pgHealth;
        string lastScheduled;                 // the profile the schedule asked for last time (null = not looked yet)
        readonly Panel checkHost = new Panel();
        readonly Label lblCheck = new Label();
        int filterMode;                    // overview filter: 0 all, 1 problems, 2 offline, 3 connected
        readonly ComboBox cmbFilter = new ComboBox();
        readonly ComboBox cmbSort = new ComboBox();
        readonly Button btnCompact = Theme.MakeButton("Compact");
        ActivityForm activityForm;
        string arrangeSig = "";
        System.Drawing.Icon[] trayIcons;
        int trayState = -2;
        const int CompactH = 42;
        LogPage pgLog;
        PcPage pgPc;
        ProfilesPage pgProf;
        SettingsPage pgSet;
        HelpPage pgHelp;
        CreditsPage pgCredits;
        TutorialForm tutorial;
        bool reportBusy;
        DateTime nextReportTry = DateTime.MinValue;

        // dashboard at the bottom
        Card cardPerf, cardSum, cardLog, cardPc;
        readonly FlowLayoutPanel perfFlow = new FlowLayoutPanel();
        readonly ListView lvSum = UI.NewList();
        readonly ListView lvLog = UI.NewList();
        readonly Label lblSel = new Label();

        class PerfItem { public SessionTile Tile; public Label Vals; }
        readonly List<PerfItem> perf = new List<PerfItem>();

        // footer bar
        readonly Label lblFootL = new Label();
        readonly Label lblWarn = new Label();
        readonly Label lblAlerts = new Label();
        readonly ToggleSwitch swAlerts = new ToggleSwitch();
        readonly Label lblSound = new Label();
        readonly ToggleSwitch swSound = new ToggleSwitch();
        bool syncingSound;
        int soundAssert = 40;                 // for a while after start and after a change: make sure the sound state in the mixer is right
        readonly ToolTip tips = new ToolTip();

        readonly NotifyIcon notify = new NotifyIcon();
        readonly Timer clock = new Timer { Interval = 1000 };
        readonly Timer peek = new Timer { Interval = 200 };

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll")]
        static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int cmd);

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_APPWINDOW = 0x40000;

        // background mode: the window is still "open" (so the sessions keep drawing) but invisible and click-through
        bool inBackground;
        bool backgroundHinted;

        public MainForm(AppConfig cfg, bool autoStart)
        {
            Cfg = cfg;
            Text = "RDP Panel";
            Size = new Size(1500, 940);
            MinimumSize = new Size(1200, 720);
            BackColor = Theme.Bg;
            Font = Theme.UI(9f);
            RestoreWindow();
            autoStartLaunch = autoStart;
            if (autoStart && Cfg.StartMinimized && !Cfg.BackgroundMode) WindowState = FormWindowState.Minimized;

            pgMon = new MonitoringPage(this);
            pgAlt = new AltPage(this);
            pgHealth = new HealthPage(this);
            pgLog = new LogPage(this);
            pgPc = new PcPage(this);
            pgProf = new ProfilesPage(this);
            pgSet = new SettingsPage(this);
            pgHelp = new HelpPage(this);
            pgCredits = new CreditsPage(this);

            BuildTop();
            BuildSide();
            BuildOverview();
            BuildFooter();

            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Bg;
            foreach (Control p in new Control[] { pgOverview, pgMon, pgLog, pgPc, pgProf, pgSet, pgHelp, pgCredits, pgAlt, pgHealth })
            {
                p.Dock = DockStyle.Fill;
                p.Visible = false;
                content.Controls.Add(p);
            }
            BuildUpdateBar();
            content.Controls.Add(updateBar);   // added last, so it docks above the pages

            // order matters for docking: footer bar, top bar, sidebar, then the rest
            Controls.Add(content);
            Controls.Add(side);
            Controls.Add(top);
            Controls.Add(footer);

            notify.Icon = SystemIcons.Application;
            notify.Text = "RDP Panel";
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Open RDP Panel", null, (s, e) => ShowFromTray());
            trayMenu.Items.Add("Test screenshot", null, (s, e) => TestScreenshot());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, (s, e) => Close());
            notify.ContextMenuStrip = trayMenu;
            notify.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
            notify.BalloonTipClicked += (s, e) => ShowFromTray();
            notify.Visible = true;

            foreach (var sc in Cfg.Sessions) CreateTile(sc);
            RefreshProfileCombo();
            var ap = Cfg.Profiles.FirstOrDefault(p => p.Name == Cfg.ActiveProfile);
            if (ap != null) ApplyProfile(ap); else UpdateProfileCard();
            UpdateGroupButton();
            RebuildPerf();
            SelectTile(Tiles.FirstOrDefault());
            ShowPage(0);

            clock.Tick += (s, e) =>
            {
                foreach (var t in Tiles) t.Tick();
                if (tick % 10 == 0) foreach (var t in Tiles) t.RefreshStatsAsync();
                if (tick % 15 == 3) foreach (var t in Tiles) t.ProbeAsync();
                if (tick % 15 == 7) RefreshPanelCache();
                if (tick % 15 == 0) RefreshSummary();
                if (tick % 60 == 20) RecordMetrics();
                if (tick % 30 == 5) CheckDailyReport();
                if (tick % 30 == 12) SendUpdatedMessage();
                if (tick % 5 == 2) UpdateTrayIcon();
                if (tick % 5 == 3) ApplySound();
                if (tick % 5 == 4) { UpdateHealthBadge(); UpdateChecklist(); }
                if (tick % 20 == 7) CheckProfileSchedule();
                if (tick % 600 == 17) pgAlt.SnapshotToday();
                if (page == 0 && focused == null && (filterMode != 0 || Cfg.SortMode >= 2) && tick % (Cfg.SortMode >= 3 ? 30 : 4) == 1) RearrangeIfChanged();
                if (tick == 10 || tick % 21600 == 10) CheckUpdateAsync();   // 10 s after start, then every 6 hours
                if (updateRetryAt != DateTime.MinValue && DateTime.Now >= updateRetryAt) { updateRetryAt = DateTime.MinValue; CheckUpdateAsync(); }
                if (Cfg.DiscordIntervalMin > 0 && Discord.IsValid(Cfg.WebhookUrl) && (DateTime.Now - lastDiscord).TotalMinutes >= Cfg.DiscordIntervalMin)
                {
                    lastDiscord = DateTime.Now;
                    SendDiscordOverview();
                }
                tick++;
                UpdateTop();
                UpdatePerf();
                UpdateGroupLabels();
                UpdateFooter();
                if (page == 1) pgMon.RefreshData();
                if (page == 3 && tick % 2 == 0) pgPc.RefreshData();
            };
            clock.Start();

            peek.Tick += (s, e) =>
            {
                if (!fullscreen) return;
                var p = PointToClient(Cursor.Position);
                if (!top.Visible && p.Y >= 0 && p.Y <= 2 && ClientRectangle.Contains(p)) top.Visible = true;
                else if (top.Visible && p.Y > top.Height + 40) top.Visible = false;
            };
        }

        // ---------------------------------------------------------------- construction

        static Label Lab(string text, int w)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Width = w,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.FgMuted,
                Margin = new Padding(0, 2, 4, 0)
            };
        }

        void BuildUpdateBar()
        {
            updateBar.Dock = DockStyle.Top;
            updateBar.Height = 38;
            updateBar.BackColor = Theme.Accent;
            updateBar.Visible = false;

            lblUpdate.Dock = DockStyle.Fill;
            lblUpdate.ForeColor = Color.White;
            lblUpdate.Font = Theme.UI(10f, FontStyle.Bold);
            lblUpdate.TextAlign = ContentAlignment.MiddleLeft;
            lblUpdate.Padding = new Padding(16, 0, 0, 0);

            var btns = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0, 5, 10, 0),
                BackColor = Theme.Accent
            };
            var btnGet = Theme.MakeButton("Update now");
            btnGet.Click += (s, e) => { if (pendingUpdate != null) ShowUpdateDialog(pendingUpdate); };
            var btnSkip = Theme.MakeButton("Skip this version");
            btnSkip.Click += (s, e) =>
            {
                if (pendingUpdate != null) { Cfg.SkippedVersion = pendingUpdate.Version; Cfg.Save(); }
                updateBar.Visible = false;
            };
            var btnLater = Theme.MakeButton("Later");
            btnLater.Click += (s, e) => updateBar.Visible = false;
            btns.Controls.Add(btnGet);
            btns.Controls.Add(btnSkip);
            btns.Controls.Add(btnLater);

            updateBar.Controls.Add(lblUpdate);
            updateBar.Controls.Add(btns);
        }

        // automatic check (10 s after start, every 6 hours, and while a new release is still being built)
        async void CheckUpdateAsync()
        {
            if (!Cfg.CheckForUpdates || updateChecking) return;
            updateChecking = true;
            try
            {
                var r = await UpdateChecker.CheckAsync();
                if (IsDisposed) return;
                if (r.Status == CheckStatus.Building) { updateRetryAt = DateTime.Now.AddMinutes(10); return; }   // installer not online yet: no pop-up
                if (r.Status == CheckStatus.Error) { updateRetryAt = DateTime.Now.AddMinutes(30); return; }       // e.g. no internet at startup
                if (r.Status != CheckStatus.Available) return;
                if (r.Info.Version == Cfg.SkippedVersion) return;
                ApplyAvailable(r.Info, false);
            }
            catch { }
            finally { updateChecking = false; }
        }

        // the "Check now" button in Settings: ignores the on/off switch and "skip this version". Returns the text for next to the button.
        internal async Task<string> CheckUpdateNow()
        {
            if (updateChecking) return "A check is already running.";
            updateChecking = true;
            try
            {
                var r = await UpdateChecker.CheckAsync();
                if (IsDisposed) return "";
                switch (r.Status)
                {
                    case CheckStatus.UpToDate:
                        return "You're up to date (v" + AppInfo.Version + ").";
                    case CheckStatus.Building:
                        return "Version " + r.Info.Version + " is being built right now. Try again in a few minutes.";
                    case CheckStatus.Error:
                        return "Could not check for updates: " + r.Message;
                    default:
                        ApplyAvailable(r.Info, true);
                        return "Version " + r.Info.Version + " is available.";
                }
            }
            catch (Exception ex) { return "Could not check for updates: " + ex.Message; }
            finally { updateChecking = false; }
        }

        void ApplyAvailable(UpdateInfo info, bool manual)
        {
            bool isNew = manual || pendingUpdate == null || pendingUpdate.Version != info.Version || pendingUpdate.FilesReady != info.FilesReady;
            pendingUpdate = info;
            lblUpdate.Text = "A new version is available: RDP Panel v" + info.Version + "  (you have v" + AppInfo.Version + ")";
            if (!isNew)
            {
                if (!updateBar.Visible) updateBar.Visible = true;
                return;
            }
            // pop-up window, unless the panel runs hidden (autostart / background / minimised): then a tray balloon + the banner
            bool quiet = !manual && (autoStartLaunch && (Cfg.StartMinimized || Cfg.BackgroundMode) || inBackground || WindowState == FormWindowState.Minimized || !Visible);
            if (quiet)
            {
                updateBar.Visible = true;
                notify.BalloonTipTitle = "RDP Panel update";
                notify.BalloonTipText = "Version " + info.Version + " is available.";
                notify.ShowBalloonTip(5000);
            }
            else ShowUpdateDialog(info);
        }

        // the pop-up. "Update now" downloads and checks the installer inside the pop-up; then we start it and close the panel,
        // the installer replaces the exe and starts the panel again.
        void ShowUpdateDialog(UpdateInfo info)
        {
            using (var dlg = new UpdateDialog(info))
            {
                var r = dlg.ShowDialog(this);
                if (r == DialogResult.Yes)
                {
                    if (!string.IsNullOrEmpty(dlg.InstallerPath))
                    {
                        string err = UpdateInstaller.Launch(dlg.InstallerPath);
                        if (err == null)
                        {
                            LogService.Write("", "Update", "installing v" + info.Version, 0);
                            updateBar.Visible = false;
                            Close();
                            return;
                        }
                        MessageBox.Show(this, "The installer could not be started:\r\n" + err + "\r\n\r\nThe download page opens instead.", "RDP Panel update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        UI.OpenUrl(info.Url);
                    }
                    else UI.OpenUrl(string.IsNullOrEmpty(info.SetupUrl) ? info.Url : info.SetupUrl);
                    updateBar.Visible = true;
                }
                else if (r == DialogResult.Ignore) { Cfg.SkippedVersion = info.Version; Cfg.Save(); updateBar.Visible = false; }
                else updateBar.Visible = true;   // later: the banner stays as a reminder
            }
        }

        void BuildTop()
        {
            top.Dock = DockStyle.Top;
            top.Height = 66;
            top.BackColor = Theme.Surface;

            var brand = new Panel { Dock = DockStyle.Left, Width = 230, BackColor = Theme.Surface };
            lblTitle.Text = "RDP Panel v" + AppInfo.Version;
            lblTitle.Font = Theme.UI(14f, FontStyle.Bold);
            lblTitle.ForeColor = Theme.Fg;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(18, 8);
            lblCount.Font = Theme.UI(9.5f);
            lblCount.ForeColor = Theme.FgMuted;
            lblCount.AutoSize = true;
            lblCount.Location = new Point(19, 38);
            brand.Controls.Add(lblTitle);
            brand.Controls.Add(lblCount);

            var right = new Panel { Dock = DockStyle.Right, Width = 210, BackColor = Theme.Surface };
            lblNet.Dock = DockStyle.Fill;
            lblNet.TextAlign = ContentAlignment.MiddleRight;
            lblNet.Padding = new Padding(0, 0, 18, 0);
            lblNet.Font = Theme.UI(10f, FontStyle.Bold);
            lblNet.ForeColor = Theme.FgMuted;
            right.Controls.Add(lblNet);

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Padding = new Padding(4, 16, 0, 0),
                BackColor = Theme.Surface
            };

            cmbProfile.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbProfile.FlatStyle = FlatStyle.Flat;
            cmbProfile.BackColor = Theme.Raised;
            cmbProfile.ForeColor = Theme.Fg;
            cmbProfile.Font = Theme.UI(9.5f);
            cmbProfile.Width = 170;
            cmbProfile.Margin = new Padding(0, 3, 16, 0);
            cmbProfile.SelectedIndexChanged += (s, e) =>
            {
                if (loadingProfiles) return;
                int i = cmbProfile.SelectedIndex;
                ApplyProfile(i <= 0 ? null : Cfg.Profiles[i - 1]);
            };

            Theme.StyleInput(numCols);
            numCols.Margin = new Padding(0, 3, 12, 0);
            numCols.Value = Math.Max(1, Math.Min(12, Cfg.Columns));
            numCols.ValueChanged += (s, e) =>
            {
                Cfg.Columns = (int)numCols.Value;
                Cfg.Save();
                ArrangeTiles();
            };

            lblRows.AutoSize = false;
            lblRows.Width = 70;
            lblRows.Height = 28;
            lblRows.TextAlign = ContentAlignment.MiddleLeft;
            lblRows.ForeColor = Theme.FgMuted;
            lblRows.Margin = new Padding(0, 2, 8, 0);

            btnGroup.Margin = new Padding(0, 0, 10, 0);
            btnGroup.Click += (s, e) =>
            {
                Cfg.GroupByHost = !Cfg.GroupByHost;
                Cfg.Save();
                UpdateGroupButton();
                ArrangeTiles();
            };

            btnAdd.Margin = new Padding(0, 0, 14, 0);
            btnAdd.Click += (s, e) => AddSession();

            Theme.StyleInput(txtSearch);
            txtSearch.Width = 210;
            txtSearch.Margin = new Padding(0, 3, 10, 0);
            txtSearch.HandleCreated += (s, e) => SendMessage(txtSearch.Handle, 0x1501, (IntPtr)1, "Search for PC…");
            txtSearch.TextChanged += (s, e) =>
            {
                filter = txtSearch.Text.Trim().ToLowerInvariant();
                ArrangeTiles();
            };

            btnOverview.Visible = false;
            btnOverview.Click += (s, e) => SetFocus(null);

            flow.Controls.Add(Lab("Profile:", 54));
            flow.Controls.Add(cmbProfile);
            flow.Controls.Add(Lab("Columns:", 74));
            flow.Controls.Add(numCols);
            flow.Controls.Add(lblRows);
            flow.Controls.Add(btnGroup);
            flow.Controls.Add(btnAdd);
            var btnShot = Theme.MakeButton("Screenshot");
            btnShot.Margin = new Padding(0, 0, 10, 0);
            btnShot.Click += (s, e) => ManualPanelShot();
            flow.Controls.Add(btnShot);
            flow.Controls.Add(txtSearch);
            flow.Controls.Add(btnOverview);

            top.Controls.Add(flow);
            top.Controls.Add(right);
            top.Controls.Add(brand);
        }

        void BuildSide()
        {
            side.Dock = DockStyle.Left;
            side.Width = 214;
            side.BackColor = Theme.Surface;

            // current profile
            var card = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Theme.Surface, Padding = new Padding(12, 6, 12, 12) };
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Raised, Cursor = Cursors.Hand };
            var star = new Label { Text = "★", ForeColor = Theme.Warn, Font = Theme.UI(16f), Location = new Point(10, 10), AutoSize = true, BackColor = Color.Transparent };
            var cap = new Label { Text = "Current profile", ForeColor = Theme.FgMuted, Font = Theme.UI(8.5f), Location = new Point(46, 8), AutoSize = true, BackColor = Color.Transparent };
            lblProfileName.ForeColor = Theme.Fg;
            lblProfileName.Font = Theme.UI(10f, FontStyle.Bold);
            lblProfileName.Location = new Point(46, 26);
            lblProfileName.AutoSize = true;
            lblProfileName.BackColor = Color.Transparent;
            inner.Controls.AddRange(new Control[] { star, cap, lblProfileName });
            foreach (Control c in new Control[] { inner, star, cap, lblProfileName }) c.Click += (s, e) => ShowPage(4);
            card.Controls.Add(inner);

            // theme and full screen
            var rows = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Theme.Surface };
            var lt = new Label { Text = "Theme (dark / light)", Location = new Point(18, 16), AutoSize = true, ForeColor = Theme.Fg, Font = Theme.UI(9.5f) };
            swTheme.Location = new Point(150, 40);
            swTheme.Checked = Cfg.LightTheme;
            swTheme.CheckedChanged += (s, e) =>
            {
                if (themeBusy) return;
                if (MessageBox.Show(this, "The panel will restart to apply the theme. Continue?", "Theme",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Cfg.LightTheme = swTheme.Checked;
                    RestartForTheme();
                }
                else
                {
                    themeBusy = true;
                    swTheme.Checked = !swTheme.Checked;
                    themeBusy = false;
                }
            };
            var lf = new Label { Text = "Full screen", Location = new Point(18, 78), AutoSize = true, ForeColor = Theme.Fg, Font = Theme.UI(9.5f), Cursor = Cursors.Hand };
            var chipF = new Chip { Text = "F11", Location = new Point(150, 74), Cursor = Cursors.Hand };
            chipF.FitWidth();
            lf.Click += (s, e) => ToggleFullscreen();
            chipF.Click += (s, e) => ToggleFullscreen();
            rows.Controls.AddRange(new Control[] { lt, swTheme, lf, chipF });

            // navigation
            string[] names = { "⌂  Overview", "∿  Monitoring", "☰  Log", "▣  PC management", "★  Profiles", "⚙  Settings", "?  Help", "♥  Credits", "🐝  Alt accounts", "✚  Health" };
            // the page numbers stay as they are; this is where each one is shown in the list (Alt accounts under PC management)
            // order on the screen: Overview, Monitoring, Health, Log, PC management, Alt accounts, Profiles, Settings, Help, Credits
            int[] slot = { 0, 1, 3, 4, 6, 7, 8, 9, 5, 2 };
            var navPanel = new Panel { Dock = DockStyle.Top, Height = names.Length * 44 + 14, BackColor = Theme.Surface };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = new Button
                {
                    Text = names[i],
                    Location = new Point(10, 8 + slot[i] * 44),
                    Size = new Size(194, 40),
                    FlatStyle = FlatStyle.Flat,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(14, 0, 0, 0),
                    Font = Theme.UI(10.5f),
                    Cursor = Cursors.Hand,
                    TabStop = false
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (s, e) => ShowPage(idx);
                nav.Add(b);
                navPanel.Controls.Add(b);
            }

            side.Controls.Add(card);
            side.Controls.Add(rows);
            side.Controls.Add(navPanel);
        }

        void BuildOverview()
        {
            pgOverview.BackColor = Theme.Bg;

            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Bg;
            host.Resize += (s, e) => ArrangeTiles();

            strip.Dock = DockStyle.Bottom;
            strip.Height = 196;
            strip.BackColor = Theme.Bg;
            strip.Padding = new Padding(8, 0, 8, 8);

            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Theme.Bg };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // performance
            cardPerf = new Card("Performance overview") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            perfFlow.Dock = DockStyle.Fill;
            perfFlow.WrapContents = false;
            perfFlow.AutoScroll = true;
            perfFlow.BackColor = Theme.Surface;
            cardPerf.Body.Controls.Add(perfFlow);

            // summary
            cardSum = new Card("Daily summary") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            lvSum.Dock = DockStyle.Fill;
            lvSum.Columns.Add("Session", 130);
            lvSum.Columns.Add("Connected time", 110);
            lvSum.Columns.Add("Status", 90);
            lvSum.Columns.Add("Yesterday", 90);
            cardSum.Body.Controls.Add(lvSum);
            lvSum.ContextMenuStrip = MakePlaceMenu(1, "the Daily summary", lvSum);

            // log
            cardLog = new Card("Log") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            lvLog.Dock = DockStyle.Fill;
            lvLog.HeaderStyle = ColumnHeaderStyle.None;
            lvLog.Columns.Add("Time", 52);
            lvLog.Columns.Add("Event", 420);
            cardLog.Body.Controls.Add(lvLog);

            // PC management
            cardPc = new Card("PC management") { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var tp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Theme.Surface };
            for (int i = 0; i < 5; i++) tp.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            lblSel.Dock = DockStyle.Fill;
            lblSel.ForeColor = Theme.FgMuted;
            lblSel.Font = Theme.UI(8.5f);
            lblSel.TextAlign = ContentAlignment.MiddleLeft;
            lblSel.Text = "Selected: –";
            tp.Controls.Add(lblSel, 0, 0);
            tp.Controls.Add(ActionBtn("Turn PC on (WoL)", Gfx.Blend(Theme.Good, Theme.Surface, 0.4f), () => { if (SelectedTile != null) SelectedTile.DoWake(); }), 0, 1);
            tp.Controls.Add(ActionBtn("↻ Restart", Gfx.Blend(Theme.Accent, Theme.Surface, 0.55f), () => { if (SelectedTile != null) SelectedTile.DoPower(true); }), 0, 2);
            tp.Controls.Add(ActionBtn("Shut down", Gfx.Blend(Theme.Bad, Theme.Surface, 0.5f), () => { if (SelectedTile != null) SelectedTile.DoPower(false); }), 0, 3);
            tp.Controls.Add(ActionBtn("Search network", Theme.Raised, () => ScanNetwork()), 0, 4);
            cardPc.Body.Controls.Add(tp);

            tl.Controls.Add(cardPerf, 0, 0);
            tl.Controls.Add(cardSum, 1, 0);
            tl.Controls.Add(cardLog, 2, 0);
            tl.Controls.Add(cardPc, 3, 0);
            strip.Controls.Add(tl);

            pgOverview.Controls.Add(host);
            pgOverview.Controls.Add(strip);

            // filter, sort and view above the tiles
            var bar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Bg };
            var barFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(8, 5, 8, 0) };
            foreach (var cb in new[] { cmbFilter, cmbSort })
            {
                cb.DropDownStyle = ComboBoxStyle.DropDownList;
                cb.FlatStyle = FlatStyle.Flat;
                Theme.StyleInput(cb);
                cb.Margin = new Padding(0, 3, 14, 0);
            }
            cmbFilter.Width = 130;
            cmbFilter.Items.AddRange(new object[] { "All sessions", "Problems", "Offline", "Connected" });
            cmbFilter.SelectedIndex = 0;
            cmbSort.Width = 170;
            cmbSort.Items.AddRange(new object[] { "My own order", "Name", "Problems first", "CPU high to low", "FPS low to high" });
            cmbSort.SelectedIndex = Math.Max(0, Math.Min(4, Cfg.SortMode));
            cmbFilter.SelectedIndexChanged += (s, e) => { filterMode = cmbFilter.SelectedIndex; ArrangeTiles(); };
            cmbSort.SelectedIndexChanged += (s, e) => { Cfg.SortMode = cmbSort.SelectedIndex; Cfg.Save(); ArrangeTiles(); };
            btnCompact.Margin = new Padding(0, 0, 10, 0);
            btnCompact.Click += (s, e) => { Cfg.CompactView = !Cfg.CompactView; Cfg.Save(); UpdateCompactButton(); ArrangeTiles(); };
            UpdateCompactButton();
            var btnActivity = Theme.MakeButton("Activity");
            btnActivity.Margin = new Padding(0, 0, 10, 0);
            btnActivity.Click += (s, e) => ShowActivity();
            var btnMulti = Theme.MakeButton("Edit several…");
            btnMulti.Margin = new Padding(0, 0, 10, 0);
            btnMulti.Click += (s, e) => EditSeveral();
            barFlow.Controls.Add(Lab("Show:", 46));
            barFlow.Controls.Add(cmbFilter);
            barFlow.Controls.Add(Lab("Sort:", 40));
            barFlow.Controls.Add(cmbSort);
            barFlow.Controls.Add(btnCompact);
            barFlow.Controls.Add(btnActivity);
            barFlow.Controls.Add(btnMulti);
            bar.Controls.Add(barFlow);
            pgOverview.Controls.Add(bar);   // docks above the tiles
            BuildChecklist();
            pgOverview.Controls.Add(checkHost);   // added last: above the toolbar
        }

        void UpdateCompactButton()
        {
            btnCompact.BackColor = Cfg.CompactView ? Theme.Accent : Theme.Raised;
            btnCompact.ForeColor = Cfg.CompactView ? Color.White : Theme.Fg;
        }

        void ShowActivity()
        {
            if (activityForm == null || activityForm.IsDisposed)
            {
                activityForm = new ActivityForm(this);
                activityForm.Show(this);
            }
            else activityForm.Activate();
        }

        internal bool IsHiddenByProfile(SessionTile t) { return hidden.Contains(t); }
        internal void RebuildPerfOverview() { RebuildPerf(); }

        static Button ActionBtn(string text, Color back, Action a)
        {
            var b = Theme.MakeButton(text);
            b.AutoSize = false;
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(0, 2, 0, 2);
            b.BackColor = back;
            b.ForeColor = Theme.IsLight && back != Theme.Raised ? Color.Black : Theme.Fg;
            b.FlatAppearance.MouseOverBackColor = Gfx.Blend(Color.White, back, 0.12f);
            b.Click += (s, e) => a();
            return b;
        }

        void BuildFooter()
        {
            footer.Dock = DockStyle.Bottom;
            footer.Height = 36;
            footer.BackColor = Theme.Surface;

            lblFootL.AutoSize = true;
            lblFootL.Location = new Point(16, 10);
            lblFootL.ForeColor = Theme.FgMuted;
            lblFootL.Font = Theme.UI(9f);

            lblWarn.AutoSize = false;
            lblWarn.TextAlign = ContentAlignment.MiddleRight;
            lblWarn.ForeColor = Theme.Warn;
            lblWarn.Font = Theme.UI(9.5f, FontStyle.Bold);
            lblWarn.AutoEllipsis = true;

            lblAlerts.AutoSize = false;
            lblAlerts.TextAlign = ContentAlignment.MiddleRight;
            lblAlerts.ForeColor = Theme.Fg;
            lblAlerts.Width = 130;
            lblAlerts.Height = 36;

            swAlerts.Checked = Cfg.AlertsEnabled;
            lblAlerts.Text = Cfg.AlertsEnabled ? "Notifications on" : "Notifications off";
            swAlerts.CheckedChanged += (s, e) =>
            {
                Cfg.AlertsEnabled = swAlerts.Checked;
                lblAlerts.Text = Cfg.AlertsEnabled ? "Notifications on" : "Notifications off";
                Cfg.Save();
            };

            // sound: one switch that turns every sound of the panel off
            lblSound.AutoSize = false;
            lblSound.TextAlign = ContentAlignment.MiddleRight;
            lblSound.ForeColor = Theme.Fg;
            lblSound.Width = 84;
            lblSound.Height = 36;
            swSound.Checked = Cfg.AlertSound;
            lblSound.Text = Cfg.AlertSound ? "Sound on" : "Sound off";
            swSound.CheckedChanged += (s, e) =>
            {
                if (syncingSound) return;
                Cfg.AlertSound = swSound.Checked;
                lblSound.Text = Cfg.AlertSound ? "Sound on" : "Sound off";
                Cfg.Save();
                soundAssert = 40;
                AppAudio.SetMuted(!Cfg.AlertSound);
            };

            footer.Controls.AddRange(new Control[] { lblFootL, lblWarn, lblSound, swSound, lblAlerts, swAlerts });
            footer.Resize += (s, e) =>
            {
                swAlerts.Location = new Point(footer.Width - 16 - swAlerts.Width, 6);
                lblAlerts.Location = new Point(swAlerts.Left - lblAlerts.Width - 6, 0);
                swSound.Location = new Point(lblAlerts.Left - 18 - swSound.Width, 6);
                lblSound.Location = new Point(swSound.Left - lblSound.Width - 6, 0);
                int x = 520;
                lblWarn.SetBounds(x, 0, Math.Max(0, lblSound.Left - x - 12), footer.Height);
            };
        }

        // ---------------------------------------------------------------- navigation

        void ShowPage(int idx)
        {
            page = idx;
            pgOverview.Visible = idx == 0;
            pgMon.Visible = idx == 1;
            pgLog.Visible = idx == 2;
            pgPc.Visible = idx == 3;
            pgProf.Visible = idx == 4;
            pgSet.Visible = idx == 5;
            pgHelp.Visible = idx == 6;
            pgCredits.Visible = idx == 7;
            pgAlt.Visible = idx == 8;
            pgHealth.Visible = idx == 9;

            for (int i = 0; i < nav.Count; i++)
            {
                bool on = i == idx;
                nav[i].BackColor = on ? Theme.Accent : Theme.Surface;
                nav[i].ForeColor = on ? Color.White : Theme.Fg;
                nav[i].FlatAppearance.MouseOverBackColor = on ? Theme.AccentHover : Theme.Raised;
            }

            if (idx == 0) ArrangeTiles();
            else if (idx == 1) { pgMon.Rebuild(); pgMon.RefreshData(); }
            else if (idx == 2) pgLog.RefreshData();
            else if (idx == 3) pgPc.RefreshData();
            else if (idx == 4) pgProf.RefreshData();
            else if (idx == 5) pgSet.Load();
            else if (idx == 8) pgAlt.Rebuild();
            else if (idx == 9) pgHealth.Rebuild();
        }

        // ---------------------------------------------------------------- tiles

        SessionTile CreateTile(SessionConfig sc)
        {
            var t = new SessionTile(sc, Cfg);
            t.FocusRequested += tl => SetFocus(focused == tl ? null : tl);
            t.EditRequested += EditSession;
            t.DuplicateRequested += DuplicateSession;
            t.Selected += SelectTile;
            t.Changed += tl => Cfg.Save();
            t.AlertRaised += OnAlert;
            t.Dropped += OnDropped;
            t.ReorderRequested += Reorder;
            Tiles.Add(t);
            host.Controls.Add(t);
            return t;
        }

        void SelectTile(SessionTile t)
        {
            SelectedTile = t;
            lblSel.Text = "Selected: " + (t == null ? "–" : t.Config.Name);
        }

        bool Match(SessionTile t)
        {
            switch (filterMode)
            {
                case 1: if (t.StatusLabel != "Dropped") return false; break;   // problems
                case 2: if (t.Online) return false; break;                      // offline
                case 3: if (!t.Online) return false; break;                      // connected
            }
            if (filter.Length == 0) return true;
            string hay = (t.Config.Name + " " + t.Config.Host + " " + t.Config.Note).ToLowerInvariant();
            return hay.Contains(filter);
        }

        // the sessions on the screen in the order they are shown: pinned first, then the chosen sort
        List<SessionTile> OrderedShown()
        {
            IEnumerable<SessionTile> q = Tiles.Where(t => !hidden.Contains(t) && Match(t));
            switch (Cfg.SortMode)
            {
                case 1: q = q.OrderBy(t => t.Config.Name, StringComparer.CurrentCultureIgnoreCase); break;
                case 2: q = q.OrderBy(t => t.StatusLabel == "Dropped" ? 0 : t.StatusLabel == "Offline" ? 1 : 2); break;
                case 3: q = q.OrderByDescending(t => double.IsNaN(t.Cpu) ? -1.0 : t.Cpu); break;
                case 4: q = q.OrderBy(t => double.IsNaN(t.Fps) ? double.MaxValue : t.Fps); break;
            }
            return q.OrderByDescending(t => t.Config.Pinned ? 1 : 0).ToList();   // OrderBy keeps the order inside each group
        }

        // when the filter or the sort result changes because a status changed
        void RearrangeIfChanged()
        {
            var sig = string.Join("|", OrderedShown().Select(t => t.Config.Name));
            if (sig != arrangeSig) ArrangeTiles();
        }

        static string GroupKey(SessionTile t)
        {
            string h = t.Config.GetHost();
            if (h.Length == 0) return "(no host)";
            return NetTools.IsLocal(h) ? "This PC" : h;
        }

        // Manual layout (no TableLayoutPanel): the RDP controls are never
        // moved to another parent, so connections stay stable.
        void ArrangeTiles()
        {
            var shown = OrderedShown();
            arrangeSig = string.Join("|", shown.Select(t => t.Config.Name));
            bool compactNow = Cfg.CompactView && focused == null;
            foreach (var t in Tiles) t.SetCompact(compactNow);
            host.AutoScroll = compactNow;
            foreach (var t in Tiles) if (!shown.Contains(t)) t.Visible = false;
            foreach (var l in groupLabels) l.Visible = false;
            lastGroups = new List<List<SessionTile>>();
            var area = host.ClientSize;

            if (shown.Count == 0) { lblRows.Text = "Rows: 0"; return; }

            if (focused != null && shown.Contains(focused))
            {
                foreach (var t in shown) t.Visible = (t == focused);
                focused.Bounds = new Rectangle(Gap, Gap, Math.Max(10, area.Width - 2 * Gap), Math.Max(10, area.Height - 2 * Gap));
                lblRows.Text = "Rows: 1";
                return;
            }

            int cols = Math.Max(1, Math.Min(Cfg.Columns, shown.Count));
            int cw = Math.Max(10, (area.Width - Gap) / cols);

            if (!Cfg.GroupByHost)
            {
                int rows = (int)Math.Ceiling(shown.Count / (double)cols);
                lblRows.Text = "Rows: " + rows;
                int ch = compactNow ? CompactH + Gap : Math.Max(10, (area.Height - Gap) / rows);
                for (int i = 0; i < shown.Count; i++)
                {
                    shown[i].Visible = true;
                    shown[i].Bounds = new Rectangle(Gap + (i % cols) * cw, Gap + (i / cols) * ch, cw - Gap, ch - Gap);
                }
                return;
            }

            // grouped per PC: every PC gets its own rows with a heading above
            lastGroups = shown.GroupBy(GroupKey).Select(g => g.ToList()).ToList();
            const int headH = 24;
            int totalRows = lastGroups.Sum(g => (int)Math.Ceiling(g.Count / (double)cols));
            lblRows.Text = "Rows: " + totalRows;
            int availH = area.Height - Gap - lastGroups.Count * (headH + 2);
            int chg = compactNow ? CompactH + Gap : Math.Max(10, availH / Math.Max(1, totalRows));

            while (groupLabels.Count < lastGroups.Count)
            {
                var l = new Label
                {
                    AutoSize = false,
                    Height = headH,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = Theme.UI(9.5f, FontStyle.Bold),
                    ForeColor = Theme.FgMuted,
                    BackColor = Theme.Bg
                };
                groupLabels.Add(l);
                host.Controls.Add(l);
            }

            int y = Gap;
            for (int gi = 0; gi < lastGroups.Count; gi++)
            {
                var g = lastGroups[gi];
                groupLabels[gi].SetBounds(Gap, y, Math.Max(10, area.Width - 2 * Gap), headH);
                groupLabels[gi].Visible = true;
                y += headH + 2;
                int rows = (int)Math.Ceiling(g.Count / (double)cols);
                for (int i = 0; i < g.Count; i++)
                {
                    g[i].Visible = true;
                    g[i].Bounds = new Rectangle(Gap + (i % cols) * cw, y + (i / cols) * chg, cw - Gap, chg - Gap);
                }
                y += rows * chg;
            }
            UpdateGroupLabels();
        }

        void UpdateGroupLabels()
        {
            for (int i = 0; i < lastGroups.Count && i < groupLabels.Count; i++)
            {
                var g = lastGroups[i];
                int on = g.Count(t => t.Online);
                groupLabels[i].Text = "▣  " + GroupKey(g[0]) + "   ·   " + on + " / " + g.Count + " connected";
                groupLabels[i].ForeColor = on == g.Count ? Theme.Good : on > 0 ? Theme.Warn : Theme.FgMuted;
            }
        }

        void SetFocus(SessionTile t)
        {
            focused = t;
            btnOverview.Visible = t != null;
            if (t != null) SelectTile(t);
            if (page != 0) ShowPage(0); else ArrangeTiles();
        }

        void UpdateGroupButton()
        {
            btnGroup.BackColor = Cfg.GroupByHost ? Theme.Accent : Theme.Raised;
            btnGroup.ForeColor = Cfg.GroupByHost ? Color.White : Theme.Fg;
        }

        // ---------------------------------------------------------------- managing sessions

        internal void AddSession()
        {
            var sc = new SessionConfig { Name = "Session " + (Cfg.Sessions.Count + 1) };
            using (var dlg = new SessionDialog(sc, false))
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
            PropagateAdmin(sc);
            AddConfigured(sc, sc.AutoConnect);
        }

        void AddConfigured(SessionConfig sc, bool connect)
        {
            Cfg.Sessions.Add(sc);
            AddToActiveProfile(sc.Name);   // otherwise the new session is hidden again after a restart
            var t = CreateTile(sc);
            Cfg.Save();
            RebuildPerf();
            ArrangeTiles();
            SelectTile(t);
            if (connect) t.Connect();
        }

        void AddToActiveProfile(string name)
        {
            if (string.IsNullOrEmpty(Cfg.ActiveProfile)) return;
            var p = Cfg.Profiles.FirstOrDefault(x => x.Name == Cfg.ActiveProfile);
            if (p != null && !p.Sessions.Contains(name)) p.Sessions.Add(name);
        }

        internal void EditSession(SessionTile t)
        {
            DialogResult r;
            string oldName = t.Config.Name;
            using (var dlg = new SessionDialog(t.Config, true))
                r = dlg.ShowDialog(this);

            if (r == DialogResult.Abort) { RemoveTile(t); return; }
            if (r != DialogResult.OK) return;

            if (oldName != t.Config.Name)   // the profiles know the sessions by name
                foreach (var pr in Cfg.Profiles)
                    for (int i = 0; i < pr.Sessions.Count; i++)
                        if (pr.Sessions[i] == oldName) pr.Sessions[i] = t.Config.Name;

            PropagateAdmin(t.Config);
            Cfg.Save();
            t.ApplyConfig();   // new host/login/link apply at the next connection
            SelectTile(t);
            RebuildPerf();
            ArrangeTiles();
        }

        void RemoveTile(SessionTile t)
        {
            if (MessageBox.Show(this, "Session '" + t.Config.Name + "' remove?", "Remove",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            if (focused == t) { focused = null; btnOverview.Visible = false; }
            t.Shutdown();
            host.Controls.Remove(t);
            Tiles.Remove(t);
            hidden.Remove(t);
            Cfg.Sessions.Remove(t.Config);
            if (SelectedTile == t) SelectTile(Tiles.FirstOrDefault());
            t.Dispose();
            Cfg.Save();
            RebuildPerf();
            ArrangeTiles();
        }

        void Reorder(SessionTile src, SessionTile target)
        {
            int i = Tiles.IndexOf(src), j = Tiles.IndexOf(target);
            if (i < 0 || j < 0 || i == j) return;
            Tiles.RemoveAt(i);
            Tiles.Insert(j, src);
            Cfg.Sessions.Remove(src.Config);
            Cfg.Sessions.Insert(j, src.Config);
            Cfg.Save();
            RebuildPerf();
            ArrangeTiles();
        }

        internal Dictionary<string, int> RunningSeconds()
        {
            var d = new Dictionary<string, int>();
            foreach (var t in Tiles)
                if (t.Online && !d.ContainsKey(t.Config.Name)) d[t.Config.Name] = t.ConnectedSeconds;
            return d;
        }

        // ---------------------------------------------------------------- profiles

        void RefreshProfileCombo()
        {
            loadingProfiles = true;
            cmbProfile.Items.Clear();
            cmbProfile.Items.Add("(all sessions)");
            foreach (var p in Cfg.Profiles) cmbProfile.Items.Add(p.Name);
            int idx = Cfg.Profiles.FindIndex(p => p.Name == Cfg.ActiveProfile);
            cmbProfile.SelectedIndex = idx >= 0 ? idx + 1 : 0;
            loadingProfiles = false;
        }

        void UpdateProfileCard()
        {
            lblProfileName.Text = string.IsNullOrEmpty(Cfg.ActiveProfile) ? "All " + Tiles.Count : Cfg.ActiveProfile;
        }

        internal void ApplyProfile(Profile p)
        {
            hidden.Clear();
            if (p != null)
            {
                foreach (var t in Tiles) if (!p.Sessions.Contains(t.Config.Name)) hidden.Add(t);
                Cfg.Columns = Math.Max(1, Math.Min(12, p.Columns));
                Cfg.GroupByHost = p.GroupByHost;
                Cfg.ActiveProfile = p.Name;
                numCols.Value = Cfg.Columns;
            }
            else
            {
                Cfg.ActiveProfile = "";
            }
            focused = null;
            btnOverview.Visible = false;
            UpdateGroupButton();
            RefreshProfileCombo();
            UpdateProfileCard();
            Cfg.Save();
            RefreshSummary();
            if (page == 0) ArrangeTiles();
            if (page == 4) pgProf.RefreshData();
        }

        internal void SaveCurrentAsProfile()
        {
            var names = Tiles.Select(t => t.Config.Name).ToList();
            var pre = Tiles.Where(t => !hidden.Contains(t)).Select(t => t.Config.Name).ToList();
            using (var d = new ProfileDialog(names, pre, Cfg.ActiveProfile ?? ""))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var p = Cfg.Profiles.FirstOrDefault(x => x.Name == d.ProfileName);
                if (p == null) { p = new Profile { Name = d.ProfileName }; Cfg.Profiles.Add(p); }
                p.Sessions = d.SelectedSessions;
                p.Columns = Cfg.Columns;
                p.GroupByHost = Cfg.GroupByHost;
                ApplyProfile(p);
            }
        }

        internal void DeleteProfile(Profile p)
        {
            if (MessageBox.Show(this, "Profile '" + p.Name + "' remove?", "Remove",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            bool active = Cfg.ActiveProfile == p.Name;
            Cfg.Profiles.Remove(p);
            if (active) ApplyProfile(null);
            else { RefreshProfileCombo(); Cfg.Save(); pgProf.RefreshData(); }
        }

        // ---------------------------------------------------------------- network and import

        internal void ScanNetwork()
        {
            using (var d = new ScanDialog(Cfg.Sessions.Select(s => s.GetHost())))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                foreach (var f in d.Selected)
                {
                    string name = !string.IsNullOrEmpty(f.Name) ? f.Name.Split('.')[0] : f.Ip;
                    AddConfigured(new SessionConfig { Name = name, Host = f.Ip, User = d.DefaultUser, Password = d.DefaultPass }, false);
                }
            }
            if (page == 3) pgPc.RefreshData();
        }

        internal void ImportUsers()
        {
            using (var d = new ImportDialog())
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                foreach (var sc in d.Result) AddConfigured(sc, false);
            }
            if (page == 3) pgPc.RefreshData();
        }

        // ---------------------------------------------------------------- Discord bot

        internal string BotState { get { return bot == null ? "off" : bot.State; } }

        internal void ApplyBotSettings()
        {
            if (bot == null) bot = new DiscordBot(this, Cfg);
            if (Cfg.BotEnabled && !string.IsNullOrWhiteSpace(Cfg.BotToken)) bot.Start();
            else bot.Stop();
        }

        internal List<SessionTile> BotTiles() { return Tiles.ToList(); }

        internal List<string> BotProfileNames() { return Cfg.Profiles.Select(p => p.Name).ToList(); }

        internal void BotSave() { Cfg.Save(); }

        internal void BotFocus(SessionTile t) { SetFocus(t); }

        internal string BotProfile(string name)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0 || n.StartsWith("all", StringComparison.OrdinalIgnoreCase))
            {
                ApplyProfile(null);
                return "✅ All sessions are shown.";
            }
            var p = Cfg.Profiles.FirstOrDefault(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                ?? Cfg.Profiles.FirstOrDefault(x => x.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
            if (p == null) return "Profile not found: " + n;
            ApplyProfile(p);
            return "✅ Profile '" + p.Name + "' is active.";
        }

        internal void RestartForTheme()
        {
            Cfg.Save();
            Application.Restart();
        }

        // ---------------------------------------------------------------- help, tutorial, daily report

        internal void ShowPageIndex(int idx)
        {
            if (idx >= 0 && idx <= 8) ShowPage(idx);
        }

        internal void ShowTutorial()
        {
            if (tutorial != null && !tutorial.IsDisposed) { tutorial.Activate(); return; }
            tutorial = new TutorialForm(this);
            tutorial.Show(this);
        }

        // one line per session per minute: CPU, RAM and FPS (the daily report averages these)
        void RecordMetrics()
        {
            var now = DateTime.Now;
            var rows = new List<MetricsService.Sample>();
            foreach (var t in Tiles)
            {
                double cpu = t.Cpu, ram = t.Ram, fps = t.Fps;
                if (double.IsNaN(cpu) && double.IsNaN(ram) && double.IsNaN(fps)) continue;
                rows.Add(new MetricsService.Sample { Time = now, Session = t.Config.Name, Cpu = cpu, Ram = ram, Fps = fps });
            }
            if (rows.Count > 0) Task.Run(() => MetricsService.Write(rows));
        }

        bool CanSendReport()
        {
            return Discord.IsValid(Cfg.WebhookUrl)
                || (bot != null && bot.Running && !string.IsNullOrWhiteSpace(Cfg.BotChannelId));
        }

        // sends the daily report once per day, from the chosen time on
        async void CheckDailyReport()
        {
            try
            {
                if (!Cfg.ReportEnabled || Cfg.DiscordPaused || reportBusy || DateTime.Now < nextReportTry) return;
                TimeSpan at;
                if (!TimeSpan.TryParse(Cfg.ReportTime, CultureInfo.InvariantCulture, out at)) at = new TimeSpan(8, 0, 0);
                var now = DateTime.Now;
                string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (Cfg.LastReportDate == today || now.TimeOfDay < at) return;
                if (!CanSendReport()) return;   // no Discord set up (yet): try again later

                reportBusy = true;
                string res = await SendReport(false);
                reportBusy = false;
                if (res == null || res == DailyReport.NoData)
                {
                    Cfg.LastReportDate = today;
                    Cfg.Save();
                    LogService.Write("", "Report", res == null ? "daily report sent" : "no data yet, daily report skipped", 0);
                }
                else
                {
                    nextReportTry = DateTime.Now.AddMinutes(10);
                    LogService.Write("", "Report", "sending failed: " + res, 0);
                }
            }
            catch (Exception ex)
            {
                reportBusy = false;
                nextReportTry = DateTime.Now.AddMinutes(10);
                ErrorLog.Write("daily report", ex);
            }
        }

        // todaySoFar = false: the whole previous day (the daily report); true: from midnight until now (the test button)
        // returns null if it worked, DailyReport.NoData if there is nothing to report, otherwise the error message
        internal async Task<string> SendReport(bool todaySoFar)
        {
            var now = DateTime.Now;
            DateTime from, to;
            string title, range;
            if (todaySoFar)
            {
                from = DateTime.Today; to = now;
                title = "Report – today so far";
                range = "00:00 – " + now.ToString("HH:mm", CultureInfo.InvariantCulture);
            }
            else
            {
                to = DateTime.Today; from = to.AddDays(-1);
                title = "Daily report – " + from.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture);
                range = "00:00 – 24:00";
            }

            var live = Tiles.Select(t => new DailyReport.Live { Name = t.Config.Name, Online = t.Online, Seconds = t.ConnectedSeconds, FpsReason = t.FpsReason, Hidden = hidden.Contains(t) }).ToList();
            string updLine = pendingUpdate != null && pendingUpdate.Version != Cfg.SkippedVersion
                ? "Update available: v" + pendingUpdate.Version + " (you have v" + AppInfo.Version + ")" : null;
            var parts = await Task.Run(() => DailyReport.Build(from, to, title, range, live, updLine));
            if (parts == null) return DailyReport.NoData;

            foreach (var part in parts)
            {
                string err;
                if (Discord.IsValid(Cfg.WebhookUrl)) err = await Discord.SendAsync(Cfg.WebhookUrl, part, null);
                else if (bot != null && bot.Running && !string.IsNullOrWhiteSpace(Cfg.BotChannelId)) err = await bot.PostChannelMessage(part);
                else return "No Discord webhook or bot channel is set up (see Settings).";
                if (err != null) return err;
            }
            return null;
        }

        // ---------------------------------------------------------------- 1.5.0: duplicate, schedule, health, checklist, settings file

        void DuplicateSession(SessionTile t)
        {
            try
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                var copy = ser.Deserialize<SessionConfig>(ser.Serialize(t.Config));
                string baseName = t.Config.Name + " (copy)";
                string name = baseName;
                int n = 2;
                while (Cfg.Sessions.Any(x => x.Name == name)) name = baseName + " " + n++;
                copy.Name = name;
                copy.AltId = "";
                copy.Pinned = false;
                copy.MutedUntilTicks = 0;
                AddConfigured(copy, false);
            }
            catch (Exception ex) { ErrorLog.Write("duplicate session", ex); }
        }

        // ----- profile on a schedule -----

        internal static string ScheduleText(Profile p)
        {
            if (!p.ScheduleOn) return "–";
            string[] d = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            var on = new List<string>();
            foreach (int i in new[] { 1, 2, 3, 4, 5, 6, 0 }) if ((p.ScheduleDays & (1 << i)) != 0) on.Add(d[i]);
            string days = on.Count == 7 ? "every day" : on.Count == 5 && (p.ScheduleDays & 1) == 0 && (p.ScheduleDays & 64) == 0 ? "Mon–Fri" : string.Join(" ", on);
            return days + "  " + p.ScheduleFrom + "–" + p.ScheduleTo;
        }

        internal void EditSchedule(Profile p)
        {
            using (var d = new ScheduleDialog(p))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Cfg.Save();
                lastScheduled = null;   // look again at the next check
                pgProf.RefreshData();
            }
        }

        static bool InWindow(Profile p, DateTime now)
        {
            TimeSpan from, to;
            if (!TimeSpan.TryParse(p.ScheduleFrom, System.Globalization.CultureInfo.InvariantCulture, out from)) return false;
            if (!TimeSpan.TryParse(p.ScheduleTo, System.Globalization.CultureInfo.InvariantCulture, out to)) return false;
            if (from == to) return false;
            var t = now.TimeOfDay;
            Func<DateTime, bool> dayOn = d => (p.ScheduleDays & (1 << (int)d.DayOfWeek)) != 0;
            if (from < to) return t >= from && t < to && dayOn(now);
            return (t >= from && dayOn(now)) || (t < to && dayOn(now.AddDays(-1)));   // runs over midnight
        }

        // switches to a scheduled profile when its time starts (once; a profile you choose yourself stays)
        void CheckProfileSchedule()
        {
            try
            {
                var now = DateTime.Now;
                Profile hit = null;
                foreach (var p in Cfg.Profiles) if (p.ScheduleOn && InWindow(p, now)) { hit = p; break; }
                string name = hit == null ? "" : hit.Name;
                if (name == lastScheduled) return;
                lastScheduled = name;
                if (hit != null && Cfg.ActiveProfile != hit.Name)
                {
                    ApplyProfile(hit);
                    LogService.Write("", "Profile", "the schedule switched to profile '" + hit.Name + "'", 0);
                }
            }
            catch (Exception ex) { ErrorLog.Write("profile schedule", ex); }
        }

        // ----- edit several sessions at once -----

        internal void EditSeveral()
        {
            if (Tiles.Count == 0) return;
            using (var d = new MultiEditDialog(Cfg.Sessions.ToList()))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                foreach (var t in Tiles) t.ApplyConfig();
                Cfg.Save();
                RebuildPerf();
                RefreshSummary();
                pgPc.RefreshData();
                ArrangeTiles();
            }
        }

        // ----- health page and getting-started card -----

        internal void SetSound(bool on) { swSound.Checked = on; }

        internal void SetNotifications(bool on)
        {
            swAlerts.Checked = on;
        }

        internal void ShowUpdate()
        {
            if (pendingUpdate != null) ShowUpdateDialog(pendingUpdate);
        }

        void UpdateHealthBadge()
        {
            try
            {
                int n = HealthPage.Collect(this).Count(i => i.Level >= 1);
                if (nav.Count > 9) nav[9].Text = "✚  Health" + (n > 0 ? "  (" + n + ")" : "");
            }
            catch { }
        }

        void BuildChecklist()
        {
            checkHost.Dock = DockStyle.Top;
            checkHost.Height = 84;
            checkHost.BackColor = Theme.Bg;
            checkHost.Padding = new Padding(8, 8, 8, 0);
            var card = new RoundedPanel { Dock = DockStyle.Fill };
            card.Controls.Add(new Label { Text = "Getting started", Font = Theme.UI(10.5f, FontStyle.Bold), ForeColor = Theme.Fg, AutoSize = true, Location = new Point(16, 10), BackColor = Color.Transparent });
            lblCheck.AutoSize = false;
            lblCheck.Location = new Point(16, 38);
            lblCheck.Size = new Size(900, 28);
            lblCheck.ForeColor = Theme.FgMuted;
            lblCheck.BackColor = Color.Transparent;
            lblCheck.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(lblCheck);
            var hide = UI.Btn(card, "Hide", 0, 8, 80, false);
            hide.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            card.Resize += (s, e) => hide.Left = card.Width - hide.Width - 14;
            hide.Click += (s, e) => { Cfg.ChecklistHidden = true; Cfg.Save(); checkHost.Visible = false; };
            checkHost.Controls.Add(card);
            checkHost.Visible = !Cfg.ChecklistHidden;
        }

        void UpdateChecklist()
        {
            if (!checkHost.Visible) return;
            if (Tiles.Any(t => t.Online)) Cfg.EverConnected = true;
            bool a = Cfg.Sessions.Count > 0;
            bool b = Cfg.EverConnected;
            bool c = Discord.IsValid(Cfg.WebhookUrl) || (Cfg.BotEnabled && !string.IsNullOrWhiteSpace(Cfg.BotToken));
            bool d = Cfg.Profiles.Count > 0;
            Func<bool, string, string> m = (ok, text) => (ok ? "✓  " : "○  ") + text;
            lblCheck.Text = m(a, "Add a session") + "      " + m(b, "Connect to it") + "      " + m(c, "Set up Discord (webhook or bot)") + "      " + m(d, "Save a profile (optional)");
            if (a && b && c)   // the three main steps are done: this card is no longer needed
            {
                Cfg.ChecklistHidden = true;
                Cfg.Save();
                checkHost.Visible = false;
            }
        }

        // ----- export and import of the settings (without passwords and tokens) -----

        internal void ExportSettings()
        {
            using (var sfd = new SaveFileDialog { Title = "Export settings", Filter = "Settings file|*.json", FileName = "rdp-panel-settings-" + DateTime.Now.ToString("yyyyMMdd") + ".json" })
            {
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var copy = ser.Deserialize<AppConfig>(ser.Serialize(Cfg));
                    copy.WebhookEnc = "";
                    copy.BotTokenEnc = "";
                    copy.Alts = new List<AltAccount>();   // the alt accounts have their own backup
                    foreach (var sc in copy.Sessions) { sc.PasswordEnc = ""; sc.AdminPassEnc = ""; sc.AltId = ""; }
                    System.IO.File.WriteAllText(sfd.FileName, ser.Serialize(copy), new System.Text.UTF8Encoding(true));
                    MessageBox.Show(this, "Saved: " + sfd.FileName + "\r\nPasswords, the webhook link and the bot token are not in this file.", "Export settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, "Export failed: " + ex.Message, "Export settings", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        internal void ImportSettings()
        {
            using (var ofd = new OpenFileDialog { Title = "Import settings", Filter = "Settings file|*.json|All files|*.*" })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var imp = ser.Deserialize<AppConfig>(System.IO.File.ReadAllText(ofd.FileName, System.Text.Encoding.UTF8));
                    if (imp == null || imp.Sessions == null)
                    {
                        MessageBox.Show(this, "This is not a settings file of RDP Panel.", "Import settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (MessageBox.Show(this, "Import " + imp.Sessions.Count + " session(s), " + imp.Profiles.Count + " profile(s) and the settings?\r\nSessions with the same name are updated (their passwords stay as they are here), the others are added.\r\nYour webhook link and bot token are not changed.",
                        "Import settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                    var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                    // simple settings (numbers, yes/no, text), not the secrets and not the lists
                    foreach (var f in typeof(AppConfig).GetFields(flags))
                    {
                        if (f.Name.EndsWith("Enc") || f.Name == "ActiveProfile" || f.Name == "LastSeenVersion" || f.Name == "UpdateMsgSentFor" || f.Name == "SkippedVersion" || f.Name == "TutorialDone") continue;
                        if (!(f.FieldType.IsPrimitive || f.FieldType == typeof(string))) continue;
                        f.SetValue(Cfg, f.GetValue(imp));
                    }
                    // sessions: update by name, add the new ones
                    int added = 0, updated = 0;
                    foreach (var isc in imp.Sessions)
                    {
                        var mine = Cfg.Sessions.FirstOrDefault(x => x.Name == isc.Name);
                        if (mine == null)
                        {
                            isc.PasswordEnc = "";
                            isc.AdminPassEnc = "";
                            isc.AltId = "";
                            Cfg.Sessions.Add(isc);
                            CreateTile(isc);
                            added++;
                        }
                        else
                        {
                            foreach (var f in typeof(SessionConfig).GetFields(flags))
                            {
                                if (f.Name == "PasswordEnc" || f.Name == "AdminPassEnc" || f.Name == "AltId") continue;
                                f.SetValue(mine, f.GetValue(isc));
                            }
                            updated++;
                        }
                    }
                    foreach (var ip in imp.Profiles)
                    {
                        var mp = Cfg.Profiles.FirstOrDefault(x => x.Name == ip.Name);
                        if (mp == null) Cfg.Profiles.Add(ip);
                        else
                        {
                            mp.Sessions = ip.Sessions; mp.Columns = ip.Columns; mp.GroupByHost = ip.GroupByHost;
                            mp.ScheduleOn = ip.ScheduleOn; mp.ScheduleFrom = ip.ScheduleFrom; mp.ScheduleTo = ip.ScheduleTo; mp.ScheduleDays = ip.ScheduleDays;
                        }
                    }
                    foreach (var t in Tiles) t.ApplyConfig();
                    Cfg.Save();
                    RebuildPerf();
                    RefreshSummary();
                    pgPc.RefreshData();
                    pgProf.RefreshData();
                    ArrangeTiles();
                    MessageBox.Show(this, added + " session(s) added, " + updated + " updated.\r\nSome settings (like the theme) apply after you restart the panel.", "Import settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, "Import failed: " + ex.Message, "Import settings", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        // ---------------------------------------------------------------- tray icon with the status color

        static System.Drawing.Icon MakeDot(Color c)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var br = new SolidBrush(c)) g.FillEllipse(br, 3, 3, 26, 26);
                    using (var pen = new Pen(Color.FromArgb(190, 0, 0, 0), 2f)) g.DrawEllipse(pen, 3, 3, 26, 26);
                }
                return System.Drawing.Icon.FromHandle(bmp.GetHicon());   // the 3 icons stay alive as long as the panel runs
            }
        }

        // green = all sessions on the screens connected, yellow = some, red = none
        void UpdateTrayIcon()
        {
            try
            {
                var list = Tiles.Where(t => !hidden.Contains(t) && !string.IsNullOrWhiteSpace(t.Config.Host)).ToList();
                int st = list.Count == 0 ? -1 : list.All(t => t.Online) ? 0 : list.Any(t => t.Online) ? 1 : 2;
                if (st == trayState) return;
                trayState = st;
                if (st < 0) { notify.Icon = SystemIcons.Application; return; }
                if (trayIcons == null) trayIcons = new[] { MakeDot(Theme.Good), MakeDot(Theme.Warn), MakeDot(Theme.Bad) };
                notify.Icon = trayIcons[st];
            }
            catch (Exception ex) { ErrorLog.Write("tray icon", ex); }
        }

        // ---------------------------------------------------------------- rules for Discord messages

        readonly List<DateTime> discordSent = new List<DateTime>();
        int discordHeldBack;
        DateTime discordHeldSince;

        bool InQuietHours(TimeSpan now)
        {
            TimeSpan from, to;
            if (!TimeSpan.TryParse(Cfg.QuietFrom, CultureInfo.InvariantCulture, out from)) from = TimeSpan.Zero;
            if (!TimeSpan.TryParse(Cfg.QuietTo, CultureInfo.InvariantCulture, out to)) to = new TimeSpan(7, 0, 0);
            if (from == to) return false;
            return from < to ? (now >= from && now < to) : (now >= from || now < to);   // e.g. 22:00 - 07:00 runs over midnight
        }

        // asks permission for one automatic Discord message: pause, quiet hours and the limit per hour.
        // critical = a drop or a PC that stops responding (a CPU/RAM warning and the overview are not critical).
        // The daily report, the bot commands and the test buttons do not use this.
        internal bool DiscordAllowed(bool critical)
        {
            if (Cfg.DiscordPaused) return false;
            if (Cfg.QuietHoursOn && InQuietHours(DateTime.Now.TimeOfDay) && !(critical && Cfg.QuietMode == 1)) return false;
            if (Cfg.MaxMsgsPerHour > 0)
            {
                var now = DateTime.Now;
                discordSent.RemoveAll(x => (now - x).TotalMinutes >= 60);
                if (discordSent.Count >= Cfg.MaxMsgsPerHour)
                {
                    if (discordHeldBack == 0) discordHeldSince = now;
                    discordHeldBack++;
                    return false;
                }
                discordSent.Add(now);
                if (discordHeldBack > 0)
                {
                    int n = discordHeldBack;
                    discordHeldBack = 0;
                    SendDiscordNote("⏸ " + n + " message" + (n == 1 ? " was" : "s were") + " held back since " + discordHeldSince.ToString("HH:mm")
                        + " (limit: " + Cfg.MaxMsgsPerHour + " per hour). See the log in the panel for everything that happened.");
                }
            }
            return true;
        }

        // a short plain message that does not count for the limit
        async void SendDiscordNote(string text)
        {
            try
            {
                string err;
                if (Discord.IsValid(Cfg.WebhookUrl)) err = await Discord.SendAsync(Cfg.WebhookUrl, text, null);
                else if (bot != null && bot.Running && !string.IsNullOrWhiteSpace(Cfg.BotChannelId)) err = await bot.PostChannelMessage(text);
                else return;
                if (err != null) LogService.Write("", "Discord", "sending failed: " + err, 0);
            }
            catch (Exception ex) { ErrorLog.Write("discord note", ex); }
        }

        // one line for the Settings page: what the rules do right now
        internal string DiscordRulesState()
        {
            if (Cfg.DiscordPaused) return "All automatic Discord messages are paused.";
            if (Cfg.QuietHoursOn && InQuietHours(DateTime.Now.TimeOfDay))
                return "Quiet hours until " + Cfg.QuietTo + (Cfg.QuietMode == 1 ? " (only drops and PCs that stop responding are sent)." : " (no messages).");
            if (Cfg.MaxMsgsPerHour > 0)
            {
                var now = DateTime.Now;
                int used = discordSent.Count(x => (now - x).TotalMinutes < 60);
                return "Messages in the last hour: " + used + " of " + Cfg.MaxMsgsPerHour + (discordHeldBack > 0 ? "  –  " + discordHeldBack + " held back." : ".");
            }
            return "Discord messages are on.";
        }

        // ---------------------------------------------------------------- notifications and dashboard

        void OnAlert(SessionTile t, string msg)
        {
            lblWarn.Text = "⚠ Warning: " + msg;
            warnAt = DateTime.Now;
            RefreshSummary();
            if (t.IsMuted) return;                   // this session is muted
            if (DateTime.Now < MuteUntil) return;   // muted with /quiet
            // a CPU/RAM warning is less important than a session that is gone or a PC that does not respond
            bool critical = msg.IndexOf("CPU ", StringComparison.Ordinal) < 0 || msg.IndexOf("RAM ", StringComparison.Ordinal) < 0;
            bool sendToDiscord = (bot != null && bot.Running && !string.IsNullOrWhiteSpace(Cfg.BotChannelId))
                || (Cfg.DiscordAlerts && Discord.IsValid(Cfg.WebhookUrl));
            if (sendToDiscord && DiscordAllowed(critical))
            {
                if (bot != null && bot.Running && !string.IsNullOrWhiteSpace(Cfg.BotChannelId))
                    bot.PostAlert(t.Config.Name, msg);   // notification with buttons in the bot channel
                else
                    Discord.Post(Cfg.WebhookUrl, t.Config.Name, "⚠ " + msg, null);
            }
            if (!Cfg.AlertsEnabled) return;
            try { notify.ShowBalloonTip(8000, "RDP Panel", msg, ToolTipIcon.Warning); } catch { }
            if (Cfg.AlertSound) System.Media.SystemSounds.Exclamation.Play();
        }

        // ---------------------------------------------------------------- screenshot of the whole panel

        // a capture of the whole panel (like the overview)
        // 1. fully visible: copy from the screen (this always worked)
        // 2. covered by another window, or in background mode: let Windows draw the window itself and put the own
        //    picture of every session on top (the RDP pictures are often black in a printed window)
        Bitmap CapturePanelNow()
        {
            try
            {
                if (WindowState == FormWindowState.Minimized || !Visible) return null;
                if (page != 0 || focused != null) return null;   // only the overview with all tiles
                var r = WinUtil.VisibleBounds(this);
                if (r.Width < 200 || r.Height < 200) return null;
                if (!inBackground && !WinUtil.IsCovered(Handle, r))
                {
                    var bmp = new Bitmap(r.Width, r.Height);
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    return bmp;
                }
                return CapturePanelPrinted(r);
            }
            catch { return null; }
        }

        Bitmap CapturePanelPrinted(Rectangle visible)
        {
            var wr = WinCapture.WindowRectangle(Handle);
            if (wr.Width < 200 || wr.Height < 200) return null;
            Bitmap full = null;
            if (!WinCapture.Print(Handle, ref full, wr.Width, wr.Height, false) || WinCapture.IsBlank(full))
            {
                if (full != null) full.Dispose();
                return null;
            }
            using (var g = Graphics.FromImage(full))
                foreach (var t in Tiles.ToArray()) t.PaintPictureInto(g, wr.Location);
            var crop = Rectangle.Intersect(new Rectangle(visible.X - wr.X, visible.Y - wr.Y, visible.Width, visible.Height),
                new Rectangle(0, 0, full.Width, full.Height));
            if (crop.Width < 200 || crop.Height < 200) return full;
            var result = full.Clone(crop, full.PixelFormat);
            full.Dispose();
            return result;
        }

        void RefreshPanelCache()
        {
            var b = CapturePanelNow();
            if (b == null) return;
            lastPanelJpeg = Discord.ToJpeg(b, 80);
            lastPanelAt = DateTime.Now;
            b.Dispose();
        }

        // a fresh capture if possible, otherwise the last good one (with the time)
        internal Tuple<byte[], DateTime> PanelShot()
        {
            RefreshPanelCache();
            return Tuple.Create(lastPanelJpeg, lastPanelAt);
        }

        string SavePanelFile(byte[] jpeg)
        {
            try
            {
                System.IO.Directory.CreateDirectory(LogService.ShotDir);
                string path = System.IO.Path.Combine(LogService.ShotDir, "panel_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg");
                System.IO.File.WriteAllBytes(path, jpeg);
                return path;
            }
            catch { return null; }
        }

        async void ManualPanelShot()
        {
            var shot = PanelShot();
            if (shot.Item1 == null)
            {
                MessageBox.Show(this, "No screenshot of the panel possible. The panel must be on the Overview and not minimised.\r\n\r\nTip: Settings > 'Test screenshot' shows which method works on this PC.",
                    "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string path = SavePanelFile(shot.Item1);
            string msg = path != null ? "Saved:\r\n" + path : "Saving failed.";
            if (Discord.IsValid(Cfg.WebhookUrl))
            {
                var files = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("panel.jpg", shot.Item1) };
                string err = await Discord.SendAsync(Cfg.WebhookUrl, "📸 Panel " + DateTime.Now.ToString("HH:mm:ss") + WinUtil.AgeText(shot.Item2), files);
                msg += err == null ? "\r\nSent to Discord." : "\r\nDiscord failed: " + err;
            }
            MessageBox.Show(this, msg, "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // unexpected drop: keep the panel as it just was and send it (with the last image of the session) to Discord
        void OnDropped(SessionTile t, string reason)
        {
            var shot = lastPanelJpeg != null ? Tuple.Create(lastPanelJpeg, lastPanelAt) : null;
            if (Cfg.ScreenshotOnDrop && shot != null)
            {
                string path = SavePanelFile(shot.Item1);
                if (path != null) LogService.Write(t.Config.Name, "Screenshot", path, 0);
            }
            if (t.IsMuted || !Cfg.DiscordOnDrop || !Discord.IsValid(Cfg.WebhookUrl) || !DiscordAllowed(true)) return;

            var files = new List<KeyValuePair<string, byte[]>>();
            string age = "";
            if (shot != null && Cfg.DiscordShotMode == 0)
            {
                files.Add(new KeyValuePair<string, byte[]>("panel.jpg", shot.Item1));
                age = WinUtil.AgeText(shot.Item2);
            }
            var own = t.LastShotJpeg();
            if (own != null && (files.Count == 0 || Cfg.DiscordShotMode == 1))
                files.Add(new KeyValuePair<string, byte[]>(t.SafeFileName + ".jpg", own));
            Discord.Post(Cfg.WebhookUrl, t.Config.Name,
                "⚠ **" + t.Config.Name + "** (" + t.Config.Host + ") dropped at " + DateTime.Now.ToString("HH:mm:ss") + ".\n" + reason + age, files);
        }

        // a message with the overview (whole panel or one image per session)
        void SendDiscordOverview()
        {
            if (!DiscordAllowed(false)) return;
            var files = new List<KeyValuePair<string, byte[]>>();
            string age = "";
            if (Cfg.DiscordShotMode == 0)
            {
                var shot = PanelShot();
                if (shot.Item1 != null)
                {
                    files.Add(new KeyValuePair<string, byte[]>("panel.jpg", shot.Item1));
                    age = WinUtil.AgeText(shot.Item2);
                }
            }
            if (files.Count == 0)
            {
                foreach (var t in Tiles.Where(x => x.Online).Take(10))
                {
                    var b = t.Snapshot();
                    if (b == null) continue;
                    files.Add(new KeyValuePair<string, byte[]>(t.SafeFileName + ".jpg", Discord.ToJpeg(b, 70)));
                    b.Dispose();
                }
            }
            string msg = "📊 **Overview " + DateTime.Now.ToString("HH:mm") + "** – " + Tiles.Count(x => x.Online) + " / " + Tiles.Count + " connected" + age;
            Discord.Post(Cfg.WebhookUrl, "Overview", msg, files);
        }

        // the admin account applies to all sessions on the same PC that do not have one yet
        void PropagateAdmin(SessionConfig src)
        {
            if (string.IsNullOrWhiteSpace(src.AdminUser)) return;
            string h = src.GetHost();
            foreach (var other in Cfg.Sessions)
            {
                if (other == src || !string.IsNullOrWhiteSpace(other.AdminUser)) continue;
                if (!other.GetHost().Equals(h, StringComparison.OrdinalIgnoreCase)) continue;
                other.AdminUser = src.AdminUser;
                other.AdminPassword = src.AdminPassword;
            }
        }

        void UpdateTop()
        {
            int on = Tiles.Count(t => t.Online && !hidden.Contains(t));
            lblCount.Text = on + " / " + Tiles.Count + " connected ●";
            lblCount.ForeColor = Tiles.Count > 0 && on == Tiles.Count ? Theme.Good : on > 0 ? Theme.Warn : Theme.FgMuted;

            var pings = Tiles.Where(t => t.PingMs >= 0).Select(t => t.PingMs).ToList();
            if (Tiles.Any(t => t.Unreachable)) { lblNet.Text = "Network: Problems"; lblNet.ForeColor = Theme.Bad; }
            else if (pings.Count == 0) { lblNet.Text = "Network: –"; lblNet.ForeColor = Theme.FgMuted; }
            else
            {
                double avg = pings.Average();
                lblNet.Text = "Network: " + (avg < 50 ? "Good" : avg < 150 ? "Fair" : "Poor");
                lblNet.ForeColor = avg < 50 ? Theme.Good : avg < 150 ? Theme.Warn : Theme.Bad;
            }
        }

        void UpdateFooter()
        {
            int on = Tiles.Count(t => t.Online && !hidden.Contains(t));
            lblFootL.Text = "RDP Panel v" + AppInfo.Version + "   |   " + on + " sessions active   |   Total connected time today: " + UI.Dur(totalToday);
            if (lblWarn.Text.Length > 0 && (DateTime.Now - warnAt).TotalMinutes > 10) lblWarn.Text = "";
            if (swSound.Checked != Cfg.AlertSound)   // changed in Settings
            {
                syncingSound = true;
                swSound.Checked = Cfg.AlertSound;
                lblSound.Text = Cfg.AlertSound ? "Sound on" : "Sound off";
                syncingSound = false;
                soundAssert = 40;
            }
        }

        // "Sound off" mutes everything the panel plays in the Windows volume mixer (the sound of the sessions too);
        // sound that starts later gets the same state, so it is repeated now and then
        void ApplySound()
        {
            if (!Cfg.AlertSound) AppAudio.SetMuted(true);
            else if (soundAssert > 0) { soundAssert--; AppAudio.SetMuted(false); }
        }

        // ----- remove a session from one place (the session itself keeps running) -----
        // place: 0 = Monitoring page and performance overview, 1 = Daily summary, 2 = PC management

        internal int RemovedCount(int place)
        {
            return Cfg.Sessions.Count(sc => place == 0 ? sc.HideInMonitoring : place == 1 ? sc.HideInDailySummary : sc.HideInPcList);
        }

        internal void RemoveFrom(SessionTile t, int place)
        {
            if (place == 0) t.Config.HideInMonitoring = true;
            else if (place == 1) t.Config.HideInDailySummary = true;
            else t.Config.HideInPcList = true;
            Cfg.Save();
            RefreshRemoved();
        }

        internal void RestoreAll(int place)
        {
            foreach (var sc in Cfg.Sessions)
            {
                if (place == 0) sc.HideInMonitoring = false;
                else if (place == 1) sc.HideInDailySummary = false;
                else sc.HideInPcList = false;
            }
            Cfg.Save();
            RefreshRemoved();
        }

        void RefreshRemoved()
        {
            RebuildPerf();
            RefreshSummary();
            pgMon.Rebuild();
            pgPc.RefreshData();
        }

        // right-click menu of a list: remove the session under the mouse, and show the removed ones again
        internal ContextMenuStrip MakePlaceMenu(int place, string placeName, ListView lv)
        {
            var m = Theme.MakeMenu();
            var rm = new ToolStripMenuItem("");
            var back = new ToolStripMenuItem("");
            rm.Click += (s, e) => { var tl = rm.Tag as SessionTile; if (tl != null) RemoveFrom(tl, place); };
            back.Click += (s, e) => RestoreAll(place);
            m.Items.Add(rm);
            m.Items.Add(back);
            m.Opening += (s, e) =>
            {
                var pt = lv.PointToClient(Cursor.Position);
                var li = lv.GetItemAt(pt.X, pt.Y);
                var tile = li == null ? null : li.Tag as SessionTile;
                rm.Tag = tile;
                rm.Visible = tile != null;
                if (tile != null) rm.Text = "Remove '" + tile.Config.Name + "' from " + placeName;
                int n = RemovedCount(place);
                back.Visible = n > 0;
                back.Text = "Show " + n + " removed session" + (n == 1 ? "" : "s") + " again";
                if (tile == null && n == 0) e.Cancel = true;
            };
            return m;
        }

        // the same for the cards of the performance overview
        ContextMenuStrip MakeCardMenu(SessionTile tile)
        {
            var m = Theme.MakeMenu();
            m.Items.Add("Remove '" + tile.Config.Name + "' from the performance overview", null, (s, e) => RemoveFrom(tile, 0));
            var back = new ToolStripMenuItem("");
            back.Click += (s, e) => RestoreAll(0);
            m.Items.Add(back);
            m.Opening += (s, e) =>
            {
                int n = RemovedCount(0);
                back.Visible = n > 0;
                back.Text = "Show " + n + " removed session" + (n == 1 ? "" : "s") + " again";
            };
            return m;
        }

        // a message from the panel itself (not from a session): balloon, and a sound unless the sound is off
        internal void ShowBalloon(string title, string text)
        {
            try
            {
                notify.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
                if (Cfg.AlertSound) System.Media.SystemSounds.Asterisk.Play();
            }
            catch { }
        }

        // show one session large (from the Alt accounts tab and from the search window)
        internal void OpenTile(SessionTile t)
        {
            SetFocus(t);
        }

        // Ctrl+K: sessions, pages, alt accounts and a few actions in one list
        void ShowQuickSearch()
        {
            var l = new List<Tuple<string, Action>>();
            foreach (var t in Tiles.ToList())
            {
                var tile = t;
                l.Add(new Tuple<string, Action>("Session: " + t.Config.Name + "   (" + t.Config.Host + ")", () => OpenTile(tile)));
            }
            string[] pages = { "Overview", "Monitoring", "Log", "PC management", "Profiles", "Settings", "Help", "Credits", "Alt accounts", "Health" };
            for (int i = 0; i < pages.Length; i++)
            {
                int idx = i;
                l.Add(new Tuple<string, Action>("Go to: " + pages[i], () => ShowPage(idx)));
            }
            foreach (var a in Cfg.Alts.ToList())
            {
                var alt = a;
                l.Add(new Tuple<string, Action>("Alt account: " + a.Name + (a.Kind.Length > 0 ? "   (" + a.Kind + ")" : ""), () => { ShowPage(8); pgAlt.ShowAlt(alt); }));
            }
            l.Add(new Tuple<string, Action>("Action: " + (Cfg.CompactView ? "Normal view (not compact)" : "Compact view"), () => { Cfg.CompactView = !Cfg.CompactView; Cfg.Save(); UpdateCompactButton(); ArrangeTiles(); }));
            l.Add(new Tuple<string, Action>("Action: Open the activity window", ShowActivity));
            l.Add(new Tuple<string, Action>("Action: " + (Cfg.AlertSound ? "Turn sound off" : "Turn sound on"), () => { swSound.Checked = !swSound.Checked; }));
            l.Add(new Tuple<string, Action>("Action: " + (Cfg.DiscordPaused ? "Resume Discord messages" : "Pause all Discord messages"), () => { Cfg.DiscordPaused = !Cfg.DiscordPaused; Cfg.Save(); }));
            l.Add(new Tuple<string, Action>("Action: Show all sessions (clear the filter)", () => { cmbFilter.SelectedIndex = 0; txtSearch.Text = ""; }));
            using (var f = new QuickSearchForm(l)) f.ShowDialog(this);
        }

        void RebuildPerf()
        {
            perfFlow.SuspendLayout();
            perfFlow.Controls.Clear();
            perf.Clear();
            if (perfFlow.ContextMenuStrip == null)   // empty place of the overview: show the removed sessions again
            {
                var back = new ToolStripMenuItem("");
                back.Click += (s, e) => RestoreAll(0);
                var fm = Theme.MakeMenu();
                fm.Items.Add(back);
                fm.Opening += (s, e) =>
                {
                    int n = RemovedCount(0);
                    back.Text = "Show " + n + " removed session" + (n == 1 ? "" : "s") + " again";
                    if (n == 0) e.Cancel = true;
                };
                perfFlow.ContextMenuStrip = fm;
            }
            foreach (var t in Tiles.Where(x => !x.Config.HideInMonitoring))
            {
                var p = new Panel { Width = 164, Height = 120, Margin = new Padding(0, 0, 8, 0), BackColor = Theme.Surface };
                var spark = new Spark { Dock = DockStyle.Fill, BackColor = Theme.Surface };
                spark.Lines.Add(new Spark.Series { V = t.CpuHist, C = Theme.Accent });
                spark.Lines.Add(new Spark.Series { V = t.RamHist, C = Color.FromArgb(160, 110, 255) });
                var vals = new Label { Dock = DockStyle.Top, Height = 18, ForeColor = Theme.FgMuted, Font = Theme.UI(8f), TextAlign = ContentAlignment.MiddleLeft };
                var name = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Fg, Font = Theme.UI(9f, FontStyle.Bold), Text = t.Config.Name, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
                var tile = t;
                var rm = new Label { Dock = DockStyle.Right, Width = 22, Text = "✕", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.FgMuted, Cursor = Cursors.Hand };
                rm.MouseEnter += (s, e) => rm.ForeColor = Theme.Bad;
                rm.MouseLeave += (s, e) => rm.ForeColor = Theme.FgMuted;
                rm.Click += (s, e) => RemoveFrom(tile, 0);
                tips.SetToolTip(rm, "Remove from the performance overview");
                var head = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = Color.Transparent };
                head.Controls.Add(name);
                head.Controls.Add(rm);
                p.Controls.Add(spark);
                p.Controls.Add(vals);
                p.Controls.Add(head);
                var cardMenu = MakeCardMenu(t);
                p.ContextMenuStrip = cardMenu;
                spark.ContextMenuStrip = cardMenu;
                vals.ContextMenuStrip = cardMenu;
                name.ContextMenuStrip = cardMenu;
                head.ContextMenuStrip = cardMenu;
                perfFlow.Controls.Add(p);
                perf.Add(new PerfItem { Tile = t, Vals = vals });
            }
            perfFlow.ResumeLayout();
            UpdatePerf();
            UpdateProfileCard();
        }

        void UpdatePerf()
        {
            foreach (var it in perf)
            {
                var t = it.Tile;
                it.Vals.Text = "CPU: " + (double.IsNaN(t.Cpu) ? "–" : t.Cpu.ToString("0") + "%")
                    + "  RAM: " + (double.IsNaN(t.Ram) ? "–" : t.Ram.ToString("0") + "%")
                    + (Cfg.ShowFps ? "  FPS: " + (double.IsNaN(t.Fps) ? "–" : t.Fps.ToString("0")) : "");
            }
            foreach (Control c in perfFlow.Controls)
                foreach (Control s in c.Controls) if (s is Spark) s.Invalidate();
        }

        void RefreshSummary()
        {
            var today = DateTime.Today;
            var allEvents = LogService.ReadAll();
            var te = allEvents.Where(e => e.Time.Date == today).ToList();
            var yesterdayEvents = allEvents.Where(e => e.Time.Date == today.AddDays(-1)).ToList();
            cardSum.Title.Text = "Daily summary (" + today.ToString("dd-MM-yyyy") + ")";

            int total = 0;
            lvSum.BeginUpdate();
            lvSum.Items.Clear();
            int hiddenCount = 0, removedCount = 0;
            foreach (var t in Tiles)
            {
                // only sessions that are on the screens (active profile); the rest is counted below the list
                if (hidden.Contains(t)) { hiddenCount++; continue; }
                if (t.Config.HideInDailySummary) { removedCount++; continue; }
                int secs = te.Where(e => e.Session == t.Config.Name && e.Event == "Disconnected").Sum(e => e.Seconds) + t.ConnectedSeconds;
                total += secs;
                var it = new ListViewItem(t.Config.Name) { UseItemStyleForSubItems = false, Tag = t };
                it.SubItems.Add(UI.Dur(secs));
                string st = t.StatusLabel;
                var sub = it.SubItems.Add(st);
                sub.ForeColor = st == "Good" ? Theme.Good : st == "Dropped" ? Theme.Warn : Theme.FgMuted;
                int ySecs = yesterdayEvents.Where(e => e.Session == t.Config.Name && e.Event == "Disconnected").Sum(e => e.Seconds);
                var ysub = it.SubItems.Add(ySecs > 0 ? UI.Dur(ySecs) : "–");
                ysub.ForeColor = Theme.FgMuted;
                lvSum.Items.Add(it);
            }
            if (hiddenCount > 0)
            {
                var hi = new ListViewItem("+" + hiddenCount + " hidden by profile" + (string.IsNullOrEmpty(Cfg.ActiveProfile) ? "" : " '" + Cfg.ActiveProfile + "'")) { UseItemStyleForSubItems = false };
                hi.ForeColor = Theme.FgMuted;
                hi.SubItems.Add("");
                hi.SubItems.Add("");
                hi.SubItems.Add("");
                lvSum.Items.Add(hi);
            }
            if (removedCount > 0)
            {
                var ri = new ListViewItem(removedCount + " removed (right-click to show again)") { UseItemStyleForSubItems = false };
                ri.ForeColor = Theme.FgMuted;
                ri.SubItems.Add("");
                ri.SubItems.Add("");
                ri.SubItems.Add("");
                lvSum.Items.Add(ri);
            }
            lvSum.EndUpdate();
            totalToday = total;

            lvLog.BeginUpdate();
            lvLog.Items.Clear();
            foreach (var e in te.OrderByDescending(x => x.Time).Take(5))
            {
                var it = new ListViewItem(e.Time.ToString("HH:mm"));
                it.SubItems.Add(e.Session + " " + e.Event.ToLowerInvariant() + (e.Detail.Length > 0 ? " – " + e.Detail : ""));
                lvLog.Items.Add(it);
            }
            lvLog.EndUpdate();
        }

        // ---------------------------------------------------------------- window

        void RestoreWindow()
        {
            if (Cfg.WinW >= 600 && Cfg.WinH >= 400)
            {
                var r = new Rectangle(Cfg.WinX, Cfg.WinY, Cfg.WinW, Cfg.WinH);
                if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r)))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = r;
                }
            }
            if (Cfg.WinMax) WindowState = FormWindowState.Maximized;
        }

        void ToggleFullscreen()
        {
            fullscreen = !fullscreen;
            if (fullscreen)
            {
                prevState = WindowState;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
                top.Visible = false;
                side.Visible = false;
                footer.Visible = false;
                strip.Visible = false;
                peek.Start();   // mouse at the top edge = show the bar temporarily
            }
            else
            {
                peek.Stop();
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = prevState;
                top.Visible = true;
                side.Visible = true;
                footer.Visible = true;
                strip.Visible = true;
            }
            ArrangeTiles();
        }

        // ---------------------------------------------------------------- background mode

        // Windows does not draw a minimised window, so the sessions would freeze (and screenshots/FPS would be black).
        // In background mode the minimise button therefore does something else: the window stays open but becomes
        // invisible, click-through and gets no taskbar button. The tray icon brings it back.
        protected override void WndProc(ref Message m)
        {
            const int WM_SYSCOMMAND = 0x0112, SC_MINIMIZE = 0xF020;
            if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt64() & 0xFFF0) == SC_MINIMIZE && Cfg.BackgroundMode && !inBackground)
            {
                EnterBackground();
                return;
            }
            base.WndProc(ref m);
        }

        void EnterBackground()
        {
            if (inBackground || !IsHandleCreated) return;
            try
            {
                if (fullscreen) ToggleFullscreen();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                inBackground = true;
                ShowWindow(Handle, 0);   // hide for a moment so the taskbar button disappears
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                ex = (ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
                SetWindowLong(Handle, GWL_EXSTYLE, ex);
                SetLayeredWindowAttributes(Handle, 0, 1, 2);   // 1/255 visible = invisible, but still drawn by Windows
                ShowWindow(Handle, 8);   // SW_SHOWNA: show without taking the focus
                notify.Text = "RDP Panel (background)";
                if (!backgroundHinted)
                {
                    backgroundHinted = true;
                    try { notify.ShowBalloonTip(5000, "RDP Panel", "The panel keeps running in the background. Click this icon to open it.", ToolTipIcon.Info); } catch { }
                }
            }
            catch (Exception ex)
            {
                inBackground = false;
                ErrorLog.Write("background", ex);
            }
        }

        void LeaveBackground()
        {
            if (!inBackground) return;
            try
            {
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                SetLayeredWindowAttributes(Handle, 0, 255, 2);
                ShowWindow(Handle, 0);
                ex = (ex & ~(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW)) | WS_EX_APPWINDOW;
                SetWindowLong(Handle, GWL_EXSTYLE, ex);
                ShowWindow(Handle, 5);   // SW_SHOW
            }
            catch (Exception e) { ErrorLog.Write("background", e); }
            inBackground = false;
            notify.Text = "RDP Panel";
            Invalidate(true);
        }

        void ShowFromTray()
        {
            LeaveBackground();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            BringToFront();
        }

        // ---------------------------------------------------------------- test screenshot

        // tries both ways of taking a picture (screen copy and printing the window) and says which one works on this PC
        internal void TestScreenshot()
        {
            bool wasBackground = inBackground;
            var sb = new StringBuilder();
            string dir = LogService.ShotDir;
            try { Directory.CreateDirectory(dir); } catch { }

            sb.AppendLine("Windows: " + Environment.OSVersion.VersionString);
            sb.AppendLine("Panel: " + (inBackground ? "in background mode" : WindowState == FormWindowState.Minimized ? "minimised" : "open")
                + ", page: " + (page == 0 ? "Overview" : "other page"));
            sb.AppendLine();

            int n = 0;
            foreach (var t in Tiles.ToArray())
            {
                if (!t.Online) continue;
                sb.AppendLine(t.TestCapture(dir));
                n++;
            }
            if (n == 0) sb.AppendLine("No connected session to test. Connect a session first.\r\n");

            var pb = CapturePanelNow();
            sb.AppendLine("Whole panel: " + (pb != null ? "OK" : "no picture (the panel must be on the Overview)"));
            if (pb != null)
            {
                try { pb.Save(Path.Combine(dir, "test_panel.png"), System.Drawing.Imaging.ImageFormat.Png); sb.AppendLine("   saved: " + Path.Combine(dir, "test_panel.png")); }
                catch (Exception ex) { sb.AppendLine("   saving failed: " + ex.Message); }
                pb.Dispose();
            }
            sb.AppendLine();
            sb.AppendLine("Tip: run this test once with the panel fully visible and once from the tray menu while another window");
            sb.AppendLine("covers the panel (or in background mode). 'window print: WORKS' means screenshots and FPS also work when covered.");
            try { File.WriteAllText(Path.Combine(dir, "test_screenshot.txt"), sb.ToString(), Encoding.UTF8); } catch { }

            // a message box on top of an invisible window would be invisible too
            if (wasBackground) ShowFromTray();
            MessageBox.Show(this, sb.ToString(), "Test screenshot", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (wasBackground) EnterBackground();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(Handle);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (autoStartLaunch && Cfg.StartMinimized && Cfg.BackgroundMode) EnterBackground();
            ArrangeTiles();
            RefreshSummary();
            ApplyBotSettings();
            var pruneTask = Task.Run(() => MetricsService.Prune());
            bool freshInstall = !Cfg.TutorialDone;   // no tutorial yet = the very first start of the app
            if (!Cfg.TutorialDone && !autoStartLaunch)
            {
                Cfg.TutorialDone = true;   // only once
                Cfg.Save();
                ShowTutorial();
            }
            if (!freshInstall && Cfg.UpdateMsgSentFor != AppInfo.Version && ReleaseNotes.IsNewer(AppInfo.Version, Cfg.LastSeenVersion))
                updatedFromVersion = Cfg.LastSeenVersion ?? "";
            ShowWhatsNewIfUpdated(freshInstall);
            // after Windows starts, give the network a moment
            if (autoStartLaunch) await Task.Delay(8000);
            // connect staggered, otherwise you get a CPU spike at startup
            foreach (var t in Tiles.ToArray())
            {
                if (t.Config.AutoConnect) { t.Connect(); await Task.Delay(1500); }
            }
        }

        // text for /ping and the like: a newer version that the user has not skipped, or null
        internal string UpdateAvailableText
        {
            get
            {
                var u = pendingUpdate;
                if (u == null || u.Version == Cfg.SkippedVersion) return null;
                return "v" + u.Version + (u.FilesReady ? "" : " (still being built)");
            }
        }

        // tells the bot channel once that the panel was updated (waits until the bot is connected)
        async void SendUpdatedMessage()
        {
            if (updatedFromVersion == null) return;
            try
            {
                if (!Cfg.BotEnabled || string.IsNullOrWhiteSpace(Cfg.BotChannelId))
                {
                    updatedFromVersion = null;   // no bot channel: nothing to tell
                    Cfg.UpdateMsgSentFor = AppInfo.Version;
                    Cfg.Save();
                    return;
                }
                if (bot == null || !bot.Running) return;   // try again at the next tick
                string from = updatedFromVersion;
                updatedFromVersion = null;
                int on = Tiles.Count(t => t.Online);
                string msg = "🔄 **RDP Panel updated to v" + AppInfo.Version + "**" + (from.Length > 0 ? " (from v" + from + ")" : "")
                    + "\n🖥 " + on + " of " + Tiles.Count + " sessions connected right now";
                string err = await bot.PostChannelMessage(msg);
                if (err == null) { Cfg.UpdateMsgSentFor = AppInfo.Version; Cfg.Save(); }
                else { updatedFromVersion = from; LogService.Write("", "Discord", "update message failed: " + err, 0); }
            }
            catch (Exception ex) { ErrorLog.Write("update message", ex); }
        }

        // "What's new" once after an update; a fresh install gets the tutorial instead
        void ShowWhatsNewIfUpdated(bool freshInstall)
        {
            try
            {
                if (Cfg.LastSeenVersion == AppInfo.Version) return;
                if (freshInstall)
                {
                    Cfg.LastSeenVersion = AppInfo.Version;
                    Cfg.Save();
                    return;
                }
                if (autoStartLaunch) return;   // started with Windows: nobody is looking, show it at the next manual start
                bool newer = ReleaseNotes.IsNewer(AppInfo.Version, Cfg.LastSeenVersion);
                Cfg.LastSeenVersion = AppInfo.Version;
                Cfg.Save();
                if (!newer) return;
                var notes = ReleaseNotes.Get(AppInfo.Version);
                if (notes == null) return;
                var dlg = new WhatsNewDialog(AppInfo.Version, notes);
                dlg.Show(this);
            }
            catch (Exception ex) { ErrorLog.Write("whats new", ex); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            clock.Stop();
            peek.Stop();
            if (tutorial != null && !tutorial.IsDisposed) tutorial.Close();
            if (bot != null) bot.Stop();
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            Cfg.WinX = b.X; Cfg.WinY = b.Y; Cfg.WinW = b.Width; Cfg.WinH = b.Height;
            Cfg.WinMax = WindowState == FormWindowState.Maximized;
            Cfg.Save();
            foreach (var t in Tiles) t.Shutdown();
            notify.Visible = false;
            notify.Dispose();
            base.OnFormClosing(e);
        }

        // Shortcuts only work when the panel itself has focus (not inside an RDP session)
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11) { ToggleFullscreen(); return true; }
            if (keyData == Keys.F1) { ShowPage(6); return true; }
            if (keyData == (Keys.Control | Keys.K)) { ShowQuickSearch(); return true; }
            if (keyData == (Keys.Control | Keys.N)) { AddSession(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { ShowPage(0); txtSearch.Focus(); txtSearch.SelectAll(); return true; }
            if (keyData == Keys.F5 && SelectedTile != null) { SelectedTile.Reconnect(); return true; }
            if (keyData == (Keys.Control | Keys.M)) { swSound.Checked = !swSound.Checked; return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.C)) { Cfg.CompactView = !Cfg.CompactView; Cfg.Save(); UpdateCompactButton(); ArrangeTiles(); return true; }
            if (keyData == (Keys.Control | Keys.L)) { ShowPage(2); return true; }
            if (keyData == (Keys.Control | Keys.H)) { ShowPage(9); return true; }
            if (keyData == Keys.Escape && fullscreen) { ToggleFullscreen(); return true; }
            if (keyData == (Keys.Control | Keys.D0)) { SetFocus(null); return true; }
            if (keyData >= (Keys.Control | Keys.D1) && keyData <= (Keys.Control | Keys.D9))
            {
                int i = (int)(keyData & Keys.KeyCode) - (int)Keys.D1;
                if (i < Tiles.Count) SetFocus(Tiles[i]);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
