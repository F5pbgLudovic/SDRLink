using System;
using System.Globalization;

namespace SDRLink
{
    // Égaliseur 10 bandes (1.54, repris de HamRadioLink 1.9) : réception du WebSDR (dans la page, filtres du navigateur) et
    // émission (micro du PC envoyé au poste, avec noise gate, compresseur et limiteur, en C# dans le fil audio de RigMonitor).
    // Réglages dans settings.ini (FxRx / FxTx) ; le fil audio lit un instantané immuable (Get), remplacé à chaque changement.
    public class FxSet
    {
        public int Ver; public bool EqOn, CompOn, GateOn;
        public double[] G = new double[10];            // gain de chaque bande, dB
        public double Thr = -18, Ratio = 3, Makeup = 3, Ceil = -1, Att = 5, Rel = 150;   // compresseur : seuil dB, rapport, gain de sortie dB, plafond du limiteur dB, attaque ms, relâche ms
        public double GateThr = -50, GateRel = 150;    // noise gate : seuil d'ouverture dB, fermeture ms

        static readonly CultureInfo Ic = CultureInfo.InvariantCulture;
        public override string ToString()
        {
            string[] g = new string[10];
            for (int i = 0; i < 10; i++) g[i] = G[i].ToString("0.0", Ic);
            return (EqOn ? "1" : "0") + (CompOn ? "1" : "0") + (GateOn ? "1" : "0") + ";" + string.Join(",", g) + ";"
                + string.Join(",", new[] { Thr, Ratio, Makeup, Ceil, Att, Rel, GateThr, GateRel }.Map(v => v.ToString("0.0", Ic)));
        }
        public static FxSet Parse(string s)
        {
            FxSet f = new FxSet();
            try
            {
                string[] p = (s ?? "").Split(';');
                if (p.Length < 3 || p[0].Length < 3) return f;
                f.EqOn = p[0][0] == '1'; f.CompOn = p[0][1] == '1'; f.GateOn = p[0][2] == '1';
                string[] g = p[1].Split(',');
                for (int i = 0; i < 10 && i < g.Length; i++) f.G[i] = Clamp(D(g[i], 0), -12, 12);
                string[] c = p[2].Split(',');
                if (c.Length >= 8)
                {
                    f.Thr = Clamp(D(c[0], -18), -40, 0); f.Ratio = Clamp(D(c[1], 3), 1, 20); f.Makeup = Clamp(D(c[2], 3), 0, 24); f.Ceil = Clamp(D(c[3], -1), -12, 0);
                    f.Att = Clamp(D(c[4], 5), 1, 100); f.Rel = Clamp(D(c[5], 150), 20, 1500); f.GateThr = Clamp(D(c[6], -50), -80, -10); f.GateRel = Clamp(D(c[7], 150), 20, 1500);
                }
            }
            catch { }
            return f;
        }
        static double D(string s, double def) { double v; return double.TryParse(s, NumberStyles.Float, Ic, out v) ? v : def; }
        static double Clamp(double v, double a, double b) { return Math.Max(a, Math.Min(b, v)); }
        public FxSet Clone() { FxSet s = Parse(ToString()); return s; }
    }

    static class FxExt
    {
        public static string[] Map(this double[] a, Func<double, string> f) { string[] r = new string[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = f(a[i]); return r; }
    }

    public static class FxCfg
    {
        public static readonly int[] Freq = { 31, 60, 115, 220, 420, 800, 1500, 2800, 5300, 10000 };   // 1.54 : jusqu'à 10 kHz seulement (écart régulier)
        public const double Q = 1.41;
        static readonly FxSet[] cur = new FxSet[2]; static int ver;
        public static volatile float GrTx;      // réduction de gain actuelle du compresseur (dB, positive)
        public static volatile bool GateOpen = true;
        public static Settings S;              // posé par MainForm au démarrage
        public static event Action<bool> Changed;   // tx ? émission : réception (appelé sur le fil de l'interface)

        public static FxSet Get(bool tx)
        {
            FxSet s = cur[tx ? 0 : 1];
            if (s == null) { s = FxSet.Parse(S == null ? "" : tx ? S.FxTx : S.FxRx); s.Ver = ++ver; cur[tx ? 0 : 1] = s; }
            return s;
        }
        // enregistre un instantané modifié (settings.Save différé par l'appelant)
        public static void Store(bool tx, FxSet s)
        {
            if (S != null) { if (tx) S.FxTx = s.ToString(); else S.FxRx = s.ToString(); }
            s.Ver = ++ver; cur[tx ? 0 : 1] = s;
            Action<bool> h = Changed; if (h != null) h(tx);
        }
        public static bool Active(bool tx) { FxSet s = Get(tx); return s.EqOn || tx && (s.CompOn || s.GateOn); }

