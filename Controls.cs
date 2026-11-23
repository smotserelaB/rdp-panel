using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RdpPanel
{
    static class WinUtil
    {
        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(Point p);

        [DllImport("user32.dll")]
        static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);

        // true if another window (or something else) covers part of the area;
        // this way windows of other programs never end up in a screenshot
        public static bool IsCovered(IntPtr rootHandle, Rectangle r)
        {
            for (int ix = 0; ix < 5; ix++)
                for (int iy = 0; iy < 5; iy++)
                {
                    var pt = new Point(r.Left + 6 + Math.Max(0, r.Width - 12) * ix / 4,
                                       r.Top + 6 + Math.Max(0, r.Height - 12) * iy / 4);
                    IntPtr h = WindowFromPoint(pt);
                    if (h == IntPtr.Zero || GetAncestor(h, 2) != rootHandle) return true;   // 2 = GA_ROOT
                }
            return false;
        }

        // the visible window without the invisible borders of Windows 10/11
        public static Rectangle VisibleBounds(Form f)
        {
            try
            {
                RECT r;
                if (DwmGetWindowAttribute(f.Handle, 9, out r, Marshal.SizeOf(typeof(RECT))) == 0)
                    return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }
            catch { }
            return f.Bounds;
        }

        public static string AgeText(DateTime taken)
        {
            var age = DateTime.Now - taken;
            if (age.TotalSeconds < 45) return "";
            return "\n(image from " + (age.TotalMinutes < 60 ? (int)Math.Max(1, age.TotalMinutes) + " min" : (int)age.TotalHours + " h") + " ago: no newer picture could be taken)";
        }
    }

    static class Gfx
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // blends a (t) with b (1 - t)
        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R * t + b.R * (1 - t)),
                (int)(a.G * t + b.G * (1 - t)),
                (int)(a.B * t + b.B * (1 - t)));
        }

        public static bool TryColor(string hex, out Color c)
        {
            c = Color.Empty;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            try { c = ColorTranslator.FromHtml(hex.Trim()); return true; }
            catch { return false; }
        }
    }

    // line chart with optional fill and grid
    class Spark : Control
    {
        public class Series
        {
            public List<float> V = new List<float>();
            public Color C = Color.White;
        }

        public readonly List<Series> Lines = new List<Series>();
        public bool Fill = true;
        public bool Grid;
        public int MaxPoints = 60;

        public Spark()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 3, Math.Max(2, Width - 2), Math.Max(2, Height - 6));

            if (Grid)
            {
                using (var p = new Pen(Theme.Line))
                    for (int i = 0; i <= 4; i++)
                    {
                        float y = r.Top + r.Height * i / 4f;
                        g.DrawLine(p, r.Left, y, r.Right, y);
                    }
            }

            foreach (var s in Lines)
            {
                var v = s.V;
                if (v.Count < 2) continue;
                int n = Math.Min(v.Count, MaxPoints);
                var pts = new PointF[n];
                for (int i = 0; i < n; i++)
                {
                    float val = Math.Max(0f, Math.Min(100f, v[v.Count - n + i]));
                    pts[i] = new PointF(r.Left + i * r.Width / (n - 1), r.Bottom - val / 100f * r.Height);
                }
                if (Fill)
                {
                    var poly = new List<PointF>(pts);
                    poly.Add(new PointF(r.Right, r.Bottom));
                    poly.Add(new PointF(r.Left, r.Bottom));
                    using (var b = new SolidBrush(Color.FromArgb(50, s.C))) g.FillPolygon(b, poly.ToArray());
                }
                using (var p = new Pen(s.C, 1.6f)) g.DrawLines(p, pts);
            }
        }
    }

    // rounded "pill" with text
    class Chip : Control
    {
        public Color FillColor = Theme.Raised;

        public Chip()
        {
            DoubleBuffered = true;
            Height = 24;
            Font = Theme.UI(9f, FontStyle.Bold);
            ForeColor = Theme.Fg;
        }

        public void FitWidth()
        {
            Width = TextRenderer.MeasureText(Text, Font).Width + 22;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Gfx.Rounded(r, Height / 2))
            using (var b = new SolidBrush(FillColor))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, Text, Font, r, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    class ToggleSwitch : Control
    {
        bool on;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public ToggleSwitch()
        {
            Size = new Size(44, 24);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !on;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 2, Width - 1, Height - 5);
            using (var path = Gfx.Rounded(r, r.Height / 2))
            using (var b = new SolidBrush(on ? Theme.Accent : Theme.Raised))
                g.FillPath(b, path);
            int d = r.Height - 6;
            int x = on ? r.Right - d - 3 : r.Left + 3;
            g.FillEllipse(Brushes.White, x, r.Top + 3, d, d);
        }
    }

    // card with border and title
    class Card : Panel
    {
        public readonly Label Title = new Label();
        public readonly Panel Body = new Panel();

        public Card(string title)
        {
            BackColor = Theme.Line;
            Padding = new Padding(1);

            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            Title.Dock = DockStyle.Top;
            Title.Height = 32;
            Title.Text = title;
            Title.Padding = new Padding(12, 0, 0, 0);
            Title.TextAlign = ContentAlignment.MiddleLeft;
            Title.Font = Theme.UI(10f, FontStyle.Bold);
            Title.ForeColor = Theme.Fg;

            Body.Dock = DockStyle.Fill;
            Body.BackColor = Theme.Surface;
            Body.Padding = new Padding(10, 0, 10, 8);

            inner.Controls.Add(Body);
            inner.Controls.Add(Title);
            Controls.Add(inner);
        }
    }
}
