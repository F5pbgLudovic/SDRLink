using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SDRLink
{
    // Mémoire de fréquence : nom (pseudo), fréquence poste en Hz, mode (vide = mode inchangé), liste (0 à MaxLists - 1).
    public class Memory
    {
        public string Name = "";
        public long Hz;
        public string Mode = "";
        public int List;
        public bool Scan;   // cochée dans « Conf scan » (scan des mémoires)

        public const int MaxLists = 10;

        public Memory(string name, long hz, string mode)
        {
            Name = (name ?? "").Trim();
            Hz = hz;
            Mode = mode ?? "";
        }

        // Texte affiché : le nom s'il y en a un, sinon la fréquence.
        public string Label { get { return Name.Length > 0 ? Name : KHz(Hz) + " kHz"; } }

        public override string ToString() { return Label; }

        public static string KHz(long hz) { return (hz / 1000.0).ToString("0.###", CultureInfo.InvariantCulture); }

        public bool Same(Memory m) { return m != null && m.Hz == Hz && m.Name == Name && m.Mode == Mode; }

        // Ligne du fichier de réglages : "nom|Hz|mode|liste|scan" (scan : 1 si cochée pour le scan).
        public string ToSetting() { return Name.Replace("|", "/") + "|" + Hz.ToString(CultureInfo.InvariantCulture) + "|" + Mode + "|" + List + (Scan ? "|1" : ""); }

        public static Memory FromSetting(string v)
        {
            string[] p = v.Split('|');
            long hz;
            if (p.Length < 2 || !long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out hz) || hz <= 0) return null;
            Memory m = new Memory(p[0], hz, p.Length >= 3 ? p[2] : "");
            int list;
            if (p.Length >= 4 && int.TryParse(p[3], out list)) m.List = Math.Max(0, Math.Min(MaxLists - 1, list));
            m.Scan = p.Length >= 5 && p[4] == "1";
            return m;
        }
    }

    // Toutes les mémoires : listes (1 à 10, nommées), mémoires, et les 20 dernières utilisées (la plus récente en tête).
    public class MemoryBook
    {
        public readonly List<Memory> Items = new List<Memory>();
        public readonly List<Memory> Recent = new List<Memory>();
        public readonly string[] ListNames = new string[Memory.MaxLists];
        public int ListCount = 1;
        public int CurrentList;
        public const int RecentSize = 20;

        public MemoryBook()
        {
            for (int i = 0; i < ListNames.Length; i++) ListNames[i] = "Liste " + (i + 1);
        }

        // Mémoire utilisée : en tête des dernières utilisées, sans doublon.
        public void Used(Memory m)
        {
            Memory copy = new Memory(m.Name, m.Hz, m.Mode);
            Recent.RemoveAll(delegate(Memory r) { return r.Same(copy); });
            Recent.Insert(0, copy);
            if (Recent.Count > RecentSize) Recent.RemoveRange(RecentSize, Recent.Count - RecentSize);
        }

        // Retire la liste i et ses mémoires ; les suivantes remontent d'un rang.
        public void DeleteList(int i)
        {
            Items.RemoveAll(delegate(Memory m) { return m.List == i; });
            foreach (Memory m in Items) if (m.List > i) m.List--;
            for (int j = i; j < ListCount - 1; j++) ListNames[j] = ListNames[j + 1] == "Liste " + (j + 2) ? "Liste " + (j + 1) : ListNames[j + 1];
            ListCount--;
            for (int j = ListCount; j < Memory.MaxLists; j++) ListNames[j] = "Liste " + (j + 1);
            CurrentList = Math.Min(CurrentList, ListCount - 1);
        }
    }

    // Saisie d'une mémoire : nom, fréquence (kHz), mode, liste (même présentation que l'ajout d'un favori).
    public class MemoryEditDialog : Form
    {
        readonly TextBox txtName = new TextBox(), txtFreq = new TextBox();
        readonly ComboBox cbMode = new ComboBox(), cbList = new ComboBox();
        static readonly string[] ModeValues = { "", Modes.USB, Modes.LSB, Modes.CW, Modes.CWR, Modes.AM, Modes.FM, Modes.DATAU, Modes.DATAL };

        public Memory Result;

        public MemoryEditDialog(string title, Memory m, MemoryBook book)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(480, 196);

            Label l1 = new Label { Text = Lang.T("Nom :", "Name:"), Location = new Point(12, 16), AutoSize = true };
            Label l2 = new Label { Text = Lang.T("Fréquence (kHz) :", "Frequency (kHz):"), Location = new Point(12, 50), AutoSize = true };
            Label l3 = new Label { Text = Lang.T("Mode :", "Mode:"), Location = new Point(12, 84), AutoSize = true };
            Label l4 = new Label { Text = Lang.T("Liste :", "List:"), Location = new Point(12, 118), AutoSize = true };
            txtName.SetBounds(130, 12, 335, 23);
            txtFreq.SetBounds(130, 46, 120, 23);
            cbMode.DropDownStyle = cbList.DropDownStyle = ComboBoxStyle.DropDownList;
            cbMode.SetBounds(130, 80, 160, 23);
            cbList.SetBounds(130, 114, 200, 23);
            txtName.Text = m.Name;
            txtFreq.Text = m.Hz > 0 ? Memory.KHz(m.Hz) : "";
            foreach (string mode in ModeValues) cbMode.Items.Add(mode == "" ? Lang.T("(mode inchangé)", "(mode unchanged)") : Modes.Display(mode));
            cbMode.SelectedIndex = Math.Max(0, Array.IndexOf(ModeValues, m.Mode));
            for (int i = 0; i < book.ListCount; i++) cbList.Items.Add(FavoritesDialog.ListLabel(book.ListNames, i));
            cbList.SelectedIndex = Math.Max(0, Math.Min(book.ListCount - 1, m.List));
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = Lang.T("Annuler", "Cancel"), DialogResult = DialogResult.Cancel };
            ok.SetBounds(300, 156, 80, 28);
            cancel.SetBounds(386, 156, 80, 28);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { l1, l2, l3, l4, txtName, txtFreq, cbMode, cbList, ok, cancel });
            Theme.Apply(this);
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (DialogResult != DialogResult.OK) return;
                long hz = ParseKHz(txtFreq.Text);
                if (hz <= 0)
                {
                    MessageBox.Show(this, Lang.T("Fréquence invalide : tapez une valeur en kHz, par ex. 7074 ou 14074.5",
                                                 "Invalid frequency: type a value in kHz, e.g. 7074 or 14074.5"), "SDRLink");
                    e.Cancel = true;
                    return;
                }
                Result = new Memory(txtName.Text, hz, ModeValues[Math.Max(0, cbMode.SelectedIndex)]);
                Result.List = Math.Max(0, cbList.SelectedIndex);
            };
        }

        // Accepte "7074", "7074.5", "7074,5" (kHz) ou "14.074.000" / "14074000" (Hz).
        public static long ParseKHz(string text)
        {
            string s = (text ?? "").Trim().Replace(" ", "");
            if (s.Length == 0) return 0;
            if (s.Split('.').Length > 2) s = s.Replace(".", "");
            double v;
            if (!double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v <= 0) return 0;
            return v >= 1000000 ? (long)Math.Round(v) : (long)Math.Round(v * 1000);
        }
    }

    // Fenêtre « Mémoires » : listes de mémoires, ajout / modification / suppression, et changement de fréquence
    // immédiat en cliquant sur une mémoire (la fenêtre reste ouverte).
    public class MemoriesDialog : Form
    {
        readonly MemoryBook book;
        readonly Func<Memory> current;    // fréquence et mode actuels (null si inconnus)
        readonly Action<Memory> tune;
        readonly ListView view = new ListView();
        readonly ComboBox cbList = new ComboBox();
        readonly Label lblStatus = new Label();
        readonly Button btnAddList, btnRemoveList;
        bool filling;

        int AllLists { get { return book.ListCount; } }
        int ShownList { get { return Math.Max(0, cbList.SelectedIndex); } }

        public MemoriesDialog(MemoryBook book, Func<Memory> current, Action<Memory> tune)
        {
            this.book = book;
            this.current = current;
            this.tune = tune;
            Text = Lang.T("Mémoires", "Memories");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(620, 520);
            MinimumSize = new Size(636, 360);

            Button add = Make(Lang.T("Mémoriser la fréquence actuelle…", "Store the current frequency…"), 12, 12, 230, 32, delegate { AddMemory(); });
            add.Font = new Font(Font, FontStyle.Bold);
            Button edit = Make(Lang.T("Modifier…", "Edit…"), 250, 12, 110, 32, delegate { EditSelected(); });
            Button del = Make(Lang.T("Supprimer…", "Remove…"), 366, 12, 110, 32, delegate { RemoveSelected(); });

            Label lList = new Label { Text = Lang.T("Liste :", "List:"), Location = new Point(12, 60), AutoSize = true };
            cbList.DropDownStyle = ComboBoxStyle.DropDownList;
            cbList.SetBounds(70, 56, 200, 23);
            ContextMenuStrip listMenu = new ContextMenuStrip();
            ToolStripMenuItem rename = new ToolStripMenuItem();
            listMenu.Items.Add(rename);
            listMenu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (ShownList == AllLists) { e.Cancel = true; return; }
                rename.Text = Lang.F("Renommer « {0} »…", "Rename \"{0}\"…", FavoritesDialog.ListLabel(book.ListNames, ShownList));
            };
            rename.Click += delegate { RenameList(); };
            cbList.ContextMenuStrip = listMenu;
            new ToolTip().SetToolTip(cbList, Lang.T("(clic droit sur la liste pour la renommer)", "(right-click the list to rename it)"));
            btnAddList = Make(Lang.T("Nouvelle liste", "New list"), 280, 54, 120, 27, delegate { AddList(); });
            btnRemoveList = Make(Lang.T("Supprimer la liste…", "Delete list…"), 406, 54, 132, 27, delegate { RemoveList(); });

            view.SetBounds(12, 90, 596, 382);
            view.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            view.View = View.Details;
            view.FullRowSelect = true;
            view.HideSelection = false;
            view.MultiSelect = false;
            view.Columns.Add(Lang.T("Nom", "Name"), 230);
            view.Columns.Add(Lang.T("Fréquence (kHz)", "Frequency (kHz)"), 120);
            view.Columns.Add(Lang.T("Mode", "Mode"), 80);
            view.Columns.Add(Lang.T("Liste", "List"), 140);
            // Un clic sur une mémoire change la fréquence tout de suite.
            view.MouseClick += delegate { TuneSelected(); };
            view.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { TuneSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete) RemoveSelected();
            };

            lblStatus.SetBounds(12, 482, 500, 30);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Button close = Make(Lang.T("Fermer", "Close"), 528, 480, 80, 30, delegate { Close(); });
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = close;

            Controls.AddRange(new Control[] { add, edit, del, lList, cbList, btnAddList, btnRemoveList, view, lblStatus, close });
            FillListCombo(Math.Min(book.CurrentList, book.ListCount - 1));
            cbList.SelectedIndexChanged += delegate
            {
                if (filling) return;
                if (ShownList != AllLists) book.CurrentList = ShownList;
                Refill(null);
            };
            Theme.Apply(this);
            Refill(null);
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
            filling = true;
            cbList.Items.Clear();
            for (int i = 0; i < book.ListCount; i++) cbList.Items.Add(FavoritesDialog.ListLabel(book.ListNames, i));
            cbList.Items.Add(Lang.T("Toutes les listes", "All lists"));
            cbList.SelectedIndex = Math.Max(0, Math.Min(cbList.Items.Count - 1, select));
            filling = false;
            btnAddList.Enabled = book.ListCount < Memory.MaxLists;
            btnRemoveList.Enabled = ShownList != AllLists && book.ListCount > 1;
        }

        void Refill(Memory select)
        {
            btnRemoveList.Enabled = ShownList != AllLists && book.ListCount > 1;
            view.BeginUpdate();
            view.Items.Clear();
            foreach (Memory m in book.Items)
            {
                if (ShownList != AllLists && m.List != ShownList) continue;
                ListViewItem it = new ListViewItem(new[] { m.Name, Memory.KHz(m.Hz), m.Mode == "" ? "—" : Modes.Display(m.Mode), FavoritesDialog.ListLabel(book.ListNames, m.List) });
                it.Tag = m;
                view.Items.Add(it);
                if (m == select) it.Selected = true;
            }
            view.EndUpdate();
            lblStatus.Text = Lang.F("{0} mémoire(s). Cliquez sur une mémoire pour y aller.", "{0} memory(ies). Click a memory to go there.", view.Items.Count);
        }

        Memory Selected { get { return view.SelectedItems.Count > 0 ? (Memory)view.SelectedItems[0].Tag : null; } }

        public void TuneSelected()
        {
            Memory m = Selected;
            if (m == null) return;
            tune(m);
            lblStatus.Text = Lang.F("Accord sur « {0} » ({1} kHz).", "Tuned to \"{0}\" ({1} kHz).", m.Label, Memory.KHz(m.Hz));
        }

        public void AddMemory()
        {
            Memory m = current() ?? new Memory("", 0, "");
            m.List = ShownList == AllLists ? book.CurrentList : ShownList;
            using (MemoryEditDialog d = new MemoryEditDialog(Lang.T("Ajouter une mémoire", "Add memory"), m, book))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                book.Items.Add(d.Result);
                if (ShownList != AllLists && d.Result.List != ShownList) FillListCombo(d.Result.List);
                Refill(d.Result);
            }
        }

        void EditSelected()
        {
            Memory m = Selected;
            if (m == null) { lblStatus.Text = Lang.T("Sélectionnez d'abord une mémoire.", "Select a memory first."); return; }
            using (MemoryEditDialog d = new MemoryEditDialog(Lang.T("Modifier la mémoire", "Edit memory"), m, book))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                m.Name = d.Result.Name; m.Hz = d.Result.Hz; m.Mode = d.Result.Mode; m.List = d.Result.List;
                if (ShownList != AllLists && m.List != ShownList) FillListCombo(m.List);
                Refill(m);
            }
        }

        void RemoveSelected()
        {
            Memory m = Selected;
            if (m == null) { lblStatus.Text = Lang.T("Sélectionnez d'abord une mémoire.", "Select a memory first."); return; }
            if (MessageBox.Show(this, Lang.F("Supprimer la mémoire « {0} » ?", "Remove the memory \"{0}\"?", m.Label), "SDRLink",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            book.Items.Remove(m);
            Refill(null);
        }

        public void AddList()
        {
            if (book.ListCount >= Memory.MaxLists) return;
            int i = book.ListCount++;
            book.ListNames[i] = "Liste " + (i + 1);
            string name = FavoritesDialog.AskListName(this, book.ListNames[i]);
            if (!string.IsNullOrEmpty(name)) book.ListNames[i] = name.Replace("|", "/");
            book.CurrentList = i;
            FillListCombo(i);
            Refill(null);
        }

        void RemoveList()
        {
            int i = ShownList;
            if (i == AllLists || book.ListCount <= 1) return;
            string label = FavoritesDialog.ListLabel(book.ListNames, i);
            int count = book.Items.FindAll(delegate(Memory m) { return m.List == i; }).Count;
            string q = count == 0
                ? Lang.F("Supprimer la liste « {0} » (vide) ?", "Delete the list \"{0}\" (empty)?", label)
                : Lang.F("Supprimer la liste « {0} » et ses {1} mémoire(s) ?", "Delete the list \"{0}\" and its {1} memory(ies)?", label, count);
            if (MessageBox.Show(this, q, "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            book.DeleteList(i);
            FillListCombo(Math.Min(i, book.ListCount - 1));
            Refill(null);
        }

        void RenameList()
        {
            int i = ShownList;
            if (i == AllLists) return;
            string name = FavoritesDialog.AskListName(this, book.ListNames[i]);
            if (string.IsNullOrEmpty(name)) return;
            book.ListNames[i] = name.Replace("|", "/");
            FillListCombo(i);
            Refill(null);
        }
    }
}
