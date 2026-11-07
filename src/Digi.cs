using System;
using System.Collections.Generic;
using System.Threading;

namespace SDRLink
{
    // Base des décodeurs de modes numériques (repris de PhoneSDRLink) : le son arrive à la fréquence Rate (8000 ou 12000 Hz),
    // mono, en flottants (±1) ; il est converti en 16 bits et traité dans un fil à part (file d'actions). Les événements
    // (Text, Lines, Status, ImageBegin, ImageChanged) partent de ce fil : l'interface doit repasser dans son propre fil.
    public abstract class DigiEngine
    {
        public event Action<string> Text;                 // flux de caractères ('\n' = nouvelle ligne)
        public event Action<List<string>, int> Lines;     // lignes d'une tranche (FT8, WSPR…) et parité de la tranche (0 ou 1)
        public event Action<string> Status;               // ligne d'état
        public event Action<int, int, int[]> ImageBegin;  // largeur, hauteur, pixels ARGB (Hell : l'image défile, redessinée en entier)
        public event Action ImageChanged;                 // pixels modifiés
        public event Action<int> ImageRows;               // WEFAX : lignes terminées (l'image se construit de haut en bas)
        public event Action<bool, int> ImageEnd;          // WEFAX : image terminée (complète ou non) et lignes reçues

        public abstract int Rate { get; }
        protected void RaiseImageRows(int done) { Action<int> e = ImageRows; if (e != null) e(done); }
        protected void RaiseImageEnd(bool complete, int rows) { Action<bool, int> e = ImageEnd; if (e != null) e(complete, rows); }

        readonly Queue<Action> work = new Queue<Action>();
        readonly object wlock = new object();
        Thread thread;
        volatile bool stopping;
        int pendingFeeds;

        protected void RaiseText(string s) { Action<string> h = Text; if (h != null) h(s); }
        protected void RaiseLines(List<string> l, int parity) { Action<List<string>, int> h = Lines; if (h != null) h(l, parity); }
        protected void RaiseStatus(string s) { Action<string> h = Status; if (h != null) h(s); }
        protected void RaiseImageBegin(int w, int h, int[] px) { Action<int, int, int[]> e = ImageBegin; if (e != null) e(w, h, px); }
        protected void RaiseImageChanged() { Action e = ImageChanged; if (e != null) e(); }

        // Morceau de son à la fréquence Rate (appelé depuis le fil du son : ne bloque pas). Les décodeurs à tranches UTC
        // (FT8, WSPR, JT9, JS8) remplacent cette méthode pour noter l'heure d'arrivée.
        public virtual void Feed(float[] x, int n)
        {
            short[] s = new short[n];
            for (int i = 0; i < n; i++)
            {
                double v = x[i] * 32767.0;
                s[i] = (short)(v > 32767 ? 32767 : v < -32768 ? -32768 : v);
            }
            lock (wlock) { if (pendingFeeds > 400) return; pendingFeeds++; }   // le calcul ne suit pas : on lâche plutôt que d'accumuler
            Post(delegate { lock (wlock) pendingFeeds--; FeedShort(s, n); });
        }

        // Traitement d'un morceau, dans le fil du décodeur.
        protected virtual void FeedShort(short[] s, int n) { }

        // Exécute a dans le fil du décodeur (changement de réglage sans course avec le calcul).
        public void Post(Action a)
        {
            lock (wlock)
            {
                if (stopping) return;
                if (thread == null)
                {
                    thread = new Thread(Run);
                    thread.IsBackground = true;
                    thread.Name = "Digi " + GetType().Name;
                    thread.Start();
                }
                work.Enqueue(a);
                Monitor.Pulse(wlock);
            }
        }

        void Run()
        {
            while (true)
            {
                Action a;
                lock (wlock)
                {
                    while (work.Count == 0 && !stopping) Monitor.Wait(wlock);
                    if (work.Count == 0) break;
                    a = work.Dequeue();
                    if (stopping) { work.Clear(); a = null; }
                }
                if (a == null) break;
                try { a(); } catch (Exception e) { Log.Write("Décodeur " + GetType().Name + " : " + e.Message); }
            }
            try { OnClose(); } catch { }
        }

