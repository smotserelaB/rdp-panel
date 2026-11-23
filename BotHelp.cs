using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RdpPanel
{
    class HelpEntry
    {
        public string Name = "";
        public string Group = "";
        public string Short = "";
        public string Usage = "";
        public string Example = "";
        public string Output = "";   // mock-up of the reply
    }

    // Texts for /help: groups, explanation per command and example output
    static class BotHelp
    {
        public static readonly string[] Groups = { "Screens", "Sessions", "PC management", "Other" };

        public static readonly List<HelpEntry> All = new List<HelpEntry>
        {
            // ---- Screens
            new HelpEntry { Name = "screen", Group = "Screens",
                Short = "Shows the screen of a session",
                Usage = "/screen [session]", Example = "/screen session:1",
                Output = "🟢 Macro1 (127.0.0.2) – connected 52m 24s\n[screenshot]\n[1] [2] [3] [4]   ◀  🔄 Refresh  ▶  All" },
            new HelpEntry { Name = "overview", Group = "Screens",
                Short = "Screenshot of the whole panel (same as /screen all)",
                Usage = "/overview", Example = "/overview",
                Output = "📊 Overview 16:58:12\n🟢 1. Macro1 – connected 52m – 2 ms – CPU 17% RAM 76%\n🟢 2. macro2 – connected 52m – 6 ms – CPU 57% RAM 71%\n🔴 4. Session 4 – offline\n[screenshot of the panel]" },
            new HelpEntry { Name = "live", Group = "Screens",
                Short = "Keeps a screen automatically up to date for a while (only you can see it)",
                Usage = "/live [session] [duration] [interval]", Example = "/live session:Macro1 duration:2 minutes interval:every 5 seconds",
                Output = "🔴 LIVE · Macro1 (127.0.0.2)\n⏱ 98 s left · refreshes every 5 s · 16:58:12\n[screenshot, replaced every time]\n⏹ Stop  ➕ 30 s  ⏸ Pause  ◀  ▶\n(without a session: all connected sessions in a grid)" },
            new HelpEntry { Name = "compare", Group = "Screens",
                Short = "Two sessions side by side in one image",
                Usage = "/compare first second", Example = "/compare first:1 second:2",
                Output = "🔍 Comparison: Macro1 ↔ macro2\n[two screenshots side by side]\n🟢 1. Macro1 – connected 52m\n🟢 2. macro2 – connected 52m" },
            new HelpEntry { Name = "large", Group = "Screens",
                Short = "Shows a session large on the PC itself (empty = back to overview)",
                Usage = "/large [session]", Example = "/large session:macro2",
                Output = "✅ macro2 is now shown large on the PC." },

            // ---- Sessions
            new HelpEntry { Name = "status", Group = "Sessions",
                Short = "Status of all sessions",
                Usage = "/status", Example = "/status",
                Output = "🟢 1. Macro1 – connected 52m 24s – 2 ms – CPU 17% RAM 76%\n🟢 2. macro2 – connected 52m 22s – 6 ms – CPU 57% RAM 71%\n🔴 4. Session 4 – offline" },
            new HelpEntry { Name = "connect", Group = "Sessions",
                Short = "Connects one session, or all sessions (with preview)",
                Usage = "/connect [session]", Example = "/connect session:all",
                Output = "👁 Preview – /connect (nothing executed yet)\n🔴 Session 4 – will be connected\n🟢 Macro1 – is already connected, stays that way\n[✅ Run] [Cancel]" },
            new HelpEntry { Name = "disconnect", Group = "Sessions",
                Short = "Disconnects one session, or all sessions (with preview)",
                Usage = "/disconnect [session]", Example = "/disconnect session:3",
                Output = "✅ Session 3 – disconnected" },
            new HelpEntry { Name = "reconnect", Group = "Sessions",
                Short = "Reconnects, useful for a frozen screen (with preview)",
                Usage = "/reconnect [session]", Example = "/reconnect session:macro2",
                Output = "✅ macro2 – reconnecting started" },
            new HelpEntry { Name = "link", Group = "Sessions",
                Short = "Changes the Roblox link of a session",
                Usage = "/link session url", Example = "/link session:1 url:https://...",
                Output = "✅ Link of Macro1 updated (applies to a new session)." },

            // ---- PC management
            new HelpEntry { Name = "restart-pc", Group = "PC management",
                Short = "Restarts the PC of a session (preview first)",
                Usage = "/restart-pc session", Example = "/restart-pc session:macro2",
                Output = "👁 Preview – PC restart (nothing executed yet)\nPC: 192.168.1.65\nSessions that stop immediately: 🟢 macro2, 🟢 Session 3\nAccount used for the command: admin account\n[✅ Run] [Cancel]" },
            new HelpEntry { Name = "shutdown-pc", Group = "PC management",
                Short = "Shuts down the PC of a session (preview first)",
                Usage = "/shutdown-pc session", Example = "/shutdown-pc session:macro2",
                Output = "👁 Preview – PC shut down (nothing executed yet)\nAfterwards you have to turn the PC on yourself, for example with /wol.\n[✅ Run] [Cancel]" },
            new HelpEntry { Name = "wol", Group = "PC management",
                Short = "Turns a PC on with Wake-on-LAN",
                Usage = "/wol session", Example = "/wol session:macro2",
                Output = "✅ Wake-on-LAN sent for macro2." },
            new HelpEntry { Name = "profile", Group = "PC management",
                Short = "Switches profile (preview first)",
                Usage = "/profile name", Example = "/profile name:Night",
                Output = "👁 Preview – /profile\nProfile Night: 2 sessions visible: Macro1, macro2\n[✅ Run] [Cancel]" },

            // ---- Other
            new HelpEntry { Name = "help", Group = "Other",
                Short = "This help (only you can see it)",
                Usage = "/help [command]", Example = "/help command:live",
                Output = "Overview with buttons per group, or an explanation of one command." },
            new HelpEntry { Name = "ping", Group = "Other",
                Short = "Is the bot alive and how fast does it respond?",
                Usage = "/ping", Example = "/ping",
                Output = "🏓 Pong! The bot is running.\n⏱ Response time: 120 ms\n📦 Panel v" + AppInfo.Version + "\n🖥 3 of 4 sessions connected" },
            new HelpEntry { Name = "log", Group = "Other",
                Short = "Latest events of today",
                Usage = "/log", Example = "/log",
                Output = "16:58  Macro1 connected\n16:41  Session 4 disconnected – PC not reachable" },
            new HelpEntry { Name = "summary", Group = "Other",
                Short = "Connected time per session today",
                Usage = "/summary", Example = "/summary",
                Output = "Connected time today:\nMacro1 – 17h 29m\nmacro2 – 17h 24m\nSession 4 – 0h 00m" },
            new HelpEntry { Name = "quiet", Group = "Other",
                Short = "Mutes notifications for a while (0 = back on)",
                Usage = "/quiet minutes", Example = "/quiet minutes:30",
                Output = "🔕 Notifications are off for 30 minutes." },
        };

        public static HelpEntry Find(string name)
        {
            string n = (name ?? "").Trim().TrimStart('/').ToLowerInvariant();
            if (n.Length == 0) return null;
            return All.FirstOrDefault(e => e.Name == n)
                ?? All.FirstOrDefault(e => e.Name.StartsWith(n, StringComparison.Ordinal));
        }

        public static string Overview(string botState)
        {
            var sb = new StringBuilder();
            sb.Append("📖 **Help – RDP Panel bot**\n");
            sb.Append("Choose a group below, or type `/help command:screen` for an explanation with an example.\n\n");
            foreach (var g in Groups)
                sb.Append("**" + g + "** – " + string.Join("  ", All.Where(e => e.Group == g).Select(e => "`/" + e.Name + "`")) + "\n");
            sb.Append("\n");
            sb.Append("Bot: " + botState + " · Panel v" + AppInfo.Version);
            if (AppInfo.ManualUrl.Length > 0) sb.Append(" · [Manual](" + AppInfo.ManualUrl + ")");
            return sb.ToString();
        }

        public static string GroupText(string group)
        {
            var sb = new StringBuilder();
            sb.Append("📖 **" + group + "**\n");
            foreach (var e in All.Where(x => x.Group == group))
                sb.Append("`" + e.Usage + "` – " + e.Short + "\n");
            sb.Append("\nClick a command for an explanation and an example.");
            return sb.ToString();
        }

        public static string Detail(HelpEntry e)
        {
            var sb = new StringBuilder();
            sb.Append("**/" + e.Name + "** – " + e.Short + "\n");
            sb.Append("**Usage:** `" + e.Usage + "`\n");
            sb.Append("**Example:** `" + e.Example + "`\n");
            sb.Append("**What you get back:**\n```\n" + e.Output + "\n```");
            return sb.ToString();
        }
    }
}
