using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.Win32.SafeHandles;

namespace ControlCenterK
{
    /// <summary>
    /// Claviers Corsair à protocole « NXP » (K65, K70, K95, Strafe RGB ; protocole documenté par OpenRGB / ckb-next).
    /// Les couleurs sont envoyées par plans rouge / vert / bleu ; ici une seule couleur pour tout le clavier.
    /// </summary>
    sealed class CorsairKeyboard : RgbKeyboard
    {
        static readonly Dictionary<int, string> Models = new Dictionary<int, string>
        {
            { 0x1B17, "Corsair K65 RGB" }, { 0x1B37, "Corsair K65 LUX RGB" }, { 0x1B39, "Corsair K65 RGB Rapidfire" },
            { 0x1B13, "Corsair K70 RGB" }, { 0x1B33, "Corsair K70 LUX RGB" }, { 0x1B38, "Corsair K70 RGB Rapidfire" },
            { 0x1B49, "Corsair K70 RGB MK.2" }, { 0x1B6B, "Corsair K70 RGB MK.2 SE" }, { 0x1B55, "Corsair K70 RGB MK.2 Low Profile" },
            { 0x1BC6, "Corsair K70 RGB Pro Optomechanical" }, { 0x1B11, "Corsair K95 RGB" }, { 0x1B2D, "Corsair K95 RGB Platinum" },
            { 0x1B82, "Corsair K95 RGB Platinum SE" }, { 0x1B20, "Corsair Strafe RGB" }, { 0x1B48, "Corsair Strafe RGB MK.2" },
        };
        // modèles qui demandent aussi la commande « fonctions spéciales » et un dernier paquet plus long
        static readonly int[] Mk2 = { 0x1B49, 0x1B6B, 0x1B55, 0x1B2D, 0x1B82 };
        static readonly int[] K95 = { 0x1B11, 0x1B2D, 0x1B82 };

        readonly HidInfo info;
        readonly SafeFileHandle h;
        readonly object io = new object();
        bool software;

        CorsairKeyboard(HidInfo info, SafeFileHandle h) { this.info = info; this.h = h; }

        public static CorsairKeyboard Probe(List<HidInfo> group)
        {
            string name;
            if (!Models.TryGetValue(group[0].Pid, out name)) return null;
            var info = group.Find(x => x.Interface == 1 && x.UsagePage == 0xFFC2 && x.OutLen >= 65);
            if (info == null) return null;
            var hd = Hid.Open(info.Path, true);
            if (hd.IsInvalid) { hd.Dispose(); return null; }
            return new CorsairKeyboard(info, hd) { Name = name, Brand = "Corsair" };
        }

        bool Send(params byte[] data)
        {
            var b = new byte[65];
            Array.Copy(data, 0, b, 1, Math.Min(data.Length, 64));
            lock (io) return Hid.Write(h, b, Math.Max(65, info.OutLen));
        }

        void Software()
        {
            if (software) return;
            software = true;
            if (Array.IndexOf(Mk2, info.Pid) >= 0) Send(0x07, 0x04, 0x02);   // fonctions spéciales : logiciel
            Send(0x07, 0x05, 0x02, 0x00, 0x03);                              // éclairage : logiciel
        }

        public override bool SetAll(Color c)
        {
            Software();
            int last = Array.IndexOf(K95, info.Pid) >= 0 ? 48 : 24;
            bool ok = true;
            byte[] channel = { c.R, c.G, c.B };
            for (int ch = 0; ch < 3; ch++)
            {
                for (int p = 0; p < 3; p++)
                {
                    int len = p < 2 ? 60 : last;
                    var pkt = new byte[4 + len];
                    pkt[0] = 0x7F; pkt[1] = (byte)(p + 1); pkt[2] = (byte)len;
                    for (int i = 0; i < len; i++) pkt[4 + i] = channel[ch];
                    ok &= Send(pkt);
                }
                ok &= Send(0x07, 0x28, (byte)(ch + 1), 0x03, (byte)(ch == 2 ? 2 : 1));
            }
            return ok;
        }

        public override void Release()
        {
            if (!software) return;
            Send(0x07, 0x04, 0x01); // retour à l'éclairage du clavier
            software = false;
        }

        public override bool Alive() { return Hid.Paths("pid_" + info.Pid.ToString("x4")).Count > 0; }

        public override void Dispose() { h.Dispose(); }
    }
}
