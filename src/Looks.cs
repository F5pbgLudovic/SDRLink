using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SDRLink
{
    // Allure des boutons (bouton « Look », depuis la 1.33). Classique = dessin habituel de Windows / du thème ;
    // les autres looks redessinent chaque bouton dans son propre rectangle, sans changer sa taille ni sa place.
    // Les couleurs qui ont un sens (PTT en émission, mode actif, Scan, REC, couleurs choisies au clic droit) sont gardées :
    // seuls les boutons « neutres » (gris du thème) prennent les teintes du look.
    public static class Looks
    {
        public const int Classic = 0, Win311 = 1, Neon = 2, Modern = 3, Glass = 4, Terminal = 5, Metal = 6;
        public const int Count = 7;

        public static int Current;

        // Nom affiché dans le menu du bouton « Look ».
        public static string Name(int look)
        {
            switch (look)
            {
                case Win311: return "Win3.11";
                case Neon: return Lang.T("Néon IA", "AI Neon");
                case Modern: return Lang.T("Moderne", "Modern");
                case Glass: return Lang.T("Verre", "Glass");
                case Terminal: return "Terminal";
                case Metal: return Lang.T("Métal", "Metal");
                default: return Lang.T("Classique", "Classic");
            }
        }

        static Color face;       // teinte principale du dernier bouton dessiné (contraste du texte, bouton désactivé)
        static Button pressed;   // bouton enfoncé à la souris (pour l'aspect « appuyé »)

        // Branché une fois par bouton (Theme.Apply / StyleButton) ; sans effet en look Classique.
        public static void Hook(Button b)
        {
            if (b is PaletteButton) return;
            b.Paint -= OnPaint; b.Paint += OnPaint;
            b.MouseEnter -= Redraw; b.MouseEnter += Redraw;
            b.MouseLeave -= Redraw; b.MouseLeave += Redraw;
            b.MouseDown -= OnDown; b.MouseDown += OnDown;
            b.MouseUp -= OnUp; b.MouseUp += OnUp;
        }

        static void Redraw(object s, EventArgs e) { if (Current != Classic) ((Control)s).Invalidate(); }
        static void OnDown(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = (Button)s; Redraw(s, e); } }
        static void OnUp(object s, MouseEventArgs e) { if (pressed == s) { pressed = null; Redraw(s, e); } }

        // Change de look et redessine toutes les fenêtres ouvertes.
        public static void Set(int look)
        {
            Current = look < 0 || look >= Count ? Classic : look;
            foreach (Form f in Application.OpenForms) f.Invalidate(true);
        }

        static void OnPaint(object sender, PaintEventArgs e)
        {
            if (Current == Classic) return;
            Button b = (Button)sender;
            if (b.Width < 4 || b.Height < 4) return;
            try { Draw(b, e.Graphics); }
            catch (Exception ex) { Log.Write("Look : dessin d'un bouton impossible : " + ex.Message); }
        }

        // ------------------------------------------------------------------ Couleurs

        static Color Mix(Color a, Color b, double t) { return Theme.Mix(a, b, Math.Max(0, Math.Min(1, t))); }
        static Color Light(Color c, double t) { return Mix(c, Color.White, t); }
        static Color Dark(Color c, double t) { return Mix(c, Color.Black, t); }
        static Color Alpha(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c); }
        static int Chroma(Color c) { return Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)); }
        static bool Chromatic(Color c) { return Chroma(c) > 40; }
        static double Luma(Color c) { return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0; }

        // Bouton neutre : fond gris du thème (ou fond Windows par défaut), sans couleur qui porte un sens.
        static bool Neutral(Button b)
        {
            if (b.UseVisualStyleBackColor) return true;
            return Chroma(b.BackColor) <= 24;
        }

        // Couleur de bordure donnée par SDRLink (boutons de mode verts, contours des couleurs choisies) si elle est colorée.
        static Color? GivenAccent(Button b)
        {
            if (b.FlatStyle != FlatStyle.Flat) return null;
            Color c = b.FlatAppearance.BorderColor;
            if (c.IsEmpty || c.A == 0 || !Chromatic(c)) return null;
            return c;
        }

        // ------------------------------------------------------------------ Dessin

        static void Draw(Button b, Graphics g)
        {
            Rectangle r = b.ClientRectangle;
            bool on = b.Enabled;
            bool hot = on && r.Contains(b.PointToClient(Cursor.Position));
            bool down = on && pressed == b && hot && (Control.MouseButtons & MouseButtons.Left) != 0;
            bool neutral = Neutral(b);
            Color parent = b.Parent != null ? b.Parent.BackColor : Theme.Back;
            Color? accent = GivenAccent(b);
            // Texte : la couleur donnée par SDRLink si elle a un sens (vert des modes, texte d'un bouton coloré).
            bool keepText = !neutral || Chromatic(b.ForeColor);

            g.SmoothingMode = SmoothingMode.None;
            using (SolidBrush pb = new SolidBrush(parent)) g.FillRectangle(pb, r);

            Color text;
            int shift = 0;
            switch (Current)
            {
                case Win311: text = DrawWin311(b, g, r, neutral, down, out shift); break;
                case Neon: text = DrawNeon(b, g, r, neutral, hot, down, accent); break;
                case Modern: text = DrawModern(b, g, r, neutral, hot, down, accent); break;
                case Glass: text = DrawGlass(b, g, r, neutral, hot, down, accent); break;
                case Terminal: text = DrawTerminal(b, g, r, neutral, hot, down, accent); break;
                default: text = DrawMetal(b, g, r, neutral, hot, down, accent); break;
            }
            // Texte gardé seulement s'il reste lisible sur le fond du look (ex. vert des modes sur le gris de Win3.11).
            if (keepText && !(Current == Terminal && neutral && down) && (!neutral || Math.Abs(Luma(b.ForeColor) - Luma(face)) >= 0.33))
                text = b.ForeColor;
            g.SmoothingMode = SmoothingMode.None;

            if (!on) text = Mix(text, face, 0.5);
            DrawContent(b, g, r, text, shift);
        }

        // Texte (et image, comme REC) à la place prévue par le bouton : marges intérieures et alignement respectés.
        static void DrawContent(Button b, Graphics g, Rectangle r, Color text, int shift)
        {
            Rectangle inner = new Rectangle(r.X + b.Padding.Left + 2 + shift, r.Y + b.Padding.Top + 2 + shift,
                                            r.Width - b.Padding.Horizontal - 4, r.Height - b.Padding.Vertical - 4);
            if (b.Image != null)
            {
                Size s = b.Image.Size;
                g.DrawImage(b.Image, new Rectangle(r.X + (r.Width - s.Width) / 2 + shift, r.Y + (r.Height - s.Height) / 2 + shift, s.Width, s.Height));
            }
            if (string.IsNullOrEmpty(b.Text)) return;
            TextFormatFlags f = TextFormatFlags.WordBreak | Align(b.TextAlign);
            if (!b.UseMnemonic) f |= TextFormatFlags.NoPrefix;
            if (inner.Width < 4) inner = new Rectangle(r.X + shift, inner.Y, r.Width, inner.Height);
            TextRenderer.DrawText(g, b.Text, b.Font, inner, text, f);
            if (Current == Win311 && b.Focused)
            {
                Size ts = TextRenderer.MeasureText(g, b.Text, b.Font, inner.Size, f);
                Rectangle fr = new Rectangle(inner.X + (inner.Width - ts.Width) / 2 - 2, inner.Y + (inner.Height - ts.Height) / 2, ts.Width + 4, ts.Height);
                fr.Intersect(Rectangle.Inflate(r, -3, -3));
                ControlPaint.DrawFocusRectangle(g, fr, text, Color.FromArgb(192, 192, 192));
            }
        }

        static TextFormatFlags Align(ContentAlignment a)
        {
            TextFormatFlags h = TextFormatFlags.HorizontalCenter, v = TextFormatFlags.VerticalCenter;
            switch (a)
            {
                case ContentAlignment.TopLeft: case ContentAlignment.MiddleLeft: case ContentAlignment.BottomLeft: h = TextFormatFlags.Left; break;
                case ContentAlignment.TopRight: case ContentAlignment.MiddleRight: case ContentAlignment.BottomRight: h = TextFormatFlags.Right; break;
            }
            switch (a)
            {
                case ContentAlignment.TopLeft: case ContentAlignment.TopCenter: case ContentAlignment.TopRight: v = TextFormatFlags.Top; break;
                case ContentAlignment.BottomLeft: case ContentAlignment.BottomCenter: case ContentAlignment.BottomRight: v = TextFormatFlags.Bottom; break;
            }
            return h | v;
        }

        static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Arrondi limité sur les petits boutons, pour ne jamais rogner le texte.
        static float Radius(Rectangle r, float wanted) { return Math.Min(wanted, Math.Min(r.Height, r.Width) / 4f); }

        static void Fill(Graphics g, GraphicsPath p, RectangleF r, Color top, Color bottom)
        {
            using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, LinearGradientMode.Vertical))
                g.FillPath(br, p);
        }

        static void Stroke(Graphics g, GraphicsPath p, Color c, float w)
        {
            using (Pen pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        // ---- Win3.11 (pour rire) : gris 192, cadre noir aux coins coupés, relief blanc / gris sur deux pixels.
        static Color DrawWin311(Button b, Graphics g, Rectangle r, bool neutral, bool down, out int shift)
        {
            Color face = Looks.face = neutral ? Color.FromArgb(192, 192, 192) : b.BackColor;
            Color hi = neutral ? Color.White : Light(face, 0.55), lo = neutral ? Color.FromArgb(128, 128, 128) : Dark(face, 0.45);
            using (SolidBrush f = new SolidBrush(face)) g.FillRectangle(f, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            int x0 = r.X, y0 = r.Y, x1 = r.Right - 1, y1 = r.Bottom - 1;
            using (Pen k = new Pen(Color.Black))
            {
                g.DrawLine(k, x0 + 1, y0, x1 - 1, y0);
                g.DrawLine(k, x0 + 1, y1, x1 - 1, y1);
                g.DrawLine(k, x0, y0 + 1, x0, y1 - 1);
                g.DrawLine(k, x1, y0 + 1, x1, y1 - 1);
            }
            using (Pen ph = new Pen(hi)) using (Pen pl = new Pen(lo))
            {
                if (down)
                {
                    g.DrawLine(pl, x0 + 1, y0 + 1, x1 - 1, y0 + 1);
                    g.DrawLine(pl, x0 + 1, y0 + 1, x0 + 1, y1 - 1);
                }
                else
                {
                    for (int i = 1; i <= 2; i++)
                    {
                        g.DrawLine(ph, x0 + i, y0 + i, x1 - i - 1, y0 + i);
                        g.DrawLine(ph, x0 + i, y0 + i, x0 + i, y1 - i - 1);
                        g.DrawLine(pl, x0 + i, y1 - i, x1 - i, y1 - i);
                        g.DrawLine(pl, x1 - i, y0 + i, x1 - i, y1 - i);
                    }
                }
            }
            shift = down ? 1 : 0;
            return neutral ? Color.Black : b.ForeColor;
        }

        // ---- Néon IA : verre sombre (ou nacré en thème clair), liseré dégradé cyan -> violet, halo au survol.
        static readonly Color Cyan = Color.FromArgb(0, 210, 255), Violet = Color.FromArgb(168, 92, 255);

        static Color DrawNeon(Button b, Graphics g, Rectangle r, bool neutral, bool hot, bool down, Color? accent)
        {
            bool dark = Theme.Dark || Luma(Theme.Back) < 0.45;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            float rad = Radius(r, 7);
            Color top, bottom;
            if (neutral)
            {
                top = dark ? Color.FromArgb(38, 40, 62) : Color.FromArgb(252, 252, 255);
                bottom = dark ? Color.FromArgb(18, 19, 32) : Color.FromArgb(232, 234, 248);
                if (hot) { top = dark ? Light(top, 0.06) : Mix(top, Cyan, 0.06); bottom = dark ? Mix(bottom, Violet, 0.12) : Mix(bottom, Violet, 0.08); }
            }
            else
            {
                top = Light(b.BackColor, hot ? 0.24 : 0.14);
                bottom = Dark(b.BackColor, 0.18);
            }
            if (down) { Color t = top; top = Dark(bottom, 0.1); bottom = t; }
            face = Mix(top, bottom, 0.5);
            using (GraphicsPath p = Round(rf, rad))
            {
                Fill(g, p, rf, top, bottom);
                // Halo intérieur : quelques contours de plus en plus transparents.
                Color c1 = accent ?? Cyan, c2 = accent.HasValue ? Light(accent.Value, 0.25) : Violet;
                if (hot || down)
                {
                    for (int i = 1; i <= 3; i++)
                    {
                        RectangleF ri = RectangleF.Inflate(rf, -i, -i);
                        using (GraphicsPath pi = Round(ri, Math.Max(1, rad - i)))
                            Stroke(g, pi, Alpha(Mix(c1, c2, 0.5), (down ? 110 : 80) / i), 1.2f);
                    }
                }
                // Reflet léger en haut.
                RectangleF hi = new RectangleF(rf.X + 2, rf.Y + 1.5f, rf.Width - 4, rf.Height * 0.42f);
                using (GraphicsPath ph = Round(hi, Math.Max(1, rad - 2)))
                using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(hi.X, hi.Y - 1, hi.Width, hi.Height + 2),
                    Alpha(Color.White, dark ? 26 : 90), Alpha(Color.White, 0), LinearGradientMode.Vertical))
                    g.FillPath(br, ph);
                using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(rf.X - 1, rf.Y, rf.Width + 2, rf.Height), c1, c2, LinearGradientMode.Horizontal))
                using (Pen pen = new Pen(br, hot || down ? 1.6f : 1f))
                {
                    g.DrawPath(pen, p);
                }
            }
            if (!neutral) return b.ForeColor;
            return dark ? (hot ? Color.White : Color.FromArgb(214, 226, 255)) : Color.FromArgb(36, 30, 84);
        }

        // ---- Moderne : à plat, coins légèrement arrondis, fin contour, ligne d'ombre en bas (style Windows 11).
        static Color DrawModern(Button b, Graphics g, Rectangle r, bool neutral, bool hot, bool down, Color? accent)
        {
            bool dark = Theme.Dark || Luma(Theme.Back) < 0.45;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            float rad = Radius(r, 4);
            if (neutral)
            {
                face = dark ? Light(Theme.Back, 0.10) : Color.FromArgb(253, 253, 253);
                if (hot) face = dark ? Light(face, 0.05) : Color.FromArgb(246, 246, 246);
                if (down) face = dark ? Dark(face, 0.12) : Color.FromArgb(240, 240, 240);
            }
            else
            {
                face = b.BackColor;
                if (hot) face = Luma(face) > 0.6 ? Dark(face, 0.06) : Light(face, 0.10);
                if (down) face = Dark(face, 0.12);
            }
            using (GraphicsPath p = Round(rf, rad))
            {
                using (SolidBrush f = new SolidBrush(face)) g.FillPath(f, p);
                Color edge = accent ?? (dark ? Alpha(Color.White, 34) : Alpha(Color.Black, 34));
                Stroke(g, p, edge, 1f);
                if (!down && neutral)
                    using (Pen sh = new Pen(dark ? Alpha(Color.Black, 70) : Alpha(Color.Black, 50)))
                        g.DrawLine(sh, rf.X + rad, rf.Bottom - 0.5f, rf.Right - rad, rf.Bottom - 0.5f);
            }
            if (!neutral) return b.ForeColor;
            Color t = dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(26, 26, 26);
            return down ? Mix(t, face, 0.3) : t;
        }

        // ---- Verre : bouton brillant façon « aqua », reflet blanc sur la moitié haute.
        static Color DrawGlass(Button b, Graphics g, Rectangle r, bool neutral, bool hot, bool down, Color? accent)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            float rad = Radius(r, 8);
            Color baseC = neutral ? Color.FromArgb(52, 120, 206) : b.BackColor;
            if (hot) baseC = Light(baseC, 0.12);
            if (down) baseC = Dark(baseC, 0.18);
            face = baseC;
            using (GraphicsPath p = Round(rf, rad))
            {
                Fill(g, p, rf, Dark(baseC, 0.15), Light(baseC, 0.25));
                RectangleF hi = new RectangleF(rf.X + 1.5f, rf.Y + 1, rf.Width - 3, rf.Height * 0.5f);
                using (GraphicsPath ph = Round(hi, Math.Max(1, rad - 1.5f)))
                using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(hi.X, hi.Y - 1, hi.Width, hi.Height + 2),
                    Alpha(Color.White, down ? 90 : 170), Alpha(Color.White, down ? 20 : 40), LinearGradientMode.Vertical))
                    g.FillPath(br, ph);
                Stroke(g, p, accent ?? Dark(baseC, 0.45), 1f);
            }
            if (!neutral) return b.ForeColor;
            return Color.White;
        }

        // ---- Terminal : écran noir, texte et contour vert phosphore ; inversé quand on appuie.
        static readonly Color Phosphor = Color.FromArgb(60, 255, 120);

        static Color DrawTerminal(Button b, Graphics g, Rectangle r, bool neutral, bool hot, bool down, Color? accent)
        {
            Color ink = accent.HasValue ? Light(accent.Value, 0.3) : Phosphor;
            face = neutral ? (down ? ink : hot ? Color.FromArgb(0, 46, 18) : Color.FromArgb(4, 10, 6)) : b.BackColor;
            if (!neutral && hot) face = Light(face, 0.12);
            if (!neutral && down) face = Dark(face, 0.15);
            using (SolidBrush f = new SolidBrush(face)) g.FillRectangle(f, r);
            using (Pen p = new Pen(neutral ? ink : accent ?? Dark(b.BackColor, 0.4)))
            {
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                if (hot && neutral) g.DrawRectangle(p, r.X + 2, r.Y + 2, r.Width - 5, r.Height - 5);
            }
            if (!neutral) return b.ForeColor;
            return down ? Color.Black : ink;
        }

        // ---- Métal : alu brossé, relief discret, coins légèrement arrondis.
        static Color DrawMetal(Button b, Graphics g, Rectangle r, bool neutral, bool hot, bool down, Color? accent)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            float rad = Radius(r, 3);
            Color baseC = neutral ? Color.FromArgb(196, 200, 206) : b.BackColor;
            if (hot) baseC = Light(baseC, 0.14);
            Color top = Light(baseC, 0.45), bottom = Dark(baseC, 0.22);
            face = Mix(top, bottom, 0.5);
            if (down) { Color t = top; top = bottom; bottom = t; }
            using (GraphicsPath p = Round(rf, rad))
            {
                Fill(g, p, rf, top, bottom);
                // Stries du métal brossé.
                using (Pen s = new Pen(Alpha(Color.White, 30)))
                    for (float y = rf.Y + 3; y < rf.Bottom - 2; y += 3) g.DrawLine(s, rf.X + 2, y, rf.Right - 2, y);
                Stroke(g, p, accent ?? Dark(baseC, 0.5), 1f);
                RectangleF ri = RectangleF.Inflate(rf, -1, -1);
                using (GraphicsPath pi = Round(ri, Math.Max(1, rad - 1)))
                    Stroke(g, pi, Alpha(Color.White, down ? 30 : 110), 1f);
            }
            if (!neutral) return b.ForeColor;
            return Color.FromArgb(24, 26, 30);
        }
    }
}
