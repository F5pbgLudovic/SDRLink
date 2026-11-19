using System;

namespace SDRLink
{
    // Notch automatique (DNF) pour le son du poste sur le PC (1.50) : écoute du poste (RigMonitor) et oreille du poste en diversité.
    // Mêmes trois types, mêmes niveaux et même réglage que le notch de la page du WebSDR (WebSdrBridge), portés ici en C# :
    // 0 = d'origine (analyse toutes les 100 ms, de 4 à 8 filtres coupe-bande étroits qui suivent les raies stables, comme les
    // BiquadFilter du navigateur), 1 = adaptatif (annulateur de raies NLMS), 2 = spectral (FFT).
    // Chaque flux audio a son RigNotch (état propre) ; le réglage, lui, est commun et posé par l'interface (Configure).
    public sealed class RigNotch
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

        // Traite x[0..n) en place (son mono, échelle ±1). Sans effet quand le notch est arrêté pour le poste.
        public void Process(int sampleRate, float[] x, int n)
        {
            int s = shared;
            if (s < 0 || n <= 0 || sampleRate < 4000) { applied = -1; return; }
            if (sampleRate != rate) { rate = sampleRate; cores = new ICore[3]; applied = -1; }
            int a = s / 16, l = s % 16;
            ICore c = cores[a];
            if (c == null) c = cores[a] = Make(a, rate);
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

        static ICore Make(int a, int sr) { return a == 2 ? (ICore)new Spectral(sr) : a == 1 ? (ICore)new Adaptive(sr) : new Original(sr); }

        // Pour les essais : un type donné, sans passer par le réglage commun.
        public static Func<float[], int, float[]> TestCore(int algo, int sampleRate, int level)
        {
            ICore c = Make(algo, sampleRate);
            c.SetLevel(level);
            return delegate(float[] x, int n) { float[] y = new float[n]; c.Process(x, n, y); return y; };
        }

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

        // ---- D'origine : comme le notch de la page. Spectre des derniers ~85 ms (fenêtre de Blackman, en dB) toutes les 100 ms,
        // moyenne glissante, plancher = médiane de 150 à 4000 Hz ; une raie au-dessus du plancher d'un seuil pendant plusieurs
        // analyses reçoit un filtre coupe-bande (formule des BiquadFilter « notch » du navigateur), qui glisse vers elle et la suit ;
        // sans raie pendant 10 analyses, le filtre repart tout en haut du spectre. Niveaux : mêmes tables que la page.
        sealed class Original : ICore
        {
            const int NOTCHES = 8, RELEASE = 10, BLOCK = 32;
            static readonly int[] NL_COUNT = { 4, 4, 5, 5, 6, 6, 7, 7, 8, 8 }, NL_HOLD = { 6, 6, 5, 5, 4, 4, 3, 3, 3, 2 };
            static readonly double[] NL_ABOVE = { 18, 16, 14, 12, 10, 9, 8, 7, 6, 5 }, NL_WIDTH = { 40, 48, 56, 64, 72, 80, 90, 100, 110, 120 };
            readonly int sr, N, every;
            readonly double hz, rest;
            readonly double[] hist, win, re, im, cur, avg;
            double[] cnt;
            readonly Fft2 fft;
            int hpos, since, nAct = 4, hold = 6;
            double above = 18, width = 40;
            bool first = true;
            // Filtres : fréquence actuelle, visée, constante de temps du glissement, Q, coefficients et état (forme directe I).
            readonly double[] f = new double[NOTCHES], target = new double[NOTCHES], tc = new double[NOTCHES], q = new double[NOTCHES];
            readonly double[] b0 = new double[NOTCHES], b1 = new double[NOTCHES], a1 = new double[NOTCHES], a2 = new double[NOTCHES];
            readonly double[] x1 = new double[NOTCHES], x2 = new double[NOTCHES], y1 = new double[NOTCHES], y2 = new double[NOTCHES];
            readonly int[] bin = new int[NOTCHES], miss = new int[NOTCHES];
            readonly bool[] placed = new bool[NOTCHES];
            int blockPos;

            public Original(int sampleRate)
            {
                sr = sampleRate;
                N = 256; while (N < 4096 && sr / (double)N > 11.8) N <<= 1;
                hz = sr / (double)N;
                every = Math.Max(1, (int)Math.Round(sr * 0.1));
                rest = sr * 0.49;
                hist = new double[N]; win = new double[N]; re = new double[N]; im = new double[N];
                cur = new double[N / 2]; avg = new double[N / 2]; cnt = new double[N / 2];
                for (int i = 0; i < N; i++) win[i] = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / N) + 0.08 * Math.Cos(4 * Math.PI * i / N);
                fft = new Fft2(N);
                Reset();
            }

            public void SetLevel(int l)
            {
                l = Math.Max(1, Math.Min(10, l)) - 1;
                int old = nAct;
                nAct = NL_COUNT[l]; above = NL_ABOVE[l]; hold = NL_HOLD[l]; width = NL_WIDTH[l];
                for (int j = nAct; j < old; j++) Release(j);
            }

            public void Reset()
            {
                Array.Clear(hist, 0, N); hpos = 0; since = 0; first = true;
                Array.Clear(cnt, 0, cnt.Length);
                for (int i = 0; i < NOTCHES; i++)
                {
                    f[i] = target[i] = rest; tc[i] = 0.01; q[i] = 30; bin[i] = -1; miss[i] = 0; placed[i] = false;
                    x1[i] = x2[i] = y1[i] = y2[i] = 0;
                    Coefs(i);
                }
                blockPos = 0;
            }

            void Coefs(int i)
            {
                double w0 = 2 * Math.PI * Math.Min(f[i], sr * 0.4999) / sr, alpha = Math.Sin(w0) / (2 * q[i]), a0 = 1 + alpha, c = Math.Cos(w0);
                b0[i] = 1 / a0; b1[i] = -2 * c / a0; a1[i] = -2 * c / a0; a2[i] = (1 - alpha) / a0;
            }

            void Release(int i)
            {
                bin[i] = -1; placed[i] = false;
                target[i] = rest; tc[i] = 0.01;
            }

            void Place(int s, int k)
            {
                double a = avg[k - 1], b = avg[k], c = avg[k + 1], d = a - 2 * b + c;
                double fr = (k + (d != 0 ? 0.5 * (a - c) / d : 0)) * hz;
                bin[s] = k; miss[s] = 0;
                target[s] = fr; tc[s] = placed[s] ? 0.05 : 0.005;
                q[s] = Math.Max(5, fr / width);
                placed[s] = true;
            }

            void Analyse()
            {
                int H = N / 2;
                for (int j = 0; j < N; j++) { re[j] = hist[(hpos + j) % N] * win[j]; im[j] = 0; }
                fft.Run(re, im, false);
                int lo = Math.Max(2, (int)Math.Ceiling(150 / hz)), hi = Math.Min(H - 3, (int)Math.Floor(4000 / hz));
                for (int k = lo - 1; k <= hi + 1; k++)
                {
                    double m = Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / N;
                    double v = m > 0 ? 20 * Math.Log10(m) : -160;
                    if (v < -160) v = -160;
                    avg[k] = first ? v : 0.6 * avg[k] + 0.4 * v;
                }
                first = false;
                double[] sorted = new double[hi - lo + 1];
                Array.Copy(avg, lo, sorted, 0, sorted.Length);
                Array.Sort(sorted);
                double floor = sorted[sorted.Length >> 1];
                if (floor < -140) return;   // silence
                double[] nc = new double[H];
                var peaks = new System.Collections.Generic.List<int>();
                for (int k = lo; k <= hi; k++)
                    if (avg[k] > floor + above && avg[k] >= avg[k - 1] && avg[k] >= avg[k + 1])
                    {
                        nc[k] = Math.Max(cnt[k - 1], Math.Max(cnt[k], cnt[k + 1])) + 1;
                        if (nc[k] >= hold) peaks.Add(k);
                    }
                cnt = nc;
                peaks.Sort((x, y) => avg[y].CompareTo(avg[x]));
                var used = new System.Collections.Generic.List<int>();
                for (int s = 0; s < nAct; s++)
                {
                    if (bin[s] < 0) continue;
                    int best = -1;
                    foreach (int p in peaks) if (Math.Abs(p - bin[s]) <= 3 && !used.Contains(p)) { best = p; break; }
                    if (best < 0) { if (++miss[s] > RELEASE) Release(s); continue; }
                    miss[s] = 0;
                    used.Add(best);
                    Place(s, best);
                }
                foreach (int p in peaks)
                {
                    if (used.Contains(p)) continue;
                    for (int t = 0; t < nAct; t++)
                    {
                        if (bin[t] >= 0) continue;
                        used.Add(p);
                        Place(t, p);
                        break;
                    }
                }
            }

            public void Process(float[] x, int n, float[] y)
            {
                for (int s = 0; s < n; s++)
                {
                    double v = x[s];
                    hist[hpos] = v; hpos = (hpos + 1) % N;
                    if (++since >= every) { since = 0; Analyse(); }
                    if (blockPos == 0)
                        for (int i = 0; i < NOTCHES; i++)
                        {
                            if (f[i] == target[i]) continue;
                            f[i] += (target[i] - f[i]) * (1 - Math.Exp(-BLOCK / (sr * tc[i])));
                            if (Math.Abs(f[i] - target[i]) < 0.01) f[i] = target[i];
                            Coefs(i);
                        }
                    blockPos = (blockPos + 1) % BLOCK;
                    for (int i = 0; i < NOTCHES; i++)
                    {
                        double o = b0[i] * v + b1[i] * x1[i] + b0[i] * x2[i] - a1[i] * y1[i] - a2[i] * y2[i];
                        x2[i] = x1[i]; x1[i] = v; y2[i] = y1[i]; y1[i] = o;
                        v = o;
                    }
                    if (double.IsNaN(v) || double.IsInfinity(v)) { Reset(); v = 0; }
                    y[s] = (float)v;
                }
            }
        }

