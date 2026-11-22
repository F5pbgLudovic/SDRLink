using System;
using System.Globalization;
using System.Speech.Recognition;

namespace SDRLink
{
    // Commande vocale du scan (comme PhoneSDRLink) : la reconnaissance vocale de Windows, hors ligne, n'écoute que quatre
    // ordres avec une grammaire fermée : « stop », « start », « plus un », « moins un ». Le micro (micro par défaut de
    // Windows) n'est ouvert que pendant une session de scan et quelques minutes après son arrêt (MainForm). Langue : celle de
    // SDRLink si Windows a sa reconnaissance vocale, sinon l'anglais, sinon la première langue installée.
    public sealed class VoiceCmd : IDisposable
    {
        // { code, ordres « plus », ordres « moins » } (synonymes séparés par |) ; « stop » et « start » sont les mêmes partout.
        static readonly string[][] Langs =
        {
            new[] { "fr", "plus un|avance", "moins un|recule" },   // « moins un » est mal reconnu par Windows en français : « recule » aussi
            new[] { "en", "plus one", "minus one" },
            new[] { "de", "plus eins", "minus eins" },
            new[] { "es", "más uno", "menos uno" },
            new[] { "it", "più uno", "meno uno" },
            new[] { "nl", "plus een", "min een" },
            new[] { "pt", "mais um", "menos um" },
            new[] { "pl", "plus jeden", "minus jeden" },
            new[] { "cs", "plus jedna", "minus jedna" },
        };

        SpeechRecognitionEngine engine;
        string[] words;
        DateTime lastCmd;
        public event Action<string> Command;   // « stop », « start », « plus », « moins » (sur le fil de la reconnaissance)

        public bool Active { get { return engine != null; } }

        // Phrase « plus un » / « moins un » dans la langue utilisée (pour les messages).
        public string Said(string cmd) { return words == null ? cmd : cmd == "plus" ? words[1].Split('|')[0] : cmd == "moins" ? words[2].Split('|')[0] : cmd; }

        static string[] Spec(string code)
        {
            foreach (string[] l in Langs) if (l[0] == code) return l;
            return null;
        }

        // Ouvre le micro et écoute. Lève une exception si la reconnaissance vocale ou le micro manquent.
        public void Start(string langCode)
        {
            if (engine != null) return;
            RecognizerInfo pick = null, en = null, any = null;
            foreach (RecognizerInfo r in SpeechRecognitionEngine.InstalledRecognizers())
            {
                string c = r.Culture.TwoLetterISOLanguageName;
                if (Spec(c) == null) continue;
                if (any == null) any = r;
                if (c == "en" && en == null) en = r;
                if (c == langCode && pick == null) pick = r;
            }
            pick = pick ?? en ?? any;
            if (pick == null) throw new InvalidOperationException(Lang.T("aucune reconnaissance vocale de Windows dans une langue prise en charge", "no Windows speech recognition in a supported language"));
            words = Spec(pick.Culture.TwoLetterISOLanguageName);
            SpeechRecognitionEngine e = new SpeechRecognitionEngine(pick);
            try
            {
                GrammarBuilder gb = new GrammarBuilder(new Choices(("stop|start|" + words[1] + "|" + words[2]).Split('|'))) { Culture = pick.Culture };
                e.LoadGrammar(new Grammar(gb));
                e.SetInputToDefaultAudioDevice();
                e.SpeechRecognized += OnRecognized;
                e.RecognizeAsync(RecognizeMode.Multiple);
            }
            catch { e.Dispose(); throw; }
            engine = e;
            Log.Write("Commande vocale : écoute en " + pick.Culture.Name + " (" + pick.Description + ")");
        }

        void OnRecognized(object sender, SpeechRecognizedEventArgs a)
        {
            if (a.Result == null || a.Result.Confidence < 0.6f) return;
            DateTime now = DateTime.UtcNow;
            if ((now - lastCmd).TotalMilliseconds < 1200) return;   // répétition immédiate
            lastCmd = now;
            string t = a.Result.Text;
            string cmd = t == "stop" ? "stop" : t == "start" ? "start" : Array.IndexOf(words[1].Split('|'), t) >= 0 ? "plus" : Array.IndexOf(words[2].Split('|'), t) >= 0 ? "moins" : null;
            if (cmd == null) return;
            Log.Write("Commande vocale : « " + t + " » (" + a.Result.Confidence.ToString("0.00", CultureInfo.InvariantCulture) + ")");
            Action<string> h = Command;
            if (h != null) h(cmd);
        }

        public void Stop()
        {
            SpeechRecognitionEngine e = engine;
            engine = null;
            if (e == null) return;
            e.SpeechRecognized -= OnRecognized;
            try { e.RecognizeAsyncCancel(); } catch { }
            try { e.SetInputToNull(); } catch { }
            try { e.Dispose(); } catch { }
            Log.Write("Commande vocale : micro fermé");
        }

        public void Dispose() { Stop(); }
    }
}
