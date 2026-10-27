using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SDRLink
{
    // Périphériques audio de Windows (Core Audio) : retrouve les deux bouts d'un câble virtuel et renomme
    // le bout "enregistrement" (celui que WSJT-X choisit comme entrée), comme le fait le panneau Son de Windows.
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
    }
}
