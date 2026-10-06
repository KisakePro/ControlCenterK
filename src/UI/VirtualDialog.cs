using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MidiSoundController
{
    /// <summary>Fenêtre "Entrées / sorties virtuelles" : câbles détectés et création en un clic dans la console.</summary>
    class VirtualDialog : Form
    {
        readonly RouterPage page;
        readonly Panel list;
        int y;

        public VirtualDialog(RouterPage page, List<DeviceInfo> outs, List<DeviceInfo> ins)
        {
            this.page = page;
            Text = "Entrées et sorties virtuelles";
            Icon = Program.AppIcon;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Size = new Size(Theme.S(820), Theme.S(680));
            MinimumSize = new Size(Theme.S(700), Theme.S(480));
            HandleCreated += (s, e) => Theme.DarkTitle(this);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            list = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(Theme.S(24), Theme.S(16), Theme.S(24), Theme.S(16)) };
            list.HandleCreated += (s, e) => Theme.DarkScroll(list);
            Controls.Add(list);

            List<VirtualDevices.Cable> cables;
            List<DeviceInfo> playOnly, recordOnly;
            VirtualDevices.Scan(outs, ins, out cables, out playOnly, out recordOnly);

            y = Theme.S(16);
            Text1("Entrées et sorties virtuelles", Theme.Semi(15f), Theme.Text);
            Text1("Une entrée virtuelle reçoit le son d'un autre logiciel ; une sortie virtuelle envoie le mix de la console à un autre logiciel " +
                  "(Discord, OBS…), qui la voit comme un micro.", Theme.Ui(9f), Theme.Muted);
            y += Theme.S(10);

            bool hasCable = cables.Exists(c => VirtualDevices.IsCable(c.Play.Name));
            OwnSection();

            Section("CÂBLES VIRTUELS", cables.Count == 0 ? "Aucun câble détecté." : null);
            foreach (var c in cables)
            {
                var cable = c;
                Row(Glyphs.Route, Short(c.Play.Name) + "  ⇄  " + Short(c.Record.Name), VirtualDevices.Brand(c.Play.Name),
                    "Entrée virtuelle", "Les logiciels jouent sur « " + c.Play.Name + " » → le son arrive dans la console.",
                    () => page.AddInput(Short(c.Play.Name), cable.Record, false, cable.Play.Id),
                    "Sortie virtuelle", "La console joue sur « " + c.Play.Name + " » → choisissez « " + c.Record.Name + " » comme micro dans l'autre logiciel.",
                    () => page.AddOutput(Short(c.Play.Name) + " (virtuel)", cable.Play));
            }

            Section("SORTIES VIRTUELLES D'AUTRES LOGICIELS", playOnly.Count == 0 ? "Aucune." : null);
            foreach (var d in playOnly)
            {
                var dev = d;
                Row(Glyphs.Speaker, d.Name, VirtualDevices.Brand(d.Name),
                    "Capturer", "Récupère dans la console tout ce qui est joué sur cette sortie.",
                    () => page.AddInput(Short(dev.Name), dev, true, null),
                    "Y envoyer", "La console envoie son mix vers cette sortie (ex. un canal Wave Link).",
                    () => page.AddOutput(Short(dev.Name), dev));
            }

            Section("ENTRÉES VIRTUELLES D'AUTRES LOGICIELS", recordOnly.Count == 0 ? "Aucune." : null);
            foreach (var d in recordOnly)
            {
                var dev = d;
                Row(Glyphs.Mic, d.Name, VirtualDevices.Brand(d.Name),
                    "Ajouter en entrée", "Récupère ce flux (ex. un mix Wave Link, une sortie Voicemeeter) dans la console.",
                    () => page.AddInput(Short(dev.Name), dev, false, null), null, null, null);
            }
            if (!hasCable) InstallCard();
            list.Controls.Add(new Panel { Bounds = new Rectangle(0, y, 1, Theme.S(16)), BackColor = Theme.Bg });
            list.Resize += (s, e) => Stretch();
        }

        #region Périphériques virtuels de l'application

        /// <summary>Section « Vos périphériques virtuels » : cartes son USB virtuelles créées par l'application.</summary>
        void OwnSection()
        {
            var cfg = Host.Cfg;
            Section("VOS PÉRIPHÉRIQUES VIRTUELS", null);
            var card = new Card { Tag = "stretch", Radius = 8, BackColor = Theme.Mix(Theme.Card, Theme.Accent, 0.10f) };
            card.SetBounds(Theme.S(24), y, Theme.S(740), Theme.S(100));
            var bg = card.BackColor;
            int cy = Theme.S(14);
            Action<Control> addc = c => { c.Location = new Point(Theme.S(18), cy); card.Controls.Add(c); cy = c.Bottom + Theme.S(8); };

            addc(Theme.Label("Créez de vrais périphériques audio, visibles par tous les logiciels", Theme.Semi(10.5f), Theme.Text, bg));
            var expl = Theme.Label("Chaque périphérique crée dans Windows « Haut-parleurs (Nom) » — ce que les logiciels y jouent arrive dans la console — " +
                "et « Microphone (Nom) » — ce que la console y envoie, les autres logiciels (Discord, OBS, jeux…) le reçoivent comme un micro. " +
                "Ils existent tant que MIDI Sound Controller est lancé.", Theme.Ui(8.5f), Theme.Muted, bg);
            expl.MaximumSize = new Size(Theme.S(700), 0);
            addc(expl);

            if (!VirtualHost.Installed)
            {
                var need = Theme.Label("Composant requis (une seule fois) : le pilote USB/IP « usbip-win2 » — gratuit, open source (BSD-2), signé par Microsoft. " +
                    "L'installation redémarre brièvement les ports USB, puis Windows demande un redémarrage.", Theme.Ui(8.5f), Theme.Text, bg);
                need.MaximumSize = new Size(Theme.S(700), 0);
                addc(need);
                var row = new Panel { BackColor = bg, Size = new Size(Theme.S(700), Theme.S(34)) };
                var dl = new FlatButton("Ouvrir la page de téléchargement", true) { Glyph = "\uE896" };
                dl.FitWidth();
                dl.Click += (s, e) => VirtualHost.OpenDownloadPage();
                var again = new FlatButton("C'est installé, vérifier") { Glyph = Glyphs.Refresh };
                again.FitWidth();
                again.Location = new Point(dl.Right + Theme.S(8), 0);
                again.Click += (s, e) => Reopen();
                row.Controls.AddRange(new Control[] { dl, again });
                addc(row);
                var how = Theme.Label("Sur la page : téléchargez « USBip-…-x64.exe », lancez-le et acceptez l'installation.", Theme.Ui(8f), Theme.Dim, bg);
                addc(how);
            }
            else
            {
                List<VirtualDef> defs;
                lock (AppConfig.Sync) defs = new List<VirtualDef>(cfg.Router.Virtuals);
                foreach (var v in defs)
                {
                    var def = v;
                    var dev = VirtualHost.Find("vdev:" + v.Id);
                    bool on = dev != null && dev.Connected;
                    var line = new Panel { BackColor = bg, Size = new Size(Theme.S(700), Theme.S(36)) };
                    var dot = Theme.Label("●", Theme.Ui(10f), on ? Theme.Green : Theme.Muted, bg);
                    dot.Location = new Point(0, Theme.S(7));
                    var nm = Theme.Label(v.Name, Theme.Semi(10f), Theme.Text, bg);
                    nm.Location = new Point(Theme.S(22), Theme.S(2));
                    var st = Theme.Label(on ? "Actif dans Windows : " + WindowsNames(v)
                                            : KindLabel(v.Kind) + " · en attente de Windows (quelques secondes ; sinon « Reconnecter »)", Theme.Ui(8f), on ? Theme.Muted : Theme.Dim, bg);
                    st.Location = new Point(Theme.S(22), Theme.S(20));
                    var del = new FlatButton("Supprimer") { Glyph = "\uE74D" };
                    del.FitWidth();
                    del.Location = new Point(line.Width - del.Width, Theme.S(2));
                    del.Click += (s, e) => DeleteVirtual(def);
                    line.Controls.AddRange(new Control[] { dot, nm, st, del });
                    addc(line);
                }

                var row = new Panel { BackColor = bg, Size = new Size(Theme.S(700), Theme.S(34)) };
                var box = new Panel { BackColor = Theme.Surface, Bounds = new Rectangle(0, 0, Theme.S(260), Theme.S(32)) };
                var tb = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Ui(9.5f) };
                tb.Bounds = new Rectangle(Theme.S(10), (box.Height - tb.PreferredHeight) / 2, box.Width - Theme.S(20), tb.PreferredHeight);
                tb.HandleCreated += (s, e) => Native.SendMessage(tb.Handle, 0x1501, (IntPtr)1, "Nom (ex. Stream, Discord, Jeu…)");
                box.Controls.Add(tb);
                var kind = new DropButton { Width = Theme.S(210), Height = Theme.S(32) };
                kind.Add(VirtualDef.KindPlay, "Sortie (haut-parleurs)");
                kind.Add(VirtualDef.KindMic, "Entrée (micro)");
                kind.Add(VirtualDef.KindBoth, "Sortie + entrée");
                kind.Value = lastKind;
                kind.Location = new Point(box.Right + Theme.S(8), 0);
                var create = new FlatButton("Créer", true) { Glyph = Glyphs.Add };
                create.FitWidth();
                create.Location = new Point(kind.Right + Theme.S(8), 0);
                create.Click += (s, e) => CreateVirtual(tb.Text, kind.Value);
                tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CreateVirtual(tb.Text, kind.Value); } };
                row.Controls.AddRange(new Control[] { box, kind, create });
                if (defs.Count > 0)
                {
                    var re = new FlatButton("Reconnecter") { Glyph = Glyphs.Refresh };
                    re.FitWidth();
                    re.Location = new Point(create.Right + Theme.S(8), 0);
                    re.Click += (s, e) =>
                    {
                        Cursor = Cursors.WaitCursor;
                        string err = VirtualHost.Reattach(cfg);
                        Cursor = Cursors.Default;
                        if (err != null) MessageBox.Show(this, err, "Périphériques virtuels", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        Reopen();
                    };
                    row.Controls.Add(re);
                }
                addc(row);
                var hint = Theme.Label(KindHint(kind.Value), Theme.Ui(8.5f), Theme.Muted, bg);
                hint.MaximumSize = new Size(Theme.S(700), 0);
                kind.ValueChanged += (s, e) => { lastKind = kind.Value; hint.Text = KindHint(kind.Value); };
                addc(hint);
                if (VirtualHost.LastError != null) addc(Theme.Label(VirtualHost.LastError, Theme.Ui(8.5f), Theme.Red, bg));
            }
            card.Height = cy + Theme.S(6);
            list.Controls.Add(card);
            y = card.Bottom + Theme.S(10);
        }

        static string lastKind = VirtualDef.KindPlay;

        static string KindLabel(string k)
        {
            return k == VirtualDef.KindPlay ? "Sortie" : k == VirtualDef.KindMic ? "Entrée" : "Sortie + entrée";
        }

        static string WindowsNames(VirtualDef v)
        {
            if (v.Kind == VirtualDef.KindPlay) return "sortie « Haut-parleurs (" + v.Name + ") »";
            if (v.Kind == VirtualDef.KindMic) return "entrée « Microphone (" + v.Name + ") »";
            return "« Haut-parleurs (" + v.Name + ") » et « Microphone (" + v.Name + ") »";
        }

        static string KindHint(string k)
        {
            if (k == VirtualDef.KindPlay)
                return "Sortie : apparaît dans Windows comme « Haut-parleurs (Nom) ». Les logiciels y envoient leur son, qui arrive dans la console comme une entrée.";
            if (k == VirtualDef.KindMic)
                return "Entrée : apparaît dans Windows comme « Microphone (Nom) ». La console y envoie un mix, que les autres logiciels (Discord, OBS, jeux…) écoutent comme un micro.";
            return "Sortie + entrée : crée les deux à la fois (« Haut-parleurs (Nom) » et « Microphone (Nom) »).";
        }

        void CreateVirtual(string name, string kind)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) { MessageBox.Show(this, "Donnez un nom au périphérique.", "Périphériques virtuels"); return; }
            if (name.Length > 30) name = name.Substring(0, 30);
            Cursor = Cursors.WaitCursor;
            string err;
            var def = VirtualHost.Create(Host.Cfg, name, kind, out err);
            bool both = kind == VirtualDef.KindBoth;
            if (kind != VirtualDef.KindMic)
                page.AddInput(name, new DeviceInfo { Id = "vdev:" + def.Id, Name = "Haut-parleurs (" + name + ")", Flow = Flow.Render }, false, null);
            if (kind != VirtualDef.KindPlay)
                page.AddOutput(both ? name + " (micro)" : name, new DeviceInfo { Id = "vdev:" + def.Id, Name = "Microphone (" + name + ")", Flow = Flow.Capture });
            Cursor = Cursors.Default;
            if (err != null) MessageBox.Show(this, "Le périphérique a été créé dans la console, mais Windows ne l'a pas encore attaché :\n" + err,
                "Périphériques virtuels", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Reopen();
        }

        void DeleteVirtual(VirtualDef def)
        {
            if (MessageBox.Show(this, "Supprimer le périphérique virtuel « " + def.Name + " » ?\nIl disparaîtra de Windows et ses tranches seront retirées de la console.",
                    "Périphériques virtuels", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Cursor = Cursors.WaitCursor;
            page.RemoveDevice("vdev:" + def.Id);
            string err = VirtualHost.Delete(Host.Cfg, def);
            Cursor = Cursors.Default;
            if (err != null) MessageBox.Show(this, err, "Périphériques virtuels", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Reopen();
        }

        /// <summary>Rafraîchit la fenêtre (rouverte par la page Routage).</summary>
        void Reopen()
        {
            page.ReopenVirtual = true;
            Close();
        }

        #endregion

        static string Short(string name)
        {
            int p = name.IndexOf(" (", StringComparison.Ordinal);
            return p > 0 ? name.Substring(0, p) : name;
        }

        int Inner { get { return list.ClientSize.Width - list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth; } }

        void Stretch()
        {
            foreach (Control c in list.Controls)
                if ("stretch".Equals(c.Tag)) { c.Width = Math.Max(Theme.S(400), Inner); c.PerformLayout(); }
        }

        void Text1(string text, Font f, Color c)
        {
            var l = Theme.Label(text, f, c, Theme.Bg);
            l.MaximumSize = new Size(Theme.S(740), 0);
            l.Location = new Point(Theme.S(24), y);
            list.Controls.Add(l);
            y = l.Bottom + Theme.S(6);
        }

        void Section(string title, string empty)
        {
            y += Theme.S(10);
            var l = Theme.Label(title, Theme.Ui(8f, FontStyle.Bold), Theme.Dim, Theme.Bg);
            l.Location = new Point(Theme.S(26), y);
            list.Controls.Add(l);
            y = l.Bottom + Theme.S(6);
            if (empty != null)
            {
                var e = Theme.Label(empty, Theme.Ui(9f), Theme.Muted, Theme.Bg);
                e.Location = new Point(Theme.S(26), y);
                list.Controls.Add(e);
                y = e.Bottom + Theme.S(6);
            }
        }

        void Row(string glyph, string title, string brand, string b1, string tip1, Action a1, string b2, string tip2, Action a2)
        {
            var card = new Card { Tag = "stretch", Radius = 8 };
            card.SetBounds(Theme.S(24), y, Theme.S(740), Theme.S(66));
            var icon = Theme.Label(glyph, Theme.Icon(14f), Theme.Accent, Theme.Card);
            icon.Location = new Point(Theme.S(16), Theme.S(22));
            var t = Theme.Label(title, Theme.Semi(10f), Theme.Text, Theme.Card);
            t.AutoSize = false;
            t.AutoEllipsis = true;
            t.Location = new Point(Theme.S(52), Theme.S(12));
            var bl = Theme.Label(brand, Theme.Ui(8f), Theme.Muted, Theme.Card);
            bl.Location = new Point(Theme.S(52), Theme.S(36));
            card.Controls.AddRange(new Control[] { icon, t, bl });
            var tips = new ToolTip();
            var buttons = new List<FlatButton>();
            foreach (var spec in new[] { new object[] { b1, tip1, a1 }, new object[] { b2, tip2, a2 } })
            {
                if (spec[0] == null) continue;
                var btn = new FlatButton((string)spec[0]) { Glyph = Glyphs.Add };
                btn.FitWidth();
                var act = (Action)spec[2];
                string label = (string)spec[0];
                btn.Click += (s, e) =>
                {
                    act();
                    btn.Text = "Ajouté ✓";
                    btn.Glyph = null;
                    btn.Primary = true;
                    btn.Invalidate();
                };
                tips.SetToolTip(btn, (string)spec[1]);
                card.Controls.Add(btn);
                buttons.Add(btn);
            }
            card.Layout += (s, e) =>
            {
                int x = card.Width - Theme.S(14);
                for (int i = buttons.Count - 1; i >= 0; i--)
                {
                    x -= buttons[i].Width;
                    buttons[i].Location = new Point(x, (card.Height - buttons[i].Height) / 2);
                    x -= Theme.S(8);
                }
                t.Size = new Size(Math.Max(Theme.S(80), x - t.Left - Theme.S(10)), Theme.S(22));
            };
            list.Controls.Add(card);
            y = card.Bottom + Theme.S(6);
        }

        void InstallCard()
        {
            y += Theme.S(6);
            var card = new Card { Tag = "stretch", Radius = 8, BackColor = Theme.Mix(Theme.Card, Theme.Accent, 0.12f) };
            card.SetBounds(Theme.S(24), y, Theme.S(740), Theme.S(118));
            var t = Theme.Label("Autre solution : VB-CABLE", Theme.Semi(10.5f), Theme.Text, card.BackColor);
            t.Location = new Point(Theme.S(18), Theme.S(14));
            var d = Theme.Label("Si vous préférez un câble classique : VB-CABLE (gratuit, par VB-Audio, l'éditeur de Voicemeeter) ajoute la paire " +
                "« CABLE Input » / « CABLE Output », qui apparaîtra automatiquement dans « Câbles virtuels ».",
                Theme.Ui(8.5f), Theme.Muted, card.BackColor);
            d.MaximumSize = new Size(Theme.S(700), 0);
            d.Location = new Point(Theme.S(18), Theme.S(40));
            var btn = new FlatButton("Ouvrir la page VB-CABLE", true) { Glyph = "" };
            btn.FitWidth();
            btn.Location = new Point(Theme.S(18), d.Bottom + Theme.S(12));
            btn.Click += (s, e) => { try { Process.Start(VirtualDevices.VbCableUrl); } catch { } };
            card.Controls.AddRange(new Control[] { t, d, btn });
            card.Height = btn.Bottom + Theme.S(16);
            list.Controls.Add(card);
            y = card.Bottom + Theme.S(10);
        }
    }
}
