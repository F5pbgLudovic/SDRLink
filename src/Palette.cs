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
                ? new[] { pal[1], pal[5], pal[9], pal[11] }
                : new[] { pal[3], pal[6], pal[8], pal[10] };
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
            Deactivate += delegate { Close(); };

            FlowLayoutPanel grid = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), WrapContents = true };
            Color[] pal = Theme.Dark ? Theme.DarkPalette : Theme.LightPalette;
            ToolTip tip = new ToolTip();
            foreach (Color c in pal)
            {
                Color col = c;
                Button b = new Button { Size = new Size(34, 34), FlatStyle = FlatStyle.Flat, BackColor = col, Margin = new Padding(3), UseVisualStyleBackColor = false };
                bool selected = current.HasValue && current.Value.ToArgb() == col.ToArgb()
                    || !current.HasValue && Theme.Dark && col.ToArgb() == Theme.DefaultDark.ToArgb();
                b.FlatAppearance.BorderSize = selected ? 3 : 1;
                b.FlatAppearance.BorderColor = selected ? Color.FromArgb(255, 200, 40) : Color.Gray;
                tip.SetToolTip(b, "#" + Theme.ToHex(col));
                b.Click += delegate { Chosen = col; Picked = true; Close(); };
                grid.Controls.Add(b);
            }
            Button reset = new Button { Text = Lang.T("Par défaut", "Default"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
            reset.Click += delegate { Chosen = null; Picked = true; Close(); };
            grid.SetFlowBreak(grid.Controls[grid.Controls.Count - 1], true);
            grid.Controls.Add(reset);
            Controls.Add(grid);
            ClientSize = new Size(6 * 40 + 14, ((pal.Length + 5) / 6) * 40 + 56);
            BackColor = Theme.Back;
            reset.BackColor = Theme.ButtonBack;
            reset.ForeColor = Theme.Fore;
            reset.FlatStyle = Theme.Styled ? FlatStyle.Flat : FlatStyle.Standard;
            Theme.TitleBar(this);
        }
    }
}
