using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Clavier dessiné à l'écran : couleur de chaque touche, sélection au clic (glisser pour en sélectionner plusieurs).</summary>
    class KeyboardView : DarkControl
    {
        public readonly HashSet<int> Selected = new HashSet<int>();
        /// <summary>Touches éclairables (les autres sont grisées).</summary>
        public HashSet<int> Lit = new HashSet<int>();
        /// <summary>Couleur affichée pour une touche.</summary>
        public Func<int, Color> ColorOf = u => Theme.Accent;
        public event Action SelectionChanged;
        /// <summary>Couleur déposée sur des touches (glisser-déposer depuis la palette).</summary>
        public event Action<IEnumerable<int>, Color> KeysPainted;

        readonly HashSet<int> painted = new HashSet<int>();
        int dropKey = -1;

        readonly Dictionary<int, int> flash = new Dictionary<int, int>();
        readonly Timer flashTimer = new Timer { Interval = 60 };
        bool dragging, dragSelect;

        public KeyboardView()
        {
            Height = Theme.S(230);
            Cursor = Cursors.Hand;
            AllowDrop = true;
            flashTimer.Tick += (s, e) =>
            {
                int now = Environment.TickCount;
                lock (flash) foreach (var k in new List<int>(flash.Keys)) if (now - flash[k] > 500) flash.Remove(k);
                Invalidate();
                lock (flash) if (flash.Count == 0) flashTimer.Stop();
            };
            Disposed += (s, e) => flashTimer.Dispose();
        }

        /// <summary>Fait briller une touche quelques instants (touche pressée).</summary>
        public void Flash(int usage)
        {
            lock (flash) flash[usage] = Environment.TickCount;
            if (!flashTimer.Enabled) flashTimer.Start();
        }

        float Unit { get { return Math.Min((Width - 2f) / KeyLayout.Width, (Height - 2f) / KeyLayout.Height); } }

        RectangleF Rect(KeyDef k)
        {
            float u = Unit, ox = (Width - KeyLayout.Width * u) / 2, gap = Math.Max(1.5f, u * 0.08f);
            return new RectangleF(ox + k.X * u + gap / 2, 1 + k.Y * u + gap / 2, k.W * u - gap, k.H * u - gap);
        }

        KeyDef Hit(Point p)
        {
            foreach (var k in KeyLayout.Keys) if (Rect(k).Contains(p)) return k;
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var k = Hit(e.Location);
            if (k == null || !Lit.Contains(k.Usage)) return;
            dragging = true;
            dragSelect = !Selected.Contains(k.Usage);
            Apply(k);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            var k = Hit(e.Location);
            if (k != null && Lit.Contains(k.Usage)) Apply(k);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            painted.Clear();
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (!e.Data.GetDataPresent(typeof(Color))) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Copy;
            var k = Hit(PointToClient(new Point(e.X, e.Y)));
            int u = k != null && Lit.Contains(k.Usage) ? k.Usage : -1;
            if (u != dropKey) { dropKey = u; Invalidate(); }
            // Ctrl maintenu : chaque touche survolée est peinte (on « dessine » avec la couleur)
            if (u >= 0 && (e.KeyState & 8) != 0 && painted.Add(u)) PaintKeys(new[] { u }, (Color)e.Data.GetData(typeof(Color)));
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            dropKey = -1;
            Invalidate();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            dropKey = -1;
            if (!e.Data.GetDataPresent(typeof(Color))) return;
            var k = Hit(PointToClient(new Point(e.X, e.Y)));
            if (k == null || !Lit.Contains(k.Usage)) { Invalidate(); return; }
            // déposée sur une touche sélectionnée : toute la sélection prend la couleur
            IEnumerable<int> keys = Selected.Contains(k.Usage) ? (IEnumerable<int>)new List<int>(Selected) : new[] { k.Usage };
            PaintKeys(keys, (Color)e.Data.GetData(typeof(Color)));
        }

        void PaintKeys(IEnumerable<int> keys, Color c)
        {
            var h = KeysPainted;
            if (h != null) h(keys, c);
            Invalidate();
        }

        void Apply(KeyDef k)
        {
            bool changed = dragSelect ? Selected.Add(k.Usage) : Selected.Remove(k.Usage);
            if (!changed) return;
            Invalidate();
            var h = SelectionChanged;
            if (h != null) h();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            float u = Unit;
            using (var font = new Font(Theme.Ui(8f).FontFamily, Math.Max(6f, u * 0.26f), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                int now = Environment.TickCount;
                foreach (var k in KeyLayout.Keys)
                {
                    var r = Rect(k);
                    bool lit = Lit.Contains(k.Usage);
                    Color c = lit ? ColorOf(k.Usage) : Theme.Surface;
                    int at;
                    bool flashing;
                    lock (flash) flashing = flash.TryGetValue(k.Usage, out at);
                    // fond : couleur de la touche atténuée, plus vive au passage d'une touche pressée
                    Color fill = lit ? Theme.Mix(Theme.Surface, c, flashing ? 0.9f : 0.55f) : Theme.Surface;
                    Theme.FillRound(g, fill, r, Math.Max(2f, u * 0.12f));
                    if (Selected.Contains(k.Usage) || k.Usage == dropKey)
                        using (var pen = new Pen(Theme.Text, Math.Max(1.5f, u * 0.07f)))
                            g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                    string label = KeyLayout.LabelOf(k);
                    if (label.Length > 0)
                    {
                        Color fg = lit ? (fill.GetBrightness() > 0.55f ? Color.Black : Color.White) : Theme.Dim;
                        TextRenderer.DrawText(g, label, font, Rectangle.Round(r), fg,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    }
                }
            }
        }
    }
}
