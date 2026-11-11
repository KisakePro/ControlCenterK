using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ControlCenterK
{
    /// <summary>Éclairage d'une zone, conservé dans la configuration.</summary>
    public class LedSetting
    {
        public string Mode { get; set; }    // none (on n'y touche pas), off, fixed, breathing, pulse, cycle, rainbow
        public int Color { get; set; }      // 0xRRGGBB
        public int Speed { get; set; }      // 0 lent, 1 normal, 2 rapide (Kraken)
        public int? Brightness { get; set; } // puissance en % (absente dans les anciens réglages : 100 %)
    }

    /// <summary>Une zone d'éclairage détectée.</summary>
    sealed class LedZone
    {
        public string Key, Name;
        public bool HasSpeed;
        public Func<LedSetting, bool> Apply;
    }

    /// <summary>
    /// Éclairage du Kraken X3 et de la carte mère ASUS (Aura USB), par les effets intégrés au matériel : ils continuent
    /// sans que l'application calcule quoi que ce soit, et rien n'est écrit dans la mémoire permanente de l'appareil.
    /// Une zone jamais réglée n'est pas touchée. Protocoles : liquidctl (Kraken), OpenRGB (Aura).
    /// </summary>
    static class LedControl
    {
        static readonly object sync = new object();
        static readonly List<HidChannel> channels = new List<HidChannel>();
        static List<LedZone> zones = new List<LedZone>();
        static AppConfig cfg;
        static bool running;

        public static event Action ZonesChanged;

        public static List<LedZone> Zones { get { lock (sync) return new List<LedZone>(zones); } }

        public static void Start(AppConfig config)
        {
            lock (sync)
            {
                if (running) return;
                running = true;
                cfg = config;
            }
            SystemEvents.PowerModeChanged += OnPower;
            ThreadPool.QueueUserWorkItem(_ => Scan(true));
        }

        public static void Stop()
        {
            lock (sync)
            {
                if (!running) return;
                running = false;
                CloseAll();
            }
            SystemEvents.PowerModeChanged -= OnPower;
        }

        static void CloseAll()
        {
            foreach (var c in channels) c.Dispose();
            channels.Clear();
            zones = new List<LedZone>();
        }

        // la veille éteint ou réinitialise ces contrôleurs : on rouvre et on réapplique
        static void OnPower(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(3000); Scan(true); });
        }

        static void Scan(bool reapply)
        {
            lock (sync)
            {
                if (!running) return;
                CloseAll();
                try
                {
                    var all = Hid.Enumerate(null);
                    OpenKraken(all);
                    OpenAura(all);
                }
                catch { }
                if (reapply)
                    foreach (var z in zones)
                    {
                        var s = Get(z.Key);
                        if (s.Mode != "none") try { z.Apply(s); } catch { }
                    }
            }
            var h = ZonesChanged;
            if (h != null) h();
        }

        public static LedSetting Get(string key)
        {
            LedSetting s;
            lock (AppConfig.Sync) if (cfg != null && cfg.FanLeds.TryGetValue(key, out s)) return s;
            return new LedSetting { Mode = "none", Color = 0xFFFFFF, Speed = 1, Brightness = 100 };
        }

        /// <summary>Enregistre le réglage et l'envoie à la zone ; false si l'appareil n'a pas répondu.</summary>
        public static bool Set(LedZone z, LedSetting s)
        {
            lock (AppConfig.Sync) cfg.FanLeds[z.Key] = s;
            cfg.Save();
            if (s.Mode == "none") return true;
            lock (sync)
            {
                try { return z.Apply(s); }
                catch { return false; }
            }
        }

        /// <summary>
        /// Couleur à la puissance demandée. Ces contrôleurs n'ont pas de réglage de luminosité : on assombrit la couleur
        /// envoyée, ce qui ne touche donc pas les effets arc-en-ciel, dont le matériel choisit les couleurs.
        /// </summary>
        static int Dimmed(LedSetting s)
        {
            int pct = Math.Max(0, Math.Min(100, s.Brightness ?? 100));
            int r = (s.Color >> 16 & 0xFF) * pct / 100, g = (s.Color >> 8 & 0xFF) * pct / 100, b = (s.Color & 0xFF) * pct / 100;
            return r << 16 | g << 8 | b;
        }

        #region NZXT Kraken X3

        static void OpenKraken(List<HidInfo> all)
        {
            foreach (var i in all)
            {
                if (i.Vid != 0x1E71 || (i.Pid != 0x2007 && i.Pid != 0x2014) || i.OutLen == 0) continue;
                var ch = HidChannel.Open(i);
                if (ch == null) continue;
                channels.Add(ch);
                zones.Add(new LedZone { Key = "kraken|ring", Name = "Kraken : anneau", HasSpeed = true, Apply = s => Kraken(ch, 2, 8, s) });
                zones.Add(new LedZone { Key = "kraken|logo", Name = "Kraken : logo", HasSpeed = true, Apply = s => Kraken(ch, 4, 1, s) });
                return;
            }
        }

        static bool Kraken(HidChannel ch, byte channel, byte staticValue, LedSetting s)
        {
            int mode, related = 0;
            int[] speeds;
            switch (s.Mode)
            {
                case "breathing": mode = 0x07; related = 8; speeds = new[] { 0x001E, 0x0014, 0x000A }; break;
                case "pulse": mode = 0x06; related = 8; speeds = new[] { 0x0014, 0x000F, 0x0007 }; break;
                case "cycle": mode = 0x02; speeds = new[] { 0x012C, 0x00FA, 0x0096 }; break;
                case "rainbow": mode = 0x0B; speeds = new[] { 0x012C, 0x00FA, 0x0096 }; break;
                default: mode = 0x00; speeds = new[] { 0x0032, 0x0032, 0x0032 }; break;
            }
            int speed = speeds[Math.Max(0, Math.Min(2, s.Speed))];
            bool colored = s.Mode != "off" && s.Mode != "cycle" && s.Mode != "rainbow";
            var r = new byte[64];
            r[0] = 0x2A; r[1] = 0x04;
            r[2] = channel; r[3] = channel;
            r[4] = (byte)mode;
            r[5] = (byte)(speed & 0xFF); r[6] = (byte)(speed >> 8);
            if (colored)
            {
                int c = Dimmed(s);
                r[7] = (byte)(c >> 8);              // vert
                r[8] = (byte)(c >> 16);             // rouge
                r[9] = (byte)c;                     // bleu
            }
            int foot = 7 + 16 * 3;
            r[foot + 1] = 1;                        // nombre de couleurs
            r[foot + 2] = (byte)related;
            r[foot + 3] = staticValue;
            r[foot + 4] = 3;                        // taille des LED
            return ch.Write(r);
        }

        #endregion

        #region ASUS Aura (carte mère)

        static void OpenAura(List<HidInfo> all)
        {
            foreach (var i in all)
            {
                if (i.Vid != 0x0B05 || i.UsagePage != 0xFF72 || i.OutLen == 0) continue;
                if (i.Pid != 0x18F3 && i.Pid != 0x1939 && i.Pid != 0x19AF && i.Pid != 0x1AA6 && i.Pid != 0x1BED) continue;
                var ch = HidChannel.Open(i);
                if (ch == null) continue;
                channels.Add(ch);
                if (!AddAura(ch)) { channels.Remove(ch); ch.Dispose(); continue; }
                return;
            }
        }

        static byte[] Ask(HidChannel ch, byte request, byte reply)
        {
            ch.Clear();
            var r = new byte[65];
            r[0] = 0xEC; r[1] = request;
            if (!ch.Write(r)) return null;
            return ch.Read(500, b => b.Length > 4 && b[1] == reply);
        }

        static bool AddAura(HidChannel ch)
        {
            var fw = Ask(ch, 0x82, 0x02);
            var table = Ask(ch, 0xB0, 0x30);
            if (table == null || table.Length < 64) return false;
            string version = fw != null && fw.Length >= 18 ? Encoding.ASCII.GetString(fw, 2, 16) : "";
            int boardLeds = table[4 + 0x1B], argb = table[4 + 0x02];
            // le micrologiciel AULA3-AR32 numérote d'abord les prises ARGB ; les autres, d'abord la carte mère
            bool argbFirst = version.StartsWith("AULA3-AR32", StringComparison.Ordinal);
            bool[] ready = { false };
            int effect = 0;
            if (boardLeds > 0)
            {
                int fx = argbFirst ? argb : effect++;
                zones.Add(new LedZone { Key = "aura|board", Name = "Carte mère : LED intégrées", Apply = s => Aura(ch, ready, fx, 0, boardLeds, s) });
            }
            for (int a = 0; a < argb; a++)
            {
                int fx = effect++, led = boardLeds + a, n = a + 1;
                zones.Add(new LedZone { Key = "aura|argb" + a, Name = "Prise ARGB " + n + " (ventilateurs, bandeaux)", Apply = s => Aura(ch, ready, fx, led, 1, s) });
            }
            return true;
        }

        static bool Aura(HidChannel ch, bool[] ready, int effect, int startLed, int leds, LedSetting s)
        {
            if (!ready[0])
            {
                var init = new byte[65];
                init[0] = 0xEC; init[1] = 0x52; init[2] = 0x53; init[4] = 0x01;
                if (!ch.Write(init)) return false;
                ready[0] = true;
            }
            byte mode;
            switch (s.Mode)
            {
                case "fixed": mode = 1; break;
                case "breathing": mode = 2; break;
                case "pulse": mode = 3; break;
                case "cycle": mode = 4; break;
                case "rainbow": mode = 5; break;
                default: mode = 0; break;
            }
            var fx = new byte[65];
            fx[0] = 0xEC; fx[1] = 0x35; fx[2] = (byte)effect; fx[5] = mode;
            if (!ch.Write(fx)) return false;
            if (startLed + leds > 20) return true; // hors du rapport : l'effet est posé, sans couleur
            int dimmed = Dimmed(s);
            int mask = ((1 << leds) - 1) << startLed;
            var col = new byte[65];
            col[0] = 0xEC; col[1] = 0x36;
            col[2] = (byte)(mask >> 8); col[3] = (byte)mask;
            for (int i = 0; i < leds; i++)
            {
                int o = 5 + 3 * (startLed + i);
                col[o] = (byte)(dimmed >> 16);
                col[o + 1] = (byte)(dimmed >> 8);
                col[o + 2] = (byte)dimmed;
            }
            return ch.Write(col);
        }

        #endregion
    }
}
