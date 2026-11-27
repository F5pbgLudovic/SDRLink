using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SDRLink
{
    // ---------------------------------------------------------------- Notch, pas de la molette et bande passante par mode (1.38)
    public partial class MainForm
    {
        // ---- Notch automatique : case dans le bouton ; clic sur le bouton : options, niveau et explication.

        string notchSent;   // dernier état envoyé à la page (« marche|niveau »), null = à renvoyer

        // Mode numérique en cours : un décodeur des modes numériques en marche, ou DIGI comme mode.
        bool DigiListening { get { return settings.SstvAuto || modeKeyShown == "digi"; } }

        bool NotchWanted
        {
            get
            {
                if (!settings.AutoNotch) return false;
                if (settings.NotchOffDigi && DigiListening) return false;
                if (settings.NotchOffFdv && settings.FreeDvAuto) return false;
                return true;
            }
        }

        // Page (re)chargée : l'état du notch est renvoyé.
        void SetPageNotch()
        {
            notchSent = null;
            dnrSent = null;
            RefreshNotch();
            SendPageEq();
        }

        // Envoie à la page l'état voulu du notch, s'il a changé (appelé souvent : changement de mode, FreeDV, décodeur…).
        void RefreshNotch()
        {
            ConfigureRigNotch();
            if (web == null || web.CoreWebView2 == null || settings == null) return;
            RefreshDnr();
            bool on = NotchWanted;
            string s = (on ? "1" : "0") + "|" + settings.NotchAlgo + "|" + NotchLevelNow;
            if (s == notchSent) return;
            notchSent = s;
            web.CoreWebView2.ExecuteScriptAsync("window.__sdrlink && window.__sdrlink.setNotch && window.__sdrlink.setNotch(" + (on ? "true" : "false") + "," + NotchLevelNow + "," + settings.NotchAlgo + ");");
            if (web2 != null) Div2Notch();
        }

        // Types de notch (1.50, repris de PhoneSDRLink 1.30) : 0 = d'origine (filtres qui suivent les raies), 1 = adaptatif (NLMS),
        // 2 = spectral (FFT). Chaque type garde son niveau.
        int NotchLevelOf(int algo) { return algo == 2 ? settings.NotchLevelSpf : algo == 1 ? settings.NotchLevelAnf : settings.NotchLevel; }
        int NotchLevelNow { get { return NotchLevelOf(settings.NotchAlgo); } }

        // Notch du son du poste (1.50) : même marche, type et niveau que le WebSDR (pauses comprises), si la case le demande.
        void ConfigureRigNotch()
        {
            if (settings == null) return;
            RigNotch.Configure(settings.NotchRig && NotchWanted, settings.NotchAlgo, NotchLevelNow);
        }
        string NotchName(int algo) { return algo == 2 ? Lang.T("spectral", "spectral") : algo == 1 ? Lang.T("adaptatif", "adaptive") : Lang.T("d'origine", "original"); }

        Form notchWin;

        // Fenêtre du notch (clic sur le bouton, hors de la case) : deux cases, le niveau, puis l'explication. Sa largeur suit la taille du
        // texte de SDRLink et sa hauteur s'adapte à l'explication (pas d'ascenseur). Elle se ferme dès qu'on clique ailleurs.
        void ShowNotchMenu()
        {
            if (notchWin != null && !notchWin.IsDisposed) { notchWin.Close(); return; }
            Form f = new Form();
            f.Text = Lang.T("Notch auto (DNF)", "Auto notch (DNF)");
            f.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.Manual;
            f.Font = Font;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.KeyPreview = true;
            f.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) f.Close(); };
            int width = (int)Math.Round(TextRenderer.MeasureText("abcdefghijklmnopqrstuvwxyz", Font).Width * 2.4);   // environ 75 caractères

            FlowLayoutPanel p = new FlowLayoutPanel();
            p.FlowDirection = FlowDirection.TopDown;
            p.WrapContents = false;
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.Padding = new Padding(10, 8, 10, 10);

            CheckBox digi = new CheckBox { AutoSize = true, Text = Lang.T("Notch auto désactivé en mode numérique", "Auto notch off in digital modes"), Checked = settings.NotchOffDigi };
            digi.CheckedChanged += delegate { settings.NotchOffDigi = digi.Checked; NotchChanged(); };
            CheckBox fdv = new CheckBox { AutoSize = true, Text = Lang.T("Notch auto désactivé en FreeDV V1 et V2", "Auto notch off in FreeDV V1 and V2"), Checked = settings.NotchOffFdv };
            fdv.CheckedChanged += delegate { settings.NotchOffFdv = fdv.Checked; NotchChanged(); };
            CheckBox rig = new CheckBox { AutoSize = true, Text = Lang.T("Notch aussi sur le son du poste joué par SDRLink (écoute sur le PC, diversité)", "Notch also on the rig audio played by SDRLink (listening on the PC, diversity)"), Checked = settings.NotchRig };
            rig.CheckedChanged += delegate { settings.NotchRig = rig.Checked; NotchChanged(); };

            // Type de notch (1.50) : choisir un type le met en marche ; le niveau ci-dessous est celui du type choisi.
            Label tl = new Label { AutoSize = true, Text = Lang.T("Type de notch :", "Notch type:"), Margin = new Padding(0, 8, 0, 2) };
            string[] names = {
                Lang.T("D'origine : filtres qui suivent les sifflements (4 à 8)", "Original: filters following the whistles (4 to 8)"),
                Lang.T("Adaptatif (NLMS) : suit les sifflements qui glissent, en traite autant qu'il y en a", "Adaptive (NLMS): follows drifting whistles, handles as many as there are"),
                Lang.T("Spectral (FFT) : le plus propre, ne touche que les raies stables", "Spectral (FFT): the cleanest, only touches steady lines") };
            RadioButton[] types = new RadioButton[3];
            for (int a = 0; a < 3; a++)
                types[a] = new RadioButton { AutoSize = true, Text = names[a], Checked = settings.NotchAlgo == a, Margin = new Padding(12, 0, 0, 0) };

            FlowLayoutPanel row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 6) };
            Label ll = new Label { AutoSize = true, Text = Lang.T("Niveau du notch :", "Notch level:"), Margin = new Padding(0, 6, 6, 0) };
            ComboBox lvl = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            bool filling = false;
            Action fillLevels = delegate
            {
                filling = true;
                lvl.Items.Clear();
                for (int i = 1; i <= 10; i++)
                    lvl.Items.Add(i == 1 ? (settings.NotchAlgo == 0 ? Lang.T("1 (réglage d'origine)", "1 (original setting)") : Lang.T("1 (le plus doux)", "1 (gentlest)"))
                        : i == 10 ? Lang.T("10 (le plus fort)", "10 (strongest)") : i.ToString(CultureInfo.InvariantCulture));
                lvl.SelectedIndex = Math.Max(0, Math.Min(9, NotchLevelNow - 1));
                filling = false;
            };
            fillLevels();
            int lw = TextRenderer.MeasureText(Lang.T("1 (réglage d'origine)", "1 (original setting)"), Font).Width;
            lw = Math.Max(lw, TextRenderer.MeasureText(Lang.T("1 (le plus doux)", "1 (gentlest)"), Font).Width);
            lw = Math.Max(lw, TextRenderer.MeasureText(Lang.T("10 (le plus fort)", "10 (strongest)"), Font).Width);
            lvl.Width = lw + SystemInformation.VerticalScrollBarWidth + 12;
            lvl.SelectedIndexChanged += delegate
            {
                if (filling) return;
                int v = lvl.SelectedIndex + 1;
                if (settings.NotchAlgo == 2) settings.NotchLevelSpf = v;
                else if (settings.NotchAlgo == 1) settings.NotchLevelAnf = v;
                else settings.NotchLevel = v;
                NotchChanged();
            };
            for (int a = 0; a < 3; a++)
            {
                int algo = a;
                types[a].CheckedChanged += delegate
                {
                    if (!types[algo].Checked || settings.NotchAlgo == algo) return;
                    settings.NotchAlgo = algo;
                    fillLevels();
                    if (!chkNotch.Checked) chkNotch.Checked = true;   // choisir un type met le notch en marche (comme le DNR)
                    NotchChanged();
                };
            }
            row.Controls.Add(ll);
            row.Controls.Add(lvl);

            Label help = new Label();
            help.AutoSize = true;
            help.MaximumSize = new Size(width, 0);
            help.Margin = new Padding(0, 4, 0, 8);
            help.Text = Lang.T("Le notch automatique repère les sifflements fixes (porteuses) dans le son du WebSDR et les atténue. Un clic sur le bouton le met en marche ou l'arrête (bouton vert en marche, couleurs au choix ci-dessous) ; le clic droit ouvre cette fenêtre. Trois types : d'origine (de 4 à 8 filtres étroits qui suivent les sifflements ; le niveau augmente leur nombre et leur largeur), adaptatif (un filtre qui retire tout ce qui se répète, même un sifflement qui glisse ; le niveau règle sa vitesse, un niveau élevé peut entamer la voix) et spectral (chaque fréquence qui dépasse nettement ses voisines pendant un moment est ramenée à leur niveau ; le plus propre, le niveau règle le seuil et la force). Chaque type garde son niveau, de 1 à 10. Il est mis en pause, sans arrêter le bouton, pendant un mode numérique (décodeur en marche ou mode DIGI) et pendant FreeDV, si les choix ci-dessus sont cochés : il abîmerait les signaux à décoder. Le câble SDRLINK et les décodeurs reçoivent toujours le son sans notch.",
                "The automatic notch detects steady whistles (carriers) in the WebSDR sound and attenuates them. A click on the button turns it on or off (green button when on, colours to choose below); right-click opens this window. Three types: original (4 to 8 narrow filters following the whistles; the level increases their number and width), adaptive (a filter removing everything that repeats, even a drifting whistle; the level sets its speed, a high level can affect the voice) and spectral (each frequency clearly above its neighbours for a while is brought down to their level; the cleanest, the level sets the threshold and the strength). Each type keeps its own level, 1 to 10. It is paused, without turning the button off, during a digital mode (decoder running or DIGI mode) and during FreeDV, if the choices above are ticked: it would damage the signals to decode. The SDRLINK cable and the decoders always receive the sound without notch.");

            // Couleurs du bouton (1.48 : le clic droit du bouton ouvre cette fenêtre, plus le menu des couleurs).
            Button colors = new Button { AutoSize = true, Text = Lang.T("Couleurs du bouton…", "Button colours…"), Margin = new Padding(0, 0, 0, 8) };
            colors.Click += delegate { if (notchColorMenu != null) notchColorMenu.Show(chkNotch, new Point(0, chkNotch.Height)); f.Close(); };
            Button close = new Button { AutoSize = true, Text = Lang.T("Fermer", "Close"), Anchor = AnchorStyles.Right };
            close.Click += delegate { f.Close(); };
            f.CancelButton = close;

            p.Controls.Add(digi);
            p.Controls.Add(fdv);
            p.Controls.Add(rig);
            p.Controls.Add(tl);
            foreach (RadioButton r in types) p.Controls.Add(r);
            p.Controls.Add(row);
            p.Controls.Add(help);
            p.Controls.Add(colors);
            p.Controls.Add(close);
            f.Controls.Add(p);
            Theme.Apply(f);
            Theme.TitleBar(f);

            // Sous le bouton, gardée dans l'écran.
            Size sz = f.GetPreferredSize(Size.Empty);
            Rectangle wa = WorkArea;
            Point at = chkNotch.PointToScreen(new Point(0, chkNotch.Height));
            f.Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - sz.Width, at.X)), Math.Max(wa.Top, Math.Min(wa.Bottom - sz.Height, at.Y)));
            f.Deactivate += delegate { BeginInvokeSafe(delegate { if (!f.IsDisposed) f.Close(); }); };
            f.FormClosed += delegate { if (notchWin == f) notchWin = null; f.Dispose(); };
            notchWin = f;
            f.Show(this);
        }

        void NotchChanged()
        {
            settings.Save();
            Log.Write("Notch auto : type " + settings.NotchAlgo + ", niveau " + NotchLevelNow + (settings.NotchOffDigi ? ", coupé en mode numérique" : "") + (settings.NotchOffFdv ? ", coupé en FreeDV" : "") + (settings.NotchRig ? ", aussi sur le son du poste" : ""));
            RefreshNotch();
            ShowStatus(Lang.F("Notch auto : {0}, niveau {1}{2}{3}", "Auto notch: {0}, level {1}{2}{3}", NotchName(settings.NotchAlgo), NotchLevelNow,
                settings.NotchOffDigi ? Lang.T(", coupé en mode numérique", ", off in digital modes") : "",
                settings.NotchOffFdv ? Lang.T(", coupé en FreeDV", ", off in FreeDV") : ""));
        }

        // ---- Réducteur de bruit (DNR, 1.46 ; trois types en 1.48) : bouton après le notch ; clic = marche / arrêt, clic droit = type
        // (classique, amélioré, IA) avec le niveau de chacun dans un sous-menu, coupures, couleurs.

        Button btnDnr;
        string dnrSent;   // dernier état envoyé à la page (« marche|type|niveau »), null = à renvoyer

        bool DnrWanted
        {
            get
            {
                if (!settings.Dnr) return false;
                if (settings.DnrOffDigi && DigiListening) return false;
                if (settings.DnrOffFdv && settings.FreeDvAuto) return false;
                return true;
            }
        }

        // Niveau retenu pour chaque type : 0 = classique, 1 = amélioré (MMSE-LSA), 2 = IA (RNNoise).
        int DnrLevelOf(int algo) { return algo == 2 ? settings.DnrLevelIa : algo == 1 ? settings.DnrLevelLsa : settings.DnrLevel; }
        int DnrLevelNow { get { return DnrLevelOf(settings.DnrAlgo); } }
        string DnrName(int algo) { return algo == 2 ? Lang.T("IA", "AI") : algo == 1 ? Lang.T("amélioré", "improved") : Lang.T("classique", "classic"); }

        void CreateDnrButton()
        {
            btnDnr = new Button();
            btnDnr.AutoSize = true;
            btnDnr.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnDnr.MinimumSize = new Size(30, 25);
            btnDnr.Tag = Theme.Custom;
            btnDnr.Margin = new Padding(3, 3, 3, 3);
            btnDnr.Click += delegate { SetDnr(!settings.Dnr); };
            ContextMenuStrip m = new ContextMenuStrip();
            m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e) { e.Cancel = false; FillDnrMenu(m); };
            m.Items.Add("-");
            btnDnr.ContextMenuStrip = m;
            AddColorMenu(btnDnr, Slot("DnrOff", () => Lang.T("Couleur DNR arrêté", "Colour when DNR is off")),
                Slot("DnrOn", () => Lang.T("Couleur DNR en marche", "Colour when DNR is on")));
            Tr(delegate
            {
                UpdateDnrButton();
                tips.SetToolTip(btnDnr, Lang.T("Réducteur de bruit (DNR) sur le son du WebSDR. Clic : marche / arrêt. Clic droit : type (classique, amélioré ou IA) et son niveau de 1 à 10, coupure en mode numérique et en FreeDV, couleurs",
                    "Noise reduction (DNR) on the WebSDR sound. Click: on / off. Right-click: type (classic, improved or AI) and its level 1 to 10, off in digital modes and FreeDV, colours"));
            });
        }

        // Menu du clic droit : les trois types (coché = celui en service) ; le niveau de chacun est dans son sous-menu, qui s'ouvre
        // au survol ou par un clic droit sur le type. Choisir un type ou un niveau met le DNR en marche.
        void FillDnrMenu(ContextMenuStrip m)
        {
            m.Items.Clear();
            ToolStripMenuItem head = new ToolStripMenuItem(Lang.T("Type de DNR (clic droit sur un type : son niveau) :", "DNR type (right-click a type: its level):"));
            head.Enabled = false;
            m.Items.Add(head);
            string[] names = {
                Lang.T("Classique (filtre de Wiener)", "Classic (Wiener filter)"),
                Lang.T("Amélioré (MMSE-LSA) : voix moins déformée", "Improved (MMSE-LSA): less voice distortion"),
                Lang.T("IA (RNNoise) : le plus efficace, plus de calcul", "AI (RNNoise): most effective, more computing") };
            ToolStripMenuItem[] types = new ToolStripMenuItem[3];
            for (int a = 0; a < 3; a++)
            {
                int algo = a;
                ToolStripMenuItem t = types[a] = new ToolStripMenuItem(names[a]);
                t.Checked = settings.DnrAlgo == a;
                ToolStripMenuItem[] levels = new ToolStripMenuItem[10];
                for (int i = 1; i <= 10; i++)
                {
                    int lvl = i;
                    string text = i == 1 ? Lang.T("1 (le plus doux)", "1 (gentlest)") : i == 10 ? Lang.T("10 (le plus fort)", "10 (strongest)") : i.ToString(CultureInfo.InvariantCulture);
                    ToolStripMenuItem it = levels[i - 1] = new ToolStripMenuItem(text);
                    it.Checked = DnrLevelOf(a) == i;
                    it.Click += delegate { SetDnrType(algo, lvl); };
                    t.DropDownItems.Add(it);
                }
                // Clic gauche (ou Entrée) : ce type ; clic droit : seulement ouvrir ses niveaux (Windows y ajoute un clic, ignoré ici).
                MouseButtons down = MouseButtons.None;
                t.MouseDown += delegate(object s, MouseEventArgs e) { down = e.Button; };
                t.Click += delegate
                {
                    bool right = down == MouseButtons.Right;
                    down = MouseButtons.None;
                    if (right) return;
                    SetDnrType(algo, DnrLevelOf(algo));
                    foreach (ToolStripMenuItem x in types) x.Checked = x == t;   // le menu reste ouvert : les coches suivent
                };
                t.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) t.ShowDropDown(); };
                m.Items.Add(t);
            }
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem digi = new ToolStripMenuItem(Lang.T("DNR coupé en mode numérique", "DNR off in digital modes"));
            digi.Checked = settings.DnrOffDigi;
            digi.Click += delegate { settings.DnrOffDigi = !settings.DnrOffDigi; DnrChanged(); };
            m.Items.Add(digi);
            ToolStripMenuItem fdv = new ToolStripMenuItem(Lang.T("DNR coupé en FreeDV V1 et V2", "DNR off in FreeDV V1 and V2"));
            fdv.Checked = settings.DnrOffFdv;
            fdv.Click += delegate { settings.DnrOffFdv = !settings.DnrOffFdv; DnrChanged(); };
            m.Items.Add(fdv);
            ToolStripMenuItem rig = new ToolStripMenuItem(Lang.T("DNR aussi sur le son du poste joué par SDRLink (écoute sur le PC, diversité)", "DNR also on the rig audio played by SDRLink (listening on the PC, diversity)"));
            rig.Checked = settings.DnrRig;
            rig.Click += delegate { settings.DnrRig = !settings.DnrRig; DnrChanged(); };
            m.Items.Add(rig);
        }

        // DNR du son du poste (1.48) : même marche, type et niveau que le WebSDR (coupures comprises), si la case le demande.
        void ConfigureRigDnr()
        {
            if (settings == null) return;
            NoiseReducer.Configure(settings.DnrRig && DnrWanted, settings.DnrAlgo, DnrLevelNow);
        }

        void SetDnr(bool on)
        {
            settings.Dnr = on;
            settings.Save();
            Log.Write("DNR : " + (on ? "en marche, type " + settings.DnrAlgo + ", niveau " + DnrLevelNow : "arrêté"));
            RefreshDnr();
            UpdateDnrButton();
            if (on && !DnrWanted)
                ShowStatus(Lang.F("DNR en marche ({0}, niveau {1}), en pause pendant le mode numérique ou FreeDV (clic droit pour changer)", "DNR on ({0}, level {1}), paused during the digital mode or FreeDV (right-click to change)", DnrName(settings.DnrAlgo), DnrLevelNow));
            else
                ShowStatus(on ? Lang.F("DNR en marche : {0}, niveau {1}", "DNR on: {0}, level {1}", DnrName(settings.DnrAlgo), DnrLevelNow) : Lang.T("DNR arrêté", "DNR off"));
        }

        // Choix d'un type et de son niveau (menu du clic droit) ; met le DNR en marche s'il était arrêté.
        void SetDnrType(int algo, int lvl)
        {
            algo = Math.Max(0, Math.Min(2, algo));
            lvl = Math.Max(1, Math.Min(10, lvl));
            settings.DnrAlgo = algo;
            if (algo == 2) settings.DnrLevelIa = lvl;
            else if (algo == 1) settings.DnrLevelLsa = lvl;
            else settings.DnrLevel = lvl;
            if (!settings.Dnr) { SetDnr(true); return; }
            DnrChanged();
        }

        void DnrChanged()
        {
            settings.Save();
            Log.Write("DNR : type " + settings.DnrAlgo + ", niveau " + DnrLevelNow + (settings.DnrOffDigi ? ", coupé en mode numérique" : "") + (settings.DnrOffFdv ? ", coupé en FreeDV" : "") + (settings.DnrRig ? ", aussi sur le son du poste" : ""));
            RefreshDnr();
            UpdateDnrButton();
            ShowStatus(Lang.F("DNR : {0}, niveau {1}{2}{3}", "DNR: {0}, level {1}{2}{3}", DnrName(settings.DnrAlgo), DnrLevelNow,
                settings.DnrOffDigi ? Lang.T(", coupé en mode numérique", ", off in digital modes") : "",
                settings.DnrOffFdv ? Lang.T(", coupé en FreeDV", ", off in FreeDV") : ""));
        }

        // Envoie à la page l'état voulu du DNR, s'il a changé (appelé avec RefreshNotch : mode, FreeDV, décodeur…).
        void RefreshDnr()
        {
            ConfigureRigDnr();
            if (web == null || web.CoreWebView2 == null || settings == null) return;
            bool on = DnrWanted;
            string s = (on ? "1" : "0") + "|" + settings.DnrAlgo + "|" + DnrLevelNow;
            if (s == dnrSent) return;
            dnrSent = s;
            web.CoreWebView2.ExecuteScriptAsync("window.__sdrlink && window.__sdrlink.setDnr && window.__sdrlink.setDnr(" + (on ? "true" : "false") + "," + DnrLevelNow + "," + settings.DnrAlgo + ");");
            if (web2 != null) Div2Notch();
        }

        // Couleur de l'état : en marche / arrêté ; en marche, le bouton dit le type (DNR, DNR+, DNR IA) et le niveau.
        void UpdateDnrButton()
        {
            if (btnDnr == null) return;
            ConfigureRigDnr();
            string text = !settings.Dnr ? "DNR" : (settings.DnrAlgo == 2 ? Lang.T("DNR IA", "DNR AI") : settings.DnrAlgo == 1 ? "DNR+" : "DNR") + " " + DnrLevelNow;
            if (btnDnr.Text != text) btnDnr.Text = text;
            int c = ColorOf(settings.Dnr ? "DnrOn" : "DnrOff");
            if (c < 0) c = settings.Dnr ? 0x00783C : -1;   // en marche : vert par défaut, comme la diversité calée
            if (c < 0) Theme.StyleButton(btnDnr);
            else PaintCustom(btnDnr, c);
        }

        // ---- Pas de la molette et bande passante, retenus pour chaque mode (cw, ssb = USB et LSB, am, fm, digi).

        string modeKeyShown;   // mode dont le pas et la bande passante sont affichés (null tant qu'aucun mode n'est connu)
        bool stepLoading;
        ComboBox cbBw;
        readonly List<int> bwItems = new List<int>();   // valeur de chaque ligne de la liste (Hz ; 0 = filtre d'origine ; -1 = autre valeur)
        System.Windows.Forms.Timer bwTimer;

        static string StepKey(string activeKey) { return activeKey == "usb" || activeKey == "lsb" ? "ssb" : activeKey; }
        static int DefaultStep(string k) { return k == "cw" ? 10 : k == "am" || k == "fm" ? 1000 : 500; }
        static int DefaultBw(string k) { return k == "cw" ? 300 : k == "ssb" ? 2900 : k == "am" ? 6000 : k == "fm" ? 8000 : 0; }
        int StepFor(string k) { int v; return settings.ModeSteps.TryGetValue(k, out v) ? v : DefaultStep(k); }
        int BwFor(string k) { int v; return settings.ModeBws.TryGetValue(k, out v) ? v : DefaultBw(k); }

        // Largeurs proposées (Hz), comme PhoneSDRLink ; « filtre d'origine » et « autre valeur » sont toujours ajoutés.
        static int[] BwChoices(string k)
        {
            if (k == "cw") return new[] { 100, 200, 300, 400, 500, 800 };
            if (k == "ssb") return new[] { 2100, 2400, 2600, 2800, 2900, 3000, 4000, 5000 };
            if (k == "am") return new[] { 6000, 8000, 10000 };
            if (k == "fm") return new[] { 8000, 10000, 12000, 15000 };
            return new[] { 200, 300, 500, 2100, 2400, 2800, 3000, 4000 };
        }

        static string BwText(int hz)
        {
            if (hz < 1000) return hz + " Hz";
            if (hz % 1000 == 0) return (hz / 1000) + " kHz";
            return (hz / 1000.0).ToString("0.###", CultureInfo.InvariantCulture).Replace(".", Lang.T(",", ".")) + " kHz";
        }

        static string ModeKeyName(string k)
        {
            switch (k) { case "cw": return "CW"; case "ssb": return "USB/LSB"; case "am": return "AM"; case "fm": return "FM"; default: return "DIGI"; }
        }

        void CreateBwCombo(Control row)
        {
            cbBw = new ComboBox();
            cbBw.DropDownStyle = ComboBoxStyle.DropDownList;
            cbBw.Width = 92;
            cbBw.Margin = new Padding(2, 3, 2, 3);
            cbBw.SelectionChangeCommitted += delegate { OnBwChosen(); };
            Tr(delegate
            {
                FillBwCombo();
                tips.SetToolTip(cbBw, Lang.T("Bande passante de réception du WebSDR, retenue pour chaque mode (CW, USB/LSB, AM, FM, DIGI), sur tous les WebSDR et après un redémarrage. « Filtre d'origine » : celui du WebSDR.",
                    "WebSDR receive bandwidth, kept for each mode (CW, USB/LSB, AM, FM, DIGI), on every WebSDR and after a restart. \"Default filter\": the WebSDR's own."));
            });
            row.Controls.Add(Lbl("BP :", "BW:"));
            row.Controls.Add(cbBw);
        }

        void FillBwCombo()
        {
            if (cbBw == null) return;
            string k = modeKeyShown ?? "ssb";
            int cur = BwFor(k);
            bwItems.Clear();
            cbBw.Items.Clear();
            bwItems.Add(0); cbBw.Items.Add(Lang.T("Filtre d'origine", "Default filter"));
            List<int> vals = new List<int>(BwChoices(k));
            if (cur > 0 && !vals.Contains(cur)) { vals.Add(cur); vals.Sort(); }
            foreach (int v in vals) { bwItems.Add(v); cbBw.Items.Add(BwText(v)); }
            bwItems.Add(-1); cbBw.Items.Add(Lang.T("Autre…", "Other…"));
            cbBw.SelectedIndex = Math.Max(0, bwItems.IndexOf(cur));
        }

        void OnBwChosen()
        {
            int i = cbBw.SelectedIndex;
            if (i < 0 || i >= bwItems.Count) return;
            int v = bwItems[i];
            if (v < 0)
            {
                int cur = BwFor(modeKeyShown ?? "ssb");
                double k = AskBwKHz(cur);
                if (k < 0) { FillBwCombo(); return; }
                v = (int)Math.Round(k * 1000);
            }
            SetBw(v);
        }

        // Petite fenêtre : largeur en kHz (0,1 à 15) ; -1 si annulée ou invalide.
        double AskBwKHz(int cur)
        {
            using (Form d = new Form())
            {
                d.Text = Lang.T("Bande passante", "Bandwidth");
                d.FormBorderStyle = FormBorderStyle.FixedDialog;
                d.MaximizeBox = d.MinimizeBox = false;
                d.ShowInTaskbar = false;
                d.StartPosition = FormStartPosition.CenterParent;
                d.AutoSize = true;
                d.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                d.Padding = new Padding(10);
                FlowLayoutPanel p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
                Label l = new Label { AutoSize = true, Text = Lang.F("Bande passante en kHz pour {0} (0,1 à 15) :", "Bandwidth in kHz for {0} (0.1 to 15):", ModeKeyName(modeKeyShown ?? "ssb")) };
                TextBox t = new TextBox { Width = 120, Text = cur > 0 ? (cur / 1000.0).ToString("0.###", CultureInfo.InvariantCulture).Replace(".", Lang.T(",", ".")) : "" };
                FlowLayoutPanel b = new FlowLayoutPanel { AutoSize = true };
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
                Button no = new Button { Text = Lang.T("Annuler", "Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
                b.Controls.Add(ok); b.Controls.Add(no);
                p.Controls.Add(l); p.Controls.Add(t); p.Controls.Add(b);
                d.Controls.Add(p);
                d.AcceptButton = ok; d.CancelButton = no;
                Theme.Apply(d);
                if (d.ShowDialog(this) != DialogResult.OK) return -1;
                double k;
                if (!double.TryParse(t.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out k) || k < 0.1 || k > 15)
                {
                    ShowStatus(Lang.T("Bande passante invalide (0,1 à 15 kHz)", "Invalid bandwidth (0.1 to 15 kHz)"));
                    return -1;
                }
                return k;
            }
        }

        void SetBw(int hz)
        {
            string k = modeKeyShown ?? "ssb";
            settings.ModeBws[k] = hz;
            settings.Save();
            FillBwCombo();
            Log.Write("Bande passante " + ModeKeyName(k) + " : " + (hz == 0 ? "filtre d'origine" : BwText(hz)));
            if (hz == 0)
            {
                // Retour au filtre d'origine : le mode est simplement réappliqué à la page.
                if (web.CoreWebView2 != null) web.CoreWebView2.ExecuteScriptAsync("window.__sdrlink && window.__sdrlink.resetBw && window.__sdrlink.resetBw();");
                ShowStatus(Lang.F("Bande passante {0} : filtre d'origine du WebSDR", "{0} bandwidth: WebSDR default filter", ModeKeyName(k)));
            }
            else
            {
                PushBw();
                ShowStatus(Lang.F("Bande passante {0} : {1}", "{0} bandwidth: {1}", ModeKeyName(k), BwText(hz)));
            }
        }

        // Bande passante du mode affiché envoyée à la page (0 : la page garde son filtre).
        void PushBw()
        {
            if (web == null || web.CoreWebView2 == null || sdrKind == "" || modeKeyShown == null) return;
            int bw = BwFor(modeKeyShown);
            web.CoreWebView2.ExecuteScriptAsync(bw > 0
                ? "window.__sdrlink && window.__sdrlink.setBw && window.__sdrlink.setBw(" + bw + ");"
                : "window.__sdrlink && (window.__sdrlink.bw = 0);");
        }

        // Envoi un peu plus tard (la page vient de changer de mode ou d'être chargée et remet d'abord son propre filtre).
        void PushBwLater(int ms)
        {
            if (bwTimer == null)
            {
                bwTimer = new System.Windows.Forms.Timer();
                bwTimer.Tick += delegate { bwTimer.Stop(); PushBw(); };
            }
            bwTimer.Stop();
            bwTimer.Interval = Math.Max(50, ms);
            bwTimer.Start();
        }

        // Le mode affiché a changé (bouton de mode, poste, page) : son pas, sa bande passante, et le notch selon le mode.
        void OnModeKeyChanged(string activeKey)
        {
            if (string.IsNullOrEmpty(activeKey)) return;
            string k = StepKey(activeKey);
            if (k == modeKeyShown) return;
            modeKeyShown = k;
            ShowStepFor(k);
            FillBwCombo();
            if (web != null && web.CoreWebView2 != null)
                web.CoreWebView2.ExecuteScriptAsync("window.__sdrlink && (window.__sdrlink.bw = " + BwFor(k) + ");");
            PushBwLater(700);
            RefreshNotch();
        }

        void ShowStepFor(string k)
        {
            int want = StepFor(k);
            int best = 0;
            for (int i = 0; i < Steps.Length; i++) if (Math.Abs(Steps[i] - want) < Math.Abs(Steps[best] - want)) best = i;
            stepLoading = true;
            cbStep.SelectedIndex = best;
            stepLoading = false;
        }

        void OnStepChosen()
        {
            if (stepLoading || modeKeyShown == null || cbStep.SelectedIndex < 0) return;
            settings.ModeSteps[modeKeyShown] = (int)Steps[cbStep.SelectedIndex];
            settings.TuneStepHz = Steps[cbStep.SelectedIndex];
            settings.Save();
            Log.Write("Pas " + ModeKeyName(modeKeyShown) + " : " + Steps[cbStep.SelectedIndex] + " Hz");
        }

        // ---- Recherche du mode écouté (bouton « Mode ? » après le bouton des modes numériques)

        Button btnFindMode;
        ModeProbe probe;
        DateTime probeEnd;
        System.Windows.Forms.Timer probeTimer;
        bool probeExtended, probeRhythmShown;

        void CreateFindModeButton(Control row)
        {
            btnFindMode = Btn("Mode ?", "Mode?", delegate { if (probe != null) StopProbe(null, null); else StartProbe(); });
            btnFindMode.Tag = Theme.Custom;
            btnFindMode.FlatStyle = FlatStyle.Flat;
            btnFindMode.FontChanged += delegate { UpdateFindModeButton(); };
            Tr(delegate
            {
                UpdateFindModeButton();
                tips.SetToolTip(btnFindMode, Lang.F("Recherche du mode numérique écouté : pendant {0} s au plus, le son est essayé avec FT8, FT4, FT2, JS8, SSTV, WEFAX, RTTY, PSK31, CW et Olivia ; le mode reconnu est choisi et son décodage mis en marche (sans changer de fréquence). Nouveau clic : arrêter la recherche",
                    "Search for the digital mode being heard: for {0} s at most, the sound is tried with FT8, FT4, FT2, JS8, SSTV, WEFAX, RTTY, PSK31, CW and Olivia; the recognised mode is chosen and its decoding started (without changing frequency). Click again: stop the search", ModeProbe.Seconds));
            });
            row.Controls.Add(btnFindMode);
            AddColorMenu(btnFindMode, Slot("FindMode", () => Lang.T("Couleur du bouton", "Button colour")),
                Slot("FindModeBusy", () => Lang.T("Couleur pendant la recherche", "Colour while searching")));
        }

        void UpdateFindModeButton()
        {
            if (btnFindMode == null) return;
            // Une seule largeur (celle du plus long des deux textes) : le bouton ne bouge pas pendant la recherche.
            string idle = Lang.T("Mode ?", "Mode?"), busy = Lang.F("Recherche… {0} s", "Searching… {0} s", ModeProbe.Seconds);
            int w = 0;
            foreach (string t in new[] { idle, busy }) w = Math.Max(w, TextRenderer.MeasureText(t, btnFindMode.Font).Width + 18);
            if (btnFindMode.MinimumSize.Width != w || btnFindMode.MinimumSize.Height != Mh) btnFindMode.MinimumSize = new Size(w, Mh);
            if (probe != null)
            {
                int left = Math.Max(0, (int)Math.Ceiling((probeEnd - DateTime.UtcNow).TotalSeconds));
                btnFindMode.Text = Lang.F("Recherche… {0} s", "Searching… {0} s", left);
                btnFindMode.BackColor = Color.FromArgb(255, 140, 0); btnFindMode.ForeColor = Color.Black; btnFindMode.FlatAppearance.BorderColor = Color.FromArgb(200, 100, 0);
                int cb = ColorOf("FindModeBusy");
                if (cb >= 0) PaintCustom(btnFindMode, cb);
            }
            else
            {
                btnFindMode.Text = Lang.T("Mode ?", "Mode?");
                int c = ColorOf("FindMode");
                if (c >= 0) PaintCustom(btnFindMode, c);
                else { btnFindMode.BackColor = Theme.ButtonBack; btnFindMode.ForeColor = Theme.Fore; btnFindMode.FlatAppearance.BorderColor = Theme.Border; }
            }
        }

        void StartProbe()
        {
            if (sdrKind == "" && worker == null)
            {
                ShowStatus(Lang.T("Recherche du mode : ni WebSDR ni poste à écouter", "Mode search: no WebSDR or rig to listen to"));
                return;
            }
            ModeProbe p = new ModeProbe(settings.DigiGet("js8Speed", 1));
            p.Sure += delegate(string mode, string detail) { BeginInvokeSafe(delegate { if (probe == p) StopProbe(mode, detail); }); };
            probe = p;
            probeExtended = false; probeRhythmShown = false;
            probeEnd = DateTime.UtcNow.AddSeconds(ModeProbe.Seconds);
            if (probeTimer == null)
            {
                probeTimer = new System.Windows.Forms.Timer { Interval = 500 };
                probeTimer.Tick += delegate
                {
                    if (probe == null) { probeTimer.Stop(); return; }
                    string rm = probe.RhythmMode;
                    if (rm != null && !probeRhythmShown)
                    {
                        probeRhythmShown = true;
                        ShowStatus(probe.RhythmDetail + Lang.T(" : attente d'un message décodé…", ": waiting for a decoded message…"));
                    }
                    if (DateTime.UtcNow >= probeEnd)
                    {
                        // Rythme FT8 / FT4 reconnu sans message : encore une tranche pour en décoder un (le retard du son vient d'être corrigé).
                        if (rm != null && !probeExtended) { probeExtended = true; probeEnd = DateTime.UtcNow.AddSeconds(rm == "FT8" ? 20 : 12); UpdateFindModeButton(); return; }
                        if (rm != null) { StopProbe(rm, probe.RhythmDetail); return; }
                        string d; int s; string m = probe.BestText(out d, out s); StopProbe(m, d); return;
                    }
                    string d2; int s2; string m2 = probe.BestText(out d2, out s2);
                    if (m2 != null && s2 >= 3 * ModeProbe.Threshold) { StopProbe(m2, d2); return; }   // texte très clair : inutile d'attendre
                    UpdateFindModeButton();
                };
            }
            probeTimer.Start();
            Log.Write("Recherche du mode : démarrée (" + ModeProbe.Seconds + " s au plus)");
            ShowStatus(Lang.F("Recherche du mode écouté ({0} s au plus)…", "Searching for the mode being heard ({0} s at most)…", ModeProbe.Seconds));
            UpdateFindModeButton();
            UpdateFreeDv();   // capture du son mise en marche si rien ne décodait
        }

        // Fin de la recherche : mode trouvé (choisi et décodé, sans changer de fréquence), ou rien (mode null).
        void StopProbe(string mode, string detail)
        {
            ModeProbe p = probe;
            if (p == null) return;
            probe = null;
            if (probeTimer != null) probeTimer.Stop();
            p.Stop();
            UpdateFindModeButton();
            if (mode != null)
            {
                Log.Write("Recherche du mode : " + mode + " (" + detail + ")");
                SelectDigiMode(mode, false, true);
                // Retard du son mesuré pendant la recherche : donné au décodeur FT8 / FT4 / FT2, qui n'a pas à le réapprendre.
                double? sh = p.ShiftFor(mode);
                Ft8Engine fe = digi as Ft8Engine;
                if (sh.HasValue && fe != null && fe.Name == mode) fe.SetShift(sh.Value);
                ShowStatus(Lang.F("Mode trouvé : {0} ({1})", "Mode found: {0} ({1})", DigiName(mode), detail));
            }
            else
            {
                bool early = DateTime.UtcNow < probeEnd;
                Log.Write("Recherche du mode : " + (early ? "arrêtée" : "aucun mode reconnu"));
                ShowStatus(early ? Lang.T("Recherche du mode arrêtée", "Mode search stopped")
                    : Lang.T("Recherche du mode : aucun mode reconnu (signal trop faible, mode non pris en charge, ou WSPR / JT9 / Hell, trop lents à reconnaître)",
                        "Mode search: no mode recognised (signal too weak, unsupported mode, or WSPR / JT9 / Hell, too slow to recognise)"));
            }
            UpdateFreeDv();   // capture arrêtée si plus rien ne décode
        }
    }
}
