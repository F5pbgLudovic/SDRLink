using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SDRLink
{
    // Look > « Redimensionner en conservant les proportions » (1.58, décoché au départ) : la fenêtre garde le rapport
    // largeur / hauteur de l'écran et tout son contenu (texte, boutons, page du WebSDR) suit sa taille : c'est l'affichage
    // plein écran, en plus petit. Seulement en affichage normal, hors plein écran et hors affichage ptt ; ailleurs, échelle 1.
    // L'échelle s'ajoute à « Taille du texte… » et au zoom de la page choisi pour chaque WebSDR.
    public partial class MainForm
    {
        double propK = 1;                    // échelle actuelle : 1 = plein écran, option décochée ou autre affichage
        const double PropMin = 0.3;          // pas plus petit que 30 % de l'écran
        System.Windows.Forms.Timer propTimer;

        // Tailles minimales des boutons (25 de haut à l'échelle 1) et zoom de la page sans l'échelle.
        int Mh { get { return Math.Max(8, (int)Math.Round(25 * propK)); } }
        Size MinSz(int w) { return new Size(Math.Max(8, (int)Math.Round(w * propK)), Mh); }
        double PageZoom { get { return web == null ? 1.0 : web.ZoomFactor / propK; } }

        bool PropActive { get { return settings.KeepProportions && viewShown == 0 && !pttView && WindowState == FormWindowState.Normal; } }

        void InitProportions()
        {
            if (propTimer != null) return;
            propTimer = new System.Windows.Forms.Timer();
            propTimer.Interval = 150;
            propTimer.Tick += delegate { propTimer.Stop(); UpdateProportions(); };
            // Pendant qu'on tire un bord, l'échelle suit après une courte pause ; au lâcher, tout de suite.
            SizeChanged += delegate { if (WindowState != FormWindowState.Minimized) { propTimer.Stop(); propTimer.Start(); } };
            ResizeEnd += delegate { propTimer.Stop(); UpdateProportions(); };
            if (settings.KeepProportions) { FitProportionRatio(); UpdateProportions(); }
        }

        void SetKeepProportions(bool on)
        {
            settings.KeepProportions = on;
            settings.Save();
            Log.Write("Redimensionner en conservant les proportions : " + (on ? "oui" : "non"));
            if (on) FitProportionRatio();
            UpdateProportions();
            ShowStatus(on
                ? Lang.T("Proportions conservées : la fenêtre garde le rapport de l'écran et tout son contenu suit sa taille", "Proportions kept: the window keeps the screen ratio and all its content follows its size")
                : Lang.T("Proportions libres : taille d'origine du contenu", "Free proportions: original content size"));
        }

        // Hauteur mise au rapport de l'écran, à largeur égale (dans la limite de l'écran).
        void FitProportionRatio()
        {
            if (!PropActive) return;
            Rectangle a = WorkArea;
            int w = Math.Max((int)(a.Width * PropMin), Math.Min(Width, a.Width));
            Size = new Size(w, (int)Math.Round((double)w * a.Height / a.Width));
            FitOnScreen();
        }

        void UpdateProportions()
        {
            if (propTimer == null || WindowState == FormWindowState.Minimized) return;
            double k = 1;
            if (PropActive)
            {
                Rectangle a = WorkArea;
                k = Math.Max(PropMin, Math.Min(1.0, (double)Width / a.Width));
            }
            if (Math.Abs(k - propK) < 0.01) return;
            double z = PageZoom;
            propK = k;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            ApplyFontScale(settings.FontScale);
            SetPageZoom(z);
            Log.Write("Proportions : échelle " + Math.Round(k * 100) + " % (" + sw.ElapsedMilliseconds + " ms)");
        }

        const int WM_SIZING = 0x0214;

        [StructLayout(LayoutKind.Sequential)]
        struct SizingRect { public int Left, Top, Right, Bottom; }

        // Pendant qu'on tire un bord ou un coin : la fenêtre garde le rapport de l'écran.
        void ProportionSizing(ref Message m)
        {
            if (m.Msg != WM_SIZING || !PropActive || m.LParam == IntPtr.Zero) return;
            SizingRect r = (SizingRect)Marshal.PtrToStructure(m.LParam, typeof(SizingRect));
            Rectangle a = WorkArea;
            double ratio = (double)a.Width / a.Height;
            int edge = m.WParam.ToInt32();   // 1 gauche, 2 droite, 3 haut, 4 haut-gauche, 5 haut-droite, 6 bas, 7 bas-gauche, 8 bas-droite
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (edge == 3 || edge == 6) w = (int)Math.Round(h * ratio);   // bord du haut ou du bas : la largeur suit
            w = Math.Max((int)(a.Width * PropMin), Math.Min(a.Width, w));
            h = (int)Math.Round(w / ratio);
            if (edge == 1 || edge == 4 || edge == 7) r.Left = r.Right - w; else r.Right = r.Left + w;
            if (edge == 3 || edge == 4 || edge == 5) r.Top = r.Bottom - h; else r.Bottom = r.Top + h;
            Marshal.StructureToPtr(r, m.LParam, false);
        }
    }
}
