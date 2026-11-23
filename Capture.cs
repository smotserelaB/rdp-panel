using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RdpPanel
{
    // ---- grabbing the picture of a window itself (works even when another window is on top of it) ----
    static class WinCapture
    {
        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hwnd);

        const uint PW_CLIENTONLY = 1;
        const uint PW_RENDERFULLCONTENT = 2;   // Windows 8.1 and later: also windows drawn by DWM / GPU

        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

        const uint GW_CHILD = 5;

        // asks Windows to draw the window into a bitmap (reusing bmp when it has the right size).
        // The RDP control hosts its real picture window as a child; if the outer window gives nothing, that child is tried.
        public static bool Print(IntPtr hwnd, ref Bitmap bmp, int w, int h, bool clientOnly)
        {
            try
            {
                if (hwnd == IntPtr.Zero || w < 10 || h < 10 || !IsWindow(hwnd)) return false;
                if (bmp == null || bmp.Width != w || bmp.Height != h)
                {
                    if (bmp != null) bmp.Dispose();
                    bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
                }
                bool ok = PrintOnce(hwnd, bmp, clientOnly);
                if (clientOnly && (!ok || IsBlank(bmp)))
                {
                    IntPtr child = GetWindow(hwnd, GW_CHILD);
                    if (child != IntPtr.Zero && PrintOnce(child, bmp, clientOnly)) ok = true;
                }
                return ok;
            }
            catch { return false; }
        }

        static bool PrintOnce(IntPtr hwnd, Bitmap bmp, bool clientOnly)
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                IntPtr dc = g.GetHdc();
                try { return PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT | (clientOnly ? PW_CLIENTONLY : 0)); }
                finally { g.ReleaseHdc(dc); }
            }
        }

        // a copy of the window as a new bitmap, or null when Windows gave nothing usable (empty or all black)
        public static Bitmap PrintCopy(IntPtr hwnd, int w, int h, bool clientOnly)
        {
            Bitmap b = null;
            if (!Print(hwnd, ref b, w, h, clientOnly) || IsBlank(b))
            {
                if (b != null) b.Dispose();
                return null;
            }
            return b;
        }

        public static Rectangle WindowRectangle(IntPtr hwnd)
        {
            RECT r;
            if (!GetWindowRect(hwnd, out r)) return Rectangle.Empty;
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        // true when (nearly) everything is black: PrintWindow often returns that when it does not work for a window
        public static bool IsBlank(Bitmap b)
        {
            if (b == null) return true;
            try
            {
                var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    var px = new byte[4];
                    int nonBlack = 0, total = 0;
                    for (int iy = 0; iy < 12; iy++)
                        for (int ix = 0; ix < 16; ix++)
                        {
                            int x = 1 + (b.Width - 3) * ix / 15;
                            int y = 1 + (b.Height - 3) * iy / 11;
                            Marshal.Copy(data.Scan0 + y * data.Stride + x * 4, px, 0, 4);
                            total++;
                            if (px[0] != 0 || px[1] != 0 || px[2] != 0) nonBlack++;
                        }
                    return nonBlack == 0 && total > 0;
                }
                finally { b.UnlockBits(data); }
            }
            catch { return true; }
        }

        // a fingerprint of a few thin horizontal bands of the bitmap; if it changes, the picture changed
        public static ulong BandHash(Bitmap b, int bands)
        {
            ulong h = 1469598103934665603UL;
            try
            {
                var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    var row = new byte[b.Width * 4];
                    for (int k = 0; k < bands; k++)
                    {
                        int y0 = (int)((k + 0.5) * b.Height / bands);
                        for (int dy = 0; dy < 2; dy++)
                        {
                            int y = Math.Min(b.Height - 1, y0 + dy);
                            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                            for (int i = 0; i + 3 < row.Length; i += 4)
                            {
                                h ^= (ulong)(row[i] | (row[i + 1] << 8) | (row[i + 2] << 16));
                                h *= 1099511628211UL;
                            }
                        }
                    }
                }
                finally { b.UnlockBits(data); }
            }
            catch { }
            return h;
        }
    }
}
