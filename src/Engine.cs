using System;
using System.Collections.Generic;
using System.Threading;

namespace MidiSoundController
{
    /// <summary>
    /// Cœur de l'application : reçoit le MIDI, applique les volumes / actions et gère les LED.
    /// Tout le travail audio se fait sur un seul thread worker qui dort tant qu'aucun message n'arrive
    /// (0 % CPU en arrière-plan). Les mouvements de fader sont fusionnés : seule la dernière valeur compte.
    /// </summary>
    public sealed class Engine
    {
        public readonly AppConfig Cfg;

        readonly object qLock = new object();
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly Queue<int> raw = new Queue<int>();
        readonly Queue<Action> work = new Queue<Action>();
        readonly Dictionary<string, int> pending = new Dictionary<string, int>();
        readonly Queue<string> presses = new Queue<string>();
        Thread thread;
        volatile bool running;
        volatile bool audioDirty;
        AudioSystem audio;

        readonly object midiLock = new object();
        MidiInput midiIn;
        MidiOutput midiOut;
        string midiSnapshot = "";

        volatile Dictionary<int, string> keyMap = new Dictionary<int, string>();
        readonly Dictionary<string, int> values = new Dictionary<string, int>();
        readonly Dictionary<string, bool> leds = new Dictionary<string, bool>();

        /// <summary>Contrôle en attente d'apprentissage MIDI (null = pas d'apprentissage).</summary>
        public volatile string LearnControl;

        /// <summary>Module de routage (null si désactivé) : les faders peuvent piloter ses tranches.</summary>
        public volatile AudioRouter Router;

        /// <summary>Un contrôle a bougé ou une LED a changé (id peut être null). Appelé depuis le worker.</summary>
        public event Action<string> ControlMoved;
        /// <summary>Un contrôle vient d'être appris. Appelé depuis le worker.</summary>
        public event Action<string> Learned;
        /// <summary>Connexion MIDI modifiée.</summary>
        public event Action StateChanged;
        /// <summary>Le profil actif a changé (n'importe quel thread).</summary>
        public event Action ProfileChanged;

        public string MidiInName { get; private set; }
        public string MidiOutName { get; private set; }

        public Engine(AppConfig cfg)
        {
            Cfg = cfg;
            RebuildMap();
            // Dernières positions connues des faders / potentiomètres (affichage au redémarrage)
            lock (AppConfig.Sync)
                foreach (var kv in cfg.ControlValues)
                {
                    var d = NanoKontrol2.Get(kv.Key);
                    if (d != null && d.Kind != ControlKind.Button) values[kv.Key] = Math.Max(0, Math.Min(127, kv.Value));
                }
        }

        Timer valuesTimer;

        /// <summary>Enregistre les positions un peu après le dernier mouvement (pas à chaque cran).</summary>
        void SaveValuesSoon()
        {
            valuesDirty = true;
            if (valuesTimer == null) valuesTimer = new Timer(_ => SaveValues(), null, 1500, Timeout.Infinite);
            else valuesTimer.Change(1500, Timeout.Infinite);
        }

        volatile bool valuesDirty;

        /// <summary>Enregistre immédiatement les positions si elles ont changé (fermeture de l'app, arrêt de Windows…).</summary>
        public void FlushValues()
        {
            if (valuesDirty) SaveValues();
        }

        void SaveValues()
        {
            valuesDirty = false;
            var copy = new Dictionary<string, int>();
            lock (values)
                foreach (var kv in values)
                {
                    var d = NanoKontrol2.Get(kv.Key);
                    if (d != null && d.Kind != ControlKind.Button) copy[kv.Key] = kv.Value;
                }
            lock (AppConfig.Sync) Cfg.ControlValues = copy;
            Cfg.Save();
        }

        #region Cycle de vie

