using System;
using System.Drawing;
using System.Windows.Forms;

namespace SDRLink
{
    // ---------------------------------------------------------------- Bouton Tune, choix du VFO, port COM virtuel (1.49)
    public partial class MainForm
    {
        // ---- Tune : émission d'une tonalité de 1500 Hz sur la carte son d'émission du poste, PTT par CAT, pendant 5 s à 1 min.

        Button btnTune;
        TuneTone tuneTone;
        bool tunePttOwned;            // PTT mis par Tune (rendu à la fin) ; sinon il était déjà en émission
        DateTime tuneUntil;
        Timer tuneTimer;
        const double TuneHz = 1500;
        static readonly int[] TuneDurations = { 5, 10, 15, 20, 30, 45, 60 };

        // Même forme que le bouton PTT ; placé au bout de la ligne, après les cases « Avant plan » / « Affichage ptt ».
        void BuildTuneButton(FlowLayoutPanel row)
        {
            btnTune = new Button();
            btnTune.Size = btnPtt.Size;
            btnTune.Margin = btnPtt.Margin;
            btnTune.Font = btnPtt.Font;
            btnTune.FlatStyle = FlatStyle.Flat;
            btnTune.FlatAppearance.BorderSize = 2;
            btnTune.Tag = Theme.Custom;
            btnTune.AccessibleName = "Tune";
            btnTune.Click += delegate { if (tuneTone != null) StopTune(Lang.T("Tune arrêté", "Tune stopped")); else StartTune(); };
            ContextMenuStrip m = new ContextMenuStrip();
            m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e) { e.Cancel = false; FillTuneMenu(m); };
            // Fenêtre du niveau ouverte depuis ce menu : le menu reste affiché à côté (sauf Échap), on y revient avec OK.
            m.Closing += delegate(object s, ToolStripDropDownClosingEventArgs e)
            {
                if (keepTuneMenu && e.CloseReason != ToolStripDropDownCloseReason.Keyboard) e.Cancel = true;
            };
            tuneMenu = m;
            m.Items.Add("-");
            btnTune.ContextMenuStrip = m;
            tuneTimer = new Timer();
            tuneTimer.Interval = 200;
            tuneTimer.Tick += delegate { TuneTick(); };
            Tr(delegate
            {
                UpdateTuneButton();
                tips.SetToolTip(btnTune, Lang.T("Tune : émission d'une tonalité de 1500 Hz pour accorder la boîte d'accord ou l'amplificateur (PTT par CAT, son envoyé sur la carte son d'émission du poste). Clic : marche / arrêt. Clic droit : carte son d'émission du poste, niveau de la tonalité, durée (5 s à 1 min), couleurs",
                    "Tune: transmits a 1500 Hz tone to adjust the antenna tuner or the amplifier (PTT by CAT, sound sent to the rig's transmit sound card). Click: on / off. Right-click: rig transmit sound card, tone level, duration (5 s to 1 min), colours"));
            });
            row.Controls.Add(btnTune);
        }

