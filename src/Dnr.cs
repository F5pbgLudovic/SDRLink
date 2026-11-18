using System;

namespace SDRLink
{
    // Réducteur de bruit pour le son du poste sur le PC (1.48) : écoute du poste (RigMonitor) et oreille du poste en diversité.
    // Mêmes trois types, mêmes niveaux et même réglage que le DNR de la page du WebSDR (WebSdrBridge : SdrlinkDnr, SdrlinkLsa,
    // SdrlinkRnn), portés ici en C# : 0 = classique (Wiener), 1 = amélioré (MMSE-LSA), 2 = IA (RNNoise 0.1.1, licence BSD).
    // Chaque flux audio a son NoiseReducer (état propre) ; le réglage, lui, est commun et posé par l'interface (Configure).
    public sealed class NoiseReducer
    {
        // -1 = arrêté, sinon type * 16 + niveau. Lu par les fils audio à chaque bloc.
        static volatile int shared = -1;

        public static void Configure(bool on, int algo, int level)
        {
            shared = on ? Math.Max(0, Math.Min(2, algo)) * 16 + Math.Max(1, Math.Min(10, level)) : -1;
        }

        public static bool Active { get { return shared >= 0; } }

        interface ICore { void SetLevel(int l); void Reset(); void Process(float[] x, int n, float[] y); }

        ICore[] cores = new ICore[3];
        int rate, applied = -1;
        float[] tmp = new float[0];

        // Traite x[0..n) en place (son mono, échelle ±1). Sans effet quand le DNR est arrêté pour le poste.
        public void Process(int sampleRate, float[] x, int n)
        {
            int s = shared;
            if (s < 0 || n <= 0 || sampleRate < 4000) { applied = -1; return; }
            if (sampleRate != rate) { rate = sampleRate; cores = new ICore[3]; applied = -1; }
            int a = s / 16, l = s % 16;
            ICore c = cores[a];
            if (c == null) c = cores[a] = a == 2 ? (ICore)new Rnn(rate) : a == 1 ? (ICore)new Lsa(rate) : new Wiener(rate);
            if (applied != s)
            {
                if (applied < 0 || applied / 16 != a) c.Reset();
                c.SetLevel(l);
                applied = s;
            }
            if (tmp.Length < n) tmp = new float[n];
            c.Process(x, n, tmp);
            Array.Copy(tmp, x, n);
        }

        // Pour les essais : un type donné, sans passer par le réglage commun.
        public static Func<float[], int, float[]> TestCore(int algo, int sampleRate, int level)
        {
            ICore c = algo == 2 ? (ICore)new Rnn(sampleRate) : algo == 1 ? (ICore)new Lsa(sampleRate) : new Wiener(sampleRate);
            c.SetLevel(level);
            return delegate(float[] x, int n) { float[] y = new float[n]; c.Process(x, n, y); return y; };
        }

        // ---- Transformée de Fourier en base 2 (classique et amélioré).
        sealed class Fft2
        {
            readonly int N;
            readonly double[] cs, sn;
            readonly int[] rev;
            public Fft2(int n)
            {
                N = n;
                cs = new double[n / 2]; sn = new double[n / 2]; rev = new int[n];
                for (int i = 0; i < n / 2; i++) { cs[i] = Math.Cos(2 * Math.PI * i / n); sn[i] = -Math.Sin(2 * Math.PI * i / n); }
                int bits = (int)Math.Round(Math.Log(n) / Math.Log(2));
                for (int i = 0; i < n; i++) { int r = 0; for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b); rev[i] = r; }
            }
            public void Run(double[] re, double[] im, bool inv)
            {
                for (int j = 0; j < N; j++) { int k = rev[j]; if (k > j) { double t = re[j]; re[j] = re[k]; re[k] = t; t = im[j]; im[j] = im[k]; im[k] = t; } }
                for (int len = 2; len <= N; len <<= 1)
                {
                    int half = len >> 1, step = N / len;
                    for (int j = 0; j < N; j += len)
                        for (int k = 0; k < half; k++)
                        {
                            double wr = cs[k * step], wi = inv ? -sn[k * step] : sn[k * step];
                            int p = j + k, q = p + half;
                            double tr = re[q] * wr - im[q] * wi, ti = re[q] * wi + im[q] * wr;
                            re[q] = re[p] - tr; im[q] = im[p] - ti; re[p] += tr; im[p] += ti;
                        }
                }
            }
        }

