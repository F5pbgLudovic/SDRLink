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
        CheckButton chkDiversity;
        DiversityMixer diversity;
        StereoOut divOut;
        AudioTap divTap;
        int divSource;                  // 1 poste, 2 ExpertSDR, 3 HDSDR (0 : arrêtée)
        bool divRigSilenced, divEsdrSilenced, divHdsdrSilenced;
        System.Windows.Forms.Timer divTimer;
        bool divShownLocked;
        double divShownDelay = -1;
        bool divSetting;

        void CreateDiversityButton()
        {
            chkDiversity = new CheckButton();
            chkDiversity.Margin = new Padding(6, 3, 3, 3);
            chkDiversity.CheckedChanged += delegate { if (!divSetting) SetDiversity(chkDiversity.Checked); };
            chkDiversity.Click += delegate { chkDiversity.Checked = !chkDiversity.Checked; };
            ContextMenuStrip m = new ContextMenuStrip();
            m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e) { e.Cancel = false; FillDiversityMenu(m); };
            m.Items.Add("-");
            chkDiversity.ContextMenuStrip = m;
            Tr(delegate
            {
                UpdateDiversityButton();
                tips.SetToolTip(chkDiversity, Lang.T("Écoute en diversité : le WebSDR dans une oreille, le poste (ou ExpertSDR, ou HDSDR) dans l'autre, le son local retardé pour tomber en même temps que le WebSDR. Clic : marche / arrêt. Clic droit : réception mise dans l'autre oreille, côté du WebSDR, retard mesuré",
                    "Diversity listening: the WebSDR in one ear, the rig (or ExpertSDR, or HDSDR) in the other, the local audio delayed to match the WebSDR. Click: on / off. Right-click: reception put in the other ear, WebSDR side, measured delay"));
            });
        }

        string DivSourceName(int s) { return s == 1 ? Lang.T("poste", "rig") : s == 2 ? "ExpertSDR" : s == 3 ? "HDSDR" : "—"; }

        // Réceptions possibles maintenant (le menu ne propose qu'elles).
        bool DivAvailable(int s)
        {
            if (s == 1) return worker != null && settings.RecCardId.Length > 0;
            if (s == 2) return esdrRunning;
            if (s == 3) return hdsdrRunning && AudioTap.ProcessCaptureWorks;
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
            if (!any)
            {
                ToolStripMenuItem none = new ToolStripMenuItem(Lang.T("Aucune réception locale : connectez le poste, ou lancez ExpertSDR", "No local reception: connect the rig, or start ExpertSDR"));
                none.Enabled = false;
                m.Items.Add(none);
            }
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
            if (diversity != null) { StopDiversity(null); StartDiversity(); }
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
            Action<int, float[], int> feed = diversity.FeedB;
            if (src == 1) divTap = new AudioTap(settings.RecCardId, 0, settings.RecCardName, feed);
            else if (src == 2) divTap = new AudioTap(settings.EsdrTciPort, "ExpertSDR", feed);
            else divTap = new AudioTap(null, RootProcess(hdsdrPids.Keys), "HDSDR", feed);
            AudioTap t = divTap;
            t.Problem += msg => BeginInvokeSafe(delegate
            {
                if (divTap != t || msg == null) return;
                Log.Write("Diversité : son de « " + t.Name + " » : " + msg);
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
            ShowStatus(Lang.F("Diversité : WebSDR dans une oreille, {0} dans l'autre ; recherche du retard du WebSDR…", "Diversity: WebSDR in one ear, {0} in the other; measuring the WebSDR delay…", DivSourceName(src)) + warn);
        }

        void StopDiversity(string message)
        {
            if (diversity == null) return;
            if (divTimer != null) divTimer.Stop();
            AudioTap t = divTap;
            divTap = null;
            if (t != null) t.Stop();
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

        void SetDiversityChecked(bool on)
        {
            if (chkDiversity == null) return;
            divSetting = true;
            chkDiversity.Checked = on;
            divSetting = false;
            UpdateDiversityButton();
        }

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
                if (esdrWin) { try { AudioEndpoints.ProcessMute(IsExpertSdr, esdrMuted); } catch { } }
                else if (esdrTci != null && esdrTci.Connected) esdrTci.SetMute(esdrMuted);
            }
            if (divHdsdrSilenced)
            {
                divHdsdrSilenced = false;
                try { AudioEndpoints.ProcessVolume(IsHdsdr, trkHdsdrVolume.Value); } catch { }
            }
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
            double hold = d.DelaySec + (double)numUnmute.Value / 1000.0;
            bool txHold = tx || (DateTime.UtcNow - divLastTx).TotalSeconds < hold;
            bool muteA = txHold && chkMute.Checked && MuteOnTxFor(0);
            bool muteB = txHold && (divSource == 1 || (chkMute.Checked && MuteOnTxFor(divSource)));
            d.GainA = muteA || sdrMuted || (fdvVoice && fdvSource == 0) ? 0 : trkVolume.Value / 100f;
            float b;
            if (divSource == 1) b = rigMuted ? 0 : trkRigVolume.Value / 100f;
            else if (divSource == 2) b = esdrMuted ? 0 : trkEsdrVolume.Value / 100f;
            else b = hdsdrMuted ? 0 : trkHdsdrVolume.Value / 100f;
            if (muteB || (fdvVoice && fdvSource == divSource)) b = 0;
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
            UpdateDiversityGains();
            bool locked = diversity.Locked;
            double delay = diversity.DelaySec;
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
            if (!d.Locked) return Lang.T("Diversité : recherche du retard du WebSDR (il faut un signal entendu des deux côtés)…", "Diversity: measuring the WebSDR delay (a signal heard on both sides is needed)…");
            return Lang.F("Diversité : son de {0} retardé de {1} s pour tomber avec le WebSDR", "Diversity: {0} audio delayed by {1} s to match the WebSDR",
                DivSourceName(divSource), d.DelaySec.ToString("0.00"));
        }

        void UpdateDiversityButton()
        {
            if (chkDiversity == null) return;
            DiversityMixer d = diversity;
            string t = Lang.T("Diversité", "Diversity");
            if (d != null) t += d.Locked ? " " + d.DelaySec.ToString("0.0") + " s" : " …";
            chkDiversity.Text = t;
            if (d != null)
            {
                chkDiversity.FlatStyle = FlatStyle.Flat;
                chkDiversity.UseVisualStyleBackColor = false;
                chkDiversity.BackColor = d.Locked ? Color.FromArgb(0, 120, 60) : Color.FromArgb(200, 120, 0);
                chkDiversity.ForeColor = Color.White;
                chkDiversity.FlatAppearance.BorderColor = d.Locked ? Color.FromArgb(0, 150, 75) : Color.FromArgb(160, 90, 0);
            }
            else Theme.StyleButton(chkDiversity);
        }
    }
}
