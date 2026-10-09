using System.Collections.Generic;

namespace ControlCenterK
{
    /// <summary>Réglages du module « FPS » : programmes surveillés et apparence de l'affichage.</summary>
    public class FpsConfig
    {
        /// <summary>Programmes sur lesquels l'affichage apparaît (nom de l'exécutable sans ".exe", en minuscules).</summary>
        public List<string> Apps { get; set; }
        public string Color { get; set; }       // couleur du texte et de la courbe (#RRGGBB)
        public int Size { get; set; }           // taille du nombre de FPS (points)
        public string Corner { get; set; }      // tl, tr, bl, br : coin de la fenêtre du jeu
        public int OffsetX { get; set; }        // décalage depuis le coin (pixels)
        public int OffsetY { get; set; }
        public bool Graph { get; set; }         // courbe sous le nombre
        public int GraphSeconds { get; set; }   // durée affichée par la courbe
        public int Background { get; set; }     // opacité du fond (0..100 %)

        public FpsConfig()
        {
            Apps = new List<string>();
            Color = "#3DFF7A";
            Size = 18;
            Corner = "tl";
            OffsetX = 12;
            OffsetY = 12;
            Graph = true;
            GraphSeconds = 10;
            Background = 55;
        }

        public void Fix()
        {
            if (Apps == null) Apps = new List<string>();
            for (int i = Apps.Count - 1; i >= 0; i--)
                if (string.IsNullOrEmpty(Apps[i])) Apps.RemoveAt(i); else Apps[i] = Apps[i].ToLowerInvariant();
            if (string.IsNullOrEmpty(Color)) Color = "#3DFF7A";
            if (Size < 8 || Size > 72) Size = 18;
            if (Corner != "tl" && Corner != "tr" && Corner != "bl" && Corner != "br") Corner = "tl";
            if (OffsetX < 0 || OffsetX > 2000) OffsetX = 12;
            if (OffsetY < 0 || OffsetY > 2000) OffsetY = 12;
            if (GraphSeconds < 2 || GraphSeconds > 120) GraphSeconds = 10;
            if (Background < 0 || Background > 100) Background = 55;
        }
    }
}
