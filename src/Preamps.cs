using System;
using System.Collections.Generic;

namespace SDRLink
{
    // Préampli du poste, rappelé avec une mémoire (1.41). Code commun à toutes les marques :
    //   Off  = sans préampli (IPO chez Yaesu, OFF chez Icom / Kenwood / Elecraft ; IPO1 sur le FTdx5000)
    //   Amp1 = préampli 1 (AMP1, P.AMP1, préampli unique)      Amp2 = préampli 2 (AMP2, P.AMP2, PRE2)
    //   Ipo2 = IPO2 (FTdx5000 seulement : préampli contourné, attaque directe du 1er mélangeur)
    // Un poste qui n'a pas le réglage demandé reçoit le plus proche (PreampSet.Map).
    //
    // Commandes (relevé du 2026-11-16 : manuels CAT des fabricants, recoupés avec Hamlib et flrig). Seuls les postes dont la
    // commande est confirmée sont pilotés ; les autres (anciens postes, TS-870S…) n'ont pas de préampli
    // (FTX-1, IC-78 et Xiegu : d'après Hamlib seulement, voir plus bas)
    // dans SDRLink plutôt qu'une commande devinée.
    //   Yaesu      PA0n;   n = 0 IPO, 1 AMP1, 2 AMP2 (FTdx5000 : 3 = IPO2 ; FT-891, FT-450, FTdx9000 : 0 / 1 seulement)
    //   Icom       CI-V 16 02 nn   00 OFF, 01 P.AMP1, 02 P.AMP2 (IC-9700 : 02 = préampli EXTERNE, jamais envoyé)
    //   Kenwood    PA0; / PA1;  (TS-890S : PA2; = PRE 2 ; TS-990S : PA00; / PA01; récepteur principal)
    //   Elecraft   PA0; / PA1; (K3 : PA2; = préampli 2, 12-6 m) ; K4 : PA00; / PA11; / PA21;
    public static class Preamps
    {
        public const int Off = 0, Amp1 = 1, Amp2 = 2, Ipo2 = 3, MaxCode = 3;

        // Réglages proposés dans la fenêtre des mémoires (poste connecté, ou modèle choisi) ; null = réglages courants.
        public static PreampSet Current;

        // Libellé neutre d'un code, quand le poste n'est pas connu.
        public static string GenericLabel(int code)
        {
            switch (code)
            {
                case Off: return Lang.T("Sans préampli (IPO)", "No preamp (IPO)");
                case Amp1: return Lang.T("Préampli 1 (AMP1)", "Preamp 1 (AMP1)");
                case Amp2: return Lang.T("Préampli 2 (AMP2)", "Preamp 2 (AMP2)");
                case Ipo2: return "IPO2";
            }
            return "";
        }

        // Proposé quand aucun poste n'est connu : les trois réglages courants.
        public static PreampSet Generic
        {
            get { return new PreampSet(null, new[] { Off, Amp1, Amp2 }, new[] { GenericLabel(Off), GenericLabel(Amp1), GenericLabel(Amp2) }, null); }
        }

        static readonly string[] Yaesu3 = { "FT-2000", "FTdx10", "FTdx101D/MP", "FT-991/991A", "FT-710", "FTdx3000", "FTdx1200", "FT-950" };
        static readonly string[] Yaesu2 = { "FT-891", "FT-450/450D", "FTdx9000" };
        static readonly string[] Icom3 = { "IC-7300", "IC-7300MK2", "IC-7410", "IC-7600", "IC-7800", "IC-7850", "IC-7851", "IC-7400",
            "IC-746PRO", "IC-746", "IC-756PRO", "IC-756PROII", "IC-756PROIII", "IC-9100", "IC-7610", "IC-7760", "IC-7700", "IC-705", "IC-7100" };
        static readonly string[] Icom2 = { "IC-7000", "IC-7200", "IC-910H", "IC-905", "IC-718", "IC-703", "IC-706MKIIG", "IC-9700" };
        static readonly string[] Kenwood1 = { "TS-590S/SG", "TS-2000", "TS-B2000", "TS-480", "TS-570" };

