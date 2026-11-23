using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RdpPanel
{
    // Discord bot with slash commands and buttons (gateway + REST, no extra libraries)
    class DiscordBot
    {
        const string Api = "https://discord.com/api/v10";
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        class BotFatal : Exception { public BotFatal(string m) : base(m) { } }

        class TileInfo
        {
            public SessionTile Tile;
            public string Name = "";
            public string Host = "";
            public bool Online;
            public int Seconds;
            public int Ping = -1;
            public double Cpu = double.NaN;
            public double Ram = double.NaN;
            public double Fps = double.NaN;
        }

        readonly MainForm main;
        readonly AppConfig cfg;
        CancellationTokenSource cts;
        string appId = "";
        readonly HashSet<string> registered = new HashSet<string>();

        // live view (/live)
        const int LiveMaxSeconds = 300;        // maximum duration when starting
        const int LiveHardMaxSeconds = 840;    // with "+30 s" never longer than this (Discord token is valid for 15 min)
        const int LiveMinInterval = 5;
        const int LiveMaxTotal = 2;            // live views running at the same time
        readonly List<LiveSession> lives = new List<LiveSession>();

        class LiveSession
        {
            public string Key = "";
            public string Token = "";
            public string UserId = "";
            public string Target = "";         // session name, empty = all sessions in a grid
            public DateTime Start, End;
            public int Interval = 5;
            public volatile bool Stop;
            public volatile bool Paused;
            public volatile int Move;          // -1 = previous session, 1 = next
            public readonly SemaphoreSlim Wake = new SemaphoreSlim(0);
            public byte[] LastJpeg;
        }

        public string State = "off";
        public bool Running { get { return State == "connected"; } }

        public DiscordBot(MainForm main, AppConfig cfg)
        {
            this.main = main;
            this.cfg = cfg;
        }

        // ------------------------------------------------------------ starting and stopping

        public void Start()
        {
            Stop();
            if (string.IsNullOrWhiteSpace(cfg.BotToken)) { State = "no token entered"; return; }
            cts = new CancellationTokenSource();
            State = "connecting…";
            var ct = cts.Token;
            Task.Run(() => RunAsync(ct));
        }

        public void Stop()
        {
            if (cts != null) { cts.Cancel(); cts = null; }
            State = "off";
            appId = "";
            registered.Clear();
            lock (lives) foreach (var l in lives) { l.Stop = true; l.Wake.Release(); }
        }

        async Task RunAsync(CancellationToken ct)
        {
            int fails = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using (var ws = new ClientWebSocket())
                    {
                        await ws.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), ct);
                        await GatewayLoop(ws, ct);
                    }
                    fails = 0;
                }
                catch (OperationCanceledException) { break; }
                catch (BotFatal ex) { State = ex.Message; return; }
                catch (Exception ex) { State = "disconnected: " + ex.Message; }

                if (!ct.IsCancellationRequested && State == "connected") State = "reconnecting…";
                try { await Task.Delay(Math.Min(60000, 5000 * (++fails)), ct); }
                catch { break; }
            }
        }

        static async Task<string> ReceiveText(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[16384];
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close) return null;
                    ms.Write(buf, 0, r.Count);
                    if (r.EndOfMessage) break;
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        async Task GatewayLoop(ClientWebSocket ws, CancellationToken ct)
        {
            var sendGate = new SemaphoreSlim(1, 1);
            Func<object, Task> send = async obj =>
            {
                var bytes = Encoding.UTF8.GetBytes(Json.Serialize(obj));
                await sendGate.WaitAsync(ct);
                try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
                finally { sendGate.Release(); }
            };

            int? seq = null;
            var hbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    string msg = await ReceiveText(ws, ct);
                    if (msg == null) break;
                    var o = Json.DeserializeObject(msg) as Dictionary<string, object>;
                    if (o == null) continue;

                    int op = ToInt(G(o, "op"));
                    if (G(o, "s") != null) seq = ToInt(G(o, "s"));

                    if (op == 10)   // Hello
                    {
                        int interval = ToInt(G(D(G(o, "d")), "heartbeat_interval"));
                        var tok = hbCts.Token;
                        var ignored = Heartbeat(interval, send, () => seq, tok);
                        await send(O("op", 2, "d", O(
                            "token", cfg.BotToken.Trim(),
                            "intents", 1,   // GUILDS: needed to hear when the bot is added to a server
                            "properties", O("os", "windows", "browser", "rdp-panel", "device", "rdp-panel"))));
                    }
                    else if (op == 1) await send(O("op", 1, "d", seq));
                    else if (op == 7 || op == 9) { await Task.Delay(2000, ct); return; }   // reconnect
                    else if (op == 0) Dispatch(S(G(o, "t")), D(G(o, "d")));
                }

                if (ws.CloseStatus.HasValue && (int)ws.CloseStatus.Value == 4004)
                    throw new BotFatal("invalid bot token");
            }
            finally
            {
                hbCts.Cancel();
            }
        }

        static async Task Heartbeat(int interval, Func<object, Task> send, Func<int?> seq, CancellationToken t)
        {
            try
            {
                await Task.Delay((int)(interval * new Random().NextDouble()), t);
                while (!t.IsCancellationRequested)
                {
                    await send(O("op", 1, "d", seq()));
                    await Task.Delay(interval, t);
                }
            }
            catch { }
        }

        void Dispatch(string t, Dictionary<string, object> d)
        {
            if (d == null) return;
            if (t == "READY")
            {
                appId = S(G(D(G(d, "application")), "id")) ?? "";
                State = "connected";
                foreach (var g in Arr(G(d, "guilds"))) Register(S(G(D(g), "id")));
            }
            else if (t == "GUILD_CREATE")
            {
                Register(S(G(d, "id")));
            }
            else if (t == "INTERACTION_CREATE")
            {
                HandleInteraction(d);
            }
        }

        // ------------------------------------------------------------ registering commands

        static Dictionary<string, object> Cmd(string name, string desc, params object[] options)
        {
            return O("name", name, "description", desc, "type", 1, "options", options.ToList());
        }

        static object Opt(string name, string desc, int type, bool required, bool auto)
        {
            var d = O("name", name, "description", desc, "type", type, "required", required);
            if (auto) d["autocomplete"] = true;
            return d;
        }

        static object OptC(string name, string desc, int type, bool required, params object[] nameValue)
        {
            var d = O("name", name, "description", desc, "type", type, "required", required);
            var choices = new List<object>();
            for (int i = 0; i + 1 < nameValue.Length; i += 2) choices.Add(O("name", nameValue[i], "value", nameValue[i + 1]));
            d["choices"] = choices;
            return d;
        }

        async void Register(string guildId)
        {
            if (string.IsNullOrEmpty(guildId) || appId.Length == 0 || !registered.Add(guildId)) return;
            var cmds = new List<object>
            {
                Cmd("help", "Explanation of all commands, with examples", Opt("command", "Command to explain, for example screen", 3, false, true)),
                Cmd("screen", "Show a screen (without a choice you get buttons)", Opt("session", "Name or number, for example 1 (or 'all')", 3, false, true)),
                Cmd("overview", "Screenshot of the whole panel (same as /screen all)"),
                Cmd("live", "Keep a screen automatically up to date for a while",
                    Opt("session", "Name or number, for example 1 (empty = all sessions)", 3, false, true),
                    OptC("duration", "How long, default 1 minute", 4, false, "30 seconds", 30, "1 minute", 60, "2 minutes", 120, "5 minutes", 300),
                    OptC("interval", "How often to refresh, default every 5 seconds", 4, false, "every 5 seconds", 5, "every 10 seconds", 10)),
                Cmd("compare", "Show two sessions side by side",
                    Opt("first", "First session, for example 1", 3, true, true),
                    Opt("second", "Second session, for example 2", 3, true, true)),
                Cmd("status", "Status of all sessions"),
                Cmd("connect", "Connect a session (all = with preview)", Opt("session", "Name or number, for example 1 (empty = all)", 3, false, true)),
                Cmd("disconnect", "Disconnect a session (all = with preview)", Opt("session", "Name or number, for example 1 (empty = all)", 3, false, true)),
                Cmd("reconnect", "Reconnect a session (all = with preview)", Opt("session", "Name or number, for example 1 (empty = all)", 3, false, true)),
                Cmd("restart-pc", "Restart the PC of a session (with preview)", Opt("session", "Name or number, for example 2", 3, true, true)),
                Cmd("shutdown-pc", "Shut down the PC of a session (with preview)", Opt("session", "Name or number, for example 2", 3, true, true)),
                Cmd("wol", "Turn a PC on with Wake-on-LAN", Opt("session", "Name or number, for example 2", 3, true, true)),
                Cmd("profile", "Switch profile (with preview)", Opt("name", "Profile name, for example Night (or 'all sessions')", 3, true, true)),
                Cmd("link", "Change the Roblox link of a session", Opt("session", "Name or number, for example 1", 3, true, true), Opt("url", "Full new link, for example a private server link", 3, true, false)),
                Cmd("large", "Show one session large on the PC (empty = back to overview)", Opt("session", "Name or number, for example 1", 3, false, true)),
                Cmd("log", "Latest events of today"),
                Cmd("summary", "Connected time per session today"),
                Cmd("ping", "Is the bot alive, how fast does it respond and which version is running?"),
                Cmd("quiet", "Mute notifications for a while", Opt("minutes", "Number of minutes, for example 30 (0 = back on)", 4, true, false))
            };
            string err = await Request("PUT", Api + "/applications/" + appId + "/guilds/" + guildId + "/commands",
                null, null, cmds);
            if (err != null) State = "connected (registering commands failed: " + err + ")";
        }

        // ------------------------------------------------------------ interactions

        bool ChannelAllowed(string channel)
        {
            string c = (cfg.BotChannelId ?? "").Trim();
            return c.Length == 0 || c == channel;
        }

        static string WhoId(Dictionary<string, object> i)
        {
            var u = D(G(D(G(i, "member")), "user")) ?? D(G(i, "user"));
            return S(G(u, "id")) ?? "";
        }

        bool UserAllowed(string userId)
        {
            string list = (cfg.BotAllowedUsers ?? "").Trim();
            if (list.Length == 0) return true;
            foreach (var p in list.Split(new[] { ',', ';', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (p.Trim() == userId) return true;
            return false;
        }

        static string Who(Dictionary<string, object> i)
        {
            var u = D(G(D(G(i, "member")), "user")) ?? D(G(i, "user"));
            return S(G(u, "username")) ?? "unknown";
        }

        async void HandleInteraction(Dictionary<string, object> i)
        {
            string id = S(G(i, "id")), token = S(G(i, "token"));
            int type = ToInt(G(i, "type"));
            var data = D(G(i, "data"));
            string channel = S(G(i, "channel_id"));
            try
            {
                if (type == 4) { await Autocomplete(id, token, data); return; }
                if (!ChannelAllowed(channel))
                {
                    await Callback(id, token, O("type", 4, "data", O("content", "This command cannot be used here.", "flags", 64)));
                    return;
                }
                if (!UserAllowed(WhoId(i)))
                {
                    LogService.Write("(bot)", "Denied", "user " + Who(i) + " (" + WhoId(i) + ") is not allowed to use the bot", 0);
                    await Callback(id, token, O("type", 4, "data", O("content", "You are not allowed to use this bot. Ask the owner to add your Discord ID.", "flags", 64)));
                    return;
                }
                if (type == 2) await HandleCommand(id, token, data, Who(i), WhoId(i));
                else if (type == 3) await HandleButton(id, token, S(G(data, "custom_id")), Who(i), WhoId(i));
            }
            catch (Exception ex)
            {
                try { await Edit(token, "⚠ Error: " + ex.Message, null, null); } catch { }
            }
        }

        static Dictionary<string, string> Options(Dictionary<string, object> data)
        {
            var d = new Dictionary<string, string>();
            foreach (var o in Arr(G(data, "options")))
            {
                var od = D(o);
                if (od == null) continue;
                var v = G(od, "value");
                d[S(G(od, "name"))] = v == null ? "" : Convert.ToString(v);
            }
            return d;
        }

        async Task Autocomplete(string id, string token, Dictionary<string, object> data)
        {
            string cmd = S(G(data, "name")), focused = "", value = "";
            foreach (var o in Arr(G(data, "options")))
            {
                var od = D(o);
                if (od != null && G(od, "focused") is bool && (bool)G(od, "focused"))
                {
                    focused = S(G(od, "name"));
                    value = S(G(od, "value")) ?? "";
                }
            }

            List<string> names;
            if (focused == "command")
            {
                names = BotHelp.All.Select(x => x.Name).ToList();
            }
            else if (focused == "name")
            {
                names = Ui(() => main.BotProfileNames());
                names.Insert(0, "all sessions");
            }
            else
            {
                names = Infos().Select(t => t.Name).ToList();
                if (cmd == "screen" || cmd == "connect" || cmd == "disconnect" || cmd == "reconnect" || cmd == "live") names.Insert(0, "all");
            }
            var choices = names.Where(n => n.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(25).Select(n => (object)O("name", Cut(n, 100), "value", Cut(n, 100))).ToList();
            await Callback(id, token, O("type", 8, "data", O("choices", choices)));
        }

        // replies that only the user themselves can see
        static bool IsEphemeral(string name, string arg)
        {
            switch (name)
            {
                case "help":
                case "live":
                case "profile":
                case "restart-pc":
                case "shutdown-pc":
                case "ping":
                    return true;
                case "connect":
                case "disconnect":
                case "reconnect":
                    {
                        string a = (arg ?? "").Trim().ToLowerInvariant();
                        return a.Length == 0 || a == "all" || a == "all";
                    }
            }
            return false;
        }

        async Task HandleCommand(string id, string token, Dictionary<string, object> data, string who, string userId)
        {
            string name = S(G(data, "name"));
            var opts = Options(data);
            string arg = opts.ContainsKey("session") ? opts["session"]
                : (opts.ContainsKey("name") ? opts["name"] : (opts.ContainsKey("command") ? opts["command"] : ""));
            await Callback(id, token, IsEphemeral(name, arg) ? O("type", 5, "data", O("flags", 64)) : O("type", 5));   // "working…", the answer follows
            LogService.Write("(bot)", "Command", "/" + name + (arg.Length > 0 ? " " + arg : "") + " by " + who, 0);

            var infos = Infos();
            switch (name)
            {
                case "screen":
                    if (string.IsNullOrWhiteSpace(arg))
                        await Edit(token, "Choose a screen:\n" + string.Join("\n", infos.Select((t, n) => (t.Online ? "🟢 " : "🔴 ") + (n + 1) + " = " + t.Name)), null, Components(infos, null));
                    else if (arg.Trim().ToLowerInvariant() == "all") await ShowAll(token, infos);
                    else
                    {
                        var t = Resolve(infos, arg).FirstOrDefault();
                        if (t == null) await Edit(token, "Session not found: " + arg, null, null);
                        else await ShowScreen(token, infos, t);
                    }
                    break;

                case "help":
                    {
                        var he = BotHelp.Find(arg);
                        string st = BotStateText();
                        if (he != null) await Edit(token, BotHelp.Detail(he), null, HelpDetailComponents(he));
                        else if (arg.Trim().Length > 0) await Edit(token, "Command not found: " + arg + "\n\n" + BotHelp.Overview(st), null, HelpOverviewComponents());
                        else await Edit(token, BotHelp.Overview(st), null, HelpOverviewComponents());
                        break;
                    }

                case "overview":
                    await ShowAll(token, infos);
                    break;

                case "ping":
                    {
                        long ms = LatencyMs(id);
                        string lat = ms < 0 || ms > 10000 ? "unknown (this PC's clock is out of sync with Discord)" : "about " + ms + " ms";
                        int online = infos.Count(x => x.Online);
                        bool muted = Ui(() => main.MuteUntil) > DateTime.Now;
                        await Edit(token, "🏓 Pong! The bot is running.\n⏱ Response time: " + lat + "\n📦 Panel v" + AppInfo.Version
                            + "\n🖥 " + online + " of " + infos.Count + " sessions connected\n🔔 Notifications: " + (muted ? "muted" : "on"), null, null);
                        break;
                    }

                case "live":
                    await StartLive(token, userId, infos, arg, opts);
                    break;

                case "compare":
                    {
                        var ta = Single(infos, opts.ContainsKey("first") ? opts["first"] : "");
                        var tb = Single(infos, opts.ContainsKey("second") ? opts["second"] : "");
                        if (ta == null || tb == null || ta == tb)
                        {
                            await Edit(token, "Choose two different sessions (name or number), for example 1 and 2.", null, null);
                            break;
                        }
                        byte[] img = Ui(() => GridJpeg(new List<SessionTile> { ta.Tile, tb.Tile }, 2, 800));
                        var vfiles = img == null ? null : new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("compare.jpg", img) };
                        await Edit(token, "🔍 Comparison: **" + ta.Name + "** ↔ **" + tb.Name + "**\n"
                            + StatusLine(infos.IndexOf(ta), ta) + "\n" + StatusLine(infos.IndexOf(tb), tb)
                            + (img == null ? "\nNo image available (the sessions must be connected and visible)." : ""),
                            vfiles, Components(infos, null));
                        break;
                    }

                case "status":
                    await Edit(token, string.Join("\n", infos.Select((t, n) => StatusLine(n, t))), null, null);
                    break;

                case "connect":
                case "disconnect":
                case "reconnect":
                    {
                        var list = Resolve(infos, arg);
                        if (list.Count == 0) { await Edit(token, "Session not found: " + arg, null, null); break; }
                        if (list.Count > 1)
                            await Edit(token, SessionPreview(name, list), null, DoComponents(name, arg));   // preview first
                        else
                            await RunSessions(token, name, list);
                        break;
                    }

                case "restart-pc":
                case "shutdown-pc":
                    {
                        var t = Single(infos, arg);
                        if (t == null) { await Edit(token, "Choose one session (name or number).", null, null); break; }
                        bool reboot = name == "restart-pc";
                        await Edit(token, PowerPreview(infos, t, reboot), null, ConfirmComponents(reboot, t.Name));
                        break;
                    }

                case "wol":
                    {
                        var t = Single(infos, arg);
                        if (t == null) { await Edit(token, "Choose one session (name or number).", null, null); break; }
                        string err = Ui(() => t.Tile.WakeSilent());
                        await Edit(token, err == null ? "✅ Wake-on-LAN sent for **" + t.Name + "**." : "⚠ " + err, null, null);
                        break;
                    }

                case "profile":
                    {
                        string pv = ProfilePreview(arg, infos);
                        if (pv == null) await Edit(token, "Profile not found: " + arg, null, null);
                        else await Edit(token, pv + "\nChoose Run to switch.", null, DoComponents("profile", arg));
                        break;
                    }

                case "link":
                    {
                        var t = Single(infos, arg);
                        if (t == null) { await Edit(token, "Choose one session (name or number).", null, null); break; }
                        string url = opts.ContainsKey("url") ? opts["url"].Trim() : "";
                        UiDo(() => { t.Tile.Config.LaunchLink = url; main.BotSave(); });
                        await Edit(token, "✅ Link of **" + t.Name + "** updated (applies to a new session).", null, null);
                        break;
                    }

                case "large":
                    {
                        if (string.IsNullOrWhiteSpace(arg)) { UiDo(() => main.BotFocus(null)); await Edit(token, "✅ Back to the overview.", null, null); break; }
                        var t = Single(infos, arg);
                        if (t == null) { await Edit(token, "Session not found: " + arg, null, null); break; }
                        UiDo(() => main.BotFocus(t.Tile));
                        await Edit(token, "✅ **" + t.Name + "** is now shown large on the PC.", null, null);
                        break;
                    }

                case "log":
                    {
                        var today = DateTime.Today;
                        var lines = LogService.ReadAll().Where(e => e.Time.Date == today).OrderByDescending(e => e.Time).Take(12)
                            .Select(e => e.Time.ToString("HH:mm") + "  " + e.Session + " " + e.Event.ToLowerInvariant() + (e.Detail.Length > 0 ? " – " + Cut(e.Detail, 80) : ""));
                        string text = string.Join("\n", lines);
                        await Edit(token, text.Length > 0 ? text : "No events yet today.", null, null);
                        break;
                    }

                case "summary":
                    {
                        var today = DateTime.Today;
                        var te = LogService.ReadAll().Where(e => e.Time.Date == today).ToList();
                        var lines = infos.Select(t =>
                        {
                            int secs = te.Where(e => e.Session == t.Name && e.Event == "Disconnected").Sum(e => e.Seconds) + t.Seconds;
                            return "**" + t.Name + "** – " + UI.Dur(secs);
                        });
                        await Edit(token, "Connected time today:\n" + string.Join("\n", lines), null, null);
                        break;
                    }

                case "quiet":
                    {
                        int m = 0;
                        int.TryParse(opts.ContainsKey("minutes") ? opts["minutes"] : "0", out m);
                        UiDo(() => main.MuteUntil = m > 0 ? DateTime.Now.AddMinutes(m) : DateTime.MinValue);
                        await Edit(token, m > 0 ? "🔕 Notifications are off for " + m + " minutes." : "🔔 Notifications are back on.", null, null);
                        break;
                    }

                default:
                    await Edit(token, "Unknown command.", null, null);
                    break;
            }
        }

        async Task HandleButton(string id, string token, string customId, string who, string userId)
        {
            int k = customId.IndexOf(':');
            string kind = k < 0 ? customId : customId.Substring(0, k);
            string arg = k < 0 ? "" : customId.Substring(k + 1);
            LogService.Write("(bot)", "Button", customId + " by " + who, 0);

            if (kind == "ask")   // preview + confirmation in a new message that only this user can see
            {
                string[] p = arg.Split(new[] { ':' }, 2);
                bool reboot = p[0] == "r";
                string tname = p.Length > 1 ? p[1] : "";
                var ai = Infos();
                var at = Resolve(ai, tname).FirstOrDefault();
                string atext = at != null ? PowerPreview(ai, at, reboot)
                    : "⚠ Session not found: " + tname;
                await Callback(id, token, O("type", 4, "data", O(
                    "content", Cut(atext, 1900), "flags", 64,
                    "components", ConfirmComponents(reboot, tname))));
                return;
            }

            await Callback(id, token, O("type", 6));   // update message
            var infos = Infos();

            switch (kind)
            {
                case "scr":
                case "ref":
                    {
                        var t = Resolve(infos, arg).FirstOrDefault();
                        if (t == null) await Edit(token, "Session not found.", null, null);
                        else await ShowScreen(token, infos, t);
                        break;
                    }
                case "prev":
                case "next":
                    {
                        int idx = infos.FindIndex(t => t.Name.Equals(arg, StringComparison.OrdinalIgnoreCase));
                        if (idx < 0 || infos.Count == 0) { await Edit(token, "Session not found.", null, null); break; }
                        idx = kind == "next" ? (idx + 1) % infos.Count : (idx - 1 + infos.Count) % infos.Count;
                        await ShowScreen(token, infos, infos[idx]);
                        break;
                    }
                case "all":
                    await ShowAll(token, infos);
                    break;
                case "rec":
                    {
                        var t = Resolve(infos, arg).FirstOrDefault();
                        if (t == null) { await Edit(token, "Session not found.", null, null); break; }
                        UiDo(() => t.Tile.Reconnect());
                        await Edit(token, "🔄 Reconnecting started for **" + t.Name + "**.", null, Components(infos, t));
                        break;
                    }
                case "pwr":
                    {
                        string[] p = arg.Split(new[] { ':' }, 2);
                        bool reboot = p[0] == "r";
                        var t = Resolve(infos, p.Length > 1 ? p[1] : "").FirstOrDefault();
                        if (t == null) { await Edit(token, "Session not found.", null, new List<object>()); break; }
                        string err = await t.Tile.PowerSilentAsync(reboot);
                        await Edit(token, err == null
                            ? "✅ Command sent: PC of **" + t.Name + "** is " + (reboot ? "restarting" : "shutting down") + "."
                            : "⚠ Failed: " + err + "\n(The admin account must be correct and WMI must be open on that PC.)",
                            null, new List<object>());
                        break;
                    }
                case "cancel":
                    await Edit(token, "Cancelled.", null, new List<object>());
                    break;
                case "help":
                    if (Array.IndexOf(BotHelp.Groups, arg) < 0) await Edit(token, BotHelp.Overview(BotStateText()), null, HelpOverviewComponents());
                    else await Edit(token, BotHelp.GroupText(arg), null, HelpGroupComponents(arg));
                    break;
                case "hc":
                    {
                        var he = BotHelp.Find(arg);
                        if (he == null) await Edit(token, BotHelp.Overview(BotStateText()), null, HelpOverviewComponents());
                        else await Edit(token, BotHelp.Detail(he), null, HelpDetailComponents(he));
                        break;
                    }
                case "do":   // run after a preview
                    {
                        string[] p = arg.Split(new[] { ':' }, 2);
                        string act = p[0], what = p.Length > 1 ? p[1] : "";
                        if (act == "profile")
                            await Edit(token, Ui(() => main.BotProfile(what)), null, null);
                        else
                        {
                            var dl = Resolve(infos, what);
                            if (dl.Count == 0) await Edit(token, "Session not found.", null, null);
                            else await RunSessions(token, act, dl);
                        }
                        break;
                    }
                case "live":
                    {
                        string[] p = arg.Split(new[] { ':' }, 2);
                        LiveControl(p[0], p.Length > 1 ? p[1] : "");
                        break;
                    }
                default:
                    await Edit(token, "Unknown button.", null, new List<object>());
                    break;
            }
        }

        // ------------------------------------------------------------ display

        List<TileInfo> Infos()
        {
            return Ui(() => main.BotTiles().Select(t => new TileInfo
            {
                Tile = t,
                Name = t.Config.Name,
                Host = t.Config.Host,
                Online = t.Online,
                Seconds = t.ConnectedSeconds,
                Ping = t.PingMs,
                Cpu = t.Cpu,
                Ram = t.Ram,
                Fps = t.Fps
            }).ToList());
        }

        static List<TileInfo> Resolve(List<TileInfo> tiles, string text)
        {
            string x = (text ?? "").Trim();
            if (x.Length == 0 || x.Equals("all", StringComparison.OrdinalIgnoreCase) || x.Equals("all", StringComparison.OrdinalIgnoreCase)) return tiles;
            int n;
            if (int.TryParse(x, out n) && n >= 1 && n <= tiles.Count) return new List<TileInfo> { tiles[n - 1] };
            var exact = tiles.Where(t => t.Name.Equals(x, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 0) return exact;
            return tiles.Where(t => t.Name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        static TileInfo Single(List<TileInfo> tiles, string text)
        {
            string x = (text ?? "").Trim();
            if (x.Length == 0 || x.Equals("all", StringComparison.OrdinalIgnoreCase)) return null;
            var r = Resolve(tiles, x);
            return r.Count == 1 ? r[0] : null;
        }

        static string StatusLine(int n, TileInfo t)
        {
            string s = (t.Online ? "🟢 " : "🔴 ") + (n + 1) + ". **" + t.Name + "**";
            if (!t.Online) return s + " – offline";
            s += " – connected " + UI.Dur(t.Seconds);
            if (t.Ping >= 0) s += " – " + t.Ping + " ms";
            if (!double.IsNaN(t.Cpu)) s += " – CPU " + t.Cpu.ToString("0") + "% RAM " + t.Ram.ToString("0") + "%";
            if (!double.IsNaN(t.Fps)) s += " – " + t.Fps.ToString("0") + " FPS";
            return s;
        }

        static object Btn(string label, string customId, int style)
        {
            return O("type", 2, "style", style, "label", label, "custom_id", Cut(customId, 100));
        }

        static List<object> Components(List<TileInfo> tiles, TileInfo cur)
        {
            var rows = new List<object>();
            int n = Math.Min(tiles.Count, 20);
            for (int r = 0; r * 5 < n; r++)
            {
                var btns = new List<object>();
                for (int i = r * 5; i < Math.Min(n, r * 5 + 5); i++)
                    btns.Add(Btn((i + 1).ToString(), "scr:" + tiles[i].Name, tiles[i] == cur ? 1 : (tiles[i].Online ? 3 : 4)));
                rows.Add(O("type", 1, "components", btns));
            }
            if (cur != null)
                rows.Add(O("type", 1, "components", new List<object>
                {
                    Btn("◀", "prev:" + cur.Name, 2),
                    Btn("🔄 Refresh", "ref:" + cur.Name, 1),
                    Btn("▶", "next:" + cur.Name, 2),
                    Btn("All", "all:", 2)
                }));
            return rows;
        }

        static List<object> ConfirmComponents(bool reboot, string name)
        {
            return new List<object>
            {
                O("type", 1, "components", new List<object>
                {
                    Btn(reboot ? "✅ Run (restart)" : "✅ Run (shut down)", "pwr:" + (reboot ? "r" : "s") + ":" + name, 4),
                    Btn("Cancel", "cancel:", 2)
                })
            };
        }

        // ------------------------------------------------------------ help, previews and helpers

        string BotStateText() { return State; }

        static long LatencyMs(string interactionId)
        {
            long sn;
            if (!long.TryParse(interactionId, out sn)) return -1;
            long created = (sn >> 22) + 1420070400000L;   // a Discord id contains the time it was created
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - created;
        }

        static List<object> HelpOverviewComponents()
        {
            var row = new List<object>();
            foreach (var g in BotHelp.Groups) row.Add(Btn(g, "help:" + g, 1));
            return new List<object> { O("type", 1, "components", row) };
        }

        static List<object> HelpGroupComponents(string group)
        {
            var cmds = new List<object>();
            foreach (var x in BotHelp.All.Where(y => y.Group == group)) cmds.Add(Btn("/" + x.Name, "hc:" + x.Name, 2));
            return new List<object>
            {
                O("type", 1, "components", cmds),
                O("type", 1, "components", new List<object> { Btn("◀ Back", "help:", 1) })
            };
        }

        static List<object> HelpDetailComponents(HelpEntry he)
        {
            return new List<object>
            {
                O("type", 1, "components", new List<object> { Btn("◀ " + he.Group, "help:" + he.Group, 1), Btn("All commands", "help:", 2) })
            };
        }

        static List<object> DoComponents(string action, string arg)
        {
            return new List<object>
            {
                O("type", 1, "components", new List<object> { Btn("✅ Run", "do:" + action + ":" + (arg ?? ""), 3), Btn("Cancel", "cancel:", 2) })
            };
        }

        static string SessionPreview(string action, List<TileInfo> list)
        {
            var sb = new StringBuilder();
            sb.Append("👁 **Preview – /" + action + "** (nothing executed yet)\n");
            sb.Append("This will happen to " + list.Count + " sessions:\n");
            foreach (var t in list)
            {
                string what;
                if (action == "connect") what = t.Online ? "is already connected, stays that way" : "will be connected";
                else if (action == "disconnect") what = t.Online ? "will be disconnected" : "is already offline, stays that way";
                else what = t.Online ? "will be disconnected and reconnected" : "will be reconnected";
                sb.Append((t.Online ? "🟢 " : "🔴 ") + "**" + t.Name + "** – " + what + "\n");
            }
            sb.Append("Choose Run to continue.");
            return sb.ToString();
        }

        static string PowerPreview(List<TileInfo> infos, TileInfo t, bool reboot)
        {
            string host = t.Tile.Config.GetHost();
            var same = infos.Where(x => string.Equals(x.Tile.Config.GetHost(), host, StringComparison.OrdinalIgnoreCase)).ToList();
            bool admin = !string.IsNullOrWhiteSpace(t.Tile.Config.AdminUser);
            var sb = new StringBuilder();
            sb.Append("👁 **Preview – PC " + (reboot ? "restart" : "shut down") + "** (nothing executed yet)\n");
            sb.Append("PC: **" + (host.Length > 0 ? host : t.Host) + "**\n");
            sb.Append("Sessions that stop immediately: " + string.Join(", ", same.Select(x => (x.Online ? "🟢 " : "🔴 ") + x.Name)) + "\n");
            sb.Append("Account used for the command: " + (admin ? "admin account" : "the regular RDP account (no admin account set)") + "\n");
            sb.Append(reboot ? "The PC will start again by itself." : "Afterwards you have to turn the PC on yourself, for example with /wol.");
            return sb.ToString();
        }

        // null = profile not found
        string ProfilePreview(string arg, List<TileInfo> infos)
        {
            string n = (arg ?? "").Trim();
            if (n.Length == 0 || n.StartsWith("all", StringComparison.OrdinalIgnoreCase))
                return "👁 **Preview – /profile** (nothing executed yet)\nAll " + infos.Count + " sessions will be shown: " + string.Join(", ", infos.Select(x => x.Name));
            Profile p = Ui(() => cfg.Profiles.FirstOrDefault(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                ?? cfg.Profiles.FirstOrDefault(x => x.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));
            if (p == null) return null;
            return "👁 **Preview – /profile** (nothing executed yet)\nProfile **" + p.Name + "**: " + p.Sessions.Count + " sessions visible"
                + (p.Sessions.Count > 0 ? " (" + string.Join(", ", p.Sessions) + ")" : "") + ", " + p.Columns + " columns.";
        }

        async Task RunSessions(string token, string name, List<TileInfo> list)
        {
            foreach (var t in list)
            {
                var tile = t.Tile;
                if (name == "connect") UiDo(() => tile.Connect());
                else if (name == "disconnect") UiDo(() => tile.Disconnect());
                else UiDo(() => tile.Reconnect());
                if (list.Count > 1) await Task.Delay(1200);
            }
            string verb = name == "connect" ? "connecting started" : name == "disconnect" ? "disconnected" : "reconnecting started";
            await Edit(token, "✅ " + string.Join(", ", list.Select(t => t.Name)) + " – " + verb, null, null);
        }

        // ------------------------------------------------------------ images for /live and /compare (call on the UI thread)

        static byte[] LiveJpeg(SessionTile tile)
        {
            using (var b = tile.Snapshot())
            {
                if (b == null) return null;
                int w = Math.Min(960, b.Width);
                int h = (int)((long)b.Height * w / b.Width);
                using (var small = new System.Drawing.Bitmap(w, h))
                {
                    using (var g = System.Drawing.Graphics.FromImage(small))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                        g.DrawImage(b, 0, 0, w, h);
                    }
                    return Discord.ToJpeg(small, 65);
                }
            }
        }

        static byte[] GridJpeg(List<SessionTile> tiles, int cols, int cellW)
        {
            var imgs = new List<KeyValuePair<string, System.Drawing.Bitmap>>();
            try
            {
                foreach (var t in tiles)
                {
                    var b = t.Snapshot();
                    if (b != null) imgs.Add(new KeyValuePair<string, System.Drawing.Bitmap>(t.Config.Name, b));
                }
                if (imgs.Count == 0) return null;
                int cellH = cellW * 9 / 16;
                int c = Math.Min(cols, imgs.Count);
                int rows = (imgs.Count + c - 1) / c;
                using (var canvas = new System.Drawing.Bitmap(c * cellW, rows * cellH))
                {
                    using (var g = System.Drawing.Graphics.FromImage(canvas))
                    {
                        g.Clear(System.Drawing.Color.Black);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                        using (var font = new System.Drawing.Font("Segoe UI", Math.Max(9, cellW / 50), System.Drawing.FontStyle.Bold))
                        using (var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(170, 0, 0, 0)))
                        {
                            for (int i = 0; i < imgs.Count; i++)
                            {
                                int x = (i % c) * cellW, y = (i / c) * cellH;
                                g.DrawImage(imgs[i].Value, x, y, cellW, cellH);
                                var textSize = g.MeasureString(imgs[i].Key, font);
                                g.FillRectangle(bg, x, y, textSize.Width + 12, textSize.Height + 4);
                                g.DrawString(imgs[i].Key, font, System.Drawing.Brushes.White, x + 6, y + 2);
                            }
                        }
                    }
                    return Discord.ToJpeg(canvas, 70);
                }
            }
            finally
            {
                foreach (var kv in imgs) kv.Value.Dispose();
            }
        }

        // ------------------------------------------------------------ /live

        async Task StartLive(string token, string userId, List<TileInfo> infos, string arg, Dictionary<string, string> opts)
        {
            if (infos.Count == 0) { await Edit(token, "There are no sessions.", null, null); return; }
            string target = (arg ?? "").Trim();
            bool all = target.Length == 0 || target.Equals("all", StringComparison.OrdinalIgnoreCase) || target.Equals("all", StringComparison.OrdinalIgnoreCase);
            string tname = "";
            if (!all)
            {
                var t = Single(infos, target);
                if (t == null) { await Edit(token, "Choose one session (name or number), or leave it empty for all sessions.", null, null); return; }
                tname = t.Name;
            }

            int dur = 60, iv = LiveMinInterval, tmp;
            if (opts.ContainsKey("duration") && int.TryParse(opts["duration"], out tmp)) dur = tmp;
            if (opts.ContainsKey("interval") && int.TryParse(opts["interval"], out tmp)) iv = tmp;
            dur = Math.Max(10, Math.Min(LiveMaxSeconds, dur));
            iv = Math.Max(LiveMinInterval, Math.Min(30, iv));

            LiveSession ls = null;
            lock (lives)
            {
                if (lives.Count(x => !x.Stop && x.UserId != userId) < LiveMaxTotal)
                {
                    foreach (var old in lives.Where(x => x.UserId == userId).ToList()) { old.Stop = true; old.Wake.Release(); }
                    ls = new LiveSession
                    {
                        Key = Guid.NewGuid().ToString("N").Substring(0, 8),
                        Token = token,
                        UserId = userId,
                        Target = tname,
                        Start = DateTime.UtcNow,
                        End = DateTime.UtcNow.AddSeconds(dur),
                        Interval = iv
                    };
                    lives.Add(ls);
                }
            }
            if (ls == null)
            {
                await Edit(token, "There are already " + LiveMaxTotal + " live views running. Try again in a few minutes.", null, null);
                return;
            }
            var ignored = Task.Run(() => RunLive(ls));
        }

        void LiveControl(string action, string key)
        {
            LiveSession ls;
            lock (lives) ls = lives.FirstOrDefault(x => x.Key == key);
            if (ls == null) return;
            switch (action)
            {
                case "stop": ls.Stop = true; break;
                case "more":
                    {
                        var max = ls.Start.AddSeconds(LiveHardMaxSeconds);
                        var next = ls.End.AddSeconds(30);
                        ls.End = next > max ? max : next;
                        break;
                    }
                case "pause": ls.Paused = !ls.Paused; break;
                case "prev": ls.Move = -1; break;
                case "next": ls.Move = 1; break;
            }
            ls.Wake.Release();
        }

        static List<object> LiveComponents(LiveSession ls, bool canSwitch)
        {
            var row = new List<object>
            {
                Btn("⏹ Stop", "live:stop:" + ls.Key, 4),
                Btn("➕ 30 s", "live:more:" + ls.Key, 2),
                Btn(ls.Paused ? "▶ Resume" : "⏸ Pause", "live:pause:" + ls.Key, 2)
            };
            if (ls.Target.Length > 0 && canSwitch)
            {
                row.Add(Btn("◀", "live:prev:" + ls.Key, 2));
                row.Add(Btn("▶", "live:next:" + ls.Key, 2));
            }
            return new List<object> { O("type", 1, "components", row) };
        }

        async Task RunLive(LiveSession ls)
        {
            string endText = null;
            int fails = 0;
            try
            {
                while (!ls.Stop && DateTime.UtcNow < ls.End)
                {
                    var infos = Infos();
                    if (ls.Move != 0 && ls.Target.Length > 0 && infos.Count > 0)
                    {
                        int idx = infos.FindIndex(x => x.Name == ls.Target);
                        if (idx < 0) idx = 0;
                        idx = ((idx + ls.Move) % infos.Count + infos.Count) % infos.Count;
                        ls.Target = infos[idx].Name;
                        ls.LastJpeg = null;
                    }
                    ls.Move = 0;

                    TileInfo cur = ls.Target.Length == 0 ? null : infos.FirstOrDefault(x => x.Name == ls.Target);
                    if (ls.Target.Length > 0 && cur == null) { endText = "⚠ Live stopped: the session '" + ls.Target + "' no longer exists."; break; }

                    if (!ls.Paused || ls.LastJpeg == null)
                    {
                        byte[] jpg = null;
                        try
                        {
                            var shown = infos.Where(x => x.Online).Take(6).Select(x => x.Tile).ToList();
                            jpg = Ui(() => cur == null ? GridJpeg(shown, 2, 640) : LiveJpeg(cur.Tile));
                        }
                        catch { }
                        if (jpg != null) ls.LastJpeg = jpg;
                    }

                    int left = (int)Math.Max(0, (ls.End - DateTime.UtcNow).TotalSeconds);
                    string head = cur == null ? "🔴 **LIVE** · all connected sessions"
                        : "🔴 **LIVE** · **" + cur.Name + "** (" + cur.Host + ")" + (cur.Online ? "" : " – offline");
                    string caption = head + (ls.Paused ? " · ⏸ paused" : "")
                        + "\n⏱ " + left + " s left · refreshes every " + ls.Interval + " s · " + DateTime.Now.ToString("HH:mm:ss");
                    if (ls.LastJpeg == null) caption += "\nNo image available (the session must be connected and visible).";

                    var files = ls.LastJpeg == null ? null
                        : new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("live_" + DateTime.Now.ToString("HHmmss") + ".jpg", ls.LastJpeg) };
                    string err = await Edit(ls.Token, caption, files, LiveComponents(ls, infos.Count > 1));
                    if (err != null)
                    {
                        if (++fails >= 4) { endText = "⚠ Live stopped: Discord is not responding properly (" + Cut(err, 120) + ")."; break; }
                    }
                    else fails = 0;

                    await Task.Delay(1200);   // never faster than Discord allows
                    int waitMs = ls.Paused ? 30000 : ls.Interval * 1000;
                    int remainMs = (int)Math.Max(0, (ls.End - DateTime.UtcNow).TotalMilliseconds);
                    if (!ls.Stop) await ls.Wake.WaitAsync(Math.Max(1, Math.Min(waitMs, remainMs + 1)));
                }

                if (endText == null) endText = ls.Stop ? "⏹ Live stopped." : "⏱ Live ended.";
                var last = ls.LastJpeg == null ? null
                    : new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("live_last.jpg", ls.LastJpeg) };
                await Edit(ls.Token, endText + (ls.Target.Length > 0 ? " (" + ls.Target + ")" : "") + " · last image of " + DateTime.Now.ToString("HH:mm:ss"),
                    last, new List<object>());
            }
            catch (Exception ex)
            {
                try { await Edit(ls.Token, "⚠ Live stopped: " + ex.Message, null, new List<object>()); } catch { }
            }
            finally
            {
                lock (lives) lives.Remove(ls);
            }
        }

        async Task ShowScreen(string token, List<TileInfo> infos, TileInfo t)
        {
            byte[] bytes = Ui(() =>
            {
                var b = t.Tile.Snapshot();
                if (b == null) return null;
                var j = Discord.ToJpeg(b, 80);
                b.Dispose();
                return j;
            });
            string caption = (t.Online ? "🟢 " : "🔴 ") + "**" + t.Name + "** (" + t.Host + ")" + (t.Online ? " – connected " + UI.Dur(t.Seconds) : " – offline")
                + (bytes == null ? "\nNo image available (the session must be connected and visible)." : "")
                + "\n" + DateTime.Now.ToString("HH:mm:ss");
            var files = bytes == null ? null : new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>(t.Tile.SafeFileName + ".jpg", bytes) };
            await Edit(token, caption, files, Components(infos, t));
        }

        async Task ShowAll(string token, List<TileInfo> infos)
        {
            var files = new List<KeyValuePair<string, byte[]>>();
            string age = "";
            if (cfg.DiscordShotMode == 0)
            {
                var shot = Ui(() => main.PanelShot());
                if (shot.Item1 != null)
                {
                    files.Add(new KeyValuePair<string, byte[]>("panel.jpg", shot.Item1));
                    age = WinUtil.AgeText(shot.Item2);
                }
            }
            if (files.Count == 0)
            {
                files = Ui(() =>
                {
                    var list = new List<KeyValuePair<string, byte[]>>();
                    foreach (var t in infos.Where(x => x.Online).Take(10))
                    {
                        var b = t.Tile.Snapshot();
                        if (b == null) continue;
                        list.Add(new KeyValuePair<string, byte[]>(t.Tile.SafeFileName + ".jpg", Discord.ToJpeg(b, 70)));
                        b.Dispose();
                    }
                    return list;
                });
            }
            string text = "📊 Overview " + DateTime.Now.ToString("HH:mm:ss") + age + "\n" + string.Join("\n", infos.Select((t, n) => StatusLine(n, t)));
            await Edit(token, text, files, Components(infos, null));
        }

        // notifications with buttons in the configured channel
        public async void PostAlert(string tileName, string msg)
        {
            if (appId.Length == 0 || string.IsNullOrWhiteSpace(cfg.BotChannelId)) return;
            var comps = new List<object>
            {
                O("type", 1, "components", new List<object>
                {
                    Btn("Reconnect", "rec:" + tileName, 1),
                    Btn("Screen", "scr:" + tileName, 2),
                    Btn("Restart PC", "ask:r:" + tileName, 4)
                })
            };
            string err = await Request("POST", Api + "/channels/" + cfg.BotChannelId.Trim() + "/messages",
                O("content", "⚠ " + Cut(msg, 1900), "components", comps), null, null);
            if (err != null) LogService.Write(tileName, "Discord", "bot message failed: " + err, 0);
        }

        // a plain message in the configured bot channel (used by the daily report); returns null if it worked
        public async Task<string> PostChannelMessage(string text)
        {
            if (appId.Length == 0 || string.IsNullOrWhiteSpace(cfg.BotChannelId)) return "bot channel is not set up";
            return await Request("POST", Api + "/channels/" + cfg.BotChannelId.Trim() + "/messages",
                O("content", Cut(text, 1900)), null, null);
        }

        // ------------------------------------------------------------ REST

        Task<string> Callback(string id, string token, Dictionary<string, object> body)
        {
            return Request("POST", Api + "/interactions/" + id + "/" + token + "/callback", body, null, null);
        }

        Task<string> Edit(string token, string content, List<KeyValuePair<string, byte[]>> files, List<object> components)
        {
            var p = O("content", Cut(content ?? "", 1900), "components", components ?? new List<object>());
            var att = new List<object>();
            if (files != null)
                for (int i = 0; i < files.Count; i++) att.Add(O("id", i, "filename", files[i].Key));
            p["attachments"] = att;
            return Request("PATCH", Api + "/webhooks/" + appId + "/" + token + "/messages/@original", p, files, null);
        }

        // payload: JSON object; jsonArray: (only for PUT of commands) a list as body
        async Task<string> Request(string method, string url, Dictionary<string, object> payload,
            List<KeyValuePair<string, byte[]>> files, List<object> jsonArray = null)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                req.UserAgent = "DiscordBot (RdpPanel, " + AppInfo.Version + ")";
                req.Headers["Authorization"] = "Bot " + cfg.BotToken.Trim();
                req.Timeout = 30000;
                req.ReadWriteTimeout = 30000;

                byte[] body;
                if (jsonArray != null)
                {
                    req.ContentType = "application/json";
                    body = new UTF8Encoding(false).GetBytes(Json.Serialize(jsonArray));
                }
                else if (files != null && files.Count > 0)
                {
                    string boundary = "----RdpPanel" + Guid.NewGuid().ToString("N");
                    req.ContentType = "multipart/form-data; boundary=" + boundary;
                    using (var ms = new MemoryStream())
                    {
                        var enc = new UTF8Encoding(false);
                        Action<string> w = t => { var b = enc.GetBytes(t); ms.Write(b, 0, b.Length); };
                        w("--" + boundary + "\r\nContent-Disposition: form-data; name=\"payload_json\"\r\nContent-Type: application/json\r\n\r\n" + Json.Serialize(payload) + "\r\n");
                        for (int i = 0; i < files.Count; i++)
                        {
                            w("--" + boundary + "\r\nContent-Disposition: form-data; name=\"files[" + i + "]\"; filename=\"" + files[i].Key + "\"\r\nContent-Type: image/jpeg\r\n\r\n");
                            ms.Write(files[i].Value, 0, files[i].Value.Length);
                            w("\r\n");
                        }
                        w("--" + boundary + "--\r\n");
                        body = ms.ToArray();
                    }
                }
                else if (payload != null)
                {
                    req.ContentType = "application/json";
                    body = new UTF8Encoding(false).GetBytes(Json.Serialize(payload));
                }
                else
                {
                    body = new byte[0];
                }

                req.ContentLength = body.Length;
                if (body.Length > 0)
                    using (var rs = await req.GetRequestStreamAsync())
                        await rs.WriteAsync(body, 0, body.Length);

                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                    return null;
            }
            catch (WebException ex)
            {
                string text = "";
                try
                {
                    if (ex.Response != null)
                        using (var sr = new StreamReader(ex.Response.GetResponseStream())) text = sr.ReadToEnd();
                }
                catch { }
                return ex.Message + (text.Length > 0 ? " – " + Cut(text, 200) : "");
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ------------------------------------------------------------ helpers

        T Ui<T>(Func<T> f)
        {
            return (T)main.Invoke(f);
        }

        void UiDo(Action a)
        {
            main.Invoke(a);
        }

        static Dictionary<string, object> O(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        static object G(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        static Dictionary<string, object> D(object o)
        {
            return o as Dictionary<string, object>;
        }

        static string S(object o)
        {
            return o as string;
        }

        static int ToInt(object o)
        {
            return o == null ? 0 : Convert.ToInt32(o);
        }

        static IEnumerable<object> Arr(object o)
        {
            var e = o as System.Collections.IEnumerable;
            if (e == null || o is string) yield break;
            foreach (var x in e) yield return x;
        }

        static string Cut(string s, int max)
        {
            s = s ?? "";
            return s.Length > max ? s.Substring(0, max) : s;
        }
    }
}
