using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.Win32.SafeHandles;

namespace ControlCenterK
{
    /// <summary>
    /// Claviers SteelSeries Apex à éclairage par touche (protocoles documentés par le projet OpenRGB).
    /// Apex M750 : une grille de 6 × 22 positions envoyée en un rapport « feature » de 513 octets (interface 2).
    /// Apex 5 / 7 / 9 / Pro : liste « code HID, R, G, B » en un rapport « feature » (interface 1).
    /// </summary>
    sealed class SteelSeriesKeyboard : RgbKeyboard
    {
        enum Proto { ApexM, Gen1, Gen2, Gen3 }

        // Apex M750 (ANSI / US) : position dans la grille (rangée du bas en premier) → code HID (0 : pas de touche).
        // La case 11 de la rangée du bas est Fn sur les modèles US, Menu sur les modèles ISO : elle suit la touche Menu.
        static readonly int[] M750Grid =
        {
            0xE0, 0xE3, 0xE2, 0, 0x2C, 0, 0, 0, 0, 0xE6, 0xE7, 0x65, 0xE4, 0, 0, 0x50, 0x51, 0x4F, 0, 0x62, 0, 0x63,
            0xE1, 0x1D, 0x1B, 0x06, 0x19, 0x05, 0x11, 0x10, 0x36, 0x37, 0x38, 0, 0xE5, 0, 0, 0, 0x52, 0, 0x59, 0x5A, 0x5B, 0x58,
            0x39, 0x04, 0x16, 0x07, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x0F, 0x33, 0x34, 0x32, 0x28, 0, 0, 0, 0, 0x5C, 0x5D, 0x5E, 0,
            0x2B, 0x14, 0x1A, 0x08, 0x15, 0x17, 0x1C, 0x18, 0x0C, 0x12, 0x13, 0x2F, 0x30, 0, 0x31, 0x4C, 0x4D, 0x4E, 0x5F, 0x60, 0x61, 0x57,
            0x35, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x2D, 0x2E, 0, 0x2A, 0x49, 0x4A, 0x4B, 0x53, 0x54, 0x55, 0x56,
            0x29, 0x3A, 0x3B, 0x3C, 0x3D, 0, 0x3E, 0x3F, 0x40, 0x41, 0, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0, 0, 0, 0,
        };

        // Apex M750 ISO (AZERTY, QWERTZ…) : la rangée Maj commence par « < » et décale Z(W)…/ d'une case (relevé sur un M750 AZERTY)
        static readonly int[] M750GridIso = IsoGrid();

        static int[] IsoGrid()
        {
            var g = (int[])M750Grid.Clone();
            int[] row = { 0xE1, 0x64, 0x1D, 0x1B, 0x06, 0x19, 0x05, 0x11, 0x10, 0x36, 0x37, 0x38 };
            for (int i = 0; i < row.Length; i++) g[22 + i] = row[i];
            return g;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);

        /// <summary>Clavier ISO (touche « < » à gauche) sauf si la disposition Windows est l'anglais US.</summary>
        static bool IsIso()
        {
            int lang = (int)((long)GetKeyboardLayout(0) & 0xFFFF);
            return lang != 0x0409 && lang != 0x1009 /* anglais (Canada) */;
        }

        // Apex 5 / 7 / 9 / Pro : codes HID adressables (liste d'OpenRGB)
        static readonly int[] ApexKeys =
        {
            0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
            0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x2B,
            0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40,
            0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50, 0x51, 0x52, 0x64, 0xE0,
            0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0x31, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E,
            0x5F, 0x60, 0x61, 0x62, 0x63,
        };

        sealed class Model
        {
            public string Name; public int Pid, Iface, Length; public Proto Proto; public bool Tested;
        }

        static readonly Model[] Models =
        {
            new Model { Name = "SteelSeries Apex M750", Pid = 0x0616, Iface = 2, Length = 513, Proto = Proto.ApexM, Tested = true },
            new Model { Name = "SteelSeries Apex 5", Pid = 0x161C, Iface = 1, Length = 643, Proto = Proto.Gen1 },
            new Model { Name = "SteelSeries Apex 7", Pid = 0x1612, Iface = 1, Length = 643, Proto = Proto.Gen1 },
            new Model { Name = "SteelSeries Apex 7 TKL", Pid = 0x1618, Iface = 1, Length = 643, Proto = Proto.Gen1 },
            new Model { Name = "SteelSeries Apex Pro", Pid = 0x1610, Iface = 1, Length = 643, Proto = Proto.Gen1 },
            new Model { Name = "SteelSeries Apex Pro TKL", Pid = 0x1614, Iface = 1, Length = 643, Proto = Proto.Gen1 },
            new Model { Name = "SteelSeries Apex Pro 3", Pid = 0x1640, Iface = 1, Length = 643, Proto = Proto.Gen1 },
            new Model { Name = "SteelSeries Apex 9 TKL", Pid = 0x1634, Iface = 1, Length = 513, Proto = Proto.Gen2 },
            new Model { Name = "SteelSeries Apex 9 Mini", Pid = 0x1620, Iface = 1, Length = 513, Proto = Proto.Gen2 },
            new Model { Name = "SteelSeries Apex Pro TKL 2023", Pid = 0x1628, Iface = 1, Length = 643, Proto = Proto.Gen2 },
            new Model { Name = "SteelSeries Apex Pro TKL Gen 3", Pid = 0x1642, Iface = 1, Length = 643, Proto = Proto.Gen3 },
        };

