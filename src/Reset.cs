using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SDRLink
{
    // Réinitialisation (clic droit sur Import/Export) : totale, ou partielle par catégories. Elle est mise de côté
    // (settings.reset.txt) et faite au démarrage suivant, avant de lire les réglages : la fermeture de SDRLink, qui
    // réenregistre settings.ini, ne peut donc pas la défaire. L'ancien settings.ini est gardé dans
    // settings.avant-reinitialisation.ini. Une mise à jour de SDRLink ne passe jamais par ici.
    public static class SettingsReset
    {
        public const int Config = 0, WebSdr = 1, Mems = 2, Scan = 3, Colors = 4, Browser = 5, Count = 6;

        public static string PendingPath { get { return Path.Combine(Log.DataDir, "settings.reset.txt"); } }
        public static string BeforePath { get { return Path.Combine(Log.DataDir, "settings.avant-reinitialisation.ini"); } }

        // Catégories de l'import, plus les couleurs (boutons, REC, palette), prises à la configuration.
        public static int Category(string key)
        {
            switch (key)
            {
                case "Color": case "RecColor": case "DarkBase": case "LightBase": return Colors;
            }
            return SettingsImport.Category(key);   // Config, WebSdr, Mems ou Scan : mêmes numéros
        }

        public static bool All(bool[] reset)
        {
            for (int i = 0; i < Count; i++) if (!reset[i]) return false;
            return true;
        }

        // Lignes de settings.ini gardées : celles des catégories non réinitialisées. Favoris remis à zéro : liste de
        // départ et recherche des WebSDR du premier démarrage, comme à l'installation.
        public static string Filter(string text, bool[] reset)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                string key = raw.Substring(0, eq).Trim();
                if (reset[Category(key)]) continue;
                if (reset[WebSdr] && key == "FirstSearch") continue;
                sb.Append(raw).Append("\r\n");
            }
            if (reset[WebSdr]) sb.Append("FirstSearch=0\r\n");
            return sb.ToString();
        }

        public static void Request(bool[] reset)
        {
            string[] p = new string[Count];
            for (int i = 0; i < Count; i++) p[i] = reset[i] ? "1" : "0";
            File.WriteAllText(PendingPath, string.Join(",", p), new UTF8Encoding(false));
        }

        // Au démarrage (Settings.Load) : réinitialisation demandée avant le redémarrage.
        public static void ApplyPending()
        {
            if (!File.Exists(PendingPath)) return;
            bool[] reset = new bool[Count];
            try
            {
                string[] p = File.ReadAllText(PendingPath).Trim().Split(',');
                for (int i = 0; i < Count && i < p.Length; i++) reset[i] = p[i].Trim() == "1";
                File.Delete(PendingPath);
            }
            catch (Exception ex) { Log.Write("Réinitialisation impossible : " + ex.Message); return; }
            List<string> done = new List<string>();
            try
            {
                string ini = Settings.FilePath;
                if (File.Exists(ini))
                {
                    File.Copy(ini, BeforePath, true);
                    if (All(reset)) File.Delete(ini);
                    else File.WriteAllText(ini, Filter(File.ReadAllText(ini, Encoding.UTF8), reset), new UTF8Encoding(false));
                }
                string[] names = { "configuration", "WebSDR", "mémoires", "scan", "couleurs" };
                for (int i = 0; i < names.Length; i++) if (reset[i]) done.Add(names[i]);
            }
            catch (Exception ex) { Log.Write("Réinitialisation des réglages impossible : " + ex.Message); }
            if (reset[Browser])
            {
                if (DeleteBrowserData()) done.Add("données du navigateur");
                else
                {
                    // Encore utilisées (l'ancienne fenêtre se ferme) : nouvel essai au démarrage suivant.
                    bool[] again = new bool[Count];
                    again[Browser] = true;
                    try { Request(again); } catch { }
                    Log.Write("Données du navigateur encore utilisées : effacées au prochain démarrage");
                }
            }
            Log.Write("Réinitialisation " + (All(reset) ? "totale" : "partielle") + " : " + string.Join(", ", done.ToArray())
                + " (anciens réglages gardés dans settings.avant-reinitialisation.ini)");
        }

        // Dossier WebView2 (cache, cookies, stockage des sites) : les processus du navigateur de l'instance précédente
        // peuvent le tenir encore quelques secondes après le redémarrage.
        static bool DeleteBrowserData()
        {
            string dir = Settings.WebViewDataDir;
            for (int i = 0; i < 30; i++)
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Thread.Sleep(500);
            }
            return false;
        }
    }

    // Fenêtre « Réinitialiser le programme » : totale, ou partielle avec une case par catégorie.
    public class ResetDialog : Form
    {
        readonly RadioButton rbAll = new RadioButton(), rbPart = new RadioButton();
        readonly CheckBox[] checks = new CheckBox[SettingsReset.Count];
        readonly Button ok = new Button(), cancel = new Button();

        public bool[] Reset
        {
            get
            {
                bool[] r = new bool[SettingsReset.Count];
                for (int i = 0; i < r.Length; i++) r[i] = rbAll.Checked || checks[i].Checked;
                return r;
            }
        }

        public ResetDialog()
        {
            Text = Lang.T("Réinitialiser le programme", "Reset the program");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            rbAll.Text = Lang.T("Réinitialisation totale (perte de tous les paramètres, mémoires et couleurs)",
                                "Full reset (all settings, memories and colours are lost)");
            Label lAll = new Label
            {
                Text = Lang.T("SDRLink redevient comme à sa première installation : favoris WebSDR de départ, aucune mémoire, aucun scan, couleurs d'origine, données du navigateur effacées.",
                              "SDRLink goes back to how it was when first installed: starting WebSDR favorites, no memory, no scan, original colours, browser data deleted.")
            };
            rbPart.Text = Lang.T("Réinitialisation partielle :", "Partial reset:");

            // Ordre d'affichage ; l'indice est la catégorie de SettingsReset.
            int[] order = { SettingsReset.Mems, SettingsReset.Scan, SettingsReset.Colors, SettingsReset.WebSdr, SettingsReset.Config, SettingsReset.Browser };
            string[] texts = new string[SettingsReset.Count];
            texts[SettingsReset.Mems] = Lang.T("Mémoires de fréquences", "Frequency memories");
            texts[SettingsReset.Scan] = Lang.T("Scan (réglage, zones non scannées et profils de scan)", "Scan (setting, skipped ranges and scan profiles)");
            texts[SettingsReset.Colors] = Lang.T("Couleurs (boutons, REC, palette)", "Colours (buttons, REC, palette)");
            texts[SettingsReset.WebSdr] = Lang.T("WebSDR (favoris et listes de favoris, remis à ceux de départ)", "WebSDR (favorites and favorite lists, back to the starting ones)");
            texts[SettingsReset.Config] = Lang.T("Configuration (poste, interface, synchro, son, affichage, langue, pseudo…)", "Configuration (rig, interface, sync, audio, display, language, name…)");
            texts[SettingsReset.Browser] = Lang.T("Données du navigateur intégré (cache, cookies, réglages gardés par les sites WebSDR)", "Built-in browser data (cache, cookies, settings kept by the WebSDR sites)");
            Label lBrowser = new Label
            {
                Text = Lang.T("À essayer si les pages des WebSDR ne s'affichent plus (fenêtre vide, comme sans Internet).",
                              "Worth trying if the WebSDR pages no longer show (empty window, as if there were no Internet)."),
                ForeColor = SystemColors.GrayText
            };
            Label note = new Label
            {
                Text = Lang.F("SDRLink va ensuite redémarrer. Les réglages actuels sont gardés dans {0} (Import/Export peut les recharger). Les mises à jour de SDRLink ne réinitialisent jamais rien.",
                              "SDRLink will then restart. The current settings are kept in {0} (Import/Export can load them back). SDRLink updates never reset anything.",
                              SettingsReset.BeforePath)
            };

            // Largeur selon la langue : la plus longue des cases sur une ligne (600 à 1000 px) ; les textes suivants
            // passent à la ligne et prennent la hauteur qu'il leur faut.
            int W = TextRenderer.MeasureText(rbAll.Text, Font).Width + 12 + 30;
            foreach (string t in texts) W = Math.Max(W, TextRenderer.MeasureText(t, Font).Width + 44 + 26);   // case à x = 32, marge droite 12, case à cocher
            W = Math.Max(600, Math.Min(1000, W));
            rbAll.SetBounds(12, 12, W - 24, 24);
            int y = Place(lAll, 30, 38, W - 42) + 6;
            rbPart.SetBounds(12, y, W - 24, 24);
            y += 28;
            foreach (int c in order)
            {
                checks[c] = new CheckBox { Text = texts[c] };
                checks[c].SetBounds(32, y, W - 44, 24);
                checks[c].CheckedChanged += delegate { UpdateState(); };
                y += 26;
            }
            y = Place(lBrowser, 50, y, W - 62) + 10;
            y = Place(note, 12, y, W - 24) + 12;
            ok.Text = Lang.T("Réinitialiser", "Reset");
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(W - 214, y, 110, 30);
            cancel.Text = Lang.T("Annuler", "Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(W - 98, y, 86, 30);
            AcceptButton = cancel;   // Entrée ne réinitialise pas par mégarde
            CancelButton = cancel;
            ClientSize = new Size(W, y + 42);
            rbPart.Checked = true;
            rbAll.CheckedChanged += delegate { UpdateState(); };
            Controls.AddRange(new Control[] { rbAll, lAll, rbPart, lBrowser, note, ok, cancel });
            Controls.AddRange(checks);
            UpdateState();
            Theme.Apply(this);
        }

        // Chemin affiché avec %APPDATA% (plus court, et lisible par l'utilisateur dans l'Explorateur).
        static string ShortPath(string path)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return path.StartsWith(appData, StringComparison.OrdinalIgnoreCase) ? "%APPDATA%" + path.Substring(appData.Length) : path;
        }

        // Étiquette à la largeur donnée, haute de ce qu'il faut pour son texte ; renvoie le bas.
        int Place(Label l, int x, int y, int width)
        {
            int h = TextRenderer.MeasureText(l.Text, Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
            l.SetBounds(x, y, width, h + 2);
            return y + h + 2;
        }

        // Cases grisées en réinitialisation totale ; rien de coché : Réinitialiser grisé.
        void UpdateState()
        {
            bool any = rbAll.Checked;
            foreach (CheckBox c in checks) { c.Enabled = rbPart.Checked; any |= rbPart.Checked && c.Checked; }
            ok.Enabled = any;
        }
    }
}
