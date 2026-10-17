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
        Label titleLabel, subLabel, status;
        readonly Dictionary<string, TextBox> coords = new Dictionary<string, TextBox>();
        FlatButton pickBtn, stopBtn;
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
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(UpdateState)); } catch { }
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
            coords.Clear();

            titleLabel = Theme.Label("Partage d'écran", Theme.Semi(18f), Theme.Text, Theme.Bg);
            titleLabel.Location = new Point(Theme.S(26), Theme.S(20));
            subLabel = Theme.Label("Partagez une zone de l'écran sur Discord comme une webcam (anciennement KStS).", Theme.Ui(9.5f), Theme.Muted, Theme.Bg);
            subLabel.Location = new Point(Theme.S(28), Theme.S(58));
            scroll.Controls.Add(titleLabel);
            scroll.Controls.Add(subLabel);

            cards.Add(HelpCard());
            cards.Add(CameraCard());
            cards.Add(ZoneCard());
            foreach (var c in cards) scroll.Controls.Add(c);
            LayoutCards();
            scroll.ResumeLayout();
            scroll.AutoScrollPosition = new Point(0, keep);
            UpdateState();
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
                "2.  Choisissez la zone de l'écran à partager : glissez à la souris, ou saisissez les coordonnées.",
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
                    try { BeginInvoke(new Action(() => { busy = false; Rebuild(); if (!ok) SetStatus(ready ? "Désinstallation annulée ou échouée." : "Installation annulée ou échouée.", true); })); } catch { }
                }) { IsBackground = true }.Start();
            };
            c.Controls.AddRange(new Control[] { st, btn });
            SetNextY(c, btn.Bottom + Theme.S(8));
            return c;
        }

        Card ZoneCard()
        {
            var c = NewCard("Zone partagée", "Coordonnées en pixels de l'écran (écran principal en haut à gauche = 0, 0).");
            int y = NextY(c);
            pickBtn = new FlatButton("Choisir une zone", true) { Glyph = "" };
            pickBtn.FitWidth();
            pickBtn.Location = new Point(Theme.S(20), y);
            pickBtn.Click += (s, e) => Pick();
            stopBtn = new FlatButton("Arrêter le partage") { Glyph = "" };
            stopBtn.FitWidth();
            stopBtn.Location = new Point(pickBtn.Right + Theme.S(8), y);
            stopBtn.Click += (s, e) => ScreenShare.Stop();
            c.Controls.AddRange(new Control[] { pickBtn, stopBtn });
            y = pickBtn.Bottom + Theme.S(14);

            int x = Theme.S(20);
            int[] vals;
            lock (AppConfig.Sync) vals = new[] { S.X, S.Y, S.W, S.H };
            string[] keys = { "X", "Y", "L", "H" };
            for (int i = 0; i < 4; i++)
            {
                var l = Theme.Label(keys[i], Theme.Semi(10f), Theme.Text, Theme.Card);
                l.Location = new Point(x, y + Theme.S(6));
                var holder = new Panel { BackColor = Theme.Surface, Bounds = new Rectangle(x + Theme.S(18), y, Theme.S(80), Theme.S(30)) };
                var tb = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Ui(9.5f),
                    TextAlign = HorizontalAlignment.Right, Text = vals[i] == 0 && i >= 2 ? "" : vals[i].ToString() };
                tb.Bounds = new Rectangle(Theme.S(8), (holder.Height - tb.PreferredHeight) / 2, holder.Width - Theme.S(16), tb.PreferredHeight);
                tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyCoords(); } };
                holder.Controls.Add(tb);
                coords[keys[i]] = tb;
                c.Controls.AddRange(new Control[] { l, holder });
                x = holder.Right + Theme.S(18);
            }
            var apply = new FlatButton("Partager ces coordonnées") { Glyph = Glyphs.Check };
            apply.FitWidth();
            apply.Location = new Point(x, y);
            apply.Click += (s, e) => ApplyCoords();
            c.Controls.Add(apply);
            y += Theme.S(44);

            var lb = Theme.Label("Afficher le cadre rouge autour de la zone", Theme.Semi(10f), Theme.Text, Theme.Card);
            lb.Location = new Point(Theme.S(20), y + Theme.S(4));
            var lbd = Theme.Label("Dessiné à l'extérieur de la zone : il n'apparaît pas dans la vidéo.", Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            lbd.Location = new Point(Theme.S(20), lb.Bottom + Theme.S(2));
            var tg = new Toggle { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            lock (AppConfig.Sync) tg.Checked = S.Border;
            tg.Location = new Point(c.Width - tg.Width - Theme.S(20), y + Theme.S(6));
            tg.CheckedChanged += (s, e) => { lock (AppConfig.Sync) S.Border = tg.Checked; cfg.Save(); ScreenShare.ShowBorder(tg.Checked); };
            c.Controls.AddRange(new Control[] { lb, lbd, tg });
            y = lbd.Bottom + Theme.S(16);

            status = Theme.Label("", Theme.Ui(9.5f), Theme.Muted, Theme.Card);
            status.MaximumSize = new Size(Theme.S(860), 0);
            status.Location = new Point(Theme.S(20), y);
            c.Controls.Add(status);
            SetNextY(c, y + Theme.S(44));
            return c;
        }

        #endregion

        #region Actions

        void Pick()
        {
            if (!ScreenShare.CameraReady()) { SetStatus("Installez d'abord la caméra virtuelle.", true); return; }
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
                    if (r == null) { SetStatus("Sélection annulée.", false); return; }
                    Share(r.Value);
                });
            };
            t.Start();
        }

        void ApplyCoords()
        {
            int x, y, w, h;
            if (!int.TryParse(coords["X"].Text, out x) || !int.TryParse(coords["Y"].Text, out y) ||
                !int.TryParse(coords["L"].Text, out w) || !int.TryParse(coords["H"].Text, out h))
            { SetStatus("Coordonnées invalides : nombres entiers attendus.", true); return; }
            w = w / 2 * 2;
            h = h / 2 * 2;
            if (w < 16 || h < 16) { SetStatus("Zone trop petite (16 × 16 minimum).", true); return; }
            if (!ScreenShare.CameraReady()) { SetStatus("Installez d'abord la caméra virtuelle.", true); return; }
            Share(new Rectangle(x, y, w, h));
        }

        void Share(Rectangle r)
        {
            lock (AppConfig.Sync) { S.X = r.X; S.Y = r.Y; S.W = r.Width; S.H = r.Height; }
            cfg.Save();
            coords["X"].Text = r.X.ToString(); coords["Y"].Text = r.Y.ToString();
            coords["L"].Text = r.Width.ToString(); coords["H"].Text = r.Height.ToString();
            bool border;
            lock (AppConfig.Sync) border = S.Border;
            ScreenShare.Start(r, border);
        }

        void SetStatus(string text, bool error)
        {
            if (status == null || status.IsDisposed) return;
            status.Text = text;
            status.ForeColor = error ? Theme.Red : Theme.Muted;
        }

        void UpdateState()
        {
            if (status == null || status.IsDisposed) return;
            if (ScreenShare.Error != null) SetStatus(ScreenShare.Error, true);
            else if (ScreenShare.Streaming)
            {
                var r = ScreenShare.Region;
                status.Text = "● En direct : zone " + r.Width + " × " + r.Height + " à (" + r.X + ", " + r.Y + ") → caméra « " + ScreenShare.CamName + " ».";
                status.ForeColor = Theme.Green;
            }
            else if (status.ForeColor != Theme.Red) SetStatus("Arrêté. Choisissez une zone pour commencer le partage.", false);
            pickBtn.Text = ScreenShare.Streaming ? "Changer de zone" : "Choisir une zone";
            pickBtn.FitWidth();
            stopBtn.Left = pickBtn.Right + Theme.S(8);
            stopBtn.Enabled = ScreenShare.Streaming;
        }

        #endregion
    }
}