        void FillTuneMenu(ContextMenuStrip m)
        {
            m.Items.Clear();
            ToolStripMenuItem title = new ToolStripMenuItem(Lang.T("Carte son d'émission du poste (sortie du PC reliée au poste) :", "Rig transmit sound card (PC output connected to the rig):"));
            title.Enabled = false;
            m.Items.Add(title);
            bool found = false;
            try
            {
                foreach (AudioEndpoints.Endpoint e in AudioEndpoints.All())
                {
                    if (e.Capture) continue;
                    string id = e.Id, name = e.FullName ?? e.Name ?? id;
                    ToolStripMenuItem it = new ToolStripMenuItem(name);
                    it.Checked = id == settings.TuneCardId;
                    found |= it.Checked;
                    it.Click += delegate
                    {
                        settings.TuneCardId = id;
                        settings.TuneCardName = name;
                        settings.Save();
                        Log.Write("Tune : carte son d'émission « " + name + " »");
                        ShowStatus(Lang.F("Tune : tonalité envoyée sur « {0} »", "Tune: tone sent to \"{0}\"", name));
                    };
                    m.Items.Add(it);
                }
            }
            catch (Exception ex) { Log.Write("Tune : lecture des cartes son impossible : " + ex.Message); }
            if (!found && settings.TuneCardId.Length > 0)
            {
                ToolStripMenuItem gone = new ToolStripMenuItem(Lang.F("{0} (non branchée)", "{0} (not connected)", settings.TuneCardName));
                gone.Checked = true;
                gone.Enabled = false;
                m.Items.Add(gone);
            }
            m.Items.Add(new ToolStripSeparator());
            // Niveau de 1 à 100 % : petite fenêtre toujours devant (un curseur dans un sous-menu peut passer sous une autre fenêtre).
            ToolStripMenuItem lvl = new ToolStripMenuItem(Lang.F("Niveau de la tonalité (1500 Hz) : {0} %…", "Tone level (1500 Hz): {0} %…", settings.TuneLevel));
            lvl.MouseDown += delegate { keepTuneMenu = true; };   // avant la fermeture du menu par le clic
            lvl.Click += delegate { keepTuneMenu = true; ShowTuneLevel(lvl); };
            m.Items.Add(lvl);
            ToolStripMenuItem dur = new ToolStripMenuItem(Lang.F("Durée de l'émission : {0} s", "Transmit time: {0} s", settings.TuneSeconds));
            foreach (int v in TuneDurations)
            {
                int sec = v;
                ToolStripMenuItem it = new ToolStripMenuItem(sec + " s");
                it.Checked = settings.TuneSeconds == sec;
                it.Click += delegate
                {
                    settings.TuneSeconds = sec;
                    settings.Save();
                    if (tuneTone != null) tuneUntil = DateTime.UtcNow.AddSeconds(sec) - (DateTime.UtcNow - tuneStart);
                    Log.Write("Tune : durée " + sec + " s");
                };
                dur.DropDownItems.Add(it);
            }
            m.Items.Add(dur);
            // Passage en émission pendant Tune : PTT par CAT (SDRLink met le poste en émission), ou VOX du poste (tonalité seule).
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem keyTitle = new ToolStripMenuItem(Lang.T("Passage en émission pendant Tune :", "Keying during Tune:"));
            keyTitle.Enabled = false;
            m.Items.Add(keyTitle);
            ToolStripMenuItem byPtt = new ToolStripMenuItem(Lang.T("PTT actif (émission par CAT)", "PTT on (transmit by CAT)"));
            ToolStripMenuItem byVox = new ToolStripMenuItem(Lang.T("VOX du poste (tonalité seule, sans PTT)", "Rig VOX (tone only, no PTT)"));
            byPtt.Checked = !settings.TuneVox;
            byVox.Checked = settings.TuneVox;
            byPtt.Click += delegate { SetTuneVox(false); };
            byVox.Click += delegate { SetTuneVox(true); };
            m.Items.Add(byPtt);
            m.Items.Add(byVox);
        }

        // Fenêtre du niveau de Tune : curseur de 1 à 100 % (pas de 1 : molette, flèches, clic) et case pour taper la valeur ;
        // pris tout de suite, même pendant l'émission. Toujours devant les autres fenêtres, posée sous le bouton Tune.
        Form tuneLevelWin;

        ContextMenuStrip tuneMenu;
        bool keepTuneMenu, tuneNoReopen;