        // Arrêt : les morceaux en attente sont abandonnés, OnClose est appelé dans le fil du décodeur.
        public virtual void Stop()
        {
            Thread t;
            lock (wlock) { stopping = true; t = thread; Monitor.Pulse(wlock); }
            if (t != null) t.Join(3000);
            else { try { OnClose(); } catch { } }
        }

        protected bool Stopping { get { return stopping; } }

        /** Appelé dans le fil du décodeur à l'arrêt (libérer le code natif). */
        protected virtual void OnClose() { }

        /** Énergie de Goertzel à la fréquence f (Hz) sur x[off .. off+n[ (échantillons à rate Hz). */
        public static double Goertzel(float[] x, int off, int n, double f, int rate)
        {
            double w = 2.0 * Math.PI * f / rate, c = 2.0 * Math.Cos(w), s1 = 0, s2 = 0;
            for (int i = 0; i < n; i++) { double s0 = x[off + i] + c * s1 - s2; s2 = s1; s1 = s0; }
            return s1 * s1 + s2 * s2 - c * s1 * s2;
        }
    }

    // Passage d'une fréquence quelconque à outRate (8000 ou 12000 Hz) : passe-bas FIR de Blackman (coupure 0,375 × outRate) à la
    // fréquence d'entrée, puis interpolation de Catmull-Rom aux instants de sortie (même méthode que le décodeur SSTV).
    public class Resampler
    {
        const int HIST = 2048, HMASK = HIST - 1;
        readonly int outRate;
        int inRate;
        double[] lp;
        readonly double[] hist = new double[HIST];
        long inCount;
        double tNext;
        float[] outBuf = new float[1024];

        public Resampler(int outRate) { this.outRate = outRate; }

        public void Reset() { inRate = 0; }

        void SetRate(int rate)
        {
            inRate = rate;
            inCount = 0;
            tNext = 0;
            Array.Clear(hist, 0, HIST);
            if (rate == outRate) { lp = null; return; }
            int n = (int)Math.Round(rate / (double)outRate * 32) | 1;
            if (n > 511) n = 511;
            lp = new double[n];
            double fc = Math.Min(0.375 * outRate, 0.375 * rate) / rate, sum = 0;
            for (int i = 0; i < n; i++)
            {
                double x = i - (n - 1) / 2.0;
                double s = x == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
                lp[i] = s * (0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (n - 1)));
                sum += lp[i];
            }
            for (int i = 0; i < n; i++) lp[i] /= sum;
        }

        double Filtered(long i)
        {
            int h = (lp.Length - 1) / 2;
            double y = 0;
            for (int k = 0; k < lp.Length; k++) y += lp[k] * hist[(int)((i + h - k) & HMASK)];
            return y;
        }

        // Renvoie un nouveau tableau (longueur = nombre d'échantillons de sortie), éventuellement vide.
        public float[] Process(int rate, float[] x, int n)
        {
            if (rate != inRate) SetRate(rate);
            if (lp == null) { float[] c = new float[n]; Array.Copy(x, c, n); return c; }
            int k = 0;
            int need = (int)(n * (double)outRate / rate) + 4;
            if (outBuf.Length < need) outBuf = new float[need];
            double step = rate / (double)outRate;
            int h = (lp.Length - 1) / 2;
            for (int i = 0; i < n; i++)
            {
                hist[(int)(inCount & HMASK)] = x[i];
                inCount++;
                while (tNext >= 1 && (long)tNext + 2 + h < inCount)
                {
                    long i0 = (long)tNext;
                    double f = tNext - i0;
                    double ym = Filtered(i0 - 1), y0 = Filtered(i0), y1 = Filtered(i0 + 1), y2 = Filtered(i0 + 2);
                    if (k >= outBuf.Length) Array.Resize(ref outBuf, outBuf.Length * 2);
                    outBuf[k++] = (float)(y0 + 0.5 * f * (y1 - ym + f * (2 * ym - 5 * y0 + 4 * y1 - y2 + f * (3 * (y0 - y1) + y2 - ym))));
                    tNext += step;
                }
                if (tNext < 1) tNext = 1;
            }
            float[] r = new float[k];
            Array.Copy(outBuf, r, k);
            return r;
        }
    }
}
