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
        // 64 teintes chacune (depuis la 1.29), en deux moitiés de 8 colonnes x 4 lignes affichées de part et d'autre du bouton :
        // les 32 premières (neutres et froides) à gauche, les 32 suivantes (chaudes) à droite ; une famille de teintes par ligne.
        // Toutes les teintes des versions précédentes y sont encore (un choix enregistré reste sélectionné).
        public const int PaletteHalf = 32, PaletteCols = 8;
        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }
        public static readonly Color[] DarkPalette =
        {
            // Gauche : noirs et gris, gris bleutés, bleus, bleu-vert et verts
            C(0, 0, 0), C(10, 10, 10), C(20, 20, 22), C(26, 26, 28), DefaultDark, C(40, 40, 42), C(46, 46, 50), C(54, 54, 56),
            C(16, 24, 30), C(12, 20, 38), C(20, 26, 34), C(24, 32, 44), C(28, 30, 38), C(30, 36, 46), C(36, 40, 50), C(40, 46, 58),
            C(10, 30, 62), C(8, 24, 48), C(16, 40, 80), C(20, 36, 70), C(14, 46, 90), C(22, 22, 58), C(30, 30, 80), C(36, 38, 96),
            C(10, 38, 42), C(8, 50, 56), C(6, 58, 64), C(12, 36, 24), C(12, 44, 36), C(10, 56, 44), C(20, 44, 40), C(16, 52, 28),
            // Droite : violets, rouges et bordeaux, bruns et orangés, olives
            C(38, 20, 50), C(30, 16, 44), C(52, 26, 70), C(44, 30, 80), C(70, 30, 90), C(60, 20, 60), C(56, 40, 66), C(48, 12, 36),
            C(36, 10, 14), C(48, 12, 20), C(62, 12, 12), C(80, 20, 24), C(76, 30, 30), C(90, 24, 36), C(70, 16, 40), C(96, 20, 50),
            C(36, 26, 18), C(44, 30, 20), C(64, 40, 30), C(54, 44, 30), C(60, 36, 16), C(70, 46, 20), C(84, 50, 24), C(90, 60, 30),
            C(36, 34, 26), C(40, 44, 16), C(50, 52, 20), C(60, 60, 24), C(44, 60, 30), C(30, 50, 20), C(28, 40, 32), C(16, 56, 30),
        };
        public static readonly Color[] LightPalette =
        {
            // Gauche : blancs et gris, gris bleutés, bleus, vert d'eau et verts
            Color.White, C(250, 250, 250), C(244, 244, 248), C(238, 238, 238), C(232, 232, 232), C(226, 226, 228), C(220, 220, 224), C(210, 210, 214),
            C(246, 248, 252), C(240, 244, 250), C(236, 240, 246), C(230, 234, 240), C(226, 232, 242), C(222, 228, 236), C(214, 222, 232), C(206, 214, 226),
            C(236, 244, 255), C(226, 237, 250), C(220, 236, 248), C(212, 228, 245), C(200, 218, 240), C(190, 212, 240), C(218, 224, 250), C(230, 230, 250),
            C(236, 250, 246), C(222, 240, 240), C(208, 236, 240), C(220, 240, 228), C(226, 244, 232), C(204, 232, 216), C(214, 238, 214), C(232, 244, 220),
            // Droite : lavandes, roses et saumon, crèmes et jaunes pâles, sables et beiges
            C(246, 240, 252), C(238, 230, 250), C(240, 232, 244), C(244, 228, 248), C(228, 224, 248), C(232, 220, 240), C(226, 216, 246), C(216, 206, 240),
            C(255, 240, 244), C(250, 232, 236), C(252, 228, 240), C(252, 220, 228), C(236, 222, 226), C(240, 214, 222), C(246, 208, 216), C(248, 226, 220),
            C(255, 252, 236), C(250, 246, 232), C(252, 248, 220), C(248, 240, 200), C(255, 244, 200), C(255, 240, 210), C(250, 234, 214), C(255, 232, 196),
            C(244, 240, 228), C(236, 228, 220), C(240, 232, 214), C(238, 236, 214), C(230, 232, 206), C(232, 222, 200), C(226, 216, 196), C(222, 214, 204),
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
