using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ControlCenterK
{
    /// <summary>Une touche du clavier affiché : code d'usage HID, code de balayage Windows et position (en unités de touche).</summary>
    sealed class KeyDef
    {
        public int Usage;       // code d'usage HID (page clavier) : identifiant commun à tous les pilotes
        public int Scan;        // code de balayage (jeu 1)
        public bool Ext;        // touche « étendue » (préfixe E0)
        public float X, Y, W = 1, H = 1;
        public string Label;    // libellé fixe (sinon : nom donné par Windows, selon la disposition AZERTY / QWERTY)

        public string Id { get { return "key:" + Usage.ToString("x2"); } }
    }

    /// <summary>Clavier complet ISO (105 touches) : position des touches et correspondance HID ↔ Windows.</summary>
    static class KeyLayout
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetKeyNameText(int lParam, StringBuilder name, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);

        public static readonly List<KeyDef> Keys = new List<KeyDef>();
        static readonly Dictionary<int, KeyDef> byUsage = new Dictionary<int, KeyDef>();
        public const float Width = 22.5f, Height = 6.5f;

        static void K(int usage, int scan, bool ext, float x, float y, float w = 1, float h = 1, string label = null)
        {
            var k = new KeyDef { Usage = usage, Scan = scan, Ext = ext, X = x, Y = y, W = w, H = h, Label = label };
            Keys.Add(k);
            byUsage[usage] = k;
        }

        static KeyLayout()
        {
            // rangée des fonctions
            K(0x29, 0x01, false, 0, 0, 1, 1, "Échap");
            for (int i = 0; i < 4; i++) K(0x3A + i, 0x3B + i, false, 2 + i, 0, 1, 1, "F" + (i + 1));
            for (int i = 0; i < 4; i++) K(0x3E + i, 0x3F + i, false, 6.5f + i, 0, 1, 1, "F" + (i + 5));
            K(0x42, 0x43, false, 11, 0, 1, 1, "F9"); K(0x43, 0x44, false, 12, 0, 1, 1, "F10");
            K(0x44, 0x57, false, 13, 0, 1, 1, "F11"); K(0x45, 0x58, false, 14, 0, 1, 1, "F12");
            K(0x46, 0x37, true, 15.25f, 0, 1, 1, "Impr"); K(0x47, 0x46, false, 16.25f, 0, 1, 1, "Défil"); K(0x48, 0x45, false, 17.25f, 0, 1, 1, "Pause");
            // chiffres
            K(0x35, 0x29, false, 0, 1.5f);
            for (int i = 0; i < 10; i++) K(0x1E + i, 0x02 + i, false, 1 + i, 1.5f);
            K(0x2D, 0x0C, false, 11, 1.5f); K(0x2E, 0x0D, false, 12, 1.5f); K(0x2A, 0x0E, false, 13, 1.5f, 2, 1, "⟵");
            K(0x49, 0x52, true, 15.25f, 1.5f, 1, 1, "Inser"); K(0x4A, 0x47, true, 16.25f, 1.5f, 1, 1, "Début"); K(0x4B, 0x49, true, 17.25f, 1.5f, 1, 1, "Pg↑");
            K(0x53, 0x45, true, 18.5f, 1.5f, 1, 1, "Verr"); K(0x54, 0x35, true, 19.5f, 1.5f, 1, 1, "/"); K(0x55, 0x37, false, 20.5f, 1.5f, 1, 1, "*"); K(0x56, 0x4A, false, 21.5f, 1.5f, 1, 1, "-");
            // rangée du haut (AZERTY : A Z E R T Y U I O P)
            K(0x2B, 0x0F, false, 0, 2.5f, 1.5f, 1, "Tab");
            int[] top = { 0x14, 0x1A, 0x08, 0x15, 0x17, 0x1C, 0x18, 0x0C, 0x12, 0x13 };
            for (int i = 0; i < 10; i++) K(top[i], 0x10 + i, false, 1.5f + i, 2.5f);
            K(0x2F, 0x1A, false, 11.5f, 2.5f); K(0x30, 0x1B, false, 12.5f, 2.5f); K(0x28, 0x1C, false, 13.75f, 2.5f, 1.25f, 2, "Entrée");
            K(0x4C, 0x53, true, 15.25f, 2.5f, 1, 1, "Suppr"); K(0x4D, 0x4F, true, 16.25f, 2.5f, 1, 1, "Fin"); K(0x4E, 0x51, true, 17.25f, 2.5f, 1, 1, "Pg↓");
            K(0x5F, 0x47, false, 18.5f, 2.5f, 1, 1, "7"); K(0x60, 0x48, false, 19.5f, 2.5f, 1, 1, "8"); K(0x61, 0x49, false, 20.5f, 2.5f, 1, 1, "9"); K(0x57, 0x4E, false, 21.5f, 2.5f, 1, 2, "+");
            // rangée du milieu
            K(0x39, 0x3A, false, 0, 3.5f, 1.75f, 1, "Verr maj");
            int[] mid = { 0x04, 0x16, 0x07, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x0F };
            for (int i = 0; i < 9; i++) K(mid[i], 0x1E + i, false, 1.75f + i, 3.5f);
            K(0x33, 0x27, false, 10.75f, 3.5f); K(0x34, 0x28, false, 11.75f, 3.5f); K(0x32, 0x2B, false, 12.75f, 3.5f);
            K(0x5C, 0x4B, false, 18.5f, 3.5f, 1, 1, "4"); K(0x5D, 0x4C, false, 19.5f, 3.5f, 1, 1, "5"); K(0x5E, 0x4D, false, 20.5f, 3.5f, 1, 1, "6");
            // rangée du bas
            K(0xE1, 0x2A, false, 0, 4.5f, 1.25f, 1, "Maj"); K(0x64, 0x56, false, 1.25f, 4.5f);
            int[] bot = { 0x1D, 0x1B, 0x06, 0x19, 0x05, 0x11, 0x10 };
            for (int i = 0; i < 7; i++) K(bot[i], 0x2C + i, false, 2.25f + i, 4.5f);
            K(0x36, 0x33, false, 9.25f, 4.5f); K(0x37, 0x34, false, 10.25f, 4.5f); K(0x38, 0x35, false, 11.25f, 4.5f);
            K(0xE5, 0x36, false, 12.25f, 4.5f, 2.75f, 1, "Maj");
            K(0x52, 0x48, true, 16.25f, 4.5f, 1, 1, "↑");
            K(0x59, 0x4F, false, 18.5f, 4.5f, 1, 1, "1"); K(0x5A, 0x50, false, 19.5f, 4.5f, 1, 1, "2"); K(0x5B, 0x51, false, 20.5f, 4.5f, 1, 1, "3"); K(0x58, 0x1C, true, 21.5f, 4.5f, 1, 2, "Entr");
            // rangée de l'espace
            K(0xE0, 0x1D, false, 0, 5.5f, 1.25f, 1, "Ctrl"); K(0xE3, 0x5B, true, 1.25f, 5.5f, 1.25f, 1, "Win"); K(0xE2, 0x38, false, 2.5f, 5.5f, 1.25f, 1, "Alt");
            K(0x2C, 0x39, false, 3.75f, 5.5f, 6.25f, 1, "Espace");
            K(0xE6, 0x38, true, 10, 5.5f, 1.25f, 1, "AltGr"); K(0xE7, 0x5C, true, 11.25f, 5.5f, 1.25f, 1, "Win"); K(0x65, 0x5D, true, 12.5f, 5.5f, 1.25f, 1, "Menu"); K(0xE4, 0x1D, true, 13.75f, 5.5f, 1.25f, 1, "Ctrl");
            K(0x50, 0x4B, true, 15.25f, 5.5f, 1, 1, "←"); K(0x51, 0x50, true, 16.25f, 5.5f, 1, 1, "↓"); K(0x4F, 0x4D, true, 17.25f, 5.5f, 1, 1, "→");
            K(0x62, 0x52, false, 18.5f, 5.5f, 2, 1, "0"); K(0x63, 0x53, false, 20.5f, 5.5f, 1, 1, ".");
        }

        public static KeyDef Get(int usage)
        {
            KeyDef k;
            return byUsage.TryGetValue(usage, out k) ? k : null;
        }

        /// <summary>Touche correspondant à un événement clavier Windows (crochet bas niveau).</summary>
        public static KeyDef FromEvent(int vk, int scan, bool ext)
        {
            if (vk == 0x13) return Get(0x48);            // Pause (même code de balayage que Verr num)
            if (vk == 0x90) return Get(0x53);            // Verr num
            foreach (var k in Keys) if (k.Scan == scan && k.Ext == ext && k.Usage != 0x48 && k.Usage != 0x53) return k;
            // certains claviers / logiciels (SteelSeries GG…) envoient un code de balayage absent ou inattendu :
            // on le déduit de la touche virtuelle
            uint sc = MapVirtualKey((uint)vk, 4 /* MAPVK_VK_TO_VSC_EX */);
            if (sc != 0)
            {
                bool e = (sc & 0xFF00) == 0xE000 || IsExtendedVk(vk);
                int code = (int)(sc & 0xFF);
                if (code != scan || e != ext)
                    foreach (var k in Keys) if (k.Scan == code && k.Ext == e && k.Usage != 0x48 && k.Usage != 0x53) return k;
            }
            return null;
        }

        static bool IsExtendedVk(int vk)
        {
            switch (vk)
            {
                case 0x21: case 0x22: case 0x23: case 0x24: case 0x25: case 0x26: case 0x27: case 0x28: // Pg, Fin, Début, flèches
                case 0x2C: case 0x2D: case 0x2E: case 0x5B: case 0x5C: case 0x5D: case 0x6F: case 0xA3: case 0xA5:
                    return true;
            }
            return false;
        }

        /// <summary>Libellé affiché : fixe, ou nom Windows de la touche (suit la disposition du clavier, ex. AZERTY).</summary>
        public static string LabelOf(KeyDef k)
        {
            if (k.Label != null) return k.Label;
            var sb = new StringBuilder(32);
            if (GetKeyNameText(k.Scan << 16 | (k.Ext ? 1 << 24 : 0), sb, sb.Capacity) > 0)
            {
                string s = sb.ToString();
                if (s.Length <= 2) return s.ToUpperInvariant();
            }
            // nom long (touche morte : « ACCENT CIRCONFLEXE »…) : caractère produit par la touche
            uint vk = MapVirtualKey((uint)k.Scan, 1);          // code de balayage → touche virtuelle
            uint ch = vk == 0 ? 0 : MapVirtualKey(vk, 2) & 0x7FFF; // touche virtuelle → caractère (bit des touches mortes retiré)
            return ch > 32 ? ((char)ch).ToString().ToUpperInvariant() : "";
        }

        public static string NameOf(int usage)
        {
            var k = Get(usage);
            if (k == null) return "Touche " + usage.ToString("X2");
            string l = LabelOf(k);
            return l.Length > 0 ? "Touche " + l : "Touche " + usage.ToString("X2");
        }

        /// <summary>Usage HID depuis un identifiant "key:xx".</summary>
        public static int UsageOf(string id)
        {
            int u;
            return id != null && id.StartsWith("key:") && int.TryParse(id.Substring(4), System.Globalization.NumberStyles.HexNumber, null, out u) ? u : -1;
        }
    }
}
