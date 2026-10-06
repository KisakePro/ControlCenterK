using System;
using System.Collections.Generic;

namespace ControlCenterK
{
    /// <summary>
    /// Reconnaissance des périphériques virtuels (câbles audio, Voicemeeter, Elgato Wave Link…).
    /// Un "câble" est une paire : ce qui est joué sur sa sortie (ex. « CABLE Input ») ressort sur son entrée (« CABLE Output »).
    /// </summary>
    static class VirtualDevices
    {
        static readonly string[] Markers = { "VB-Audio", "CABLE", "Voicemeeter", "VAIO", "Virtual", "Steam Streaming", "Sonar", "NVIDIA Broadcast" };

        public const string VbCableUrl = "https://vb-audio.com/Cable/";

        public static bool IsVirtual(string name)
        {
            if (name == null) return false;
            foreach (var m in Markers)
                if (name.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>Vrai pour les câbles VB-Audio "purs" (pas besoin d'un logiciel tiers pour transporter le son).</summary>
        public static bool IsCable(string name)
        {
            return name != null && name.IndexOf("Voicemeeter", StringComparison.OrdinalIgnoreCase) < 0 &&
                   (name.IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string Brand(string name)
        {
            if (name == null) return "";
            if (name.IndexOf("Voicemeeter", StringComparison.OrdinalIgnoreCase) >= 0) return "Voicemeeter";
            if (name.IndexOf("Elgato", StringComparison.OrdinalIgnoreCase) >= 0) return "Elgato Wave Link";
            if (IsCable(name)) return "VB-CABLE";
            if (name.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0) return "Steam";
            return "Virtuel";
        }

        /// <summary>Retrouve l'autre extrémité d'un câble : « X Input (Y) » ↔ « X Output (Y) ».</summary>
        public static DeviceInfo Twin(DeviceInfo d, List<DeviceInfo> others)
        {
            string from = d.Flow == Flow.Render ? "Input" : "Output", to = d.Flow == Flow.Render ? "Output" : "Input";
            int i = d.Name.IndexOf(from, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            string want = d.Name.Substring(0, i) + to + d.Name.Substring(i + from.Length);
            return others.Find(o => string.Equals(o.Name, want, StringComparison.OrdinalIgnoreCase));
        }

        public class Cable
        {
            public DeviceInfo Play;     // sortie Windows : les logiciels y jouent leur son
            public DeviceInfo Record;   // entrée Windows : les logiciels y lisent le son (comme un micro)
        }

        /// <summary>Paires de câbles + points virtuels isolés (sorties seules, entrées seules).</summary>
        public static void Scan(List<DeviceInfo> outs, List<DeviceInfo> ins,
            out List<Cable> cables, out List<DeviceInfo> playOnly, out List<DeviceInfo> recordOnly)
        {
            cables = new List<Cable>();
            playOnly = new List<DeviceInfo>();
            recordOnly = new List<DeviceInfo>();
            var used = new HashSet<string>();
            foreach (var o in outs)
            {
                if (!IsVirtual(o.Name)) continue;
                var twin = Twin(o, ins);
                if (twin != null) { cables.Add(new Cable { Play = o, Record = twin }); used.Add(twin.Id); }
                else playOnly.Add(o);
            }
            foreach (var i in ins)
                if (IsVirtual(i.Name) && !used.Contains(i.Id)) recordOnly.Add(i);
        }
    }
}
