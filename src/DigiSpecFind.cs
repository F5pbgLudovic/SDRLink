using System;

namespace SDRLink
{
    // Recherche d'un signal dans le spectre audio (son à 8000 Hz) pour les modes à large bande (Olivia, Contestia, Hell…) :
    // FFT de 2048 points (3,9 Hz par case) toutes les 512 échantillons, puissance lissée sur environ 1,3 s, puis une fenêtre de largeur donnée glisse
    // sur le spectre : on garde l'endroit où la puissance dans la fenêtre dépasse le plus celle des deux flancs (le bruit voisin).
    // (Portage de SpecFind.java de PhoneSDRLink.)
    public sealed class SpecFind
    {
        public const int N = 2048, HOP = 512, BINS = N / 2;
        public const double HZ = 8000.0 / N;
        public const double LOW = 250, HIGH = 2850;

        readonly float[] ring = new float[N];
        readonly double[] win = new double[N], re = new double[N], im = new double[N];
        readonly double[] cosT = new double[N / 2], sinT = new double[N / 2];
        readonly double[] pw = new double[BINS];
        readonly double[] cum = new double[BINS + 1];
        int rp, sinceHop;
        long total;
        bool dirty;

        static int JRound(double x) { return (int)Math.Floor(x + 0.5); }   // Math.round de Java

        public SpecFind()
        {
            for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
            for (int i = 0; i < N / 2; i++) { cosT[i] = Math.Cos(2 * Math.PI * i / N); sinT[i] = -Math.Sin(2 * Math.PI * i / N); }
        }

        public void Add(short[] s, int n)
        {
            for (int i = 0; i < n; i++)
            {
                ring[rp] = s[i] / 32768f;
                rp = (rp + 1) & (N - 1);
                total++;
                if (++sinceHop >= HOP && total >= N) { sinceHop = 0; Hop(); }
            }
        }

        void Hop()
        {
            for (int i = 0; i < N; i++) { re[i] = ring[(rp + i) & (N - 1)] * win[i]; im[i] = 0; }
            Fft();
            for (int b = 0; b < BINS; b++) pw[b] = pw[b] * 0.95 + re[b] * re[b] + im[b] * im[b];
            dirty = true;
        }

        void Fft()
        {
            int j = 0;
            for (int i = 0; i < N - 1; i++)
            {
                if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
                int m = N >> 1;
                while (m >= 1 && j >= m) { j -= m; m >>= 1; }
                j += m;
            }
            for (int len = 2; len <= N; len <<= 1)
            {
                int half = len >> 1, step = N / len;
                for (int i = 0; i < N; i += len)
                {
                    for (int k = 0; k < half; k++)
                    {
                        double wr = cosT[k * step], wi = sinT[k * step];
                        int a = i + k, b = a + half;
                        double tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                    }
                }
            }
        }

        void Prep()
        {
            if (!dirty) return;
            cum[0] = 0;
            for (int b = 0; b < BINS; b++) cum[b + 1] = cum[b] + pw[b];
            dirty = false;
        }

        /** Puissance moyenne entre f0 et f1 (Hz), -1 si la plage est vide. */
        double Mean(double f0, double f1)
        {
            int b0 = Math.Max(0, Math.Min(BINS, JRound(f0 / HZ))), b1 = Math.Max(0, Math.Min(BINS, JRound(f1 / HZ)));
            if (b1 <= b0) return -1;
            return (cum[b1] - cum[b0]) / (b1 - b0);
        }

        /** Rapport entre la puissance de la fenêtre [c-w/2, c+w/2] et celle des flancs (largeur fl de chaque côté, limités à LOW..HIGH) ; 0 si les flancs sont trop courts. */
        public double Ratio(double c, double w, double fl)
        {
            Prep();
            double inn = Mean(c - w / 2, c + w / 2);
            if (inn <= 0) return 0;
            double l0 = Math.Max(LOW, c - w / 2 - fl), l1 = c - w / 2, r0 = c + w / 2, r1 = Math.Min(HIGH, c + w / 2 + fl);
            double sum = 0, len = 0;
            if (l1 - l0 > 0) { double m = Mean(l0, l1); if (m >= 0) { sum += m * (l1 - l0); len += l1 - l0; } }
            if (r1 - r0 > 0) { double m = Mean(r0, r1); if (m >= 0) { sum += m * (r1 - r0); len += r1 - r0; } }
            if (len < Math.Max(30, 0.5 * fl)) return 0;
            double fm = sum / len;
            return fm <= 0 ? 0 : inn / fm;
        }

        /** Meilleur centre de fenêtre de largeur w (flancs de 0,75 w) : { centre en Hz, rapport } ; centre 0 si rien d'exploitable. */
        public double[] Best(double w)
        {
            Prep();
            double lo = Math.Max(LOW + w / 4, w / 2 + 100), hi = Math.Min(HIGH - w / 4, 3300 - w / 2);
            double bc = 0, br = 0;
            for (double c = lo; c <= hi; c += HZ)
            {
                double r = Ratio(c, w, 0.75 * w);
                if (r > br) { br = r; bc = c; }
            }
            return new double[] { bc, br };
        }

        /** Case la plus forte (en Hz) entre f0 et f1 sur le spectre lissé. */
        public double Peak(double f0, double f1)
        {
            Prep();
            int b0 = Math.Max(1, JRound(f0 / HZ)), b1 = Math.Min(BINS - 2, JRound(f1 / HZ));
            int bb = b0;
            double bp = -1;
            for (int b = b0; b <= b1; b++)
            {
                double p = pw[b - 1] + pw[b] + pw[b + 1];
                if (p > bp) { bp = p; bb = b; }
            }
            return bb * HZ;
        }
    }
}
