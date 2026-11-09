using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace SDRLink
{
    // Décodeur SSTV à détection automatique du mode (code VIS), repris du décodeur Java de PhoneSDRLink : Robot 36 et 72, Robot 8, 12 et
    // 24 N&B, Martin M1 à M4, Scottie S1 à S4 et DX, PD50 à PD290, Wraase SC2-120 et SC2-180, Pasokon P3, P5 et P7.
    // Le son (ramené à 8 kHz) est ramené en bande de base autour de 1700 Hz, filtré (FIR à 31 coefficients), et la fréquence
    // instantanée vient de la différence de phase entre deux échantillons ; la phase cumulée est gardée dans un anneau de 16 s, ce qui
    // donne la fréquence moyenne de n'importe quel intervalle (un pixel) par une simple différence.
    // 1) Code VIS : premier 1900 Hz (au moins 120 ms), impulsion de 10 ms à 1200 Hz, second 1900 Hz (au moins 200 ms), puis 10 bits de
    //    30 ms (départ, 7 bits de données, parité paire, arrêt). Le décalage d'accord est mesuré sur le second 1900 Hz.
    // 2) Image : chaque bloc (ligne, ou paire de lignes en PD) commence par une impulsion de synchro 1200 Hz retrouvée autour de
    //    l'instant attendu ; la période réelle des blocs est estimée sur l'image (corrige l'inclinaison). 1500 Hz = noir, 2300 Hz = blanc.
    // 3) Image prise en cours de route (sans code VIS) : une suite d'impulsions de synchro au rythme exact d'un mode donne le mode.
    // Le calcul tourne sur son propre fil ; les événements sont levés depuis ce fil.
    public class SstvEngine
    {
        public event Action<string, int, int, int[]> ImageBegin;   // mode, largeur, hauteur, pixels (remplis au fil des lignes)
        public event Action<int> ImageRows;                        // lignes terminées
        public event Action<bool, int> ImageEnd;                   // image complète ou signal perdu, lignes terminées
        public event Action ImageScrolled;                         // l'émission continue après le bas de l'image : tout a remonté
        public event Action<string> Status;                       // message à afficher (début d'image, signal retrouvé, mode inconnu)

        const int RATE = 8000;
        const int RING = 1 << 17, MASK = RING - 1, FIRN = 31;
        const double CENTER = 1700.0, SPMS = 8.0;      // 8 échantillons par ms
        const int RGB = 0, R36 = 1, R72 = 2, PD = 3, GRAY = 4;
        const int R = 0, G = 1, B = 2, Y = 3, CR = 4, CB = 5, Y1 = 6;

        class Mode
        {
            public readonly string Name; public readonly int Vis, W, Kind;
            public int H, Rep = 1;   // Rep : chaque ligne reçue est répétée (Martin M3 et M4, Scottie S3 et S4, Robot 12 et 8 N&B)
            public readonly double SyncMs, PeriodMs, ScanMs;
            public double[] St, Len; public int[] Role;
            public bool Scottie;
            public Mode(string n, int v, int w, int h, int kind, double sync, double period, double scan)
            { Name = n; Vis = v; W = w; H = h; Kind = kind; SyncMs = sync; PeriodMs = period; ScanMs = scan; }
            public Mode Ch(double[] s, double[] l, int[] r) { St = s; Len = l; Role = r; return this; }
            public Mode Twice() { Rep = 2; return this; }
        }

        static readonly Mode[] Modes = Build();

        static Mode[] Build()
        {
            List<Mode> l = new List<Mode>();
            l.Add(Martin("Martin M1", 44, 146.432));
            l.Add(Martin("Martin M2", 40, 73.216));
            l.Add(ScottieMode("Scottie S1", 60, 138.240));
            l.Add(ScottieMode("Scottie S2", 56, 88.064));
            l.Add(ScottieMode("Scottie DX", 76, 345.6));
            l.Add(new Mode("Wraase SC2-180", 55, 320, 256, RGB, 5.5225, 5.5225 + 0.5 + 705, 235).Ch(
                new double[] { 6.0225 + 470, 6.0225, 6.0225 + 235 }, new double[] { 235, 235, 235 }, new int[] { B, R, G }));
            l.Add(new Mode("Robot 36", 8, 320, 240, R36, 9, 150, 88).Ch(new double[] { 12, 106 }, new double[] { 88, 44 }, new int[] { Y, CR }));
            l.Add(new Mode("Robot 72", 12, 320, 240, R72, 9, 300, 138).Ch(new double[] { 12, 156, 231 }, new double[] { 138, 69, 69 }, new int[] { Y, CR, CB }));
            l.Add(Pd("PD50", 93, 320, 256, 91.520));
            l.Add(Pd("PD90", 99, 320, 256, 170.240));
            l.Add(Pd("PD120", 95, 640, 496, 121.600));
            l.Add(Pd("PD160", 98, 512, 400, 195.584));
            l.Add(Pd("PD180", 96, 640, 496, 183.040));
            l.Add(Pd("PD240", 97, 640, 496, 244.480));
            l.Add(Pd("PD290", 94, 800, 616, 228.800));
            l.Add(Martin("Martin M3", 36, 146.432).Twice());
            l.Add(Martin("Martin M4", 32, 73.216).Twice());
            l.Add(ScottieMode("Scottie S3", 52, 138.240).Twice());
            l.Add(ScottieMode("Scottie S4", 48, 88.064).Twice());
            double w1 = 320 * 0.489039081;
            l.Add(new Mode("Wraase SC2-120", 63, 320, 256, RGB, 5.5225, 5.5225 + 0.5 + 3 * w1, w1).Ch(
                new double[] { 6.0225 + 2 * w1, 6.0225, 6.0225 + w1 }, new double[] { w1, w1, w1 }, new int[] { B, R, G }));
            l.Add(Pasokon("Pasokon P3", 113, 5.208, 1.042, 0.2083));
            l.Add(Pasokon("Pasokon P5", 114, 7.813, 1.563, 0.3125));
            l.Add(Pasokon("Pasokon P7", 115, 10.417, 2.083, 0.4167));
            l.Add(new Mode("Robot 24 BW", 10, 320, 240, GRAY, 7, 100, 93.12).Ch(new double[] { 7 }, new double[] { 93.12 }, new int[] { Y }));
            l.Add(new Mode("Robot 12 BW", 6, 320, 240, GRAY, 7, 100, 93.12).Ch(new double[] { 7 }, new double[] { 93.12 }, new int[] { Y }).Twice());
            l.Add(new Mode("Robot 8 BW", 2, 320, 240, GRAY, 7, 66.9, 59.9).Ch(new double[] { 7 }, new double[] { 59.9 }, new int[] { Y }).Twice());
            return l.ToArray();
        }

        static Mode Martin(string n, int vis, double scan)
        {
            double s0 = 4.862 + 0.572;
            return new Mode(n, vis, 320, 256, RGB, 4.862, s0 + 3 * (scan + 0.572), scan).Ch(
                new double[] { s0 + scan + 0.572 + scan + 0.572, s0, s0 + scan + 0.572 }, new double[] { scan, scan, scan }, new int[] { R, G, B });
        }

        static Mode ScottieMode(string n, int vis, double scan)
        {
            Mode m = new Mode(n, vis, 320, 256, RGB, 9, 3 * scan + 13.5, scan).Ch(
                new double[] { 10.5, -scan, -2 * scan - 1.5 }, new double[] { scan, scan, scan }, new int[] { R, B, G });
            m.Scottie = true;
            return m;
        }

        static Mode Pasokon(string n, int vis, double sync, double gap, double px)
        {
            double scan = 640 * px;
            return new Mode(n, vis, 640, 496, RGB, sync, sync + gap + 3 * (scan + gap), scan).Ch(
                new double[] { sync + gap, sync + gap + scan + gap, sync + gap + 2 * (scan + gap) }, new double[] { scan, scan, scan }, new int[] { R, G, B });
        }

        static Mode Pd(string n, int vis, int w, int h, double scan)
        {
            return new Mode(n, vis, w, h, PD, 20, 22.08 + 4 * scan, scan).Ch(
                new double[] { 22.08, 22.08 + scan, 22.08 + 2 * scan, 22.08 + 3 * scan }, new double[] { scan, scan, scan, scan }, new int[] { Y, CR, CB, Y1 });
        }

        static Mode ByVis(int v) { foreach (Mode m in Modes) if (m.Vis == v) return m; return null; }

        // Réglages par défaut de PhoneSDRLink (sensibilité 5 sur 10, perte data 4 sur 10 = 12 s).
        volatile int sensLevel = 5, lossLevel = 4;
        double sens = 4 * 4.0 / 9.0;
        int lossMs = 12000;
        static readonly int[] LOSS_S = { 3, 5, 8, 12, 18, 25, 35, 50, 75, 120 };   // « perte data » 1 à 10 : secondes

        // Sensibilité (1 à 10) et perte data (1 à 10), comme dans PhoneSDRLink ; pris en compte au bloc suivant.
        public void SetLevels(int sensitivity, int loss)
        {
            sensLevel = Math.Max(1, Math.Min(10, sensitivity));
            lossLevel = Math.Max(1, Math.Min(10, loss));
            sens = (sensLevel - 1) * 4.0 / 9.0;
            lossMs = LOSS_S[lossLevel - 1] * 1000;
        }
        int Tv1() { return (int)(130 + 25 * sens); }
        int Tv2() { return (int)(150 + 25 * sens); }
        int Tb() { return (int)(110 + 15 * sens); }
        int Tp() { return (int)(140 + 15 * sens); }
        int Ts() { return (int)(160 + 15 * sens); }
        double Sf() { return 0.5 - 0.06 * sens; }
        int Win() { return 16 + (int)(2 * sens); }

        static long Rnd(double x) { return (long)Math.Floor(x + 0.5); }

        // ---------------------------------------------------------------- fil de calcul et passage à 8 kHz
        readonly Thread thread;
        volatile bool stopping;
        volatile bool resetPending;
        readonly object qlock = new object();
        readonly Queue<float[]> queue = new Queue<float[]>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        int queued;
        float[] blk = new float[400];
        int blkN;

        // Passage à 8 kHz : filtre passe-bas (3 kHz, FIR de Blackman, coupure nette avant 4 kHz) à la fréquence d'entrée, puis interpolation cubique aux instants
        // de sortie. Une simple moyenne sur des intervalles de 1/8000 s ne tombe pas juste quand la fréquence d'entrée n'en est pas
        // un multiple (44,1 kHz) : les instants de sortie tremblent et la fréquence mesurée aussi (rayures dans l'image).
        const int HIST = 2048, HMASK = HIST - 1;
        int inRate;
        double[] lp;              // coefficients du passe-bas (nombre impair)
        readonly double[] hist = new double[HIST];
        long inCount;             // échantillons d'entrée reçus
        double tNext;             // instant (en échantillons d'entrée) du prochain échantillon de sortie

        void SetRate(int rate)
        {
            inRate = rate;
            inCount = 0;
            tNext = 0;
            Array.Clear(hist, 0, HIST);
            if (rate == 8000) { lp = null; return; }
            int n = (int)Math.Round(rate / 8000.0 * 32) | 1;
            if (n > 511) n = 511;
            lp = new double[n];
            double fc = 3000.0 / rate, sum = 0;
            for (int i = 0; i < n; i++)
            {
                double x = i - (n - 1) / 2.0;
                double s = x == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
                lp[i] = s * (0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (n - 1)));   // Blackman
                sum += lp[i];
            }
            for (int i = 0; i < n; i++) lp[i] /= sum;
        }

        // Signal filtré centré sur l'échantillon d'entrée i (il faut avoir reçu i + (n−1)/2).
        double Filtered(long i)
        {
            int h = (lp.Length - 1) / 2;
            double y = 0;
            for (int k = 0; k < lp.Length; k++) y += lp[k] * hist[(int)((i + h - k) & HMASK)];
            return y;
        }

        void Out(double v)
        {
            blk[blkN++] = (float)v;
            if (blkN == blk.Length)
            {
                if (queued < 100) { queue.Enqueue(blk); queued++; }   // 5 s au plus en attente
                blk = new float[400];
                blkN = 0;
            }
        }

        public SstvEngine()
        {
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "SSTV";
            thread.Start();
        }

        public void Stop()
        {
            stopping = true;
            wake.Set();
            thread.Join(3000);
        }

        // Nouvelle source : image en cours abandonnée, recherche reprise à zéro.
        public void Reset()
        {
            lock (qlock) { queue.Clear(); queued = 0; blkN = 0; inRate = 0; }
            resetPending = true;
            wake.Set();
        }

        public void Feed16(int rate, byte[] pcm)
        {
            float[] f = new float[pcm.Length / 2];
            for (int i = 0; i < f.Length; i++) f[i] = (short)(pcm[2 * i] | pcm[2 * i + 1] << 8) / 32768f;
            Feed(rate, f, f.Length);
        }

        public void Feed(int rate, float[] x, int n)
        {
            if (rate < 8000) return;
            lock (qlock)
            {
                if (rate != inRate) SetRate(rate);
                if (lp == null) { for (int i = 0; i < n; i++) Out(x[i]); }
                else
                {
                    double step = rate / 8000.0;
                    int h = (lp.Length - 1) / 2;
                    for (int i = 0; i < n; i++)
                    {
                        hist[(int)(inCount & HMASK)] = x[i];
                        inCount++;
                        // Échantillons filtrés i0−1 … i0+2 disponibles : sortie à l'instant tNext (interpolation de Catmull-Rom).
                        while (tNext >= 1 && (long)tNext + 2 + h < inCount)
                        {
                            long i0 = (long)tNext;
                            double f = tNext - i0;
                            double ym = Filtered(i0 - 1), y0 = Filtered(i0), y1 = Filtered(i0 + 1), y2 = Filtered(i0 + 2);
                            Out(y0 + 0.5 * f * (y1 - ym + f * (2 * ym - 5 * y0 + 4 * y1 - y2 + f * (3 * (y0 - y1) + y2 - ym))));
                            tNext += step;
                        }
                        if (tNext < 1) tNext = 1;
                    }
                }
            }
            wake.Set();
        }

        void Run()
        {
            try
            {
                while (!stopping)
                {
                    wake.WaitOne(50);
                    if (resetPending) { resetPending = false; if (img) Finish(false); Clear(); }
                    while (!stopping)
                    {
                        float[] b;
                        lock (qlock) { if (queue.Count == 0) break; b = queue.Dequeue(); queued--; }
                        Process(b, b.Length);
                    }
                }
            }
            catch (Exception ex) { Log.Write("SSTV : arrêt du décodage : " + ex.Message); }
        }

        // Remise à zéro de la démodulation et des recherches.
        void Clear()
        {
            Array.Clear(cum, 0, RING);
            Array.Clear(mr, 0, mr.Length);
            Array.Clear(mi, 0, mi.Length);
            ph = pr = pi = cumv = 0;
            n = 0; mp = 0;
            VisReset();
            np = 0; pRun = 0;
            ofs = 0;
        }

        // ---------------------------------------------------------------- démodulation
        readonly double[] cum = new double[RING];
        readonly double[] mr = new double[32], mi = new double[32];
        readonly double[] fir = new double[FIRN];
        double ph, pr, pi, cumv;
        long n;                 // nombre d'échantillons reçus
        int mp;
        bool firReady;

        void InitFir()
        {
            double fc = 1000.0 / RATE, sum = 0;
            for (int i = 0; i < FIRN; i++)
            {
                double x = i - (FIRN - 1) / 2.0;
                double s = x == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
                fir[i] = s * (0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FIRN - 1)));
                sum += fir[i];
            }
            for (int i = 0; i < FIRN; i++) fir[i] /= sum;
            firReady = true;
        }

        double CumAt(double x)
        {
            long i = (long)Math.Floor(x);
            double f = x - i;
            if (i + 1 >= n) { i = n - 2; f = 1; }
            return cum[(int)(i & MASK)] * (1 - f) + cum[(int)((i + 1) & MASK)] * f;
        }

        // Fréquence moyenne (Hz) entre les échantillons a et b (brute, sans correction d'accord).
        double FreqAvg(double a, double b)
        {
            if (b - a < 1e-6) b = a + 1;
            return (CumAt(b) - CumAt(a)) / (b - a) * RATE / (2 * Math.PI) + CENTER;
        }

        void Process(float[] s, int cnt)
        {
            if (!firReady) InitFir();
            for (int k = 0; k < cnt; k++)
            {
                double x = s[k];
                ph += 2 * Math.PI * CENTER / RATE;
                if (ph > 2 * Math.PI) ph -= 2 * Math.PI;
                mr[mp] = x * Math.Cos(ph);
                mi[mp] = -x * Math.Sin(ph);
                double yr = 0, yi = 0;
                for (int j = 0; j < FIRN; j++) { int q = (mp - j) & 31; yr += fir[j] * mr[q]; yi += fir[j] * mi[q]; }
                mp = (mp + 1) & 31;
                double inc = Math.Atan2(yi * pr - yr * pi, yr * pr + yi * pi);   // arg(y · conj(y précédent))
                pr = yr; pi = yi;
                cumv += inc;
                cum[(int)(n & MASK)] = cumv;
                n++;
                if (img) ImgStep();
                if ((n & 7) == 0 && n > 400) { VisStep(); PulseStep(); }   // une nouvelle image qui commence interrompt la précédente
            }
        }

        // ---------------------------------------------------------------- code VIS
        int vs, lead, miss, brk, lead2;
        long tStart;
        double ofs;             // décalage d'accord (Hz) : fréquence mesurée − fréquence nominale

        static bool Near(double f, double c, double w) { return Math.Abs(f - c) < w; }

        void VisReset() { vs = 0; lead = 0; miss = 0; brk = 0; lead2 = 0; }

        void VisStep()
        {
            double f = FreqAvg(n - Win(), n - 1);
            switch (vs)
            {
                case 0:
                    if (Near(f, 1900, Tv1())) { lead++; miss = 0; }
                    else if (lead >= 120 && f < 1500) { vs = 1; brk = 1; miss = 0; }
                    else if (++miss > 2 + sens) { lead = 0; miss = 0; }
                    break;
                case 1:
                    if (Near(f, 1200, Tv2())) { brk++; miss = 0; }
                    else if (brk >= 3 && brk <= 22 && Near(f, 1900, Tv2())) { vs = 2; lead2 = 1; miss = 0; }
                    else if (++miss > 2 + sens) VisReset();
                    break;
                case 2:
                    if (Near(f, 1900, Tv2())) { if (++lead2 > 700) VisReset(); miss = 0; }   // au-delà de 300 ms, ce n'est pas un VIS
                    else if (++miss > 2 + sens && f < 1550)
                    {
                        if (lead2 >= 200)
                        {
                            tStart = n - 8L * miss - 8;
                            double lf = FreqAvg(tStart - 150 * SPMS, tStart - 30 * SPMS);
                            ofs = Math.Abs(lf - 1900) < 120 ? lf - 1900 : 0;
                            vs = 3;
                        }
                        else VisReset();
                    }
                    else if (miss > 8 + 2 * sens) VisReset();
                    break;
                case 3:
                    if (n >= tStart + (long)(300 * SPMS) + 24) VisDecode();
                    break;
            }
        }

        void VisDecode()
        {
            double[] bit = new double[10];
            for (int j = 0; j < 10; j++) bit[j] = FreqAvg(tStart + (30 * j + 8) * SPMS, tStart + (30 * j + 22) * SPMS) - ofs;
            bool ok = Near(bit[0], 1200, Tb());
            int code = 0, ones = 0;
            for (int j = 1; j <= 8; j++)
            {
                if (!(Near(bit[j], 1100, Tb()) || Near(bit[j], 1300, Tb()))) ok = false;
                int b = bit[j] < 1200 ? 1 : 0;
                ones += b;
                if (j <= 7) code |= b << (j - 1);
            }
            if ((ones & 1) != 0) ok = false;
            Mode m = ok ? ByVis(code) : null;
            VisReset();
            if (m == null)
            {
                if (ok) Say(Lang.F("SSTV : mode non pris en charge (VIS {0})", "SSTV: unsupported mode (VIS {0})", code));
                return;
            }
            StartImage(m, tStart + (long)(300 * SPMS));
        }

        // ---------------------------------------------------------------- image prise en cours de route (sans code VIS)
        const int NP = 48;
        readonly long[] pT = new long[NP];
        readonly int[] pL = new int[NP];
        readonly double[] pF = new double[NP];
        int np, pRun;
        long pStart;
        double pSum;

        void PulseStep()
        {
            double f = FreqAvg(n - Win(), n - 1);
            if (Near(f, 1200, Tp()))
            {
                if (pRun == 0) { pStart = n - 8; pSum = 0; }
                pRun++;
                pSum += f;
            }
            else if (pRun > 0)
            {
                int effLen = pRun + Win() / 8 - 2;   // la moyenne de fréquence raccourcit l'impulsion vue
                if (effLen >= 3 && effLen <= 26)
                {
                    if (np == NP) { Array.Copy(pT, 1, pT, 0, NP - 1); Array.Copy(pL, 1, pL, 0, NP - 1); Array.Copy(pF, 1, pF, 0, NP - 1); np--; }
                    pT[np] = pStart; pL[np] = effLen; pF[np] = pSum / pRun; np++;
                }
                pRun = 0;
            }
            if ((n & 2047) == 0 && np >= 5 && vs != 3 && (!img || misses >= 4)) BlindStep();
        }

        void BlindStep()
        {
            Mode best = null;
            int bestChain = 0;
            long bestLast = 0;
            double bestOfs = 0;
            foreach (Mode m in Modes)
            {
                double P = m.PeriodMs * SPMS, tol = 3 * SPMS + 0.002 * P, wl = Math.Max(2.5, 0.3 * m.SyncMs);
                int j = -1;
                for (int i = np - 1; i >= 0; i--) if (Math.Abs(pL[i] - m.SyncMs) <= wl) { j = i; break; }
                if (j < 0) continue;
                long last = pT[j];
                int chain = 0, skips = 0;
                double[] fr = new double[NP];
                fr[0] = pF[j];
                for (int i = j - 1; i >= 0; i--)
                {
                    double d = pT[j] - pT[i];
                    if (Math.Abs(pL[i] - m.SyncMs) > wl) { if (d < P - tol) continue; else break; }
                    if (Math.Abs(d - P) <= tol) { fr[++chain] = pF[i]; j = i; }
                    else if (d < P - tol) skips++;
                    else break;
                }
                if (chain >= 4 && skips <= 1 && chain > bestChain)
                {
                    Array.Sort(fr, 0, chain + 1);
                    bestChain = chain; best = m; bestLast = last; bestOfs = fr[(chain + 1) / 2] - 1200;
                }
            }
            if (best == null) return;
            if (Math.Abs(bestOfs) < 120) ofs = bestOfs;
            if (img)
            {
                // Signal perdu pendant une image : il faut des impulsions plus récentes que la dernière synchro trouvée.
                if (bestLast <= sLast + (long)(0.5 * period) || n - bestLast > 6 * 8000L) return;
                if (best == md) { Resync(bestLast); return; }   // même mode : on reprend à la bonne ligne (lignes perdues en gris)
                Finish(false);                                   // autre mode : la première image est gardée telle quelle
            }
            StartBlind(best, bestLast);
        }

        void Resync(long syncStart)
        {
            int k = kLast + (int)Rnd((syncStart - sLast) / period);
            if (k < blockIdx || k >= BlocksTotal()) return;
            blockIdx = k; sLast = syncStart; kLast = k; expected = syncStart; hw = 12 * SPMS;
            misses = 0; pendY = null; awaitSync = true;
            Say(Lang.F("SSTV : {0} — signal retrouvé, ligne {1}", "SSTV: {0} — signal found again, line {1}", md.Name, k));
        }

        void StartBlind(Mode m, long syncStart)
        {
            NewImage(m);
            expected = syncStart;
            hw = 12 * SPMS;
            Say(Lang.F("SSTV : {0} ({1}×{2}) — début manquant, réception en cours de route", "SSTV: {0} ({1}×{2}) — start missing, picked up in progress", m.Name, m.W, m.H));
        }

        // ---------------------------------------------------------------- image
        Mode md;
        bool img;               // une image est en cours
        int[] px;
        int blockIdx, rowsDone, misses, foundCount;
        long s0found, sLast;
        int kLast;
        double period, expected, hw;
        bool awaitSync;
        long curS;
        int[] pendY, pendCr;
        int pendRow;
        // Bas de l'image atteint alors que l'émission continue (réception prise en cours de route, image plus longue, images à la
        // suite sans code VIS) : l'image remonte d'une ligne à chaque nouvelle ligne reçue, la plus récente en bas ; shift = blocs remontés.
        bool scrolling;
        int shift;

        void NewImage(Mode m)
        {
            np = 0; pRun = 0;
            md = m;
            px = new int[m.W * m.H];
            for (int i = 0; i < px.Length; i++) px[i] = unchecked((int)0xFF202020);
            blockIdx = 0; rowsDone = 0; misses = 0; foundCount = 0; kLast = 0; pendY = null;
            scrolling = false; shift = 0;
            period = m.PeriodMs * SPMS;
            awaitSync = true;
            img = true;
            Action<string, int, int, int[]> h = ImageBegin;
            if (h != null) h(m.Name, m.W, m.H, px);
        }

        void StartImage(Mode m, long visEnd)
        {
            if (img) Finish(false);   // une autre image commence : la précédente est abandonnée (gardée partielle)
            NewImage(m);
            expected = m.Scottie ? visEnd + (2 * m.ScanMs + 7) * SPMS : visEnd + 2 * SPMS;
            hw = 10 * SPMS;
            Say(Lang.F("SSTV : {0} ({1}×{2})", "SSTV: {0} ({1}×{2})", m.Name, m.W, m.H));
        }

        int BlocksTotal() { return md.Kind == PD ? md.H / 2 : md.H / md.Rep; }

        void ImgStep()
        {
            int L = (int)Rnd(md.SyncMs * SPMS);
            if (awaitSync)
            {
                if (n < expected + hw + L + 24) return;
                long s = FindSync(expected, hw, L);
                if (s < 0)
                {
                    if (blockIdx == 0) { Finish(false); return; }
                    // Après le bas de l'image, pas de synchro : l'émission est finie (une ligne manquée tolérée une fois que l'image remonte).
                    if (scrolling && (shift == 0 || misses >= 1)) { Finish(true); return; }
                    misses++;
                    s = Rnd(expected);
                }
                else
                {
                    misses = 0;
                    if (foundCount == 0) { s0found = s; kLast = blockIdx; }
                    foundCount++;
                    sLast = s; kLast = blockIdx;
                    double fs = FreqAvg(s + L * 0.25, s + L * 0.75);
                    if (Near(fs, 1200, 150)) ofs += 0.3 * ((fs - 1200) - ofs);   // l'accord suit les impulsions de synchro
                    if (blockIdx >= 6)
                    {
                        double pm = (s - s0found) / (double)blockIdx;
                        if (Math.Abs(pm - md.PeriodMs * SPMS) < 0.004 * md.PeriodMs * SPMS) period = pm;
                    }
                }
                if (misses * md.PeriodMs > lossMs) { Finish(false); return; }   // signal perdu depuis trop longtemps
                curS = s;
                awaitSync = false;
            }
            double after = 0;
            for (int i = 0; i < md.St.Length; i++) after = Math.Max(after, md.St[i] + md.Len[i]);
            if (md.Kind == R36) after = 150;
            if (n < curS + after * SPMS + 16) return;
            if (scrolling)
            {   // une ligne n'entre en bas (et l'image ne remonte) que si sa synchro a été trouvée
                if (misses == 0)
                {
                    if (blockIdx - shift >= BlocksTotal()) ScrollUp(md.Kind == R36 ? 2 : 1);
                    DecodeBlock(curS);
                }
            }
            else if (misses <= 6) DecodeBlock(curS);   // au-delà de quelques lignes sans synchro, les lignes restent grises
            blockIdx++;
            if (!scrolling && blockIdx >= BlocksTotal()) scrolling = true;   // la ligne suivante dira si l'émission continue (sinon : image terminée)
            expected = sLast + (blockIdx - kLast) * period;
            hw = (6 + 3 * Math.Min(misses, 5)) * SPMS;
            awaitSync = true;
        }

        // Début de l'impulsion de synchro (échantillon) dans [exp − hw, exp + hw], ou −1.
        long FindSync(double exp, double hwin, int L)
        {
            long lo = Rnd(exp - hwin), hi = Rnd(exp + hwin);
            int span = (int)(hi - lo) + L + 1;
            int[] pre = new int[span + 1];
            for (int i = 0; i < span; i++)
            {
                long x = lo + i;
                double f = FreqAvg(x - 3, x + 1) - ofs;
                pre[i + 1] = pre[i] + (Near(f, 1200, Ts()) ? 1 : -1);
            }
            long best = -1;
            int bs = int.MinValue;
            for (long c = lo; c <= hi; c++)
            {
                int sc = pre[(int)(c - lo) + L] - pre[(int)(c - lo)];
                if (sc >= bs) { bs = sc; best = c; }
            }
            return bs >= Sf() * L ? best : -1;
        }

        // Pixels d'un canal : cnt valeurs 0..255 (1500 Hz = 0, 2300 Hz = 255).
        int[] Pixels(long s, double aMs, double lenMs, int cnt)
        {
            int[] o = new int[cnt];
            double pw = lenMs * SPMS / cnt;
            for (int i = 0; i < cnt; i++)
            {
                double t0 = s + aMs * SPMS + i * pw, mid = t0 + pw / 2, half = Math.Max(pw / 2, 1.5);
                double f = FreqAvg(mid - half, mid + half) - ofs;
                int v = (int)Rnd((f - 1500) / 800.0 * 255);
                o[i] = v < 0 ? 0 : v > 255 ? 255 : v;
            }
            return o;
        }

        static int Clip(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        void Put(int row0, int[] a, int[] b, int[] c, bool ycc)
        {
            int w = md.W;
            for (int q = 0; q < md.Rep; q++)
            {
                int row = row0 * md.Rep + q;
                if (row >= md.H) break;
                for (int x = 0; x < w; x++)
                {
                    int r, g, bl;
                    if (ycc)
                    {
                        int y = a[x], cr = b[x], cb = c[x];
                        r = Clip((100 * y + 140 * cr - 17850) / 100);
                        g = Clip((100 * y - 71 * cr - 33 * cb + 13260) / 100);
                        bl = Clip((100 * y + 178 * cb - 22780) / 100);
                    }
                    else { r = a[x]; g = b[x]; bl = c[x]; }
                    px[row * w + x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | bl;
                }
            }
        }

        static int[] Wide(int[] half, int w) { int[] o = new int[w]; for (int i = 0; i < w; i++) o[i] = half[Math.Min(half.Length - 1, i / 2)]; return o; }

        // L'image remonte de n blocs (les lignes du haut sortent, le bas redevient gris pour les lignes suivantes).
        void ScrollUp(int n)
        {
            int rows = n * (md.Kind == PD ? 2 : md.Rep);
            int w = md.W, h = md.H;
            if (rows <= 0 || rows >= h) return;
            Array.Copy(px, rows * w, px, 0, (h - rows) * w);
            for (int i = (h - rows) * w; i < h * w; i++) px[i] = unchecked((int)0xFF202020);
            if (shift == 0) Say(Lang.F("SSTV : {0} — l'émission continue après le bas de l'image : l'image remonte", "SSTV: {0} — the transmission goes on past the bottom of the image: the image scrolls up", md.Name));
            shift += n;
            rowsDone = h;   // l'image est pleine (Robot 36 pris en cours de route : la dernière ligne paire peut manquer)
            if (pendY != null) pendRow -= n;
            Action h2 = ImageScrolled;
            if (h2 != null) h2();
        }

        void DecodeBlock(long s)
        {
            int w = md.W;
            int blockIdx = this.blockIdx - shift;   // rang du bloc dans l'image (l'image a pu remonter)
            switch (md.Kind)
            {
                case RGB:
                {
                    int[][] c = new int[3][];
                    for (int i = 0; i < 3; i++) c[md.Role[i]] = Pixels(s, md.St[i], md.Len[i], w);
                    Put(blockIdx, c[R], c[G], c[B], false);
                    rowsDone = Math.Max(rowsDone, (blockIdx + 1) * md.Rep);
                    break;
                }
                case GRAY:
                {
                    int[] g = Pixels(s, md.St[0], md.Len[0], w);
                    Put(blockIdx, g, g, g, false);
                    rowsDone = Math.Max(rowsDone, (blockIdx + 1) * md.Rep);
                    break;
                }
                case R72:
                {
                    int[] y = Pixels(s, md.St[0], md.Len[0], w), cr = Pixels(s, md.St[1], md.Len[1], w), cb = Pixels(s, md.St[2], md.Len[2], w);
                    Put(blockIdx, y, cr, cb, true);
                    rowsDone = Math.Max(rowsDone, blockIdx + 1);
                    break;
                }
                case R36:
                {
                    int[] y = Pixels(s, 12, 88, w);
                    double sep = FreqAvg(s + 100.5 * SPMS, s + 103.5 * SPMS) - ofs;
                    int[] ch = Wide(Pixels(s, 106, 44, w / 2), w);
                    if (sep < 1900) { pendY = y; pendCr = ch; pendRow = blockIdx; }   // ligne paire : R-Y
                    else if (pendY != null && pendRow == blockIdx - 1)             // ligne impaire : B-Y, on écrit la paire
                    {
                        Put(pendRow, pendY, pendCr, ch, true);
                        Put(blockIdx, y, pendCr, ch, true);
                        rowsDone = Math.Max(rowsDone, blockIdx + 1);
                        pendY = null;
                    }
                    break;
                }
                case PD:
                {
                    int[] y0 = Pixels(s, md.St[0], md.Len[0], w), cr = Pixels(s, md.St[1], md.Len[1], w), cb = Pixels(s, md.St[2], md.Len[2], w), y1 = Pixels(s, md.St[3], md.Len[3], w);
                    Put(2 * blockIdx, y0, cr, cb, true);
                    Put(2 * blockIdx + 1, y1, cr, cb, true);
                    rowsDone = Math.Max(rowsDone, 2 * blockIdx + 2);
                    break;
                }
            }
            Action<int> h = ImageRows;
            if (h != null) h(rowsDone);
        }

        void Finish(bool complete)
        {
            if (img)
            {
                Action<bool, int> h = ImageEnd;
                if (h != null) h(complete, rowsDone);
            }
            img = false;
            px = null;
            VisReset();
            np = 0; pRun = 0;   // les impulsions de cette image ne doivent pas déclencher une nouvelle réception
        }

        void Say(string s)
        {
            Action<string> h = Status;
            if (h != null) h(s);
        }
    }

    // Fenêtre de l'image SSTV, posée sur la page WebSDR (en bas à gauche au départ). Appui long (0,4 s) puis glisser : déplacement
    // (position retenue en proportion de la place libre, donc gardée quand la fenêtre change de taille).
    public class SstvView : Control
    {
        public event Action Moved;
        Bitmap bmp;
        int[] px;
        int imgW = 320, imgH = 256, shown;
        string caption = "SSTV";
        public double PosX = 0, PosY = 1;   // 0 = gauche / haut, 1 = droite / bas
        public double CustW, CustH;         // taille choisie en tirant sur les bords, en fraction de la page (0 = taille d'origine)
        readonly System.Windows.Forms.Timer hold = new System.Windows.Forms.Timer { Interval = 400 };
        bool dragging, pressed;
        Point grab;
        const int EDGE = 6;                 // largeur de la zone des bords où l'on tire pour agrandir
        int sizing;                         // bords tirés : 1 gauche, 2 droite, 4 haut, 8 bas (0 = pas de redimensionnement)
        Rectangle sizeStart;
        Point sizeGrab;

        // Genre d'affichage : image SSTV, texte décodé (FT8, CW…), image Hell (large et basse, redessinée en entier quand elle défile),
        // image WEFAX (très grande : on montre le bas, les dernières lignes reçues).
        public const int KindSstv = 0, KindText = 1, KindHell = 2, KindFax = 3;
        int kind;
        string placeholder = "SSTV";
        List<DecLine> lines = new List<DecLine>();
        string[] hiPats = new string[0];
        Font mono;
        float monoW;

        public int Kind { get { return kind; } }

        // Changement de mode : plus d'image, plus de texte.
        public void SetKind(int k, string name)
        {
            kind = k;
            placeholder = name;
            lines = new List<DecLine>();
            ClearImage();
        }

        // Texte décodé (copie des dernières lignes) et suites de caractères à mettre en surbrillance (séparées par des virgules).
        public void SetLines(List<DecLine> l, string hi)
        {
            lines = l;
            List<string> p = new List<string>();
            foreach (string s in (hi ?? "").Split(',', ';', '\n')) { string t = s.Trim().ToLowerInvariant(); if (t.Length > 0) p.Add(t); }
            hiPats = p.ToArray();
            Invalidate();
        }

        // Hell : toute l'image a changé (elle défile).
        public void RedrawAll()
        {
            if (bmp == null || px == null) return;
            shown = 0;
            Rows(imgH);
        }

        Font Mono
        {
            get
            {
                if (mono == null)
                {
                    mono = new Font("Consolas", Math.Max(7f, Font.SizeInPoints));
                    monoW = TextRenderer.MeasureText("MMMMMMMMMMMMMMMMMMMM", mono, Size.Empty, TextFormatFlags.NoPadding).Width / 20f;
                }
                return mono;
            }
        }

        public SstvView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(24, 24, 24);
            ForeColor = Color.White;
            hold.Tick += delegate
            {
                hold.Stop();
                if (!pressed) return;
                dragging = true;
                Cursor = Cursors.SizeAll;
                Invalidate();
            };
        }

        public string Caption { get { return caption; } set { if (caption == value) return; caption = value; Invalidate(); } }
        public bool HasImage { get { return bmp != null; } }

        // Plus d'image affichée (remise en marche de SSTV).
        public void ClearImage()
        {
            if (bmp != null) bmp.Dispose();
            bmp = null; px = null; shown = 0; imgW = 320; imgH = 256;
            Place();
            Invalidate();
        }

        public void Begin(int w, int h, int[] pixels)
        {
            if (bmp != null) bmp.Dispose();
            bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.FromArgb(32, 32, 32));
            px = pixels; imgW = w; imgH = h; shown = 0;
            Place();
            Invalidate();
        }

        // Recopie des lignes terminées dans l'image affichée.
        public void Rows(int done)
        {
            if (bmp == null || px == null) return;
            done = Math.Min(done, imgH);
            if (done <= shown) return;
            BitmapData d = bmp.LockBits(new Rectangle(0, shown, imgW, done - shown), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int r = shown; r < done; r++)
                    System.Runtime.InteropServices.Marshal.Copy(px, r * imgW, d.Scan0 + (r - shown) * d.Stride, imgW);
            }
            finally { bmp.UnlockBits(d); }
            shown = done;
            Invalidate();
        }

        // Fin d'image : copie de la partie reçue (pour l'enregistrement), ou null si moins de 16 lignes.
        public Bitmap Received(int rows)
        {
            Rows(rows);
            px = null;
            if (bmp == null || shown < 16) return null;
            return bmp.Clone(new Rectangle(0, 0, imgW, shown), PixelFormat.Format32bppArgb);
        }

        // Copie de toute l'image affichée (Hell), ou null.
        public Bitmap Snapshot()
        {
            if (bmp == null) return null;
            RedrawAll();
            return bmp.Clone(new Rectangle(0, 0, imgW, imgH), PixelFormat.Format32bppArgb);
        }

        int CaptionH { get { return Font.Height + 4; } }

        // Taille et position dans la page : l'image tient dans 42 % de la largeur et de la hauteur de la page.
        public void Place()
        {
            Control p = Parent;
            if (p == null || p.ClientSize.Width < 50 || p.ClientSize.Height < 50) return;
            int ch = CaptionH;
            int pw = p.ClientSize.Width, ph = p.ClientSize.Height;
            int w, h;
            if (CustW > 0 && CustH > 0 && Locked)
            {   // image à proportions fixes : la largeur choisie donne la hauteur (l'image est agrandie d'autant)
                double ar = Ratio;
                w = Math.Max(MinW, (int)Math.Round(CustW * pw));
                if (w > pw) w = pw;
                int ih = (int)(w * ar);
                if (ih + ch > ph) { ih = ph - ch; w = (int)(ih / ar); }
                h = ih + ch;
            }
            else if (CustW > 0 && CustH > 0)
            {
                w = Math.Max(MinW, (int)Math.Round(CustW * pw));
                h = Math.Max(MinH, (int)Math.Round(CustH * ph));
            }
            else if (kind == KindText)
            {
                Font f = Mono;
                w = (int)Math.Min(pw * 0.62, 74 * monoW + 12);
                h = Math.Max(8 * f.Height + ch, (int)(ph * 0.68));
            }
            else
            {
                int vh = kind == KindFax ? imgW * 3 / 4 : imgH;
                double fw = kind == KindHell ? 0.92 : 0.42, fh = kind == KindHell ? 0.40 : 0.42;
                double k = Math.Min(pw * fw / imgW, (ph * fh - ch) / vh);
                if (k <= 0) k = 0.2;
                w = Math.Max(120, (int)(imgW * k)); h = Math.Max(60, (int)(vh * k));
                if (kind != KindSstv) h *= 2;   // Hell et WEFAX : deux fois plus haut que l'image seule
                h += ch;
            }
            w = Math.Min(w, p.ClientSize.Width); h = Math.Min(h, p.ClientSize.Height);
            Size = new Size(w, h);
            int fx = p.ClientSize.Width - w, fy = p.ClientSize.Height - h;
            Location = new Point((int)Math.Round(Math.Max(0, Math.Min(1, PosX)) * fx), (int)Math.Round(Math.Max(0, Math.Min(1, PosY)) * fy));
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent != null) Parent.Resize += delegate { Place(); };
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (mono != null) { mono.Dispose(); mono = null; }
            Place();
        }

        static readonly Color ColA = Color.FromArgb(120, 200, 255), ColB = Color.FromArgb(255, 205, 120), HiBack = Color.FromArgb(0, 230, 118);

        // Largeur dessinée d'un début de ligne (espaces finaux compris : on mesure avec un caractère de plus).
        static int Wid(Graphics g, string s, Font f, TextFormatFlags tf)
        {
            if (s.Length == 0) return 0;
            Size big = new Size(int.MaxValue, int.MaxValue);
            return TextRenderer.MeasureText(g, s + "x", f, big, tf).Width - TextRenderer.MeasureText(g, "x", f, big, tf).Width;
        }

        // Texte : les dernières lignes, la plus récente en bas ; les deux périodes FT8 en deux couleurs ; surbrillance en vert.
        void PaintText(Graphics g, Rectangle area)
        {
            Font f = Mono;
            int lh = f.Height;
            int y = area.Bottom - 4 - lh;
            TextFormatFlags tf = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            List<DecLine> l = lines;
            if (l.Count == 0)
            {
                using (Font big = new Font(Font.FontFamily, Math.Max(8f, Math.Min(area.Height / 5f, 28f)), FontStyle.Bold))
                    TextRenderer.DrawText(g, placeholder, big, area, Color.FromArgb(70, 70, 70), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            g.SetClip(area);
            for (int i = l.Count - 1; i >= 0 && y + lh > area.Top; i--, y -= lh)
            {
                string t = l[i].Text;
                Color c = l[i].Col == 1 ? ColA : l[i].Col == 2 ? ColB : ForeColor;
                TextRenderer.DrawText(g, t, f, new Point(6, y), c, tf);
                if (hiPats.Length == 0) continue;
                string low = t.ToLowerInvariant();
                if (low.Length != t.Length) continue;
                foreach (string p in hiPats)
                    for (int k = low.IndexOf(p, StringComparison.Ordinal); k >= 0; k = low.IndexOf(p, k + p.Length, StringComparison.Ordinal))
                    {
                        int x0 = Wid(g, t.Substring(0, k), f, tf), x1 = Wid(g, t.Substring(0, k + p.Length), f, tf);   // mesuré comme dessiné
                        Rectangle r = new Rectangle(6 + x0, y, x1 - x0, lh);
                        using (SolidBrush b = new SolidBrush(HiBack)) g.FillRectangle(b, r);
                        TextRenderer.DrawText(g, t.Substring(k, p.Length), f, r.Location, Color.Black, tf);
                    }
            }
            g.ResetClip();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int ch = CaptionH;
            Rectangle area = new Rectangle(0, ch, Width, Height - ch);
            if (kind == KindText) PaintText(g, area);
            else if (bmp != null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                if (kind == KindFax)
                {   // le bas de l'image reçue, à l'échelle de la largeur de la fenêtre (une fenêtre plus haute montre plus de lignes)
                    int vh = Math.Max(1, (int)((long)Math.Max(1, area.Height) * imgW / Math.Max(1, area.Width)));
                    int top = Math.Max(0, Math.Min(shown, imgH) - vh), rows = Math.Min(vh, imgH - top);
                    if (rows > 0)
                        g.DrawImage(bmp, new Rectangle(area.X, area.Y, area.Width, (int)((long)area.Height * rows / vh)), new Rectangle(0, top, imgW, rows), GraphicsUnit.Pixel);
                }
                else if (kind == KindSstv)
                {   // proportions de l'image gardées, centrée dans la fenêtre
                    double k = Math.Min(area.Width / (double)imgW, area.Height / (double)imgH);
                    int dw = Math.Max(1, (int)(imgW * k)), dh = Math.Max(1, (int)(imgH * k));
                    g.DrawImage(bmp, new Rectangle(area.X + (area.Width - dw) / 2, area.Y + (area.Height - dh) / 2, dw, dh));
                }
                else g.DrawImage(bmp, area);
            }
            else
                using (Font big = new Font(Font.FontFamily, Math.Max(8f, area.Height / 6f), FontStyle.Bold))
                    TextRenderer.DrawText(g, placeholder, big, area, Color.FromArgb(70, 70, 70), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, caption, Font, new Rectangle(4, 2, Width - 8, ch - 2), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (Pen p = new Pen(dragging ? Color.FromArgb(255, 140, 0) : Color.FromArgb(90, 90, 90), dragging ? 3 : 1))
                g.DrawRectangle(p, dragging ? 1 : 0, dragging ? 1 : 0, Width - (dragging ? 3 : 1), Height - (dragging ? 3 : 1));
        }

        // SSTV et Hell : proportions de la fenêtre fixes (hauteur de l'image / largeur ; Hell : image affichée deux fois plus haute).
        bool Locked { get { return kind == KindSstv || kind == KindHell; } }
        double Ratio { get { return (kind == KindHell ? 2.0 * imgH : imgH) / (double)Math.Max(1, imgW); } }

        int MinW { get { return 120; } }
        int MinH { get { return CaptionH + 40; } }

        // Bords sous la souris (comme une fenêtre Windows : bords et coins).
        int HitEdges(Point p)
        {
            int k = 0;
            if (p.X < EDGE) k |= 1; else if (p.X >= Width - EDGE) k |= 2;
            if (p.Y < EDGE) k |= 4; else if (p.Y >= Height - EDGE) k |= 8;
            return k;
        }

        static Cursor EdgeCursor(int k)
        {
            switch (k)
            {
                case 1: case 2: return Cursors.SizeWE;
                case 4: case 8: return Cursors.SizeNS;
                case 5: case 10: return Cursors.SizeNWSE;
                case 6: case 9: return Cursors.SizeNESW;
                default: return Cursors.Default;
            }
        }

        // Taille et position actuelles retenues (fractions de la page), pour Place() et l'enregistrement.
        void KeepBounds()
        {
            if (Parent == null) return;
            Size ps = Parent.ClientSize;
            if (ps.Width <= 0 || ps.Height <= 0) return;
            CustW = Width / (double)ps.Width; CustH = Height / (double)ps.Height;
            PosX = ps.Width > Width ? Left / (double)(ps.Width - Width) : 0;
            PosY = ps.Height > Height ? Top / (double)(ps.Height - Height) : 1;
        }

        void EndSizing()
        {
            sizing = 0;
            Cursor = Cursors.Default;
            KeepBounds();
            Invalidate();
            Action h = Moved;
            if (h != null) h();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int k = HitEdges(e.Location);
            if (k != 0 && Parent != null)
            {   // bord ou coin : redimensionnement tout de suite
                sizing = k;
                sizeStart = Bounds;
                sizeGrab = Parent.PointToClient(PointToScreen(e.Location));
                Cursor = EdgeCursor(k);
                return;
            }
            pressed = true;
            grab = e.Location;
            hold.Stop();
            hold.Start();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (sizing != 0 && Parent != null)
            {
                Size pz = Parent.ClientSize;
                Point m = Parent.PointToClient(PointToScreen(e.Location));
                int dx = m.X - sizeGrab.X, dy = m.Y - sizeGrab.Y;
                int l = sizeStart.Left, t = sizeStart.Top, r = sizeStart.Right, b = sizeStart.Bottom;
                if ((sizing & 1) != 0) l = Math.Max(0, Math.Min(r - MinW, l + dx));
                if ((sizing & 2) != 0) r = Math.Min(pz.Width, Math.Max(l + MinW, r + dx));
                if ((sizing & 4) != 0) t = Math.Max(0, Math.Min(b - MinH, t + dy));
                if ((sizing & 8) != 0) b = Math.Min(pz.Height, Math.Max(t + MinH, b + dy));
                if (Locked)
                {   // proportions gardées : le bord tiré donne la taille, le côté ou le coin opposé reste en place
                    double ar = Ratio;
                    int ch = CaptionH;
                    bool horiz = (sizing & 3) != 0, vert = (sizing & 12) != 0;
                    double nw = r - l, fromH = Math.Max(1, sizeStart.Height + ((sizing & 4) != 0 ? -dy : dy) - ch) / ar;
                    if (horiz && vert) nw = Math.Max(nw, fromH);
                    else if (vert) nw = fromH;
                    bool bottomFixed = (sizing & 4) != 0 || (!vert && sizeStart.Bottom >= pz.Height - 1);
                    double maxW = (sizing & 1) != 0 ? sizeStart.Right : pz.Width - sizeStart.Left;
                    double maxH = bottomFixed ? sizeStart.Bottom : (sizing & 8) != 0 ? pz.Height - sizeStart.Top : pz.Height;
                    maxW = Math.Min(maxW, (maxH - ch) / ar);
                    int sw = (int)Math.Max(MinW, Math.Min(maxW, nw));
                    int sh = (int)(sw * ar) + ch;
                    int sx = (sizing & 1) != 0 ? sizeStart.Right - sw : sizeStart.Left;
                    int sy = bottomFixed ? sizeStart.Bottom - sh : sizeStart.Top;
                    sy = Math.Max(0, Math.Min(pz.Height - sh, sy));
                    sx = Math.Max(0, Math.Min(pz.Width - sw, sx));
                    Bounds = new Rectangle(sx, sy, sw, sh);
                    return;
                }
                Bounds = new Rectangle(l, t, r - l, b - t);
                return;
            }
            if (!dragging && !pressed) Cursor = EdgeCursor(HitEdges(e.Location));
            if (!dragging || Parent == null) return;
            Size ps = Parent.ClientSize;
            int x = Math.Max(0, Math.Min(ps.Width - Width, Left + e.X - grab.X));
            int y = Math.Max(0, Math.Min(ps.Height - Height, Top + e.Y - grab.Y));
            Location = new Point(x, y);
            PosX = ps.Width > Width ? x / (double)(ps.Width - Width) : 0;
            PosY = ps.Height > Height ? y / (double)(ps.Height - Height) : 1;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (sizing != 0) { EndSizing(); return; }
            pressed = false;
            hold.Stop();
            if (!dragging) return;
            dragging = false;
            Cursor = Cursors.Default;
            Invalidate();
            Action h = Moved;
            if (h != null) h();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (sizing == 0 && !dragging) Cursor = Cursors.Default;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;
            if (sizing != 0) { EndSizing(); return; }
            pressed = false;
            hold.Stop();
            if (dragging) { dragging = false; Cursor = Cursors.Default; Invalidate(); Action h = Moved; if (h != null) h(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { hold.Dispose(); if (bmp != null) bmp.Dispose(); if (mono != null) mono.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // Une ligne de texte décodé : Col 0 normal, 1 et 2 = périodes paire et impaire (FT8, WSPR…).
    public class DecLine
    {
        public string Text;
        public readonly int Col;
        public DecLine(string t, int c) { Text = t; Col = c; }
    }
}
