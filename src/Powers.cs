using System;

namespace SDRLink
{
    // Puissance d'émission du poste, rappelée avec une mémoire (1.42). La mémoire garde des watts (-1 = puissance inchangée).
    //
    // Précautions (une puissance trop forte peut abîmer un amplificateur ou une antenne) :
    //   - rien n'est envoyé tant que la mémoire n'a pas de puissance (champ vide par défaut) ;
    //   - plafond général (Powers.Limit, réglé dans la fenêtre d'une mémoire) : une valeur au-dessus est ramenée au plafond ;
    //   - jamais pendant l'émission : la commande attend le retour en réception (RigWorker) ;
    //   - seuls les postes dont la commande est confirmée sont pilotés ; quand la commande est en proportion (Icom, Hamlib, TCI),
    //     la puissance maxi retenue est celle de la version la plus puissante du poste : une erreur donne MOINS de watts, jamais plus.
    //
    // Commandes (relevé du 2026-11-16, manuels CAT des fabricants recoupés avec Hamlib) :
    //   Yaesu / Kenwood / Elecraft   PCnnn;  nnn = watts (le poste ramène lui-même la valeur dans ses limites, par bande et mode)
    //   Icom                         CI-V 14 0A + niveau 0000-0255 (BCD) proportionnel à la puissance maxi de la bande
    //   Hamlib                       L RFPOWER 0.000-1.000 (proportion ; watts maxi lus par \power2mW, sinon ceux du tableau)
    //   TCI                          drive (0-100 %) : % = watts, juste sur un appareil de 100 W, moins sur un plus petit
    //   HRD                          non piloté : l'échelle de son curseur (« RF power percentage », 0-255 sur le FT-2000) n'est
    //                                pas documentée ; à essayer sur un vrai poste avant de l'activer.
    public static class Powers
    {
        // Plafond général en watts (réglage PowerLimit) : aucune mémoire n'envoie plus.
        public static int Limit = 100;
        public const int LimitMax = 400;

        // Réglage proposé dans la fenêtre des mémoires (poste connecté, ou modèle choisi) ; null = inconnu.
        public static PowerRange Current;

        static readonly string[] YaesuPc = { "FT-2000", "FTdx10", "FTdx101D/MP", "FT-991/991A", "FT-891", "FT-710", "FTdx9000",
            "FTdx5000", "FTdx3000", "FTdx1200", "FT-950", "FT-450/450D" };
        static readonly string[] KenwoodPc = { "TS-590S/SG", "TS-890S", "TS-990S", "TS-2000", "TS-B2000", "TS-480", "TS-570" };

        // Puissance réglable du modèle (liaison par port COM, ou poste reconnu derrière OmniRig / Hamlib) ; null : pas de commande connue.
        public static PowerRange ForModel(RigModel m)
        {
            if (m == null) return null;
            string n = m.Name;
            switch (m.Protocol)
            {
                case RigProtocol.Yaesu:
                    if (Array.IndexOf(YaesuPc, n) < 0) return null;   // FTX-1 : format à deux têtes non confirmé
                    return new PowerRange(n, PowerKind.Pc, n == "FTdx5000" ? 10 : 5,
                        n == "FTdx9000" ? 400 : n == "FT-2000" || n == "FTdx101D/MP" || n == "FTdx5000" ? 200 : 100);
                case RigProtocol.Kenwood:
                    if (Array.IndexOf(KenwoodPc, n) < 0) return null;
                    return new PowerRange(n, PowerKind.Pc, 5, n == "TS-990S" || n == "TS-480" ? 200 : 100);
                case RigProtocol.Elecraft:
                    // K4 (PCnnnr; avec gamme) et K2 (option KPA100) : format non confirmé, pas piloté.
                    if (n == "K3/K3S") return new PowerRange(n, PowerKind.Pc, 1, 110);
                    if (n == "KX3") return new PowerRange(n, PowerKind.Pc, 1, 15);
                    if (n == "KX2") return new PowerRange(n, PowerKind.Pc, 1, 12);
                    return null;
                case RigProtocol.Icom:
                    if (m.Brand != "Icom" || IcomMax(n, 14000000) <= 0) return null;   // Xiegu : non confirmé
                    return new PowerRange(n, PowerKind.Icom, 1, IcomMax(n, 14000000));
            }
            return null;   // anciens Yaesu à protocole binaire : pas de commande de puissance
        }

        // Puissance maxi (W) d'un Icom sur la bande de `hz` (version la plus puissante du modèle) ; 0 = poste non piloté.
        public static int IcomMax(string n, long hz)
        {
            bool v = hz > 60000000, u = hz > 300000000, g = hz > 1000000000;
            switch (n)
            {
                case "IC-7700": case "IC-7800": case "IC-7850": case "IC-7851": case "IC-7760": return 200;
                case "IC-7300": case "IC-7300MK2": case "IC-7610": case "IC-7410": case "IC-7600": case "IC-7200": case "IC-718":
                case "IC-746": case "IC-746PRO": case "IC-756PRO": case "IC-756PROII": case "IC-756PROIII": case "IC-7400": return 100;
                case "IC-7000": case "IC-7100": return u ? 35 : v ? 50 : 100;
                case "IC-706MKIIG": return u ? 20 : v ? 50 : 100;
                case "IC-9100": case "IC-9700": case "IC-910H": return g ? 10 : u ? 75 : 100;
                case "IC-705": case "IC-703": case "IC-905": return 10;
            }
            return 0;
        }

        // Watts réellement demandés : plafond général, puis limites du poste.
        public static int Clamp(int watts, PowerRange r)
        {
            int w = Math.Min(watts, Limit);
            if (r != null) w = Math.Max(r.MinW, Math.Min(r.MaxW, w));
            return Math.Max(0, w);
        }

        // Niveau CI-V 0-255 pour `watts` sur la bande de `hz`.
        public static int IcomLevel(string rig, int watts, long hz)
        {
            int max = IcomMax(rig, hz);
            if (max <= 0) max = 200;
            return Math.Max(0, Math.Min(255, (int)Math.Round(watts * 255.0 / max)));
        }

        // « PC050; »
        public static string PcCommand(int watts) { return "PC" + Math.Max(0, Math.Min(999, watts)).ToString("000") + ";"; }

        public static string Text(int watts) { return watts < 0 ? "—" : watts + " W"; }
    }

    public enum PowerKind { Pc, Icom, Hamlib, Tci }

    // Puissance réglable d'un poste : bornes en watts et manière de l'envoyer.
    public class PowerRange
    {
        public readonly string Rig;
        public readonly PowerKind Kind;
        public readonly int MinW, MaxW;

        public PowerRange(string rig, PowerKind kind, int minW, int maxW)
        {
            Rig = rig;
            Kind = kind;
            MinW = minW;
            MaxW = maxW;
        }

        public string Describe()
        {
            return (Rig != null ? Rig + " : " : "") + MinW + "-" + MaxW + " W" +
                (Kind == PowerKind.Tci ? " (TCI drive, % = W)" : Kind == PowerKind.Icom ? " (CI-V 14 0A)" : Kind == PowerKind.Hamlib ? " (Hamlib RFPOWER)" : " (PCnnn;)");
        }
    }
}
