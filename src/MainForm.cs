using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SDRLink
{
    public class MainForm : Form
    {
        const long Tolerance = 10;        // Hz : écart en dessous duquel on considère les fréquences égales
        const int EchoIgnoreMs = 800;     // durée pendant laquelle on ignore le retour d'un ordre qu'on vient d'envoyer

        enum Direction { Both, RigToSdr, SdrToRig, Off }

        readonly Settings settings;
        bool loading;
        // Mise à jour des textes de l'interface quand on change de langue.
        readonly List<Action> translators = new List<Action>();

        ComboBox cbInterface, cbBrand, cbModel, cbPort, cbBaud, cbStop, cbOmni, cbDirection, cbUrl, cbHrdTx, cbLang;
        TextBox txtCiv, txtHost;
        NumericUpDown numNetPort, numOffset, numUnmute;
        CheckBox chkRts, chkDtr, chkMode, chkAuto, chkMute, chkCompact, chkTop, chkDark;
        TrackBar trkVolume, trkRigVolume;
        Label lblVolume, lblRigVolume;
        PaletteButton btnPalette;
        ComboBox cbFavList;
        Button btnMuteSdr, btnMuteRig;
        bool sdrMuted, rigMuted;
        int rigAfSupported = -1;    // -1 inconnu (pas connecté), 0 non pris en charge, 1 disponible
        bool rigVolumeLoading;
        Favorite currentFav;        // favori correspondant à la page affichée (porte son volume), null sinon
        bool volumeLoading;
        FlowLayoutPanel pnlModel, pnlCom, pnlOmni, pnlNet, pnlHrdTx, rowDisplay, rowUrl;
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
        static readonly string[] ModeButtonModes = { Modes.USB, Modes.LSB, Modes.AM, Modes.FM };
        string shownModeFamily;     // mode mis en surbrillance sur les boutons
        static readonly long[] Steps = { 100, 250, 500, 1000, 2500, 5000, 10000 };
        long wheelTarget;           // fréquence poste visée par la molette
        DateTime wheelLast, wheelActiveUntil;
        int wheelRemainder;         // molettes "haute résolution" : fractions de cran accumulées
        Size normalSize;            // taille de la fenêtre avant passage en affichage réduit
        bool compactPending;
        bool wasMaximized;          // plein écran avant le passage en affichage réduit

        RigWorker worker;
        bool rigOnline;
        long rigF; string rigM;
        long sdrF; string sdrM; string sdrKind = "";
        long lastRigF; string lastRigFam;
        long lastSdrF; string lastSdrFam;
        DateTime ignoreRigUntil, ignoreSdrUntil;

        public MainForm()
        {
            settings = Settings.Load();
            Lang.English = settings.Language == "en";
            Text = "SDRLink V" + Program.Version + " @F5PBG 2026 - WebSDR ⇄ Transceiver";
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
            syncTimer.Tick += delegate { CheckSync(); CheckPttTimeout(); };
            syncTimer.Start();
            LoadSettingsIntoUi();
            Load += delegate
            {
                InitWebView();
                if (settings.AutoConnect) Connect();
            };
            Shown += delegate { ApplyCompact(); };
            Application.AddMessageFilter(new WheelFilter(this));
            Resize += delegate { if (compactPending && WindowState != FormWindowState.Minimized) ApplyCompact(); };
            FormClosing += delegate { Disconnect(); SaveUiToSettings(); SaveWindowBounds(); settings.Save(); };
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
                if (settings.Compact) wasMaximized = true;
                else WindowState = FormWindowState.Maximized;
            }
        }

        void SaveWindowBounds()
        {
            Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            // En affichage réduit, la taille à retenir est celle d'avant la réduction.
            Size normal = !web.Visible ? (normalSize.IsEmpty ? DefaultWindowSize() : normalSize) : b.Size;
            settings.WinX = b.X;
            settings.WinY = b.Y;
            settings.WinW = normal.Width;
            settings.WinH = normal.Height;
            settings.WinMax = WindowState == FormWindowState.Maximized || (!web.Visible && wasMaximized);
        }

        // ---------------------------------------------------------------- Interface

        void BuildUi()
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.ColumnCount = 1;
            table.RowCount = 6;
            for (int i = 0; i < 5; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Ligne 1 : interface, marque, modèle, connexion
            FlowLayoutPanel row1 = Row();
            cbInterface = Combo(170, new[] { "Port COM (CAT direct)", "OmniRig", "TCI", "Ham Radio Deluxe" },
                                     new[] { "COM port (direct CAT)", "OmniRig", "TCI", "Ham Radio Deluxe" });
            cbInterface.SelectedIndexChanged += delegate { UpdateInterfacePanels(); };
            row1.Controls.Add(Lbl("Interface :", "Interface:"));
            row1.Controls.Add(cbInterface);

            pnlModel = SubRow();
            cbBrand = Combo(90, RigCatalog.Brands);
            cbBrand.SelectedIndexChanged += delegate { FillModels(); };
            cbModel = Combo(130);
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
            row1.Controls.Add(Btn("Ouvrir le journal", "Open log", delegate
            {
                try { System.Diagnostics.Process.Start("notepad.exe", "\"" + Log.FilePath + "\""); }
                catch (Exception ex) { MessageBox.Show(Lang.T("Impossible d'ouvrir le journal : ", "Cannot open the log: ") + ex.Message); }
            }));
            chkDark = Check("Mode sombre", "Dark mode");
            chkDark.CheckedChanged += delegate { if (!loading) ApplyTheme(); };
            row1.Controls.Add(chkDark);
            btnPalette = new PaletteButton();
            btnPalette.Click += delegate { ShowPalette(); };
            row1.Controls.Add(btnPalette);

            // Ligne 2 : paramètres propres à l'interface choisie
            FlowLayoutPanel row2 = Row();
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

            cbLang = Combo(85, "Français", "English");
            cbLang.SelectedIndexChanged += delegate
            {
                if (loading) return;
                Lang.English = cbLang.SelectedIndex == 1;
                settings.Language = Lang.English ? "en" : "fr";
                ApplyLanguage();
            };
            row2.Controls.Add(Lbl("Langue :", "Language:"));
            row2.Controls.Add(cbLang);

            // Ligne 3 : synchronisation
            FlowLayoutPanel row3 = Row();
            cbDirection = Combo(160, new[] { "Bidirectionnelle", "Poste → WebSDR", "WebSDR → Poste", "Désactivée" },
                                     new[] { "Both ways", "Rig → WebSDR", "WebSDR → Rig", "Off" });
            // Passage en "Bidirectionnelle" ou "Poste → WebSDR" : le WebSDR est recalé tout de suite sur le poste.
            cbDirection.SelectedIndexChanged += delegate
            {
                if (loading) return;
                Log.Write("Synchro : " + cbDirection.Text);
                if (RigToSdr && rigOnline) PushToSdr();
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

            // Ligne 4 : PTT, fréquences, modes, saisie ; l'affichage réduit coupe la fenêtre juste après cette ligne.
            rowDisplay = Row();
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
                b.Text = Modes.Display(mode);
                b.Size = new Size(44, 30);
                b.Margin = new Padding(i == 0 ? 8 : 2, 5, 0, 0);
                b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 75);
                b.Tag = Theme.Custom;
                b.Click += delegate { SetModeBoth(mode); };
                modeButtons[i] = b;
                rowDisplay.Controls.Add(b);
            }

            // Saisie d'une fréquence en kHz, avec l'historique des valeurs précédentes.
            cbFreq = new ComboBox();
            cbFreq.DropDownStyle = ComboBoxStyle.DropDown;
            cbFreq.Width = 85;
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
            chkCompact = Check("Affichage réduit", "Compact view");
            chkCompact.CheckedChanged += delegate { if (!loading) ApplyCompact(); };
            chkTop = Check("Avant plan", "Always on top");
            chkTop.CheckedChanged += delegate { TopMost = chkTop.Checked; };
            rowDisplay.Controls.Add(Lbl("kHz :", "kHz:"));
            rowDisplay.Controls.Add(cbFreq);
            rowDisplay.Controls.Add(Btn("QSY", "QSY", delegate { TuneFromEntry(); }));
            rowDisplay.Controls.Add(Btn("Save", "Save", delegate
            {
                long f = CurrentFreq();
                if (f > 0) AddToHistory(f);
                else status.Text = Lang.T("Save : aucune fréquence connue (poste ou WebSDR)", "Save: no known frequency (rig or WebSDR)");
            }));
            rowDisplay.Controls.Add(Lbl("Pas :", "Step:"));
            rowDisplay.Controls.Add(cbStep);
            // Les deux cases l'une sous l'autre : elles tiennent dans la hauteur du bouton PTT.
            FlowLayoutPanel views = SubRow();
            views.FlowDirection = FlowDirection.TopDown;
            chkCompact.Margin = new Padding(8, 2, 2, 0);
            chkTop.Margin = new Padding(8, 2, 2, 0);
            views.Controls.Add(chkCompact);
            views.Controls.Add(chkTop);
            rowDisplay.Controls.Add(views);

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
                settings.CurrentList = Math.Max(0, cbFavList.SelectedIndex);
                RefillUrlCombo();
            };
            ContextMenuStrip listMenu = new ContextMenuStrip();
            ToolStripMenuItem renameList = new ToolStripMenuItem();
            listMenu.Items.Add(renameList);
            listMenu.Opening += delegate
            {
                renameList.Text = Lang.T("Renommer « ", "Rename \"") + FavoritesDialog.ListLabel(settings.ListNames, settings.CurrentList) + Lang.T(" »…", "\"…");
            };
            renameList.Click += delegate { RenameFavoriteList(); };
            cbFavList.ContextMenuStrip = listMenu;
            rowUrl.Controls.Add(cbFavList);
            rowUrl.Controls.Add(Btn("Aller", "Go", delegate { NavigateFromBar(); }));
            rowUrl.Controls.Add(Btn("◀ Retour", "◀ Back", delegate { if (web.CoreWebView2 != null && web.CanGoBack) web.GoBack(); }));
            rowUrl.Controls.Add(Btn("Recharger", "Reload", delegate { if (web.CoreWebView2 != null) web.Reload(); }));
            rowUrl.Controls.Add(Btn("★ Ajouter aux favoris", "★ Add to favorites", delegate { AddFavorite(); }));
            rowUrl.Controls.Add(Btn("Retirer des favoris", "Remove from favorites", delegate { RemoveCurrentFavorite(); }));
            rowUrl.Controls.Add(Btn("Gérer les favoris…", "Manage favorites…", delegate { ManageFavorites(); }));
            rowUrl.Controls.Add(Btn("Rechercher des WebSDR…", "Find WebSDRs…", delegate { SearchWebSdrs(); }));

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
            audio.Controls.Add(Lbl("Volume :", "Volume:"));
            audio.Controls.Add(trkVolume);
            audio.Controls.Add(lblVolume);
            rowUrl.Controls.Add(audio);

            // Coupures du son : WebSDR (dans la page) et poste (gain BF mis à zéro puis rétabli), et volume du poste.
            btnMuteSdr = Btn("Mute WebSDR", "Mute WebSDR", delegate { sdrMuted = !sdrMuted; ApplyVolume(); UpdateMuteButtons(); });
            btnMuteRig = Btn("Mute TX", "Mute TRX", delegate { ToggleRigMute(); });
            btnMuteSdr.Tag = btnMuteRig.Tag = Theme.Custom;
            btnMuteSdr.FlatStyle = btnMuteRig.FlatStyle = FlatStyle.Flat;
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
            audio.Controls.Add(btnMuteSdr);
            audio.Controls.Add(btnMuteRig);
            audio.Controls.Add(Lbl("Volume TX :", "TRX volume:"));
            audio.Controls.Add(trkRigVolume);
            audio.Controls.Add(lblRigVolume);

            web = new WebView2();
            web.Dock = DockStyle.Fill;

            table.Controls.Add(row1, 0, 0);
            table.Controls.Add(row2, 0, 1);
            table.Controls.Add(row3, 0, 2);
            table.Controls.Add(rowDisplay, 0, 3);
            table.Controls.Add(rowUrl, 0, 4);
            table.Controls.Add(web, 0, 5);

            strip = new StatusStrip();
            status = new ToolStripStatusLabel();
            strip.Items.Add(status);

            Controls.Add(table);
            Controls.Add(strip);
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
                c.Items.AddRange(Lang.English ? en : fr);
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
            lblRig.BackColor = lblSdr.BackColor = Theme.Back;
            lblSdr.ForeColor = Theme.SdrFreq;
            lblRig.ForeColor = Transmitting ? Theme.TxFreq : Theme.RigFreq;
            shownModeFamily = null;     // force la remise en couleur des boutons de mode
            ResumeLayout();
            btnPalette.Invalidate();
            UpdatePttButton();
            UpdateMuteButtons();
            UpdateDisplays();
        }

        // Largeur fixe des fréquences, calculée sur une fréquence HF avec le mode le plus long : les boutons à droite
        // ne bougent plus quand le mode change (USB, AM...). Une fréquence plus longue (VHF, GHz) élargit la zone une fois.
        void SizeFreqLabels()
        {
            FitWidth(lblRig, Lang.T("POSTE", "RIG") + " 14.074.000 RTTY-R", true);
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
            if (worker == null) status.Text = Lang.T("Déconnecté", "Disconnected");
            UpdateDisplays();
            if (chkCompact.Checked && web.Visible == false)
            {
                // Les textes ont changé de longueur : on recalcule la hauteur de l'affichage réduit.
                chkCompact.Checked = false;
                ApplyCompact();
                chkCompact.Checked = true;
                ApplyCompact();
            }
        }

        void UpdateConnectButton()
        {
            btnConnect.Text = worker == null ? Lang.T("Connecter", "Connect") : Lang.T("Déconnecter", "Disconnect");
        }

        void UpdateInterfacePanels()
        {
            int i = cbInterface.SelectedIndex;
            pnlModel.Visible = pnlCom.Visible = i == 0;
            pnlOmni.Visible = i == 1;
            pnlNet.Visible = i == 2 || i == 3;
            pnlHrdTx.Visible = i == 3;
            if (!loading && (i == 2 || i == 3))
            {
                txtHost.Text = i == 2 ? settings.TciHost : settings.HrdHost;
                numNetPort.Value = i == 2 ? settings.TciPort : settings.HrdPort;
            }
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
            cbLang.SelectedIndex = Lang.English ? 1 : 0;
            cbInterface.SelectedIndex = Math.Max(0, Math.Min(3, settings.Interface));
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
            bool tci = settings.Interface == 2;
            txtHost.Text = tci ? settings.TciHost : settings.HrdHost;
            numNetPort.Value = tci ? settings.TciPort : settings.HrdPort;
            chkAuto.Checked = settings.AutoConnect;
            cbHrdTx.SelectedIndex = Math.Max(0, Math.Min(cbHrdTx.Items.Count - 1, settings.HrdTxDetect));
            foreach (string f in settings.FreqHistory) cbFreq.Items.Add(f);
            int stepIndex = Array.IndexOf(Steps, settings.TuneStepHz);
            cbStep.SelectedIndex = stepIndex >= 0 ? stepIndex : 0;
            chkCompact.Checked = settings.Compact;
            chkTop.Checked = settings.TopMost;
            chkDark.Checked = settings.DarkMode;
            trkVolume.Value = settings.DefaultVolume;
            chkMute.Checked = settings.MuteOnTx;
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
            if (settings.Interface == 2) { settings.TciHost = txtHost.Text.Trim(); settings.TciPort = (int)numNetPort.Value; }
            if (settings.Interface == 3) { settings.HrdHost = txtHost.Text.Trim(); settings.HrdPort = (int)numNetPort.Value; }
            settings.AutoConnect = chkAuto.Checked;
            settings.HrdTxDetect = cbHrdTx.SelectedIndex;
            settings.Language = Lang.English ? "en" : "fr";
            settings.TuneStepHz = Steps[Math.Max(0, cbStep.SelectedIndex)];
            settings.FreqHistory.Clear();
            foreach (object o in cbFreq.Items) settings.FreqHistory.Add(o.ToString());
            settings.Compact = chkCompact.Checked;
            settings.TopMost = chkTop.Checked;
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

        void Disconnect()
        {
            if (worker == null) return;
            RigWorker w = worker;
            // Son du poste coupé par SDRLink : on le rétablit avant de lâcher la liaison.
            if (rigMuted) w.SetAfGain(trkRigVolume.Value);
            worker = null;
            w.Stop();
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
            status.Text = Lang.T("Déconnecté", "Disconnected");
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
            status.Text = msg;
            UpdateDisplays();
        }

        // Affichage réduit : la fenêtre s'arrête juste après la ligne PTT/fréquences (barre d'adresse et page masquées).
        // Le WebSDR continue de jouer et de suivre le poste.
        void ApplyCompact()
        {
            bool compact = chkCompact.Checked;
            if (compact == !web.Visible) return;
            // Fenêtre réduite dans la barre des tâches : Windows ignore le redimensionnement, on attend sa réapparition.
            if (WindowState == FormWindowState.Minimized) { compactPending = true; return; }
            compactPending = false;
            if (compact)
            {
                // Fenêtre en plein écran : on le retient pour y revenir en quittant l'affichage réduit.
                // (remis à zéro seulement au retour en affichage normal : au démarrage en mode réduit,
                // il vient des réglages enregistrés).
                if (WindowState == FormWindowState.Maximized)
                {
                    wasMaximized = true;
                    WindowState = FormWindowState.Normal;
                }
                normalSize = Size;
                rowUrl.Visible = web.Visible = false;
                PerformLayout();
                int h = SizeFromClientSize(new Size(ClientSize.Width, rowDisplay.Bottom + strip.Height + 4)).Height;
                MaximumSize = new Size(10000, h);
                Height = h;
            }
            else
            {
                MaximumSize = Size.Empty;
                rowUrl.Visible = web.Visible = true;
                Size = normalSize.IsEmpty ? DefaultWindowSize() : normalSize;
                if (wasMaximized) WindowState = FormWindowState.Maximized;
                wasMaximized = false;
            }
        }

        // ---------------------------------------------------------------- Coupure du son en émission

        // Bouton PTT : bascule émission/réception, comme le MOX de HRD. Le son est coupé tout de suite,
        // sans attendre que le poste confirme l'émission.
        // Clic droit sur PTT : délai de sécurité au bout duquel une émission lancée par le bouton est coupée.
        static readonly int[] PttTimeouts = { 0, 30, 60, 120, 180, 300, 600 };

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
                string msg = Lang.T("Émission coupée automatiquement après ", "Transmission stopped automatically after ")
                    + TimeoutLabel(settings.PttTimeoutSec) + Lang.T(" (délai de sécurité, clic droit sur PTT pour le régler)", " (safety timeout, right-click PTT to change it)");
                status.Text = msg;
                Log.Write(msg);
                return;
            }
            UpdatePttButton();
        }

        void SetPttButton(bool on)
        {
            if (on && (worker == null || !rigOnline)) { status.Text = Lang.T("PTT : connectez d'abord le poste", "PTT: connect the rig first"); return; }
            if (on) pttSince = DateTime.UtcNow;
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
                if (!form.IsOver(form.lblRig, p) && !form.IsOver(form.lblSdr, p)) return false;
                form.TuneByWheel((short)(((long)m.WParam >> 16) & 0xFFFF));
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

        // Poste ET WebSDR bougent ensemble, quel que soit le sens de synchronisation choisi.
        void TuneByWheel(int delta)
        {
            wheelRemainder += delta;
            int notches = wheelRemainder / 120;
            if (notches == 0) return;
            wheelRemainder -= notches * 120;

            DateTime now = DateTime.UtcNow;
            double msPerNotch = (now - wheelLast).TotalMilliseconds / Math.Abs(notches);
            wheelLast = now;
            long step = Accelerate(Steps[Math.Max(0, cbStep.SelectedIndex)], msPerNotch);

            // Pendant qu'on tourne, on part de la dernière cible plutôt que de la fréquence relue, qui a du retard.
            long baseF = WheelActive && wheelTarget > 0 ? wheelTarget : CurrentFreq();
            if (baseF <= 0) return;
            TuneBoth((long)Math.Round((double)baseF / step) * step + notches * step);
        }

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
                status.Text = Lang.T("Fréquence invalide : tapez une valeur en kHz, par ex. 7074 ou 14074.5",
                                     "Invalid frequency: type a value in kHz, e.g. 7074 or 14074.5");
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
                    ? Lang.T("Supprimer « " + k + " » de la liste", "Remove \"" + k + "\" from the list")
                    : Lang.T("Supprimer cette valeur (choisissez-la d'abord dans la liste)", "Remove this value (pick it in the list first)");
                removeOne.Enabled = present;
                removeAll.Text = Lang.T("Tout supprimer (" + cbFreq.Items.Count + " valeurs)", "Remove all (" + cbFreq.Items.Count + " values)");
                removeAll.Enabled = cbFreq.Items.Count > 0;
            };
            removeOne.Click += delegate { cbFreq.Items.Remove(cbFreq.Text.Trim()); cbFreq.Text = ""; };
            removeAll.Click += delegate
            {
                if (MessageBox.Show(Lang.T("Supprimer les " + cbFreq.Items.Count + " fréquences de la liste ?",
                                           "Remove all " + cbFreq.Items.Count + " frequencies from the list?"), "SDRLink",
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
                // Page qui vient de se charger : le poste impose sa fréquence s'il est connecté.
                if (RigToSdr && rigOnline && rigF > 0) PushToSdr();
                else if (Dir == Direction.SdrToRig) PushToRig(true, true);
                return;
            }
            if (DateTime.UtcNow < ignoreSdrUntil) return;
            if ((freqChanged || modeChanged) && SdrToRig) PushToRig(freqChanged, modeChanged);
        }

        // Le poste fait foi : en "Bidirectionnelle" ou "Poste → WebSDR", un WebSDR resté décalé de plus de 3 s
        // (réglage perdu, page lente, clic dans la cascade en mode "Poste → WebSDR") est recalé sur le poste.
        void CheckSync()
        {
            bool diverged = worker != null && rigOnline && RigToSdr && sdrKind != "" && rigF > 0 && !WheelActive
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
            long shownSdr = wheel ? wheelTarget + Offset : sdrF;
            SetFreqText(lblRig, Lang.T("POSTE ", "RIG ") + (worker != null && rigF > 0 ? FormatFreq(shownRig) + " " + Modes.Display(rigM) : "---"));
            // L'émission est signalée par la couleur (et le bouton PTT) : la largeur du texte ne change pas.
            Color c = Transmitting ? Theme.TxFreq : Theme.RigFreq;
            if (lblRig.ForeColor != c) lblRig.ForeColor = c;
            // Mode WebSDR en forme courte (USB, LSB, CW, AM, FM) : le mode exact est montré par les boutons.
            string fam = Modes.SdrFamily(sdrM);
            string sdrMode = fam != "" ? fam.ToUpperInvariant() : (sdrM ?? "").ToUpperInvariant();
            SetFreqText(lblSdr, "WEBSDR " + (sdrKind != "" ? FormatFreq(shownSdr) + " " + sdrMode : "---"));

            // Bouton de mode en surbrillance : mode du poste s'il est connecté, sinon celui du WebSDR.
            string active = worker != null && rigM != null ? Modes.ToSdr(rigM) : fam;
            if (active != shownModeFamily)
            {
                shownModeFamily = active;
                for (int i = 0; i < modeButtons.Length; i++)
                {
                    bool on = Modes.ToSdr(ModeButtonModes[i]) == active;
                    modeButtons[i].BackColor = on ? Color.FromArgb(0, 120, 60) : Theme.IdleButton;
                    modeButtons[i].ForeColor = on ? Color.White : Theme.ModeIdleText;
                }
            }
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
                core.DOMContentLoaded += delegate { ApplyVolume(); };
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
            catch (ArgumentException) { status.Text = Lang.T("Adresse invalide : ", "Invalid address: ") + url; }
        }

        // Le texte de la barre peut être une adresse, ou le nom d'un favori choisi dans la liste.
        void NavigateFromBar()
        {
            string text = cbUrl.Text.Trim();
            Favorite f = settings.Favorites.Find(delegate(Favorite x) { return x.Name == text; });
            Navigate(f != null ? f.Url : text);
        }

        // ---------------------------------------------------------------- Volume du WebSDR

        // Nouvelle page : on reprend le volume mémorisé de ce favori (ou le volume des pages hors favoris).
        void OnPageChanged(string url)
        {
            currentFav = settings.Favorites.Find(delegate(Favorite f) { return f.Matches(url); });
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
            if (worker == null) { status.Text = Lang.T("Mute TX : connectez d'abord le poste", "Mute TRX: connect the rig first"); return; }
            if (rigAfSupported != 1)
            {
                status.Text = Lang.T("Volume du poste non disponible avec ce poste ou cette interface (OmniRig, HRD, FT-817/818…)",
                                     "Rig volume not available with this rig or interface (OmniRig, HRD, FT-817/818…)");
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
            lblRigVolume.Text = on ? trkRigVolume.Value + " %" : "—";
            UpdateMuteButtons();
        }

        void UpdateMuteButtons()
        {
            StyleToggle(btnMuteSdr, sdrMuted, true);
            StyleToggle(btnMuteRig, rigMuted, worker != null && rigAfSupported == 1);
        }

        // Bouton de coupure : rouge quand le son est coupé ; grisé à la main (pas désactivé, pour rester lisible).
        static void StyleToggle(Button b, bool active, bool available)
        {
            b.BackColor = active ? Color.FromArgb(200, 0, 0) : Theme.ButtonBack;
            b.ForeColor = active ? Color.White : available ? Theme.Fore : Color.FromArgb(140, 140, 140);
            b.FlatAppearance.BorderColor = active ? Color.FromArgb(150, 0, 0) : Theme.Border;
        }

        // ---------------------------------------------------------------- Favoris

        // Liste déroulante des WebSDR : seulement les favoris de la liste choisie.
        void RefillUrlCombo()
        {
            string text = cbUrl.Text;
            cbUrl.BeginUpdate();
            cbUrl.Items.Clear();
            foreach (Favorite f in settings.Favorites)
                if (f.List == settings.CurrentList) cbUrl.Items.Add(f);
            cbUrl.EndUpdate();
            cbUrl.Text = text;
        }

        // Noms des listes (ils changent quand on les renomme ou qu'on change de langue).
        void FillFavListCombo()
        {
            bool was = loading;
            loading = true;
            cbFavList.Items.Clear();
            for (int i = 0; i < Favorite.ListCount; i++) cbFavList.Items.Add(FavoritesDialog.ListLabel(settings.ListNames, i));
            cbFavList.SelectedIndex = Math.Max(0, Math.Min(Favorite.ListCount - 1, settings.CurrentList));
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
            using (FavoriteEditDialog d = new FavoriteEditDialog(Lang.T("Ajouter un favori", "Add favorite"),
                cur != null ? cur.Name : "", cur != null ? cur.Url : cbUrl.Text.Trim()))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                // Le nouveau favori garde le volume actuel ; s'il s'agit de la page affichée, il devient le favori courant.
                Favorite f = new Favorite(d.FavName, d.FavUrl);
                f.Volume = trkVolume.Value;
                f.List = settings.CurrentList;
                settings.Favorites.Add(f);
                if (web.CoreWebView2 != null && f.Matches(web.CoreWebView2.Source)) currentFav = f;
                RefillUrlCombo();
            }
        }

        // Retire des favoris la page affichée.
        void RemoveCurrentFavorite()
        {
            if (currentFav == null)
            {
                status.Text = Lang.T("Cette page n'est pas dans les favoris.", "This page is not in the favorites.");
                return;
            }
            if (MessageBox.Show(Lang.T("Retirer « " + currentFav.Name + " » des favoris ?", "Remove \"" + currentFav.Name + "\" from favorites?"),
                    "SDRLink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            settings.Favorites.Remove(currentFav);
            RefillUrlCombo();
            if (web.CoreWebView2 != null) OnPageChanged(web.CoreWebView2.Source);
            SaveUiToSettings();
            settings.Save();
        }

        // Recherche dans les annuaires publics ; les récepteurs cochés sont ajoutés aux favoris.
        void SearchWebSdrs()
        {
            using (WebSdrSearchDialog d = new WebSdrSearchDialog(settings.Favorites, delegate(string url) { Navigate(url); }))
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

        void ManageFavorites()
        {
            using (FavoritesDialog d = new FavoritesDialog(settings.Favorites, CurrentPage, settings.ListNames, settings.CurrentList))
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
