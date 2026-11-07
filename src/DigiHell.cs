using System;

namespace SDRLink
{
    // Décodeur Hellschreiber (Feld Hell, Slow Hell, Hell X5, Hell X9, FM Hell 105 / 245, Hell 80) : le texte n'est pas décodé en lettres, il se lit à l'image,
    // comme sur la machine d'origine. Principe (paramètres de fldigi) : colonnes de 14 points, mode à tonalité manipulée (OOK : Feld Hell, Slow Hell, X5, X9)
    // ou à deux tonalités (FSK : FM Hell 105, FM Hell 245, Hell 80). Le son est ramené autour de zéro par la fréquence trouvée dans le spectre (SpecFind),
    // filtré (4 cellules), puis chaque point est la moyenne de l'enveloppe (OOK) ou de la fréquence instantanée (FSK) sur sa durée. Quatre horloges de point
    // décalées d'un quart de point tournent en parallèle ; on suit celle qui donne les points les plus contrastés. Le rang du point dans la colonne n'est pas
    // connu : les lignes du haut et du bas de la police sont vides, on cherche donc les quatre rangs consécutifs les plus sombres et on les place en marge.
    // Le résultat est une image (lignes de 14 points qui défilent vers le haut) : ImageBegin (une fois, tableau de pixels ARGB partagé) puis ImageChanged.
    // (Portage de HellEngine.java de PhoneSDRLink.)
    public class HellEngine : DigiEngine
    {
        const int RATE = 8000;
        public const int COLH = 14, PXW = 3, PXH = 2, WC = 330, LINES = 5, GAP = 4;
        public const int W = WC * PXW, LH = COLH * PXH + GAP, H = LINES * LH;
        public const int PAPER = unchecked((int)0xFF0E1A26), INK = unchecked((int)0xFFF4F8FF);

        public static readonly string[] NAMES = { "Feld Hell", "Slow Hell", "Hell X5", "Hell X9", "FM Hell 105", "FM Hell 245", "Hell 80" };
        public static readonly double[] COLRATE = { 17.5, 2.1875, 87.5, 157.5, 17.5, 17.5, 35 };      // colonnes par seconde
        public static readonly bool[] FSK = { false, false, false, false, true, true, true };
        public static readonly double[] SHIFT = { 0, 0, 0, 0, 55, 122.5, 300 };                        // écart entre les deux tonalités (Hz) des modes FSK

        // Réglages ; après un changement de Variant : Post(SettingsChanged). ClearReq++ efface l'image.
        public volatile int Variant = 0;
        public volatile int CenterSetting = 0;     // 0 = fréquence trouvée automatiquement, sinon fréquence imposée (Hz)
        public volatile int Sq = 5;                // 0 = pas de squelch, sinon seuil de rapport signal / bruit = 1 + 0,4 × sq
        public volatile bool Invert;
        public volatile int ClearReq;

        readonly int[] px = new int[W * H];
        readonly SpecFind sf = new SpecFind();

        // paramètres du mode choisi
        bool fsk;
        double spp, fb, shift, centre = 1000, ph, dph, al;
        double decay, flUp;
        // filtre et mesures
        readonly double[] zr = new double[4], zi = new double[4];
        double pr, pi;
        readonly double[] acc = new double[4], accA = new double[4], phs = new double[4];
        readonly int[] cnt = new int[4];
        readonly double[] pk = new double[4], fl = new double[4], mean = new double[4], var = new double[4];
        int sel, nPix, k, k0;
        readonly double[] energy = new double[COLH];
        // image
        int cx = -1, li;
        bool open, gapNew = true, dirty, started;
        long total, closeAt, since;
        int seenClear;
        double cand, ratioNow;
        int candHits;
        string lastStatus = "";

        public HellEngine()
        {
            ClearAll();
            Setup();
            // (l'état « recherche d'un signal » est envoyé au premier morceau de son : personne n'écoute encore dans le constructeur)
        }

        public override int Rate { get { return RATE; } }

        /** Pixels de l'image (W × H, ARGB), remplis dans le fil du décodeur. */
        public int[] Pixels { get { return px; } }

        string Name() { return NAMES[Math.Max(0, Math.Min(NAMES.Length - 1, Variant))]; }

        void SetStatus(string s)
        {
            string t = Name() + " : " + s;
            if (t != lastStatus) { lastStatus = t; RaiseStatus(t); }
        }

        static long JRound(double x) { return (long)Math.Floor(x + 0.5); }   // Math.round de Java

        // Appelé dans le fil du décodeur quand un réglage change.
        public void SettingsChanged()
        {
            Setup();
            SetStatus(Lang.T("recherche d'un signal…", "searching for a signal…"));
        }

