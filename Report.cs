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
            public bool Hidden;      // not on the screens (hidden by the active profile): reported under its own heading
        }

        class Row
        {
            public string Name = "";
            public bool Hidden;
            public int UpSeconds;
            public int Drops;
            public int Failed;
            public double Cpu = double.NaN, Ram = double.NaN, Fps = double.NaN;
            public double CpuMax = double.NaN, RamMax = double.NaN;
            public int Samples;
            public string FpsNote = "";
            public List<DateTime> DropTimes = new List<DateTime>();
            public int LongestOutage;      // seconds
            public double PrevPct = double.NaN;   // uptime % of the period before (NaN = unknown)
            public double Pct;
            public int Rank;               // 0 = needs most attention
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

        static double Max(IEnumerable<double> v)
        {
            var l = v.Where(x => !double.IsNaN(x)).ToList();
            return l.Count == 0 ? double.NaN : l.Max();
        }

        // seconds of connection a session had inside [from, to]
        static double UpSeconds(string name, List<LogService.Entry> log, Live live, DateTime now, DateTime from, DateTime to)
        {
            double up = 0;
            foreach (var e in log.Where(x => x.Session == name && x.Event == "Disconnected"))
                up += Overlap(e.Time.AddSeconds(-e.Seconds), e.Time, from, to);
            if (live != null && live.Online) up += Overlap(now.AddSeconds(-live.Seconds), now, from, to);   // still connected
            return up;
        }

        static string Pts(double v)
        {
            return Math.Abs(v).ToString("0.0", CultureInfo.InvariantCulture);
        }

        // Builds the message(s) for the window [from, to). Returns null when there is nothing to report.
        // Call this on a background thread: it reads the log files.
        // updateLine: optional last line, e.g. "Update available: v1.1.0"
        public static List<string> Build(DateTime from, DateTime to, string title, string range, List<Live> sessions, string updateLine = null)
        {
            var now = DateTime.Now;
            double window = Math.Max(1, (to - from).TotalSeconds);
            // compare with the period right before it, but only for a full day (a "today so far" test has nothing to compare with)
            bool compare = window >= 23 * 3600;
            var prevFrom = from.AddSeconds(-window);

            // read the files once; a bit wider than the window because a long session started before it
            var log = LogService.ReadAll().Where(e => e.Time >= (compare ? prevFrom : from) && e.Time <= to.AddDays(1)).OrderBy(e => e.Time).ToList();
            var samples = MetricsService.Read(from, to);

            var rows = new List<Row>();
            foreach (var s in sessions)
            {
                var row = new Row { Name = s.Name, Hidden = s.Hidden };

                row.UpSeconds = (int)Math.Min(window, Math.Round(UpSeconds(s.Name, log, s, now, from, to)));
                row.Pct = 100.0 * row.UpSeconds / window;
                if (compare)
                {
                    double prevUp = Math.Min(window, UpSeconds(s.Name, log, s, now, prevFrom, from));
                    if (prevUp > 0) row.PrevPct = 100.0 * prevUp / window;
                }

                var mine = log.Where(e => e.Session == s.Name).ToList();
                var drops = mine.Where(e => e.Time >= from && e.Time < to && IsDrop(e)).ToList();
                row.Drops = drops.Count;
                row.Failed = mine.Count(e => e.Time >= from && e.Time < to && e.Event == "Failed");
                row.DropTimes = drops.Select(e => e.Time).ToList();

                // longest outage: from a drop until the next successful connection (or the end of the window)
                foreach (var d in drops)
                {
                    var next = mine.FirstOrDefault(e => e.Time > d.Time && e.Event == "Connected");
                    DateTime end = next != null ? next.Time : (s.Online ? now : to);
                    if (end > to) end = to;
                    row.LongestOutage = Math.Max(row.LongestOutage, (int)Math.Max(0, (end - d.Time).TotalSeconds));
                }

                var sm = samples.Where(x => x.Session == s.Name).ToList();
                row.Samples = sm.Count;
                row.Cpu = Avg(sm.Select(x => x.Cpu));
                row.Ram = Avg(sm.Select(x => x.Ram));
                row.Fps = Avg(sm.Select(x => x.Fps));
                row.CpuMax = Max(sm.Select(x => x.Cpu));
                row.RamMax = Max(sm.Select(x => x.Ram));
                if (double.IsNaN(row.Fps) && s.Online && !string.IsNullOrEmpty(s.FpsReason)) row.FpsNote = s.FpsReason;

                // 0 = red, 1 = yellow, 2 = green, 3 = no data
                row.Rank = !row.HasData ? 3 : row.Pct >= 99 ? 2 : row.Pct >= 90 ? 1 : 0;
                rows.Add(row);
            }

            if (!rows.Any(r => r.HasData)) return null;

            // worst first: the sessions that need attention are at the top
            rows = rows.OrderBy(r => r.Rank).ThenBy(r => r.Pct).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            // hidden sessions are still reported, but under their own heading and not in the headline numbers
            var hiddenRows = rows.Where(r => r.Hidden).ToList();
            rows = rows.Where(r => !r.Hidden).ToList();
            var withData = rows.Where(r => r.HasData).ToList();
            int attention = withData.Count(r => r.Rank < 2);
            double avgUp = withData.Count > 0 ? withData.Average(r => r.Pct) : 0;
            int totalDrops = withData.Sum(r => r.Drops);
            var prevs = withData.Where(r => !double.IsNaN(r.PrevPct)).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("📋 **" + title + "**");
            sb.AppendLine("RDP Panel v" + AppInfo.Version + " · " + range);
            sb.AppendLine();

            // the one-line answer first
            var head = new StringBuilder();
            if (withData.Count == 0)
                head.Append("ℹ️ **No data for the sessions on the screens**");
            else if (attention == 0)
                head.Append("✅ **All " + withData.Count + " session" + (withData.Count == 1 ? " was" : "s were") + " stable**");
            else
                head.Append("⚠️ **" + attention + " of " + withData.Count + " session" + (withData.Count == 1 ? "" : "s") + " need" + (attention == 1 ? "s" : "") + " attention**");
            head.Append(" · average uptime " + avgUp.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (prevs.Count > 0)
            {
                double d = avgUp - prevs.Average(r => r.PrevPct);
                if (Math.Abs(d) >= 0.1) head.Append(" (" + (d > 0 ? "▲" : "▼") + Pts(d) + " pts vs the day before)");
            }
            head.Append(" · " + totalDrops + " drop" + (totalDrops == 1 ? "" : "s"));
            sb.AppendLine(head.ToString());
            sb.AppendLine();

            Action<Row> addRow = r =>
            {
                if (!r.HasData) { sb.AppendLine("⚪ **" + r.Name + "** · no data"); return; }
                string dot = r.Rank == 2 ? "🟢" : r.Rank == 1 ? "🟡" : "🔴";
                var line = new StringBuilder();
                line.Append(dot + " **" + r.Name + "** · uptime " + UI.Dur(r.UpSeconds) + " (" + r.Pct.ToString("0.0", CultureInfo.InvariantCulture) + "%");
                if (!double.IsNaN(r.PrevPct) && Math.Abs(r.Pct - r.PrevPct) >= 0.5)
                    line.Append(", " + (r.Pct > r.PrevPct ? "▲" : "▼") + Pts(r.Pct - r.PrevPct));
                line.Append(")");

                line.Append(" · drops " + r.Drops);
                if (r.Drops > 0)
                {
                    var times = r.DropTimes.Take(3).Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)).ToList();
                    string more = r.Drops > 3 ? ", +" + (r.Drops - 3) + " more" : "";
                    line.Append(" (" + string.Join(", ", times) + more + ")");
                    if (r.LongestOutage >= 60) line.Append(" · longest outage " + Span(r.LongestOutage));
                }
                if (r.Failed > 0) line.Append(" (+" + r.Failed + " failed tries)");

                if (!double.IsNaN(r.Cpu)) line.Append(" · CPU " + r.Cpu.ToString("0") + "%" + Peak(r.CpuMax, r.Cpu));
                if (!double.IsNaN(r.Ram)) line.Append(" · RAM " + r.Ram.ToString("0") + "%" + Peak(r.RamMax, r.Ram));
                if (!double.IsNaN(r.Fps)) line.Append(" · " + r.Fps.ToString("0") + " FPS");
                else if (r.FpsNote.Length > 0) line.Append(" · FPS: no value (" + r.FpsNote + ")");
                sb.AppendLine(line.ToString());
            };
            foreach (var r in rows) addRow(r);

            if (hiddenRows.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("🙈 **Hidden by profile** (not on the screens, still monitored)");
                foreach (var r in hiddenRows) addRow(r);
            }

            if (!string.IsNullOrEmpty(updateLine))
            {
                sb.AppendLine();
                sb.AppendLine("⬆️ " + updateLine);
            }

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

        // " (max 98%)" only when the peak says something the average hides
        static string Peak(double max, double avg)
        {
            if (double.IsNaN(max) || max < avg + 10) return "";
            return " (max " + max.ToString("0") + "%)";
        }

        static string Span(int secs)
        {
            var t = TimeSpan.FromSeconds(secs);
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            return Math.Max(1, (int)Math.Round(t.TotalMinutes)) + "m";
        }
    }
}
