using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RdpPanel
{
    // ---- texts of the in-app manual ----
    class HelpTopic
    {
        public string Title = "";
        public List<string[]> Blocks = new List<string[]>();   // { kind, text }  kind: p = paragraph, b = bullet, n = note

        public HelpTopic P(string t) { Blocks.Add(new[] { "p", t }); return this; }
        public HelpTopic B(string t) { Blocks.Add(new[] { "b", t }); return this; }
        public HelpTopic N(string t) { Blocks.Add(new[] { "n", t }); return this; }
    }

    static class HelpContent
    {
        public static readonly List<HelpTopic> Topics = new List<HelpTopic>
        {
            new HelpTopic { Title = "Getting started" }
                .P("RDP Panel shows several Remote Desktop (RDP) sessions in one window. Every session is a **tile** with the live screen, its status, how long it has been connected, CPU/RAM of that PC, FPS and ping.")
                .B("Click **+ Session** at the top. Fill in the host (**192.168.1.20** or **192.168.1.20:3389**), username and password. Passwords are stored encrypted (Windows DPAPI), so only your Windows account can read them.")
                .B("Press **Connect** on the tile. \"Connect automatically at startup\" is on by default.")
                .B("On the PC you connect to, Remote Desktop must be switched on.")
                .B("Want several sessions on the same PC at the same time? Windows only allows one by default. RDPWrapKit solves that; it is installed **separately** and is not part of this program (see Credits).")
                .N("If a session drops, the panel reconnects by itself: it waits a bit longer after every failed try and gives up after 20 tries, so a wrong password can never lock your account."),

            new HelpTopic { Title = "The overview" }
                .P("The Overview page is your main screen.")
                .B("**Tile colors:** green = connected, orange = dropped / reconnecting, grey = offline. Your own tile color (in the session settings) tints the border.")
                .B("**Tile header:** name, host, label, FPS and ping. Drag the header onto another tile to swap places. The **⋮** menu (or right-click) has large view, connect/disconnect, screenshot, Wake-on-LAN, restart/shut down and settings.")
                .B("**⤢** shows one tile large; press **Back to overview** to return.")
                .B("**Columns**, **Group by PC** and the **search box** change what you see. **Profiles** remember a selection of sessions with its layout.")
                .B("**F11** = full screen (move the mouse to the top edge to bring the top bar back). **Ctrl+1 … Ctrl+9** = show that session large, **Ctrl+0** = back. **F1** = this help.")
                .B("The strip at the bottom shows performance, today's connected time, the latest log lines and quick PC actions."),

            new HelpTopic { Title = "FPS" }
                .P("The number next to the ping in every tile header is the **FPS**: how many times per second the picture of that session changes.")
                .B("An idle desktop shows **0**. A busy game or macro shows something like 5-10.")
                .B("It is measured by the panel itself (Windows does not report a frame rate for Remote Desktop), by comparing a few thin strips of the picture about 10 times per second. So the highest value you can see is about 10, and it is **not** the in-game FPS of Roblox or any other program. It is enough to see whether a session is frozen or active.")
                .B("If another window covers the tile, the panel reads the picture from the window itself. That is heavier, so then it is measured at most 4 times per second.")
                .B("No number? You see **– FPS**. **Hover the mouse over it**: the tooltip says why (not connected, tile on another page, panel minimised, picture not readable ...). The same reason is in the daily report and the diagnostic file.")
                .B("The average FPS also appears in the daily report. You can switch the measurement off in **Settings → Show FPS**."),

            new HelpTopic { Title = "Background mode and screenshots" }
                .P("Screenshots (webhook and bot) and FPS need a picture of the session. Windows does not draw a **minimised** window, so a minimised panel gives old or black pictures.")
                .B("**Background mode** (Settings): the minimise button hides the panel instead: it stays open but becomes invisible and click-through, without a taskbar button. Sessions, FPS, screenshots and the bot keep working. Click the **tray icon** (next to the clock) to open the panel again; right-click it for Open, Test screenshot and Exit.")
                .B("If another window covers the panel, the pictures are taken from the panel window itself. If that does not work on your PC, the screenshot of the last visible moment is used and the message says how old it is.")
                .B("**Test screenshot** (Settings or tray menu) tries both methods and tells you which one works on your PC. The test pictures are saved in the screenshots folder.")
                .B("If the panel is completely **closed**, there are no sessions and so no pictures and no bot."),

            new HelpTopic { Title = "Monitoring and log" }
                .B("**Monitoring** shows a large CPU (blue) and RAM (purple) chart per session.")
                .B("**Log** lists every event: connected, disconnected (with the reason in plain language), failed tries, notifications, screenshots. Pick a day at the top. The table above it counts connections and drops per session.")
                .B("**Open screenshots** opens the folder with the screenshots that were taken when a session dropped.")
                .P("CPU/RAM of **this** PC are read directly. For **another** PC the panel uses WMI: run **wmi-setup.bat** as administrator on that PC once and enter an administrator account of that PC in the session settings."),

            new HelpTopic { Title = "PC management" }
                .B("**Wake-on-LAN** turns a PC on. It needs the MAC address (filled in automatically after the first connection) and Wake-on-LAN switched on in the BIOS / network card.")
                .B("**Restart / Shut down** use WMI (see Monitoring and log) and always ask for confirmation first.")
                .B("**Search network** scans your /24 network for PCs with RDP (port 3389) open and lets you add them.")
                .B("**Import users** turns pasted lines like **name password** into one session per line."),

            new HelpTopic { Title = "Notifications" }
                .P("In **Settings** you choose after how many seconds a missing session triggers a notification, and the CPU, RAM and free-memory limits. A notification is shown by Windows, optionally with a sound, and optionally in Discord. The switch in the bottom right of the window turns all notifications on or off."),

            new HelpTopic { Title = "Discord" }
                .P("There are two ways to use Discord, and you can use both.")
                .B("**Webhook:** simple messages and screenshots. Channel settings → Integrations → Webhooks → copy the link and paste it in Settings. Use **Send test** to check it.")
                .B("**Bot:** slash commands and buttons (`/status`, `/screen`, `/live`, `/restart-pc` …). Create it at **discord.com/developers**, add the token in Settings, tick **Discord bot on** and Save. Type **/help** in Discord for all commands with examples.")
                .N("Never share your webhook link or bot token. Anyone who has it can post as your panel or control it."),

            new HelpTopic { Title = "Daily report" }
                .P("Every morning the panel posts one message in Discord with, **per session**: the **uptime** of the previous day (time and percentage), the number of **drops**, and the average **CPU, RAM and FPS**.")
                .B("Switch it on in **Settings → Daily report** and choose the time (default 08:00). It is sent to the Discord webhook; if there is none, to the bot channel.")
                .B("**Send report now** sends a report for today so far, so you can see what it looks like right away.")
                .B("Uptime is the time a session was connected, as a percentage of 24 hours. If the panel itself was closed or the PC was off, that time counts as not connected.")
                .B("A **drop** is a connection that was lost without you disconnecting it. Failed reconnect tries are listed separately.")
                .B("If the PC was off at the chosen time, the report is sent as soon as the panel starts that day. Nothing is sent when there is no data yet.")
                .P("The measurements for the averages are saved once a minute in **metrics.csv** and kept for 14 days."),

            new HelpTopic { Title = "Diagnostic file" }
                .P("If something does not work and you ask someone for help, click **Create diagnostic file** (Settings or this page). It makes one zip with:")
                .B("the log of the last 7 days and the CPU/RAM/FPS measurements of the last 3 days,")
                .B("system information (Windows, .NET, Remote Desktop and RDP wrapper versions),")
                .B("your settings, but only as \"set / not set\" for anything secret,")
                .B("errors that the panel recorded (errors.log).")
                .P("It does **not** contain passwords, usernames, the Discord webhook or bot token, session links or screenshots, and your Windows user name and PC name are replaced. The zip is a normal zip: open it to check it before you send it."),

            new HelpTopic { Title = "Troubleshooting" }
                .B("**Tile stays Offline:** is the PC on, Remote Desktop enabled and port 3389 open in the firewall?")
                .B("**\"Cannot read CPU/RAM\":** run wmi-setup.bat as administrator on that PC and enter an administrator account in the session settings.")
                .B("**Second session on the same PC kicks the first one off:** Windows allows one RDP session per user. Install RDPWrapKit on that PC (see Credits) and use a separate Windows user per session.")
                .B("**FPS shows – :** hover over it: the tooltip tells the reason. Minimised panel? Turn on background mode in Settings.")
                .B("**Old or black screenshot:** press Settings → Test screenshot and read what it says.")
                .B("**The bot does not respond:** check the token, that the bot was invited with the **applications.commands** scope, and the channel ID / allowed users.")
                .B("**Windows SmartScreen warning:** the program is not code-signed. Click More info → Run anyway, and compare the SHA256 from the release page if you want to be sure.")
                .B("**Anything else:** create the diagnostic file and send it along with a description of what happened."),

            new HelpTopic { Title = "Files and privacy" }
                .P("Everything is stored on your PC in **%APPDATA%\\RdpPanel\\**:")
                .B("**config.json** – settings. Passwords, webhook and bot token are encrypted per Windows user, so a copied file does not work on another account or PC.")
                .B("**log.csv** – events. **metrics.csv** – CPU/RAM/FPS per minute (14 days). **errors.log** – unexpected errors.")
                .B("**screenshots\\** – pictures taken when a session dropped.")
                .N("Never upload config.json to GitHub or send it to anyone.")
        };
    }

    // ---- Help page: tutorial button + manual ----
    class HelpPage : Panel
    {
        readonly MainForm main;
        readonly ListBox topics = new ListBox();
        readonly RichTextBox rtb = new RichTextBox();

        public HelpPage(MainForm main)
        {
            this.main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(16);

            var bar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Bg };
            var title = new Label
            {
                Text = "Help and tutorial",
                AutoSize = true,
                Location = new Point(0, 8),
                Font = Theme.UI(14f, FontStyle.Bold),
                ForeColor = Theme.Fg
            };
            bar.Controls.Add(title);
            var tour = UI.Btn(bar, "▶  Start the tutorial", 0, 0, 170, true);
            tour.Left = 230; tour.Top = 10;
            tour.Click += (s, e) => main.ShowTutorial();
            var diag = UI.Btn(bar, "Create diagnostic file…", 410, 10, 190, false);
            diag.Click += (s, e) => DiagnosticFile.RunWithDialog(main);
            if (AppInfo.ManualUrl.Length > 0)
            {
                var web = UI.Btn(bar, "Online manual", 608, 10, 130, false);
                web.Click += (s, e) => UI.OpenUrl(AppInfo.ManualUrl);
            }

            topics.Dock = DockStyle.Left;
            topics.Width = 230;
            topics.BorderStyle = BorderStyle.None;
            topics.BackColor = Theme.Surface;
            topics.ForeColor = Theme.Fg;
            topics.Font = Theme.UI(10.5f);
            topics.DrawMode = DrawMode.OwnerDrawFixed;
            topics.ItemHeight = 34;
            topics.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var b = new SolidBrush(sel ? Theme.Accent : Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, topics.Items[e.Index].ToString(), topics.Font,
                    new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height),
                    sel ? Color.White : Theme.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            foreach (var t in HelpContent.Topics) topics.Items.Add(t.Title);
            topics.SelectedIndexChanged += (s, e) => ShowTopic(topics.SelectedIndex);

            var spacer = new Panel { Dock = DockStyle.Left, Width = 12, BackColor = Theme.Bg };

            rtb.Dock = DockStyle.Fill;
            rtb.ReadOnly = true;
            rtb.BorderStyle = BorderStyle.None;
            rtb.BackColor = Theme.Surface;
            rtb.ForeColor = Theme.Fg;
            rtb.Font = Theme.UI(10.5f);
            rtb.DetectUrls = true;
            rtb.TabStop = false;
            rtb.LinkClicked += (s, e) => UI.OpenUrl(e.LinkText);

            // docking order: fill first in the list = docked last
            Controls.Add(rtb);
            Controls.Add(spacer);
            Controls.Add(topics);
            Controls.Add(bar);
            topics.SelectedIndex = 0;
        }

        public void OpenTopic(string title)
        {
            int i = HelpContent.Topics.FindIndex(t => t.Title == title);
            if (i >= 0) topics.SelectedIndex = i;
        }

        void Put(string text, Font f, Color c, int indent, int hanging)
        {
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionFont = f;
            rtb.SelectionColor = c;
            rtb.SelectionIndent = indent;
            rtb.SelectionHangingIndent = hanging;
            if (hanging > 0) rtb.SelectionTabs = new[] { indent + hanging };
            rtb.SelectedText = text;
        }

        // text with **bold** parts
        void PutRich(string text, int indent, int hanging, Color color)
        {
            var parts = text.Split(new[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0)
                    Put(parts[i], Theme.UI(10.5f, i % 2 == 1 ? FontStyle.Bold : FontStyle.Regular), color, indent, hanging);
        }

        void ShowTopic(int idx)
        {
            if (idx < 0 || idx >= HelpContent.Topics.Count) return;
            var t = HelpContent.Topics[idx];
            rtb.SuspendLayout();
            rtb.Clear();
            Put(t.Title + "\n", Theme.UI(17f, FontStyle.Bold), Theme.Fg, 12, 0);
            Put("\n", Theme.UI(4f), Theme.Fg, 12, 0);
            foreach (var b in t.Blocks)
            {
                switch (b[0])
                {
                    case "b":
                        Put("•\t", Theme.UI(10.5f), Theme.Accent, 12, 22);
                        PutRich(b[1], 12, 22, Theme.Fg);
                        Put("\n", Theme.UI(10.5f), Theme.Fg, 12, 22);
                        Put("\n", Theme.UI(3f), Theme.Fg, 12, 22);
                        break;
                    case "n":
                        PutRich("Note: " + b[1], 12, 0, Theme.Warn);
                        Put("\n", Theme.UI(10.5f), Theme.Fg, 12, 0);
                        Put("\n", Theme.UI(3f), Theme.Fg, 12, 0);
                        break;
                    default:
                        PutRich(b[1], 12, 0, Theme.Fg);
                        Put("\n", Theme.UI(10.5f), Theme.Fg, 12, 0);
                        Put("\n", Theme.UI(5f), Theme.Fg, 12, 0);
                        break;
                }
            }
            rtb.SelectionStart = 0;
            rtb.SelectionLength = 0;
            rtb.ResumeLayout();
            rtb.ScrollToCaret();
        }
    }

    // ---- Credits page ----
    class CreditsPage : Panel
    {
        readonly Panel col = new Panel();

        public CreditsPage(MainForm main)
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            AutoScroll = true;
            Padding = new Padding(16);

            col.BackColor = Theme.Bg;
            col.Width = 680;
            Controls.Add(col);

            int y = 20;
            y = Centered("RDP Panel", Theme.UI(24f, FontStyle.Bold), Theme.Fg, y, 44);
            y = Centered("Version " + AppInfo.Version, Theme.UI(10.5f), Theme.FgMuted, y, 24);
            y = Centered("Made by " + AppInfo.Author, Theme.UI(13f, FontStyle.Bold), Theme.Accent, y + 6, 32);
            y += 20;

            y = Header("Credits", y);
            y = Entry("smotserela", "Author and maintainer of RDP Panel.", null, y);
            y = Entry("RDPWrapKit", "by cpdx4 – a free, open-source all-in-one installer that sets up local RDP, so several users can be connected to one PC at the same time. Thank you for making this so easy!",
                new[] { "https://rdpwrapkit.cpdx4.space/", "https://github.com/cpdx4/RDPWrapKit" }, y);
            y = Entry("TermWrap", "by llccd – the component RDPWrapKit is built on.", new[] { "https://github.com/llccd/TermWrap" }, y);
            y = Entry("RDPWrapper", "by Stas'M Corp. – the original project that TermWrap was inspired by.", new[] { "https://github.com/stascorp/rdpwrap/releases" }, y);
            y = Entry("Windows Remote Desktop", "RDP Panel uses the Remote Desktop control (mstscax) that is built into Windows to show the sessions.", null, y);
            y = Entry("Discord", "Webhooks and the bot API are used for notifications and reports. RDP Panel is not affiliated with or endorsed by Discord Inc.", new[] { "https://discord.com/developers/docs" }, y);
            y += 10;

            y = Header("Good to know", y);
            y = Paragraph("RDPWrapKit and the projects it builds on are separate, third-party tools. They are **not** included in RDP Panel and RDP Panel is not affiliated with their authors. You install them yourself, on the PCs you connect to, at your own risk; see their pages for details and support.", y);
            y = Paragraph("© 2026 " + AppInfo.Author + ".", y);

            col.Height = y + 20;
            Resize += (s, e) => CenterColumn();
            CenterColumn();
        }

        void CenterColumn()
        {
            col.Left = Math.Max(0, (ClientSize.Width - col.Width) / 2);
            col.Top = 0;
        }

        int Centered(string text, Font f, Color c, int y, int h)
        {
            var l = new Label
            {
                Text = text, Font = f, ForeColor = c, BackColor = Theme.Bg,
                AutoSize = false, TextAlign = ContentAlignment.MiddleCenter
            };
            l.SetBounds(0, y, col.Width, h);
            col.Controls.Add(l);
            return y + h;
        }

        int Header(string text, int y)
        {
            var l = new Label
            {
                Text = text, Font = Theme.UI(12f, FontStyle.Bold), ForeColor = Theme.Fg, BackColor = Theme.Bg,
                AutoSize = false, TextAlign = ContentAlignment.MiddleLeft
            };
            l.SetBounds(0, y, col.Width, 30);
            col.Controls.Add(l);
            var line = new Panel { BackColor = Theme.Line, Bounds = new Rectangle(0, y + 32, col.Width, 1) };
            col.Controls.Add(line);
            return y + 42;
        }

        int Paragraph(string text, int y)
        {
            var parts = text.Split(new[] { "**" }, StringSplitOptions.None);
            string plain = string.Concat(parts);
            var l = new Label
            {
                Text = plain, Font = Theme.UI(9.5f), ForeColor = Theme.FgMuted, BackColor = Theme.Bg,
                AutoSize = false
            };
            int h = TextRenderer.MeasureText(plain, l.Font, new Size(col.Width, 2000), TextFormatFlags.WordBreak).Height + 6;
            l.SetBounds(0, y, col.Width, h);
            col.Controls.Add(l);
            return y + h + 8;
        }

        int Entry(string name, string text, string[] links, int y)
        {
            var n = new Label
            {
                Text = name, Font = Theme.UI(11f, FontStyle.Bold), ForeColor = Theme.Fg, BackColor = Theme.Bg,
                AutoSize = false, TextAlign = ContentAlignment.MiddleLeft
            };
            n.SetBounds(0, y, col.Width, 24);
            col.Controls.Add(n);
            y += 24;

            var d = new Label { Text = text, Font = Theme.UI(9.5f), ForeColor = Theme.FgMuted, BackColor = Theme.Bg, AutoSize = false };
            int h = TextRenderer.MeasureText(text, d.Font, new Size(col.Width, 2000), TextFormatFlags.WordBreak).Height + 4;
            d.SetBounds(0, y, col.Width, h);
            col.Controls.Add(d);
            y += h;

            if (links != null)
                foreach (var url in links)
                {
                    var ll = new LinkLabel
                    {
                        Text = url, Font = Theme.UI(9.5f), BackColor = Theme.Bg, AutoSize = false,
                        LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, VisitedLinkColor = Theme.Accent,
                        TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand
                    };
                    ll.SetBounds(0, y, col.Width, 22);
                    string u = url;
                    ll.LinkClicked += (s, e) => UI.OpenUrl(u);
                    col.Controls.Add(ll);
                    y += 22;
                }
            return y + 14;
        }
    }

    // ---- first-start tutorial: a short tour with "show me" buttons ----
    class TutorialForm : Form
    {
        class Step
        {
            public string Glyph = "", Title = "", Text = "", ActionText = "";
            public Action Action;
        }

        readonly List<Step> steps = new List<Step>();
        readonly Label lblGlyph = new Label();
        readonly Label lblTitle = new Label();
        readonly Label lblText = new Label();
        readonly Label lblCount = new Label();
        readonly Button btnBack, btnNext, btnAction;
        readonly Panel dots = new Panel();
        int idx;

        public TutorialForm(MainForm main)
        {
            UI.Init(this, "RDP Panel – quick tour", 600, 420);
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            steps.Add(new Step
            {
                Glyph = "⌂", Title = "Welcome to RDP Panel",
                Text = "All your Remote Desktop sessions in one window, with live screens, status, CPU/RAM, FPS and ping, and optional Discord control from your phone.\n\nThis tour takes about two minutes. You can start it again any time from the Help page."
            });
            steps.Add(new Step
            {
                Glyph = "+", Title = "1. Add a session",
                Text = "Click \"+ Session\" at the top. Enter the host (for example 192.168.1.20), username and password, then press Connect on the tile.\n\nPasswords are stored encrypted and only your Windows account can read them. For several sessions on one PC you need RDPWrapKit on that PC (see Credits).",
                ActionText = "Go to the overview", Action = () => main.ShowPageIndex(0)
            });
            steps.Add(new Step
            {
                Glyph = "●", Title = "2. Read a tile",
                Text = "Green border = connected, orange = reconnecting, grey = offline. In the header you see the name, the FPS (how often the picture changes per second) and the ping. The panel reconnects by itself when a session drops.\n\nDrag a header onto another tile to swap places."
            });
            steps.Add(new Step
            {
                Glyph = "⤢", Title = "3. Make it yours",
                Text = "⤢ shows one session large. Use Columns, Group by PC and search to change the view, Profiles to save a selection, and F11 for full screen. Ctrl+1 … Ctrl+9 jump to a session.",
                ActionText = "Open Profiles", Action = () => main.ShowPageIndex(4)
            });
            steps.Add(new Step
            {
                Glyph = "∿", Title = "4. Keep an eye on it",
                Text = "Monitoring shows CPU and RAM charts per session. The Log lists every connect, drop and reason in plain language, per day.",
                ActionText = "Open Monitoring", Action = () => main.ShowPageIndex(1)
            });
            steps.Add(new Step
            {
                Glyph = "⚙", Title = "5. Notifications and Discord",
                Text = "In Settings you choose when you get a warning, and connect Discord: a webhook for messages and screenshots, and/or a bot with slash commands (type /help in Discord).",
                ActionText = "Open Settings", Action = () => main.ShowPageIndex(5)
            });
            steps.Add(new Step
            {
                Glyph = "☰", Title = "6. The daily report",
                Text = "Every morning you get one Discord message with, per session, the uptime of the day before, the number of drops and the average CPU, RAM and FPS.\n\nSwitch it on in Settings → Daily report, pick a time and press \"Send report now\" to see an example.",
                ActionText = "Open Settings", Action = () => main.ShowPageIndex(5)
            });
            steps.Add(new Step
            {
                Glyph = "?", Title = "7. When something goes wrong",
                Text = "Click \"Create diagnostic file\" and you get one zip with the log and system information, without passwords, tokens or links. Send it to whoever helps you.\n\nThe Help page also has a manual and a troubleshooting list.",
                ActionText = "Create diagnostic file…", Action = () => DiagnosticFile.RunWithDialog(main)
            });
            steps.Add(new Step
            {
                Glyph = "✔", Title = "You are ready!",
                Text = "That is all. The Help page (F1) has the full manual, and the Credits page thanks everyone whose work RDP Panel relies on.",
                ActionText = "Open Help", Action = () => main.ShowPageIndex(6)
            });

            lblGlyph.Font = new Font("Segoe UI Symbol", 30f);
            lblGlyph.ForeColor = Theme.Accent;
            lblGlyph.BackColor = Theme.Raised;
            lblGlyph.TextAlign = ContentAlignment.MiddleCenter;
            lblGlyph.SetBounds(24, 24, 80, 80);
            Controls.Add(lblGlyph);

            lblCount.ForeColor = Theme.FgMuted;
            lblCount.BackColor = Color.Transparent;
            lblCount.Font = Theme.UI(9f);
            lblCount.SetBounds(124, 22, 440, 20);
            Controls.Add(lblCount);

            lblTitle.Font = Theme.UI(16f, FontStyle.Bold);
            lblTitle.ForeColor = Theme.Fg;
            lblTitle.BackColor = Color.Transparent;
            lblTitle.SetBounds(124, 44, 450, 40);
            Controls.Add(lblTitle);

            lblText.Font = Theme.UI(10.5f);
            lblText.ForeColor = Theme.Fg;
            lblText.BackColor = Color.Transparent;
            lblText.SetBounds(24, 124, 552, 200);
            Controls.Add(lblText);

            dots.BackColor = Color.Transparent;
            dots.SetBounds(24, 336, 552, 14);
            dots.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                for (int i = 0; i < steps.Count; i++)
                    using (var b = new SolidBrush(i == idx ? Theme.Accent : Theme.Line))
                        e.Graphics.FillEllipse(b, i * 20, 2, 10, 10);
            };
            Controls.Add(dots);

            btnBack = UI.Btn(this, "◀  Back", 24, 372, 100, false);
            btnAction = UI.Btn(this, "", 140, 372, 200, false);
            btnNext = UI.Btn(this, "Next  ▶", 476, 372, 100, true);
            var close = UI.Btn(this, "Close", 364, 372, 100, false);
            btnBack.Click += (s, e) => Go(idx - 1);
            btnNext.Click += (s, e) => { if (idx >= steps.Count - 1) Close(); else Go(idx + 1); };
            close.Click += (s, e) => Close();
            btnAction.Click += (s, e) => { var a = steps[idx].Action; if (a != null) a(); };
            CancelButton = close;
            Go(0);
        }

        void Go(int i)
        {
            idx = Math.Max(0, Math.Min(steps.Count - 1, i));
            var st = steps[idx];
            lblGlyph.Text = st.Glyph;
            lblTitle.Text = st.Title;
            lblText.Text = st.Text;
            lblCount.Text = "Step " + (idx + 1) + " of " + steps.Count;
            btnBack.Enabled = idx > 0;
            btnNext.Text = idx >= steps.Count - 1 ? "Finish" : "Next  ▶";
            btnAction.Visible = st.Action != null;
            btnAction.Text = st.ActionText;
            dots.Invalidate();
        }
    }
}
