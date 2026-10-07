using System;
using System.Collections.Generic;
using System.Text;

namespace ControlCenterK
{
    /// <summary>
    /// Souris Logitech G (protocole HID++ 2.0, commun aux souris filaires et aux récepteurs Lightspeed / Unifying).
    /// Fonctions utilisées : 0x0005 (nom), 0x2201 (DPI réglable), 0x8060 (fréquence réglable).
    /// Les réglages sont envoyés en direct ; les profils de la mémoire interne ne sont pas modifiés.
    /// </summary>
    sealed class LogitechMouse : GamingMouse
    {
        const byte LongReport = 0x11, SoftwareId = 0x0B;
        const int Timeout = 150;

        readonly HidChannel ch;
        readonly byte index;           // 0xFF : souris filaire ; 1..6 : appareil appairé au récepteur
        byte fDpi, fRate;              // index des fonctions dans la table de la souris (0 = absente)
        readonly object io = new object();

        LogitechMouse(HidChannel ch, byte index)
        {
            this.ch = ch;
            this.index = index;
        }

        public static LogitechMouse Probe(List<HidInfo> group)
        {
            var info = group.Find(h => h.UsagePage == 0xFF00 && h.Usage == 0x0002 && h.OutLen >= 20);
            if (info == null) return null;
            var ch = HidChannel.Open(info);
            if (ch == null) return null;
            // 0xFF : souris branchée en filaire ; 1..6 : appareils d'un récepteur sans fil
            foreach (byte idx in new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 })
            {
                var dev = new LogitechMouse(ch, idx);
                if (!dev.Init()) continue;
                dev.Brand = "Logitech";
                if (dev.Name.Length == 0) dev.Name = MouseDetect.CleanName(MouseDetect.NameOf(group, "Souris Logitech"), "Logitech");
                else if (!dev.Name.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase)) dev.Name = "Logitech " + dev.Name;
                return dev;
            }
            ch.Dispose();
            return null;
        }

        bool Init()
        {
            fDpi = Feature(0x2201);
            if (fDpi == 0) return false; // pas une souris à DPI réglable (clavier, casque, appareil éteint…)
            fRate = Feature(0x8060);

            // plage de DPI du capteur 0
            var list = Call(fDpi, 1, 0);
            if (list != null)
            {
                int min = int.MaxValue, max = 0, step = 0;
                for (int i = 5; i + 1 < list.Length; i += 2)
                {
                    int v = list[i] << 8 | list[i + 1];
                    if (v == 0) break;
                    if (v > 0xE000) step = v - 0xE000;
                    else { min = Math.Min(min, v); max = Math.Max(max, v); }
                }
                if (max > 0) { MinDpi = min; MaxDpi = max; DpiStep = step > 0 ? step : 50; }
            }

            if (fRate != 0)
            {
                var r = Call(fRate, 0);
                if (r != null)
                {
                    var rates = new List<int>();
                    for (int bit = 7; bit >= 0; bit--)
                        if ((r[4] & (1 << bit)) != 0) rates.Add(1000 / (bit + 1));
                    rates.RemoveAll(hz => hz != 125 && hz != 250 && hz != 500 && hz != 1000);
                    PollRates = rates.ToArray();
                }
            }

            byte fName = Feature(0x0005);
            if (fName != 0)
            {
                var c = Call(fName, 0);
                int len = c == null ? 0 : c[4];
                var sb = new StringBuilder();
                while (sb.Length < len && sb.Length < 64)
                {
                    var part = Call(fName, 1, (byte)sb.Length);
                    if (part == null) break;
                    int n = 0;
                    for (int i = 4; i < part.Length && sb.Length < len; i++, n++) sb.Append((char)part[i]);
                    if (n == 0) break;
                }
                Name = sb.ToString().TrimEnd('\0', ' ');
            }
            return true;
        }

        #region Échanges HID++

        /// <summary>Index d'une fonction HID++ (0 si absente), via la fonction racine.</summary>
        byte Feature(int id)
        {
            var r = Call(0, 0, (byte)(id >> 8), (byte)id);
            return r == null ? (byte)0 : r[4];
        }

        /// <summary>Envoie une requête et renvoie la réponse (octets 4 et suivants = paramètres), null si erreur ou délai.</summary>
        byte[] Call(byte feature, int function, params byte[] args)
        {
            byte addr = (byte)(function << 4 | SoftwareId);
            var msg = new byte[20];
            msg[0] = LongReport;
            msg[1] = index;
            msg[2] = feature;
            msg[3] = addr;
            Array.Copy(args, 0, msg, 4, Math.Min(args.Length, 16));
            lock (io)
            {
                ch.Clear();
                if (!ch.Write(msg)) return null;
                var resp = ch.Read(Timeout, b => b.Length >= 5 && b[0] == LongReport && b[1] == index &&
                    ((b[2] == feature && b[3] == addr) || (b[2] == 0xFF && b[3] == feature && b[4] == addr)));
                return resp == null || resp[2] == 0xFF ? null : resp;
            }
        }

        #endregion

        public override bool SetDpi(int dpi)
        {
            dpi = Clamp(dpi, MinDpi, MaxDpi);
            return Call(fDpi, 3, 0, (byte)(dpi >> 8), (byte)dpi) != null;
        }

        public override int ReadDpi()
        {
            var r = Call(fDpi, 2, 0);
            return r == null ? -1 : r[5] << 8 | r[6];
        }

        public override bool SetPollRate(int hz)
        {
            if (fRate == 0 || hz <= 0) return false;
            return Call(fRate, 2, (byte)Math.Max(1, 1000 / hz)) != null;
        }

        public override bool Alive() { return ReadDpi() > 0; }

        public override void Dispose() { ch.Dispose(); }
    }
}
