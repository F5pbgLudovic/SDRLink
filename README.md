# SDRLink V1.15 — @F5PBG 2026

SDRLink garde un **WebSDR** calé sur votre **transceiver**. Le WebSDR suit le VFO du poste et le poste suit la cascade du WebSDR. Le mode est synchronisé aussi.

- **Interfaces** : port COM (CAT direct), OmniRig, TCI, Ham Radio Deluxe, Hamlib (rigctld).
- **Postes en CAT direct (128 modèles, classés par numéro)** : Yaesu (40, du FT-100 au FTX-1, dont les anciens FT-747GX, FT-757GX/GXII, FT-767GX, FT-840, FT-890, FT-900, FT-920, FT-990, FT-1000…), Icom (55, de l'IC-78 à l'IC-9700, dont les IC-7xx des années 80), Kenwood (23, du TS-50S au TS-B2000), Elecraft (5), Xiegu (G90, X108G, X5105, X6100, X6200). Les autres postes passent par OmniRig, HRD ou Hamlib.
- **WebSDR** : websdr.org, KiwiSDR, OpenWebRX / OpenWebRX+, UberSDR, PhantomSDR.
- **Fonctions** :
  - bouton PTT et coupure du son du WebSDR en émission ;
  - bouton ⇄ qui passe le son d'une réception à l'autre (WebSDR, poste, ExpertSDR, HDSDR), deux réceptions pouvant être laissées de côté ;
  - molette sur les fréquences, saisie en kHz avec historique, boutons de mode ;
  - mémoires de fréquences nommées, en listes, avec les 20 dernières utilisées ;
  - scan de fréquences ou de mémoires ;
  - bouton REC sur la ligne de son : enregistrement du son des haut-parleurs (ou de la carte son du poste) en MP3, WAV ou FLAC, couleur et largeur réglables ;
  - affichages réduit, mini et ptt (bouton PTT seul) ;
  - favoris en 10 listes maximum avec vérification des liens et des doublons, export/import compatible Firefox par listes, recherche des WebSDR proches de la France ;
  - volume du WebSDR, du poste, d'ExpertSDR (Mute ExpertSDR, par Windows ou TCI) et de HDSDR (Mute HDSDR, par Windows) ;
  - câble audio SDRLINK pour WSJT-X, notch automatique ;
  - interface en 23 langues européennes ;
  - mode sombre, palette, couleur de chaque bouton au choix (clic droit) et taille du texte réglable ;
  - vérification des mises à jour au démarrage, import/export de tous les réglages vers un autre ordinateur ;
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

## English

SDRLink keeps a **WebSDR** locked to your **transceiver**. The WebSDR follows the rig's VFO and the rig follows the WebSDR waterfall. The mode is synchronised too.

- **Interfaces**: COM port (direct CAT), OmniRig, TCI, Ham Radio Deluxe, Hamlib (rigctld).
- **Rigs in direct CAT (128 models, sorted by number)**: Yaesu (40, from the FT-100 to the FTX-1, including the older FT-747GX, FT-757GX/GXII, FT-767GX, FT-840, FT-890, FT-900, FT-920, FT-990, FT-1000…), Icom (55, from the IC-78 to the IC-9700, including the 1980s IC-7xx), Kenwood (23, from the TS-50S to the TS-B2000), Elecraft (5), Xiegu (G90, X108G, X5105, X6100, X6200). Other rigs work through OmniRig, HRD or Hamlib.
- **WebSDR**: websdr.org, KiwiSDR, OpenWebRX / OpenWebRX+, UberSDR, PhantomSDR.
- **Features**:
  - PTT button, WebSDR audio muted while transmitting;
  - ⇄ button that switches the audio from one receiver to the next (WebSDR, rig, ExpertSDR, HDSDR), with up to two receivers left aside;
  - mouse wheel on the frequencies, kHz entry with history, mode buttons;
  - named frequency memories, in lists, with the last 20 used;
  - frequency or memory scan;
  - REC button on the audio row: records the speaker sound (or the rig sound card) to MP3, WAV or FLAC, with adjustable colour and width;
  - reduced, mini and PTT (PTT button only) views;
  - favorites in up to 10 lists with dead-link and duplicate checks, Firefox-compatible export/import by list, search for WebSDRs near France;
  - WebSDR, rig, ExpertSDR (Mute ExpertSDR, through Windows or TCI) and HDSDR volume (Mute HDSDR, through Windows);
  - SDRLINK audio cable for WSJT-X, automatic notch;
  - interface in 23 European languages (menu on the 2nd line);
  - dark mode, colour palette, colour of each button of your choice (right-click) and adjustable text size;
  - update check at startup, import/export of all settings to another computer;
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