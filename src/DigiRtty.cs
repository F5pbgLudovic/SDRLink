using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SDRLink
{
    // Décodeur RTTY : recherche de la paire de tonalités (décalage 170, 200, 425, 450 ou 850 Hz) ou paire imposée, deux filtres de
    // Goertzel fenêtrés, puis des décodeurs asynchrones en parallèle (débits 45,45 / 50 / 75 bauds ou un débit imposé, polarité normale /
    // inversée ou imposée) ; on affiche celui dont les trames (départ, 5 bits, arrêt) sont le plus souvent valides. Alphabet Baudot
    // (lettres / chiffres). Quand la paire de tonalités est trouvée, la dernière seconde de son est rejouée pour ne pas perdre le début.
    // (Portage de RttyEngine.java de PhoneSDRLink.)
    public class RttyEngine : DigiEngine
    {
        const int RATE = 8000;
        public const int HOP = 20, WIN = 120, BUF = 8192;
        public static readonly double[] BAUDS = { 45.45, 50.0, 75.0 };
        public static readonly double[] SHIFTS = { 170, 200, 425, 450, 850 };
        readonly float[] ring = new float[BUF];
        readonly double[] hann = new double[WIN];
        long total;
        double fLo, fHi;                // paire de tonalités (0 = pas trouvée)
        int tick, miss, bestTick, statTick;
        bool replay;
        Uart[] u;
        int best = -1;

        // Réglages ; 0 = automatique (les double ne peuvent pas être volatile en C#). Après un changement de débit, décalage, polarité,
        // fréquence ou AFC : Post(SettingsChanged).
        public double BaudSetting = 0;
        public volatile int ShiftSetting = 0;
        public volatile int PolSetting = 0;        // 0 les deux, 1 normale (mark = tonalité basse), 2 inversée
        public volatile bool Uos = true;           // « unshift on space » : une espace ramène en lettres
        public volatile int Squelch = 15;          // les deux tonalités doivent dépasser ainsi le bruit
        public volatile int FreqSetting = 0;       // tonalité basse imposée (Hz)
        public volatile bool Afc = true;           // la paire de tonalités suit la dérive

        public const string LTRS = "\u0000E\nA SIU\rDRJNFCKTZLWHYPQOBG\u0001MXV\u0002";
        public const string FIGS = "\u00003\n- \u000787\r$4',!:(5\")2#6019?&\u0001./;\u0002";

        sealed class Uart
        {
            readonly RttyEngine owner;
            internal readonly double baud, bitHops;
            internal readonly bool reversed;
            int state;                  // 0 attente du repos (mark), 1 repos, 2 trame en cours
            double t, t0;
            int k, code;
            bool figs;
            internal double score, qual, q;      // trames valides (pondérées), confiance moyenne aux instants d'échantillonnage
            internal readonly StringBuilder outb = new StringBuilder();
            internal Uart(RttyEngine o, double b, bool rev) { owner = o; baud = b; reversed = rev; bitHops = RATE / b / HOP; }

            // v > 0 : tonalité basse dominante.
            internal void Hop(double v)
            {
                bool mark = reversed ? v < 0 : v > 0;
                bool strong = Math.Abs(v) > 0.2;
                t += 1;
                if (state == 0) { if (mark && strong) state = 1; return; }
                if (state == 1) { if (!mark && strong) { state = 2; t0 = t; k = 0; code = 0; } return; }
                double tk = t0 + (k + 0.5) * bitHops;
                if (t < tk) return;
                q += Math.Abs(v);
                if (k == 0)
                {                       // bit de départ : doit être « space »
                    if (mark) { state = 1; score -= 0.5; return; }
                }
                else if (k <= 5)
                {
                    if (mark) code |= 1 << (k - 1);
                }
                else
                {                            // bit d'arrêt : doit être « mark »
                    state = 0;
                    qual = 0.9 * qual + 0.1 * (q / 7); q = 0;
                    if (mark) { score = score * 0.98 + 1; Character(code); } else score = score * 0.98 - 1;
                    return;
                }
                k++;
            }

            void Character(int c)
            {
                if (c == 31) { figs = false; return; }
                if (c == 27) { figs = true; return; }
                if (c == 4 && owner.Uos) figs = false;   // « unshift on space » (comme fldigi par défaut)
                char ch = (figs ? FIGS : LTRS)[c];
                if (ch == '\r' || ch == 0 || (ch < 8 && ch != '\n')) return;
                outb.Append(ch);
            }
        }

        public RttyEngine()
        {
            for (int i = 0; i < WIN; i++) hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (i + 0.5) / WIN);
            ResetUarts();
        }

        public override int Rate { get { return RATE; } }

        static double G(float[] x, int off, int n, double f) { return Goertzel(x, off, n, f, RATE); }

        void ResetUarts()
        {
            double bs = BaudSetting;
            int pol = PolSetting;
            double[] bauds = bs > 0 ? new double[] { bs } : BAUDS;
            List<Uart> l = new List<Uart>();
            foreach (double b in bauds)
            {
                if (pol != 2) l.Add(new Uart(this, b, false));
                if (pol != 1) l.Add(new Uart(this, b, true));
            }
            u = l.ToArray();
            best = -1;
        }

        // Appelé (dans le fil du décodeur) quand un réglage change : les décodeurs sont refaits, la paire de tonalités est cherchée de nouveau.
        public void SettingsChanged()
        {
            fLo = fHi = 0;
            miss = 0;
            ResetUarts();
        }

        protected override void FeedShort(short[] s, int n)
        {
            for (int i = 0; i < n; i++)
            {
                ring[(int)(total % BUF)] = s[i] / 32768f;
                total++;
                if (total % HOP == 0) Step();
            }
        }

        readonly float[] wbuf = new float[WIN];   // fenêtre réutilisée à chaque pas

        float[] WindowInto(long end)
        {
            long start = end - WIN;
            for (int i = 0; i < WIN; i++) wbuf[i] = (float)(ring[(int)(((start + i) % BUF + BUF) % BUF)] * hann[i]);
            return wbuf;
        }

        float[] WindowAt(long end, int len)
        {
            float[] w = new float[len];
            long start = end - len;
            for (int i = 0; i < len; i++) w[i] = ring[(int)(((start + i) % BUF + BUF) % BUF)];
            return w;
        }

        static int JRound(double x) { return (int)Math.Floor(x + 0.5); }   // Math.round de Java

        // Toutes les 0,25 s : recherche des deux tonalités sur la dernière demi-seconde.
        void FindTones()
        {
            if (total < BUF) return;
            int freqSetting = FreqSetting, shiftSetting = ShiftSetting;
            if (fLo != 0 && !Afc && freqSetting == 0) return;   // sans AFC la paire trouvée reste en place
            float[] w = WindowAt(total, 4096);
            int n = 97;
            double[] e = new double[n], sorted = new double[n];
            for (int i = 0; i < n; i++) { e[i] = G(w, 0, 4096, 500 + 25.0 * i); sorted[i] = e[i]; }
            Array.Sort(sorted);
            double med = sorted[n / 2] + 1e-12;
            if (freqSetting > 0)
            {   // tonalité basse imposée : on cherche seulement la seconde
                double lo = freqSetting, hi = lo + (shiftSetting > 0 ? shiftSetting : 170), bestE0 = 0;
                if (shiftSetting == 0) foreach (double s in SHIFTS) { double x = G(w, 0, 4096, lo + s); if (x > bestE0) { bestE0 = x; hi = lo + s; } }
                if (fLo != lo || fHi != hi) { fLo = lo; fHi = hi; replay = true; ResetUarts(); }
                miss = 0;
                SetStatus();
                return;
            }
            double bestE = 0;
            int bi = -1, bs = -1;
            for (int i = 0; i < n; i++) for (int si = 0; si < SHIFTS.Length; si++)
                {
                    if (shiftSetting > 0 && Math.Abs(SHIFTS[si] - shiftSetting) > 1e-6) continue;
                    int j = i + JRound(SHIFTS[si] / 25.0);
                    if (j >= n) continue;
                    double m = Math.Min(e[i], e[j]);
                    if (m > bestE) { bestE = m; bi = i; bs = si; }
                }
            if (shiftSetting > 0 && bi < 0)
            {   // décalage imposé qui n'est pas dans la liste : on le traite comme une valeur libre
                for (int i = 0; i < n; i++)
                {
                    int j = i + JRound(shiftSetting / 25.0);
                    if (j >= n) continue;
                    double m = Math.Min(e[i], e[j]);
                    if (m > bestE) { bestE = m; bi = i; bs = -1; }
                }
            }
            if (bi >= 0 && bestE > Squelch * med)
            {
                double sh = bs >= 0 ? SHIFTS[bs] : shiftSetting;
                double a = Refine(w, 500 + 25.0 * bi), b = Refine(w, 500 + 25.0 * bi + sh);
                if (fLo == 0 || Math.Abs(a - fLo) > 40 || Math.Abs(b - fHi) > 40) { fLo = a; fHi = b; replay = true; ResetUarts(); }
                else { fLo = 0.8 * fLo + 0.2 * a; fHi = 0.8 * fHi + 0.2 * b; }
                miss = 0;
                SetStatus();
            }
            else if (++miss > 80) { fLo = fHi = 0; }
        }

        void SetStatus()
        {
            if (fLo == 0) return;
            if (best >= 0) RaiseStatus(string.Format(CultureInfo.InvariantCulture, "RTTY : {0:F0} / {1:F0} Hz ({2:F0} Hz), {3:F2} bauds{4}", fLo, fHi, fHi - fLo, u[best].baud, u[best].reversed ? ", " + Lang.T("inversé", "reversed") : ""));
            else RaiseStatus(string.Format(CultureInfo.InvariantCulture, "RTTY : {0:F0} / {1:F0} Hz ({2:F0} Hz)", fLo, fHi, fHi - fLo));
        }

        double Refine(float[] w, double f)
        {
            double bb = 0, bf = f;
            for (double x = f - 12; x <= f + 12; x += 4) { double e = G(w, 0, 4096, x); if (e > bb) { bb = e; bf = x; } }
            return bf;
        }

        void Step()
        {
            if (++tick >= 32) { tick = 0; FindTones(); }
            if (++bestTick >= 50) { bestTick = 0; SelectBest(); if (++statTick >= 8) { statTick = 0; SetStatus(); } }
            if (fLo == 0) return;
            if (replay)
            {   // paire nouvellement trouvée : on traite de nouveau la dernière seconde
                replay = false;
                for (long e = total - 7000; e < total; e += HOP) Process(e);
                SelectBest();
            }
            Process(total);
        }

        void Process(long end)
        {
            float[] w = WindowInto(end);
            double a = G(w, 0, WIN, fLo), b = G(w, 0, WIN, fHi);
            double v = (a - b) / (a + b + 1e-12);
            foreach (Uart x in u) x.Hop(v);
            FlushBest();
        }

        void SelectBest()
        {
            int bi = -1;
            double bs = 0;     // au moins six trames valides ; à score voisin, la confiance départage (le bon débit échantillonne au milieu des bits)
            for (int i = 0; i < u.Length; i++) if (u[i].score > 6.0) { double s = u[i].score + 8 * u[i].qual; if (s > bs) { bs = s; bi = i; } }
            if (bi != best && (best < 0 || bi < 0 || u[bi].score + 8 * u[bi].qual > u[best].score + 8 * u[best].qual + 2)) best = bi;
        }

        // Les caractères du décodeur retenu sont émis ; ceux des autres sont jetés. Avant d'en retenir un, on garde les caractères en attente.
        void FlushBest()
        {
            if (best < 0) return;
            StringBuilder o = u[best].outb;
            if (o.Length > 0) { RaiseText(o.ToString()); o.Length = 0; }
            for (int i = 0; i < u.Length; i++) if (i != best && u[i].outb.Length > 200) u[i].outb.Length = 0;
        }
    }
}