        // ---- Découpage commun : tranches de N points recouvertes de moitié, fenêtre racine de Hann, retard d'une tranche.
        abstract class Stft : ICore
        {
            protected readonly int N, H, K;
            protected readonly double[] re, im;
            readonly double[] win, inb, ola, outq;
            readonly Fft2 fft;
            int fill, opos;
            protected Stft(int sr)
            {
                N = sr <= 16000 ? 256 : sr <= 32000 ? 512 : 1024; H = N / 2; K = H + 1;
                win = new double[N]; inb = new double[N]; ola = new double[N]; outq = new double[H]; re = new double[N]; im = new double[N];
                for (int i = 0; i < N; i++) win[i] = Math.Sqrt(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N));
                fft = new Fft2(N);
            }
            public abstract void SetLevel(int l);
            protected abstract void Clear();
            protected abstract void Gains();   // agit sur re / im (fréquences 0..H, et leurs symétriques)
            public void Reset() { Array.Clear(inb, 0, N); Array.Clear(ola, 0, N); Array.Clear(outq, 0, H); fill = N - H; opos = 0; Clear(); }
            protected void Apply(int k, double g) { re[k] *= g; im[k] *= g; if (k > 0 && k < H) { re[N - k] *= g; im[N - k] *= g; } }
            void Frame()
            {
                for (int j = 0; j < N; j++) { re[j] = inb[j] * win[j]; im[j] = 0; }
                fft.Run(re, im, false);
                Gains();
                fft.Run(re, im, true);
                for (int j = 0; j < N; j++) ola[j] += re[j] * win[j] / N;
                for (int j = 0; j < H; j++) outq[j] = ola[j];
                if (double.IsNaN(outq[0]) || double.IsInfinity(outq[0])) { Reset(); return; }   // jamais de NaN qui resterait
                Array.Copy(ola, H, ola, 0, N - H); Array.Clear(ola, N - H, H);
                Array.Copy(inb, H, inb, 0, N - H);
            }
            public void Process(float[] x, int n, float[] y)
            {
                for (int s = 0; s < n; s++)
                {
                    inb[fill++] = x[s];
                    y[s] = (float)outq[opos++];
                    if (fill == N) { Frame(); fill = N - H; opos = 0; }
                }
            }
        }

        // Classique (1.46) : filtre de Wiener, bruit = minimum du niveau lissé sur environ 1,6 s, SNR « décision dirigée ».
        sealed class Wiener : Stft
        {
            const int U = 8; const double BIAS = 2.0;
            readonly int V;
            readonly double[] psm, cmin, mins, pmin, spow;
            int frames, sub, slot;
            double floorG = 0.5, beta = 1;
            public Wiener(int sr) : base(sr)
            {
                V = Math.Max(4, (int)Math.Round(1.6 * sr / H / U));
                psm = new double[K]; cmin = new double[K]; mins = new double[K * U]; pmin = new double[K]; spow = new double[K];
                Reset();
            }
            public override void SetLevel(int l)
            {
                l = Math.Max(1, Math.Min(10, l)) - 1;
                floorG = Math.Pow(10, -(6 + 3 * l) / 20.0);
                beta = 1 + 0.22 * l;
            }
            protected override void Clear() { Array.Clear(spow, 0, K); frames = 0; sub = 0; slot = 0; }
            protected override void Gains()
            {
                bool turn = ++sub >= V;
                for (int k = 0; k < K; k++)
                {
                    double P = re[k] * re[k] + im[k] * im[k];
                    if (frames == 0)
                    {
                        psm[k] = P; cmin[k] = P; pmin[k] = P;
                        for (int j = 0; j < U; j++) mins[k * U + j] = P;
                    }
                    else
                    {
                        psm[k] = 0.8 * psm[k] + 0.2 * P;
                        if (psm[k] < cmin[k]) cmin[k] = psm[k];
                        if (psm[k] < pmin[k]) pmin[k] = psm[k];
                        if (turn)
                        {
                            mins[k * U + slot] = cmin[k];
                            double m = cmin[k];
                            for (int j = 0; j < U; j++) if (mins[k * U + j] < m) m = mins[k * U + j];
                            pmin[k] = m;
                            cmin[k] = psm[k];
                        }
                    }
                    double nz = pmin[k] * BIAS * beta + 1e-20, post = P / nz;
                    double xi = 0.98 * spow[k] / nz + 0.02 * (post > 1 ? post - 1 : 0);
                    double g = xi / (1 + xi);
                    if (!(g >= floorG)) g = floorG;
                    spow[k] = g * g * P;
                    Apply(k, g);
                }
                if (turn) { sub = 0; slot = (slot + 1) % U; }
                frames++;
            }
        }

        // Amélioré (1.48) : bruit estimé par probabilité de présence de la parole (Gerkmann et Hendriks), gain MMSE-LSA mélangé au
        // plancher selon la présence de voix (OM-LSA), relâchement doux d'une tranche à l'autre.
        sealed class Lsa : Stft
        {
            const double XI1 = 31.62, XIMIN = 0.003;
            readonly double G1 = XI1 / (1 + XI1), LX = Math.Log(1 + XI1), aN, rel;
            readonly double[] lam, pbar, gprev, gam, gs;
            double gmin = 0.3;
            int frames;
            public Lsa(int sr) : base(sr)
            {
                double hop = (double)H / sr;
                aN = Math.Exp(-hop / 0.072); rel = Math.Pow(0.5, hop / 0.01);
                lam = new double[K]; pbar = new double[K]; gprev = new double[K]; gam = new double[K]; gs = new double[K];
                Reset();
            }
            public override void SetLevel(int l) { l = Math.Max(1, Math.Min(10, l)) - 1; gmin = Math.Pow(10, -(8 + 2.45 * l) / 20); }
            protected override void Clear()
            {
                Array.Clear(lam, 0, K); Array.Clear(pbar, 0, K); Array.Clear(gs, 0, K);
                for (int k = 0; k < K; k++) { gprev[k] = 1; gam[k] = 1; }
                frames = 0;
            }
            static double E1(double v)
            {
                if (v < 1e-8) v = 1e-8;
                if (v < 1) return -0.57721566 - Math.Log(v) + v - v * v / 4 + v * v * v / 18 - v * v * v * v / 96;
                return Math.Exp(-v) / v * (v * v + 2.334733 * v + 0.250621) / (v * v + 3.330657 * v + 1.681534);
            }
            protected override void Gains()
            {
                double lg = Math.Log(gmin);
                for (int k = 0; k < K; k++)
                {
                    double P = re[k] * re[k] + im[k] * im[k];
                    if (frames < 8) lam[k] = (lam[k] * frames + P) / (frames + 1);
                    else
                    {
                        double ph = 1 / (1 + Math.Exp(LX - P / (lam[k] + 1e-20) * G1));
                        pbar[k] = 0.9 * pbar[k] + 0.1 * ph;
                        if (pbar[k] > 0.99 && ph > 0.99) ph = 0.99;
                        lam[k] = aN * lam[k] + (1 - aN) * ((1 - ph) * P + ph * lam[k]);
                    }
                    double g = P / (lam[k] + 1e-20);
                    if (g > 1e4) g = 1e4;
                    double xi = 0.98 * gprev[k] * gprev[k] * gam[k] + 0.02 * (g > 1 ? g - 1 : 0);
                    if (xi < XIMIN) xi = XIMIN;
                    double v = xi * g / (1 + xi), G = xi / (1 + xi) * Math.Exp(0.5 * E1(v));
                    if (!(G <= 1)) G = 1;
                    double p = 1 / (1 + (1 + xi) * Math.Exp(-v));
                    double Gf = Math.Exp(p * Math.Log(G + 1e-20) + (1 - p) * lg);
                    if (Gf < gmin) Gf = gmin;
                    gprev[k] = G; gam[k] = g;
                    if (Gf < rel * gs[k]) Gf = rel * gs[k];
                    gs[k] = Gf;
                    Apply(k, Gf);
                }
                frames++;
            }
        }

        // ---- IA (1.48) : RNNoise 0.1.1 (Jean-Marc Valin, Xiph.Org / Mozilla, licence BSD), à 48 kHz par tranches de 10 ms.
        sealed class Rnn : ICore
        {
            const int FS = 480, WS = 960, FQ = 481, NB = 22, PBS = 1728, CM = 8;
            static readonly int[] eb = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 34, 40, 48, 60, 78, 100 };
            static readonly int[] SC = { 0, 0, 3, 2, 3, 2, 5, 2, 3, 2, 3, 2, 5, 2, 3, 2 };
            static readonly int[] fac = { 4, 4, 4, 3, 5 };

            // Poids communs à tous les flux, transposés une fois (une ligne contiguë par neurone).
            static float[] idW, idB, vgW, vgR, vgB, ngW, ngR, ngB, dgW, dgR, dgB, doW, doB, voW, voB;
            static readonly object wLock = new object();
            static void LoadWeights()
            {
                lock (wLock)
                {
                    if (idW != null) return;
                    byte[] w = Convert.FromBase64String(RnnoiseWeights.B64);
                    int off = 0;
                    Func<int, int, float[]> take = delegate(int M, int Nn)
                    {
                        float[] a = new float[M * Nn];
                        for (int c = 0; c < M; c++) for (int k = 0; k < Nn; k++) a[k * M + c] = (sbyte)w[off + c * Nn + k];
                        off += M * Nn; return a;
                    };
                    float[] a1 = take(42, 24), a2 = take(1, 24), a3 = take(24, 72), a4 = take(24, 72), a5 = take(1, 72), a6 = take(90, 144), a7 = take(48, 144), a8 = take(1, 144);
                    float[] a9 = take(114, 288), a10 = take(96, 288), a11 = take(1, 288), a12 = take(96, 22), a13 = take(1, 22), a14 = take(24, 1), a15 = take(1, 1);
                    idB = a2; vgW = a3; vgR = a4; vgB = a5; ngW = a6; ngR = a7; ngB = a8; dgW = a9; dgR = a10; dgB = a11; doW = a12; doB = a13; voW = a14; voB = a15;
                    idW = a1;
                }
            }

            // Transformée de Fourier de 960 points (bases 4, 4, 4, 3, 5).
            static int[] perm;
            static double[][] stc, sts;
            static int[] sLp;
            static readonly object fLock = new object();
            static int Src(int pos, int n, int s) { if (s == 0) return 0; int p = fac[s - 1], m = n / p; return pos / m + p * Src(pos % m, m, s - 1); }
            static void FftTables()
            {
                lock (fLock)
                {
                    if (perm != null) return;
                    int[] pm = new int[WS];
                    for (int i = 0; i < WS; i++) pm[i] = Src(i, WS, fac.Length);
                    stc = new double[fac.Length][]; sts = new double[fac.Length][]; sLp = new int[fac.Length];
                    int Lp = 1;
                    for (int s = 0; s < fac.Length; s++)
                    {
                        int P = fac[s], L = Lp * P;
                        double[] tc = new double[Lp * P], ts = new double[Lp * P];
                        for (int k = 0; k < Lp; k++) for (int q = 0; q < P; q++) { tc[k * P + q] = Math.Cos(2 * Math.PI * q * k / L); ts[k * P + q] = -Math.Sin(2 * Math.PI * q * k / L); }
                        stc[s] = tc; sts[s] = ts; sLp[s] = Lp;
                        Lp = L;
                    }
                    perm = pm;
                }
            }

            readonly double[] fr = new double[WS], fi = new double[WS], tr = new double[WS], ti = new double[WS], ar = new double[5], ai = new double[5];
            void Fft()
            {
                const double C3 = 0.8660254037844386, c1 = 0.30901699437494745, c2 = -0.8090169943352474, s1 = 0.9510565162951535, s2 = 0.5877852522924731;
                for (int p = 0; p < WS; p++) { tr[p] = fr[perm[p]]; ti[p] = fi[perm[p]]; }
                for (int s = 0; s < fac.Length; s++)
                {
                    int P = fac[s], Lp = sLp[s], L = Lp * P;
                    double[] tc = stc[s], ts = sts[s];
                    for (int b = 0; b < WS; b += L)
                        for (int k = 0; k < Lp; k++)
                        {
                            int o = b + k, w = k * P;
                            for (int q = 0; q < P; q++)
                            {
                                int x = o + q * Lp; double cq = tc[w + q], sq = ts[w + q];
                                ar[q] = tr[x] * cq - ti[x] * sq; ai[q] = tr[x] * sq + ti[x] * cq;
                            }
                            if (P == 4)
                            {
                                double t0r = ar[0] + ar[2], t0i = ai[0] + ai[2], t1r = ar[0] - ar[2], t1i = ai[0] - ai[2], t2r = ar[1] + ar[3], t2i = ai[1] + ai[3], t3r = ar[1] - ar[3], t3i = ai[1] - ai[3];
                                tr[o] = t0r + t2r; ti[o] = t0i + t2i;
                                tr[o + Lp] = t1r + t3i; ti[o + Lp] = t1i - t3r;
                                tr[o + 2 * Lp] = t0r - t2r; ti[o + 2 * Lp] = t0i - t2i;
                                tr[o + 3 * Lp] = t1r - t3i; ti[o + 3 * Lp] = t1i + t3r;
                            }
                            else if (P == 3)
                            {
                                double sr3 = ar[1] + ar[2], si3 = ai[1] + ai[2], dr3 = ar[1] - ar[2], di3 = ai[1] - ai[2], mr = ar[0] - 0.5 * sr3, mi = ai[0] - 0.5 * si3;
                                tr[o] = ar[0] + sr3; ti[o] = ai[0] + si3;
                                tr[o + Lp] = mr + C3 * di3; ti[o + Lp] = mi - C3 * dr3;
                                tr[o + 2 * Lp] = mr - C3 * di3; ti[o + 2 * Lp] = mi + C3 * dr3;
                            }
                            else
                            {
                                double b1r = ar[1] + ar[4], b1i = ai[1] + ai[4], b2r = ar[2] + ar[3], b2i = ai[2] + ai[3], d1r = ar[1] - ar[4], d1i = ai[1] - ai[4], d2r = ar[2] - ar[3], d2i = ai[2] - ai[3];
                                double m1r = ar[0] + c1 * b1r + c2 * b2r, m1i = ai[0] + c1 * b1i + c2 * b2i, m2r = ar[0] + c2 * b1r + c1 * b2r, m2i = ai[0] + c2 * b1i + c1 * b2i;
                                double n1r = s1 * d1r + s2 * d2r, n1i = s1 * d1i + s2 * d2i, n2r = s2 * d1r - s1 * d2r, n2i = s2 * d1i - s1 * d2i;
                                tr[o] = ar[0] + b1r + b2r; ti[o] = ai[0] + b1i + b2i;
                                tr[o + Lp] = m1r + n1i; ti[o + Lp] = m1i - n1r;
                                tr[o + 4 * Lp] = m1r - n1i; ti[o + 4 * Lp] = m1i + n1r;
                                tr[o + 2 * Lp] = m2r + n2i; ti[o + 2 * Lp] = m2i - n2r;
                                tr[o + 3 * Lp] = m2r - n2i; ti[o + 3 * Lp] = m2i + n2r;
                            }
                        }
                }
                Array.Copy(tr, fr, WS); Array.Copy(ti, fi, WS);
            }

            static readonly double[] hw = new double[FS], dctT = new double[NB * NB];
            static Rnn()
            {
                for (int i = 0; i < FS; i++) { double sv = Math.Sin(0.5 * Math.PI * (i + 0.5) / FS); hw[i] = Math.Sin(0.5 * Math.PI * sv * sv); }
                for (int i = 0; i < NB; i++) for (int j = 0; j < NB; j++) dctT[i * NB + j] = Math.Cos((i + 0.5) * j * Math.PI / NB) * (j == 0 ? Math.Sqrt(0.5) : 1);
            }
            static void Dct(double[] o, double[] inp) { double sc = Math.Sqrt(2.0 / 22); for (int a = 0; a < NB; a++) { double s = 0; for (int c = 0; c < NB; c++) s += inp[c] * dctT[c * NB + a]; o[a] = s * sc; } }
            static void Win(double[] x) { for (int a = 0; a < FS; a++) { x[a] *= hw[a]; x[WS - 1 - a] *= hw[a]; } }

            readonly double[] Xr = new double[FQ], Xi = new double[FQ], Pr = new double[FQ], Pi = new double[FQ], xw = new double[WS], pw = new double[WS];
            void Fwd2(double[] xa, double[] xb)
            {
                for (int a = 0; a < WS; a++) { fr[a] = xa[a]; fi[a] = xb[a]; }
                Fft();
                for (int a = 0; a < FQ; a++)
                {
                    int b2 = a == 0 ? 0 : WS - a;
                    double zr = fr[a], zi = fi[a], wr = fr[b2], wi = fi[b2];
                    Xr[a] = (zr + wr) / (2 * WS); Xi[a] = (zi - wi) / (2 * WS);
                    Pr[a] = (zi + wi) / (2 * WS); Pi[a] = (wr - zr) / (2 * WS);
                }
            }
            void Inv(double[] o)
            {
                int a;
                for (a = 0; a < FQ; a++) { fr[a] = Xr[a]; fi[a] = -Xi[a]; }
                for (; a < WS; a++) { fr[a] = Xr[WS - a]; fi[a] = Xi[WS - a]; }
                Fft();
                Array.Copy(fr, o, WS);
            }
            static void BandE(double[] o, double[] Ar, double[] Ai, double[] Br, double[] Bi)
            {
                Array.Clear(o, 0, NB);
                for (int a = 0; a < NB - 1; a++)
                {
                    int bs = (eb[a + 1] - eb[a]) << 2, b0 = eb[a] << 2;
                    for (int c = 0; c < bs; c++) { double f = (double)c / bs, t = Ar[b0 + c] * Br[b0 + c] + Ai[b0 + c] * Bi[b0 + c]; o[a] += (1 - f) * t; o[a + 1] += f * t; }
                }
                o[0] *= 2; o[NB - 1] *= 2;
            }
            static void Interp(double[] g, double[] bE)
            {
                Array.Clear(g, 0, FQ);
                for (int a = 0; a < NB - 1; a++)
                {
                    int bs = (eb[a + 1] - eb[a]) << 2, b0 = eb[a] << 2;
                    for (int c = 0; c < bs; c++) { double f = (double)c / bs; g[b0 + c] = (1 - f) * bE[a] + f * bE[a + 1]; }
                }
            }

            // Hauteur de la voix (pitch.c et celt_lpc.c, version flottante).
            readonly double[] xlp = new double[PBS / 2], ac = new double[5], lpc = new double[4], yyl = new double[385], xc4 = new double[147], xc = new double[294], x4 = new double[240], y4 = new double[387];
            readonly int[] best = new int[2];
            void Down(double[] x)
            {
                int h = PBS / 2, a, k;
                for (a = 1; a < h; a++) xlp[a] = 0.5 * (0.5 * (x[2 * a - 1] + x[2 * a + 1]) + x[2 * a]);
                xlp[0] = 0.5 * (0.5 * x[1] + x[0]);
                for (k = 0; k <= 4; k++) { double s = 0; for (a = k; a < h; a++) s += xlp[a] * xlp[a - k]; ac[k] = s; }
                ac[0] *= 1.0001;
                for (a = 1; a <= 4; a++) ac[a] -= ac[a] * (0.008 * a) * (0.008 * a);
                double err = ac[0];
                Array.Clear(lpc, 0, 4);
                if (ac[0] != 0)
                    for (a = 0; a < 4; a++)
                    {
                        double rr = 0; for (k = 0; k < a; k++) rr += lpc[k] * ac[a - k];
                        rr += ac[a + 1];
                        double r = -rr / err; lpc[a] = r;
                        for (k = 0; k < (a + 1) >> 1; k++) { double t1 = lpc[k], t2 = lpc[a - 1 - k]; lpc[k] = t1 + r * t2; lpc[a - 1 - k] = t2 + r * t1; }
                        err = err - r * r * err;
                        if (err < 0.001 * ac[0]) break;
                    }
                double tmp = 1; for (a = 0; a < 4; a++) { tmp *= 0.9; lpc[a] *= tmp; }
                double n0 = lpc[0] + 0.8, n1 = lpc[1] + 0.8 * lpc[0], n2 = lpc[2] + 0.8 * lpc[1], n3 = lpc[3] + 0.8 * lpc[2], n4 = 0.8 * lpc[3];
                double m0 = 0, m1 = 0, m2 = 0, m3 = 0, m4 = 0;
                for (a = 0; a < h; a++)
                {
                    double xi0 = xlp[a], s2 = xi0 + n0 * m0 + n1 * m1 + n2 * m2 + n3 * m3 + n4 * m4;
                    m4 = m3; m3 = m2; m2 = m1; m1 = m0; m0 = xi0; xlp[a] = s2;
                }
            }
            void FindBest(double[] xcr, double[] y, int len, int maxp)
            {
                double Syy = 1, bn0 = -1, bn1 = -1, bd0 = 0, bd1 = 0;
                best[0] = 0; best[1] = 1;
                for (int a = 0; a < len; a++) Syy += y[a] * y[a];
                for (int a = 0; a < maxp; a++)
                {
                    if (xcr[a] > 0)
                    {
                        double x16 = xcr[a] * 1e-12, num = x16 * x16;
                        if (num * bd1 > bn1 * Syy)
                        {
                            if (num * bd0 > bn0 * Syy) { bn1 = bn0; bd1 = bd0; best[1] = best[0]; bn0 = num; bd0 = Syy; best[0] = a; }
                            else { bn1 = num; bd1 = Syy; best[1] = a; }
                        }
                    }
                    Syy += y[a + len] * y[a + len] - y[a] * y[a];
                    if (Syy < 1) Syy = 1;
                }
            }
            int Search()
            {
                const int X0 = 384;
                for (int a = 0; a < 240; a++) x4[a] = xlp[X0 + 2 * a];
                for (int a = 0; a < 387; a++) y4[a] = xlp[2 * a];
                for (int k = 0; k < 147; k++) { double s = 0; for (int a = 0; a < 240; a++) s += x4[a] * y4[k + a]; xc4[k] = s; }
                FindBest(xc4, y4, 240, 147);
                int b0 = best[0], b1 = best[1];
                for (int k = 0; k < 294; k++)
                {
                    xc[k] = 0;
                    if (Math.Abs(k - 2 * b0) > 2 && Math.Abs(k - 2 * b1) > 2) continue;
                    double s3 = 0; for (int a = 0; a < 480; a++) s3 += xlp[X0 + a] * xlp[k + a];
                    xc[k] = Math.Max(-1, s3);
                }
                FindBest(xc, xlp, 480, 294);
                int o = 0;
                if (best[0] > 0 && best[0] < 293)
                {
                    double pa = xc[best[0] - 1], pb = xc[best[0]], pc = xc[best[0] + 1];
                    if (pc - pa > 0.7 * (pb - pa)) o = 1; else if (pa - pc > 0.7 * (pb - pc)) o = -1;
                }
                return 2 * best[0] - o;
            }
            double Dot(int o1, int o2, int n) { double s = 0; for (int a = 0; a < n; a++) s += xlp[o1 + a] * xlp[o2 + a]; return s; }
            int Doubling(int T0in, int prevP, double prevG, out double pgOut)
            {
                const int maxp = 384, minp = 30, Nn = 480, xo = 384;
                int T0 = T0in / 2, pp = prevP / 2;
                if (T0 >= maxp) T0 = maxp - 1;
                int T = T0;
                double xx = Dot(xo, xo, Nn), xy = Dot(xo, xo - T0, Nn), yy = xx;
                yyl[0] = xx;
                for (int a = 1; a <= maxp; a++) { yy = yy + xlp[xo - a] * xlp[xo - a] - xlp[xo + Nn - a] * xlp[xo + Nn - a]; yyl[a] = Math.Max(0, yy); }
                yy = yyl[T0];
                double bxy = xy, byy = yy, g0 = xy / Math.Sqrt(1 + xx * yy), g = g0;
                for (int k = 2; k <= 15; k++)
                {
                    int T1 = (2 * T0 + k) / (2 * k), T1b;
                    if (T1 < minp) break;
                    if (k == 2) T1b = T1 + T0 > maxp ? T0 : T0 + T1;
                    else T1b = (2 * SC[k] * T0 + k) / (2 * k);
                    double xy1 = 0.5 * (Dot(xo, xo - T1, Nn) + Dot(xo, xo - T1b, Nn)), yy1 = 0.5 * (yyl[T1] + yyl[T1b]);
                    double g1 = xy1 / Math.Sqrt(1 + xx * yy1), cont = 0;
                    if (Math.Abs(T1 - pp) <= 1) cont = prevG;
                    else if (Math.Abs(T1 - pp) <= 2 && 5 * k * k < T0) cont = 0.5 * prevG;
                    double th = Math.Max(0.3, 0.7 * g0 - cont);
                    if (T1 < 3 * minp) th = Math.Max(0.4, 0.85 * g0 - cont);
                    else if (T1 < 2 * minp) th = Math.Max(0.5, 0.9 * g0 - cont);
                    if (g1 > th) { bxy = xy1; byy = yy1; T = T1; g = g1; }
                }
                bxy = Math.Max(0, bxy);
                double pg = byy <= bxy ? 1 : bxy / (byy + 1), cc0 = Dot(xo, xo - (T - 1), Nn), cc1 = Dot(xo, xo - T, Nn), cc2 = Dot(xo, xo - (T + 1), Nn);
                int off = 0;
                if (cc2 - cc0 > 0.7 * (cc1 - cc0)) off = 1; else if (cc0 - cc2 > 0.7 * (cc1 - cc2)) off = -1;
                if (pg > g) pg = g;
                int T0o = 2 * T + off;
                if (T0o < 60) T0o = 60;
                pgOut = pg;
                return T0o;
            }

            // Réseau (rnn.c) : couches denses et GRU, poids entiers / 256.
            static double Sig(double v) { return 1 / (1 + Math.Exp(-v)); }
            static double VDot(float[] W, int o, double[] v, int n) { double s = 0; for (int c = 0; c < n; c++) s += W[o + c] * v[c]; return s; }
            static void Dense(float[] W, float[] B, int M, int Nn, double[] inp, double[] o, bool tanh)
            {
                for (int a = 0; a < Nn; a++) { double s = (B[a] + VDot(W, a * M, inp, M)) / 256; o[a] = tanh ? Math.Tanh(s) : Sig(s); }
            }
            readonly double[] gz = new double[96], gr = new double[96], gh = new double[96], gsv = new double[96];
            void Gru(float[] Wi, float[] Wr, float[] B, int M, int Nn, double[] st, double[] inp)
            {
                for (int a = 0; a < Nn; a++) gz[a] = Sig((B[a] + VDot(Wi, a * M, inp, M) + VDot(Wr, a * Nn, st, Nn)) / 256);
                for (int a = 0; a < Nn; a++) { gr[a] = Sig((B[Nn + a] + VDot(Wi, (Nn + a) * M, inp, M) + VDot(Wr, (Nn + a) * Nn, st, Nn)) / 256); gsv[a] = st[a] * gr[a]; }
                for (int a = 0; a < Nn; a++)
                {
                    double s = (B[2 * Nn + a] + VDot(Wi, (2 * Nn + a) * M, inp, M) + VDot(Wr, (2 * Nn + a) * Nn, gsv, Nn)) / 256;
                    if (s < 0) s = 0;
                    gh[a] = gz[a] * st[a] + (1 - gz[a]) * s;
                }
                Array.Copy(gh, st, Nn);
            }

            // État.
            double[] amem, smem, pbuf, cmem, lastg, vst, nst, dst;
            int memid, lastP;
            double lastG, hp0, hp1, floorG;
            readonly double[] Ex = new double[NB], Ep = new double[NB], Exp = new double[NB], Ly = new double[NB], tmpB = new double[NB], feat = new double[42];
            readonly double[] g = new double[NB], gf = new double[FQ], rf = new double[FQ], dOut = new double[24], vad = new double[1];
            readonly double[] nIn = new double[90], dIn = new double[114], rr2 = new double[NB], xin = new double[FS], y480 = new double[FS];

            bool Features()
            {
                double E = 0;
                for (int a = 0; a < FS; a++) { xw[a] = amem[a]; xw[FS + a] = xin[a]; amem[a] = xin[a]; }
                Win(xw);
                Array.Copy(pbuf, FS, pbuf, 0, PBS - FS); Array.Copy(xin, 0, pbuf, PBS - FS, FS);
                Down(pbuf);
                double pg;
                int pi = Doubling(768 - Search(), lastP, lastG, out pg);
                lastP = pi; lastG = pg;
                for (int a = 0; a < WS; a++) pw[a] = pbuf[PBS - WS - pi + a];
                Win(pw); Fwd2(xw, pw);
                BandE(Ex, Xr, Xi, Xr, Xi); BandE(Ep, Pr, Pi, Pr, Pi); BandE(Exp, Xr, Xi, Pr, Pi);
                for (int a = 0; a < NB; a++) Exp[a] = Exp[a] / Math.Sqrt(0.001 + Ex[a] * Ep[a]);
                Dct(tmpB, Exp);
                for (int a = 0; a < 6; a++) feat[34 + a] = tmpB[a];
                feat[34] -= 1.3; feat[35] -= 0.9; feat[40] = 0.01 * (pi - 300);
                double logMax = -2, follow = -2;
                for (int a = 0; a < NB; a++)
                {
                    Ly[a] = Math.Log10(1e-2 + Ex[a]);
                    Ly[a] = Math.Max(logMax - 7, Math.Max(follow - 1.5, Ly[a]));
                    logMax = Math.Max(logMax, Ly[a]); follow = Math.Max(follow - 1.5, Ly[a]); E += Ex[a];
                }
                if (E < 0.04) { Array.Clear(feat, 0, 42); return true; }
                Dct(tmpB, Ly);
                Array.Copy(tmpB, feat, NB);
                feat[0] -= 12; feat[1] -= 4;
                int c0 = memid * NB, c1 = ((memid + CM - 1) % CM) * NB, c2 = ((memid + CM - 2) % CM) * NB;
                for (int a = 0; a < NB; a++) cmem[c0 + a] = feat[a];
                memid++;
                for (int a = 0; a < 6; a++)
                {
                    feat[a] = cmem[c0 + a] + cmem[c1 + a] + cmem[c2 + a];
                    feat[NB + a] = cmem[c0 + a] - cmem[c2 + a];
                    feat[NB + 6 + a] = cmem[c0 + a] - 2 * cmem[c1 + a] + cmem[c2 + a];
                }
                if (memid == CM) memid = 0;
                double sv2 = 0;
                for (int a = 0; a < CM; a++)
                {
                    double md = 1e15;
                    for (int b2 = 0; b2 < CM; b2++)
                    {
                        if (b2 == a) continue;
                        double d = 0; for (int k = 0; k < NB; k++) { double t = cmem[a * NB + k] - cmem[b2 * NB + k]; d += t * t; }
                        if (d < md) md = d;
                    }
                    sv2 += md;
                }
                feat[41] = sv2 / CM - 2.1;
                return false;
            }
            void PFilter()
            {
                for (int a = 0; a < NB; a++)
                {
                    double r;
                    if (Exp[a] > g[a]) r = 1;
                    else r = Exp[a] * Exp[a] * (1 - g[a] * g[a]) / (0.001 + g[a] * g[a] * (1 - Exp[a] * Exp[a]));
                    r = Math.Sqrt(Math.Min(1, Math.Max(0, r)));
                    rr2[a] = r * Math.Sqrt(Ex[a] / (1e-8 + Ep[a]));
                }
                Interp(rf, rr2);
                for (int a = 0; a < FQ; a++) { Xr[a] += rf[a] * Pr[a]; Xi[a] += rf[a] * Pi[a]; }
                BandE(tmpB, Xr, Xi, Xr, Xi);
                for (int a = 0; a < NB; a++) rr2[a] = Math.Sqrt(Ex[a] / (1e-8 + tmpB[a]));
                Interp(rf, rr2);
                for (int a = 0; a < FQ; a++) { Xr[a] *= rf[a]; Xi[a] *= rf[a]; }
            }
            void Frame()
            {
                for (int a = 0; a < FS; a++) { double xi0 = xin[a], yi = xi0 + hp0; hp0 = hp1 + (-2 * xi0 + 1.99599 * yi); hp1 = xi0 - 0.996 * yi; xin[a] = yi; }
                if (!Features())
                {
                    Dense(idW, idB, 42, 24, feat, dOut, true);
                    Gru(vgW, vgR, vgB, 24, 24, vst, dOut);
                    Dense(voW, voB, 24, 1, vst, vad, false);
                    for (int a = 0; a < 24; a++) { nIn[a] = dOut[a]; nIn[24 + a] = vst[a]; }
                    Array.Copy(feat, 0, nIn, 48, 42);
                    Gru(ngW, ngR, ngB, 90, 48, nst, nIn);
                    Array.Copy(vst, 0, dIn, 0, 24);
                    Array.Copy(nst, 0, dIn, 24, 48);
                    Array.Copy(feat, 0, dIn, 72, 42);
                    Gru(dgW, dgR, dgB, 114, 96, dst, dIn);
                    Dense(doW, doB, 96, 22, dst, g, false);
                    PFilter();
                    for (int a = 0; a < NB; a++) { g[a] = Math.Max(g[a], 0.6 * lastg[a]); lastg[a] = g[a]; if (g[a] < floorG) g[a] = floorG; }
                    Interp(gf, g);
                    for (int a = 0; a < FQ; a++) { Xr[a] *= gf[a]; Xi[a] *= gf[a]; }
                }
                Inv(xw); Win(xw);
                for (int a = 0; a < FS; a++) { y480[a] = xw[a] + smem[a]; smem[a] = xw[FS + a]; }
            }

            // Pour les essais : une tranche de 480 échantillons à 48 kHz, échelle 16 bits.
            public double Frame480(float[] x, int o, float[] y)
            {
                for (int a = 0; a < FS; a++) xin[a] = x[o + a];
                Frame();
                for (int a = 0; a < FS; a++) y[o + a] = (float)y480[a];
                return vad[0];
            }

            // Rééchantillonneur polyphase (sinus cardinal fenêtré).
            sealed class Rs
            {
                readonly bool same, exact;
                readonly int half, T, P, stepN;
                readonly double stepF;
                readonly float[] co;
                float[] buf = new float[1 << 15];
                int len, ip; double fr0;
                public readonly int Lat;
                static int Gcd(int u, int v) { while (v != 0) { int t = u % v; u = v; v = t; } return u; }
                public Rs(int fi0, int fo)
                {
                    if (fi0 == fo) { same = true; return; }
                    double fc = 0.9 * Math.Min(1.0, (double)fo / fi0);
                    half = (int)Math.Ceiling(12 / fc); T = 2 * half; Lat = half;
                    int gg = Gcd(fi0, fo);
                    P = fo / gg; exact = P <= 1024;
                    if (!exact) P = 1024;
                    stepN = exact ? fi0 / gg : 0; stepF = (double)fi0 / fo;
                    co = new float[P * T];
                    for (int ph = 0; ph < P; ph++)
                    {
                        double sum = 0;
                        for (int j = 0; j < T; j++)
                        {
                            double u = Math.Abs((double)ph / P + half - 1 - j), h = 0;
                            if (u < half) h = fc * (u == 0 ? 1 : Math.Sin(Math.PI * fc * u) / (Math.PI * fc * u)) * (0.42 + 0.5 * Math.Cos(Math.PI * u / half) + 0.08 * Math.Cos(2 * Math.PI * u / half));
                            co[ph * T + j] = (float)h; sum += h;
                        }
                        for (int j = 0; j < T; j++) co[ph * T + j] = (float)(co[ph * T + j] / sum);
                    }
                    Reset();
                }
                public void Reset() { if (same) return; Array.Clear(buf, 0, buf.Length); len = half; ip = half; fr0 = 0; }
                public int Push(float[] x, int xo, int n, float[] o)
                {
                    if (same) { Array.Copy(x, xo, o, 0, n); return n; }
                    int c = 0;
                    if (len + n > buf.Length) { float[] nb = new float[(len + n) * 2]; Array.Copy(buf, nb, len); buf = nb; }
                    Array.Copy(x, xo, buf, len, n); len += n;
                    while (ip + half < len)
                    {
                        int phs = exact ? (int)fr0 : (int)Math.Round(fr0 * P), i0 = ip - half + 1;
                        if (phs >= P) { phs -= P; i0++; }
                        int b = phs * T; double s = 0;
                        for (int a = 0; a < T; a++) s += buf[i0 + a] * co[b + a];
                        o[c++] = (float)s;
                        if (exact) { fr0 += stepN; while (fr0 >= P) { fr0 -= P; ip++; } }
                        else { fr0 += stepF; int w = (int)Math.Floor(fr0); ip += w; fr0 -= w; }
                    }
                    int d = ip - half - 1;
                    if (d > 0) { Array.Copy(buf, d, buf, 0, len - d); len -= d; ip -= d; }
                    return c;
                }
            }

            readonly Rs up, dn;
            readonly float[] u48 = new float[1 << 15], dnb = new float[4096], f48 = new float[FS], oq = new float[1 << 15];
            readonly int D;
            long oqr, oqw;
            int fpos;

            public Rnn(int sr)
            {
                LoadWeights(); FftTables();
                up = new Rs(sr, 48000); dn = new Rs(48000, sr);
                D = (int)Math.Ceiling(FS * sr / 48000.0) + up.Lat + (int)Math.Ceiling(dn.Lat * sr / 48000.0) + 4;
                SetLevel(5);
                Reset();
            }
            public void SetLevel(int l) { l = Math.Max(1, Math.Min(10, l)); floorG = l >= 10 ? 0 : Math.Pow(10, -(6 + 3.5 * (l - 1)) / 20); }
            public void Reset()
            {
                amem = new double[FS]; smem = new double[FS]; pbuf = new double[PBS]; cmem = new double[CM * NB];
                memid = 0; lastG = 0; lastP = 0; hp0 = 0; hp1 = 0; lastg = new double[NB]; vst = new double[24]; nst = new double[48]; dst = new double[96];
                up.Reset(); dn.Reset(); Array.Clear(oq, 0, oq.Length); oqr = 0; oqw = D; fpos = 0;
            }
            public void Process(float[] x, int n, float[] y)
            {
                int M = oq.Length - 1, p = 0;
                while (p < n)
                {
                    int m = Math.Min(n - p, 1024), c = up.Push(x, p, m, u48);
                    for (int a = 0; a < c; a++)
                    {
                        f48[fpos++] = u48[a];
                        if (fpos == FS)
                        {
                            for (int b2 = 0; b2 < FS; b2++) xin[b2] = f48[b2] * 32768.0;
                            Frame();
                            bool bad = false;
                            for (int b2 = 0; b2 < FS; b2++) { double v = y480[b2] / 32768.0; if (double.IsNaN(v) || double.IsInfinity(v)) bad = true; f48[b2] = (float)v; }
                            if (bad) { Reset(); return; }
                            int k = dn.Push(f48, 0, FS, dnb);
                            for (int b2 = 0; b2 < k; b2++) { oq[(int)(oqw & M)] = dnb[b2]; oqw++; }
                            fpos = 0;
                        }
                    }
                    for (int a = 0; a < m; a++) { y[p + a] = oqr < oqw ? oq[(int)(oqr & M)] : 0; if (oqr < oqw) oqr++; }
                    p += m;
                }
            }
        }

        // Pour les essais : RNNoise à 48 kHz, tranche par tranche, comparable au C d'origine.
        public static Func<float[], float[], double[]> TestRnnFrames()
        {
            Rnn r = new Rnn(48000);
            r.SetLevel(10);
            return delegate(float[] x, float[] y)
            {
                int nf = x.Length / 480; double[] v = new double[nf];
                for (int f = 0; f < nf; f++) v[f] = r.Frame480(x, f * 480, y);
                return v;
            };
        }
    }
}
