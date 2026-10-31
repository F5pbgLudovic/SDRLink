using System;
using System.Threading;

namespace SDRLink
{
    // Anciens Yaesu à bloc d'état : FT-100, FT-600, FT-747GX, FT-757GXII, FT-840, FT-890, FT-900, FT-920.
    // D'après Hamlib (ft100.c, ft600.c, ft747.c, ft757gx.c, ft840.c, ft890.c, ft900.c, ft920.c) :
    // commandes de 5 octets [P4 P3 P2 P1 CMD], fréquence envoyée en BCD (unité 10 Hz, octet de poids faible en premier, 0Ah),
    // mode 0Ch, PTT 0Fh ; lecture par la commande 10h, qui renvoie un bloc dont la disposition dépend du modèle.
    public class YaesuLegacyRig : SerialRig
    {
        enum FreqFormat { BcdBigHz, BcdLittle10Hz, Binary3x10Hz, Binary4Hz, Binary4x125 }

        // Ordre des codes de mode à envoyer : LSB, USB, CW, CW-R, AM, FM, DATA-L, DATA-U (-1 = absent sur ce poste).
        const int iLsb = 0, iUsb = 1, iCw = 2, iCwr = 3, iAm = 4, iFm = 5, iDataL = 6, iDataU = 7;

        readonly string model;
        readonly byte statusP1;
        readonly int statusLength;
        readonly int freqOffset;
        readonly FreqFormat freqFormat;
        readonly int modeOffset;
        readonly int blockTxMask;      // bit d'émission dans l'octet 0 du bloc (0 = pas là)
        readonly int flagsP1 = -1;     // lecture des drapeaux par FAh (-1 = pas de commande)
        readonly int flagsLength, flagsByte;
        readonly bool catPtt = true;
        readonly int byteDelayMs, postDelayMs = 20;
        readonly int[] modeCodes;

        byte[] block;
        DateTime blockTime;
        bool pttWarned;

        public YaesuLegacyRig(SerialSettings s, string modelName) : base(s)
        {
            model = modelName;
            switch (modelName)
            {
                case "FT-747GX":
                    // Bloc de 344 octets ; fréquence affichée en BCD (Hz, poids fort en premier) à l'octet 1,
                    // mode à 18h ; ne répond pas pendant l'émission.
                    statusP1 = 0; statusLength = 344; freqOffset = 1; freqFormat = FreqFormat.BcdBigHz; modeOffset = 0x18;
                    blockTxMask = 0x20; byteDelayMs = 5;
                    modeCodes = new int[] { 0, 1, 2, 2, 4, 6, 0, 1 };
                    break;
                case "FT-757GXII":
                    // Bloc de 75 octets ; fréquence en BCD (10 Hz, poids faible en premier) à l'octet 5, mode à l'octet 9.
                    // Pas de commande PTT : l'émission se commande par la broche DTR de la prise CAT.
                    statusP1 = 0; statusLength = 75; freqOffset = 5; freqFormat = FreqFormat.BcdLittle10Hz; modeOffset = 9;
                    blockTxMask = 0x20; byteDelayMs = 50; catPtt = false;
                    modeCodes = new int[] { 0, 1, 2, 2, 4, 5, 0, 1 };
                    break;
                case "FT-840":
                case "FT-890":
                case "FT-900":
                    // Données de trafic (10h, P1 = 2) : 19 octets, fréquence binaire sur 3 octets (10 Hz) à l'octet 2,
                    // mode à l'octet 7 ; émission : drapeaux FAh (5 octets), octet 2 bit 7.
                    statusP1 = 2; statusLength = 19; freqOffset = 2; freqFormat = FreqFormat.Binary3x10Hz; modeOffset = 7;
                    flagsP1 = 0; flagsLength = 5; flagsByte = 2; byteDelayMs = modelName == "FT-900" ? 5 : 50;
                    modeCodes = new int[] { 0, 1, 2, 2, 4, 6, 0, 1 };
                    break;
                case "FT-920":
                    // Données de trafic (10h, P1 = 2) : 28 octets, fréquence binaire sur 4 octets (Hz) à l'octet 1,
                    // mode à l'octet 7 ; émission : drapeaux FAh (8 octets), octet 0 bit 7.
                    statusP1 = 2; statusLength = 28; freqOffset = 1; freqFormat = FreqFormat.Binary4Hz; modeOffset = 7;
                    flagsP1 = 0; flagsLength = 8; flagsByte = 0; postDelayMs = 80;
                    modeCodes = new int[] { 0, 1, 2, 3, 4, 6, 8, 0x0A };
                    break;
                case "FT-100/100D":
                    // Bloc de 32 octets ; fréquence binaire sur 4 octets (pas de 1,25 Hz) à l'octet 1, mode à l'octet 5 ;
                    // émission : drapeaux FAh (P1 = 1, 8 octets), octet 0 bit 7.
                    statusP1 = 0; statusLength = 32; freqOffset = 1; freqFormat = FreqFormat.Binary4x125; modeOffset = 5;
                    flagsP1 = 1; flagsLength = 8; flagsByte = 0; postDelayMs = 25;
                    modeCodes = new int[] { 0, 1, 2, 3, 4, 6, 0, 5 };
                    break;
                case "FT-600":
                    // Bloc de 19 octets (10h, P1 = 2) ; fréquence binaire sur 3 octets (10 Hz) à l'octet 1, mode à l'octet 6.
                    // Pas de FM ; état d'émission non lisible.
                    statusP1 = 2; statusLength = 19; freqOffset = 1; freqFormat = FreqFormat.Binary3x10Hz; modeOffset = 6;
                    byteDelayMs = 5; postDelayMs = 200;
                    modeCodes = new int[] { 0, 1, 3, 3, 4, -1, 0, 9 };
                    break;
                default:
                    throw new ArgumentException("Modèle Yaesu inconnu : " + modelName);
            }
        }

