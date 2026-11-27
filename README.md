# RDP Panel

One window for all your RDP sessions: sidebar, tiles with live screen and status, dashboard at the bottom, and an optional Discord bot to control everything from your phone.

> Windows only (.NET Framework 4.8). Full guide: [docs/MANUAL.md](docs/MANUAL.md) – it is also built into the app (Help page, F1), with a short tour on the first start.
> Made by smotserela. See [CREDITS.md](CREDITS.md).

## Download
Go to **Releases** on this GitHub page and download either:
- `RdpPanel-Setup-x.y.z.exe` – installer (shortcut, optional start with Windows, uninstaller), or
- `RdpPanel.exe` – **one single file**, portable: download and run, nothing to install (the same exe is also in `RdpPanel-x.y.z.zip`).

`SHA256SUMS.txt` lets you check that your download is intact.

## Build it yourself
**One click:** double-click `build.bat`. It creates the RDP DLLs if needed (aximp), builds, and puts in `dist\`:
`RdpPanel.exe` (single file), `RdpPanel-x.y.z.zip`, `SHA256SUMS.txt` and, if [Inno Setup 6](https://jrsoftware.org/isdl.php) is installed, `RdpPanel-Setup-x.y.z.exe`.
Needs the .NET SDK, and internet the first time (the Costura.Fody package builds the two DLLs into the exe).

**Manual:** run `setup.bat` once, then press F5 in Visual Studio or run `dotnet build -c Release`.

**GitHub:** push a tag such as `v1.1.0`; the workflow builds the exe, zip and installer and attaches them to a release.

The program is not digitally signed, so Windows SmartScreen or a virus scanner may warn. Needs .NET Framework 4.8 (already in Windows 10/11).

## Updating
- **Installer version:** the panel announces a new version; click **Update now** (it downloads, checks the SHA256 checksum, installs and restarts). Your sessions and settings are kept.
- **Portable exe:** download the new `RdpPanel.exe` and replace the old one.
- After an update a **What's new** window shows once. The text comes from `docs/release-notes/<version>.md`, which is also used as the text of the GitHub release.
- **Building from source:** copy ALL `.cs` files from the new version over your old ones and leave the `lib` folder alone.

### Releasing a new version (for the maintainer)
1. Raise the version in `AppInfo.cs` and `RdpPanel.csproj`.
2. Write `docs/release-notes/<version>.md` in plain language (the workflow stops if it is missing).
3. Push a tag such as `v1.1.1`.

## Features
- Overview grid (adjustable columns), group per PC, search, drag to rearrange.
- Per tile: live screen, status, time, CPU/RAM with a small chart, ping, own color and label.
- Notifications for drops, unreachable PCs and high CPU/RAM (Windows notification, sound, optional Discord).
- **Discord** page: kinds of messages, second webhook with mention, reminder, bundling, weekly report, own texts, tests; own webhook/channel per session.
- Rounded look, collapsible page list, quick buttons, notification bell, .rdp drop, tour, number fields without arrows.
- **Health** page, getting-started card, duplicate session, edit several sessions, profiles on a schedule, export/import of the settings, yesterday in the Daily summary, more shortcuts.
- **Alt accounts** tab (Bee Swarm alts with their items, goals, history, a hive picture, CSV export/import and backup); Ctrl+K search; one switch for all sound; overview filter and sort, pin, mute per session, compact view, activity window, colored tray icon, frozen-session warning, own CPU/RAM levels per session.
- **Rules for Discord messages**: pause all, quiet hours, maximum per hour (Settings).
- Screenshot of the whole panel; screenshot on a drop; Discord webhook for drops, manually and every N minutes.
- Discord bot with slash commands and buttons (see below).
- Log with explanations in plain language, daily summary (only the sessions on your screens; sessions hidden by the active profile are counted on a separate line).
- **FPS** per session (how often the picture changes per second, max. about 10), shown in the tile header; hover for the reason when there is no number.
- **Background mode**: the minimise button hides the panel but keeps it working, so screenshots (webhook and bot) and FPS also work when it is not on screen. Tray icon to open it again. **Test screenshot** button in Settings and the tray menu.
- **Daily report** in Discord every morning (sessions hidden by the active profile are reported under their own heading): summary, then per session uptime (with comparison to the day before), drops with times and longest outage, average and peak CPU/RAM, FPS.
- **Updates from inside the app**: a new version is announced (only when the installer is online), and with the installer version "Update now" downloads it, checks the SHA256 checksum, installs it and restarts the panel. "Check now" in Settings.
- **Diagnostic file**: one button makes a zip with the log and system information, without passwords or tokens.
- **Help page and tutorial** inside the app, and a **Credits** page.
- PC management: Wake-on-LAN, restart/shut down, network scan, import users.
- Admin account per session for CPU/RAM and restarting other PCs (run `wmi-setup.bat` on that PC).
- Profiles, dark/light theme, F11 full screen, start with Windows.

## Setting up the Discord bot
1. discord.com/developers/applications: New Application, Bot tab, Reset Token, copy the token.
2. OAuth2, URL Generator: scopes `bot` and `applications.commands`; permissions View Channels, Send Messages, Attach Files. Open the link and add the bot to your (private) server.
3. In the panel: Settings, Discord bot: paste the token, tick "Discord bot on", Save. Optionally enter a channel ID: notifications with buttons appear there and commands only work there.
4. Type `/` in Discord. Type `/help` for explanations with examples (only you can see them).
5. Optional: enter your Discord user ID under "Allowed users" so only you can use the bot.

### Commands
| Command | What it does |
|---|---|
| `/help [command]` | Explanation and examples |
| `/screen [session]` | Show a screen, with buttons |
| `/overview` | Screenshot of the whole panel |
| `/live [session] [duration] [interval]` | Keep a screen refreshing (only you see it, max. 2 at a time) |
| `/compare first second` | Two sessions side by side |
| `/status` | Status of all sessions |
| `/connect` `/disconnect` `/reconnect` | One session or all; with several you get a preview first |
| `/restart-pc` `/shutdown-pc` | Preview first, then confirm |
| `/wol` | Turn a PC on with Wake-on-LAN |
| `/profile` | Switch profile, with preview |
| `/link` | Change the Roblox link |
| `/large` | Show a session large on the PC |
| `/log` `/summary` | Today's events and connected time |
| `/ping` | Bot alive, response time, version, and whether an update is available |
| `/quiet [minutes]` | Mute notifications |

## Files
- `%APPDATA%\RdpPanel\config.json` – settings (passwords, webhook and bot token encrypted with Windows DPAPI)
- `%APPDATA%\RdpPanel\log.csv` – log
- `%APPDATA%\RdpPanel\metrics.csv` – CPU/RAM/FPS per minute, kept 14 days (for the daily report)
- `%APPDATA%\RdpPanel\errors.log` – unexpected errors
- `%APPDATA%\RdpPanel\screenshots\` – screenshots taken on a drop

> Note: log lines written by older Dutch versions use Dutch event names and are not counted in the new summaries.
