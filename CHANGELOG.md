# Journal des versions

Chaque section `## [x.y.z]` sert de notes à la Release GitHub correspondante.

## [0.7.2] - 2026-10-24 (BETA)

- Le module « FPS » devient « Capteurs » : en plus des FPS, l'affichage en jeu montre le processeur, la carte graphique
  et la mémoire vive (utilisation, température, courbe de 0 à 100 %) ; chaque bloc s'affiche ou non.
- Carte graphique : charge et température lues par le pilote AMD ou NVIDIA, sans rien installer.
- Processeur : charge mesurée par Windows ; température par la nouvelle sonde (PawnIO, pilote signé, module AMD Ryzen
  embarqué), installée en un clic et lancée par une tâche planifiée, ou à défaut par HWiNFO.
- Mémoire vive : pourcentage utilisé et Go utilisés sur le total.

## [0.7.1] - 2026-10-18 (BETA)

- Partage d'écran : zones enregistrées (nom, coordonnées, couleur), redéfinissables à la souris.
- Passage d'une zone à l'autre en direct, sans couper la caméra (Discord ne voit qu'un changement d'image).
- Couleur par zone : cadre de la zone partagée à sa couleur, et option « Afficher toutes les zones ».
- Raccourcis globaux désactivables : Ctrl+Alt+F1 à F9 (zone 1 à 9), Ctrl+Alt+F10 (arrêt).
- L'ancienne zone unique est reprise comme « Zone 1 ».

## [0.7] - 2026-10-17 (BETA)

- Nouveau module « Partage d'écran » (ancien utilitaire KStS) : une zone de l'écran envoyée en direct dans la caméra
  virtuelle « KStS Virtual Screen », pour la partager sur Discord comme une webcam (1920 × 1080, 30 images/s, curseur).
  Installation de la caméra en un clic (module caméra virtuelle d'OBS, GPL), zone choisie à la souris ou par
  coordonnées, cadre rouge hors capture, explication pas à pas.
- Souris et clavier : un périphérique branché peut être supprimé de la liste (masqué, réglages effacés) et rétabli.
- Souris et clavier : périphériques non connectés et masqués regroupés en bas de la page.

## [0.6.2] - 2026-10-13

- FPS : réglages par défaut et, au besoin, réglages propres à un programme (couleur, taille, coin, décalages,
  courbe, fond) : « Réglages de » dans la carte Apparence, ou le bouton de chaque programme.
- FPS : l'affichage reste visible sur le jeu en arrière-plan pendant les réglages dans ControlCenterK,
  sans passer par-dessus la fenêtre de réglage ; les changements s'y voient en direct.

## [0.6.1] - 2026-10-13

- FPS : valeur stable et exacte. Les images sont comptées selon leur horodatage Windows et non plus à leur arrivée
  (qui se fait par paquets) : un jeu bloqué à 75 FPS affiche bien 75 au lieu d'osciller entre 50 et 93.

## [0.6] - 2026-10-10

- Nouveau module « FPS » : images par seconde affichées par-dessus les programmes choisis, avec une courbe pour repérer
  les chutes. Mesure par les événements Windows (DirectX 9 à 12, Vulkan, OpenGL), sans injection dans les jeux.
  Couleur, taille, coin de la fenêtre, décalages, durée de la courbe, opacité du fond et aperçu réglables.
  Fonctionne en fenêtré et en plein écran fenêtré ; nécessite les droits administrateur (ou le groupe
  « Utilisateurs du journal de performances »).

## [0.5.4] - 2026-10-08

- Souris et clavier : les périphériques enregistrés mais débranchés sont listés et peuvent être supprimés (avec confirmation).
- Souris : seuls les boutons présents sont listés. Précédent / suivant et molette inclinée s'ajoutent avec « Détecter un bouton ».
- Clavier SteelSeries Apex M750 ISO (AZERTY…) : la touche « < » et la touche Menu sont éclairables, et la rangée Maj est
  correctement alignée.
- Périphériques audio : entrées à gauche, sorties à droite, applications en dessous.

## [0.5.3] - 2026-10-08

- Macros clavier : une combinaison peut servir de déclencheur (ex. Ctrl+A, Ctrl+Maj+F1). « Ajouter une touche » attend
  la touche après les modificateurs ; un modificateur seul (Ctrl…) est retenu à son relâchement. Une combinaison passe
  avant la touche seule, et les modificateurs tenus sont relâchés avant l'action.

## [0.5.2] - 2026-10-08

- Macros clavier : les touches sont de nouveau reconnues (claviers SteelSeries notamment). Le crochet clavier
  tourne désormais dans son propre thread, et une touche sans code de balayage est identifiée par sa touche virtuelle.
- Mises à jour : la liste déroulante des versions GitHub se remplit dès l'ouverture des paramètres.

## [0.5.1] - 2026-10-08

- Le profil actif reste affiché même quand le module Contrôleur est désactivé.
- Raccourcis et macros (clavier et souris) : choix du mode Impulsion (une fois à l'appui) ou Continu
  (maintenu / rejoué tant que la touche est enfoncée) ; la touche Windows peut faire partie d'une combinaison.
- Mises à jour : liste des versions publiées pour installer une version précise, y compris revenir à une ancienne.

## [0.5] - 2026-10-07

- Nouvelle numérotation des versions : 0.5 (remplace la série 1.x).
- Barre latérale en sections : profil actif sous le nom de l'application, section « Audio »
  (Contrôleur, Routage, Périphériques audio) et section « Périphériques » (Souris, Clavier).
- Module « Souris » : détection automatique des souris branchées. Réglages (DPI, fréquence, éclairage)
  pour les souris Corsair, Logitech G (HID++), Razer et SteelSeries ; testé sur la Corsair Nightsword RGB,
  les autres modèles sont signalés « expérimental ». Réglages enregistrés séparément pour chaque modèle.
  Boutons réaffectables (touches, macros, DPI, média) avec un bouton « Détecter un bouton ».
- Module « Clavier » : détection automatique des claviers. Éclairage touche par touche sur les SteelSeries Apex
  (testé sur l'Apex M750) et de tout le clavier sur les Corsair K65 / K70 / K95 / Strafe et Razer, avec animations
  (respiration, arc-en-ciel, vague, réactif). Macros sur n'importe quelle touche, pour tous les claviers.
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
