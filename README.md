# SDRLink V1.60 — @F5PBG 2026

SDRLink garde un **WebSDR** calé sur votre **transceiver**. Le WebSDR suit le VFO du poste et le poste suit la cascade du WebSDR. Le mode est synchronisé aussi.

- **Interfaces** : port COM (CAT direct), OmniRig, TCI, Ham Radio Deluxe, Hamlib (rigctld) ; liaison avec le poste contrôlée chaque minute, aide OmniRig si le poste ne répond pas.
- **Postes en CAT direct (128 modèles, classés par numéro)** : Yaesu (40, du FT-100 au FTX-1, dont les anciens FT-747GX, FT-757GX/GXII, FT-767GX, FT-840, FT-890, FT-900, FT-920, FT-990, FT-1000…), Icom (55, de l'IC-78 à l'IC-9700, dont les IC-7xx des années 80), Kenwood (23, du TS-50S au TS-B2000), Elecraft (5), Xiegu (G90, X108G, X5105, X6100, X6200). Les autres postes passent par OmniRig, HRD ou Hamlib.
- **WebSDR** : websdr.org, KiwiSDR, OpenWebRX / OpenWebRX+, UberSDR, PhantomSDR. Le son démarre sans clic, un pseudo (bouton Pseudo/Call, « SDRLink » par défaut) est donné d'office aux KiwiSDR et websdr.org qui en demandent un, un KiwiSDR complet met en file d'attente, le squelch est ouvert (sauf FM VHF), et un clic dans la page fait suivre le poste dès l'arrivée. Le zoom de la cascade reste celui choisi sur la page quand SDRLink change la fréquence. Ronds **partage**, **+** et **−** sur la page (lien de partage qui ouvre SDRLink au même endroit, écoute dans le navigateur ou téléchargement de SDRLink, zoom de la page retenu par WebSDR, OpenWebRX ajusté pour voir tout son panneau), adresse masquable par des étoiles (clic droit sur la page).
- **Fonctions** :
  - bouton PTT avec délai de sécurité jusqu'à 55 min, rouge aussi quand le poste émet par VOX (FreeDV, pédale, micro), Mute TX et Volume TX aussi par TCI (Brick SDR / Thetis, ExpertSDR), et coupure du son en émission (WebSDR, poste, ExpertSDR, HDSDR, au choix par clic droit) ;
  - bouton **TUNE** (tonalité de 1500 Hz sur la carte son d'émission du poste, niveau et durée au clic droit), **micro du PC envoyé au poste** sur cette même carte, sans fenêtre (clic droit sur PTT), avec **égaliseur 10 bandes, noise gate et compresseur / limiteur**, choix du **VFO A ou B**, partage du poste en CAT direct avec un autre logiciel par un **port COM virtuel** (com0com installé depuis SDRLink) ;
  - synchro désactivée : la case kHz, les mémoires, le scan et les modes ne règlent que le WebSDR, l'émetteur ne bouge pas ;
  - bouton ⇄ qui passe le son d'une réception à l'autre (WebSDR, poste, ExpertSDR, HDSDR), deux réceptions pouvant être laissées de côté ;
  - molette sur les fréquences, saisie en kHz avec historique, boutons de mode ;
  - clic sur la fréquence : clavier de fréquence et liste Mem des 20 dernières fréquences (Store, enregistrement automatique après 1 à 30 min sur la même fréquence), comme dans PhoneSDRLink ;
  - mémoires de fréquences nommées, en listes, avec les 20 dernières utilisées, et une liste « Modes numériques » des fréquences habituelles du mode utilisé (1,8 à 440 MHz, selon la région IARU), avec le **préampli du poste** rappelé par chaque mémoire (IPO / AMP1 / AMP2, IPO1 / IPO2 sur le FTdx5000, P.AMP Icom, PRE Kenwood et Elecraft ; 58 modèles, par port COM, OmniRig, Hamlib ou HRD), et la **puissance d'émission** (en watts, avec un plafond, jamais pendant l'émission ; 49 modèles, par port COM, OmniRig, Hamlib ou TCI) ;
  - scan de fréquences (avec jusqu'à 10 zones non scannées) ou de mémoires, jusqu'à 10 profils de scan nommés (même format que PhoneSDRLink), clic = reprise immédiate à la fréquence actuelle, clic droit = scan de la bande où l'on se trouve (160 m à 70 cm, 27 MHz) et ses profils, **pause sur signal** (seuil S/N, durée, pas en arrière), durées dès 0,05 s, mode par scan, **touches multimédia** et **commande vocale** du scan, scan arrêté si l'on change la fréquence sur le poste ou le WebSDR ;
  - **mode par bande** : en changeant de bande (160 m à 70 cm, 27 MHz, et jusqu'à 0,5 MHz d'une bande), le poste et le WebSDR prennent le mode retenu pour la bande, sinon LSB / USB / AM ;
  - bouton REC sur la ligne de son : enregistrement du son des haut-parleurs (ou de la carte son du poste) en MP3, WAV ou FLAC, même taille que Mute WebSDR, couleur réglable ;
  - bouton Réinit WebSDR : relance un WebSDR arrêté (délai d'inactivité) en gardant ses réglages (fréquence, mode, filtre, zoom), ou automatiquement quand son son est perdu (Réinit Auto WebSDR, clic droit : Auto et délai de 10 à 60 s, jamais quand le son est coupé dans la page, même avec Mute WebSDR ou le volume de SDRLink à 0) ; réinitialisation totale au clic droit ;
  - **égaliseur 10 bandes** du son du WebSDR et du poste écouté sur le PC (bouton EQ, préréglages, courbe de réponse) ;
  - écoute en diversité (bouton Diversité, notch auto et DNR compris) : le WebSDR dans une oreille, le poste, ExpertSDR ou HDSDR dans l'autre, le son local retardé automatiquement pour tomber en même temps que le WebSDR ; ou **deux WebSDR**, un dans chaque oreille (le second écouté sans être affiché, calé tout seul) ;
  - affichages réduit, mini et ptt (bouton PTT seul, avec la case Avant plan) ; fenêtre redimensionnable en conservant les proportions (tout suit sa taille, comme le plein écran en plus petit) ;
  - propositions pendant la frappe dans la barre WebSDR (nom ou adresse, liste choisie) ;
  - favoris en 10 listes maximum avec vérification des liens et des doublons, export/import compatible Firefox par listes, recherche des WebSDR proches de votre position (ordinateur, locator ou coordonnées), listes déroulantes qui restent ouvertes jusqu'à un nouveau clic ;
  - volume du WebSDR, du poste, d'ExpertSDR (Mute ExpertSDR, par Windows ou TCI ; ColibrinanoLink aussi, par Windows) et de HDSDR (Mute HDSDR, par Windows), son du poste aussi sur le PC au choix (clic droit sur Mute TX) avec son propre curseur « Volume TX sur PC » ; tous les volumes au pas de 1 % (molette, flèches, clic) ;
  - câble audio SDRLINK pour WSJT-X, notch automatique (DNF) en trois types : d'origine, adaptatif (NLMS) et spectral (FFT), et réducteur de bruit **DNR** en trois types : classique, amélioré (voix moins déformée) et **IA** (réseau de neurones RNNoise intégré, sans site externe), notch et DNR chacun de niveau 1 à 10, appliqués aussi au son du poste joué par SDRLink (coupés au choix en mode numérique et en FreeDV) ;
  - bande passante de réception et pas de la molette (dès 10 Hz) retenus pour chaque mode ;
  - décodage automatique de la voix numérique FreeDV RADE (V1 ou V2) dès qu'un signal est reçu (bouton FreeDV auto), aussi bien que FreeDV 2.4.1 (même récepteur RADE), avec son propre volume (le curseur Volume WebSDR devient « Volume FreeDV » pendant la parole) ;
  - décodeurs de modes numériques sur un seul bouton : FT8, FT4, FT2, WSPR, JT9, JS8, CW, RTTY, PSK31/63, Olivia, Contestia, Hell, SSTV (26 modes) et WEFAX, affichés dans une fenêtre déplaçable sur la page ou séparée (second écran, plein écran), images enregistrées en PNG ; accord automatique sur la fréquence du mode choisi, et bouton Mode ? qui reconnaît le mode écouté ;
  - interface en 23 langues européennes ;
  - mode sombre, palette de 64 teintes et couleur de fond au choix (RVB), 7 looks de boutons (Classique, Win3.11, Néon IA, Moderne, Verre, Terminal, Métal), panneau de commande en haut ou sous la page du WebSDR, couleur de chaque bouton au choix (clic droit) et taille du texte réglable ;
  - vérification des mises à jour au démarrage et mise à jour automatique en un clic (empreinte SHA-256 vérifiée, même nom de fichier, ancienne version supprimée), import/export de tous les réglages vers un autre ordinateur, avec import au choix (configuration, WebSDR, mémoires, scan ; ajout ou remplacement), réinitialisation totale ou partielle du programme ;
  - historique des 20 derniers messages (clic droit sur la barre d'état).

## Télécharger

**[Télécharger la dernière version](https://github.com/F5pbgLudovic/SDRLink/releases/latest)** (`SDRLink.exe`, ou le `.zip` qui contient aussi la notice en français et en anglais). C'est un seul fichier, sans installation, pour Windows 10 et 11.

## Notice

Voir [LISEZMOI.md](LISEZMOI.md) (notice complète en anglais : [MANUAL.md](MANUAL.md)).

## Recompiler

Aucun Visual Studio n'est nécessaire : le compilateur C# fourni avec Windows (.NET Framework 4.x) suffit.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Le script télécharge le paquet WebView2 (NuGet) au premier lancement et produit `dist\SDRLink.exe`.

Le décodeur FreeDV (`native\sdrlink_freedv.dll.gz`, déjà dans le dépôt) est une DLL en C : radae_nopy (David Rowe et Peter B Marks, licence BSD 2 clauses, `native\freedv\rade\LICENSE`) et le vocodeur FARGAN d'Opus (licence BSD, `native\freedv\opus\COPYING`). Pour la recompiler : `tools\install-toolchain.ps1` (télécharge LLVM-MinGW), puis `native\build-freedv.ps1`.

Les décodeurs FT8 / FT4 / FT2 et Olivia / Contestia (`native\sdrlink_digi.dll.gz`, déjà dans le dépôt) sont une DLL en C / C++ : ft8_lib (Kārlis Goba, licence MIT, `native\digi\ft8lib\LICENSE`) et la bibliothèque MFSK de Pawel Jalocha (reprise de fldigi, GNU GPL). Pour la recompiler : `native\build-digi.ps1`. Le dictionnaire de texte JS8 (`data\js8jsc.bin.gz`) vient de JS8Call (GNU GPL).

## Licence

SDRLink est distribué sous la **licence publique générale GNU version 3** (GPL v3), voir le fichier [LICENSE](LICENSE). Copyright © 2026 F5PBG et contributeurs. Ce programme est fourni SANS AUCUNE GARANTIE, dans les limites permises par la loi. Raison : les décodeurs Olivia / Contestia (bibliothèque de Pawel Jalocha, reprise de fldigi) et JS8 (données de JS8Call) sont sous GPL. Vous pouvez utiliser, étudier, modifier et redistribuer le programme, à condition de fournir aux destinataires les sources correspondantes sous la même licence. Les composants de tiers gardent leur licence (MIT, BSD : compatibles), voir [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## English

SDRLink keeps a **WebSDR** locked to your **transceiver**. The WebSDR follows the rig's VFO and the rig follows the WebSDR waterfall. The mode is synchronised too.

- **Interfaces**: COM port (direct CAT), OmniRig, TCI, Ham Radio Deluxe, Hamlib (rigctld); rig link checked every minute, OmniRig help if the rig does not answer.
- **Rigs in direct CAT (128 models, sorted by number)**: Yaesu (40, from the FT-100 to the FTX-1, including the older FT-747GX, FT-757GX/GXII, FT-767GX, FT-840, FT-890, FT-900, FT-920, FT-990, FT-1000…), Icom (55, from the IC-78 to the IC-9700, including the 1980s IC-7xx), Kenwood (23, from the TS-50S to the TS-B2000), Elecraft (5), Xiegu (G90, X108G, X5105, X6100, X6200). Other rigs work through OmniRig, HRD or Hamlib.
- **WebSDR**: websdr.org, KiwiSDR, OpenWebRX / OpenWebRX+, UberSDR, PhantomSDR. The audio starts without a click, a name (Pseudo/Call button, "SDRLink" by default) is given automatically to KiwiSDRs and websdr.org receivers that ask for one, a full KiwiSDR puts you in the queue, the squelch is opened (except VHF FM), and a click in the page makes the rig follow right after arriving. The waterfall zoom stays as chosen on the page when SDRLink changes the frequency. **Share**, **+** and **−** round buttons on the page (share link that opens SDRLink at the same spot, listens in the browser or downloads SDRLink, page zoom kept per WebSDR, OpenWebRX fitted to show its whole panel), address can be hidden by stars (right-click the page).
- **Features**:
  - PTT button with a safety timeout of up to 55 min, also red when the rig transmits by VOX (FreeDV, foot switch, microphone), Mute TRX and TRX volume also over TCI (Brick SDR / Thetis, ExpertSDR), audio muted while transmitting (WebSDR, rig, ExpertSDR, HDSDR, chosen by right-click);
  - **TUNE** button (1500 Hz tone on the rig transmit sound card, level and duration by right-click), **PC microphone sent to the rig** on that same card, with no window (right-click PTT), with a **10-band equalizer, noise gate and compressor / limiter**, **VFO A or B** choice, sharing of the direct-CAT rig with another program through a **virtual COM port** (com0com installed from SDRLink);
  - sync Off: the kHz box, memories, scan and modes only tune the WebSDR, the transmitter does not move;
  - ⇄ button that switches the audio from one receiver to the next (WebSDR, rig, ExpertSDR, HDSDR), with up to two receivers left aside;
  - mouse wheel on the frequencies, kHz entry with history, mode buttons;
  - click on the frequency: frequency keypad and Mem list of the last 20 frequencies (Store, automatic storing after 1 to 30 min on the same frequency), as in PhoneSDRLink;
  - named frequency memories, in lists, with the last 20 used, and a "Digital modes" list of the usual frequencies of the mode in use (1.8 to 440 MHz, by IARU region), with the **rig preamp** recalled by each memory (IPO / AMP1 / AMP2, IPO1 / IPO2 on the FTdx5000, Icom P.AMP, Kenwood and Elecraft PRE; 58 models, through COM port, OmniRig, Hamlib or HRD), and the **transmit power** (in watts, with a ceiling, never while transmitting; 49 models, through COM port, OmniRig, Hamlib or TCI);
  - frequency scan (with up to 10 skipped ranges) or memory scan, up to 10 named scan profiles (same format as PhoneSDRLink), click = resume at once from the current frequency, right-click = scan of the band you are in (160 m to 70 cm, 27 MHz) and its profiles, **pause on signal** (S/N threshold, time, steps back), times from 0.05 s, mode per scan, **media keys** and **voice command** for the scan, scan stopped when you change the frequency on the rig or the WebSDR;
  - **mode per band**: when changing band (160 m to 70 cm, 27 MHz, and up to 0.5 MHz from a band), the rig and the WebSDR take the mode kept for the band, otherwise LSB / USB / AM;
  - REC button on the audio row: records the speaker sound (or the rig sound card) to MP3, WAV or FLAC, same size as Mute WebSDR, adjustable colour;
  - Reset WebSDR button: restarts a stopped WebSDR (idle timeout) keeping its settings (frequency, mode, filter, zoom), or automatically when its sound is lost (Auto Reset WebSDR, right-click: Auto and a time of 10 to 60 s, never when the sound is cut in the page, even with Mute WebSDR or SDRLink's volume at 0); full reset from the right-click menu;
  - **10-band equalizer** for the WebSDR audio and the rig heard on the PC (EQ button, presets, response curve);
  - diversity listening (Diversity button, auto notch and DNR included): the WebSDR in one ear, the rig, ExpertSDR or HDSDR in the other, the local audio delayed automatically to match the WebSDR; or **two WebSDRs**, one in each ear (the second one heard without being displayed, aligned by itself);
  - reduced, mini and PTT (PTT button only, with the Always on top box) views; window resizable keeping the proportions (everything follows its size, like the full-screen view, only smaller);
  - suggestions while typing in the WebSDR bar (name or address, chosen list);
  - favorites in up to 10 lists with dead-link and duplicate checks, Firefox-compatible export/import by list, search for WebSDRs near your position (computer, locator or coordinates), drop-down lists that stay open until a new click;
  - WebSDR, rig, ExpertSDR (Mute ExpertSDR, through Windows or TCI; ColibrinanoLink too, through Windows) and HDSDR volume (Mute HDSDR, through Windows), rig audio also on the PC if wanted (right-click Mute TRX) with its own "TRX volume on PC" slider; every volume in 1 % steps (wheel, arrows, click);
  - SDRLINK audio cable for WSJT-X, automatic notch (DNF) in three types: original, adaptive (NLMS) and spectral (FFT), and **DNR** noise reduction in three types: classic, improved (less voice distortion) and **AI** (built-in RNNoise neural network, no external website), notch and DNR each with level 1 to 10, also applied to the rig audio played by SDRLink (optionally off in digital modes and FreeDV);
  - receive bandwidth and wheel step (from 10 Hz) kept for each mode;
  - automatic decoding of FreeDV RADE digital voice (V1 or V2) as soon as a signal is received (FreeDV auto button), as well as FreeDV 2.4.1 (same RADE receiver), with its own volume (the WebSDR volume slider becomes "FreeDV volume" during speech);
  - digital mode decoders on a single button: FT8, FT4, FT2, WSPR, JT9, JS8, CW, RTTY, PSK31/63, Olivia, Contestia, Hell, SSTV (26 modes) and WEFAX, shown in a movable window over the page or in a separate one (second screen, full screen), pictures saved as PNG; automatic tuning to the chosen mode's frequency, and a Mode? button that recognises the mode being heard;
  - interface in 23 European languages (menu on the 2nd line);
  - dark mode, 64-shade palette and background colour of your choice (RGB), 7 button looks (Classic, Win3.11, AI Neon, Modern, Glass, Terminal, Metal), control panel at the top or below the WebSDR page, colour of each button of your choice (right-click) and adjustable text size;
  - update check at startup and one-click automatic update (SHA-256 fingerprint checked, same file name, old version deleted), import/export of all settings to another computer, with selective import (configuration, WebSDR, memories, scan; add or replace), full or partial reset of the program;
  - history of the last 20 status messages (right-click the status bar).

### Download

**[Download the latest version](https://github.com/F5pbgLudovic/SDRLink/releases/latest)** (`SDRLink.exe`, or the `.zip` that also contains the manual in English and French). It is a single file, no installation, for Windows 10 and 11. On first launch, SDRLink checks for .NET Framework 4.6.2+ and Microsoft Edge WebView2 and offers to install them if they are missing.

### Manual

See the full English manual: [MANUAL.md](MANUAL.md) (French version: [LISEZMOI.md](LISEZMOI.md)).

### Building

No Visual Studio needed: the C# compiler shipped with Windows (.NET Framework 4.x) is enough.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

On first run, the script downloads the WebView2 package (NuGet) and produces `dist\SDRLink.exe`.

The FreeDV decoder (`native\sdrlink_freedv.dll.gz`, already in the repository) is a C DLL: radae_nopy (David Rowe and Peter B Marks, 2-clause BSD licence, `native\freedv\rade\LICENSE`) and Opus's FARGAN vocoder (BSD licence, `native\freedv\opus\COPYING`). To rebuild it: `tools\install-toolchain.ps1` (downloads LLVM-MinGW), then `native\build-freedv.ps1`.

The FT8 / FT4 / FT2 and Olivia / Contestia decoders (`native\sdrlink_digi.dll.gz`, already in the repository) are a C / C++ DLL: ft8_lib (Kārlis Goba, MIT licence, `native\digi\ft8lib\LICENSE`) and Pawel Jalocha's MFSK library (taken from fldigi, GNU GPL). To rebuild it: `native\build-digi.ps1`. The JS8 text dictionary (`data\js8jsc.bin.gz`) comes from JS8Call (GNU GPL).

### License

SDRLink is released under the **GNU General Public License v3** (see [LICENSE](LICENSE)). Copyright (C) 2026 F5PBG and contributors. This program comes with ABSOLUTELY NO WARRANTY, to the extent permitted by law. Reason: the Olivia / Contestia decoders (Pawel Jalocha's library, taken from fldigi) and JS8 (JS8Call data) are GPL. You may use, study, modify and redistribute the program, provided you give recipients the corresponding source code under the same licence. Third-party parts keep their own (MIT, BSD: compatible) licences, see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
