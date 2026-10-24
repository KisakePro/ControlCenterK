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
        static readonly List<int> samples = new List<int>();
        static readonly List<float> cpuSamples = new List<float>(), gpuSamples = new List<float>(), ramSamples = new List<float>();
        static int sensorTick;   // images par tranche de 250 ms, pour le processus suivi
        static readonly Dictionary<int, string> names = new Dictionary<int, string>();
        static int trackedPid, secondFps;
        static long lastEnd;
        static IntPtr previewWnd;
        static DateTime previewUntil;
        static int previewTick;
        static string previewApp; // apparence montrée par l'aperçu (null : réglages par défaut)

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
        public static void Preview(IntPtr wnd, string app)
        {
            previewWnd = wnd;
            previewApp = app;
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

        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        static readonly int SelfPid = Process.GetCurrentProcess().Id;
        static IntPtr lastGame;

        /// <summary>Fenêtre d'un programme surveillé : le dernier jeu suivi s'il est encore ouvert, sinon le premier trouvé.</summary>
        static IntPtr GameWindow()
        {
            List<string> apps;
            lock (AppConfig.Sync) apps = new List<string>(F.Apps);
            if (lastGame != IntPtr.Zero && IsWindow(lastGame) && IsWindowVisible(lastGame) && !IsIconic(lastGame))
            {
                int p;
                GetWindowThreadProcessId(lastGame, out p);
                if (apps.Contains(NameOf(p))) return lastGame;
            }
            foreach (var app in apps)
                foreach (var proc in Process.GetProcessesByName(app))
                    using (proc)
                    {
                        IntPtr h;
                        try { h = proc.MainWindowHandle; } catch { continue; }
                        if (h != IntPtr.Zero && IsWindowVisible(h) && !IsIconic(h)) return lastGame = h;
                    }
            return IntPtr.Zero;
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
            // fenêtre de mesure décalée de 200 ms : les derniers événements ont le temps d'arriver
            long end = FpsCapture.Now - FpsCapture.Freq / 5;
            if (lastEnd == 0 || end - lastEnd > FpsCapture.Freq) lastEnd = end - FpsCapture.Freq / PerSecond;
            long from = lastEnd;
            lastEnd = end;
            IntPtr wnd;
            int pid, frames;
            bool behind = false;
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
                // ControlCenterK au premier plan (réglages en cours) : l'affichage reste sur le jeu, derrière
                if (pid == SelfPid)
                {
                    var game = GameWindow();
                    if (game != IntPtr.Zero) { wnd = game; GetWindowThreadProcessId(wnd, out pid); behind = true; }
                }
                bool watched;
                lock (AppConfig.Sync) watched = pid > 0 && F.Apps.Contains(NameOf(pid));
                if (!watched || IsIconic(wnd)) { Hide(); return; }
                if (!behind) lastGame = wnd;
                // toutes les instances du programme : certains (navigateurs, Electron…) affichent depuis un processus annexe
                string name = NameOf(pid);
                int fg = pid;
                Func<int, bool> match = p => p == fg || NameOf(p) == name;
                frames = FpsCapture.Count(match, from, end);
                // FPS affichés : images horodatées dans la dernière seconde (valeur exacte, sans effet de paquet)
                secondFps = FpsCapture.Count(match, end - FpsCapture.Freq, end);
            }
            if (pid != trackedPid) { samples.Clear(); cpuSamples.Clear(); gpuSamples.Clear(); ramSamples.Clear(); trackedPid = pid; }
            samples.Add(frames);
            // apparence : propre au programme si elle existe, sinon réglages par défaut
            FpsLook look;
            lock (AppConfig.Sync) look = F.LookFor(preview ? previewApp : NameOf(pid)).Clone();
            int keep = look.GraphSeconds * PerSecond;
            if (samples.Count > keep) samples.RemoveRange(0, samples.Count - keep);

            // processeur et carte graphique : relevés deux fois par seconde, seulement s'ils sont affichés
            if ((look.ShowCpu || look.ShowGpu || look.ShowRam) && sensorTick++ % 2 == 0) SystemSensors.Update();
            float cpu = SystemSensors.CpuUsage, gpu = SystemSensors.GpuUsage;
            if (preview) { cpu = 35 + 20 * (float)Math.Sin(previewTick / 5.0); gpu = 85 - (previewTick % 14 == 0 ? 40 : 0); }
            float ram = preview ? 48 : SystemSensors.RamUsage;
            cpuSamples.Add(cpu);
            ramSamples.Add(ram);
            if (ramSamples.Count > keep) ramSamples.RemoveRange(0, ramSamples.Count - keep);
            gpuSamples.Add(gpu);
            if (cpuSamples.Count > keep) cpuSamples.RemoveRange(0, cpuSamples.Count - keep);
            if (gpuSamples.Count > keep) gpuSamples.RemoveRange(0, gpuSamples.Count - keep);

            int fps;
            if (preview)
            {
                int n = Math.Min(PerSecond, samples.Count), sum = 0;
                for (int i = samples.Count - n; i < samples.Count; i++) sum += samples[i];
                fps = n == 0 ? 0 : sum * PerSecond / n;
            }
            else fps = secondFps;
            string cur = preview ? "aperçu" : NameOf(pid);
            if (cur != Current || fps != CurrentFps) { Current = cur; CurrentFps = fps; Raise(); }

            var area = ClientOnScreen(wnd);
            if (area.Width <= 0 || area.Height <= 0) { Hide(); return; }
            if (overlay == null) overlay = new FpsOverlay();
            var info = new OverlayInfo
            {
                Fps = fps, FpsSamples = samples, Capacity = keep,
                Cpu = cpu, CpuTemp = preview ? 62 : SystemSensors.CpuTemp, CpuSamples = cpuSamples,
                Gpu = gpu, GpuTemp = preview ? 71 : SystemSensors.GpuTemp, GpuSamples = gpuSamples,
                Ram = ram, RamUsedGb = preview ? 15.4f : SystemSensors.RamUsedGb, RamTotalGb = preview ? 32 : SystemSensors.RamTotalGb, RamSamples = ramSamples,
            };
            overlay.Render(info, look, area, behind ? wnd : IntPtr.Zero);
        }

        static void Hide()
        {
            if (overlay != null && overlay.Visible) overlay.Hide();
            if (Current != null) { Current = null; CurrentFps = 0; Raise(); }
            trackedPid = 0;
            samples.Clear();
            cpuSamples.Clear();
            gpuSamples.Clear();
            ramSamples.Clear();
        }

    }

    /// <summary>Valeurs affichées par l'overlay.</summary>
    sealed class OverlayInfo
    {
        public int Fps, Capacity;
        public List<int> FpsSamples;
        public float Cpu, CpuTemp, Gpu, GpuTemp;
        public float Ram, RamUsedGb, RamTotalGb;
        public List<float> CpuSamples, GpuSamples, RamSamples;
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

        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);

        /// <summary>
        /// Dessine l'affichage. "behind" : fenêtre du jeu quand il est en arrière-plan ; l'affichage est alors posé
        /// juste au-dessus de lui (sous ControlCenterK) au lieu d'être au premier plan.
        /// </summary>
        public void Render(OverlayInfo d, FpsLook f, Rectangle area, IntPtr behind)
        {
            using (var bmp = Draw(d, f))
            {
                int x = f.Corner == "tr" || f.Corner == "br" ? area.Right - bmp.Width - f.OffsetX : area.Left + f.OffsetX;
                int y = f.Corner == "bl" || f.Corner == "br" ? area.Bottom - bmp.Height - f.OffsetY : area.Top + f.OffsetY;
                Push(bmp, new Point(x, y));
            }
            if (!Visible) Show();
            // reste au-dessus du jeu (certains jeux repassent devant)
            const uint flags = 0x1 | 0x2 | 0x10; // NOSIZE | NOMOVE | NOACTIVATE
            if (behind == IntPtr.Zero) SetWindowPos(Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, flags);
            else
            {
                SetWindowPos(Handle, new IntPtr(-2) /* HWND_NOTOPMOST */, 0, 0, 0, 0, flags);
                // juste au-dessus du jeu : on se place sous la fenêtre qui le précède
                IntPtr above = GetWindow(behind, 3 /* GW_HWNDPREV */);
                if (above == Handle) return;
                SetWindowPos(Handle, above == IntPtr.Zero ? IntPtr.Zero /* HWND_TOP */ : above, 0, 0, 0, 0, flags);
            }
        }

        /// <summary>
        /// Image de l'affichage : bloc FPS (nombre + courbe), puis blocs processeur et carte graphique
        /// (utilisation, température, courbe de l'utilisation de 0 à 100 %). Largeur fixe pour ne pas bouger.
        /// </summary>
        public static Bitmap Draw(OverlayInfo d, FpsLook f)
        {
            float px = f.Size * 96f / 72f * Theme.S(100) / 100f;          // taille du nombre en pixels (suit l'échelle d'affichage)
            var color = Theme.FromHex(f.Color, Color.Lime);
            using (var big = new Font("Segoe UI Semibold", px, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI Semibold", Math.Max(7f, px * 0.42f), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var mid = new Font("Segoe UI Semibold", Math.Max(8f, px * 0.6f), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var probe = new Bitmap(1, 1))
            using (var pg = Graphics.FromImage(probe))
            {
                var fmt = StringFormat.GenericTypographic;
                SizeF digits = pg.MeasureString(Math.Max(999, d.Fps).ToString(), big, PointF.Empty, fmt);
                SizeF unit = pg.MeasureString("FPS", small, PointF.Empty, fmt);
                int pad = (int)Math.Max(4, px * 0.3f), gap = (int)(px * 0.15f);
                int textW = (int)Math.Ceiling(digits.Width + gap + unit.Width);
                int inner = Math.Max(textW, (int)(px * 5.5f));
                // lignes CPU / GPU / RAM : libellé + valeurs les plus longues possibles, sans chevauchement
                float label = pg.MeasureString("RAM", mid, PointF.Empty, fmt).Width + px * 0.5f;
                if (f.ShowCpu || f.ShowGpu) inner = Math.Max(inner, (int)Math.Ceiling(label + pg.MeasureString("100 %   100 °C", mid, PointF.Empty, fmt).Width));
                if (f.ShowRam) inner = Math.Max(inner, (int)Math.Ceiling(label + pg.MeasureString("100 %   " + d.RamUsedGb.ToString("0.0") + " / " + Math.Round(d.RamTotalGb) + " Go", mid, PointF.Empty, fmt).Width));
                int fpsH = (int)Math.Ceiling(px * 1.05f), lineH = (int)Math.Ceiling(px * 0.75f);
                int graphH = (int)(px * 1.3f), smallGraphH = (int)(px * 0.9f);

                // hauteur totale : blocs affichés, séparés par un petit espace
                int h = pad, blocks = 0;
                if (f.ShowFps) { h += fpsH + (f.Graph ? pad + graphH : 0); blocks++; }
                foreach (bool on in new[] { f.ShowCpu, f.ShowGpu, f.ShowRam })
                    if (on) { h += (blocks > 0 ? pad : 0) + lineH + (f.Graph ? pad / 2 + smallGraphH : 0); blocks++; }
                h += pad;
                int w = inner + 2 * pad;

                var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                using (var br = new SolidBrush(color))
                using (var dim = new SolidBrush(Color.FromArgb(170, color)))
                using (var shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    if (f.Background > 0)
                        using (var bg = new SolidBrush(Color.FromArgb(f.Background * 255 / 100, 10, 12, 16)))
                        using (var path = Round(new Rectangle(0, 0, w - 1, h - 1), Math.Max(3, pad / 2)))
                            g.FillPath(bg, path);
                    Action<string, Font, Brush, float, float> text = (t, font, b, x, y) =>
                    {
                        g.DrawString(t, font, shadow, x + 1, y + 1, fmt); // ombre légère pour rester lisible sans fond
                        g.DrawString(t, font, b, x, y, fmt);
                    };

                    int y0 = pad;
                    if (f.ShowFps)
                    {
                        // nombre aligné à droite sur 3 chiffres, « FPS » à côté
                        string num = d.Fps.ToString();
                        float nw = g.MeasureString(num, big, PointF.Empty, fmt).Width;
                        text(num, big, br, pad + digits.Width - nw, y0 - px * 0.12f);
                        text("FPS", small, br, pad + digits.Width + gap, y0 + fpsH - unit.Height - px * 0.05f);
                        y0 += fpsH;
                        if (f.Graph) { DrawGraph(g, d.FpsSamples, d.Capacity, 0, new Rectangle(pad, y0 + pad, inner, graphH), color); y0 += pad + graphH; }
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        if (!(k == 0 ? f.ShowCpu : k == 1 ? f.ShowGpu : f.ShowRam)) continue;
                        if (y0 > pad) y0 += pad;
                        float use = k == 0 ? d.Cpu : k == 1 ? d.Gpu : d.Ram, temp = k == 0 ? d.CpuTemp : d.GpuTemp;
                        // « CPU   45 %   62 °C » / « RAM   48 %   15,4 / 32 Go » : libellé à gauche, valeurs à droite
                        text(k == 0 ? "CPU" : k == 1 ? "GPU" : "RAM", mid, dim, pad, y0);
                        string vals = Math.Round(use) + " %   " + (k == 2 ? d.RamUsedGb.ToString("0.0") + " / " + Math.Round(d.RamTotalGb) + " Go"
                                    : float.IsNaN(temp) ? "— °C" : Math.Round(temp) + " °C");
                        float vw = g.MeasureString(vals, mid, PointF.Empty, fmt).Width;
                        text(vals, mid, br, pad + inner - vw, y0);
                        y0 += lineH;
                        if (f.Graph)
                        {
                            DrawGraph(g, k == 0 ? d.CpuSamples : k == 1 ? d.GpuSamples : d.RamSamples, d.Capacity, 100, new Rectangle(pad, y0 + pad / 2, inner, smallGraphH), color);
                            y0 += pad / 2 + smallGraphH;
                        }
                    }
                }
                return bmp;
            }
        }

        /// <summary>Courbe des dernières valeurs ; échelle fixe (fixedMax > 0) ou du maximum récent + 15 %.</summary>
        static void DrawGraph<T>(Graphics g, List<T> samples, int capacity, float fixedMax, Rectangle r, Color color) where T : IConvertible
        {
            using (var line = new Pen(Color.FromArgb(70, color), 1))
                g.DrawLine(line, r.Left, r.Bottom, r.Right, r.Bottom);
            if (samples.Count < 2) return;
            float max = fixedMax;
            if (max <= 0)
            {
                max = 1;
                foreach (var s in samples) max = Math.Max(max, s.ToSingle(null));
                max *= 1.15f; // les chutes se voient en creux
            }
            float scale = (r.Height - 2) / max, step = r.Width / (float)Math.Max(1, capacity - 1);
            var pts = new PointF[samples.Count];
            float x0 = r.Right - step * (samples.Count - 1);
            for (int i = 0; i < samples.Count; i++) pts[i] = new PointF(x0 + i * step, r.Bottom - 1 - Math.Min(max, samples[i].ToSingle(null)) * scale);
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
