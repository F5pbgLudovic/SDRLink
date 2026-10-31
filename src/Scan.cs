using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SDRLink
{
    // Réglage complet d'un scan : type, fréquences (début, fin, pas, durée, zones non scannées), mémoires cochées et durée.
    // Sert au réglage du moment (Settings) et aux profils de scan nommés (10 au plus).
    public class ScanProfile
    {
        public const int MaxProfiles = 10, MaxSkips = 10;
        public string Name = "";
        public int Type;                    // 0 fréquences, 1 mémoires
        public long StartHz, EndHz, StepHz = 1000;
        public int FreqDwell = 5, MemDwell = 5;
        public List<long[]> Skips = new List<long[]>();   // zones non scannées { début, fin } en Hz, début <= fin
        public List<string> Mems = new List<string>();    // mémoires cochées (MemKey)

        public long Lo { get { return Math.Min(StartHz, EndHz); } }
        public long Hi { get { return Math.Max(StartHz, EndHz); } }

        public static string MemKey(Memory m)
        {
            return m.Hz.ToString(CultureInfo.InvariantCulture) + ":" + Uri.EscapeDataString(m.Mode) + ":" + Uri.EscapeDataString(m.Name);
        }

        // Les bornes d'une zone sont scannées : zone 96 – 103 → le scan s'arrête sur 96 puis repart à 103.
        public bool Skipped(long hz)
        {
            foreach (long[] z in Skips) if (hz > z[0] && hz < z[1]) return true;
            return false;
        }

        // Fréquence suivante du scan (la première si hz < 0), en sautant les zones non scannées ; après la fin, on
        // recommence au début. -1 si toutes les fréquences sont dans des zones non scannées.
        public long NextFreq(long hz)
        {
            long lo = Lo, step = Math.Max(1, StepHz), n = (Hi - lo) / step + 1;
            long pos = hz < lo ? -1 : (hz - lo) / step;
            for (long i = 0; i < n; i++)
            {
                pos = (pos + 1) % n;
                long f = lo + pos * step;
                if (!Skipped(f)) return f;
            }
            return -1;
        }

        public static string SkipText(long[] z)
        {
            return z[0].ToString(CultureInfo.InvariantCulture) + "-" + z[1].ToString(CultureInfo.InvariantCulture);
        }

        public static long[] ParseSkip(string v)
        {
            int d = v.Length > 2 ? v.IndexOf('-', 1) : -1;
            long a, b;
            if (d < 0 || !long.TryParse(v.Substring(0, d), NumberStyles.Integer, CultureInfo.InvariantCulture, out a)
                || !long.TryParse(v.Substring(d + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out b) || a <= 0 || b <= 0) return null;
            return new long[] { Math.Min(a, b), Math.Max(a, b) };
        }

        // Nom|type|début|fin|pas|durée fréquences|durée mémoires|zones (début-fin;…)|mémoires (clé;…)
        public string ToSetting()
        {
            List<string> z = Skips.ConvertAll(SkipText);
            return Name.Replace("|", "/") + "|" + Type + "|" + StartHz.ToString(CultureInfo.InvariantCulture) + "|" + EndHz.ToString(CultureInfo.InvariantCulture)
                + "|" + StepHz.ToString(CultureInfo.InvariantCulture) + "|" + FreqDwell + "|" + MemDwell
                + "|" + string.Join(";", z.ToArray()) + "|" + string.Join(";", Mems.ToArray());
        }

        public static ScanProfile FromSetting(string v)
        {
            string[] p = v.Split('|');
            if (p.Length < 7 || p[0].Trim().Length == 0) return null;
            ScanProfile s = new ScanProfile { Name = p[0].Trim() };
            int i; long l;
            s.Type = int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out i) && i == 1 ? 1 : 0;
            if (long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out l) && l > 0) s.StartHz = l;
            if (long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out l) && l > 0) s.EndHz = l;
            if (long.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out l) && l > 0) s.StepHz = l;
            if (int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out i) && i > 0) s.FreqDwell = i;
            if (int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out i) && i > 0) s.MemDwell = i;
            if (p.Length > 7)
                foreach (string z in p[7].Split(';'))
                {
                    long[] r = ParseSkip(z.Trim());
                    if (r != null && s.Skips.Count < MaxSkips) s.Skips.Add(r);
                }
            if (p.Length > 8)
                foreach (string m in p[8].Split(';')) if (m.Length > 0) s.Mems.Add(m);
            return s;
        }

        // Résumé affiché dans le menu du bouton Scan.
        public string Summary()
        {
            if (Type == 1) return Lang.F("mémoires : {0}, {1}", "memories: {0}, {1}", Mems.Count, ScanDialog.DwellText(MemDwell));
            if (Lo <= 0 || Lo == Hi) return Lang.T("fréquences non réglées", "frequencies not set");
            string s = Memory.KHz(Lo) + " – " + Memory.KHz(Hi) + " kHz, " + ScanDialog.StepText(StepHz) + ", " + ScanDialog.DwellText(FreqDwell);
            if (Skips.Count > 0) s += ", " + Lang.F("zones non scannées : {0}", "skipped ranges: {0}", Skips.Count);
            return s;
        }
    }

    // Fenêtre « Conf scan » : scan de fréquences (début, fin, pas, durée, jusqu'à 10 zones non scannées) ou de mémoires
    // (mémoires cochées, durée). Chaque type garde ses réglages quand on passe à l'autre ; Réinitialiser remet à zéro le
    // type affiché. En haut, les profils de scan : choisir un profil le charge, Enregistrer garde les réglages affichés
    // sous le nom tapé (10 profils au plus). OK enregistre ; « Lancer le scan » enregistre et démarre.
    public class ScanDialog : Form
    {
        // Durées proposées, en secondes : 1 s, 2 s, 5 s, 10 s, 30 s, 1 min, 2 min, 5 min, 10 min.
        public static readonly int[] Dwells = { 1, 2, 5, 10, 30, 60, 120, 300, 600 };
        public static readonly long[] Steps = { 100, 250, 500, 1000, 2500, 5000, 6250, 9000, 10000, 12500, 25000, 100000 };
        const int DefaultDwell = 5;
        const long DefaultStep = 1000;
        const int W = 540, PanelTop = 84, MemPanelH = 330, ZoneTop = 176, ZoneH = 30;

        readonly Settings settings;
        readonly ComboBox cbProfile = new ComboBox();
        readonly RadioButton rbFreq = new RadioButton(), rbMem = new RadioButton();
        readonly Panel pnlFreq = new Panel(), pnlMem = new Panel();
        readonly TextBox txtStart = new TextBox(), txtEnd = new TextBox();
        readonly ComboBox cbStep = new ComboBox(), cbDwellF = new ComboBox(), cbDwellM = new ComboBox();
        readonly CheckedListBox clbMem = new CheckedListBox();
        readonly Label lblZones = new Label();
        readonly Button btnAddZone = new Button();
        readonly List<Panel> zoneRows = new List<Panel>();
        readonly Button reset, start, ok, cancel;
        readonly List<Memory> mems;
        bool filling;
        int lastLoaded = -1;   // profil choisi en dernier dans la liste (Renommer, après avoir changé le nom dans la case)

        public static string DwellText(int sec)
        {
            return sec < 60 ? Lang.F("{0} s", "{0} s", sec) : Lang.F("{0} min", "{0} min", sec / 60);
        }

        public static string StepText(long hz)
        {
            return hz < 1000 ? hz + " Hz" : (hz / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " kHz";
        }

        public ScanDialog(Settings settings)
        {
            this.settings = settings;
            mems = settings.Memories.Items;
            Text = Lang.T("Conf scan", "Scan setup");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(W, 470);

            // Profils de scan : liste modifiable (choisir un nom charge le profil, taper un nom puis Enregistrer en crée un).
            Label lProfile = new Label { Text = Lang.T("Profil :", "Profile:"), Location = new Point(12, 16), AutoSize = true };
            cbProfile.DropDownStyle = ComboBoxStyle.DropDown;
            cbProfile.MaxLength = 40;
            cbProfile.SelectedIndexChanged += delegate { if (!filling && cbProfile.SelectedIndex >= 0) { lastLoaded = cbProfile.SelectedIndex; LoadProfile(settings.ScanProfiles[lastLoaded]); } };
            Button save = Make(Lang.T("Enregistrer", "Save"), 0, 11, 0, 26, delegate { SaveProfile(); });
            Button rename = Make(Lang.T("Renommer", "Rename"), 0, 11, 0, 26, delegate { RenameProfile(); });
            Button delete = Make(Lang.T("Supprimer", "Delete"), 0, 11, 0, 26, delegate { DeleteProfile(); });
            // Boutons à la largeur de leur texte (selon la langue), alignés à droite ; la liste prend la place restante.
            int bx = W - 10;
            foreach (Button b in new[] { delete, rename, save })
            {
                b.Width = Math.Max(72, TextRenderer.MeasureText(b.Text, Font).Width + 18);
                bx -= b.Width;
                b.Left = bx;
                bx -= 4;
            }
            cbProfile.SetBounds(130, 12, Math.Max(100, bx - 130 - 4), 23);
            FillProfiles(null);

            Label lType = new Label { Text = Lang.T("Type de scan :", "Scan type:"), Location = new Point(12, 54), AutoSize = true };
            rbFreq.Text = Lang.T("Fréquences", "Frequencies");
            rbMem.Text = Lang.T("Mémoires", "Memories");
            rbFreq.SetBounds(130, 50, 130, 24);
            rbMem.SetBounds(270, 50, 130, 24);
            rbFreq.CheckedChanged += delegate { ShowType(); };

            // Fréquences : début, fin (kHz), pas, durée sur chaque fréquence, zones non scannées (affichées seulement si définies).
            pnlFreq.SetBounds(0, PanelTop, W, 200);
            pnlFreq.Controls.Add(new Label { Text = Lang.T("Début (kHz) :", "Start (kHz):"), Location = new Point(12, 14), AutoSize = true });
            pnlFreq.Controls.Add(new Label { Text = Lang.T("Fin (kHz) :", "End (kHz):"), Location = new Point(12, 48), AutoSize = true });
            pnlFreq.Controls.Add(new Label { Text = Lang.T("Pas :", "Step:"), Location = new Point(12, 82), AutoSize = true });
            pnlFreq.Controls.Add(new Label { Text = Lang.T("Durée sur chaque fréquence :", "Time on each frequency:"), Location = new Point(12, 116), AutoSize = true });
            txtStart.SetBounds(210, 10, 120, 23);
            txtEnd.SetBounds(210, 44, 120, 23);
            cbStep.DropDownStyle = cbDwellF.DropDownStyle = cbDwellM.DropDownStyle = ComboBoxStyle.DropDownList;
            cbStep.SetBounds(210, 78, 120, 23);
            cbDwellF.SetBounds(210, 112, 120, 23);
            foreach (long s in Steps) cbStep.Items.Add(StepText(s));
            foreach (int d in Dwells) { cbDwellF.Items.Add(DwellText(d)); cbDwellM.Items.Add(DwellText(d)); }
            lblZones.Text = Lang.T("Zones non scannées (de … à … kHz) :", "Skipped ranges (from … to … kHz):");
            lblZones.Location = new Point(12, 152);
            lblZones.AutoSize = true;
            btnAddZone.Text = Lang.T("+ Zone non scannée", "+ Skipped range");
            btnAddZone.Click += delegate { AddZone("", ""); LayoutZones(); zoneRows[zoneRows.Count - 1].Controls[1].Focus(); };
            pnlFreq.Controls.AddRange(new Control[] { txtStart, txtEnd, cbStep, cbDwellF, lblZones, btnAddZone });

            // Mémoires : liste à cocher, durée sur chaque mémoire.
            pnlMem.SetBounds(0, PanelTop, W, MemPanelH);
            pnlMem.Controls.Add(new Label { Text = Lang.T("Mémoires à parcourir :", "Memories to scan:"), Location = new Point(12, 8), AutoSize = true });
            clbMem.SetBounds(12, 30, W - 24, 220);
            clbMem.CheckOnClick = true;
            clbMem.IntegralHeight = false;
            foreach (Memory m in mems)
                clbMem.Items.Add(m.Label + "   —   " + Memory.KHz(m.Hz) + " kHz" + (m.Mode != "" ? " " + Modes.Display(m.Mode) : "")
                    + "   (" + FavoritesDialog.ListLabel(settings.Memories.ListNames, m.List) + ")");
            Button all = Make(Lang.T("Tout cocher", "Check all"), 12, 256, 110, 28, delegate { SetAll(true); });
            Button none = Make(Lang.T("Tout décocher", "Uncheck all"), 128, 256, 110, 28, delegate { SetAll(false); });
            pnlMem.Controls.Add(new Label { Text = Lang.T("Durée sur chaque mémoire :", "Time on each memory:"), Location = new Point(12, 298), AutoSize = true });
            cbDwellM.SetBounds(210, 294, 120, 23);
            pnlMem.Controls.AddRange(new Control[] { clbMem, all, none, cbDwellM });
            if (mems.Count == 0)
                pnlMem.Controls.Add(new Label { Text = Lang.T("(aucune mémoire : bouton Mémoires)", "(no memory: Memories button)"), Location = new Point(250, 262), AutoSize = true });

            reset = Make(Lang.T("Réinitialiser", "Reset"), 12, 0, 110, 30, delegate { ResetShown(); });
            start = Make(Lang.T("Lancer le scan", "Start scan"), 230, 0, 120, 30, null);
            start.DialogResult = DialogResult.Yes;
            ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            cancel = new Button { Text = Lang.T("Annuler", "Cancel"), DialogResult = DialogResult.Cancel };
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { lProfile, cbProfile, save, rename, delete, lType, rbFreq, rbMem, pnlFreq, pnlMem, reset, start, ok, cancel });

            // Réglages enregistrés.
            LoadProfile(settings.CurrentScan(""));
            Theme.Apply(this);

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (DialogResult != DialogResult.OK && DialogResult != DialogResult.Yes) return;
                ScanProfile p;
                string error = CheckInput(DialogResult == DialogResult.Yes, out p);
                if (error != null) { MessageBox.Show(this, error, "SDRLink"); e.Cancel = true; return; }
                settings.UseScan(p);
            };
        }

        static int DwellIndex(int sec)
        {
            int i = Array.IndexOf(Dwells, sec);
            return i >= 0 ? i : Array.IndexOf(Dwells, DefaultDwell);
        }

        static Button Make(string text, int x, int y, int w, int h, EventHandler click)
        {
            Button b = new Button { Text = text };
            b.SetBounds(x, y, w, h);
            if (click != null) b.Click += click;
            return b;
        }

        void ShowType()
        {
            pnlFreq.Visible = rbFreq.Checked;
            pnlMem.Visible = !rbFreq.Checked;
            LayoutZones();
        }

        void SetAll(bool on)
        {
            for (int i = 0; i < clbMem.Items.Count; i++) clbMem.SetItemChecked(i, on);
        }

        // ------------------------------------------------ zones non scannées

        void AddZone(string from, string to)
        {
            if (zoneRows.Count >= ScanProfile.MaxSkips) return;
            Panel row = new Panel { Size = new Size(W - 24, ZoneH) };
            Label lFrom = new Label { Text = Lang.T("de", "from"), Location = new Point(0, 6), Size = new Size(40, 20), TextAlign = ContentAlignment.MiddleRight };
            TextBox a = new TextBox { Text = from };
            a.SetBounds(46, 3, 100, 23);
            Label lTo = new Label { Text = Lang.T("à", "to"), Location = new Point(150, 6), Size = new Size(30, 20), TextAlign = ContentAlignment.MiddleCenter };
            TextBox b = new TextBox { Text = to };
            b.SetBounds(184, 3, 100, 23);
            Label kHz = new Label { Text = "kHz", Location = new Point(290, 6), AutoSize = true };
            Button del = new Button { Text = "✕" };
            del.SetBounds(332, 2, 30, 25);
            del.Click += delegate { zoneRows.Remove(row); pnlFreq.Controls.Remove(row); row.Dispose(); LayoutZones(); };
            row.Controls.AddRange(new Control[] { lFrom, a, lTo, b, kHz, del });
            zoneRows.Add(row);
            pnlFreq.Controls.Add(row);
            if (IsHandleCreated) Theme.Apply(row);
        }

        void ClearZones()
        {
            foreach (Panel r in zoneRows) { pnlFreq.Controls.Remove(r); r.Dispose(); }
            zoneRows.Clear();
        }

        // Place les zones sous la durée, puis le bouton d'ajout, et adapte la hauteur de la fenêtre au type affiché.
        void LayoutZones()
        {
            lblZones.Visible = zoneRows.Count > 0;
            int y = zoneRows.Count > 0 ? ZoneTop : 150;
            foreach (Panel r in zoneRows) { r.Location = new Point(12, y); y += ZoneH; }
            bool canAdd = zoneRows.Count < ScanProfile.MaxSkips;   // (Visible reste faux tant que la fenêtre n'est pas affichée)
            btnAddZone.Visible = canAdd;
            btnAddZone.SetBounds(12, y + 4, 200, 28);
            int freqH = y + (canAdd ? 40 : 8);
            pnlFreq.Height = freqH;
            int bottom = PanelTop + (rbFreq.Checked ? freqH : MemPanelH) + 14;
            reset.Top = start.Top = bottom;
            ok.SetBounds(W - 182, bottom, 80, 30);
            cancel.SetBounds(W - 94, bottom, 80, 30);
            ClientSize = new Size(W, bottom + 42);
        }

        // ------------------------------------------------ profils

        void FillProfiles(string select)
        {
            filling = true;
            cbProfile.Items.Clear();
            foreach (ScanProfile p in settings.ScanProfiles) cbProfile.Items.Add(p.Name);
            cbProfile.Text = select ?? "";
            filling = false;
        }

        int ProfileIndex(string name)
        {
            return settings.ScanProfiles.FindIndex(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        }

        // Affiche les réglages d'un profil (ou du réglage du moment).
        public void LoadProfile(ScanProfile p)
        {
            txtStart.Text = p.StartHz > 0 ? Memory.KHz(p.StartHz) : "";
            txtEnd.Text = p.EndHz > 0 ? Memory.KHz(p.EndHz) : "";
            int si = Array.IndexOf(Steps, p.StepHz);
            cbStep.SelectedIndex = si >= 0 ? si : Array.IndexOf(Steps, DefaultStep);
            cbDwellF.SelectedIndex = DwellIndex(p.FreqDwell);
            cbDwellM.SelectedIndex = DwellIndex(p.MemDwell);
            for (int i = 0; i < mems.Count; i++) clbMem.SetItemChecked(i, p.Mems.Contains(ScanProfile.MemKey(mems[i])));
            ClearZones();
            foreach (long[] z in p.Skips) AddZone(Memory.KHz(z[0]), Memory.KHz(z[1]));
            rbFreq.Checked = p.Type == 0;
            rbMem.Checked = p.Type == 1;
            ShowType();
        }

        void SaveProfile()
        {
            string name = cbProfile.Text.Trim().Replace("|", "/");
            if (name.Length == 0) { MessageBox.Show(this, Lang.T("Tapez d'abord un nom de profil dans la case « Profil ».", "First type a profile name in the \"Profile\" box."), "SDRLink"); return; }
            ScanProfile p;
            string error = CheckInput(false, out p);
            if (error != null) { MessageBox.Show(this, error, "SDRLink"); return; }
            p.Name = name;
            int i = ProfileIndex(name);
            if (i >= 0)
            {
                if (MessageBox.Show(this, Lang.F("Remplacer le profil « {0} » par les réglages affichés ?", "Replace the profile \"{0}\" with the settings shown?", settings.ScanProfiles[i].Name),
                        "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                settings.ScanProfiles[i] = p;
            }
            else if (settings.ScanProfiles.Count >= ScanProfile.MaxProfiles)
            {
                MessageBox.Show(this, Lang.F("{0} profils au plus : supprimez-en un d'abord.", "{0} profiles at most: delete one first.", ScanProfile.MaxProfiles), "SDRLink");
                return;
            }
            else settings.ScanProfiles.Add(p);
            FillProfiles(name);
            lastLoaded = ProfileIndex(name);
        }

        void RenameProfile()
        {
            string name = cbProfile.Text.Trim().Replace("|", "/");
            int i = lastLoaded;
            if (i < 0 || i >= settings.ScanProfiles.Count)
            {
                MessageBox.Show(this, Lang.T("Choisissez d'abord un profil dans la liste, puis tapez son nouveau nom dans la case et cliquez sur Renommer.",
                    "First choose a profile in the list, then type its new name in the box and click Rename."), "SDRLink");
                return;
            }
            if (name.Length == 0) return;
            int other = ProfileIndex(name);
            if (other >= 0 && other != i) { MessageBox.Show(this, Lang.F("Le profil « {0} » existe déjà.", "The profile \"{0}\" already exists.", name), "SDRLink"); return; }
            settings.ScanProfiles[i].Name = name;
            FillProfiles(name);
            lastLoaded = i;
        }

        void DeleteProfile()
        {
            int i = ProfileIndex(cbProfile.Text.Trim());
            if (i < 0) { MessageBox.Show(this, Lang.T("Choisissez dans la liste le profil à supprimer.", "Choose in the list the profile to delete."), "SDRLink"); return; }
            if (MessageBox.Show(this, Lang.F("Supprimer le profil « {0} » ?", "Delete the profile \"{0}\"?", settings.ScanProfiles[i].Name),
                    "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            settings.ScanProfiles.RemoveAt(i);
            FillProfiles(null);
            lastLoaded = -1;
        }


        // Remet à zéro les choix du type affiché (l'autre type garde les siens).
        public void ResetShown()
        {
            if (rbFreq.Checked)
            {
                txtStart.Text = txtEnd.Text = "";
                cbStep.SelectedIndex = Array.IndexOf(Steps, DefaultStep);
                cbDwellF.SelectedIndex = DwellIndex(DefaultDwell);
                ClearZones();
                LayoutZones();
            }
            else
            {
                SetAll(false);
                cbDwellM.SelectedIndex = DwellIndex(DefaultDwell);
            }
        }

        // null si correct, avec les réglages affichés dans p. Pour lancer le scan, le type choisi doit être complet.
        string CheckInput(bool starting, out ScanProfile p)
        {
            p = new ScanProfile();
            p.Type = rbMem.Checked ? 1 : 0;
            long a = MemoryEditDialog.ParseKHz(txtStart.Text), b = MemoryEditDialog.ParseKHz(txtEnd.Text);
            bool freqEmpty = txtStart.Text.Trim() == "" && txtEnd.Text.Trim() == "";
            if (rbFreq.Checked && (starting || !freqEmpty) && (a <= 0 || b <= 0 || a == b))
                return Lang.T("Indiquez une fréquence de début et une fréquence de fin différentes, en kHz (par ex. 7000 et 7200).",
                              "Enter different start and end frequencies, in kHz (e.g. 7000 and 7200).");
            p.StartHz = Math.Max(0, a);
            p.EndHz = Math.Max(0, b);
            p.StepHz = Steps[Math.Max(0, cbStep.SelectedIndex)];
            p.FreqDwell = Dwells[Math.Max(0, cbDwellF.SelectedIndex)];
            p.MemDwell = Dwells[Math.Max(0, cbDwellM.SelectedIndex)];
            // Zones non scannées : une ligne vide est ignorée, une ligne à moitié remplie est une erreur.
            for (int i = 0; i < zoneRows.Count; i++)
            {
                string ta = zoneRows[i].Controls[1].Text.Trim(), tb = zoneRows[i].Controls[3].Text.Trim();
                if (ta == "" && tb == "") continue;
                long za = MemoryEditDialog.ParseKHz(ta), zb = MemoryEditDialog.ParseKHz(tb);
                if (za <= 0 || zb <= 0)
                    return Lang.F("Zone non scannée n° {0} : indiquez deux fréquences en kHz (par ex. 7097 et 7103).",
                                  "Skipped range no. {0}: enter two frequencies in kHz (e.g. 7097 and 7103).", i + 1);
                p.Skips.Add(new long[] { Math.Min(za, zb), Math.Max(za, zb) });
            }
            for (int i = 0; i < mems.Count; i++) if (clbMem.GetItemChecked(i)) p.Mems.Add(ScanProfile.MemKey(mems[i]));
            if (rbFreq.Checked && starting && p.NextFreq(-1) < 0)
                return Lang.T("Toutes les fréquences de début à fin sont dans des zones non scannées.", "All frequencies from start to end are in skipped ranges.");
            if (rbMem.Checked && starting && p.Mems.Count == 0)
                return Lang.T("Cochez au moins une mémoire à parcourir.", "Check at least one memory to scan.");
            return null;
        }
    }
}