        public override bool Read(out long freqHz, out string mode)
        {
            freqHz = 0;
            mode = null;
            byte[] r = Block(true);
            if (r == null) return false;
            Decode(model, r, out freqHz, out mode);
            return freqHz > 0;
        }

        // Décodage d'un bloc d'état (public pour les tests).
        public static void Decode(string modelName, byte[] r, out long freqHz, out string mode)
        {
            YaesuLegacyRig p = new YaesuLegacyRig(new SerialSettings(), modelName);
            freqHz = p.DecodeFreq(r);
            mode = p.DecodeMode(r[p.modeOffset]);
        }

        long DecodeFreq(byte[] r)
        {
            int o = freqOffset;
            switch (freqFormat)
            {
                case FreqFormat.BcdBigHz:
                {
                    long v = 0;
                    for (int i = 0; i < 4; i++) v = v * 100 + ((r[o + i] >> 4) & 0x0F) * 10 + (r[o + i] & 0x0F);
                    return v;
                }
                case FreqFormat.BcdLittle10Hz:
                {
                    long v = 0;
                    for (int i = 3; i >= 0; i--) v = v * 100 + ((r[o + i] >> 4) & 0x0F) * 10 + (r[o + i] & 0x0F);
                    return v * 10;
                }
                case FreqFormat.Binary3x10Hz:
                    return (((long)r[o] << 16) | ((long)r[o + 1] << 8) | r[o + 2]) * 10;
                case FreqFormat.Binary4Hz:
                    return ((long)r[o] << 24) | ((long)r[o + 1] << 16) | ((long)r[o + 2] << 8) | r[o + 3];
                default:
                    return ((((long)r[o] << 24) | ((long)r[o + 1] << 16) | ((long)r[o + 2] << 8) | r[o + 3]) * 5) / 4;
            }
        }

