using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Forms;
using AxMSTSCLib;
using MSTSCLib;

namespace RdpPanel
{
    public class SessionTile : Panel
    {
        enum St { Offline, NotSet, Connecting, Online, Retrying, Stopped, Error }

        public SessionConfig Config { get; private set; }
        readonly AppConfig app;

        public event Action<SessionTile> EditRequested;
        public event Action<SessionTile> FocusRequested;
        public event Action<SessionTile> Selected;
        public event Action<SessionTile> Changed;
        public event Action<SessionTile, string> AlertRaised;
        public event Action<SessionTile, string> Dropped;   // unexpected drop (with the reason)
        public event Action<SessionTile, SessionTile> ReorderRequested;
        public event Action<SessionTile> LayoutChanged;   // pinned or unpinned: the overview has to be arranged again

        // ---- header ----
        readonly Panel header = new Panel();
        readonly Label dot = L(11f, FontStyle.Regular, Theme.FgMuted, ContentAlignment.MiddleCenter);
        readonly Label lblName = L(10.5f, FontStyle.Bold, Theme.Fg, ContentAlignment.MiddleLeft);
        readonly Label lblHost = L(9f, FontStyle.Regular, Theme.FgMuted, ContentAlignment.MiddleLeft);
        readonly Button btnEditS = Icon("✎");
        readonly Chip chipNote = new Chip();
        readonly Label lblPing = L(9f, FontStyle.Regular, Theme.FgMuted, ContentAlignment.MiddleRight);
        readonly Label lblFps = L(9f, FontStyle.Regular, Theme.FgMuted, ContentAlignment.MiddleRight);
        readonly Label lblCompact = L(9f, FontStyle.Regular, Theme.FgMuted, ContentAlignment.MiddleRight);   // status, CPU and RAM in the compact view
        readonly Button btnExpand = Icon("⤢");
        readonly Button btnMenu = Icon("⋮");

        // ---- content ----
        readonly Panel body = new Panel();
        readonly Panel screen = new Panel();
        readonly Panel overlay = new Panel();
        readonly Label lblOverlay = L(11f, FontStyle.Regular, Theme.FgMuted, ContentAlignment.MiddleCenter);
        readonly Panel info = new Panel();
        readonly Chip chipStatus = new Chip();
        readonly Label lblDur = L(9.5f, FontStyle.Regular, Theme.FgMuted, ContentAlignment.MiddleRight);
        readonly Label lblSub = L(8.5f, FontStyle.Regular, Theme.FgMuted, ContentAlignment.MiddleLeft);
        readonly Label lblCpuName = L(9.5f, FontStyle.Regular, Theme.Fg, ContentAlignment.MiddleLeft);
        readonly Label lblCpuVal = L(9.5f, FontStyle.Bold, Theme.Fg, ContentAlignment.MiddleRight);
        readonly Label lblRamName = L(9.5f, FontStyle.Regular, Theme.Fg, ContentAlignment.MiddleLeft);
        readonly Label lblRamVal = L(9.5f, FontStyle.Bold, Theme.Fg, ContentAlignment.MiddleRight);
        readonly Spark spCpu = new Spark();
        readonly Spark spRam = new Spark();
        readonly Panel pnlOn = new Panel();
        readonly Panel pnlOff = new Panel();
        readonly Button btnGroot = Btn("⤢  Large", false);
        readonly Button btnLink = Btn("Edit link", false);
        readonly Button btnRestart = Btn("↻ Restart", false);
        readonly Button btnShut = Btn("Shut down", false);
        readonly Button btnReconnect = Btn("Connect", true);
        readonly Button btnWol = Btn("Wake-on-LAN", false);

        readonly ToolTip tips = new ToolTip();
        readonly Timer retryTimer = new Timer();
        readonly ContextMenuStrip menu = Theme.MakeMenu();

        public readonly List<float> CpuHist = new List<float>();
        public readonly List<float> RamHist = new List<float>();
        public double Cpu = double.NaN, Ram = double.NaN, FreeMb = double.NaN;
        public int PingMs = -1;
        readonly FpsSampler.Target fpsTarget = new FpsSampler.Target();
        string lastFpsText = "";
        string lastFpsTip = "";
        public bool Unreachable;
        public string StatsError = "";
        Bitmap lastShot;
        DateTime lastShotAt = DateTime.MinValue;
        string lastStatsTip = "";

        AxMsRdpClient9NotSafeForScripting rdp;
        St state = St.Offline;
        string stateText = "";
        string lastReason = "";
        bool userDisconnected = true;
        bool reconnectPending;
        bool connectWhenReady;   // Connect() was asked while the control had no window handle yet
        int retryCount;
        DateTime? connectedAt;
        DateTime? lostAt;
        DateTime? lastAttempt;
        bool lostAlerted, unreachAlerted, highAlerted;
        int unreachableCount, highCount;
        bool headerDirty = true;
        bool compact;
        string lastCompactText = "";
        DateTime? frozenSince;
        bool frozenAlerted;
        string lastPingText = "";
        Point dragStart = Point.Empty;

        const int MaxRetries = 20; // prevents a wrong password from locking your account
        const int MaxHist = 360;

        static Label L(float size, FontStyle style, Color color, ContentAlignment align)
        {
            return new Label
            {
                AutoSize = false,
                Font = Theme.UI(size, style),
                ForeColor = color,
                TextAlign = align,
                BackColor = Color.Transparent
            };
        }

