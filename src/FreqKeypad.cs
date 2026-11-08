using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace SDRLink
{
    // Ligne de la liste « Mem » (clavier de fréquence) : fréquence poste, mode, mode numérique en marche ("" = aucun),
    // heure d'enregistrement (ms depuis 1970, UTC ; 0 = inconnue). Même contenu que l'historique « Mem » de PhoneSDRLink.
    public class MemHistEntry
    {
        public long Hz;
        public string Mode = "";
        public string Dec = "";
        public long T;

        public const int Size = 20;   // 20 lignes au plus, la plus récente en tête

        public static long Now() { return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / TimeSpan.TicksPerMillisecond; }

        public bool Same(long hz, string mode, string dec) { return Hz == hz && Mode == (mode ?? "") && Dec == (dec ?? ""); }

        // Heure et date locales (« 14:32 8/11/2026 ») ; vide si l'heure est inconnue.
        public string When()
        {
            if (T <= 0) return "";
            DateTime d = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(T).ToLocalTime();
            return d.ToString("HH:mm d/M/yyyy", CultureInfo.InvariantCulture);
        }

        // Réglage : "Hz|mode|mode numérique|heure".
        public string ToSetting() { return Hz.ToString(CultureInfo.InvariantCulture) + "|" + Mode + "|" + Dec + "|" + T.ToString(CultureInfo.InvariantCulture); }

        public static MemHistEntry FromSetting(string v)
        {
            string[] p = (v ?? "").Split('|');
            long hz;
            if (p.Length < 2 || !long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hz) || hz <= 0) return null;
            MemHistEntry e = new MemHistEntry { Hz = hz, Mode = p[1] };
            if (p.Length >= 3) e.Dec = p[2];
            long t;
            if (p.Length >= 4 && long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out t)) e.T = Math.Max(0, t);
            return e;
        }

        // Ajout en tête : la même ligne déjà en tête ne fait que prendre l'heure actuelle ; un doublon plus bas est retiré.
        public static void Add(List<MemHistEntry> list, long hz, string mode, string dec)
        {
            mode = mode ?? ""; dec = dec ?? "";
            if (list.Count > 0 && list[0].Same(hz, mode, dec)) { list[0].T = Now(); return; }
            list.RemoveAll(delegate(MemHistEntry e) { return e.Same(hz, mode, dec); });
            list.Insert(0, new MemHistEntry { Hz = hz, Mode = mode, Dec = dec, T = Now() });
            if (list.Count > Size) list.RemoveRange(Size, list.Count - Size);
        }
    }

    // Bouton à coins arrondis (clavier de fréquence et liste « Mem », comme sur le téléphone) ; appui long possible.
    public class RoundButton : Control
    {
        public event EventHandler LongPress;
        public ContentAlignment Align = ContentAlignment.MiddleCenter;
        bool hot, down, longDone;
        readonly Timer hold = new Timer { Interval = 600 };

        public RoundButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick | ControlStyles.Selectable, false);
            Tag = Theme.Custom;
            Cursor = Cursors.Hand;
            hold.Tick += delegate
            {
                hold.Stop();
                if (!down || LongPress == null) return;
                longDone = true;
                down = false;
                Invalidate();
                LongPress(this, EventArgs.Empty);
            };
        }

        static Color Fill { get { return Theme.Styled ? Theme.ButtonBack : Color.FromArgb(225, 225, 225); } }
        static Color Edge { get { return Theme.Styled ? Theme.Border : Color.FromArgb(173, 173, 173); } }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; hold.Stop(); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            down = true; longDone = false;
            hold.Stop(); hold.Start();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            hold.Stop();
            bool click = down && !longDone && ClientRectangle.Contains(e.Location);
            down = false;
            Invalidate();
            if (click) OnClick(EventArgs.Empty);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void Dispose(bool disposing) { if (disposing) hold.Dispose(); base.Dispose(disposing); }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Fill;
            Color shade = Theme.Dark ? Color.White : Color.Black;
            if (down) fill = Theme.Mix(fill, shade, 0.16);
            else if (hot) fill = Theme.Mix(fill, shade, 0.07);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Rounded(r, Math.Max(4f, Font.Height * 0.55f)))
            using (SolidBrush b = new SolidBrush(fill))
            using (Pen pen = new Pen(Edge))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            f |= Align == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            int pad = Align == ContentAlignment.MiddleLeft ? Font.Height * 2 / 3 : 2;
            Rectangle tr = new Rectangle(pad, 0, Width - 2 * pad, Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Fore : Theme.Border, f);
        }
    }

    // Ce que le clavier demande à la fenêtre principale.
    public interface IMemHistHost
    {
        List<MemHistEntry> MemHist { get; }
        int MemHistMinutes { get; set; }
        void MemHistStore();                           // « Store » : fréquence, mode et mode numérique actuels en tête de liste
        void MemHistRecall(MemHistEntry e);            // rappel d'une ligne (fréquence, mode, mode numérique)
        void MemHistCopy(MemHistEntry e, int list);    // copie dans une liste de « Mémoires »
        void MemHistChanged();                         // liste modifiée : à enregistrer
        string[] MemoryListLabels { get; }
        string MemHistText(MemHistEntry e);            // « 14074 kHz  USB  FT8 »
    }

    // Clavier de fréquence (kHz), comme celui de PhoneSDRLink : 7 8 9 / 4 5 6 / 1 2 3 / . 0 ⌫, puis Mem, Store, Auto, puis Annuler et OK.
    public class FreqKeypad : Form
    {
        public static readonly int[] AutoMinutes = { 0, 1, 2, 3, 5, 10, 15, 20, 30 };
        public const long MinHz = 1000, MaxHz = 99999999999;   // 1 kHz à 100 GHz

        public long ResultHz;
        readonly StringBuilder txt;
        bool fresh = true;
        readonly Label shown, preview;
        readonly RoundButton btnAuto;
        readonly IMemHistHost host;
        readonly ToolTip tips = new ToolTip();

        public FreqKeypad(IMemHistHost host, long startHz, Color accent, Font baseFont)
        {
            this.host = host;
            txt = new StringBuilder(startHz > 0 ? Memory.KHz(startHz) : "");
            Text = Lang.T("Fréquence (kHz) :", "Frequency (kHz):").TrimEnd(' ', ':');
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Font = baseFont;
            int u = Font.Height;                       // unité : hauteur d'une ligne de texte
            int gap = Math.Max(4, u / 3), pad = u * 3 / 4;
            int kw = u * 5, kh = u * 3;                // touche
            int width = 3 * kw + 2 * gap;

            Font big = new Font("Consolas", baseFont.Size * 2.6f, FontStyle.Bold);
            Font keyFont = new Font(baseFont.FontFamily, baseFont.Size * 1.7f);
            shown = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Font = big, Tag = Theme.Custom, ForeColor = accent, BackColor = Theme.Back };
            shown.SetBounds(pad, pad, width, big.Height + u / 2);
            preview = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleRight };
            preview.SetBounds(pad, shown.Bottom, width, u + 4);
            Controls.Add(shown);
            Controls.Add(preview);

            string[] keys = { "7", "8", "9", "4", "5", "6", "1", "2", "3", ".", "0", "⌫" };
            int y = preview.Bottom + gap;
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                RoundButton b = new RoundButton { Text = k, Font = keyFont };
                b.SetBounds(pad + (i % 3) * (kw + gap), y + (i / 3) * (kh + gap), kw, kh);
                b.Click += delegate { Key(k); };
                if (k == "⌫") b.LongPress += delegate { txt.Length = 0; fresh = false; Refresh2(); };
                Controls.Add(b);
            }
            y += 4 * (kh + gap) + gap;

            int rh = u * 2 + u / 2;
            RoundButton mem = new RoundButton { Text = "Mem  ▾", Align = ContentAlignment.MiddleLeft };
            mem.SetBounds(pad, y, kw, rh);
            mem.Click += delegate { ShowMemList(); };
            RoundButton store = new RoundButton { Text = "Store" };
            store.SetBounds(pad + kw + gap, y, kw, rh);
            store.Click += delegate { host.MemHistStore(); };
            btnAuto = new RoundButton();
            btnAuto.SetBounds(pad + 2 * (kw + gap), y, kw, rh);
            btnAuto.Click += delegate { AutoMenu(); };
            Controls.Add(mem); Controls.Add(store); Controls.Add(btnAuto);
            tips.SetToolTip(mem, Lang.T("Les 20 dernières fréquences mémorisées (clic sur une ligne : y retourner)", "The last 20 stored frequencies (click a line: go back to it)"));
            tips.SetToolTip(store, Lang.T("Mémorise tout de suite la fréquence, le mode et le mode numérique en marche dans la liste Mem", "Stores the frequency, the mode and the running digital mode in the Mem list right away"));
            tips.SetToolTip(btnAuto, Lang.T("Enregistrement automatique dans la liste Mem quand la fréquence reste ce temps sans changer (0 = jamais)", "Automatic storing in the Mem list when the frequency stays unchanged for this time (0 = never)"));
            ShowAuto();
            y += rh + gap * 2;

            RoundButton ok = new RoundButton { Text = "OK" };
            ok.SetBounds(pad + 2 * (kw + gap), y, kw, rh);
            ok.Click += delegate { Ok(); };
            RoundButton cancel = new RoundButton { Text = Lang.T("Annuler", "Cancel") };
            cancel.SetBounds(pad + kw + gap, y, kw, rh);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel); Controls.Add(ok);
            ClientSize = new Size(width + 2 * pad, y + rh + pad);

            Theme.Apply(this);
            shown.BackColor = BackColor;
            Refresh2();
        }

        void ShowAuto() { btnAuto.Text = "Auto " + host.MemHistMinutes + " min"; }

        void AutoMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            foreach (int v in AutoMinutes)
            {
                int mins = v;
                ToolStripMenuItem it = new ToolStripMenuItem("Auto " + mins + " min") { Checked = mins == host.MemHistMinutes };
                it.Click += delegate { host.MemHistMinutes = mins; ShowAuto(); };
                m.Items.Add(it);
            }
            m.Closed += delegate { BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
            m.Show(btnAuto, new Point(0, btnAuto.Height));
        }

        void ShowMemList()
        {
            Rectangle wa = !WorkArea.IsEmpty ? WorkArea : Screen.FromControl(this).WorkingArea;
            using (MemHistForm f = new MemHistForm(host, Font, wa))
            {
                // À droite du clavier, sans le recouvrir ; à gauche s'il n'y a pas la place ; sinon du côté le plus large, dans l'écran.
                int gap = 6, x = Right + gap;
                if (x + f.Width > wa.Right)
                {
                    int left = Left - gap - f.Width;
                    x = left >= wa.Left ? left : (wa.Right - Right >= Left - wa.Left ? wa.Right - f.Width : wa.Left);
                }
                f.Location = new Point(Math.Max(wa.Left, x), Math.Max(wa.Top, Math.Min(wa.Bottom - f.Height, Top)));
                f.TopMost = TopMost;
                if (f.ShowDialog(this) != DialogResult.OK || f.Chosen == null) return;
                MemHistEntry e = f.Chosen;
                DialogResult = DialogResult.Abort;     // la fenêtre principale fait le rappel une fois le clavier fermé
                Recall = e;
            }
        }

        public MemHistEntry Recall;
        public Rectangle WorkArea;               // zone de l'écran où poser la liste « Mem » (celle de la fenêtre principale)

        void Key(string k)
        {
            if (k == "⌫") { if (txt.Length > 0) txt.Length--; fresh = false; }
            else
            {
                if (fresh) { txt.Length = 0; fresh = false; }
                if (k == "." && txt.ToString().IndexOf('.') >= 0) return;
                if (txt.Length < 11) txt.Append(k == "." && txt.Length == 0 ? "0." : k);
            }
            Refresh2();
        }

        void Refresh2()
        {
            shown.Text = txt.Length == 0 ? "0" : txt.ToString();
            long hz = Parse(txt.ToString());
            bool ok = hz >= MinHz && hz <= MaxHz;
            preview.Text = hz > 0 ? "= " + FormatHz(hz) + (ok ? "" : "   ✗") : "";
            preview.ForeColor = hz > 0 && !ok ? Color.FromArgb(198, 40, 40) : Theme.Fore;
        }

        void Ok()
        {
            long hz = Parse(txt.ToString());
            if (hz < MinHz || hz > MaxHz)
            {
                preview.ForeColor = Color.FromArgb(198, 40, 40);
                preview.Text = Lang.T("Fréquence invalide : tapez une valeur en kHz, par ex. 7074 ou 14074.5", "Invalid frequency: type a value in kHz, e.g. 7074 or 14074.5");
                tips.SetToolTip(preview, preview.Text);
                return;
            }
            ResultHz = hz;
            DialogResult = DialogResult.OK;
        }

        // Clavier du PC : chiffres, point ou virgule, retour arrière, Suppr (tout effacer), Entrée (OK), Échap (Annuler).
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Back: Key("⌫"); return true;
                case Keys.Delete: txt.Length = 0; fresh = false; Refresh2(); return true;
                case Keys.Enter: Ok(); return true;
                case Keys.Escape: DialogResult = DialogResult.Cancel; return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Chiffres, point et virgule : le caractère tapé (pavé numérique ou rangée du haut, quelle que soit la disposition du clavier).
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            char c = e.KeyChar;
            if (c >= '0' && c <= '9') Key(c.ToString());
            else if (c == '.' || c == ',') Key(".");
            else return;
            e.Handled = true;
        }

        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }

        public static long Parse(string s)
        {
            double v;
            if (!double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v <= 0) return 0;
            return (long)Math.Round(v * 1000.0);
        }

        public static string FormatHz(long hz)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1:000}.{2:000}", hz / 1000000, (hz / 1000) % 1000, hz % 1000);
        }
    }

    // Liste « Mem » : les 20 dernières fréquences, dans des cases arrondies, avec l'heure et la date ; ascenseur si elle
    // ne tient pas à l'écran. Clic : rappel. Clic droit ou appui long : la mémoriser dans « Mémoires » ou la supprimer.
    public class MemHistForm : Form
    {
        public MemHistEntry Chosen;
        readonly IMemHistHost host;
        readonly Panel list = new Panel { AutoScroll = true };

        readonly Rectangle wa;

        public MemHistForm(IMemHistHost host, Font baseFont, Rectangle workArea)
        {
            this.host = host;
            wa = workArea;
            Text = "Mem";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Font = baseFont;
            Controls.Add(list);
            Build();
            Theme.Apply(this);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        string Line(MemHistEntry e) { string w = e.When(); return host.MemHistText(e) + (w.Length > 0 ? "  " + w : ""); }

        void Build()
        {
            int u = Font.Height, gap = Math.Max(4, u / 3), pad = u * 3 / 4, rh = u * 2 + u / 2;

            List<MemHistEntry> items = host.MemHist;
            int w = u * 18;
            foreach (MemHistEntry e in items) w = Math.Max(w, TextRenderer.MeasureText(Line(e), Font).Width + u * 2);
            string hintText = items.Count == 0
                ? Lang.T("Liste vide : « Store » ou l'enregistrement automatique l'alimentent.", "Empty list: \"Store\" or the automatic storing fills it.")
                : Lang.T("Clic droit (ou appui long) sur une ligne : la mémoriser dans « Mémoires » ou la supprimer.", "Right-click (or long press) a line: store it in \"Memories\" or delete it.");
            w = Math.Min(w, wa.Width * 9 / 10 - 2 * pad - SystemInformation.VerticalScrollBarWidth);
            Size hs = TextRenderer.MeasureText(hintText, Font, new Size(w, 0), TextFormatFlags.WordBreak);
            int contentH = items.Count * (rh + gap) + hs.Height + gap;
            int chrome = Height - ClientSize.Height;
            int maxH = wa.Height * 9 / 10 - chrome - 2 * pad;
            bool scroll = contentH > maxH;
            int sb = scroll ? SystemInformation.VerticalScrollBarWidth : 0;

            list.SuspendLayout();
            Control[] old = new Control[list.Controls.Count];
            list.Controls.CopyTo(old, 0);
            list.Controls.Clear();
            foreach (Control c in old) { if (c.ContextMenuStrip != null) c.ContextMenuStrip.Dispose(); c.Dispose(); }
            list.AutoScrollPosition = Point.Empty;
            int y = 0;
            foreach (MemHistEntry e in items)
            {
                MemHistEntry it = e;
                RoundButton b = new RoundButton { Text = Line(e), Align = ContentAlignment.MiddleLeft };
                b.SetBounds(0, y, w, rh);
                b.Click += delegate { Chosen = it; DialogResult = DialogResult.OK; };
                ContextMenuStrip m = ItemMenu(it);
                b.ContextMenuStrip = m;
                b.LongPress += delegate { m.Show(Cursor.Position); };
                b.MouseWheel += delegate(object s, MouseEventArgs a) { Wheel(a.Delta); };
                list.Controls.Add(b);
                y += rh + gap;
            }
            Label hint = new Label { AutoSize = false, Text = hintText };
            hint.SetBounds(0, y, w, hs.Height);
            list.Controls.Add(hint);
            list.ResumeLayout();
            list.SetBounds(pad, pad, w + sb, Math.Min(contentH, maxH));
            ClientSize = new Size(w + sb + 2 * pad, list.Height + 2 * pad);
            Theme.Apply(list);
        }

        void Wheel(int delta)
        {
            if (!list.VerticalScroll.Visible) return;
            int step = Font.Height * 3 * Math.Sign(delta) * Math.Max(1, Math.Abs(delta) / 120);
            list.AutoScrollPosition = new Point(0, Math.Max(0, -list.AutoScrollPosition.Y - step));
        }

        ContextMenuStrip ItemMenu(MemHistEntry e)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            ToolStripMenuItem copy = new ToolStripMenuItem(Lang.T("Mémoriser dans « Mémoires »", "Store in \"Memories\""));
            string[] names = host.MemoryListLabels;
            for (int i = 0; i < names.Length; i++)
            {
                int k = i;
                copy.DropDownItems.Add(names[i], null, delegate { host.MemHistCopy(e, k); });
            }
            m.Items.Add(copy);
            m.Items.Add(Lang.T("Supprimer de la liste « Mem »", "Delete from the \"Mem\" list"), null, delegate
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (MessageBox.Show(this, host.MemHistText(e), Lang.T("Supprimer de la liste « Mem » ?", "Delete from the \"Mem\" list?"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    host.MemHist.Remove(e);
                    host.MemHistChanged();
                    Build();
                });
            });
            return m;
        }
    }
}
