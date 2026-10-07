using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ControlCenterK
{
    /// <summary>Action d'un bouton de souris.</summary>
    public class MouseAction
    {
        public string Name { get; set; }    // nom donné au bouton par l'utilisateur
        public string Kind { get; set; }    // "", keys, macro, click, dpi_next, dpi_prev, dpi_stage, sniper, media_*, profile_*
        public string Value { get; set; }   // raccourci, macro, numéro de bouton / d'étape…

        public MouseAction() { Name = ""; Kind = ""; Value = ""; }
    }

    public class DpiStage
    {
        public bool Enabled { get; set; }
        public int Dpi { get; set; }
        public string Color { get; set; }   // couleur de l'indicateur DPI (#RRGGBB)
    }

    /// <summary>Réglages propres à un modèle de souris (clé "vid:pid").</summary>
    public class MouseDeviceConfig
    {
        public string Name { get; set; }        // dernier nom connu (pour l'affichage)
        public bool Imported { get; set; }      // réglages DPI repris de la souris au premier branchement
        public bool Advanced { get; set; }      // mode logiciel (Corsair) : éclairage + tous les boutons
        public int PollHz { get; set; }         // 0 = ne pas modifier
        public int CurrentStage { get; set; }
        public List<DpiStage> Stages { get; set; }   // 0 = sniper, 1..5
        public List<string> ZoneColors { get; set; }
        public List<string> ZoneNames { get; set; }
        public string Effect { get; set; }      // static, breathe, rainbow, off
        public int EffectSpeed { get; set; }    // 1..10

        public MouseDeviceConfig()
        {
            Name = "";
            Effect = "static";
            EffectSpeed = 5;
            CurrentStage = 1;
            Stages = new List<DpiStage>();
            ZoneColors = new List<string>();
            ZoneNames = new List<string>();
        }

        public void Fix(string[] zoneNames)
        {
            if (Stages == null) Stages = new List<DpiStage>();
            if (ZoneColors == null) ZoneColors = new List<string>();
            if (ZoneNames == null) ZoneNames = new List<string>();
            if (Effect == null) Effect = "static";
            if (Name == null) Name = "";
            if (EffectSpeed < 1 || EffectSpeed > 10) EffectSpeed = 5;
            int[] defaults = { 400, 800, 1600, 3200, 6400, 12000 };
            while (Stages.Count < CorsairMouse.StageCount)
                Stages.Add(new DpiStage { Enabled = Stages.Count < 4, Dpi = defaults[Stages.Count], Color = Stages.Count == 0 ? "#FF2020" : "#00BFFF" });
            int zones = Math.Max(6, zoneNames != null ? zoneNames.Length : 0);
            while (ZoneColors.Count < zones) ZoneColors.Add(ZoneColors.Count == 2 ? "#00BFFF" : "#4C8DFF");
            while (ZoneNames.Count < zones)
                ZoneNames.Add(zoneNames != null && ZoneNames.Count < zoneNames.Length ? zoneNames[ZoneNames.Count] : "Zone " + (ZoneNames.Count + 1));
            if (CurrentStage < 1 || CurrentStage >= CorsairMouse.StageCount) CurrentStage = 1;
        }
    }

    public class MouseConfig
    {
        // Réglages Windows (toutes les souris)
        public bool ApplyWindows { get; set; }
        public int Speed { get; set; }          // 1..20 (10 = défaut Windows)
        public bool Precision { get; set; }     // « améliorer la précision du pointeur »
        public int DoubleClick { get; set; }    // ms
        public int WheelLines { get; set; }     // -1 = un écran à la fois
        public bool SwapButtons { get; set; }

        /// <summary>Réglages par modèle de souris ("vid:pid").</summary>
        public Dictionary<string, MouseDeviceConfig> Devices { get; set; }
        /// <summary>Souris pilotée quand plusieurs souris réglables sont branchées ("vid:pid").</summary>
        public string Selected { get; set; }

        // Anciens champs (jusqu'à la version 0.3 : une seule souris Corsair) : repris dans Devices au chargement
        public bool Imported { get; set; }
        public bool Advanced { get; set; }
        public int PollHz { get; set; }
        public int CurrentStage { get; set; }
        public List<DpiStage> Stages { get; set; }
        public List<string> ZoneColors { get; set; }
        public List<string> ZoneNames { get; set; }
        public string Effect { get; set; }
        public int EffectSpeed { get; set; }

        /// <summary>Boutons : "hid:2" (milieu), "hid:3" (précédent), "hid:4" (suivant), "cor:N" (bouton Corsair n° N).</summary>
        public Dictionary<string, MouseAction> Buttons { get; set; }

        public MouseConfig()
        {
            Speed = 10;
            DoubleClick = 500;
            WheelLines = 3;
            Buttons = new Dictionary<string, MouseAction>();
            Devices = new Dictionary<string, MouseDeviceConfig>();
        }

        /// <summary>Réglages d'un modèle (créés au premier branchement).</summary>
        public MouseDeviceConfig Device(string key, string name, string[] zones)
        {
            MouseDeviceConfig d;
            if (!Devices.TryGetValue(key, out d)) Devices[key] = d = new MouseDeviceConfig();
            if (!string.IsNullOrEmpty(name)) d.Name = name;
            d.Fix(zones);
            return d;
        }

        public void Fix()
        {
            if (Devices == null) Devices = new Dictionary<string, MouseDeviceConfig>();
            // migration : les réglages de l'unique souris Corsair (Nightsword) deviennent ceux du modèle 1b1c:1b5c
            if (Devices.Count == 0 && Stages != null && Stages.Count > 0)
            {
                Devices["1b1c:1b5c"] = new MouseDeviceConfig
                {
                    Name = "Corsair Nightsword RGB", Imported = Imported, Advanced = Advanced, PollHz = PollHz, CurrentStage = CurrentStage,
                    Stages = Stages, ZoneColors = ZoneColors, ZoneNames = ZoneNames, Effect = Effect, EffectSpeed = EffectSpeed,
                };
            }
            Stages = null; ZoneColors = null; ZoneNames = null; Effect = null;
            Imported = Advanced = false; PollHz = CurrentStage = EffectSpeed = 0;
            foreach (var d in Devices.Values) d.Fix(null);
            if (Buttons == null) Buttons = new Dictionary<string, MouseAction>();
            if (Speed < 1 || Speed > 20) Speed = 10;
            foreach (var k in new List<string>(Buttons.Keys))
            {
                var a = Buttons[k];
                // les boutons standard (bits 0 à 5) ne sont plus envoyés en mode avancé : anciennes détections retirées
                int bit;
                if (a == null || (k.StartsWith("cor:") && int.TryParse(k.Substring(4), out bit) && bit < 6 && string.IsNullOrEmpty(a.Kind))) Buttons.Remove(k);
            }
        }
    }

    /// <summary>Lecture / écriture des réglages de souris de Windows.</summary>
    static class WinMouse
    {
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint ini);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, int[] value, uint ini);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfo")] static extern bool SpiSet(uint action, uint param, IntPtr value, uint ini);
        [DllImport("user32.dll")] static extern bool SwapMouseButton(bool swap);
        [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] static extern bool SetDoubleClickTime(uint ms);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);

        const uint SPI_GETMOUSE = 0x3, SPI_SETMOUSE = 0x4, SPI_GETMOUSESPEED = 0x70, SPI_SETMOUSESPEED = 0x71,
                   SPI_GETWHEELSCROLLLINES = 0x68, SPI_SETWHEELSCROLLLINES = 0x69, SAVE = 0x3; // UPDATEINIFILE | SENDCHANGE

        public static int Speed
        {
            get { int v = 10; SystemParametersInfo(SPI_GETMOUSESPEED, 0, ref v, 0); return v; }
            set { SpiSet(SPI_SETMOUSESPEED, 0, new IntPtr(Math.Max(1, Math.Min(20, value))), SAVE); }
        }

        public static bool Precision
        {
            get { var a = new int[3]; SystemParametersInfo(SPI_GETMOUSE, 0, a, 0); return a[2] != 0; }
            set { var a = value ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 }; SystemParametersInfo(SPI_SETMOUSE, 0, a, SAVE); }
        }

        public static int DoubleClick
        {
            get { return (int)GetDoubleClickTime(); }
            set { SetDoubleClickTime((uint)Math.Max(200, Math.Min(900, value))); }
        }

        public static int WheelLines
        {
            get { int v = 3; SystemParametersInfo(SPI_GETWHEELSCROLLLINES, 0, ref v, 0); return v == unchecked((int)0xFFFFFFFF) ? -1 : v; }
            set { SpiSet(SPI_SETWHEELSCROLLLINES, value < 0 ? 0xFFFFFFFF : (uint)value, IntPtr.Zero, SAVE); }
        }

        public static bool Swapped
        {
            get { return GetSystemMetrics(23) != 0; } // SM_SWAPBUTTON
            set { SwapMouseButton(value); }
        }
    }
}