        string DecodeMode(byte b)
        {
            switch (model)
            {
                case "FT-747GX":
                    // Un bit par mode ; 80h = filtre étroit.
                    switch (b & 0x1F)
                    {
                        case 0x01: return Modes.FM;
                        case 0x02: return Modes.AM;
                        case 0x04: return Modes.CW;
                        case 0x08: return Modes.USB;
                        case 0x10: return Modes.LSB;
                    }
                    return null;
                case "FT-757GXII":
                    switch (b)
                    {
                        case 0: return Modes.LSB;
                        case 1: return Modes.USB;
                        case 2: case 3: return Modes.CW;
                        case 4: return Modes.AM;
                        case 5: return Modes.FM;
                    }
                    return null;
                case "FT-920":
                    // 40h = côté USB ; 80h = filtre étroit.
                    switch (b & 0x47)
                    {
                        case 0x00: return Modes.LSB;
                        case 0x01: return Modes.CWR;
                        case 0x02: return Modes.AM;
                        case 0x03: case 0x06: return Modes.FM;
                        case 0x04: return Modes.DATAL;
                        case 0x05: return Modes.DATAU;
                        case 0x40: return Modes.USB;
                        case 0x41: return Modes.CW;
                    }
                    return null;
                case "FT-100/100D":
                    switch (b & 0x0F)
                    {
                        case 0: return Modes.LSB;
                        case 1: return Modes.USB;
                        case 2: return Modes.CW;
                        case 3: return Modes.CWR;
                        case 4: return Modes.AM;
                        case 5: return Modes.DATAU;
                        case 6: case 7: return Modes.FM;
                    }
                    return null;
                case "FT-600":
                    switch (b)
                    {
                        case 0: return Modes.LSB;
                        case 1: return Modes.USB;
                        case 2: return Modes.CW;
                        case 4: return Modes.AM;
                        case 5: return Modes.DATAU;
                    }
                    return null;
                default:   // FT-840, FT-890, FT-900
                    switch (b & 0x07)
                    {
                        case 0: return Modes.LSB;
                        case 1: return Modes.USB;
                        case 2: return Modes.CW;
                        case 3: return Modes.AM;
                        case 4: return Modes.FM;
                    }
                    return null;
            }
        }

        public override int ReadTx()
        {
            if (blockTxMask != 0)
            {
                byte[] r = Block(false);
                if (r == null) return -1;
                return (r[0] & blockTxMask) != 0 ? 1 : 0;
            }
            if (flagsP1 < 0) return -1;
            byte[] f = Command((byte)flagsP1, 0xFA, flagsLength, flagsLength);
            if (f == null) return -1;
            return (f[flagsByte] & 0x80) != 0 ? 1 : 0;
        }

        public override void SetPtt(bool tx)
        {
            if (!catPtt)
            {
                if (!pttWarned) { pttWarned = true; Log.Write(model + " : pas de commande PTT par CAT"); }
                return;
            }
            Command((byte)(tx ? 1 : 0), 0x0F, 0, 0);
            Thread.Sleep(postDelayMs);
        }

        public override void SetFreq(long freqHz)
        {
            Write(YaesuOldRig.FreqCommand(freqHz));
            block = null;
            Thread.Sleep(postDelayMs);
        }

        public override void SetMode(string mode)
        {
            int code = ModeCode(mode, modeCodes);
            if (code < 0) return;
            Command((byte)code, 0x0C, 0, 0);
            block = null;
            Thread.Sleep(postDelayMs);
        }

        // Code à envoyer pour un mode ; RTTY suit le mode données du même côté, et à défaut USB/LSB.
        public static int ModeCode(string mode, int[] codes)
        {
            int i;
            switch (mode)
            {
                case Modes.LSB: i = iLsb; break;
                case Modes.USB: i = iUsb; break;
                case Modes.CW: i = iCw; break;
                case Modes.CWR: i = iCwr; break;
                case Modes.AM: i = iAm; break;
                case Modes.FM: i = iFm; break;
                case Modes.DATAL: case Modes.RTTY: i = iDataL; break;
                case Modes.DATAU: case Modes.RTTYR: i = iDataU; break;
                default: return -1;
            }
            return codes[i];
        }

        // Codes de mode du modèle (pour les tests).
        public int ModeCodeFor(string mode) { return ModeCode(mode, modeCodes); }

        // Bloc d'état ; ReadTx puis Read se suivent : le même bloc sert aux deux s'il a moins de 500 ms.
        byte[] Block(bool allowCached)
        {
            if (allowCached && block != null && (DateTime.UtcNow - blockTime).TotalMilliseconds < 500) return block;
            int needed = Math.Max(freqOffset + 4, modeOffset + 1);
            block = Command(statusP1, 0x10, statusLength, needed);
            blockTime = DateTime.UtcNow;
            return block;
        }

        // Envoie [0 0 0 p1 cmd] ; attend `length` octets, accepte la réponse dès `minimum` octets reçus.
        byte[] Command(byte p1, byte cmd, int length, int minimum)
        {
            port.DiscardInBuffer();
            Write(new byte[] { 0, 0, 0, p1, cmd });
            if (length == 0) return null;
            byte[] r = new byte[length];
            // 4800 bauds, 2 bits de stop : environ 2,3 ms par octet.
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(400 + length * 3);
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
    }