        // Un menu ouvert capte tout le clavier de l'application (filtre interne de WinForms, chiffres pris comme raccourcis) :
        // le menu de Tune, laissé affiché, est retiré de ce filtre ; il est rouvert normalement à la fermeture de la fenêtre.
        void ReleaseTuneMenu()
        {
            if (tuneMenu == null || !tuneMenu.Visible) return;
            try
            {
                Type t = typeof(ToolStripManager).GetNestedType("ModalMenuFilter", System.Reflection.BindingFlags.NonPublic);
                System.Reflection.MethodInfo mi = t == null ? null : t.GetMethod("RemoveActiveToolStrip",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (mi != null) mi.Invoke(null, new object[] { tuneMenu });
                System.Reflection.MethodInfo exit = t == null ? null : t.GetMethod("ExitMenuMode",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (exit != null) exit.Invoke(null, null);
            }
            catch (Exception ex) { Log.Write("Tune : clavier du menu : " + ex.Message); }
        }

        // Remet le menu de Tune, resté affiché, dans le filtre clavier de WinForms (mode menu normal) ; false si impossible.
        bool RetakeTuneMenu(ContextMenuStrip tm)
        {
            try
            {
                Type t = typeof(ToolStripManager).GetNestedType("ModalMenuFilter", System.Reflection.BindingFlags.NonPublic);
                System.Reflection.MethodInfo mi = t == null ? null : t.GetMethod("SetActiveToolStrip",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                    null, new Type[] { typeof(ToolStrip) }, null);
                if (mi == null) return false;
                mi.Invoke(null, new object[] { tm });
                tm.Focus();
                return true;
            }
            catch (Exception ex) { Log.Write("Tune : retour au menu : " + ex.Message); return false; }
        }

        void ShowTuneLevel(ToolStripMenuItem lvl)
        {
            if (tuneLevelWin != null && !tuneLevelWin.IsDisposed) { tuneLevelWin.Activate(); return; }
            Form f = tuneLevelWin = new Form();
            f.Text = Lang.T("Niveau de la tonalité Tune (1500 Hz)", "Tune tone level (1500 Hz)");
            f.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            f.ShowInTaskbar = false;
            f.TopMost = true;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.KeyPreview = true;
            f.Font = Font;
            f.StartPosition = FormStartPosition.Manual;
            TrackBar trk = new TrackBar();
            trk.Minimum = 1;
            trk.Maximum = 100;
            trk.SmallChange = trk.LargeChange = 1;
            trk.TickFrequency = 10;
            trk.Width = 300;
            trk.Value = Math.Max(1, Math.Min(100, settings.TuneLevel));
            VolumeWheel(trk);
            NumericUpDown num = new NumericUpDown();
            num.Minimum = 1;
            num.Maximum = 100;
            num.Width = 55;
            num.Margin = new Padding(3, 8, 0, 3);
            num.Value = trk.Value;
            Label pctLbl = new Label();
            pctLbl.Text = "%";
            pctLbl.AutoSize = true;
            pctLbl.Margin = new Padding(2, 11, 6, 0);
            Button ok = new Button();
            ok.Text = "OK";
            ok.AutoSize = true;
            ok.Margin = new Padding(6, 6, 3, 3);
            ok.Click += delegate { f.Close(); };
            FlowLayoutPanel box = new FlowLayoutPanel();
            box.AutoSize = true;
            box.WrapContents = false;
            box.Padding = new Padding(6);
            box.Controls.Add(trk);
            box.Controls.Add(num);
            box.Controls.Add(pctLbl);
            box.Controls.Add(ok);
            f.Controls.Add(box);
            bool syncing = false;
            Action<int> apply = delegate(int pct)
            {
                if (syncing) return;
                syncing = true;
                if (trk.Value != pct) trk.Value = pct;
                if ((int)num.Value != pct) num.Value = pct;
                syncing = false;
                if (settings.TuneLevel == pct) return;
                settings.TuneLevel = pct;
                settings.Save();
                TuneTone t = tuneTone;
                if (t != null) t.Level = pct / 100f;   // pris tout de suite pendant l'émission
                if (lvl != null) lvl.Text = Lang.F("Niveau de la tonalité (1500 Hz) : {0} %…", "Tone level (1500 Hz): {0} %…", pct);
                Log.Write("Tune : niveau " + pct + " %");
            };
            trk.ValueChanged += delegate { apply(trk.Value); };
            num.ValueChanged += delegate { apply((int)num.Value); };
            f.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) { e.Handled = true; f.Close(); } };
            // Menu de Tune resté affiché : il ne capte plus le clavier (frappe dans la case) tant que cette fenêtre est ouverte.
            bool fromMenu = tuneMenu != null && tuneMenu.Visible;
            Point menuLoc = fromMenu ? tuneMenu.Location : Point.Empty;
            ReleaseTuneMenu();
            f.FormClosed += delegate
            {
                tuneLevelWin = null;
                keepTuneMenu = false;
                // Retour aux réglages de Tune : le menu est rouvert au même endroit, comme un menu normal (niveau à jour).
                // Le passage du focus de cette fenêtre à SDRLink ne doit pas le refermer aussitôt : protégé une demi-seconde.
                ContextMenuStrip tm = tuneMenu;
                if (tuneNoReopen) { tuneNoReopen = false; return; }   // « Fermer » du menu : tout se ferme
                if (tm == null || !fromMenu || IsDisposed || !btnTune.IsHandleCreated) return;
                BeginInvoke((Action)delegate
                {
                    Activate();
                    keepTuneMenu = true;
                    // Menu toujours affiché : il reprend simplement le clavier (sans le refermer ni le rouvrir, pas de clignement) ;
                    // sinon il est rouvert au même endroit.
                    if (!tm.Visible || !RetakeTuneMenu(tm))
                    {
                        if (tm.Visible) tm.Close();
                        tm.Show(btnTune, btnTune.PointToClient(menuLoc));
                    }
                    Timer once = new Timer();
                    once.Interval = 500;
                    once.Tick += delegate { once.Stop(); once.Dispose(); if (tuneLevelWin == null) keepTuneMenu = false; };
                    once.Start();
                });
            };
            f.Shown += delegate
            {
                // À gauche du menu de Tune, à la hauteur de la ligne « Niveau » (à droite s'il n'y a pas la place) ; sinon sous le bouton.
                Rectangle wa = Screen.FromControl(btnTune).WorkingArea;
                Point p = btnTune.PointToScreen(new Point(btnTune.Width - f.Width, btnTune.Height));
                if (tuneMenu != null && tuneMenu.Visible && lvl != null)
                {
                    Rectangle mb = tuneMenu.Bounds;
                    int y = mb.Top + lvl.Bounds.Top;
                    p = new Point(mb.Left - f.Width - 2, y);
                    if (p.X < wa.Left) p.X = mb.Right + 2;
                }
                f.Location = new Point(Math.Max(wa.Left, Math.Min(p.X, wa.Right - f.Width)), Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - f.Height)));
                trk.Focus();
            };
            f.Show(this);
        }

        // Dernière ligne du menu de Tune : « Fermer » (ajoutée après les couleurs, à chaque ouverture).
        const string TuneCloseTag = "tune-close";

        void AddTuneCloseItem()
        {
            ContextMenuStrip m = btnTune.ContextMenuStrip;
            m.Opening += delegate
            {
                for (int i = m.Items.Count - 1; i >= 0; i--)
                    if (TuneCloseTag.Equals(m.Items[i].Tag)) m.Items.RemoveAt(i);
                m.Items.Add(new ToolStripSeparator { Tag = TuneCloseTag });
                ToolStripMenuItem close = new ToolStripMenuItem(Lang.T("Fermer", "Close"));
                close.Tag = TuneCloseTag;
                close.Click += delegate
                {
                    keepTuneMenu = false;
                    if (tuneLevelWin != null && !tuneLevelWin.IsDisposed) { tuneNoReopen = true; tuneLevelWin.Close(); }
                    m.Close();
                };
                m.Items.Add(close);
            };
        }

        void SetTuneVox(bool vox)
        {
            if (settings.TuneVox == vox) return;
            if (tuneTone != null) StopTune(null);   // pris au prochain Tune
            settings.TuneVox = vox;
            settings.Save();
            Log.Write("Tune : " + (vox ? "VOX du poste" : "PTT par CAT"));
            ShowStatus(vox ? Lang.T("Tune : tonalité seule, le VOX du poste passe en émission", "Tune: tone only, the rig's VOX keys the transmitter")
                           : Lang.T("Tune : PTT actif par CAT pendant la tonalité", "Tune: PTT on by CAT during the tone"));
        }

        DateTime tuneStart;

        void StartTune()
        {
            // VOX : la tonalité seule fait passer le poste en émission ; pas besoin de liaison CAT.
            if (!settings.TuneVox && (worker == null || !rigOnline)) { ShowStatus(Lang.T("Tune : connectez d'abord le poste", "Tune: connect the rig first")); return; }
            if (settings.TuneCardId.Length == 0)
            {
                ShowStatus(Lang.T("Tune : choisissez d'abord la carte son d'émission du poste (clic droit sur Tune)", "Tune: first choose the rig's transmit sound card (right-click Tune)"));
                return;
            }
            tunePttOwned = false;
            if (!settings.TuneVox)
            {
                tunePttOwned = !pttActive;
                if (!pttActive) SetPttButton(true);
                if (!pttActive) return;   // PTT refusé (message déjà affiché)
            }
            tuneTone = new TuneTone(settings.TuneCardId, TuneHz, settings.TuneLevel / 100f);
            tuneTone.Start();
            tuneStart = DateTime.UtcNow;
            tuneUntil = tuneStart.AddSeconds(settings.TuneSeconds);
            tuneTimer.Start();
            Log.Write("Tune : émission " + settings.TuneSeconds + " s, niveau " + settings.TuneLevel + " %, " + (settings.TuneVox ? "VOX du poste" : "PTT par CAT"));
            UpdateTuneButton();
        }

        // Fin de Tune : tonalité arrêtée (descente de 10 ms), puis retour en réception si c'est Tune qui avait mis le PTT.
        void StopTune(string why)
        {
            TuneTone t = tuneTone;
            if (t == null) return;
            tuneTone = null;
            tuneTimer.Stop();
            t.Stop();
            if (tunePttOwned && pttActive) SetPttButton(false);
            tunePttOwned = false;
            if (why != null) { ShowStatus(why); Log.Write("Tune : " + why); }
            UpdateTuneButton();
        }

        void TuneTick()
        {
            TuneTone t = tuneTone;
            if (t == null) { tuneTimer.Stop(); return; }
            if (t.Error != null) { StopTune(Lang.F("Tune : carte son d'émission indisponible ({0})", "Tune: transmit sound card unavailable ({0})", t.Error)); return; }
            if (DateTime.UtcNow >= tuneUntil) { StopTune(Lang.F("Tune terminé ({0} s)", "Tune finished ({0} s)", settings.TuneSeconds)); return; }
            UpdateTuneButton();
        }

        void UpdateTuneButton()
        {
            if (btnTune == null) return;
            bool on = tuneTone != null;
            string text = "TUNE";
            if (on) text = "● TUNE " + Math.Max(0, (int)Math.Ceiling((tuneUntil - DateTime.UtcNow).TotalSeconds));
            if (btnTune.Text != text) btnTune.Text = text;
            // Mêmes couleurs que le PTT : jaune clair sur fond sombre au repos, blanc sur rouge en émission.
            btnTune.BackColor = on ? Color.FromArgb(210, 0, 0) : Color.FromArgb(55, 55, 60);
            btnTune.ForeColor = on ? Color.White : Color.FromArgb(255, 235, 110);
            btnTune.FlatAppearance.BorderColor = Color.FromArgb(180, 0, 0);
            int custom = ColorOf(on ? "TuneOn" : "TuneOff");
            if (custom >= 0)
            {
                Color back = Opaque(custom);
                btnTune.BackColor = back;
                btnTune.ForeColor = IsLight(back) ? Color.Black : on ? Color.White : Color.FromArgb(255, 235, 110);
                btnTune.FlatAppearance.BorderColor = Outline(back);
            }
            UpdateShareButton();   // appelé aussi aux changements de thème, de couleurs et à la déconnexion
        }

        // ---- VFO commandé (A ou B) : clic droit sur la fréquence RADIO (avant ses couleurs), pour toutes les interfaces.

        void BuildVfoMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                e.Cancel = false;
                m.Items.Clear();
                ToolStripMenuItem title = new ToolStripMenuItem(Lang.T("VFO du poste commandé par SDRLink :", "Rig VFO controlled by SDRLink:"));
                title.Enabled = false;
                m.Items.Add(title);
                for (int i = 0; i < 2; i++)
                {
                    int v = i;
                    ToolStripMenuItem it = new ToolStripMenuItem(v == 0 ? "VFO A" : Lang.T("VFO B (postes à deux VFO)", "VFO B (rigs with two VFOs)"));
                    it.Checked = settings.RigVfo == v;
                    it.Click += delegate { SetRigVfo(v); };
                    m.Items.Add(it);
                }
            };
            m.Items.Add("-");
            lblRig.ContextMenuStrip = m;
        }

        // « RADIO », ou « RADIO B » quand le VFO B est commandé.
        string RigCaption() { return Lang.T("RADIO", "RIG") + (settings.RigVfo == 1 ? " B" : ""); }

        void SetRigVfo(int v)
        {
            {
                if (v == settings.RigVfo) return;
                settings.RigVfo = v;
                settings.Save();
                SizeFreqLabels();
                UpdateDisplays();
                Log.Write("VFO commandé : " + (v == 1 ? "B" : "A"));
                // Réglage pris à la connexion : on reconnecte pour l'appliquer tout de suite.
                if (worker != null)
                {
                    if (tuneTone != null) StopTune(null);
                    if (pttActive) SetPttButton(false);
                    Disconnect();
                    Connect();
                }
                ShowStatus(Lang.F("VFO {0} commandé", "VFO {0} controlled", v == 1 ? "B" : "A"));
            }
        }

        // Valeur IRig.Vfo pour la prochaine connexion : 1 = B ; 2 = A en remettant le poste sur A (il avait été mis sur B).
        int VfoForConnect()
        {
            int v = settings.RigVfo == 1 ? 1 : settings.RigVfoOnB ? 2 : 0;
            if (settings.RigVfoOnB != (settings.RigVfo == 1)) { settings.RigVfoOnB = settings.RigVfo == 1; settings.Save(); }
            return v;
        }

        static bool HasSubMode(RigModel m)
        {
            if (m == null || m.Protocol != RigProtocol.Yaesu) return false;
            switch (m.Name) { case "FT-2000": case "FTdx9000": case "FTdx5000": case "FTdx101D/MP": case "FTX-1": return true; }
            return false;
        }

        // ---- Port COM virtuel (com0com) : le poste piloté en CAT direct est partagé avec un autre logiciel.

        Button btnVcom;

        void BuildShareButton(FlowLayoutPanel p)
        {
            btnVcom = Btn("Port COM virtuel…", "Virtual COM port…", delegate { ShowShareDialog(); });
            Tr(delegate
            {
                UpdateShareButton();
                tips.SetToolTip(btnVcom, Lang.T("Partage du poste avec un autre logiciel (WSJT-X, logiciel de journal…) par un port COM virtuel (paire com0com) : SDRLink garde le vrai port du poste et passe les commandes de l'autre logiciel. Installation de com0com et création d'une paire depuis la fenêtre",
                    "Shares the rig with another program (WSJT-X, logging program…) through a virtual COM port (com0com pair): SDRLink keeps the rig's real port and passes on the other program's commands. com0com installation and pair creation from the window"));
            });
            p.Controls.Add(btnVcom);
        }

        void UpdateShareButton()
        {
            if (btnVcom == null) return;
            string other = settings.ShareCom.Length > 0 ? Com0Com.OtherEnd(settings.ShareCom) : null;
            btnVcom.Text = settings.ShareCom.Length == 0 ? Lang.T("Port COM virtuel…", "Virtual COM port…")
                : Lang.F("Port COM virtuel : {0}", "Virtual COM port: {0}", other ?? settings.ShareCom);
            // Port virtuel ouvert par SDRLink (partage en service) : vert, sinon couleur du thème (ou celles choisies au clic droit).
            bool active = worker != null && worker.ShareOpen;
            if (active) { int c = ColorOf("VcomOn"); PaintCustom(btnVcom, c >= 0 ? c : 0x00964B); }
            else PaintKeyed(btnVcom, "VcomOff");
        }

        void ShowShareDialog()
        {
            Form f = new Form();
            f.Text = Lang.T("Port COM virtuel (partage du poste)", "Virtual COM port (rig sharing)");
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.Font = Font;
            f.TopMost = TopMost;
            FlowLayoutPanel col = new FlowLayoutPanel();
            col.FlowDirection = FlowDirection.TopDown;
            col.AutoSize = true;
            col.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            col.Padding = new Padding(10);
            col.WrapContents = false;
            f.Controls.Add(col);

            Label help = new Label();
            help.AutoSize = true;
            help.MaximumSize = new Size(560, 0);
            help.Text = Lang.T("SDRLink garde le port COM du poste et ouvre l'un des deux ports d'une paire com0com ; votre autre logiciel (WSJT-X, logiciel de journal…) ouvre l'autre port, avec le même modèle de poste, comme s'il était branché sur le poste. Ses commandes sont passées au poste entre celles de SDRLink. Seulement avec l'interface « Port COM (CAT direct) ».",
                "SDRLink keeps the rig's COM port and opens one of the two ports of a com0com pair; your other program (WSJT-X, logging program…) opens the other port, with the same rig model, as if it were connected to the rig. Its commands are passed to the rig between SDRLink's own. Only with the \"COM port (direct CAT)\" interface.");
            col.Controls.Add(help);

            Label state = new Label();
            state.AutoSize = true;
            state.Margin = new Padding(3, 10, 3, 3);
            col.Controls.Add(state);
            Button install = new Button();
            install.AutoSize = true;
            install.Text = Lang.T("Installer com0com…", "Install com0com…");
            col.Controls.Add(install);

            CheckBox on = new CheckBox();
            on.AutoSize = true;
            on.Margin = new Padding(3, 10, 3, 3);
            on.Text = Lang.T("Partager le poste sur la paire de ports :", "Share the rig on the port pair:");
            on.Checked = settings.ShareCom.Length > 0;
            col.Controls.Add(on);
            ComboBox pairs = new ComboBox();
            pairs.DropDownStyle = ComboBoxStyle.DropDownList;
            pairs.Width = 440;
            col.Controls.Add(pairs);
            FlowLayoutPanel pairBtns = new FlowLayoutPanel();
            pairBtns.AutoSize = true;
            pairBtns.Margin = Padding.Empty;
            Button create = new Button();
            create.AutoSize = true;
            pairBtns.Controls.Add(create);
            Button remove = new Button();
            remove.AutoSize = true;
            remove.Text = Lang.T("Supprimer la paire choisie…", "Remove the chosen pair…");
            pairBtns.Controls.Add(remove);
            col.Controls.Add(pairBtns);
            Label hint = new Label();
            hint.AutoSize = true;
            hint.MaximumSize = new Size(560, 0);
            hint.Margin = new Padding(3, 8, 3, 3);
            col.Controls.Add(hint);

            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.AutoSize = true;
            btns.Margin = new Padding(0, 10, 0, 0);
            Button ok = new Button(), cancel = new Button();
            ok.Text = "OK";
            cancel.Text = Lang.T("Annuler", "Cancel");
            ok.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            btns.Controls.Add(ok);
            btns.Controls.Add(cancel);
            col.Controls.Add(btns);
            f.AcceptButton = ok;
            f.CancelButton = cancel;

            string rigPort = cbPort.Text.Trim();
            bool busy = false;
            string prefer = null;   // paire tout juste créée : choisie d'office
            Action refresh = null;
            refresh = delegate
            {
                bool inst = Com0Com.Installed;
                state.Text = inst ? Lang.T("com0com : installé", "com0com: installed")
                                  : Lang.T("com0com n'est pas installé sur ce PC (pilote de ports COM virtuels, gratuit, licence GPL).", "com0com is not installed on this PC (virtual COM port driver, free, GPL licence).");
                install.Visible = !inst;
                string keep = prefer ?? (pairs.SelectedItem is Com0Com.Pair ? ((Com0Com.Pair)pairs.SelectedItem).A : settings.ShareCom);
                prefer = null;
                pairs.Items.Clear();
                foreach (Com0Com.Pair p in Com0Com.Usable(rigPort, settings.ShareCom))
                {
                    pairs.Items.Add(p);
                    if (p.A.Equals(keep, StringComparison.OrdinalIgnoreCase)) pairs.SelectedItem = p;
                }
                if (pairs.SelectedIndex < 0 && pairs.Items.Count > 0) pairs.SelectedIndex = 0;
                int[] n = Com0Com.FreeNumbers();
                create.Text = Lang.F("Créer une nouvelle paire COM{0} ↔ COM{1}…", "Create a new pair COM{0} ↔ COM{1}…", n[0], n[1]);
                create.Tag = n;
                create.Enabled = install.Enabled = !busy;
                pairs.Enabled = !busy;
                Com0Com.Pair sel = pairs.SelectedItem as Com0Com.Pair;
                remove.Enabled = !busy && sel != null && Com0Com.SetupC() != null;
                hint.Text = sel == null
                    ? Lang.T("Aucune paire de ports COM : créez-en une.", "No COM port pair: create one.")
                    : Lang.F("Dans votre autre logiciel : port {0}, même modèle de poste que dans SDRLink (la vitesse n'a pas d'importance sur un port virtuel). Pris en compte à la prochaine connexion du poste.",
                             "In your other program: port {0}, same rig model as in SDRLink (the baud rate does not matter on a virtual port). Applied at the next rig connection.", sel.B);
            };
            on.CheckedChanged += delegate { refresh(); };
            pairs.SelectedIndexChanged += delegate
            {
                Com0Com.Pair sel = pairs.SelectedItem as Com0Com.Pair;
                if (sel != null) hint.Text = Lang.F("Dans votre autre logiciel : port {0}, même modèle de poste que dans SDRLink (la vitesse n'a pas d'importance sur un port virtuel). Pris en compte à la prochaine connexion du poste.",
                             "In your other program: port {0}, same rig model as in SDRLink (the baud rate does not matter on a virtual port). Applied at the next rig connection.", sel.B);
            };
            // Installation et création : Windows demande l'autorisation d'administrateur ; le travail se fait hors de la fenêtre.
            Action<Func<string>> work = delegate(Func<string> job)
            {
                busy = true;
                refresh();
                f.UseWaitCursor = true;
                System.Threading.Thread t = new System.Threading.Thread(delegate()
                {
                    string err = job();
                    try
                    {
                        f.BeginInvoke((Action)delegate
                        {
                            busy = false;
                            f.UseWaitCursor = false;
                            if (err != null) MessageBox.Show(f, err, "SDRLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            if (!f.IsDisposed) refresh();
                        });
                    }
                    catch { }
                });
                t.IsBackground = true;
                t.Start();
            };
            install.Click += delegate
            {
                work(delegate
                {
                    string err;
                    return Com0Com.Install(out err) ? null : Lang.T("com0com n'a pas pu être installé : ", "com0com could not be installed: ") + err;
                });
            };
            create.Click += delegate
            {
                if (!Com0Com.Installed || Com0Com.SetupC() == null)
                {
                    MessageBox.Show(f, Lang.T("Installez d'abord com0com.", "Install com0com first."), "SDRLink");
                    return;
                }
                int[] n = (int[])create.Tag;
                string a = "COM" + n[0], b = "COM" + n[1];
                work(delegate
                {
                    string err;
                    if (!Com0Com.CreatePair(a, b, out err)) return Lang.T("La paire n'a pas pu être créée : ", "The pair could not be created: ") + err;
                    BeginInvokeSafe(delegate { prefer = a; on.Checked = true; });
                    return null;
                });
            };
            // Suppression d'une paire (après confirmation) ; jamais celle que SDRLink tient ouverte en ce moment.
            remove.Click += delegate
            {
                Com0Com.Pair sel = pairs.SelectedItem as Com0Com.Pair;
                if (sel == null) return;
                if (worker != null && worker.ShareOpen && sel.A.Equals(settings.ShareCom, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(f, Lang.T("Cette paire est utilisée par SDRLink en ce moment : déconnectez d'abord le poste.", "This pair is in use by SDRLink right now: disconnect the rig first."), "SDRLink");
                    return;
                }
                if (MessageBox.Show(f, Lang.F("Supprimer la paire de ports {0} ↔ {1} ?\n\nUn logiciel qui utilise {1} ne pourra plus l'ouvrir. Windows demande l'autorisation d'administrateur.",
                        "Remove the port pair {0} ↔ {1}?\n\nA program that uses {1} will no longer be able to open it. Windows asks for administrator permission.", sel.A, sel.B),
                        "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                work(delegate
                {
                    string err;
                    if (!Com0Com.RemovePair(sel, out err)) return Lang.T("La paire n'a pas pu être supprimée : ", "The pair could not be removed: ") + err;
                    BeginInvokeSafe(delegate
                    {
                        if (sel.A.Equals(settings.ShareCom, StringComparison.OrdinalIgnoreCase))
                        {
                            settings.ShareCom = "";
                            settings.Save();
                            UpdateShareButton();
                        }
                    });
                    return null;
                });
            };
            refresh();
            if (f.ShowDialog(this) != DialogResult.OK) { f.Dispose(); return; }
            Com0Com.Pair chosen = pairs.SelectedItem as Com0Com.Pair;
            string want = on.Checked && chosen != null ? chosen.A : "";
            f.Dispose();
            if (want == settings.ShareCom) return;
            settings.ShareCom = want;
            settings.Save();
            Log.Write("Partage : " + (want.Length == 0 ? "arrêté" : "port COM virtuel " + want));
            UpdateShareButton();
            // Poste connecté en CAT direct : on reconnecte pour appliquer tout de suite.
            if (worker != null && settings.Interface == 0)
            {
                if (tuneTone != null) StopTune(null);
                if (pttActive) SetPttButton(false);
                Disconnect();
                Connect();
                return;
            }
            ShowStatus(want.Length == 0 ? Lang.T("Partage du poste arrêté (à la prochaine connexion)", "Rig sharing off (at the next connection)")
                                        : Lang.F("Partage du poste sur {0} : à la prochaine connexion du poste", "Rig sharing on {0}: at the next rig connection", want));
        }
    }
}
