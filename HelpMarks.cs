using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace RdpPanel
{
    // a small round "?" with a short explanation when you point at it
    class HelpDot : Control
    {
        bool hover;

        public HelpDot()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(18, 18);
            Cursor = Cursors.Help;
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            ButtonRenderer.DrawParentBackground(g, ClientRectangle, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color c = hover ? Theme.Accent : Theme.FgMuted;
            using (var pen = new Pen(c, 1.4f)) g.DrawEllipse(pen, r);
            using (var f = Theme.UI(8f, FontStyle.Bold))
                TextRenderer.DrawText(g, "?", f, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // puts a "?" behind the settings that need an explanation (found by the start of their text)
    static class HelpMarks
    {
        static readonly ToolTip tip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 150, ReshowDelay = 100 };

        static readonly string[][] Items =
        {
            new[] { "Active profile", "The profile decides which sessions you see on the screens. You can also change it on the Profiles page." },
            new[] { "Sound on a notification", "Plays a sound when a session drops or has a problem. The switch 'Sound' at the bottom turns all sound of the panel off." },
            new[] { "Start the panel automatically", "Starts the panel when you log in to Windows and connects the sessions that have 'connect automatically' on." },
            new[] { "Start minimised", "When Windows starts the panel, it starts in the tray instead of opening the window." },
            new[] { "Background mode", "The minimise button hides the window, but the panel keeps monitoring and keeps your sessions connected." },
            new[] { "Send a screenshot to Discord when", "When a session drops, the last picture of your panel is sent to the Discord webhook." },
            new[] { "Also send all notifications as text", "Every warning is also sent to Discord as a text message." },
            new[] { "Discord bot on", "Turns the Discord bot on: you can use commands like /overview and press the buttons in Discord." },
            new[] { "Pause all automatic", "Stops all automatic Discord messages, for example during maintenance. Commands and tests still work." },
            new[] { "Quiet hours", "In this time window fewer messages are sent: nothing, or only drops and PCs that do not respond." },
            new[] { "Save a screenshot when", "Saves a picture of the panel on this PC when a session drops, so you can see what happened." },
            new[] { "Show FPS", "Shows how often the picture of a session changes. It is also used to find frozen sessions." },
            new[] { "Send the daily report", "Every morning a report with the uptime, drops and average CPU, RAM and FPS of the day before." },
            new[] { "Check for updates", "Looks for a new version at startup and every 6 hours." },
            new[] { "Discord webhook", "A link you make in Discord (channel settings, Integrations, Webhooks). The panel sends its messages to it." },
            new[] { "Send an overview with images", "Sends a picture of your sessions to Discord every N minutes." },
            new[] { "Which image is sent", "Choose the whole panel in one picture, or one picture per session." },
            new[] { "Bot token", "The secret code of your bot from discord.com/developers. Keep it private." },
            new[] { "Channel ID", "Limits the bot to one channel. In Discord (with developer mode on) right-click the channel and copy its ID." },
            new[] { "Allowed users", "Only these Discord users may use the bot commands. Empty means everyone in the channel." },
            new[] { "Maximum messages per hour", "More automatic messages than this in one hour are held back and replaced by one summary." },
            new[] { "During quiet hours", "What happens in the quiet hours: no messages at all, or only the important ones." },
            new[] { "Time of day", "The time the daily report is sent." }
        };

        public static void Add(Control box)
        {
            foreach (var c in box.Controls.Cast<Control>().ToList())
            {
                if (!(c is Label) && !(c is CheckBox)) continue;
                if (string.IsNullOrEmpty(c.Text) || c is HelpDot) continue;
                foreach (var it in Items)
                {
                    if (!c.Text.StartsWith(it[0], StringComparison.OrdinalIgnoreCase)) continue;
                    int w = TextRenderer.MeasureText(c.Text, c.Font).Width;
                    int x = c is CheckBox ? c.Left + 22 + w + 8 : c.Left + w + 8;
                    var dot = new HelpDot { Location = new Point(x, c.Top + Math.Max(0, (c.Height - 18) / 2)) };
                    tip.SetToolTip(dot, it[1]);
                    box.Controls.Add(dot);
                    break;
                }
            }
        }
    }
}
