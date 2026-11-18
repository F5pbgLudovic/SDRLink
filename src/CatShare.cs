using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace SDRLink
{
    // Découpage des commandes CAT : texte terminé par « ; » (Yaesu récents, Kenwood, Elecraft), trames CI-V FE FE … FD
    // (Icom, Xiegu), ou ancien protocole binaire Yaesu (commandes de 5 octets).
    public enum ShareFraming { Text, Civ, Binary }

    // Partage du poste sur un port COM virtuel (1.49) : SDRLink tient le vrai port COM du poste et l'une des extrémités
    // d'une paire com0com ; un autre logiciel (WSJT-X, logiciel de journal…) ouvre l'autre extrémité comme si c'était le poste.
    // Ses commandes sont passées telles quelles au poste par le fil du poste (RigWorker), entre deux interrogations de SDRLink :
    // les réponses ne se mélangent jamais. Seulement pour le CAT direct (port COM) : OmniRig, TCI, HRD et Hamlib se partagent déjà.
    public class CatShare : IDisposable
    {
        readonly string com;
        readonly int baud;
        readonly ShareFraming framing;
        SerialPort vp;
        readonly List<byte> inbuf = new List<byte>();
        readonly Queue<byte[]> queue = new Queue<byte[]>();
        int lastByteTick;
        public readonly AutoResetEvent Signal = new AutoResetEvent(false);
        public int Commands;     // commandes de l'autre logiciel passées au poste

        public CatShare(string com, int baud, ShareFraming framing)
        {
            this.com = com;
            this.baud = baud;
            this.framing = framing;
        }

        public string Port { get { return com; } }

        public void Open()
        {
            vp = new SerialPort(com, baud, Parity.None, 8, StopBits.One);
            vp.Handshake = Handshake.None;
            vp.WriteTimeout = 500;
            vp.DataReceived += delegate { OnData(); };
            vp.Open();
            // Certains logiciels attendent CTS / DSR (reliés à RTS / DTR de ce bout par com0com) avant de parler.
            vp.RtsEnable = true;
            vp.DtrEnable = true;
            Log.Write("Partage : port COM virtuel " + com + " ouvert (" + framing + ")");
        }

        public void Close()
        {
            SerialPort p = vp;
            vp = null;
            if (p != null) { try { p.Close(); } catch { } try { p.Dispose(); } catch { } Log.Write("Partage : port COM virtuel " + com + " fermé"); }
        }

        public void Dispose() { Close(); }

        void OnData()
        {
            SerialPort p = vp;
            if (p == null) return;
            byte[] b;
            try
            {
                int n = p.BytesToRead;
                if (n <= 0) return;
                b = new byte[n];
                n = p.Read(b, 0, n);
                if (n < b.Length) Array.Resize(ref b, n);
            }
            catch { return; }
            List<byte[]> cmds;
            lock (inbuf)
            {
                // Morceau resté seul plus d'une demi-seconde : reste d'une commande coupée, oublié (protocole binaire surtout).
                if (inbuf.Count > 0 && Environment.TickCount - lastByteTick > 500) inbuf.Clear();
                lastByteTick = Environment.TickCount;
                inbuf.AddRange(b);
                cmds = Split(framing, inbuf);
            }
            if (cmds.Count == 0) return;
            lock (queue) foreach (byte[] c in cmds) { if (queue.Count < 200) queue.Enqueue(c); }
            Signal.Set();
        }

        // Commandes complètes retirées du tampon (le reste, incomplet, y reste).
        public static List<byte[]> Split(ShareFraming f, List<byte> buf)
        {
            List<byte[]> l = new List<byte[]>();
            if (f == ShareFraming.Text)
            {
                int i;
                while ((i = buf.IndexOf((byte)';')) >= 0)
                {
                    int s = 0;
                    while (s < i && (buf[s] == '\r' || buf[s] == '\n' || buf[s] == ' ')) s++;
                    if (i - s >= 1) l.Add(buf.GetRange(s, i - s + 1).ToArray());
                    buf.RemoveRange(0, i + 1);
                }
                if (buf.Count > 300) buf.Clear();
            }
            else if (f == ShareFraming.Civ)
            {
                while (true)
                {
                    int st = -1;
                    for (int k = 0; k + 1 < buf.Count; k++) if (buf[k] == 0xFE && buf[k + 1] == 0xFE) { st = k; break; }
                    if (st < 0) { if (buf.Count > 0 && buf[buf.Count - 1] == 0xFE) buf.RemoveRange(0, buf.Count - 1); else buf.Clear(); break; }
                    if (st > 0) buf.RemoveRange(0, st);
                    int e = buf.IndexOf(0xFD);
                    if (e < 0) { if (buf.Count > 600) buf.Clear(); break; }
                    int body = 2;
                    while (body < e && buf[body] == 0xFE) body++;
                    if (e - body >= 3) { List<byte> fr = new List<byte>(); fr.Add(0xFE); fr.Add(0xFE); fr.AddRange(buf.GetRange(body, e - body + 1)); l.Add(fr.ToArray()); }
                    buf.RemoveRange(0, e + 1);
                }
            }
            else
            {
                while (buf.Count >= 5) { l.Add(buf.GetRange(0, 5).ToArray()); buf.RemoveRange(0, 5); }
            }
            return l;
        }

        // Fil du poste : prochaine commande en attente (null : aucune).
        public byte[] Next()
        {
            lock (queue) return queue.Count > 0 ? queue.Dequeue() : null;
        }

        public void Reply(byte[] r)
        {
            SerialPort p = vp;
            if (p == null || r == null || r.Length == 0) return;
            try { p.Write(r, 0, r.Length); } catch { }
        }

        // Passe une commande au poste et rend sa réponse (null : aucune). write envoie au poste ; read(ms) lit un octet du poste
        // (-1 au bout de ms millisecondes sans rien).
        //  - Texte : une question (« FA; », « MD0; », « IF; »…) attend la réponse qui commence par les mêmes deux lettres
        //    (ou « ?; ») ; les messages spontanés reçus avant (mode AI) sont rendus aussi. Un réglage (« FA014074000; »)
        //    n'attend presque pas : le poste ne répond rien, sauf erreur.
        //  - CI-V : l'écho de la trame sur le bus est sauté ; la réponse adressée à l'expéditeur (données, FB ou FA) est rendue.
        //  - Binaire : tout ce qui arrive jusqu'à un silence (réponses de longueur variable selon le poste et la commande).
        public static byte[] Exchange(ShareFraming f, byte[] cmd, Action<byte[]> write, Func<int, int> read)
        {
            write(cmd);
            List<byte> outb = new List<byte>();
            if (f == ShareFraming.Text)
            {
                string head = cmd.Length >= 3 ? ((char)cmd[0]).ToString() + (char)cmd[1] : "";
                bool question = cmd.Length <= 4 || (cmd.Length == 5 && cmd[2] == '0' && !char.IsDigit((char)cmd[3]));
                List<byte> tok = new List<byte>();
                int deadline = Environment.TickCount + (question ? 450 : 60);
                while (Environment.TickCount - deadline < 0)
                {
                    int b = read(Math.Max(1, deadline - Environment.TickCount));
                    if (b < 0) break;
                    tok.Add((byte)b);
                    if (b != ';') continue;
                    outb.AddRange(tok);
                    string t = System.Text.Encoding.ASCII.GetString(tok.ToArray()).Trim();
                    tok.Clear();
                    if (!question || t == "?;" || t.StartsWith(head, StringComparison.OrdinalIgnoreCase)) break;
                }
            }
            else if (f == ShareFraming.Civ)
            {
                if (cmd.Length < 6) return null;
                byte to = cmd[2], from = cmd[3];
                List<byte> fr = new List<byte>();
                int deadline = Environment.TickCount + 500;
                while (Environment.TickCount - deadline < 0)
                {
                    int b = read(Math.Max(1, deadline - Environment.TickCount));
                    if (b < 0) break;
                    fr.Add((byte)b);
                    if (b != 0xFD) continue;
                    List<byte[]> frames = Split(ShareFraming.Civ, fr);
                    bool done = false;
                    foreach (byte[] x in frames)
                    {
                        if (x.Length < 6) continue;
                        if (x[2] == to && x[3] == from) continue;          // écho de la commande
                        if (x[2] == from && x[3] == to) { outb.AddRange(x); done = true; }
                    }
                    if (done) break;
                }
            }
            else
            {
                int b = read(300);
                while (b >= 0 && outb.Count < 2048) { outb.Add((byte)b); b = read(80); }
            }
            return outb.Count > 0 ? outb.ToArray() : null;
        }
    }
}
