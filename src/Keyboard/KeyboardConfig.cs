using System.Collections.Generic;

namespace ControlCenterK
{
    /// <summary>Éclairage d'un modèle de clavier (clé "vid:pid").</summary>
    public class KeyboardDeviceConfig
    {
        public string Name { get; set; }
        public string Effect { get; set; }      // device, static, breathe, rainbow, wave, reactive, off
        public string Color { get; set; }       // couleur principale (#RRGGBB)
        public int Speed { get; set; }          // 1..10
        public int Brightness { get; set; }     // 10..100 %
        /// <summary>Couleurs propres à certaines touches : code HID en hexadécimal ("04") → #RRGGBB.</summary>
        public Dictionary<string, string> Keys { get; set; }
        /// <summary>Couleurs choisies pour les effets (respiration, cycle, dégradé, vague, réactif).</summary>
        public List<string> EffectColors { get; set; }

        public KeyboardDeviceConfig()
        {
            EffectColors = new List<string> { "#FF2D55", "#4C8DFF" };
            Name = "";
            Effect = "device";
            Color = "#4C8DFF";
            Speed = 5;
            Brightness = 100;
            Keys = new Dictionary<string, string>();
        }

        public void Fix()
        {
            if (Name == null) Name = "";
            if (Effect == null) Effect = "device";
            if (Color == null) Color = "#4C8DFF";
            if (Keys == null) Keys = new Dictionary<string, string>();
            if (EffectColors == null || EffectColors.Count == 0) EffectColors = new List<string> { "#FF2D55", "#4C8DFF" };
            if (Speed < 1 || Speed > 10) Speed = 5;
            if (Brightness < 10 || Brightness > 100) Brightness = 100;
        }
    }

    public class KeyboardConfig
    {
        public Dictionary<string, KeyboardDeviceConfig> Devices { get; set; }
        /// <summary>Clavier piloté quand plusieurs claviers réglables sont branchés ("vid:pid").</summary>
        public string Selected { get; set; }
        /// <summary>Macros : "key:xx" (code HID) → action. Valables pour tous les claviers.</summary>
        public Dictionary<string, MouseAction> Macros { get; set; }
        /// <summary>Palette de couleurs à glisser sur les touches.</summary>
        public List<string> Palette { get; set; }

        public KeyboardConfig()
        {
            Palette = DefaultPalette();
            Devices = new Dictionary<string, KeyboardDeviceConfig>();
            Macros = new Dictionary<string, MouseAction>();
        }

        public KeyboardDeviceConfig Device(string key, string name)
        {
            KeyboardDeviceConfig d;
            if (!Devices.TryGetValue(key, out d)) Devices[key] = d = new KeyboardDeviceConfig();
            if (!string.IsNullOrEmpty(name)) d.Name = name;
            d.Fix();
            return d;
        }

        static List<string> DefaultPalette()
        {
            return new List<string> { "#FF2020", "#FF8A00", "#FFD400", "#2BD65A", "#00D1C1", "#4C8DFF", "#8A4CFF", "#FF4CB8", "#FFFFFF" };
        }

        public void Fix()
        {
            if (Devices == null) Devices = new Dictionary<string, KeyboardDeviceConfig>();
            if (Macros == null) Macros = new Dictionary<string, MouseAction>();
            if (Palette == null) Palette = DefaultPalette();
            foreach (var d in Devices.Values) if (d != null) d.Fix();
            foreach (var k in new List<string>(Macros.Keys)) if (Macros[k] == null) Macros.Remove(k);
        }
    }
}
