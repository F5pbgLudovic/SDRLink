using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SDRLink
{
    // Décodeurs numériques natifs : DLL sdrlink_digi.dll (native\build-digi.ps1) — FT8 / FT4 / FT2 (ft8_lib de Kārlis Goba, MIT)
    // et Olivia / Contestia (bibliothèque MFSK de Pawel Jalocha, GPL). Embarquée compressée dans l'exe, écrite sur le disque
    // et chargée seulement au premier usage (comme FreeDvNative).
    public static class DigiNative
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Ft8DecodeFn(short[] pcm, int n, int rate, int mode, byte[] output, int outMax);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Ft8ResetFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Ft8SynthFn(int mode, byte[] msgs, uint seed, double noise, short[] output, int outMax);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr OliviaNewFn(int tones, int bw, int margin, int integ, int contestia, int center, int reverse, int thr10);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void OliviaFreeFn(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int OliviaCenterFn(IntPtr h, int center);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int OliviaProcessFn(IntPtr h, short[] input, int n, byte[] output, int outMax, double[] st);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int OliviaMakeFn(int tones, int bw, int contestia, byte[] text, int snr10, int center, int pre, int post, uint seed, short[] output, int outMax);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int OliviaMakeWavFn(int tones, int bw, int contestia, byte[] text, int snr10, int center, byte[] path);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int OliviaLoopTestFn(int tones, int bw, int contestia, byte[] text, int snr10, int dfreq, byte[] output, int outMax);

        static Ft8DecodeFn ft8Decode;
        static Ft8ResetFn ft8Reset;
        static Ft8SynthFn ft8Synth;
        static OliviaNewFn oliviaNew;
        static OliviaFreeFn oliviaFree;
        static OliviaCenterFn oliviaCenter;
        static OliviaProcessFn oliviaProcess;
        static OliviaMakeFn oliviaMake;
        static OliviaMakeWavFn oliviaMakeWav;
        static OliviaLoopTestFn oliviaLoopTest;

        static volatile bool loaded;
        static string error;
        static readonly object sync = new object();
        static readonly object ft8Lock = new object();   // la table des indicatifs de ft8_lib est globale

        // Vrai quand la DLL est chargée (Load a réussi).
        public static bool Loaded { get { return loaded; } }

        // Charge la DLL (une fois) ; null si tout va bien, sinon la raison.
        public static string Load()
        {
            lock (sync)
            {
                if (loaded) return null;
                if (error != null) return error;
                try
                {
                    byte[] dll;
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("sdrlink_digi.dll.gz"))
                    {
                        if (s == null) throw new IOException(Lang.T("décodeur absent de cette version", "decoder missing from this build"));
                        using (GZipStream z = new GZipStream(s, CompressionMode.Decompress))
                        using (MemoryStream m = new MemoryStream())
                        {
                            z.CopyTo(m);
                            dll = m.ToArray();
                        }
                    }
                    // Dossier propre à cette DLL (taille et empreinte : une DLL refaite de même taille ne réutilise pas l'ancienne).
                    // SDRLINK_DATA (essais) : la DLL va dans ce dossier plutôt que dans LOCALAPPDATA\SDRLink.
                    uint fnv = 2166136261;
                    for (int i = 0; i < dll.Length; i++) fnv = (fnv ^ dll[i]) * 16777619;
                    string tag = "digi-" + dll.Length + "-" + fnv.ToString("x8");
                    string baseDir = Environment.GetEnvironmentVariable("SDRLINK_DATA");
                    string dir = baseDir != null ? Path.Combine(baseDir, "runtime", tag)
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SDRLink", "runtime", tag);
                    string path = Path.Combine(dir, "sdrlink_digi.dll");
                    if (!File.Exists(path) || new FileInfo(path).Length != dll.Length)
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllBytes(path, dll);
                    }
                    IntPtr h = LoadLibrary(path);
                    if (h == IntPtr.Zero) throw new IOException("LoadLibrary : " + Marshal.GetLastWin32Error());
                    ft8Decode = Get<Ft8DecodeFn>(h, "digi_ft8_decode");
                    ft8Reset = Get<Ft8ResetFn>(h, "digi_ft8_reset");
                    ft8Synth = Get<Ft8SynthFn>(h, "digi_ft8_synth");
                    oliviaNew = Get<OliviaNewFn>(h, "digi_olivia_new");
                    oliviaFree = Get<OliviaFreeFn>(h, "digi_olivia_free");
                    oliviaCenter = Get<OliviaCenterFn>(h, "digi_olivia_center");
                    oliviaProcess = Get<OliviaProcessFn>(h, "digi_olivia_process");
                    oliviaMake = Get<OliviaMakeFn>(h, "digi_olivia_make");
                    oliviaMakeWav = Get<OliviaMakeWavFn>(h, "digi_olivia_make_wav");
                    oliviaLoopTest = Get<OliviaLoopTestFn>(h, "digi_olivia_loop_test");
                    loaded = true;
                    Log.Write("Modes numériques : décodeurs natifs chargés (" + path + ")");
                    return null;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Log.Write("Modes numériques : décodeurs natifs non chargés : " + ex.Message);
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

        static byte[] Z(string s) { return Encoding.UTF8.GetBytes((s ?? "") + "\0"); }

        static string Str(byte[] b)
        {
            int n = Array.IndexOf(b, (byte)0);
            return Encoding.UTF8.GetString(b, 0, n < 0 ? b.Length : n);
        }

        // ------------------------------------------------------------ FT8 / FT4 / FT2

        /** Décode une tranche (16 bits, rate Hz ; mode 0 FT8, 1 FT4, 2 FT2) : une ligne « snr|dt|freq|texte » par message ("" si rien). */
        public static string Ft8Decode(short[] pcm, int n, int rate, int mode)
        {
            if (!loaded || pcm == null) return "";
            if (n > pcm.Length) n = pcm.Length;
            if (n <= 0) return "";
            byte[] o = new byte[16384];
            lock (ft8Lock) ft8Decode(pcm, n, rate, mode, o, o.Length);
            return Str(o);
        }

        /** Vide la table des indicatifs « hachés ». */
        public static void Ft8Reset()
        {
            if (!loaded) return;
            lock (ft8Lock) ft8Reset();
        }

        /** Essais : tranche synthétique à 12000 Hz ; msgs = lignes « freq|dt|snr|texte » (snr en dB dans 2500 Hz), noise = écart-type du bruit (pleine échelle 1).
         *  null en cas d'erreur. */
        public static short[] Ft8Synth(int mode, string msgs, uint seed, double noise)
        {
            if (!loaded) return null;
            short[] o = new short[12000 * 15];
            int n = ft8Synth(mode, Z(msgs), seed, noise, o, o.Length);
            if (n <= 0) return null;
            short[] r = new short[n];
            Array.Copy(o, r, n);
            return r;
        }

        // ------------------------------------------------------------ Olivia / Contestia

        /** Nouveau décodeur (centre en Hz audio, thr10 = seuil de synchro en dixièmes) ; IntPtr.Zero si les réglages sont refusés. */
        public static IntPtr OliviaNew(int tones, int bw, int margin, int integ, bool contestia, int center, bool reverse, int thr10)
        {
            if (!loaded) return IntPtr.Zero;
            return oliviaNew(tones, bw, margin, integ, contestia ? 1 : 0, center, reverse ? 1 : 0, thr10);
        }

        public static void OliviaFree(IntPtr h)
        {
            if (loaded && h != IntPtr.Zero) oliviaFree(h);
        }

        /** Nouvelle fréquence centrale (le décodeur repart de zéro) ; 0 si bon. */
        public static int OliviaCenter(IntPtr h, int center)
        {
            if (!loaded || h == IntPtr.Zero) return -1;
            return oliviaCenter(h, center);
        }

        /** Son 8000 Hz -> nombre de caractères écrits dans output ; st (2 cases) = { rapport signal/bruit de la synchro, décalage en Hz }. */
        public static int OliviaProcess(IntPtr h, short[] input, int n, byte[] output, double[] st)
        {
            if (!loaded || h == IntPtr.Zero || input == null || n <= 0) return 0;
            if (n > input.Length) n = input.Length;
            return oliviaProcess(h, input, n, output, output == null ? 0 : output.Length, st);
        }

        /** Essais : son 8000 Hz (pre s de bruit, le texte centré sur center Hz, post s de bruit), snr10 en dixièmes de dB dans 2,5 kHz. null si erreur. */
        public static short[] OliviaMake(int tones, int bw, bool contestia, string text, int snr10, int center, int pre, int post, uint seed)
        {
            if (!loaded) return null;
            short[] o = new short[8000 * 900];
            int n = oliviaMake(tones, bw, contestia ? 1 : 0, Z(text), snr10, center, pre, post, seed, o, o.Length);
            if (n <= 0) return null;
            short[] r = new short[n];
            Array.Copy(o, r, n);
            return r;
        }

        /** Essais : fichier WAV 8000 Hz (8 s de bruit avant, 12 s après) ; renvoie le nombre d'échantillons (< 0 si erreur). Chemin en caractères ANSI. */
        public static int OliviaMakeWav(int tones, int bw, bool contestia, string text, int snr10, int center, string path)
        {
            if (!loaded) return -1;
            return oliviaMakeWav(tones, bw, contestia ? 1 : 0, Z(text), snr10, center, Encoding.Default.GetBytes(path + "\0"));
        }

        /** Essai de bouclage natif (émission à 1500 Hz, réception à 1500 + dfreq Hz) : « texte décodé|rapport max|décalage ». */
        public static string OliviaLoopTest(int tones, int bw, bool contestia, string text, int snr10, int dfreq)
        {
            if (!loaded) return "";
            byte[] o = new byte[65536];
            oliviaLoopTest(tones, bw, contestia ? 1 : 0, Z(text), snr10, dfreq, o, o.Length);
            return Str(o);
        }
    }
}