        // Réglages du modèle (liaison par port COM, ou poste reconnu derrière OmniRig / Hamlib) ; null : pas de commande connue.
        public static PreampSet ForModel(RigModel m)
        {
            if (m == null) return null;
            string n = m.Name;
            if (m.Protocol == RigProtocol.Yaesu)
            {
                if (n == "FTdx5000")
                    return new PreampSet(n, new[] { Off, Ipo2, Amp1, Amp2 }, new[] { "IPO1", "IPO2", "AMP1", "AMP2" }, new[] { "PA00;", "PA03;", "PA01;", "PA02;" });
                if (Array.IndexOf(Yaesu3, n) >= 0)
                    return new PreampSet(n, new[] { Off, Amp1, Amp2 }, new[] { "IPO", "AMP1", "AMP2" }, new[] { "PA00;", "PA01;", "PA02;" });
                if (Array.IndexOf(Yaesu2, n) >= 0)
                    return new PreampSet(n, new[] { Off, Amp1 }, new[] { "IPO", "AMP" }, new[] { "PA00;", "PA01;" });
                // FTX-1 (d'après Hamlib ftx1_preamp.c, qui cite le manuel CAT p. 21 ; non vérifié sur le manuel) : PA P1 P2, P1 = 0 HF/50 MHz
                // (IPO / AMP1 / AMP2), 1 = 144 MHz, 2 = 430 MHz (OFF / ON) ; la bande est choisie par YaesuRig selon la fréquence.
                if (n == "FTX-1")
                    return new PreampSet(n, new[] { Off, Amp1, Amp2 }, new[] { "IPO", "AMP1", "AMP2" }, new[] { "PA00;", "PA01;", "PA02;" });
                return null;
            }
            if (m.Protocol == RigProtocol.Icom)
            {
                // Xiegu (G90, X108G, X5105, X6100, X6200) et IC-78 : 16 02 00 / 01 d'après Hamlib (non confirmé par un manuel ; une
                // commande refusée est simplement répondue « NG » par le poste). IC-78 : un seul préampli proposé (Hamlib en liste deux, douteux).
                if (m.Brand == "Xiegu" || n == "IC-78") return new PreampSet(n, new[] { Off, Amp1 }, new[] { "OFF", "P.AMP" }, new[] { "0", "1" });
                if (Array.IndexOf(Icom3, n) >= 0)
                    return new PreampSet(n, new[] { Off, Amp1, Amp2 }, new[] { "OFF", "P.AMP1", "P.AMP2" }, new[] { "0", "1", "2" });
                if (Array.IndexOf(Icom2, n) >= 0)
                    return new PreampSet(n, new[] { Off, Amp1 }, new[] { "OFF", "P.AMP" }, new[] { "0", "1" });
                return null;
            }
            if (m.Protocol == RigProtocol.Kenwood)
            {
                if (n == "TS-890S")
                    return new PreampSet(n, new[] { Off, Amp1, Amp2 }, new[] { "OFF", "PRE 1", "PRE 2" }, new[] { "PA0;", "PA1;", "PA2;" });
                if (n == "TS-990S")
                    return new PreampSet(n, new[] { Off, Amp1 }, new[] { "OFF", "PRE" }, new[] { "PA00;", "PA01;" });
                if (Array.IndexOf(Kenwood1, n) >= 0)
                    return new PreampSet(n, new[] { Off, Amp1 }, new[] { "OFF", "PRE" }, new[] { "PA0;", "PA1;" });
                return null;
            }
            if (m.Protocol == RigProtocol.Elecraft)
            {
                if (n == "K4")
                    return new PreampSet(n, new[] { Off, Amp1, Amp2 }, new[] { "OFF", "PRE 10 dB", "PRE 2" }, new[] { "PA00;", "PA11;", "PA21;" });
                if (n == "K3/K3S")
                    return new PreampSet(n, new[] { Off, Amp1, Amp2 }, new[] { "OFF", "PRE", "PRE 2" }, new[] { "PA0;", "PA1;", "PA2;" });
                return new PreampSet(n, new[] { Off, Amp1 }, new[] { "OFF", "PRE" }, new[] { "PA0;", "PA1;" });   // KX3, KX2, K2
            }
            return null;   // anciens Yaesu à protocole binaire : pas de commande de préampli
        }

        // Commande texte du réglage pour un poste Yaesu / Kenwood / Elecraft ; FTX-1 : P1 selon la bande (0 jusqu'à 60 MHz, 1 en 144,
        // 2 en 430), un seul préampli (ON) en VHF/UHF.
        public static string AsciiWire(PreampSet s, int code, long hz)
        {
            if (s == null) return null;
            if (s.Rig != "FTX-1" || hz <= 60000000) return s.WireOf(code);
            int c = s.Map(code);
            return "PA" + (hz > 300000000 ? "2" : "1") + (c == Off ? "0" : "1") + ";";
        }

