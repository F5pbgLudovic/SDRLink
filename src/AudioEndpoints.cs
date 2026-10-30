using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SDRLink
{
    // Périphériques audio de Windows (Core Audio) : retrouve les deux bouts d'un câble virtuel et renomme
    // le bout "enregistrement" (celui que WSJT-X choisit comme entrée), comme le fait le panneau Son de Windows ;
    // coupe aussi le son d'un autre logiciel (ExpertSDR) dans le mélangeur de volume.
    public static class AudioEndpoints
    {
        public class Endpoint
        {
            public string Id;
            public bool Capture;        // true : enregistrement (entrée), false : lecture (sortie)
            public string Name;         // nom modifiable, ex. "CABLE Output"
            public string Adapter;      // pilote, ex. "VB-Audio Virtual Cable"
            public string FullName;     // "CABLE Output (VB-Audio Virtual Cable)"
        }

        const int eRender = 0, eCapture = 1, DEVICE_STATE_ACTIVE = 1;
        const int STGM_READ = 0, STGM_READWRITE = 2;
        const int VT_LPWSTR = 31;
        public const int E_ACCESSDENIED = unchecked((int)0x80070005);

        static PropertyKey DeviceDesc = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
        static PropertyKey FriendlyName = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        static PropertyKey InterfaceName = new PropertyKey(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);

        public static List<Endpoint> All()
        {
            List<Endpoint> r = new List<Endpoint>();
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                foreach (int flow in new[] { eRender, eCapture })
                {
                    IMMDeviceCollection col;
                    if (en.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out col) != 0) continue;
                    int n;
                    col.GetCount(out n);
                    for (int i = 0; i < n; i++)
                    {
                        IMMDevice d;
                        if (col.Item(i, out d) != 0) continue;
                        Endpoint e = new Endpoint();
                        e.Capture = flow == eCapture;
                        d.GetId(out e.Id);
                        IPropertyStore ps;
                        if (d.OpenPropertyStore(STGM_READ, out ps) == 0)
                        {
                            e.Name = GetString(ps, DeviceDesc);
                            e.FullName = GetString(ps, FriendlyName);
                            e.Adapter = GetString(ps, InterfaceName);
                            Marshal.ReleaseComObject(ps);
                        }
                        Marshal.ReleaseComObject(d);
                        r.Add(e);
                    }
                    Marshal.ReleaseComObject(col);
                }
            }
            finally { Marshal.ReleaseComObject(en); }
            return r;
        }

        // Bout "enregistrement" du câble dont la sortie waveOut porte ce nom (Windows le tronque à 31 caractères).
        public static Endpoint CaptureOf(string waveOutName)
        {
            if (string.IsNullOrEmpty(waveOutName)) return null;
            List<Endpoint> all = All();
            Endpoint render = all.Find(delegate(Endpoint e)
            {
                return !e.Capture && e.FullName != null && e.FullName.StartsWith(waveOutName, StringComparison.Ordinal);
            });
            if (render == null || string.IsNullOrEmpty(render.Adapter)) return null;
            List<Endpoint> caps = all.FindAll(delegate(Endpoint e) { return e.Capture && e.Adapter == render.Adapter; });
            return caps.Count == 1 ? caps[0] : null;   // un seul bout possible, sinon on ne devine pas
        }

        // Renomme un périphérique. Retourne 0, ou E_ACCESSDENIED s'il faut les droits d'administrateur.
        public static int SetName(string id, string name)
        {
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice d = null;
            IPropertyStore ps = null;
            try
            {
                int hr = en.GetDevice(id, out d);
                if (hr != 0) return hr;
                hr = d.OpenPropertyStore(STGM_READWRITE, out ps);
                if (hr != 0) return hr;
                PropVariant v = new PropVariant();
                v.vt = VT_LPWSTR;
                v.p = Marshal.StringToCoTaskMemUni(name);
                try
                {
                    hr = ps.SetValue(ref DeviceDesc, ref v);
                    if (hr == 0) hr = ps.Commit();
                }
                finally { Marshal.FreeCoTaskMem(v.p); }
                return hr;
            }
            finally
            {
                if (ps != null) Marshal.ReleaseComObject(ps);
                if (d != null) Marshal.ReleaseComObject(d);
                Marshal.ReleaseComObject(en);
            }
        }

        // Interface Core Audio (IAudioClient…) de la sortie par défaut de Windows (haut-parleurs), avec son nom.
        // Retourne un pointeur COM à libérer par l'appelant.
        public static IntPtr ActivateDefaultOutput(Guid iid, out string name)
        {
            return Activate(null, iid, out name);
        }

        // Même chose pour un périphérique donné par son identifiant (entrée d'une carte son, par exemple).
        public static IntPtr ActivateDevice(string id, Guid iid, out string name)
        {
            return Activate(id, iid, out name);
        }

        static IntPtr Activate(string id, Guid iid, out string name)
        {
            name = null;
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice d = null;
            try
            {
                int found = id == null ? en.GetDefaultAudioEndpoint(eRender, 0, out d) : en.GetDevice(id, out d);
                if (found != 0 && id != null) throw new System.IO.IOException(Lang.T("carte son introuvable", "sound card not found"));
                Marshal.ThrowExceptionForHR(found);
                int state;
                if (id != null && (d.GetState(out state) != 0 || state != DEVICE_STATE_ACTIVE))
                    throw new System.IO.IOException(Lang.T("carte son débranchée ou désactivée", "sound card unplugged or disabled"));
                IPropertyStore ps;
                if (d.OpenPropertyStore(STGM_READ, out ps) == 0)
                {
                    name = GetString(ps, FriendlyName);
                    Marshal.ReleaseComObject(ps);
                }
                IntPtr p;
                Marshal.ThrowExceptionForHR(d.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out p));
                return p;
            }
            finally
            {
                if (d != null) Marshal.ReleaseComObject(d);
                Marshal.ReleaseComObject(en);
            }
        }

        // Coupure du son d'un logiciel dans le mélangeur de volume de Windows (toutes les sorties) :
        // mute = null lit l'état, sinon l'applique. Retourne true si toutes ses sessions sont coupées,
        // false sinon, null s'il n'a aucune session audio.
        public static bool? ProcessMute(Predicate<int> isProcess, bool? mute)
        {
            bool allMuted = true;
            bool any = ForEachSession(isProcess, delegate(ISimpleAudioVolume vol)
            {
                if (mute.HasValue) vol.SetMute(mute.Value ? 1 : 0, IntPtr.Zero);
                int m;
                vol.GetMute(out m);
                if (m == 0) allMuted = false;
            });
            return any ? allMuted : (bool?)null;
        }

        // Volume d'un logiciel dans le mélangeur (0 à 100 %) : percent = null le lit (le plus fort de ses sessions),
        // sinon l'applique à toutes ses sessions. Retourne -1 s'il n'a aucune session audio.
        public static int ProcessVolume(Predicate<int> isProcess, int? percent)
        {
            float max = -1;
            ForEachSession(isProcess, delegate(ISimpleAudioVolume vol)
            {
                if (percent.HasValue) vol.SetMasterVolume(Math.Max(0, Math.Min(100, percent.Value)) / 100f, IntPtr.Zero);
                float level;
                if (vol.GetMasterVolume(out level) == 0 && level > max) max = level;
            });
            return max < 0 ? -1 : (int)Math.Round(max * 100);
        }

        delegate void SessionAction(ISimpleAudioVolume vol);

        // Applique l'action à chaque session audio (toutes les sorties) des processus choisis ; false s'il n'y en a aucune.
        static bool ForEachSession(Predicate<int> isProcess, SessionAction action)
        {
            bool any = false;
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                IMMDeviceCollection col;
                if (en.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out col) != 0) return false;
                int n;
                col.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IMMDevice d;
                    if (col.Item(i, out d) != 0) continue;
                    Guid iid = typeof(IAudioSessionManager2).GUID;
                    IntPtr p;
                    if (d.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out p) == 0)
                    {
                        IAudioSessionManager2 mgr = (IAudioSessionManager2)Marshal.GetObjectForIUnknown(p);
                        Marshal.Release(p);
                        IAudioSessionEnumerator sessions;
                        if (mgr.GetSessionEnumerator(out sessions) == 0)
                        {
                            int count;
                            sessions.GetCount(out count);
                            for (int k = 0; k < count; k++)
                            {
                                object o;
                                if (sessions.GetSession(k, out o) != 0) continue;
                                IAudioSessionControl2 ctl = o as IAudioSessionControl2;
                                int pid;
                                if (ctl != null && ctl.GetProcessId(out pid) == 0 && isProcess(pid))
                                {
                                    action((ISimpleAudioVolume)o);
                                    any = true;
                                }
                                Marshal.ReleaseComObject(o);
                            }
                            Marshal.ReleaseComObject(sessions);
                        }
                        Marshal.ReleaseComObject(mgr);
                    }
                    Marshal.ReleaseComObject(d);
                }
                Marshal.ReleaseComObject(col);
            }
            finally { Marshal.ReleaseComObject(en); }
            return any;
        }

        const int CLSCTX_ALL = 23;

        static string GetString(IPropertyStore ps, PropertyKey key)
        {
            PropVariant v;
            if (ps.GetValue(ref key, out v) != 0) return null;
            try { return v.vt == VT_LPWSTR ? Marshal.PtrToStringUni(v.p) : null; }
            finally { PropVariantClear(ref v); }
        }

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant v);

        [StructLayout(LayoutKind.Sequential)]
        struct PropertyKey
        {
            public Guid fmtid;
            public int pid;
            public PropertyKey(Guid g, int id) { fmtid = g; pid = id; }
        }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        struct PropVariant
        {
            [FieldOffset(0)] public short vt;
            [FieldOffset(8)] public IntPtr p;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr instance);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
        }

        // IAudioSessionControl puis IAudioSessionControl2 : seules les méthodes de lecture du processus servent.
        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
            [PreserveSig] int GetGroupingParam(out Guid group);
            [PreserveSig] int SetGroupingParam(IntPtr group, IntPtr context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out int pid);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, IntPtr context);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute(int mute, IntPtr context);
            [PreserveSig] int GetMute(out int mute);
        }
    }
}
