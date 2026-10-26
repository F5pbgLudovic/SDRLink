using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SDRLink
{
    // Un récepteur trouvé dans les annuaires publics.
    public class FoundSdr
    {
        public string Name, Location, Type, Url, Users, Country;
        public double Lat, Lon, DistanceKm;
    }

    // Lecture des annuaires publics : websdr.org, KiwiSDR (kiwisdr.com via rx.linkfanel.net) et ReceiverBook.
    public static class WebSdrFinder
    {
        // Centre de la France : les distances sont comptées depuis ce point.
        public const double CenterLat = 46.6, CenterLon = 2.4;

        const string WebSdrOrgUrl = "http://websdr.ewi.utwente.nl/~~websdrlistk?v=1&fmt=2&chseq=0";
        const string KiwiUrl = "http://rx.linkfanel.net/kiwisdr_com.js";
        const string ReceiverBookUrl = "https://www.receiverbook.de/map";

        // Télécharge et fusionne les trois annuaires ; une source en panne n'empêche pas les autres.
        public static List<FoundSdr> LoadAll(List<string> report)
        {
            List<FoundSdr> all = new List<FoundSdr>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            Load("websdr.org", WebSdrOrgUrl, ParseWebSdrOrg, all, seen, report);
            Load("KiwiSDR", KiwiUrl, ParseKiwi, all, seen, report);
            Load("ReceiverBook", ReceiverBookUrl, ParseReceiverBook, all, seen, report);
            foreach (FoundSdr s in all)
            {
                s.DistanceKm = Distance(CenterLat, CenterLon, s.Lat, s.Lon);
                s.Country = GuessCountry(s.Name + " " + s.Location + " " + s.Url, s.Lat, s.Lon);
            }
            all.Sort(delegate(FoundSdr a, FoundSdr b) { return a.DistanceKm.CompareTo(b.DistanceKm); });
            return all;
        }

        static void Load(string source, string url, Func<string, List<FoundSdr>> parse,
            List<FoundSdr> all, Dictionary<string, bool> seen, List<string> report)
        {
            try
            {
                List<FoundSdr> found = parse(Download(url));
                int added = 0;
                foreach (FoundSdr s in found)
                {
                    string key = UrlKey(s.Url);
                    if (key == null || seen.ContainsKey(key)) continue;
                    seen[key] = true;
                    all.Add(s);
                    added++;
                }
                report.Add(source + " : " + added);
            }
            catch (Exception ex)
            {
                string msg = ex.GetBaseException().Message;
                if (msg.Length > 100) msg = msg.Substring(0, 100) + "…";
                report.Add(source + " : " + Lang.T("indisponible", "unavailable") + " (" + msg + ")");
                Log.Write("Recherche WebSDR, source " + source + " en erreur : " + msg);
            }
        }

        static string Download(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "SDRLink/1.0 (F5PBG)";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        static object Json(string text)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js.DeserializeObject(text);
        }

        static string Str(IDictionary d, string key)
        {
            object v = d.Contains(key) ? d[key] : null;
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
        }

        static double Num(object v)
        {
            if (v == null) return double.NaN;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return double.NaN; }
        }

        // websdr.org : tableau JSON [{url, desc, qth, lat, lon, users}, ...] précédé d'un préfixe.
        static List<FoundSdr> ParseWebSdrOrg(string text)
        {
            List<FoundSdr> list = new List<FoundSdr>();
            foreach (object o in (object[])Json(text.Substring(text.IndexOf('['))))
            {
                IDictionary d = (IDictionary)o;
                FoundSdr s = new FoundSdr { Type = "websdr.org", Url = Str(d, "url"), Name = Str(d, "desc"), Location = Str(d, "qth"),
                    Users = Str(d, "users"), Lat = Num(d["lat"]), Lon = Num(d["lon"]) };
                if (!double.IsNaN(s.Lat)) list.Add(s);
            }
            return list;
        }

        // KiwiSDR : "var kiwisdr_com = [ {...}, ... ];" avec gps "(lat, lon)".
        static List<FoundSdr> ParseKiwi(string text)
        {
            List<FoundSdr> list = new List<FoundSdr>();
            int a = text.IndexOf('['), b = text.LastIndexOf(']');
            // Fichier JavaScript : virgules finales autorisées ("},\n]") mais refusées par le lecteur JSON.
            string json = System.Text.RegularExpressions.Regex.Replace(text.Substring(a, b - a + 1), ",\\s*([\\]}])", "$1");
            foreach (object o in (object[])Json(json))
            {
                IDictionary d = (IDictionary)o;
                if (Str(d, "offline") == "yes") continue;
                string[] gps = Str(d, "gps").Trim('(', ')').Split(',');
                double lat, lon;
                if (gps.Length < 2 || !double.TryParse(gps[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
                    || !double.TryParse(gps[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon)) continue;
                string users = Str(d, "users"), max = Str(d, "users_max");
                list.Add(new FoundSdr { Type = "KiwiSDR", Url = Str(d, "url"), Name = Str(d, "name"), Location = Str(d, "loc"),
                    Users = users + (max != "" ? "/" + max : ""), Lat = lat, Lon = lon });
            }
            return list;
        }

        // ReceiverBook : "var receivers = [ {label, location:{coordinates:[lon,lat]}, receivers:[{label,url,type}]} ];"
        static List<FoundSdr> ParseReceiverBook(string html)
        {
            List<FoundSdr> list = new List<FoundSdr>();
            int i = html.IndexOf("var receivers = ", StringComparison.Ordinal);
            if (i < 0) throw new Exception(Lang.T("format inattendu", "unexpected format"));
            int start = html.IndexOf('[', i), end = html.IndexOf('\n', start);
            string json = html.Substring(start, (end < 0 ? html.Length : end) - start).Trim().TrimEnd(';');
            foreach (object o in (object[])Json(json))
            {
                IDictionary site = (IDictionary)o;
                IDictionary loc = site["location"] as IDictionary;
                object[] coords = loc != null ? loc["coordinates"] as object[] : null;
                if (coords == null || coords.Length < 2) continue;
                object[] rx = site["receivers"] as object[];
                if (rx == null) continue;
                foreach (object r in rx)
                {
                    IDictionary d = (IDictionary)r;
                    list.Add(new FoundSdr { Type = Str(d, "type"), Url = Str(d, "url"), Name = Str(d, "label"), Location = Str(site, "label"),
                        Users = "", Lat = Num(coords[1]), Lon = Num(coords[0]) });
                }
            }
            return list;
        }

        // Clé de comparaison d'adresses (doublons entre annuaires) : hôte + port + chemin.
        public static string UrlKey(string url)
        {
            Uri u;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out u)) return null;
            return (u.Host + ":" + u.Port + u.AbsolutePath.TrimEnd('/')).ToLowerInvariant();
        }

        public static double Distance(double lat1, double lon1, double lat2, double lon2)
        {
            double r = Math.PI / 180, dLat = (lat2 - lat1) * r, dLon = (lon2 - lon1) * r;
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * r) * Math.Cos(lat2 * r) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 6371 * 2 * Math.Asin(Math.Min(1, Math.Sqrt(h)));
        }

        // Pays estimé d'après le texte (nom, lieu, adresse) ; "" si rien de reconnaissable.
        static readonly string[][] Countries =
        {
            new[] { "France", "france", "french", "francia", ".fr/", ".fr:", " fr ", "(fr)", ", fr" },
            new[] { "Belgique", "belgi", ".be/", ".be:", "(be)", ", be" },
            new[] { "Luxembourg", "luxemb" },
            new[] { "Suisse", "switzerland", "suisse", "schweiz", "svizzera", ".ch/", ".ch:", "(ch)", ", ch" },
            new[] { "Allemagne", "germany", "deutschland", "allemagne", ".de/", ".de:", "(de)", ", de" },
            new[] { "Italie", "italy", "italia", "italie", ".it/", ".it:", "(it)", ", it" },
            new[] { "Espagne", "spain", "españa", "espana", "espagne", ".es/", ".es:", "(es)", ", es" },
            new[] { "Andorre", "andorra" },
            new[] { "Monaco", "monaco" },
            new[] { "Royaume-Uni", "united kingdom", "england", "scotland", "wales", " uk", "(uk)", ".uk/", ".uk:", "britain" },
            new[] { "Pays-Bas", "netherlands", "nederland", "holland", ", nl", "(nl)", ".nl/", ".nl:" },
            new[] { "Autriche", "austria", "österreich", ".at/", ".at:" },
            new[] { "Portugal", "portugal", ".pt/", ".pt:" },
            new[] { "Irlande", "ireland", ".ie/", ".ie:" },
        };

        // Pays estimé : d'abord d'après le texte (nom du pays, indicatif français), sinon d'après la position
        // (contour simplifié de la France métropolitaine et de la Corse). Les autres pays restent "" si le texte ne les cite pas.
        static string GuessCountry(string text, double lat, double lon)
        {
            string t = " " + (text ?? "").ToLowerInvariant() + " ";
            foreach (string[] c in Countries)
                for (int i = 1; i < c.Length; i++)
                    if (t.Contains(c[i])) return Lang.English ? EnglishName(c[0]) : c[0];
            if (FrenchCallsign.IsMatch(text ?? "") || InFrance(lat, lon)) return Lang.English ? "France" : "France";
            return "";
        }

        // Indicatifs français : F1ABC, F4JOY, F5PBG, TK5XX (Corse)...
        static readonly System.Text.RegularExpressions.Regex FrenchCallsign =
            new System.Text.RegularExpressions.Regex("\\b(F[1-9]|TK[0-9])[A-Z]{1,3}\\b");

        // Contour simplifié de la France métropolitaine (longitude, latitude), sens des aiguilles d'une montre.
        static readonly double[,] FranceOutline =
        {
            { 2.55, 51.09 }, { 4.15, 49.98 }, { 4.85, 50.15 }, { 5.82, 49.50 }, { 6.36, 49.46 }, { 8.23, 48.96 },
            { 7.58, 47.58 }, { 6.95, 47.30 }, { 6.10, 46.40 }, { 6.80, 46.40 }, { 7.03, 45.92 }, { 6.63, 45.10 },
            { 7.00, 44.25 }, { 7.53, 43.78 }, { 6.20, 43.05 }, { 5.00, 43.30 }, { 4.00, 43.45 }, { 3.10, 42.95 },
            { 3.17, 42.43 }, { 1.70, 42.50 }, { 0.00, 42.70 }, { -1.78, 43.37 }, { -1.25, 44.50 }, { -1.20, 46.20 },
            { -2.30, 47.10 }, { -4.80, 48.40 }, { -3.00, 48.85 }, { -1.60, 48.65 }, { -1.95, 49.70 }, { -1.00, 49.40 },
            { 0.20, 49.50 }, { 1.40, 50.10 }, { 1.60, 50.95 },
        };

        static bool InFrance(double lat, double lon)
        {
            if (lat >= 41.3 && lat <= 43.05 && lon >= 8.5 && lon <= 9.6) return true;   // Corse
            bool inside = false;
            int n = FranceOutline.GetLength(0);
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = FranceOutline[i, 0], yi = FranceOutline[i, 1], xj = FranceOutline[j, 0], yj = FranceOutline[j, 1];
                if ((yi > lat) != (yj > lat) && lon < (xj - xi) * (lat - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        static string EnglishName(string fr)
        {
            switch (fr)
            {
                case "Belgique": return "Belgium";
                case "Suisse": return "Switzerland";
                case "Allemagne": return "Germany";
                case "Italie": return "Italy";
                case "Espagne": return "Spain";
                case "Andorre": return "Andorra";
                case "Royaume-Uni": return "United Kingdom";
                case "Pays-Bas": return "Netherlands";
                case "Autriche": return "Austria";
                case "Irlande": return "Ireland";
            }
            return fr;
        }
    }

    // Fenêtre de recherche : liste filtrable, cases à cocher, ajout aux favoris.
    public class WebSdrSearchDialog : Form
    {
        readonly List<Favorite> favs;
        readonly Action<string> listen;
        readonly ListView view = new ListView();
        readonly NumericUpDown numDist = new NumericUpDown();
        readonly TextBox txtFilter = new TextBox();
        readonly ComboBox cbType = new ComboBox();
        readonly CheckBox chkHideFav = new CheckBox();
        readonly Label lblStatus = new Label();
        List<FoundSdr> found = new List<FoundSdr>();
        readonly Dictionary<string, bool> checkedKeys = new Dictionary<string, bool>();
        int sortColumn = 4;
        bool sortDesc;

        public int Added;

        public WebSdrSearchDialog(List<Favorite> favorites, Action<string> listen)
        {
            favs = favorites;
            this.listen = listen;
            Text = Lang.T("Rechercher des WebSDR (France et pays voisins)", "Find WebSDRs (France and neighbouring countries)");
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(1060, 620);
            MinimumSize = new Size(760, 400);

            FlowLayoutPanel top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 6, 6, 2) };
            numDist.Minimum = 50; numDist.Maximum = 20000; numDist.Increment = 100; numDist.Value = 1000; numDist.Width = 70;
            txtFilter.Width = 160;
            cbType.DropDownStyle = ComboBoxStyle.DropDownList;
            cbType.Width = 120;
            cbType.Items.AddRange(new object[] { Lang.T("Tous", "All"), "websdr.org", "KiwiSDR", "OpenWebRX", "WebSDR" });
            cbType.SelectedIndex = 0;
            chkHideFav.Text = Lang.T("Masquer ceux déjà en favoris", "Hide those already in favorites");
            chkHideFav.AutoSize = true;
            chkHideFav.Checked = true;
            top.Controls.Add(L(Lang.T("Distance max du centre de la France (km) :", "Max distance from the centre of France (km):")));
            top.Controls.Add(numDist);
            top.Controls.Add(L(Lang.T("Filtre :", "Filter:")));
            top.Controls.Add(txtFilter);
            top.Controls.Add(L(Lang.T("Type :", "Type:")));
            top.Controls.Add(cbType);
            top.Controls.Add(chkHideFav);
            numDist.ValueChanged += delegate { Fill(); };
            txtFilter.TextChanged += delegate { Fill(); };
            cbType.SelectedIndexChanged += delegate { Fill(); };
            chkHideFav.CheckedChanged += delegate { Fill(); };

            view.Dock = DockStyle.Fill;
            view.View = View.Details;
            view.CheckBoxes = true;
            view.FullRowSelect = true;
            view.HideSelection = false;
            view.Columns.Add(Lang.T("Nom", "Name"), 330);
            view.Columns.Add(Lang.T("Lieu", "Location"), 190);
            view.Columns.Add(Lang.T("Pays", "Country"), 95);
            view.Columns.Add("Type", 85);
            view.Columns.Add("km", 55, HorizontalAlignment.Right);
            view.Columns.Add(Lang.T("Util.", "Users"), 55, HorizontalAlignment.Right);
            view.Columns.Add(Lang.T("Adresse", "Address"), 230);
            view.ColumnClick += delegate(object s, ColumnClickEventArgs e)
            {
                sortDesc = e.Column == sortColumn && !sortDesc;
                sortColumn = e.Column;
                Fill();
            };
            view.ItemChecked += delegate(object s, ItemCheckedEventArgs e)
            {
                string key = WebSdrFinder.UrlKey(((FoundSdr)e.Item.Tag).Url);
                if (e.Item.Checked) checkedKeys[key] = true; else checkedKeys.Remove(key);
                UpdateStatus();
            };
            view.DoubleClick += delegate { Listen(); };

            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(6, 4, 6, 6) };
            bottom.Controls.Add(B(Lang.T("Écouter la sélection", "Listen to selection"), delegate { Listen(); }));
            bottom.Controls.Add(B(Lang.T("Tout cocher", "Check all"), delegate { SetAll(true); }));
            bottom.Controls.Add(B(Lang.T("Tout décocher", "Uncheck all"), delegate { SetAll(false); }));
            Button add = B(Lang.T("Ajouter les cochés aux favoris", "Add checked to favorites"), delegate { AddChecked(); });
            add.Font = new Font(Font, FontStyle.Bold);
            bottom.Controls.Add(add);
            bottom.Controls.Add(B(Lang.T("Actualiser", "Refresh"), delegate { StartLoading(); }));
            bottom.Controls.Add(B(Lang.T("Fermer", "Close"), delegate { Close(); }));
            lblStatus.AutoSize = true;
            lblStatus.Margin = new Padding(12, 8, 0, 0);
            bottom.Controls.Add(lblStatus);

            Controls.Add(view);
            Controls.Add(top);
            Controls.Add(bottom);
            Theme.Apply(this);
            Shown += delegate { StartLoading(); };
        }

        static Label L(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(8, 6, 2, 0) };
        }

        static Button B(string text, EventHandler click)
        {
            Button b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(80, 28) };
            b.Click += click;
            return b;
        }

        // Téléchargement en arrière-plan pour ne pas figer la fenêtre.
        void StartLoading()
        {
            lblStatus.Text = Lang.T("Recherche dans les annuaires (websdr.org, KiwiSDR, ReceiverBook)…", "Searching directories (websdr.org, KiwiSDR, ReceiverBook)…");
            view.Items.Clear();
            Cursor = Cursors.AppStarting;
            Thread t = new Thread(delegate()
            {
                List<string> report = new List<string>();
                List<FoundSdr> list = WebSdrFinder.LoadAll(report);
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        Cursor = Cursors.Default;
                        found = list;
                        sourcesReport = string.Join(", ", report.ToArray());
                        Log.Write("Recherche WebSDR : " + sourcesReport);
                        Fill();
                    });
                }
                catch (InvalidOperationException) { }   // fenêtre fermée pendant le chargement
            });
            t.IsBackground = true;
            t.Start();
        }

        string sourcesReport = "";

        bool IsFavorite(FoundSdr s)
        {
            return favs.Exists(delegate(Favorite f) { return f.Matches(s.Url); });
        }

        // Applique les filtres et le tri, sans perdre les cases déjà cochées.
        void Fill()
        {
            string filter = txtFilter.Text.Trim().ToLowerInvariant();
            string type = cbType.SelectedIndex > 0 ? (string)cbType.SelectedItem : null;
            List<FoundSdr> shown = found.FindAll(delegate(FoundSdr s)
            {
                if (s.DistanceKm > (double)numDist.Value) return false;
                if (type != null && !string.Equals(s.Type, type, StringComparison.OrdinalIgnoreCase)) return false;
                if (chkHideFav.Checked && IsFavorite(s)) return false;
                return filter == "" || (s.Name + " " + s.Location + " " + s.Country + " " + s.Url).ToLowerInvariant().Contains(filter);
            });
            shown.Sort(delegate(FoundSdr a, FoundSdr b)
            {
                int c;
                switch (sortColumn)
                {
                    case 4: c = a.DistanceKm.CompareTo(b.DistanceKm); break;
                    case 5: c = UserCount(a).CompareTo(UserCount(b)); break;
                    default: c = string.Compare(Column(a, sortColumn), Column(b, sortColumn), StringComparison.OrdinalIgnoreCase); break;
                }
                return sortDesc ? -c : c;
            });

            view.BeginUpdate();
            view.Items.Clear();
            foreach (FoundSdr s in shown)
            {
                ListViewItem it = new ListViewItem(new[] { s.Name, s.Location, s.Country, s.Type,
                    Math.Round(s.DistanceKm).ToString(CultureInfo.InvariantCulture), s.Users, s.Url });
                it.Tag = s;
                it.Checked = checkedKeys.ContainsKey(WebSdrFinder.UrlKey(s.Url));
                view.Items.Add(it);
            }
            view.EndUpdate();
            UpdateStatus();
        }

        static string Column(FoundSdr s, int col)
        {
            switch (col)
            {
                case 0: return s.Name;
                case 1: return s.Location;
                case 2: return s.Country;
                case 3: return s.Type;
                default: return s.Url;
            }
        }

        static int UserCount(FoundSdr s)
        {
            int n;
            return int.TryParse((s.Users ?? "").Split('/')[0], out n) ? n : -1;
        }

        void UpdateStatus()
        {
            if (found.Count == 0 && sourcesReport == "") return;
            lblStatus.Text = Lang.T(view.Items.Count + " récepteur(s) affiché(s), " + checkedKeys.Count + " coché(s)   —   sources : ",
                                    view.Items.Count + " receiver(s) shown, " + checkedKeys.Count + " checked   —   sources: ") + sourcesReport;
        }

        void SetAll(bool on)
        {
            view.BeginUpdate();
            foreach (ListViewItem it in view.Items) it.Checked = on;
            view.EndUpdate();
        }

        void Listen()
        {
            if (view.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, Lang.T("Sélectionnez d'abord un récepteur dans la liste.", "Please select a receiver in the list first."), "SDRLink");
                return;
            }
            listen(((FoundSdr)view.SelectedItems[0].Tag).Url);
        }

        void AddChecked()
        {
            int n = 0;
            foreach (FoundSdr s in found)
            {
                if (!checkedKeys.ContainsKey(WebSdrFinder.UrlKey(s.Url)) || IsFavorite(s)) continue;
                string name = s.Name.Length > 90 ? s.Name.Substring(0, 90).Trim() + "…" : s.Name;
                if (s.Country != "" && name.IndexOf(s.Country, StringComparison.OrdinalIgnoreCase) < 0) name += " (" + s.Country + ")";
                favs.Add(new Favorite(name, s.Url));
                n++;
            }
            Added += n;
            checkedKeys.Clear();
            Fill();
            MessageBox.Show(this, Lang.T(n + " favori(s) ajouté(s). Vous pouvez les renommer dans « Gérer les favoris… ».",
                                         n + " favorite(s) added. You can rename them in \"Manage favorites…\"."), "SDRLink");
        }
    }
}
