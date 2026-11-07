using System;
using System.Collections.Generic;
using System.Globalization;

namespace SDRLink
{
    // Décodeur CW : suivi de la tonalité (200 à 1200 Hz) ou tonalité imposée, enveloppe par Goertzel (largeur de filtre réglable),
    // seuil adaptatif réglable, vitesse apprise ou imposée, seuils de fin de lettre et de fin de mot réglables.
    // Quand la tonalité est trouvée (ou change), la dernière seconde de son est rejouée pour ne pas perdre le début.
    // (Portage de CwEngine.java de PhoneSDRLink.)
    public class CwEngine : DigiEngine
    {
        const int RATE = 8000;
        public const int HOP = 64;                      // 8 ms de pas
        public const int BUF = 8192;                    // 1 s de mémoire
        readonly float[] ring = new float[BUF];
        long total;
        double tone = 0;                                // Hz (0 = pas encore trouvée)
        double peak = 1e-9, floor = -1, dot = 80, dash = 240;   // enveloppe, vitesse (ms)
        bool on, spaced, replay;
        double runMs;                                   // durée de l'état courant
        string sym = "";
        int sinceTone, charsOut, toneTick;

        // Réglages (modifiables pendant le décodage) ; les double ne peuvent pas être volatile en C#.
        public volatile bool AutoTone = true, AutoSpeed = true;
        public volatile int ManualTone = 700;      // Hz
        public volatile int Bandwidth = 80;        // Hz : largeur du filtre (fenêtre de Goertzel plus longue = filtre plus étroit)
        public volatile int ManualWpm = 18;
        public volatile int Threshold = 55;        // % entre le bruit et la crête
        public double CharGap = 2.0;               // fin de lettre : silence supérieur à ce nombre de points
        public double WordGap = 5.0;               // fin de mot : silence supérieur à ce nombre de points
        public volatile int Squelch = 5;           // la crête doit dépasser ainsi le bruit pour décoder
        public volatile bool AutoBw = true;        // filtre adapté à la vitesse (environ 2 / durée d'un point, de 25 à 250 Hz)
        public volatile bool Afc = true;           // la tonalité suit la dérive du signal
        public volatile int Agc = 1;               // 0 lent, 1 moyen, 2 rapide : rapidité de suivi des évanouissements

        public static readonly string[] MORSE = {
            ".-", "-...", "-.-.", "-..", ".", "..-.", "--.", "....", "..", ".---", "-.-", ".-..", "--", "-.", "---", ".--.", "--.-", ".-.", "...", "-", "..-", "...-", ".--", "-..-", "-.--", "--..",
            "-----", ".----", "..---", "...--", "....-", ".....", "-....", "--...", "---..", "----.",
            ".-.-.-", "--..--", "..--..", "-..-.", "-...-", ".-.-.", "-.--.", "-.--.-", ".----.", "---...", "-....-", ".-..-.", "...-.-", "...-..-", ".--.-." };
        public const string CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,?/=+()':-\"*$@";   // une lettre par code de MORSE (51)

        public override int Rate { get { return RATE; } }

        int Win()
        {
            double bw = AutoBw && calibrated ? Math.Max(30, Math.Min(300, 3000.0 / Math.Max(20, dot))) : Bandwidth;
            return Math.Max(24, Math.Min(800, (int)(1.2 * RATE / Math.Max(20, bw))));
        }

        protected override void FeedShort(short[] s, int n)
        {
            for (int i = 0; i < n; i++)
            {
                ring[(int)(total % BUF)] = s[i] / 32768f;
                total++;
                if (total % HOP == 0) Step();
            }
        }

        readonly float[] wbuf = new float[800];   // fenêtre réutilisée à chaque pas

        float[] WindowInto(long end, int len)
        {
            long start = end - len;
            for (int i = 0; i < len; i++) wbuf[i] = ring[(int)(((start + i) % BUF + BUF) % BUF)];
            return wbuf;
        }

