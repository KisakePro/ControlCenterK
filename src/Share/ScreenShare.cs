using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ControlCenterK
{
    /// <summary>Réglages du module « Partage d'écran » (KStS).</summary>
    public class ShareConfig
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public bool Border { get; set; }    // cadre rouge autour de la zone partagée

        public ShareConfig() { Border = true; }
    }

    /// <summary>
    /// Module « Partage d'écran » (ancien utilitaire KStS) : envoie en direct une zone de l'écran dans la caméra
    /// virtuelle « KStS Virtual Screen », pour la partager sur Discord (ou toute appli de visio) comme une webcam.
    /// La caméra est le module caméra virtuelle d'OBS (GPL), embarqué et enregistré sous ce nom.
    /// </summary>
    static class ScreenShare
    {
        public const int Fps = 30, OutW = 1920, OutH = 1080;
        public const string CamName = "KStS Virtual Screen";
        const string CamClsid = "{A3FCE0F5-3493-419F-958A-ABA1250EC20B}";
        const string VideoInput = "{860BB310-5D01-11d0-BD3B-00A0C911CE86}";
        static readonly string DriverDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KStS");
        static readonly string ObsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"obs-studio\data\obs-plugins\win-dshow");

        static Thread worker;
        static volatile bool stopping;
        static BorderFrame border;

        public static Rectangle Region { get; private set; }
        public static bool Streaming { get { return worker != null; } }
        /// <summary>Dernière erreur (null si aucune).</summary>
        public static string Error { get; private set; }
        public static event Action Changed;

        static void Raise()
        {
            var h = Changed;
            if (h != null) h();
        }

        #region Caméra virtuelle

        /// <summary>Vrai si le module caméra d'OBS est enregistré sous le nom « KStS Virtual Screen ».</summary>
        public static bool CameraReady()
        {
            try
            {
                using (var k = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + VideoInput + @"\Instance\" + CamClsid))
                    return k != null && (k.GetValue("FriendlyName") as string) == CamName;
            }
            catch { return false; }
        }

        /// <summary>Installe la caméra (une demande administrateur) : copie, enregistrement 64 et 32 bits, renommage.</summary>
        public static bool InstallCamera()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "ControlCenterK-cam");
            Directory.CreateDirectory(tmp);
            foreach (var bits in new[] { "64", "32" })
                using (var res = typeof(ScreenShare).Assembly.GetManifestResourceStream("obs-virtualcam-module" + bits + ".dll"))
                using (var f = File.Create(Path.Combine(tmp, "obs-virtualcam-module" + bits + ".dll")))
                    res.CopyTo(f);
            var lines = new System.Collections.Generic.List<string> { "New-Item -ItemType Directory -Force '" + DriverDir + "' | Out-Null" };
            foreach (var bits in new[] { "64", "32" })
            {
                string dll = Path.Combine(DriverDir, "obs-virtualcam-module" + bits + ".dll");
                lines.Add("Copy-Item -Force '" + Path.Combine(tmp, "obs-virtualcam-module" + bits + ".dll") + "' '" + dll + "'");
                lines.Add("Start-Process -Wait '" + RegSvr(bits) + "' -ArgumentList '/s', '\"" + dll + "\"'");
            }
            lines.AddRange(SetName(CamName));
            bool ok = RunElevated(lines);
            try { Directory.Delete(tmp, true); } catch { }
            return ok && CameraReady();
        }

        /// <summary>Retire la caméra ; si OBS est installé, lui rend sa caméra d'origine.</summary>
        public static bool UninstallCamera()
        {
            Stop();
            var lines = new System.Collections.Generic.List<string>();
            bool obs = Directory.Exists(ObsDir);
            foreach (var bits in new[] { "64", "32" })
            {
                string theirs = Path.Combine(ObsDir, "obs-virtualcam-module" + bits + ".dll");
                string ours = Path.Combine(DriverDir, "obs-virtualcam-module" + bits + ".dll");
                lines.Add(File.Exists(theirs)
                    ? "Start-Process -Wait '" + RegSvr(bits) + "' -ArgumentList '/s', '\"" + theirs + "\"'"
                    : "Start-Process -Wait '" + RegSvr(bits) + "' -ArgumentList '/s', '/u', '\"" + ours + "\"'");
            }
            if (obs) lines.AddRange(SetName("OBS Virtual Camera"));
            lines.Add("Remove-Item -Recurse -Force '" + DriverDir + "'");
            return RunElevated(lines) && !CameraReady();
        }

        static string RegSvr(string bits)
        {
            return Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), bits == "64" ? "System32" : "SysWOW64", "regsvr32.exe");
        }

        static string[] SetName(string name)
        {
            return new[]
            {
                @"Set-ItemProperty 'HKLM:\SOFTWARE\Classes\CLSID\" + VideoInput + @"\Instance\" + CamClsid + "' -Name FriendlyName -Value '" + name + "'",
                @"Set-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Classes\CLSID\" + VideoInput + @"\Instance\" + CamClsid + "' -Name FriendlyName -Value '" + name + "'",
            };
        }

        /// <summary>Exécute un script PowerShell en administrateur (une seule confirmation de Windows) et attend la fin.</summary>
        static bool RunElevated(System.Collections.Generic.List<string> lines)
        {
            string script = Path.Combine(Path.GetTempPath(), "ControlCenterK-cam.ps1");
            lines.Insert(0, "$ErrorActionPreference = 'SilentlyContinue'");
            File.WriteAllText(script, string.Join("\r\n", lines), new System.Text.UTF8Encoding(true));
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"")
                    { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }))
                    p.WaitForExit();
                return true;
            }
            catch (System.ComponentModel.Win32Exception) { return false; } // refusé
            finally { try { File.Delete(script); } catch { } }
        }

        #endregion

        #region Partage

        /// <summary>Démarre le partage de la zone (coordonnées écran en pixels physiques).</summary>
        public static void Start(Rectangle region, bool showBorder)
        {
            Stop();
            Error = null;
            Region = region;
            stopping = false;
            var t = new Thread(() => Run(region)) { IsBackground = true, Name = "Partage d'écran" };
            worker = t;
            t.Start();
            ShowBorder(showBorder);
            Raise();
        }

        public static void Stop()
        {
            var t = worker;
            if (t != null)
            {
                stopping = true;
                t.Join(3000);
                worker = null;
            }
            ShowBorder(false);
            Raise();
        }

        public static void ShowBorder(bool show)
        {
            if (border != null) { border.Close(); border.Dispose(); border = null; }
            if (show && worker != null) border = new BorderFrame(Region);
        }

        static void Fail(string message)
        {
            Error = message;
            worker = null;
            var f = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            Action a = () => { ShowBorder(false); Raise(); };
            try { if (f != null) f.BeginInvoke(a); else Raise(); } catch { }
        }

        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
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

        /// <summary>Boucle de capture : zone mise à l'échelle et centrée en 1920 × 1080 (bandes noires), 30 images/s.</summary>
        static unsafe void Run(Rectangle r)
        {
            SetThreadDpiAwarenessContext(new IntPtr(-4)); // coordonnées physiques sur tous les écrans
            string err;
            var cam = VirtualCam.Open(OutW, OutH, Fps, out err);
            if (cam == null) { Fail(err); return; }
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bits;
            var bi = new BITMAPINFOHEADER { Size = 40, Width = OutW, Height = -OutH, Planes = 1, BitCount = 32 };
            IntPtr dib = CreateDIBSection(mem, ref bi, 0, out bits, IntPtr.Zero, 0), old = SelectObject(mem, dib);
            double s = Math.Min(OutW / (double)r.Width, OutH / (double)r.Height);
            int sw = (int)(r.Width * s), sh = (int)(r.Height * s), ox = (OutW - sw) / 2, oy = (OutH - sh) / 2;
            bool same = sw == r.Width && sh == r.Height;
            SetStretchBltMode(mem, 4 /* HALFTONE */);
            SetBrushOrgEx(mem, 0, 0, IntPtr.Zero);
            int cursorW = (int)(GetSystemMetrics(13) * s), cursorH = (int)(GetSystemMetrics(14) * s);
            var clock = Stopwatch.StartNew();
            long frame = 0, tick = Stopwatch.Frequency / Fps;
            try
            {
                while (!stopping)
                {
                    if (same) BitBlt(mem, ox, oy, sw, sh, screen, r.X, r.Y, 0x00CC0020);
                    else StretchBlt(mem, ox, oy, sw, sh, screen, r.X, r.Y, r.Width, r.Height, 0x00CC0020);
                    DrawCursor(mem, r, s, ox, oy, cursorW, cursorH);
                    cam.Write((byte*)bits, clock.ElapsedTicks * 1000000000L / Stopwatch.Frequency);
                    frame++;
                    long wait = (frame * tick - clock.ElapsedTicks) * 1000 / Stopwatch.Frequency;
                    if (wait > 0) Thread.Sleep((int)wait);
                    else if (wait < -1000) frame = clock.ElapsedTicks / tick; // retard important : on repart de maintenant
                }
            }
            catch (Exception e) { Fail("Erreur de capture : " + e.Message); }
            finally
            {
                cam.Dispose();
                SelectObject(mem, old);
                DeleteObject(dib);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        static void DrawCursor(IntPtr dc, Rectangle r, double s, int ox, int oy, int w, int h)
        {
            var ci = new CURSORINFO { Size = Marshal.SizeOf(typeof(CURSORINFO)) };
            if (!GetCursorInfo(ref ci) || (ci.Flags & 1) == 0) return;
            ICONINFO ii;
            if (!GetIconInfo(ci.Cursor, out ii)) return;
            int x = ox + (int)((ci.Pos.X - r.X - ii.HotX) * s), y = oy + (int)((ci.Pos.Y - r.Y - ii.HotY) * s);
            DrawIconEx(dc, x, y, ci.Cursor, w, h, 0, IntPtr.Zero, 3 /* DI_NORMAL */);
            if (ii.Mask != IntPtr.Zero) DeleteObject(ii.Mask);
            if (ii.Color != IntPtr.Zero) DeleteObject(ii.Color);
        }

        #endregion
    }

    /// <summary>Cadre rouge autour de la zone partagée, dessiné à l'extérieur (donc jamais capturé), transparent aux clics.</summary>
    sealed class BorderFrame : IDisposable
    {
        readonly Form[] sides = new Form[4];

        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

        public BorderFrame(Rectangle r)
        {
            const int b = 2;
            var rects = new[]
            {
                new Rectangle(r.X - b, r.Y - b, r.Width + 2 * b, b), new Rectangle(r.X - b, r.Bottom, r.Width + 2 * b, b),
                new Rectangle(r.X - b, r.Y, b, r.Height), new Rectangle(r.Right, r.Y, b, r.Height),
            };
            var prev = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var f = new Side { BackColor = Color.Red, FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false,
                        StartPosition = FormStartPosition.Manual, TopMost = true, Bounds = rects[i] };
                    f.Show();
                    f.Bounds = rects[i];
                    sides[i] = f;
                }
            }
            finally { SetThreadDpiAwarenessContext(prev); }
        }

        sealed class Side : Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000 | 0x8; return cp; } // calque, clics traversants, sans barre des tâches
            }
            protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SetLayeredWindowAttributes(Handle, 0, 255, 2); }
            [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, int key, byte alpha, int flags);
        }

        public void Close() { foreach (var f in sides) if (f != null) f.Close(); }
        public void Dispose() { foreach (var f in sides) if (f != null) f.Dispose(); }
    }
}
