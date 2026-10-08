using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Page du module « Souris ».</summary>
    class MousePage : Panel
    {
        readonly AppConfig cfg;
        readonly Panel scroll;
        readonly List<Card> cards = new List<Card>();
        Label lastButton, titleLabel, subLabel;
        FlatButton detectBtn;
        bool detecting;
        Timer detectTimeout;
        readonly Dictionary<string, Card> buttonRows = new Dictionary<string, Card>();

        MouseConfig M { get { return cfg.Mouse; } }
        /// <summary>Réglages de la souris pilotée (null si aucune).</summary>
        MouseDeviceConfig D;

        public MousePage(AppConfig cfg)
        {
            this.cfg = cfg;
            BackColor = Theme.Bg;
            scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            scroll.HandleCreated += (s, e) => Theme.DarkScroll(scroll);
            scroll.Resize += (s, e) => LayoutCards();
            Controls.Add(scroll);
            MouseModule.Changed += OnChanged;
            MouseModule.ButtonEvent += OnButton;
            Disposed += (s, e) => { MouseModule.Changed -= OnChanged; MouseModule.ButtonEvent -= OnButton; if (detecting) MouseModule.CancelDetect(); MouseModule.Watching = false; };
            // page affichée : les boutons pressés sont mis en évidence dans la liste
            VisibleChanged += (s, e) => { if (!Visible && detecting) StopDetect(null); MouseModule.Watching = Visible; };
            Rebuild();
        }

        void OnChanged() { Ui(Rebuild); }

        void Ui(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        #region Construction

        public void Rebuild()
        {
            int keep = -scroll.AutoScrollPosition.Y;
            scroll.AutoScrollPosition = Point.Empty; // positions absolues calculées depuis le haut
            scroll.SuspendLayout();
            foreach (var c in cards) { scroll.Controls.Remove(c); c.Dispose(); }
            cards.Clear();
            buttonRows.Clear();
            foreach (Control c in new List<Control>(scroll.Controls.Count > 0 ? ToList(scroll.Controls) : new List<Control>())) { scroll.Controls.Remove(c); c.Dispose(); }

            var title = titleLabel = Theme.Label("Souris", Theme.Semi(18f), Theme.Text, Theme.Bg);
            title.Location = new Point(Theme.S(26), Theme.S(20));
            var sub = subLabel = Theme.Label("Souris détectées automatiquement : sensibilité, fréquence, éclairage et réaffectation des boutons (touches, raccourcis, macros).", Theme.Ui(9.5f), Theme.Muted, Theme.Bg);
            sub.Location = new Point(Theme.S(28), Theme.S(58));
            scroll.Controls.Add(title);
            scroll.Controls.Add(sub);

            var dev = MouseModule.Device;
            D = MouseModule.DeviceConfig;
            if (dev == null) D = null;
            cards.Add(DetectedCard(dev));
            if (dev != null && D != null)
            {
                cards.Add(DeviceCard(dev));
                cards.Add(DpiCard(dev));
                if (dev.Zones.Length > 0) cards.Add(LightCard(dev));
            }
            cards.Add(ButtonsCard(dev));
            cards.Add(WindowsCard());
            foreach (var c in cards) scroll.Controls.Add(c);
            LayoutCards();
            scroll.ResumeLayout();
            scroll.AutoScrollPosition = new Point(0, keep);
        }

        static List<Control> ToList(Control.ControlCollection cc)
        {
            var l = new List<Control>();
            foreach (Control c in cc) l.Add(c);
            return l;
        }

        void LayoutCards()
        {
            int pad = Theme.S(28), y = Theme.S(92) + scroll.AutoScrollPosition.Y;
            if (titleLabel != null) titleLabel.Top = Theme.S(20) + scroll.AutoScrollPosition.Y;
            if (subLabel != null) subLabel.Top = Theme.S(58) + scroll.AutoScrollPosition.Y;
            int w = Math.Max(Theme.S(560), Math.Min(Theme.S(980), scroll.ClientSize.Width - 2 * pad));
            foreach (var c in cards)
            {
                c.SetBounds(pad, y, w, c.Height);
                y = c.Bottom + Theme.S(16);
            }
        }

        // --- petites fabriques de contrôles ---

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
            c.Tag = y + Theme.S(10); // prochaine position libre
            return c;
        }

        static int NextY(Card c) { return (int)c.Tag; }
        static void SetNextY(Card c, int y) { c.Tag = y; c.Height = y + Theme.S(12); }

        /// <summary>Ligne libellé + contrôle aligné à droite.</summary>
        void Row(Card c, string label, string desc, Control right)
        {
            int y = NextY(c);
            var l = Theme.Label(label, Theme.Semi(10f), Theme.Text, Theme.Card);
            l.Location = new Point(Theme.S(20), y + Theme.S(4));
            c.Controls.Add(l);
            int h = Theme.S(36);
            if (desc != null)
            {
                var d = Theme.Label(desc, Theme.Ui(8.5f), Theme.Muted, Theme.Card);
                d.Location = new Point(Theme.S(20), l.Bottom + Theme.S(2));
                c.Controls.Add(d);
                h = d.Bottom - y + Theme.S(8);
            }
            right.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            right.Location = new Point(c.Width - Theme.S(20) - right.Width, y + (Math.Min(h, Theme.S(48)) - right.Height) / 2);
            c.Controls.Add(right);
            SetNextY(c, y + h + Theme.S(6));
        }

        static TextBox DarkText(Panel holder, string text, int width)
        {
            holder.BackColor = Theme.Surface;
            holder.Size = new Size(width, Theme.S(30));
            var tb = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Ui(9.5f), Text = text };
            tb.Bounds = new Rectangle(Theme.S(8), (holder.Height - tb.PreferredHeight) / 2, width - Theme.S(16), tb.PreferredHeight);
            holder.Controls.Add(tb);
            return tb;
        }

        Button ColorButton(string hex, Action<string> changed)
        {
            var b = new Button { FlatStyle = FlatStyle.Flat, Size = new Size(Theme.S(44), Theme.S(28)), BackColor = Theme.FromHex(hex, Color.Black), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Theme.Border;
            b.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { FullOpen = true, Color = b.BackColor })
                    if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                    {
                        b.BackColor = dlg.Color;
                        changed(Theme.ToHex(dlg.Color));
                    }
            };
            return b;
        }

        #endregion

        #region Cartes

        Card DetectedCard(GamingMouse dev)
        {
            var mice = MouseModule.Detected;
            if (mice.Count == 0)
            {
                var none = NewCard(MouseModule.Scanning ? "Détection des souris…" : "Aucune souris détectée",
                    "Branchez une souris : elle est détectée automatiquement. Souris réglables : Corsair, Logitech G, Razer et SteelSeries. " +
                    "La réaffectation des boutons standard et les réglages Windows ci-dessous fonctionnent avec toutes les souris.");
                SetNextY(none, NextY(none));
                return none;
            }
            var c = NewCard("Souris détectées", "Les souris réglables sont configurées automatiquement à leur branchement. " +
                "Les autres profitent de la réaffectation des boutons standard et des réglages Windows.");
            int y = NextY(c);
            foreach (var m in mice)
            {
                bool active = dev != null && dev.Key == m.Key;
                var row = new Card { BackColor = active ? Theme.Mix(Theme.Surface, Theme.Accent, 0.18f) : Theme.Surface, Radius = 6,
                    Bounds = new Rectangle(Theme.S(14), y, Theme.S(870), Theme.S(44)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                var icon = Theme.Label(Glyphs.Mouse, Theme.Icon(12f), active ? Theme.Accent : Theme.Muted, row.BackColor);
                icon.Location = new Point(Theme.S(12), Theme.S(13));
                var name = Theme.Label(m.Name, Theme.Semi(10f), Theme.Text, row.BackColor);
                name.Location = new Point(Theme.S(42), Theme.S(5));
                string state = active ? "Pilotée par l'application" + (dev.Experimental ? " · prise en charge expérimentale" : "")
                             : m.Supported ? "Réglable : cliquez sur « Piloter » pour la configurer"
                             : "Non réglable : boutons standard et réglages Windows uniquement";
                var st = Theme.Label(state, Theme.Ui(8.5f), active ? Theme.Green : Theme.Muted, row.BackColor);
                st.Location = new Point(Theme.S(42), Theme.S(24));
                row.Controls.AddRange(new Control[] { icon, name, st });
                if (m.Supported && !active)
                {
                    string key = m.Key;
                    var pick = new FlatButton("Piloter") { Anchor = AnchorStyles.Top | AnchorStyles.Right };
                    pick.FitWidth();
                    pick.Location = new Point(row.Width - pick.Width - Theme.S(8), Theme.S(6));
                    pick.Click += (s, e) => MouseModule.Select(key);
                    row.Controls.Add(pick);
                }
                c.Controls.Add(row);
                y += Theme.S(50);
            }
            SetNextY(c, y);
            return c;
        }

        static string VendorApp(string brand)
        {
            switch (brand)
            {
                case "Corsair": return "iCUE";
                case "Logitech": return "G HUB";
                case "Razer": return "Synapse";
                case "SteelSeries": return "SteelSeries GG";
            }
            return null;
        }

        Card DeviceCard(GamingMouse dev)
        {
            string app = VendorApp(dev.Brand);
            var c = NewCard(dev.Name,
                "Connectée" + (dev.Firmware.Length > 0 ? " · firmware v" + dev.Firmware : "") + ". Les réglages sont appliqués en direct, sans modifier la mémoire interne de la souris." +
                (app != null ? " Fermez " + app + " s'il est lancé : il imposerait ses propres réglages." : "") +
                (dev.Experimental ? "\nPrise en charge expérimentale : ce modèle n'a pas encore été testé. Si un réglage ne s'applique pas, la souris reste utilisable normalement." : ""));
            if (dev.PollRates.Length > 0)
            {
                var poll = new DropButton { Width = Theme.S(200) };
                poll.Add("0", "Ne pas modifier");
                foreach (var hz in dev.PollRates) poll.Add(hz.ToString(), hz + " Hz");
                poll.Value = D.PollHz.ToString();
                poll.ValueChanged += (s, e) => { int hz = int.Parse(poll.Value); if (hz > 0) MouseModule.SetPollRate(hz); else { lock (AppConfig.Sync) D.PollHz = 0; cfg.Save(); } };
                Row(c, "Fréquence d'interrogation", dev is CorsairMouse ? "La souris se reconnecte environ 1 s lors du changement." : null, poll);
            }

            if (dev.HasAdvancedMode)
            {
                var adv = new Toggle { Checked = D.Advanced };
                adv.CheckedChanged += (s, e) =>
                {
                    lock (AppConfig.Sync) D.Advanced = adv.Checked;
                    cfg.Save();
                    MouseModule.ApplyDevice();
                    MouseModule.UpdateHook();
                    Rebuild();
                };
                if (dev is CorsairMouse)
                    Row(c, "Mode avancé (éclairage et tous les boutons)",
                        "Nécessaire pour l'éclairage et pour réaffecter les boutons DPI, sniper et latéraux. Les clics, la molette et précédent / suivant restent normaux.\n" +
                        "En mode avancé, les boutons DPI et sniper sont gérés par l'application (réglables ci-dessous).", adv);
                else
                    Row(c, "Mode avancé (tous les boutons)",
                        dev.ExtraButtons.Length + " boutons supplémentaires sont reprogrammés pour être reconnus par l'application.\n" +
                        "En mémoire vive seulement : la souris retrouve ses réglages d'usine au rebranchement.", adv);
            }
            else SetNextY(c, NextY(c));
            return c;
        }

        Card DpiCard(GamingMouse dev)
        {
            var c = NewCard("Sensibilité (DPI)", dev.HardwareStages
                ? "Étapes parcourues avec les boutons DPI. « Sniper » est la sensibilité temporaire du bouton sniper."
                : "La souris reçoit la valeur de l'étape active (de " + dev.MinDpi + " à " + dev.MaxDpi + " DPI). « Sniper » : sensibilité temporaire d'un bouton réglé sur « Sniper (maintenir) ».\n" +
                  "Les boutons DPI de la souris restent gérés par la souris : pour passer d'une étape à l'autre depuis l'application, réglez un bouton sur « DPI : étape suivante ».");
            int y = NextY(c);
            for (int i = 0; i < CorsairMouse.StageCount; i++)
            {
                int stage = i;
                var st = D.Stages[i];
                var name = Theme.Label(i == 0 ? "Sniper" : "Étape " + i, Theme.Semi(10f), Theme.Text, Theme.Card);
                name.Location = new Point(Theme.S(20), y + Theme.S(6));
                var en = new Toggle { Checked = st.Enabled, Location = new Point(Theme.S(110), y + Theme.S(5)) };
                en.CheckedChanged += (s, e) => { lock (AppConfig.Sync) st.Enabled = en.Checked; cfg.Save(); MouseModule.ApplyDevice(); };
                var holder = new Panel { Location = new Point(Theme.S(170), y) };
                var tb = DarkText(holder, st.Dpi.ToString(), Theme.S(90));
                var unit = Theme.Label("DPI", Theme.Ui(9f), Theme.Muted, Theme.Card);
                unit.Location = new Point(holder.Right + Theme.S(6), y + Theme.S(7));
                Action commit = () =>
                {
                    int v;
                    if (!int.TryParse(tb.Text.Trim(), out v)) { tb.Text = st.Dpi.ToString(); return; }
                    int step = Math.Max(1, dev.DpiStep);
                    v = Math.Max(dev.MinDpi, Math.Min(dev.MaxDpi, (v + step / 2) / step * step));
                    tb.Text = v.ToString();
                    lock (AppConfig.Sync) st.Dpi = v;
                    cfg.Save();
                    MouseModule.ApplyDevice();
                };
                tb.Leave += (s, e) => commit();
                tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; commit(); } };
                Control col = unit;
                c.Controls.AddRange(new Control[] { name, en, holder, unit });
                if (dev.HardwareStages)
                {
                    // couleur de l'indicateur DPI de la souris
                    col = ColorButton(st.Color, hex => { lock (AppConfig.Sync) st.Color = hex; cfg.Save(); MouseModule.ApplyDevice(); });
                    col.Location = new Point(unit.Right + Theme.S(18), y + Theme.S(1));
                    c.Controls.Add(col);
                }
                if (i > 0)
                {
                    bool current = D.CurrentStage == i;
                    var act = new FlatButton(current ? "Étape active" : "Activer", current);
                    act.FitWidth();
                    act.Location = new Point(col.Right + Theme.S(18), y);
                    act.Click += (s, e) => { MouseModule.SetStage(stage); cfg.Save(); Rebuild(); };
                    c.Controls.Add(act);
                }
                y += Theme.S(40);
            }
            SetNextY(c, y);
            return c;
        }

        Card LightCard(GamingMouse dev)
        {
            if (dev.HasAdvancedMode && !D.Advanced)
            {
                var off = NewCard("Éclairage", "Activez le « mode avancé » ci-dessus pour régler l'éclairage : en mode normal, la souris gère elle-même sa lumière.");
                SetNextY(off, NextY(off));
                return off;
            }
            var c = NewCard("Éclairage", "« Identifier » fait clignoter la zone sur la souris : renommez les zones selon ce que vous voyez." +
                (dev is CorsairMouse ? " La zone 3 suit la couleur de l'étape DPI." : ""));
            var fx = new DropButton { Width = Theme.S(200) };
            if (!dev.HasAdvancedMode) fx.Add("device", "Géré par la souris");
            fx.Add("static", "Couleurs fixes");
            fx.Add("breathe", "Respiration");
            fx.Add("rainbow", "Arc-en-ciel");
            fx.Add("off", "Éteint");
            fx.Value = D.Effect;
            fx.ValueChanged += (s, e) => { lock (AppConfig.Sync) D.Effect = fx.Value; cfg.Save(); MouseModule.ApplyLighting(); };
            Row(c, "Effet", null, fx);
            var speed = new DropButton { Width = Theme.S(200) };
            for (int i = 1; i <= 10; i++) speed.Add(i.ToString(), i == 1 ? "1 (lent)" : i == 10 ? "10 (rapide)" : i.ToString());
            speed.Value = D.EffectSpeed.ToString();
            speed.ValueChanged += (s, e) => { lock (AppConfig.Sync) D.EffectSpeed = int.Parse(speed.Value); cfg.Save(); };
            Row(c, "Vitesse de l'effet", null, speed);

            int y = NextY(c) + Theme.S(4);
            for (int z = 0; z < dev.Zones.Length && z < D.ZoneNames.Count; z++)
            {
                int zone = z;
                var holder = new Panel { Location = new Point(Theme.S(20), y) };
                var tb = DarkText(holder, D.ZoneNames[z], Theme.S(220));
                tb.Leave += (s, e) => { lock (AppConfig.Sync) D.ZoneNames[zone] = tb.Text.Trim().Length > 0 ? tb.Text.Trim() : dev.Zones[zone]; cfg.Save(); };
                var col = ColorButton(D.ZoneColors[z], hex =>
                {
                    lock (AppConfig.Sync) { D.ZoneColors[zone] = hex; if (D.Effect == "device") D.Effect = "static"; }
                    cfg.Save();
                    MouseModule.ApplyLighting();
                    if (fx.Value == "device") fx.Value = "static";
                });
                col.Location = new Point(holder.Right + Theme.S(12), y + Theme.S(1));
                var id = new FlatButton("Identifier");
                id.FitWidth();
                id.Location = new Point(col.Right + Theme.S(12), y - Theme.S(1));
                id.Click += (s, e) => MouseModule.IdentifyZone(zone);
                c.Controls.AddRange(new Control[] { holder, col, id });
                y += Theme.S(40);
            }
            SetNextY(c, y);
            return c;
        }

        Card ButtonsCard(GamingMouse dev)
        {
            var c = NewCard("Boutons",
                "Milieu, précédent et suivant : réaffectés pour toutes les souris. " +
                (dev != null && dev.HasAdvancedMode ? (D.Advanced ? (dev.ExtraButtons.Length > 0 ? "Tous les boutons de la souris sont listés ci-dessous." : "Autres boutons (DPI, sniper, latéraux) : utilisez « Détecter un bouton ».")
                                                                  : "Pour les boutons DPI, sniper et latéraux supplémentaires : activez le mode avancé.")
                                                    : "Les autres boutons restent gérés par la souris.") +
                "\nMacro : étapes séparées par des virgules — ex. « Ctrl+C, 50ms, Ctrl+V » ou « \"bonjour\", Entrée » ; « x3 » répète une étape.");
            detectBtn = new FlatButton("Détecter un bouton", true) { Glyph = Glyphs.Mouse };
            detectBtn.FitWidth();
            detectBtn.Location = new Point(Theme.S(20), NextY(c));
            detectBtn.Click += (s, e) => { if (detecting) StopDetect(null); else StartDetect(); };
            lastButton = Theme.Label("Cliquez puis appuyez sur le bouton de la souris à configurer.", Theme.Ui(9f), Theme.Muted, Theme.Card);
            lastButton.Location = new Point(detectBtn.Right + Theme.S(12), detectBtn.Top + (detectBtn.Height - lastButton.Height) / 2);
            c.Controls.Add(detectBtn);
            c.Controls.Add(lastButton);
            SetNextY(c, detectBtn.Bottom + Theme.S(12));

            var ids = new List<string> { "hid:2", "hid:3", "hid:4", "hid:tl", "hid:tr" };
            // boutons supplémentaires connus d'avance (SteelSeries en mode avancé), sinon ceux déjà détectés (Corsair)
            if (dev != null && D != null && D.Advanced)
                for (int i = 0; i < dev.ExtraButtons.Length; i++) ids.Add(dev.ButtonPrefix + i);
            lock (AppConfig.Sync) foreach (var k in M.Buttons.Keys) if (k.StartsWith("cor:") && !ids.Contains(k)) ids.Add(k);
            foreach (var id in ids) AddButtonRow(c, id);
            return c;
        }

        static string DefaultName(string id)
        {
            switch (id)
            {
                case "hid:2": return "Bouton du milieu";
                case "hid:3": return "Précédent (latéral)";
                case "hid:4": return "Suivant (latéral)";
                case "hid:tl": return "Molette inclinée à gauche";
                case "hid:tr": return "Molette inclinée à droite";
            }
            var dev = MouseModule.Device;
            int n;
            if (dev != null && id.StartsWith(dev.ButtonPrefix) && int.TryParse(id.Substring(dev.ButtonPrefix.Length), out n) && n < dev.ExtraButtons.Length)
                return dev.ExtraButtons[n];
            return "Bouton " + id.Substring(id.IndexOf(':') + 1);
        }

        void AddButtonRow(Card c, string id)
        {
            MouseAction a;
            lock (AppConfig.Sync)
            {
                if (!M.Buttons.TryGetValue(id, out a)) { a = new MouseAction { Name = DefaultName(id) }; if (id.StartsWith("cor:")) M.Buttons[id] = a; }
            }
            int y = NextY(c);
            var row = new Card { BackColor = Theme.Surface, Radius = 6, Bounds = new Rectangle(Theme.S(14), y, Theme.S(870), Theme.S(44)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var nameHolder = new Panel { Location = new Point(Theme.S(8), Theme.S(7)) };
            var name = DarkText(nameHolder, string.IsNullOrEmpty(a.Name) ? DefaultName(id) : a.Name, Theme.S(200));
            nameHolder.BackColor = name.BackColor = Theme.SurfaceHi;
            name.Leave += (s, e) => { lock (AppConfig.Sync) { a.Name = name.Text.Trim(); Store(id, a); } cfg.Save(); };

            var kind = new DropButton { Width = Theme.S(220), Location = new Point(nameHolder.Right + Theme.S(10), Theme.S(6)) };
            kind.Add("", id.StartsWith("hid:") ? "Comportement normal" : "Aucune action");
            if (id.StartsWith("hid:")) kind.Add("none", "Désactivé");
            kind.Add("keys", "Touche / raccourci clavier");
            kind.Add("macro", "Macro");
            kind.Add("click", "Clic de souris");
            kind.Add("dpi_next", "DPI : étape suivante");
            kind.Add("dpi_prev", "DPI : étape précédente");
            kind.Add("dpi_stage", "DPI : aller à l'étape…");
            kind.Add("sniper", "Sniper (maintenir)");
            kind.Add("media_play", "Média : lecture / pause");
            kind.Add("media_next", "Média : piste suivante");
            kind.Add("media_prev", "Média : piste précédente");
            kind.Add("media_mute", "Muet");
            kind.Add("vol_up", "Volume +");
            kind.Add("vol_down", "Volume −");
            kind.Value = a.Kind;

            // raccourci / macro : envoyé une fois à l'appui, ou tant que la touche reste enfoncée
            var mode = new DropButton { Width = Theme.S(130), Location = new Point(kind.Right + Theme.S(10), Theme.S(6)) };
            mode.Add("pulse", "Impulsion");
            mode.Add("hold", "Continu");
            mode.Value = a.Pulse ? "pulse" : "hold";
            mode.ValueChanged += (s, e) => { lock (AppConfig.Sync) { a.Mode = mode.Value; Store(id, a); } cfg.Save(); };

            var valHolder = new Panel { Location = new Point(kind.Right + Theme.S(10), Theme.S(7)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var val = DarkText(valHolder, a.Value, Theme.S(300));
            valHolder.BackColor = val.BackColor = Theme.SurfaceHi;
            valHolder.Width = row.Width - valHolder.Left - Theme.S(10);
            val.Width = valHolder.Width - Theme.S(16);
            val.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Action hint = () =>
            {
                string k = kind.Value;
                mode.Visible = k == "keys" || k == "macro";
                mode.Value = a.Pulse ? "pulse" : "hold";
                int left = (mode.Visible ? mode.Right : kind.Right) + Theme.S(10);
                valHolder.Width += valHolder.Left - left;
                valHolder.Left = left;
                valHolder.Visible = k == "keys" || k == "macro" || k == "click" || k == "dpi_stage";
                string cue = k == "keys" ? "Cliquez ici puis appuyez sur la combinaison (ex. Ctrl+Maj+S)" : k == "macro" ? "Ctrl+C, 50ms, Ctrl+V" :
                             k == "click" ? "0 gauche · 1 droit · 2 milieu · 3 précédent · 4 suivant" : k == "dpi_stage" ? "Numéro d'étape (1 à 5)" : "";
                if (val.IsHandleCreated) Native.SendMessage(val.Handle, 0x1501, (IntPtr)1, cue);
            };
            val.HandleCreated += (s, e) => hint();
            // capture d'un raccourci : la combinaison pressée remplit le champ
            val.KeyDown += (s, e) =>
            {
                if (kind.Value != "keys") return;
                e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) return;
                var parts = new List<string>();
                if (e.Control) parts.Add("Ctrl");
                if (e.Shift) parts.Add("Maj");
                if (e.Alt) parts.Add("Alt");
                if (Native.WinDown()) parts.Add("Win");
                parts.Add(InputSim.Describe(e.KeyCode));
                val.Text = string.Join("+", parts);
            };
            val.Leave += (s, e) =>
            {
                string err = kind.Value == "macro" ? InputSim.Validate(val.Text) : kind.Value == "keys" && val.Text.Trim().Length > 0 && InputSim.ParseCombo(val.Text) == null ? "Raccourci non compris" : null;
                val.ForeColor = err == null ? Theme.Text : Theme.Red;
                lock (AppConfig.Sync) { a.Value = val.Text.Trim(); Store(id, a); }
                cfg.Save();
            };
            kind.ValueChanged += (s, e) =>
            {
                lock (AppConfig.Sync) { a.Kind = kind.Value; Store(id, a); }
                cfg.Save();
                hint();
                MouseModule.UpdateHook();
            };
            hint();
            if (id.StartsWith("cor:"))
            {
                var del = new FlatButton("Retirer") { Anchor = AnchorStyles.Top | AnchorStyles.Right };
                del.FitWidth();
                del.Location = new Point(row.Width - del.Width - Theme.S(8), Theme.S(6));
                del.Click += (s, e) => { lock (AppConfig.Sync) M.Buttons.Remove(id); cfg.Save(); Rebuild(); };
                row.Controls.Add(del);
                valHolder.Width -= del.Width + Theme.S(8);
                val.Width = valHolder.Width - Theme.S(16);
            }
            row.Controls.AddRange(new Control[] { nameHolder, kind, mode, valHolder });
            c.Controls.Add(row);
            buttonRows[id] = row;
            SetNextY(c, y + Theme.S(52));
        }

        void Store(string id, MouseAction a)
        {
            if (!id.StartsWith("cor:") && string.IsNullOrEmpty(a.Kind) && string.IsNullOrEmpty(a.Value)) M.Buttons.Remove(id);
            else M.Buttons[id] = a;
        }

        void OnButton(string id, bool down)
        {
            if (!down) return;
            Ui(() => Highlight(id, false));
        }

        /// <summary>Met une ligne en évidence (et la fait défiler à l'écran si demandé).</summary>
        void Highlight(string id, bool show)
        {
            Card row;
            if (!buttonRows.TryGetValue(id, out row) || row.IsDisposed) return;
            if (show) scroll.ScrollControlIntoView(row);
            row.BackColor = Theme.Mix(Theme.Surface, Theme.Accent, 0.35f);
            row.Invalidate(true);
            var t = new Timer { Interval = show ? 1200 : 300 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (!row.IsDisposed) { row.BackColor = Theme.Surface; row.Invalidate(true); } };
            t.Start();
        }

        void StartDetect()
        {
            detecting = true;
            detectBtn.Text = "Appuyez sur un bouton de la souris…  (Annuler)";
            detectBtn.FitWidth();
            lastButton.Visible = false;
            KeyDownCancel(true);
            MouseModule.BeginDetect(id => Ui(() => StopDetect(id)));
            detectTimeout = new Timer { Interval = 15000 };
            detectTimeout.Tick += (s, e) => StopDetect(null);
            detectTimeout.Start();
        }

        void StopDetect(string id)
        {
            if (!detecting) return;
            detecting = false;
            MouseModule.CancelDetect();
            KeyDownCancel(false);
            if (detectTimeout != null) { detectTimeout.Stop(); detectTimeout.Dispose(); detectTimeout = null; }
            if (id == null)
            {
                detectBtn.Text = "Détecter un bouton";
                detectBtn.FitWidth();
                var dv = MouseModule.Device;
                lastButton.Text = dv != null && dv.HasAdvancedMode && D != null && !D.Advanced
                    ? "Aucun bouton détecté. Les boutons DPI, sniper et latéraux nécessitent le mode avancé."
                    : "Aucun bouton détecté.";
                lastButton.Left = detectBtn.Right + Theme.S(12);
                lastButton.Visible = true;
                return;
            }
            bool isNew = false;
            lock (AppConfig.Sync)
                if (id.StartsWith("cor:") && !M.Buttons.ContainsKey(id)) { M.Buttons[id] = new MouseAction { Name = DefaultName(id) }; isNew = true; }
            if (isNew) { cfg.Save(); Rebuild(); }
            else
            {
                detectBtn.Text = "Détecter un bouton";
                detectBtn.FitWidth();
            }
            string name;
            MouseAction a;
            lock (AppConfig.Sync) name = M.Buttons.TryGetValue(id, out a) && !string.IsNullOrEmpty(a.Name) ? a.Name : DefaultName(id);
            lastButton.Text = "✓ " + name + (isNew ? " ajouté à la liste" : " : déjà dans la liste");
            lastButton.ForeColor = Theme.Green;
            lastButton.Left = detectBtn.Right + Theme.S(12);
            lastButton.Visible = true;
            Highlight(id, true);
        }

        // Échap annule la détection
        void KeyDownCancel(bool on)
        {
            var f = FindForm();
            if (f == null) return;
            f.KeyDown -= OnFormKey;
            if (on) { f.KeyPreview = true; f.KeyDown += OnFormKey; }
        }

        void OnFormKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && detecting) { e.Handled = true; StopDetect(null); }
        }

        Card WindowsCard()
        {
            var c = NewCard("Windows (toutes les souris)", "Réglages du système, appliqués immédiatement à toutes les souris.");
            var speed = new DropButton { Width = Theme.S(200) };
            for (int i = 1; i <= 20; i++) speed.Add(i.ToString(), i == 10 ? "10 (par défaut)" : i.ToString());
            speed.Value = WinMouse.Speed.ToString();
            speed.ValueChanged += (s, e) => WinMouse.Speed = int.Parse(speed.Value);
            Row(c, "Vitesse du pointeur", null, speed);

            var prec = new Toggle { Checked = WinMouse.Precision };
            prec.CheckedChanged += (s, e) => WinMouse.Precision = prec.Checked;
            Row(c, "Améliorer la précision du pointeur", "Accélération de Windows : souvent désactivée par les joueurs.", prec);

            var dbl = new DropButton { Width = Theme.S(200) };
            for (int ms = 200; ms <= 900; ms += 100) dbl.Add(ms.ToString(), ms + " ms" + (ms == 500 ? " (par défaut)" : ""));
            dbl.Value = (Math.Max(200, Math.Min(900, (WinMouse.DoubleClick + 50) / 100 * 100))).ToString();
            dbl.ValueChanged += (s, e) => WinMouse.DoubleClick = int.Parse(dbl.Value);
            Row(c, "Délai du double-clic", null, dbl);

            var wheel = new DropButton { Width = Theme.S(200) };
            for (int i = 1; i <= 10; i++) wheel.Add(i.ToString(), i + (i == 1 ? " ligne" : " lignes") + (i == 3 ? " (par défaut)" : ""));
            wheel.Add("-1", "Un écran à la fois");
            wheel.Value = WinMouse.WheelLines.ToString();
            wheel.ValueChanged += (s, e) => WinMouse.WheelLines = int.Parse(wheel.Value);
            Row(c, "Défilement de la molette", null, wheel);

            var swap = new Toggle { Checked = WinMouse.Swapped };
            swap.CheckedChanged += (s, e) => WinMouse.Swapped = swap.Checked;
            Row(c, "Inverser les boutons gauche et droit", "Pour les gauchers.", swap);
            return c;
        }

        #endregion
    }
}
