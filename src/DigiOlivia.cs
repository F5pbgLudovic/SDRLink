using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SDRLink
{
    // Décodeur Olivia et Contestia (MFSK à tons multiples et correction d'erreurs) : le calcul est fait par la bibliothèque MFSK de Pawel Jalocha
    // (code natif, la même que celle de fldigi, dans sdrlink_digi.dll). Ici : réglages, recherche automatique du centre du signal dans le
    // spectre (SpecFind), texte et état (synchro, rapport signal/bruit, décalage de fréquence). Réception seule.
    // (Portage de OliviaEngine.java de PhoneSDRLink.)
    public class OliviaEngine : DigiEngine
    {
        public const int RATE = 8000;
        public override int Rate { get { return RATE; } }

        public readonly bool Contestia;

        // Réglages : à changer puis appeler SettingsChanged() par Post.
        public volatile int Tones = 32, Bw = 1000;      // nombre de tons, bande en Hz
        public volatile int Margin = 8, Integ = 4;      // marge de recherche de fréquence (en tons) et durée d'intégration de la synchro
        public volatile int Thr10 = 30;                 // seuil de synchro (rapport signal/bruit) en dixièmes
        public volatile int CenterSetting = 0;          // 0 = centre trouvé automatiquement, sinon centre imposé (Hz)
        public volatile bool Reverse;                   // signal inversé (bande latérale opposée)

        // Fil du décodeur
        IntPtr h;
        int centre = 1500, cand, candHits;
        readonly SpecFind sf = new SpecFind();
        readonly byte[] outBuf = new byte[1024];
        readonly double[] st = new double[2];
        long since, statAt;
        bool locked, started, loadFailed;

        // Synchro et silencieux. Sur du bruit seul, le rapport de synchro du décodeur natif reste vers 3,0 à 3,3 quel que soit le format
        // (mesuré sur 60 s, 16 formats), juste au niveau du seuil par défaut (3,0) : le décodeur se dit « en synchro » et sort des
        // caractères au hasard ; un vrai signal donne 4 à 12. La valeur d'un bloc retombe souvent à 0 même sur un bon signal : on garde
        // donc l'instant de la dernière bonne valeur (en échantillons). Synchro : une valeur ≥ LockOn l'établit, elle tient tant qu'une
        // valeur ≥ LockKeep revient en moins de LockHold. Silencieux : les caractères ne s'affichent que si une valeur ≥ SqLevel est vue
        // à moins de SqHold ; sinon ils attendent (jusqu'à SqHold) et sortent si le silencieux s'ouvre, ou sont jetés.
        const double LockOnFloor = 3.8, LockKeepFloor = 3.5, SqFloor = 3.5;
        const long LockHold = RATE * 4, SqHold = RATE * 6;
        long sampleNo, strongAt = long.MinValue / 2, keepAt = long.MinValue / 2, sqAt = long.MinValue / 2;
        double shownSnr, shownOff;   // dernière bonne valeur de synchro (affichée)
        readonly StringBuilder pending = new StringBuilder();
        readonly List<long> pendingAt = new List<long>();
        string lastStatus = "";
        int curTones, curBw;

        public OliviaEngine(bool contestia)
        {
            Contestia = contestia;
        }

        /** Centre actuel du décodeur (Hz audio). */
        public int Centre { get { return centre; } }
        /** Vrai quand le décodeur est synchronisé. */
        public bool Locked { get { return locked; } }
        /** Dernier rapport signal/bruit de la synchro et décalage (Hz) donnés par le décodeur natif (fil du décodeur ; essais). */
        public double SyncSnr { get { return st[0]; } }
        public double SyncOffset { get { return st[1]; } }

        string ModeName() { return (Contestia ? "Contestia " : "Olivia ") + curTones + "/" + curBw; }

        void SetStatus(string s)
        {
            if (s != lastStatus) { lastStatus = s; RaiseStatus(s); }
        }

        // Format à la Java (%d, %s, %.Nf, %+.Nf) : les textes traduits de PhoneSDRLink sont repris tels quels.
        static string JFmt(string fmt, params object[] args)
        {
            StringBuilder sb = new StringBuilder();
            int ai = 0;
            for (int i = 0; i < fmt.Length; i++)
            {
                char ch = fmt[i];
                if (ch != '%' || i + 1 >= fmt.Length) { sb.Append(ch); continue; }
                int j = i + 1;
                if (fmt[j] == '%') { sb.Append('%'); i = j; continue; }
                bool plus = false;
                if (fmt[j] == '+') { plus = true; j++; }
                int prec = -1;
                if (j < fmt.Length && fmt[j] == '.')
                {
                    j++;
                    int p0 = j;
                    while (j < fmt.Length && char.IsDigit(fmt[j])) j++;
                    prec = int.Parse(fmt.Substring(p0, j - p0), CultureInfo.InvariantCulture);
                }
                if (j >= fmt.Length) { sb.Append(fmt, i, fmt.Length - i); break; }
                char conv = fmt[j];
                object a = ai < args.Length ? args[ai++] : null;
                string v;
                if (conv == 'f')
                {
                    double d = Convert.ToDouble(a, CultureInfo.InvariantCulture);
                    string pat = prec <= 0 ? "0" : "0." + new string('0', prec);
                    v = d.ToString(plus ? "+" + pat + ";-" + pat : pat, CultureInfo.InvariantCulture);
                }
                else if (conv == 'd')
                {
                    long d = Convert.ToInt64(a, CultureInfo.InvariantCulture);
                    v = (plus && d >= 0 ? "+" : "") + d.ToString(CultureInfo.InvariantCulture);
                }
                else v = Convert.ToString(a, CultureInfo.InvariantCulture);
                sb.Append(v);
                i = j;
            }
            return sb.ToString();
        }

        // Appelé dans le fil du décodeur (Post) quand un réglage change : le décodeur natif est refait.
        public void SettingsChanged()
        {
            if (h != IntPtr.Zero) { DigiNative.OliviaFree(h); h = IntPtr.Zero; }
            ResetLock();
            cand = 0; candHits = 0;
            Build();
        }

        // Charge la DLL au premier besoin (fil du décodeur) ; faux si elle n'a pas pu être chargée (l'état le dit une fois).
        bool EnsureLoaded()
        {
            if (DigiNative.Loaded) return true;
            if (loadFailed) return false;
            string err = DigiNative.Load();
            if (err == null) return true;
            loadFailed = true;
            SetStatus((Contestia ? "Contestia : " : "Olivia : ") + err);
            return false;
        }

        void Build()
        {
            if (!EnsureLoaded()) return;
            if (CenterSetting > 0) centre = CenterSetting;
            curTones = Tones; curBw = Bw;
            h = DigiNative.OliviaNew(Tones, Bw, Margin, Integ, Contestia, centre, Reverse, Thr10);
            if (h == IntPtr.Zero) SetStatus(Lang.T("Olivia : réglages refusés", "Olivia: settings refused"));
            else SetStatus(ModeName() + " : " + JFmt(Lang.T("à l'écoute à %d Hz", "listening at %d Hz"), centre));
        }

        protected override void OnClose()
        {
            if (h != IntPtr.Zero) { DigiNative.OliviaFree(h); h = IntPtr.Zero; }
        }

        /** Vrai tant que le décodeur natif existe (essais : faux après Stop). */
        public bool HasNative { get { return h != IntPtr.Zero; } }

        protected override void FeedShort(short[] s, int n)
        {
            if (!started)
            {
                started = true;
                if (lastStatus.Length == 0) SetStatus(Lang.T("recherche d'un signal…", "searching for a signal…"));
            }
            if (!EnsureLoaded()) return;
            if (h == IntPtr.Zero) { Build(); if (h == IntPtr.Zero) return; }
            if (CenterSetting > 0 && CenterSetting != centre) { centre = CenterSetting; DigiNative.OliviaCenter(h, centre); ResetLock(); }
            sf.Add(s, n);
            int k = DigiNative.OliviaProcess(h, s, n, outBuf, st);
            sampleNo += n;
            UpdateLock();
            if (k > 0)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < k; i++)
                {
                    int c = outBuf[i];
                    if (c == 10 || c == 13) { if (c == 10) sb.Append('\n'); }
                    else if (c >= 32 && c < 127) sb.Append((char)c);
                }
                for (int i = 0; i < sb.Length; i++) { pending.Append(sb[i]); pendingAt.Add(sampleNo); }
            }
            Squelch();
            since += n;
            if (since >= 4000) { since = 0; AutoCentre(); }
            if (statAt++ % 4 == 0) Report();
        }

        // Synchro « vraie » (voir LockOnFloor) : établie par une valeur nettement au-dessus du bruit, tenue malgré les blocs à 0.
        void UpdateLock()
        {
            double thr = Thr10 / 10.0, v = st[0];
            if (v >= Math.Max(thr, LockOnFloor)) strongAt = sampleNo;
            if (v >= Math.Max(thr, LockKeepFloor)) { keepAt = sampleNo; shownSnr = v; shownOff = st[1]; }
            if (v >= Math.Max(thr, SqFloor)) sqAt = sampleNo;
            if (!locked) locked = sampleNo - strongAt <= LockHold;
            else locked = sampleNo - keepAt <= LockHold;
        }

        // Silencieux : caractères affichés si le décodeur a donné une valeur de synchro au-dessus du bruit depuis moins de SqHold ;
        // sinon ils attendent (au plus SqHold) : le décodage d'un caractère précède parfois la bonne valeur de synchro.
        void Squelch()
        {
            if (pending.Length == 0) return;
            if (sampleNo - sqAt <= SqHold)
            {
                RaiseText(pending.ToString());
                pending.Length = 0;
                pendingAt.Clear();
                return;
            }
            int drop = 0;
            while (drop < pendingAt.Count && sampleNo - pendingAt[drop] > SqHold) drop++;
            if (drop > 0) { pending.Remove(0, drop); pendingAt.RemoveRange(0, drop); }
        }

        // Le décodeur natif repart de zéro (nouveau centre, nouveaux réglages) : la synchro aussi.
        void ResetLock()
        {
            locked = false;
            strongAt = keepAt = sqAt = long.MinValue / 2;
            pending.Length = 0;
            pendingAt.Clear();
        }

        static int JRound(double x) { return (int)Math.Floor(x + 0.5); }   // Math.round de Java

        // Sans synchro et en mode automatique : on cherche dans le spectre la zone de la bande du signal ; on ne bouge que si la même zone
        // ressort deux fois de suite (le décodeur natif repart de zéro à chaque changement de centre).
        void AutoCentre()
        {
            if (CenterSetting > 0 || locked) { cand = 0; candHits = 0; return; }
            int bw = Bw, tones = Tones;
            double[] b = sf.Best(bw);
            // Rapport minimal fenêtre / flancs. 1,8 demande environ -1 dB de S/B dans la bande du signal : à -6 dB (dans 2,5 kHz) un
            // signal de 1000 Hz ne donne que 1,4 à 1,7 et n'était jamais trouvé. Bruit seul (mesuré sur 120 s, y compris filtré par une
            // BLU 300-2700 Hz) : au plus 1,25 pour une fenêtre de 1000 Hz, d'où 1,4 pour 1000 Hz. À 2000 Hz la fenêtre remplit la
            // bande passante et ne se localise pas : inchangé (1,8), comme les formats plus étroits.
            double need = bw >= 1000 && bw < 2000 ? 1.4 : 1.8;
            if (b[0] <= 0 || b[1] < need) { cand = 0; candHits = 0; return; }
            int c = JRound(b[0] / 5) * 5;
            if (Math.Abs(c - centre) <= Math.Max(5, bw / (double)tones)) { cand = 0; candHits = 0; return; }   // déjà dans la tolérance du décodeur
            if (cand != 0 && Math.Abs(c - cand) <= Math.Max(10, bw / (double)tones)) candHits++; else { cand = c; candHits = 1; }
            if (candHits >= 2)
            {
                centre = c;
                DigiNative.OliviaCenter(h, centre);
                ResetLock();
                cand = 0; candHits = 0;
            }
        }

        void Report()
        {
            if (locked)
            {
                double db = 10 * Math.Log10(Math.Max(shownSnr, 1e-6));
                SetStatus(ModeName() + " : " + JFmt(Lang.T("synchro, S/B %.0f dB, écart %+.0f Hz, centre %d Hz", "in sync, S/N %.0f dB, offset %+.0f Hz, centre %d Hz"), db, shownOff, centre));
            }
            else
            {
                SetStatus(ModeName() + " : " + JFmt(Lang.T("recherche d'un signal… (centre %d Hz)", "searching for a signal… (centre %d Hz)"), centre));
            }
        }

        /** Débit approximatif en caractères par seconde (64 symboles par bloc FEC, log2(tons) caractères par bloc). */
        public static double CharsPerSecond(int tones, int bw, bool contestia)
        {
            int bits = JRound(Math.Log(tones) / Math.Log(2));
            double baud = bw / (double)tones;
            return bits * baud / (contestia ? 32.0 : 64.0);
        }
    }
}
