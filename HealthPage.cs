using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RdpPanel
{
    // one thing the panel wants you to know: level 0 = information, 1 = attention, 2 = problem
    class HealthIssue
    {
        public int Level;
        public string Title = "";
        public string Text = "";
        public string Button = "";
        public Action Act;
    }

    // the page "Health": what is wrong right now, in plain language, with a button to fix it
    class HealthPage : Panel
    {
        readonly MainForm main;
        readonly FlowLayoutPanel flow = new FlowLayoutPanel();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 3000 };
        string signature = "";

        public HealthPage(MainForm main)
        {
            this.main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(16);

            var head = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Bg };
            head.Controls.Add(new Label
            {
                Text = "✚  Health",
                Font = Theme.UI(13f, FontStyle.Bold),
                ForeColor = Theme.Fg,
                AutoSize = true,
                Location = new Point(0, 2),
                BackColor = Color.Transparent
            });
            head.Controls.Add(new Label
            {
                Text = "What needs your attention right now. This list updates by itself.",
                ForeColor = Theme.FgMuted,
                AutoSize = true,
                Location = new Point(0, 32),
                BackColor = Color.Transparent
            });

            flow.Dock = DockStyle.Fill;
            flow.AutoScroll = true;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.BackColor = Theme.Bg;
            Controls.Add(flow);
            Controls.Add(head);

            timer.Tick += (s, e) => { if (Visible) Rebuild(false); };
            timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        // everything that is not fine, worst first
        public static List<HealthIssue> Collect(MainForm main)
        {
            var l = new List<HealthIssue>();
            var cfg = main.Cfg;

            if (main.Tiles.Count == 0)
                l.Add(new HealthIssue { Level = 1, Title = "No sessions yet", Text = "Add your first session to start monitoring it.", Button = "Add session", Act = main.AddSession });

            foreach (var t in main.Tiles.ToList())
            {
                if (main.IsHiddenByProfile(t)) continue;
                var tile = t;
                string n = t.Config.Name;
                if (t.StatusLabel == "Dropped")
                    l.Add(new HealthIssue { Level = 2, Title = n + " lost its connection", Text = "The session was connected and then the connection dropped. " + t.ActivityDetail, Button = "Reconnect", Act = () => tile.Reconnect() });
                else if (!t.Online && t.Unreachable)
                    l.Add(new HealthIssue
                    {
                        Level = 2,
                        Title = "The PC of " + n + " does not respond",
                        Text = "It does not answer to a ping: the PC is probably off, asleep or off the network.",
                        Button = string.IsNullOrEmpty(t.Config.Mac) ? "Edit session" : "Turn on (Wake-on-LAN)",
                        Act = () => { if (string.IsNullOrEmpty(tile.Config.Mac)) main.EditSession(tile); else tile.DoWake(); }
                    });
                else if (!t.Online && !string.IsNullOrWhiteSpace(t.Config.Host))
                    l.Add(new HealthIssue { Level = 1, Title = n + " is not connected", Text = t.ActivityState + ". " + t.ActivityDetail, Button = "Connect", Act = () => tile.Connect() });

                if (t.FrozenSeconds >= 60)
                    l.Add(new HealthIssue { Level = 2, Title = n + " looks frozen", Text = "The picture has not changed for " + UI.Dur(t.FrozenSeconds) + ". The game or the PC may have hung.", Button = "Reconnect", Act = () => tile.Reconnect() });

                if (t.Online && !double.IsNaN(t.Cpu) && t.Cpu >= t.CpuLimit)
                    l.Add(new HealthIssue { Level = 1, Title = n + ": CPU is high (" + t.Cpu.ToString("0") + "%)", Text = "The PC is working hard (limit " + t.CpuLimit + "%). The session may be slow." });
                if (t.Online && !double.IsNaN(t.Ram) && t.Ram >= t.RamLimit)
                    l.Add(new HealthIssue { Level = 1, Title = n + ": memory is high (" + t.Ram.ToString("0") + "%)", Text = "The PC is almost out of memory (limit " + t.RamLimit + "%). Close other programs or restart it." });
            }

            string bot = main.BotState;
            if (cfg.BotEnabled && bot != "connected")
                l.Add(new HealthIssue
                {
                    Level = bot.StartsWith("disconnected") || bot.StartsWith("no token") ? 2 : 1,
                    Title = "The Discord bot is not connected",
                    Text = "Status: " + bot + ". Check the token in Settings and that the bot is invited to your server.",
                    Button = "Open Settings",
                    Act = () => main.ShowPageIndex(5)
                });
            if ((cfg.DiscordOnDrop || cfg.DiscordAlerts || cfg.DiscordIntervalMin > 0) && !Discord.IsValid(cfg.WebhookUrl))
                l.Add(new HealthIssue { Level = 1, Title = "Discord messages are on, but there is no webhook", Text = "Messages to Discord need a webhook link.", Button = "Open Settings", Act = () => main.ShowPageIndex(5) });
            if (!cfg.AlertsEnabled)
                l.Add(new HealthIssue { Level = 1, Title = "Notifications are off", Text = "You get no warnings (balloon, sound, Discord) at all.", Button = "Turn on", Act = () => main.SetNotifications(true) });

            var upd = main.UpdateAvailableText;
            if (upd != null)
                l.Add(new HealthIssue { Level = 1, Title = "A new version is available (" + upd + ")", Text = "Update to get the newest features and fixes.", Button = "Update…", Act = main.ShowUpdate });

            if (cfg.DiscordPaused)
                l.Add(new HealthIssue { Level = 0, Title = "Discord messages are paused", Text = "You do not get automatic messages in Discord until you resume.", Button = "Resume", Act = () => { cfg.DiscordPaused = false; cfg.Save(); } });
            if (!cfg.AlertSound)
                l.Add(new HealthIssue { Level = 0, Title = "Sound is off", Text = "The panel and your sessions are muted.", Button = "Turn on", Act = () => main.SetSound(true) });

            return l.OrderByDescending(i => i.Level).ToList();
        }

        public void Rebuild() { Rebuild(true); }

        void Rebuild(bool force)
        {
            var issues = Collect(main);
            string sig = string.Join("|", issues.Select(i => i.Level + i.Title + i.Text));
            if (!force && sig == signature) return;
            signature = sig;

            flow.SuspendLayout();
            flow.Controls.Clear();
            int w = Math.Max(500, flow.ClientSize.Width - 30);
            if (issues.Count == 0)
            {
                flow.Controls.Add(Card(new HealthIssue { Level = -1, Title = "Everything looks fine", Text = "All sessions on your screens are connected and nothing needs attention." }, w));
            }
            else
                foreach (var i in issues) flow.Controls.Add(Card(i, w));
            flow.ResumeLayout();
        }

        Control Card(HealthIssue i, int width)
        {
            var card = new RoundedPanel { Width = width, Height = 82, Margin = new Padding(0, 0, 0, 10) };
            Color c = i.Level >= 2 ? Theme.Bad : i.Level == 1 ? Theme.Warn : i.Level == 0 ? Theme.FgMuted : Theme.Good;
            string icon = i.Level >= 2 ? "✖" : i.Level == 1 ? "!" : i.Level == 0 ? "i" : "✔";
            card.Controls.Add(new Label
            {
                Text = icon,
                ForeColor = c,
                Font = Theme.UI(16f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(46, 82),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            });
            bool hasButton = i.Button.Length > 0;
            int textW = Math.Max(200, width - 60 - (hasButton ? 190 : 20));
            card.Controls.Add(new Label
            {
                Text = i.Title,
                ForeColor = Theme.Fg,
                Font = Theme.UI(10.5f, FontStyle.Bold),
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(52, 10),
                Size = new Size(textW, 22),
                BackColor = Color.Transparent
            });
            card.Controls.Add(new Label
            {
                Text = i.Text,
                ForeColor = Theme.FgMuted,
                AutoSize = false,
                Location = new Point(52, 34),
                Size = new Size(textW, 40),
                BackColor = Color.Transparent
            });
            if (hasButton)
            {
                var b = UI.Btn(card, i.Button, width - 180, 26, 160, i.Level >= 2);
                var act = i.Act;
                b.Click += (s, e) => { if (act != null) act(); Rebuild(true); };
            }
            return card;
        }
    }
}
