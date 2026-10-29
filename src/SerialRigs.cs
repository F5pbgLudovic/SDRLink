using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace SDRLink
{
    public class SerialSettings
    {
        public string Port = "COM1";
        public int Baud = 4800;
        public int StopBits = 1;
        public bool Rts = true;
        public bool Dtr = false;
    }

    // Base commune des postes pilotés en direct par port COM.
    public abstract class SerialRig : IRig
    {
        protected SerialPort port;
        protected readonly SerialSettings settings;

        protected SerialRig(SerialSettings s) { settings = s; }

        public virtual void Open()
        {
            port = new SerialPort(settings.Port, settings.Baud, Parity.None, 8,
                settings.StopBits == 2 ? StopBits.Two : StopBits.One);
            port.Handshake = Handshake.None;
            port.ReadTimeout = 400;
            port.WriteTimeout = 1000;
            port.Encoding = Encoding.ASCII;
            port.Open();
            // RTS/DTR après ouverture : certains postes (FTdx10, FT-2000) n'acceptent le CAT qu'avec RTS actif.
            port.RtsEnable = settings.Rts;
            port.DtrEnable = settings.Dtr;
            Thread.Sleep(100);
            port.DiscardInBuffer();
        }

        public void Close()
        {
            if (port != null)
            {
                try { port.Close(); } catch { }
                port.Dispose();
                port = null;
            }
        }

        public void Dispose() { Close(); }

        public abstract bool Read(out long freqHz, out string mode);
        public abstract int ReadTx();
        public abstract void SetPtt(bool tx);

        // Par défaut : pas de commande de volume (FT-817/818).
        public virtual int ReadAfGain() { return -1; }
        public virtual void SetAfGain(int percent) { }

        protected static int ToPercent(long raw, int max) { return (int)Math.Round(raw * 100.0 / max); }
        protected static int FromPercent(int pct, int max) { return (int)Math.Round(Math.Max(0, Math.Min(100, pct)) * max / 100.0); }
        public abstract void SetFreq(long freqHz);
        public abstract void SetMode(string mode);
    }

    // Protocole texte "commande;" (Yaesu récents, Kenwood, Elecraft).
    public abstract class AsciiCatRig : SerialRig
    {
        protected AsciiCatRig(SerialSettings s) : base(s) { }

        protected void Send(string cmd)
        {
            port.Write(cmd);
        }

        // Envoie une requête et attend la réponse commençant par `prefix`, en ignorant les messages spontanés (AI).
        protected string Query(string cmd, string prefix)
        {
            port.DiscardInBuffer();
            port.Write(cmd);
            StringBuilder sb = new StringBuilder();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(600);
            while (DateTime.UtcNow < deadline)
            {
                int b;
                try { b = port.ReadByte(); }
                catch (TimeoutException) { return null; }
                if (b < 0) return null;
                char c = (char)b;
                if (c == ';')
                {
                    string token = sb.ToString().Trim();
                    sb.Length = 0;
                    if (token.StartsWith(prefix)) return token.Substring(prefix.Length);
                    if (token == "?") return null;
                }
                else sb.Append(c);
            }
            return null;
        }

        protected static long ParseDigits(string s)
        {
            if (s == null) return 0;
            long v;
            return long.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }

    public class YaesuRig : AsciiCatRig
    {
        readonly int digits;

        public YaesuRig(SerialSettings s, int freqDigits) : base(s) { digits = freqDigits; }

        public override bool Read(out long freqHz, out string mode)
        {
            freqHz = ParseDigits(Query("FA;", "FA"));
            mode = null;
            if (freqHz <= 0) return false;
            string md = Query("MD0;", "MD0");
            if (!string.IsNullOrEmpty(md)) mode = CodeToMode(md[0]);
            return true;
        }

        // TX0 = réception, TX1 = émission par le PTT/micro, TX2 = émission par CAT.
        public override int ReadTx()
        {
            string tx = Query("TX;", "TX");
            if (string.IsNullOrEmpty(tx)) return -1;
            return tx[0] == '0' ? 0 : 1;
        }

        public override void SetPtt(bool tx)
        {
            Send(tx ? "TX1;" : "TX0;");
        }

        // AF GAIN : "AG0nnn;" (P1 = 0 fixe, nnn = 000 à 255), manuel CAT Yaesu FTdx10.
        public override int ReadAfGain()
        {
            string r = Query("AG0;", "AG0");
            long v = ParseDigits(r);
            return string.IsNullOrEmpty(r) ? -1 : ToPercent(v, 255);
        }

        public override void SetAfGain(int percent)
        {
            Send("AG0" + FromPercent(percent, 255).ToString("000", CultureInfo.InvariantCulture) + ";");
        }

        public override void SetFreq(long freqHz)
        {
            Send("FA" + freqHz.ToString(new string('0', digits), CultureInfo.InvariantCulture) + ";");
        }

        public override void SetMode(string mode)
        {
            char c = ModeToCode(mode);
            if (c != '\0') Send("MD0" + c + ";");
        }

        static string CodeToMode(char c)
        {
            switch (char.ToUpperInvariant(c))
            {
                case '1': return Modes.LSB;
                case '2': return Modes.USB;
                case '3': return Modes.CW;     // CW-U sur FTdx10/101
                case '4': case 'B': return Modes.FM;
                case '5': case 'D': return Modes.AM;
                case '6': return Modes.RTTY;   // RTTY-LSB
                case '7': return Modes.CWR;    // CW-L
                case '8': return Modes.DATAL;
                case '9': return Modes.RTTYR;  // RTTY-USB
                case 'A': return Modes.FM;     // DATA-FM
                case 'C': return Modes.DATAU;
            }
            return null;
        }

        static char ModeToCode(string m)
        {
            switch (m)
            {
                case Modes.LSB: return '1';
                case Modes.USB: return '2';
                case Modes.CW: return '3';
                case Modes.FM: return '4';
                case Modes.AM: return '5';
                case Modes.RTTY: return '6';
                case Modes.CWR: return '7';
                case Modes.DATAL: return '8';
                case Modes.RTTYR: return '9';
                case Modes.DATAU: return 'C';
            }
            return '\0';
        }
    }

    // Kenwood et Elecraft partagent le même jeu de commandes de base (FA / MD).
    public class KenwoodRig : AsciiCatRig
    {
        readonly bool elecraft;

        public KenwoodRig(SerialSettings s, bool isElecraft) : base(s) { elecraft = isElecraft; }

        public override bool Read(out long freqHz, out string mode)
        {
            freqHz = ParseDigits(Query("FA;", "FA"));
            mode = null;
            if (freqHz <= 0) return false;
            string md = Query("MD;", "MD");
            if (!string.IsNullOrEmpty(md)) mode = CodeToMode(md[md.Length - 1]);
            return true;
        }

        // Réponse IF : fréquence (11), 5 espaces, RIT/XIT (5), RIT, XIT, banque, mémoire (2), puis TX/RX au rang 26.
        public override int ReadTx()
        {
            string s = Query("IF;", "IF");
            if (s == null || s.Length <= 26) return -1;
            return s[26] == '1' ? 1 : s[26] == '0' ? 0 : -1;
        }

        public override void SetPtt(bool tx)
        {
            Send(tx ? "TX;" : "RX;");
        }

        // AF GAIN. Kenwood : "AGnnn;" ou "AG0nnn;" selon le modèle (détecté au premier usage, comme Hamlib), 000-255.
        // Elecraft : "AGnnn;", 000-250 (K3, KX3, KX2) ou 000-060 (K4).
        int agFormat = -1;       // -1 inconnu, 0 non pris en charge, 1 "AGnnn", 2 "AG0nnn"
        public int AfMax = 255;

        public override int ReadAfGain()
        {
            if (agFormat < 0) DetectAgFormat();
            if (agFormat == 0) return -1;
            string r = agFormat == 1 ? Query("AG;", "AG") : Query("AG0;", "AG0");
            if (string.IsNullOrEmpty(r)) return -1;
            if (agFormat == 1 && r.Length == 4) r = r.Substring(1);   // certains modèles répondent "AG0nnn" à "AG;"
            return ToPercent(ParseDigits(r), AfMax);
        }

        public override void SetAfGain(int percent)
        {
            if (agFormat < 0) DetectAgFormat();
            if (agFormat == 0) return;
            Send((agFormat == 1 ? "AG" : "AG0") + FromPercent(percent, AfMax).ToString("000", CultureInfo.InvariantCulture) + ";");
        }

        void DetectAgFormat()
        {
            if (elecraft) { agFormat = 1; return; }
            string r = Query("AG;", "AG");
            if (!string.IsNullOrEmpty(r) && r.Length == 3) { agFormat = 1; return; }
            if (!string.IsNullOrEmpty(r) && r.Length == 4) { agFormat = 2; return; }
            r = Query("AG0;", "AG0");
            agFormat = !string.IsNullOrEmpty(r) ? 2 : 0;
            Log.Write("Kenwood : format du gain BF = " + (agFormat == 1 ? "AGnnn" : agFormat == 2 ? "AG0nnn" : "non pris en charge"));
        }

        public override void SetFreq(long freqHz)
        {
            Send("FA" + freqHz.ToString("00000000000", CultureInfo.InvariantCulture) + ";");
        }

        public override void SetMode(string mode)
        {
            char c = ModeToCode(mode);
            if (c != '\0') Send("MD" + c + ";");
        }

        string CodeToMode(char c)
        {
            switch (c)
            {
                case '1': return Modes.LSB;
                case '2': return Modes.USB;
                case '3': return Modes.CW;
                case '4': return Modes.FM;
                case '5': return Modes.AM;
                case '6': return elecraft ? Modes.DATAU : Modes.RTTY;
                case '7': return Modes.CWR;
                case '9': return elecraft ? Modes.DATAL : Modes.RTTYR;
            }
            return null;
        }

        char ModeToCode(string m)
        {
            switch (m)
            {
                case Modes.LSB: return '1';
                case Modes.USB: return '2';
                case Modes.CW: return '3';
                case Modes.FM: return '4';
                case Modes.AM: return '5';
                case Modes.CWR: return '7';
                case Modes.RTTY: return elecraft ? '9' : '6';
                case Modes.RTTYR: return elecraft ? '6' : '9';
                // Mode données (FT8, WSJT...) : DATA sur Elecraft ; USB/LSB sur Kenwood (6/9 y sont le FSK, pas les données).
                case Modes.DATAU: return elecraft ? '6' : '2';
                case Modes.DATAL: return elecraft ? '9' : '1';
            }
            return '\0';
        }
    }

    // Yaesu FT-817/818/857/897 : ancien protocole CAT binaire, commandes de 5 octets [P1 P2 P3 P4 CMD],
    // fréquence en BCD sur 4 octets (unité 10 Hz, chiffre de poids fort en premier).
    public class Ft817Rig : SerialRig
    {
        public Ft817Rig(SerialSettings s) : base(s) { }

        public override bool Read(out long freqHz, out string mode)
        {
            freqHz = 0;
            mode = null;
            byte[] r = Command(0, 0, 0, 0, 0x03, 5);
            if (r == null) return false;
            freqHz = FromBcd(r) * 10;
            mode = CodeToMode(r[4]);
            return freqHz > 0;
        }

        // 0xF7 : état d'émission ; bit 7 à 0 pendant l'émission.
        public override int ReadTx()
        {
            byte[] r = Command(0, 0, 0, 0, 0xF7, 1);
            if (r == null) return -1;
            return (r[0] & 0x80) == 0 ? 1 : 0;
        }

        public override void SetPtt(bool tx)
        {
            Command(0, 0, 0, 0, (byte)(tx ? 0x08 : 0x88), 1);
        }

        public override void SetFreq(long freqHz)
        {
            byte[] b = ToBcd(freqHz / 10);
            Command(b[0], b[1], b[2], b[3], 0x01, 0);
        }

        public override void SetMode(string mode)
        {
            int code = ModeToCode(mode);
            if (code >= 0) Command((byte)code, 0, 0, 0, 0x07, 0);
        }

        byte[] Command(byte p1, byte p2, byte p3, byte p4, byte cmd, int replyLength)
        {
            port.DiscardInBuffer();
            port.Write(new byte[] { p1, p2, p3, p4, cmd }, 0, 5);
            if (replyLength == 0) return null;
            byte[] r = new byte[replyLength];
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(500);
            int got = 0;
            while (got < replyLength && DateTime.UtcNow < deadline)
            {
                try { got += port.Read(r, got, replyLength - got); }
                catch (TimeoutException) { return null; }
            }
            return got == replyLength ? r : null;
        }

        // 4 octets BCD -> valeur (8 chiffres), poids fort en premier.
        public static long FromBcd(byte[] d)
        {
            long v = 0;
            for (int i = 0; i < 4; i++) v = v * 100 + ((d[i] >> 4) & 0x0F) * 10 + (d[i] & 0x0F);
            return v;
        }

        public static byte[] ToBcd(long v)
        {
            byte[] b = new byte[4];
            for (int i = 3; i >= 0; i--)
            {
                int lo = (int)(v % 10); v /= 10;
                int hi = (int)(v % 10); v /= 10;
                b[i] = (byte)((hi << 4) | lo);
            }
            return b;
        }

        // Le bit 7 indique la version étroite (CW-N, FM-N...) : on l'ignore.
        static string CodeToMode(byte c)
        {
            switch (c & 0x7F)
            {
                case 0x00: return Modes.LSB;
                case 0x01: return Modes.USB;
                case 0x02: return Modes.CW;
                case 0x03: return Modes.CWR;
                case 0x04: return Modes.AM;
                case 0x06: case 0x08: return Modes.FM;
                case 0x0A: case 0x0C: return Modes.DATAU;   // DIG / PKT
            }
            return null;
        }

        static int ModeToCode(string m)
        {
            switch (m)
            {
                case Modes.LSB: return 0x00;
                case Modes.USB: return 0x01;
                case Modes.CW: return 0x02;
                case Modes.CWR: return 0x03;
                case Modes.AM: return 0x04;
                case Modes.FM: return 0x08;
                case Modes.DATAU: case Modes.DATAL: case Modes.RTTY: case Modes.RTTYR: return 0x0A;
            }
            return -1;
        }
    }

    // Yaesu FT-990, FT-1000, FT-1000D, FT-1000MP et Mark-V : ancien protocole binaire, 5 octets [P4 P3 P2 P1 CMD]
    // (d'après Hamlib ft990.c, ft1000d.c, ft1000mp.c). Fréquence et mode lus dans le bloc « données de trafic » (10h, P1 = 2).
    public class YaesuOldRig : SerialRig
    {
        readonly bool mp;          // FT-1000MP / Mark-V : fréquence sur 4 octets en pas de 0,625 Hz
        readonly int blockLength;  // taille du bloc renvoyé : 32 (FT-990), 16 (FT-1000/1000D/1000MP)
        readonly int byteDelayMs;  // pause entre octets envoyés : 50 ms pour le FT-990, 5 ms pour le MP

        public YaesuOldRig(SerialSettings s, string model) : base(s)
        {
            mp = model.Contains("1000MP");
            blockLength = model == "FT-990" ? 32 : 16;
            byteDelayMs = model == "FT-990" ? 50 : mp ? 5 : 0;
        }

        public override bool Read(out long freqHz, out string mode)
        {
            freqHz = 0;
            mode = null;
            byte[] r = Command(2, 0x10, blockLength, 16);
            if (r == null) return false;
            Decode(r, mp, out freqHz, out mode);
            return freqHz > 0;
        }

        // Bloc de données : octet 0 bande, 1-3 fréquence en 10 Hz (MP : 1-4 en 0,625 Hz), 7 mode, 8 filtre.
        public static void Decode(byte[] r, bool mp, out long freqHz, out string mode)
        {
            bool ext = (r[8] & 0x80) != 0;
            if (mp) freqHz = ((((long)r[1] << 24) | ((long)r[2] << 16) | ((long)r[3] << 8) | r[4]) * 10) / 16;
            else freqHz = (((long)r[1] << 16) | ((long)r[2] << 8) | r[3]) * 10;
            mode = CodeToMode(r[7] & 0x07, ext, mp);
        }

        // Drapeaux d'état (FAh) : octet 1, bit 7 = émission. Le FT-1000MP ne le donne pas de façon sûre : inconnu.
        public override int ReadTx()
        {
            if (mp) return -1;
            byte[] r = Command(0, 0xFA, 5, 5);
            if (r == null) return -1;
            return (r[0] & 0x80) != 0 ? 1 : 0;
        }

        public override void SetPtt(bool tx)
        {
            Command((byte)(tx ? 1 : 0), 0x0F, 0, 0);
        }

        // Fréquence en BCD, unité 10 Hz, octet de poids faible en premier.
        public override void SetFreq(long freqHz)
        {
            Write(FreqCommand(freqHz));
        }

        public static byte[] FreqCommand(long freqHz)
        {
            long v = (freqHz + 5) / 10;
            byte[] cmd = new byte[5];
            for (int i = 0; i < 4; i++)
            {
                int lo = (int)(v % 10); v /= 10;
                int hi = (int)(v % 10); v /= 10;
                cmd[i] = (byte)((hi << 4) | lo);
            }
            cmd[4] = 0x0A;
            return cmd;
        }

        public override void SetMode(string mode)
        {
            int code = ModeToCode(mode, mp);
            if (code >= 0) Command((byte)code, 0x0C, 0, 0);
        }

        // Envoie [0 0 0 p1 cmd] ; attend `length` octets, accepte la réponse dès `minimum` octets reçus.
        byte[] Command(byte p1, byte cmd, int length, int minimum)
        {
            port.DiscardInBuffer();
            Write(new byte[] { 0, 0, 0, p1, cmd });
            if (length == 0) return null;
            byte[] r = new byte[length];
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(800);
            int got = 0;
            while (got < length && DateTime.UtcNow < deadline)
            {
                try { got += port.Read(r, got, length - got); }
                catch (TimeoutException) { break; }
            }
            return got >= minimum ? r : null;
        }

        void Write(byte[] cmd)
        {
            if (byteDelayMs == 0) { port.Write(cmd, 0, cmd.Length); return; }
            for (int i = 0; i < cmd.Length; i++)
            {
                port.Write(cmd, i, 1);
                Thread.Sleep(byteDelayMs);
            }
        }

        // Mode du bloc : 0 LSB, 1 USB, 2 CW, 3 AM, 4 FM, 5 RTTY, 6 PKT ; `ext` = bit 7 de l'octet filtre
        // (MP : CW côté USB, AM synchro, RTTY-USB, PKT-FM ; 990/1000 : RTTY-USB, PKT-FM).
        static string CodeToMode(int c, bool ext, bool mp)
        {
            switch (c)
            {
                case 0: return Modes.LSB;
                case 1: return Modes.USB;
                case 2: return mp && !ext ? Modes.CWR : Modes.CW;
                case 3: return Modes.AM;
                case 4: return Modes.FM;
                case 5: return ext ? Modes.RTTYR : Modes.RTTY;
                case 6: return ext ? Modes.FM : Modes.DATAL;
            }
            return null;
        }

        // Codes de la commande 0Ch : 0 LSB, 1 USB, 2 CW, 3 CW-LSB (MP ; CW étroit sur 990/1000), 4 AM, 6 FM,
        // 8 RTTY-LSB, 9 RTTY-USB, 0Ah PKT-LSB. Pas de PKT-USB : les données côté USB passent en USB.
        public static int ModeToCode(string m, bool mp)
        {
            switch (m)
            {
                case Modes.LSB: return 0x00;
                case Modes.USB: case Modes.DATAU: return 0x01;
                case Modes.CW: return 0x02;
                case Modes.CWR: return mp ? 0x03 : 0x02;
                case Modes.AM: return 0x04;
                case Modes.FM: return 0x06;
                case Modes.RTTY: return 0x08;
                case Modes.RTTYR: return 0x09;
                case Modes.DATAL: return 0x0A;
            }
            return -1;
        }
    }

    // Icom CI-V : trames FE FE <dest> <src> <cmd> [données] FD.
    public class IcomRig : SerialRig
    {
        const byte Controller = 0xE0;
        readonly byte address;
        readonly List<byte> buffer = new List<byte>();

        public IcomRig(SerialSettings s, byte civAddress) : base(s) { address = civAddress; }

        public override bool Read(out long freqHz, out string mode)
        {
            freqHz = 0;
            mode = null;
            byte[] f = Transact(new byte[] { 0x03 }, 0x03);
            if (f == null || f.Length < 5) return false;
            freqHz = FromBcd(f, 0, 5);
            byte[] m = Transact(new byte[] { 0x04 }, 0x04);
            if (m != null && m.Length >= 1) mode = CodeToMode(m[0]);
            return freqHz > 0;
        }

        // Commande 1C 00 : état émission/réception (réponse 1C 00 00 ou 1C 00 01).
        public override int ReadTx()
        {
            byte[] d = Transact(new byte[] { 0x1C, 0x00 }, 0x1C);
            if (d == null || d.Length < 2 || d[0] != 0x00) return -1;
            return d[1] == 0x01 ? 1 : 0;
        }

        public override void SetPtt(bool tx)
        {
            Transact(new byte[] { 0x1C, 0x00, (byte)(tx ? 0x01 : 0x00) }, 0xFB);
        }

        // Niveau AF : commande 14 01, valeur 0000 à 0255 en BCD sur 2 octets (Hamlib : C_CTL_LVL / S_LVL_AF).
        public override int ReadAfGain()
        {
            byte[] d = Transact(new byte[] { 0x14, 0x01 }, 0x14);
            if (d == null || d.Length < 3 || d[0] != 0x01) return -1;
            int v = ((d[1] >> 4) * 10 + (d[1] & 0x0F)) * 100 + (d[2] >> 4) * 10 + (d[2] & 0x0F);
            return ToPercent(v, 255);
        }

        public override void SetAfGain(int percent)
        {
            int v = FromPercent(percent, 255);
            byte hi = (byte)(((v / 1000) << 4) | ((v / 100) % 10));
            byte lo = (byte)((((v / 10) % 10) << 4) | (v % 10));
            Transact(new byte[] { 0x14, 0x01, hi, lo }, 0xFB);
        }

        public override void SetFreq(long freqHz)
        {
            byte[] cmd = new byte[6];
            cmd[0] = 0x05;
            ToBcd(freqHz, cmd, 1, 5);
            Transact(cmd, 0xFB);
        }

        public override void SetMode(string mode)
        {
            int code = ModeToCode(mode);
            if (code >= 0) Transact(new byte[] { 0x06, (byte)code }, 0xFB);
        }

        // Envoie une commande et renvoie les données de la réponse dont la commande vaut `expect`
        // (0xFB = OK pour les commandes d'écriture). Ignore l'écho du bus et les trames "transceive".
        byte[] Transact(byte[] payload, byte expect)
        {
            port.DiscardInBuffer();
            buffer.Clear();
            byte[] frame = new byte[payload.Length + 5];
            frame[0] = 0xFE; frame[1] = 0xFE; frame[2] = address; frame[3] = Controller;
            Array.Copy(payload, 0, frame, 4, payload.Length);
            frame[frame.Length - 1] = 0xFD;
            port.Write(frame, 0, frame.Length);

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < deadline)
            {
                int b;
                try { b = port.ReadByte(); }
                catch (TimeoutException) { return null; }
                if (b < 0) return null;
                buffer.Add((byte)b);
                if (b != 0xFD) continue;

                byte[] msg = ExtractFrame();
                if (msg == null) continue;
                // msg = dest, src, cmd, données...
                if (msg.Length < 3 || msg[1] != address) continue;   // écho de notre propre trame
                if (msg[0] != Controller) continue;
                if (msg[2] == 0xFA) return null;                       // NG : commande refusée
                if (msg[2] != expect) continue;
                byte[] data = new byte[msg.Length - 3];
                Array.Copy(msg, 3, data, 0, data.Length);
                return data;
            }
            return null;
        }

        byte[] ExtractFrame()
        {
            int start = -1;
            for (int i = buffer.Count - 2; i >= 0; i--)
            {
                if (buffer[i] == 0xFE && (i + 1 < buffer.Count) && buffer[i + 1] == 0xFE) { start = i; break; }
            }
            if (start < 0) { buffer.Clear(); return null; }
            int body = start + 2;
            while (body < buffer.Count && buffer[body] == 0xFE) body++;
            int len = buffer.Count - 1 - body;
            byte[] msg = new byte[Math.Max(0, len)];
            if (len > 0) buffer.CopyTo(body, msg, 0, len);
            buffer.Clear();
            return msg;
        }

        static long FromBcd(byte[] d, int offset, int count)
        {
            long v = 0, mul = 1;
            for (int i = 0; i < count; i++)
            {
                byte b = d[offset + i];
                v += (b & 0x0F) * mul; mul *= 10;
                v += ((b >> 4) & 0x0F) * mul; mul *= 10;
            }
            return v;
        }

        static void ToBcd(long v, byte[] d, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int lo = (int)(v % 10); v /= 10;
                int hi = (int)(v % 10); v /= 10;
                d[offset + i] = (byte)((hi << 4) | lo);
            }
        }

        static string CodeToMode(byte c)
        {
            switch (c)
            {
                case 0x00: return Modes.LSB;
                case 0x01: return Modes.USB;
                case 0x02: return Modes.AM;
                case 0x03: return Modes.CW;
                case 0x04: return Modes.RTTY;
                case 0x05: case 0x06: return Modes.FM;
                case 0x07: return Modes.CWR;
                case 0x08: return Modes.RTTYR;
                case 0x17: return Modes.FM;   // DV
            }
            return null;
        }

        static int ModeToCode(string m)
        {
            switch (m)
            {
                case Modes.LSB: case Modes.DATAL: return 0x00;
                case Modes.USB: case Modes.DATAU: return 0x01;
                case Modes.AM: return 0x02;
                case Modes.CW: return 0x03;
                case Modes.RTTY: return 0x04;
                case Modes.FM: return 0x05;
                case Modes.CWR: return 0x07;
                case Modes.RTTYR: return 0x08;
            }
            return -1;
        }
    }
}
