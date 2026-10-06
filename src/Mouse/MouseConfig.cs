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

    public class MouseConfig
    {
        // Réglages Windows (toutes les souris)
        public bool ApplyWindows { get; set; }
        public int Speed { get; set; }          // 1..20 (10 = défaut Windows)
        public bool Precision { get; set; }     // « améliorer la précision du pointeur »
        public int DoubleClick { get; set; }    // ms
        public int WheelLines { get; set; }     // -1 = un écran à la fois
        public bool SwapButtons { get; set; }

        // Souris Corsair
        public bool Imported { get; set; }      // réglages DPI repris de la souris au premier branchement
        public bool Advanced { get; set; }      // mode logiciel : éclairage + tous les boutons
        public int PollHz { get; set; }         // 0 = ne pas modifier
        public int CurrentStage { get; set; }
        public List<DpiStage> Stages { get; set; }   // 0 = sniper, 1..5
        public List<string> ZoneColors { get; set; } // 6 zones
        public List<string> ZoneNames { get; set; }
        public string Effect { get; set; }      // static, breathe, rainbow, off
        public int EffectSpeed { get; set; }    // 1..10

        /// <summary>Boutons : "hid:2" (milieu), "hid:3" (précédent), "hid:4" (suivant), "cor:N" (bouton Corsair n° N).</summary>
        public Dictionary<string, MouseAction> Buttons { get; set; }

        public MouseConfig()
        {
            Speed = 10;
            DoubleClick = 500;
            WheelLines = 3;
            Effect = "static";
            EffectSpeed = 5;
            CurrentStage = 1;
            Stages = new List<DpiStage>();
            ZoneColors = new List<string>();
            ZoneNames = new List<string>();
            Buttons = new Dictionary<string, MouseAction>();
        }

        public void Fix()
        {
            if (Stages == null) Stages = new List<DpiStage>();
            if (ZoneColors == null) ZoneColors = new List<string>();
            if (ZoneNames == null) ZoneNames = new List<string>();
            if (Buttons == null) Buttons = new Dictionary<string, MouseAction>();
            if (Effect == null) Effect = "static";
            if (Speed < 1 || Speed > 20) Speed = 10;
            if (EffectSpeed < 1 || EffectSpeed > 10) EffectSpeed = 5;
            int[] defaults = { 400, 800, 1600, 3200, 6400, 12000 };
            while (Stages.Count < CorsairMouse.StageCount)
                Stages.Add(new DpiStage { Enabled = Stages.Count < 4, Dpi = defaults[Stages.Count], Color = Stages.Count == 0 ? "#FF2020" : "#00BFFF" });
            string[] zc = { "#4C8DFF", "#4C8DFF", "#00BFFF", "#4C8DFF", "#4C8DFF", "#4C8DFF" };
            while (ZoneColors.Count < 6) ZoneColors.Add(zc[ZoneColors.Count]);
            while (ZoneNames.Count < 6) ZoneNames.Add("Zone " + (ZoneNames.Count + 1));
            if (CurrentStage < 1 || CurrentStage >= CorsairMouse.StageCount) CurrentStage = 1;
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
