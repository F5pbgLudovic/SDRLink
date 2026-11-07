using System;
using System.Globalization;

namespace SDRLink
{
    // Décodeur WEFAX (fac-similé météo HF, ou FAX radioamateur) : modulation de fréquence, noir 1500 Hz, blanc 2300 Hz (centre 1900 Hz,
    // le poste en USB 1,9 kHz sous la fréquence publiée). Lignes à 60, 90, 120 ou 240 par minute ; largeur = IOC × π points (IOC 576 :
    // 1810 points, IOC 288 : 905 points). Ton de début (APT) : le signal bascule noir / blanc 300 fois par seconde (IOC 576) ou 675 fois
    // (IOC 288) pendant 5 s ; puis 30 s de lignes de calage (une impulsion de 5 % de la ligne repère le début des lignes) ; ton de fin :
    // 450 bascules par seconde. Le son arrive à 8000 Hz.
    public class FaxEngine : DigiEngine
    {
        public override int Rate { get { return 8000; } }
        const int FS = 8000;

        // Réglages (appliqués tout de suite)
        public volatile int Lpm = 120;          // lignes par minute
        public volatile int Ioc = 576;          // indice de coopération (288 ou 576)
        public volatile bool AutoStart = true;  // attendre le ton de début ; sinon l'image commence tout de suite
        public volatile bool AutoStop = true;   // le ton de fin termine l'image
        public volatile bool AutoPhase = true;  // calage du début des lignes sur les lignes de calage
        public volatile int Center = 1900;      // fréquence du milieu (gris), Hz ; noir = Center − 400, blanc = Center + 400
        public volatile int Slant = 0;          // correction de pente, millionièmes (ppm) de la durée d'une ligne
        public volatile int StartReq, StopReq;  // demandes de l'interface (« commencer maintenant », « terminer et enregistrer »)
        int startSeen, stopSeen;

        public static readonly int[] LPMS = { 60, 90, 120, 240 };
        public static readonly int[] IOCS = { 288, 576 };

        // Démodulation : mélange complexe avec Center, passe-bas FIR, écart de phase d'un échantillon au suivant.
        const int NT = 31;
        readonly double[] lpf = new double[NT];
        readonly double[] bi = new double[NT], bq = new double[NT];
        int bp;
        double ph, pi0 = 1, pq0;
        double level;                           // niveau du signal (moyenne lente de |z|²)

        // Détection des tons APT : Goertzel sur la luminance (centrée) par blocs de 0,25 s.
        const int ABLK = 2000;
        readonly float[] ablk = new float[ABLK];
        int an;
        int runStart576, runStart288, runStop, gapAfterStart;
        int pendingIoc;                         // ton de début entendu (IOC), l'image commence à la fin du ton

        // Image en cours
        bool active;
        int imgIoc, imgLpm, W, H, rowsDone;
        int[] px;
        byte[] lum = new byte[FS * 60];         // luminance depuis le début de l'image (0 noir … 255 blanc)
        int nLum;
        double origin;                          // début de la première ligne (en échantillons depuis le début de l'image)
        bool phased, viaApt;
        const int MAXROWS = 4000;
        string lastStatus = "";

        public FaxEngine()
        {
            double fc = 1100.0 / FS, sum = 0;
            for (int i = 0; i < NT; i++)
            {
                double x = i - (NT - 1) / 2.0;
                double s = x == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
                lpf[i] = s * (0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (NT - 1)));
                sum += lpf[i];
            }
            for (int i = 0; i < NT; i++) lpf[i] /= sum;
        }

        public bool Active { get { return active; } }

        void SetStatus(string s) { if (s != lastStatus) { lastStatus = s; RaiseStatus(s); } }

        string Waiting()
        {
            return AutoStart ? Lang.T("WEFAX : attente du ton de début", "WEFAX: waiting for the start tone")
                             : Lang.T("WEFAX : en écoute", "WEFAX: listening");
        }

