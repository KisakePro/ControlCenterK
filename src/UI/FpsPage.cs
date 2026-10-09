using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Page du module « FPS » : programmes surveillés et apparence de l'affichage en jeu.</summary>
    class FpsPage : Panel
    {
        readonly AppConfig cfg;
        readonly Panel scroll;
        readonly List<Card> cards = new List<Card>();
        Label titleLabel, subLabel, status;
        Card statusCard;

        FpsConfig F { get { return cfg.Fps; } }

        public FpsPage(AppConfig cfg)
        {
            this.cfg = cfg;
            BackColor = Theme.Bg;
            scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            scroll.HandleCreated += (s, e) => Theme.DarkScroll(scroll);
            scroll.Resize += (s, e) => LayoutCards();
            Controls.Add(scroll);
            FpsModule.Changed += OnChanged;
            Disposed += (s, e) => FpsModule.Changed -= OnChanged;
            Rebuild();
        }

        void OnChanged()
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(UpdateStatus)); } catch { }
        }

        #region Construction

        public void Rebuild()
        {
            int keep = -scroll.AutoScrollPosition.Y;
            scroll.AutoScrollPosition = Point.Empty;
            scroll.SuspendLayout();
            foreach (Control c in new List<Control>(Controls2(scroll))) { scroll.Controls.Remove(c); c.Dispose(); }
            cards.Clear();

            titleLabel = Theme.Label("FPS", Theme.Semi(18f), Theme.Text, Theme.Bg);
            titleLabel.Location = new Point(Theme.S(26), Theme.S(20));
            subLabel = Theme.Label("Affiche les images par seconde et leur courbe par-dessus les programmes choisis.", Theme.Ui(9.5f), Theme.Muted, Theme.Bg);
            subLabel.Location = new Point(Theme.S(28), Theme.S(58));
            scroll.Controls.Add(titleLabel);
            scroll.Controls.Add(subLabel);

            cards.Add(StatusCard());
            cards.Add(AppsCard());
            cards.Add(LookCard());
            foreach (var c in cards) scroll.Controls.Add(c);
            LayoutCards();
            scroll.ResumeLayout();
            scroll.AutoScrollPosition = new Point(0, keep);
            UpdateStatus();
        }

        static List<Control> Controls2(Control p)
        {
            var l = new List<Control>();
            foreach (Control c in p.Controls) l.Add(c);
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

        void Save() { cfg.Save(); }

        #endregion

        #region Cartes

        Card StatusCard()
        {
            var c = statusCard = NewCard("Mesure", null);
            status = Theme.Label("", Theme.Ui(9.5f), Theme.Muted, Theme.Card);
            status.MaximumSize = new Size(Theme.S(620), 0);
            status.Location = new Point(Theme.S(20), NextY(c));
            c.Controls.Add(status);
            var admin = new FlatButton("Relancer en administrateur", true) { Glyph = "", Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false, Name = "admin" };
            admin.FitWidth();
            admin.Location = new Point(c.Width - admin.Width - Theme.S(20), Theme.S(14));
            admin.Click += (s, e) => Host.RestartElevated();
            var retry = new FlatButton("Réessayer") { Glyph = Glyphs.Refresh, Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false, Name = "retry" };
            retry.FitWidth();
            retry.Location = new Point(c.Width - retry.Width - Theme.S(20), Theme.S(14));
            retry.Click += (s, e) => FpsModule.Restart();
            c.Controls.Add(admin);
            c.Controls.Add(retry);
            SetNextY(c, status.Bottom + Theme.S(28));
            return c;
        }

        void UpdateStatus()
        {
            if (status == null || status.IsDisposed) return;
            bool err = FpsCapture.Error != null;
            if (err) { status.Text = FpsCapture.Error + (FpsCapture.NeedsAdmin ? "\nWindows ne fournit le nombre d'images affichées qu'aux programmes lancés en administrateur." : ""); status.ForeColor = Theme.Red; }
            else if (FpsModule.Current != null) { status.Text = "Mesure en cours : " + FpsModule.Current + " — " + FpsModule.CurrentFps + " FPS"; status.ForeColor = Theme.Green; }
            else { status.Text = "Prêt : l'affichage apparaît dès qu'un des programmes ci-dessous est au premier plan."; status.ForeColor = Theme.Muted; }
            statusCard.Controls["admin"].Visible = err && FpsCapture.NeedsAdmin;
            statusCard.Controls["retry"].Visible = err && !FpsCapture.NeedsAdmin;
            SetNextY(statusCard, Math.Max(status.Bottom + Theme.S(28), Theme.S(96)));
            LayoutCards();
        }

        Card AppsCard()
        {
            var c = NewCard("Programmes",
                "L'affichage apparaît sur ces programmes quand ils sont au premier plan. Fonctionne en fenêtré et en plein écran fenêtré " +
                "(sans bordure) ; le plein écran exclusif de certains jeux masque les fenêtres par-dessus.");
            int y = NextY(c);
            var pick = new DropButton { Width = Theme.S(320), Location = new Point(Theme.S(20), y), Placeholder = "Choisir un programme ouvert…" };
            FillRunning(pick);
            var add = new FlatButton("Ajouter", true) { Glyph = Glyphs.Add };
            add.FitWidth();
            add.Location = new Point(pick.Right + Theme.S(8), y);
            add.Click += (s, e) => { if (!string.IsNullOrEmpty(pick.Value)) AddApp(pick.Value); };
            var browse = new FlatButton("Parcourir…") { Glyph = Glyphs.Folder };
            browse.FitWidth();
            browse.Location = new Point(add.Right + Theme.S(8), y);
            browse.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Title = "Choisir le programme", Filter = "Programmes (*.exe)|*.exe" })
                    if (dlg.ShowDialog(FindForm()) == DialogResult.OK) AddApp(System.IO.Path.GetFileNameWithoutExtension(dlg.FileName));
            };
            var refresh = new FlatButton("") { Glyph = Glyphs.Refresh, Width = Theme.S(34) };
            refresh.Location = new Point(browse.Right + Theme.S(8), y);
            refresh.Click += (s, e) => FillRunning(pick);
            c.Controls.AddRange(new Control[] { pick, add, browse, refresh });
            y = pick.Bottom + Theme.S(12);

            List<string> apps;
            lock (AppConfig.Sync) apps = new List<string>(F.Apps);
            if (apps.Count == 0)
            {
                var none = Theme.Label("Aucun programme : ajoutez votre jeu ci-dessus.", Theme.Ui(9f), Theme.Dim, Theme.Card);
                none.Location = new Point(Theme.S(20), y + Theme.S(4));
                c.Controls.Add(none);
                y = none.Bottom + Theme.S(8);
            }
            foreach (var app in apps)
            {
                string a = app;
                var row = new Card { BackColor = Theme.Surface, Radius = 6, Bounds = new Rectangle(Theme.S(14), y, Theme.S(870), Theme.S(44)),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                var icon = Theme.Label(Glyphs.App, Theme.Icon(12f), Theme.Accent, row.BackColor);
                icon.Location = new Point(Theme.S(12), Theme.S(13));
                var name = Theme.Label(Catalog.Describe(a, null), Theme.Semi(10f), Theme.Text, row.BackColor);
                name.Location = new Point(Theme.S(42), Theme.S(5));
                var exe = Theme.Label(a + ".exe", Theme.Ui(8.5f), Theme.Muted, row.BackColor);
                exe.Location = new Point(Theme.S(42), Theme.S(24));
                var del = new FlatButton("Retirer") { Anchor = AnchorStyles.Top | AnchorStyles.Right };
                del.FitWidth();
                del.Location = new Point(row.Width - del.Width - Theme.S(8), Theme.S(6));
                del.Click += (s, e) => { lock (AppConfig.Sync) F.Apps.Remove(a); Save(); Rebuild(); };
                row.Controls.AddRange(new Control[] { icon, name, exe, del });
                c.Controls.Add(row);
                y += Theme.S(50);
            }
            SetNextY(c, y);
            return c;
        }

        void AddApp(string name)
        {
            name = name.ToLowerInvariant();
            lock (AppConfig.Sync) if (!F.Apps.Contains(name)) F.Apps.Add(name);
            Save();
            Rebuild();
        }

        /// <summary>Programmes ouverts ayant une fenêtre (sans doublon), triés par nom.</summary>
        static void FillRunning(DropButton dd)
        {
            dd.Items.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<KeyValuePair<string, string>>();
            int self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self || p.MainWindowHandle == IntPtr.Zero || !seen.Add(p.ProcessName)) continue;
                    string title = p.MainWindowTitle;
                    list.Add(new KeyValuePair<string, string>(p.ProcessName.ToLowerInvariant(),
                        p.ProcessName + ".exe" + (string.IsNullOrEmpty(title) ? "" : "  —  " + (title.Length > 40 ? title.Substring(0, 40) + "…" : title))));
                }
                catch { }
                finally { p.Dispose(); }
            }
            list.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.CurrentCultureIgnoreCase));
            foreach (var kv in list) dd.Add(kv.Key, kv.Value);
            dd.Value = "";
        }

        Card LookCard()
        {
            var c = NewCard("Apparence", "Couleur, taille et emplacement de l'affichage, mesuré depuis un coin de la fenêtre du jeu.");
            var prev = new FlatButton("Aperçu (6 s)", true) { Glyph = "", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            prev.FitWidth();
            prev.Location = new Point(c.Width - prev.Width - Theme.S(20), Theme.S(14));
            prev.Click += (s, e) =>
            {
                if (!FpsModule.Running) return;
                var f = FindForm();
                if (f != null) FpsModule.Preview(f.Handle);
            };
            c.Controls.Add(prev);

            var color = new Button { FlatStyle = FlatStyle.Flat, Size = new Size(Theme.S(44), Theme.S(28)), Cursor = Cursors.Hand };
            lock (AppConfig.Sync) color.BackColor = Theme.FromHex(F.Color, Color.Lime);
            color.FlatAppearance.BorderColor = Theme.Border;
            color.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { FullOpen = true, Color = color.BackColor })
                    if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                    {
                        color.BackColor = dlg.Color;
                        lock (AppConfig.Sync) F.Color = Theme.ToHex(dlg.Color);
                        Save();
                    }
            };
            Row(c, "Couleur", "Du nombre et de la courbe.", color);

            Row(c, "Taille", null, Drop(Theme.S(200), new[] { 10, 12, 14, 16, 18, 22, 26, 32, 40, 48 }, v => v + " pt" + (v == 18 ? " (par défaut)" : ""),
                () => F.Size, v => F.Size = v));

            var corner = new DropButton { Width = Theme.S(200) };
            corner.Add("tl", "En haut à gauche");
            corner.Add("tr", "En haut à droite");
            corner.Add("bl", "En bas à gauche");
            corner.Add("br", "En bas à droite");
            lock (AppConfig.Sync) corner.Value = F.Corner;
            corner.ValueChanged += (s, e) => { lock (AppConfig.Sync) F.Corner = corner.Value; Save(); };
            Row(c, "Emplacement", null, corner);

            var offsets = new[] { 0, 4, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512 };
            Row(c, "Décalage horizontal", "Distance au bord gauche ou droit de la fenêtre.", Drop(Theme.S(200), offsets, v => v + " px", () => F.OffsetX, v => F.OffsetX = v));
            Row(c, "Décalage vertical", "Distance au bord haut ou bas de la fenêtre.", Drop(Theme.S(200), offsets, v => v + " px", () => F.OffsetY, v => F.OffsetY = v));

            var graph = new Toggle();
            lock (AppConfig.Sync) graph.Checked = F.Graph;
            graph.CheckedChanged += (s, e) => { lock (AppConfig.Sync) F.Graph = graph.Checked; Save(); };
            Row(c, "Courbe des FPS", "Sous le nombre : les chutes d'images y apparaissent en creux.", graph);

            Row(c, "Durée de la courbe", null, Drop(Theme.S(200), new[] { 5, 10, 20, 30, 60 }, v => v + " secondes", () => F.GraphSeconds, v => F.GraphSeconds = v));
            Row(c, "Opacité du fond", null, Drop(Theme.S(200), new[] { 0, 25, 40, 55, 70, 85, 100 }, v => v == 0 ? "Sans fond" : v + " %", () => F.Background, v => F.Background = v));
            return c;
        }

        DropButton Drop(int width, int[] values, Func<int, string> text, Func<int> get, Action<int> set)
        {
            var d = new DropButton { Width = width };
            int cur;
            lock (AppConfig.Sync) cur = get();
            bool found = false;
            foreach (int v in values) { d.Add(v.ToString(), text(v)); if (v == cur) found = true; }
            if (!found) d.Add(cur.ToString(), text(cur));
            d.Value = cur.ToString();
            d.ValueChanged += (s, e) => { lock (AppConfig.Sync) set(int.Parse(d.Value)); Save(); };
            return d;
        }

        #endregion
    }
}
