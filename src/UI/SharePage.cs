using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Page du module « Partage d'écran » (KStS).</summary>
    class SharePage : Panel
    {
        readonly AppConfig cfg;
        readonly Panel scroll;
        readonly List<Card> cards = new List<Card>();
        Label titleLabel, subLabel;
        bool busy;

        ShareConfig S { get { return cfg.Share; } }

        public SharePage(AppConfig cfg)
        {
            this.cfg = cfg;
            BackColor = Theme.Bg;
            scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            scroll.HandleCreated += (s, e) => Theme.DarkScroll(scroll);
            scroll.Resize += (s, e) => LayoutCards();
            Controls.Add(scroll);
            ScreenShare.Changed += OnChanged;
            Disposed += (s, e) => ScreenShare.Changed -= OnChanged;
            Rebuild();
        }

        void OnChanged()
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(Rebuild)); } catch { }
        }

        #region Construction

        void Rebuild()
        {
            int keep = -scroll.AutoScrollPosition.Y;
            scroll.AutoScrollPosition = Point.Empty;
            scroll.SuspendLayout();
            var old = new List<Control>();
            foreach (Control c in scroll.Controls) old.Add(c);
            foreach (var c in old) { scroll.Controls.Remove(c); c.Dispose(); }
            cards.Clear();

            titleLabel = Theme.Label("Partage d'écran", Theme.Semi(18f), Theme.Text, Theme.Bg);
            titleLabel.Location = new Point(Theme.S(26), Theme.S(20));
            subLabel = Theme.Label("Partagez une zone de l'écran sur Discord comme une webcam (anciennement KStS).", Theme.Ui(9.5f), Theme.Muted, Theme.Bg);
            subLabel.Location = new Point(Theme.S(28), Theme.S(58));
            scroll.Controls.Add(titleLabel);
            scroll.Controls.Add(subLabel);

            cards.Add(HelpCard());
            cards.Add(CameraCard());
            cards.Add(ZonesCard());
            foreach (var c in cards) scroll.Controls.Add(c);
            LayoutCards();
            scroll.ResumeLayout();
            scroll.AutoScrollPosition = new Point(0, keep);
        }

        void LayoutCards()
        {
            int pad = Theme.S(28), y = Theme.S(92) + scroll.AutoScrollPosition.Y;
            if (titleLabel != null) titleLabel.Top = Theme.S(20) + scroll.AutoScrollPosition.Y;
            if (subLabel != null) subLabel.Top = Theme.S(58) + scroll.AutoScrollPosition.Y;
            int w = Math.Max(Theme.S(560), Math.Min(Theme.S(980), scroll.ClientSize.Width - 2 * pad));
            foreach (var c in cards) { c.SetBounds(pad, y, w, c.Height); y = c.Bottom + Theme.S(16); }
        }

        Card NewCard(string title, string desc)
        {
            var c = new Card { Width = Theme.S(900) };
            var t = Theme.Label(title, Theme.Semi(12f), Theme.Text, Theme.Card);
            t.Location = new Point(Theme.S(20), Theme.S(16));
            c.Controls.Add(t);
            int y = t.Bottom + Theme.S(4);
            if (desc != null)
            {
                var d = Theme.Label(desc, Theme.Ui(8.5f), Theme.Muted, Theme.Card);
                d.MaximumSize = new Size(Theme.S(860), 0);
                d.Location = new Point(Theme.S(20), y);
                c.Controls.Add(d);
                y = d.Bottom + Theme.S(4);
            }
            c.Tag = y + Theme.S(10);
            return c;
        }

        static int NextY(Card c) { return (int)c.Tag; }
        static void SetNextY(Card c, int y) { c.Tag = y; c.Height = y + Theme.S(12); }

        #endregion

        #region Cartes

        Card HelpCard()
        {
            var c = NewCard("Comment ça marche", null);
            string[] steps =
            {
                "1.  Installez une fois la caméra virtuelle « " + ScreenShare.CamName + " » (carte ci-dessous, droits administrateur).",
                "2.  Enregistrez vos zones (glissez à la souris ou saisissez les coordonnées), puis partagez-en une d'un clic.",
                "     Pour jongler sans ouvrir ControlCenterK : Ctrl+Alt+F1 à F9 partage la zone 1 à 9, Ctrl+Alt+F10 arrête (désactivables). La caméra ne se coupe pas.",
                "3.  Dans Discord : Paramètres → Voix et vidéo → Caméra → « " + ScreenShare.CamName + " », puis activez votre caméra en appel.",
                "La zone est envoyée en direct (30 images/s, 1920 × 1080, curseur compris), avec des bandes noires si ses proportions diffèrent.",
                "Utile pour ne montrer qu'une partie de l'écran, ou pour partager en plus d'un partage d'écran classique.",
            };
            int y = NextY(c);
            foreach (var s in steps)
            {
                var l = Theme.Label(s, Theme.Ui(9f), s.StartsWith("La zone") || s.StartsWith("Utile") ? Theme.Muted : Theme.Text, Theme.Card);
                l.MaximumSize = new Size(Theme.S(860), 0);
                l.Location = new Point(Theme.S(20), y);
                c.Controls.Add(l);
                y = l.Bottom + Theme.S(6);
            }
            SetNextY(c, y + Theme.S(4));
            return c;
        }

        Card CameraCard()
        {
            bool ready = ScreenShare.CameraReady();
            var c = NewCard("Caméra virtuelle",
                "Pilote : module caméra virtuelle d'OBS Studio (licence GPL), enregistré sous le nom « " + ScreenShare.CamName + " ». " +
                "Si OBS est installé, la désinstallation lui rend sa caméra d'origine.");
            var st = Theme.Label(ready ? "✓ Caméra installée" : "Caméra non installée", Theme.Semi(10f), ready ? Theme.Green : Theme.Red, Theme.Card);
            st.Location = new Point(Theme.S(20), NextY(c) + Theme.S(6));
            var btn = new FlatButton(ready ? "Désinstaller la caméra" : "Installer la caméra", !ready) { Glyph = "", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btn.FitWidth();
            btn.Location = new Point(c.Width - btn.Width - Theme.S(20), NextY(c));
            btn.Click += (s, e) =>
            {
                if (busy) return;
                busy = true;
                btn.Enabled = false;
                btn.Text = (ready ? "Désinstallation" : "Installation") + "… (acceptez la demande administrateur)";
                btn.FitWidth();
                btn.Left = c.Width - btn.Width - Theme.S(20);
                new System.Threading.Thread(() =>
                {
                    bool ok = ready ? ScreenShare.UninstallCamera() : ScreenShare.InstallCamera();
                    try { BeginInvoke(new Action(() => { busy = false; Rebuild(); if (!ok) MessageBox.Show(FindForm(), ready ? "Désinstallation annulée ou échouée." : "Installation annulée ou échouée.", "Caméra virtuelle", MessageBoxButtons.OK, MessageBoxIcon.Warning); })); } catch { }
                }) { IsBackground = true }.Start();
            };
            c.Controls.AddRange(new Control[] { st, btn });
            SetNextY(c, btn.Bottom + Theme.S(8));
            return c;
        }

        Card ZonesCard()
        {
            var c = NewCard("Zones", "Chaque zone a sa couleur : son cadre l'entoure quand elle est partagée, et « Afficher toutes les zones » " +
                "montre toutes les zones enregistrées à la fois (pratique avec plusieurs écrans). Coordonnées en pixels, écran principal en haut à gauche = 0, 0.");
            int y = NextY(c);
            var add = new FlatButton("Nouvelle zone", true) { Glyph = Glyphs.Add };
            add.FitWidth();
            add.Location = new Point(Theme.S(20), y);
            add.Click += (s, e) => Pick(null);
            var stop = new FlatButton("Arrêter le partage") { Glyph = "\uE71A", Enabled = ScreenShare.Streaming };
            stop.FitWidth();
            stop.Location = new Point(add.Right + Theme.S(8), y);
            stop.Click += (s, e) => ScreenShare.Stop();
            c.Controls.AddRange(new Control[] { add, stop });
            y = add.Bottom + Theme.S(12);

            y = Option(c, y, "Cadre autour de la zone partagée", "Dessiné à l'extérieur de la zone : il n'apparaît pas dans la vidéo.",
                () => S.Border, v => S.Border = v);
            y = Option(c, y, "Raccourcis clavier", "Ctrl+Alt+F1 à F9 : partager la zone 1 à 9 ; Ctrl+Alt+F10 : arrêter. Fonctionnent partout, même en jeu.",
                () => S.HotKeys, v => { S.HotKeys = v; }, ScreenShare.UpdateHotKeys);
            y = Option(c, y, "Afficher toutes les zones", "Cadres fins de toutes les zones enregistrées, chacune dans sa couleur.",
                () => S.ShowAll, v => S.ShowAll = v);

            var status = Theme.Label("", Theme.Ui(9.5f), Theme.Muted, Theme.Card);
            status.MaximumSize = new Size(Theme.S(860), 0);
            if (ScreenShare.Error != null) { status.Text = ScreenShare.Error; status.ForeColor = Theme.Red; }
            else if (ScreenShare.Streaming)
            {
                var r = ScreenShare.Region;
                status.Text = "● En direct : " + r.Width + " × " + r.Height + " à (" + r.X + ", " + r.Y + ") → caméra « " + ScreenShare.CamName + " ».";
                status.ForeColor = Theme.Green;
            }
            else status.Text = "Arrêté. Partagez une zone pour commencer.";
            status.Location = new Point(Theme.S(20), y);
            c.Controls.Add(status);
            y = status.Bottom + Theme.S(12);

            List<ShareZone> zones;
            lock (AppConfig.Sync) zones = new List<ShareZone>(S.Zones);
            if (zones.Count == 0)
            {
                var none = Theme.Label("Aucune zone : cliquez sur « Nouvelle zone » et glissez sur l'écran.", Theme.Ui(9f), Theme.Dim, Theme.Card);
                none.Location = new Point(Theme.S(20), y);
                c.Controls.Add(none);
                y = none.Bottom + Theme.S(8);
            }
            for (int i = 0; i < zones.Count; i++) y = ZoneRow(c, y, zones[i], i);
            SetNextY(c, y);
            return c;
        }

        int Option(Card c, int y, string label, string desc, Func<bool> get, Action<bool> set, Action after = null)
        {
            var l = Theme.Label(label, Theme.Semi(10f), Theme.Text, Theme.Card);
            l.Location = new Point(Theme.S(20), y + Theme.S(2));
            var d = Theme.Label(desc, Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            d.Location = new Point(Theme.S(20), l.Bottom + Theme.S(2));
            var tg = new Toggle { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            lock (AppConfig.Sync) tg.Checked = get();
            tg.Location = new Point(c.Width - tg.Width - Theme.S(20), y + Theme.S(4));
            tg.CheckedChanged += (s, e) =>
            {
                lock (AppConfig.Sync) set(tg.Checked);
                cfg.Save();
                ScreenShare.RefreshFrames();
                if (after != null) after();
                Rebuild(); // affiche ou masque les raccourcis sur chaque zone
            };
            c.Controls.AddRange(new Control[] { l, d, tg });
            return d.Bottom + Theme.S(12);
        }

        int ZoneRow(Card c, int y, ShareZone z, int index)
        {
            bool live = ScreenShare.Streaming && ScreenShare.ActiveZone == z.Id;
            var color = Theme.FromHex(z.Color, Color.Red);
            var row = new Card { BackColor = live ? Theme.Mix(Theme.Surface, color, 0.16f) : Theme.Surface, Radius = 6,
                Bounds = new Rectangle(Theme.S(14), y, c.Width - Theme.S(28), Theme.S(86)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            string id = z.Id;

            // ligne 1 : couleur, nom, raccourci, état, actions
            var swatch = new Button { FlatStyle = FlatStyle.Flat, BackColor = color, Size = new Size(Theme.S(26), Theme.S(26)), Location = new Point(Theme.S(12), Theme.S(9)), Cursor = Cursors.Hand };
            swatch.FlatAppearance.BorderColor = Theme.Border;
            swatch.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { FullOpen = true, Color = swatch.BackColor })
                {
                    var custom = new List<int>();
                    foreach (var h in ShareConfig.Palette) { var k = Theme.FromHex(h, Color.Red); custom.Add(k.R | k.G << 8 | k.B << 16); }
                    dlg.CustomColors = custom.ToArray();
                    if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                    lock (AppConfig.Sync) { var zz = S.Find(id); if (zz != null) zz.Color = Theme.ToHex(dlg.Color); }
                    cfg.Save();
                    ScreenShare.RefreshFrames();
                    Rebuild();
                }
            };
            var nameBox = Box(row, z.Name, new Rectangle(swatch.Right + Theme.S(10), Theme.S(7), Theme.S(200), Theme.S(30)), HorizontalAlignment.Left);
            nameBox.Leave += (s, e) =>
            {
                string n = nameBox.Text.Trim();
                if (n.Length == 0) return;
                lock (AppConfig.Sync) { var zz = S.Find(id); if (zz != null) zz.Name = n; }
                cfg.Save();
            };
            bool hk;
            lock (AppConfig.Sync) hk = S.HotKeys;
            var keys = Theme.Label(hk && index < 9 ? "Ctrl+Alt+F" + (index + 1) : "", Theme.Ui(8.5f), Theme.Muted, row.BackColor);
            keys.Location = new Point(nameBox.Parent.Right + Theme.S(12), Theme.S(14));
            var state = Theme.Label(live ? "● En direct" : "", Theme.Semi(9f), Theme.Green, row.BackColor);
            state.Location = new Point(keys.Right + Theme.S(12), Theme.S(13));
            row.Controls.AddRange(new Control[] { swatch, keys, state });

            int right = row.Width - Theme.S(8);
            foreach (var spec in new[] { "del", "redo", "share" })
            {
                FlatButton b;
                if (spec == "del")
                {
                    b = new FlatButton("Supprimer") { Glyph = Glyphs.Close };
                    b.Click += (s, e) =>
                    {
                        if (MessageBox.Show(FindForm(), "Supprimer la zone « " + z.Name + " » ?", "Supprimer la zone", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                        if (ScreenShare.ActiveZone == id) ScreenShare.Stop();
                        lock (AppConfig.Sync) S.Zones.RemoveAll(zz => zz.Id == id);
                        cfg.Save();
                        ScreenShare.RefreshFrames();
                        Rebuild();
                    };
                }
                else if (spec == "redo")
                {
                    b = new FlatButton("Redéfinir") { Glyph = "\uE7A8" };
                    b.Click += (s, e) => Pick(id);
                }
                else
                {
                    b = new FlatButton(live ? "En direct" : "Partager", !live) { Glyph = "\uE7F4", Enabled = !live };
                    b.Click += (s, e) => Share(id);
                }
                b.FitWidth();
                b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                b.Location = new Point(right - b.Width, Theme.S(6));
                right = b.Left - Theme.S(8);
                row.Controls.Add(b);
            }

            // ligne 2 : coordonnées modifiables
            int x = Theme.S(48);
            var boxes = new Dictionary<string, TextBox>();
            int[] vals = { z.X, z.Y, z.W, z.H };
            string[] names = { "X", "Y", "L", "H" };
            for (int i = 0; i < 4; i++)
            {
                var l = Theme.Label(names[i], Theme.Semi(9.5f), Theme.Muted, row.BackColor);
                l.Location = new Point(x, Theme.S(52));
                row.Controls.Add(l);
                boxes[names[i]] = Box(row, vals[i].ToString(), new Rectangle(x + Theme.S(16), Theme.S(46), Theme.S(72), Theme.S(30)), HorizontalAlignment.Right);
                x += Theme.S(104);
            }
            var apply = new FlatButton("Appliquer") { Glyph = Glyphs.Check };
            apply.FitWidth();
            apply.Location = new Point(x + Theme.S(4), Theme.S(46));
            Action save = () =>
            {
                int nx, ny, nw, nh;
                if (!int.TryParse(boxes["X"].Text, out nx) || !int.TryParse(boxes["Y"].Text, out ny) ||
                    !int.TryParse(boxes["L"].Text, out nw) || !int.TryParse(boxes["H"].Text, out nh) || nw < 16 || nh < 16)
                {
                    MessageBox.Show(FindForm(), "Coordonnées invalides : nombres entiers, zone de 16 × 16 au minimum.", "Zone", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Update(id, new Rectangle(nx, ny, nw / 2 * 2, nh / 2 * 2));
            };
            apply.Click += (s, e) => save();
            foreach (var tb in boxes.Values) tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; save(); } };
            row.Controls.Add(apply);

            c.Controls.Add(row);
            return y + Theme.S(94);
        }

        static TextBox Box(Control parent, string text, Rectangle bounds, HorizontalAlignment align)
        {
            var holder = new Panel { BackColor = Theme.SurfaceHi, Bounds = bounds };
            var tb = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.SurfaceHi, ForeColor = Theme.Text, Font = Theme.Ui(9.5f), Text = text, TextAlign = align };
            tb.Bounds = new Rectangle(Theme.S(8), (holder.Height - tb.PreferredHeight) / 2, holder.Width - Theme.S(16), tb.PreferredHeight);
            holder.Controls.Add(tb);
            parent.Controls.Add(holder);
            return tb;
        }

        #endregion

        #region Actions

        /// <summary>Sélection à la souris : nouvelle zone (id null) ou nouvelle position d'une zone existante.</summary>
        void Pick(string id)
        {
            var f = FindForm();
            if (f != null) f.WindowState = FormWindowState.Minimized;
            var t = new Timer { Interval = 200 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                t.Dispose();
                ZoneSelector.Pick(r =>
                {
                    if (f != null) { f.WindowState = FormWindowState.Normal; f.Activate(); }
                    if (r == null) return;
                    if (id != null) { Update(id, r.Value); return; }
                    var z = new ShareZone { Id = Guid.NewGuid().ToString("N").Substring(0, 8), X = r.Value.X, Y = r.Value.Y, W = r.Value.Width, H = r.Value.Height };
                    lock (AppConfig.Sync)
                    {
                        z.Name = "Zone " + (S.Zones.Count + 1);
                        z.Color = S.NextColor();
                        S.Zones.Add(z);
                    }
                    cfg.Save();
                    ScreenShare.RefreshFrames();
                    Rebuild();
                });
            };
            t.Start();
        }

        void Update(string id, Rectangle r)
        {
            lock (AppConfig.Sync) { var z = S.Find(id); if (z == null) return; z.X = r.X; z.Y = r.Y; z.W = r.Width; z.H = r.Height; }
            cfg.Save();
            if (ScreenShare.Streaming && ScreenShare.ActiveZone == id) ScreenShare.ShareSaved(id); // zone en direct : suit la nouvelle position
            else ScreenShare.RefreshFrames();
            Rebuild();
        }

        void Share(string id)
        {
            if (!ScreenShare.CameraReady()) { MessageBox.Show(FindForm(), "Installez d'abord la caméra virtuelle.", "Partage d'écran", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            ScreenShare.ShareSaved(id);
        }

        #endregion
    }
}
