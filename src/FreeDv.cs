using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace SDRLink
{
    // FreeDV RADE (bouton « FreeDV auto ») : DLL native sdrlink_freedv.dll (radae_nopy de David Rowe et Peter B Marks, BSD ;
    // vocodeur FARGAN d'Opus, BSD), embarquée compressée dans l'exe et écrite sur le disque seulement au premier usage.
    // Rien n'est chargé tant que FreeDV auto n'est pas activé.
    static class FreeDvNative
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr NewFn(int v2);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void FreeFn(IntPtr s);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int ProcessFn(IntPtr s, float[] input, int n, short[] output, int outMax, out int sync, out int snr, out float foff);

        public static NewFn New;
        public static FreeFn Free;
        public static ProcessFn Process;
        static string error;
        static readonly object sync = new object();

        // Charge la DLL (une fois) ; null si tout va bien, sinon la raison.
        public static string Load()
        {
            lock (sync)
            {
                if (Process != null) return null;
                if (error != null) return error;
                try
                {
                    byte[] dll;
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("sdrlink_freedv.dll.gz"))
                    {
                        if (s == null) throw new IOException(Lang.T("décodeur absent de cette version", "decoder missing from this build"));
                        using (GZipStream z = new GZipStream(s, CompressionMode.Decompress))
                        using (MemoryStream m = new MemoryStream())
                        {
                            z.CopyTo(m);
                            dll = m.ToArray();
                        }
                    }
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SDRLink", "runtime", "freedv-" + dll.Length);
                    string path = Path.Combine(dir, "sdrlink_freedv.dll");
                    if (!File.Exists(path) || new FileInfo(path).Length != dll.Length)
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllBytes(path, dll);
                    }
                    IntPtr h = LoadLibrary(path);
                    if (h == IntPtr.Zero) throw new IOException("LoadLibrary : " + Marshal.GetLastWin32Error());
                    New = Get<NewFn>(h, "sdrdec_rade_new_v");
                    Free = Get<FreeFn>(h, "sdrdec_rade_free");
                    Process = Get<ProcessFn>(h, "sdrdec_rade_process");
                    Log.Write("FreeDV : décodeur chargé (" + path + ")");
                    return null;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Log.Write("FreeDV : décodeur non chargé : " + ex.Message);
                    return error;
                }
            }
        }

        static T Get<T>(IntPtr h, string name) where T : class
        {
            IntPtr p = GetProcAddress(h, name);
            if (p == IntPtr.Zero) throw new IOException(name + " absent");
            return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }
    }

    // Décodage FreeDV RADE du son reçu, tel que le poste ou le WebSDR le démodule (USB ou LSB, au choix de l'opérateur) :
    // ramené à 8 kHz, décodé, parole 16 kHz jouée sur la sortie par défaut de Windows. Le calcul se fait dans un fil à part.
    public class FreeDvEngine
    {
        public readonly bool V2;
        // Parole décodée ou non (fil de calcul) : voix, SNR (dB).
        public event Action<bool, int> VoiceChanged;
        public volatile bool OutputMuted;           // émission : la parole n'est pas jouée
        public volatile bool Silent;                // essais : la parole n'est jamais jouée
        long speechSamples;
        public long SpeechSamples { get { return Interlocked.Read(ref speechSamples); } }
        volatile int snrNow;
        public int Snr { get { return snrNow; } }   // SNR de la parole en cours (dB)

        const int VoiceHoldMs = 1500, InputHoldMs = 500;
        // Sous ce SNR (dB), la parole est ignorée : le décodeur peut s'accrocher à tort (par exemple à un signal FreeDV reçu
        // sur l'autre bande latérale) et ne sortir que du charabia, avec un SNR annoncé très bas (vers −15 dB).
        const int MinSnr = -5;
        const int StartSamples = 9600;   // 0,6 s de parole à 16 kHz
        readonly Thread thread;
        volatile bool stopping;
        readonly object qlock = new object();
        readonly Queue<float[]> queue = new Queue<float[]>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        int queued;
        volatile bool resetPending;

        // Passage à 8 kHz (moyenne sur chaque intervalle de 1/8000 s).
        int inRate;
        double pos, acc;
        int accN;
        float[] blk = new float[400];
        int blkN;

        public FreeDvEngine(bool v2)
        {
            V2 = v2;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "FreeDV";
            thread.Start();
        }

        public void Stop()
        {
            stopping = true;
            wake.Set();
            thread.Join(3000);
        }

        // Nouvelle source : décodeur remis à zéro.
        public void Reset()
        {
            lock (qlock) { queue.Clear(); queued = 0; blkN = 0; acc = 0; accN = 0; pos = 0; }
            resetPending = true;
            wake.Set();
        }

        // Son 16 bits mono (bloc de la page WebSDR).
        public void Feed16(int rate, byte[] pcm)
        {
            float[] f = new float[pcm.Length / 2];
            for (int i = 0; i < f.Length; i++) f[i] = (short)(pcm[2 * i] | pcm[2 * i + 1] << 8) / 32768f;
            Feed(rate, f, f.Length);
        }

        public void Feed(int rate, float[] x, int n)
        {
            if (rate < 8000) return;
            lock (qlock)
            {
                if (rate != inRate) { inRate = rate; pos = 0; acc = 0; accN = 0; }
                double step = 8000.0 / rate;
                for (int i = 0; i < n; i++)
                {
                    acc += x[i];
                    accN++;
                    pos += step;
                    if (pos < 1.0) continue;
                    pos -= 1.0;
                    blk[blkN++] = (float)(acc / accN);
                    acc = 0;
                    accN = 0;
                    if (blkN == blk.Length)
                    {
                        if (queued < 40) { queue.Enqueue(blk); queued++; }   // 2 s au plus en attente
                        blk = new float[400];
                        blkN = 0;
                    }
                }
            }
            wake.Set();
        }

        void Run()
        {
            IntPtr dec = IntPtr.Zero;
            PcmOut player = null;
            short[] speech = new short[16000];
            bool voice = false;
            int pending = 0;
            DateTime lastSpeech = DateTime.MinValue, lastInput = DateTime.UtcNow;
            int snr = 0;
            try
            {
                dec = NewDecoder();
                player = new PcmOut(16000);
                while (!stopping)
                {
                    wake.WaitOne(20);
                    if (resetPending)
                    {
                        resetPending = false;
                        FreeDvNative.Free(dec);
                        dec = IntPtr.Zero;
                        dec = NewDecoder();
                    }
                    while (true)
                    {
                        float[] b;
                        lock (qlock) { if (queue.Count == 0) break; b = queue.Dequeue(); queued--; }
                        lastInput = DateTime.UtcNow;
                        int sync, s;
                        float foff;
                        int got = FreeDvNative.Process(dec, b, b.Length, speech, speech.Length, out sync, out s, out foff);
                        if (got == 0 || s < MinSnr) continue;
                        DateTime t = DateTime.UtcNow;
                        if (!voice)
                        {
                            // Parole annoncée seulement après 0,6 s de décodage continu (pas sur un éclair isolé).
                            pending = (t - lastSpeech).TotalMilliseconds > 500 ? got : pending + got;
                            lastSpeech = t;
                            if (pending < StartSamples) continue;
                            voice = true;
                            Raise(true, s);
                        }
                        lastSpeech = t;
                        Interlocked.Add(ref speechSamples, got);
                        snr = snrNow = s;
                        if (!OutputMuted && !Silent) player.Write(speech, got);
                    }
                    DateTime now = DateTime.UtcNow;
                    if (voice && ((now - lastSpeech).TotalMilliseconds > VoiceHoldMs || (now - lastInput).TotalMilliseconds > InputHoldMs))
                    {
                        voice = false;
                        player.Clear();
                        Raise(false, snr);
                    }
                    if (OutputMuted) player.Clear();
                    player.Pump();
                }
            }
            catch (Exception ex) { Log.Write("FreeDV : arrêt du décodage : " + ex.Message); }
            finally
            {
                if (voice) Raise(false, snr);
                if (dec != IntPtr.Zero) FreeDvNative.Free(dec);
                if (player != null) player.Close();
            }
        }

        IntPtr NewDecoder()
        {
            IntPtr d = FreeDvNative.New(V2 ? 1 : 0);
            if (d == IntPtr.Zero) throw new IOException("sdrdec_rade_new_v");
            return d;
        }

        void Raise(bool on, int snr)
        {
            Action<bool, int> h = VoiceChanged;
            if (h != null) h(on, snr);
        }
    }

    // Sortie de la parole décodée : 16 bits mono à une fréquence donnée, rejoué sur la sortie par défaut de Windows
    // (WASAPI partagé, au format de la sortie). Pump est appelé régulièrement par le fil qui écrit.
    public class PcmOut
    {
        const int AUDCLNT_SHAREMODE_SHARED = 0;
        static readonly Guid FloatFormat = new Guid("00000003-0000-0010-8000-00aa00389b71");
        readonly int inRate;
        AudioRecorder.IAudioClient client;
        RigMonitor.IAudioRenderClient render;
        int outRate, channels, bits, bufferFrames;
        bool isFloat;
        float[] ring = new float[0];
        int head, count;
        double step, t;
        float last;
        byte[] bytes = new byte[0];
        DateTime retryAt;
        bool waiting = true;

        public PcmOut(int rate) { inRate = rate; }

        public void Write(short[] pcm, int n)
        {
            if (client == null) Open();
            if (client == null) return;
            int cap = ring.Length;
            for (int i = 0; i < n; i++)
            {
                float v = pcm[i] / 32768f;
                while (t < 1.0)
                {
                    if (count == cap) { head = (head + 1) % cap; count--; }
                    ring[(head + count) % cap] = last + (v - last) * (float)t;
                    count++;
                    t += step;
                }
                t -= 1.0;
                last = v;
            }
        }

        public void Clear() { count = 0; waiting = true; }

        public void Pump()
        {
            if (client == null) return;
            try
            {
                if (count > outRate * 3 / 10) { int drop = count - outRate / 10; head = (head + drop) % ring.Length; count -= drop; }
                int padding;
                Marshal.ThrowExceptionForHR(client.GetCurrentPadding(out padding));
                if (waiting) { if (count < outRate * 6 / 100) return; waiting = false; }
                if (count == 0) { if (padding == 0) waiting = true; return; }
                int todo = Math.Min(bufferFrames - padding, count);
                if (todo <= 0) return;
                IntPtr buf;
                Marshal.ThrowExceptionForHR(render.GetBuffer(todo, out buf));
                int bpp = bits / 8, size = todo * bpp * channels;
                if (bytes.Length < size) bytes = new byte[size];
                int o = 0;
                for (int i = 0; i < todo; i++)
                {
                    float v = ring[(head + i) % ring.Length];
                    for (int c = 0; c < channels; c++)
                    {
                        if (isFloat) Buffer.BlockCopy(BitConverter.GetBytes(v), 0, bytes, o, 4);
                        else
                        {
                            double d = Math.Max(-1.0, Math.Min(1.0, v));
                            int s = bpp == 2 ? (int)Math.Round(d * 32767) : bpp == 3 ? (int)Math.Round(d * 8388607) : (int)Math.Round(d * 2147483647.0);
                            for (int k = 0; k < bpp; k++) bytes[o + k] = (byte)(s >> (8 * k));
                        }
                        o += bpp;
                    }
                }
                Marshal.Copy(bytes, 0, buf, size);
                Marshal.ThrowExceptionForHR(render.ReleaseBuffer(todo, 0));
                head = (head + todo) % ring.Length;
                count -= todo;
            }
            catch (Exception ex) { Log.Write("FreeDV : sortie audio perdue : " + ex.Message); Close(); }
        }

        void Open()
        {
            if (DateTime.UtcNow < retryAt) return;
            retryAt = DateTime.UtcNow.AddSeconds(2);
            try
            {
                string name;
                IntPtr p = AudioEndpoints.ActivateDefaultOutput(typeof(AudioRecorder.IAudioClient).GUID, out name);
                try { client = (AudioRecorder.IAudioClient)Marshal.GetObjectForIUnknown(p); }
                finally { Marshal.Release(p); }
                IntPtr wf;
                Marshal.ThrowExceptionForHR(client.GetMixFormat(out wf));
                try
                {
                    int tag = Marshal.ReadInt16(wf, 0) & 0xFFFF;
                    channels = Marshal.ReadInt16(wf, 2);
                    outRate = Marshal.ReadInt32(wf, 4);
                    bits = Marshal.ReadInt16(wf, 14);
                    isFloat = tag == 3;
                    if (tag == 0xFFFE)
                    {
                        byte[] g = new byte[16];
                        Marshal.Copy(new IntPtr(wf.ToInt64() + 24), g, 0, 16);
                        isFloat = new Guid(g) == FloatFormat;
                    }
                    if (channels < 1 || outRate < 8000 || (isFloat ? bits != 32 : bits != 16 && bits != 24 && bits != 32))
                        throw new IOException(Lang.F("format audio non pris en charge ({0} bits, {1} Hz)", "unsupported audio format ({0} bits, {1} Hz)", bits, outRate));
                    Marshal.ThrowExceptionForHR(client.Initialize(AUDCLNT_SHAREMODE_SHARED, 0, 2000000, 0, wf, IntPtr.Zero));
                }
                finally { Marshal.FreeCoTaskMem(wf); }
                Guid iid = typeof(RigMonitor.IAudioRenderClient).GUID;
                IntPtr r;
                Marshal.ThrowExceptionForHR(client.GetService(ref iid, out r));
                try { render = (RigMonitor.IAudioRenderClient)Marshal.GetObjectForIUnknown(r); }
                finally { Marshal.Release(r); }
                Marshal.ThrowExceptionForHR(client.GetBufferSize(out bufferFrames));
                ring = new float[outRate * 2];
                head = count = 0;
                step = (double)inRate / outRate;
                t = 0;
                waiting = true;
                Marshal.ThrowExceptionForHR(client.Start());
                Log.Write("FreeDV : parole jouée sur « " + name + " » (" + outRate + " Hz)");
            }
            catch (Exception ex) { Log.Write("FreeDV : sortie audio indisponible : " + ex.Message); Close(); }
        }

        public void Close()
        {
            if (client != null) { try { client.Stop(); } catch { } }
            if (render != null) { Marshal.ReleaseComObject(render); render = null; }
            if (client != null) { Marshal.ReleaseComObject(client); client = null; }
        }
    }

    // Capture d'un son pour FreeDV : entrée d'une carte son (celle du poste), ou son d'un programme et de ses
    // sous-processus (ExpertSDR, HDSDR) par la capture par processus de Windows 10 (version 2004 et suivantes).
    // Le son est passé en mono flottant au rappel, depuis le fil de capture.
    public class AudioTap
    {
        const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x20000, AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x40000,
            AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = unchecked((int)0x80000000), AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000,
            AUDCLNT_BUFFERFLAGS_SILENT = 2;
        static readonly Guid FloatFormat = new Guid("00000003-0000-0010-8000-00aa00389b71");

        public readonly string CaptureId;    // carte son (null si programme)
        public readonly int ProcessId;       // programme (0 si carte son)
        public readonly string Name;
        readonly Action<int, float[], int> sink;
        readonly Thread thread;
        volatile bool stopping;
        string lastProblem = "";
        public event Action<string> Problem;

        AudioRecorder.IAudioClient client;
        AudioRecorder.IAudioCaptureClient capture;
        int rate, channels, bits;
        bool isFloat;
        byte[] scratch = new byte[0];
        float[] mono = new float[0];
        AutoResetEvent ready;

        public AudioTap(string captureId, int processId, string name, Action<int, float[], int> sink)
        {
            CaptureId = captureId;
            ProcessId = processId;
            Name = name;
            this.sink = sink;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "FreeDV, capture";
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
                if (client == null)
                {
                    try { Open(); Report(null); }
                    catch (Exception ex) { Report(ex.Message); Close(); for (int i = 0; i < 20 && !stopping; i++) Thread.Sleep(50); continue; }
                }
                try
                {
                    if (ready != null) ready.WaitOne(50); else Thread.Sleep(10);
                    Drain();
                }
                catch (Exception ex) { Report(ex.Message); Close(); }
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
            if (ProcessId == 0)
            {
                string name;
                IntPtr p = AudioEndpoints.ActivateDevice(CaptureId, typeof(AudioRecorder.IAudioClient).GUID, out name);
                try { client = (AudioRecorder.IAudioClient)Marshal.GetObjectForIUnknown(p); }
                finally { Marshal.Release(p); }
                IntPtr wf;
                Marshal.ThrowExceptionForHR(client.GetMixFormat(out wf));
                try
                {
                    ReadFormat(wf);
                    Marshal.ThrowExceptionForHR(client.Initialize(0, 0, 1000000, 0, wf, IntPtr.Zero));
                }
                finally { Marshal.FreeCoTaskMem(wf); }
            }
            else
            {
                client = ProcessLoopback.Activate(ProcessId);
                // Pas de format proposé par Windows en capture par processus : 16 bits stéréo 48 kHz, converti par Windows.
                IntPtr wf = Marshal.AllocCoTaskMem(18);
                try
                {
                    Marshal.WriteInt16(wf, 0, 1); Marshal.WriteInt16(wf, 2, 2); Marshal.WriteInt32(wf, 4, 48000);
                    Marshal.WriteInt32(wf, 8, 48000 * 4); Marshal.WriteInt16(wf, 12, 4); Marshal.WriteInt16(wf, 14, 16); Marshal.WriteInt16(wf, 16, 0);
                    ReadFormat(wf);
                    Marshal.ThrowExceptionForHR(client.Initialize(0, AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
                        AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, 2000000, 0, wf, IntPtr.Zero));
                }
                finally { Marshal.FreeCoTaskMem(wf); }
                ready = new AutoResetEvent(false);
                Marshal.ThrowExceptionForHR(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
            }
            Guid iid = typeof(AudioRecorder.IAudioCaptureClient).GUID;
            IntPtr c;
            Marshal.ThrowExceptionForHR(client.GetService(ref iid, out c));
            try { capture = (AudioRecorder.IAudioCaptureClient)Marshal.GetObjectForIUnknown(c); }
            finally { Marshal.Release(c); }
            Marshal.ThrowExceptionForHR(client.Start());
            Log.Write("FreeDV : capture de « " + Name + " » (" + rate + " Hz, " + channels + " voies)");
        }

        void ReadFormat(IntPtr wf)
        {
            int tag = Marshal.ReadInt16(wf, 0) & 0xFFFF;
            channels = Marshal.ReadInt16(wf, 2);
            rate = Marshal.ReadInt32(wf, 4);
            bits = Marshal.ReadInt16(wf, 14);
            isFloat = tag == 3;
            if (tag == 0xFFFE)
            {
                byte[] g = new byte[16];
                Marshal.Copy(new IntPtr(wf.ToInt64() + 24), g, 0, 16);
                isFloat = new Guid(g) == FloatFormat;
            }
            if (channels < 1 || rate < 8000 || (isFloat ? bits != 32 : bits != 16 && bits != 24 && bits != 32))
                throw new IOException(Lang.F("format audio non pris en charge ({0} bits, {1} Hz)", "unsupported audio format ({0} bits, {1} Hz)", bits, rate));
        }

        void Close()
        {
            if (client != null) { try { client.Stop(); } catch { } }
            if (capture != null) { Marshal.ReleaseComObject(capture); capture = null; }
            if (client != null) { Marshal.ReleaseComObject(client); client = null; }
            if (ready != null) { ready.Dispose(); ready = null; }
        }

        void Drain()
        {
            while (true)
            {
                int n;
                Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out n));
                if (n == 0) return;
                IntPtr data;
                int frames, flags;
                long pos, qpc;
                Marshal.ThrowExceptionForHR(capture.GetBuffer(out data, out frames, out flags, out pos, out qpc));
                try
                {
                    int bpp = bits / 8, align = bpp * channels, size = frames * align;
                    if (mono.Length < frames) mono = new float[frames];
                    if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0) Array.Clear(mono, 0, frames);
                    else
                    {
                        if (scratch.Length < size) scratch = new byte[size];
                        Marshal.Copy(data, scratch, 0, size);
                        for (int i = 0; i < frames; i++)
                        {
                            double sum = 0;
                            for (int c = 0; c < channels; c++)
                            {
                                int o = i * align + c * bpp;
                                sum += isFloat ? BitConverter.ToSingle(scratch, o) : bpp == 2 ? BitConverter.ToInt16(scratch, o) / 32768.0
                                    : bpp == 3 ? ((scratch[o] << 8 | scratch[o + 1] << 16 | scratch[o + 2] << 24) >> 8) / 8388608.0
                                    : BitConverter.ToInt32(scratch, o) / 2147483648.0;
                            }
                            mono[i] = (float)(sum / channels);
                        }
                    }
                }
                finally { capture.ReleaseBuffer(frames); }
                sink(rate, mono, frames);
            }
        }
    }

    // Capture par processus de Windows 10 2004+ (ActivateAudioInterfaceAsync sur « VAD\Process_Loopback »).
    static class ProcessLoopback
    {
        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            IntPtr activationParams, IActivateAudioInterfaceCompletionHandler handler, out IActivateAudioInterfaceAsyncOperation op);

        public static AudioRecorder.IAudioClient Activate(int pid)
        {
            // AUDIOCLIENT_ACTIVATION_PARAMS : type 1 (capture par processus), pid, mode 0 (le processus et ses enfants).
            IntPtr prm = Marshal.AllocHGlobal(12), pv = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(prm, 0, 1); Marshal.WriteInt32(prm, 4, pid); Marshal.WriteInt32(prm, 8, 0);
                for (int i = 0; i < 16; i += 4) Marshal.WriteInt32(pv, i, 0);
                Marshal.WriteInt16(pv, 0, 65);              // VT_BLOB
                Marshal.WriteInt32(pv, 8, 12);              // taille
                Marshal.WriteIntPtr(pv, 12, prm);           // données (x86 : pointeur après la taille)
                Handler h = new Handler();
                IActivateAudioInterfaceAsyncOperation op;
                ActivateAudioInterfaceAsync("VAD\\Process_Loopback", typeof(AudioRecorder.IAudioClient).GUID, pv, h, out op);
                if (!h.Done.WaitOne(5000)) throw new IOException(Lang.T("capture du programme sans réponse", "program capture does not answer"));
                int hr;
                object iface;
                op.GetActivateResult(out hr, out iface);
                Marshal.ThrowExceptionForHR(hr);
                return (AudioRecorder.IAudioClient)iface;
            }
            finally { Marshal.FreeHGlobal(prm); Marshal.FreeHGlobal(pv); }
        }

        class Handler : IActivateAudioInterfaceCompletionHandler, IAgileObject
        {
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op) { Done.Set(); }
        }

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IActivateAudioInterfaceCompletionHandler
        {
            void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op);
        }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IActivateAudioInterfaceAsyncOperation
        {
            void GetActivateResult(out int hr, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAgileObject { }
    }
}
