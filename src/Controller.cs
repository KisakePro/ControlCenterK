using System;
using System.Collections.Generic;
using System.Drawing;

namespace ControlCenterK
{
    public enum ControlKind { Fader, Knob, Button }
    public enum MsgType { Cc = 0, Note = 1 }

    /// <summary>
    /// Clé MIDI d'un contrôle : type (CC / Note), canal (0-15, ou 16 = n'importe quel canal) et numéro.
    /// </summary>
    public static class MidiKey
    {
        public const int AnyChannel = 16;

        public static int Make(MsgType type, int channel, int number)
        {
            return ((int)type << 16) | ((channel < 0 || channel > 15 ? AnyChannel : channel) << 8) | (number & 0x7F);
        }

        public static MsgType Type(int key) { return (MsgType)((key >> 16) & 1); }
        public static int Channel(int key) { return (key >> 8) & 0x1F; }
        public static int Number(int key) { return key & 0x7F; }
        public static int AnyChannelOf(int key) { return Make(Type(key), AnyChannel, Number(key)); }

        public static string Describe(int key)
        {
            if (key < 0) return "non appris — utilisez MIDI learn";
            int ch = Channel(key);
            return (Type(key) == MsgType.Note ? "Note " : "CC ") + Number(key) + "  ·  " + (ch == AnyChannel ? "tous canaux" : "Canal " + (ch + 1));
        }
    }

    public sealed class ControlDef
    {
        public string Id;
        public string Label;
        public string Caption;      // texte imprimé sur le bouton
        public ControlKind Kind;
        public MsgType Msg;
        public int Number = -1;     // numéro CC / note d'usine (-1 : à apprendre)
        public int Channel = -1;    // canal d'usine (-1 : n'importe lequel)
        public int Strip = -1;      // tranche (0..n), -1 hors tranche
        public string Fader;        // fader de la même tranche (action « Muet : basculer le fader »)
        public bool Pad;            // pad carré (style MPC / grille)
        public RectangleF Rect;     // position dans le dessin logique du modèle
        public RectangleF LabelRect;// zone où afficher le nom de la cible (vide = aucune)

        public int DefaultKey { get { return Number < 0 ? -1 : MidiKey.Make(Msg, Channel, Number); } }
    }

    /// <summary>Un modèle de contrôleur : dessin, contrôles, mapping d'usine, détection, LED.</summary>
    public sealed class ControllerModel
    {
        public string Id, Name, Title;
        public float W, H;
        public string[] PortMatch = new string[0];  // morceaux du nom du port MIDI pour la détection automatique
        public int LedOn = 127;                     // valeur envoyée pour allumer une LED
        public int LedChannel = 0;                  // canal des LED quand le contrôle accepte tous les canaux
        public string Note;                         // remarque affichée dans l'éditeur (mode à activer…)
        public readonly List<ControlDef> Controls = new List<ControlDef>();
        public readonly List<KeyValuePair<RectangleF, string>> Texts = new List<KeyValuePair<RectangleF, string>>();
        public readonly List<RectangleF> Lines = new List<RectangleF>(); // lignes de séparation (x1, y1, x2, y2)
        public RectangleF TitleRect = new RectangleF(25, 10, 300, 24);
        readonly Dictionary<string, ControlDef> byId = new Dictionary<string, ControlDef>();

        public ControlDef Get(string id)
        {
            ControlDef d;
            return id != null && byId.TryGetValue(id, out d) ? d : null;
        }

        public ControlDef Add(ControlDef d)
        {
            Controls.Add(d);
            byId[d.Id] = d;
            return d;
        }

        public void Text(float x, float y, float w, string text) { Texts.Add(new KeyValuePair<RectangleF, string>(new RectangleF(x, y, w, 16), text)); }

        public ControlDef FirstFader
        {
            get { return Controls.Find(c => c.Kind == ControlKind.Fader) ?? Controls.Find(c => c.Kind == ControlKind.Knob) ?? Controls[0]; }
        }
    }

    /// <summary>Catalogue des contrôleurs pris en charge.</summary>
    public static class Controllers
    {
        public static readonly List<ControllerModel> All = new List<ControllerModel>
        {
            NanoKontrol2(), XTouchMini(), LaunchControlXL(), ApcMini(false), ApcMini(true),
            Lpd8(false), Lpd8(true), MpkMini(false), MpkMini(true), Generic(),
        };

