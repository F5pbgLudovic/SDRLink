using System;
using System.Threading;

namespace SDRLink
{
    // Fait tourner toutes les E/S du poste sur un thread dédié : interrogation périodique, ordres en attente, reconnexion.
    public class RigWorker
    {
        public event Action<long, string> StateReceived;   // fréquence Hz, mode normalisé (peut être null) ; seulement si changé
        public event Action<string, bool> StatusChanged;   // message, connecté ?
        public event Action<int> TxChanged;                // 1 émission, 0 réception, -1 inconnu
        public event Action<int> AfGainChanged;            // volume du poste en %, -1 = non pris en charge

        // Lecture du PTT à chaque tour : seulement si quelqu'un s'en sert, pour ne pas charger la liaison CAT.
        public volatile bool PollTx;

        // Heure (Environment.TickCount) de la dernière lecture réussie du poste : sert au contrôle de la liaison chaque minute.
        public volatile int LastOkTick;

        // Oublie le dernier état annoncé : le prochain état sera de nouveau signalé (après un contrôle qui a vu la liaison perdue).
        public void ResetStatus() { lastStatus = null; }

        readonly Func<IRig> factory;
        readonly int pollMs;
        Thread thread;
        volatile bool running;
        readonly object pendingLock = new object();
        long pendingFreq;
        string pendingMode;
        int pendingPtt = -1;       // -1 : rien à envoyer
        bool pttCommandedOn;       // émission lancée par SDRLink, à couper avant de fermer la liaison
        volatile string lastStatus;

        public RigWorker(Func<IRig> factory, int pollMs)
        {
            this.factory = factory;
            this.pollMs = pollMs;
        }

        public void Start()
        {
            running = true;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "Rig";
            // Mode COM classique (STA), celui qu'attendent les serveurs COM comme OmniRig.
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            if (thread != null) thread.Join(3000);
        }

        // Le dernier ordre l'emporte : on ne garde pas d'historique.
        public void SetFreq(long hz) { lock (pendingLock) pendingFreq = hz; }
        public void SetMode(string mode) { lock (pendingLock) pendingMode = mode; }
        public void SetPtt(bool tx) { lock (pendingLock) pendingPtt = tx ? 1 : 0; }
        public void SetAfGain(int pct) { lock (pendingLock) pendingAf = pct; }

        // Mute TX et Volume TX par une liaison TCI à part (1.56, clic droit sur Mute TX > « Émetteur commandé par TCI ») :
        // port du serveur TCI (Thetis pour un Brick SDR ou un Hermes-Lite, ExpertSDR…), 0 = par l'interface du poste.
        // Commandes TCI standard « volume:dB; » (-60 à 0, en dB entiers : Thetis lit le nombre avec la langue de Windows) et
        // « volume; » pour le lire ; Mute TX = volume à -60 dB, comme avec l'interface du poste.
        public volatile int AfTciPort;
        volatile bool afReread;
        public void RereadAf() { afReread = true; }
        ExpertSdrTci afTci;
        int afTciPortUsed;
        DateTime afTciRetry = DateTime.MinValue;

        ExpertSdrTci AfTciLink()
        {
            int p = AfTciPort;
            if (afTci != null && (!afTci.Connected || afTciPortUsed != p)) { afTci.Dispose(); afTci = null; }
            if (afTci == null && p > 0 && DateTime.UtcNow >= afTciRetry)
            {
                afTciRetry = DateTime.UtcNow.AddSeconds(10);
                afTci = ExpertSdrTci.TryConnect("127.0.0.1", p);
                afTciPortUsed = p;
                Log.Write("Volume du poste par TCI : " + (afTci != null ? "liaison établie sur le port " + p : "pas de serveur TCI sur le port " + p));
                // Le serveur répond à « volume; » un peu après : on lui laisse 0,5 s.
                for (int i = 0; i < 10 && afTci != null && afTci.VolumePercent < 0; i++) Thread.Sleep(50);
            }
            return afTci;
        }

        int ReadAf(IRig rig)
        {
            if (AfTciPort <= 0) { if (afTci != null) { afTci.Dispose(); afTci = null; } return rig.ReadAfGain(); }
            ExpertSdrTci t = AfTciLink();
            return t == null ? -1 : t.VolumePercent;
        }

