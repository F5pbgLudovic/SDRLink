using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace SDRLink
{
    // Bouton Tune (1.49) : tonalité sinusoïdale (1500 Hz) jouée sur la carte son d'émission du poste (sortie Windows reliée
    // à l'entrée modulation / USB du poste), en WASAPI partagé au format de la sortie, sur son propre fil.
    // Montée et descente de 10 ms : pas de claquement au début ni à la fin.
    public class TuneTone
    {
        const int AUDCLNT_SHAREMODE_SHARED = 0;
        static readonly Guid FloatFormat = new Guid("00000003-0000-0010-8000-00aa00389b71");
        readonly string deviceId;
        readonly double freq;
        public volatile float Level;          // 0 à 1 (proportion de la pleine échelle)
        volatile bool stop;
        Thread thread;
        public volatile string Error;         // raison d'un échec (carte introuvable…), null si tout va bien
        public volatile string DeviceName;
        public long FramesPlayed;             // pour les essais

        public TuneTone(string deviceId, double freq, float level)
        {
            this.deviceId = deviceId;
            this.freq = freq;
            Level = level;
        }

        public void Start()
        {
            stop = false;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "Tune";
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        // Arrêt avec la descente de 10 ms, puis attente de la fin du fil.
        public void Stop()
        {
            stop = true;
            Thread t = thread;
            if (t != null) t.Join(1000);
            thread = null;
        }

        public bool Running { get { Thread t = thread; return t != null && t.IsAlive; } }

        void Run()
        {
            AudioRecorder.IAudioClient client = null;
            RigMonitor.IAudioRenderClient render = null;
            try
            {
                string name;
                IntPtr p = string.IsNullOrEmpty(deviceId)
                    ? AudioEndpoints.ActivateDefaultOutput(typeof(AudioRecorder.IAudioClient).GUID, out name)
                    : AudioEndpoints.ActivateDevice(deviceId, typeof(AudioRecorder.IAudioClient).GUID, out name);
                try { client = (AudioRecorder.IAudioClient)Marshal.GetObjectForIUnknown(p); }
                finally { Marshal.Release(p); }
                DeviceName = name;
                IntPtr wf;
                Marshal.ThrowExceptionForHR(client.GetMixFormat(out wf));
                int channels, rate, bits;
                bool isFloat;
                try
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
                    Marshal.ThrowExceptionForHR(client.Initialize(AUDCLNT_SHAREMODE_SHARED, 0, 1000000, 0, wf, IntPtr.Zero));   // 100 ms
                }
                finally { Marshal.FreeCoTaskMem(wf); }
                Guid iid = typeof(RigMonitor.IAudioRenderClient).GUID;
                IntPtr r;
                Marshal.ThrowExceptionForHR(client.GetService(ref iid, out r));
                try { render = (RigMonitor.IAudioRenderClient)Marshal.GetObjectForIUnknown(r); }
                finally { Marshal.Release(r); }
                int bufferFrames;
                Marshal.ThrowExceptionForHR(client.GetBufferSize(out bufferFrames));
                Log.Write("Tune : tonalité " + freq + " Hz sur « " + name + " » (" + rate + " Hz, " + bits + " bits" + (isFloat ? " flottants" : "") + ")");

                double phase = 0, dphi = 2 * Math.PI * freq / rate;
                double ramp = 0, rampStep = 1.0 / (rate * 0.01);
                int bpp = bits / 8;
                byte[] bytes = new byte[bufferFrames * bpp * channels];
                bool started = false, ending = false;
                while (true)
                {
                    if (stop) ending = true;
                    int padding;
                    Marshal.ThrowExceptionForHR(client.GetCurrentPadding(out padding));
                    int todo = bufferFrames - padding;
                    if (todo > 0)
                    {
                        IntPtr buf;
                        Marshal.ThrowExceptionForHR(render.GetBuffer(todo, out buf));
                        int o = 0;
                        float lvl = Math.Max(0f, Math.Min(1f, Level));
                        for (int i = 0; i < todo; i++)
                        {
                            if (ending) ramp = Math.Max(0, ramp - rampStep); else ramp = Math.Min(1, ramp + rampStep);
                            double v = Math.Sin(phase) * lvl * ramp;
                            phase += dphi;
                            if (phase > 2 * Math.PI) phase -= 2 * Math.PI;
                            for (int c = 0; c < channels; c++)
                            {
                                if (isFloat) Buffer.BlockCopy(BitConverter.GetBytes((float)v), 0, bytes, o, 4);
                                else
                                {
                                    int s = bpp == 2 ? (int)Math.Round(v * 32767) : bpp == 3 ? (int)Math.Round(v * 8388607) : (int)Math.Round(v * 2147483647.0);
                                    for (int k = 0; k < bpp; k++) bytes[o + k] = (byte)(s >> (8 * k));
                                }
                                o += bpp;
                            }
                        }
                        Marshal.Copy(bytes, 0, buf, o);
                        Marshal.ThrowExceptionForHR(render.ReleaseBuffer(todo, 0));
                        FramesPlayed += todo;
                        if (!started) { Marshal.ThrowExceptionForHR(client.Start()); started = true; }
                    }
                    if (ending && ramp <= 0)
                    {
                        // Laisse jouer ce qui est déjà dans le tampon (fin de la descente), puis arrête.
                        for (int i = 0; i < 30; i++) { int pd; if (client.GetCurrentPadding(out pd) != 0 || pd == 0) break; Thread.Sleep(5); }
                        break;
                    }
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Log.Write("Tune : sortie audio impossible : " + ex.Message);
            }
            finally
            {
                if (client != null) { try { client.Stop(); } catch { } }
                if (render != null) Marshal.ReleaseComObject(render);
                if (client != null) Marshal.ReleaseComObject(client);
            }
        }
    }
}