        static volatile ControllerModel current = All[0];

        /// <summary>Modèle actuellement utilisé.</summary>
        public static ControllerModel Current { get { return current; } set { current = value ?? All[0]; } }

        public static ControllerModel Find(string id) { return All.Find(m => m.Id == id); }

        public static ControlDef Get(string id) { return current.Get(id); }

        /// <summary>Modèle correspondant au nom d'un port MIDI (le plus spécifique d'abord), ou null.</summary>
        public static ControllerModel Detect(IEnumerable<string> portNames)
        {
            ControllerModel best = null;
            int bestLen = 0;
            foreach (var port in portNames)
                foreach (var m in All)
                    foreach (var pat in m.PortMatch)
                        if (port.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0 && pat.Length > bestLen) { best = m; bestLen = pat.Length; }
            return best;
        }

        static ControlDef Btn(string id, string label, string cap, MsgType msg, int num, float x, float y, float w, float h, int ch = -1)
        {
            return new ControlDef { Id = id, Label = label, Caption = cap, Kind = ControlKind.Button, Msg = msg, Number = num, Channel = ch, Rect = new RectangleF(x, y, w, h) };
        }

        #region Korg nanoKONTROL2

        static ControllerModel NanoKontrol2()
        {
            var m = new ControllerModel { Id = "nanokontrol2", Name = "Korg nanoKONTROL2", Title = "nanoKONTROL2", W = 1000, H = 300, PortMatch = new[] { "nanoKONTROL2" },
                Note = "Pour les LED : « LED Mode : External » dans KORG Kontrol Editor." };
            const float sx = 302, sw = 86;
            for (int i = 0; i < 8; i++)
            {
                float x = sx + i * sw;
                int n = i + 1;
                string f = "F" + n;
                m.Add(new ControlDef { Id = "K" + n, Label = "Knob " + n, Kind = ControlKind.Knob, Number = 16 + i, Channel = 0, Strip = i, Fader = f, Rect = new RectangleF(x + 23, 44, 40, 40) });
                m.Add(new ControlDef { Id = "S" + n, Label = "Bouton S " + n, Caption = "S", Kind = ControlKind.Button, Number = 32 + i, Channel = 0, Strip = i, Fader = f, Rect = new RectangleF(x + 6, 108, 30, 24) });
                m.Add(new ControlDef { Id = "M" + n, Label = "Bouton M " + n, Caption = "M", Kind = ControlKind.Button, Number = 48 + i, Channel = 0, Strip = i, Fader = f, Rect = new RectangleF(x + 6, 158, 30, 24) });
                m.Add(new ControlDef { Id = "R" + n, Label = "Bouton R " + n, Caption = "R", Kind = ControlKind.Button, Number = 64 + i, Channel = 0, Strip = i, Fader = f, Rect = new RectangleF(x + 6, 208, 30, 24) });
                m.Add(new ControlDef { Id = f, Label = "Fader " + n, Kind = ControlKind.Fader, Number = i, Channel = 0, Strip = i, Fader = f,
                    Rect = new RectangleF(x + 44, 96, 38, 190), LabelRect = new RectangleF(x + 4, 10, 78, 24) });
            }
            m.Add(Btn("TRK_PREV", "Track ◀", "◀", MsgType.Cc, 58, 25, 58, 52, 26, 0));
            m.Add(Btn("TRK_NEXT", "Track ▶", "▶", MsgType.Cc, 59, 85, 58, 52, 26, 0));
            m.Add(Btn("CYCLE", "Cycle", "↻", MsgType.Cc, 46, 25, 132, 52, 26, 0));
            m.Add(Btn("MRK_SET", "Marker Set", "SET", MsgType.Cc, 60, 105, 132, 52, 26, 0));
            m.Add(Btn("MRK_PREV", "Marker ◀", "◀", MsgType.Cc, 61, 165, 132, 52, 26, 0));
            m.Add(Btn("MRK_NEXT", "Marker ▶", "▶", MsgType.Cc, 62, 225, 132, 52, 26, 0));
            m.Add(Btn("REW", "Retour rapide", "◀◀", MsgType.Cc, 43, 25, 212, 46, 30, 0));
            m.Add(Btn("FF", "Avance rapide", "▶▶", MsgType.Cc, 44, 78, 212, 46, 30, 0));
            m.Add(Btn("STOP", "Stop", "■", MsgType.Cc, 42, 131, 212, 46, 30, 0));
            m.Add(Btn("PLAY", "Play", "▶", MsgType.Cc, 41, 184, 212, 46, 30, 0));
            m.Add(Btn("REC", "Rec", "●", MsgType.Cc, 45, 237, 212, 46, 30, 0));
            m.Text(25, 38, 120, "TRACK");
            m.Text(25, 112, 60, "CYCLE");
            m.Text(105, 112, 120, "MARKER");
            m.Text(25, 192, 200, "TRANSPORT");
            m.Lines.Add(new RectangleF(292, 14, 292, 286));
            return m;
        }

