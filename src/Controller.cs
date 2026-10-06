using System.Collections.Generic;
using System.Drawing;

namespace ControlCenterK
{
    public enum ControlKind { Fader, Knob, Button }

    public sealed class ControlDef
    {
        public string Id;
        public string Label;
        public string Caption;     // texte imprimé sur le bouton
        public ControlKind Kind;
        public int Cc;             // CC par défaut (canal 1) en mode CC d'usine du nanoKONTROL2
        public int Strip = -1;     // tranche 0..7, -1 pour la section transport
        public RectangleF Rect;    // position dans le dessin logique (1000 x 300)
    }

    /// <summary>Disposition et mapping d'usine du Korg nanoKONTROL2.</summary>
    public static class NanoKontrol2
    {
        public const float W = 1000f, H = 300f, StripX = 302f, StripW = 86f;

        public static readonly List<ControlDef> All = Build();
        static readonly Dictionary<string, ControlDef> byId = Index();

        public static ControlDef Get(string id)
        {
            ControlDef d;
            return id != null && byId.TryGetValue(id, out d) ? d : null;
        }

        static Dictionary<string, ControlDef> Index()
        {
            var d = new Dictionary<string, ControlDef>();
            foreach (var c in All) d[c.Id] = c;
            return d;
        }

        static List<ControlDef> Build()
        {
            var l = new List<ControlDef>();
            for (int i = 0; i < 8; i++)
            {
                float x = StripX + i * StripW;
                int n = i + 1;
                l.Add(new ControlDef { Id = "K" + n, Label = "Knob " + n, Kind = ControlKind.Knob, Cc = 16 + i, Strip = i, Rect = new RectangleF(x + 23, 44, 40, 40) });
                l.Add(new ControlDef { Id = "S" + n, Label = "Bouton S " + n, Caption = "S", Kind = ControlKind.Button, Cc = 32 + i, Strip = i, Rect = new RectangleF(x + 6, 108, 30, 24) });
                l.Add(new ControlDef { Id = "M" + n, Label = "Bouton M " + n, Caption = "M", Kind = ControlKind.Button, Cc = 48 + i, Strip = i, Rect = new RectangleF(x + 6, 158, 30, 24) });
                l.Add(new ControlDef { Id = "R" + n, Label = "Bouton R " + n, Caption = "R", Kind = ControlKind.Button, Cc = 64 + i, Strip = i, Rect = new RectangleF(x + 6, 208, 30, 24) });
                l.Add(new ControlDef { Id = "F" + n, Label = "Fader " + n, Kind = ControlKind.Fader, Cc = i, Strip = i, Rect = new RectangleF(x + 44, 96, 38, 190) });
            }
            l.Add(Btn("TRK_PREV", "Track ◀", "◀", 58, 25, 58, 52, 26));
            l.Add(Btn("TRK_NEXT", "Track ▶", "▶", 59, 85, 58, 52, 26));
            l.Add(Btn("CYCLE", "Cycle", "↻", 46, 25, 132, 52, 26));
            l.Add(Btn("MRK_SET", "Marker Set", "SET", 60, 105, 132, 52, 26));
            l.Add(Btn("MRK_PREV", "Marker ◀", "◀", 61, 165, 132, 52, 26));
            l.Add(Btn("MRK_NEXT", "Marker ▶", "▶", 62, 225, 132, 52, 26));
            l.Add(Btn("REW", "Retour rapide", "◀◀", 43, 25, 212, 46, 30));
            l.Add(Btn("FF", "Avance rapide", "▶▶", 44, 78, 212, 46, 30));
            l.Add(Btn("STOP", "Stop", "■", 42, 131, 212, 46, 30));
            l.Add(Btn("PLAY", "Play", "▶", 41, 184, 212, 46, 30));
            l.Add(Btn("REC", "Rec", "●", 45, 237, 212, 46, 30));
            return l;
        }

        static ControlDef Btn(string id, string label, string cap, int cc, float x, float y, float w, float h)
        {
            return new ControlDef { Id = id, Label = label, Caption = cap, Kind = ControlKind.Button, Cc = cc, Rect = new RectangleF(x, y, w, h) };
        }
    }
}