        // Postes Icom à un seul préampli en VHF/UHF (deux en HF/50 MHz).
        public static bool OnePreampAbove60MHz(string rig) { return rig == "IC-705" || rig == "IC-7100" || rig == "IC-9100"; }

        // Nom de poste annoncé par OmniRig ou Hamlib (« FTDX-101D », « IC7300 »...) → modèle du catalogue, ou null.
        public static RigModel FindModel(string name)
        {
            string k = Key(name);
            if (k.Length < 3) return null;
            foreach (RigModel m in RigCatalog.Models) foreach (string v in Variants(m.Name)) if (Key(v) == k) return m;
            return null;
        }

        // « FTdx101D/MP » → FTdx101D, FTdx101MP ; « FT-991/991A » → FT-991, FT-991A ; « TS-590S/SG » → TS-590S, TS-590SG.
        static IEnumerable<string> Variants(string name)
        {
            string[] p = name.Split('/');
            yield return p[0];
            for (int i = 1; i < p.Length; i++)
            {
                int lead = 0;
                while (lead < p[0].Length && !char.IsDigit(p[0][lead])) lead++;
                if (p[i].Length > 0 && char.IsDigit(p[i][0])) yield return p[0].Substring(0, lead) + p[i];
                else yield return p[0].Substring(0, Math.Max(0, p[0].Length - p[i].Length + (p[i].Length > 1 ? 1 : 0))) + p[i];
            }
        }

        static string Key(string s)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            foreach (char ch in s ?? "") if (char.IsLetterOrDigit(ch)) b.Append(char.ToUpperInvariant(ch));
            string r = b.ToString();
            foreach (string brand in new[] { "YAESU", "ICOM", "KENWOOD", "ELECRAFT" }) if (r.StartsWith(brand)) r = r.Substring(brand.Length);
            return r;
        }
    }

    // Réglages de préampli d'un poste : codes (Preamps.Off...), leur nom sur ce poste, et la commande de chacun (Wire :
    // texte CAT pour Yaesu / Kenwood / Elecraft, octet CI-V en chiffre pour Icom, valeur pour Hamlib, index pour HRD).
    public class PreampSet
    {
        public readonly string Rig;        // nom du poste (journal), peut être null
        public readonly int[] Codes;
        public readonly string[] Labels;
        public readonly string[] Wire;

        public PreampSet(string rig, int[] codes, string[] labels, string[] wire)
        {
            Rig = rig;
            Codes = codes;
            Labels = labels;
            Wire = wire;
        }

        public bool Has(int code) { return Array.IndexOf(Codes, code) >= 0; }

        // Code ramené à ce que le poste sait faire : AMP2 → AMP1 sur un poste à un seul préampli, IPO2 → IPO ailleurs
        // que sur le FTdx5000.
        public int Map(int code)
        {
            if (Has(code)) return code;
            if (code == Preamps.Amp2 && Has(Preamps.Amp1)) return Preamps.Amp1;
            if (code == Preamps.Amp1 && Has(Preamps.Amp2)) return Preamps.Amp2;
            return Preamps.Off;
        }

        public string LabelOf(int code)
        {
            int i = Array.IndexOf(Codes, code);
            return i >= 0 ? Labels[i] : Preamps.GenericLabel(code);
        }

        // Commande du réglage le plus proche de `code`.
        public string WireOf(int code)
        {
            int i = Array.IndexOf(Codes, Map(code));
            return i >= 0 && Wire != null ? Wire[i] : null;
        }

        // Même réglages sans IPO2 (Hamlib ne connaît pas l'IPO2 du FTdx5000) et limités à `levels` préamplis.
        public PreampSet Limit(int levels, string[] wireOffAmp1Amp2)
        {
            List<int> c = new List<int>();
            List<string> l = new List<string>(), w = new List<string>();
            for (int i = 0; i < Codes.Length; i++)
            {
                int code = Codes[i];
                if (code == Preamps.Ipo2 || (code == Preamps.Amp1 && levels < 1) || (code == Preamps.Amp2 && levels < 2)) continue;
                c.Add(code); l.Add(Labels[i]); w.Add(wireOffAmp1Amp2[code]);
            }
            return new PreampSet(Rig, c.ToArray(), l.ToArray(), w.ToArray());
        }

        public string Describe() { return (Rig != null ? Rig + " : " : "") + string.Join(" / ", Labels); }
    }
}
