using System;
using System.Collections.Generic;
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
        public bool Compact;
        public bool TopMost;
        public bool AutoConnect;
        public bool MuteOnTx = true;
        public int UnmuteDelayMs = 1000;
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
        public bool DarkMode;
        public string DarkBase = "", LightBase = "";   // couleurs de palette choisies (RRGGBB, vide = par défaut)
        // Position et taille de la fenêtre en affichage normal (0 = jamais enregistrées).
        public int WinX, WinY, WinW, WinH;
        public bool WinMax;
        public int DefaultVolume = 100;   // volume des WebSDR qui ne sont pas en favoris
        // Recherche des WebSDR lancée une seule fois, au premier démarrage (pas encore de fichier de réglages).
        public bool FirstSearchDone;
        public bool CheckUpdates = true;   // recherche d'une nouvelle version sur GitHub au démarrage
        public int SwapIgnore = -1;        // coupure laissée de côté par le bouton ⇄ (-1 aucune, 0 WebSDR, 1 TX, 2 ExpertSDR)
        public List<Favorite> Favorites = new List<Favorite>
        {
            new Favorite("Liste des WebSDR (websdr.org)", "http://www.websdr.org/"),
            new Favorite("Liste des KiwiSDR", "http://rx.linkfanel.net/"),
            new Favorite("Liste des OpenWebRX (ReceiverBook)", "https://www.receiverbook.de/?type=openwebrx"),
            new Favorite("Twente (NL) - websdr.org", "http://websdr.ewi.utwente.nl:8901/"),
            new Favorite("Wessex (UK) - UberSDR", "https://wessex.zapto.org/v2/"),
            new Favorite("French Alps - PhantomSDR", "http://linkz.ddns.net:8088/"),
        };

        static string FilePath
        {
            get
            {
                string dir = Log.DataDir;
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static Settings Load()
        {
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
                    case "Compact": s.Compact = v == "1"; break;
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
                    case "MemListCount": s.Memories.ListCount = Math.Max(1, Math.Min(Memory.MaxLists, Int(v, 1))); break;
                    case "MemCurrentList": s.Memories.CurrentList = Math.Max(0, Math.Min(Memory.MaxLists - 1, Int(v, 0))); break;
                    case "FontScale": s.FontScale = Math.Max(FontScaleMin, Math.Min(FontScaleMax, Int(v, 100))); break;
                    case "HideMsg": if (v.Length > 0 && !s.HiddenMessages.Contains(v)) s.HiddenMessages.Add(v); break;
                    case "PttTimeout": s.PttTimeoutSec = Math.Max(0, Int(v, 0)); break;
                    case "DigiDevice": case "CableDevice": s.CableDevice = v; break;
                    case "Cable": s.CableEnabled = v != "0"; break;
                    case "AutoNotch": s.AutoNotch = v == "1"; break;
                    case "DarkMode": s.DarkMode = v == "1"; break;
                    case "DarkBase": s.DarkBase = v; break;
                    case "LightBase": s.LightBase = v; break;
                    case "WinX": s.WinX = Int(v, 0); break;
                    case "WinY": s.WinY = Int(v, 0); break;
                    case "WinW": s.WinW = Int(v, 0); break;
                    case "WinH": s.WinH = Int(v, 0); break;
                    case "WinMax": s.WinMax = v == "1"; break;
                    case "Volume": s.DefaultVolume = Math.Max(0, Math.Min(100, Int(v, 100))); break;
                    case "FirstSearch": s.FirstSearchDone = v != "0"; break;
                    case "CheckUpdates": s.CheckUpdates = v != "0"; break;
                    case "SwapIgnore": s.SwapIgnore = Math.Max(-1, Math.Min(2, Int(v, -1))); break;
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
            Line(sb, "Compact", Compact ? 1 : 0);
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
            Line(sb, "CurrentList", CurrentList);
            Line(sb, "PttTimeout", PttTimeoutSec);
            Line(sb, "CableDevice", CableDevice);
            Line(sb, "Cable", CableEnabled ? 1 : 0);
            Line(sb, "AutoNotch", AutoNotch ? 1 : 0);
            Line(sb, "DarkMode", DarkMode ? 1 : 0);
            Line(sb, "DarkBase", DarkBase);
            Line(sb, "LightBase", LightBase);
            Line(sb, "WinX", WinX);
            Line(sb, "WinY", WinY);
            Line(sb, "WinW", WinW);
            Line(sb, "WinH", WinH);
            Line(sb, "WinMax", WinMax ? 1 : 0);
            Line(sb, "Volume", DefaultVolume);
            Line(sb, "FirstSearch", FirstSearchDone ? 1 : 0);
            Line(sb, "CheckUpdates", CheckUpdates ? 1 : 0);
            Line(sb, "SwapIgnore", SwapIgnore);
            foreach (Favorite f in Favorites) Line(sb, "Fav", f.ToSetting());
            foreach (string f in FreqHistory) Line(sb, "Freq", f);
            try { File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8); } catch { }
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
