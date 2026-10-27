# SDRLink V1.3 — @F5PBG 2026

SDRLink garde un **WebSDR** calé sur votre **transceiver**. Le WebSDR suit le VFO du poste et le poste suit la cascade du WebSDR. Le mode est synchronisé aussi.

- **Interfaces** : port COM (CAT direct), OmniRig, TCI, Ham Radio Deluxe.
- **Postes** : Yaesu (FT-2000, FTdx10, FTdx101, FT-991, FT-817/818…), Icom (IC-7000, IC-7300, IC-7760, IC-706…), Kenwood, Elecraft, Xiegu (G90, X6100, X6200).
- **WebSDR** : websdr.org, KiwiSDR, OpenWebRX / OpenWebRX+, UberSDR, PhantomSDR.
- **Fonctions** :
  - bouton PTT et coupure du son du WebSDR en émission ;
  - molette sur les fréquences, saisie en kHz avec historique, boutons de mode ;
  - favoris avec vérification des liens, recherche des WebSDR proches de la France ;
  - volume du WebSDR et du poste ;
  - mode sombre et palette, français et anglais.

## Télécharger

L'exe prêt à l'emploi se trouve dans la section **Releases** de ce dépôt. C'est un seul fichier, pour Windows 10 et 11.

## Notice

Voir [LISEZMOI.md](LISEZMOI.md).

## Recompiler

Aucun Visual Studio n'est nécessaire : le compilateur C# fourni avec Windows (.NET Framework 4.x) suffit.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Le script télécharge le paquet WebView2 (NuGet) au premier lancement et produit `dist\SDRLink.exe`.
