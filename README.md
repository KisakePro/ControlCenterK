# ControlCenterK

Mixeur Windows piloté par un **Korg nanoKONTROL2** : chaque fader / knob règle le volume d'une ou plusieurs cibles
(sorties, entrées, applications…), les boutons coupent le son, changent le périphérique par défaut ou envoient des touches multimédia.

## Compiler

Aucune installation : on utilise le compilateur C# fourni avec Windows (.NET Framework 4.8).

```bat
build.cmd
```

Résultat :
- `bin\ControlCenterK.exe` : l'application (un seul fichier, utilisable sans installation) ;
- `dist\ControlCenterK-Setup.exe` : l'installateur à distribuer. Il installe pour l'utilisateur courant, sans droits
  administrateur, dans `%LOCALAPPDATA%\Programs\ControlCenterK` (raccourcis, démarrage avec Windows en option,
  entrée dans « Applications installées »). Relancé sur une version existante, il fait la mise à jour en gardant les réglages.

## Utilisation

- Fermer la fenêtre ne quitte pas l'app : elle reste dans la zone de notification (clic = ouvrir, clic droit = Quitter).
- **Contrôleur** : cliquez sur un contrôle du dessin, ou touchez-le sur le nanoKONTROL2, puis ajoutez des cibles.
- **Audio** : masquez les périphériques/applications inutiles et donnez-leur un nom court.
- **Paramètres** : démarrage avec Windows, retour LED, courbe de volume…
- **Profils** (barre latérale, bouton `⋯`) : nouveau, dupliquer, renommer, supprimer, **exporter / importer** en fichier `.json`.
  On peut aussi changer de profil depuis l'icône de notification, ou avec un bouton du contrôleur (actions « Profil suivant / précédent »).
  À l'import sur un autre PC, les périphériques sont retrouvés par leur nom.
- **Modules** (Paramètres) : « Contrôleur MIDI » et « Routage audio » s'activent séparément. Un module désactivé
  ne charge ni sa page, ni ses threads, ni sa mémoire audio.
- **Routage audio** (module) : console façon Voicemeeter. Une *entrée* est un micro / une entrée ligne, ou une sortie
  capturée « en boucle » (tout ce qui y est joué). Chaque entrée peut être envoyée vers une ou plusieurs *sorties*
  (casque, enceintes…) avec gain en dB, muet, vumètres, et un délai par sortie pour les synchroniser.
  Glisser le fader, molette = ±1 dB (Maj = ±0,1), double-clic = 0 dB, clic droit = menu.
  Les tranches peuvent aussi être pilotées par le nanoKONTROL2 (cibles « Routage audio »).
  Deux vues : **Console** (rubrique « ENVOYER VERS » sur chaque entrée) et **Matrice de routage** (entrées en lignes,
  sorties en colonnes, un clic par liaison).
- **Vos périphériques virtuels** (bouton « Virtuels », section du haut) : l'app crée de vraies cartes son visibles par
  tous les logiciels, « Haut-parleurs (Nom) » et « Microphone (Nom) ». Principe : l'app émule une carte son USB Audio 1.0
  (48 kHz, stéréo 16 bits) et la présente à Windows via USB/IP en local (`127.0.0.1:3240`) ; Windows utilise alors son
  propre pilote audio. Prérequis unique : le pilote [usbip-win2](https://github.com/vadimgrn/usbip-win2) (BSD-2, signé
  Microsoft), à installer soi-même depuis sa page de versions. Les périphériques existent tant que l'app tourne.
- **Entrées / sorties virtuelles d'autres logiciels** (même fenêtre) : détecte les câbles virtuels (paires
  « X Input » ⇄ « X Output ») et les périphériques virtuels d'autres logiciels (Wave Link, Voicemeeter…), et crée en un clic
  une *entrée virtuelle* (un logiciel joue sur le câble → le son arrive dans la console) ou une *sortie virtuelle*
  (la console joue sur le câble → l'autre logiciel le reçoit comme un micro). Les boucles câble → même câble sont bloquées.
  Windows n'autorise la création de nouveaux périphériques audio que par un pilote signé : pour avoir vos propres câbles,
  installez [VB-CABLE](https://vb-audio.com/Cable/) (gratuit), ils apparaîtront automatiquement.
- **Apparence** (Paramètres) : thèmes prédéfinis, couleur d'accent et teinte du fond à choisir dans une grille de nuances
  (ou en couleur personnalisée), luminosité du fond, et « Enregistrer ce thème… » pour garder ses combinaisons.
- **Filtre anti-tremblement** (Paramètres) : ignore les micro-variations d'un fader au repos ; seul un mouvement franc change le son ou sélectionne le contrôle.
- **MIDI learn** : si votre nanoKONTROL2 n'est pas en mapping d'usine, sélectionnez un contrôle, cliquez « MIDI learn » et bougez le contrôle physique.

### LED des boutons

Pour que les LED suivent l'état muet, passez le nanoKONTROL2 en **LED Mode : External** avec *KORG Kontrol Editor*
(sinon le contrôleur gère ses LED lui‑même).

## Cibles disponibles

| Cible | Effet |
|---|---|
| Sortie / micro par défaut | Le périphérique par défaut actuel de Windows |
| Périphérique | Une sortie ou entrée précise (Elgato Wave Link, Voicemeeter, Realtek…) |
| Application | Toutes les sessions audio de l'exécutable (ex. `brave`, `steam`) |
| Application au premier plan | L'application de la fenêtre active |
| Sons système | Les sons de Windows |
| Applications non assignées | Toutes les applis qui ne sont assignées à aucun contrôle |

## Structure

```
src/Program.cs          démarrage, instance unique, icône de notification
src/Engine.cs           thread worker : MIDI -> volumes / actions / LED
src/CoreAudio.cs        accès WASAPI (volumes, sessions, périphérique par défaut)
src/Midi.cs             entrée / sortie MIDI (winmm)
src/Config.cs           configuration (%APPDATA%\ControlCenterK\config.json)
src/Router/*            module de routage : flux WASAPI, rééchantillonnage, mixage
src/Controller.cs       disposition et CC d'usine du nanoKONTROL2
src/UI/*                interface sombre
```

## Mises à jour

L'application vérifie (au plus une fois par jour, 30 s après le démarrage) s'il existe une nouvelle version dans les
[Releases GitHub](https://github.com/KisakePro/ControlCenterK/releases). Si c'est le cas, une notification s'affiche et
*Paramètres → Mises à jour → Installer* télécharge l'installateur, vérifie son empreinte SHA-256, puis le lance.
La vérification automatique peut être désactivée dans les Paramètres.

## Publier une nouvelle version

1. Modifier le numéro dans `src/Version.cs` (ex. `1.1.0`).
2. Ajouter une section `## [1.1.0] - date` en haut de `CHANGELOG.md` : elle devient le texte de la Release.
3. Committer, puis créer et pousser le tag :
   ```
   git tag v1.1.0
   git push origin main --tags
   ```
4. GitHub Actions (`.github/workflows/release.yml`) compile l'application et l'installateur, et publie la Release avec
   `ControlCenterK-Setup.exe` et son empreinte `.sha256`. Les utilisateurs sont alors prévenus automatiquement.
