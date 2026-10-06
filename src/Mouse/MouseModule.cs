using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>
    /// Module « Souris » : réglages de la souris Corsair (DPI, fréquence, éclairage) et réaffectation des boutons.
    /// Rien ne tourne quand le module est désactivé ; activé, il n'utilise qu'un thread de lecture endormi
    /// (mode avancé) et un crochet souris uniquement si un bouton standard est réaffecté.
    /// </summary>
    static class MouseModule
    {
        static AppConfig cfg;
        static volatile CorsairMouse dev;
        static readonly object gate = new object();
        static System.Threading.Timer effectTimer;
        static int sniperReturn = -1;
        static readonly HashSet<string> held = new HashSet<string>();

        public static bool Running { get; private set; }
        public static CorsairMouse Device { get { return dev; } }

        /// <summary>Souris connectée / déconnectée.</summary>
        public static event Action Changed;
        /// <summary>Un bouton a été pressé / relâché (identifiant, appui). Thread quelconque.</summary>
        public static event Action<string, bool> ButtonEvent;

        static MouseConfig M { get { return cfg.Mouse; } }

        public static void Start(AppConfig c)
        {
            cfg = c;
            Running = true;
            Connect();
            UpdateHook();
        }

        public static void Stop()
        {
            Running = false;
            RemoveHook();
            StopEffect();
            lock (gate)
            {
                if (dev != null)
                {
                    try { if (M.Advanced) dev.SetSoftwareMode(false); } catch { }
                    dev.Dispose();
                    dev = null;
                }
            }
            Raise();
        }

        static void Raise()
        {
            var h = Changed;
            if (h != null) h();
        }

        #region Connexion

        static void Connect()
        {
            lock (gate)
            {
                if (dev != null) return;
                dev = CorsairMouse.Find();
                if (dev == null) { Raise(); return; }
                dev.Button += OnCorsairButton;
                if (!M.Imported) ImportFromDevice(dev);
                ApplyDevice();
            }
            Raise();
        }

        /// <summary>Changement matériel signalé par Windows : souris branchée, débranchée, ou reconnectée (fréquence).</summary>
        public static void OnDeviceChange()
        {
            if (!Running) return;
            lock (gate)
            {
                if (dev != null && dev.ReadCurrentStage() < 0)
                {
                    dev.Dispose(); // la souris a disparu ou s'est reconnectée
                    dev = null;
                }
            }
            if (dev == null) Connect();
        }

        /// <summary>Premier branchement : reprend les réglages actuels de la souris (rien n'est modifié).</summary>
        static void ImportFromDevice(CorsairMouse d)
        {
            int mask = d.ReadStageMask(), cur = d.ReadCurrentStage();
            if (mask < 0 || cur < 0) return;
            lock (AppConfig.Sync)
            {
                for (int i = 0; i < CorsairMouse.StageCount; i++)
                {
                    int dpi;
                    Color led;
                    if (!d.ReadStage(i, out dpi, out led)) return;
                    var s = M.Stages[i];
                    s.Enabled = (mask & (1 << i)) != 0;
                    if (dpi >= 100) s.Dpi = dpi;
                    if (led.R + led.G + led.B > 0) s.Color = Theme.ToHex(led);
                }
                M.CurrentStage = Math.Max(1, Math.Min(CorsairMouse.StageCount - 1, cur));
                M.Imported = true;
            }
            cfg.Save();
        }

        /// <summary>Réapplique tous les réglages enregistrés à la souris.</summary>
        public static void ApplyDevice()
        {
            var d = dev;
            if (d == null) return;
            lock (AppConfig.Sync)
            {
                int mask = 0;
                for (int i = 0; i < CorsairMouse.StageCount; i++)
                {
                    var s = M.Stages[i];
                    if (s.Enabled) mask |= 1 << i;
                    d.SetStage(i, s.Dpi, Theme.FromHex(s.Color, Color.Black));
                }
                d.SetStageMask(mask);
                d.SetCurrentStage(M.CurrentStage);
            }
            if (M.Advanced)
            {
                d.SetSoftwareMode(true);
                d.StartButtons();
                ApplyLighting();
            }
            else
            {
                StopEffect();
                d.SetSoftwareMode(false);
            }
        }

        #endregion

        #region DPI et fréquence

        public static void SetPollRate(int hz)
        {
            lock (AppConfig.Sync) M.PollHz = hz;
            cfg.Save();
            var d = dev;
            if (d != null) d.SetPollRate(hz); // la souris se reconnecte : OnDeviceChange réappliquera le reste
        }

        public static void SetStage(int stage)
        {
            lock (AppConfig.Sync) M.CurrentStage = stage;
            var d = dev;
            if (d != null) d.SetCurrentStage(stage);
            if (M.Advanced) ApplyLighting();
        }

        static void CycleStage(int dir)
        {
            var d = dev;
            int cur;
            List<int> enabled = new List<int>();
            lock (AppConfig.Sync)
            {
                for (int i = 1; i < CorsairMouse.StageCount; i++) if (M.Stages[i].Enabled) enabled.Add(i);
                cur = M.CurrentStage;
            }
            if (enabled.Count == 0) return;
            int idx = enabled.IndexOf(cur);
            int next = enabled[((idx < 0 ? 0 : idx + dir) % enabled.Count + enabled.Count) % enabled.Count];
            SetStage(next);
            cfg.Save();
        }

        #endregion

        #region Éclairage

        public static void ApplyLighting()
        {
            var d = dev;
            if (d == null || !M.Advanced) return;
            string fx;
            lock (AppConfig.Sync) fx = M.Effect;
            if (fx == "breathe" || fx == "rainbow") StartEffect();
            else
            {
                StopEffect();
                d.SetZones(StaticColors(fx == "off" ? 0f : 1f));
            }
        }

        static Color[] StaticColors(float level)
        {
            var c = new Color[6];
            lock (AppConfig.Sync)
            {
                for (int z = 0; z < 6; z++) c[z] = Theme.FromHex(M.ZoneColors[z], Color.Black);
                // la zone 3 (indicateur DPI) prend la couleur de l'étape en cours
                c[2] = Theme.FromHex(M.Stages[Math.Max(0, Math.Min(CorsairMouse.StageCount - 1, M.CurrentStage))].Color, c[2]);
            }
            for (int z = 0; z < 6; z++) c[z] = Color.FromArgb((int)(c[z].R * level), (int)(c[z].G * level), (int)(c[z].B * level));
            return c;
        }

        /// <summary>Fait clignoter une zone en blanc pour l'identifier sur la souris.</summary>
        public static void IdentifyZone(int zone)
        {
            var d = dev;
            if (d == null || !M.Advanced) return;
            StopEffect();
            new Thread(() =>
            {
                for (int i = 0; i < 6; i++)
                {
                    var c = StaticColors(0.05f);
                    c[zone] = i % 2 == 0 ? Color.White : Color.Black;
                    d.SetZones(c);
                    Thread.Sleep(250);
                }
                ApplyLighting();
            }) { IsBackground = true }.Start();
        }

        static void StartEffect()
        {
            if (effectTimer != null) return;
            var t0 = Environment.TickCount;
            effectTimer = new System.Threading.Timer(_ =>
            {
                var d = dev;
                if (d == null) return;
                string fx;
                int speed;
                lock (AppConfig.Sync) { fx = M.Effect; speed = M.EffectSpeed; }
                double t = (Environment.TickCount - t0) / 1000.0 * (0.2 + speed * 0.12);
                Color[] c;
                if (fx == "rainbow")
                {
                    c = new Color[6];
                    for (int z = 0; z < 6; z++) c[z] = Theme.Hsl((t * 90 + z * 40) % 360, 1, 0.5);
                }
                else c = StaticColors((float)(0.08 + 0.92 * (0.5 + 0.5 * Math.Sin(t * Math.PI))));
                d.SetZones(c);
            }, null, 0, 50);
        }

        static void StopEffect()
        {
            if (effectTimer == null) return;
            effectTimer.Dispose();
            effectTimer = null;
        }

        #endregion

        #region Boutons

        static void OnCorsairButton(int bit, bool down)
        {
            string id = "cor:" + bit;
            var h = ButtonEvent;
            if (h != null) h(id, down);
            MouseAction a;
            lock (AppConfig.Sync) M.Buttons.TryGetValue(id, out a);
            if (a != null) Execute(id, a, down);
        }

        /// <summary>Exécute l'action d'un bouton (appui / relâchement).</summary>
        public static void Execute(string id, MouseAction a, bool down)
        {
            string kind = a.Kind ?? "", val = a.Value ?? "";
            switch (kind)
            {
                case "keys":
                    {
                        var keys = InputSim.ParseCombo(val);
                        if (keys == null) return;
                        // maintenu tant que le bouton est maintenu (utile en jeu, push-to-talk…)
                        lock (held)
                        {
                            if (down && held.Add(id)) foreach (var k in keys) InputSim.Key(k, true);
                            else if (!down && held.Remove(id)) for (int i = keys.Count - 1; i >= 0; i--) InputSim.Key(keys[i], false);
                        }
                        break;
                    }
                case "macro": if (down) InputSim.RunMacro(val); break;
                case "click": { int b; if (int.TryParse(val, out b)) InputSim.MouseButton(b, down); break; }
                case "dpi_next": if (down) CycleStage(1); break;
                case "dpi_prev": if (down) CycleStage(-1); break;
                case "dpi_stage": { int s; if (down && int.TryParse(val, out s)) { SetStage(s); cfg.Save(); } break; }
                case "sniper":
                    if (down) { lock (AppConfig.Sync) sniperReturn = M.CurrentStage; SetStage(0); }
                    else if (sniperReturn >= 0) { SetStage(sniperReturn); sniperReturn = -1; }
                    break;
                case "media_play": if (down) Native.PressKey(0xB3); break;
                case "media_next": if (down) Native.PressKey(0xB0); break;
                case "media_prev": if (down) Native.PressKey(0xB1); break;
                case "media_mute": if (down) Native.PressKey(0xAD); break;
                case "vol_up": if (down) Native.PressKey(0xAF); break;
                case "vol_down": if (down) Native.PressKey(0xAE); break;
            }
        }

        // --- Crochet souris pour réaffecter le bouton du milieu, précédent et suivant ---
        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [StructLayout(LayoutKind.Sequential)] struct MSLL { public int x, y; public int mouseData; public uint flags, time; public IntPtr extra; }

        static IntPtr hook;
        static HookProc hookProc; // gardé en champ pour le GC

        /// <summary>Installe le crochet seulement si un bouton standard est réaffecté (sinon : aucun coût).</summary>
        public static void UpdateHook()
        {
            bool need;
            lock (AppConfig.Sync)
            {
                need = false;
                foreach (var kv in M.Buttons)
                    if (kv.Key.StartsWith("hid:") && !string.IsNullOrEmpty(kv.Value.Kind)) need = true;
            }
            if (need && Running && hook == IntPtr.Zero)
            {
                hookProc = HookCallback;
                hook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, hookProc, GetModuleHandle(null), 0);
            }
            else if ((!need || !Running) && hook != IntPtr.Zero) RemoveHook();
        }

        static void RemoveHook()
        {
            if (hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        static IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var info = (MSLL)Marshal.PtrToStructure(lParam, typeof(MSLL));
                int msg = wParam.ToInt32();
                string id = null;
                bool down = false;
                if (info.extra != InputSim.Tag)
                {
                    if (msg == 0x207 || msg == 0x208) { id = "hid:2"; down = msg == 0x207; }                          // milieu
                    else if (msg == 0x20B || msg == 0x20C) { id = (info.mouseData >> 16) == 1 ? "hid:3" : "hid:4"; down = msg == 0x20B; } // précédent / suivant
                }
                if (id != null)
                {
                    MouseAction a;
                    lock (AppConfig.Sync) M.Buttons.TryGetValue(id, out a);
                    if (a != null && !string.IsNullOrEmpty(a.Kind))
                    {
                        string cid = id;
                        bool cdown = down;
                        ThreadPool.QueueUserWorkItem(_ => Execute(cid, a, cdown));
                        return new IntPtr(1); // le clic d'origine est remplacé
                    }
                }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        #endregion
    }
}
