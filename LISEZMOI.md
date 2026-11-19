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

1. **1re ligne (connexion et réglages généraux)** : **Interface** (port COM, OmniRig, TCI, HRD, Hamlib), **Marque** et **Modèle** (CAT direct), **Connecter / Connexion... / Déconnecter**, case **Connexion auto au démarrage**, **Ouvrir le journal** (sdrlink.log dans le Bloc-notes), case **Mode sombre**, carré **Palette**, **Look ▾**, **Panneau de commande**, **Taille du texte…**, bouton **Vérifier les mises à jour** avec sa case, **Import/Export ▾**, **Pseudo/Call**.
2. **2e ligne (paramètres de l'interface)** : selon l'interface, **Port** COM (avec ↻ pour relire la liste), **Vitesse**, **Bits de stop**, **Adresse CI-V** (Icom), **RTS** / **DTR**, **Port COM virtuel…** (partage du poste, depuis la 1.49) ; ou **Poste OmniRig** ; ou **Hôte** / **Port** (TCI, HRD, Hamlib) ; **Détection TX (HRD)**. Puis **Langue** et **Câble SDRLINK ▾**. Avec Hamlib, une ligne en plus : **Lancer rigctld**, chemin de rigctld, **N° de modèle Hamlib**, **Télécharger Hamlib…**.
3. **3e ligne (synchronisation)** : **Synchro** (Bidirectionnelle, Radio → WebSDR, WebSDR → Radio, Désactivée), **Synchroniser le mode**, **Décalage WebSDR (Hz)**, **Couper le son du WebSDR en émission** (clic droit : réceptions concernées) et **rétablir après (ms)**, **Notch auto (DNF)** (clic : marche / arrêt, bouton vert en marche ; clic droit : options, type d'origine, adaptatif ou spectral et son niveau, couleurs), **DNR** (réducteur de bruit, depuis la 1.46 ; clic : marche / arrêt, clic droit : type classique, amélioré ou IA et son niveau), **Conf scan**, **▶ Scan**.
4. **4e ligne (affichage et accord)** : bouton **PTT**, fréquence et mode **RADIO**, fréquence et mode **WEBSDR** (clic sur l'une ou l'autre : **clavier de fréquence** et liste **Mem**, depuis la 1.26), boutons **USB, LSB, AM, FM, DIGI, CW**, saisie **kHz** (avec historique), **QSY**, **Save**, **Mémoires** et **▾** (20 dernières), **Pas** de la molette, **BP** (bande passante, depuis la 1.38), cases **Affichage réduit**, **Affichage mini**, **Avant plan**, **Affichage ptt**, puis le bouton **TUNE** (depuis la 1.49), de même forme que PTT et aligné sur lui.
5. **5e ligne (WebSDR)** : adresse **WebSDR** (liste des favoris, propositions pendant la frappe), **liste** de favoris affichée, **Aller**, **◀ Retour**, **Recharger**, **★ Ajouter aux favoris**, **Retirer des favoris**, **Gérer les favoris…**, **Rechercher des WebSDR…**, **FreeDV auto V1 / V2** (depuis la 1.24), bouton des **modes numériques** (SSTV, FT8, CW, WEFAX…, depuis la 1.25), **Mode ?** (recherche du mode écouté, depuis la 1.38).
6. **Ligne de son** : **⇄**, **Mute WebSDR**, **Mute TX**, **Mute ExpertSDR**, **Mute HDSDR**, **Volume WebSDR** (« Volume FreeDV » pendant une parole FreeDV, depuis la 1.45), **Volume TX sur PC** (quand « Écouter aussi le poste sur le PC » est coché, depuis la 1.45), **Volume TX**, **Volume ExpertSDR**, **Volume HDSDR** (seulement les sons disponibles), puis **REC**, **Réinit WebSDR** (depuis la 1.31) et le bouton **Diversité** (depuis la 1.40), suivi, quand la diversité est réglée **sur deux WebSDR**, de la case du **second WebSDR** (depuis la 1.44).
7. **La page du WebSDR** avec ses ronds **partage**, **+** et **−** (depuis la 1.43), puis la **barre d'état** (messages, lien de mise à jour).

## Tous les clics droits

| Où | Ce que propose le clic droit |
|---|---|
| Connecter | couleur quand connecté / déconnecté / poste qui ne répond pas encore |
| Ouvrir le journal, Look, Panneau de commande, Taille du texte, Vérifier les mises à jour, Conf scan, QSY, Save, Aller, Retour, Recharger, Ajouter / Retirer / Gérer les favoris, Rechercher des WebSDR, Pseudo/Call | couleur du bouton |
| FreeDV auto | pas de menu : le clic droit passe de RADE V1 à RADE V2 et inversement (depuis la 1.24) |
| Modes numériques (bouton et fenêtre posée sur la page) | les 14 modes (clic gauche : choisir et lancer ; clic droit : choisir et ouvrir les réglages), **Réglages <mode>…**, **Effacer le texte reçu**, **Ouvrir le dossier des images**, **Réinitialiser la fenêtre <mode>**, **Réinitialiser les fenêtres** (depuis la 1.25), **Fenêtre séparée**, **Plein écran**, **Choix d'un mode : aller sur sa fréquence la plus proche** (depuis la 1.38), couleur arrêté / en marche / signal reçu (depuis la 1.41) |
| Import/Export | **Réinitialiser le programme…** (depuis la 1.22), couleur du bouton |
| Câble SDRLINK (aussi au clic gauche) | sortie qui reçoit le son du WebSDR, nom SDRLINK, téléchargement de VB-CABLE, couleur activé / désactivé |
| Scan | couleur scan arrêté / en marche |
| PTT | délai de sécurité TX, **micro du PC vers le poste** (case, micro, carte son d'émission du poste ; depuis la 1.51), couleur en réception / en émission |
| TUNE | **carte son d'émission du poste**, **niveau de la tonalité** (1 à 100 %, curseur et case, pas de 1), **durée** (5 s à 1 min), **passage en émission : PTT actif ou VOX du poste**, couleur au repos / pendant Tune, **Fermer** en dernière ligne (depuis la 1.49) |
| RADIO | **VFO A / VFO B** commandé (depuis la 1.49), couleur du fond, couleur du texte (le clic gauche ouvre le clavier de fréquence) |
| WEBSDR | couleur du fond, couleur du texte (le clic gauche ouvre le clavier de fréquence) |
| Ligne de la liste **Mem** (clavier de fréquence ; aussi par un appui long) | **Mémoriser dans « Mémoires »** ▸ liste choisie, **Supprimer de la liste « Mem »** (depuis la 1.26) |
| USB, LSB, AM, FM, DIGI, CW | couleur du mode sélectionné / des autres |
| Saisie kHz | supprimer la valeur de l'historique, tout supprimer |
| Mémoires | mémoriser la fréquence actuelle, couleur du bouton |
| Liste des favoris (à côté de l'adresse) | renommer la liste |
| Notch auto (DNF) | fenêtre du notch : coupure en mode numérique et en FreeDV, **notch aussi sur le son du poste** et **type de notch** (d'origine, adaptatif, spectral) (depuis la 1.50), niveau du type choisi, explication, **Couleurs du bouton…** (notch arrêté / en marche) (depuis la 1.48 ; avant, le clic droit ne donnait que les couleurs) |
| DNR | **type** (classique, amélioré, IA ; depuis la 1.48), avec son **niveau** de 1 à 10 en sous-menu (clic droit sur le type), **DNR coupé en mode numérique**, **DNR coupé en FreeDV V1 et V2**, **DNR aussi sur le son du poste** (1.48), couleur DNR arrêté / en marche (depuis la 1.46) |
| Mode ? | couleur du bouton, couleur pendant la recherche (depuis la 1.41) |
| Couper le son du WebSDR en émission | réceptions dont le son est coupé en émission : WebSDR, poste, ExpertSDR, HDSDR (cases à cocher, toutes cochées au départ) |
| ⇄ | réceptions que ⇄ laisse coupées (cases à cocher, deux au plus), couleur du bouton (depuis la 1.41) |
| Mute WebSDR, Mute HDSDR | couleur son coupé / son actif |
| Mute TX | **écouter aussi le poste sur le PC**, carte son du poste (depuis la 1.23), couleur son coupé / son actif |
| Mute ExpertSDR | port TCI d'ExpertSDR, couleur son coupé / son actif |
| « Volume WebSDR » | port TCI d'ExpertSDR |
| REC | carte son du poste, couleur du bouton REC |
| Réinit WebSDR | **Auto** (relance quand le son est perdu) et son délai, **réinitialisation totale** du WebSDR en cours, couleurs des deux états (depuis la 1.37) |
| Diversité | réception mise dans l'autre oreille (poste, ExpertSDR, HDSDR sous Windows 11, ou **Sur deux WebSDR** depuis la 1.44), **WebSDR dans l'oreille droite**, retard mesuré (depuis la 1.40), couleur arrêtée / recherche du retard / calée (depuis la 1.41) |
| Page du WebSDR | **Masquer l'adresse du WebSDR** en tête du menu de la page (depuis la 1.43) |
| Ronds partage, +, − sur la page | **position des boutons** (6 places, verticale ou horizontale), **Zoom de la page : automatique**, **Masquer l'adresse du WebSDR** (depuis la 1.43) |
| Barre d'état | 20 derniers messages, types de messages à ne plus afficher |

## Fichiers de SDRLink

- `%APPDATA%\SDRLink\settings.ini` : **tous les réglages** (interface, favoris, mémoires, couleurs, fenêtre…). C'est ce fichier que copie **Import/Export**.
- `%APPDATA%\SDRLink\settings.avant-import.ini` : les réglages d'avant le dernier import.
- `%APPDATA%\SDRLink\settings.avant-reinitialisation.ini` : les réglages d'avant la dernière réinitialisation (depuis la 1.22).
- `%APPDATA%\SDRLink\settings.reset.txt` : réinitialisation demandée, faite au redémarrage puis effacée (il n'existe que quelques secondes).
- `%APPDATA%\SDRLink\sdrlink.log` : le journal (remis à zéro à chaque lancement).
- `%LOCALAPPDATA%\SDRLink\WebView2` : données des pages WebSDR (cookies, cache ; le pseudo donné aux KiwiSDR et websdr.org y est aussi) ; `%LOCALAPPDATA%\SDRLink\runtime` : WebView2Loader.dll ; `%LOCALAPPDATA%\SDRLink\runtime\freedv-<taille>\sdrlink_freedv.dll` : décodeur FreeDV, écrit seulement au premier usage de FreeDV auto (depuis la 1.24).
- À côté de `SDRLink.exe`, pendant une mise à jour automatique seulement : `SDRLink.exe.download` (le téléchargement) et `SDRLink.previous.exe` (l'ancienne version, supprimée dès que la nouvelle a démarré, depuis la 1.30).
- `Musique\SDRLink` : dossier des enregistrements REC par défaut ; pendant un enregistrement, un fichier provisoire `SDRLink-REC-*.raw` est dans `%TEMP%`.
- `Images\SDRLink` : images SSTV, WEFAX et Hell reçues, en PNG (`SSTV_<mode>_<date>_<heure>.png`, `WEFAX_<lignes>-<IOC>_<date>_<heure>.png`, `Hell_<date>_<heure>.png` ; `_partiel` si l'image n'est pas allée jusqu'au bout) (depuis la 1.25).
- `%LOCALAPPDATA%\SDRLink\runtime\digi-…\sdrlink_digi.dll` : décodeurs FT8 / FT4 / FT2 et Olivia / Contestia, écrit seulement au premier usage de ces modes (depuis la 1.25).
- Registre, `HKEY_CURRENT_USER\Software\Classes\sdrlink` (depuis la 1.46) : écrit à chaque lancement, sans droits d'administrateur, pour que les liens `sdrlink://` (bouton « Ouvrir dans SDRLink » de la page de partage) ouvrent ce SDRLink. Si SDRLink est retiré du PC, cette clé peut être effacée à la main ; laissée, elle ne gêne rien.

## Interfaces (menu « Interface »)

| Interface | Réglages | Remarque |
|---|---|---|
| Port COM (CAT direct) | Marque, modèle, port, vitesse, bits de stop, RTS/DTR, adresse CI-V (Icom) | Le port ne doit pas être ouvert par un autre logiciel ; pour le partager, **Port COM virtuel…** (voir plus bas) |
| OmniRig | Rig 1 / Rig 2 | Le poste se configure dans OmniRig ; permet de partager le poste avec d'autres logiciels. **Si OmniRig ne fonctionne pas, utiliser OmniRig version 1, en mode administrateur** (voir Dépannage) |
| TCI | Hôte / port (40001 par défaut) | ExpertSDR (SunSDR), Thetis… activer le serveur TCI |
| Ham Radio Deluxe | Hôte / port (7809 par défaut) | Activer le serveur IP dans HRD Rig Control |
| Hamlib (rigctld) | Hôte / port (4532 par défaut), « Lancer rigctld », n° de modèle Hamlib | Voir ci-dessous |

### Bouton Connecter et contrôle de la liaison (depuis la 1.34)

- **Connecter** : pas de liaison avec le poste.
- **Connexion...** (orange) : la liaison est lancée mais le poste n'a pas encore répondu, ou ne répond plus. Un clic arrête la tentative.
- **Déconnecter** : le poste répond vraiment ; un clic coupe la liaison.
- La liaison est contrôlée **15 s après « Connecter », puis chaque minute**. Si le poste ne répond pas, la barre d'état le dit avec le dernier message d'erreur. Une liaison figée (OmniRig ou port COM qui ne répond plus) est vue perdue : « Liaison perdue : le poste ne répond plus depuis X s », le bouton repasse à « Connexion... » puis revient seul à « Déconnecter » quand le poste répond de nouveau. Chaque problème est noté dans le journal.
- **Avec OmniRig**, si le poste ne répond pas, une **fenêtre d'aide** s'ouvre au premier plan (une fois par connexion, sans bloquer SDRLink) : utiliser **OmniRig version 1**, relancer **OmniRig et SDRLink en mode administrateur** (elle indique si SDRLink l'est déjà), vérifier dans OmniRig le modèle, le port COM et la vitesse ; avec le lien vers la page d'OmniRig.

### VFO A ou B (depuis la 1.49)

Le **clic droit sur la fréquence RADIO** choisit le VFO du poste piloté par SDRLink : **VFO A** ou **VFO B** (postes à deux VFO), avant les couleurs. Avec le VFO B, l'affichage devient « RADIO B ». Changée pendant la connexion, la liaison est refaite tout de suite.
- **Yaesu** récents, **Kenwood**, **Elecraft**, **OmniRig**, **TCI**, **HRD** : la fréquence du VFO B est lue et réglée directement (`FB`, `FreqB`, canal 1 en TCI…), sans changer le VFO affiché par le poste.
- Mode du VFO B : `MD1` sur les Yaesu à deux récepteurs (FT-2000, FTdx9000, FTdx5000, FTdx101, FTX-1), `MD$` sur les Elecraft ; sur les autres, le mode lu et réglé reste celui du VFO en service.
- **Icom** et **Hamlib** : le poste est mis sur le VFO B à la connexion (CI-V `07 01`, `V VFOB`), puis piloté normalement ; en revenant à A, il est remis une fois sur le VFO A.
- Anciens Yaesu à protocole binaire (FT-817, FT-1000MP…) : VFO A seulement (noté dans le journal).

### Port COM virtuel : partager le poste avec un autre logiciel (depuis la 1.49)

En CAT direct, SDRLink tient le port COM du poste : un autre logiciel (WSJT-X, logiciel de journal…) ne peut plus l'ouvrir. Le bouton **Port COM virtuel…** (2e ligne) règle un partage par une **paire de ports COM virtuels com0com** :
- SDRLink ouvre l'un des deux ports de la paire ; l'autre logiciel ouvre l'**autre port**, avec le **même modèle de poste** (la vitesse n'a pas d'importance sur un port virtuel), comme s'il était branché sur le poste.
- Ses commandes sont passées au poste telles quelles, entre celles de SDRLink, et il reçoit les réponses du poste (texte Yaesu / Kenwood / Elecraft, CI-V Icom / Xiegu, anciens Yaesu binaires).
- **com0com** (pilote gratuit, licence GPL) : s'il n'est pas sur le PC, la fenêtre l'installe (bouton **Installer com0com…**, installateur signé fourni avec SDRLink ; Windows demande l'autorisation d'administrateur). **Créer une nouvelle paire COMx ↔ COMy…** en ajoute une (numéros libres à partir de COM30). Les paires déjà présentes sont proposées. **Supprimer la paire choisie…** en enlève une, après confirmation (Windows demande l'autorisation ; jamais celle que SDRLink utilise en ce moment).
- Le partage démarre à la connexion du poste (choisi pendant la connexion : la liaison est refaite tout de suite). Le bouton passe au **vert** quand le port COM virtuel est en service (couleurs au clic droit) ; la barre d'état indique le port à choisir dans l'autre logiciel. Avec OmniRig, TCI, HRD ou Hamlib, inutile : ils se partagent déjà.

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

Dans la liste **Modèle**, les postes sont classés par numéro (FT-100, FT-450… FT-2000, puis FTdx…, FTX-1). La liste a été comparée à celles de Hamlib, d'OmniRig et de Ham Radio Deluxe ; les protocoles viennent de Hamlib.

- **Yaesu** (40) :
  - protocole actuel (commandes texte) : FT-450/450D, FT-710, FT-891, FT-950, FT-991/991A, FT-2000, FTdx10, FTdx101D/MP, FTdx1200, FTdx3000, FTdx5000, FTdx9000, FTX-1 ;
  - ancien protocole binaire : FT-100/100D, FT-600, FT-650, FT-747GX, FT-757GXII, FT-767GX, FT-817, FT-817ND, FT-818, FT-818ND, FT-840, FT-847, FT-857, FT-857D, FT-890, FT-897, FT-897D, FT-900, FT-920, FT-990, FT-1000, FT-1000D, FT-1000MP, FT-1000MP Mark-V, FT-1000MP Mark-V Field ;
  - commande seule (le poste ne renvoie rien) : FT-757GX, FT-736R.
- **Icom** (55) : IC-78, IC-271, IC-275, IC-375, IC-471, IC-475, IC-575, IC-703, IC-705, IC-706, IC-706MKII, IC-706MKIIG, IC-707, IC-718, IC-725, IC-726, IC-728, IC-729, IC-735, IC-736, IC-737, IC-738, IC-746, IC-746PRO, IC-751/751A, IC-756, IC-756PRO, IC-756PROII, IC-756PROIII, IC-761, IC-765, IC-775, IC-781, IC-820H, IC-821H, IC-905, IC-910H, IC-970, IC-1275, IC-7000, IC-7100, IC-7200, IC-7300, IC-7300MK2, IC-7400, IC-7410, IC-7600, IC-7610, IC-7700, IC-7760, IC-7800, IC-7850, IC-7851, IC-9100, IC-9700
- **Kenwood** (23) : TS-50S, TS-60S, TS-140S, TS-440S, TS-450S, TS-480, TS-570, TS-590S/SG, TS-680S, TS-690S, TS-711, TS-790, TS-811, TS-850S, TS-870S, TS-890S, TS-930S, TS-940S, TS-950S, TS-950SDX, TS-990S, TS-2000, TS-B2000
- **Elecraft** (5) : K2, K3/K3S, K4, KX2, KX3
- **Xiegu** (5, CI-V compatible Icom) : G90, X108G, X5105, X6100, X6200

Pas dans la liste : les **FT-850 et FT-870 n'existent pas chez Yaesu** (ce sont les Kenwood **TS-850S** et **TS-870S**, présents dans la liste Kenwood ; chez Yaesu il y a les FT-840, FT-890 et FT-900). Le **FT-980** n'est pas en CAT direct (son protocole n'est qu'en version d'essai dans Hamlib) : passer par l'interface Hamlib (n° 1031) ou par HRD. Les récepteurs (IC-R…, FRG…, R-5000) et les portatifs FM ne sont pas proposés.

Quand on choisit un modèle, la vitesse, les bits de stop et l'adresse CI-V se règlent sur les valeurs d'usine (celles de Hamlib). Il faut qu'elles correspondent au menu du poste :

- **FT-2000** : menu CAT RATE (4800 par défaut). Testé chez toi : 38400, 1 bit de stop, RTS décoché.
- **FTdx10** : menu CAT RATE ; sur l'USB, prendre le port COM « Enhanced ». Si rien ne répond, essayer RTS coché/décoché.
- **FTdx9000** : 4800 bauds, 2 bits de stop par défaut. **FTX-1** : 38400 bauds, 1 bit de stop par défaut (à régler comme le menu CAT du poste).
- **IC-7000** : adresse CI-V 70 (hex), vitesse CI-V du menu (souvent 19200 ou Auto). CI-V Transceive peut rester sur ON.
- **FT-817/818, FT-857/857D, FT-897/897D** : ancien protocole CAT binaire de Yaesu, 2 bits de stop, vitesse selon le menu CAT RATE du poste (4800 par défaut). Le volume du poste (Mute TX, Volume TX) n'est pas réglable par ce protocole.
- **FT-847, FT-650** : mêmes commandes que le FT-817 ; SDRLink met le poste en mode CAT à la connexion et l'en sort à la déconnexion. Vitesse proposée : 57600 pour le FT-847 (menu CAT RATE), 4800 pour le FT-650. Le FT-847 n'a pas de mode données : un mode données du WebSDR le met en USB ou LSB. Seuls les FT-847 fabriqués à partir de la série 8G05 savent renvoyer leur fréquence (d'après Hamlib).
- **FT-990, FT-1000, FT-1000D, FT-1000MP, Mark-V** : ancien protocole CAT binaire (le même que Hamlib), 4800 bauds, 2 bits de stop, sans réglage de vitesse sur le poste. Fréquence au pas de 10 Hz. Pas de PKT côté USB : un mode données USB du WebSDR met le poste en USB. Le volume du poste (Mute TX, Volume TX) n'est pas réglable. Le FT-1000MP et le Mark-V ne disent pas de façon sûre par CAT s'ils émettent : en CAT direct, la coupure automatique du son à l'émission ne marche donc pas avec eux (le FT-990 et le FT-1000/1000D, eux, le disent). Le FT-990 est lent à recevoir les commandes (50 ms par octet, comme dans Hamlib), le suivi du VFO y est donc un peu plus lent. Par OmniRig ou HRD, choisir le poste dans ces logiciels ; par Hamlib, le numéro de modèle se remplit tout seul (FT-990 : 1016 ; si votre FT-990 a une ancienne ROM et ne répond pas, essayer 1048).
- **FT-100/100D, FT-600, FT-747GX, FT-757GXII, FT-840, FT-890, FT-900, FT-920** : ancien protocole CAT binaire, 4800 bauds, 2 bits de stop (pas de réglage de vitesse sur ces postes). SDRLink lit le bloc d'état du poste, comme Hamlib. Le volume du poste n'est pas réglable. Particularités :
  - **FT-747GX** : le poste renvoie un long bloc (344 octets), une relecture prend presque 1 seconde ; il ne répond pas pendant l'émission (« Le poste ne répond pas » peut s'afficher pendant une longue émission, puis tout reprend) ;
  - **FT-757GXII** : pas de commande PTT par CAT (sur ce poste, l'émission se commande par une broche de la prise CAT) : le bouton PTT de SDRLink n'a pas d'effet, mais l'émission est bien détectée ;
  - **FT-600** : l'état d'émission n'est pas lisible (pas de coupure automatique du son à l'émission), pas de FM ;
  - **FT-920** : modes données DATA-L / DATA-U reconnus.
- **FT-767GX** : protocole particulier (chaque commande est répétée par le poste, puis acquittée), 4800 bauds, 2 bits de stop. SDRLink passe le poste en mode CAT le temps de chaque lecture, puis le rend : la façade est bloquée pendant ces quelques dixièmes de seconde. Pas de commande PTT par CAT ; l'émission est détectée.
- **FT-757GX** (le premier, sans « II ») et **FT-736R** : ces postes reçoivent les ordres mais ne renvoient rien. SDRLink ne peut donc pas lire leur fréquence : il affiche la dernière fréquence qu'il a envoyée, et seul le sens **WebSDR → Radio** a du sens (choisir ce sens dans **Synchro**). Le FT-736R est mis en mode CAT à la connexion ; il accepte le PTT, pas l'AM ; au-dessus de 1,2 GHz la fréquence est codée comme dans Hamlib. Le FT-757GX n'a pas de PTT par CAT.
- **Anciens Kenwood** (TS-50S, TS-60S, TS-140S, TS-440S, TS-450S, TS-680S, TS-690S, TS-711, TS-790, TS-811, TS-930S, TS-940S, TS-950S/SDX) : 4800 bauds, 2 bits de stop (interface IF-232C ou équivalent). Ils ne répondent pas à la lecture du mode `MD;` : SDRLink le voit à la première lecture et lit alors le mode dans la réponse `IF;` (le journal l'indique). Les TS-140S, TS-680S, TS-711 et TS-811 ne disent pas par CAT s'ils émettent (pas de coupure automatique du son à l'émission). **TS-850S** : 4800 bauds, 2 bits de stop. **TS-B2000** : comme le TS-2000.
- **Icom** : chaque modèle a son adresse CI-V d'usine (IC-7760 : B2, IC-706 : 48, IC-706MKII : 4E, IC-7300MK2 : B6, IC-905 : AC…) ; si le poste a été réglé autrement, taper son adresse dans **Adresse CI-V**. Les Icom des années 80 (IC-271, 275, 375, 471, 475, 575, 725, 726, 728, 729, 735, 751, 761, 765, 781, 1275) sont proposés à **1200 bauds**, la vitesse d'origine de leur CI-V. L'**IC-735** utilise une fréquence sur 4 octets (géré automatiquement). Sur les anciens Icom, la lecture d'émission et le volume ne sont pas disponibles : SDRLink le détecte, Volume TX et Mute TX sont alors masqués.
- **Xiegu** : l'adresse CI-V dépend du firmware. Si le poste ne répond pas, essayer une autre adresse :
  - G90 : 88 (valeur d'usine d'après Hamlib et le manuel Radioddity), sinon 70 ;
  - X108G, X5105 : 70 ;
  - X6100 : A4, sinon 70 pour les firmwares antérieurs au 1.1.8 ;
  - X6200 : A4.
  Régler la vitesse comme dans le menu CI-V du poste.

Ces nouveaux modèles (version 1.14) ont été écrits d'après Hamlib et vérifiés sur des blocs de données simulés, pas sur de vrais postes : en cas de souci, le journal (**Ouvrir le journal**) aide à comprendre, et l'interface **Hamlib** reste une solution de repli.

## WebSDR compatibles

Le type est reconnu automatiquement (affiché entre parenthèses) :
- **websdr.org** (PA3FWM), ex. Twente
- **UberSDR** (interface v2), ex. https://wessex.zapto.org/v2/
- **PhantomSDR** (avec le module CATsync), ex. http://linkz.ddns.net:8088/ (French Alps WebSDR)
- **KiwiSDR**
- **OpenWebRX / OpenWebRX+** (seulement dans la plage de la bande affichée)

**Zoom de la cascade gardé** (corrigé dans la 1.36 pour les KiwiSDR) : quand SDRLink change la fréquence ou le mode du WebSDR (synchro avec le poste, clavier de fréquence, mémoires, scan), le zoom choisi sur la page ne change pas. Si la nouvelle fréquence sort de la partie visible, la cascade se décale pour la montrer, au même zoom. Avant la 1.36, un KiwiSDR revenait à chaque fois à la vue de toute la bande.

### Arrivée sur une page (depuis la 1.20)

- **Son sans clic** : le son démarre tout seul. SDRLink clique lui-même sur « Click to start » (UberSDR) et retire le voile « Tap to enable audio » (PhantomSDR, French Alps), dans les 30 secondes qui suivent l'ouverture.
- **Pseudo donné d'office** : les KiwiSDR et les websdr.org qui demandent un nom avant le son reçoivent celui du bouton **Pseudo/Call** (voir plus bas).
- **KiwiSDR complet** : SDRLink appuie une fois sur **Enter queue** (file d'attente) ; le KiwiSDR recharge la page de lui-même dès qu'un canal se libère. Avec des canaux à mot de passe, « Queue or Camp » mène d'abord à cet écran. Une sortie de file voulue par l'utilisateur n'est pas annulée.
- **Squelch ouvert** : sur les WebSDR qui en ont un (case « squelch » des websdr.org, réglette des OpenWebRX), le squelch est ouvert, **sauf en FM à partir de 30 MHz** (relais, FM VHF/UHF) où il est remis tel que la page l'avait. Il n'est changé qu'à l'ouverture et quand on entre ou sort de la FM VHF : un squelch réglé à la main est laissé tel quel.
- **Bonne fréquence entendue** : après un changement de WebSDR, le son correspond bien à la fréquence affichée (sur UberSDR et PhantomSDR, SDRLink attend que le son ait démarré avant de régler la fréquence).
- **websdr.org à une seule bande affichée** (Bordeaux…) : quand la fréquence du poste est dans une autre bande, SDRLink change de bande comme les boutons 80, 40, 20… de la page : la cascade et le curseur passent sur la bonne bande et le mode du poste est gardé (la page inversait LSB/USB). Le rappel du mode près du S-mètre est tenu à jour. Un clic de l'utilisateur sur un bouton de bande de la page fait suivre le poste (fréquence et mode de la page).
- **PhantomSDR (French Alps)** : un clic dans le spectre ou la cascade fait suivre le poste, pas seulement la saisie de la fréquence. Si le son de la page tombe à zéro après un changement de fréquence (défaut de la page : il fallait changer de mode puis revenir), SDRLink renvoie le mode au serveur et le son revient en 1 à 2 secondes.

### Pseudo/Call

Bouton **Pseudo/Call** (1re ligne, après Import/Export) : nom ou indicatif donné d'office aux WebSDR qui en demandent un avant le son (**KiwiSDR**, **websdr.org**). Par défaut **SDRLink**.
- Un clic ouvre une fenêtre qui montre le pseudo actuel ; **OK** l'enregistre et ferme la fenêtre, **Annuler** ne change rien. Le bouton affiche seulement « Pseudo/Call ».
- Le pseudo ne peut pas être **vide** (OK grisé).
- Un nom déjà tapé directement sur un site est **gardé** ; seul le nom que SDRLink avait mis est remplacé quand le pseudo change. Sur KiwiSDR, un pseudo d'un seul caractère est doublé (KiwiSDR en demande deux).
- 30 caractères au plus ; les caractères ; , = ' " sont retirés. Enregistré dans `settings.ini` (ligne `Callsign=`).
- Clic droit : couleur du bouton.

## Sur la page du WebSDR : partage, + et −, adresse masquée (depuis la 1.43)

- **Trois ronds posés sur la page** (comme dans PhoneSDRLink) : **partage**, **+** et **−**. Ils fonctionnent au **clic gauche** ; le **clic droit** sur l'un d'eux choisit leur **position** : **bas à droite**, **bas à gauche**, **haut à droite**, **haut à gauche**, **milieu haut** ou **milieu bas**, chacune **verticale** (en colonne) ou **horizontale** (en ligne). Au départ : milieu bas, horizontal. Position retenue (ligne `PageBtnPos=` des réglages).
- **+ et −** agrandissent ou réduisent **toute la page** du WebSDR (25 % à 300 %), comme le zoom d'un navigateur ; **Ctrl + molette** dans la page fait de même. Le zoom choisi est **retenu pour ce WebSDR** (lignes `PageZoom=` des réglages) et revient à chaque visite, même après un redémarrage de SDRLink ; **Réinit WebSDR** (clic) le garde, seule la **réinitialisation totale** du WebSDR (clic droit sur Réinit WebSDR) l'oublie et remet l'affichage d'origine. Les autres WebSDR restent à 100 %.
- **Zoom automatique des OpenWebRX** : sur certains écrans, le **panneau de commande** d'OpenWebRX (à droite : fréquence, S-mètre, profils, modes, réglages) passe en partie **sous le bandeau du haut** de la page. Sans zoom choisi à la main pour ce WebSDR, SDRLink réduit la page **juste assez** pour le voir **en entier** (la barre d'état le dit), et recommence quand la fenêtre change de taille ; jamais au-dessus de 100 %, jamais sous 50 %. Le clic droit sur un rond montre le zoom actuel ; **Zoom de la page : automatique** oublie le zoom choisi à la main et revient à ce réglage automatique.
- **Partage** (lien de SDRLink depuis la 1.46 ; avant, celui de PhoneSDRLink) : copie dans le **presse-papiers** un **lien seul**, à coller dans un message, un courriel ou un forum. Il n'y a pas de texte autour : avec du texte collé devant, certaines messageries ne rendaient plus le lien cliquable. Le lien mène à la **page de partage de SDRLink**, qui montre la fréquence, le mode et le WebSDR, puis propose :
  - sur un PC : **Ouvrir dans SDRLink**, qui ouvre ce WebSDR **au même endroit** (même fréquence, même mode) ; **Écouter dans le navigateur**, sur la bonne fréquence pour les websdr.org, KiwiSDR et OpenWebRX ; **Télécharger SDRLink**, qui mène à la dernière version ;
  - sur Android : **Ouvrir dans PhoneSDRLink** (même format de lien), **Installer PhoneSDRLink**, ou écouter dans le navigateur.
- **Ouvrir dans SDRLink** marche dès que SDRLink 1.46 (ou plus récent) a été **lancé une fois** sur le PC : il s'inscrit alors auprès de Windows pour les liens `sdrlink://`. Le navigateur demande d'abord la permission d'ouvrir SDRLink. Ensuite :
  - si SDRLink est **déjà ouvert**, c'est lui qui reçoit le lien (pas de second SDRLink) et sa fenêtre passe devant ;
  - sinon SDRLink démarre directement sur ce WebSDR, au lieu du dernier ouvert ;
  - la fréquence et le mode sont appliqués **comme le rappel d'une mémoire** : le **poste** suit aussi, si la synchro va du WebSDR vers le poste (Synchro désactivée : le WebSDR seul). Si le poste se connecte après, c'est lui qui prend la fréquence partagée.
- Un lien de partage (de SDRLink ou de PhoneSDRLink) **collé dans la case WebSDR**, puis Entrée ou Aller, fait la même chose.
- **Adresse masquée** : **clic droit sur la page** du WebSDR, **Masquer l'adresse du WebSDR** (en tête du menu ; aussi au clic droit sur un rond). La case de l'adresse reste en place mais n'affiche que des **étoiles** ; sa flèche ouvre toujours la liste des favoris, et l'adresse ne peut plus être copiée. Le même clic droit (élément coché) la fait revenir. Pratique pour une capture d'écran ou une vidéo. Retenu (ligne `HideUrl=` des réglages).

## Favoris

- La liste déroulante de la barre WebSDR affiche les favoris par leur **nom**. En choisir un ouvre le WebSDR.
- Le menu des **listes** de favoris (à droite) se rouvre s'il se referme tout seul moins de **0,5 s** après son ouverture, sans qu'un choix ait été fait (deux fois au plus ; **Échap** le ferme) : parade à une fermeture intempestive vue pendant la diversité (depuis la 1.41).
- **Propositions pendant la frappe** (depuis la 1.19) : dès que tu tapes un mot (deux lettres au moins) dans la barre WebSDR, les WebSDR dont le **nom ou l'adresse contient ce mot** s'affichent juste dessous, comme dans un moteur de recherche. Il suffit qu'**un des mots** tapés corresponde (« twente », « kiwi », « bretagne »…), sans tenir compte des majuscules ni des accents ; les WebSDR qui correspondent le mieux sont en haut. Un **clic** sur une proposition ouvre le WebSDR ; au clavier, **↓ / ↑** pour choisir, **Entrée** pour ouvrir, **Échap** pour fermer. Seuls les WebSDR de la **liste choisie** dans le menu de droite sont proposés. La **flèche** de la liste déroulante affiche toujours **tous** les WebSDR de la liste, même si du texte est tapé. Une adresse tapée en entier puis **Entrée** (sans choisir de proposition) s'ouvre comme avant.
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

Les doublons sont retirés. La liste s'affiche **triée par distance depuis votre position** (depuis la 1.30).
- **Position (Grid locator)** : laissez la case **vide** pour prendre la **position de l'ordinateur**, donnée par le service de localisation de Windows (GPS, Wi-Fi ou réseau ; il faut que **Paramètres > Confidentialité > Position** soit activé, avec « Autoriser les applications de bureau à accéder à votre position »). Ou tapez votre **locator** (JN18 ou JN18du) ou vos **coordonnées** (48.85, 2.35 ; 48,85 2,35 ; 48.85N 1.5W…). Ce que vous tapez est retenu pour la fois suivante.
  - À droite, SDRLink indique la position utilisée, en locator : « position tapée : JN18du » ou « ordinateur : JN18eu ». Une position fausse s'affiche en rouge.
  - **Position de l'ordinateur** : vide la case et redemande la position à Windows.
  - Position de l'ordinateur inconnue (localisation de Windows désactivée) et rien de tapé : les distances partent du **centre de la France**, comme avant la 1.30 ; c'est écrit en rouge.
- **Distance max par rapport à la position actuelle** (1000 km par défaut ; depuis le centre de la France : France, Belgique, Luxembourg, Suisse, nord de l'Italie et de l'Espagne, ouest de l'Allemagne, Pays-Bas, sud de l'Angleterre…). À augmenter ou réduire à volonté.
- **Filtre** : texte libre (ville, indicatif, département…). **Type** : websdr.org, KiwiSDR, OpenWebRX…
- Les récepteurs **déjà en favoris** (dans n'importe quelle liste) ne sont pas proposés ; le bas de la fenêtre indique combien sont ainsi masqués. Les relais KiwiSDR (xxx.proxy.kiwisdr.com) avec ou sans :8073 comptent comme un seul récepteur.
- Clic sur un titre de colonne : tri par cette colonne.
- **Pays** : estimé d'après le nom (pays cité, indicatif français F…/TK…) ou, pour la France, d'après la position GPS. Vide si inconnu.
- **Écouter la sélection** (ou double-clic) : ouvre le récepteur dans SDRLink pour l'essayer.
- Cocher ceux qui intéressent, puis **Ajouter les cochés aux favoris** : SDRLink demande dans quelle liste les enregistrer. Ils se renomment ensuite dans « Gérer les favoris… ».

**Premier démarrage** : la recherche s'ouvre d'elle-même, une seule fois, si Internet répond, pour remplir les favoris. Sans connexion, elle est retentée au démarrage suivant.

Il faut un accès Internet. Si un annuaire ne répond pas, les autres sont quand même affichés : le bas de la fenêtre indique le résultat de chaque source.

## FreeDV auto (réception FreeDV RADE)

Bouton **FreeDV auto V1** (barre WebSDR, après « Rechercher des WebSDR… », depuis la 1.24) : décodage automatique de la voix numérique **FreeDV RADE**.
- **Clic** : marche / arrêt (arrêt au départ). **Clic droit** : **RADE V1** (FreeDV 2, le mode actuel) ou **RADE V2** (FreeDV 3, encore en développement) ; le bouton affiche « FreeDV auto V1 » ou « V2 ».
- Couleurs : normales à l'arrêt, **vert** en écoute, **orange** pendant une parole décodée.
- En marche, le son écouté est décodé en permanence. Dès qu'un signal FreeDV est reçu (après 0,6 s de décodage continu), le **son brut est coupé** et la **voix décodée** est jouée dans les haut-parleurs du PC. La barre d'état indique par exemple « FreeDV RADE V1 : parole décodée (WebSDR, SNR 15 dB) », SNR tenu à jour. 1,5 s après la fin du signal, le son normal revient. En émission, la voix décodée est muette.
- Le son est décodé **tel qu'il est reçu**, dans le mode choisi par l'opérateur : en USB si le poste ou le WebSDR est en USB, en LSB s'il est en LSB. Un signal FreeDV écouté sur la mauvaise bande latérale n'est pas décodé (on entend le son brut).
- **Une seule réception** est décodée, dans cet ordre :
  1. **le poste** : si Mute TX n'est pas actif et qu'une carte son du poste est choisie (celle de REC, clic droit sur REC ou sur Mute TX) ; il faut donc que le volume du poste soit télécommandé. Pendant la parole, l'écoute du poste sur le PC est coupée ; le volume du poste lui-même n'est pas touché ;
  2. **le WebSDR**, si Mute WebSDR n'est pas actif : la page est mise à volume 0 pendant la parole (Mute WebSDR ne change pas) ;
  3. **ExpertSDR**, par son **flux audio TCI** (depuis la 1.40 ; serveur TCI d'ExpertSDR activé, même port que pour Mute ExpertSDR), puis **HDSDR** (capture du son du programme, **Windows 11** seulement depuis la 1.40 : sous Windows 10, cette capture ne rend que du silence). Leur son brut **reste audible** sous la voix décodée : couper un programme dans le mélangeur de Windows couperait aussi ce que reçoit le décodeur.
- **Volume de la parole** (depuis la 1.45) : pendant une parole décodée, le curseur **Volume WebSDR** devient **Volume FreeDV** et règle le volume de la voix décodée, quelle que soit la réception décodée (poste, WebSDR, ExpertSDR ou HDSDR). À la fin de la parole, il redevient Volume WebSDR et reprend la valeur du WebSDR. Les deux valeurs sont **séparées** : le volume FreeDV ne se règle que pendant une parole FreeDV, le volume du WebSDR le reste du temps. Le volume FreeDV est gardé après redémarrage et fait partie de l'export et de l'import (partie Configuration). Avant la 1.45, la voix décodée était jouée à niveau fixe.
- La parole n'est jouée que si le SNR est d'au moins −5 dB : en dessous, le décodeur peut s'accrocher à tort (par exemple sur la mauvaise bande latérale) et ne sortirait que du charabia.
- **Arrêté, FreeDV ne prend aucune ressource** : le décodeur n'est même pas chargé. En marche, il prend environ 7 à 10 % d'un cœur du processeur.
- Le décodeur est celui de FreeDV (radae_nopy, de David Rowe et Peter B Marks, licence BSD ; vocodeur FARGAN d'Opus, licence BSD), le même que dans PhoneSDRLink. Il n'a pas encore été essayé sur un vrai signal reçu en direct : seulement sur l'enregistrement hors antenne du projet FreeDV.

## Modes numériques : SSTV, FT8, CW, WEFAX… (réception)

Bouton des **modes numériques** (barre WebSDR, après « FreeDV auto », depuis la 1.25) : un seul bouton pour tous les décodeurs repris de PhoneSDRLink, plus WEFAX. Son texte est le nom du mode choisi (« SSTV » au départ).
- **Clic** : marche / arrêt (arrêt au départ). Couleurs : normales à l'arrêt, **vert** en écoute, **orange** pendant une réception (image SSTV ou WEFAX en cours, texte ou message décodé il y a moins de 2 s, image Hell qui s'écrit).
- **Clic droit** (sur le bouton ou sur la fenêtre du mode) : menu des modes. **Clic gauche sur un mode** : il devient le mode du bouton et le décodage se met en marche. **Clic droit sur un mode** : même chose, et la fenêtre de **réglages** de ce mode s'ouvre ; si c'était déjà le mode choisi, seule la fenêtre de réglages s'ouvre. En bas du menu : **Réglages <mode>…**, **Effacer le texte reçu** (modes à texte), **Ouvrir le dossier des images**, **Réinitialiser la fenêtre <mode>** (seule la fenêtre du mode affiché reprend sa place et sa taille d'origine), **Réinitialiser les fenêtres** (toutes les fenêtres des modes reprennent leur place, en bas à gauche, et leur taille d'origine).
- **Accord automatique** (depuis la 1.38) : choisir un mode dans le menu accorde sur **sa fréquence d'usage la plus proche** de la fréquence actuelle (liste « Modes numériques » des Mémoires, région IARU choisie), avec le mode du récepteur (USB, CW…), selon le sens de synchronisation. Par exemple vers 7100 kHz : FT8 → 7074, RTTY → 7040, CW → 7030. Case **Choix d'un mode : aller sur sa fréquence la plus proche** dans le même menu pour l'arrêter.
- **Fenêtre séparée** et **plein écran** (depuis la 1.38), dans le même menu : **Fenêtre séparée (déplaçable sur un autre écran)** sort la fenêtre du mode de la page : elle devient une fenêtre normale, à placer et agrandir où l'on veut, par exemple sur un **second écran**. **Plein écran** la met sur tout l'écran où elle se trouve ; aussi **F11** ou **double-clic** dans la fenêtre, **Échap** pour sortir. Fermer la fenêtre séparée la remet sur la page (le décodage continue). Place, fenêtre séparée et plein écran sont retenus, même après redémarrage ; **Réinitialiser la fenêtre** la ramène à côté de SDRLink.
- Bouton **Mode ?** (depuis la 1.38), juste après le bouton des modes : **recherche du mode écouté**. Pendant **33 s au plus** (le bouton devient orange avec le compte à rebours), le son est essayé en même temps avec FT8, FT4, FT2, JS8, SSTV, WEFAX, RTTY, PSK31, CW et Olivia. Un message FT8, FT4, FT2 ou JS8, un en-tête SSTV ou un ton de début WEFAX donnent une réponse sûre tout de suite ; pour RTTY, PSK, CW et Olivia, le texte décodé est noté (indicatifs, CQ, DE, 73, RST…). Le mode reconnu devient celui du bouton et son décodage se met en marche, **sans changer de fréquence**. Nouveau clic : arrêter la recherche. WSPR, JT9 et Hell ne sont pas cherchés (il faudrait plusieurs minutes). Les décodeurs ne tournent que le temps de la recherche.
  - **Plusieurs signaux dans la bande passante** (depuis la 1.39) : chaque signal net reçoit ses propres décodeurs CW, PSK et RTTY, réglés sur sa fréquence (jusqu'à 4 signaux) ; un signal plus fort à côté ne gêne plus la lecture d'un autre. Le message indique la fréquence du signal reconnu (« vers 620 Hz »).
  - **Rythme des tranches FT8 / FT4** (depuis la 1.39) : même si aucun message n'est encore décodé, SDRLink reconnaît que toutes les émissions s'arrêtent ensemble à la fin de chaque tranche de 15 s (FT8) ou 7,5 s (FT4). Il en déduit le **retard du son** du WebSDR, le donne au décodeur (un retard de plus de 2 s empêchait tout décodage) et attend encore une tranche pour lire un message (jusqu'à 20 s de plus). Le décodeur lancé ensuite garde ce retard.
  - **Quel son est analysé** : le même que celui des décodeurs, dans cet ordre : le poste (BF reçue par la carte son de réception, si elle est réglée et le poste non coupé), le WebSDR, ExpertSDR, HDSDR. **Mute WebSDR n'empêche pas la recherche** : le son est pris dans la page avant le volume. Si tout est coupé, la recherche écoute le poste ou le WebSDR ; ExpertSDR et HDSDR coupés ne donnent plus de son.
  - **WSPR** n'est pas cherché : une émission dure près de 2 minutes et ne se décode qu'une fois finie (2 à 4 minutes d'écoute). Choisir WSPR dans le menu des modes.
- **Contrôle automatique de fréquence** : CW, RTTY et PSK ont leur **AFC** (coché au départ, dans leurs réglages) : le décodeur suit le signal même un peu à côté ou qui glisse. Olivia, Contestia et Hell cherchent eux-mêmes le signal dans le son ; FT8, FT4, FT2, WSPR, JT9 et JS8 décodent toute la bande passante.
- Modes : **FT8**, **FT4**, **FT2**, **WSPR**, **JT9**, **JS8** (tranches UTC : l'heure du PC doit être exacte), **CW**, **RTTY**, **PSK31 / PSK63**, **Olivia**, **Contestia**, **Hell** (Feld Hell, Slow Hell, Hell X5, X9, FM Hell 105 / 245, Hell 80), **SSTV** (26 modes), **WEFAX** (fac-similé météo, ou FAX radioamateur). Un seul mode décode à la fois ; FreeDV auto peut marcher en même temps.
- **Fenêtre de réglages** : une explication du mode (fréquences habituelles, colonnes affichées), la **surbrillance** (modes à texte : indicatifs ou textes à repérer, séparés par des virgules, surlignés en vert) et les réglages du décodeur, les mêmes que dans PhoneSDRLink : CW (tonalité, filtre, vitesse, seuils, AFC, AGC), RTTY (débit, décalage, polarité, fréquence, AFC, UOS, squelch), PSK (fréquence, PSK31 / PSK63, AFC, squelch), Olivia et Contestia (tons, bande, centre, inversion, recherche, synchro), Hell (variante, fréquence, squelch, inversion, **Effacer l'écran**, **Enregistrer l'image**), JS8 (vitesse), SSTV (sensibilité, perte data, enregistrement automatique), WEFAX (voir plus bas). **Valeurs par défaut** remet les réglages du mode. Tout est appliqué tout de suite et **retenu, même après redémarrage**, pour chaque mode (comme le mode choisi, la surbrillance et la marche / arrêt).
- En marche, une **fenêtre** est posée sur la page WebSDR, **en bas à gauche** au départ, avec en légende l'état du décodeur. Modes à texte : les dernières lignes, la plus récente en bas (FT8, FT4, FT2, WSPR, JT9, JS8 : les deux périodes en deux couleurs ; CW, RTTY, PSK, Olivia, Contestia : une ligne datée en UTC environ tous les 70 caractères). SSTV : l'image ligne par ligne. Hell : la bande d'écriture qui défile. WEFAX : le bas de l'image en cours.
- **Déplacer la fenêtre** : **appui long** (0,4 s, le cadre devient orange), puis glisser vers le haut, le bas, la gauche ou la droite, et lâcher.
- **Agrandir ou réduire la fenêtre** : comme une fenêtre Windows, tirer avec le clic gauche sur un **bord** ou un **coin** (le pointeur devient une double flèche ; pas besoin d'appui long). La fenêtre reste dans la page.
  - **SSTV** et **Hell** : les proportions de la fenêtre sont **fixes** (celles de l'image) ; la largeur et la hauteur bougent ensemble et l'image décodée est **agrandie d'autant**, sans déformation. En SSTV, quand une image d'un autre format arrive (par exemple PD120, 640 × 496), la fenêtre garde sa largeur et prend la hauteur de ce format. Si la fenêtre agrandie ne tient plus en bas de la page, elle remonte.
  - **Modes à texte** : largeur et hauteur libres ; plus haute, la fenêtre montre plus de lignes. **WEFAX** : libre aussi ; plus haute, elle montre plus de lignes de la carte.
  - Quand la fenêtre de SDRLink change de taille, celle du mode suit en proportion.
- Chaque mode a **sa propre fenêtre** : place et taille sont **retenues pour chaque mode, même après redémarrage** (la fenêtre CW agrandie ne change rien à la fenêtre SSTV). **Réinitialiser la fenêtre <mode>** (menu) remet seulement celle du mode affiché, **Réinitialiser les fenêtres** les remet toutes.
- **Taille d'origine** : SSTV, l'image tient dans 42 % de la largeur et de la hauteur de la page ; modes à texte, 68 % de la hauteur de la page (jusqu'à 74 caractères de large) ; Hell et WEFAX, deux fois la hauteur de l'image seule (Hell jusqu'à 92 % de la largeur de la page).
- La réception décodée est choisie **comme pour FreeDV auto**, une seule à la fois : **le poste** (Mute TX pas actif et carte son du poste choisie), sinon **le WebSDR** (Mute WebSDR pas actif), sinon **ExpertSDR**, puis **HDSDR**. Le **son n'est pas coupé**.
- **Toutes les réceptions coupées** (depuis la 1.28) : le décodage **continue** sur le poste ou le WebSDR (celui qui était décodé, sinon le poste, sinon le WebSDR), le son restant coupé ; la parole FreeDV n'est alors pas jouée. ExpertSDR et HDSDR coupés ne peuvent pas être décodés : Windows ne transmet plus rien d'un programme coupé.
- **SSTV** : le mode est reconnu tout seul par le code VIS : Robot 36 et 72, Robot 8, 12 et 24 N&B, Martin M1 à M4, Scottie S1 à S4 et DX, PD50, PD90, PD120, PD160, PD180, PD240, PD290, Wraase SC2-120 et SC2-180, Pasokon P3, P5 et P7. Une image prise **en cours de route** est aussi reconnue, par le rythme de ses synchros. Si le signal est perdu, l'image attend le temps de la « perte data » (12 s au départ) puis s'arrête. Si l'émission **continue après le bas de l'image** (image prise en cours de route, puis la suivante sans code VIS), l'image **remonte d'une ligne à chaque ligne reçue**, la plus récente en bas (depuis la 1.28) ; à la fin de l'émission, l'image enregistrée est celle qui est affichée (les dernières lignes reçues), donc en général l'image suivante en entier.
- **WEFAX** : se mettre en **USB 1,9 kHz sous la fréquence publiée** (par exemple DWD 7880 kHz → 7878,1 kHz). Noir 1500 Hz, blanc 2300 Hz. L'image commence au **ton de début** (IOC 576 ou 288 reconnu), le début des lignes est calé sur les **lignes de calage**, et elle se termine au **ton de fin**. Réglages : lignes par minute (60, 90, **120**, 240), IOC sans ton de début, attendre ou non le ton de début, ton de fin, calage, fréquence du milieu, **correction de pente** (en ppm, si l'image penche), **Commencer une image maintenant**, **Terminer l'image et l'enregistrer**.
- Les images **SSTV** et **WEFAX** d'au moins 16 lignes sont **enregistrées en PNG** dans `Images\SDRLink` (`SSTV_Robot36_20261107_101500.png`, `WEFAX_120-576_20261107_101500.png` ; `_partiel` si l'image n'est pas allée jusqu'au bout). **Hell** : avec **Enregistrer l'image** (`Hell_<date>_<heure>.png`).
- **Arrêté, rien ne tourne** et la fenêtre est cachée.
- Les décodeurs sont ceux de PhoneSDRLink : ft8_lib (Kārlis Goba, licence MIT) pour FT8, FT4 et FT2 ; bibliothèque MFSK de Pawel Jalocha (celle de fldigi, GNU GPL) pour Olivia et Contestia ; les autres écrits pour PhoneSDRLink. WEFAX est écrit pour SDRLink. Ils ont été essayés sur des signaux de test et des enregistrements, **pas encore sur de vrais signaux reçus en direct** dans SDRLink : vos retours sont les bienvenus.

## Volume du WebSDR

La ligne de son, toujours en début de ligne sous la barre WebSDR, se lit de gauche à droite : le bouton ⇄, Mute WebSDR, Mute TX, Mute ExpertSDR, Mute HDSDR, puis Volume WebSDR, Volume TX sur PC (depuis la 1.45), Volume TX, Volume ExpertSDR et Volume HDSDR, chacun avec sa réglette et sa valeur en %. Mute TX et Volume TX n'apparaissent que si le volume du poste est télécommandé, Mute ExpertSDR et Volume ExpertSDR que si ExpertSDR est lancé et répond (son Windows ou TCI), Mute HDSDR et Volume HDSDR que si HDSDR est lancé, et ⇄ que s'il y a au moins deux sons à permuter. Les commandes absentes ne laissent pas de vide : la ligne se resserre à gauche. Le curseur **Volume WebSDR** règle uniquement le son du WebSDR, pas celui de Windows ni des autres logiciels.
- Il agit sur tous les types de WebSDR. Le son démarre sans clic, même sur UberSDR (« Click to start ») et PhantomSDR (« Tap to enable audio »), depuis la 1.20.
- Le volume est **mémorisé pour chaque favori** : en rouvrant un favori, on retrouve son volume.
- Les WebSDR qui ne sont pas en favoris partagent un même volume, lui aussi mémorisé.
- Un favori ajouté garde le volume du moment. L'export (HTML ou CSV) inclut le volume, et l'import le reprend.
- **Pendant une parole FreeDV** (FreeDV auto, depuis la 1.45), le même curseur s'appelle **Volume FreeDV** et règle la voix décodée ; il a sa propre valeur, gardée à part. Dès la fin de la parole, il redevient Volume WebSDR avec la valeur du WebSDR (voir FreeDV auto).

### Les volumes en bref (1.45)

| Curseur | Ce qu'il règle | Quand on le voit | Gardé |
|---|---|---|---|
| **Volume WebSDR** | le son du WebSDR (et les deux oreilles en diversité sur deux WebSDR) | toujours, hors parole FreeDV | par favori ; un volume commun pour les WebSDR hors favoris |
| **Volume FreeDV** (même curseur) | la voix FreeDV décodée | seulement pendant une parole FreeDV | une valeur, dans les réglages |
| **Volume TX sur PC** | le son du poste rejoué dans les haut-parleurs du PC | quand « Écouter aussi le poste sur le PC » est coché et que Mute TX est affiché | une valeur, dans les réglages |
| **Volume TX** | le gain BF du poste lui-même, par CAT | quand le volume du poste est télécommandé | dans le poste |
| **Volume ExpertSDR**, **Volume HDSDR** | le volume du logiciel (mélangeur de Windows ou TCI) | quand le logiciel tourne | dans le logiciel |

Tous les volumes gardés par SDRLink font partie de l'export et de l'import des réglages.

**Pas de 1 %** (depuis la 1.47) : sur tous ces curseurs (Volume WebSDR / FreeDV, Volume TX sur PC, Volume TX, Volume ExpertSDR, Volume HDSDR), un cran de **molette**, une **flèche du clavier** ou un **clic** à côté du curseur change le volume de **1 %**. Avant, la molette avançait du nombre de lignes réglé dans Windows (souvent 3 ou 6), et le clic de 10 %. Le curseur se tire toujours à la souris pour aller vite.

## Coupure du son et volume du poste

À droite du volume du WebSDR :
- **⇄** (petit bouton carré) : passe le son d'une réception à l'autre parmi **Mute WebSDR**, **Mute TX**, **Mute ExpertSDR** et **Mute HDSDR** (seulement celles qui sont disponibles : Mute TX si le volume du poste est télécommandé, Mute ExpertSDR si ExpertSDR répond, Mute HDSDR si HDSDR joue par le son de Windows) :
  - aucun son coupé : un clic les coupe tous ;
  - un seul son actif : il est coupé et le suivant est rétabli ; chaque clic passe au suivant (WebSDR → TX → ExpertSDR → HDSDR → WebSDR…) ;
  - tout coupé : le premier est rétabli ; plusieurs sons actifs (sans l'être tous) : tous coupés.
  - **Clic droit** sur ⇄ : cocher les coupures dont ⇄ ne tient pas compte, **deux au plus** (ou « Aucune » pour tout décocher). Une coupure cochée est coupée tout de suite et le reste ; ⇄ ne permute alors qu'entre les réceptions restantes. Avec quatre réceptions, en cocher deux permet de ne permuter qu'entre les deux autres. Quand deux sont cochées, les autres sont grisées : décocher l'une pour en choisir une autre. Le choix est conservé.
- Couleurs des boutons Mute : **vert, écriture blanche** quand le son passe (comme le bouton de mode choisi), **rouge, écriture blanche** quand il est coupé, gris quand la coupure n'est pas disponible.
- **Mute WebSDR** : coupe le son du WebSDR (bouton rouge), un second clic le rétablit au volume réglé.
- **Mute TX** : coupe le son du **transceiver** en mettant son gain BF à zéro par CAT, puis rétablit le volume au second clic. Le son est aussi rétabli à la déconnexion.
  - **Écouter aussi le poste sur le PC** (depuis la 1.23, clic droit sur Mute TX, non coché au départ) : le son qui arrive du poste sur la **carte son du poste** (la même que celle de REC, par exemple « USB AUDIO CODEC ») est aussi joué dans les **haut-parleurs du PC** (sortie par défaut de Windows), avec un retard de quelques centièmes de seconde, **tant que Mute TX n'est pas actif**. Mute TX actif (à la main, par ⇄ ou pendant l'émission) : le PC se tait aussi, et le son revient dès que Mute TX est relâché. Ne marche que poste connecté, quand Mute TX est affiché ; le choix est gardé.
  - Pas de carte choisie, ou carte absente : cocher l'option ouvre la fenêtre **Carte son du poste** (liste des entrées de Windows) au lieu d'un message d'erreur ; Annuler ne change rien. **Carte son du poste…** (même clic droit) ouvre cette fenêtre à tout moment. Une carte débranchée en cours de route est signalée dans la barre d'état ; le son repart seul quand elle revient.
  - **Volume TX sur PC** (depuis la 1.45) : curseur placé entre Volume WebSDR et Volume TX, visible seulement quand cette option est cochée (et Mute TX affiché). Il règle le niveau du poste **dans les haut-parleurs du PC**, de 0 à 100 % ; 100 % est le niveau fixe des versions d'avant. Il ne touche **ni au poste** (Volume TX règle le gain BF du poste), **ni à ce que captent les autres fonctions** : REC, la diversité et les décodeurs (FreeDV auto, modes numériques) prennent chacun le son de la carte son du poste directement, avant ce volume. Même à 0 %, ils reçoivent donc le son complet. La valeur est gardée après redémarrage et fait partie de l'export et de l'import (partie Configuration).
  - La sortie USB de beaucoup de postes ne suit pas leur bouton de volume (ni Volume TX) : c'est Volume TX sur PC (ou le volume de Windows) qui règle ce que l'on entend sur le PC. Ne pas mettre comme sortie par défaut de Windows l'entrée d'émission d'un poste : ce son y repartirait (comme celui du WebSDR).
- **Mute ExpertSDR** : n'apparaît que lorsque le logiciel **ExpertSDR** (2 ou 3) tourne et répond. Il coupe et rétablit le son d'ExpertSDR sans aller dans ExpertSDR :
  - par le **mélangeur de volume de Windows** si ExpertSDR joue par le son de Windows ;
  - sinon (son sorti par le poste lui-même, MB1, ou par ASIO) par la commande **TCI** `mute` : il faut alors activer le serveur TCI dans ExpertSDR (Options > TCI). SDRLink garde une liaison TCI avec ExpertSDR tant qu'il tourne, même si le poste est relié autrement (HRD…) : une coupure ou un volume réglé dans ExpertSDR s'affiche aussi dans SDRLink. Un message l'explique si le serveur TCI ne répond pas.
  - **Clic droit** sur Mute ExpertSDR, ou sur « Volume WebSDR » : port du serveur TCI d'ExpertSDR (**40001** par défaut), à régler comme dans ExpertSDR. Tant qu'ExpertSDR ne répond pas, Mute et Volume ExpertSDR sont masqués et la barre d'état indique de passer par « Volume WebSDR ».
  - Si SDRLink a coupé ExpertSDR, le son est rétabli à la fermeture de SDRLink.
- **Mute HDSDR** : n'apparaît que lorsque le logiciel **HDSDR** tourne (sur le même PC), qu'il soit relié au poste par HRD, OmniRig ou autrement. Il coupe et rétablit le son de HDSDR dans le **mélangeur de volume de Windows**, sans toucher à HDSDR ni à sa liaison CAT, et sans port COM supplémentaire. ExpertSDR et HDSDR peuvent tourner en même temps : chacun a son Mute et son volume.
  - HDSDR doit jouer par le son de Windows (sortie MME ou WDM, réglage habituel). Si sa réception est arrêtée ou si son son sort par ASIO, Mute HDSDR reste gris, Volume HDSDR affiche « — » et un clic sur Mute HDSDR l'explique dans la barre d'état.
  - Une coupure ou un volume réglé dans le mélangeur de Windows s'affiche aussi dans SDRLink (relu toutes les 3 secondes).
  - Si SDRLink a coupé HDSDR, le son est rétabli à la fermeture de SDRLink.
  - **Clic droit** sur Mute HDSDR : couleur son coupé / son actif.
- **Volume TX** : règle le volume du transceiver (gain BF, en %). La barre suit aussi le bouton du poste : elle est relue toutes les 2 secondes environ.
- **Volume TX** et **Mute TX** ne s'affichent que si le volume du poste est télécommandé (poste connecté, et interface qui le permet).
- **Volume ExpertSDR** (à droite de Volume TX) : volume d'ExpertSDR, par le mélangeur de Windows ou par TCI (`volume:` de −60 à 0 dB) comme Mute ExpertSDR. Affiché quand ExpertSDR tourne ; grisé tant qu'aucune des deux voies ne répond.
- **Volume HDSDR** (à droite de Volume ExpertSDR) : volume de HDSDR dans le mélangeur de Windows (0 à 100 %). Affiché dès que HDSDR est lancé, il disparaît quand HDSDR est fermé ; grisé tant que HDSDR ne joue pas par le son de Windows.

Commandes utilisées (vérifiées dans le manuel CAT Yaesu et dans Hamlib) :
- **Yaesu** : `AG0nnn;`, de 000 à 255.
- **Kenwood** : `AGnnn;` ou `AG0nnn;` selon le modèle, détecté automatiquement. De 000 à 255.
- **Elecraft** : `AGnnn;`, de 000 à 250 (K3, KX3, KX2) ou de 000 à 060 (K4).
- **Icom / Xiegu** : CI-V `14 01`, valeur de 0000 à 0255.
- **TCI** : `volume:` de −60 à 0 dB.
- **HRD** : curseur « AF gain (main) » de HRD Rig Control (sinon « AF gain » ou « Volume »), par `get/set slider-pos`. Il est recherché à la connexion, et de nouveau quand on change de poste dans HRD (FT-2000, FTdx10, FTdx5000…) sans reconnecter SDRLink ; le journal l'indique par la ligne « HRD curseur volume … ». Si HRD n'a pas de curseur de volume pour le poste, Volume TX et Mute TX sont masqués et le journal liste les curseurs disponibles.
- **Non disponible** avec les Yaesu à ancien protocole binaire (FT-100, FT-600, FT-650, FT-736R, FT-747GX, FT-757GX/GXII, FT-767GX, FT-817/818, FT-840, FT-847, FT-857, FT-890, FT-897, FT-900, FT-920, FT-990, FT-1000/1000D/1000MP : pas de commande), avec les anciens Icom et Kenwood qui ne connaissent pas la commande (détecté automatiquement) et avec OmniRig (pas de réglage de volume) : Volume TX et Mute TX sont alors masqués.

## Mode sombre

La case **Mode sombre**, à droite de « Ouvrir le journal », passe toute l'interface en couleurs sombres : fenêtre, barre de titre (Windows 10 20H1 et plus, Windows 11), listes, boutons et gestionnaire de favoris. La page du WebSDR garde ses propres couleurs.
- Les fréquences, le PTT et les boutons de mode prennent des teintes plus claires, lisibles dans la pénombre.
- Les réglages grisés pendant la connexion restent lisibles.
- **Palette** : le petit carré multicolore à droite de « Mode sombre » ouvre un choix de couleurs de fond : 64 teintes, en deux moitiés de 32 de part et d'autre du bouton (à gauche les neutres et les tons froids, à droite les tons chauds).
  - En mode sombre : uniquement des teintes foncées (noirs et graphites, gris bleutés, bleus nuit, bleu-vert et verts foncés ; violets, prunes, bordeaux, rouges très foncés, bruns, olives).
  - En mode clair : uniquement des teintes claires (blancs et gris perle, bleus, vert d'eau, verts tendres ; lavandes, roses, saumon, crèmes, jaunes pâles, sables, beiges).
  - **Couleur au choix** (sous la palette) : tapez les valeurs **Rouge**, **Vert**, **Bleu** (0 à 255) puis **Appliquer** (ou Entrée) ; l'aperçu « Texte » montre le fond avec la couleur du texte de ce mode, pour vérifier qu'il reste lisible. **Autre couleur…** ouvre le sélecteur de couleurs de Windows. La case est préremplie avec la couleur en cours.
  - Le reste de l'interface (champs, boutons, bordures) s'accorde automatiquement. « Par défaut » revient aux couleurs d'origine.
  - Le choix est mémorisé séparément pour le mode sombre et le mode clair.
- **Look ▾** (depuis la 1.33), à droite de la palette : allure de tous les boutons, au choix dans un menu. Les boutons gardent exactement leur taille et leur place ; seul le dessin change.
  - **Classique** : l'allure habituelle (par défaut).
  - **Win3.11** : gris, cadre noir et relief d'époque, pour le clin d'œil.
  - **Néon IA** : verre sombre (nacré en mode clair), liseré dégradé cyan-violet, halo au survol.
  - **Moderne** : à plat, coins légèrement arrondis, façon Windows 11.
  - **Verre** : boutons bleus brillants avec reflet.
  - **Terminal** : fond noir, texte vert phosphore, inversé quand on appuie.
  - **Métal** : alu brossé en léger relief.
  - Les couleurs qui ont un sens restent visibles dans tous les looks (PTT en émission, mode actif, Scan, REC, couleurs choisies au clic droit) ; le look suit le mode sombre ou clair et la palette. Le choix est retenu au prochain lancement et suit l'export des réglages.
- **Panneau de commande** (depuis la 1.35), juste après Look : chaque clic fait passer le **panneau de commande** (de la ligne « Interface : » jusqu'à la ligne des Mute comprise) du **haut de la fenêtre** à **sous la page du WebSDR**, et inversement. Le bouton garde toujours le même nom. La **barre d'état reste tout en bas**. La page n'est pas rechargée (le son continue). Les affichages réduit et mini fonctionnent aussi avec le panneau en bas (réduit : la page en haut, puis Synchro, PTT et WebSDR/Mute). La position est retenue au prochain lancement et suit l'export des réglages.

## Couleurs des boutons (clic droit)

Un clic droit sur la plupart des boutons propose leur **couleur de fond**. Si le bouton avait déjà un clic droit (PTT, Mémoires, Câble SDRLINK, Mute ExpertSDR…), ce menu est conservé et les couleurs sont ajoutées à la fin.
- **Un seul choix** (« Couleur du bouton ») : Ouvrir le journal, Look, Panneau de commande, Taille du texte, Conf scan, QSY, Save, Mémoires, Aller, Retour, Recharger, Ajouter aux favoris, Retirer des favoris, Gérer les favoris, Rechercher des WebSDR, Import/Export, ⇄.
- **Deux choix, un pour chaque état** :
  - **Connecter** : quand connecté / quand déconnecté / quand le poste ne répond pas encore (orange par défaut) ;
  - **Câble SDRLINK** : quand activé / quand désactivé ;
  - **Scan** : scan arrêté / scan en marche ;
  - **Réinit WebSDR** : Auto arrêté / Auto en marche (« Réinit Auto WebSDR ») ;
  - **PTT** : en réception / en émission (PTT activé) ;
  - **RADIO** et **WEBSDR** (fréquence et mode) : couleur du fond / couleur du texte (en émission, RADIO passe toujours en rouge) ;
  - **USB, LSB, AM, FM, DIGI, CW** : mode sélectionné / modes non sélectionnés (le choix vaut pour les six boutons) ;
  - **Mute WebSDR**, **Mute TX**, **Mute ExpertSDR**, **Mute HDSDR** (chacun le sien) : son coupé / son actif. Un Mute indisponible reste grisé.
  - **Notch auto (DNF)** : notch arrêté / en marche (depuis la 1.41 ; l'ancienne couleur unique vaut pour les deux tant qu'elles ne sont pas choisies) ;
  - **Mode ?** : au repos / pendant la recherche (depuis la 1.41) ;
  - **Diversité** : arrêtée / recherche du retard / calée (depuis la 1.41) ;
  - bouton des **modes numériques** (SSTV, FT8…) : décodage arrêté / en marche / signal en cours de réception (depuis la 1.41).
- **FreeDV auto** n'a pas de couleur réglable : son clic droit passe directement de RADE V1 à V2, sans menu.
- Couleurs proposées : **Par défaut** (couleur d'origine, qui suit le mode sombre et la palette), bleu marine, bleu, bleu ciel, bleu canard, vert, rouge, bordeaux, violet, rose, marron, gris, noir, jaune, orange, blanc, ou **Autre couleur…** au choix.
- Le texte passe en noir sur les couleurs claires et en blanc sur les foncées ; une bordure fait contour.
- Les choix sont conservés, y compris en passant du mode clair au mode sombre.
- Le bouton REC garde son propre menu (carte son et couleur, voir « Enregistrement (REC) »).

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
- **Au démarrage de SDRLink, le poste ne change jamais** de fréquence ni de mode : en Bidirectionnelle ou Radio → WebSDR, c'est le WebSDR qui se cale sur le poste ; en WebSDR → Radio ou Désactivée, rien ne change. De même, pendant les 8 secondes qui suivent le chargement d'une page ou la connexion du poste, un changement fait **par la page seule** ne commande pas le poste : une page encore en train de s'installer annonce parfois son ancienne fréquence, ou part d'elle-même ailleurs (UberSDR à la première visite) ; elle est remise sur le poste. En revanche, **un clic, une touche ou la molette de l'utilisateur dans la page** est suivi tout de suite (depuis la 1.20) : on peut cliquer dans la cascade dès que la page est calée sur le poste, le poste suit en 0,2 seconde environ. Sur Twente, c'est l'échelle des fréquences sous la cascade qui règle la fréquence, pas la cascade.
- En **Bidirectionnelle**, le **poste fait foi** : si le WebSDR reste décalé de plus de 3 secondes, il est recalé sur le poste. C'est le cas si un réglage est perdu ou si la page est lente. En « Radio → WebSDR », on peut au contraire écarter le WebSDR du poste (molette ou clic dans la cascade) : il n'est recalé qu'au prochain mouvement du VFO.
- **OpenWebRX à plusieurs profils** (un profil par bande, par ex. « HF_7MHz », « VHF_144-146MHz ») : si la fréquence demandée est hors du profil actif, SDRLink bascule automatiquement sur le profil qui la couvre, d'après son nom, puis règle la fréquence. Le changement de profil s'applique à tous les auditeurs de ce récepteur, comme lorsqu'on le fait à la main.
- **Décalage WebSDR (Hz)** : ajouté à la fréquence du poste côté WebSDR (transverter, écart de calibration…)
- **◀ Retour** (barre WebSDR) : revient à la page précédente, comme la flèche retour d'un navigateur. Utile après avoir suivi un lien, par exemple dans un annuaire de WebSDR.
- **Changement de WebSDR sans le poste** (depuis la 1.28) : quand le poste est déconnecté, ou que la synchronisation est **Désactivée** ou en **WebSDR → Radio**, le nouveau WebSDR (favori, adresse, lien, **◀ Retour**, **Recharger**) reprend la **fréquence et le mode** de celui qu'on vient de quitter ; s'il part de lui-même ailleurs pendant sa mise en place, il y est remis. Poste connecté en Bidirectionnelle ou Radio → WebSDR : c'est le poste qui impose sa fréquence, comme avant.
- Si le poste est en DATA-U et le WebSDR en USB, SDRLink ne change pas le mode du poste (même famille).
- **Synchro Désactivée : le WebSDR seul** (depuis la 1.32). La case **kHz** avec **QSY**, le rappel d'une **mémoire** (bouton Mémoires, liste Mem du clavier de fréquence), le **scan** (fréquences ou mémoires) et les **boutons de mode** (USB, LSB, AM, FM, DIGI, CW) ne règlent que le WebSDR : la fréquence et le mode de l'émetteur ne changent pas. Les boutons de mode s'allument d'après le mode du WebSDR, et c'est la fréquence du WebSDR qui est reprise par **Save**, « Mémoriser la fréquence actuelle », la liste Mem et la reprise du scan. La molette sur la fréquence RADIO règle toujours le poste seul.

## Bouton PTT

Le gros bouton **PTT**, à gauche de « RADIO », fait passer le poste en émission par CAT : un clic pour émettre (le bouton devient rouge « ● TX »), un second clic pour revenir en réception. C'est l'équivalent du bouton MOX de HRD.
- Il envoie `TX1;`/`TX0;` aux Yaesu, `TX;`/`RX;` aux Kenwood et Elecraft, `1C 00` aux Icom, `trx` en TCI.
- Avec OmniRig, il agit sur la propriété `Tx` ; avec HRD, sur le bouton MOX.
- Le son des réceptions choisies (le WebSDR, et selon le clic droit le poste, ExpertSDR, HDSDR) est coupé dès le clic, même si le poste ne signale pas son état d'émission (si « Couper le son du WebSDR en émission » est cochée).
- Par sécurité, si on se déconnecte ou si on ferme SDRLink pendant une émission lancée par ce bouton, le poste repasse en réception.
- **Délai de sécurité TX** (clic droit sur PTT) : Désactivé, 30 s, 1, 2, 3, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50 ou 55 min.
  - Pendant l'émission, le bouton affiche le temps restant (« ● TX 1:45 »).
  - À l'échéance, SDRLink repasse en réception et l'indique en bas de la fenêtre.
  - Ce délai concerne l'émission lancée par le bouton PTT de SDRLink, pas la pédale ni le micro.
- **Micro du PC vers le poste** (depuis la 1.51, clic droit sur PTT) : le micro du PC est envoyé sur la **carte son d'émission du poste**, sans fenêtre et sans rien rejouer sur le PC.
  - **Micro du PC** : le micro à prendre (entrées de Windows). **Carte son d'émission du poste** : la sortie du PC reliée à l'entrée modulation / USB du poste, **la même que celle de Tune** (la changer d'un côté la change de l'autre). La case **Micro du PC vers le poste (« micro » → « carte »)** met l'envoi en marche ou l'arrête ; elle n'est pas cochée au départ, et le choix est gardé après redémarrage.
  - Le micro reste disponible pour les autres logiciels : Windows le partage, SDRLink ne le prend pas pour lui seul.
  - Le son part en continu, tel quel (sans notch ni DNR), avec environ 20 à 40 ms de retard. Le poste ne l'émet qu'en émission (PTT de SDRLink, pédale, PTT du micro, VOX), et seulement si sa **source de modulation** est l'USB / l'arrière (DATA, « REAR »). Avec le VOX du poste réglé sur l'USB, le bruit de la pièce peut le déclencher.
  - Pendant **Tune**, le micro se tait : seule la tonalité part.
  - Le niveau se règle dans Windows (niveau du micro) et sur le poste (gain de l'entrée USB / DATA).
- **Émission sans modulation** (constaté sur un FTdx10) : le PTT par CAT met bien le poste en émission, mais le son émis dépend de la **source de modulation** du poste. Si elle est réglée sur l'arrière ou l'USB (« REAR », souvent le cas pour le FT8), le micro n'est pas transmis. Sur le FTdx10, vérifier dans les menus de réglage de l'émission (TX AUDIO) la source de modulation SSB (MOD SOURCE : MIC) et le gain micro. Les autres logiciels qui passent en émission par CAT, comme WSJT-X, rencontrent le même comportement.

## Bouton TUNE (depuis la 1.49)

Le bouton **TUNE**, au bout de la ligne du PTT (après les cases Avant plan / Affichage ptt), de même forme et aligné sur lui, émet une **tonalité de 1500 Hz** pour régler une boîte d'accord ou un amplificateur.
- **Clic** : passage en émission par CAT (comme PTT) et tonalité envoyée sur la **carte son d'émission du poste** ; le bouton devient rouge et compte les secondes (« ● TUNE 8 »). Second clic, clic sur PTT, ou fin de la durée : la tonalité s'arrête (sans claquement) et le poste repasse en réception. Si le PTT était déjà en émission avant Tune, il y reste. Avec **VOX du poste** (clic droit), SDRLink n'envoie aucun PTT : seule la tonalité part, et le VOX du poste passe en émission (aucune liaison CAT nécessaire).
- **Clic droit** : la **carte son d'émission du poste** (sortie du PC reliée à l'entrée modulation / USB du poste ; la même que pour le micro du PC vers le poste, depuis la 1.51), le **niveau de la tonalité** (petite fenêtre toujours devant, à gauche du menu de Tune qui reste ouvert, OK ramène au menu : curseur et case de 1 à 100 %, par pas de 1 : molette, flèches ou valeur tapée ; pris tout de suite, même pendant l'émission ; 5 % au départ), la **durée** (5, 10, 15, 20, 30, 45 s ou 1 min ; 10 s au départ) et les couleurs.
- Le poste doit prendre sa modulation sur l'USB / l'arrière (DATA, « REAR ») pour émettre la tonalité ; régler la puissance sur le poste avant d'accorder.

## Coupure du son en émission

Coche **« Couper le son du WebSDR en émission »** (activée par défaut) : pendant que le poste émet, le son des réceptions est coupé pour ne pas s'entendre. L'indication **● TX** s'affiche alors en rouge à côté de « RADIO ». Décocher pour garder le son en émission.

**Réceptions concernées** (depuis la 1.19) : **clic droit** sur la case pour cocher celles dont le son est coupé en émission. Toutes sont cochées au départ :
- **WebSDR** : son de la page ;
- **Poste (Mute TX)** : gain BF du transceiver mis à zéro par CAT, si le volume du poste est télécommandé (comme Mute TX) ;
- **ExpertSDR** : par le mélangeur de Windows, sinon par la liaison TCI (comme Mute ExpertSDR) ;
- **HDSDR** : par le mélangeur de Windows (comme Mute HDSDR).

Le menu reste ouvert pour cocher ou décocher plusieurs réceptions ; le choix est conservé. Une réception absente (logiciel fermé, volume du poste non télécommandé) est simplement ignorée. Pendant l'émission, les boutons Mute des réceptions coupées passent au rouge. Une réception **déjà coupée à la main** avant l'émission n'est pas touchée : elle reste coupée après. Décocher une réception (ou la case) pendant l'émission rétablit son son tout de suite. À la fermeture de SDRLink, tout ce qu'il a coupé est rétabli.

**« rétablir après (ms) »** (1000 par défaut) : le son d'un WebSDR arrive avec 0,5 à 2 s de retard, il contient donc encore la fin de ton émission après le relâchement du PTT. Ce délai vaut pour **toutes** les réceptions coupées : elles sont rétablies ensemble à la fin du délai. Augmente cette valeur si tu entends la fin de tes passages, mets 0 pour rétablir le son tout de suite.

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

Bouton **Notch auto (DNF)**, au bout de la ligne Synchro : comme le DNF du FT-2000, il supprime automatiquement les **porteuses et sifflements stables** du son du WebSDR.
- **Clic** sur le bouton : marche / arrêt du notch (depuis la 1.48 ; avant, une case dans le bouton). En marche, le bouton est **vert** ; arrêté, il reprend la couleur des autres boutons. Le choix est mémorisé.
- **Clic droit** sur le bouton : la fenêtre du notch (depuis la 1.48 ; avant, elle s'ouvrait au clic sur le bouton) :
  - **Notch auto désactivé en mode numérique** (coché au départ) : le notch est mis en pause, sans arrêter le bouton, quand un décodeur des modes numériques est en marche ou que **DIGI** est le mode ;
  - **Notch auto désactivé en FreeDV V1 et V2** (coché au départ) : coupé tant que FreeDV auto est en marche ;
  - **Notch aussi sur le son du poste joué par SDRLink (écoute sur le PC, diversité)** (coché au départ, depuis la 1.50) : voir plus bas ;
  - **Type de notch** (depuis la 1.50) : **d'origine**, **adaptatif (NLMS)** ou **spectral (FFT)**, voir plus bas. Choisir un type met le notch en marche ;
  - **Niveau du notch** de **1** à **10**, propre au type choisi (chaque type garde le sien ; 5 au départ pour l'adaptatif et le spectral) ;
  - l'explication, **Couleurs du bouton…** (notch arrêté / en marche), puis **Fermer** (ou un clic ailleurs, ou Échap). Tout s'applique tout de suite et est retenu.
- **Les trois types** (depuis la 1.50 ; avant, seul le type d'origine existait) :
  - **D'origine** : celui des versions d'avant. Il repère les raies fixes toutes les 100 ms et pose sur chacune un filtre étroit qui la suit si elle glisse. Niveau **1** (le réglage d'origine) à **10** : le nombre de sifflements traités passe de 4 à 8, ils sont repérés plus vite et plus bas au-dessus du bruit, et chaque coupure est plus large. Au niveau 1 : jusqu'à 4 raies à la fois, chacune repérée en environ 0,6 s ; atténuation mesurée d'environ 48 dB, jusqu'au niveau du bruit.
  - **Adaptatif (NLMS)** : un filtre qui apprend tout ce qui **se répète** dans le son et le retire, comme le notch automatique des anciens postes. Il traite autant de sifflements qu'il y en a, même s'ils **glissent**. Le niveau règle sa vitesse d'apprentissage : un niveau élevé suit plus vite mais peut entamer la voix.
  - **Spectral (FFT)** : le son est découpé en fréquences ; chaque fréquence qui dépasse **nettement ses voisines pendant un moment** est ramenée à leur niveau. Le plus propre : il ne touche que les raies stables. Le niveau règle le seuil et la force.
- **Mesures** (voix de synthèse + sifflement fixe à 2300 Hz, niveau 5) : sifflement baissé d'environ 50 dB (d'origine), 32 dB (adaptatif) et 29 dB (spectral). Cette voix de synthèse, très régulière, est un cas défavorable : elle perd 3 à 9 dB selon le type. Avec une voix dont la hauteur bouge un peu, au niveau 1, le type d'origine ne la touche pas, le spectral lui retire 0,5 dB et l'adaptatif 3 dB. Conseil : commencer par un niveau bas, monter seulement si un sifflement résiste.
- **Retard** : aucun pour le type d'origine. Sur la page du WebSDR, l'adaptatif et le spectral passent par le même traitement du son que le DNR : retard de 0 à environ 0,1 s selon que le WebSDR est en https ou en http, plus environ 43 ms pour le spectral (son découpage).
- La parole bouge trop pour être retenue (un niveau élevé peut toutefois toucher une voix qui tient une note). Une **CW** stable est en revanche supprimée, comme avec le DNF du poste : l'arrêter en CW.
- Seul le son entendu est filtré, le câble SDRLINK et les décodeurs reçoivent le son brut. Le notch passe **avant** le DNR quand les deux marchent.
- **Son du poste** (depuis la 1.50) : comme pour le DNR, quand SDRLink joue lui-même le son du poste (**écoute du poste sur le PC**, **oreille du poste en diversité**), le même notch s'y applique : même marche / arrêt, même type, même niveau et mêmes coupures en mode numérique et en FreeDV que pour le WebSDR, si la case **Notch aussi sur le son du poste joué par SDRLink** est cochée. Retard ajouté : aucun pour le type d'origine et l'adaptatif, environ 43 ms pour le spectral. Le haut-parleur du poste, REC, le câble SDRLINK et les décodeurs ne sont pas touchés.

## Réducteur de bruit (DNR) (depuis la 1.46 ; trois types depuis la 1.48)

Bouton **DNR**, juste après **Notch auto (DNF)** : il réduit le **souffle** (bruit de fond) du son du WebSDR, comme le DNR d'un poste.
- **Clic** : marche / arrêt. En marche, le bouton est **vert** et affiche le type et le niveau : « DNR 3 » (classique), « DNR+ 5 » (amélioré), « DNR IA 5 » (IA). Arrêté, il reprend la couleur des autres boutons. Le choix est retenu.
- **Clic droit** :
  - les **trois types** de DNR, celui en service coché. **Clic** sur un type : il est choisi (le menu reste ouvert). **Clic droit** sur un type, ou simple survol : son sous-menu de **niveaux**, de **1** (le plus doux) à **10** (le plus fort). Chaque type garde son propre niveau. Choisir un type ou un niveau met le DNR en marche ;
  - **DNR coupé en mode numérique** (coché au départ) : en pause, sans éteindre le bouton, quand un décodeur des modes numériques est en marche ou que **DIGI** est le mode. Vaut pour les trois types ;
  - **DNR coupé en FreeDV V1 et V2** (coché au départ) : en pause tant que FreeDV auto est en marche. Vaut pour les trois types ;
  - **DNR aussi sur le son du poste joué par SDRLink (écoute sur le PC, diversité)** (coché au départ, depuis la 1.48) : voir plus bas ;
  - **Couleur DNR arrêté** et **Couleur DNR en marche**.
- **Les trois types** :
  - **Classique (filtre de Wiener)**, celui de la 1.46 (niveau 3 au départ). Le son est découpé en tranches de 20 à 30 ms ; pour chaque fréquence audio, SDRLink suit le niveau du bruit (le plus bas entendu sur environ 1,6 s) et atténue ce qui ne le dépasse pas. Efficace sur un souffle régulier, mais aux niveaux forts il « mange » aussi la voix, qui devient métallique ou sous l'eau.
  - **Amélioré (MMSE-LSA)** (niveau 5 au départ). Même découpage, mais le bruit est estimé d'après la **probabilité que la voix soit présente** dans chaque fréquence (il suit plus vite un bruit qui change), et l'atténuation est calculée sur l'échelle de l'oreille (méthode d'Ephraim et Malah), sans sur-estimation du bruit. Résultat : à réduction égale, la **voix est beaucoup moins déformée**, et monter le niveau baisse le bruit sans abîmer davantage la voix. Calcul aussi léger que le classique.
  - **IA (RNNoise)** (niveau 5 au départ). Un **réseau de neurones** entraîné à séparer la voix du bruit (RNNoise, de Jean-Marc Valin, Xiph.Org et Mozilla, licence BSD), intégré à SDRLink : **aucun site externe**, rien n'est envoyé sur Internet. Il « reconnaît » la voix et peut enlever le bruit presque entièrement entre les mots. Le niveau fixe le plancher d'atténuation (de -6 dB au niveau 1 à environ -34 dB au niveau 9) ; au niveau **10**, RNNoise travaille sans plancher, le silence entre les mots devient total. Il a été entraîné sur la **parole** : il convient à la BLU et à l'AM, pas à la CW ni aux modes numériques (d'où les coupures ci-dessus).
- **Mesures** (voix réelle filtrée comme en BLU, avec un souffle fort, son à 12 kHz) : bruit baissé de 9 dB (niveau 3) à 16 dB (niveau 10) en classique, la voix se dégradant avec le niveau ; de 8 à 23 dB en amélioré et de 6 dB à près de 50 dB en IA, la voix restant aussi nette à tous les niveaux.
- **Ressources** : classique et amélioré, moins de 1 % d'un cœur du processeur. **IA** : environ **4 à 5 % d'un cœur** pour un WebSDR (le double en diversité sur deux WebSDR), et une centaine de Ko de plus dans l'exe. Le réseau n'est chargé que si on choisit ce type.
- **Retard** : le son du WebSDR prend environ **20 à 30 ms** de retard de plus sur un WebSDR en **https**, quel que soit le type, et un peu plus (jusqu'à environ 0,1 s) sur un WebSDR en **http**, le cas de la plupart des websdr.org et KiwiSDR. Sur ces pages, le navigateur n'offre que l'ancien traitement du son, qui travaille par blocs plus longs.
- Il agit **avant le volume**, après le notch automatique quand les deux marchent. Comme le notch, il ne touche **que le son entendu** : le câble SDRLINK et les décodeurs reçoivent le son brut. En diversité, il s'applique aussi à l'oreille du WebSDR, et aux deux oreilles en diversité sur deux WebSDR.
- **Son du poste** (depuis la 1.48) : quand SDRLink joue lui-même le son du poste, c'est-à-dire l'**écoute du poste sur le PC** (clic droit sur Mute TX, curseur Volume TX sur PC) et l'**oreille du poste en diversité** (source « poste (carte son rec) »), le même DNR s'y applique : même marche / arrêt, même type, même niveau et mêmes coupures en mode numérique et en FreeDV que pour le WebSDR. Il suffit de laisser cochée la case **DNR aussi sur le son du poste joué par SDRLink**. Le son passe en mono (moyenne des deux voies) dans le DNR, puis dans les deux oreilles. Retard ajouté : environ 20 à 30 ms ; l'IA y prend environ 3 à 6 % d'un cœur en plus. Le son entendu **sur le haut-parleur du poste** ne passe pas par l'ordinateur : le DNR ne peut pas le traiter. REC, le câble SDRLINK et les décodeurs gardent le son brut.
- Il ne touche pas le son d'ExpertSDR ni de HDSDR (ils ont leur propre DNR).

Les boutons gardent toujours la même place : les zones RADIO et WEBSDR ont une largeur fixe. Le mode du WebSDR y est affiché en forme courte (USB, LSB, CW, AM, FM). L'émission est signalée par la fréquence RADIO en rouge et par le bouton PTT. La FM devient NBFM sur KiwiSDR et NFM sur UberSDR et OpenWebRX.

## Saisie d'une fréquence

- Taper une fréquence en **kHz** dans le champ « kHz » (ex. `7074`, `14074,5` ou `14074.5`), puis **Entrée** ou **QSY** : le poste et le WebSDR s'y accordent.
- Le menu déroulant garde les **100 dernières** fréquences, la plus récente en haut. Choisir une valeur dans la liste y accorde directement.
- **Save** ajoute la fréquence en cours à la liste.
- **Clic droit** sur le champ : « Supprimer … de la liste » (la valeur affichée) ou « Tout supprimer » (une confirmation est demandée).
- La liste est conservée au prochain lancement.

## Clavier de fréquence et liste Mem (depuis la 1.26)

Le même clavier et la même liste « Mem » que dans PhoneSDRLink.

- **Clic gauche sur la fréquence RADIO ou WEBSDR** : le clavier s'ouvre sous la fréquence cliquée, avec cette fréquence en kHz en grands chiffres et, en dessous, la valeur complète (`= 14.074.000`).
  - Touches **7 8 9 / 4 5 6 / 1 2 3 / . 0 ⌫**, dans des cases arrondies. La première touche remplace la valeur affichée ; un seul point décimal ; 11 caractères au plus.
  - **⌫** efface le dernier chiffre ; **appui long sur ⌫** : tout effacer.
  - Le clavier du PC marche aussi : chiffres (rangée du haut ou pavé numérique), point ou virgule, **Retour arrière**, **Suppr** (tout effacer), **Entrée** (OK), **Échap** (Annuler).
  - Fréquences acceptées : de 1 kHz à 100 GHz. Une valeur hors de cette plage est marquée ✗ ; OK est alors refusé avec « Fréquence invalide » et le clavier reste ouvert.
  - **OK** : même règle que la molette (voir « Molette sur les fréquences »). En Bidirectionnelle, le poste et le WebSDR vont sur la fréquence tapée. Sinon, selon le sens de synchronisation, seule la fréquence cliquée change. La fréquence rejoint aussi l'historique du champ « kHz ». Un scan en cours s'arrête.
  - **Annuler** : ne change rien.
  - Pendant que le clavier est ouvert, la molette sur RADIO / WEBSDR ne change pas la fréquence.
- Sous les touches : **Mem ▾**, **Store** et **Auto N min**.
  - **Store** met tout de suite en tête de la liste Mem : la fréquence actuelle (celle du poste, sinon celle du WebSDR), le mode, et le mode numérique s'il est en marche (FT8, SSTV, CW…). FreeDV auto n'est pas retenu. Si la même ligne est déjà en tête, seule son heure est mise à jour ; un doublon plus bas est retiré.
  - **Auto** : enregistrement automatique dans la liste Mem après **0** (jamais, par défaut), **1, 2, 3, 5, 10, 15, 20 ou 30 minutes**. La fréquence n'est enregistrée que si la fréquence, le mode et le mode numérique n'ont pas changé pendant ce temps. Tout changement remet le compteur à zéro. Ensuite, elle est enregistrée de nouveau à chaque échéance (la ligne déjà en tête prend la nouvelle heure).
- **Mem ▾** ouvre la liste **Mem**, à droite du clavier sans le recouvrir. S'il n'y a pas la place à droite, elle s'ouvre à gauche.
  - Les **20 dernières** fréquences, la plus récente en haut, chacune dans une case arrondie : fréquence, mode, mode numérique, heure et date (`14074 kHz  USB  FT8  10:26 8/11/2026`).
  - Un **ascenseur** apparaît si la liste ne tient pas à l'écran ; la molette fait défiler.
  - **Clic sur une ligne** : la liste et le clavier se ferment. Le poste et le WebSDR vont sur la fréquence et le mode de la ligne, et le mode numérique de la ligne se met en marche.
  - **Clic droit ou appui long sur une ligne** :
    - **Mémoriser dans « Mémoires »** ▸ liste choisie : copie fréquence et mode dans les mémoires (sans le mode numérique ni l'heure) ;
    - **Supprimer de la liste « Mem »** : supprime la ligne, après confirmation.
  - Liste vide : « Store » ou l'enregistrement automatique l'alimentent.
- La liste et le réglage Auto sont gardés après redémarrage (`settings.ini`, lignes `MemHist=` et `MemHistMin=`). Pour Import/Export et Réinitialiser le programme, la liste Mem fait partie des « mémoires ».

## Molette sur les fréquences

Laisser la souris sur **RADIO** ou **WEBSDR** et tourner la molette. Ce qui bouge dépend de la synchronisation :

| Synchro | Molette sur RADIO | Molette sur WEBSDR |
|---|---|---|
| Bidirectionnelle | radio et WebSDR | radio et WebSDR |
| Radio → WebSDR | radio et WebSDR | WebSDR seul |
| WebSDR → Radio | radio seule | radio et WebSDR |
| Désactivée | radio seule | WebSDR seul |

- Le **pas** se choisit dans la liste « Pas » de la même ligne : 10 Hz, 25 Hz, 50 Hz (depuis la 1.38), 100 Hz, 250 Hz, 500 Hz, 1 kHz, 2,5 kHz, 5 kHz ou 10 kHz. Depuis la 1.38, il est **retenu pour chaque mode** (CW, USB/LSB, AM, FM, DIGI), même après redémarrage ; au départ 10 Hz en CW, 1 kHz en AM et FM, 500 Hz pour les autres.

## Bande passante de réception (depuis la 1.38)

Liste **BP**, après « Pas » : largeur du filtre de réception du WebSDR, **retenue pour chaque mode** (CW, USB/LSB, AM, FM, DIGI), **sur tous les WebSDR** et **après redémarrage**, comme dans PhoneSDRLink.
- Choix proposés : CW 100 à 800 Hz ; USB/LSB 2,1 à 5 kHz ; AM 6, 8, 10 kHz ; FM 8 à 15 kHz ; DIGI 200 Hz à 4 kHz ; plus **Filtre d'origine** (celui du WebSDR) et **Autre…** (valeur en kHz, de 0,1 à 15).
- Au départ : CW 300 Hz, USB/LSB 2,9 kHz, AM 6 kHz, FM 8 kHz, DIGI filtre d'origine.
- En USB et LSB, le filtre commence à 300 Hz de la porteuse, du côté du mode ; en CW, AM et FM, il reste centré.
- La largeur est remise quand le mode change (bouton de mode, poste, page), quand on change de WebSDR et quand la page est rechargée. Essayé sur KiwiSDR et websdr.org ; OpenWebRX, UberSDR et PhantomSDR sont pris en charge.
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
- **Affichage ptt** (sous « Avant plan ») : toute petite fenêtre qui ne montre que le **bouton PTT**, la case « Affichage ptt » et, dessous, la case **Avant plan** (depuis la 1.21 : on peut mettre la petite fenêtre au premier plan sans revenir en grand ; c'est la même case que dans la grande fenêtre). Le PTT y fonctionne normalement (clic, clic droit pour le délai de sécurité). **Décocher** la case revient à l'affichage d'avant **strictement identique** : même affichage (normal, réduit ou mini), même taille, même position, plein écran compris. Fermer SDRLink en affichage ptt enregistre l'affichage d'avant.
- **Avant plan** : SDRLink reste au-dessus des autres fenêtres (pratique avec l'affichage ptt).
- **Taille du texte…** (1re ligne) : agrandit ou réduit le texte de la fenêtre principale, de 70 à 200 % (100 % = taille d'origine), avec le curseur ou les boutons − / +. Le changement se voit tout de suite ; **Annuler** revient à la taille d'avant, **100 %** à la taille d'origine. Les boutons et curseurs de taille fixe suivent. La taille est conservée au prochain lancement.

Ces deux réglages sont conservés au prochain lancement.

## Scan

Sur la ligne « Synchro », après « Notch auto (DNF) » :
- **Conf scan** : choix du type de scan.
  - **Fréquences** : fréquence de **début** et de **fin** en kHz (dans n'importe quel ordre), **pas** (100 Hz à 100 kHz), **durée** sur chaque fréquence : 1 s, 2 s, 5 s, 10 s, 30 s, 1 min, 2 min, 5 min ou 10 min. Arrivé à la fin, le scan repart du début.
  - **Zones non scannées** (jusqu'à **10**) : bouton **+ Zone non scannée**, puis « de … à … kHz ». Les fréquences entre les deux bornes sont sautées ; les bornes elles-mêmes sont scannées. Exemple : début 7040, fin 7200, zone de 7096 à 7103 : avec un pas de 1 kHz, le scan s'arrête sur 7096 puis repart à 7103. Les zones n'apparaissent que si elles sont définies ; **✕** en retire une ; une ligne laissée vide est ignorée. Si toutes les fréquences sont dans des zones, le scan refuse de démarrer.
  - **Mémoires** : liste des mémoires avec une **case à cocher** pour chacune (Tout cocher / Tout décocher), et la **durée** sur chaque mémoire (mêmes choix). Le mode mémorisé est appliqué.
  - Chaque type garde ses réglages : en repassant de Mémoires à Fréquences (ou l'inverse), les choix précédents sont toujours là. **Réinitialiser** remet à zéro le type affiché (l'autre garde les siens, les zones du scan de fréquences sont effacées).
  - **OK** enregistre ; **Lancer le scan** enregistre et démarre.
- **Profils de scan** (jusqu'à **10**, en haut de Conf scan) : un profil garde tout le réglage affiché (type, début, fin, pas, durées, zones non scannées, mémoires cochées).
  - **Enregistrer** : taper un nom dans la case **Profil**, puis Enregistrer. Un nom déjà pris propose de remplacer ce profil.
  - **Choisir un profil** dans la liste : ses réglages s'affichent (OK ou Lancer le scan les prend).
  - **Renommer** : choisir le profil, changer son nom dans la case, puis Renommer. **Supprimer** : choisir le profil, puis Supprimer (après confirmation).
  - Les profils sont gardés même si l'on ferme Conf scan par Annuler.
- **▶ Scan** : démarre le scan ; le bouton devient rouge « ■ Arrêter le scan ». La barre d'état indique la fréquence ou la mémoire en cours (sans remplir l'historique des messages).
  - Un clic ouvre une liste (depuis la 1.21, même sans profil) : **Reprendre le scan**, **Réglage actuel de « Conf scan »** (en gras), puis chaque profil avec son résumé (par ex. « 40 m CW — 7040 – 7200 kHz, 1 kHz, 5 s, zones non scannées : 1 »). Le profil choisi devient le réglage de Conf scan et le scan démarre.
  - **Reprendre le scan** (depuis la 1.21) : le scan du réglage de Conf scan repart de la **fréquence actuelle** au lieu du début. La liste indique d'où il repartira (par ex. « Reprendre le scan — à 7074 kHz »).
    - Scan de fréquences : si la fréquence actuelle est entre le début et la fin, le scan repart d'elle, puis continue au pas réglé (de 7074,3 il passe à 7075 avec un pas de 1 kHz) ; si elle est dans une zone non scannée, il repart à la fin de la zone. **Hors de la plage**, il repart de la **première fréquence** du scan (« au début (7000 kHz) : fréquence actuelle hors de la plage »).
    - Scan de mémoires : il repart de la mémoire cochée qui a la fréquence actuelle, sinon de la première mémoire.
  - **Réglage actuel de « Conf scan »** : le scan repart toujours du début.
- Le scan règle le poste et le WebSDR, comme une saisie de fréquence. Il s'arrête dès que le poste passe en **émission** (PTT de SDRLink ou du poste) ; il ne peut pas démarrer en émission.
- **Clic dans la page pendant un scan** (depuis la 1.22) : un clic n'importe où dans la page du WebSDR (cascade, boutons de la page…) **arrête le scan** sur la fréquence (ou la mémoire) en cours. Si le clic a déplacé la fréquence de la page (clic dans la cascade), elle est **remise aussitôt** sur la fréquence scannée, et le poste n'en bouge pas. La barre d'état indique « Scan arrêté : clic dans la page, arrêt sur 7001 kHz ». Le clic suivant règle normalement la fréquence. Les boutons et réglages de SDRLink (volumes…) n'arrêtent pas le scan ; le bouton ■ Arrêter le scan l'arrête comme avant.

## Enregistrement (REC)

- Bouton **REC**, sur la ligne de son, juste après le dernier volume affiché (il se rapproche quand Volume TX, Volume ExpertSDR ou Volume HDSDR sont absents), de la même taille que **Mute WebSDR** (comme **Mute TX**, depuis la 1.37) : **bleu marine** au repos (couleur réglable), **orange** pendant l'enregistrement.
- Un clic lance l'enregistrement de **tout ce qu'on entend dans les haut-parleurs** (sortie audio par défaut de Windows) : WebSDR, ExpertSDR, poste relié au PC, sons de Windows… Le volume de Windows et les Mute ne changent rien à ce qui est capté avant eux : on enregistre ce que la sortie joue.
- Un nouveau clic arrête et ouvre la fenêtre **Enregistrer le son** :
  - **Nom** : proposé d'après la date, l'heure, la fréquence et le mode au départ (modifiable) ;
  - **Dossier** : par défaut Musique\SDRLink, **Parcourir…** pour en choisir un autre (le dernier dossier choisi est retenu) ;
  - **Format** : **MP3** (par défaut, 128 kbit/s, codeur de Windows), **WAV** (sans perte, volumineux) ou **FLAC** (sans perte, environ deux fois plus petit) ;
  - **Supprimer** abandonne l'enregistrement (après confirmation).
- Le fichier est écrit en arrière-plan ; la barre d'état indique la progression puis « Enregistrement sauvegardé ». En cas d'échec, la fenêtre revient : le son n'est pas perdu.
- **Clic droit sur REC** : choix de la **carte son du poste** (son entrée dans Windows, par exemple « USB AUDIO CODEC » ou « USB Sound Device »), ou « Aucune (toujours les haut-parleurs) ». Cette carte est enregistrée **à la place des haut-parleurs** seulement si l'on écoute le poste seul au moment d'appuyer sur REC : **Mute WebSDR** actif (rouge), **Mute ExpertSDR** actif (rouge) si ExpertSDR est lancé et **Mute HDSDR** actif (rouge) si HDSDR joue. Sinon, ce sont les haut-parleurs. Si la carte est débranchée, les haut-parleurs sont enregistrés et la barre d'état le signale. Le choix est conservé.
- **Clic droit sur REC > Couleur du bouton REC** : couleur du bouton au repos (bleu marine par défaut, bleu, bleu ciel, bleu canard, vert, rouge, bordeaux, violet, rose, marron, gris, noir, jaune, ou « Autre couleur… » au choix). Le texte passe en noir sur les couleurs claires ; pendant l'enregistrement, le bouton reste orange. Le choix est conservé.
- Les silences sont gardés (la durée du fichier est la durée réelle). Fermer SDRLink pendant un enregistrement propose de le sauvegarder avant de quitter.
## Réinit WebSDR (depuis la 1.31)

Bouton **Réinit WebSDR**, sur la ligne de son, juste après REC : il relance le WebSDR affiché quand il s'est arrêté, par exemple après son **délai d'inactivité** (la page websdr.org n'affiche plus que « Idle time out. », un KiwiSDR coupe la liaison), ou quand le son ne revient pas.

- **Clic** : relance **en gardant tous les réglages**. La page est rechargée, puis SDRLink remet la **fréquence** et le **mode**, et, sur websdr.org et KiwiSDR, le **filtre** (bande passante) et le **zoom** de la cascade tels qu'ils étaient, même si la page était déjà arrêtée. Le volume, les Mute, le notch et le pseudo de SDRLink sont remis aussi. OpenWebRX et les autres WebSDR gardent eux-mêmes leurs réglages au rechargement. La barre d'état indique « WebSDR relancé en gardant ses réglages », puis « WebSDR relancé : filtre et zoom remis comme avant ».
- **Clic droit** : un menu (depuis la 1.37) :
  - **Auto : relancer le WebSDR quand le son est perdu** (case à cocher) : voir « Réinit Auto WebSDR » ci-dessous ;
  - **Délai sans son avant la relance** : de 10 à 60 s, par pas de 5 s (30 s au départ) ;
  - **Réinitialisation totale du WebSDR (comme une première visite)** : les cookies et les réglages enregistrés par ce site (stockage de la page) sont effacés, et la page est rouverte **comme à la première visite** (sans la partie de l'adresse après « # », où OpenWebRX garde fréquence et mode) ; la fréquence du WebSDR d'avant n'est pas reprise. Le pseudo du bouton Pseudo/Call est redonné. Si la synchronisation va du poste vers le WebSDR, le poste impose toujours sa fréquence. Les réglages de SDRLink (favoris, volume…) et ceux des autres sites ne sont pas touchés. (Jusqu'à la 1.36, le clic droit faisait directement cette réinitialisation.)
  - les **couleurs** du bouton : une pour « Réinit WebSDR » (Auto arrêté, couleur du thème par défaut), une pour « Réinit Auto WebSDR » (Auto en marche, bleu canard par défaut).

### Réinit Auto WebSDR (depuis la 1.37)

Relance automatique du WebSDR quand son son est perdu (coupure du serveur, délai d'inactivité, « session timed out »…), comme la « Réinitialisation automatique WEBSDR » de PhoneSDRLink.
- Cochez **Auto** au clic droit : le bouton devient **Réinit Auto WebSDR** et change de couleur. Il garde **la même largeur** dans les deux états (celle du plus long des deux textes dans la langue choisie).
- Toutes les **10 secondes**, SDRLink regarde depuis combien de temps la page du WebSDR ne produit plus de son. Au-delà du délai choisi, la page est relancée **en gardant ses réglages**, comme un clic sur le bouton ; la barre d'état l'indique (« Plus de son depuis 30 s : WebSDR relancé automatiquement »). La relance a donc lieu entre le délai choisi et 10 s de plus.
- **Pas de relance quand le son est coupé volontairement** : **Mute WebSDR** enclenché, **Volume WebSDR** à 0, parole FreeDV jouée à la place du WebSDR, **curseur de volume de la page** à zéro, ou **bouton « mute » de la page** enfoncé (websdr.org, KiwiSDR…). Quand le son est remis, le décompte repart de zéro.
- Une page dont le son n'a jamais démarré n'est pas relancée.
- **3 essais de suite au plus** : si le son ne revient pas après 3 relances, SDRLink attend. Le compteur repart quand le son revient vraiment, quand on change de WebSDR ou quand on clique sur le bouton.
- En FM avec un squelch fermé, le silence compte comme une perte de son (3 relances au plus).
- Les choix (Auto, délai) sont conservés.

## Écoute en diversité (depuis la 1.40)

Bouton **Diversité**, sur la ligne de son juste après Réinit WebSDR : le **WebSDR dans une oreille**, la **réception locale dans l'autre** (le poste, ExpertSDR ou HDSDR), comme deux récepteurs sur deux antennes éloignées. L'oreille combine les deux : un signal qui s'évanouit d'un côté reste lisible de l'autre (QSB), et le QRM ou le bruit local d'un côté gêne moins. Il faut un casque ou deux haut-parleurs.
- Le son d'un WebSDR arrive toujours **en retard** (0,3 à 5 s selon le serveur). SDRLink **mesure ce retard en continu** en comparant les deux sons (rythme des syllabes, de la manipulation CW, des évanouissements) et **retarde le son local d'autant** : les deux oreilles entendent la même chose au même moment. Si le retard du WebSDR change, il est retrouvé tout seul (en 8 s environ).
- Il faut **un signal entendu des deux côtés** (même fréquence, même mode) pour mesurer le retard. Tant qu'il n'est pas trouvé, le bouton est **orange** et affiche « Diversité … » ; ensuite il est **vert** et affiche le retard appliqué (« Diversité 1,8 s »), aussi donné dans la barre d'état. Sur du bruit seul, rien n'est calé : les deux sons sont joués sans alignement.
- Les deux oreilles sont mises **au même niveau** automatiquement, quel que soit le volume de chaque réception.
- **Clic** : marche / arrêt (un simple bouton depuis la 1.41, sans case ; sa largeur ne change pas). **Clic droit** :
  - **Dans l'autre oreille** : **Poste (carte son de REC)**, **ExpertSDR (son par TCI)**, **HDSDR**. Seules les réceptions disponibles sont proposées : poste connecté avec sa carte son choisie (clic droit sur REC), ExpertSDR lancé, HDSDR lancé **sous Windows 11** ;
  - **WebSDR dans l'oreille droite** (à gauche sinon) ;
  - en marche, l'état : retard mesuré, ou recherche ;
  - les couleurs des trois états : arrêtée, recherche du retard, calée (depuis la 1.41).
- Pendant la diversité, SDRLink **fait taire la sortie habituelle** de chaque réception et joue les deux sons lui-même, sur la sortie son par défaut de Windows :
  - **WebSDR** : la page est mise à volume 0 (le son est pris dans la page avant le volume) ;
  - **poste** : son **gain BF est mis à 0** (le son reçu par la carte son du poste n'en dépend pas) et l'écoute du poste sur le PC est en sourdine. Il faut un poste dont SDRLink règle le volume (Volume TX affiché) ; sinon, baissez son volume à la main. Tourner le bouton de volume du poste règle son oreille, et le haut-parleur est refait taire ;
  - **ExpertSDR** : son son arrive par le **flux audio TCI** (serveur TCI d'ExpertSDR activé, même port que pour Mute ExpertSDR) ; ExpertSDR est coupé dans le mélangeur de Windows s'il y joue, sinon par TCI. Depuis la 1.45, SDRLink attend qu'ExpertSDR soit prêt (message `ready` du TCI) avant de demander son flux audio : avant, la demande pouvait arriver trop tôt et être ignorée, et l'oreille d'ExpertSDR restait muette. Si, une fois coupé par TCI, ExpertSDR n'envoie toujours aucun son au bout de 4 s, SDRLink lui rend le son et baisse plutôt son **volume au minimum** (−60 dB), puis remonte son oreille ; le volume d'avant est rendu à l'arrêt de la diversité. Le journal (`sdrlink.log`, lignes « Son TCI : » et « Diversité : ») dit ce qu'ExpertSDR a envoyé ;
  - **HDSDR** (Windows 11) : baissé à −60 dB dans le mélangeur ; son son est capté par Windows puis remonté.
- **Mute** et **Volume** de chaque réception règlent **son oreille** (Mute WebSDR fait taire l'oreille du WebSDR, Volume TX celle du poste…), sans rien changer à la réception elle-même.
- **Sauf Mute TX** (depuis la 1.46) : pendant la diversité, le haut-parleur du poste est **déjà muet** (gain BF à 0), et le son pris par la carte son de REC n'en dépend pas. Mute TX **ne coupe donc plus l'oreille du poste** : la diversité continue avec ses deux voies. Pour baisser l'oreille du poste, utiliser **Volume TX**. Mute TX appuyé pendant la diversité s'applique à son arrêt : le poste reste alors muet au lieu de reprendre son volume. Avant la 1.46, Mute TX faisait taire l'oreille du poste et l'on n'entendait plus qu'une voie.
- **En émission** : les oreilles des réceptions cochées au clic droit de « Couper le son du WebSDR en émission » se taisent dès le passage en émission et le restent après, pendant le retard appliqué plus le délai de rétablissement (« rétablir après (ms) ») : la fin de l'émission, jouée en retard, n'est pas entendue. L'oreille du poste se tait en émission dans tous les cas.
- À l'arrêt (nouveau clic, poste déconnecté, logiciel fermé, ou fermeture de SDRLink), **chaque réception retrouve son son** : volume de la page, gain BF du poste, ExpertSDR et HDSDR comme avant.
- Le **notch automatique** s'applique aussi à l'oreille du WebSDR (depuis la 1.41 ; avant, le son était pris avant lui), comme le **DNR** (depuis la 1.46). Si le câble SDRLINK ou un décodeur écoute aussi le WebSDR, le son reste pris avant eux (son brut pour eux).
- La réception choisie et le côté du WebSDR sont conservés ; la diversité, elle, est arrêtée à chaque démarrage.

### Sur deux WebSDR (depuis la 1.44)

Sans poste ni SDR à la maison, ou pour comparer deux stations éloignées : **deux WebSDR**, un dans chaque oreille (diversité d'emplacement).
- **Clic droit sur Diversité** : **Sur deux WebSDR (le second choisi dans la case après Diversité)**. Une **case** apparaît juste après le bouton Diversité, avec la même liste de favoris que la case WebSDR. Choisissez-y le **second WebSDR** (ou tapez son adresse, puis Entrée). Il est retenu.
- **Clic sur Diversité** : le **WebSDR affiché** dans une oreille, le **second** dans l'autre. Le second s'ouvre dans une **page cachée** (il n'est pas affiché) et seulement pendant la diversité ; il se ferme à l'arrêt.
- **Toutes les commandes restent sur le WebSDR affiché** : le second prend tout seul sa **fréquence** et son **mode** (et le notch automatique s'il est en marche), à chaque changement.
- **Volume WebSDR** et **Mute WebSDR** règlent les **deux oreilles** ; « WebSDR dans l'oreille droite » place le WebSDR affiché à droite.
- L'un ou l'autre WebSDR peut arriver en premier : SDRLink retarde celui qui est en avance (le second peut avoir jusqu'à 4 s de retard sur le WebSDR affiché, ou une dizaine de secondes d'avance). Une fois calé, le bouton affiche l'**écart** entre les deux (« Diversité 0,6 s ») et la barre d'état dit lequel arrive avant.
- Il faut un signal **entendu sur les deux WebSDR** pour trouver le retard ; les deux doivent couvrir la fréquence (pour OpenWebRX, le profil de la bande est choisi tout seul).
- Adresse masquée (clic droit sur la page) : la case du second WebSDR passe aussi en étoiles.
- Revenir au poste, à ExpertSDR ou à HDSDR (même clic droit) cache la case ; le second WebSDR choisi est gardé.
- Deux WebSDR à la fois, c'est deux fois plus de données reçues ; un KiwiSDR plein peut refuser la connexion du second.

## Mémoires de fréquences

- Bouton **Mémoires** (ligne des fréquences, après Save) : ouvre la fenêtre des mémoires.
  - **Mémoriser la fréquence actuelle…** : même fenêtre que l'ajout d'un favori, avec **Nom** (pseudo, facultatif), **Fréquence** en kHz, **Mode** (ou « mode inchangé »), **Liste**, **Préampli** et **Puissance** (voir plus bas).
  - **Cliquer sur une mémoire** change tout de suite la fréquence (poste et WebSDR) et le mode s'il est mémorisé ; la fenêtre reste ouverte pour passer à une autre. Entrée fait de même.
  - **Modifier…**, **Supprimer…** (ou touche Suppr).
  - Listes de mémoires comme pour les favoris : menu **Liste** (ou « Toutes les listes »), clic droit pour renommer, **Nouvelle liste** (10 au maximum), **Supprimer la liste…** (avec ses mémoires, après confirmation).
  - Liste **Modes numériques** (dernière du menu **Liste**, depuis la 1.28, comme dans PhoneSDRLink) : les **fréquences habituelles** du mode numérique choisi, bandes radioamateurs de **1,8 à 440 MHz**, d'après les listes de WSJT-X et JS8Call, les plans de bande IARU et les usages courants (Olivia, Contestia, Hell, stations WEFAX). Elle suit le **mode utilisé** : quand un décodeur est en marche (bouton des modes numériques ou FreeDV auto), la fenêtre s'ouvre directement sur cette liste et ce mode. À droite : le **mode** (FT8, FT4, FT2, WSPR, JT9, JS8, CW, RTTY, PSK, Olivia, Contestia, Hell, SSTV, WEFAX, FreeDV) pour voir les fréquences d'un autre mode, et la **région IARU** (1 Europe-Afrique au départ, 2 Amériques, 3 Asie-Pacifique ; retenue) : les fréquences propres à une région sont marquées (R1), (R2)… Un clic y va, avec le mode de la fréquence (USB, LSB, CW ou FM) ; **Modifier…** choisit son **préampli** (depuis la 1.41) et sa **puissance** (depuis la 1.42) ; seuls ces champs se changent ; gardés pour cette fréquence, lignes `MemDigiPre=` et `MemDigiPwr=` des réglages ; **Copier…** (à la place de Supprimer…) l'ajoute à une de vos listes. Ces fréquences d'usage peuvent changer et ne remplacent pas le plan de bande de chaque pays.
- **Clic droit** sur Mémoires : mémoriser la fréquence actuelle sans ouvrir la fenêtre.
- Bouton **▾** à droite : les **20 dernières mémoires utilisées**, la plus récente en haut. Une mémoire qui a un nom (pseudo) est affichée par son nom, sinon par sa fréquence. Un clic y va. En bas : Mémoires… et Effacer cette liste.
- Les mémoires sont gardées dans les réglages (`settings.ini`, lignes `Mem=`).

### Préampli mémorisé (depuis la 1.41)

- Dans la fenêtre d'une mémoire (Mémoriser la fréquence actuelle…, Modifier…), le choix **Préampli** : **(préampli inchangé)** au départ, ou un réglage du préampli du poste. Au rappel de la mémoire (clic, liste ▾, scan des mémoires), le poste passe sur ce réglage **juste après la fréquence** (la plupart des postes retiennent le préampli par bande). La barre d'état le rappelle (« Mémoire « 40 m » : 7074 kHz, AMP1 »). Colonne **Préampli** dans la liste des mémoires.
- Les réglages proposés sont ceux du **poste synchronisé** : chez Yaesu **IPO** (sans préampli), **AMP1**, **AMP2** ; sur le **FTdx5000**, **IPO1**, **IPO2**, **AMP1**, **AMP2** (IPO2 : préampli contourné, attaque directe du premier mélangeur) ; chez Icom **OFF**, **P.AMP1**, **P.AMP2** ; chez Kenwood et Elecraft **OFF**, **PRE** (PRE 1 / PRE 2 sur le TS-890S, le K3 et le K4). Sans poste connecté : sans préampli, préampli 1, préampli 2.
- IPO chez Yaesu équivaut à « pas de préampli » chez les autres marques. Une mémoire faite pour un autre poste est ramenée au réglage le plus proche : AMP2 devient AMP1 sur un poste à un seul préampli, IPO2 devient IPO (ou OFF) ailleurs que sur le FTdx5000.
- Postes pilotés, réglages proposés et commande CAT exacte de chacun : voir le tableau « Préampli : par interface et par poste » ci-dessous.
- Par les **autres interfaces** :
  - **Hamlib** : `L PREAMP`, d'après la liste de préamplis donnée par rigctld, seulement pour un poste de la liste ci-dessus (IPO2 du FTdx5000 non disponible par Hamlib) ;
  - **OmniRig** : la commande du modèle choisi dans OmniRig est envoyée telle quelle (Icom : adresse CI-V par défaut du modèle) ;
  - **Ham Radio Deluxe** : HRD n'a pas de commande préampli ; SDRLink utilise la liste déroulante ou les boutons de préampli de la fenêtre HRD du poste (IPO, AMP 1, AMP 2, P.AMP… ; sur le **FT-2000**, les boutons **Pre 1** et **Pre 2** : IPO = les deux éteints, AMP1 = Pre 1, AMP2 = Pre 2), trouvés à la connexion et notés dans le journal. Pour aller en AMP1 ou AMP2, seul le bouton voulu est allumé, l'autre n'est **jamais** éteint (HRD peut le laisser affiché allumé ; l'éteindre renverrait le poste en IPO) : AMP1 ↔ AMP2 passe directement, sans IPO entre les deux. Seul le retour en IPO éteint le bouton allumé (correctif repris de WSJT-WWA, vérifié sur le FT-2000). L'état des boutons est lu avant d'écrire, puis relu à +0,5 s, +1,5 s et +3 s et renvoyé si le poste a rechargé sa mémoire de préampli par bande après le changement de fréquence ;
  - **TCI** (ExpertSDR, Thetis…) : le protocole TCI (jusqu'à la version 2.1) ne contient pas de commande de préampli : rien n'est envoyé.
- **Commandes relevées dans HRD 6.8** (tables internes du programme) et identiques aux manuels : FT-2000 « Pre 1 » = `PA01;`, « Pre 2 » = `PA02;`, les deux éteints = `PA00;` (IPO) ; FTdx5000 liste « IPO 1, Amp 1, Amp 2, IPO 2 » = `PA00;` `PA01;` `PA02;` `PA03;` (récepteur B : `PA10;`…`PA13;`) ; Kenwood « Pre » = `PA0;` / `PA1;` ; TS-990 « Pre (MAIN) » = `PA00;` / `PA01;`. Le serveur IP de HRD n'a pas de commande CAT brute : SDRLink passe par ces boutons et listes, reconnus par leur nom ou par leurs choix (IPO, Amp, P.AMP, Pre).
- Si le poste ou l'interface ne permet pas de changer le préampli, la mémoire est rappelée sans lui et la barre d'état le dit.
- Dans `settings.ini`, le préampli est le 6e champ de la ligne `Mem=` (absent = inchangé) ; les versions d'avant la 1.41 l'ignorent.

### Préampli : par interface et par poste

| Interface | Comment le préampli est commandé | Postes concernés |
|---|---|---|
| Port COM (CAT direct) | commande CAT du tableau ci-dessous, envoyée juste après la fréquence | les postes du tableau |
| OmniRig | la même commande, envoyée telle quelle par OmniRig ; le poste est reconnu par le nom choisi dans OmniRig ; Icom : adresse CI-V par défaut du modèle | les postes du tableau |
| Ham Radio Deluxe | boutons ou liste de préampli de la fenêtre HRD du poste (HRD n'accepte pas de commande CAT brute), trouvés à la connexion et notés dans le journal ; FT-2000 : boutons **Pre 1** / **Pre 2** | selon la définition du poste dans HRD |
| Hamlib (rigctld) | `L PREAMP <dB>`, valeurs données par rigctld (1re = préampli 1, 2e = préampli 2) | les postes du tableau, limités aux réglages connus de Hamlib (pas d'IPO2 sur le FTdx5000) |
| TCI (ExpertSDR, Thetis…) | aucune : le protocole TCI n'a pas de commande de préampli | aucun |

Tous les postes de SDRLink (commandes relevées dans les manuels CAT des fabricants et recoupées avec Hamlib, flrig et HRD 6.8 ; « Hamlib » : d'après Hamlib seulement, une commande que le poste ne connaît pas est simplement refusée). Icom : trame CI-V `FE FE <adresse> E0 16 02 <valeur> FD`.

| Marque | Modèle | Réglages proposés et commande CAT | Source |
|---|---|---|---|
| Yaesu | FT-450/450D | IPO `PA00;`, AMP `PA01;` | manuel |
| Yaesu | FT-710 | IPO `PA00;`, AMP1 `PA01;`, AMP2 `PA02;` | Hamlib + flrig |
| Yaesu | FT-891 | IPO `PA00;`, AMP `PA01;` | manuel |
| Yaesu | FT-950 | IPO `PA00;`, AMP1 `PA01;`, AMP2 `PA02;` | manuel |
| Yaesu | FT-991/991A | IPO `PA00;`, AMP1 `PA01;`, AMP2 `PA02;` | manuel |
| Yaesu | FT-2000 | IPO `PA00;`, AMP1 `PA01;`, AMP2 `PA02;` | manuel |
| Yaesu | FTdx10 | IPO `PA00;`, AMP1 `PA01;`, AMP2 `PA02;` | manuel |
| Yaesu | FTdx101D/MP | IPO `PA00;`, AMP1 `PA01;`, AMP2 `PA02;` | manuel |
| Yaesu | FTdx1200 | IPO `PA00;`, AMP1 `PA01;`, AMP2 `PA02;` | Hamlib + flrig |
| Yaesu | FTdx3000 | IPO `PA00;`, AMP1 `PA01;`, AMP2 `PA02;` | manuel |
| Yaesu | FTdx5000 | IPO1 `PA00;`, IPO2 `PA03;`, AMP1 `PA01;`, AMP2 `PA02;` | manuel |
| Yaesu | FTdx9000 | IPO `PA00;`, AMP `PA01;` | manuel |
| Yaesu | FTX-1 | IPO `PA00;`, AMP1 `PA01;`, AMP2 `PA02;` ; 144 MHz `PA10;`/`PA11;`, 430 MHz `PA20;`/`PA21;` | Hamlib |
| Icom | IC-78 | OFF `16 02 00`, P.AMP `16 02 01` | Hamlib |
| Icom | IC-703 | OFF `16 02 00`, P.AMP `16 02 01` | manuel |
| Icom | IC-705 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` (> 60 MHz : P.AMP1 max) | Hamlib |
| Icom | IC-706MKIIG | OFF `16 02 00`, P.AMP `16 02 01` | Hamlib |
| Icom | IC-718 | OFF `16 02 00`, P.AMP `16 02 01` | manuel |
| Icom | IC-746 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | manuel |
| Icom | IC-746PRO | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | manuel |
| Icom | IC-756PRO | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | manuel |
| Icom | IC-756PROII | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | Hamlib |
| Icom | IC-756PROIII | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | Hamlib |
| Icom | IC-905 | OFF `16 02 00`, P.AMP `16 02 01` | manuel |
| Icom | IC-910H | OFF `16 02 00`, P.AMP `16 02 01` | manuel |
| Icom | IC-7000 | OFF `16 02 00`, P.AMP `16 02 01` | manuel |
| Icom | IC-7100 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` (> 60 MHz : P.AMP1 max) | Hamlib |
| Icom | IC-7200 | OFF `16 02 00`, P.AMP `16 02 01` | manuel |
| Icom | IC-7300 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | manuel |
| Icom | IC-7300MK2 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | manuel |
| Icom | IC-7400 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | manuel |
| Icom | IC-7410 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | manuel |
| Icom | IC-7600 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | manuel |
| Icom | IC-7610 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | Hamlib |
| Icom | IC-7700 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | Hamlib |
| Icom | IC-7760 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | Hamlib |
| Icom | IC-7800 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | manuel |
| Icom | IC-7850 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | manuel |
| Icom | IC-7851 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` | manuel |
| Icom | IC-9100 | OFF `16 02 00`, P.AMP1 `16 02 01`, P.AMP2 `16 02 02` (> 60 MHz : P.AMP1 max) | manuel |
| Icom | IC-9700 | OFF `16 02 00`, P.AMP `16 02 01` | manuel |
| Kenwood | TS-480 | OFF `PA0;`, PRE `PA1;` | Hamlib + flrig |
| Kenwood | TS-570 | OFF `PA0;`, PRE `PA1;` | manuel |
| Kenwood | TS-590S/SG | OFF `PA0;`, PRE `PA1;` | manuel |
| Kenwood | TS-890S | OFF `PA0;`, PRE 1 `PA1;`, PRE 2 `PA2;` | manuel |
| Kenwood | TS-990S | OFF `PA00;`, PRE `PA01;` | manuel |
| Kenwood | TS-2000 | OFF `PA0;`, PRE `PA1;` | manuel |
| Kenwood | TS-B2000 | OFF `PA0;`, PRE `PA1;` | comme TS-2000 |
| Elecraft | K2 | OFF `PA0;`, PRE `PA1;` | manuel |
| Elecraft | K3/K3S | OFF `PA0;`, PRE `PA1;`, PRE 2 `PA2;` | manuel |
| Elecraft | K4 | OFF `PA00;`, PRE 10 dB `PA11;`, PRE 2 `PA21;` | manuel |
| Elecraft | KX2 | OFF `PA0;`, PRE `PA1;` | manuel |
| Elecraft | KX3 | OFF `PA0;`, PRE `PA1;` | manuel |
| Xiegu | G90 | OFF `16 02 00`, P.AMP `16 02 01` | Hamlib |
| Xiegu | X108G | OFF `16 02 00`, P.AMP `16 02 01` | Hamlib |
| Xiegu | X5105 | OFF `16 02 00`, P.AMP `16 02 01` | Hamlib |
| Xiegu | X6100 | OFF `16 02 00`, P.AMP `16 02 01` | Hamlib |
| Xiegu | X6200 | OFF `16 02 00`, P.AMP `16 02 01` | Hamlib |

Sans commande CAT de préampli (le préampli s'y règle à la main) :
- **Yaesu** : FT-100/100D, FT-600, FT-650, FT-736R, FT-747GX, FT-757GX, FT-757GXII, FT-767GX, FT-817, FT-817ND, FT-818, FT-818ND, FT-840, FT-847, FT-857, FT-857D, FT-890, FT-897, FT-897D, FT-900, FT-920, FT-990, FT-1000, FT-1000D, FT-1000MP, FT-1000MP Mark-V, FT-1000MP Mark-V Field
- **Icom** : IC-271, IC-275, IC-375, IC-471, IC-475, IC-575, IC-706, IC-706MKII, IC-707, IC-725, IC-726, IC-728, IC-729, IC-735, IC-736, IC-737, IC-738, IC-751/751A, IC-756, IC-761, IC-765, IC-775, IC-781, IC-820H, IC-821H, IC-970, IC-1275
- **Kenwood** : TS-50S, TS-60S, TS-140S, TS-440S, TS-450S, TS-680S, TS-690S, TS-711, TS-790, TS-811, TS-850S, TS-870S, TS-930S, TS-940S, TS-950S, TS-950SDX

### Puissance mémorisée (depuis la 1.42)

- Dans la fenêtre d'une mémoire, ligne **Puissance** : cochez **régler à** et indiquez les **watts**. Sans coche (départ), la puissance du poste n'est **pas touchée**. Au rappel de la mémoire (clic, liste ▾, scan des mémoires), le poste reçoit cette puissance **juste après la fréquence**. La barre d'état le rappelle (« Mémoire « 40 m » : 7074 kHz, 50 W »). Colonne **Puissance** dans la liste des mémoires.
- Aussi pour les fréquences de la liste **Modes numériques** : **Modifier…** y règle le préampli et la puissance (ligne `MemDigiPwr=` des réglages), repris quand le choix d'un mode numérique accorde sur sa fréquence. Pratique pour, par exemple, 30 W en FT8 et 100 W en SSB.
- Utile sur les postes qui ne retiennent qu'une puissance par **catégorie** (le FT-891 n'en a que six : HF SSB, HF AM, HF autres modes, et les mêmes en 50 MHz) : la mémoire donne la puissance voulue **bande par bande**.
- **Précautions** (une puissance trop forte peut abîmer un amplificateur ou une antenne) :
  - **Plafond** (même ligne, 100 W au départ) : il vaut pour **toutes** les mémoires ; une mémoire au-dessus est ramenée au plafond. Réglez-le selon votre amplificateur et votre antenne (ligne `PowerLimit=` des réglages) ;
  - **jamais pendant l'émission** : si le poste est en émission au rappel d'une mémoire, la puissance attend le **retour en réception** (la fréquence, elle, change tout de suite) ;
  - la valeur est aussi ramenée aux **limites du poste** (par exemple 5 W au minimum sur le FT-891), et le poste la limite lui-même selon la bande et le mode (en AM, le FT-891 ne dépasse pas sa puissance AM) ;
  - quand la commande est en **proportion** (Icom, Hamlib, TCI), SDRLink compte sur la puissance maxi de la **version la plus puissante** du poste : en cas de doute, le poste sort **moins** de watts que demandé, jamais plus ;
  - seuls les postes dont la commande est confirmée sont pilotés : les autres sont rappelés sans changer la puissance, et la barre d'état le dit.
- Par interface :
  - **Port COM** et **OmniRig** : commande du tableau ci-dessous (par OmniRig, envoyée telle quelle pour le modèle choisi dans OmniRig ; Icom : adresse CI-V par défaut du modèle) ;
  - **Hamlib** : `L RFPOWER <0 à 1>`, proportion de la puissance maxi donnée par rigctld (`\power2mW`), sinon par le tableau ; seulement pour un poste du tableau dont Hamlib sait régler la puissance ;
  - **TCI** (ExpertSDR, Thetis…) : `drive` en pourcentage, avec **% = watts** : juste sur un appareil de 100 W (SunSDR2 DX), moins de watts sur un plus petit (sur un SunSDR2 Pro de 20 W, 50 « W » donnent 50 %, soit 10 W). Proposé dès que le serveur TCI a annoncé son réglage ;
  - **Ham Radio Deluxe** : **pas de puissance par HRD** dans cette version. HRD n'a pas de commande CAT brute, et l'échelle de son curseur de puissance (« RF power percentage », 0 à 255 sur le FT-2000) n'est pas documentée : une erreur pourrait donner plus de watts que demandé. Elle sera ajoutée après un essai sur un vrai poste.
- Dans `settings.ini`, la puissance est le 7e champ de la ligne `Mem=` (vide ou absent = inchangée) ; les versions plus anciennes l'ignorent.

### Puissance : par interface et par poste

| Interface | Comment la puissance est commandée | Postes concernés |
|---|---|---|
| Port COM (CAT direct) | commande du tableau ci-dessous, après la fréquence, jamais en émission | les postes du tableau |
| OmniRig | la même commande, envoyée telle quelle par OmniRig ; poste reconnu par le nom choisi dans OmniRig | les postes du tableau |
| Hamlib (rigctld) | `L RFPOWER <0 à 1>` (watts ÷ puissance maxi) | les postes du tableau dont Hamlib sait régler la puissance |
| TCI (ExpertSDR, Thetis…) | `drive:0,<%>;` (ou `drive:<%>;` sur un ancien serveur), % = watts | appareils TCI, après l'annonce de leur `drive` |
| Ham Radio Deluxe | aucune (échelle du curseur HRD non documentée) | aucun |

Puissance maxi : celle de la version la plus puissante du modèle (FT-2000D, FTdx101MP, TS-480HX…) ; le poste ramène lui-même la valeur à ses limites. Icom : trame CI-V `FE FE <adresse> E0 14 0A <niveau en BCD> FD`, niveau proportionnel à la puissance maxi de la bande.

| Marque | Modèle | Puissance (W) | Commande |
|---|---|---|---|
| Yaesu | FT-450/450D | 5 à 100 | `PC005;` … `PC100;` |
| Yaesu | FT-710 | 5 à 100 | `PC005;` … `PC100;` |
| Yaesu | FT-891 | 5 à 100 | `PC005;` … `PC100;` |
| Yaesu | FT-950 | 5 à 100 | `PC005;` … `PC100;` |
| Yaesu | FT-991/991A | 5 à 100 | `PC005;` … `PC100;` |
| Yaesu | FT-2000 | 5 à 200 | `PC005;` … `PC200;` |
| Yaesu | FTdx10 | 5 à 100 | `PC005;` … `PC100;` |
| Yaesu | FTdx101D/MP | 5 à 200 | `PC005;` … `PC200;` |
| Yaesu | FTdx1200 | 5 à 100 | `PC005;` … `PC100;` |
| Yaesu | FTdx3000 | 5 à 100 | `PC005;` … `PC100;` |
| Yaesu | FTdx5000 | 10 à 200 | `PC010;` … `PC200;` |
| Yaesu | FTdx9000 | 5 à 400 | `PC005;` … `PC400;` |
| Icom | IC-703 | 1 à 10 | `14 0A 0000` … `14 0A 0255` (255 = 10 W) |
| Icom | IC-705 | 1 à 10 | `14 0A 0000` … `14 0A 0255` (255 = 10 W) |
| Icom | IC-706MKIIG | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W en HF / 50, 50 W en 144, 20 W en 430) |
| Icom | IC-718 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-746 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-746PRO | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-756PRO | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-756PROII | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-756PROIII | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-905 | 1 à 10 | `14 0A 0000` … `14 0A 0255` (255 = 10 W) |
| Icom | IC-910H | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W en HF / 144, 75 W en 430, 10 W en 1200) |
| Icom | IC-7000 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W en HF / 50, 50 W en 144, 35 W en 430) |
| Icom | IC-7100 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W en HF / 50, 50 W en 144, 35 W en 430) |
| Icom | IC-7200 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-7300 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-7300MK2 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-7400 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-7410 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-7600 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-7610 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W) |
| Icom | IC-7700 | 1 à 200 | `14 0A 0000` … `14 0A 0255` (255 = 200 W) |
| Icom | IC-7760 | 1 à 200 | `14 0A 0000` … `14 0A 0255` (255 = 200 W) |
| Icom | IC-7800 | 1 à 200 | `14 0A 0000` … `14 0A 0255` (255 = 200 W) |
| Icom | IC-7850 | 1 à 200 | `14 0A 0000` … `14 0A 0255` (255 = 200 W) |
| Icom | IC-7851 | 1 à 200 | `14 0A 0000` … `14 0A 0255` (255 = 200 W) |
| Icom | IC-9100 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W en HF / 144, 75 W en 430, 10 W en 1200) |
| Icom | IC-9700 | 1 à 100 | `14 0A 0000` … `14 0A 0255` (255 = 100 W en 144, 75 W en 430, 10 W en 1200) |
| Kenwood | TS-480 | 5 à 200 | `PC005;` … `PC200;` |
| Kenwood | TS-570 | 5 à 100 | `PC005;` … `PC100;` |
| Kenwood | TS-590S/SG | 5 à 100 | `PC005;` … `PC100;` |
| Kenwood | TS-890S | 5 à 100 | `PC005;` … `PC100;` |
| Kenwood | TS-990S | 5 à 200 | `PC005;` … `PC200;` |
| Kenwood | TS-2000 | 5 à 100 | `PC005;` … `PC100;` |
| Kenwood | TS-B2000 | 5 à 100 | `PC005;` … `PC100;` |
| Elecraft | K3/K3S | 1 à 110 | `PC001;` … `PC110;` |
| Elecraft | KX2 | 1 à 12 | `PC001;` … `PC012;` |
| Elecraft | KX3 | 1 à 15 | `PC001;` … `PC015;` |

Sans commande de puissance pilotée par SDRLink (la puissance s'y règle à la main) :
- **Yaesu** : FT-100/100D, FT-600, FT-650, FT-736R, FT-747GX, FT-757GX, FT-757GXII, FT-767GX, FT-817, FT-817ND, FT-818, FT-818ND, FT-840, FT-847, FT-857, FT-857D, FT-890, FT-897, FT-897D, FT-900, FT-920, FT-990, FT-1000, FT-1000D, FT-1000MP, FT-1000MP Mark-V, FT-1000MP Mark-V Field, FTX-1
- **Icom** : IC-78, IC-271, IC-275, IC-375, IC-471, IC-475, IC-575, IC-706, IC-706MKII, IC-707, IC-725, IC-726, IC-728, IC-729, IC-735, IC-736, IC-737, IC-738, IC-751/751A, IC-756, IC-761, IC-765, IC-775, IC-781, IC-820H, IC-821H, IC-970, IC-1275
- **Kenwood** : TS-50S, TS-60S, TS-140S, TS-440S, TS-450S, TS-680S, TS-690S, TS-711, TS-790, TS-811, TS-850S, TS-870S, TS-930S, TS-940S, TS-950S, TS-950SDX
- **Elecraft** : K2, K4
- **Xiegu** : G90, X108G, X5105, X6100, X6200

## Barre d'état (messages en bas de la fenêtre)

- **Clic droit** sur la barre d'état : les **20 derniers messages**, le plus récent en haut, avec leur heure.
- Cliquer sur un message **masque ce type de message** (coché ✓) : il ne s'affiche plus dans la barre, mais reste dans la liste des derniers messages. Les messages qui ne diffèrent que par un nom ou un nombre (« « A » ajouté à « HF » », « « B » ajouté à « VHF » ») sont du même type. Cliquer de nouveau le réaffiche.
- **Réafficher tous les messages masqués** : annule tous les masquages. Les types masqués sont conservés au prochain lancement.
- Le message « Câble SDRLINK : aucun câble audio virtuel trouvé » disparaît dès que le câble est mis sur **Désactivé**.

## Import / Export des réglages

Bouton **Import/Export ▾** (1re ligne, après « Vérifier les mises à jour ») : pour retrouver exactement la même configuration sur un autre ordinateur, ou garder une sauvegarde. Clic droit : couleur du bouton.
- Tous les réglages de SDRLink sont dans un seul fichier : **`%APPDATA%\SDRLink\settings.ini`** (par exemple `C:\Users\<nom>\AppData\Roaming\SDRLink\settings.ini`). Le journal `sdrlink.log` est à côté.
- **Exporter tous les réglages…** : enregistre ce fichier où vous voulez (par défaut dans Documents, nom `SDRLink-reglages-<date>.ini`), avec l'état du moment : interface et poste (port COM, vitesse, TCI, HRD, Hamlib…), synchronisation, décalage, favoris et leurs listes, mémoires, historique des fréquences, langue, mode sombre et palette, look des boutons, position du panneau de commande, couleurs des boutons, taille du texte, REC (dossier, carte son, couleur), Réinit Auto WebSDR (Auto, délai), câble SDRLINK, scan (réglage, zones non scannées et profils de scan), volumes, taille et position de la fenêtre, etc.
- **Importer des réglages…** : choisir un fichier exporté. Une fenêtre demande **ce qu'il faut importer**, avec une case par partie (une seule ou plusieurs) :
  - **Configuration** (poste, interface, affichage, couleurs, langue… : tout ce qui n'est pas dans les trois cases suivantes) ;
  - **WebSDR** (favoris, listes de favoris, dernier WebSDR ouvert, volume des WebSDR hors favoris) ;
  - **Mémoires de fréquences** (mémoires et listes de mémoires) ;
  - **Scan** (réglage de Conf scan, zones non scannées et profils de scan).
  - Une partie absente du fichier est grisée (« absent du fichier »). Une partie non cochée garde les réglages actuels.
  - Pour les favoris, les mémoires et les profils de scan déjà dans SDRLink, choisir :
    - **les garder et y ajouter ceux du fichier** (par défaut, sans doublons : même adresse dans la même liste pour un favori, même nom, fréquence et mode pour une mémoire, profil identique pour le scan). Les listes en plus dans le fichier sont créées avec leur nom ; un profil du fichier qui porte le nom d'un autre profil est renommé « nom (2) » ; 10 profils au plus. Le réglage du moment de Conf scan ne change pas ;
    - **les effacer et les remplacer par ceux du fichier**.
  - La configuration cochée est toujours remplacée par celle du fichier.
  - **Un réglage que le fichier ne cite pas ne change pas** (depuis la 1.45) : par exemple, un fichier exporté par une version plus ancienne, sans les volumes FreeDV et TX sur PC, garde les volumes actuels. Avant, un réglage absent du fichier revenait à sa valeur de départ.
  - Puis **Importer** : SDRLink redémarre avec les réglages obtenus. Les anciens réglages sont gardés dans `settings.avant-import.ini` (même dossier) : pour revenir en arrière, réimporter ce fichier en cochant tout et en choisissant « les effacer ». Le journal indique ce qui a été ajouté.
- Un fichier qui ne contient pas de réglages SDRLink est refusé (message dans la barre d'état).
- Sur un autre ordinateur, vérifier ce qui dépend de la machine : port COM du poste, cartes son (câble SDRLINK, carte son du poste pour REC), chemin de rigctld, dossier des enregistrements. Une carte son du poste absente est signalée « non branchée » dans le menu de REC ; si la position de la fenêtre tombe hors des écrans, elle est ignorée.
- Les données de navigation des pages WebSDR (cookies, cache) ne font pas partie des réglages et ne sont pas exportées.

## Réinitialiser le programme

**Clic droit sur Import/Export > Réinitialiser le programme…** (depuis la 1.22) : pour repartir de zéro, en tout ou en partie, par exemple si les pages des WebSDR restent vides (fenêtre blanche, comme sans Internet) alors qu'Internet marche.
- **Réinitialisation totale (perte de tous les paramètres, mémoires et couleurs)** : SDRLink redevient comme à sa première installation (favoris WebSDR de départ, aucune mémoire, aucun scan, couleurs d'origine, données du navigateur effacées).
- **Réinitialisation partielle** (choix par défaut), une case par partie, une ou plusieurs :
  - **Mémoires de fréquences** ;
  - **Scan** (réglage, zones non scannées et profils de scan) ;
  - **Couleurs** (boutons, REC, palette) ;
  - **WebSDR** (favoris et listes de favoris, remis à ceux de départ) ;
  - **Configuration** (poste, interface, synchro, son, affichage, langue, pseudo… : tout le reste) ;
  - **Données du navigateur intégré** (cache, cookies, réglages gardés par les sites WebSDR) : **à essayer en premier si les pages des WebSDR ne s'affichent plus**. Les réglages de SDRLink n'en sont pas touchés.
- **Réinitialiser** reste grisé tant que rien n'est choisi ; **Annuler** est le bouton par défaut. Une confirmation est demandée (« Non » par défaut), puis SDRLink **redémarre** et fait la réinitialisation au démarrage.
- Les réglages d'avant sont gardés dans `%APPDATA%\SDRLink\settings.avant-reinitialisation.ini` : pour revenir en arrière, **Import/Export > Importer des réglages…** et choisir ce fichier. Les données du navigateur effacées, elles, ne reviennent pas (les sites redemandent simplement leurs choix).
- Si les données du navigateur ne peuvent pas être effacées (fichiers encore ouverts), SDRLink réessaie au démarrage suivant ; le journal l'indique.
- **Les mises à jour de SDRLink ne réinitialisent jamais rien** : les réglages sont dans `%APPDATA%\SDRLink`, pas à côté de SDRLink.exe ; remplacer l'exe les garde tous.

## Mises à jour

Bouton **Vérifier les mises à jour** (1re ligne, après « Taille du texte… ») : depuis la 1.27, la case est **dans le bouton**, à gauche du texte.
- **Clic sur la case** (cochée par défaut) : vérification automatique au démarrage oui / non. Cocher ou décocher n'ouvre rien.
- **Clic ailleurs sur le bouton** (ou Entrée / Espace quand il a le focus) : vérification tout de suite (« Recherche des mises à jour sur GitHub… » dans la barre d'état), puis la fenêtre de mise à jour s'ouvre dans tous les cas, avec un texte adapté : nouvelle version disponible, « Vous utilisez la dernière version de SDRLink » ou « Impossible de joindre GitHub ». En bas, elle rappelle si la vérification automatique est activée ou désactivée et comment la changer.
- **Clic droit** : couleur du bouton.

Case cochée : au démarrage, si Internet répond, SDRLink regarde la dernière version publiée sur GitHub. S'il en existe une plus récente, une fenêtre s'ouvre (depuis la 1.18) : elle indique la version utilisée et la dernière version publiée, avec le lien direct vers la page de cette version (téléchargement et nouveautés) et le lien vers la liste de toutes les versions (https://github.com/F5pbgLudovic/SDRLink/releases, la dernière y est marquée « Latest »). Le bouton **Ouvrir la page de la mise à jour** ouvre cette page dans le navigateur ; **Plus tard** ferme la fenêtre. Un lien orange « ⚠ Mise à jour Vx.y disponible » reste aussi en bas à droite de la fenêtre, dans la barre d'état (visible aussi en affichages réduit et mini) : un clic rouvre cette fenêtre (depuis la 1.27 ; avant, il ouvrait la page). Pour ne plus être prévenu, décochez la case du bouton.

**Mise à jour automatique** (depuis la 1.27) : dans cette fenêtre, le lien orange **➜ Mettre à jour automatiquement en Vx.y maintenant** fait tout seul :
- il télécharge `SDRLink.exe` de la nouvelle version sur GitHub, avec une barre de progression et un bouton **Annuler** ;
- il vérifie le fichier : taille, **empreinte SHA-256** publiée par GitHub et numéro de version ; s'il ne correspond pas, rien n'est changé ;
- il met la nouvelle version à la place de celle-ci, **sous le même nom `SDRLink.exe` et dans le même dossier** : un raccourci sur le bureau ou dans la barre des tâches lance donc toujours la nouvelle version ;
- il ferme SDRLink (réglages enregistrés comme d'habitude) et lance la nouvelle version, qui attend la fermeture complète de l'ancienne avant de démarrer, puis **supprime l'ancienne version du disque** (depuis la 1.30 ; avant, elle restait à côté sous le nom `SDRLink.previous.exe`). La barre d'état indique ensuite « SDRLink a été mis à jour en Vx.y ; l'ancienne version a été supprimée. » Si Windows bloque encore l'ancien fichier, il est supprimé au démarrage suivant. Les copies `SDRLink.previous.exe` laissées par les mises à jour d'avant la 1.30 sont supprimées aussi.

Les réglages ne bougent pas (ils sont dans `%APPDATA%\SDRLink`). Si quelque chose échoue (antivirus qui bloque ou supprime le fichier, dossier où SDRLink n'a pas le droit d'écrire comme `Program Files`, Internet coupé…), SDRLink reste tel quel, explique pourquoi et propose d'ouvrir la page de téléchargement. **Les liens vers la page de la version et vers toutes les versions restent dans la fenêtre** : certains antivirus n'aiment pas qu'un programme se remplace lui-même, on peut alors toujours télécharger à la main. La mise à jour automatique fonctionne à partir d'une version qui la contient (1.27 ou plus) vers une version plus récente.

## Dépannage

**OmniRig ne fonctionne pas** (pas de fréquence, erreur à la connexion) : utiliser **OmniRig version 1** (pas la 2), lancé **en mode administrateur** (clic droit sur OmniRig > Exécuter en tant qu'administrateur), et **SDRLink aussi en mode administrateur**. Depuis la 1.34, SDRLink ouvre lui-même une fenêtre d'aide avec ces conseils quand OmniRig ne répond pas.

Réglages : `%APPDATA%\SDRLink\settings.ini`. Journal : `%APPDATA%\SDRLink\sdrlink.log`. Le journal est remis à zéro à chaque lancement et contient l'état de la connexion et, pour HRD, la liste des modes reçue.

Message « Unhandled exception… 0x8007139F » en cliquant sur un fichier `.html` dans la fenêtre « Importer des favoris » : il vient de l'aperçu **Monaco** de **PowerToys**, pas de SDRLink (cliquer sur Continue, l'import fonctionne). Pour l'éviter : **Alt+P** dans la fenêtre d'ouverture (masque le volet d'aperçu), ou dans PowerToys > Extensions de l'Explorateur de fichiers, désactiver l'aperçu du code source (Monaco).

## Licence

Depuis la 1.25, SDRLink est un **logiciel libre** sous **licence GNU GPL v3** (fichier `LICENSE` du dépôt GitHub). Il est fourni sans aucune garantie. Vous pouvez l'utiliser, l'étudier, le modifier et le redistribuer, à condition de fournir les sources sous la même licence. Raison : les décodeurs Olivia / Contestia (bibliothèque de Pawel Jalocha, reprise de fldigi) et JS8 (données de JS8Call) sont sous GPL. Les autres composants (FreeDV : BSD ; ft8_lib : MIT ; RNNoise : BSD ; com0com : GPL ; WebView2) sont listés dans `THIRD-PARTY-NOTICES.md`.

## Historique des versions

- **1.51**
  - **Micro du PC vers le poste** (clic droit sur PTT) : le micro choisi est envoyé en continu sur la carte son d'émission du poste (la même que Tune), sans fenêtre, sans écoute en retour, avec environ 20 à 40 ms de retard ; les autres logiciels gardent le micro. Muet pendant Tune.
- **1.50**
  - **Trois types de notch automatique (DNF)**, au choix dans la fenêtre du clic droit du bouton : **d'origine** (celui d'avant), **adaptatif (NLMS)** (suit les sifflements qui glissent, en traite autant qu'il y en a) et **spectral (FFT)** (le plus propre, ne touche que les raies stables). Chaque type garde son niveau de 1 à 10.
  - **Notch sur le son du poste** joué par SDRLink (écoute du poste sur le PC, oreille du poste en diversité) : même type, même niveau et mêmes coupures que pour le WebSDR (case de la fenêtre du notch, cochée au départ).
- **1.49**
  - **Bouton TUNE** au bout de la ligne du PTT, de même forme et aligné sur lui : tonalité de 1500 Hz sur la carte son d'émission du poste, pendant 5 s à 1 min. Clic droit : carte son, **niveau de 1 à 100 %** (petite fenêtre à réglette et case, à gauche du menu resté ouvert ; OK y ramène), durée, **PTT actif par CAT ou VOX du poste**, couleurs, **Fermer**.
  - **VFO A ou B** : au clic droit sur la fréquence **RADIO** (affichée « RADIO B »), pour piloter le VFO B des postes qui en ont deux (toutes les interfaces, sauf les anciens Yaesu binaires).
  - **Port COM virtuel** : le poste en CAT direct peut être partagé avec un autre logiciel (WSJT-X, journal…) par une paire com0com ; installation de com0com, création et **suppression** de paires depuis la fenêtre ; bouton **vert** quand le port virtuel est en service, réglable aussi poste connecté.
- **1.48**
  - **Trois types de DNR**, au choix dans le clic droit du bouton DNR : **classique** (celui de la 1.46), **amélioré** (MMSE-LSA : voix beaucoup moins déformée à réduction égale) et **IA** (RNNoise, réseau de neurones intégré, sans site externe : le bruit peut disparaître presque entièrement entre les mots). Le niveau de 1 à 10 de chaque type est dans son sous-menu (clic droit sur le type). Les coupures en mode numérique et en FreeDV valent pour les trois.
  - **Bouton Notch auto (DNF)** : la case disparaît ; **clic** = marche / arrêt (bouton vert en marche), **clic droit** = la fenêtre du notch (coupures, niveau, explication), qui donne aussi les couleurs du bouton.
  - **DNR sur le son du poste** joué par SDRLink (écoute du poste sur le PC, oreille du poste en diversité) : même type, même niveau et mêmes coupures que pour le WebSDR (case du clic droit, cochée au départ).
- **1.47**
  - **Volumes au pas de 1 %** : molette, flèches et clic sur tous les curseurs de volume (WebSDR / FreeDV, TX sur PC, TX, ExpertSDR, HDSDR). La molette allait trop vite (6 % par cran avec le réglage de Windows à 6 lignes).
- **1.46**
  - **DNR** (réducteur de bruit) : nouveau bouton après Notch auto (DNF). Clic : marche / arrêt ; clic droit : niveau de 1 à 10, coupure en mode numérique et en FreeDV, couleurs. Il réduit le souffle du son du WebSDR, de 12 dB au niveau 3 à 33 dB au niveau 10.
  - **Lien de partage de SDRLink** (rond partage) : le lien copié est seul, sans texte autour. Sa page propose d'ouvrir SDRLink au même endroit (même déjà ouvert), d'écouter dans le navigateur ou de télécharger SDRLink ; sur Android, PhoneSDRLink. Un lien de partage collé dans la case WebSDR est aussi compris.
  - **Diversité sur le poste** : Mute TX ne coupe plus l'oreille du poste (son haut-parleur est déjà muet) ; les deux voies restent, Volume TX règle l'oreille du poste.
- **1.45**
  - **Volume TX sur PC** : nouveau curseur entre Volume WebSDR et Volume TX quand « Écouter aussi le poste sur le PC » est coché. Il règle seulement le son rejoué dans les haut-parleurs du PC ; REC, la diversité et les décodeurs gardent le son complet.
  - **Volume FreeDV** : pendant une parole FreeDV, le curseur Volume WebSDR devient Volume FreeDV et règle la voix décodée (avant, niveau fixe). Il garde sa propre valeur, séparée de celle du WebSDR.
  - Les deux nouveaux volumes sont gardés après redémarrage et exportés ; à l'**import**, un réglage absent du fichier ne change plus.
  - **Diversité avec ExpertSDR** réparée : SDRLink attend qu'ExpertSDR soit prêt avant de demander son son par TCI ; s'il reste muet une fois coupé, son volume est baissé au minimum à la place.
- **1.44**
  - **Diversité sur deux WebSDR** : clic droit sur Diversité, **Sur deux WebSDR** ; le second WebSDR se choisit dans la case qui suit le bouton Diversité. Il est écouté sans être affiché, suit la fréquence et le mode du WebSDR affiché, et SDRLink cale les deux sons quel que soit celui qui arrive en premier.
- **1.43**
  - **Ronds partage, + et − sur la page** du WebSDR (comme dans PhoneSDRLink), au clic gauche ; clic droit : 6 positions (coins, milieu haut, milieu bas), verticales ou horizontales. + et − : zoom de la page retenu par WebSDR (oublié seulement par la réinitialisation totale du WebSDR). **Partage** : lien de PhoneSDRLink copié dans le presse-papiers.
  - **OpenWebRX** : page réduite automatiquement juste assez pour voir tout le panneau de commande (il passait sous le bandeau du haut sur certains écrans).
  - **Masquer l'adresse du WebSDR** (clic droit sur la page) : étoiles dans la case, la liste reste utilisable.
- **1.42**
  - **Puissance mémorisée** avec chaque mémoire et pour les fréquences de la liste **Modes numériques** (en watts, case **régler à** ; rien par défaut), envoyée au rappel juste après la fréquence, en CAT direct, par OmniRig, Hamlib et TCI (pas par HRD). **Plafond** commun à toutes les mémoires, **jamais pendant l'émission** (attend le retour en réception), en cas de doute moins de watts, jamais plus. 49 modèles pilotés : tableau dans la notice.
- **1.41**
  - **Préampli mémorisé** avec chaque mémoire, et pour les fréquences de la liste **Modes numériques** (Modifier…), aussi au choix d'un mode numérique qui accorde sur sa fréquence (IPO / AMP1 / AMP2 chez Yaesu, IPO1 / IPO2 sur le FTdx5000, P.AMP chez Icom, PRE chez Kenwood et Elecraft), appliqué au rappel juste après la fréquence, en CAT direct, par Hamlib, OmniRig et HRD.
  - **Diversité** : un simple bouton (clic = marche / arrêt), largeur fixe ; le **notch automatique** s'applique aussi à l'oreille du WebSDR.
  - **Couleurs par état** au clic droit : Diversité, Notch auto, Mode ?, bouton des modes numériques ; couleur de ⇄.
  - Menu des listes de favoris rouvert s'il se referme tout seul moins de 0,5 s après son ouverture.
- **1.40**
  - Case **Diversité** (après Réinit WebSDR) : **écoute en diversité**, le WebSDR dans une oreille et le poste, ExpertSDR ou HDSDR dans l'autre, le son local **retardé automatiquement** pour tomber en même temps que le WebSDR.
  - Décodage d'**ExpertSDR** (FreeDV auto, modes numériques, Mode ?) par son **flux audio TCI** : sous Windows 10, la capture du son d'un programme ne rendait que du silence. **HDSDR** n'est plus décodé que sous **Windows 11**, pour la même raison.
- **1.39**
  - Bouton **Mode ?** : plusieurs signaux dans la bande passante sont lus chacun par leurs propres décodeurs CW, PSK et RTTY ; le **rythme des tranches FT8 / FT4** est reconnu et donne le retard du son du WebSDR au décodeur.
- **1.38**
  - **Modes numériques** : fenêtre séparée, déplaçable sur un **second écran**, et **plein écran** ; choisir un mode **accorde sur sa fréquence d'usage la plus proche** ; bouton **Mode ?** qui reconnaît le mode écouté (33 s au plus) et lance son décodage.
  - **Bande passante de réception** (liste BP) retenue pour chaque mode, sur tous les WebSDR et après redémarrage.
  - **Notch auto** : case dans le bouton ; fenêtre avec coupure en mode numérique et en FreeDV, **niveau de 1 à 10**.
  - **Pas** de 10, 25 et 50 Hz ajoutés ; pas retenu pour chaque mode (CW 10 Hz, AM/FM 1 kHz, autres 500 Hz).
  - Corrigé : sur websdr.org, une mémoire CW rappelée depuis USB tombait 750 Hz à côté (le mode est maintenant réglé avant la fréquence).
- **1.37**
  - **Réinit Auto WebSDR** : au clic droit sur Réinit WebSDR, case **Auto** et délai sans son (10 à 60 s) ; le WebSDR est relancé tout seul quand son son est perdu (contrôle toutes les 10 s, 3 essais de suite au plus), jamais quand le son est coupé volontairement (Mute WebSDR, volume à 0, volume ou mute de la page). Le bouton devient « Réinit Auto WebSDR », même largeur, deux couleurs réglables. La réinitialisation totale passe dans ce menu.
  - **REC** et **Mute TX** ont la même taille que **Mute WebSDR** (le choix étroit / large de REC disparaît).
- **1.36**
  - **KiwiSDR : zoom de la cascade gardé** quand SDRLink change la fréquence ou le mode (synchro, clavier, mémoires, scan) ; avant, le KiwiSDR revenait à la vue de toute la bande. Vérifié aussi sur websdr.org, OpenWebRX+, UberSDR et PhantomSDR, qui gardaient déjà leur zoom.
- **1.35**
  - Bouton **Panneau de commande** : le panneau (de la ligne Interface à la ligne des Mute) passe du haut de la fenêtre à sous la page du WebSDR, et inversement ; la barre d'état reste en bas, la position est retenue.
- **1.34**
  - **Liaison avec le poste vérifiée** : le bouton affiche « Connexion... » (orange) tant que le poste n'a pas répondu, « Déconnecter » seulement quand il répond ; contrôle 15 s après la connexion puis chaque minute, liaison figée détectée.
  - **Aide OmniRig** au premier plan si le poste ne répond pas (OmniRig version 1, mode administrateur pour OmniRig et SDRLink).
- **1.33**
  - Bouton **Look ▾** : 7 allures de boutons au choix (Classique, Win3.11, Néon IA, Moderne, Verre, Terminal, Métal), sans changer la taille des boutons.
- **1.32**
  - **Synchro Désactivée** : la case kHz (QSY), les mémoires et la liste Mem, le scan et les boutons de mode ne règlent plus que le **WebSDR**, sans toucher à la fréquence ni au mode de l'émetteur.
  - **Délai de sécurité TX** (clic droit sur PTT) : jusqu'à **55 min** (30, 35, 40, 45, 50 et 55 min ajoutés).
- **1.31**
  - Bouton **Réinit WebSDR** après REC, pour relancer un WebSDR arrêté (délai d'inactivité dépassé…) : **clic** = relance en gardant ses réglages (fréquence, mode, filtre, zoom, volume) ; **clic droit** = réinitialisation totale du WebSDR en cours (cookies et réglages du site effacés, comme à la première visite).
- **1.30**
  - **Mise à jour automatique** : l'ancienne version est **supprimée du disque** dès que la nouvelle a démarré (plus de `SDRLink.previous.exe`) ; la nouvelle garde le nom `SDRLink.exe`, les raccourcis du bureau marchent toujours.
  - **Rechercher des WebSDR** : les distances partent de **votre position** et non plus du centre de la France : position de l'ordinateur (localisation de Windows), ou locator ou coordonnées tapés (retenus). « Distance max par rapport à la position actuelle ». Centre de la France seulement si aucune position n'est connue.
- **1.29**
  - **Palette** des couleurs de fond : **64 teintes** en mode sombre et 64 en mode clair, 32 de chaque côté du bouton (neutres et froides à gauche, chaudes à droite).
  - **Couleur au choix** sous la palette : valeurs **Rouge**, **Vert**, **Bleu** (0 à 255) avec aperçu du texte, **Appliquer**, ou **Autre couleur…** (sélecteur de Windows).
- **1.28**
  - **Mémoires** : liste **Modes numériques**, comme dans PhoneSDRLink : les fréquences habituelles du mode utilisé (1,8 à 440 MHz) selon la région IARU, ouverte directement quand un décodeur est en marche ; **Copier…** vers vos listes.
  - **SSTV** : si l'émission continue après le bas de l'image, l'image remonte ligne par ligne au lieu de s'arrêter.
  - **Toutes les réceptions coupées** : les modes numériques et FreeDV continuent de décoder (poste ou WebSDR).
  - **Changement de WebSDR ou ◀ Retour sans le poste** (déconnecté ou pas synchronisé) : le nouveau WebSDR reprend la fréquence et le mode du précédent.
- **1.27**
  - **Mise à jour automatique** : dans la fenêtre « mise à jour disponible », lien **➜ Mettre à jour automatiquement en Vx.y maintenant**. SDRLink télécharge la nouvelle version, vérifie son empreinte SHA-256, la met à la place de l'exe (l'ancien est gardé en `SDRLink.previous.exe`) et redémarre. Les liens vers la page de téléchargement restent, pour les antivirus qui bloquent. Le lien « ⚠ Mise à jour disponible » de la barre d'état rouvre cette fenêtre.
  - **Bouton « Vérifier les mises à jour »** (après « Taille du texte… ») : la case est dans le bouton. Case : vérification automatique au démarrage ; clic ailleurs sur le bouton : vérifier tout de suite et ouvrir la fenêtre de mise à jour, avec un texte adapté s'il n'y a pas de nouvelle version.
- **1.26**
  - **Clic gauche sur la fréquence RADIO ou WEBSDR** : clavier de fréquence comme dans PhoneSDRLink. Touches en cases arrondies, puis **Mem ▾**, **Store**, **Auto N min**, puis **Annuler** et **OK** ; le clavier du PC marche aussi.
  - Liste **Mem** : les 20 dernières fréquences avec le mode, le mode numérique en marche, l'heure et la date. Elle s'ouvre à droite du clavier (à gauche s'il n'y a pas la place), avec un ascenseur si besoin. Clic : y retourner, mode numérique compris ; clic droit ou appui long : copier dans « Mémoires » ou supprimer.
  - **Store** et enregistrement automatique (**Auto**, 0 à 30 min) quand la fréquence ne change pas pendant ce temps. Liste et réglage gardés après redémarrage.
- **1.25**
  - Bouton des **modes numériques** (après « FreeDV auto ») : tous les décodeurs de PhoneSDRLink — FT8, FT4, FT2, WSPR, JT9, JS8, CW, RTTY, PSK31 / PSK63, Olivia, Contestia, Hell, SSTV (26 modes) — plus **WEFAX** (fac-similé météo), sur la même réception que FreeDV (poste, sinon WebSDR, sinon ExpertSDR, sinon HDSDR). Clic : marche / arrêt ; clic droit : choix du mode (clic gauche) ou choix et réglages du mode (clic droit), avec les mêmes réglages que PhoneSDRLink et la surbrillance. Le texte ou l'image s'affiche dans une fenêtre posée sur la page, en bas à gauche, déplaçable par appui long puis glisser, agrandissable par les bords et les coins comme une fenêtre Windows (proportions fixes en SSTV et Hell : l'image est agrandie d'autant ; place et taille retenues pour chaque mode ; **Réinitialiser la fenêtre <mode>** et **Réinitialiser les fenêtres** dans le menu) ; les images SSTV et WEFAX sont enregistrées en PNG dans Images\SDRLink. Tous les réglages des modes sont retenus après redémarrage. Arrêté, rien ne tourne.
  - SDRLink est désormais un **logiciel libre sous licence GNU GPL v3** (fichier LICENSE du dépôt), comme PhoneSDRLink : les décodeurs Olivia / Contestia (bibliothèque de Pawel Jalocha, reprise de fldigi) et le dictionnaire JS8 (JS8Call) sont sous GPL.
- **1.24**
  - Bouton **FreeDV auto** (après « Rechercher des WebSDR… ») : décodage automatique de FreeDV RADE V1 (ou V2 au clic droit) sur le son reçu (poste, sinon WebSDR, sinon ExpertSDR, sinon HDSDR), en USB ou LSB selon le mode choisi ; dès qu'un signal FreeDV est reçu, la voix décodée remplace le son brut. Arrêté, rien ne tourne. L'exe passe à 17 Mo (décodeur FreeDV).
- **1.23**
  - **Écouter aussi le poste sur le PC** (clic droit sur Mute TX, non coché au départ) : le son de la carte son du poste (celle de REC) est joué dans les haut-parleurs du PC tant que Mute TX n'est pas actif. Sans carte choisie, la fenêtre de choix de la carte s'ouvre (pas de message d'erreur).
- **1.22**
  - **Réinitialiser le programme** (clic droit sur Import/Export) : réinitialisation totale, comme une première installation, ou partielle au choix (mémoires, scan, couleurs, WebSDR, configuration, données du navigateur intégré). Anciens réglages gardés dans settings.avant-reinitialisation.ini. Utile si les pages des WebSDR restent vides. Les mises à jour ne réinitialisent rien.
  - **Clic dans la page pendant un scan** : le scan s'arrête sur la fréquence scannée ; si le clic a déplacé la cascade, la fréquence y est remise aussitôt (le poste n'en bouge pas). Le clic suivant règle normalement.
- **1.21**
  - Case **Avant plan** dans l'affichage ptt, sous « Affichage ptt » : la petite fenêtre peut être mise au premier plan sans revenir en grand ; sa taille ne change pas.
  - **Reprendre le scan**, en haut de la liste du bouton ▶ Scan : le scan repart de la fréquence actuelle (ou de la mémoire sur cette fréquence) ; hors de la plage, il repart de la première fréquence. Le bouton ▶ Scan ouvre maintenant toujours la liste, même sans profil.
- **1.20**
  - **Son sans clic** à l'arrivée sur une page : « Click to start » d'UberSDR et voile « Tap to enable audio » de PhantomSDR passés automatiquement.
  - Bouton **Pseudo/Call** (1re ligne, après Import/Export) : nom ou indicatif donné d'office aux KiwiSDR et websdr.org qui en demandent un avant le son, « SDRLink » par défaut, jamais vide ; fenêtre ouverte d'un clic.
  - **KiwiSDR complet** : entrée automatique dans la file d'attente (« Enter queue »).
  - **Squelch** ouvert sur les WebSDR qui en ont un, sauf en FM à partir de 30 MHz.
  - **websdr.org à une seule bande affichée** (Bordeaux) : changement de bande avec la fréquence du poste (cascade et curseur sur la bonne bande), mode du poste gardé ; un clic sur un bouton de bande de la page fait suivre le poste.
  - **Changement de WebSDR** : le son entendu correspond à la fréquence affichée (UberSDR, PhantomSDR).
  - **PhantomSDR (French Alps)** : le poste suit un clic dans le spectre ; le son qui restait muet après un changement de fréquence revient tout seul.
  - **Synchro plus rapide** : UberSDR signale ses changements en 15 à 45 ms au lieu de 0,3 s ; les autres pages sont relues toutes les 125 ms.
  - **Clic dans la page dès l'arrivée** : un clic, une touche ou la molette de l'utilisateur est suivi tout de suite par le poste, sans attendre les 8 secondes de mise en place ; un saut fait par la page seule est toujours remis sur le poste.
- **1.19**
  - **Coupure du son en émission** : « Couper le son du WebSDR en émission » et « rétablir après (ms) » agissent maintenant aussi sur le **poste** (gain BF), **ExpertSDR** et **HDSDR**, pas seulement sur le WebSDR. **Clic droit** sur la case pour choisir les réceptions concernées (toutes par défaut). Une réception déjà coupée à la main reste coupée.
  - **Barre WebSDR** : pendant la frappe, des **propositions** s'affichent juste dessous : les WebSDR de la liste choisie dont le nom ou l'adresse contient l'un des mots tapés ; un clic ouvre le WebSDR. La flèche de la liste déroulante affiche toujours tous les WebSDR de la liste.
- **1.18**
  - **Mises à jour** : quand une version plus récente est publiée, une **fenêtre s'ouvre** au démarrage (case « Vérifier les mises à jour » cochée) avec la version utilisée, la dernière version publiée, le lien direct vers la page de cette version et le lien vers la liste de toutes les versions ; bouton « Ouvrir la page de la mise à jour ». Avant, seul un lien apparaissait dans la barre d'état, facile à manquer. Cocher la case à la main ouvre la fenêtre dans tous les cas (à jour, nouvelle version ou GitHub injoignable).
- **1.17**
  - **Conf scan, zones non scannées** : les deux bornes d'une zone sont maintenant **scannées**, seules les fréquences entre les deux sont sautées (zone de 96 à 103, pas de 1 → le scan s'arrête sur 96 puis repart à 103 ; avant, il passait de 95 à 104).
- **1.16**
  - **Conf scan, zones non scannées** : jusqu'à 10 zones « de … à … kHz » sautées par le scan de fréquences (par ex. 7040 à 7200 sans 7097 à 7103), affichées seulement si elles sont définies ;
  - **profils de scan** : jusqu'à 10 réglages de scan nommés (Enregistrer, Renommer, Supprimer dans Conf scan) ; avec des profils, **▶ Scan** propose le réglage actuel ou l'un des profils ;
  - **Import** : une fenêtre demande quoi importer (cases Configuration, WebSDR, Mémoires de fréquences, Scan), et si les favoris, mémoires et profils de scan du fichier **s'ajoutent** aux actuels (sans doublons) ou les **remplacent** ;
  - l'export contient aussi les zones non scannées et les profils de scan.
- **1.15**
  - **HDSDR** : quand HDSDR est lancé sur le PC, **Mute HDSDR** (après les autres Mute) et **Volume HDSDR** (réglette et %) apparaissent sur la ligne de son, et disparaissent quand il est fermé. Le son est coupé et réglé par le mélangeur de volume de Windows, sans port COM supplémentaire ; HDSDR peut rester relié à HRD. ExpertSDR garde son fonctionnement et peut tourner en même temps ;
  - **⇄** permute aussi avec HDSDR ; clic droit sur ⇄ : jusqu'à **deux** coupures laissées de côté (cases à cocher), pour ne permuter qu'entre les réceptions restantes ;
  - Mute HDSDR : couleurs son coupé / son actif au clic droit, comme les autres Mute ;
  - REC : la carte son du poste est prise si Mute HDSDR est aussi actif quand HDSDR joue.
- **1.14**
  - **nouveaux postes en CAT direct**, après comparaison avec les listes de Hamlib, OmniRig et Ham Radio Deluxe :
    - Yaesu : FT-100/100D, FT-600, FT-650, FT-736R, FT-747GX, FT-757GX, FT-757GXII, FT-767GX, FT-840, FT-847, FT-890, FT-900, FT-920, FTdx9000, FTX-1 ;
    - Icom : IC-78, IC-271, IC-275, IC-375, IC-471, IC-475, IC-575, IC-703, IC-707, IC-725, IC-726, IC-728, IC-729, IC-735, IC-736, IC-737, IC-738, IC-746, IC-751/751A, IC-756, IC-756PRO, IC-756PROII, IC-761, IC-765, IC-775, IC-781, IC-820H, IC-821H, IC-905, IC-910H, IC-970, IC-1275, IC-7300MK2, IC-7400, IC-7850, IC-9100 ;
    - Kenwood : TS-50S, TS-60S, TS-140S, TS-440S, TS-450S, TS-680S, TS-690S, TS-711, TS-790, TS-811, TS-930S, TS-940S, TS-950S, TS-950SDX, TS-B2000 ;
    - Xiegu : X108G, X5105 ;
  - liste des modèles **classée par numéro** pour chaque marque ;
  - Xiegu G90 : adresse CI-V proposée corrigée en 88 (valeur d'usine d'après Hamlib) ;
  - anciens Kenwood : mode lu dans la réponse `IF;` quand le poste ne connaît pas `MD;`.
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
  - case « Vérifier les mises à jour » et lien vers la nouvelle version dans la barre d'état (fenêtre de mise à jour depuis la 1.18).
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
