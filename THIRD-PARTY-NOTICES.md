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
| **RNNoise** 0.1.1 (Jean-Marc Valin, Mozilla, Xiph.Org) | DNR IA : réseau de neurones de réduction de bruit, porté en JavaScript avec ses poids d'origine (depuis la 1.48) | BSD 3 clauses (texte ci-dessous) | `src/WebSdrBridge.cs`, `src/RnnoiseWeights.cs` |
| **Microsoft Edge WebView2 SDK** | navigateur intégré (téléchargé par `build.ps1` depuis NuGet, embarqué dans l'exe) | licence du SDK WebView2 (redistribution autorisée) | `packages/` (non versionné) |

Les décodeurs SSTV, CW, RTTY, PSK, Hell, WSPR, JT9, JS8 sont repris de PhoneSDRLink (même auteur, GPL v3) ; WSPR et JT9 sont écrits d'après les descriptions publiées des protocoles (K1JT et al., WSJT-X). Le décodeur WEFAX est écrit pour SDRLink. FT2 : aucun code de Decodium ni de WSJT-X ; le moteur FT4 de ft8_lib est utilisé avec des symboles de 24 ms, d'après les paramètres publiés.

Sources : https://github.com/drowe67/radae_nopy · https://github.com/xiph/rnnoise · https://github.com/freedv/rade_c · https://github.com/xiph/opus · https://github.com/kgoba/ft8_lib · https://github.com/w1hkj/fldigi · https://github.com/JS8Call-improved/JS8Call-improved

---

SDRLink is released under the **GNU GPL v3** (see [LICENSE](LICENSE)). It includes the components listed above, whose licences are compatible with the GPL v3: radae_nopy / rade_c (2-clause BSD) and Opus (3-clause BSD) for FreeDV; ft8_lib (MIT, slightly modified for FT2) for FT8 / FT4 / FT2; Pawel Jalocha's MFSK library (taken from fldigi, GNU GPL) for Olivia and Contestia; RNNoise 0.1.1 (3-clause BSD, ported to JavaScript with its original weights, since 1.48) for the AI DNR; JS8Call data (GNU GPL v3) for JS8; Hell modulation parameters from fldigi (GPL v3); the Microsoft Edge WebView2 SDK (redistributable). The SSTV, CW, RTTY, PSK, Hell, WSPR, JT9 and JS8 decoders come from PhoneSDRLink (same author, GPL v3); the WEFAX decoder is written for SDRLink.

---

## Licence de RNNoise / RNNoise licence

```
Copyright (c) 2017, Mozilla
Copyright (c) 2007-2017, Jean-Marc Valin
Copyright (c) 2005-2017, Xiph.Org Foundation
Copyright (c) 2003-2004, Mark Borgerding

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

- Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.

- Redistributions in binary form must reproduce the above copyright
notice, this list of conditions and the following disclaimer in the
documentation and/or other materials provided with the distribution.

- Neither the name of the Xiph.Org Foundation nor the names of its
contributors may be used to endorse or promote products derived from
this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE FOUNDATION
OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
