using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SDRLink
{
    // Rend l'exe autonome (DLL WebView2 intégrées en ressources) et vérifie les composants Windows nécessaires.
    public static class Prerequisites
    {
        const string WebView2Version = "1.0.2903.40";
        const int DotNet462Release = 394802;
        const string DotNetInstallerUrl = "https://go.microsoft.com/fwlink/?LinkId=2085155";      // .NET Framework 4.8 (installeur web)
        const string DotNetPageUrl = "https://dotnet.microsoft.com/download/dotnet-framework/net48";
        const string WebView2InstallerUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";  // WebView2 Evergreen (bootstrapper)
        const string WebView2PageUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

        static string loaderFolder;

        // ------------------------------------------------------------ .NET Framework

        public static bool CheckDotNet()
        {
            int release = 0;
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                    .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (k != null) release = Convert.ToInt32(k.GetValue("Release", 0));
                }
            }
            catch { }
            if (release >= DotNet462Release) return true;

            DialogResult r = MessageBox.Show(Lang.T(
                "SDRLink a besoin de Microsoft .NET Framework 4.6.2 ou plus récent (4.8 recommandé).\n" +
                "Il n'est pas installé sur ce PC.\n\n" +
                "Télécharger et installer .NET Framework 4.8 maintenant ?\n" +
                "(un redémarrage du PC peut être demandé, relancez ensuite SDRLink)",
                "SDRLink needs Microsoft .NET Framework 4.6.2 or later (4.8 recommended).\n" +
                "It is not installed on this PC.\n\n" +
                "Download and install .NET Framework 4.8 now?\n" +
                "(a restart may be required, then start SDRLink again)"),
                Lang.T("SDRLink - composant manquant", "SDRLink - missing component"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes)
                DownloadAndRun(DotNetInstallerUrl, "ndp48-web.exe", "", DotNetPageUrl);
            return false;
        }

        // ------------------------------------------------------------ DLL intégrées

        public static void InstallEmbeddedLibraries()
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
            {
                string name = new AssemblyName(e.Name).Name;
                if (name != "Microsoft.Web.WebView2.Core" && name != "Microsoft.Web.WebView2.WinForms") return null;
                byte[] data = ReadResource(name + ".dll");
                return data == null ? null : Assembly.Load(data);
            };

            // La DLL native ne peut pas être chargée depuis la mémoire : on l'écrit une fois sur le disque.
            loaderFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SDRLink", "runtime", WebView2Version);
            string path = Path.Combine(loaderFolder, "WebView2Loader.dll");
            byte[] loader = ReadResource("WebView2Loader.dll");
            try
            {
                if (loader != null && (!File.Exists(path) || new FileInfo(path).Length != loader.Length))
                {
                    Directory.CreateDirectory(loaderFolder);
                    File.WriteAllBytes(path, loader);
                }
            }
            catch (IOException) { }   // déjà présent et utilisé par une autre instance
        }

        static byte[] ReadResource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                byte[] b = new byte[s.Length];
                int got = 0;
                while (got < b.Length) got += s.Read(b, got, b.Length - got);
                return b;
            }
        }

        // ------------------------------------------------------------ Edge WebView2

        // Appelée seulement après InstallEmbeddedLibraries (sinon les types WebView2 ne se chargeraient pas).
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool CheckWebView2()
        {
            SetLoaderFolder();
            while (true)
            {
                string version = WebView2RuntimeVersion();
                if (version != null) return true;

                DialogResult r = MessageBox.Show(Lang.T(
                    "SDRLink a besoin du composant Microsoft Edge WebView2 Runtime pour afficher les WebSDR.\n" +
                    "Il n'est pas installé sur ce PC.\n\n" +
                    "Télécharger et installer WebView2 maintenant ? (gratuit, environ 2 Mo à télécharger)",
                    "SDRLink needs the Microsoft Edge WebView2 Runtime to display WebSDRs.\n" +
                    "It is not installed on this PC.\n\n" +
                    "Download and install WebView2 now? (free, about 2 MB)"),
                    Lang.T("SDRLink - composant manquant", "SDRLink - missing component"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return false;
                if (!DownloadAndRun(WebView2InstallerUrl, "MicrosoftEdgeWebview2Setup.exe", "/install", WebView2PageUrl))
                    return false;
                // Boucle : on revérifie après l'installation.
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void SetLoaderFolder()
        {
            if (loaderFolder != null && File.Exists(Path.Combine(loaderFolder, "WebView2Loader.dll")))
                Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(loaderFolder);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static string WebView2RuntimeVersion()
        {
            try
            {
                string v = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
                return string.IsNullOrEmpty(v) ? null : v;
            }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------ Téléchargement

        static bool DownloadAndRun(string url, string fileName, string args, string fallbackPage)
        {
            string file = Path.Combine(Path.GetTempPath(), fileName);
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
                Cursor.Current = Cursors.WaitCursor;
                using (WebClient wc = new WebClient()) wc.DownloadFile(url, file);
                Cursor.Current = Cursors.Default;
                using (Process p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }))
                {
                    if (p != null) p.WaitForExit();
                }
                return true;
            }
            catch (Exception ex)
            {
                Cursor.Current = Cursors.Default;
                MessageBox.Show(Lang.T("Le téléchargement ou l'installation a échoué :\n", "Download or installation failed:\n") + ex.Message +
                    Lang.T("\n\nLa page de téléchargement officielle va s'ouvrir :\n", "\n\nThe official download page will open:\n") + fallbackPage,
                    "SDRLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
                try { Process.Start(fallbackPage); } catch { }
                return false;
            }
        }
    }
}
