using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>
    /// Nuancier : pastilles de couleur + tuile « ajouter ». Les pastilles se glissent (glisser-déposer) vers les touches
    /// du clavier ou vers un autre nuancier ; clic droit pour retirer une couleur.
    /// </summary>
    class Swatches : DarkControl
    {
        public readonly List<string> Items;
        public int Columns = 2, MinCount = 0;
        public int Size_ = Theme.S(30), Gap = Theme.S(6);
        /// <summary>Clic sur une pastille (sans glisser).</summary>
        public event Action<int, Color> ItemClicked;
        /// <summary>La liste a changé (ajout, retrait, remplacement par dépôt).</summary>
        public event Action ListChanged;

        Point downAt;
        int downIndex = -1, dropIndex = -1;

        public Swatches(List<string> items)
        {
            Items = items;
            AllowDrop = true;
            Cursor = Cursors.Hand;
        }

        public void FitHeight()
        {
            int rows = (Items.Count + 1 + Columns - 1) / Columns;
            Size = new Size(Columns * (Size_ + Gap) - Gap + 2, rows * (Size_ + Gap) - Gap + 2);
        }

        Rectangle Cell(int i)
        {
            return new Rectangle(1 + i % Columns * (Size_ + Gap), 1 + i / Columns * (Size_ + Gap), Size_, Size_);
        }

        int Hit(Point p)
        {
            for (int i = 0; i <= Items.Count; i++) if (Cell(i).Contains(p)) return i; // Items.Count : tuile « ajouter »
            return -1;
        }

        Color ColorAt(int i) { return Theme.FromHex(Items[i], Color.White); }

        void Raise()
        {
            FitHeight();
            Invalidate();
            var h = ListChanged;
            if (h != null) h();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = Hit(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (i >= 0 && i < Items.Count && Items.Count > MinCount) { Items.RemoveAt(i); Raise(); }
                return;
            }
            downIndex = i;
            downAt = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (downIndex < 0 || downIndex >= Items.Count || e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - downAt.X) + Math.Abs(e.Y - downAt.Y) < Theme.S(5)) return;
            var c = ColorAt(downIndex);
            downIndex = -1;
            DoDragDrop(c, DragDropEffects.Copy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = downIndex;
            downIndex = -1;
            if (e.Button != MouseButtons.Left || i < 0 || Hit(e.Location) != i) return;
            if (i == Items.Count)
            {
                using (var dlg = new ColorDialog { FullOpen = true, AnyColor = true })
                    if (dlg.ShowDialog(FindForm()) == DialogResult.OK) { Items.Add(Theme.ToHex(dlg.Color)); Raise(); }
                return;
            }
            var h = ItemClicked;
            if (h != null) h(i, ColorAt(i));
        }

        /// <summary>Remplace une couleur (clic sur une pastille d'un nuancier modifiable).</summary>
        public void Edit(int i)
        {
            using (var dlg = new ColorDialog { FullOpen = true, AnyColor = true, Color = ColorAt(i) })
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK) { Items[i] = Theme.ToHex(dlg.Color); Raise(); }
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            e.Effect = e.Data.GetDataPresent(typeof(Color)) ? DragDropEffects.Copy : DragDropEffects.None;
            int i = Hit(PointToClient(new Point(e.X, e.Y)));
            if (i != dropIndex) { dropIndex = i; Invalidate(); }
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            dropIndex = -1;
            Invalidate();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            dropIndex = -1;
            if (!e.Data.GetDataPresent(typeof(Color))) return;
            var c = (Color)e.Data.GetData(typeof(Color));
            int i = Hit(PointToClient(new Point(e.X, e.Y)));
            if (i >= 0 && i < Items.Count) Items[i] = Theme.ToHex(c);   // déposée sur une pastille : la remplace
            else Items.Add(Theme.ToHex(c));                            // ailleurs : ajoutée
            Raise();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            for (int i = 0; i < Items.Count; i++)
            {
                var r = Cell(i);
                Theme.FillRound(g, ColorAt(i), r, Theme.S(6));
                if (i == dropIndex)
                    using (var pen = new Pen(Theme.Text, 2)) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
            }
            var add = Cell(Items.Count);
            Theme.FillRound(g, dropIndex == Items.Count ? Theme.SurfaceHi : Theme.Surface, add, Theme.S(6));
            TextRenderer.DrawText(g, Glyphs.Add, Theme.Icon(10f), add, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
