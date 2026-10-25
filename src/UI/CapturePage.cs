using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Page du module « Capture d'écran ».</summary>
    class CapturePage : Panel
    {
        readonly AppConfig cfg;
        readonly Panel scroll;
        readonly List<Card> cards = new List<Card>();
        Label titleLabel, subLabel;

        CaptureConfig C { get { return cfg.Capture; } }

        public CapturePage(AppConfig cfg)
        {
            this.cfg = cfg;
            BackColor = Theme.Bg;
            scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            scroll.HandleCreated += (s, e) => Theme.DarkScroll(scroll);
            scroll.Resize += (s, e) => LayoutCards();
            Controls.Add(scroll);
            ScreenCapture.Changed += OnChanged;
            Disposed += (s, e) => ScreenCapture.Changed -= OnChanged;
            Rebuild();
        }

        void OnChanged() { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(Rebuild)); } catch { } }

        void Rebuild()
        {
            int keep = -scroll.AutoScrollPosition.Y;
            scroll.AutoScrollPosition = Point.Empty;
            scroll.SuspendLayout();
            var old = new List<Control>();
            foreach (Control c in scroll.Controls) old.Add(c);
            foreach (var c in old) { scroll.Controls.Remove(c); c.Dispose(); }
            cards.Clear();
            titleLabel = Theme.Label("Capture d'écran", Theme.Semi(18f), Theme.Text, Theme.Bg);
            titleLabel.Location = new Point(Theme.S(26), Theme.S(20));
            subLabel = Theme.Label("Impr. écran : choisissez une zone, une fenêtre ou l'écran, puis copiez, enregistrez ou annotez l'image.", Theme.Ui(9.5f), Theme.Muted, Theme.Bg);
            subLabel.Location = new Point(Theme.S(28), Theme.S(58));
            scroll.Controls.Add(titleLabel);
            scroll.Controls.Add(subLabel);
            cards.Add(HelpCard());
            cards.Add(SettingsCard());
            cards.Add(RecentCard());
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
                d.MaximumSize = new Size(Theme.S(560), 0);
                d.Location = new Point(Theme.S(20), l.Bottom + Theme.S(2));
                c.Controls.Add(d);
                h = d.Bottom - y + Theme.S(8);
            }
            right.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            right.Location = new Point(c.Width - Theme.S(20) - right.Width, y + (Math.Min(h, Theme.S(48)) - right.Height) / 2);
            c.Controls.Add(right);
            SetNextY(c, y + h + Theme.S(6));
        }

        Card HelpCard()
        {
            var c = NewCard("Raccourcis", null);
            string[,] keys =
            {
                { "Impr. écran", "Figer l'écran et choisir : glisser = zone, clic = fenêtre survolée, Entrée = écran entier, Échap = annuler" },
                { "Ctrl + Impr. écran", "Capturer directement l'écran sous la souris" },
                { "Alt + Impr. écran", "Capturer directement la fenêtre active" },
                { "Dans le menu", "Ctrl+C copier · Ctrl+S enregistrer · Entrée copier et enregistrer · E éditeur · Échap annuler" },
                { "Dans l'éditeur", "Ctrl+Z / Ctrl+Y annuler / rétablir · Ctrl+C copier · Ctrl+S enregistrer" },
            };
            int y = NextY(c);
            for (int i = 0; i < keys.GetLength(0); i++)
            {
                var k = Theme.Label(keys[i, 0], Theme.Semi(9.5f), Theme.Accent, Theme.Card);
                k.Location = new Point(Theme.S(20), y);
                var d = Theme.Label(keys[i, 1], Theme.Ui(9f), Theme.Text, Theme.Card);
                d.MaximumSize = new Size(Theme.S(680), 0);
                d.Location = new Point(Theme.S(180), y);
                c.Controls.AddRange(new Control[] { k, d });
                y = Math.Max(k.Bottom, d.Bottom) + Theme.S(8);
            }
            var now = new FlatButton("Capturer une zone maintenant", true) { Glyph = "" };
            now.FitWidth();
            now.Location = new Point(Theme.S(20), y + Theme.S(4));
            now.Click += (s, e) =>
            {
                var f = FindForm();
                if (f != null) f.WindowState = FormWindowState.Minimized;
                var t = new Timer { Interval = 300 };
                t.Tick += (s2, e2) => { t.Stop(); t.Dispose(); ScreenCapture.Start("region"); };
                t.Start();
            };
            c.Controls.Add(now);
            SetNextY(c, now.Bottom + Theme.S(8));
            return c;
        }

        Card SettingsCard()
        {
            var c = NewCard("Réglages", null);
            var hk = new Toggle();
            lock (AppConfig.Sync) hk.Checked = C.HotKey;
            hk.CheckedChanged += (s, e) => { lock (AppConfig.Sync) C.HotKey = hk.Checked; cfg.Save(); ScreenCapture.UpdateHook(); };
            Row(c, "Utiliser la touche Impr. écran", "ControlCenterK prend la touche en priorité (avant Windows, ShareX, OneDrive…). Désactivez pour la rendre aux autres outils.", hk);

            var after = new DropButton { Width = Theme.S(260) };
            after.Add("menu", "Afficher le menu (recommandé)");
            after.Add("copy", "Copier directement");
            after.Add("save", "Enregistrer directement");
            after.Add("copysave", "Copier et enregistrer");
            after.Add("editor", "Ouvrir l'éditeur");
            lock (AppConfig.Sync) after.Value = C.After;
            after.ValueChanged += (s, e) => { lock (AppConfig.Sync) C.After = after.Value; cfg.Save(); };
            Row(c, "Après la sélection", null, after);

            var cur = new Toggle();
            lock (AppConfig.Sync) cur.Checked = C.Cursor;
            cur.CheckedChanged += (s, e) => { lock (AppConfig.Sync) C.Cursor = cur.Checked; cfg.Save(); };
            Row(c, "Inclure le curseur de la souris", null, cur);

            var fmt = new DropButton { Width = Theme.S(260) };
            fmt.Add("png", "PNG (sans perte)");
            fmt.Add("jpg", "JPEG (plus léger)");
            lock (AppConfig.Sync) fmt.Value = C.Format;
            fmt.ValueChanged += (s, e) => { lock (AppConfig.Sync) C.Format = fmt.Value; cfg.Save(); };
            Row(c, "Format des fichiers", null, fmt);

            string folder;
            lock (AppConfig.Sync) folder = C.FolderPath;
            var change = new FlatButton("Changer…") { Glyph = Glyphs.Folder };
            change.FitWidth();
            change.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "Dossier des captures", SelectedPath = folder })
                    if (dlg.ShowDialog(FindForm()) == DialogResult.OK) { lock (AppConfig.Sync) C.Folder = dlg.SelectedPath; cfg.Save(); Rebuild(); }
            };
            Row(c, "Dossier des captures", folder, change);
            var open = new FlatButton("Ouvrir le dossier") { Glyph = "" };
            open.FitWidth();
            open.Click += (s, e) => { try { Directory.CreateDirectory(folder); Process.Start(folder); } catch { } };
            open.Location = new Point(change.Left - open.Width - Theme.S(8), change.Top);
            open.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            c.Controls.Add(open);

            var holder = new Panel { BackColor = Theme.Surface, Size = new Size(Theme.S(260), Theme.S(30)) };
            var pat = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Ui(9.5f) };
            lock (AppConfig.Sync) pat.Text = C.Pattern;
            pat.Bounds = new Rectangle(Theme.S(8), (holder.Height - pat.PreferredHeight) / 2, holder.Width - Theme.S(16), pat.PreferredHeight);
            holder.Controls.Add(pat);
            string example;
            try { example = DateTime.Now.ToString(pat.Text); } catch { example = "?"; }
            Row(c, "Nom des fichiers", "Format de date .NET, texte entre apostrophes. Exemple : " + example, holder);
            pat.Leave += (s, e) => { lock (AppConfig.Sync) C.Pattern = pat.Text.Trim().Length > 0 ? pat.Text.Trim() : "'Capture' yyyy-MM-dd HH.mm.ss"; cfg.Save(); Rebuild(); };
            return c;
        }

        Card RecentCard()
        {
            var c = NewCard("Dernières captures enregistrées", null);
            List<string> files;
            lock (AppConfig.Sync) files = C.Recent.FindAll(File.Exists);
            int y = NextY(c);
            if (files.Count == 0)
            {
                var none = Theme.Label("Aucune pour l'instant.", Theme.Ui(9f), Theme.Dim, Theme.Card);
                none.Location = new Point(Theme.S(20), y);
                c.Controls.Add(none);
                SetNextY(c, none.Bottom + Theme.S(8));
                return c;
            }
            foreach (var f in files)
            {
                string path = f;
                var row = new Card { BackColor = Theme.Surface, Radius = 6, Bounds = new Rectangle(Theme.S(14), y, c.Width - Theme.S(28), Theme.S(56)),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                var pic = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Bg, Bounds = new Rectangle(Theme.S(8), Theme.S(6), Theme.S(78), Theme.S(44)) };
                try { using (var tmp = Image.FromFile(path)) pic.Image = new Bitmap(tmp, Theme.S(156), Math.Max(1, Theme.S(156) * tmp.Height / Math.Max(1, tmp.Width))); } catch { }
                var name = Theme.Label(Path.GetFileName(path), Theme.Semi(9.5f), Theme.Text, row.BackColor);
                name.Location = new Point(Theme.S(96), Theme.S(9));
                var when = Theme.Label(File.GetLastWriteTime(path).ToString("dd/MM/yyyy HH:mm"), Theme.Ui(8.5f), Theme.Muted, row.BackColor);
                when.Location = new Point(Theme.S(96), Theme.S(30));
                row.Controls.AddRange(new Control[] { pic, name, when });
                int right = row.Width - Theme.S(8);
                foreach (var spec in new[] { "folder", "copy", "open" })
                {
                    var b = new FlatButton(spec == "open" ? "Ouvrir" : spec == "copy" ? "Copier" : "Dossier") { Anchor = AnchorStyles.Top | AnchorStyles.Right };
                    b.FitWidth();
                    b.Location = new Point(right - b.Width, Theme.S(11));
                    right = b.Left - Theme.S(6);
                    string sp = spec;
                    b.Click += (s, e) =>
                    {
                        try
                        {
                            if (sp == "open") Process.Start(path);
                            else if (sp == "folder") Process.Start("explorer.exe", "/select,\"" + path + "\"");
                            else using (var img = Image.FromFile(path)) ScreenCapture.Copy(img);
                        }
                        catch { }
                    };
                    row.Controls.Add(b);
                }
                c.Controls.Add(row);
                y += Theme.S(62);
            }
            SetNextY(c, y);
            return c;
        }
    }
}
