using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ControlCenterK
{
    /// <summary>
    /// Souris Corsair à protocole « NXP » (testé sur la Nightsword RGB, firmware 3.41 ; même protocole pour
    /// la plupart des souris Corsair filaires : M65, Scimitar, Ironclaw, Harpoon, Glaive, Katar…).
    /// Commandes de 64 octets envoyées en rapport « feature » sur l'interface MI_01 (page 0xFFC2).
    /// Seules des commandes de réglage en direct sont utilisées : rien n'est écrit dans la mémoire interne de la souris.
    /// </summary>
    sealed class CorsairMouse : GamingMouse
    {
        /// <summary>DPI maximal des modèles vérifiés (les autres : 18000, la souris limite d'elle-même).</summary>
        static readonly Dictionary<int, int> KnownMaxDpi = new Dictionary<int, int> { { 0x1B5C, 18000 } };

        public const int StageCount = 6; // étape 0 = sniper, 1..5 = étapes DPI

        public override bool HardwareStages { get { return true; } }
        public override bool HasAdvancedMode { get { return true; } }

        readonly string ctlPath;
        readonly List<string> inputPaths;
        SafeFileHandle ctl;
        readonly object io = new object();
        readonly List<Thread> readers = new List<Thread>();
        readonly List<SafeFileHandle> inputHandles = new List<SafeFileHandle>();
        volatile bool closing;

        CorsairMouse(string ctlPath, List<string> inputs)
        {
            this.ctlPath = ctlPath;
            inputPaths = inputs;
        }

        /// <summary>Essaie le protocole Corsair sur les interfaces d'un modèle : la souris doit répondre à l'identification.</summary>
        public static CorsairMouse Probe(List<HidInfo> group)
        {
            var ctl = group.Find(h => h.Interface == 1 && h.UsagePage == 0xFFC2 && h.FeatureLen >= 65)
                   ?? group.Find(h => h.Interface == 1 && h.FeatureLen == 65);
            if (ctl == null) return null;
            var inputs = new List<string>();
            foreach (var h in group)
                if (h.Interface == 0 && h.InLen > 0 && h.UsagePage >= 0xFF00) inputs.Add(h.Path); // rapports de boutons (mode logiciel)
            var dev = new CorsairMouse(ctl.Path, inputs);
            if (!dev.Connect()) { dev.Dispose(); return null; }
            int max;
            dev.MaxDpi = KnownMaxDpi.TryGetValue(ctl.Pid, out max) ? max : 18000;
            dev.Name = MouseDetect.CleanName(MouseDetect.NameOf(group, "Souris " + ctl.Pid.ToString("X4")), "Corsair");
            dev.Brand = "Corsair";
            dev.Experimental = ctl.Pid != 0x1B5C; // protocole vérifié sur la Nightsword
            dev.PollRates = new[] { 125, 250, 500, 1000 };
            dev.Zones = new[] { "Zone 1", "Zone 2", "Zone 3", "Zone 4", "Zone 5", "Zone 6" };
            return dev;
        }

        bool Connect()
        {
            ctl = Hid.Open(ctlPath, true);
            if (ctl.IsInvalid) return false;
            var id = Ask(0x0e, 0x01);
            if (id == null) return false;
            Firmware = id[9].ToString("X") + "." + id[8].ToString("X2");
            return true;
        }

        #region Échanges

        bool Send(params byte[] cmd)
        {
            var o = new byte[65];
            Array.Copy(cmd, 0, o, 1, Math.Min(cmd.Length, 64));
            lock (io) return ctl != null && !ctl.IsInvalid && Hid.HidD_SetFeature(ctl, o, 65);
        }

        byte[] Ask(params byte[] cmd)
        {
            lock (io)
            {
                if (!Send(cmd)) return null;
                for (int t = 0; t < 10; t++)
                {
                    Thread.Sleep(4);
                    var i = new byte[65];
                    if (Hid.HidD_GetFeature(ctl, i, 65) && i[1] == cmd[0] && i[2] == cmd[1] && (cmd.Length < 3 || i[3] == cmd[2]))
                    {
                        var r = new byte[64];
                        Array.Copy(i, 1, r, 0, 64);
                        return r;
                    }
                }
                return null;
            }
        }

        #endregion

        #region Interface commune

        public override bool SetDpi(int dpi)
        {
            // valeur unique : l'étape 1 devient l'étape courante
            return SetStage(1, dpi, Color.Black) && SetCurrentStage(1);
        }

        public override void ApplyStages(IList<DpiStage> stages, int current)
        {
            int mask = 0;
            for (int i = 0; i < StageCount && i < stages.Count; i++)
            {
                var s = stages[i];
                if (s.Enabled) mask |= 1 << i;
                SetStage(i, s.Dpi, Theme.FromHex(s.Color, Color.Black));
            }
            SetStageMask(mask);
            SetCurrentStage(current);
        }

        public override void SelectStage(IList<DpiStage> stages, int stage) { SetCurrentStage(stage); }

        public override bool ReadStages(IList<DpiStage> stages, out int current)
        {
            int mask = ReadStageMask();
            current = ReadCurrentStage();
            if (mask < 0 || current < 0) return false;
            for (int i = 0; i < StageCount && i < stages.Count; i++)
            {
                int dpi;
                Color led;
                if (!ReadStage(i, out dpi, out led)) return false;
                var s = stages[i];
                s.Enabled = (mask & (1 << i)) != 0;
                if (dpi >= 100) s.Dpi = dpi;
                if (led.R + led.G + led.B > 0) s.Color = Theme.ToHex(led);
            }
            return true;
        }

        public override bool Alive() { return ReadCurrentStage() >= 0; }

        public override void SetAdvanced(bool on)
        {
            SetSoftwareMode(on);
            if (on) StartButtons();
        }

        #endregion

        #region Réglages

        /// <summary>Valeur et couleur d'indicateur d'une étape DPI (0 = sniper).</summary>
        public bool SetStage(int stage, int dpi, Color led)
        {
            dpi = Clamp(dpi, 100, MaxDpi);
            return Send(0x07, 0x13, (byte)(0xD0 | stage), 0x00, 0x00,
                (byte)(dpi & 0xFF), (byte)(dpi >> 8), (byte)(dpi & 0xFF), (byte)(dpi >> 8),
                led.R, led.G, led.B);
        }

        /// <summary>Étapes actives (bit 0 = sniper, bits 1..5 = étapes).</summary>
        public bool SetStageMask(int mask) { return Send(0x07, 0x13, 0x05, 0x00, (byte)(mask & 0x3F)); }

        public bool SetCurrentStage(int stage) { return Send(0x07, 0x13, 0x02, 0x00, (byte)stage); }

        public int ReadCurrentStage()
        {
            var r = Ask(0x0e, 0x13, 0x02, 0x00);
            return r == null ? -1 : r[4];
        }

        /// <summary>Lit la valeur DPI et la couleur d'une étape (réglages en cours). La valeur est renvoyée octet fort en premier.</summary>
        public bool ReadStage(int stage, out int dpi, out Color led)
        {
            var r = Ask(0x0e, 0x13, (byte)(0xD0 | stage), 0x00);
            dpi = 0;
            led = Color.Black;
            if (r == null) return false;
            dpi = r[5] << 8 | r[6];
            led = Color.FromArgb(r[9], r[10], r[11]);
            return true;
        }

        public int ReadStageMask()
        {
            var r = Ask(0x0e, 0x13, 0x05, 0x00);
            return r == null ? -1 : r[4];
        }

        /// <summary>Fréquence d'interrogation (125, 250, 500 ou 1000 Hz). La souris se reconnecte (~1 s).</summary>
        public override bool SetPollRate(int hz)
        {
            int ms = hz >= 1000 ? 1 : hz >= 500 ? 2 : hz >= 250 ? 4 : 8;
            return Send(0x07, 0x0a, 0x00, 0x00, (byte)ms);
        }

        /// <summary>
        /// Mode logiciel : nécessaire pour l'éclairage et pour recevoir les boutons DPI / sniper / latéraux.
        /// Les 6 premiers boutons restent des boutons Windows normaux (clics, molette, précédent / suivant) et ne sont pas envoyés à l'application.
        /// </summary>
        public bool SetSoftwareMode(bool on)
        {
            if (!on) return Send(0x07, 0x04, 0x01);
            if (!Send(0x07, 0x04, 0x02)) return false;
            Thread.Sleep(20);
            var ki = new byte[44];
            ki[0] = 0x07; ki[1] = 0x40; ki[2] = 20;
            for (int i = 0; i < 20; i++) { ki[4 + i * 2] = (byte)(i + 1); ki[5 + i * 2] = (byte)(i < 6 ? 0x80 : 0x40); } // 1-6 : Windows uniquement, 7-20 : application
            return Send(ki);
        }

        /// <summary>Couleurs des 6 zones d'éclairage (mode logiciel requis).</summary>
        public override bool SetZones(Color[] zones)
        {
            var p = new byte[4 + 6 * 4];
            p[0] = 0x07; p[1] = 0x22; p[2] = 6; p[3] = 0x01;
            for (int z = 0; z < 6; z++)
            {
                var c = z < zones.Length ? zones[z] : Color.Black;
                p[4 + z * 4] = (byte)(z + 1);
                p[5 + z * 4] = c.R;
                p[6 + z * 4] = c.G;
                p[7 + z * 4] = c.B;
            }
            return Send(p);
        }

        #endregion

        #region Boutons (mode logiciel)

        /// <summary>Démarre la lecture des événements de boutons Corsair (un thread bloqué en lecture, 0 % CPU au repos).</summary>
        public override void StartButtons()
        {
            if (readers.Count > 0) return;
            foreach (var path in inputPaths)
            {
                var h = Hid.Open(path, false);
                if (h.IsInvalid) continue;
                inputHandles.Add(h);
                var fs = new FileStream(h, FileAccess.Read, 65, false);
                var t = new Thread(() => ReadLoop(fs)) { IsBackground = true, Name = "Boutons Corsair" };
                readers.Add(t);
                t.Start();
            }
        }

        void ReadLoop(FileStream fs)
        {
            var buf = new byte[65];
            ulong last = 0;
            try
            {
                while (!closing)
                {
                    int n = fs.Read(buf, 0, buf.Length);
                    if (n < 2 || buf[0] != 0x03) continue; // rapport 3 : état des boutons
                    ulong mask = 0;
                    for (int i = 1; i < Math.Min(n, 9); i++) mask |= (ulong)buf[i] << (8 * (i - 1));
                    ulong changed = mask ^ last;
                    last = mask;
                    if (changed == 0) continue;
                    for (int bit = 0; bit < 64; bit++)
                        if ((changed & (1UL << bit)) != 0) RaiseButton(bit, (mask & (1UL << bit)) != 0);
                }
            }
            catch { }
        }

        #endregion

        public override void Dispose()
        {
            closing = true;
            foreach (var h in inputHandles) try { Hid.CancelIoEx(h, IntPtr.Zero); h.Dispose(); } catch { }
            inputHandles.Clear();
            lock (io) { if (ctl != null) ctl.Dispose(); ctl = null; }
        }
    }
}
