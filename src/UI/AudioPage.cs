using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Périphériques et applications détectés : masquer / renommer.</summary>
    class AudioPage : Panel
    {
        readonly Engine engine;
        readonly AppConfig cfg;
        readonly Label title, sub;
        readonly FlatButton btnRefresh;
        readonly Panel list;
        readonly List<Control> rows = new List<Control>();
        // entrées à gauche, sorties à droite, applications en dessous sur toute la largeur
        readonly List<Control> colIn = new List<Control>(), colOut = new List<Control>(), colApps = new List<Control>();
        List<Control> cur;

        public AudioPage(Engine engine)
        {
            this.engine = engine;
            cfg = engine.Cfg;
            BackColor = Theme.Bg;

            title = Theme.Label("Périphériques audio", Theme.Semi(18f), Theme.Text, BackColor);
            sub = Theme.Label("Tout est détecté automatiquement. Masquez ce que vous ne voulez pas voir dans les menus, ou donnez un nom plus court.",
                Theme.Ui(9.5f), Theme.Muted, BackColor);
            btnRefresh = new FlatButton("Actualiser") { Glyph = Glyphs.Refresh };
            btnRefresh.FitWidth();
            btnRefresh.Click += (s, e) => Reload();
            list = new Panel { AutoScroll = true, BackColor = Theme.Bg };
            list.HandleCreated += (s, e) => Theme.DarkScroll(list);
            list.Resize += (s, e) => LayoutRows();
            Controls.AddRange(new Control[] { title, sub, btnRefresh, list });
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (list == null) return;
            int pad = Theme.S(28);
            title.Location = new Point(pad - Theme.S(2), Theme.S(20));
            sub.Location = new Point(pad, title.Bottom + Theme.S(2));
            btnRefresh.Location = new Point(ClientSize.Width - pad - btnRefresh.Width, Theme.S(30));
            list.SetBounds(pad, Theme.S(100), ClientSize.Width - pad - Theme.S(12), ClientSize.Height - Theme.S(100) - Theme.S(12));
        }

        public void Reload()
        {
            Cursor = Cursors.WaitCursor;
            var outs = engine.Query(a => a.ListDevices(Flow.Render)) ?? new List<DeviceInfo>();
            var ins = engine.Query(a => a.ListDevices(Flow.Capture)) ?? new List<DeviceInfo>();
            var apps = Catalog.Apps(engine, false);
            // Ajoute aussi les applications déjà assignées / masquées / renommées même si elles ne jouent pas de son en ce moment.
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in apps) known.Add(a.Proc);
            lock (AppConfig.Sync)
            {
                var extra = new List<string>();
                foreach (var m in cfg.Controls.Values)
                    foreach (var t in m.Targets)
                        if (t.Type == "app" && t.Id != null) extra.Add(t.Id);
                foreach (var k in cfg.Hidden) if (k.StartsWith("app:")) extra.Add(k.Substring(4));
                foreach (var k in cfg.Aliases.Keys) if (k.StartsWith("app:")) extra.Add(k.Substring(4));
                foreach (var p in extra)
                    if (known.Add(p)) apps.Add(new Catalog.App { Proc = p, Name = Catalog.Describe(p, null) });
            }
            Cursor = Cursors.Default;
            Comparison<DeviceInfo> byName = (a, b) => a.IsDefault != b.IsDefault ? (a.IsDefault ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            outs.Sort(byName);
            ins.Sort(byName);
            apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            list.SuspendLayout();
            foreach (var r in rows) r.Dispose();
            rows.Clear();
            colIn.Clear(); colOut.Clear(); colApps.Clear();
            list.AutoScrollPosition = Point.Empty;

            cur = colIn;
            Section("ENTRÉES AUDIO", ins.Count);
            foreach (var d in ins) Row("dev:" + d.Id, Glyphs.Mic, d.Name, d.IsDefault ? "Périphérique par défaut" : "Entrée");
            cur = colOut;
            Section("SORTIES AUDIO", outs.Count);
            foreach (var d in outs) Row("dev:" + d.Id, Glyphs.Speaker, d.Name, d.IsDefault ? "Périphérique par défaut" : "Sortie");
            cur = colApps;
            Section("APPLICATIONS", apps.Count);
            foreach (var a in apps) Row("app:" + a.Proc, Glyphs.App, a.Name, a.Proc + ".exe" + (a.Playing ? "  ·  session audio active" : ""));

            LayoutRows();
            list.ResumeLayout();
        }

        void Section(string text, int count)
        {
            var l = Theme.Label(text + "  (" + count + ")", Theme.Ui(8f, FontStyle.Bold), Theme.Dim, Theme.Bg);
            l.Tag = "section";
            rows.Add(l);
            cur.Add(l);
            list.Controls.Add(l);
        }

        void Row(string key, string glyph, string name, string detail)
        {
            var card = new Card { Height = Theme.S(58), Radius = 8 };
            var icon = Theme.Label(glyph, Theme.Icon(14f), Theme.Accent, Theme.Card);
            icon.Location = new Point(Theme.S(16), Theme.S(18));
            var lName = Theme.Label(name, Theme.Semi(10f), Theme.Text, Theme.Card);
            lName.Location = new Point(Theme.S(54), Theme.S(9));
            lName.AutoEllipsis = true;
            var lDetail = Theme.Label(detail, Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            lDetail.Location = new Point(Theme.S(54), Theme.S(31));

            var aliasBox = new Panel { BackColor = Theme.Surface, Size = new Size(Theme.S(230), Theme.S(32)) };
            var tb = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Ui(9.5f) };
            tb.Bounds = new Rectangle(Theme.S(10), (aliasBox.Height - tb.PreferredHeight) / 2, aliasBox.Width - Theme.S(20), tb.PreferredHeight);
            aliasBox.Controls.Add(tb);
            lock (AppConfig.Sync) tb.Text = cfg.Alias(key) ?? "";
            tb.HandleCreated += (s, e) => Native.SendMessage(tb.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, "Nom personnalisé…");
            tb.TextChanged += (s, e) =>
            {
                lock (AppConfig.Sync)
                {
                    if (tb.Text.Trim().Length == 0) cfg.Aliases.Remove(key);
                    else cfg.Aliases[key] = tb.Text.Trim();
                }
            };
            tb.Leave += (s, e) => engine.ConfigChanged();

            var lVis = Theme.Label("Afficher", Theme.Ui(9f), Theme.Muted, Theme.Card);
            var toggle = new Toggle();
            lock (AppConfig.Sync) toggle.Checked = !cfg.IsHidden(key);
            Action applyDim = () =>
            {
                lName.ForeColor = toggle.Checked ? Theme.Text : Theme.Dim;
                icon.ForeColor = toggle.Checked ? Theme.Accent : Theme.Dim;
            };
            applyDim();
            toggle.CheckedChanged += (s, e) =>
            {
                lock (AppConfig.Sync)
                {
                    cfg.Hidden.Remove(key);
                    if (!toggle.Checked) cfg.Hidden.Add(key);
                }
                applyDim();
                engine.ConfigChanged();
            };

            card.Controls.AddRange(new Control[] { icon, lName, lDetail, aliasBox, lVis, toggle });
            card.Resize += (s, e) =>
            {
                int right = card.Width - Theme.S(16);
                // colonne étroite : champ de nom plus court
                aliasBox.Width = card.Width < Theme.S(620) ? Theme.S(150) : Theme.S(230);
                tb.Width = aliasBox.Width - Theme.S(20);
                toggle.Location = new Point(right - toggle.Width, (card.Height - toggle.Height) / 2);
                lVis.Location = new Point(toggle.Left - lVis.Width - Theme.S(8), (card.Height - lVis.Height) / 2);
                aliasBox.Location = new Point(lVis.Left - Theme.S(24) - aliasBox.Width, (card.Height - aliasBox.Height) / 2);
                lName.AutoSize = false;
                lName.Size = new Size(Math.Max(Theme.S(60), aliasBox.Left - lName.Left - Theme.S(12)), Theme.S(22));
            };
            rows.Add(card);
            cur.Add(card);
            list.Controls.Add(card);
        }

        void LayoutRows()
        {
            int w = list.Width - SystemInformation.VerticalScrollBarWidth - Theme.S(8);
            int scroll = list.AutoScrollPosition.Y;
            int bottom;
            if (w >= Theme.S(760))
            {
                int gap = Theme.S(16), half = (w - gap) / 2;
                bottom = Math.Max(Place(colIn, 0, 0, half, scroll), Place(colOut, half + gap, 0, half, scroll));
            }
            else bottom = Place(colOut, 0, Place(colIn, 0, 0, w, scroll), w, scroll); // fenêtre étroite : une seule colonne
            Place(colApps, 0, bottom, w, scroll);
        }

        /// <summary>Empile une colonne (titres de section + lignes) à partir de y ; renvoie le y suivant.</summary>
        static int Place(List<Control> col, int x, int y, int w, int scroll)
        {
            foreach (var r in col)
            {
                if ("section".Equals(r.Tag))
                {
                    y += y == 0 ? 0 : Theme.S(14);
                    r.Location = new Point(x + Theme.S(2), y + scroll);
                    y += r.Height + Theme.S(6);
                }
                else
                {
                    r.SetBounds(x, y + scroll, w, r.Height);
                    y += r.Height + Theme.S(6);
                }
            }
            return y;
        }
    }
}
