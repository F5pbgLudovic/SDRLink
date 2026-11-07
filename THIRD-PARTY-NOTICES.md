# Composants de tiers / Third-party components

SDRLink est distribué sous la licence **GNU GPL v3** (fichier [LICENSE](LICENSE)). Il embarque les composants suivants, dont les licences sont compatibles avec la GPL v3.

| Composant | Rôle dans SDRLink | Licence | Dossier |
|---|---|---|---|
| **radae_nopy** et **rade_c** (David Rowe, Peter B Marks, FreeDV) | FreeDV auto : réception FreeDV RADE V1 et V2 | BSD 2 clauses | `native/freedv/rade/`, `native/freedv/rade2/` (LICENSE) |
| **Opus** (Xiph.Org, dnn) | réseaux de neurones et vocodeur FARGAN de FreeDV | BSD 3 clauses | `native/freedv/opus/` (COPYING) |
| **ft8_lib** (Kārlis Goba), légèrement modifiée pour FT2 | décodage FT8, FT4, FT2 | MIT | `native/digi/ft8lib/` (LICENSE) |
| **Bibliothèque MFSK de Pawel Jalocha** (reprise de fldigi, W1HKJ et contributeurs) | décodage Olivia et Contestia | GNU GPL | `native/digi/jalocha/` |
| **JS8Call** (Jordan Sherer KN4CRD et contributeurs) | données du décodeur JS8 : code LDPC (174,87), CRC 12 bits, synchros de Costas, Varicode, Huffman, dictionnaire JSC | GNU GPL v3 | `data/js8jsc.bin.gz`, `src/DigiJs8.cs` |
| **fldigi** (feld.cxx) | paramètres de modulation Hell (cadence, filtres, écarts de tonalité) | GNU GPL v3 | `src/DigiHell.cs` |
| **Microsoft Edge WebView2 SDK** | navigateur intégré (téléchargé par `build.ps1` depuis NuGet, embarqué dans l'exe) | licence du SDK WebView2 (redistribution autorisée) | `packages/` (non versionné) |

Les décodeurs SSTV, CW, RTTY, PSK, Hell, WSPR, JT9, JS8 sont repris de PhoneSDRLink (même auteur, GPL v3) ; WSPR et JT9 sont écrits d'après les descriptions publiées des protocoles (K1JT et al., WSJT-X). Le décodeur WEFAX est écrit pour SDRLink. FT2 : aucun code de Decodium ni de WSJT-X ; le moteur FT4 de ft8_lib est utilisé avec des symboles de 24 ms, d'après les paramètres publiés.

Sources : https://github.com/drowe67/radae_nopy · https://github.com/freedv/rade_c · https://github.com/xiph/opus · https://github.com/kgoba/ft8_lib · https://github.com/w1hkj/fldigi · https://github.com/JS8Call-improved/JS8Call-improved

---

SDRLink is released under the **GNU GPL v3** (see [LICENSE](LICENSE)). It includes the components listed above, whose licences are compatible with the GPL v3: radae_nopy / rade_c (2-clause BSD) and Opus (3-clause BSD) for FreeDV; ft8_lib (MIT, slightly modified for FT2) for FT8 / FT4 / FT2; Pawel Jalocha's MFSK library (taken from fldigi, GNU GPL) for Olivia and Contestia; JS8Call data (GNU GPL v3) for JS8; Hell modulation parameters from fldigi (GPL v3); the Microsoft Edge WebView2 SDK (redistributable). The SSTV, CW, RTTY, PSK, Hell, WSPR, JT9 and JS8 decoders come from PhoneSDRLink (same author, GPL v3); the WEFAX decoder is written for SDRLink.
