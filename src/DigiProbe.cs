using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SDRLink
{
    // Recherche du mode écouté (bouton « Mode ? », depuis la 1.38) : pendant au plus Seconds secondes, le son décodé est envoyé en même
    // temps à plusieurs décodeurs (FT8, FT4, FT2, JS8, SSTV, WEFAX, RTTY, PSK31, CW, Olivia). Les modes à contrôle d'erreur
    // (message FT8/FT4/FT2/JS8 reçu, en-tête VIS SSTV, ton de début WEFAX) donnent une réponse sûre et arrêtent la recherche tout de suite.
    // Les modes en texte (RTTY, PSK, CW, Olivia) sont notés d'après leur texte : indicatifs, CQ, DE, 73, RST… ; le meilleur l'emporte s'il
    // dépasse un seuil. Les décodeurs ne tournent que le temps de la recherche, puis sont arrêtés.
    public sealed class ModeProbe
    {
        public const int Seconds = 33;

        sealed class Cand
        {
            public string Mode;
            public DigiEngine E;
            public SstvEngine S;
            public Resampler Rs;
            public readonly StringBuilder Text = new StringBuilder();
            public int Msgs;
        }

        readonly List<Cand> cands = new List<Cand>();
        readonly object gate = new object();
        string sure, sureDetail;
        volatile bool stopped;

        // Réponse sûre (fil d'un décodeur) : mode et détail ; l'interface repasse dans son fil.
        public event Action<string, string> Sure;

        public ModeProbe(int js8Speed)
        {
            Add("FT8", new Ft8Engine(0));
            Add("FT4", new Ft8Engine(1));
            Add("FT2", new Ft8Engine(2));
            Add("JS8", new Js8Engine(Math.Max(0, Math.Min(4, js8Speed))));
            Add("RTTY", new RttyEngine());
            Add("PSK", new PskEngine());
            Add("CW", new CwEngine());
            Add("Olivia", new OliviaEngine(false));
            Add("WEFAX", new FaxEngine());
            Cand s = new Cand { Mode = "SSTV", S = new SstvEngine() };
            s.S.ImageBegin += delegate { Hit(s, Lang.T("en-tête VIS reçu", "VIS header received")); };
            cands.Add(s);
        }

        void Add(string mode, DigiEngine e)
        {
            Cand c = new Cand { Mode = mode, E = e, Rs = new Resampler(e.Rate) };
            if (mode == "WEFAX") e.ImageBegin += delegate { Hit(c, Lang.T("ton de début reçu", "start tone received")); };
            else
            {
                e.Lines += delegate(List<string> l, int parity)
                {
                    if (l == null || l.Count == 0) return;
                    lock (gate) c.Msgs += l.Count;
                    Hit(c, Lang.F("{0} message(s)", "{0} message(s)", c.Msgs));
                };
                e.Text += delegate(string t) { lock (gate) { if (c.Text.Length < 4000) c.Text.Append(t); } };
            }
            cands.Add(c);
        }

        void Hit(Cand c, string detail)
        {
            Action<string, string> h;
            lock (gate)
            {
                if (sure != null || stopped) return;
                sure = c.Mode; sureDetail = detail;
                h = Sure;
            }
            if (h != null) h(c.Mode, detail);
        }

        // Son de la réception (fil de capture).
        public void Feed(int rate, float[] x, int n)
        {
            if (stopped) return;
            foreach (Cand c in cands)
            {
                if (c.S != null) { c.S.Feed(rate, x, n); continue; }
                float[] y;
                lock (c.Rs) y = c.Rs.Process(rate, x, n);
                if (y.Length > 0) c.E.Feed(y, y.Length);
            }
        }

        // Arrêt de tous les décodeurs, dans un fil à part (un arrêt peut attendre la fin d'un calcul).
        public void Stop()
        {
            if (stopped) return;
            stopped = true;
            List<Cand> l = new List<Cand>(cands);
            Thread t = new Thread(delegate()
            {
                foreach (Cand c in l)
                {
                    try { if (c.S != null) c.S.Stop(); else c.E.Stop(); } catch { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // Meilleur mode en texte (RTTY, PSK, CW, Olivia) d'après la note de son texte ; null si aucun ne dépasse le seuil.
        public string BestText(out string detail, out int score)
        {
            detail = ""; score = 0;
            string best = null;
            lock (gate)
                foreach (Cand c in cands)
                {
                    if (c.Text.Length == 0) continue;
                    int s = TextScore(c.Text.ToString());
                    if (s > score) { score = s; best = c.Mode; detail = Excerpt(c.Text.ToString()); }
                }
            return score >= Threshold ? best : null;
        }

        public const int Threshold = 9;

        static readonly Regex Call = new Regex(@"^(?:[A-Z]{1,2}|[A-Z][0-9]|[0-9][A-Z])[0-9][A-Z]{1,4}$", RegexOptions.CultureInvariant);
        static readonly HashSet<string> Words = new HashSet<string>(StringComparer.Ordinal)
        {
            "TU", "73", "QSL", "RST", "5NN", "599", "QRZ", "TEST", "NAME", "QTH", "UR", "BK", "KN", "SK", "OP", "RIG", "ANT", "WX", "PSE", "TNX", "FB", "GM", "GA", "GE", "HW", "CPY", "QSO", "DX"
        };

        // Note d'un texte décodé : 6 par indicatif, 4 par CQ ou DE, 2 par mot courant du trafic ; divisée par 3 si le texte est
        // surtout fait de signes (bruit décodé).
        public static int TextScore(string text)
        {
            string t = text.ToUpperInvariant();
            int junk = 0, total = 0;
            foreach (char ch in t)
            {
                if (char.IsWhiteSpace(ch)) continue;
                total++;
                if (!((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '/' || ch == '?' || ch == '.' || ch == ',' || ch == '-')) junk++;
            }
            int score = 0;
            foreach (string tok in Regex.Split(t, @"[^A-Z0-9/]+"))
            {
                if (tok.Length == 0) continue;
                string w = tok;
                int slash = w.IndexOf('/');
                if (slash > 0) w = w.Substring(0, slash);
                if (w == "CQ" || w == "DE") score += 4;
                else if (Words.Contains(w)) score += 2;
                else if (w.Length >= 3 && w.Length <= 7 && Call.IsMatch(w)) score += 6;
            }
            if (total > 0 && junk * 10 > total * 3) score /= 3;
            return score;
        }

        static string Excerpt(string s)
        {
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > 40 ? s.Substring(s.Length - 40) : s;
        }

        public string SureMode { get { lock (gate) return sure; } }
        public string SureDetail { get { lock (gate) return sureDetail; } }
    }
}