        // ---- Adaptatif : annulateur de raies NLMS (SdrlinkAnf de la page). Le son est prédit à partir de celui d'il y a ~8 ms ;
        // ce qui est prévisible (sifflements, porteuses) est retranché. Niveau 1 à 10 : vitesse d'adaptation 0,0015 à 0,06.
        sealed class Adaptive : ICore
        {
            readonly int L, D, mask;
            readonly double[] buf, w;
            int pos;
            double mu = 0.01;
            public Adaptive(int sr)
            {
                L = Math.Max(32, Math.Min(256, (int)Math.Round(sr * 0.008))); D = Math.Max(16, (int)Math.Round(sr * 0.008));
                int M = 1; while (M < D + L + 2) M <<= 1;
                mask = M - 1; buf = new double[M]; w = new double[L];
                SetLevel(5);
            }
            public void SetLevel(int l) { l = Math.Max(1, Math.Min(10, l)); mu = 0.0015 * Math.Pow(1.5, l - 1); }
            public void Reset() { Array.Clear(buf, 0, buf.Length); Array.Clear(w, 0, L); pos = 0; }
            public void Process(float[] x, int n, float[] y)
            {
                bool bad = false;
                for (int i = 0; i < n; i++)
                {
                    double s = x[i], yy = 0, en = 1e-6, v;
                    int p = (pos - D) & mask;
                    for (int j = 0; j < L; j++) { v = buf[(p - j) & mask]; yy += w[j] * v; en += v * v; }
                    double e = s - yy;
                    if (double.IsNaN(e) || e > 4 || e < -4) { bad = true; e = 0; }
                    double g = mu * e / en;
                    for (int j = 0; j < L; j++) w[j] = w[j] * 0.99995 + g * buf[(p - j) & mask];
                    buf[pos] = s; pos = (pos + 1) & mask;
                    y[i] = (float)e;
                }
                if (bad) Reset();
            }
        }

