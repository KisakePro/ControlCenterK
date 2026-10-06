using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Dessin interactif du contrôleur MIDI : clic = sélection, affichage en direct des faders / knobs / LED.</summary>
    class ControllerView : DarkControl
    {
        readonly Engine engine;
        string selected, hover;
        float sc = 1f;
        PointF off;

        public event Action<string> ControlClicked;
        public Func<string, string> StripLabel;
        public Func<string, bool> IsAssigned;

        static readonly StringFormat Center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        static readonly StringFormat LeftFmt = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public ControllerView(Engine engine)
        {
            this.engine = engine;
        }

        public string Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        Font Px(string family, float px, FontStyle st = FontStyle.Regular)
        {
            string key = family + (int)(px * 4) + st;
            Font f;
            if (!fonts.TryGetValue(key, out f)) { f = new Font(family, Math.Max(1f, px), st, GraphicsUnit.Pixel); fonts[key] = f; }
            return f;
        }

        protected override void OnResize(EventArgs e)
        {
            foreach (var f in fonts.Values) f.Dispose();
            fonts.Clear();
            base.OnResize(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (var f in fonts.Values) f.Dispose(); fonts.Clear(); }
            base.Dispose(disposing);
        }

        void Calc()
        {
            float pad = Theme.S(2);
            sc = Math.Max(0.1f, Math.Min((Width - 2 * pad) / Controllers.Current.W, (Height - 2 * pad) / Controllers.Current.H));
            off = new PointF((Width - Controllers.Current.W * sc) / 2, (Height - Controllers.Current.H * sc) / 2);
        }

        RectangleF Map(RectangleF r)
        {
            return new RectangleF(off.X + r.X * sc, off.Y + r.Y * sc, r.Width * sc, r.Height * sc);
        }

        string HitTest(Point p)
        {
            foreach (var d in Controllers.Current.Controls)
            {
                var r = Map(d.Rect);
                r.Inflate(4 * sc, 4 * sc);
                if (r.Contains(p)) return d.Id;
            }
            return null;
        }

        // --- Manipulation à la souris : clic gauche = agir sur le contrôle, clic droit = l'éditer ---
        string active;          // contrôle en cours de manipulation (clic gauche maintenu)
        int dragStartY, dragStartValue;

        static int Clamp(int v) { return v < 0 ? 0 : v > 127 ? 127 : v; }

        int FaderValueAt(ControlDef d, int y)
        {
            var r = Map(d.Rect);
            float top = r.Y + 12 * sc, bot = r.Bottom - 12 * sc;
            return Clamp((int)Math.Round((bot - y) / Math.Max(1f, bot - top) * 127));
        }

        void Send(string id, int value)
        {
            if (engine.GetValue(id) == value) return;
            engine.SetFromUi(id, value);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (active != null)
            {
                var d = Controllers.Get(active);
                if (d.Kind == ControlKind.Fader) Send(active, FaderValueAt(d, e.Y));
                else if (d.Kind == ControlKind.Knob) Send(active, Clamp(dragStartValue + (int)Math.Round((dragStartY - e.Y) * 127.0 / Theme.S(160))));
                return;
            }
            string h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                var hd = Controllers.Get(h);
                Cursor = hd == null ? Cursors.Default : hd.Kind == ControlKind.Button ? Cursors.Hand : Cursors.SizeNS;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Focus(); // pour recevoir la molette
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || active == null) return;
            var d = Controllers.Get(active);
            if (d.Kind == ControlKind.Button) engine.SetFromUi(active, 0); // relâchement du bouton
            active = null;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var d = Controllers.Get(HitTest(e.Location));
            if (d == null || d.Kind == ControlKind.Button) return;
            int step = (ModifierKeys & Keys.Shift) != 0 ? 1 : 4;
            int cur = Math.Max(0, engine.GetValue(d.Id));
            Send(d.Id, Clamp(cur + Math.Sign(e.Delta) * step));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = null;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            string h = HitTest(e.Location);
            if (h == null) return;
            if (e.Button == MouseButtons.Right)
            {
                if (ControlClicked != null) ControlClicked(h); // clic droit : éditer ce contrôle
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            var d = Controllers.Get(h);
            active = h;
            Capture = true;
            dragStartY = e.Y;
            dragStartValue = Math.Max(0, engine.GetValue(h));
            if (d.Kind == ControlKind.Button) engine.SetFromUi(h, 127);  // appui
            else if (d.Kind == ControlKind.Fader) Send(h, FaderValueAt(d, e.Y));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(ParentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Calc();

            // Corps du contrôleur
            var model = Controllers.Current;
            var body = new RectangleF(off.X, off.Y, model.W * sc, model.H * sc);
            using (var p = Theme.Round(body, 16 * sc))
            using (var br = new LinearGradientBrush(body, Theme.Card, Theme.Mix(Theme.Bg, Color.Black, 0.35f), 90f))
            {
                g.FillPath(br, p);
                using (var pen = new Pen(Theme.Border, Math.Max(1f, 1.5f * sc))) g.DrawPath(pen, p);
            }

            DrawLabel(g, model.Title, model.TitleRect, 15, Theme.Muted, true, false);
            using (var pen = new Pen(Color.FromArgb(40, 43, 52), Math.Max(1f, 1.2f * sc)))
                foreach (var l in model.Lines)
                    g.DrawLine(pen, off.X + l.X * sc, off.Y + l.Y * sc, off.X + l.Width * sc, off.Y + l.Height * sc);
            foreach (var t in model.Texts) DrawLabel(g, t.Value, t.Key, 10, Theme.Dim, true, false);

            // Étiquettes des cibles (style "scribble strip")
            foreach (var d in model.Controls)
            {
                if (d.LabelRect.IsEmpty) continue;
                var lr = Map(d.LabelRect);
                string label = StripLabel != null ? StripLabel(d.Id) : null;
                bool has = !string.IsNullOrEmpty(label);
                Theme.FillRound(g, has ? Theme.Mix(Theme.Card, Theme.Accent, 0.18f) : Theme.Surface, lr, 5 * sc);
                float numW = d.Strip >= 0 ? 12 * sc : 0;
                if (numW > 0)
                {
                    var nr = new RectangleF(lr.X + 5 * sc, lr.Y, numW, lr.Height);
                    using (var b2 = new SolidBrush(has ? Theme.Accent : Theme.Dim)) g.DrawString((d.Strip + 1).ToString(), Px("Segoe UI", 10 * sc, FontStyle.Bold), b2, nr, Center);
                }
                var tr = new RectangleF(lr.X + 5 * sc + numW, lr.Y, lr.Width - 8 * sc - numW, lr.Height);
                using (var b2 = new SolidBrush(has ? Theme.Text : Theme.Dim)) g.DrawString(has ? label : "—", Px("Segoe UI", 10.5f * sc), b2, tr, LeftFmt);
            }

            foreach (var d in Controllers.Current.Controls)
            {
                switch (d.Kind)
                {
                    case ControlKind.Fader: DrawFader(g, d); break;
                    case ControlKind.Knob: DrawKnob(g, d); break;
                    default: DrawButton(g, d); break;
                }
                var r = Map(d.Rect);
                if (IsAssigned != null && IsAssigned(d.Id))
                {
                    float ds = 5 * sc;
                    using (var b = new SolidBrush(Theme.Accent))
                        g.FillEllipse(b, r.Right - ds * 0.4f, r.Y - ds * 0.6f, ds, ds);
                }
                if (d.Id == selected || d.Id == hover)
                {
                    var sr = RectangleF.Inflate(r, 4 * sc, 4 * sc);
                    var c = d.Id == selected ? Theme.Accent : Color.FromArgb(110, Theme.Accent);
                    Theme.DrawRound(g, c, sr, (d.Kind == ControlKind.Knob ? sr.Width / 2 : 6 * sc), Math.Max(1.5f, 2f * sc));
                }
            }
        }

        void DrawLabel(Graphics g, string s, RectangleF logical, float px, Color c, bool bold, bool center)
        {
            using (var b = new SolidBrush(c))
                g.DrawString(s, Px("Segoe UI", px * sc, bold ? FontStyle.Bold : FontStyle.Regular), b, Map(logical), center ? Center : LeftFmt);
        }

        void DrawFader(Graphics g, ControlDef d)
        {
            var r = Map(d.Rect);
            int v = engine.GetValue(d.Id);
            float t = v < 0 ? 0 : v / 127f;
            float cx = r.X + r.Width / 2;
            float top = r.Y + 12 * sc, bot = r.Bottom - 12 * sc;

            using (var tick = new Pen(Color.FromArgb(48, 52, 63), Math.Max(1f, sc)))
                for (int k = 0; k <= 10; k++)
                {
                    float y = bot - (bot - top) * k / 10f;
                    float len = (k % 5 == 0 ? 8 : 5) * sc;
                    g.DrawLine(tick, cx + 7 * sc, y, cx + 7 * sc + len, y);
                    g.DrawLine(tick, cx - 7 * sc, y, cx - 7 * sc - len, y);
                }

            var slot = new RectangleF(cx - 2.5f * sc, top, 5 * sc, bot - top);
            Theme.FillRound(g, Color.FromArgb(5, 6, 8), slot, 2.5f * sc);
            float capY = bot - (bot - top) * t;
            if (t > 0)
                Theme.FillRound(g, Color.FromArgb(200, Theme.Accent), new RectangleF(slot.X, capY, slot.Width, bot - capY), 2.5f * sc);

            float capW = r.Width - 6 * sc, capH = 20 * sc;
            var cap = new RectangleF(cx - capW / 2, capY - capH / 2, capW, capH);
            using (var p = Theme.Round(cap, 3 * sc))
            using (var br = new LinearGradientBrush(cap, Color.FromArgb(86, 92, 106), Color.FromArgb(46, 50, 60), 90f))
            {
                g.FillPath(br, p);
                using (var pen = new Pen(Color.FromArgb(105, 111, 126), Math.Max(1f, sc))) g.DrawPath(pen, p);
            }
            using (var pen = new Pen(Color.FromArgb(235, 238, 245), Math.Max(1f, 2f * sc)))
                g.DrawLine(pen, cap.X + 4 * sc, capY, cap.Right - 4 * sc, capY);
        }

        void DrawKnob(Graphics g, ControlDef d)
        {
            var r = Map(d.Rect);
            int v = engine.GetValue(d.Id);
            float t = v < 0 ? 0 : v / 127f;
            var arc = RectangleF.Inflate(r, -2 * sc, -2 * sc);
            using (var pen = new Pen(Color.FromArgb(46, 50, 60), 3.5f * sc) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pen, arc, 135, 270);
            if (t > 0.004f)
                using (var pen = new Pen(Theme.Accent, 3.5f * sc) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, arc, 135, 270 * t);

            var inner = RectangleF.Inflate(r, -8 * sc, -8 * sc);
            using (var br = new LinearGradientBrush(inner, Color.FromArgb(72, 77, 90), Color.FromArgb(34, 37, 45), 90f))
                g.FillEllipse(br, inner);
            using (var pen = new Pen(Color.FromArgb(92, 98, 112), Math.Max(1f, sc))) g.DrawEllipse(pen, inner);

            double a = (135 + 270 * t) * Math.PI / 180;
            float cx = inner.X + inner.Width / 2, cy = inner.Y + inner.Height / 2, rad = inner.Width / 2;
            using (var pen = new Pen(Color.White, Math.Max(1.5f, 2.2f * sc)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, cx + (float)Math.Cos(a) * rad * 0.25f, cy + (float)Math.Sin(a) * rad * 0.25f,
                                cx + (float)Math.Cos(a) * rad * 0.82f, cy + (float)Math.Sin(a) * rad * 0.82f);
        }

        void DrawButton(Graphics g, ControlDef d)
        {
            var r = Map(d.Rect);
            bool lit = engine.GetLed(d.Id);
            bool pressed = engine.GetValue(d.Id) > 0;
            Color fill = lit ? Color.FromArgb(120, 28, 38) : pressed ? Color.FromArgb(58, 63, 76) : Color.FromArgb(36, 39, 47);
            if (lit)
            {
                var glow = RectangleF.Inflate(r, 3 * sc, 3 * sc);
                Theme.FillRound(g, Color.FromArgb(60, Theme.Red), glow, 7 * sc);
            }
            float rad = d.Pad ? 7 * sc : 4 * sc;
            if (d.Pad && !lit && !pressed) fill = Color.FromArgb(44, 47, 56);
            Theme.FillRound(g, fill, r, rad);
            Theme.DrawRound(g, lit ? Theme.Red : Color.FromArgb(56, 60, 72), r, rad, Math.Max(1f, sc));
            if (string.IsNullOrEmpty(d.Caption)) return;
            bool symbol = d.Caption.Length > 0 && d.Caption[0] > 0x2000;
            using (var b = new SolidBrush(lit ? Color.White : Theme.Muted))
                g.DrawString(d.Caption,
                    symbol ? Px("Segoe UI Symbol", 12 * sc) : Px("Segoe UI", 11 * sc, FontStyle.Bold),
                    b, r, Center);
        }
    }
}
