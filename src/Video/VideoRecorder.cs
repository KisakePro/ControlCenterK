using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ControlCenterK
{
    public class VideoConfig
    {
        public string Format { get; set; }       // mp4, wmv, avi
        public int Fps { get; set; }             // 10, 15, 24, 25, 30, 50, 60
        public string Size { get; set; }         // source, 2160, 1440, 1080, 720, 480 : hauteur maximale de la vidéo
        public string Audio { get; set; }        // none, system, mic, both
        public string Quality { get; set; }      // low, medium, high
        public bool Cursor { get; set; }         // inclure le curseur
        public bool HotKey { get; set; }         // Ctrl+Alt+F11 : choisir une zone et enregistrer / arrêter
        public string Folder { get; set; }       // "" = Vidéos\ControlCenterK
        public List<string> Recent { get; set; } // dernières vidéos

        public VideoConfig()
        {
            Format = "mp4";
            Fps = 30;
            Size = "source";
            Audio = "system";
            Quality = "medium";
            Cursor = true;
            HotKey = true;
            Folder = "";
            Recent = new List<string>();
        }

        public void Fix()
        {
            if (Format != "wmv" && Format != "avi") Format = "mp4";
            if (Array.IndexOf(new[] { 10, 15, 24, 25, 30, 50, 60 }, Fps) < 0) Fps = 30;
            if (Array.IndexOf(new[] { "2160", "1440", "1080", "720", "480" }, Size) < 0) Size = "source";
            if (Audio != "none" && Audio != "mic" && Audio != "both") Audio = "system";
            if (Quality != "low" && Quality != "high") Quality = "medium";
            if (Folder == null) Folder = "";
            if (Recent == null) Recent = new List<string>();
        }

        public string FolderPath
        {
            get
            {
                return string.IsNullOrEmpty(Folder)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ControlCenterK")
                    : Folder;
            }
        }
    }

    /// <summary>
    /// Module « Capture vidéo » : enregistre une zone de l'écran (ou l'écran sous la souris) en MP4, WMV ou AVI.
    /// Un thread copie l'écran au rythme choisi, un autre encode : si l'encodeur prend du retard, des images sont
    /// sautées plutôt que de ralentir la capture. Le cadre et le panneau d'arrêt sont exclus de l'enregistrement.
    /// </summary>
    static class VideoRecorder
    {
        static AppConfig cfg;
        static VideoHotKey hotKey;
        static volatile bool stopping;
        static Thread capture, encoder;
        static BorderFrame frame;
        static RecordPanel panel;
        static string outPath;

        public static bool Recording { get; private set; }
        public static Rectangle Zone { get; private set; }
        public static DateTime Started { get; private set; }
        public static string Error { get; private set; }
        public static string Warning { get; private set; }   // son manquant, etc. : la vidéo est quand même enregistrée
        public static event Action Changed;

        static VideoConfig C { get { return cfg.Video; } }

        public static void Enable(AppConfig c)
        {
            cfg = c;
            UpdateHotKey();
        }

        public static void Disable()
        {
            Stop();
            if (hotKey != null) { hotKey.Dispose(); hotKey = null; }
        }

        public static void UpdateHotKey()
        {
            bool want;
            lock (AppConfig.Sync) want = cfg != null && C.HotKey;
            if (want && hotKey == null) hotKey = new VideoHotKey(() => { if (Recording) Stop(); else PickAndStart(); });
            else if (!want && hotKey != null) { hotKey.Dispose(); hotKey = null; }
        }

        static void Raise() { var h = Changed; if (h != null) h(); }

        public static void PickAndStart()
        {
            if (Recording) return;
            ZoneSelector.Pick(r => { if (r.HasValue && r.Value.Width >= 16 && r.Value.Height >= 16) Start(r.Value); });
        }

        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point p, int flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public int Flags; }

        /// <summary>Écran sous la souris, en pixels physiques.</summary>
        public static void StartScreen()
        {
            var prev = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                Point p;
                GetCursorPos(out p);
                var mi = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (!GetMonitorInfo(MonitorFromPoint(p, 2), ref mi)) return;
                Start(Rectangle.FromLTRB(mi.Monitor.L, mi.Monitor.T, mi.Monitor.R, mi.Monitor.B));
            }
            finally { SetThreadDpiAwarenessContext(prev); }
        }

        public static void Start(Rectangle zone)
        {
            if (Recording) return;
            string fmt, dir;
            int fps;
            string q, size, audio;
            bool cursor;
            lock (AppConfig.Sync) { fmt = C.Format; fps = C.Fps; q = C.Quality; cursor = C.Cursor; dir = C.FolderPath; size = C.Size; audio = C.Audio; }
            try
            {
                Directory.CreateDirectory(dir);
                string name = DateTime.Now.ToString("'Vidéo' yyyy-MM-dd HH.mm.ss");
                outPath = Path.Combine(dir, name + "." + fmt);
                for (int i = 2; File.Exists(outPath); i++) outPath = Path.Combine(dir, name + " (" + i + ")." + fmt);
            }
            catch (Exception e) { Error = "Dossier inaccessible : " + e.Message; Raise(); return; }

            // taille de sortie : la zone est mise à l'échelle pour tenir dans le cadre 16:9 choisi (agrandie ou réduite),
            // proportions gardées ; « d'origine » = taille de la zone, au plus 3840 × 2160. Dimensions paires (H.264).
            int maxH = size == "source" ? 2160 : int.Parse(size), maxW = maxH * 16 / 9;
            double k = Math.Min(maxW / (double)zone.Width, maxH / (double)zone.Height);
            if (size == "source") k = Math.Min(1, k);
            int w = Math.Max(16, (int)(zone.Width * k) & ~1), h = Math.Max(16, (int)(zone.Height * k) & ~1);
            Error = null;
            Warning = null;
            Zone = zone;
            Started = DateTime.Now;
            Recording = true;
            stopping = false;
            var queue = new FrameQueue(w * h * 4);
            string path = outPath;
            // une seule horloge pour l'image et le son : ils restent synchronisés
            var clock = Stopwatch.StartNew();
            var mixer = audio == "none" ? null : new AudioMixer(audio != "mic", audio != "system", clock);
            encoder = new Thread(() => Encode(queue, path, fmt, w, h, fps, q, mixer)) { IsBackground = true, Name = "Encodage vidéo" };
            capture = new Thread(() => Capture(queue, zone, w, h, fps, cursor, clock)) { IsBackground = true, Name = "Capture vidéo" };
            encoder.Start();
            capture.Start();
            frame = new BorderFrame(zone, Color.FromArgb(230, 40, 40), 3);
            panel = new RecordPanel(zone);
            panel.Show();
            Raise();
        }

        public static void Stop()
        {
            if (!Recording) return;
            stopping = true;
            if (frame != null) { frame.Close(); frame.Dispose(); frame = null; }
            if (panel != null) { panel.Close(); panel = null; }
            var c = capture;
            var en = encoder;
            Recording = false;
            Raise();
            // la finalisation du fichier peut prendre un moment : hors du thread de l'interface
            new Thread(() =>
            {
                if (c != null) c.Join();
                if (en != null) en.Join();
                Finished();
            }) { IsBackground = true }.Start();
        }

        static void Finished()
        {
            string path = outPath;
            Action ui = () =>
            {
                if (Error == null && path != null && File.Exists(path))
                {
                    lock (AppConfig.Sync)
                    {
                        C.Recent.Remove(path);
                        C.Recent.Insert(0, path);
                        if (C.Recent.Count > 12) C.Recent.RemoveRange(12, C.Recent.Count - 12);
                    }
                    cfg.Save();
                    new CaptureToast("Vidéo enregistrée : " + Path.GetFileName(path) + (Warning != null ? " (" + Warning + ")" : ""), path).Show();
                }
                Raise();
            };
            var f = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            try { if (f != null) f.BeginInvoke(ui); else ui(); } catch { }
        }

        static void Fail(string message)
        {
            Error = message;
            StopSoon();
        }

        /// <summary>Arrêt demandé depuis un thread de travail.</summary>
        static void StopSoon()
        {
            stopping = true;
            var f = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            Action a = () => Stop();
            try { if (f != null) f.BeginInvoke(a); } catch { }
        }

        #region Capture

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr old);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO ii);
        [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; }
        [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int Size, Flags; public IntPtr Cursor; public Point Pos; }
        [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool IsIcon; public int HotX, HotY; public IntPtr Mask, Color; }

        static void Capture(FrameQueue queue, Rectangle r, int w, int h, int fps, bool cursor, Stopwatch clock)
        {
            SetThreadDpiAwarenessContext(new IntPtr(-4)); // coordonnées physiques sur tous les écrans
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bits;
            var bi = new BITMAPINFOHEADER { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
            IntPtr dib = CreateDIBSection(mem, ref bi, 0, out bits, IntPtr.Zero, 0), old = SelectObject(mem, dib);
            bool same = w == r.Width && h == r.Height;
            double s = (double)w / r.Width;
            SetStretchBltMode(mem, 4 /* HALFTONE */);
            SetBrushOrgEx(mem, 0, 0, IntPtr.Zero);
            long n = clock.ElapsedTicks * fps / Stopwatch.Frequency, tick = Stopwatch.Frequency / fps;
            try
            {
                while (!stopping)
                {
                    long t = clock.ElapsedTicks;
                    // 0x40000000 = CAPTUREBLT : inclut les fenêtres superposées (menus, infobulles)
                    if (same) BitBlt(mem, 0, 0, w, h, screen, r.X, r.Y, 0x00CC0020 | 0x40000000);
                    else StretchBlt(mem, 0, 0, w, h, screen, r.X, r.Y, r.Width, r.Height, 0x00CC0020 | 0x40000000);
                    if (cursor) DrawCursor(mem, r, s);
                    var buf = queue.Rent();
                    if (buf != null)
                    {
                        Marshal.Copy(bits, buf, 0, buf.Length);
                        queue.Push(buf, t * 10000000L / Stopwatch.Frequency);
                    }
                    n++;
                    long wait = (n * tick - clock.ElapsedTicks) * 1000 / Stopwatch.Frequency;
                    if (wait > 0) Thread.Sleep((int)wait);
                    else if (wait < -1000) n = clock.ElapsedTicks / tick; // gros retard : on repart de maintenant
                }
            }
            catch (Exception e) { Fail("Erreur de capture : " + e.Message); }
            finally
            {
                queue.Complete();
                SelectObject(mem, old);
                DeleteObject(dib);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        static void DrawCursor(IntPtr dc, Rectangle r, double s)
        {
            var ci = new CURSORINFO { Size = Marshal.SizeOf(typeof(CURSORINFO)) };
            if (!GetCursorInfo(ref ci) || (ci.Flags & 1) == 0) return;
            ICONINFO ii;
            if (!GetIconInfo(ci.Cursor, out ii)) return;
            int x = (int)((ci.Pos.X - r.X - ii.HotX) * s), y = (int)((ci.Pos.Y - r.Y - ii.HotY) * s);
            DrawIconEx(dc, x, y, ci.Cursor, (int)(GetSystemMetrics(13) * s), (int)(GetSystemMetrics(14) * s), 0, IntPtr.Zero, 3 /* DI_NORMAL */);
            if (ii.Mask != IntPtr.Zero) DeleteObject(ii.Mask);
            if (ii.Color != IntPtr.Zero) DeleteObject(ii.Color);
        }

        #endregion

        const int AudioDelay = AudioSource.Rate * 3 / 10; // le son est écrit avec 300 ms de retard : les paquets ont le temps d'arriver

        static void Encode(FrameQueue queue, string path, string fmt, int w, int h, int fps, string q, AudioMixer mixer)
        {
            IVideoSink sink = null;
            long audioDone = 0, lastTime = 0;
            try
            {
                // débit : environ 0,05 / 0,1 / 0,2 bit par pixel et par image ; qualité JPEG pour l'AVI
                double bpp = q == "low" ? 0.05 : q == "high" ? 0.2 : 0.1;
                int bitrate = (int)Math.Min(100000000, Math.Max(1000000, w * (long)h * fps * bpp));
                if (fmt == "avi") sink = new AviWriter(path, w, h, fps, q == "low" ? 60 : q == "high" ? 92 : 80, mixer != null);
                else
                {
                    var mf = new MfWriter(path, w, h, fps, bitrate, fmt == "wmv", mixer != null);
                    if (mf.AudioError != null) Warning = mf.AudioError;
                    sink = mf;
                }
                var avi = sink as AviWriter;
                byte[] buf;
                long time;
                while (queue.Take(out buf, out time))
                {
                    sink.Write(buf, time);
                    queue.Return(buf);
                    lastTime = time;
                    if (mixer != null) WriteAudio(sink, mixer, time * AudioSource.Rate / 10000000L - AudioDelay, ref audioDone);
                    if (avi != null && avi.Length > AviWriter.MaxBytes && !stopping) StopSoon(); // limite de 4 Go du format AVI : le fichier est conservé
                }
            }
            catch (Exception e)
            {
                Fail("Erreur d'encodage (" + fmt.ToUpperInvariant() + ") : " + e.Message);
                byte[] rest;
                long t;
                while (queue.Take(out rest, out t)) { }
            }
            finally
            {
                if (mixer != null)
                {
                    // fin : le son jusqu'à la dernière image, puis on coupe les captures
                    try { if (sink != null) WriteAudio(sink, mixer, (lastTime + 10000000L / fps) * AudioSource.Rate / 10000000L, ref audioDone); } catch { }
                    if (mixer.Error != null && Warning == null) Warning = mixer.Error;
                    mixer.Dispose();
                }
                if (sink != null) try { sink.Dispose(); } catch (Exception e) { if (Error == null) Error = "Fichier mal finalisé : " + e.Message; }
            }
        }

        /// <summary>Écrit le son jusqu'à la trame "target", par paquets de 100 ms au plus.</summary>
        static void WriteAudio(IVideoSink sink, AudioMixer mixer, long target, ref long done)
        {
            while (target - done > 0)
            {
                int n = (int)Math.Min(AudioSource.Rate / 10, target - done);
                sink.WriteAudio(mixer.Pull(n), n, done * 10000000L / AudioSource.Rate);
                done += n;
            }
        }

        /// <summary>File bornée entre capture et encodage ; tampons réutilisés pour ne pas saturer la mémoire.</summary>
        sealed class FrameQueue
        {
            const int Capacity = 8;
            readonly int size;
            readonly Queue<KeyValuePair<byte[], long>> full = new Queue<KeyValuePair<byte[], long>>();
            readonly Stack<byte[]> free = new Stack<byte[]>();
            int allocated;
            bool done;

            public FrameQueue(int size) { this.size = size; }

            /// <summary>Tampon libre, ou null si l'encodeur est en retard (l'image est alors sautée).</summary>
            public byte[] Rent()
            {
                lock (this)
                {
                    if (free.Count > 0) return free.Pop();
                    if (allocated >= Capacity) return null;
                    allocated++;
                    return new byte[size];
                }
            }

            public void Return(byte[] b) { lock (this) free.Push(b); }

            public void Push(byte[] b, long time) { lock (this) { full.Enqueue(new KeyValuePair<byte[], long>(b, time)); Monitor.Pulse(this); } }

            public void Complete() { lock (this) { done = true; Monitor.PulseAll(this); } }

            public bool Take(out byte[] b, out long time)
            {
                lock (this)
                {
                    while (full.Count == 0 && !done) Monitor.Wait(this);
                    if (full.Count == 0) { b = null; time = 0; return false; }
                    var e = full.Dequeue();
                    b = e.Key; time = e.Value;
                    return true;
                }
            }
        }
    }

    /// <summary>Petit panneau « ● 00:12  Arrêter » près de la zone, exclu de l'enregistrement.</summary>
    sealed class RecordPanel : Form
    {
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, int affinity);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

        readonly Label time;
        readonly System.Windows.Forms.Timer timer;
        readonly Rectangle zone;

        public RecordPanel(Rectangle zone)
        {
            this.zone = zone;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Card;
            time = Theme.Label("●  00:00", Theme.Semi(10f), Color.FromArgb(240, 70, 70), Theme.Card);
            time.Location = new Point(Theme.S(12), Theme.S(12));
            var stop = new FlatButton("Arrêter", true) { Glyph = "" };
            stop.FitWidth();
            stop.Location = new Point(Theme.S(96), Theme.S(6));
            stop.Click += (s, e) => VideoRecorder.Stop();
            Controls.AddRange(new Control[] { time, stop });
            ClientSize = new Size(stop.Right + Theme.S(8), stop.Bottom + Theme.S(6));
            timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (s, e) =>
            {
                var d = DateTime.Now - VideoRecorder.Started;
                time.Text = "●  " + ((int)d.TotalMinutes).ToString("00") + ":" + d.Seconds.ToString("00");
            };
            timer.Start();
            FormClosed += (s, e) => timer.Dispose();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetWindowDisplayAffinity(Handle, 0x11); // WDA_EXCLUDEFROMCAPTURE : invisible dans la vidéo
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // sous la zone si possible, sinon au-dessus, sinon dans son coin bas droit (coordonnées physiques)
            var prev = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                var wa = Screen.FromRectangle(zone).Bounds;
                int x = Math.Max(wa.Left, Math.Min(zone.Right - Width, wa.Right - Width));
                int y = zone.Bottom + 8 + Height <= wa.Bottom ? zone.Bottom + 8 : zone.Top - 8 - Height >= wa.Top ? zone.Top - 8 - Height : zone.Bottom - Height - 8;
                Location = new Point(x, y);
            }
            finally { SetThreadDpiAwarenessContext(prev); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Color.FromArgb(230, 40, 40))) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Raccourci global Ctrl+Alt+F11 (fenêtre de messages invisible).</summary>
    sealed class VideoHotKey : NativeWindow, IDisposable
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        const int Id = 0x4C00;
        readonly Action pressed;

        public VideoHotKey(Action pressed)
        {
            this.pressed = pressed;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) /* HWND_MESSAGE */ });
            RegisterHotKey(Handle, Id, 0x1 | 0x2 | 0x4000 /* ALT | CTRL | NOREPEAT */, 0x7A /* F11 */);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x312 && m.WParam.ToInt32() == Id) pressed();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterHotKey(Handle, Id);
            DestroyHandle();
        }
    }
}
