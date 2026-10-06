using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MidiSoundController
{
    /// <summary>Matrice de routage : une ligne par entrée, une colonne par sortie, un clic = une liaison.</summary>
    class RouteMatrix : DarkControl
    {
        readonly RouterPage page;
        List<RouteInput> ins = new List<RouteInput>();
        List<RouteOutput> outs = new List<RouteOutput>();
        readonly Dictionary<string, float> level = new Dictionary<string, float>();
        int hoverR = -1, hoverC = -1;

        int RowHead { get { return Theme.S(250); } }
        int ColHead { get { return Theme.S(64); } }
        int CellW { get { return Theme.S(130); } }
        int CellH { get { return Theme.S(56); } }

        public RouteMatrix(RouterPage page)
        {
            this.page = page;
        }

        public void Reload()
        {
            lock (AppConfig.Sync)
            {
                ins = new List<RouteInput>(Host.Cfg.Router.Inputs);
                outs = new List<RouteOutput>(Host.Cfg.Router.Outputs);
            }
            Width = Math.Max(Theme.S(400), RowHead + outs.Count * CellW + Theme.S(20));
            Height = ColHead + Math.Max(1, ins.Count) * CellH + Theme.S(20);
            Invalidate();
        }

        /// <summary>Vumètre simplifié des entrées (pastille de signal).</summary>
        public void Tick(AudioRouter router)
        {
            bool changed = false;
            foreach (var i in ins)
            {
                float l = 0, r = 0;
                if (router != null) router.ReadPeaks(true, i.Id, out l, out r);
                float v = Math.Max(l, r), old;
                level.TryGetValue(i.Id, out old);
                float n = Db.Decay(v, old, 0.8f);
                if (Db.Differs(n, old)) changed = true;
                level[i.Id] = n;
            }
            if (changed) Invalidate(new Rectangle(0, ColHead, RowHead, Height));
        }

        bool Cell(Point p, out int r, out int c)
        {
            r = (p.Y - ColHead) / CellH;
            c = (p.X - RowHead) / CellW;
            return p.X >= RowHead && p.Y >= ColHead && r < ins.Count && c < outs.Count;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int r, c;
            if (!Cell(e.Location, out r, out c)) r = c = -1;
            if (r != hoverR || c != hoverC)
            {
                hoverR = r;
                hoverC = c;
                Cursor = r >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverR = hoverC = -1;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int r, c;
            if (e.Button != MouseButtons.Left || !Cell(e.Location, out r, out c)) return;
            var i = ins[r];
            var o = outs[c];
            if (AudioRouter.IsFeedback(i, o)) return;
            lock (AppConfig.Sync) { if (!i.Buses.Remove(o.Id)) i.Buses.Add(o.Id); }
            Invalidate();
            page.TopologyChanged();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            if (ins.Count == 0 || outs.Count == 0)
            {
                TextRenderer.DrawText(g, ins.Count == 0 ? "Ajoutez d'abord une entrée (« + Entrée » ou « Virtuels »)." : "Ajoutez une sortie (« + Sortie » ou « Virtuels »).",
                    Theme.Ui(10f), new Rectangle(0, 0, Width, Theme.S(40)), Theme.Muted, flags);
                return;
            }

            // En-têtes de colonnes (sorties)
            TextRenderer.DrawText(g, "Cliquez une case pour envoyer l'entrée (ligne) vers la sortie (colonne).", Theme.Ui(8.5f),
                new Rectangle(0, Theme.S(4), RowHead - Theme.S(10), Theme.S(36)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
            TextRenderer.DrawText(g, "ENTRÉES  ↓      SORTIES  →", Theme.Ui(7.5f, FontStyle.Bold),
                new Rectangle(0, ColHead - Theme.S(22), RowHead, Theme.S(20)), Theme.Dim, flags);
            for (int c = 0; c < outs.Count; c++)
            {
                var r = new Rectangle(RowHead + c * CellW + Theme.S(4), Theme.S(6), CellW - Theme.S(8), ColHead - Theme.S(12));
                if (c == hoverC) Theme.FillRound(g, Theme.Surface, r, Theme.S(6));
                string name, dev;
                lock (AppConfig.Sync) { name = outs[c].Name; dev = outs[c].DeviceName ?? ""; }
                TextRenderer.DrawText(g, name, Theme.Semi(9.5f), new Rectangle(r.X + Theme.S(6), r.Y + Theme.S(4), r.Width - Theme.S(12), Theme.S(22)), Theme.Text, flags);
                TextRenderer.DrawText(g, VirtualDevices.IsVirtual(dev) ? "virtuelle · " + dev : dev, Theme.Ui(7.5f),
                    new Rectangle(r.X + Theme.S(6), r.Y + Theme.S(24), r.Width - Theme.S(12), Theme.S(18)), Theme.Muted, flags);
            }

            for (int ri = 0; ri < ins.Count; ri++)
            {
                var i = ins[ri];
                int y = ColHead + ri * CellH;
                var head = new Rectangle(0, y + Theme.S(4), RowHead - Theme.S(8), CellH - Theme.S(8));
                Theme.FillRound(g, ri == hoverR ? Theme.SurfaceHi : Theme.Card, head, Theme.S(8));
                float lv;
                level.TryGetValue(i.Id, out lv);
                double db = lv > 0.0001f ? 20 * Math.Log10(lv) : -99;
                Color dot = db < -55 ? Theme.Border : db > -3 ? Theme.Red : db > -12 ? Color.FromArgb(255, 200, 60) : Theme.Green;
                int d = Theme.S(10);
                using (var b = new SolidBrush(dot)) g.FillEllipse(b, head.X + Theme.S(12), head.Y + (head.Height - d) / 2, d, d);
                string name, dev;
                bool loop;
                lock (AppConfig.Sync) { name = i.Name; dev = i.DeviceName ?? ""; loop = i.Loopback; }
                TextRenderer.DrawText(g, name, Theme.Semi(9.5f), new Rectangle(head.X + Theme.S(32), head.Y + Theme.S(4), head.Width - Theme.S(36), Theme.S(22)), Theme.Text, flags);
                TextRenderer.DrawText(g, (loop ? "↺ " : "") + (VirtualDevices.IsVirtual(dev) ? "virtuelle · " : "") + dev, Theme.Ui(7.5f),
                    new Rectangle(head.X + Theme.S(32), head.Y + Theme.S(24), head.Width - Theme.S(36), Theme.S(18)), Theme.Muted, flags);

                for (int c = 0; c < outs.Count; c++)
                {
                    var o = outs[c];
                    var cell = new Rectangle(RowHead + c * CellW + Theme.S(4), y + Theme.S(4), CellW - Theme.S(8), CellH - Theme.S(8));
                    bool fb = AudioRouter.IsFeedback(i, o), on;
                    lock (AppConfig.Sync) on = i.Buses.Contains(o.Id);
                    bool hov = ri == hoverR && c == hoverC && !fb;
                    if (fb)
                    {
                        Theme.FillRound(g, Theme.Mix(Theme.Bg, Theme.Card, 0.5f), cell, Theme.S(8));
                        TextRenderer.DrawText(g, "boucle", Theme.Ui(8f), cell, Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        continue;
                    }
                    Theme.FillRound(g, on ? Theme.Accent : hov ? Theme.SurfaceHi : Theme.Surface, cell, Theme.S(8));
                    if (!on) Theme.DrawRound(g, hov ? Theme.Accent : Theme.Border, new RectangleF(cell.X + 0.5f, cell.Y + 0.5f, cell.Width - 1, cell.Height - 1), Theme.S(8));
                    TextRenderer.DrawText(g, on ? Glyphs.Check : Glyphs.Add, Theme.Icon(on ? 11f : 9f),
                        new Rectangle(cell.X, cell.Y + Theme.S(6), cell.Width, cell.Height / 2), on ? Theme.OnAccent : hov ? Theme.Accent : Theme.Dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    TextRenderer.DrawText(g, on ? "envoyé" : "relier", Theme.Ui(7.5f, on ? FontStyle.Bold : FontStyle.Regular),
                        new Rectangle(cell.X, cell.Y + cell.Height / 2, cell.Width, cell.Height / 2 - Theme.S(4)), on ? Theme.OnAccent : hov ? Theme.Accent : Theme.Dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }
}
