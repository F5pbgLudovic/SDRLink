using System;
using System.Collections.Generic;
using System.Globalization;

namespace SDRLink
{
    // Liste « Modes numériques » des mémoires : fréquences habituelles (fréquence affichée par le récepteur, en kHz) du mode numérique
    // choisi, bandes de 1,8 à 440 MHz, selon la région IARU (1 Europe-Afrique, 2 Amériques, 3 Asie-Pacifique). Même table que PhoneSDRLink.
    // Sources : fréquences par défaut de WSJT-X (FT8, FT4, JT9, WSPR) et de JS8Call (JS8), plans de bande IARU et usages courants (PSK31,
    // RTTY, SSTV, CW QRP, FreeDV), listes publiées (Olivia, Contestia, Hell, stations WEFAX). Ce sont des fréquences d'usage : elles peuvent
    // changer, et ne remplacent pas le plan de bande de chaque pays.
    public static class DigitalFreqs
    {
        class Row
        {
            public string Mode, SdrMode, Regions, Note;
            public double KHz;
        }

        static Row R(string mode, double khz, string sdrMode, string regions, string note)
        {
            return new Row { Mode = mode, KHz = khz, SdrMode = sdrMode, Regions = regions, Note = note };
        }

        // Une ligne : mode, kHz, mode du récepteur, régions ("a" = toutes, sinon chiffres), remarque.
        static readonly Row[] T =
        {
            // FT8
            R("FT8", 1840.0, "USB", "a", ""), R("FT8", 3573.0, "USB", "a", ""), R("FT8", 5357.0, "USB", "a", ""), R("FT8", 7074.0, "USB", "a", ""),
            R("FT8", 10136.0, "USB", "a", ""), R("FT8", 14074.0, "USB", "a", ""), R("FT8", 18100.0, "USB", "a", ""), R("FT8", 21074.0, "USB", "a", ""),
            R("FT8", 24915.0, "USB", "a", ""), R("FT8", 28074.0, "USB", "a", ""), R("FT8", 50313.0, "USB", "a", ""), R("FT8", 50323.0, "USB", "a", ""),
            R("FT8", 70154.0, "USB", "1", ""), R("FT8", 144174.0, "USB", "a", ""), R("FT8", 432174.0, "USB", "a", ""),
            // FT4
            R("FT4", 3575.0, "USB", "a", ""), R("FT4", 3568.0, "USB", "3", ""), R("FT4", 7047.5, "USB", "a", ""), R("FT4", 10140.0, "USB", "a", ""),
            R("FT4", 14080.0, "USB", "a", ""), R("FT4", 18104.0, "USB", "a", ""), R("FT4", 21140.0, "USB", "a", ""), R("FT4", 24919.0, "USB", "a", ""),
            R("FT4", 28180.0, "USB", "a", ""), R("FT4", 50318.0, "USB", "a", ""), R("FT4", 144170.0, "USB", "a", ""),
            // FT2
            R("FT2", 1843.0, "USB", "a", ""), R("FT2", 3578.0, "USB", "a", ""), R("FT2", 5360.0, "USB", "a", ""), R("FT2", 7052.0, "USB", "a", ""),
            R("FT2", 7062.0, "USB", "a", ""), R("FT2", 10144.0, "USB", "a", ""), R("FT2", 14084.0, "USB", "a", ""), R("FT2", 18108.0, "USB", "a", ""),
            R("FT2", 21144.0, "USB", "a", ""), R("FT2", 24923.0, "USB", "a", ""),
            // WSPR
            R("WSPR", 1836.6, "USB", "a", ""), R("WSPR", 3568.6, "USB", "a", ""), R("WSPR", 7038.6, "USB", "a", ""), R("WSPR", 10138.7, "USB", "a", ""),
            R("WSPR", 14095.6, "USB", "a", ""), R("WSPR", 18104.6, "USB", "a", ""), R("WSPR", 21094.6, "USB", "a", ""), R("WSPR", 24924.6, "USB", "a", ""),
            R("WSPR", 28124.6, "USB", "a", ""), R("WSPR", 50293.0, "USB", "a", ""), R("WSPR", 70091.0, "USB", "1", ""), R("WSPR", 144489.0, "USB", "a", ""),
            R("WSPR", 432300.0, "USB", "a", ""),
            // JT9
            R("JT9", 1839.0, "USB", "a", ""), R("JT9", 3572.0, "USB", "a", ""), R("JT9", 7078.0, "USB", "a", ""), R("JT9", 10140.0, "USB", "a", ""),
            R("JT9", 14078.0, "USB", "a", ""), R("JT9", 18104.0, "USB", "a", ""), R("JT9", 21078.0, "USB", "a", ""), R("JT9", 24919.0, "USB", "a", ""),
            R("JT9", 28078.0, "USB", "a", ""), R("JT9", 50312.0, "USB", "a", ""), R("JT9", 70104.0, "USB", "1", ""),
            // JS8
            R("JS8", 1843.5, "USB", "a", ""), R("JS8", 3578.0, "USB", "a", ""), R("JS8", 5363.0, "USB", "a", ""), R("JS8", 7078.0, "USB", "a", ""),
            R("JS8", 10130.0, "USB", "a", ""), R("JS8", 14078.0, "USB", "a", ""), R("JS8", 18104.0, "USB", "a", ""), R("JS8", 21078.0, "USB", "a", ""),
            R("JS8", 24922.0, "USB", "a", ""), R("JS8", 28078.0, "USB", "a", ""), R("JS8", 50318.0, "USB", "a", ""), R("JS8", 144178.0, "USB", "a", ""),
            // PSK
            R("PSK", 1838.15, "USB", "a", ""), R("PSK", 3580.15, "USB", "a", ""), R("PSK", 7035.15, "USB", "13", ""), R("PSK", 7080.15, "USB", "2", ""),
            R("PSK", 10142.15, "USB", "a", ""), R("PSK", 14070.15, "USB", "a", ""), R("PSK", 18100.15, "USB", "a", ""), R("PSK", 21080.15, "USB", "a", ""),
            R("PSK", 24920.15, "USB", "a", ""), R("PSK", 28120.15, "USB", "a", ""), R("PSK", 50290.15, "USB", "a", ""),
            // RTTY
            R("RTTY", 3590.0, "USB", "a", ""), R("RTTY", 7040.0, "USB", "13", ""), R("RTTY", 7080.0, "USB", "2", ""), R("RTTY", 10140.0, "USB", "a", ""),
            R("RTTY", 14080.0, "USB", "a", ""), R("RTTY", 18105.0, "USB", "a", ""), R("RTTY", 21080.0, "USB", "a", ""), R("RTTY", 24920.0, "USB", "a", ""),
            R("RTTY", 28080.0, "USB", "a", ""),
            // Olivia
            R("Olivia", 1825.4, "USB", "a", "8/250"), R("Olivia", 3571.4, "USB", "a", "8/250"), R("Olivia", 3576.5, "USB", "a", "32/1000"), R("Olivia", 3581.75, "USB", "a", "16/500"),
            R("Olivia", 7071.4, "USB", "a", "8/250"), R("Olivia", 7071.75, "USB", "a", "16/500"), R("Olivia", 10141.4, "USB", "a", "8/250"), R("Olivia", 14071.4, "USB", "a", "8/250"),
            R("Olivia", 14073.9, "USB", "a", "16/500"), R("Olivia", 14105.0, "USB", "a", "32/1000"), R("Olivia", 18101.4, "USB", "a", "8/250"), R("Olivia", 21071.4, "USB", "a", "8/250"),
            R("Olivia", 21152.0, "USB", "a", "32/1000"), R("Olivia", 24921.4, "USB", "a", "8/250"), R("Olivia", 28075.25, "USB", "a", "16/500"), R("Olivia", 28121.4, "USB", "a", "8/250"),
            R("Olivia", 50290.75, "USB", "a", "16/500"), R("Olivia", 144134.75, "USB", "a", "16/500"),
            // Contestia
            R("Contestia", 3582.5, "USB", "a", "8/250"), R("Contestia", 7042.5, "USB", "a", "8/250"), R("Contestia", 14072.5, "USB", "a", "8/250"),
            // Hell
            R("Hell", 3585.0, "USB", "a", "FM Hell 105"), R("Hell", 3592.0, "USB", "a", "Feld Hell"), R("Hell", 7046.0, "USB", "a", "Feld Hell"), R("Hell", 7069.5, "USB", "a", "FM Hell 105"),
            R("Hell", 10142.5, "USB", "a", "FM Hell 105"), R("Hell", 10144.0, "USB", "a", ""), R("Hell", 14063.0, "USB", "a", "Feld Hell"), R("Hell", 14073.0, "USB", "a", "Feld Hell"),
            R("Hell", 18098.0, "USB", "a", ""),
            // WEFAX
            R("WEFAX", 2616.6, "USB", "a", "Northwood 2618.5"), R("WEFAX", 3853.1, "USB", "a", "DWD DDH3 3855"), R("WEFAX", 4233.1, "USB", "a", "Boston NMF 4235"), R("WEFAX", 4296.1, "USB", "a", "Kodiak NOJ 4298"),
            R("WEFAX", 4344.1, "USB", "a", "Pt Reyes NMC 4346"), R("WEFAX", 4608.1, "USB", "a", "Northwood 4610"), R("WEFAX", 6338.6, "USB", "a", "Boston NMF 6340.5"), R("WEFAX", 6832.1, "USB", "a", "Northwood 6834"),
            R("WEFAX", 7793.1, "USB", "a", "Tokyo JMH 7795"), R("WEFAX", 7878.1, "USB", "a", "DWD DDK3 7880"), R("WEFAX", 8038.1, "USB", "a", "Northwood 8040"), R("WEFAX", 8457.1, "USB", "a", "Kodiak NOJ 8459"),
            R("WEFAX", 8680.1, "USB", "a", "Pt Reyes NMC 8682"), R("WEFAX", 9108.1, "USB", "a", "Boston NMF 9110"), R("WEFAX", 11084.6, "USB", "a", "Northwood 11086.5"), R("WEFAX", 12388.1, "USB", "a", "Northwood 12390"),
            R("WEFAX", 12410.6, "USB", "a", "Kodiak NOJ 12412.5"), R("WEFAX", 12748.1, "USB", "a", "Boston NMF 12750"), R("WEFAX", 12784.1, "USB", "a", "Pt Reyes NMC 12786"), R("WEFAX", 13595.1, "USB", "a", "Tokyo JMH 13597"),
            R("WEFAX", 13880.6, "USB", "a", "DWD DDK9 13882.5"), R("WEFAX", 17149.3, "USB", "a", "Pt Reyes NMC 17151.2"), R("WEFAX", 18218.1, "USB", "a", "Tokyo JMH 18220"), R("WEFAX", 18259.1, "USB", "a", "Northwood 18261"),
            R("WEFAX", 22540.1, "USB", "a", "Northwood 22542"),
            // SSTV
            R("SSTV", 3730.0, "LSB", "13", ""), R("SSTV", 3845.0, "LSB", "2", ""), R("SSTV", 7033.0, "LSB", "13", ""), R("SSTV", 7171.0, "LSB", "2", ""),
            R("SSTV", 14230.0, "USB", "a", ""), R("SSTV", 21340.0, "USB", "a", ""), R("SSTV", 28680.0, "USB", "a", ""), R("SSTV", 144500.0, "FM", "1", ""),
            // CW
            R("CW", 1836.0, "CW", "a", "QRP"), R("CW", 3560.0, "CW", "a", "QRP"), R("CW", 7030.0, "CW", "a", "QRP"), R("CW", 10106.0, "CW", "a", "QRP"),
            R("CW", 14060.0, "CW", "a", "QRP"), R("CW", 18086.0, "CW", "a", "QRP"), R("CW", 21060.0, "CW", "a", "QRP"), R("CW", 24906.0, "CW", "a", "QRP"),
            R("CW", 28060.0, "CW", "a", "QRP"), R("CW", 50090.0, "CW", "a", ""), R("CW", 70050.0, "CW", "1", ""), R("CW", 144050.0, "CW", "a", ""),
            R("CW", 222050.0, "CW", "2", ""), R("CW", 432050.0, "CW", "a", ""),
            // FreeDV
            R("FreeDV", 1997.0, "LSB", "2", ""), R("FreeDV", 3625.0, "LSB", "13", ""), R("FreeDV", 3850.0, "LSB", "2", ""), R("FreeDV", 7177.0, "LSB", "a", ""),
            R("FreeDV", 7197.0, "LSB", "2", ""), R("FreeDV", 14236.0, "USB", "a", ""), R("FreeDV", 18118.0, "USB", "a", ""), R("FreeDV", 21313.0, "USB", "a", ""),
            R("FreeDV", 24933.0, "USB", "a", ""), R("FreeDV", 28330.0, "USB", "a", ""),
        };

