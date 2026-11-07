using System;
using System.Collections.Generic;
using System.Globalization;

namespace SDRLink
{
    // Outils communs aux décodeurs WSPR et JT9 (repris de PhoneSDRLink, WsjtCore.java) : FFT, passage en bande de base,
    // code convolutif K=32 r=1/2 et décodeur de Fano.
    public static class WsjtCore
    {
        public const int POLY1 = unchecked((int)0xf2d05351), POLY2 = unchecked((int)0xe4613c47);

        // ---------------------------------------------------------------- FFT complexe (taille puissance de 2)
        public sealed class Fft
        {
            readonly int n;
            readonly double[] cs, sn;
            readonly int[] rev;
            public Fft(int n)
            {
                this.n = n;
                cs = new double[n / 2];
                sn = new double[n / 2];
                for (int i = 0; i < n / 2; i++) { cs[i] = Math.Cos(2 * Math.PI * i / n); sn[i] = -Math.Sin(2 * Math.PI * i / n); }
                rev = new int[n];
                int bits = SlotDecoder.TrailingZeros(n);
                for (int i = 0; i < n; i++) rev[i] = (int)((uint)SlotDecoder.Reverse(i) >> (32 - bits));
            }
            public void Run(double[] re, double[] im)
            {
                for (int i = 0; i < n; i++)
                {
                    int j = rev[i];
                    if (j > i) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
                }
                for (int len = 2; len <= n; len <<= 1)
                {
                    int half = len >> 1, step = n / len;
                    for (int i = 0; i < n; i += len)
                    {
                        for (int k = 0, w = 0; k < half; k++, w += step)
                        {
                            int a = i + k, b = a + half;
                            double xr = re[b] * cs[w] - im[b] * sn[w], xi = re[b] * sn[w] + im[b] * cs[w];
                            re[b] = re[a] - xr; im[b] = im[a] - xi;
                            re[a] += xr; im[a] += xi;
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- bande de base
        public static double[] Lowpass(double cut, double fs, int taps)
        {
            double[] h = new double[taps];
            int m = taps / 2;
            double wc = 2 * Math.PI * cut / fs, sum = 0;
            for (int i = 0; i < taps; i++)
            {
                int k = i - m;
                double s = k == 0 ? wc / Math.PI : Math.Sin(wc * k) / (Math.PI * k);
                double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (taps - 1));
                h[i] = s * w;
                sum += h[i];
            }
            for (int i = 0; i < taps; i++) h[i] /= sum;
            return h;
        }

        /** Multiplie x[off .. off+n[ par exp(-j 2π fc t), filtre (sinc fenêtrée, coupure cut Hz, taps impair) et décime par dec (taux 12000/dec). */
        public static void MixDecimate(short[] x, int off, int n, double fc, int dec, double cut, int taps, double[] re, double[] im, int nOut)
        {
            double[] h = Lowpass(cut, 12000.0, taps);
            int m = taps / 2;
            double dw = 2 * Math.PI * fc / 12000.0;
            const int CH = 512;
            double[] cr = new double[CH * dec + taps], ci = new double[CH * dec + taps];
            for (int o0 = 0; o0 < nOut; o0 += CH)
            {
                int o1 = Math.Min(nOut, o0 + CH);
                int i0 = o0 * dec - m, len = (o1 - o0 - 1) * dec + taps;
                double ph = dw * i0;
                double pr = Math.Cos(ph), pi = -Math.Sin(ph), rr = Math.Cos(dw), ri = -Math.Sin(dw);
                for (int i = 0; i < len; i++)
                {
                    int idx = i0 + i;
                    double v = idx >= 0 && idx < n ? x[off + idx] : 0;
                    cr[i] = v * pr;
                    ci[i] = v * pi;
                    double nr = pr * rr - pi * ri, ni = pr * ri + pi * rr;
                    pr = nr; pi = ni;
                }
                for (int o = o0; o < o1; o++)
                {
                    int a = (o - o0) * dec;
                    double sr = 0, si = 0;
                    for (int k = 0; k < taps; k++) { sr += h[k] * cr[a + k]; si += h[k] * ci[a + k]; }
                    re[o] = sr; im[o] = si;
                }
            }
        }

        /** Filtre passe-bas (coupure cut Hz, taux fs) et décimation par dec d'un signal complexe. */
        public static void Decimate(double[] xr, double[] xi, int n, double fs, int dec, double cut, int taps, double[] re, double[] im, int nOut)
        {
            double[] h = Lowpass(cut, fs, taps);
            int m = taps / 2;
            for (int o = 0; o < nOut; o++)
            {
                int a = o * dec - m;
                double sr = 0, si = 0;
                for (int k = 0; k < taps; k++)
                {
                    int idx = a + k;
                    if (idx < 0 || idx >= n) continue;
                    sr += h[k] * xr[idx]; si += h[k] * xi[idx];
                }
                re[o] = sr; im[o] = si;
            }
        }

        // ---------------------------------------------------------------- code convolutif
        public static int Parity(int v) { return SlotDecoder.BitCount(v) & 1; }

        /** bits[0..nb[ (info + queue de zéros) -> 2*nb symboles (0/1). */
        public static int[] Encode(int[] bits, int nb)
        {
            int[] s = new int[2 * nb];
            int st = 0;
            for (int i = 0; i < nb; i++)
            {
                st = (st << 1) | bits[i];
                s[2 * i] = Parity(st & POLY1);
                s[2 * i + 1] = Parity(st & POLY2);
            }
            return s;
        }

        // Table de métrique (wsprd / WSJT-X, 256 valeurs : indexée par le symbole reçu 0..255, pour le bit 1 ; le bit 0 utilise 255-i).
        static readonly float[] METRIC = {
            0.9999f, 0.9998f, 0.9998f, 0.9998f, 0.9998f, 0.9998f, 0.9997f, 0.9997f, 0.9997f, 0.9997f,
            0.9997f, 0.9996f, 0.9996f, 0.9996f, 0.9995f, 0.9995f, 0.9994f, 0.9994f, 0.9994f, 0.9993f,
            0.9993f, 0.9992f, 0.9991f, 0.9991f, 0.9990f, 0.9989f, 0.9988f, 0.9988f, 0.9988f, 0.9986f,
            0.9985f, 0.9984f, 0.9983f, 0.9982f, 0.9980f, 0.9979f, 0.9977f, 0.9976f, 0.9974f, 0.9971f,
            0.9969f, 0.9968f, 0.9965f, 0.9962f, 0.9960f, 0.9957f, 0.9953f, 0.9950f, 0.9947f, 0.9941f,
            0.9937f, 0.9933f, 0.9928f, 0.9922f, 0.9917f, 0.9911f, 0.9904f, 0.9897f, 0.9890f, 0.9882f,
            0.9874f, 0.9863f, 0.9855f, 0.9843f, 0.9832f, 0.9819f, 0.9806f, 0.9792f, 0.9777f, 0.9760f,
            0.9743f, 0.9724f, 0.9704f, 0.9683f, 0.9659f, 0.9634f, 0.9609f, 0.9581f, 0.9550f, 0.9516f,
            0.9481f, 0.9446f, 0.9406f, 0.9363f, 0.9317f, 0.9270f, 0.9218f, 0.9160f, 0.9103f, 0.9038f,
            0.8972f, 0.8898f, 0.8822f, 0.8739f, 0.8647f, 0.8554f, 0.8457f, 0.8357f, 0.8231f, 0.8115f,
            0.7984f, 0.7854f, 0.7704f, 0.7556f, 0.7391f, 0.7210f, 0.7038f, 0.6840f, 0.6633f, 0.6408f,
            0.6174f, 0.5939f, 0.5678f, 0.5410f, 0.5137f, 0.4836f, 0.4524f, 0.4193f, 0.3850f, 0.3482f,
            0.3132f, 0.2733f, 0.2315f, 0.1891f, 0.1435f, 0.0980f, 0.0493f, 0.0000f, -0.0510f, -0.1052f,
            -0.1593f, -0.2177f, -0.2759f, -0.3374f, -0.4005f, -0.4599f, -0.5266f, -0.5935f, -0.6626f, -0.7328f,
            -0.8051f, -0.8757f, -0.9498f, -1.0271f, -1.1019f, -1.1816f, -1.2642f, -1.3459f, -1.4295f, -1.5077f,
            -1.5958f, -1.6818f, -1.7647f, -1.8548f, -1.9387f, -2.0295f, -2.1152f, -2.2154f, -2.3011f, -2.3904f,
            -2.4820f, -2.5786f, -2.6730f, -2.7652f, -2.8616f, -2.9546f, -3.0526f, -3.1445f, -3.2445f, -3.3416f,
            -3.4357f, -3.5325f, -3.6324f, -3.7313f, -3.8225f, -3.9209f, -4.0248f, -4.1278f, -4.2261f, -4.3193f,
            -4.4220f, -4.5262f, -4.6214f, -4.7242f, -4.8234f, -4.9245f, -5.0298f, -5.1250f, -5.2232f, -5.3267f,
            -5.4332f, -5.5342f, -5.6431f, -5.7270f, -5.8401f, -5.9350f, -6.0407f, -6.1418f, -6.2363f, -6.3384f,
            -6.4536f, -6.5429f, -6.6582f, -6.7433f, -6.8438f, -6.9478f, -7.0789f, -7.1894f, -7.2714f, -7.3815f,
            -7.4810f, -7.5575f, -7.6852f, -7.8071f, -7.8580f, -7.9724f, -8.1000f, -8.2207f, -8.2867f, -8.4017f,
            -8.5287f, -8.6347f, -8.7082f, -8.8319f, -8.9448f, -9.0355f, -9.1885f, -9.2095f, -9.2863f, -9.4186f,
            -9.5064f, -9.6386f, -9.7207f, -9.8286f, -9.9453f, -10.0701f, -10.1735f, -10.3001f, -10.2858f, -10.5427f,
            -10.5982f, -10.7361f, -10.7042f, -10.9212f, -11.0097f, -11.0469f, -11.1155f, -11.2812f, -11.3472f, -11.4988f,
            -11.5327f, -11.6692f, -11.9376f, -11.8606f, -12.1372f, -13.2539f };

        // Les symboles reçus sont des octets 0..255 : 128 = indécis ; > 127 = « 1 » probable. MT[b][r] = métrique du symbole émis b, reçu r.
        static readonly int[][] MT = MakeMt();
        static int[][] MakeMt()
        {
            int[][] mt = { new int[256], new int[256] };
            for (int i = 0; i < 256; i++)
            {
                // Java : 10 * (float - 0.45) en double
                mt[0][i] = (int)SlotDecoder.JRound(10 * ((double)METRIC[i] - 0.45));
                mt[1][i] = (int)SlotDecoder.JRound(10 * ((double)METRIC[255 - i] - 0.45));
            }
            return mt;
        }

        /**
         * Décodeur séquentiel de Fano (algorithme de Karn). sym : 2*nbits octets 0..255 ; nbits : bits d'information + queue (les 31 derniers sont à zéro).
         * Renvoie la métrique finale ou int.MinValue en cas d'échec (limite de cycles) ; data[] reçoit les bits (0/1).
         */
        public static int Fano(int[] sym, int nbits, int delta, long maxCycles, int[] data)
        {
            int tail = nbits - 31;
            int[] enc = new int[nbits + 1], gam = new int[nbits + 1], ibr = new int[nbits + 1];
            int[][] tm = { new int[nbits + 1], new int[nbits + 1] };   // métriques des branches classées (0 : meilleure)
            int[] tb = new int[nbits + 1];                              // bit de la meilleure branche
            int np = 0, t = 0;
            enc[0] = 0; gam[0] = 0; ibr[0] = 0;
            Branch(sym, 0, enc[0], tm, tb, tail);
            long cycles = 0;
            while (true)
            {
                if (++cycles > maxCycles) return int.MinValue;
                int ng = gam[np] + tm[ibr[np]][np];
                if (ng >= t)
                {
                    // pas en avant
                    if (gam[np] < t + delta) { while (ng >= t + delta) t += delta; }
                    int bit = ibr[np] == 0 ? tb[np] : 1 - tb[np];
                    data[np] = bit;
                    int ne = (enc[np] << 1) | bit;
                    np++;
                    gam[np] = ng; enc[np] = ne;
                    if (np == nbits) return ng;
                    ibr[np] = 0;
                    Branch(sym, np, ne, tm, tb, tail);
                    continue;
                }
                // pas en arrière
                while (true)
                {
                    if (np == 0 || gam[np - 1] < t)
                    {
                        t -= delta;
                        ibr[np] = 0;
                        break;
                    }
                    np--;
                    if (ibr[np] == 0 && np < tail) { ibr[np] = 1; break; }
                }
            }
        }

        // Métriques des deux branches du noeud k (état e) : la meilleure d'abord. Dans la queue, seule la branche « 0 » existe.
        static void Branch(int[] sym, int k, int e, int[][] tm, int[] tb, int tail)
        {
            int r1 = sym[2 * k], r2 = sym[2 * k + 1];
            int s0 = e << 1, s1 = s0 | 1;
            int m0 = MT[Parity(s0 & POLY1)][r1] + MT[Parity(s0 & POLY2)][r2];
            int m1 = MT[Parity(s1 & POLY1)][r1] + MT[Parity(s1 & POLY2)][r2];
            if (k >= tail) { tm[0][k] = m0; tm[1][k] = int.MinValue / 4; tb[k] = 0; return; }
            if (m0 >= m1) { tm[0][k] = m0; tm[1][k] = m1; tb[k] = 0; }
            else { tm[0][k] = m1; tm[1][k] = m0; tb[k] = 1; }
        }

        // ---------------------------------------------------------------- entrelacement par inversion de bits (WSPR : 162 sur 256 ; JT9 : 206 sur 256)
        public static int[] BitRevTable(int count)
        {
            int[] j0 = new int[count];
            int k = 0;
            for (int i = 0; i < 256; i++)
            {
                int n = (int)((uint)SlotDecoder.Reverse(i) >> 24);
                if (n < count) j0[k++] = n;
            }
            return j0;
        }

        /** Normalisation des valeurs souples v[] (positif = « 1 ») vers des octets 0..255 centrés sur 128, écart-type ramené à fac. */
        public static int[] ToBytes(double[] v, double fac)
        {
            int n = v.Length;
            double mean = 0, sq = 0;
            for (int i = 0; i < n; i++) mean += v[i];
            mean /= n;
            for (int i = 0; i < n; i++) sq += (v[i] - mean) * (v[i] - mean);
            double sd = Math.Sqrt(sq / n);
            if (sd < 1e-12) sd = 1;
            int[] o = new int[n];
            for (int i = 0; i < n; i++)
            {
                double x = (v[i] - mean) * fac / sd;
                o[i] = (int)Math.Max(0, Math.Min(255, SlotDecoder.JRound(x + 128)));
            }
            return o;
        }

        /** Locator de 4 caractères (WSPR, JT9) d'après ng < 32400. */
        internal static string Grid(long ng)
        {
            int dlat = (int)(ng % 180) - 90;
            int dlong = (int)(ng / 180) * 2 - 180 + 2;
            int nlong = (int)(60.0 * (180.0 - dlong) / 5.0);
            int g1 = nlong / 240, g2 = (nlong - 240 * g1) / 24;
            int nlat = (int)(60.0 * (dlat + 90) / 2.5);
            int g3 = nlat / 240, g4 = (nlat - 240 * g3) / 24;
            string ch = WsprEngine.CH;
            return "" + ch[10 + g1] + ch[10 + g3] + ch[g2] + ch[g4];
        }
    }

    // Décodeur WSPR (réception seule, repris de PhoneSDRLink) : tranches de 120 s qui commencent à chaque minute paire UTC (+1 s), quatre
    // tonalités (1,4648 Hz d'écart, 162 symboles de 0,6827 s) entre 1400 et 1600 Hz, message de 50 bits (indicatif, locator, puissance)
    // protégé par un code convolutif (K=32, r=1/2) décodé par l'algorithme de Fano. Seuls les messages de type 1 sont décodés.
    public sealed class WsprEngine : SlotDecoder
    {
        public static readonly int[] PR3 = {
            1, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1, 0,
            0, 1, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 1,
            0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1, 0, 0, 0, 1,
            1, 0, 1, 0, 0, 0, 0, 1, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0, 1,
            0, 0, 1, 0, 1, 1, 0, 0, 0, 1, 1, 0, 1, 0, 1, 0, 0, 0, 1, 0,
            0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1, 1, 1, 0, 1, 1, 0, 0, 1, 1,
            0, 1, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 1, 1,
            0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 1, 0, 1, 1, 0, 0, 0, 1, 1, 0,
            0, 0 };
        public static readonly int[] J0 = WsjtCore.BitRevTable(162);
        public const double DF = 12000.0 / 8192.0;   // écart entre tonalités (Hz)
        const double FS = 375.0;                      // taux de la bande de base
        const int SYM = 256;                          // échantillons par symbole à 375 Hz
        const int HOP = 32;
        const int MAX_ERR = 72;                       // erreurs de bits durs tolérées sur 162 (le bruit seul en donne ~81)
        const long MAX_CYCLES = 81L * 3000;
        public static double CAND_MIN = 0.07;
        public static int MAX_CAND = 40;
        public static int EXCL = 3;                   // candidats : écart minimal (bins de 0,73 Hz) entre deux maxima de corrélation

        public sealed class Msg
        {
            public string Text; public int Snr, Errs; public double Dt, Freq;
        }

        public WsprEngine() : base("WSPR", 120000, 3000, 118000) { }

        protected override string TimeText(DateTime c) { return Two(c.Hour) + Two(c.Minute); }

        public override Result Decode(short[] win, int n, long slotStartMs, string t)
        {
            Result r = new Result();
            foreach (Msg m in DecodeWindow(win, n, PreMs + 1000))
            {
                r.Lines.Add(FormatLine(t, m.Snr, m.Dt, m.Freq, m.Text));
                r.Dts.Add(m.Dt);
            }
            return r;
        }

        // ------------------------------------------------------------------ décodage d'une fenêtre (12 kHz) ; nominalMs : instant nominal du début du signal
        public static List<Msg> DecodeWindow(short[] win, int n, int nominalMs)
        {
            List<Msg> output = new List<Msg>();
            int n1 = n / 8;
            double[] r1 = new double[n1], i1 = new double[n1];
            WsjtCore.MixDecimate(win, 0, n, 1500, 8, 500, 97, r1, i1, n1);
            int nb = n1 / 4;
            double[] br = new double[nb], bi = new double[nb];
            WsjtCore.Decimate(r1, i1, n1, 1500, 4, 165, 161, br, bi, nb);
            r1 = null; i1 = null;
            int nf = (nb - SYM) / HOP + 1;
            if (nf < 8 * 162 + 10) return output;
            // spectrogramme : bins de 0,732 Hz (FFT de 512 sur 256 échantillons)
            float[][] P = new float[nf][];
            WsjtCore.Fft fft = new WsjtCore.Fft(512);
            double[] xr = new double[512], xi = new double[512];
            for (int m = 0; m < nf; m++)
            {
                for (int i = 0; i < SYM; i++) { xr[i] = br[m * HOP + i]; xi[i] = bi[m * HOP + i]; }
                for (int i = SYM; i < 512; i++) { xr[i] = 0; xi[i] = 0; }
                fft.Run(xr, xi);
                float[] p = new float[512];
                P[m] = p;
                for (int b = 0; b < 512; b++) p[b] = (float)(xr[b] * xr[b] + xi[b] * xi[b]);
            }
            int nomF = (int)JRound(nominalMs * FS / 1000.0 / HOP);
            int mLo = Math.Max(0, nomF - 36), mHi = Math.Min(nf - 1 - 8 * 161, nomF + 36);
            const int bLo = -200, bHi = 200 - 8;
            double[] bestR = new double[bHi - bLo + 1];
            int[] bestM = new int[bHi - bLo + 1];
            for (int b0 = bLo; b0 <= bHi; b0++)
            {
                int i0 = b0 & 511, i1b = (b0 + 2) & 511, i2 = (b0 + 4) & 511, i3 = (b0 + 6) & 511;
                double br0 = -1; int bm = mLo;
                for (int m0 = mLo; m0 <= mHi; m0++)
                {
                    double sc = 0, nr = 1e-30;
                    for (int k = 0; k < 162; k++)
                    {
                        float[] p = P[m0 + 8 * k];
                        // sommes en float comme en Java
                        double d = (float)((float)((float)(p[i1b] + p[i3]) - p[i0]) - p[i2]);
                        sc += PR3[k] == 1 ? d : -d;
                        nr += (float)((float)((float)(p[i0] + p[i1b]) + p[i2]) + p[i3]);
                    }
                    double rr = sc / nr;
                    if (rr > br0) { br0 = rr; bm = m0; }
                }
                bestR[b0 - bLo] = br0; bestM[b0 - bLo] = bm;
            }
            P = null;
            // candidats : maxima de la corrélation
            List<int[]> cands = new List<int[]>();
            bool[] used = new bool[bestR.Length];
            for (int c = 0; c < MAX_CAND; c++)
            {
                int bi0 = -1; double bv = CAND_MIN;
                for (int i = 0; i < bestR.Length; i++) if (!used[i] && bestR[i] > bv) { bv = bestR[i]; bi0 = i; }
                if (bi0 < 0) break;
                for (int i = Math.Max(0, bi0 - EXCL); i <= Math.Min(bestR.Length - 1, bi0 + EXCL); i++) used[i] = true;
                cands.Add(new int[] { bi0 + bLo, bestM[bi0] });
            }
            double[] pw = new double[162 * 4];
            double[] drifts = { 0, 1, -1, 2, -2 };
            foreach (int[] c in cands)
            {
                double f0 = c[0] * FS / 512.0;
                int t0 = c[1] * HOP;
                // affinage : fréquence ±0,37 Hz, instant ±16 échantillons
                double bf = f0, bs = -2; int bt = t0;
                for (int dt = -16; dt <= 16; dt += 4)
                {
                    for (int df = -3; df <= 3; df++)
                    {
                        double f = f0 + df * 0.12;
                        int tt = t0 + dt;
                        if (tt < 0 || tt + 162 * SYM > nb) continue;
                        double s = Demod(br, bi, f, tt, 0, pw);
                        if (s > bs) { bs = s; bf = f; bt = tt; }
                    }
                }
                foreach (double drift in drifts)
                {
                    Msg m = TryDecode(br, bi, bf, bt, drift, pw);
                    if (m == null) continue;
                    m.Dt = bt / FS - nominalMs / 1000.0;
                    m.Freq = 1500 + bf + 1.5 * DF;
                    bool dup = false;
                    foreach (Msg o in output) if (o.Text == m.Text) { dup = true; if (m.Snr > o.Snr) { o.Snr = m.Snr; o.Dt = m.Dt; o.Freq = m.Freq; } }
                    if (!dup) output.Add(m);
                    break;
                }
            }
            return output;
        }

        /** Démodulation directe à partir de l'instant t (échantillons à 375 Hz) : pw[4k+j] = puissance de la tonalité j au symbole k. Renvoie le score de synchro normalisé. */
        static double Demod(double[] br, double[] bi, double f0, int t, double drift, double[] pw)
        {
            double[] cs = TC, sn = TS;
            double sc = 0, nr = 1e-30;
            for (int k = 0; k < 162; k++)
            {
                double f = f0 + drift * (k - 80.5) / 161.0;
                double w = -2 * Math.PI * f / FS, wr = Math.Cos(w), wi = Math.Sin(w);
                double pr = 1, pi = 0;
                double a0r = 0, a0i = 0, a1r = 0, a1i = 0, a2r = 0, a2i = 0, a3r = 0, a3i = 0;
                int s = t + k * SYM;
                for (int n = 0; n < SYM; n++)
                {
                    double xr = br[s + n], xi = bi[s + n];
                    double yr = xr * pr - xi * pi, yi = xr * pi + xi * pr;
                    a0r += yr; a0i += yi;
                    int i1 = n & 255, i2 = (2 * n) & 255, i3 = (3 * n) & 255;
                    a1r += yr * cs[i1] - yi * sn[i1]; a1i += yr * sn[i1] + yi * cs[i1];
                    a2r += yr * cs[i2] - yi * sn[i2]; a2i += yr * sn[i2] + yi * cs[i2];
                    a3r += yr * cs[i3] - yi * sn[i3]; a3i += yr * sn[i3] + yi * cs[i3];
                    double nr2 = pr * wr - pi * wi; pi = pr * wi + pi * wr; pr = nr2;
                }
                double p0 = a0r * a0r + a0i * a0i, p1 = a1r * a1r + a1i * a1i, p2 = a2r * a2r + a2i * a2i, p3 = a3r * a3r + a3i * a3i;
                pw[4 * k] = p0; pw[4 * k + 1] = p1; pw[4 * k + 2] = p2; pw[4 * k + 3] = p3;
                double d = p1 + p3 - p0 - p2;
                sc += PR3[k] == 1 ? d : -d;
                nr += p0 + p1 + p2 + p3;
            }
            return sc / nr;
        }

        // tonalité j du symbole : exp(-j 2π j n / 256)
        static readonly double[] TC = MakeTab(true), TS = MakeTab(false);
        static double[] MakeTab(bool cos)
        {
            double[] a = new double[256];
            for (int i = 0; i < 256; i++) a[i] = cos ? Math.Cos(2 * Math.PI * i / 256) : -Math.Sin(2 * Math.PI * i / 256);
            return a;
        }

        static Msg TryDecode(double[] br, double[] bi, double f, int t, double drift, double[] pw)
        {
            Demod(br, bi, f, t, drift, pw);
            double[] v = new double[162];
            for (int k = 0; k < 162; k++)
            {
                int s = PR3[k];
                v[k] = Math.Sqrt(pw[4 * k + s + 2]) - Math.Sqrt(pw[4 * k + s]);
            }
            double[] d = new double[162];
            for (int p = 0; p < 162; p++) d[p] = v[J0[p]];
            int[] sym = WsjtCore.ToBytes(d, 50);
            int[] data = new int[81];
            int met = WsjtCore.Fano(sym, 81, 60, MAX_CYCLES, data);
            if (met == int.MinValue) return null;
            int[] enc = WsjtCore.Encode(data, 81);
            int errs = 0;
            for (int p = 0; p < 162; p++) if ((sym[p] > 127 ? 1 : 0) != enc[p]) errs++;
            if (errs > MAX_ERR) return null;
            string text = Unpack(data);
            if (text == null) return null;
            // SNR (référence 2500 Hz) : puissance de la tonalité émise contre le bruit moyen des trois autres
            int[] tx = new int[162];
            for (int p = 0; p < 162; p++) tx[J0[p]] = enc[p];
            double sig = 0, noi = 0;
            for (int k = 0; k < 162; k++)
            {
                int tone = PR3[k] + 2 * tx[k];
                double tot = pw[4 * k] + pw[4 * k + 1] + pw[4 * k + 2] + pw[4 * k + 3];
                sig += pw[4 * k + tone];
                noi += (tot - pw[4 * k + tone]) / 3;
            }
            sig /= 162; noi /= 162;
            double ps = Math.Max(sig - noi, noi * 0.01);
            Msg m = new Msg();
            m.Text = text;
            m.Errs = errs;
            m.Snr = (int)JRound(Math.Max(-35, Math.Min(30, 10 * Math.Log10(ps / noi) - 10 * Math.Log10(2500.0 / DF))));
            return m;
        }

        // ------------------------------------------------------------------ message de 50 bits (type 1) ; null si invalide
        public const string CH = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ ";

        public static string Unpack(int[] bits)
        {
            long n1 = 0, n2 = 0;
            for (int i = 0; i < 28; i++) n1 = (n1 << 1) + bits[i];
            for (int i = 28; i < 50; i++) n2 = (n2 << 1) + bits[i];
            if (n1 >= 262177560L) return null;
            long ng = n2 >> 7;
            int ntype = (int)(n2 & 127) - 64;
            if (ng >= 32400 || ntype < 0 || ntype > 60 || (ntype % 10 != 0 && ntype % 10 != 3 && ntype % 10 != 7)) return null;
            string call = CallFrom(n1);
            if (call == null) return null;
            return call + " " + WsjtCore.Grid(ng) + " " + ntype.ToString(CultureInfo.InvariantCulture);
        }

        /** Indicatif standard de 28 bits : texte sans espaces, ou null s'il est invalide. */
        public static string CallFrom(long n1)
        {
            if (n1 < 0 || n1 >= 262177560L) return null;
            char[] c = new char[6];
            long n = n1;
            c[5] = CH[(int)(n % 27) + 10]; n /= 27;
            c[4] = CH[(int)(n % 27) + 10]; n /= 27;
            c[3] = CH[(int)(n % 27) + 10]; n /= 27;
            c[2] = CH[(int)(n % 10)]; n /= 10;
            c[1] = CH[(int)(n % 36)]; n /= 36;
            c[0] = CH[(int)n];
            string call = new string(c);
            return PlausibleCall(call) ? JTrim(call) : null;
        }

        static bool IsDigit(char c) { return c >= '0' && c <= '9'; }
        static bool IsLetter(char c) { return c >= 'A' && c <= 'Z'; }

        /** Indicatif de 6 caractères (complété par des espaces) : le 3e est un chiffre ; espaces seulement au début (en 1re place) et à la fin. */
        static bool PlausibleCall(string c)
        {
            if (c.Length != 6 || !IsDigit(c[2])) return false;
            char c0 = c[0], c1 = c[1];
            if (c1 == ' ') return false;
            if (c0 == ' ' && !IsLetter(c1)) return false;
            if (IsDigit(c0) && !IsLetter(c1)) return false;
            bool sp = false;
            int letters = 0;
            for (int i = 3; i < 6; i++)
            {
                char x = c[i];
                if (x == ' ') sp = true;
                else if (sp || !IsLetter(x)) return false;
                else letters++;
            }
            return letters >= 1;
        }
    }

    // Décodeur JT9 (sous-mode A, réception seule, repris de PhoneSDRLink) : tranches de 60 s, FSK à 9 tonalités (1,736 Hz d'écart,
    // 85 symboles de 0,576 s dont 16 de synchro sur la tonalité 0), message de 72 bits (comme JT65) protégé par le même code
    // convolutif que WSPR (Fano), entrelacement par inversion de bits, code de Gray. Seuls les messages standard sont décodés.
    public sealed class Jt9Engine : SlotDecoder
    {
        static readonly int[] ISYNC_POS = { 1, 2, 5, 10, 16, 23, 33, 35, 51, 52, 55, 60, 66, 73, 83, 85 };   // positions (à partir de 1) des symboles de synchro
        public static readonly bool[] SYNC = MakeSync();
        static bool[] MakeSync()
        {
            bool[] s = new bool[85];
            foreach (int p in ISYNC_POS) s[p - 1] = true;
            return s;
        }
        public static readonly int[] J0 = WsjtCore.BitRevTable(206);
        public const int SYMLEN = 6912;
        public const double DF = 12000.0 / SYMLEN;
        const int BSYM = 432;            // échantillons par symbole à 750 Hz
        const int NFFT = 16384, HOP = 3456;
        public static double MAX_ERR_FRAC = 0.40;   // part de bits durs en désaccord tolérée (le bruit seul : 0,5)
        public static double CAND_MIN = 1.6;
        public static int MAX_CAND = 20;
        public static double SYMFAC = 60;
        const long MAX_CYCLES = 103L * 3000;
        const long NBASE = 262177560L;

        public sealed class Msg
        {
            public string Text; public int Snr, Errs; public double Dt, Freq;
        }

        public Jt9Engine() : base("JT9", 60000, 2500, 54000) { }

        protected override string TimeText(DateTime c) { return Two(c.Hour) + Two(c.Minute) + Two(c.Second); }

        public override Result Decode(short[] win, int n, long slotStartMs, string t)
        {
            Result r = new Result();
            foreach (Msg m in DecodeWindow(win, n, PreMs + 1000))
            {
                r.Lines.Add(FormatLine(t, m.Snr, m.Dt, m.Freq, m.Text));
                r.Dts.Add(m.Dt);
            }
            return r;
        }

        // ------------------------------------------------------------------ décodage d'une fenêtre (12 kHz)
        public static List<Msg> DecodeWindow(short[] win, int n, int nominalMs)
        {
            List<Msg> output = new List<Msg>();
            int nf = (n - SYMLEN) / HOP + 1;
            if (nf < 2 * 84 + 4) return output;
            int bLo = (int)(300 / (12000.0 / NFFT)), bHi = (int)(2900 / (12000.0 / NFFT)), nb = bHi - bLo + 1;
            float[][] P = new float[nf][];
            for (int m = 0; m < nf; m++) P[m] = new float[nb];
            WsjtCore.Fft fft = new WsjtCore.Fft(NFFT);
            double[] xr = new double[NFFT], xi = new double[NFFT];
            for (int m = 0; m < nf; m += 2)
            {   // deux trames par FFT complexe (réelle + imaginaire)
                bool two = m + 1 < nf;
                for (int i = 0; i < SYMLEN; i++) { xr[i] = win[m * HOP + i]; xi[i] = two ? win[(m + 1) * HOP + i] : 0; }
                for (int i = SYMLEN; i < NFFT; i++) { xr[i] = 0; xi[i] = 0; }
                fft.Run(xr, xi);
                for (int b = 0; b < nb; b++)
                {
                    int k = b + bLo, k2 = (NFFT - k) & (NFFT - 1);
                    double ar = (xr[k] + xr[k2]) * 0.5, ai = (xi[k] - xi[k2]) * 0.5;
                    P[m][b] = (float)(ar * ar + ai * ai);
                    if (two)
                    {
                        double cr = (xi[k] + xi[k2]) * 0.5, ci = (xr[k] - xr[k2]) * 0.5;
                        P[m + 1][b] = (float)(cr * cr + ci * ci);
                    }
                }
            }
            int nomF = (int)JRound(nominalMs * 12.0 / HOP);
            int mLo = Math.Max(0, nomF - 11), mHi = Math.Min(nf - 1 - 2 * 84, nomF + 11);
            if (mHi < mLo) return output;
            int nbt = nb - 19;   // le signal occupe 9 tonalités : 8 × 1,736 Hz = 19 bins
            double[] bestR = new double[nbt];
            int[] bestM = new int[nbt];
            const int span = 0;
            for (int b = 0; b < nbt; b++)
            {
                double br0 = -1e9; int bm = mLo;
                for (int m0 = mLo; m0 <= mHi; m0++)
                {
                    double s1 = 0, s2 = 0, tot = 1e-30;
                    for (int k = 0; k < 85; k++)
                    {
                        double p = P[m0 + 2 * k][b + span];
                        tot += p;
                        if (SYNC[k]) s1 += p; else s2 += p;
                    }
                    double rr = (s1 / 16 - s2 / 69) / (tot / 85);
                    if (rr > br0) { br0 = rr; bm = m0; }
                }
                bestR[b] = br0; bestM[b] = bm;
            }
            P = null;
            List<int[]> cands = new List<int[]>();
            bool[] used = new bool[nbt];
            for (int c = 0; c < MAX_CAND; c++)
            {
                int bi0 = -1; double bv = CAND_MIN;
                for (int i = 0; i < nbt; i++) if (!used[i] && bestR[i] > bv) { bv = bestR[i]; bi0 = i; }
                if (bi0 < 0) break;
                for (int i = Math.Max(0, bi0 - 8); i <= Math.Min(nbt - 1, bi0 + 8); i++) used[i] = true;
                cands.Add(new int[] { bi0 + span + bLo, bestM[bi0] });
            }
            foreach (int[] c in cands)
            {
                double f0 = c[0] * 12000.0 / NFFT;   // fréquence (Hz) de la tonalité 0, précision 0,37 Hz
                Msg m = DecodeCandidate(win, n, f0, c[1] * HOP, nominalMs);
                if (m == null) continue;
                bool dup = false;
                foreach (Msg o in output) if (o.Text == m.Text) { dup = true; if (m.Snr > o.Snr) { o.Snr = m.Snr; o.Dt = m.Dt; o.Freq = m.Freq; } }
                if (!dup) output.Add(m);
            }
            return output;
        }

        static Msg DecodeCandidate(short[] win, int n, double f0, int tStart, int nominalMs)
        {
            double fc = f0 + 4 * DF;   // centre des 9 tonalités
            int nbb = n / 16;
            double[] br = new double[nbb], bi = new double[nbb];
            WsjtCore.MixDecimate(win, 0, n, fc, 16, 150, 321, br, bi, nbb);
            int t0 = tStart / 16;
            // affinage sur la tonalité 0 : fréquence ±1 Hz, instant ±0,27 s
            double bf = -4 * DF, bs = -1e9; int bt = t0;
            for (int dt = -216; dt <= 216; dt += 24)
            {
                for (int df = -4; df <= 4; df++)
                {
                    double f = -4 * DF + df * 0.25;
                    int tt = t0 + dt;
                    if (tt < 0 || tt + 85 * BSYM > nbb) continue;
                    double s = SyncScore(br, bi, f, tt);
                    if (s > bs) { bs = s; bf = f; bt = tt; }
                }
            }
            // second affinage plus fin
            double f1 = bf; int t1 = bt;
            for (int dt = -20; dt <= 20; dt += 5)
            {
                for (int df = -2; df <= 2; df++)
                {
                    double f = f1 + df * 0.1;
                    int tt = t1 + dt;
                    if (tt < 0 || tt + 85 * BSYM > nbb) continue;
                    double s = SyncScore(br, bi, f, tt);
                    if (s > bs) { bs = s; bf = f; bt = tt; }
                }
            }
            if (bt < 0 || bt + 85 * BSYM > nbb) return null;
            double[] a = new double[85 * 9];
            Demod(br, bi, bf, bt, a);
            // métriques souples : 3 bits par symbole de données (code de Gray)
            double[] v = new double[207];
            int d = 0;
            for (int k = 0; k < 85; k++)
            {
                if (SYNC[k]) continue;
                double[] best1 = { -1, -1, -1 }, best0 = { -1, -1, -1 };
                for (int b = 0; b < 8; b++)
                {
                    int tone = (b ^ (b >> 1)) + 1;
                    double am = a[9 * k + tone];
                    for (int m = 0; m < 3; m++)
                    {
                        if (((b >> (2 - m)) & 1) == 1) { if (am > best1[m]) best1[m] = am; } else if (am > best0[m]) best0[m] = am;
                    }
                }
                for (int m = 0; m < 3; m++) v[3 * d + m] = best1[m] - best0[m];
                d++;
            }
            double[] dv = new double[206];
            for (int i = 0; i < 206; i++) dv[i] = v[J0[i]];
            int[] sym = WsjtCore.ToBytes(dv, SYMFAC);
            int[] data = new int[103];
            int met = WsjtCore.Fano(sym, 103, 60, MAX_CYCLES, data);
            if (met == int.MinValue) return null;
            int[] enc = WsjtCore.Encode(data, 103);
            int errs = 0;
            for (int i = 0; i < 206; i++) if ((sym[i] > 127 ? 1 : 0) != enc[i]) errs++;
            if (errs > MAX_ERR_FRAC * 206) return null;
            string text = Unpack(data);
            if (text == null) return null;
            // SNR : puissance de la tonalité émise contre le bruit moyen des huit autres
            int[] tx = new int[207];
            for (int i = 0; i < 206; i++) tx[J0[i]] = enc[i];
            double sig = 0, noi = 0;
            d = 0;
            for (int k = 0; k < 85; k++)
            {
                int tone;
                if (SYNC[k]) tone = 0;
                else
                {
                    int b = (tx[3 * d] << 2) | (tx[3 * d + 1] << 1) | tx[3 * d + 2];
                    tone = (b ^ (b >> 1)) + 1;
                    d++;
                }
                double tot = 0;
                for (int j = 0; j < 9; j++) tot += a[9 * k + j] * a[9 * k + j];
                double ps = a[9 * k + tone] * a[9 * k + tone];
                sig += ps;
                noi += (tot - ps) / 8;
            }
            sig /= 85; noi /= 85;
            double pe = Math.Max(sig - noi, noi * 0.01);
            Msg msg = new Msg();
            msg.Text = text;
            msg.Errs = errs;
            msg.Snr = (int)JRound(Math.Max(-35, Math.Min(30, 10 * Math.Log10(pe / noi) - 10 * Math.Log10(2500.0 / DF))));
            msg.Dt = bt / 750.0 - nominalMs / 1000.0;
            msg.Freq = fc + bf;   // fréquence de la tonalité 0 après affinage
            return msg;
        }

        // énergie de la tonalité 0 (fréquence f en Hz dans la bande de base) : (synchro − données) normalisée
        static double SyncScore(double[] br, double[] bi, double f, int t)
        {
            double w = -2 * Math.PI * f / 750.0, wr = Math.Cos(w), wi = Math.Sin(w);
            double s1 = 0, s2 = 0, tot = 1e-30;
            for (int k = 0; k < 85; k++)
            {
                double pr = 1, pi = 0, ar = 0, ai = 0;
                int s = t + k * BSYM;
                for (int i = 0; i < BSYM; i++)
                {
                    double xr = br[s + i], xi = bi[s + i];
                    ar += xr * pr - xi * pi; ai += xr * pi + xi * pr;
                    double nr = pr * wr - pi * wi; pi = pr * wi + pi * wr; pr = nr;
                }
                double p = ar * ar + ai * ai;
                tot += p;
                if (SYNC[k]) s1 += p; else s2 += p;
            }
            return (s1 / 16 - s2 / 69) / (tot / 85);
        }

        static readonly double[] TC = MakeTab(true), TS = MakeTab(false);
        static double[] MakeTab(bool cos)
        {
            double[] a = new double[BSYM];
            for (int i = 0; i < BSYM; i++) a[i] = cos ? Math.Cos(2 * Math.PI * i / BSYM) : -Math.Sin(2 * Math.PI * i / BSYM);
            return a;
        }

        /** Amplitudes des 9 tonalités pour chacun des 85 symboles : a[9k + j]. f0 : fréquence de la tonalité 0 (bande de base). */
        static void Demod(double[] br, double[] bi, double f0, int t, double[] a)
        {
            double w = -2 * Math.PI * f0 / 750.0, wr = Math.Cos(w), wi = Math.Sin(w);
            double[] yr = new double[BSYM], yi = new double[BSYM];
            for (int k = 0; k < 85; k++)
            {
                double pr = 1, pi = 0;
                int s = t + k * BSYM;
                for (int i = 0; i < BSYM; i++)
                {
                    double xr = br[s + i], xi = bi[s + i];
                    yr[i] = xr * pr - xi * pi; yi[i] = xr * pi + xi * pr;
                    double nr = pr * wr - pi * wi; pi = pr * wi + pi * wr; pr = nr;
                }
                for (int j = 0; j < 9; j++)
                {
                    double sr = 0, si = 0;
                    int idx = 0;
                    for (int i = 0; i < BSYM; i++)
                    {
                        sr += yr[i] * TC[idx] - yi[i] * TS[idx];
                        si += yr[i] * TS[idx] + yi[i] * TC[idx];
                        idx += j; if (idx >= BSYM) idx -= BSYM;
                    }
                    a[9 * k + j] = Math.Sqrt(sr * sr + si * si);
                }
            }
        }

        // ------------------------------------------------------------------ message de 72 bits (JT65 / JT9) ; null si non pris en charge ou invalide
        public static string Unpack(int[] bits)
        {
            long nc1 = 0, nc2 = 0, ng = 0;
            for (int i = 0; i < 28; i++) nc1 = (nc1 << 1) + bits[i];
            for (int i = 28; i < 56; i++) nc2 = (nc2 << 1) + bits[i];
            for (int i = 56; i < 72; i++) ng = (ng << 1) + bits[i];
            if (ng >= 32768) return null;   // texte libre : non pris en charge
            string c1, c2, g;
            if (nc1 < NBASE)
            {
                c1 = WsprEngine.CallFrom(nc1);
                if (c1 == null) return null;
            }
            else if (nc1 == NBASE + 1) c1 = "CQ";
            else if (nc1 == NBASE + 2) c1 = "QRZ";
            else if (nc1 == 267796945L) c1 = "DE";
            else if (nc1 >= NBASE + 3 && nc1 <= NBASE + 1002) c1 = "CQ " + (nc1 - NBASE - 3).ToString("000", CultureInfo.InvariantCulture);
            else return null;
            if (nc2 >= NBASE) return null;
            c2 = WsprEngine.CallFrom(nc2);
            if (c2 == null) return null;
            if (ng < 32400) g = WsjtCore.Grid(ng);
            else
            {
                int r = (int)(ng - 32401);
                if (r == 0) g = "";
                else if (r >= 1 && r <= 30) g = "-" + r.ToString("00", CultureInfo.InvariantCulture);
                else if (r >= 31 && r <= 60) g = "R-" + (r - 30).ToString("00", CultureInfo.InvariantCulture);
                else if (r == 61) g = "RO";
                else if (r == 62) g = "RRR";
                else if (r == 63) g = "73";
                else return null;
            }
            return JTrim(c1 + " " + c2 + (g.Length == 0 ? "" : " " + g));
        }
    }
}
