using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RdpPanel
{
    // ---- the daily report: uptime, drops and average CPU/RAM/FPS per session ----
    static class DailyReport
    {
        public const string NoData = "no data";

        // what the panel knows about a session right now (collected on the UI thread)
        public class Live
        {
            public string Name = "";
            public bool Online;
            public int Seconds;      // connected for ... seconds right now
            public string FpsReason = "";   // why there is no FPS number right now (empty = there is one)
        }

        class Row
        {
            public string Name = "";
            public int UpSeconds;
            public int Drops;
            public int Failed;
            public double Cpu = double.NaN, Ram = double.NaN, Fps = double.NaN;
            public int Samples;
            public string FpsNote = "";
            public bool HasData { get { return UpSeconds > 0 || Drops > 0 || Failed > 0 || Samples > 0; } }
        }

        // seconds that the interval [a, b] overlaps with the window [from, to]
        static double Overlap(DateTime a, DateTime b, DateTime from, DateTime to)
        {
            var s = a > from ? a : from;
            var e = b < to ? b : to;
            return e > s ? (e - s).TotalSeconds : 0;
        }

        static double Avg(IEnumerable<double> v)
        {
            var l = v.Where(x => !double.IsNaN(x)).ToList();
            return l.Count == 0 ? double.NaN : l.Average();
        }

        static bool IsDrop(LogService.Entry e)
        {
            return e.Event == "Disconnected" && e.Detail != DiscReasons.ByUser && e.Detail != "Reconnect";
        }

        // Builds the message(s) for the window [from, to). Returns null when there is nothing to report.
        // Call this on a background thread: it reads the log files.
        public static List<string> Build(DateTime from, DateTime to, string title, string range, List<Live> sessions)
        {
            var now = DateTime.Now;
            double window = Math.Max(1, (to - from).TotalSeconds);

            // read the files once; a bit wider than the window because a long session started before it
            var log = LogService.ReadAll().Where(e => e.Time >= from && e.Time <= to.AddDays(1)).ToList();
            var samples = MetricsService.Read(from, to);

            var rows = new List<Row>();
            foreach (var s in sessions)
            {
                var row = new Row { Name = s.Name };

                double up = 0;
                foreach (var e in log.Where(x => x.Session == s.Name && x.Event == "Disconnected"))
                    up += Overlap(e.Time.AddSeconds(-e.Seconds), e.Time, from, to);
                if (s.Online) up += Overlap(now.AddSeconds(-s.Seconds), now, from, to);   // still connected
                row.UpSeconds = (int)Math.Min(window, Math.Round(up));

                row.Drops = log.Count(e => e.Session == s.Name && e.Time >= from && e.Time < to && IsDrop(e));
                row.Failed = log.Count(e => e.Session == s.Name && e.Time >= from && e.Time < to && e.Event == "Failed");

                var mine = samples.Where(x => x.Session == s.Name).ToList();
                row.Samples = mine.Count;
                row.Cpu = Avg(mine.Select(x => x.Cpu));
                row.Ram = Avg(mine.Select(x => x.Ram));
                row.Fps = Avg(mine.Select(x => x.Fps));
                if (double.IsNaN(row.Fps) && s.Online && !string.IsNullOrEmpty(s.FpsReason)) row.FpsNote = s.FpsReason;
                rows.Add(row);
            }

            if (!rows.Any(r => r.HasData)) return null;

            var sb = new StringBuilder();
            sb.AppendLine("📋 **" + title + "**");
            sb.AppendLine("RDP Panel v" + AppInfo.Version + " · " + range);
            sb.AppendLine();

            foreach (var r in rows)
            {
                if (!r.HasData) { sb.AppendLine("⚪ **" + r.Name + "** · no data"); continue; }
                double pct = 100.0 * r.UpSeconds / window;
                string dot = pct >= 99 ? "🟢" : pct >= 90 ? "🟡" : "🔴";
                var line = new StringBuilder();
                line.Append(dot + " **" + r.Name + "** · uptime " + UI.Dur(r.UpSeconds) + " (" + pct.ToString("0.0", CultureInfo.InvariantCulture) + "%)");
                line.Append(" · drops " + r.Drops);
                if (r.Failed > 0) line.Append(" (+" + r.Failed + " failed tries)");
                if (!double.IsNaN(r.Cpu)) line.Append(" · CPU " + r.Cpu.ToString("0") + "%");
                if (!double.IsNaN(r.Ram)) line.Append(" · RAM " + r.Ram.ToString("0") + "%");
                if (!double.IsNaN(r.Fps)) line.Append(" · " + r.Fps.ToString("0") + " FPS");
                else if (r.FpsNote.Length > 0) line.Append(" · FPS: no value (" + r.FpsNote + ")");
                sb.AppendLine(line.ToString());
            }

            var withData = rows.Where(r => r.HasData).ToList();
            double avgUp = withData.Average(r => 100.0 * r.UpSeconds / window);
            sb.AppendLine();
            sb.AppendLine("**Total** · " + withData.Count + " session" + (withData.Count == 1 ? "" : "s")
                + " · average uptime " + avgUp.ToString("0.0", CultureInfo.InvariantCulture) + "%"
                + " · " + withData.Sum(r => r.Drops) + " drop" + (withData.Sum(r => r.Drops) == 1 ? "" : "s"));

            // Discord allows 2000 characters per message: split on line breaks
            var parts = new List<string>();
            var cur = new StringBuilder();
            foreach (var l in sb.ToString().Replace("\r\n", "\n").Split('\n'))
            {
                if (cur.Length + l.Length + 1 > 1800 && cur.Length > 0) { parts.Add(cur.ToString().TrimEnd()); cur.Clear(); }
                cur.Append(l).Append('\n');
            }
            if (cur.ToString().Trim().Length > 0) parts.Add(cur.ToString().TrimEnd());
            return parts;
        }
    }
}
