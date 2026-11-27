using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace RdpPanel
{
    // a chart of 14 days: bars (hours, drops) or a line (daily averages)
    class DayChart : Control
    {
        public bool Bars;
        public string Unit = "";
        public double? FixedMax;
        public readonly List<DateTime> Days = new List<DateTime>();
        public readonly List<double> Vals = new List<double>();   // NaN = no data that day

        public DayChart()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Surface;
        }

        static string Format(double v)
        {
            return v >= 100 || Math.Abs(v - Math.Round(v)) < 0.05 ? v.ToString("0", CultureInfo.CurrentCulture) : v.ToString("0.#", CultureInfo.CurrentCulture);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var f = Theme.UI(8.5f))
            using (var muted = new SolidBrush(Theme.FgMuted))
            {
                var valid = Vals.Where(v => !double.IsNaN(v)).ToList();
                if (valid.Count == 0)
                {
                    g.DrawString("No data for these days yet.", f, muted, 16, 16);
                    return;
                }
                var r = new Rectangle(60, 22, Math.Max(40, Width - 84), Math.Max(40, Height - 62));
                double max = FixedMax ?? Math.Max(1.0, valid.Max() * 1.15);
                using (var gridPen = new Pen(Theme.Line))
                    for (int i = 0; i <= 4; i++)
                    {
                        float y = r.Bottom - i * r.Height / 4f;
                        g.DrawLine(gridPen, r.Left, y, r.Right, y);
                        g.DrawString(Format(max * i / 4) + Unit, f, muted, 4, y - 8);
                    }
                int n = Math.Max(1, Days.Count);
                float slot = r.Width / (float)n;
                var pts = new List<PointF?>();
                for (int i = 0; i < Days.Count; i++)
                {
                    double v = Vals[i];
                    float cx = r.Left + slot * (i + 0.5f);
                    if (double.IsNaN(v)) { pts.Add(null); }
                    else
                    {
                        float h = (float)(v / max * r.Height);
                        float top = r.Bottom - h;
                        if (Bars)
                        {
                            float bw = Math.Max(6, slot * 0.55f);
                            var rect = new Rectangle((int)(cx - bw / 2), (int)top, (int)bw, Math.Max(1, (int)h));
                            using (var path = Rounded.Path(rect, Math.Min(5, Math.Max(1, rect.Height / 2))))
                            using (var br = new SolidBrush(Theme.Accent)) g.FillPath(br, path);
                            string label = Format(v);
                            var sz = g.MeasureString(label, f);
                            g.DrawString(label, f, muted, cx - sz.Width / 2, top - 16);
                        }
                        pts.Add(new PointF(cx, top));
                    }
                }
                if (!Bars)
                {
                    using (var pen = new Pen(Theme.Accent, 2.5f))
                    using (var dot = new SolidBrush(Theme.Accent))
                    {
                        PointF? prev = null;
                        foreach (var p in pts)
                        {
                            if (p.HasValue)
                            {
                                if (prev.HasValue) g.DrawLine(pen, prev.Value, p.Value);
                                g.FillEllipse(dot, p.Value.X - 3.5f, p.Value.Y - 3.5f, 7, 7);
                            }
                            prev = p;
                        }
                    }
                }
                for (int i = 0; i < Days.Count; i++)
                {
                    string d = Days[i].ToString("dd-MM");
                    var sz = g.MeasureString(d, f);
                    g.DrawString(d, f, muted, r.Left + slot * (i + 0.5f) - sz.Width / 2, r.Bottom + 8);
                }
            }
        }
    }

    // the last 14 days of one session: connected time, drops, CPU, RAM and FPS
    class SessionHistoryForm : Form
    {
        readonly MainForm main;
        readonly string session;
        readonly ComboBox cmbWhat = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly DayChart chart = new DayChart();
        readonly Label lblSum = new Label();
        readonly List<DateTime> days;
        readonly List<LogService.Entry> events;
        readonly List<MetricsService.Sample> metrics;

        public SessionHistoryForm(MainForm main, string session)
        {
            this.main = main;
            this.session = session;
            UI.Init(this, "History of " + session + " (14 days)", 860, 540);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            ShowInTaskbar = true;

            days = Enumerable.Range(0, 14).Select(i => DateTime.Today.AddDays(-13 + i)).ToList();
            events = LogService.ReadAll().Where(e => e.Session == session && e.Time >= days[0]).ToList();
            metrics = MetricsService.Read(days[0], DateTime.Today.AddDays(1)).Where(s => s.Session == session).ToList();

            UI.Lbl(this, "Show", 16, 18, 50, true);
            foreach (var t in new[] { "Connected time per day (hours)", "Drops per day", "CPU (daily average)", "RAM (daily average)", "FPS (daily average)" }) cmbWhat.Items.Add(t);
            UI.Put(this, cmbWhat, 66, 14, 290);
            cmbWhat.SelectedIndex = 0;
            cmbWhat.SelectedIndexChanged += (s, e) => Fill();

            lblSum.SetBounds(376, 16, 470, 26);
            lblSum.ForeColor = Theme.Fg;
            lblSum.BackColor = Color.Transparent;
            Controls.Add(lblSum);

            chart.SetBounds(16, 56, 820, 440);
            chart.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(chart);
            Fill();
        }

        static double? Avg(IEnumerable<double> v)
        {
            var l = v.Where(x => !double.IsNaN(x)).ToList();
            return l.Count == 0 ? (double?)null : l.Average();
        }

        void Fill()
        {
            int mode = Math.Max(0, cmbWhat.SelectedIndex);
            var running = main.RunningSeconds();
            var vals = new List<double>();
            foreach (var d in days)
            {
                var ev = events.Where(e => e.Time.Date == d).ToList();
                var ms = metrics.Where(m => m.Time.Date == d).ToList();
                double v;
                if (mode == 0)
                {
                    int secs = ev.Where(e => e.Event == "Disconnected").Sum(e => e.Seconds);
                    if (d == DateTime.Today && running.ContainsKey(session)) secs += running[session];
                    v = secs / 3600.0;
                }
                else if (mode == 1)
                    v = ev.Count(e => (e.Event == "Disconnected" && e.Detail != DiscReasons.ByUser && e.Detail != "Reconnect") || e.Event == "Failed");
                else
                {
                    var a = Avg(ms.Select(m => mode == 2 ? m.Cpu : mode == 3 ? m.Ram : m.Fps));
                    v = a.HasValue ? a.Value : double.NaN;
                }
                vals.Add(v);
            }
            chart.Days.Clear();
            chart.Days.AddRange(days);
            chart.Vals.Clear();
            chart.Vals.AddRange(vals);
            chart.Bars = mode <= 1;
            chart.Unit = mode == 2 || mode == 3 ? "%" : "";
            chart.FixedMax = mode == 2 || mode == 3 ? 100.0 : (double?)null;

            var good = vals.Where(x => !double.IsNaN(x)).ToList();
            if (good.Count == 0) lblSum.Text = "";
            else if (mode == 0) lblSum.Text = "Total " + UI.Dur((int)(good.Sum() * 3600)) + "   ·   average " + UI.Dur((int)(good.Average() * 3600)) + " per day";
            else if (mode == 1) lblSum.Text = "Total " + (int)good.Sum() + " drops in 14 days";
            else lblSum.Text = "Average " + good.Average().ToString("0.#", CultureInfo.CurrentCulture) + (mode == 4 ? " FPS" : "%");
            chart.Invalidate();
        }
    }
}
