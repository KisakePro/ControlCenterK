# Journal des versions

Chaque section `## [x.y.z]` sert de notes à la Release GitHub correspondante.

## [0.3] - 2026-10-07

- Nouvelle numérotation des versions : 0.3.
- Barre latérale en sections : profil actif sous le nom de l'application, section « Audio »
  (Contrôleur, Routage, Périphériques audio) et section « Périphériques » (Souris, Clavier).
- Module « Souris » : Corsair Nightsword RGB (DPI, fréquence, éclairage, boutons, macros),
  avec un bouton « Détecter un bouton ».
- Module « Clavier » (page en construction).
- Prise en charge de plusieurs contrôleurs MIDI.
- Paramètres regroupés par catégorie : Général, Apparence, Contrôleur MIDI, Dossier de configuration.
- Les réglages sont enregistrés dans un dossier « config » à côté de ControlCenterK.exe
  (repris automatiquement de l'ancien emplacement) ; le dossier peut être changé dans les paramètres.

## [1.1.0] - 2026-10-06

- Le nanoKONTROL2 affiché à l'écran est interactif : glisser un fader ou un potentiomètre, appuyer
  sur un bouton (même effet que le contrôleur physique : volume, action, LED). Molette pour un réglage fin.
- Le clic droit sur un contrôle l'ouvre dans l'éditeur (toucher le contrôle physique le sélectionne toujours).
- L'onglet « Audio » s'appelle désormais « Périphériques audio ».

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