    // Yaesu FT-767GX (Hamlib ft767gx.c) : chaque commande de 5 octets est renvoyée en écho par le poste, on répond
    // par un accusé (0Bh), puis le poste envoie ses données à l'envers. La lecture se fait en passant en mode CAT
    // (00h, P1 = 0) puis en en sortant (P1 = 1) : la réponse de sortie (86 octets) contient l'état du poste.
    // Pas de commande PTT.
    public class Ft767Rig : SerialRig
    {
        static readonly byte[] Ack = { 0, 0, 0, 0, 0x0B };
        byte[] status;
        DateTime statusTime;
        bool pttWarned;

        public Ft767Rig(SerialSettings s) : base(s) { }

        public override bool Read(out long freqHz, out string mode)
        {
            freqHz = 0;
            mode = null;
            byte[] r = Status(true);
            if (r == null) return false;
            Decode(r, out freqHz, out mode);
            return freqHz > 0;
        }

        // Bloc d'état remis à l'endroit : octet 0 drapeaux (bit 0 = émission), 1-4 fréquence en BCD (10 Hz, poids fort
        // en premier), 6 mode (0 LSB, 1 USB, 2 CW, 3 AM, 4 FM, 5 FSK).
        public static void Decode(byte[] r, out long freqHz, out string mode)
        {
            long v = 0;
            for (int i = 1; i <= 4; i++) v = v * 100 + ((r[i] >> 4) & 0x0F) * 10 + (r[i] & 0x0F);
            freqHz = v * 10;
            switch (r[6] & 0x07)
            {
                case 0: mode = Modes.LSB; break;
                case 1: mode = Modes.USB; break;
                case 2: mode = Modes.CW; break;
                case 3: mode = Modes.AM; break;
                case 4: mode = Modes.FM; break;
                case 5: mode = Modes.RTTY; break;
                default: mode = null; break;
            }
        }

        public override int ReadTx()
        {
            byte[] r = Status(false);
            if (r == null) return -1;
            return (r[0] & 0x01) != 0 ? 1 : 0;
        }

        public override void SetPtt(bool tx)
        {
            if (!pttWarned) { pttWarned = true; Log.Write("FT-767GX : pas de commande PTT par CAT"); }
        }

        public override void SetFreq(long freqHz)
        {
            byte[] cmd = YaesuOldRig.FreqCommand(freqHz);
            cmd[4] = 0x08;
            InCat(cmd, 5);
        }

        public override void SetMode(string mode)
        {
            int code = ModeCode(mode);
            if (code >= 0) InCat(new byte[] { 0, 0, 0, (byte)(0x10 + code), 0x0A }, 8);
        }

        // 0 LSB, 1 USB, 2 CW, 3 AM, 4 FM, 5 FSK ; pas de CW-R ni de modes données : on prend le côté correspondant.
        public static int ModeCode(string m)
        {
            switch (m)
            {
                case Modes.LSB: case Modes.DATAL: return 0;
                case Modes.USB: case Modes.DATAU: return 1;
                case Modes.CW: case Modes.CWR: return 2;
                case Modes.AM: return 3;
                case Modes.FM: return 4;
                case Modes.RTTY: case Modes.RTTYR: return 5;
            }
            return -1;
        }

        void InCat(byte[] cmd, int replyLength)
        {
            if (Exchange(new byte[] { 0, 0, 0, 0, 0x00 }, 86) == null) return;
            Exchange(cmd, replyLength);
            Exchange(new byte[] { 0, 0, 0, 1, 0x00 }, 86);
            status = null;
        }

        byte[] Status(bool allowCached)
        {
            if (allowCached && status != null && (DateTime.UtcNow - statusTime).TotalMilliseconds < 500) return status;
            status = null;
            if (Exchange(new byte[] { 0, 0, 0, 0, 0x00 }, 86) != null)
                status = Exchange(new byte[] { 0, 0, 0, 1, 0x00 }, 86);
            statusTime = DateTime.UtcNow;
            return status;
        }

