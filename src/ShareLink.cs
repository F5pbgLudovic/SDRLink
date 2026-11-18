using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace SDRLink
{
    // Lien de partage d'une fréquence (1.46) : <page>?u=<WebSDR>&f=<Hz>&m=<mode>[&k=<type>].
    //   - page web de SDRLink (GitHub Pages) : propose d'ouvrir SDRLink (lien sdrlink://), d'écouter dans le navigateur,
    //     ou de télécharger SDRLink ; sur Android, d'ouvrir PhoneSDRLink (même format de lien) ;
    //   - sdrlink://tune?… : ouvert par Windows avec SDRLink (protocole inscrit pour l'utilisateur, sans droits d'administrateur) ;
    //   - les liens de PhoneSDRLink (même format) sont aussi compris, collés dans la barre d'adresse.
    public class ShareLink
    {
        public const string PageBase = "https://f5pbgludovic.github.io/SDRLink/s/";
        const string PhonePageBase = "https://f5pbgludovic.github.io/PhoneSDRLink/s/";
        public const string Scheme = "sdrlink";
        static readonly string[] ModesOk = { "USB", "LSB", "AM", "FM", "CW", "DATAU" };

        public string Url = "";    // adresse du WebSDR
        public long Hz;            // fréquence du WebSDR
        public string Mode = "";   // USB, LSB, AM, FM, CW, DATAU
        public string Kind = "";   // websdr, kiwi, owrx ou vide

        public static string Build(string url, long hz, string mode, string kind)
        {
            string k = kind.StartsWith("owrx", StringComparison.Ordinal) ? "owrx" : kind == "websdr" || kind == "kiwi" ? kind : "";
            if (k == "owrx") { int hash = url.IndexOf('#'); if (hash >= 0) url = url.Substring(0, hash); }
            string s = PageBase + "?u=" + Uri.EscapeDataString(url) + "&f=" + hz.ToString(CultureInfo.InvariantCulture) + "&m=" + mode;
            if (k != "") s += "&k=" + k;
            return s;
        }

        // Lien sdrlink://, page de SDRLink ou page de PhoneSDRLink ; null si ce n'en est pas un ou s'il est incomplet.
        public static ShareLink Parse(string text)
        {
            string s = (text ?? "").Trim().Trim('"', '<', '>');
            int q;
            if (s.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase)) q = s.IndexOf('?');
            else if (s.StartsWith(PageBase, StringComparison.OrdinalIgnoreCase) || s.StartsWith(PhonePageBase, StringComparison.OrdinalIgnoreCase)) q = s.IndexOf('?');
            else return null;
            if (q < 0) return null;
            string query = s.Substring(q + 1);
            int hashAt = query.IndexOf('#');
            if (hashAt >= 0) query = query.Substring(0, hashAt);
            Dictionary<string, string> p = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string part in query.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq);
                string val;
                try { val = Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' ')); } catch { continue; }
                if (!p.ContainsKey(key)) p[key] = val;
            }
            string u, f, m, k;
            if (!p.TryGetValue("u", out u) || !p.TryGetValue("f", out f) || !p.TryGetValue("m", out m)) return null;
            u = u.Trim();
            Uri uri;
            if (!Uri.TryCreate(u, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return null;
            long hz;
            if (!long.TryParse(f.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out hz) || hz < 1000 || hz > 3000000000L) return null;
            m = m.Trim().ToUpperInvariant();
            if (Array.IndexOf(ModesOk, m) < 0) return null;
            p.TryGetValue("k", out k);
            k = (k ?? "").Trim().ToLowerInvariant();
            if (k != "websdr" && k != "kiwi" && k != "owrx") k = "";
            return new ShareLink { Url = u, Hz = hz, Mode = m, Kind = k };
        }

        // Adresse du WebSDR déjà réglée sur la fréquence et le mode (comme la page de partage) : websdr.org ?tune=14074usb,
        // KiwiSDR ?f=14074usb, OpenWebRX #freq=14074000,mod=usb ; autre type : adresse telle quelle (SDRLink règle ensuite).
        public string TunedUrl()
        {
            string kh = (Hz / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
            string sub = Mode == "DATAU" ? "usb" : Mode.ToLowerInvariant();
            try
            {
                if (Kind == "websdr") return SetQuery(Url, "tune", kh + sub);
                if (Kind == "kiwi") return SetQuery(Url, "f", kh + (Mode == "FM" ? "nbfm" : sub));
                if (Kind == "owrx")
                {
                    int hash = Url.IndexOf('#');
                    return (hash >= 0 ? Url.Substring(0, hash) : Url) + "#freq=" + Hz.ToString(CultureInfo.InvariantCulture) + ",mod=" + (Mode == "FM" ? "nfm" : sub);
                }
            }
            catch { }
            return Url;
        }

        static string SetQuery(string url, string key, string value)
        {
            UriBuilder b = new UriBuilder(url);
            string q = b.Query.TrimStart('?');
            List<string> parts = new List<string>();
            foreach (string part in q.Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                if ((eq < 0 ? part : part.Substring(0, eq)) == key) continue;
                parts.Add(part);
            }
            parts.Add(key + "=" + Uri.EscapeDataString(value));
            b.Query = string.Join("&", parts.ToArray());
            return b.Uri.AbsoluteUri;
        }

        public string ModeText { get { return Mode == "DATAU" ? "DATA" : Mode; } }

        // ---------------------------------------------------------------- Protocole sdrlink:// (Windows)

        // Inscrit sdrlink:// pour l'utilisateur (HKCU, sans droits d'administrateur), vers cet exe. Refait à chaque démarrage :
        // l'exe peut avoir été déplacé ou mis à jour. Pas pour les copies d'essai (SDRLINK_DATA).
        public static void RegisterProtocol(string exePath)
        {
            if (Environment.GetEnvironmentVariable("SDRLINK_DATA") != null) return;
            try
            {
                string cmd = "\"" + exePath + "\" \"%1\"";
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Scheme))
                {
                    if ((k.GetValue("") as string) != "URL:SDRLink") k.SetValue("", "URL:SDRLink");
                    if (k.GetValue("URL Protocol") == null) k.SetValue("URL Protocol", "");
                    using (RegistryKey icon = k.CreateSubKey("DefaultIcon"))
                        if ((icon.GetValue("") as string) != "\"" + exePath + "\",0") icon.SetValue("", "\"" + exePath + "\",0");
                    using (RegistryKey c = k.CreateSubKey(@"shell\open\command"))
                        if ((c.GetValue("") as string) != cmd) { c.SetValue("", cmd); Log.Write("Liens sdrlink:// inscrits vers " + exePath); }
                }
            }
            catch (Exception ex) { Log.Write("Liens sdrlink:// non inscrits : " + ex.Message); }
        }

        // ---------------------------------------------------------------- Transmission à SDRLink déjà ouvert

        // Un tube nommé par dossier de données : la copie d'essai et la vraie station ne se parlent pas.
        static string PipeName
        {
            get
            {
                using (MD5 md5 = MD5.Create())
                {
                    byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes(Log.DataDir.TrimEnd('\\').ToLowerInvariant()));
                    return "SDRLink.share." + BitConverter.ToString(h, 0, 8).Replace("-", "");
                }
            }
        }

        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

        // Vrai si un SDRLink déjà ouvert (même dossier de données) a reçu le lien.
        public static bool SendToRunning(string link)
        {
            try
            {
                using (NamedPipeClientStream c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    c.Connect(1500);
                    AllowSetForegroundWindow(-1);   // la fenêtre déjà ouverte peut passer devant
                    byte[] b = Encoding.UTF8.GetBytes(link + "\n");
                    c.Write(b, 0, b.Length);
                    c.Flush();
                    c.WaitForPipeDrain();
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        // Écoute les liens envoyés par un second lancement (fil de fond) ; onLink est appelé sur ce fil.
        public static void Listen(Action<string> onLink)
        {
            Thread t = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        using (NamedPipeServerStream s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1))
                        {
                            s.WaitForConnection();
                            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                            {
                                string line = r.ReadLine();
                                if (!string.IsNullOrEmpty(line) && line.Length < 8192) onLink(line);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Liens de partage : écoute arrêtée (" + ex.Message + ")");
                        return;   // autre SDRLink sur le même dossier : c'est lui qui reçoit les liens
                    }
                }
            });
            t.IsBackground = true;
            t.Name = "SDRLink share";
            t.Start();
        }
    }
}
