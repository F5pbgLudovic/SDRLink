using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SDRLink
{
    // Sortie audio Windows (waveOut) pour le mode DIGI : le son du WebSDR est recopié vers une sortie choisie,
    // typiquement un câble audio virtuel (VB-CABLE) que WSJT-X utilise comme entrée.
    public class AudioSink : IDisposable
    {
        const int WAVE_MAPPER = -1, CALLBACK_NULL = 0, WHDR_DONE = 1, MMSYSERR_ALLOCATED = 4;
        const int MaxQueued = 12;   // ~1 s de son en attente au plus

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX
        {
            public short wFormatTag, nChannels;
            public int nSamplesPerSec, nAvgBytesPerSec;
            public short nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct WAVEOUTCAPS
        {
            public short wMid, wPid;
            public int vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public int dwFormats;
            public short wChannels, wReserved1;
            public int dwSupport;
        }

        [DllImport("winmm.dll")] static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Auto)] static extern int waveOutGetDevCaps(IntPtr id, ref WAVEOUTCAPS caps, int size);
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, int device, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

        // WAVEHDR : lpData, dwBufferLength, dwBytesRecorded, dwUser, dwFlags, dwLoops, lpNext, reserved.
        static readonly int HdrSize = IntPtr.Size * 4 + 4 * 4;
        static readonly int FlagsOffset = IntPtr.Size * 2 + 8;

        IntPtr handle = IntPtr.Zero;
        int rate;
        string device;
        readonly List<IntPtr[]> queued = new List<IntPtr[]>();   // { en-tête, données }
        readonly object sync = new object();

        DateTime retryAt;            // après un échec d'ouverture, nouvel essai seulement à cette heure
        string error;

        public string DeviceName { get { return device; } }
        // Dernière erreur d'ouverture (null si la sortie fonctionne).
        public string Error { get { return error; } }

        // Noms des sorties audio (tronqués à 31 caractères par Windows).
        public static List<string> OutputDevices()
        {
            List<string> r = new List<string>();
            int n = waveOutGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                WAVEOUTCAPS c = new WAVEOUTCAPS();
                if (waveOutGetDevCaps(new IntPtr(i), ref c, Marshal.SizeOf(c)) == 0) r.Add(c.szPname);
            }
            return r;
        }

        // Sortie choisie automatiquement : le câble dont l'autre bout s'appelle "SDRLINK", sinon un câble VB-Audio
        // portant encore son nom d'origine (donc a priori libre). Un câble renommé ou toute autre sortie
        // (haut-parleurs, carte son d'émission d'un poste...) n'est jamais pris sans un choix explicite.
        public static string DefaultDevice()
        {
            List<string> devs = OutputDevices();
            try
            {
                List<AudioEndpoints.Endpoint> eps = AudioEndpoints.All();
                AudioEndpoints.Endpoint cap = eps.Find(delegate(AudioEndpoints.Endpoint e) { return e.Capture && e.Name == "SDRLINK"; });
                if (cap != null && !string.IsNullOrEmpty(cap.Adapter))
                    foreach (AudioEndpoints.Endpoint e in eps)
                        if (!e.Capture && e.Adapter == cap.Adapter && e.FullName != null)
                            foreach (string d in devs)
                                if (d.Length > 0 && e.FullName.StartsWith(d, StringComparison.Ordinal)) return d;
            }
            catch (Exception ex) { Log.Write("Câble SDRLINK : lecture des périphériques audio impossible : " + ex.Message); }
            foreach (string key in new[] { "SDRLINK", "CABLE Input", "CABLE-A Input", "CABLE-B Input", "CABLE-C Input", "CABLE-D Input" })
                foreach (string d in devs)
                    if (d.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        // Sortie qui ressemble à un câble virtuel (sinon, le son du WebSDR serait joué ou émis pour de vrai).
        public static bool LooksVirtual(string name)
        {
            string n = (name ?? "").ToUpperInvariant();
            return n.Contains("CABLE") || n.Contains("SDRLINK") || n.Contains("VB-AUDIO") || n.Contains("VIRTUAL");
        }

        public AudioSink(string deviceName) { device = deviceName; }

        // Échantillons 16 bits mono ; la sortie est (ré)ouverte si la fréquence d'échantillonnage change.
        public void Write(int sampleRate, byte[] pcm)
        {
            lock (sync)
            {
                if ((handle == IntPtr.Zero && DateTime.Now >= retryAt) || (handle != IntPtr.Zero && sampleRate != rate)) Open(sampleRate);
                if (handle == IntPtr.Zero) return;
                Reclaim();
                if (queued.Count >= MaxQueued) return;   // la sortie prend du retard : on saute ce bloc plutôt que d'accumuler

                IntPtr data = Marshal.AllocHGlobal(pcm.Length);
                Marshal.Copy(pcm, 0, data, pcm.Length);
                IntPtr hdr = Marshal.AllocHGlobal(HdrSize);
                for (int i = 0; i < HdrSize; i++) Marshal.WriteByte(hdr, i, 0);
                Marshal.WriteIntPtr(hdr, 0, data);
                Marshal.WriteInt32(hdr, IntPtr.Size, pcm.Length);
                if (waveOutPrepareHeader(handle, hdr, HdrSize) != 0 || waveOutWrite(handle, hdr, HdrSize) != 0)
                {
                    Marshal.FreeHGlobal(hdr);
                    Marshal.FreeHGlobal(data);
                    return;
                }
                queued.Add(new[] { hdr, data });
            }
        }

        void Open(int sampleRate)
        {
            CloseDevice();
            rate = sampleRate;
            int id = WAVE_MAPPER;
            List<string> devs = OutputDevices();
            for (int i = 0; i < devs.Count; i++) if (devs[i] == device) id = i;
            if (id == WAVE_MAPPER) { Fail(Lang.T("sortie introuvable", "output not found")); return; }
            WAVEFORMATEX f = new WAVEFORMATEX
            {
                wFormatTag = 1, nChannels = 1, nSamplesPerSec = sampleRate, wBitsPerSample = 16,
                nBlockAlign = 2, nAvgBytesPerSec = sampleRate * 2
            };
            int err = waveOutOpen(out handle, id, ref f, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
            if (err != 0)
            {
                handle = IntPtr.Zero;
                Fail(err == MMSYSERR_ALLOCATED
                    ? Lang.T("sortie déjà utilisée en mode exclusif par un autre programme", "output already used in exclusive mode by another program")
                    : Lang.T("ouverture impossible, erreur ", "cannot open, error ") + err);
                return;
            }
            error = null;
            Log.Write("DIGI : son du WebSDR envoyé vers « " + device + " » (" + sampleRate + " Hz, mono)");
        }

        // Échec : un seul message dans le journal, nouvel essai dans 5 s.
        void Fail(string why)
        {
            retryAt = DateTime.Now.AddSeconds(5);
            if (why == error) return;
            error = why;
            Log.Write("DIGI : « " + device + " » : " + why);
        }

        // Libère les blocs déjà joués.
        void Reclaim()
        {
            for (int i = queued.Count - 1; i >= 0; i--)
            {
                IntPtr hdr = queued[i][0];
                if ((Marshal.ReadInt32(hdr, FlagsOffset) & WHDR_DONE) == 0) continue;
                waveOutUnprepareHeader(handle, hdr, HdrSize);
                Marshal.FreeHGlobal(hdr);
                Marshal.FreeHGlobal(queued[i][1]);
                queued.RemoveAt(i);
            }
        }

        void CloseDevice()
        {
            if (handle == IntPtr.Zero) return;
            waveOutReset(handle);   // marque tous les blocs comme joués
            Reclaim();
            waveOutClose(handle);
            handle = IntPtr.Zero;
        }

        public void Dispose()
        {
            lock (sync) CloseDevice();
        }
    }
}
