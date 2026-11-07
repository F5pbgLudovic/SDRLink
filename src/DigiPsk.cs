using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SDRLink
{
    // Décodeur PSK31 / PSK63 (BPSK) : recherche d'un signal dans le spectre (FFT 4096 points, toutes les 0,5 s) ou fréquence imposée,
    // mélange à la fréquence suivie, filtre passe-bas à 128 coefficients, décimation à 500 Hz, rythme symbole trouvé sur l'énergie des
    // 16 (ou 8) phases possibles, démodulation différentielle (pas de saut de phase = 1, saut de 180° = 0), Varicode (« 00 » sépare les
    // lettres). Le suivi de fréquence (AFC) lit la dérive de phase d'un symbole à l'autre. Le squelch lit la cohérence de phase
    // (cos² de l'angle entre deux symboles : 1 pour un vrai signal, 0,5 pour du bruit).
    // (Portage de PskEngine.java de PhoneSDRLink.)
    public class PskEngine : DigiEngine
    {
        const int RATE = 8000;
        public const int DEC = 16, TAPS = 128, FFT = 4096;

        public static readonly string[] VARI = {
            "1010101011", "1011011011", "1011101101", "1101110111", "1011101011", "1101011111", "1011101111", "1011111101",
            "1011111111", "11101111", "11101", "1101101111", "1011011101", "11111", "1101110101", "1110101011",
            "1011110111", "1011110101", "1110101101", "1110101111", "1101011011", "1101101011", "1101101101", "1101010111",
            "1101111011", "1101111101", "1110110111", "1101010101", "1101011101", "1110111011", "1011111011", "1101111111",
            "1", "111111111", "101011111", "111110101", "111011011", "1011010101", "1010111011", "101111111",
            "11111011", "11110111", "101101111", "111011111", "1110101", "110101", "1010111", "110101111",
            "10110111", "10111101", "11101101", "11111111", "101110111", "101011011", "101101011", "110101101",
            "110101011", "110110111", "11110101", "110111101", "111101101", "1010101", "111010111", "1010101111",
            "1010111101", "1111101", "11101011", "10101101", "10110101", "1110111", "11011011", "11111101",
            "101010101", "1111111", "111111101", "101111101", "11010111", "10111011", "11011101", "10101011",
            "11010101", "111011101", "10101111", "1101111", "1101101", "101010111", "110110101", "101011101",
            "101110101", "101111011", "1010101101", "111110111", "111101111", "111111011", "1010111111", "101101101",
            "1011011111", "1011", "1011111", "101111", "101101", "11", "111101", "1011011",
            "101011", "1101", "111101011", "10111111", "11011", "111011", "1111", "111",
            "111111", "110111111", "10101", "10111", "101", "110111", "1111011", "1101011",
            "11011111", "1011101", "111010101", "1010110111", "110111011", "1010110101", "1011010111", "1110110101" };
        static readonly Dictionary<string, char> TABLE = new Dictionary<string, char>();
        static PskEngine() { for (int i = 0; i < VARI.Length; i++) TABLE[VARI[i]] = (char)i; }

        // Réglages ; après un changement : Post(SettingsChanged).
        public volatile bool AutoFreq = true, Afc = true;
        public volatile int ManualFreq = 1000;
        public volatile int Baud = 31;          // 31 (31,25 bauds) ou 63 (62,5 bauds)
        public volatile int Squelch = 75;       // seuil de cohérence de phase, en %

        readonly float[] ring = new float[FFT];
        readonly double[] hann = new double[FFT], fr = new double[FFT], fi = new double[FFT], pw = new double[FFT / 2];
        readonly double[] taps = new double[TAPS], mr = new double[TAPS], mi = new double[TAPS];
        int tapsBaud, rp, sinceSearch, mp, dc, appliedManual;
        long total;

        double freq, ph, lockF;          // fréquence audio suivie (0 = pas de signal), phase de l'oscillateur, fréquence d'accrochage
        long lockAt, closedSince;
        bool everOpen, open, started;
        readonly double[] rejF = new double[8];
        readonly long[] rejUntil = new long[8];

        // démodulateur de symboles
        readonly double[] e = new double[16];
        int k, best;
        double p1r, p1i, p2r, p2i, zr0, zi0;
        bool haveZ;
        double qm;                       // cohérence de phase lissée
        readonly StringBuilder bits = new StringBuilder();
        bool lastCr;
        readonly StringBuilder outb = new StringBuilder();
        string lastStatus = "";

        public PskEngine()
        {
            for (int i = 0; i < FFT; i++) hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FFT);
            // (l'état « recherche d'un signal » est envoyé au premier morceau de son : personne n'écoute encore dans le constructeur)
        }

        public override int Rate { get { return RATE; } }

        void SetStatus(string s)
        {
            string t = "PSK" + (Baud >= 47 ? "63" : "31") + " : " + s;
            if (t != lastStatus) { lastStatus = t; RaiseStatus(t); }
        }

        static string Searching() { return Lang.T("recherche d'un signal…", "searching for a signal…"); }
        static string Listening(double f) { return Lang.F("à l'écoute à {0:F0} Hz", "listening at {0:F0} Hz", f); }

        // Appelé dans le fil du décodeur quand un réglage change.
        public void SettingsChanged()
        {
            if (!AutoFreq) { appliedManual = ManualFreq; LockOn(appliedManual, false); }
            else if (freq == 0) SetStatus(Searching());
            if (tapsBaud != Baud) { ResetDemod(); }
        }

        void ResetDemod()
        {
            haveZ = false; bits.Length = 0; qm = 0.5; open = false; k = 0;
            for (int i = 0; i < 16; i++) e[i] = 0;
        }

        void LockOn(double f, bool probation)
        {
            freq = f; lockF = f;
            lockAt = total;
            everOpen = !probation;
            closedSince = total;
            ResetDemod();
            SetStatus(Listening(f));
        }

        void Unlock()
        {
            freq = 0;
            open = false;
            ResetDemod();
            SetStatus(Searching());
        }

        void BuildTaps()
        {
            tapsBaud = Baud;
            double fc = (tapsBaud >= 47 ? 170.0 : 85.0) / 8000.0, sum = 0;
            for (int i = 0; i < TAPS; i++)
            {
                double x = i - (TAPS - 1) / 2.0;
                double s = x == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
                taps[i] = s * (0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (TAPS - 1)));
                sum += taps[i];
            }
            for (int i = 0; i < TAPS; i++) taps[i] /= sum;
            ResetDemod();
        }

        protected override void FeedShort(short[] s, int n)
        {
            if (!started) { started = true; SetStatus(Searching()); }
            if (tapsBaud != Baud) BuildTaps();
            if (!AutoFreq && (freq == 0 || ManualFreq != appliedManual)) { appliedManual = ManualFreq; LockOn(appliedManual, false); }
            for (int i = 0; i < n; i++)
            {
                float x = s[i] / 32768f;
                ring[rp] = x;
                rp = (rp + 1) & (FFT - 1);
                total++;
                if (++sinceSearch >= FFT)
                {
                    sinceSearch = 0;
                    if (AutoFreq && freq == 0) { double f = Search(300, 2900, 12, true); if (f > 0) LockOn(f, true); }
                    else if (!AutoFreq && Afc && !open) { int mf = ManualFreq; double f = Search(mf - 30, mf + 30, 8, false); if (f > 0) { freq = f; lockF = f; } }
                }
                if (freq == 0) continue;
                ph += 2 * Math.PI * freq / RATE;
                if (ph > 2 * Math.PI) ph -= 2 * Math.PI;
                mr[mp] = x * Math.Cos(ph);
                mi[mp] = -x * Math.Sin(ph);
                mp = (mp + 1) & (TAPS - 1);
                if (++dc >= DEC)
                {
                    dc = 0;
                    double yr = 0, yi = 0;
                    for (int j = 0; j < TAPS; j++)
                    {
                        int q = (mp - 1 - j) & (TAPS - 1);
                        yr += taps[j] * mr[q];
                        yi += taps[j] * mi[q];
                    }
                    Step(yr, yi);
                }
            }
            if (outb.Length > 0) { string t = outb.ToString(); outb.Length = 0; RaiseText(t); }
        }

        // Cherche le signal le plus fort entre fa et fb (Hz) ; renvoie sa fréquence, ou 0. Le bruit de référence est la médiane sur toute la bande.
        double Search(double fa, double fb, double ratio, bool useReject)
        {
            for (int i = 0; i < FFT; i++) { fr[i] = ring[(rp + i) & (FFT - 1)] * hann[i]; fi[i] = 0; }
            Fft(fr, fi);
            for (int i = 0; i < FFT / 2; i++) pw[i] = fr[i] * fr[i] + fi[i] * fi[i];
            double bin = (double)RATE / FFT;
            int lo = (int)(300 / bin), hi = (int)(2900 / bin), W = Baud >= 47 ? 20 : 10;
            int m = hi - lo + 1;
            double[] sm = new double[m];
            double run = 0;
            for (int j = lo - W; j <= lo + W; j++) run += pw[j];
            for (int i = 0; i < m; i++)
            {
                sm[i] = run;
                int a = lo + i - W, b = lo + i + W + 1;
                run += pw[b] - pw[a];
            }
            double[] cp = (double[])sm.Clone();
            Array.Sort(cp);
            double med = cp[m / 2];
            int bk = -1;
            double bv = 0;
            for (int i = 0; i < m; i++)
            {
                double f = (lo + i) * bin;
                bool rej = f < fa || f > fb;
                if (useReject) for (int r = 0; r < rejF.Length; r++) if (rejUntil[r] > total && Math.Abs(rejF[r] - f) < 45) rej = true;
                if (!rej && sm[i] > bv) { bv = sm[i]; bk = i; }
            }
            if (bk < 0 || !(bv > ratio * med)) return 0;
            double sw = 0, sk = 0;
            for (int j = lo + bk - W; j <= lo + bk + W; j++) { sw += pw[j]; sk += pw[j] * j; }
            return sk / sw * bin;
        }

        static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double a = -2 * Math.PI / len, wr = Math.Cos(a), wi = Math.Sin(a);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int j = 0; j < len / 2; j++)
                    {
                        int u = i + j, v = i + j + len / 2;
                        double xr = re[v] * cr - im[v] * ci, xi = re[v] * ci + im[v] * cr;
                        re[v] = re[u] - xr; im[v] = im[u] - xi;
                        re[u] += xr; im[u] += xi;
                        double t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
                    }
                }
            }
        }

        // Un échantillon à 500 Hz.
        void Step(double br, double bi)
        {
            int baud = Baud;
            int sps = baud >= 47 ? 8 : 16;
            int idx = k % sps;
            e[idx] = 0.97 * e[idx] + 0.03 * (br * br + bi * bi);
            if (idx == 0)
            {
                int nb = 0;
                for (int i = 1; i < sps; i++) if (e[i] > e[nb]) nb = i;
                if (e[nb] > 1.15 * e[best % sps]) best = nb;
                best %= sps;
            }
            bool decide = idx == (best + 1) % sps;
            double zr = 0, zi = 0;
            if (decide) { zr = p2r + 2 * p1r + br; zi = p2i + 2 * p1i + bi; }
            p2r = p1r; p2i = p1i; p1r = br; p1i = bi;
            k++;
            if (!decide) return;
            if (haveZ)
            {
                double dr = zr * zr0 + zi * zi0, di = zi * zr0 - zr * zi0;   // z · conj(z précédent)
                double m = dr * dr + di * di;
                if (m > 1e-18)
                {
                    double c2 = dr * dr / m;
                    qm = 0.9 * qm + 0.1 * c2;
                    double thr = Squelch / 100.0;
                    if (!open && qm > thr) { open = true; everOpen = true; bits.Length = 0; SetStatus(Lang.F("signal à {0:F0} Hz", "signal at {0:F0} Hz", freq)); }
                    else if (open && qm < thr - 0.12) { open = false; closedSince = total; bits.Length = 0; SetStatus(Listening(freq)); }
                    if (Afc)
                    {
                        double err = 0.5 * Math.Atan2(2 * dr * di, dr * dr - di * di);   // dérive de phase par symbole, données ôtées
                        double df = err / (2 * Math.PI) * (baud >= 47 ? 62.5 : 31.25);
                        freq = Math.Max(lockF - 30, Math.Min(lockF + 30, freq + 0.05 * Math.Max(-10, Math.Min(10, df))));
                    }
                    if (open)
                    {
                        Bit(dr > 0 ? 1 : 0);
                    }
                }
            }
            zr0 = zr; zi0 = zi; haveZ = true;
            if (!open)
            {
                long idle = total - closedSince;
                if (AutoFreq)
                {
                    if (!everOpen && total - lockAt > 6L * RATE)
                    {   // fausse alerte (CW, RTTY, porteuse…) : on l'écarte un moment
                        for (int r = 0; r < rejF.Length; r++) if (rejUntil[r] <= total) { rejF[r] = freq; rejUntil[r] = total + 90L * RATE; break; }
                        Unlock();
                    }
                    else if (everOpen && idle > 6L * RATE) Unlock();
                }
            }
        }

        void Bit(int b)
        {
            bits.Append((char)('0' + b));
            int n = bits.Length;
            if (n >= 2 && bits[n - 1] == '0' && bits[n - 2] == '0')
            {
                if (n > 2)
                {
                    char c;
                    if (TABLE.TryGetValue(bits.ToString(0, n - 2), out c)) Emit(c);
                }
                bits.Length = 0;
            }
            else if (n > 12) bits.Length = 0;
        }

        void Emit(char c)
        {
            if (c == 13) { outb.Append('\n'); lastCr = true; return; }
            bool after = lastCr;
            lastCr = false;
            if (c == 10) { if (!after) outb.Append('\n'); return; }
            if (c == 9) { outb.Append(' '); return; }
            if (c >= 32 && c < 127) outb.Append(c);
        }
    }
}
