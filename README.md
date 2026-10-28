# SDRLink V1.5 — @F5PBG 2026

SDRLink garde un **WebSDR** calé sur votre **transceiver**. Le WebSDR suit le VFO du poste et le poste suit la cascade du WebSDR. Le mode est synchronisé aussi.

- **Interfaces** : port COM (CAT direct), OmniRig, TCI, Ham Radio Deluxe, Hamlib (rigctld).
- **Postes** : Yaesu (FT-2000, FTdx10, FTdx101, FT-991, FT-817/818, FT-857, FT-897…), Icom (IC-7000, IC-7300, IC-7760, IC-706…), Kenwood, Elecraft, Xiegu (G90, X6100, X6200).
- **WebSDR** : websdr.org, KiwiSDR, OpenWebRX / OpenWebRX+, UberSDR, PhantomSDR.
- **Fonctions** :
  - bouton PTT et coupure du son du WebSDR en émission ;
  - molette sur les fréquences, saisie en kHz avec historique, boutons de mode ;
  - favoris en 4 listes avec vérification des liens et des doublons, recherche des WebSDR proches de la France ;
  - volume du WebSDR, du poste et d'ExpertSDR (Mute ExpertSDR, par Windows ou TCI) ;
  - câble audio SDRLINK pour WSJT-X, notch automatique ;
  - interface en 23 langues européennes ;
  - mode sombre et palette.

## Télécharger

**[Télécharger la dernière version](https://github.com/F5pbgLudovic/SDRLink/releases/latest)** (`SDRLink.exe`, ou le `.zip` qui contient aussi la notice). C'est un seul fichier, sans installation, pour Windows 10 et 11.

## Notice

Voir [LISEZMOI.md](LISEZMOI.md).

## Recompiler

Aucun Visual Studio n'est nécessaire : le compilateur C# fourni avec Windows (.NET Framework 4.x) suffit.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Le script télécharge le paquet WebView2 (NuGet) au premier lancement et produit `dist\SDRLink.exe`.

## English

SDRLink keeps a **WebSDR** (websdr.org, KiwiSDR, OpenWebRX, UberSDR, PhantomSDR) in sync with your **transceiver**: frequency, mode and PTT, both ways. Rig control through a COM port (CAT), OmniRig, TCI, Ham Radio Deluxe or Hamlib (rigctld). The WebSDR audio can be sent to a virtual audio cable (SDRLINK) for WSJT-X. The interface is available in 23 European languages (menu on the 2nd line). Single exe, no installation, Windows 10/11: see **[Releases](https://github.com/F5pbgLudovic/SDRLink/releases/latest)**.