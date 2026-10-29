using System;
using System.Collections.Generic;

namespace SDRLink
{
    // Interface commune à tous les moyens de piloter un poste.
    public interface IRig : IDisposable
    {
        void Open();
        void Close();
        // Lit fréquence (Hz) et mode normalisé. Retourne false si le poste ne répond pas.
        bool Read(out long freqHz, out string mode);
        // État d'émission : 1 = émission, 0 = réception, -1 = inconnu (non pris en charge).
        int ReadTx();
        // Commande d'émission (PTT/MOX par CAT).
        void SetPtt(bool tx);
        // Volume audio du poste (gain BF), en % ; -1 = non pris en charge par ce poste ou cette interface.
        int ReadAfGain();
        void SetAfGain(int percent);
        void SetFreq(long freqHz);
        void SetMode(string mode);
    }

    public enum RigProtocol { Yaesu, Yaesu817, YaesuOld, Icom, Kenwood, Elecraft }

    public class RigModel
    {
        public string Brand;
        public string Name;
        public RigProtocol Protocol;
        public int FreqDigits;   // Yaesu : nombre de chiffres de la commande FA
        public byte CivAddress;  // Icom : adresse CI-V par défaut
        public int Baud;
        public int StopBits;

        public RigModel(string brand, string name, RigProtocol proto, int digits, byte civ, int baud, int stopBits)
        {
            Brand = brand; Name = name; Protocol = proto; FreqDigits = digits;
            CivAddress = civ; Baud = baud; StopBits = stopBits;
        }

        public override string ToString() { return Name; }
    }

    public static class RigCatalog
    {
        public static readonly string[] Brands = { "Yaesu", "Icom", "Kenwood", "Elecraft", "Xiegu" };

