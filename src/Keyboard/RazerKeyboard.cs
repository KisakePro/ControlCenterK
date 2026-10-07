using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ControlCenterK
{
    /// <summary>
    /// Échange avec un périphérique Razer : rapport « feature » de 90 octets (protocole documenté par openrazer),
    /// avec somme de contrôle et attente de la réponse. Commun aux souris et aux claviers.
    /// </summary>
    sealed class RazerLink : IDisposable
    {
        readonly SafeFileHandle h;
        readonly object io = new object();
        public byte Tid;

        RazerLink(SafeFileHandle h) { this.h = h; }

        /// <summary>Ouvre une interface Razer et trouve l'identifiant de transaction qui fait répondre "probe".</summary>
        public static RazerLink Open(HidInfo info, Func<RazerLink, bool> probe)
        {
            var hd = Hid.Open(info.Path, true);
            if (hd.IsInvalid) { hd.Dispose(); hd = Hid.OpenNoAccess(info.Path); }
            if (hd.IsInvalid) { hd.Dispose(); return null; }
            var link = new RazerLink(hd);
            // 0x1F (récents), 0x3F, 0xFF (anciens)
            foreach (byte t in new byte[] { 0x1F, 0x3F, 0xFF })
            {
                link.Tid = t;
                if (probe(link)) return link;
            }
            link.Dispose();
            return null;
        }

        /// <summary>Envoie une commande ; renvoie les 80 octets d'arguments de la réponse, null en cas d'échec.</summary>
        public byte[] Send(byte cls, byte id, byte size, params byte[] args)
        {
            var r = new byte[91];       // octet 0 : identifiant de rapport (0)
            r[2] = Tid;
            r[6] = size;
            r[7] = cls;
            r[8] = id;
            Array.Copy(args, 0, r, 9, Math.Min(args.Length, 80));
            byte crc = 0;
            for (int i = 3; i < 89; i++) crc ^= r[i];   // octets 2 à 87 du rapport
            r[89] = crc;
            lock (io)
            {
                if (!Hid.HidD_SetFeature(h, r, r.Length)) return null;
                int end = Environment.TickCount + 450;
                while (Environment.TickCount < end)
                {
                    Thread.Sleep(2);
                    var resp = new byte[91];
                    if (!Hid.HidD_GetFeature(h, resp, resp.Length)) return null;
                    byte status = resp[1];
                    if (status == 0x01 || status == 0x00) continue;               // occupé / pas encore traité
                    if (status != 0x02 || resp[7] != cls || resp[8] != id) return null; // échec, délai, non pris en charge
                    var a = new byte[80];
                    Array.Copy(resp, 9, a, 0, 80);
                    return a;
                }
                return null;
            }
        }

        /// <summary>Couleur statique d'une LED : effet « étendu » (récents), sinon commandes LED standard (anciens).</summary>
        public bool StaticColor(byte led, Color c)
        {
            if (Send(0x0F, 0x02, 0x09, 0x01, led, 0x01, 0, 0, 0x01, c.R, c.G, c.B) != null) return true;
            return Send(0x03, 0x01, 0x05, 0x01, led, c.R, c.G, c.B) != null && Send(0x03, 0x02, 0x03, 0x01, led, 0x00) != null;
        }

        public void Dispose() { h.Dispose(); }
    }

    /// <summary>Claviers Razer : couleur du rétroéclairage (LED 0x05) pour tout le clavier.</summary>
    sealed class RazerKeyboard : RgbKeyboard
    {
        readonly RazerLink link;
        readonly int pid;

        RazerKeyboard(RazerLink link, int pid) { this.link = link; this.pid = pid; }

        public static RazerKeyboard Probe(List<HidInfo> group)
        {
            foreach (var info in group.FindAll(x => x.FeatureLen == 91))
            {
                // lecture du firmware : toutes les générations y répondent
                var link = RazerLink.Open(info, l => l.Send(0x00, 0x81, 0x02) != null);
                if (link == null) continue;
                return new RazerKeyboard(link, info.Pid)
                {
                    Brand = "Razer",
                    Name = MouseDetect.CleanName(MouseDetect.NameOf(group, "Clavier Razer"), "Razer"),
                };
            }
            return null;
        }

        public override bool SetAll(Color c) { return link.StaticColor(0x05, c); }

        public override bool Alive() { return link.Send(0x00, 0x81, 0x02) != null; }

        public override void Dispose() { link.Dispose(); }
    }
}
