using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace SDRLink
{
    // Hamlib : dialogue avec rigctld (serveur réseau de Hamlib, port 4532 par défaut), protocole texte d'une ligne
    // par commande : f / F (fréquence), m / M (mode), t / T (PTT), l AF / L AF (volume, de 0 à 1).
    public class HamlibRig : IRig
    {
        readonly string host;
        readonly int port;
        TcpClient client;
        NetworkStream stream;
        StreamReader reader;
        bool afUnsupported, passbandNoChange = true;
        int afFailures;

        public HamlibRig(string host, int port)
        {
            this.host = host;
            this.port = port;
        }

        public void Open()
        {
            client = new TcpClient();
            client.NoDelay = true;
            IAsyncResult ar = client.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(5000) || !client.Connected)
            {
                string why = Rigctld.LastError;
                throw new Exception(Lang.F("Pas de rigctld sur {0}:{1} (lancez rigctld, ou cochez « Lancer rigctld »).",
                    "No rigctld at {0}:{1} (start rigctld, or tick \"Start rigctld\").", host, port) + (why != null ? " rigctld : " + why : ""));
            }
            client.EndConnect(ar);
            client.ReceiveTimeout = 2500;
            stream = client.GetStream();
            reader = new StreamReader(stream, Encoding.ASCII);
            afUnsupported = false;
            string[] v = Command("_", 1);   // nom du poste (rigctld : commande "_" = get_info)
            Log.Write("Hamlib : connecté à rigctld " + host + ":" + port + (v != null ? " (" + v[0] + ")" : ""));
        }

        public void Close()
        {
            if (client != null) { try { client.Close(); } catch { } client = null; stream = null; reader = null; }
        }

        public void Dispose() { Close(); }

        public bool Read(out long freqHz, out string mode)
        {
            freqHz = 0;
            mode = null;
            string[] f = Command("f", 1);
            double d;
            if (f == null || !double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d <= 0) return false;
            freqHz = (long)Math.Round(d);
            string[] m = Command("m", 2);
            if (m != null) mode = FromHamlib(m[0]);
            return true;
        }

        public int ReadTx()
        {
            string[] t = Command("t", 1);
            if (t == null) return -1;
            return t[0] == "0" ? 0 : t[0] == "1" || t[0] == "2" || t[0] == "3" ? 1 : -1;
        }

        public void SetPtt(bool tx) { Command("T " + (tx ? "1" : "0"), 0); }

        public int ReadAfGain()
        {
            if (afUnsupported) return -1;
            string[] a = Command("l AF", 1);
            double v;
            // Abandon après 3 échecs de suite seulement : une réponse perdue ne doit pas griser le volume.
            if (a == null || !double.TryParse(a[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                if (++afFailures >= 3) { afUnsupported = true; Log.Write("Hamlib : volume (l AF) non pris en charge par ce poste"); }
                return -1;
            }
            afFailures = 0;
            return (int)Math.Round(Math.Max(0, Math.Min(1, v)) * 100);
        }

        public void SetAfGain(int percent)
        {
            if (afUnsupported) return;
            Command("L AF " + (Math.Max(0, Math.Min(100, percent)) / 100.0).ToString("0.000", CultureInfo.InvariantCulture), 0);
        }

        public void SetFreq(long freqHz) { Command("F " + freqHz.ToString(CultureInfo.InvariantCulture), 0); }

        public void SetMode(string mode)
        {
            string m = ToHamlib(mode);
            if (m == null) return;
            // Largeur de filtre -1 = inchangée ; les anciens rigctld ne la connaissent pas : 0 = largeur normale du mode.
            if (passbandNoChange && Command("M " + m + " -1", 0) == null)
            {
                passbandNoChange = false;
                Command("M " + m + " 0", 0);
            }
            else if (!passbandNoChange) Command("M " + m + " 0", 0);
        }

        // Envoie une commande ; renvoie les `lines` lignes de réponse, ou null si rigctld répond par une erreur (RPRT -n).
        // Les commandes de réglage (lines = 0) répondent "RPRT 0" en cas de succès : on renvoie alors un tableau vide.
        string[] Command(string cmd, int lines)
        {
            if (stream == null) throw new IOException(Lang.T("Connexion rigctld fermée", "rigctld connection closed"));
            while (stream.DataAvailable) reader.ReadLine();   // réponse en retard d'une commande précédente
            byte[] b = Encoding.ASCII.GetBytes(cmd + "\n");
            stream.Write(b, 0, b.Length);
            string first = ReadLine();
            if (first.StartsWith("RPRT", StringComparison.Ordinal))
                return first.Trim() == "RPRT 0" && lines == 0 ? new string[0] : null;
            if (lines == 0) return new string[0];
            string[] r = new string[lines];
            r[0] = first.Trim();
            for (int i = 1; i < lines; i++) r[i] = ReadLine().Trim();
            return r;
        }

        string ReadLine()
        {
            string s = reader.ReadLine();
            if (s == null) throw new IOException(Lang.T("Connexion rigctld fermée", "rigctld connection closed"));
            return s;
        }

        static string FromHamlib(string m)
        {
            switch ((m ?? "").ToUpperInvariant())
            {
                case "USB": return Modes.USB;
                case "LSB": return Modes.LSB;
                case "CW": return Modes.CW;
                case "CWR": return Modes.CWR;
                case "RTTY": return Modes.RTTY;
                case "RTTYR": return Modes.RTTYR;
                case "AM": case "AMS": case "SAM": case "AMN": case "PKTAM": case "DSB": return Modes.AM;
                case "FM": case "FMN": case "WFM": case "PKTFM": case "PKTFMN": return Modes.FM;
                case "PKTUSB": return Modes.DATAU;
                case "PKTLSB": return Modes.DATAL;
            }
            return null;
        }

        static string ToHamlib(string m)
        {
            switch (m)
            {
                case Modes.USB: return "USB";
                case Modes.LSB: return "LSB";
                case Modes.CW: return "CW";
                case Modes.CWR: return "CWR";
                case Modes.RTTY: return "RTTY";
                case Modes.RTTYR: return "RTTYR";
                case Modes.AM: return "AM";
                case Modes.FM: return "FM";
                case Modes.DATAU: return "PKTUSB";
                case Modes.DATAL: return "PKTLSB";
            }
            return null;
        }

        // Numéros de modèle Hamlib (include/hamlib/riglist.h, Hamlib 4.7) des postes du catalogue.
        public static readonly Dictionary<string, int> ModelIds = new Dictionary<string, int>
        {
            { "FT-2000", 1029 }, { "FTdx10", 1042 }, { "FTdx101D/MP", 1040 }, { "FT-991/991A", 1035 }, { "FT-891", 1036 },
            { "FT-710", 1049 }, { "FTdx5000", 1032 }, { "FTdx3000", 1037 }, { "FTdx1200", 1034 }, { "FT-950", 1028 },
            { "FT-450/450D", 1027 }, { "FT-817", 1020 }, { "FT-817ND", 1020 }, { "FT-818", 1041 }, { "FT-818ND", 1041 },
            { "FT-857", 1022 }, { "FT-857D", 1022 }, { "FT-897", 1023 }, { "FT-897D", 1043 },
            { "IC-7000", 3060 }, { "IC-7760", 3092 }, { "IC-7300", 3073 }, { "IC-7610", 3078 }, { "IC-705", 3085 },
            { "IC-9700", 3081 }, { "IC-7100", 3070 }, { "IC-7200", 3061 }, { "IC-7410", 3067 }, { "IC-7600", 3063 },
            { "IC-7700", 3062 }, { "IC-7800", 3056 }, { "IC-7851", 3075 }, { "IC-756PROIII", 3057 }, { "IC-746PRO", 3046 },
            { "IC-718", 3013 }, { "IC-706MKIIG", 3011 }, { "IC-706MKII", 3010 }, { "IC-706", 3009 },
            { "TS-590S/SG", 2031 }, { "TS-890S", 2041 }, { "TS-990S", 2039 }, { "TS-2000", 2014 }, { "TS-480", 2028 },
            { "TS-870S", 2010 }, { "TS-570", 2016 }, { "TS-850S", 2009 },
            { "K4", 2047 }, { "K3/K3S", 2029 }, { "KX3", 2045 }, { "KX2", 2044 }, { "K2", 2021 },
            { "G90", 3088 }, { "X6100", 3087 }, { "X6200", 3091 },
        };
    }

    // rigctld lancé par SDRLink (option « Lancer rigctld ») : un seul processus, arrêté à la déconnexion.
    public static class Rigctld
    {
        static Process process;
        static string lastError;

        // Dernière ligne d'erreur de rigctld s'il s'est arrêté (port COM occupé, modèle inconnu...).
        public static string LastError
        {
            get { lock (typeof(Rigctld)) return process != null && process.HasExited ? lastError ?? Lang.T("arrêté", "stopped") : null; }
        }

        // Emplacements habituels : installation de Hamlib, puis WSJT-X / JTDX (rigctld-wsjtx), puis le PATH.
        public static string FindExe(string configured)
        {
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
            List<string> roots = new List<string>();
            foreach (string v in new[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432" })
            {
                string r = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrEmpty(r) && !roots.Contains(r)) roots.Add(r);
            }
            try
            {
                foreach (string root in roots)
                    foreach (string dir in Directory.GetDirectories(root, "hamlib*"))
                    {
                        string exe = Path.Combine(Path.Combine(dir, "bin"), "rigctld.exe");
                        if (File.Exists(exe)) return exe;
                    }
                foreach (string path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                {
                    string exe = Path.Combine(path.Trim(), "rigctld.exe");
                    if (path.Trim() != "" && File.Exists(exe)) return exe;
                }
                List<string> wsjt = new List<string>();
                foreach (string root in roots)
                    foreach (string app in new[] { "wsjtx", "JTDX", "jtdx" })
                        wsjt.Add(Path.Combine(Path.Combine(Path.Combine(root, app), "bin"), "rigctld-wsjtx.exe"));
                if (Directory.Exists(@"C:\WSJT"))
                    foreach (string dir in Directory.GetDirectories(@"C:\WSJT"))
                        wsjt.Add(Path.Combine(Path.Combine(dir, "bin"), "rigctld-wsjtx.exe"));
                foreach (string exe in wsjt) if (File.Exists(exe)) return exe;
            }
            catch (Exception) { }
            return null;
        }

        public static void Start(string exe, string args)
        {
            Stop();
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.WorkingDirectory = Path.GetDirectoryName(exe);
            Process p = new Process();
            p.StartInfo = psi;
            DataReceivedEventHandler onLine = delegate(object s, DataReceivedEventArgs e)
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                lock (typeof(Rigctld)) lastError = e.Data.Trim();
                Log.Write("rigctld : " + e.Data.Trim());
            };
            p.ErrorDataReceived += onLine;
            p.OutputDataReceived += onLine;
            lastError = null;
            p.Start();
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();
            lock (typeof(Rigctld)) process = p;
            Log.Write("rigctld lancé : \"" + exe + "\" " + args);
        }

        public static void Stop()
        {
            Process p;
            lock (typeof(Rigctld)) { p = process; process = null; }
            if (p == null) return;
            try { if (!p.HasExited) { p.Kill(); p.WaitForExit(2000); } } catch { }
            p.Dispose();
            Log.Write("rigctld arrêté");
        }
    }
}
