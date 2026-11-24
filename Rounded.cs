using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RdpPanel
{
    static class Rounded
    {
        public static GraphicsPath Path(Rectangle r, int radius)
        {
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // a button with rounded corners (drawn smooth)
    class RoundButton : Button
    {
        bool hover, down;
        public int Radius = 8;

        public RoundButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            ButtonRenderer.DrawParentBackground(g, ClientRectangle, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg = BackColor;
            if (Enabled && down && FlatAppearance.MouseDownBackColor != Color.Empty) bg = FlatAppearance.MouseDownBackColor;
            else if (Enabled && hover && FlatAppearance.MouseOverBackColor != Color.Empty) bg = FlatAppearance.MouseOverBackColor;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded.Path(rect, Radius))
            {
                using (var br = new SolidBrush(bg)) g.FillPath(br, path);
                if (FlatAppearance.BorderSize > 0) using (var pen = new Pen(FlatAppearance.BorderColor)) g.DrawPath(pen, path);
            }
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.FgMuted, flags);
        }
    }

    // a panel with a rounded outline; the controls on it keep their own place
    class RoundedPanel : Panel
    {
        public int Radius = 14;

        public RoundedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var p = Rounded.Path(r, Radius))
            {
                using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, p);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, p);
            }
        }
    }
}
