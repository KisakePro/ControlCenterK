using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Page du module "Routage audio" : console entrées → sorties façon Voicemeeter.</summary>
    class RouterPage : Panel
    {
        public readonly Engine Engine;
        readonly AppConfig cfg;
        readonly Label title, sub, lblEngine, lblStatus, capIn, capOut, hint;
        readonly Toggle tglRunning;
        readonly FlatButton btnAddIn, btnAddOut, btnVirtual, tabConsole, tabMatrix, tabMap;
        readonly Panel strips, matrixPanel, mapPanel;
        readonly RouteMatrix matrix;
        readonly RouteMap map;
        readonly Panel divider;
        readonly List<RouteStrip> stripList = new List<RouteStrip>();
        readonly Timer timer;
        int statusTick;

        public RouterPage(Engine engine)
        {
            Engine = engine;
            cfg = engine.Cfg;
            BackColor = Theme.Bg;

            title = Theme.Label("Routage audio", Theme.Semi(18f), Theme.Text, BackColor);
            sub = Theme.Label("Envoyez vos entrées (micros, ou ce qui est joué sur une sortie) vers une ou plusieurs sorties en même temps.",
                Theme.Ui(9.5f), Theme.Muted, BackColor);
            lblEngine = Theme.Label("Moteur", Theme.Semi(10f), Theme.Text, BackColor);
            tglRunning = new Toggle();
            lock (AppConfig.Sync) tglRunning.Checked = cfg.Router.Running;
            tglRunning.CheckedChanged += (s, e) =>
            {
                lock (AppConfig.Sync) cfg.Router.Running = tglRunning.Checked;
                cfg.Save();
                if (Router != null) Router.Sync();
                foreach (var st in stripList) st.Invalidate();
            };
            lblStatus = Theme.Label("", Theme.Ui(8.5f), Theme.Muted, BackColor);
            btnAddIn = new FlatButton("Entrée", true) { Glyph = Glyphs.Add };
            btnAddIn.FitWidth();
            btnAddIn.Click += (s, e) => ShowAddMenu(true, btnAddIn);
            btnAddOut = new FlatButton("Sortie", true) { Glyph = Glyphs.Add };
            btnAddOut.FitWidth();
            btnAddOut.Click += (s, e) => ShowAddMenu(false, btnAddOut);
            btnVirtual = new FlatButton("Virtuels") { Glyph = Glyphs.Route };
            btnVirtual.FitWidth();
            btnVirtual.Click += (s, e) => ShowVirtual();
            tabConsole = new FlatButton("Console") { Glyph = Glyphs.Mixer };
            tabConsole.FitWidth();
            tabConsole.Click += (s, e) => SetView("console");
            tabMatrix = new FlatButton("Matrice de routage") { Glyph = "\uE80A" };
            tabMatrix.FitWidth();
            tabMatrix.Click += (s, e) => SetView("matrix");
            matrixPanel = new Panel { AutoScroll = true, BackColor = Theme.Bg, Visible = false };
            matrixPanel.HandleCreated += (s, e) => Theme.DarkScroll(matrixPanel);
            matrix = new RouteMatrix(this) { Location = Point.Empty };
            matrixPanel.Controls.Add(matrix);
            tabMap = new FlatButton("Cartographie") { Glyph = "\uE8F1" };
            tabMap.FitWidth();
            tabMap.Click += (s, e) => SetView("map");
            mapPanel = new Panel { AutoScroll = true, BackColor = Theme.Bg, Visible = false };
            mapPanel.HandleCreated += (s, e) => Theme.DarkScroll(mapPanel);
            map = new RouteMap { Location = Point.Empty };
            mapPanel.Controls.Add(map);
            mapPanel.Resize += (s, e) => { map.Width = Math.Max(Theme.S(700), mapPanel.ClientSize.Width - Theme.S(4)); map.Invalidate(); };

            strips = new Panel { AutoScroll = true, BackColor = Theme.Bg };
            strips.HandleCreated += (s, e) => Theme.DarkScroll(strips);
            strips.Resize += (s, e) => LayoutStrips();
            capIn = Theme.Label("ENTRÉES", Theme.Ui(8f, FontStyle.Bold), Theme.Dim, Theme.Bg);
            capOut = Theme.Label("SORTIES", Theme.Ui(8f, FontStyle.Bold), Theme.Dim, Theme.Bg);
            divider = new Panel { BackColor = Theme.Border, Width = 1 };
            hint = Theme.Label("", Theme.Ui(9f), Theme.Muted, Theme.Bg);
            strips.Controls.AddRange(new Control[] { capIn, capOut, divider, hint });

            Controls.AddRange(new Control[] { title, sub, lblEngine, tglRunning, lblStatus, btnAddIn, btnAddOut, btnVirtual, tabConsole, tabMatrix, tabMap, strips, matrixPanel, mapPanel });
            string view;
            lock (AppConfig.Sync) view = cfg.Router.View;
            ApplyView(view == "matrix" || view == "map" ? view : "console");

            timer = new Timer { Interval = 33 };
            timer.Tick += (s, e) => OnTick();
            VisibleChanged += (s, e) => { if (Visible) timer.Start(); else timer.Stop(); };
            Rebuild();
        }

        public AudioRouter Router { get { return Host.Router; } }
        public bool Running { get { lock (AppConfig.Sync) return cfg.Router.Running; } }

        public List<RouteOutput> Outputs()
        {
            lock (AppConfig.Sync) return new List<RouteOutput>(cfg.Router.Outputs);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        #region Mise en page

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (strips == null) return;
            int pad = Theme.S(28), w = ClientSize.Width;
            title.Location = new Point(pad - Theme.S(2), Theme.S(20));
            btnAddOut.Location = new Point(w - pad - btnAddOut.Width, Theme.S(28));
            btnAddIn.Location = new Point(btnAddOut.Left - Theme.S(8) - btnAddIn.Width, Theme.S(28));
            btnVirtual.Location = new Point(btnAddIn.Left - Theme.S(8) - btnVirtual.Width, Theme.S(28));
            tglRunning.Location = new Point(btnVirtual.Left - Theme.S(28) - tglRunning.Width, Theme.S(33));
            lblEngine.Location = new Point(tglRunning.Left - Theme.S(8) - lblEngine.Width, Theme.S(33));
            lblStatus.Location = new Point(lblEngine.Left, tglRunning.Bottom + Theme.S(8));
            sub.MaximumSize = new Size(Math.Max(Theme.S(200), lblEngine.Left - pad - Theme.S(20)), 0);
            sub.Location = new Point(pad, title.Bottom + Theme.S(2));
            int tabsY = Math.Max(sub.Bottom, lblStatus.Bottom) + Theme.S(14);
            tabConsole.Location = new Point(pad, tabsY);
            tabMatrix.Location = new Point(tabConsole.Right + Theme.S(6), tabsY);
            tabMap.Location = new Point(tabMatrix.Right + Theme.S(6), tabsY);
            int top = tabConsole.Bottom + Theme.S(16);
            strips.SetBounds(pad, top, w - pad - Theme.S(12), ClientSize.Height - top - Theme.S(12));
            matrixPanel.Bounds = strips.Bounds;
            mapPanel.Bounds = strips.Bounds;
        }

        void LayoutStrips()
        {
            int x = strips.AutoScrollPosition.X, y0 = strips.AutoScrollPosition.Y;
            int capH = Theme.S(24);
            int h = Math.Max(Theme.S(420), strips.ClientSize.Height - capH - Theme.S(4));
            int gap = Theme.S(10);
            capIn.Location = new Point(x + Theme.S(2), y0);
            bool anyIn = stripList.Exists(s => s.IsInput);
            LayoutGroup(true, ref x, y0 + capH, h, gap);
            if (!anyIn) x += Theme.S(160);
            x += Theme.S(14);
            divider.SetBounds(x, y0 + capH, 1, h);
            x += Theme.S(24);
            capOut.Location = new Point(x + Theme.S(2), y0);
            LayoutGroup(false, ref x, y0 + capH, h, gap);
            hint.Visible = stripList.Count == 0 || !anyIn || stripList.TrueForAll(s => s.IsInput);
            hint.MaximumSize = new Size(Theme.S(420), 0);
            if (!anyIn) { hint.Text = "Ajoutez une entrée (bouton « + Entrée ») : un micro, ou une sortie en boucle pour en récupérer le son."; hint.Location = new Point(capIn.Left, y0 + capH + Theme.S(8)); hint.MaximumSize = new Size(Theme.S(150), 0); }
            else if (stripList.TrueForAll(s => s.IsInput)) { hint.Text = "Ajoutez une ou plusieurs sorties (casque, enceintes…) avec « + Sortie »."; hint.Location = new Point(capOut.Left, y0 + capH + Theme.S(8)); }
        }

        /// <summary>Place les tranches d'un groupe ; pendant un glisser, laisse un trou à l'endroit de dépôt.</summary>
        void LayoutGroup(bool input, ref int x, int y, int h, int gap)
        {
            int w = RouteStrip.StripWidth, i = 0;
            bool dragging = dragActive && dragStrip != null && dragStrip.IsInput == input;
            foreach (var s in stripList)
            {
                if (s.IsInput != input) continue;
                if (dragging && s == dragStrip) continue;
                if (dragging && i == dragTarget) x += w + gap;
                s.SetBounds(x, y, w, h);
                x += w + gap;
                i++;
            }
            if (dragging && i == dragTarget) x += w + gap;
        }

        #region Glisser-déposer des tranches

        RouteStrip dragStrip;
        bool dragActive;
        int dragTarget, dragOffset;
        Point dragStart;

        public void DragDown(RouteStrip s, Point local)
        {
            dragStrip = s;
            dragActive = false;
            dragOffset = local.X;
            dragStart = Cursor.Position;
        }

        public void DragMove(RouteStrip s)
        {
            if (dragStrip != s) return;
            var cp = Cursor.Position;
            if (!dragActive)
            {
                if (Math.Abs(cp.X - dragStart.X) < Theme.S(8)) return;
                dragActive = true;
                s.BringToFront();
                s.Cursor = Cursors.SizeWE;
            }
            var pt = strips.PointToClient(cp);
            s.Left = pt.X - dragOffset;
            // position de dépôt : nombre de tranches du groupe dont le centre est à gauche du centre de la tranche glissée
            int center = s.Left + s.Width / 2, idx = 0;
            foreach (var o in stripList)
                if (o != s && o.IsInput == s.IsInput && o.Left + o.Width / 2 < center) idx++;
            dragTarget = idx;
            LayoutStrips();
            s.Left = pt.X - dragOffset;
        }

        /// <summary>Fin du glisser. Renvoie vrai si une tranche a réellement été déplacée (le clic est alors ignoré).</summary>
        public bool DragUp(RouteStrip s)
        {
            if (dragStrip != s) return false;
            bool moved = dragActive;
            dragStrip = null;
            dragActive = false;
            s.Cursor = Cursors.Default;
            if (!moved) return false;
            lock (AppConfig.Sync)
            {
                if (s.IsInput)
                {
                    var list = cfg.Router.Inputs;
                    var n = (RouteInput)s.Node;
                    list.Remove(n);
                    list.Insert(Math.Min(dragTarget, list.Count), n);
                }
                else
                {
                    var list = cfg.Router.Outputs;
                    var n = (RouteOutput)s.Node;
                    list.Remove(n);
                    list.Insert(Math.Min(dragTarget, list.Count), n);
                }
            }
            cfg.Save();
            BeginInvoke(new Action(Rebuild)); // la tranche relâchée est encore en train de traiter son événement souris
            return true;
        }

        #endregion

        /// <summary>Recrée toutes les tranches (après ajout / suppression).</summary>
        public void Rebuild()
        {
            strips.SuspendLayout();
            foreach (var s in stripList) { strips.Controls.Remove(s); s.Dispose(); }
            stripList.Clear();
            List<RouteInput> ins;
            List<RouteOutput> outs;
            lock (AppConfig.Sync)
            {
                ins = new List<RouteInput>(cfg.Router.Inputs);
                outs = new List<RouteOutput>(cfg.Router.Outputs);
            }
            foreach (var i in ins) stripList.Add(new RouteStrip(this, i, true));
            foreach (var o in outs) stripList.Add(new RouteStrip(this, o, false));
            foreach (var s in stripList) strips.Controls.Add(s);
            LayoutStrips();
            strips.ResumeLayout();
            matrix.Reload();
            map.Reload();
            UpdateStatus();
        }

        void OnTick()
        {
            var r = Router;
            if (matrixPanel.Visible) matrix.Tick(r);
            else if (mapPanel.Visible) map.Tick(r);
            else foreach (var s in stripList) s.Tick(r);
            if (++statusTick % 15 == 0)
            {
                UpdateStatus();
                foreach (var s in stripList) s.Invalidate(); // rafraîchit les états (périphérique, valeurs pilotées en MIDI)
            }
        }

        void UpdateStatus()
        {
            var r = Router;
            int n = r == null ? 0 : r.ThreadCount;
            lblStatus.Text = !Running ? "Arrêté · aucun flux audio" : n == 0 ? "Démarré · rien à router" : n + " flux audio actif" + (n > 1 ? "s" : "");
        }

        #endregion

        #region Actions

        public void SaveSoon()
        {
            if (Router != null) Router.SaveSoon(); else cfg.Save();
        }

        public void TopologyChanged()
        {
            cfg.Save();
            if (Router != null) Router.Sync();
            foreach (var s in stripList) s.Invalidate();
            matrix.Invalidate();
            map.Invalidate();
            UpdateStatus();
        }

        void SetView(string view)
        {
            lock (AppConfig.Sync) cfg.Router.View = view;
            cfg.Save();
            ApplyView(view);
        }

        void ApplyView(string view)
        {
            bool m = view == "matrix", c = view == "map";
            matrixPanel.Visible = m;
            mapPanel.Visible = c;
            strips.Visible = !m && !c;
            tabMatrix.Primary = m;
            tabMap.Primary = c;
            tabConsole.Primary = !m && !c;
            tabMatrix.Invalidate();
            tabMap.Invalidate();
            tabConsole.Invalidate();
            if (m) matrix.Reload();
            else if (c) map.Reload();
            else foreach (var s in stripList) s.Invalidate();
        }

        public void AddInput(string name, DeviceInfo d, bool loopback, string pairId)
        {
            lock (AppConfig.Sync)
            {
                var i = new RouteInput { Name = name, DeviceId = d.Id, DeviceName = d.Name, Loopback = loopback, PairId = pairId };
                var first = cfg.Router.Outputs.Find(o => !AudioRouter.IsFeedback(i, o));
                if (first != null) i.Buses.Add(first.Id);
                cfg.Router.Inputs.Add(i);
            }
            Rebuild();
            TopologyChanged();
        }

        public void AddOutput(string name, DeviceInfo d)
        {
            lock (AppConfig.Sync) cfg.Router.Outputs.Add(new RouteOutput { Name = name, DeviceId = d.Id, DeviceName = d.Name });
            Rebuild();
            TopologyChanged();
        }

        void ShowVirtual()
        {
            Cursor = Cursors.WaitCursor;
            var outs = Engine.Query(a => a.ListDevices(Flow.Render)) ?? new List<DeviceInfo>();
            var ins = Engine.Query(a => a.ListDevices(Flow.Capture)) ?? new List<DeviceInfo>();
            Cursor = Cursors.Default;
            ReopenVirtual = false;
            using (var dlg = new VirtualDialog(this, outs, ins)) dlg.ShowDialog(FindForm());
            if (ReopenVirtual) BeginInvoke(new Action(ShowVirtual));
        }

        public bool ReopenVirtual;

        /// <summary>Retire toutes les tranches qui utilisent un périphérique (ex. périphérique virtuel supprimé).</summary>
        public void RemoveDevice(string deviceId)
        {
            lock (AppConfig.Sync)
            {
                foreach (var o in cfg.Router.Outputs.FindAll(x => x.DeviceId == deviceId))
                    foreach (var i in cfg.Router.Inputs) i.Buses.Remove(o.Id);
                cfg.Router.Outputs.RemoveAll(x => x.DeviceId == deviceId);
                cfg.Router.Inputs.RemoveAll(x => x.DeviceId == deviceId);
            }
            Rebuild();
            TopologyChanged();
        }

        static string ShortName(AppConfig cfg, DeviceInfo d)
        {
            string a = cfg.Alias("dev:" + d.Id);
            if (a != null) return a;
            int p = d.Name.IndexOf(" (", StringComparison.Ordinal);
            return p > 0 ? d.Name.Substring(0, p) : d.Name;
        }

        void ShowAddMenu(bool input, Control anchor)
        {
            var menu = DarkMenu.Create();
            var outsAll = Engine.Query(a => a.ListDevices(Flow.Render)) ?? new List<DeviceInfo>();
            FillDeviceMenu(menu.Items, input, (d, loop) =>
            {
                if (input)
                {
                    var twin = loop ? null : VirtualDevices.Twin(d, outsAll);
                    AddInput(ShortName(cfg, d), d, loop, twin != null ? twin.Id : null);
                }
                else AddOutput(ShortName(cfg, d), d);
            }, null);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(DarkMenu.Item("Entrées / sorties virtuelles…", Glyphs.Route, ShowVirtual));
            DarkMenu.Show(menu, anchor, new Point(0, anchor.Height + Theme.S(2)));
        }

        /// <summary>Remplit un menu avec les périphériques disponibles.</summary>
        void FillDeviceMenu(ToolStripItemCollection items, bool input, Action<DeviceInfo, bool> pick, string currentId)
        {
            var outs = Engine.Query(a => a.ListDevices(Flow.Render)) ?? new List<DeviceInfo>();
            Comparison<DeviceInfo> byName = (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            outs.Sort(byName);
            if (input)
            {
                var ins = Engine.Query(a => a.ListDevices(Flow.Capture)) ?? new List<DeviceInfo>();
                ins.Sort(byName);
                items.Add(DarkMenu.Header("Micros et entrées"));
                foreach (var d in ins) AddDevice(items, d, false, Glyphs.Mic, pick, currentId);
                items.Add(new ToolStripSeparator());
                items.Add(DarkMenu.Header("Ce qui est joué sur une sortie (boucle)"));
                foreach (var d in outs) AddDevice(items, d, true, Glyphs.Loop, pick, currentId);
            }
            else
            {
                items.Add(DarkMenu.Header("Sorties audio"));
                foreach (var d in outs) AddDevice(items, d, false, Glyphs.Speaker, pick, currentId);
            }
        }

        void AddDevice(ToolStripItemCollection items, DeviceInfo d, bool loop, string glyph, Action<DeviceInfo, bool> pick, string currentId)
        {
            bool hidden;
            lock (AppConfig.Sync)
                hidden = cfg.IsHidden("dev:" + d.Id) || cfg.Router.Virtuals.Exists(v => d.Name.EndsWith("(" + v.Name + ")", StringComparison.Ordinal));
            if (hidden && d.Id != currentId) return;
            string text;
            lock (AppConfig.Sync) text = cfg.Alias("dev:" + d.Id) ?? d.Name;
            if (VirtualDevices.IsVirtual(d.Name)) text += "   · virtuel";
            var it = new ToolStripMenuItem(text, Theme.GlyphImage(glyph, Theme.Muted)) { Checked = d.Id == currentId };
            it.Click += (s, e) => pick(d, loop);
            items.Add(it);
        }

        public void ShowStripMenu(RouteStrip strip, Point p)
        {
            var menu = DarkMenu.Create();
            menu.Items.Add(DarkMenu.Item("Renommer…", "", () => Rename(strip)));
            var dev = DarkMenu.SubItem("Changer de périphérique", strip.IsInput ? Glyphs.Mic : Glyphs.Speaker);
            FillDeviceMenu(dev.DropDownItems, strip.IsInput, (d, loop) =>
            {
                lock (AppConfig.Sync)
                {
                    strip.Node.DeviceId = d.Id;
                    strip.Node.DeviceName = d.Name;
                    var i = strip.Node as RouteInput;
                    if (i != null) { i.Loopback = loop; i.PairId = null; }
                }
                strip.Invalidate();
                TopologyChanged();
            }, strip.Node.DeviceId);
            menu.Items.Add(dev);
            menu.Items.Add(DarkMenu.Item("Remettre le gain à 0 dB", Glyphs.Refresh, () =>
            {
                lock (AppConfig.Sync) strip.Node.Gain = 0;
                strip.Invalidate();
                SaveSoon();
            }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(DarkMenu.Item("Supprimer", "", () => Delete(strip)));
            DarkMenu.Show(menu, strip, p);
        }

        public void ShowDelayMenu(RouteStrip strip, Point p)
        {
            var o = (RouteOutput)strip.Node;
            var menu = DarkMenu.Create();
            menu.Items.Add(DarkMenu.Header("Retarder cette sortie"));
            foreach (int ms in new[] { 0, 5, 10, 15, 20, 30, 40, 50, 75, 100, 150, 200, 300, 400 })
            {
                int v = ms;
                var it = new ToolStripMenuItem(ms == 0 ? "Aucun délai" : ms + " ms") { Checked = o.DelayMs == ms };
                it.Click += (s, e) =>
                {
                    lock (AppConfig.Sync) o.DelayMs = v;
                    strip.Invalidate();
                    cfg.Save();
                };
                menu.Items.Add(it);
            }
            DarkMenu.Show(menu, strip, p);
        }

        public void Rename(RouteStrip strip)
        {
            string name = InputBox.Show(FindForm(), "Renommer", "Nom de la tranche :", strip.Node.Name);
            if (string.IsNullOrEmpty(name)) return;
            lock (AppConfig.Sync) strip.Node.Name = name;
            cfg.Save();
            foreach (var s in stripList) s.Invalidate(); // les boutons de bus affichent le nom des sorties
        }

        void Delete(RouteStrip strip)
        {
            if (MessageBox.Show(FindForm(), "Supprimer « " + strip.Node.Name + " » ?", "Routage audio",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            lock (AppConfig.Sync)
            {
                if (strip.IsInput) cfg.Router.Inputs.Remove((RouteInput)strip.Node);
                else
                {
                    cfg.Router.Outputs.Remove((RouteOutput)strip.Node);
                    foreach (var i in cfg.Router.Inputs) i.Buses.Remove(strip.Node.Id);
                }
            }
            Rebuild();
            TopologyChanged();
        }

        #endregion
    }
}
