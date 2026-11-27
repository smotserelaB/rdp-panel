using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RdpPanel
{
    public class Notice
    {
        public string Time = "";
        public string Session = "";
        public string Text = "";
        public int Level;   // 0 information, 1 warning, 2 problem
    }

    // the last 100 notifications, also after a restart
    static class Notices
    {
        static readonly List<Notice> list = new List<Notice>();
        public static int Unread;

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpPanel", "notifications.json"); }
        }

        static Notices()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var l = new JavaScriptSerializer().Deserialize<List<Notice>>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (l != null) list.AddRange(l.Take(100));
                }
            }
            catch { }
        }

        public static void Add(string session, string text, int level)
        {
            lock (list)
            {
                list.Insert(0, new Notice { Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Session = session ?? "", Text = text ?? "", Level = level });
                while (list.Count > 100) list.RemoveAt(list.Count - 1);
                Unread++;
            }
            Save();
        }

        public static List<Notice> All()
        {
            lock (list) return list.ToList();
        }

        public static void Clear()
        {
            lock (list) { list.Clear(); Unread = 0; }
            Save();
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string json;
                lock (list) json = new JavaScriptSerializer().Serialize(list);
                File.WriteAllText(FilePath, json, new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // the window behind the bell
    class NoticeForm : Form
    {
        readonly ListView lv = UI.NewList();

        public NoticeForm()
        {
            UI.Init(this, "Notifications", 760, 460);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            ShowInTaskbar = true;

            lv.Dock = DockStyle.Fill;
            lv.Columns.Add("Time", 140);
            lv.Columns.Add("Session", 160);
            lv.Columns.Add("Message", 420);
            Controls.Add(lv);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Theme.Bg };
            var clear = UI.Btn(bar, "Clear the list", 10, 8, 150, false);
            clear.Click += (s, e) => { Notices.Clear(); Fill(); };
            Controls.Add(bar);

            Resize += (s, e) => { if (lv.Columns.Count == 3) lv.Columns[2].Width = Math.Max(160, lv.ClientSize.Width - 320); };
            Fill();
            Notices.Unread = 0;
        }

        void Fill()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var n in Notices.All())
            {
                var it = new ListViewItem(n.Time) { UseItemStyleForSubItems = false };
                it.SubItems.Add(n.Session);
                var m = it.SubItems.Add(n.Text);
                m.ForeColor = n.Level >= 2 ? Theme.Bad : n.Level == 1 ? Theme.Warn : Theme.Fg;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
        }
    }
}
