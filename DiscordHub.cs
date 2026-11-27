using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RdpPanel
{
    // a message that was sent (or failed), for the list "Last messages"
    class DiscordMessage
    {
        public DateTime Time;
        public string Kind = "";
        public string Session = "";
        public string Destination = "";
        public string Text = "";
        public string Error;   // null = it worked
    }

    static class DiscordMessages
    {
        static readonly List<DiscordMessage> list = new List<DiscordMessage>();

        public static void Add(string kind, string session, string destination, string text, string error)
        {
            lock (list)
            {
                list.Insert(0, new DiscordMessage { Time = DateTime.Now, Kind = kind, Session = session ?? "", Destination = destination ?? "", Text = text ?? "", Error = error });
                while (list.Count > 60) list.RemoveAt(list.Count - 1);
            }
        }

        public static List<DiscordMessage> All()
        {
            lock (list) return list.ToList();
        }
    }

    // decides what goes to Discord, where, and how: kinds, the second webhook, bundling, reminders, own texts
    class DiscordHub
    {
        class Pending
        {
            public SessionTile Tile;
            public string Msg = "";
            public string Kind = "";
            public bool IsDrop;
            public List<KeyValuePair<string, byte[]>> Files;
            public string Age = "";
        }

        class Problem
        {
            public DateTime Since = DateTime.Now;
            public string Msg = "";
            public bool Reminded;
        }

        readonly MainForm main;
        readonly List<Pending> pending = new List<Pending>();
        DateTime bundleDue = DateTime.MaxValue;
        readonly Dictionary<SessionTile, Problem> problems = new Dictionary<SessionTile, Problem>();
        int ticks;

        public DiscordHub(MainForm main) { this.main = main; }

        AppConfig Cfg { get { return main.Cfg; } }

        // ---------- small helpers ----------

        public static string KindOf(string msg)
        {
            if (msg.IndexOf("CPU ", StringComparison.Ordinal) >= 0 && msg.IndexOf("RAM ", StringComparison.Ordinal) >= 0) return "load";
            if (msg.IndexOf("frozen", StringComparison.OrdinalIgnoreCase) >= 0) return "frozen";
            return "problem";
        }

        bool KindOn(string kind)
        {
            if (kind == "load") return Cfg.DcLoad;
            if (kind == "frozen") return Cfg.DcFrozen;
            return Cfg.DcProblem;
        }

        string Hook(SessionConfig sc)
        {
            return Discord.IsValid(sc.Webhook) ? sc.Webhook : Cfg.WebhookUrl;
        }

        string Chan(SessionConfig sc)
        {
            return !string.IsNullOrWhiteSpace(sc.BotChannel) ? sc.BotChannel.Trim() : (Cfg.BotChannelId ?? "").Trim();
        }

        bool BotOk(string chan)
        {
            var b = main.BotRef;
            return b != null && b.Running && !string.IsNullOrWhiteSpace(chan);
        }

        public static string Render(string tpl, Dictionary<string, string> vars)
        {
            string r = tpl ?? "";
            foreach (var kv in vars) r = r.Replace("{" + kv.Key + "}", kv.Value ?? "");
            return r;
        }

        Dictionary<string, string> Vars(string session, string host, string message)
        {
            return new Dictionary<string, string>
            {
                { "session", session }, { "host", host }, { "message", message }, { "reason", message },
                { "time", DateTime.Now.ToString("HH:mm:ss") }, { "date", DateTime.Now.ToString("dd-MM-yyyy") }
            };
        }

        // "role:123", "user:456", "@here", "@everyone" (or just an ID = a user) become a Discord mention
        public static string BuildMention(string raw)
        {
            var sb = new StringBuilder();
            foreach (var p0 in (raw ?? "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = p0.Trim();
                string m;
                if (p.StartsWith("role:", StringComparison.OrdinalIgnoreCase)) m = "<@&" + p.Substring(5) + ">";
                else if (p.StartsWith("user:", StringComparison.OrdinalIgnoreCase)) m = "<@" + p.Substring(5) + ">";
                else if (p == "@everyone" || p == "@here" || p.StartsWith("<")) m = p;
                else if (p.All(char.IsDigit)) m = "<@" + p + ">";
                else continue;
                sb.Append(m).Append(' ');
            }
            return sb.ToString();
        }

        string Mention() { return BuildMention(Cfg.CritMention); }

        static string Short(string url)
        {
            url = (url ?? "").Trim();
            return "webhook …" + (url.Length > 6 ? url.Substring(url.Length - 6) : url);
        }

        // ---------- sending ----------

        public async Task<string> SendAsync(string url, string text, List<KeyValuePair<string, byte[]>> files, string kind, string session)
        {
            string err = await Discord.SendAsync(url, text, files);
            DiscordMessages.Add(kind, session, Short(url), text, err);
            if (err != null) LogService.Write(session, "Discord", "sending failed: " + err, 0);
            return err;
        }

        async void Send(string url, string text, List<KeyValuePair<string, byte[]>> files, string kind, string session)
        {
            try { await SendAsync(url, text, files, kind, session); }
            catch (Exception ex) { ErrorLog.Write("discord send", ex); }
        }

        // ---------- a warning of a session (OnAlert) ----------

        public void Alert(SessionTile t, string msg)
        {
            string kind = KindOf(msg);
            bool critical = kind != "load";
            if (!KindOn(kind)) return;
            if (critical) Track(t, msg);

            var sc = t.Config;
            bool route = BotOk(Chan(sc)) || (Cfg.DiscordAlerts && Discord.IsValid(Hook(sc))) || (critical && Discord.IsValid(Cfg.CritWebhookUrl));
            if (!route) return;
            if (!main.DiscordAllowed(critical)) return;

            if (critical && Cfg.BundleSeconds > 0)
            {
                pending.Add(new Pending { Tile = t, Msg = msg, Kind = kind });
                if (bundleDue == DateTime.MaxValue) bundleDue = DateTime.Now.AddSeconds(Cfg.BundleSeconds);
                return;
            }
            Deliver(t, msg, kind, critical);
        }

        void Deliver(SessionTile t, string msg, string kind, bool critical)
        {
            var sc = t.Config;
            var vars = Vars(sc.Name, sc.Host, msg);
            vars["kind"] = kind;
            string chan = Chan(sc), hook = Hook(sc);
            if (BotOk(chan))
            {
                main.BotRef.PostAlert(sc.Name, msg, chan);
                DiscordMessages.Add(kind, sc.Name, "bot channel", msg, null);
            }
            else if (Cfg.DiscordAlerts && Discord.IsValid(hook))
                Send(hook, Render(Cfg.TplAlert, vars), null, kind, sc.Name);
            if (critical && Discord.IsValid(Cfg.CritWebhookUrl))
                Send(Cfg.CritWebhookUrl, Mention() + Render(Cfg.TplAlert, vars), null, kind + " (critical)", sc.Name);
        }

        // ---------- a session that dropped (OnDropped) ----------

        public void Dropped(SessionTile t, string reason, List<KeyValuePair<string, byte[]>> files, string age)
        {
            if (!Cfg.DiscordOnDrop) return;
            var sc = t.Config;
            bool crit = Discord.IsValid(Cfg.CritWebhookUrl);
            if (!Discord.IsValid(Hook(sc)) && !crit) return;
            Track(t, "dropped: " + reason);
            if (!main.DiscordAllowed(true)) return;
            if (Cfg.BundleSeconds > 0)
            {
                pending.Add(new Pending { Tile = t, Msg = reason, Kind = "drop", IsDrop = true, Files = files, Age = age });
                if (bundleDue == DateTime.MaxValue) bundleDue = DateTime.Now.AddSeconds(Cfg.BundleSeconds);
                return;
            }
            SendDrop(t, reason, files, age);
        }

        void SendDrop(SessionTile t, string reason, List<KeyValuePair<string, byte[]>> files, string age)
        {
            var sc = t.Config;
            string text = Render(Cfg.TplDrop, Vars(sc.Name, sc.Host, reason)) + age;
            string hook = Hook(sc);
            if (Discord.IsValid(hook)) Send(hook, text, files, "drop", sc.Name);
            if (Discord.IsValid(Cfg.CritWebhookUrl)) Send(Cfg.CritWebhookUrl, Mention() + text, files, "drop (critical)", sc.Name);
        }

        // ---------- bundling ----------

        void Flush()
        {
            var items = pending.ToList();
            pending.Clear();
            bundleDue = DateTime.MaxValue;
            if (items.Count == 0) return;
            if (items.Count == 1)
            {
                var p = items[0];
                if (p.IsDrop) SendDrop(p.Tile, p.Msg, p.Files, p.Age);
                else Deliver(p.Tile, p.Msg, p.Kind, true);
                return;
            }
            string list = string.Join("\n", items.Select(p => "• **" + p.Tile.Config.Name + "**: " + p.Msg));
            var vars = Vars("", "", "");
            vars["count"] = items.Count.ToString();
            vars["list"] = list;
            string text = Render(Cfg.TplBundle, vars);
            var files = items.Select(p => p.Files).FirstOrDefault(f => f != null && f.Count > 0);
            if (Discord.IsValid(Cfg.WebhookUrl)) Send(Cfg.WebhookUrl, text, files, "bundle", "");
            else
            {
                var b = main.BotRef;
                string chan = (Cfg.BotChannelId ?? "").Trim();
                if (BotOk(chan))
                {
                    b.PostChannelMessage(text, chan);
                    DiscordMessages.Add("bundle", "", "bot channel", text, null);
                }
            }
            if (Discord.IsValid(Cfg.CritWebhookUrl)) Send(Cfg.CritWebhookUrl, Mention() + text, files, "bundle (critical)", "");
        }

        // ---------- reminders ----------

        void Track(SessionTile t, string msg)
        {
            if (!problems.ContainsKey(t)) problems[t] = new Problem { Msg = msg };
        }

        static bool InTrouble(SessionTile t)
        {
            return t.StatusLabel == "Dropped" || (!t.Online && t.Unreachable) || t.FrozenSeconds > 0;
        }

        // called every second by the panel
        public void Tick()
        {
            ticks++;
            if (pending.Count > 0 && DateTime.Now >= bundleDue) Flush();
            if (ticks % 5 != 0) return;
            foreach (var kv in problems.ToList())
            {
                var t = kv.Key;
                var p = kv.Value;
                if (!main.Tiles.Contains(t) || !InTrouble(t)) { problems.Remove(t); continue; }
                if (Cfg.RepeatMinutes <= 0 || p.Reminded) continue;
                if ((DateTime.Now - p.Since).TotalMinutes < Cfg.RepeatMinutes) continue;
                if (t.IsMuted || DateTime.Now < main.MuteUntil || !main.DiscordAllowed(true)) continue;
                p.Reminded = true;
                Remind(t, p);
            }
        }

        void Remind(SessionTile t, Problem p)
        {
            var sc = t.Config;
            var vars = Vars(sc.Name, sc.Host, p.Msg);
            vars["minutes"] = ((int)(DateTime.Now - p.Since).TotalMinutes).ToString();
            string text = Render(Cfg.TplReminder, vars);
            string hook = Hook(sc), chan = Chan(sc);
            if (Discord.IsValid(hook)) Send(hook, text, null, "reminder", sc.Name);
            else if (BotOk(chan))
            {
                main.BotRef.PostChannelMessage(text, chan);
                DiscordMessages.Add("reminder", sc.Name, "bot channel", text, null);
            }
            if (Discord.IsValid(Cfg.CritWebhookUrl)) Send(Cfg.CritWebhookUrl, Mention() + text, null, "reminder (critical)", sc.Name);
        }

        // ---------- test messages ----------

        // kind: alert, load, frozen, drop, reminder, bundle. critical = to the second webhook (with the mention)
        public async Task<string> SendTest(string kind, bool critical)
        {
            string url = critical ? Cfg.CritWebhookUrl : Cfg.WebhookUrl;
            string chan = (Cfg.BotChannelId ?? "").Trim();
            bool bot = !critical && !Discord.IsValid(url) && BotOk(chan);
            if (!Discord.IsValid(url) && !bot) return critical ? "There is no second (critical) webhook yet." : "There is no webhook (or bot channel) yet.";

            var vars = Vars("Test PC", "192.168.1.10", "The PC does not respond to a ping. (this is a test)");
            string text;
            switch (kind)
            {
                case "load": vars["message"] = "PC of 'Test PC': CPU 96% (limit 90%). (this is a test)"; text = Render(Cfg.TplAlert, vars); break;
                case "frozen": vars["message"] = "Session 'Test PC' looks frozen: the picture has not changed for 5 min. (this is a test)"; text = Render(Cfg.TplAlert, vars); break;
                case "drop": vars["reason"] = "The connection was lost (this is a test)."; text = Render(Cfg.TplDrop, vars); break;
                case "reminder": vars["minutes"] = "15"; text = Render(Cfg.TplReminder, vars); break;
                case "bundle":
                    vars["count"] = "3";
                    vars["list"] = "• **Test PC 1**: no response\n• **Test PC 2**: no response\n• **Test PC 3**: dropped";
                    text = Render(Cfg.TplBundle, vars);
                    break;
                default: text = Render(Cfg.TplAlert, vars); break;
            }
            if (critical) text = Mention() + text;
            if (bot)
            {
                string err = await main.BotRef.PostChannelMessage(text, chan);
                DiscordMessages.Add("test " + kind, "Test PC", "bot channel", text, err);
                return err;
            }
            return await SendAsync(url, text, null, "test " + kind + (critical ? " (critical)" : ""), "Test PC");
        }
    }
}
