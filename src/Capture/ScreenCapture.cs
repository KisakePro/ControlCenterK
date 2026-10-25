using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Réglages du module « Capture d'écran ».</summary>
    public class CaptureConfig
    {
        public bool HotKey { get; set; }        // intercepter Impr. écran
        public string After { get; set; }       // menu, copy, save, copysave, editor : action après la sélection
        public string Folder { get; set; }      // dossier des captures ("" = Images\Captures ControlCenterK)
        public string Pattern { get; set; }     // nom des fichiers (date/heure .NET), sans extension
        public string Format { get; set; }      // png, jpg
        public bool Cursor { get; set; }        // inclure le curseur
        public List<string> Recent { get; set; } // dernières captures enregistrées

        public CaptureConfig()
        {
            HotKey = true;
            After = "menu";
            Folder = "";
            Pattern = "'Capture' yyyy-MM-dd HH.mm.ss";
            Format = "png";
            Recent = new List<string>();
        }

        public void Fix()
        {
            if (string.IsNullOrEmpty(After)) After = "menu";
            if (Folder == null) Folder = "";
            if (string.IsNullOrEmpty(Pattern)) Pattern = "'Capture' yyyy-MM-dd HH.mm.ss";
            if (Format != "jpg") Format = "png";
            if (Recent == null) Recent = new List<string>();
        }

        public string FolderPath
        {
            get
            {
                return string.IsNullOrEmpty(Folder)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Captures ControlCenterK")
                    : Folder;
            }
        }
    }

    /// <summary>
    /// Module « Capture d'écran » (façon ShareX) : Impr. écran fige l'écran, on choisit une zone, une fenêtre ou
    /// l'écran entier, puis un menu propose de copier, enregistrer ou retoucher l'image dans l'éditeur.
    /// La touche est interceptée par un crochet clavier : elle fonctionne même si un autre outil l'a réservée.
    /// </summary>
    static class ScreenCapture
    {
        static AppConfig cfg;
        static Control ui;               // pour revenir sur le thread de l'interface
        static Thread hookThread;
        static uint hookThreadId;
        static volatile bool busy;

        public static event Action Changed;
        static CaptureConfig C { get { return cfg.Capture; } }

        public static void Enable(AppConfig c)
        {
            cfg = c;
            if (ui == null) { ui = new Control(); ui.CreateControl(); var h = ui.Handle; }
            UpdateHook();
        }

        public static void Disable()
        {
            StopHook();
        }

        public static void UpdateHook()
        {
            bool on;
            lock (AppConfig.Sync) on = cfg != null && C.HotKey;
            if (on && hookThread == null) StartHook();
            else if (!on) StopHook();
        }

        #region Crochet clavier (Impr. écran)

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct KBDLL { public int vk, scan, flags, time; public IntPtr extra; }
        static HookProc proc; // gardé en champ pour le ramasse-miettes

        static void StartHook()
        {
            var ready = new ManualResetEvent(false);
            hookThread = new Thread(() =>
            {
                hookThreadId = GetCurrentThreadId();
                proc = Hook;
                IntPtr h = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, proc, GetModuleHandle(null), 0);
                ready.Set();
                MSG m;
                while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }
                if (h != IntPtr.Zero) UnhookWindowsHookEx(h);
            }) { IsBackground = true, Name = "Touche Impr. écran" };
            hookThread.Start();
            ready.WaitOne(2000);
        }

        static void StopHook()
        {
            var t = hookThread;
            if (t == null) return;
            hookThread = null;
            PostThreadMessage(hookThreadId, 0x12 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
            t.Join(1000);
        }

        static IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var k = (KBDLL)Marshal.PtrToStructure(lParam, typeof(KBDLL));
                if (k.vk == 0x2C /* VK_SNAPSHOT */ && k.extra != InputSim.Tag)
                {
                    int msg = wParam.ToInt32();
                    if (msg == 0x100 || msg == 0x104) // appui (Alt+Impr arrive en WM_SYSKEYDOWN)
                    {
                        bool ctrl = GetAsyncKeyState(0x11) < 0, alt = GetAsyncKeyState(0x12) < 0;
                        string mode = ctrl ? "screen" : alt ? "window" : "region";
                        try { ui.BeginInvoke(new Action(() => Start(mode))); } catch { }
                    }
                    return new IntPtr(1); // la touche n'est pas transmise (ni à Windows ni aux autres outils)
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        #endregion

        #region Capture

        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>Bureau virtuel complet en pixels physiques.</summary>
        public static Rectangle Desktop
        {
            get { return new Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)); }
        }

        /// <summary>Lance une capture : "region" (sélection), "screen" (écran sous la souris), "window" (fenêtre active).</summary>
        public static void Start(string mode)
        {
            if (busy || cfg == null) return;
            busy = true;
            var prev = SetThreadDpiAwarenessContext(new IntPtr(-4)); // pixels physiques sur tous les écrans
            try
            {
                var desk = Desktop;
                bool cursor;
                lock (AppConfig.Sync) cursor = C.Cursor;
                var frozen = Grab(desk, cursor);
                if (mode == "screen")
                {
                    var scr = Screen.FromPoint(Cursor.Position).Bounds;
                    Done(Crop(frozen, desk, scr), scr);
                    frozen.Dispose();
                    return;
                }
                if (mode == "window")
                {
                    RECT r;
                    var w = GetForegroundWindow();
                    if (DwmGetWindowAttribute(w, 9 /* EXTENDED_FRAME_BOUNDS */, out r, 16) == 0)
                    {
                        var rect = Rectangle.Intersect(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), desk);
                        if (rect.Width > 0 && rect.Height > 0) { Done(Crop(frozen, desk, rect), rect); frozen.Dispose(); return; }
                    }
                }
                CaptureOverlay.Show(frozen, desk, (img, rect) => { if (img != null) Done(img, rect); else busy = false; });
                return;
            }
            catch { busy = false; }
            finally { SetThreadDpiAwarenessContext(prev); }
        }

        /// <summary>Image de la sélection : action choisie dans les réglages, ou menu.</summary>
        static void Done(Bitmap img, Rectangle rect)
        {
            string after;
            lock (AppConfig.Sync) after = C.After;
            if (after == "menu") CaptureMenu.Show(img, rect, () => busy = false);
            else { Act(after, img); busy = false; }
        }

        /// <summary>Exécute une action sur la capture (l'image est libérée ensuite, sauf pour l'éditeur).</summary>
        public static void Act(string action, Bitmap img)
        {
            switch (action)
            {
                case "copy": Copy(img); img.Dispose(); break;
                case "save": Save(img, null); img.Dispose(); break;
                case "copysave": Copy(img); Save(img, null); img.Dispose(); break;
                case "saveas": SaveAs(img); img.Dispose(); break;
                case "editor": new ImageEditor(img).Show(); break;
                default: img.Dispose(); break;
            }
        }

        public static void Copy(Image img)
        {
            try { Clipboard.SetImage(img); } catch { }
            Toast("Capture copiée dans le presse-papiers.");
        }

        /// <summary>Enregistre dans le dossier des captures (ou à l'emplacement donné) ; renvoie le chemin.</summary>
        public static string Save(Image img, string path)
        {
            try
            {
                string fmt;
                lock (AppConfig.Sync)
                {
                    fmt = C.Format;
                    if (path == null)
                    {
                        string dir = C.FolderPath;
                        Directory.CreateDirectory(dir);
                        string name;
                        try { name = DateTime.Now.ToString(C.Pattern); } catch { name = DateTime.Now.ToString("'Capture' yyyy-MM-dd HH.mm.ss"); }
                        foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '-');
                        path = Path.Combine(dir, name + "." + fmt);
                        for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, name + " (" + i + ")." + fmt);
                    }
                }
                bool jpg = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
                img.Save(path, jpg ? ImageFormat.Jpeg : ImageFormat.Png);
                lock (AppConfig.Sync)
                {
                    C.Recent.Remove(path);
                    C.Recent.Insert(0, path);
                    if (C.Recent.Count > 12) C.Recent.RemoveRange(12, C.Recent.Count - 12);
                }
                cfg.Save();
                Toast("Capture enregistrée : " + Path.GetFileName(path), path);
                var h = Changed;
                if (h != null) h();
                return path;
            }
            catch (Exception e)
            {
                MessageBox.Show("Impossible d'enregistrer la capture :\n" + e.Message, "Capture d'écran", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }

        public static string SaveAs(Image img)
        {
            using (var dlg = new SaveFileDialog { Title = "Enregistrer la capture", Filter = "Image PNG (*.png)|*.png|Image JPEG (*.jpg)|*.jpg" })
            {
                lock (AppConfig.Sync)
                {
                    dlg.InitialDirectory = C.FolderPath;
                    try { dlg.FileName = DateTime.Now.ToString(C.Pattern); } catch { }
                    if (C.Format == "jpg") dlg.FilterIndex = 2;
                }
                return dlg.ShowDialog() == DialogResult.OK ? Save(img, dlg.FileName) : null;
            }
        }

        /// <summary>Petite notification en bas à droite de l'écran (clic : ouvrir l'image enregistrée).</summary>
        static void Toast(string text, string path = null)
        {
            try { new CaptureToast(text, path).Show(); } catch { }
        }

        #endregion

        #region Images

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO ii);
        [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int Size, Flags; public IntPtr Cursor; public Point Pos; }
        [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool IsIcon; public int HotX, HotY; public IntPtr Mask, Color; }

        /// <summary>Copie de l'écran (pixels physiques), curseur compris si demandé.</summary>
        static Bitmap Grab(Rectangle r, bool cursor)
        {
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dst = g.GetHdc(), src = GetDC(IntPtr.Zero);
                BitBlt(dst, 0, 0, r.Width, r.Height, src, r.X, r.Y, 0x00CC0020 | 0x40000000 /* SRCCOPY | CAPTUREBLT */);
                if (cursor)
                {
                    var ci = new CURSORINFO { Size = Marshal.SizeOf(typeof(CURSORINFO)) };
                    ICONINFO ii;
                    if (GetCursorInfo(ref ci) && (ci.Flags & 1) != 0 && GetIconInfo(ci.Cursor, out ii))
                    {
                        DrawIconEx(dst, ci.Pos.X - r.X - ii.HotX, ci.Pos.Y - r.Y - ii.HotY, ci.Cursor, 0, 0, 0, IntPtr.Zero, 3);
                        if (ii.Mask != IntPtr.Zero) DeleteObject(ii.Mask);
                        if (ii.Color != IntPtr.Zero) DeleteObject(ii.Color);
                    }
                }
                ReleaseDC(IntPtr.Zero, src);
                g.ReleaseHdc(dst);
            }
            return bmp;
        }

        /// <summary>Découpe une zone (coordonnées écran) dans l'image du bureau.</summary>
        public static Bitmap Crop(Bitmap desk, Rectangle deskRect, Rectangle r)
        {
            var b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
                g.DrawImage(desk, new Rectangle(0, 0, r.Width, r.Height), new Rectangle(r.X - deskRect.X, r.Y - deskRect.Y, r.Width, r.Height), GraphicsUnit.Pixel);
            return b;
        }

        #endregion
    }
}
