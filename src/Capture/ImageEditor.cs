using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>
    /// Éditeur de capture : rectangle, flèche, crayon, surligneur, texte, floutage ; couleur, épaisseur,
    /// annuler / rétablir ; puis copier ou enregistrer. Chaque annotation est un objet redessiné sur l'image d'origine.
    /// </summary>
    sealed class ImageEditor : Form
    {
        abstract class Shape
        {
            public Color Color; public float Width;
            public abstract void Draw(Graphics g, Bitmap source);
        }
        sealed class RectShape : Shape
        {
            public Rectangle R;
            public override void Draw(Graphics g, Bitmap s) { using (var p = new Pen(Color, Width) { LineJoin = LineJoin.Round }) g.DrawRectangle(p, R); }
        }
        sealed class ArrowShape : Shape
        {
            public Point A, B;
            public override void Draw(Graphics g, Bitmap s)
            {
                using (var p = new Pen(Color, Width) { StartCap = LineCap.Round, CustomEndCap = new AdjustableArrowCap(4, 4, true) })
                    g.DrawLine(p, A, B);
            }
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
        }
        sealed class TextShape : Shape
        {
            public Point At; public string Text;
            public override void Draw(Graphics g, Bitmap s)
            {
                using (var f = new Font("Segoe UI Semibold", Math.Max(10, Width * 5), GraphicsUnit.Pixel))
                using (var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                using (var b = new SolidBrush(Color))
                {
                    g.DrawString(Text, f, shadow, At.X + 1, At.Y + 1);
                    g.DrawString(Text, f, b, At);
                }
            }
        }
        sealed class BlurShape : Shape
        {
            public Rectangle R;
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

        readonly Bitmap source;
        readonly List<Shape> shapes = new List<Shape>(), redo = new List<Shape>();
        readonly Panel canvasHost;
        readonly Canvas canvas;
        string tool = "rect";
        Color color = Color.FromArgb(255, 59, 48);
        float width = 4;
        Shape drawing;
        Point down;
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
            StartPosition = FormStartPosition.CenterScreen;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(wa.Width - 80, Math.Max(Theme.S(900), img.Width + Theme.S(60))), Math.Min(wa.Height - 80, Math.Max(Theme.S(600), img.Height + Theme.S(140))));

            var bar = new Panel { Dock = DockStyle.Top, Height = Theme.S(52), BackColor = Theme.Side };
            int x = Theme.S(10);
            foreach (var t in new[] { new[] { "rect", "Rectangle", "" }, new[] { "arrow", "Flèche", "" }, new[] { "pen", "Crayon", "" },
                                      new[] { "marker", "Surligneur", "" }, new[] { "text", "Texte", "" }, new[] { "blur", "Flouter", "" } })
            {
                string id = t[0];
                var b = new FlatButton(t[1]) { Glyph = t[2] };
                b.FitWidth();
                b.Location = new Point(x, Theme.S(9));
                b.Click += (s, e) => SetTool(id);
                bar.Controls.Add(b);
                toolButtons[id] = b;
                x = b.Right + Theme.S(6);
            }
            x += Theme.S(10);
            var col = new Button { FlatStyle = FlatStyle.Flat, BackColor = color, Size = new Size(Theme.S(34), Theme.S(34)), Location = new Point(x, Theme.S(9)), Cursor = Cursors.Hand };
            col.FlatAppearance.BorderColor = Theme.Border;
            col.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { FullOpen = true, Color = color })
                    if (dlg.ShowDialog(this) == DialogResult.OK) { color = dlg.Color; col.BackColor = color; }
            };
            bar.Controls.Add(col);
            x = col.Right + Theme.S(8);
            var thick = new DropButton { Width = Theme.S(110), Location = new Point(x, Theme.S(9)) };
            foreach (int w in new[] { 2, 4, 6, 10, 16 }) thick.Add(w.ToString(), "Épaisseur " + w);
            thick.Value = "4";
            thick.ValueChanged += (s, e) => width = float.Parse(thick.Value);
            bar.Controls.Add(thick);
            x = thick.Right + Theme.S(16);
            var undo = new FlatButton("") { Glyph = "", Width = Theme.S(36), Location = new Point(x, Theme.S(9)) };
            undo.Click += (s, e) => Undo();
            var redoB = new FlatButton("") { Glyph = "", Width = Theme.S(36), Location = new Point(undo.Right + Theme.S(4), Theme.S(9)) };
            redoB.Click += (s, e) => Redo();
            bar.Controls.AddRange(new Control[] { undo, redoB });

            var save = new FlatButton("Enregistrer", true) { Glyph = "", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            save.FitWidth();
            var saveAs = new FlatButton("Enregistrer sous…") { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            saveAs.FitWidth();
            var copy = new FlatButton("Copier") { Glyph = "", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            copy.FitWidth();
            bar.Controls.AddRange(new Control[] { save, saveAs, copy });
            bar.Layout += (s, e) =>
            {
                save.Location = new Point(bar.Width - save.Width - Theme.S(10), Theme.S(9));
                saveAs.Location = new Point(save.Left - saveAs.Width - Theme.S(6), Theme.S(9));
                copy.Location = new Point(saveAs.Left - copy.Width - Theme.S(6), Theme.S(9));
            };
            save.Click += (s, e) => { using (var b = Render()) if (ScreenCapture.Save(b, null) != null) Close(); };
            saveAs.Click += (s, e) => { using (var b = Render()) if (ScreenCapture.SaveAs(b) != null) Close(); };
            copy.Click += (s, e) => { using (var b = Render()) ScreenCapture.Copy(b); };

            canvasHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            canvasHost.HandleCreated += (s, e) => Theme.DarkScroll(canvasHost);
            canvas = new Canvas { Size = img.Size, Location = new Point(Theme.S(20), Theme.S(20)), Cursor = Cursors.Cross };
            canvas.Paint += (s, e) => Draw(e.Graphics);
            canvas.MouseDown += CanvasDown;
            canvas.MouseMove += CanvasMove;
            canvas.MouseUp += CanvasUp;
            canvasHost.Controls.Add(canvas);
            canvasHost.Resize += (s, e) => Center();
            Controls.Add(canvasHost);
            Controls.Add(bar);
            SetTool("rect");
            FormClosed += (s, e) => source.Dispose();
        }

        void Center()
        {
            int x = Math.Max(Theme.S(20), (canvasHost.ClientSize.Width - canvas.Width) / 2), y = Math.Max(Theme.S(20), (canvasHost.ClientSize.Height - canvas.Height) / 2);
            canvas.Location = new Point(x + canvasHost.AutoScrollPosition.X, y + canvasHost.AutoScrollPosition.Y);
        }

        void SetTool(string t)
        {
            tool = t;
            foreach (var kv in toolButtons) { kv.Value.Primary = kv.Key == t; kv.Value.Invalidate(); }
            canvas.Cursor = t == "text" ? Cursors.IBeam : Cursors.Cross;
        }

        void Draw(Graphics g)
        {
            g.DrawImageUnscaled(source, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            foreach (var s in shapes) s.Draw(g, source);
            if (drawing != null) drawing.Draw(g, source);
        }

        Bitmap Render()
        {
            var b = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) Draw(g);
            return b;
        }

        static Rectangle Norm(Point a, Point b) { return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)); }

        void CanvasDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            down = e.Location;
            switch (tool)
            {
                case "rect": drawing = new RectShape { R = new Rectangle(down, Size.Empty) }; break;
                case "blur": drawing = new BlurShape { R = new Rectangle(down, Size.Empty) }; break;
                case "arrow": drawing = new ArrowShape { A = down, B = down }; break;
                case "pen": case "marker": var p = new PenShape { Marker = tool == "marker" }; p.Points.Add(down); drawing = p; break;
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
            if (drawing == null) return;
            if (drawing is RectShape) ((RectShape)drawing).R = Norm(down, e.Location);
            else if (drawing is BlurShape) ((BlurShape)drawing).R = Norm(down, e.Location);
            else if (drawing is ArrowShape) ((ArrowShape)drawing).B = e.Location;
            else if (drawing is PenShape) ((PenShape)drawing).Points.Add(e.Location);
            canvas.Invalidate();
        }

        void CanvasUp(object sender, MouseEventArgs e)
        {
            if (drawing == null) return;
            var d = drawing;
            drawing = null;
            Add(d);
        }

        void Add(Shape s) { shapes.Add(s); redo.Clear(); canvas.Invalidate(); }
        void Undo() { if (shapes.Count == 0) return; redo.Add(shapes[shapes.Count - 1]); shapes.RemoveAt(shapes.Count - 1); canvas.Invalidate(); }
        void Redo() { if (redo.Count == 0) return; shapes.Add(redo[redo.Count - 1]); redo.RemoveAt(redo.Count - 1); canvas.Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z) Undo();
            else if (e.Control && e.KeyCode == Keys.Y) Redo();
            else if (e.Control && e.KeyCode == Keys.C) { using (var b = Render()) ScreenCapture.Copy(b); }
            else if (e.Control && e.KeyCode == Keys.S) { using (var b = Render()) if (ScreenCapture.Save(b, null) != null) Close(); }
            else if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Center(); Activate(); }

        sealed class Canvas : Panel
        {
            public Canvas() { DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        }
    }
}
