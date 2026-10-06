using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MidiSoundController
{
    /// <summary>Grille de nuances : une colonne par teinte, une ligne par luminosité / saturation.</summary>
    class SwatchGrid : DarkControl
    {
        readonly Color[,] colors; // [ligne, colonne]
        readonly int rows, cols;
        int hoverR = -1, hoverC = -1;
        Color selected = Color.Empty;

        public event Action<Color> Picked;

        public SwatchGrid(Color[,] colors)
        {
            this.colors = colors;
            rows = colors.GetLength(0);
            cols = colors.GetLength(1);
            Cursor = Cursors.Hand;
            Size = new Size(cols * Cell, rows * Cell);
        }

        static int Cell { get { return Theme.S(30); } }

        public Color Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        /// <summary>Grille d'accents : 12 teintes + gris, 5 luminosités.</summary>
        public static Color[,] AccentShades()
        {
            double[] light = { 0.78, 0.68, 0.58, 0.48, 0.38 };
            var c = new Color[light.Length, 13];
            for (int r = 0; r < light.Length; r++)
            {
                for (int h = 0; h < 12; h++) c[r, h] = Theme.Hsl(h * 30, 0.85, light[r]);
                c[r, 12] = Theme.Hsl(220, 0.08, light[r]);
            }
            return c;
        }

        /// <summary>Grille de teintes de fond : 12 teintes + neutre, du plus discret au plus coloré.</summary>
        public static Color[,] BaseShades()
        {
            double[] sat = { 0.18, 0.35, 0.55 };
            var c = new Color[sat.Length, 13];
            for (int r = 0; r < sat.Length; r++)
            {
                for (int h = 0; h < 12; h++) c[r, h] = Theme.Hsl(h * 30, sat[r], 0.76);
                c[r, 12] = Theme.Hsl(0, 0, 0.62 + 0.07 * r);
            }
            return c;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int r = e.Y / Cell, c = e.X / Cell;
            if (r >= rows || c >= cols) r = c = -1;
            if (r != hoverR || c != hoverC) { hoverR = r; hoverC = c; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverR = hoverC = -1;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int r = e.Y / Cell, c = e.X / Cell;
            if (r < 0 || c < 0 || r >= rows || c >= cols) return;
            Selected = colors[r, c];
            if (Picked != null) Picked(colors[r, c]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            int cell = Cell, pad = Theme.S(3);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var rc = new RectangleF(c * cell + pad, r * cell + pad, cell - 2 * pad, cell - 2 * pad);
                    bool sel = colors[r, c].ToArgb() == selected.ToArgb();
                    bool hov = r == hoverR && c == hoverC;
                    if (hov && !sel) rc.Inflate(Theme.S(1), Theme.S(1));
                    Theme.FillRound(g, colors[r, c], rc, Theme.S(5));
                    if (sel)
                    {
                        var ring = RectangleF.Inflate(rc, Theme.S(2), Theme.S(2));
                        Theme.DrawRound(g, Theme.Text, ring, Theme.S(7), Math.Max(1.5f, Theme.S(2)));
                    }
                }
        }
    }

    /// <summary>Tuiles de thèmes (prédéfinis + enregistrés). Les thèmes enregistrés ont une croix pour les supprimer.</summary>
    class ThemeTiles : DarkControl
    {
        public class Tile
        {
            public ThemeDef Def;
            public bool Deletable;
        }

        public readonly List<Tile> Tiles = new List<Tile>();
        public ThemeDef Current;
        public event Action<ThemeDef> Picked;
        public event Action<ThemeDef> DeleteClicked;

        readonly List<Rectangle> rects = new List<Rectangle>();
        int hover = -1;
        bool hoverX, layingOut;

        public ThemeTiles()
        {
            Cursor = Cursors.Hand;
        }

        public void Relayout()
        {
            if (layingOut) return;
            layingOut = true;
            rects.Clear();
            int w = Theme.S(150), h = Theme.S(64), gap = Theme.S(10), x = 0, y = 0;
            foreach (var t in Tiles)
            {
                if (x > 0 && x + w > Width) { x = 0; y += h + gap; }
                rects.Add(new Rectangle(x, y, w, h));
                x += w + gap;
            }
            Height = (rects.Count == 0 ? 0 : rects[rects.Count - 1].Bottom) + 2;
            layingOut = false;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!layingOut) Relayout();
        }

        Rectangle XRect(Rectangle r)
        {
            int s = Theme.S(20);
            return new Rectangle(r.Right - s - Theme.S(4), r.Y + Theme.S(4), s, s);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = rects.FindIndex(r => r.Contains(e.Location));
            bool x = i >= 0 && Tiles[i].Deletable && XRect(rects[i]).Contains(e.Location);
            if (i != hover || x != hoverX) { hover = i; hoverX = x; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = rects.FindIndex(r => r.Contains(e.Location));
            if (i < 0) return;
            if (Tiles[i].Deletable && XRect(rects[i]).Contains(e.Location)) { if (DeleteClicked != null) DeleteClicked(Tiles[i].Def); }
            else if (Picked != null) Picked(Tiles[i].Def);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            for (int i = 0; i < Tiles.Count && i < rects.Count; i++)
            {
                var t = Tiles[i].Def;
                var r = rects[i];
                Color tint = Theme.FromHex(t.Base, Color.Gray), acc = Theme.FromHex(t.Accent, Theme.Accent);
                float k = (float)(t.Intensity <= 0 ? 1 : t.Intensity);
                Color bg = Scale(tint, 0.10f * k), card = Scale(tint, 0.21f * k);
                var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 2, r.Height - 2);
                Theme.FillRound(g, bg, rf, Theme.S(8));
                // mini aperçu : barre latérale + carte + pastille d'accent
                Theme.FillRound(g, card, new RectangleF(r.X + Theme.S(8), r.Y + Theme.S(8), Theme.S(28), r.Height - Theme.S(16)), Theme.S(4));
                Theme.FillRound(g, acc, new RectangleF(r.X + Theme.S(14), r.Y + Theme.S(14), Theme.S(16), Theme.S(16)), Theme.S(4));
                Theme.FillRound(g, card, new RectangleF(r.X + Theme.S(14), r.Y + Theme.S(36), Theme.S(16), Theme.S(4)), Theme.S(2));
                TextRenderer.DrawText(g, t.Name, Theme.Semi(9f), new Rectangle(r.X + Theme.S(44), r.Y, r.Width - Theme.S(50) - (Tiles[i].Deletable ? Theme.S(18) : 0), r.Height),
                    Theme.Mix(Color.White, tint, 0.1f), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                bool cur = Current != null && t.SameColors(Current);
                if (cur) Theme.DrawRound(g, acc, rf, Theme.S(8), Math.Max(2f, Theme.S(2)));
                else if (i == hover) Theme.DrawRound(g, Theme.Mix(bg, Color.White, 0.35f), rf, Theme.S(8), 1.2f);
                else Theme.DrawRound(g, Theme.Border, rf, Theme.S(8), 1f);

                if (Tiles[i].Deletable && i == hover)
                {
                    var xr = XRect(r);
                    if (hoverX) Theme.FillRound(g, Theme.Mix(bg, Theme.Red, 0.4f), xr, xr.Height / 2f);
                    TextRenderer.DrawText(g, Glyphs.Close, Theme.Icon(6.5f), xr, hoverX ? Color.White : Theme.Mix(Color.White, tint, 0.3f),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                }
            }
        }

        static Color Scale(Color c, float k)
        {
            return Color.FromArgb(Math.Min(255, (int)(c.R * k)), Math.Min(255, (int)(c.G * k)), Math.Min(255, (int)(c.B * k)));
        }
    }
}
