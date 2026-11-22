using System;
using System.Globalization;

namespace SDRLink
{
    // Bandes radioamateurs 160 à 6 m (limites selon la région IARU) et mode usuel de chaque bande : LSB en 160, 80 et 40 m,
    // USB sur 60 m et à partir de 30 m. Sert au scan : « Scan de la bande », scan prêt qui suit la bande, mode proposé dans
    // « Conf scan ». Même table que PhoneSDRLink.
    public static class Bands
    {
        // nom, limites basse et haute en Hz pour les régions 1, 2 et 3, mode usuel
        static readonly object[][] Table =
        {
            new object[] { "160", new long[] { 1810000, 2000000 }, new long[] { 1800000, 2000000 }, new long[] { 1800000, 2000000 }, Modes.LSB },
            new object[] { "80", new long[] { 3500000, 3800000 }, new long[] { 3500000, 4000000 }, new long[] { 3500000, 3900000 }, Modes.LSB },
            new object[] { "60", new long[] { 5250000, 5450000 }, new long[] { 5250000, 5450000 }, new long[] { 5250000, 5450000 }, Modes.USB },
            new object[] { "40", new long[] { 7000000, 7200000 }, new long[] { 7000000, 7300000 }, new long[] { 7000000, 7200000 }, Modes.LSB },
            new object[] { "30", new long[] { 10100000, 10150000 }, new long[] { 10100000, 10150000 }, new long[] { 10100000, 10150000 }, Modes.USB },
            new object[] { "20", new long[] { 14000000, 14350000 }, new long[] { 14000000, 14350000 }, new long[] { 14000000, 14350000 }, Modes.USB },
            new object[] { "17", new long[] { 18068000, 18168000 }, new long[] { 18068000, 18168000 }, new long[] { 18068000, 18168000 }, Modes.USB },
            new object[] { "15", new long[] { 21000000, 21450000 }, new long[] { 21000000, 21450000 }, new long[] { 21000000, 21450000 }, Modes.USB },
            new object[] { "12", new long[] { 24890000, 24990000 }, new long[] { 24890000, 24990000 }, new long[] { 24890000, 24990000 }, Modes.USB },
            new object[] { "10", new long[] { 28000000, 29700000 }, new long[] { 28000000, 29700000 }, new long[] { 28000000, 29700000 }, Modes.USB },
            new object[] { "6", new long[] { 50000000, 52000000 }, new long[] { 50000000, 54000000 }, new long[] { 50000000, 54000000 }, Modes.USB },
            // Bande étendue du 27 MHz (26,000 à 28,000 MHz) : après le 10 m, donc 28,000 MHz reste au 10 m (comme PhoneSDRLink 1.33).
            new object[] { "27", new long[] { 26000000, 28000000 }, new long[] { 26000000, 28000000 }, new long[] { 26000000, 28000000 }, Modes.AM },
            // 4 m (région 1 seulement), 2 m et 70 cm (1.52) : USB, comme la BLU au-dessus de 10 MHz ; le FM choisi par l'opérateur est retenu.
            new object[] { "4", new long[] { 70000000, 70500000 }, new long[] { 0, 0 }, new long[] { 0, 0 }, Modes.USB },
            new object[] { "2", new long[] { 144000000, 146000000 }, new long[] { 144000000, 148000000 }, new long[] { 144000000, 148000000 }, Modes.USB },
            new object[] { "70cm", new long[] { 430000000, 440000000 }, new long[] { 420000000, 450000000 }, new long[] { 430000000, 440000000 }, Modes.USB },
        };

        static int region;   // 1, 2 ou 3 ; 0 = pas encore déterminée

        // Région IARU d'après le pays réglé dans Windows (Amériques = 2, Asie et Océanie = 3, sinon 1).
        public static int Region()
        {
            if (region != 0) return region;
            string c = "";
            try { c = RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant(); } catch (Exception) { }
            const string r2 = " US CA MX BR AR CL CO PE VE EC UY PY BO CU DO GT HN SV NI CR PA PR JM TT BS BB ";
            const string r3 = " JP CN IN AU NZ KR KP TW HK SG MY TH VN ID PH PK BD LK NP MM KH LA MN FJ PG ";
            region = r2.Contains(" " + c + " ") ? 2 : r3.Contains(" " + c + " ") ? 3 : 1;
            return region;
        }

        // Indice de la bande qui contient hz, -1 si aucune.
        public static int At(long hz)
        {
            if (hz <= 0) return -1;
            int r = Region();
            for (int i = 0; i < Table.Length; i++)
            {
                long[] e = (long[])Table[i][r];
                if (hz >= e[0] && hz <= e[1]) return i;
            }
            return -1;
        }

        // Mode retenu par l'opérateur pour chaque bande (nom de bande -> mode ; réglage BandModes), comme PhoneSDRLink 1.31.
        public static readonly System.Collections.Generic.Dictionary<string, string> Remembered = new System.Collections.Generic.Dictionary<string, string>();

        // Mode à prendre en arrivant dans la bande : celui retenu, sinon le mode usuel.
        public static string ModeOf(int i)
        {
            string m;
            return Remembered.TryGetValue(Name(i), out m) && !string.IsNullOrEmpty(m) ? m : DefMode(i);
        }

        public static bool IsCb(int i) { return Name(i) == "27"; }

        // Nom pour le journal : « 40 m », « 27 MHz », « 70 cm ».
        public static string Label(int i) { string n = Name(i); return n == "27" ? "27 MHz" : n == "70cm" ? "70 cm" : n + " m"; }
        public static bool IsCm(int i) { return Name(i) == "70cm"; }

        // Pour le mode par bande : la bande qui contient hz, sinon la plus proche à moins de 0,5 MHz de ses limites (on prend
        // alors le mode de cette bande, comme si l'on y était) ; -1 si aucune.
        public const long NearMargin = 500000;
        public static int Near(long hz)
        {
            int b = At(hz);
            if (b >= 0 || hz <= 0) return b;
            int r = Region();
            long best = NearMargin + 1;
            for (int i = 0; i < Table.Length; i++)
            {
                long[] e = (long[])Table[i][r];
                if (e[1] <= 0) continue;   // bande absente de cette région
                long d = hz < e[0] ? e[0] - hz : hz - e[1];
                if (d <= NearMargin && d < best) { best = d; b = i; }
            }
            return b;
        }

        public static string Name(int i) { return (string)Table[i][0]; }
        public static string DefMode(int i) { return (string)Table[i][4]; }
        public static long Lo(int i) { return ((long[])Table[i][Region()])[0]; }
        public static long Hi(int i) { return ((long[])Table[i][Region()])[1]; }
    }
}
