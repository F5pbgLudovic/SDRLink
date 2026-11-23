using System;
using System.Windows.Forms;

namespace SDRLink
{
    // ---------------------------------------------------------------- Micro du PC vers le poste (1.51)
    // Le micro choisi est envoyé en continu, sans fenêtre, sur la carte son d'émission du poste (la même que Tune), en WASAPI
    // partagé : les autres logiciels gardent le micro, rien n'est rejoué sur le PC. Le poste ne l'émet qu'en émission, avec
    // sa source de modulation sur l'USB / l'arrière. Réglage au clic droit sur PTT. Pendant Tune, le micro se tait.
    public partial class MainForm
    {
        RigMonitor micRoute;
        bool micRouteTrouble;

        void AddMicRouteItems(ContextMenuStrip m)
        {
            m.Items.Add(new ToolStripSeparator());
            string mic = settings.MicRouteId.Length > 0 ? settings.MicRouteName : Lang.T("micro à choisir", "microphone to choose");
            string card = settings.TuneCardId.Length > 0 ? settings.TuneCardName : Lang.T("carte son à choisir", "sound card to choose");
            ToolStripMenuItem on = new ToolStripMenuItem(Lang.F("Micro du PC vers le poste (« {0} » → « {1} »)", "PC microphone to the rig (\"{0}\" → \"{1}\")", mic, card));
            on.Checked = settings.MicRoute;
            on.Click += delegate { SetMicRoute(!settings.MicRoute); };
            m.Items.Add(on);
            ToolStripMenuItem eq = new ToolStripMenuItem(Lang.T("Égaliseur 10 bandes, noise gate, compresseur / limiteur du micro…", "Microphone 10-band equalizer, noise gate, compressor / limiter…"));
            eq.Checked = FxCfg.Active(true);
            eq.Click += delegate { EqForm.Open(this, true); };   // 1.54
            m.Items.Add(eq);

            ToolStripMenuItem mics = new ToolStripMenuItem(Lang.T("Micro du PC", "PC microphone"));
            ToolStripMenuItem cards = new ToolStripMenuItem(Lang.T("Carte son d'émission du poste (sortie du PC reliée au poste)", "Rig transmit sound card (PC output connected to the rig)"));
            try
            {
                foreach (AudioEndpoints.Endpoint e in AudioEndpoints.All())
                {
                    string id = e.Id, name = e.FullName ?? e.Name ?? id;
                    ToolStripMenuItem it = new ToolStripMenuItem(name);
                    if (e.Capture)
                    {
                        it.Checked = id == settings.MicRouteId;
                        it.Click += delegate
                        {
                            settings.MicRouteId = id;
                            settings.MicRouteName = name;
                            settings.Save();
                            Log.Write("Micro vers le poste : micro « " + name + " »");
                            ShowStatus(Lang.F("Micro du PC : « {0} »", "PC microphone: \"{0}\"", name));
                            UpdateMicRoute();
                        };
                        mics.DropDownItems.Add(it);
                    }
                    else
                    {
                        it.Checked = id == settings.TuneCardId;
                        it.Click += delegate
                        {
                            settings.TuneCardId = id;
                            settings.TuneCardName = name;
                            settings.Save();
                            Log.Write("Carte son d'émission du poste : « " + name + " »");
                            ShowStatus(Lang.F("Carte son d'émission du poste : « {0} » (aussi pour Tune)", "Rig transmit sound card: \"{0}\" (also for Tune)", name));
                            UpdateMicRoute();
                        };
                        cards.DropDownItems.Add(it);
                    }
                }
            }
            catch (Exception ex) { Log.Write("Micro vers le poste : lecture des cartes son impossible : " + ex.Message); }
            if (mics.DropDownItems.Count == 0) mics.DropDownItems.Add(new ToolStripMenuItem(Lang.T("Aucune entrée son trouvée dans Windows", "No sound input found in Windows")) { Enabled = false });
            if (cards.DropDownItems.Count == 0) cards.DropDownItems.Add(new ToolStripMenuItem(Lang.T("Aucune sortie son trouvée dans Windows", "No sound output found in Windows")) { Enabled = false });
            m.Items.Add(mics);
            m.Items.Add(cards);
        }

        void SetMicRoute(bool on)
        {
            if (on && (settings.MicRouteId.Length == 0 || settings.TuneCardId.Length == 0))
            {
                ShowStatus(Lang.T("Micro vers le poste : choisissez d'abord le micro et la carte son d'émission du poste (clic droit sur PTT)",
                                  "Microphone to the rig: first choose the microphone and the rig transmit sound card (right-click PTT)"));
                return;
            }
            settings.MicRoute = on;
            settings.Save();
            Log.Write("Micro vers le poste : " + (on ? "activé (" + settings.MicRouteName + " → " + settings.TuneCardName + ")" : "désactivé"));
            ShowStatus(on
                ? Lang.F("Micro du PC envoyé au poste : « {0} » → « {1} »", "PC microphone sent to the rig: \"{0}\" → \"{1}\"", settings.MicRouteName, settings.TuneCardName)
                : Lang.T("Micro du PC vers le poste désactivé", "PC microphone to the rig turned off"));
            UpdateMicRoute();
            UpdateEqButton();
        }

        // Démarre, arrête ou rouvre l'aiguillage selon le réglage ; muet pendant Tune (la tonalité passe seule).
        void UpdateMicRoute()
        {
            bool want = settings.MicRoute && settings.MicRouteId.Length > 0 && settings.TuneCardId.Length > 0;
            if (micRoute != null && (!want || micRoute.CaptureId != settings.MicRouteId || micRoute.OutputId != settings.TuneCardId)) StopMicRoute();
            if (want && micRoute == null)
            {
                RigMonitor r = new RigMonitor(settings.MicRouteId, settings.TuneCardId, tuneTone != null, true, "Micro vers le poste");
                r.Problem += msg => BeginInvokeSafe(delegate { if (micRoute == r) OnMicRouteProblem(msg); });
                micRoute = r;
                micRouteTrouble = false;
            }
            if (micRoute != null) micRoute.Muted = tuneTone != null;
        }

        void OnMicRouteProblem(string msg)
        {
            if (msg == null)
            {
                if (micRouteTrouble) ShowStatus(Lang.T("Micro du PC vers le poste rétabli", "PC microphone to the rig back"));
                micRouteTrouble = false;
                return;
            }
            micRouteTrouble = true;
            Log.Write("Micro vers le poste : « " + settings.MicRouteName + " » → « " + settings.TuneCardName + " » : " + msg);
            ShowStatus(Lang.F("Micro vers le poste : carte son indisponible ({0})", "Microphone to the rig: sound card not available ({0})", msg));
        }

        void StopMicRoute()
        {
            if (micRoute == null) return;
            RigMonitor r = micRoute;
            micRoute = null;
            r.Stop();
            Log.Write("Micro vers le poste arrêté");
        }
    }
}
