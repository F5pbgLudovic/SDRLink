using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace SDRLink
{
    // Propositions sous la barre WebSDR pendant la frappe, comme un moteur de recherche : les WebSDR de la liste
    // déroulante (donc de la liste de favoris choisie) dont le nom ou l'adresse contient l'un des mots tapés.
    // Un clic (ou ↓ puis Entrée) ouvre le WebSDR. La flèche de la liste déroulante montre toujours toute la liste.
    class UrlSuggest
    {
        const int MaxRows = 12;
        readonly ComboBox box;
        readonly Action<Favorite> pick;
        SuggestPopup popup;
        SuggestList list;

        public UrlSuggest(ComboBox box, Action<Favorite> pick)
        {
            this.box = box;
            this.pick = pick;
            box.TextUpdate += delegate { Refresh(); };   // frappe seulement (pas les changements faits par le programme)
            box.DropDown += delegate { Hide(); };
            box.Leave += delegate { Hide(); };
            box.LostFocus += delegate { Hide(); };
            box.VisibleChanged += delegate { Hide(); };
        }

        public bool Visible { get { return popup != null && popup.Visible; } }

        // Touches de la barre quand les propositions sont affichées : ↓ ↑ pour choisir, Entrée pour ouvrir, Échap pour fermer.
        // Renvoie vrai si la touche est prise.
        public bool HandleKey(Keys key)
        {
            if (!Visible) return false;
            switch (key)
            {
                case Keys.Down:
                    if (list.SelectedIndex < list.Items.Count - 1) list.SelectedIndex++;
                    return true;
                case Keys.Up:
                    if (list.SelectedIndex >= 0) list.SelectedIndex--;
                    return true;
                case Keys.Escape:
                    Hide();
                    return true;
                case Keys.Enter:
                    if (list.SelectedIndex < 0) { Hide(); return false; }   // rien de choisi : le texte tapé est ouvert
                    Choose(list.SelectedIndex);
                    return true;
            }
            return false;
        }

        public void Hide()
        {
            if (popup != null && popup.Visible) popup.Hide();
        }

        void Choose(int i)
        {
            Suggestion s = i >= 0 && i < list.Items.Count ? list.Items[i] as Suggestion : null;
            Hide();
            if (s != null) pick(s.Fav);
        }

        void Refresh()
        {
            List<Favorite> favs = new List<Favorite>();
            foreach (object o in box.Items)
            {
                Favorite f = o as Favorite;
                if (f != null) favs.Add(f);
            }
            List<Favorite> found = Match(box.Text, favs);
            if (found.Count == 0 || !box.Focused) { Hide(); return; }
            Show(found);
        }

        void Show(List<Favorite> found)
        {
            Form owner = box.FindForm();
            if (owner == null) return;
            if (popup == null)
            {
                popup = new SuggestPopup();
                list = new SuggestList();
                list.Dock = DockStyle.Fill;
                list.BorderStyle = BorderStyle.None;
                list.IntegralHeight = false;
                list.Picked += Choose;
                popup.Controls.Add(list);
                popup.Owner = owner;
                owner.Deactivate += delegate { Hide(); };
                owner.Move += delegate { Hide(); };
                owner.Resize += delegate { Hide(); };
            }
            list.Font = box.Font;
            if (!list.IsHandleCreated) { IntPtr h = list.Handle; }   // fenêtre créée (sans l'afficher) : hauteur des lignes juste dès la 1re fois
            list.BackColor = Theme.InputBack;
            list.ForeColor = Theme.InputFore;
            popup.BackColor = Theme.Border;
            list.BeginUpdate();
            list.Items.Clear();
            int width = box.Width;
            foreach (Favorite f in found)
            {
                Suggestion s = new Suggestion(f);
                list.Items.Add(s);
                width = Math.Max(width, TextRenderer.MeasureText(s.Text, list.Font).Width + SystemInformation.VerticalScrollBarWidth + 12);
            }
            list.SelectedIndex = -1;
            list.EndUpdate();

            // Juste sous la barre, au-dessus si la place manque en bas de l'écran.
            Rectangle area = Screen.FromControl(box).WorkingArea;
            int rows = Math.Min(MaxRows, found.Count);
            Size size = new Size(Math.Min(width, area.Width), rows * list.ItemHeight + 2);
            Point below = box.Parent.PointToScreen(new Point(box.Left, box.Bottom));
            Point at = below;
            if (at.Y + size.Height > area.Bottom) at.Y = box.Parent.PointToScreen(new Point(box.Left, box.Top)).Y - size.Height;
            if (at.X + size.Width > area.Right) at.X = Math.Max(area.Left, area.Right - size.Width);
            popup.Bounds = new Rectangle(at, size);
            if (!popup.Visible) popup.Show(owner);
        }

        // Favoris dont le nom ou l'adresse contient au moins un des mots tapés ; ceux qui en contiennent le plus,
        // ou où le mot commence un mot du nom, d'abord ; à égalité, l'ordre de la liste.
        public static List<Favorite> Match(string text, List<Favorite> favs)
        {
            List<string> words = Words(text);
            List<KeyValuePair<int, int>> scored = new List<KeyValuePair<int, int>>();   // (score, rang dans la liste)
            if (words.Count > 0)
                for (int k = 0; k < favs.Count; k++)
                {
                    string name = Fold(favs[k].Name), url = Fold(favs[k].Url);
                    int score = 0;
                    foreach (string w in words)
                    {
                        int at = name.IndexOf(w, StringComparison.Ordinal);
                        if (at >= 0) score += at == 0 || !char.IsLetterOrDigit(name[at - 1]) ? 6 : 4;
                        else if (url.IndexOf(w, StringComparison.Ordinal) >= 0) score += 3;
                    }
                    if (score > 0) scored.Add(new KeyValuePair<int, int>(score, k));
                }
            scored.Sort(delegate(KeyValuePair<int, int> a, KeyValuePair<int, int> b)
            {
                return a.Key != b.Key ? b.Key.CompareTo(a.Key) : a.Value.CompareTo(b.Value);
            });
            return scored.ConvertAll(p => favs[p.Value]);
        }

        // Mots tapés, sans accents ni majuscules, deux caractères au moins ; « http:// », « https:// » et « www. »
        // en tête d'un mot ne comptent pas (toutes les adresses les contiennent).
        static List<string> Words(string text)
        {
            List<string> words = new List<string>();
            foreach (string part in Fold(text ?? "").Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = part;
                foreach (string p in new[] { "https://", "http://", "www." })
                    if (w.StartsWith(p, StringComparison.Ordinal)) w = w.Substring(p.Length);
                w = w.TrimEnd('/');
                if (w.Length >= 2 && !words.Contains(w)) words.Add(w);
            }
            return words;
        }

        static string Fold(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in (s ?? "").Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().ToLowerInvariant();
        }

        // Ligne de la liste : nom du WebSDR, et son adresse quand le nom ne la contient pas.
        class Suggestion
        {
            public readonly Favorite Fav;
            public readonly string Text;
            public Suggestion(Favorite f)
            {
                Fav = f;
                string host = Favorite.HostOf(f.Url) ?? "";
                Text = host == "" || f.Name.IndexOf(host, StringComparison.OrdinalIgnoreCase) >= 0 ? f.Name : f.Name + "   (" + host + ")";
            }
            public override string ToString() { return Text; }
        }

        // Fenêtre sans bordure qui ne prend jamais la main : la barre WebSDR garde le clavier pendant la frappe.
        class SuggestPopup : Form
        {
            public SuggestPopup()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Padding = new Padding(1);   // le fond (couleur de bordure) fait le contour
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams p = base.CreateParams;
                    p.ExStyle |= 0x08000000 | 0x00000080;   // WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW
                    return p;
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0021) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE → MA_NOACTIVATE
                base.WndProc(ref m);
            }
        }

        // Liste qui ne prend pas le clavier : un clic choisit la ligne, le survol la surligne.
        class SuggestList : ListBox
        {
            public event Action<int> Picked;

            protected override void WndProc(ref Message m)
            {
                const int WM_MOUSEACTIVATE = 0x0021, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONDBLCLK = 0x0203;
                if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)3; return; }
                if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK || m.Msg == WM_MOUSEMOVE)
                {
                    long lp = (long)m.LParam;
                    int i = IndexFromPoint(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    if (m.Msg == WM_MOUSEMOVE) { if (i >= 0 && i != SelectedIndex) SelectedIndex = i; }
                    else
                    {
                        if (i >= 0 && Picked != null) Picked(i);
                        return;   // pas de focus pour la liste
                    }
                }
                base.WndProc(ref m);
            }
        }
    }
}
