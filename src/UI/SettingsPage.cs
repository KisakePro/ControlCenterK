using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ControlCenterK
{
    class SettingsPage : Panel
    {
        readonly Engine engine;
        readonly AppConfig cfg;
        readonly Label title, lblMem, lblFolder;
        readonly Card genCard, midiCard, infoCard, lookCard, modCard, updCard;
        Label updStatus;
        FlatButton updInstall, updNotes, updCheck, updPick, btnFolderDefault;
        Label updPickLbl;
        DropButton updVersions;
        Card rowCard;   // carte en cours de remplissage par AddRow
        int y;

        // Apparence
        ThemeTiles tiles;
        SwatchGrid gridAccent, gridBase;
        FlatButton btnAccent, btnBase, btnSave;
        DropButton ddIntensity;
        Label lookTitle, capThemes, capAccent, capBase, capIntensity;

        public SettingsPage(Engine engine)
        {
            this.engine = engine;
            cfg = engine.Cfg;
            BackColor = Theme.Bg;
            AutoScroll = true;
            HandleCreated += (s, e) => Theme.DarkScroll(this);

            title = Theme.Label("Paramètres", Theme.Semi(18f), Theme.Text, BackColor);
            Controls.Add(title);

            updCard = new Card();
            Controls.Add(updCard);
            BuildUpdates();

            modCard = new Card();
            Controls.Add(modCard);
            BuildModules();

            lookCard = new Card();
            Controls.Add(lookCard);
            BuildLook();

            // --- Général ---
            genCard = BeginCard("Général");
            AddToggle("Démarrer avec Windows", "Lance l'application en arrière-plan à l'ouverture de session.",
                cfg.StartWithWindows, v => { cfg.StartWithWindows = v; Startup.Apply(v); });
            AddToggle("Démarrer réduit", "Au lancement, l'application reste dans la zone de notification sans ouvrir la fenêtre.",
                cfg.StartMinimized, v => cfg.StartMinimized = v);
            EndCard();

            // --- Module Contrôleur MIDI ---
            midiCard = BeginCard("Contrôleur MIDI", Glyphs.Mixer);
            AddToggle("Sélection automatique", "Toucher un contrôle physique le sélectionne dans l'éditeur.",
                cfg.AutoSelect, v => cfg.AutoSelect = v);
            AddToggle("Retour LED sur le contrôleur", "Allume les boutons selon l'état muet / périphérique par défaut. Nécessite « LED Mode : External » dans KORG Kontrol Editor.",
                cfg.LedFeedback, v => { cfg.LedFeedback = v; engine.OpenMidi(); });

            var curve = new DropButton();
            curve.Add("1", "Linéaire");
            curve.Add("1.5", "Douce");
            curve.Add("2", "Logarithmique");
            curve.Add("3", "Très progressive");
            curve.Value = cfg.Curve.ToString(CultureInfo.InvariantCulture);
            curve.ValueChanged += (s, e) =>
            {
                lock (AppConfig.Sync) cfg.Curve = double.Parse(curve.Value, CultureInfo.InvariantCulture);
                engine.ConfigChanged();
            };
            curve.Width = Theme.S(200);
            AddRow("Courbe de volume", "Plus la courbe est progressive, plus le bas de course du fader est précis.", curve);

            var jitter = new DropButton();
            jitter.Add("0", "Désactivé");
            jitter.Add("2", "Faible");
            jitter.Add("3", "Moyen (recommandé)");
            jitter.Add("5", "Fort");
            jitter.Add("8", "Très fort");
            jitter.Value = cfg.Jitter.ToString(CultureInfo.InvariantCulture);
            jitter.ValueChanged += (s, e) =>
            {
                lock (AppConfig.Sync) cfg.Jitter = int.Parse(jitter.Value, CultureInfo.InvariantCulture);
                cfg.Save();
            };
            jitter.Width = Theme.S(200);
            AddRow("Filtre anti-tremblement", "Ignore les micro-variations d'un fader au repos : seul un mouvement franc compte.", jitter);

            var reset = new FlatButton("Réinitialiser");
            reset.FitWidth();
            reset.Click += (s, e) =>
            {
                engine.ResetLearned();
                MessageBox.Show(FindForm(), "Les contrôles appris ont été remis sur le mapping d'usine du contrôleur.", "ControlCenterK");
            };
            AddRow("CC appris (MIDI learn)", "Remet tous les contrôles sur le mapping d'usine du modèle choisi.", reset);
            EndCard();

            // --- Configuration ---
            infoCard = BeginCard("Dossier de configuration", Glyphs.Folder);
            lblFolder = Theme.Label("", Theme.Ui(9f), Theme.Text, Theme.Card);
            lblFolder.Location = new Point(Theme.S(20), y);
            var openDir = new FlatButton("Ouvrir") { Glyph = Glyphs.Folder };
            openDir.FitWidth();
            openDir.Click += (s, e) => { try { Process.Start("explorer.exe", "\"" + AppConfig.Folder + "\""); } catch { } };
            var change = new FlatButton("Changer de dossier…");
            change.FitWidth();
            change.Click += (s, e) => ChangeFolder(false);
            btnFolderDefault = new FlatButton("Revenir au dossier par défaut");
            btnFolderDefault.FitWidth();
            btnFolderDefault.Click += (s, e) => ChangeFolder(true);
            lblMem = Theme.Label("", Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            infoCard.Controls.AddRange(new Control[] { lblFolder, openDir, change, btnFolderDefault, lblMem });
            infoCard.Layout += (s, e) =>
            {
                openDir.Location = new Point(Theme.S(20), lblFolder.Bottom + Theme.S(12));
                change.Location = new Point(openDir.Right + Theme.S(8), openDir.Top);
                btnFolderDefault.Location = new Point(change.Right + Theme.S(8), openDir.Top);
                lblMem.Location = new Point(Theme.S(20), openDir.Bottom + Theme.S(16));
                int h = lblMem.Bottom + Theme.S(18);
                if (infoCard.Height != h) infoCard.Height = h;
            };
            ShowFolder();

            Host.ModulesChanged += OnModulesChanged;
            Disposed += (s, e) => Host.ModulesChanged -= OnModulesChanged;
        }

        void OnModulesChanged()
        {
            try { if (IsHandleCreated) BeginInvoke(new Action(() => OnResize(EventArgs.Empty))); } catch { }
        }

        #region Dossier de configuration

        void ShowFolder()
        {
            lblFolder.Text = AppConfig.Folder + (AppConfig.CustomFolder ? "" : "   (par défaut : à côté de ControlCenterK.exe)");
            btnFolderDefault.Visible = AppConfig.CustomFolder;
            infoCard.PerformLayout();
        }

        void ChangeFolder(bool toDefault)
        {
            string dir = null;
            if (!toDefault)
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Dossier où ControlCenterK enregistre ses réglages (profils, routage, thèmes…)";
                    dlg.SelectedPath = AppConfig.Folder;
                    if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                    dir = dlg.SelectedPath;
                }
            }
            string target = dir ?? AppConfig.DefaultFolder;
            if (System.IO.File.Exists(System.IO.Path.Combine(target, "config.json")) &&
                MessageBox.Show(FindForm(), "Ce dossier contient déjà un fichier config.json.\nIl sera remplacé par vos réglages actuels. Continuer ?",
                    "Dossier de configuration", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            string err = cfg.ChangeFolder(dir);
            if (err != null) MessageBox.Show(FindForm(), err, "Dossier de configuration", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ShowFolder();
            OnResize(EventArgs.Empty);
        }

        #endregion

        #region Mises à jour

        void BuildUpdates()
        {
            var t = Theme.Label("Mises à jour", Theme.Semi(12f), Theme.Text, Theme.Card);
            t.Location = new Point(Theme.S(20), Theme.S(16));
            var v = Theme.Label("Version installée : " + AppVersion.Current + "   ·   github.com/" + AppVersion.Repo, Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            v.Location = new Point(Theme.S(20), Theme.S(42));
            v.Cursor = Cursors.Hand;
            v.Click += (s, e) => { try { Process.Start(AppVersion.RepoUrl); } catch { } };
            updStatus = Theme.Label("", Theme.Ui(9f), Theme.Text, Theme.Card);
            updStatus.Location = new Point(Theme.S(20), Theme.S(70));

            updCheck = new FlatButton("Vérifier maintenant") { Glyph = Glyphs.Refresh };
            updCheck.FitWidth();
            updCheck.Click += (s, e) => CheckNow();
            updInstall = new FlatButton("Installer", true) { Glyph = "\uE896", Visible = false };
            updInstall.Click += (s, e) => InstallUpdate(Updater.Available);
            updNotes = new FlatButton("Nouveautés") { Visible = false };
            updNotes.FitWidth();
            updNotes.Click += (s, e) => { var u = Updater.Available; if (u != null) try { Process.Start(u.PageUrl); } catch { } };

            // choix d'une version précise (ex. revenir à la précédente après une mauvaise mise à jour)
            updPickLbl = Theme.Label("Installer une version précise :", Theme.Ui(9f), Theme.Muted, Theme.Card);
            updPickLbl.Visible = false;
            updVersions = new DropButton { Width = Theme.S(260), Visible = false };
            updPick = new FlatButton("Installer cette version") { Glyph = "", Visible = false };
            updPick.FitWidth();
            updPick.Click += (s, e) =>
            {
                foreach (var r in Updater.Releases) if (r.Version == updVersions.Value) { InstallUpdate(r); return; }
            };
            updCard.Controls.AddRange(new Control[] { updPickLbl, updVersions, updPick });

            var auto = new Toggle { Checked = cfg.AutoUpdate, Tag = "right" };
            auto.CheckedChanged += (s, e) => { lock (AppConfig.Sync) cfg.AutoUpdate = auto.Checked; cfg.Save(); };
            auto.Top = Theme.S(18);
            var autoLbl = Theme.Label("Vérifier automatiquement", Theme.Ui(8.5f), Theme.Muted, Theme.Card);

            updCard.Controls.AddRange(new Control[] { t, v, updStatus, updCheck, updInstall, updNotes, auto, autoLbl });
            updCard.Height = Theme.S(150);
            updCard.Layout += (s, e) =>
            {
                autoLbl.Location = new Point(auto.Left - autoLbl.Width - Theme.S(8), auto.Top + (auto.Height - autoLbl.Height) / 2);
                int y = Math.Max(Theme.S(100), updStatus.Bottom + Theme.S(10));
                updCheck.Location = new Point(Theme.S(20), y);
                int x = updCheck.Right + Theme.S(8);
                if (updInstall.Visible) { updInstall.Location = new Point(x, y); x = updInstall.Right + Theme.S(8); }
                if (updNotes.Visible) updNotes.Location = new Point(x, y);
                int h = updCheck.Bottom + Theme.S(18);
                if (updVersions.Visible)
                {
                    int y2 = updCheck.Bottom + Theme.S(14);
                    updVersions.SetBounds(updPickLbl.Right + Theme.S(10), y2, Theme.S(260), updCheck.Height);
                    updPickLbl.Location = new Point(Theme.S(20), y2 + (updVersions.Height - updPickLbl.Height) / 2);
                    updVersions.Left = updPickLbl.Right + Theme.S(10);
                    updPick.Location = new Point(updVersions.Right + Theme.S(8), y2);
                    h = updVersions.Bottom + Theme.S(18);
                }
                if (updCard.Height != h) updCard.Height = h;
            };
            Updater.Changed += OnUpdateChanged;
            Disposed += (s, e) => Updater.Changed -= OnUpdateChanged;
            ShowUpdate(null);
            if (Updater.Releases.Count == 0) Updater.LoadReleasesAsync();
        }

        void OnUpdateChanged()
        {
            try { if (IsHandleCreated) BeginInvoke(new Action(() => ShowUpdate(null))); } catch { }
        }

        void ShowUpdate(string message)
        {
            var u = Updater.Available;
            if (u != null)
            {
                updStatus.Text = "Nouvelle version disponible : " + u.Version + (string.IsNullOrEmpty(u.Title) || u.Title == "v" + u.Version ? "" : " — " + u.Title);
                updStatus.ForeColor = Theme.Green;
                updInstall.Text = "Installer la version " + u.Version;
                updInstall.FitWidth();
            }
            else
            {
                string last;
                lock (AppConfig.Sync) last = cfg.LastUpdateCheck;
                DateTime d;
                updStatus.Text = message ?? (DateTime.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out d)
                    ? "À jour (dernière vérification : " + d.ToLocalTime().ToString("dd/MM/yyyy HH:mm") + ")" : "Pas encore vérifié.");
                updStatus.ForeColor = Theme.Muted;
            }
            updInstall.Visible = updNotes.Visible = u != null;
            var rels = Updater.Releases;
            string keep = updVersions.Value;
            updVersions.Items.Clear();
            foreach (var r in rels)
            {
                int cmp = Updater.Compare(r.Version, AppVersion.Current);
                updVersions.Add(r.Version, "Version " + r.Version + (cmp == 0 ? "  (installée)" : cmp > 0 ? "  (plus récente)" : "  (ancienne)"));
            }
            bool found = false;
            foreach (var r in rels) if (r.Version == keep) found = true;
            if (!found && rels.Count > 0)
            {
                // par défaut : la version précédant celle installée (cas d'un retour arrière)
                keep = rels[0].Version;
                foreach (var r in rels) if (Updater.Compare(r.Version, AppVersion.Current) < 0) { keep = r.Version; break; }
            }
            updVersions.Value = keep;
            updPickLbl.Visible = updVersions.Visible = updPick.Visible = rels.Count > 0;
            updCard.PerformLayout();
            OnResize(EventArgs.Empty);
        }

        void CheckNow()
        {
            updCheck.Enabled = false;
            updStatus.Text = "Vérification…";
            Updater.CheckAsync(cfg, true, (u, msg) =>
            {
                try { BeginInvoke(new Action(() => { updCheck.Enabled = true; ShowUpdate(u != null ? null : msg); })); } catch { }
            });
        }

        void InstallUpdate(UpdateInfo u)
        {
            if (u == null) return;
            int cmp = Updater.Compare(u.Version, AppVersion.Current);
            string what = cmp < 0 ? "Revenir à l'ancienne version " + u.Version + " ?\n\nLes réglages ajoutés par une version plus récente peuvent ne pas être repris."
                        : cmp == 0 ? "Réinstaller la version " + u.Version + " ?" : "Télécharger et installer la version " + u.Version + " ?";
            if (MessageBox.Show(FindForm(), what + "\n\nL'application va se fermer pendant l'installation ; vos réglages sont conservés.",
                    "Mise à jour", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            updInstall.Enabled = updCheck.Enabled = updPick.Enabled = false;
            new System.Threading.Thread(() =>
            {
                string err = Updater.DownloadAndInstall(u, p => { try { BeginInvoke(new Action(() => updStatus.Text = "Téléchargement… " + p + " %")); } catch { } });
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        updInstall.Enabled = updCheck.Enabled = updPick.Enabled = true;
                        if (err != null) { updStatus.Text = err; updStatus.ForeColor = Theme.Red; }
                        else updStatus.Text = "Installateur lancé : suivez ses instructions.";
                    }));
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        #endregion

        #region Modules

        void BuildModules()
        {
            var t = Theme.Label("Modules", Theme.Semi(12f), Theme.Text, Theme.Card);
            t.Location = new Point(Theme.S(20), Theme.S(16));
            var d = Theme.Label("Un module désactivé n'est pas chargé du tout : ni page, ni thread, ni mémoire audio.", Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            d.Location = new Point(Theme.S(20), Theme.S(42));
            modCard.Controls.AddRange(new Control[] { t, d });
            AddModule(Theme.S(70), Glyphs.Mixer, "Contrôleur MIDI", "Pilote le volume avec un contrôleur MIDI (nanoKONTROL2, X-Touch Mini, APC mini…).",
                cfg.ModMidi, v => cfg.ModMidi = v);
            AddModule(Theme.S(70) + Theme.S(66), Glyphs.Route, "Routage audio", "Console façon Voicemeeter : envoyer des entrées vers plusieurs sorties (casque + enceintes…).",
                cfg.ModRouter, v => cfg.ModRouter = v);
            AddModule(Theme.S(70) + 2 * Theme.S(66), Glyphs.Mouse, "Souris", "Réglages des souris Corsair (DPI, fréquence, éclairage) et boutons : touches, raccourcis, macros.",
                cfg.ModMouse, v => cfg.ModMouse = v);
            AddModule(Theme.S(70) + 3 * Theme.S(66), Glyphs.Keyboard, "Clavier", "Touches, macros et éclairage des claviers (en construction).",
                cfg.ModKeyboard, v => cfg.ModKeyboard = v);
            modCard.Height = Theme.S(70) + 4 * Theme.S(66) + Theme.S(8);
        }

        void AddModule(int y, string glyph, string label, string desc, bool value, Action<bool> apply)
        {
            var icon = Theme.Label(glyph, Theme.Icon(14f), Theme.Accent, Theme.Card);
            icon.Location = new Point(Theme.S(20), y + Theme.S(18));
            var l1 = Theme.Label(label, Theme.Semi(10f), Theme.Text, Theme.Card);
            l1.Location = new Point(Theme.S(56), y + Theme.S(12));
            var l2 = Theme.Label(desc, Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            l2.Location = new Point(Theme.S(56), y + Theme.S(36));
            var tg = new Toggle { Checked = value, Tag = "right" };
            tg.Top = y + (Theme.S(66) - tg.Height) / 2;
            tg.CheckedChanged += (s, e) =>
            {
                lock (AppConfig.Sync) apply(tg.Checked);
                cfg.Save();
                Host.ApplyModules();
            };
            modCard.Controls.AddRange(new Control[] { icon, l1, l2, tg });
        }

        #endregion

        #region Apparence

        void BuildLook()
        {
            lookTitle = Theme.Label("Apparence", Theme.Semi(12f), Theme.Text, Theme.Card);
            capThemes = Cap("THÈMES");
            capAccent = Cap("COULEUR D'ACCENT");
            capBase = Cap("TEINTE DU FOND");
            capIntensity = Cap("LUMINOSITÉ DU FOND");

            tiles = new ThemeTiles();
            tiles.Picked += t => ApplyTheme(t.Copy());
            tiles.DeleteClicked += t =>
            {
                lock (AppConfig.Sync) cfg.SavedThemes.Remove(t);
                cfg.Save();
                FillTiles();
                LayoutLook();
            };

            gridAccent = new SwatchGrid(SwatchGrid.AccentShades());
            gridAccent.Picked += c => ApplyTheme(With(c, null, null));
            gridBase = new SwatchGrid(SwatchGrid.BaseShades());
            gridBase.Picked += c => ApplyTheme(With(null, c, null));

            btnAccent = new FlatButton("Couleur personnalisée…") { Glyph = Glyphs.Palette };
            btnAccent.FitWidth();
            btnAccent.Click += (s, e) => { var c = PickColor(Theme.Accent); if (c.HasValue) ApplyTheme(With(c, null, null)); };
            btnBase = new FlatButton("Couleur personnalisée…") { Glyph = Glyphs.Palette };
            btnBase.FitWidth();
            btnBase.Click += (s, e) =>
            {
                Color cur;
                lock (AppConfig.Sync) cur = Theme.FromHex(cfg.Theme.Base, Color.Gray);
                var c = PickColor(cur);
                if (c.HasValue) ApplyTheme(With(null, c, null));
            };

            ddIntensity = new DropButton { Width = Theme.S(200) };
            ddIntensity.Add("0.75", "Très sombre");
            ddIntensity.Add("0.9", "Sombre");
            ddIntensity.Add("1", "Normal");
            ddIntensity.Add("1.25", "Doux");
            ddIntensity.Add("1.5", "Clair");
            ddIntensity.ValueChanged += (s, e) => ApplyTheme(With(null, null, double.Parse(ddIntensity.Value, CultureInfo.InvariantCulture)));

            btnSave = new FlatButton("Enregistrer ce thème…", true) { Glyph = Glyphs.Save };
            btnSave.FitWidth();
            btnSave.Click += (s, e) => SaveTheme();

            lookCard.Controls.AddRange(new Control[] { lookTitle, capThemes, tiles, capAccent, gridAccent, btnAccent,
                capBase, gridBase, btnBase, capIntensity, ddIntensity, btnSave });
            FillTiles();
            SyncLook();
        }

        static Label Cap(string text)
        {
            return Theme.Label(text, Theme.Ui(7.5f, FontStyle.Bold), Theme.Dim, Theme.Card);
        }

        void FillTiles()
        {
            tiles.Tiles.Clear();
            foreach (var p in Theme.Presets) tiles.Tiles.Add(new ThemeTiles.Tile { Def = p });
            lock (AppConfig.Sync)
                foreach (var t in cfg.SavedThemes) tiles.Tiles.Add(new ThemeTiles.Tile { Def = t, Deletable = true });
            tiles.Relayout();
        }

        /// <summary>Thème actuel avec une ou plusieurs valeurs remplacées.</summary>
        ThemeDef With(Color? accent, Color? baseColor, double? intensity)
        {
            lock (AppConfig.Sync)
            {
                var t = cfg.Theme.Copy("Personnalisé");
                if (accent.HasValue) t.Accent = Theme.ToHex(accent.Value);
                if (baseColor.HasValue) t.Base = Theme.ToHex(baseColor.Value);
                if (intensity.HasValue) t.Intensity = intensity.Value;
                return t;
            }
        }

        void ApplyTheme(ThemeDef t)
        {
            lock (AppConfig.Sync) cfg.Theme = t;
            cfg.Save();
            Theme.Apply(t);
            SyncLook();
        }

        void SyncLook()
        {
            ThemeDef t;
            lock (AppConfig.Sync) t = cfg.Theme;
            tiles.Current = t;
            gridAccent.Selected = Theme.FromHex(t.Accent, Color.Empty);
            gridBase.Selected = Theme.FromHex(t.Base, Color.Empty);
            ddIntensity.Value = t.Intensity.ToString(CultureInfo.InvariantCulture);
            tiles.Invalidate();
        }

        Color? PickColor(Color current)
        {
            using (var dlg = new ColorDialog { FullOpen = true, Color = current, AnyColor = true })
                return dlg.ShowDialog(FindForm()) == DialogResult.OK ? dlg.Color : (Color?)null;
        }

        void SaveTheme()
        {
            string name = InputBox.Show(FindForm(), "Enregistrer le thème", "Nom du thème :", "Mon thème");
            if (string.IsNullOrEmpty(name)) return;
            lock (AppConfig.Sync)
            {
                string n = name;
                for (int i = 2; cfg.SavedThemes.Exists(x => string.Equals(x.Name, n, StringComparison.CurrentCultureIgnoreCase))
                                || Array.Exists(Theme.Presets, x => string.Equals(x.Name, n, StringComparison.CurrentCultureIgnoreCase)); i++)
                    n = name + " (" + i + ")";
                cfg.Theme.Name = n;
                cfg.SavedThemes.Add(cfg.Theme.Copy(n));
            }
            cfg.Save();
            FillTiles();
            LayoutLook();
        }

        void LayoutLook()
        {
            int p = Theme.S(20), w = lookCard.Width, inner = w - 2 * p;
            lookTitle.Location = new Point(p, Theme.S(16));
            btnSave.Location = new Point(w - p - btnSave.Width, Theme.S(14));
            capThemes.Location = new Point(p, Theme.S(60));
            tiles.SetBounds(p, capThemes.Bottom + Theme.S(8), inner, tiles.Height);
            tiles.Relayout();
            int y0 = tiles.Bottom + Theme.S(22);

            int gw = gridAccent.Width;
            bool side = inner >= gw * 2 + Theme.S(48);
            int xa = p, xb = side ? p + gw + Theme.S(48) : p;
            capAccent.Location = new Point(xa, y0);
            gridAccent.Location = new Point(xa - Theme.S(3), capAccent.Bottom + Theme.S(6));
            btnAccent.Location = new Point(xa, gridAccent.Bottom + Theme.S(8));
            int yb = side ? y0 : btnAccent.Bottom + Theme.S(22);
            capBase.Location = new Point(xb, yb);
            gridBase.Location = new Point(xb - Theme.S(3), capBase.Bottom + Theme.S(6));
            btnBase.Location = new Point(xb, gridBase.Bottom + Theme.S(8));
            capIntensity.Location = new Point(xb, btnBase.Bottom + Theme.S(16));
            ddIntensity.Location = new Point(xb, capIntensity.Bottom + Theme.S(6));
            lookCard.Height = Math.Max(btnAccent.Bottom, ddIntensity.Bottom) + Theme.S(20);
        }

        #endregion

        void AddToggle(string label, string desc, bool value, Action<bool> apply)
        {
            var t = new Toggle { Checked = value };
            t.CheckedChanged += (s, e) =>
            {
                lock (AppConfig.Sync) apply(t.Checked);
                cfg.Save();
            };
            AddRow(label, desc, t);
        }

        /// <summary>Commence une carte de réglages avec un titre ; les AddRow suivants la remplissent.</summary>
        Card BeginCard(string text, string glyph = null)
        {
            var c = new Card();
            Controls.Add(c);
            int x = Theme.S(20);
            if (glyph != null)
            {
                var icon = Theme.Label(glyph, Theme.Icon(12f), Theme.Accent, Theme.Card);
                icon.Location = new Point(x, Theme.S(19));
                c.Controls.Add(icon);
                x = Theme.S(48);
            }
            var t = Theme.Label(text, Theme.Semi(12f), Theme.Text, Theme.Card);
            t.Location = new Point(x, Theme.S(16));
            c.Controls.Add(t);
            rowCard = c;
            y = Theme.S(52);
            return c;
        }

        void EndCard()
        {
            rowCard.Height = y + Theme.S(8);
        }

        void AddRow(string label, string desc, Control right)
        {
            int h = Theme.S(66);
            var card = rowCard;
            if (y > Theme.S(52))
            {
                var sep = new Panel { BackColor = Theme.Border, Height = 1, Tag = "sep" };
                sep.Location = new Point(Theme.S(20), y);
                card.Controls.Add(sep);
            }
            var l1 = Theme.Label(label, Theme.Semi(10f), Theme.Text, Theme.Card);
            l1.Location = new Point(Theme.S(20), y + Theme.S(12));
            var l2 = Theme.Label(desc, Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            l2.Location = new Point(Theme.S(20), y + Theme.S(36));
            right.Tag = "right";
            right.Top = y + (h - right.Height) / 2;
            card.Controls.AddRange(new Control[] { l1, l2, right });
            y += h;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (infoCard == null || lblMem == null) return;
            int pad = Theme.S(28), w = Math.Min(ClientSize.Width - 2 * pad, Theme.S(900));
            title.Location = new Point(pad - Theme.S(2), Theme.S(20) + AutoScrollPosition.Y);
            updCard.SetBounds(pad, Theme.S(80) + AutoScrollPosition.Y, w, updCard.Height);
            foreach (Control c in updCard.Controls) if ("right".Equals(c.Tag)) c.Left = w - Theme.S(20) - c.Width;
            updStatus.MaximumSize = new Size(w - Theme.S(40), 0);
            modCard.SetBounds(pad, updCard.Bottom + Theme.S(16), w, modCard.Height);
            foreach (Control c in modCard.Controls) if ("right".Equals(c.Tag)) c.Left = w - Theme.S(20) - c.Width;
            genCard.SetBounds(pad, modCard.Bottom + Theme.S(16), w, genCard.Height);
            lookCard.SetBounds(pad, genCard.Bottom + Theme.S(16), w, lookCard.Height);
            LayoutLook();
            int bottom = lookCard.Bottom;
            // cartes propres à un module : affichées seulement si le module est actif
            bool midi;
            lock (AppConfig.Sync) midi = cfg.ModMidi;
            midiCard.Visible = midi;
            if (midi) { midiCard.SetBounds(pad, bottom + Theme.S(16), w, midiCard.Height); bottom = midiCard.Bottom; }
            foreach (var card in new[] { genCard, midiCard })
                foreach (Control c in card.Controls)
                {
                    if ("right".Equals(c.Tag)) c.Left = w - Theme.S(20) - c.Width;
                    else if ("sep".Equals(c.Tag)) c.Width = w - Theme.S(40);
                }
            lblMem.MaximumSize = lblFolder.MaximumSize = new Size(w - Theme.S(40), 0);
            infoCard.SetBounds(pad, bottom + Theme.S(16), w, infoCard.Height);
        }

        public void RefreshInfo()
        {
            using (var p = Process.GetCurrentProcess())
                lblMem.Text = "Mémoire utilisée en ce moment : " + (p.WorkingSet64 / (1024 * 1024)) + " Mo   ·   Fenêtre fermée, l'application libère sa mémoire et ne consomme rien tant que vous ne touchez pas le contrôleur.";
        }
    }

    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "ControlCenterK";

        const string LegacyValueName = "MidiSoundController";

        /// <summary>Démarrage automatique actif ? Convertit au passage l'entrée enregistrée sous l'ancien nom.</summary>
        public static bool IsEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return false;
                    if (k.GetValue(LegacyValueName) != null)
                    {
                        k.DeleteValue(LegacyValueName, false);
                        k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --minimized");
                    }
                    return k.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void Apply(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --minimized");
                    else k.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }
    }
}
