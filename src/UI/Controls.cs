using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MidiSoundController
{
    /// <summary>Base des contrôles dessinés à la main (double buffer, survol).</summary>
    class DarkControl : Control
    {
        protected bool Hover, Down;

        public DarkControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
        }

        protected Color ParentBack { get { return Parent != null ? Parent.BackColor : Theme.Bg; } }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected Graphics Prepare(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(ParentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            return g;
        }
    }

    class FlatButton : DarkControl
    {
        public bool Primary;
        public string Glyph;

        public FlatButton(string text, bool primary = false)
        {
            Text = text;
            Primary = primary;
            Cursor = Cursors.Hand;
            Height = Theme.S(32);
        }

        public void FitWidth()
        {
            Width = TextRenderer.MeasureText(Text, Font).Width + Theme.S(28) + (Glyph != null ? Theme.S(22) : 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            Color bg = Primary ? Theme.Accent : Theme.Surface;
            if (Down) bg = Theme.Mix(bg, Color.Black, 0.15f);
            else if (Hover) bg = Theme.Mix(bg, Color.White, 0.08f);
            Theme.FillRound(g, bg, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(6));
            if (!Primary) Theme.DrawRound(g, Theme.Border, new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), Theme.S(6));
            Color fg = Primary ? Theme.OnAccent : Theme.Text;
            var r = ClientRectangle;
            if (Glyph != null)
            {
                int tw = TextRenderer.MeasureText(Text, Font).Width;
                int gw = Theme.S(22);
                int x = (Width - tw - gw) / 2;
                TextRenderer.DrawText(g, Glyph, Theme.Icon(9.5f), new Rectangle(x, 0, gw, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x + gw, 0, tw + 4, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            }
            else TextRenderer.DrawText(g, Text, Font, r, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
    }

    class Toggle : DarkControl
    {
        bool on;
        public event EventHandler CheckedChanged;

        public Toggle()
        {
            Size = new Size(Theme.S(40), Theme.S(22));
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return on; }
            set { on = value; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            float h = Height - 1, w = Width - 1;
            Color track = on ? Theme.Green : Theme.SurfaceHi;
            if (Hover) track = Theme.Mix(track, Color.White, 0.08f);
            Theme.FillRound(g, track, new RectangleF(0, 0, w, h), h / 2);
            float k = h - Theme.S(6);
            float x = on ? w - k - Theme.S(3) : Theme.S(3);
            using (var b = new SolidBrush(on ? Color.White : Theme.Muted)) g.FillEllipse(b, x, Theme.S(3), k, k);
        }
    }

    /// <summary>Liste déroulante sombre (affiche un menu sombre au clic).</summary>
    class DropButton : DarkControl
    {
        public readonly List<KeyValuePair<string, string>> Items = new List<KeyValuePair<string, string>>();
        string value = "";
        public string Placeholder = "";
        public event EventHandler ValueChanged;

        public DropButton()
        {
            Height = Theme.S(32);
            Cursor = Cursors.Hand;
        }

        public string Value
        {
            get { return value; }
            set { this.value = value ?? ""; Invalidate(); }
        }

        public void Add(string val, string text) { Items.Add(new KeyValuePair<string, string>(val, text)); }

        string Display()
        {
            foreach (var kv in Items) if (kv.Key == value) return kv.Value;
            return string.IsNullOrEmpty(value) ? Placeholder : value;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var m = DarkMenu.Create();
            m.MinimumSize = new Size(Width, 0);
            foreach (var kv in Items)
            {
                string k = kv.Key;
                var it = new ToolStripMenuItem(kv.Value) { Checked = k == value };
                it.Click += (s, a) =>
                {
                    Value = k;
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                };
                m.Items.Add(it);
            }
            DarkMenu.Show(m, this, new Point(0, Height + Theme.S(2)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            Theme.FillRound(g, Hover ? Theme.SurfaceHi : Theme.Surface, r, Theme.S(6));
            Theme.DrawRound(g, Theme.Border, r, Theme.S(6));
            int p = Theme.S(12);
            TextRenderer.DrawText(g, Display(), Font, new Rectangle(p, 0, Width - p * 2 - Theme.S(16), Height),
                string.IsNullOrEmpty(value) && Placeholder != "" && Display() == Placeholder ? Theme.Muted : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Glyphs.Chevron, Theme.Icon(7.5f), new Rectangle(Width - p - Theme.S(14), 0, Theme.S(16), Height),
                Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
    }

    class NavButton : DarkControl
    {
        public string Glyph;
        bool selected;

        public NavButton(string glyph, string text)
        {
            Glyph = glyph;
            Text = text;
            Height = Theme.S(42);
            Cursor = Cursors.Hand;
            Font = Theme.Semi(10f);
        }

        public bool Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(Theme.S(10), 0, Width - Theme.S(20), Height - 1);
            if (selected) Theme.FillRound(g, Theme.Surface, r, Theme.S(8));
            else if (Hover) Theme.FillRound(g, Theme.Mix(Theme.Side, Theme.Surface, 0.5f), r, Theme.S(8));
            if (selected) Theme.FillRound(g, Theme.Accent, new RectangleF(Theme.S(10), Theme.S(11), Theme.S(3), Height - Theme.S(22)), Theme.S(2));
            Color fg = selected ? Theme.Text : Theme.Muted;
            TextRenderer.DrawText(g, Glyph, Theme.Icon(11f), new Rectangle(Theme.S(24), 0, Theme.S(24), Height), selected ? Theme.Accent : fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Theme.S(58), 0, Width - Theme.S(60), Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    /// <summary>Panneau à coins arrondis.</summary>
    class Card : Panel
    {
        public float Radius = 10;

        public Card()
        {
            DoubleBuffered = true;
            BackColor = Theme.Card;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, BackColor, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(Radius));
        }
    }

    /// <summary>Liste de "puces" représentant les cibles d'un contrôle, avec un bouton d'ajout.</summary>
    class ChipsBox : DarkControl
    {
        public class Chip
        {
            public string Glyph;
            public string Text;
        }

        public readonly List<Chip> Chips = new List<Chip>();
        public string AddText = "Ajouter";
        public event Action<int> RemoveClicked;
        public event Action<Rectangle> AddClicked;

        readonly List<Rectangle> rects = new List<Rectangle>();
        readonly List<Rectangle> xs = new List<Rectangle>();
        Rectangle addRect;
        int hoverChip = -1, hoverX = -1;
        bool hoverAdd, layingOut;

        public ChipsBox()
        {
            Height = Theme.S(40);
        }

        public void Relayout()
        {
            if (layingOut) return;
            layingOut = true;
            rects.Clear();
            xs.Clear();
            int h = Theme.S(34), gap = Theme.S(8), x = 0, y = 0;
            int maxW = Math.Max(Theme.S(200), Width);
            foreach (var c in Chips)
            {
                int tw = TextRenderer.MeasureText(c.Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
                int w = Math.Min(maxW, Theme.S(36) + tw + Theme.S(40));
                if (x > 0 && x + w > maxW) { x = 0; y += h + gap; }
                var r = new Rectangle(x, y, w, h);
                rects.Add(r);
                xs.Add(new Rectangle(r.Right - Theme.S(28), r.Y + Theme.S(5), Theme.S(24), h - Theme.S(10)));
                x += w + gap;
            }
            int aw = TextRenderer.MeasureText(AddText, Font).Width + Theme.S(42);
            if (x > 0 && x + aw > maxW) { x = 0; y += h + gap; }
            addRect = new Rectangle(x, y, aw, h);
            Height = y + h + 1;
            layingOut = false;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!layingOut) Relayout();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hc = rects.FindIndex(r => r.Contains(e.Location));
            int hx = xs.FindIndex(r => r.Contains(e.Location));
            bool ha = addRect.Contains(e.Location);
            if (hc != hoverChip || hx != hoverX || ha != hoverAdd)
            {
                hoverChip = hc; hoverX = hx; hoverAdd = ha;
                Cursor = hx >= 0 || ha ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverChip = hoverX = -1;
            hoverAdd = false;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int hx = xs.FindIndex(r => r.Contains(e.Location));
            if (hx >= 0) { if (RemoveClicked != null) RemoveClicked(hx); return; }
            if (addRect.Contains(e.Location) && AddClicked != null) AddClicked(addRect);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            float rad = Theme.S(17);
            for (int i = 0; i < Chips.Count && i < rects.Count; i++)
            {
                var r = rects[i];
                var c = Chips[i];
                Theme.FillRound(g, i == hoverChip ? Theme.SurfaceHi : Theme.Surface, new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 1), rad);
                TextRenderer.DrawText(g, c.Glyph, Theme.Icon(10f), new Rectangle(r.X + Theme.S(10), r.Y, Theme.S(22), r.Height), Theme.Accent,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, c.Text, Font, new Rectangle(r.X + Theme.S(36), r.Y, r.Width - Theme.S(66), r.Height), Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                var xr = xs[i];
                if (i == hoverX) Theme.FillRound(g, Theme.Mix(Theme.Surface, Theme.Red, 0.25f), xr, xr.Height / 2f);
                TextRenderer.DrawText(g, Glyphs.Close, Theme.Icon(7f), xr, i == hoverX ? Theme.Red : Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            }
            var ar = new RectangleF(addRect.X + 0.5f, addRect.Y + 0.5f, addRect.Width - 2, addRect.Height - 2);
            if (hoverAdd) Theme.FillRound(g, Theme.Mix(ParentBack, Theme.Accent, 0.15f), ar, rad);
            using (var p = Theme.Round(ar, rad))
            using (var pen = new Pen(Theme.Mix(ParentBack, Theme.Accent, 0.6f), 1f) { DashStyle = DashStyle.Dash })
                g.DrawPath(pen, p);
            TextRenderer.DrawText(g, Glyphs.Add, Theme.Icon(8.5f), new Rectangle(addRect.X + Theme.S(10), addRect.Y, Theme.S(20), addRect.Height), Theme.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, AddText, Font, new Rectangle(addRect.X + Theme.S(32), addRect.Y, addRect.Width - Theme.S(34), addRect.Height), Theme.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    #region Menus sombres

    static class DarkMenu
    {
        public static ContextMenuStrip Create()
        {
            var m = new ContextMenuStrip
            {
                Renderer = new DarkRenderer(),
                BackColor = Theme.Card,
                ForeColor = Theme.Text,
                Font = Theme.Ui(9.5f),
                ShowCheckMargin = false,
                ShowImageMargin = true,
            };
            return m;
        }

        public static ToolStripMenuItem Header(string text)
        {
            return new ToolStripMenuItem(text.ToUpperInvariant()) { Enabled = false, Font = Theme.Ui(7.5f, FontStyle.Bold) };
        }

        /// <summary>Ajoute un sous-menu sombre et renvoie sa collection d'éléments.</summary>
        public static ToolStripItemCollection Sub(ContextMenuStrip m, string text, string glyph)
        {
            var it = SubItem(text, glyph);
            m.Items.Add(it);
            return it.DropDownItems;
        }

        public static ToolStripMenuItem SubItem(string text, string glyph)
        {
            var it = new ToolStripMenuItem(text, Theme.GlyphImage(glyph, Theme.Muted));
            var dd = (ToolStripDropDownMenu)it.DropDown;
            dd.Renderer = new DarkRenderer();
            dd.BackColor = Theme.Card;
            dd.ForeColor = Theme.Text;
            dd.Font = Theme.Ui(9.5f);
            dd.ShowImageMargin = true;
            dd.ShowCheckMargin = false;
            dd.MaximumSize = new Size(Theme.S(600), Screen.PrimaryScreen.WorkingArea.Height - Theme.S(40));
            return it;
        }

        public static ToolStripMenuItem Item(string text, string glyph, Action onClick)
        {
            var it = new ToolStripMenuItem(text, glyph != null ? Theme.GlyphImage(glyph, Theme.Muted) : null);
            it.Click += (s, e) => onClick();
            return it;
        }

        /// <summary>Affiche le menu et le détruit une fois fermé.</summary>
        public static void Show(ContextMenuStrip m, Control owner, Point p)
        {
            m.Closed += (s, e) => { if (owner.IsHandleCreated) owner.BeginInvoke(new Action(m.Dispose)); };
            m.Show(owner, p);
        }
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.SurfaceHi; } }
        public override Color MenuItemBorder { get { return Theme.SurfaceHi; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Card; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.SurfaceHi; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.SurfaceHi; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.SurfaceHi; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.SurfaceHi; } }
        public override Color CheckBackground { get { return Theme.Accent; } }
        public override Color CheckSelectedBackground { get { return Theme.Accent; } }
        public override Color CheckPressedBackground { get { return Theme.Accent; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Dim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            r.Inflate(Theme.S(2), Theme.S(2));
            Theme.FillRound(g, Theme.Accent, r, Theme.S(4));
            if (e.Item.Image == null)
                TextRenderer.DrawText(g, Glyphs.Check, Theme.Icon(8f), r, Theme.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    #endregion

    static class InputBox
    {
        public static string Show(IWin32Window owner, string title, string prompt, string initial = "")
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.BackColor = Theme.Card;
                f.Font = Theme.Ui(9.5f);
                f.ClientSize = new Size(Theme.S(440), Theme.S(150));
                f.HandleCreated += (s, e) => Theme.DarkTitle(f);

                var lbl = Theme.Label(prompt, f.Font, Theme.Text, Theme.Card);
                lbl.Location = new Point(Theme.S(20), Theme.S(18));
                var box = new Panel { BackColor = Theme.Surface, Bounds = new Rectangle(Theme.S(20), Theme.S(48), Theme.S(400), Theme.S(32)) };
                var tb = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = f.Font };
                tb.Bounds = new Rectangle(Theme.S(10), (box.Height - tb.PreferredHeight) / 2, box.Width - Theme.S(20), tb.PreferredHeight);
                box.Controls.Add(tb);
                var ok = new FlatButton("Valider", true);
                var cancel = new FlatButton("Annuler");
                ok.Width = cancel.Width = Theme.S(100);
                ok.Location = new Point(Theme.S(320), Theme.S(100));
                cancel.Location = new Point(Theme.S(210), Theme.S(100));
                ok.Click += (s, e) => { f.DialogResult = DialogResult.OK; };
                cancel.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; };
                tb.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; f.DialogResult = DialogResult.OK; }
                    else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; f.DialogResult = DialogResult.Cancel; }
                };
                f.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
                tb.Text = initial ?? "";
                f.Shown += (s, e) => { tb.Focus(); tb.SelectAll(); };
                return f.ShowDialog(owner) == DialogResult.OK ? tb.Text.Trim() : null;
            }
        }
    }
}