        public static readonly List<RigModel> Models = new List<RigModel>
        {
            // Yaesu (protocole "newcat" ASCII terminé par ;)
            new RigModel("Yaesu", "FT-2000",      RigProtocol.Yaesu, 8, 0, 4800, 2),
            new RigModel("Yaesu", "FTdx10",       RigProtocol.Yaesu, 9, 0, 4800, 2),
            new RigModel("Yaesu", "FTdx101D/MP",  RigProtocol.Yaesu, 9, 0, 4800, 2),
            new RigModel("Yaesu", "FT-991/991A",  RigProtocol.Yaesu, 9, 0, 4800, 2),
            new RigModel("Yaesu", "FT-891",       RigProtocol.Yaesu, 9, 0, 4800, 2),
            new RigModel("Yaesu", "FT-710",       RigProtocol.Yaesu, 9, 0, 4800, 2),
            new RigModel("Yaesu", "FTdx5000",     RigProtocol.Yaesu, 8, 0, 4800, 2),
            new RigModel("Yaesu", "FTdx3000",     RigProtocol.Yaesu, 8, 0, 4800, 2),
            new RigModel("Yaesu", "FTdx1200",     RigProtocol.Yaesu, 8, 0, 4800, 2),
            new RigModel("Yaesu", "FT-950",       RigProtocol.Yaesu, 8, 0, 4800, 2),
            new RigModel("Yaesu", "FT-450/450D",  RigProtocol.Yaesu, 8, 0, 4800, 2),
            // Yaesu FT-817/818/857/897 (ancien protocole binaire 5 octets, mêmes commandes sur les quatre)
            new RigModel("Yaesu", "FT-817",       RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-817ND",     RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-818",       RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-818ND",     RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-857",       RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-857D",      RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-897",       RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-897D",      RigProtocol.Yaesu817, 0, 0, 4800, 2),
            // Yaesu FT-990/1000/1000D/1000MP (ancien protocole binaire 5 octets, lecture par bloc d'état)
            new RigModel("Yaesu", "FT-990",       RigProtocol.YaesuOld, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-1000",      RigProtocol.YaesuOld, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-1000D",     RigProtocol.YaesuOld, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-1000MP",    RigProtocol.YaesuOld, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-1000MP Mark-V", RigProtocol.YaesuOld, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-1000MP Mark-V Field", RigProtocol.YaesuOld, 0, 0, 4800, 2),

            // Icom (CI-V binaire)
            new RigModel("Icom", "IC-7000",       RigProtocol.Icom, 0, 0x70, 19200, 1),
            new RigModel("Icom", "IC-7760",       RigProtocol.Icom, 0, 0xB2, 115200, 1),
            new RigModel("Icom", "IC-7300",       RigProtocol.Icom, 0, 0x94, 115200, 1),
            new RigModel("Icom", "IC-7610",       RigProtocol.Icom, 0, 0x98, 115200, 1),
            new RigModel("Icom", "IC-705",        RigProtocol.Icom, 0, 0xA4, 115200, 1),
            new RigModel("Icom", "IC-9700",       RigProtocol.Icom, 0, 0xA2, 115200, 1),
            new RigModel("Icom", "IC-7100",       RigProtocol.Icom, 0, 0x88, 19200, 1),
            new RigModel("Icom", "IC-7200",       RigProtocol.Icom, 0, 0x76, 19200, 1),
            new RigModel("Icom", "IC-7410",       RigProtocol.Icom, 0, 0x80, 19200, 1),
            new RigModel("Icom", "IC-7600",       RigProtocol.Icom, 0, 0x7A, 19200, 1),
            new RigModel("Icom", "IC-7700",       RigProtocol.Icom, 0, 0x74, 19200, 1),
            new RigModel("Icom", "IC-7800",       RigProtocol.Icom, 0, 0x6A, 19200, 1),
            new RigModel("Icom", "IC-7851",       RigProtocol.Icom, 0, 0x8E, 115200, 1),
            new RigModel("Icom", "IC-756PROIII",  RigProtocol.Icom, 0, 0x6E, 19200, 1),
            new RigModel("Icom", "IC-746PRO",     RigProtocol.Icom, 0, 0x66, 9600, 1),
            new RigModel("Icom", "IC-718",        RigProtocol.Icom, 0, 0x5E, 9600, 1),
            new RigModel("Icom", "IC-706MKIIG",   RigProtocol.Icom, 0, 0x58, 9600, 1),
            new RigModel("Icom", "IC-706MKII",    RigProtocol.Icom, 0, 0x4E, 9600, 1),
            new RigModel("Icom", "IC-706",        RigProtocol.Icom, 0, 0x48, 9600, 1),

            // Kenwood
            new RigModel("Kenwood", "TS-590S/SG", RigProtocol.Kenwood, 11, 0, 115200, 1),
            new RigModel("Kenwood", "TS-890S",    RigProtocol.Kenwood, 11, 0, 115200, 1),
            new RigModel("Kenwood", "TS-990S",    RigProtocol.Kenwood, 11, 0, 115200, 1),
            new RigModel("Kenwood", "TS-2000",    RigProtocol.Kenwood, 11, 0, 9600, 1),
            new RigModel("Kenwood", "TS-480",     RigProtocol.Kenwood, 11, 0, 9600, 1),
            new RigModel("Kenwood", "TS-870S",    RigProtocol.Kenwood, 11, 0, 9600, 1),
            new RigModel("Kenwood", "TS-570",     RigProtocol.Kenwood, 11, 0, 9600, 1),
            new RigModel("Kenwood", "TS-850S",    RigProtocol.Kenwood, 11, 0, 4800, 2),

            // Elecraft (compatible Kenwood)
            new RigModel("Elecraft", "K4",        RigProtocol.Elecraft, 11, 0, 38400, 1),
            new RigModel("Elecraft", "K3/K3S",    RigProtocol.Elecraft, 11, 0, 38400, 1),
            new RigModel("Elecraft", "KX3",       RigProtocol.Elecraft, 11, 0, 38400, 1),
            new RigModel("Elecraft", "KX2",       RigProtocol.Elecraft, 11, 0, 38400, 1),
            new RigModel("Elecraft", "K2",        RigProtocol.Elecraft, 11, 0, 4800, 1),

            // Xiegu (protocole CI-V compatible Icom ; l'adresse dépend du firmware, modifiable si besoin)
            new RigModel("Xiegu", "G90",          RigProtocol.Icom, 0, 0x70, 19200, 1),
            new RigModel("Xiegu", "X6100",        RigProtocol.Icom, 0, 0xA4, 19200, 1),
            new RigModel("Xiegu", "X6200",        RigProtocol.Icom, 0, 0xA4, 19200, 1),
        };

        public static List<RigModel> ForBrand(string brand)
        {
            return Models.FindAll(delegate(RigModel m) { return m.Brand == brand; });
        }

        public static RigModel Find(string name)
        {
            return Models.Find(delegate(RigModel m) { return m.Name == name; });
        }
    }

    // Modes normalisés utilisés en interne.
    public static class Modes
    {
        public const string LSB = "LSB", USB = "USB", CW = "CW", CWR = "CWR", AM = "AM", FM = "FM",
            RTTY = "RTTY", RTTYR = "RTTYR", DATAL = "DATAL", DATAU = "DATAU";

        // Mode le plus proche disponible sur un WebSDR : lsb, usb, cw, am, fm.
        public static string ToSdr(string mode)
        {
            switch (mode)
            {
                case LSB: case RTTY: case DATAL: return "lsb";
                case USB: case RTTYR: case DATAU: return "usb";
                case CW: case CWR: return "cw";
                case AM: return "am";
                case FM: return "fm";
            }
            return "";
        }

        // Traduit le mode affiché par le WebSDR vers la famille lsb/usb/cw/am/fm.
        public static string SdrFamily(string sdrMode)
        {
            string m = (sdrMode ?? "").ToLowerInvariant();
            if (m.StartsWith("lsb") || m == "lsn" || m == "digl" || m == "sal") return "lsb";
            if (m.StartsWith("usb") || m == "usn" || m == "digu" || m == "sau") return "usb";
            if (m.StartsWith("cw")) return "cw";
            if (m.Contains("fm")) return "fm";
            if (m.Contains("am") || m == "sas" || m == "dsb") return "am";
            return "";
        }

        public static string FromSdrFamily(string family)
        {
            switch (family)
            {
                case "lsb": return LSB;
                case "usb": return USB;
                case "cw": return CW;
                case "am": return AM;
                case "fm": return FM;
            }
            return null;
        }

        public static string Display(string mode)
        {
            switch (mode)
            {
                case CWR: return "CW-R";
                case RTTYR: return "RTTY-R";
                case DATAL: return "DATA-L";
                case DATAU: return "DATA-U";
            }
            return mode ?? "";
        }
    }
}
