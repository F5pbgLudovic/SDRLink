using System;
using System.Windows.Forms;

namespace SDRLink
{
    // Bouton « Panneau de commande » (depuis la 1.35) : le panneau (de la ligne Interface à la ligne des Mute) passe
    // du haut de la fenêtre à sous la page du WebSDR, et inversement. La barre d'état reste toujours tout en bas.
    // Les lignes changent seulement de rang dans le tableau principal : la page n'est ni retirée ni rechargée.
    public partial class MainForm
    {
        void SetPanelBottom(bool bottom)
        {
            if (bottom == settings.PanelBottom) return;
            settings.PanelBottom = bottom;
            ApplyPanelPosition();
            Log.Write("Panneau de commande : " + (bottom ? "en bas" : "en haut"));
            settings.Save();
        }

        void ApplyPanelPosition()
        {
            Control page = web.Parent;
            Control[] panel = { rowConn, rowParams, rowHamlib, rowSync, rowDisplay, rowUrl };
            bool bottom = settings.PanelBottom;
            mainTable.SuspendLayout();
            int r = 0;
            if (bottom) mainTable.SetCellPosition(page, new TableLayoutPanelCellPosition(0, r++));
            foreach (Control c in panel) mainTable.SetCellPosition(c, new TableLayoutPanelCellPosition(0, r++));
            if (!bottom) mainTable.SetCellPosition(page, new TableLayoutPanelCellPosition(0, r));
            // La rangée de la page prend toute la place qui reste ; les autres se règlent sur leur contenu.
            int pageRow = bottom ? 0 : panel.Length;
            for (int i = 0; i < mainTable.RowStyles.Count; i++)
                mainTable.RowStyles[i] = i == pageRow ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize);
            mainTable.ResumeLayout(true);
        }
    }
}