        #endregion

        #region Behringer X-Touch Mini (mode standard, couche A)

        static ControllerModel XTouchMini()
        {
            var m = new ControllerModel { Id = "xtouchmini", Name = "Behringer X-Touch Mini", Title = "X-TOUCH MINI", W = 900, H = 330,
                PortMatch = new[] { "X-TOUCH MINI" }, LedOn = 1,
                Note = "Mode standard (MC désactivé), couche A. Couche B ou autre réglage : utilisez « Tout apprendre »." };
            for (int i = 0; i < 8; i++)
            {
                float x = 40 + i * 90;
                int n = i + 1;
                m.Add(new ControlDef { Id = "xtm.E" + n, Label = "Encodeur " + n, Kind = ControlKind.Knob, Number = 1 + i, Strip = i, Rect = new RectangleF(x + 4, 48, 48, 48),
                    LabelRect = new RectangleF(x - 2, 20, 60, 20) });
                m.Add(Btn("xtm.P" + n, "Poussoir encodeur " + n, "•", MsgType.Note, i, x + 16, 102, 24, 16));
                m.Add(Btn("xtm.A" + n, "Bouton haut " + n, n.ToString(), MsgType.Note, 8 + i, x, 140, 56, 40));
                m.Add(Btn("xtm.B" + n, "Bouton bas " + n, (n + 8).ToString(), MsgType.Note, 16 + i, x, 196, 56, 40));
            }
            m.Add(new ControlDef { Id = "xtm.F", Label = "Fader", Kind = ControlKind.Fader, Number = 9, Rect = new RectangleF(800, 40, 50, 250),
                LabelRect = new RectangleF(780, 296, 90, 22) });
            m.Lines.Add(new RectangleF(760, 30, 760, 300));
            m.TitleRect = new RectangleF(40, 260, 300, 24);
            return m;
        }

        #endregion

        #region Novation Launch Control XL (modèle d'usine)

