using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Page du module « Capture vidéo ».</summary>
    class VideoPage : Panel
    {
        readonly AppConfig cfg;
        readonly Panel scroll;
        readonly List<Card> cards = new List<Card>();
        Label titleLabel, subLabel;

        VideoConfig C { get { return cfg.Video; } }

        public VideoPage(AppConfig cfg)
        {
            this.cfg = cfg;
            BackColor = Theme.Bg;
            scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            scroll.HandleCreated += (s, e) => Theme.DarkScroll(scroll);
            scroll.Resize += (s, e) => LayoutCards();
            Controls.Add(scroll);
            VideoRecorder.Changed += OnChanged;
            Disposed += (s, e) => VideoRecorder.Changed -= OnChanged;
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
            titleLabel = Theme.Label("Capture vidéo", Theme.Semi(18f), Theme.Text, Theme.Bg);
            titleLabel.Location = new Point(Theme.S(26), Theme.S(20));
            subLabel = Theme.Label("Enregistrez une zone de l'écran en MP4, WMV ou AVI. Le cadre rouge et le panneau d'arrêt n'apparaissent pas dans la vidéo.", Theme.Ui(9.5f), Theme.Muted, Theme.Bg);
            subLabel.Location = new Point(Theme.S(28), Theme.S(58));
            scroll.Controls.Add(titleLabel);
            scroll.Controls.Add(subLabel);
            cards.Add(RecordCard());
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

        /// <summary>Réduit la fenêtre puis lance l'action, pour qu'elle ne soit pas dans la vidéo.</summary>
        void Hidden(Action a)
        {
            var f = FindForm();
            if (f != null) f.WindowState = FormWindowState.Minimized;
            var t = new Timer { Interval = 350 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); a(); };
            t.Start();
        }

        Card RecordCard()
        {
            var c = NewCard("Enregistrer", VideoRecorder.Error ?? (VideoRecorder.Warning != null ? "Dernière vidéo : " + VideoRecorder.Warning : null));
            int y = NextY(c);
            if (VideoRecorder.Recording)
            {
                var z = VideoRecorder.Zone;
                var st = Theme.Label("●  Enregistrement en cours (" + z.Width + " × " + z.Height + ")", Theme.Semi(10f), Theme.Red, Theme.Card);
                st.Location = new Point(Theme.S(20), y + Theme.S(6));
                var stop = new FlatButton("Arrêter", true) { Glyph = "" };
                stop.FitWidth();
                stop.Location = new Point(Theme.S(20), st.Bottom + Theme.S(10));
                stop.Click += (s, e) => VideoRecorder.Stop();
                c.Controls.AddRange(new Control[] { st, stop });
                SetNextY(c, stop.Bottom + Theme.S(8));
                return c;
            }
            var zone = new FlatButton("Choisir une zone et enregistrer", true) { Glyph = "" };
            zone.FitWidth();
            zone.Location = new Point(Theme.S(20), y);
            zone.Click += (s, e) => Hidden(VideoRecorder.PickAndStart);
            var screen = new FlatButton("Enregistrer l'écran entier") { Glyph = "" };
            screen.FitWidth();
            screen.Location = new Point(zone.Right + Theme.S(8), y);
            screen.Click += (s, e) => Hidden(VideoRecorder.StartScreen);
            c.Controls.AddRange(new Control[] { zone, screen });
            var hint = Theme.Label("Écran entier : celui sous la souris. Arrêt : bouton du panneau près de la zone, ou Ctrl+Alt+F11 si le raccourci est actif. ",
                Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            hint.MaximumSize = new Size(Theme.S(860), 0);
            hint.Location = new Point(Theme.S(20), zone.Bottom + Theme.S(10));
            c.Controls.Add(hint);
            SetNextY(c, hint.Bottom + Theme.S(4));
            return c;
        }

        Card SettingsCard()
        {
            var c = NewCard("Réglages", null);
            var fmt = new DropButton { Width = Theme.S(280) };
            fmt.Add("mp4", "MP4 (H.264) : léger, recommandé");
            fmt.Add("wmv", "WMV (Windows Media)");
            fmt.Add("avi", "AVI (Motion JPEG) : pour le montage");
            lock (AppConfig.Sync) fmt.Value = C.Format;
            fmt.ValueChanged += (s, e) => { lock (AppConfig.Sync) C.Format = fmt.Value; cfg.Save(); };
            Row(c, "Format vidéo", "Encodé par Windows (carte graphique si possible), sans codec à installer. AVI : fichiers plus gros, 4 Go au plus.", fmt);

            var size = new DropButton { Width = Theme.S(280) };
            size.Add("source", "Taille d'origine de la zone");
            size.Add("2160", "4K (3840 × 2160)");
            size.Add("1440", "1440p (2560 × 1440)");
            size.Add("1080", "1080p (1920 × 1080)");
            size.Add("720", "720p (1280 × 720)");
            size.Add("480", "480p (854 × 480)");
            lock (AppConfig.Sync) size.Value = C.Size;
            size.ValueChanged += (s, e) => { lock (AppConfig.Sync) C.Size = size.Value; cfg.Save(); };
            Row(c, "Taille de la vidéo", "La zone est agrandie ou réduite pour tenir dans ce cadre, sans être déformée.", size);

            var fps = new DropButton { Width = Theme.S(280) };
            foreach (int f in new[] { 10, 15, 24, 25, 30, 50, 60 }) fps.Add(f.ToString(), f + " images/s" + (f == 30 ? " (recommandé)" : ""));
            lock (AppConfig.Sync) fps.Value = C.Fps.ToString();
            fps.ValueChanged += (s, e) => { lock (AppConfig.Sync) C.Fps = int.Parse(fps.Value); cfg.Save(); };
            Row(c, "Images par seconde", "60 images/s sur une grande zone peut sauter des images selon le PC.", fps);

            var snd = new DropButton { Width = Theme.S(280) };
            snd.Add("none", "Aucun son");
            snd.Add("system", "Son du PC");
            snd.Add("mic", "Micro");
            snd.Add("both", "Son du PC + micro");
            lock (AppConfig.Sync) snd.Value = C.Audio;
            snd.ValueChanged += (s, e) => { lock (AppConfig.Sync) C.Audio = snd.Value; cfg.Save(); };
            Row(c, "Son", "Sortie et micro par défaut de Windows, en 48 kHz stéréo (AAC dans le MP4, WMA dans le WMV, PCM dans l'AVI).", snd);

            var q = new DropButton { Width = Theme.S(280) };
            q.Add("low", "Basse (fichiers légers)");
            q.Add("medium", "Moyenne");
            q.Add("high", "Haute");
            lock (AppConfig.Sync) q.Value = C.Quality;
            q.ValueChanged += (s, e) => { lock (AppConfig.Sync) C.Quality = q.Value; cfg.Save(); };
            Row(c, "Qualité", null, q);

            var cur = new Toggle();
            lock (AppConfig.Sync) cur.Checked = C.Cursor;
            cur.CheckedChanged += (s, e) => { lock (AppConfig.Sync) C.Cursor = cur.Checked; cfg.Save(); };
            Row(c, "Inclure le curseur de la souris", null, cur);

            var hk = new Toggle();
            lock (AppConfig.Sync) hk.Checked = C.HotKey;
            hk.CheckedChanged += (s, e) => { lock (AppConfig.Sync) C.HotKey = hk.Checked; cfg.Save(); VideoRecorder.UpdateHotKey(); };
            Row(c, "Raccourci Ctrl+Alt+F11", "Choisir une zone et enregistrer ; le même raccourci arrête l'enregistrement.", hk);

            string folder;
            lock (AppConfig.Sync) folder = C.FolderPath;
            var change = new FlatButton("Changer…") { Glyph = Glyphs.Folder };
            change.FitWidth();
            change.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "Dossier des vidéos", SelectedPath = folder })
                    if (dlg.ShowDialog(FindForm()) == DialogResult.OK) { lock (AppConfig.Sync) C.Folder = dlg.SelectedPath; cfg.Save(); Rebuild(); }
            };
            Row(c, "Dossier des vidéos", folder, change);
            var open = new FlatButton("Ouvrir le dossier") { Glyph = Glyphs.Folder };
            open.FitWidth();
            open.Click += (s, e) => { try { Directory.CreateDirectory(folder); Process.Start(folder); } catch { } };
            open.Location = new Point(change.Left - open.Width - Theme.S(8), change.Top);
            open.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            c.Controls.Add(open);
            return c;
        }

        Card RecentCard()
        {
            var c = NewCard("Dernières vidéos", null);
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
                var row = new Card { BackColor = Theme.Surface, Radius = 6, Bounds = new Rectangle(Theme.S(14), y, c.Width - Theme.S(28), Theme.S(52)),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                var name = Theme.Label(Path.GetFileName(path), Theme.Semi(9.5f), Theme.Text, row.BackColor);
                name.Location = new Point(Theme.S(12), Theme.S(7));
                var info = new FileInfo(path);
                var when = Theme.Label(info.LastWriteTime.ToString("dd/MM/yyyy HH:mm") + "   ·   " + (info.Length / 1048576.0).ToString("0.0") + " Mo", Theme.Ui(8.5f), Theme.Muted, row.BackColor);
                when.Location = new Point(Theme.S(12), Theme.S(28));
                row.Controls.AddRange(new Control[] { name, when });
                int right = row.Width - Theme.S(8);
                foreach (var spec in new[] { "delete", "folder", "open" })
                {
                    var b = spec == "delete" ? new FlatButton("") { Glyph = "", Width = Theme.S(36) }
                                              : new FlatButton(spec == "open" ? "Ouvrir" : "Dossier");
                    b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    if (spec != "delete") b.FitWidth();
                    b.Location = new Point(right - b.Width, Theme.S(10));
                    right = b.Left - Theme.S(6);
                    string sp = spec;
                    b.Click += (s, e) =>
                    {
                        try
                        {
                            if (sp == "delete") Delete(path);
                            else if (sp == "open") Process.Start(path);
                            else Process.Start("explorer.exe", "/select,\"" + path + "\"");
                        }
                        catch { }
                    };
                    row.Controls.Add(b);
                }
                c.Controls.Add(row);
                y += Theme.S(58);
            }
            SetNextY(c, y);
            return c;
        }

        void Delete(string path)
        {
            if (MessageBox.Show(FindForm(), "Supprimer « " + Path.GetFileName(path) + " » ?\nLe fichier sera envoyé à la Corbeille.",
                    "Supprimer la vidéo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (!CapturePage.Recycle(path))
            {
                MessageBox.Show(FindForm(), "Impossible de supprimer le fichier (est-il ouvert dans un lecteur ?).", "Supprimer la vidéo", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            lock (AppConfig.Sync) C.Recent.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
            cfg.Save();
            Rebuild();
        }
    }
}