        static Button Icon(string glyph)
        {
            var b = new Button
            {
                Text = glyph,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Symbol", 10f),
                ForeColor = Theme.FgMuted,
                BackColor = Theme.Surface,
                Cursor = Cursors.Hand,
                TabStop = false,
                Size = new Size(28, 28)
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.Raised;
            return b;
        }

        static Button Btn(string text, bool accent)
        {
            var b = Theme.MakeButton(text, accent);
            b.AutoSize = false;
            b.Margin = Padding.Empty;
            return b;
        }

        public bool Online { get { return IsConnected; } }

        public int ConnectedSeconds
        {
            get { return connectedAt.HasValue ? (int)(DateTime.Now - connectedAt.Value).TotalSeconds : 0; }
        }

        // "Good", "Dropped" or "Offline"
        public string StatusLabel
        {
            get
            {
                if (IsConnected) return "Good";
                if (state == St.Retrying || state == St.Stopped || state == St.Error || Unreachable) return "Dropped";
                return "Offline";
            }
        }

        bool IsConnected
        {
            get
            {
                // The ActiveX object only exists once the control has a window handle
                // (e.g. not while the panel is started hidden/minimised). Reading Connected
                // before that throws InvalidActiveXStateException.
                try { return rdp != null && rdp.IsHandleCreated && rdp.Connected != 0; }
                catch (InvalidOperationException) { return false; }
            }
        }

        // notifications of this session are muted (the warning in the panel and the log still work)
        public bool IsMuted { get { return Config.MutedUntilTicks > DateTime.Now.Ticks; } }

        // the CPU/RAM limits: own value of this session, or the general one from Settings
        int CpuLimit { get { return Config.CpuWarn > 0 ? Config.CpuWarn : app.CpuWarn; } }
        int RamLimit { get { return Config.RamWarn > 0 ? Config.RamWarn : app.RamWarn; } }

        string NameText { get { return (Config.Pinned ? "★ " : "") + Config.Name; } }

        void MuteFor(TimeSpan span)
        {
            Config.MutedUntilTicks = span <= TimeSpan.Zero ? 0 : DateTime.Now.Add(span).Ticks;
            RefreshInfo();
            if (Changed != null) Changed(this);
        }

        // what this session is doing right now (for the Activity window)
        public string ActivityState
        {
            get
            {
                if (IsConnected) return "Connected";
                switch (state)
                {
                    case St.Connecting: return "Connecting";
                    case St.Retrying: return "Waiting to reconnect";
                    case St.Stopped: return "Stopped";
                    case St.Error: return "Error";
                    case St.NotSet: return "Not set up";
                    default: return userDisconnected ? "Disconnected by you" : "Offline";
                }
            }
        }

        public string ActivityDetail
        {
            get
            {
                var l = new List<string>();
                if (state == St.Retrying || state == St.Stopped || state == St.Error)
                {
                    if (!string.IsNullOrEmpty(stateText)) l.Add(stateText);
                    if (state == St.Retrying && retryCount > 0) l.Add("attempt " + retryCount + " of " + MaxRetries);
                }
                else if (state == St.Offline && lastAttempt.HasValue) l.Add("last attempt " + Ago(DateTime.Now - lastAttempt.Value));
                if (IsConnected && connectedAt.HasValue) l.Add("connected for " + FormatSpan(DateTime.Now - connectedAt.Value));
                if (IsConnected && frozenSince.HasValue) l.Add("picture unchanged for " + FormatSpan(DateTime.Now - frozenSince.Value));
                if (!IsConnected && Unreachable) l.Add("PC does not respond");
                if (!IsConnected && !string.IsNullOrEmpty(lastReason)) l.Add("last reason: " + lastReason);
                if (IsMuted) l.Add("muted until " + new DateTime(Config.MutedUntilTicks).ToString("HH:mm"));
                return string.Join("  ·  ", l);
            }
        }

        // compact view: only the header (status, CPU and RAM in it); the screen is hidden
        public void SetCompact(bool on)
        {
            if (compact == on) return;
            compact = on;
            body.Visible = !on;
            headerDirty = true;
            RefreshInfo();
        }

        // short description of the connection state (for the diagnostic file)
        public string DebugState
        {
            get
            {
                string r = state.ToString();
                if (!string.IsNullOrEmpty(stateText)) r += " (" + stateText + ")";
                if (!string.IsNullOrEmpty(lastReason)) r += ", last reason: " + lastReason;
                if (retryCount > 0) r += ", retries: " + retryCount;
                return r;
            }
        }

        // screen updates per second of this session (NaN = unknown: off, not connected or tile not fully visible)
        public double Fps
        {
            get { return app.ShowFps ? fpsTarget.Fps : double.NaN; }
        }

        // why there is no FPS number (empty when there is one)
        public string FpsReason
        {
            get { return !app.ShowFps ? "FPS display is off" : fpsTarget.Reason; }
        }

        // how the picture was read: "screen" (strips of the screen) or "window" (the window itself, when covered)
        public string FpsMethod
        {
            get { return fpsTarget.Method; }
        }

        public SessionTile(SessionConfig cfg, AppConfig app)
        {
            Config = cfg;
            this.app = app;
            Padding = new Padding(1);   // the border gets the color of the session/status
            BackColor = Theme.Line;

            // header
            header.Dock = DockStyle.Top;
            header.Height = 38;
            header.BackColor = Theme.Surface;
            dot.Text = "●";
            lblPing.Text = "";
            header.Controls.AddRange(new Control[] { dot, lblName, lblHost, btnEditS, chipNote, lblFps, lblPing, lblCompact, btnExpand, btnMenu });
            lblCompact.Visible = false;
            header.Resize += (s, e) => LayoutHeader();

            // screen with overlay
            screen.Dock = DockStyle.Fill;
            screen.BackColor = Theme.Well;
            overlay.Dock = DockStyle.Fill;
            overlay.BackColor = Theme.Well;
            lblOverlay.Dock = DockStyle.Fill;
            overlay.Controls.Add(lblOverlay);
            screen.Controls.Add(overlay);

            // info panel
            info.Dock = DockStyle.Right;
            info.Width = 236;
            info.BackColor = Theme.Surface;
            lblCpuName.Text = "CPU";
            lblRamName.Text = "RAM";
            spCpu.Lines.Add(new Spark.Series { V = CpuHist, C = Theme.Accent });
            spRam.Lines.Add(new Spark.Series { V = RamHist, C = Color.FromArgb(160, 110, 255) });
            btnShut.ForeColor = Theme.Bad;
            btnWol.BackColor = Color.FromArgb(104, 80, 190);
            btnWol.ForeColor = Color.White;
            btnWol.FlatAppearance.MouseOverBackColor = Color.FromArgb(124, 100, 210);
            pnlOn.Controls.AddRange(new Control[] { btnGroot, btnLink, btnRestart, btnShut });
            pnlOff.Controls.AddRange(new Control[] { btnReconnect, btnWol });
            info.Controls.AddRange(new Control[]
            {
                chipStatus, lblDur, lblSub, lblCpuName, lblCpuVal, spCpu, lblRamName, lblRamVal, spRam, pnlOn, pnlOff
            });
            info.Resize += (s, e) => LayoutInfo();

            body.Dock = DockStyle.Fill;
            body.BackColor = Theme.Surface;
            body.Controls.Add(screen);
            body.Controls.Add(info);     // higher index number: docked first (right)
            Controls.Add(body);
            Controls.Add(header);        // header is docked first (top)

            // menu (⋮ and right-click on the header)
            menu.Items.Add("Show large / back", null, (s, e) => RaiseFocus());
            menu.Items.Add("Connect / reconnect", null, (s, e) => Reconnect());
            menu.Items.Add("Disconnect", null, (s, e) => Disconnect());
            menu.Items.Add(new ToolStripSeparator());
            var pinItem = new ToolStripMenuItem("Pin to top", null, (s, e) =>
            {
                Config.Pinned = !Config.Pinned;
                headerDirty = true;
                RefreshInfo();
                if (Changed != null) Changed(this);
                if (LayoutChanged != null) LayoutChanged(this);
            });
            var muteItem = new ToolStripMenuItem("Mute notifications of this session");
            muteItem.DropDownItems.Add("For 1 hour", null, (s, e) => MuteFor(TimeSpan.FromHours(1)));
            muteItem.DropDownItems.Add("For 8 hours", null, (s, e) => MuteFor(TimeSpan.FromHours(8)));
            muteItem.DropDownItems.Add("For 24 hours", null, (s, e) => MuteFor(TimeSpan.FromHours(24)));
            muteItem.DropDownItems.Add("Unmute", null, (s, e) => MuteFor(TimeSpan.Zero));
            menu.Items.Add(pinItem);
            menu.Items.Add(muteItem);
            menu.Opening += (s, e) =>
            {
                pinItem.Text = Config.Pinned ? "Unpin from top" : "Pin to top";
                muteItem.Text = IsMuted ? "Muted until " + new DateTime(Config.MutedUntilTicks).ToString("dd-MM HH:mm") + " (change)" : "Mute notifications of this session";
            };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Edit link…", null, (s, e) => EditLink());
            menu.Items.Add("Copy link", null, (s, e) => CopyLink());
            menu.Items.Add("Take screenshot", null, (s, e) => ManualShot());
            menu.Items.Add("Send screenshot to Discord", null, (s, e) => ManualDiscord());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Turn PC on (Wake-on-LAN)", null, (s, e) => DoWake());
            menu.Items.Add("Restart PC…", null, (s, e) => DoPower(true));
            menu.Items.Add("Shut down PC…", null, (s, e) => DoPower(false));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Settings…", null, (s, e) => { if (EditRequested != null) EditRequested(this); });

            tips.SetToolTip(btnEditS, "Settings of this session");
            tips.SetToolTip(btnExpand, "Show only this screen large (and back)");
            tips.SetToolTip(btnMenu, "More actions");
            tips.SetToolTip(lblFps, "FPS = how many times per second the picture of this session changes (max. about 10).\r\nIdle desktop = 0. This is not the frame rate inside the game.");
            FpsSampler.Add(fpsTarget);

            btnEditS.Click += (s, e) => { if (EditRequested != null) EditRequested(this); };
            btnExpand.Click += (s, e) => RaiseFocus();
            btnMenu.Click += (s, e) => menu.Show(btnMenu, new Point(0, btnMenu.Height));
            btnGroot.Click += (s, e) => RaiseFocus();
            btnLink.Click += (s, e) => EditLink();
            btnRestart.Click += (s, e) => DoPower(true);
            btnShut.Click += (s, e) => DoPower(false);
            btnReconnect.Click += (s, e) => Reconnect();
            btnWol.Click += (s, e) => DoWake();

            foreach (var c in new Control[] { header, dot, lblName, lblHost, lblFps, lblPing, lblCompact })
                HookHeader(c);
            info.MouseDown += (s, e) => { if (Selected != null) Selected(this); };
            info.MouseDoubleClick += (s, e) => RaiseFocus();

            retryTimer.Tick += (s, e) =>
            {
                retryTimer.Stop();
                Connect();
            };

            ApplyConfig();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            info.Visible = Width >= 560;   // too narrow: only show the screen, actions are in the ⋮ menu
        }

        // ---------- header and layout ----------

        void HookHeader(Control c)
        {
            c.ContextMenuStrip = menu;
            c.AllowDrop = true;
            c.DragEnter += (s, e) =>
            {
                e.Effect = e.Data.GetDataPresent(typeof(SessionTile)) ? DragDropEffects.Move : DragDropEffects.None;
            };
            c.DragDrop += (s, e) =>
            {
                var src = e.Data.GetData(typeof(SessionTile)) as SessionTile;
                if (src != null && src != this && ReorderRequested != null) ReorderRequested(src, this);
            };
            c.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) dragStart = e.Location;
                if (Selected != null) Selected(this);
            };
            c.MouseMove += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || dragStart == Point.Empty) return;
                if (Math.Abs(e.X - dragStart.X) > 8 || Math.Abs(e.Y - dragStart.Y) > 8)
                {
                    dragStart = Point.Empty;
                    DoDragDrop(this, DragDropEffects.Move);   // drag onto the header of another tile to move
                }
            };
            c.MouseUp += (s, e) => { dragStart = Point.Empty; };
            c.DoubleClick += (s, e) => RaiseFocus();   // double-click on the header: show this session large (and back)
        }

        void LayoutHeader()
        {
            int h = header.Height;
            int x = 12;
            dot.SetBounds(x, 0, 18, h); x += 22;
            int w = TextRenderer.MeasureText(lblName.Text, lblName.Font).Width + 8;
            lblName.SetBounds(x, 0, w, h); x += w + 4;
            w = TextRenderer.MeasureText(lblHost.Text, lblHost.Font).Width + 8;
            lblHost.SetBounds(x, 0, w, h); x += w;
            btnEditS.SetBounds(x, 5, 28, 28); x += 34;
            chipNote.FitWidth();
            chipNote.SetBounds(x, 7, chipNote.Width, 24);

            int r = header.Width - 8;
            btnMenu.SetBounds(r - 28, 5, 28, 28); r -= 32;
            btnExpand.SetBounds(r - 28, 5, 28, 28); r -= 34;
            int pw = TextRenderer.MeasureText(lblPing.Text, lblPing.Font).Width + 10;
            lblPing.SetBounds(r - pw, 0, pw, h);

            int limit = lblPing.Left;
            if (lblFps.Text.Length > 0)
            {
                int fw = TextRenderer.MeasureText(lblFps.Text, lblFps.Font).Width + 10;
                lblFps.SetBounds(lblPing.Left - fw - 6, 0, fw, h);
                lblFps.Visible = lblFps.Left > lblName.Right + 6;   // no room: hide it
                if (lblFps.Visible) limit = lblFps.Left;
            }
            else lblFps.Visible = false;

            if (compact && lblCompact.Text.Length > 0)
            {
                int cw = TextRenderer.MeasureText(lblCompact.Text, lblCompact.Font).Width + 10;
                lblCompact.SetBounds(limit - cw - 6, 0, cw, h);
                lblCompact.Visible = lblCompact.Left > lblName.Right + 6;
                if (lblCompact.Visible) limit = lblCompact.Left;
            }
            else lblCompact.Visible = false;

            chipNote.Visible = chipNote.Text.Length > 0 && chipNote.Right < limit - 6;
            lblHost.Visible = lblHost.Right < limit - 6;
            btnEditS.Visible = btnEditS.Right < limit - 2;
            headerDirty = false;
        }

        void LayoutInfo()
        {
            int W = Math.Max(60, info.Width - 24);
            chipStatus.FitWidth();
            chipStatus.SetBounds(12, 10, Math.Min(W - 70, chipStatus.Width), 24);
            lblDur.SetBounds(12 + W - 80, 10, 80, 24);
            lblSub.SetBounds(12, 38, W, 18);

            bool tall = info.Height >= 280;
            int y = 62;
            lblCpuName.SetBounds(12, y, 60, 18);
            lblCpuVal.SetBounds(12 + W - 70, y, 70, 18);
            spCpu.Visible = tall;
            if (tall) { spCpu.SetBounds(12, y + 20, W, 30); y += 54; } else y += 22;
            lblRamName.SetBounds(12, y, 60, 18);
            lblRamVal.SetBounds(12 + W - 70, y, 70, 18);
            spRam.Visible = tall;
            if (tall) { spRam.SetBounds(12, y + 20, W, 30); y += 54; } else y += 22;

            pnlOn.SetBounds(0, y + 4, info.Width, 92);
            pnlOff.SetBounds(0, y + 4, info.Width, 66);
            int half = (W - 6) / 2;
            btnGroot.SetBounds(12, 0, W, 26);
            btnLink.SetBounds(12, 30, W, 26);
            btnRestart.SetBounds(12, 60, half, 26);
            btnShut.SetBounds(12 + half + 6, 60, half, 26);
            btnReconnect.SetBounds(12, 0, W, 28);
            btnWol.SetBounds(12, 34, W, 28);
        }

        // ---------- connecting ----------

        void EnsureRdp()
        {
            if (rdp != null) return;

            rdp = new AxMsRdpClient9NotSafeForScripting();
            ((ISupportInitialize)rdp).BeginInit();
            rdp.Dock = DockStyle.Fill;
            ((ISupportInitialize)rdp).EndInit();
            screen.Controls.Add(rdp);
            rdp.BringToFront();

            // when the control gets its handle later (window becomes visible), finish the pending connect
            rdp.HandleCreated += (s, e) =>
            {
                if (!connectWhenReady) return;
                connectWhenReady = false;
                BeginInvoke(new Action(Connect));
            };

            rdp.OnConnected += (s, e) =>
            {
                retryCount = 0;
                connectedAt = DateTime.Now;
                lostAt = null;
                lostAlerted = false;
                lastReason = "";
                state = St.Online;
                stateText = "";
                LogService.Write(Config.Name, "Connected", Config.GetHost(), 0);
                FillMac();
                RefreshInfo();
            };

            rdp.OnDisconnected += (s, e) =>
            {
                bool was = connectedAt.HasValue;
                int secs = was ? (int)(DateTime.Now - connectedAt.Value).TotalSeconds : 0;
                connectedAt = null;
                string reason = DiscReasons.Describe(e.discReason);
                if (was) LogService.Write(Config.Name, "Disconnected", reason, secs);
                else if (!userDisconnected) LogService.Write(Config.Name, "Failed", reason, 0);

                // on an unexpected drop: keep the last screenshot
                if (was && !userDisconnected && !reconnectPending && app.ScreenshotOnDrop && lastShot != null)
                {
                    string shot = SaveShot(lastShot);
                    if (shot != null) LogService.Write(Config.Name, "Screenshot", shot, 0);
                }

                // the panel handles the rest (panel screenshot, Discord)
                if (was && !userDisconnected && !reconnectPending && Dropped != null) Dropped(this, reason);

                if (reconnectPending)
                {
                    reconnectPending = false;
                    state = St.Connecting;
                    BeginInvoke(new Action(Connect));
                    RefreshInfo();
                    return;
                }

                lastReason = reason;
                if (userDisconnected)
                {
                    state = St.Offline;
                    lostAt = null;
                    lastReason = "";
                }
                else if (++retryCount > MaxRetries)
                {
                    userDisconnected = true;
                    state = St.Stopped;
                    stateText = "Stopped after " + MaxRetries + " attempts";
                    Raise("Session '" + Config.Name + "' stopped reconnecting: " + reason);
                }
                else
                {
                    int secs2 = Math.Min(60, 5 * retryCount);
                    state = St.Retrying;
                    stateText = "Reconnecting in " + secs2 + "s";
                    if (!lostAt.HasValue) lostAt = DateTime.Now;
                    retryTimer.Interval = secs2 * 1000;
                    retryTimer.Start();
                }
                RefreshInfo();
            };
        }

        public void Connect()
        {
            if (string.IsNullOrWhiteSpace(Config.Host))
            {
                state = St.NotSet;
                RefreshInfo();
                return;
            }

            try
            {
                EnsureRdp();
                if (!rdp.IsHandleCreated) rdp.CreateControl();
                if (!rdp.IsHandleCreated)
                {
                    // no window handle yet (panel hidden): connect as soon as it gets one
                    connectWhenReady = true;
                    state = St.Connecting;
                    RefreshInfo();
                    return;
                }
                connectWhenReady = false;
                if (rdp.Connected != 0) return;

                userDisconnected = false;
                retryTimer.Stop();
                lastAttempt = DateTime.Now;

                rdp.Server = Config.GetHost();
                rdp.UserName = Config.User;
                ((IMsTscNonScriptable)rdp.GetOcx()).ClearTextPassword = Config.Password;
                rdp.DesktopWidth = Config.Width;
                rdp.DesktopHeight = Config.Height;
                rdp.ColorDepth = 16; // light: less bandwidth and CPU

                var a = rdp.AdvancedSettings9;
                a.RDPPort = Config.GetPort();
                a.SmartSizing = true;               // scale the screen in the tile
                a.Compress = 1;
                a.BitmapPeristence = 1;
                a.EnableCredSspSupport = true;
                a.AuthenticationLevel = 0;          // no certificate prompt on your own network
                a.PerformanceFlags = 0x1 | 0x2 | 0x4 | 0x8 | 0x20; // wallpaper, animations, themes off
                a.RedirectClipboard = true;
                a.allowBackgroundInput = 1;
                a.keepAliveInterval = 60000;

                // Only for a NEW session: open the link and then simply start the desktop shell.
                // When reconnecting to an existing session this is ignored (it is already running).
                if (!string.IsNullOrWhiteSpace(Config.LaunchLink))
                {
                    rdp.SecuredSettings.StartProgram =
                        "cmd.exe /c start \"\" \"" + Config.LaunchLink.Trim() + "\" & explorer.exe";
                }

                state = St.Connecting;
                RefreshInfo();
                rdp.Connect();
            }
            catch (Exception ex)
            {
                state = St.Error;
                stateText = ex.Message;
                RefreshInfo();
            }
        }

        public void Disconnect()
        {
            userDisconnected = true;
            reconnectPending = false;
            connectWhenReady = false;
            retryTimer.Stop();
            retryCount = 0;
            lostAt = null;
            lostAlerted = false;
            if (connectedAt.HasValue)
            {
                LogService.Write(Config.Name, "Disconnected", DiscReasons.ByUser, (int)(DateTime.Now - connectedAt.Value).TotalSeconds);
                connectedAt = null;
            }
            try { if (IsConnected) rdp.Disconnect(); } catch { }
            state = string.IsNullOrWhiteSpace(Config.Host) ? St.NotSet : St.Offline;
            lastReason = "";
            RefreshInfo();
        }

        public void Reconnect()
        {
            if (IsConnected)
            {
                reconnectPending = true;
                userDisconnected = true;
                retryTimer.Stop();
                if (connectedAt.HasValue)
                {
                    LogService.Write(Config.Name, "Disconnected", "Reconnect", (int)(DateTime.Now - connectedAt.Value).TotalSeconds);
                    connectedAt = null;
                }
                try { rdp.Disconnect(); } catch { reconnectPending = false; }
            }
            else
            {
                retryCount = 0;
                Connect();
            }
        }

        public void Shutdown()
        {
            FpsSampler.Remove(fpsTarget);
            Disconnect();
            retryTimer.Dispose();
            if (rdp != null) { try { rdp.Dispose(); } catch { } rdp = null; }
            if (lastShot != null) { lastShot.Dispose(); lastShot = null; }
        }

        async void FillMac()
        {
            if (!string.IsNullOrWhiteSpace(Config.Mac) || NetTools.IsLocal(Config.GetHost())) return;
            string host = Config.GetHost();
            string mac = await Task.Run(() => NetTools.GetMac(host));
            if (!string.IsNullOrEmpty(mac) && string.IsNullOrWhiteSpace(Config.Mac))
            {
                Config.Mac = mac;
                if (Changed != null) Changed(this);
            }
        }

        // ---------- actions ----------

        void RaiseFocus()
        {
            if (FocusRequested != null) FocusRequested(this);
        }

        void Raise(string msg)
        {
            if (AlertRaised != null) AlertRaised(this, msg);
        }

        void EditLink()
        {
            var v = PromptDialog.Ask(FindForm(), "Edit link", "Link for '" + Config.Name + "' (opened when a new session starts)", Config.LaunchLink);
            if (v == null) return;
            Config.LaunchLink = v.Trim();
            if (Changed != null) Changed(this);
        }

        void CopyLink()
        {
            if (!string.IsNullOrWhiteSpace(Config.LaunchLink)) Clipboard.SetText(Config.LaunchLink);
        }

        public void DoWake()
        {
            if (string.IsNullOrWhiteSpace(Config.Mac))
            {
                MessageBox.Show(FindForm(),
                    "No MAC address is known for this PC yet.\r\n\r\nIt is filled in automatically after the first connection, or you can enter it in Settings.",
                    "Wake-on-LAN", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                NetTools.Wake(Config.Mac);
                LogService.Write(Config.Name, "Wake-on-LAN", Config.Mac, 0);
                MessageBox.Show(FindForm(),
                    "Wake-on-LAN signal sent to " + Config.Mac + ".\r\n\r\nThe PC must support this in BIOS/network card and be on power/network.",
                    "Wake-on-LAN", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "Failed: " + ex.Message, "Wake-on-LAN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public async void DoPower(bool reboot)
        {
            string host = Config.GetHost();
            if (string.IsNullOrWhiteSpace(host)) return;
            bool local = NetTools.IsLocal(host);
            string what = reboot ? "restart" : "shut down";
            string msg = "Are you sure you want to " + what + " " + (local ? "THIS PC (the PC this panel runs on)" : "PC '" + host + "'")
                + "?\r\n\r\nAll sessions and programs on that PC are stopped immediately.";
            if (MessageBox.Show(FindForm(), msg, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            string user = string.IsNullOrWhiteSpace(Config.AdminUser) ? Config.User : Config.AdminUser;
            string pass = string.IsNullOrWhiteSpace(Config.AdminUser) ? Config.Password : Config.AdminPassword;
            string err = await Task.Run(() => NetTools.PowerAction(host, user, pass, reboot));
            LogService.Write(Config.Name, reboot ? "Restart PC" : "Shut down PC", err ?? "command sent", 0);
            MessageBox.Show(FindForm(),
                err == null ? "Command sent." : "Failed: " + err + "\r\n\r\n(Your account must be an administrator on that PC and WMI must be allowed through the firewall.)",
                reboot ? "Restart" : "Shut down", MessageBoxButtons.OK,
                err == null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        // ---------- display ----------

        public void ApplyConfig()
        {
            lblName.Text = NameText;
            lblHost.Text = Config.Host;
            chipNote.Text = (Config.Note ?? "").Trim();
            Color c;
            chipNote.FillColor = Gfx.TryColor(Config.ColorHex, out c) ? Gfx.Blend(c, Theme.Surface, 0.45f) : Theme.Raised;
            chipNote.ForeColor = Gfx.TryColor(Config.ColorHex, out c) ? Color.White : Theme.Fg;
            if (state == St.NotSet && !string.IsNullOrWhiteSpace(Config.Host)) state = St.Offline;
            headerDirty = true;
            RefreshInfo();
        }

        public void Tick()
        {
            UpdateFpsTarget();
            // a screenshot in memory every 10 s (only if the tile is visible)
            if ((app.ScreenshotOnDrop || (app.DiscordOnDrop && Discord.IsValid(app.WebhookUrl))) && IsConnected && (DateTime.Now - lastShotAt).TotalSeconds >= 10)
            {
                lastShotAt = DateTime.Now;
                var b = Capture();
                if (b != null)
                {
                    if (lastShot != null) lastShot.Dispose();
                    lastShot = b;
                }
            }
            if (lostAt.HasValue && !lostAlerted && (DateTime.Now - lostAt.Value).TotalSeconds >= app.AlertAfterSeconds)
            {
                lostAlerted = true;
                string msg = "Session '" + Config.Name + "' has been offline for " + (int)(DateTime.Now - lostAt.Value).TotalSeconds + " s.";
                LogService.Write(Config.Name, "Notification", msg, 0);
                Raise(msg);
            }

            // frozen: connected, but the picture has not changed for N minutes (needs the FPS display; an idle desktop looks the same)
            if (Config.FrozenMinutes > 0 && IsConnected && !double.IsNaN(Fps))
            {
                if (Fps > 0) { frozenSince = null; frozenAlerted = false; }
                else if (!frozenSince.HasValue) frozenSince = DateTime.Now;
                else if (!frozenAlerted && (DateTime.Now - frozenSince.Value).TotalMinutes >= Config.FrozenMinutes)
                {
                    frozenAlerted = true;
                    string fm = "Session '" + Config.Name + "' looks frozen: the picture has not changed for " + Config.FrozenMinutes + " min."
                        + (Config.FrozenReconnect ? " Reconnecting." : "");
                    LogService.Write(Config.Name, "Notification", fm, 0);
                    Raise(fm);
                    if (Config.FrozenReconnect)
                    {
                        frozenSince = null;
                        frozenAlerted = false;
                        Reconnect();
                    }
                }
            }
            else { frozenSince = null; frozenAlerted = false; }

            RefreshInfo();
        }

        public void RefreshInfo()
        {
            lblName.Text = NameText;

            Color sc;
            bool hasColor = Gfx.TryColor(Config.ColorHex, out sc);
            bool lost = state == St.Retrying || state == St.Stopped || state == St.Error || (Unreachable && !IsConnected);

            // status pill
            string text; Color fg;
            if (Unreachable && !IsConnected && state != St.Connecting) { text = "⚠ PC unreachable"; fg = Theme.Bad; }
            else switch (state)
            {
                case St.Online: text = "✔ Connected"; fg = Theme.Good; break;
                case St.Connecting: text = "◌ Connecting…"; fg = Theme.Warn; break;
                case St.Retrying: text = "⚠ Disconnected"; fg = Theme.Bad; break;
                case St.Stopped: text = "⚠ Stopped"; fg = Theme.Bad; break;
                case St.Error: text = "⚠ Error"; fg = Theme.Bad; break;
                case St.NotSet: text = "● Not set up"; fg = Theme.FgMuted; break;
                default: text = "● Offline"; fg = Theme.FgMuted; break;
            }
            if (chipStatus.Text != text)
            {
                chipStatus.Text = text;
                chipStatus.ForeColor = fg;
                chipStatus.FillColor = fg == Theme.FgMuted ? Theme.Raised : Gfx.Blend(fg, Theme.Surface, 0.18f);
                chipStatus.FitWidth();
                LayoutInfo();
            }

            lblDur.Text = connectedAt.HasValue ? "⏱ " + FormatSpan(DateTime.Now - connectedAt.Value) : "";

            string sub = "";
            if (state == St.Retrying || state == St.Error || state == St.Stopped) sub = stateText;
            else if (state == St.Offline && lastAttempt.HasValue) sub = "Last attempt: " + Ago(DateTime.Now - lastAttempt.Value);
            else if (state == St.NotSet) sub = "Click ✎ to set up";
            lblSub.Text = sub;
            lblSub.ForeColor = lost ? Theme.Warn : Theme.FgMuted;

            string na = StatsError.Length > 0 ? "n.v.t." : "–";
            lblCpuVal.Text = double.IsNaN(Cpu) ? na : Cpu.ToString("0") + "%";
            lblRamVal.Text = double.IsNaN(Ram) ? na : Ram.ToString("0") + "%";
            string statsTip = StatsError.Length > 0 ? "Cannot read CPU/RAM: " + StatsError : "";
            if (statsTip != lastStatsTip)
            {
                lastStatsTip = statsTip;
                tips.SetToolTip(lblCpuVal, statsTip);
                tips.SetToolTip(lblRamVal, statsTip);
            }
            lblCpuVal.ForeColor = !double.IsNaN(Cpu) && Cpu >= CpuLimit ? Theme.Bad : Theme.Fg;
            lblRamVal.ForeColor = !double.IsNaN(Ram) && Ram >= RamLimit ? Theme.Bad : Theme.Fg;

            // button set: online or not
            bool on = IsConnected;
            pnlOn.Visible = on;
            pnlOff.Visible = !on;
            btnReconnect.Text = (state == St.Retrying || state == St.Stopped || state == St.Error) ? "↻  Reconnect" : "Connect";

            // overlay on the screen
            if (on)
            {
                overlay.Visible = false;
            }
            else
            {
                string t;
                switch (state)
                {
                    case St.NotSet: t = "Not set up yet\r\n\r\nClick ✎ to enter host and login"; break;
                    case St.Connecting: t = "Connecting…"; break;
                    case St.Retrying: t = "Connection lost\r\n" + lastReason + "\r\n\r\nReconnecting automatically…"; break;
                    case St.Stopped: t = "Stopped reconnecting\r\n" + lastReason + "\r\n\r\nCheck the settings and click Reconnect"; break;
                    case St.Error: t = "Error\r\n" + stateText; break;
                    default: t = "Offline\r\n\r\nClick Connect to start"; break;
                }
                if (lblOverlay.Text != t) lblOverlay.Text = t;
                if (!overlay.Visible) { overlay.Visible = true; }
                overlay.BringToFront();
            }

            // colors: dot, border
            Color dotColor = hasColor ? sc : (on ? Theme.Good : (lost ? Theme.Bad : Theme.FgMuted));
            dot.ForeColor = dotColor;
            Color border;
            if (state == St.Stopped || state == St.Error || (Unreachable && !on)) border = Gfx.Blend(Theme.Bad, Theme.Bg, 0.6f);
            else if (state == St.Retrying || state == St.Connecting) border = Gfx.Blend(Theme.Warn, Theme.Bg, 0.6f);
            else if (on) border = hasColor ? Gfx.Blend(sc, Theme.Bg, 0.65f) : Gfx.Blend(Theme.Good, Theme.Bg, 0.45f);
            else border = hasColor ? Gfx.Blend(sc, Theme.Bg, 0.3f) : Theme.Line;
            if (BackColor != border) BackColor = border;


            // ping
            string pt = PingMs >= 0 ? "● " + PingMs + " ms" : "— ms";
            lblPing.ForeColor = PingMs < 0 ? Theme.FgMuted : (PingMs < 50 ? Theme.Good : (PingMs < 150 ? Theme.Warn : Theme.Bad));
            string ct = compact
                ? text + (on && !double.IsNaN(Cpu) ? "   CPU " + Cpu.ToString("0") + "%" : "") + (on && !double.IsNaN(Ram) ? "   RAM " + Ram.ToString("0") + "%" : "")
                : "";
            if (ct != lastCompactText) { lastCompactText = ct; lblCompact.Text = ct; headerDirty = true; }
            if (pt != lastPingText) { lastPingText = pt; lblPing.Text = pt; headerDirty = true; }

            string ft = (!app.ShowFps || !on) ? "" : (double.IsNaN(Fps) ? "– FPS" : Fps.ToString("0") + " FPS");
            if (ft != lastFpsText) { lastFpsText = ft; lblFps.Text = ft; headerDirty = true; }
            string tip = "FPS = how many times per second the picture of this session changes (max. about 10).\r\nIdle desktop = 0. This is not the frame rate inside the game.";
            if (app.ShowFps && on)
            {
                if (double.IsNaN(Fps)) tip += "\r\n\r\nNo value: " + FpsReason;
                else if (FpsMethod == "window") tip += "\r\n\r\nMeasured from the window itself (the tile is covered), at most 4 times per second.";
            }
            if (tip != lastFpsTip) { lastFpsTip = tip; tips.SetToolTip(lblFps, tip); }
            if (headerDirty) LayoutHeader();
        }


        static string FormatSpan(TimeSpan t)
        {
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes.ToString("00") + "m";
            return t.Minutes + "m " + t.Seconds.ToString("00") + "s";
        }

        static string Ago(TimeSpan t)
        {
            if (t.TotalSeconds < 60) return (int)t.TotalSeconds + "s ago";
            if (t.TotalMinutes < 60) return (int)t.TotalMinutes + " min ago";
            return (int)t.TotalHours + " h ago";
        }

        // ---------- actions without pop-ups (for the Discord bot) ----------

        // returns null if it worked, otherwise the error message
        public string WakeSilent()
        {
            if (string.IsNullOrWhiteSpace(Config.Mac)) return "No MAC address known for this PC.";
            try
            {
                NetTools.Wake(Config.Mac);
                LogService.Write(Config.Name, "Wake-on-LAN", Config.Mac + " (bot)", 0);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public async Task<string> PowerSilentAsync(bool reboot)
        {
            string host = Config.GetHost();
            if (string.IsNullOrWhiteSpace(host)) return "No host set.";
            string user = string.IsNullOrWhiteSpace(Config.AdminUser) ? Config.User : Config.AdminUser;
            string pass = string.IsNullOrWhiteSpace(Config.AdminUser) ? Config.Password : Config.AdminPassword;
            string err = await Task.Run(() => NetTools.PowerAction(host, user, pass, reboot));
            LogService.Write(Config.Name, reboot ? "Restart PC" : "Shut down PC", (err ?? "command sent") + " (bot)", 0);
            return err;
        }

        // ---------- screenshots ----------

        public byte[] LastShotJpeg()
        {
            return lastShot == null ? null : Discord.ToJpeg(lastShot, 80);
        }

        public string SafeFileName
        {
            get { return new string(Config.Name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()); }
        }

        // a fresh capture if the tile is visible, otherwise the last one we had
        public Bitmap Snapshot()
        {
            var b = Capture();
            if (b != null) return b;
            return lastShot != null ? new Bitmap(lastShot) : null;
        }

        async void ManualDiscord()
        {
            if (!Discord.IsValid(app.WebhookUrl))
            {
                MessageBox.Show(FindForm(), "Set up a Discord webhook in Settings first.", "Discord", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var b = Snapshot();
            if (b == null)
            {
                MessageBox.Show(FindForm(), "No image available. The session must be connected.", "Discord", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var files = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>(SafeFileName + ".jpg", Discord.ToJpeg(b, 80)) };
            b.Dispose();
            string err = await Discord.SendAsync(app.WebhookUrl,
                "📷 **" + Config.Name + "** (" + Config.Host + ") – " + DateTime.Now.ToString("HH:mm:ss"), files);
            MessageBox.Show(FindForm(), err == null ? "Sent to Discord." : "Failed: " + err, "Discord",
                MessageBoxButtons.OK, err == null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        // Where is the picture of this tile and how can it be read?
        //   why == null: the tile has a picture
        //   screenOk:    the tile is fully visible, so it can be copied from the screen (fast, thin bands are enough)
        //   otherwise:   the picture is read from the window itself (PrintWindow), also when another window covers it
        void GetCaptureInfo(out Rectangle r, out bool screenOk, out IntPtr hw, out string why)
        {
            r = Rectangle.Empty; screenOk = false; hw = IntPtr.Zero; why = null;
            try
            {
                if (rdp == null || !IsConnected) { why = "not connected"; return; }
                if (overlay.Visible) { why = "no picture yet (waiting for the session)"; return; }
                if (!rdp.Visible || !rdp.IsHandleCreated) { why = "this tile is on another page (not shown)"; return; }
                var f = FindForm();
                if (f == null) { why = "tile not in a window"; return; }
                if (f.WindowState == FormWindowState.Minimized) { why = "the panel is minimised (use 'Background mode' in Settings so it keeps working)"; return; }
                var rc = rdp.RectangleToScreen(rdp.ClientRectangle);
                if (rc.Width < 50 || rc.Height < 50) { why = "tile is too small"; return; }
                r = rc;
                hw = rdp.Handle;
                screenOk = !WinUtil.IsCovered(f.Handle, rc);
            }
            catch (Exception ex) { why = "error: " + ex.Message; }
        }

        // tells the FPS sampler where to look (or to stop looking)
        void UpdateFpsTarget()
        {
            if (!app.ShowFps) { fpsTarget.Set(false, Rectangle.Empty, false, IntPtr.Zero, "FPS display is off"); return; }
            Rectangle r; bool screenOk; IntPtr hw; string why;
            GetCaptureInfo(out r, out screenOk, out hw, out why);
            fpsTarget.Set(why == null, r, screenOk, hw, why);
        }

        // the picture of the tile: from the screen when it is freely visible (this always worked),
        // otherwise from the window itself (works behind other windows and in background mode). null = nothing usable.
        Bitmap Capture()
        {
            Rectangle r; bool screenOk; IntPtr hw; string why;
            GetCaptureInfo(out r, out screenOk, out hw, out why);
            if (why != null) return null;
            if (screenOk)
            {
                try
                {
                    var bmp = new Bitmap(r.Width, r.Height);
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    return bmp;
                }
                catch { }
            }
            return WinCapture.PrintCopy(hw, r.Width, r.Height, true);
        }

        // draws the picture of this session into g (origin = screen position of the bitmap's top-left corner);
        // used for the panel screenshot when the panel itself is covered or in background mode
        public void PaintPictureInto(Graphics g, Point origin)
        {
            try
            {
                var b = Capture();
                if (b == null) return;
                var rc = rdp.RectangleToScreen(rdp.ClientRectangle);
                g.DrawImage(b, rc.X - origin.X, rc.Y - origin.Y, rc.Width, rc.Height);
                b.Dispose();
            }
            catch { }
        }

        // for the 'Test screenshot' button: tries both ways and says what happened (saves the images too)
        public string TestCapture(string folder)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Session '" + Config.Name + "':");
            Rectangle r; bool screenOk; IntPtr hw; string why;
            GetCaptureInfo(out r, out screenOk, out hw, out why);
            if (why != null) { sb.AppendLine("   no test possible: " + why); return sb.ToString(); }
            string safe = SafeFileName;
            sb.AppendLine("   screen copy: " + (screenOk ? "possible (tile fully visible)" : "not possible (tile covered or partly off screen)"));
            if (screenOk)
            {
                try
                {
                    using (var bmp = new Bitmap(r.Width, r.Height))
                    {
                        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                        string path = Path.Combine(folder, "test_" + safe + "_screen.png");
                        bmp.Save(path, ImageFormat.Png);
                        sb.AppendLine("   screen copy saved: " + path);
                    }
                }
                catch (Exception ex) { sb.AppendLine("   screen copy failed: " + ex.Message); }
            }
            Bitmap pb = null;
            try
            {
                bool ok = WinCapture.Print(hw, ref pb, r.Width, r.Height, true);
                if (!ok) sb.AppendLine("   window print: FAILED (Windows returned an error)");
                else if (WinCapture.IsBlank(pb)) sb.AppendLine("   window print: BLACK picture (does not work for this window on this PC; only the screen copy can be used)");
                else
                {
                    string path = Path.Combine(folder, "test_" + safe + "_window.png");
                    pb.Save(path, ImageFormat.Png);
                    sb.AppendLine("   window print: WORKS, saved: " + path);
                }
            }
            catch (Exception ex) { sb.AppendLine("   window print failed: " + ex.Message); }
            finally { if (pb != null) pb.Dispose(); }
            sb.AppendLine("   FPS now: " + (double.IsNaN(Fps) ? "no value (" + FpsReason + ")" : Fps.ToString("0") + " (" + FpsMethod + ")"));
            return sb.ToString();
        }

        string SaveShot(Bitmap bmp)
        {
            try
            {
                Directory.CreateDirectory(LogService.ShotDir);
                string safe = SafeFileName;
                string path = Path.Combine(LogService.ShotDir, safe + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                bmp.Save(path, ImageFormat.Png);
                // keep the last 30 per session
                foreach (var old in Directory.GetFiles(LogService.ShotDir, safe + "_*.png")
                    .OrderByDescending(x => File.GetCreationTime(x)).Skip(30))
                {
                    try { File.Delete(old); } catch { }
                }
                return path;
            }
            catch { return null; }
        }

        void ManualShot()
        {
            var b = Capture();
            if (b == null)
            {
                MessageBox.Show(FindForm(), "No screenshot possible. The session must be connected and show a picture.\r\n\r\nTip: Settings > 'Test screenshot' shows which method works on this PC.",
                    "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string path = SaveShot(b);
            b.Dispose();
            if (path != null) LogService.Write(Config.Name, "Screenshot", path, 0);
            MessageBox.Show(FindForm(), path != null ? "Saved:\r\n" + path : "Saving failed.", "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- CPU/RAM and reachability ----------

        public async Task RefreshStatsAsync()
        {
            if (string.IsNullOrWhiteSpace(Config.Host)) return;
            string host = Config.GetHost();
            string user = string.IsNullOrWhiteSpace(Config.AdminUser) ? Config.User : Config.AdminUser;
            string pass = string.IsNullOrWhiteSpace(Config.AdminUser) ? Config.Password : Config.AdminPassword;
            var res = await Task.Run(() =>
            {
                string err;
                var v = QueryStats(host, user, pass, out err);
                return Tuple.Create(v, err);
            });
            var r = res.Item1;

            if (r == null)
            {
                Cpu = Ram = FreeMb = double.NaN;
                highCount = 0;
                StatsError = string.IsNullOrEmpty(res.Item2) ? "unknown error" : res.Item2;
                return;
            }

            StatsError = "";
            Cpu = r[0]; Ram = r[1]; FreeMb = r[2];
            CpuHist.Add((float)Cpu); RamHist.Add((float)Ram);
            while (CpuHist.Count > MaxHist) CpuHist.RemoveAt(0);
            while (RamHist.Count > MaxHist) RamHist.RemoveAt(0);
            spCpu.Invalidate(); spRam.Invalidate();
            EvaluateLoad();
        }

        void EvaluateLoad()
        {
            bool cpuHigh = Cpu >= CpuLimit;
            bool ramHigh = Ram >= RamLimit;
            bool lowMem = FreeMb < app.MinFreeMb;
            if (cpuHigh || ramHigh || lowMem)
            {
                if (++highCount >= 3 && !highAlerted)   // ± 30 seconds in a row
                {
                    highAlerted = true;
                    string msg = "PC of '" + Config.Name + "': CPU " + Cpu.ToString("0") + "%, RAM " + Ram.ToString("0") + "% (free " + FreeMb.ToString("0") + " MB)";
                    LogService.Write(Config.Name, "Notification", msg, 0);
                    Raise(msg);
                }
            }
            else
            {
                highCount = 0;
                highAlerted = false;
            }
        }

        public async Task ProbeAsync()
        {
            if (string.IsNullOrWhiteSpace(Config.Host)) { PingMs = -1; return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = await NetTools.PortOpenAsync(Config.GetHost(), Config.GetPort(), 1500);
            sw.Stop();
            if (ok)
            {
                PingMs = (int)Math.Max(1, sw.ElapsedMilliseconds);
                Unreachable = false;
                unreachableCount = 0;
                unreachAlerted = false;
            }
            else
            {
                PingMs = -1;
                if (!userDisconnected)
                {
                    Unreachable = true;
                    if (++unreachableCount >= 2 && !unreachAlerted)
                    {
                        unreachAlerted = true;
                        string msg = "PC '" + Config.Name + "' (" + Config.Host + ") is no longer responding. Did the IP address change or is the PC off?";
                        LogService.Write(Config.Name, "Notification", msg, 0);
                        Raise(msg);
                    }
                }
            }
        }

        // returns [cpu%, ram%, free MB] or null (with the reason in error)
        static double[] QueryStats(string host, string user, string pass, out string error)
        {
            error = "";

            // this PC itself: directly via Windows, without WMI or rights
            if (NetTools.IsLocal(host))
            {
                var l = NetTools.LocalStats();
                if (l == null) error = "measurement on this PC failed";
                return l;
            }

            // other PC: via WMI (firewall and admin account needed)
            try
            {
                var opts = new ConnectionOptions { Timeout = TimeSpan.FromSeconds(5) };
                string path = @"\\" + host + @"\root\cimv2";
                opts.Username = (user.Contains("\\") || user.Contains("@")) ? user : host + "\\" + user;
                opts.Password = pass;

                var scope = new ManagementScope(path, opts);
                scope.Connect();

                double cpu = 0; int n = 0;
                using (var q = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT LoadPercentage FROM Win32_Processor")))
                    foreach (ManagementObject o in q.Get())
                    {
                        if (o["LoadPercentage"] == null) continue;
                        cpu += Convert.ToDouble(o["LoadPercentage"]);
                        n++;
                    }

                double total = 0, free = 0;
                using (var q = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem")))
                    foreach (ManagementObject o in q.Get())
                    {
                        total = Convert.ToDouble(o["TotalVisibleMemorySize"]);
                        free = Convert.ToDouble(o["FreePhysicalMemory"]);
                    }

                if (total <= 0) { error = "no memory data received"; return null; }
                return new[] { n > 0 ? cpu / n : 0, 100 * (total - free) / total, free / 1024.0 };
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }
    }
}
