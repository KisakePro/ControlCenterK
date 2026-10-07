using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>
    /// Module « Souris » : détection automatique des souris branchées, réglages des souris prises en charge
    /// (DPI, fréquence, éclairage : Corsair, Logitech, Razer, SteelSeries) et réaffectation des boutons.
    /// Rien ne tourne quand le module est désactivé ; activé, la détection ne s'exécute qu'au branchement
    /// d'un périphérique, et un crochet souris n'est installé que si un bouton standard est réaffecté.
    /// </summary>
    static class MouseModule
    {
        static AppConfig cfg;
        static volatile GamingMouse dev;
        static MouseDeviceConfig dc;
        static List<DetectedMouse> detected = new List<DetectedMouse>();
        static readonly object gate = new object();
        static System.Threading.Timer effectTimer, rescan;
        static int sniperReturn = -1;
        static volatile bool scanning;
        static bool probed;                  // les souris de "detected" ont déjà été essayées
        static readonly HashSet<string> held = new HashSet<string>();

        public static bool Running { get; private set; }
        /// <summary>Souris réglable pilotée en ce moment (null si aucune).</summary>
        public static GamingMouse Device { get { return dev; } }
        /// <summary>Réglages enregistrés de la souris pilotée.</summary>
        public static MouseDeviceConfig DeviceConfig { get { return dc; } }
        /// <summary>Toutes les souris branchées (réglables ou non).</summary>
        public static List<DetectedMouse> Detected { get { lock (gate) return new List<DetectedMouse>(detected); } }
        /// <summary>Détection en cours (premier passage ou branchement).</summary>
        public static bool Scanning { get { return scanning; } }

        /// <summary>Souris détectée / débranchée / changée.</summary>
        public static event Action Changed;
        /// <summary>Un bouton a été pressé / relâché (identifiant, appui). Thread quelconque.</summary>
        public static event Action<string, bool> ButtonEvent;

        static MouseConfig M { get { return cfg.Mouse; } }

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
            StopEffect();
            if (rescan != null) { rescan.Dispose(); rescan = null; }
            lock (gate) { Disconnect(); probed = false; detected = new List<DetectedMouse>(); }
            Raise();
        }

        static void Raise()
        {
            var h = Changed;
            if (h != null) h();
        }

        #region Détection et connexion

        /// <summary>Lance une détection en arrière-plan après "delay" ms (les notifications de branchement arrivent en rafale).</summary>
        static void Rescan(int delay)
        {
            if (!Running) return;
            if (rescan == null) rescan = new System.Threading.Timer(_ => Scan(), null, Timeout.Infinite, Timeout.Infinite);
            rescan.Change(delay, Timeout.Infinite);
        }

        /// <summary>Changement matériel signalé par Windows : souris branchée, débranchée, ou reconnectée (fréquence).</summary>
        public static void OnDeviceChange()
        {
            if (Running) Rescan(800);
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
                    var mice = MouseDetect.List(all);
                    bool sameMice = mice.Count == detected.Count && mice.TrueForAll(m => detected.Exists(d => d.Key == m.Key));
                    // mêmes souris qu'au dernier passage et souris pilotée toujours là : rien à faire
                    if (sameMice && probed && (dev == null || dev.Alive())) return;
                    Disconnect();
                    string preferred = M.Selected;
                    var found = MouseDetect.Probe(all, mice, preferred);
                    GamingMouse pick = found.Find(d => d.Key == preferred) ?? (found.Count > 0 ? found[0] : null);
                    foreach (var d in found) if (d != pick) d.Dispose();
                    detected = mice;
                    probed = true;
                    if (pick != null) Connect(pick);
                }
                catch { }
                finally { scanning = false; }
            }
            Raise();
        }

        static void Connect(GamingMouse d)
        {
            dev = d;
            lock (AppConfig.Sync)
            {
                dc = M.Device(d.Key, d.Name, d.Zones);
                if (!d.HasAdvancedMode && dc.Advanced) dc.Advanced = false;
                if (dc.Effect == "device" && d.HasAdvancedMode) dc.Effect = "static";
            }
            d.Button += OnDeviceButton;
            if (!dc.Imported) ImportFromDevice(d);
            ApplyDevice();
        }

        static void Disconnect()
        {
            var d = dev;
            if (d == null) return;
            StopEffect();
            try { if (d.HasAdvancedMode && dc != null && dc.Advanced) d.SetAdvanced(false); } catch { }
            d.Button -= OnDeviceButton;
            d.Dispose();
            dev = null;
            dc = null;
        }

        /// <summary>Choisit la souris à piloter quand plusieurs souris réglables sont branchées.</summary>
        public static void Select(string key)
        {
            lock (AppConfig.Sync) M.Selected = key;
            cfg.Save();
            lock (gate) { Disconnect(); probed = false; }
            Rescan(0);
        }

        /// <summary>Premier branchement : reprend les réglages actuels de la souris (rien n'est modifié).</summary>
        static void ImportFromDevice(GamingMouse d)
        {
            int cur;
            lock (AppConfig.Sync)
            {
                if (d.ReadStages(dc.Stages, out cur)) dc.CurrentStage = Math.Max(1, Math.Min(CorsairMouse.StageCount - 1, cur));
                else
                {
                    int dpi = d.ReadDpi();
                    if (dpi > 0) { dc.Stages[1].Dpi = dpi; dc.Stages[1].Enabled = true; dc.CurrentStage = 1; }
                    // les souris sans étapes matérielles gardent leur éclairage tant que l'utilisateur n'y touche pas
                    dc.Effect = "device";
                }
                foreach (var s in dc.Stages) s.Dpi = Math.Max(d.MinDpi, Math.Min(d.MaxDpi, s.Dpi));
                dc.Imported = true;
            }
            cfg.Save();
        }

        /// <summary>Réapplique tous les réglages enregistrés à la souris.</summary>
        public static void ApplyDevice()
        {
            var d = dev;
            var c = dc;
            if (d == null || c == null) return;
            List<DpiStage> stages;
            int cur;
            bool adv;
            lock (AppConfig.Sync) { stages = new List<DpiStage>(c.Stages); cur = c.CurrentStage; adv = c.Advanced; }
            d.ApplyStages(stages, cur);
            if (d.HasAdvancedMode)
            {
                d.SetAdvanced(adv);
                if (adv) ApplyLighting(); else StopEffect();
            }
            else ApplyLighting();
        }

        #endregion

        #region DPI et fréquence

        public static void SetPollRate(int hz)
        {
            var c = dc;
            if (c == null) return;
            lock (AppConfig.Sync) c.PollHz = hz;
            cfg.Save();
            var d = dev;
            if (d != null && hz > 0) d.SetPollRate(hz); // Corsair : la souris se reconnecte, la détection réappliquera le reste
        }

        public static void SetStage(int stage)
        {
            var c = dc;
            var d = dev;
            if (c == null) return;
            List<DpiStage> stages;
            lock (AppConfig.Sync) { c.CurrentStage = stage; stages = new List<DpiStage>(c.Stages); }
            if (d != null) d.SelectStage(stages, stage);
            if (d is CorsairMouse && c.Advanced) ApplyLighting();
        }

        static void CycleStage(int dir)
        {
            var c = dc;
            if (c == null) return;
            int cur;
            List<int> enabled = new List<int>();
            lock (AppConfig.Sync)
            {
                for (int i = 1; i < CorsairMouse.StageCount; i++) if (c.Stages[i].Enabled) enabled.Add(i);
                cur = c.CurrentStage;
            }
            if (enabled.Count == 0) return;
            int idx = enabled.IndexOf(cur);
            int next = enabled[((idx < 0 ? 0 : idx + dir) % enabled.Count + enabled.Count) % enabled.Count];
            SetStage(next);
            cfg.Save();
        }

        #endregion

        #region Éclairage

        static bool LightingActive(GamingMouse d, MouseDeviceConfig c)
        {
            return d != null && c != null && d.Zones.Length > 0 && (!d.HasAdvancedMode || c.Advanced);
        }

        public static void ApplyLighting()
        {
            var d = dev;
            var c = dc;
            if (!LightingActive(d, c)) return;
            string fx;
            lock (AppConfig.Sync) fx = c.Effect;
            if (fx == "breathe" || fx == "rainbow") StartEffect();
            else
            {
                StopEffect();
                if (fx != "device") d.SetZones(StaticColors(fx == "off" ? 0f : 1f));
            }
        }

        static Color[] StaticColors(float level)
        {
            var d = dev;
            var c = dc;
            int n = d != null ? d.Zones.Length : 0;
            var col = new Color[n];
            if (c == null) return col;
            lock (AppConfig.Sync)
            {
                for (int z = 0; z < n && z < c.ZoneColors.Count; z++) col[z] = Theme.FromHex(c.ZoneColors[z], Color.Black);
                // Corsair : la zone 3 (indicateur DPI) prend la couleur de l'étape en cours
                if (d is CorsairMouse && n > 2)
                    col[2] = Theme.FromHex(c.Stages[Math.Max(0, Math.Min(CorsairMouse.StageCount - 1, c.CurrentStage))].Color, col[2]);
            }
            for (int z = 0; z < n; z++) col[z] = Color.FromArgb((int)(col[z].R * level), (int)(col[z].G * level), (int)(col[z].B * level));
            return col;
        }

        /// <summary>Fait clignoter une zone en blanc pour l'identifier sur la souris.</summary>
        public static void IdentifyZone(int zone)
        {
            var d = dev;
            if (!LightingActive(d, dc)) return;
            StopEffect();
            new Thread(() =>
            {
                for (int i = 0; i < 6; i++)
                {
                    var c = StaticColors(0.05f);
                    if (zone < c.Length) c[zone] = i % 2 == 0 ? Color.White : Color.Black;
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
            // les souris non-Corsair acceptent moins de commandes par seconde
            int period = dev is CorsairMouse ? 50 : 150;
            effectTimer = new System.Threading.Timer(_ =>
            {
                var d = dev;
                var c = dc;
                if (d == null || c == null) return;
                string fx;
                int speed;
                lock (AppConfig.Sync) { fx = c.Effect; speed = c.EffectSpeed; }
                double t = (Environment.TickCount - t0) / 1000.0 * (0.2 + speed * 0.12);
                Color[] col;
                if (fx == "rainbow")
                {
                    col = new Color[d.Zones.Length];
                    for (int z = 0; z < col.Length; z++) col[z] = Theme.Hsl((t * 90 + z * 40) % 360, 1, 0.5);
                }
                else col = StaticColors((float)(0.08 + 0.92 * (0.5 + 0.5 * Math.Sin(t * Math.PI))));
                d.SetZones(col);
            }, null, 0, period);
        }

        static void StopEffect()
        {
            if (effectTimer == null) return;
            effectTimer.Dispose();
            effectTimer = null;
        }

        #endregion

        #region Boutons

        static volatile Action<string> detect;     // détection en cours : reçoit le prochain bouton pressé
        static string detectedUp;                  // relâchement à absorber après une détection

        /// <summary>Le prochain bouton pressé sur la souris est envoyé à "found" au lieu d'exécuter son action.</summary>
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

        /// <summary>Renvoie vrai si l'appui a été pris par la détection.</summary>
        static bool TakeDetect(string id, bool down)
        {
            if (!down && detectedUp == id) { detectedUp = null; return true; }
            var d = detect;
            if (d == null || !down) return false;
            detect = null;
            detectedUp = id;
            ThreadPool.QueueUserWorkItem(_ => d(id));
            return true;
        }

        static void OnDeviceButton(int bit, bool down)
        {
            string id = "cor:" + bit;
            var h = ButtonEvent;
            if (h != null) h(id, down);
            if (TakeDetect(id, down)) return;
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
                case "dpi_stage": { int s; if (down && int.TryParse(val, out s) && s >= 0 && s < CorsairMouse.StageCount) { SetStage(s); cfg.Save(); } break; }
                case "sniper":
                    if (down) { var c = dc; if (c == null) break; lock (AppConfig.Sync) sniperReturn = c.CurrentStage; SetStage(0); }
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
            if (detect != null) need = true; // la détection doit aussi voir milieu / précédent / suivant
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
                if (id != null && TakeDetect(id, down)) return new IntPtr(1); // pris par la détection : pas d'action d'origine
                if (id != null)
                {
                    var bh = ButtonEvent;
                    if (bh != null) bh(id, down);
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
