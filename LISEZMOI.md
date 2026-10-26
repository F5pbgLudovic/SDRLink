# SDRLink — WebSDR ⇄ Transceiver

SDRLink affiche un WebSDR dans sa fenêtre et le garde calé sur ton poste : tu tournes le VFO du poste, le WebSDR suit ; tu cliques dans la cascade du WebSDR, le poste suit. Le mode suit aussi (LSB/USB/CW/AM/FM).

## Lancer

`dist\SDRLink.exe` : un seul fichier, à copier où on veut (clé USB, autre PC). Pas d'installation. Fonctionne sous Windows 10 et 11.

Au démarrage, SDRLink vérifie les deux composants Windows dont il a besoin. S'il en manque un, il propose de le télécharger et de l'installer :
- **Microsoft .NET Framework 4.6.2 ou plus récent** : fourni avec Windows 10 et 11. Sinon, SDRLink propose .NET Framework 4.8.
- **Microsoft Edge WebView2 Runtime** : sert à afficher les WebSDR. Il est présent sur les Windows à jour ; sinon, SDRLink propose l'installeur officiel Microsoft (environ 2 Mo).

Si le téléchargement échoue, la page officielle de Microsoft s'ouvre dans le navigateur.

Au premier lancement, SDRLink dépose `WebView2Loader.dll` dans `%LOCALAPPDATA%\SDRLink\runtime\` : Windows ne peut pas charger ce fichier directement depuis l'exe.

Pour recompiler après une modification : `powershell -ExecutionPolicy Bypass -File build.ps1` (résultat dans `dist\`).

## Interfaces (menu « Interface »)

| Interface | Réglages | Remarque |
|---|---|---|
| Port COM (CAT direct) | Marque, modèle, port, vitesse, bits de stop, RTS/DTR, adresse CI-V (Icom) | Le port ne doit pas être ouvert par un autre logiciel |
| OmniRig | Rig 1 / Rig 2 | Le poste se configure dans OmniRig ; permet de partager le poste avec d'autres logiciels |
| TCI | Hôte / port (40001 par défaut) | ExpertSDR (SunSDR), Thetis… activer le serveur TCI |
| Ham Radio Deluxe | Hôte / port (7809 par défaut) | Activer le serveur IP dans HRD Rig Control |

## Postes en CAT direct

- **Yaesu** : FT-2000, FTdx10, FTdx101, FT-991/A, FT-891, FT-710, FTdx5000, FTdx3000, FTdx1200, FT-950, FT-450, FT-817, FT-817ND, FT-818, FT-818ND
- **Icom** : IC-7000, IC-7760, IC-7300, IC-7610, IC-705, IC-9700, IC-7100, IC-7200, IC-7410, IC-7600, IC-7700, IC-7800, IC-7851, IC-756PROIII, IC-746PRO, IC-718, IC-706, IC-706MKII, IC-706MKIIG
- **Kenwood** : TS-590, TS-890, TS-990, TS-2000, TS-480, TS-870, TS-570, TS-850S
- **Elecraft** : K4, K3/K3S, KX3, KX2, K2
- **Xiegu** (CI-V compatible Icom) : G90, X6100, X6200

Quand on choisit un modèle, la vitesse et l'adresse CI-V se règlent sur les valeurs d'usine. Il faut qu'elles correspondent au menu du poste :

- **FT-2000** : menu CAT RATE (4800 par défaut). Testé chez toi : 38400, 1 bit de stop, RTS décoché.
- **FTdx10** : menu CAT RATE ; sur l'USB, prendre le port COM « Enhanced ». Si rien ne répond, essayer RTS coché/décoché.
- **IC-7000** : adresse CI-V 70 (hex), vitesse CI-V du menu (souvent 19200 ou Auto). CI-V Transceive peut rester sur ON.
- **FT-817/818** : ancien protocole CAT binaire de Yaesu, 2 bits de stop, vitesse selon le menu CAT RATE (4800 par défaut).
- **TS-850S** : 4800 bauds, 2 bits de stop.
- **IC-7760** : adresse B2. **IC-706** : adresse 48. **IC-706MKII** : adresse 4E.
- **Xiegu** : l'adresse CI-V dépend du firmware. Si le poste ne répond pas, essayer une autre adresse :
  - G90 : 70, sinon 88 ;
  - X6100 : A4, sinon 70 pour les firmwares antérieurs au 1.1.8 ;
  - X6200 : A4.
  Régler la vitesse comme dans le menu CI-V du poste.

## WebSDR compatibles

Le type est reconnu automatiquement (affiché entre parenthèses) :
- **websdr.org** (PA3FWM), ex. Twente
- **UberSDR** (interface v2), ex. https://wessex.zapto.org/v2/
- **PhantomSDR** (avec le module CATsync), ex. http://linkz.ddns.net:8088/ (French Alps WebSDR)
- **KiwiSDR**
- **OpenWebRX / OpenWebRX+** (seulement dans la plage de la bande affichée)

## Favoris

- La liste déroulante de la barre WebSDR affiche les favoris par leur **nom**. En choisir un ouvre le WebSDR.
- **★ Ajouter aux favoris** : ajoute la page affichée, nommée d'après le titre de la page (modifiable).
- **Retirer des favoris** : retire la page affichée des favoris, après confirmation.
- **Gérer les favoris…** ouvre le gestionnaire :
  - **Ajouter un favori…** ;
  - une **case à cocher** devant chaque favori. **Supprimer…** retire tous les favoris cochés en une fois, ou le favori sélectionné si aucun n'est coché.
  - **Renommer…**, **Monter**, **Descendre** pour le favori sélectionné ;
  - **Vérifier les favoris** :
    - interroge chaque adresse (10 à la fois, 8 s maximum chacune) et affiche l'état dans la colonne État ;
    - les **liens morts** (pas de réponse, nom inconnu, connexion refusée, page introuvable 404) et les **doublons** (même adresse) sont **cochés automatiquement** et affichés en rouge ;
    - il ne reste qu'à cliquer sur « Supprimer… ». Un serveur qui répond, même par un refus d'accès, est considéré comme vivant.
  - **Tout décocher** ;
  - champs **Nom** et **Adresse** : ils affichent le favori sélectionné et se corrigent directement. La liste se met à jour en tapant. Un champ laissé vide n'est pas appliqué.
  - **Ouvrir** ou double-clic : ouvre le WebSDR. La touche Suppr supprime, Entrée ouvre.
  - **Importer…** :
    - favoris exportés d'un navigateur (Edge, Chrome, Firefox : fichier `.html`) ;
    - fichier CSV `nom;adresse` ;
    - simple liste d'adresses.
    Les adresses déjà présentes sont ignorées.
  - **Exporter…** : CSV (lisible par Excel) ou HTML (importable dans un navigateur).
- Les favoris de départ comprennent les annuaires websdr.org, KiwiSDR et OpenWebRX, ainsi que Twente, Wessex et French Alps.

## Rechercher des WebSDR

Le bouton **Rechercher des WebSDR…** (barre WebSDR) consulte trois annuaires publics :
- **websdr.org** ;
- la liste officielle des **KiwiSDR** ;
- **ReceiverBook** (OpenWebRX, WebSDR, KiwiSDR).

Les doublons sont retirés. La liste s'affiche **triée par distance depuis le centre de la France**.
- **Distance max** (1000 km par défaut) : France, Belgique, Luxembourg, Suisse, nord de l'Italie et de l'Espagne, ouest de l'Allemagne, Pays-Bas, sud de l'Angleterre… À augmenter ou réduire à volonté.
- **Filtre** : texte libre (ville, indicatif, département…). **Type** : websdr.org, KiwiSDR, OpenWebRX…
- **Masquer ceux déjà en favoris** (coché par défaut).
- Clic sur un titre de colonne : tri par cette colonne.
- **Pays** : estimé d'après le nom (pays cité, indicatif français F…/TK…) ou, pour la France, d'après la position GPS. Vide si inconnu.
- **Écouter la sélection** (ou double-clic) : ouvre le récepteur dans SDRLink pour l'essayer.
- Cocher ceux qui intéressent, puis **Ajouter les cochés aux favoris**. Ils se renomment ensuite dans « Gérer les favoris… ».

Il faut un accès Internet. Si un annuaire ne répond pas, les autres sont quand même affichés : le bas de la fenêtre indique le résultat de chaque source.

## Volume du WebSDR

Le curseur **Volume**, à droite de « Gérer les favoris… », règle uniquement le son du WebSDR, pas celui de Windows ni des autres logiciels.
- Il agit sur tous les types de WebSDR. Sur UberSDR, le son ne démarre qu'après un clic sur « Click to start » dans la page.
- Le volume est **mémorisé pour chaque favori** : en rouvrant un favori, on retrouve son volume.
- Les WebSDR qui ne sont pas en favoris partagent un même volume, lui aussi mémorisé.
- Un favori ajouté garde le volume du moment. L'export CSV inclut le volume, et l'import le reprend.

## Coupure du son et volume du poste

À droite du volume du WebSDR :
- **Mute WebSDR** : coupe le son du WebSDR (bouton rouge), un second clic le rétablit au volume réglé.
- **Mute TX** : coupe le son du **transceiver** en mettant son gain BF à zéro par CAT, puis rétablit le volume au second clic. Le son est aussi rétabli à la déconnexion.
- **Volume TX** : règle le volume du transceiver (gain BF, en %). La barre suit aussi le bouton du poste : elle est relue toutes les 2 secondes environ.

Commandes utilisées (vérifiées dans le manuel CAT Yaesu et dans Hamlib) :
- **Yaesu** : `AG0nnn;`, de 000 à 255.
- **Kenwood** : `AGnnn;` ou `AG0nnn;` selon le modèle, détecté automatiquement. De 000 à 255.
- **Elecraft** : `AGnnn;`, de 000 à 250 (K3, KX3, KX2) ou de 000 à 060 (K4).
- **Icom / Xiegu** : CI-V `14 01`, valeur de 0000 à 0255.
- **TCI** : `volume:` de −60 à 0 dB.
- **Non disponible** avec FT-817/818 (pas de commande), OmniRig (pas de réglage de volume) et HRD (commande non documentée). La barre est alors grisée et un message l'explique.

## Mode sombre

La case **Mode sombre**, à droite de « Ouvrir le journal », passe toute l'interface en couleurs sombres : fenêtre, barre de titre (Windows 10 20H1 et plus, Windows 11), listes, boutons et gestionnaire de favoris. La page du WebSDR garde ses propres couleurs.
- Les fréquences, le PTT et les boutons de mode prennent des teintes plus claires, lisibles dans la pénombre.
- Les réglages grisés pendant la connexion restent lisibles.
- **Palette** : le petit carré multicolore à droite de « Mode sombre » ouvre un choix de couleurs de fond.
  - En mode sombre : uniquement des teintes foncées (noir, graphite, bleus nuit, violet, bordeaux, rouge très foncé, brun, vert et bleu-vert foncés).
  - En mode clair : uniquement des teintes claires (blanc, gris perle, crème, sable, jaune pâle, bleus, vert d'eau, rose, lavande).
  - Le reste de l'interface (champs, boutons, bordures) s'accorde automatiquement. « Par défaut » revient aux couleurs d'origine.
  - Le choix est mémorisé séparément pour le mode sombre et le mode clair.

## Langue

Le menu **Langue / Language**, sur la 2ᵉ ligne, bascule toute l'interface entre français et anglais, immédiatement et sans redémarrer. Le choix est conservé. Le journal de diagnostic reste en français.

## Synchronisation

- **Bidirectionnelle** / **Poste → WebSDR** / **WebSDR → Poste** / **Désactivée**
- **Décalage WebSDR (Hz)** : ajouté à la fréquence du poste côté WebSDR (transverter, écart de calibration…)
- **◀ Retour** (barre WebSDR) : revient à la page précédente, comme la flèche retour d'un navigateur. Utile après avoir suivi un lien, par exemple dans un annuaire de WebSDR.
- Si le poste est en DATA-U et le WebSDR en USB, SDRLink ne change pas le mode du poste (même famille).

## Bouton PTT

Le gros bouton **PTT**, à gauche de « POSTE », fait passer le poste en émission par CAT : un clic pour émettre (le bouton devient rouge « ● TX »), un second clic pour revenir en réception. C'est l'équivalent du bouton MOX de HRD.
- Il envoie `TX1;`/`TX0;` aux Yaesu, `TX;`/`RX;` aux Kenwood et Elecraft, `1C 00` aux Icom, `trx` en TCI.
- Avec OmniRig, il agit sur la propriété `Tx` ; avec HRD, sur le bouton MOX.
- Le son du WebSDR est coupé dès le clic, même si le poste ne signale pas son état d'émission (si « Couper le son du WebSDR en émission » est cochée).
- Par sécurité, si on se déconnecte ou si on ferme SDRLink pendant une émission lancée par ce bouton, le poste repasse en réception.
- **Émission sans modulation** (constaté sur un FTdx10) : le PTT par CAT met bien le poste en émission, mais le son émis dépend de la **source de modulation** du poste. Si elle est réglée sur l'arrière ou l'USB (« REAR », souvent le cas pour le FT8), le micro n'est pas transmis. Sur le FTdx10, vérifier dans les menus de réglage de l'émission (TX AUDIO) la source de modulation SSB (MOD SOURCE : MIC) et le gain micro. Les autres logiciels qui passent en émission par CAT, comme WSJT-X, rencontrent le même comportement.

## Coupure du son en émission

Coche **« Couper le son du WebSDR en émission »** (activée par défaut) : pendant que le poste émet, le son du WebSDR est coupé pour ne pas s'entendre. L'indication **● TX** s'affiche alors en rouge à côté de « POSTE ». Décocher pour garder le son en émission.

**« rétablir après (ms) »** (1000 par défaut) : le son d'un WebSDR arrive avec 0,5 à 2 s de retard, il contient donc encore la fin de ton émission après le relâchement du PTT. Augmente cette valeur si tu entends la fin de tes passages, mets 0 pour rétablir le son tout de suite.

Lecture de l'état émission selon l'interface :
- Yaesu : commande `TX;`
- Kenwood/Elecraft : commande `IF;`
- Icom : commande CI-V `1C 00`
- OmniRig : propriété `Tx`
- TCI : message `trx`
- HRD : boutons `MOX`, `TX`, `PTT` (et `VOX`, voir ci-dessous)

Si le poste ne fournit pas cette information, le journal l'indique et le son n'est jamais coupé.

**Avec HRD, la pédale ou le PTT du micro ne sont pas toujours vus.** Le serveur IP de HRD n'a pas de commande « état d'émission ». Avec le FT-2000, le bouton MOX ne change que si on clique dessus dans HRD. Avec la pédale, seul le bouton VOX change d'état. Le menu **« Détection TX (HRD) »** propose deux choix :
- **Boutons MOX/TX/PTT** : émission seulement si l'un de ces boutons est enfoncé.
- **Boutons MOX/VOX/TX/PTT** : en plus, SDRLink relève la valeur du bouton VOX à la connexion, puis considère toute autre valeur comme une émission.
  - Se connecter quand le poste est en réception.
  - HRD met environ 1 s à mettre ce bouton à jour.
  - À éviter si on utilise réellement le VOX du poste.

Changer de choix reconnecte automatiquement à HRD.

**Solution la plus rapide si le poste est partagé par des ports virtuels** (com0com + hub4com, HRD sur un port) : connecter SDRLink en **Port COM (CAT direct)** sur un autre port virtuel du partage, HRD restant ouvert. SDRLink interroge alors directement le poste (commande `TX;`), sans passer par la lenteur de HRD.

## Boutons de mode

Les boutons **USB, LSB, AM, FM**, à droite de la fréquence WEBSDR, changent le mode du poste **et** du WebSDR en même temps. Le bouton du mode en cours est en vert.

Les boutons gardent toujours la même place : les zones POSTE et WEBSDR ont une largeur fixe. Le mode du WebSDR y est affiché en forme courte (USB, LSB, CW, AM, FM). L'émission est signalée par la fréquence POSTE en rouge et par le bouton PTT. La FM devient NBFM sur KiwiSDR et NFM sur UberSDR et OpenWebRX.

## Saisie d'une fréquence

- Taper une fréquence en **kHz** dans le champ « kHz » (ex. `7074`, `14074,5` ou `14074.5`), puis **Entrée** ou **QSY** : le poste et le WebSDR s'y accordent.
- Le menu déroulant garde les **100 dernières** fréquences, la plus récente en haut. Choisir une valeur dans la liste y accorde directement.
- **Save** ajoute la fréquence en cours à la liste.
- **Clic droit** sur le champ : « Supprimer … de la liste » (la valeur affichée) ou « Tout supprimer » (une confirmation est demandée).
- La liste est conservée au prochain lancement.

## Molette sur les fréquences

Laisser la souris sur **POSTE** ou **WEBSDR** et tourner la molette : le poste **et** le WebSDR changent de fréquence ensemble, quel que soit le sens de synchronisation choisi.
- Le **pas** se choisit dans le menu « Pas » de la même ligne : 100 Hz, 250 Hz, 500 Hz, 1 kHz, 2,5 kHz, 5 kHz ou 10 kHz.
- La fréquence est arrondie au pas : au pas de 1 kHz, on tombe sur des kHz ronds.
- **Accélération** si le pas est inférieur à 1 kHz, selon le temps entre deux crans :
  - moins de 100 ms : pas × 2 ;
  - moins de 60 ms : pas × 5 ;
  - moins de 30 ms : pas × 10.

## Affichage

- **Affichage réduit** : la fenêtre s'arrête juste après la ligne PTT / POSTE / WEBSDR. La barre d'adresse et la page du WebSDR sont masquées, mais le WebSDR continue de jouer et de suivre le poste. Décocher pour retrouver la grande fenêtre.
- La position et la taille de la fenêtre en affichage normal sont mémorisées à la fermeture, même si on ferme en affichage réduit. Au prochain lancement, décocher « Affichage réduit » redonne la taille d'avant. Si la fenêtre était en plein écran avant l'affichage réduit, elle y revient.
- **Avant plan** : SDRLink reste au-dessus des autres fenêtres.

Ces deux réglages sont conservés au prochain lancement.

## Dépannage

Réglages : `%APPDATA%\SDRLink\settings.ini`. Journal : `%APPDATA%\SDRLink\sdrlink.log`. Le journal est remis à zéro à chaque lancement et contient l'état de la connexion et, pour HRD, la liste des modes reçue.
