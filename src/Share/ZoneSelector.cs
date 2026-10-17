using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Voile sur tous les écrans : glisser pour choisir la zone à partager, Échap pour annuler.</summary>
    sealed class ZoneSelector : Form
    {
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);

        readonly Action<Rectangle?> done;
        readonly Rectangle screen;
        Point start, cur;
        bool dragging, finished;

        ZoneSelector(Action<Rectangle?> done)
        {
            this.done = done;
            // bureau virtuel complet, en pixels physiques
            screen = new Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.35;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
            Bounds = screen;
        }

        /// <summary>Ouvre le sélecteur ; "done" reçoit la zone (coordonnées écran) ou null si annulé.</summary>
        public static void Pick(Action<Rectangle?> done)
        {
            var prev = SetThreadDpiAwarenessContext(new IntPtr(-4)); // fenêtre à l'échelle physique sur tous les écrans
            try
            {
                var f = new ZoneSelector(done);
                f.Show();
                f.Bounds = f.screen;
                f.Activate();
            }
            finally { SetThreadDpiAwarenessContext(prev); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Finish(null);
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Finish(null); return; }
            start = cur = e.Location;
            dragging = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            cur = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            var r = Norm(start, e.Location);
            // dimensions paires (exigées par la vidéo), 16 × 16 minimum
            r.Width = r.Width / 2 * 2;
            r.Height = r.Height / 2 * 2;
            Finish(r.Width >= 16 && r.Height >= 16 ? new Rectangle(r.X + screen.X, r.Y + screen.Y, r.Width, r.Height) : (Rectangle?)null);
        }

        static Rectangle Norm(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var font = new Font("Segoe UI", 16f))
            {
                string help = "Glissez pour sélectionner la zone à partager — Échap ou clic droit pour annuler";
                var sz = g.MeasureString(help, font);
                // consigne centrée sur l'écran principal
                var main = Screen.PrimaryScreen.Bounds;
                g.DrawString(help, font, Brushes.White, main.X - screen.X + (main.Width - sz.Width) / 2, main.Y - screen.Y + 40);
            }
            string info = "X " + (cur.X + screen.X) + "  Y " + (cur.Y + screen.Y);
            if (dragging)
            {
                var r = Norm(start, cur);
                using (var pen = new Pen(Color.Red, 2)) g.DrawRectangle(pen, r);
                info += "\nL " + r.Width + "  H " + r.Height;
            }
            using (var mono = new Font("Consolas", 12f)) g.DrawString(info, mono, Brushes.Yellow, cur.X + 16, cur.Y + 16);
        }

        void Finish(Rectangle? r)
        {
            if (finished) return;
            finished = true;
            Close();
            done(r);
        }

    }
}
