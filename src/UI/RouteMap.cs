using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Cartographie du routage : entrées à gauche, sorties à droite, une courbe par liaison.</summary>
    class RouteMap : DarkControl
    {
        List<RouteInput> ins = new List<RouteInput>();
        List<RouteOutput> outs = new List<RouteOutput>();
        readonly Dictionary<string, float> level = new Dictionary<string, float>();
        readonly Dictionary<string, RectangleF> nodes = new Dictionary<string, RectangleF>();
        string hover;

        static float NodeW { get { return Theme.S(240); } }
        static float NodeH { get { return Theme.S(58); } }
        static float Gap { get { return Theme.S(14); } }

        public void Reload()
        {
            lock (AppConfig.Sync)
            {
                ins = new List<RouteInput>(Host.Cfg.Router.Inputs);
                outs = new List<RouteOutput>(Host.Cfg.Router.Outputs);
            }
            int rows = Math.Max(ins.Count, outs.Count);
            Height = Math.Max(Theme.S(300), Theme.S(50) + (int)(rows * (NodeH + Gap)) + Theme.S(20));
            Invalidate();
        }

        public void Tick(AudioRouter router)
        {
            bool changed = false;
            Action<bool, string> read = (input, id) =>
            {
                float l = 0, r = 0;
                if (router != null) router.ReadPeaks(input, id, out l, out r);
                float v = Math.Max(l, r), old;
                level.TryGetValue(id, out old);
                float n = Db.Decay(v, old, 0.85f);
                if (Db.Differs(n, old)) changed = true;
                level[id] = n;
            };
            foreach (var i in ins) read(true, i.Id);
            foreach (var o in outs) read(false, o.Id);
            if (changed) Invalidate();
        }

        float Level(string id)
        {
            float v;
            return level.TryGetValue(id, out v) ? v : 0;
        }

        void ComputeLayout(out float xin, out float xout, out float top)
        {
            xin = Theme.S(4);
            xout = Math.Max(xin + NodeW + Theme.S(160), Width - NodeW - Theme.S(8));
            top = Theme.S(36);
            nodes.Clear();
            int rows = Math.Max(ins.Count, outs.Count);
            float colH = rows * (NodeH + Gap);
            float yi = top + (colH - ins.Count * (NodeH + Gap)) / 2, yo = top + (colH - outs.Count * (NodeH + Gap)) / 2;
            foreach (var i in ins) { nodes["in:" + i.Id] = new RectangleF(xin, yi, NodeW, NodeH); yi += NodeH + Gap; }
            foreach (var o in outs) { nodes["out:" + o.Id] = new RectangleF(xout, yo, NodeW, NodeH); yo += NodeH + Gap; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string h = null;
            foreach (var kv in nodes) if (kv.Value.Contains(e.Location)) { h = kv.Key; break; }
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = null;
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (ins.Count == 0 && outs.Count == 0)
            {
                TextRenderer.DrawText(g, "Rien à afficher : ajoutez des entrées et des sorties.", Theme.Ui(10f), new Rectangle(0, 0, Width, Theme.S(40)), Theme.Muted, flags);
                return;
            }
            float xin, xout, top;
            ComputeLayout(out xin, out xout, out top);
            TextRenderer.DrawText(g, "ENTRÉES", Theme.Ui(8f, FontStyle.Bold), new Rectangle((int)xin, 0, (int)NodeW, (int)top - Theme.S(8)), Theme.Dim, flags);
            TextRenderer.DrawText(g, "SORTIES", Theme.Ui(8f, FontStyle.Bold), new Rectangle((int)xout, 0, (int)NodeW, (int)top - Theme.S(8)), Theme.Dim, flags);

            bool running;
            lock (AppConfig.Sync) running = Host.Cfg.Router.Running;

            // Liaisons (sous les nœuds)
            foreach (var i in ins)
            {
                List<string> buses;
                bool inMute;
                lock (AppConfig.Sync) { buses = new List<string>(i.Buses); inMute = i.Mute; }
                foreach (var b in buses)
                {
                    var o = outs.Find(x => x.Id == b);
                    RectangleF a, z;
                    if (o == null || AudioRouter.IsFeedback(i, o) || !nodes.TryGetValue("in:" + i.Id, out a) || !nodes.TryGetValue("out:" + o.Id, out z)) continue;
                    bool outMute;
                    lock (AppConfig.Sync) outMute = o.Mute;
                    bool focus = hover == null || hover == "in:" + i.Id || hover == "out:" + o.Id;
                    float sig = running && !inMute ? Math.Min(1f, Level(i.Id) * 1.5f) : 0f;
                    Color c = inMute || outMute || !running ? Theme.Dim : Theme.Mix(Theme.Mix(Theme.Card, Theme.Accent, 0.65f), Theme.Accent, sig);
                    if (!focus) c = Color.FromArgb(50, c);
                    var p1 = new PointF(a.Right, a.Y + a.Height / 2);
                    var p2 = new PointF(z.Left, z.Y + z.Height / 2);
                    float dx = (p2.X - p1.X) * 0.45f;
                    using (var pen = new Pen(c, Theme.S(focus && hover != null ? 3.5f : 2.2f + sig * 2f)))
                    {
                        if (inMute || outMute) pen.DashStyle = DashStyle.Dash;
                        pen.StartCap = pen.EndCap = LineCap.Round;
                        g.DrawBezier(pen, p1, new PointF(p1.X + dx, p1.Y), new PointF(p2.X - dx, p2.Y), p2);
                    }
                }
            }

            foreach (var i in ins) DrawNode(g, "in:" + i.Id, i, true, i.Loopback ? Glyphs.Loop : Glyphs.Mic);
            foreach (var o in outs) DrawNode(g, "out:" + o.Id, o, false, Glyphs.Speaker);

            if (!running)
                TextRenderer.DrawText(g, "Moteur arrêté", Theme.Semi(10f), new Rectangle((int)(xin + NodeW), 0, (int)(xout - xin - NodeW), (int)top),
                    Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void DrawNode(Graphics g, string key, RouteNode n, bool input, string glyph)
        {
            RectangleF r;
            if (!nodes.TryGetValue(key, out r)) return;
            string name, dev;
            bool mute;
            lock (AppConfig.Sync) { name = n.Name; dev = n.DeviceName ?? ""; mute = n.Mute; }
            bool virt = VirtualDevices.IsVirtual(dev) || (n.DeviceId != null && n.DeviceId.StartsWith("vdev:"));
            bool hov = hover == key;
            Theme.FillRound(g, hov ? Theme.SurfaceHi : Theme.Card, r, Theme.S(9));
            Theme.DrawRound(g, hov ? Theme.Accent : Theme.Border, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.S(9));

            // port de connexion
            float pr = Theme.S(5);
            var port = new RectangleF((input ? r.Right : r.Left) - pr, r.Y + r.Height / 2 - pr, pr * 2, pr * 2);
            using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, port);

            var gl = new Rectangle((int)r.X + Theme.S(10), (int)r.Y, Theme.S(24), (int)r.Height);
            TextRenderer.DrawText(g, glyph, Theme.Icon(12f), gl, mute ? Theme.Red : Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int tx = (int)r.X + Theme.S(42), tw = (int)r.Width - Theme.S(54);
            TextRenderer.DrawText(g, name + (mute ? "  (muet)" : ""), Theme.Semi(9.5f), new Rectangle(tx, (int)r.Y + Theme.S(7), tw, Theme.S(20)), mute ? Theme.Muted : Theme.Text, flags);
            TextRenderer.DrawText(g, (virt ? "virtuel · " : "") + dev, Theme.Ui(7.5f), new Rectangle(tx, (int)r.Y + Theme.S(27), tw, Theme.S(16)), Theme.Muted, flags);

            // niveau
            float lv = Level(n.Id);
            if (lv > 0.001f)
            {
                double db = 20 * Math.Log10(lv);
                float t = (float)Math.Max(0, Math.Min(1, (db + 60) / 60));
                var bar = new RectangleF(tx, r.Bottom - Theme.S(9), tw * t, Theme.S(3));
                Theme.FillRound(g, db > -3 ? Theme.Red : db > -12 ? Color.FromArgb(255, 200, 60) : Theme.Green, bar, Theme.S(1.5f));
            }
        }
    }
}
