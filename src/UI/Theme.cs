using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ControlCenterK
{
    static class Theme
    {
        // Palette courante : recalculée par Apply() à partir du thème choisi.
        public static Color Bg, Side, Card, Surface, SurfaceHi, Border, Text, Muted, Dim, Accent, OnAccent;
        public static readonly Color Green = Color.FromArgb(52, 199, 137);
        public static readonly Color Red = Color.FromArgb(255, 84, 98);

        /// <summary>Déclenché après un changement de thème (sur le thread UI).</summary>
        public static event Action Changed;

        public static readonly ThemeDef[] Presets =
        {
            new ThemeDef { Name = "Bleu nuit",  Accent = "#4C8DFF", Base = "#AAB4E1", Intensity = 1 },
            new ThemeDef { Name = "Émeraude",   Accent = "#2ECC8A", Base = "#A5C8B9", Intensity = 1 },
            new ThemeDef { Name = "Améthyste",  Accent = "#A06EFF", Base = "#BEAFE6", Intensity = 1 },
            new ThemeDef { Name = "Corail",     Accent = "#FF6E64", Base = "#D7B4B4", Intensity = 1 },
            new ThemeDef { Name = "Ambre",      Accent = "#FFB02E", Base = "#D2C3AA", Intensity = 1 },
            new ThemeDef { Name = "Rose",       Accent = "#FF5AAA", Base = "#CDAAC8", Intensity = 1 },
            new ThemeDef { Name = "Cyan",       Accent = "#28C8E6", Base = "#A0C3D2", Intensity = 1 },
            new ThemeDef { Name = "Graphite",   Accent = "#C8CDD7", Base = "#B9B9B9", Intensity = 0.9 },
        };

        static Theme() { Compute(Presets[0]); }

        static void Compute(ThemeDef t)
        {
            Color tint = FromHex(t.Base, Color.FromArgb(170, 180, 225));
            float k = (float)Math.Max(0.5, Math.Min(2.0, t.Intensity <= 0 ? 1 : t.Intensity));
            Bg = Shade(tint, 0.100f * k);
            Side = Shade(tint, 0.125f * k);
            Card = Shade(tint, 0.155f * k);
            Surface = Shade(tint, 0.210f * k);
            SurfaceHi = Shade(tint, 0.275f * k);
            Border = Shade(tint, 0.290f * k);
            Text = Mix(Color.White, tint, 0.07f);
            Muted = Mix(Color.FromArgb(150, 150, 150), tint, 0.35f);
            Dim = Mix(Color.FromArgb(90, 90, 90), tint, 0.25f);
            Accent = FromHex(t.Accent, Color.FromArgb(76, 141, 255));
            OnAccent = Luma(Accent) > 0.62 ? Color.FromArgb(20, 22, 28) : Color.White;
        }

        /// <summary>Applique un thème et recolore toutes les fenêtres ouvertes.</summary>
        public static void Apply(ThemeDef t)
        {
            var old = new[] { Bg, Side, Card, Surface, SurfaceHi, Border, Text, Muted, Dim, Accent };
            Compute(t);
            var now = new[] { Bg, Side, Card, Surface, SurfaceHi, Border, Text, Muted, Dim, Accent };
            var map = new Dictionary<int, Color>();
            for (int i = 0; i < old.Length; i++) if (!map.ContainsKey(old[i].ToArgb())) map[old[i].ToArgb()] = now[i];
            foreach (Form f in Application.OpenForms)
            {
                Recolor(f, map);
                if (f.IsHandleCreated && f.Visible) DarkTitle(f);
                f.Invalidate(true);
            }
            var h = Changed;
            if (h != null) h();
        }

        /// <summary>Remplace les anciennes couleurs de la palette par les nouvelles sur tout un arbre de contrôles.</summary>
        public static void Recolor(Control c, Dictionary<int, Color> map)
        {
            Color n;
            if (map.TryGetValue(c.BackColor.ToArgb(), out n)) c.BackColor = n;
            if (map.TryGetValue(c.ForeColor.ToArgb(), out n)) c.ForeColor = n;
            var cms = c.ContextMenuStrip;
            if (cms != null && map.TryGetValue(cms.BackColor.ToArgb(), out n)) cms.BackColor = n;
            foreach (Control child in c.Controls) Recolor(child, map);
        }

        static Color Shade(Color c, float k)
        {
            return Color.FromArgb(Clamp(c.R * k), Clamp(c.G * k), Clamp(c.B * k));
        }

        static int Clamp(float v) { return v < 0 ? 0 : v > 255 ? 255 : (int)Math.Round(v); }

        public static double Luma(Color c) { return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0; }

        public static string ToHex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        public static Color FromHex(string s, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return fallback;
                return Color.FromArgb(255, ColorTranslator.FromHtml(s.StartsWith("#") ? s : "#" + s));
            }
            catch { return fallback; }
        }

        /// <summary>Couleur à partir de teinte (0-360), saturation et luminosité (0-1).</summary>
        public static Color Hsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromArgb(Clamp((float)((r + m) * 255)), Clamp((float)((g + m) * 255)), Clamp((float)((b + m) * 255)));
        }

        public static float Scale = 1f;
        public static string IconFamily = "Segoe MDL2 Assets";

        public static void Init()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            using (var fonts = new InstalledFontCollection())
                foreach (var f in fonts.Families)
                    if (f.Name == "Segoe Fluent Icons") { IconFamily = f.Name; break; }
        }

        public static int S(float v) { return (int)Math.Round(v * Scale); }

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static Font Ui(float pt, FontStyle style = FontStyle.Regular) { return Get("Segoe UI", pt, style); }
        public static Font Semi(float pt) { return Get("Segoe UI Semibold", pt, FontStyle.Regular); }
        public static Font Icon(float pt) { return Get(IconFamily, pt, FontStyle.Regular); }

        static Font Get(string family, float pt, FontStyle style)
        {
            string key = family + "|" + pt + "|" + (int)style;
            Font f;
            if (!fonts.TryGetValue(key, out f)) { f = new Font(family, pt, style); fonts[key] = f; }
            return f;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Color c, RectangleF r, float rad, float width = 1f)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        /// <summary>Barre de titre sombre (Windows 10 2004+ / Windows 11).</summary>
        public static void DarkTitle(Form f)
        {
            int on = 1;
            Native.DwmSetWindowAttribute(f.Handle, 20, ref on, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE
            int col = ColorTranslator.ToWin32(Bg);
            Native.DwmSetWindowAttribute(f.Handle, 35, ref col, 4);  // DWMWA_CAPTION_COLOR (Windows 11)
        }

        /// <summary>Barres de défilement sombres.</summary>
        public static void DarkScroll(Control c)
        {
            Native.SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
        }

        static readonly Dictionary<string, Bitmap> glyphCache = new Dictionary<string, Bitmap>();

        /// <summary>Petite image d'icône (pour les menus).</summary>
        public static Bitmap GlyphImage(string glyph, Color color)
        {
            string key = glyph + color.ToArgb();
            Bitmap b;
            if (glyphCache.TryGetValue(key, out b)) return b;
            int sz = S(16);
            b = new Bitmap(sz, sz);
            using (var g = Graphics.FromImage(b))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var br = new SolidBrush(color))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var f = new Font(IconFamily, sz * 0.72f, GraphicsUnit.Pixel))
                    g.DrawString(glyph, f, br, new RectangleF(0, 0, sz, sz), sf);
            }
            glyphCache[key] = b;
            return b;
        }

        public static Label Label(string text, Font font, Color fore, Color back)
        {
            return new Label { Text = text, Font = font, ForeColor = fore, BackColor = back, AutoSize = true, UseMnemonic = false };
        }

        public static System.Drawing.Icon MakeIcon(int size)
        {
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float s = size;
                    FillRound(g, Accent, new RectangleF(0, 0, s - 1, s - 1), s * 0.22f);
                    float[] levels = { 0.62f, 0.32f, 0.5f };
                    for (int i = 0; i < 3; i++)
                    {
                        float x = s * (0.28f + i * 0.22f);
                        using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1f, s * 0.07f)))
                            g.DrawLine(pen, x, s * 0.2f, x, s * 0.8f);
                        float y = s * 0.2f + s * 0.6f * levels[i];
                        float w = s * 0.17f, h = Math.Max(2f, s * 0.12f);
                        FillRound(g, Color.White, new RectangleF(x - w / 2, y - h / 2, w, h), h * 0.3f);
                    }
                }
                return System.Drawing.Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
