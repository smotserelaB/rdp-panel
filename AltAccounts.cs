using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RdpPanel
{
    // one alt account: name, kind, the stock of Bee Swarm items and a picture of its hive
    public class AltAccount
    {
        public string Id = "";
        public string Name = "";
        public string Kind = "";
        public long Gumdrops;
        public long Coconuts;
        public long Jellybeans;
        public long CloudVials;
        public long Bloomshakers;
        public long Nightbells;
        public long NectarShowerVials;
        public long SmokingPlanters;
        public string HiveImage = "";   // file name in %APPDATA%\RdpPanel\alts
    }

    class AltItem
    {
        public string Emoji;
        public string Label;
        public Func<AltAccount, long> Get;
        public Action<AltAccount, long> Set;
    }

    // ---- Alt accounts: a row per alt with its items and a hive preview ----
    class AltPage : Panel
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        static readonly AltItem[] Items =
        {
            new AltItem { Emoji = "🍬", Label = "Gumdrops",           Get = a => a.Gumdrops,          Set = (a, v) => a.Gumdrops = v },
            new AltItem { Emoji = "🥥", Label = "Coconuts",           Get = a => a.Coconuts,          Set = (a, v) => a.Coconuts = v },
            new AltItem { Emoji = "🫘", Label = "Jellybeans",         Get = a => a.Jellybeans,        Set = (a, v) => a.Jellybeans = v },
            new AltItem { Emoji = "☁",  Label = "Cloud vials",        Get = a => a.CloudVials,        Set = (a, v) => a.CloudVials = v },
            new AltItem { Emoji = "🌸", Label = "Bloomshakers",       Get = a => a.Bloomshakers,      Set = (a, v) => a.Bloomshakers = v },
            new AltItem { Emoji = "🔔", Label = "Nightbells",         Get = a => a.Nightbells,        Set = (a, v) => a.Nightbells = v },
            new AltItem { Emoji = "🚿", Label = "Nectar shower vials", Get = a => a.NectarShowerVials, Set = (a, v) => a.NectarShowerVials = v },
            new AltItem { Emoji = "🪴", Label = "Smoking planters",   Get = a => a.SmokingPlanters,   Set = (a, v) => a.SmokingPlanters = v }
        };

        const int CardW = 940;
        readonly MainForm main;
        readonly FlowLayoutPanel flow = new FlowLayoutPanel();
        readonly Label lblTotal = new Label();
        readonly Label lblEmpty = new Label();
        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 700 };
        readonly List<Image> loaded = new List<Image>();

        public AltPage(MainForm main)
        {
            this.main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(16);

            var bar = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Bg };
            bar.Controls.Add(new Label
            {
                Text = "🐝  Alt accounts",
                Font = Theme.UI(13f, FontStyle.Bold),
                ForeColor = Theme.Fg,
                AutoSize = true,
                Location = new Point(0, 2),
                BackColor = Color.Transparent
            });
            var add = UI.Btn(bar, "+ Add alt account", 240, 0, 160, true);
            add.Click += (s, e) => AddAlt();
            lblTotal.AutoSize = false;
            lblTotal.Location = new Point(0, 40);
            lblTotal.Size = new Size(1200, 28);
            lblTotal.ForeColor = Theme.FgMuted;
            lblTotal.BackColor = Color.Transparent;
            lblTotal.Font = Theme.UI(9.5f);
            lblTotal.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bar.Controls.Add(lblTotal);

            flow.Dock = DockStyle.Fill;
            flow.AutoScroll = true;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.BackColor = Theme.Bg;

            lblEmpty.Text = "No alt accounts yet. Click \"+ Add alt account\" to add one.";
            lblEmpty.ForeColor = Theme.FgMuted;
            lblEmpty.Font = Theme.UI(10f);
            lblEmpty.AutoSize = true;
            lblEmpty.Margin = new Padding(0, 10, 0, 0);

            Controls.Add(flow);
            Controls.Add(bar);   // added last: docks above the list

            saveTimer.Tick += (s, e) => { saveTimer.Stop(); main.Cfg.Save(); };
            VisibleChanged += (s, e) => { if (!Visible && saveTimer.Enabled) { saveTimer.Stop(); main.Cfg.Save(); } };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                saveTimer.Dispose();
                foreach (var i in loaded) i.Dispose();
                loaded.Clear();
            }
            base.Dispose(disposing);
        }

        // ---------- data ----------

        static string ImageDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpPanel", "alts"); }
        }

        string ImagePath(AltAccount a)
        {
            return string.IsNullOrEmpty(a.HiveImage) ? "" : Path.Combine(ImageDir, a.HiveImage);
        }

        void QueueSave()
        {
            saveTimer.Stop();
            saveTimer.Start();
            UpdateTotals();
        }

        void AddAlt()
        {
            var a = new AltAccount { Id = Guid.NewGuid().ToString("N"), Name = "Alt " + (main.Cfg.Alts.Count + 1) };
            main.Cfg.Alts.Add(a);
            main.Cfg.Save();
            Rebuild();
            flow.ScrollControlIntoView(flow.Controls[flow.Controls.Count - 1]);
        }

        void RemoveAlt(AltAccount a)
        {
            if (MessageBox.Show(main, "Remove alt account '" + a.Name + "'?", "Alt accounts", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { var p = ImagePath(a); if (p.Length > 0 && File.Exists(p)) File.Delete(p); } catch { }
            main.Cfg.Alts.Remove(a);
            main.Cfg.Save();
            Rebuild();
        }

        void UpdateTotals()
        {
            var alts = main.Cfg.Alts;
            if (alts.Count == 0) { lblTotal.Text = ""; return; }
            var parts = new List<string>();
            foreach (var it in Items) parts.Add(it.Emoji + " " + alts.Sum(a => it.Get(a)).ToString("N0"));
            lblTotal.Text = alts.Count + " alt" + (alts.Count == 1 ? "" : "s") + "  ·  total:  " + string.Join("   ", parts);
        }

        // ---------- pictures ----------

        // reads a picture without keeping the file locked
        static Image LoadImage(string file)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
                using (var fs = File.OpenRead(file))
                using (var img = Image.FromStream(fs))
                    return new Bitmap(img);
            }
            catch { return null; }
        }

        // makes the picture smaller (max 800x500) and keeps it as a jpg next to the config
        bool StoreImage(AltAccount a, Image src)
        {
            try
            {
                Directory.CreateDirectory(ImageDir);
                double sc = Math.Min(1.0, Math.Min(800.0 / src.Width, 500.0 / src.Height));
                int w = Math.Max(1, (int)(src.Width * sc));
                int h = Math.Max(1, (int)(src.Height * sc));
                string name = a.Id + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".jpg";
                using (var bmp = new Bitmap(w, h))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, w, h);
                    }
                    bmp.Save(Path.Combine(ImageDir, name), ImageFormat.Jpeg);
                }
                try { var old = ImagePath(a); if (old.Length > 0 && File.Exists(old)) File.Delete(old); } catch { }
                a.HiveImage = name;
                main.Cfg.Save();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(main, "The picture could not be saved: " + ex.Message, "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        // ---------- screen ----------

        public void Rebuild()
        {
            flow.SuspendLayout();
            flow.Controls.Clear();
            foreach (var i in loaded) i.Dispose();
            loaded.Clear();

            foreach (var a in main.Cfg.Alts) flow.Controls.Add(BuildCard(a));
            if (main.Cfg.Alts.Count == 0) flow.Controls.Add(lblEmpty);
            flow.ResumeLayout();
            UpdateTotals();
        }

        Control BuildCard(AltAccount a)
        {
            var outer = new Panel { Width = CardW, Height = 226, Margin = new Padding(0, 0, 0, 14), BackColor = Theme.Line, Padding = new Padding(1) };
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            outer.Controls.Add(inner);

            // row 1: name and kind of alt
            UI.Lbl(inner, "Name", 16, 10, 200, true).Font = Theme.UI(8.5f);
            var txtName = new TextBox { Text = a.Name };
            UI.Put(inner, txtName, 16, 28, 220);
            txtName.TextChanged += (s, e) => { a.Name = txtName.Text; QueueSave(); };

            UI.Lbl(inner, "Kind of alt", 250, 10, 200, true).Font = Theme.UI(8.5f);
            var txtKind = new TextBox { Text = a.Kind };
            UI.Put(inner, txtKind, 250, 28, 220);
            txtKind.HandleCreated += (s, e) => SendMessage(txtKind.Handle, 0x1501, (IntPtr)1, "e.g. gifted, sprout, farm");
            txtKind.TextChanged += (s, e) => { a.Kind = txtKind.Text; QueueSave(); };

            var del = UI.Btn(inner, "Remove alt", 484, 27, 110, false);
            del.ForeColor = Theme.Bad;
            del.Click += (s, e) => RemoveAlt(a);

            // row 2 and 3: the items (4 per row)
            for (int i = 0; i < Items.Length; i++)
            {
                var it = Items[i];
                int x = 16 + (i % 4) * 172;
                int y = 76 + (i / 4) * 66;
                UI.Lbl(inner, it.Emoji + "  " + it.Label + ":", x, y, 166, false).Font = Theme.UI(9f);
                var num = new NumericUpDown
                {
                    Minimum = 0,
                    Maximum = 999999999999999m,
                    ThousandsSeparator = true,
                    Value = Math.Max(0, it.Get(a))
                };
                UI.Put(inner, num, x, y + 24, 160);
                num.ValueChanged += (s, e) => { it.Set(a, (long)num.Value); QueueSave(); };
            }

            // hive picture
            var pic = new PictureBox
            {
                Location = new Point(712, 12),
                Size = new Size(214, 146),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Theme.Well,
                Cursor = Cursors.Hand,
                AllowDrop = true
            };
            var lblNo = new Label
            {
                Text = "🍯  No hive picture\r\n(drop one here)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.FgMuted,
                BackColor = Color.Transparent,
                Enabled = false
            };
            pic.Controls.Add(lblNo);
            inner.Controls.Add(pic);

            Action show = () =>
            {
                var old = pic.Image;
                var img = LoadImage(ImagePath(a));
                pic.Image = img;
                if (img != null) loaded.Add(img);
                if (old != null) { loaded.Remove(old); old.Dispose(); }
                lblNo.Visible = img == null;
            };
            show();

            Action<Image> setImage = img =>
            {
                if (img == null) return;
                if (StoreImage(a, img)) show();
            };

            var choose = UI.Btn(inner, "Choose…", 712, 166, 88, false);
            choose.Click += (s, e) =>
            {
                using (var ofd = new OpenFileDialog { Title = "Hive picture", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" })
                {
                    if (ofd.ShowDialog(main) != DialogResult.OK) return;
                    try { using (var src = Image.FromFile(ofd.FileName)) setImage(src); }
                    catch { MessageBox.Show(main, "That file is not a picture.", "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                }
            };
            var paste = UI.Btn(inner, "Paste", 806, 166, 66, false);
            paste.Click += (s, e) =>
            {
                Image img = null;
                try { if (Clipboard.ContainsImage()) img = Clipboard.GetImage(); } catch { }
                if (img == null) { MessageBox.Show(main, "There is no picture on the clipboard.", "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                using (img) setImage(img);
            };
            var clear = UI.Btn(inner, "✕", 878, 166, 48, false);
            clear.Click += (s, e) =>
            {
                try { var p = ImagePath(a); if (p.Length > 0 && File.Exists(p)) File.Delete(p); } catch { }
                a.HiveImage = "";
                main.Cfg.Save();
                show();
            };

            pic.DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            pic.DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                try { using (var src = Image.FromFile(files[0])) setImage(src); }
                catch { MessageBox.Show(main, "That file is not a picture.", "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            pic.Click += (s, e) => ViewLarge(a);
            return outer;
        }

        // the picture large in its own window
        void ViewLarge(AltAccount a)
        {
            var img = LoadImage(ImagePath(a));
            if (img == null) return;
            var f = new Form
            {
                Text = "Hive of " + a.Name,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(Math.Min(img.Width, 1000), Math.Min(img.Height, 640)),
                BackColor = Theme.Bg,
                ShowInTaskbar = false
            };
            var pb = new PictureBox { Dock = DockStyle.Fill, Image = img, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Bg };
            f.Controls.Add(pb);
            f.FormClosed += (s, e) => { pb.Image = null; img.Dispose(); };
            f.KeyPreview = true;
            f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
            f.Show(main);
        }
    }
}
