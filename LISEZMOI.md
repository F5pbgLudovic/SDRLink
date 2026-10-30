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

## La fenêtre, ligne par ligne

Vue d'ensemble ; chaque fonction est détaillée plus bas dans sa propre section.

1. **1re ligne (connexion et réglages généraux)** : **Interface** (port COM, OmniRig, TCI, HRD, Hamlib), **Marque** et **Modèle** (CAT direct), **Connecter / Déconnecter**, case **Connexion auto au démarrage**, **Ouvrir le journal** (sdrlink.log dans le Bloc-notes), case **Mode sombre**, carré **Palette**, **Taille du texte…**, case **Vérifier les mises à jour**, **Import/Export ▾**.
2. **2e ligne (paramètres de l'interface)** : selon l'interface, **Port** COM (avec ↻ pour relire la liste), **Vitesse**, **Bits de stop**, **Adresse CI-V** (Icom), **RTS** / **DTR** ; ou **Poste OmniRig** ; ou **Hôte** / **Port** (TCI, HRD, Hamlib) ; **Détection TX (HRD)**. Puis **Langue** et **Câble SDRLINK ▾**. Avec Hamlib, une ligne en plus : **Lancer rigctld**, chemin de rigctld, **N° de modèle Hamlib**, **Télécharger Hamlib…**.
3. **3e ligne (synchronisation)** : **Synchro** (Bidirectionnelle, Radio → WebSDR, WebSDR → Radio, Désactivée), **Synchroniser le mode**, **Décalage WebSDR (Hz)**, **Couper le son du WebSDR en émission** et **rétablir après (ms)**, **Notch auto (DNF)**, **Conf scan**, **▶ Scan**.
4. **4e ligne (affichage et accord)** : bouton **PTT**, fréquence et mode **RADIO**, fréquence et mode **WEBSDR**, boutons **USB, LSB, AM, FM, DIGI, CW**, saisie **kHz** (avec historique), **QSY**, **Save**, **Mémoires** et **▾** (20 dernières), **Pas** de la molette, cases **Affichage réduit**, **Affichage mini**, **Avant plan**, **Affichage ptt**.
5. **5e ligne (WebSDR)** : adresse **WebSDR** (liste des favoris), **liste** de favoris affichée, **Aller**, **◀ Retour**, **Recharger**, **★ Ajouter aux favoris**, **Retirer des favoris**, **Gérer les favoris…**, **Rechercher des WebSDR…**.
6. **Ligne de son** : **⇄**, **Mute WebSDR**, **Mute TX**, **Mute ExpertSDR**, **Volume WebSDR**, **Volume TX**, **Volume ExpertSDR** (seulement les sons disponibles), puis **REC**.
7. **La page du WebSDR**, puis la **barre d'état** (messages, lien de mise à jour).

## Tous les clics droits

| Où | Ce que propose le clic droit |
|---|---|
| Connecter | couleur quand connecté / déconnecté |
| Ouvrir le journal, Taille du texte, Conf scan, QSY, Save, Aller, Retour, Recharger, Ajouter / Retirer / Gérer les favoris, Rechercher des WebSDR, Import/Export | couleur du bouton |
| Câble SDRLINK (aussi au clic gauche) | sortie qui reçoit le son du WebSDR, nom SDRLINK, téléchargement de VB-CABLE, couleur activé / désactivé |
| Scan | couleur scan arrêté / en marche |
| PTT | délai de sécurité TX, couleur en réception / en émission |
| RADIO, WEBSDR | couleur du fond, couleur du texte |
| USB, LSB, AM, FM, DIGI, CW | couleur du mode sélectionné / des autres |
| Saisie kHz | supprimer la valeur de l'historique, tout supprimer |
| Mémoires | mémoriser la fréquence actuelle, couleur du bouton |
| Liste des favoris (à côté de l'adresse) | renommer la liste |
| ⇄ | réception que ⇄ laisse coupée |
| Mute WebSDR, Mute TX | couleur son coupé / son actif |
| Mute ExpertSDR | port TCI d'ExpertSDR, couleur son coupé / son actif |
| « Volume WebSDR » | port TCI d'ExpertSDR |
| REC | carte son du poste, couleur du bouton REC, largeur (étroit / large) |
| Barre d'état | 20 derniers messages, types de messages à ne plus afficher |

## Fichiers de SDRLink

- `%APPDATA%\SDRLink\settings.ini` : **tous les réglages** (interface, favoris, mémoires, couleurs, fenêtre…). C'est ce fichier que copie **Import/Export**.
- `%APPDATA%\SDRLink\settings.avant-import.ini` : les réglages d'avant le dernier import.
- `%APPDATA%\SDRLink\sdrlink.log` : le journal (remis à zéro à chaque lancement).
- `%LOCALAPPDATA%\SDRLink\WebView2` : données des pages WebSDR (cookies, cache) ; `%LOCALAPPDATA%\SDRLink\runtime` : WebView2Loader.dll.
- `Musique\SDRLink` : dossier des enregistrements REC par défaut ; pendant un enregistrement, un fichier provisoire `SDRLink-REC-*.raw` est dans `%TEMP%`.

## Interfaces (menu « Interface »)

| Interface | Réglages | Remarque |
|---|---|---|
| Port COM (CAT direct) | Marque, modèle, port, vitesse, bits de stop, RTS/DTR, adresse CI-V (Icom) | Le port ne doit pas être ouvert par un autre logiciel |
| OmniRig | Rig 1 / Rig 2 | Le poste se configure dans OmniRig ; permet de partager le poste avec d'autres logiciels. **Si OmniRig ne fonctionne pas, utiliser OmniRig version 1, en mode administrateur** (voir Dépannage) |
| TCI | Hôte / port (40001 par défaut) | ExpertSDR (SunSDR), Thetis… activer le serveur TCI |
| Ham Radio Deluxe | Hôte / port (7809 par défaut) | Activer le serveur IP dans HRD Rig Control |
| Hamlib (rigctld) | Hôte / port (4532 par défaut), « Lancer rigctld », n° de modèle Hamlib | Voir ci-dessous |

## Hamlib

Hamlib est la bibliothèque libre de télécommande des postes utilisée par WSJT-X, fldigi, JTDX… Elle reconnaît plus de 300 modèles. SDRLink dialogue avec **rigctld**, le petit serveur réseau de Hamlib.
- **Lancer rigctld** (coché par défaut) : SDRLink démarre lui-même rigctld à la connexion et l'arrête à la déconnexion. Il utilise le poste, le port COM, la vitesse, les bits de stop (et RTS/DTR s'ils sont cochés) de la ligne de réglages, qui s'affiche alors.
  - L'emplacement de `rigctld.exe` est trouvé automatiquement (installation de Hamlib, PATH, puis WSJT-X/JTDX). Sinon, le bouton **…** permet de l'indiquer.
  - **Télécharger Hamlib…** ouvre la page officielle (github.com/Hamlib/Hamlib/releases). Prendre `hamlib-w64-….exe` (installeur) ou le `.zip`.
  - Le `rigctld-wsjtx.exe` fourni avec WSJT-X ou WSJT-Z ne démarre pas toujours seul, hors de WSJT : dans ce cas, installer Hamlib.
- **N° de modèle Hamlib** : rempli d'après le modèle choisi (FT-2000 = 1029, FTdx10 = 1042, IC-7000 = 3060…). Pour un autre poste, taper le numéro de la liste Hamlib (`rigctl -l`).
- **Décoché** : SDRLink se connecte à un rigctld déjà lancé, à l'adresse **Hôte / Port** indiquée (même sur un autre PC).
- Le port COM ne peut servir qu'à un seul logiciel : ne pas lancer rigctld sur le port déjà ouvert par HRD ou OmniRig.
- Fonctions : fréquence, mode (le mode DIGI passe par PKTUSB/PKTLSB, aussi sur les Icom), PTT, état émission, volume (Mute TX, Volume TX) si le poste le permet.
- En cas d'erreur (port COM occupé, modèle inconnu…), le message de rigctld s'affiche dans la barre d'état et dans le journal.

## Postes en CAT direct

- **Yaesu** : FT-2000, FTdx10, FTdx101, FT-991/A, FT-891, FT-710, FTdx5000, FTdx3000, FTdx1200, FT-950, FT-450, FT-817, FT-817ND, FT-818, FT-818ND, FT-857, FT-857D, FT-897, FT-897D, FT-990, FT-1000, FT-1000D, FT-1000MP, FT-1000MP Mark-V, FT-1000MP Mark-V Field
- **Icom** : IC-7000, IC-7760, IC-7300, IC-7610, IC-705, IC-9700, IC-7100, IC-7200, IC-7410, IC-7600, IC-7700, IC-7800, IC-7851, IC-756PROIII, IC-746PRO, IC-718, IC-706, IC-706MKII, IC-706MKIIG
- **Kenwood** : TS-590, TS-890, TS-990, TS-2000, TS-480, TS-870, TS-570, TS-850S
- **Elecraft** : K4, K3/K3S, KX3, KX2, K2
- **Xiegu** (CI-V compatible Icom) : G90, X6100, X6200

Quand on choisit un modèle, la vitesse et l'adresse CI-V se règlent sur les valeurs d'usine. Il faut qu'elles correspondent au menu du poste :

- **FT-2000** : menu CAT RATE (4800 par défaut). Testé chez toi : 38400, 1 bit de stop, RTS décoché.
- **FTdx10** : menu CAT RATE ; sur l'USB, prendre le port COM « Enhanced ». Si rien ne répond, essayer RTS coché/décoché.
- **IC-7000** : adresse CI-V 70 (hex), vitesse CI-V du menu (souvent 19200 ou Auto). CI-V Transceive peut rester sur ON.
- **FT-817/818, FT-857/857D, FT-897/897D** : ancien protocole CAT binaire de Yaesu, 2 bits de stop, vitesse selon le menu CAT RATE du poste (4800 par défaut). Le volume du poste (Mute TX, Volume TX) n'est pas réglable par ce protocole.
- **FT-990, FT-1000, FT-1000D, FT-1000MP, Mark-V** : ancien protocole CAT binaire (le même que Hamlib), 4800 bauds, 2 bits de stop, sans réglage de vitesse sur le poste. Fréquence au pas de 10 Hz. Pas de PKT côté USB : un mode données USB du WebSDR met le poste en USB. Le volume du poste (Mute TX, Volume TX) n'est pas réglable. Le FT-1000MP et le Mark-V ne disent pas de façon sûre par CAT s'ils émettent : en CAT direct, la coupure automatique du son à l'émission ne marche donc pas avec eux (le FT-990 et le FT-1000/1000D, eux, le disent). Le FT-990 est lent à recevoir les commandes (50 ms par octet, comme dans Hamlib), le suivi du VFO y est donc un peu plus lent. Par OmniRig ou HRD, choisir le poste dans ces logiciels ; par Hamlib, le numéro de modèle se remplit tout seul (FT-990 : 1016 ; si votre FT-990 a une ancienne ROM et ne répond pas, essayer 1048).
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
- **Listes de favoris** : de 1 à **10 listes** (4 au départ : Liste 1 à Liste 4) pour trier les WebSDR.
  - Le menu juste à droite de la liste des WebSDR choisit la liste affichée.
  - **Clic droit** sur ce menu pour renommer la liste (par ex. « HF France », « VHF »…).
  - À chaque ajout (★, gestionnaire, recherche), SDRLink **demande la liste** où enregistrer (la liste affichée est proposée). Les favoris importés vont dans leur liste (voir Importer). Les favoris existants sont dans la liste 1.
  - Dans « Gérer les favoris… » :
    - le menu **Liste** filtre l'affichage (ou « Toutes les listes ») ; clic droit dessus pour la renommer ;
    - **Nouvelle liste** ajoute une liste à la fin (10 au maximum) et demande son nom ;
    - **Supprimer la liste…** supprime la liste affichée **et ses favoris**, après confirmation (il reste toujours au moins une liste). Les listes suivantes remontent d'un rang ; une liste jamais renommée prend le nom de son nouveau rang (« Liste 5 » devient « Liste 4 ») ;
    - **Déplacer vers ▸** range les favoris cochés (ou, à défaut, le favori sélectionné) dans une autre liste ;
    - une colonne « Liste » indique la liste de chaque favori.
- **★ Ajouter aux favoris** : ajoute la page affichée, nommée d'après le titre de la page (modifiable), à la liste choisie dans la fenêtre. Si elle est déjà dans cette liste, elle n'est pas ajoutée une seconde fois (un message l'indique). La même page peut figurer dans plusieurs listes.
- **Retirer des favoris** : retire la page affichée de la **liste affichée seulement**, après confirmation. Elle reste dans les autres listes.
- **Gérer les favoris…** ouvre le gestionnaire :
  - **Ajouter un favori…** ;
  - une **case à cocher** devant chaque favori. **Supprimer…** retire tous les favoris cochés en une fois, ou le favori sélectionné si aucun n'est coché.
  - **Renommer…**, **Monter**, **Descendre** pour le favori sélectionné ;
  - **Vérifier les favoris** :
    - interroge chaque adresse (10 à la fois, 8 s maximum chacune) et affiche l'état dans la colonne État ;
    - les **doublons** (même adresse dans la même liste) sont **supprimés d'office** : seul le premier est gardé ;
    - les **liens morts** (pas de réponse, nom inconnu, connexion refusée, page introuvable 404) sont **cochés automatiquement** et affichés en rouge ;
    - il ne reste qu'à cliquer sur « Supprimer… ». Un serveur qui répond, même par un refus d'accès, est considéré comme vivant.
  - **Tout cocher** / **Tout décocher** (favoris affichés) ;
  - champs **Nom** et **Adresse** : ils affichent le favori sélectionné et se corrigent directement. La liste se met à jour en tapant. Un champ laissé vide n'est pas appliqué.
  - **Écouter la sélection** : ouvre le favori sélectionné dans SDRLink pour l'écouter, sans fermer le gestionnaire (comme dans la recherche des WebSDR).
  - **Ouvrir** ou double-clic : ouvre le WebSDR et ferme le gestionnaire. La touche Suppr supprime, Entrée ouvre.
  - **Exporter…** : **tous** les favoris, de toutes les listes :
    - **HTML** (choix proposé d'abord) : fichier de marque-pages au format de **Firefox**, importable aussi dans Edge et Chrome. Il contient un dossier **SDRLink** avec un sous-dossier par liste, au nom de la liste (toutes les listes, même vides). Le volume de chaque WebSDR y est gardé pour SDRLink ; les navigateurs l'ignorent.
    - **CSV** (lisible par Excel) : `nom;adresse;volume;liste;numéro de liste`.
    - Dans Firefox : Marque-pages > Gérer les marque-pages (Ctrl+Maj+O) > Importation et sauvegarde > **Importer des marque-pages au format HTML**. Le dossier SDRLink arrive dans le menu des marque-pages.
  - **Importer…** : chaque favori va **dans sa liste** :
    - fichier exporté par SDRLink, ou exporté par Firefox (Importation et sauvegarde > **Exporter des marque-pages au format HTML**) : seul le contenu du dossier **SDRLink** est importé, où qu'il se trouve dans les marque-pages (barre personnelle, menu…). Chaque sous-dossier va dans la liste de même nom, sinon dans la liste de même rang (1er sous-dossier → liste 1…), créée si besoin (jusqu'à 10 listes). Une liste jamais renommée prend le nom de son sous-dossier.
    - marque-pages d'un navigateur sans dossier SDRLink : un dossier qui porte le nom d'une liste va dans cette liste ; le reste va dans la liste affichée du gestionnaire.
    - fichier CSV `nom;adresse;volume;liste;numéro`, ou `nom;adresse`, ou simple liste d'adresses : la liste de même nom, sinon celle du numéro (créée si besoin) ; un nom de liste inconnu sans numéro crée une nouvelle liste s'il reste de la place ; sans colonne de liste, liste affichée.
    - un WebSDR déjà présent dans sa liste n'est pas ajouté une seconde fois. Le message final donne le nombre importé par liste.
    - le volume est repris d'un export SDRLink (HTML ou CSV) ; Firefox ne le garde pas.
- Les favoris de départ comprennent les annuaires websdr.org, KiwiSDR et OpenWebRX, ainsi que Twente, Wessex et French Alps.

## Rechercher des WebSDR

Le bouton **Rechercher des WebSDR…** (barre WebSDR) consulte trois annuaires publics :
- **websdr.org** ;
- la liste officielle des **KiwiSDR** ;
- **ReceiverBook** (OpenWebRX, WebSDR, KiwiSDR).

Les doublons sont retirés. La liste s'affiche **triée par distance depuis le centre de la France**.
- **Distance max** (1000 km par défaut) : France, Belgique, Luxembourg, Suisse, nord de l'Italie et de l'Espagne, ouest de l'Allemagne, Pays-Bas, sud de l'Angleterre… À augmenter ou réduire à volonté.
- **Filtre** : texte libre (ville, indicatif, département…). **Type** : websdr.org, KiwiSDR, OpenWebRX…
- Les récepteurs **déjà en favoris** (dans n'importe quelle liste) ne sont pas proposés ; le bas de la fenêtre indique combien sont ainsi masqués. Les relais KiwiSDR (xxx.proxy.kiwisdr.com) avec ou sans :8073 comptent comme un seul récepteur.
- Clic sur un titre de colonne : tri par cette colonne.
- **Pays** : estimé d'après le nom (pays cité, indicatif français F…/TK…) ou, pour la France, d'après la position GPS. Vide si inconnu.
- **Écouter la sélection** (ou double-clic) : ouvre le récepteur dans SDRLink pour l'essayer.
- Cocher ceux qui intéressent, puis **Ajouter les cochés aux favoris** : SDRLink demande dans quelle liste les enregistrer. Ils se renomment ensuite dans « Gérer les favoris… ».

**Premier démarrage** : la recherche s'ouvre d'elle-même, une seule fois, si Internet répond, pour remplir les favoris. Sans connexion, elle est retentée au démarrage suivant.

Il faut un accès Internet. Si un annuaire ne répond pas, les autres sont quand même affichés : le bas de la fenêtre indique le résultat de chaque source.

## Volume du WebSDR

La ligne de son, toujours en début de ligne sous la barre WebSDR, se lit de gauche à droite : le bouton ⇄, Mute WebSDR, Mute TX, Mute ExpertSDR, puis Volume WebSDR, Volume TX et Volume ExpertSDR, chacun avec sa réglette et sa valeur en %. Mute TX et Volume TX n'apparaissent que si le volume du poste est télécommandé, Mute ExpertSDR et Volume ExpertSDR que si ExpertSDR est lancé et répond (son Windows ou TCI), et ⇄ que s'il y a au moins deux sons à permuter. Les commandes absentes ne laissent pas de vide : la ligne se resserre à gauche. Le curseur **Volume WebSDR** règle uniquement le son du WebSDR, pas celui de Windows ni des autres logiciels.
- Il agit sur tous les types de WebSDR. Sur UberSDR, le son ne démarre qu'après un clic sur « Click to start » dans la page.
- Le volume est **mémorisé pour chaque favori** : en rouvrant un favori, on retrouve son volume.
- Les WebSDR qui ne sont pas en favoris partagent un même volume, lui aussi mémorisé.
- Un favori ajouté garde le volume du moment. L'export (HTML ou CSV) inclut le volume, et l'import le reprend.

## Coupure du son et volume du poste

À droite du volume du WebSDR :
- **⇄** (petit bouton carré) : passe le son d'une réception à l'autre parmi **Mute WebSDR**, **Mute TX** et **Mute ExpertSDR** (seulement celles qui sont disponibles : Mute TX si le volume du poste est télécommandé, Mute ExpertSDR si ExpertSDR répond) :
  - aucun son coupé : un clic les coupe tous ;
  - un seul son actif : il est coupé et le suivant est rétabli ; chaque clic passe au suivant (WebSDR → TX → ExpertSDR → WebSDR…) ;
  - tout coupé : le premier est rétabli ; plusieurs sons actifs (sans l'être tous) : tous coupés.
  - **Clic droit** sur ⇄ : choisir la coupure dont ⇄ ne tient pas compte (ou « Aucune »). Elle est coupée tout de suite et le reste, pour n'entendre qu'une réception ; ⇄ ne permute alors qu'entre les deux autres. Le choix est conservé.
- Couleurs des boutons Mute : **vert, écriture blanche** quand le son passe (comme le bouton de mode choisi), **rouge, écriture blanche** quand il est coupé, gris quand la coupure n'est pas disponible.
- **Mute WebSDR** : coupe le son du WebSDR (bouton rouge), un second clic le rétablit au volume réglé.
- **Mute TX** : coupe le son du **transceiver** en mettant son gain BF à zéro par CAT, puis rétablit le volume au second clic. Le son est aussi rétabli à la déconnexion.
- **Mute ExpertSDR** : n'apparaît que lorsque le logiciel **ExpertSDR** (2 ou 3) tourne et répond. Il coupe et rétablit le son d'ExpertSDR sans aller dans ExpertSDR :
  - par le **mélangeur de volume de Windows** si ExpertSDR joue par le son de Windows ;
  - sinon (son sorti par le poste lui-même, MB1, ou par ASIO) par la commande **TCI** `mute` : il faut alors activer le serveur TCI dans ExpertSDR (Options > TCI). SDRLink garde une liaison TCI avec ExpertSDR tant qu'il tourne, même si le poste est relié autrement (HRD…) : une coupure ou un volume réglé dans ExpertSDR s'affiche aussi dans SDRLink. Un message l'explique si le serveur TCI ne répond pas.
  - **Clic droit** sur Mute ExpertSDR, ou sur « Volume WebSDR » : port du serveur TCI d'ExpertSDR (**40001** par défaut), à régler comme dans ExpertSDR. Tant qu'ExpertSDR ne répond pas, Mute et Volume ExpertSDR sont masqués et la barre d'état indique de passer par « Volume WebSDR ».
  - Si SDRLink a coupé ExpertSDR, le son est rétabli à la fermeture de SDRLink.
- **Volume TX** : règle le volume du transceiver (gain BF, en %). La barre suit aussi le bouton du poste : elle est relue toutes les 2 secondes environ.
- **Volume TX** et **Mute TX** ne s'affichent que si le volume du poste est télécommandé (poste connecté, et interface qui le permet).
- **Volume ExpertSDR** (à droite de Volume TX) : volume d'ExpertSDR, par le mélangeur de Windows ou par TCI (`volume:` de −60 à 0 dB) comme Mute ExpertSDR. Affiché quand ExpertSDR tourne ; grisé tant qu'aucune des deux voies ne répond.

Commandes utilisées (vérifiées dans le manuel CAT Yaesu et dans Hamlib) :
- **Yaesu** : `AG0nnn;`, de 000 à 255.
- **Kenwood** : `AGnnn;` ou `AG0nnn;` selon le modèle, détecté automatiquement. De 000 à 255.
- **Elecraft** : `AGnnn;`, de 000 à 250 (K3, KX3, KX2) ou de 000 à 060 (K4).
- **Icom / Xiegu** : CI-V `14 01`, valeur de 0000 à 0255.
- **TCI** : `volume:` de −60 à 0 dB.
- **HRD** : curseur « AF gain (main) » de HRD Rig Control (sinon « AF gain » ou « Volume »), par `get/set slider-pos`. Il est recherché à la connexion, et de nouveau quand on change de poste dans HRD (FT-2000, FTdx10, FTdx5000…) sans reconnecter SDRLink ; le journal l'indique par la ligne « HRD curseur volume … ». Si HRD n'a pas de curseur de volume pour le poste, Volume TX et Mute TX sont masqués et le journal liste les curseurs disponibles.
- **Non disponible** avec FT-817/818/857/897, FT-990 et FT-1000/1000D/1000MP (pas de commande) et OmniRig (pas de réglage de volume) : Volume TX et Mute TX sont alors masqués.

## Mode sombre

La case **Mode sombre**, à droite de « Ouvrir le journal », passe toute l'interface en couleurs sombres : fenêtre, barre de titre (Windows 10 20H1 et plus, Windows 11), listes, boutons et gestionnaire de favoris. La page du WebSDR garde ses propres couleurs.
- Les fréquences, le PTT et les boutons de mode prennent des teintes plus claires, lisibles dans la pénombre.
- Les réglages grisés pendant la connexion restent lisibles.
- **Palette** : le petit carré multicolore à droite de « Mode sombre » ouvre un choix de couleurs de fond.
  - En mode sombre : uniquement des teintes foncées (noir, graphite, bleus nuit, violet, bordeaux, rouge très foncé, brun, vert et bleu-vert foncés).
  - En mode clair : uniquement des teintes claires (blanc, gris perle, crème, sable, jaune pâle, bleus, vert d'eau, rose, lavande).
  - Le reste de l'interface (champs, boutons, bordures) s'accorde automatiquement. « Par défaut » revient aux couleurs d'origine.
  - Le choix est mémorisé séparément pour le mode sombre et le mode clair.

## Couleurs des boutons (clic droit)

Un clic droit sur la plupart des boutons propose leur **couleur de fond**. Si le bouton avait déjà un clic droit (PTT, Mémoires, Câble SDRLINK, Mute ExpertSDR…), ce menu est conservé et les couleurs sont ajoutées à la fin.
- **Un seul choix** (« Couleur du bouton ») : Ouvrir le journal, Taille du texte, Conf scan, QSY, Save, Mémoires, Aller, Retour, Recharger, Ajouter aux favoris, Retirer des favoris, Gérer les favoris, Rechercher des WebSDR, Import/Export.
- **Deux choix, un pour chaque état** :
  - **Connecter** : quand connecté / quand déconnecté ;
  - **Câble SDRLINK** : quand activé / quand désactivé ;
  - **Scan** : scan arrêté / scan en marche ;
  - **PTT** : en réception / en émission (PTT activé) ;
  - **RADIO** et **WEBSDR** (fréquence et mode) : couleur du fond / couleur du texte (en émission, RADIO passe toujours en rouge) ;
  - **USB, LSB, AM, FM, DIGI, CW** : mode sélectionné / modes non sélectionnés (le choix vaut pour les six boutons) ;
  - **Mute WebSDR**, **Mute TX**, **Mute ExpertSDR** (chacun le sien) : son coupé / son actif. Un Mute indisponible reste grisé.
- Couleurs proposées : **Par défaut** (couleur d'origine, qui suit le mode sombre et la palette), bleu marine, bleu, bleu ciel, bleu canard, vert, rouge, bordeaux, violet, rose, marron, gris, noir, jaune, orange, blanc, ou **Autre couleur…** au choix.
- Le texte passe en noir sur les couleurs claires et en blanc sur les foncées ; une bordure fait contour.
- Les choix sont conservés, y compris en passant du mode clair au mode sombre.
- Le bouton REC garde son propre menu (couleur et largeur, voir « Enregistrement (REC) »).

## Langue

Le menu **Langue**, sur la 2ᵉ ligne, change la langue de toute l'interface, immédiatement et sans redémarrer. Le choix est conservé.
- Langues : français, anglais, allemand, espagnol, italien, néerlandais, portugais, polonais, tchèque, slovaque, slovène, croate, hongrois, roumain, bulgare, grec, suédois, danois, norvégien, finnois, estonien, letton et lituanien.
- Les langues autres que le français et l'anglais sont traduites à partir de l'anglais. Un texte manquant s'affiche en anglais.
- Le journal de diagnostic reste en français.
- Pour corriger une traduction : fichier `lang\<code>.txt` (texte anglais, tabulation, traduction), puis `tools\langgen.ps1` et recompiler.

## Synchronisation

- **Bidirectionnelle** / **Radio → WebSDR** / **WebSDR → Radio** / **Désactivée**
- En choisissant **Bidirectionnelle** ou **Radio → WebSDR**, le WebSDR est aussitôt recalé sur la fréquence du poste.
- En choisissant **WebSDR → Radio**, c'est le poste qui se cale aussitôt sur la fréquence et le mode du WebSDR.
- **Au démarrage de SDRLink, le poste ne change jamais** de fréquence ni de mode : en Bidirectionnelle ou Radio → WebSDR, c'est le WebSDR qui se cale sur le poste ; en WebSDR → Radio ou Désactivée, rien ne change. De même, pendant les 8 secondes qui suivent le chargement d'une page ou la connexion du poste, le WebSDR ne commande pas le poste : une page encore en train de s'installer annonce parfois son ancienne fréquence, qui était auparavant renvoyée au poste.
- En **Bidirectionnelle**, le **poste fait foi** : si le WebSDR reste décalé de plus de 3 secondes, il est recalé sur le poste. C'est le cas si un réglage est perdu ou si la page est lente. En « Radio → WebSDR », on peut au contraire écarter le WebSDR du poste (molette ou clic dans la cascade) : il n'est recalé qu'au prochain mouvement du VFO.
- **OpenWebRX à plusieurs profils** (un profil par bande, par ex. « HF_7MHz », « VHF_144-146MHz ») : si la fréquence demandée est hors du profil actif, SDRLink bascule automatiquement sur le profil qui la couvre, d'après son nom, puis règle la fréquence. Le changement de profil s'applique à tous les auditeurs de ce récepteur, comme lorsqu'on le fait à la main.
- **Décalage WebSDR (Hz)** : ajouté à la fréquence du poste côté WebSDR (transverter, écart de calibration…)
- **◀ Retour** (barre WebSDR) : revient à la page précédente, comme la flèche retour d'un navigateur. Utile après avoir suivi un lien, par exemple dans un annuaire de WebSDR.
- Si le poste est en DATA-U et le WebSDR en USB, SDRLink ne change pas le mode du poste (même famille).

## Bouton PTT

Le gros bouton **PTT**, à gauche de « RADIO », fait passer le poste en émission par CAT : un clic pour émettre (le bouton devient rouge « ● TX »), un second clic pour revenir en réception. C'est l'équivalent du bouton MOX de HRD.
- Il envoie `TX1;`/`TX0;` aux Yaesu, `TX;`/`RX;` aux Kenwood et Elecraft, `1C 00` aux Icom, `trx` en TCI.
- Avec OmniRig, il agit sur la propriété `Tx` ; avec HRD, sur le bouton MOX.
- Le son du WebSDR est coupé dès le clic, même si le poste ne signale pas son état d'émission (si « Couper le son du WebSDR en émission » est cochée).
- Par sécurité, si on se déconnecte ou si on ferme SDRLink pendant une émission lancée par ce bouton, le poste repasse en réception.
- **Délai de sécurité TX** (clic droit sur PTT) : Désactivé, 30 s, 1, 2, 3, 5, 10, 15, 20 ou 25 min.
  - Pendant l'émission, le bouton affiche le temps restant (« ● TX 1:45 »).
  - À l'échéance, SDRLink repasse en réception et l'indique en bas de la fenêtre.
  - Ce délai concerne l'émission lancée par le bouton PTT de SDRLink, pas la pédale ni le micro.
- **Émission sans modulation** (constaté sur un FTdx10) : le PTT par CAT met bien le poste en émission, mais le son émis dépend de la **source de modulation** du poste. Si elle est réglée sur l'arrière ou l'USB (« REAR », souvent le cas pour le FT8), le micro n'est pas transmis. Sur le FTdx10, vérifier dans les menus de réglage de l'émission (TX AUDIO) la source de modulation SSB (MOD SOURCE : MIC) et le gain micro. Les autres logiciels qui passent en émission par CAT, comme WSJT-X, rencontrent le même comportement.

## Coupure du son en émission

Coche **« Couper le son du WebSDR en émission »** (activée par défaut) : pendant que le poste émet, le son du WebSDR est coupé pour ne pas s'entendre. L'indication **● TX** s'affiche alors en rouge à côté de « RADIO ». Décocher pour garder le son en émission.

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

Les boutons **USB, LSB, AM, FM, DIGI, CW**, à droite de la fréquence WEBSDR, changent le mode de la radio **et** du WebSDR en même temps. Le bouton du mode en cours est en vert.
- **DIGI** : mode données de la radio (PKT, DATA, USB-D… selon la marque), USB ou LSB côté WebSDR. Un nouveau clic sur DIGI déjà allumé bascule entre **DIGI U** (DATA-USB) et **DIGI L** (DATA-LSB).
- **CW** : un nouveau clic sur CW déjà allumé bascule entre **CW U** (CW normale) et **CW L** (CW-R). Les WebSDR n'ont qu'un mode CW. En TCI, il n'y a qu'un mode CW.
- Icom en CAT direct : le mode DATA ne se commande pas par cette interface, DIGI met la radio en USB ou LSB.

## Câble SDRLINK (son du WebSDR vers WSJT-X…)

Le son du WebSDR est recopié **en permanence, dans tous les modes**, vers un câble audio virtuel. L'autre bout du câble, nommé **SDRLINK**, apparaît dans la liste des **entrées audio** de WSJT-X, JTDX, fldigi…
- La recopie n'est pas touchée par **Mute WebSDR**, le volume à 0, la coupure en émission ni le notch : on peut décoder sans entendre le WebSDR.
- **Il faut un pilote de câble virtuel.** SDRLink ne peut pas créer lui-même un câble : Windows exige pour cela un pilote signé. Installer **VB-CABLE** de VB-Audio, gratuit : bouton **Câble SDRLINK ▾** › « Télécharger VB-CABLE ».
  - Si VB-CABLE sert déjà à autre chose, VB-Audio propose aussi des câbles supplémentaires (« VB-CABLE A+B », « C+D »).
- **Bouton Câble SDRLINK ▾** (2ᵉ ligne) :
  - choix de la sortie qui reçoit le son (le bout « lecture » du câble, par ex. « CABLE Input »), ou **Désactivé** ;
  - **Nommer « SDRLINK » l'autre bout du câble** : renomme le bout « enregistrement » (« CABLE Output ») en `SDRLINK`, comme le panneau Son de Windows. Windows demande l'autorisation d'administrateur. Redémarrer ensuite WSJT-X et choisir l'entrée **SDRLINK**.
- Choix automatique : le câble dont l'autre bout s'appelle SDRLINK, sinon un câble VB-Audio qui a gardé son nom d'origine (CABLE Input, CABLE-A Input…). Un câble déjà renommé, les haut-parleurs ou une carte son de radio ne sont **jamais** pris automatiquement. Les choisir à la main demande une confirmation.
- Ne jamais choisir la carte son d'**émission** d'une radio (par ex. « Emission FT2000 ») : le son du WebSDR serait émis.
- Si la barre d'état indique « sortie déjà utilisée en mode exclusif », un autre logiciel s'est réservé le câble. Dans le panneau Son, ouvrir les propriétés du câble, onglet Avancé, et décocher « Autoriser les applications à prendre le contrôle exclusif ». Sinon, fermer ce logiciel.

## Notch automatique (DNF)

Case **Notch auto (DNF)**, au bout de la ligne Synchro : comme le DNF du FT-2000, elle supprime automatiquement les **porteuses et sifflements stables** du son du WebSDR.
- Jusqu'à 4 raies à la fois. Chaque raie est repérée en environ 0,6 s, puis suivie si elle glisse. L'atténuation mesurée est d'environ 48 dB, jusqu'au niveau du bruit.
- La parole bouge trop pour être retenue. Une **CW** stable est en revanche supprimée, comme avec le DNF du poste : décocher en CW.
- Seul le son entendu est filtré, le câble SDRLINK reçoit le son brut (les décodeurs en ont besoin).
- Le choix est mémorisé.

Les boutons gardent toujours la même place : les zones RADIO et WEBSDR ont une largeur fixe. Le mode du WebSDR y est affiché en forme courte (USB, LSB, CW, AM, FM). L'émission est signalée par la fréquence RADIO en rouge et par le bouton PTT. La FM devient NBFM sur KiwiSDR et NFM sur UberSDR et OpenWebRX.

## Saisie d'une fréquence

- Taper une fréquence en **kHz** dans le champ « kHz » (ex. `7074`, `14074,5` ou `14074.5`), puis **Entrée** ou **QSY** : le poste et le WebSDR s'y accordent.
- Le menu déroulant garde les **100 dernières** fréquences, la plus récente en haut. Choisir une valeur dans la liste y accorde directement.
- **Save** ajoute la fréquence en cours à la liste.
- **Clic droit** sur le champ : « Supprimer … de la liste » (la valeur affichée) ou « Tout supprimer » (une confirmation est demandée).
- La liste est conservée au prochain lancement.

## Molette sur les fréquences

Laisser la souris sur **RADIO** ou **WEBSDR** et tourner la molette. Ce qui bouge dépend de la synchronisation :

| Synchro | Molette sur RADIO | Molette sur WEBSDR |
|---|---|---|
| Bidirectionnelle | radio et WebSDR | radio et WebSDR |
| Radio → WebSDR | radio et WebSDR | WebSDR seul |
| WebSDR → Radio | radio seule | radio et WebSDR |
| Désactivée | radio seule | WebSDR seul |

- Le **pas** se choisit dans le menu « Pas » de la même ligne : 100 Hz, 250 Hz, 500 Hz, 1 kHz, 2,5 kHz, 5 kHz ou 10 kHz.
- La fréquence est arrondie au pas : au pas de 1 kHz, on tombe sur des kHz ronds.
- **Accélération** si le pas est inférieur à 1 kHz, selon le temps entre deux crans :
  - moins de 100 ms : pas × 2 ;
  - moins de 60 ms : pas × 5 ;
  - moins de 30 ms : pas × 10.

## Affichage

- **Affichage réduit** : la fenêtre commence à la ligne « Synchro » et garde **tout le bas**, page du WebSDR comprise. Les lignes de connexion et de réglage de l'interface sont masquées ; la fenêtre perd leur hauteur, la page garde sa taille. La fenêtre peut être redimensionnée.
- **Affichage mini** (l'ancien « Affichage réduit ») : la fenêtre ne garde que les lignes « Synchro » à « Volume WebSDR » (PTT, fréquences, modes, barre WebSDR, volumes), à hauteur fixe. La page du WebSDR est masquée, mais le WebSDR continue de jouer et de suivre le poste.
- En affichages réduit et mini, la ligne PTT / fréquences / modes / cases reste **sur une seule ligne** : la fenêtre ne peut pas devenir plus étroite qu'elle (sauf écran trop petit), seule la hauteur se règle. La fenêtre est aussi gardée entière dans l'écran (bas visible, au-dessus de la barre des tâches) ; en plein écran, l'affichage réduit reste en plein écran (toute la page du WebSDR est visible) et le retour à l'affichage normal aussi ; le mini, lui, est toujours une petite fenêtre.
- Une seule des deux cases à la fois. **Décocher** la case cochée revient toujours à l'affichage normal (les deux cases décochées), même si l'on était passé du réduit au mini ou du mini au réduit.
- Chaque affichage retrouve la taille qu'il avait quand on l'a quitté (normal et réduit), y compris d'une fois sur l'autre : les tailles sont mémorisées à la fermeture, avec l'affichage en cours. Si la fenêtre était en plein écran en affichage normal, elle y revient.
- **Affichage ptt** (sous « Avant plan ») : toute petite fenêtre qui ne montre que le **bouton PTT** et la case « Affichage ptt ». Le PTT y fonctionne normalement (clic, clic droit pour le délai de sécurité). **Décocher** la case revient à l'affichage d'avant **strictement identique** : même affichage (normal, réduit ou mini), même taille, même position, plein écran compris. Fermer SDRLink en affichage ptt enregistre l'affichage d'avant.
- **Avant plan** : SDRLink reste au-dessus des autres fenêtres (pratique avec l'affichage ptt).
- **Taille du texte…** (1re ligne) : agrandit ou réduit le texte de la fenêtre principale, de 70 à 200 % (100 % = taille d'origine), avec le curseur ou les boutons − / +. Le changement se voit tout de suite ; **Annuler** revient à la taille d'avant, **100 %** à la taille d'origine. Les boutons et curseurs de taille fixe suivent. La taille est conservée au prochain lancement.

Ces deux réglages sont conservés au prochain lancement.

## Scan

Sur la ligne « Synchro », après « Notch auto (DNF) » :
- **Conf scan** : choix du type de scan.
  - **Fréquences** : fréquence de **début** et de **fin** en kHz (dans n'importe quel ordre), **pas** (100 Hz à 100 kHz), **durée** sur chaque fréquence : 1 s, 2 s, 5 s, 10 s, 30 s, 1 min, 2 min, 5 min ou 10 min. Arrivé à la fin, le scan repart du début.
  - **Mémoires** : liste des mémoires avec une **case à cocher** pour chacune (Tout cocher / Tout décocher), et la **durée** sur chaque mémoire (mêmes choix). Le mode mémorisé est appliqué.
  - Chaque type garde ses réglages : en repassant de Mémoires à Fréquences (ou l'inverse), les choix précédents sont toujours là. **Réinitialiser** remet à zéro le type affiché (l'autre garde les siens).
  - **OK** enregistre ; **Lancer le scan** enregistre et démarre.
- **▶ Scan** : démarre le scan configuré ; le bouton devient rouge « ■ Arrêter le scan ». La barre d'état indique la fréquence ou la mémoire en cours (sans remplir l'historique des messages).
- Le scan règle le poste et le WebSDR, comme une saisie de fréquence. Il s'arrête dès que le poste passe en **émission** (PTT de SDRLink ou du poste) ; il ne peut pas démarrer en émission.

## Enregistrement (REC)

- Bouton **REC**, sur la ligne de son, juste après le dernier volume affiché (il se rapproche quand Volume TX ou Volume ExpertSDR sont absents) : **bleu marine** au repos (couleur réglable), **orange** pendant l'enregistrement.
- Un clic lance l'enregistrement de **tout ce qu'on entend dans les haut-parleurs** (sortie audio par défaut de Windows) : WebSDR, ExpertSDR, poste relié au PC, sons de Windows… Le volume de Windows et les Mute ne changent rien à ce qui est capté avant eux : on enregistre ce que la sortie joue.
- Un nouveau clic arrête et ouvre la fenêtre **Enregistrer le son** :
  - **Nom** : proposé d'après la date, l'heure, la fréquence et le mode au départ (modifiable) ;
  - **Dossier** : par défaut Musique\SDRLink, **Parcourir…** pour en choisir un autre (le dernier dossier choisi est retenu) ;
  - **Format** : **MP3** (par défaut, 128 kbit/s, codeur de Windows), **WAV** (sans perte, volumineux) ou **FLAC** (sans perte, environ deux fois plus petit) ;
  - **Supprimer** abandonne l'enregistrement (après confirmation).
- Le fichier est écrit en arrière-plan ; la barre d'état indique la progression puis « Enregistrement sauvegardé ». En cas d'échec, la fenêtre revient : le son n'est pas perdu.
- **Clic droit sur REC** : choix de la **carte son du poste** (son entrée dans Windows, par exemple « USB AUDIO CODEC » ou « USB Sound Device »), ou « Aucune (toujours les haut-parleurs) ». Cette carte est enregistrée **à la place des haut-parleurs** seulement si l'on écoute le poste seul au moment d'appuyer sur REC : **Mute WebSDR** actif (rouge) et **Mute ExpertSDR** actif (rouge) si ExpertSDR est lancé. Sinon, ce sont les haut-parleurs. Si la carte est débranchée, les haut-parleurs sont enregistrés et la barre d'état le signale. Le choix est conservé.
- **Clic droit sur REC > Couleur du bouton REC** : couleur du bouton au repos (bleu marine par défaut, bleu, bleu ciel, bleu canard, vert, rouge, bordeaux, violet, rose, marron, gris, noir, jaune, ou « Autre couleur… » au choix). Le texte passe en noir sur les couleurs claires ; pendant l'enregistrement, le bouton reste orange. Le choix est conservé.
- **Clic droit sur REC > Largeur du bouton REC** : **Étroit** (rectangle deux fois plus large que haut, par défaut) ou **Large** (quatre fois plus large que haut), toujours à la hauteur des boutons Mute, « REC » centré. Le choix est conservé.
- Les silences sont gardés (la durée du fichier est la durée réelle). Fermer SDRLink pendant un enregistrement propose de le sauvegarder avant de quitter.
## Mémoires de fréquences

- Bouton **Mémoires** (ligne des fréquences, après Save) : ouvre la fenêtre des mémoires.
  - **Mémoriser la fréquence actuelle…** : même fenêtre que l'ajout d'un favori, avec **Nom** (pseudo, facultatif), **Fréquence** en kHz, **Mode** (ou « mode inchangé ») et **Liste**.
  - **Cliquer sur une mémoire** change tout de suite la fréquence (poste et WebSDR) et le mode s'il est mémorisé ; la fenêtre reste ouverte pour passer à une autre. Entrée fait de même.
  - **Modifier…**, **Supprimer…** (ou touche Suppr).
  - Listes de mémoires comme pour les favoris : menu **Liste** (ou « Toutes les listes »), clic droit pour renommer, **Nouvelle liste** (10 au maximum), **Supprimer la liste…** (avec ses mémoires, après confirmation).
- **Clic droit** sur Mémoires : mémoriser la fréquence actuelle sans ouvrir la fenêtre.
- Bouton **▾** à droite : les **20 dernières mémoires utilisées**, la plus récente en haut. Une mémoire qui a un nom (pseudo) est affichée par son nom, sinon par sa fréquence. Un clic y va. En bas : Mémoires… et Effacer cette liste.
- Les mémoires sont gardées dans les réglages (`settings.ini`, lignes `Mem=`).

## Barre d'état (messages en bas de la fenêtre)

- **Clic droit** sur la barre d'état : les **20 derniers messages**, le plus récent en haut, avec leur heure.
- Cliquer sur un message **masque ce type de message** (coché ✓) : il ne s'affiche plus dans la barre, mais reste dans la liste des derniers messages. Les messages qui ne diffèrent que par un nom ou un nombre (« « A » ajouté à « HF » », « « B » ajouté à « VHF » ») sont du même type. Cliquer de nouveau le réaffiche.
- **Réafficher tous les messages masqués** : annule tous les masquages. Les types masqués sont conservés au prochain lancement.
- Le message « Câble SDRLINK : aucun câble audio virtuel trouvé » disparaît dès que le câble est mis sur **Désactivé**.

## Import / Export des réglages

Bouton **Import/Export ▾** (1re ligne, après « Vérifier les mises à jour ») : pour retrouver exactement la même configuration sur un autre ordinateur, ou garder une sauvegarde. Clic droit : couleur du bouton.
- Tous les réglages de SDRLink sont dans un seul fichier : **`%APPDATA%\SDRLink\settings.ini`** (par exemple `C:\Users\<nom>\AppData\Roaming\SDRLink\settings.ini`). Le journal `sdrlink.log` est à côté.
- **Exporter tous les réglages…** : enregistre ce fichier où vous voulez (par défaut dans Documents, nom `SDRLink-reglages-<date>.ini`), avec l'état du moment : interface et poste (port COM, vitesse, TCI, HRD, Hamlib…), synchronisation, décalage, favoris et leurs listes, mémoires, historique des fréquences, langue, mode sombre et palette, couleurs des boutons, taille du texte, REC (dossier, carte son, couleur, largeur), câble SDRLINK, scan, volumes, taille et position de la fenêtre, etc.
- **Importer des réglages…** : choisir un fichier exporté. Après confirmation, tous les réglages actuels sont remplacés et SDRLink redémarre avec la configuration du fichier. Les anciens réglages sont gardés dans `settings.avant-import.ini` (même dossier) : pour revenir en arrière, réimporter ce fichier.
- Un fichier qui ne contient pas de réglages SDRLink est refusé (message dans la barre d'état).
- Sur un autre ordinateur, vérifier ce qui dépend de la machine : port COM du poste, cartes son (câble SDRLINK, carte son du poste pour REC), chemin de rigctld, dossier des enregistrements. Une carte son du poste absente est signalée « non branchée » dans le menu de REC ; si la position de la fenêtre tombe hors des écrans, elle est ignorée.
- Les données de navigation des pages WebSDR (cookies, cache) ne font pas partie des réglages et ne sont pas exportées.

## Mises à jour

Case **Vérifier les mises à jour** (1re ligne, cochée par défaut) : au démarrage, si Internet répond, SDRLink regarde la dernière version publiée sur GitHub. S'il en existe une plus récente, un lien orange « ⚠ Mise à jour Vx.y disponible » apparaît en bas à droite de la fenêtre, dans la barre d'état (visible aussi en affichages réduit et mini) : un clic ouvre la page de téléchargement. Cocher la case lance la vérification tout de suite.

## Dépannage

**OmniRig ne fonctionne pas** (pas de fréquence, erreur à la connexion) : utiliser **OmniRig version 1** (pas la 2), lancé **en mode administrateur** (clic droit sur OmniRig > Exécuter en tant qu'administrateur).

Réglages : `%APPDATA%\SDRLink\settings.ini`. Journal : `%APPDATA%\SDRLink\sdrlink.log`. Le journal est remis à zéro à chaque lancement et contient l'état de la connexion et, pour HRD, la liste des modes reçue.

Message « Unhandled exception… 0x8007139F » en cliquant sur un fichier `.html` dans la fenêtre « Importer des favoris » : il vient de l'aperçu **Monaco** de **PowerToys**, pas de SDRLink (cliquer sur Continue, l'import fonctionne). Pour l'éviter : **Alt+P** dans la fenêtre d'ouverture (masque le volet d'aperçu), ou dans PowerToys > Extensions de l'Explorateur de fichiers, désactiver l'aperçu du code source (Monaco).

## Historique des versions

- **1.13**
  - **couleur de fond au choix par clic droit** sur presque tous les boutons (Connecter, Ouvrir le journal, Taille du texte, Câble SDRLINK, Conf scan, Scan, PTT, RADIO, WEBSDR, boutons de mode, QSY, Save, Mémoires, Aller, Retour, Recharger, favoris, Rechercher des WebSDR, les trois Mute), une couleur par état pour les boutons qui en ont deux ; les clics droits existants sont conservés ;
  - bouton **Import/Export** : exporte tous les réglages (interface, favoris, mémoires, couleurs…) dans un fichier, et les importe sur un autre ordinateur (SDRLink redémarre, anciens réglages gardés dans settings.avant-import.ini).
- **1.12**
  - bouton **REC** déplacé sur la ligne de son, juste après le dernier volume affiché (sans espace vide quand ExpertSDR ou le volume du poste sont absents), avec la même allure et la même hauteur que les boutons Mute ;
  - clic droit sur REC : **couleur du bouton** au repos (bleu marine par défaut) et **largeur** (étroit ou large) ;
  - icône **SDR / LINK** dans la barre des tâches, la barre de titre et l'Explorateur.
- **1.11**
  - bouton **REC** : enregistre le son entendu dans les haut-parleurs, puis le sauvegarde en MP3, WAV ou FLAC (nom, dossier et format au choix) ; clic droit : carte son du poste, enregistrée quand seul le poste est écouté (Mute WebSDR et Mute ExpertSDR actifs) ;
  - case **Affichage ptt** : toute petite fenêtre avec le seul bouton PTT, retour à l'affichage d'avant à l'identique ;
  - notice : si OmniRig ne fonctionne pas, utiliser OmniRig version 1 en mode administrateur.
- **1.10**
  - décocher « Affichage réduit » ou « Affichage mini » revient toujours à l'affichage normal ;
  - ligne de son réorganisée : ⇄, Mute WebSDR, Mute TX, Mute ExpertSDR, puis Volume WebSDR, Volume TX, Volume ExpertSDR (chaque libellé avant sa réglette ; l'ordre ne se mélange plus quand le poste ou ExpertSDR apparaissent) ; n'y figurent que les sons disponibles, sans place vide, et ⇄ seulement s'il y a au moins deux sons ;
  - nouveaux postes Yaesu : FT-990, FT-1000, FT-1000D, FT-1000MP, FT-1000MP Mark-V et Mark-V Field (CAT direct, Hamlib, et HRD / OmniRig par leur propre choix de poste).
- **1.9**
  - au démarrage, le poste ne change plus de fréquence ni de mode : seul le WebSDR se cale sur lui (si la synchro le prévoit) ;
  - **scan** de fréquences ou de mémoires : boutons « Conf scan » et « ▶ Scan » ;
  - « Affichage réduit » renommé **Affichage mini** ; nouvel **Affichage réduit** à partir de la ligne Synchro, page comprise ; chaque affichage retrouve sa taille.
  - correction : avec un texte agrandi (plus de 100 %) et la fenêtre en plein écran, la fenêtre se rouvrait coupée en hauteur.
- **1.8**
  - **mémoires de fréquences** : nom (pseudo), fréquence, mode, jusqu'à 10 listes ; accord immédiat d'un clic ; bouton ▾ des 20 dernières utilisées ;
  - fenêtre **Taille du texte** (70 à 200 %) ;
  - boutons Mute verts quand le son passe, rouges quand il est coupé (écriture blanche) ; bouton ⇄ vraiment carré ;
  - jusqu'à **10 listes de favoris**, boutons « Nouvelle liste » et « Supprimer la liste… » ; export et import (HTML Firefox, CSV) avec toutes les listes, listes créées à l'import si besoin ;
  - clic droit sur la barre d'état : 20 derniers messages, types de messages masquables ;
  - le message « aucun câble audio virtuel trouvé » ne reste plus affiché quand le câble SDRLINK est désactivé.
- **1.7**
  - favoris : export et import compatibles avec **Firefox**, par listes (dossier SDRLink avec un sous-dossier par liste) ; l'import remet chaque favori dans sa liste au lieu de tout mettre dans une seule.
  - une erreur imprévue est notée en détail dans le journal (ligne « Erreur inattendue ») et signalée en bas de la fenêtre, sans boîte de dialogue.
- **1.6**
  - bouton ⇄ : le son passe d'une réception à la suivante (WebSDR, TX, ExpertSDR) ; clic droit pour laisser une coupure de côté ;
  - case « Vérifier les mises à jour » et lien vers la nouvelle version dans la barre d'état.
- **1.5**
  - premier démarrage : recherche des WebSDR lancée une fois, si Internet répond ;
  - la liste où enregistrer est demandée à chaque ajout de favori, et un favori déjà présent dans la liste n'est pas ajouté ;
  - « Vérifier les favoris » supprime les doublons ;
  - la recherche ne propose plus les WebSDR déjà en favoris ;
  - « Écouter la sélection » dans le gestionnaire de favoris ;
  - bouton ⇄ qui inverse Mute WebSDR et Mute TX ;
  - bouton Mute ExpertSDR et barre Volume ExpertSDR quand ExpertSDR tourne (mélangeur de Windows ou TCI), port TCI par clic droit ;
  - Volume TX et Mute TX masqués quand le volume du poste n'est pas télécommandé ;
  - HRD : volume du poste suivi quand on change de poste dans HRD ; Hamlib : le volume n'est plus grisé après une seule lecture manquée.
- **1.4.1** : Yaesu FT-857, FT-857D, FT-897 et FT-897D.
- **1.4**
  - interface **Hamlib (rigctld)**, avec lancement automatique de rigctld ;
  - 23 langues européennes ;
  - « Ajouter aux favoris » et « Retirer des favoris » ne concernent que la liste affichée.
- **1.3**
  - « POSTE » devient « RADIO » (bandeau et menu Synchro) ;
  - les listes de favoris vides ne sont plus proposées dans la barre WebSDR ;
  - câble SDRLINK actif dans tous les modes, bouton « Câble SDRLINK » et renommage de l'entrée en SDRLINK ;
  - bouton CW ; DIGI et CW basculent entre bande latérale haute et basse ;
  - notch automatique (DNF).

- **1.2**
  - mode DIGI avec recopie du son du WebSDR vers une carte son virtuelle (WSJT-X) ;
  - molette selon le sens de synchronisation ; en « WebSDR → Radio », le poste se cale sur le WebSDR ;
  - volume du poste via HRD (curseur AF gain) ;
  - affichage réduit de « Synchro » à « Volume » ;
  - délais TX de 15, 20 et 25 min ; « Tout cocher » dans les favoris ;
  - correction du mode DATA sur les Kenwood.
- **1.1**
  - listes de favoris (4 listes renommables, déplacement entre listes) ;
  - délai de sécurité TX par clic droit sur PTT ;
  - recalage du WebSDR au choix de la synchro et surveillance du décalage ;
  - bascule automatique de profil sur les OpenWebRX à plusieurs bandes.
- **1.0** — première version.
