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
                throw new Exception(Lang.T("Pas de serveur HRD sur ", "No HRD server at ") + host + ":" + port + Lang.T(" (activez le serveur IP dans HRD Rig Control).", " (enable the IP server in HRD Rig Control)."));
            client.EndConnect(ar);
            client.ReceiveTimeout = 3000;
            stream = client.GetStream();

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
            Log.Write("HRD " + Command("get radio") + " : liste des modes [" + modeDropdown + "], boutons émission [" +
                string.Join(", ", txButtons) + "]" + (voxBaseline != null ? ", VOX en réception = " + voxBaseline : ""));
        }

        public void Close()
        {
            if (client != null) { try { client.Close(); } catch { } client = null; stream = null; }
        }

        public void Dispose() { Close(); }

        public bool Read(out long freqHz, out string mode)
        {
            mode = null;
            string f = Command("get frequency");
            if (!long.TryParse((f ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out freqHz) || freqHz <= 0)
                return false;
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
            foreach (string pref in new[] { "AF gain (main)", "AF gain", "AF Gain", "Volume" })
                foreach (string s in sliders)
                    if (afSlider == null && s.Equals(pref, StringComparison.OrdinalIgnoreCase)) afSlider = s;
            if (afSlider == null)
                foreach (string s in sliders)
                    if (s.IndexOf("AF", StringComparison.OrdinalIgnoreCase) >= 0 && s.IndexOf("gain", StringComparison.OrdinalIgnoreCase) >= 0
                        && s.IndexOf("sub", StringComparison.OrdinalIgnoreCase) < 0) { afSlider = s; break; }
            if (afSlider == null || radioName == "") { Log.Write("HRD : pas de curseur de volume (AF gain) pour ce poste"); return; }

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
                case Modes.DATAL: return new[] { "PKT-L", "DATA-L", "LSB-D", "DATA-LSB", "PKT-LSB" };
                case Modes.DATAU: return new[] { "PKT-U", "DATA-U", "USB-D", "DATA-USB", "PKT-USB" };
            }
            return new string[0];
        }
    }
}
