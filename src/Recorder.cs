using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace SDRLink
{
    // Bouton REC : enregistrement du son joué par la sortie audio par défaut de Windows (ce qu'on entend dans les
    // haut-parleurs : WebSDR, ExpertSDR, poste relié au PC…) par la capture « loopback » de WASAPI, ou de l'entrée de la
    // carte son du poste quand seul le poste est écouté (Mute WebSDR et Mute ExpertSDR actifs). Le son est gardé en
    // PCM 16 bits stéréo dans un fichier temporaire, puis enregistré en MP3, WAV ou FLAC (AudioFile).
    public class AudioRecorder
    {
        const int AUDCLNT_SHAREMODE_SHARED = 0, AUDCLNT_STREAMFLAGS_LOOPBACK = 0x20000, AUDCLNT_BUFFERFLAGS_SILENT = 2;
        static readonly Guid FloatFormat = new Guid("00000003-0000-0010-8000-00aa00389b71");

        public readonly string TempPath;
        public int SampleRate { get; private set; }     // 44 100 ou 48 000 Hz (le MP3 ne va pas au-delà)
        public string DeviceName { get; private set; }
        long frames;
        public long Frames { get { return Interlocked.Read(ref frames); } }
        public TimeSpan Duration { get { return TimeSpan.FromSeconds(SampleRate > 0 ? (double)Frames / SampleRate : 0); } }

        string captureId;
        public bool FromCard { get { return captureId != null; } }
        readonly Thread thread;
        readonly ManualResetEvent started = new ManualResetEvent(false);
        volatile bool stopping;
        string startError, lastError;
        FileStream file;
        byte[] pending = new byte[65536];
        int pendingLength;

        // Sortie en cours : format de son de Windows, et passage à la fréquence d'enregistrement.
        IAudioClient client;
        IAudioCaptureClient capture;
        int inRate, inChannels, inBits;
        bool inFloat;
        double step, t;          // rééchantillonnage linéaire : pas, et position entre l'échantillon précédent et le suivant
        double lastL, lastR;
        bool haveLast;
        byte[] scratch = new byte[0];

        AudioRecorder()
        {
            TempPath = Path.Combine(Path.GetTempPath(), "SDRLink-REC-" + DateTime.Now.Ticks + ".raw");
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "REC";
        }

        // Démarre l'enregistrement ; exception si la sortie audio ne peut pas être capturée.
        // captureId : entrée d'une carte son (celle du poste) ; null = son des haut-parleurs (sortie par défaut).
        public static AudioRecorder Start(string captureId)
        {
            AudioRecorder r = new AudioRecorder();
            r.captureId = captureId;
            r.file = new FileStream(r.TempPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            r.thread.Start();
            r.started.WaitOne(5000);
            if (r.startError != null || r.SampleRate == 0)
            {
                r.Stop();
                r.Discard();
                throw new IOException(r.startError ?? Lang.T("la sortie audio ne répond pas", "the audio output does not answer"));
            }
            return r;
        }

        public void Stop()
        {
            stopping = true;
            thread.Join(5000);
            if (file != null) { file.Dispose(); file = null; }
        }

        // Fichier temporaire effacé (enregistrement sauvegardé ou abandonné).
        public void Discard()
        {
            try { File.Delete(TempPath); } catch { }
        }

        void Run()
        {
            Stopwatch clock = new Stopwatch();
            long lastPacket = 0;
            try
            {
                Open();
                SampleRate = inRate % 44100 == 0 ? 44100 : 48000;
                Retune();
            }
            catch (Exception ex)
            {
                startError = ex.Message;
                Close();
                started.Set();
                return;
            }
            clock.Start();
            started.Set();
            while (!stopping)
            {
                if (client == null)
                {
                    // Sortie perdue (périphérique débranché ou changé) : on reprend la nouvelle sortie par défaut (ou la carte rebranchée).
                    try { Open(); Retune(); }
                    catch (Exception ex) { Note(ex.Message); Close(); }
                }
                if (client != null)
                {
                    try { if (Drain()) lastPacket = clock.ElapsedMilliseconds; }
                    catch (Exception ex) { Note(ex.Message); Close(); }
                }
                // Rien n'est joué : Windows ne donne plus de son, on complète par du silence pour garder la durée réelle.
                if (clock.ElapsedMilliseconds - lastPacket > 100)
                {
                    long behind = (long)(clock.Elapsed.TotalSeconds * SampleRate) - Frames - SampleRate / 50;
                    if (behind > 0) { for (long i = 0; i < behind; i++) Emit(0, 0); haveLast = false; }
                }
                Flush();
                Thread.Sleep(client == null ? 500 : 10);
            }
            try { if (client != null) Drain(); } catch { }
            Flush();
            Close();
        }

        void Note(string message)
        {
            if (message == lastError) return;
            lastError = message;
            Log.Write("Enregistrement : " + message);
        }

        void Open()
        {
            string name;
            IntPtr p = captureId == null ? AudioEndpoints.ActivateDefaultOutput(typeof(IAudioClient).GUID, out name)
                : AudioEndpoints.ActivateDevice(captureId, typeof(IAudioClient).GUID, out name);
            try { client = (IAudioClient)Marshal.GetObjectForIUnknown(p); }
            finally { Marshal.Release(p); }
            DeviceName = name ?? "?";
            IntPtr wf;
            Marshal.ThrowExceptionForHR(client.GetMixFormat(out wf));
            try
            {
                int tag = Marshal.ReadInt16(wf, 0) & 0xFFFF;
                inChannels = Marshal.ReadInt16(wf, 2);
                inRate = Marshal.ReadInt32(wf, 4);
                inBits = Marshal.ReadInt16(wf, 14);
                inFloat = tag == 3;
                if (tag == 0xFFFE)   // WAVEFORMATEXTENSIBLE : le vrai format est dans SubFormat
                {
                    byte[] g = new byte[16];
                    Marshal.Copy(new IntPtr(wf.ToInt64() + 24), g, 0, 16);
                    inFloat = new Guid(g) == FloatFormat;
                }
                if (inChannels < 1 || inRate < 8000 || (inFloat ? inBits != 32 : inBits != 16 && inBits != 24 && inBits != 32))
                    throw new IOException(Lang.F("format audio non pris en charge ({0} bits, {1} Hz)", "unsupported audio format ({0} bits, {1} Hz)", inBits, inRate));
                Marshal.ThrowExceptionForHR(client.Initialize(AUDCLNT_SHAREMODE_SHARED, captureId == null ? AUDCLNT_STREAMFLAGS_LOOPBACK : 0, 10000000, 0, wf, IntPtr.Zero));
            }
            finally { Marshal.FreeCoTaskMem(wf); }
            Guid iid = typeof(IAudioCaptureClient).GUID;
            IntPtr c;
            Marshal.ThrowExceptionForHR(client.GetService(ref iid, out c));
            try { capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(c); }
            finally { Marshal.Release(c); }
            Marshal.ThrowExceptionForHR(client.Start());
            Log.Write("Enregistrement : capture de « " + DeviceName + " » (" + inRate + " Hz, " + inChannels + " voies, " + inBits + " bits" + (inFloat ? " flottants" : "") + ")");
        }

        void Retune()
        {
            step = (double)inRate / SampleRate;
            t = 0;
            haveLast = false;
        }

        void Close()
        {
            if (client != null) { try { client.Stop(); } catch { } }
            if (capture != null) { Marshal.ReleaseComObject(capture); capture = null; }
            if (client != null) { Marshal.ReleaseComObject(client); client = null; }
        }

        // Lit tout le son disponible ; true si Windows en a donné.
        bool Drain()
        {
            bool any = false;
            while (true)
            {
                int n;
                Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out n));
                if (n == 0) return any;
                IntPtr data;
                int count, flags;
                long pos, qpc;
                Marshal.ThrowExceptionForHR(capture.GetBuffer(out data, out count, out flags, out pos, out qpc));
                try { Convert(data, count, (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0); }
                finally { capture.ReleaseBuffer(count); }
                any = true;
            }
        }

        void Convert(IntPtr data, int count, bool silent)
        {
            int bytesPer = inBits / 8, align = bytesPer * inChannels, size = count * align;
            if (!silent)
            {
                if (scratch.Length < size) scratch = new byte[size];
                Marshal.Copy(data, scratch, 0, size);
            }
            for (int i = 0; i < count; i++)
            {
                double l = 0, r = 0;
                if (!silent)
                {
                    l = Sample(i * align);
                    r = inChannels > 1 ? Sample(i * align + bytesPer) : l;
                }
                if (!haveLast) { lastL = l; lastR = r; haveLast = true; }
                while (t < 1.0)
                {
                    Emit(lastL + (l - lastL) * t, lastR + (r - lastR) * t);
                    t += step;
                }
                t -= 1.0;
                lastL = l;
                lastR = r;
            }
        }

        // Échantillon de -1 à 1.
        double Sample(int o)
        {
            if (inFloat) return BitConverter.ToSingle(scratch, o);
            if (inBits == 16) return BitConverter.ToInt16(scratch, o) / 32768.0;
            if (inBits == 24) return ((scratch[o] << 8 | scratch[o + 1] << 16 | scratch[o + 2] << 24) >> 8) / 8388608.0;
            return BitConverter.ToInt32(scratch, o) / 2147483648.0;
        }

        void Emit(double l, double r)
        {
            if (pendingLength + 4 > pending.Length) Flush();
            Put(l);
            Put(r);
            Interlocked.Increment(ref frames);
        }

        void Put(double v)
        {
            int s = (int)Math.Round(v * 32767);
            s = Math.Max(-32768, Math.Min(32767, s));
            pending[pendingLength++] = (byte)s;
            pending[pendingLength++] = (byte)(s >> 8);
        }

        void Flush()
        {
            if (pendingLength == 0) return;
            try { file.Write(pending, 0, pendingLength); }
            catch (Exception ex) { Note(ex.Message); }
            pendingLength = 0;
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
            [PreserveSig] int GetBufferSize(out int frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out int frames);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid iid, out IntPtr service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
            [PreserveSig] int ReleaseBuffer(int frames);
            [PreserveSig] int GetNextPacketSize(out int frames);
        }
    }

    // Enregistrement du son capturé (PCM 16 bits stéréo) en MP3 (codeur de Windows, Media Foundation), WAV ou FLAC.
    public static class AudioFile
    {
        public static readonly string[] Formats = { "MP3", "WAV", "FLAC" };
        public static readonly string[] Extensions = { ".mp3", ".wav", ".flac" };

        public static void Save(string rawPath, int rate, long frames, string path, int format, Action<int> progress)
        {
            try
            {
                if (format == 0) Mp3.Write(rawPath, rate, frames, path, progress);
                else if (format == 1) WriteWav(rawPath, rate, frames, path, progress);
                else Flac.Write(rawPath, rate, frames, path, progress);
            }
            catch
            {
                try { File.Delete(path); } catch { }
                throw;
            }
        }

        // Parcourt le son par blocs de « frames » échantillons stéréo ; progress reçoit le pourcentage.
        static void ReadBlocks(string rawPath, long total, int blockFrames, Action<byte[], int> block, Action<int> progress)
        {
            byte[] buf = new byte[blockFrames * 4];
            long done = 0;
            int shown = -1;
            using (FileStream raw = new FileStream(rawPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
            {
                while (done < total)
                {
                    int want = (int)Math.Min(blockFrames, total - done), got = 0;
                    while (got < want * 4)
                    {
                        int n = raw.Read(buf, got, want * 4 - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    if (got < want * 4) Array.Clear(buf, got, want * 4 - got);   // fichier plus court : silence
                    block(buf, want);
                    done += want;
                    int pc = (int)(done * 100 / total);
                    if (pc != shown && progress != null) { shown = pc; progress(pc); }
                }
            }
        }

        static void WriteWav(string rawPath, int rate, long frames, string path, Action<int> progress)
        {
            long data = frames * 4;
            if (data > uint.MaxValue - 36) throw new IOException(Lang.T("enregistrement trop long pour le format WAV (4 Go au plus)", "recording too long for the WAV format (4 GB at most)"));
            using (FileStream f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (BinaryWriter w = new BinaryWriter(f))
            {
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write((uint)(36 + data));
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);
                w.Write((short)1);          // PCM
                w.Write((short)2);
                w.Write(rate);
                w.Write(rate * 4);
                w.Write((short)4);
                w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write((uint)data);
                ReadBlocks(rawPath, frames, 65536, delegate(byte[] b, int n) { w.Write(b, 0, n * 4); }, progress);
            }
        }

        // ------------------------------------------------------------ FLAC (sans perte)

        // Codeur FLAC simple : blocs de 4096, prédicteurs fixes (ordres 0 à 4), stéréo indépendante ou mid/side,
        // résidu en codes de Rice par partitions. Somme MD5 du son dans l'en-tête, comme les autres codeurs.
        static class Flac
        {
            const int BlockSize = 4096, MaxPartitionOrder = 6;

            public static void Write(string rawPath, int rate, long frames, string path, Action<int> progress)
            {
                using (FileStream f = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16))
                using (MD5 md5 = MD5.Create())
                {
                    BitWriter w = new BitWriter();
                    w.Bytes((byte)'f', (byte)'L', (byte)'a', (byte)'C');
                    w.Bits(1, 1); w.Bits(0, 7); w.Bits(34, 24);                  // STREAMINFO, dernier bloc d'en-tête
                    w.Bits(BlockSize, 16); w.Bits(BlockSize, 16);
                    w.Bits(0, 24); w.Bits(0, 24);                                  // tailles de trame inconnues
                    w.Bits((uint)rate, 20); w.Bits(1, 3); w.Bits(15, 5);           // stéréo, 16 bits
                    w.Bits((uint)(frames >> 32) & 0xF, 4); w.Bits((uint)frames, 32);
                    for (int i = 0; i < 16; i++) w.Bits(0, 8);                     // MD5, rempli à la fin
                    f.Write(w.Buffer, 0, w.Length);

                    int[] left = new int[BlockSize], right = new int[BlockSize], mid = new int[BlockSize], side = new int[BlockSize];
                    long frameNumber = 0;
                    Encoder enc = new Encoder();
                    ReadBlocks(rawPath, frames, BlockSize, delegate(byte[] b, int n)
                    {
                        md5.TransformBlock(b, 0, n * 4, null, 0);
                        for (int i = 0; i < n; i++)
                        {
                            left[i] = (short)(b[i * 4] | b[i * 4 + 1] << 8);
                            right[i] = (short)(b[i * 4 + 2] | b[i * 4 + 3] << 8);
                            mid[i] = (left[i] + right[i]) >> 1;
                            side[i] = left[i] - right[i];
                        }
                        enc.Frame(w, frameNumber++, n, left, right, mid, side);
                        f.Write(w.Buffer, 0, w.Length);
                    }, progress);
                    md5.TransformFinalBlock(new byte[0], 0, 0);
                    f.Position = 26;
                    f.Write(md5.Hash, 0, 16);
                }
            }

            // Façon de coder une voie : constante, brute, ou prédicteur fixe avec ses paramètres de Rice.
            class Plan
            {
                public int Kind;            // 0 constante, 1 brute, 2 prédicteur fixe
                public int Order, PartitionOrder;
                public int[] Ks = new int[1 << MaxPartitionOrder];
                public long Bits;
            }

            class Encoder
            {
                readonly uint[][] residual = new uint[5][];
                readonly long[] sums = new long[1 << MaxPartitionOrder];
                readonly Plan[] plans = { new Plan(), new Plan(), new Plan(), new Plan() };
                readonly Plan trial = new Plan();

                public Encoder()
                {
                    for (int i = 0; i < 5; i++) residual[i] = new uint[BlockSize];
                }

                public void Frame(BitWriter w, long number, int n, int[] left, int[] right, int[] mid, int[] side)
                {
                    Plan pl = Choose(plans[0], left, n, 16), pr = Choose(plans[1], right, n, 16);
                    Plan pm = Choose(plans[2], mid, n, 16), ps = Choose(plans[3], side, n, 17);
                    // Indépendantes (1), gauche/side (8), side/droite (9) ou mid/side (10) : la moins coûteuse.
                    long[] cost = { pl.Bits + pr.Bits, pl.Bits + ps.Bits, ps.Bits + pr.Bits, pm.Bits + ps.Bits };
                    int best = 0;
                    for (int i = 1; i < 4; i++) if (cost[i] < cost[best]) best = i;

                    w.Clear();
                    w.Bits(0x3FFE, 14); w.Bits(0, 1); w.Bits(0, 1);    // synchro, taille de bloc fixe
                    w.Bits(7, 4);                                       // taille du bloc sur 16 bits en fin d'en-tête
                    w.Bits(0, 4);                                       // fréquence : celle de STREAMINFO
                    w.Bits((uint)(best == 0 ? 1 : best == 1 ? 8 : best == 2 ? 9 : 10), 4);
                    w.Bits(4, 3); w.Bits(0, 1);                         // 16 bits
                    w.Utf8(number);
                    w.Bits((uint)(n - 1), 16);
                    w.Bits(Crc8(w.Buffer, w.Length), 8);
                    if (best == 0) { Write(w, pl, left, n, 16); Write(w, pr, right, n, 16); }
                    else if (best == 1) { Write(w, pl, left, n, 16); Write(w, ps, side, n, 17); }
                    else if (best == 2) { Write(w, ps, side, n, 17); Write(w, pr, right, n, 16); }
                    else { Write(w, pm, mid, n, 16); Write(w, ps, side, n, 17); }
                    w.Align();
                    w.Bits(Crc16(w.Buffer, w.Length), 16);
                }

                Plan Choose(Plan best, int[] x, int n, int bps)
                {
                    bool constant = true;
                    for (int i = 1; i < n && constant; i++) constant = x[i] == x[0];
                    best.Kind = constant ? 0 : 1;
                    best.Bits = constant ? 8 + bps : 8 + (long)n * bps;
                    if (constant) return best;
                    for (int order = 0; order <= 4 && order < n; order++)
                    {
                        uint[] u = residual[order];
                        for (int i = order; i < n; i++)
                        {
                            int r;
                            switch (order)
                            {
                                case 0: r = x[i]; break;
                                case 1: r = x[i] - x[i - 1]; break;
                                case 2: r = x[i] - 2 * x[i - 1] + x[i - 2]; break;
                                case 3: r = x[i] - 3 * x[i - 1] + 3 * x[i - 2] - x[i - 3]; break;
                                default: r = x[i] - 4 * x[i - 1] + 6 * x[i - 2] - 4 * x[i - 3] + x[i - 4]; break;
                            }
                            u[i] = (uint)((r << 1) ^ (r >> 31));
                        }
                        // Partitions les plus fines possibles, puis regroupées deux à deux.
                        int top = 0;
                        while (top < MaxPartitionOrder && n % (2 << top) == 0 && (n >> (top + 1)) > order) top++;
                        int parts = 1 << top, size = n >> top;
                        for (int p = 0; p < parts; p++)
                        {
                            long s = 0;
                            for (int i = Math.Max(order, p * size); i < (p + 1) * size; i++) s += u[i];
                            sums[p] = s;
                        }
                        for (int po = top; po >= 0; po--)
                        {
                            int count = 1 << po, len = n >> po;
                            long bits = 8 + (long)order * bps + 6;
                            for (int p = 0; p < count; p++)
                            {
                                int k;
                                bits += 4 + RiceBits(sums[p], p == 0 ? len - order : len, out k);
                                trial.Ks[p] = k;
                            }
                            if (bits < best.Bits)
                            {
                                best.Kind = 2;
                                best.Order = order;
                                best.PartitionOrder = po;
                                best.Bits = bits;
                                Array.Copy(trial.Ks, best.Ks, count);
                            }
                            for (int p = 0; p < count / 2; p++) sums[p] = sums[2 * p] + sums[2 * p + 1];
                        }
                    }
                    return best;
                }

                // Coût approché d'une partition en codes de Rice, avec le meilleur paramètre k (0 à 14).
                static long RiceBits(long sum, int count, out int k)
                {
                    int k0 = 0;
                    if (count > 0) while (k0 < 14 && ((long)count << (k0 + 1)) <= sum) k0++;
                    long best = long.MaxValue;
                    k = k0;
                    for (int kk = Math.Max(0, k0 - 1); kk <= Math.Min(14, k0 + 1); kk++)
                    {
                        long bits = (long)count * (kk + 1) + (sum >> kk);
                        if (bits < best) { best = bits; k = kk; }
                    }
                    return best;
                }

                void Write(BitWriter w, Plan p, int[] x, int n, int bps)
                {
                    w.Bits(0, 1);
                    if (p.Kind == 0) { w.Bits(0, 6); w.Bits(0, 1); w.Signed(x[0], bps); return; }
                    if (p.Kind == 1) { w.Bits(1, 6); w.Bits(0, 1); for (int i = 0; i < n; i++) w.Signed(x[i], bps); return; }
                    w.Bits((uint)(8 + p.Order), 6); w.Bits(0, 1);
                    for (int i = 0; i < p.Order; i++) w.Signed(x[i], bps);
                    // Résidu recalculé ici : les tableaux de travail ont servi aux autres voies depuis.
                    w.Bits(0, 2); w.Bits((uint)p.PartitionOrder, 4);
                    int count = 1 << p.PartitionOrder, len = n >> p.PartitionOrder, idx = p.Order;
                    for (int part = 0; part < count; part++)
                    {
                        int k = p.Ks[part];
                        w.Bits((uint)k, 4);
                        for (int end = (part + 1) * len; idx < end; idx++)
                        {
                            int r;
                            int i = idx;
                            switch (p.Order)
                            {
                                case 0: r = x[i]; break;
                                case 1: r = x[i] - x[i - 1]; break;
                                case 2: r = x[i] - 2 * x[i - 1] + x[i - 2]; break;
                                case 3: r = x[i] - 3 * x[i - 1] + 3 * x[i - 2] - x[i - 3]; break;
                                default: r = x[i] - 4 * x[i - 1] + 6 * x[i - 2] - 4 * x[i - 3] + x[i - 4]; break;
                            }
                            w.Rice((uint)((r << 1) ^ (r >> 31)), k);
                        }
                    }
                }
            }

            static uint Crc8(byte[] b, int len)
            {
                int crc = 0;
                for (int i = 0; i < len; i++)
                {
                    crc ^= b[i];
                    for (int j = 0; j < 8; j++) crc = (crc & 0x80) != 0 ? ((crc << 1) ^ 0x07) & 0xFF : (crc << 1) & 0xFF;
                }
                return (uint)crc;
            }

            static uint Crc16(byte[] b, int len)
            {
                int crc = 0;
                for (int i = 0; i < len; i++)
                {
                    crc ^= b[i] << 8;
                    for (int j = 0; j < 8; j++) crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x8005) & 0xFFFF : (crc << 1) & 0xFFFF;
                }
                return (uint)crc;
            }
        }

        // Écriture bit à bit (poids fort d'abord), pour FLAC.
        class BitWriter
        {
            public byte[] Buffer = new byte[1 << 16];
            public int Length;
            ulong acc;
            int count;

            public void Clear() { Length = 0; acc = 0; count = 0; }

            public void Bits(uint value, int bits)
            {
                if (bits == 0) return;
                acc = (acc << bits) | (value & (bits == 32 ? 0xFFFFFFFFu : (1u << bits) - 1));
                count += bits;
                while (count >= 8)
                {
                    count -= 8;
                    if (Length == Buffer.Length) Array.Resize(ref Buffer, Buffer.Length * 2);
                    Buffer[Length++] = (byte)(acc >> count);
                }
            }

            public void Bytes(params byte[] b) { foreach (byte x in b) Bits(x, 8); }
            public void Signed(int v, int bits) { Bits((uint)v, bits); }

            public void Rice(uint u, int k)
            {
                uint q = u >> k;
                while (q >= 32) { Bits(0, 32); q -= 32; }
                Bits(1, (int)q + 1);
                if (k > 0) Bits(u, k);
            }

            public void Align() { if (count > 0) Bits(0, 8 - count); }

            // Numéro de trame codé comme un caractère UTF-8 (étendu à 36 bits).
            public void Utf8(long v)
            {
                if (v < 0x80) { Bits((uint)v, 8); return; }
                int extra = v < 0x800 ? 1 : v < 0x10000 ? 2 : v < 0x200000 ? 3 : v < 0x4000000 ? 4 : v < 0x80000000L ? 5 : 6;
                uint firstMask = (uint)(0xFF00 >> (extra + 1)) & 0xFF;
                Bits(firstMask | (uint)(v >> (6 * extra)), 8);
                for (int i = extra - 1; i >= 0; i--) Bits(0x80 | (uint)((v >> (6 * i)) & 0x3F), 8);
            }
        }

        // ------------------------------------------------------------ MP3 (codeur MP3 de Windows, Media Foundation)

        static class Mp3
        {
            const int MF_VERSION = 0x00020070, Kbps = 128;

            static readonly Guid MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
            static readonly Guid MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
            static readonly Guid MT_AUDIO_NUM_CHANNELS = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
            static readonly Guid MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
            static readonly Guid MT_AUDIO_AVG_BYTES_PER_SECOND = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
            static readonly Guid MT_AUDIO_BLOCK_ALIGNMENT = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
            static readonly Guid MT_AUDIO_BITS_PER_SAMPLE = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
            static readonly Guid MT_ALL_SAMPLES_INDEPENDENT = new Guid("c9173739-5e56-461c-b713-46fb995cb95f");
            static readonly Guid MediaTypeAudio = new Guid("73647561-0000-0010-8000-00aa00389b71");
            static readonly Guid AudioFormatPcm = new Guid("00000001-0000-0010-8000-00aa00389b71");
            static readonly Guid AudioFormatMp3 = new Guid("00000055-0000-0010-8000-00aa00389b71");
            static readonly Guid TRANSCODE_CONTAINERTYPE = new Guid("150ff23f-4abc-478b-ac4f-e1916fba1cca");
            static readonly Guid ContainerMp3 = new Guid("e438b912-83f1-4de6-9e3a-9ffbc6dd24d1");

            public static void Write(string rawPath, int rate, long frames, string path, Action<int> progress)
            {
                Check(MFStartup(MF_VERSION, 0), "MFStartup");
                IMFAttributes attrs = null;
                IMFMediaType output = null, input = null;
                IMFSinkWriter writer = null;
                try
                {
                    Check(MFCreateAttributes(out attrs, 1), "MFCreateAttributes");
                    Guid key = TRANSCODE_CONTAINERTYPE, val = ContainerMp3;
                    Check(attrs.SetGUID(ref key, ref val), "container");
                    Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attrs, out writer), "MFCreateSinkWriterFromURL");

                    Check(MFCreateMediaType(out output), "MFCreateMediaType");
                    SetGuid(output, MT_MAJOR_TYPE, MediaTypeAudio);
                    SetGuid(output, MT_SUBTYPE, AudioFormatMp3);
                    SetInt(output, MT_AUDIO_NUM_CHANNELS, 2);
                    SetInt(output, MT_AUDIO_SAMPLES_PER_SECOND, rate);
                    SetInt(output, MT_AUDIO_AVG_BYTES_PER_SECOND, Kbps * 1000 / 8);
                    SetInt(output, MT_AUDIO_BLOCK_ALIGNMENT, 1);
                    int stream;
                    Check(writer.AddStream(output, out stream), "AddStream (MP3)");

                    Check(MFCreateMediaType(out input), "MFCreateMediaType");
                    SetGuid(input, MT_MAJOR_TYPE, MediaTypeAudio);
                    SetGuid(input, MT_SUBTYPE, AudioFormatPcm);
                    SetInt(input, MT_AUDIO_NUM_CHANNELS, 2);
                    SetInt(input, MT_AUDIO_SAMPLES_PER_SECOND, rate);
                    SetInt(input, MT_AUDIO_BITS_PER_SAMPLE, 16);
                    SetInt(input, MT_AUDIO_BLOCK_ALIGNMENT, 4);
                    SetInt(input, MT_AUDIO_AVG_BYTES_PER_SECOND, rate * 4);
                    SetInt(input, MT_ALL_SAMPLES_INDEPENDENT, 1);
                    Check(writer.SetInputMediaType(stream, input, null), "SetInputMediaType");
                    Check(writer.BeginWriting(), "BeginWriting");

                    long written = 0;
                    ReadBlocks(rawPath, frames, rate, delegate(byte[] b, int n)
                    {
                        IMFMediaBuffer buf;
                        Check(MFCreateMemoryBuffer(n * 4, out buf), "MFCreateMemoryBuffer");
                        IMFSample sample = null;
                        try
                        {
                            IntPtr p;
                            int max, cur;
                            Check(buf.Lock(out p, out max, out cur), "Lock");
                            Marshal.Copy(b, 0, p, n * 4);
                            buf.Unlock();
                            buf.SetCurrentLength(n * 4);
                            Check(MFCreateSample(out sample), "MFCreateSample");
                            sample.AddBuffer(buf);
                            sample.SetSampleTime(written * 10000000L / rate);
                            sample.SetSampleDuration((long)n * 10000000L / rate);
                            Check(writer.WriteSample(stream, sample), "WriteSample");
                            written += n;
                        }
                        finally
                        {
                            if (sample != null) Marshal.ReleaseComObject(sample);
                            Marshal.ReleaseComObject(buf);
                        }
                    }, progress);
                    Check(writer.DoFinalize(), "Finalize");
                }
                finally
                {
                    if (writer != null) Marshal.ReleaseComObject(writer);
                    if (input != null) Marshal.ReleaseComObject(input);
                    if (output != null) Marshal.ReleaseComObject(output);
                    if (attrs != null) Marshal.ReleaseComObject(attrs);
                    MFShutdown();
                }
            }

            static void SetGuid(IMFMediaType t, Guid key, Guid value) { Check(t.SetGUID(ref key, ref value), "SetGUID"); }
            static void SetInt(IMFMediaType t, Guid key, int value) { Check(t.SetUINT32(ref key, value), "SetUINT32"); }

            static void Check(int hr, string what)
            {
                if (hr >= 0) return;
                // Codeur MP3 absent (éditions N de Windows sans le « Media Feature Pack », par exemple).
                if (hr == unchecked((int)0xC00D5212) || hr == unchecked((int)0xC00D36B4) || hr == unchecked((int)0x80040154))
                    throw new IOException(Lang.F("codeur MP3 de Windows indisponible (0x{0:X8}) : choisissez WAV ou FLAC", "Windows MP3 encoder not available (0x{0:X8}): choose WAV or FLAC", hr));
                throw new IOException("MP3 : " + what + " 0x" + hr.ToString("X8"));
            }

            [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
            [DllImport("mfplat.dll")] static extern int MFShutdown();
            [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out IMFAttributes attributes, int size);
            [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IMFMediaType type);
            [DllImport("mfplat.dll")] static extern int MFCreateSample(out IMFSample sample);
            [DllImport("mfplat.dll")] static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);
            [DllImport("mfreadwrite.dll")]
            static extern int MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream, IMFAttributes attributes, out IMFSinkWriter writer);

            // Seules les méthodes appelées ont leur vraie signature ; les autres réservent leur place dans la table.
            [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IMFAttributes
            {
                [PreserveSig] int GetItem(); [PreserveSig] int GetItemType(); [PreserveSig] int CompareItem(); [PreserveSig] int Compare();
                [PreserveSig] int GetUINT32(); [PreserveSig] int GetUINT64(); [PreserveSig] int GetDouble(); [PreserveSig] int GetGUID();
                [PreserveSig] int GetStringLength(); [PreserveSig] int GetString(); [PreserveSig] int GetAllocatedString();
                [PreserveSig] int GetBlobSize(); [PreserveSig] int GetBlob(); [PreserveSig] int GetAllocatedBlob(); [PreserveSig] int GetUnknown();
                [PreserveSig] int SetItem(); [PreserveSig] int DeleteItem(); [PreserveSig] int DeleteAllItems();
                [PreserveSig] int SetUINT32(ref Guid key, int value);
                [PreserveSig] int SetUINT64(); [PreserveSig] int SetDouble();
                [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
                [PreserveSig] int SetString(); [PreserveSig] int SetBlob(); [PreserveSig] int SetUnknown();
                [PreserveSig] int LockStore(); [PreserveSig] int UnlockStore(); [PreserveSig] int GetCount(); [PreserveSig] int GetItemByIndex();
                [PreserveSig] int CopyAllItems();
            }

            [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IMFMediaType
            {
                [PreserveSig] int GetItem(); [PreserveSig] int GetItemType(); [PreserveSig] int CompareItem(); [PreserveSig] int Compare();
                [PreserveSig] int GetUINT32(); [PreserveSig] int GetUINT64(); [PreserveSig] int GetDouble(); [PreserveSig] int GetGUID();
                [PreserveSig] int GetStringLength(); [PreserveSig] int GetString(); [PreserveSig] int GetAllocatedString();
                [PreserveSig] int GetBlobSize(); [PreserveSig] int GetBlob(); [PreserveSig] int GetAllocatedBlob(); [PreserveSig] int GetUnknown();
                [PreserveSig] int SetItem(); [PreserveSig] int DeleteItem(); [PreserveSig] int DeleteAllItems();
                [PreserveSig] int SetUINT32(ref Guid key, int value);
                [PreserveSig] int SetUINT64(); [PreserveSig] int SetDouble();
                [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
                [PreserveSig] int SetString(); [PreserveSig] int SetBlob(); [PreserveSig] int SetUnknown();
                [PreserveSig] int LockStore(); [PreserveSig] int UnlockStore(); [PreserveSig] int GetCount(); [PreserveSig] int GetItemByIndex();
                [PreserveSig] int CopyAllItems();
                [PreserveSig] int GetMajorType(); [PreserveSig] int IsCompressedFormat(); [PreserveSig] int IsEqual();
                [PreserveSig] int GetRepresentation(); [PreserveSig] int FreeRepresentation();
            }

            [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IMFSample
            {
                [PreserveSig] int GetItem(); [PreserveSig] int GetItemType(); [PreserveSig] int CompareItem(); [PreserveSig] int Compare();
                [PreserveSig] int GetUINT32(); [PreserveSig] int GetUINT64(); [PreserveSig] int GetDouble(); [PreserveSig] int GetGUID();
                [PreserveSig] int GetStringLength(); [PreserveSig] int GetString(); [PreserveSig] int GetAllocatedString();
                [PreserveSig] int GetBlobSize(); [PreserveSig] int GetBlob(); [PreserveSig] int GetAllocatedBlob(); [PreserveSig] int GetUnknown();
                [PreserveSig] int SetItem(); [PreserveSig] int DeleteItem(); [PreserveSig] int DeleteAllItems();
                [PreserveSig] int SetUINT32(); [PreserveSig] int SetUINT64(); [PreserveSig] int SetDouble(); [PreserveSig] int SetGUID();
                [PreserveSig] int SetString(); [PreserveSig] int SetBlob(); [PreserveSig] int SetUnknown();
                [PreserveSig] int LockStore(); [PreserveSig] int UnlockStore(); [PreserveSig] int GetCount(); [PreserveSig] int GetItemByIndex();
                [PreserveSig] int CopyAllItems();
                [PreserveSig] int GetSampleFlags(); [PreserveSig] int SetSampleFlags();
                [PreserveSig] int GetSampleTime();
                [PreserveSig] int SetSampleTime(long time);
                [PreserveSig] int GetSampleDuration();
                [PreserveSig] int SetSampleDuration(long duration);
                [PreserveSig] int GetBufferCount(); [PreserveSig] int GetBufferByIndex(); [PreserveSig] int ConvertToContiguousBuffer();
                [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
            }

            [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IMFMediaBuffer
            {
                [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
                [PreserveSig] int Unlock();
                [PreserveSig] int GetCurrentLength(out int length);
                [PreserveSig] int SetCurrentLength(int length);
                [PreserveSig] int GetMaxLength(out int length);
            }

            [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IMFSinkWriter
            {
                [PreserveSig] int AddStream(IMFMediaType targetType, out int streamIndex);
                [PreserveSig] int SetInputMediaType(int streamIndex, IMFMediaType inputType, IMFAttributes encodingParameters);
                [PreserveSig] int BeginWriting();
                [PreserveSig] int WriteSample(int streamIndex, IMFSample sample);
                [PreserveSig] int SendStreamTick(int streamIndex, long timestamp);
                [PreserveSig] int PlaceMarker(int streamIndex, IntPtr context);
                [PreserveSig] int NotifyEndOfSegment(int streamIndex);
                [PreserveSig] int Flush(int streamIndex);
                [PreserveSig] int DoFinalize();
            }
        }
    }

    // Fenêtre affichée à l'arrêt de l'enregistrement : nom, dossier et format (MP3 par défaut) du fichier.
    public class RecordSaveDialog : Form
    {
        readonly TextBox txtName = new TextBox(), txtFolder = new TextBox();
        readonly ComboBox cbFormat = new ComboBox();
        bool discardConfirmed;

        public string Folder { get { return txtFolder.Text.Trim(); } }
        public int Format { get { return cbFormat.SelectedIndex; } }
        public string FilePath { get { return Path.Combine(Folder, CleanName(txtName.Text) + AudioFile.Extensions[Format]); } }

        public RecordSaveDialog(TimeSpan duration, string name, string folder)
        {
            Text = Lang.T("Enregistrer le son", "Save the recording");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);

            Label lblDuration = new Label { AutoSize = true, Location = new Point(12, 14) };
            lblDuration.Text = Lang.F("Durée de l'enregistrement : {0}", "Recording length: {0}",
                duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"mm\:ss"));
            Label lblName = new Label { AutoSize = true, Text = Lang.T("Nom :", "Name:") };
            Label lblFolder = new Label { AutoSize = true, Text = Lang.T("Dossier :", "Folder:") };
            Label lblFormat = new Label { AutoSize = true, Text = Lang.T("Format :", "Format:") };
            Controls.AddRange(new Control[] { lblDuration, lblName, lblFolder, lblFormat });
            int x = 12 + Math.Max(lblName.PreferredWidth, Math.Max(lblFolder.PreferredWidth, lblFormat.PreferredWidth)) + 8;

            lblName.Location = new Point(12, 48);
            txtName.SetBounds(x, 45, 400, 23);
            txtName.Text = name;
            lblFolder.Location = new Point(12, 82);
            txtFolder.SetBounds(x, 79, 300, 23);
            txtFolder.Text = folder;
            Button browse = new Button { Text = Lang.T("Parcourir…", "Browse…") };
            browse.SetBounds(x + 306, 77, 94, 27);
            browse.Click += delegate { Browse(); };
            lblFormat.Location = new Point(12, 116);
            cbFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            cbFormat.Items.AddRange(AudioFile.Formats);
            cbFormat.SelectedIndex = 0;
            cbFormat.SetBounds(x, 113, 90, 23);
            Label lblHint = new Label { AutoSize = true, Location = new Point(x + 100, 116) };
            cbFormat.SelectedIndexChanged += delegate { lblHint.Text = Hint(); };
            lblHint.Text = Hint();

            Button save = new Button { Text = Lang.T("Enregistrer", "Save file") };
            Button discard = new Button { Text = Lang.T("Supprimer", "Delete"), DialogResult = DialogResult.Cancel };
            save.SetBounds(x + 400 - 200, 156, 96, 28);
            discard.SetBounds(x + 400 - 98, 156, 98, 28);
            save.Click += delegate { if (CheckInputs()) DialogResult = DialogResult.OK; };
            AcceptButton = save;
            CancelButton = discard;
            Controls.AddRange(new Control[] { txtName, txtFolder, browse, cbFormat, lblHint, save, discard });
            ClientSize = new Size(x + 400 + 12, 198);
            Theme.Apply(this);
            Shown += delegate { txtName.SelectAll(); txtName.Focus(); };
            // Fermer sans enregistrer (Supprimer, Échap ou croix) : le son serait perdu, on demande confirmation.
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (DialogResult == DialogResult.OK || discardConfirmed) return;
                if (MessageBox.Show(this, Lang.T("Supprimer cet enregistrement sans le sauvegarder ?", "Delete this recording without saving it?"),
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                    discardConfirmed = true;
                else e.Cancel = true;
            };
        }

        string Hint()
        {
            switch (Format)
            {
                case 0: return Lang.T("compressé, lisible partout", "compressed, plays everywhere");
                case 1: return Lang.T("sans perte, fichier volumineux", "lossless, large file");
                default: return Lang.T("sans perte, compressé", "lossless, compressed");
            }
        }

        void Browse()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = Lang.T("Dossier où enregistrer le son", "Folder where the recording is saved");
                if (Directory.Exists(Folder)) d.SelectedPath = Folder;
                if (d.ShowDialog(this) == DialogResult.OK) txtFolder.Text = d.SelectedPath;
            }
        }

        bool CheckInputs()
        {
            if (CleanName(txtName.Text).Length == 0)
            {
                MessageBox.Show(this, Lang.T("Indiquez un nom pour le fichier.", "Enter a name for the file."), Text);
                txtName.Focus();
                return false;
            }
            try { Directory.CreateDirectory(Folder); }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.T("Dossier impossible à créer : ", "Cannot create the folder: ") + ex.Message, Text);
                return false;
            }
            if (File.Exists(FilePath) && MessageBox.Show(this, Lang.F("« {0} » existe déjà. Le remplacer ?", "\"{0}\" already exists. Replace it?", Path.GetFileName(FilePath)),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return false;
            return true;
        }

        // Nom de fichier sans caractères interdits ni extension audio déjà tapée.
        public static string CleanName(string name)
        {
            string n = (name ?? "").Trim();
            foreach (string ext in AudioFile.Extensions)
                if (n.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - ext.Length);
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Trim().TrimEnd('.');
        }
    }
}
