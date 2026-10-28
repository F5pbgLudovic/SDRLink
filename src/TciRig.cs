using System;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace SDRLink
{
    // Liaison TCI permanente avec ExpertSDR pour sa coupure ("mute") et son volume ("volume", -60 à 0 dB), quand
    // ExpertSDR ne passe pas par le son de Windows (son sorti par le poste lui-même, MB1, ou par ASIO).
    // Connexion à part, à côté de celle du poste ; l'état est suivi en direct (réglages faits dans ExpertSDR compris).
    public class ExpertSdrTci : IDisposable
    {
        ClientWebSocket ws;
        volatile bool running;
        readonly object sendLock = new object(), sync = new object();
        bool? mute;
        int volumeDb = int.MinValue;

        // null si le serveur TCI ne répond pas.
        public static ExpertSdrTci TryConnect(string host, int port)
        {
            ExpertSdrTci t = new ExpertSdrTci();
            t.ws = new ClientWebSocket();
            t.ws.Options.Proxy = null;   // liaison locale : pas de recherche de proxy (WPAD), qui peut prendre plusieurs secondes
            try
            {
                // Hors du fil de l'interface : appelé depuis WinForms, Wait() sur l'opération réseau se bloquerait.
                Uri uri = new Uri("ws://" + host + ":" + port + "/");
                if (!System.Threading.Tasks.Task.Run(delegate { return t.ws.ConnectAsync(uri, CancellationToken.None); }).Wait(1500)
                    || t.ws.State != WebSocketState.Open) { t.Dispose(); return null; }
            }
            catch (Exception) { t.Dispose(); return null; }
            t.running = true;
            new Thread(t.ReceiveLoop) { IsBackground = true }.Start();
            t.Send("mute;");
            t.Send("volume;");
            return t;
        }

        public bool Connected { get { return running && ws != null && ws.State == WebSocketState.Open; } }

        // État annoncé par ExpertSDR (null tant qu'il ne l'a pas donné).
        public bool? Mute { get { lock (sync) return mute; } }

        // Volume en % (même échelle que la liaison TCI du poste), -1 s'il n'est pas encore connu.
        public int VolumePercent
        {
            get { lock (sync) return volumeDb == int.MinValue ? -1 : (int)Math.Round((Math.Max(-60, Math.Min(0, volumeDb)) + 60) * 100 / 60.0); }
        }

        public void SetMute(bool on)
        {
            lock (sync) mute = on;
            Send("mute:" + (on ? "true" : "false") + ";");
        }

        public void SetVolumePercent(int percent)
        {
            int db = (int)Math.Round(Math.Max(0, Math.Min(100, percent)) * 60 / 100.0) - 60;
            lock (sync) volumeDb = db;
            Send("volume:" + db.ToString(CultureInfo.InvariantCulture) + ";");
        }

        void Send(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            try { lock (sendLock) System.Threading.Tasks.Task.Run(delegate { return ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None); }).Wait(2000); }
            catch (Exception) { running = false; }
        }

        void ReceiveLoop()
        {
            byte[] buf = new byte[65536];
            StringBuilder msg = new StringBuilder();
            try
            {
                while (running && ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    if (r.MessageType != WebSocketMessageType.Text) continue;
                    msg.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    if (!r.EndOfMessage) continue;
                    foreach (string cmd in msg.ToString().Split(';'))
                    {
                        string c = cmd.Trim().ToLowerInvariant();
                        double db;
                        if (c.StartsWith("mute:")) lock (sync) mute = c.Substring(5).Trim() == "true";
                        else if (c.StartsWith("volume:") && double.TryParse(c.Substring(7), NumberStyles.Float, CultureInfo.InvariantCulture, out db))
                            lock (sync) volumeDb = (int)Math.Round(db);
                    }
                    msg.Length = 0;
                }
            }
            catch { }
            running = false;
        }

        public void Dispose()
        {
            running = false;
            if (ws == null) return;
            ClientWebSocket w = ws;
            try { if (w.State == WebSocketState.Open) System.Threading.Tasks.Task.Run(delegate { return w.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }).Wait(1000); } catch { }
            w.Dispose();
        }
    }

    // Protocole TCI (Expert Electronics SunSDR, Thetis, ...) : WebSocket texte, commandes "nom:args;".
    public class TciRig : IRig
    {
        readonly string url;
        ClientWebSocket ws;
        Thread receiver;
        volatile bool running;
        readonly object sync = new object();
        long freq;
        string mode;
        int tx = -1;
        int volumeDb = int.MinValue;   // volume principal en dB (-60 à 0), annoncé par le serveur

        public TciRig(string host, int port) { url = "ws://" + host + ":" + port + "/"; }

        public void Open()
        {
            ws = new ClientWebSocket();
            if (!ws.ConnectAsync(new Uri(url), CancellationToken.None).Wait(5000))
                throw new Exception(Lang.T("Pas de serveur TCI sur ", "No TCI server at ") + url);
            running = true;
            receiver = new Thread(ReceiveLoop);
            receiver.IsBackground = true;
            receiver.Start();
            Send("vfo:0,0;");
            Send("modulation:0;");
            Send("trx:0;");
            Send("volume;");
        }

        public void Close()
        {
            running = false;
            if (ws != null)
            {
                try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(1000); } catch { }
                ws.Dispose();
                ws = null;
            }
        }

        public void Dispose() { Close(); }

        public bool Read(out long freqHz, out string m)
        {
            if (ws == null || ws.State != WebSocketState.Open) throw new Exception(Lang.T("Connexion TCI perdue", "TCI connection lost"));
            lock (sync) { freqHz = freq; m = mode; }
            return freqHz > 0;
        }

        // L'état d'émission arrive spontanément ("trx:0,true;"), on renvoie la dernière valeur reçue.
        public int ReadTx()
        {
            lock (sync) return tx;
        }

        // Volume principal TCI : "volume:dB;" de -60 dB (0 %) à 0 dB (100 %).
        public int ReadAfGain()
        {
            lock (sync) return volumeDb == int.MinValue ? -1 : (int)Math.Round((Math.Max(-60, Math.Min(0, volumeDb)) + 60) * 100 / 60.0);
        }

        public void SetAfGain(int percent)
        {
            int db = (int)Math.Round(Math.Max(0, Math.Min(100, percent)) * 60 / 100.0) - 60;
            lock (sync) volumeDb = db;
            Send("volume:" + db.ToString(CultureInfo.InvariantCulture) + ";");
        }

        public void SetPtt(bool on)
        {
            Send("trx:0," + (on ? "true" : "false") + ";");
        }

        public void SetFreq(long freqHz)
        {
            Send("vfo:0,0," + freqHz.ToString(CultureInfo.InvariantCulture) + ";");
        }

        public void SetMode(string m)
        {
            string t = ToTci(m);
            if (t != null) Send("modulation:0," + t + ";");
        }

        void Send(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None).Wait(2000);
        }

        void ReceiveLoop()
        {
            byte[] buf = new byte[65536];
            StringBuilder msg = new StringBuilder();
            try
            {
                while (running && ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    if (r.MessageType != WebSocketMessageType.Text) continue;
                    msg.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    if (!r.EndOfMessage) continue;
                    foreach (string cmd in msg.ToString().Split(';')) Handle(cmd.Trim());
                    msg.Length = 0;
                }
            }
            catch { }
        }

        void Handle(string cmd)
        {
            int colon = cmd.IndexOf(':');
            if (colon < 0) return;
            string name = cmd.Substring(0, colon).ToLowerInvariant();
            string[] a = cmd.Substring(colon + 1).Split(',');
            if (name == "vfo" && a.Length >= 3 && a[0] == "0" && a[1] == "0")
            {
                long f;
                if (long.TryParse(a[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out f))
                    lock (sync) freq = f;
            }
            else if (name == "modulation" && a.Length >= 2 && a[0] == "0")
            {
                lock (sync) mode = FromTci(a[1]);
            }
            else if (name == "volume" && a.Length >= 1)
            {
                double db;
                if (double.TryParse(a[0], NumberStyles.Float, CultureInfo.InvariantCulture, out db))
                    lock (sync) volumeDb = (int)Math.Round(db);
            }
            else if (name == "trx" && a.Length >= 2 && a[0] == "0")
            {
                string v = a[1].Trim().ToLowerInvariant();
                lock (sync) tx = v == "true" ? 1 : v == "false" ? 0 : tx;
            }
        }

        static string FromTci(string m)
        {
            switch (m.Trim().ToLowerInvariant())
            {
                case "lsb": return Modes.LSB;
                case "usb": return Modes.USB;
                case "cw": return Modes.CW;
                case "am": case "sam": case "dsb": return Modes.AM;
                case "nfm": case "wfm": return Modes.FM;
                case "digl": return Modes.DATAL;
                case "digu": return Modes.DATAU;
            }
            return null;
        }

        static string ToTci(string m)
        {
            switch (m)
            {
                case Modes.LSB: return "lsb";
                case Modes.USB: return "usb";
                case Modes.CW: case Modes.CWR: return "cw";
                case Modes.AM: return "am";
                case Modes.FM: return "nfm";
                case Modes.DATAL: case Modes.RTTY: return "digl";
                case Modes.DATAU: case Modes.RTTYR: return "digu";
            }
            return null;
        }
    }
}
