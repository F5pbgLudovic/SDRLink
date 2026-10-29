using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SDRLink
{
    public class Favorite
    {
        public string Name;
        public string Url;
        public int Volume = -1;   // volume mémorisé pour ce WebSDR, en %, -1 = jamais réglé
        public int List;          // liste de favoris (0 à 3)

        public const int MaxLists = 10;
        public static int ListCount = 4;   // listes utilisées (1 à MaxLists), lu et enregistré avec les réglages

        public Favorite(string name, string url)
        {
            Url = (url ?? "").Trim();
            Name = string.IsNullOrEmpty(name) ? HostOf(Url) : name.Trim();
        }

        // Même WebSDR si hôte, port et chemin sont identiques (sans tenir compte de ?tune=... ni de la casse).
        public bool Matches(string url)
        {
            string k = WebSdrFinder.UrlKey(url);
            return k != null && k == WebSdrFinder.UrlKey(Url);
        }

        public override string ToString() { return Name; }

        public static string HostOf(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host + (u.IsDefaultPort ? "" : ":" + u.Port) : url;
        }

        // Ligne du fichier de réglages : "Nom|URL|volume|liste" (volume -1 = jamais réglé).
        // Anciens formats acceptés : "Nom|URL|volume", "Nom|URL", ou URL seule.
        public static Favorite FromSetting(string v)
        {
            string[] p = v.Split('|');
            int vol, list;
            if (p.Length >= 4 && int.TryParse(p[p.Length - 2], out vol) && int.TryParse(p[p.Length - 1], out list))
                return new Favorite(string.Join("|", p, 0, p.Length - 3), p[p.Length - 3])
                    { Volume = vol < 0 ? -1 : Math.Min(100, vol), List = Math.Max(0, Math.Min(MaxLists - 1, list)) };
            if (p.Length >= 3 && int.TryParse(p[p.Length - 1], out vol))
                return new Favorite(string.Join("|", p, 0, p.Length - 2), p[p.Length - 2]) { Volume = Math.Max(0, Math.Min(100, vol)) };
            int bar = v.LastIndexOf('|');
            return bar > 0 ? new Favorite(v.Substring(0, bar), v.Substring(bar + 1)) : new Favorite(null, v);
        }

        public string ToSetting()
        {
            return Name.Replace("|", "/") + "|" + Url + "|" + Volume + "|" + List;
        }

        static bool LooksLikeUrl(string s)
        {
            s = s.Trim();
            return s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        // Numéro de la liste portant ce nom (nom donné par l'utilisateur, ou "Liste 2" / "List 2" / nom traduit), -1 sinon.
        static int ListIndexOf(string name, string[] listNames)
        {
            name = (name ?? "").Trim();
            for (int i = 0; i < ListCount; i++)
                if (string.Equals(name, listNames[i], StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Liste " + (i + 1), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "List " + (i + 1), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, FavoritesDialog.ListLabel(listNames, i), StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        static bool IsDefaultListName(string[] listNames, int i) { return listNames[i] == "Liste " + (i + 1); }

        // Import : favoris HTML d'un navigateur (Firefox, Edge, Chrome : <H3>dossier</H3><DL>… <A HREF="...">nom</A>),
        // ou lignes "nom;URL;volume;liste", "nom<tab>URL", "nom|URL", "URL". Chaque favori reçoit sa liste :
        // - dossier « SDRLink » (export de SDRLink, repris tel quel ou après un passage par Firefox) : seul son contenu
        //   est importé, chaque sous-dossier allant dans la liste de même nom, sinon dans la liste de même rang ;
        //   une liste jamais renommée prend alors le nom du sous-dossier ;
        // - autre fichier : le dossier qui porte le nom d'une liste y va, le reste dans defaultList.
        public static List<Favorite> Import(string path, string[] listNames, int defaultList)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            List<Favorite> list = new List<Favorite>();
            List<List<string>> paths = new List<List<string>>();
            List<string> stack = new List<string>();
            List<string> subfolders = new List<string>();   // sous-dossiers du dossier SDRLink, dans l'ordre du fichier (vides compris)
            string pending = null;
            bool html = false;
            foreach (Match m in Regex.Matches(text, "<DL\\b[^>]*>|</DL\\s*>|<H3\\b[^>]*>(.*?)</H3\\s*>|<A\\s([^>]*)>(.*?)</A\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string tag = m.Value.Substring(0, Math.Min(3, m.Value.Length)).ToUpperInvariant();
                if (tag == "<DL")
                {
                    string folder = pending ?? "";
                    if (stack.Count > 0 && IsSdrLinkFolder(stack[stack.Count - 1]) && folder.Length > 0 && !subfolders.Contains(folder)) subfolders.Add(folder);
                    stack.Add(folder);
                    pending = null;
                }
                else if (tag == "</D") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
                else if (tag == "<H3") pending = WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]*>", "")).Trim();
                else
                {
                    html = true;
                    Match href = Regex.Match(m.Groups[2].Value, "HREF\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                    if (!href.Success) continue;
                    string url = WebUtility.HtmlDecode(href.Groups[1].Value);
                    if (!LooksLikeUrl(url)) continue;
                    Favorite f = new Favorite(WebUtility.HtmlDecode(Regex.Replace(m.Groups[3].Value, "<[^>]*>", "")).Trim(), url);
                    Match vol = Regex.Match(m.Groups[2].Value, "SDRLINK_VOLUME\\s*=\\s*\"(\\d+)\"", RegexOptions.IgnoreCase);
                    if (vol.Success) f.Volume = Math.Min(100, int.Parse(vol.Groups[1].Value));
                    list.Add(f);
                    paths.Add(new List<string>(stack));
                }
            }
            if (html)
            {
                bool sdrlink = paths.Exists(delegate(List<string> fp) { return fp.Exists(IsSdrLinkFolder); });
                // Chaque sous-dossier du dossier SDRLink devient une liste (créée si besoin, jusqu'à MaxLists).
                Dictionary<string, int> folderList = new Dictionary<string, int>();
                if (sdrlink)
                    for (int k = 0; k < subfolders.Count; k++) folderList[subfolders[k]] = ListForName(subfolders[k], k + 1, listNames);
                List<Favorite> kept = new List<Favorite>();
                for (int i = 0; i < list.Count; i++)
                {
                    List<string> fp = paths[i];
                    int root = fp.FindLastIndex(IsSdrLinkFolder);
                    if (sdrlink)
                    {
                        if (root < 0) continue;   // hors du dossier SDRLink (autres marque-pages du navigateur)
                        if (root + 1 >= fp.Count) { list[i].List = defaultList; kept.Add(list[i]); continue; }
                        int idx;
                        list[i].List = folderList.TryGetValue(fp[root + 1], out idx) && idx >= 0 ? idx : defaultList;
                    }
                    else
                    {
                        int idx = -1;
                        for (int j = fp.Count - 1; j >= 0 && idx < 0; j--) idx = ListIndexOf(fp[j], listNames);
                        list[i].List = idx >= 0 ? idx : defaultList;
                    }
                    kept.Add(list[i]);
                }
                return kept;
            }
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string[] p = line.Split(new[] { ';', '\t', '|' });
                int vol;
                Favorite f = null;
                if (p.Length >= 2 && LooksLikeUrl(p[1]))
                {
                    f = new Favorite(p[0].Trim('"', ' '), p[1].Trim('"', ' '));
                    if (p.Length >= 3 && int.TryParse(p[2].Trim('"', ' '), out vol)) f.Volume = Math.Max(0, Math.Min(100, vol));
                }
                else if (LooksLikeUrl(p[0])) f = new Favorite(null, p[0].Trim('"', ' '));
                if (f == null) continue;
                // 4e colonne : nom de la liste ; 5e : son numéro (depuis la 1.8).
                int number;
                if (p.Length < 5 || !int.TryParse(p[4].Trim('"', ' '), out number)) number = 0;
                int idx = p.Length >= 4 ? ListForName(p[3].Trim('"', ' '), number, listNames) : -1;
                f.List = idx >= 0 ? idx : defaultList;
                list.Add(f);
            }
            return list;
        }

        // Liste d'un dossier ou d'une colonne du fichier importé : celle qui porte ce nom ; sinon celle de ce rang
        // (1 à MaxLists, créée si besoin ; une liste jamais renommée prend alors ce nom) ; sans rang, une nouvelle liste
        // à ce nom s'il reste de la place. -1 si aucune.
        static int ListForName(string name, int number, string[] listNames)
        {
            name = (name ?? "").Trim();
            int idx = ListIndexOf(name, listNames);
            if (idx >= 0 || name.Length == 0) return idx;
            Match def = Regex.Match(name, "^Lis(?:te|t) (\\d+)$", RegexOptions.IgnoreCase);   // "Liste 7" : 7e liste
            if (def.Success) number = int.Parse(def.Groups[1].Value);
            if (number < 1) { if (ListCount >= MaxLists) return -1; number = ListCount + 1; }
            if (number > MaxLists) return -1;
            idx = number - 1;
            while (ListCount <= idx) { listNames[ListCount] = "Liste " + (ListCount + 1); ListCount++; }
            if (IsDefaultListName(listNames, idx)) listNames[idx] = name.Replace("|", "/");
            return idx;
        }

        static bool IsSdrLinkFolder(string name) { return string.Equals(name, "SDRLink", StringComparison.OrdinalIgnoreCase); }

        // Export de tous les favoris. .html : marque-pages au format Netscape de Firefox (importable aussi dans Edge
        // et Chrome), dossier « SDRLink » avec un sous-dossier par liste, même vide (l'import s'appuie sur leur rang) ; le volume est gardé dans SDRLINK_VOLUME
        // (ignoré par les navigateurs). Sinon CSV "nom;URL;volume;liste;numéro de liste".
        public static void Export(string path, IList<Favorite> favs, string[] listNames)
        {
            StringBuilder sb = new StringBuilder();
            if (path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                string now = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                sb.Append("<!DOCTYPE NETSCAPE-Bookmark-file-1>\r\n<!-- This is an automatically generated file.\r\n     It will be read and overwritten.\r\n     DO NOT EDIT! -->\r\n");
                sb.Append("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\r\n");
                sb.Append("<TITLE>Bookmarks</TITLE>\r\n<H1>Bookmarks Menu</H1>\r\n\r\n<DL><p>\r\n");
                sb.Append("    <DT><H3 ADD_DATE=\"").Append(now).Append("\" LAST_MODIFIED=\"").Append(now).Append("\">SDRLink</H3>\r\n    <DL><p>\r\n");
                for (int l = 0; l < ListCount; l++)
                {
                    sb.Append("        <DT><H3 ADD_DATE=\"").Append(now).Append("\" LAST_MODIFIED=\"").Append(now).Append("\">")
                      .Append(WebUtility.HtmlEncode(FavoritesDialog.ListLabel(listNames, l))).Append("</H3>\r\n        <DL><p>\r\n");
                    foreach (Favorite f in favs)
                    {
                        if (f.List != l) continue;
                        sb.Append("            <DT><A HREF=\"").Append(WebUtility.HtmlEncode(f.Url)).Append("\" ADD_DATE=\"").Append(now).Append('"');
                        if (f.Volume >= 0) sb.Append(" SDRLINK_VOLUME=\"").Append(f.Volume).Append('"');
                        sb.Append('>').Append(WebUtility.HtmlEncode(f.Name)).Append("</A>\r\n");
                    }
                    sb.Append("        </DL><p>\r\n");
                }
                sb.Append("    </DL><p>\r\n</DL>\r\n");
            }
            else
            {
                foreach (Favorite f in favs)
                    sb.Append(f.Name.Replace(";", ",")).Append(';').Append(f.Url).Append(';').Append(f.Volume >= 0 ? f.Volume.ToString() : "")
                      .Append(';').Append(FavoritesDialog.ListLabel(listNames, f.List).Replace(";", ",")).Append(';').Append(f.List + 1).Append("\r\n");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }

    // Saisie nom + adresse (ajout, renommage).
    public class FavoriteEditDialog : Form
    {
        readonly TextBox txtName = new TextBox(), txtUrl = new TextBox();

        public string FavName { get { return txtName.Text.Trim(); } }
        public string FavUrl { get { return txtUrl.Text.Trim(); } }
        Label addressLabel;

        // Saisie d'un nom seul (renommage d'une liste de favoris).
        public void HideAddress()
        {
            addressLabel.Visible = txtUrl.Visible = false;
            txtUrl.Text = "-";
        }

        public FavoriteEditDialog(string title, string name, string url) : this(title, name, url, null, 0, null) { }

        readonly ComboBox cbList = new ComboBox();

        // Liste de favoris choisie (seulement si la fenêtre propose le choix de la liste).
        public int FavList { get { return Math.Max(0, cbList.SelectedIndex); } }

        // Avec listNames : choix de la liste où enregistrer le favori. existsIn(adresse, liste) refuse
        // une adresse déjà présente dans la liste choisie, la fenêtre restant ouverte.
        public FavoriteEditDialog(string title, string name, string url, string[] listNames, int list, Func<string, int, bool> existsIn)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            int extra = listNames != null ? 34 : 0;
            ClientSize = new Size(480, 128 + extra);

            Label l1 = new Label { Text = Lang.T("Nom :", "Name:"), Location = new Point(12, 16), AutoSize = true };
            Label l2 = new Label { Text = Lang.T("Adresse :", "Address:"), Location = new Point(12, 50), AutoSize = true };
            txtName.SetBounds(90, 12, 375, 23);
            txtUrl.SetBounds(90, 46, 375, 23);
            txtName.Text = name;
            txtUrl.Text = url;
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = Lang.T("Annuler", "Cancel"), DialogResult = DialogResult.Cancel };
            ok.SetBounds(300, 88 + extra, 80, 28);
            cancel.SetBounds(386, 88 + extra, 80, 28);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { l1, l2, txtName, txtUrl, ok, cancel });
            if (listNames != null)
            {
                Label l3 = new Label { Text = Lang.T("Liste :", "List:"), Location = new Point(12, 84), AutoSize = true };
                cbList.DropDownStyle = ComboBoxStyle.DropDownList;
                cbList.SetBounds(90, 80, 200, 23);
                for (int i = 0; i < Favorite.ListCount; i++) cbList.Items.Add(FavoritesDialog.ListLabel(listNames, i));
                cbList.SelectedIndex = Math.Max(0, Math.Min(Favorite.ListCount - 1, list));
                Controls.AddRange(new Control[] { l3, cbList });
            }
            addressLabel = l2;
            Theme.Apply(this);
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (DialogResult != DialogResult.OK) return;
                if (FavName == "" || FavUrl == "")
                {
                    MessageBox.Show(this, Lang.T("Indiquez un nom et une adresse.", "Please enter a name and an address."), "SDRLink");
                    e.Cancel = true;
                }
                else if (existsIn != null && existsIn(FavUrl, FavList))
                {
                    MessageBox.Show(this, Lang.F("Ce WebSDR est déjà dans « {0} ».", "This WebSDR is already in \"{0}\".", cbList.Text), "SDRLink");
                    e.Cancel = true;
                }
            };
        }

        // Choix d'une liste de favoris seule (ajout depuis la recherche). Retourne -1 si annulé.
        public static int AskList(IWin32Window owner, string title, string[] listNames, int list)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.Font = new Font("Segoe UI", 9f);
                f.ClientSize = new Size(400, 92);
                Label l = new Label { Text = Lang.T("Enregistrer dans la liste :", "Save to list:"), Location = new Point(12, 18), AutoSize = true };
                ComboBox cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                cb.SetBounds(186, 14, 200, 23);
                for (int i = 0; i < Favorite.ListCount; i++) cb.Items.Add(FavoritesDialog.ListLabel(listNames, i));
                cb.SelectedIndex = Math.Max(0, Math.Min(Favorite.ListCount - 1, list));
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = Lang.T("Annuler", "Cancel"), DialogResult = DialogResult.Cancel };
                ok.SetBounds(220, 52, 80, 28);
                cancel.SetBounds(306, 52, 80, 28);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Controls.AddRange(new Control[] { l, cb, ok, cancel });
                Theme.Apply(f);
                return f.ShowDialog(owner) == DialogResult.OK ? cb.SelectedIndex : -1;
            }
        }
    }

    // Gestionnaire des favoris : cases à cocher (suppression en bloc), vérification des liens morts et des doublons,
    // nom et adresse modifiables, ordre, import/export.
    public class FavoritesDialog : Form
    {
        readonly List<Favorite> favs;
        readonly ListView view = new ListView();
        readonly Func<Favorite> currentPage;
        readonly Action<string> listen;
        readonly TextBox txtName = new TextBox(), txtUrl = new TextBox();
        readonly Label lblStatus = new Label();
        readonly Button btnCheck;
        // Cases cochées et résultat de la vérification, conservés quand la liste est redessinée.
        readonly Dictionary<Favorite, bool> checkedFavs = new Dictionary<Favorite, bool>();
        readonly Dictionary<Favorite, string> state = new Dictionary<Favorite, string>();
        readonly Dictionary<Favorite, bool> problem = new Dictionary<Favorite, bool>();   // lien mort ou doublon (affiché en rouge)
        bool filling, refreshing, checking;

        // Favori à ouvrir dans le WebSDR après fermeture (double-clic ou bouton Ouvrir).
        public Favorite ToOpen;

        readonly string[] listNames;
        readonly ComboBox cbList = new ComboBox();
        int AllLists { get { return Favorite.ListCount; } }   // dernier choix de la liste déroulante : toutes les listes

        // Liste affichée (0 à ListCount - 1, ou AllLists) ; c'est aussi la liste où vont les favoris ajoutés ou importés.
        public int ShownList { get { return Math.Max(0, cbList.SelectedIndex); } }
        int TargetList(int mainList) { return ShownList == AllLists ? mainList : ShownList; }
        readonly int mainList;

        // Nom affiché d'une liste : "Liste 1" devient "List 1" en anglais tant qu'elle n'a pas été renommée.
        public static string ListLabel(string[] names, int i)
        {
            string def = "Liste " + (i + 1);
            return names[i] == def ? Lang.F("Liste {0}", "List {0}", i + 1) : names[i];
        }

        // Petite fenêtre de saisie d'un nom de liste.
        public static string AskListName(IWin32Window owner, string current)
        {
            using (FavoriteEditDialog d = new FavoriteEditDialog(Lang.T("Renommer la liste", "Rename list"), current, "-"))
            {
                d.HideAddress();
                return d.ShowDialog(owner) == DialogResult.OK ? d.FavName : null;
            }
        }

        public FavoritesDialog(List<Favorite> favorites, Func<Favorite> currentPage, string[] listNames, int startList, Action<string> listen)
        {
            favs = favorites;
            this.currentPage = currentPage;
            this.listen = listen;
            this.listNames = listNames;
            mainList = Math.Max(0, Math.Min(Favorite.ListCount - 1, startList));
            Text = Lang.T("Gestion des favoris", "Favorite manager");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(700, 640);
            MinimumSize = new Size(716, 460);

            Button add = Make(Lang.T("Ajouter un favori…", "Add favorite…"), 12, 26, 150, 34, delegate { AddFavorite(); });
            add.Font = new Font(Font, FontStyle.Bold);
            Button imp = Make(Lang.T("Importer…", "Import…"), 12, 66, 72, 28, delegate { ImportFile(); });
            Button exp = Make(Lang.T("Exporter…", "Export…"), 90, 66, 72, 28, delegate { ExportFile(); });

            GroupBox sel = new GroupBox { Text = Lang.T("Éléments sélectionnés / cochés", "Selected / checked items"), Location = new Point(176, 4), Size = new Size(282, 90) };
            sel.Controls.Add(Make(Lang.T("Supprimer…", "Remove…"), 10, 22, 125, 28, delegate { RemoveItems(); }));
            sel.Controls.Add(Make(Lang.T("Renommer…", "Rename…"), 10, 54, 125, 28, delegate { RenameSelected(); }));
            sel.Controls.Add(Make(Lang.T("Monter", "Move up"), 145, 22, 125, 28, delegate { MoveItem(-1); }));
            sel.Controls.Add(Make(Lang.T("Descendre", "Move down"), 145, 54, 125, 28, delegate { MoveItem(1); }));

            GroupBox chk = new GroupBox { Text = Lang.T("Vérification", "Check"), Location = new Point(466, 4), Size = new Size(222, 90) };
            btnCheck = Make(Lang.T("Vérifier les favoris", "Check favorites"), 10, 22, 202, 28, delegate { VerifyAll(); });
            btnCheck.Font = new Font(Font, FontStyle.Bold);
            chk.Controls.Add(btnCheck);
            chk.Controls.Add(Make(Lang.T("Tout cocher", "Check all"), 10, 54, 99, 28, delegate { SetAllChecked(true); }));
            chk.Controls.Add(Make(Lang.T("Tout décocher", "Uncheck all"), 113, 54, 99, 28, delegate { SetAllChecked(false); }));

            // Liste affichée (clic droit : renommer) et déplacement des favoris vers une autre liste.
            Label lList = new Label { Text = Lang.T("Liste :", "List:"), Location = new Point(12, 108), AutoSize = true };
            cbList.DropDownStyle = ComboBoxStyle.DropDownList;
            cbList.SetBounds(80, 104, 200, 23);
            FillListCombo(mainList);
            cbList.SelectedIndexChanged += delegate { Refill(-1); UpdateListButtons(); };
            ContextMenuStrip listMenu = new ContextMenuStrip();
            ToolStripMenuItem rename = new ToolStripMenuItem();
            listMenu.Items.Add(rename);
            listMenu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (ShownList == AllLists) { e.Cancel = true; return; }
                rename.Text = Lang.F("Renommer « {0} »…", "Rename \"{0}\"…", ListLabel(listNames, ShownList));
            };
            rename.Click += delegate { RenameList(); };
            cbList.ContextMenuStrip = listMenu;
            Button btnMove = Make(Lang.T("Déplacer vers ▸", "Move to ▸"), 290, 102, 130, 27, null);
            btnMove.Click += delegate { ShowMoveMenu(btnMove); };
            // Ajout et suppression de listes (1 à Favorite.MaxLists).
            btnAddList = Make(Lang.T("Nouvelle liste", "New list"), 430, 102, 120, 27, delegate { AddList(); });
            btnRemoveList = Make(Lang.T("Supprimer la liste…", "Delete list…"), 556, 102, 132, 27, delegate { RemoveList(); });
            ToolTip tips = new ToolTip();
            tips.SetToolTip(cbList, Lang.T("(clic droit sur la liste pour la renommer)", "(right-click the list to rename it)"));
            UpdateListButtons();

            // Nom et adresse du favori sélectionné, modifiables directement (la liste se met à jour en tapant).
            Label lName = new Label { Text = Lang.T("Nom :", "Name:"), Location = new Point(12, 142), AutoSize = true };
            Label lUrl = new Label { Text = Lang.T("Adresse :", "Address:"), Location = new Point(12, 172), AutoSize = true };
            txtName.SetBounds(80, 138, 608, 23);
            txtUrl.SetBounds(80, 168, 608, 23);
            txtName.Anchor = txtUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtName.TextChanged += delegate { EditSelected(true); };
            txtUrl.TextChanged += delegate { EditSelected(false); };
            // Champ laissé vide : on remet la valeur du favori.
            txtName.Leave += delegate { ShowSelected(); };
            txtUrl.Leave += delegate { ShowSelected(); };

            view.SetBounds(12, 200, 676, 388);
            view.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            view.View = View.Details;
            view.CheckBoxes = true;
            view.FullRowSelect = true;
            view.HideSelection = false;
            view.MultiSelect = false;
            view.Columns.Add(Lang.T("Nom", "Name"), 220);
            view.Columns.Add(Lang.T("Adresse", "Address"), 235);
            view.Columns.Add(Lang.T("État", "Status"), 120);
            view.Columns.Add(Lang.T("Liste", "List"), 75);
            view.SelectedIndexChanged += delegate { if (!refreshing) ShowSelected(); };
            view.ItemChecked += delegate(object s, ItemCheckedEventArgs e)
            {
                if (refreshing) return;
                Favorite f = (Favorite)e.Item.Tag;
                if (e.Item.Checked) checkedFavs[f] = true; else checkedFavs.Remove(f);
                UpdateStatus();
            };
            view.DoubleClick += delegate { OpenSelected(); };
            view.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete) RemoveItems();
                else if (e.KeyCode == Keys.Enter) OpenSelected();
            };

            lblStatus.SetBounds(12, 593, 378, 40);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            // Écoute dans la fenêtre principale sans fermer le gestionnaire, comme dans la recherche des WebSDR.
            Button btnListen = Make(Lang.T("Écouter la sélection", "Listen to selection"), 396, 598, 120, 30, delegate { ListenSelected(); });
            Button open = Make(Lang.T("Ouvrir", "Open"), 522, 598, 80, 30, delegate { OpenSelected(); });
            Button close = Make(Lang.T("Fermer", "Close"), 608, 598, 80, 30, delegate { Close(); });
            btnListen.Anchor = open.Anchor = close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = close;

            Controls.AddRange(new Control[] { add, imp, exp, sel, chk, lList, cbList, btnMove, btnAddList, btnRemoveList, lName, txtName, lUrl, txtUrl, view, lblStatus, btnListen, open, close });
            Theme.Apply(this);
            Refill(-1);
            ShowSelected();
        }

        static Button Make(string text, int x, int y, int w, int h, EventHandler click)
        {
            Button b = new Button { Text = text };
            b.SetBounds(x, y, w, h);
            if (click != null) b.Click += click;
            return b;
        }

        void FillListCombo(int select)
        {
            cbList.Items.Clear();
            for (int i = 0; i < Favorite.ListCount; i++) cbList.Items.Add(ListLabel(listNames, i));
            cbList.Items.Add(Lang.T("Toutes les listes", "All lists"));
            cbList.SelectedIndex = select;
        }

        Button btnAddList, btnRemoveList;

        void UpdateListButtons()
        {
            if (btnAddList == null) return;
            btnAddList.Enabled = Favorite.ListCount < Favorite.MaxLists;
            btnRemoveList.Enabled = ShownList != AllLists && Favorite.ListCount > 1;
        }

        // Nouvelle liste à la fin (nommée "Liste N", à renommer par clic droit), affichée tout de suite.
        void AddList()
        {
            if (Favorite.ListCount >= Favorite.MaxLists) return;
            int i = Favorite.ListCount++;
            listNames[i] = "Liste " + (i + 1);
            string name = AskListName(this, listNames[i]);
            if (!string.IsNullOrEmpty(name)) listNames[i] = name.Replace("|", "/");
            refreshing = true;
            FillListCombo(i);
            refreshing = false;
            Refill(-1);
            UpdateListButtons();
            lblStatus.Text = Lang.F("Liste « {0} » ajoutée ({1}/{2}).", "List \"{0}\" added ({1}/{2}).", ListLabel(listNames, i), Favorite.ListCount, Favorite.MaxLists);
        }

        // Supprime la liste affichée et ses favoris (après confirmation) ; les listes suivantes remontent d'un rang.
        void RemoveList()
        {
            int i = ShownList;
            if (i == AllLists || Favorite.ListCount <= 1) return;
            string label = ListLabel(listNames, i);
            int count = favs.FindAll(delegate(Favorite f) { return f.List == i; }).Count;
            string q = count == 0
                ? Lang.F("Supprimer la liste « {0} » (vide) ?", "Delete the list \"{0}\" (empty)?", label)
                : Lang.F("Supprimer la liste « {0} » et ses {1} favori(s) ?", "Delete the list \"{0}\" and its {1} favorite(s)?", label, count);
            if (MessageBox.Show(this, q, "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            DeleteList(favs, listNames, i);
            refreshing = true;
            FillListCombo(Math.Min(i, Favorite.ListCount - 1));
            refreshing = false;
            Refill(-1);
            UpdateListButtons();
            lblStatus.Text = Lang.F("Liste « {0} » supprimée.", "List \"{0}\" deleted.", label);
        }

        // Retire la liste i : ses favoris sont supprimés, ceux des listes suivantes et les noms remontent d'un rang.
        public static void DeleteList(List<Favorite> favs, string[] listNames, int i)
        {
            favs.RemoveAll(delegate(Favorite f) { return f.List == i; });
            foreach (Favorite f in favs) if (f.List > i) f.List--;
            // Une liste jamais renommée (« Liste 5 ») prend le nom de son nouveau rang (« Liste 4 »).
            for (int j = i; j < Favorite.ListCount - 1; j++) listNames[j] = listNames[j + 1] == "Liste " + (j + 2) ? "Liste " + (j + 1) : listNames[j + 1];
            Favorite.ListCount--;
            for (int j = Favorite.ListCount; j < Favorite.MaxLists; j++) listNames[j] = "Liste " + (j + 1);
        }

        void RenameList()
        {
            int i = ShownList;
            if (i == AllLists) return;
            string name = AskListName(this, listNames[i]);
            if (string.IsNullOrEmpty(name)) return;
            listNames[i] = name.Replace("|", "/");
            refreshing = true;
            FillListCombo(i);
            refreshing = false;
            Refill(-1);
        }

        // Déplace les favoris cochés (ou, à défaut, le favori sélectionné) vers la liste choisie.
        void ShowMoveMenu(Control anchor)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            for (int i = 0; i < Favorite.ListCount; i++)
            {
                int target = i;
                m.Items.Add(ListLabel(listNames, i), null, delegate { MoveToList(target); });
            }
            m.Show(anchor, new Point(0, anchor.Height));
        }

        void MoveToList(int target)
        {
            List<Favorite> items = CheckedShown();
            if (items.Count == 0)
            {
                if (!NeedSelection()) return;
                items.Add(Selected);
            }
            foreach (Favorite f in items) { f.List = target; checkedFavs.Remove(f); }
            Refill(-1);
            lblStatus.Text = Lang.F("{0} favori(s) déplacé(s) vers « {1} ».", "{0} favorite(s) moved to \"{1}\".", items.Count, ListLabel(listNames, target));
        }

        // Favoris de la liste affichée, dans l'ordre général.
        List<Favorite> VisibleFavorites()
        {
            int l = ShownList;
            return favs.FindAll(delegate(Favorite f) { return l == AllLists || f.List == l; });
        }

        void Refill(int select)
        {
            refreshing = true;
            view.BeginUpdate();
            view.Items.Clear();
            foreach (Favorite f in VisibleFavorites())
            {
                string st;
                state.TryGetValue(f, out st);
                ListViewItem it = new ListViewItem(new[] { f.Name, f.Url, st ?? "", ListLabel(listNames, f.List) });
                it.Tag = f;
                it.Checked = checkedFavs.ContainsKey(f);
                if (problem.ContainsKey(f)) it.ForeColor = ProblemColor;
                view.Items.Add(it);
            }
            view.EndUpdate();
            if (select >= 0 && select < view.Items.Count)
            {
                view.Items[select].Selected = true;
                view.Items[select].EnsureVisible();
            }
            refreshing = false;
            ShowSelected();
            UpdateStatus();
        }

        static readonly Color ProblemColor = Color.FromArgb(220, 60, 60);

        int SelectedIndex { get { return view.SelectedIndices.Count > 0 ? view.SelectedIndices[0] : -1; } }
        Favorite Selected { get { return view.SelectedItems.Count > 0 ? (Favorite)view.SelectedItems[0].Tag : null; } }

        bool NeedSelection()
        {
            if (Selected != null) return true;
            MessageBox.Show(this, Lang.T("Sélectionnez d'abord un favori dans la liste.", "Please select a favorite in the list first."), "SDRLink");
            return false;
        }

        // Recopie le favori sélectionné dans les champs Nom / Adresse.
        void ShowSelected()
        {
            Favorite f = Selected;
            filling = true;
            txtName.Text = f != null ? f.Name : "";
            txtUrl.Text = f != null ? f.Url : "";
            txtName.Enabled = txtUrl.Enabled = f != null;
            filling = false;
        }

        // Applique la saisie au favori sélectionné et met à jour sa ligne.
        void EditSelected(bool name)
        {
            Favorite f = Selected;
            if (filling || f == null) return;
            string v = (name ? txtName.Text : txtUrl.Text).Trim();
            if (v.Length == 0) return;
            if (name) f.Name = v; else f.Url = v;
            ListViewItem it = view.Items[SelectedIndex];
            it.SubItems[name ? 0 : 1].Text = v;
        }

        void UpdateStatus()
        {
            if (checking) return;
            int shown = view.Items.Count, ticked = CheckedShown().Count;
            lblStatus.Text = Lang.F("{0} favori(s) affiché(s), {1} coché(s)", "{0} favorite(s) shown, {1} checked", shown, ticked);
        }

        // Seuls les favoris visibles comptent : une case cochée dans une autre liste n'est pas touchée.
        List<Favorite> CheckedShown()
        {
            return VisibleFavorites().FindAll(delegate(Favorite f) { return checkedFavs.ContainsKey(f); });
        }

        void SetAllChecked(bool on)
        {
            if (!on) checkedFavs.Clear();
            else foreach (Favorite f in VisibleFavorites()) checkedFavs[f] = true;
            Refill(SelectedIndex);
        }

        void AddFavorite()
        {
            Favorite cur = currentPage();
            using (FavoriteEditDialog d = new FavoriteEditDialog(Lang.T("Ajouter un favori", "Add favorite"),
                cur != null ? cur.Name : "", cur != null ? cur.Url : "http://", listNames, TargetList(mainList), InList))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                favs.Add(new Favorite(d.FavName, d.FavUrl) { List = d.FavList });
                // Le nouveau favori n'est visible que si sa liste est affichée.
                if (ShownList != AllLists && ShownList != d.FavList) cbList.SelectedIndex = d.FavList;
                Refill(-1);
                if (view.Items.Count > 0) { view.Items[view.Items.Count - 1].Selected = true; view.Items[view.Items.Count - 1].EnsureVisible(); }
            }
        }

        bool InList(string url, int list)
        {
            return favs.Exists(delegate(Favorite f) { return f.List == list && f.Matches(url); });
        }

        // Supprime les favoris cochés ; s'il n'y en a aucun, le favori sélectionné.
        void RemoveItems()
        {
            List<Favorite> targets = CheckedShown();
            if (targets.Count == 0)
            {
                if (!NeedSelection()) return;
                targets.Add(Selected);
            }
            string question = targets.Count == 1
                ? Lang.F("Supprimer le favori « {0} » ?", "Remove favorite \"{0}\"?", targets[0].Name)
                : Lang.F("Supprimer les {0} favoris cochés ?", "Remove the {0} checked favorites?", targets.Count);
            if (MessageBox.Show(this, question, "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int keep = SelectedIndex;
            foreach (Favorite f in targets)
            {
                favs.Remove(f);
                checkedFavs.Remove(f);
                state.Remove(f);
                problem.Remove(f);
            }
            Refill(Math.Min(keep, VisibleFavorites().Count - 1));
        }

        // Renommer : place le curseur dans le champ Nom, le texte sélectionné, prêt à être corrigé.
        void RenameSelected()
        {
            if (!NeedSelection()) return;
            txtName.Focus();
            txtName.SelectAll();
        }

        // Échange avec le voisin visible (même liste), dans l'ordre général des favoris.
        void MoveItem(int delta)
        {
            if (!NeedSelection()) return;
            List<Favorite> shown = VisibleFavorites();
            int i = SelectedIndex, j = i + delta;
            if (j < 0 || j >= shown.Count) return;
            int a = favs.IndexOf(shown[i]), b = favs.IndexOf(shown[j]);
            Favorite f = favs[a];
            favs[a] = favs[b];
            favs[b] = f;
            Refill(j);
        }

        void ListenSelected()
        {
            if (!NeedSelection()) return;
            listen(Selected.Url);
        }

        void OpenSelected()
        {
            if (!NeedSelection()) return;
            ToOpen = Selected;
            Close();
        }

        // ---------------------------------------------------------------- Vérification des liens et des doublons

        // Doublons supprimés tout de suite ; chaque adresse est ensuite interrogée en arrière-plan,
        // et celles qui ne répondent pas sont cochées pour une suppression manuelle.
        void VerifyAll()
        {
            // Vérifie les favoris affichés (la liste choisie, ou toutes les listes).
            List<Favorite> scope = VisibleFavorites();
            if (checking || scope.Count == 0) return;
            checking = true;
            btnCheck.Enabled = false;
            state.Clear();
            problem.Clear();

            // Doublons (même adresse dans la même liste) supprimés d'office : on garde le premier.
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            List<Favorite> toTest = new List<Favorite>();
            int duplicates = 0;
            foreach (Favorite f in scope)
            {
                string key = WebSdrFinder.UrlKey(f.Url);
                if (key != null && seen.ContainsKey(key + "#" + f.List))
                {
                    favs.Remove(f);
                    checkedFavs.Remove(f);
                    duplicates++;
                    continue;
                }
                if (key != null) seen[key + "#" + f.List] = true;
                state[f] = Lang.T("vérification…", "checking…");
                toTest.Add(f);
            }
            if (duplicates > 0) Log.Write("Vérification des favoris : " + duplicates + " doublon(s) supprimé(s)");
            Refill(-1);

            int remaining = toTest.Count, dead = 0;
            lblStatus.Text = Lang.F("Vérification de {0} adresse(s)…", "Checking {0} address(es)…", remaining);
            if (remaining == 0) { FinishVerify(duplicates, 0); return; }

            // 10 vérifications en parallèle : rapide sans saturer la connexion.
            Queue<Favorite> queue = new Queue<Favorite>(toTest);
            object qLock = new object();
            for (int t = 0; t < Math.Min(10, toTest.Count); t++)
            {
                System.Threading.Thread th = new System.Threading.Thread(delegate()
                {
                    while (true)
                    {
                        Favorite f;
                        lock (qLock) { if (queue.Count == 0) return; f = queue.Dequeue(); }
                        bool alive;
                        string result = Probe(f.Url, out alive);
                        try
                        {
                            BeginInvoke((Action)delegate
                            {
                                state[f] = result;
                                if (!alive) { checkedFavs[f] = true; problem[f] = true; dead++; }
                                foreach (ListViewItem it in view.Items)
                                {
                                    if (it.Tag != f) continue;
                                    refreshing = true;
                                    it.SubItems[2].Text = result;
                                    it.Checked = checkedFavs.ContainsKey(f);
                                    it.ForeColor = alive ? view.ForeColor : ProblemColor;
                                    refreshing = false;
                                }
                                if (--remaining == 0) FinishVerify(duplicates, dead);
                                else lblStatus.Text = Lang.F("Vérification… encore {0}", "Checking… {0} left", remaining);
                            });
                        }
                        catch (InvalidOperationException) { return; }   // fenêtre fermée pendant la vérification
                    }
                });
                th.IsBackground = true;
                th.Start();
            }
        }

        void FinishVerify(int duplicates, int dead)
        {
            checking = false;
            btnCheck.Enabled = true;
            Refill(SelectedIndex);
            lblStatus.Text = Lang.F("Vérification terminée : {1} doublon(s) supprimé(s), {0} lien(s) mort(s) cochés. Cliquez sur « Supprimer… » pour les retirer.",
                                    "Check done: {1} duplicate(s) removed, {0} dead link(s) checked. Click \"Remove…\" to delete them.", dead, duplicates);
            Log.Write("Vérification des favoris : " + dead + " lien(s) mort(s)");
        }

        // Le lien est "vivant" si le serveur répond, même par une erreur d'accès (401, 403...) ou un certificat imparfait :
        // seuls l'absence de réponse et les pages introuvables (404, 410) comptent comme liens morts.
        static string Probe(string url, out bool alive)
        {
            alive = false;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = "SDRLink/1.0 (F5PBG)";
                req.AllowAutoRedirect = true;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    alive = true;
                    return Lang.T("OK", "OK") + " (" + (int)resp.StatusCode + ")";
                }
            }
            catch (UriFormatException) { return Lang.T("adresse invalide", "invalid address"); }
            catch (WebException ex)
            {
                HttpWebResponse r = ex.Response as HttpWebResponse;
                if (r != null)
                {
                    int code = (int)r.StatusCode;
                    r.Close();
                    alive = code != 404 && code != 410;
                    return (alive ? Lang.T("répond", "answers") : Lang.T("introuvable", "not found")) + " (" + code + ")";
                }
                switch (ex.Status)
                {
                    case WebExceptionStatus.TrustFailure:
                    case WebExceptionStatus.SecureChannelFailure:
                        alive = true;
                        return Lang.T("répond (certificat)", "answers (certificate)");
                    case WebExceptionStatus.Timeout: return Lang.T("ne répond pas", "no answer");
                    case WebExceptionStatus.NameResolutionFailure: return Lang.T("nom inconnu", "unknown host");
                    case WebExceptionStatus.ConnectFailure: return Lang.T("connexion refusée", "connection refused");
                }
                return Lang.T("injoignable", "unreachable");
            }
            catch (Exception) { return Lang.T("injoignable", "unreachable"); }
        }

        // ---------------------------------------------------------------- Import / export

        void ImportFile()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = Lang.T("Importer des favoris", "Import favorites");
                d.Filter = Lang.T("Favoris (*.csv;*.txt;*.html;*.htm)|*.csv;*.txt;*.html;*.htm|Tous les fichiers (*.*)|*.*",
                                  "Favorites (*.csv;*.txt;*.html;*.htm)|*.csv;*.txt;*.html;*.htm|All files (*.*)|*.*");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                // Chaque favori va dans sa liste (dossiers du fichier) ; ceux sans liste vont dans la liste affichée.
                List<Favorite> imported;
                bool wasAll = ShownList == AllLists;   // l'import peut créer des listes : "Toutes les listes" change de rang
                try { imported = Favorite.Import(d.FileName, listNames, TargetList(mainList)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "SDRLink"); return; }
                int added = 0;
                int[] perList = new int[Favorite.MaxLists];
                foreach (Favorite f in imported)
                {
                    Favorite n = f;
                    if (favs.Exists(delegate(Favorite x) { return x.List == n.List && x.Matches(n.Url); })) continue;
                    favs.Add(f);
                    perList[f.List]++;
                    added++;
                }
                refreshing = true;
                FillListCombo(wasAll ? AllLists : ShownList);   // listes créées ou renommées d'après le fichier
                refreshing = false;
                UpdateListButtons();
                Refill(-1);
                StringBuilder detail = new StringBuilder();
                for (int i = 0; i < Favorite.ListCount; i++)
                    if (perList[i] > 0) detail.Append("\n").Append(ListLabel(listNames, i)).Append(" : ").Append(perList[i]);
                MessageBox.Show(this, Lang.F("{0} favori(s) importé(s), {1} déjà présent(s).",
                                             "{0} favorite(s) imported, {1} already present.", added, imported.Count - added) + detail, "SDRLink");
            }
        }

        void ExportFile()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = Lang.T("Exporter les favoris", "Export favorites");
                d.FileName = "SDRLink-favoris";
                d.Filter = Lang.T("Marque-pages Firefox / navigateur (*.html)|*.html|CSV, lisible par Excel (*.csv)|*.csv|Texte (*.txt)|*.txt",
                                  "Firefox / browser bookmarks (*.html)|*.html|CSV, Excel-readable (*.csv)|*.csv|Text (*.txt)|*.txt");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Favorite.Export(d.FileName, favs, listNames);
                    MessageBox.Show(this, Lang.F("{0} favori(s) exporté(s).", "{0} favorite(s) exported.", favs.Count), "SDRLink");
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "SDRLink"); }
            }
        }
    }
}
