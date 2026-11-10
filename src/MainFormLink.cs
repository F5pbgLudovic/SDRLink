using System;
using System.Drawing;
using System.Security.Principal;
using System.Windows.Forms;

namespace SDRLink
{
    // Contrôle de la liaison avec le poste : une première fois 15 s après « Connecter », puis chaque minute.
    // La boucle du poste (RigWorker) interroge déjà le poste plusieurs fois par seconde ; ce contrôle repère en plus
    // une liaison figée (appel à OmniRig ou au port COM qui ne revient plus) et propose l'aide OmniRig si rien ne répond.
    public partial class MainForm
    {
        const int LinkFirstCheckMs = 15000, LinkCheckMs = 60000;
        const int LinkMaxSilenceMs = 15000;      // au-delà, sans lecture réussie, la liaison est vue perdue
        const int ConnectWaitDefault = 0xF0A030; // orange : liaison lancée, poste muet
        const string OmniRigPage = "https://www.dxatlas.com/omnirig/";

        Timer linkTimer;
        string lastRigMsg;          // dernier message de la boucle du poste (repris dans l'aide OmniRig)
        bool omniHelpShown;         // aide OmniRig déjà montrée pour cette connexion
        Form omniHelp;

        void StartLinkCheck()
        {
            if (linkTimer == null)
            {
                linkTimer = new Timer();
                linkTimer.Tick += delegate { CheckLink(); };
            }
            omniHelpShown = false;
            lastRigMsg = null;
            linkTimer.Stop();
            linkTimer.Interval = LinkFirstCheckMs;
            linkTimer.Start();
        }

        void StopLinkCheck()
        {
            if (linkTimer != null) linkTimer.Stop();
        }

        void CheckLink()
        {
            if (worker == null) { StopLinkCheck(); return; }
            linkTimer.Interval = LinkCheckMs;
            int last = worker.LastOkTick;
            int age = unchecked(Environment.TickCount - last);
            bool fresh = last != 0 && age >= 0 && age < LinkMaxSilenceMs;
            if (rigOnline && fresh) return;
            if (rigOnline)
            {
                // Le poste était vu en ligne mais plus aucune lecture n'aboutit : liaison figée.
                rigOnline = false;
                worker.ResetStatus();
                lastRigMsg = Lang.F("Liaison perdue : le poste ne répond plus depuis {0} s", "Link lost: the rig has not answered for {0} s", age / 1000);
                Log.Write("Contrôle de la liaison : poste muet depuis " + age / 1000 + " s");
                ShowStatus(lastRigMsg);
                UpdateConnectButton();
                UpdateDisplays();
            }
            else
            {
                Log.Write("Contrôle de la liaison : le poste ne répond pas" + (lastRigMsg != null ? " (" + lastRigMsg + ")" : ""));
                ShowStatus(Lang.T("Le poste ne répond toujours pas : vérifiez qu'il est allumé et le réglage de la liaison", "The rig is still not answering: check that it is switched on and the link settings") +
                    (lastRigMsg != null ? " (" + lastRigMsg + ")" : ""));
            }
            if (settings.Interface == 1 && !omniHelpShown) ShowOmniRigHelp();
        }

        static bool RunningAsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        // Fenêtre d'aide au premier plan, non bloquante : SDRLink continue d'essayer pendant qu'on suit les conseils.
        void ShowOmniRigHelp()
        {
            omniHelpShown = true;
            Log.Write("Aide OmniRig affichée");
            if (omniHelp != null && !omniHelp.IsDisposed) { omniHelp.Close(); omniHelp = null; }
            Form f = new Form();
            f.Text = Lang.T("SDRLink : pas de liaison avec OmniRig", "SDRLink: no link with OmniRig");
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.TopMost = true;

            f.Font = Font;
            f.Icon = Icon;
            const int W = 560;
            int y = 14;
            Func<string, bool, Label> add = delegate(string text, bool bold)
            {
                Label l = new Label { AutoSize = true, MaximumSize = new Size(W - 28, 0), Location = new Point(14, y), Text = text };
                if (bold) l.Font = new Font(f.Font, FontStyle.Bold);
                f.Controls.Add(l);
                y += l.PreferredSize.Height + 8;
                return l;
            };
            add(Lang.F("SDRLink n'arrive pas à dialoguer avec le poste par OmniRig (Rig {0}).", "SDRLink cannot talk to the rig through OmniRig (Rig {0}).", settings.OmniRigNumber), true);
            if (!string.IsNullOrEmpty(lastRigMsg)) add(Lang.T("Dernier message : ", "Last message: ") + lastRigMsg, false);
            y += 4;
            add(Lang.T("À essayer :", "Things to try:"), true);
            add(Lang.T("1. Utilisez la version 1 d'OmniRig (OmniRig 1.x de VE3NEA), à télécharger sur la page ci-dessous.",
                       "1. Use version 1 of OmniRig (OmniRig 1.x by VE3NEA), available from the page below."), false);
            add(Lang.T("2. Fermez OmniRig et SDRLink, puis relancez-les tous les deux en mode administrateur : clic droit sur le programme, puis « Exécuter en tant qu'administrateur ».",
                       "2. Close OmniRig and SDRLink, then start both of them again as administrator: right-click the program, then “Run as administrator”."), false);
            add("    " + (RunningAsAdmin()
                ? Lang.T("SDRLink tourne en ce moment en mode administrateur.", "SDRLink is currently running as administrator.")
                : Lang.T("SDRLink ne tourne pas en ce moment en mode administrateur.", "SDRLink is not currently running as administrator.")), false);
            add(Lang.T("3. Dans OmniRig (Rig 1 ou Rig 2, celui choisi dans SDRLink), vérifiez le modèle du poste, le port COM et la vitesse ; aucun autre programme ne doit utiliser ce port COM.",
                       "3. In OmniRig (Rig 1 or Rig 2, the one chosen in SDRLink), check the rig model, the COM port and the baud rate; no other program may use this COM port."), false);
            add(Lang.T("Ensuite, cliquez sur le bouton « Connexion... » pour arrêter, puis sur « Connecter ».",
                       "Then click the “Connecting...” button to stop, then click “Connect”."), false);
            LinkLabel link = new LinkLabel { AutoSize = true, Location = new Point(14, y), Text = OmniRigPage };
            link.LinkClicked += delegate { OpenUrl(OmniRigPage); };
            f.Controls.Add(link);
            y += 34;
            Button close = new Button { Text = Lang.T("Fermer", "Close"), DialogResult = DialogResult.Cancel };
            int cw = Math.Max(110, TextRenderer.MeasureText(close.Text, f.Font).Width + 30);
            close.SetBounds(W - 14 - cw, y, cw, 30);
            close.Click += delegate { f.Close(); };
            f.Controls.Add(close);
            f.CancelButton = f.AcceptButton = close;
            f.ClientSize = new Size(W, y + 44);
            Theme.Apply(f);
            link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = Theme.RigFreq;
            // Centrée sur SDRLink (CenterParent ne joue pas pour une fenêtre non bloquante), sans sortir de l'écran.
            Rectangle area = WorkArea;
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - f.Width, Left + (Width - f.Width) / 2)),
                                   Math.Max(area.Top, Math.Min(area.Bottom - f.Height, Top + (Height - f.Height) / 2)));
            f.FormClosed += delegate { if (omniHelp == f) omniHelp = null; f.Dispose(); };
            omniHelp = f;
            f.Show(this);
            f.Activate();
        }
    }
}