        // Modes de la liste : ceux du bouton des modes numériques, et FreeDV (RADE : mêmes fréquences d'appel).
        public static readonly string[] ModesShown = { "FT8", "FT4", "FT2", "WSPR", "JT9", "JS8", "CW", "RTTY", "PSK", "Olivia", "Contestia", "Hell", "SSTV", "WEFAX", "FreeDV" };

        public static string Band(double khz)
        {
            if (khz < 2000) return "160 m";
            if (khz < 4000) return "80 m";
            if (khz < 5500) return "60 m";
            if (khz < 7500) return "40 m";
            if (khz < 10200) return "30 m";
            if (khz < 14500) return "20 m";
            if (khz < 18200) return "17 m";
            if (khz < 21500) return "15 m";
            if (khz < 25000) return "12 m";
            if (khz < 30000) return "10 m";
            if (khz < 55000) return "6 m";
            if (khz < 80000) return "4 m";
            if (khz < 150000) return "2 m";
            if (khz < 250000) return "1.25 m";
            return "70 cm";
        }

        // Fréquences du mode (nom de ModesShown) pour la région IARU 1, 2 ou 3, dans l'ordre croissant. Mémoires hors des listes enregistrées.
        // shownName : nom affiché du mode (ex. PSK63 au lieu de PSK), null = nom de la table.
        public static List<Memory> List(string mode, int region, string shownName)
        {
            List<Memory> out_ = new List<Memory>();
            string r = region.ToString(CultureInfo.InvariantCulture);
            List<Row> rows = new List<Row>();
            foreach (Row row in T)
                if (row.Mode == mode && (row.Regions == "a" || row.Regions.Contains(r))) rows.Add(row);
            rows.Sort(delegate(Row a, Row b) { return a.KHz.CompareTo(b.KHz); });
            foreach (Row row in rows)
            {
                string label = (shownName ?? mode) + " " + Band(row.KHz) + (row.Note == "" ? "" : " " + row.Note) + (row.Regions == "a" ? "" : " (" + Regs(row.Regions) + ")");
                out_.Add(new Memory(label, (long)Math.Round(row.KHz * 1000), row.SdrMode));
            }
            return out_;
        }

        static string Regs(string reg)
        {
            string s = "";
            for (int i = 0; i < reg.Length; i++) s += (i > 0 ? "/" : "") + "R" + reg[i];
            return s;
        }

        public static string RegionName(int region)
        {
            return region == 2 ? Lang.T("Région IARU 2 (Amériques)", "IARU Region 2 (Americas)")
                : region == 3 ? Lang.T("Région IARU 3 (Asie-Pacifique)", "IARU Region 3 (Asia-Pacific)")
                : Lang.T("Région IARU 1 (Europe, Afrique)", "IARU Region 1 (Europe, Africa)");
        }
    }
}