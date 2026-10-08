using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Liste des périphériques enregistrés mais débranchés, avec suppression confirmée de leurs réglages.</summary>
    static class SavedDevices
    {
        /// <summary>Ajoute une ligne par périphérique (clé "vid:pid" → nom) et renvoie le Y suivant.</summary>
        public static int Fill(Card c, int y, List<KeyValuePair<string, string>> items, string glyph, string kind, Action<string> remove)
        {
            foreach (var it in items)
            {
                string key = it.Key, name = it.Value;
                var row = new Card { BackColor = Theme.Surface, Radius = 6,
                    Bounds = new Rectangle(Theme.S(14), y, c.Width - Theme.S(28), Theme.S(44)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                var icon = Theme.Label(glyph, Theme.Icon(12f), Theme.Dim, row.BackColor);
                icon.Location = new Point(Theme.S(12), Theme.S(13));
                var lName = Theme.Label(name, Theme.Semi(10f), Theme.Muted, row.BackColor);
                lName.Location = new Point(Theme.S(42), Theme.S(5));
                var st = Theme.Label("Non connecté · " + key, Theme.Ui(8.5f), Theme.Dim, row.BackColor);
                st.Location = new Point(Theme.S(42), Theme.S(24));
                var del = new FlatButton("Supprimer") { Glyph = Glyphs.Close, Anchor = AnchorStyles.Top | AnchorStyles.Right };
                del.FitWidth();
                del.Location = new Point(row.Width - del.Width - Theme.S(8), Theme.S(6));
                del.Click += (s, e) =>
                {
                    if (MessageBox.Show(c.FindForm(), "Supprimer les réglages enregistrés pour « " + name + " » ?\n\nCette action est définitive. " +
                            "Si vous rebranchez ce " + kind + ", il repartira des réglages par défaut.",
                            "Supprimer le " + kind, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                        remove(key);
                };
                row.Controls.AddRange(new Control[] { icon, lName, st, del });
                c.Controls.Add(row);
                y += Theme.S(50);
            }
            return y;
        }
    }
}
