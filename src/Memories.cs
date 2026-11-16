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
        public int Preamp = -1;   // préampli du poste (Preamps : -1 inchangé, 0 sans/IPO, 1 AMP1, 2 AMP2, 3 IPO2), depuis la 1.41
        public int Power = -1;    // puissance d'émission en watts (-1 inchangée), depuis la 1.42

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

        // Ligne du fichier de réglages : "nom|Hz|mode|liste|scan|préampli|puissance" (scan : 1 si cochée pour le scan ; préampli ou
        // puissance vide ou absent = inchangé ; les versions plus anciennes ignorent les champs en trop).
        public string ToSetting()
        {
            string s = Name.Replace("|", "/") + "|" + Hz.ToString(CultureInfo.InvariantCulture) + "|" + Mode + "|" + List;
            if (Power >= 0) return s + "|" + (Scan ? "1" : "0") + "|" + (Preamp >= 0 ? Preamp.ToString(CultureInfo.InvariantCulture) : "") + "|" + Power.ToString(CultureInfo.InvariantCulture);
            if (Preamp >= 0) return s + "|" + (Scan ? "1" : "0") + "|" + Preamp.ToString(CultureInfo.InvariantCulture);
            return s + (Scan ? "|1" : "");
        }

        public static Memory FromSetting(string v)
        {
            string[] p = v.Split('|');
            long hz;
            if (p.Length < 2 || !long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out hz) || hz <= 0) return null;
            Memory m = new Memory(p[0], hz, p.Length >= 3 ? p[2] : "");
            int list;
            if (p.Length >= 4 && int.TryParse(p[3], out list)) m.List = Math.Max(0, Math.Min(MaxLists - 1, list));
            m.Scan = p.Length >= 5 && p[4] == "1";
            int pre;
            if (p.Length >= 6 && int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out pre) && pre >= 0 && pre <= Preamps.MaxCode) m.Preamp = pre;
            int pw;
            if (p.Length >= 7 && int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out pw) && pw >= 0 && pw <= Powers.LimitMax) m.Power = pw;
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

        // Préampli choisi pour des fréquences de la liste « Modes numériques » (1.41) : clé « Hz|mode », valeur Preamps.Off... ;
        // lignes « MemDigiPre=Hz|mode|code » du fichier de réglages.
        public readonly Dictionary<string, int> DigiPreamps = new Dictionary<string, int>(StringComparer.Ordinal);

        // Puissance (W) choisie pour des fréquences « Modes numériques » (1.42) : lignes « MemDigiPwr=Hz|mode|watts ».
        public readonly Dictionary<string, int> DigiPowers = new Dictionary<string, int>(StringComparer.Ordinal);

        public static string DigiKey(Memory m) { return m.Hz.ToString(CultureInfo.InvariantCulture) + "|" + m.Mode; }

        // Préampli des fréquences « Modes numériques » repris dans la liste donnée.
        public List<Memory> WithDigiPreamps(List<Memory> list)
        {
            foreach (Memory m in list) { int p; m.Preamp = DigiPreamps.TryGetValue(DigiKey(m), out p) ? p : -1; m.Power = DigiPowers.TryGetValue(DigiKey(m), out p) ? p : -1; }
            return list;
        }

        public MemoryBook()
        {
            for (int i = 0; i < ListNames.Length; i++) ListNames[i] = "Liste " + (i + 1);
        }

        // Mémoire utilisée : en tête des dernières utilisées, sans doublon.
        public void Used(Memory m)
        {
            Memory copy = new Memory(m.Name, m.Hz, m.Mode) { Preamp = m.Preamp, Power = m.Power };
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
        readonly ComboBox cbMode = new ComboBox(), cbList = new ComboBox(), cbPre = new ComboBox();
        static readonly string[] ModeValues = { "", Modes.USB, Modes.LSB, Modes.CW, Modes.CWR, Modes.AM, Modes.FM, Modes.DATAU, Modes.DATAL };
        readonly List<int> preCodes = new List<int>();
        readonly CheckBox chkPwr = new CheckBox();
        readonly NumericUpDown nuPwr = new NumericUpDown(), nuLimit = new NumericUpDown();

        public Memory Result;

        public MemoryEditDialog(string title, Memory m, MemoryBook book) : this(title, m, book, false) { }

        // preampOnly : fréquence de la liste « Modes numériques » (1.41) : seuls le préampli et la puissance se changent, le reste est grisé.
        public MemoryEditDialog(string title, Memory m, MemoryBook book, bool preampOnly)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(480, 322);

            Label l1 = new Label { Text = Lang.T("Nom :", "Name:"), Location = new Point(12, 16), AutoSize = true };
            Label l2 = new Label { Text = Lang.T("Fréquence (kHz) :", "Frequency (kHz):"), Location = new Point(12, 50), AutoSize = true };
            Label l3 = new Label { Text = Lang.T("Mode :", "Mode:"), Location = new Point(12, 84), AutoSize = true };
            Label l4 = new Label { Text = Lang.T("Liste :", "List:"), Location = new Point(12, 118), AutoSize = true };
            Label l5 = new Label { Text = Lang.T("Préampli :", "Preamp:"), Location = new Point(12, 152), AutoSize = true };
            // Préampli : réglages du poste synchronisé (IPO / AMP1 / AMP2 chez Yaesu…), ou les trois réglages courants sans poste.
            PreampSet ps = Preamps.Current ?? Preamps.Generic;
            cbPre.DropDownStyle = ComboBoxStyle.DropDownList;
            cbPre.SetBounds(130, 148, 200, 23);
            preCodes.Add(-1);
            cbPre.Items.Add(Lang.T("(préampli inchangé)", "(preamp unchanged)"));
            for (int i = 0; i < ps.Codes.Length; i++) { preCodes.Add(ps.Codes[i]); cbPre.Items.Add(ps.Labels[i]); }
            if (m.Preamp >= 0 && !preCodes.Contains(m.Preamp))
            {
                // Réglage mémorisé pour un autre poste : gardé tel quel, avec le réglage que recevra le poste actuel.
                preCodes.Add(m.Preamp);
                cbPre.Items.Add(Preamps.GenericLabel(m.Preamp) + Lang.F(" (ici : {0})", " (here: {0})", ps.LabelOf(ps.Map(m.Preamp))));
            }
            cbPre.SelectedIndex = Math.Max(0, preCodes.IndexOf(m.Preamp));
            new ToolTip().SetToolTip(cbPre, Lang.T("Réglage du préampli du poste appliqué en rappelant cette mémoire (après la fréquence)",
                "Rig preamp setting applied when this memory is recalled (after the frequency)"));
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
            // Puissance (1.42) : rien par défaut ; plafond commun à toutes les mémoires ; envoyée seulement en réception (RigWorker).
            Label l6 = new Label { Text = Lang.T("Puissance :", "Power:"), Location = new Point(12, 186), AutoSize = true };
            chkPwr.Text = Lang.T("régler à", "set to");
            chkPwr.SetBounds(130, 184, 80, 23);
            nuPwr.SetBounds(212, 183, 60, 23);
            Label lw = new Label { Text = "W", Location = new Point(276, 186), AutoSize = true };
            Label l7 = new Label { Text = Lang.T("Plafond :", "Ceiling:"), Location = new Point(320, 186), AutoSize = true };
            nuLimit.SetBounds(384, 183, 60, 23);
            Label lw2 = new Label { Text = "W", Location = new Point(448, 186), AutoSize = true };
            nuLimit.Minimum = 1; nuLimit.Maximum = Powers.LimitMax;
            nuLimit.Value = Math.Max(1, Math.Min(Powers.LimitMax, Powers.Limit));
            nuPwr.Minimum = 0;
            nuPwr.Maximum = nuLimit.Value;
            chkPwr.Checked = m.Power >= 0;
            nuPwr.Value = Math.Max(0, Math.Min(nuPwr.Maximum, m.Power >= 0 ? m.Power : Math.Min(10, (int)nuLimit.Value)));
            nuPwr.Enabled = chkPwr.Checked;
            chkPwr.CheckedChanged += delegate { nuPwr.Enabled = chkPwr.Checked; };
            nuLimit.ValueChanged += delegate { nuPwr.Maximum = nuLimit.Value; };
            PowerRange pr = Powers.Current;
            Label lWarn = new Label { AutoSize = false };
            lWarn.SetBounds(12, 214, 456, 62);
            lWarn.Text = Lang.T("Puissance d'émission envoyée en rappelant la mémoire, jamais pendant l'émission. Le plafond vaut pour toutes les mémoires : réglez-le selon votre amplificateur et votre antenne.",
                "Transmit power sent when the memory is recalled, never while transmitting. The ceiling applies to every memory: set it for your amplifier and antenna.")
                + (pr != null ? " " + Lang.F("Ce poste : {0} à {1} W.", "This rig: {0} to {1} W.", pr.MinW, pr.MaxW) : "");
            ToolTip tp = new ToolTip();
            tp.SetToolTip(chkPwr, Lang.T("Sans coche, la puissance du poste n'est pas touchée", "Unticked, the rig power is left alone"));
            tp.SetToolTip(nuLimit, Lang.T("Aucune mémoire n'enverra plus que ce plafond (toutes les mémoires)", "No memory will send more than this ceiling (all memories)"));
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = Lang.T("Annuler", "Cancel"), DialogResult = DialogResult.Cancel };
            ok.SetBounds(300, 284, 80, 28);
            cancel.SetBounds(386, 284, 80, 28);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { l1, l2, l3, l4, l5, txtName, txtFreq, cbMode, cbList, cbPre, l6, chkPwr, nuPwr, lw, l7, nuLimit, lw2, lWarn, ok, cancel });
            if (preampOnly) { txtName.Enabled = txtFreq.Enabled = cbMode.Enabled = cbList.Enabled = false; ActiveControl = cbPre; }
            Theme.Apply(this);
            // Ligne « Puissance » placée selon la longueur des textes (langues).
            chkPwr.Width = chkPwr.PreferredSize.Width + 2;
            nuPwr.Left = chkPwr.Right + 2;
            lw.Left = nuPwr.Right + 4;
            l7.Left = Math.Max(320, lw.Right + 18);
            nuLimit.Left = l7.Left + l7.PreferredWidth + 4;
            lw2.Left = nuLimit.Right + 4;
            if (lw2.Right + 14 > ClientSize.Width) ClientSize = new Size(lw2.Right + 14, ClientSize.Height);
            ok.Left = ClientSize.Width - 180; cancel.Left = ClientSize.Width - 94; lWarn.Width = ClientSize.Width - 24;
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
                Result.Preamp = preCodes[Math.Max(0, cbPre.SelectedIndex)];
                Result.Scan = m.Scan;
                Result.Power = chkPwr.Checked ? (int)nuPwr.Value : -1;
                Powers.Limit = (int)nuLimit.Value;
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
        readonly Button btnAddList, btnRemoveList, btnEdit, btnDel;
        readonly ComboBox cbDigi = new ComboBox(), cbRegion = new ComboBox();
        readonly Func<string, string> digiName;
        bool filling;

        // Liste « Modes numériques » (après « Toutes les listes ») : fréquences habituelles du mode choisi, région IARU choisie.
        public int IaruRegion = 1;
        int AllLists { get { return book.ListCount; } }
        int DigiList { get { return book.ListCount + 1; } }
        int ShownList { get { return Math.Max(0, cbList.SelectedIndex); } }
        bool Digital { get { return ShownList == DigiList; } }
        string DigiMode { get { return cbDigi.SelectedIndex >= 0 ? DigitalFreqs.ModesShown[cbDigi.SelectedIndex] : "FT8"; } }

        public MemoriesDialog(MemoryBook book, Func<Memory> current, Action<Memory> tune)
            : this(book, current, tune, null, 1, false, null) { }

        // digiMode : mode numérique utilisé (la liste « Modes numériques » le montre d'abord) ; openDigital : ouvrir sur cette liste
        // (décodage en marche) ; digiName : nom affiché d'un mode (PSK31 / PSK63).
        public MemoriesDialog(MemoryBook book, Func<Memory> current, Action<Memory> tune, string digiMode, int region, bool openDigital, Func<string, string> digiName)
        {
            this.book = book;
            this.current = current;
            this.tune = tune;
            this.digiName = digiName ?? delegate(string m) { return m; };
            IaruRegion = Math.Max(1, Math.Min(3, region));
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
            Button edit = btnEdit = Make(Lang.T("Modifier…", "Edit…"), 250, 12, 110, 32, delegate { EditSelected(); });
            Button del = btnDel = Make(Lang.T("Supprimer…", "Remove…"), 366, 12, 110, 32, delegate { RemoveSelected(); });

            Label lList = new Label { Text = Lang.T("Liste :", "List:"), Location = new Point(12, 60), AutoSize = true };
            cbList.DropDownStyle = ComboBoxStyle.DropDownList;
            cbList.SetBounds(70, 56, 200, 23);
            ContextMenuStrip listMenu = new ContextMenuStrip();
            ToolStripMenuItem rename = new ToolStripMenuItem();
            listMenu.Items.Add(rename);
            listMenu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (ShownList >= AllLists) { e.Cancel = true; return; }
                rename.Text = Lang.F("Renommer « {0} »…", "Rename \"{0}\"…", FavoritesDialog.ListLabel(book.ListNames, ShownList));
            };
            rename.Click += delegate { RenameList(); };
            cbList.ContextMenuStrip = listMenu;
            new ToolTip().SetToolTip(cbList, Lang.T("(clic droit sur la liste pour la renommer)", "(right-click the list to rename it)"));
            btnAddList = Make(Lang.T("Nouvelle liste", "New list"), 280, 54, 120, 27, delegate { AddList(); });
            btnRemoveList = Make(Lang.T("Supprimer la liste…", "Delete list…"), 406, 54, 132, 27, delegate { RemoveList(); });
            // Liste « Modes numériques » : mode et région à la place des boutons de liste.
            cbDigi.DropDownStyle = cbRegion.DropDownStyle = ComboBoxStyle.DropDownList;
            cbDigi.SetBounds(280, 56, 110, 23);
            cbRegion.SetBounds(398, 56, 210, 23);
            cbRegion.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            foreach (string m in DigitalFreqs.ModesShown) cbDigi.Items.Add(this.digiName(m));
            int di = Array.IndexOf(DigitalFreqs.ModesShown, digiMode ?? "");
            cbDigi.SelectedIndex = di >= 0 ? di : 0;
            for (int r = 1; r <= 3; r++) cbRegion.Items.Add(DigitalFreqs.RegionName(r));
            cbRegion.SelectedIndex = IaruRegion - 1;
            cbDigi.SelectedIndexChanged += delegate { if (!filling) Refill(null); };
            cbRegion.SelectedIndexChanged += delegate { IaruRegion = cbRegion.SelectedIndex + 1; if (!filling) Refill(null); };
            new ToolTip().SetToolTip(cbDigi, Lang.T("Mode numérique dont on voit les fréquences habituelles", "Digital mode whose usual frequencies are shown"));

            view.SetBounds(12, 90, 596, 382);
            view.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            view.View = View.Details;
            view.FullRowSelect = true;
            view.HideSelection = false;
            view.MultiSelect = false;
            view.Columns.Add(Lang.T("Nom", "Name"), 160);
            view.Columns.Add(Lang.T("Fréquence (kHz)", "Frequency (kHz)"), 110);
            view.Columns.Add(Lang.T("Mode", "Mode"), 80);
            view.Columns.Add(Lang.T("Préampli", "Preamp"), 90);
            view.Columns.Add(Lang.T("Puissance", "Power"), 70);
            view.Columns.Add(Lang.T("Liste", "List"), 90);
            // Un clic sur une mémoire change la fréquence tout de suite.
            view.MouseClick += delegate { TuneSelected(); };
            view.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { TuneSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete && !Digital) RemoveSelected();
            };

            lblStatus.SetBounds(12, 482, 500, 30);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Button close = Make(Lang.T("Fermer", "Close"), 528, 480, 80, 30, delegate { Close(); });
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = close;

            Controls.AddRange(new Control[] { add, edit, del, lList, cbList, btnAddList, btnRemoveList, cbDigi, cbRegion, view, lblStatus, close });
            FillListCombo(openDigital ? DigiList : Math.Min(book.CurrentList, book.ListCount - 1));
            cbList.SelectedIndexChanged += delegate
            {
                if (filling) return;
                if (ShownList < AllLists) book.CurrentList = ShownList;
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
            cbList.Items.Add(Lang.T("Modes numériques", "Digital modes"));
            cbList.SelectedIndex = Math.Max(0, Math.Min(cbList.Items.Count - 1, select));
            filling = false;
            btnAddList.Enabled = book.ListCount < Memory.MaxLists;
            btnRemoveList.Enabled = ShownList < AllLists && book.ListCount > 1;
        }

        void Refill(Memory select)
        {
            bool dig = Digital;
            btnRemoveList.Enabled = ShownList < AllLists && book.ListCount > 1;
            btnAddList.Visible = btnRemoveList.Visible = !dig;
            cbDigi.Visible = cbRegion.Visible = dig;
            // « Modes numériques » : Modifier… change seulement le préampli (1.41) et la puissance (1.42) de la fréquence, Copier… prend la place de Supprimer….
            btnEdit.Text = Lang.T("Modifier…", "Edit…");
            btnDel.Text = dig ? Lang.T("Copier…", "Copy…") : Lang.T("Supprimer…", "Remove…");
            view.BeginUpdate();
            view.Items.Clear();
            if (dig)
            {
                foreach (Memory m in book.WithDigiPreamps(DigitalFreqs.List(DigiMode, IaruRegion, digiName(DigiMode))))
                {
                    ListViewItem it = new ListViewItem(new[] { m.Name, Memory.KHz(m.Hz), Modes.Display(m.Mode), PreampText(m.Preamp), Powers.Text(m.Power), Lang.T("Modes numériques", "Digital modes") });
                    it.Tag = m;
                    view.Items.Add(it);
                    if (select != null && select.Hz == m.Hz && select.Mode == m.Mode) it.Selected = true;
                }
                view.EndUpdate();
                lblStatus.Text = Lang.F("Fréquences habituelles de {0}, {1}. Cliquez pour y aller (Modifier… : son préampli et sa puissance ; Copier… : l'ajouter à une de vos listes).",
                    "Usual {0} frequencies, {1}. Click to go there (Edit…: its preamp and power; Copy…: add it to one of your lists).", digiName(DigiMode), DigitalFreqs.RegionName(IaruRegion));
                return;
            }
            foreach (Memory m in book.Items)
            {
                if (ShownList != AllLists && m.List != ShownList) continue;
                ListViewItem it = new ListViewItem(new[] { m.Name, Memory.KHz(m.Hz), m.Mode == "" ? "—" : Modes.Display(m.Mode), PreampText(m.Preamp), Powers.Text(m.Power), FavoritesDialog.ListLabel(book.ListNames, m.List) });
                it.Tag = m;
                view.Items.Add(it);
                if (m == select) it.Selected = true;
            }
            view.EndUpdate();
            lblStatus.Text = Lang.F("{0} mémoire(s). Cliquez sur une mémoire pour y aller.", "{0} memory(ies). Click a memory to go there.", view.Items.Count);
        }

        static string PreampText(int code)
        {
            if (code < 0) return "—";
            PreampSet ps = Preamps.Current;
            return ps != null ? ps.LabelOf(ps.Map(code)) : Preamps.GenericLabel(code);
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
            m.List = ShownList >= AllLists ? book.CurrentList : ShownList;
            using (MemoryEditDialog d = new MemoryEditDialog(Lang.T("Ajouter une mémoire", "Add memory"), m, book))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                book.Items.Add(d.Result);
                if (ShownList != AllLists && d.Result.List != ShownList) FillListCombo(d.Result.List);
                Refill(d.Result);
            }
        }

        // Liste « Modes numériques » : la fréquence choisie est copiée dans une des listes de l'utilisateur.
        void CopySelected(Memory m)
        {
            Memory c = new Memory(m.Name, m.Hz, m.Mode) { Preamp = m.Preamp, Power = m.Power };
            c.List = Math.Min(book.CurrentList, book.ListCount - 1);
            using (MemoryEditDialog d = new MemoryEditDialog(Lang.T("Ajouter une mémoire", "Add memory"), c, book))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                book.Items.Add(d.Result);
                lblStatus.Text = Lang.F("Mémoire « {0} » ajoutée à « {1} ».", "Memory \"{0}\" added to \"{1}\".", d.Result.Label,
                    FavoritesDialog.ListLabel(book.ListNames, d.Result.List));
            }
        }

        void EditSelected()
        {
            Memory m = Selected;
            if (m == null) { lblStatus.Text = Lang.T("Sélectionnez d'abord une mémoire.", "Select a memory first."); return; }
            if (Digital) { EditDigiPreamp(m); return; }
            using (MemoryEditDialog d = new MemoryEditDialog(Lang.T("Modifier la mémoire", "Edit memory"), m, book))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                m.Name = d.Result.Name; m.Hz = d.Result.Hz; m.Mode = d.Result.Mode; m.List = d.Result.List; m.Preamp = d.Result.Preamp; m.Power = d.Result.Power;
                if (ShownList != AllLists && m.List != ShownList) FillListCombo(m.List);
                Refill(m);
            }
        }

        // Fréquence « Modes numériques » : seuls son préampli et sa puissance se changent ; gardés pour cette fréquence et ce mode
        // (MemDigiPre, MemDigiPwr).
        void EditDigiPreamp(Memory m)
        {
            using (MemoryEditDialog d = new MemoryEditDialog(Lang.T("Préampli et puissance de la fréquence", "Preamp and power of the frequency"), m, book, true))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (d.Result.Preamp < 0) book.DigiPreamps.Remove(MemoryBook.DigiKey(m));
                else book.DigiPreamps[MemoryBook.DigiKey(m)] = d.Result.Preamp;
                if (d.Result.Power < 0) book.DigiPowers.Remove(MemoryBook.DigiKey(m));
                else book.DigiPowers[MemoryBook.DigiKey(m)] = d.Result.Power;
                Refill(m);
            }
        }

        void RemoveSelected()
        {
            Memory m = Selected;
            if (m == null) { lblStatus.Text = Lang.T("Sélectionnez d'abord une mémoire.", "Select a memory first."); return; }
            if (Digital) { CopySelected(m); return; }
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
            if (i >= AllLists || book.ListCount <= 1) return;
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
            if (i >= AllLists) return;
            string name = FavoritesDialog.AskListName(this, book.ListNames[i]);
            if (string.IsNullOrEmpty(name)) return;
            book.ListNames[i] = name.Replace("|", "/");
            FillListCombo(i);
            Refill(null);
        }
    }
}