        void WriteAf(IRig rig, int pct)
        {
            if (AfTciPort <= 0) { rig.SetAfGain(pct); return; }
            ExpertSdrTci t = AfTciLink();
            if (t != null) t.SetVolumePercent(pct);
        }
        public void SetPreamp(int code) { lock (pendingLock) pendingPreamp = code; }
        // Puissance en watts (1.42), déjà ramenée sous le plafond général ; envoyée seulement en réception.
        public void SetPower(int watts) { lock (pendingLock) pendingPower = watts; }

        // Puissance réglable du poste connecté (null : non pilotable), annoncée après la connexion et à chaque changement
        // (TCI ne la connaît qu'après l'annonce du drive par le serveur).
        public event Action<PowerRange> PowerKnown;

        // Réglages de préampli du poste connecté (null : non pilotable), annoncés après chaque connexion.
        public event Action<PreampSet> PreampsKnown;

        // Partage sur un port COM virtuel (1.49) : port tenu par SDRLink (vide = aucun) ; seulement en CAT direct.
        public string ShareCom = "";
        public int ShareBaud = 9600;
        public event Action<string> ShareStatus;   // message pour la barre d'état (port ouvert, impossible…)
        CatShare share;
        // Port COM virtuel ouvert (bouton vert) ; ShareChanged annonce chaque ouverture / fermeture.
        public bool ShareOpen { get { return share != null; } }
        public event Action<bool> ShareChanged;
        void RaiseShare(bool on) { Action<bool> h = ShareChanged; if (h != null) h(on); }

        int pendingAf = -1, pendingPreamp = -1, pendingPower = -1;

        void OpenShare(IRig rig)
        {
            CloseShare();
            if (string.IsNullOrEmpty(ShareCom)) return;
            SerialRig sr = rig as SerialRig;
            string msg;
            if (sr == null) msg = Lang.T("Port COM virtuel : seulement avec l'interface « Port COM (CAT direct) »", "Virtual COM port: only with the \"COM port (direct CAT)\" interface");
            else
            {
                CatShare s = new CatShare(ShareCom, ShareBaud, sr.Framing);
                try
                {
                    s.Open();
                    share = s;
                    RaiseShare(true);
                    string other = Com0Com.OtherEnd(ShareCom);
                    msg = other != null
                        ? Lang.F("Poste partagé : votre autre logiciel peut ouvrir {0}", "Rig shared: your other program can open {0}", other)
                        : Lang.F("Poste partagé sur {0}", "Rig shared on {0}", ShareCom);
                }
                catch (Exception ex)
                {
                    msg = Lang.F("Port COM virtuel {0} impossible à ouvrir : {1}", "Virtual COM port {0} cannot be opened: {1}", ShareCom, ex.Message);
                    Log.Write("Partage : " + msg);
                }
            }
            Action<string> h = ShareStatus;
            if (h != null) h(msg);
        }

        void CloseShare()
        {
            if (share != null) { share.Close(); share = null; RaiseShare(false); }
        }

        // Attente entre deux interrogations, en servant tout de suite les commandes de l'autre logiciel (port COM virtuel).
        void WaitServing(IRig rig, int ms)
        {
            int end = Environment.TickCount + ms;
            while (running)
            {
                CatShare s = share;
                SerialRig sr = rig as SerialRig;
                if (s != null && sr != null)
                {
                    byte[] c;
                    while ((c = s.Next()) != null && running)
                    {
                        s.Reply(sr.Passthrough(c));
                        s.Commands++;
                    }
                }
                int left = end - Environment.TickCount;
                if (left <= 0) return;
                if (s != null) s.Signal.WaitOne(left); else Thread.Sleep(left);
            }
        }
        const int AfReadEvery = 10;     // relecture du volume du poste tous les 10 tours (bouton tourné sur le poste)

