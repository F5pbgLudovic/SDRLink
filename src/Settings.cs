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
        public string HrdHost = "127.0.0.1";
        public int HrdPort = 7809;
        public int HrdTxDetect;            // HrdRig.TxDetect
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
        public string[] ListNames = { "Liste 1", "Liste 2", "Liste 3", "Liste 4" };
        public int CurrentList;
        public int PttTimeoutSec;          // délai de sécurité du bouton PTT (0 = désactivé)
        public string DigiDevice = "";     // sortie audio du mode DIGI (câble virtuel), vide = choix automatique
        public bool DarkMode;
        public string DarkBase = "", LightBase = "";   // couleurs de palette choisies (RRGGBB, vide = par défaut)
        // Position et taille de la fenêtre en affichage normal (0 = jamais enregistrées).
        public int WinX, WinY, WinW, WinH;
        public bool WinMax;
        public int DefaultVolume = 100;   // volume des WebSDR qui ne sont pas en favoris
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
            if (!File.Exists(FilePath)) return s;
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
                    case "HrdHost": s.HrdHost = v; break;
                    case "HrdPort": s.HrdPort = Int(v, 7809); break;
                    case "HrdVoxTx": if (v == "1") s.HrdTxDetect = 1; break;   // ancien réglage (case à cocher VOX)
                    case "HrdTxDetect": s.HrdTxDetect = Int(v, 0) > 0 ? 1 : 0; break;
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
                    case "Language": s.Language = v == "en" ? "en" : "fr"; break;
                    case "List1": case "List2": case "List3": case "List4":
                        if (v.Length > 0) s.ListNames[k[4] - '1'] = v;
                        break;
                    case "CurrentList": s.CurrentList = Math.Max(0, Math.Min(3, Int(v, 0))); break;
                    case "PttTimeout": s.PttTimeoutSec = Math.Max(0, Int(v, 0)); break;
                    case "DigiDevice": s.DigiDevice = v; break;
                    case "DarkMode": s.DarkMode = v == "1"; break;
                    case "DarkBase": s.DarkBase = v; break;
                    case "LightBase": s.LightBase = v; break;
                    case "WinX": s.WinX = Int(v, 0); break;
                    case "WinY": s.WinY = Int(v, 0); break;
                    case "WinW": s.WinW = Int(v, 0); break;
                    case "WinH": s.WinH = Int(v, 0); break;
                    case "WinMax": s.WinMax = v == "1"; break;
                    case "Volume": s.DefaultVolume = Math.Max(0, Math.Min(100, Int(v, 100))); break;
                    case "Freq": if (s.FreqHistory.Count < 100) s.FreqHistory.Add(v); break;
                }
            }
            if (favs.Count > 0) s.Favorites = favs;
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
            Line(sb, "HrdHost", HrdHost);
            Line(sb, "HrdPort", HrdPort);
            Line(sb, "HrdTxDetect", HrdTxDetect);
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
            for (int i = 0; i < ListNames.Length; i++) Line(sb, "List" + (i + 1), ListNames[i]);
            Line(sb, "CurrentList", CurrentList);
            Line(sb, "PttTimeout", PttTimeoutSec);
            Line(sb, "DigiDevice", DigiDevice);
            Line(sb, "DarkMode", DarkMode ? 1 : 0);
            Line(sb, "DarkBase", DarkBase);
            Line(sb, "LightBase", LightBase);
            Line(sb, "WinX", WinX);
            Line(sb, "WinY", WinY);
            Line(sb, "WinW", WinW);
            Line(sb, "WinH", WinH);
            Line(sb, "WinMax", WinMax ? 1 : 0);
            Line(sb, "Volume", DefaultVolume);
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
