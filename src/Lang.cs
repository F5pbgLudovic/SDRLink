using System;
using System.Collections.Generic;

namespace SDRLink
{
    // Langue de l'interface. Les textes sont écrits en français et en anglais dans le code ; les autres langues
    // traduisent le texte anglais (LangData.cs), qui sert aussi de secours s'il manque une traduction.
    // Le journal de diagnostic reste en français.
    public static class Lang
    {
        public static readonly string[] Codes =
            { "fr", "en", "de", "es", "it", "nl", "pt", "pl", "cs", "sk", "sl", "hr", "hu", "ro", "bg", "el", "sv", "da", "nb", "fi", "et", "lv", "lt" };
        public static readonly string[] Names =
            { "Français", "English", "Deutsch", "Español", "Italiano", "Nederlands", "Português", "Polski", "Čeština", "Slovenčina",
              "Slovenščina", "Hrvatski", "Magyar", "Română", "Български", "Ελληνικά", "Svenska", "Dansk", "Norsk", "Suomi",
              "Eesti", "Latviešu", "Lietuvių" };

        static string code = "fr";
        static Dictionary<string, string> table;

        public static string Code
        {
            get { return code; }
            set
            {
                code = Array.IndexOf(Codes, value) >= 0 ? value : "fr";
                table = code == "fr" || code == "en" ? null : LangData.Load(code);
            }
        }

        // Toute langue autre que le français part du texte anglais.
        public static bool English { get { return code != "fr"; } }

        public static string T(string fr, string en)
        {
            if (code == "fr") return fr;
            string t;
            return table != null && table.TryGetValue(en, out t) ? t : en;
        }

        // Texte à trous : {0}, {1}... remplacés par les valeurs (noms, nombres).
        public static string F(string fr, string en, params object[] args)
        {
            return string.Format(T(fr, en), args);
        }
    }
}
