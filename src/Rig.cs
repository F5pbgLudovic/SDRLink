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

    public enum RigProtocol { Yaesu, Yaesu817, YaesuOld, YaesuLegacy, Yaesu767, YaesuWriteOnly, Icom, Kenwood, Elecraft }

    public class RigModel
    {
        public string Brand;
        public string Name;
        public RigProtocol Protocol;
        public int FreqDigits;   // Yaesu : nombre de chiffres de la commande FA
        public byte CivAddress;  // Icom : adresse CI-V par défaut
        public int Baud;
        public int StopBits;
        public bool NoTx;        // état émission/réception non lisible par CAT (anciens Kenwood)
        public bool ShortFreq;   // Icom : fréquence sur 4 octets (IC-735)

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

        // Postes pilotables en direct (CAT par port COM). Listes comparées à Hamlib, OmniRig et Ham Radio Deluxe ;
        // protocoles et réglages d'après Hamlib. L'ordre ici n'a pas d'importance : la liste affichée est triée (ForBrand).
        public static readonly List<RigModel> Models = new List<RigModel>
        {
            // Yaesu (protocole "newcat" ASCII terminé par ;)
            new RigModel("Yaesu", "FT-2000",      RigProtocol.Yaesu, 8, 0, 4800, 2),
            new RigModel("Yaesu", "FTdx10",       RigProtocol.Yaesu, 9, 0, 4800, 2),
            new RigModel("Yaesu", "FTdx101D/MP",  RigProtocol.Yaesu, 9, 0, 4800, 2),
            new RigModel("Yaesu", "FT-991/991A",  RigProtocol.Yaesu, 9, 0, 4800, 2),
            new RigModel("Yaesu", "FT-891",       RigProtocol.Yaesu, 9, 0, 4800, 2),
            new RigModel("Yaesu", "FT-710",       RigProtocol.Yaesu, 9, 0, 4800, 2),
            new RigModel("Yaesu", "FTX-1",        RigProtocol.Yaesu, 9, 0, 38400, 1),
            new RigModel("Yaesu", "FTdx9000",     RigProtocol.Yaesu, 8, 0, 4800, 2),
            new RigModel("Yaesu", "FTdx5000",     RigProtocol.Yaesu, 8, 0, 4800, 2),
            new RigModel("Yaesu", "FTdx3000",     RigProtocol.Yaesu, 8, 0, 4800, 2),
            new RigModel("Yaesu", "FTdx1200",     RigProtocol.Yaesu, 8, 0, 4800, 2),
            new RigModel("Yaesu", "FT-950",       RigProtocol.Yaesu, 8, 0, 4800, 2),
            new RigModel("Yaesu", "FT-450/450D",  RigProtocol.Yaesu, 8, 0, 4800, 2),
            // Yaesu FT-817/818/857/897, FT-847, FT-650 (ancien protocole binaire 5 octets, mêmes commandes)
            new RigModel("Yaesu", "FT-817",       RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-817ND",     RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-818",       RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-818ND",     RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-857",       RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-857D",      RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-897",       RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-897D",      RigProtocol.Yaesu817, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-847",       RigProtocol.Yaesu817, 0, 0, 57600, 2),
            new RigModel("Yaesu", "FT-650",       RigProtocol.Yaesu817, 0, 0, 4800, 2),
            // Yaesu FT-990/1000/1000D/1000MP (ancien protocole binaire 5 octets, lecture par bloc d'état)
            new RigModel("Yaesu", "FT-990",       RigProtocol.YaesuOld, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-1000",      RigProtocol.YaesuOld, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-1000D",     RigProtocol.YaesuOld, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-1000MP",    RigProtocol.YaesuOld, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-1000MP Mark-V", RigProtocol.YaesuOld, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-1000MP Mark-V Field", RigProtocol.YaesuOld, 0, 0, 4800, 2),
            // Autres anciens Yaesu à bloc d'état (disposition propre à chaque modèle, voir YaesuLegacyRig)
            new RigModel("Yaesu", "FT-100/100D",  RigProtocol.YaesuLegacy, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-600",       RigProtocol.YaesuLegacy, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-747GX",     RigProtocol.YaesuLegacy, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-757GXII",   RigProtocol.YaesuLegacy, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-840",       RigProtocol.YaesuLegacy, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-890",       RigProtocol.YaesuLegacy, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-900",       RigProtocol.YaesuLegacy, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-920",       RigProtocol.YaesuLegacy, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-767GX",     RigProtocol.Yaesu767, 0, 0, 4800, 2),
            // Postes qui reçoivent les ordres mais ne renvoient rien (sens WebSDR -> poste seulement)
            new RigModel("Yaesu", "FT-757GX",     RigProtocol.YaesuWriteOnly, 0, 0, 4800, 2),
            new RigModel("Yaesu", "FT-736R",      RigProtocol.YaesuWriteOnly, 0, 0, 4800, 2),

            // Icom (CI-V binaire)
            new RigModel("Icom", "IC-7000",       RigProtocol.Icom, 0, 0x70, 19200, 1),
            new RigModel("Icom", "IC-7760",       RigProtocol.Icom, 0, 0xB2, 115200, 1),
            new RigModel("Icom", "IC-7300",       RigProtocol.Icom, 0, 0x94, 115200, 1),
            new RigModel("Icom", "IC-7300MK2",    RigProtocol.Icom, 0, 0xB6, 115200, 1),
            new RigModel("Icom", "IC-7610",       RigProtocol.Icom, 0, 0x98, 115200, 1),
            new RigModel("Icom", "IC-705",        RigProtocol.Icom, 0, 0xA4, 115200, 1),
            new RigModel("Icom", "IC-905",        RigProtocol.Icom, 0, 0xAC, 115200, 1),
            new RigModel("Icom", "IC-9700",       RigProtocol.Icom, 0, 0xA2, 115200, 1),
            new RigModel("Icom", "IC-9100",       RigProtocol.Icom, 0, 0x7C, 19200, 1),
            new RigModel("Icom", "IC-7100",       RigProtocol.Icom, 0, 0x88, 19200, 1),
            new RigModel("Icom", "IC-7200",       RigProtocol.Icom, 0, 0x76, 19200, 1),
            new RigModel("Icom", "IC-7410",       RigProtocol.Icom, 0, 0x80, 19200, 1),
            new RigModel("Icom", "IC-7400",       RigProtocol.Icom, 0, 0x66, 19200, 1),
            new RigModel("Icom", "IC-7600",       RigProtocol.Icom, 0, 0x7A, 19200, 1),
            new RigModel("Icom", "IC-7700",       RigProtocol.Icom, 0, 0x74, 19200, 1),
            new RigModel("Icom", "IC-7800",       RigProtocol.Icom, 0, 0x6A, 19200, 1),
            new RigModel("Icom", "IC-7850",       RigProtocol.Icom, 0, 0x8E, 115200, 1),
            new RigModel("Icom", "IC-7851",       RigProtocol.Icom, 0, 0x8E, 115200, 1),
            new RigModel("Icom", "IC-756",        RigProtocol.Icom, 0, 0x50, 19200, 1),
            new RigModel("Icom", "IC-756PRO",     RigProtocol.Icom, 0, 0x5C, 19200, 1),
            new RigModel("Icom", "IC-756PROII",   RigProtocol.Icom, 0, 0x64, 19200, 1),
            new RigModel("Icom", "IC-756PROIII",  RigProtocol.Icom, 0, 0x6E, 19200, 1),
            new RigModel("Icom", "IC-746",        RigProtocol.Icom, 0, 0x56, 9600, 1),
            new RigModel("Icom", "IC-746PRO",     RigProtocol.Icom, 0, 0x66, 9600, 1),
            new RigModel("Icom", "IC-718",        RigProtocol.Icom, 0, 0x5E, 9600, 1),
            new RigModel("Icom", "IC-78",         RigProtocol.Icom, 0, 0x62, 9600, 1),
            new RigModel("Icom", "IC-703",        RigProtocol.Icom, 0, 0x68, 9600, 1),
            new RigModel("Icom", "IC-706MKIIG",   RigProtocol.Icom, 0, 0x58, 9600, 1),
            new RigModel("Icom", "IC-706MKII",    RigProtocol.Icom, 0, 0x4E, 9600, 1),
            new RigModel("Icom", "IC-706",        RigProtocol.Icom, 0, 0x48, 9600, 1),
            new RigModel("Icom", "IC-707",        RigProtocol.Icom, 0, 0x3E, 9600, 1),
            new RigModel("Icom", "IC-736",        RigProtocol.Icom, 0, 0x40, 9600, 1),
            new RigModel("Icom", "IC-737",        RigProtocol.Icom, 0, 0x3C, 9600, 1),
            new RigModel("Icom", "IC-738",        RigProtocol.Icom, 0, 0x44, 9600, 1),
            new RigModel("Icom", "IC-775",        RigProtocol.Icom, 0, 0x46, 9600, 1),
            new RigModel("Icom", "IC-820H",       RigProtocol.Icom, 0, 0x42, 9600, 1),
            new RigModel("Icom", "IC-821H",       RigProtocol.Icom, 0, 0x4C, 9600, 1),
            new RigModel("Icom", "IC-910H",       RigProtocol.Icom, 0, 0x60, 9600, 1),
            new RigModel("Icom", "IC-970",        RigProtocol.Icom, 0, 0x2E, 9600, 1),
            // Icom plus anciens (CI-V à 1200 bauds d'origine)
            new RigModel("Icom", "IC-725",        RigProtocol.Icom, 0, 0x28, 1200, 1),
            new RigModel("Icom", "IC-726",        RigProtocol.Icom, 0, 0x30, 1200, 1),
            new RigModel("Icom", "IC-728",        RigProtocol.Icom, 0, 0x38, 1200, 1),
            new RigModel("Icom", "IC-729",        RigProtocol.Icom, 0, 0x3A, 1200, 1),
            new RigModel("Icom", "IC-735",        RigProtocol.Icom, 0, 0x04, 1200, 1) { ShortFreq = true },
            new RigModel("Icom", "IC-751/751A",   RigProtocol.Icom, 0, 0x1C, 1200, 1),
            new RigModel("Icom", "IC-761",        RigProtocol.Icom, 0, 0x1E, 1200, 1),
            new RigModel("Icom", "IC-765",        RigProtocol.Icom, 0, 0x2C, 1200, 1),
            new RigModel("Icom", "IC-781",        RigProtocol.Icom, 0, 0x26, 1200, 1),
            new RigModel("Icom", "IC-271",        RigProtocol.Icom, 0, 0x20, 1200, 1),
            new RigModel("Icom", "IC-275",        RigProtocol.Icom, 0, 0x10, 1200, 1),
            new RigModel("Icom", "IC-375",        RigProtocol.Icom, 0, 0x12, 1200, 1),
            new RigModel("Icom", "IC-471",        RigProtocol.Icom, 0, 0x22, 1200, 1),
            new RigModel("Icom", "IC-475",        RigProtocol.Icom, 0, 0x14, 1200, 1),
            new RigModel("Icom", "IC-575",        RigProtocol.Icom, 0, 0x16, 1200, 1),
            new RigModel("Icom", "IC-1275",       RigProtocol.Icom, 0, 0x18, 1200, 1),

            // Kenwood (les anciens modèles donnent le mode par la réponse IF, voir KenwoodRig)
            new RigModel("Kenwood", "TS-590S/SG", RigProtocol.Kenwood, 11, 0, 115200, 1),
            new RigModel("Kenwood", "TS-890S",    RigProtocol.Kenwood, 11, 0, 115200, 1),
            new RigModel("Kenwood", "TS-990S",    RigProtocol.Kenwood, 11, 0, 115200, 1),
            new RigModel("Kenwood", "TS-2000",    RigProtocol.Kenwood, 11, 0, 9600, 1),
            new RigModel("Kenwood", "TS-B2000",   RigProtocol.Kenwood, 11, 0, 9600, 1),
            new RigModel("Kenwood", "TS-480",     RigProtocol.Kenwood, 11, 0, 9600, 1),
            new RigModel("Kenwood", "TS-870S",    RigProtocol.Kenwood, 11, 0, 9600, 1),
            new RigModel("Kenwood", "TS-570",     RigProtocol.Kenwood, 11, 0, 9600, 1),
            new RigModel("Kenwood", "TS-850S",    RigProtocol.Kenwood, 11, 0, 4800, 2),
            new RigModel("Kenwood", "TS-50S",     RigProtocol.Kenwood, 11, 0, 4800, 2),
            new RigModel("Kenwood", "TS-60S",     RigProtocol.Kenwood, 11, 0, 4800, 2),
            new RigModel("Kenwood", "TS-450S",    RigProtocol.Kenwood, 11, 0, 4800, 2),
            new RigModel("Kenwood", "TS-690S",    RigProtocol.Kenwood, 11, 0, 4800, 2),
            new RigModel("Kenwood", "TS-790",     RigProtocol.Kenwood, 11, 0, 4800, 2),
            new RigModel("Kenwood", "TS-930S",    RigProtocol.Kenwood, 11, 0, 4800, 2),
            new RigModel("Kenwood", "TS-940S",    RigProtocol.Kenwood, 11, 0, 4800, 2),
            new RigModel("Kenwood", "TS-950S",    RigProtocol.Kenwood, 11, 0, 4800, 2),
            new RigModel("Kenwood", "TS-950SDX",  RigProtocol.Kenwood, 11, 0, 4800, 2),
            new RigModel("Kenwood", "TS-440S",    RigProtocol.Kenwood, 11, 0, 4800, 2),
            // Sans lecture de l'état émission/réception (Hamlib ne la propose pas pour ces postes)
            new RigModel("Kenwood", "TS-140S",    RigProtocol.Kenwood, 11, 0, 4800, 2) { NoTx = true },
            new RigModel("Kenwood", "TS-680S",    RigProtocol.Kenwood, 11, 0, 4800, 2) { NoTx = true },
            new RigModel("Kenwood", "TS-711",     RigProtocol.Kenwood, 11, 0, 4800, 2) { NoTx = true },
            new RigModel("Kenwood", "TS-811",     RigProtocol.Kenwood, 11, 0, 4800, 2) { NoTx = true },

            // Elecraft (compatible Kenwood)
            new RigModel("Elecraft", "K4",        RigProtocol.Elecraft, 11, 0, 38400, 1),
            new RigModel("Elecraft", "K3/K3S",    RigProtocol.Elecraft, 11, 0, 38400, 1),
            new RigModel("Elecraft", "KX3",       RigProtocol.Elecraft, 11, 0, 38400, 1),
            new RigModel("Elecraft", "KX2",       RigProtocol.Elecraft, 11, 0, 38400, 1),
            new RigModel("Elecraft", "K2",        RigProtocol.Elecraft, 11, 0, 4800, 1),

            // Xiegu (protocole CI-V compatible Icom ; adresses par défaut d'après Hamlib, modifiables si besoin)
            new RigModel("Xiegu", "G90",          RigProtocol.Icom, 0, 0x88, 19200, 1),
            new RigModel("Xiegu", "X108G",        RigProtocol.Icom, 0, 0x70, 19200, 1),
            new RigModel("Xiegu", "X5105",        RigProtocol.Icom, 0, 0x70, 19200, 1),
            new RigModel("Xiegu", "X6100",        RigProtocol.Icom, 0, 0xA4, 19200, 1),
            new RigModel("Xiegu", "X6200",        RigProtocol.Icom, 0, 0xA4, 19200, 1),
        };

        // Modèles d'une marque, dans l'ordre des numéros (FT-100, FT-450..., FT-2000, puis FTdx..., FTX-1).
        public static List<RigModel> ForBrand(string brand)
        {
            List<RigModel> list = Models.FindAll(delegate(RigModel m) { return m.Brand == brand; });
            list.Sort(delegate(RigModel a, RigModel b) { return CompareNames(a.Name, b.Name); });
            return list;
        }

        // Tri « naturel » : d'abord les lettres du début (FT, FTDX, FTX ; tirets ignorés), puis le numéro, puis le reste.
        public static int CompareNames(string a, string b)
        {
            string pa, ra, pb, rb;
            long na = SplitName(a, out pa, out ra), nb = SplitName(b, out pb, out rb);
            int c = string.CompareOrdinal(pa, pb);
            if (c != 0) return c;
            c = na.CompareTo(nb);
            if (c != 0) return c;
            return string.Compare(ra, rb, StringComparison.OrdinalIgnoreCase);
        }

        static long SplitName(string name, out string prefix, out string rest)
        {
            int i = 0;
            System.Text.StringBuilder p = new System.Text.StringBuilder();
            while (i < name.Length && !char.IsDigit(name[i]))
            {
                if (char.IsLetter(name[i])) p.Append(char.ToUpperInvariant(name[i]));
                i++;
            }
            long n = 0;
            int start = i;
            while (i < name.Length && char.IsDigit(name[i]) && i - start < 9) n = n * 10 + (name[i++] - '0');
            prefix = p.ToString();
            rest = name.Substring(i);
            return n;
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
