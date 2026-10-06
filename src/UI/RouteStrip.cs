using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Tranche de console : entrée ou sortie du routage, dessinée à la main.</summary>
    class RouteStrip : DarkControl
    {
        public readonly RouteNode Node;
        public readonly bool IsInput;
        readonly RouterPage page;

        Rectangle rName, rMenu, rDev, rStatus, rFader, rDb, rMute, rDelay, rBusCap;
        readonly List<KeyValuePair<RouteOutput, Rectangle>> busRects = new List<KeyValuePair<RouteOutput, Rectangle>>();
        int trackTop, trackBot;
        bool dragging, headerDown, suppressClick;
        string hover;
        float meterL, meterR;

        public static int StripWidth { get { return Theme.S(150); } }

        public RouteStrip(RouterPage page, RouteNode node, bool isInput)
        {
            this.page = page;
            Node = node;
            IsInput = isInput;
            Width = StripWidth;
        }

        RouteInput In { get { return Node as RouteInput; } }
        RouteOutput Out { get { return Node as RouteOutput; } }

        public void Relayout()
        {
            int pad = Theme.S(12), w = Width, h = Height, inner = w - 2 * pad;
            rMenu = new Rectangle(w - pad - Theme.S(24), Theme.S(10), Theme.S(26), Theme.S(26));
            rName = new Rectangle(pad, Theme.S(10), inner - Theme.S(26), Theme.S(24));
            rDev = new Rectangle(pad, Theme.S(36), inner, Theme.S(18));
            rStatus = new Rectangle(pad, Theme.S(54), inner, Theme.S(16));
            rMute = new Rectangle(pad, h - pad - Theme.S(30), inner, Theme.S(30));
            int y = rMute.Top - Theme.S(6);
            busRects.Clear();
            if (IsInput)
            {
                var outs = page.Outputs();
                for (int i = outs.Count - 1; i >= 0; i--)
                {
                    var r = new Rectangle(pad, y - Theme.S(28), inner, Theme.S(28));
                    busRects.Insert(0, new KeyValuePair<RouteOutput, Rectangle>(outs[i], r));
                    y = r.Top - Theme.S(4);
                }
                rBusCap = new Rectangle(pad, y - Theme.S(18), inner, Theme.S(16));
                y = rBusCap.Top - Theme.S(4);
                rDelay = Rectangle.Empty;
            }
            else
            {
                rDelay = new Rectangle(pad, y - Theme.S(28), inner, Theme.S(28));
                y = rDelay.Top - Theme.S(5);
            }
            rDb = new Rectangle(pad, y - Theme.S(24), inner, Theme.S(22));
            rFader = new Rectangle(pad, Theme.S(78), inner, Math.Max(Theme.S(60), rDb.Top - Theme.S(4) - Theme.S(78)));
            trackTop = rFader.Top + Theme.S(12);
            trackBot = rFader.Bottom - Theme.S(12);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        #region Valeurs

        double Gain { get { lock (AppConfig.Sync) return Node.Gain; } }

        float DbToY(double db)
        {
            return trackBot - (float)((db - Db.Min) / (Db.Max - Db.Min)) * (trackBot - trackTop);
        }

        double YToDb(int y)
        {
            double t = (trackBot - y) / (double)Math.Max(1, trackBot - trackTop);
            double db = Db.Min + Math.Max(0, Math.Min(1, t)) * (Db.Max - Db.Min);
            return Math.Abs(db) < 1.0 ? 0 : Math.Round(db, 1); // aimantation sur 0 dB
        }

        void SetGain(double db, bool save)
        {
            lock (AppConfig.Sync) Node.Gain = Math.Max(Db.Min, Math.Min(Db.Max, db));
            Invalidate();
            if (save) page.SaveSoon();
        }

        /// <summary>Appelé ~30 fois/s : met à jour les vumètres.</summary>
        public void Tick(AudioRouter router)
        {
            float l = 0, r = 0;
            if (router != null) router.ReadPeaks(IsInput, Node.Id, out l, out r);
            if (IsInput)
            {
                float g;
                lock (AppConfig.Sync) g = Node.Mute ? 0 : Db.ToGain(Node.Gain);
                l *= g;
                r *= g;
            }
            float nl = Db.Decay(l, meterL, 0.82f), nr = Db.Decay(r, meterR, 0.82f);
            bool redraw = Db.Differs(nl, meterL) || Db.Differs(nr, meterR) || dragging;
            meterL = nl;
            meterR = nr;
            if (redraw) Invalidate(rFader);
        }

        #endregion

        #region Souris

        string HitTest(Point p)
        {
            if (rMenu.Contains(p)) return "menu";
            if (rMute.Contains(p)) return "mute";
            if (rDelay.Contains(p)) return "delay";
            if (rName.Contains(p) || rDev.Contains(p)) return "name";
            if (rFader.Contains(p) || rDb.Contains(p)) return "fader";
            for (int i = 0; i < busRects.Count; i++) if (busRects[i].Value.Contains(p)) return "bus" + i;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) { SetGain(YToDb(e.Y), false); return; }
            if (headerDown) { page.DragMove(this); return; }
            string h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                Cursor = h == "fader" ? Cursors.SizeNS : h == "name" || (h == null && e.Y < rFader.Top) ? Cursors.SizeAll : h == null ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Focus(); // pour recevoir la molette
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = null;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            string h = HitTest(e.Location);
            if (h == "fader" && e.Clicks == 1) { dragging = true; Capture = true; SetGain(YToDb(e.Y), false); }
            else if (e.Clicks == 1 && h != "menu" && e.Y < rFader.Top) { headerDown = true; page.DragDown(this, e.Location); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging) { dragging = false; Capture = false; page.SaveSoon(); }
            if (headerDown) { headerDown = false; suppressClick = page.DragUp(this); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            string h = HitTest(e.Location);
            if (h == "fader") SetGain(0, true);
            else if (h == "name") page.Rename(this);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            double step = (ModifierKeys & Keys.Shift) != 0 ? 0.1 : 1.0;
            SetGain(Math.Round(Gain + Math.Sign(e.Delta) * step, 1), true);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (suppressClick) { suppressClick = false; return; }
            if (e.Button == MouseButtons.Right) { page.ShowStripMenu(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            string h = HitTest(e.Location);
            if (h == "menu") page.ShowStripMenu(this, new Point(rMenu.Left, rMenu.Bottom));
            else if (h == "mute")
            {
                lock (AppConfig.Sync) Node.Mute = !Node.Mute;
                Invalidate();
                page.SaveSoon();
                page.Engine.RequestLedSync();
            }
            else if (h == "delay") page.ShowDelayMenu(this, new Point(rDelay.Left, rDelay.Bottom));
            else if (h != null && h.StartsWith("bus"))
            {
                var o = busRects[int.Parse(h.Substring(3))].Key;
                if (AudioRouter.IsFeedback(In, o)) return;
                lock (AppConfig.Sync)
                {
                    if (!In.Buses.Remove(o.Id)) In.Buses.Add(o.Id);
                }
                Invalidate();
                page.TopologyChanged();
            }
        }

        #endregion

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            string name, dev;
            bool mute;
            double gain;
            int delay = 0;
            lock (AppConfig.Sync)
            {
                name = Node.Name;
                dev = Node.DeviceName ?? "Aucun périphérique";
                mute = Node.Mute;
                gain = Node.Gain;
                if (Out != null) delay = Out.DelayMs;
            }
            Theme.FillRound(g, Theme.Card, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(10));
            Color tag = IsInput ? Theme.Accent : Theme.Mix(Theme.Accent, Theme.Green, 0.6f);
            Theme.FillRound(g, tag, new RectangleF(Theme.S(12), 0, Width - Theme.S(24), Theme.S(3)), Theme.S(1.5f));

            // En-tête
            TextRenderer.DrawText(g, name, Theme.Semi(10.5f), rName, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (hover == "menu") Theme.FillRound(g, Theme.SurfaceHi, rMenu, Theme.S(5));
            TextRenderer.DrawText(g, Glyphs.More, Theme.Icon(9f), rMenu, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            string devText = (In != null && In.Loopback ? "↺ " : "") + dev;
            TextRenderer.DrawText(g, devText, Theme.Ui(8f), rDev, Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            string status = page.Router == null ? null : page.Router.Status(IsInput, Node.Id);
            string st;
            Color stc = Theme.Dim;
            if (status == null) st = page.Running ? (IsInput ? "Inactive" : "Aucune entrée") : "Moteur arrêté";
            else if (status == "OK") { st = page.Router.FormatText(IsInput, Node.Id) ?? "OK"; stc = Theme.Green; }
            else { st = status; stc = Theme.Red; }
            int badgeW = 0;
            if (VirtualDevices.IsVirtual(dev) || (Node.DeviceId != null && Node.DeviceId.StartsWith("vdev:")))
            {
                var f = Theme.Ui(6.5f, FontStyle.Bold);
                badgeW = TextRenderer.MeasureText("VIRTUEL", f).Width + Theme.S(6);
                var br = new Rectangle(rStatus.Right - badgeW, rStatus.Y + Theme.S(1), badgeW, rStatus.Height - Theme.S(2));
                Theme.FillRound(g, Theme.Mix(Theme.Card, Theme.Accent, 0.25f), br, Theme.S(4));
                TextRenderer.DrawText(g, "VIRTUEL", f, br, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, st, Theme.Ui(7.5f), new Rectangle(rStatus.X, rStatus.Y, rStatus.Width - badgeW - Theme.S(4), rStatus.Height), stc,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            DrawFader(g, gain);

            // dB
            string dbText = gain <= Db.Min ? "-∞ dB" : (gain > 0 ? "+" : "") + gain.ToString("0.0") + " dB";
            TextRenderer.DrawText(g, dbText, Theme.Semi(9.5f), rDb, gain > 0 ? Theme.Accent : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // Envois vers les sorties : de vrais interrupteurs
            if (IsInput)
                TextRenderer.DrawText(g, busRects.Count == 0 ? "AJOUTEZ UNE SORTIE →" : "ENVOYER VERS", Theme.Ui(7f, FontStyle.Bold), rBusCap, Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            for (int i = 0; i < busRects.Count; i++)
            {
                var o = busRects[i].Key;
                var r = busRects[i].Value;
                bool on, fb = AudioRouter.IsFeedback(In, o);
                string oname;
                lock (AppConfig.Sync) { on = In.Buses.Contains(o.Id); oname = o.Name; }
                bool hov = hover == "bus" + i && !fb;
                Color bg = on ? Theme.Mix(Theme.Card, Theme.Accent, hov ? 0.35f : 0.25f) : hov ? Theme.SurfaceHi : Theme.Surface;
                Theme.FillRound(g, bg, r, Theme.S(5));
                if (on) Theme.DrawRound(g, Theme.Accent, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.S(5));

                // mini interrupteur
                int tw = Theme.S(24), th = Theme.S(14);
                var tr = new RectangleF(r.Right - tw - Theme.S(7), r.Y + (r.Height - th) / 2f, tw, th);
                Theme.FillRound(g, fb ? Theme.Border : on ? Theme.Accent : Theme.SurfaceHi, tr, th / 2f);
                float k = th - Theme.S(4);
                using (var b = new SolidBrush(on ? Theme.OnAccent : Theme.Muted))
                    g.FillEllipse(b, on ? tr.Right - k - Theme.S(2) : tr.X + Theme.S(2), tr.Y + Theme.S(2), k, k);

                TextRenderer.DrawText(g, fb ? oname + " (boucle)" : oname, Theme.Ui(8.5f, on ? FontStyle.Bold : FontStyle.Regular),
                    new Rectangle(r.X + Theme.S(8), r.Y, r.Width - tw - Theme.S(20), r.Height),
                    fb ? Theme.Dim : on ? Theme.Text : Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (!rDelay.IsEmpty)
            {
                Theme.FillRound(g, hover == "delay" ? Theme.SurfaceHi : Theme.Surface, rDelay, Theme.S(5));
                TextRenderer.DrawText(g, "Délai : " + delay + " ms", Theme.Ui(8.5f), rDelay, delay > 0 ? Theme.Text : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // Muet
            Color mbg = mute ? Theme.Red : hover == "mute" ? Theme.SurfaceHi : Theme.Surface;
            Theme.FillRound(g, mbg, rMute, Theme.S(6));
            TextRenderer.DrawText(g, mute ? "MUET" : "Muet", Theme.Semi(9f), rMute, mute ? Color.White : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void DrawFader(Graphics g, double gain)
        {
            // Vumètres stéréo
            int mw = Theme.S(6), mx = rFader.Left + Theme.S(2);
            DrawMeter(g, new Rectangle(mx, trackTop, mw, trackBot - trackTop), meterL);
            DrawMeter(g, new Rectangle(mx + mw + Theme.S(3), trackTop, mw, trackBot - trackTop), meterR);

            // Graduations
            int cx = rFader.Left + rFader.Width / 2 + Theme.S(4);
            using (var pen = new Pen(Theme.Border, 1f))
                foreach (int db in new[] { 12, 6, 0, -6, -12, -24, -36, -48, -60 })
                {
                    float y = DbToY(db);
                    g.DrawLine(pen, cx + Theme.S(10), y, cx + Theme.S(16), y);
                    TextRenderer.DrawText(g, db > 0 ? "+" + db : db.ToString(), Theme.Ui(6.5f),
                        new Rectangle(cx + Theme.S(18), (int)y - Theme.S(8), Theme.S(30), Theme.S(16)),
                        db == 0 ? Theme.Muted : Theme.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                }

            // Rail + niveau
            var slot = new RectangleF(cx - Theme.S(2.5f), trackTop, Theme.S(5), trackBot - trackTop);
            Theme.FillRound(g, Theme.Mix(Theme.Bg, Color.Black, 0.5f), slot, Theme.S(2.5f));
            float capY = DbToY(gain);
            if (gain > Db.Min)
                Theme.FillRound(g, Color.FromArgb(200, Theme.Accent), new RectangleF(slot.X, capY, slot.Width, trackBot - capY), Theme.S(2.5f));
            float zero = DbToY(0);
            using (var pen = new Pen(Theme.Muted, 1f)) g.DrawLine(pen, cx - Theme.S(9), zero, cx - Theme.S(5), zero);

            // Curseur
            float cw = Theme.S(34), ch = Theme.S(20);
            var cap = new RectangleF(cx - cw / 2, capY - ch / 2, cw, ch);
            using (var p = Theme.Round(cap, Theme.S(4)))
            using (var br = new LinearGradientBrush(cap, Theme.Mix(Theme.SurfaceHi, Color.White, 0.25f), Theme.SurfaceHi, 90f))
            {
                g.FillPath(br, p);
                using (var pen = new Pen(hover == "fader" || dragging ? Theme.Accent : Theme.Mix(Theme.SurfaceHi, Color.White, 0.3f), 1f)) g.DrawPath(pen, p);
            }
            using (var pen = new Pen(Theme.Text, Math.Max(1.5f, Theme.S(2))))
                g.DrawLine(pen, cap.X + Theme.S(5), capY, cap.Right - Theme.S(5), capY);
        }

        static void DrawMeter(Graphics g, Rectangle r, float peak)
        {
            using (var b = new SolidBrush(Theme.Mix(Theme.Bg, Color.Black, 0.4f))) g.FillRectangle(b, r);
            if (peak <= 0.0001f) return;
            double db = 20 * Math.Log10(peak);
            float t = (float)Math.Max(0, Math.Min(1, (db + 60) / 60.0));
            int h = (int)(r.Height * t);
            var lit = new Rectangle(r.X, r.Bottom - h, r.Width, h);
            using (var br = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), Theme.Red, Theme.Green, 90f))
            {
                var blend = new ColorBlend
                {
                    Colors = new[] { Theme.Red, Color.FromArgb(255, 200, 60), Theme.Green, Theme.Green },
                    Positions = new[] { 0f, 0.1f, 0.3f, 1f },
                };
                br.InterpolationColors = blend;
                g.FillRectangle(br, lit);
            }
        }
    }
}
