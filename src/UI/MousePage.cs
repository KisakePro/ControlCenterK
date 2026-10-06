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
        readonly Dictionary<string, Card> buttonRows = new Dictionary<string, Card>();

        MouseConfig M { get { return cfg.Mouse; } }

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
            Disposed += (s, e) => { MouseModule.Changed -= OnChanged; MouseModule.ButtonEvent -= OnButton; };
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
            var sub = subLabel = Theme.Label("Réglages de votre souris Corsair et réaffectation des boutons (touches, raccourcis, macros).", Theme.Ui(9.5f), Theme.Muted, Theme.Bg);
            sub.Location = new Point(Theme.S(28), Theme.S(58));
            scroll.Controls.Add(title);
            scroll.Controls.Add(sub);

            var dev = MouseModule.Device;
            cards.Add(DeviceCard(dev));
            if (dev != null)
            {
                cards.Add(DpiCard());
                cards.Add(LightCard());
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

        Card DeviceCard(CorsairMouse dev)
        {
            if (dev == null)
            {
                var none = NewCard("Aucune souris Corsair compatible détectée",
                    "Souris prise en charge : Corsair Nightsword RGB. Branchez-la : elle est détectée automatiquement. " +
                    "La réaffectation des boutons standard et les réglages Windows ci-dessous fonctionnent avec toutes les souris.");
                SetNextY(none, NextY(none));
                return none;
            }
            var c = NewCard(dev.Info.Name, "Connectée · firmware v" + dev.Firmware + ". Les réglages sont appliqués en direct, sans modifier la mémoire interne de la souris. " +
                "Fermez iCUE s'il est lancé : il imposerait ses propres réglages.");
            var poll = new DropButton { Width = Theme.S(200) };
            poll.Add("0", "Ne pas modifier");
            foreach (var hz in new[] { 125, 250, 500, 1000 }) poll.Add(hz.ToString(), hz + " Hz");
            poll.Value = M.PollHz.ToString();
            poll.ValueChanged += (s, e) => { int hz = int.Parse(poll.Value); if (hz > 0) MouseModule.SetPollRate(hz); else { lock (AppConfig.Sync) M.PollHz = 0; cfg.Save(); } };
            Row(c, "Fréquence d'interrogation", "La souris se reconnecte environ 1 s lors du changement.", poll);

            var adv = new Toggle { Checked = M.Advanced };
            adv.CheckedChanged += (s, e) =>
            {
                lock (AppConfig.Sync) M.Advanced = adv.Checked;
                cfg.Save();
                MouseModule.ApplyDevice();
                Rebuild();
            };
            Row(c, "Mode avancé (éclairage et tous les boutons)",
                "Nécessaire pour l'éclairage et pour réaffecter les boutons DPI, sniper et latéraux. Les clics, la molette et précédent / suivant restent normaux.\n" +
                "En mode avancé, les boutons DPI et sniper sont gérés par l'application (réglables ci-dessous).", adv);
            return c;
        }

        Card DpiCard()
        {
            var c = NewCard("Sensibilité (DPI)", "Étapes parcourues avec les boutons DPI. « Sniper » est la sensibilité temporaire du bouton sniper.");
            int y = NextY(c);
            for (int i = 0; i < CorsairMouse.StageCount; i++)
            {
                int stage = i;
                var st = M.Stages[i];
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
                    v = Math.Max(100, Math.Min(MouseModule.Device != null ? MouseModule.Device.Info.MaxDpi : 18000, (v + 25) / 50 * 50));
                    tb.Text = v.ToString();
                    lock (AppConfig.Sync) st.Dpi = v;
                    cfg.Save();
                    MouseModule.ApplyDevice();
                };
                tb.Leave += (s, e) => commit();
                tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; commit(); } };
                var col = ColorButton(st.Color, hex => { lock (AppConfig.Sync) st.Color = hex; cfg.Save(); MouseModule.ApplyDevice(); });
                col.Location = new Point(unit.Right + Theme.S(18), y + Theme.S(1));
                c.Controls.AddRange(new Control[] { name, en, holder, unit, col });
                if (i > 0)
                {
                    bool current = M.CurrentStage == i;
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

        Card LightCard()
        {
            if (!M.Advanced)
            {
                var off = NewCard("Éclairage", "Activez le « mode avancé » ci-dessus pour régler l'éclairage : en mode normal, la souris gère elle-même sa lumière.");
                SetNextY(off, NextY(off));
                return off;
            }
            var c = NewCard("Éclairage", "« Identifier » fait clignoter la zone sur la souris : renommez les zones selon ce que vous voyez. La zone 3 suit la couleur de l'étape DPI.");
            var fx = new DropButton { Width = Theme.S(200) };
            fx.Add("static", "Couleurs fixes");
            fx.Add("breathe", "Respiration");
            fx.Add("rainbow", "Arc-en-ciel");
            fx.Add("off", "Éteint");
            fx.Value = M.Effect;
            fx.ValueChanged += (s, e) => { lock (AppConfig.Sync) M.Effect = fx.Value; cfg.Save(); MouseModule.ApplyLighting(); };
            Row(c, "Effet", null, fx);
            var speed = new DropButton { Width = Theme.S(200) };
            for (int i = 1; i <= 10; i++) speed.Add(i.ToString(), i == 1 ? "1 (lent)" : i == 10 ? "10 (rapide)" : i.ToString());
            speed.Value = M.EffectSpeed.ToString();
            speed.ValueChanged += (s, e) => { lock (AppConfig.Sync) M.EffectSpeed = int.Parse(speed.Value); cfg.Save(); };
            Row(c, "Vitesse de l'effet", null, speed);

            int y = NextY(c) + Theme.S(4);
            for (int z = 0; z < 6; z++)
            {
                int zone = z;
                var holder = new Panel { Location = new Point(Theme.S(20), y) };
                var tb = DarkText(holder, M.ZoneNames[z], Theme.S(220));
                tb.Leave += (s, e) => { lock (AppConfig.Sync) M.ZoneNames[zone] = tb.Text.Trim().Length > 0 ? tb.Text.Trim() : "Zone " + (zone + 1); cfg.Save(); };
                var col = ColorButton(M.ZoneColors[z], hex => { lock (AppConfig.Sync) M.ZoneColors[zone] = hex; cfg.Save(); MouseModule.ApplyLighting(); });
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

        Card ButtonsCard(CorsairMouse dev)
        {
            var c = NewCard("Boutons",
                "Milieu, précédent et suivant : réaffectés pour toutes les souris. " +
                (dev != null && M.Advanced ? "Autres boutons : appuyez dessus, ils s'ajoutent automatiquement à la liste."
                                           : "Pour les boutons DPI, sniper et latéraux supplémentaires : activez le mode avancé.") +
                "\nMacro : étapes séparées par des virgules — ex. « Ctrl+C, 50ms, Ctrl+V » ou « \"bonjour\", Entrée » ; « x3 » répète une étape.");
            lastButton = Theme.Label(dev != null && M.Advanced ? "Dernier bouton détecté : —" : "", Theme.Ui(8.5f), Theme.Accent, Theme.Card);
            lastButton.Location = new Point(Theme.S(20), NextY(c));
            c.Controls.Add(lastButton);
            SetNextY(c, lastButton.Bottom + Theme.S(10));

            var ids = new List<string> { "hid:2", "hid:3", "hid:4" };
            lock (AppConfig.Sync) foreach (var k in M.Buttons.Keys) if (k.StartsWith("cor:")) ids.Add(k);
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
            }
            return "Bouton " + id.Substring(4);
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

            var valHolder = new Panel { Location = new Point(kind.Right + Theme.S(10), Theme.S(7)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var val = DarkText(valHolder, a.Value, Theme.S(300));
            valHolder.BackColor = val.BackColor = Theme.SurfaceHi;
            valHolder.Width = row.Width - valHolder.Left - Theme.S(10);
            val.Width = valHolder.Width - Theme.S(16);
            val.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Action hint = () =>
            {
                string k = kind.Value;
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
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) return;
                var parts = new List<string>();
                if (e.Control) parts.Add("Ctrl");
                if (e.Shift) parts.Add("Maj");
                if (e.Alt) parts.Add("Alt");
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
            row.Controls.AddRange(new Control[] { nameHolder, kind, valHolder });
            c.Controls.Add(row);
            buttonRows[id] = row;
            SetNextY(c, y + Theme.S(52));
        }

        void Store(string id, MouseAction a)
        {
            if (id.StartsWith("hid:") && string.IsNullOrEmpty(a.Kind) && string.IsNullOrEmpty(a.Value)) M.Buttons.Remove(id);
            else M.Buttons[id] = a;
        }

        void OnButton(string id, bool down)
        {
            if (!down) return;
            Ui(() =>
            {
                if (lastButton != null) lastButton.Text = "Dernier bouton détecté : " + (buttonRows.ContainsKey(id) ? DefaultName(id) : DefaultName(id) + " (ajouté)");
                if (!buttonRows.ContainsKey(id) && id.StartsWith("cor:"))
                {
                    lock (AppConfig.Sync) if (!M.Buttons.ContainsKey(id)) M.Buttons[id] = new MouseAction { Name = DefaultName(id) };
                    cfg.Save();
                    Rebuild();
                    return;
                }
                Card row;
                if (buttonRows.TryGetValue(id, out row))
                {
                    row.BackColor = Theme.Mix(Theme.Surface, Theme.Accent, 0.35f);
                    row.Invalidate(true);
                    var t = new Timer { Interval = 300 };
                    t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (!row.IsDisposed) { row.BackColor = Theme.Surface; row.Invalidate(true); } };
                    t.Start();
                }
            });
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
