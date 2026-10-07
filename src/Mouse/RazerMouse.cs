using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ControlCenterK
{
    /// <summary>
    /// Souris Razer (protocole des rapports « feature » de 90 octets, documenté par le projet openrazer).
    /// Commandes utilisées : DPI (0x04/0x05), fréquence (0x00/0x05), couleur statique du logo et de la molette.
    /// L'identifiant de transaction varie selon les générations : il est trouvé en interrogeant la souris.
    /// </summary>
    sealed class RazerMouse : GamingMouse
    {
        [Flags] enum Z { None = 0, Logo = 1, Scroll = 2, All = 4 }

        sealed class RazerModel
        {
            public readonly int MaxDpi, Interface;
            public readonly Z Zones;
            public readonly bool Poll;
            public RazerModel(int maxDpi, Z zones, bool poll, int iface) { MaxDpi = maxDpi; Zones = zones; Poll = poll; Interface = iface; }
        }

        /// <summary>Modèles connus (d'après openrazer) : DPI maximal, zones d'éclairage, fréquence réglable, interface de commande.</summary>
        static readonly Dictionary<int, RazerModel> Models = new Dictionary<int, RazerModel>
        {
            { 0x0024, new RazerModel(6400, Z.Scroll, true, 0) },  // RazerMamba2012Wired
            { 0x0025, new RazerModel(6400, Z.Scroll, true, 0) },  // RazerMamba2012Wireless
            { 0x002F, new RazerModel(6400, Z.None, true, 0) },  // RazerImperator
            { 0x0032, new RazerModel(8200, Z.None, true, 0) },  // RazerOuroboros
            { 0x0034, new RazerModel(8200, Z.None, true, 0) },  // RazerTaipan
            { 0x0039, new RazerModel(6400, Z.None, true, 0) },  // RazerOrochi2013
            { 0x0040, new RazerModel(8200, Z.None, true, 0) },  // RazerNaga2014
            { 0x0042, new RazerModel(16000, Z.None, true, 0) },  // RazerAbyssus
            { 0x0043, new RazerModel(10000, Z.Logo | Z.Scroll, true, 0) },  // RazerDeathAdderChroma
            { 0x0044, new RazerModel(16000, Z.None, true, 0) },  // RazerMambaChromaWired
            { 0x0045, new RazerModel(16000, Z.None, true, 0) },  // RazerMambaChromaWireless
            { 0x0046, new RazerModel(16000, Z.None, false, 0) },  // RazerMambaTE
            { 0x0048, new RazerModel(8200, Z.None, true, 0) },  // RazerOrochiWired
            { 0x004C, new RazerModel(16000, Z.None, false, 0) },  // RazerDiamondbackChroma
            { 0x004F, new RazerModel(2000, Z.None, true, 0) },  // RazerDeathAdder2000
            { 0x0050, new RazerModel(16000, Z.Logo | Z.Scroll, true, 0) },  // RazerNagaHexV2
            { 0x0053, new RazerModel(16000, Z.Logo | Z.Scroll, true, 0) },  // RazerNagaChroma
            { 0x0054, new RazerModel(3500, Z.Logo | Z.Scroll, true, 0) },  // RazerDeathAdder3500
            { 0x0059, new RazerModel(16000, Z.Logo | Z.Scroll, true, 0) },  // RazerLanceheadWired
            { 0x005B, new RazerModel(5000, Z.Logo | Z.Scroll, true, 0) },  // RazerAbyssusV2
            { 0x005C, new RazerModel(16000, Z.Logo | Z.Scroll, true, 0) },  // RazerDeathAdderElite
            { 0x005E, new RazerModel(2000, Z.None, true, 0) },  // RazerAbyssus2000
            { 0x0060, new RazerModel(16000, Z.Logo | Z.Scroll, true, 0) },  // RazerLanceheadTE
            { 0x0062, new RazerModel(7200, Z.None, true, 0) },  // RazerAtherisReceiver
            { 0x0064, new RazerModel(16000, Z.Logo | Z.Scroll, true, 0) },  // RazerBasilisk
            { 0x0065, new RazerModel(6400, Z.Logo, true, 0) },  // RazerBasiliskEssential
            { 0x0067, new RazerModel(16000, Z.All, true, 0) },  // RazerNagaTrinity
            { 0x006A, new RazerModel(7200, Z.Logo, true, 0) },  // RazerAbyssusEliteDVaEdition
            { 0x006B, new RazerModel(7200, Z.Logo, true, 0) },  // RazerAbyssusEssential
            { 0x006C, new RazerModel(16000, Z.Logo | Z.Scroll, true, 0) },  // RazerMambaElite
            { 0x006E, new RazerModel(6400, Z.Logo | Z.Scroll, true, 0) },  // RazerDeathAdderEssential
            { 0x0070, new RazerModel(16000, Z.Logo | Z.Scroll, true, 0) },  // RazerLanceheadWirelessWired
            { 0x0071, new RazerModel(6400, Z.Logo | Z.Scroll, true, 0) },  // RazerDeathAdderEssentialWhiteEdition
            { 0x0073, new RazerModel(16000, Z.Logo | Z.Scroll, true, 0) },  // RazerMambaWirelessWired
            { 0x0077, new RazerModel(16000, Z.None, true, 0) },  // RazerProClickReceiver
            { 0x0078, new RazerModel(16000, Z.Logo, true, 0) },  // RazerViper
            { 0x007A, new RazerModel(20000, Z.Logo, true, 0) },  // RazerViperUltimateWired
            { 0x007C, new RazerModel(20000, Z.Logo, true, 0) },  // RazerDeathAdderV2ProWired
            { 0x0080, new RazerModel(16000, Z.None, true, 0) },  // RazerProClickWired
            { 0x0083, new RazerModel(16000, Z.None, true, 0) },  // RazerBasiliskXHyperSpeed
            { 0x0084, new RazerModel(20000, Z.Logo | Z.Scroll, true, 0) },  // RazerDeathAdderV2
            { 0x0085, new RazerModel(20000, Z.Logo | Z.Scroll, true, 0) },  // RazerBasiliskV2
            { 0x0086, new RazerModel(20000, Z.Logo | Z.Scroll, true, 0) },  // RazerBasiliskUltimateWired
            { 0x008A, new RazerModel(8500, Z.Logo, true, 0) },  // RazerViperMini
            { 0x008C, new RazerModel(8500, Z.Logo, true, 0) },  // RazerDeathAdderV2Mini
            { 0x008D, new RazerModel(20000, Z.Logo | Z.Scroll, true, 0) },  // RazerNagaLeftHanded2020
            { 0x008F, new RazerModel(20000, Z.Logo | Z.Scroll, true, 0) },  // RazerNagaProWired
            { 0x0091, new RazerModel(20000, Z.Logo, true, 0) },  // RazerViper8KHz
            { 0x0094, new RazerModel(18000, Z.None, true, 0) },  // RazerOrochiV2Receiver
            { 0x0095, new RazerModel(18000, Z.None, true, 0) },  // RazerOrochiV2Bluetooth
            { 0x0096, new RazerModel(18000, Z.Scroll, true, 3) },  // RazerNagaX
            { 0x0098, new RazerModel(6400, Z.Logo, true, 0) },  // RazerDeathAdderEssential2021
            { 0x0099, new RazerModel(26000, Z.Logo | Z.Scroll, true, 3) },  // RazerBasiliskV3
            { 0x009A, new RazerModel(12000, Z.None, true, 0) },  // RazerProClickMiniReceiver
            { 0x009C, new RazerModel(14000, Z.None, true, 0) },  // RazerDeathAdderV2XHyperSpeed
            { 0x009E, new RazerModel(30000, Z.None, true, 0) },  // RazerViperMiniSEWired
            { 0x00A1, new RazerModel(8500, Z.Logo, true, 0) },  // RazerDeathAdderV2Lite
            { 0x00A3, new RazerModel(8500, Z.Logo, true, 0) },  // RazerCobra
            { 0x00A5, new RazerModel(30000, Z.None, true, 0) },  // RazerViperV2ProWired
            { 0x00A6, new RazerModel(30000, Z.None, true, 0) },  // RazerViperV2ProWireless
            { 0x00A7, new RazerModel(30000, Z.Logo, true, 0) },  // RazerNagaV2ProWired
            { 0x00AA, new RazerModel(30000, Z.Logo | Z.Scroll, true, 0) },  // RazerBasiliskV3ProWired
            { 0x00AB, new RazerModel(30000, Z.Logo | Z.Scroll, true, 0) },  // RazerBasiliskV3ProWireless
            { 0x00AF, new RazerModel(30000, Z.Logo | Z.Scroll, true, 0) },  // RazerCobraProWired
            { 0x00B0, new RazerModel(30000, Z.Logo | Z.Scroll, true, 0) },  // RazerCobraProWireless
            { 0x00B2, new RazerModel(30000, Z.None, true, 0) },  // RazerDeathAdderV3
            { 0x00B3, new RazerModel(30000, Z.None, true, 0) },  // RazerHyperPollingWirelessDongle
            { 0x00B4, new RazerModel(30000, Z.None, true, 0) },  // RazerNagaV2HyperSpeedReceiver
            { 0x00B6, new RazerModel(35000, Z.None, true, 0) },  // RazerDeathAdderV3ProWired
            { 0x00B7, new RazerModel(35000, Z.None, true, 0) },  // RazerDeathAdderV3ProWireless
            { 0x00B8, new RazerModel(30000, Z.None, true, 0) },  // RazerViperV3HyperSpeed
            { 0x00B9, new RazerModel(18000, Z.Scroll, true, 0) },  // RazerBasiliskV3XHyperSpeed
            { 0x00BE, new RazerModel(45000, Z.None, true, 0) },  // RazerDeathAdderV4ProWired
            { 0x00C0, new RazerModel(35000, Z.None, true, 0) },  // RazerViperV3ProWired
            { 0x00C2, new RazerModel(35000, Z.None, true, 0) },  // RazerDeathAdderV3ProWired_Alternate
            { 0x00C3, new RazerModel(35000, Z.None, true, 0) },  // RazerDeathAdderV3ProWireless_Alternate
            { 0x00C4, new RazerModel(26000, Z.None, true, 0) },  // RazerDeathAdderV3HyperSpeedWired
            { 0x00C5, new RazerModel(26000, Z.None, true, 0) },  // RazerDeathAdderV3HyperSpeedWireless
            { 0x00C7, new RazerModel(30000, Z.All, true, 0) },  // RazerProClickV2VerticalEditionWired
            { 0x00C8, new RazerModel(30000, Z.All, true, 0) },  // RazerProClickV2VerticalEditionWireless
            { 0x00CB, new RazerModel(35000, Z.Logo | Z.Scroll, true, 3) },  // RazerBasiliskV3_35K
            { 0x00CC, new RazerModel(35000, Z.Logo | Z.Scroll, true, 0) },  // RazerBasiliskV3Pro35KWired
            { 0x00CD, new RazerModel(35000, Z.Logo | Z.Scroll, true, 0) },  // RazerBasiliskV3Pro35KWireless
            { 0x00D0, new RazerModel(30000, Z.All, true, 0) },  // RazerProClickV2Wired
            { 0x00D1, new RazerModel(30000, Z.All, true, 0) },  // RazerProClickV2Wireless
            { 0x00D3, new RazerModel(18000, Z.All, true, 0) },  // RazerBasiliskMobileWired
            { 0x00D4, new RazerModel(18000, Z.All, true, 0) },  // RazerBasiliskMobileReceiver
            { 0x00D6, new RazerModel(35000, Z.Scroll, true, 0) },  // RazerBasiliskV3Pro35KPhantomGreenEditionWired
            { 0x00D7, new RazerModel(35000, Z.Scroll, true, 0) },  // RazerBasiliskV3Pro35KPhantomGreenEditionWireless
            { 0x00DA, new RazerModel(26000, Z.Logo, true, 0) },  // RazerCobraHyperSpeed
            { 0x00DB, new RazerModel(26000, Z.Logo, true, 0) },  // RazerCobraHyperSpeedWireless
            { 0x00DE, new RazerModel(35000, Z.None, true, 0) },  // RazerViperV3ProSEWired
            { 0x00DF, new RazerModel(35000, Z.None, true, 0) },  // RazerViperV3ProSEWireless
        };

        const byte VarStore = 0x01;

        readonly RazerLink link;
        readonly List<byte> leds = new List<byte>();

        RazerMouse(RazerLink link) { this.link = link; }

        public static RazerMouse Probe(List<HidInfo> group)
        {
            int pid = group[0].Pid;
            RazerModel model;
            Models.TryGetValue(pid, out model);
            int iface = model != null ? model.Interface : 0;
            foreach (var info in group.FindAll(x => x.FeatureLen == 91 && (x.Interface == iface || x.Interface < 0)))
            {
                var link = RazerLink.Open(info, l => ReadDpi(l) > 0);
                if (link == null) continue;
                var dev = new RazerMouse(link);
                dev.Brand = "Razer";
                dev.Name = MouseDetect.CleanName(MouseDetect.NameOf(group, "Souris Razer"), "Razer");
                dev.MaxDpi = model != null ? model.MaxDpi : 16000;
                dev.PollRates = model == null || model.Poll ? new[] { 125, 500, 1000 } : new int[0];
                var z = model != null ? model.Zones : Z.None;
                var names = new List<string>();
                if ((z & Z.Logo) != 0) { dev.leds.Add(0x04); names.Add("Logo"); }
                if ((z & Z.Scroll) != 0) { dev.leds.Add(0x01); names.Add("Molette"); }
                if ((z & Z.All) != 0) { dev.leds.Add(0x00); names.Add("Éclairage"); }
                dev.Zones = names.ToArray();
                return dev;
            }
            return null;
        }

        static int ReadDpi(RazerLink l)
        {
            var a = l.Send(0x04, 0x85, 0x07, 0x00);
            return a == null ? -1 : a[1] << 8 | a[2];
        }

        byte[] Send(byte cls, byte id, byte size, params byte[] args) { return link.Send(cls, id, size, args); }

        public override bool SetDpi(int dpi)
        {
            dpi = Clamp(dpi, 100, MaxDpi);
            return Send(0x04, 0x05, 0x07, VarStore, (byte)(dpi >> 8), (byte)dpi, (byte)(dpi >> 8), (byte)dpi, 0, 0) != null;
        }

        public override int ReadDpi() { return ReadDpi(link); }

        public override bool SetPollRate(int hz)
        {
            byte v = hz >= 1000 ? (byte)0x01 : hz >= 500 ? (byte)0x02 : (byte)0x08;
            return Send(0x00, 0x05, 0x01, v) != null;
        }

        public override bool SetZones(Color[] colors)
        {
            bool ok = true;
            for (int i = 0; i < leds.Count && i < colors.Length; i++)
            {
                ok &= link.StaticColor(leds[i], colors[i]);
            }
            return ok;
        }

        public override bool Alive() { return ReadDpi() > 0; }

        public override void Dispose() { link.Dispose(); }
    }
}
