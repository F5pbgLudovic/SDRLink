using System;
using System.IO;

namespace SDRLink
{
    // Journal de diagnostic : %APPDATA%\SDRLink\sdrlink.log (remis à zéro à chaque démarrage).
    public static class Log
    {
        static readonly object sync = new object();
        // Dossier des réglages et du journal ; la variable SDRLINK_DATA permet d'en utiliser un autre (tests).
        public static readonly string DataDir = Environment.GetEnvironmentVariable("SDRLINK_DATA") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SDRLink");
        public static readonly string FilePath = Path.Combine(DataDir, "sdrlink.log");

        public static void Reset()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath, ""); } catch { }
        }

        public static void Write(string msg)
        {
            lock (sync)
            {
                try { File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + "\r\n"); } catch { }
            }
        }
    }
}
