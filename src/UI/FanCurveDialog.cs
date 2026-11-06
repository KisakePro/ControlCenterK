using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Réglage d'une courbe température → vitesse : cinq points à déplacer, source et préréglages.</summary>
    sealed class FanCurveDialog : Form
    {
        const float MinT = 20, MaxT = 95;

        public readonly List<FanPoint> Points = new List<FanPoint>();
        public string Source;
        readonly Graph graph;
        readonly System.Windows.Forms.Timer timer;

        public FanCurveDialog(string title, FanSetting s)
        {
            Text = "Courbe — " + title;
            Icon = Program.AppIcon;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(Theme.S(640), Theme.S(470));
            foreach (var p in s.Points) Points.Add(new FanPoint { T = p.T, P = p.P });
            Source = s.Source;

            var src = new DropButton { Width = Theme.S(220), Location = new Point(Theme.S(16), Theme.S(14)) };
            src.Add("cpu", "Température du processeur");
            src.Add("gpu", "Température de la carte graphique");
            src.Add("max", "La plus haute (processeur, carte graphique)");
            src.Add("liquid", "Température du liquide (AIO)");
            src.Add("board", "Température de la carte mère");
            src.Value = Source;
            src.ValueChanged += (a, e) => { Source = src.Value; graph.Invalidate(); };
            Controls.Add(src);
            int x = src.Right + Theme.S(10);
            foreach (var pr in new[] { new[] { "silent", "Silencieux" }, new[] { "balanced", "Équilibré" }, new[] { "performance", "Performance" } })
            {
                string id = pr[0];
                var b = new FlatButton(pr[1]);
                b.FitWidth();
                b.Location = new Point(x, Theme.S(14));
                b.Click += (a, e) => { Points.Clear(); Points.AddRange(FanControl.Preset(id)); graph.Invalidate(); };
                Controls.Add(b);
                x = b.Right + Theme.S(6);
            }
            graph = new Graph(this) { Bounds = new Rectangle(Theme.S(16), Theme.S(58), ClientSize.Width - Theme.S(32), Theme.S(350)) };
            Controls.Add(graph);
            var ok = new FlatButton("Appliquer", true);
            ok.FitWidth();
            ok.Location = new Point(ClientSize.Width - ok.Width - Theme.S(16), ClientSize.Height - ok.Height - Theme.S(14));
            ok.Click += (a, e) => { DialogResult = DialogResult.OK; Close(); };
            var cancel = new FlatButton("Annuler");
            cancel.FitWidth();
            cancel.Location = new Point(ok.Left - cancel.Width - Theme.S(6), ok.Top);
            cancel.Click += (a, e) => Close();
            Controls.AddRange(new Control[] { ok, cancel });
            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (a, e) => graph.Invalidate();
            timer.Start();
            FormClosed += (a, e) => timer.Dispose();
        }

        sealed class Graph : Panel
        {
            readonly FanCurveDialog owner;
            int drag = -1;

            public Graph(FanCurveDialog o)
            {
                owner = o;
                DoubleBuffered = true;
                BackColor = Theme.Card;
            }

            RectangleF Plot { get { int l = Theme.S(44), t = Theme.S(12); return new RectangleF(l, t, Width - l - Theme.S(14), Height - t - Theme.S(30)); } }

            PointF ToScreen(float temp, float pct)
            {
                var r = Plot;
                return new PointF(r.Left + (temp - MinT) / (MaxT - MinT) * r.Width, r.Bottom - pct / 100f * r.Height);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = Plot;
                using (var grid = new Pen(Theme.Border))
                using (var text = new SolidBrush(Theme.Muted))
                {
                    var f = Theme.Ui(8f); // police partagée du thème : ne pas la libérer
                    for (int p = 0; p <= 100; p += 20)
                    {
                        var a = ToScreen(MinT, p);
                        g.DrawLine(grid, r.Left, a.Y, r.Right, a.Y);
                        g.DrawString(p + " %", f, text, Theme.S(4), a.Y - Theme.S(8));
                    }
                    for (int t = 20; t <= 95; t += 15)
                    {
                        var a = ToScreen(t, 0);
                        g.DrawLine(grid, a.X, r.Top, a.X, r.Bottom);
                        g.DrawString(t + " °C", f, text, a.X - Theme.S(14), r.Bottom + Theme.S(6));
                    }
                }
                var pts = owner.Points;
                var line = new List<PointF> { ToScreen(MinT, pts[0].P) };
                foreach (var p in pts) line.Add(ToScreen(p.T, p.P));
                line.Add(ToScreen(MaxT, 100));
                using (var pen = new Pen(Theme.Accent, Theme.S(2))) g.DrawLines(pen, line.ToArray());
                float now = FanControl.Temp(owner.Source);
                if (!float.IsNaN(now) && now >= MinT && now <= MaxT)
                {
                    var a = ToScreen(now, FanControl.Interpolate(pts, now));
                    using (var pen = new Pen(Theme.Green, 1) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, a.X, r.Top, a.X, r.Bottom);
                    using (var b = new SolidBrush(Theme.Green)) g.FillEllipse(b, a.X - Theme.S(4), a.Y - Theme.S(4), Theme.S(8), Theme.S(8));
                    using (var b = new SolidBrush(Theme.Text))
                        g.DrawString(now.ToString("0.0") + " °C → " + FanControl.Interpolate(pts, now).ToString("0") + " %", Theme.Semi(9f), b, r.Left + Theme.S(8), r.Top + Theme.S(4));
                }
                int h = Theme.S(6);
                foreach (var p in pts)
                {
                    var a = ToScreen(p.T, p.P);
                    g.FillEllipse(Brushes.White, a.X - h, a.Y - h, h * 2, h * 2);
                    using (var pen = new Pen(Theme.Accent, Theme.S(2))) g.DrawEllipse(pen, a.X - h, a.Y - h, h * 2, h * 2);
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                float best = Theme.S(14);
                drag = -1;
                for (int i = 0; i < owner.Points.Count; i++)
                {
                    var a = ToScreen(owner.Points[i].T, owner.Points[i].P);
                    float dist = (float)Math.Sqrt((a.X - e.X) * (a.X - e.X) + (a.Y - e.Y) * (a.Y - e.Y));
                    if (dist < best) { best = dist; drag = i; }
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (drag < 0) return;
                var pts = owner.Points;
                var r = Plot;
                // les points restent ordonnés et la vitesse ne descend jamais quand la température monte
                float t = MinT + (e.X - r.Left) / r.Width * (MaxT - MinT);
                float p = (r.Bottom - e.Y) / r.Height * 100;
                float loT = drag > 0 ? pts[drag - 1].T + 1 : MinT, hiT = drag < pts.Count - 1 ? pts[drag + 1].T - 1 : FanRules.HotCpu - 1;
                float loP = drag > 0 ? pts[drag - 1].P : FanRules.MinPercent, hiP = drag < pts.Count - 1 ? pts[drag + 1].P : 100;
                pts[drag].T = (float)Math.Round(Math.Max(loT, Math.Min(hiT, t)));
                pts[drag].P = drag == pts.Count - 1 ? 100 : (float)Math.Round(Math.Max(loP, Math.Min(hiP, p)));
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = -1; }
        }
    }
}
