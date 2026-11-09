using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace SDRLink
{
    // ---------------------------------------------------------------- Modes numériques (bouton après « FreeDV auto »)
    //
    // Un seul bouton pour tous les décodeurs repris de PhoneSDRLink (FT8, FT4, FT2, WSPR, JT9, JS8, CW, RTTY, PSK31/63, Olivia,
    // Contestia, Hell, SSTV) et WEFAX : son texte est le nom du mode choisi. Clic : marche / arrêt. Clic droit : menu des modes ;
    // un clic gauche sur un mode le choisit (et lance le décodage), un clic droit le choisit et ouvre ses réglages (seulement les
    // réglages si c'était déjà le mode choisi). Vert : en marche ; orange : réception en cours (image, ou texte décodé il y a moins
    // de 2 s). La réception décodée est celle de FreeDV (poste, sinon WebSDR, sinon ExpertSDR, sinon HDSDR). Le résultat s'affiche
    // dans une fenêtre posée sur la page (appui long puis glisser pour la déplacer) ; les images (SSTV, WEFAX, Hell à la demande)
    // sont enregistrées en PNG dans Images\SDRLink. Le son brut n'est pas coupé.
    public partial class MainForm
    {
        internal static readonly string[] DigiModes = { "FT8", "FT4", "FT2", "WSPR", "JT9", "JS8", "CW", "RTTY", "PSK", "Olivia", "Contestia", "Hell", "SSTV", "WEFAX" };

        Button btnSstv;
        SstvEngine sstv;             // SSTV (son propre passage à 8 kHz)
        DigiEngine digi;             // les autres modes
        Resampler digiRs;
        string digiRun;              // mode du décodeur en marche
        SstvView sstvView;
        bool sstvImage;              // une image est en cours de réception (SSTV, WEFAX)
        string sstvMode = "";
        int sstvRows, sstvHeight;
        int digiActivity = int.MinValue;   // Environment.TickCount du dernier résultat décodé
        readonly List<DecLine> decLines = new List<DecLine>();
        bool decNewLine = true, decDirty, hellDirty;
        System.Windows.Forms.Timer decTimer;
        DigiSettingsForm digiForm;
        bool menuRight, menuPending; // bouton de la souris enfoncé sur un mode du menu (droit ?), action pas encore faite

        internal static string SstvFolderOverride = null;   // essais : dossier à la place d'Images\SDRLink

        static string SstvFolder
        {
            get { return SstvFolderOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SDRLink"); }
        }

        string CurDigiMode { get { return Array.IndexOf(DigiModes, settings.DigiMode) >= 0 ? settings.DigiMode : "SSTV"; } }

        string DigiName(string m)
        {
            return m == "PSK" ? (settings.DigiGet("pskBaud", 31) >= 47 ? "PSK63" : "PSK31") : m;
        }

        static int ViewKind(string m) { return m == "SSTV" ? SstvView.KindSstv : m == "Hell" ? SstvView.KindHell : m == "WEFAX" ? SstvView.KindFax : SstvView.KindText; }

        bool DigiRunning { get { return sstv != null || digi != null; } }

        // ---- bouton et fenêtre

        void CreateDigiButton(Control row)
        {
            btnSstv = Btn("SSTV", "SSTV", delegate { SetSstv(!settings.SstvAuto); });
            btnSstv.Tag = Theme.Custom;
            btnSstv.FlatStyle = FlatStyle.Flat;
            ContextMenuStrip m = new ContextMenuStrip();
            m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e) { e.Cancel = false; FillSstvMenu(m); };
            m.Items.Add("-");
            btnSstv.ContextMenuStrip = m;
            Tr(delegate
            {
                UpdateSstvButton();
                tips.SetToolTip(btnSstv, Lang.T("Modes numériques : le décodage s'affiche dans une fenêtre sur la page (clic : marche / arrêt ; clic droit : choix du mode, clic droit sur un mode : ses réglages)",
                    "Digital modes: decoding is shown in a window over the page (click: on / off; right-click: choose the mode, right-click on a mode: its settings)"));
            });
            row.Controls.Add(btnSstv);
        }

        void CreateDigiView(Control host)
        {
            sstvView = new SstvView();
            sstvView.Tag = Theme.Custom;
            sstvView.Visible = false;
            LoadDigiWindow(CurDigiMode);
            sstvView.Moved += delegate { SaveSstvPosition(); };
            ContextMenuStrip vm = new ContextMenuStrip();
            vm.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e) { e.Cancel = false; FillSstvMenu(vm); };
            vm.Items.Add("-");
            sstvView.ContextMenuStrip = vm;
            Tr(delegate { tips.SetToolTip(sstvView, Lang.T("Appui long puis glisser : déplacer la fenêtre ; tirer sur un bord ou un coin : l'agrandir (clic droit : modes et réglages)", "Long press then drag: move the window; drag an edge or a corner: resize it (right-click: modes and settings)")); });
            host.Controls.Add(sstvView);
            sstvView.BringToFront();
            decTimer = new System.Windows.Forms.Timer { Interval = 150 };
            decTimer.Tick += delegate
            {
                if (decDirty) { decDirty = false; sstvView.SetLines(new List<DecLine>(decLines), settings.DigiHi); }
                if (hellDirty) { hellDirty = false; sstvView.RedrawAll(); }
            };
            decTimer.Start();
        }

        void SetSstv(bool on)
        {
            settings.SstvAuto = on;
            settings.Save();
            string n = DigiName(CurDigiMode);
            Log.Write("Modes numériques : " + n + (on ? " activé" : " désactivé"));
            ShowStatus(on ? Lang.F("{0} : décodage en marche", "{0}: decoding on", n) : Lang.F("{0} : décodage arrêté", "{0}: decoding off", n));
            UpdateFreeDv();
        }

        // Choix d'un mode dans le menu : il remplace le précédent et le décodage se met en marche ; avec le clic droit, ses réglages
        // s'ouvrent (seulement eux si c'était déjà le mode choisi).
        void SelectDigiMode(string mode, bool openSettings)
        {
            bool same = mode == CurDigiMode;
            if (openSettings && same) { OpenDigiSettings(mode); return; }
            if (!same)
            {
                StopDigiEngine();
                settings.DigiMode = mode;
                settings.Save();
            }
            if (!settings.SstvAuto) SetSstv(true);
            else { UpdateFreeDv(); ShowStatus(Lang.F("{0} : décodage en marche", "{0}: decoding on", DigiName(mode))); }
            UpdateSstvButton();
            if (openSettings) OpenDigiSettings(mode);
        }

        void OpenDigiSettings(string mode)
        {
            if (digiForm != null && !digiForm.IsDisposed)
            {
                if (digiForm.Mode == mode) { digiForm.Activate(); return; }
                digiForm.Close();
            }
            DigiSettingsForm f = new DigiSettingsForm(settings, mode, Lang.F("Réglages {0}", "{0} settings", DigiName(mode)), ApplyDigiSettings, DigiCommand);
            f.Owner = this;
            f.TopMost = TopMost;
            Rectangle wa = WorkArea;
            Point p = btnSstv.PointToScreen(new Point(0, btnSstv.Height));
            f.Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - f.Width, p.X)), Math.Max(wa.Top, Math.Min(wa.Bottom - f.Height, p.Y)));
            f.FormClosed += delegate { if (digiForm == f) digiForm = null; };
            digiForm = f;
            f.Show(this);
        }

        void DigiCommand(string c)
        {
            switch (c)
            {
                case "folder": OpenSstvFolder(); break;
                case "clearText": decLines.Clear(); decNewLine = true; decDirty = true; break;
                case "hellClear": { HellEngine h = digi as HellEngine; if (h != null) h.ClearReq++; } break;
                case "hellSave": SaveHell(); break;
                case "faxStart": { FaxEngine fx = digi as FaxEngine; if (fx != null) fx.StartReq++; } break;
                case "faxStop": { FaxEngine fx = digi as FaxEngine; if (fx != null) fx.StopReq++; } break;
                case "restart": if (digi != null) { StopDigiEngine(); UpdateFreeDv(); } break;
                case "rename": UpdateSstvButton(); if (sstvView != null && digi != null) sstvView.Caption = DigiName(CurDigiMode); break;
            }
        }

        void UpdateSstvButton()
        {
            if (btnSstv == null || settings == null) return;
            string n = DigiName(CurDigiMode);
            if (btnSstv.Text != n) btnSstv.Text = n;
            bool busy = sstvImage || (DigiRunning && unchecked(Environment.TickCount - digiActivity) < 2000 && unchecked(Environment.TickCount - digiActivity) >= 0);
            if (busy) { btnSstv.BackColor = Color.FromArgb(255, 140, 0); btnSstv.ForeColor = Color.Black; btnSstv.FlatAppearance.BorderColor = Color.FromArgb(200, 100, 0); }
            else if (settings.SstvAuto) { btnSstv.BackColor = Color.FromArgb(0, 120, 60); btnSstv.ForeColor = Color.White; btnSstv.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 75); }
            else { btnSstv.BackColor = Theme.ButtonBack; btnSstv.ForeColor = Theme.Fore; btnSstv.FlatAppearance.BorderColor = Theme.Border; }
        }

        // Clic droit sur le bouton ou sur la fenêtre : les modes, puis les commandes.
        void FillSstvMenu(ContextMenuStrip m)
        {
            m.Items.Clear();
            string cur = CurDigiMode;
            foreach (string mode in DigiModes)
            {
                string md = mode;
                ToolStripMenuItem it = new ToolStripMenuItem(DigiName(md));
                it.Checked = md == cur;
                if (md == cur && settings.SstvAuto) it.Font = new Font(it.Font, FontStyle.Bold);
                // Le clic droit sur un élément de menu déclenche (ou non, selon le cas) aussi Click : une seule action par appui.
                it.MouseDown += delegate(object s, MouseEventArgs e) { menuRight = e.Button == MouseButtons.Right; menuPending = true; };
                it.MouseUp += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button != MouseButtons.Right || !menuPending) return;
                    menuPending = false;
                    m.Close();
                    BeginInvokeSafe(delegate { SelectDigiMode(md, true); });
                };
                it.Click += delegate
                {
                    bool right = menuRight, pending = menuPending;
                    menuRight = false; menuPending = false;
                    if (right && !pending) return;   // déjà fait au relâchement du bouton droit
                    if (right) { m.Close(); BeginInvokeSafe(delegate { SelectDigiMode(md, true); }); }
                    else SelectDigiMode(md, false);
                };
                m.Items.Add(it);
                if (md == "JS8" || md == "Hell") m.Items.Add(new ToolStripSeparator());
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Lang.F("Réglages {0}…", "{0} settings…", DigiName(cur)), null, delegate { OpenDigiSettings(cur); });
            if (DigiSettingsForm.TextMode(cur))
                m.Items.Add(Lang.T("Effacer le texte reçu", "Clear the received text"), null, delegate { DigiCommand("clearText"); });
            m.Items.Add(Lang.T("Ouvrir le dossier des images", "Open the image folder"), null, delegate { OpenSstvFolder(); });
            m.Items.Add(Lang.F("Réinitialiser la fenêtre {0}", "Reset the {0} window", DigiName(ViewMode)), null, delegate { ResetDigiWindow(); });
            m.Items.Add(Lang.T("Réinitialiser les fenêtres", "Reset the windows"), null, delegate { ResetDigiWindows(); });
        }

        // ---- marche / arrêt (appelé par UpdateFreeDv)

        void UpdateDigiEngine()
        {
            if (!settings.SstvAuto) StopDigiEngine();
            else if (!DigiRunning || digiRun != CurDigiMode) { StopDigiEngine(); StartDigiEngine(); }
        }

        void StartDigiEngine()
        {
            string mode = CurDigiMode;
            digiRun = mode;
            sstvImage = false;
            decLines.Clear();
            decNewLine = true;
            LoadDigiWindow(mode);
            sstvView.SetKind(ViewKind(mode), DigiName(mode));
            sstvView.Caption = DigiIdleText();
            if (mode == "SSTV")
            {
                SstvEngine e = new SstvEngine();
                e.ImageBegin += delegate(string name, int w, int h, int[] px) { BeginInvokeSafe(delegate { if (sstv == e) OnSstvBegin(name, w, h, px); }); };
                e.ImageRows += delegate(int done) { BeginInvokeSafe(delegate { if (sstv == e) OnSstvRows(done); }); };
                e.ImageEnd += delegate(bool complete, int rows) { BeginInvokeSafe(delegate { if (sstv == e) OnSstvEnd(complete, rows); }); };
                e.ImageScrolled += delegate { BeginInvokeSafe(delegate { if (sstv == e && sstvView != null) sstvView.RedrawAll(); }); };
                e.Status += delegate(string text) { BeginInvokeSafe(delegate { if (sstv == e) { Log.Write(text); ShowStatus(text); } }); };
                sstv = e;
            }
            else
            {
                DigiEngine e = CreateDigi(mode);
                e.Text += delegate(string s) { BeginInvokeSafe(delegate { if (digi == e) { DecAppendText(s); Activity(); } }); };
                e.Lines += delegate(List<string> l, int parity) { BeginInvokeSafe(delegate { if (digi == e) { foreach (string s in l) DecAddLine(s, parity == 0 ? 1 : 2); if (l.Count > 0) Activity(); } }); };
                e.Status += delegate(string s) { BeginInvokeSafe(delegate { if (digi == e) sstvView.Caption = s; }); };
                e.ImageBegin += delegate(int w, int h, int[] px) { BeginInvokeSafe(delegate { if (digi == e) OnDigiImageBegin(w, h, px); }); };
                e.ImageChanged += delegate { BeginInvokeSafe(delegate { if (digi == e) { hellDirty = true; Activity(); } }); };
                e.ImageRows += delegate(int done) { BeginInvokeSafe(delegate { if (digi == e) OnSstvRows(done); }); };
                e.ImageEnd += delegate(bool complete, int rows) { BeginInvokeSafe(delegate { if (digi == e) OnSstvEnd(complete, rows); }); };
                Resampler rs = new Resampler(e.Rate);
                digiRs = rs;
                digi = e;
                ApplyDigiSettings();
            }
            ApplyDigiSettings();
            sstvView.Visible = true;
            sstvView.Place();
            sstvView.BringToFront();
            Log.Write(DigiName(mode) + " : décodage démarré");
        }

        DigiEngine CreateDigi(string mode)
        {
            switch (mode)
            {
                case "FT8": return new Ft8Engine(0);
                case "FT4": return new Ft8Engine(1);
                case "FT2": return new Ft8Engine(2);
                case "WSPR": return new WsprEngine();
                case "JT9": return new Jt9Engine();
                case "JS8": return new Js8Engine(Math.Max(0, Math.Min(4, settings.DigiGet("js8Speed", 1))));
                case "CW": return new CwEngine();
                case "RTTY": return new RttyEngine();
                case "PSK": return new PskEngine();
                case "Olivia": return new OliviaEngine(false);
                case "Contestia": return new OliviaEngine(true);
                case "Hell": return new HellEngine();
                default: return new FaxEngine();
            }
        }

        void StopDigiEngine()
        {
            if (sstvView != null) sstvView.Visible = false;
            if (!DigiRunning) return;
            string n = DigiName(digiRun ?? CurDigiMode);
            if (sstv != null)
            {
                SstvEngine e = sstv;
                sstv = null;
                e.Stop();
            }
            if (digi != null)
            {
                DigiEngine e = digi;
                digi = null;
                digiRs = null;
                e.Stop();
            }
            if (sstvImage) { sstvImage = false; SaveImage(false, sstvRows); }   // image en cours gardée telle quelle
            digiRun = null;
            UpdateSstvButton();
            Log.Write(n + " : décodage arrêté");
        }

        void Activity() { digiActivity = Environment.TickCount; UpdateSstvButton(); }

        string DigiIdleText()
        {
            return Lang.F("{0} : en écoute ({1})", "{0}: listening ({1})", DigiName(CurDigiMode), FreeDvSourceName(fdvSource));
        }

        // Appelé toutes les 500 ms par UpdateFreeDv : légende au repos, couleur du bouton.
        void DigiTick()
        {
            UpdateSstvButton();
            if (sstv != null && !sstvView.HasImage) sstvView.Caption = DigiIdleText();   // après une image, sa légende reste
        }

        // Son de la réception décodée vers le décodeur en marche (fil de capture).
        void DigiFeed(int rate, float[] x, int n)
        {
            SstvEngine se = sstv;
            if (se != null) se.Feed(rate, x, n);
            DigiEngine de = digi;
            Resampler rs = digiRs;
            if (de != null && rs != null)
            {
                float[] y;
                lock (rs) y = rs.Process(rate, x, n);
                if (y.Length > 0) de.Feed(y, y.Length);
            }
        }

        // Nouvelle source : image en cours abandonnée, passage de fréquence repris à zéro.
        void DigiReset()
        {
            SstvEngine se = sstv;
            if (se != null) se.Reset();
            Resampler rs = digiRs;
            if (rs != null) lock (rs) rs.Reset();
        }

        // Réglages enregistrés copiés vers le décodeur en marche (au démarrage et à chaque changement dans la fenêtre des réglages).
        void ApplyDigiSettings()
        {
            Settings s = settings;
            if (sstv != null) sstv.SetLevels(s.DigiGet("sstvSens", 5), s.DigiGet("sstvLoss", 4));
            DigiEngine d = digi;
            if (d == null) { decDirty = true; return; }
            CwEngine cw = d as CwEngine;
            if (cw != null)
            {
                cw.AutoTone = s.DigiGet("cwAutoTone", true);
                cw.ManualTone = s.DigiGet("cwTone", 700);
                cw.Bandwidth = s.DigiGet("cwBw", 80);
                cw.AutoSpeed = s.DigiGet("cwAutoSpeed", true);
                cw.ManualWpm = s.DigiGet("cwWpm", 18);
                cw.Threshold = s.DigiGet("cwThr", 55);
                cw.CharGap = s.DigiGet("cwChar", 20) / 10.0;
                cw.WordGap = s.DigiGet("cwWord", 50) / 10.0;
                cw.Squelch = s.DigiGet("cwSq", 5);
                cw.AutoBw = s.DigiGet("cwAutoBw", true);
                cw.Afc = s.DigiGet("cwAfc", true);
                cw.Agc = s.DigiGet("cwAgc", 1);
                if (!cw.AutoSpeed) cw.Post(delegate { cw.SetManualSpeed(); });
            }
            RttyEngine r = d as RttyEngine;
            if (r != null)
            {
                double baud = s.DigiGet("rttyBaud", 0) / 100.0;
                int shift = s.DigiGet("rttyShift", 0), pol = s.DigiGet("rttyPol", 0), freq = s.DigiGet("rttyFreq", 0);
                bool afc = s.DigiGet("rttyAfc", true);
                bool redo = r.BaudSetting != baud || r.ShiftSetting != shift || r.PolSetting != pol || r.FreqSetting != freq || r.Afc != afc;
                r.BaudSetting = baud; r.ShiftSetting = shift; r.PolSetting = pol; r.FreqSetting = freq; r.Afc = afc;
                r.Uos = s.DigiGet("rttyUos", true);
                r.Squelch = s.DigiGet("rttySq", 15);
                if (redo) r.Post(delegate { r.SettingsChanged(); });
            }
            PskEngine p = d as PskEngine;
            if (p != null)
            {
                p.AutoFreq = s.DigiGet("pskAuto", true);
                p.ManualFreq = s.DigiGet("pskFreq", 1000);
                p.Afc = s.DigiGet("pskAfc", true);
                p.Baud = s.DigiGet("pskBaud", 31);
                p.Squelch = s.DigiGet("pskSq", 75);
                p.Post(delegate { p.SettingsChanged(); });
            }
            OliviaEngine o = d as OliviaEngine;
            if (o != null)
            {
                bool con = o.Contestia;
                int t = s.DigiGet(con ? "conTones" : "oliTones", con ? 16 : 32), b = s.DigiGet(con ? "conBw" : "oliBw", con ? 500 : 1000);
                int f = s.DigiGet("oliAuto", true) ? 0 : s.DigiGet("oliFreq", 1500);
                int margin = s.DigiGet("oliMargin", 8), integ = s.DigiGet("oliInteg", 4), thr = s.DigiGet("oliThr", 30);
                bool rev = s.DigiGet("oliRev", false);
                bool redo = o.Tones != t || o.Bw != b || o.Margin != margin || o.Integ != integ || o.Thr10 != thr || o.Reverse != rev || (o.CenterSetting != f && f == 0 && o.CenterSetting > 0);
                o.Tones = t; o.Bw = b; o.Margin = margin; o.Integ = integ; o.Thr10 = thr; o.Reverse = rev; o.CenterSetting = f;
                if (redo) o.Post(delegate { o.SettingsChanged(); });
            }
            HellEngine h = d as HellEngine;
            if (h != null)
            {
                int v = s.DigiGet("hellVar", 0);
                bool redo = h.Variant != v;
                h.Variant = v;
                h.CenterSetting = s.DigiGet("hellAuto", true) ? 0 : s.DigiGet("hellFreq", 1000);
                h.Sq = s.DigiGet("hellSq", 5);
                h.Invert = s.DigiGet("hellInv", false);
                if (redo) h.Post(delegate { h.SettingsChanged(); });
            }
            FaxEngine fx = d as FaxEngine;
            if (fx != null)
            {
                fx.Lpm = s.DigiGet("faxLpm", 120);
                fx.Ioc = s.DigiGet("faxIoc", 576);
                fx.AutoStart = s.DigiGet("faxAutoStart", true);
                fx.AutoStop = s.DigiGet("faxAutoStop", true);
                fx.AutoPhase = s.DigiGet("faxAutoPhase", true);
                fx.Center = s.DigiGet("faxCenter", 1900);
                fx.Slant = s.DigiGet("faxSlant", 0);
            }
            decDirty = true;   // surbrillance changée
        }

        // ---- texte

        void DecAddLine(string text, int col)
        {
            decLines.Add(new DecLine(text, col));
            while (decLines.Count > 200) decLines.RemoveAt(0);
            decNewLine = true;
            decDirty = true;
        }

        static string Clock() { return DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture); }

        // CW, RTTY, PSK, Olivia : flux de caractères, une ligne datée environ tous les 70 caractères.
        void DecAppendText(string s)
        {
            foreach (char ch in s)
            {
                if (ch == '\n') { decNewLine = true; continue; }
                if (decNewLine || decLines.Count == 0 || decLines[decLines.Count - 1].Text.Length >= 70)
                {
                    if (ch == ' ') { decNewLine = true; continue; }
                    DecAddLine(Clock() + "  ", 0);
                    decNewLine = false;
                }
                decLines[decLines.Count - 1].Text += ch;
            }
            decDirty = true;
        }

        // ---- images (SSTV, WEFAX, Hell)

        string ImagePrefix { get { return digiRun == "WEFAX" ? "WEFAX" : "SSTV"; } }

        string SstvProgress()
        {
            return Lang.F("{0} {1} ({2}) : {3} / {4}", "{0} {1} ({2}): {3} / {4}", ImagePrefix, sstvMode, FreeDvSourceName(fdvSource), sstvRows, sstvHeight);
        }

        void OnSstvBegin(string name, int w, int h, int[] px)
        {
            sstvImage = true;
            sstvMode = name;
            sstvRows = 0;
            sstvHeight = h;
            sstvView.Begin(w, h, px);
            sstvView.Caption = SstvProgress();
            UpdateSstvButton();
        }

        // Hell : image qui défile (redessinée en entier) ; WEFAX : début d'une image, ou image agrandie (lignes déjà reçues recopiées).
        void OnDigiImageBegin(int w, int h, int[] px)
        {
            if (digiRun == "WEFAX")
            {
                FaxEngine fx = digi as FaxEngine;
                if (!sstvImage)
                {
                    sstvImage = true;
                    sstvRows = 0;
                    sstvMode = fx != null ? fx.Lpm + "/" + (w > 1000 ? 576 : 288) : "";
                    sstvView.Begin(w, h, px);
                }
                else { sstvView.Begin(w, h, px); sstvView.Rows(sstvRows); }
                sstvHeight = h;
                sstvView.Caption = SstvProgress();
                UpdateSstvButton();
                return;
            }
            sstvView.Begin(w, h, px);
            sstvView.RedrawAll();
        }

        void OnSstvRows(int done)
        {
            if (!sstvImage) return;
            sstvRows = done;
            sstvView.Rows(done);
            if (digiRun == "WEFAX") sstvView.Caption = Lang.F("WEFAX {0} ({1}) : ligne {2}", "WEFAX {0} ({1}): line {2}", sstvMode, FreeDvSourceName(fdvSource), done);
            else sstvView.Caption = SstvProgress();
        }

        void OnSstvEnd(bool complete, int rows)
        {
            if (!sstvImage) return;
            sstvImage = false;
            SaveImage(complete, rows);
            UpdateSstvButton();
        }

        // Image terminée (ou signal perdu) : enregistrée en PNG si elle a au moins 16 lignes (et si l'enregistrement automatique est coché).
        void SaveImage(bool complete, int rows)
        {
            string pre = ImagePrefix;
            string label = pre + " " + sstvMode;
            string end = complete ? Lang.T("image terminée", "image complete") : Lang.T("signal perdu", "signal lost");
            bool save = settings.DigiGet(pre == "WEFAX" ? "faxSave" : "sstvSave", true);
            Bitmap b = sstvView.Received(rows);
            string msg;
            if (b == null || !save) { msg = Lang.F("{0} : {1}", "{0}: {1}", label, end); if (b != null) b.Dispose(); }
            else
            {
                string file = SavePng(b, pre + "_" + sstvMode.Replace(" ", "").Replace("/", "-"), complete);
                msg = file != null
                    ? Lang.F("{0} : {1}, image enregistrée : {2}", "{0}: {1}, image saved: {2}", label, end, Path.GetFileName(file))
                    : Lang.F("{0} : {1}, enregistrement impossible", "{0}: {1}, could not save", label, end);
            }
            sstvView.Caption = msg;
            Log.Write(msg);
            ShowStatus(msg);
        }

        string SavePng(Bitmap b, string stem, bool complete)
        {
            string file = null;
            try
            {
                Directory.CreateDirectory(SstvFolder);
                file = Path.Combine(SstvFolder, stem + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + (complete ? "" : "_partiel") + ".png");
                b.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex) { Log.Write("Image : enregistrement impossible : " + ex.Message); file = null; }
            finally { b.Dispose(); }
            return file;
        }

        void SaveHell()
        {
            Bitmap b = digi is HellEngine ? sstvView.Snapshot() : null;
            if (b == null) { ShowStatus(Lang.T("Rien à enregistrer", "Nothing to save")); return; }
            string file = SavePng(b, "Hell", true);
            ShowStatus(file != null ? Lang.F("Hell : image enregistrée : {0}", "Hell: picture saved: {0}", Path.GetFileName(file)) : Lang.T("Hell : enregistrement impossible", "Hell: could not save"));
        }

        void OpenSstvFolder()
        {
            try
            {
                Directory.CreateDirectory(SstvFolder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + SstvFolder + "\"");
            }
            catch (Exception ex) { ShowStatus(ex.Message); }
        }

        // Fenêtre de chaque mode : position (millièmes de la place libre) et taille (dix-millièmes de la page, 0 = taille d'origine),
        // retenues à part pour chaque mode ; SSTV reprend l'ancienne position SstvX / SstvY.
        string ViewMode { get { return digiRun ?? CurDigiMode; } }

        void LoadDigiWindow(string mode)
        {
            bool s = mode == "SSTV";
            sstvView.PosX = Math.Max(0, Math.Min(1000, settings.DigiGet("winX." + mode, s ? settings.SstvX : 0))) / 1000.0;
            sstvView.PosY = Math.Max(0, Math.Min(1000, settings.DigiGet("winY." + mode, s ? settings.SstvY : 1000))) / 1000.0;
            sstvView.CustW = Math.Max(0, Math.Min(10000, settings.DigiGet("winW." + mode, 0))) / 10000.0;
            sstvView.CustH = Math.Max(0, Math.Min(10000, settings.DigiGet("winH." + mode, 0))) / 10000.0;
        }

        // « Réinitialiser les fenêtres » : toutes les fenêtres des modes reprennent leur place (en bas à gauche) et leur taille d'origine.
        void ResetDigiWindows()
        {
            foreach (string mode in DigiModes)
                foreach (string k in new string[] { "winX.", "winY.", "winW.", "winH." }) settings.Digi.Remove(k + mode);
            settings.SstvX = 0; settings.SstvY = 1000;
            settings.Save();
            LoadDigiWindow(ViewMode);
            sstvView.Place();
        }

        // « Réinitialiser la fenêtre <mode> » : seule la fenêtre du mode affiché reprend sa place et sa taille d'origine.
        void ResetDigiWindow()
        {
            string mode = ViewMode;
            foreach (string k in new string[] { "winX.", "winY.", "winW.", "winH." }) settings.Digi.Remove(k + mode);
            if (mode == "SSTV") { settings.SstvX = 0; settings.SstvY = 1000; }
            settings.Save();
            LoadDigiWindow(mode);
            sstvView.Place();
        }

        void SaveSstvPosition()
        {
            string mode = ViewMode;
            settings.DigiSet("winX." + mode, (int)Math.Round(sstvView.PosX * 1000));
            settings.DigiSet("winY." + mode, (int)Math.Round(sstvView.PosY * 1000));
            if (sstvView.CustW > 0 && sstvView.CustH > 0)
            {
                settings.DigiSet("winW." + mode, Math.Max(1, (int)Math.Round(sstvView.CustW * 10000)));
                settings.DigiSet("winH." + mode, Math.Max(1, (int)Math.Round(sstvView.CustH * 10000)));
            }
            if (mode == "SSTV") { settings.SstvX = settings.DigiGet("winX.SSTV", 0); settings.SstvY = settings.DigiGet("winY.SSTV", 1000); }
            settings.Save();
        }
    }
}
