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
Settings → Daily report. Every morning (default 08:00) one message goes to the Discord webhook (or the bot channel): per session the uptime of the previous day, the number of drops (with times and the longest outage), the average CPU, RAM and FPS (with the peak when it is high), and a comparison with the day before. Sessions that need attention are at the top, with a one-line summary above the list. "Send report now" shows today so far. If the PC was off at that time, the report follows when the panel starts. Nothing is sent when there is no data. The measurements are saved once a minute in `metrics.csv` for 14 days.
7b2. Updates
Settings → Updates. The panel checks GitHub at startup and every 6 hours (switch it off with the checkbox). "Check now" checks right away, also when the automatic check is off, and says "You're up to date" or opens the update window. The window only appears when the installer is really on GitHub; while GitHub Actions is still building it, the panel waits and tries again every 10 minutes. With the installer version, "Update now" downloads `RdpPanel-Setup-x.y.z.exe`, checks it against `SHA256SUMS.txt`, closes the panel, installs the update and starts the panel again. If the checksum does not match, nothing is installed. A loose `RdpPanel.exe` (from the zip) opens the download page instead.
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

After an update
The first time you start a new version, a "What's new" window shows what changed (not on a fresh install). If the Discord bot has a channel, it also posts "RDP Panel updated to vX" there once. /ping in Discord tells you when a newer version is available.
The Daily summary shows only the sessions on your screens (the active profile). "+N hidden by profile" at the bottom counts the others. The daily report in Discord still includes them, under "Hidden by profile".

Discord message rules (Settings)
Under the Discord bot you can pause all automatic Discord messages, set quiet hours (send nothing, or only drops and PCs that stop responding) and set a maximum number of messages per hour. Above the limit messages wait and you get one summary. The daily report, bot commands and test buttons are not limited (the daily report is only stopped by Pause). Warnings on screen, sound and the log are not affected.

Overview: filter, sort, pin, mute, compact (1.3.0)
Above the tiles you choose what to show (all, problems, offline, connected) and the sort order. Pinned sessions are always first. Right-click the header of a tile (or use the menu with the three dots) to pin it or to mute its notifications for 1, 8 or 24 hours. The Compact button makes small tiles with the status, CPU and RAM. A double-click on the header shows the tile large and back. The Activity button opens a window that shows what the panel is doing with each session. The tray icon is green when all sessions on your screens are connected, yellow when some are and red when none are.
In the session settings (the pencil on a tile) you can set a CPU and RAM warning level for that session only, switch on the frozen warning (the picture has not changed for N minutes, with an optional automatic reconnect) and choose whether the session is shown on the Monitoring page. On the Monitoring page the Remove button takes a session off the page; Show all again brings them back.

Alt accounts
The tab Alt accounts is for your Bee Swarm alts. Add one with the button, then fill in the name, the kind of alt and the numbers. Everything is saved by itself.
- Use - and + next to a number (the Step box sets how much). The mouse wheel does not change numbers; it scrolls the list.
- Session of this alt: choose the RDP session that plays this alt. You see if it is online and Go to opens it large.
- Goal: put a number in the target box under an item; a line shows the progress and you get a message when you reach it.
- History: the panel saves your numbers once a day. The History button shows a chart per item and what changed today, this week and this month.
- Search, sort and the arrow to collapse a card keep a long list manageable.
- Export CSV and Import CSV: a file with all numbers, goals, notes and sessions. Import updates an alt with the same name and adds the others.
- Backup and Restore: a zip with everything, including the hive pictures, the history and the links to sessions.
- For the hive picture use Choose, Paste (a screenshot from the clipboard) or drop a file on the picture; click the picture to see it large.

Sound and search
The switch Sound on/off in the bar at the bottom turns all sounds of the panel off. Ctrl+K opens a search window for sessions, pages, alt accounts and a few actions (compact view, activity window, sound, pause Discord).

Sound off (1.4.1)
The switch Sound on/off in the bar at the bottom mutes the whole panel in the Windows volume mixer: the alert sounds and the sound of your RDP sessions. You can see and change it there too (the app is called RdpPanel).

Removing a session from a list
A session can be taken out of the Performance overview (the x on its card or right-click), the Daily summary (right-click) and PC management (the button Remove from this list, or right-click). The session keeps running and is still in the Discord report. The same right-click menu shows the removed sessions again, and in the session settings you can choose where a session is shown.

Health, getting started and more (1.5.0)
The page Health lists everything that needs attention (a session that dropped, a PC that does not respond, a frozen session, high CPU or memory, the Discord bot or webhook, a new version) with a short explanation and a button to fix it. The number next to Health in the list shows how many things need attention.
The card Getting started on the overview shows the first steps and hides itself when they are done.
Duplicate this session (in the menu of a tile) makes a copy with a new name. Edit several... changes the same setting of many sessions at once.
A profile can switch by itself: Profiles, Schedule... chooses the days and the times. At the start time the panel switches to that profile once; if you choose another profile yourself it stays until the next switch.
Settings has Export settings and Import settings: a file with your sessions, profiles and settings, without passwords, webhook link and bot token. Importing updates sessions with the same name and adds the new ones.
The Daily summary has a column Yesterday.
Shortcuts: Ctrl+K search everything, Ctrl+N add a session, Ctrl+F search the sessions, F5 reconnect the selected session, Ctrl+M sound on/off, Ctrl+Shift+C compact view, Ctrl+L the log, Ctrl+H Health.

Look and quick actions (1.6.0)
The corners are rounded and the drop-down lists follow the theme. The arrow at the top of the list of pages makes it smaller (icons only); point at an icon to see its name.
Settings has "Quick buttons at the bottom of the panel": tick the actions you want (connect all, disconnect all, reconnect problems, screenshot, pause Discord, sound, compact view, Health, Activity, search, add session, Alt accounts). The ? next to many settings explains them.
The bell at the bottom shows how many new notifications there are; click it for the last 100.
Drop a .rdp file on the top bar, the list of pages, the bottom bars or the empty part of the overview: the panel makes a session with the address, user name and screen size from the file and opens its settings so you can add the password.
Ctrl+K and then "Start the tour" shows the short tour again.

Alt accounts to Discord
The button Export to Discord on the Alt accounts tab sends every alt (name, kind, the session, the numbers, goals, note, last updated and the hive picture) and a total of all alts to the Discord webhook from Settings. Set the webhook there first.
