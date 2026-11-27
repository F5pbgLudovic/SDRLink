using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SDRLink
{
    // Scan (fréquences ou mémoires), avec ce que PhoneSDRLink a apporté (1.52) : clic = reprendre le scan sans rien demander,
    // clic droit = menu (scan de la bande, profils, options), pause sur signal, durées dès 0,05 s, mode du scan, scan prêt qui
    // suit la bande, touches multimédia (casque, clavier) et commande vocale.
    public partial class MainForm
    {
        readonly Timer scanTimer = new Timer();
        bool scanning;
        long scanHz;
        ScanProfile scanRun;   // réglage du scan en cours (zones non scannées, pause, mode)
        int scanIndex;
        List<Memory> scanMems = new List<Memory>();
        int scanDwellMs = 5000;
        string scanWhat = "";

        void ConfigureScan()
        {
            DialogResult r;
            using (ScanDialog d = new ScanDialog(settings)) r = d.ShowDialog(this);
            settings.Save();   // les profils enregistrés dans la fenêtre restent, même après Annuler
            if (r == DialogResult.Yes) StartScan();
        }

        // Clic sur Scan. Rouge (scan en marche) : arrêt du scan et de la commande vocale. Vert (commande vocale à l'écoute) :
        // micro coupé. Sinon : reprend sans rien demander le scan réglé ; aucun scan réglé : celui du premier profil ; aucun
        // profil : la fenêtre « Conf scan ».
        void OnScanClick()
        {
            if (scanning) { StopScan(null); VoiceOff(); return; }
            if (VoiceListening) { VoiceOff(); return; }
            if (ScanReady(settings.CurrentScan(""))) { StartScan(true); return; }
            if (settings.ScanProfiles.Count > 0)
            {
                settings.UseScan(settings.ScanProfiles[0]);
                settings.Save();
                if (ScanReady(settings.CurrentScan(""))) { StartScan(true); return; }
            }
            ConfigureScan();
        }

        static bool ScanReady(ScanProfile p)
        {
            if (p.Type == 1) return p.Mems.Count > 0;
            return p.Lo > 0 && p.Lo != p.Hi && p.NextFreq(-1) >= 0;
        }

        // Clic droit sur Scan : la bande radioamateur où l'on se trouve (la bande entière, puis les profils qui en font partie),
        // la reprise du scan, le réglage actuel, les autres profils ; puis « Conf scan » et les options. Les couleurs du bouton
        // sont ajoutées à la fin (AddColorMenu).
        void FillScanMenu(ContextMenuStrip m)
        {
            m.Items.Clear();
            int b = Bands.At(CurrentFreq());
            HashSet<ScanProfile> shown = new HashSet<ScanProfile>();
            if (b >= 0)
            {
                long lo = Bands.Lo(b), hi = Bands.Hi(b);
                ToolStripMenuItem band = new ToolStripMenuItem((Bands.IsCb(b) ? Lang.T("Scan de la bande étendue des 27 MHz", "Scan of the extended 27 MHz band") : Bands.IsCm(b) ? Lang.T("Scan de la bande des 70 cm", "Scan of the 70 cm band") : Lang.F("Scan de la bande des {0} m", "Scan of the {0} m band", Bands.Name(b))) + "  —  " + Memory.KHz(lo) + " – " + Memory.KHz(hi) + " kHz");
                band.Font = new Font(band.Font, FontStyle.Bold);
                band.Click += delegate
                {
                    ScanProfile p = settings.CurrentScan("");
                    p.Type = 0; p.StartHz = lo; p.EndHz = hi; p.Skips.Clear();
                    if (p.StepHz <= 0) p.StepHz = 1000;
                    settings.UseScan(p);
                    settings.Save();
                    StartScan(true);
                };
                m.Items.Add(band);
                foreach (ScanProfile p in settings.ScanProfiles)
                {
                    if (p.Type != 0 || p.Lo < lo || p.Hi > hi || p.Lo >= p.Hi) continue;
                    shown.Add(p);
                    m.Items.Add(ProfileItem(p, true));
                }
                m.Items.Add(new ToolStripSeparator());
            }
            ToolStripMenuItem resume = new ToolStripMenuItem(Lang.T("Reprendre le scan", "Resume the scan") + "  —  " + ResumeText());
            resume.Click += delegate { StartScan(true); };
            m.Items.Add(resume);
            ToolStripMenuItem now = new ToolStripMenuItem(Lang.T("Réglage actuel de « Conf scan »", "Current \"Scan setup\" setting") + "  —  " + settings.CurrentScan("").Summary());
            now.Click += delegate { StartScan(); };
            m.Items.Add(now);
            bool sep = false;
            foreach (ScanProfile p in settings.ScanProfiles)
            {
                if (shown.Contains(p)) continue;
                if (!sep) { m.Items.Add(new ToolStripSeparator()); sep = true; }
                m.Items.Add(ProfileItem(p, false));
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem(Lang.T("Conf scan…", "Scan setup…"), null, delegate { ConfigureScan(); }));
            ToolStripMenuItem keys = new ToolStripMenuItem(Lang.T("Touches multimédia (casque, clavier) : arrêter / reprendre le scan", "Media keys (headset, keyboard): stop / resume the scan"))
                { Checked = settings.ScanMediaKeys, CheckOnClick = true };
            keys.ToolTipText = Lang.T("Lecture/Pause : arrête ou reprend le scan. Suivant / Précédent : recule d'un pas quand ces touches ont arrêté le scan. Les autres logiciels ne reçoivent plus ces touches tant que la case est cochée.",
                "Play/Pause: stops or resumes the scan. Next / Previous: back one step when these keys stopped the scan. Other programs no longer get these keys while this is checked.");
            keys.Click += delegate { settings.ScanMediaKeys = keys.Checked; settings.Save(); ApplyMediaKeys(true); };
            m.Items.Add(keys);
            ToolStripMenuItem voice = new ToolStripMenuItem(Lang.T("Commande vocale du scan (« stop », « start », « plus un » ou « avance », « moins un » ou « recule »)", "Scan voice command (\"stop\", \"start\", \"plus one\", \"minus one\")"))
                { Checked = settings.ScanVoice, CheckOnClick = true };
            voice.ToolTipText = Lang.T("Reconnaissance vocale de Windows, hors ligne, avec le micro par défaut de Windows ; le micro n'écoute que pendant le scan et 5 minutes après son arrêt.",
                "Windows speech recognition, offline, with the Windows default microphone; it only listens during the scan and for 5 minutes after it stops.");
            voice.Click += delegate
            {
                settings.ScanVoice = voice.Checked;
                settings.Save();
                if (!settings.ScanVoice) VoiceOff();
                else if (scanning) VoiceStart();
                ShowStatus(settings.ScanVoice ? Lang.T("Commande vocale du scan : le micro écoutera pendant le scan", "Scan voice command: the microphone will listen during the scan")
                                              : Lang.T("Commande vocale du scan coupée", "Scan voice command off"));
            };
            m.Items.Add(voice);
        }

        ToolStripMenuItem ProfileItem(ScanProfile p, bool resume)
        {
            ScanProfile pp = p;
            ToolStripMenuItem it = new ToolStripMenuItem(p.Name + "  —  " + p.Summary());
            it.Click += delegate
            {
                settings.UseScan(pp);
                settings.Save();
                Log.Write("Scan : profil « " + pp.Name + " »");
                StartScan(resume);
            };
            return it;
        }

        // Fréquences : de la plus basse à la plus haute, par pas, en sautant les zones non scannées, puis on recommence.
        // Mémoires : les cochées, dans l'ordre. Chaque fréquence ou mémoire est gardée la durée choisie. L'émission arrête le scan.
        // Reprendre (resume) : le scan part de la fréquence actuelle si elle est dans la plage (la suivante si elle est
        // dans une zone non scannée), de la mémoire cochée sur la fréquence actuelle ; sinon du début.
        void StartScan(bool resume = false)
        {
            if (Transmitting) { ShowStatus(Lang.T("Scan impossible en émission", "No scan while transmitting")); return; }
            if (scanning) StopScan(null);
            long cur = resume ? CurrentFreq() : -1;
            scanRun = settings.CurrentScan("");
            if (settings.ScanType == 0)
            {
                if (scanRun.Lo <= 0 || scanRun.Lo == scanRun.Hi) { ShowStatus(Lang.T("Scan : indiquez les fréquences de début et de fin dans « Conf scan »", "Scan: set the start and end frequencies in \"Scan setup\"")); return; }
                scanHz = ResumeFreq(scanRun, cur);
                if (scanHz < 0) { ShowStatus(Lang.T("Scan : toutes les fréquences sont dans des zones non scannées (« Conf scan »)", "Scan: all frequencies are in skipped ranges (\"Scan setup\")")); return; }
                scanDwellMs = Math.Max(50, settings.ScanFreqDwell);
            }
            else
            {
                scanMems = ScanMemories();
                if (scanMems.Count == 0) { ShowStatus(Lang.T("Scan : cochez des mémoires dans « Conf scan »", "Scan: check memories in \"Scan setup\"")); return; }
                scanIndex = Math.Max(0, scanMems.FindIndex(m => m.Hz == cur));
                scanDwellMs = Math.Max(50, settings.ScanMemDwell);
            }
            scanRecent.Clear();
            NoteScanTarget(CurrentFreq());   // fréquence d'avant le scan : encore relue un instant
            scanning = true;
            stoppedByKeys = false;
            if (settings.ScanType == 0 && scanRun.Mode != "") ApplyMode(scanRun.Mode);   // mode choisi pour ce scan
            Log.Write("Scan : " + (resume ? "reprise à " + (settings.ScanType == 0 ? FormatFreq(scanHz) : scanMems[scanIndex].Label) + " " : "départ ") + "(" +
                (settings.ScanType == 0 ? FormatFreq(scanRun.Lo) + " - " + FormatFreq(scanRun.Hi) + ", pas " + scanRun.StepHz + " Hz"
                    + (scanRun.Mode != "" ? ", " + scanRun.Mode : "")
                    + (scanRun.Skips.Count > 0 ? ", sans " + string.Join(", ", scanRun.Skips.ConvertAll(z => Memory.KHz(z[0]) + "-" + Memory.KHz(z[1]) + " kHz").ToArray()) : "")
                    : scanMems.Count + " mémoire(s)")
                + ", " + scanDwellMs + " ms" + (scanRun.PauseSec > 0 ? ", pause sur signal " + scanRun.PauseSec + " s dès S/N " + scanRun.SnrDb + " dB" : "") + ")");
            snrPollMs = Math.Max(40, Math.Min(160, scanDwellMs / 3));   // lecture du S/N : 3 fois par durée de fréquence au moins
            snrSettleMs = Math.Min(400, scanDwellMs * 2 / 5);          // début de chaque fréquence ignoré (le son arrive après le réglage)
            VoiceStart();
            scanFirst = true;
            ScanTune();
            scanTimer.Interval = scanDwellMs;
            scanTimer.Start();
            if (scanRun.PauseSec > 0) SnrStart();
            UpdateScanButton();
        }

        // Fréquence de départ : la fréquence actuelle (cur) si elle est dans la plage, la suivante si elle est dans une zone
        // non scannée, sinon la première du scan (aussi pour cur < 0).
        static long ResumeFreq(ScanProfile p, long cur)
        {
            if (cur < p.Lo || cur > p.Hi) return p.NextFreq(-1);
            return p.Skipped(cur) ? p.NextFreq(cur) : cur;
        }

        List<Memory> ScanMemories()
        {
            return settings.Memories.Items.FindAll(delegate(Memory m) { return m.Scan; });
        }

        // Où « Reprendre le scan » repartira, pour le menu du bouton Scan.
        string ResumeText()
        {
            long cur = CurrentFreq();
            if (settings.ScanType == 0)
            {
                ScanProfile p = settings.CurrentScan("");
                if (p.Lo <= 0 || p.Lo == p.Hi) return Lang.T("fréquences non réglées", "frequencies not set");
                long f = ResumeFreq(p, cur);
                if (f < 0) return Lang.T("toutes les fréquences sont dans des zones non scannées", "all frequencies are in skipped ranges");
                if (cur >= p.Lo && cur <= p.Hi) return Lang.F("à {0} kHz", "at {0} kHz", Memory.KHz(f));
                return Lang.F("au début ({0} kHz) : fréquence actuelle hors de la plage", "from the start ({0} kHz): current frequency out of range", Memory.KHz(f));
            }
            List<Memory> mems = ScanMemories();
            if (mems.Count == 0) return Lang.T("aucune mémoire cochée", "no memory checked");
            int i = mems.FindIndex(m => m.Hz == cur);
            if (i >= 0) return Lang.F("à la mémoire {0}", "at memory {0}", mems[i].Label);
            return Lang.F("au début ({0}) : fréquence actuelle hors des mémoires", "from the start ({0}): current frequency not in the memories", mems[0].Label);
        }

        // Clic dans la page du WebSDR pendant un scan : le scan s'arrête et reste sur la fréquence scannée à ce
        // moment-là ; pendant ScanHoldMs, le changement de fréquence que le clic fait dans la cascade est annulé
        // (OnSdrState). Le clic suivant, même dans ces 2 s, règle la fréquence normalement.
        const int ScanHoldMs = 2000;
        long scanHoldHz;
        DateTime scanHoldUntil;

        void OnPageClick()
        {
            if (!scanning) { scanHoldUntil = DateTime.MinValue; return; }   // clic suivant : réglage normal
            long hz = settings.ScanType == 0 ? scanHz : scanMems[scanIndex].Hz;
            StopScan(Lang.F("clic dans la page, arrêt sur {0} kHz", "click in the page, stopped on {0} kHz", Memory.KHz(hz)));
            scanHoldHz = hz;
            scanHoldUntil = DateTime.UtcNow.AddMilliseconds(ScanHoldMs);
            // Le changement a pu arriver avant ce message : on revient tout de suite sur la fréquence scannée.
            if (Math.Abs(CurrentFreq() - hz) > Tolerance) TuneBoth(hz);
        }

        // Fréquences (côté poste) que le scan vient de régler : une relecture du poste ou de la page qui tombe sur l'une
        // d'elles est l'écho du scan (même en retard de plusieurs pas aux durées courtes), pas un geste de l'opérateur.
        readonly Queue<long> scanRecent = new Queue<long>();

        void NoteScanTarget(long rigHz)
        {
            scanRecent.Enqueue(rigHz);
            while (scanRecent.Count > 100) scanRecent.Dequeue();
        }

        // Fréquence changée pendant le scan sur le poste (VFO) ou dans la page du WebSDR (molette, touches) : le scan s'arrête
        // et l'on reste sur la nouvelle fréquence (1.52). Vrai si le scan a été arrêté.
        bool ScanOperatorTuned(long rigHz, bool onRig)
        {
            if (!scanning || rigHz <= 0) return false;
            foreach (long x in scanRecent) if (Math.Abs(x - rigHz) <= Tolerance) return false;
            StopScan(onRig ? Lang.F("fréquence changée sur le poste, arrêt sur {0} kHz", "frequency changed on the rig, stopped on {0} kHz", Memory.KHz(rigHz))
                           : Lang.F("fréquence changée sur le WebSDR, arrêt sur {0} kHz", "frequency changed on the WebSDR, stopped on {0} kHz", Memory.KHz(rigHz)));
            return true;
        }

        void StopScan(string reason)
        {
            if (!scanning) return;
            scanning = false;
            scanTimer.Stop();
            SnrStop();
            Log.Write("Scan : arrêt" + (reason != null ? " (" + reason + ")" : ""));
            ShowStatus(reason != null ? Lang.F("Scan arrêté : {0}", "Scan stopped: {0}", reason) : Lang.T("Scan arrêté", "Scan stopped"));
            // La commande vocale reste à l'écoute 5 minutes : « start » doit pouvoir reprendre le scan.
            if (voice != null && voice.Active) { voiceIdle.Stop(); voiceIdle.Start(); }
            UpdateScanButton();
        }

        void ScanNext()
        {
            if (!scanning) return;
            if (Transmitting) { StopScan(Lang.T("émission", "transmitting")); return; }
            scanFirst = false;
            if (settings.ScanType == 0) scanHz = scanRun.NextFreq(scanHz);
            else scanIndex = (scanIndex + 1) % scanMems.Count;
            ScanTune();
        }

        // Le texte du scan va directement dans la barre d'état, sans remplir l'historique des messages.
        void ScanTune()
        {
            string what;
            if (settings.ScanType == 0) { NoteScanTarget(scanHz); TuneBoth(scanHz); what = Memory.KHz(scanHz) + " kHz"; }
            else
            {
                Memory m = scanMems[scanIndex];
                NoteScanTarget(m.Hz);
                ApplyMemory(m);
                what = m.Label + (m.Name.Length > 0 ? " (" + Memory.KHz(m.Hz) + " kHz)" : "");
            }
            scanWhat = what;
            scanTuneAt = DateTime.UtcNow;
            scanHolding = false;
            status.Text = Lang.F("Scan : {0}", "Scan: {0}", what);
        }

        void UpdateScanButton()
        {
            bool listen = VoiceListening;
            btnScan.Text = scanning ? Lang.T("■ Arrêter le scan", "■ Stop scan") : "▶ Scan";
            btnScan.BackColor = scanning ? Color.FromArgb(200, 0, 0) : listen ? Color.FromArgb(0, 150, 60) : Theme.ButtonBack;
            btnScan.ForeColor = scanning || listen ? Color.White : Theme.Fore;
            btnScan.FlatAppearance.BorderColor = scanning ? Color.FromArgb(150, 0, 0) : listen ? Color.FromArgb(0, 110, 45) : Theme.Border;
            if (listen) return;
            int custom = ColorOf(scanning ? "ScanOn" : "ScanOff");
            if (custom >= 0) PaintCustom(btnScan, custom);
        }

        // ---------------------------------------------------------------- scan prêt qui suit la bande

        int scanBand = -2;   // bande radioamateur de la fréquence actuelle (-1 hors bandes, -2 pas encore connue)

        // Appelé à chaque affichage de fréquence. Passage dans une autre bande radioamateur (hors scan en marche) : le scan de
        // fréquences prêt devient celui de la nouvelle bande, soit le premier profil qui y tient, soit la bande entière.
        void ScanBandFollow()
        {
            int b = Bands.At(CurrentFreq());
            if (b == scanBand) return;
            bool follow = scanBand != -2 && b >= 0 && !scanning && settings.ScanType == 0;
            scanBand = b;
            if (!follow) return;
            long lo = Bands.Lo(b), hi = Bands.Hi(b);
            ScanProfile cur = settings.CurrentScan("");
            if (cur.Lo >= lo && cur.Hi <= hi && cur.Lo < cur.Hi) return;   // le scan prêt est déjà dans cette bande
            ScanProfile pick = settings.ScanProfiles.Find(p => p.Type == 0 && p.Lo >= lo && p.Hi <= hi && p.Lo < p.Hi);
            if (pick == null)
            {
                pick = cur;
                pick.StartHz = lo; pick.EndHz = hi; pick.Skips.Clear(); pick.Mode = "";
                if (pick.StepHz <= 0) pick.StepHz = 1000;
            }
            settings.UseScan(pick);
            settings.Save();
            Log.Write("Scan : bande des " + Bands.Label(b) + ", scan prêt = " + (pick.Name != "" ? "profil « " + pick.Name + " »" : "bande entière"));
        }

        // ---------------------------------------------------------------- pause sur signal (« Conf scan » : Pause sur signal)
        // Le pont JS mesure le S/N du son du WebSDR (analyseur de 1024 points, lu seulement pendant un scan avec pause).
        // Signal repéré (S/N >= seuil) sur la fréquence N : retour d'un pas en N-1 (le son arrive après le réglage), pause de
        // exactement PauseSec secondes quel que soit le signal ensuite, puis le scan repart de N, donc va en N+1.

        readonly Timer snrTimer = new Timer();
        int snrPollMs = 160, snrSettleMs = 400;
        bool scanHolding, snrBusy, snrWired;
        bool scanFirst;   // première fréquence du scan (départ ou reprise) : pas de retour d'un pas sur un signal
        DateTime scanTuneAt, holdStart;
        long holdShownSec = -1, holdFromHz;
        int holdFromIdx;

        void SnrStart()
        {
            if (!snrWired) { snrWired = true; snrTimer.Tick += delegate { SnrPoll(); }; }
            scanHolding = false;
            snrTimer.Interval = snrPollMs;
            snrTimer.Start();
        }

        void SnrStop()
        {
            snrTimer.Stop();
            scanHolding = false;
            try { if (web != null && web.CoreWebView2 != null) web.CoreWebView2.ExecuteScriptAsync("window.__sdrlink && window.__sdrlink.setSnr && window.__sdrlink.setSnr(false);"); } catch { }
        }

        async void SnrPoll()
        {
            if (!scanning || scanRun == null || scanRun.PauseSec <= 0) { snrTimer.Stop(); return; }
            if (snrBusy || web == null || web.CoreWebView2 == null) return;
            snrBusy = true;
            string v;
            // setSnr(true) à chaque lecture : l'analyseur revient après un rechargement de la page.
            try { v = await web.CoreWebView2.ExecuteScriptAsync("(function(){try{var L=window.__sdrlink;if(!L||!L.snr)return -99;if(!L.snrOn)L.setSnr(true);return L.snr();}catch(e){return -99;}})()"); }
            catch { v = null; }
            snrBusy = false;
            double snr;
            if (v == null || !double.TryParse(v.Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out snr)) return;
            if (scanning && scanRun.PauseSec > 0) OnSnr(snr);
        }

        void OnSnr(double snr)
        {
            DateTime now = DateTime.UtcNow;
            if (!scanHolding)
            {
                if ((now - scanTuneAt).TotalMilliseconds < snrSettleMs || snr < scanRun.SnrDb) return;
                scanTimer.Stop();
                holdFromHz = scanHz; holdFromIdx = scanIndex;   // fréquence où le signal a été repéré : le scan reprendra après elle
                string seen = scanWhat;
                // Le signal mesuré vient en fait de la fréquence précédente (le son arrive après le réglage) : retour d'un pas.
                // Sauf sur la première fréquence du scan : il n'y a pas de fréquence précédente (7040 renvoyait à 7200).
                if (scanFirst) { }
                // « Pas en arrière » de Conf scan (1 à 99, PhoneSDRLink 1.33).
                else if (settings.ScanType == 0)
                {
                    long f = scanHz;
                    for (int i = 0; i < Math.Max(1, scanRun.Back); i++) { long g = scanRun.PrevFreq(f); if (g < 0 || g > f) break; f = g; }   // sans repasser par la fin de la plage
                    if (f != scanHz) { scanHz = f; ScanTune(); }
                }
                else if (scanMems.Count > 1) { int n = scanMems.Count; scanIndex = ((scanIndex - Math.Max(1, scanRun.Back)) % n + n) % n; ScanTune(); }
                scanHolding = true;
                holdStart = now;
                holdShownSec = -1;
                Log.Write("Scan : signal sur " + seen + " (S/N " + snr.ToString("0.0", CultureInfo.InvariantCulture) + " dB), pause sur " + scanWhat);
            }
            double left = scanRun.PauseSec * 1000.0 - (now - holdStart).TotalMilliseconds;
            if (left <= 0)
            {
                scanHolding = false;
                scanHz = holdFromHz; scanIndex = holdFromIdx;   // reprise après la fréquence du signal (sans y revenir)
                ScanNext();
                scanTimer.Stop();
                scanTimer.Start();
                return;
            }
            long sec = (long)Math.Ceiling(left / 1000);
            if (sec != holdShownSec)
            {
                holdShownSec = sec;
                status.Text = Lang.F("Scan en pause sur {0} : signal {1} dB, reprise dans {2}", "Scan paused on {0}: signal {1} dB, resumes in {2}",
                    scanWhat, snr.ToString("0.0", CultureInfo.CurrentCulture), (sec / 60) + ":" + (sec % 60).ToString("00", CultureInfo.InvariantCulture));
            }
        }

        // ---------------------------------------------------------------- touches multimédia (casque, clavier)
        // Lecture/Pause (ou Stop) : arrête le scan, ou le reprend. Suivant / Précédent : quand ces touches ont arrêté le scan,
        // recule d'un pas (le signal est déjà passé) ; Lecture/Pause reprend ensuite depuis cette fréquence. Touches prises à
        // tout Windows (RegisterHotKey) seulement quand l'option est cochée.

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        const int WM_HOTKEY = 0x0312, HotKeyBase = 0x5C10;
        const uint MOD_NOREPEAT = 0x4000;
        static readonly uint[] MediaVks = { 0xB3, 0xB2, 0xB0, 0xB1 };   // lecture/pause, stop, suivant, précédent
        bool mediaKeysOn, stoppedByKeys;
        DateTime lastMediaKey;

        void ApplyMediaKeys(bool report)
        {
            if (!IsHandleCreated) return;
            bool want = settings.ScanMediaKeys;
            if (want == mediaKeysOn) return;
            if (want)
            {
                int ok = 0;
                for (int i = 0; i < MediaVks.Length; i++) if (RegisterHotKey(Handle, HotKeyBase + i, MOD_NOREPEAT, MediaVks[i])) ok++;
                mediaKeysOn = true;
                Log.Write("Scan : touches multimédia prises (" + ok + "/" + MediaVks.Length + ")");
                if (report) ShowStatus(ok > 0 ? Lang.T("Touches multimédia : Lecture/Pause arrête ou reprend le scan", "Media keys: Play/Pause stops or resumes the scan")
                                              : Lang.T("Touches multimédia : déjà prises par un autre logiciel", "Media keys: already taken by another program"));
            }
            else
            {
                for (int i = 0; i < MediaVks.Length; i++) UnregisterHotKey(Handle, HotKeyBase + i);
                mediaKeysOn = false;
                if (report) ShowStatus(Lang.T("Touches multimédia rendues à Windows", "Media keys given back to Windows"));
            }
        }

        protected override void WndProc(ref Message msg)
        {
            ProportionSizing(ref msg);
            if (msg.Msg == WM_HOTKEY)
            {
                int i = msg.WParam.ToInt32() - HotKeyBase;
                if (i >= 0 && i < MediaVks.Length) { OnMediaKey(i >= 2); return; }
            }
            base.WndProc(ref msg);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            mediaKeysOn = false;
            ApplyMediaKeys(false);
        }

        void OnMediaKey(bool back)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - lastMediaKey).TotalMilliseconds < (back ? 300 : 700)) return;   // anti-rebond (double événement de certains casques)
            lastMediaKey = now;
            if (back) { ScanBackStep(); return; }
            if (scanning) { StopScan(Lang.T("touche multimédia", "media key")); stoppedByKeys = true; }
            else { stoppedByKeys = false; StartScan(true); }
        }

        void ScanBackStep()
        {
            if (scanning || !stoppedByKeys || scanRun == null) return;
            if (settings.ScanType == 0)
            {
                long f = scanRun.PrevFreq(scanHz);
                if (f < 0) return;
                scanHz = f;
            }
            else
            {
                if (scanMems.Count == 0) return;
                scanIndex = (scanIndex - 1 + scanMems.Count) % scanMems.Count;
            }
            ScanTune();
            ShowStatus(Lang.F("Retour d'un pas : {0}", "Back one step: {0}", scanWhat));
        }

        // ---------------------------------------------------------------- commande vocale du scan

        VoiceCmd voice;
        readonly Timer voiceIdle = new Timer { Interval = 5 * 60 * 1000 };   // le micro reste ouvert 5 minutes après l'arrêt du scan
        bool voiceWired;

        bool VoiceListening { get { return !scanning && settings.ScanVoice && voice != null && voice.Active; } }

        void VoiceStart()
        {
            if (!voiceWired) { voiceWired = true; voiceIdle.Tick += delegate { VoiceOff(); }; }
            voiceIdle.Stop();
            if (!settings.ScanVoice || (voice != null && voice.Active)) return;
            try
            {
                if (voice == null)
                {
                    voice = new VoiceCmd();
                    voice.Command += cmd => { try { BeginInvoke((Action)(() => OnVoice(cmd))); } catch { } };
                }
                voice.Start(Lang.Code);
            }
            catch (Exception ex)
            {
                Log.Write("Commande vocale impossible : " + ex.Message);
                ShowStatus(Lang.F("Commande vocale impossible : {0}", "Voice command unavailable: {0}", ex.Message));
            }
        }

        void VoiceOff()
        {
            voiceIdle.Stop();
            if (voice != null) voice.Stop();
            UpdateScanButton();
        }

        void OnVoice(string cmd)
        {
            if (voice == null || !voice.Active) return;
            ShowStatus(Lang.F("Commande vocale : « {0} »", "Voice command: \"{0}\"", voice.Said(cmd)));
            if (cmd == "stop") StopScan(Lang.T("commande vocale", "voice command"));
            else if (cmd == "start") { if (!scanning) StartScan(true); }
            else if (cmd == "plus" || cmd == "moins")
            {
                long step = Math.Max(1, settings.TuneStepHz), cur = CurrentFreq();
                if (cur <= 0) return;
                if (scanning) StopScan(Lang.T("commande vocale", "voice command"));
                TuneBoth((long)Math.Round((double)cur / step) * step + (cmd == "plus" ? step : -step));
            }
        }

        void CloseScan()
        {
            StopScan(null);
            voiceIdle.Stop();
            if (voice != null) { voice.Dispose(); voice = null; }
            if (mediaKeysOn && IsHandleCreated) for (int i = 0; i < MediaVks.Length; i++) UnregisterHotKey(Handle, HotKeyBase + i);
            mediaKeysOn = false;
        }
    }
}
