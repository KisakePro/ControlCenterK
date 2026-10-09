using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>
    /// Module « FPS » : quand la fenêtre au premier plan appartient à un programme choisi, affiche par-dessus
    /// le nombre d'images par seconde et une courbe des dernières secondes. Tout tourne sur le thread de l'interface,
    /// avec un relevé tous les quarts de seconde ; la mesure elle-même est faite par FpsCapture.
    /// </summary>
    static class FpsModule
    {
        const int TickMs = 250, PerSecond = 1000 / TickMs;

        static AppConfig cfg;
        static Timer timer;
        static FpsOverlay overlay;
        static readonly List<int> samples = new List<int>();   // images par tranche de 250 ms, pour le processus suivi
        static readonly Dictionary<int, string> names = new Dictionary<int, string>();
        static int trackedPid;
        static IntPtr previewWnd;
        static DateTime previewUntil;
        static int previewTick;

        public static bool Running { get; private set; }
        public static event Action Changed;
        /// <summary>Programme actuellement mesuré et ses FPS (pour la page), ou null.</summary>
        public static string Current { get; private set; }
        public static int CurrentFps { get; private set; }

        static FpsConfig F { get { return cfg.Fps; } }

        public static void Start(AppConfig c)
        {
            cfg = c;
            Running = true;
            FpsCapture.Start();
            timer = new Timer { Interval = TickMs };
            timer.Tick += (s, e) => Tick();
            timer.Start();
            Raise();
        }

        public static void Stop()
        {
            Running = false;
            if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; }
            if (overlay != null) { overlay.Close(); overlay.Dispose(); overlay = null; }
            FpsCapture.Stop();
            samples.Clear();
            Current = null;
            Raise();
        }

        /// <summary>Redémarre la mesure (après un échec, par exemple).</summary>
        public static void Restart()
        {
            if (!Running) return;
            FpsCapture.Stop();
            FpsCapture.Start();
            Raise();
        }

        static void Raise()
        {
            var h = Changed;
            if (h != null) h();
        }

        /// <summary>Montre l'affichage quelques secondes sur une fenêtre (valeurs simulées), pour régler l'apparence.</summary>
        public static void Preview(IntPtr wnd)
        {
            previewWnd = wnd;
            previewUntil = DateTime.Now.AddSeconds(6);
            previewTick = 0;
            samples.Clear();
            trackedPid = -1;
        }

        #region Fenêtre au premier plan

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref Point p);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        static string NameOf(int pid)
        {
            string n;
            if (names.TryGetValue(pid, out n)) return n;
            try { using (var p = Process.GetProcessById(pid)) n = p.ProcessName.ToLowerInvariant(); }
            catch { n = ""; }
            if (names.Count > 500) names.Clear();
            names[pid] = n;
            return n;
        }

        static Rectangle ClientOnScreen(IntPtr h)
        {
            RECT r;
            if (!GetClientRect(h, out r)) return Rectangle.Empty;
            var p = new Point(0, 0);
            ClientToScreen(h, ref p);
            return new Rectangle(p.X, p.Y, r.Right - r.Left, r.Bottom - r.Top);
        }

        #endregion

        static void Tick()
        {
            var counts = FpsCapture.TakeCounts();
            IntPtr wnd;
            int pid, frames;
            bool preview = previewWnd != IntPtr.Zero && DateTime.Now < previewUntil;
            if (preview)
            {
                // valeurs simulées : environ 144 FPS avec des chutes régulières
                wnd = previewWnd;
                pid = -1;
                previewTick++;
                double f = 140 + 8 * Math.Sin(previewTick / 3.0) - (previewTick % 14 == 0 ? 90 : previewTick % 14 == 1 ? 50 : 0);
                frames = (int)Math.Round(f / PerSecond);
            }
            else
            {
                previewWnd = IntPtr.Zero;
                wnd = GetForegroundWindow();
                GetWindowThreadProcessId(wnd, out pid);
                bool watched;
                lock (AppConfig.Sync) watched = pid > 0 && F.Apps.Contains(NameOf(pid));
                if (!watched || IsIconic(wnd)) { Hide(); return; }
                // toutes les instances du programme : certains (navigateurs, Electron…) affichent depuis un processus annexe
                string name = NameOf(pid);
                frames = 0;
                foreach (var kv in counts) if (kv.Key == pid || NameOf(kv.Key) == name) frames += kv.Value;
            }
            if (pid != trackedPid) { samples.Clear(); trackedPid = pid; }
            samples.Add(frames);
            int keep;
            lock (AppConfig.Sync) keep = F.GraphSeconds * PerSecond;
            if (samples.Count > keep) samples.RemoveRange(0, samples.Count - keep);

            // FPS affichés : images de la dernière seconde
            int n = Math.Min(PerSecond, samples.Count), sum = 0;
            for (int i = samples.Count - n; i < samples.Count; i++) sum += samples[i];
            int fps = n == 0 ? 0 : sum * PerSecond / n;
            string cur = preview ? "aperçu" : NameOf(pid);
            if (cur != Current || fps != CurrentFps) { Current = cur; CurrentFps = fps; Raise(); }

            var area = ClientOnScreen(wnd);
            if (area.Width <= 0 || area.Height <= 0) { Hide(); return; }
            if (overlay == null) overlay = new FpsOverlay();
            FpsConfig look;
            lock (AppConfig.Sync) look = Copy(F);
            overlay.Render(fps, samples, keep, look, area);
        }

        static void Hide()
        {
            if (overlay != null && overlay.Visible) overlay.Hide();
            if (Current != null) { Current = null; CurrentFps = 0; Raise(); }
            trackedPid = 0;
            samples.Clear();
        }

        static FpsConfig Copy(FpsConfig f)
        {
            return new FpsConfig { Color = f.Color, Size = f.Size, Corner = f.Corner, OffsetX = f.OffsetX, OffsetY = f.OffsetY,
                Graph = f.Graph, GraphSeconds = f.GraphSeconds, Background = f.Background };
        }
    }

    /// <summary>Fenêtre transparente, toujours au premier plan et non cliquable (les clics vont au jeu).</summary>
    sealed class FpsOverlay : Form
    {
        public FpsOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // calque transparent aux clics, sans bouton dans la barre des tâches, jamais activé
                cp.ExStyle |= 0x80000 /* LAYERED */ | 0x20 /* TRANSPARENT */ | 0x80 /* TOOLWINDOW */ | 0x8000000 /* NOACTIVATE */ | 0x8 /* TOPMOST */;
                return cp;
            }
        }

        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref Point pos, ref Size size, IntPtr src, ref Point srcPos, int key, ref BLEND blend, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }

        public void Render(int fps, List<int> samples, int capacity, FpsConfig f, Rectangle area)
        {
            using (var bmp = Draw(fps, samples, capacity, f))
            {
                int x = f.Corner == "tr" || f.Corner == "br" ? area.Right - bmp.Width - f.OffsetX : area.Left + f.OffsetX;
                int y = f.Corner == "bl" || f.Corner == "br" ? area.Bottom - bmp.Height - f.OffsetY : area.Top + f.OffsetY;
                Push(bmp, new Point(x, y));
            }
            if (!Visible) Show();
            // reste au-dessus du jeu (certains jeux repassent devant)
            SetWindowPos(Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 /* NOSIZE | NOMOVE | NOACTIVATE */);
        }

        /// <summary>Image de l'affichage : nombre de FPS, et courbe en dessous. Largeur fixe (3 chiffres) pour ne pas bouger.</summary>
        public static Bitmap Draw(int fps, List<int> samples, int capacity, FpsConfig f)
        {
            float px = f.Size * 96f / 72f * Theme.S(100) / 100f;          // taille du nombre en pixels (suit l'échelle d'affichage)
            var color = Theme.FromHex(f.Color, Color.Lime);
            using (var big = new Font("Segoe UI Semibold", px, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI Semibold", Math.Max(7f, px * 0.42f), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var probe = new Bitmap(1, 1))
            using (var pg = Graphics.FromImage(probe))
            {
                var fmt = StringFormat.GenericTypographic;
                string num = fps.ToString();
                SizeF digits = pg.MeasureString(Math.Max(999, fps).ToString(), big, PointF.Empty, fmt);
                SizeF unit = pg.MeasureString("FPS", small, PointF.Empty, fmt);
                int pad = (int)Math.Max(4, px * 0.3f), gap = (int)(px * 0.15f);
                int textW = (int)Math.Ceiling(digits.Width + gap + unit.Width);
                int textH = (int)Math.Ceiling(px * 1.05f);
                int graphW = f.Graph ? Math.Max(textW, (int)(px * 5.5f)) : 0;
                int graphH = f.Graph ? (int)(px * 1.3f) : 0;
                int w = Math.Max(textW, graphW) + 2 * pad;
                int h = textH + (f.Graph ? graphH + pad : 0) + 2 * pad;

                var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    if (f.Background > 0)
                        using (var bg = new SolidBrush(Color.FromArgb(f.Background * 255 / 100, 10, 12, 16)))
                        using (var path = Round(new Rectangle(0, 0, w - 1, h - 1), Math.Max(3, pad / 2)))
                            g.FillPath(bg, path);
                    using (var br = new SolidBrush(color))
                    using (var shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                    {
                        // nombre aligné à droite sur 3 chiffres, « FPS » à côté ; ombre légère pour rester lisible sans fond
                        float nw = g.MeasureString(num, big, PointF.Empty, fmt).Width;
                        float nx = pad + digits.Width - nw, ny = pad - px * 0.12f;
                        g.DrawString(num, big, shadow, nx + 1, ny + 1, fmt);
                        g.DrawString(num, big, br, nx, ny, fmt);
                        float ux = pad + digits.Width + gap, uy = pad + textH - unit.Height - px * 0.05f;
                        g.DrawString("FPS", small, shadow, ux + 1, uy + 1, fmt);
                        g.DrawString("FPS", small, br, ux, uy, fmt);
                    }
                    if (f.Graph) DrawGraph(g, samples, capacity, new Rectangle(pad, pad + textH + pad, w - 2 * pad, graphH), color);
                }
                return bmp;
            }
        }

        static void DrawGraph(Graphics g, List<int> samples, int capacity, Rectangle r, Color color)
        {
            using (var line = new Pen(Color.FromArgb(70, color), 1))
                g.DrawLine(line, r.Left, r.Bottom, r.Right, r.Bottom);
            if (samples.Count < 2) return;
            // échelle : de 0 au maximum récent (+15 %), pour que les chutes se voient en creux
            int max = 1;
            foreach (int s in samples) max = Math.Max(max, s);
            float scale = (r.Height - 2) / (max * 1.15f), step = r.Width / (float)Math.Max(1, capacity - 1);
            var pts = new PointF[samples.Count];
            float x0 = r.Right - step * (samples.Count - 1);
            for (int i = 0; i < samples.Count; i++) pts[i] = new PointF(x0 + i * step, r.Bottom - 1 - samples[i] * scale);
            var fill = new List<PointF>(pts) { new PointF(pts[pts.Length - 1].X, r.Bottom), new PointF(pts[0].X, r.Bottom) };
            using (var b = new SolidBrush(Color.FromArgb(50, color))) g.FillPolygon(b, fill.ToArray());
            using (var p = new Pen(color, Math.Max(1.5f, r.Height / 22f)) { LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
        }

        static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Affiche l'image avec sa transparence (alpha par pixel).</summary>
        void Push(Bitmap bmp, Point pos)
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
            IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, hb);
            try
            {
                var size = bmp.Size;
                var src = Point.Empty;
                var blend = new BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 /* AC_SRC_ALPHA */ };
                UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
            }
            finally
            {
                SelectObject(mem, old);
                DeleteObject(hb);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
