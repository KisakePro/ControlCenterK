using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>
    /// Éditeur de capture : sélection / déplacement / redimensionnement, rectangle, ellipse, ligne, flèche, crayon,
    /// surligneur, texte, numéros, floutage, images collées (Ctrl+V ou glisser-déposer) ; couleur, épaisseur,
    /// annuler / rétablir ; puis copier ou enregistrer. Chaque annotation est un objet redessiné sur l'image d'origine.
    /// </summary>
    sealed class ImageEditor : Form
    {
        abstract class Shape
        {
            public Color Color; public float Width;
            public abstract void Draw(Graphics g, Bitmap source);
            public abstract Rectangle Bounds { get; }
            public virtual bool Hit(Point p) { return Rectangle.Inflate(Bounds, 4, 4).Contains(p); }
            public abstract void Offset(int dx, int dy);
            /// <summary>Poignées de redimensionnement (vide si la forme ne se redimensionne pas).</summary>
            public virtual Point[] Handles { get { return new Point[0]; } }
            public virtual void MoveHandle(int i, Point p, bool free) { }
            public virtual Shape Clone() { return (Shape)MemberwiseClone(); }

            protected bool HitOutline(GraphicsPath path, Point p)
            {
                using (var pen = new Pen(Color.Black, Math.Max(Width, 10))) return path.IsOutlineVisible(p, pen);
            }
        }

        /// <summary>Forme définie par un rectangle, redimensionnable par ses quatre coins.</summary>
        abstract class BoxShape : Shape
        {
            public Rectangle R;
            public override Rectangle Bounds { get { return R; } }
            public override void Offset(int dx, int dy) { R.Offset(dx, dy); }
            public override Point[] Handles { get { return new[] { R.Location, new Point(R.Right, R.Top), new Point(R.Right, R.Bottom), new Point(R.Left, R.Bottom) }; } }
            protected virtual Size? Ratio { get { return null; } }
            public override void MoveHandle(int i, Point p, bool free)
            {
                var opp = Handles[(i + 2) % 4];
                var ratio = Ratio;
                if (ratio != null && !free && ratio.Value.Width > 0)
                {
                    int w = Math.Max(8, Math.Abs(p.X - opp.X));
                    int h = Math.Max(1, w * ratio.Value.Height / ratio.Value.Width);
                    p = new Point(opp.X + (p.X >= opp.X ? w : -w), opp.Y + (p.Y >= opp.Y ? h : -h));
                }
                R = Norm(opp, p);
            }
        }
        sealed class RectShape : BoxShape
        {
            public override void Draw(Graphics g, Bitmap s) { using (var p = new Pen(Color, Width) { LineJoin = LineJoin.Round }) g.DrawRectangle(p, R); }
            public override bool Hit(Point p) { using (var path = new GraphicsPath()) { path.AddRectangle(R); return HitOutline(path, p); } }
        }
        sealed class EllipseShape : BoxShape
        {
            public override void Draw(Graphics g, Bitmap s) { using (var p = new Pen(Color, Width)) g.DrawEllipse(p, R); }
            public override bool Hit(Point p) { using (var path = new GraphicsPath()) { path.AddEllipse(R); return HitOutline(path, p); } }
        }
        sealed class BlurShape : BoxShape
        {
            /// <summary>Pixellisation : la zone est réduite puis agrandie sans lissage.</summary>
            public override void Draw(Graphics g, Bitmap s)
            {
                var r = Rectangle.Intersect(R, new Rectangle(0, 0, s.Width, s.Height));
                if (r.Width < 2 || r.Height < 2) return;
                int block = 12;
                using (var small = new Bitmap(Math.Max(1, r.Width / block), Math.Max(1, r.Height / block)))
                {
                    using (var sg = Graphics.FromImage(small)) sg.DrawImage(s, new Rectangle(0, 0, small.Width, small.Height), r, GraphicsUnit.Pixel);
                    var mode = g.InterpolationMode;
                    var off = g.PixelOffsetMode;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(small, r);
                    g.InterpolationMode = mode;
                    g.PixelOffsetMode = off;
                }
            }
        }
        /// <summary>Image collée par-dessus la capture ; garde ses proportions (Maj = libre).</summary>
        sealed class ImageShape : BoxShape
        {
            public Bitmap Img;
            protected override Size? Ratio { get { return Img.Size; } }
            public override void Draw(Graphics g, Bitmap s)
            {
                var mode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(Img, R);
                g.InterpolationMode = mode;
            }
        }
        /// <summary>Ligne ou flèche, modifiable par ses deux extrémités.</summary>
        sealed class LineShape : Shape
        {
            public Point A, B;
            public bool Arrow;
            public override void Draw(Graphics g, Bitmap s)
            {
                using (var p = new Pen(Color, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    if (Arrow) p.CustomEndCap = new AdjustableArrowCap(4, 4, true);
                    g.DrawLine(p, A, B);
                }
            }
            public override Rectangle Bounds { get { return Norm(A, B); } }
            public override bool Hit(Point p) { using (var path = new GraphicsPath()) { path.AddLine(A, B); return HitOutline(path, p); } }
            public override void Offset(int dx, int dy) { A.Offset(dx, dy); B.Offset(dx, dy); }
            public override Point[] Handles { get { return new[] { A, B }; } }
            public override void MoveHandle(int i, Point p, bool free) { if (i == 0) A = p; else B = p; }
        }
        sealed class PenShape : Shape
        {
            public List<Point> Points = new List<Point>();
            public bool Marker;
            public override void Draw(Graphics g, Bitmap s)
            {
                if (Points.Count < 2) return;
                using (var p = new Pen(Marker ? Color.FromArgb(100, Color) : Color, Marker ? Width * 4 : Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(p, Points.ToArray());
            }
            public override Rectangle Bounds
            {
                get
                {
                    if (Points.Count == 0) return Rectangle.Empty;
                    return Rectangle.FromLTRB(Points.Min(q => q.X), Points.Min(q => q.Y), Points.Max(q => q.X), Points.Max(q => q.Y));
                }
            }
            public override bool Hit(Point p)
            {
                if (Points.Count < 2) return false;
                using (var path = new GraphicsPath())
                using (var pen = new Pen(Color.Black, Math.Max(Marker ? Width * 4 : Width, 10)))
                {
                    path.AddLines(Points.ToArray());
                    return path.IsOutlineVisible(p, pen);
                }
            }
            public override void Offset(int dx, int dy) { for (int i = 0; i < Points.Count; i++) Points[i] = new Point(Points[i].X + dx, Points[i].Y + dy); }
            public override Shape Clone() { var c = (PenShape)base.Clone(); c.Points = new List<Point>(Points); return c; }
        }
        sealed class TextShape : Shape
        {
            public Point At; public string Text;
            Font MakeFont() { return new Font("Segoe UI Semibold", Math.Max(10, Width * 5), GraphicsUnit.Pixel); }
            public override void Draw(Graphics g, Bitmap s)
            {
                using (var f = MakeFont())
                using (var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                using (var b = new SolidBrush(Color))
                {
                    g.DrawString(Text, f, shadow, At.X + 1, At.Y + 1);
                    g.DrawString(Text, f, b, At);
                }
            }
            public override Rectangle Bounds
            {
                get
                {
                    using (var f = MakeFont())
                    using (var bmp = new Bitmap(1, 1))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        var sz = g.MeasureString(Text, f);
                        return new Rectangle(At, new Size((int)Math.Ceiling(sz.Width), (int)Math.Ceiling(sz.Height)));
                    }
                }
            }
            public override void Offset(int dx, int dy) { At.Offset(dx, dy); }
        }
        /// <summary>Pastille numérotée (étapes 1, 2, 3…).</summary>
        sealed class CounterShape : Shape
        {
            public Point C; public int Number;
            int Diameter { get { return (int)Math.Max(24, Width * 6); } }
            public override Rectangle Bounds { get { int d = Diameter; return new Rectangle(C.X - d / 2, C.Y - d / 2, d, d); } }
            public override void Draw(Graphics g, Bitmap s)
            {
                var r = Bounds;
                using (var b = new SolidBrush(Color)) g.FillEllipse(b, r);
                using (var p = new Pen(Color.White, Math.Max(2, Diameter / 12f))) g.DrawEllipse(p, r);
                bool dark = Color.R * 0.299 + Color.G * 0.587 + Color.B * 0.114 > 160;
                using (var f = new Font("Segoe UI", Diameter * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var tb = new SolidBrush(dark ? Color.Black : Color.White))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(Number.ToString(), f, tb, new RectangleF(r.X, r.Y + 1, r.Width, r.Height), sf);
            }
            public override void Offset(int dx, int dy) { C.Offset(dx, dy); }
        }

        readonly Bitmap source;
        List<Shape> shapes = new List<Shape>();
        readonly List<List<Shape>> undo = new List<List<Shape>>(), redo = new List<List<Shape>>();
        readonly List<Bitmap> pasted = new List<Bitmap>();
        readonly Panel canvasHost;
        readonly Canvas canvas;
        string tool = "rect";
        Color color = Color.FromArgb(255, 59, 48);
        float width = 4;
        Shape drawing, selected;
        Point down, last;
        int handle = -1;
        bool dragging, pushed;
        readonly Dictionary<string, FlatButton> toolButtons = new Dictionary<string, FlatButton>();

        public ImageEditor(Bitmap img)
        {
            source = img;
            Text = "Éditeur de capture — ControlCenterK";
            Icon = Program.AppIcon;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            KeyPreview = true;
            AllowDrop = true;
            StartPosition = FormStartPosition.CenterScreen;
            var wa = Screen.PrimaryScreen.WorkingArea;
            MinimumSize = new Size(Theme.S(640), Theme.S(420));
            Size = new Size(Math.Min(wa.Width - 80, Math.Max(Theme.S(1000), img.Width + Theme.S(60))), Math.Min(wa.Height - 80, Math.Max(Theme.S(600), img.Height + Theme.S(170))));

            // Barre d'outils en flux : les boutons passent à la ligne au lieu de se superposer quand la fenêtre est étroite.
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Theme.Side, Padding = new Padding(Theme.S(8), Theme.S(6), Theme.S(8), Theme.S(6)) };
            Func<Control, Control> add = c => { c.Margin = new Padding(Theme.S(3)); bar.Controls.Add(c); return c; };
            Action gap = () => add(new Panel { Width = Theme.S(10), Height = Theme.S(32), BackColor = Theme.Side });

            foreach (var t in new[] { new[] { "select", "Sélection" }, new[] { "rect", "Rectangle" }, new[] { "ellipse", "Ellipse" }, new[] { "line", "Ligne" },
                                      new[] { "arrow", "Flèche" }, new[] { "pen", "Crayon" }, new[] { "marker", "Surligneur" }, new[] { "text", "Texte" },
                                      new[] { "counter", "Numéro" }, new[] { "blur", "Flouter" } })
            {
                string id = t[0];
                var b = new FlatButton(t[1]);
                b.FitWidth();
                b.Click += (s, e) => SetTool(id);
                add(b);
                toolButtons[id] = b;
            }
            gap();
            var col = new Button { FlatStyle = FlatStyle.Flat, BackColor = color, Size = new Size(Theme.S(32), Theme.S(32)), Cursor = Cursors.Hand };
            col.FlatAppearance.BorderColor = Theme.Border;
            col.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { FullOpen = true, Color = color })
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        color = dlg.Color; col.BackColor = color;
                        if (selected != null && !(selected is ImageShape) && !(selected is BlurShape)) { Push(); selected.Color = color; canvas.Invalidate(); }
                    }
            };
            add(col);
            var thick = new DropButton { Width = Theme.S(140) };
            foreach (int w in new[] { 2, 4, 6, 10, 16 }) thick.Add(w.ToString(), "Épaisseur " + w);
            thick.Value = "4";
            thick.ValueChanged += (s, e) =>
            {
                width = float.Parse(thick.Value);
                if (selected != null && !(selected is ImageShape) && !(selected is BlurShape)) { Push(); selected.Width = width; canvas.Invalidate(); }
            };
            add(thick);
            gap();
            var undoB = new FlatButton("") { Glyph = "", Width = Theme.S(36) };
            undoB.Click += (s, e) => Undo();
            var redoB = new FlatButton("") { Glyph = "", Width = Theme.S(36) };
            redoB.Click += (s, e) => Redo();
            var del = new FlatButton("") { Glyph = "", Width = Theme.S(36) };
            del.Click += (s, e) => DeleteSelected();
            add(undoB); add(redoB); add(del);
            gap();
            var paste = new FlatButton("Coller une image") { Glyph = "" };
            paste.FitWidth();
            paste.Click += (s, e) => PasteImage();
            add(paste);
            gap();
            var copy = new FlatButton("Copier") { Glyph = "" };
            copy.FitWidth();
            var saveAs = new FlatButton("Enregistrer sous…");
            saveAs.FitWidth();
            var save = new FlatButton("Enregistrer", true) { Glyph = "" };
            save.FitWidth();
            add(copy); add(saveAs); add(save);
            save.Click += (s, e) => { using (var b = Render()) if (ScreenCapture.Save(b, null) != null) Close(); };
            saveAs.Click += (s, e) => { using (var b = Render()) if (ScreenCapture.SaveAs(b) != null) Close(); };
            copy.Click += (s, e) => { using (var b = Render()) ScreenCapture.Copy(b); };
            var tips = new ToolTip();
            tips.SetToolTip(undoB, "Annuler (Ctrl+Z)");
            tips.SetToolTip(redoB, "Rétablir (Ctrl+Y)");
            tips.SetToolTip(del, "Supprimer l'élément sélectionné (Suppr)");
            tips.SetToolTip(paste, "Coller l'image du presse-papiers (Ctrl+V) ; on peut aussi glisser un fichier image");
            tips.SetToolTip(toolButtons["select"], "Cliquer un élément pour le déplacer ; poignées pour le redimensionner ; Maj = proportions libres ; flèches = décaler ; Ctrl+D = dupliquer ; double-clic sur un texte = modifier");

            canvasHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            canvasHost.HandleCreated += (s, e) => Theme.DarkScroll(canvasHost);
            canvas = new Canvas { Size = img.Size, Location = new Point(Theme.S(20), Theme.S(20)), Cursor = Cursors.Cross };
            canvas.Paint += (s, e) => Draw(e.Graphics, true);
            canvas.MouseDown += CanvasDown;
            canvas.MouseMove += CanvasMove;
            canvas.MouseUp += CanvasUp;
            canvas.MouseDoubleClick += CanvasDoubleClick;
            canvasHost.Controls.Add(canvas);
            canvasHost.Resize += (s, e) => Center();
            Controls.Add(canvasHost);
            Controls.Add(bar);
            SetTool("rect");
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => DropImage(e);
            FormClosed += (s, e) => { source.Dispose(); foreach (var b in pasted) b.Dispose(); };
        }

        void Center()
        {
            int x = Math.Max(Theme.S(20), (canvasHost.ClientSize.Width - canvas.Width) / 2), y = Math.Max(Theme.S(20), (canvasHost.ClientSize.Height - canvas.Height) / 2);
            canvas.Location = new Point(x + canvasHost.AutoScrollPosition.X, y + canvasHost.AutoScrollPosition.Y);
        }

        void SetTool(string t)
        {
            tool = t;
            if (t != "select") Select(null);
            foreach (var kv in toolButtons) { kv.Value.Primary = kv.Key == t; kv.Value.Invalidate(); }
            canvas.Cursor = t == "text" ? Cursors.IBeam : t == "select" ? Cursors.Default : Cursors.Cross;
        }

        void Select(Shape s) { selected = s; canvas.Invalidate(); }

        void Draw(Graphics g, bool ui)
        {
            g.DrawImageUnscaled(source, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            foreach (var s in shapes) s.Draw(g, source);
            if (drawing != null) drawing.Draw(g, source);
            if (!ui || selected == null) return;
            var r = Rectangle.Inflate(selected.Bounds, 3, 3);
            using (var p = new Pen(Color.White, 1) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, r);
            using (var p = new Pen(Theme.Accent, 1) { DashStyle = DashStyle.Dash, DashOffset = 3 }) g.DrawRectangle(p, r);
            int h = Theme.S(4);
            foreach (var pt in selected.Handles)
            {
                var hr = new Rectangle(pt.X - h, pt.Y - h, h * 2, h * 2);
                g.FillRectangle(Brushes.White, hr);
                using (var p = new Pen(Theme.Accent, 1.5f)) g.DrawRectangle(p, hr);
            }
        }

        Bitmap Render()
        {
            var b = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) Draw(g, false);
            return b;
        }

        static Rectangle Norm(Point a, Point b) { return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)); }

        Shape HitShape(Point p)
        {
            for (int i = shapes.Count - 1; i >= 0; i--) if (shapes[i].Hit(p)) return shapes[i];
            return null;
        }

        int HitHandle(Point p)
        {
            if (selected == null) return -1;
            var hs = selected.Handles;
            int tol = Theme.S(7);
            for (int i = 0; i < hs.Length; i++) if (Math.Abs(hs[i].X - p.X) <= tol && Math.Abs(hs[i].Y - p.Y) <= tol) return i;
            return -1;
        }

        void CanvasDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            down = last = e.Location;
            if (tool == "select")
            {
                handle = HitHandle(e.Location);
                if (handle < 0) Select(HitShape(e.Location));
                dragging = selected != null;
                pushed = false;
                return;
            }
            switch (tool)
            {
                case "rect": drawing = new RectShape { R = new Rectangle(down, Size.Empty) }; break;
                case "ellipse": drawing = new EllipseShape { R = new Rectangle(down, Size.Empty) }; break;
                case "blur": drawing = new BlurShape { R = new Rectangle(down, Size.Empty) }; break;
                case "arrow": case "line": drawing = new LineShape { A = down, B = down, Arrow = tool == "arrow" }; break;
                case "pen": case "marker": var p = new PenShape { Marker = tool == "marker" }; p.Points.Add(down); drawing = p; break;
                case "counter":
                    Add(new CounterShape { C = down, Number = shapes.OfType<CounterShape>().Count() + 1, Color = color, Width = width });
                    return;
                case "text":
                    string t = InputBox.Show(this, "Texte", "Texte à ajouter :");
                    if (!string.IsNullOrEmpty(t)) Add(new TextShape { At = down, Text = t, Color = color, Width = width });
                    return;
            }
            drawing.Color = color;
            drawing.Width = width;
        }

        void CanvasMove(object sender, MouseEventArgs e)
        {
            if (tool == "select")
            {
                if (!dragging || e.Button != MouseButtons.Left)
                {
                    canvas.Cursor = HitHandle(e.Location) >= 0 ? Cursors.SizeNWSE : HitShape(e.Location) != null ? Cursors.SizeAll : Cursors.Default;
                    return;
                }
                if (!pushed) { Push(); pushed = true; }
                if (handle >= 0) selected.MoveHandle(handle, e.Location, (ModifierKeys & Keys.Shift) != 0);
                else selected.Offset(e.X - last.X, e.Y - last.Y);
                last = e.Location;
                canvas.Invalidate();
                return;
            }
            if (drawing == null) return;
            if (drawing is BoxShape) ((BoxShape)drawing).R = Norm(down, e.Location);
            else if (drawing is LineShape) ((LineShape)drawing).B = e.Location;
            else if (drawing is PenShape) ((PenShape)drawing).Points.Add(e.Location);
            canvas.Invalidate();
        }

        void CanvasUp(object sender, MouseEventArgs e)
        {
            dragging = false;
            handle = -1;
            if (drawing == null) return;
            var d = drawing;
            drawing = null;
            var b = d.Bounds;
            if (b.Width < 3 && b.Height < 3 && !(d is PenShape)) { canvas.Invalidate(); return; }
            Add(d);
        }

        void CanvasDoubleClick(object sender, MouseEventArgs e)
        {
            var t = (tool == "select" ? HitShape(e.Location) : null) as TextShape;
            if (t == null) return;
            string s = InputBox.Show(this, "Texte", "Modifier le texte :", t.Text);
            if (string.IsNullOrEmpty(s) || s == t.Text) return;
            Push();
            t.Text = s;
            canvas.Invalidate();
        }

        /// <summary>Mémorise l'état actuel pour pouvoir l'annuler.</summary>
        void Push()
        {
            undo.Add(shapes.Select(s => s.Clone()).ToList());
            if (undo.Count > 200) undo.RemoveAt(0);
            redo.Clear();
        }

        void Add(Shape s) { Push(); shapes.Add(s); canvas.Invalidate(); }

        void Undo()
        {
            if (undo.Count == 0) return;
            redo.Add(shapes);
            shapes = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            Select(null);
        }

        void Redo()
        {
            if (redo.Count == 0) return;
            undo.Add(shapes);
            shapes = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            Select(null);
        }

        void DeleteSelected()
        {
            if (selected == null) return;
            Push();
            shapes.Remove(selected);
            Select(null);
        }

        void Duplicate()
        {
            if (selected == null) return;
            var c = selected.Clone();
            c.Offset(Theme.S(16), Theme.S(16));
            Add(c);
            Select(c);
        }

        void Nudge(int dx, int dy)
        {
            if (selected == null) return;
            Push();
            selected.Offset(dx, dy);
            canvas.Invalidate();
        }

        /// <summary>Ajoute une image au centre de la partie visible, réduite si elle dépasse, et la sélectionne.</summary>
        void AddImage(Image img)
        {
            var bmp = new Bitmap(img);
            pasted.Add(bmp);
            var host = canvasHost.ClientRectangle;
            var c = canvas.PointToClient(canvasHost.PointToScreen(new Point(host.Width / 2, host.Height / 2)));
            c = new Point(Math.Max(0, Math.Min(source.Width, c.X)), Math.Max(0, Math.Min(source.Height, c.Y)));
            double k = Math.Min(1, Math.Min(source.Width * 0.8 / bmp.Width, source.Height * 0.8 / bmp.Height));
            var sz = new Size(Math.Max(8, (int)(bmp.Width * k)), Math.Max(8, (int)(bmp.Height * k)));
            var s = new ImageShape { Img = bmp, R = new Rectangle(c.X - sz.Width / 2, c.Y - sz.Height / 2, sz.Width, sz.Height) };
            Add(s);
            SetTool("select");
            Select(s);
        }

        void PasteImage()
        {
            try
            {
                if (Clipboard.ContainsImage()) { using (var i = Clipboard.GetImage()) AddImage(i); return; }
                if (Clipboard.ContainsFileDropList())
                    foreach (string f in Clipboard.GetFileDropList()) if (LoadFile(f)) return;
            }
            catch { }
        }

        bool LoadFile(string f)
        {
            try
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp" && ext != ".gif" && ext != ".tif" && ext != ".tiff") return false;
                using (var fs = File.OpenRead(f)) using (var i = Image.FromStream(fs)) AddImage(i);
                return true;
            }
            catch { return false; }
        }

        void DropImage(DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) { foreach (var f in files) LoadFile(f); return; }
            var img = e.Data.GetData(DataFormats.Bitmap) as Image;
            if (img != null) AddImage(img);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Flèches : décaler l'élément sélectionné (sinon elles déplaceraient le focus entre les boutons).
            if (selected != null)
            {
                int step = (keyData & Keys.Shift) != 0 ? 10 : 1;
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Left: Nudge(-step, 0); return true;
                    case Keys.Right: Nudge(step, 0); return true;
                    case Keys.Up: Nudge(0, -step); return true;
                    case Keys.Down: Nudge(0, step); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z) Undo();
            else if (e.Control && e.KeyCode == Keys.Y) Redo();
            else if (e.Control && e.KeyCode == Keys.V) PasteImage();
            else if (e.Control && e.KeyCode == Keys.D) Duplicate();
            else if (e.Control && e.KeyCode == Keys.C) { using (var b = Render()) ScreenCapture.Copy(b); }
            else if (e.Control && e.KeyCode == Keys.S) { using (var b = Render()) if (ScreenCapture.Save(b, null) != null) Close(); }
            else if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back) DeleteSelected();
            else if (e.KeyCode == Keys.Escape) { if (selected != null) Select(null); else Close(); }
            base.OnKeyDown(e);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Center(); Activate(); }

        sealed class Canvas : Panel
        {
            public Canvas() { DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        }
    }
}
