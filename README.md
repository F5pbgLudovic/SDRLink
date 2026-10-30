# SDRLink V1.11 — @F5PBG 2026

SDRLink garde un **WebSDR** calé sur votre **transceiver**. Le WebSDR suit le VFO du poste et le poste suit la cascade du WebSDR. Le mode est synchronisé aussi.

- **Interfaces** : port COM (CAT direct), OmniRig, TCI, Ham Radio Deluxe, Hamlib (rigctld).
- **Postes** : Yaesu (FT-2000, FTdx10, FTdx101, FT-991, FT-817/818, FT-857, FT-897, FT-990, FT-1000/1000D, FT-1000MP, Mark-V…), Icom (IC-7000, IC-7300, IC-7760, IC-706…), Kenwood, Elecraft, Xiegu (G90, X6100, X6200).
- **WebSDR** : websdr.org, KiwiSDR, OpenWebRX / OpenWebRX+, UberSDR, PhantomSDR.
- **Fonctions** :
  - bouton PTT et coupure du son du WebSDR en émission ;
  - bouton ⇄ qui passe le son d'une réception à l'autre (WebSDR, poste, ExpertSDR) ;
  - molette sur les fréquences, saisie en kHz avec historique, boutons de mode ;
  - mémoires de fréquences nommées, en listes, avec les 20 dernières utilisées ;
  - scan de fréquences ou de mémoires ;
  - bouton REC : enregistrement du son des haut-parleurs (ou de la carte son du poste) en MP3, WAV ou FLAC ;
  - affichages réduit, mini et ptt (bouton PTT seul) ;
  - favoris en 10 listes maximum avec vérification des liens et des doublons, export/import compatible Firefox par listes, recherche des WebSDR proches de la France ;
  - volume du WebSDR, du poste et d'ExpertSDR (Mute ExpertSDR, par Windows ou TCI) ;
  - câble audio SDRLINK pour WSJT-X, notch automatique ;
  - interface en 23 langues européennes ;
  - mode sombre, palette et taille du texte réglable ;
  - vérification des mises à jour au démarrage ;
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
- **Rigs**: Yaesu (FT-2000, FTdx10, FTdx101, FT-991, FT-817/818, FT-857, FT-897, FT-990, FT-1000/1000D, FT-1000MP, Mark-V…), Icom (IC-7000, IC-7300, IC-7760, IC-706…), Kenwood, Elecraft, Xiegu (G90, X6100, X6200).
- **WebSDR**: websdr.org, KiwiSDR, OpenWebRX / OpenWebRX+, UberSDR, PhantomSDR.
- **Features**:
  - PTT button, WebSDR audio muted while transmitting;
  - ⇄ button that switches the audio from one receiver to the next (WebSDR, rig, ExpertSDR);
  - mouse wheel on the frequencies, kHz entry with history, mode buttons;
  - named frequency memories, in lists, with the last 20 used;
  - frequency or memory scan;
  - REC button: records the speaker sound (or the rig sound card) to MP3, WAV or FLAC;
  - reduced, mini and PTT (PTT button only) views;
  - favorites in up to 10 lists with dead-link and duplicate checks, Firefox-compatible export/import by list, search for WebSDRs near France;
  - WebSDR, rig and ExpertSDR volume (Mute ExpertSDR, through Windows or TCI);
  - SDRLINK audio cable for WSJT-X, automatic notch;
  - interface in 23 European languages (menu on the 2nd line);
  - dark mode, colour palette and adjustable text size;
  - update check at startup;
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