        void Setup()
        {
            int v = Math.Max(0, Math.Min(NAMES.Length - 1, Variant));
            fsk = FSK[v];
            shift = SHIFT[v];
            double pixRate = COLH * COLRATE[v];
            spp = RATE / pixRate;
            double tx = pixRate;
            fb = fsk ? 5 * JRound(4.0 * shift / 5.0) : 5 * JRound(1.2 * tx / 5.0);       // largeur du filtre
            // 4 cellules du premier ordre en cascade, -3 dB à fb/2
            double fc1 = (fb / 2) / 0.435;
            al = 1 - Math.Exp(-2 * Math.PI * fc1 / RATE);
            decay = Math.Exp(-1.0 / (pixRate * 4.0));
            flUp = 1 - Math.Exp(-1.0 / (pixRate * 12.0));
            for (int m = 0; m < 4; m++) { phs[m] = m * spp / 4; acc[m] = accA[m] = 0; cnt[m] = 0; pk[m] = 0; fl[m] = 0; mean[m] = 0; var[m] = 0; }
            for (int i = 0; i < 4; i++) { zr[i] = 0; zi[i] = 0; }
            pr = 1; pi = 0;
            sel = 0; nPix = 0; k = 0; k0 = 0;
            for (int i = 0; i < COLH; i++) energy[i] = 0;
            int cs = CenterSetting;
            if (cs > 0) centre = cs;
            dph = 2 * Math.PI * centre / RATE;
            candHits = 0; cand = 0;
            gapNew = true;
        }

        void ClearAll()
        {
            for (int i = 0; i < px.Length; i++) px[i] = PAPER;
            cx = -1; li = 0; gapNew = true; dirty = true;
        }

        void NewLine()
        {
            if (li < LINES - 1) li++;
            else
            {
                Array.Copy(px, LH * W, px, 0, (LINES - 1) * LH * W);
                for (int i = (LINES - 1) * LH * W; i < px.Length; i++) px[i] = PAPER;
            }
            cx = -1;
        }

        protected override void FeedShort(short[] s, int n)
        {
            if (!started) { started = true; SetStatus(Lang.T("recherche d'un signal…", "searching for a signal…")); RaiseImageBegin(W, H, px); }
            int cr = ClearReq;
            if (seenClear != cr) { seenClear = cr; ClearAll(); }
            sf.Add(s, n);
            int cs = CenterSetting;
            if (cs > 0 && Math.Abs(cs - centre) > 0.5) { centre = cs; dph = 2 * Math.PI * centre / RATE; }
            for (int i = 0; i < n; i++)
            {
                double x = s[i] / 32768.0;
                double c = Math.Cos(ph), sn = Math.Sin(ph);
                ph += dph;
                if (ph > 2 * Math.PI) ph -= 2 * Math.PI;
                double a = x * c, b = -x * sn;
                for (int j = 0; j < 4; j++)
                {
                    zr[j] += al * (a - zr[j]); zi[j] += al * (b - zi[j]);
                    a = zr[j]; b = zi[j];
                }
                double amp = Math.Sqrt(a * a + b * b), v = amp;
                if (fsk)
                {
                    v = Math.Atan2(pr * b - pi * a, pr * a + pi * b) * RATE / (2 * Math.PI);
                    pr = a; pi = b;
                }
                for (int m = 0; m < 4; m++)
                {
                    acc[m] += v; accA[m] += amp; cnt[m]++;
                    phs[m] += 1;
                    if (phs[m] >= spp)
                    {
                        phs[m] -= spp;
                        Pixel(m, acc[m] / cnt[m], accA[m] / cnt[m]);
                        acc[m] = 0; accA[m] = 0; cnt[m] = 0;
                    }
                }
            }
            total += n;
            since += n;
            if (since >= 2000) { since = 0; Control(); }
            if (dirty) { dirty = false; RaiseImageChanged(); }   // (l'affichage limite lui-même la cadence)
        }

        // Un point terminé sur l'horloge m : v = moyenne de l'enveloppe (OOK) ou de la fréquence en Hz (FSK), a = amplitude moyenne.
        void Pixel(int m, double v, double a)
        {
            double ink;
            if (!fsk)
            {
                pk[m] = Math.Max(v, pk[m] * decay);
                fl[m] += (v - fl[m]) * (v < fl[m] ? 0.05 : flUp);
                ink = pk[m] > fl[m] * 1.001 ? (v - fl[m]) / (pk[m] - fl[m]) : 0;
            }
            else
            {
                double d = v / (shift / 2.0);
                ink = 0.5 - 0.5 * Math.Max(-1, Math.Min(1, d));
                if (Invert) ink = 1 - ink;
            }
            ink = Math.Max(0, Math.Min(1, ink));
            double dv = ink - mean[m];
            mean[m] += dv * 0.01;
            var[m] += (dv * dv - var[m]) * 0.01;
            if (m != sel) return;
            if (++nPix % 64 == 0)
            {
                int best = sel;
                for (int j = 0; j < 4; j++) if (var[j] > var[best]) best = j;
                if (best != sel && var[best] > var[sel] * 1.15) sel = best;
            }
            Emit(ink);
        }

