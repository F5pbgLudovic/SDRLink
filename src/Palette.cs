using System;
using System.Drawing;
using System.Windows.Forms;

namespace SDRLink
{
    // Petit bouton carré multicolore ouvrant la palette des couleurs de fond.
    public class PaletteButton : Button
    {
        public PaletteButton()
        {
            Size = new Size(24, 24);
            Margin = new Padding(2, 4, 2, 0);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            Tag = Theme.Custom;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Quatre teintes de la palette du thème en cours, en damier.
            Color[] pal = Theme.Dark ? Theme.DarkPalette : Theme.LightPalette;
            Color[] q = Theme.Dark
                ? new[] { pal[4], pal[16], pal[42], pal[27] }
                : new[] { pal[49], pal[17], pal[28], pal[41] };
            int w = Width / 2, h = Height / 2;
            Rectangle[] r = { new Rectangle(0, 0, w, h), new Rectangle(w, 0, Width - w, h), new Rectangle(0, h, w, Height - h), new Rectangle(w, h, Width - w, Height - h) };
            for (int i = 0; i < 4; i++)
                using (SolidBrush b = new SolidBrush(q[i])) e.Graphics.FillRectangle(b, r[i]);
            using (Pen p = new Pen(Theme.Dark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(90, 90, 90)))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    // Fenêtre de choix de la couleur de fond : couleurs sombres en mode sombre, claires en mode clair.
    public class PalettePopup : Form
    {
        public Color? Chosen;
        public bool Picked;

        public PalettePopup(Color? current)
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Text = Theme.Dark ? Lang.T("Couleur du mode sombre", "Dark mode colour") : Lang.T("Couleur du mode clair", "Light mode colour");
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };

            // Deux moitiés de 8 x 4 côte à côte (neutres et froides à gauche, chaudes à droite), séparées par un espace ;
            // la fenêtre est centrée sous le bouton, une moitié de chaque côté (voir MainForm.ShowPalette).
            const int cell = 36, pad = 9, gap = 18;
            Color[] pal = Theme.Dark ? Theme.DarkPalette : Theme.LightPalette;
            int rows = (Theme.PaletteHalf + Theme.PaletteCols - 1) / Theme.PaletteCols;
            int halfW = Theme.PaletteCols * cell;
            ToolTip tip = new ToolTip();
            for (int i = 0; i < pal.Length; i++)
            {
                Color col = pal[i];
                int half = i / Theme.PaletteHalf, k = i % Theme.PaletteHalf;
                Button b = new Button { Size = new Size(cell - 6, cell - 6), FlatStyle = FlatStyle.Flat, BackColor = col, UseVisualStyleBackColor = false };
                b.Location = new Point(pad + half * (halfW + gap) + (k % Theme.PaletteCols) * cell + 3, pad + (k / Theme.PaletteCols) * cell + 3);
                bool selected = current.HasValue && current.Value.ToArgb() == col.ToArgb()
                    || !current.HasValue && Theme.Dark && col.ToArgb() == Theme.DefaultDark.ToArgb();
                b.FlatAppearance.BorderSize = selected ? 3 : 1;
                b.FlatAppearance.BorderColor = selected ? Color.FromArgb(255, 200, 40) : Color.Gray;
                tip.SetToolTip(b, "#" + Theme.ToHex(col));
                b.Click += delegate { Chosen = col; Picked = true; Close(); };
                Controls.Add(b);
            }
            int gridH = pad + rows * cell;

            // Couleur au choix (depuis la 1.29) : Rouge, Vert, Bleu de 0 à 255, aperçu avec la couleur du texte du mode,
            // « Appliquer » (ou Entrée), et « Autre couleur… » pour le sélecteur de Windows. Préremplie avec la couleur en cours.
            Color start = current ?? (Theme.Dark ? Theme.DefaultDark : SystemColors.Control);
            FlowLayoutPanel own = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Location = new Point(pad, gridH + 4), Margin = Padding.Empty };
            own.Controls.Add(MakeLabel(Lang.T("Couleur au choix :", "Your own colour:")));
            nR = AddChannel(own, Lang.T("Rouge", "Red"), start.R);
            nG = AddChannel(own, Lang.T("Vert", "Green"), start.G);
            nB = AddChannel(own, Lang.T("Bleu", "Blue"), start.B);
            preview = new Label
            {
                Text = Lang.T("Texte", "Text"), Size = new Size(56, 24), TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(8, 3, 3, 3),
                ForeColor = Theme.Dark ? Color.FromArgb(228, 228, 228) : Color.FromArgb(25, 25, 25),
            };
            tip.SetToolTip(preview, Lang.T("Aperçu : le fond choisi avec le texte de ce mode", "Preview: the chosen background with this mode's text"));
            own.Controls.Add(preview);
            Button apply = MakeButton(Lang.T("Appliquer", "Apply"));
            apply.Margin = new Padding(6, 3, 3, 3);
            apply.Click += delegate { Chosen = OwnColor(); Picked = true; Close(); };
            own.Controls.Add(apply);
            AcceptButton = apply;
            Controls.Add(own);
            UpdatePreview();