        float[] WindowAt(long end, int len)
        {
            float[] w = new float[len];
            long start = end - len;
            for (int i = 0; i < len; i++) w[i] = ring[(int)(((start + i) % BUF + BUF) % BUF)];
            return w;
        }

        static double G(float[] x, int off, int n, double f) { return Goertzel(x, off, n, f, RATE); }

        void NewTone(double f)
        {
            tone = f; peak = 1e-9; floor = -1; on = false; runMs = 0; sym = ""; replay = true; warm = 12; pending = 0;
            calibrated = false; rec.Clear(); ons.Clear();
            if (!AutoSpeed) SetManualSpeed();
        }

        // Vitesse imposée (à appeler dans le fil du décodeur : Post(SetManualSpeed)).
        public void SetManualSpeed() { dot = 1200.0 / Math.Max(5, ManualWpm); dash = 3 * dot; calibrated = true; }

        string StatusText() { return string.Format(CultureInfo.InvariantCulture, "CW : {0:F0} Hz, {1:F0} WPM", tone, 1200.0 / dot); }

        // Recherche de la tonalité : le pic le plus net entre 200 et 1200 Hz (toutes les 0,25 s). Il doit dépasser de beaucoup le bruit
        // voisin (±200 Hz) : avec un filtre étroit du WebSDR, le bruit seul fait aussi un « pic » dans la bande passante.
        void FindTone()
        {
            if (total < BUF) return;   // (aussi en tonalité manuelle : la relecture d'une seconde vide laissait le bruit de fond à zéro)
            if (!AutoTone)
            {
                // (seulement quand le réglage change : l'AFC déplace ensuite la tonalité sans tout remettre à zéro)
                if (manualApplied != ManualTone || tone == 0) { manualApplied = ManualTone; NewTone(ManualTone); }
                return;
            }
            manualApplied = -1;
            float[] w = WindowAt(total, 4096);
            int n = 41;
            double[] e = new double[n];
            int bi = 0;
            for (int i = 0; i < n; i++) { e[i] = G(w, 0, 4096, 200 + 25.0 * i); if (e[i] > e[bi]) bi = i; }
            double side = 0;
            int cnt = 0;
            for (int d = 2; d <= 8; d++)
            {
                if (bi - d >= 0) { side += e[bi - d]; cnt++; }
                if (bi + d < n) { side += e[bi + d]; cnt++; }
            }
            side = cnt > 0 ? side / cnt : 1e9;
            double best = e[bi], bf = 200 + 25.0 * bi;
            if (best > 25 * side && best > 1e-4)
            {
                double bb = best, ff = bf;
                for (double f = bf - 24; f <= bf + 24; f += 4) { double x = G(w, 0, 4096, f); if (x > bb) { bb = x; ff = f; } }
                if (tone == 0 || Math.Abs(ff - tone) > 30) NewTone(ff);
                else tone = 0.7 * tone + 0.3 * ff;
                sinceTone = 0;
                RaiseStatus(StatusText());
            }
            else if (++sinceTone > 24) tone = 0;
        }

        void Step()
        {
            if (++toneTick >= 32)
            {
                toneTick = 0;
                FindTone();
                if (!AutoTone && tone != 0) RaiseStatus(StatusText());
            }
            if (tone == 0) { runMs = 0; return; }
            if (replay)
            {   // tonalité nouvellement trouvée : on traite de nouveau la dernière seconde
                replay = false;
                for (long e = total - 8000; e < total; e += HOP) Process(e);
            }
            Process(total);
        }

        int warm, pending, afcTick, manualApplied = -1;

