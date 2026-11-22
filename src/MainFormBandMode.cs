using System;

namespace SDRLink
{
    // Mode par bande radioamateur (repris de PhoneSDRLink 1.31) : en passant dans une autre bande (ou à moins de 0,5 MHz d'une bande) (160 m à 6 m, et la bande
    // étendue des 27 MHz), le mode retenu par l'opérateur pour cette bande est repris, sinon le mode usuel (LSB en 160, 80 et
    // 40 m ; USB en 60 m et à partir de 30 m ; AM sur 27 MHz). Le mode que l'opérateur choisit dans une bande (boutons de mode,
    // page du WebSDR, bouton du poste) est retenu pour cette bande. Rien ne change dans une même bande, hors des bandes, au
    // rappel d'une mémoire (elle a son mode), pendant un scan qui a son propre mode, pendant un décodeur numérique, ni quand le
    // mode est un mode numérique (DATA, par ex. WSJT-X qui change de bande).
    public partial class MainForm
    {
        DateTime bandModeQuietUntil;   // rappel de mémoire ou lien de partage : leur mode est gardé

        // Mode en cours : celui du poste s'il est synchronisé, sinon celui du WebSDR (null si inconnu).
        string ModeNow()
        {
            if (worker != null && rigOnline && rigM != null && !SdrAlone) return rigM;
            string fam = Modes.SdrFamily(sdrM);
            return fam != "" ? Modes.FromSdrFamily(fam) : null;
        }

        int bandAutoLast = -1;          // dernière bande dont le mode a été mis (molette rapide : une seule fois)
        DateTime bandAutoAt;

        // À appeler AVANT de régler la nouvelle fréquence (côté poste) : met le mode de la nouvelle bande si l'on change de bande.
        // Vrai si le mode a été changé.
        bool BandAutoMode(long newHz, long oldHz, string why)
        {
            if (newHz <= 0 || oldHz <= 0 || DateTime.UtcNow < bandModeQuietUntil) return false;
            if (scanning && settings.ScanType == 0 && scanRun != null && scanRun.Mode != "") return false;   // le scan a son propre mode
            if (DigiRunning) return false;
            int nb = Bands.Near(newHz);
            if (nb < 0 || nb == Bands.Near(oldHz)) return false;
            if (nb == bandAutoLast && DateTime.UtcNow < bandAutoAt.AddMilliseconds(1500)) return false;   // relectures en retard
            string cur = ModeNow();
            if (cur == null || cur == Modes.DATAU || cur == Modes.DATAL || cur == Modes.RTTY || cur == Modes.RTTYR) return false;
            string m = Bands.ModeOf(nb);
            if (ModeKey(m) == ModeKey(cur) && Modes.ToSdr(m) == Modes.SdrFamily(sdrM)) return false;   // poste et WebSDR déjà dans ce mode
            bandAutoLast = nb;
            bandAutoAt = DateTime.UtcNow;
            Log.Write("Mode par bande : " + Bands.Label(nb) + " -> " + m + " (" + why + ")");
            ApplyMode(m);
            bandSdrMode = Modes.ToSdr(m);
            return true;
        }

        // Mode de la bande renvoyé au WebSDR avec la fréquence qui suit (une page qui change de bande ou de profil, ou qui
        // garde l'ancien mode en attendant sa relecture, n'annule pas le mode de la bande).
        string bandSdrMode = "";

        string TakeBandSdrMode()
        {
            string s = bandSdrMode;
            bandSdrMode = "";
            return s;
        }

        // Choix de mode de l'opérateur : retenu pour la bande où l'on se trouve.
        void NoteBandMode(string mode)
        {
            if (mode == Modes.CWR) mode = Modes.CW;
            if (mode == Modes.DATAL) mode = Modes.DATAU;
            if (Array.IndexOf(Settings.BandModeList, mode) < 0) return;
            int b = Bands.Near(CurrentFreq());
            if (b < 0 || Bands.ModeOf(b) == mode) return;
            Bands.Remembered[Bands.Name(b)] = mode;
            Log.Write("Mode par bande : " + mode + " retenu pour " + Bands.Label(b));
            settings.Save();
        }
    }
}
