using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RdpPanel
{
    static class Theme
    {
        public static bool IsLight;
        public static Color Bg = Color.FromArgb(18, 20, 24);
        public static Color Surface = Color.FromArgb(27, 30, 36);
        public static Color Raised = Color.FromArgb(44, 48, 57);
        public static Color RaisedHover = Color.FromArgb(58, 63, 74);
        public static Color Line = Color.FromArgb(52, 57, 67);
        public static Color Well = Color.FromArgb(14, 16, 19);
        public static Color Fg = Color.FromArgb(232, 234, 238);
        public static Color FgMuted = Color.FromArgb(150, 156, 168);
        public static Color Accent = Color.FromArgb(79, 140, 255);
        public static Color AccentHover = Color.FromArgb(108, 162, 255);
        public static Color Good = Color.FromArgb(46, 204, 113);
        public static Color Warn = Color.FromArgb(245, 166, 35);
        public static Color Bad = Color.FromArgb(229, 72, 77);

        public static void Apply(bool light)
        {
            IsLight = light;
            if (light)
            {
                Bg = Color.FromArgb(240, 242, 245);
                Surface = Color.FromArgb(255, 255, 255);
                Raised = Color.FromArgb(225, 229, 235);
                RaisedHover = Color.FromArgb(210, 215, 223);
                Line = Color.FromArgb(205, 210, 218);
                Well = Color.FromArgb(228, 231, 236);
                Fg = Color.FromArgb(28, 32, 38);
                FgMuted = Color.FromArgb(104, 112, 124);
                Good = Color.FromArgb(30, 160, 90);
                Warn = Color.FromArgb(214, 140, 10);
                Bad = Color.FromArgb(205, 55, 60);
            }
            else
            {
                Bg = Color.FromArgb(18, 20, 24);
                Surface = Color.FromArgb(27, 30, 36);
                Raised = Color.FromArgb(44, 48, 57);
                RaisedHover = Color.FromArgb(58, 63, 74);
                Line = Color.FromArgb(52, 57, 67);
                Well = Color.FromArgb(14, 16, 19);
                Fg = Color.FromArgb(232, 234, 238);
                FgMuted = Color.FromArgb(150, 156, 168);
                Good = Color.FromArgb(46, 204, 113);
                Warn = Color.FromArgb(245, 166, 35);
                Bad = Color.FromArgb(229, 72, 77);
            }
        }

        public static Font UI(float size, FontStyle style)
        {
            return new Font("Segoe UI", size, style);
        }

        public static Font UI(float size)
        {
            return UI(size, FontStyle.Regular);
        }

        public static Button MakeButton(string text, bool accent)
        {
            var b = new RoundButton
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.Flat,
                ForeColor = accent ? Color.White : Fg,
                BackColor = accent ? Accent : Raised,
                Font = UI(9f),
                Cursor = Cursors.Hand,
                TabStop = false,
                Padding = new Padding(6, 0, 6, 0),
                Margin = new Padding(0, 0, 6, 0),
                MinimumSize = new Size(30, 28)
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = accent ? AccentHover : RaisedHover;
            b.FlatAppearance.MouseDownBackColor = accent ? Accent : Raised;
            return b;
        }

        public static Button MakeButton(string text)
        {
            return MakeButton(text, false);
        }

        public static ContextMenuStrip MakeMenu()
        {
            return new ContextMenuStrip
            {
                BackColor = Surface,
                ForeColor = Fg,
                ShowImageMargin = false,
                Font = UI(9.5f)
            };
        }

        public static void StyleInput(Control c)
        {
            c.BackColor = Raised;
            c.ForeColor = Fg;
            c.Font = UI(9.5f);
            var tb = c as TextBox;
            if (tb != null) tb.BorderStyle = BorderStyle.FixedSingle;
            var nu = c as NumericUpDown;
            if (nu != null) nu.BorderStyle = BorderStyle.FixedSingle;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // color the title bar with the theme (Windows 10 20H1+ / Windows 11)
        public static void TitleBar(IntPtr handle)
        {
            try
            {
                int v = IsLight ? 0 : 1;
                DwmSetWindowAttribute(handle, 20, ref v, 4);
            }
            catch { }
        }
    }
}
