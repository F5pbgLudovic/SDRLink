using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace SDRLink
{
    // Bouton ordinaire avec une case à cocher dessinée à gauche du texte (bouton « Vérifier les mises à jour »).
    // Clic sur la case (ou juste autour) : elle change d'état (CheckedChanged), sans Click.
    // Clic ailleurs sur le bouton, Entrée ou Espace : Click, comme un bouton normal.
    public class CheckButton : Button
    {
        bool isChecked, downOnBox, hotBox;
        public event EventHandler CheckedChanged;

        public CheckButton()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(30, 25);
            UpdatePadding();
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        Size Glyph
        {
            get
            {
                try { using (Graphics g = CreateGraphics()) return CheckBoxRenderer.GetGlyphSize(g, CheckBoxState.UncheckedNormal); }
                catch { return new Size(13, 13); }
            }
        }

        // Zone sensible de la case : la case et quelques pixels autour, sur toute la hauteur du bouton.
        public Rectangle BoxArea { get { return new Rectangle(0, 0, 8 + Glyph.Width + 4, Height); } }

        void UpdatePadding() { Padding = new Padding(8 + Glyph.Width + 2, 0, 4, 0); }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdatePadding(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            downOnBox = e.Button == MouseButtons.Left && BoxArea.Contains(e.Location);
            base.OnMouseDown(e);
        }

        // Appui sur la case puis relâché hors du bouton : pas de clic, et la touche Entrée/Espace suivante reste un clic normal.
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            downOnBox = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hot = BoxArea.Contains(e.Location);
            if (hot != hotBox) { hotBox = hot; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hotBox) { hotBox = false; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (downOnBox)
            {
                downOnBox = false;
                Checked = !Checked;
                return;
            }
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Size g = Glyph;
            Point p = new Point(8, (Height - g.Height) / 2);
            CheckBoxState s = isChecked ? (hotBox ? CheckBoxState.CheckedHot : CheckBoxState.CheckedNormal)
                                        : (hotBox ? CheckBoxState.UncheckedHot : CheckBoxState.UncheckedNormal);
            if (!Enabled) s = isChecked ? CheckBoxState.CheckedDisabled : CheckBoxState.UncheckedDisabled;
            CheckBoxRenderer.DrawCheckBox(e.Graphics, p, s);
        }
    }
}