        public void Start()
        {
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "MidiSoundController.Engine" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public void Stop()
        {
            if (valuesTimer != null) { valuesTimer.Dispose(); valuesTimer = null; }
            FlushValues();
            CloseMidi();
            running = false;
            signal.Set();
            if (thread != null) thread.Join(1500);
        }

        void Loop()
        {
            audio = new AudioSystem();
            SyncLeds(true);
            while (running)
            {
                signal.WaitOne();
                while (running)
                {
                    int[] msgs = null;
                    Action w = null;
                    lock (qLock)
                    {
                        if (raw.Count > 0) { msgs = raw.ToArray(); raw.Clear(); }
                        else if (work.Count > 0) w = work.Dequeue();
                    }
                    if (msgs != null) { foreach (int m in msgs) Route(m); continue; }
                    if (w != null) { Safe(w); continue; }
                    if (pending.Count > 0)
                    {
                        var batch = new List<KeyValuePair<string, int>>(pending);
                        pending.Clear();
                        foreach (var kv in batch) Safe(() => ApplyVolume(kv.Key, kv.Value));
                        continue;
                    }
                    if (presses.Count > 0)
                    {
                        string id = presses.Dequeue();
                        Safe(() => Press(id));
                        continue;
                    }
                    break;
                }
            }
            audio.Dispose();
        }

        void Safe(Action a)
        {
            try
            {
                if (audioDirty) { audioDirty = false; audio.Invalidate(); }
                a();
            }
            catch
            {
                audio.Invalidate();
            }
        }

        /// <summary>Exécute du code sur le thread worker.</summary>
        public void Post(Action a)
        {
            lock (qLock) work.Enqueue(a);
            signal.Set();
        }

        /// <summary>Exécute une requête audio sur le worker et attend le résultat (utilisé par l'UI).</summary>
        public T Query<T>(Func<AudioSystem, T> f)
        {
            if (Thread.CurrentThread == thread) return f(audio);
            T result = default(T);
            var done = new ManualResetEvent(false);
            Post(() => { try { result = f(audio); } finally { done.Set(); } });
            done.WaitOne(4000);
            return result;
        }

        #endregion

        #region MIDI

        void OnRawMidi(int msg)
        {
            lock (qLock)
            {
                if (raw.Count < 4096) raw.Enqueue(msg);
            }
            signal.Set();
        }

        public void OpenMidi()
        {
            if (!Cfg.ModMidi) { CloseMidi(); return; }
            lock (midiLock)
            {
                CloseMidiLocked();
                var ins = MidiDevices.Inputs();
                var outs = MidiDevices.Outputs();
                midiSnapshot = string.Join("|", ins) + "#" + string.Join("|", outs);
                int i = Pick(ins, Cfg.MidiIn), o = Pick(outs, Cfg.MidiOut);
                if (i >= 0)
                {
                    try { midiIn = new MidiInput(i, OnRawMidi); MidiInName = ins[i]; }
                    catch { midiIn = null; }
                }
                if (o >= 0)
                {
                    try { midiOut = new MidiOutput(o); MidiOutName = outs[o]; }
                    catch { midiOut = null; }
                }
            }
            Post(() => SyncLeds(true));
            RaiseState();
        }

        public void CloseMidi()
        {
            lock (midiLock) CloseMidiLocked();
            RaiseState();
        }

        void CloseMidiLocked()
        {
            if (midiIn != null) { try { midiIn.Dispose(); } catch { } midiIn = null; }
            if (midiOut != null) { try { midiOut.Dispose(); } catch { } midiOut = null; }
            MidiInName = null;
            MidiOutName = null;
        }

        static int Pick(List<string> names, string want)
        {
            if (want == "-") return -1;
            if (!string.IsNullOrEmpty(want)) return names.IndexOf(want);
            return names.FindIndex(n => n.IndexOf("nanoKONTROL", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Appelé quand Windows signale un changement de périphérique (USB branché, carte son...).</summary>
        public void OnDeviceChange()
        {
            audioDirty = true;
            signal.Set();
            if (!Cfg.ModMidi) return;
            string snap = string.Join("|", MidiDevices.Inputs()) + "#" + string.Join("|", MidiDevices.Outputs());
            if (snap != midiSnapshot) OpenMidi();
        }

        void RaiseState()
        {
            var h = StateChanged;
            if (h != null) h();
        }

        void SendCc(int key, int value)
        {
            lock (midiLock)
            {
                if (midiOut != null) midiOut.Send(0xB0 | ((key >> 8) & 0x0F), key & 0x7F, value);
            }
        }

        #endregion

        #region Mapping

        public int KeyOf(string id)
        {
            lock (AppConfig.Sync)
            {
                int k;
                if (Cfg.CcOverrides.TryGetValue(id, out k)) return k;
            }
            var d = NanoKontrol2.Get(id);
            return d == null ? -1 : d.Cc;
        }

        void RebuildMap()
        {
            var map = new Dictionary<int, string>();
            foreach (var d in NanoKontrol2.All) map[KeyOf(d.Id)] = d.Id;
            keyMap = map;
        }

        /// <summary>À appeler après chaque modification de la config par l'UI.</summary>
        public void ConfigChanged()
        {
            RebuildMap();
            Cfg.Save();
            Post(() => SyncLeds(false));
        }

        public void ResetLearned()
        {
            lock (AppConfig.Sync) Cfg.CcOverrides.Clear();
            ConfigChanged();
        }

        void ApplyLearn(string id, int key)
        {
            int old = KeyOf(id);
            lock (AppConfig.Sync)
            {
                // Si un autre contrôle utilisait déjà ce CC, on échange les deux.
                foreach (var d in NanoKontrol2.All)
                {
                    if (d.Id == id || KeyOfLocked(d) != key) continue;
                    SetKeyLocked(d, old);
                }
                SetKeyLocked(NanoKontrol2.Get(id), key);
            }
            ConfigChanged();
        }

        int KeyOfLocked(ControlDef d)
        {
            int k;
            return Cfg.CcOverrides.TryGetValue(d.Id, out k) ? k : d.Cc;
        }

        void SetKeyLocked(ControlDef d, int key)
        {
            if (key == d.Cc) Cfg.CcOverrides.Remove(d.Id);
            else Cfg.CcOverrides[d.Id] = key;
        }

        HashSet<string> AssignedAppsLocked()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in Cfg.Controls.Values)
                foreach (var t in m.Targets)
                    if (t.Type == "app" && t.Id != null) set.Add(t.Id);
            return set;
        }

        /// <summary>Cibles réelles d'un contrôle (pour "mutestrip" : celles du fader de la tranche).</summary>
        List<Target> TargetsLocked(string id, string action)
        {
            string src = id;
            if (action == "mutestrip")
            {
                var d = NanoKontrol2.Get(id);
                if (d == null || d.Strip < 0) return new List<Target>();
                src = "F" + (d.Strip + 1);
            }
            ControlMapping m;
            return Cfg.Controls.TryGetValue(src, out m) ? new List<Target>(m.Targets) : new List<Target>();
        }

        /// <summary>Tranches de routage visées par une liste de cibles (vide si le module est désactivé).</summary>
        List<RouteNode> RouteNodes(List<Target> targets)
        {
            var list = new List<RouteNode>();
            if (Router == null) return list;
            lock (AppConfig.Sync)
                foreach (var t in targets)
                {
                    var n = Cfg.Router.Find(t.Type, t.Id);
                    if (n != null) list.Add(n);
                }
            return list;
        }

        public static bool IsLatching(string action)
        {
            return action == "mute" || action == "mutestrip" || action == "default";
        }

        #endregion

        #region Traitement (thread worker)

        void Route(int msg)
        {
            int status = msg & 0xFF, d1 = (msg >> 8) & 0x7F, d2 = (msg >> 16) & 0x7F;
            if ((status & 0xF0) != 0xB0) return; // seuls les Control Change nous intéressent
            int key = ((status & 0x0F) << 8) | d1;

            string learn = LearnControl;
            if (learn == null) learnSeen.Clear();
            else
            {
                // On n'apprend que sur un geste franc (bouton appuyé puis relâché, ou fader réellement bougé).
                int first;
                if (!learnSeen.TryGetValue(key, out first)) { learnSeen[key] = d2; return; }
                if (Math.Abs(d2 - first) < 6) return;
                learnSeen.Clear();
                LearnControl = null;
                ApplyLearn(learn, key);
                var lh = Learned;
                if (lh != null) lh(learn);
                return;
            }

            string id;
            if (!keyMap.TryGetValue(key, out id)) return;
            var def = NanoKontrol2.Get(id);
            if (def.Kind != ControlKind.Button)
            {
                // La position physique est toujours mémorisée (affichage + prochain démarrage)…
                bool changed;
                lock (values) { int old; changed = !values.TryGetValue(id, out old) || old != d2; values[id] = d2; }
                if (changed) SaveValuesSoon();
                // …mais seul un mouvement franc change le son ou sélectionne le contrôle.
                if (!AcceptMotion(id, d2))
                {
                    var rh = ControlMoved;
                    if (rh != null && changed) rh(null);
                    return;
                }
            }
            else lock (values) values[id] = d2;

            if (def.Kind == ControlKind.Button)
            {
                if (d2 > 0) presses.Enqueue(id);
                else if (!IsLatching(ActionOf(id))) SetLed(id, false, false);
            }
            else pending[id] = d2;

            var h = ControlMoved;
            if (h != null) h(id);
        }

        sealed class Motion
        {
            public int Anchor = -1;
            public int Until = Environment.TickCount;
        }

        readonly Dictionary<string, Motion> motion = new Dictionary<string, Motion>();
        readonly Dictionary<int, int> learnSeen = new Dictionary<int, int>();
        const int MotionWindowMs = 300;

        /// <summary>
        /// Filtre anti-tremblement. Un fader au repos oscille souvent de ±1 ou ±2 : ces petites variations sont ignorées.
        /// Dès qu'un mouvement franc (écart ≥ seuil) est détecté, on suit le fader finement pendant le geste,
        /// puis il se reverrouille 300 ms après le dernier mouvement franc.
        /// </summary>
        bool AcceptMotion(string id, int v)
        {
            int threshold = Cfg.Jitter;
            if (threshold <= 1) return true;
            Motion m;
            if (!motion.TryGetValue(id, out m)) { m = new Motion(); motion[id] = m; }
            if (m.Anchor < 0)
            {
                m.Anchor = v; // première valeur reçue : simple référence, pas un mouvement
                return false;
            }
            int now = Environment.TickCount;
            if (Math.Abs(v - m.Anchor) >= threshold) m.Until = now + MotionWindowMs;
            else if (unchecked(now - m.Until) >= 0) return false;
            m.Anchor = v;
            return true;
        }

        string ActionOf(string id)
        {
            lock (AppConfig.Sync)
            {
                ControlMapping m;
                return Cfg.Controls.TryGetValue(id, out m) ? (m.Action ?? "") : "";
            }
        }

        void ApplyVolume(string id, int v)
        {
            List<Target> targets;
            HashSet<string> assigned;
            double curve;
            lock (AppConfig.Sync)
            {
                ControlMapping m;
                if (!Cfg.Controls.TryGetValue(id, out m) || m.Targets.Count == 0) return;
                targets = new List<Target>(m.Targets);
                assigned = AssignedAppsLocked();
                curve = Cfg.Curve;
            }
            float level = (float)Math.Pow(v / 127.0, curve);
            var routes = RouteNodes(targets);
            if (routes.Count > 0)
            {
                double db = Db.FromLevel(level);
                lock (AppConfig.Sync) foreach (var n in routes) n.Gain = db;
                var r = Router;
                if (r != null) r.SaveSoon();
            }
            foreach (var h in audio.Resolve(targets, assigned)) audio.SetVolume(h, level);
        }

        void Press(string id)
        {
            string action;
            List<Target> targets;
            HashSet<string> assigned;
            lock (AppConfig.Sync)
            {
                ControlMapping m;
                action = Cfg.Controls.TryGetValue(id, out m) ? (m.Action ?? "") : "";
                targets = TargetsLocked(id, action);
                assigned = AssignedAppsLocked();
            }
            if (!IsLatching(action)) SetLed(id, true, false);

            switch (action)
            {
                case "mute":
                case "mutestrip":
                    {
                        var hs = audio.Resolve(targets, assigned);
                        var routes = RouteNodes(targets);
                        if (hs.Count == 0 && routes.Count == 0) break;
                        bool mute = !(hs.Count > 0 ? audio.GetMute(hs[0]) : routes[0].Mute);
                        foreach (var h in hs) audio.SetMute(h, mute);
                        if (routes.Count > 0)
                        {
                            lock (AppConfig.Sync) foreach (var n in routes) n.Mute = mute;
                            var r = Router;
                            if (r != null) r.SaveSoon();
                        }
                        SyncLeds(false);
                        break;
                    }
                case "default":
                    {
                        var t = targets.Find(x => x.Type == "device");
                        if (t != null) { audio.SetDefault(t.Id); SyncLeds(false); }
                        break;
                    }
                case "media_play": Native.PressKey(0xB3); break;
                case "media_next": Native.PressKey(0xB0); break;
                case "media_prev": Native.PressKey(0xB1); break;
                case "media_stop": Native.PressKey(0xB2); break;
                case "media_mute": Native.PressKey(0xAD); break;
                case "profile_next": CycleProfile(1); break;
                case "profile_prev": CycleProfile(-1); break;
            }
        }

        void CycleProfile(int dir)
        {
            string name;
            lock (AppConfig.Sync)
            {
                int n = Cfg.Profiles.Count;
                if (n < 2) return;
                int i = Cfg.Profiles.IndexOf(Cfg.FindProfile(Cfg.ActiveProfile));
                name = Cfg.Profiles[((i + dir) % n + n) % n].Name;
            }
            SwitchProfile(name);
        }

        /// <summary>Change de profil actif (depuis l'UI, le menu de notification ou un bouton du contrôleur).</summary>
        public void SwitchProfile(string name)
        {
            lock (AppConfig.Sync)
            {
                if (!Cfg.SetActive(name)) return;
            }
            ConfigChanged();
            var h = ProfileChanged;
            if (h != null) h();
        }

        /// <summary>Recalcule l'état de toutes les LED (muet / périphérique par défaut).</summary>
        void SyncLeds(bool force)
        {
            var items = new List<KeyValuePair<string, List<Target>>>();
            var actions = new Dictionary<string, string>();
            HashSet<string> assigned;
            lock (AppConfig.Sync)
            {
                foreach (var d in NanoKontrol2.All)
                {
                    if (d.Kind != ControlKind.Button) continue;
                    ControlMapping m;
                    string a = Cfg.Controls.TryGetValue(d.Id, out m) ? (m.Action ?? "") : "";
                    actions[d.Id] = a;
                    items.Add(new KeyValuePair<string, List<Target>>(d.Id, TargetsLocked(d.Id, a)));
                }
                assigned = AssignedAppsLocked();
            }
            string defOut = null, defIn = null;
            bool defLoaded = false;
            foreach (var kv in items)
            {
                bool on = false;
                try
                {
                    string a = actions[kv.Key];
                    if (a == "mute" || a == "mutestrip")
                    {
                        var hs = audio.Resolve(kv.Value, assigned);
                        var routes = RouteNodes(kv.Value);
                        on = hs.Count > 0 ? audio.GetMute(hs[0]) : routes.Count > 0 && routes[0].Mute;
                    }
                    else if (a == "default")
                    {
                        if (!defLoaded) { defOut = audio.DefaultId(Flow.Render); defIn = audio.DefaultId(Flow.Capture); defLoaded = true; }
                        var t = kv.Value.Find(x => x.Type == "device");
                        on = t != null && (t.Id == defOut || t.Id == defIn);
                    }
                }
                catch { }
                SetLed(kv.Key, on, force);
            }
        }

        public void RequestLedSync()
        {
            Post(() => SyncLeds(false));
        }

        void SetLed(string id, bool on, bool force)
        {
            bool changed;
            lock (leds)
            {
                bool old;
                changed = force || !leds.TryGetValue(id, out old) || old != on;
                leds[id] = on;
            }
            if (!changed) return;
            if (Cfg.LedFeedback) SendCc(KeyOf(id), on ? 127 : 0);
            else if (force) SendCc(KeyOf(id), 0);
            var h = ControlMoved;
            if (h != null) h(null);
        }

        #endregion

        #region Lecture d'état pour l'UI

        public int GetValue(string id)
        {
            lock (values)
            {
                int v;
                return values.TryGetValue(id, out v) ? v : -1;
            }
        }

        public bool GetLed(string id)
        {
            lock (leds)
            {
                bool v;
                return leds.TryGetValue(id, out v) && v;
            }
        }

        #endregion
    }
}
