using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SDRLink
{
    // Fenêtre « Réglages du mode » (clic droit sur un mode dans le menu du bouton des modes numériques) : explication du mode,
    // surbrillance (modes à texte) et réglages du décodeur, les mêmes que dans PhoneSDRLink, plus WEFAX. Tout est appliqué tout de suite.
    public class DigiSettingsForm : Form
    {
        public readonly string Mode;
        readonly Settings st;
        readonly Action apply;           // réglages copiés vers le décodeur en marche
        readonly Action<string> command; // actions : "folder", "hellClear", "hellSave", "faxStart", "faxStop", "clearText", "restart"
        readonly FlowLayoutPanel box;
        readonly int innerW = 400;
        readonly Timer saveLater = new Timer { Interval = 400 };

        public DigiSettingsForm(Settings s, string mode, string title, Action apply, Action<string> command)
        {
            st = s; Mode = mode; this.apply = apply; this.command = command;
            Text = title;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            saveLater.Tick += delegate { saveLater.Stop(); st.Save(); };
            FormClosed += delegate { if (saveLater.Enabled) { saveLater.Stop(); st.Save(); } saveLater.Dispose(); };
            box = new FlowLayoutPanel();
            box.Dock = DockStyle.Fill;
            box.FlowDirection = FlowDirection.TopDown;
            box.WrapContents = false;
            box.AutoScroll = true;
            box.Padding = new Padding(8);
            Controls.Add(box);
            Build();
            ClientSize = new Size(innerW + 40, Math.Min(Screen.FromPoint(Cursor.Position).WorkingArea.Height - 80, Math.Max(200, Needed() + 20)));
            Theme.Apply(this);
            Theme.TitleBar(this);
        }

        int Needed()
        {
            int h = box.Padding.Vertical;
            foreach (Control c in box.Controls) h += c.Height + c.Margin.Vertical;
            return h;
        }

        public static bool TextMode(string m) { return m != "SSTV" && m != "Hell" && m != "WEFAX"; }

        void Rebuild()
        {
            box.SuspendLayout();
            box.Controls.Clear();
            Build();
            Theme.Apply(this);
            box.ResumeLayout();
        }

        void Build()
        {
            Label info = new Label();
            info.AutoSize = true;
            info.MaximumSize = new Size(innerW, 0);
            info.Text = Info(Mode);
            info.Margin = new Padding(0, 0, 0, 8);
            box.Controls.Add(info);
            if (TextMode(Mode))
            {
                Small(Lang.T("Surbrillance : indicatifs ou textes à repérer, séparés par des virgules", "Highlight: callsigns or texts to spot, separated by commas"));
                TextBox hi = new TextBox();
                hi.Width = innerW;
                hi.Text = st.DigiHi;
                hi.TextChanged += delegate { st.DigiHi = hi.Text.Trim(); st.Save(); apply(); };
                box.Controls.Add(hi);
            }
            switch (Mode)
            {
                case "CW": Cw(); break;
                case "RTTY": Rtty(); break;
                case "PSK": Psk(); break;
                case "Olivia": Oli(false); break;
                case "Contestia": Oli(true); break;
                case "Hell": Hell(); break;
                case "JS8": Js8(); break;
                case "SSTV": Sstv(); break;
                case "WEFAX": Fax(); break;
            }
            if (TextMode(Mode)) Button(Lang.T("Effacer le texte reçu", "Clear the received text"), delegate { command("clearText"); });
        }

        // ---- éléments

        Label Small(string t)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.MaximumSize = new Size(innerW, 0);
            l.Text = t;
            l.Margin = new Padding(0, 8, 0, 0);
            box.Controls.Add(l);
            return l;
        }

        CheckBox CheckRow(string t, string key, bool def, Action changed)
        {
            CheckBox c = new CheckBox();
            c.AutoSize = true;
            c.MaximumSize = new Size(innerW, 0);
            c.Text = t;
            c.Checked = st.DigiGet(key, def);
            c.Margin = new Padding(0, 6, 0, 0);
            c.CheckedChanged += delegate { st.DigiSet(key, c.Checked); st.Save(); if (changed != null) changed(); apply(); };
            box.Controls.Add(c);
            return c;
        }

        TrackBar Slider(Func<int, string> fmt, string key, int min, int max, int def, Action<int> changed)
        {
            Label l = Small("");
            TrackBar t = new TrackBar();
            t.Minimum = min; t.Maximum = max;
            t.Value = Math.Max(min, Math.Min(max, st.DigiGet(key, def)));
            t.Width = innerW;
            t.TickStyle = TickStyle.None;
            t.AutoSize = false;
            t.Height = 26;
            int span = max - min;
            t.SmallChange = 1;
            t.LargeChange = Math.Max(1, span / 20);
            l.Text = fmt(t.Value);
            t.ValueChanged += delegate
            {
                l.Text = fmt(t.Value);
                st.DigiSet(key, t.Value);
                if (changed != null) changed(t.Value);
                apply();
                saveLater.Stop(); saveLater.Start();   // enregistré peu après (souris, molette ou clavier)
            };
            box.Controls.Add(t);
            return t;
        }

        ComboBox Choice(string label, string[] items, int selected, Action<int> picked)
        {
            Small(label);
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = innerW;
            c.Items.AddRange(items);
            c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected));
            c.SelectedIndexChanged += delegate { picked(c.SelectedIndex); st.Save(); apply(); };
            box.Controls.Add(c);
            return c;
        }

        Button Button(string t, EventHandler click)
        {
            Button b = new Button();
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Text = t;
            b.Margin = new Padding(0, 8, 0, 0);
            b.Click += click;
            box.Controls.Add(b);
            return b;
        }

        void Defaults(params string[] keys)
        {
            Button(Lang.T("Valeurs par défaut", "Default values"), delegate
            {
                foreach (string k in keys) st.Digi.Remove(k);
                st.Save();
                apply();
                Rebuild();
            });
        }

        static int IndexOf(int[] a, int v) { for (int i = 0; i < a.Length; i++) if (a[i] == v) return i; return 0; }
        static string F1(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

        // ---- réglages par mode (mêmes valeurs par défaut que PhoneSDRLink)

        void Cw()
        {
            Small(Lang.T("Réglages CW (appliqués tout de suite) :", "CW settings (applied immediately):"));
            CheckBox autoTone = CheckRow(Lang.T("Tonalité automatique", "Automatic tone"), "cwAutoTone", true, null);
            Slider(v => Lang.F("Tonalité : {0} Hz", "Tone: {0} Hz", v), "cwTone", 200, 1200, 700, v => { if (autoTone.Checked) autoTone.Checked = false; });
            CheckRow(Lang.T("Filtre adapté à la vitesse", "Filter follows the speed"), "cwAutoBw", true, null);
            Slider(v => Lang.F("Filtre : {0} Hz", "Filter: {0} Hz", v), "cwBw", 30, 400, 80, null);
            CheckBox autoSpeed = CheckRow(Lang.T("Vitesse automatique", "Automatic speed"), "cwAutoSpeed", true, null);
            Slider(v => Lang.F("Vitesse : {0} mots/min", "Speed: {0} WPM", v), "cwWpm", 5, 60, 18, v => { if (autoSpeed.Checked) autoSpeed.Checked = false; });
            Slider(v => Lang.F("Seuil de détection : {0} %", "Detection threshold: {0} %", v), "cwThr", 30, 80, 55, null);
            Slider(v => Lang.F("Squelch (signal / bruit) : ×{0}", "Squelch (signal / noise): ×{0}", v), "cwSq", 3, 30, 5, null);
            Slider(v => Lang.F("Fin de lettre : {0} points", "Letter gap: {0} dots", F1(v / 10.0)), "cwChar", 15, 40, 20, null);
            Slider(v => Lang.F("Fin de mot : {0} points", "Word gap: {0} dots", F1(v / 10.0)), "cwWord", 40, 90, 50, null);
            CheckRow(Lang.T("Suivi de dérive (AFC)", "Drift tracking (AFC)"), "cwAfc", true, null);
            Slider(v => Lang.F("AGC (évanouissements) : {0}", "AGC (fading): {0}", v == 0 ? Lang.T("lent", "slow") : v == 1 ? Lang.T("moyen", "medium") : Lang.T("rapide", "fast")), "cwAgc", 0, 2, 1, null);
            Defaults("cwAutoTone", "cwTone", "cwAutoBw", "cwBw", "cwAutoSpeed", "cwWpm", "cwThr", "cwSq", "cwChar", "cwWord", "cwAfc", "cwAgc");
        }

        public static readonly int[] RTTY_BAUDS = { 0, 4545, 5000, 5688, 7500, 10000 };
        public static readonly int[] RTTY_SHIFTS = { 0, 85, 170, 200, 425, 450, 850 };

        void Rtty()
        {
            Small(Lang.T("Réglages RTTY (appliqués tout de suite) :", "RTTY settings (applied immediately):"));
            string auto = Lang.T("Automatique", "Automatic");
            Choice(Lang.T("Vitesse (débit en bauds)", "Speed (baud rate)"), new string[] { auto, "45,45", "50", "56,88", "75", "100" }, IndexOf(RTTY_BAUDS, st.DigiGet("rttyBaud", 0)),
                i => st.DigiSet("rttyBaud", RTTY_BAUDS[i]));
            Choice(Lang.T("Décalage entre les deux tonalités", "Shift between the two tones"), new string[] { auto, "85 Hz", "170 Hz", "200 Hz", "425 Hz", "450 Hz", "850 Hz" }, IndexOf(RTTY_SHIFTS, st.DigiGet("rttyShift", 0)),
                i => st.DigiSet("rttyShift", RTTY_SHIFTS[i]));
            Choice(Lang.T("Polarité", "Polarity"), new string[] { auto, Lang.T("Normale (mark = tonalité basse)", "Normal (mark = low tone)"), Lang.T("Inversée", "Reversed") }, st.DigiGet("rttyPol", 0),
                i => st.DigiSet("rttyPol", i));
            CheckBox autoFreq = new CheckBox();
            autoFreq.AutoSize = true;
            autoFreq.Text = Lang.T("Fréquence automatique", "Automatic frequency");
            autoFreq.Checked = st.DigiGet("rttyFreq", 0) == 0;
            autoFreq.Margin = new Padding(0, 6, 0, 0);
            box.Controls.Add(autoFreq);
            bool busy = false;
            TrackBar f = Slider(v => Lang.F("Tonalité basse : {0} Hz", "Low tone: {0} Hz", v), "rttyFreqManual", 500, 2900, 2125, v => { if (!busy) { st.DigiSet("rttyFreq", v); busy = true; autoFreq.Checked = false; busy = false; } });
            autoFreq.CheckedChanged += delegate
            {
                if (busy) return;
                st.DigiSet("rttyFreq", autoFreq.Checked ? 0 : f.Value);
                st.Save(); apply();
            };
            CheckRow(Lang.T("Suivi de dérive (AFC)", "Drift tracking (AFC)"), "rttyAfc", true, null);
            CheckRow(Lang.T("Une espace ramène en lettres (UOS)", "A space returns to letters (UOS)"), "rttyUos", true, null);
            Slider(v => Lang.F("Squelch (signal / bruit) : ×{0}", "Squelch (signal / noise): ×{0}", v), "rttySq", 5, 40, 15, null);
            Defaults("rttyBaud", "rttyShift", "rttyPol", "rttyFreq", "rttyFreqManual", "rttyAfc", "rttyUos", "rttySq");
        }

        void Psk()
        {
            Small(Lang.T("Réglages PSK31 (appliqués tout de suite) :", "PSK31 settings (applied immediately):"));
            CheckBox auto = CheckRow(Lang.T("Fréquence automatique (signal le plus fort)", "Automatic frequency (strongest signal)"), "pskAuto", true, null);
            Slider(v => Lang.F("Fréquence audio : {0} Hz", "Audio frequency: {0} Hz", v), "pskFreq", 300, 2900, 1000, v => { if (auto.Checked) auto.Checked = false; });
            Choice(Lang.T("Vitesse", "Speed"), new string[] { "PSK31 (31,25 bauds)", "PSK63 (62,5 bauds)" }, st.DigiGet("pskBaud", 31) >= 47 ? 1 : 0,
                i => { st.DigiSet("pskBaud", i == 1 ? 63 : 31); command("rename"); });
            CheckRow(Lang.T("Suivi de dérive (AFC)", "Drift tracking (AFC)"), "pskAfc", true, null);
            Slider(v => Lang.F("Squelch (cohérence de phase) : {0} %", "Squelch (phase coherence): {0} %", v), "pskSq", 60, 98, 75, null);
            Defaults("pskAuto", "pskFreq", "pskBaud", "pskAfc", "pskSq");
        }

        public static readonly int[] OLI_TONES = { 2, 4, 8, 16, 32, 64, 128, 256 }, OLI_BWS = { 125, 250, 500, 1000, 2000 };

        void Oli(bool con)
        {
            string nm = con ? "Contestia" : "Olivia";
            string kt = con ? "conTones" : "oliTones", kb = con ? "conBw" : "oliBw";
            int dt = con ? 16 : 32, db = con ? 500 : 1000;
            Small(Lang.F("Réglages {0} (appliqués tout de suite) :", "{0} settings (applied immediately):", nm));
            Label rate = null;
            Action upd = delegate
            {
                int t = st.DigiGet(kt, dt), b = st.DigiGet(kb, db);
                if (rate != null) rate.Text = Lang.F("Débit : environ {0} caractères par seconde ({1} tons sur {2} Hz)", "Speed: about {0} characters per second ({1} tones over {2} Hz)",
                    F1(OliviaEngine.CharsPerSecond(t, b, con)), t, b);
            };
            Choice(Lang.T("Nombre de tons", "Number of tones"), new string[] { "2", "4", "8", "16", "32", "64", "128", "256" }, IndexOf(OLI_TONES, st.DigiGet(kt, dt)),
                i => { st.DigiSet(kt, OLI_TONES[i]); upd(); });
            Choice(Lang.T("Bande du signal", "Signal bandwidth"), new string[] { "125 Hz", "250 Hz", "500 Hz", "1000 Hz", "2000 Hz" }, IndexOf(OLI_BWS, st.DigiGet(kb, db)),
                i => { st.DigiSet(kb, OLI_BWS[i]); upd(); });
            rate = Small("");
            upd();
            CheckBox auto = CheckRow(Lang.T("Centre du signal automatique", "Automatic signal centre"), "oliAuto", true, null);
            Slider(v => Lang.F("Centre du signal : {0} Hz", "Signal centre: {0} Hz", v), "oliFreq", 300, 2900, 1500, v => { if (auto.Checked) auto.Checked = false; });
            CheckRow(Lang.T("Signal inversé (bande latérale opposée)", "Reversed signal (opposite sideband)"), "oliRev", false, null);
            Slider(v => Lang.F("Marge de recherche en fréquence : ± {0} tons", "Frequency search margin: ± {0} tones", v), "oliMargin", 2, 20, 8, null);
            Slider(v => Lang.F("Intégration de la synchro : {0}", "Synchronisation integration: {0}", v), "oliInteg", 2, 20, 4, null);
            Slider(v => Lang.F("Seuil de synchro (signal / bruit) : ×{0}", "Synchronisation threshold (signal / noise): ×{0}", F1(v / 10.0)), "oliThr", 30, 100, 30, null);
            Defaults(kt, kb, "oliAuto", "oliFreq", "oliRev", "oliMargin", "oliInteg", "oliThr");
        }

        void Hell()
        {
            Small(Lang.T("Réglages Hell (appliqués tout de suite) :", "Hell settings (applied immediately):"));
            Choice(Lang.T("Variante de l'émetteur", "Transmitter variant"), HellEngine.NAMES, st.DigiGet("hellVar", 0), i => st.DigiSet("hellVar", i));
            CheckBox auto = CheckRow(Lang.T("Fréquence automatique", "Automatic frequency"), "hellAuto", true, null);
            Slider(v => Lang.F("Fréquence audio : {0} Hz", "Audio frequency: {0} Hz", v), "hellFreq", 300, 2900, 1000, v => { if (auto.Checked) auto.Checked = false; });
            Slider(v => Lang.F("Squelch : {0}", "Squelch: {0}", v == 0 ? Lang.T("aucun", "none") : v.ToString(CultureInfo.InvariantCulture)), "hellSq", 0, 10, 5, null);
            CheckRow(Lang.T("Inverser les tonalités (FM Hell 105 / 245, Hell 80)", "Swap the tones (FM Hell 105 / 245, Hell 80)"), "hellInv", false, null);
            Button(Lang.T("Effacer l'écran", "Clear the screen"), delegate { command("hellClear"); });
            Button(Lang.T("Enregistrer l'image", "Save the picture"), delegate { command("hellSave"); });
            Button(Lang.T("Ouvrir le dossier des images", "Open the image folder"), delegate { command("folder"); });
        }

        void Js8()
        {
            string[] names = { Lang.T("Lent (30 s)", "Slow (30 s)"), Lang.T("Normal (15 s)", "Normal (15 s)"), Lang.T("Rapide (10 s)", "Fast (10 s)"), "Turbo (6 s)", "Ultra (4 s)" };
            Choice(Lang.T("Vitesse", "Speed"), names, st.DigiGet("js8Speed", 1), i => { st.DigiSet("js8Speed", i); command("restart"); });
        }

        void Sstv()
        {
            string[] nums = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
            Choice(Lang.T("Sensibilité", "Sensitivity"), nums, st.DigiGet("sstvSens", 5) - 1, i => st.DigiSet("sstvSens", i + 1));
            Choice(Lang.T("Perte data", "Data loss"), nums, st.DigiGet("sstvLoss", 4) - 1, i => st.DigiSet("sstvLoss", i + 1));
            CheckRow(Lang.T("Enregistrer automatiquement les images (Images › SDRLink)", "Save the pictures automatically (Pictures › SDRLink)"), "sstvSave", true, null);
            Button(Lang.T("Ouvrir le dossier des images", "Open the image folder"), delegate { command("folder"); });
        }

        void Fax()
        {
            Small(Lang.T("Réglages WEFAX (appliqués tout de suite) :", "WEFAX settings (applied immediately):"));
            int[] lpms = FaxEngine.LPMS;
            Choice(Lang.T("Lignes par minute", "Lines per minute"), new string[] { "60", "90", "120", "240" }, IndexOf(lpms, st.DigiGet("faxLpm", 120)), i => st.DigiSet("faxLpm", lpms[i]));
            Choice(Lang.T("Indice de coopération (IOC) quand l'image commence sans ton de début", "Index of cooperation (IOC) when the picture starts without a start tone"),
                new string[] { "IOC 576", "IOC 288" }, st.DigiGet("faxIoc", 576) == 288 ? 1 : 0, i => st.DigiSet("faxIoc", i == 1 ? 288 : 576));
            CheckRow(Lang.T("Attendre le ton de début (sinon l'image commence tout de suite)", "Wait for the start tone (otherwise the picture starts at once)"), "faxAutoStart", true, null);
            CheckRow(Lang.T("Terminer l'image au ton de fin", "End the picture at the stop tone"), "faxAutoStop", true, null);
            CheckRow(Lang.T("Caler le début des lignes sur les lignes de calage", "Align the line start on the phasing lines"), "faxAutoPhase", true, null);
            Slider(v => Lang.F("Fréquence du milieu : {0} Hz (noir {1} Hz, blanc {2} Hz)", "Centre frequency: {0} Hz (black {1} Hz, white {2} Hz)", v, v - 400, v + 400), "faxCenter", 1500, 2300, 1900, null);
            Slider(v => Lang.F("Correction de pente : {0} ppm", "Slant correction: {0} ppm", v > 0 ? "+" + v : v.ToString(CultureInfo.InvariantCulture)), "faxSlant", -2000, 2000, 0, null);
            CheckRow(Lang.T("Enregistrer automatiquement les images (Images › SDRLink)", "Save the pictures automatically (Pictures › SDRLink)"), "faxSave", true, null);
            Button(Lang.T("Commencer une image maintenant", "Start a picture now"), delegate { command("faxStart"); });
            Button(Lang.T("Terminer l'image et l'enregistrer", "End the picture and save it"), delegate { command("faxStop"); });
            Button(Lang.T("Ouvrir le dossier des images", "Open the image folder"), delegate { command("folder"); });
            Defaults("faxLpm", "faxIoc", "faxAutoStart", "faxAutoStop", "faxAutoPhase", "faxCenter", "faxSlant", "faxSave");
        }

        // ---- explication de chaque mode (reprise de PhoneSDRLink, adaptée au PC)

        public static string Info(string mode)
        {
            string head, cols;
            switch (mode)
            {
                case "FT8": case "FT4":
                    head = Lang.T("Mode USB ou DATA, sur une fréquence FT8 (7.074, 14.074 MHz…) ou FT4 (7.0475, 14.080 MHz…). L'heure du PC doit être exacte.", "USB or DATA mode, on an FT8 (7.074, 14.074 MHz…) or FT4 (7.0475, 14.080 MHz…) frequency. The PC clock must be accurate.");
                    cols = Lang.T("Heure UTC · DT (s) · fréquence audio (Hz) · message. Les deux périodes (paire et impaire) sont affichées en deux couleurs.", "UTC time · DT (s) · audio frequency (Hz) · message. The two periods (even and odd) are shown in two colours.");
                    break;
                case "FT2":
                    head = Lang.T("Mode USB ou DATA, sur une fréquence FT2 (3.578, 7.052, 14.084 MHz…). FT2 est un FT4 deux fois plus rapide : tranches de 3,75 s, messages de 2,5 s, bande de 170 Hz. L'heure du PC doit être exacte. Réception seule ; les signaux faibles se décodent moins bien qu'en FT4 (symboles plus courts).", "USB or DATA mode, on an FT2 frequency (3.578, 7.052, 14.084 MHz…). FT2 is FT4 at double speed: 3.75 s slots, 2.5 s messages, 170 Hz wide. The PC clock must be accurate. Reception only; weak signals decode less well than in FT4 (shorter symbols).");
                    cols = Lang.T("Heure UTC · DT (s) · fréquence audio (Hz) · message. Les deux périodes (paire et impaire) sont affichées en deux couleurs.", "UTC time · DT (s) · audio frequency (Hz) · message. The two periods (even and odd) are shown in two colours.");
                    break;
                case "WSPR":
                    head = Lang.T("Mode USB, sur une fréquence WSPR (14.0956, 7.0386, 10.1387 MHz… : fréquence de la porteuse + 1500 Hz). Tranches de 2 minutes ; l'heure du PC doit être exacte ; premiers résultats à la fin de la première tranche.", "USB mode, on a WSPR frequency (14.0956, 7.0386, 10.1387 MHz…: carrier frequency + 1500 Hz). 2-minute slots; the PC clock must be accurate; first results at the end of the first slot.");
                    cols = Lang.T("Heure UTC · SNR (dB) · DT (s) · fréquence audio (Hz) · indicatif, locator, puissance (dBm).", "UTC time · SNR (dB) · DT (s) · audio frequency (Hz) · callsign, locator, power (dBm).");
                    break;
                case "JT9":
                    head = Lang.T("Mode USB, sur une fréquence JT9 (14.078, 7.078, 3.578 MHz…). Tranches d'une minute ; l'heure du PC doit être exacte.", "USB mode, on a JT9 frequency (14.078, 7.078, 3.578 MHz…). 1-minute slots; the PC clock must be accurate.");
                    cols = Lang.T("Heure UTC · SNR (dB) · DT (s) · fréquence audio (Hz) · message.", "UTC time · SNR (dB) · DT (s) · audio frequency (Hz) · message.");
                    break;
                case "JS8":
                    head = Lang.T("Mode USB, sur une fréquence JS8Call (14.078, 7.078, 3.578 MHz…). Réception seule : trames balise (HEARTBEAT), messages dirigés, texte libre. Choisissez la vitesse de l'émetteur (liste « Vitesse » ci-dessous) : Lent (tranches de 30 s), Normal (15 s), Rapide (10 s), Turbo (6 s), Ultra (4 s) ; elle doit être la même que celle de la station écoutée. L'heure du PC doit être exacte.", "USB mode, on a JS8Call frequency (14.078, 7.078, 3.578 MHz…). Receive only: heartbeat frames, directed messages, free text. Choose the transmitter's speed (the Speed list below): Slow (30 s slots), Normal (15 s), Fast (10 s), Turbo (6 s), Ultra (4 s); it must be the same as the station you listen to. The PC clock must be accurate.");
                    cols = Lang.T("Heure UTC · SNR (dB) · DT (s) · fréquence audio (Hz) · trame (indicatif émetteur : destinataire ou commande, texte). Les deux périodes (paire et impaire) sont affichées en deux couleurs.", "UTC time · SNR (dB) · DT (s) · audio frequency (Hz) · frame (sender callsign: addressee or command, text). The two periods (even and odd) are shown in two colours.");
                    break;
                case "CW":
                    head = Lang.T("Mode CW ou USB, sur un signal CW.", "CW or USB mode, on a CW signal.");
                    cols = Lang.T("Heure UTC · texte.", "UTC time · text.");
                    break;
                case "RTTY":
                    head = Lang.T("Mode USB ou LSB (au moins 500 Hz de bande), sur un signal RTTY (14.080 à 14.099 MHz…).", "USB or LSB mode (at least 500 Hz bandwidth), on an RTTY signal (14.080 to 14.099 MHz…).");
                    cols = Lang.T("Heure UTC · texte.", "UTC time · text.");
                    break;
                case "PSK":
                    head = Lang.T("Mode USB, sur un signal PSK31 (14.070, 7.035, 3.580 MHz…) ; la fréquence audio est trouvée automatiquement (ou réglée à la main). PSK63 possible.", "USB mode, on a PSK31 signal (14.070, 7.035, 3.580 MHz…); the audio frequency is found automatically (or set by hand). PSK63 possible.");
                    cols = Lang.T("Heure UTC · texte.", "UTC time · text.");
                    break;
                case "Olivia":
                    head = Lang.T("Mode USB, sur un signal Olivia (appel 8/250 : 14.0729, 7.0729, 3.5729 MHz… ; 16/500 et 32/1000 un peu plus haut, vers 14.075 et 14.106 MHz). Réglez le nombre de tons et la bande de l'émetteur : ils doivent être les mêmes que les siens ; formats courants : 8/250, 16/500, 32/1000. Le centre du signal est trouvé tout seul dans le spectre (ou imposé). Le texte apparaît par blocs, quelques secondes après la synchronisation ; le signal passe même bien en dessous du bruit. Réception seule.", "USB mode, on an Olivia signal (calling 8/250: 14.0729, 7.0729, 3.5729 MHz…; 16/500 and 32/1000 a little higher, around 14.075 and 14.106 MHz). Set the transmitter's number of tones and bandwidth: they must be the same as its own; common formats: 8/250, 16/500, 32/1000. The signal's centre is found automatically in the spectrum (or fixed by hand). The text appears in blocks, a few seconds after synchronisation; the signal can even be well below the noise. Receive only.");
                    cols = Lang.T("Heure UTC · texte. La légende de la fenêtre donne la synchro, le rapport signal/bruit et l'écart de fréquence.", "UTC time · text. The window caption shows the synchronisation, the signal-to-noise ratio and the frequency offset.");
                    break;
                case "Contestia":
                    head = Lang.T("Mode USB, sur un signal Contestia (variante d'Olivia à caractères de 6 bits : 3.5825, 7.0425, 14.0725 MHz…). Réglez le nombre de tons et la bande de l'émetteur : formats courants 8/250, 16/500, 32/1000. Le centre du signal est trouvé tout seul dans le spectre (ou imposé). Réception seule.", "USB mode, on a Contestia signal (an Olivia variant with 6-bit characters: 3.5825, 7.0425, 14.0725 MHz…). Set the transmitter's number of tones and bandwidth: common formats 8/250, 16/500, 32/1000. The signal's centre is found automatically in the spectrum (or fixed by hand). Receive only.");
                    cols = Lang.T("Heure UTC · texte.", "UTC time · text.");
                    break;
                case "Hell":
                    head = Lang.T("Mode USB, sur un signal Hellschreiber (Feld Hell : 14.063, 7.046, 3.592 MHz… ; FM Hell 105 : 3.585, 7.0695, 10.1425 MHz…). Choisissez la variante de l'émetteur : Feld Hell, Slow Hell, Hell X5, Hell X9, FM Hell 105, FM Hell 245 ou Hell 80. Le texte n'est pas décodé en lettres : il s'écrit comme une image que l'on lit à l'œil, comme sur la machine d'origine (lignes de 14 points qui défilent). La fréquence est trouvée toute seule dans le spectre ; les rangs de points se calent d'eux-mêmes. Réception seule.", "USB mode, on a Hellschreiber signal (Feld Hell: 14.063, 7.046, 3.592 MHz…; FM Hell 105: 3.585, 7.0695, 10.1425 MHz…). Choose the transmitter's variant: Feld Hell, Slow Hell, Hell X5, Hell X9, FM Hell 105, FM Hell 245 or Hell 80. The text is not decoded into letters: it is written as a picture that you read by eye, like on the original machine (14-dot lines scrolling up). The frequency is found automatically in the spectrum; the dot rows line up by themselves. Receive only.");
                    cols = Lang.T("Image : les lettres s'écrivent de gauche à droite et la ligne passe à la suivante en bout de ligne. Si le texte semble penché, la vitesse d'horloge des deux stations diffère un peu : le suivi se rattrape tout seul.", "Picture: the letters are written from left to right and the line wraps at its end. If the text looks slanted, the two stations' clocks differ slightly: tracking catches up by itself.");
                    break;
                case "WEFAX":
                    head = Lang.T("Mode USB, 1,9 kHz sous la fréquence publiée de la station de fac-similé météo (DWD Pinneberg 3.855, 7.880, 13.8825 MHz ; Northwood 2.618, 4.610, 8.040 MHz…), ou sur un signal FAX radioamateur. Noir 1500 Hz, blanc 2300 Hz. L'image commence au ton de début (son IOC, 576 ou 288, est reconnu) et se termine au ton de fin ; elle est enregistrée à la fin. La plupart des stations émettent à 120 lignes par minute, IOC 576.", "USB mode, 1.9 kHz below the published frequency of the weather fax station (DWD Pinneberg 3.855, 7.880, 13.8825 MHz; Northwood 2.618, 4.610, 8.040 MHz…), or on an amateur FAX signal. Black 1500 Hz, white 2300 Hz. The picture starts at the start tone (its IOC, 576 or 288, is recognised) and ends at the stop tone; it is saved at the end. Most stations send 120 lines per minute, IOC 576.");
                    cols = Lang.T("Si l'image penche, la carte son ou le WebSDR n'a pas tout à fait la bonne cadence : corrigez avec « Correction de pente ». Si l'image est décalée vers la droite, elle a été prise en route (sans lignes de calage).", "If the picture slants, the sound card or the WebSDR is not running at quite the right rate: correct it with “Slant correction”. If the picture is shifted to the right, it was picked up midway (without phasing lines).");
                    break;
                default:   // SSTV
                    head = Lang.T("Mode USB (au moins 2,4 kHz de bande), sur une fréquence SSTV (14.230, 7.033, 3.730 MHz…) ; le mode de l'image est reconnu tout seul, l'image se construit ligne par ligne, puis elle est enregistrée à la fin.", "USB mode (at least 2.4 kHz bandwidth), on an SSTV frequency (14.230, 7.033, 3.730 MHz…); the picture mode is recognised automatically, the picture builds up line by line and is saved when it ends.");
                    cols = Lang.T("Sensibilité (1 à 10) : plus le chiffre est haut, plus le décodeur accepte les signaux faibles (code de début, synchros, lignes), au risque de quelques fausses détections.", "Sensitivity (1 to 10): the higher the number, the weaker the signals the decoder accepts (start code, sync pulses, lines), at the risk of a few false detections.")
                        + "\n\n" + Lang.T("Perte data (1 à 10) : combien de temps le décodeur continue une image quand le signal disparaît (3 s au chiffre 1, 12 s au 4, 120 s au 10). Si le signal revient dans ce temps, l'image reprend à la bonne ligne et les lignes perdues restent grises. Passé ce temps, l'image est enregistrée telle quelle et le décodeur se remet à l'écoute (il retrouve alors le mode tout seul).", "Data loss (1 to 10): how long the decoder keeps going with a picture when the signal disappears (3 s at 1, 12 s at 4, 120 s at 10). If the signal comes back in that time, the picture resumes at the right line and the lost lines stay grey. After that time the picture is saved as it is and the decoder listens again (it then finds the mode by itself).");
                    break;
            }
            return Lang.T("La réception choisie comme pour FreeDV (poste, sinon WebSDR, sinon ExpertSDR, sinon HDSDR) est analysée en continu (une réception coupée n'est pas choisie ; si toutes sont coupées, le décodage continue sur le poste ou le WebSDR).", "The reception chosen as for FreeDV (rig, otherwise WebSDR, otherwise ExpertSDR, otherwise HDSDR) is analysed continuously (a muted reception is not chosen; if all are muted, decoding goes on with the rig or the WebSDR).")
                + "\n\n" + head + (cols != null ? "\n\n" + cols : "");
        }
    }
}
