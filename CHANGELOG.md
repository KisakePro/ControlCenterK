# Journal des versions

Chaque section `## [x.y.z]` sert de notes à la Release GitHub correspondante.

## [1.0.0] - 2026-10-06

Première version publique.

- Contrôleur MIDI (Korg nanoKONTROL2) : vue interactive du contrôleur, cibles de volume (sorties, entrées,
  applications, application au premier plan…), actions des boutons, LED, MIDI learn, filtre anti-tremblement,
  profils (export / import), mémorisation de la position des faders.
- Routage audio façon console : entrées (micros ou sorties en boucle) vers plusieurs sorties, gains, muet,
  délai par sortie, vumètres ; vues Console, Matrice de routage et Cartographie ; glisser-déposer des tranches.
- Périphériques virtuels visibles par tous les logiciels (« Haut-parleurs (Nom) » / « Microphone (Nom) »),
  via USB/IP et le pilote usbip-win2.
- Modules activables séparément, thèmes de couleurs personnalisables, très faible consommation en arrière-plan.
- Installateur sans droits administrateur et mises à jour depuis GitHub.
