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

        public const int ListCount = 4;

        public Favorite(string name, string url)
        {
            Url = (url ?? "").Trim();
            Name = string.IsNullOrEmpty(name) ? HostOf(Url) : name.Trim();
        }

        // Même WebSDR si hôte, port et chemin sont identiques (sans tenir compte de ?tune=... ni de la casse).
        public bool Matches(string url)
        {
            return Key(url) != null && Key(url) == Key(Url);
        }

        static string Key(string url)
        {
            Uri u;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out u)) return null;
            return (u.Host + ":" + u.Port + u.AbsolutePath.TrimEnd('/')).ToLowerInvariant();
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
                    { Volume = vol < 0 ? -1 : Math.Min(100, vol), List = Math.Max(0, Math.Min(ListCount - 1, list)) };
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

        // Import : favoris HTML d'un navigateur (<A HREF="...">nom</A>), ou lignes "nom;URL", "nom<tab>URL", "nom|URL", "URL".
        public static List<Favorite> Import(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            List<Favorite> list = new List<Favorite>();
            MatchCollection links = Regex.Matches(text, "<a[^>]+href\\s*=\\s*\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (links.Count > 0)
            {
                foreach (Match m in links)
                {
                    string url = WebUtility.HtmlDecode(m.Groups[1].Value);
                    string name = WebUtility.HtmlDecode(Regex.Replace(m.Groups[2].Value, "<[^>]*>", "")).Trim();
                    if (LooksLikeUrl(url)) list.Add(new Favorite(name, url));
                }
                return list;
            }
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string[] p = line.Split(new[] { ';', '\t', '|' });
                int vol;
                if (p.Length >= 2 && LooksLikeUrl(p[1]))
                {
                    Favorite f = new Favorite(p[0].Trim('"', ' '), p[1].Trim('"', ' '));
                    if (p.Length >= 3 && int.TryParse(p[2].Trim('"', ' '), out vol)) f.Volume = Math.Max(0, Math.Min(100, vol));
                    list.Add(f);
                }
                else if (LooksLikeUrl(p[0])) list.Add(new Favorite(null, p[0].Trim('"', ' ')));
            }
            return list;
        }

        // Export : .html au format "favoris de navigateur" (importable dans Edge, Chrome, Firefox), sinon CSV "nom;URL;volume".
        public static void Export(string path, IList<Favorite> favs)
        {
            StringBuilder sb = new StringBuilder();
            if (path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("<!DOCTYPE NETSCAPE-Bookmark-file-1>\r\n<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\r\n");
                sb.Append("<TITLE>SDRLink</TITLE>\r\n<H1>SDRLink</H1>\r\n<DL><p>\r\n");
                foreach (Favorite f in favs)
                    sb.Append("    <DT><A HREF=\"").Append(WebUtility.HtmlEncode(f.Url)).Append("\">").Append(WebUtility.HtmlEncode(f.Name)).Append("</A>\r\n");
                sb.Append("</DL><p>\r\n");
            }
            else
            {
                foreach (Favorite f in favs)
                    sb.Append(f.Name.Replace(";", ",")).Append(';').Append(f.Url).Append(f.Volume >= 0 ? ";" + f.Volume : "").Append("\r\n");
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

        public FavoriteEditDialog(string title, string name, string url)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(480, 128);

            Label l1 = new Label { Text = Lang.T("Nom :", "Name:"), Location = new Point(12, 16), AutoSize = true };
            Label l2 = new Label { Text = Lang.T("Adresse :", "Address:"), Location = new Point(12, 50), AutoSize = true };
            txtName.SetBounds(90, 12, 375, 23);
            txtUrl.SetBounds(90, 46, 375, 23);
            txtName.Text = name;
            txtUrl.Text = url;
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = Lang.T("Annuler", "Cancel"), DialogResult = DialogResult.Cancel };
            ok.SetBounds(300, 88, 80, 28);
            cancel.SetBounds(386, 88, 80, 28);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { l1, l2, txtName, txtUrl, ok, cancel });
            addressLabel = l2;
            Theme.Apply(this);
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (DialogResult == DialogResult.OK && (FavName == "" || FavUrl == ""))
                {
                    MessageBox.Show(this, Lang.T("Indiquez un nom et une adresse.", "Please enter a name and an address."), "SDRLink");
                    e.Cancel = true;
                }
            };
        }
    }

    // Gestionnaire des favoris : cases à cocher (suppression en bloc), vérification des liens morts et des doublons,
    // nom et adresse modifiables, ordre, import/export.
    public class FavoritesDialog : Form
    {
        readonly List<Favorite> favs;
        readonly ListView view = new ListView();
        readonly Func<Favorite> currentPage;
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
        const int AllLists = Favorite.ListCount;   // dernier choix de la liste déroulante : toutes les listes

        // Liste affichée (0 à 3, ou AllLists) ; c'est aussi la liste où vont les favoris ajoutés ou importés.
        public int ShownList { get { return Math.Max(0, cbList.SelectedIndex); } }
        int TargetList(int mainList) { return ShownList == AllLists ? mainList : ShownList; }
        readonly int mainList;

        // Nom affiché d'une liste : "Liste 1" devient "List 1" en anglais tant qu'elle n'a pas été renommée.
        public static string ListLabel(string[] names, int i)
        {
            string def = "Liste " + (i + 1);
            return names[i] == def ? Lang.T(def, "List " + (i + 1)) : names[i];
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

        public FavoritesDialog(List<Favorite> favorites, Func<Favorite> currentPage, string[] listNames, int startList)
        {
            favs = favorites;
            this.currentPage = currentPage;
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
            cbList.SelectedIndexChanged += delegate { Refill(-1); };
            ContextMenuStrip listMenu = new ContextMenuStrip();
            ToolStripMenuItem rename = new ToolStripMenuItem();
            listMenu.Items.Add(rename);
            listMenu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (ShownList == AllLists) { e.Cancel = true; return; }
                rename.Text = Lang.T("Renommer « ", "Rename \"") + ListLabel(listNames, ShownList) + Lang.T(" »…", "\"…");
            };
            rename.Click += delegate { RenameList(); };
            cbList.ContextMenuStrip = listMenu;
            Button btnMove = Make(Lang.T("Déplacer vers ▸", "Move to ▸"), 290, 102, 130, 27, null);
            btnMove.Click += delegate { ShowMoveMenu(btnMove); };
            Label hint = new Label { Text = Lang.T("(clic droit sur la liste pour la renommer)", "(right-click the list to rename it)"), Location = new Point(430, 108), AutoSize = true };

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

            lblStatus.SetBounds(12, 593, 502, 40);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Button open = Make(Lang.T("Ouvrir", "Open"), 522, 598, 80, 30, delegate { OpenSelected(); });
            Button close = Make(Lang.T("Fermer", "Close"), 608, 598, 80, 30, delegate { Close(); });
            open.Anchor = close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = close;

            Controls.AddRange(new Control[] { add, imp, exp, sel, chk, lList, cbList, btnMove, hint, lName, txtName, lUrl, txtUrl, view, lblStatus, open, close });
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
            lblStatus.Text = Lang.T(items.Count + " favori(s) déplacé(s) vers « ", items.Count + " favorite(s) moved to \"")
                + ListLabel(listNames, target) + Lang.T(" ».", "\".");
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
            lblStatus.Text = Lang.T(shown + " favori(s) affiché(s), " + ticked + " coché(s)", shown + " favorite(s) shown, " + ticked + " checked");
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
                cur != null ? cur.Name : "", cur != null ? cur.Url : "http://"))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                favs.Add(new Favorite(d.FavName, d.FavUrl) { List = TargetList(mainList) });
                Refill(-1);
                if (view.Items.Count > 0) { view.Items[view.Items.Count - 1].Selected = true; view.Items[view.Items.Count - 1].EnsureVisible(); }
            }
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
                ? Lang.T("Supprimer le favori « " + targets[0].Name + " » ?", "Remove favorite \"" + targets[0].Name + "\"?")
                : Lang.T("Supprimer les " + targets.Count + " favoris cochés ?", "Remove the " + targets.Count + " checked favorites?");
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

        void OpenSelected()
        {
            if (!NeedSelection()) return;
            ToOpen = Selected;
            Close();
        }

        // ---------------------------------------------------------------- Vérification des liens et des doublons

        // Doublons cochés tout de suite ; chaque adresse est ensuite interrogée en arrière-plan,
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

            Dictionary<string, Favorite> firstByKey = new Dictionary<string, Favorite>();
            List<Favorite> toTest = new List<Favorite>();
            int duplicates = 0;
            foreach (Favorite f in scope)
            {
                string key = WebSdrFinder.UrlKey(f.Url);
                Favorite first;
                if (key != null && firstByKey.TryGetValue(key, out first))
                {
                    state[f] = Lang.T("doublon de « ", "duplicate of \"") + first.Name + Lang.T(" »", "\"");
                    checkedFavs[f] = true;
                    problem[f] = true;
                    duplicates++;
                    continue;
                }
                if (key != null) firstByKey[key] = f;
                state[f] = Lang.T("vérification…", "checking…");
                toTest.Add(f);
            }
            Refill(SelectedIndex);

            int remaining = toTest.Count, dead = 0;
            lblStatus.Text = Lang.T("Vérification de " + remaining + " adresse(s)…", "Checking " + remaining + " address(es)…");
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
                                else lblStatus.Text = Lang.T("Vérification… encore " + remaining, "Checking… " + remaining + " left");
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
            lblStatus.Text = Lang.T("Vérification terminée : " + dead + " lien(s) mort(s), " + duplicates + " doublon(s) cochés. Cliquez sur « Supprimer… » pour les retirer.",
                                    "Check done: " + dead + " dead link(s), " + duplicates + " duplicate(s) checked. Click \"Remove…\" to delete them.");
            Log.Write("Vérification des favoris : " + dead + " lien(s) mort(s), " + duplicates + " doublon(s)");
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
                List<Favorite> imported;
                try { imported = Favorite.Import(d.FileName); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "SDRLink"); return; }
                int added = 0;
                foreach (Favorite f in imported)
                {
                    if (favs.Exists(delegate(Favorite x) { return string.Equals(x.Url, f.Url, StringComparison.OrdinalIgnoreCase); })) continue;
                    f.List = TargetList(mainList);
                    favs.Add(f);
                    added++;
                }
                Refill(-1);
                MessageBox.Show(this, Lang.T(added + " favori(s) importé(s), " + (imported.Count - added) + " déjà présent(s).",
                                             added + " favorite(s) imported, " + (imported.Count - added) + " already present."), "SDRLink");
            }
        }

        void ExportFile()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = Lang.T("Exporter les favoris", "Export favorites");
                d.FileName = "SDRLink-favoris";
                d.Filter = Lang.T("CSV, lisible par Excel (*.csv)|*.csv|Favoris de navigateur (*.html)|*.html|Texte (*.txt)|*.txt",
                                  "CSV, Excel-readable (*.csv)|*.csv|Browser bookmarks (*.html)|*.html|Text (*.txt)|*.txt");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Favorite.Export(d.FileName, favs);
                    MessageBox.Show(this, Lang.T(favs.Count + " favori(s) exporté(s).", favs.Count + " favorite(s) exported."), "SDRLink");
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "SDRLink"); }
            }
        }
    }
}
