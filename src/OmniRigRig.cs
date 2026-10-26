using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SDRLink
{
    // Pilotage via OmniRig (serveur COM d'Alex VE3NEA), en liaison tardive pour ne pas dépendre de sa bibliothèque de types.
    // L'objet OmniRig est créé une seule fois par connexion : le relâcher puis le recréer en boucle peut fermer et
    // relancer OmniRig, qui rouvre alors sa fenêtre de réglages. Les erreurs de lecture ne provoquent donc pas
    // de reconnexion : elles sont journalisées et le poste est simplement vu « hors ligne ».
    public class OmniRigRig : IRig
    {
        const int PM_FREQ = 0x2, PM_FREQA = 0x4, PM_RX = 0x200000, PM_TX = 0x400000;
        const int PM_CW_U = 0x800000, PM_CW_L = 0x1000000, PM_SSB_U = 0x2000000, PM_SSB_L = 0x4000000,
            PM_DIG_U = 0x8000000, PM_DIG_L = 0x10000000, PM_AM = 0x20000000, PM_FM = 0x40000000;
        const int ST_ONLINE = 4;

        readonly int rigNumber;
        object omni;
        object rig;
        string lastStatus;
        string lastError;

        public OmniRigRig(int rigNumber) { this.rigNumber = rigNumber; }

        public void Open()
        {
            Type t = Type.GetTypeFromProgID("OmniRig.OmniRigX");
            if (t == null) throw new Exception(Lang.T("OmniRig n'est pas installé (OmniRig.OmniRigX introuvable).", "OmniRig is not installed (OmniRig.OmniRigX not found)."));
            omni = Activator.CreateInstance(t);
            rig = Get(omni, rigNumber == 2 ? "Rig2" : "Rig1");
            try { Log.Write("OmniRig " + Get(omni, "SoftwareVersion") + ", Rig" + rigNumber + " : " + Get(rig, "RigType")); }
            catch { }
        }

        public void Close()
        {
            if (rig != null) { try { Marshal.ReleaseComObject(rig); } catch { } rig = null; }
            if (omni != null) { try { Marshal.ReleaseComObject(omni); } catch { } omni = null; }
        }

        public void Dispose() { Close(); }

        public bool Read(out long freqHz, out string mode)
        {
            freqHz = 0;
            mode = null;
            try
            {
                int status = Convert.ToInt32(Get(rig, "Status"));
                if (status != ST_ONLINE)
                {
                    LogStatus(Convert.ToString(Get(rig, "StatusStr")));
                    return false;
                }
                LogStatus("en ligne");
                freqHz = Convert.ToInt64(Get(rig, "Freq"));
                if (freqHz <= 0) freqHz = Convert.ToInt64(Get(rig, "FreqA"));
                mode = FromOmni(Convert.ToInt32(Get(rig, "Mode")));
                return freqHz > 0;
            }
            catch (Exception ex)
            {
                LogError(ex);
                return false;
            }
        }

        public int ReadTx()
        {
            try
            {
                int tx = Convert.ToInt32(Get(rig, "Tx"));
                return tx == PM_TX ? 1 : tx == PM_RX ? 0 : -1;
            }
            catch (Exception ex) { LogError(ex); return -1; }
        }

        // OmniRig n'expose pas le gain BF.
        public int ReadAfGain() { return -1; }
        public void SetAfGain(int percent) { }

        public void SetPtt(bool tx)
        {
            try { Set(rig, "Tx", tx ? PM_TX : PM_RX); }
            catch (Exception ex) { LogError(ex); }
        }

        public void SetFreq(long freqHz)
        {
            try
            {
                int f = (int)freqHz;
                if (Writeable(PM_FREQ)) Set(rig, "Freq", f);
                else if (Writeable(PM_FREQA)) Set(rig, "FreqA", f);
                else Call(rig, "SetSimplexMode", f);
            }
            catch (Exception ex) { LogError(ex); }
        }

        public void SetMode(string mode)
        {
            int m = ToOmni(mode);
            if (m == 0) return;
            try { Set(rig, "Mode", m); }
            catch (Exception ex) { LogError(ex); }
        }

        void LogStatus(string s)
        {
            if (s == lastStatus) return;
            lastStatus = s;
            Log.Write("OmniRig Rig" + rigNumber + " : " + s);
        }

        void LogError(Exception ex)
        {
            string msg = ex.GetBaseException().Message;
            if (msg == lastError) return;
            lastError = msg;
            Log.Write("OmniRig erreur : " + msg);
        }

        bool Writeable(int param)
        {
            try { return Convert.ToBoolean(Call(rig, "IsParamWriteable", param)); }
            catch { return false; }
        }

        static string FromOmni(int m)
        {
            switch (m)
            {
                case PM_SSB_L: return Modes.LSB;
                case PM_SSB_U: return Modes.USB;
                case PM_CW_U: return Modes.CW;
                case PM_CW_L: return Modes.CWR;
                case PM_DIG_L: return Modes.DATAL;
                case PM_DIG_U: return Modes.DATAU;
                case PM_AM: return Modes.AM;
                case PM_FM: return Modes.FM;
            }
            return null;
        }

        static int ToOmni(string m)
        {
            switch (m)
            {
                case Modes.LSB: return PM_SSB_L;
                case Modes.USB: return PM_SSB_U;
                case Modes.CW: return PM_CW_U;
                case Modes.CWR: return PM_CW_L;
                case Modes.DATAL: case Modes.RTTY: return PM_DIG_L;
                case Modes.DATAU: case Modes.RTTYR: return PM_DIG_U;
                case Modes.AM: return PM_AM;
                case Modes.FM: return PM_FM;
            }
            return 0;
        }

        static object Get(object o, string name)
        {
            return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);
        }

        static void Set(object o, string name, object value)
        {
            o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new object[] { value });
        }

        static object Call(object o, string name, params object[] args)
        {
            return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args);
        }
    }
}
