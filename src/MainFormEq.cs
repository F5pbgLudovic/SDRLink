using System;
using System.Drawing;
using System.Windows.Forms;

namespace SDRLink
{
    // ---------------------------------------------------------------- Égaliseur 10 bandes (1.54)
    // Bouton EQ après Diversité : réception du WebSDR (filtres dans la page) ; clic droit sur PTT : micro du PC vers le poste
    // (noise gate, égaliseur, compresseur, limiteur ; FxChain dans RigMonitor). Même fenêtre (EqForm) pour les deux.
    public partial class MainForm
    {
        Button btnEq;

        void CreateEqButton()
        {
            FxCfg.S = settings;
            FxCfg.Changed += delegate(bool tx) { if (!tx) SendPageEq(); UpdateEqButton(); };
            btnEq = new Button();
            btnEq.AutoSize = true;
            btnEq.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnEq.MinimumSize = new Size(36, 25);
            btnEq.Tag = Theme.Custom;
            btnEq.Margin = new Padding(6, 3, 3, 3);
            btnEq.Click += delegate { EqForm.Open(this, false); };
            ContextMenuStrip m = new ContextMenuStrip();
            m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                m.Items.Clear();
                ToolStripMenuItem rx = new ToolStripMenuItem(Lang.T("Égaliseur 10 bandes : réception (WebSDR et poste écouté sur le PC)…", "10-band equalizer: receive (WebSDR and rig heard on the PC)…"));
                rx.Checked = FxCfg.Active(false);
                rx.Click += delegate { EqForm.Open(this, false); };
                m.Items.Add(rx);
                ToolStripMenuItem txi = new ToolStripMenuItem(Lang.T("Égaliseur 10 bandes, noise gate, compresseur / limiteur : émission (micro du PC)…", "10-band equalizer, noise gate, compressor / limiter: transmit (PC microphone)…"));
                txi.Checked = FxCfg.Active(true);
                txi.Click += delegate { EqForm.Open(this, true); };
                m.Items.Add(txi);
                e.Cancel = false;
            };
            m.Items.Add("-");
            btnEq.ContextMenuStrip = m;
            AddColorMenu(btnEq, Slot("EqOff", () => Lang.T("Couleur égaliseur arrêté", "Colour when the equalizer is off")),
                Slot("EqOn", () => Lang.T("Couleur égaliseur en marche", "Colour when the equalizer is on")));
            Tr(delegate
            {
                UpdateEqButton();
                tips.SetToolTip(btnEq, Lang.T("Égaliseur 10 bandes. Clic : réception (WebSDR et poste écouté sur le PC). Clic droit : réception ou émission (micro du PC vers le poste, avec noise gate et compresseur / limiteur), couleurs du bouton",
                    "10-band equalizer. Click: receive (WebSDR and rig heard on the PC). Right-click: receive or transmit (PC microphone to the rig, with noise gate and compressor / limiter), button colours"));
            });
        }

        // Texte : EQ, EQ RX, EQ TX ou EQ RX+TX selon ce qui est en marche (TX : seulement quand le micro part vers le poste).
        void UpdateEqButton()
        {
            if (btnEq == null) return;
            bool rx = FxCfg.Active(false), tx = FxCfg.Active(true) && settings.MicRoute;
            string t = "EQ" + (rx && tx ? " RX+TX" : rx ? " RX" : tx ? " TX" : "");
            if (btnEq.Text != t) btnEq.Text = t;
            bool on = rx || tx;
            int c = ColorOf(on ? "EqOn" : "EqOff");
            if (c < 0) c = on ? 0x00783C : -1;   // en marche : vert par défaut, comme DNR
            if (c < 0) Theme.StyleButton(btnEq);
            else PaintCustom(btnEq, c);
        }

        // Page (re)chargée ou réglage changé : l'égaliseur de réception est envoyé au WebSDR affiché et au second WebSDR de la diversité.
        void SendPageEq()
        {
            string js = FxCfg.PageCall();
            try { if (web != null && web.CoreWebView2 != null) web.CoreWebView2.ExecuteScriptAsync(js); } catch { }
            try { if (web2 != null && web2.CoreWebView2 != null) web2.CoreWebView2.ExecuteScriptAsync(js); } catch { }
        }
    }
}