        void Emit(double ink)
        {
            if (open) energy[k] += (ink - energy[k]) * 0.02;
            int j = (k - k0 + COLH) % COLH;
            if (open)
            {
                if (j == 0 || cx < 0)
                {
                    if (gapNew) { if (cx >= 0) NewLine(); gapNew = false; }
                    cx++;
                    if (cx >= WC) { NewLine(); cx = 0; }
                }
                DrawPixel(cx, COLH - 1 - j, ink);
            }
            k++;
            if (k >= COLH) { k = 0; SyncRows(); }
        }

        // Les quatre rangs consécutifs (circulaires) les moins encrés sont les marges de la police : ils doivent être répartis en haut et en bas.
        void SyncRows()
        {
            int cur = (k0 + COLH - 2) % COLH;
            double curS = 0, bestS = 1e9;
            int bs = cur;
            for (int s = 0; s < COLH; s++)
            {
                double t = 0;
                for (int i = 0; i < 4; i++) t += energy[(s + i) % COLH];
                if (s == cur) curS = t;
                if (t < bestS) { bestS = t; bs = s; }
            }
            if (bs != cur && bestS < curS * 0.8) k0 = (bs + 2) % COLH;
        }

        void DrawPixel(int col, int row, double ink)
        {
            if (cx >= WC) return;
            int ir = (int)(((PAPER >> 16) & 255) + ink * (((INK >> 16) & 255) - ((PAPER >> 16) & 255)));
            int ig = (int)(((PAPER >> 8) & 255) + ink * (((INK >> 8) & 255) - ((PAPER >> 8) & 255)));
            int ib = (int)((PAPER & 255) + ink * ((INK & 255) - (PAPER & 255)));
            int color = unchecked((int)0xFF000000) | (ir << 16) | (ig << 8) | ib;
            int x0 = col * PXW, y0 = li * LH + row * PXH;
            for (int y = 0; y < PXH; y++) for (int x = 0; x < PXW; x++) px[(y0 + y) * W + x0 + x] = color;
            dirty = true;
        }

        // Toutes les 0,25 s : recherche de la fréquence, squelch, état.
        void Control()
        {
            int centerSetting = CenterSetting, sq = Sq;
            double w = fsk ? shift + 60 : fb;
            bool wide = w > 1200;   // X5, X9 : la bande du filtre couvre presque tout le son, on suit simplement la porteuse la plus forte
            if (centerSetting == 0 && (!open || wide))
            {
                double[] b = wide ? new double[] { 1500, 9 } : sf.Best(w);
                if (b[0] > 0 && b[1] >= 2.0)
                {
                    double c = wide ? sf.Peak(SpecFind.LOW, SpecFind.HIGH) : fsk ? b[0] : sf.Peak(b[0] - w / 2, b[0] + w / 2);
                    if (Math.Abs(c - centre) > (fsk ? 6 : 4))
                    {
                        if (cand != 0 && Math.Abs(c - cand) < 8) candHits++; else { cand = c; candHits = 1; }
                        if (candHits >= 2) { centre = c; dph = 2 * Math.PI * centre / RATE; cand = 0; candHits = 0; }
                    }
                    else { cand = 0; candHits = 0; }
                }
            }
            ratioNow = sf.Ratio(centre, w, 0.75 * w);
            double thr = 1 + 0.4 * sq;
            bool good = sq == 0 || wide || ratioNow >= thr;   // (X9 : la bande du filtre couvre presque tout le son, pas de flancs pour comparer)
            if (good) closeAt = total + 3L * RATE;
            bool now = sq == 0 || total < closeAt;
            if (now && !open) { open = true; if (cx >= 0) gapNew = true; }
            else if (!now && open) { open = false; }
            if (open) SetStatus(Lang.F("réception à {0:F0} Hz (S/B {1:F0} dB)", "receiving at {0:F0} Hz (S/N {1:F0} dB)", centre, 10 * Math.Log10(Math.Max(ratioNow, 1e-3))));
            else SetStatus(Lang.F("recherche d'un signal… ({0:F0} Hz)", "searching for a signal… ({0:F0} Hz)", centre));
        }
    }
}
