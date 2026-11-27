using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
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

    // rounded outline behind input controls (text boxes, number boxes, drop-down lists): the parent draws it
    static class RoundInput
    {
        static readonly HashSet<Control> inputs = new HashSet<Control>();
        static readonly HashSet<Control> hooked = new HashSet<Control>();

        public static void Attach(Control c)
        {
            if (!inputs.Add(c)) return;
            c.Disposed += (s, e) => inputs.Remove(c);
            c.ParentChanged += (s, e) => Hook(c);
            c.LocationChanged += (s, e) => Refresh(c);
            c.SizeChanged += (s, e) => Refresh(c);
            c.VisibleChanged += (s, e) => Refresh(c);
            c.Enter += (s, e) => Refresh(c);
            c.Leave += (s, e) => Refresh(c);
            Hook(c);
        }

        static Size Pad(Control c)
        {
            var tb = c as TextBox;
            if (tb != null && !tb.Multiline) return new Size(6, 5);   // a text box without border is low: give it room
            return new Size(4, 3);
        }

        static Rectangle Outline(Control c)
        {
            var r = c.Bounds;
            var p = Pad(c);
            r.Inflate(p.Width, p.Height);
            return r;
        }

        static void Hook(Control c)
        {
            var p = c.Parent;
            if (p == null) return;
            if (hooked.Add(p))
            {
                p.Paint += OnParentPaint;
                p.Disposed += (s, e) => hooked.Remove(p);
            }
            p.Invalidate(Outline(c));
        }

        static void Refresh(Control c)
        {
            if (c.Parent != null) c.Parent.Invalidate(Rectangle.Inflate(Outline(c), 2, 2));
        }

        static void OnParentPaint(object sender, PaintEventArgs e)
        {
            var p = (Control)sender;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Control c in p.Controls)
            {
                if (!inputs.Contains(c) || !c.Visible) continue;
                var r = Outline(c);
                if (!r.IntersectsWith(e.ClipRectangle)) continue;
                using (var path = Rounded.Path(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), 9))
                {
                    using (var br = new SolidBrush(c.Enabled ? Theme.Raised : Theme.Surface)) g.FillPath(br, path);
                    using (var pen = new Pen(c.ContainsFocus ? Theme.Accent : Theme.Line)) g.DrawPath(pen, path);
                }
            }
        }
    }

    // a drop-down list in the colors of the theme (the standard one stays white)
    class DarkCombo : ComboBox
    {
        const int WM_PAINT = 0xF;

        public DarkCombo()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            ItemHeight = 22;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool inList = (e.State & DrawItemState.ComboBoxEdit) == 0;
            bool sel = inList && (e.State & DrawItemState.Selected) != 0;
            using (var br = new SolidBrush(sel ? Theme.Accent : Theme.Raised)) e.Graphics.FillRectangle(br, e.Bounds);
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(10, e.Bounds.Width - 28), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, r, sel ? Color.White : Theme.Fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT && DropDownStyle == ComboBoxStyle.DropDownList)
            {
                try
                {
                    using (var g = Graphics.FromHwnd(Handle))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var br = new SolidBrush(Theme.Raised)) g.FillRectangle(br, new Rectangle(Width - 24, 0, 24, Height));
                        int cx = Width - 12, cy = Height / 2;
                        using (var pen = new Pen(Theme.FgMuted, 1.8f))
                            g.DrawLines(pen, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
                    }
                }
                catch { }
            }
        }
    }

    // a box for a whole number: no arrows, you type it (the up and down keys change it by 1; with Shift by 100)
    class NumBox : TextBox
    {
        long val;
        bool setting;
        public decimal Minimum = 0;
        public decimal Maximum = long.MaxValue;
        public bool ThousandsSeparator = true;
        public event EventHandler ValueChanged;

        long Clamp(decimal d)
        {
            if (d < Minimum) d = Minimum;
            if (d > Maximum) d = Maximum;
            return (long)d;
        }

        void ShowValue()
        {
            setting = true;
            Text = ThousandsSeparator ? val.ToString("N0") : val.ToString();
            setting = false;
        }

        public decimal Value
        {
            get { return val; }
            set
            {
                long v = Clamp(value);
                bool changed = v != val;
                val = v;
                ShowValue();
                if (changed && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (setting) return;
            string digits = new string(Text.Where(char.IsDigit).ToArray());
            long v;
            if (digits.Length == 0) v = 0;
            else if (!long.TryParse(digits, out v)) v = long.MaxValue;
            v = Clamp(v);
            if (v != val)
            {
                val = v;
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnLeave(EventArgs e)
        {
            ShowValue();
            base.OnLeave(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != ',' && e.KeyChar != ' ') e.Handled = true;
            base.OnKeyPress(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                decimal step = e.Shift ? 100 : 1;
                Value = val + (e.KeyCode == Keys.Up ? step : -step);
                SelectionStart = Text.Length;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter) ShowValue();
            base.OnKeyDown(e);
        }

        // the mouse wheel scrolls the list instead of changing the number
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
            NoWheel.Scroll(this, e.Delta);
        }
    }

    // a thin rounded progress line
    class RoundBar : Control
    {
        public double Ratio;
        public Color FillColor = Theme.Accent;

        public RoundBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            ButtonRenderer.DrawParentBackground(g, ClientRectangle, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int rad = Math.Max(1, Height / 2);
            using (var path = Rounded.Path(new Rectangle(0, 0, Width - 1, Height - 1), rad))
            using (var br = new SolidBrush(Theme.Raised)) g.FillPath(br, path);
            int w = (int)((Width - 1) * Math.Max(0, Math.Min(1, Ratio)));
            if (w > 1)
                using (var path = Rounded.Path(new Rectangle(0, 0, Math.Max(2, w), Height - 1), rad))
                using (var br = new SolidBrush(FillColor)) g.FillPath(br, path);
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
