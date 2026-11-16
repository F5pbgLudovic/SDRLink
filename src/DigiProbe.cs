using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SDRLink
{
    // Recherche du mode écouté (bouton « Mode ? », depuis la 1.38) : pendant au plus Seconds secondes, le son décodé est envoyé en même
    // temps à plusieurs décodeurs (FT8, FT4, FT2, JS8, SSTV, WEFAX, RTTY, PSK31, CW, Olivia). Les modes à contrôle d'erreur
    // (message FT8/FT4/FT2/JS8 reçu, en-tête VIS SSTV, ton de début WEFAX) donnent une réponse sûre et arrêtent la recherche tout de suite.
    // Les modes en texte (RTTY, PSK, CW, Olivia) sont notés d'après leur texte : indicatifs, CQ, DE, 73, RST… ; le meilleur l'emporte s'il
    // dépasse un seuil. Les décodeurs ne tournent que le temps de la recherche, puis sont arrêtés.
    //
    // Plusieurs signaux dans la bande passante (depuis la 1.39) :
    // - les pics nets du spectre (au plus MaxFocus) reçoivent chacun leurs propres décodeurs CW, PSK et RTTY réglés sur leur fréquence
    //   (sans cela, chaque décodeur ne suit que le signal le plus fort, et un mélange ne donne aucun texte) ; les 6 dernières secondes
    //   de son leur sont rejouées ;
    // - le rythme des tranches FT8 (15 s) ou FT4 (7,5 s) est reconnu sur le spectre, quel que soit le nombre de signaux : les émissions
    //   s'arrêtent toutes ensemble environ 2,4 s avant la tranche suivante. La place de ce silence donne le retard du son du WebSDR ;
    //   il est donné au décodeur, qui relit aussitôt la dernière tranche (un retard de plus de 2 s empêchait tout décodage).
    public sealed class ModeProbe
    {
        public const int Seconds = 33;
        public const int MaxFocus = 4;

        /** Horloge (ms UTC) ; remplaçable pour les essais. */
        public Func<long> NowMs = Ft8Engine.UtcNowMs;

        sealed class Cand
        {
            public string Mode;
            public DigiEngine E;
            public SstvEngine S;
            public Resampler Rs;           // null : son à 8000 Hz commun (décodeurs centrés sur un pic)
            public int Hz;                 // fréquence du pic suivi (0 : décodeur libre)
            public readonly StringBuilder Text = new StringBuilder();
            public int Msgs;
        }

        readonly List<Cand> cands = new List<Cand>();
        readonly object gate = new object();
        string sure, sureDetail;
        volatile bool stopped;

        // Son commun à 8000 Hz : spectre, mémoire de 6 s (rejouée aux décodeurs centrés), heure UTC de chaque échantillon.
        const int R8 = 8000;
        readonly Resampler rs8 = new Resampler(R8);
        readonly SpecFind spec = new SpecFind();
        readonly float[] mem = new float[R8 * 6];
        long n8, anchorMs, nextLook, hopN = SpecFind.N - SpecFind.HOP;
        bool anchored;
        readonly List<double> focusHz = new List<double>();

        // Rythme des tranches : (heure UTC en ms, « relief » du spectre = moyenne / médiane des cases 400-2600 Hz).
        readonly List<long> rT = new List<long>();
        readonly List<float> rV = new List<float>();
        double[] sortBuf;
        string rhythm, rhythmDetail;
        double rhythmShift;

        // Réponse sûre (fil d'un décodeur) : mode et détail ; l'interface repasse dans son fil.
        public event Action<string, string> Sure;

        public ModeProbe(int js8Speed)
        {
            Add("FT8", new Ft8Engine(0), 0, true);
            Add("FT4", new Ft8Engine(1), 0, true);
            Add("FT2", new Ft8Engine(2), 0, true);
            Add("JS8", new Js8Engine(Math.Max(0, Math.Min(4, js8Speed))), 0, true);
            Add("RTTY", new RttyEngine(), 0, true);
            Add("PSK", new PskEngine(), 0, true);
            Add("CW", new CwEngine(), 0, true);
            Add("Olivia", new OliviaEngine(false), 0, true);
            Add("WEFAX", new FaxEngine(), 0, true);
            Cand s = new Cand { Mode = "SSTV", S = new SstvEngine() };
            s.S.ImageBegin += delegate { Hit(s, Lang.T("en-tête VIS reçu", "VIS header received")); };
            cands.Add(s);
            spec.Spectrum = OnSpectrum;
        }

        Cand Add(string mode, DigiEngine e, int hz, bool own)
        {
            Cand c = new Cand { Mode = mode, E = e, Rs = own ? new Resampler(e.Rate) : null, Hz = hz };
            if (mode == "WEFAX") e.ImageBegin += delegate { Hit(c, Lang.T("ton de début reçu", "start tone received")); };
            else
            {
                e.Lines += delegate(List<string> l, int parity)
                {
                    if (l == null || l.Count == 0) return;
                    lock (gate) c.Msgs += l.Count;
                    Hit(c, Lang.F("{0} message(s)", "{0} message(s)", c.Msgs));
                };
                e.Text += delegate(string t) { lock (gate) { if (c.Text.Length < 4000) c.Text.Append(t); } };
            }
            lock (gate) cands.Add(c);
            return c;
        }

        void Hit(Cand c, string detail)
        {
            Action<string, string> h;
            lock (gate)
            {
                if (sure != null || stopped) return;
                sure = c.Mode; sureDetail = detail;
                h = Sure;
            }
            if (h != null) h(c.Mode, detail);
        }

        // Son de la réception (fil de capture).
        public void Feed(int rate, float[] x, int n)
        {
            if (stopped) return;
            long now = NowMs();
            float[] y8 = rs8.Process(rate, x, n);
            List<Cand> l;
            lock (gate) l = new List<Cand>(cands);
            foreach (Cand c in l)
            {
                if (c.S != null) { c.S.Feed(rate, x, n); continue; }
                if (c.Rs == null) { if (y8.Length > 0) c.E.Feed(y8, y8.Length); continue; }
                float[] y;
                lock (c.Rs) y = c.Rs.Process(rate, x, n);
                if (y.Length > 0) c.E.Feed(y, y.Length);
            }
            if (y8.Length == 0) return;
            // Heure de chaque échantillon (comme Ft8Engine : recalée si le son s'est arrêté plus de 2,5 s).
            if (!anchored) { anchorMs = now - y8.Length * 1000L / R8; anchored = true; }
            else
            {
                long expected = anchorMs + (n8 + y8.Length) * 1000L / R8;
                if (now - expected > 2500) anchorMs += now - expected;
            }
            short[] s = new short[y8.Length];
            for (int i = 0; i < y8.Length; i++)
            {
                mem[(int)(n8 % mem.Length)] = y8[i];
                n8++;
                double v = y8[i] * 32767.0;
                s[i] = (short)(v > 32767 ? 32767 : v < -32768 ? -32768 : v);
            }
            spec.Add(s, s.Length);
            if (n8 >= nextLook && n8 >= 3 * R8)
            {
                nextLook = n8 + R8;   // une fois par seconde
                Look();
            }
        }

        // Spectre instantané (toutes les 64 ms) : relief = moyenne / médiane entre 400 et 2600 Hz (bruit seul : environ 1,4 ;
        // des signaux FT8 : bien plus). Indifférent à l'AGC du WebSDR, qui remonte le bruit dans les silences.
        void OnSpectrum(double[] p)
        {
            int b0 = (int)(400 / SpecFind.HZ), b1 = (int)(2600 / SpecFind.HZ);
            int len = b1 - b0;
            if (sortBuf == null) sortBuf = new double[len];
            double sum = 0;
            for (int i = 0; i < len; i++) { sortBuf[i] = p[b0 + i]; sum += p[b0 + i]; }
            Array.Sort(sortBuf);
            double med = sortBuf[len / 2];
            if (med <= 0) return;
            hopN += SpecFind.HOP;   // SpecFind calcule un spectre tous les HOP échantillons, le premier quand il en a N
            long t = anchorMs + (hopN - SpecFind.N / 2) * 1000L / R8;   // milieu de la fenêtre de la FFT
            rT.Add(t);
            rV.Add((float)(sum / len / med));
            if (rT.Count > 1500) { rT.RemoveRange(0, 300); rV.RemoveRange(0, 300); }
        }

        // Une fois par seconde : nouveaux pics à suivre, rythme des tranches.
        void Look()
        {
            if (focusHz.Count < MaxFocus)
                foreach (double f in spec.Peaks(MaxFocus * 2, 250, 2800, 10, 60))
                {
                    if (focusHz.Count >= MaxFocus) break;
                    bool known = false;
                    foreach (double g in focusHz) if (Math.Abs(g - f) < 40) { known = true; break; }
                    if (known) continue;
                    focusHz.Add(f);
                    Focus(f);
                }
            if (rhythm == null)
            {
                string m = null; double sh = 0;
                if (Rhythm(7500, 3, 6770, 5500, out sh)) m = "FT4";
                else if (Rhythm(15000, 2, 14320, 13000, out sh)) m = "FT8";
                if (m != null)
                {
                    lock (gate) { rhythm = m; rhythmShift = sh; rhythmDetail = Lang.F("rythme des tranches {0} reconnu, retard du son {1} s", "{0} slot rhythm recognised, sound delay {1} s", m, sh.ToString("0.0", CultureInfo.InvariantCulture)); }
                    Log.Write("Recherche du mode : rythme " + m + " reconnu, retard " + sh.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                    foreach (Cand c in Snapshot())
                    {
                        Ft8Engine fe = c.E as Ft8Engine;
                        if (fe != null && c.Mode == m && c.Msgs == 0) fe.SetShift(sh);
                    }
                }
            }
        }

        List<Cand> Snapshot() { lock (gate) return new List<Cand>(cands); }

        // Décodeurs CW, PSK et RTTY centrés sur le pic f, nourris d'abord des 6 dernières secondes.
        void Focus(double f)
        {
            int hz = (int)Math.Round(f);
            List<Cand> added = new List<Cand>();
            CwEngine cw = new CwEngine { AutoTone = false, ManualTone = hz };
            added.Add(Add("CW", cw, hz, false));
            PskEngine psk = new PskEngine { AutoFreq = false, ManualFreq = hz };
            added.Add(Add("PSK", psk, hz, false));
            // RTTY : le pic peut être la tonalité haute d'une paire déjà suivie ; on ne le prend comme tonalité basse que sinon.
            bool high = false;
            foreach (double g in focusHz)
                foreach (double sft in RttyEngine.SHIFTS)
                    if (Math.Abs(f - g - sft) < 15) high = true;
            if (!high) added.Add(Add("RTTY", new RttyEngine { FreqSetting = hz }, hz, false));
            int have = (int)Math.Min(n8, mem.Length);
            float[] past = new float[have];
            for (int i = 0; i < have; i++) past[i] = mem[(int)((n8 - have + i) % mem.Length)];
            foreach (Cand c in added) c.E.Feed(past, have);
        }

        // Rythme d'une tranche de slotMs : silence de 1,5 s au même endroit de chaque tranche, alors que le reste a du relief.
        // nominal = milieu du silence attendu sans retard (ms dans la tranche) ; au-delà de wrap, le retard est compté négatif.
        bool Rhythm(int slotMs, int minGaps, int nominal, int wrap, out double shift)
        {
            shift = 0;
            int cnt = rT.Count;
            if (cnt < 100) return false;
            long t0 = rT[0], t1 = rT[cnt - 1];
            if (t1 - t0 < (minGaps - 1) * slotMs + 3000) return false;
            const int BIN = 250, W = 6;
            int nb = slotMs / BIN;
            double[] sum = new double[nb];
            int[] num = new int[nb];
            for (int i = 0; i < cnt; i++) { int b = (int)(FloorMod(rT[i], slotMs) / BIN); sum[b] += rV[i]; num[b]++; }
            for (int b = 0; b < nb; b++) if (num[b] == 0) return false;
            int g = -1; double gv = double.MaxValue;
            for (int b = 0; b < nb; b++)
            {
                double v = 0;
                for (int k = 0; k < W; k++) v += sum[(b + k) % nb] / num[(b + k) % nb];
                if (v / W < gv) { gv = v / W; g = b; }
            }
            double rs = 0; int rn = 0;
            for (int b = 0; b < nb; b++)
            {
                int d = ((b - g) % nb + nb) % nb;
                if (d >= W + 2 && d < nb - 2) { rs += sum[b] / num[b]; rn++; }
            }
            if (rn == 0) return false;
            double rest = rs / rn;
            const double NOISE = 1.44;   // moyenne / médiane d'un bruit seul
            if (rest < 2.2 || gv - NOISE > 0.3 * (rest - NOISE)) return false;
            // Pas de second silence à mi-tranche (sinon c'est le rythme d'une tranche deux fois plus courte : FT4 pour FT8, FT2 pour FT4).
            double hv = 0;
            for (int k = 0; k < W; k++) { int b = (g + nb / 2 + k) % nb; hv += sum[b] / num[b]; }
            if (hv / W - NOISE < 0.6 * (rest - NOISE)) return false;
            // Chaque tranche vue en entier doit avoir ce silence (une pause de hasard ne suffit pas).
            long gStart = g * BIN, gEnd = gStart + W * BIN;
            Dictionary<long, double[]> per = new Dictionary<long, double[]>();   // tranche → { somme silence, nb, somme reste, nb }
            for (int i = 0; i < cnt; i++)
            {
                long rel = rT[i] - gStart;
                long k = FloorDiv(rel, slotMs);
                long inS = rel - k * slotMs;
                double[] a;
                if (!per.TryGetValue(k, out a)) { a = new double[4]; per[k] = a; }
                if (inS < W * BIN) { a[0] += rV[i]; a[1]++; }
                else if (inS >= (W + 2) * BIN && inS < slotMs - 2 * BIN) { a[2] += rV[i]; a[3]++; }
            }
            int good = 0;
            foreach (double[] a in per.Values)
            {
                if (a[1] < 15 || a[3] < 15) continue;   // tranche vue en partie seulement
                double gm = a[0] / a[1], rm = a[2] / a[3];
                if (rm < 1.8 || gm - NOISE > 0.45 * (rm - NOISE)) return false;
                good++;
            }
            if (good < minGaps) return false;
            long centre = gStart + W * BIN / 2;
            long d0 = FloorMod(centre - nominal, slotMs);
            if (d0 > wrap) d0 -= slotMs;
            shift = d0 / 1000.0;
            return true;
        }

        static long FloorDiv(long a, long b) { long q = a / b; if ((a % b != 0) && ((a < 0) != (b < 0))) q--; return q; }
        static long FloorMod(long a, long b) { return a - FloorDiv(a, b) * b; }

        // Arrêt de tous les décodeurs, dans un fil à part (un arrêt peut attendre la fin d'un calcul).
        public void Stop()
        {
            if (stopped) return;
            stopped = true;
            List<Cand> l = Snapshot();
            Thread t = new Thread(delegate()
            {
                foreach (Cand c in l)
                {
                    try { if (c.S != null) c.S.Stop(); else c.E.Stop(); } catch { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // Rythme FT8 / FT4 reconnu (sans message décodé pour l'instant) : mode, détail ; null sinon.
        public string RhythmMode { get { lock (gate) return rhythm; } }
        public string RhythmDetail { get { lock (gate) return rhythmDetail; } }

        // Retard du son (s) à donner au décodeur du mode choisi : celui appris par le décodeur de la recherche, sinon celui du rythme.
        public double? ShiftFor(string mode)
        {
            foreach (Cand c in Snapshot())
            {
                Ft8Engine fe = c.E as Ft8Engine;
                if (fe != null && c.Mode == mode && c.Msgs > 0) return fe.ShiftSec;
            }
            lock (gate) if (rhythm == mode) return rhythmShift;
            return null;
        }

        // Meilleur mode en texte (RTTY, PSK, CW, Olivia) d'après la note de son texte ; null si aucun ne dépasse le seuil.
        public string BestText(out string detail, out int score)
        {
            detail = ""; score = 0;
            string best = null;
            lock (gate)
                foreach (Cand c in cands)
                {
                    if (c.Text.Length == 0) continue;
                    int s = TextScore(c.Text.ToString());
                    if (s > score)
                    {
                        score = s; best = c.Mode;
                        detail = (c.Hz > 0 ? Lang.F("vers {0} Hz : ", "around {0} Hz: ", c.Hz) : "") + Excerpt(c.Text.ToString());
                    }
                }
            return score >= Threshold ? best : null;
        }

        public const int Threshold = 9;

        static readonly Regex Call = new Regex(@"^(?:[A-Z]{1,2}|[A-Z][0-9]|[0-9][A-Z])[0-9][A-Z]{1,4}$", RegexOptions.CultureInvariant);
        static readonly HashSet<string> Words = new HashSet<string>(StringComparer.Ordinal)
        {
            "TU", "73", "QSL", "RST", "5NN", "599", "QRZ", "TEST", "NAME", "QTH", "UR", "BK", "KN", "SK", "OP", "RIG", "ANT", "WX", "PSE", "TNX", "FB", "GM", "GA", "GE", "HW", "CPY", "QSO", "DX"
        };

        // Note d'un texte décodé : 6 par indicatif, 4 par CQ ou DE, 2 par mot courant du trafic ; divisée par 3 si le texte est
        // surtout fait de signes (bruit décodé).
        public static int TextScore(string text)
        {
            string t = text.ToUpperInvariant();
            int junk = 0, total = 0;
            foreach (char ch in t)
            {
                if (char.IsWhiteSpace(ch)) continue;
                total++;
                if (!((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '/' || ch == '?' || ch == '.' || ch == ',' || ch == '-')) junk++;
            }
            int score = 0;
            foreach (string tok in Regex.Split(t, @"[^A-Z0-9/]+"))
            {
                if (tok.Length == 0) continue;
                string w = tok;
                int slash = w.IndexOf('/');
                if (slash > 0) w = w.Substring(0, slash);
                if (w == "CQ" || w == "DE") score += 4;
                else if (Words.Contains(w)) score += 2;
                else if (w.Length >= 3 && w.Length <= 7 && Call.IsMatch(w)) score += 6;
            }
            if (total > 0 && junk * 10 > total * 3) score /= 3;
            return score;
        }

        static string Excerpt(string s)
        {
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > 40 ? s.Substring(s.Length - 40) : s;
        }

        public string SureMode { get { lock (gate) return sure; } }
        public string SureDetail { get { lock (gate) return sureDetail; } }
    }
}
