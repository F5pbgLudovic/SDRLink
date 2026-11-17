using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace SDRLink
{
    // Écoute en diversité (case « Diversité », après « Réinit WebSDR ») : le son du WebSDR dans une oreille, celui du poste
    // (ou d'ExpertSDR, ou de HDSDR) dans l'autre. Le WebSDR arrive toujours en retard (0,3 à 5 s) : le son local est
    // retardé d'autant pour que les deux oreilles entendent la même chose au même moment.
    //
    // Le retard est mesuré en continu en comparant les enveloppes des deux sons (syllabes, manipulation CW, évanouissements) :
    // les deux réceptions n'ont pas le même battement (BFO, filtre), la forme d'onde ne se compare pas, l'enveloppe si.
    // Le son local (B) donne la cadence : chaque bloc reçu fait sortir autant de trames stéréo, retardées ; le WebSDR (A)
    // est lu à l'endroit qui lui correspond. Si le son local s'arrête, le WebSDR continue seul (et inversement).
    public class DiversityMixer
    {
        public const int Rate = 16000;
        const int EnvRate = 200, EnvDiv = Rate / EnvRate;      // enveloppe : une valeur toutes les 5 ms
        const int WinEnv = 8 * EnvRate;                         // fenêtre comparée : 8 s
        const int MaxLagEnv = 6 * EnvRate, MinLagEnv = -EnvRate * 3 / 10;   // retard cherché : -0,3 à 6 s
        const int EnvCap = WinEnv + MaxLagEnv + MaxLeadEnv + EnvRate * 2;
        public const int MaxLeadEnv = 4 * EnvRate;              // deux WebSDR (1.44) : le second peut aussi arriver jusqu'à 4 s après le premier
        const int Margin = Rate * 12 / 100;                    // 120 ms de marge pour les paquets du WebSDR (blocs de 85 ms)
        const int JumpLimit = Rate * 3 / 100;                   // écart de plus de 30 ms : on recale
        const int Fade = Rate / 100;                            // fondu de 10 ms à chaque recalage

        readonly object sync = new object();
        readonly Action<float[], int> output;                   // trames stéréo entrelacées à Rate (gauche, droite)

        // Réglages (lus à chaque trame) : gain de chaque oreille (0 = coupée), WebSDR à droite plutôt qu'à gauche.
        public volatile float GainA = 1, GainB = 1;
        public volatile bool Swap;
        public volatile float MaxGainB = 30;                    // HDSDR très baissé dans le mélangeur : jusqu'à 1000
        // Deux WebSDR (1.44) : B n'est pas forcément en avance sur A. La fenêtre de A comparée est prise LeadEnv plus tôt,
        // ce qui permet de trouver un B en retard sur A (jusqu'à LeadEnv) ; A est alors lu dans le passé, B joué tout de suite.
        public volatile int LeadEnv;

        // État du calage (pour la barre d'état et le bouton).
        public bool Locked { get { lock (sync) return locked; } }
        public double DelaySec { get { lock (sync) return (bIdx - play) / (double)Rate; } }
        // Retard appliqué au WebSDR (A) : dernier échantillon reçu moins celui qu'on joue.
        public double DelayASec { get { lock (sync) return play < 0 || !locked ? 0 : Math.Max(0, (aIdx - (play + (long)Math.Round(m))) / (double)Rate); } }
        // Écart entre les deux réceptions (B moins A, en secondes) : positif si B arrive avant A.
        public double GapSec { get { return DelaySec - DelayASec; } }
        public double Quality { get { lock (sync) return quality; } }
        public bool HearsA { get { return Environment.TickCount - lastA < 1500; } }
        public bool HearsB { get { return Environment.TickCount - lastB < 1500; } }

        readonly DivResampler resA = new DivResampler(), resB = new DivResampler();
        readonly float[] aRing = new float[Rate * 8], bRing = new float[Rate * 10];
        long aIdx, bIdx, play = -1;
        double m;                       // décalage : l'échantillon A n°a correspond à l'échantillon B n°(a - m)
        bool locked;
        double quality;
        double candidateM = double.MinValue;
        int candidateHits;
        // Enveloppes (log du niveau moyen sur 5 ms).
        readonly float[] aEnv = new float[EnvCap], bEnv = new float[EnvCap];
        long aEnvN, bEnvN;
        double aAcc, bAcc;
        int aAccN, bAccN;
        long nextLook;
        // Avance du WebSDR sur le son local (aIdx - bIdx), la plus petite des 3 dernières secondes (paquets en rafale).
        readonly long[] offs = new long[64];
        readonly int[] offT = new int[64];
        int offN;
        volatile int lastA = -100000, lastB = -100000;
        readonly Agc agcA = new Agc(), agcB = new Agc();
        int fadeLeft;
        float[] outBuf = new float[0];

        public DiversityMixer(Action<float[], int> output) { this.output = output; }

        // Son du WebSDR (fil de l'interface).
        public void FeedA(int rate, float[] x, int n)
        {
            float[] y; int k;
            resA.Run(rate, x, n, out y, out k);
            lock (sync)
            {
                for (int i = 0; i < k; i++)
                {
                    aRing[(aIdx + i) % aRing.Length] = y[i];
                    Env(y[i], aEnv, ref aEnvN, ref aAcc, ref aAccN);
                }
                aIdx += k;
                lastA = Environment.TickCount;
                // Le son local ne vient plus : le WebSDR est joué seul, à sa propre cadence.
                if (!HearsB && k > 0) Emit(k, true);
            }
        }

        // Son local (fil de capture de la carte son, de TCI ou du programme).
        public void FeedB(int rate, float[] x, int n)
        {
            float[] y; int k;
            resB.Run(rate, x, n, out y, out k);
            lock (sync)
            {
                for (int i = 0; i < k; i++)
                {
                    bRing[(bIdx + i) % bRing.Length] = y[i];
                    Env(y[i], bEnv, ref bEnvN, ref bAcc, ref bAccN);
                }
                bIdx += k;
                lastB = Environment.TickCount;
                NoteOffset();
                if (bEnvN >= nextLook) { nextLook = bEnvN + EnvRate * 2; Look(); }
                if (k > 0) Emit(k, false);
            }
        }

        static void Env(float v, float[] env, ref long n, ref double acc, ref int accN)
        {
            acc += Math.Abs(v);
            if (++accN < EnvDiv) return;
            env[n % env.Length] = (float)Math.Log(acc / accN + 1e-5);
            n++;
            acc = 0;
            accN = 0;
        }

        void NoteOffset()
        {
            int now = Environment.TickCount;
            if (offN > 0 && now - offT[(offN - 1) % offs.Length] < 50) return;
            offs[offN % offs.Length] = aIdx - bIdx;
            offT[offN % offs.Length] = now;
            offN++;
        }

        long MinOffset()
        {
            int now = Environment.TickCount;
            long best = long.MaxValue;
            for (int i = Math.Max(0, offN - offs.Length); i < offN; i++)
                if (now - offT[i % offs.Length] < 3000 && offs[i % offs.Length] < best) best = offs[i % offs.Length];
            return best == long.MaxValue ? aIdx - bIdx : best;
        }

        // Toutes les 2 s : comparaison des enveloppes, retard le plus probable.
        void Look()
        {
            int lead = Math.Max(0, Math.Min(MaxLeadEnv, LeadEnv));
            if (aEnvN < WinEnv + lead || bEnvN < WinEnv + MaxLagEnv / 4) return;
            int n = WinEnv;
            double[] a = new double[n];
            double ma = 0;
            for (int i = 0; i < n; i++) { a[i] = aEnv[(aEnvN - lead - n + i) % EnvCap]; ma += a[i]; }
            ma /= n;
            double va = 0;
            for (int i = 0; i < n; i++) { a[i] -= ma; va += a[i] * a[i]; }
            if (va < 1e-6) return;
            int maxLag = (int)Math.Min(MaxLagEnv + lead, bEnvN - n);
            int lags = maxLag - MinLagEnv + 1;
            double[] c = new double[lags];
            double best = -2;
            int bestI = -1;
            // Retard s : la fin de la fenêtre A correspond à l'enveloppe B n° (bEnvN - s).
            for (int li = 0; li < lags; li++)
            {
                int s = MinLagEnv + li;
                long end = bEnvN - s;
                if (end > bEnvN || end - n < Math.Max(0, bEnvN - EnvCap)) { c[li] = -2; continue; }
                double sb = 0, sbb = 0, sab = 0;
                for (int i = 0; i < n; i++)
                {
                    double v = bEnv[(end - n + i) % EnvCap];
                    sb += v; sbb += v * v; sab += a[i] * v;
                }
                double vb = sbb - sb * sb / n;
                c[li] = vb < 1e-6 ? -2 : sab / Math.Sqrt(va * vb);
                if (c[li] > best) { best = c[li]; bestI = li; }
            }
            if (bestI < 0) return;
            // Pic net : nettement au-dessus de ce qu'on trouve loin de lui.
            double far = -2;
            for (int li = 0; li < lags; li++) if (Math.Abs(li - bestI) > EnvRate / 4 && c[li] > far) far = c[li];
            quality = best;
            if (best < 0.25 || best - far < 0.06) { candidateHits = 0; return; }
            double frac = 0;
            if (bestI > 0 && bestI < lags - 1 && c[bestI - 1] > -2 && c[bestI + 1] > -2)
            {
                double y0 = c[bestI - 1], y1 = c[bestI], y2 = c[bestI + 1], den = y0 - 2 * y1 + y2;
                if (den < 0) frac = Math.Max(-0.5, Math.Min(0.5, 0.5 * (y0 - y2) / den));
            }
            // Fin de la fenêtre A : échantillon aEnvN*EnvDiv ; il correspond à l'échantillon B (bEnvN - s)*EnvDiv.
            // Ce décalage ne dépend pas de l'arrivée en paquets du WebSDR (contrairement au retard s lui-même).
            double mNew = (aEnvN - lead - (bEnvN - (MinLagEnv + bestI + frac))) * EnvDiv;
            // Deux mesures d'accord de suite (à 15 ms près) avant de changer le calage ; un premier calage net suffit.
            if (Math.Abs(mNew - candidateM) <= Rate * 15 / 1000) candidateHits++; else { candidateM = mNew; candidateHits = 1; }
            if (candidateHits < 2 && (locked || best < 0.45)) return;
            m = mNew;
            locked = true;
        }

        // Sortie de k trames : le son local retardé et le WebSDR à l'endroit qui lui correspond.
        void Emit(int k, bool aOnly)
        {
            long target;
            if (aOnly) target = aIdx - k - Margin;   // en A seul, play compte en échantillons A
            else
            {
                long avail = bIdx + MinOffset() - Margin;   // dernier échantillon A sûrement arrivé, en échelle B (avant décalage)
                target = locked ? (long)Math.Round(avail - m) : bIdx - Margin;
                if (target > bIdx - Margin / 4) target = bIdx - Margin / 4;
                if (target < bIdx - bRing.Length + Rate) target = bIdx - bRing.Length + Rate;
                target -= k;
            }
            if (play < 0 || aOnly != playingAOnly || Math.Abs(play - target) > JumpLimit) { play = target; fadeLeft = Fade; playingAOnly = aOnly; }
            if (outBuf.Length < 2 * k) outBuf = new float[2 * k];
            float ga = GainA, gb = GainB;
            bool swap = Swap;
            long mi = (long)Math.Round(m);
            for (int i = 0; i < k; i++, play++)
            {
                float va, vb;
                if (aOnly) { va = ReadA(play); vb = 0; }
                else
                {
                    vb = bRing[((play % bRing.Length) + bRing.Length) % bRing.Length];
                    va = ReadA(locked ? play + mi : play + (aIdx - bIdx) - Margin);
                }
                va = agcA.Run(va, 30) * ga;
                vb = agcB.Run(vb, MaxGainB) * gb;
                if (fadeLeft > 0) { float f = 1f - fadeLeft / (float)Fade; va *= f; vb *= f; fadeLeft--; }
                outBuf[2 * i] = Clip(swap ? vb : va);
                outBuf[2 * i + 1] = Clip(swap ? va : vb);
            }
            output(outBuf, k);
        }

        bool playingAOnly;

        float ReadA(long i)
        {
            if (i < 0 || i >= aIdx || i < aIdx - aRing.Length) return 0;
            return aRing[i % aRing.Length];
        }

        static float Clip(float v) { return v > 0.98f ? 0.98f : v < -0.98f ? -0.98f : v; }

        // Commande automatique de gain : les deux oreilles au même niveau, quel que soit le réglage de chaque réception.
        class Agc
        {
            double peak = 1e-4, gain = 1;
            public float Run(float v, float maxGain)
            {
                double a = Math.Abs(v);
                peak = a > peak ? peak + (a - peak) * 0.05 : peak * 0.99995;   // montée ~1 ms, descente ~1,2 s
                if (peak < 1e-7) peak = 1e-7;
                double want = Math.Min(maxGain, 0.3 / peak);
                gain += (want - gain) * (want < gain ? 0.02 : 0.0002);
                return (float)(v * gain);
            }
        }
    }

    // Changement de fréquence d'échantillonnage vers DiversityMixer.Rate : filtre passe-bas (à la descente) puis interpolation.
    public class DivResampler
    {
        int inRate;
        float[] taps = new float[0], hist = new float[0];
        int histPos;
        double t;
        float last;
        float[] outBuf = new float[0];

        public void Run(int rate, float[] x, int n, out float[] y, out int k)
        {
            if (rate != inRate) Setup(rate);
            double step = (double)rate / DiversityMixer.Rate;
            int cap = (int)(n / step) + 4;
            if (outBuf.Length < cap) outBuf = new float[cap];
            k = 0;
            for (int i = 0; i < n; i++)
            {
                float v = x[i];
                if (taps.Length > 0)
                {
                    hist[histPos] = v;
                    histPos = (histPos + 1) % hist.Length;
                    double s = 0;
                    for (int j = 0; j < taps.Length; j++) s += taps[j] * hist[(histPos + j) % hist.Length];
                    v = (float)s;
                }
                while (t < 1.0 && k < cap) { outBuf[k++] = (float)(last + (v - last) * t); t += step; }
                t -= 1.0;
                last = v;
            }
            y = outBuf;
        }

        void Setup(int rate)
        {
            inRate = rate;
            t = 0;
            last = 0;
            histPos = 0;
            if (rate <= DiversityMixer.Rate) { taps = new float[0]; hist = new float[0]; return; }
            int n = 31;
            double fc = 7000.0 / rate;
            taps = new float[n];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                double x = i - (n - 1) / 2.0;
                double sinc = x == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
                double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (n - 1));
                taps[i] = (float)(sinc * w);
                sum += taps[i];
            }
            for (int i = 0; i < n; i++) taps[i] = (float)(taps[i] / sum);
            hist = new float[n];
        }
    }

    // Sortie stéréo en WASAPI partagé (haut-parleurs par défaut de Windows, ou une sortie donnée) : trames à 16 kHz
    // données par Write depuis n'importe quel fil, rejouées avec un petit matelas contre les à-coups (paquets du WebSDR).
    public class StereoOut
    {
        const int AUDCLNT_SHAREMODE_SHARED = 0;
        static readonly Guid FloatFormat = new Guid("00000003-0000-0010-8000-00aa00389b71");

        public readonly string DeviceId;      // null : sortie par défaut
        public event Action<string> Problem;
        public string OutputName { get; private set; }

        readonly Thread thread;
        volatile bool stopping;
        string lastProblem = "";
        readonly object sync = new object();

        AudioRecorder.IAudioClient client;
        IRender render;
        int rate, channels, bits, buffer;
        bool isFloat;
        float[] ring = new float[0];
        int head, count;
        bool waiting = true;
        double t, step;
        float lastL, lastR;
        byte[] bytes = new byte[0];

        public StereoOut(string deviceId)
        {
            DeviceId = string.IsNullOrEmpty(deviceId) ? null : deviceId;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "Diversité, sortie";
            thread.Start();
        }

        public void Stop()
        {
            stopping = true;
            thread.Join(3000);
        }

        // Trames stéréo entrelacées à 16 kHz.
        public void Write(float[] x, int frames)
        {
            lock (sync)
            {
                if (rate == 0) return;
                int cap = ring.Length / 2;
                for (int i = 0; i < frames; i++)
                {
                    float l = x[2 * i], r = x[2 * i + 1];
                    while (t < 1.0)
                    {
                        if (count == cap) Drop(cap / 4);
                        int k = (head + count) % cap * 2;
                        ring[k] = lastL + (l - lastL) * (float)t;
                        ring[k + 1] = lastR + (r - lastR) * (float)t;
                        count++;
                        t += step;
                    }
                    t -= 1.0;
                    lastL = l;
                    lastR = r;
                }
            }
        }

        void Drop(int frames)
        {
            frames = Math.Min(frames, count);
            head = (head + frames) % (ring.Length / 2);
            count -= frames;
        }

        void Run()
        {
            while (!stopping)
            {
                if (client == null)
                {
                    try { Open(); Report(null); }
                    catch (Exception ex) { Report(ex.Message); Close(); for (int i = 0; i < 20 && !stopping; i++) Thread.Sleep(50); continue; }
                }
                try { Play(); }
                catch (Exception ex) { Report(ex.Message); Close(); continue; }
                Thread.Sleep(5);
            }
            Close();
        }

        void Report(string message)
        {
            if ((message ?? "") == lastProblem) return;
            lastProblem = message ?? "";
            Action<string> h = Problem;
            if (h != null) h(message);
        }

        void Open()
        {
            string name;
            IntPtr p = DeviceId == null ? AudioEndpoints.ActivateDefaultOutput(typeof(AudioRecorder.IAudioClient).GUID, out name)
                : AudioEndpoints.ActivateDevice(DeviceId, typeof(AudioRecorder.IAudioClient).GUID, out name);
            try { client = (AudioRecorder.IAudioClient)Marshal.GetObjectForIUnknown(p); }
            finally { Marshal.Release(p); }
            OutputName = name ?? "?";
            IntPtr wf;
            Marshal.ThrowExceptionForHR(client.GetMixFormat(out wf));
            try
            {
                int tag = Marshal.ReadInt16(wf, 0) & 0xFFFF;
                int ch = Marshal.ReadInt16(wf, 2), r = Marshal.ReadInt32(wf, 4), b = Marshal.ReadInt16(wf, 14);
                bool fl = tag == 3;
                if (tag == 0xFFFE)
                {
                    byte[] g = new byte[16];
                    Marshal.Copy(new IntPtr(wf.ToInt64() + 24), g, 0, 16);
                    fl = new Guid(g) == FloatFormat;
                }
                if (ch < 1 || r < 8000 || (fl ? b != 32 : b != 16 && b != 24 && b != 32))
                    throw new IOException(Lang.F("format audio non pris en charge ({0} bits, {1} Hz)", "unsupported audio format ({0} bits, {1} Hz)", b, r));
                Marshal.ThrowExceptionForHR(client.Initialize(AUDCLNT_SHAREMODE_SHARED, 0, 1000000, 0, wf, IntPtr.Zero));
                lock (sync)
                {
                    channels = ch; rate = r; bits = b; isFloat = fl;
                    ring = new float[rate * 2];   // 1 s au plus
                    head = count = 0;
                    waiting = true;
                    step = (double)DiversityMixer.Rate / rate;
                    t = 0;
                }
            }
            finally { Marshal.FreeCoTaskMem(wf); }
            Guid iid = typeof(IRender).GUID;
            IntPtr s;
            Marshal.ThrowExceptionForHR(client.GetService(ref iid, out s));
            try { render = (IRender)Marshal.GetObjectForIUnknown(s); }
            finally { Marshal.Release(s); }
            Marshal.ThrowExceptionForHR(client.GetBufferSize(out buffer));
            Marshal.ThrowExceptionForHR(client.Start());
            Log.Write("Diversité : sortie « " + OutputName + " » (" + rate + " Hz, " + channels + " voies)");
        }

        void Close()
        {
            if (client != null) { try { client.Stop(); } catch { } }
            if (render != null) { Marshal.ReleaseComObject(render); render = null; }
            if (client != null) { Marshal.ReleaseComObject(client); client = null; }
            lock (sync) rate = 0;
        }

        void Play()
        {
            int padding;
            Marshal.ThrowExceptionForHR(client.GetCurrentPadding(out padding));
            lock (sync)
            {
                // Matelas : 80 ms avant de (re)partir ; au-delà de 350 ms en attente, on revient à 150 ms.
                if (count > rate * 35 / 100) Drop(count - rate * 15 / 100);
                if (waiting)
                {
                    if (count < rate * 8 / 100) return;
                    waiting = false;
                }
                if (count == 0 && padding == 0) { waiting = true; return; }
                int todo = Math.Min(buffer - padding, count);
                if (todo <= 0) return;
                IntPtr buf;
                Marshal.ThrowExceptionForHR(render.GetBuffer(todo, out buf));
                int bpp = bits / 8, size = todo * bpp * channels;
                if (bytes.Length < size) bytes = new byte[size];
                int o = 0, cap = ring.Length / 2;
                for (int i = 0; i < todo; i++)
                {
                    int k = (head + i) % cap * 2;
                    float l = ring[k], r = ring[k + 1];
                    for (int c = 0; c < channels; c++)
                    {
                        float v = channels == 1 ? (l + r) * 0.5f : c == 0 ? l : c == 1 ? r : 0;
                        Put(v, o, bpp);
                        o += bpp;
                    }
                }
                Marshal.Copy(bytes, 0, buf, size);
                Marshal.ThrowExceptionForHR(render.ReleaseBuffer(todo, 0));
                Drop(todo);
            }
        }

        void Put(float v, int o, int bpp)
        {
            if (isFloat) { byte[] b = BitConverter.GetBytes(v); Buffer.BlockCopy(b, 0, bytes, o, 4); return; }
            double d = Math.Max(-1.0, Math.Min(1.0, v));
            if (bpp == 2) { int s = (int)Math.Round(d * 32767); bytes[o] = (byte)s; bytes[o + 1] = (byte)(s >> 8); }
            else if (bpp == 3) { int s = (int)Math.Round(d * 8388607); bytes[o] = (byte)s; bytes[o + 1] = (byte)(s >> 8); bytes[o + 2] = (byte)(s >> 16); }
            else { int s = (int)Math.Round(d * 2147483647.0); bytes[o] = (byte)s; bytes[o + 1] = (byte)(s >> 8); bytes[o + 2] = (byte)(s >> 16); bytes[o + 3] = (byte)(s >> 24); }
        }

        [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IRender
        {
            [PreserveSig] int GetBuffer(int frames, out IntPtr data);
            [PreserveSig] int ReleaseBuffer(int frames, int flags);
        }
    }
}