        void Run()
        {
            IRig rig = null;
            int misses = 0;
            int lastTx = -1;
            long lastF = -1;
            string lastM = null;
            int lastAf = -2, afCountdown = 0;
            DateTime afQuietUntil = DateTime.MinValue;
            bool txUnsupportedLogged = false;
            bool powerAnnounced = false, powerWaitLogged = false;
            PowerRange lastPower = null;
            while (running)
            {
                try
                {
                    if (rig == null)
                    {
                        Status(Lang.T("Connexion...", "Connecting..."), false);
                        rig = factory();
                        rig.Open();
                        misses = 0;
                        lastF = -1;
                        lastAf = -2;
                        afCountdown = 0;
                        Status(Lang.T("Connecté, en attente du poste...", "Connected, waiting for the rig..."), false);
                        if (rig.Vfo == 1) Log.Write(rig.CanVfoB ? "VFO B commandé" : "VFO B non pilotable avec ce poste / cette interface : VFO A commandé");
                        OpenShare(rig);
                        PreampSet ps = null;
                        try { ps = rig.Preamps; } catch { }
                        Log.Write("Préampli : " + (ps == null ? "non pilotable sur ce poste / cette interface" : ps.Describe()));
                        Action<PreampSet> pk = PreampsKnown;
                        if (pk != null) pk(ps);
                        powerAnnounced = false;
                    }

                    long f; string m; int ptt, af, pre, pwr;
                    lock (pendingLock)
                    {
                        f = pendingFreq; m = pendingMode; ptt = pendingPtt; af = pendingAf; pre = pendingPreamp;
                        pwr = pendingPower;
                        pendingFreq = 0; pendingMode = null; pendingPtt = -1; pendingAf = -1; pendingPreamp = -1; pendingPower = -1;
                    }
                    if (afReread) { afReread = false; afCountdown = 0; afQuietUntil = DateTime.MinValue; lastAf = -2; }
                    if (af >= 0)
                    {
                        WriteAf(rig, af);
                        lastAf = af;
                        // Pas de relecture juste après un réglage : le poste n'a peut-être pas encore pris la valeur.
                        afQuietUntil = DateTime.UtcNow.AddMilliseconds(1500);
                    }
                    if (ptt >= 0)
                    {
                        rig.SetPtt(ptt == 1);
                        pttCommandedOn = ptt == 1;
                        Log.Write("Bouton PTT : " + (ptt == 1 ? "passage en émission" : "retour en réception"));
                    }
                    // Mode avant fréquence : certains postes décalent la fréquence au changement de mode.
                    if (m != null) { rig.SetMode(m); Thread.Sleep(40); }
                    if (f > 0) { rig.SetFreq(f); Thread.Sleep(40); }
                    // Préampli après la fréquence : la plupart des postes le retiennent par bande (changement de bande = autre réglage).
                    if (pre >= 0)
                    {
                        PreampSet ps = rig.Preamps;
                        if (ps != null)
                        {
                            Log.Write("Préampli : " + ps.LabelOf(ps.Map(pre)));
                            rig.SetPreamp(pre);
                            Thread.Sleep(40);
                        }
                    }

                    // Puissance (1.42) : après la fréquence (le poste la retient souvent par bande / mode), et JAMAIS pendant l'émission :
                    // elle attend le retour en réception (sauf si un ordre plus récent l'a remplacée).
                    PowerRange pr = null;
                    try { pr = rig.Power; } catch { }
                    if (!powerAnnounced || !SamePower(pr, lastPower))
                    {
                        powerAnnounced = true;
                        lastPower = pr;
                        Log.Write("Puissance : " + (pr == null ? "non pilotable sur ce poste / cette interface" : pr.Describe()));
                        Action<PowerRange> wk = PowerKnown;
                        if (wk != null) wk(pr);
                    }
                    if (pwr >= 0 && pr != null)
                    {
                        if (rig.ReadTx() == 1)
                        {
                            if (!powerWaitLogged) Log.Write("Puissance : poste en émission, réglage remis au retour en réception");
                            powerWaitLogged = true;
                            lock (pendingLock) if (pendingPower < 0) pendingPower = pwr;
                        }
                        else
                        {
                            int w = Powers.Clamp(pwr, pr);
                            Log.Write("Puissance : " + w + " W" + (w != pwr ? " (demandé " + pwr + " W, ramené au plafond / aux limites du poste)" : ""));
                            rig.SetPower(w);
                            powerWaitLogged = false;
                            Thread.Sleep(40);
                        }
                    }

                    // PTT lu en premier : c'est ce qui doit réagir le plus vite (coupure du son).
                    if (PollTx)
                    {
                        int tx = rig.ReadTx();
                        if (tx < 0 && !txUnsupportedLogged)
                        {
                            txUnsupportedLogged = true;
                            Log.Write("État émission/réception non lisible sur ce poste/interface");
                        }
                        if (tx != lastTx) RaiseTx(lastTx = tx);
                    }
                    else if (lastTx != -1) RaiseTx(lastTx = -1);

                    if (rig.Read(out f, out m))
                    {
                        misses = 0;
                        LastOkTick = Environment.TickCount;
                        Status(Lang.T("Poste en ligne", "Rig online"), true);
                        if (f != lastF || m != lastM)
                        {
                            lastF = f;
                            lastM = m;
                            Action<long, string> h = StateReceived;
                            if (h != null) h(f, m);
                        }

                        // Volume du poste : lu tout de suite à la connexion, puis de temps en temps.
                        // Non pris en charge : on réessaie beaucoup plus rarement (TCI annonce parfois son volume un peu après).
                        if (--afCountdown <= 0 && DateTime.UtcNow >= afQuietUntil)
                        {
                            int a = ReadAf(rig);
                            afCountdown = a < 0 ? AfReadEvery * 5 : AfReadEvery;
                            if (a != lastAf)
                            {
                                Log.Write(a < 0 ? "Volume du poste non disponible (poste ou interface)" : "Volume du poste : " + a + " %");
                                lastAf = a;
                                Action<int> g = AfGainChanged;
                                if (g != null) g(a);
                            }
                        }
                    }
                    else if (++misses == 5)
                    {
                        Status(Lang.T("Le poste ne répond pas (vérifiez port, vitesse, adresse CI-V, menu CAT)", "The rig does not answer (check port, baud rate, CI-V address, CAT menu)"), false);
                    }
                }
                catch (Exception ex)
                {
                    if (lastTx != -1) RaiseTx(lastTx = -1);
                    // Les appels asynchrones (TCI) enveloppent la vraie cause dans une AggregateException.
                    Status(Lang.T("Erreur : ", "Error: ") + ex.GetBaseException().Message, false);
                    CloseShare();
                    if (rig != null) { try { rig.Close(); } catch { } rig = null; }
                    for (int i = 0; i < 30 && running; i++) Thread.Sleep(100);
                    continue;
                }
                try { WaitServing(rig, pollMs); }
                catch (Exception ex)
                {
                    // Port du poste perdu pendant une commande de l'autre logiciel : même reprise qu'une erreur d'interrogation.
                    Status(Lang.T("Erreur : ", "Error: ") + ex.GetBaseException().Message, false);
                    CloseShare();
                    if (rig != null) { try { rig.Close(); } catch { } rig = null; }
                    for (int i = 0; i < 30 && running; i++) Thread.Sleep(100);
                }
            }
            CloseShare();
            if (rig != null)
            {
                // Dernier réglage de volume demandé juste avant l'arrêt (rétablissement après "Mute TX").
                int lastAfRequest;
                lock (pendingLock) { lastAfRequest = pendingAf; pendingAf = -1; }
                if (lastAfRequest >= 0) { try { WriteAf(rig, lastAfRequest); Thread.Sleep(100); } catch { } }
                if (pttCommandedOn)
                {
                    try { rig.SetPtt(false); Log.Write("Bouton PTT : retour en réception avant déconnexion"); Thread.Sleep(100); } catch { }
                }
                try { rig.Close(); } catch { }
            }
            if (afTci != null) { afTci.Dispose(); afTci = null; }
            Status(Lang.T("Déconnecté", "Disconnected"), false);
        }

        static bool SamePower(PowerRange a, PowerRange b)
        {
            if (a == null || b == null) return a == b;
            return a.Kind == b.Kind && a.MinW == b.MinW && a.MaxW == b.MaxW && a.Rig == b.Rig;
        }

        void RaiseTx(int tx)
        {
            Log.Write("Poste : " + (tx == 1 ? "ÉMISSION" : tx == 0 ? "réception" : "état TX inconnu"));
            Action<int> t = TxChanged;
            if (t != null) t(tx);
        }

        void Status(string msg, bool online)
        {
            if (msg == lastStatus) return;
            lastStatus = msg;
            Log.Write("Poste : " + msg);
            Action<string, bool> h = StatusChanged;
            if (h != null) h(msg, online);
        }
    }
}
