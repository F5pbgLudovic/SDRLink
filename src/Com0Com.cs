using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SDRLink
{
    // com0com (paires de ports COM virtuels reliés entre eux, licence GPL, https://sourceforge.net/projects/com0com/) :
    // détection, liste des paires, création d'une paire, et installation silencieuse à partir de l'installateur signé
    // embarqué dans SDRLink (vendor\com0com). Windows demande seulement l'autorisation d'administrateur. (1.49)
    public static class Com0Com
    {
        public class Pair
        {
            public int N;
            public string A, B;        // SDRLink ouvre A ; l'autre logiciel ouvre B
            public bool Busy;          // A déjà ouvert par un autre programme
            public override string ToString()
            {
                return Lang.F("SDRLink : {0}  ↔  votre logiciel : {1}", "SDRLink: {0}  ↔  your program: {1}", A, B) +
                       (Busy ? Lang.T("   (occupée par un autre programme)", "   (in use by another program)") : "");
            }
        }

        const string Hash64 = "26486b28604b49a9008c54feb11b9ece0008a8287ee5caf0bcf2a62f4317128f",
                     Hash86 = "de917425e022839f6374b509e3d34f3f9e9773915a44996194442337eadf4a5a";

        public static string SetupC()
        {
            foreach (string pf in new string[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramW6432") })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                string p = Path.Combine(pf, "com0com\\setupc.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static bool Installed { get { return SetupC() != null || Pairs().Count > 0; } }

        // Paires déclarées dans le registre (lecture seule, sans droits d'administrateur).
        public static List<Pair> Pairs()
        {
            List<Pair> l = new List<Pair>();
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\COM0COM\PORT"))
                {
                    if (k == null) return l;
                    foreach (string n in k.GetSubKeyNames())
                    {
                        Match m = Regex.Match(n, @"^CNCA(\d+)$", RegexOptions.IgnoreCase);
                        if (!m.Success) continue;
                        string a = PortName(k, n), b = PortName(k, "CNCB" + m.Groups[1].Value);
                        if (a != null && b != null) { Pair p = new Pair(); p.N = int.Parse(m.Groups[1].Value); p.A = a; p.B = b; l.Add(p); }
                    }
                }
            }
            catch { }
            l.Sort(delegate(Pair x, Pair y) { return x.N.CompareTo(y.N); });
            return l;
        }

        static string PortName(RegistryKey k, string sub)
        {
            try { using (RegistryKey d = k.OpenSubKey(sub + @"\Device Parameters")) { return d == null ? null : d.GetValue("PortName") as string; } }
            catch { return null; }
        }

        public static bool IsComName(string s) { return s != null && Regex.IsMatch(s, @"^COM\d+$", RegexOptions.IgnoreCase); }
        public static string[] PortsNow() { try { return SerialPort.GetPortNames(); } catch { return new string[0]; } }

        // Paires utilisables : deux vrais noms COMn présents dans Windows, sans rapport avec le port du poste.
        // keep = port déjà choisi par SDRLink (tenu ouvert par lui : jamais marqué occupé).
        public static List<Pair> Usable(string rigPort, string keep)
        {
            List<Pair> l = new List<Pair>();
            HashSet<string> present = new HashSet<string>(PortsNow(), StringComparer.OrdinalIgnoreCase);
            foreach (Pair p in Pairs())
            {
                if (!IsComName(p.A) || !IsComName(p.B) || !present.Contains(p.A) || !present.Contains(p.B)) continue;
                if (rigPort != null && (rigPort.Equals(p.A, StringComparison.OrdinalIgnoreCase) || rigPort.Equals(p.B, StringComparison.OrdinalIgnoreCase))) continue;
                if (!p.A.Equals(keep ?? "", StringComparison.OrdinalIgnoreCase)) p.Busy = !CanOpen(p.A);
                l.Add(p);
            }
            return l;
        }

        // L'autre bout de la paire dont SDRLink tient com (null : inconnu).
        public static string OtherEnd(string com)
        {
            foreach (Pair p in Pairs())
            {
                if (p.A.Equals(com, StringComparison.OrdinalIgnoreCase)) return p.B;
                if (p.B.Equals(com, StringComparison.OrdinalIgnoreCase)) return p.A;
            }
            return null;
        }

        static bool CanOpen(string port)
        {
            try { using (SerialPort s = new SerialPort(port)) { s.Open(); s.Close(); } return true; }
            catch { return false; }
        }

        // Deux numéros de port consécutifs libres (ni dans Windows, ni dans une paire com0com), à partir de COM30.
        public static int[] FreeNumbers()
        {
            HashSet<int> used = new HashSet<int>();
            foreach (string s in PortsNow()) { Match m = Regex.Match(s, @"^COM(\d+)$", RegexOptions.IgnoreCase); if (m.Success) used.Add(int.Parse(m.Groups[1].Value)); }
            foreach (Pair p in Pairs()) foreach (string s in new string[] { p.A, p.B }) { Match m = Regex.Match(s, @"(\d+)$"); if (m.Success) used.Add(int.Parse(m.Groups[1].Value)); }
            for (int n = 30; n < 250; n++) if (!used.Contains(n) && !used.Contains(n + 1)) return new int[] { n, n + 1 };
            return new int[] { 100, 101 };
        }

        // Installation silencieuse (installateur signé embarqué, vérifié par son empreinte). Windows demande l'autorisation.
        public static bool Install(out string err)
        {
            err = null;
            try
            {
                bool x64 = Environment.Is64BitOperatingSystem;
                string res = x64 ? "com0com-x64.exe" : "com0com-x86.exe", want = x64 ? Hash64 : Hash86;
                string dir = Path.Combine(Path.GetTempPath(), "SDRLink-com0com");
                Directory.CreateDirectory(dir);
                string exe = Path.Combine(dir, res);
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
                {
                    if (s == null) { err = Lang.T("installateur com0com absent de cette version", "com0com installer missing from this version"); return false; }
                    using (FileStream f = File.Create(exe)) s.CopyTo(f);
                }
                string h;
                using (SHA256 sha = SHA256.Create()) using (FileStream f = File.OpenRead(exe)) h = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                if (h != want)
                {
                    err = Lang.T("installateur com0com altéré (empreinte différente)", "com0com installer altered (different fingerprint)");
                    try { File.Delete(exe); } catch { }
                    return false;
                }
                Log.Write("com0com : installation demandée");
                if (!RunAdmin(exe, "/S", dir, 300000, out err)) { Log.Write("com0com : installation refusée : " + err); return false; }
                for (int i = 0; i < 40 && SetupC() == null; i++) System.Threading.Thread.Sleep(500);
                if (SetupC() == null) { err = Lang.T("l'installation n'a pas abouti (setupc.exe introuvable)", "the installation did not complete (setupc.exe not found)"); return false; }
                Log.Write("com0com : installé");
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        // Crée la paire a ↔ b (noms COMn), EmuBR : vitesse imitée comme un vrai port.
        public static bool CreatePair(string a, string b, out string err)
        {
            err = null;
            string sc = SetupC();
            if (sc == null) { err = Lang.T("com0com n'est pas installé", "com0com is not installed"); return false; }
            if (!IsComName(a) || !IsComName(b) || a.Equals(b, StringComparison.OrdinalIgnoreCase)) { err = Lang.T("noms de ports invalides", "invalid port names"); return false; }
            Log.Write("com0com : création demandée de la paire " + a + " <-> " + b);
            if (!RunAdmin(sc, "--silent install PortName=" + a + ",EmuBR=yes PortName=" + b + ",EmuBR=yes", Path.GetDirectoryName(sc), 60000, out err))
            {
                Log.Write("com0com : création refusée : " + err);
                return false;
            }
            for (int i = 0; i < 50; i++)
            {
                foreach (Pair p in Pairs())
                    if (p.A.Equals(a, StringComparison.OrdinalIgnoreCase) && p.B.Equals(b, StringComparison.OrdinalIgnoreCase))
                    {
                        for (int j = 0; j < 20; j++)
                        {
                            foreach (string s in PortsNow()) if (s.Equals(a, StringComparison.OrdinalIgnoreCase)) return true;
                            System.Threading.Thread.Sleep(300);
                        }
                        return true;
                    }
                System.Threading.Thread.Sleep(500);
            }
            err = Lang.T("la paire n'est pas apparue (autorisation refusée ou pilote non confirmé ?)", "the pair did not appear (permission refused or driver not confirmed?)");
            return false;
        }

        // Supprime la paire n° p.N (CNCAn / CNCBn) : seulement une paire listée dans la fenêtre, après confirmation.
        public static bool RemovePair(Pair p, out string err)
        {
            err = null;
            string sc = SetupC();
            if (sc == null) { err = Lang.T("com0com n'est pas installé", "com0com is not installed"); return false; }
            Log.Write("com0com : suppression demandée de la paire " + p.A + " <-> " + p.B + " (n° " + p.N + ")");
            if (!RunAdmin(sc, "--silent remove " + p.N, Path.GetDirectoryName(sc), 60000, out err))
            {
                Log.Write("com0com : suppression refusée : " + err);
                return false;
            }
            for (int i = 0; i < 50; i++)
            {
                bool still = false;
                foreach (Pair q in Pairs()) if (q.N == p.N) still = true;
                if (!still) { Log.Write("com0com : paire " + p.A + " <-> " + p.B + " supprimée"); return true; }
                System.Threading.Thread.Sleep(500);
            }
            err = Lang.T("la paire est toujours là (autorisation refusée, ou port encore ouvert par un logiciel ?)", "the pair is still there (permission refused, or port still open in a program?)");
            return false;
        }

        static bool RunAdmin(string exe, string args, string workDir, int timeoutMs, out string err)
        {
            err = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                psi.WorkingDirectory = workDir;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) { err = Lang.T("démarrage impossible", "cannot start"); return false; }
                    if (!p.WaitForExit(timeoutMs)) { err = Lang.T("délai dépassé", "timed out"); return false; }
                }
                return true;
            }
            catch (Win32Exception ex) { err = ex.NativeErrorCode == 1223 ? Lang.T("autorisation administrateur refusée", "administrator permission refused") : ex.Message; return false; }
            catch (Exception ex) { err = ex.Message; return false; }
        }
    }
}
