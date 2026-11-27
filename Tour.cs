using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RdpPanel
{
    class TourStep
    {
        public Func<Control> Target;
        public string Title = "";
        public string Text = "";
        public bool Right;   // show it to the right of the target (for the list on the left)
    }

    // a short tour: a small card that points at the main parts of the panel, one after the other
    class TourForm : Form
    {
        readonly Form owner;
        readonly List<TourStep> steps;
        int index;
        readonly Label lblTitle = new Label();
        readonly Label lblText = new Label();
        readonly Label lblStep = new Label();
        readonly Button btnNext;
        readonly Button btnSkip;

        public TourForm(Form owner, List<TourStep> steps)
        {
            this.owner = owner;
            this.steps = steps;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Surface;
            Size = new Size(380, 188);

            lblTitle.SetBounds(18, 14, 344, 26);
            lblTitle.Font = Theme.UI(11.5f, FontStyle.Bold);
            lblTitle.ForeColor = Theme.Fg;
            lblTitle.BackColor = Color.Transparent;
            lblText.SetBounds(18, 46, 344, 80);
            lblText.ForeColor = Theme.FgMuted;
            lblText.BackColor = Color.Transparent;
            lblStep.SetBounds(18, 146, 80, 22);
            lblStep.ForeColor = Theme.FgMuted;
            lblStep.BackColor = Color.Transparent;
            Controls.AddRange(new Control[] { lblTitle, lblText, lblStep });

            btnSkip = UI.Btn(this, "Skip", 186, 140, 84, false);
            btnNext = UI.Btn(this, "Next", 278, 140, 84, true);
            btnSkip.Click += (s, e) => Close();
            btnNext.Click += (s, e) =>
            {
                if (index >= steps.Count - 1) { Close(); return; }
                index++;
                ShowStep();
            };
            AcceptButton = btnNext;
            CancelButton = btnSkip;
            SizeChanged += (s, e) => Round();
            Round();
            ShowStep();
        }

        void Round()
        {
            var old = Region;
            using (var p = Rounded.Path(new Rectangle(0, 0, Width, Height), 14)) Region = new Region(p);
            if (old != null) old.Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Rounded.Path(new Rectangle(1, 1, Width - 3, Height - 3), 13))
            using (var pen = new Pen(Theme.Accent, 2f)) e.Graphics.DrawPath(pen, p);
        }

        void ShowStep()
        {
            var st = steps[index];
            lblTitle.Text = st.Title;
            lblText.Text = st.Text;
            lblStep.Text = (index + 1) + " / " + steps.Count;
            btnNext.Text = index >= steps.Count - 1 ? "Done" : "Next";
            Place(st);
        }

        void Place(TourStep st)
        {
            var wa = Screen.FromControl(owner).WorkingArea;
            Control target = null;
            try { target = st.Target(); } catch { }
            int x, y;
            if (target == null || !target.Visible || target.IsDisposed)
            {
                x = owner.Left + (owner.Width - Width) / 2;
                y = owner.Top + (owner.Height - Height) / 2;
            }
            else
            {
                var r = target.RectangleToScreen(target.ClientRectangle);
                if (st.Right) { x = r.Right + 12; y = r.Top + 40; }
                else
                {
                    x = r.Left + (r.Width - Width) / 2;
                    y = r.Bottom + 12;
                    if (y + Height > wa.Bottom) y = r.Top - Height - 12;
                }
            }
            x = Math.Max(wa.Left + 8, Math.Min(x, wa.Right - Width - 8));
            y = Math.Max(wa.Top + 8, Math.Min(y, wa.Bottom - Height - 8));
            Location = new Point(x, y);
        }
    }
}
