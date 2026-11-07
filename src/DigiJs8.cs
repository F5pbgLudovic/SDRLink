using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace SDRLink
{
    // Décodeur JS8Call (réception seule, repris de PhoneSDRLink), cinq vitesses : Lent (30 s), Normal (15 s), Rapide (10 s), Turbo (6 s), Ultra (4 s).
    // Couche physique : FSK à 8 tonalités (79 symboles : trois suites de Costas de 7 symboles aux positions 0, 36 et 72, 58 symboles de données),
    // code LDPC (174,87) (propagation de croyance), CRC de 12 bits, message de 72 bits (12 caractères de 6 bits) + 3 bits de type de trame.
    // Les matrices et le CRC sont ceux de JS8Call (GPL v3). Le texte des trames est interprété par Js8Text.
    public sealed class Js8Engine : SlotDecoder
    {
        // vitesses : 0 Lent (JS8E), 1 Normal (JS8A), 2 Rapide (JS8B), 3 Turbo (JS8C), 4 Ultra (JS8I)
        public static readonly int[] NSPS = { 3840, 1920, 1200, 600, 384 };
        public static readonly int[] PERIOD = { 30000, 15000, 10000, 6000, 4000 };
        public static readonly int[] DELAY = { 500, 500, 200, 100, 100 };
        public static readonly int[] NDS = { 32, 32, 20, 12, 12 };            // échantillons par symbole en bande de base
        public static readonly bool[] ORIGINAL = { false, true, false, false, false };
        public static readonly string[] NAMES = { "JS8 Lent", "JS8 Normal", "JS8 Rapide", "JS8 Turbo", "JS8 Ultra" };
        public const int PRE_MS = 1000;
        static readonly int[][] COSTAS_ORIG = { new int[] { 4, 2, 5, 6, 1, 3, 0 }, new int[] { 4, 2, 5, 6, 1, 3, 0 }, new int[] { 4, 2, 5, 6, 1, 3, 0 } };
        static readonly int[][] COSTAS_MOD = { new int[] { 0, 6, 2, 3, 5, 4, 1 }, new int[] { 1, 5, 0, 2, 3, 6, 4 }, new int[] { 2, 5, 0, 6, 4, 1, 3 } };

        public static double SYNC_MIN = 1.35;     // seuil du score de synchro grossière (1 = bruit)
        public static int MAX_CAND = 70;
        public static int MIN_SYNC = 6;           // suites de Costas retrouvées (sur 21) pour accepter un décodage
        public static int PASSES = 2;

        // matrice de contrôle : voisins (indices de bits 0 à 173) de chacun des 87 contrôles
        static readonly int[][] NM = {
            new int[] {0,29,59,88,117,146}, new int[] {1,30,60,89,118,146}, new int[] {2,31,61,90,119,147}, new int[] {3,32,62,91,120,148}, new int[] {1,33,63,92,121,149}, new int[] {4,32,64,93,122,147},
            new int[] {5,33,65,94,123,150}, new int[] {6,34,66,95,119,151}, new int[] {7,35,67,96,124,152}, new int[] {8,36,68,97,125,151}, new int[] {9,37,69,98,126,153}, new int[] {10,38,70,99,125,154},
            new int[] {11,39,60,100,127,144}, new int[] {9,32,59,94,127,155}, new int[] {12,40,71,96,125,156}, new int[] {12,41,72,89,128,155}, new int[] {13,38,73,98,129,157}, new int[] {14,42,74,101,130,158},
            new int[] {15,42,70,102,117,159}, new int[] {16,43,75,97,129,155}, new int[] {17,44,59,95,131,160}, new int[] {18,45,72,82,132,161}, new int[] {11,37,76,101,133,162}, new int[] {18,46,77,103,134,146},
            new int[] {0,31,76,104,135,163}, new int[] {19,47,72,105,122,162}, new int[] {20,40,78,106,136,164}, new int[] {21,41,65,107,137,151}, new int[] {17,41,79,108,138,153}, new int[] {22,48,80,109,134,165},
            new int[] {15,49,81,90,128,157}, new int[] {2,47,62,106,123,166}, new int[] {5,50,66,110,133,154}, new int[] {23,34,76,99,121,161}, new int[] {19,44,75,111,139,156}, new int[] {20,35,63,91,129,158},
            new int[] {7,51,82,110,117,165}, new int[] {20,52,83,112,137,167}, new int[] {24,50,78,88,121,157}, new int[] {21,43,74,106,132,154,171}, new int[] {8,53,83,89,140,168}, new int[] {21,53,84,109,135,160},
            new int[] {7,36,64,101,128,169}, new int[] {18,38,84,113,138,149}, new int[] {25,54,70,92,141,166}, new int[] {26,55,64,95,132,159,173}, new int[] {27,30,85,99,116,170}, new int[] {27,51,69,103,131,143},
            new int[] {23,56,67,94,136,141}, new int[] {6,29,71,109,142,150}, new int[] {3,50,75,114,126,167}, new int[] {15,44,86,113,124,171}, new int[] {14,29,85,114,122,149}, new int[] {22,45,63,90,143,172},
            new int[] {22,34,74,112,144,152}, new int[] {13,40,86,107,116,148,169}, new int[] {24,39,84,93,123,158}, new int[] {24,57,68,115,142,173}, new int[] {28,42,60,115,131,161}, new int[] {14,57,87,111,120,163},
            new int[] {3,58,71,113,118,162,172}, new int[] {26,46,85,97,133,152}, new int[] {4,43,77,108,140}, new int[] {9,45,68,102,135,164}, new int[] {8,49,58,92,127,163}, new int[] {13,56,57,108,119,165},
            new int[] {16,54,61,115,124,153}, new int[] {2,53,69,100,139,169}, new int[] {0,35,81,107,126,173}, new int[] {4,52,80,104,139}, new int[] {28,52,66,98,141,172}, new int[] {17,48,73,96,114,166},
            new int[] {1,56,62,102,137,156}, new int[] {25,37,78,111,134,170}, new int[] {10,51,65,87,118,147}, new int[] {19,39,67,116,140,159}, new int[] {10,47,80,88,145,168}, new int[] {28,46,79,91,145,171},
            new int[] {5,31,86,103,144,168}, new int[] {26,33,73,105,130,164}, new int[] {11,55,83,87,138}, new int[] {12,55,61,110,145,170}, new int[] {25,36,79,104,143,150}, new int[] {16,30,81,112,120,160},
            new int[] {27,48,58,93,136}, new int[] {6,54,82,100,130,167}, new int[] {23,49,77,105,142,148}
        };
        // matrice génératrice (87 lignes de 87 bits, en hexadécimal) : seulement pour les essais (émission de signaux de test)
        static readonly string[] GEN = {
            "23bba830e23b6b6f50982e", "1f8e55da218c5df3309052", "ca7b3217cd92bd59a5ae20", "56f78313537d0f4382964e",
            "6be396b5e2e819e373340c", "293548a138858328af4210", "cb6c6afcdc28bb3f7c6e86", "3f2a86f5c5bd225c961150",
            "849dd2d63673481860f62c", "56cdaec6e7ae14b43feeee", "04ef5cfa3766ba778f45a4", "c525ae4bd4f627320a3974",
            "41fd9520b2e4abeb2f989c", "7fb36c24085a34d8c1dbc4", "40fc3e44bb7d2bb2756e44", "d38ab0a1d2e52a8ec3bc76",
            "3d0f929ef3949bd84d4734", "45d3814f504064f80549ae", "f14dbf263825d0bd04b05e", "db714f8f64e8ac7af1a76e",
            "8d0274de71e7c1a8055eb0", "51f81573dd4049b082de14", "d8f937f31822e57c562370", "b6537f417e61d1a7085336",
            "ecbd7c73b9cd34c3720c8a", "3d188ea477f6fa41317a4e", "1ac4672b549cd6dba79bcc", "a377253773ea678367c3f6",
            "0dbd816fba1543f721dc72", "ca4186dd44c3121565cf5c", "29c29dba9c545e267762fe", "1616d78018d0b4745ca0f2",
            "fe37802941d66dde02b99c", "a9fa8e50bcb032c85e3304", "83f640f1a48a8ebc0443ea", "3776af54ccfbae916afde6",
            "a8fc906976c35669e79ce0", "f08a91fb2e1f78290619a8", "cc9da55fe046d0cb3a770c", "d36d662a69ae24b74dcbd8",
            "40907b01280f03c0323946", "d037db825175d851f3af00", "1bf1490607c54032660ede", "0af7723161ec223080be86",
            "eca9afa0f6b01d92305edc", "7a8dec79a51e8ac5388022", "9059dfa2bb20ef7ef73ad4", "6abb212d9739dfc02580f2",
            "f6ad4824b87c80ebfce466", "d747bfc5fd65ef70fbd9bc", "612f63acc025b6ab476f7c", "05209a0abb530b9e7e34b0",
            "45b7ab6242b77474d9f11a", "6c280d2a0523d9c4bc5946", "f1627701a2d692fd9449e6", "8d9071b7e7a6a2eed6965e",
            "bf4f56e073271f6ab4bf80", "c0fc3ec4fb7d2bb2756644", "57da6d13cb96a7689b2790", "a9fa2eefa6f8796a355772",
            "164cc861bdd803c547f2ac", "cc6de59755420925f90ed2", "a0c0033a52ab6299802fd2", "b274db8abd3c6f396ea356",
            "97d4169cb33e7435718d90", "81cfc6f18c35b1e1f17114", "481a2a0df8a23583f82d6c", "081c29a10d468ccdbcecb6",
            "2c4142bf42b01e71076acc", "a6573f3dc8b16c9d19f746", "c87af9a5d5206abca532a8", "012dee2198eba82b19a1da",
            "b1ca4ea2e3d173bad4379c", "b33ec97be83ce413f9acc8", "5b0f7742bca86b8012609a", "37d8e0af9258b9e8c5f9b2",
            "35ad3fb0faeb5f1b0c30dc", "6114e08483043fd3f38a8a", "cd921fdf59e882683763f6", "95e45ecd0135aca9d6e6ae",
            "2e547dd7a05f6597aac516", "14cd0f642fc0c5fe3a65ca", "3a0a1dfd7eee29c2e827e0", "c8b5dffc335095dcdcaf2a",
            "3dd01a59d86310743ec752", "8abdb889efbe39a510a118", "3f231f212055371cf3e2a2",
        };

        const int NCHK = 87;
        static readonly int[] EDGE_START = new int[NCHK + 1];
        static readonly int[] EDGE_BIT;
        static readonly int[] BIT_EDGE_START = new int[175];
        static readonly int[] BIT_EDGES;     // pour chaque bit, indices des arêtes

        static Js8Engine()
        {
            int ne = 0;
            for (int i = 0; i < NCHK; i++) { EDGE_START[i] = ne; ne += NM[i].Length; }
            EDGE_START[NCHK] = ne;
            EDGE_BIT = new int[ne];
            int[] cnt = new int[174];
            for (int i = 0; i < NCHK; i++) for (int j = 0; j < NM[i].Length; j++) { EDGE_BIT[EDGE_START[i] + j] = NM[i][j]; cnt[NM[i][j]]++; }
            int s = 0;
            for (int b = 0; b < 174; b++) { BIT_EDGE_START[b] = s; s += cnt[b]; }
            BIT_EDGE_START[174] = s;
            BIT_EDGES = new int[s];
            int[] fill = new int[174];
            for (int e = 0; e < ne; e++) { int b = EDGE_BIT[e]; BIT_EDGES[BIT_EDGE_START[b] + fill[b]++] = e; }
        }

        public sealed class Msg
        {
            public string Chars; public int I3, Snr, NSync, S0, Di; public int[] Tone; public double Dt, Freq;
        }

        public readonly int Speed;

        public Js8Engine(int speed)
            : base(NAMES[speed], PERIOD[speed], PRE_MS, DELAY[speed] + (int)(79000L * NSPS[speed] / 12000) + 1500)
        {
            Speed = speed;
        }

        protected override string TimeText(DateTime c) { return Two(c.Hour) + Two(c.Minute) + Two(c.Second); }

        public override Result Decode(short[] win, int n, long slotStartMs, string t)
        {
            Result r = new Result();
            foreach (Msg m in DecodeWindow(win, n, Speed, PRE_MS + DELAY[Speed]))
            {
                string tx = null;
                try { tx = Js8Text.FrameText(m.Chars, m.I3, Speed >= 3); } catch { }
                if (tx == null) tx = "?" + m.Chars;
                r.Lines.Add(FormatLine(t, m.Snr, m.Dt, m.Freq, tx));
                r.Dts.Add(m.Dt);
            }
            return r;
        }

        // ------------------------------------------------------------------ FFT à base mixte (facteurs 2, 3 et 5)
        sealed class MFft
        {
            readonly int n;
            readonly double[] cr, ci;
            readonly double[] tr = new double[8], ti = new double[8];
            public MFft(int n)
            {
                this.n = n;
                cr = new double[n];
                ci = new double[n];
                for (int i = 0; i < n; i++) { double a = -2 * Math.PI * i / n; cr[i] = Math.Cos(a); ci[i] = Math.Sin(a); }
            }
            public static bool Ok(int n)
            {
                foreach (int p in new int[] { 2, 3, 5 }) while (n % p == 0) n /= p;
                return n == 1;
            }
            /** y = TFD(x) (x, y distincts). */
            public void Run(double[] xr, double[] xi, double[] yr, double[] yi) { Rec(xr, xi, 0, 1, n, yr, yi, 0); }
            void Rec(double[] xr, double[] xi, int xo, int xs, int m, double[] yr, double[] yi, int yo)
            {
                if (m == 1) { yr[yo] = xr[xo]; yi[yo] = xi[xo]; return; }
                int p = m % 2 == 0 ? 2 : m % 3 == 0 ? 3 : 5;
                int q = m / p, gstep = n / m;
                for (int r = 0; r < p; r++) Rec(xr, xi, xo + r * xs, xs * p, q, yr, yi, yo + r * q);
                if (p == 2)
                {
                    for (int k = 0; k < q; k++)
                    {
                        int a = yo + k, b = a + q, w = k * gstep;
                        double br = yr[b] * cr[w] - yi[b] * ci[w], bi = yr[b] * ci[w] + yi[b] * cr[w];
                        double ar = yr[a], ai = yi[a];
                        yr[a] = ar + br; yi[a] = ai + bi; yr[b] = ar - br; yi[b] = ai - bi;
                    }
                    return;
                }
                int pstep = n / p;
                for (int k = 0; k < q; k++)
                {
                    for (int r = 0; r < p; r++)
                    {
                        int idx = yo + r * q + k, w = r * k * gstep;
                        tr[r] = yr[idx] * cr[w] - yi[idx] * ci[w];
                        ti[r] = yr[idx] * ci[w] + yi[idx] * cr[w];
                    }
                    for (int o = 0; o < p; o++)
                    {
                        double sr = 0, si = 0;
                        for (int r = 0; r < p; r++)
                        {
                            int w = ((r * o) % p) * pstep;
                            sr += tr[r] * cr[w] - ti[r] * ci[w];
                            si += tr[r] * ci[w] + ti[r] * cr[w];
                        }
                        yr[yo + o * q + k] = sr; yi[yo + o * q + k] = si;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ CRC et mots de code
        static int Crc12(int[] bits88)
        {   // 88 bits (un bit par case) : reste modulo le polynôme x^12 + 0xC06, valeur finale xor 42
            int rem = 0;
            for (int i = 0; i < 88; i++)
            {
                int top = (rem >> 11) & 1;
                rem = ((rem << 1) | bits88[i]) & 0xfff;
                if (top != 0) rem ^= 0xc06;
            }
            return rem ^ 42;
        }

        /** 87 bits décodés : CRC valide ? */
        static bool CrcOk(int[] d)
        {
            int[] b = new int[88];
            for (int i = 0; i < 75; i++) b[i] = d[i];
            int crc = 0;
            for (int i = 75; i < 87; i++) crc = (crc << 1) | d[i];
            return Crc12(b) == crc;
        }

        /** Propagation de croyance. llr > 0 : le bit vaut plutôt 1. Renvoie le nombre de contrôles non satisfaits à la fin (0 : mot de code valide). */
        static int Bp(double[] llr, int[] cw, int maxIter)
        {
            int ne = EDGE_START[NCHK];
            double[] L = new double[174];
            for (int i = 0; i < 174; i++) L[i] = -Math.Max(-25, Math.Min(25, llr[i]));   // convention : L > 0 favorise le bit 0
            double[] tov = new double[ne];       // contrôle -> bit, par arête
            double[] toc = new double[ne];       // bit -> contrôle
            double[] zn = new double[174];
            int nclast = 0, ncnt = 0, nc = NCHK;
            for (int it = 0; it <= maxIter; it++)
            {
                for (int b = 0; b < 174; b++)
                {
                    double s = L[b];
                    for (int k = BIT_EDGE_START[b]; k < BIT_EDGE_START[b + 1]; k++) s += tov[BIT_EDGES[k]];
                    zn[b] = s;
                    cw[b] = s < 0 ? 1 : 0;
                }
                nc = 0;
                for (int c = 0; c < NCHK; c++)
                {
                    int p = 0;
                    for (int e = EDGE_START[c]; e < EDGE_START[c + 1]; e++) p ^= cw[EDGE_BIT[e]];
                    nc += p;
                }
                if (nc == 0) return 0;
                if (it > 0)
                {
                    int nd = nc - nclast;
                    ncnt = nd < 0 ? 0 : ncnt + 1;
                    if (ncnt >= 5 && it >= 10 && nc > 15) return nc;
                }
                nclast = nc;
                for (int c = 0; c < NCHK; c++)
                    for (int e = EDGE_START[c]; e < EDGE_START[c + 1]; e++)
                    {
                        double v = zn[EDGE_BIT[e]] - tov[e];
                        toc[e] = Math.Tanh(v / 2);
                    }
                for (int c = 0; c < NCHK; c++)
                {
                    for (int e = EDGE_START[c]; e < EDGE_START[c + 1]; e++)
                    {
                        double prod = 1;
                        for (int f = EDGE_START[c]; f < EDGE_START[c + 1]; f++) if (f != e) prod *= toc[f];
                        prod = Math.Max(-0.999999, Math.Min(0.999999, prod));
                        tov[e] = 2 * Atanh(prod);
                    }
                }
            }
            return nc;
        }

        static double Atanh(double x) { return 0.5 * Math.Log((1 + x) / (1 - x)); }

        // ------------------------------------------------------------------ décodage d'une fenêtre (12 kHz)
        /** win : n échantillons ; nominalMs : instant nominal du premier symbole dans la fenêtre (début de tranche − début de fenêtre + délai de départ). */
        public static List<Msg> DecodeWindow(short[] win, int n, int speed, int nominalMs)
        {
            List<Msg> output = new List<Msg>();
            double[] x = new double[n];
            for (int i = 0; i < n; i++) x[i] = win[i];
            for (int pass = 0; pass < PASSES; pass++)
            {
                x = DecodePass(x, n, speed, nominalMs, output, pass + 1 < PASSES);
                if (x == null) break;
            }
            return output;
        }

        /** Une passe : ajoute les messages nouveaux à output ; renvoie la fenêtre privée des signaux décodés (ou null). */
        static double[] DecodePass(double[] win, int n, int speed, int nominalMs, List<Msg> output, bool residual)
        {
            int nsps = NSPS[speed], hop = nsps / 4, nfft = 2 * nsps, nds = NDS[speed], dec = nsps / nds;
            double sp = 12000.0 / nsps, binHz = 12000.0 / nfft;
            int nfr = (n - nsps) / hop + 1;
            if (nfr < 4 * 79 + 8) return null;
            int bLo = (int)Math.Ceiling(150 / binHz), bHi = (int)(3100 / binHz), nb = bHi - bLo + 1;
            // ---- spectrogramme (deux trames réelles par FFT complexe)
            float[][] P = new float[nfr][];
            for (int m = 0; m < nfr; m++) P[m] = new float[nb];
            MFft ft = new MFft(nfft);
            double[] w = new double[nsps];
            for (int i = 0; i < nsps; i++) { double s = Math.Sin(Math.PI * (i + 0.5) / nsps); w[i] = s * s; }
            double[] xr = new double[nfft], xi = new double[nfft], yr = new double[nfft], yi = new double[nfft];
            for (int m = 0; m < nfr; m += 2)
            {
                bool two = m + 1 < nfr;
                for (int i = 0; i < nsps; i++) { xr[i] = win[m * hop + i] * w[i]; xi[i] = two ? win[(m + 1) * hop + i] * w[i] : 0; }
                for (int i = nsps; i < nfft; i++) { xr[i] = 0; xi[i] = 0; }
                ft.Run(xr, xi, yr, yi);
                for (int b = 0; b < nb; b++)
                {
                    int k = b + bLo, k2 = (nfft - k) % nfft;
                    double ar = (yr[k] + yr[k2]) * 0.5, ai = (yi[k] - yi[k2]) * 0.5;
                    P[m][b] = (float)(ar * ar + ai * ai);
                    if (two)
                    {
                        double cr = (yi[k] + yi[k2]) * 0.5, ci = (yr[k] - yr[k2]) * 0.5;
                        P[m + 1][b] = (float)(cr * cr + ci * ci);
                    }
                }
            }
            // ---- synchro grossière
            int[][] cs = ORIGINAL[speed] ? COSTAS_ORIG : COSTAS_MOD;
            int fNom = (int)JRound(nominalMs * 12.0 / hop);
            int rng = (int)JRound(2000 * 12.0 / hop);
            int tLo = Math.Max(0, fNom - rng), tHi = Math.Min(nfr - 1 - 4 * 78 - 3, fNom + rng);
            if (tHi < tLo) return null;
            int nb0 = nb - 14;
            double[] score = new double[nb0];
            int[] tbest = new int[nb0];
            for (int b0 = 0; b0 < nb0; b0++)
            {
                double best = -1; int bt = tLo;
                for (int t0 = tLo; t0 <= tHi; t0++)
                {
                    double num = 0, tot = 0;
                    for (int blk = 0; blk < 3; blk++)
                    {
                        for (int i = 0; i < 7; i++)
                        {
                            float[] row = P[t0 + 4 * (blk * 36 + i)];
                            num += row[b0 + 2 * cs[blk][i]];
                            for (int c = 0; c < 8; c++) tot += row[b0 + 2 * c];
                        }
                    }
                    double r = num / ((tot - num) / 7 + 1e-30);
                    if (r > best) { best = r; bt = t0; }
                }
                score[b0] = best; tbest[b0] = bt;
            }
            P = null;
            List<int[]> cands = new List<int[]>();
            bool[] taken = new bool[nb0];
            for (int c = 0; c < MAX_CAND; c++)
            {
                int bi = -1; double bv = SYNC_MIN;
                for (int b = 0; b < nb0; b++) if (!taken[b] && score[b] > bv)
                    {
                        // maximum local sur ±2 cases
                        bool peak = true;
                        for (int d = -2; d <= 2; d++) if (d != 0 && b + d >= 0 && b + d < nb0 && score[b + d] > score[b]) { peak = false; break; }
                        if (peak) { bv = score[b]; bi = b; }
                    }
                if (bi < 0) break;
                for (int b = Math.Max(0, bi - 2); b <= Math.Min(nb0 - 1, bi + 2); b++) taken[b] = true;
                cands.Add(new int[] { bi + bLo, tbest[bi] });
            }
            if (cands.Count == 0) return null;
            // ---- spectre complet de la fenêtre (longueur composée de facteurs 2, 3 et 5, multiple de dec)
            int m0 = (n + dec - 1) / dec;
            while (!MFft.Ok(m0)) m0++;
            int M = m0, N1 = M * dec;
            MFft big = new MFft(N1);
            double[] bxr = new double[N1], bxi = new double[N1], byr = new double[N1], byi = new double[N1];
            for (int i = 0; i < n; i++) bxr[i] = win[i];
            big.Run(bxr, bxi, byr, byi);
            bxr = null; bxi = null;
            MFft small = new MFft(M);
            double[] sr = new double[M], si = new double[M], br = new double[M], bi2 = new double[M];
            // twiddles de la recherche fine
            const int ND = 17;
            double[][][] ec = new double[ND][][], es = new double[ND][][];
            double[] deltas = new double[ND];
            double fs2 = 12000.0 / dec;
            for (int d = 0; d < ND; d++)
            {
                deltas[d] = (d - 8) * sp / 16;
                ec[d] = new double[8][]; es[d] = new double[8][];
                for (int t = 0; t < 8; t++)
                {
                    ec[d][t] = new double[nds]; es[d][t] = new double[nds];
                    double f = (t - 3.5) * sp + deltas[d];
                    for (int k = 0; k < nds; k++) { double a = -2 * Math.PI * f * (k + 0.5) / fs2; ec[d][t][k] = Math.Cos(a); es[d][t][k] = Math.Sin(a); }
                }
            }
            double kHz = N1 / 12000.0;   // cases du spectre complet par Hz
            int found = 0;
            foreach (int[] c in cands)
            {
                double f0 = c[0] * binHz;
                double fc = f0 + 3.5 * sp;
                int kc = (int)JRound(fc * kHz);
                int hwp = (int)JRound(4.5 * sp * kHz), hwt = (int)JRound(5.5 * sp * kHz);
                Array.Clear(sr, 0, M); Array.Clear(si, 0, M);
                for (int d = -hwt; d <= hwt; d++)
                {
                    int k = kc + d;
                    if (k < 0 || k >= N1) continue;
                    int ad = Math.Abs(d);
                    double wt = ad <= hwp ? 1 : 0.5 * (1 + Math.Cos(Math.PI * (ad - hwp) / (double)(hwt - hwp)));
                    int j = ((d % M) + M) % M;
                    // conjuguée (pour la TFD inverse par TFD directe)
                    sr[j] = byr[k] * wt; si[j] = -byi[k] * wt;
                }
                small.Run(sr, si, br, bi2);
                for (int j = 0; j < M; j++) { br[j] = br[j] / N1; bi2[j] = -bi2[j] / N1; }
                Msg msg = DecodeCandidate(br, bi2, M, speed, c[1] * (nds / 4), ec, es, deltas, sp, dec, f0, cs, nominalMs);
                if (msg == null) continue;
                bool dup = false;
                foreach (Msg o in output) if (o.Chars == msg.Chars && o.I3 == msg.I3) { dup = true; if (msg.Snr > o.Snr) { o.Snr = msg.Snr; o.Dt = msg.Dt; o.Freq = msg.Freq; } break; }
                if (dup) continue;
                output.Add(msg);
                found++;
                if (!residual) continue;
                // soustraction du signal décodé dans le spectre complet (modèle : amplitude complexe par symbole)
                double[] mr = new double[M], mi = new double[M], fr = new double[M], fi = new double[M];
                for (int k = 0; k < 79; k++)
                {
                    int st = msg.S0 + k * nds, t = msg.Tone[k];
                    double ar = 0, ai = 0;
                    double[] cc = ec[msg.Di][t], ss = es[msg.Di][t];
                    for (int q = 0; q < nds; q++) { double xr2 = br[st + q], xi2 = bi2[st + q]; ar += xr2 * cc[q] - xi2 * ss[q]; ai += xr2 * ss[q] + xi2 * cc[q]; }
                    ar /= nds; ai /= nds;
                    for (int q = 0; q < nds; q++) { mr[st + q] = ar * cc[q] + ai * ss[q]; mi[st + q] = ai * cc[q] - ar * ss[q]; }
                }
                small.Run(mr, mi, fr, fi);
                for (int d = -hwp; d <= hwp; d++)
                {
                    int k = kc + d;
                    if (k < 1 || k >= N1) continue;
                    int jj = ((d % M) + M) % M;
                    byr[k] -= dec * fr[jj]; byi[k] -= dec * fi[jj];
                    byr[N1 - k] -= dec * fr[jj]; byi[N1 - k] += dec * fi[jj];
                }
            }
            if (!residual || found == 0) return null;
            double[] ir = new double[N1], ii = new double[N1], zr = new double[N1], zi = new double[N1];
            for (int i = 0; i < N1; i++) { ir[i] = byr[i]; ii[i] = -byi[i]; }
            big.Run(ir, ii, zr, zi);
            double[] res = new double[n];
            for (int i = 0; i < n; i++) res[i] = zr[i] / N1;
            return res;
        }

        // variantes de calcul des LLR essayées l'une après l'autre : { 1 = amplitude / 0 = puissance, facteur }
        static readonly double[][] VARIANTS = { new double[] { 1, 1.0 }, new double[] { 1, 1.7 }, new double[] { 0, 1.0 }, new double[] { 1, 0.7 } };

        static double[] Llrs(double[][] S, double sigma2, bool amp, double scale)
        {
            double[] llr = new double[174];
            double[] wv = new double[8];
            for (int j = 0; j < 58; j++)
            {
                int k = j < 29 ? 7 + j : 43 + (j - 29);
                for (int t = 0; t < 8; t++) wv[t] = amp ? Math.Sqrt(S[k][t] / sigma2) * 2 : 0.5 * S[k][t] / sigma2;
                for (int b = 0; b < 3; b++)
                {
                    int sh = 2 - b;
                    double m1 = -1e30, m0 = -1e30;
                    for (int t = 0; t < 8; t++) { if (((t >> sh) & 1) == 1) { if (wv[t] > m1) m1 = wv[t]; } else if (wv[t] > m0) m0 = wv[t]; }
                    double s1 = 0, s0 = 0;
                    for (int t = 0; t < 8; t++) { if (((t >> sh) & 1) == 1) s1 += Math.Exp(wv[t] - m1); else s0 += Math.Exp(wv[t] - m0); }
                    llr[3 * j + b] = ((m1 + Math.Log(s1)) - (m0 + Math.Log(s0))) * scale;
                }
            }
            return llr;
        }

        static Msg DecodeCandidate(double[] br, double[] bi, int M, int speed, int s0c, double[][][] ec, double[][][] es, double[] deltas,
                                   double sp, int dec, double f0, int[][] cs, int nominalMs)
        {
            int nds = NDS[speed], ND = deltas.Length;
            // ---- recherche fine instant / décalage de fréquence sur les 21 symboles de Costas
            double best = -1; int bs0 = s0c, bd = 8;
            double[] pw = new double[8];
            for (int s0 = s0c - nds / 3; s0 <= s0c + nds / 3; s0++)
            {
                if (s0 < 0 || s0 + 79 * nds > M) continue;
                for (int d = 0; d < ND; d++)
                {
                    double num = 0, tot = 0;
                    for (int blk = 0; blk < 3; blk++) for (int i = 0; i < 7; i++)
                        {
                            int st = s0 + (blk * 36 + i) * nds;
                            Tones(br, bi, st, nds, ec[d], es[d], pw);
                            double a = 0;
                            for (int t = 0; t < 8; t++) a += pw[t];
                            double e = pw[cs[blk][i]];
                            num += e; tot += a - e;
                        }
                    double r = num / (tot / 7 + 1e-30);
                    if (r > best) { best = r; bs0 = s0; bd = d; }
                }
            }
            if (best < 1.2) return null;
            // ---- démodulation des 79 symboles
            double[][] S = new double[79][];
            for (int k = 0; k < 79; k++)
            {
                S[k] = new double[8];
                if (bs0 + k * nds + nds > M) return null;
                Tones(br, bi, bs0 + k * nds, nds, ec[bd], es[bd], S[k]);
            }
            // bruit : médiane des puissances sur les symboles de données
            double[] all = new double[58 * 8];
            int qn = 0;
            for (int k = 0; k < 79; k++)
            {
                if ((k >= 36 && k < 43) || k < 7 || k >= 72) continue;
                for (int t = 0; t < 8; t++) all[qn++] = S[k][t];
            }
            Array.Sort(all);
            double sigma2 = all[all.Length / 2] / 0.693 + 1e-30;
            int[] cw = new int[174];
            bool good = false;
            for (int v = 0; v < VARIANTS.Length && !good; v++)
            {
                double[] llr = Llrs(S, sigma2, VARIANTS[v][0] != 0, VARIANTS[v][1]);
                if (Bp(llr, cw, 30) != 0) continue;
                int[] dd = new int[87];
                for (int i2 = 0; i2 < 87; i2++) dd[i2] = cw[87 + i2];
                if (CrcOk(dd)) good = true;
            }
            if (!good) return null;
            int[] dbits = new int[87];
            for (int i = 0; i < 87; i++) dbits[i] = cw[87 + i];
            // ---- tonalités du mot de code et contrôle de la synchro
            int[] tone = new int[79];
            for (int blk = 0; blk < 3; blk++) for (int i = 0; i < 7; i++) tone[blk * 36 + i] = cs[blk][i];
            for (int j = 0; j < 58; j++)
            {
                int k = j < 29 ? 7 + j : 43 + (j - 29);
                tone[k] = (cw[3 * j] << 2) | (cw[3 * j + 1] << 1) | cw[3 * j + 2];
            }
            int nsync = 0;
            for (int blk = 0; blk < 3; blk++) for (int i = 0; i < 7; i++)
                {
                    int k = blk * 36 + i, am = 0;
                    for (int t = 1; t < 8; t++) if (S[k][t] > S[k][am]) am = t;
                    if (am == cs[blk][i]) nsync++;
                }
            if (nsync < MIN_SYNC) return null;
            double xs = 0, xn = 0;
            for (int k = 0; k < 79; k++) { xs += S[k][tone[k]]; xn += S[k][(tone[k] + 4) & 7]; }
            double rr = Math.Max((xs - xn) / (xn + 1e-30), 1e-3);
            Msg m = new Msg();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 12; i++)
            {
                int v = 0;
                for (int b = 0; b < 6; b++) v = (v << 1) | dbits[i * 6 + b];
                sb.Append(ALPHABET[v]);
            }
            m.Chars = sb.ToString();
            m.I3 = (dbits[72] << 2) | (dbits[73] << 1) | dbits[74];
            m.NSync = nsync;
            m.S0 = bs0; m.Di = bd; m.Tone = tone;
            m.Snr = (int)JRound(10 * Math.Log10(rr * sp / 2500));
            m.Dt = (bs0 * (double)dec * 1000 / 12000 - nominalMs) / 1000.0;
            m.Freq = f0 + deltas[bd];
            return m;
        }

        public const string ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-+";

        /** Puissances des 8 tonalités du symbole commençant à l'échantillon st (bande de base). */
        static void Tones(double[] br, double[] bi, int st, int nds, double[][] ec, double[][] es, double[] output)
        {
            for (int t = 0; t < 8; t++)
            {
                double[] c = ec[t], s = es[t];
                double ar = 0, ai = 0;
                for (int k = 0; k < nds; k++)
                {
                    double xr = br[st + k], xi = bi[st + k];
                    ar += xr * c[k] - xi * s[k];
                    ai += xr * s[k] + xi * c[k];
                }
                output[t] = ar * ar + ai * ai;
            }
        }

        // ------------------------------------------------------------------ émission (essais uniquement)
        /** 79 tonalités pour 12 caractères (alphabet JS8) et un type de trame. */
        public static int[] Encode(string chars, int i3, bool original)
        {
            int[] bits = new int[88];
            for (int i = 0; i < 12; i++)
            {
                int v = ALPHABET.IndexOf(chars[i]);
                for (int b = 0; b < 6; b++) bits[i * 6 + b] = (v >> (5 - b)) & 1;
            }
            bits[72] = (i3 >> 2) & 1; bits[73] = (i3 >> 1) & 1; bits[74] = i3 & 1;
            int crc = Crc12(bits);
            for (int i = 0; i < 12; i++) bits[75 + i] = (crc >> (11 - i)) & 1;
            int[] par = new int[87];
            for (int i = 0; i < 87; i++)
            {
                int p = 0;
                string row = GEN[i];
                for (int j = 0; j < 87; j++)
                {
                    int nib = Convert.ToInt32(row[j / 4].ToString(), 16);
                    if (((nib >> (3 - (j % 4))) & 1) != 0) p ^= bits[j];
                }
                par[i] = p;
            }
            int[][] cs = original ? COSTAS_ORIG : COSTAS_MOD;
            int[] tone = new int[79];
            for (int blk = 0; blk < 3; blk++) for (int i = 0; i < 7; i++) tone[blk * 36 + i] = cs[blk][i];
            for (int j = 0; j < 29; j++)
            {
                tone[7 + j] = (par[3 * j] << 2) | (par[3 * j + 1] << 1) | par[3 * j + 2];
                tone[43 + j] = (bits[3 * j] << 2) | (bits[3 * j + 1] << 1) | bits[3 * j + 2];
            }
            return tone;
        }
    }

    /**
     * Couche texte de JS8Call (réception seule, repris de PhoneSDRLink) : transforme une trame décodée de 12 caractères (72 bits) et le type
     * de transmission sur 3 bits (i3 : 1 = première trame, 2 = dernière, 4 = données) en texte affichable. Portage des fonctions unpack* de
     * Varicode.cpp / JSC.cpp et de DecodedText.cpp de JS8Call. Dictionnaire JSC : 262144 mots Latin-1 dans l'ordre, chacun terminé par un octet 0
     * (ressource « js8jsc.bin.gz », compressée en gzip).
     */
    public static class Js8Text
    {
        /** Résultat du décodage d'une trame. */
        public sealed class Frame
        {
            public string Text;
            public bool First, Last;
            /** 0 balise/CQ, 1 composé, 2 composé dirigé, 3 dirigé, 4 données (huffman ou JSC). */
            public int Kind;
            /** Vrai si le dictionnaire JSC manquait (le texte est alors celui des caractères bruts). */
            public bool Degraded;
        }

        public const int KIND_HEARTBEAT = 0, KIND_COMPOUND = 1, KIND_COMPOUND_DIRECTED = 2, KIND_DIRECTED = 3, KIND_DATA = 4;

        const string ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ+-./?";
        const string ALPHABET72 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-+/?.";
        const string ALPHANUMERIC = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ /@";
        const int NALPHABET = 41;
        const long NBASECALL = 37L * 36 * 10 * 27 * 27 * 27;
        const int NBASEGRID = 180 * 180;
        const int NUSERGRID = NBASEGRID + 10;
        const int NMAXGRID = (1 << 15) - 1;

        static readonly string[] BASECALLS = {null, "<....>", "@ALLCALL", "@JS8NET",
            "@DX/NA", "@DX/SA", "@DX/EU", "@DX/AS", "@DX/AF", "@DX/OC", "@DX/AN",
            "@REGION/1", "@REGION/2", "@REGION/3",
            "@GROUP/0", "@GROUP/1", "@GROUP/2", "@GROUP/3", "@GROUP/4", "@GROUP/5", "@GROUP/6", "@GROUP/7", "@GROUP/8", "@GROUP/9",
            "@COMMAND", "@CONTROL", "@NET", "@NTS",
            "@RESERVE/0", "@RESERVE/1", "@RESERVE/2", "@RESERVE/3", "@RESERVE/4",
            "@APRSIS", "@RAGCHEW", "@JS8", "@EMCOMM", "@ARES", "@MARS", "@AMRRON", "@RACES", "@RAYNET", "@RADAR", "@SKYWARN",
            "@CQ", "@HB", "@QSO", "@QSOPARTY", "@CONTEST", "@FIELDDAY", "@SOTA", "@IOTA", "@POTA", "@QRP", "@QRO"};

        static readonly string[] CQS = { "CQ CQ CQ", "CQ DX", "CQ QRP", "CQ CONTEST", "CQ FIELD", "CQ FD", "CQ CQ", "CQ" };

        /** QMap::key(value) de directed_cmds pour 0..31 = la plus petite clé (comme Qt). */
        static readonly string[] CMD = {" SNR?", " DIT DIT", " NACK", " HEARING?", " GRID?", ">", " STATUS?", " STATUS",
            " HEARING", " MSG", " MSG TO:", " QUERY", " QUERY MSGS", " QUERY CALL", " ACK", " GRID", " INFO?", " INFO", " FB",
            " HW CPY?", " SK", " RR", " QSL?", " QSL", " CMD", " SNR", " NO", " YES", " 73", " HEARTBEAT SNR", " AGN?", " "};

        static readonly string[] HUFF_CH = {" ", "E", "T", "A", "O", "I", "N", "S", "H", "R", "D", "L", "C", "U", "M", "W", "F", "G", "Y",
            "P", "B", ".", "V", "K", "-", "+", "?", "!", "\"", "X", "0", "J", "1", "Q", "2", "Z", "3", "5", "4", "9", "8", "6", "7", "/"};
        static readonly string[] HUFF_CODE = {"01", "100", "1101", "0011", "11111", "11100", "10111", "10100", "00011", "00000",
            "111011", "110011", "110001", "101101", "101011", "001011", "001001", "000101", "000011",
            "1111011", "1111001", "1110100", "1100101", "1100100", "1100001", "1100000", "1011001", "1011000", "1010101", "1010100",
            "0010101", "0010100", "0010001", "0010000", "0001001", "0001000", "0000101", "0000100",
            "11110101", "11110100", "11110001", "11110000", "11101011", "11101010"};

        // ---------------------------------------------------------------- dictionnaire JSC
        const int JSC_SIZE = 262144;
        static volatile byte[] jscData;
        static volatile int[] jscOff;
        static bool jscTried;
        static readonly object jscLock = new object();

        /** Charge le dictionnaire JSC (non compressé). Sans effet s'il est déjà chargé ; le flux est lu en entier mais pas fermé. */
        public static void LoadJsc(Stream input)
        {
            lock (jscLock)
            {
                if (jscData != null) return;
                MemoryStream bo = new MemoryStream(2000000);
                byte[] buf = new byte[65536];
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0) bo.Write(buf, 0, n);
                byte[] d = bo.ToArray();
                int[] off = new int[JSC_SIZE + 1];
                int w = 0;
                for (int i = 0; i < d.Length && w < JSC_SIZE; i++) if (d[i] == 0) off[++w] = i + 1;
                if (w != JSC_SIZE) throw new IOException("bad JSC table: " + w + " words");
                jscOff = off;
                jscData = d;
            }
        }

        /** Charge le dictionnaire depuis la ressource « js8jsc.bin.gz » de l'exe (une seule tentative). */
        public static bool EnsureJsc()
        {
            if (jscData != null) return true;
            lock (jscLock)
            {
                if (jscData != null) return true;
                if (jscTried) return false;
                jscTried = true;
                try
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("js8jsc.bin.gz"))
                    {
                        if (s == null) { Log.Write("JS8 : dictionnaire absent des ressources"); return false; }
                        using (GZipStream gz = new GZipStream(s, CompressionMode.Decompress)) LoadJsc(gz);
                    }
                }
                catch (Exception e) { Log.Write("JS8 dictionnaire : " + e.Message); }
                return jscData != null;
            }
        }

        public static bool JscLoaded { get { return jscData != null; } }

        static string JscWord(int j)
        {
            byte[] d = jscData; int[] off = jscOff;
            int s = off[j], e = off[j + 1] - 1;
            char[] c = new char[e - s];
            for (int i = 0; i < c.Length; i++) c[i] = (char)(d[s + i] & 0xFF);
            return new string(c);
        }

        // ---------------------------------------------------------------- bits
        static long BitsToInt(bool[] b, int from, int n)
        {
            long v = 0;
            for (int i = 0; i < n; i++) v = (v << 1) | (b[from + i] ? 1L : 0L);
            return v;
        }

        /** 12 caractères alphabet72 -> 72 bits. Null si invalide. */
        static bool[] FrameBits(string t)
        {
            if (t == null || t.Length < 12) return null;
            bool[] b = new bool[72];
            for (int i = 0; i < 12; i++)
            {
                int v = ALPHABET72.IndexOf(t[i]);
                if (v < 0) return null;
                for (int j = 0; j < 6; j++) b[i * 6 + j] = ((v >> (5 - j)) & 1) != 0;
            }
            return b;
        }

        // ---------------------------------------------------------------- alphanumériques, indicatif, locator, commande
        static string UnpackAlphaNumeric50(long packed)
        {
            ulong up = (ulong)packed;
            char[] w = new char[11];
            w[10] = ALPHANUMERIC[(int)(up % 38)]; packed = (long)(up / 38);
            w[9] = ALPHANUMERIC[(int)(packed % 38)]; packed /= 38;
            w[8] = ALPHANUMERIC[(int)(packed % 38)]; packed /= 38;
            w[7] = (packed % 2) != 0 ? '/' : ' '; packed /= 2;
            w[6] = ALPHANUMERIC[(int)(packed % 38)]; packed /= 38;
            w[5] = ALPHANUMERIC[(int)(packed % 38)]; packed /= 38;
            w[4] = ALPHANUMERIC[(int)(packed % 38)]; packed /= 38;
            w[3] = (packed % 2) != 0 ? '/' : ' '; packed /= 2;
            w[2] = ALPHANUMERIC[(int)(packed % 38)]; packed /= 38;
            w[1] = ALPHANUMERIC[(int)(packed % 38)]; packed /= 38;
            w[0] = ALPHANUMERIC[(int)(packed % 39)];
            return new string(w).Replace(" ", "");
        }

        /** Null si la valeur est hors du domaine des indicatifs. */
        static string UnpackCallsign(long value, bool portable)
        {
            if (value > NBASECALL && value - NBASECALL < BASECALLS.Length) return BASECALLS[(int)(value - NBASECALL)];
            if (value / 7085880L > 38) return null;  // 36*10*27^3 = 7085880
            char[] w = new char[6];
            w[5] = ALPHANUMERIC[(int)(value % 27 + 10)]; value /= 27;
            w[4] = ALPHANUMERIC[(int)(value % 27 + 10)]; value /= 27;
            w[3] = ALPHANUMERIC[(int)(value % 27 + 10)]; value /= 27;
            w[2] = ALPHANUMERIC[(int)(value % 10)]; value /= 10;
            w[1] = ALPHANUMERIC[(int)(value % 36)]; value /= 36;
            w[0] = ALPHANUMERIC[(int)value];
            string cs = new string(w);
            if (cs.StartsWith("3D0", StringComparison.Ordinal)) cs = "3DA0" + cs.Substring(3);
            if (cs[0] == 'Q' && cs[1] >= 'A' && cs[1] <= 'Z') cs = "3X" + cs.Substring(1);
            if (portable) cs = SlotDecoder.JTrim(cs) + "/P";
            return SlotDecoder.JTrim(cs);
        }

        /** Locator de 4 caractères ; "" au-delà de nbasegrid (comme JS8Call). */
        static string UnpackGrid(int value)
        {
            if (value > NBASEGRID) return "";
            float dlat = value % 180 - 90;
            float dlong = value / 180 * 2 - 180 + 2;
            if (dlong < -180) dlong += 360;
            if (dlong > 180) dlong -= 360;
            int nlong = (int)(60.0 * (180.0 - dlong) / 5);
            int n1 = nlong / 240, n2 = (nlong - 240 * n1) / 24;
            int nlat = (int)(60.0 * (dlat + 90) / 2.5);
            int m1 = nlat / 240, m2 = (nlat - 240 * m1) / 24;
            return "" + (char)('A' + n1) + (char)('A' + m1) + (char)('0' + n2) + (char)('0' + m2);
        }

        static bool IsSnrCmd(string cmd) { return " SNR" == cmd || " HEARTBEAT SNR" == cmd; }

        /** Indice de commande 0..31 (25 ou 29 pour les formes SNR) ; num[0] reçoit le nombre sur 6 bits. */
        static int UnpackCmd(int value, int[] num)
        {
            value &= 0xFF;
            if ((value & 0x80) != 0)
            {
                num[0] = value & 63;
                return (value & 0x40) != 0 ? 29 : 25;
            }
            num[0] = 0;
            return value & 127;
        }

        static string FormatSNR(int snr)
        {
            if (snr < -60 || snr > 60) return "";
            int a = Math.Abs(snr);
            string d = (a < 10 ? "0" : "") + a;
            if (snr < 0) return "-" + d;
            return "+" + d;
        }

        // ---------------------------------------------------------------- types de messages
        /** out[0]=type, out[1]=nombre (16 bits), out[2]=bits3 ; renvoie l'indicatif ou null. */
        static string CompoundFrame(bool[] b, int[] o)
        {
            int flag = (int)BitsToInt(b, 0, 3);
            if (flag == 4 || flag == 3) return null;
            long call = BitsToInt(b, 3, 50);
            int p11 = (int)BitsToInt(b, 53, 11);
            int p5 = (int)BitsToInt(b, 64, 5);
            o[0] = flag;
            o[1] = (p11 << 5) | p5;
            o[2] = (int)BitsToInt(b, 69, 3);
            try { return UnpackAlphaNumeric50(call); } catch (Exception) { return null; }
        }

        static Frame Heartbeat(bool[] b)
        {
            int[] o = new int[3];
            string call = CompoundFrame(b, o);
            if (call == null || o[0] != KIND_HEARTBEAT) return null;
            bool alt = (o[1] & (1 << 15)) != 0;
            string grid = UnpackGrid(o[1] & ((1 << 15) - 1));
            Frame f = new Frame();
            f.Kind = KIND_HEARTBEAT;
            f.Text = call + ": " + (alt ? "@ALLCALL " + CQS[o[2] & 7] : "@HB HEARTBEAT") + " " + grid + " ";
            return f;
        }

        static Frame Compound(bool[] b)
        {
            int[] o = new int[3];
            string call = CompoundFrame(b, o);
            if (call == null || (o[0] != KIND_COMPOUND && o[0] != KIND_COMPOUND_DIRECTED)) return null;
            int extra = o[1];
            string ex = null;
            if (extra <= NBASEGRID)
            {
                ex = " " + UnpackGrid(extra);
            }
            else if (NUSERGRID <= extra && extra < NMAXGRID)
            {
                int[] num = new int[1];
                int cmd = UnpackCmd(extra - NUSERGRID, num);
                ex = CMD[cmd];
                if (IsSnrCmd(ex)) ex = ex + " " + FormatSNR(num[0] - 31);
            }
            Frame f = new Frame();
            f.Kind = o[0];
            f.Text = o[0] == KIND_COMPOUND ? call + ": " : call + (ex == null ? "" : ex) + " ";
            return f;
        }

        static Frame Directed(bool[] b)
        {
            if ((int)BitsToInt(b, 0, 3) != KIND_DIRECTED) return null;
            long pf = BitsToInt(b, 3, 28), pt = BitsToInt(b, 31, 28);
            int cmdI = (int)BitsToInt(b, 59, 5);
            int extra = (int)BitsToInt(b, 64, 8);
            bool portFrom = ((extra >> 7) & 1) == 1, portTo = ((extra >> 6) & 1) == 1;
            extra %= 64;
            string from = UnpackCallsign(pf, portFrom), to = UnpackCallsign(pt, portTo);
            if (from == null || to == null) return null;
            string cmd = CMD[cmdI % 32];
            StringBuilder sb = new StringBuilder(from).Append(": ").Append(to).Append(cmd);
            if (extra != 0) sb.Append(' ').Append(IsSnrCmd(cmd) ? FormatSNR(extra - 31) : (extra - 31).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Frame f = new Frame();
            f.Kind = KIND_DIRECTED;
            f.Text = sb.Append(' ').ToString();
            return f;
        }

        static string HuffDecode(bool[] b, int from, int to)
        {
            StringBuilder bits = new StringBuilder();
            for (int i = from; i < to; i++) bits.Append(b[i] ? '1' : '0');
            string s = bits.ToString();
            StringBuilder output = new StringBuilder();
            int p = 0;
            while (p < s.Length)
            {
                int found = -1;
                for (int k = 0; k < HUFF_CODE.Length; k++)
                {
                    string code = HUFF_CODE[k];
                    if (p + code.Length <= s.Length && string.CompareOrdinal(s, p, code, 0, code.Length) == 0) { found = k; break; }
                }
                if (found < 0) break;
                output.Append(HUFF_CH[found]);
                p += HUFF_CODE[found].Length;
            }
            return output.ToString();
        }

        /** JSC::decompress sur les bits [from,to). Null si le dictionnaire n'est pas chargé. */
        static string JscDecompress(bool[] bv, int from, int to)
        {
            if (jscData == null && !EnsureJsc()) return null;
            const int s = 7, c = 9;
            long[] bas = new long[8];
            bas[1] = s;
            long p = s;
            for (int k = 2; k < 8; k++) { p *= c; bas[k] = bas[k - 1] + p; }
            int count = to - from;
            int[] bytes = new int[count / 4 + 1];
            int[] seps = new int[count / 4 + 1];
            int nb = 0, ns = 0, i = 0;
            while (i < count)
            {
                if (count - i < 4) break;
                int by = (int)BitsToInt(bv, from + i, 4);
                bytes[nb++] = by;
                i += 4;
                if (by < s)
                {
                    if (count - i > 0 && bv[from + i]) seps[ns++] = nb - 1;
                    i++;
                }
            }
            StringBuilder output = new StringBuilder();
            int sp = 0, start = 0;
            while (start < nb)
            {
                int k = 0;
                long j = 0;
                while (start + k < nb && bytes[start + k] >= s) { j = j * c + (bytes[start + k] - s); k++; if (k > 7) return output.ToString(); }
                if (j >= JSC_SIZE) break;
                if (start + k >= nb) break;
                j = j * s + bytes[start + k] + bas[k];
                if (j >= JSC_SIZE) break;
                output.Append(JscWord((int)j));
                if (sp < ns && seps[sp] == start + k) { output.Append(' '); sp++; }
                start += k + 1;
            }
            return output.ToString();
        }

        /** unpackDataMessage (modes normaux) ou unpackFastDataMessage ; null = rien à décoder / dictionnaire absent. */
        static string Data(bool[] b, bool fast, bool[] needJsc)
        {
            if (!fast && !b[0]) return null;
            int n = -1;
            for (int i = 71; i >= 0; i--) if (!b[i]) { n = i; break; }
            if (fast)
            {
                int end = n < 0 ? 72 : n;
                string r = JscDecompress(b, 0, end);
                if (r == null) needJsc[0] = true;
                return r;
            }
            int nn = n - 1;
            bool compressed = b[1];
            int len = nn - 1;
            int from = 2, to = len < 0 ? 72 : 2 + len;
            if (compressed)
            {
                string r = JscDecompress(b, from, to);
                if (r == null) needJsc[0] = true;
                return r;
            }
            return HuffDecode(b, from, to);
        }

        // ---------------------------------------------------------------- interface publique
        /**
         * Décode une trame. chars12 : les 12 caractères JS8, i3 : bits de type (1 première, 2 dernière, 4 données), fastSubmode : inutilisé.
         * Renvoie null si la trame ne peut pas être interprétée. Ne lève jamais d'exception.
         */
        public static Frame Decode(string chars12, int i3, bool fastSubmode)
        {
            try
            {
                if (chars12 == null) return null;
                string m = SlotDecoder.JTrim(chars12);
                if (m.Length < 12 || m.IndexOf(' ') >= 0) return null;
                bool[] b = FrameBits(m);
                if (b == null) return null;
                Frame f = null;
                bool[] needJsc = new bool[1];
                if ((i3 & 4) != 0)
                {
                    string d = Data(b, true, needJsc);
                    if (needJsc[0]) { f = new Frame(); f.Text = m.Substring(0, 12); f.Degraded = true; f.Kind = KIND_DATA; }
                    else if (d != null && d.Length > 0) { f = new Frame(); f.Text = d; f.Kind = KIND_DATA; }
                }
                else
                {
                    string d = Data(b, false, needJsc);
                    if (needJsc[0]) { f = new Frame(); f.Text = m.Substring(0, 12); f.Degraded = true; f.Kind = KIND_DATA; }
                    else if (d != null && d.Length > 0) { f = new Frame(); f.Text = d; f.Kind = KIND_DATA; }
                    if (f == null) f = Heartbeat(b);
                    if (f == null) f = Compound(b);
                    if (f == null) f = Directed(b);
                }
                if (f == null) return null;
                f.First = (i3 & 1) != 0;
                f.Last = (i3 & 2) != 0;
                return f;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /** Texte d'une trame sur une ligne (espaces de fin enlevés sauf pour les données ; retours à la ligne remplacés par des espaces). */
        public static string FrameText(string chars12, int i3, bool fastSubmode)
        {
            Frame f = Decode(chars12, i3, fastSubmode);
            if (f == null) return null;
            string t = f.Text.Replace('\n', ' ').Replace('\r', ' ');
            if (f.Kind != KIND_DATA)
            {
                int e = t.Length;
                while (e > 0 && t[e - 1] == ' ') e--;
                t = t.Substring(0, e);
            }
            return t;
        }
    }
}