        // Commande, écho, accusé, puis réponse renvoyée à l'endroit.
        byte[] Exchange(byte[] cmd, int replyLength)
        {
            port.DiscardInBuffer();
            port.Write(cmd, 0, 5);
            byte[] echo = ReadBytes(5);
            if (echo == null) return null;
            for (int i = 0; i < 5; i++) if (echo[i] != cmd[i]) return null;
            port.Write(Ack, 0, 5);
            byte[] r = ReadBytes(replyLength);
            if (r == null) return null;
            Array.Reverse(r);
            return r;
        }

        byte[] ReadBytes(int length)
        {
            byte[] r = new byte[length];
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(400 + length * 3);
            int got = 0;
            while (got < length && DateTime.UtcNow < deadline)
            {
                try { got += port.Read(r, got, length - got); }
                catch (TimeoutException) { break; }
            }
            return got == length ? r : null;
        }
    }

    // Postes qui reçoivent les ordres CAT sans rien renvoyer : FT-757GX (le premier, sans « II ») et FT-736R
    // (Hamlib ft757gx.c, ft736.c). SDRLink affiche la dernière fréquence envoyée : seul le sens WebSDR → poste a du sens.
    public class YaesuWriteOnlyRig : SerialRig
    {
        readonly bool ft736;
        long lastFreq;
        string lastMode;

        public YaesuWriteOnlyRig(SerialSettings s, string model) : base(s)
        {
            ft736 = model == "FT-736R";
        }

        public override void Open()
        {
            base.Open();
            if (ft736) Write(new byte[] { 0, 0, 0, 0, 0x00 });   // CAT ON
            Log.Write((ft736 ? "FT-736R" : "FT-757GX") + " : le poste ne renvoie rien, SDRLink affiche la dernière fréquence envoyée");
        }

        public override void Close()
        {
            if (ft736 && port != null) { try { Write(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80 }); } catch { } }   // CAT OFF
            base.Close();
        }

        // Toujours « en ligne » : sans réponse possible, c'est la seule façon de laisser passer les ordres du WebSDR.
        public override bool Read(out long freqHz, out string mode)
        {
            freqHz = lastFreq;
            mode = lastMode;
            return true;
        }

        public override int ReadTx() { return -1; }

        public override void SetPtt(bool tx)
        {
            if (ft736) Write(new byte[] { 0, 0, 0, 0, (byte)(tx ? 0x08 : 0x88) });
        }

        public override void SetFreq(long freqHz)
        {
            Write(FreqCommand(freqHz, ft736));
            lastFreq = freqHz / 10 * 10;
        }

        // FT-757GX : BCD poids faible en premier, commande 0Ah. FT-736R : BCD poids fort en premier, commande 01h,
        // et le chiffre des GHz remplacé par Ch au-dessus de 1,2 GHz.
        public static byte[] FreqCommand(long freqHz, bool ft736)
        {
            if (!ft736) return YaesuOldRig.FreqCommand(freqHz);
            byte[] b = Ft817Rig.ToBcd(freqHz / 10);
            if (freqHz > 1200000000L) b[0] = (byte)((b[0] & 0x0F) | 0xC0);
            return new byte[] { b[0], b[1], b[2], b[3], 0x01 };
        }

        public override void SetMode(string mode)
        {
            int code = ModeCode(mode, ft736);
            if (code < 0) return;
            Write(ft736 ? new byte[] { (byte)code, 0, 0, 0, 0x07 } : new byte[] { 0, 0, 0, (byte)code, 0x0C });
            lastMode = mode;
        }

        // FT-757GX : 0 LSB, 1 USB, 2 CW, 4 AM, 5 FM. FT-736R : 0 LSB, 1 USB, 2 CW, 8 FM (pas d'AM).
        public static int ModeCode(string m, bool ft736)
        {
            switch (m)
            {
                case Modes.LSB: case Modes.DATAL: case Modes.RTTY: return 0;
                case Modes.USB: case Modes.DATAU: case Modes.RTTYR: return 1;
                case Modes.CW: case Modes.CWR: return 2;
                case Modes.AM: return ft736 ? -1 : 4;
                case Modes.FM: return ft736 ? 8 : 5;
            }
            return -1;
        }

        void Write(byte[] cmd)
        {
            // 50 ms entre les octets pour le FT-757GX, 30 ms pour le FT-736R (Hamlib).
            for (int i = 0; i < cmd.Length; i++)
            {
                port.Write(cmd, i, 1);
                Thread.Sleep(ft736 ? 30 : 50);
            }
        }
    }
}
