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
            cards.Add(ZonesCard());
            cards.Add(KeysCard());
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

        /// <summary>Réduit la fenêtre puis trace une zone ; la fenêtre revient ensuite (sauf si on enregistre).</summary>
        void Trace(bool record)
        {
            var f = FindForm();
            Hidden(() =>
            {
                ZoneSelector.Pick(r =>
                {
                    if (r.HasValue && r.Value.Width >= 16 && r.Value.Height >= 16)
                    {
                        VideoRecorder.AddZone(r.Value);
                        if (record) { VideoRecorder.Start(r.Value); return; }
                    }
                    if (f != null) { f.WindowState = FormWindowState.Normal; f.Activate(); }
                });
            });
        }

        static string KeyHint(string k) { return string.IsNullOrEmpty(k) ? "" : "  (" + k + ")"; }

        Card RecordCard()
        {
            var c = NewCard("Enregistrer", VideoRecorder.Error ?? (VideoRecorder.Warning != null ? "Dernière vidéo : " + VideoRecorder.Warning : null));
            int y = NextY(c);
            string toggleKey;
            bool keys;
            lock (AppConfig.Sync) { toggleKey = C.KeyToggle; keys = C.HotKey; }
            if (!keys) toggleKey = "";
            if (VideoRecorder.Recording)
            {
                var z = VideoRecorder.Zone;
                var st = Theme.Label("●  Enregistrement en cours (" + z.Width + " × " + z.Height + ")", Theme.Semi(10f), Theme.Red, Theme.Card);
                st.Location = new Point(Theme.S(20), y + Theme.S(6));
                var stop = new FlatButton("Arrêter" + KeyHint(toggleKey), true) { Glyph = "\uE71A" };
                stop.FitWidth();
                stop.Location = new Point(Theme.S(20), st.Bottom + Theme.S(10));
                stop.Click += (s, e) => VideoRecorder.Stop();
                c.Controls.AddRange(new Control[] { st, stop });
                SetNextY(c, stop.Bottom + Theme.S(8));
                return c;
            }
            string target = "Zone choisie : " + VideoRecorder.SelectedLabel;
            var info = Theme.Label(target, Theme.Semi(10f), Theme.Text, Theme.Card);
            info.Location = new Point(Theme.S(20), y + Theme.S(2));
            var rec = new FlatButton("Enregistrer" + KeyHint(toggleKey), true) { Glyph = "\uE7C8" };
            rec.FitWidth();
            rec.Location = new Point(Theme.S(20), info.Bottom + Theme.S(10));
            rec.Click += (s, e) => Hidden(VideoRecorder.Toggle);
            var trace = new FlatButton("Tracer une zone et enregistrer") { Glyph = "\uE714" };
            trace.FitWidth();
            trace.Location = new Point(rec.Right + Theme.S(8), rec.Top);
            trace.Click += (s, e) => Trace(true);
            c.Controls.AddRange(new Control[] { info, rec, trace });
            var hint = Theme.Label("Arrêt : bouton du panneau rouge près de la zone, ce bouton ou le raccourci. Choisissez ou tracez la zone ci-dessous, puis lancez quand vous voulez.",
                Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            hint.MaximumSize = new Size(Theme.S(860), 0);
            hint.Location = new Point(Theme.S(20), rec.Bottom + Theme.S(10));
            c.Controls.Add(hint);
            SetNextY(c, hint.Bottom + Theme.S(4));
            return c;
        }

        Card ZonesCard()
        {
            var c = NewCard("Zones", "La zone choisie est celle qu'enregistrent le bouton « Enregistrer » et le raccourci. Son cadre s'affiche 2 secondes quand on la choisit.");
            int y = NextY(c);
            var add = new FlatButton("Tracer une nouvelle zone", true) { Glyph = Glyphs.Add };
            add.FitWidth();
            add.Location = new Point(Theme.S(20), y);
            add.Click += (s, e) => Trace(false);
            c.Controls.Add(add);
            var frameT = new Toggle { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            lock (AppConfig.Sync) frameT.Checked = C.ShowFrame;
            frameT.Location = new Point(c.Width - frameT.Width - Theme.S(20), y + Theme.S(6));
            frameT.CheckedChanged += (s, e) => { lock (AppConfig.Sync) C.ShowFrame = frameT.Checked; cfg.Save(); VideoRecorder.RefreshPreview(); };
            var frameL = Theme.Label("Toujours afficher le cadre de la zone choisie", Theme.Ui(9f), Theme.Text, Theme.Card);
            frameL.AutoSize = false;
            frameL.Size = TextRenderer.MeasureText(frameL.Text, frameL.Font) + new Size(Theme.S(4), Theme.S(2));
            frameL.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            frameL.Location = new Point(frameT.Left - frameL.Width - Theme.S(8), y + Theme.S(7));
            c.Controls.AddRange(new Control[] { frameT, frameL });
            y = add.Bottom + Theme.S(12);

            List<ShareZone> zones;
            string selected;
            lock (AppConfig.Sync) { zones = new List<ShareZone>(C.Zones); selected = C.Selected; }
            var mons = VideoRecorder.Monitors();
            string selDev = selected.StartsWith("mon:") ? selected.Substring(4) : null;
            // "mon:" seul (ou écran débranché) = écran principal
            if (selDev != null && !mons.Exists(m => m.Device == selDev) && mons.Count > 0) selDev = mons[0].Device;
            foreach (var m in mons) y = ScreenRow(c, y, m, m.Device == selDev);
            foreach (var z in zones) y = ZoneRow(c, y, z, z.Id == selected);
            SetNextY(c, y);
            return c;
        }

        /// <summary>Un écran entier.</summary>
        int ScreenRow(Card c, int y, VideoRecorder.MonitorInfo m, bool selected)
        {
            var row = new Card { BackColor = selected ? Theme.Mix(Theme.Surface, Theme.Accent, 0.16f) : Theme.Surface, Radius = 6,
                Bounds = new Rectangle(Theme.S(14), y, c.Width - Theme.S(28), Theme.S(48)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            string id = "mon:" + m.Device;
            var pick = new FlatButton(selected ? "Choisi" : "Choisir", selected) { Glyph = selected ? Glyphs.Check : null, Enabled = !selected };
            pick.FitWidth();
            pick.Location = new Point(Theme.S(8), Theme.S(8));
            pick.Click += (s, e) => VideoRecorder.Select(id);
            var icon = Theme.Label("\uE7F4", Theme.Icon(12f), Theme.Muted, row.BackColor);
            icon.Location = new Point(pick.Right + Theme.S(14), Theme.S(14));
            var n = Theme.Label(m.Name, Theme.Semi(9.5f), Theme.Text, row.BackColor);
            n.Location = new Point(icon.Right + Theme.S(8), Theme.S(15));
            row.Controls.AddRange(new Control[] { pick, icon, n });
            c.Controls.Add(row);
            return y + Theme.S(54);
        }

        /// <summary>Une zone tracée : choisir, renommer, couleur, redéfinir, supprimer.</summary>
        int ZoneRow(Card c, int y, ShareZone z, bool selected)
        {
            var color = z == null ? Theme.Accent : Theme.FromHex(z.Color, Color.Red);
            var row = new Card { BackColor = selected ? Theme.Mix(Theme.Surface, color, 0.16f) : Theme.Surface, Radius = 6,
                Bounds = new Rectangle(Theme.S(14), y, c.Width - Theme.S(28), Theme.S(48)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            string id = z == null ? "screen" : z.Id;
            var pick = new FlatButton(selected ? "Choisie" : "Choisir", selected) { Glyph = selected ? Glyphs.Check : null, Enabled = !selected };
            pick.FitWidth();
            pick.Location = new Point(Theme.S(8), Theme.S(8));
            pick.Click += (s, e) => VideoRecorder.Select(id);
            row.Controls.Add(pick);
            int x = pick.Right + Theme.S(12);
            if (z == null)
            {
                var n = Theme.Label("Écran sous la souris", Theme.Semi(9.5f), Theme.Text, row.BackColor);
                n.Location = new Point(x, Theme.S(15));
                row.Controls.Add(n);
                c.Controls.Add(row);
                return y + Theme.S(54);
            }
            var swatch = new Button { FlatStyle = FlatStyle.Flat, BackColor = color, Size = new Size(Theme.S(26), Theme.S(26)), Location = new Point(x, Theme.S(11)), Cursor = Cursors.Hand };
            swatch.FlatAppearance.BorderColor = Theme.Border;
            swatch.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { FullOpen = true, Color = swatch.BackColor })
                {
                    if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                    lock (AppConfig.Sync) { var zz = C.Find(id); if (zz != null) zz.Color = Theme.ToHex(dlg.Color); }
                    cfg.Save();
                    VideoRecorder.Flash();
                    Rebuild();
                }
            };
            var holder = new Panel { BackColor = Theme.SurfaceHi, Bounds = new Rectangle(swatch.Right + Theme.S(10), Theme.S(9), Theme.S(180), Theme.S(30)) };
            var name = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.SurfaceHi, ForeColor = Theme.Text, Font = Theme.Ui(9.5f), Text = z.Name };
            name.Bounds = new Rectangle(Theme.S(8), (holder.Height - name.PreferredHeight) / 2, holder.Width - Theme.S(16), name.PreferredHeight);
            holder.Controls.Add(name);
            name.Leave += (s, e) =>
            {
                string n = name.Text.Trim();
                if (n.Length == 0) return;
                lock (AppConfig.Sync) { var zz = C.Find(id); if (zz != null) zz.Name = n; }
                cfg.Save();
            };
            var dims = Theme.Label(z.W + " × " + z.H + "  à (" + z.X + ", " + z.Y + ")", Theme.Ui(8.5f), Theme.Muted, row.BackColor);
            dims.Location = new Point(holder.Right + Theme.S(12), Theme.S(16));
            row.Controls.AddRange(new Control[] { swatch, holder, dims });
            int right = row.Width - Theme.S(8);
            foreach (var spec in new[] { "del", "redo" })
            {
                var b = spec == "del" ? new FlatButton("") { Glyph = "\uE74D", Width = Theme.S(36) } : new FlatButton("Redéfinir") { Glyph = "\uE7A8" };
                if (spec != "del") b.FitWidth();
                b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                b.Location = new Point(right - b.Width, Theme.S(8));
                right = b.Left - Theme.S(6);
                if (spec == "del")
                    b.Click += (s, e) =>
                    {
                        if (MessageBox.Show(FindForm(), "Supprimer la zone « " + z.Name + " » ?", "Supprimer la zone", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                        lock (AppConfig.Sync) { C.Zones.RemoveAll(zz => zz.Id == id); if (C.Selected == id) C.Selected = "mon:"; }
                        cfg.Save();
                        VideoRecorder.RefreshPreview();
                        Rebuild();
                    };
                else
                    b.Click += (s, e) =>
                    {
                        var f = FindForm();
                        Hidden(() => ZoneSelector.Pick(r =>
                        {
                            if (f != null) { f.WindowState = FormWindowState.Normal; f.Activate(); }
                            if (!r.HasValue || r.Value.Width < 16 || r.Value.Height < 16) return;
                            lock (AppConfig.Sync) { var zz = C.Find(id); if (zz != null) { zz.X = r.Value.X; zz.Y = r.Value.Y; zz.W = r.Value.Width; zz.H = r.Value.Height; } C.Selected = id; }
                            cfg.Save();
                            VideoRecorder.Flash();
                            Rebuild();
                        }));
                    };
                row.Controls.Add(b);
            }
            c.Controls.Add(row);
            return y + Theme.S(54);
        }

        Card KeysCard()
        {
            var c = NewCard("Raccourcis", "Cliquez sur un raccourci puis appuyez sur la combinaison voulue (Échap : annuler, Suppr : aucun). Évitez Ctrl+Alt + chiffre ou lettre : c'est AltGr en AZERTY.");
            var on = new Toggle();
            lock (AppConfig.Sync) on.Checked = C.HotKey;
            on.CheckedChanged += (s, e) => { lock (AppConfig.Sync) C.HotKey = on.Checked; cfg.Save(); VideoRecorder.UpdateHotKey(); Rebuild(); };
            Row(c, "Raccourcis actifs", "Fonctionnent partout, même quand ControlCenterK est fermé dans la zone de notification.", on);
            KeyRow(c, "Démarrer / arrêter", "Enregistre la zone choisie ; arrête l'enregistrement en cours.", () => C.KeyToggle, v => C.KeyToggle = v);
            KeyRow(c, "Tracer une zone", "Trace une nouvelle zone, qui devient la zone choisie (sans enregistrer).", () => C.KeyPick, v => C.KeyPick = v);
            KeyRow(c, "Tracer et enregistrer", "Trace une zone et enregistre aussitôt ; arrête si un enregistrement est en cours.", () => C.KeyPickRecord, v => C.KeyPickRecord = v);
            if (VideoRecorder.HotKeyFailures.Count > 0)
            {
                var warn = Theme.Label("Déjà utilisé par un autre programme : " + string.Join(", ", VideoRecorder.HotKeyFailures) + ". Choisissez une autre combinaison.", Theme.Ui(8.5f), Theme.Red, Theme.Card);
                warn.MaximumSize = new Size(Theme.S(860), 0);
                warn.Location = new Point(Theme.S(20), NextY(c));
                c.Controls.Add(warn);
                SetNextY(c, warn.Bottom + Theme.S(8));
            }
            return c;
        }

        DropButton SoundDrop(List<DeviceInfo> devices, string none, Func<string> get, Action<string> set)
        {
            var d = new DropButton { Width = Theme.S(280) };
            d.Add("", none);
            d.Add("default", "Par défaut de Windows");
            foreach (var dev in devices) d.Add(dev.Id, dev.Name + (dev.IsDefault ? " (par défaut)" : ""));
            string cur;
            lock (AppConfig.Sync) cur = get() ?? "";
            if (cur.Length > 0 && cur != "default" && !devices.Exists(x => x.Id == cur)) d.Add(cur, "Périphérique débranché");
            d.Value = cur;
            d.ValueChanged += (s, e) => { lock (AppConfig.Sync) set(d.Value); cfg.Save(); };
            return d;
        }

        void KeyRow(Card c, string label, string desc, Func<string> get, Action<string> set)
        {
            string cur;
            lock (AppConfig.Sync) cur = get();
            var box = new HotKeyBox { Width = Theme.S(200), Value = cur };
            box.ValueChanged += (s, e) =>
            {
                lock (AppConfig.Sync) set(box.Value);
                cfg.Save();
                VideoRecorder.UpdateHotKey();
                Rebuild();
            };
            Row(c, label, desc, box);
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

            List<DeviceInfo> outs, mics;
            try { var sys = new AudioSystem(); outs = sys.ListDevices(Flow.Render); mics = sys.ListDevices(Flow.Capture); }
            catch { outs = new List<DeviceInfo>(); mics = new List<DeviceInfo>(); }
            var sndOut = SoundDrop(outs, "Aucun son du PC", () => C.SoundOut, v => C.SoundOut = v);
            Row(c, "Son du PC", "Ce que joue une sortie du mélangeur Windows (haut-parleurs, casque…). 48 kHz stéréo : AAC dans le MP4, WMA dans le WMV, PCM dans l'AVI.", sndOut);
            var sndMic = SoundDrop(mics, "Aucun micro", () => C.SoundMic, v => C.SoundMic = v);
            Row(c, "Micro", "Mélangé au son du PC s'ils sont tous les deux choisis.", sndMic);

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

    /// <summary>Fenêtre de saisie d'un raccourci : la combinaison s'affiche en direct, OK pour valider.</summary>
    sealed class HotKeyDialog : Form
    {
        public string Result = "";
        readonly Label shown;
        readonly FlatButton ok;

        public HotKeyDialog()
        {
            Text = "Raccourci";
            Icon = Program.AppIcon;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            ClientSize = new Size(Theme.S(420), Theme.S(170));
            var hint = Theme.Label("Appuyez sur la combinaison voulue (par exemple Ctrl+Alt+F11).\nÉchap : annuler · Suppr : aucun raccourci.", Theme.Ui(9f), Theme.Muted, Theme.Bg);
            hint.Location = new Point(Theme.S(16), Theme.S(14));
            shown = Theme.Label("…", Theme.Semi(16f), Theme.Accent, Theme.Bg);
            shown.AutoSize = false;
            shown.TextAlign = ContentAlignment.MiddleCenter;
            shown.Bounds = new Rectangle(Theme.S(16), Theme.S(58), ClientSize.Width - Theme.S(32), Theme.S(44));
            ok = new FlatButton("OK", true) { Enabled = false };
            ok.FitWidth();
            ok.Location = new Point(ClientSize.Width - ok.Width - Theme.S(16), ClientSize.Height - ok.Height - Theme.S(14));
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            var cancel = new FlatButton("Annuler");
            cancel.FitWidth();
            cancel.Location = new Point(ok.Left - cancel.Width - Theme.S(8), ok.Top);
            cancel.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { hint, shown, ok, cancel });
        }

        // les touches arrivent ici avant les boutons (Entrée, Échap, flèches, Alt…)
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) { Close(); return true; }
            if (key == Keys.Enter && ok.Enabled && (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) == 0) { DialogResult = DialogResult.OK; Close(); return true; }
            if (key == Keys.Delete || key == Keys.Back) { Result = ""; DialogResult = DialogResult.OK; Close(); return true; }
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;
            // une touche seule bloquerait la frappe partout : modificateur obligatoire, sauf F1…F24
            bool fkey = key >= Keys.F1 && key <= Keys.F24;
            if ((keyData & (Keys.Control | Keys.Alt | Keys.Shift)) == 0 && !fkey) { shown.Text = "Ajoutez Ctrl, Alt ou Maj"; return true; }
            Result = VideoHotKey.Format(keyData);
            shown.Text = Result;
            ok.Enabled = Result.Length > 0;
            ok.Invalidate();
            return true;
        }
    }

    /// <summary>Bouton qui capture une combinaison de touches (« Ctrl+Alt+F11 ») ; Échap annule, Suppr / Retour efface.</summary>
    sealed class HotKeyBox : DarkControl
    {
        string value = "";
        bool listening;
        public event EventHandler ValueChanged;

        public HotKeyBox() { Height = Theme.S(32); Cursor = Cursors.Hand; SetStyle(ControlStyles.Selectable, true); TabStop = true; }

        public string Value { get { return value; } set { this.value = value ?? ""; Invalidate(); } }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            listening = true;
            Invalidate();
            // les raccourcis déjà actifs sont coupés pendant la saisie : sinon Windows les intercepte avant nous
            VideoRecorder.SuspendHotKeys();
            string result;
            try { using (var dlg = new HotKeyDialog()) result = dlg.ShowDialog(FindForm()) == DialogResult.OK ? dlg.Result : null; }
            finally { listening = false; Invalidate(); }
            if (result == null) { VideoRecorder.UpdateHotKey(); return; }
            value = result;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); else VideoRecorder.UpdateHotKey();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            Theme.FillRound(g, listening ? Theme.SurfaceHi : Hover ? Theme.SurfaceHi : Theme.Surface, r, Theme.S(6));
            Theme.DrawRound(g, listening ? Theme.Accent : Theme.Border, r, Theme.S(6));
            string text = listening ? "Appuyez sur la combinaison…" : value.Length == 0 ? "Aucun" : value;
            TextRenderer.DrawText(g, text, Font, new Rectangle(Theme.S(10), 0, Width - Theme.S(20), Height),
                listening || value.Length == 0 ? Theme.Muted : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }
}
