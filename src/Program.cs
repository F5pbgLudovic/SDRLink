using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace SDRLink
{
    static class Program
    {
        // Version affichée dans le titre ; à garder identique à AssemblyInfo.cs.
        public const string Version = "1.1";

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Langue lue dès le départ pour que les messages de vérification soient dans la bonne langue.
            try { Lang.English = Settings.Load().Language == "en"; } catch { }
            if (!Prerequisites.CheckDotNet()) return;
            Prerequisites.InstallEmbeddedLibraries();
            Run();
        }

        // Méthode séparée : les types WebView2 ne doivent être résolus qu'une fois les DLL intégrées disponibles.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Run()
        {
            if (!Prerequisites.CheckWebView2()) return;
            Log.Reset();
            Application.Run(new MainForm());
        }
    }
}