        protected override void FeedShort(short[] s, int n)
        {
            if (startSeen != StartReq) { startSeen = StartReq; if (active) EndImage(false); BeginImage(Ioc, false); }
            if (stopSeen != StopReq) { stopSeen = StopReq; if (active) EndImage(false); }
            if (!active && !AutoStart && pendingIoc == 0 && runStop == 0) BeginImage(Ioc, false);   // (pas pendant un ton de fin)
            double w = 2 * Math.PI * Center / FS;
            double dev = 400.0;
            for (int k = 0; k < n; k++)
            {
                double x = s[k] / 32768.0;
                ph += w; if (ph > 2 * Math.PI) ph -= 2 * Math.PI;
                bi[bp] = x * Math.Cos(ph); bq[bp] = -x * Math.Sin(ph);
                double zi = 0, zq = 0;
                for (int j = 0, idx = bp; j < NT; j++) { zi += lpf[j] * bi[idx]; zq += lpf[j] * bq[idx]; if (--idx < 0) idx = NT - 1; }
                if (++bp == NT) bp = 0;
                // écart de phase : z × conj(z précédent)
                double re = zi * pi0 + zq * pq0, im = zq * pi0 - zi * pq0;
                pi0 = zi; pq0 = zq;
                double f = Math.Atan2(im, re) * FS / (2 * Math.PI);    // écart à Center, Hz
                double v = 0.5 + f / (2 * dev);
                if (v < 0) v = 0; else if (v > 1) v = 1;
                level = level * 0.999 + (zi * zi + zq * zq) * 0.001;
                ablk[an++] = (float)(v - 0.5);
                if (an == ABLK) { an = 0; Apt(); }
                if (active) AddLum((byte)(v * 255 + 0.5));
            }
            if (active) Render();
        }

        // Tons APT sur le bloc de luminance : part de l'énergie à 300, 450, 675 Hz.
        void Apt()
        {
            double tot = 0, mean = 0;
            for (int i = 0; i < ABLK; i++) mean += ablk[i];
            mean /= ABLK;
            for (int i = 0; i < ABLK; i++) { ablk[i] -= (float)mean; tot += ablk[i] * ablk[i]; }
            if (tot < 1e-3) { runStart576 = runStart288 = runStop = 0; return; }
            double norm = tot * ABLK / 2.0;
            double r300 = Goertzel(ablk, 0, ABLK, 300, FS) / norm, r675 = Goertzel(ablk, 0, ABLK, 675, FS) / norm, r450 = Goertzel(ablk, 0, ABLK, 450, FS) / norm;
            const double TH = 0.35;
            runStart576 = r300 > TH ? runStart576 + 1 : 0;
            runStart288 = r675 > TH ? runStart288 + 1 : 0;
            runStop = r450 > TH ? runStop + 1 : 0;
            if (AutoStart && (runStart576 >= 6 || runStart288 >= 6))   // 1,5 s de ton de début
            {
                int ioc = runStart576 >= 6 ? 576 : 288;
                if (active && rowsDone > 20) EndImage(false);            // une nouvelle image commence : la précédente est gardée
                if (!active || pendingIoc != ioc)
                {
                    if (active) { active = false; }
                    pendingIoc = ioc;
                    SetStatus(Lang.F("WEFAX : ton de début (IOC {0})", "WEFAX: start tone (IOC {0})", ioc));
                }
                gapAfterStart = 0;
            }
            else if (pendingIoc != 0 && runStart576 == 0 && runStart288 == 0)
            {
                if (++gapAfterStart >= 1) { int ioc = pendingIoc; pendingIoc = 0; BeginImage(ioc, true); }
            }
            if (active && AutoStop && runStop >= 8 && rowsDone > 0)        // 2 s de ton de fin
            {
                SetStatus(Lang.T("WEFAX : ton de fin", "WEFAX: stop tone"));
                EndImage(true);
            }
        }

