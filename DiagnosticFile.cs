using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace RdpPanel
{
    // ---- one button: a zip with the log and system information, without any passwords ----
    static class DiagnosticFile
    {
        [DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();

        // asks where to save the file, creates it and shows where it is
        public static void RunWithDialog(MainForm main)
        {
            string name = "RdpPanel-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".zip";
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Save diagnostic file";
                sfd.Filter = "Zip file (*.zip)|*.zip";
                sfd.FileName = name;
                sfd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                sfd.OverwritePrompt = true;
                if (sfd.ShowDialog(main) != DialogResult.OK) return;

                string err = Create(main.Cfg, main.Tiles.ToList(), main.BotState, sfd.FileName);
                if (err != null)
                {
                    MessageBox.Show(main, "Could not create the diagnostic file:\r\n" + err, "Diagnostic file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                MessageBox.Show(main,
                    "Diagnostic file saved:\r\n" + sfd.FileName + "\r\n\r\n"
                    + "It contains the log of the last 7 days, system information and your settings.\r\n"
                    + "It does NOT contain passwords, usernames, the Discord webhook or bot token, links or screenshots.\r\n\r\n"
                    + "You can open the zip to check it before you send it to someone.",
                    "Diagnostic file", MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Process.Start("explorer.exe", "/select,\"" + sfd.FileName + "\""); } catch { }
            }
        }

        // returns null if it worked, otherwise the error message
        public static string Create(AppConfig cfg, List<SessionTile> tiles, string botState, string path)
        {
            try
            {
                var red = new Redactor(cfg);
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    AddText(zip, "info.txt", red.Clean(BuildInfo()));
                    AddText(zip, "settings.txt", red.Clean(BuildSettings(cfg, tiles, botState)));

                    string log = ReadRecentLines(LogService.FilePath, 7);
                    AddText(zip, "log-last-7-days.csv", red.Clean(log.Length > 0 ? "time;session;event;details;seconds\r\n" + log : "(no log yet)"));

                    string metrics = MetricsService.ReadRecentText(3);
                    AddText(zip, "metrics-last-3-days.csv", red.Clean(metrics.Length > 0 ? metrics : "(no measurements yet)"));

                    string errors = ReadTail(ErrorLog.FilePath, 200 * 1024);
                    AddText(zip, "errors.log", red.Clean(errors.Length > 0 ? errors : "(no unexpected errors recorded)"));
                }
                return null;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                return ex.Message;
            }
        }

        static void AddText(ZipArchive zip, string name, string text)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = e.Open())
            using (var w = new StreamWriter(s, new UTF8Encoding(false)))
                w.Write(text);
        }

        // reads a file that the panel may be writing to at this moment
        static string ReadShared(string path)
        {
            try
            {
                if (!File.Exists(path)) return "";
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (Exception ex) { return "(could not be read: " + ex.Message + ")"; }
        }

        static string ReadRecentLines(string path, int days)
        {
            string cutoff = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            foreach (var line in ReadShared(path).Replace("\r\n", "\n").Split('\n'))
                if (line.Length >= 10 && string.CompareOrdinal(line.Substring(0, 10), cutoff) >= 0) sb.Append(line).Append("\r\n");
            return sb.ToString();
        }

        static string ReadTail(string path, int maxChars)
        {
            string t = ReadShared(path);
            return t.Length > maxChars ? t.Substring(t.Length - maxChars) : t;
        }

        static string Reg(string key, string value)
        {
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\" + key, value, null);
                return v == null ? "(not found)" : v.ToString();
            }
            catch { return "(not readable)"; }
        }

        static string FileVer(string file)
        {
            try
            {
                string p = Path.Combine(Environment.SystemDirectory, file);
                if (!File.Exists(p)) return "(not found)";
                return FileVersionInfo.GetVersionInfo(p).FileVersion ?? "(unknown)";
            }
            catch { return "(not readable)"; }
        }

        static string YesNo(bool b) { return b ? "yes" : "no"; }

        static string BuildInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("RDP Panel diagnostic file");
            sb.AppendLine("=========================");
            sb.AppendLine("Made with the 'Create diagnostic file' button. It contains:");
            sb.AppendLine("  info.txt, settings.txt, log-last-7-days.csv, metrics-last-3-days.csv, errors.log");
            sb.AppendLine("It does NOT contain: passwords, usernames, the Discord webhook or bot token,");
            sb.AppendLine("session links, or screenshots. Your Windows user name and PC name are replaced.");
            sb.AppendLine();
            sb.AppendLine("Created:  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + " (UTC" + (TimeZoneInfo.Local.GetUtcOffset(DateTime.Now) < TimeSpan.Zero ? "-" : "+")
                + TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).ToString(@"hh\:mm") + ")");
            sb.AppendLine("Version:  RDP Panel " + AppInfo.Version);
            sb.AppendLine("Program:  " + Application.StartupPath);
            sb.AppendLine("Data:     " + Path.GetDirectoryName(LogService.FilePath));
            sb.AppendLine();

            sb.AppendLine("== Windows ==");
            string build = Reg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild");
            int b; int.TryParse(build, out b);
            string product = Reg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
            if (b >= 22000 && product.StartsWith("Windows 10")) product = product.Replace("Windows 10", "Windows 11");
            string disp = Reg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            if (disp.StartsWith("(")) disp = Reg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ReleaseId");
            sb.AppendLine("Windows:        " + product + " " + disp + " (build " + build + "." + Reg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR") + ")");
            sb.AppendLine("System type:    " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit") + ", panel runs as " + (Environment.Is64BitProcess ? "64-bit" : "32-bit"));
            sb.AppendLine(".NET Framework: " + Environment.Version + " (release " + Reg(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release") + ")");
            sb.AppendLine("Language:       " + CultureInfo.CurrentCulture.Name + " / UI " + CultureInfo.CurrentUICulture.Name);
            try
            {
                var up = TimeSpan.FromMilliseconds(GetTickCount64());
                sb.AppendLine("Windows uptime: " + (int)up.TotalDays + " d " + up.Hours + " h " + up.Minutes + " min");
            }
            catch { }
            try { sb.AppendLine("Administrator:  " + YesNo(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))); }
            catch { }
            sb.AppendLine("CPU cores:      " + Environment.ProcessorCount);
            var st = NetTools.LocalStats();
            if (st != null) sb.AppendLine("Now:            CPU " + st[0].ToString("0") + "%, memory in use " + st[1].ToString("0") + "%, free " + st[2].ToString("0") + " MB");
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                    sb.AppendLine("Display scale:  " + (g.DpiX / 96.0 * 100).ToString("0") + "%");
                sb.AppendLine("Screens:        " + Screen.AllScreens.Length + " (" + string.Join(", ", Screen.AllScreens.Select(s => s.Bounds.Width + "x" + s.Bounds.Height)) + ")");
            }
            catch { }
            try
            {
                var root = Path.GetPathRoot(Application.StartupPath);
                var di = new DriveInfo(root);
                sb.AppendLine("Free disk (" + root + "): " + (di.AvailableFreeSpace / 1048576) + " MB");
            }
            catch { }
            sb.AppendLine();

            sb.AppendLine("== Remote Desktop on this PC ==");
            sb.AppendLine("mstscax.dll (RDP client used by the panel): " + FileVer("mstscax.dll"));
            sb.AppendLine("termsrv.dll (RDP server):                   " + FileVer("termsrv.dll"));
            string svc = Reg(@"SYSTEM\CurrentControlSet\Services\TermService\Parameters", "ServiceDll");
            bool wrapped = svc.Length > 0 && !svc.StartsWith("(") && svc.IndexOf("termsrv.dll", StringComparison.OrdinalIgnoreCase) < 0;
            sb.AppendLine("TermService ServiceDll:                      " + svc + (wrapped ? "   <- not the Windows default (an RDP wrapper is probably active)" : ""));
            string deny = Reg(@"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections");
            sb.AppendLine("Remote Desktop switched on:                 " + (deny == "0" ? "yes" : deny == "1" ? "no" : deny));
            sb.AppendLine("One session per user (fSingleSessionPerUser): " + Reg(@"SYSTEM\CurrentControlSet\Control\Terminal Server", "fSingleSessionPerUser"));
            sb.AppendLine("RDP port:                                   " + Reg(@"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp", "PortNumber"));
            return sb.ToString();
        }

        static string BuildSettings(AppConfig c, List<SessionTile> tiles, string botState)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RDP Panel settings (without secrets)");
            sb.AppendLine("====================================");
            sb.AppendLine();
            sb.AppendLine("== General ==");
            sb.AppendLine("Columns: " + c.Columns + ", group by PC: " + YesNo(c.GroupByHost) + ", light theme: " + YesNo(c.LightTheme));
            sb.AppendLine("Active profile: " + (string.IsNullOrEmpty(c.ActiveProfile) ? "(all sessions)" : c.ActiveProfile) + ", profiles: " + c.Profiles.Count);
            sb.AppendLine("Start with Windows: " + YesNo(c.StartWithWindows) + ", start minimised: " + YesNo(c.StartMinimized) + ", background mode: " + YesNo(c.BackgroundMode));
            sb.AppendLine("Show FPS: " + YesNo(c.ShowFps));
            sb.AppendLine();
            sb.AppendLine("== Notifications ==");
            sb.AppendLine("Enabled: " + YesNo(c.AlertsEnabled) + ", sound: " + YesNo(c.AlertSound));
            sb.AppendLine("After " + c.AlertAfterSeconds + " s offline, CPU from " + c.CpuWarn + "%, RAM from " + c.RamWarn + "%, free memory below " + c.MinFreeMb + " MB");
            sb.AppendLine("Screenshot on a drop: " + YesNo(c.ScreenshotOnDrop));
            sb.AppendLine();
            sb.AppendLine("== Discord ==");
            sb.AppendLine("Webhook set: " + YesNo(!string.IsNullOrWhiteSpace(c.WebhookUrl)) + ", valid format: " + YesNo(Discord.IsValid(c.WebhookUrl)));
            sb.AppendLine("On drop: " + YesNo(c.DiscordOnDrop) + ", all notifications: " + YesNo(c.DiscordAlerts)
                + ", overview every " + c.DiscordIntervalMin + " min, image mode: " + (c.DiscordShotMode == 1 ? "per session" : "whole panel"));
            sb.AppendLine("Bot enabled: " + YesNo(c.BotEnabled) + ", token set: " + YesNo(!string.IsNullOrWhiteSpace(c.BotToken))
                + ", channel set: " + YesNo(!string.IsNullOrWhiteSpace(c.BotChannelId))
                + ", allowed users: " + (string.IsNullOrWhiteSpace(c.BotAllowedUsers) ? 0 : c.BotAllowedUsers.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Length)
                + ", bot status: " + botState);
            sb.AppendLine("Daily report: " + YesNo(c.ReportEnabled) + " at " + c.ReportTime + ", last sent: " + (string.IsNullOrEmpty(c.LastReportDate) ? "never" : c.LastReportDate));
            sb.AppendLine();
            sb.AppendLine("== Sessions (" + tiles.Count + ") ==");
            int n = 0;
            foreach (var t in tiles)
            {
                n++;
                var s = t.Config;
                sb.AppendLine(n + ". " + s.Name + "   host: " + s.Host + "   size: " + s.Width + "x" + s.Height + "   auto-connect: " + YesNo(s.AutoConnect));
                sb.AppendLine("   username set: " + YesNo(!string.IsNullOrWhiteSpace(s.User)) + ", password set: " + YesNo(!string.IsNullOrEmpty(s.PasswordEnc))
                    + ", admin account set: " + YesNo(!string.IsNullOrWhiteSpace(s.AdminUser)) + ", link set: " + YesNo(!string.IsNullOrWhiteSpace(s.LaunchLink))
                    + ", MAC set: " + YesNo(!string.IsNullOrWhiteSpace(s.Mac)));
                sb.AppendLine("   now: " + t.DebugState + "; status " + t.StatusLabel + "; ping " + (t.PingMs >= 0 ? t.PingMs + " ms" : "-")
                    + "; CPU " + (double.IsNaN(t.Cpu) ? "-" : t.Cpu.ToString("0") + "%") + "; RAM " + (double.IsNaN(t.Ram) ? "-" : t.Ram.ToString("0") + "%")
                    + "; FPS " + (double.IsNaN(t.Fps) ? "- (" + t.FpsReason + ")" : t.Fps.ToString("0") + " via " + t.FpsMethod) + "; connected " + t.ConnectedSeconds + " s"
                    + (t.Unreachable ? "; PC unreachable" : "") + (t.StatsError.Length > 0 ? "; CPU/RAM error: " + t.StatsError : ""));
            }
            return sb.ToString();
        }

        // removes secrets from any text that ends up in the zip (on top of leaving them out in the first place)
        class Redactor
        {
            readonly List<string> secrets = new List<string>();
            static readonly Regex Hook = new Regex(@"https://(?:(?:ptb|canary)\.)?discord(?:app)?\.com/api/webhooks/\S+", RegexOptions.IgnoreCase);
            static readonly Regex Token = new Regex(@"[A-Za-z0-9_\-]{23,28}\.[A-Za-z0-9_\-]{6,7}\.[A-Za-z0-9_\-]{27,}");

            void Add(string v)
            {
                if (!string.IsNullOrEmpty(v) && v.Trim().Length >= 4 && !secrets.Contains(v.Trim())) secrets.Add(v.Trim());
            }

            public Redactor(AppConfig c)
            {
                Add(c.WebhookUrl);
                Add(c.BotToken);
                foreach (var s in c.Sessions)
                {
                    Add(s.Password); Add(s.AdminPassword); Add(s.User); Add(s.AdminUser); Add(s.LaunchLink);
                }
                secrets.Sort((a, b) => b.Length.CompareTo(a.Length));   // long ones first
            }

            public string Clean(string text)
            {
                if (string.IsNullOrEmpty(text)) return text ?? "";
                text = Hook.Replace(text, "<discord-webhook>");
                text = Token.Replace(text, "<token>");
                foreach (var s in secrets) text = text.Replace(s, "<removed>");
                text = Replace(text, Environment.UserName, "<user>");
                text = Replace(text, Environment.MachineName, "<pc>");
                return text;
            }

            static string Replace(string text, string what, string with)
            {
                if (string.IsNullOrEmpty(what) || what.Length < 3) return text;
                return Regex.Replace(text, Regex.Escape(what), with, RegexOptions.IgnoreCase);
            }
        }
    }
}
