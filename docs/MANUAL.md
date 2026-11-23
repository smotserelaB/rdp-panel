RDP Panel – Manual
1. What is it?
RDP Panel opens several Remote Desktop (RDP) sessions in one window. Each session is a tile that shows the live screen, whether it is connected, how long, CPU/RAM of that PC and the ping. If a session drops, the panel reconnects by itself (waiting a bit longer after every failed try, and giving up after several attempts so a wrong password cannot lock your account).
2. Installing
Easiest: download `RdpPanel-Setup-x.y.z.exe` from the Releases page and run it.
Portable: download the zip, unzip, run `RdpPanel.exe`.
Windows SmartScreen may warn because the program is not code-signed. Click "More info", then "Run anyway". Check `SHA256SUMS.txt` if you want to be sure the file is intact.
3. First start
Click + Session (or use Settings on a tile with the ✎ button).
Fill in host (`192.168....` or `192.168.....`), username and password. Passwords are stored encrypted with Windows DPAPI, so only your Windows account can read them.
Press Connect. "Connect automatically at startup" is on by default.
On the PC you connect to, Remote Desktop must be turned on. If you want several sessions on one PC at the same time you need RDPWrap/RDPWrapKit; that is installed separately and is not part of this program.
4. The window
Sidebar: Overview, Monitoring (big charts), Log, PC management, Profiles, Settings.
Tile header: drag it onto another tile to swap places. The ⋮ menu (or right-click) has: large view, connect/disconnect, edit link, copy link, screenshot, Wake-on-LAN, restart/shut down PC, settings.
Tile colors: green = good, orange = dropped, grey = offline.
Dashboard (bottom): number of connected sessions, total connected time today, CPU/RAM.
F11: full screen. F1: help. Ctrl+1…9: show a session large, Ctrl+0: back.
FPS (in the tile header) is how many times per second the picture of that session changes. The panel measures it itself (about 10 times per second, so the maximum is about 10), so it is not the in-game FPS. If there is no number (`– FPS`), hover over it: the tooltip tells the reason. Switch it off in Settings.
Background mode and screenshots
Screenshots (webhook and bot) and FPS need a picture of the session. Windows does not draw a minimised window, so a minimised panel gives old or black pictures.
Background mode (Settings): the minimise button makes the panel invisible and click-through (no taskbar button) but keeps it open, so sessions, FPS, screenshots and the bot keep working. The tray icon brings it back; right-click it for Open, Test screenshot and Exit.
When another window covers the panel, the pictures are taken from the window itself (with the screen copy as the normal way when nothing covers it).
Test screenshot (Settings or tray menu) shows which method works on your PC and saves test pictures in the screenshots folder.
If the panel is completely closed there are no sessions, so no pictures and no bot.
5. PC management
Wake-on-LAN: needs the MAC address (filled in automatically after the first connection) and WoL turned on in the PC's BIOS/network card.
Restart / shut down: uses WMI. Run `wmi-setup.bat` as administrator on the target PC once, then enter an administrator account of that PC in the session settings ("Administrator").
Search network: scans your /24 network for PCs with RDP (port 3389) open and lets you add them.
Import users: paste lines like `name password` and the panel creates a session per line.
6. Notifications
Settings → set after how many seconds a missing session triggers a notification, and CPU/RAM/free memory limits. Notifications go to Windows, with sound, and optionally to Discord. Use a Discord webhook (channel settings → Integrations) for simple messages and screenshots.
7. The Discord bot
Setup is in the README. What the bot adds on top of the webhook:
Slash commands (`/help` lists all of them with examples).
Buttons under screenshots and notifications.
Previews: `/restart-pc`, `/shutdown-pc`, `/profile` and `/connect`, `/disconnect`, `/reconnect` for several sessions first show what would happen (only you see it) with Run and Cancel buttons.
/live: one message that refreshes itself every 5 or 10 seconds for 30 s to 5 min. Discord limits how often a message can be edited, so it is a fast slideshow, not video. Max. 2 live views at a time, one per user, and never longer than 14 minutes in total.
Allowed users: put Discord user IDs in Settings → Allowed users. Nobody else can use the bot. Empty means everyone in the channel.
7b. Daily report
Settings → Daily report. Every morning (default 08:00) one message goes to the Discord webhook (or the bot channel): per session the uptime of the previous day, the number of drops and the average CPU, RAM and FPS. "Send report now" shows today so far. If the PC was off at that time, the report follows when the panel starts. Nothing is sent when there is no data. The measurements are saved once a minute in `metrics.csv` for 14 days.
7c. Diagnostic file
Settings or Help → Create diagnostic file. One zip with the log of 7 days, measurements of 3 days, system information and your settings, without passwords, usernames, webhook, bot token, links or screenshots. Open it to check before you send it.
7d. Help and tutorial
The Help page (F1) contains this manual. A short tour starts by itself the first time you run the panel after installing, and can be started again from the Help page.
8. Settings and files
Stored in `%APPDATA%\RdpPanel\`: `config.json`, `log.csv`, `metrics.csv`, `errors.log`, `screenshots\`. Passwords, webhook and bot token are encrypted per Windows user, so a copied `config.json` does not work on another account or PC. Never upload `config.json` to GitHub (the `.gitignore` already blocks it).
9. Troubleshooting
Problem	Check
Tile stays "Offline"	PC on? Remote Desktop enabled? Port 3389 open in the firewall?
"Cannot read CPU/RAM"	Run `wmi-setup.bat` as administrator on that PC; enter an administrator account in the session settings.
Bot does not respond	Token correct? Bot invited with `applications.commands`? Channel ID and allowed users correct?
Build fails with missing `MSTSCLib`	Run `setup.bat` (or let the GitHub workflow build it).
`– FPS`	Hover over it for the reason. Minimised panel: turn on background mode in Settings.
Old or black screenshot	Settings → Test screenshot.
10. For developers: how the code is organized
File	Purpose
`Program.cs`	Entry point, loads config and theme
`MainForm.cs`	Window: sidebar, pages, tile layout, notifications, Discord, screenshots
`SessionTile.cs`	One RDP tile: connect/retry logic, status, CPU/RAM, screenshots
`SessionDialog.cs`	Session settings dialog
`Pages.cs`	Monitoring, Log, PC management, Profiles, Settings pages
`Bot.cs`	Discord bot (gateway + REST), slash commands, previews, /live
`BotHelp.cs`	Texts for `/help`
`Services.cs`	Log, network tools (WoL, WMI, scan), startup, Discord webhook
`Stats.cs`	FPS measurement, per-minute metrics, error log
`Capture.cs`	Picture of a window (PrintWindow), blank check, change fingerprint
`build.bat` / `build.ps1`	One-click build: single exe, zip, installer (if Inno Setup is installed)
`Report.cs`	Daily report
`DiagnosticFile.cs`	Diagnostic zip
`HelpPages.cs`	Help page, tutorial, Credits page
`Config.cs`	Settings and sessions, encrypted secrets
`Controls.cs`, `Theme.cs`, `Dialogs.cs`	UI building blocks, colors, small dialogs
`AppInfo.cs`	Version number (single place)
