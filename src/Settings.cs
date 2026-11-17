using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace SDRLink
{
    // Réglages sauvegardés dans %APPDATA%\SDRLink\settings.ini (clé=valeur).
    public class Settings
    {
        public int Interface;
        public string Brand = "Yaesu";
        public string Model = "FT-2000";
        public string Port = "COM1";
        public int Baud = 4800;
        public int StopBits = 2;
        public string Civ = "70";
        public bool Rts = true;
        public bool Dtr = false;
        public int OmniRigNumber = 1;
        public string TciHost = "127.0.0.1";
        public int TciPort = 40001;
        public int EsdrTciPort = 40001;    // serveur TCI d'ExpertSDR pour Mute / Volume ExpertSDR (clic droit sur Mute ExpertSDR)
        public string HrdHost = "127.0.0.1";
        public int HrdPort = 7809;
        public int HrdTxDetect;            // HrdRig.TxDetect
        public string HamlibHost = "127.0.0.1";
        public int HamlibPort = 4532;
        public bool HamlibLaunch = true;   // SDRLink lance rigctld lui-même (poste, port COM et vitesse ci-dessus)
        public string RigctldPath = "";
        public int HamlibModel;            // numéro de modèle Hamlib (0 = d'après le modèle choisi)
        public long TuneStepHz = 100;
        public List<string> FreqHistory = new List<string>();   // kHz, la plus récente en premier
        public int ViewMode;               // affichage : 0 normal, 1 réduit, 2 mini
        public int RedW, RedH;             // taille de la fenêtre en affichage réduit
        // Scan : 0 fréquences, 1 mémoires (mémoires à parcourir : Memory.Scan) ; durées en secondes.
        public int ScanType;
        public long ScanStartHz, ScanEndHz;
        public long ScanStepHz = 1000;
        public int ScanFreqDwell = 5, ScanMemDwell = 5;
        public List<long[]> ScanSkips = new List<long[]>();              // zones non scannées du scan de fréquences (10 au plus)
        public List<ScanProfile> ScanProfiles = new List<ScanProfile>(); // profils de scan nommés (10 au plus)
        public string RecFolder = "";      // dossier des enregistrements (bouton REC), vide = Musique\SDRLink
        // Carte son du poste (entrée Windows) enregistrée par REC quand seul le poste est écouté ; vide = aucune.
        public string RecCardId = "", RecCardName = "";
        public int RecColor = 0x000080;    // fond du bouton REC au repos (RVB), bleu marine par défaut
        public int RecWidth = 1;           // largeur du bouton REC : 1 étroit (2 fois la hauteur), 2 large (4 fois)
        public bool AutoReinit;            // Réinit Auto WebSDR : relance du WebSDR quand son son est perdu depuis AutoReinitSec secondes
        public int AutoReinitSec = 30;     // 10 à 60 s, par pas de 5 (comme PhoneSDRLink)
        public int FreeDvVolume = 100;     // volume de la parole FreeDV : curseur « Volume WebSDR » pendant la parole FreeDV (1.45)
        public int RigMonitorVolume = 100; // « Volume TX sur PC » : écoute du poste sur le PC (1.45)
        public bool FreeDvAuto, FreeDvV2;    // bouton FreeDV auto (décoché au départ) ; RADE V2 au lieu de V1 (clic droit)
        public bool SstvAuto;              // bouton SSTV (décoché au départ)
        public int SstvX = 0, SstvY = 1000; // position de la fenêtre SSTV sur la page, en millièmes de la place libre (0,1000 = en bas à gauche)
        public string DigiMode = "SSTV";   // mode numérique du bouton SSTV (SSTV, FT8, CW, WEFAX...) ; SstvAuto = décodeur en marche
        public int IaruRegion = 1;         // liste de mémoires « Modes numériques » : région IARU 1, 2 ou 3
        public string SearchPosition = "";  // recherche de WebSDR : locator ou coordonnées tapés (vide = position de l'ordinateur)
        public string DigiHi = "";         // surbrillance : textes à repérer dans le trafic décodé, séparés par des virgules
        public Dictionary<string, int> Digi = new Dictionary<string, int>();   // réglages des modes numériques (nom : valeur entière)
        public int DigiGet(string k, int def) { int v; return Digi.TryGetValue(k, out v) ? v : def; }
        public bool DigiGet(string k, bool def) { int v; return Digi.TryGetValue(k, out v) ? v != 0 : def; }
        public void DigiSet(string k, int v) { Digi[k] = v; }
        public void DigiSet(string k, bool v) { Digi[k] = v ? 1 : 0; }
        public bool RigMonitor;            // clic droit sur Mute TX : carte son du poste rejouée sur le PC quand Mute TX n'est pas actif
        public int DiversitySource = 1;   // Diversité : réception locale mise dans l'autre oreille (1 poste, 2 ExpertSDR par TCI, 3 HDSDR sous Windows 11, 4 second WebSDR)
        public string Div2Url = "";        // Diversité sur deux WebSDR (1.44) : adresse du second WebSDR, écouté sans être affiché
        public bool DiversitySwap;         // Diversité : WebSDR à droite (à gauche sinon)
        public string DiversityOut = "";   // Diversité : sortie (identifiant Windows ; vide = sortie par défaut ; pas dans l'interface)
        // Couleurs choisies par clic droit sur les boutons (clé → RVB) ; clé absente = couleur par défaut.
        public Dictionary<string, int> Colors = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool TopMost;
        public bool AutoConnect;
        public bool MuteOnTx = true;
        public int UnmuteDelayMs = 1000;
        // Réceptions laissées en marche pendant l'émission (0 WebSDR, 1 poste, 2 ExpertSDR, 3 HDSDR) ; vide = toutes coupées.
        public List<int> MuteTxSkip = new List<int>();
        public int Direction;
        public bool SyncMode = true;
        public long OffsetHz;
        public string LastUrl = "http://websdr.ewi.utwente.nl:8901/";
        public string Language = "fr";
        // Listes de favoris : noms modifiables et liste affichée dans la barre WebSDR.
        // Noms des listes de favoris (Favorite.MaxLists places ; seules les Favorite.ListCount premières sont utilisées).
        public string[] ListNames = DefaultListNames();
        public List<string> HiddenMessages = new List<string>();
        public MemoryBook Memories = new MemoryBook();   // mémoires de fréquences (bouton Mémoires)
        public List<MemHistEntry> MemHist = new List<MemHistEntry>();   // liste « Mem » du clavier de fréquence (20 au plus, la plus récente en tête)
        public int MemHistMin;                             // enregistrement automatique dans « Mem » après ce nombre de minutes sur la même fréquence (0 = jamais)
        public bool HideUrl;                               // adresse du WebSDR masquée par des étoiles (1.43)
        public int PageBtnPos = MainForm.PageBtnDefault;   // ronds partage, +, − sur la page (MainForm.PlacePageButtons)
        public readonly Dictionary<string, double> PageZooms = new Dictionary<string, double>(StringComparer.Ordinal);   // zoom de la page choisi par WebSDR (hôte:port)
        public int FontScale = 100;                        // taille du texte de la fenêtre principale, en %
        public const int FontScaleMin = 70, FontScaleMax = 200;   // types de messages de la barre d'état masqués (clic droit)

        static string[] DefaultListNames()
        {
            string[] n = new string[Favorite.MaxLists];
            for (int i = 0; i < n.Length; i++) n[i] = "Liste " + (i + 1);
            return n;
        }
        public int CurrentList;
        public int PttTimeoutSec;          // délai de sécurité du bouton PTT (0 = désactivé)
        public string CableDevice = "";    // sortie audio recevant le son du WebSDR (câble virtuel), vide = choix automatique
        public bool CableEnabled = true;
        public bool AutoNotch;             // notch automatique (comme le DNF) sur le son du WebSDR
        public bool NotchOffDigi = true;   // notch coupé quand un mode numérique décode ou que DIGI est le mode
        public bool NotchOffFdv = true;    // notch coupé quand FreeDV auto (V1 ou V2) est en marche
        public int NotchLevel = 1;         // force du notch, 1 (celui d'avant la 1.38) à 10
        // Pas de la molette et bande passante de réception retenus par mode (clés cw, ssb, am, fm, digi) ; bande passante 0 = filtre
        // d'origine du WebSDR. Les valeurs absentes prennent celles par défaut (ModeStep, ModeBw).
        public Dictionary<string, int> ModeSteps = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> ModeBws = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool DigiAutoQsy = true;    // choix d'un mode numérique : accord sur sa fréquence d'usage la plus proche
        public bool DigiDetached;          // fenêtre des modes numériques séparée de la page (déplaçable sur un autre écran)
        public bool DigiFull;              // fenêtre séparée en plein écran
        public Rectangle DigiWin = Rectangle.Empty;   // place de la fenêtre séparée (coordonnées de l'écran)
        public bool DarkMode;
        public string DarkBase = "", LightBase = "";   // couleurs de palette choisies (RRGGBB, vide = par défaut)
        public int Look;                                   // allure des boutons (Looks.Classic, Win311...), depuis la 1.33
        public bool PanelBottom;                           // panneau de commande sous la page du WebSDR, depuis la 1.35
        // Position et taille de la fenêtre en affichage normal (0 = jamais enregistrées).
        public int WinX, WinY, WinW, WinH;
        public bool WinMax;
        public int DefaultVolume = 100;   // volume des WebSDR qui ne sont pas en favoris
        // Recherche des WebSDR lancée une seule fois, au premier démarrage (pas encore de fichier de réglages).
        public bool FirstSearchDone;
        public bool CheckUpdates = true;   // recherche d'une nouvelle version sur GitHub au démarrage
        // Pseudo ou indicatif donné aux WebSDR qui en demandent un (KiwiSDR, websdr.org), jamais vide. Enregistré encodé
        // (%XX) pour être relu tel quel.
        public string Callsign = "SDRLink";
        // Coupures laissées de côté par le bouton ⇄ (0 WebSDR, 1 TX, 2 ExpertSDR, 3 HDSDR), deux au plus.
        public List<int> SwapIgnore = new List<int>();
        public const int MaxSwapIgnore = 2;
        public List<Favorite> Favorites = new List<Favorite>
        {
            new Favorite("Liste des WebSDR (websdr.org)", "http://www.websdr.org/"),
            new Favorite("Liste des KiwiSDR", "http://rx.linkfanel.net/"),
            new Favorite("Liste des OpenWebRX (ReceiverBook)", "https://www.receiverbook.de/?type=openwebrx"),
            new Favorite("Twente (NL) - websdr.org", "http://websdr.ewi.utwente.nl:8901/"),
            new Favorite("Wessex (UK) - UberSDR", "https://wessex.zapto.org/v2/"),
            new Favorite("French Alps - PhantomSDR", "http://linkz.ddns.net:8088/"),
        };

        public static string FilePath
        {
            get
            {
                string dir = Log.DataDir;
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.ini");
            }
        }

        // Réglages importés (bouton Import/Export) : mis de côté, ils remplacent settings.ini au démarrage suivant ;
        // l'ancien fichier est gardé dans settings.avant-import.ini.
        public static string PendingImportPath { get { return Path.Combine(Log.DataDir, "settings.import.ini"); } }
        public static string BeforeImportPath { get { return Path.Combine(Log.DataDir, "settings.avant-import.ini"); } }

        // Données du navigateur intégré (WebView2 : cache, cookies, stockage des sites), hors des réglages.
        public static string WebViewDataDir
        {
            get
            {
                return Environment.GetEnvironmentVariable("SDRLINK_DATA") != null
                    ? Path.Combine(Log.DataDir, "WebView2")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SDRLink", "WebView2");
            }
        }

        public static Settings Load()
        {
            SettingsReset.ApplyPending();   // réinitialisation demandée avant le redémarrage
            if (File.Exists(PendingImportPath))
            {
                try
                {
                    if (File.Exists(FilePath)) File.Copy(FilePath, BeforeImportPath, true);
                    File.Copy(PendingImportPath, FilePath, true);
                    File.Delete(PendingImportPath);
                    Log.Write("Réglages importés (anciens réglages gardés dans settings.avant-import.ini)");
                }
                catch (Exception ex) { Log.Write("Import des réglages impossible : " + ex.Message); }
            }
            Settings s = new Settings();
            Favorite.ListCount = 4;
            if (!File.Exists(FilePath)) return s;
            int listCount = 4;
            s.FirstSearchDone = true;   // réglages déjà présents : l'application a déjà démarré
            List<Favorite> favs = new List<Favorite>();
            foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "Interface": s.Interface = Int(v, 0); break;
                    case "Brand": s.Brand = v; break;
                    case "Model": s.Model = v; break;
                    case "Port": s.Port = v; break;
                    case "Baud": s.Baud = Int(v, 4800); break;
                    case "StopBits": s.StopBits = Int(v, 1); break;
                    case "Civ": s.Civ = v; break;
                    case "Rts": s.Rts = v == "1"; break;
                    case "Dtr": s.Dtr = v == "1"; break;
                    case "OmniRig": s.OmniRigNumber = Int(v, 1); break;
                    case "TciHost": s.TciHost = v; break;
                    case "TciPort": s.TciPort = Int(v, 40001); break;
                    case "EsdrTciPort": s.EsdrTciPort = Math.Max(1, Math.Min(65535, Int(v, 40001))); break;
                    case "HrdHost": s.HrdHost = v; break;
                    case "HrdPort": s.HrdPort = Int(v, 7809); break;
                    case "HrdVoxTx": if (v == "1") s.HrdTxDetect = 1; break;   // ancien réglage (case à cocher VOX)
                    case "HrdTxDetect": s.HrdTxDetect = Int(v, 0) > 0 ? 1 : 0; break;
                    case "HamlibHost": s.HamlibHost = v; break;
                    case "HamlibPort": s.HamlibPort = Int(v, 4532); break;
                    case "HamlibLaunch": s.HamlibLaunch = v == "1"; break;
                    case "RigctldPath": s.RigctldPath = v; break;
                    case "HamlibModel": s.HamlibModel = Int(v, 0); break;
                    case "TuneStep": s.TuneStepHz = Int(v, 100); break;
                    case "Compact": if (v == "1") s.ViewMode = 2; break;   // ancien réglage : l'affichage réduit est devenu « mini »
                    case "View": s.ViewMode = Math.Max(0, Math.Min(2, Int(v, 0))); break;
                    case "RedW": s.RedW = Int(v, 0); break;
                    case "RedH": s.RedH = Int(v, 0); break;
                    case "ScanType": s.ScanType = Int(v, 0) == 1 ? 1 : 0; break;
                    case "ScanStart": long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out s.ScanStartHz); break;
                    case "ScanEnd": long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out s.ScanEndHz); break;
                    case "ScanStep": { long st; if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out st) && st > 0) s.ScanStepHz = st; } break;
                    case "ScanFreqDwell": s.ScanFreqDwell = Math.Max(1, Int(v, 5)); break;
                    case "ScanMemDwell": s.ScanMemDwell = Math.Max(1, Int(v, 5)); break;
                    case "ScanSkip": { long[] z = ScanProfile.ParseSkip(v); if (z != null && s.ScanSkips.Count < ScanProfile.MaxSkips) s.ScanSkips.Add(z); } break;
                    case "ScanProfile": { ScanProfile p = ScanProfile.FromSetting(v); if (p != null && s.ScanProfiles.Count < ScanProfile.MaxProfiles) s.ScanProfiles.Add(p); } break;
                    case "RecFolder": s.RecFolder = v; break;
                    case "RecCardId": s.RecCardId = v; break;
                    case "RecCardName": s.RecCardName = v; break;
                    case "RecColor": s.RecColor = Int(v, 0x000080) & 0xFFFFFF; break;
                    case "RecWidth": s.RecWidth = Math.Max(1, Math.Min(2, Int(v, 1))); break;
                    case "AutoReinit": s.AutoReinit = v == "1"; break;
                    case "AutoReinitSec": s.AutoReinitSec = Math.Max(10, Math.Min(60, Int(v, 30) / 5 * 5)); break;
                    case "Color":   // clé:RRGGBB
                        {
                            int c = v.IndexOf(':'), rgb;
                            if (c > 0 && int.TryParse(v.Substring(c + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
                                s.Colors[v.Substring(0, c)] = rgb & 0xFFFFFF;
                        }
                        break;
                    case "TopMost": s.TopMost = v == "1"; break;
                    case "AutoConnect": s.AutoConnect = v == "1"; break;
                    case "MuteOnTx": s.MuteOnTx = v == "1"; break;
                    case "UnmuteDelay": s.UnmuteDelayMs = Int(v, 1000); break;
                    case "Direction": s.Direction = Int(v, 0); break;
                    case "SyncMode": s.SyncMode = v == "1"; break;
                    case "Offset": long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out s.OffsetHz); break;
                    case "LastUrl": s.LastUrl = v; break;
                    case "Fav": if (v.Length > 0) favs.Add(Favorite.FromSetting(v)); break;
                    case "Language": s.Language = Array.IndexOf(Lang.Codes, v) >= 0 ? v : "fr"; break;
                    case "ListCount": listCount = Math.Max(1, Math.Min(Favorite.MaxLists, Int(v, 4))); break;
                    case "CurrentList": s.CurrentList = Math.Max(0, Math.Min(Favorite.MaxLists - 1, Int(v, 0))); break;
                    case "Mem": { Memory m = Memory.FromSetting(v); if (m != null) s.Memories.Items.Add(m); } break;
                    case "MemRecent": { Memory m = Memory.FromSetting(v); if (m != null && s.Memories.Recent.Count < MemoryBook.RecentSize) s.Memories.Recent.Add(m); } break;
                    case "MemDigiPre":
                    {
                        string[] q = v.Split('|');
                        int pre;
                        if (q.Length == 3 && int.TryParse(q[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out pre) && pre >= 0 && pre <= Preamps.MaxCode)
                            s.Memories.DigiPreamps[q[0] + "|" + q[1]] = pre;
                    }
                    break;
                    case "MemDigiPwr":
                    {
                        string[] q = v.Split('|');
                        int pw;
                        if (q.Length == 3 && int.TryParse(q[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out pw) && pw >= 0 && pw <= Powers.LimitMax)
                            s.Memories.DigiPowers[q[0] + "|" + q[1]] = pw;
                    }
                    break;
                    case "HideUrl": s.HideUrl = v == "1"; break;
                    case "PageBtnPos": { int p = Int(v, MainForm.PageBtnDefault); s.PageBtnPos = (p & 7) < MainForm.PageBtnPlaces && p >= 0 && p < 16 ? p : MainForm.PageBtnDefault; } break;
                    case "PageZoom":
                    {
                        int bar = v.LastIndexOf('|');
                        double z;
                        if (bar > 0 && double.TryParse(v.Substring(bar + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out z) && z >= MainForm.ZoomMin && z <= MainForm.ZoomMax)
                            s.PageZooms[v.Substring(0, bar).ToLowerInvariant()] = z;
                    }
                    break;
                    case "PowerLimit": Powers.Limit = Math.Max(1, Math.Min(Powers.LimitMax, Int(v, 100))); break;
                    case "MemListCount": s.Memories.ListCount = Math.Max(1, Math.Min(Memory.MaxLists, Int(v, 1))); break;
                    case "MemCurrentList": s.Memories.CurrentList = Math.Max(0, Math.Min(Memory.MaxLists - 1, Int(v, 0))); break;
                    case "FontScale": s.FontScale = Math.Max(FontScaleMin, Math.Min(FontScaleMax, Int(v, 100))); break;
                    case "HideMsg": if (v.Length > 0 && !s.HiddenMessages.Contains(v)) s.HiddenMessages.Add(v); break;
                    case "PttTimeout": s.PttTimeoutSec = Math.Max(0, Int(v, 0)); break;
                    case "DigiDevice": case "CableDevice": s.CableDevice = v; break;
                    case "Cable": s.CableEnabled = v != "0"; break;
                    case "AutoNotch": s.AutoNotch = v == "1"; break;
                    case "NotchOffDigi": s.NotchOffDigi = v == "1"; break;
                    case "NotchOffFdv": s.NotchOffFdv = v == "1"; break;
                    case "NotchLevel": s.NotchLevel = Math.Max(1, Math.Min(10, Int(v, 1))); break;
                    case "ModeStep": { int c = v.IndexOf(':'); if (c > 0) s.ModeSteps[v.Substring(0, c)] = Math.Max(1, Int(v.Substring(c + 1), 500)); } break;
                    case "ModeBw": { int c = v.IndexOf(':'); if (c > 0) s.ModeBws[v.Substring(0, c)] = Math.Max(0, Math.Min(20000, Int(v.Substring(c + 1), 0))); } break;
                    case "DigiAutoQsy": s.DigiAutoQsy = v == "1"; break;
                    case "DigiDetached": s.DigiDetached = v == "1"; break;
                    case "DigiFull": s.DigiFull = v == "1"; break;
                    case "DigiWin": { string[] q = v.Split(','); if (q.Length == 4) s.DigiWin = new Rectangle(Int(q[0], 0), Int(q[1], 0), Int(q[2], 0), Int(q[3], 0)); } break;
                    case "DarkMode": s.DarkMode = v == "1"; break;
                    case "DarkBase": s.DarkBase = v; break;
                    case "LightBase": s.LightBase = v; break;
                    case "Look": s.Look = Math.Max(0, Math.Min(Looks.Count - 1, Int(v, 0))); break;
                    case "PanelBottom": s.PanelBottom = v == "1"; break;
                    case "WinX": s.WinX = Int(v, 0); break;
                    case "WinY": s.WinY = Int(v, 0); break;
                    case "WinW": s.WinW = Int(v, 0); break;
                    case "WinH": s.WinH = Int(v, 0); break;
                    case "WinMax": s.WinMax = v == "1"; break;
                    case "Volume": s.DefaultVolume = Math.Max(0, Math.Min(100, Int(v, 100))); break;
                    case "FirstSearch": s.FirstSearchDone = v != "0"; break;
                    case "CheckUpdates": s.CheckUpdates = v != "0"; break;
                    case "RigMonitor": s.RigMonitor = v == "1"; break;
                    case "DiversitySource": s.DiversitySource = Math.Max(1, Math.Min(4, Int(v, 1))); break;
                    case "Div2Url": s.Div2Url = v; break;
                    case "DiversitySwap": s.DiversitySwap = v == "1"; break;
                    case "DiversityOut": s.DiversityOut = v; break;
                    case "FreeDvAuto": s.FreeDvAuto = v == "1"; break;
                    case "FreeDvV2": s.FreeDvV2 = v == "1"; break;
                    case "FreeDvVolume": s.FreeDvVolume = Math.Max(0, Math.Min(100, Int(v, 100))); break;
                    case "RigMonitorVolume": s.RigMonitorVolume = Math.Max(0, Math.Min(100, Int(v, 100))); break;
                    case "SstvAuto": s.SstvAuto = v == "1"; break;
                    case "SstvX": s.SstvX = Math.Max(0, Math.Min(1000, Int(v, 0))); break;
                    case "SstvY": s.SstvY = Math.Max(0, Math.Min(1000, Int(v, 1000))); break;
                    case "DigiMode": s.DigiMode = v; break;
                    case "IaruRegion": s.IaruRegion = Math.Max(1, Math.Min(3, Int(v, 1))); break;
                    case "SearchPosition": s.SearchPosition = v; break;
                    case "MemHist": { MemHistEntry e = MemHistEntry.FromSetting(v); if (e != null && s.MemHist.Count < MemHistEntry.Size) s.MemHist.Add(e); } break;
                    case "MemHistMin": { int m = Int(v, 0); s.MemHistMin = Array.IndexOf(FreqKeypad.AutoMinutes, m) >= 0 ? m : 0; } break;
                    case "DigiHi": try { s.DigiHi = Uri.UnescapeDataString(v); } catch { } break;
                    case "Digi":
                        int dc = v.IndexOf(':');
                        if (dc > 0) s.Digi[v.Substring(0, dc)] = Int(v.Substring(dc + 1), 0);
                        break;
                    case "Callsign":
                        string cs = "";
                        try { cs = WebSdrBridge.Clean(Uri.UnescapeDataString(v)); } catch (UriFormatException) { }
                        s.Callsign = cs != "" ? cs : "SDRLink";
                        break;
                    case "SwapIgnore":
                        // « 0,3 » ; une seule valeur dans les versions d'avant la 1.15, -1 pour aucune.
                        s.SwapIgnore.Clear();
                        foreach (string part in v.Split(','))
                        {
                            int sk = Int(part.Trim(), -1);
                            if (sk >= 0 && sk <= 3 && !s.SwapIgnore.Contains(sk) && s.SwapIgnore.Count < MaxSwapIgnore) s.SwapIgnore.Add(sk);
                        }
                        s.SwapIgnore.Sort();
                        break;
                    case "MuteTxSkip":
                        s.MuteTxSkip.Clear();
                        foreach (string part in v.Split(','))
                        {
                            int mk = Int(part.Trim(), -1);
                            if (mk >= 0 && mk <= 3 && !s.MuteTxSkip.Contains(mk)) s.MuteTxSkip.Add(mk);
                        }
                        s.MuteTxSkip.Sort();
                        break;
                    case "Freq": if (s.FreqHistory.Count < 100) s.FreqHistory.Add(v); break;
                    default:
                        // List1 à List10 : noms des listes de favoris.
                        int li;
                        if (k.StartsWith("List") && int.TryParse(k.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out li)
                            && li >= 1 && li <= Favorite.MaxLists && v.Length > 0) s.ListNames[li - 1] = v;
                        // MemList1 à MemList10 : noms des listes de mémoires.
                        else if (k.StartsWith("MemList") && int.TryParse(k.Substring(7), NumberStyles.None, CultureInfo.InvariantCulture, out li)
                            && li >= 1 && li <= Memory.MaxLists && v.Length > 0) s.Memories.ListNames[li - 1] = v;
                        break;
                }
            }
            if (favs.Count > 0) s.Favorites = favs;
            // Nombre de listes : celui enregistré (4 avant la 1.8), agrandi si un favori est rangé plus loin.
            foreach (Favorite f in s.Favorites) listCount = Math.Max(listCount, f.List + 1);
            Favorite.ListCount = listCount;
            s.CurrentList = Math.Min(s.CurrentList, listCount - 1);
            foreach (Memory m in s.Memories.Items) s.Memories.ListCount = Math.Max(s.Memories.ListCount, m.List + 1);
            s.Memories.CurrentList = Math.Min(s.Memories.CurrentList, s.Memories.ListCount - 1);
            return s;
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            Line(sb, "Interface", Interface);
            Line(sb, "Brand", Brand);
            Line(sb, "Model", Model);
            Line(sb, "Port", Port);
            Line(sb, "Baud", Baud);
            Line(sb, "StopBits", StopBits);
            Line(sb, "Civ", Civ);
            Line(sb, "Rts", Rts ? 1 : 0);
            Line(sb, "Dtr", Dtr ? 1 : 0);
            Line(sb, "OmniRig", OmniRigNumber);
            Line(sb, "TciHost", TciHost);
            Line(sb, "TciPort", TciPort);
            Line(sb, "EsdrTciPort", EsdrTciPort);
            Line(sb, "HrdHost", HrdHost);
            Line(sb, "HrdPort", HrdPort);
            Line(sb, "HrdTxDetect", HrdTxDetect);
            Line(sb, "HamlibHost", HamlibHost);
            Line(sb, "HamlibPort", HamlibPort);
            Line(sb, "HamlibLaunch", HamlibLaunch ? 1 : 0);
            Line(sb, "RigctldPath", RigctldPath);
            Line(sb, "HamlibModel", HamlibModel);
            Line(sb, "TuneStep", TuneStepHz);
            Line(sb, "View", ViewMode);
            Line(sb, "RedW", RedW);
            Line(sb, "RedH", RedH);
            Line(sb, "ScanType", ScanType);
            Line(sb, "ScanStart", ScanStartHz);
            Line(sb, "ScanEnd", ScanEndHz);
            Line(sb, "ScanStep", ScanStepHz);
            Line(sb, "ScanFreqDwell", ScanFreqDwell);
            Line(sb, "ScanMemDwell", ScanMemDwell);
            foreach (long[] z in ScanSkips) Line(sb, "ScanSkip", ScanProfile.SkipText(z));
            foreach (ScanProfile p in ScanProfiles) Line(sb, "ScanProfile", p.ToSetting());
            Line(sb, "RecFolder", RecFolder);
            Line(sb, "RecCardId", RecCardId);
            Line(sb, "RecCardName", RecCardName);
            Line(sb, "RecColor", RecColor);
            Line(sb, "RecWidth", RecWidth);
            Line(sb, "AutoReinit", AutoReinit ? 1 : 0);
            Line(sb, "AutoReinitSec", AutoReinitSec);
            foreach (KeyValuePair<string, int> c in Colors) Line(sb, "Color", c.Key + ":" + c.Value.ToString("X6"));
            Line(sb, "TopMost", TopMost ? 1 : 0);
            Line(sb, "AutoConnect", AutoConnect ? 1 : 0);
            Line(sb, "MuteOnTx", MuteOnTx ? 1 : 0);
            Line(sb, "UnmuteDelay", UnmuteDelayMs);
            Line(sb, "Direction", Direction);
            Line(sb, "SyncMode", SyncMode ? 1 : 0);
            Line(sb, "Offset", OffsetHz);
            Line(sb, "LastUrl", LastUrl);
            Line(sb, "Language", Language);
            Line(sb, "ListCount", Favorite.ListCount);
            for (int i = 0; i < Favorite.ListCount; i++) Line(sb, "List" + (i + 1), ListNames[i]);
            foreach (string h in HiddenMessages) Line(sb, "HideMsg", h);
            Line(sb, "FontScale", FontScale);
            Line(sb, "MemListCount", Memories.ListCount);
            Line(sb, "MemCurrentList", Memories.CurrentList);
            for (int i = 0; i < Memories.ListCount; i++) Line(sb, "MemList" + (i + 1), Memories.ListNames[i]);
            foreach (Memory m in Memories.Items) Line(sb, "Mem", m.ToSetting());
            foreach (Memory m in Memories.Recent) Line(sb, "MemRecent", m.ToSetting());
            foreach (KeyValuePair<string, int> p in Memories.DigiPreamps) Line(sb, "MemDigiPre", p.Key + "|" + p.Value.ToString(CultureInfo.InvariantCulture));
            foreach (KeyValuePair<string, int> p in Memories.DigiPowers) Line(sb, "MemDigiPwr", p.Key + "|" + p.Value.ToString(CultureInfo.InvariantCulture));
            Line(sb, "PowerLimit", Powers.Limit);
            Line(sb, "HideUrl", HideUrl ? 1 : 0);
            Line(sb, "PageBtnPos", PageBtnPos);
            foreach (KeyValuePair<string, double> p in PageZooms) Line(sb, "PageZoom", p.Key + "|" + p.Value.ToString("0.###", CultureInfo.InvariantCulture));
            Line(sb, "CurrentList", CurrentList);
            Line(sb, "PttTimeout", PttTimeoutSec);
            Line(sb, "CableDevice", CableDevice);
            Line(sb, "Cable", CableEnabled ? 1 : 0);
            Line(sb, "AutoNotch", AutoNotch ? 1 : 0);
            Line(sb, "NotchOffDigi", NotchOffDigi ? 1 : 0);
            Line(sb, "NotchOffFdv", NotchOffFdv ? 1 : 0);
            Line(sb, "NotchLevel", NotchLevel);
            foreach (KeyValuePair<string, int> p in ModeSteps) Line(sb, "ModeStep", p.Key + ":" + p.Value);
            foreach (KeyValuePair<string, int> p in ModeBws) Line(sb, "ModeBw", p.Key + ":" + p.Value);
            Line(sb, "DigiAutoQsy", DigiAutoQsy ? 1 : 0);
            Line(sb, "DigiDetached", DigiDetached ? 1 : 0);
            Line(sb, "DigiFull", DigiFull ? 1 : 0);
            if (!DigiWin.IsEmpty) Line(sb, "DigiWin", DigiWin.X + "," + DigiWin.Y + "," + DigiWin.Width + "," + DigiWin.Height);
            Line(sb, "DarkMode", DarkMode ? 1 : 0);
            Line(sb, "DarkBase", DarkBase);
            Line(sb, "LightBase", LightBase);
            Line(sb, "Look", Look);
            Line(sb, "PanelBottom", PanelBottom ? 1 : 0);
            Line(sb, "WinX", WinX);
            Line(sb, "WinY", WinY);
            Line(sb, "WinW", WinW);
            Line(sb, "WinH", WinH);
            Line(sb, "WinMax", WinMax ? 1 : 0);
            Line(sb, "Volume", DefaultVolume);
            Line(sb, "FirstSearch", FirstSearchDone ? 1 : 0);
            Line(sb, "CheckUpdates", CheckUpdates ? 1 : 0);
            Line(sb, "RigMonitor", RigMonitor ? 1 : 0);
            Line(sb, "DiversitySource", DiversitySource);
            Line(sb, "DiversitySwap", DiversitySwap ? 1 : 0);
            if (Div2Url.Length > 0) Line(sb, "Div2Url", Div2Url);
            if (DiversityOut.Length > 0) Line(sb, "DiversityOut", DiversityOut);
            Line(sb, "FreeDvAuto", FreeDvAuto ? 1 : 0);
            Line(sb, "FreeDvV2", FreeDvV2 ? 1 : 0);
            Line(sb, "FreeDvVolume", FreeDvVolume);
            Line(sb, "RigMonitorVolume", RigMonitorVolume);
            Line(sb, "SstvAuto", SstvAuto ? 1 : 0);
            Line(sb, "SstvX", SstvX);
            Line(sb, "SstvY", SstvY);
            Line(sb, "DigiMode", DigiMode);
            Line(sb, "IaruRegion", IaruRegion);
            Line(sb, "SearchPosition", SearchPosition);
            Line(sb, "MemHistMin", MemHistMin);
            foreach (MemHistEntry e in MemHist) Line(sb, "MemHist", e.ToSetting());
            Line(sb, "DigiHi", Uri.EscapeDataString(DigiHi));
            foreach (KeyValuePair<string, int> d in Digi) Line(sb, "Digi", d.Key + ":" + d.Value.ToString(CultureInfo.InvariantCulture));
            Line(sb, "Callsign", Uri.EscapeDataString(Callsign));
            Line(sb, "SwapIgnore", SwapIgnore.Count == 0 ? "-1" : string.Join(",", SwapIgnore.ConvertAll(k => k.ToString()).ToArray()));
            Line(sb, "MuteTxSkip", MuteTxSkip.Count == 0 ? "-1" : string.Join(",", MuteTxSkip.ConvertAll(k => k.ToString()).ToArray()));
            foreach (Favorite f in Favorites) Line(sb, "Fav", f.ToSetting());
            foreach (string f in FreqHistory) Line(sb, "Freq", f);
            try { File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8); } catch { }
        }

        // Réglage de scan du moment, sous forme de profil (mémoires cochées comprises).
        public ScanProfile CurrentScan(string name)
        {
            ScanProfile p = new ScanProfile { Name = name, Type = ScanType, StartHz = ScanStartHz, EndHz = ScanEndHz, StepHz = ScanStepHz,
                                              FreqDwell = ScanFreqDwell, MemDwell = ScanMemDwell };
            foreach (long[] z in ScanSkips) p.Skips.Add(new long[] { z[0], z[1] });
            foreach (Memory m in Memories.Items) if (m.Scan) p.Mems.Add(ScanProfile.MemKey(m));
            return p;
        }

        // Le profil devient le réglage de scan du moment (les mémoires qu'il ne cite pas sont décochées).
        public void UseScan(ScanProfile p)
        {
            ScanType = p.Type;
            ScanStartHz = p.StartHz;
            ScanEndHz = p.EndHz;
            ScanStepHz = p.StepHz;
            ScanFreqDwell = p.FreqDwell;
            ScanMemDwell = p.MemDwell;
            ScanSkips.Clear();
            foreach (long[] z in p.Skips) ScanSkips.Add(new long[] { z[0], z[1] });
            foreach (Memory m in Memories.Items) m.Scan = p.Mems.Contains(ScanProfile.MemKey(m));
        }

        static void Line(StringBuilder sb, string k, object v)
        {
            sb.Append(k).Append('=').Append(Convert.ToString(v, CultureInfo.InvariantCulture)).Append("\r\n");
        }

        static int Int(string v, int def)
        {
            int r;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }
    }
}
