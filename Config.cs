using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace RdpPanel
{
    static class Secret
    {
        public static string Protect(string v)
        {
            return string.IsNullOrEmpty(v) ? "" :
                Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(v), null, DataProtectionScope.CurrentUser));
        }

        public static string Unprotect(string e)
        {
            if (string.IsNullOrEmpty(e)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(e), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
    }

    public class SessionConfig
    {
        public string Name = "Session";
        public string Note = "";            // short description/label, shown in the header of the tile
        public string ColorHex = "";        // custom color, e.g. #4F8CFF
        public string Host = "";            // IP or name, optionally with port: 192.168.1.20:3389
        public string User = "";
        public string PasswordEnc = "";     // DPAPI: only readable by your Windows account
        public string LaunchLink = "";      // e.g. your private server link
        public string Mac = "";             // for Wake-on-LAN
        public string AdminUser = "";       // admin account, only for CPU/RAM (WMI) and restart/shut down
        public string AdminPassEnc = "";
        public int Width = 1366;
        public int Height = 768;
        public bool AutoConnect = true;
        public bool Pinned;                 // shown first in the overview
        public long MutedUntilTicks;        // no Discord/balloon/sound messages for this session until then (0 = not muted)
        public int CpuWarn;                 // 0 = use the general value from Settings
        public int RamWarn;                 // 0 = use the general value from Settings
        public int FrozenMinutes;           // 0 = off; connected but the picture has not changed for this long = frozen
        public bool FrozenReconnect;        // reconnect by itself when frozen
        public bool HideInMonitoring;       // not shown on the Monitoring page and in the performance overview
        public bool HideInDailySummary;     // not shown in the Daily summary
        public bool HideInPcList;           // not shown in PC management
        public string AltId = "";           // the alt account (Alt accounts tab) that plays on this session

        [ScriptIgnore]
        public string Password
        {
            get
            {
                if (string.IsNullOrEmpty(PasswordEnc)) return "";
                try
                {
                    var raw = ProtectedData.Unprotect(Convert.FromBase64String(PasswordEnc), null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(raw);
                }
                catch { return ""; }
            }
            set
            {
                PasswordEnc = string.IsNullOrEmpty(value)
                    ? ""
                    : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
            }
        }

        [ScriptIgnore]
        public string AdminPassword
        {
            get { return Secret.Unprotect(AdminPassEnc); }
            set { AdminPassEnc = Secret.Protect(value); }
        }

        public string GetHost()
        {
            var h = (Host ?? "").Trim();
            int i = h.LastIndexOf(':');
            int p;
            if (i > 0 && h.IndexOf(':') == i && int.TryParse(h.Substring(i + 1), out p)) return h.Substring(0, i);
            return h;
        }

        public int GetPort()
        {
            var h = (Host ?? "").Trim();
            int i = h.LastIndexOf(':');
            int p;
            if (i > 0 && h.IndexOf(':') == i && int.TryParse(h.Substring(i + 1), out p)) return p;
            return 3389;
        }
    }

    public class Profile
    {
        public string Name = "";
        public int Columns = 2;
        public bool GroupByHost;
        public List<string> Sessions = new List<string>();   // names of the sessions that are visible
        public bool ScheduleOn;              // switch to this profile by itself
        public string ScheduleFrom = "08:00";
        public string ScheduleTo = "18:00";
        public int ScheduleDays = 127;       // bit 0 = Sunday ... bit 6 = Saturday
    }

    public class AppConfig
    {
        public int Columns = 2;
        public bool GroupByHost;
        public bool LightTheme;
        public string ActiveProfile = "";
        public List<SessionConfig> Sessions = new List<SessionConfig>();
        public List<AltAccount> Alts = new List<AltAccount>();   // the 'Alt accounts' tab
        public List<Profile> Profiles = new List<Profile>();

        // Discord
        public string WebhookEnc = "";
        public bool DiscordOnDrop = true;
        public bool DiscordAlerts;
        public int DiscordIntervalMin;   // 0 = off
        public int DiscordShotMode;      // 0 = whole panel, 1 = per session

        // rules for all automatic Discord messages
        public bool DiscordPaused;       // pause everything (maintenance)
        public bool QuietHoursOn;
        public string QuietFrom = "00:00";
        public string QuietTo = "07:00";
        public int QuietMode;            // 0 = no messages in the quiet hours, 1 = only drops and PCs that stop responding
        public int MaxMsgsPerHour;       // 0 = no limit; above it the messages are held back and summarised

        // overview
        public int SortMode;             // 0 = my order, 1 = name, 2 = problems first, 3 = CPU high to low, 4 = FPS low to high
        public bool SidebarCollapsed;    // the list of pages on the left shows only icons
        public string FavActions = "connectall,pause,sound,health";   // the quick buttons at the bottom
        public bool TourDone;            // the tour of the screen was shown
        public bool ChecklistHidden;     // the 'Getting started' card on the overview
        public bool EverConnected;       // a session was connected at least once (for that card)
        public int AltSort;              // sort of the Alt accounts tab: 0 = my order, 1 = name, 2 = kind, 3.. = most of an item
        public bool CompactView;         // small tiles: only the header with status, CPU and RAM

        [ScriptIgnore]
        public string WebhookUrl
        {
            get { return Secret.Unprotect(WebhookEnc); }
            set { WebhookEnc = Secret.Protect(value); }
        }

        // Discord bot
        public string BotTokenEnc = "";
        public bool BotEnabled;
        public string BotChannelId = "";   // optional: notifications with buttons + commands only here
        public string BotAllowedUsers = "";   // optional: Discord user IDs (comma separated) allowed to use the bot; empty = everyone in the channel

        [ScriptIgnore]
        public string BotToken
        {
            get { return Secret.Unprotect(BotTokenEnc); }
            set { BotTokenEnc = Secret.Protect(value); }
        }

        // notifications
        public int AlertAfterSeconds = 60;
        public bool AlertSound = true;
        public bool AlertsEnabled = true;
        public bool ScreenshotOnDrop = true;
        public bool StartWithWindows;
        public bool StartMinimized;
        // background mode: the minimise button makes the panel invisible and click-through (no taskbar button)
        // but keeps it running, so screenshots, FPS and the bot keep working. The tray icon brings it back.
        public bool BackgroundMode;
        public int CpuWarn = 90;
        public int RamWarn = 90;
        public int MinFreeMb = 500;

        // daily report (a message in Discord every morning)
        public bool ReportEnabled = true;
        public string ReportTime = "08:00";     // HH:mm
        public string LastReportDate = "";      // yyyy-MM-dd of the last report that was sent

        // FPS display (how many times per second the screen of a session changes)
        public bool ShowFps = true;

        // update check (GitHub releases)
        public bool CheckForUpdates = true;
        public string SkippedVersion = "";   // version the user clicked "Skip" on

        // first start: show the tutorial once
        public bool TutorialDone;

        // the version that was running the last time the panel was started (to show "What's new" once after an update)
        public string LastSeenVersion = "";
        public string UpdateMsgSentFor = "";   // the version for which the "updated" message was sent to the bot channel

        // window
        public int WinX = -1;
        public int WinY = -1;
        public int WinW;
        public int WinH;
        public bool WinMax;

        static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RdpPanel", "config.json");
            }
        }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = new JavaScriptSerializer().Deserialize<AppConfig>(File.ReadAllText(FilePath));
                    if (cfg != null && cfg.Sessions != null && cfg.Sessions.Count > 0)
                    {
                        if (cfg.Profiles == null) cfg.Profiles = new List<Profile>();
                        // old default size (1024x576) is automatically changed to 1366x768
                        foreach (var s in cfg.Sessions)
                            if (s.Width == 1024 && s.Height == 576) { s.Width = 1366; s.Height = 768; }
                        return cfg;
                    }
                }
            }
            catch { /* kapot bestand: begin opnieuw met standaardwaarden */ }

            var def = new AppConfig();
            for (int i = 1; i <= 4; i++)
                def.Sessions.Add(new SessionConfig { Name = "Session " + i });
            return def;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this));
            }
            catch { }
        }
    }
}