        static ControllerModel LaunchControlXL()
        {
            var m = new ControllerModel { Id = "launchcontrolxl", Name = "Novation Launch Control XL", Title = "Launch Control XL", W = 1000, H = 560,
                PortMatch = new[] { "Launch Control XL" }, LedOn = 15, LedChannel = 8,
                Note = "Modèle d'usine 1 (Factory Template 1)." };
            int[] rows = { 13, 29, 49 };
            for (int i = 0; i < 8; i++)
            {
                float x = 40 + i * 100;
                int n = i + 1;
                string f = "lcx.F" + n;
                for (int r = 0; r < 3; r++)
                    m.Add(new ControlDef { Id = "lcx.K" + (r + 1) + n, Label = (r == 2 ? "Pan " : "Envoi " + (char)('A' + r) + " ") + n, Kind = ControlKind.Knob,
                        Number = rows[r] + i, Strip = i, Fader = f, Rect = new RectangleF(x + 6, 46 + r * 62, 44, 44) });
                m.Add(new ControlDef { Id = f, Label = "Fader " + n, Kind = ControlKind.Fader, Number = 77 + i, Strip = i, Fader = f,
                    Rect = new RectangleF(x + 10, 250, 36, 196), LabelRect = new RectangleF(x - 2, 226, 60, 20) });
                int focus = i < 4 ? 41 + i : 57 + (i - 4), ctrl = i < 4 ? 73 + i : 89 + (i - 4);
                var b1 = m.Add(Btn("lcx.T" + n, "Track Focus " + n, "", MsgType.Note, focus, x + 2, 458, 52, 30));
                var b2 = m.Add(Btn("lcx.C" + n, "Track Control " + n, "", MsgType.Note, ctrl, x + 2, 498, 52, 30));
                b1.Strip = b2.Strip = i;
                b1.Fader = b2.Fader = f;
            }
            m.Add(Btn("lcx.UP", "Send ▲", "▲", MsgType.Cc, 104, 860, 60, 50, 30));
            m.Add(Btn("lcx.DOWN", "Send ▼", "▼", MsgType.Cc, 105, 860, 100, 50, 30));
            m.Add(Btn("lcx.LEFT", "Track ◀", "◀", MsgType.Cc, 106, 860, 160, 50, 30));
            m.Add(Btn("lcx.RIGHT", "Track ▶", "▶", MsgType.Cc, 107, 920, 160, 50, 30));
            m.Add(Btn("lcx.DEVICE", "Device", "DEV", MsgType.Note, 105, 880, 360, 70, 26));
            m.Add(Btn("lcx.MUTE", "Mute", "MUTE", MsgType.Note, 106, 880, 396, 70, 26));
            m.Add(Btn("lcx.SOLO", "Solo", "SOLO", MsgType.Note, 107, 880, 432, 70, 26));
            m.Add(Btn("lcx.ARM", "Record Arm", "ARM", MsgType.Note, 108, 880, 468, 70, 26));
            m.Lines.Add(new RectangleF(840, 40, 840, 530));
            m.TitleRect = new RectangleF(40, 14, 300, 24);
            return m;
        }

        #endregion

        #region Akai APC mini / APC mini mk2

        static ControllerModel ApcMini(bool mk2)
        {
            var m = new ControllerModel
            {
                Id = mk2 ? "apcminimk2" : "apcmini", Name = mk2 ? "Akai APC mini mk2" : "Akai APC mini", Title = mk2 ? "APC mini mk2" : "APC mini",
                W = 640, H = 830, PortMatch = mk2 ? new[] { "APC mini mk2" } : new[] { "APC MINI" }, LedOn = mk2 ? 21 : 1,
            };
            string p = mk2 ? "apc2." : "apc.";
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    int note = (7 - r) * 8 + c;
                    m.Add(new ControlDef { Id = p + "P" + note, Label = "Pad " + (note + 1), Caption = "", Kind = ControlKind.Button, Pad = true,
                        Msg = MsgType.Note, Number = note, Rect = new RectangleF(30 + c * 62, 50 + r * 62, 54, 54) });
                }
            for (int r = 0; r < 8; r++)
                m.Add(Btn(p + "S" + (r + 1), "Scène " + (r + 1), "▶", MsgType.Note, (mk2 ? 112 : 82) + r, 538, 62 + r * 62, 30, 30));
            for (int c = 0; c < 8; c++)
            {
                string f = p + "F" + (c + 1);
                var b = m.Add(Btn(p + "T" + (c + 1), "Bouton piste " + (c + 1), "", MsgType.Note, (mk2 ? 100 : 64) + c, 30 + c * 62, 552, 54, 22));
                b.Strip = c;
                b.Fader = f;
                m.Add(new ControlDef { Id = f, Label = "Fader " + (c + 1), Kind = ControlKind.Fader, Number = 48 + c, Strip = c, Fader = f,
                    Rect = new RectangleF(38 + c * 62, 590, 38, 200), LabelRect = new RectangleF(28 + c * 62, 796, 58, 20) });
            }
            m.Add(Btn(p + "SHIFT", "Shift", "SHIFT", MsgType.Note, mk2 ? 122 : 98, 530, 552, 46, 22));
            m.Add(new ControlDef { Id = p + "F9", Label = "Fader master", Kind = ControlKind.Fader, Number = 56, Rect = new RectangleF(534, 590, 38, 200),
                LabelRect = new RectangleF(524, 796, 58, 20) });
            m.TitleRect = new RectangleF(30, 14, 300, 24);
            return m;
        }

        #endregion

        #region Akai LPD8 / MPK mini (pads + potentiomètres)

