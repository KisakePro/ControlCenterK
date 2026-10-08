using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace ControlCenterK
{
    /// <summary>
    /// Module « Clavier » : détection des claviers branchés, éclairage (par touche ou global, avec animations)
    /// et macros sur les touches. La détection ne s'exécute qu'au branchement d'un périphérique, l'animation
    /// ne tourne que pour un effet animé, et le crochet clavier n'est installé que s'il sert (macro, effet réactif, détection).
    /// </summary>
    static class KeyboardModule
    {
        static AppConfig cfg;
        static volatile RgbKeyboard dev;
        static KeyboardDeviceConfig dc;
        static List<DetectedKeyboard> detected = new List<DetectedKeyboard>();
        static readonly object gate = new object();
        static System.Threading.Timer anim, rescan;
        static volatile bool scanning;
        static bool probed;
        static readonly Dictionary<int, int> pressedAt = new Dictionary<int, int>(); // effet réactif : code HID → instant d'appui
        static readonly HashSet<int> swallowed = new HashSet<int>();                // touches dont on absorbe l'appui en cours

        public static bool Running { get; private set; }
        public static RgbKeyboard Device { get { return dev; } }
        public static KeyboardDeviceConfig DeviceConfig { get { return dc; } }
        public static List<DetectedKeyboard> Detected { get { lock (gate) return new List<DetectedKeyboard>(detected); } }
        public static bool Scanning { get { return scanning; } }

        public static event Action Changed;
        /// <summary>Touche pressée (identifiant "key:xx"), pour la mettre en évidence dans la page. Thread du crochet.</summary>
        public static event Action<string> KeyPressed;

        static KeyboardConfig K { get { return cfg.Keyboard; } }

        public static void Start(AppConfig c)
        {
            cfg = c;
            Running = true;
            UpdateHook();
            Rescan(0);
        }

        public static void Stop()
        {
            Running = false;
            RemoveHook();
            StopAnim();
            if (rescan != null) { rescan.Dispose(); rescan = null; }
            lock (gate) { Disconnect(); probed = false; detected = new List<DetectedKeyboard>(); }
            Raise();
        }

        static void Raise()
        {
            var h = Changed;
            if (h != null) h();
        }

        #region Détection

        static void Rescan(int delay)
        {
            if (!Running) return;
            if (rescan == null) rescan = new System.Threading.Timer(_ => Scan(), null, Timeout.Infinite, Timeout.Infinite);
            rescan.Change(delay, Timeout.Infinite);
        }

        public static void OnDeviceChange()
        {
            if (Running) Rescan(900);
        }

        static void Scan()
        {
            if (!Running) return;
            lock (gate)
            {
                scanning = true;
                try
                {
                    var all = Hid.Enumerate(null);
                    var boards = KeyboardDetect.List(all);
                    bool same = boards.Count == detected.Count && boards.TrueForAll(b => detected.Exists(d => d.Key == b.Key));
                    if (same && probed && (dev == null || dev.Alive())) return;
                    Disconnect();
                    string preferred = K.Selected;
                    var found = KeyboardDetect.Probe(all, boards, preferred);
                    var pick = found.Find(d => d.Key == preferred) ?? (found.Count > 0 ? found[0] : null);
                    foreach (var d in found) if (d != pick) d.Dispose();
                    detected = boards;
                    probed = true;
                    if (pick != null)
                    {
                        dev = pick;
                        lock (AppConfig.Sync) dc = K.Device(pick.Key, pick.Name);
                        ApplyLighting();
                    }
                }
                catch { }
                finally { scanning = false; }
            }
            Raise();
        }

        static void Disconnect()
        {
            var d = dev;
            if (d == null) return;
            StopAnim();
            try { d.Release(); } catch { }
            d.Dispose();
            dev = null;
            dc = null;
        }

        public static void Select(string key)
        {
            lock (AppConfig.Sync) K.Selected = key;
            cfg.Save();
            lock (gate) { Disconnect(); probed = false; }
            Rescan(0);
        }

        #endregion

        #region Éclairage

        /// <summary>Applique l'effet enregistré (à appeler après chaque changement de réglage).</summary>
        public static void ApplyLighting()
        {
            var d = dev;
            var c = dc;
            if (d == null || c == null) return;
            string fx;
            lock (AppConfig.Sync) fx = c.Effect;
            if (fx == "device")
            {
                StopAnim();
                d.Release();
            }
            else if (fx == "static" || fx == "off" || fx == "gradient")
            {
                StopAnim();
                Frame(d, c, 0);
            }
            else StartAnim();
            UpdateHook();
        }

        static void StartAnim()
        {
            if (anim != null) return;
            var d = dev;
            int t0 = Environment.TickCount;
            int period = d != null && d.PerKey ? 50 : 100;
            anim = new System.Threading.Timer(_ =>
            {
                var dv = dev;
                var c = dc;
                if (dv != null && c != null) Frame(dv, c, (Environment.TickCount - t0) / 1000.0);
            }, null, 0, period);
        }

        static void StopAnim()
        {
            if (anim == null) return;
            anim.Dispose();
            anim = null;
        }

        static Color Scale(Color c, double f)
        {
            f = Math.Max(0, Math.Min(1, f));
            return Color.FromArgb((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
        }

        /// <summary>Couleur à la position "p" d'un dégradé passant par les couleurs (cyclique : revient à la première).</summary>
        static Color Gradient(List<Color> cols, double p, bool cyclic)
        {
            if (cols.Count == 0) return Color.Black;
            if (cols.Count == 1) return cols[0];
            int n = cyclic ? cols.Count : cols.Count - 1;
            p = cyclic ? (p % 1 + 1) % 1 : Math.Max(0, Math.Min(1, p));
            double f = p * n;
            int i = Math.Min(n - 1, (int)f);
            return Theme.Mix(cols[i], cols[(i + 1) % cols.Count], (float)(f - i));
        }

        /// <summary>Calcule et envoie une image de l'effet à l'instant t (secondes).</summary>
        static void Frame(RgbKeyboard d, KeyboardDeviceConfig c, double t)
        {
            string fx;
            Color main;
            double speed, bright;
            var custom = new Dictionary<int, Color>();
            var ec = new List<Color>();
            lock (AppConfig.Sync)
            {
                fx = c.Effect;
                main = Theme.FromHex(c.Color, Color.White);
                speed = 0.2 + c.Speed * 0.12;
                bright = c.Brightness / 100.0;
                foreach (var kv in c.Keys)
                {
                    int u;
                    if (int.TryParse(kv.Key, System.Globalization.NumberStyles.HexNumber, null, out u)) custom[u] = Theme.FromHex(kv.Value, main);
                }
                foreach (var h in c.EffectColors) ec.Add(Theme.FromHex(h, main));
            }
            if (ec.Count == 0) ec.Add(main);
            double ts = t * speed;
            Func<int, Color> keyColor = u => { Color x; return custom.TryGetValue(u, out x) ? x : main; };
            // respiration : une couleur de l'effet par souffle
            double breathe = 0.08 + 0.92 * (0.5 + 0.5 * Math.Sin(ts * Math.PI - Math.PI / 2));
            Color breathColor = ec[(int)(ts / 2) % ec.Count];
            Color cycle = Gradient(ec, ts * 0.25, true);
            int now = Environment.TickCount;

            if (!d.PerKey)
            {
                Color one;
                switch (fx)
                {
                    case "off": one = Color.Black; break;
                    case "breathe": one = Scale(breathColor, breathe); break;
                    case "cycle":
                    case "wave": one = cycle; break;
                    case "gradient": one = ec[0]; break;
                    case "rainbow": one = Theme.Hsl(ts * 60 % 360, 1, 0.5); break;
                    default: one = main; break;
                }
                d.SetAll(Scale(one, bright));
                return;
            }

            var frame = new Dictionary<int, Color>();
            foreach (int u in d.Keys)
            {
                Color col;
                var k = KeyLayout.Get(u);
                float x = k != null ? (k.X + k.W / 2) / KeyLayout.Width : 0;
                switch (fx)
                {
                    case "off": col = Color.Black; break;
                    case "breathe": col = Scale(breathColor, breathe); break;
                    case "cycle": col = cycle; break;
                    case "gradient": col = Gradient(ec, x, false); break;
                    case "rainbow": col = Theme.Hsl(ts * 60 % 360, 1, 0.5); break;
                    case "wave": col = Gradient(ec, ts * 0.35 - x, true); break;
                    case "reactive":
                        {
                            double flash = 0;
                            int at;
                            lock (pressedAt) if (pressedAt.TryGetValue(u, out at)) flash = 1 - (now - at) / (700.0 / speed * 1.4);
                            // fond : couleur de la touche atténuée ; appui : première couleur de l'effet qui s'estompe
                            col = Theme.Mix(Scale(keyColor(u), 0.12), ec[0], (float)Math.Max(0, Math.Min(1, flash)));
                            break;
                        }
                    default: col = keyColor(u); break;
                }
                frame[u] = Scale(col, bright);
            }
            d.SetKeys(frame);
        }

        #endregion

        #region Macros et crochet clavier

        static volatile Action<string> detect;
        static int detectedUsage = -1;

        /// <summary>La prochaine touche pressée est envoyée à "found" (sans être tapée).</summary>
        public static void BeginDetect(Action<string> found)
        {
            detect = found;
            UpdateHook();
        }

        public static void CancelDetect()
        {
            detect = null;
            UpdateHook();
        }

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [StructLayout(LayoutKind.Sequential)] struct KBDLL { public int vk, scan, flags, time; public IntPtr extra; }

        static volatile IntPtr hook;
        static HookProc hookProc; // gardé en champ pour le GC

        /// <summary>Installe le crochet seulement s'il sert : macro définie, effet réactif ou détection en cours.</summary>
        public static void UpdateHook()
        {
            bool need = detect != null || KeyPressed != null;
            if (cfg != null)
                lock (AppConfig.Sync)
                {
                    foreach (var kv in K.Macros) if (!string.IsNullOrEmpty(kv.Value.Kind)) need = true;
                    var c = dc;
                    if (c != null && dev != null && c.Effect == "reactive") need = true;
                }
            if (need && Running && hook == IntPtr.Zero && hookThread == null)
            {
                // le crochet bas niveau a besoin d'une boucle de messages : il vit dans son propre thread,
                // quel que soit le thread appelant (interface, minuterie de détection…)
                var ready = new ManualResetEvent(false);
                hookThread = new Thread(() =>
                {
                    hookThreadId = GetCurrentThreadId();
                    hookProc = HookCallback;
                    hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, hookProc, GetModuleHandle(null), 0);
                    ready.Set();
                    MSG m;
                    while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }
                    if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
                    hook = IntPtr.Zero;
                }) { IsBackground = true, Name = "Crochet clavier" };
                hookThread.Start();
                ready.WaitOne(2000);
            }
            else if ((!need || !Running) && hookThread != null) RemoveHook();
        }

        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        static Thread hookThread;
        static volatile uint hookThreadId;

        static void RemoveHook()
        {
            var t = hookThread;
            if (t == null) return;
            hookThread = null;
            PostThreadMessage(hookThreadId, 0x12 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
            if (t != Thread.CurrentThread) t.Join(1000);
        }

        static IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var info = (KBDLL)Marshal.PtrToStructure(lParam, typeof(KBDLL));
                int msg = wParam.ToInt32();
                bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                var key = info.extra == InputSim.Tag ? null : KeyLayout.FromEvent(info.vk, info.scan, (info.flags & 1) != 0);
                if (key != null && (down || up))
                {
                    int u = key.Usage;
                    string id = key.Id;
                    if (down)
                    {
                        lock (pressedAt) pressedAt[u] = Environment.TickCount;
                        var kp = KeyPressed;
                        if (kp != null) kp(id);
                    }
                    // détection : la touche est prise, ni tapée ni exécutée
                    var d = detect;
                    if (down && d != null)
                    {
                        detect = null;
                        detectedUsage = u;
                        ThreadPool.QueueUserWorkItem(_ => d(id));
                        return new IntPtr(1);
                    }
                    if (u == detectedUsage)
                    {
                        if (up) detectedUsage = -1;
                        return new IntPtr(1);
                    }
                    // macro : l'appui d'origine est remplacé par l'action (répétitions automatiques ignorées)
                    MouseAction a = null;
                    if (cfg != null) lock (AppConfig.Sync) K.Macros.TryGetValue(id, out a);
                    if (a != null && !string.IsNullOrEmpty(a.Kind))
                    {
                        bool first;
                        lock (swallowed) first = down ? swallowed.Add(u) : swallowed.Remove(u);
                        if (first)
                        {
                            bool cd = down;
                            ThreadPool.QueueUserWorkItem(_ => InputActions.Run(id, a, cd));
                        }
                        return new IntPtr(1);
                    }
                }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        #endregion
    }
}
