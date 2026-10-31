using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace SDRLink
{
    // Import à cases : les lignes d'un fichier de réglages sont rangées en quatre catégories (configuration, WebSDR,
    // mémoires, scan). Chaque catégorie cochée vient du fichier importé, les autres restent celles du moment.
    // Favoris, mémoires et scan importés remplacent les actuels, ou s'y ajoutent sans doublons.
    public static class SettingsImport
    {
        public const int Config = 0, WebSdr = 1, Mems = 2, Scan = 3, Count = 4;

        static bool Numbered(string key, string prefix)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.Length == prefix.Length) return false;
            for (int i = prefix.Length; i < key.Length; i++) if (!char.IsDigit(key[i])) return false;
            return true;
        }

        public static int Category(string key)
        {
            switch (key)
            {
                case "Fav": case "ListCount": case "CurrentList": case "LastUrl": case "Volume": return WebSdr;
                case "Mem": case "MemRecent": case "MemListCount": case "MemCurrentList": return Mems;
            }
            if (Numbered(key, "MemList")) return Mems;
            if (Numbered(key, "List")) return WebSdr;
            if (key.StartsWith("Scan", StringComparison.Ordinal)) return Scan;
            return Config;
        }

        // Lignes clé=valeur (les autres, comme l'en-tête « # SDRLink », sont ignorées).
        static List<KeyValuePair<string, string>> Lines(string text)
        {
            List<KeyValuePair<string, string>> r = new List<KeyValuePair<string, string>>();
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                int eq = raw.IndexOf('=');
                if (eq > 0) r.Add(new KeyValuePair<string, string>(raw.Substring(0, eq).Trim(), raw.Substring(eq + 1).Trim()));
            }
            return r;
        }

        // Catégories présentes dans un fichier (la configuration l'est dès qu'il y a « Interface= »).
        public static bool[] Present(string text)
        {
            bool[] p = new bool[Count];
            foreach (KeyValuePair<string, string> l in Lines(text))
            {
                int c = Category(l.Key);
                if (c == Config ? l.Key == "Interface" : true) p[c] = true;
            }
            return p;
        }

        static List<KeyValuePair<string, string>> Of(List<KeyValuePair<string, string>> lines, int cat)
        {
            return lines.FindAll(l => Category(l.Key) == cat);
        }

        static int IntOf(List<KeyValuePair<string, string>> lines, string key, int def)
        {
            int r = def;
            foreach (KeyValuePair<string, string> l in lines)
                if (l.Key == key) int.TryParse(l.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out r);
            return r;
        }

        // take[c] : catégorie prise dans le fichier importé ; replace : favoris, mémoires et scan importés remplacent
        // les actuels (sinon ils s'y ajoutent). report reçoit ce qui a été fait, pour le journal.
        public static string Merge(string current, string imported, bool[] take, bool replace, List<string> report)
        {
            List<KeyValuePair<string, string>> cur = Lines(current), imp = Lines(imported), outp = new List<KeyValuePair<string, string>>();
            for (int c = 0; c < Count; c++)
            {
                if (!take[c]) outp.AddRange(Of(cur, c));
                else if (c == Config || replace) { outp.AddRange(Of(imp, c)); report.Add(Name(c) + " : remplacé"); }
                else if (c == WebSdr) outp.AddRange(AddFavorites(Of(cur, c), Of(imp, c), report));
                else if (c == Mems) outp.AddRange(AddMemories(Of(cur, c), Of(imp, c), report));
                else outp.AddRange(AddProfiles(Of(cur, c), Of(imp, c), report));
            }
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> l in outp) sb.Append(l.Key).Append('=').Append(l.Value).Append("\r\n");
            return sb.ToString();
        }

        static string Name(int c)
        {
            return c == Config ? "configuration" : c == WebSdr ? "WebSDR" : c == Mems ? "mémoires" : "scan";
        }

        static string Url(Favorite f) { return (f.Url ?? "").Trim().TrimEnd('/').ToLowerInvariant(); }

        // Favoris du fichier ajoutés aux actuels (même adresse dans la même liste = doublon). Les listes en plus
        // du fichier sont créées avec leur nom ; les listes déjà là gardent le leur.
        static List<KeyValuePair<string, string>> AddFavorites(List<KeyValuePair<string, string>> cur, List<KeyValuePair<string, string>> imp, List<string> report)
        {
            int curCount = IntOf(cur, "ListCount", 4), impCount = IntOf(imp, "ListCount", 4);
            List<KeyValuePair<string, string>> r = cur.FindAll(l => l.Key != "ListCount");
            r.Add(new KeyValuePair<string, string>("ListCount", Math.Max(curCount, impCount).ToString(CultureInfo.InvariantCulture)));
            foreach (KeyValuePair<string, string> l in imp)
            {
                int n;
                if (Numbered(l.Key, "List") && int.TryParse(l.Key.Substring(4), out n) && n > curCount) r.Add(l);
            }
            List<Favorite> have = new List<Favorite>();
            foreach (KeyValuePair<string, string> l in cur) if (l.Key == "Fav") have.Add(Favorite.FromSetting(l.Value));
            int added = 0;
            foreach (KeyValuePair<string, string> l in imp)
            {
                if (l.Key != "Fav") continue;
                Favorite f = Favorite.FromSetting(l.Value);
                if (have.Exists(h => h.List == f.List && Url(h) == Url(f))) continue;
                have.Add(f);
                r.Add(l);
                added++;
            }
            report.Add("WebSDR : " + added + " favori(s) ajouté(s)");
            return r;
        }

        // Mémoires du fichier ajoutées aux actuelles (même nom, fréquence et mode = doublon), listes comme les favoris.
        static List<KeyValuePair<string, string>> AddMemories(List<KeyValuePair<string, string>> cur, List<KeyValuePair<string, string>> imp, List<string> report)
        {
            int curCount = IntOf(cur, "MemListCount", 1), impCount = IntOf(imp, "MemListCount", 1);
            List<KeyValuePair<string, string>> r = cur.FindAll(l => l.Key != "MemListCount");
            r.Add(new KeyValuePair<string, string>("MemListCount", Math.Max(curCount, impCount).ToString(CultureInfo.InvariantCulture)));
            foreach (KeyValuePair<string, string> l in imp)
            {
                int n;
                if (Numbered(l.Key, "MemList") && int.TryParse(l.Key.Substring(7), out n) && n > curCount) r.Add(l);
            }
            List<Memory> have = new List<Memory>();
            foreach (KeyValuePair<string, string> l in cur)
            {
                if (l.Key != "Mem") continue;
                Memory m = Memory.FromSetting(l.Value);
                if (m != null) have.Add(m);
            }
            int added = 0;
            foreach (KeyValuePair<string, string> l in imp)
            {
                if (l.Key != "Mem") continue;
                Memory m = Memory.FromSetting(l.Value);
                if (m == null || have.Exists(h => h.Same(m))) continue;
                have.Add(m);
                r.Add(l);
                added++;
            }
            report.Add("mémoires : " + added + " ajoutée(s)");
            return r;
        }

        // Profils de scan du fichier ajoutés aux actuels (10 au plus) ; un profil identique est ignoré, un autre profil
        // du même nom est renommé « nom (2) ». Le réglage de scan du moment ne change pas.
        static List<KeyValuePair<string, string>> AddProfiles(List<KeyValuePair<string, string>> cur, List<KeyValuePair<string, string>> imp, List<string> report)
        {
            List<KeyValuePair<string, string>> r = cur.FindAll(l => l.Key != "ScanProfile");
            List<ScanProfile> have = new List<ScanProfile>();
            foreach (KeyValuePair<string, string> l in cur)
            {
                if (l.Key != "ScanProfile") continue;
                ScanProfile p = ScanProfile.FromSetting(l.Value);
                if (p != null) have.Add(p);
            }
            int added = 0, full = 0;
            foreach (KeyValuePair<string, string> l in imp)
            {
                if (l.Key != "ScanProfile") continue;
                ScanProfile p = ScanProfile.FromSetting(l.Value);
                if (p == null || have.Exists(h => h.ToSetting() == p.ToSetting())) continue;
                if (have.Count >= ScanProfile.MaxProfiles) { full++; continue; }
                string baseName = p.Name;
                for (int n = 2; have.Exists(h => string.Equals(h.Name, p.Name, StringComparison.CurrentCultureIgnoreCase)); n++)
                    p.Name = baseName + " (" + n + ")";
                have.Add(p);
                added++;
            }
            foreach (ScanProfile p in have) r.Add(new KeyValuePair<string, string>("ScanProfile", p.ToSetting()));
            report.Add("scan : " + added + " profil(s) ajouté(s)" + (full > 0 ? ", " + full + " de trop (10 au plus)" : ""));
            return r;
        }
    }

    // Fenêtre posée au moment d'importer : ce qu'il faut prendre dans le fichier (cases), puis effacer ou ajouter.
    public class ImportDialog : Form
    {
        readonly CheckBox[] checks = new CheckBox[SettingsImport.Count];
        readonly RadioButton rbAdd = new RadioButton(), rbReplace = new RadioButton();
        readonly Label lblMode = new Label();
        readonly Button ok = new Button(), cancel = new Button();

        public bool[] Take
        {
            get { bool[] t = new bool[SettingsImport.Count]; for (int i = 0; i < t.Length; i++) t[i] = checks[i].Checked; return t; }
        }
        public bool Replace { get { return rbReplace.Checked; } }

        public ImportDialog(string file, bool[] present)
        {
            Text = Lang.T("Importer des réglages", "Import settings");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(520, 380);

            Label lFile = new Label { Text = System.IO.Path.GetFileName(file), Location = new Point(12, 10), Size = new Size(496, 20), AutoEllipsis = true };
            Label lWhat = new Label { Text = Lang.T("Que faut-il importer de ce fichier ?", "What should be imported from this file?"), Location = new Point(12, 36), AutoSize = true };
            string[] texts =
            {
                Lang.T("Configuration (poste, interface, affichage, couleurs, langue…)", "Configuration (rig, interface, display, colours, language…)"),
                Lang.T("WebSDR (favoris et listes de favoris)", "WebSDR (favorites and favorite lists)"),
                Lang.T("Mémoires de fréquences", "Frequency memories"),
                Lang.T("Scan (réglage, zones non scannées et profils de scan)", "Scan (setting, skipped ranges and scan profiles)"),
            };
            for (int i = 0; i < checks.Length; i++)
            {
                checks[i] = new CheckBox { Text = texts[i] + (present[i] ? "" : Lang.T(" — absent du fichier", " — not in the file")), Enabled = present[i], Checked = present[i] };
                checks[i].SetBounds(24, 62 + i * 28, 484, 24);
                checks[i].CheckedChanged += delegate { UpdateState(); };
            }

            lblMode.Text = Lang.T("Favoris, mémoires et profils de scan déjà dans SDRLink :", "Favorites, memories and scan profiles already in SDRLink:");
            lblMode.Location = new Point(12, 184);
            lblMode.AutoSize = true;
            rbAdd.Text = Lang.T("les garder et y ajouter ceux du fichier (sans doublons)", "keep them and add those of the file (no duplicates)");
            rbReplace.Text = Lang.T("les effacer et les remplacer par ceux du fichier", "delete them and replace them with those of the file");
            rbAdd.SetBounds(24, 208, 484, 24);
            rbReplace.SetBounds(24, 234, 484, 24);
            rbAdd.Checked = true;

            Label note = new Label
            {
                Text = Lang.T("SDRLink va ensuite redémarrer. Les réglages actuels sont gardés dans settings.avant-import.ini.",
                              "SDRLink will then restart. The current settings are kept in settings.avant-import.ini."),
                Location = new Point(12, 272), Size = new Size(496, 40)
            };
            ok.Text = Lang.T("Importer", "Import");
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(332, 336, 86, 30);
            cancel.Text = Lang.T("Annuler", "Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(424, 336, 86, 30);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { lFile, lWhat, lblMode, rbAdd, rbReplace, note, ok, cancel });
            Controls.AddRange(checks);
            UpdateState();
            Theme.Apply(this);
        }

        // Le choix effacer / ajouter ne sert que pour WebSDR, mémoires et scan ; rien de coché : Importer grisé.
        void UpdateState()
        {
            bool any = false, lists = false;
            for (int i = 0; i < checks.Length; i++)
            {
                any |= checks[i].Checked;
                if (i != SettingsImport.Config) lists |= checks[i].Checked;
            }
            lblMode.Enabled = rbAdd.Enabled = rbReplace.Enabled = lists;
            ok.Enabled = any;
        }
    }
}
