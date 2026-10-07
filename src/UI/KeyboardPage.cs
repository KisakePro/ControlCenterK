using System;
using System.Drawing;
using System.Windows.Forms;

namespace ControlCenterK
{
    /// <summary>Module « Clavier » : page provisoire, le module est en construction.</summary>
    class KeyboardPage : Panel
    {
        readonly Label title, sub, icon, head, text;
        readonly Card card;

        public KeyboardPage()
        {
            BackColor = Theme.Bg;
            title = Theme.Label("Clavier", Theme.Semi(18f), Theme.Text, BackColor);
            sub = Theme.Label("Réglages, éclairage et touches des claviers.", Theme.Ui(9.5f), Theme.Muted, BackColor);
            card = new Card();
            icon = Theme.Label(Glyphs.Keyboard, Theme.Icon(30f), Theme.Accent, Theme.Card);
            head = Theme.Label("Page en construction", Theme.Semi(13f), Theme.Text, Theme.Card);
            text = Theme.Label("Ce module arrive dans une prochaine version : réaffectation des touches, macros et éclairage des claviers compatibles.",
                Theme.Ui(9.5f), Theme.Muted, Theme.Card);
            text.TextAlign = ContentAlignment.TopCenter;
            card.Controls.AddRange(new Control[] { icon, head, text });
            Controls.AddRange(new Control[] { title, sub, card });
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (card == null) return;
            int pad = Theme.S(28), w = Math.Min(ClientSize.Width - 2 * pad, Theme.S(900));
            title.Location = new Point(pad - Theme.S(2), Theme.S(20));
            sub.Location = new Point(pad, title.Bottom + Theme.S(2));
            text.MaximumSize = new Size(Math.Max(Theme.S(200), w - Theme.S(80)), 0);
            icon.Location = new Point((w - icon.Width) / 2, Theme.S(36));
            head.Location = new Point((w - head.Width) / 2, icon.Bottom + Theme.S(14));
            text.Location = new Point((w - text.Width) / 2, head.Bottom + Theme.S(8));
            card.SetBounds(pad, sub.Bottom + Theme.S(24), w, text.Bottom + Theme.S(40));
        }
    }
}