        void Process(long end)
        {
            int win = Win();
            float[] w = WindowInto(end, win);
            double e = Math.Sqrt(G(w, 0, win, tone)) / win;   // amplitude de la tonalité
            if (floor < 0) { floor = e; peak = e; }
            int agc = Agc;
            peak = Math.Max(e, peak * (agc == 0 ? 0.9998 : agc == 1 ? 0.9995 : 0.998));
            if (!on) floor += 0.02 * (Math.Min(e, floor * 3) - floor);   // moyenne lente du bruit, hors signal
            if (Afc && on && warm == 0 && (++afcTick & 3) == 0)
            {   // AFC : on compare l'énergie juste au-dessus et juste au-dessous de la tonalité
                double eL = G(w, 0, win, tone - 4), eH = G(w, 0, win, tone + 4);
                if (eH > 1.15 * eL) tone = Math.Min(1200, tone + 0.5);
                else if (eL > 1.15 * eH) tone = Math.Max(200, tone - 0.5);
            }
            double t = Threshold / 100.0, range = peak - floor;
            bool sig = (peak > Squelch * floor) && (on ? e > floor + (t - 0.1) * range : e > floor + t * range);
            double ms = HOP * 1000.0 / RATE;
            if (warm > 0) { warm--; on = sig; runMs = 0; pending = 0; return; }   // mise en route : on apprend seulement les niveaux
            if (sig == on) { pending = 0; runMs += ms; if (!on) GapCheck(); return; }
            if (++pending < 2) { runMs += ms; return; }   // un changement d'état doit durer deux pas (16 ms)
            if (on) Element(runMs); else Gap(runMs);
            on = sig;
            runMs = pending * ms;
            pending = 0;
        }

        // Au départ (vitesse automatique), la vitesse n'est pas connue : on enregistre les huit premiers éléments, on en tire la durée
        // d'un point (quart inférieur) et d'un trait, puis on décode de nouveau ce qui a été enregistré.
        bool calibrated;
        readonly List<double[]> rec = new List<double[]>();
        readonly List<double> ons = new List<double>();

        void Calibrate()
        {
            List<double> s = new List<double>(ons);
            s.Sort();
            dot = s[s.Count / 4];
            double sum = 0;
            int n = 0;
            foreach (double d in s) if (d > 2.0 * dot) { sum += d; n++; }
            dash = n > 0 ? sum / n : 3 * dot;
            if (dash < 2.2 * dot || dash > 4.5 * dot) dash = 3 * dot;
            calibrated = true;
            List<double[]> r = new List<double[]>(rec);
            rec.Clear(); ons.Clear();
            foreach (double[] ev in r) { if (ev[0] == 1) Element(ev[1]); else Gap(ev[1]); }
        }

        void Element(double d)
        {
            if (d < 22) return;   // parasite (un point à 50 mots par minute dure 24 ms)
            if (!calibrated) { rec.Add(new double[] { 1, d }); ons.Add(d); if (ons.Count >= 8) Calibrate(); return; }
            double thr = Math.Sqrt(dot * dash);
            if (d < thr) { sym += "."; if (AutoSpeed) dot = 0.8 * dot + 0.2 * Math.Max(25, d); }
            else { sym += "-"; if (AutoSpeed) dash = 0.8 * dash + 0.2 * Math.Min(1200, d); }
            if (AutoSpeed)
            {
                if (dash < 2.2 * dot) dash = 3 * dot;   // garde-fous : le trait vaut environ trois points
                if (dash > 4.5 * dot) dash = 3 * dot;
            }
        }

        void Gap(double d)
        {
            if (!calibrated) { rec.Add(new double[] { 0, d }); return; }
            if (d > CharGap * dot) Flush();
            if (d > WordGap * dot && !spaced && charsOut > 0) Emit(" ");   // espace entre deux mots (cas des éléments rejoués)
            spaced = false;
        }

        // Espace long sans signal : on termine la lettre, puis on met un espace entre les mots.
        void GapCheck()
        {
            if (!calibrated) return;
            if (sym.Length > 0 && runMs > CharGap * dot) Flush();
            if (!spaced && charsOut > 0 && runMs > WordGap * dot) { Emit(" "); spaced = true; }
        }

        void Flush()
        {
            if (sym.Length == 0) return;
            int k = Array.IndexOf(MORSE, sym);
            Emit(k >= 0 ? CHARS[k].ToString() : "*");
            sym = "";
            spaced = false;
        }

        void Emit(string s)
        {
            charsOut++;
            RaiseText(s);
        }
    }
}
