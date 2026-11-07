# SDRLink V1.25 — @F5PBG 2026

SDRLink garde un **WebSDR** calé sur votre **transceiver**. Le WebSDR suit le VFO du poste et le poste suit la cascade du WebSDR. Le mode est synchronisé aussi.

- **Interfaces** : port COM (CAT direct), OmniRig, TCI, Ham Radio Deluxe, Hamlib (rigctld).
- **Postes en CAT direct (128 modèles, classés par numéro)** : Yaesu (40, du FT-100 au FTX-1, dont les anciens FT-747GX, FT-757GX/GXII, FT-767GX, FT-840, FT-890, FT-900, FT-920, FT-990, FT-1000…), Icom (55, de l'IC-78 à l'IC-9700, dont les IC-7xx des années 80), Kenwood (23, du TS-50S au TS-B2000), Elecraft (5), Xiegu (G90, X108G, X5105, X6100, X6200). Les autres postes passent par OmniRig, HRD ou Hamlib.
- **WebSDR** : websdr.org, KiwiSDR, OpenWebRX / OpenWebRX+, UberSDR, PhantomSDR. Le son démarre sans clic, un pseudo (bouton Pseudo/Call, « SDRLink » par défaut) est donné d'office aux KiwiSDR et websdr.org qui en demandent un, un KiwiSDR complet met en file d'attente, le squelch est ouvert (sauf FM VHF), et un clic dans la page fait suivre le poste dès l'arrivée.
- **Fonctions** :
  - bouton PTT et coupure du son en émission (WebSDR, poste, ExpertSDR, HDSDR, au choix par clic droit) ;
  - bouton ⇄ qui passe le son d'une réception à l'autre (WebSDR, poste, ExpertSDR, HDSDR), deux réceptions pouvant être laissées de côté ;
  - molette sur les fréquences, saisie en kHz avec historique, boutons de mode ;
  - mémoires de fréquences nommées, en listes, avec les 20 dernières utilisées ;
  - scan de fréquences (avec jusqu'à 10 zones non scannées) ou de mémoires, jusqu'à 10 profils de scan nommés, reprise du scan à la fréquence actuelle ;
  - bouton REC sur la ligne de son : enregistrement du son des haut-parleurs (ou de la carte son du poste) en MP3, WAV ou FLAC, couleur et largeur réglables ;
  - affichages réduit, mini et ptt (bouton PTT seul, avec la case Avant plan) ;
  - propositions pendant la frappe dans la barre WebSDR (nom ou adresse, liste choisie) ;
  - favoris en 10 listes maximum avec vérification des liens et des doublons, export/import compatible Firefox par listes, recherche des WebSDR proches de la France ;
  - volume du WebSDR, du poste, d'ExpertSDR (Mute ExpertSDR, par Windows ou TCI) et de HDSDR (Mute HDSDR, par Windows), son du poste aussi sur le PC au choix (clic droit sur Mute TX) ;
  - câble audio SDRLINK pour WSJT-X, notch automatique ;
  - décodage automatique de la voix numérique FreeDV RADE (V1 ou V2) dès qu'un signal est reçu (bouton FreeDV auto) ;
  - décodeurs de modes numériques sur un seul bouton : FT8, FT4, FT2, WSPR, JT9, JS8, CW, RTTY, PSK31/63, Olivia, Contestia, Hell, SSTV (26 modes) et WEFAX, affichés dans une fenêtre déplaçable sur la page (images enregistrées en PNG) ;
  - interface en 23 langues européennes ;
  - mode sombre, palette, couleur de chaque bouton au choix (clic droit) et taille du texte réglable ;
  - vérification des mises à jour au démarrage, import/export de tous les réglages vers un autre ordinateur, avec import au choix (configuration, WebSDR, mémoires, scan ; ajout ou remplacement), réinitialisation totale ou partielle du programme ;
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

- **Interfaces**: COM port (direct CAT), OmniRig, TCI, Ham Radio Deluxe, Hamlib (rigctld).
- **Rigs in direct CAT (128 models, sorted by number)**: Yaesu (40, from the FT-100 to the FTX-1, including the older FT-747GX, FT-757GX/GXII, FT-767GX, FT-840, FT-890, FT-900, FT-920, FT-990, FT-1000…), Icom (55, from the IC-78 to the IC-9700, including the 1980s IC-7xx), Kenwood (23, from the TS-50S to the TS-B2000), Elecraft (5), Xiegu (G90, X108G, X5105, X6100, X6200). Other rigs work through OmniRig, HRD or Hamlib.
- **WebSDR**: websdr.org, KiwiSDR, OpenWebRX / OpenWebRX+, UberSDR, PhantomSDR. The audio starts without a click, a name (Pseudo/Call button, "SDRLink" by default) is given automatically to KiwiSDRs and websdr.org receivers that ask for one, a full KiwiSDR puts you in the queue, the squelch is opened (except VHF FM), and a click in the page makes the rig follow right after arriving.
- **Features**:
  - PTT button, audio muted while transmitting (WebSDR, rig, ExpertSDR, HDSDR, chosen by right-click);
  - ⇄ button that switches the audio from one receiver to the next (WebSDR, rig, ExpertSDR, HDSDR), with up to two receivers left aside;
  - mouse wheel on the frequencies, kHz entry with history, mode buttons;
  - named frequency memories, in lists, with the last 20 used;
  - frequency scan (with up to 10 skipped ranges) or memory scan, up to 10 named scan profiles, scan resumed from the current frequency;
  - REC button on the audio row: records the speaker sound (or the rig sound card) to MP3, WAV or FLAC, with adjustable colour and width;
  - reduced, mini and PTT (PTT button only, with the Always on top box) views;
  - suggestions while typing in the WebSDR bar (name or address, chosen list);
  - favorites in up to 10 lists with dead-link and duplicate checks, Firefox-compatible export/import by list, search for WebSDRs near France;
  - WebSDR, rig, ExpertSDR (Mute ExpertSDR, through Windows or TCI) and HDSDR volume (Mute HDSDR, through Windows), rig audio also on the PC if wanted (right-click Mute TRX);
  - SDRLINK audio cable for WSJT-X, automatic notch;
  - automatic decoding of FreeDV RADE digital voice (V1 or V2) as soon as a signal is received (FreeDV auto button);
  - digital mode decoders on a single button: FT8, FT4, FT2, WSPR, JT9, JS8, CW, RTTY, PSK31/63, Olivia, Contestia, Hell, SSTV (26 modes) and WEFAX, shown in a movable window over the page (pictures saved as PNG);
  - interface in 23 European languages (menu on the 2nd line);
  - dark mode, colour palette, colour of each button of your choice (right-click) and adjustable text size;
  - update check at startup, import/export of all settings to another computer, with selective import (configuration, WebSDR, memories, scan; add or replace), full or partial reset of the program;
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