        static ControllerModel PadsAndKnobs(string id, string name, string title, string prefix, int knobCc, int[] padNotes, string[] ports, string note)
        {
            var m = new ControllerModel { Id = id, Name = name, Title = title, W = 820, H = 300, PortMatch = ports, Note = note };
            for (int i = 0; i < 8; i++)
            {
                int row = i < 4 ? 1 : 0, col = i % 4;  // pads 1-4 en bas, 5-8 en haut
                m.Add(new ControlDef { Id = prefix + "P" + (i + 1), Label = "Pad " + (i + 1), Caption = (i + 1).ToString(), Kind = ControlKind.Button, Pad = true,
                    Msg = MsgType.Note, Number = padNotes[i], Rect = new RectangleF(30 + col * 100, 46 + row * 120, 88, 88) });
            }
            for (int i = 0; i < 8; i++)
            {
                int row = i / 4, col = i % 4;
                m.Add(new ControlDef { Id = prefix + "K" + (i + 1), Label = "Potentiomètre " + (i + 1), Kind = ControlKind.Knob, Number = knobCc + i,
                    Rect = new RectangleF(470 + col * 85, 60 + row * 120, 52, 52), LabelRect = new RectangleF(456 + col * 85, 118 + row * 120, 80, 18) });
            }
            m.Lines.Add(new RectangleF(440, 30, 440, 280));
            return m;
        }

        static ControllerModel Lpd8(bool mk2)
        {
            return mk2
                ? PadsAndKnobs("lpd8mk2", "Akai LPD8 mk2", "LPD8 mk2", "lpd2.", 70, new[] { 36, 37, 38, 39, 40, 41, 42, 43 }, new[] { "LPD8 mk2", "LPD8 MK2" }, "Programme 1.")
                : PadsAndKnobs("lpd8", "Akai LPD8", "LPD8", "lpd.", 1, new[] { 36, 37, 38, 39, 40, 41, 42, 43 }, new[] { "LPD8" }, "Programme 1 (PROG 1).");
        }

        static ControllerModel MpkMini(bool mk3)
        {
            return mk3
                ? PadsAndKnobs("mpkminimk3", "Akai MPK mini mk3", "MPK mini 3", "mpk3.", 70, new[] { 36, 37, 38, 39, 40, 41, 42, 43 }, new[] { "MPK mini 3", "MPK mini mk3" }, "Banque A des pads ; le clavier n'est pas utilisé.")
                : PadsAndKnobs("mpkminimk2", "Akai MPK mini mk2", "MPK mini mk2", "mpk2.", 1, new[] { 36, 37, 38, 39, 40, 41, 42, 43 }, new[] { "MPK mini", "MPKmini" }, "Programme 1, banque A ; le clavier n'est pas utilisé.");
        }

        #endregion

        #region Générique

        static ControllerModel Generic()
        {
            var m = new ControllerModel { Id = "generic", Name = "Autre contrôleur (générique)", Title = "Contrôleur générique", W = 900, H = 300,
                Note = "Aucun mapping d'usine : utilisez « Tout apprendre » pour associer chaque contrôle de votre appareil." };
            for (int i = 0; i < 8; i++)
            {
                float x = 40 + i * 105;
                int n = i + 1;
                string f = "gen.F" + n;
                m.Add(new ControlDef { Id = "gen.K" + n, Label = "Potentiomètre " + n, Kind = ControlKind.Knob, Strip = i, Fader = f, Rect = new RectangleF(x + 28, 44, 40, 40) });
                m.Add(new ControlDef { Id = "gen.A" + n, Label = "Bouton A " + n, Caption = "A", Kind = ControlKind.Button, Strip = i, Fader = f, Rect = new RectangleF(x + 4, 108, 30, 24) });
                m.Add(new ControlDef { Id = "gen.B" + n, Label = "Bouton B " + n, Caption = "B", Kind = ControlKind.Button, Strip = i, Fader = f, Rect = new RectangleF(x + 4, 158, 30, 24) });
                m.Add(new ControlDef { Id = f, Label = "Fader " + n, Kind = ControlKind.Fader, Strip = i, Fader = f,
                    Rect = new RectangleF(x + 50, 96, 38, 190), LabelRect = new RectangleF(x + 4, 10, 92, 24) });
            }
            m.TitleRect = new RectangleF(40, 270, 300, 24);
            return m;
        }

        #endregion
    }
}