            int row2 = gridH + 4 + own.PreferredSize.Height + 4;
            Button reset = MakeButton(Lang.T("Par défaut", "Default"));
            reset.Location = new Point(pad + 3, row2);
            reset.Click += delegate { Chosen = null; Picked = true; Close(); };
            Controls.Add(reset);
            Button other = MakeButton(Lang.T("Autre couleur…", "Other colour…"));
            other.Location = new Point(reset.Right + 8, row2);
            other.Click += delegate { PickWithWindows(); };
            Controls.Add(other);

            ClientSize = new Size(Math.Max(2 * pad + 2 * halfW + gap, 2 * pad + own.PreferredSize.Width),
                row2 + reset.PreferredSize.Height + pad);
            BackColor = Theme.Back;
            Theme.TitleBar(this);
        }

        NumericUpDown nR, nG, nB;
        Label preview;
        bool inDialog;      // sélecteur de Windows ouvert : la palette ne se ferme pas en perdant le focus

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!inDialog) Close();
        }

        Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Theme.Fore, Margin = new Padding(3, 7, 2, 3) };
        }

        Button MakeButton(string text)
        {
            Button b = new Button { Text = text, AutoSize = true };
            Theme.StyleButton(b);
            return b;
        }

        NumericUpDown AddChannel(FlowLayoutPanel p, string name, int value)
        {
            p.Controls.Add(MakeLabel(name));
            NumericUpDown n = new NumericUpDown
            {
                Minimum = 0, Maximum = 255, Value = value, Width = 50, TextAlign = HorizontalAlignment.Right,
                BackColor = Theme.InputBack, ForeColor = Theme.InputFore, Margin = new Padding(0, 4, 4, 3),
            };
            n.ValueChanged += delegate { UpdatePreview(); };
            n.TextChanged += delegate { UpdatePreview(); };
            p.Controls.Add(n);
            return n;
        }

        // Valeur tapée, même si la case n'est pas encore validée (Entrée pendant la frappe).
        static int Channel(NumericUpDown n)
        {
            int v;
            if (!int.TryParse(n.Text.Trim(), out v)) v = (int)n.Value;
            return Math.Max(0, Math.Min(255, v));
        }

        Color OwnColor() { return Color.FromArgb(Channel(nR), Channel(nG), Channel(nB)); }

        void UpdatePreview() { if (preview != null && nR != null && nG != null && nB != null) preview.BackColor = OwnColor(); }

        void PickWithWindows()
        {
            inDialog = true;
            try
            {
                using (ColorDialog d = new ColorDialog())
                {
                    d.FullOpen = true;
                    d.Color = OwnColor();
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    nR.Value = d.Color.R; nG.Value = d.Color.G; nB.Value = d.Color.B;
                    Chosen = Color.FromArgb(d.Color.R, d.Color.G, d.Color.B);
                    Picked = true;
                }
            }
            finally { inDialog = false; }
            Close();
        }
    }
}
