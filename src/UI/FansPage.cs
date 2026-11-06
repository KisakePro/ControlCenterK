using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Page du module « Ventilateurs » : relevés, vitesse fixe, identification et noms des prises.</summary>
    class FansPage : Panel
    {
        const int StopSeconds = 30, StopGrace = 3; // temps de redémarrage : la ligne reste en place pendant ce délai
        const int IdentifyPhase = 4, IdentifyPhases = 4; // secondes par palier ; plein régime / ralenti, deux fois

        readonly AppConfig cfg;
        readonly Panel scroll;
        readonly List<Card> cards = new List<Card>();
        readonly Dictionary<string, Label> values = new Dictionary<string, Label>();
        readonly Dictionary<string, string> results = new Dictionary<string, string>();
        readonly Timer timer;
        Label titleLabel, subLabel;
        string layoutKey;

        // identification en cours
        string idKey;
        int idTicks;
        float idHigh, idLow = float.MaxValue;

        // arrêt de repérage en cours
        string stopKey;
        int stopTicks;
        FlatButton stopButton;

        public FansPage(AppConfig cfg)
        {
            this.cfg = cfg;
            BackColor = Theme.Bg;
            scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            scroll.HandleCreated += (s, e) => Theme.DarkScroll(scroll);
            scroll.Resize += (s, e) => LayoutCards();
            Controls.Add(scroll);
            timer = new Timer { Interval = 1000 };
            timer.Tick += (s, e) => Tick();
            timer.Start();
            LedControl.ZonesChanged += OnZones;
            Disposed += (s, e) => { LedControl.ZonesChanged -= OnZones; timer.Dispose(); if (idKey != null) FanControl.Hold(idKey, null); if (stopKey != null) FanControl.Hold(stopKey, null); };
            Tick();
        }

        void OnZones() { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => layoutKey = null)); } catch { } }

        void Tick()
        {
            string error;
            var devs = FanSensors.Read(out error);
            if (idKey != null) StepIdentify(devs);
            if (stopKey != null) StepStop();
            string key = devs == null ? "none" : error != null ? "err" + error : Key(devs);
            if (key != layoutKey) { layoutKey = key; Rebuild(devs, error); }
            if (devs != null) ShowValues(devs);
            if (subLabel != null)
            {
                string sub = FanSensors.Guarded ? "Processeur trop chaud : toutes les prises sont rendues au BIOS jusqu'à ce qu'il refroidisse."
                                                : "Le BIOS garde la main sauf sur les prises réglées ici, et les réglages sont conservés. Vitesse minimale " + FanRules.MinPercent.ToString("0") + " % ; les pompes restent en lecture seule.";
                if (subLabel.Text != sub) subLabel.Text = sub;
            }
        }

        // la structure ne change que si une prise démarre ou s'arrête : on ne reconstruit qu'à ce moment
        string Key(List<FanDevice> devs)
        {
            var sb = new StringBuilder();
            foreach (var d in devs)
            {
                sb.Append(d.Name).Append('|');
                foreach (var f in d.Fans) sb.Append(f.Name).Append(f.Rpm > 0 || d.Name + "|" + f.Name == stopKey).Append(FanControl.IsPumpSignal(d.Name + "|" + f.Name)).Append(',');
            }
            return sb.ToString();
        }

        string Display(string key, string name)
        {
            string custom;
            lock (AppConfig.Sync) if (cfg.FanNames.TryGetValue(key, out custom) && custom.Length > 0) return custom;
            return name;
        }

        void Rebuild(List<FanDevice> devs, string error)
        {
            scroll.SuspendLayout();
            var old = new List<Control>();
            foreach (Control c in scroll.Controls) old.Add(c);
            foreach (var c in old) { scroll.Controls.Remove(c); c.Dispose(); }
            cards.Clear();
            values.Clear();
            titleLabel = Theme.Label("Ventilateurs", Theme.Semi(18f), Theme.Text, Theme.Bg);
            titleLabel.Location = new Point(Theme.S(26), Theme.S(20));
            subLabel = Theme.Label("", Theme.Ui(9.5f), Theme.Muted, Theme.Bg);
            subLabel.Location = new Point(Theme.S(28), Theme.S(58));
            scroll.Controls.Add(titleLabel);
            scroll.Controls.Add(subLabel);
            if (error != null) cards.Add(Message("Lecture impossible", error, true));
            else if (devs == null)
                cards.Add(Message(!CpuProbe.PawnIOInstalled ? "PawnIO n'est pas installé" : "En attente de la sonde…",
                    !CpuProbe.PawnIOInstalled ? "Lire les ventilateurs demande le pilote PawnIO (signé et open source). Installez-le depuis pawnio.eu, puis revenez ici."
                                              : "La sonde lit les ventilateurs avec les droits administrateur. Si rien n'apparaît après quelques secondes, installez-la ou réinstallez-la ci-dessous : une seule confirmation.", true));
            else if (devs.Count == 0)
                cards.Add(Message("Aucun ventilateur détecté", "La puce de cette carte mère n'est pas reconnue, ou ce PC ne donne pas accès à ses ventilateurs (fréquent sur les portables et PC de marque).", false));
            else foreach (var d in devs) cards.Add(DeviceCard(d, devs));
            cards.Add(LedCard());
            foreach (var c in cards) scroll.Controls.Add(c);
            LayoutCards();
            scroll.ResumeLayout();
        }

        void LayoutCards()
        {
            int pad = Theme.S(28), y = Theme.S(92) + scroll.AutoScrollPosition.Y;
            if (titleLabel != null) titleLabel.Top = Theme.S(20) + scroll.AutoScrollPosition.Y;
            if (subLabel != null) subLabel.Top = Theme.S(58) + scroll.AutoScrollPosition.Y;
            int w = Math.Max(Theme.S(560), Math.Min(Theme.S(980), scroll.ClientSize.Width - 2 * pad));
            foreach (var c in cards) { c.SetBounds(pad, y, w, c.Height); y = c.Bottom + Theme.S(16); }
        }

        Card LedCard()
        {
            var zones = LedControl.Zones;
            var c = new Card { Width = Theme.S(900) };
            var t = Theme.Label("Éclairage", Theme.Semi(12f), Theme.Text, Theme.Card);
            t.Location = new Point(Theme.S(20), Theme.S(16));
            var d = Theme.Label(zones.Count == 0 ? "Aucun éclairage reconnu : NZXT Kraken X3 et cartes mères ASUS Aura sont pris en charge."
                : "Les effets tournent dans le matériel. Une zone sur « Ne pas toucher » garde son éclairage actuel. Fermez Armoury Crate et NZXT CAM : ils imposeraient le leur.", Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            d.MaximumSize = new Size(Theme.S(860), 0);
            d.Location = new Point(Theme.S(20), t.Bottom + Theme.S(4));
            c.Controls.AddRange(new Control[] { t, d });
            int y = d.Bottom + Theme.S(10);
            foreach (var z in zones)
            {
                var zone = z;
                var st = LedControl.Get(z.Key);
                var row = new Card { BackColor = Theme.Surface, Radius = 6, Bounds = new Rectangle(Theme.S(14), y, c.Width - Theme.S(28), Theme.S(48)),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                var name = Theme.Label(z.Name, Theme.Semi(9.5f), Theme.Text, row.BackColor);
                name.Location = new Point(Theme.S(12), Theme.S(14));
                row.Controls.Add(name);
                var mode = new DropButton { Width = Theme.S(170), Anchor = AnchorStyles.Top | AnchorStyles.Right };
                mode.Add("none", "Ne pas toucher");
                mode.Add("off", "Éteint");
                mode.Add("fixed", "Couleur fixe");
                mode.Add("breathing", "Respiration");
                mode.Add("pulse", "Pulsation");
                mode.Add("cycle", "Cycle de couleurs");
                mode.Add("rainbow", "Arc-en-ciel");
                mode.Value = st.Mode;
                mode.Location = new Point(row.Width - mode.Width - Theme.S(8), Theme.S(8));
                var col = new Button { FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(st.Color | unchecked((int)0xFF000000)), Size = new Size(Theme.S(32), Theme.S(32)), Cursor = Cursors.Hand, Anchor = AnchorStyles.Top | AnchorStyles.Right };
                col.FlatAppearance.BorderColor = Theme.Border;
                col.Location = new Point(mode.Left - col.Width - Theme.S(6), Theme.S(8));
                new ToolTip().SetToolTip(col, "Couleur (couleur fixe, respiration, pulsation)");
                var speed = new DropButton { Width = Theme.S(100), Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = z.HasSpeed };
                speed.Add("0", "Lent");
                speed.Add("1", "Normal");
                speed.Add("2", "Rapide");
                speed.Value = st.Speed.ToString();
                speed.Location = new Point(col.Left - speed.Width - Theme.S(6), Theme.S(8));
                var fail = Theme.Label("", Theme.Ui(8.5f), Theme.Red, row.BackColor);
                fail.Location = new Point(Theme.S(12), Theme.S(30));
                Action send = () =>
                {
                    int rgb = col.BackColor.ToArgb() & 0xFFFFFF;
                    bool ok = LedControl.Set(zone, new LedSetting { Mode = mode.Value, Color = rgb, Speed = int.Parse(speed.Value) });
                    fail.Text = ok ? "" : "L'appareil n'a pas répondu";
                };
                mode.ValueChanged += (s, e) => send();
                speed.ValueChanged += (s, e) => send();
                col.Click += (s, e) =>
                {
                    using (var dlg = new ColorDialog { FullOpen = true, Color = col.BackColor })
                        if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                        {
                            col.BackColor = Color.FromArgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                            if (mode.Value != "none") send();
                        }
                };
                row.Controls.AddRange(new Control[] { mode, col, speed, fail });
                c.Controls.Add(row);
                y += Theme.S(54);
            }
            c.Height = y + Theme.S(8);
            return c;
        }

        Card Message(string title, string text, bool probeButton)
        {
            var c = new Card { Width = Theme.S(900) };
            var t = Theme.Label(title, Theme.Semi(12f), Theme.Text, Theme.Card);
            t.Location = new Point(Theme.S(20), Theme.S(16));
            var d = Theme.Label(text, Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            d.MaximumSize = new Size(Theme.S(860), 0);
            d.Location = new Point(Theme.S(20), t.Bottom + Theme.S(4));
            c.Controls.AddRange(new Control[] { t, d });
            int y = d.Bottom + Theme.S(12);
            if (probeButton)
            {
                bool pawn = CpuProbe.PawnIOInstalled;
                var b = new FlatButton(pawn ? "Installer ou réinstaller la sonde" : "Ouvrir pawnio.eu", true) { Glyph = pawn ? "" : "" };
                b.FitWidth();
                b.Location = new Point(Theme.S(20), y);
                b.Click += (s, e) =>
                {
                    if (!pawn) { try { Process.Start("https://pawnio.eu"); } catch { } return; }
                    b.Enabled = false;
                    new System.Threading.Thread(() => CpuProbe.Install()) { IsBackground = true }.Start();
                };
                c.Controls.Add(b);
                y = b.Bottom + Theme.S(12);
            }
            c.Height = y;
            return c;
        }

        Card DeviceCard(FanDevice d, List<FanDevice> all)
        {
            var c = new Card { Width = Theme.S(900) };
            var t = Theme.Label(d.Name, Theme.Semi(12f), Theme.Text, Theme.Card);
            t.Location = new Point(Theme.S(20), Theme.S(16));
            c.Controls.Add(t);
            var temp = Theme.Label("", Theme.Ui(8.5f), Theme.Muted, Theme.Card);
            temp.AutoSize = false;
            temp.TextAlign = ContentAlignment.MiddleRight;
            temp.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            temp.Bounds = new Rectangle(c.Width - Theme.S(420), Theme.S(16), Theme.S(400), t.Height);
            c.Controls.Add(temp);
            values[d.Name + "|temps"] = temp;
            int y = t.Bottom + Theme.S(10);
            if (d.Fans.Exists(f => !FanRules.CanDrive(f.Name)))
            {
                var note = Theme.Label("La pompe se règle dans le BIOS (ou le logiciel du fabricant), pas depuis ControlCenterK : ses valeurs sont affichées à titre indicatif.", Theme.Ui(8.5f), Theme.Muted, Theme.Card);
                note.MaximumSize = new Size(Theme.S(860), 0);
                note.Location = new Point(Theme.S(20), y - Theme.S(4));
                c.Controls.Add(note);
                y = note.Bottom + Theme.S(8);
            }
            var fans = new List<FanReading>(d.Fans);
            Func<FanReading, bool> isLive = x => x.Rpm > 0 || d.Name + "|" + x.Name == stopKey;
            fans.Sort((a, b) => isLive(b) != isLive(a) ? isLive(b).CompareTo(isLive(a)) : string.CompareOrdinal(a.Name, b.Name));
            foreach (var f in fans)
            {
                string key = d.Name + "|" + f.Name;
                if (FanControl.IsPumpSignal(key)) continue; // affichée avec la pompe, dans la carte de l'AIO
                bool live = isLive(f);
                var row = new Card { BackColor = Theme.Surface, Radius = 6, Bounds = new Rectangle(Theme.S(14), y, c.Width - Theme.S(28), Theme.S(48)),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                var name = Theme.Label(Display(key, f.Name), Theme.Semi(9.5f), live ? Theme.Text : Theme.Dim, row.BackColor);
                name.Location = new Point(Theme.S(12), Theme.S(6));
                var val = Theme.Label("", Theme.Ui(8.5f), live ? Theme.Muted : Theme.Dim, row.BackColor);
                val.AutoSize = false;
                val.Bounds = new Rectangle(Theme.S(12), Theme.S(26), Theme.S(470), Theme.S(18));
                row.Controls.AddRange(new Control[] { name, val });
                values[key] = val;
                int right = row.Width - Theme.S(8);
                if (live && f.Controllable && FanRules.CanDrive(f.Name)) right = AddControls(row, key, right);
                if (live)
                {
                    var ren = new FlatButton("") { Glyph = "", Width = Theme.S(36), Anchor = AnchorStyles.Top | AnchorStyles.Right };
                    ren.Location = new Point(right - ren.Width, Theme.S(8));
                    string fanName = f.Name;
                    ren.Click += (s, e) => Rename(key, fanName);
                    new ToolTip().SetToolTip(ren, "Renommer");
                    row.Controls.Add(ren);
                }
                c.Controls.Add(row);
                y += Theme.S(54);
            }
            if (d.Fans.Exists(f => !FanRules.CanDrive(f.Name) && f.Rpm > 0))
                foreach (var o in all)
                    foreach (var f in o.Fans)
                    {
                        string key = o.Name + "|" + f.Name;
                        if (!FanControl.IsPumpSignal(key)) continue;
                        var row = new Card { BackColor = Theme.Surface, Radius = 6, Bounds = new Rectangle(Theme.S(14), y, c.Width - Theme.S(28), Theme.S(48)),
                            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                        var name = Theme.Label("Signal de la pompe sur la carte mère (" + f.Name + ")", Theme.Semi(9.5f), Theme.Text, row.BackColor);
                        name.Location = new Point(Theme.S(12), Theme.S(6));
                        var val = Theme.Label("", Theme.Ui(8.5f), Theme.Muted, row.BackColor);
                        val.AutoSize = false;
                        val.Bounds = new Rectangle(Theme.S(12), Theme.S(26), Theme.S(470), Theme.S(18));
                        var no = new FlatButton("Ce n'est pas la pompe") { Anchor = AnchorStyles.Top | AnchorStyles.Right };
                        no.FitWidth();
                        no.Location = new Point(row.Width - no.Width - Theme.S(8), Theme.S(8));
                        no.Click += (s, e) => { FanControl.NotPump(key); layoutKey = null; };
                        new ToolTip().SetToolTip(no, "Détecté car cette prise a la même vitesse que la pompe. Cliquez si c'est un vrai ventilateur.");
                        row.Controls.AddRange(new Control[] { name, val, no });
                        values[key] = val;
                        c.Controls.Add(row);
                        y += Theme.S(54);
                    }
            c.Height = y + Theme.S(8);
            return c;
        }

        int AddControls(Card row, string key, int right)
        {
            var speed = new DropButton { Width = Theme.S(110), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            speed.Add("bios", "BIOS");
            speed.Add("curve", "Courbe…");
            foreach (int p in new[] { 30, 40, 50, 60, 70, 80, 90, 100 }) speed.Add(p.ToString(), p + " %");
            var st = Setting(key);
            speed.Value = st.Mode == "fixed" ? st.Fixed.ToString("0") : st.Mode;
            speed.Location = new Point(right - speed.Width, Theme.S(8));
            speed.ValueChanged += (s, e) =>
            {
                var cur = Setting(key);
                if (speed.Value == "curve")
                {
                    using (var dlg = new FanCurveDialog(Display(key, key.Substring(key.IndexOf('|') + 1)), cur))
                        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) Store(key, x => { x.Mode = "curve"; x.Points = dlg.Points; x.Source = dlg.Source; x.Fix(); });
                    speed.Value = Setting(key).Mode == "fixed" ? Setting(key).Fixed.ToString("0") : Setting(key).Mode;
                    speed.Invalidate();
                }
                else if (speed.Value == "bios") Store(key, x => x.Mode = "bios");
                else { float v = float.Parse(speed.Value); Store(key, x => { x.Mode = "fixed"; x.Fixed = v; }); }
            };
            row.Controls.Add(speed);
            var id = new FlatButton("Identifier") { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            id.FitWidth();
            id.Location = new Point(speed.Left - id.Width - Theme.S(6), Theme.S(8));
            id.Click += (s, e) => StartIdentify(key);
            new ToolTip().SetToolTip(id, "Fait varier ce ventilateur seul pendant " + IdentifyPhase * IdentifyPhases + " secondes pour le repérer, et vérifie qu'il réagit.");
            row.Controls.Add(id);
            var stop = new FlatButton("Arrêt " + StopSeconds + " s") { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            stop.FitWidth();
            stop.Location = new Point(id.Left - stop.Width - Theme.S(6), Theme.S(8));
            if (key == stopKey && stopTicks < StopSeconds) { stopButton = stop; ShowStopping(stop, true); }
            stop.Click += (s, e) =>
            {
                if (key == stopKey && stopTicks < StopSeconds) Resume();
                else if (StartStop(key)) { stopButton = stop; ShowStopping(stop, true); }
            };
            new ToolTip().SetToolTip(stop, "Met ce ventilateur à 0 % pendant " + StopSeconds + " secondes pour le repérer, puis il reprend tout seul.");
            row.Controls.Add(stop);
            return stop.Left - Theme.S(6);
        }

        FanSetting Setting(string key)
        {
            FanSetting s;
            lock (AppConfig.Sync) if (cfg.Fans.TryGetValue(key, out s)) return s;
            s = new FanSetting();
            s.Fix();
            return s;
        }

        void Store(string key, Action<FanSetting> change)
        {
            lock (AppConfig.Sync)
            {
                var s = Setting(key);
                change(s);
                cfg.Fans[key] = s;
            }
            cfg.Save();
            FanControl.Changed();
        }

        void Rename(string key, string fanName)
        {
            string current = Display(key, fanName);
            string n = InputBox.Show(FindForm(), "Renommer", "Nom de cette prise (vide = " + fanName + ") :", current == fanName ? "" : current);
            if (n == null) return;
            lock (AppConfig.Sync) { if (n.Trim().Length == 0) cfg.FanNames.Remove(key); else cfg.FanNames[key] = n.Trim(); }
            cfg.Save();
            layoutKey = null;
        }

        static void ShowStopping(FlatButton b, bool stopping)
        {
            b.Text = stopping ? "Reprendre" : "Arrêt " + StopSeconds + " s";
            b.Primary = stopping;
            b.Invalidate();
        }

        /// <summary>Ventilateur repéré avant la fin : il reprend tout de suite.</summary>
        void Resume()
        {
            stopTicks = StopSeconds;
            FanControl.Hold(stopKey, null);
            if (stopButton != null) ShowStopping(stopButton, false);
        }

        bool StartStop(string key)
        {
            if (idKey != null || stopKey != null || FanSensors.Guarded) return false;
            stopKey = key;
            stopTicks = 0;
            results.Remove(key);
            FanControl.Hold(key, 0);
            return true;
        }

        void StepStop()
        {
            stopTicks++;
            if (stopTicks == StopSeconds)
            {
                FanControl.Hold(stopKey, null);
                if (stopButton != null) ShowStopping(stopButton, false);
            }
            if (stopTicks >= StopSeconds + StopGrace) { stopKey = null; layoutKey = null; }
        }

        void StartIdentify(string key)
        {
            if (idKey != null || stopKey != null || FanSensors.Guarded) return;
            idKey = key;
            idTicks = 0;
            idHigh = 0;
            idLow = float.MaxValue;
            results.Remove(key);
            FanControl.Hold(key, 100);
        }

        /// <summary>Plein régime puis ralenti ; on mesure en fin de palier, le temps que le ventilateur change de vitesse.</summary>
        void StepIdentify(List<FanDevice> devs)
        {
            idTicks++;
            int phase = (idTicks - 1) / IdentifyPhase;
            bool endOfPhase = idTicks % IdentifyPhase == 0;
            if (endOfPhase && devs != null)
                foreach (var d in devs)
                    foreach (var f in d.Fans)
                        if (d.Name + "|" + f.Name == idKey && f.Rpm > 0)
                        {
                            if (phase % 2 == 0) idHigh = Math.Max(idHigh, f.Rpm);
                            else idLow = Math.Min(idLow, f.Rpm);
                        }
            if (idTicks >= IdentifyPhase * IdentifyPhases)
            {
                bool reacts = idLow != float.MaxValue && idHigh > 0 && idHigh - idLow >= idHigh * 0.15f;
                results[idKey] = idLow == float.MaxValue || idHigh == 0 ? "identification : mesure impossible"
                    : reacts ? "réagit (" + idLow.ToString("0") + " à " + idHigh.ToString("0") + " tr/min)"
                             : "ne réagit pas : vérifiez le mode PWM/DC de la prise dans le BIOS";
                FanControl.Hold(idKey, null);
                idKey = null;
                return;
            }
            if (endOfPhase) FanControl.Hold(idKey, (phase + 1) % 2 == 0 ? 100 : FanRules.MinPercent);
        }

        void ShowValues(List<FanDevice> devs)
        {
            foreach (var d in devs)
            {
                Label l;
                if (values.TryGetValue(d.Name + "|temps", out l))
                {
                    var parts = new List<string>();
                    foreach (var t in d.Temps) if (t.Value > 0) parts.Add(t.Key + " " + t.Value.ToString("0.0") + " °C");
                    l.Text = string.Join("   ·   ", parts);
                }
                foreach (var f in d.Fans)
                {
                    string key = d.Name + "|" + f.Name;
                    if (!values.TryGetValue(key, out l)) continue;
                    string text = float.IsNaN(f.Rpm) ? "tours/min inconnus" : f.Rpm > 0 ? f.Rpm.ToString("0") + " tr/min" : "arrêté ou rien de branché";
                    if (!float.IsNaN(f.Percent)) text += "  ·  " + f.Percent.ToString("0") + " %";
                    string res;
                    if (key == idKey) text += "  ·  identification en cours…";
                    else if (key == stopKey) text += stopTicks < StopSeconds ? "  ·  arrêt : encore " + (StopSeconds - stopTicks) + " s" : "  ·  redémarrage…";
                    else if (results.TryGetValue(key, out res)) text += "  ·  " + res;
                    l.Text = text;
                }
            }
        }
    }
}
