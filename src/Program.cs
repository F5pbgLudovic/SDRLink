using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace SDRLink
{
    static class Program
    {
        // Version affichée dans le titre ; à garder identique à AssemblyInfo.cs.
        public const string Version = "1.6";

        [STAThread]
        static int Main(string[] args)
        {
            // Relancé avec les droits d'administrateur pour renommer un périphérique audio (câble SDRLINK).
            if (args.Length == 3 && args[0] == "--name-endpoint") return AudioEndpoints.SetName(args[1], args[2]);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Langue lue dès le départ pour que les messages de vérification soient dans la bonne langue.
            try { Lang.Code = Settings.Load().Language; } catch { }
            if (!Prerequisites.CheckDotNet()) return 1;
            Prerequisites.InstallEmbeddedLibraries();
            Run();
            return 0;
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
