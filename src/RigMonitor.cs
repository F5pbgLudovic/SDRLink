using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace SDRLink
{
    // Écoute du poste sur le PC (clic droit sur Mute TX) : le son de l'entrée de la carte son du poste (celle choisie au
    // clic droit sur REC) est rejoué sur la sortie par défaut de Windows (haut-parleurs), en WASAPI partagé, avec peu de
    // retard (50 à 120 ms). Muted : la carte reste ouverte mais rien n'est joué (Mute TX actif, émission), pour repartir
    // tout de suite.
    public class RigMonitor
    {
        const int AUDCLNT_SHAREMODE_SHARED = 0, AUDCLNT_BUFFERFLAGS_SILENT = 2;
        const long BufferHns = 1000000;   // 100 ms de tampon de chaque côté
        static readonly Guid FloatFormat = new Guid("00000003-0000-0010-8000-00aa00389b71");

        public readonly string CaptureId;
        public volatile bool Muted;
        // Problème d'ouverture ou de lecture (appelé depuis le fil audio, une fois par message différent) ; null = tout va bien.
        public event Action<string> Problem;
        public string CardName { get; private set; }
        public string OutputName { get; private set; }

        readonly Thread thread;
        volatile bool stopping;
        string lastProblem = "";

        AudioRecorder.IAudioClient inClient, outClient;
        AudioRecorder.IAudioCaptureClient capture;
        IAudioRenderClient render;
        Fmt inFmt, outFmt;
        int outBuffer;

        // Son en attente, stéréo à la fréquence de la sortie.
        float[] ring = new float[0];
        int head, count;          // en trames (2 valeurs)
        bool waiting = true;      // après un manque de son : on attend un petit matelas avant de rejouer
        double step, t, lastL, lastR;
        bool haveLast;
        byte[] inBytes = new byte[0], outBytes = new byte[0];

        struct Fmt { public int Rate, Channels, Bits; public bool Float; public int Align { get { return Bits / 8 * Channels; } } }

        public RigMonitor(string captureId, bool muted)
        {
            CaptureId = captureId;
            Muted = muted;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "Écoute du poste";
            thread.Start();
        }

        public void Stop()
        {
            stopping = true;
            thread.Join(3000);
        }

        void Run()
        {
            while (!stopping)
            {
                if (inClient == null)
                {
                    try { Open(); Report(null); }
                    catch (Exception ex) { Report(ex.Message); Close(); Sleep(1000); continue; }
                }
                try { Pump(); }
                catch (Exception ex) { Report(ex.Message); Close(); Sleep(500); continue; }
                Thread.Sleep(5);
            }
            Close();
        }

        void Sleep(int ms) { for (int i = 0; i < ms / 50 && !stopping; i++) Thread.Sleep(50); }

        void Report(string message)
        {
            if ((message ?? "") == lastProblem) return;
            lastProblem = message ?? "";
            Action<string> h = Problem;
            if (h != null) h(message);
        }

        static AudioRecorder.IAudioClient Activate(string id, out string name)
        {
            IntPtr p = id == null ? AudioEndpoints.ActivateDefaultOutput(typeof(AudioRecorder.IAudioClient).GUID, out name)
                : AudioEndpoints.ActivateDevice(id, typeof(AudioRecorder.IAudioClient).GUID, out name);
            try { return (AudioRecorder.IAudioClient)Marshal.GetObjectForIUnknown(p); }
            finally { Marshal.Release(p); }
        }

        static Fmt Init(AudioRecorder.IAudioClient c)
        {
            IntPtr wf;
            Marshal.ThrowExceptionForHR(c.GetMixFormat(out wf));
            try
            {
                Fmt f = new Fmt();
                int tag = Marshal.ReadInt16(wf, 0) & 0xFFFF;
                f.Channels = Marshal.ReadInt16(wf, 2);
                f.Rate = Marshal.ReadInt32(wf, 4);
                f.Bits = Marshal.ReadInt16(wf, 14);
                f.Float = tag == 3;
                if (tag == 0xFFFE)
                {
                    byte[] g = new byte[16];
                    Marshal.Copy(new IntPtr(wf.ToInt64() + 24), g, 0, 16);
                    f.Float = new Guid(g) == FloatFormat;
                }
                if (f.Channels < 1 || f.Rate < 8000 || (f.Float ? f.Bits != 32 : f.Bits != 16 && f.Bits != 24 && f.Bits != 32))
                    throw new IOException(Lang.F("format audio non pris en charge ({0} bits, {1} Hz)", "unsupported audio format ({0} bits, {1} Hz)", f.Bits, f.Rate));
                Marshal.ThrowExceptionForHR(c.Initialize(AUDCLNT_SHAREMODE_SHARED, 0, BufferHns, 0, wf, IntPtr.Zero));
                return f;
            }
            finally { Marshal.FreeCoTaskMem(wf); }
        }

        static T Service<T>(AudioRecorder.IAudioClient c)
        {
            Guid iid = typeof(T).GUID;
            IntPtr p;
            Marshal.ThrowExceptionForHR(c.GetService(ref iid, out p));
            try { return (T)Marshal.GetObjectForIUnknown(p); }
            finally { Marshal.Release(p); }
        }

        void Open()
        {
            string name;
            inClient = Activate(CaptureId, out name);
            CardName = name ?? "?";
            inFmt = Init(inClient);
            capture = Service<AudioRecorder.IAudioCaptureClient>(inClient);
            outClient = Activate(null, out name);
            OutputName = name ?? "?";
            outFmt = Init(outClient);
            render = Service<IAudioRenderClient>(outClient);
            Marshal.ThrowExceptionForHR(outClient.GetBufferSize(out outBuffer));
            ring = new float[outFmt.Rate * 2];   // 1 s au plus
            head = count = 0;
            waiting = true;
            step = (double)inFmt.Rate / outFmt.Rate;
            t = 0;
            haveLast = false;
            Marshal.ThrowExceptionForHR(inClient.Start());
            Marshal.ThrowExceptionForHR(outClient.Start());
            Log.Write("Écoute du poste : « " + CardName + " » (" + inFmt.Rate + " Hz) rejouée sur « " + OutputName + " » (" + outFmt.Rate + " Hz, " + outFmt.Channels + " voies)");
        }

        void Close()
        {
            if (inClient != null) { try { inClient.Stop(); } catch { } }
            if (outClient != null) { try { outClient.Stop(); } catch { } }
            if (capture != null) { Marshal.ReleaseComObject(capture); capture = null; }
            if (render != null) { Marshal.ReleaseComObject(render); render = null; }
            if (inClient != null) { Marshal.ReleaseComObject(inClient); inClient = null; }
            if (outClient != null) { Marshal.ReleaseComObject(outClient); outClient = null; }
        }

        void Pump()
        {
            // Entrée : tout ce que la carte a donné, rééchantillonné à la fréquence de la sortie.
            while (true)
            {
                int n;
                Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out n));
                if (n == 0) break;
                IntPtr data;
                int frames, flags;
                long pos, qpc;
                Marshal.ThrowExceptionForHR(capture.GetBuffer(out data, out frames, out flags, out pos, out qpc));
                try { if (!Muted) Take(data, frames, (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0); }
                finally { capture.ReleaseBuffer(frames); }
            }
            if (Muted) { count = 0; waiting = true; return; }
            Play();
        }

        // Sortie : le son en attente est donné aux haut-parleurs, autant qu'ils en acceptent.
        void Play()
        {
            // Retard limité : au-delà de 120 ms en attente, on saute pour revenir à 50 ms.
            int rate = outFmt.Rate;
            if (count > rate * 12 / 100) Drop(count - rate * 5 / 100);

            int padding;
            Marshal.ThrowExceptionForHR(outClient.GetCurrentPadding(out padding));
            if (waiting)
            {
                if (count < rate * 3 / 100) return;   // matelas de 30 ms avant de (re)commencer
                waiting = false;
            }
            int free = outBuffer - padding, todo = Math.Min(free, count);
            if (count == 0 && padding == 0) { waiting = true; return; }
            if (todo <= 0) return;
            IntPtr buf;
            Marshal.ThrowExceptionForHR(render.GetBuffer(todo, out buf));
            int size = todo * outFmt.Align;
            if (outBytes.Length < size) outBytes = new byte[size];
            int o = 0, bytes = outFmt.Bits / 8;
            for (int i = 0; i < todo; i++)
            {
                int k = (head + i) % (ring.Length / 2) * 2;
                float l = ring[k], r = ring[k + 1];
                for (int c = 0; c < outFmt.Channels; c++)
                {
                    float v = outFmt.Channels == 1 ? (l + r) * 0.5f : c % 2 == 0 ? l : r;
                    Put(v, o, bytes);
                    o += bytes;
                }
            }
            Marshal.Copy(outBytes, 0, buf, size);
            Marshal.ThrowExceptionForHR(render.ReleaseBuffer(todo, 0));
            Drop(todo);
        }

        void Drop(int frames)
        {
            frames = Math.Min(frames, count);
            head = (head + frames) % (ring.Length / 2);
            count -= frames;
        }

        void Put(float v, int o, int bytes)
        {
            if (outFmt.Float) { byte[] b = BitConverter.GetBytes(v); Buffer.BlockCopy(b, 0, outBytes, o, 4); return; }
            double d = Math.Max(-1.0, Math.Min(1.0, v));
            if (bytes == 2) { int s = (int)Math.Round(d * 32767); outBytes[o] = (byte)s; outBytes[o + 1] = (byte)(s >> 8); }
            else if (bytes == 3) { int s = (int)Math.Round(d * 8388607); outBytes[o] = (byte)s; outBytes[o + 1] = (byte)(s >> 8); outBytes[o + 2] = (byte)(s >> 16); }
            else { int s = (int)Math.Round(d * 2147483647.0); outBytes[o] = (byte)s; outBytes[o + 1] = (byte)(s >> 8); outBytes[o + 2] = (byte)(s >> 16); outBytes[o + 3] = (byte)(s >> 24); }
        }

        void Take(IntPtr data, int frames, bool silent)
        {
            int bytes = inFmt.Bits / 8, align = inFmt.Align, size = frames * align;
            if (!silent)
            {
                if (inBytes.Length < size) inBytes = new byte[size];
                Marshal.Copy(data, inBytes, 0, size);
            }
            int cap = ring.Length / 2;
            for (int i = 0; i < frames; i++)
            {
                double l = 0, r = 0;
                if (!silent)
                {
                    l = Sample(i * align, bytes);
                    r = inFmt.Channels > 1 ? Sample(i * align + bytes, bytes) : l;
                }
                if (!haveLast) { lastL = l; lastR = r; haveLast = true; }
                while (t < 1.0)
                {
                    if (count == cap) Drop(1);
                    int k = (head + count) % cap * 2;
                    ring[k] = (float)(lastL + (l - lastL) * t);
                    ring[k + 1] = (float)(lastR + (r - lastR) * t);
                    count++;
                    t += step;
                }
                t -= 1.0;
                lastL = l;
                lastR = r;
            }
        }

        double Sample(int o, int bytes)
        {
            if (inFmt.Float) return BitConverter.ToSingle(inBytes, o);
            if (bytes == 2) return BitConverter.ToInt16(inBytes, o) / 32768.0;
            if (bytes == 3) return ((inBytes[o] << 8 | inBytes[o + 1] << 16 | inBytes[o + 2] << 24) >> 8) / 8388608.0;
            return BitConverter.ToInt32(inBytes, o) / 2147483648.0;
        }

        [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IAudioRenderClient
        {
            [PreserveSig] int GetBuffer(int frames, out IntPtr data);
            [PreserveSig] int ReleaseBuffer(int frames, int flags);
        }
    }
}
