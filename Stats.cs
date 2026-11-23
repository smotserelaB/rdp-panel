using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RdpPanel
{
    // ---- FPS: how many times per second the picture of a session changes ----
    // The RDP component does not report a frame rate, so we measure it: a background thread takes a few thin
    // bands of the picture about 10 times per second and counts how often they differ from the previous ones.
    // Idle desktop = 0, a busy game = up to 10. When the tile is covered by another window the picture is taken
    // from the window itself (PrintWindow), which is slower, so then it is measured at most 4 times per second.
    // If there is no number, Reason says why (shown in the tooltip, the report and the diagnostic file).
    static class FpsSampler
    {
        public class Target
        {
            internal readonly object Gate = new object();
            Rectangle rect = Rectangle.Empty;
            IntPtr hwnd = IntPtr.Zero;
            bool active, screenOk;
            double fps = double.NaN;
            string reason = "not measured yet";
            string method = "";

            // thread of the sampler only
            internal ulong PrevHash;
            internal bool HasPrev;
            internal string PrevMethod = "";
            internal long LastPrint;
            internal Bitmap PrintBmp;
            internal Bitmap StripBmp;
            internal readonly Queue<long> Changes = new Queue<long>();
            internal long Since;

            // on: the session is connected and has a picture; screenOk: the tile can be copied from the screen
            // (fully visible); hwnd: the window of the RDP control (for PrintWindow when covered)
            public void Set(bool on, Rectangle r, bool screenVisible, IntPtr window, string why)
            {
                lock (Gate)
                {
                    active = on; rect = r; screenOk = screenVisible; hwnd = window;
                    if (!on) { fps = double.NaN; reason = why ?? "not measured"; method = ""; }
                }
            }

            internal void Read(out bool on, out Rectangle r, out bool screen, out IntPtr window)
            {
                lock (Gate) { on = active; r = rect; screen = screenOk; window = hwnd; }
            }

            internal void SetFps(double v, string how)
            {
                lock (Gate) { fps = v; reason = ""; method = how; }
            }

            internal void SetNoValue(string why)
            {
                lock (Gate) { fps = double.NaN; reason = why; }
            }

            internal void Reset()
            {
                HasPrev = false; PrevMethod = ""; Changes.Clear();
                if (PrintBmp != null) { PrintBmp.Dispose(); PrintBmp = null; }
                if (StripBmp != null) { StripBmp.Dispose(); StripBmp = null; }
            }

            public double Fps
            {
                get { lock (Gate) return fps; }
            }

            // empty when there is a number; otherwise the reason there is none
            public string Reason
            {
                get { lock (Gate) return reason; }
            }

            // "screen" (strips of the screen) or "window" (PrintWindow); empty when nothing is measured
            public string Method
            {
                get { lock (Gate) return method; }
            }
        }

        const int Bands = 6;
        const int MinIntervalMs = 100;     // about 10 measurements per second
        const int MaxIntervalMs = 500;     // when the PC is struggling the sampler slows itself down
        const int PrintIntervalMs = 250;   // PrintWindow is heavier: at most 4 per second per session
        const int WindowMs = 2000;         // the FPS is the average over the last 2 seconds

        static readonly object Gate = new object();
        static readonly List<Target> Targets = new List<Target>();
        static Thread thread;

        public static void Add(Target t)
        {
            lock (Gate)
            {
                if (!Targets.Contains(t)) Targets.Add(t);
                if (thread == null)
                {
                    thread = new Thread(Loop) { IsBackground = true, Name = "FpsSampler", Priority = ThreadPriority.BelowNormal };
                    thread.Start();
                }
            }
        }

        public static void Remove(Target t)
        {
            lock (Gate) Targets.Remove(t);
        }

        static void Loop()
        {
            var sw = Stopwatch.StartNew();
            double cost = 0;       // average time one round takes (ms)
            int interval = MinIntervalMs;

            while (true)
            {
                long t0 = sw.ElapsedMilliseconds;
                Target[] list;
                lock (Gate) list = Targets.ToArray();

                foreach (var t in list)
                {
                    try
                    {
                        bool on, screen; Rectangle r; IntPtr hw;
                        t.Read(out on, out r, out screen, out hw);
                        if (!on || r.Width < 50 || r.Height < 50) { t.Reset(); continue; }
                        if (screen) SampleScreen(t, r, t0);
                        else if (hw != IntPtr.Zero) SampleWindow(t, hw, r.Size, t0);
                        else { t.Reset(); t.SetNoValue("no picture available"); }
                    }
                    catch (Exception ex)
                    {
                        t.Reset();
                        t.SetNoValue("measurement error: " + ex.Message);
                    }
                }

                long spent = sw.ElapsedMilliseconds - t0;
                cost = cost * 0.9 + spent * 0.1;
                // never use more than about 1/6 of the time
                interval = (int)Math.Max(MinIntervalMs, Math.Min(MaxIntervalMs, cost * 6));
                Thread.Sleep((int)Math.Max(1, interval - spent));
            }
        }

        // thin bands copied from the screen (the tile is fully visible)
        static void SampleScreen(Target t, Rectangle r, long now)
        {
            int w = Math.Min(r.Width, 1000);
            if (t.StripBmp == null || t.StripBmp.Width != w)
            {
                if (t.StripBmp != null) t.StripBmp.Dispose();
                t.StripBmp = new Bitmap(w, Bands * 2, PixelFormat.Format32bppRgb);
            }
            using (var g = Graphics.FromImage(t.StripBmp))
            {
                for (int k = 0; k < Bands; k++)
                {
                    int y = r.Top + (int)((k + 0.5) * r.Height / Bands);
                    g.CopyFromScreen(r.Left + (r.Width - w) / 2, y, 0, k * 2, new Size(w, 2));
                }
            }
            Count(t, WinCapture.BandHash(t.StripBmp, Bands), "screen", now);
        }

        // the picture of the window itself (the tile is covered or the panel is in the background)
        static void SampleWindow(Target t, IntPtr hw, Size size, long now)
        {
            if (t.LastPrint != 0 && now - t.LastPrint < PrintIntervalMs) return;
            t.LastPrint = now;
            if (!WinCapture.Print(hw, ref t.PrintBmp, size.Width, size.Height, true))
            {
                t.Reset();
                t.SetNoValue("Windows could not draw this window (PrintWindow failed)");
                return;
            }
            if (WinCapture.IsBlank(t.PrintBmp))
            {
                t.Reset();
                t.SetNoValue("the picture of the covered tile is black (PrintWindow does not work for this window on this PC)");
                return;
            }
            Count(t, WinCapture.BandHash(t.PrintBmp, 8), "window", now);
        }

        static void Count(Target t, ulong hash, string method, long now)
        {
            if (!t.HasPrev || t.PrevMethod != method)
            {
                // first picture, or switched from screen to window (or back): start again, no false change
                t.Changes.Clear();
                t.Since = now;
                t.HasPrev = true;
                t.PrevMethod = method;
                t.PrevHash = hash;
                return;
            }
            if (hash != t.PrevHash) t.Changes.Enqueue(now);
            t.PrevHash = hash;

            while (t.Changes.Count > 0 && now - t.Changes.Peek() > WindowMs) t.Changes.Dequeue();
            long span = Math.Min(WindowMs, now - t.Since);
            if (span < 500) { t.SetNoValue("measuring…"); return; }   // too little data to say anything yet
            t.SetFps(t.Changes.Count * 1000.0 / span, method);
        }
    }

    // ---- CPU / RAM / FPS per session, one line per minute (for the daily report) ----
    static class MetricsService
    {
        static readonly object Gate = new object();
        public const int KeepDays = 14;

        public class Sample
        {
            public DateTime Time;
            public string Session = "";
            public double Cpu = double.NaN, Ram = double.NaN, Fps = double.NaN;
        }

        static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpPanel"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(Dir, "metrics.csv"); }
        }

        static string Num(double v)
        {
            return double.IsNaN(v) ? "" : v.ToString("0.0", CultureInfo.InvariantCulture);
        }

        static double Parse(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        public static void Write(IEnumerable<Sample> rows)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var s in rows)
                    sb.Append(s.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(';')
                      .Append((s.Session ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ")).Append(';')
                      .Append(Num(s.Cpu)).Append(';').Append(Num(s.Ram)).Append(';').Append(Num(s.Fps))
                      .Append(Environment.NewLine);
                if (sb.Length == 0) return;
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);
                    File.AppendAllText(FilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch { }
        }

        public static List<Sample> Read(DateTime from, DateTime to)
        {
            var list = new List<Sample>();
            try
            {
                lock (Gate)
                {
                    if (!File.Exists(FilePath)) return list;
                    foreach (var line in File.ReadLines(FilePath, Encoding.UTF8))
                    {
                        var p = line.Split(';');
                        if (p.Length < 5) continue;
                        DateTime t;
                        if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                        if (t < from || t >= to) continue;
                        list.Add(new Sample { Time = t, Session = p[1], Cpu = Parse(p[2]), Ram = Parse(p[3]), Fps = Parse(p[4]) });
                    }
                }
            }
            catch { }
            return list;
        }

        // the raw lines of the last few days (for the diagnostic file)
        public static string ReadRecentText(int days)
        {
            var sb = new StringBuilder();
            try
            {
                string cutoff = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                lock (Gate)
                {
                    if (!File.Exists(FilePath)) return "";
                    sb.AppendLine("time;session;cpu%;ram%;fps");
                    foreach (var line in File.ReadLines(FilePath, Encoding.UTF8))
                        if (line.Length >= 10 && string.CompareOrdinal(line.Substring(0, 10), cutoff) >= 0) sb.AppendLine(line);
                }
            }
            catch { }
            return sb.ToString();
        }

        // throws away everything older than KeepDays so the file stays small
        public static void Prune()
        {
            try
            {
                lock (Gate)
                {
                    if (!File.Exists(FilePath)) return;
                    string cutoff = DateTime.Now.AddDays(-KeepDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var keep = File.ReadLines(FilePath, Encoding.UTF8)
                        .Where(l => l.Length >= 10 && string.CompareOrdinal(l.Substring(0, 10), cutoff) >= 0).ToList();
                    string tmp = FilePath + ".tmp";
                    File.WriteAllLines(tmp, keep, Encoding.UTF8);
                    File.Delete(FilePath);
                    File.Move(tmp, FilePath);
                }
            }
            catch { }
        }
    }

    // ---- unexpected errors go to errors.log (and into the diagnostic file) ----
    static class ErrorLog
    {
        static readonly object Gate = new object();

        public static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpPanel", "errors.log"); }
        }

        public static void Write(string where, Exception ex)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();   // keep it small
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  [" + where + "] v" + AppInfo.Version
                        + Environment.NewLine + (ex == null ? "(no details)" : ex.ToString()) + Environment.NewLine + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
