using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SDRLink
{
    // ---------------------------------------------------------------- Écoute en diversité (1.40)
    // Case « Diversité » après « Réinit WebSDR » : le WebSDR dans une oreille, la réception locale dans l'autre (le poste par
    // la carte son de REC, ExpertSDR par son flux audio TCI, ou HDSDR par la capture du programme sous Windows 11), le son
    // local retardé pour tomber en même temps que le WebSDR (DiversityMixer). Pendant ce temps, SDRLink fait taire la sortie
    // directe de chacun (page WebSDR à volume 0, gain BF du poste à 0, ExpertSDR coupé, HDSDR à -60 dB) et rejoue les deux
    // sons dans les haut-parleurs ; Mute et Volume de chaque réception règlent son oreille. Tout revient à l'arrêt.
    public partial class MainForm
    {
        Button chkDiversity;
        DiversityMixer diversity;
        StereoOut divOut;
        AudioTap divTap;
        int divSource;                  // 1 poste, 2 ExpertSDR, 3 HDSDR (0 : arrêtée)
        bool divRigSilenced, divEsdrSilenced, divHdsdrSilenced;
        System.Windows.Forms.Timer divTimer;
        bool divShownLocked;
        double divShownDelay = -1;

        // Bouton simple (1.41) : clic = marche / arrêt, la couleur dit l'état (orange : recherche du retard, vert : calé).
        void CreateDiversityButton()
        {
            chkDiversity = new Button();
            chkDiversity.AutoSize = true;
            chkDiversity.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            chkDiversity.MinimumSize = new Size(30, 25);
            chkDiversity.Tag = Theme.Custom;
            chkDiversity.Margin = new Padding(6, 3, 3, 3);
            chkDiversity.Click += delegate { SetDiversity(diversity == null); };
            ContextMenuStrip m = new ContextMenuStrip();
            m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e) { e.Cancel = false; FillDiversityMenu(m); };
            m.Items.Add("-");
            chkDiversity.ContextMenuStrip = m;
            AddColorMenu(chkDiversity, Slot("DiversityOff", () => Lang.T("Couleur diversité arrêtée", "Colour when diversity is off")),
                Slot("DiversitySearch", () => Lang.T("Couleur pendant la recherche du retard", "Colour while measuring the delay")),
                Slot("DiversityOn", () => Lang.T("Couleur diversité calée", "Colour when diversity is locked")));
            Tr(delegate
            {
                UpdateDiversityButton(true);
                tips.SetToolTip(chkDiversity, Lang.T("Écoute en diversité : le WebSDR dans une oreille, le poste (ou ExpertSDR, ou HDSDR) dans l'autre, le son local retardé pour tomber en même temps que le WebSDR. Clic : marche / arrêt. Clic droit : réception mise dans l'autre oreille, côté du WebSDR, retard mesuré",
                    "Diversity listening: the WebSDR in one ear, the rig (or ExpertSDR, or HDSDR) in the other, the local audio delayed to match the WebSDR. Click: on / off. Right-click: reception put in the other ear, WebSDR side, measured delay"));
            });
        }

        string DivSourceName(int s) { return s == 1 ? Lang.T("poste", "rig") : s == 2 ? "ExpertSDR" : s == 3 ? "HDSDR" : s == DivTwoWebSdr ? Lang.T("second WebSDR", "second WebSDR") : "—"; }

        // Réceptions possibles maintenant (le menu ne propose qu'elles).
        bool DivAvailable(int s)
        {
            if (s == 1) return worker != null && settings.RecCardId.Length > 0;
            if (s == 2) return esdrRunning;
            if (s == 3) return hdsdrRunning && AudioTap.ProcessCaptureWorks;
            if (s == DivTwoWebSdr) return webEnv != null && settings.Div2Url.Length > 0;
            return false;
        }

        void FillDiversityMenu(ContextMenuStrip m)
        {
            m.Items.Clear();
            ToolStripMenuItem head = new ToolStripMenuItem(Lang.T("Dans l'autre oreille :", "In the other ear:"));
            head.Enabled = false;
            m.Items.Add(head);
            bool any = false;
            for (int s = 1; s <= 3; s++)
            {
                int src = s;
                if (s == 1 && worker != null && settings.RecCardId.Length == 0)
                {
                    ToolStripMenuItem need = new ToolStripMenuItem(Lang.T("Poste : choisissez d'abord sa carte son (clic droit sur REC)", "Rig: first choose its sound card (right-click REC)"));
                    need.Enabled = false;
                    m.Items.Add(need);
                    continue;
                }
                if (!DivAvailable(s)) continue;
                any = true;
                string text = s == 1 ? Lang.T("Poste (carte son de REC)", "Rig (REC sound card)") : s == 2 ? Lang.T("ExpertSDR (son par TCI)", "ExpertSDR (audio over TCI)") : "HDSDR";
                ToolStripMenuItem it = new ToolStripMenuItem(text);
                it.Checked = (diversity != null ? divSource : settings.DiversitySource) == s;
                it.Click += delegate { SetDiversitySource(src); };
                m.Items.Add(it);
            }
            if (!any && settings.DiversitySource != DivTwoWebSdr)
            {
                ToolStripMenuItem none = new ToolStripMenuItem(Lang.T("Aucune réception locale : connectez le poste, ou lancez ExpertSDR", "No local reception: connect the rig, or start ExpertSDR"));
                none.Enabled = false;
                m.Items.Add(none);
            }
            ToolStripMenuItem two = new ToolStripMenuItem(Lang.T("Sur deux WebSDR (le second choisi dans la case après Diversité)", "On two WebSDRs (the second one chosen in the box after Diversity)"));
            two.Checked = (diversity != null ? divSource : settings.DiversitySource) == DivTwoWebSdr;
            two.Click += delegate { SetDiversitySource(DivTwoWebSdr); };
            m.Items.Add(two);
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem swap = new ToolStripMenuItem(Lang.T("WebSDR dans l'oreille droite", "WebSDR in the right ear"));
            swap.Checked = settings.DiversitySwap;
            swap.Click += delegate
            {
                settings.DiversitySwap = !settings.DiversitySwap;
                settings.Save();
                if (diversity != null) diversity.Swap = settings.DiversitySwap;
                ShowStatus(settings.DiversitySwap ? Lang.T("Diversité : WebSDR à droite", "Diversity: WebSDR on the right") : Lang.T("Diversité : WebSDR à gauche", "Diversity: WebSDR on the left"));
            };
            m.Items.Add(swap);
            if (diversity != null)
            {
                ToolStripMenuItem info = new ToolStripMenuItem(DiversityStateText());
                info.Enabled = false;
                m.Items.Add(info);
            }
        }

        void SetDiversitySource(int s)
        {
            settings.DiversitySource = s;
            settings.Save();
            UpdateDiv2Combo();
            if (diversity != null) { StopDiversity(null); StartDiversity(); }
            else if (s == DivTwoWebSdr && settings.Div2Url == "") ShowStatus(Lang.T("Diversité sur deux WebSDR : choisissez le second dans la case après Diversité", "Diversity on two WebSDRs: choose the second one in the box after Diversity"));
            else ShowStatus(Lang.F("Diversité : {0} dans l'autre oreille", "Diversity: {0} in the other ear", DivSourceName(s)));
            UpdateDiversityButton();
        }

        void SetDiversity(bool on)
        {
            if (on && diversity == null) StartDiversity();
            else if (!on && diversity != null) StopDiversity(Lang.T("Diversité arrêtée : chaque réception retrouve son son", "Diversity off: each reception gets its own audio back"));
            UpdateDiversityButton();
        }

        void StartDiversity()
        {
            if (settings.DiversitySource == DivTwoWebSdr && !DivAvailable(DivTwoWebSdr))
            {
                ShowStatus(Lang.T("Diversité sur deux WebSDR : choisissez le second dans la case après Diversité", "Diversity on two WebSDRs: choose the second one in the box after Diversity"));
                UpdateDiv2Combo();
                if (cbUrl2 != null) cbUrl2.Focus();
                SetDiversityChecked(false);
                return;
            }
            int src = DivAvailable(settings.DiversitySource) ? settings.DiversitySource : 0;
            for (int s = 1; s <= 3 && src == 0; s++) if (DivAvailable(s)) src = s;
            if (src == 0)
            {
                ShowStatus(worker != null && settings.RecCardId.Length == 0
                    ? Lang.T("Diversité : choisissez d'abord la carte son du poste (clic droit sur REC)", "Diversity: first choose the rig's sound card (right-click REC)")
                    : Lang.T("Diversité : aucune réception locale (connectez le poste, ou lancez ExpertSDR)", "Diversity: no local reception (connect the rig, or start ExpertSDR)"));
                SetDiversityChecked(false);
                return;
            }
            if (web.CoreWebView2 == null)
            {
                ShowStatus(Lang.T("Diversité : pas de WebSDR ouvert", "Diversity: no WebSDR open"));
                SetDiversityChecked(false);
                return;
            }
            divSource = src;
            StereoOut o = new StereoOut(settings.DiversityOut);
            o.Problem += msg => BeginInvokeSafe(delegate
            {
                if (divOut != o || msg == null) return;
                Log.Write("Diversité : sortie : " + msg);
                ShowStatus(Lang.F("Diversité : sortie son indisponible ({0})", "Diversity: audio output not available ({0})", msg));
            });
            divOut = o;
            diversity = new DiversityMixer(o.Write);
            diversity.Swap = settings.DiversitySwap;
            diversity.MaxGainB = src == 3 ? 3000 : 30;
            diversity.LeadEnv = src == DivTwoWebSdr ? DiversityMixer.MaxLeadEnv : 0;
            Action<int, float[], int> feed = diversity.FeedB;
            if (src == DivTwoWebSdr) OpenWeb2();
            else if (src == 1) divTap = new AudioTap(settings.RecCardId, 0, settings.RecCardName, feed);
            else if (src == 2) divTap = new AudioTap(settings.EsdrTciPort, "ExpertSDR", feed);
            else divTap = new AudioTap(null, RootProcess(hdsdrPids.Keys), "HDSDR", feed);
            AudioTap t = divTap;
            if (t != null) t.Problem += msg => BeginInvokeSafe(delegate
            {
                if (divTap != t || msg == null) return;
                Log.Write("Diversité : son de « " + t.Name + " » : " + msg);
                // ExpertSDR coupé par TCI et muet aussi sur son flux TCI : on le rétablit et on baisse plutôt son volume (1.45).
                if (divSource == 2 && t.TciNoAudio && divEsdrSilenced && !divEsdrByVolume && EsdrSilenceByVolume()) return;
                ShowStatus(Lang.F("Diversité : {0} : son indisponible ({1})", "Diversity: {0}: audio not available ({1})", DivSourceName(divSource), msg));
            });
            SilenceDivSource(true);
            divShownLocked = false;
            divShownDelay = -1;
            UpdateDiversityGains();
            ApplyVolume();
            UpdatePageTap();
            UpdateRigMonitor();
            if (divTimer == null)
            {
                divTimer = new System.Windows.Forms.Timer { Interval = 500 };
                divTimer.Tick += delegate { DiversityTick(); };
            }
            divTimer.Start();
            SetDiversityChecked(true);
            Log.Write("Diversité : WebSDR + " + DivSourceName(src));
            string warn = src == 1 && rigAfSupported != 1
                ? Lang.T(" ; SDRLink ne peut pas couper le haut-parleur de ce poste : baissez son volume à la main", "; SDRLink cannot mute this rig's speaker: turn its volume down by hand") : "";
            if (src == DivTwoWebSdr) ShowStatus(Lang.F("Diversité : WebSDR affiché dans une oreille, « {0} » dans l'autre ; recherche du retard…", "Diversity: WebSDR shown in one ear, \"{0}\" in the other; measuring the delay…", Div2Name()));
            else ShowStatus(Lang.F("Diversité : WebSDR dans une oreille, {0} dans l'autre ; recherche du retard du WebSDR…", "Diversity: WebSDR in one ear, {0} in the other; measuring the WebSDR delay…", DivSourceName(src)) + warn);
        }

        void StopDiversity(string message)
        {
            if (diversity == null) return;
            if (divTimer != null) divTimer.Stop();
            AudioTap t = divTap;
            divTap = null;
            if (t != null) t.Stop();
            CloseWeb2();
            StereoOut o = divOut;
            divOut = null;
            if (o != null) o.Stop();
            diversity = null;
            SilenceDivSource(false);
            divSource = 0;
            ApplyVolume();
            UpdatePageTap();
            UpdateRigMonitor();
            SetDiversityChecked(false);
            Log.Write("Diversité arrêtée");
            if (message != null) ShowStatus(message);
        }

        void SetDiversityChecked(bool on) { UpdateDiversityButton(); }

        // Sortie directe de la réception locale coupée (on) ou rendue (off), sans toucher à son Mute ni à son volume dans SDRLink.
        void SilenceDivSource(bool on)
        {
            if (on)
            {
                if (divSource == 1 && worker != null && rigAfSupported == 1) { worker.SetAfGain(0); divRigSilenced = true; }
                if (divSource == 2)
                {
                    bool done = false;
                    if (esdrWin) { try { done = AudioEndpoints.ProcessMute(IsExpertSdr, true).HasValue; } catch (Exception ex) { Log.Write("Diversité, ExpertSDR : " + ex.Message); } }
                    // Son sorti par le poste ou par ASIO : coupure par TCI (si ExpertSDR coupe aussi son flux TCI, l'oreille reste muette).
                    if (!done && esdrTci != null && esdrTci.Connected) { esdrTci.SetMute(true); done = true; }
                    divEsdrSilenced = done;
                }
                if (divSource == 3 && hdsdrWin)
                {
                    try { divHdsdrSilenced = AudioEndpoints.ProcessLevel(IsHdsdr, 0.001f); } catch (Exception ex) { Log.Write("Diversité, HDSDR : " + ex.Message); }
                }
                return;
            }
            if (divRigSilenced)
            {
                divRigSilenced = false;
                if (worker != null && rigAfSupported == 1) worker.SetAfGain(rigMuted ? 0 : trkRigVolume.Value);
            }
            if (divEsdrSilenced)
            {
                divEsdrSilenced = false;
                if (divEsdrByVolume)
                {
                    divEsdrByVolume = false;
                    if (esdrTci != null && esdrTci.Connected) esdrTci.SetVolumePercent(divEsdrVolumeBefore >= 0 ? divEsdrVolumeBefore : trkEsdrVolume.Value);
                    Log.Write("Diversité : volume d'ExpertSDR rendu");
                }
                if (esdrWin) { try { AudioEndpoints.ProcessMute(IsExpertSdr, esdrMuted); } catch { } }
                else if (esdrTci != null && esdrTci.Connected) esdrTci.SetMute(esdrMuted);
            }
            if (divHdsdrSilenced)
            {
                divHdsdrSilenced = false;
                try { AudioEndpoints.ProcessVolume(IsHdsdr, trkHdsdrVolume.Value); } catch { }
            }
        }

        // 1.45 : ExpertSDR coupé par TCI (son par le poste ou ASIO) et aucun son sur son flux TCI : la coupure (mute) tait peut-être
        // aussi ce flux. On rétablit le son d'ExpertSDR et on baisse son volume au minimum (-60 dB) ; l'oreille d'ExpertSDR
        // est remontée par le mélangeur (MaxGainB). Le volume d'avant est rendu à l'arrêt de la diversité.
        bool divEsdrByVolume;
        int divEsdrVolumeBefore = -1;

        bool EsdrSilenceByVolume()
        {
            if (esdrWin || esdrTci == null || !esdrTci.Connected || diversity == null) return false;
            divEsdrVolumeBefore = esdrTci.VolumePercent >= 0 ? esdrTci.VolumePercent : trkEsdrVolume.Value;
            divEsdrByVolume = true;
            esdrTci.SetMute(false);
            esdrTci.SetVolumePercent(0);
            diversity.MaxGainB = 3000;
            Log.Write("Diversité : pas de son TCI avec ExpertSDR coupé : son rétabli, volume d'ExpertSDR baissé au minimum (avant : " + divEsdrVolumeBefore + " %)");
            ShowStatus(Lang.T("Diversité : ExpertSDR coupé n'envoie pas de son par TCI ; essai avec son volume au minimum à la place…", "Diversity: muted ExpertSDR sends no audio over TCI; trying with its volume at minimum instead…"));
            return true;
        }

        DateTime divLastTx = DateTime.MinValue;

        // Mute et Volume de chaque réception règlent son oreille.
        // Émission : la coupure de la page (IsMuted) ne touche pas le son rejoué par la diversité, et les deux oreilles sont
        // jouées en retard (le son local est retardé, le WebSDR l'est par nature). Les oreilles choisies au clic droit de
        // « Couper le son du WebSDR en émission » se taisent donc dès le passage en émission et jusqu'à « retard appliqué +
        // délai de rétablissement » après la fin ; l'oreille du poste se tait en émission dans tous les cas.
        void UpdateDiversityGains()
        {
            DiversityMixer d = diversity;
            if (d == null) return;
            bool tx = Transmitting;
            if (tx) divLastTx = DateTime.UtcNow;
            double hold = Math.Max(d.DelaySec, d.DelayASec) + (double)numUnmute.Value / 1000.0;
            bool txHold = tx || (DateTime.UtcNow - divLastTx).TotalSeconds < hold;
            bool muteA = txHold && chkMute.Checked && MuteOnTxFor(0);
            int bOf = divSource == DivTwoWebSdr ? 0 : divSource;   // second WebSDR : réglé comme le WebSDR affiché
            bool muteB = txHold && (divSource == 1 || (chkMute.Checked && MuteOnTxFor(bOf)));
            d.GainA = muteA || sdrMuted || (fdvVoice && fdvSource == 0) ? 0 : sdrVolume / 100f;
            float b;
            if (divSource == 1) b = rigMuted ? 0 : trkRigVolume.Value / 100f;
            else if (divSource == 2) b = esdrMuted ? 0 : trkEsdrVolume.Value / 100f;
            else if (divSource == 3) b = hdsdrMuted ? 0 : trkHdsdrVolume.Value / 100f;
            else b = sdrMuted ? 0 : sdrVolume / 100f;
            if (muteB || (fdvVoice && fdvSource == bOf)) b = 0;
            d.GainB = b;
        }

        void DiversityTick()
        {
            if (diversity == null) return;
            if (!DivAvailable(divSource) || web.CoreWebView2 == null)
            {
                StopDiversity(Lang.F("Diversité arrêtée : {0} n'est plus disponible", "Diversity off: {0} is no longer available", DivSourceName(divSource)));
                return;
            }
            // Réception locale réapparue dans le mélangeur, ou volume du poste remis par son bouton : on la refait taire.
            if (divSource == 2 && !divEsdrSilenced) SilenceDivSource(true);
            if (divSource == 3 && !divHdsdrSilenced) SilenceDivSource(true);
            if (divSource == 1 && !divRigSilenced && rigAfSupported == 1) SilenceDivSource(true);
            if (divSource == DivTwoWebSdr) Div2Sync();
            UpdateDiversityGains();
            bool locked = diversity.Locked;
            double delay = divSource == DivTwoWebSdr ? diversity.GapSec : diversity.DelaySec;
            if (locked && (!divShownLocked || Math.Abs(delay - divShownDelay) > 0.15))
            {
                Log.Write("Diversité : retard " + delay.ToString("0.00", CultureInfo.InvariantCulture) + " s (corrélation " + diversity.Quality.ToString("0.00", CultureInfo.InvariantCulture) + ")");
                ShowStatus(DiversityStateText());
                divShownDelay = delay;
            }
            divShownLocked = locked;
            UpdateDiversityButton();
        }

        string DiversityStateText()
        {
            DiversityMixer d = diversity;
            if (d == null) return "";
            if (!d.HearsA) return Lang.T("Diversité : le WebSDR ne donne pas de son", "Diversity: the WebSDR gives no audio");
            if (!d.HearsB) return Lang.F("Diversité : pas de son de {0}", "Diversity: no audio from {0}", DivSourceName(divSource));
            if (divSource == DivTwoWebSdr && d.Locked)
                return d.GapSec >= 0
                    ? Lang.F("Diversité : deux WebSDR calés, « {0} » arrive {1} s avant le WebSDR affiché", "Diversity: two WebSDRs locked, \"{0}\" arrives {1} s before the WebSDR shown", Div2Name(), Math.Abs(d.GapSec).ToString("0.00"))
                    : Lang.F("Diversité : deux WebSDR calés, « {0} » arrive {1} s après le WebSDR affiché", "Diversity: two WebSDRs locked, \"{0}\" arrives {1} s after the WebSDR shown", Div2Name(), Math.Abs(d.GapSec).ToString("0.00"));
            if (!d.Locked) return Lang.T("Diversité : recherche du retard du WebSDR (il faut un signal entendu des deux côtés)…", "Diversity: measuring the WebSDR delay (a signal heard on both sides is needed)…");
            return Lang.F("Diversité : son de {0} retardé de {1} s pour tomber avec le WebSDR", "Diversity: {0} audio delayed by {1} s to match the WebSDR",
                DivSourceName(divSource), d.DelaySec.ToString("0.00"));
        }

        string divButtonShown;   // état affiché (texte + couleur) : le bouton n'est retouché que s'il change

        // Appelé toutes les 500 ms pendant la diversité : rien n'est retouché si l'état affiché n'a pas changé, et la largeur
        // du bouton est fixée sur son texte le plus long. Avant la 1.41, chaque passage relançait la mise en page de la
        // fenêtre (FlatStyle reposé), ce qui refermait aussitôt une liste déroulante ouverte (liste des favoris…).
        void UpdateDiversityButton(bool force = false)
        {
            if (chkDiversity == null) return;
            DiversityMixer d = diversity;
            string word = Lang.T("Diversité", "Diversity");
            string t = word;
            if (d != null) t += d.Locked ? " " + (divSource == DivTwoWebSdr ? Math.Abs(d.GapSec) : d.DelaySec).ToString("0.0") + " s" : " …";
            string key = t + "|" + (d == null ? 0 : d.Locked ? 2 : 1) + "|" + Theme.Dark;
            if (!force && key == divButtonShown) return;
            bool resize = force || divButtonShown == null;
            divButtonShown = key;
            chkDiversity.SuspendLayout();
            if (resize)
            {
                int w = 0;
                chkDiversity.MinimumSize = new Size(30, 25);
                foreach (string s in new[] { word, word + " …", word + " " + (8.8).ToString("0.0") + " s" })
                {
                    chkDiversity.Text = s;
                    w = Math.Max(w, chkDiversity.GetPreferredSize(Size.Empty).Width);
                }
                chkDiversity.MinimumSize = new Size(w, 25);
            }
            chkDiversity.Text = t;
            chkDiversity.ResumeLayout();
            if (d != null)
            {
                chkDiversity.FlatStyle = FlatStyle.Flat;
                chkDiversity.UseVisualStyleBackColor = false;
                chkDiversity.BackColor = d.Locked ? Color.FromArgb(0, 120, 60) : Color.FromArgb(200, 120, 0);
                chkDiversity.ForeColor = Color.White;
                chkDiversity.FlatAppearance.BorderColor = d.Locked ? Color.FromArgb(0, 150, 75) : Color.FromArgb(160, 90, 0);
            }
            else Theme.StyleButton(chkDiversity);
            // Couleurs choisies au clic droit (1.41) : arrêt, recherche du retard, calé.
            int custom = ColorOf(d == null ? "DiversityOff" : d.Locked ? "DiversityOn" : "DiversitySearch");
            if (custom >= 0) PaintCustom(chkDiversity, custom);
        }
    }
}
