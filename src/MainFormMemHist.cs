using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SDRLink
{
    // Clic gauche sur la fréquence RADIO ou WEBSDR : clavier de fréquence avec la liste « Mem » (les 20 dernières fréquences,
    // « Store », enregistrement automatique « Auto »), comme dans PhoneSDRLink.
    public partial class MainForm : IMemHistHost
    {
        Form freqPopup;                         // clavier ouvert (la molette sur les fréquences ne règle rien pendant ce temps)
        readonly Timer memHistTimer = new Timer { Interval = 1000 };
        long memHistHz = -1;                    // dernier état vu : fréquence, mode, mode numérique
        string memHistMode = "", memHistDec = "";
        DateTime memHistDue = DateTime.MaxValue; // prochain enregistrement automatique

        void InitFreqKeypad()
        {
            foreach (Label l in new[] { lblRig, lblSdr })
            {
                Label lbl = l;
                lbl.Cursor = Cursors.Hand;
                lbl.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowFreqKeypad(lbl == lblSdr); };
                tips.SetToolTip(lbl, Lang.T("Clic : clavier de fréquence et liste Mem des dernières fréquences ; molette : changer de fréquence (clic droit : couleurs)",
                    "Click: frequency keypad and Mem list of the last frequencies; wheel: change the frequency (right-click: colours)"));
            }
            memHistTimer.Tick += delegate { MemHistTick(); };
            memHistTimer.Start();
        }

        void ShowFreqKeypad(bool sdr)
        {
            if (freqPopup != null) return;
            long start = sdr ? (sdrKind != "" ? sdrF : 0) : (worker != null && rigF > 0 ? rigF : CurrentFreq());
            Label from = sdr ? lblSdr : lblRig;
            using (FreqKeypad k = new FreqKeypad(this, start, sdr ? Theme.SdrFreq : Theme.RigFreq, Font))
            {
                Rectangle wa = WorkArea;
                k.WorkArea = wa;
                Point p = from.PointToScreen(new Point(0, from.Height + 2));
                k.Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - k.Width, p.X)), Math.Max(wa.Top, Math.Min(wa.Bottom - k.Height, p.Y)));
                k.TopMost = TopMost;
                freqPopup = k;
                DialogResult r;
                try { r = k.ShowDialog(this); }
                finally { freqPopup = null; }
                if (r == DialogResult.Abort && k.Recall != null) { MemHistRecall(k.Recall); return; }
                if (r != DialogResult.OK || k.ResultHz <= 0) return;
                if (scanning) StopScan(null);
                long hz = k.ResultHz;
                // Même règle que la molette : selon le sens de synchronisation, la fréquence cliquée règle les deux ou la sienne seule.
                bool sdrOnly = sdr && (Dir == Direction.RigToSdr || Dir == Direction.Off);
                bool rigOnly = !sdr && (Dir == Direction.SdrToRig || Dir == Direction.Off) && worker != null && rigOnline;
                if (sdrOnly) TuneSdrOnly(hz);
                else if (rigOnly) TuneRigOnly(hz);
                else { long rigHz = sdr ? hz - Offset : hz; if (rigHz <= 0) return; TuneBoth(rigHz); AddToHistory(rigHz); }
                Log.Write("Clavier de fréquence : " + Memory.KHz(hz) + " kHz" + (sdrOnly ? " (WebSDR)" : rigOnly ? " (poste)" : ""));
            }
        }

        // ---- IMemHistHost

        public List<MemHistEntry> MemHist { get { return settings.MemHist; } }

        public int MemHistMinutes
        {
            get { return settings.MemHistMin; }
            set
            {
                if (settings.MemHistMin == value) return;
                settings.MemHistMin = value;
                settings.Save();
                MemHistSchedule();
                Log.Write("Liste Mem : enregistrement automatique " + (value > 0 ? "après " + value + " min" : "arrêté"));
            }
        }

        public string[] MemoryListLabels
        {
            get
            {
                MemoryBook b = settings.Memories;
                string[] r = new string[b.ListCount];
                for (int i = 0; i < r.Length; i++) r[i] = FavoritesDialog.ListLabel(b.ListNames, i);
                return r;
            }
        }

        // Mode actuel : celui du poste s'il est connecté, sinon celui du WebSDR (aussi en synchro désactivée).
        string MemHistModeNow()
        {
            if (worker != null && rigF > 0 && rigM != null && !SdrAlone) return rigM;
            string fam = Modes.SdrFamily(sdrM);
            if (SdrAlone && digiActive && (fam == "usb" || fam == "lsb")) return digiMode;
            return fam != "" ? Modes.FromSdrFamily(fam) : (clickedMode ?? "");
        }

        // Mode numérique choisi et en marche (FreeDV auto n'en fait pas partie).
        string MemHistDecNow() { return settings.SstvAuto ? CurDigiMode : ""; }

        public void MemHistStore()
        {
            long hz = CurrentFreq();
            if (hz <= 0) { ShowStatus(Lang.T("Aucune fréquence à mémoriser (ni poste ni WebSDR).", "No frequency to store (no rig, no WebSDR).")); return; }
            MemHistEntry.Add(settings.MemHist, hz, MemHistModeNow(), MemHistDecNow());
            settings.Save();
            ShowStatus(Lang.F("Mémorisé : {0} kHz", "Stored: {0} kHz", Memory.KHz(hz)));
        }

        public string MemHistText(MemHistEntry e)
        {
            return Memory.KHz(e.Hz) + " kHz" + (e.Mode != "" ? "  " + Modes.Display(e.Mode) : "") + (e.Dec != "" ? "  " + DigiName(e.Dec) : "");
        }

        // Rappel : fréquence et mode (poste et WebSDR), puis le mode numérique s'il y en avait un.
        public void MemHistRecall(MemHistEntry e)
        {
            if (e == null || e.Hz <= 0) return;
            if (scanning) StopScan(null);
            ApplyMemory(new Memory("", e.Hz, e.Mode));
            AddToHistory(e.Hz);
            if (e.Dec != "" && Array.IndexOf(DigiModes, e.Dec) >= 0) SelectDigiMode(e.Dec, false);
            string t = MemHistText(e);
            Log.Write("Liste Mem : " + t);
            ShowStatus("Mem : " + t);
        }

        public void MemHistCopy(MemHistEntry e, int list)
        {
            Memory m = new Memory("", e.Hz, e.Mode) { List = list };
            settings.Memories.Items.Add(m);
            settings.Save();
            ShowStatus(Lang.F("Mémoire « {0} » ajoutée à « {1} ».", "Memory \"{0}\" added to \"{1}\".", m.Label,
                FavoritesDialog.ListLabel(settings.Memories.ListNames, list)));
        }

        public void MemHistChanged() { settings.Save(); }

        // ---- enregistrement automatique : seulement si l'on reste le temps choisi sur la même fréquence, le même mode et
        // le même mode numérique ; tout changement remet le compteur à zéro. Ensuite, de nouveau à chaque échéance.

        void MemHistSchedule()
        {
            memHistDue = settings.MemHistMin > 0 ? DateTime.UtcNow.AddMinutes(settings.MemHistMin) : DateTime.MaxValue;
        }

        void MemHistTick()
        {
            if (settings == null) return;
            long hz = CurrentFreq();
            string mode = MemHistModeNow(), dec = MemHistDecNow();
            if (hz != memHistHz || mode != memHistMode || dec != memHistDec)
            {
                memHistHz = hz; memHistMode = mode; memHistDec = dec;
                MemHistSchedule();
                return;
            }
            if (hz <= 0 || settings.MemHistMin <= 0 || DateTime.UtcNow < memHistDue) return;
            MemHistEntry.Add(settings.MemHist, hz, mode, dec);
            settings.Save();
            Log.Write("Liste Mem : enregistrement automatique " + Memory.KHz(hz) + " kHz");
            MemHistSchedule();
        }
    }
}
