using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Relève les textes à traduire dans src\*.cs : appels Lang.T / Lang.F et assistants Lbl / Btn / Check (français, anglais),
// listes Combo(largeur, new[] {français...}, new[] {anglais...}). Écrit lang\strings.txt (anglais <TAB> français)
// et signale les appels dont le texte n'est pas une simple chaîne (il faut alors passer par Lang.F).
// Usage : LangExtract.exe <dossier src> <fichier de sortie>
class LangExtract
{
    static readonly string[] Calls = { "Lang.T(", "Lang.F(", "Lbl(", "Btn(", "Check(" };
    static readonly List<string[]> found = new List<string[]>();
    static int dynamicCount;

    static int Main(string[] args)
    {
        foreach (string file in Directory.GetFiles(args[0], "*.cs"))
        {
            if (Path.GetFileName(file) == "LangData.cs") continue;
            string src = File.ReadAllText(file, Encoding.UTF8);
            foreach (string call in Calls) Scan(file, src, call);
            ScanCombos(file, src);
        }
        Dictionary<string, string> seen = new Dictionary<string, string>();
        StringBuilder sb = new StringBuilder();
        foreach (string[] p in found)
        {
            if (p[1].Trim() == "" || seen.ContainsKey(p[1])) continue;
            seen[p[1]] = p[0];
            sb.Append(Escape(p[1])).Append('\t').Append(Escape(p[0])).Append('\n');
        }
        File.WriteAllText(args[1], sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine(seen.Count + " textes, " + dynamicCount + " appel(s) non constant(s)");
        return 0;
    }

    static void Scan(string file, string src, string call)
    {
        int i = 0;
        while ((i = src.IndexOf(call, i, StringComparison.Ordinal)) >= 0)
        {
            int start = i;
            i += call.Length;
            // "Lbl(" ne doit pas être la fin d'un autre nom (ex. "AddLbl(") ; on ignore aussi les définitions.
            if (call[0] != 'L' || call.StartsWith("Lang"))
            {
                if (start > 0 && (char.IsLetterOrDigit(src[start - 1]) || src[start - 1] == '_')) continue;
            }
            else if (start > 0 && (char.IsLetterOrDigit(src[start - 1]) || src[start - 1] == '.' || src[start - 1] == '_')) continue;
            List<string> argsText = SplitArgs(src, i);
            if (argsText == null || argsText.Count < 2) continue;
            if (argsText[0].TrimStart().StartsWith("string ")) continue;   // définition de la méthode
            string fr = Literal(argsText[0]), en = Literal(argsText[1]);
            if (fr == null || en == null)
            {
                dynamicCount++;
                Console.WriteLine("NON CONSTANT " + Path.GetFileName(file) + ":" + Line(src, start) + "  " + Short(src.Substring(start, Math.Min(140, src.Length - start))));
                continue;
            }
            found.Add(new[] { fr, en });
        }
    }

    static void ScanCombos(string file, string src)
    {
        int i = 0;
        while ((i = src.IndexOf("Combo(", i, StringComparison.Ordinal)) >= 0)
        {
            int start = i;
            i += 6;
            List<string> a = SplitArgs(src, i);
            if (a == null || a.Count != 3 || !a[1].Trim().StartsWith("new[]") || !a[2].Trim().StartsWith("new[]")) continue;
            List<string> fr = ArrayItems(a[1]), en = ArrayItems(a[2]);
            if (fr == null || en == null || fr.Count != en.Count)
            {
                dynamicCount++;
                Console.WriteLine("NON CONSTANT " + Path.GetFileName(file) + ":" + Line(src, start) + "  Combo");
                continue;
            }
            for (int k = 0; k < fr.Count; k++) found.Add(new[] { fr[k], en[k] });
        }
    }

    static List<string> ArrayItems(string text)
    {
        int b = text.IndexOf('{'), e = text.LastIndexOf('}');
        if (b < 0 || e < b) return null;
        List<string> parts = SplitTop(text.Substring(b + 1, e - b - 1));
        List<string> r = new List<string>();
        foreach (string p in parts)
        {
            if (p.Trim() == "") continue;
            string v = Literal(p);
            if (v == null) return null;
            r.Add(v);
        }
        return r;
    }

    // Arguments d'un appel dont la parenthèse ouvrante précède `pos`, séparés au premier niveau.
    static List<string> SplitArgs(string src, int pos)
    {
        int depth = 1, i = pos;
        while (i < src.Length && depth > 0)
        {
            char c = src[i];
            if (c == '"' || c == '\'') { i = SkipString(src, i); continue; }
            if (c == '@' && i + 1 < src.Length && src[i + 1] == '"') { i = SkipVerbatim(src, i + 1); continue; }
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            i++;
        }
        if (depth != 0) return null;
        return SplitTop(src.Substring(pos, i - 1 - pos));
    }

    static List<string> SplitTop(string s)
    {
        List<string> r = new List<string>();
        int depth = 0, last = 0, i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '"' || c == '\'') { i = SkipString(s, i); continue; }
            if (c == '@' && i + 1 < s.Length && s[i + 1] == '"') { i = SkipVerbatim(s, i + 1); continue; }
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            else if (c == ',' && depth == 0) { r.Add(s.Substring(last, i - last)); last = i + 1; }
            i++;
        }
        r.Add(s.Substring(last));
        return r;
    }

    static int SkipString(string s, int i)
    {
        char q = s[i++];
        while (i < s.Length && s[i] != q) { if (s[i] == '\\') i++; i++; }
        return i + 1;
    }

    static int SkipVerbatim(string s, int i)
    {
        i++;
        while (i < s.Length) { if (s[i] == '"') { if (i + 1 < s.Length && s[i + 1] == '"') i += 2; else return i + 1; } else i++; }
        return i;
    }

    // Valeur d'une expression faite uniquement de chaînes littérales jointes par +, sinon null.
    static string Literal(string expr)
    {
        StringBuilder sb = new StringBuilder();
        string s = expr.Trim();
        int i = 0;
        bool expectString = true;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (expectString)
            {
                if (c != '"') return null;
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\')
                    {
                        i++;
                        char e = s[i];
                        sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e);
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                expectString = false;
            }
            else
            {
                if (c != '+') return null;
                i++;
                expectString = true;
            }
        }
        return expectString ? null : sb.ToString();
    }

    static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n"); }
    static int Line(string src, int pos) { int n = 1; for (int i = 0; i < pos; i++) if (src[i] == '\n') n++; return n; }
    static string Short(string s) { s = s.Replace("\r", "").Replace("\n", " "); return s.Length > 140 ? s.Substring(0, 140) : s; }
}
