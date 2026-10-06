using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Page principale : le contrôleur dessiné + l'éditeur du contrôle sélectionné.</summary>
    class ControllerPage : Panel
    {
        readonly Engine engine;
        readonly AppConfig cfg;
        readonly ControllerView view;
        readonly Card editor;
        readonly Label title, sub, capIn, capOut, lblName, lblInfo, lblActionCap, lblTargetsCap, lblHint;
        readonly DropButton ddIn, ddOut, ddAction;
        readonly FlatButton btnLearn;
        readonly ChipsBox chips;
        string sel;
        readonly Label capModel;
        readonly DropButton ddModel;
        readonly FlatButton btnWizard;
        List<string> wizard;   // assistant « Tout apprendre » : contrôles restant à apprendre
        int wizardTotal;
        bool showAction, showChips;

        public ControllerPage(Engine engine)
        {
            this.engine = engine;
            cfg = engine.Cfg;
            BackColor = Theme.Bg;
            DoubleBuffered = true;

            title = Theme.Label("Contrôleur", Theme.Semi(18f), Theme.Text, BackColor);
            sub = Theme.Label("Clic gauche : manipuler un fader, un potentiomètre ou un bouton.  Clic droit (ou toucher le contrôle physique) : le configurer.", Theme.Ui(9.5f), Theme.Muted, BackColor);
            capModel = Theme.Label("MODÈLE", Theme.Ui(7.5f, FontStyle.Bold), Theme.Dim, BackColor);
            ddModel = new DropButton();
            foreach (var mdl in Controllers.All) ddModel.Add(mdl.Id, mdl.Name);
            ddModel.Value = Controllers.Current.Id;
            ddModel.ValueChanged += (s, e) => ChangeModel(ddModel.Value);
            capIn = Theme.Label("ENTRÉE MIDI", Theme.Ui(7.5f, FontStyle.Bold), Theme.Dim, BackColor);
            capOut = Theme.Label("SORTIE MIDI (LED)", Theme.Ui(7.5f, FontStyle.Bold), Theme.Dim, BackColor);
            ddIn = new DropButton();
            ddOut = new DropButton();
            ddIn.ValueChanged += (s, e) => { lock (AppConfig.Sync) cfg.MidiIn = ddIn.Value; cfg.Save(); engine.OpenMidi(); };
            ddOut.ValueChanged += (s, e) => { lock (AppConfig.Sync) cfg.MidiOut = ddOut.Value; cfg.Save(); engine.OpenMidi(); };

            view = new ControllerView(engine) { StripLabel = StripLabel, IsAssigned = IsAssigned };
            view.ControlClicked += id => { engine.LearnControl = null; SelectControl(id); };

            editor = new Card();
            lblName = Theme.Label("", Theme.Semi(15f), Theme.Text, Theme.Card);
            lblInfo = Theme.Label("", Theme.Ui(9f), Theme.Muted, Theme.Card);
            lblActionCap = Theme.Label("ACTION DU BOUTON", Theme.Ui(7.5f, FontStyle.Bold), Theme.Dim, Theme.Card);
            lblTargetsCap = Theme.Label("CIBLES", Theme.Ui(7.5f, FontStyle.Bold), Theme.Dim, Theme.Card);
            lblHint = Theme.Label("", Theme.Ui(9f), Theme.Muted, Theme.Card);
            btnWizard = new FlatButton("Tout apprendre") { Glyph = Glyphs.Learn };
            btnWizard.Click += (s, e) => { if (wizard == null) StartWizard(); else StopWizard(); };
            btnLearn = new FlatButton("MIDI learn") { Glyph = Glyphs.Plug };
            btnLearn.Click += (s, e) => ToggleLearn();
            ddAction = new DropButton();
            ddAction.ValueChanged += (s, e) => OnActionChanged();
            chips = new ChipsBox();
            chips.RemoveClicked += OnRemoveTarget;
            chips.AddClicked += OnAddTarget;
            editor.Controls.AddRange(new Control[] { lblName, lblInfo, btnLearn, btnWizard, lblActionCap, ddAction, lblTargetsCap, chips, lblHint });

            Controls.AddRange(new Control[] { ddModel, ddIn, ddOut, capModel, capIn, capOut, title, sub, view, editor });
            RefreshMidiLists();
            SelectControl(Controllers.Current.FirstFader.Id);
        }

        #region Mise en page

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DoLayout();
        }

        void DoLayout()
        {
            if (editor == null) return;
            int pad = Theme.S(28), w = ClientSize.Width, h = ClientSize.Height;
            title.Location = new Point(pad - Theme.S(2), Theme.S(20));
            int ddW = Theme.S(210), mdW = Theme.S(250);
            sub.MaximumSize = new Size(Math.Max(Theme.S(200), w - 2 * pad - 2 * ddW - mdW - Theme.S(50)), 0);
            sub.Location = new Point(pad, title.Bottom + Theme.S(2));
            ddOut.SetBounds(w - pad - ddW, Theme.S(42), ddW, Theme.S(32));
            capOut.Location = new Point(ddOut.Left + Theme.S(2), Theme.S(22));
            ddIn.SetBounds(ddOut.Left - Theme.S(12) - ddW, Theme.S(42), ddW, Theme.S(32));
            capIn.Location = new Point(ddIn.Left + Theme.S(2), Theme.S(22));
            ddModel.SetBounds(ddIn.Left - Theme.S(12) - mdW, Theme.S(42), mdW, Theme.S(32));
            capModel.Location = new Point(ddModel.Left + Theme.S(2), Theme.S(22));

            int top = Math.Max(Theme.S(100), sub.Bottom + Theme.S(12));
            int vw = w - 2 * pad;
            int vh = (int)Math.Min(vw * Controllers.Current.H / Controllers.Current.W, Math.Max(Theme.S(170), h - top - Theme.S(300)));
            view.SetBounds(pad, top, vw, vh);
            int ey = view.Bottom + Theme.S(18);
            editor.SetBounds(pad, ey, vw, Math.Max(Theme.S(160), h - ey - pad));
            LayoutEditor();
        }

        void LayoutEditor()
        {
            int p = Theme.S(22), w = editor.Width;
            lblName.Location = new Point(p, Theme.S(16));
            lblInfo.Location = new Point(p + Theme.S(1), lblName.Bottom + Theme.S(2));
            btnLearn.FitWidth();
            btnLearn.Location = new Point(w - p - btnLearn.Width, Theme.S(22));
            btnWizard.FitWidth();
            btnWizard.Location = new Point(btnLearn.Left - Theme.S(8) - btnWizard.Width, Theme.S(22));
            int y = lblInfo.Bottom + Theme.S(18);
            ddAction.Visible = lblActionCap.Visible = showAction;
            if (showAction)
            {
                lblActionCap.Location = new Point(p, y);
                ddAction.SetBounds(p, lblActionCap.Bottom + Theme.S(6), Math.Min(Theme.S(420), w - 2 * p), Theme.S(34));
                y = ddAction.Bottom + Theme.S(18);
            }
            chips.Visible = lblTargetsCap.Visible = showChips;
            if (showChips)
            {
                lblTargetsCap.Location = new Point(p, y);
                chips.SetBounds(p, lblTargetsCap.Bottom + Theme.S(6), w - 2 * p, chips.Height);
                chips.Relayout();
                y = chips.Bottom + Theme.S(14);
            }
            lblHint.MaximumSize = new Size(w - 2 * p, 0);
            lblHint.Location = new Point(p, y);
        }

        #endregion

        #region Sélection / éditeur

        ControlMapping Mapping(string id)
        {
            lock (AppConfig.Sync) return cfg.Get(id);
        }

        string StripLabel(string id)
        {
            lock (AppConfig.Sync)
            {
                ControlMapping m;
                if (id == null || !cfg.Controls.TryGetValue(id, out m) || m.Targets.Count == 0) return null;
                string s = Names.Display(cfg, m.Targets[0]);
                return m.Targets.Count > 1 ? s + " +" + (m.Targets.Count - 1) : s;
            }
        }

        bool IsAssigned(string id)
        {
            lock (AppConfig.Sync)
            {
                ControlMapping m;
                return cfg.Controls.TryGetValue(id, out m) && (m.Targets.Count > 0 || m.Action != "");
            }
        }

        public void SelectControl(string id)
        {
            var def = Controllers.Get(id);
            if (def == null) return;
            sel = id;
            view.Selected = id;
            var m = Mapping(id);
            bool isBtn = def.Kind == ControlKind.Button;
            string action = m.Action ?? "";

            lblName.Text = def.Label;
            int key = engine.KeyOf(id);
            string kind = def.Kind == ControlKind.Fader ? "Fader" : def.Kind == ControlKind.Knob ? "Potentiomètre" : "Bouton";
            lblInfo.Text = kind + "  ·  " + MidiKey.Describe(key);
            if (wizard != null) lblInfo.Text = "Touchez ce contrôle sur votre appareil  (" + (wizardTotal - wizard.Count + 1) + " / " + wizardTotal + ")  ·  Échap pour arrêter";

            showAction = isBtn;
            if (isBtn)
            {
                ddAction.Items.Clear();
                ddAction.Add("", "Aucune action");
                ddAction.Add("mute", "Muet : basculer les cibles ci-dessous");
                var linked = Controllers.Get(def.Fader);
                if (linked != null && linked.Id != def.Id) ddAction.Add("mutestrip", "Muet : basculer le " + linked.Label);
                ddAction.Add("default", "Définir le périphérique par défaut");
                ddAction.Add("media_play", "Média : Lecture / Pause");
                ddAction.Add("media_next", "Média : Piste suivante");
                ddAction.Add("media_prev", "Média : Piste précédente");
                ddAction.Add("media_stop", "Média : Stop");
                ddAction.Add("media_mute", "Touche Muet de Windows");
                ddAction.Add("profile_next", "Profil suivant");
                ddAction.Add("profile_prev", "Profil précédent");
                ddAction.Value = action;
            }

            showChips = !isBtn || action == "mute" || action == "default";
            chips.AddText = action == "default" ? "Choisir le périphérique" : "Ajouter une cible";
            chips.Chips.Clear();
            lock (AppConfig.Sync)
                foreach (var t in m.Targets)
                    chips.Chips.Add(new ChipsBox.Chip { Glyph = Names.Glyph(t), Text = Names.Display(cfg, t) });

            lblHint.Text = Hint(def, action) + (Controllers.Current.Note != null ? "\n" + Controllers.Current.Name + " : " + Controllers.Current.Note : "");
            UpdateLearnButton();
            LayoutEditor();
            chips.Relayout();
            view.Invalidate();
        }

        string Hint(ControlDef def, string action)
        {
            if (def.Kind != ControlKind.Button)
                return "Le volume de toutes les cibles suit ce contrôle. Ajoutez plusieurs cibles pour les piloter ensemble.";
            switch (action)
            {
                case "mute": return "Chaque appui coupe / rétablit le son des cibles. La LED du bouton s'allume quand elles sont muettes.";
                case "mutestrip":
                    {
                        string f = StripLabel(def.Fader);
                        var linked = Controllers.Get(def.Fader);
                        return "Chaque appui coupe / rétablit le son des cibles du " + (linked != null ? linked.Label : "fader") +
                               (f != null ? " (" + f + ")." : " — aucune cible assignée à ce fader pour l'instant.");
                    }
                case "default": return "Un appui définit ce périphérique comme périphérique par défaut de Windows. La LED indique celui qui est actif.";
                case "": return "Choisissez ce que fait ce bouton.";
                case "profile_next":
                case "profile_prev": return "Passe au profil suivant / précédent. Pensez à mettre ce bouton dans chacun de vos profils.";
                default: return "Simule la touche multimédia correspondante du clavier.";
            }
        }

        void OnActionChanged()
        {
            if (sel == null) return;
            lock (AppConfig.Sync)
            {
                var m = cfg.Get(sel);
                m.Action = ddAction.Value;
                if (m.Action == "default")
                {
                    var dev = m.Targets.Find(t => t.Type == "device");
                    m.Targets.Clear();
                    if (dev != null) m.Targets.Add(dev);
                }
            }
            engine.ConfigChanged();
            SelectControl(sel);
        }

        void OnRemoveTarget(int index)
        {
            lock (AppConfig.Sync)
            {
                var m = cfg.Get(sel);
                if (index >= 0 && index < m.Targets.Count) m.Targets.RemoveAt(index);
            }
            engine.ConfigChanged();
            SelectControl(sel);
        }

        void ToggleTarget(Target t, bool single)
        {
            lock (AppConfig.Sync)
            {
                var m = cfg.Get(sel);
                int i = m.Targets.FindIndex(x => x.Same(t));
                if (i >= 0) m.Targets.RemoveAt(i);
                else
                {
                    if (single) m.Targets.Clear();
                    m.Targets.Add(t);
                }
            }
            engine.ConfigChanged();
            SelectControl(sel);
        }

        void OnAddTarget(Rectangle r)
        {
            if (sel == null) return;
            string id = sel;
            List<Target> current;
            bool devOnly;
            lock (AppConfig.Sync)
            {
                var m = cfg.Get(id);
                current = new List<Target>(m.Targets);
                devOnly = m.Action == "default";
            }

            Cursor = Cursors.WaitCursor;
            var outs = engine.Query(a => a.ListDevices(Flow.Render)) ?? new List<DeviceInfo>();
            var ins = engine.Query(a => a.ListDevices(Flow.Capture)) ?? new List<DeviceInfo>();
            var apps = devOnly ? new List<Catalog.App>() : Catalog.Apps(engine, true);
            Cursor = Cursors.Default;

            var menu = DarkMenu.Create();
            ToolStripItemCollection into = menu.Items;
            Action<Target, string> add = (t, glyph) =>
            {
                bool on = current.Exists(x => x.Same(t));
                var it = new ToolStripMenuItem(Names.Display(cfg, t), Theme.GlyphImage(glyph, on ? Theme.OnAccent : Theme.Muted));
                it.Checked = on;
                it.Click += (s, e) => ToggleTarget(t, devOnly);
                into.Add(it);
            };

            if (!devOnly)
            {
                menu.Items.Add(DarkMenu.Header("Spécial"));
                add(new Target { Type = "master" }, Glyphs.Volume);
                add(new Target { Type = "mic" }, Glyphs.Mic);
                add(new Target { Type = "focus" }, Glyphs.Focus);
                add(new Target { Type = "system" }, Glyphs.System);
                add(new Target { Type = "unassigned" }, Glyphs.Apps);
                menu.Items.Add(new ToolStripSeparator());
            }

            lock (AppConfig.Sync)
            {
                if (Host.Router != null && !devOnly && (cfg.Router.Inputs.Count + cfg.Router.Outputs.Count) > 0)
                {
                    into = DarkMenu.Sub(menu, "Routage audio", Glyphs.Route);
                    into.Add(DarkMenu.Header("Entrées"));
                    foreach (var n in cfg.Router.Inputs) add(new Target { Type = "route_in", Id = n.Id, Name = n.Name }, Glyphs.Route);
                    into.Add(DarkMenu.Header("Sorties"));
                    foreach (var n in cfg.Router.Outputs) add(new Target { Type = "route_out", Id = n.Id, Name = n.Name }, Glyphs.Route);
                }
                into = DarkMenu.Sub(menu, "Sorties audio", Glyphs.Speaker);
                AddDevices(into, add, outs, Glyphs.Speaker);
                into = DarkMenu.Sub(menu, "Entrées audio", Glyphs.Mic);
                AddDevices(into, add, ins, Glyphs.Mic);
                if (!devOnly)
                {
                    into = DarkMenu.Sub(menu, "Applications", Glyphs.App);
                    foreach (var a in apps)
                        if (!cfg.IsHidden("app:" + a.Proc))
                            add(new Target { Type = "app", Id = a.Proc, Name = a.Name }, Glyphs.App);
                }
            }
            if (!devOnly)
            {
                var other = new ToolStripMenuItem("Autre application (nom de l'exécutable)…", Theme.GlyphImage(Glyphs.Add, Theme.Accent));
                other.Click += (s, e) =>
                {
                    string exe = InputBox.Show(FindForm(), "Ajouter une application", "Nom de l'exécutable (ex : spotify.exe) :");
                    if (string.IsNullOrEmpty(exe)) return;
                    string proc = Native.ExeName(exe);
                    if (!string.IsNullOrEmpty(proc)) ToggleTarget(new Target { Type = "app", Id = proc, Name = Catalog.Describe(proc, null) }, false);
                };
                if (into.Count > 0) into.Add(new ToolStripSeparator());
                into.Add(other);
            }
            menu.MaximumSize = new Size(Theme.S(560), Screen.FromControl(this).WorkingArea.Height - Theme.S(40));
            DarkMenu.Show(menu, chips, new Point(r.Left, r.Bottom + Theme.S(4)));
        }

        void AddDevices(ToolStripItemCollection into, Action<Target, string> add, List<DeviceInfo> devices, string glyph)
        {
            var visible = devices.FindAll(d => !cfg.IsHidden("dev:" + d.Id));
            visible.Sort((x, y) => string.Compare(Names.Display(cfg, new Target { Type = "device", Id = x.Id, Name = x.Name }),
                                                  Names.Display(cfg, new Target { Type = "device", Id = y.Id, Name = y.Name }),
                                                  StringComparison.CurrentCultureIgnoreCase));
            foreach (var d in visible) add(new Target { Type = "device", Id = d.Id, Name = d.Name }, glyph);
            if (visible.Count == 0) into.Add(new ToolStripMenuItem("(tout est masqué — voir la page Périphériques audio)") { Enabled = false });
        }

        #endregion

        #region MIDI learn / MIDI

        void ToggleLearn()
        {
            if (wizard != null) { NextWizard(); return; } // « Passer » pendant l'assistant
            engine.LearnControl = engine.LearnControl == null ? sel : null;
            UpdateLearnButton();
        }

        #region Assistant « Tout apprendre »

        void StartWizard()
        {
            wizard = new List<string>();
            foreach (var d in Controllers.Current.Controls) wizard.Add(d.Id);
            wizardTotal = wizard.Count;
            NextWizard(false);
        }

        void NextWizard(bool skip = true)
        {
            if (skip && wizard.Count > 0) wizard.RemoveAt(0);
            if (wizard.Count == 0) { StopWizard(); return; }
            engine.LearnControl = wizard[0];
            SelectControl(wizard[0]);
            UpdateLearnButton();
        }

        void StopWizard()
        {
            wizard = null;
            engine.LearnControl = null;
            UpdateLearnButton();
            if (sel != null) SelectControl(sel);
        }

        #endregion

        void ChangeModel(string id)
        {
            var model = Controllers.Find(id);
            if (model == null || model == Controllers.Current) return;
            StopWizard();
            lock (AppConfig.Sync) cfg.ControllerModel = model.Id;
            Controllers.Current = model;
            cfg.Save();
            engine.ModelChanged();
            sel = null;
            SelectControl(model.FirstFader.Id);
            DoLayout();
            view.Invalidate();
        }

        void UpdateLearnButton()
        {
            bool on = engine.LearnControl != null;
            btnLearn.Text = wizard != null ? "Passer ce contrôle" : on ? "Bougez un contrôle…  (Échap pour annuler)" : "MIDI learn";
            btnLearn.Primary = on && wizard == null;
            btnWizard.Text = wizard != null ? "Arrêter l'apprentissage" : "Tout apprendre";
            btnWizard.Primary = wizard != null;
            btnWizard.FitWidth();
            btnLearn.FitWidth();
            btnLearn.Location = new Point(editor.Width - Theme.S(22) - btnLearn.Width, Theme.S(22));
            btnWizard.Location = new Point(btnLearn.Left - Theme.S(8) - btnWizard.Width, Theme.S(22));
            btnLearn.Invalidate();
            btnWizard.Invalidate();
        }

        public void CancelLearn()
        {
            if (wizard != null) { StopWizard(); return; }
            if (engine.LearnControl == null) return;
            engine.LearnControl = null;
            UpdateLearnButton();
        }

        public void OnLearned(string id)
        {
            if (wizard != null && wizard.Count > 0 && wizard[0] == id) { NextWizard(); return; }
            UpdateLearnButton();
            SelectControl(id);
        }

        /// <summary>Appelé ~30 fois/s quand le contrôleur bouge.</summary>
        public void OnActivity(string moved)
        {
            view.Invalidate();
            if (moved != null && moved != sel && cfg.AutoSelect && engine.LearnControl == null && wizard == null && Visible)
                SelectControl(moved);
        }

        public void RefreshMidiLists()
        {
            Fill(ddIn, MidiDevices.Inputs(), cfg.MidiIn, engine.MidiInName);
            Fill(ddOut, MidiDevices.Outputs(), cfg.MidiOut, engine.MidiOutName);
        }

        static void Fill(DropButton dd, List<string> names, string current, string connected)
        {
            dd.Items.Clear();
            dd.Add("", "Automatique" + (string.IsNullOrEmpty(current) && connected != null ? "  ·  " + connected : ""));
            dd.Add("-", "Aucune");
            foreach (var n in names) dd.Add(n, n);
            if (!string.IsNullOrEmpty(current) && current != "-" && !names.Contains(current)) dd.Add(current, current + " (déconnecté)");
            dd.Value = current ?? "";
        }

        public void RefreshAll()
        {
            RefreshMidiLists();
            if (sel != null) SelectControl(sel);
        }

        #endregion
    }
}
