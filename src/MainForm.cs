using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SDRLink
{
    public class MainForm : Form
    {
        const long Tolerance = 10;        // Hz : écart en dessous duquel on considère les fréquences égales
        const int EchoIgnoreMs = 800;
        const int SettleMs = 8000;          // après le chargement d'une page ou la connexion du poste : pas de WebSDR → poste     // durée pendant laquelle on ignore le retour d'un ordre qu'on vient d'envoyer

        enum Direction { Both, RigToSdr, SdrToRig, Off }

        readonly Settings settings;
        bool loading;
        // Mise à jour des textes de l'interface quand on change de langue.
        readonly List<Action> translators = new List<Action>();

        ComboBox cbInterface, cbBrand, cbModel, cbPort, cbBaud, cbStop, cbOmni, cbDirection, cbUrl, cbHrdTx, cbLang;
        TextBox txtCiv, txtHost;
        NumericUpDown numNetPort, numOffset, numUnmute;
        CheckBox chkRts, chkDtr, chkMode, chkAuto, chkMute, chkMini, chkReduced, chkTop, chkDark, chkPttView;
        TrackBar trkVolume, trkRigVolume;
        Label lblVolume, lblRigVolume;
        PaletteButton btnPalette;
        ComboBox cbFavList;
        Button btnCable;
        CheckBox chkNotch;
        FlowLayoutPanel pnlHamlib;
        CheckBox chkHamlibLaunch;
        CheckBox chkUpdates;
        ToolStripStatusLabel lblUpdate;   // nouvelle version disponible (clic : page de téléchargement)
        string updateVersion, updateUrl;
        TextBox txtRigctld;
        NumericUpDown numHamlibModel;
        Button btnMuteSdr, btnMuteRig, btnMuteSwap, btnMuteEsdr;
        Button btnMemRecent, btnScan, btnRec;
        bool sdrMuted, rigMuted;
        bool esdrMuted, esdrMutedByUs;   // son d'ExpertSDR coupé, et coupé par SDRLink
        bool esdrRunning, esdrWin;       // ExpertSDR lancé ; il joue par le son de Windows (sinon : liaison TCI)
        ExpertSdrTci esdrTci;            // liaison TCI permanente avec ExpertSDR (mute, volume)
        DateTime esdrTciRetry;
        bool esdrConnecting, esdrVolumeLoading;
        int esdrPoll;
        Label lblRigVolumeCaption, lblEsdrVolumeCaption, lblEsdrVolume;
        TrackBar trkEsdrVolume;
        FlowLayoutPanel audioRow;
        Control[] audioOrder;
        bool? audioShownRig, audioShownEsdr;   // ce qui est affiché sur la ligne de son (null : pas encore réglé)
        bool esdrUnreachableShown;             // message « ExpertSDR ne répond pas » déjà donné pour ce lancement
        readonly ToolTip tips = new ToolTip();
        int rigAfSupported = -1;    // -1 inconnu (pas connecté), 0 non pris en charge, 1 disponible
        bool rigVolumeLoading;
        Favorite currentFav;        // favori correspondant à la page affichée (porte son volume), null sinon
        bool volumeLoading;
        FlowLayoutPanel pnlModel, pnlCom, pnlOmni, pnlNet, pnlHrdTx, rowDisplay, rowUrl, rowConn, rowParams, rowHamlib;
        Button btnConnect, btnPtt;
        Label lblRig, lblSdr;
        StatusStrip strip;
        ToolStripStatusLabel status;
        WebView2 web;
        readonly Timer unmuteTimer = new Timer();
        readonly Timer syncTimer = new Timer();
        DateTime divergedSince = DateTime.MinValue;   // depuis quand le WebSDR n'est plus sur la fréquence du poste
        DateTime pttSince;                            // début de l'émission lancée par le bouton PTT
        bool rigTx;
        bool pttActive;   // émission lancée par le bouton PTT de SDRLink

        ComboBox cbStep, cbFreq;
        Button[] modeButtons;
        static readonly string[] ModeButtonModes = { Modes.USB, Modes.LSB, Modes.AM, Modes.FM, Modes.DATAU, Modes.CW };
        // DIGI et CW : un nouveau clic sur le bouton déjà actif passe de la bande latérale haute à la basse et inversement.
        string digiMode = Modes.DATAU, cwMode = Modes.CW;
        bool digiActive;            // bouton DIGI choisi (sert quand aucun poste n'indique son mode)
        bool tapShown;              // recopie du son demandée à la page
        AudioSink cableSink;        // sortie Windows (câble virtuel) recevant le son du WebSDR
        string shownModeFamily;     // mode mis en surbrillance sur les boutons
        static readonly long[] Steps = { 100, 250, 500, 1000, 2500, 5000, 10000 };
        long wheelTarget;           // fréquence poste visée par la molette
        DateTime wheelLast, wheelActiveUntil;
        int wheelRemainder;         // molettes "haute résolution" : fractions de cran accumulées
        // Affichages : 0 normal, 1 réduit (à partir de la ligne Synchro, page comprise), 2 mini (de Synchro au volume).
        int viewShown;              // affichage en place
        Size normalSize;            // taille de la fenêtre en affichage normal (pour y revenir)
        Size reducedSize;           // taille en affichage réduit (pour y revenir)
        int topRowsHeight;          // hauteur des lignes masquées par les affichages réduit et mini
        bool compactPending;
        bool wasMaximized;          // plein écran en affichage normal, à rétablir en y revenant
        FlowLayoutPanel rowSync;
        TableLayoutPanel mainTable;

        RigWorker worker;
        bool rigOnline;
        long rigF; string rigM;
        long sdrF; string sdrM; string sdrKind = "";
        long lastRigF; string lastRigFam;
        long lastSdrF; string lastSdrFam;
        DateTime ignoreRigUntil, ignoreSdrUntil;
        DateTime syncSettleUntil;   // jusqu'à cette heure, le WebSDR ne commande pas le poste (mise en place)
        bool firstPage = true;      // première page depuis le lancement de SDRLink

        public MainForm()
        {
            settings = Settings.Load();
            Lang.Code = settings.Language;
            Text = "SDRLink V" + Program.Version + " @F5PBG 2026 - WebSDR ⇄ Transceiver";
            // Icône « SDR / LINK » (barre des tâches, titre) : toutes les tailles de l'ico, chacune dessinée pour sa taille.
            try
            {
                using (Stream ico = typeof(MainForm).Assembly.GetManifestResourceStream("SDRLink.ico"))
                    if (ico != null) Icon = new Icon(ico);
            }
            catch (Exception ex) { Log.Write("Icône : " + ex.Message); }
            Size = DefaultWindowSize();
            StartPosition = FormStartPosition.CenterScreen;
            RestoreWindowBounds();
            Font = new Font("Segoe UI", 9f);
            BuildUi();
            unmuteTimer.Tick += delegate
            {
                unmuteTimer.Stop();
                if (!Transmitting) SetMuted(false);
            };
            // Surveillance de la synchro (toutes les secondes) et délai de sécurité du PTT.
            syncTimer.Interval = 1000;
            syncTimer.Tick += delegate { CheckSync(); CheckPttTimeout(); UpdateDisplays(); if (++esdrPoll % 3 == 0) CheckExpertSdr(); else RefreshExpertSdr(); };
            syncTimer.Start();
            scanTimer.Tick += delegate { ScanNext(); };
            LoadSettingsIntoUi();
            Load += delegate
            {
                InitWebView();
                if (settings.AutoConnect) Connect();
            };
            Shown += delegate { ApplyView(); FirstRunSearch(); if (settings.CheckUpdates) CheckForUpdate(); };
            Application.AddMessageFilter(new WheelFilter(this));
            Resize += delegate { if (compactPending && WindowState != FormWindowState.Minimized) ApplyView(); };
            FormClosing += delegate
            {
                // Enregistrement en cours : arrêté, et le fichier est proposé avant de quitter.
                if (recorder != null) StopRecording(true);
                // Affichage ptt : on revient d'abord à l'affichage d'avant, celui qui est enregistré.
                if (pttView) SetPttView(false);
                Disconnect();
                if (cableSink != null) { cableSink.Dispose(); cableSink = null; }
                // ExpertSDR coupé par SDRLink : son rétabli (Windows garde la coupure d'un logiciel d'une fois sur l'autre).
                try { if (esdrMutedByUs) SetExpertSdrMute(false); } catch { }
                if (esdrTci != null) { esdrTci.Dispose(); esdrTci = null; }
                SaveUiToSettings();
                SaveWindowBounds();
                settings.Save();
            };
        }

        static Size DefaultWindowSize()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            return new Size(Math.Min(1400, area.Width), Math.Min(900, area.Height));
        }

        // Reprend la taille et la position de la fenêtre en affichage normal enregistrées à la fermeture.
        // Elles servent aussi au retour de l'affichage réduit si l'application a été fermée en mode réduit.
        void RestoreWindowBounds()
        {
            if (settings.WinW < 300 || settings.WinH < 150) return;
            Rectangle r = new Rectangle(settings.WinX, settings.WinY, settings.WinW, settings.WinH);
            normalSize = r.Size;
            Size = r.Size;
            if (settings.RedW >= 300 && settings.RedH >= 100) reducedSize = new Size(settings.RedW, settings.RedH);
            // Position reprise seulement si la fenêtre reste visible (écran débranché depuis, par exemple).
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle seen = Rectangle.Intersect(s.WorkingArea, r);
                if (seen.Width >= 200 && seen.Height >= 100)
                {
                    StartPosition = FormStartPosition.Manual;
                    Location = r.Location;
                    break;
                }
            }
            // Plein écran à la fermeture : rétabli tout de suite, ou au retour de l'affichage réduit.
            if (settings.WinMax)
            {
                if (settings.ViewMode != 0) wasMaximized = true;
                else WindowState = FormWindowState.Maximized;
            }
        }

        void SaveWindowBounds()
        {
            Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            // Hors affichage normal, la taille à retenir est celle de l'affichage normal ; de même pour le réduit.
            Size normal = viewShown != 0 ? (normalSize.IsEmpty ? DefaultWindowSize() : normalSize) : b.Size;
            Size reduced = viewShown == 1 ? b.Size : reducedSize;
            settings.WinX = b.X;
            settings.WinY = b.Y;
            settings.WinW = normal.Width;
            settings.WinH = normal.Height;
            settings.RedW = reduced.Width;
            settings.RedH = reduced.Height;
            settings.WinMax = viewShown == 0 ? WindowState == FormWindowState.Maximized : wasMaximized;
        }

        // ---------------------------------------------------------------- Interface

        void BuildUi()
        {
            TableLayoutPanel table = mainTable = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.ColumnCount = 1;
            table.RowCount = 7;
            for (int i = 0; i < 6; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Ligne 1 : interface, marque, modèle, connexion
            FlowLayoutPanel row1 = rowConn = Row();
            cbInterface = Combo(170, new[] { "Port COM (CAT direct)", "OmniRig", "TCI", "Ham Radio Deluxe", "Hamlib (rigctld)" },
                                     new[] { "COM port (direct CAT)", "OmniRig", "TCI", "Ham Radio Deluxe", "Hamlib (rigctld)" });
            cbInterface.SelectedIndexChanged += delegate { UpdateInterfacePanels(); };
            row1.Controls.Add(Lbl("Interface :", "Interface:"));
            row1.Controls.Add(cbInterface);

            pnlModel = SubRow();
            cbBrand = Combo(90, RigCatalog.Brands);
            cbBrand.SelectedIndexChanged += delegate { FillModels(); };
            cbModel = Combo(130);
            cbModel.DropDownWidth = 180;   // « FT-1000MP Mark-V Field »
            cbModel.SelectedIndexChanged += delegate { ApplyModelDefaults(); };
            pnlModel.Controls.Add(Lbl("Marque :", "Brand:"));
            pnlModel.Controls.Add(cbBrand);
            pnlModel.Controls.Add(Lbl("Modèle :", "Model:"));
            pnlModel.Controls.Add(cbModel);
            row1.Controls.Add(pnlModel);

            btnConnect = new Button();
            btnConnect.Width = 110;
            btnConnect.Height = 27;
            btnConnect.Click += delegate { if (worker == null) Connect(); else Disconnect(); };
            row1.Controls.Add(btnConnect);
            chkAuto = Check("Connexion auto au démarrage", "Auto-connect at startup");
            row1.Controls.Add(chkAuto);
            row1.Controls.Add(Colorable("Log", Btn("Ouvrir le journal", "Open log", delegate
            {
                try { System.Diagnostics.Process.Start("notepad.exe", "\"" + Log.FilePath + "\""); }
                catch (Exception ex) { MessageBox.Show(Lang.T("Impossible d'ouvrir le journal : ", "Cannot open the log: ") + ex.Message); }
            })));
            chkDark = Check("Mode sombre", "Dark mode");
            chkDark.CheckedChanged += delegate { if (!loading) ApplyTheme(); };
            row1.Controls.Add(chkDark);
            btnPalette = new PaletteButton();
            btnPalette.Click += delegate { ShowPalette(); };
            row1.Controls.Add(btnPalette);
            row1.Controls.Add(Colorable("TextSize", Btn("Taille du texte…", "Text size…", delegate { AskFontScale(); })));
            chkUpdates = Check("Vérifier les mises à jour", "Check for updates");
            chkUpdates.CheckedChanged += delegate { if (!loading && chkUpdates.Checked) CheckForUpdate(); };
            row1.Controls.Add(chkUpdates);
            // Import/Export : tous les réglages (interface, favoris, mémoires, couleurs…) dans un fichier, pour retrouver
            // la même configuration sur un autre ordinateur. Clic gauche : menu ; clic droit : couleur du bouton.
            Button btnImpExp = Btn("Import/Export ▾", "Import/Export ▾", null);
            ContextMenuStrip ieMenu = new ContextMenuStrip();
            ToolStripMenuItem ieExport = new ToolStripMenuItem(), ieImport = new ToolStripMenuItem();
            ieMenu.Items.Add(ieExport);
            ieMenu.Items.Add(ieImport);
            ieMenu.Opening += delegate
            {
                ieExport.Text = Lang.T("Exporter tous les réglages…", "Export all settings…");
                ieImport.Text = Lang.T("Importer des réglages…", "Import settings…");
            };
            ieExport.Click += delegate { ExportSettings(); };
            ieImport.Click += delegate { ImportSettings(); };
            btnImpExp.Click += delegate { ieMenu.Show(btnImpExp, new Point(0, btnImpExp.Height)); };
            Tr(delegate { tips.SetToolTip(btnImpExp, Lang.T("Exporter ou importer tous les réglages, pour la même configuration sur un autre ordinateur (clic droit : couleur du bouton)", "Export or import all settings, for the same configuration on another computer (right-click: button colour)")); });
            row1.Controls.Add(Colorable("ImportExport", btnImpExp));

            // Ligne 2 : paramètres propres à l'interface choisie
            FlowLayoutPanel row2 = rowParams = Row();
            pnlCom = SubRow();
            cbPort = Combo(80);
            cbPort.DropDownStyle = ComboBoxStyle.DropDown;
            Button btnRefresh = new Button();
            btnRefresh.Text = "↻";
            btnRefresh.Width = 28;
            btnRefresh.Height = 25;
            btnRefresh.Click += delegate { RefreshPorts(); };
            cbBaud = Combo(80, "1200", "4800", "9600", "19200", "38400", "57600", "115200");
            cbBaud.DropDownStyle = ComboBoxStyle.DropDown;
            cbStop = Combo(40, "1", "2");
            txtCiv = new TextBox();
            txtCiv.Width = 40;
            chkRts = Check("RTS", "RTS");
            chkDtr = Check("DTR", "DTR");
            pnlCom.Controls.Add(Lbl("Port :", "Port:"));
            pnlCom.Controls.Add(cbPort);
            pnlCom.Controls.Add(btnRefresh);
            pnlCom.Controls.Add(Lbl("Vitesse :", "Baud rate:"));
            pnlCom.Controls.Add(cbBaud);
            pnlCom.Controls.Add(Lbl("Bits de stop :", "Stop bits:"));
            pnlCom.Controls.Add(cbStop);
            pnlCom.Controls.Add(Lbl("Adresse CI-V (hex) :", "CI-V address (hex):"));
            pnlCom.Controls.Add(txtCiv);
            pnlCom.Controls.Add(chkRts);
            pnlCom.Controls.Add(chkDtr);
            row2.Controls.Add(pnlCom);

            pnlOmni = SubRow();
            cbOmni = Combo(70, "Rig 1", "Rig 2");
            pnlOmni.Controls.Add(Lbl("Poste OmniRig :", "OmniRig rig:"));
            pnlOmni.Controls.Add(cbOmni);
            row2.Controls.Add(pnlOmni);

            pnlNet = SubRow();
            txtHost = new TextBox();
            txtHost.Width = 120;
            numNetPort = new NumericUpDown();
            numNetPort.Minimum = 1;
            numNetPort.Maximum = 65535;
            numNetPort.Width = 70;
            pnlNet.Controls.Add(Lbl("Hôte :", "Host:"));
            pnlNet.Controls.Add(txtHost);
            pnlNet.Controls.Add(Lbl("Port :", "Port:"));
            pnlNet.Controls.Add(numNetPort);
            row2.Controls.Add(pnlNet);

            // HRD : moyen de détecter l'émission par la pédale/le micro, que HRD ne signale pas toujours par MOX.
            pnlHrdTx = SubRow();
            cbHrdTx = Combo(190, new[] { "Boutons MOX/TX/PTT", "Boutons MOX/VOX/TX/PTT" },
                                 new[] { "MOX/TX/PTT buttons", "MOX/VOX/TX/PTT buttons" });
            pnlHrdTx.Controls.Add(Lbl("Détection TX (HRD) :", "TX detection (HRD):"));
            // Hamlib : rigctld déjà lancé (hôte/port), ou lancé par SDRLink avec le poste, le port COM et la vitesse choisis.
            pnlHamlib = SubRow();
            chkHamlibLaunch = Check("Lancer rigctld :", "Start rigctld:");
            chkHamlibLaunch.CheckedChanged += delegate { UpdateInterfacePanels(); };
            txtRigctld = new TextBox();
            txtRigctld.Width = 230;
            Button btnRigctld = Btn("…", "…", delegate { BrowseRigctld(); });
            numHamlibModel = new NumericUpDown();
            numHamlibModel.Minimum = 1;
            numHamlibModel.Maximum = 99999;
            numHamlibModel.Width = 65;
            pnlHamlib.Controls.Add(chkHamlibLaunch);
            pnlHamlib.Controls.Add(txtRigctld);
            pnlHamlib.Controls.Add(btnRigctld);
            pnlHamlib.Controls.Add(Lbl("N° de modèle Hamlib :", "Hamlib model no.:"));
            pnlHamlib.Controls.Add(numHamlibModel);
            pnlHamlib.Controls.Add(Btn("Télécharger Hamlib…", "Download Hamlib…", delegate { OpenHamlibDownload(); }));
            // Ligne à part (sous la ligne du port COM), affichée seulement pour Hamlib.
            rowHamlib = Row();
            rowHamlib.Controls.Add(pnlHamlib);
            pnlHrdTx.Controls.Add(cbHrdTx);
            row2.Controls.Add(pnlHrdTx);
            // Réglage lu à la connexion : on reconnecte pour l'appliquer tout de suite.
            cbHrdTx.SelectedIndexChanged += delegate
            {
                if (loading || worker == null || cbInterface.SelectedIndex != 3) return;
                if (pttActive) SetPttButton(false);
                Disconnect();
                Connect();
            };

            cbLang = Combo(110, Lang.Names);
            cbLang.MaxDropDownItems = Lang.Names.Length;
            cbLang.SelectedIndexChanged += delegate
            {
                if (loading) return;
                Lang.Code = Lang.Codes[Math.Max(0, cbLang.SelectedIndex)];
                settings.Language = Lang.Code;
                ApplyLanguage();
            };
            row2.Controls.Add(Lbl("Langue :", "Language:"));
            row2.Controls.Add(cbLang);
            // Câble SDRLINK : le son du WebSDR recopié vers un câble audio virtuel (entrée de WSJT-X...).
            btnCable = Btn("Câble SDRLINK ▾", "SDRLINK cable ▾", null);
            btnCable.ContextMenuStrip = BuildCableMenu();
            btnCable.Click += delegate { btnCable.ContextMenuStrip.Show(btnCable, new Point(0, btnCable.Height)); };
            row2.Controls.Add(btnCable);

            // Ligne 3 : synchronisation
            FlowLayoutPanel row3 = rowSync = Row();
            cbDirection = Combo(160, new[] { "Bidirectionnelle", "Radio → WebSDR", "WebSDR → Radio", "Désactivée" },
                                     new[] { "Both ways", "Rig → WebSDR", "WebSDR → Rig", "Off" });
            // Passage en "Bidirectionnelle" ou "Poste → WebSDR" : le WebSDR est recalé tout de suite sur le poste.
            cbDirection.SelectedIndexChanged += delegate
            {
                if (loading) return;
                Log.Write("Synchro : " + cbDirection.Text);
                if (RigToSdr && rigOnline) PushToSdr();
                // "WebSDR → Poste" : le poste est aussitôt réglé sur la fréquence et le mode du WebSDR.
                else if (Dir == Direction.SdrToRig) PushToRig(true, true);
            };
            chkMode = Check("Synchroniser le mode", "Sync mode");
            numOffset = new NumericUpDown();
            numOffset.Minimum = -2000000000;
            numOffset.Maximum = 2000000000;
            numOffset.Increment = 100;
            numOffset.Width = 100;
            numOffset.ThousandsSeparator = true;
            row3.Controls.Add(Lbl("Synchro :", "Sync:"));
            row3.Controls.Add(cbDirection);
            row3.Controls.Add(chkMode);
            row3.Controls.Add(Lbl("Décalage WebSDR (Hz) :", "WebSDR offset (Hz):"));
            row3.Controls.Add(numOffset);
            chkMute = Check("Couper le son du WebSDR en émission", "Mute WebSDR while transmitting");
            chkMute.CheckedChanged += delegate { if (worker != null) worker.PollTx = chkMute.Checked; ApplyMute(); };
            numUnmute = new NumericUpDown();
            numUnmute.Minimum = 0;
            numUnmute.Maximum = 10000;
            numUnmute.Increment = 250;
            numUnmute.Width = 65;
            row3.Controls.Add(chkMute);
            row3.Controls.Add(Lbl("rétablir après (ms) :", "restore after (ms):"));
            row3.Controls.Add(numUnmute);
            chkNotch = Check("Notch auto (DNF)", "Auto notch (DNF)");
            chkNotch.CheckedChanged += delegate { settings.AutoNotch = chkNotch.Checked; SetPageNotch(); };
            row3.Controls.Add(chkNotch);
            // Scan : réglages (fréquences ou mémoires) et marche / arrêt.
            row3.Controls.Add(Colorable("ScanSetup", Btn("Conf scan", "Scan setup", delegate { ConfigureScan(); })));
            btnScan = Btn("▶ Scan", "▶ Scan", delegate { if (scanning) StopScan(null); else StartScan(); });
            btnScan.Tag = Theme.Custom;
            btnScan.FlatStyle = FlatStyle.Flat;
            row3.Controls.Add(btnScan);
            // REC : enregistre le son entendu dans les haut-parleurs ; à l'arrêt, fenêtre pour sauvegarder le fichier.
            // Même allure que les boutons Mute : même police, même hauteur (30), mêmes marges, bordure qui fait contour.
            // « REC » est dessiné en image : en texte, il passerait à la ligne dans un carré de 30.
            btnRec = new Button();
            btnRec.AccessibleName = "REC";
            btnRec.MinimumSize = btnRec.Size = RecSize();
            btnRec.Margin = new Padding(6, 3, 3, 3);
            btnRec.Padding = Padding.Empty;
            btnRec.Tag = Theme.Custom;
            btnRec.FlatStyle = FlatStyle.Flat;
            btnRec.Click += delegate { if (recorder == null) StartRecording(); else StopRecording(false); };
            Tr(delegate { UpdateRecButton(); });
            // Clic droit : carte son du poste (enregistrée à la place des haut-parleurs quand seul le poste est écouté)
            // et couleur du bouton au repos. Le bouton est placé sur la ligne de son, après le dernier volume affiché.
            ContextMenuStrip recMenu = new ContextMenuStrip();
            recMenu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e) { e.Cancel = false; FillRecCardMenu(recMenu); };
            recMenu.Items.Add("-");
            btnRec.ContextMenuStrip = recMenu;

            // Ligne 4 : PTT, fréquences, modes, saisie ; l'affichage réduit coupe la fenêtre juste après cette ligne.
            rowDisplay = Row();
            rowDisplay.Layout += delegate { OnRowDisplayLayout(); };
            btnPtt = new Button();
            btnPtt.Size = new Size(130, 44);
            btnPtt.Margin = new Padding(8, 0, 0, 0);
            btnPtt.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            btnPtt.FlatStyle = FlatStyle.Flat;
            btnPtt.FlatAppearance.BorderSize = 2;
            btnPtt.Click += delegate { SetPttButton(!pttActive); };
            btnPtt.Tag = Theme.Custom;
            btnPtt.ContextMenuStrip = BuildPttMenu();
            lblRig = FreqLabel();
            lblSdr = FreqLabel();
            rowDisplay.Controls.Add(btnPtt);
            rowDisplay.Controls.Add(lblRig);
            rowDisplay.Controls.Add(lblSdr);

            // Boutons de mode : poste et WebSDR ensemble.
            modeButtons = new Button[ModeButtonModes.Length];
            for (int i = 0; i < ModeButtonModes.Length; i++)
            {
                string mode = ModeButtonModes[i];
                Button b = new Button();
                b.Text = ModeButtonText(mode, false);
                // DIGI et CW affichent aussi la bande latérale (DIGI U, CW L...) : police un peu plus petite.
                bool sided = mode == Modes.DATAU || mode == Modes.CW;
                b.Size = new Size(mode == Modes.DATAU ? 54 : mode == Modes.CW ? 46 : 44, 30);
                b.Margin = new Padding(i == 0 ? 8 : 1, 5, 0, 0);
                b.Padding = Padding.Empty;
                b.Font = new Font("Segoe UI", sided ? 8f : 9f, FontStyle.Bold);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 75);
                b.Tag = Theme.Custom;
                b.Click += delegate { OnModeButton(mode); };
                modeButtons[i] = b;
                rowDisplay.Controls.Add(b);
            }

            // Saisie d'une fréquence en kHz, avec l'historique des valeurs précédentes.
            cbFreq = new ComboBox();
            cbFreq.DropDownStyle = ComboBoxStyle.DropDown;
            cbFreq.Width = 75;
            cbFreq.MaxDropDownItems = 25;
            cbFreq.Margin = new Padding(2, 6, 2, 0);
            cbFreq.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { TuneFromEntry(); e.SuppressKeyPress = true; }
            };
            // Choisir une valeur dans la liste y accorde directement (BeginInvoke : la liste se réorganise après la sélection).
            cbFreq.SelectionChangeCommitted += delegate { BeginInvoke((Action)TuneFromEntry); };
            BuildFreqMenu();
            cbStep = Combo(70, new[] { "100 Hz", "250 Hz", "500 Hz", "1 kHz", "2,5 kHz", "5 kHz", "10 kHz" },
                               new[] { "100 Hz", "250 Hz", "500 Hz", "1 kHz", "2.5 kHz", "5 kHz", "10 kHz" });
            // Affichage réduit (à partir de la ligne Synchro, page comprise) et mini (de Synchro au volume) : l'un ou l'autre.
            chkReduced = Check("Affichage réduit", "Reduced view");
            chkMini = Check("Affichage mini", "Mini view");
            chkReduced.CheckedChanged += delegate { OnViewBox(chkReduced, chkMini, 1); };
            chkMini.CheckedChanged += delegate { OnViewBox(chkMini, chkReduced, 2); };
            chkTop = Check("Avant plan", "Always on top");
            chkTop.CheckedChanged += delegate { TopMost = chkTop.Checked; };
            rowDisplay.Controls.Add(Lbl("kHz :", "kHz:"));
            rowDisplay.Controls.Add(cbFreq);
            rowDisplay.Controls.Add(Colorable("Qsy", Btn("QSY", "QSY", delegate { TuneFromEntry(); })));
            rowDisplay.Controls.Add(Colorable("Save", Btn("Save", "Save", delegate
            {
                long f = CurrentFreq();
                if (f > 0) AddToHistory(f);
                else ShowStatus(Lang.T("Save : aucune fréquence connue (poste ou WebSDR)", "Save: no known frequency (rig or WebSDR)"));
            })));
            // Mémoires de fréquences : fenêtre de gestion, et menu des 20 dernières utilisées.
            Button btnMem = Btn("Mémoires", "Memories", delegate { ShowMemories(); });
            ContextMenuStrip memMenu = new ContextMenuStrip();
            ToolStripMenuItem memAdd = new ToolStripMenuItem();
            memMenu.Items.Add(memAdd);
            memMenu.Opening += delegate { memAdd.Text = Lang.T("Mémoriser la fréquence actuelle…", "Store the current frequency…"); };
            memAdd.Click += delegate { AddMemoryHere(); };
            btnMem.ContextMenuStrip = memMenu;
            Colorable("Memories", btnMem);
            btnMemRecent = Btn("▾", "▾", null);
            btnMemRecent.Margin = new Padding(0, 3, 3, 3);
            btnMemRecent.MinimumSize = new Size(22, 25);
            btnMemRecent.Click += delegate { ShowRecentMemories(); };
            Tr(delegate { tips.SetToolTip(btnMemRecent, Lang.F("{0} dernières mémoires utilisées", "Last {0} memories used", MemoryBook.RecentSize)); });
            rowDisplay.Controls.Add(btnMem);
            rowDisplay.Controls.Add(btnMemRecent);
            rowDisplay.Controls.Add(Lbl("Pas :", "Step:"));
            rowDisplay.Controls.Add(cbStep);
            // Les cases deux par deux, l'une sous l'autre : elles tiennent dans la hauteur du bouton PTT.
            FlowLayoutPanel views = SubRow();
            views.FlowDirection = FlowDirection.TopDown;
            chkReduced.Margin = chkMini.Margin = chkTop.Margin = new Padding(8, 2, 2, 0);
            views.Controls.Add(chkReduced);
            views.Controls.Add(chkMini);
            rowDisplay.Controls.Add(views);
            FlowLayoutPanel views2 = SubRow();
            views2.FlowDirection = FlowDirection.TopDown;
            views2.Controls.Add(chkTop);
            // Affichage ptt : toute petite fenêtre avec le seul bouton PTT ; décocher revient à l'affichage d'avant, à l'identique.
            chkPttView = Check("Affichage ptt", "PTT view");
            chkPttView.Margin = new Padding(8, 2, 2, 0);
            chkPttView.CheckedChanged += delegate { if (!loading) SetPttView(chkPttView.Checked); };
            views2.Controls.Add(chkPttView);
            rowDisplay.Controls.Add(views2);

            // Ligne 5 : barre d'adresse WebSDR et favoris
            rowUrl = Row();
            cbUrl = new ComboBox();
            cbUrl.Width = 380;
            cbUrl.MaxDropDownItems = 30;
            cbUrl.DropDownStyle = ComboBoxStyle.DropDown;
            cbUrl.SelectionChangeCommitted += delegate
            {
                Favorite f = cbUrl.SelectedItem as Favorite;
                if (f != null) BeginInvoke((Action)delegate { Navigate(f.Url); });
            };
            cbUrl.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { NavigateFromBar(); e.SuppressKeyPress = true; }
            };
            rowUrl.Controls.Add(Lbl("WebSDR :", "WebSDR:"));
            rowUrl.Controls.Add(cbUrl);

            // Liste de favoris affichée dans la liste déroulante des WebSDR (clic droit : renommer).
            cbFavList = Combo(110);
            cbFavList.SelectedIndexChanged += delegate
            {
                if (loading) return;
                if (cbFavList.SelectedIndex < 0 || cbFavList.SelectedIndex >= favListShown.Count) return;
                settings.CurrentList = favListShown[cbFavList.SelectedIndex];
                RefillUrlCombo();
            };
            ContextMenuStrip listMenu = new ContextMenuStrip();
            ToolStripMenuItem renameList = new ToolStripMenuItem();
            listMenu.Items.Add(renameList);
            listMenu.Opening += delegate
            {
                renameList.Text = Lang.F("Renommer « {0} »…", "Rename \"{0}\"…", FavoritesDialog.ListLabel(settings.ListNames, settings.CurrentList));
            };
            renameList.Click += delegate { RenameFavoriteList(); };
            cbFavList.ContextMenuStrip = listMenu;
            rowUrl.Controls.Add(cbFavList);
            rowUrl.Controls.Add(Colorable("Go", Btn("Aller", "Go", delegate { NavigateFromBar(); })));
            rowUrl.Controls.Add(Colorable("Back", Btn("◀ Retour", "◀ Back", delegate { if (web.CoreWebView2 != null && web.CanGoBack) web.GoBack(); })));
            rowUrl.Controls.Add(Colorable("Reload", Btn("Recharger", "Reload", delegate { if (web.CoreWebView2 != null) web.Reload(); })));
            rowUrl.Controls.Add(Colorable("FavAdd", Btn("★ Ajouter aux favoris", "★ Add to favorites", delegate { AddFavorite(); })));
            rowUrl.Controls.Add(Colorable("FavRemove", Btn("Retirer des favoris", "Remove from favorites", delegate { RemoveCurrentFavorite(); })));
            rowUrl.Controls.Add(Colorable("FavManage", Btn("Gérer les favoris…", "Manage favorites…", delegate { ManageFavorites(); })));
            rowUrl.Controls.Add(Colorable("Find", Btn("Rechercher des WebSDR…", "Find WebSDRs…", delegate { SearchWebSdrs(); })));

            // Volume du WebSDR seul (pas celui de Windows), mémorisé pour chaque favori.
            trkVolume = new TrackBar();
            trkVolume.Minimum = 0;
            trkVolume.Maximum = 100;
            trkVolume.TickStyle = TickStyle.None;
            trkVolume.AutoSize = false;
            trkVolume.Size = new Size(130, 28);
            trkVolume.SmallChange = 5;
            trkVolume.LargeChange = 10;
            trkVolume.Margin = new Padding(2, 2, 0, 0);
            trkVolume.ValueChanged += delegate { OnVolumeChanged(); };
            lblVolume = new Label();
            lblVolume.AutoSize = false;
            lblVolume.Size = new Size(44, 20);
            lblVolume.Margin = new Padding(0, 7, 2, 0);
            // Réglages de son regroupés : ils passent à la ligne d'un seul bloc si la fenêtre est étroite.
            FlowLayoutPanel audio = SubRow();
            audioRow = audio;
            Label lblVolumeCaption = Lbl("Volume WebSDR :", "WebSDR volume:");
            // La ligne de son commence toujours une nouvelle ligne, même quand elle tiendrait après les boutons.
            rowUrl.SetFlowBreak(rowUrl.Controls[rowUrl.Controls.Count - 1], true);
            rowUrl.Controls.Add(audio);

            // Coupures du son : WebSDR (dans la page) et poste (gain BF mis à zéro puis rétabli), et volume du poste.
            btnMuteSdr = Btn("Mute WebSDR", "Mute WebSDR", delegate { sdrMuted = !sdrMuted; ApplyVolume(); UpdateMuteButtons(); });
            // REC suit la hauteur des Mute (taille du texte) et redessine « REC » quand sa police change.
            btnMuteSdr.SizeChanged += delegate { ResizeRecButton(); };
            btnRec.FontChanged += delegate { UpdateRecButton(); };
            btnMuteRig = Btn("Mute TX", "Mute TRX", delegate { ToggleRigMute(); });
            // Inverse les deux coupures d'un coup (Mute TX seulement si le volume du poste est télécommandé).
            // Bouton ⇄ : passe le son d'une réception à la suivante (WebSDR, poste, ExpertSDR).
            btnMuteSwap = Btn("⇄", "⇄", delegate { SwapMutes(); });
            btnMuteSwap.AutoSize = false;
            btnMuteSwap.MinimumSize = new Size(25, 25);   // d'abord le minimum : celui de Btn (30 de large) empêcherait le carré
            btnMuteSwap.Size = new Size(25, 25);
            Tr(delegate { tips.SetToolTip(btnMuteSwap, Lang.T("Passer le son à la réception suivante (clic droit : réception à laisser coupée)", "Switch the audio to the next receiver (right-click: receiver to keep muted)")); });
            // Clic droit : coupure dont le bouton ⇄ ne tient pas compte (elle reste coupée).
            ContextMenuStrip swapMenu = new ContextMenuStrip();
            swapMenu.Opening += delegate { FillSwapMenu(swapMenu); };
            btnMuteSwap.ContextMenuStrip = swapMenu;
            // Visible seulement quand ExpertSDR tourne : coupe son son dans le mélangeur de volume de Windows.
            btnMuteEsdr = Btn("Mute ExpertSDR", "Mute ExpertSDR", delegate { ToggleExpertSdrMute(); });
            btnMuteEsdr.Visible = false;
            // Clic droit : port du serveur TCI d'ExpertSDR (40001 par défaut).
            ContextMenuStrip esdrMenu = new ContextMenuStrip();
            ToolStripMenuItem esdrPort = new ToolStripMenuItem();
            esdrMenu.Items.Add(esdrPort);
            esdrMenu.Opening += delegate { esdrPort.Text = Lang.F("Port TCI d'ExpertSDR : {0}…", "ExpertSDR TCI port: {0}…", settings.EsdrTciPort); };
            esdrPort.Click += delegate { AskExpertSdrPort(); };
            btnMuteEsdr.ContextMenuStrip = esdrMenu;
            // Mute ExpertSDR est masqué tant qu'ExpertSDR ne répond pas : le port se règle aussi par clic droit sur « Volume WebSDR ».
            lblVolumeCaption.ContextMenuStrip = audio.ContextMenuStrip = esdrMenu;
            btnMuteSwap.Visible = false;
            btnMuteSdr.Tag = btnMuteRig.Tag = btnMuteSwap.Tag = btnMuteEsdr.Tag = Theme.Custom;
            btnMuteSdr.FlatStyle = btnMuteRig.FlatStyle = btnMuteSwap.FlatStyle = btnMuteEsdr.FlatStyle = FlatStyle.Flat;
            trkRigVolume = new TrackBar();
            trkRigVolume.Minimum = 0;
            trkRigVolume.Maximum = 100;
            trkRigVolume.TickStyle = TickStyle.None;
            trkRigVolume.AutoSize = false;
            trkRigVolume.Size = new Size(110, 28);
            trkRigVolume.SmallChange = 5;
            trkRigVolume.LargeChange = 10;
            trkRigVolume.Margin = new Padding(2, 2, 0, 0);
            trkRigVolume.ValueChanged += delegate { OnRigVolumeChanged(); };
            lblRigVolume = new Label();
            lblRigVolume.AutoSize = false;
            lblRigVolume.Size = new Size(44, 20);
            lblRigVolume.Margin = new Padding(0, 7, 2, 0);
            // Volume TX et Mute TX ne sont affichés que si le volume du poste est télécommandé.
            lblRigVolumeCaption = Lbl("Volume TX :", "TRX volume:");
            lblRigVolumeCaption.Visible = trkRigVolume.Visible = lblRigVolume.Visible = btnMuteRig.Visible = false;
            // Volume d'ExpertSDR (mélangeur de Windows ou TCI), affiché quand ExpertSDR tourne.
            trkEsdrVolume = new TrackBar();
            trkEsdrVolume.Minimum = 0;
            trkEsdrVolume.Maximum = 100;
            trkEsdrVolume.TickStyle = TickStyle.None;
            trkEsdrVolume.AutoSize = false;
            trkEsdrVolume.Size = new Size(110, 28);
            trkEsdrVolume.SmallChange = 5;
            trkEsdrVolume.LargeChange = 10;
            trkEsdrVolume.Margin = new Padding(2, 2, 0, 0);
            trkEsdrVolume.ValueChanged += delegate { OnEsdrVolumeChanged(); };
            lblEsdrVolume = new Label();
            lblEsdrVolume.AutoSize = false;
            lblEsdrVolume.Size = new Size(44, 20);
            lblEsdrVolume.Margin = new Padding(0, 7, 2, 0);
            lblEsdrVolumeCaption = Lbl("Volume ExpertSDR :", "ExpertSDR volume:");
            lblEsdrVolumeCaption.Visible = trkEsdrVolume.Visible = lblEsdrVolume.Visible = false;
            // Ordre : ⇄, les trois Mute, puis chaque volume (libellé, réglette, valeur), et REC.
            // Les volumes masqués ne prennent pas de place : REC suit toujours le dernier volume affiché.
            audioOrder = new Control[]
            {
                btnMuteSwap, btnMuteSdr, btnMuteRig, btnMuteEsdr,
                lblVolumeCaption, trkVolume, lblVolume,
                lblRigVolumeCaption, trkRigVolume, lblRigVolume,
                lblEsdrVolumeCaption, trkEsdrVolume, lblEsdrVolume,
                btnRec,
            };
            audio.Controls.AddRange(audioOrder);

            web = new WebView2();
            web.Dock = DockStyle.Fill;

            table.Controls.Add(row1, 0, 0);
            table.Controls.Add(row2, 0, 1);
            table.Controls.Add(rowHamlib, 0, 2);
            table.Controls.Add(row3, 0, 3);
            table.Controls.Add(rowDisplay, 0, 4);
            table.Controls.Add(rowUrl, 0, 5);
            table.Controls.Add(web, 0, 6);

            strip = new StatusStrip();
            status = new ToolStripStatusLabel();
            status.Spring = true;
            status.TextAlign = ContentAlignment.MiddleLeft;
            strip.Items.Add(status);
            // Clic droit sur la barre d'état : derniers messages, et types de messages à ne plus afficher.
            ContextMenuStrip history = new ContextMenuStrip();
            history.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e) { e.Cancel = false; FillStatusHistory(history); };
            history.Items.Add("-");
            strip.ContextMenuStrip = history;
            lblUpdate = new ToolStripStatusLabel();
            lblUpdate.Visible = false;
            lblUpdate.IsLink = true;
            lblUpdate.Font = new Font(Font, FontStyle.Bold);
            lblUpdate.Click += delegate { OpenUpdatePage(); };
            Tr(delegate { if (updateVersion != null) lblUpdate.Text = Lang.F("⚠ Mise à jour V{0} disponible : cliquez pour la télécharger", "⚠ Update V{0} available: click to download it", updateVersion); });
            strip.Items.Add(lblUpdate);

            // Couleurs au clic droit des boutons à deux états (ajoutées après le menu propre au bouton, s'il en a un).
            AddColorMenu(btnConnect, Slot("ConnectOn", () => Lang.T("Couleur quand connecté", "Colour when connected")),
                Slot("ConnectOff", () => Lang.T("Couleur quand déconnecté", "Colour when disconnected")));
            AddColorMenu(btnCable, Slot("CableOn", () => Lang.T("Couleur quand activé", "Colour when on")),
                Slot("CableOff", () => Lang.T("Couleur quand désactivé", "Colour when off")));
            AddColorMenu(btnScan, Slot("ScanOff", () => Lang.T("Couleur scan arrêté", "Colour when the scan is stopped")),
                Slot("ScanOn", () => Lang.T("Couleur scan en marche", "Colour while scanning")));
            AddColorMenu(btnPtt, Slot("PttRx", () => Lang.T("Couleur en réception", "Colour when receiving")),
                Slot("PttTx", () => Lang.T("Couleur en émission (PTT activé)", "Colour when transmitting (PTT on)")));
            AddColorMenu(lblRig, Slot("RigBack", () => Lang.T("Couleur du fond", "Background colour")), Slot("RigText", () => Lang.T("Couleur du texte", "Text colour")));
            AddColorMenu(lblSdr, Slot("SdrBack", () => Lang.T("Couleur du fond", "Background colour")), Slot("SdrText", () => Lang.T("Couleur du texte", "Text colour")));
            foreach (Button b in modeButtons)
                AddColorMenu(b, Slot("ModeOn", () => Lang.T("Couleur du mode sélectionné", "Colour of the selected mode")),
                    Slot("ModeOff", () => Lang.T("Couleur des modes non sélectionnés", "Colour of the other modes")));
            AddColorMenu(btnMuteSdr, Slot("MuteSdrCut", () => Lang.T("Couleur son coupé", "Colour when muted")), Slot("MuteSdrOn", () => Lang.T("Couleur son actif", "Colour when the sound is on")));
            AddColorMenu(btnMuteRig, Slot("MuteRigCut", () => Lang.T("Couleur son coupé", "Colour when muted")), Slot("MuteRigOn", () => Lang.T("Couleur son actif", "Colour when the sound is on")));
            AddColorMenu(btnMuteEsdr, Slot("MuteEsdrCut", () => Lang.T("Couleur son coupé", "Colour when muted")), Slot("MuteEsdrOn", () => Lang.T("Couleur son actif", "Colour when the sound is on")));

            Controls.Add(table);
            Controls.Add(strip);
        }

        // ---------------------------------------------------------------- Import / export des réglages

        // Première ligne des fichiers exportés (ignorée à la lecture : elle n'a pas de « = »).
        const string ExportHeader = "# SDRLink";
        // Redémarrage après un import (remplacé dans les tests).
        Action restartApp = Application.Restart;

        void ExportSettings()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = Lang.T("Exporter tous les réglages de SDRLink", "Export all SDRLink settings");
                d.Filter = Lang.T("Réglages SDRLink (*.ini)|*.ini", "SDRLink settings (*.ini)|*.ini");
                d.FileName = "SDRLink-reglages-" + DateTime.Now.ToString("yyyy-MM-dd") + ".ini";
                d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (d.ShowDialog(this) == DialogResult.OK) ExportSettingsTo(d.FileName);
            }
        }

        // Réglages du moment (fenêtre comprise) enregistrés, puis recopiés tels quels dans le fichier choisi.
        bool ExportSettingsTo(string path)
        {
            try
            {
                SaveUiToSettings();
                SaveWindowBounds();
                settings.Save();
                string body = File.ReadAllText(Settings.FilePath, Encoding.UTF8);
                File.WriteAllText(path, ExportHeader + " V" + Program.Version + " : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\r\n" + body, new UTF8Encoding(false));
                Log.Write("Réglages exportés : " + path);
                ShowStatus(Lang.F("Réglages exportés dans « {0} »", "Settings exported to \"{0}\"", path));
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Export des réglages impossible : " + ex.Message);
                ShowStatus(Lang.T("Export des réglages impossible : ", "Cannot export the settings: ") + ex.Message);
                return false;
            }
        }

        void ImportSettings()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = Lang.T("Importer des réglages SDRLink", "Import SDRLink settings");
                d.Filter = Lang.T("Réglages SDRLink (*.ini)|*.ini", "SDRLink settings (*.ini)|*.ini");
                d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (d.ShowDialog(this) == DialogResult.OK) ImportSettingsFrom(d.FileName, true);
            }
        }

        // Le fichier est vérifié puis mis de côté : il remplace les réglages au redémarrage (Settings.Load), ce qui évite
        // que la fermeture de SDRLink réenregistre les anciens réglages par-dessus.
        bool ImportSettingsFrom(string path, bool ask)
        {
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex)
            {
                ShowStatus(Lang.T("Import des réglages impossible : ", "Cannot import the settings: ") + ex.Message);
                return false;
            }
            if (!text.StartsWith(ExportHeader) && !text.StartsWith("Interface=") && !text.Contains("\nInterface="))
            {
                ShowStatus(Lang.T("Import : ce fichier ne contient pas de réglages SDRLink", "Import: this file does not contain SDRLink settings"));
                return false;
            }
            if (ask && MessageBox.Show(this,
                    Lang.T("Tous les réglages actuels (interface, favoris, mémoires, couleurs…) vont être remplacés par ceux du fichier, puis SDRLink va redémarrer.\n\nLes réglages actuels sont gardés dans settings.avant-import.ini.\n\nContinuer ?",
                           "All current settings (interface, favorites, memories, colours…) will be replaced by those in the file, then SDRLink will restart.\n\nThe current settings are kept in settings.avant-import.ini.\n\nContinue?"),
                    "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
            try { File.WriteAllText(Settings.PendingImportPath, text, new UTF8Encoding(false)); }
            catch (Exception ex)
            {
                ShowStatus(Lang.T("Import des réglages impossible : ", "Cannot import the settings: ") + ex.Message);
                return false;
            }
            Log.Write("Import des réglages : " + path + " (pris en compte au redémarrage)");
            restartApp();
            return true;
        }

        // ---------------------------------------------------------------- Couleurs des boutons (clic droit)

        // Couleur réglable : clé dans Settings.Colors et titre du sous-menu.
        class ColorSlot { public string Key; public Func<string> Title; }

        static ColorSlot Slot(string key, Func<string> title) { return new ColorSlot { Key = key, Title = title }; }

        readonly Dictionary<Control, ColorSlot[]> colorSlots = new Dictionary<Control, ColorSlot[]>();
        readonly HashSet<ContextMenuStrip> colorMenus = new HashSet<ContextMenuStrip>();
        readonly Dictionary<Button, string> plainColorKeys = new Dictionary<Button, string>();
        const string ColorItemTag = "couleurs";

        // Bouton ordinaire (un seul état) : couleur du thème, ou celle choisie au clic droit.
        Button Colorable(string key, Button b)
        {
            plainColorKeys[b] = key;
            AddColorMenu(b, Slot(key, () => Lang.T("Couleur du bouton", "Button colour")));
            return b;
        }

        // Ajoute les sous-menus de couleur au clic droit (menu existant conservé, sinon menu créé).
        // Un menu partagé entre plusieurs commandes ne montre que les couleurs de celle qui a été cliquée.
        void AddColorMenu(Control c, params ColorSlot[] slots)
        {
            colorSlots[c] = slots;
            ContextMenuStrip m = c.ContextMenuStrip;
            if (m == null)
            {
                m = new ContextMenuStrip();
                m.Items.Add(new ToolStripSeparator { Tag = ColorItemTag });   // sinon le menu vide ne s'ouvre pas
                c.ContextMenuStrip = m;
            }
            if (!colorMenus.Add(m)) return;
            m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                FillColorItems(m);
                if (m.Items.Count > 0) e.Cancel = false;
            };
        }

        void FillColorItems(ContextMenuStrip m)
        {
            for (int i = m.Items.Count - 1; i >= 0; i--)
                if (ColorItemTag.Equals(m.Items[i].Tag)) m.Items.RemoveAt(i);
            ColorSlot[] slots;
            if (m.SourceControl == null || !colorSlots.TryGetValue(m.SourceControl, out slots)) return;
            if (m.Items.Count > 0) m.Items.Add(new ToolStripSeparator { Tag = ColorItemTag });
            foreach (ColorSlot slot in slots)
            {
                string key = slot.Key;
                ToolStripMenuItem it = BuildColorMenu(slot.Title(), ColorOf(key), ButtonColors, false, delegate(int rgb) { SetColor(key, rgb); });
                it.Tag = ColorItemTag;
                m.Items.Add(it);
            }
        }

        // Couleur choisie (RVB) ou -1 : couleur par défaut.
        int ColorOf(string key)
        {
            int v;
            return settings.Colors.TryGetValue(key, out v) ? v : -1;
        }

        void SetColor(string key, int rgb)
        {
            if (rgb < 0) settings.Colors.Remove(key);
            else settings.Colors[key] = rgb & 0xFFFFFF;
            Log.Write("Couleur " + key + " : " + (rgb < 0 ? "par défaut" : (rgb & 0xFFFFFF).ToString("X6")));
            settings.Save();
            RefreshColors();
        }

        // Remet toutes les couleurs réglables (thème, état des boutons, couleurs choisies).
        void RefreshColors()
        {
            foreach (KeyValuePair<Button, string> p in plainColorKeys) PaintKeyed(p.Key, p.Value);
            UpdateConnectButton();
            UpdateCableButton();
            UpdateScanButton();
            UpdatePttButton();
            UpdateMuteButtons();
            UpdateFreqColors();
            shownModeFamily = null;   // force la remise en couleur des boutons de mode
            UpdateDisplays();
        }

        // Bouton ordinaire : couleur choisie pour la clé, sinon celle du thème.
        void PaintKeyed(Button b, string key)
        {
            int c = ColorOf(key);
            if (c < 0) Theme.StyleButton(b);
            else PaintCustom(b, c);
        }

        // Fond choisi, texte noir ou blanc selon le fond, bordure qui fait contour (comme REC).
        static void PaintCustom(Button b, int rgb)
        {
            Color back = Opaque(rgb);
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.BackColor = back;
            b.ForeColor = Contrast(back);
            b.FlatAppearance.BorderColor = Outline(back);
        }

        static Color Opaque(int rgb) { return Color.FromArgb(255, Color.FromArgb(rgb)); }

        static bool IsLight(Color c) { return c.R * 299 + c.G * 587 + c.B * 114 > 160000; }

        static Color Contrast(Color back) { return IsLight(back) ? Color.Black : Color.White; }

        // RADIO et WEBSDR : fond et texte choisis, sinon ceux du thème ; en émission, RADIO passe en rouge
        // (rouge vif sur fond sombre, rouge foncé sur fond clair).
        void UpdateFreqColors()
        {
            int rb = ColorOf("RigBack"), sb = ColorOf("SdrBack");
            lblRig.BackColor = rb < 0 ? Theme.Back : Opaque(rb);
            lblSdr.BackColor = sb < 0 ? Theme.Back : Opaque(sb);
            lblRig.ForeColor = RigFore();
            int st = ColorOf("SdrText");
            lblSdr.ForeColor = st >= 0 ? Opaque(st) : IsLight(lblSdr.BackColor) ? Color.FromArgb(0, 120, 60) : Color.FromArgb(90, 225, 130);
        }

        Color RigFore()
        {
            bool light = IsLight(lblRig.BackColor);
            if (Transmitting) return light ? Color.FromArgb(200, 0, 0) : Color.FromArgb(255, 95, 95);
            int t = ColorOf("RigText");
            return t >= 0 ? Opaque(t) : light ? Color.FromArgb(0, 90, 160) : Color.FromArgb(110, 185, 255);
        }

        // ---------------------------------------------------------------- Fabrication des contrôles (textes bilingues)

        void Tr(Action apply)
        {
            translators.Add(apply);
            apply();
        }

        static FlowLayoutPanel Row()
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.Dock = DockStyle.Fill;
            p.WrapContents = true;
            p.Padding = new Padding(4, 2, 4, 2);
            return p;
        }

        static FlowLayoutPanel SubRow()
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.WrapContents = false;
            p.Margin = new Padding(0);
            return p;
        }

        Label Lbl(string fr, string en)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Margin = new Padding(8, 7, 2, 0);
            Tr(delegate { l.Text = Lang.T(fr, en); });
            return l;
        }

        // Liste dont les éléments ne dépendent pas de la langue.
        static ComboBox Combo(int width, params string[] items)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = width;
            c.Margin = new Padding(2, 3, 2, 3);
            c.Items.AddRange(items);
            return c;
        }

        // Liste traduite : les éléments sont remplacés au changement de langue, la sélection est conservée.
        ComboBox Combo(int width, string[] fr, string[] en)
        {
            ComboBox c = Combo(width);
            Tr(delegate
            {
                int sel = c.SelectedIndex;
                c.Items.Clear();
                for (int i = 0; i < fr.Length; i++) c.Items.Add(Lang.T(fr[i], en[i]));
                if (sel >= 0) c.SelectedIndex = sel;
            });
            return c;
        }

        CheckBox Check(string fr, string en)
        {
            CheckBox c = new CheckBox();
            c.AutoSize = true;
            c.Margin = new Padding(8, 6, 2, 0);
            Tr(delegate { c.Text = Lang.T(fr, en); });
            return c;
        }

        Button Btn(string fr, string en, EventHandler click)
        {
            Button b = new Button();
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(30, 25);
            b.Click += click;
            Tr(delegate { b.Text = Lang.T(fr, en); });
            return b;
        }

        // Couleurs fixées par UpdateDisplays/ApplyTheme selon le thème et l'émission.
        static Label FreqLabel()
        {
            Label l = new Label();
            l.AutoSize = false;
            l.Font = new Font("Consolas", 13f, FontStyle.Bold);
            l.Margin = new Padding(8, 8, 0, 0);
            l.Tag = Theme.Custom;
            return l;
        }

        // Thème clair/sombre de toute la fenêtre ; les éléments colorés de SDRLink prennent leurs teintes adaptées.
        // Palette : couleur de fond du thème en cours (sombre ou clair), choisie dans une petite fenêtre sous le bouton.
        void ShowPalette()
        {
            PalettePopup p = new PalettePopup(Theme.Dark ? Theme.DarkBase : Theme.LightBase);
            Point below = btnPalette.PointToScreen(new Point(0, btnPalette.Height + 2));
            p.Location = below;
            p.FormClosed += delegate
            {
                if (!p.Picked) return;
                if (Theme.Dark) { Theme.DarkBase = p.Chosen; settings.DarkBase = Theme.ToHex(p.Chosen); }
                else { Theme.LightBase = p.Chosen; settings.LightBase = Theme.ToHex(p.Chosen); }
                ApplyTheme();
            };
            p.Show(this);
        }

        void ApplyTheme()
        {
            Theme.Dark = chkDark.Checked;
            settings.DarkMode = Theme.Dark;
            Theme.DarkBase = Theme.FromHex(settings.DarkBase);
            Theme.LightBase = Theme.FromHex(settings.LightBase);
            SuspendLayout();
            Theme.Apply(this);
            // Couleurs choisies au clic droit : reposées après le thème.
            foreach (KeyValuePair<Button, string> p in plainColorKeys) PaintKeyed(p.Key, p.Value);
            UpdateConnectButton();
            UpdateCableButton();
            UpdateFreqColors();
            shownModeFamily = null;     // force la remise en couleur des boutons de mode
            ResumeLayout();
            btnPalette.Invalidate();
            UpdatePttButton();
            UpdateMuteButtons();
            UpdateDisplays();
            StyleUpdateLabel();
            UpdateScanButton();
        }

        // Largeur fixe des fréquences, calculée sur une fréquence HF avec le mode le plus long : les boutons à droite
        // ne bougent plus quand le mode change (USB, AM...). Une fréquence plus longue (VHF, GHz) élargit la zone une fois.
        void SizeFreqLabels()
        {
            FitWidth(lblRig, Lang.T("RADIO", "RIG") + " 14.074.000 RTTY-R", true);
            FitWidth(lblSdr, "WEBSDR 14.074.000 USB", true);
        }

        static void FitWidth(Label l, string text, bool reset)
        {
            Size s = TextRenderer.MeasureText(text, l.Font);
            int w = s.Width + 6;
            if (reset || w > l.Width) l.Size = new Size(w, s.Height + 2);
        }

        // Applique la langue choisie à tous les textes ; "loading" évite que les listes rechargées déclenchent des actions.
        void ApplyLanguage()
        {
            bool wasLoading = loading;
            loading = true;
            foreach (Action a in translators) a();
            loading = wasLoading;
            UpdateConnectButton();
            SizeFreqLabels();
            if (cbFavList.Items.Count > 0) FillFavListCombo();
            UpdateCableButton();
            UpdateScanButton();
            if (worker == null) ShowStatus(Lang.T("Déconnecté", "Disconnected"));
            UpdateDisplays();
            // Les textes ont changé de longueur : on recalcule la hauteur de l'affichage mini.
            if (viewShown != 0) KeepRowWhole();
            if (viewShown == 2) FitMiniHeight();
        }

        void UpdateConnectButton()
        {
            btnConnect.Text = worker == null ? Lang.T("Connecter", "Connect") : Lang.T("Déconnecter", "Disconnect");
            PaintKeyed(btnConnect, worker == null ? "ConnectOff" : "ConnectOn");
        }

        void UpdateInterfacePanels()
        {
            int i = cbInterface.SelectedIndex;
            // Hamlib avec rigctld lancé par SDRLink : il faut aussi le poste, le port COM et la vitesse.
            pnlModel.Visible = pnlCom.Visible = i == 0 || (i == 4 && chkHamlibLaunch.Checked);
            pnlOmni.Visible = i == 1;
            pnlNet.Visible = i >= 2;
            pnlHrdTx.Visible = i == 3;
            pnlHamlib.Visible = i == 4;
            rowHamlib.Visible = i == 4 && !TopRowsHidden;
            if (i != lastInterfaceShown && i >= 2)
            {
                if (lastInterfaceShown >= 2) StoreNetSettings(lastInterfaceShown);
                txtHost.Text = i == 2 ? settings.TciHost : i == 3 ? settings.HrdHost : settings.HamlibHost;
                numNetPort.Value = i == 2 ? settings.TciPort : i == 3 ? settings.HrdPort : settings.HamlibPort;
            }
            lastInterfaceShown = i;
        }

        int lastInterfaceShown = -1;

        // Hôte et port affichés : ceux de l'interface réseau choisie (TCI, HRD ou Hamlib).
        void StoreNetSettings(int i)
        {
            if (i == 2) { settings.TciHost = txtHost.Text.Trim(); settings.TciPort = (int)numNetPort.Value; }
            if (i == 3) { settings.HrdHost = txtHost.Text.Trim(); settings.HrdPort = (int)numNetPort.Value; }
            if (i == 4) { settings.HamlibHost = txtHost.Text.Trim(); settings.HamlibPort = (int)numNetPort.Value; }
        }

        void BrowseRigctld()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = Lang.T("Emplacement de rigctld.exe (Hamlib)", "Location of rigctld.exe (Hamlib)");
                d.Filter = "rigctld (rigctld*.exe)|rigctld*.exe|" + Lang.T("Programmes (*.exe)|*.exe", "Programs (*.exe)|*.exe");
                if (txtRigctld.Text != "" && File.Exists(txtRigctld.Text)) d.InitialDirectory = Path.GetDirectoryName(txtRigctld.Text);
                if (d.ShowDialog(this) == DialogResult.OK) txtRigctld.Text = d.FileName;
            }
        }

        void OpenHamlibDownload()
        {
            try { System.Diagnostics.Process.Start("https://github.com/Hamlib/Hamlib/releases/latest"); } catch { }
        }

        void FillModels()
        {
            cbModel.Items.Clear();
            foreach (RigModel m in RigCatalog.ForBrand((string)cbBrand.SelectedItem)) cbModel.Items.Add(m);
            if (cbModel.Items.Count > 0) cbModel.SelectedIndex = 0;
        }

        void ApplyModelDefaults()
        {
            RigModel m = cbModel.SelectedItem as RigModel;
            if (m == null || loading) return;
            cbBaud.Text = m.Baud.ToString(CultureInfo.InvariantCulture);
            cbStop.SelectedItem = m.StopBits.ToString(CultureInfo.InvariantCulture);
            txtCiv.Text = m.Protocol == RigProtocol.Icom ? m.CivAddress.ToString("X2") : "";
            int hamlibId;
            if (HamlibRig.ModelIds.TryGetValue(m.Name, out hamlibId)) numHamlibModel.Value = hamlibId;
            txtCiv.Enabled = m.Protocol == RigProtocol.Icom;
        }

        void RefreshPorts()
        {
            string current = cbPort.Text;
            cbPort.Items.Clear();
            string[] ports = SerialPort.GetPortNames();
            Array.Sort(ports, delegate(string a, string b) { return PortNumber(a).CompareTo(PortNumber(b)); });
            foreach (string p in ports) if (p.StartsWith("COM")) cbPort.Items.Add(p);
            cbPort.Text = current;
        }

        static int PortNumber(string p)
        {
            int n;
            return int.TryParse(p.Replace("COM", ""), out n) ? n : 9999;
        }

        void LoadSettingsIntoUi()
        {
            loading = true;
            cbLang.SelectedIndex = Math.Max(0, Array.IndexOf(Lang.Codes, Lang.Code));
            chkHamlibLaunch.Checked = settings.HamlibLaunch;
            cbInterface.SelectedIndex = Math.Max(0, Math.Min(4, settings.Interface));
            cbBrand.SelectedItem = settings.Brand;
            if (cbBrand.SelectedIndex < 0) cbBrand.SelectedIndex = 0;
            FillModels();
            foreach (object o in cbModel.Items) if (o.ToString() == settings.Model) cbModel.SelectedItem = o;
            RefreshPorts();
            cbPort.Text = settings.Port;
            cbBaud.Text = settings.Baud.ToString(CultureInfo.InvariantCulture);
            cbStop.SelectedItem = settings.StopBits == 2 ? "2" : "1";
            txtCiv.Text = settings.Civ;
            RigModel model = cbModel.SelectedItem as RigModel;
            txtCiv.Enabled = model != null && model.Protocol == RigProtocol.Icom;
            chkRts.Checked = settings.Rts;
            chkDtr.Checked = settings.Dtr;
            cbOmni.SelectedIndex = settings.OmniRigNumber == 2 ? 1 : 0;
            int hamlibDefault;
            if (!HamlibRig.ModelIds.TryGetValue(settings.Model, out hamlibDefault)) hamlibDefault = 1;
            numHamlibModel.Value = Math.Max(1, Math.Min(99999, settings.HamlibModel > 0 ? settings.HamlibModel : hamlibDefault));
            txtRigctld.Text = Rigctld.FindExe(settings.RigctldPath) ?? settings.RigctldPath;
            lastInterfaceShown = -1;
            chkAuto.Checked = settings.AutoConnect;
            cbHrdTx.SelectedIndex = Math.Max(0, Math.Min(cbHrdTx.Items.Count - 1, settings.HrdTxDetect));
            foreach (string f in settings.FreqHistory) cbFreq.Items.Add(f);
            int stepIndex = Array.IndexOf(Steps, settings.TuneStepHz);
            cbStep.SelectedIndex = stepIndex >= 0 ? stepIndex : 0;
            chkReduced.Checked = settings.ViewMode == 1;
            chkMini.Checked = settings.ViewMode == 2;
            chkTop.Checked = settings.TopMost;
            chkDark.Checked = settings.DarkMode;
            chkUpdates.Checked = settings.CheckUpdates;
            trkVolume.Value = settings.DefaultVolume;
            chkMute.Checked = settings.MuteOnTx;
            chkNotch.Checked = settings.AutoNotch;
            numUnmute.Value = Math.Max(0, Math.Min(10000, settings.UnmuteDelayMs));
            cbDirection.SelectedIndex = Math.Max(0, Math.Min(3, settings.Direction));
            chkMode.Checked = settings.SyncMode;
            numOffset.Value = settings.OffsetHz;
            FillFavListCombo();
            RefillUrlCombo();
            cbUrl.Text = settings.LastUrl;
            UpdateInterfacePanels();
            loading = false;
            ApplyLanguage();
            ApplyTheme();
            UpdateRigVolumeControls();
            if (settings.FontScale != 100) ApplyFontScale(settings.FontScale);
        }

        void SaveUiToSettings()
        {
            settings.Interface = cbInterface.SelectedIndex;
            settings.Brand = (string)cbBrand.SelectedItem;
            settings.Model = cbModel.SelectedItem != null ? cbModel.SelectedItem.ToString() : "";
            settings.Port = cbPort.Text.Trim();
            int baud;
            if (int.TryParse(cbBaud.Text, out baud)) settings.Baud = baud;
            settings.StopBits = (string)cbStop.SelectedItem == "2" ? 2 : 1;
            settings.Civ = txtCiv.Text.Trim();
            settings.Rts = chkRts.Checked;
            settings.Dtr = chkDtr.Checked;
            settings.OmniRigNumber = cbOmni.SelectedIndex + 1;
            StoreNetSettings(settings.Interface);
            settings.HamlibLaunch = chkHamlibLaunch.Checked;
            settings.RigctldPath = txtRigctld.Text.Trim();
            settings.HamlibModel = (int)numHamlibModel.Value;
            settings.AutoConnect = chkAuto.Checked;
            settings.HrdTxDetect = cbHrdTx.SelectedIndex;
            settings.Language = Lang.Code;
            settings.TuneStepHz = Steps[Math.Max(0, cbStep.SelectedIndex)];
            settings.FreqHistory.Clear();
            foreach (object o in cbFreq.Items) settings.FreqHistory.Add(o.ToString());
            settings.ViewMode = WantedView;
            settings.TopMost = chkTop.Checked;
            settings.CheckUpdates = chkUpdates.Checked;
            settings.MuteOnTx = chkMute.Checked;
            settings.UnmuteDelayMs = (int)numUnmute.Value;
            settings.Direction = cbDirection.SelectedIndex;
            settings.SyncMode = chkMode.Checked;
            settings.OffsetHz = (long)numOffset.Value;
            if (web.Source != null) settings.LastUrl = web.Source.ToString();
        }

        // ---------------------------------------------------------------- Poste

        void Connect()
        {
            SaveUiToSettings();
            Func<IRig> factory;
            switch (settings.Interface)
            {
                case 0:
                {
                    RigModel model = cbModel.SelectedItem as RigModel;
                    if (model == null) { MessageBox.Show(Lang.T("Choisissez un modèle de poste.", "Please choose a rig model.")); return; }
                    SerialSettings ss = new SerialSettings();
                    ss.Port = settings.Port;
                    ss.Baud = settings.Baud;
                    ss.StopBits = settings.StopBits;
                    ss.Rts = settings.Rts;
                    ss.Dtr = settings.Dtr;
                    byte civ = model.CivAddress;
                    if (model.Protocol == RigProtocol.Icom &&
                        !byte.TryParse(settings.Civ.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out civ))
                    {
                        MessageBox.Show(Lang.T("Adresse CI-V invalide (ex. 70 pour l'IC-7000).", "Invalid CI-V address (e.g. 70 for the IC-7000)."));
                        return;
                    }
                    factory = delegate
                    {
                        switch (model.Protocol)
                        {
                            case RigProtocol.Yaesu: return new YaesuRig(ss, model.FreqDigits);
                            case RigProtocol.Yaesu817: return new Ft817Rig(ss);
                            case RigProtocol.YaesuOld: return new YaesuOldRig(ss, model.Name);
                            case RigProtocol.Icom: return new IcomRig(ss, civ);
                            // Gain BF Elecraft : 000-250, sauf K4 : 000-060 (Hamlib).
                            case RigProtocol.Elecraft: return new KenwoodRig(ss, true) { AfMax = model.Name == "K4" ? 60 : 250 };
                            default: return new KenwoodRig(ss, false);
                        }
                    };
                    break;
                }
                case 1:
                {
                    int n = settings.OmniRigNumber;
                    factory = delegate { return new OmniRigRig(n); };
                    break;
                }
                case 2:
                {
                    string h = settings.TciHost; int p = settings.TciPort;
                    factory = delegate { return new TciRig(h, p); };
                    break;
                }
                case 4:
                    if (!PrepareHamlib(out factory)) return;
                    break;
                default:
                {
                    string h = settings.HrdHost; int p = settings.HrdPort;
                    HrdRig.TxDetect detect = (HrdRig.TxDetect)settings.HrdTxDetect;
                    factory = delegate { return new HrdRig(h, p, detect); };
                    break;
                }
            }

            lastRigF = 0; lastRigFam = null; rigF = 0; rigM = null;
            // HRD tourne sur le même PC : on peut l'interroger plus souvent pour réagir plus vite au PTT.
            worker = new RigWorker(factory, settings.Interface == 3 ? 100 : 200);
            worker.StateReceived += delegate(long f, string m) { BeginInvokeSafe(delegate { OnRigState(f, m); }); };
            worker.StatusChanged += delegate(string msg, bool online) { BeginInvokeSafe(delegate { OnRigStatus(msg, online); }); };
            worker.TxChanged += delegate(int tx) { BeginInvokeSafe(delegate { OnRigTx(tx); }); };
            worker.AfGainChanged += delegate(int a) { BeginInvokeSafe(delegate { OnRigAfGain(a); }); };
            worker.PollTx = chkMute.Checked;
            rigAfSupported = -1;
            rigMuted = false;
            worker.Start();
            UpdateRigVolumeControls();
            pttActive = false;
            UpdatePttButton();
            UpdateConnectButton();
            SetConfigEnabled(false);
        }

        // Hamlib : lance rigctld si demandé (poste, port COM, vitesse de la ligne de réglages), puis s'y connecte.
        bool PrepareHamlib(out Func<IRig> factory)
        {
            factory = null;
            string host = settings.HamlibHost;
            int port = settings.HamlibPort;
            if (settings.HamlibLaunch)
            {
                string exe = Rigctld.FindExe(settings.RigctldPath);
                if (exe == null)
                {
                    if (MessageBox.Show(this, Lang.T("rigctld.exe (Hamlib) est introuvable. Indiquez son emplacement avec le bouton « … », " +
                            "ou installez Hamlib (gratuit).\n\nOuvrir la page de téléchargement de Hamlib ?",
                            "rigctld.exe (Hamlib) was not found. Set its location with the \"…\" button, " +
                            "or install Hamlib (free).\n\nOpen the Hamlib download page?"),
                            "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) OpenHamlibDownload();
                    return false;
                }
                txtRigctld.Text = settings.RigctldPath = exe;
                if (settings.Port == "")
                {
                    MessageBox.Show(this, Lang.T("Choisissez le port COM du poste.", "Please choose the rig's COM port."), "SDRLink");
                    return false;
                }
                // Options série : celles de la ligne de réglages (RTS/DTR seulement s'ils sont cochés).
                List<string> conf = new List<string>();
                conf.Add("stop_bits=" + settings.StopBits);
                if (settings.Rts) conf.Add("rts_state=ON");
                if (settings.Dtr) conf.Add("dtr_state=ON");
                string args = string.Format(CultureInfo.InvariantCulture, "-m {0} -r {1} -s {2} -T 127.0.0.1 -t {3} -C {4}",
                    settings.HamlibModel, settings.Port, settings.Baud, port, string.Join(",", conf.ToArray()));
                RigModel model = cbModel.SelectedItem as RigModel;
                byte civ;
                if (model != null && model.Protocol == RigProtocol.Icom &&
                    byte.TryParse(settings.Civ.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out civ))
                    args += " -c 0x" + civ.ToString("X2");
                try { Rigctld.Start(exe, args); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, Lang.T("Impossible de lancer rigctld : ", "Cannot start rigctld: ") + ex.Message, "SDRLink");
                    return false;
                }
                host = "127.0.0.1";
            }
            factory = delegate { return new HamlibRig(host, port); };
            return true;
        }

        void Disconnect()
        {
            if (worker == null) return;
            RigWorker w = worker;
            // Son du poste coupé par SDRLink : on le rétablit avant de lâcher la liaison.
            if (rigMuted) w.SetAfGain(trkRigVolume.Value);
            worker = null;
            w.Stop();
            Rigctld.Stop();
            rigMuted = false;
            rigAfSupported = -1;
            UpdateRigVolumeControls();
            rigOnline = false;
            rigF = 0;
            rigTx = false;
            pttActive = false;
            ApplyMute();
            UpdatePttButton();
            UpdateConnectButton();
            ShowStatus(Lang.T("Déconnecté", "Disconnected"));
            SetConfigEnabled(true);
            UpdateDisplays();
        }

        // Pendant la connexion, seuls les champs sont verrouillés : les libellés restent lisibles (surtout en mode sombre).
        void SetConfigEnabled(bool on)
        {
            cbInterface.Enabled = on;
            foreach (FlowLayoutPanel p in new[] { pnlModel, pnlCom, pnlOmni, pnlNet })
                foreach (Control c in p.Controls)
                    if (!(c is Label)) c.Enabled = on;
        }

        void BeginInvokeSafe(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        void OnRigStatus(string msg, bool online)
        {
            if (worker == null) return;
            rigOnline = online;
            ShowStatus(msg);
            UpdateDisplays();
        }

        // Cocher une case passe à cet affichage (l'autre case se décoche) ; décocher la case en place ramène toujours
        // à l'affichage normal, les deux cases décochées.
        bool viewSwitching;

        void OnViewBox(CheckBox box, CheckBox other, int view)
        {
            if (loading || viewSwitching) return;
            viewSwitching = true;
            if (box.Checked) other.Checked = false;
            else chkReduced.Checked = chkMini.Checked = false;
            viewSwitching = false;
            ApplyView();
        }

        // Affichage demandé par les cases (Visible n'est pas fiable avant l'affichage de la fenêtre).
        int WantedView { get { return chkMini != null && chkMini.Checked ? 2 : chkReduced != null && chkReduced.Checked ? 1 : 0; } }
        bool TopRowsHidden { get { return WantedView != 0; } }

        // Passe à l'affichage demandé. Normal : tout. Réduit : à partir de la ligne Synchro, page du WebSDR comprise
        // (la fenêtre perd la hauteur des lignes masquées). Mini : de Synchro à la ligne du volume, page masquée (le WebSDR
        // continue de jouer et de suivre le poste). Chaque affichage retrouve la taille qu'il avait en le quittant.
        void ApplyView()
        {
            int want = WantedView;
            if (want == viewShown || pttView) return;
            // Fenêtre réduite dans la barre des tâches : Windows ignore le redimensionnement, on attend sa réapparition.
            if (WindowState == FormWindowState.Minimized) { compactPending = true; return; }
            compactPending = false;
            if (viewShown == 0)
            {
                // Plein écran : retenu pour y revenir en repassant en affichage normal (au démarrage dans un autre
                // affichage, il vient des réglages enregistrés).
                if (WindowState == FormWindowState.Maximized)
                {
                    wasMaximized = true;
                    WindowState = FormWindowState.Normal;
                }
                normalSize = Size;
                topRowsHeight = rowSync.Top;
            }
            else if (viewShown == 1)
            {
                // Réduit en plein écran : on n'en retient pas la taille (celle-ci ne vaut que hors plein écran).
                if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
                else reducedSize = Size;
            }
            MaximumSize = Size.Empty;
            bool top = want == 0;
            rowConn.Visible = rowParams.Visible = top;
            rowHamlib.Visible = top && cbInterface.SelectedIndex == 4;
            web.Visible = want != 2;
            viewShown = want;
            Log.Write("Affichage : " + (want == 0 ? "normal" : want == 1 ? "réduit" : "mini"));
            if (want == 0)
            {
                MinimumSize = Size.Empty;
                rowNeed = 0;
                Size = normalSize.IsEmpty ? DefaultWindowSize() : normalSize;
                if (wasMaximized) WindowState = FormWindowState.Maximized;
                wasMaximized = false;
                FitOnScreen();
                return;
            }
            if (want == 1)
            {
                Size baseSize = normalSize.IsEmpty ? DefaultWindowSize() : normalSize;
                if (wasMaximized)
                {
                    // Venu du plein écran : le réduit reste en plein écran, toute la page du WebSDR est visible.
                    KeepRowWhole();
                    WindowState = FormWindowState.Maximized;
                    return;
                }
                if (!reducedSize.IsEmpty) Size = reducedSize;
                else Size = new Size(baseSize.Width, Math.Max(300, baseSize.Height - topRowsHeight));
            }
            KeepRowWhole();
            if (want == 2) FitMiniHeight();
            FitOnScreen();
        }

        // Écran où se trouve la fenêtre (zone de travail, sans la barre des tâches). Les tests en donnent une autre.
        Rectangle workAreaOverride;
        Rectangle WorkArea { get { return !workAreaOverride.IsEmpty ? workAreaOverride : Screen.FromControl(this).WorkingArea; } }

        // Affichages réduit et mini : la ligne PTT / fréquences / modes / cases reste sur une seule ligne ; la fenêtre
        // est au moins assez large pour elle (dans la limite de l'écran).
        void KeepRowWhole()
        {
            if (viewShown == 0 || pttView) return;
            PerformLayout();
            // Largeur de la ligne posée sur une seule rangée (largeur proposée sans limite).
            int w = rowDisplay.GetPreferredSize(new Size(100000, 0)).Width + rowDisplay.Margin.Horizontal + 2;
            int need = Math.Min(SizeFromClientSize(new Size(w, 10)).Width, WorkArea.Width);
            // Windows peut plafonner la largeur minimale (taille de l'écran) : on ne la redemande que si elle change.
            if (need != rowNeed || MinimumSize.Width == 0) { rowNeed = need; MinimumSize = new Size(need, 0); }
            if (Width < need) Width = need;
        }
        int rowNeed;

        // La ligne s'élargit (fréquence plus longue, texte agrandi…) : la fenêtre suit, après la mise en page.
        bool rowCheckQueued;
        void OnRowDisplayLayout()
        {
            if (viewShown == 0 || pttView || rowCheckQueued || !IsHandleCreated) return;
            rowCheckQueued = true;
            BeginInvoke((Action)delegate
            {
                rowCheckQueued = false;
                KeepRowWhole();
                if (viewShown == 2) FitMiniHeight();
            });
        }

        // La fenêtre reste entière dans l'écran : bas de fenêtre visible, sans passer sous la barre des tâches.
        void FitOnScreen()
        {
            if (WindowState != FormWindowState.Normal) return;
            Rectangle a = WorkArea;
            int w = Math.Min(Width, a.Width), h = Math.Min(Height, a.Height);
            if (w != Width || h != Height) Size = new Size(w, h);
            Location = new Point(Math.Max(a.Left, Math.Min(Left, a.Right - Width)), Math.Max(a.Top, Math.Min(Top, a.Bottom - Height)));
        }

        // ---------------------------------------------------------------- Affichage ptt

        // Fenêtre réduite au bouton PTT et à sa case ; tout ce qui précède (affichage, taille, position, plein écran,
        // tailles minimale et maximale) est gardé tel quel et rétabli en décochant.
        bool pttView;
        FlowLayoutPanel pttPanel;
        Rectangle pttPrevBounds;
        FormWindowState pttPrevState;
        Size pttPrevMin, pttPrevMax;
        Control[] pttMoved;
        Control[] pttParents;
        int[] pttIndexes;

        void SetPttView(bool on)
        {
            if (on == pttView) return;
            SuspendLayout();
            if (on)
            {
                pttPrevState = WindowState;
                pttPrevBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                pttPrevMin = MinimumSize;
                pttPrevMax = MaximumSize;
                if (pttPanel == null)
                {
                    pttPanel = new FlowLayoutPanel();
                    pttPanel.AutoSize = true;
                    pttPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    pttPanel.WrapContents = false;
                    pttPanel.Padding = new Padding(4);
                    pttPanel.Location = Point.Empty;
                    Controls.Add(pttPanel);
                }
                pttView = true;
                // Le bouton PTT et la case quittent leur ligne ; leur place est notée pour les y remettre.
                pttMoved = new Control[] { btnPtt, chkPttView };
                pttParents = new Control[pttMoved.Length];
                pttIndexes = new int[pttMoved.Length];
                for (int i = 0; i < pttMoved.Length; i++)
                {
                    pttParents[i] = pttMoved[i].Parent;
                    pttIndexes[i] = pttParents[i].Controls.GetChildIndex(pttMoved[i]);
                }
                foreach (Control c in pttMoved) pttPanel.Controls.Add(c);
                chkPttView.Margin = new Padding(8, 14, 4, 0);
                mainTable.Visible = strip.Visible = false;
                pttPanel.Visible = true;
                MinimumSize = MaximumSize = Size.Empty;
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                ResumeLayout(true);
                ClientSize = pttPanel.GetPreferredSize(Size.Empty);
                Location = pttPrevBounds.Location;
                FitOnScreen();
                Log.Write("Affichage : ptt");
                return;
            }
            pttView = false;
            pttPanel.Visible = false;
            chkPttView.Margin = new Padding(8, 2, 2, 0);
            for (int i = pttMoved.Length - 1; i >= 0; i--)
            {
                pttParents[i].Controls.Add(pttMoved[i]);
                pttParents[i].Controls.SetChildIndex(pttMoved[i], pttIndexes[i]);
            }
            mainTable.Visible = strip.Visible = true;
            WindowState = FormWindowState.Normal;
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            Bounds = pttPrevBounds;
            MinimumSize = pttPrevMin;
            MaximumSize = pttPrevMax;
            ResumeLayout(true);
            if (pttPrevState == FormWindowState.Maximized) WindowState = FormWindowState.Maximized;
            Log.Write("Affichage : retour de l'affichage ptt");
        }

        // Affichage mini : hauteur fixe, juste ce qu'il faut pour les lignes affichées.
        void FitMiniHeight()
        {
            if (pttView) return;
            MaximumSize = Size.Empty;
            PerformLayout();
            int h = SizeFromClientSize(new Size(ClientSize.Width, rowUrl.Bottom + strip.Height + 4)).Height;
            MaximumSize = new Size(10000, h);
            Height = h;
        }
        // ---------------------------------------------------------------- Coupure du son en émission

        // Bouton PTT : bascule émission/réception, comme le MOX de HRD. Le son est coupé tout de suite,
        // sans attendre que le poste confirme l'émission.
        // Clic droit sur PTT : délai de sécurité au bout duquel une émission lancée par le bouton est coupée.
        static readonly int[] PttTimeouts = { 0, 30, 60, 120, 180, 300, 600, 900, 1200, 1500 };

        ContextMenuStrip BuildPttMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Opening += delegate
            {
                m.Items.Clear();
                ToolStripMenuItem title = new ToolStripMenuItem(Lang.T("Délai de sécurité TX (bouton PTT)", "TX safety timeout (PTT button)"));
                title.Enabled = false;
                m.Items.Add(title);
                m.Items.Add(new ToolStripSeparator());
                foreach (int s in PttTimeouts)
                {
                    int sec = s;
                    ToolStripMenuItem it = new ToolStripMenuItem(TimeoutLabel(sec));
                    it.Checked = settings.PttTimeoutSec == sec;
                    it.Click += delegate
                    {
                        settings.PttTimeoutSec = sec;
                        Log.Write("Délai de sécurité TX : " + TimeoutLabel(sec));
                        UpdatePttButton();
                    };
                    m.Items.Add(it);
                }
            };
            m.Items.Add("-");   // contenu construit à l'ouverture (langue et choix en cours)
            return m;
        }

        static string TimeoutLabel(int sec)
        {
            if (sec == 0) return Lang.T("Désactivé", "Off");
            return sec < 60 ? sec + " s" : (sec / 60) + " min";
        }

        // Appelé chaque seconde : compte à rebours sur le bouton et coupure à l'échéance.
        void CheckPttTimeout()
        {
            if (!pttActive || settings.PttTimeoutSec <= 0) return;
            if ((DateTime.UtcNow - pttSince).TotalSeconds >= settings.PttTimeoutSec)
            {
                SetPttButton(false);
                string msg = Lang.F("Émission coupée automatiquement après {0} (délai de sécurité, clic droit sur PTT pour le régler)",
                    "Transmission stopped automatically after {0} (safety timeout, right-click PTT to change it)", TimeoutLabel(settings.PttTimeoutSec));
                ShowStatus(msg);
                Log.Write(msg);
                return;
            }
            UpdatePttButton();
        }

        void SetPttButton(bool on)
        {
            if (on && (worker == null || !rigOnline)) { ShowStatus(Lang.T("PTT : connectez d'abord le poste", "PTT: connect the rig first")); return; }
            if (on) pttSince = DateTime.UtcNow;
            if (on && scanning) StopScan(Lang.T("émission", "transmitting"));
            pttActive = on;
            if (worker != null) worker.SetPtt(on);
            UpdatePttButton();
            UpdateDisplays();
            ApplyMute();
        }

        void UpdatePttButton()
        {
            // Au repos : jaune clair sur fond sombre, bien visible sur tous les écrans et dans les deux thèmes.
            string text = "PTT";
            if (pttActive)
            {
                text = "● TX";
                if (settings.PttTimeoutSec > 0)
                {
                    int left = Math.Max(0, settings.PttTimeoutSec - (int)(DateTime.UtcNow - pttSince).TotalSeconds);
                    text += " " + (left / 60) + ":" + (left % 60).ToString("00");
                }
            }
            if (btnPtt.Text != text) btnPtt.Text = text;
            btnPtt.BackColor = pttActive ? Color.FromArgb(210, 0, 0) : Color.FromArgb(55, 55, 60);
            btnPtt.ForeColor = pttActive ? Color.White : Color.FromArgb(255, 235, 110);
            btnPtt.FlatAppearance.BorderColor = Color.FromArgb(180, 0, 0);
            // Couleur choisie au clic droit : texte noir sur fond clair ; sur fond sombre, jaune au repos, blanc en émission.
            int custom = ColorOf(pttActive ? "PttTx" : "PttRx");
            if (custom >= 0)
            {
                Color back = Opaque(custom);
                btnPtt.BackColor = back;
                btnPtt.ForeColor = IsLight(back) ? Color.Black : pttActive ? Color.White : Color.FromArgb(255, 235, 110);
                btnPtt.FlatAppearance.BorderColor = Outline(back);
            }
            // Toujours actif : désactivé, Windows grise le texte (illisible). Sans poste, un clic affiche un message.
        }

        bool Transmitting { get { return rigTx || pttActive; } }

        void OnRigTx(int tx)
        {
            if (worker == null) return;
            rigTx = tx == 1;
            UpdateDisplays();
            ApplyMute();
        }

        // Coupe tout de suite au passage en émission ; rétablit après un délai, car le son du WebSDR
        // arrive avec du retard et contiendrait encore la fin de notre propre émission.
        void ApplyMute()
        {
            if (chkMute.Checked && Transmitting)
            {
                unmuteTimer.Stop();
                SetMuted(true);
            }
            else if (!chkMute.Checked || numUnmute.Value == 0)
            {
                unmuteTimer.Stop();
                SetMuted(false);
            }
            else if (web.CoreWebView2 != null && web.CoreWebView2.IsMuted && !unmuteTimer.Enabled)
            {
                unmuteTimer.Interval = (int)numUnmute.Value;
                unmuteTimer.Start();
            }
        }

        void SetMuted(bool muted)
        {
            if (web.CoreWebView2 == null || web.CoreWebView2.IsMuted == muted) return;
            web.CoreWebView2.IsMuted = muted;
            Log.Write(muted ? "Son du WebSDR coupé (émission)" : "Son du WebSDR rétabli");
        }

        // ---------------------------------------------------------------- Molette sur les fréquences

        // Intercepte la molette pour toute l'application : elle agit dès que la souris survole une fréquence,
        // sans avoir à cliquer dessus.
        class WheelFilter : IMessageFilter
        {
            const int WM_MOUSEWHEEL = 0x020A;
            readonly MainForm form;
            public WheelFilter(MainForm form) { this.form = form; }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_MOUSEWHEEL) return false;
                // Position écran du curseur au moment du cran, fournie par le message.
                long lp = (long)m.LParam;
                Point p = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                bool overRig = form.IsOver(form.lblRig, p), overSdr = form.IsOver(form.lblSdr, p);
                if (!overRig && !overSdr) return false;
                form.TuneByWheel((short)(((long)m.WParam >> 16) & 0xFFFF), overSdr);
                return true;
            }
        }

        bool IsOver(Control c, Point screen)
        {
            return c.Visible && c.RectangleToScreen(c.ClientRectangle).Contains(screen);
        }

        // Accélération (pas < 1 kHz seulement) selon le temps entre deux crans de molette.
        static long Accelerate(long step, double msPerNotch)
        {
            if (step >= 1000) return step;
            if (msPerNotch < 30) return step * 10;
            if (msPerNotch < 60) return step * 5;
            if (msPerNotch < 100) return step * 2;
            return step;
        }

        bool WheelActive { get { return DateTime.UtcNow < wheelActiveUntil; } }

        // Ce que bouge la molette dépend du sens de synchronisation :
        //   Bidirectionnelle : poste et WebSDR ensemble, quelle que soit la fréquence survolée ;
        //   Poste → WebSDR   : sur POSTE, les deux (le WebSDR suit) ; sur WEBSDR, le WebSDR seul ;
        //   WebSDR → Poste   : sur WEBSDR, les deux (le poste suit) ; sur POSTE, le poste seul ;
        //   Désactivée       : chaque fréquence ne bouge que la sienne.
        void TuneByWheel(int delta, bool overSdr)
        {
            wheelRemainder += delta;
            int notches = wheelRemainder / 120;
            if (notches == 0) return;
            wheelRemainder -= notches * 120;

            DateTime now = DateTime.UtcNow;
            double msPerNotch = (now - wheelLast).TotalMilliseconds / Math.Abs(notches);
            wheelLast = now;
            long step = Accelerate(Steps[Math.Max(0, cbStep.SelectedIndex)], msPerNotch);

            bool sdrOnly = overSdr && (Dir == Direction.RigToSdr || Dir == Direction.Off);
            bool rigOnly = !overSdr && (Dir == Direction.SdrToRig || Dir == Direction.Off);
            if (sdrOnly)
            {
                // Pendant qu'on tourne, on part de la dernière cible plutôt que de la fréquence relue, qui a du retard.
                long baseSdr = SdrWheelActive && sdrWheelTarget > 0 ? sdrWheelTarget : sdrF;
                if (baseSdr <= 0 || sdrKind == "") return;
                TuneSdrOnly((long)Math.Round((double)baseSdr / step) * step + notches * step);
                return;
            }
            long baseF = WheelActive && wheelTarget > 0 ? wheelTarget : CurrentFreq();
            if (baseF <= 0) return;
            long target = (long)Math.Round((double)baseF / step) * step + notches * step;
            if (rigOnly) TuneRigOnly(target); else TuneBoth(target);
        }

        long sdrWheelTarget;
        DateTime sdrWheelUntil;
        bool SdrWheelActive { get { return DateTime.UtcNow < sdrWheelUntil; } }

        void TuneSdrOnly(long sdrHz)
        {
            if (sdrHz <= 0) return;
            sdrWheelTarget = sdrHz;
            sdrWheelUntil = DateTime.UtcNow.AddMilliseconds(1000);
            SetSdr(sdrHz, "");
            UpdateDisplays();
        }

        void TuneRigOnly(long rigHz)
        {
            if (rigHz <= 0 || worker == null || !rigOnline) return;
            DateTime now = DateTime.UtcNow;
            wheelTarget = rigHz;
            wheelActiveUntil = now.AddMilliseconds(1000);
            rigOnlyWheel = true;
            ignoreRigUntil = now.AddMilliseconds(EchoIgnoreMs);
            worker.SetFreq(rigHz);
            UpdateDisplays();
        }

        bool rigOnlyWheel;   // la cible de molette en cours ne concerne que le poste (l'affichage WebSDR reste réel)

        // Fréquence actuelle côté poste (ou déduite du WebSDR si aucun poste n'est connecté), 0 si inconnue.
        long CurrentFreq()
        {
            if (rigF > 0 && worker != null) return rigF;
            return sdrKind != "" ? sdrF - Offset : 0;
        }

        // Règle le poste ET le WebSDR sur la même fréquence (fréquence poste ; le décalage s'applique au WebSDR).
        void TuneBoth(long rigHz)
        {
            if (rigHz <= 0) return;
            DateTime now = DateTime.UtcNow;
            wheelTarget = rigHz;
            wheelActiveUntil = now.AddMilliseconds(1000);
            rigOnlyWheel = false;
            if (worker != null && rigOnline)
            {
                ignoreRigUntil = now.AddMilliseconds(EchoIgnoreMs);
                worker.SetFreq(rigHz);
            }
            SetSdr(rigHz + Offset, "");
            UpdateDisplays();
        }

        // Change le mode du poste ET du WebSDR (boutons USB/LSB/AM/FM).
        void SetModeBoth(string mode)
        {
            if (worker != null && rigOnline)
            {
                ignoreRigUntil = DateTime.UtcNow.AddMilliseconds(EchoIgnoreMs);
                worker.SetMode(mode);
            }
            SetSdr(0, Modes.ToSdr(mode));
        }

        // hz = 0 : fréquence inchangée ; sdrMode = "" : mode inchangé.
        void SetSdr(long hz, string sdrMode)
        {
            if (sdrKind == "" || web.CoreWebView2 == null) return;
            ignoreSdrUntil = DateTime.UtcNow.AddMilliseconds(EchoIgnoreMs);
            web.CoreWebView2.ExecuteScriptAsync(string.Format(CultureInfo.InvariantCulture,
                "window.__sdrlink && window.__sdrlink.set({0}, '{1}');", hz, sdrMode));
        }

        // ---------------------------------------------------------------- Saisie de fréquence et historique

        const int MaxHistory = 100;

        // Accepte "7074", "7074.5", "7074,5" (kHz) ou "14.074.000" / "14074000" (Hz).
        static long ParseFreq(string text)
        {
            string s = (text ?? "").Trim().Replace(" ", "");
            if (s.Length == 0) return 0;
            if (s.Split('.').Length > 2) s = s.Replace(".", "");          // 14.074.000 : séparateurs de milliers
            double v;
            if (!double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v <= 0) return 0;
            return v >= 1000000 ? (long)Math.Round(v) : (long)Math.Round(v * 1000);
        }

        static string KHz(long hz)
        {
            return (hz / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        }

        void TuneFromEntry()
        {
            long hz = ParseFreq(cbFreq.Text);
            if (hz <= 0)
            {
                ShowStatus(Lang.T("Fréquence invalide : tapez une valeur en kHz, par ex. 7074 ou 14074.5",
                                     "Invalid frequency: type a value in kHz, e.g. 7074 or 14074.5"));
                return;
            }
            TuneBoth(hz);
            AddToHistory(hz);
        }

        // Ajoute en tête de liste (la plus récente en premier), sans doublon, 100 valeurs au plus.
        void AddToHistory(long hz)
        {
            string k = KHz(hz);
            cbFreq.BeginUpdate();
            cbFreq.Items.Remove(k);
            cbFreq.Items.Insert(0, k);
            while (cbFreq.Items.Count > MaxHistory) cbFreq.Items.RemoveAt(cbFreq.Items.Count - 1);
            cbFreq.EndUpdate();
            cbFreq.Text = k;
        }

        void BuildFreqMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem removeOne = new ToolStripMenuItem();
            ToolStripMenuItem removeAll = new ToolStripMenuItem();
            menu.Items.Add(removeOne);
            menu.Items.Add(removeAll);
            menu.Opening += delegate
            {
                string k = cbFreq.Text.Trim();
                bool present = cbFreq.Items.Contains(k);
                removeOne.Text = present
                    ? Lang.F("Supprimer « {0} » de la liste", "Remove \"{0}\" from the list", k)
                    : Lang.T("Supprimer cette valeur (choisissez-la d'abord dans la liste)", "Remove this value (pick it in the list first)");
                removeOne.Enabled = present;
                removeAll.Text = Lang.F("Tout supprimer ({0} valeurs)", "Remove all ({0} values)", cbFreq.Items.Count);
                removeAll.Enabled = cbFreq.Items.Count > 0;
            };
            removeOne.Click += delegate { cbFreq.Items.Remove(cbFreq.Text.Trim()); cbFreq.Text = ""; };
            removeAll.Click += delegate
            {
                if (MessageBox.Show(Lang.F("Supprimer les {0} fréquences de la liste ?",
                                           "Remove all {0} frequencies from the list?", cbFreq.Items.Count), "SDRLink",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    cbFreq.Items.Clear();
                    cbFreq.Text = "";
                }
            };
            cbFreq.ContextMenuStrip = menu;
        }

        // ---------------------------------------------------------------- Synchronisation

        Direction Dir { get { return (Direction)Math.Max(0, cbDirection.SelectedIndex); } }
        bool RigToSdr { get { return Dir == Direction.Both || Dir == Direction.RigToSdr; } }
        bool SdrToRig { get { return Dir == Direction.Both || Dir == Direction.SdrToRig; } }
        long Offset { get { return (long)numOffset.Value; } }

        void OnRigState(long f, string m)
        {
            if (worker == null) return;
            rigF = f;
            if (m != null) rigM = m;
            UpdateDisplays();

            string fam = Modes.ToSdr(rigM);
            bool changed = Math.Abs(f - lastRigF) > Tolerance || fam != lastRigFam;
            // Première relecture du poste (connexion) : le WebSDR se cale sur lui, sans retour vers le poste un moment.
            if (lastRigF == 0) syncSettleUntil = DateTime.UtcNow.AddMilliseconds(SettleMs);
            lastRigF = f;
            lastRigFam = fam;
            // Pendant qu'on vient de commander le poste, on n'en renvoie pas l'écho vers le WebSDR.
            if (DateTime.UtcNow < ignoreRigUntil) return;
            if (changed && RigToSdr) PushToSdr();
        }

        void OnSdrState(string kind, long f, string m)
        {
            bool first = sdrKind == "";
            if (first)
            {
                Log.Write("WebSDR détecté : " + kind + " " + f + " Hz " + m);
                ApplyVolume();
                if (tapShown) SetPageTap(true);
                SetPageNotch();
            }
            sdrKind = kind;
            sdrF = f;
            sdrM = m;
            UpdateDisplays();

            string fam = Modes.SdrFamily(m);
            bool freqChanged = Math.Abs(f - lastSdrF) > Tolerance;
            bool modeChanged = fam != lastSdrFam;
            lastSdrF = f;
            lastSdrFam = fam;

            if (first)
            {
                // Page qui vient de se charger : le poste impose sa fréquence s'il est connecté. Au démarrage de
                // SDRLink, le poste ne change jamais (en "WebSDR → Radio" non plus) : seul le WebSDR suit.
                syncSettleUntil = DateTime.UtcNow.AddMilliseconds(SettleMs);
                if (RigToSdr && rigOnline && rigF > 0) PushToSdr();
                else if (Dir == Direction.SdrToRig && !firstPage) PushToRig(true, true);
                firstPage = false;
                return;
            }
            // Page en cours de mise en place (elle annonce parfois encore son ancienne fréquence après notre réglage) :
            // rien n'est envoyé au poste ; le WebSDR est recalé sur le poste si la synchro va dans ce sens.
            if (DateTime.UtcNow < syncSettleUntil)
            {
                if (RigToSdr && rigOnline && rigF > 0) PushToSdr();
                return;
            }
            if (DateTime.UtcNow < ignoreSdrUntil) return;
            if ((freqChanged || modeChanged) && SdrToRig) PushToRig(freqChanged, modeChanged);
        }

        // Le poste fait foi : en "Bidirectionnelle" ou "Poste → WebSDR", un WebSDR resté décalé de plus de 3 s
        // (réglage perdu, page lente, clic dans la cascade en mode "Poste → WebSDR") est recalé sur le poste.
        void CheckSync()
        {
            // Seulement en "Bidirectionnelle" : en "Poste → WebSDR", on peut maintenant régler le WebSDR seul (molette).
            bool diverged = worker != null && rigOnline && Dir == Direction.Both && sdrKind != "" && rigF > 0 && !WheelActive && !SdrWheelActive
                && DateTime.UtcNow > ignoreSdrUntil && DateTime.UtcNow > ignoreRigUntil
                && Math.Abs(rigF + Offset - sdrF) > Tolerance;
            if (!diverged) { divergedSince = DateTime.MinValue; return; }
            if (divergedSince == DateTime.MinValue) { divergedSince = DateTime.UtcNow; return; }
            if ((DateTime.UtcNow - divergedSince).TotalSeconds < 3) return;
            Log.Write("Synchro : WebSDR recalé sur le poste (" + FormatFreq(sdrF) + " -> " + FormatFreq(rigF + Offset) + ")");
            divergedSince = DateTime.MinValue;
            PushToSdr();
        }

        void PushToSdr()
        {
            if (sdrKind == "" || rigF <= 0 || web.CoreWebView2 == null) return;
            long target = rigF + Offset;
            string fam = chkMode.Checked ? Modes.ToSdr(rigM) : "";
            bool needF = Math.Abs(target - sdrF) > Tolerance;
            bool needM = fam != "" && fam != Modes.SdrFamily(sdrM);
            if (!needF && !needM) return;
            ignoreSdrUntil = DateTime.UtcNow.AddMilliseconds(EchoIgnoreMs);
            web.CoreWebView2.ExecuteScriptAsync(string.Format(CultureInfo.InvariantCulture,
                "window.__sdrlink && window.__sdrlink.set({0}, '{1}');", needF ? target : 0, needM ? fam : ""));
        }

        void PushToRig(bool freq, bool mode)
        {
            if (worker == null || !rigOnline || sdrF <= 0) return;
            ignoreRigUntil = DateTime.UtcNow.AddMilliseconds(EchoIgnoreMs);
            if (mode && chkMode.Checked)
            {
                string fam = Modes.SdrFamily(sdrM);
                // On ne touche pas au mode du poste s'il est déjà de la même famille (ex. DATA-U contre USB).
                if (fam != "" && fam != Modes.ToSdr(rigM)) worker.SetMode(Modes.FromSdrFamily(fam));
            }
            if (freq)
            {
                long target = sdrF - Offset;
                if (target > 0 && Math.Abs(target - rigF) > Tolerance) worker.SetFreq(target);
            }
        }

        // Ne touche aux libellés que si leur contenu change, pour éviter de redessiner la fenêtre pour rien.
        void UpdateDisplays()
        {
            // Pendant la molette, on affiche la cible : les relectures arrivent avec un temps de retard.
            bool wheel = WheelActive;
            long shownRig = wheel ? wheelTarget : rigF;
            long shownSdr = SdrWheelActive ? sdrWheelTarget : wheel && !rigOnlyWheel ? wheelTarget + Offset : sdrF;
            SetFreqText(lblRig, Lang.T("RADIO ", "RIG ") + (worker != null && rigF > 0 ? FormatFreq(shownRig) + " " + Modes.Display(rigM) : "---"));
            // L'émission est signalée par la couleur (et le bouton PTT) : la largeur du texte ne change pas.
            Color c = RigFore();
            if (lblRig.ForeColor != c) lblRig.ForeColor = c;
            // Mode WebSDR en forme courte (USB, LSB, CW, AM, FM) : le mode exact est montré par les boutons.
            string fam = Modes.SdrFamily(sdrM);
            string sdrMode = fam != "" ? fam.ToUpperInvariant() : (sdrM ?? "").ToUpperInvariant();
            SetFreqText(lblSdr, "WEBSDR " + (sdrKind != "" ? FormatFreq(shownSdr) + " " + sdrMode : "---"));

            // Bouton de mode en surbrillance : mode du poste s'il est connecté, sinon celui du WebSDR.
            string active = ActiveModeKey(fam);
            if (active != shownModeFamily)
            {
                shownModeFamily = active;
                string activeKey = active.Split('|')[0];
                for (int i = 0; i < modeButtons.Length; i++)
                {
                    bool on = ModeKey(ModeButtonModes[i]) == activeKey;
                    modeButtons[i].BackColor = on ? Color.FromArgb(0, 120, 60) : Theme.IdleButton;
                    modeButtons[i].ForeColor = on ? Color.White : Theme.ModeIdleText;
                    modeButtons[i].FlatAppearance.BorderColor = Color.FromArgb(0, 150, 75);
                    int custom = ColorOf(on ? "ModeOn" : "ModeOff");
                    if (custom >= 0) PaintCustom(modeButtons[i], custom);
                    modeButtons[i].Text = ModeButtonText(ModeButtonModes[i], on);
                }
            }
        }

        // Famille d'un mode pour les boutons : usb, lsb, am, fm, cw ou digi.
        static string ModeKey(string mode)
        {
            if (mode == Modes.DATAU || mode == Modes.DATAL) return "digi";
            return Modes.ToSdr(mode);
        }

        string ActiveModeKey(string sdrFamily)
        {
            if (worker != null && rigOnline && rigM != null)
            {
                // Le poste dit lui-même de quel côté il est (CW/CW-R, DATA-U/DATA-L), une fois la commande arrivée.
                bool settled = DateTime.UtcNow > modeClickUntil;
                if (!settled) return ModeKey(clickedMode) + "|" + digiMode + cwMode;
                if (settled && (rigM == Modes.DATAU || rigM == Modes.DATAL)) digiMode = rigM;
                if (settled && (rigM == Modes.CW || rigM == Modes.CWR)) cwMode = rigM;
                // Postes qui ne signalent pas le mode DATA (Icom...) : DIGI reste allumé tant que la bande latérale correspond.
                if (digiActive && ModeKey(rigM) != "digi" && settled)
                {
                    if (Modes.ToSdr(rigM) == Modes.ToSdr(digiMode)) return "digi|" + digiMode + cwMode;
                    digiActive = false;
                }
                return ModeKey(rigM) + "|" + digiMode + cwMode;
            }
            if (digiActive && (sdrFamily == "usb" || sdrFamily == "lsb")) return "digi|" + digiMode + cwMode;
            return sdrFamily + "|" + digiMode + cwMode;
        }

        DateTime modeClickUntil;   // jusqu'à cette heure, le mode relu du poste peut encore être l'ancien
        string clickedMode;

        string ModeButtonText(string mode, bool on)
        {
            if (mode == Modes.DATAU) return on ? (digiMode == Modes.DATAL ? "DIGI L" : "DIGI U") : "DIGI";
            if (mode == Modes.CW) return on ? (cwMode == Modes.CWR ? "CW L" : "CW U") : "CW";
            return Modes.Display(mode);
        }

        // Clic sur un bouton de mode. DIGI et CW déjà actifs : bascule bande latérale haute / basse.
        void OnModeButton(string mode)
        {
            bool wasOn = shownModeFamily != null && shownModeFamily.StartsWith(ModeKey(mode) + "|");
            if (mode == Modes.DATAU)
            {
                if (wasOn) digiMode = digiMode == Modes.DATAU ? Modes.DATAL : Modes.DATAU;
                mode = digiMode;
            }
            else if (mode == Modes.CW)
            {
                if (wasOn) cwMode = cwMode == Modes.CW ? Modes.CWR : Modes.CW;
                mode = cwMode;
            }
            digiActive = ModeKey(mode) == "digi";
            modeClickUntil = DateTime.UtcNow.AddSeconds(2);
            clickedMode = mode;
            SetModeBoth(mode);
            shownModeFamily = null;
            UpdateDisplays();
        }

        static void SetFreqText(Label l, string text)
        {
            if (l.Text == text) return;
            l.Text = text;
            FitWidth(l, text, false);
        }

        static string FormatFreq(long hz)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1:000}.{2:000}", hz / 1000000, (hz / 1000) % 1000, hz % 1000);
        }

        // ---------------------------------------------------------------- WebSDR

        async void InitWebView()
        {
            try
            {
                string dataDir = Environment.GetEnvironmentVariable("SDRLINK_DATA") != null
                    ? Path.Combine(Log.DataDir, "WebView2")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SDRLink", "WebView2");
                CoreWebView2EnvironmentOptions opts = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, dataDir, opts);
                await web.EnsureCoreWebView2Async(env);
                CoreWebView2 core = web.CoreWebView2;
                await core.AddScriptToExecuteOnDocumentCreatedAsync(WebSdrBridge.Script);
                core.WebMessageReceived += OnWebMessage;
                core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e)
                {
                    e.Handled = true;
                    core.Navigate(e.Uri);
                };
                core.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (!e.IsRedirected) { sdrKind = ""; lastSdrF = 0; lastSdrFam = null; UpdateDisplays(); }
                };
                core.SourceChanged += delegate
                {
                    if (!cbUrl.Focused) cbUrl.Text = core.Source;
                    OnPageChanged(core.Source);
                };
                // Dès que la page est chargée : son volume (les gains audio créés plus tard le reprennent aussi).
                core.DOMContentLoaded += delegate { ApplyVolume(); if (tapShown) SetPageTap(true); SetPageNotch(); };
                ApplyCable();
                UpdateCableButton();
                Navigate(settings.LastUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("Impossible de démarrer WebView2 (Edge) :\n", "Cannot start WebView2 (Edge):\n") + ex.Message,
                    "SDRLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string msg;
            try { msg = e.TryGetWebMessageAsString(); } catch { return; }
            if (msg != null && msg.StartsWith("pcm|")) { OnPcm(msg); return; }
            string[] p = (msg ?? "").Split('|');
            long f;
            if (p.Length < 3 || !long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out f)) return;
            OnSdrState(p[0], f, p[2]);
        }

        void Navigate(string url)
        {
            url = (url ?? "").Trim();
            if (url == "" || web.CoreWebView2 == null) return;
            if (!url.Contains("://")) url = "http://" + url;
            try { web.CoreWebView2.Navigate(url); }
            catch (ArgumentException) { ShowStatus(Lang.T("Adresse invalide : ", "Invalid address: ") + url); }
        }

        // Le texte de la barre peut être une adresse, ou le nom d'un favori choisi dans la liste.
        void NavigateFromBar()
        {
            string text = cbUrl.Text.Trim();
            Favorite f = settings.Favorites.Find(delegate(Favorite x) { return x.Name == text; });
            Navigate(f != null ? f.Url : text);
        }

        // ---------------------------------------------------------------- Câble SDRLINK (son du WebSDR vers un câble audio virtuel)

        // Le son du WebSDR est recopié en permanence (tous modes) vers la sortie choisie, typiquement l'entrée
        // d'un câble virtuel (VB-CABLE) ; l'autre bout du câble, nommé SDRLINK, sert d'entrée audio à WSJT-X & co.
        // La recopie se fait avant le volume et les coupures : Mute WebSDR ne la coupe pas.
        void ApplyCable()
        {
            string dev = settings.CableEnabled ? CableDeviceName() : null;
            if (cableSink != null && cableSink.DeviceName != dev) { cableSink.Dispose(); cableSink = null; }
            if (dev != null && cableSink == null)
            {
                cableSink = new AudioSink(dev);
                shownCableError = null;
                Log.Write("Câble SDRLINK : sortie « " + dev + " »");
            }
            string missing = Lang.T("Câble SDRLINK : aucun câble audio virtuel trouvé. Installez VB-CABLE (gratuit) : bouton « Câble SDRLINK ».",
                                    "SDRLINK cable: no virtual audio cable found. Install VB-CABLE (free): \"SDRLINK cable\" button.");
            if (settings.CableEnabled && dev == null) ShowStatus(missing);
            else if (status.Text == missing) ShowStatus("");   // câble désactivé ou trouvé : le message ne vaut plus
            SetPageTap(cableSink != null);
        }

        void SetPageTap(bool on)
        {
            tapShown = on;
            if (web.CoreWebView2 != null)
                web.CoreWebView2.ExecuteScriptAsync("window.__sdrlink && window.__sdrlink.setTap && window.__sdrlink.setTap(" + (on ? "true" : "false") + ");");
        }

        string CableDeviceName()
        {
            List<string> devs = AudioSink.OutputDevices();
            if (settings.CableDevice != "" && devs.Contains(settings.CableDevice)) return settings.CableDevice;
            return AudioSink.DefaultDevice();
        }

        // Bouton "Câble SDRLINK" : sortie utilisée, nom SDRLINK du bout enregistrement, installation de VB-CABLE.
        ContextMenuStrip BuildCableMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                e.Cancel = false;
                m.Items.Clear();
                ToolStripMenuItem title = new ToolStripMenuItem(Lang.T("Sortie recevant le son du WebSDR :", "Output receiving the WebSDR audio:"));
                title.Enabled = false;
                m.Items.Add(title);
                ToolStripMenuItem off = new ToolStripMenuItem(Lang.T("Désactivé", "Off"));
                off.Checked = !settings.CableEnabled;
                off.Click += delegate { settings.CableEnabled = false; ApplyCable(); UpdateCableButton(); };
                m.Items.Add(off);
                string current = settings.CableEnabled ? CableDeviceName() : null;
                foreach (string d in AudioSink.OutputDevices())
                {
                    string dev = d;
                    ToolStripMenuItem it = new ToolStripMenuItem(dev);
                    it.Checked = dev == current;
                    it.Click += delegate
                    {
                        if (!AudioSink.LooksVirtual(dev) && MessageBox.Show(this, Lang.F(
                                "« {0} » ne semble pas être un câble virtuel : le son du WebSDR y sera joué en permanence " +
                                "(et émis, s'il s'agit de la carte son d'émission d'un poste).\n\nUtiliser quand même cette sortie ?",
                                "\"{0}\" does not look like a virtual cable: the WebSDR audio will be played there all the time " +
                                "(and transmitted, if it is a radio's transmit sound card).\n\nUse this output anyway?", dev),
                                "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                        settings.CableDevice = dev;
                        settings.CableEnabled = true;
                        ApplyCable();
                        UpdateCableButton();
                    };
                    m.Items.Add(it);
                }
                m.Items.Add(new ToolStripSeparator());
                AudioEndpoints.Endpoint cap = null;
                try { cap = AudioEndpoints.CaptureOf(current); } catch (Exception ex) { Log.Write("Câble SDRLINK : " + ex.Message); }
                ToolStripMenuItem name = new ToolStripMenuItem(cap == null
                    ? Lang.T("Nommer « SDRLINK » l'autre bout du câble (choisir d'abord un câble virtuel)", "Name the other end of the cable \"SDRLINK\" (pick a virtual cable first)")
                    : Lang.F("Nommer « SDRLINK » l'autre bout du câble (« {0} »)…", "Name the other end of the cable \"SDRLINK\" (\"{0}\")…", cap.FullName));
                name.Enabled = cap != null && cap.Name != CableName;
                AudioEndpoints.Endpoint target = cap;
                name.Click += delegate { NameCableEnd(target); };
                m.Items.Add(name);
                ToolStripMenuItem get = new ToolStripMenuItem(Lang.T("Télécharger VB-CABLE (gratuit, vb-audio.com)…", "Download VB-CABLE (free, vb-audio.com)…"));
                get.Click += delegate
                {
                    try { System.Diagnostics.Process.Start("https://vb-audio.com/Cable/"); } catch { }
                };
                m.Items.Add(get);
            };
            m.Items.Add("-");   // contenu construit à l'ouverture (liste des sorties à jour)
            return m;
        }

        const string CableName = "SDRLINK";

        // Renomme le bout "enregistrement" du câble, comme le panneau Son de Windows (droits d'administrateur si besoin).
        void NameCableEnd(AudioEndpoints.Endpoint cap)
        {
            // Câble déjà renommé par l'utilisateur : il sert sans doute déjà à autre chose.
            if (!(cap.Name ?? "").StartsWith("CABLE", StringComparison.OrdinalIgnoreCase) && MessageBox.Show(this, Lang.F(
                    "Ce câble a déjà été renommé « {0} » : il sert peut-être déjà à un autre logiciel (VoiceMeeter…). " +
                    "Le renommer en « {1} » obligera à le choisir à nouveau dans ces logiciels.\n\nContinuer ?",
                    "This cable was already renamed \"{0}\": it may already be used by other software (VoiceMeeter…). " +
                    "Renaming it \"{1}\" means selecting it again in that software.\n\nContinue?", cap.Name, CableName),
                    "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (MessageBox.Show(this, Lang.F(
                    "Le périphérique d'enregistrement « {0} » va être renommé « {1} ».\n\n" +
                    "Il apparaîtra sous ce nom dans les entrées audio de WSJT-X, JTDX, fldigi… (à redémarrer). " +
                    "Windows peut demander l'autorisation d'administrateur.\n\nContinuer ?",
                    "The recording device \"{0}\" will be renamed \"{1}\".\n\n" +
                    "It will show under this name in the audio inputs of WSJT-X, JTDX, fldigi… (restart them). " +
                    "Windows may ask for administrator permission.\n\nContinue?", cap.FullName, CableName),
                    "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int hr;
            try { hr = AudioEndpoints.SetName(cap.Id, CableName); }
            catch (Exception ex) { hr = Marshal.GetHRForException(ex); }
            if (hr == AudioEndpoints.E_ACCESSDENIED)
            {
                try
                {
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath,
                        "--name-endpoint \"" + cap.Id + "\" " + CableName);
                    psi.Verb = "runas";
                    psi.UseShellExecute = true;
                    using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                    {
                        p.WaitForExit(30000);
                        hr = p.HasExited ? p.ExitCode : -1;
                    }
                }
                catch (System.ComponentModel.Win32Exception) { hr = AudioEndpoints.E_ACCESSDENIED; }   // autorisation refusée
            }
            Log.Write("Câble SDRLINK : renommage de « " + cap.FullName + " » -> " + (hr == 0 ? "OK" : "erreur 0x" + hr.ToString("X8")));
            if (hr == 0)
                MessageBox.Show(this, Lang.F("C'est fait : l'entrée audio s'appelle maintenant « {0} ». Redémarrez WSJT-X pour la voir.",
                                             "Done: the audio input is now called \"{0}\". Restart WSJT-X to see it.", CableName),
                    "SDRLink", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, Lang.F("Le renommage n'a pas abouti ({0}).\n\n" +
                                             "À la main : Panneau de configuration Son › onglet Enregistrement › « {1} » › Propriétés › nom.",
                                             "Renaming failed ({0}).\n\n" +
                                             "By hand: Sound control panel › Recording tab › \"{1}\" › Properties › name.",
                                             hr == AudioEndpoints.E_ACCESSDENIED ? Lang.T("autorisation refusée", "permission denied") : Lang.T("erreur", "error") + " 0x" + hr.ToString("X8"),
                                             cap.FullName),
                    "SDRLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void UpdateCableButton()
        {
            bool on = settings.CableEnabled && cableSink != null;
            btnCable.Text = Lang.T("Câble SDRLINK", "SDRLINK cable") + (on ? " ▾" : Lang.T(" (arrêt) ▾", " (off) ▾"));
            PaintKeyed(btnCable, on ? "CableOn" : "CableOff");
        }

        // Bloc audio de la page ("pcm|fréquence|base64") à recopier vers le câble.
        void OnPcm(string msg)
        {
            if (cableSink == null) return;
            int a = msg.IndexOf('|'), b = msg.IndexOf('|', a + 1);
            int rate;
            if (b < 0 || !int.TryParse(msg.Substring(a + 1, b - a - 1), out rate)) return;
            try { cableSink.Write(rate, Convert.FromBase64String(msg.Substring(b + 1))); }
            catch (FormatException) { }
            if (cableSink.Error != shownCableError)
            {
                shownCableError = cableSink.Error;
                ShowStatus(shownCableError == null
                    ? Lang.F("Câble SDRLINK : son du WebSDR envoyé vers « {0} »", "SDRLINK cable: WebSDR audio sent to \"{0}\"", cableSink.DeviceName)
                    : Lang.F("Câble SDRLINK : « {0} » : {1}", "SDRLINK cable: \"{0}\": {1}", cableSink.DeviceName, shownCableError));
            }
        }
        string shownCableError;

        // Notch automatique (comme le DNF des Yaesu) : supprime les porteuses et sifflements stables du son du WebSDR.
        void SetPageNotch()
        {
            if (web.CoreWebView2 != null)
                web.CoreWebView2.ExecuteScriptAsync("window.__sdrlink && window.__sdrlink.setNotch && window.__sdrlink.setNotch(" + (chkNotch.Checked ? "true" : "false") + ");");
        }

        // ---------------------------------------------------------------- Volume du WebSDR

        // Nouvelle page : on reprend le volume mémorisé de ce favori (ou le volume des pages hors favoris).
        void OnPageChanged(string url)
        {
            // Volume mémorisé : celui du favori de la liste affichée, sinon celui d'une autre liste.
            currentFav = FavoriteInCurrentList(url) ?? settings.Favorites.Find(delegate(Favorite f) { return f.Matches(url); });
            int v = currentFav != null && currentFav.Volume >= 0 ? currentFav.Volume : settings.DefaultVolume;
            volumeLoading = true;
            trkVolume.Value = Math.Max(0, Math.Min(100, v));
            volumeLoading = false;
            ApplyVolume();
        }

        void OnVolumeChanged()
        {
            lblVolume.Text = trkVolume.Value + " %";
            if (volumeLoading) return;
            if (currentFav != null) currentFav.Volume = trkVolume.Value;
            else settings.DefaultVolume = trkVolume.Value;
            ApplyVolume();
        }

        void ApplyVolume()
        {
            lblVolume.Text = trkVolume.Value + " %";
            if (web.CoreWebView2 == null) return;
            double v = sdrMuted ? 0 : trkVolume.Value / 100.0;
            web.CoreWebView2.ExecuteScriptAsync(string.Format(CultureInfo.InvariantCulture,
                "window.__sdrlink && window.__sdrlink.setVolume({0});", v));
        }

        // ---------------------------------------------------------------- Volume et coupure du son du poste

        void ToggleRigMute()
        {
            if (worker == null) { ShowStatus(Lang.T("Mute TX : connectez d'abord le poste", "Mute TRX: connect the rig first")); return; }
            if (rigAfSupported != 1)
            {
                ShowStatus(Lang.T("Volume du poste non disponible avec ce poste ou cette interface (OmniRig, HRD, FT-817/818…)",
                                     "Rig volume not available with this rig or interface (OmniRig, HRD, FT-817/818…)"));
                return;
            }
            rigMuted = !rigMuted;
            worker.SetAfGain(rigMuted ? 0 : trkRigVolume.Value);
            Log.Write(rigMuted ? "Son du poste coupé (gain BF à 0)" : "Son du poste rétabli à " + trkRigVolume.Value + " %");
            UpdateMuteButtons();
        }

        void OnRigVolumeChanged()
        {
            lblRigVolume.Text = rigAfSupported == 1 ? trkRigVolume.Value + " %" : "—";
            if (rigVolumeLoading || worker == null || rigAfSupported != 1) return;
            // Bouger le curseur rétablit le son s'il était coupé.
            rigMuted = false;
            worker.SetAfGain(trkRigVolume.Value);
            UpdateMuteButtons();
        }

        // Volume relu sur le poste (à la connexion, puis quand on tourne le bouton du poste).
        void OnRigAfGain(int pct)
        {
            if (worker == null) return;
            rigAfSupported = pct < 0 ? 0 : 1;
            if (pct >= 0 && !(rigMuted && pct == 0))
            {
                rigMuted = false;
                rigVolumeLoading = true;
                trkRigVolume.Value = Math.Max(0, Math.Min(100, pct));
                rigVolumeLoading = false;
            }
            UpdateRigVolumeControls();
        }

        void UpdateRigVolumeControls()
        {
            bool on = worker != null && rigAfSupported == 1;
            trkRigVolume.Enabled = on;
            UpdateAudioRow();
            lblRigVolume.Text = on ? trkRigVolume.Value + " %" : "—";
            UpdateMuteButtons();
        }

        void UpdateMuteButtons()
        {
            StyleToggle(btnMuteSdr, sdrMuted, true, "MuteSdr");
            StyleToggle(btnMuteRig, rigMuted, worker != null && rigAfSupported == 1, "MuteRig");
            btnMuteSwap.BackColor = Theme.ButtonBack;   // ⇄ n'est pas une coupure : couleurs ordinaires
            btnMuteSwap.ForeColor = Theme.Fore;
            btnMuteSwap.FlatAppearance.BorderColor = Theme.Border;
            StyleToggle(btnMuteEsdr, esdrMuted, EsdrAvailable, "MuteEsdr");
        }

        // ---------------------------------------------------------------- Bouton ⇄

        // Coupures parcourues par le bouton ⇄ : 0 WebSDR, 1 poste (volume télécommandé), 2 ExpertSDR (s'il répond).
        static readonly string[] SwapNamesFr = { "Mute WebSDR", "Mute TX", "Mute ExpertSDR" };
        static readonly string[] SwapNamesEn = { "Mute WebSDR", "Mute TRX", "Mute ExpertSDR" };

        bool SwapAvailable(int i)
        {
            if (i == 0) return true;
            if (i == 1) return worker != null && rigAfSupported == 1;
            return EsdrAvailable;
        }

        bool SwapMuted(int i) { return i == 0 ? sdrMuted : i == 1 ? rigMuted : esdrMuted; }

        void SetSwapMute(int i, bool mute)
        {
            if (SwapMuted(i) == mute) return;
            if (i == 0) { sdrMuted = mute; ApplyVolume(); }
            else if (i == 1) ToggleRigMute();
            else
            {
                bool? r = SetExpertSdrMute(mute);
                if (!r.HasValue) return;
                esdrMuted = esdrMutedByUs = r.Value;
                Log.Write((esdrMuted ? "Son d'ExpertSDR coupé" : "Son d'ExpertSDR rétabli") + (esdrWin ? " (mélangeur Windows)" : " (TCI)"));
            }
        }

        // Aucune coupure : toutes coupées. Une seule réception audible : elle est coupée et la suivante rétablie.
        // Tout coupé : la première est rétablie. Plusieurs audibles (sans l'être toutes) : toutes coupées.
        // La coupure laissée de côté (clic droit) reste coupée, pour n'entendre qu'une réception.
        void SwapMutes()
        {
            List<int> used = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                if (!SwapAvailable(i)) continue;
                if (i == settings.SwapIgnore) SetSwapMute(i, true);
                else used.Add(i);
            }
            if (used.Count == 1) SetSwapMute(used[0], !SwapMuted(used[0]));
            else if (used.Count > 1)
            {
                List<int> on = used.FindAll(delegate(int i) { return !SwapMuted(i); });
                if (on.Count == 1)
                {
                    int k = used.IndexOf(on[0]);
                    SetSwapMute(on[0], true);
                    SetSwapMute(used[(k + 1) % used.Count], false);
                }
                else if (on.Count == 0) SetSwapMute(used[0], false);
                else foreach (int i in used) SetSwapMute(i, true);
            }
            UpdateEsdrControls();
        }

        void FillSwapMenu(ContextMenuStrip menu)
        {
            menu.Items.Clear();
            ToolStripMenuItem title = new ToolStripMenuItem(Lang.T("Coupure dont ⇄ ne tient pas compte (reste coupée) :", "Mute ignored by ⇄ (stays muted):"));
            title.Enabled = false;
            menu.Items.Add(title);
            for (int i = -1; i < 3; i++)
            {
                int idx = i;
                ToolStripMenuItem it = new ToolStripMenuItem(i < 0 ? Lang.T("Aucune", "None") : Lang.T(SwapNamesFr[i], SwapNamesEn[i]));
                it.Checked = settings.SwapIgnore == i;
                it.Click += delegate { SetSwapIgnore(idx); };
                menu.Items.Add(it);
            }
        }

        void SetSwapIgnore(int i)
        {
            settings.SwapIgnore = i;
            settings.Save();
            Log.Write("Bouton ⇄ : coupure laissée de côté = " + (i < 0 ? "aucune" : SwapNamesFr[i]));
            if (i >= 0 && SwapAvailable(i)) SetSwapMute(i, true);
            UpdateEsdrControls();
        }

        // ---------------------------------------------------------------- ExpertSDR

        // Processus ExpertSDR2, ExpertSDR3… (identifiants relevés à chaque surveillance).
        readonly Dictionary<int, bool> esdrPids = new Dictionary<int, bool>();

        bool IsExpertSdr(int pid) { return esdrPids.ContainsKey(pid); }

        bool EsdrAvailable { get { return esdrWin || (esdrTci != null && esdrTci.Connected); } }

        // Toutes les 3 secondes : Mute et Volume ExpertSDR affichés si le logiciel tourne. Son par Windows : état lu
        // dans le mélangeur ; sinon liaison TCI (tentée en arrière-plan toutes les 10 s tant qu'elle ne répond pas).
        void CheckExpertSdr()
        {
            esdrPids.Clear();
            foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
            {
                try { if (p.ProcessName.StartsWith("ExpertSDR", StringComparison.OrdinalIgnoreCase)) esdrPids[p.Id] = true; }
                catch { }
                p.Dispose();
            }
            esdrRunning = esdrPids.Count > 0;
            if (!esdrRunning)
            {
                if (esdrTci != null) { esdrTci.Dispose(); esdrTci = null; }
                esdrWin = esdrMuted = esdrMutedByUs = esdrUnreachableShown = false;
                UpdateEsdrControls();
                return;
            }
            bool? m = null;
            try { m = AudioEndpoints.ProcessMute(IsExpertSdr, null); } catch (Exception ex) { Log.Write("ExpertSDR, lecture du son : " + ex.Message); }
            esdrWin = m.HasValue;
            if (esdrWin)
            {
                esdrMuted = m.Value;
                try { ShowEsdrVolume(AudioEndpoints.ProcessVolume(IsExpertSdr, null)); } catch { }
            }
            else
            {
                ConnectExpertSdrTci();
                RefreshExpertSdr();
            }
            UpdateEsdrControls();
        }

        // Chaque seconde : état annoncé par ExpertSDR sur la liaison TCI (réglages faits dans ExpertSDR compris).
        void RefreshExpertSdr()
        {
            if (!esdrRunning || esdrWin || esdrTci == null) return;
            if (esdrTci.Connected)
            {
                bool? m = esdrTci.Mute;
                if (m.HasValue) esdrMuted = m.Value;
                ShowEsdrVolume(esdrTci.VolumePercent);
            }
            UpdateEsdrControls();
        }

        void ConnectExpertSdrTci()
        {
            if ((esdrTci != null && esdrTci.Connected) || esdrConnecting || DateTime.UtcNow < esdrTciRetry) return;
            esdrConnecting = true;
            esdrTciRetry = DateTime.UtcNow.AddSeconds(10);
            int port = settings.EsdrTciPort;
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                ExpertSdrTci c = ExpertSdrTci.TryConnect("127.0.0.1", port);
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        esdrConnecting = false;
                        if (esdrTci != null) esdrTci.Dispose();
                        esdrTci = c;
                        if (c != null) Log.Write("ExpertSDR : liaison TCI établie sur le port " + port);
                        else if (esdrRunning && !esdrWin && !esdrUnreachableShown)
                        {
                            // Mute et Volume ExpertSDR restent masqués : on dit où changer le port.
                            esdrUnreachableShown = true;
                            ShowStatus(Lang.F("ExpertSDR est lancé mais ne répond pas sur le port TCI {0} : clic droit sur « Volume WebSDR » pour changer le port.",
                                "ExpertSDR is running but does not answer on TCI port {0}: right-click \"WebSDR volume\" to change the port.", port));
                        }
                        RefreshExpertSdr();
                    });
                }
                catch (InvalidOperationException) { if (c != null) c.Dispose(); }   // fenêtre fermée entre-temps
            });
            t.IsBackground = true;
            t.Start();
        }

        // Windows Forms peut changer la place d'une commande cachée quand elle s'affiche pour la première fois
        // (création de sa fenêtre) : on remet l'ordre voulu de la ligne de son à chaque affichage ou masquage.
        void OrderAudioRow()
        {
            audioRow.SuspendLayout();
            for (int i = 0; i < audioOrder.Length; i++) audioRow.Controls.SetChildIndex(audioOrder[i], i);
            audioRow.ResumeLayout(true);
        }

        // Ligne de son : Mute TX et Volume TX seulement si le volume du poste est télécommandé, Mute et Volume ExpertSDR
        // seulement si ExpertSDR répond, ⇄ seulement s'il y a au moins deux sons à permuter. Les commandes masquées ne
        // laissent pas de place vide (la ligne se resserre à gauche).
        void UpdateAudioRow()
        {
            bool rig = worker != null && rigAfSupported == 1, esdr = EsdrAvailable;
            if (audioShownRig == rig && audioShownEsdr == esdr) return;
            audioShownRig = rig;
            audioShownEsdr = esdr;
            lblRigVolumeCaption.Visible = trkRigVolume.Visible = lblRigVolume.Visible = btnMuteRig.Visible = rig;
            btnMuteEsdr.Visible = lblEsdrVolumeCaption.Visible = trkEsdrVolume.Visible = lblEsdrVolume.Visible = esdr;
            btnMuteSwap.Visible = rig || esdr;
            OrderAudioRow();
        }

        void UpdateEsdrControls()
        {
            UpdateAudioRow();
            bool avail = EsdrAvailable;
            trkEsdrVolume.Enabled = avail;
            lblEsdrVolume.Text = avail ? trkEsdrVolume.Value + " %" : "—";
            UpdateMuteButtons();
        }

        void ShowEsdrVolume(int pct)
        {
            if (pct < 0 || trkEsdrVolume.Capture || trkEsdrVolume.Value == pct) return;   // pas pendant que l'on fait glisser le curseur
            esdrVolumeLoading = true;
            trkEsdrVolume.Value = Math.Max(0, Math.Min(100, pct));
            esdrVolumeLoading = false;
        }

        void OnEsdrVolumeChanged()
        {
            lblEsdrVolume.Text = EsdrAvailable ? trkEsdrVolume.Value + " %" : "—";
            if (esdrVolumeLoading) return;
            if (esdrWin) { try { AudioEndpoints.ProcessVolume(IsExpertSdr, trkEsdrVolume.Value); } catch { } }
            else if (esdrTci != null && esdrTci.Connected) esdrTci.SetVolumePercent(trkEsdrVolume.Value);
        }

        void ToggleExpertSdrMute()
        {
            // Pas encore de liaison TCI : on la tente tout de suite.
            if (!EsdrAvailable)
            {
                Cursor = Cursors.WaitCursor;
                if (esdrTci != null) esdrTci.Dispose();
                esdrTci = ExpertSdrTci.TryConnect("127.0.0.1", settings.EsdrTciPort);
                Cursor = Cursors.Default;
            }
            bool? r = SetExpertSdrMute(!esdrMuted);
            if (!r.HasValue)
            {
                Log.Write("ExpertSDR : ni son Windows ni serveur TCI sur le port " + settings.EsdrTciPort);
                MessageBox.Show(this, Lang.F("SDRLink ne peut pas couper le son d'ExpertSDR : ExpertSDR ne passe pas par le son de Windows (son sorti par le poste ou par ASIO), et son serveur TCI ne répond pas sur le port {0}.\n\nDans ExpertSDR, activez le serveur TCI (Options > TCI). Si son port n'est pas {0}, faites un clic droit sur Mute ExpertSDR pour l'indiquer.",
                    "SDRLink cannot mute ExpertSDR: ExpertSDR does not play through Windows sound (audio comes out of the radio or through ASIO), and its TCI server does not answer on port {0}.\n\nIn ExpertSDR, enable the TCI server (Options > TCI). If its port is not {0}, right-click Mute ExpertSDR to set it.",
                    settings.EsdrTciPort), "SDRLink", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateEsdrControls();
                return;
            }
            esdrMuted = r.Value;
            esdrMutedByUs = esdrMuted;
            Log.Write((esdrMuted ? "Son d'ExpertSDR coupé" : "Son d'ExpertSDR rétabli") + (esdrWin ? " (mélangeur Windows)" : " (TCI)"));
            UpdateEsdrControls();
        }

        // Mélangeur de volume de Windows si ExpertSDR y joue, sinon commande TCI "mute". null si aucun des deux.
        bool? SetExpertSdrMute(bool mute)
        {
            if (esdrWin)
            {
                try { bool? r = AudioEndpoints.ProcessMute(IsExpertSdr, mute); if (r.HasValue) return r; }
                catch (Exception ex) { Log.Write("ExpertSDR, coupure du son : " + ex.Message); }
            }
            if (esdrTci != null && esdrTci.Connected) { esdrTci.SetMute(mute); return mute; }
            return null;
        }

        // Clic droit sur Mute ExpertSDR ou sur « Volume WebSDR » : port du serveur TCI d'ExpertSDR.
        void AskExpertSdrPort()
        {
            using (Form f = new Form())
            {
                f.Text = Lang.T("Port TCI d'ExpertSDR", "ExpertSDR TCI port");
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.Font = Font;
                f.ClientSize = new Size(360, 92);
                Label l = new Label { Text = Lang.T("Port du serveur TCI (ExpertSDR, Options > TCI) :", "TCI server port (ExpertSDR, Options > TCI):"), Location = new Point(12, 18), AutoSize = true };
                NumericUpDown n = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = settings.EsdrTciPort, Location = new Point(270, 14), Width = 76 };
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = Lang.T("Annuler", "Cancel"), DialogResult = DialogResult.Cancel };
                ok.SetBounds(180, 52, 80, 28);
                cancel.SetBounds(266, 52, 80, 28);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Controls.AddRange(new Control[] { l, n, ok, cancel });
                Theme.Apply(f);
                if (f.ShowDialog(this) != DialogResult.OK || (int)n.Value == settings.EsdrTciPort) return;
                settings.EsdrTciPort = (int)n.Value;
            }
            Log.Write("ExpertSDR : port TCI changé en " + settings.EsdrTciPort);
            settings.Save();
            // Reconnexion sur le nouveau port.
            if (esdrTci != null) { esdrTci.Dispose(); esdrTci = null; }
            esdrTciRetry = DateTime.MinValue;
            CheckExpertSdr();
        }

        // Bouton de coupure : rouge quand le son est coupé, vert quand il passe (comme le bouton de mode choisi),
        // écriture blanche ; grisé à la main s'il est indisponible (pas désactivé, pour rester lisible).
        // Couleurs « son coupé » et « son actif » réglables au clic droit (clé + Cut / On).
        void StyleToggle(Button b, bool active, bool available, string key)
        {
            b.BackColor = active ? Color.FromArgb(200, 0, 0) : available ? Color.FromArgb(0, 120, 60) : Theme.ButtonBack;
            b.ForeColor = active || available ? Color.White : Color.FromArgb(140, 140, 140);
            b.FlatAppearance.BorderColor = active ? Color.FromArgb(150, 0, 0) : available ? Color.FromArgb(0, 150, 75) : Theme.Border;
            int custom = active ? ColorOf(key + "Cut") : available ? ColorOf(key + "On") : -1;
            if (custom >= 0) PaintCustom(b, custom);
        }

        // ---------------------------------------------------------------- Favoris

        // Liste déroulante des WebSDR : seulement les favoris de la liste choisie.
        void RefillUrlCombo()
        {
            FillFavListCombo();   // une liste a pu se vider ou se remplir
            string text = cbUrl.Text;
            cbUrl.BeginUpdate();
            cbUrl.Items.Clear();
            foreach (Favorite f in settings.Favorites)
                if (f.List == settings.CurrentList) cbUrl.Items.Add(f);
            cbUrl.EndUpdate();
            cbUrl.Text = text;
        }

        // Numéro de liste de chaque ligne du menu des listes (les listes vides n'y figurent pas).
        readonly List<int> favListShown = new List<int>();

        // Noms des listes (ils changent quand on les renomme ou qu'on change de langue). Seules les listes
        // contenant des favoris sont proposées ; si la liste choisie s'est vidée, on passe à la première non vide.
        void FillFavListCombo()
        {
            favListShown.Clear();
            for (int i = 0; i < Favorite.ListCount; i++)
                if (settings.Favorites.Exists(delegate(Favorite f) { return f.List == i; })) favListShown.Add(i);
            if (favListShown.Count == 0) favListShown.Add(Math.Max(0, Math.Min(Favorite.ListCount - 1, settings.CurrentList)));
            if (!favListShown.Contains(settings.CurrentList)) settings.CurrentList = favListShown[0];

            bool was = loading;
            loading = true;
            cbFavList.Items.Clear();
            foreach (int i in favListShown) cbFavList.Items.Add(FavoritesDialog.ListLabel(settings.ListNames, i));
            cbFavList.SelectedIndex = favListShown.IndexOf(settings.CurrentList);
            loading = was;
        }

        void RenameFavoriteList()
        {
            int i = settings.CurrentList;
            string name = FavoritesDialog.AskListName(this, settings.ListNames[i]);
            if (string.IsNullOrEmpty(name)) return;
            settings.ListNames[i] = name.Replace("|", "/");
            FillFavListCombo();
            settings.Save();
        }

        // Page affichée, proposée comme nouveau favori (nom = titre de la page).
        Favorite CurrentPage()
        {
            if (web.CoreWebView2 == null || string.IsNullOrEmpty(web.CoreWebView2.Source)) return null;
            return new Favorite(web.CoreWebView2.DocumentTitle, web.CoreWebView2.Source);
        }

        void AddFavorite()
        {
            Favorite cur = CurrentPage();
            // La liste est demandée à chaque ajout (la liste affichée est proposée) ; une page déjà présente
            // dans la liste choisie est refusée. La même page peut figurer dans plusieurs listes.
            using (FavoriteEditDialog d = new FavoriteEditDialog(Lang.T("Ajouter un favori", "Add favorite"),
                cur != null ? cur.Name : "", cur != null ? cur.Url : cbUrl.Text.Trim(), settings.ListNames, settings.CurrentList,
                delegate(string url, int list) { return settings.Favorites.Exists(delegate(Favorite x) { return x.List == list && x.Matches(url); }); }))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                // Le nouveau favori garde le volume actuel ; s'il s'agit de la page affichée dans la liste affichée, il devient le favori courant.
                Favorite f = new Favorite(d.FavName, d.FavUrl);
                f.Volume = trkVolume.Value;
                f.List = d.FavList;
                settings.Favorites.Add(f);
                if (f.List == settings.CurrentList && web.CoreWebView2 != null && f.Matches(web.CoreWebView2.Source)) currentFav = f;
                RefillUrlCombo();
                ShowStatus(Lang.F("« {0} » ajouté à « {1} ».", "\"{0}\" added to \"{1}\".", f.Name, FavoritesDialog.ListLabel(settings.ListNames, f.List)));
            }
            SaveUiToSettings();
            settings.Save();
        }

        // Favori de la liste affichée correspondant à cette adresse (null si la page n'y figure pas).
        Favorite FavoriteInCurrentList(string url)
        {
            return settings.Favorites.Find(delegate(Favorite f) { return f.List == settings.CurrentList && f.Matches(url); });
        }

        // Retire la page affichée de la liste de favoris affichée seulement (elle reste dans les autres listes).
        void RemoveCurrentFavorite()
        {
            string listName = FavoritesDialog.ListLabel(settings.ListNames, settings.CurrentList);
            Favorite fav = web.CoreWebView2 != null ? FavoriteInCurrentList(web.CoreWebView2.Source) : null;
            if (fav == null)
            {
                ShowStatus(Lang.F("Cette page n'est pas dans « {0} ».", "This page is not in \"{0}\".", listName));
                return;
            }
            if (MessageBox.Show(Lang.F("Retirer « {0} » de « {1} » ?", "Remove \"{0}\" from \"{1}\"?", fav.Name, listName),
                    "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            settings.Favorites.Remove(fav);
            RefillUrlCombo();
            if (web.CoreWebView2 != null) OnPageChanged(web.CoreWebView2.Source);
            SaveUiToSettings();
            settings.Save();
        }

        // Recherche dans les annuaires publics ; les récepteurs cochés sont ajoutés aux favoris.
        void SearchWebSdrs()
        {
            using (WebSdrSearchDialog d = new WebSdrSearchDialog(settings.Favorites, settings.ListNames, delegate(string url) { Navigate(url); }))
            {
                d.TargetList = settings.CurrentList;
                d.ShowDialog(this);
                if (d.Added == 0) return;
            }
            RefillUrlCombo();
            if (web.CoreWebView2 != null) OnPageChanged(web.CoreWebView2.Source);
            SaveUiToSettings();
            settings.Save();
        }

        // Premier démarrage : recherche des WebSDR pour remplir les favoris, si Internet répond. Faite une seule fois ;
        // sans connexion, elle est retentée au démarrage suivant.
        void FirstRunSearch()
        {
            if (settings.FirstSearchDone) return;
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                bool online = InternetAvailable();
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (!online) { Log.Write("Premier démarrage : pas de connexion Internet, recherche des WebSDR reportée"); return; }
                        settings.FirstSearchDone = true;
                        settings.Save();
                        Log.Write("Premier démarrage : recherche des WebSDR");
                        SearchWebSdrs();
                    });
                }
                catch (InvalidOperationException) { }   // fenêtre fermée entre-temps
            });
            t.IsBackground = true;
            t.Start();
        }

        static bool InternetAvailable()
        {
            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) return false;
            try
            {
                System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://rx.linkfanel.net/");
                req.Method = "HEAD";
                req.Timeout = 6000;
                req.UserAgent = "SDRLink/1.0 (F5PBG)";
                using (req.GetResponse()) return true;
            }
            catch (System.Net.WebException ex) { return ex.Response != null; }   // le serveur a répondu, même par une erreur
            catch (Exception) { return false; }
        }

        public void ShowError(string message)
        {
            try { ShowStatus(Lang.F("Erreur inattendue, notée dans le journal : {0}", "Unexpected error, written to the log: {0}", message)); } catch { }
        }

        // ---------------------------------------------------------------- Mémoires de fréquences

        // Fréquence et mode actuels, proposés pour une nouvelle mémoire.
        Memory CurrentMemory()
        {
            string mode = worker != null && rigM != null ? rigM : (clickedMode ?? "");
            return new Memory("", CurrentFreq(), mode);
        }

        void ShowMemories()
        {
            using (MemoriesDialog d = new MemoriesDialog(settings.Memories, CurrentMemory, TuneMemory))
                d.ShowDialog(this);
            settings.Save();
        }

        void AddMemoryHere()
        {
            Memory m = CurrentMemory();
            m.List = settings.Memories.CurrentList;
            using (MemoryEditDialog d = new MemoryEditDialog(Lang.T("Ajouter une mémoire", "Add memory"), m, settings.Memories))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                settings.Memories.Items.Add(d.Result);
                ShowStatus(Lang.F("Mémoire « {0} » ajoutée à « {1} ».", "Memory \"{0}\" added to \"{1}\".", d.Result.Label,
                    FavoritesDialog.ListLabel(settings.Memories.ListNames, d.Result.List)));
            }
            settings.Save();
        }

        // Accord immédiat sur une mémoire (poste et WebSDR), avec son mode s'il est mémorisé.
        void TuneMemory(Memory m)
        {
            if (m == null || m.Hz <= 0) return;
            ApplyMemory(m);
            AddToHistory(m.Hz);
            settings.Memories.Used(m);
            Log.Write("Mémoire : " + m.Label + " (" + Memory.KHz(m.Hz) + " kHz" + (m.Mode != "" ? " " + m.Mode : "") + ")");
            ShowStatus(Lang.F("Mémoire « {0} » : {1} kHz", "Memory \"{0}\": {1} kHz", m.Label, Memory.KHz(m.Hz)));
            settings.Save();
        }

        // Fréquence (poste et WebSDR) et mode de la mémoire, sans la noter (sert aussi au scan).
        void ApplyMemory(Memory m)
        {
            TuneBoth(m.Hz);
            if (m.Mode != "")
            {
                if (ModeKey(m.Mode) == "digi") digiMode = m.Mode;
                else if (m.Mode == Modes.CW || m.Mode == Modes.CWR) cwMode = m.Mode;
                digiActive = ModeKey(m.Mode) == "digi";
                modeClickUntil = DateTime.UtcNow.AddSeconds(2);
                clickedMode = m.Mode;
                SetModeBoth(m.Mode);
                shownModeFamily = null;
                UpdateDisplays();
            }
        }

        // ---------------------------------------------------------------- Scan

        readonly Timer scanTimer = new Timer();
        bool scanning;
        long scanHz, scanLo, scanHi;
        int scanIndex;
        List<Memory> scanMems;

        void ConfigureScan()
        {
            DialogResult r;
            using (ScanDialog d = new ScanDialog(settings)) r = d.ShowDialog(this);
            if (r == DialogResult.OK || r == DialogResult.Yes) settings.Save();
            if (r == DialogResult.Yes) { if (scanning) StopScan(null); StartScan(); }
        }

        // Fréquences : de la plus basse à la plus haute, par pas, puis on recommence. Mémoires : les cochées, dans l'ordre.
        // Chaque fréquence ou mémoire est gardée la durée choisie. L'émission arrête le scan.
        void StartScan()
        {
            if (Transmitting) { ShowStatus(Lang.T("Scan impossible en émission", "No scan while transmitting")); return; }
            if (settings.ScanType == 0)
            {
                scanLo = Math.Min(settings.ScanStartHz, settings.ScanEndHz);
                scanHi = Math.Max(settings.ScanStartHz, settings.ScanEndHz);
                if (scanLo <= 0 || scanLo == scanHi) { ShowStatus(Lang.T("Scan : indiquez les fréquences de début et de fin dans « Conf scan »", "Scan: set the start and end frequencies in \"Scan setup\"")); return; }
                scanHz = scanLo;
                scanTimer.Interval = Math.Max(1, settings.ScanFreqDwell) * 1000;
            }
            else
            {
                scanMems = settings.Memories.Items.FindAll(delegate(Memory m) { return m.Scan; });
                if (scanMems.Count == 0) { ShowStatus(Lang.T("Scan : cochez des mémoires dans « Conf scan »", "Scan: check memories in \"Scan setup\"")); return; }
                scanIndex = 0;
                scanTimer.Interval = Math.Max(1, settings.ScanMemDwell) * 1000;
            }
            scanning = true;
            Log.Write("Scan : départ (" + (settings.ScanType == 0 ? FormatFreq(scanLo) + " à " + FormatFreq(scanHi) + ", pas " + settings.ScanStepHz + " Hz" : scanMems.Count + " mémoire(s)")
                + ", " + scanTimer.Interval / 1000 + " s)");
            ScanTune();
            scanTimer.Start();
            UpdateScanButton();
        }

        void StopScan(string reason)
        {
            if (!scanning) return;
            scanning = false;
            scanTimer.Stop();
            Log.Write("Scan : arrêt" + (reason != null ? " (" + reason + ")" : ""));
            ShowStatus(reason != null ? Lang.F("Scan arrêté : {0}", "Scan stopped: {0}", reason) : Lang.T("Scan arrêté", "Scan stopped"));
            UpdateScanButton();
        }

        void ScanNext()
        {
            if (!scanning) return;
            if (Transmitting) { StopScan(Lang.T("émission", "transmitting")); return; }
            if (settings.ScanType == 0)
            {
                scanHz += Math.Max(1, settings.ScanStepHz);
                if (scanHz > scanHi) scanHz = scanLo;
            }
            else scanIndex = (scanIndex + 1) % scanMems.Count;
            ScanTune();
        }

        // Le texte du scan va directement dans la barre d'état, sans remplir l'historique des messages.
        void ScanTune()
        {
            string what;
            if (settings.ScanType == 0) { TuneBoth(scanHz); what = Memory.KHz(scanHz) + " kHz"; }
            else
            {
                Memory m = scanMems[scanIndex];
                ApplyMemory(m);
                what = m.Label + (m.Name.Length > 0 ? " (" + Memory.KHz(m.Hz) + " kHz)" : "");
            }
            status.Text = Lang.F("Scan : {0}", "Scan: {0}", what);
        }

        void UpdateScanButton()
        {
            btnScan.Text = scanning ? Lang.T("■ Arrêter le scan", "■ Stop scan") : "▶ Scan";
            btnScan.BackColor = scanning ? Color.FromArgb(200, 0, 0) : Theme.ButtonBack;
            btnScan.ForeColor = scanning ? Color.White : Theme.Fore;
            btnScan.FlatAppearance.BorderColor = scanning ? Color.FromArgb(150, 0, 0) : Theme.Border;
            int custom = ColorOf(scanning ? "ScanOn" : "ScanOff");
            if (custom >= 0) PaintCustom(btnScan, custom);
        }

        // ---------------------------------------------------------------- Enregistrement (REC)

        AudioRecorder recorder;
        string recName;             // nom proposé pour le fichier : date, heure, fréquence et mode au départ

        // Seul le poste est écouté : Mute WebSDR actif, et Mute ExpertSDR aussi si ExpertSDR répond.
        bool OnlyRigHeard { get { return sdrMuted && (!EsdrAvailable || esdrMuted); } }

        void StartRecording()
        {
            // Carte son du poste choisie et seul le poste écouté : on enregistre son entrée, sinon les haut-parleurs.
            string card = settings.RecCardId.Length > 0 && OnlyRigHeard ? settings.RecCardId : null;
            string cardError = null;
            if (card != null)
            {
                try { recorder = AudioRecorder.Start(card); }
                catch (Exception ex)
                {
                    cardError = ex.Message;
                    Log.Write("Enregistrement : carte son du poste « " + settings.RecCardName + " » indisponible (" + ex.Message + "), haut-parleurs à la place");
                }
            }
            if (recorder == null)
            {
                try { recorder = AudioRecorder.Start(null); }
                catch (Exception ex)
                {
                    recorder = null;
                    Log.Write("Enregistrement impossible : " + ex.Message);
                    ShowStatus(Lang.F("Enregistrement impossible : {0}", "Cannot record: {0}", ex.Message));
                    return;
                }
            }
            long hz = sdrF > 0 ? sdrF : rigF;
            string mode = sdrF > 0 ? sdrM : rigM;
            recName = "SDRLink " + DateTime.Now.ToString("yyyy-MM-dd HH'h'mm", CultureInfo.InvariantCulture)
                + (hz > 0 ? " " + Memory.KHz(hz) + " kHz" : "") + (!string.IsNullOrEmpty(mode) ? " " + Modes.Display(mode) : "");
            if (cardError != null)
                ShowStatus(Lang.F("Carte son du poste « {0} » indisponible ({1}) : enregistrement des haut-parleurs", "Rig sound card \"{0}\" not available ({1}): recording the speakers", settings.RecCardName, cardError));
            else
                ShowStatus(Lang.F("Enregistrement en cours : son de « {0} » (cliquez sur REC pour l'arrêter)", "Recording the sound of \"{0}\" (click REC to stop)", recorder.DeviceName));
            UpdateRecButton();
        }

        // Menu du clic droit sur REC : entrées des cartes son de Windows (celle du poste : « USB Audio CODEC »…).
        void FillRecCardMenu(ContextMenuStrip m)
        {
            m.Items.Clear();
            ToolStripMenuItem title = new ToolStripMenuItem(Lang.T("Carte son du poste, enregistrée quand Mute WebSDR et Mute ExpertSDR sont actifs :",
                "Rig sound card, recorded when Mute WebSDR and Mute ExpertSDR are on:"));
            title.Enabled = false;
            m.Items.Add(title);
            ToolStripMenuItem none = new ToolStripMenuItem(Lang.T("Aucune (toujours les haut-parleurs)", "None (always the speakers)"));
            none.Checked = settings.RecCardId.Length == 0;
            none.Click += delegate { SetRecCard("", ""); };
            m.Items.Add(none);
            bool found = false;
            try
            {
                foreach (AudioEndpoints.Endpoint e in AudioEndpoints.All())
                {
                    if (!e.Capture) continue;
                    string id = e.Id, name = e.FullName ?? e.Name ?? id;
                    ToolStripMenuItem it = new ToolStripMenuItem(name);
                    it.Checked = id == settings.RecCardId;
                    found |= it.Checked;
                    it.Click += delegate { SetRecCard(id, name); };
                    m.Items.Add(it);
                }
            }
            catch (Exception ex) { Log.Write("REC : lecture des cartes son impossible : " + ex.Message); }
            // Carte choisie mais absente (débranchée) : gardée, affichée grisée.
            if (!found && settings.RecCardId.Length > 0)
            {
                ToolStripMenuItem gone = new ToolStripMenuItem(Lang.F("{0} (non branchée)", "{0} (not connected)", settings.RecCardName));
                gone.Checked = true;
                gone.Enabled = false;
                m.Items.Add(gone);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(BuildRecColorMenu());
            // Largeur : étroit (rectangle deux fois plus large que haut, par défaut) ou large (quatre fois), même hauteur.
            ToolStripMenuItem width = new ToolStripMenuItem(Lang.T("Largeur du bouton REC", "REC button width"));
            string[] widths = { Lang.T("Étroit", "Narrow"), Lang.T("Large", "Wide") };
            for (int i = 0; i < widths.Length; i++)
            {
                int n = i + 1;
                ToolStripMenuItem it = new ToolStripMenuItem(widths[i]);
                it.Checked = settings.RecWidth == n;
                it.Click += delegate { SetRecWidth(n); };
                width.DropDownItems.Add(it);
            }
            m.Items.Add(width);
        }

        // Hauteur des boutons Mute (30 à la taille de texte normale) ; largeur : 2 (étroit) ou 4 (large) fois la hauteur.
        Size RecSize()
        {
            int h = btnMuteSdr != null && btnMuteSdr.Height > 0 ? btnMuteSdr.Height : 30;
            return new Size(h << settings.RecWidth, h);
        }

        void SetRecWidth(int w)
        {
            settings.RecWidth = w;
            settings.Save();
            ResizeRecButton();
        }

        void ResizeRecButton()
        {
            Size s = RecSize();
            if (btnRec.Size == s && btnRec.Image != null) return;
            btnRec.MinimumSize = Size.Empty;   // d'abord le minimum, sinon il empêche de rétrécir
            btnRec.Size = btnRec.MinimumSize = s;
            UpdateRecButton();
        }

        // Couleurs proposées pour le fond de REC au repos (pas d'orange : c'est la couleur de l'enregistrement en cours).
        static readonly int[] RecColors = { 0x000080, 0x1E5AC8, 0x4FA3E0, 0x008080, 0x1E7D32, 0xB71C1C, 0x6D1028, 0x6A1B9A, 0xD81B60, 0x6D4C41, 0x607080, 0x000000, 0xF2C200 };
        // Autres boutons : les mêmes, plus orange et blanc.
        static readonly int[] ButtonColors = { 0x000080, 0x1E5AC8, 0x4FA3E0, 0x008080, 0x1E7D32, 0xB71C1C, 0x6D1028, 0x6A1B9A, 0xD81B60, 0x6D4C41, 0x607080, 0x000000, 0xF2C200, 0xFF8C00, 0xFFFFFF };

        ToolStripMenuItem BuildRecColorMenu()
        {
            return BuildColorMenu(Lang.T("Couleur du bouton REC", "REC button colour"), settings.RecColor, RecColors, true, SetRecColor);
        }

        // Noms des couleurs de ButtonColors (RecColors en est le début).
        static string[] ColorNames(bool rec)
        {
            return new[]
            {
                rec ? Lang.T("Bleu marine (par défaut)", "Navy blue (default)") : Lang.T("Bleu marine", "Navy blue"),
                Lang.T("Bleu", "Blue"), Lang.T("Bleu ciel", "Sky blue"), Lang.T("Bleu canard", "Teal"), Lang.T("Vert", "Green"),
                Lang.T("Rouge", "Red"), Lang.T("Bordeaux", "Burgundy"), Lang.T("Violet", "Purple"), Lang.T("Rose", "Pink"),
                Lang.T("Marron", "Brown"), Lang.T("Gris", "Grey"), Lang.T("Noir", "Black"), Lang.T("Jaune", "Yellow"),
                Lang.T("Orange", "Orange"), Lang.T("Blanc", "White"),
            };
        }

        // Sous-menu de couleurs : « Par défaut » (sauf REC, dont le défaut est le bleu marine), la palette,
        // et « Autre couleur… » (choix libre). current = -1 : couleur par défaut ; set(-1) y revient.
        ToolStripMenuItem BuildColorMenu(string title, int current, int[] colors, bool rec, Action<int> set)
        {
            string[] names = ColorNames(rec);
            ToolStripMenuItem menu = new ToolStripMenuItem(title);
            if (current >= 0) menu.Image = ColorSwatch(current);
            bool found = false;
            if (!rec)
            {
                ToolStripMenuItem def = new ToolStripMenuItem(Lang.T("Par défaut", "Default"));
                def.Checked = found = current < 0;
                def.Click += delegate { set(-1); };
                menu.DropDownItems.Add(def);
                menu.DropDownItems.Add(new ToolStripSeparator());
            }
            for (int i = 0; i < colors.Length; i++)
            {
                int rgb = colors[i];
                ToolStripMenuItem it = new ToolStripMenuItem(names[i], ColorSwatch(rgb));
                it.Checked = rgb == current;
                found |= it.Checked;
                it.Click += delegate { set(rgb); };
                menu.DropDownItems.Add(it);
            }
            menu.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem other = new ToolStripMenuItem(Lang.T("Autre couleur…", "Other colour…"), found ? null : ColorSwatch(current));
            other.Checked = !found;
            other.Click += delegate
            {
                using (ColorDialog d = new ColorDialog())
                {
                    d.FullOpen = true;
                    if (current >= 0)
                    {
                        d.Color = Color.FromArgb(current);
                        d.CustomColors = new int[] { ToBgr(current) };
                    }
                    if (d.ShowDialog(this) == DialogResult.OK) set(d.Color.ToArgb() & 0xFFFFFF);
                }
            };
            menu.DropDownItems.Add(other);
            return menu;
        }

        // ColorDialog.CustomColors est en BGR.
        static int ToBgr(int rgb) { return ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF); }

        static Bitmap ColorSwatch(int rgb)
        {
            Bitmap b = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(b))
            {
                using (SolidBrush br = new SolidBrush(Color.FromArgb(255, Color.FromArgb(rgb)))) g.FillRectangle(br, 1, 1, 14, 14);
                g.DrawRectangle(Pens.Gray, 1, 1, 13, 13);
            }
            return b;
        }

        void SetRecColor(int rgb)
        {
            settings.RecColor = rgb & 0xFFFFFF;
            settings.Save();
            UpdateRecButton();
        }

        void SetRecCard(string id, string name)
        {
            settings.RecCardId = id;
            settings.RecCardName = name;
            settings.Save();
            Log.Write("REC : carte son du poste = " + (id.Length > 0 ? name : "aucune"));
            ShowStatus(id.Length > 0
                ? Lang.F("REC : « {0} » sera enregistrée quand seul le poste est écouté", "REC: \"{0}\" will be recorded when only the rig is heard", name)
                : Lang.T("REC : enregistrement des haut-parleurs", "REC: recording the speakers"));
            UpdateRecButton();
        }

        // Arrêt : fenêtre pour sauvegarder le son (nom, dossier, format). En fermant SDRLink, le fichier est écrit tout de suite.
        void StopRecording(bool closing)
        {
            AudioRecorder r = recorder;
            recorder = null;
            r.Stop();
            UpdateRecButton();
            Log.Write("Enregistrement : arrêt (" + r.Duration.ToString(@"hh\:mm\:ss") + ")");
            if (r.Frames == 0)
            {
                r.Discard();
                ShowStatus(Lang.T("Enregistrement vide : rien à sauvegarder", "Empty recording: nothing to save"));
                return;
            }
            AskSaveRecording(r, closing);
        }

        void AskSaveRecording(AudioRecorder r, bool wait)
        {
            string folder = settings.RecFolder.Length > 0 ? settings.RecFolder
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "SDRLink");
            string path;
            int format;
            using (RecordSaveDialog d = new RecordSaveDialog(r.Duration, recName, folder))
            {
                if (d.ShowDialog(this) != DialogResult.OK)
                {
                    r.Discard();
                    Log.Write("Enregistrement : supprimé sans sauvegarde");
                    ShowStatus(Lang.T("Enregistrement supprimé", "Recording deleted"));
                    return;
                }
                path = d.FilePath;
                format = d.Format;
                settings.RecFolder = d.Folder;
            }
            settings.Save();
            string file = Path.GetFileName(path);
            System.Threading.ThreadStart work = delegate
            {
                Exception error = null;
                try
                {
                    AudioFile.Save(r.TempPath, r.SampleRate, r.Frames, path, format, delegate(int pc)
                    {
                        if (!wait) UiPost(delegate { status.Text = Lang.F("Écriture de « {0} » : {1} %", "Writing \"{0}\": {1}%", file, pc); });
                    });
                    r.Discard();
                    Log.Write("Enregistrement sauvegardé : " + path);
                }
                catch (Exception ex)
                {
                    error = ex;
                    Log.Write("Enregistrement : échec de l'écriture de " + path + " : " + ex.Message);
                }
                Action done = delegate
                {
                    if (error == null) { ShowStatus(Lang.F("Enregistrement sauvegardé : « {0} »", "Recording saved: \"{0}\"", path)); return; }
                    // Le son est gardé : nouvel essai, avec un autre format ou un autre dossier.
                    MessageBox.Show(this, Lang.T("Le fichier n'a pas pu être écrit : ", "The file could not be written: ") + error.Message,
                        "SDRLink", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    AskSaveRecording(r, wait);
                };
                if (wait) done(); else UiPost(done);
            };
            if (wait)
            {
                Cursor = Cursors.WaitCursor;
                try { work(); } finally { Cursor = Cursors.Default; }
                return;
            }
            // Écriture en arrière-plan ; SDRLink attend sa fin avant de se fermer (thread de premier plan).
            System.Threading.Thread t = new System.Threading.Thread(work);
            t.IsBackground = false;
            t.Name = "REC écriture";
            t.Start();
        }

        // Action renvoyée vers la fenêtre (ignorée si elle est déjà fermée).
        void UiPost(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        void UpdateRecButton()
        {
            bool on = recorder != null;
            Color idle = Color.FromArgb(255, Color.FromArgb(settings.RecColor));
            btnRec.BackColor = on ? Color.FromArgb(255, 140, 0) : idle;
            // Texte blanc, noir sur les fonds clairs (jaune, bleu ciel…).
            btnRec.ForeColor = !on && idle.R * 299 + idle.G * 587 + idle.B * 114 > 160000 ? Color.Black : Color.White;
            // Bordure comme les Mute : plus claire que le fond au repos (vert 0,120,60 → 0,150,75), plus foncée en marche
            // (rouge 200,0,0 → 150,0,0).
            btnRec.FlatAppearance.BorderColor = on ? Color.FromArgb(190, 105, 0) : Lighter(idle);
            Image old = btnRec.Image;
            btnRec.Image = RecLabel(btnRec.ClientSize, btnRec.ForeColor);
            if (old != null) old.Dispose();
            tips.SetToolTip(btnRec, on
                ? Lang.T("Arrêter l'enregistrement et sauvegarder le son", "Stop recording and save the sound")
                : Lang.T("Enregistrer le son entendu dans les haut-parleurs (clic droit : carte son du poste, couleur)", "Record the sound heard in the speakers (right-click: rig sound card, colour)"));
        }

        // Bordure d'un quart plus claire (au moins un peu pour le noir).
        static Color Lighter(Color c)
        {
            return Color.FromArgb(Math.Min(255, c.R * 5 / 4 + 15), Math.Min(255, c.G * 5 / 4 + 15), Math.Min(255, c.B * 5 / 4 + 15));
        }

        // Contour : plus clair sur un fond sombre, plus foncé sur un fond clair (sinon invisible sur blanc ou jaune).
        static Color Outline(Color c)
        {
            return IsLight(c) ? Color.FromArgb(c.R * 3 / 4, c.G * 3 / 4, c.B * 3 / 4) : Lighter(c);
        }

        // « REC » dans la police des boutons Mute, centré, sans retour à la ligne.
        Bitmap RecLabel(Size size, Color fore)
        {
            Bitmap b = new Bitmap(Math.Max(1, size.Width - 2), Math.Max(1, size.Height - 2));
            using (Graphics g = Graphics.FromImage(b))
            using (SolidBrush br = new SolidBrush(fore))
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.NoClip))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                sf.Alignment = sf.LineAlignment = StringAlignment.Center;
                g.DrawString("REC", btnRec.Font, br, new RectangleF(0, 0, b.Width, b.Height), sf);
            }
            return b;
        }

        // Bouton ▾ : les 20 dernières mémoires utilisées (nom s'il y en a un, sinon fréquence).
        void ShowRecentMemories()
        {
            BuildRecentMenu().Show(btnMemRecent, new Point(0, btnMemRecent.Height));
        }

        ContextMenuStrip BuildRecentMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            if (settings.Memories.Recent.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem(Lang.T("(aucune mémoire utilisée)", "(no memory used yet)"));
                none.Enabled = false;
                m.Items.Add(none);
            }
            foreach (Memory r in settings.Memories.Recent.ToArray())
            {
                Memory mem = r;
                ToolStripMenuItem it = new ToolStripMenuItem(mem.Label);
                it.ToolTipText = Memory.KHz(mem.Hz) + " kHz" + (mem.Mode != "" ? "  " + Modes.Display(mem.Mode) : "");
                it.Click += delegate { TuneMemory(mem); };
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem open = new ToolStripMenuItem(Lang.T("Mémoires…", "Memories…"));
            open.Click += delegate { ShowMemories(); };
            m.Items.Add(open);
            ToolStripMenuItem clear = new ToolStripMenuItem(Lang.T("Effacer cette liste", "Clear this list"));
            clear.Enabled = settings.Memories.Recent.Count > 0;
            clear.Click += delegate { settings.Memories.Recent.Clear(); settings.Save(); };
            m.Items.Add(clear);
            return m;
        }

        // ---------------------------------------------------------------- Taille du texte

        // Tailles d'origine (100 %) des polices et des contrôles de taille fixe, relevées au premier changement.
        readonly Dictionary<Control, Font> baseFonts = new Dictionary<Control, Font>();
        readonly Dictionary<Control, Size> baseSizes = new Dictionary<Control, Size>();
        readonly Dictionary<Control, int> baseWidths = new Dictionary<Control, int>();
        readonly Dictionary<ToolStripItem, Font> baseItemFonts = new Dictionary<ToolStripItem, Font>();
        Font baseFormFont;

        void CaptureBaseSizes(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is WebView2) continue;
                baseFonts[c] = c.Font;
                // Zones de saisie et listes : la hauteur suit la police, seule la largeur est agrandie.
                if (c is TextBoxBase || c is ComboBox || c is UpDownBase) baseWidths[c] = c.Width;
                else if (!c.AutoSize && c.Dock == DockStyle.None && !(c is FlowLayoutPanel) && !(c is TableLayoutPanel)) baseSizes[c] = c.Size;
                ToolStrip ts = c as ToolStrip;
                if (ts != null) foreach (ToolStripItem i in ts.Items) baseItemFonts[i] = i.Font;
                CaptureBaseSizes(c);
            }
        }

        // Taille du texte de la fenêtre principale (100 % = taille d'origine) ; les boutons et curseurs de taille fixe suivent.
        void ApplyFontScale(int percent)
        {
            if (baseFormFont == null) { baseFormFont = Font; CaptureBaseSizes(this); }
            float f = percent / 100f;
            SuspendLayout();
            Font = new Font(baseFormFont.FontFamily, baseFormFont.Size * f, baseFormFont.Style);
            foreach (KeyValuePair<Control, Font> kv in baseFonts) kv.Key.Font = new Font(kv.Value.FontFamily, kv.Value.Size * f, kv.Value.Style);
            foreach (KeyValuePair<ToolStripItem, Font> kv in baseItemFonts) kv.Key.Font = new Font(kv.Value.FontFamily, kv.Value.Size * f, kv.Value.Style);
            foreach (KeyValuePair<Control, Size> kv in baseSizes)
            {
                // Taille minimale égale à la taille (bouton ⇄) : levée le temps du changement, sinon il ne rapetisserait pas.
                bool fixedMin = kv.Key.MinimumSize.Width > 0;
                if (fixedMin) kv.Key.MinimumSize = Size.Empty;
                kv.Key.Size = new Size((int)Math.Round(kv.Value.Width * f), (int)Math.Round(kv.Value.Height * f));
                if (fixedMin) kv.Key.MinimumSize = kv.Key.Size;
            }
            foreach (KeyValuePair<Control, int> kv in baseWidths) kv.Key.Width = (int)Math.Round(kv.Value * f);
            ResumeLayout(true);
            SizeFreqLabels();
            // Affichages réduit et mini : la ligne PTT reste entière ; en mini, la hauteur de la fenêtre suit celle des lignes.
            if (viewShown != 0) KeepRowWhole();
            if (viewShown == 2) FitMiniHeight();
            if (viewShown != 0) FitOnScreen();
        }

        // Petite fenêtre « Taille du texte » : 70 à 200 %, appliquée en direct ; Annuler revient à la taille d'avant.
        void AskFontScale()
        {
            int before = settings.FontScale;
            using (Form d = new Form())
            {
                d.Text = Lang.T("Taille du texte", "Text size");
                d.FormBorderStyle = FormBorderStyle.FixedDialog;
                d.MaximizeBox = d.MinimizeBox = false;
                d.ShowInTaskbar = false;
                d.StartPosition = FormStartPosition.CenterParent;
                d.Font = new Font("Segoe UI", 9f);
                d.ClientSize = new Size(420, 132);
                Label l = new Label { Text = Lang.T("Taille du texte de la fenêtre (100 % = taille d'origine) :", "Window text size (100% = original size):"), Location = new Point(12, 14), AutoSize = true };
                TrackBar tb = new TrackBar { Minimum = Settings.FontScaleMin / 5, Maximum = Settings.FontScaleMax / 5, TickFrequency = 2, SmallChange = 1, LargeChange = 2, Value = before / 5 };
                tb.SetBounds(46, 38, 270, 40);
                Button minus = new Button { Text = "−" }, plus = new Button { Text = "+" }, reset = new Button { Text = "100 %" };
                minus.SetBounds(12, 40, 30, 28);
                plus.SetBounds(320, 40, 30, 28);
                Label value = new Label { Location = new Point(356, 46), AutoSize = true };
                reset.SetBounds(12, 90, 80, 28);
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = Lang.T("Annuler", "Cancel"), DialogResult = DialogResult.Cancel };
                ok.SetBounds(240, 90, 80, 28);
                cancel.SetBounds(326, 90, 80, 28);
                d.AcceptButton = ok;
                d.CancelButton = cancel;
                Action show = delegate { value.Text = tb.Value * 5 + " %"; };
                tb.ValueChanged += delegate { show(); ApplyFontScale(tb.Value * 5); };
                minus.Click += delegate { tb.Value = Math.Max(tb.Minimum, tb.Value - 1); };
                plus.Click += delegate { tb.Value = Math.Min(tb.Maximum, tb.Value + 1); };
                reset.Click += delegate { tb.Value = 20; };
                show();
                d.Controls.AddRange(new Control[] { l, minus, tb, plus, value, reset, ok, cancel });
                Theme.Apply(d);
                if (d.ShowDialog(this) == DialogResult.OK) settings.FontScale = tb.Value * 5;
                else if (tb.Value * 5 != before) ApplyFontScale(before);
            }
            Log.Write("Taille du texte : " + settings.FontScale + " %");
            settings.Save();
        }

        // ---------------------------------------------------------------- Barre d'état et historique des messages

        // 20 derniers messages (le plus récent en tête) ; un type de message masqué reste dans l'historique
        // mais ne s'affiche plus dans la barre.
        const int HistorySize = 20;
        readonly List<DateTime> historyTimes = new List<DateTime>();
        readonly List<string> historyTexts = new List<string>();

        void ShowStatus(string text)
        {
            text = text ?? "";
            if (text.Length > 0)
            {
                if (historyTexts.Count > 0 && historyTexts[0] == text) historyTimes[0] = DateTime.Now;   // message répété
                else
                {
                    historyTimes.Insert(0, DateTime.Now);
                    historyTexts.Insert(0, text);
                    if (historyTexts.Count > HistorySize) { historyTimes.RemoveAt(HistorySize); historyTexts.RemoveAt(HistorySize); }
                }
                if (settings.HiddenMessages.Contains(MessageKey(text))) return;
            }
            status.Text = text;
        }

        // Type d'un message : son texte sans les nombres ni les noms entre guillemets (fréquences, appareils, favoris…).
        public static string MessageKey(string text)
        {
            string k = Regex.Replace(text ?? "", "«[^»]*»|\"[^\"]*\"|“[^”]*”|„[^“”]*[“”]", "«…»");
            k = Regex.Replace(k, "\\d+([.,]\\d+)*", "#");
            return k.Replace("|", "/").Trim();
        }

        void FillStatusHistory(ContextMenuStrip m)
        {
            m.Items.Clear();
            ToolStripMenuItem title = new ToolStripMenuItem(Lang.F("{0} derniers messages (clic : masquer ou réafficher ce type de message, ✓ = masqué)",
                "Last {0} messages (click: hide or show this kind of message again, ✓ = hidden)", HistorySize));
            title.Enabled = false;
            m.Items.Add(title);
            if (historyTexts.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem(Lang.T("(aucun message)", "(no message)"));
                none.Enabled = false;
                m.Items.Add(none);
            }
            for (int i = 0; i < historyTexts.Count; i++)
            {
                string text = historyTexts[i], key = MessageKey(text);
                ToolStripMenuItem it = new ToolStripMenuItem(historyTimes[i].ToString("HH:mm:ss") + "   " + (text.Length > 140 ? text.Substring(0, 137) + "…" : text));
                it.Checked = settings.HiddenMessages.Contains(key);
                it.ToolTipText = text;
                it.Click += delegate { ToggleHiddenMessage(key); };
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem showAll = new ToolStripMenuItem(Lang.F("Réafficher tous les messages masqués ({0})", "Show all hidden messages again ({0})", settings.HiddenMessages.Count));
            showAll.Enabled = settings.HiddenMessages.Count > 0;
            showAll.Click += delegate
            {
                settings.HiddenMessages.Clear();
                settings.Save();
                Log.Write("Barre d'état : tous les messages réaffichés");
            };
            m.Items.Add(showAll);
        }

        void ToggleHiddenMessage(string key)
        {
            if (settings.HiddenMessages.Remove(key)) Log.Write("Barre d'état : messages réaffichés [" + key + "]");
            else
            {
                settings.HiddenMessages.Add(key);
                Log.Write("Barre d'état : messages masqués [" + key + "]");
                if (MessageKey(status.Text) == key) status.Text = "";
            }
            settings.Save();
        }

        // ---------------------------------------------------------------- Mise à jour

        // Au démarrage (case cochée) : dernière version publiée sur GitHub ; si elle est plus récente, lien dans la barre d'état.
        void CheckForUpdate()
        {
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                string tag = null, url = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/F5pbgLudovic/SDRLink/releases/latest");
                    req.Timeout = 8000;
                    req.UserAgent = "SDRLink/" + Program.Version + " (F5PBG)";
                    req.Accept = "application/vnd.github+json";
                    string json;
                    using (WebResponse resp = req.GetResponse())
                    using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = r.ReadToEnd();
                    Match m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9][0-9.]*)\"");
                    if (m.Success) tag = m.Groups[1].Value.TrimEnd('.');
                    m = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github\\.com/[^\"]+/releases/tag/[^\"]+)\"");
                    url = m.Success ? m.Groups[1].Value : "https://github.com/F5pbgLudovic/SDRLink/releases/latest";
                }
                catch (Exception ex) { Log.Write("Mise à jour : vérification impossible (" + ex.Message + ")"); }
                if (tag == null) return;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (!IsNewer(tag, Program.Version)) { Log.Write("Mise à jour : V" + Program.Version + " est la dernière version"); return; }
                        Log.Write("Mise à jour : V" + tag + " disponible");
                        updateVersion = tag;
                        updateUrl = url;
                        lblUpdate.Text = Lang.F("⚠ Mise à jour V{0} disponible : cliquez pour la télécharger", "⚠ Update V{0} available: click to download it", updateVersion);
                        lblUpdate.Visible = true;
                        StyleUpdateLabel();
                    });
                }
                catch (InvalidOperationException) { }   // fenêtre fermée entre-temps
            });
            t.IsBackground = true;
            t.Start();
        }

        static bool IsNewer(string latest, string current)
        {
            Version a, b;
            if (!Version.TryParse(latest.Contains(".") ? latest : latest + ".0", out a)) return false;
            if (!Version.TryParse(current.Contains(".") ? current : current + ".0", out b)) return false;
            return a > b;
        }

        void OpenUpdatePage()
        {
            try { System.Diagnostics.Process.Start(updateUrl ?? "https://github.com/F5pbgLudovic/SDRLink/releases/latest"); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "SDRLink"); }
        }

        void StyleUpdateLabel()
        {
            if (lblUpdate == null) return;
            lblUpdate.LinkColor = lblUpdate.ActiveLinkColor = lblUpdate.VisitedLinkColor = Theme.Dark ? Color.FromArgb(255, 170, 60) : Color.FromArgb(200, 80, 0);
        }
        void ManageFavorites()
        {
            using (FavoritesDialog d = new FavoritesDialog(settings.Favorites, CurrentPage, settings.ListNames, settings.CurrentList, delegate(string url) { Navigate(url); }))
            {
                d.ShowDialog(this);
                FillFavListCombo();
                RefillUrlCombo();
                // Favoris modifiés (supprimé, adresse changée...) : on retrouve celui de la page affichée.
                if (web.CoreWebView2 != null) OnPageChanged(web.CoreWebView2.Source);
                if (d.ToOpen != null) Navigate(d.ToOpen.Url);
            }
            SaveUiToSettings();
            settings.Save();
        }
    }
}
