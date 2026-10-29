using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SDRLink
{
    // Fenêtre « Conf scan » : scan de fréquences (début, fin, pas, durée) ou de mémoires (mémoires cochées, durée).
    // Chaque type garde ses réglages quand on passe à l'autre ; Réinitialiser remet à zéro le type affiché.
    // OK enregistre ; « Lancer le scan » enregistre et démarre.
    public class ScanDialog : Form
    {
        // Durées proposées, en secondes : 1 s, 2 s, 5 s, 10 s, 30 s, 1 min, 2 min, 5 min, 10 min.
        public static readonly int[] Dwells = { 1, 2, 5, 10, 30, 60, 120, 300, 600 };
        public static readonly long[] Steps = { 100, 250, 500, 1000, 2500, 5000, 6250, 9000, 10000, 12500, 25000, 100000 };
        const int DefaultDwell = 5;
        const long DefaultStep = 1000;

        readonly Settings settings;
        readonly RadioButton rbFreq = new RadioButton(), rbMem = new RadioButton();
        readonly Panel pnlFreq = new Panel(), pnlMem = new Panel();
        readonly TextBox txtStart = new TextBox(), txtEnd = new TextBox();
        readonly ComboBox cbStep = new ComboBox(), cbDwellF = new ComboBox(), cbDwellM = new ComboBox();
        readonly CheckedListBox clbMem = new CheckedListBox();
        readonly List<Memory> mems;

        public static string DwellText(int sec)
        {
            return sec < 60 ? Lang.F("{0} s", "{0} s", sec) : Lang.F("{0} min", "{0} min", sec / 60);
        }

        static string StepText(long hz)
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
            ClientSize = new Size(500, 430);

            Label lType = new Label { Text = Lang.T("Type de scan :", "Scan type:"), Location = new Point(12, 16), AutoSize = true };
            rbFreq.Text = Lang.T("Fréquences", "Frequencies");
            rbMem.Text = Lang.T("Mémoires", "Memories");
            rbFreq.SetBounds(130, 12, 130, 24);
            rbMem.SetBounds(270, 12, 130, 24);
            rbFreq.CheckedChanged += delegate { ShowType(); };

            // Fréquences : début, fin (kHz), pas, durée sur chaque fréquence.
            pnlFreq.SetBounds(0, 44, 500, 330);
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
            pnlFreq.Controls.AddRange(new Control[] { txtStart, txtEnd, cbStep, cbDwellF });

            // Mémoires : liste à cocher, durée sur chaque mémoire.
            pnlMem.SetBounds(0, 44, 500, 330);
            pnlMem.Controls.Add(new Label { Text = Lang.T("Mémoires à parcourir :", "Memories to scan:"), Location = new Point(12, 8), AutoSize = true });
            clbMem.SetBounds(12, 30, 476, 220);
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

            Button reset = Make(Lang.T("Réinitialiser", "Reset"), 12, 388, 110, 30, delegate { ResetShown(); });
            Button start = Make(Lang.T("Lancer le scan", "Start scan"), 190, 388, 120, 30, null);
            start.DialogResult = DialogResult.Yes;
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = Lang.T("Annuler", "Cancel"), DialogResult = DialogResult.Cancel };
            ok.SetBounds(318, 388, 80, 30);
            cancel.SetBounds(406, 388, 80, 30);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { lType, rbFreq, rbMem, pnlFreq, pnlMem, reset, start, ok, cancel });

            // Réglages enregistrés.
            txtStart.Text = settings.ScanStartHz > 0 ? Memory.KHz(settings.ScanStartHz) : "";
            txtEnd.Text = settings.ScanEndHz > 0 ? Memory.KHz(settings.ScanEndHz) : "";
            cbStep.SelectedIndex = Math.Max(0, Array.IndexOf(Steps, settings.ScanStepHz) >= 0 ? Array.IndexOf(Steps, settings.ScanStepHz) : Array.IndexOf(Steps, DefaultStep));
            cbDwellF.SelectedIndex = DwellIndex(settings.ScanFreqDwell);
            cbDwellM.SelectedIndex = DwellIndex(settings.ScanMemDwell);
            for (int i = 0; i < mems.Count; i++) clbMem.SetItemChecked(i, mems[i].Scan);
            rbFreq.Checked = settings.ScanType == 0;
            rbMem.Checked = settings.ScanType == 1;
            ShowType();
            Theme.Apply(this);

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (DialogResult != DialogResult.OK && DialogResult != DialogResult.Yes) return;
                string error = CheckInput(DialogResult == DialogResult.Yes);
                if (error != null) { MessageBox.Show(this, error, "SDRLink"); e.Cancel = true; return; }
                Store();
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
        }

        void SetAll(bool on)
        {
            for (int i = 0; i < clbMem.Items.Count; i++) clbMem.SetItemChecked(i, on);
        }

        // Remet à zéro les choix du type affiché (l'autre type garde les siens).
        public void ResetShown()
        {
            if (rbFreq.Checked)
            {
                txtStart.Text = txtEnd.Text = "";
                cbStep.SelectedIndex = Array.IndexOf(Steps, DefaultStep);
                cbDwellF.SelectedIndex = DwellIndex(DefaultDwell);
            }
            else
            {
                SetAll(false);
                cbDwellM.SelectedIndex = DwellIndex(DefaultDwell);
            }
        }

        // null si correct. Pour lancer le scan, le type choisi doit être complet.
        string CheckInput(bool starting)
        {
            long a = MemoryEditDialog.ParseKHz(txtStart.Text), b = MemoryEditDialog.ParseKHz(txtEnd.Text);
            bool freqEmpty = txtStart.Text.Trim() == "" && txtEnd.Text.Trim() == "";
            if (rbFreq.Checked && (starting || !freqEmpty) && (a <= 0 || b <= 0 || a == b))
                return Lang.T("Indiquez une fréquence de début et une fréquence de fin différentes, en kHz (par ex. 7000 et 7200).",
                              "Enter different start and end frequencies, in kHz (e.g. 7000 and 7200).");
            if (rbMem.Checked && starting && clbMem.CheckedIndices.Count == 0)
                return Lang.T("Cochez au moins une mémoire à parcourir.", "Check at least one memory to scan.");
            return null;
        }

        void Store()
        {
            settings.ScanType = rbMem.Checked ? 1 : 0;
            settings.ScanStartHz = Math.Max(0, MemoryEditDialog.ParseKHz(txtStart.Text));
            settings.ScanEndHz = Math.Max(0, MemoryEditDialog.ParseKHz(txtEnd.Text));
            settings.ScanStepHz = Steps[Math.Max(0, cbStep.SelectedIndex)];
            settings.ScanFreqDwell = Dwells[Math.Max(0, cbDwellF.SelectedIndex)];
            settings.ScanMemDwell = Dwells[Math.Max(0, cbDwellM.SelectedIndex)];
            for (int i = 0; i < mems.Count; i++) mems[i].Scan = clbMem.GetItemChecked(i);
        }
    }
}
