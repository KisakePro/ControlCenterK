using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Page du module « Clavier » : claviers détectés, éclairage (par touche si possible) et macros.</summary>
    class KeyboardPage : Panel
    {
        readonly AppConfig cfg;
        readonly Panel scroll;
        readonly List<Card> cards = new List<Card>();
        readonly Dictionary<string, Card> macroRows = new Dictionary<string, Card>();
        Label titleLabel, subLabel, detectInfo;
        FlatButton detectBtn;
        KeyboardView view;
        bool detecting;
        Timer detectTimeout;
        KeyboardDeviceConfig D;

        KeyboardConfig K { get { return cfg.Keyboard; } }

        public KeyboardPage(AppConfig cfg)
        {
            this.cfg = cfg;
            BackColor = Theme.Bg;
            scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            scroll.HandleCreated += (s, e) => Theme.DarkScroll(scroll);
            scroll.Resize += (s, e) => LayoutCards();
            Controls.Add(scroll);
            KeyboardModule.Changed += OnChanged;
            KeyboardModule.KeyPressed += OnKey;
            KeyboardModule.UpdateHook();
            Disposed += (s, e) =>
            {
                KeyboardModule.Changed -= OnChanged;
                KeyboardModule.KeyPressed -= OnKey;
                if (detecting) KeyboardModule.CancelDetect();
                KeyboardModule.UpdateHook();
            };
            VisibleChanged += (s, e) => { if (!Visible && detecting) StopDetect(null); };
            Rebuild();
        }

        void OnChanged() { Ui(Rebuild); }

        void OnKey(string id)
        {
            int u = KeyLayout.UsageOf(id);
            Ui(() =>
            {
                if (view != null && !view.IsDisposed) view.Flash(u);
                Card row;
                if (macroRows.TryGetValue(id, out row)) Highlight(row, false);
            });
        }

        void Ui(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        #region Construction

        public void Rebuild()
        {
            int keep = -scroll.AutoScrollPosition.Y;
            scroll.AutoScrollPosition = Point.Empty;
            scroll.SuspendLayout();
            foreach (Control c in new List<Control>(ToList(scroll.Controls))) { scroll.Controls.Remove(c); c.Dispose(); }
            cards.Clear();
            macroRows.Clear();
            view = null;

            titleLabel = Theme.Label("Clavier", Theme.Semi(18f), Theme.Text, Theme.Bg);
            titleLabel.Location = new Point(Theme.S(26), Theme.S(20));
            subLabel = Theme.Label("Claviers détectés automatiquement : éclairage des touches, animations et macros.", Theme.Ui(9.5f), Theme.Muted, Theme.Bg);
            subLabel.Location = new Point(Theme.S(28), Theme.S(58));
            scroll.Controls.Add(titleLabel);
            scroll.Controls.Add(subLabel);

            var dev = KeyboardModule.Device;
            D = dev != null ? KeyboardModule.DeviceConfig : null;
            cards.Add(DetectedCard(dev));
            var saved = SavedCard();
            if (saved != null) cards.Add(saved);
            if (dev != null && D != null) cards.Add(LightCard(dev));
            cards.Add(MacroCard());
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
            int w = Math.Max(Theme.S(560), Math.Min(Theme.S(1100), scroll.ClientSize.Width - 2 * pad));
            foreach (var c in cards)
            {
                c.SetBounds(pad, y, w, c.Height);
                y = c.Bottom + Theme.S(16);
            }
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

        Color? PickColor(Color current)
        {
            using (var dlg = new ColorDialog { FullOpen = true, Color = current, AnyColor = true })
                return dlg.ShowDialog(FindForm()) == DialogResult.OK ? dlg.Color : (Color?)null;
        }

        #endregion

        #region Cartes

        Card SavedCard()
        {
            var connected = new HashSet<string>();
            foreach (var m in KeyboardModule.Detected) connected.Add(m.Key);
            var items = new List<KeyValuePair<string, string>>();
            lock (AppConfig.Sync)
                foreach (var kv in K.Devices)
                    if (!connected.Contains(kv.Key))
                        items.Add(new KeyValuePair<string, string>(kv.Key, kv.Value == null || string.IsNullOrEmpty(kv.Value.Name) ? "Clavier " + kv.Key : kv.Value.Name));
            if (items.Count == 0) return null;
            var c = NewCard("Claviers enregistrés non connectés", "Réglages conservés pour des claviers débranchés. Supprimez ceux dont vous n'avez plus besoin.");
            int y = SavedDevices.Fill(c, NextY(c), items, Glyphs.Keyboard, "clavier", key =>
            {
                lock (AppConfig.Sync)
                {
                    K.Devices.Remove(key);
                    if (K.Selected == key) K.Selected = null;
                }
                cfg.Save();
                Rebuild();
            });
            SetNextY(c, y);
            return c;
        }

        Card DetectedCard(RgbKeyboard dev)
        {
            var list = KeyboardModule.Detected;
            if (list.Count == 0)
            {
                var none = NewCard(KeyboardModule.Scanning ? "Détection des claviers…" : "Aucun clavier détecté",
                    "Éclairage réglable : SteelSeries Apex (par touche), Corsair K65 / K70 / K95 / Strafe et Razer (tout le clavier). " +
                    "Les macros ci-dessous fonctionnent avec tous les claviers.");
                SetNextY(none, NextY(none));
                return none;
            }
            var c = NewCard("Claviers détectés", "Les claviers à éclairage réglable sont configurés automatiquement à leur branchement. Les macros valent pour tous les claviers.");
            int y = NextY(c);
            foreach (var m in list)
            {
                bool active = dev != null && dev.Key == m.Key;
                var row = new Card { BackColor = active ? Theme.Mix(Theme.Surface, Theme.Accent, 0.18f) : Theme.Surface, Radius = 6,
                    Bounds = new Rectangle(Theme.S(14), y, Theme.S(870), Theme.S(44)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                var icon = Theme.Label(Glyphs.Keyboard, Theme.Icon(12f), active ? Theme.Accent : Theme.Muted, row.BackColor);
                icon.Location = new Point(Theme.S(12), Theme.S(13));
                var name = Theme.Label(m.Name, Theme.Semi(10f), Theme.Text, row.BackColor);
                name.Location = new Point(Theme.S(42), Theme.S(5));
                string state = active ? "Éclairage piloté" + (dev.PerKey ? " touche par touche" : " (tout le clavier)") + (dev.Experimental ? " · prise en charge expérimentale" : "")
                             : m.Supported ? "Éclairage réglable : cliquez sur « Piloter »"
                             : "Éclairage non réglable : macros uniquement";
                var st = Theme.Label(state, Theme.Ui(8.5f), active ? Theme.Green : Theme.Muted, row.BackColor);
                st.Location = new Point(Theme.S(42), Theme.S(24));
                row.Controls.AddRange(new Control[] { icon, name, st });
                if (m.Supported && !active)
                {
                    string key = m.Key;
                    var pick = new FlatButton("Piloter") { Anchor = AnchorStyles.Top | AnchorStyles.Right };
                    pick.FitWidth();
                    pick.Location = new Point(row.Width - pick.Width - Theme.S(8), Theme.S(6));
                    pick.Click += (s, e) => KeyboardModule.Select(key);
                    row.Controls.Add(pick);
                }
                c.Controls.Add(row);
                y += Theme.S(50);
            }
            SetNextY(c, y);
            return c;
        }

        void Save(Action change)
        {
            lock (AppConfig.Sync) change();
            cfg.Save();
            KeyboardModule.ApplyLighting();
            if (view != null) view.Invalidate();
        }

        Card LightCard(RgbKeyboard dev)
        {
            var c = NewCard("Éclairage", (dev.PerKey
                    ? "Cliquez sur des touches (ou glissez) pour les sélectionner, puis choisissez leur couleur. Les autres touches prennent la couleur principale."
                    : "Ce clavier se règle d'une seule couleur pour toutes les touches.") +
                (dev.Experimental ? "\nPrise en charge expérimentale : ce modèle n'a pas encore été testé." : ""));

            var fx = new DropButton { Width = Theme.S(220) };
            fx.Add("device", "Géré par le clavier");
            fx.Add("static", dev.PerKey ? "Couleurs des touches" : "Couleur fixe");
            if (dev.PerKey) fx.Add("gradient", "Dégradé");
            fx.Add("breathe", "Respiration");
            fx.Add("cycle", "Cycle de couleurs");
            fx.Add("rainbow", "Arc-en-ciel");
            if (dev.PerKey)
            {
                fx.Add("wave", "Vague");
                fx.Add("reactive", "Réactif (touches pressées)");
            }
            fx.Add("off", "Éteint");
            fx.Value = D.Effect;
            fx.ValueChanged += (s, e) => Save(() => D.Effect = fx.Value);
            Row(c, "Effet", "« Géré par le clavier » laisse le clavier utiliser son propre éclairage.", fx);

            var main = new FlatButton("Couleur principale") { Glyph = Glyphs.Palette };
            main.FitWidth();
            main.Click += (s, e) =>
            {
                var col = PickColor(Theme.FromHex(D.Color, Color.White));
                if (col.HasValue) Save(() => { D.Color = Theme.ToHex(col.Value); if (D.Effect == "device") D.Effect = "static"; });
                if (D.Effect != fx.Value) fx.Value = D.Effect;
            };
            Row(c, "Couleur principale", null, main);

            // couleurs de l'effet : glisser une couleur de la palette dessus, clic pour modifier, clic droit pour retirer
            var effectCols = new Swatches(D.EffectColors) { Columns = 12, MinCount = 1, Size_ = Theme.S(26) };
            effectCols.FitHeight();
            effectCols.ItemClicked += (i, col) => effectCols.Edit(i);
            effectCols.ListChanged += () =>
            {
                Save(() => { if (D.Effect == "device" || D.Effect == "static" || D.Effect == "off") D.Effect = dev.PerKey ? "gradient" : "cycle"; });
                if (D.Effect != fx.Value) fx.Value = D.Effect;
            };
            Row(c, "Couleurs de l'effet", "Dégradé, respiration, cycle, vague et réactif utilisent ces couleurs. Clic : modifier · clic droit : retirer.", effectCols);

            var speed = new DropButton { Width = Theme.S(220) };
            for (int i = 1; i <= 10; i++) speed.Add(i.ToString(), i == 1 ? "1 (lent)" : i == 10 ? "10 (rapide)" : i.ToString());
            speed.Value = D.Speed.ToString();
            speed.ValueChanged += (s, e) => Save(() => D.Speed = int.Parse(speed.Value));
            Row(c, "Vitesse des animations", null, speed);

            var bright = new DropButton { Width = Theme.S(220) };
            for (int i = 10; i <= 100; i += 10) bright.Add(i.ToString(), i + " %");
            bright.Value = D.Brightness.ToString();
            bright.ValueChanged += (s, e) => Save(() => D.Brightness = int.Parse(bright.Value));
            Row(c, "Luminosité", null, bright);

            if (dev.PerKey)
            {
                int y = NextY(c) + Theme.S(4);
                // palette à droite du clavier : glisser une couleur sur une touche (ou sur la sélection)
                var palette = new Swatches(K.Palette) { Columns = 2, Anchor = AnchorStyles.Top | AnchorStyles.Right };
                palette.FitHeight();
                var palCap = Theme.Label("PALETTE", Theme.Ui(7.5f, FontStyle.Bold), Theme.Dim, Theme.Card);
                palCap.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                palette.Location = new Point(c.Width - Theme.S(20) - palette.Width, y + Theme.S(18));
                palCap.Location = new Point(palette.Left, y);
                palette.ListChanged += () => cfg.Save();
                c.Controls.Add(palCap);
                c.Controls.Add(palette);
                view = new KeyboardView { Location = new Point(Theme.S(20), y), Width = palette.Left - Theme.S(36), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                Action<IEnumerable<int>, Color> paintKeys = (keys, col) =>
                {
                    string hex = Theme.ToHex(col);
                    Save(() =>
                    {
                        foreach (int u in keys) D.Keys[u.ToString("x2")] = hex;
                        if (D.Effect != "static" && D.Effect != "reactive") D.Effect = "static";
                    });
                    if (D.Effect != fx.Value) fx.Value = D.Effect;
                };
                view.KeysPainted += paintKeys;
                palette.ItemClicked += (i, col) => { if (view.Selected.Count > 0) paintKeys(new List<int>(view.Selected), col); };
                view.Lit = new HashSet<int>(dev.Keys);
                view.ColorOf = u =>
                {
                    string hex;
                    lock (AppConfig.Sync) return Theme.FromHex(D.Keys.TryGetValue(u.ToString("x2"), out hex) ? hex : D.Color, Color.White);
                };
                c.Controls.Add(view);
                y = view.Bottom + Theme.S(10);

                var tip = Theme.Label("Glissez une couleur de la palette sur une touche (sur la sélection : toutes les touches sélectionnées ; Ctrl maintenu : peint les touches survolées). " +
                    "Clic sur une couleur : colore la sélection.", Theme.Ui(8.5f), Theme.Muted, Theme.Card);
                tip.MaximumSize = new Size(Theme.S(820), 0);
                tip.Location = new Point(Theme.S(20), y);
                c.Controls.Add(tip);
                y = Math.Max(tip.Bottom + Theme.S(8), palette.Bottom + Theme.S(8));
                var info = Theme.Label("Aucune touche sélectionnée.", Theme.Ui(9f), Theme.Muted, Theme.Card);
                var paint = new FlatButton("Colorer la sélection…", true) { Glyph = Glyphs.Palette, Enabled = false };
                paint.FitWidth();
                var reset = new FlatButton("Couleur principale") { Enabled = false };
                reset.FitWidth();
                var all = new FlatButton("Tout sélectionner");
                all.FitWidth();
                var none = new FlatButton("Désélectionner");
                none.FitWidth();
                paint.Location = new Point(Theme.S(20), y);
                reset.Location = new Point(paint.Right + Theme.S(8), y);
                all.Location = new Point(reset.Right + Theme.S(8), y);
                none.Location = new Point(all.Right + Theme.S(8), y);
                info.Location = new Point(none.Right + Theme.S(14), y + (paint.Height - info.Height) / 2);
                Action sync = () =>
                {
                    int n = view.Selected.Count;
                    info.Text = n == 0 ? "Aucune touche sélectionnée." : n + (n == 1 ? " touche sélectionnée." : " touches sélectionnées.");
                    paint.Enabled = reset.Enabled = n > 0;
                };
                view.SelectionChanged += sync;
                paint.Click += (s, e) =>
                {
                    if (view.Selected.Count == 0) return;
                    int first = 0;
                    foreach (int u in view.Selected) { first = u; break; }
                    var col = PickColor(view.ColorOf(first));
                    if (!col.HasValue) return;
                    paintKeys(new List<int>(view.Selected), col.Value);
                };
                reset.Click += (s, e) => Save(() => { foreach (int u in view.Selected) D.Keys.Remove(u.ToString("x2")); });
                all.Click += (s, e) => { foreach (int u in dev.Keys) view.Selected.Add(u); view.Invalidate(); sync(); };
                none.Click += (s, e) => { view.Selected.Clear(); view.Invalidate(); sync(); };
                c.Controls.AddRange(new Control[] { paint, reset, all, none, info });
                SetNextY(c, paint.Bottom + Theme.S(8));
            }
            return c;
        }

        Card MacroCard()
        {
            var c = NewCard("Macros",
                "Remplace une touche par un raccourci, une macro, une commande multimédia… (tous les claviers). La touche d'origine n'est plus envoyée.\n" +
                "Macro : étapes séparées par des virgules — ex. « Ctrl+C, 50ms, Ctrl+V » ou « \"bonjour\", Entrée » ; « x3 » répète une étape.");
            detectBtn = new FlatButton("Ajouter une touche", true) { Glyph = Glyphs.Keyboard };
            detectBtn.FitWidth();
            detectBtn.Location = new Point(Theme.S(20), NextY(c));
            detectBtn.Click += (s, e) => { if (detecting) StopDetect(null); else StartDetect(); };
            detectInfo = Theme.Label("Cliquez puis appuyez sur la touche ou la combinaison à réaffecter (ex. Ctrl+A).", Theme.Ui(9f), Theme.Muted, Theme.Card);
            detectInfo.Location = new Point(detectBtn.Right + Theme.S(12), detectBtn.Top + (detectBtn.Height - detectInfo.Height) / 2);
            c.Controls.Add(detectBtn);
            c.Controls.Add(detectInfo);
            SetNextY(c, detectBtn.Bottom + Theme.S(12));
            List<string> ids;
            lock (AppConfig.Sync) ids = new List<string>(K.Macros.Keys);
            ids.Sort((a, b) => { int r = KeyLayout.UsageOf(a).CompareTo(KeyLayout.UsageOf(b)); return r != 0 ? r : KeyLayout.ModsOf(a).CompareTo(KeyLayout.ModsOf(b)); });
            foreach (var id in ids) AddMacroRow(c, id);
            return c;
        }

        void AddMacroRow(Card c, string id)
        {
            MouseAction a;
            lock (AppConfig.Sync) if (!K.Macros.TryGetValue(id, out a)) return;
            int y = NextY(c);
            var row = new Card { BackColor = Theme.Surface, Radius = 6, Bounds = new Rectangle(Theme.S(14), y, Theme.S(870), Theme.S(44)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var name = Theme.Label(KeyLayout.NameOfId(id), Theme.Semi(10f), Theme.Text, Theme.Surface);
            name.AutoSize = false;
            name.Bounds = new Rectangle(Theme.S(12), Theme.S(12), Theme.S(150), Theme.S(22));

            var kind = new DropButton { Width = Theme.S(220), Location = new Point(Theme.S(170), Theme.S(6)) };
            kind.Add("", "Comportement normal");
            kind.Add("keys", "Touche / raccourci clavier");
            kind.Add("macro", "Macro");
            kind.Add("click", "Clic de souris");
            kind.Add("media_play", "Média : lecture / pause");
            kind.Add("media_next", "Média : piste suivante");
            kind.Add("media_prev", "Média : piste précédente");
            kind.Add("media_mute", "Muet");
            kind.Add("vol_up", "Volume +");
            kind.Add("vol_down", "Volume −");
            kind.Add("none", "Désactiver la touche");
            kind.Value = a.Kind;

            // raccourci / macro : envoyé une fois à l'appui, ou tant que la touche reste enfoncée
            var mode = new DropButton { Width = Theme.S(130), Location = new Point(kind.Right + Theme.S(10), Theme.S(6)) };
            mode.Add("pulse", "Impulsion");
            mode.Add("hold", "Continu");
            mode.Value = a.Pulse ? "pulse" : "hold";
            mode.ValueChanged += (s, e) => { lock (AppConfig.Sync) { a.Mode = mode.Value; } cfg.Save(); };

            var holder = new Panel { Location = new Point(kind.Right + Theme.S(10), Theme.S(7)), BackColor = Theme.SurfaceHi, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            holder.Size = new Size(row.Width - holder.Left - Theme.S(10), Theme.S(30));
            var val = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.SurfaceHi, ForeColor = Theme.Text, Font = Theme.Ui(9.5f), Text = a.Value,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            val.Bounds = new Rectangle(Theme.S(8), (holder.Height - val.PreferredHeight) / 2, holder.Width - Theme.S(16), val.PreferredHeight);
            holder.Controls.Add(val);
            Action hint = () =>
            {
                string k = kind.Value;
                mode.Visible = k == "keys" || k == "macro";
                mode.Value = a.Pulse ? "pulse" : "hold";
                int left = (mode.Visible ? mode.Right : kind.Right) + Theme.S(10);
                holder.Width += holder.Left - left;
                holder.Left = left;
                holder.Visible = k == "keys" || k == "macro" || k == "click";
                string cue = k == "keys" ? "Cliquez ici puis appuyez sur la combinaison (ex. Ctrl+Maj+S)" : k == "macro" ? "Ctrl+C, 50ms, Ctrl+V" :
                             k == "click" ? "0 gauche · 1 droit · 2 milieu · 3 précédent · 4 suivant" : "";
                if (val.IsHandleCreated) Native.SendMessage(val.Handle, 0x1501, (IntPtr)1, cue);
            };
            val.HandleCreated += (s, e) => hint();
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
                lock (AppConfig.Sync) a.Value = val.Text.Trim();
                cfg.Save();
            };
            kind.ValueChanged += (s, e) =>
            {
                lock (AppConfig.Sync) a.Kind = kind.Value;
                cfg.Save();
                hint();
                KeyboardModule.UpdateHook();
            };
            hint();

            var del = new FlatButton("Retirer") { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            del.FitWidth();
            del.Location = new Point(row.Width - del.Width - Theme.S(8), Theme.S(6));
            del.Click += (s, e) => { lock (AppConfig.Sync) K.Macros.Remove(id); cfg.Save(); KeyboardModule.UpdateHook(); Rebuild(); };
            holder.Width -= del.Width + Theme.S(8);
            row.Controls.AddRange(new Control[] { name, kind, mode, holder, del });
            c.Controls.Add(row);
            macroRows[id] = row;
            SetNextY(c, y + Theme.S(52));
        }

        void Highlight(Card row, bool show)
        {
            if (row.IsDisposed) return;
            if (show) scroll.ScrollControlIntoView(row);
            row.BackColor = Theme.Mix(Theme.Surface, Theme.Accent, 0.35f);
            row.Invalidate(true);
            var t = new Timer { Interval = show ? 1200 : 300 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (!row.IsDisposed) { row.BackColor = Theme.Surface; row.Invalidate(true); } };
            t.Start();
        }

        #endregion

        #region Ajout d'une touche

        void StartDetect()
        {
            detecting = true;
            detectBtn.Text = "Appuyez sur une touche ou une combinaison…  (Annuler)";
            detectBtn.FitWidth();
            detectInfo.Visible = false;
            KeyboardModule.BeginDetect(id => Ui(() => StopDetect(id)));
            detectTimeout = new Timer { Interval = 15000 };
            detectTimeout.Tick += (s, e) => StopDetect(null);
            detectTimeout.Start();
        }

        void StopDetect(string id)
        {
            if (!detecting) return;
            detecting = false;
            KeyboardModule.CancelDetect();
            if (detectTimeout != null) { detectTimeout.Stop(); detectTimeout.Dispose(); detectTimeout = null; }
            detectBtn.Text = "Ajouter une touche";
            detectBtn.FitWidth();
            if (id == null)
            {
                detectInfo.Text = "Aucune touche détectée.";
                detectInfo.ForeColor = Theme.Muted;
                detectInfo.Left = detectBtn.Right + Theme.S(12);
                detectInfo.Visible = true;
                return;
            }
            bool isNew = false;
            lock (AppConfig.Sync)
                if (!K.Macros.ContainsKey(id)) { K.Macros[id] = new MouseAction { Name = KeyLayout.NameOfId(id) }; isNew = true; }
            if (isNew) { cfg.Save(); Rebuild(); }
            string name = KeyLayout.NameOfId(id);
            detectInfo.Text = "✓ " + name + (isNew ? " ajoutée : choisissez son action" : " : déjà dans la liste");
            detectInfo.ForeColor = Theme.Green;
            detectInfo.Left = detectBtn.Right + Theme.S(12);
            detectInfo.Visible = true;
            Card row;
            if (macroRows.TryGetValue(id, out row)) Highlight(row, true);
        }

        #endregion
    }
}
