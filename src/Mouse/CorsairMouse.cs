using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ControlCenterK
{
    /// <summary>
    /// Souris Corsair à protocole « NXP » (testé sur la Nightsword RGB, firmware 3.41).
    /// Commandes de 64 octets envoyées en rapport « feature » sur l'interface MI_01 (page 0xFFC2).
    /// Seules des commandes de réglage en direct sont utilisées : rien n'est écrit dans la mémoire interne de la souris.
    /// </summary>
    sealed class CorsairMouse : IDisposable
    {
        public sealed class Model
        {
            public int Pid;
            public string Name;
            public int MaxDpi;
            public string[] Zones; // nom des zones d'éclairage 1..6
        }

        public static readonly Model[] Models =
        {
            new Model { Pid = 0x1B5C, Name = "Corsair Nightsword RGB", MaxDpi = 18000,
                Zones = new[] { "Zone 1", "Zone 2", "Zone 3", "Zone 4", "Zone 5", "Zone 6" } },
        };

        public const int StageCount = 6; // étape 0 = sniper, 1..5 = étapes DPI

        public readonly Model Info;
        readonly string ctlPath;
        readonly List<string> inputPaths;
        SafeFileHandle ctl;
        readonly object io = new object();
        readonly List<Thread> readers = new List<Thread>();
        readonly List<SafeFileHandle> inputHandles = new List<SafeFileHandle>();
        volatile bool closing;

        /// <summary>Appui (true) ou relâchement (false) d'un bouton, identifié par son numéro de bit Corsair. Thread de lecture.</summary>
        public event Action<int, bool> Button;

        public string Firmware { get; private set; }

        CorsairMouse(Model m, string ctlPath, List<string> inputs)
        {
            Info = m;
            this.ctlPath = ctlPath;
            inputPaths = inputs;
        }

        /// <summary>Cherche une souris compatible branchée.</summary>
        public static CorsairMouse Find()
        {
            foreach (var m in Models)
            {
                string id = "vid_1b1c&pid_" + m.Pid.ToString("x4");
                var all = Hid.Paths(id);
                var ctl = all.Find(p => p.IndexOf("&mi_01", StringComparison.OrdinalIgnoreCase) >= 0);
                if (ctl == null) continue;
                var inputs = all.FindAll(p => p.IndexOf("&col03", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              p.IndexOf("&col04", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              p.IndexOf("&col05", StringComparison.OrdinalIgnoreCase) >= 0);
                var dev = new CorsairMouse(m, ctl, inputs);
                if (dev.Connect()) return dev;
                dev.Dispose();
            }
            return null;
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

        #region Réglages

        /// <summary>Valeur et couleur d'indicateur d'une étape DPI (0 = sniper).</summary>
        public bool SetStage(int stage, int dpi, Color led)
        {
            dpi = Math.Max(100, Math.Min(Info.MaxDpi, dpi));
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
        public bool SetPollRate(int hz)
        {
            int ms = hz >= 1000 ? 1 : hz >= 500 ? 2 : hz >= 250 ? 4 : 8;
            return Send(0x07, 0x0a, 0x00, 0x00, (byte)ms);
        }

        /// <summary>
        /// Mode logiciel : nécessaire pour l'éclairage et pour recevoir les boutons DPI / sniper / latéraux.
        /// Les 6 premiers boutons restent des boutons Windows normaux (clics, molette, précédent / suivant).
        /// </summary>
        public bool SetSoftwareMode(bool on)
        {
            if (!on) return Send(0x07, 0x04, 0x01);
            if (!Send(0x07, 0x04, 0x02)) return false;
            Thread.Sleep(20);
            var ki = new byte[44];
            ki[0] = 0x07; ki[1] = 0x40; ki[2] = 20;
            for (int i = 0; i < 20; i++) { ki[4 + i * 2] = (byte)(i + 1); ki[5 + i * 2] = (byte)(i < 6 ? 0xC0 : 0x40); }
            return Send(ki);
        }

        /// <summary>Couleurs des 6 zones d'éclairage (mode logiciel requis).</summary>
        public bool SetZones(Color[] zones)
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
        public void StartButtons()
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
                    var h = Button;
                    if (h == null || changed == 0) continue;
                    for (int bit = 0; bit < 64; bit++)
                        if ((changed & (1UL << bit)) != 0) h(bit, (mask & (1UL << bit)) != 0);
                }
            }
            catch { }
        }

        #endregion

        public void Dispose()
        {
            closing = true;
            foreach (var h in inputHandles) try { h.Dispose(); } catch { }
            inputHandles.Clear();
            lock (io) { if (ctl != null) ctl.Dispose(); ctl = null; }
        }
    }
}
