using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace SDRLink
{
    // Ham Radio Deluxe : serveur IP (port 7809 par défaut), messages texte UTF-16 encadrés d'un en-tête de 16 octets.
    public class HrdRig : IRig
    {
        const uint Sanity1 = 0x1234ABCD, Sanity2 = 0xABCD1234;

        // Boutons surveillés pour détecter l'émission. Le serveur IP de HRD n'a pas de commande "état d'émission" :
        // sur certains postes (FT-2000), le PTT pédale/micro n'apparaît que par un changement d'état du bouton VOX.
        public enum TxDetect { Buttons = 0, ButtonsAndVox = 1 }
        public static readonly string[] TxDetectLabels = { "Boutons MOX/TX/PTT", "Boutons MOX/VOX/TX/PTT" };

        readonly string host;
        readonly int port;
        readonly TxDetect txDetect;
        TcpClient client;
        NetworkStream stream;
        string prefix = "";
        string modeDropdown = "Mode";
        string[] modeList = new string[0];
        string[] allButtons = new string[0];
        string[] txButtons = new string[0];
        string voxBaseline;            // valeur du bouton VOX en réception, relevée à la connexion
        string lastUnknownMode;

        public HrdRig(string host, int port, TxDetect txDetect)
        {
            this.host = host;
            this.port = port;
            this.txDetect = txDetect;
        }

        public void Open()
        {
            client = new TcpClient();
            client.NoDelay = true;
            IAsyncResult ar = client.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(5000) || !client.Connected)
                throw new Exception(Lang.F("Pas de serveur HRD sur {0}:{1} (activez le serveur IP dans HRD Rig Control).", "No HRD server at {0}:{1} (enable the IP server in HRD Rig Control).", host, port));
            client.EndConnect(ar);
            client.ReceiveTimeout = 3000;
            stream = client.GetStream();

            Probe();
        }

        // Liste des modes, boutons d'émission et curseur de volume du poste affiché dans HRD. Refait quand on
        // change de poste dans HRD sans reconnecter SDRLink (FT-2000, puis FTdx10...).
        void Probe()
        {
            prefix = "";
            afSlider = null;
            afMin = 0;
            afMax = -1;
            voxBaseline = null;
            string ctx = Command("get context");
            if (!string.IsNullOrEmpty(ctx) && ctx.Trim() != "0") prefix = "[" + ctx.Trim() + "] ";

            // Selon le poste, HRD nomme la liste "Mode" ou "Main Mode" (FT-2000 : Main Mode, Sub Mode, Mode Key...).
            string exact = null, main = null, first = null;
            foreach (string name in Distinct(Command("get dropdowns")))
            {
                if (name.Equals("Mode", StringComparison.OrdinalIgnoreCase)) exact = name;
                else if (name.Equals("Main Mode", StringComparison.OrdinalIgnoreCase)) main = name;
                else if (first == null && name.IndexOf("Mode", StringComparison.OrdinalIgnoreCase) >= 0) first = name;
            }
            modeDropdown = exact ?? main ?? first ?? "Mode";
            string list = Command("get dropdown-list {" + modeDropdown + "}");
            if (!string.IsNullOrEmpty(list)) modeList = list.Split(',');

            allButtons = Distinct(Command("get buttons"));
            List<string> found = new List<string>();
            foreach (string wanted in new[] { "MOX", "TX", "PTT" })
                if (Array.IndexOf(allButtons, wanted) >= 0) found.Add(wanted);
            txButtons = found.ToArray();
            if (txDetect == TxDetect.ButtonsAndVox && Array.IndexOf(allButtons, "VOX") >= 0)
                voxBaseline = (Command("get button-select VOX") ?? "").Trim();

            ProbeAfSlider();
            ProbePreamp();
            Log.Write("HRD " + Command("get radio") + " : liste des modes [" + modeDropdown + "], boutons émission [" +
                string.Join(", ", txButtons) + "]" + (voxBaseline != null ? ", VOX en réception = " + voxBaseline : ""));
        }

        public void Close()
        {
            if (client != null) { try { client.Close(); } catch { } client = null; stream = null; }
        }

        public void Dispose() { Close(); }

        DateTime nextRadioCheck;

        public bool Read(out long freqHz, out string mode)
        {
            mode = null;
            // Toutes les 5 s : poste changé dans HRD ? On relit alors ses modes, boutons et curseur de volume.
            if (DateTime.UtcNow >= nextRadioCheck)
            {
                nextRadioCheck = DateTime.UtcNow.AddSeconds(5);
                string radio = (Command("get radio") ?? "").Trim();
                if (radio != "" && radio != radioName)
                {
                    Log.Write("HRD : changement de poste [" + radioName + "] -> [" + radio + "]");
                    Probe();
                }
            }
            PreampCheck();
            long fa;
            if (Vfo == 1) { if (!ReadAB(out fa, out freqHz)) return false; }
            else
            {
                string f = Command("get frequency");
                if (!long.TryParse((f ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out freqHz) || freqHz <= 0)
                    return false;
            }
            string m = Command("get dropdown-text {" + modeDropdown + "}");
            if (m != null)
            {
                int colon = m.IndexOf(':');
                mode = FromHrd(colon >= 0 ? m.Substring(colon + 1) : m);
            }
            if (mode == null && m != lastUnknownMode)
            {
                lastUnknownMode = m;
                Log.Write("HRD mode non reconnu : [" + m + "]");
            }
            return true;
        }

        // Émission si l'un des boutons surveillés est enfoncé, ou si le bouton VOX a quitté sa valeur de réception.
        public int ReadTx()
        {
            int result = -1;
            if (voxBaseline != null)
            {
                if ((Command("get button-select VOX") ?? "").Trim() != voxBaseline) return 1;
                result = 0;
            }
            foreach (string b in txButtons)
            {
                string r = (Command("get button-select " + b) ?? "").Trim();
                if (r == "1") return 1;
                if (r == "0") result = 0;
            }
            return result;
        }

        // Volume du poste via le curseur "AF gain" de HRD : commandes "get/set slider-pos <poste> <curseur> [valeur]"
        // et "get slider-range <poste> <curseur>" (espaces remplacés par '~'). Le format est vérifié à la connexion
        // par des lectures seules ; s'il n'est pas reconnu, le volume reste indisponible.
        string afSlider, radioName;
        int afMin, afMax = -1;

        void ProbeAfSlider()
        {
            radioName = (Command("get radio") ?? "").Trim();
            string[] sliders = Distinct(Command("get sliders"));
            foreach (string pref in new[] { "AF gain (main)", "AF gain", "AF Gain", "Main AF gain", "AF gain main", "Volume" })
                foreach (string s in sliders)
                    if (afSlider == null && s.Equals(pref, StringComparison.OrdinalIgnoreCase)) afSlider = s;
            // Sinon : un curseur "AF..." (AF gain, AF-Gain, AF level...) ou "Volume...", hors réception secondaire (sub).
            if (afSlider == null)
                foreach (string s in sliders)
                {
                    string l = s.ToLowerInvariant();
                    if (l.Contains("sub") || l.Contains("rf") || l.Contains("mic") || l.Contains("monitor")) continue;
                    if (l.StartsWith("af") || l.Contains(" af") || l.Contains("volume")) { afSlider = s; break; }
                }
            if (afSlider == null || radioName == "")
            {
                Log.Write("HRD : pas de curseur de volume pour [" + radioName + "], curseurs disponibles : " + string.Join(", ", sliders));
                return;
            }

            string range = Command("get slider-range " + SliderArgs()) ?? "";
            string pos = Command("get slider-pos " + SliderArgs()) ?? "";
            Log.Write("HRD curseur volume [" + afSlider + "] : plage = [" + range + "], position = [" + pos + "]");
            int[] r = Ints(range);
            if (r.Length >= 2 && r[1] > r[0]) { afMin = r[0]; afMax = r[1]; }
            else if (Ints(pos).Length >= 1) { afMin = 0; afMax = 255; }   // plage non donnée : échelle habituelle de HRD
        }

        string SliderArgs() { return radioName.Replace(' ', '~') + " " + afSlider.Replace(' ', '~'); }

        static int[] Ints(string s)
        {
            List<int> r = new List<int>();
            foreach (string p in (s ?? "").Split(',', ' '))
            {
                int v;
                if (int.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) r.Add(v);
            }
            return r.ToArray();
        }

        public int ReadAfGain()
        {
            if (afMax <= afMin) return -1;
            int[] v = Ints(Command("get slider-pos " + SliderArgs()));
            if (v.Length == 0) return -1;
            return (int)Math.Round((Math.Max(afMin, Math.Min(afMax, v[0])) - afMin) * 100.0 / (afMax - afMin));
        }

        // Préampli (1.41) : le serveur IP de HRD n'a pas de commande préampli ; on passe par les commandes de la fenêtre
        // de HRD pour ce poste, trouvées à la connexion : une liste déroulante (« Preamp », « IPO »…) dont les choix
        // ressemblent à IPO / AMP1 / AMP2 / OFF / ON, sinon des boutons (« IPO », « AMP 1 », « AMP 2 », « P.AMP »…).
        // Les noms dépendent de la définition du poste dans HRD : ce qui est trouvé est écrit dans le journal.
        PreampSet preamps;
        string preDropdown;          // liste déroulante utilisée, ou null (boutons)
        string btnIpo, btnAmp1, btnAmp2, btnPre, btnPre1, btnPre2;
        public PreampSet Preamps { get { return preamps; } }

        // Puissance (1.42) : non pilotée par HRD : l'échelle de son curseur (« RF power percentage », 0-255 sur le FT-2000) n'est pas
        // documentée, et une erreur pourrait donner plus de watts que demandé.
        public PowerRange Power { get { return null; } }

        // VFO commandé (1.49) : voir IRig.Vfo.
        public int Vfo { get; set; }
        public bool CanVfoB { get { return true; } }

        // « get frequencies » : « A-B » en Hz (VFO B, 1.49).
        bool ReadAB(out long a, out long b)
        {
            a = b = 0;
            string[] p = (Command("get frequencies") ?? "").Trim().Split('-');
            return p.Length == 2 && long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out a) &&
                   long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out b) && b > 0;
        }
        public void SetPower(int watts) { }

        static string Norm(string s)
        {
            StringBuilder b = new StringBuilder();
            foreach (char c in (s ?? "").ToUpperInvariant()) if (char.IsLetterOrDigit(c)) b.Append(c);
            return b.ToString();
        }

        // Code d'un libellé HRD : -1 si ce n'est pas un réglage de préampli reconnu.
        static int PreCode(string text)
        {
            string n = Norm(text);
            if (n == "IPO2") return SDRLink.Preamps.Ipo2;
            if (n == "IPO" || n == "IPO1" || n == "OFF" || n == "IPOON" || n == "PREOFF" || n == "PAMPOFF") return SDRLink.Preamps.Off;
            if (n == "AMP2" || n == "PAMP2" || n == "PRE2" || n == "PREAMP2") return SDRLink.Preamps.Amp2;
            if (n == "AMP1" || n == "PAMP1" || n == "PRE1" || n == "PREAMP1" || n == "AMP" || n == "PAMP" || n == "ON" || n == "PRE" || n == "PREON" || n == "IPOOFF") return SDRLink.Preamps.Amp1;
            return -1;
        }

        void ProbePreamp()
        {
            preamps = null; preDropdown = null; btnIpo = btnAmp1 = btnAmp2 = btnPre = btnPre1 = btnPre2 = null; preTarget = -1;
            foreach (string name in Distinct(Command("get dropdowns")))
            {
                string n = Norm(name);
                bool named = n.Contains("PREAMP") || n.Contains("IPO") || n == "PAMP" || n == "AMP" || n == "RFAMP" || n == "PRE" || n.StartsWith("PREMAIN");
                // Liste reconnue par son nom, ou par ses choix : au moins un IPO / AMP / P.AMP / PRE (FTdx5000 dans HRD :
                // « IPO 1, Amp 1, Amp 2, IPO 2 » = PA00, PA01, PA02, PA03). Une liste OFF / ON seule ne suffit pas (NB, NR…).
                if (!named && n.Contains("MODE")) continue;
                string[] items = (Command("get dropdown-list {" + name + "}") ?? "").Split(',');
                if (!named)
                {
                    bool pre = false;
                    foreach (string it in items) { string q = Norm(it); if (PreCode(it) >= 0 && (q.Contains("IPO") || q.Contains("AMP") || q.StartsWith("PRE"))) pre = true; }
                    if (!pre) continue;
                }
                List<int> codes = new List<int>();
                List<string> labels = new List<string>(), wire = new List<string>();
                for (int i = 0; i < items.Length; i++)
                {
                    int c = PreCode(items[i]);
                    if (c < 0 || codes.Contains(c)) continue;
                    codes.Add(c); labels.Add(items[i].Trim()); wire.Add(i.ToString(CultureInfo.InvariantCulture) + "|" + items[i].Trim());
                }
                if (codes.Count < 2 || !codes.Contains(SDRLink.Preamps.Off)) continue;
                preDropdown = name;
                preamps = new PreampSet(radioName, codes.ToArray(), labels.ToArray(), wire.ToArray());
                Log.Write("HRD : préampli par la liste [" + name + "] : " + string.Join(", ", items));
                return;
            }
            foreach (string b in allButtons)
            {
                string n = Norm(b);
                if (n == "IPO") btnIpo = b;
                else if (n == "AMP1" || n == "PAMP1") btnAmp1 = b;
                else if (n == "AMP2" || n == "PAMP2") btnAmp2 = b;
                else if (n == "PAMP" || n == "PREAMP" || n == "PRE" || n == "AMP") btnPre = b;
                else if (n == "PRE1" || n == "PRE1A") btnPre1 = b;     // « Pre 1 A » : récepteur principal (A)
                else if (n == "PRE2" || n == "PRE2A") btnPre2 = b;
                else if (n == "PREMAIN" && btnPre == null) btnPre = b; // « Pre (MAIN) » (TS-990 dans HRD = PA00 / PA01)
            }
            // FT-2000 (et d'autres Yaesu) dans HRD : deux boutons « Pre 1 » et « Pre 2 » (relevé de F5PBG dans WSJT-WWA,
            // vérifié sur le vrai poste) : IPO = les deux éteints, AMP1 = Pre 1 seul, AMP2 = Pre 2 seul.
            if (btnPre1 != null && btnPre2 != null)
            {
                bool yaesu = (radioName ?? "").ToUpperInvariant().Contains("FT");
                preamps = new PreampSet(radioName, new[] { SDRLink.Preamps.Off, SDRLink.Preamps.Amp1, SDRLink.Preamps.Amp2 },
                    yaesu ? new[] { "IPO", "AMP1", "AMP2" } : new[] { "OFF", btnPre1, btnPre2 }, null);
                Log.Write("HRD : préampli par les boutons [" + btnPre1 + "] et [" + btnPre2 + "]");
                return;
            }
            List<int> bc = new List<int>();
            List<string> bl = new List<string>();
            if (btnIpo != null || btnPre != null) { bc.Add(SDRLink.Preamps.Off); bl.Add(btnIpo != null ? "IPO" : "OFF"); }
            if (btnAmp1 != null || btnPre != null || (btnIpo != null && btnAmp2 == null)) { bc.Add(SDRLink.Preamps.Amp1); bl.Add(btnAmp1 ?? btnPre ?? "AMP"); }
            if (btnAmp2 != null) { bc.Add(SDRLink.Preamps.Amp2); bl.Add(btnAmp2); }
            if (bc.Count >= 2 && bc.Contains(SDRLink.Preamps.Off))
            {
                preamps = new PreampSet(radioName, bc.ToArray(), bl.ToArray(), null);
                Log.Write("HRD : préampli par les boutons [" + btnIpo + "] [" + btnAmp1 + "] [" + btnAmp2 + "] [" + btnPre + "]");
            }
            else Log.Write("HRD : pas de commande de préampli reconnue pour ce poste (boutons : " + string.Join(", ", allButtons) + ")");
        }

        int preTarget = -1, prePass;
        DateTime preNext;
        static readonly int[] PreWaits = { 0, 500, 1000, 1500 };

        bool ButtonOn(string b) { return (Command("get button-select " + b.Replace(' ', '~')) ?? "").Trim() == "1"; }

        void PreampPass()
        {
            bool want1 = preTarget == SDRLink.Preamps.Amp1, want2 = preTarget == SDRLink.Preamps.Amp2;
            bool was1 = ButtonOn(btnPre1), was2 = ButtonOn(btnPre2);
            string trace = "HRD : préampli " + preamps.LabelOf(preTarget) + ", passage " + prePass + " : Pre1=" + (was1 ? 1 : 0) + " Pre2=" + (was2 ? 1 : 0);
            // Le poste n'a qu'une position de préampli : allumer Pre 1 (ou Pre 2) l'y place directement, sans IPO, mais HRD
            // peut continuer à afficher l'AUTRE bouton allumé ; l'éteindre enverrait le poste en IPO. On ne l'éteint donc
            // JAMAIS pour aller vers AMP1 / AMP2 ; seul l'aller vers IPO éteint le bouton allumé (correctif de F5PBG dans
            // WSJT-WWA, vérifié sur le FT-2000). Réussi = bouton voulu allumé (ou les deux éteints pour IPO).
            bool ok = !want1 && !want2 ? !was1 && !was2 : want1 ? was1 : was2;
            if (ok)
            {
                if (prePass >= 1) { Log.Write(trace + " (stable)"); preTarget = -1; return; }
            }
            else
            {
                if (!want1 && !want2)
                {
                    if (was1) Command("set button-select " + btnPre1.Replace(' ', '~') + " 0");
                    if (was2) Command("set button-select " + btnPre2.Replace(' ', '~') + " 0");
                }
                else Command("set button-select " + (want1 ? btnPre1 : btnPre2).Replace(' ', '~') + " 1");
                trace += " -> envoyé";
            }
            Log.Write(trace);
            if (++prePass >= PreWaits.Length) { preTarget = -1; return; }
            preNext = DateTime.UtcNow.AddMilliseconds(PreWaits[prePass]);
        }

        // Relectures programmées du préampli (boutons Pre 1 / Pre 2), à chaque lecture du poste.
        void PreampCheck()
        {
            if (preTarget >= 0 && btnPre1 != null && DateTime.UtcNow >= preNext) PreampPass();
        }

        public void SetPreamp(int code)
        {
            if (preamps == null) return;
            int c = preamps.Map(code);
            if (btnPre1 != null && btnPre2 != null)
            {
                // État lu avant d'écrire (une commande redondante peut faire basculer le poste), puis relu à +0,5 s, +1,5 s
                // et +3 s (PreampCheck, appelé par Read) : le poste recharge sa mémoire de préampli par bande après un QSY.
                preTarget = c;
                prePass = 0;
                PreampPass();
                return;
            }
            if (preDropdown != null)
            {
                string w = preamps.WireOf(code);
                int bar = w.IndexOf('|');
                Command("set dropdown " + preDropdown.Replace(' ', '~') + " " + w.Substring(bar + 1).Replace(' ', '~') + " " + w.Substring(0, bar));
                return;
            }
            // Boutons : IPO enfoncé = sans préampli ; AMP1 / AMP2 / P.AMP enfoncé = préampli.
            if (c == SDRLink.Preamps.Off)
            {
                if (btnIpo != null) Command("set button-select " + btnIpo + " 1");
                else Command("set button-select " + btnPre.Replace(' ', '~') + " 0");
                return;
            }
            string target = c == SDRLink.Preamps.Amp2 ? btnAmp2 : btnAmp1 ?? btnPre;
            if (target != null) Command("set button-select " + target.Replace(' ', '~') + " 1");
            else if (btnIpo != null) Command("set button-select " + btnIpo + " 0");
        }

        public void SetAfGain(int percent)
        {
            if (afMax <= afMin) return;
            int v = afMin + (int)Math.Round(Math.Max(0, Math.Min(100, percent)) * (afMax - afMin) / 100.0);
            Command("set slider-pos " + SliderArgs() + " " + v.ToString(CultureInfo.InvariantCulture));
        }

        // Même action que le bouton MOX de HRD (ou TX s'il n'y a pas de MOX).
        public void SetPtt(bool tx)
        {
            string b = Array.IndexOf(allButtons, "MOX") >= 0 ? "MOX" : Array.IndexOf(allButtons, "TX") >= 0 ? "TX" : null;
            if (b == null) { Log.Write("HRD : aucun bouton MOX/TX pour passer en émission"); return; }
            Command("set button-select " + b + " " + (tx ? "1" : "0"));
        }

        public void SetFreq(long freqHz)
        {
            long a, b;
            if (Vfo == 1 && ReadAB(out a, out b))
            {
                Command("set frequencies-hz " + a.ToString(CultureInfo.InvariantCulture) + " " + freqHz.ToString(CultureInfo.InvariantCulture));
                return;
            }
            Command("set frequency-hz " + freqHz.ToString(CultureInfo.InvariantCulture));
        }

        public void SetMode(string mode)
        {
            foreach (string candidate in Candidates(mode))
            {
                for (int i = 0; i < modeList.Length; i++)
                {
                    if (modeList[i].Trim().Equals(candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        Command("set dropdown " + modeDropdown.Replace(' ', '~') + " " + modeList[i].Trim().Replace(' ', '~') + " " + i);
                        return;
                    }
                }
            }
        }

        string Command(string text)
        {
            byte[] payload = Encoding.Unicode.GetBytes(prefix + text + "\0");
            byte[] msg = new byte[16 + payload.Length];
            BitConverter.GetBytes((uint)msg.Length).CopyTo(msg, 0);
            BitConverter.GetBytes(Sanity1).CopyTo(msg, 4);
            BitConverter.GetBytes(Sanity2).CopyTo(msg, 8);
            Array.Copy(payload, 0, msg, 16, payload.Length);
            stream.Write(msg, 0, msg.Length);

            byte[] header = ReadExact(16);
            uint size = BitConverter.ToUInt32(header, 0);
            if (BitConverter.ToUInt32(header, 4) != Sanity1 || size < 16 || size > 1 << 20)
                throw new IOException(Lang.T("Réponse HRD invalide", "Invalid HRD answer"));
            byte[] body = ReadExact((int)size - 16);
            return Encoding.Unicode.GetString(body).TrimEnd('\0');
        }

        byte[] ReadExact(int n)
        {
            byte[] b = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = stream.Read(b, got, n - got);
                if (r <= 0) throw new IOException(Lang.T("Connexion HRD fermée", "HRD connection closed"));
                got += r;
            }
            return b;
        }

        static string[] Distinct(string commaList)
        {
            List<string> r = new List<string>();
            foreach (string x in (commaList ?? "").Split(','))
            {
                string t = x.Trim();
                if (t.Length > 0 && !r.Contains(t)) r.Add(t);
            }
            return r.ToArray();
        }

        static string FromHrd(string s)
        {
            string m = s.Trim().ToUpperInvariant();
            // FT-990/1000/1000MP : « PKT » est côté LSB, « PKT-FM » est de la FM.
            if (m == "PKT") return Modes.DATAL;
            if (m.StartsWith("PKT") && m.Contains("FM")) return Modes.FM;
            if (m.Contains("PKT") || m.Contains("DATA") || m.EndsWith("-D") || m.StartsWith("DIG"))
                return (m.Contains("L")) && !m.Contains("USB") ? Modes.DATAL : Modes.DATAU;
            if (m.StartsWith("RTTY") || m.StartsWith("FSK"))
                return m.EndsWith("-R") || m.EndsWith("-U") || m.EndsWith("USB") ? Modes.RTTYR : Modes.RTTY;
            if (m.StartsWith("CW"))
                return (m == "CW-R" || m == "CWR" || m == "CW-L" || m == "CW-LSB") ? Modes.CWR : Modes.CW;
            if (m.StartsWith("LSB")) return Modes.LSB;
            if (m.StartsWith("USB")) return Modes.USB;
            if (m.Contains("FM")) return Modes.FM;
            if (m.Contains("AM")) return Modes.AM;
            return null;
        }

        static string[] Candidates(string mode)
        {
            switch (mode)
            {
                case Modes.LSB: return new[] { "LSB" };
                case Modes.USB: return new[] { "USB" };
                case Modes.CW: return new[] { "CW", "CW-U", "CW-USB" };
                case Modes.CWR: return new[] { "CW-R", "CWR", "CW-L", "CW-LSB" };
                case Modes.AM: return new[] { "AM" };
                case Modes.FM: return new[] { "FM", "FM-N" };
                case Modes.RTTY: return new[] { "RTTY", "RTTY-L", "RTTY-LSB", "FSK" };
                case Modes.RTTYR: return new[] { "RTTY-R", "RTTY-U", "RTTY-USB", "FSK-R" };
                case Modes.DATAL: return new[] { "PKT-L", "DATA-L", "LSB-D", "DATA-LSB", "PKT-LSB", "PKT" };
                case Modes.DATAU: return new[] { "PKT-U", "DATA-U", "USB-D", "DATA-USB", "PKT-USB" };
            }
            return new string[0];
        }
    }
}
