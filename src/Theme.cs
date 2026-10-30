using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SDRLink
{
    // Thème clair / sombre de l'interface (la page du WebSDR garde ses propres couleurs).
    // Les contrôles marqués Tag = Theme.Custom (PTT, boutons de mode, fréquences) sont colorés par la fenêtre elle-même.
    public static class Theme
    {
        public const string Custom = "custom";
        public static bool Dark;
        // Couleur de fond choisie dans la palette (null = couleur par défaut du thème). Toutes les autres teintes en découlent.
        public static Color? DarkBase, LightBase;

        public static readonly Color DefaultDark = Color.FromArgb(32, 32, 34);

        // Thème "maison" (sombre, ou clair avec une couleur de palette) : contrôles dessinés à plat pour respecter les couleurs.
        public static bool Styled { get { return Dark || LightBase.HasValue; } }

        static Color Base { get { return Dark ? (DarkBase ?? DefaultDark) : (LightBase ?? SystemColors.Control); } }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Back { get { return Base; } }
        public static Color Fore { get { return Dark ? Color.FromArgb(228, 228, 228) : LightBase.HasValue ? Color.FromArgb(25, 25, 25) : SystemColors.ControlText; } }
        public static Color InputBack { get { return Dark ? Mix(Base, Color.White, 0.10) : LightBase.HasValue ? Mix(Base, Color.White, 0.65) : SystemColors.Window; } }
        public static Color InputFore { get { return Dark ? Color.FromArgb(240, 240, 240) : LightBase.HasValue ? Color.Black : SystemColors.WindowText; } }
        public static Color ButtonBack { get { return Dark ? Mix(Base, Color.White, 0.18) : LightBase.HasValue ? Mix(Base, Color.Black, 0.07) : SystemColors.Control; } }
        public static Color Border { get { return Dark ? Mix(Base, Color.White, 0.40) : LightBase.HasValue ? Mix(Base, Color.Black, 0.35) : SystemColors.ControlDark; } }

        // Palettes proposées : uniquement des teintes sombres en mode sombre, uniquement des teintes claires en mode clair.
        public static readonly Color[] DarkPalette =
        {
            Color.FromArgb(10, 10, 10), DefaultDark, Color.FromArgb(46, 46, 50), Color.FromArgb(28, 30, 38),
            Color.FromArgb(12, 20, 38), Color.FromArgb(10, 30, 62), Color.FromArgb(22, 22, 58), Color.FromArgb(38, 20, 50),
            Color.FromArgb(48, 12, 20), Color.FromArgb(62, 12, 12), Color.FromArgb(44, 30, 20), Color.FromArgb(12, 36, 24),
            Color.FromArgb(10, 38, 42),
        };
        public static readonly Color[] LightPalette =
        {
            Color.White, Color.FromArgb(232, 232, 232), Color.FromArgb(244, 244, 248), Color.FromArgb(250, 246, 232),
            Color.FromArgb(240, 232, 214), Color.FromArgb(252, 248, 220), Color.FromArgb(226, 237, 250), Color.FromArgb(212, 228, 245),
            Color.FromArgb(226, 244, 232), Color.FromArgb(222, 240, 240), Color.FromArgb(250, 232, 236), Color.FromArgb(238, 230, 250),
        };

        public static string ToHex(Color? c) { return c.HasValue ? c.Value.R.ToString("X2") + c.Value.G.ToString("X2") + c.Value.B.ToString("X2") : ""; }

        public static Color? FromHex(string s)
        {
            int v;
            if (string.IsNullOrEmpty(s) || s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v)) return null;
            return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
        }

        // Couleurs des éléments propres à SDRLink, lisibles sur fond clair comme sur fond sombre.
        public static Color RigFreq { get { return Dark ? Color.FromArgb(110, 185, 255) : Color.FromArgb(0, 90, 160); } }
        public static Color SdrFreq { get { return Dark ? Color.FromArgb(90, 225, 130) : Color.FromArgb(0, 120, 60); } }
        public static Color TxFreq { get { return Dark ? Color.FromArgb(255, 95, 95) : Color.FromArgb(200, 0, 0); } }
        public static Color IdleButton { get { return Dark ? Mix(Base, Color.White, 0.15) : LightBase.HasValue ? Mix(Base, Color.Black, 0.06) : Color.FromArgb(235, 235, 235); } }
        public static Color ModeIdleText { get { return Dark ? Color.FromArgb(120, 230, 150) : Color.FromArgb(0, 90, 45); } }

        public static void Apply(Control root)
        {
            ApplyOne(root);
            foreach (Control c in root.Controls) Apply(c);
            Form f = root as Form;
            if (f != null) TitleBar(f);
        }

        static void ApplyOne(Control c)
        {
            if (Custom.Equals(c.Tag)) return;
            if (c is TextBox || c is ComboBox || c is NumericUpDown || c is ListBox)
            {
                c.BackColor = InputBack;
                c.ForeColor = InputFore;
                ComboBox cb = c as ComboBox;
                if (cb != null)
                {
                    // Une liste déroulante "standard" ignore la couleur de fond : le style plat la respecte.
                    cb.FlatStyle = Styled ? FlatStyle.Flat : FlatStyle.Standard;
                    // En sombre, Windows grise trop le texte d'une liste désactivée : on le dessine nous-mêmes.
                    if (cb.DropDownStyle == ComboBoxStyle.DropDownList)
                    {
                        if (cb.DrawMode == DrawMode.Normal) cb.DrawItem += DrawComboItem;
                        cb.DrawMode = Styled ? DrawMode.OwnerDrawFixed : DrawMode.Normal;
                    }
                }
            }
            else if (c is Button) StyleButton((Button)c);
            else
            {
                c.BackColor = Back;
                c.ForeColor = Fore;
                StatusStrip s = c as StatusStrip;
                if (s != null)
                {
                    s.RenderMode = Styled ? ToolStripRenderMode.System : ToolStripRenderMode.ManagerRenderMode;
                    foreach (ToolStripItem i in s.Items) i.ForeColor = Fore;
                }
            }
        }

        // Bouton ordinaire aux couleurs du thème (aussi pour revenir à la couleur par défaut après un choix au clic droit).
        public static void StyleButton(Button b)
        {
            b.FlatStyle = Styled ? FlatStyle.Flat : FlatStyle.Standard;
            b.UseVisualStyleBackColor = !Styled;
            b.BackColor = ButtonBack;
            b.ForeColor = Fore;
            b.FlatAppearance.BorderColor = Border;
        }

        static readonly Color Highlight = Color.FromArgb(0, 95, 170);   // sélection dans les listes
        static Color DisabledText { get { return Dark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(120, 120, 120); } }

        static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            ComboBox cb = (ComboBox)sender;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (SolidBrush b = new SolidBrush(selected ? Highlight : InputBack)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index < 0) return;
            Color fore = !cb.Enabled ? DisabledText : selected ? Color.White : InputFore;
            TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), e.Font, e.Bounds, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Barre de titre sombre (Windows 10 20H1 et plus, Windows 11) ; sans effet sur les versions plus anciennes.
        public static void TitleBar(Form f)
        {
            if (!f.IsHandleCreated)
            {
                f.HandleCreated += delegate { TitleBar(f); };
                return;
            }
            int on = Dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
                // Redessine le cadre tout de suite (sinon la barre ne change qu'au prochain passage au premier plan).
                SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