        readonly Model model;
        readonly int[] grid;
        readonly SafeFileHandle h;
        readonly object io = new object();
        bool started;

        SteelSeriesKeyboard(Model m, SafeFileHandle h)
        {
            model = m;
            this.h = h;
            Name = m.Name;
            Brand = "SteelSeries";
            Experimental = !m.Tested;
            var keys = new List<int>();
            grid = IsIso() ? M750GridIso : M750Grid;
            foreach (int k in m.Proto == Proto.ApexM ? grid : ApexKeys) if (k > 0 && k < 0x100 && !keys.Contains(k)) keys.Add(k);
            Keys = keys.ToArray();
        }

        public static SteelSeriesKeyboard Probe(List<HidInfo> group)
        {
            var m = Array.Find(Models, x => x.Pid == group[0].Pid);
            if (m == null) return null;
            var info = group.Find(x => x.Interface == m.Iface && x.FeatureLen >= m.Length) ?? group.Find(x => x.Interface == m.Iface && x.FeatureLen > 64);
            if (info == null) return null;
            var hd = Hid.Open(info.Path, true);
            if (hd.IsInvalid) { hd.Dispose(); return null; }
            return new SteelSeriesKeyboard(m, hd);
        }

        bool Feature(byte[] buf)
        {
            lock (io) return !h.IsInvalid && Hid.HidD_SetFeature(h, buf, buf.Length);
        }

        /// <summary>Passe le clavier en éclairage piloté (une seule fois).</summary>
        void Start()
        {
            if (started) return;
            started = true;
            if (model.Proto == Proto.ApexM)
            {
                var b = new byte[513];
                b[4] = 0x01; b[6] = 0x85; Feature(b);
                b = new byte[513];
                b[4] = 0x03; b[5] = 0x01; b[7] = 0xFF; Feature(b);
                b = new byte[513];
                b[4] = 0x01; b[6] = 0x85; Feature(b);
            }
            else if (model.Proto == Proto.Gen3)
            {
                var b = new byte[65];
                b[1] = 0x4B;
                Feature(b);
            }
        }

        public override bool SetKeys(Dictionary<int, Color> colors)
        {
            Start();
            var buf = new byte[model.Length];
            Color c;
            if (model.Proto == Proto.ApexM)
            {
                buf[3] = 0x01; buf[4] = 0x8E; buf[5] = 0x01; buf[6] = 0x03; buf[7] = 0x06; buf[8] = 0x16;
                for (int i = 0; i < grid.Length; i++)
                {
                    int k = grid[i];
                    if (k == 0 || !colors.TryGetValue(k, out c)) continue;
                    buf[9 + i * 3] = c.R;
                    buf[10 + i * 3] = c.G;
                    buf[11 + i * 3] = c.B;
                }
                return Feature(buf);
            }
            int n = 0;
            buf[1] = model.Proto == Proto.Gen1 ? (byte)0x3A : (byte)0x40;
            foreach (int k in ApexKeys)
            {
                if ((n + 1) * 4 + 3 > model.Length) break;
                colors.TryGetValue(k, out c);
                buf[3 + n * 4] = (byte)k;
                buf[4 + n * 4] = c.R;
                buf[5 + n * 4] = c.G;
                buf[6 + n * 4] = c.B;
                n++;
            }
            buf[2] = (byte)n;
            return Feature(buf);
        }

        public override void Release()
        {
            if (!started || model.Proto == Proto.ApexM) return; // l'Apex M750 n'a pas de commande de retour (il reprend au rebranchement)
            var b = new byte[65];
            b[1] = model.Proto == Proto.Gen3 ? (byte)0x41 : (byte)0x3B;
            lock (io) Hid.Write(h, b, 65);
            started = false;
        }

        public override bool Alive()
        {
            return !h.IsInvalid && Hid.Paths("pid_" + model.Pid.ToString("x4")).Count > 0;
        }

        public override void Dispose() { h.Dispose(); }
    }
}