        // ---- Spectral (SdrlinkSpf de la page) : tranches de ~43 ms ; une fréquence qui dépasse la médiane de ses voisines d'un
        // seuil pendant plusieurs tranches est ramenée à leur niveau, avec ses voisines immédiates. Niveau 1 à 10 : seuil 15 à 7 dB,
        // présence 0,35 à 0,08 s, atténuation maximale 20 à 40 dB, largeur 3 à 5 fréquences. Retard : une tranche.
        sealed class Spectral : ICore
        {
            readonly int N, H, K;
            readonly double frames;
            readonly double[] win, inb, ola, outq, re, im, sdb, ex, gn, tg, tmp = new double[12];
            readonly byte[] cnt;
            readonly Fft2 fft;
            int fill, opos, hold = 8, wid = 1;
            bool first = true;
            double thr = 11, floorG = 0.03;
            public Spectral(int sr)
            {
                N = sr <= 12500 ? 512 : sr <= 24000 ? 1024 : 2048; H = N >> 1; K = H + 1;
                frames = sr / (double)H;
                win = new double[N]; inb = new double[N]; ola = new double[H]; outq = new double[H]; re = new double[N]; im = new double[N];
                sdb = new double[K]; ex = new double[K]; gn = new double[K]; tg = new double[K]; cnt = new byte[K];
                for (int i = 0; i < N; i++) win[i] = Math.Sqrt(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N));
                fft = new Fft2(N);
                SetLevel(5);
                Reset();
            }
            public void SetLevel(int l)
            {
                l = Math.Max(1, Math.Min(10, l)) - 1;
                thr = 15 - 0.9 * l;
                hold = Math.Max(3, (int)Math.Round((0.35 - 0.03 * l) * frames));
                floorG = Math.Pow(10, -(20 + 2.2 * l) / 20);
                wid = l < 4 ? 1 : 2;
            }
            public void Reset()
            {
                Array.Clear(inb, 0, N); Array.Clear(ola, 0, H); Array.Clear(outq, 0, H); Array.Clear(cnt, 0, K); Array.Clear(ex, 0, K);
                for (int k = 0; k < K; k++) gn[k] = 1;
                fill = H; opos = 0; first = true;
            }
            void Frame()
            {
                for (int j = 0; j < N; j++) { re[j] = inb[j] * win[j]; im[j] = 0; }
                fft.Run(re, im, false);
                for (int k = 0; k < K; k++)
                {
                    double v = 10 * Math.Log10(re[k] * re[k] + im[k] * im[k] + 1e-10);
                    sdb[k] = first ? v : sdb[k] + (v - sdb[k]) * 0.35;
                }
                first = false;
                for (int k = 0; k < K; k++) tg[k] = 1;
                for (int k = 2; k < K - 1; k++)
                {
                    int m = 0, t;
                    for (int d = 3; d <= 8; d++)
                    {
                        double v;
                        if (k - d >= 1) { v = sdb[k - d]; for (t = m; t > 0 && tmp[t - 1] > v; t--) tmp[t] = tmp[t - 1]; tmp[t] = v; m++; }
                        if (k + d < K) { v = sdb[k + d]; for (t = m; t > 0 && tmp[t - 1] > v; t--) tmp[t] = tmp[t - 1]; tmp[t] = v; m++; }
                    }
                    if (m < 4) continue;
                    double b = (m & 1) != 0 ? tmp[m >> 1] : 0.5 * (tmp[(m >> 1) - 1] + tmp[m >> 1]);
                    ex[k] = sdb[k] - b;
                    if (ex[k] > thr) { if (cnt[k] < 250) cnt[k]++; } else cnt[k] = (byte)(cnt[k] > 2 ? cnt[k] - 2 : 0);
                }
                for (int k = 2; k < K - 1; k++)
                {
                    if (cnt[k] < hold) continue;
                    double a = Math.Max(floorG, Math.Min(1, Math.Pow(10, -ex[k] / 20)));
                    if (a < tg[k]) tg[k] = a;
                    for (int d = 1; d <= wid; d++)
                    {
                        double c = Math.Pow(a, d == 1 ? 0.7 : 0.4);
                        if (k - d >= 1 && c < tg[k - d]) tg[k - d] = c;
                        if (k + d < K && c < tg[k + d]) tg[k + d] = c;
                    }
                }
                for (int k = 0; k < K; k++)
                {
                    gn[k] += (tg[k] - gn[k]) * (tg[k] < gn[k] ? 0.7 : 0.1);
                    re[k] *= gn[k]; im[k] *= gn[k];
                    if (k > 0 && k < H) { re[N - k] *= gn[k]; im[N - k] *= gn[k]; }
                }
                fft.Run(re, im, true);
                for (int j = 0; j < H; j++) outq[j] = ola[j] + re[j] / N * win[j];
                for (int j = 0; j < H; j++) ola[j] = re[H + j] / N * win[H + j];
            }
            public void Process(float[] x, int n, float[] y)
            {
                for (int i = 0; i < n; i++)
                {
                    y[i] = (float)outq[opos];
                    inb[fill++] = x[i];
                    opos++;
                    if (fill == N)
                    {
                        Frame();
                        Array.Copy(inb, H, inb, 0, H);
                        fill = H; opos = 0;
                    }
                }
                if (double.IsNaN(outq[0]) || double.IsInfinity(outq[0])) Reset();
            }
        }
    }
}