        // coefficients d'un filtre en cloche (RBJ, comme le « peaking » du navigateur) : b0 b1 b2 a1 a2, normalisés
        public static void Peak(double f, double gdb, double fs, double[] c)
        {
            double A = Math.Pow(10, gdb / 40), w = 2 * Math.PI * f / fs, al = Math.Sin(w) / (2 * Q), cs = Math.Cos(w), a0 = 1 + al / A;
            c[0] = (1 + al * A) / a0; c[1] = -2 * cs / a0; c[2] = (1 - al * A) / a0; c[3] = -2 * cs / a0; c[4] = (1 - al / A) / a0;
        }
        // réponse de l'égaliseur en dB à la fréquence f (courbe de la fenêtre)
        public static double Response(FxSet s, double f, double fs)
        {
            double tot = 0, w = 2 * Math.PI * f / fs; double[] c = new double[5];
            double cw = Math.Cos(w), sw = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
            for (int i = 0; i < 10; i++)
            {
                if (Math.Abs(s.G[i]) < 0.05 || Freq[i] >= 0.45 * fs) continue; Peak(Freq[i], s.G[i], fs, c);
                double nr = c[0] + c[1] * cw + c[2] * c2, ni = -(c[1] * sw + c[2] * s2), dr = 1 + c[3] * cw + c[4] * c2, di = -(c[3] * sw + c[4] * s2);
                tot += 10 * Math.Log10((nr * nr + ni * ni) / (dr * dr + di * di));
            }
            return tot;
        }
        // appel de la page : window.__sdrlink.setEq(marche, [10 gains en dB])
        public static string PageCall()
        {
            FxSet s = Get(false);
            return "window.__sdrlink && window.__sdrlink.setEq && window.__sdrlink.setEq(" + (s.EqOn ? "true" : "false") + ",[" + string.Join(",", s.G.Map(v => v.ToString("0.0", CultureInfo.InvariantCulture))) + "]);";
        }
    }

    // Chaîne du micro envoyé au poste : noise gate, égaliseur 10 bandes (stéréo), compresseur et limiteur.
    // Un exemplaire par flux, utilisé par le seul fil audio.
    public class FxChain
    {
        readonly bool tx; int ver = -1, rate; FxSet s;
        readonly double[][] co = new double[10][]; readonly bool[] used = new bool[10];
        readonly double[] zl1 = new double[10], zl2 = new double[10], zr1 = new double[10], zr2 = new double[10];
        double red, lim = 1, ka, kr, kl, env, gate = 1, gOpen, gClose, kEnv; int hold, holdLen;
        public FxChain(bool tx) { this.tx = tx; for (int i = 0; i < 10; i++) co[i] = new double[5]; }

        void Update(int fs)
        {
            s = FxCfg.Get(tx); ver = s.Ver; rate = fs;
            for (int i = 0; i < 10; i++)
            {
                used[i] = Math.Abs(s.G[i]) >= 0.05 && FxCfg.Freq[i] < 0.45 * fs;
                if (used[i]) FxCfg.Peak(FxCfg.Freq[i], s.G[i], fs, co[i]);
            }
            ka = 1 - Math.Exp(-1 / (Math.Max(0.5, s.Att) * 0.001 * fs)); kr = 1 - Math.Exp(-1 / (Math.Max(5, s.Rel) * 0.001 * fs)); kl = 1 - Math.Exp(-1 / (0.05 * fs));
            gOpen = 1 - Math.Exp(-1 / (0.002 * fs)); gClose = 1 - Math.Exp(-6.9 / (Math.Max(5, s.GateRel) * 0.001 * fs)); holdLen = (int)(0.1 * fs); kEnv = 1 - Math.Exp(-1 / (0.02 * fs));
        }

        public void Process(int fs, ref double l, ref double r)
        {
            FxSet cs = FxCfg.Get(tx); if (cs.Ver != ver || fs != rate) Update(fs);
            if (tx && s.GateOn)
            {
                // enveloppe crête (montée immédiate, descente 20 ms) ; ouverture en 2 ms, maintien 100 ms, puis fermeture (-60 dB) en GateRel ms
                double pk = Math.Max(Math.Abs(l), Math.Abs(r)); env = pk > env ? pk : env + (pk - env) * kEnv;
                bool open = 20 * Math.Log10(env + 1e-9) > s.GateThr;
                if (open) hold = holdLen; else if (hold > 0) hold--;
                double want = open || hold > 0 ? 1 : 0.001;
                gate += (want - gate) * (want > gate ? gOpen : gClose);
                FxCfg.GateOpen = hold > 0;
                l *= gate; r *= gate;
            }
            if (s.EqOn)
            {
                for (int i = 0; i < 10; i++)
                {
                    if (!used[i]) continue; double[] c = co[i];
                    double y = c[0] * l + zl1[i]; zl1[i] = c[1] * l - c[3] * y + zl2[i]; zl2[i] = c[2] * l - c[4] * y; l = y;
                    y = c[0] * r + zr1[i]; zr1[i] = c[1] * r - c[3] * y + zr2[i]; zr2[i] = c[2] * r - c[4] * y; r = y;
                }
            }
            if (tx && s.CompOn)
            {
                double pk = Math.Max(Math.Abs(l), Math.Abs(r)), pdb = 20 * Math.Log10(pk + 1e-9), over = pdb - s.Thr, target = over > 0 ? over * (1 - 1 / Math.Max(1, s.Ratio)) : 0;
                red += (target - red) * (target > red ? ka : kr); FxCfg.GrTx = (float)red;
                double g = Math.Pow(10, (s.Makeup - red) / 20); l *= g; r *= g;
                double ceil = Math.Pow(10, s.Ceil / 20), p2 = Math.Max(Math.Abs(l), Math.Abs(r)), need = p2 > ceil ? ceil / p2 : 1;
                if (need < lim) lim = need; else lim += (1 - lim) * kl;
                l *= lim; r *= lim;
            }
        }
    }
}