        void BeginImage(int ioc, bool apt)
        {
            imgIoc = ioc; imgLpm = Lpm;
            W = (int)Math.Round(ioc * Math.PI);
            H = 400;
            px = new int[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = unchecked((int)0xFF202020);
            nLum = 0; rowsDone = 0; origin = 0;
            phased = !apt || !AutoPhase; viaApt = apt;
            active = true;
            runStop = 0;
            RaiseImageBegin(W, H, px);
            SetStatus(Lang.F("WEFAX : image {0} lignes/min, IOC {1}", "WEFAX: picture {0} lines/min, IOC {1}", imgLpm, imgIoc));
        }

        void EndImage(bool complete)
        {
            if (!active) return;
            active = false;
            Render();
            int rows = rowsDone;
            RaiseImageEnd(complete, rows);
            SetStatus(Waiting());
        }

        protected override void OnClose() { }

        // Arrêt du décodeur : l'image en cours est terminée (pour l'enregistrer).
        public void Finish() { Post(delegate { if (active) EndImage(false); }); }

        void AddLum(byte b)
        {
            if (nLum == lum.Length)
            {
                if (lum.Length >= FS * 60 * 40) { EndImage(false); return; }   // 40 minutes : on arrête
                Array.Resize(ref lum, lum.Length * 2);
            }
            lum[nLum++] = b;
        }

        double LineLen { get { return FS * 60.0 / imgLpm * (1 + Slant * 1e-6); } }

        // Lignes complètes à dessiner ; calage sur les lignes de calage après 12 lignes.
        void Render()
        {
            double L = LineLen;
            if (!phased && nLum >= (int)(L * 12)) { Phase(L); phased = true; RedrawAll(); }
            int avail = (int)Math.Floor((nLum - origin) / L);
            if (avail > MAXROWS) avail = MAXROWS;
            if (avail <= rowsDone) return;
            if (avail > H) Grow(avail);
            for (int r = rowsDone; r < avail; r++) DrawRow(r, L);
            rowsDone = avail;
            RaiseImageRows(rowsDone);
            if (rowsDone % 10 == 0)
                SetStatus(Lang.F("WEFAX : image {0} lignes/min, IOC {1}, ligne {2}", "WEFAX: picture {0} lines/min, IOC {1}, line {2}", imgLpm, imgIoc, rowsDone));
            if (rowsDone >= MAXROWS) EndImage(false);
        }

        void Grow(int need)
        {
            int nh = H;
            while (nh < need) nh *= 2;
            if (nh > MAXROWS) nh = MAXROWS;
            int[] np = new int[W * nh];
            Array.Copy(px, np, px.Length);
            for (int i = px.Length; i < np.Length; i++) np[i] = unchecked((int)0xFF202020);
            px = np; H = nh;
            RaiseImageBegin(W, H, px);
        }

        void DrawRow(int r, double L)
        {
            double start = origin + r * L, step = L / W;
            int o = r * W;
            for (int x = 0; x < W; x++)
            {
                double a = start + x * step, b = a + step;
                int ia = (int)a, ib = (int)b;
                double sum = 0, wsum = 0;
                for (int i = ia; i <= ib && i < nLum; i++)
                {
                    if (i < 0) continue;
                    double lo = Math.Max(a, i), hi = Math.Min(b, i + 1);
                    if (hi <= lo) continue;
                    sum += lum[i] * (hi - lo); wsum += hi - lo;
                }
                int g = wsum > 0 ? (int)(sum / wsum + 0.5) : 32;
                px[o + x] = unchecked((int)0xFF000000) | (g << 16) | (g << 8) | g;
            }
        }

        void RedrawAll()
        {
            double L = LineLen;
            int avail = Math.Min((int)Math.Floor((nLum - origin) / L), Math.Min(rowsDone, H));
            for (int r = 0; r < avail; r++) DrawRow(r, L);
            for (int i = avail * W; i < rowsDone * W && i < px.Length; i++) px[i] = unchecked((int)0xFF202020);
            rowsDone = Math.Max(0, avail);
            RaiseImageBegin(W, H, px);
            RaiseImageRows(rowsDone);
        }

        // Lignes de calage : une impulsion de 5 % de la ligne, de teinte opposée au reste. On cherche, sur les lignes 2 à 11,
        // la position de la fenêtre de 5 % qui s'écarte le plus de la moyenne de la ligne ; les lignes commencent à cette impulsion.
        void Phase(double L)
        {
            const int B = 400;
            double[] acc = new double[B];
            int[] cnt = new int[B];
            for (int r = 2; r < 12; r++)
            {
                int a = (int)(r * L), b = (int)((r + 1) * L);
                for (int i = a; i < b && i < nLum; i++)
                {
                    int k = (int)((i - r * L) / L * B);
                    if (k < 0 || k >= B) continue;
                    acc[k] += lum[i]; cnt[k]++;
                }
            }
            double tot = 0; int tc = 0;
            for (int k = 0; k < B; k++) if (cnt[k] > 0) { acc[k] /= cnt[k]; tot += acc[k]; tc++; }
            if (tc < B / 2) return;
            double mean = tot / tc;
            int win = B / 20, best = 0;
            double bestDev = 0;
            for (int k = 0; k < B; k++)
            {
                double s = 0;
                for (int j = 0; j < win; j++) s += acc[(k + j) % B];
                double d = Math.Abs(s / win - mean);
                if (d > bestDev) { bestDev = d; best = k; }
            }
            if (bestDev < 40) return;   // pas de lignes de calage nettes (image prise en route) : pas de décalage
            origin = best * L / B;
        }

        public static string Info(int lpm, int ioc)
        {
            return string.Format(CultureInfo.InvariantCulture, "WEFAX {0}/{1}", lpm, ioc);
        }
    }
}
