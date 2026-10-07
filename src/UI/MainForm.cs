using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>
    /// Fenêtre principale. Elle est entièrement détruite à la fermeture : seule l'icône de notification
    /// et le moteur restent en mémoire.
    /// </summary>
    class MainForm : Form
    {
        readonly Engine engine;
        readonly Panel side, content;
        readonly NavButton navCtl, navRoute, navAudio, navMouse, navKeyboard, navSettings;
        readonly Label capProfile, capAudio, capDevices;
        readonly List<int> separators = new List<int>(); // lignes de séparation de la barre latérale
        bool blockAbove;                                  // mise en page : un bloc précède (séparation à tracer)
        // Pages créées à la demande : un module désactivé n'a pas de page en mémoire.
        ControllerPage pCtl;
        RouterPage pRoute;
        MousePage pMouse;
        KeyboardPage pKeyboard;
        AudioPage pAudio;
        SettingsPage pSettings;
        string current;
        readonly Label status;
        readonly DropButton ddProfile;
        readonly FlatButton btnProfiles;
        readonly Timer timer;
        volatile bool dirty;
        volatile string moved;

        public MainForm(Engine engine)
        {
            this.engine = engine;
            Text = "ControlCenterK";
            Icon = Program.AppIcon;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.S(1280), Theme.S(820));
            MinimumSize = new Size(Theme.S(1000), Theme.S(700));
            KeyPreview = true;

            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            side = new Panel { Dock = DockStyle.Left, Width = Theme.S(220), BackColor = Theme.Side };
            side.Paint += PaintSide;
            Controls.Add(content);
            Controls.Add(side);

            navCtl = new NavButton(Glyphs.Mixer, "Contrôleur");
            navRoute = new NavButton(Glyphs.Route, "Routage");
            navAudio = new NavButton(Glyphs.Speaker, "Périphériques audio");
            navMouse = new NavButton(Glyphs.Mouse, "Souris");
            navKeyboard = new NavButton(Glyphs.Keyboard, "Clavier");
            navSettings = new NavButton(Glyphs.Settings, "Paramètres");
            foreach (var n in new[] { navCtl, navRoute, navAudio, navMouse, navKeyboard, navSettings }) side.Controls.Add(n);
            capAudio = Theme.Label("AUDIO", Theme.Ui(7.5f, FontStyle.Bold), Theme.Dim, Theme.Side);
            capDevices = Theme.Label("PÉRIPHÉRIQUES", Theme.Ui(7.5f, FontStyle.Bold), Theme.Dim, Theme.Side);
            side.Controls.AddRange(new Control[] { capAudio, capDevices });
            navMouse.Click += (s, e) => ShowPage("mouse");
            navKeyboard.Click += (s, e) => ShowPage("keyboard");
            navCtl.Click += (s, e) => ShowPage("ctl");
            navRoute.Click += (s, e) => ShowPage("route");
            navAudio.Click += (s, e) => ShowPage("audio");
            navSettings.Click += (s, e) => ShowPage("settings");

            // --- Profils (mappings du contrôleur MIDI) ---
            capProfile = Theme.Label("PROFIL ACTIF", Theme.Ui(7.5f, FontStyle.Bold), Theme.Dim, Theme.Side);
            ddProfile = new DropButton();
            ddProfile.ValueChanged += (s, e) => engine.SwitchProfile(ddProfile.Value);
            btnProfiles = new FlatButton("") { Glyph = "" };
            btnProfiles.Click += (s, e) => ShowProfileMenu();
            side.Controls.AddRange(new Control[] { capProfile, ddProfile, btnProfiles });
            RefreshProfiles();

            status =Theme.Label("", Theme.Ui(8.5f), Theme.Muted, Theme.Side);
            status.AutoSize = false;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Paint += PaintStatusDot;
            status.Dock = DockStyle.Bottom;
            status.Height = Theme.S(56);
            status.Padding = new Padding(Theme.S(44), 0, Theme.S(8), Theme.S(8));
            side.Controls.Add(status);
            side.Resize += (s, e) => { if (capProfile != null) LayoutNav(); };

            engine.ControlMoved += OnControlMoved;
            engine.Learned += OnLearned;
            engine.StateChanged += OnStateChanged;
            engine.ProfileChanged += OnProfileChanged;
            Theme.Changed += OnThemeChanged;
            Host.ModulesChanged += OnModulesChanged;

            timer = new Timer { Interval = 33 };
            timer.Tick += (s, e) =>
            {
                if (!dirty) return;
                dirty = false;
                string m = moved;
                moved = null;
                if (pCtl != null && pCtl.Visible) pCtl.OnActivity(m);
            };
            timer.Start();
            LayoutNav();
            ShowPage(engine.Cfg.ModMidi ? "ctl" : engine.Cfg.ModRouter ? "route" : "audio");
            UpdateStatus();
            engine.RequestLedSync();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitle(this);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && pCtl != null) pCtl.CancelLearn();
            base.OnKeyDown(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            engine.ControlMoved -= OnControlMoved;
            engine.Learned -= OnLearned;
            engine.StateChanged -= OnStateChanged;
            engine.ProfileChanged -= OnProfileChanged;
            Theme.Changed -= OnThemeChanged;
            Host.ModulesChanged -= OnModulesChanged;
            engine.LearnControl = null;
            timer.Stop();
            timer.Dispose();
            base.OnFormClosed(e);
        }

        /// <summary>Place les boutons de navigation selon les modules actifs.</summary>
        void LayoutNav()
        {
            var cfg = engine.Cfg;
            separators.Clear();
            blockAbove = false;
            int y = Theme.S(84);
            // Profil actif : juste sous le nom de l'application
            capProfile.Visible = ddProfile.Visible = btnProfiles.Visible = cfg.ModMidi;
            if (cfg.ModMidi)
            {
                capProfile.Location = new Point(Theme.S(24), y);
                ddProfile.SetBounds(Theme.S(18), capProfile.Bottom + Theme.S(6), side.Width - Theme.S(18) - Theme.S(56), Theme.S(34));
                btnProfiles.SetBounds(ddProfile.Right + Theme.S(6), ddProfile.Top, Theme.S(34), Theme.S(34));
                y = ddProfile.Bottom + Theme.S(8);
                blockAbove = true;
            }
            y = NavSection(capAudio, y, new[] { navCtl, navRoute, navAudio }, new[] { cfg.ModMidi, cfg.ModRouter, true });
            y = NavSection(capDevices, y, new[] { navMouse, navKeyboard }, new[] { cfg.ModMouse, cfg.ModKeyboard });
            // Paramètres : en bas à gauche, juste au-dessus de l'équipement connecté
            navSettings.SetBounds(0, side.ClientSize.Height - Theme.S(56) - navSettings.Height - Theme.S(4), side.Width, navSettings.Height);
            separators.Add(navSettings.Top - Theme.S(8));
            side.Invalidate();
        }

        /// <summary>Place une section de la barre latérale (titre + boutons des modules actifs) et renvoie le Y suivant.</summary>
        int NavSection(Label cap, int y, NavButton[] navs, bool[] on)
        {
            bool any = false;
            for (int i = 0; i < navs.Length; i++) { navs[i].Visible = on[i]; any |= on[i]; }
            cap.Visible = any;
            if (!any) return y;
            if (blockAbove)
            {
                separators.Add(y + Theme.S(6));
                y += Theme.S(18);
            }
            blockAbove = true;
            cap.Location = new Point(Theme.S(24), y);
            y = cap.Bottom + Theme.S(6);
            for (int i = 0; i < navs.Length; i++)
            {
                if (!on[i]) continue;
                navs[i].SetBounds(0, y, side.Width, navs[i].Height);
                y += navs[i].Height + Theme.S(2);
            }
            return y + Theme.S(4);
        }

        public void OpenSettings() { ShowPage("settings"); }

        void ShowPage(string key)
        {
            if (pCtl != null) pCtl.CancelLearn();
            Control page;
            switch (key)
            {
                case "ctl": page = pCtl ?? (pCtl = Add(new ControllerPage(engine))); break;
                case "route": page = pRoute ?? (pRoute = Add(new RouterPage(engine))); break;
                case "audio": page = pAudio ?? (pAudio = Add(new AudioPage(engine))); break;
                case "mouse": page = pMouse ?? (pMouse = Add(new MousePage(engine.Cfg))); break;
                case "keyboard": page = pKeyboard ?? (pKeyboard = Add(new KeyboardPage())); break;
                default: key = "settings"; page = pSettings ?? (pSettings = Add(new SettingsPage(engine))); break;
            }
            current = key;
            foreach (Control c in content.Controls) c.Visible = c == page;
            navCtl.Selected = key == "ctl";
            navRoute.Selected = key == "route";
            navAudio.Selected = key == "audio";
            navMouse.Selected = key == "mouse";
            navKeyboard.Selected = key == "keyboard";
            navSettings.Selected = key == "settings";
            if (page == pAudio) pAudio.Reload();
            if (page == pSettings) pSettings.RefreshInfo();
            if (page == pCtl) pCtl.RefreshAll();
            if (page == pRoute) pRoute.Rebuild();
        }

        T Add<T>(T page) where T : Control
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            content.Controls.Add(page);
            return page;
        }

        void OnModulesChanged()
        {
            Ui(() =>
            {
                var cfg = engine.Cfg;
                if (!cfg.ModMidi && pCtl != null) { content.Controls.Remove(pCtl); pCtl.Dispose(); pCtl = null; }
                if (!cfg.ModRouter && pRoute != null) { content.Controls.Remove(pRoute); pRoute.Dispose(); pRoute = null; }
                if (!cfg.ModMouse && pMouse != null) { content.Controls.Remove(pMouse); pMouse.Dispose(); pMouse = null; }
                if (!cfg.ModKeyboard && pKeyboard != null) { content.Controls.Remove(pKeyboard); pKeyboard.Dispose(); pKeyboard = null; }
                LayoutNav();
                if ((current == "ctl" && !cfg.ModMidi) || (current == "route" && !cfg.ModRouter) || (current == "mouse" && !cfg.ModMouse)
                    || (current == "keyboard" && !cfg.ModKeyboard)) ShowPage("settings");
                UpdateStatus();
                side.Invalidate();
            });
        }

        // Ces trois callbacks arrivent depuis le thread du moteur.
        void OnControlMoved(string id)
        {
            if (id != null) moved = id;
            dirty = true;
        }

        void OnLearned(string id)
        {
            Ui(() => { if (pCtl != null) pCtl.OnLearned(id); });
        }

        void OnStateChanged()
        {
            Ui(() => { UpdateStatus(); if (pCtl != null) pCtl.RefreshMidiLists(); });
        }

        void OnThemeChanged()
        {
            Icon = Program.AppIcon;
        }

        void OnProfileChanged()
        {
            Ui(() => { RefreshProfiles(); if (pCtl != null) pCtl.RefreshAll(); });
        }

        #region Profils

        AppConfig Cfg { get { return engine.Cfg; } }

        void RefreshProfiles()
        {
            ddProfile.Items.Clear();
            lock (AppConfig.Sync)
            {
                foreach (var p in Cfg.Profiles) ddProfile.Add(p.Name, p.Name);
                ddProfile.Value = Cfg.ActiveProfile;
            }
        }

        void ShowProfileMenu()
        {
            var m = DarkMenu.Create();
            m.Items.Add(DarkMenu.Item("Nouveau profil…", Glyphs.Add, NewProfile));
            m.Items.Add(DarkMenu.Item("Dupliquer ce profil…", "", DuplicateProfile));
            m.Items.Add(DarkMenu.Item("Renommer…", "", RenameProfile));
            var del = DarkMenu.Item("Supprimer", "", DeleteProfile);
            lock (AppConfig.Sync) del.Enabled = Cfg.Profiles.Count > 1;
            m.Items.Add(del);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(DarkMenu.Item("Exporter ce profil…", "", ExportProfile));
            m.Items.Add(DarkMenu.Item("Importer un profil…", "", ImportProfile));
            DarkMenu.Show(m, btnProfiles, new Point(0, btnProfiles.Height + Theme.S(2)));
        }

        void AddProfile(Profile p)
        {
            lock (AppConfig.Sync)
            {
                p.Name = Cfg.UniqueName(p.Name);
                Cfg.Profiles.Add(p);
            }
            engine.SwitchProfile(p.Name);
        }

        void NewProfile()
        {
            string name = InputBox.Show(this, "Nouveau profil", "Nom du profil (il démarre vide) :");
            if (string.IsNullOrEmpty(name)) return;
            AddProfile(new Profile { Name = name });
        }

        void DuplicateProfile()
        {
            string current;
            lock (AppConfig.Sync) current = Cfg.ActiveProfile;
            string name = InputBox.Show(this, "Dupliquer le profil", "Nom de la copie :", current + " (copie)");
            if (string.IsNullOrEmpty(name)) return;
            Dictionary<string, ControlMapping> copy;
            lock (AppConfig.Sync) copy = AppConfig.CloneControls(Cfg.Controls);
            AddProfile(new Profile { Name = name, Controls = copy });
        }

        void RenameProfile()
        {
            string current;
            lock (AppConfig.Sync) current = Cfg.ActiveProfile;
            string name = InputBox.Show(this, "Renommer le profil", "Nouveau nom :", current);
            if (string.IsNullOrEmpty(name)) return;
            lock (AppConfig.Sync)
            {
                var p = Cfg.FindProfile(current);
                if (p == null) return;
                p.Name = Cfg.UniqueName(name, p);
                Cfg.ActiveProfile = p.Name;
            }
            Cfg.Save();
            RefreshProfiles();
        }

        void DeleteProfile()
        {
            string current;
            lock (AppConfig.Sync)
            {
                if (Cfg.Profiles.Count < 2) return;
                current = Cfg.ActiveProfile;
            }
            if (MessageBox.Show(this, "Supprimer le profil « " + current + " » ?\nCette action est définitive (pensez à l'exporter avant).",
                    "Supprimer le profil", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            string next;
            lock (AppConfig.Sync)
            {
                Cfg.Profiles.Remove(Cfg.FindProfile(current));
                next = Cfg.Profiles[0].Name;
            }
            engine.SwitchProfile(next);
        }

        void ExportProfile()
        {
            string json, name;
            lock (AppConfig.Sync)
            {
                var p = Cfg.FindProfile(Cfg.ActiveProfile);
                name = p.Name;
                json = AppConfig.ExportProfile(p);
            }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Exporter le profil";
                dlg.Filter = "Profil ControlCenterK (*.json)|*.json";
                dlg.FileName = SafeFileName(name) + ".json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { System.IO.File.WriteAllText(dlg.FileName, json, new System.Text.UTF8Encoding(false)); }
                catch (Exception ex) { MessageBox.Show(this, "Impossible d'écrire le fichier :\n" + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void ImportProfile()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Importer un profil";
                dlg.Filter = "Profil ControlCenterK (*.json)|*.json|Tous les fichiers (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Profile p;
                try { p = AppConfig.ImportProfile(System.IO.File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8)); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Impossible d'importer ce fichier :\n" + ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                int remapped = RemapDevices(p);
                AddProfile(p);
                MessageBox.Show(this, "Profil « " + p.Name + " » importé et activé." +
                    (remapped > 0 ? "\n" + remapped + " périphérique(s) retrouvé(s) par leur nom sur ce PC." : ""),
                    "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Les identifiants de périphériques changent d'un PC à l'autre : si un périphérique du profil importé
        /// n'existe pas ici, on cherche un périphérique portant le même nom.
        /// </summary>
        int RemapDevices(Profile p)
        {
            var devices = new List<DeviceInfo>();
            devices.AddRange(engine.Query(a => a.ListDevices(Flow.Render)) ?? new List<DeviceInfo>());
            devices.AddRange(engine.Query(a => a.ListDevices(Flow.Capture)) ?? new List<DeviceInfo>());
            int count = 0;
            foreach (var m in p.Controls.Values)
                foreach (var t in m.Targets)
                {
                    if (t.Type != "device" || devices.Exists(d => d.Id == t.Id)) continue;
                    bool capture = Names.IsCaptureId(t.Id);
                    var match = devices.Find(d => d.Name == t.Name && (d.Flow == Flow.Capture) == capture);
                    if (match != null) { t.Id = match.Id; count++; }
                }
            return count;
        }

        static string SafeFileName(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        #endregion

        void Ui(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        void UpdateStatus()
        {
            string n = engine.MidiInName;
            if (!engine.Cfg.ModMidi)
            {
                var r = Host.Router;
                status.Text = r != null ? "Routage audio actif" : "Modules désactivés";
                status.Tag = r != null;
            }
            else
            {
                status.Text = n != null ? n + " connecté" : "Aucun contrôleur MIDI";
                status.Tag = n != null;
            }
            status.Invalidate();
        }

        void PaintStatusDot(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            bool ok = status.Tag is bool && (bool)status.Tag;
            int d = Theme.S(8);
            using (var b = new SolidBrush(ok ? Theme.Green : Theme.Red))
                e.Graphics.FillEllipse(b, Theme.S(26), (status.Height - Theme.S(8) - d) / 2, d, d);
        }

        void PaintSide(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var logo = new RectangleF(Theme.S(22), Theme.S(26), Theme.S(38), Theme.S(38));
            Theme.FillRound(g, Theme.Accent, logo, Theme.S(9));
            TextRenderer.DrawText(g, Glyphs.Mixer, Theme.Icon(14f), Rectangle.Round(logo), Theme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "ControlCenterK", Theme.Semi(11.5f), new Rectangle(Theme.S(70), Theme.S(26), side.Width - Theme.S(72), Theme.S(38)), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            using (var pen = new Pen(Theme.Border))
                foreach (int y in separators) g.DrawLine(pen, Theme.S(20), y, side.Width - Theme.S(20), y);
        }
    }
}
