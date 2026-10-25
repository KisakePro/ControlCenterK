using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>
    /// Écran figé : l'image du bureau est affichée assombrie ; glisser = zone, clic = fenêtre survolée (cadre bleu),
    /// Entrée = écran sous la souris, Échap ou clic droit = annuler.
    /// </summary>
    sealed class CaptureOverlay : Form
    {
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out ScreenCapture.RECT r, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        delegate bool EnumProc(IntPtr h, IntPtr l);

        readonly Bitmap frozen, dimmed;
        readonly Rectangle desk;
        readonly Action<Bitmap, Rectangle> done;
        readonly List<Rectangle> windows = new List<Rectangle>(); // fenêtres visibles, de la plus haute à la plus basse
        Point start, cur;
        bool dragging, finished;
        Rectangle hover;

        CaptureOverlay(Bitmap frozen, Rectangle desk, Action<Bitmap, Rectangle> done)
        {
            this.frozen = frozen;
            this.desk = desk;
            this.done = done;
            dimmed = new Bitmap(frozen.Width, frozen.Height);
            using (var g = Graphics.FromImage(dimmed))
            using (var veil = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            {
                g.DrawImageUnscaled(frozen, 0, 0);
                g.FillRectangle(veil, 0, 0, frozen.Width, frozen.Height);
            }
            ListWindows();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            Bounds = desk;
        }

        /// <summary>Affiche l'écran figé ; "done" reçoit la capture découpée (ou null si annulé).</summary>
        public static void Show(Bitmap frozen, Rectangle desk, Action<Bitmap, Rectangle> done)
        {
            var prev = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                var f = new CaptureOverlay(frozen, desk, done);
                f.Show();
                f.Bounds = desk;
                f.Activate();
                f.Focus();
            }
            finally { SetThreadDpiAwarenessContext(prev); }
        }

        void ListWindows()
        {
            EnumWindows((h, l) =>
            {
                int cloaked;
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                if (DwmGetWindowAttribute(h, 14 /* CLOAKED */, out cloaked, 4) == 0 && cloaked != 0) return true;
                if ((GetWindowLong(h, -20) & 0x80 /* TOOLWINDOW */) != 0) return true;
                ScreenCapture.RECT r;
                if (DwmGetWindowAttribute(h, 9, out r, 16) != 0) return true;
                var rect = Rectangle.Intersect(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), desk);
                if (rect.Width > 20 && rect.Height > 20) windows.Add(rect);
                return true;
            }, IntPtr.Zero);
        }

        Point Screen(Point client) { return new Point(client.X + desk.X, client.Y + desk.Y); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Finish(Rectangle.Empty); return; }
            start = cur = e.Location;
            dragging = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            cur = e.Location;
            if (!dragging)
            {
                var p = Screen(e.Location);
                hover = Rectangle.Empty;
                foreach (var w in windows) if (w.Contains(p)) { hover = w; break; }
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            var r = Norm(start, e.Location);
            if (r.Width < 4 && r.Height < 4) { Finish(hover); return; } // simple clic : fenêtre survolée
            Finish(new Rectangle(r.X + desk.X, r.Y + desk.Y, r.Width, r.Height));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Finish(Rectangle.Empty);
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) Finish(System.Windows.Forms.Screen.FromPoint(Screen(cur)).Bounds);
            base.OnKeyDown(e);
        }

        static Rectangle Norm(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(dimmed, 0, 0);
            Rectangle sel = dragging ? Norm(start, cur)
                          : hover.IsEmpty ? Rectangle.Empty : new Rectangle(hover.X - desk.X, hover.Y - desk.Y, hover.Width, hover.Height);
            if (!sel.IsEmpty)
            {
                // zone choisie : image nette à l'intérieur du voile
                g.DrawImage(frozen, sel, sel, GraphicsUnit.Pixel);
                using (var pen = new Pen(dragging ? Color.FromArgb(76, 141, 255) : Color.FromArgb(76, 141, 255), dragging ? 2 : 3))
                    g.DrawRectangle(pen, sel);
            }
            using (var font = new Font("Segoe UI", 11f))
            {
                // dimensions près du curseur
                string info = dragging ? sel.Width + " × " + sel.Height : !hover.IsEmpty ? "Fenêtre " + hover.Width + " × " + hover.Height : "";
                if (info.Length > 0)
                {
                    var sz = g.MeasureString(info, font);
                    var box = new RectangleF(cur.X + 18, cur.Y + 18, sz.Width + 10, sz.Height + 4);
                    using (var bg = new SolidBrush(Color.FromArgb(200, 20, 22, 28))) g.FillRectangle(bg, box);
                    g.DrawString(info, font, Brushes.White, box.X + 5, box.Y + 2);
                }
                // aide en haut de l'écran sous la souris
                var scr = System.Windows.Forms.Screen.FromPoint(Screen(cur)).Bounds;
                string help = "Glisser : zone   ·   Clic : fenêtre   ·   Entrée : écran entier   ·   Échap : annuler";
                var hs = g.MeasureString(help, font);
                float hx = scr.X - desk.X + (scr.Width - hs.Width) / 2, hy = scr.Y - desk.Y + 18;
                using (var bg = new SolidBrush(Color.FromArgb(200, 20, 22, 28)))
                using (var path = Rounded(new RectangleF(hx - 14, hy - 6, hs.Width + 28, hs.Height + 12), 8))
                    g.FillPath(bg, path);
                g.DrawString(help, font, Brushes.White, hx, hy);
            }
        }

        static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
            p.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
            p.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
            p.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Finish(Rectangle r)
        {
            if (finished) return;
            finished = true;
            Hide();
            Bitmap img = null;
            r = Rectangle.Intersect(r, desk);
            if (r.Width >= 2 && r.Height >= 2) img = ScreenCapture.Crop(frozen, desk, r);
            Close();
            frozen.Dispose();
            dimmed.Dispose();
            done(img, r);
        }
    }

    /// <summary>Menu après la sélection (façon ShareX) : copier, enregistrer, éditeur… avec raccourcis clavier.</summary>
    sealed class CaptureMenu : Form
    {
        readonly Bitmap img;
        readonly Action closed;
        bool handled;

        static readonly string[][] Items =
        {
            new[] { "copy", "Copier dans le presse-papiers", "Ctrl+C", "" },
            new[] { "save", "Enregistrer", "Ctrl+S", "" },
            new[] { "copysave", "Copier et enregistrer", "Entrée", "" },
            new[] { "saveas", "Enregistrer sous…", "Ctrl+Maj+S", "" },
            new[] { "editor", "Ouvrir l'éditeur", "E", "" },
            new[] { "cancel", "Annuler", "Échap", "" },
        };

        CaptureMenu(Bitmap img, Action closed)
        {
            this.img = img;
            this.closed = closed;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            BackColor = Theme.Border;
            Padding = new Padding(1);
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
            Controls.Add(panel);

            // aperçu de la capture
            int w = Theme.S(300);
            var thumb = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Image = img, BackColor = Theme.Surface,
                Bounds = new Rectangle(Theme.S(10), Theme.S(10), w - Theme.S(20), Theme.S(130)) };
            var size = Theme.Label(img.Width + " × " + img.Height + " px", Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            size.Location = new Point(Theme.S(12), thumb.Bottom + Theme.S(6));
            panel.Controls.AddRange(new Control[] { thumb, size });
            int y = size.Bottom + Theme.S(8);
            foreach (var it in Items)
            {
                string action = it[0];
                var b = new MenuRow(it[3], it[1], it[2]) { Bounds = new Rectangle(Theme.S(4), y, w - Theme.S(8), Theme.S(36)) };
                b.Click += (s, e) => Do(action);
                panel.Controls.Add(b);
                y += Theme.S(36);
            }
            ClientSize = new Size(w, y + Theme.S(6));
        }

        public static void Show(Bitmap img, Rectangle near, Action closed)
        {
            var m = new CaptureMenu(img, closed);
            // à droite de la sélection si possible, sinon à gauche ; toujours dans l'écran
            var scr = Screen.FromPoint(new Point(near.Right, near.Top)).WorkingArea;
            int x = near.Right + 12 + m.Width <= scr.Right ? near.Right + 12 : Math.Max(scr.X, near.X - 12 - m.Width);
            int y = Math.Max(scr.Y, Math.Min(near.Y, scr.Bottom - m.Height));
            if (near.Width >= scr.Width - 40) { x = scr.X + (scr.Width - m.Width) / 2; y = scr.Y + (scr.Height - m.Height) / 2; }
            m.Location = new Point(x, y);
            m.Show();
            m.Activate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Do("cancel");
            else if (e.Control && e.Shift && e.KeyCode == Keys.S) Do("saveas");
            else if (e.Control && e.KeyCode == Keys.S) Do("save");
            else if (e.Control && e.KeyCode == Keys.C) Do("copy");
            else if (e.KeyCode == Keys.Enter) Do("copysave");
            else if (e.KeyCode == Keys.E) Do("editor");
            base.OnKeyDown(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // clic ailleurs : le menu reste ouvert tant qu'aucun choix n'est fait (sauf boîte « Enregistrer sous »)
        }

        void Do(string action)
        {
            if (handled) return;
            handled = true;
            Hide();
            var copy = (Bitmap)img.Clone();
            Close();
            img.Dispose();
            ScreenCapture.Act(action, copy);
            closed();
        }

        /// <summary>Ligne du menu : icône, libellé, raccourci.</summary>
        sealed class MenuRow : Control
        {
            readonly string glyph, text, keys;
            bool over;

            public MenuRow(string glyph, string text, string keys)
            {
                this.glyph = glyph; this.text = text; this.keys = keys;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(over ? Theme.Surface : Theme.Card);
                TextRenderer.DrawText(g, glyph, Theme.Icon(11f), new Rectangle(Theme.S(8), 0, Theme.S(28), Height), over ? Theme.Accent : Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, text, Theme.Ui(9.5f), new Rectangle(Theme.S(42), 0, Width, Height), Theme.Text, TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, keys, Theme.Ui(8.5f), new Rectangle(0, 0, Width - Theme.S(10), Height), Theme.Dim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            }
        }
    }

    /// <summary>Notification discrète (2,5 s), sans prendre le focus ; un clic ouvre l'image.</summary>
    sealed class CaptureToast : Form
    {
        public CaptureToast(string text, string path)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Card;
            var l = Theme.Label("✓  " + text, Theme.Ui(9.5f), Theme.Text, Theme.Card);
            l.Location = new Point(Theme.S(14), Theme.S(12));
            Controls.Add(l);
            ClientSize = new Size(l.Width + Theme.S(28), l.Height + Theme.S(24));
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - Theme.S(16), wa.Bottom - Height - Theme.S(16));
            if (path != null)
            {
                Cursor = l.Cursor = Cursors.Hand;
                EventHandler open = (s, e) => { try { System.Diagnostics.Process.Start(path); } catch { } Close(); };
                Click += open; l.Click += open;
            }
            var t = new Timer { Interval = 2500 };
            t.Tick += (s, e) => { t.Dispose(); Close(); };
            t.Start();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }
}
