using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RdpPanel
{
    // one alt account: name, kind, the stock of Bee Swarm items, goals, a note and a picture of its hive
    public class AltAccount
    {
        public string Id = "";
        public string Name = "";
        public string Kind = "";
        public string Note = "";
        public long Gumdrops;
        public long Coconuts;
        public long Jellybeans;
        public long CloudVials;
        public long Bloomshakers;
        public long Nightbells;
        public long NectarShowerVials;
        public long SmokingPlanters;
        public Dictionary<string, long> Goals = new Dictionary<string, long>();   // goal per item (key = the item name); missing or 0 = no goal
        public long LastUpdatedTicks;
        public bool Collapsed;
        public string HiveImage = "";   // file name in %APPDATA%\RdpPanel\alts
    }

    // one day of one alt (the numbers in the order of AltItems.All)
    public class AltSnap
    {
        public string Day = "";
        public string AltId = "";
        public long[] V = new long[0];
    }

    public class AltLink
    {
        public string Session = "";
        public string AltId = "";
    }

    public class AltBackup
    {
        public List<AltAccount> Alts = new List<AltAccount>();
        public List<AltLink> Links = new List<AltLink>();
    }

    class AltItem
    {
        public string Emoji;
        public string Label;
        public Func<AltAccount, long> Get;
        public Action<AltAccount, long> Set;
    }

    static class AltItems
    {
        public static readonly AltItem[] All =
        {
            new AltItem { Emoji = "🍬", Label = "Gumdrops",            Get = a => a.Gumdrops,          Set = (a, v) => a.Gumdrops = v },
            new AltItem { Emoji = "🥥", Label = "Coconuts",            Get = a => a.Coconuts,          Set = (a, v) => a.Coconuts = v },
            new AltItem { Emoji = "🫘", Label = "Jellybeans",          Get = a => a.Jellybeans,        Set = (a, v) => a.Jellybeans = v },
            new AltItem { Emoji = "☁",  Label = "Cloud vials",         Get = a => a.CloudVials,        Set = (a, v) => a.CloudVials = v },
            new AltItem { Emoji = "🌸", Label = "Bloomshakers",        Get = a => a.Bloomshakers,      Set = (a, v) => a.Bloomshakers = v },
            new AltItem { Emoji = "🔔", Label = "Nightbells",          Get = a => a.Nightbells,        Set = (a, v) => a.Nightbells = v },
            new AltItem { Emoji = "🚿", Label = "Nectar shower vials", Get = a => a.NectarShowerVials, Set = (a, v) => a.NectarShowerVials = v },
            new AltItem { Emoji = "🪴", Label = "Smoking planters",    Get = a => a.SmokingPlanters,   Set = (a, v) => a.SmokingPlanters = v }
        };
    }

    // history: one line per alt per day in %APPDATA%\RdpPanel\alts-history.json
    static class AltStats
    {
        static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpPanel"); }
        }

        public static string HistoryPath { get { return Path.Combine(Dir, "alts-history.json"); } }

        static JavaScriptSerializer Ser()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        public static string Today { get { return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }

        public static List<AltSnap> Load()
        {
            try
            {
                if (!File.Exists(HistoryPath)) return new List<AltSnap>();
                var l = Ser().Deserialize<List<AltSnap>>(File.ReadAllText(HistoryPath, Encoding.UTF8));
                return l ?? new List<AltSnap>();
            }
            catch { return new List<AltSnap>(); }
        }

        public static void Save(List<AltSnap> l)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(HistoryPath, Ser().Serialize(l), new UTF8Encoding(false));
            }
            catch { }
        }

        // writes today's numbers of every alt (replaces the line of today); old lines (> 400 days) are dropped
        public static void SnapshotToday(List<AltAccount> alts)
        {
            if (alts.Count == 0) return;
            var hist = Load();
            string day = Today;
            bool changed = false;
            foreach (var a in alts)
            {
                var vals = AltItems.All.Select(i => i.Get(a)).ToArray();
                var s = hist.FirstOrDefault(x => x.AltId == a.Id && x.Day == day);
                if (s == null) { hist.Add(new AltSnap { Day = day, AltId = a.Id, V = vals }); changed = true; }
                else if (s.V == null || !s.V.SequenceEqual(vals)) { s.V = vals; changed = true; }
            }
            string cutoff = DateTime.Today.AddDays(-400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (hist.RemoveAll(x => string.CompareOrdinal(x.Day, cutoff) < 0) > 0) changed = true;
            if (changed) Save(hist);
        }

        // how much an item went up (or down) in the last N days; null = no history yet
        public static long? DeltaSince(List<AltSnap> hist, AltAccount a, int idx, int days)
        {
            var mine = hist.Where(x => x.AltId == a.Id && x.V != null && x.V.Length > idx).OrderBy(x => x.Day, StringComparer.Ordinal).ToList();
            if (mine.Count == 0) return null;
            string cutoff = DateTime.Today.AddDays(-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var reference = mine.LastOrDefault(x => string.CompareOrdinal(x.Day, cutoff) <= 0) ?? mine[0];
            return AltItems.All[idx].Get(a) - reference.V[idx];
        }

        public static string Signed(long d)
        {
            return (d >= 0 ? "+" : "−") + Math.Abs(d).ToString("N0");
        }
    }

    // the mouse wheel must not change a number or a choice by accident: it scrolls the alt list instead
    class NoWheelNumeric : NumericUpDown
    {
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
            NoWheel.Scroll(this, e.Delta);
        }
    }

    class NoWheelCombo : ComboBox
    {
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
            NoWheel.Scroll(this, e.Delta);
        }
    }

    static class NoWheel
    {
        // scrolls the nearest scrolling parent (the list of alt cards)
        public static void Scroll(Control c, int delta)
        {
            var p = c.Parent;
            while (p != null && !(p is ScrollableControl && ((ScrollableControl)p).AutoScroll)) p = p.Parent;
            var sc = p as ScrollableControl;
            if (sc == null) return;
            sc.AutoScrollPosition = new Point(-sc.AutoScrollPosition.X, -sc.AutoScrollPosition.Y - delta);
        }
    }

    // ---- Alt accounts: a card per alt ----
    class AltPage : Panel
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        static readonly AltItem[] Items = AltItems.All;
        static readonly long[] Steps = { 1, 10, 100, 1000, 10000 };

        const int CardW = 1060;
        const int HeadH = 66;      // the row with name, kind and session
        const int DetailsH = 276;
        readonly MainForm main;
        readonly FlowLayoutPanel flow = new FlowLayoutPanel();
        readonly Label lblTotal = new Label();
        readonly Label lblEmpty = new Label();
        readonly TextBox txtSearch = new TextBox();
        readonly ComboBox cmbSort = new NoWheelCombo();
        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 700 };
        readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly List<Image> loaded = new List<Image>();
        readonly List<Action> refreshers = new List<Action>();                         // session status per card
        readonly List<Tuple<AltAccount, Label>> weekLabels = new List<Tuple<AltAccount, Label>>();
        bool building;

        public AltPage(MainForm main)
        {
            this.main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(16);

            var bar = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Theme.Bg };
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
            var btnCsv = UI.Btn(bar, "Export CSV", 410, 0, 110, false);
            btnCsv.Click += (s, e) => ExportCsv();
            var btnImport = UI.Btn(bar, "Import CSV", 530, 0, 110, false);
            btnImport.Click += (s, e) => ImportCsv();
            var btnBackup = UI.Btn(bar, "Backup…", 650, 0, 100, false);
            btnBackup.Click += (s, e) => Backup();
            var btnRestore = UI.Btn(bar, "Restore…", 760, 0, 100, false);
            btnRestore.Click += (s, e) => Restore();

            Theme.StyleInput(txtSearch);
            txtSearch.Location = new Point(0, 40);
            txtSearch.Width = 240;
            txtSearch.HandleCreated += (s, e) => SendMessage(txtSearch.Handle, 0x1501, (IntPtr)1, "Search alts…");
            txtSearch.TextChanged += (s, e) => Rebuild();
            bar.Controls.Add(txtSearch);

            cmbSort.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSort.FlatStyle = FlatStyle.Flat;
            Theme.StyleInput(cmbSort);
            cmbSort.Location = new Point(252, 40);
            cmbSort.Width = 230;
            cmbSort.Items.Add("My own order");
            cmbSort.Items.Add("Name");
            cmbSort.Items.Add("Kind of alt");
            foreach (var it in Items) cmbSort.Items.Add("Most " + it.Emoji + " " + it.Label.ToLowerInvariant());
            cmbSort.SelectedIndex = Math.Max(0, Math.Min(cmbSort.Items.Count - 1, main.Cfg.AltSort));
            cmbSort.SelectedIndexChanged += (s, e) =>
            {
                if (building) return;
                main.Cfg.AltSort = cmbSort.SelectedIndex;
                main.Cfg.Save();
                Rebuild();
            };
            bar.Controls.Add(cmbSort);

            lblTotal.AutoSize = false;
            lblTotal.Location = new Point(0, 78);
            lblTotal.Size = new Size(1300, 28);
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

            lblEmpty.ForeColor = Theme.FgMuted;
            lblEmpty.Font = Theme.UI(10f);
            lblEmpty.AutoSize = true;
            lblEmpty.Margin = new Padding(0, 10, 0, 0);

            Controls.Add(flow);
            Controls.Add(bar);   // added last: docks above the list

            saveTimer.Tick += (s, e) =>
            {
                saveTimer.Stop();
                main.Cfg.Save();
                try { AltStats.SnapshotToday(main.Cfg.Alts); } catch { }
                RefreshWeekLabels();
            };
            statusTimer.Tick += (s, e) =>
            {
                if (!Visible) return;
                foreach (var r in refreshers) r();
            };
            statusTimer.Start();
            VisibleChanged += (s, e) => { if (!Visible && saveTimer.Enabled) { saveTimer.Stop(); main.Cfg.Save(); try { AltStats.SnapshotToday(main.Cfg.Alts); } catch { } } };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                saveTimer.Dispose();
                statusTimer.Dispose();
                foreach (var i in loaded) i.Dispose();
                loaded.Clear();
            }
            base.Dispose(disposing);
        }

        // called by the panel now and then, so a new day gets its line even when nothing changed
        public void SnapshotToday()
        {
            try { AltStats.SnapshotToday(main.Cfg.Alts); } catch { }
        }

        // jump to an alt from the search window (Ctrl+K)
        public void ShowAlt(AltAccount a)
        {
            txtSearch.Text = "";
            Rebuild();
            foreach (Control c in flow.Controls)
                if (c.Tag == a) { flow.ScrollControlIntoView(c); break; }
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
            var a = new AltAccount { Id = Guid.NewGuid().ToString("N"), Name = "Alt " + (main.Cfg.Alts.Count + 1), LastUpdatedTicks = DateTime.Now.Ticks };
            main.Cfg.Alts.Add(a);
            main.Cfg.Save();
            txtSearch.Text = "";
            Rebuild();
            foreach (Control c in flow.Controls)
                if (c.Tag == a) { flow.ScrollControlIntoView(c); break; }
        }

        void RemoveAlt(AltAccount a)
        {
            if (MessageBox.Show(main, "Remove alt account '" + a.Name + "'?", "Alt accounts", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { var p = ImagePath(a); if (p.Length > 0 && File.Exists(p)) File.Delete(p); } catch { }
            foreach (var sc in main.Cfg.Sessions) if (sc.AltId == a.Id) sc.AltId = "";
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

        // "This week: 🍬 +250  🥥 −3" (only items that changed)
        static string WeekText(List<AltSnap> hist, AltAccount a)
        {
            var parts = new List<string>();
            for (int i = 0; i < Items.Length; i++)
            {
                long? d = AltStats.DeltaSince(hist, a, i, 7);
                if (d.HasValue && d.Value != 0) parts.Add(Items[i].Emoji + " " + AltStats.Signed(d.Value));
            }
            return parts.Count == 0 ? "This week: no change" : "This week:  " + string.Join("   ", parts);
        }

        void RefreshWeekLabels()
        {
            var hist = AltStats.Load();
            foreach (var w in weekLabels) w.Item2.Text = WeekText(hist, w.Item1);
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

        List<AltAccount> VisibleAlts()
        {
            IEnumerable<AltAccount> list = main.Cfg.Alts;
            string q = txtSearch.Text.Trim().ToLowerInvariant();
            if (q.Length > 0) list = list.Where(a => (a.Name + " " + a.Kind + " " + a.Note).ToLowerInvariant().Contains(q));
            int sort = main.Cfg.AltSort;
            if (sort == 1) list = list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
            else if (sort == 2) list = list.OrderBy(a => a.Kind, StringComparer.CurrentCultureIgnoreCase);
            else if (sort >= 3 && sort - 3 < Items.Length)
            {
                var it = Items[sort - 3];
                list = list.OrderByDescending(a => it.Get(a));
            }
            return list.ToList();
        }

        public void Rebuild()
        {
            building = true;
            int keepSort = Math.Max(0, Math.Min(cmbSort.Items.Count - 1, main.Cfg.AltSort));
            if (cmbSort.SelectedIndex != keepSort) cmbSort.SelectedIndex = keepSort;
            building = false;

            flow.SuspendLayout();
            flow.Controls.Clear();
            foreach (var i in loaded) i.Dispose();
            loaded.Clear();
            refreshers.Clear();
            weekLabels.Clear();

            var hist = AltStats.Load();
            var list = VisibleAlts();
            foreach (var a in list) flow.Controls.Add(BuildCard(a, hist));
            if (list.Count == 0)
            {
                lblEmpty.Text = main.Cfg.Alts.Count == 0
                    ? "No alt accounts yet. Click \"+ Add alt account\" to add one."
                    : "No alt account matches your search.";
                flow.Controls.Add(lblEmpty);
            }
            flow.ResumeLayout();
            UpdateTotals();
        }

        Control BuildCard(AltAccount a, List<AltSnap> hist)
        {
            var outer = new Panel { Width = CardW, Height = 2 + HeadH + (a.Collapsed ? 0 : DetailsH), Margin = new Padding(0, 0, 0, 14), BackColor = Theme.Line, Padding = new Padding(1), Tag = a };
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            outer.Controls.Add(inner);
            var details = new Panel { Location = new Point(0, HeadH), Size = new Size(CardW - 2, DetailsH), BackColor = Theme.Surface, Visible = !a.Collapsed };
            inner.Controls.Add(details);

            // ---- row 1: name, kind, session
            UI.Lbl(inner, "Name", 16, 8, 160, true).Font = Theme.UI(8.5f);
            var txtName = new TextBox { Text = a.Name };
            UI.Put(inner, txtName, 16, 28, 180);
            txtName.TextChanged += (s, e) => { a.Name = txtName.Text; Touch(a, null); };

            UI.Lbl(inner, "Kind of alt", 206, 8, 160, true).Font = Theme.UI(8.5f);
            var txtKind = new TextBox { Text = a.Kind };
            UI.Put(inner, txtKind, 206, 28, 180);
            txtKind.HandleCreated += (s, e) => SendMessage(txtKind.Handle, 0x1501, (IntPtr)1, "e.g. gifted, sprout");
            txtKind.TextChanged += (s, e) => { a.Kind = txtKind.Text; Touch(a, null); };

            UI.Lbl(inner, "Session of this alt", 396, 8, 200, true).Font = Theme.UI(8.5f);
            var cmbSession = new NoWheelCombo { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            UI.Put(inner, cmbSession, 396, 28, 190);
            cmbSession.Items.Add("(none)");
            foreach (var sc0 in main.Cfg.Sessions) cmbSession.Items.Add(sc0.Name);
            var linked = main.Cfg.Sessions.FirstOrDefault(sc => sc.AltId == a.Id);
            int li = linked == null ? 0 : main.Cfg.Sessions.IndexOf(linked) + 1;
            cmbSession.SelectedIndex = li > 0 && li < cmbSession.Items.Count ? li : 0;
            var lblState = new Label { Location = new Point(596, 30), Size = new Size(150, 22), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, ForeColor = Theme.FgMuted };
            inner.Controls.Add(lblState);
            var btnGo = UI.Btn(inner, "Go to", 750, 27, 60, false);
            btnGo.Click += (s, e) =>
            {
                var t = TileOf(a);
                if (t != null) main.OpenTile(t);
            };
            Action refreshState = () =>
            {
                var t = TileOf(a);
                if (t == null) { lblState.Text = ""; btnGo.Enabled = false; return; }
                btnGo.Enabled = true;
                lblState.Text = "● " + t.ActivityState;
                lblState.ForeColor = t.Online ? Theme.Good : t.StatusLabel == "Dropped" ? Theme.Warn : Theme.FgMuted;
            };
            refreshState();
            refreshers.Add(refreshState);
            cmbSession.SelectedIndexChanged += (s, e) =>
            {
                foreach (var sc in main.Cfg.Sessions) if (sc.AltId == a.Id) sc.AltId = "";
                int idx = cmbSession.SelectedIndex;
                if (idx > 0 && idx - 1 < main.Cfg.Sessions.Count)
                {
                    var target = main.Cfg.Sessions[idx - 1];
                    target.AltId = a.Id;
                }
                main.Cfg.Save();
                refreshState();
            };

            var btnCollapse = UI.Btn(inner, a.Collapsed ? "▸" : "▾", 876, 27, 44, false);
            btnCollapse.Click += (s, e) =>
            {
                a.Collapsed = !a.Collapsed;
                details.Visible = !a.Collapsed;
                outer.Height = 2 + HeadH + (a.Collapsed ? 0 : DetailsH);
                btnCollapse.Text = a.Collapsed ? "▸" : "▾";
                main.Cfg.Save();
            };
            var del = UI.Btn(inner, "Remove alt", 928, 27, 110, false);
            del.ForeColor = Theme.Bad;
            del.Click += (s, e) => RemoveAlt(a);

            // ---- details: step, bottom strip (created first: the numbers use them)
            var cmbStep = new NoWheelCombo { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            foreach (var st in Steps) cmbStep.Items.Add(st.ToString("N0"));
            cmbStep.SelectedIndex = 1;   // 10
            var lblUpd = new Label { Location = new Point(590, 208), Size = new Size(210, 22), ForeColor = Theme.FgMuted, BackColor = Color.Transparent };
            lblUpd.Text = UpdText(a);

            // ---- items: 4 per row; each with - / + buttons, a goal and a progress line
            for (int i = 0; i < Items.Length; i++)
            {
                var it = Items[i];
                int x = 16 + (i % 4) * 190;
                int y = 8 + (i / 4) * 96;
                UI.Lbl(details, it.Emoji + "  " + it.Label + ":", x, y, 180, false).Font = Theme.UI(9f);

                var num = new NoWheelNumeric { Minimum = 0, Maximum = 999999999999999m, ThousandsSeparator = true, Value = Math.Max(0, it.Get(a)) };
                var minus = UI.Btn(details, "−", x, y + 22, 30, false);
                UI.Put(details, num, x + 34, y + 24, 92);
                var plus = UI.Btn(details, "+", x + 130, y + 22, 30, false);

                UI.Lbl(details, "🎯", x, y + 54, 22, true);
                var numGoal = new NoWheelNumeric { Minimum = 0, Maximum = 999999999999999m, ThousandsSeparator = true };
                long g0; a.Goals.TryGetValue(it.Label, out g0);
                numGoal.Value = Math.Max(0, g0);
                UI.Put(details, numGoal, x + 24, y + 52, 100);
                var pct = new Label { Location = new Point(x + 128, y + 54), Size = new Size(50, 20), BackColor = Color.Transparent, ForeColor = Theme.FgMuted, Font = Theme.UI(8.5f) };
                details.Controls.Add(pct);
                var track = new Panel { Location = new Point(x, y + 78), Size = new Size(160, 5), BackColor = Theme.Raised };
                var fill = new Panel { Location = new Point(0, 0), Size = new Size(0, 5), BackColor = Theme.Accent };
                track.Controls.Add(fill);
                details.Controls.Add(track);

                Action showGoal = () =>
                {
                    long v = (long)num.Value;
                    long goal = (long)numGoal.Value;
                    if (goal <= 0) { pct.Text = ""; fill.Width = 0; return; }
                    double r = Math.Min(1.0, (double)v / goal);
                    fill.Width = (int)(track.Width * r);
                    bool done = v >= goal;
                    pct.Text = done ? "✓" : (int)(r * 100) + "%";
                    pct.ForeColor = done ? Theme.Good : Theme.FgMuted;
                    fill.BackColor = done ? Theme.Good : Theme.Accent;
                };
                showGoal();

                long prev = (long)num.Value;
                num.ValueChanged += (s, e) =>
                {
                    long v = (long)num.Value;
                    it.Set(a, v);
                    Touch(a, lblUpd);
                    showGoal();
                    long goal = (long)numGoal.Value;
                    if (goal > 0 && v >= goal && prev < goal)
                        main.ShowBalloon("Alt accounts", a.Name + ": goal reached for " + it.Label + " (" + goal.ToString("N0") + ")");
                    prev = v;
                    QueueSave();
                };
                numGoal.ValueChanged += (s, e) =>
                {
                    long goal = (long)numGoal.Value;
                    if (goal <= 0) a.Goals.Remove(it.Label); else a.Goals[it.Label] = goal;
                    showGoal();
                    QueueSave();
                };
                minus.Click += (s, e) => num.Value = Math.Max(num.Minimum, num.Value - Steps[Math.Max(0, cmbStep.SelectedIndex)]);
                plus.Click += (s, e) => num.Value = Math.Min(num.Maximum, num.Value + Steps[Math.Max(0, cmbStep.SelectedIndex)]);
            }

            // ---- bottom strip: note, step, last updated, this week
            UI.Lbl(details, "Note", 16, 210, 40, true);
            var txtNote = new TextBox { Text = a.Note };
            UI.Put(details, txtNote, 56, 206, 380);
            txtNote.TextChanged += (s, e) => { a.Note = txtNote.Text; Touch(a, lblUpd); QueueSave(); };
            UI.Lbl(details, "Step", 450, 210, 40, true);
            UI.Put(details, cmbStep, 490, 206, 80);
            details.Controls.Add(lblUpd);

            var lblWeek = new Label { Location = new Point(16, 242), Size = new Size(760, 24), ForeColor = Theme.FgMuted, BackColor = Color.Transparent, Text = WeekText(hist, a) };
            details.Controls.Add(lblWeek);
            weekLabels.Add(Tuple.Create(a, lblWeek));

            var btnHist = UI.Btn(details, "📈  History", 800, 204, 226, false);
            btnHist.Click += (s, e) => new AltHistoryForm(a).Show(main);

            // ---- hive picture
            var pic = new PictureBox
            {
                Location = new Point(800, 8),
                Size = new Size(226, 150),
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
            details.Controls.Add(pic);

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

            var choose = UI.Btn(details, "Choose…", 800, 164, 88, false);
            choose.Click += (s, e) =>
            {
                using (var ofd = new OpenFileDialog { Title = "Hive picture", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" })
                {
                    if (ofd.ShowDialog(main) != DialogResult.OK) return;
                    try { using (var src = Image.FromFile(ofd.FileName)) setImage(src); }
                    catch { MessageBox.Show(main, "That file is not a picture.", "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                }
            };
            var paste = UI.Btn(details, "Paste", 894, 164, 70, false);
            paste.Click += (s, e) =>
            {
                Image img = null;
                try { if (Clipboard.ContainsImage()) img = Clipboard.GetImage(); } catch { }
                if (img == null) { MessageBox.Show(main, "There is no picture on the clipboard.", "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                using (img) setImage(img);
            };
            var clear = UI.Btn(details, "✕", 970, 164, 56, false);
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

        SessionTile TileOf(AltAccount a)
        {
            var sc = main.Cfg.Sessions.FirstOrDefault(x => x.AltId == a.Id);
            return sc == null ? null : main.Tiles.FirstOrDefault(t => t.Config == sc);
        }

        static string UpdText(AltAccount a)
        {
            return a.LastUpdatedTicks <= 0 ? "Last updated: –" : "Last updated: " + new DateTime(a.LastUpdatedTicks).ToString("dd-MM-yyyy HH:mm");
        }

        // something changed on this alt: remember the time
        void Touch(AltAccount a, Label lblUpd)
        {
            a.LastUpdatedTicks = DateTime.Now.Ticks;
            if (lblUpd != null) lblUpd.Text = UpdText(a);
            QueueSave();
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

        // ---------- export, backup, restore ----------

        static string Csv(string v)
        {
            return "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
        }

        void ExportCsv()
        {
            using (var sfd = new SaveFileDialog { Title = "Export alt accounts", Filter = "CSV file|*.csv", FileName = "alt-accounts-" + DateTime.Now.ToString("yyyyMMdd") + ".csv" })
            {
                if (sfd.ShowDialog(main) != DialogResult.OK) return;
                try
                {
                    var sb = new StringBuilder();
                    var head = new List<string> { "Name", "Kind", "Session", "Note" };
                    head.AddRange(Items.Select(i => i.Label));
                    head.AddRange(Items.Select(i => i.Label + " goal"));
                    head.Add("Last updated");
                    sb.AppendLine(string.Join(";", head.Select(Csv)));
                    foreach (var a in main.Cfg.Alts)
                    {
                        var sc = main.Cfg.Sessions.FirstOrDefault(x => x.AltId == a.Id);
                        var row = new List<string> { a.Name, a.Kind, sc == null ? "" : sc.Name, a.Note };
                        row.AddRange(Items.Select(i => i.Get(a).ToString(CultureInfo.InvariantCulture)));
                        row.AddRange(Items.Select(i => { long g; a.Goals.TryGetValue(i.Label, out g); return g.ToString(CultureInfo.InvariantCulture); }));
                        row.Add(a.LastUpdatedTicks <= 0 ? "" : new DateTime(a.LastUpdatedTicks).ToString("yyyy-MM-dd HH:mm"));
                        sb.AppendLine(string.Join(";", row.Select(Csv)));
                    }
                    File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));
                    MessageBox.Show(main, "Saved: " + sfd.FileName, "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(main, "Export failed: " + ex.Message, "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        // one line of a ; separated file with "quotes"
        static List<string> ParseCsvLine(string line)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ';') { res.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            res.Add(sb.ToString());
            return res;
        }

        static long ParseNum(string v)
        {
            long n;
            if (v == null) return 0;
            v = v.Trim().Replace(" ", "").Replace(".", "").Replace(",", "");
            return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0 ? n : 0;
        }

        // reads a file made by "Export CSV": an alt with the same name is updated, the others are added
        void ImportCsv()
        {
            using (var ofd = new OpenFileDialog { Title = "Import alt accounts", Filter = "CSV file|*.csv|All files|*.*" })
            {
                if (ofd.ShowDialog(main) != DialogResult.OK) return;
                try
                {
                    var lines = File.ReadAllLines(ofd.FileName, Encoding.UTF8).Where(l => l.Trim().Length > 0).ToList();
                    if (lines.Count < 2) { MessageBox.Show(main, "There is nothing to import in this file.", "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                    var head = ParseCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
                    int iName = head.IndexOf("name");
                    if (iName < 0) { MessageBox.Show(main, "This is not a file made by Export CSV (the column 'Name' is missing).", "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                    int iKind = head.IndexOf("kind"), iSession = head.IndexOf("session"), iNote = head.IndexOf("note");
                    var iVal = Items.Select(i => head.IndexOf(i.Label.ToLowerInvariant())).ToArray();
                    var iGoal = Items.Select(i => head.IndexOf(i.Label.ToLowerInvariant() + " goal")).ToArray();

                    int rows = lines.Count - 1;
                    if (MessageBox.Show(main, "Import " + rows + " alt account(s)?\r\nAn alt with the same name is updated, the others are added.", "Alt accounts",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                    int added = 0, updated = 0;
                    Func<List<string>, int, string> cell = (f, i) => i >= 0 && i < f.Count ? f[i] : "";
                    for (int r = 1; r < lines.Count; r++)
                    {
                        var f = ParseCsvLine(lines[r]);
                        string name = cell(f, iName).Trim();
                        if (name.Length == 0) continue;
                        var a = main.Cfg.Alts.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase));
                        if (a == null) { a = new AltAccount { Id = Guid.NewGuid().ToString("N"), Name = name }; main.Cfg.Alts.Add(a); added++; }
                        else updated++;
                        if (iKind >= 0) a.Kind = cell(f, iKind);
                        if (iNote >= 0) a.Note = cell(f, iNote);
                        for (int k = 0; k < Items.Length; k++)
                        {
                            if (iVal[k] >= 0) Items[k].Set(a, ParseNum(cell(f, iVal[k])));
                            if (iGoal[k] >= 0)
                            {
                                long g = ParseNum(cell(f, iGoal[k]));
                                if (g > 0) a.Goals[Items[k].Label] = g; else a.Goals.Remove(Items[k].Label);
                            }
                        }
                        a.LastUpdatedTicks = DateTime.Now.Ticks;
                        string sname = cell(f, iSession).Trim();
                        if (sname.Length > 0)
                        {
                            var sc = main.Cfg.Sessions.FirstOrDefault(x => x.Name == sname);
                            if (sc != null)
                            {
                                foreach (var o in main.Cfg.Sessions) if (o.AltId == a.Id) o.AltId = "";
                                sc.AltId = a.Id;
                            }
                        }
                    }
                    main.Cfg.Save();
                    SnapshotToday();
                    Rebuild();
                    MessageBox.Show(main, added + " added, " + updated + " updated.", "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(main, "Import failed: " + ex.Message, "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void Backup()
        {
            using (var sfd = new SaveFileDialog { Title = "Back up alt accounts", Filter = "Zip file|*.zip", FileName = "alt-accounts-backup-" + DateTime.Now.ToString("yyyyMMdd") + ".zip" })
            {
                if (sfd.ShowDialog(main) != DialogResult.OK) return;
                try
                {
                    saveTimer.Stop();
                    main.Cfg.Save();
                    SnapshotToday();
                    if (File.Exists(sfd.FileName)) File.Delete(sfd.FileName);
                    var bk = new AltBackup { Alts = main.Cfg.Alts };
                    foreach (var sc in main.Cfg.Sessions) if (!string.IsNullOrEmpty(sc.AltId)) bk.Links.Add(new AltLink { Session = sc.Name, AltId = sc.AltId });
                    using (var za = ZipFile.Open(sfd.FileName, ZipArchiveMode.Create))
                    {
                        var e = za.CreateEntry("alts.json");
                        using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
                            w.Write(new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(bk));
                        foreach (var a in main.Cfg.Alts)
                        {
                            var p = ImagePath(a);
                            if (p.Length > 0 && File.Exists(p)) za.CreateEntryFromFile(p, "images/" + a.HiveImage);
                        }
                        if (File.Exists(AltStats.HistoryPath)) za.CreateEntryFromFile(AltStats.HistoryPath, "history.json");
                    }
                    MessageBox.Show(main, "Backup saved: " + sfd.FileName, "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(main, "Backup failed: " + ex.Message, "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void Restore()
        {
            using (var ofd = new OpenFileDialog { Title = "Restore alt accounts", Filter = "Zip file|*.zip" })
            {
                if (ofd.ShowDialog(main) != DialogResult.OK) return;
                try
                {
                    AltBackup bk;
                    using (var za = ZipFile.OpenRead(ofd.FileName))
                    {
                        var e = za.GetEntry("alts.json");
                        if (e == null) { MessageBox.Show(main, "This is not a backup of the alt accounts.", "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                        string json;
                        using (var r = new StreamReader(e.Open(), Encoding.UTF8)) json = r.ReadToEnd();
                        bk = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<AltBackup>(json);
                        if (bk == null || bk.Alts == null) { MessageBox.Show(main, "The backup is empty.", "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                        if (MessageBox.Show(main, "Replace your " + main.Cfg.Alts.Count + " alt account(s) with the " + bk.Alts.Count + " from the backup?", "Alt accounts",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                        Directory.CreateDirectory(ImageDir);
                        foreach (var en in za.Entries)
                        {
                            if (en.Name.Length == 0) continue;
                            string target = null;
                            if (en.FullName.StartsWith("images/", StringComparison.OrdinalIgnoreCase)) target = Path.Combine(ImageDir, Path.GetFileName(en.Name));
                            else if (en.FullName == "history.json") target = AltStats.HistoryPath;
                            if (target == null) continue;
                            using (var s = en.Open())
                            using (var f = File.Create(target)) s.CopyTo(f);
                        }
                    }
                    main.Cfg.Alts.Clear();
                    main.Cfg.Alts.AddRange(bk.Alts);
                    foreach (var sc in main.Cfg.Sessions) sc.AltId = "";
                    if (bk.Links != null)
                        foreach (var l in bk.Links)
                        {
                            var sc = main.Cfg.Sessions.FirstOrDefault(x => x.Name == l.Session);
                            if (sc != null) sc.AltId = l.AltId;
                        }
                    main.Cfg.Save();
                    Rebuild();
                }
                catch (Exception ex) { MessageBox.Show(main, "Restore failed: " + ex.Message, "Alt accounts", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
    }

    // ---- history of one alt: a line chart per item ----
    class AltHistoryForm : Form
    {
        readonly AltAccount alt;
        readonly List<AltSnap> hist;
        readonly ComboBox cmbItem = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
        readonly AltLineChart chart = new AltLineChart();
        readonly Label lblSum = new Label();

        public AltHistoryForm(AltAccount a)
        {
            alt = a;
            hist = AltStats.Load();
            UI.Init(this, "History of " + a.Name, 820, 500);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            ShowInTaskbar = true;

            UI.Lbl(this, "Item", 16, 16, 40, true);
            foreach (var it in AltItems.All) cmbItem.Items.Add(it.Emoji + "  " + it.Label);
            UI.Put(this, cmbItem, 60, 12, 240);
            cmbItem.SelectedIndex = 0;
            cmbItem.SelectedIndexChanged += (s, e) => Fill();

            lblSum.Location = new Point(320, 14);
            lblSum.Size = new Size(480, 26);
            lblSum.ForeColor = Theme.Fg;
            lblSum.BackColor = Color.Transparent;
            Controls.Add(lblSum);

            chart.Location = new Point(16, 52);
            chart.Size = new Size(788, 430);
            chart.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(chart);
            Fill();
        }

        void Fill()
        {
            int idx = Math.Max(0, cmbItem.SelectedIndex);
            var mine = hist.Where(x => x.AltId == alt.Id && x.V != null && x.V.Length > idx).OrderBy(x => x.Day, StringComparer.Ordinal).ToList();
            string cutoff = DateTime.Today.AddDays(-60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            mine = mine.Where(x => string.CompareOrdinal(x.Day, cutoff) >= 0).ToList();
            chart.Days.Clear();
            chart.Vals.Clear();
            foreach (var m in mine)
            {
                DateTime d;
                if (DateTime.TryParseExact(m.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) { chart.Days.Add(d); chart.Vals.Add(m.V[idx]); }
            }
            chart.Invalidate();

            long? d1 = AltStats.DeltaSince(hist, alt, idx, 1);
            long? d7 = AltStats.DeltaSince(hist, alt, idx, 7);
            long? d30 = AltStats.DeltaSince(hist, alt, idx, 30);
            Func<long?, string> f = d => d.HasValue ? AltStats.Signed(d.Value) : "–";
            lblSum.Text = "Today " + f(d1) + "     week " + f(d7) + "     month " + f(d30) + "     now " + AltItems.All[idx].Get(alt).ToString("N0");
        }
    }

    class AltLineChart : Control
    {
        public readonly List<DateTime> Days = new List<DateTime>();
        public readonly List<long> Vals = new List<long>();

        public AltLineChart()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var f = Theme.UI(8.5f))
            using (var muted = new SolidBrush(Theme.FgMuted))
            {
                if (Vals.Count == 0)
                {
                    g.DrawString("No history yet. The panel saves your numbers once a day.", f, muted, 16, 16);
                    return;
                }
                var r = new Rectangle(80, 16, Math.Max(20, Width - 110), Math.Max(20, Height - 56));
                long min = Vals.Min(), max = Vals.Max();
                if (max == min) max = min + 1;
                using (var gridPen = new Pen(Theme.Line))
                    for (int i = 0; i <= 4; i++)
                    {
                        float y = r.Bottom - i * r.Height / 4f;
                        g.DrawLine(gridPen, r.Left, y, r.Right, y);
                        long v = min + (max - min) * i / 4;
                        g.DrawString(v.ToString("N0"), f, muted, 4, y - 8);
                    }
                DateTime d0 = Days.First(), d1 = Days.Last();
                double span = Math.Max(1, (d1 - d0).TotalDays);
                var pts = new List<PointF>();
                for (int i = 0; i < Vals.Count; i++)
                    pts.Add(new PointF(r.Left + (float)((Days[i] - d0).TotalDays / span * r.Width),
                                       r.Bottom - (float)((Vals[i] - min) / (double)(max - min) * r.Height)));
                if (pts.Count > 1) using (var pen = new Pen(Theme.Accent, 2.5f)) g.DrawLines(pen, pts.ToArray());
                using (var dot = new SolidBrush(Theme.Accent))
                    foreach (var p in pts) g.FillEllipse(dot, p.X - 3, p.Y - 3, 6, 6);
                g.DrawString(d0.ToString("dd-MM"), f, muted, r.Left, r.Bottom + 8);
                string last = d1.ToString("dd-MM");
                g.DrawString(last, f, muted, r.Right - g.MeasureString(last, f).Width, r.Bottom + 8);
            }
        }
    }
}
