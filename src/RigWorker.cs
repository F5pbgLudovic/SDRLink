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

        readonly Func<IRig> factory;
        readonly int pollMs;
        Thread thread;
        volatile bool running;
        readonly object pendingLock = new object();
        long pendingFreq;
        string pendingMode;
        int pendingPtt = -1;       // -1 : rien à envoyer
        bool pttCommandedOn;       // émission lancée par SDRLink, à couper avant de fermer la liaison
        string lastStatus;

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

        int pendingAf = -1;
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
                    }

                    long f; string m; int ptt, af;
                    lock (pendingLock)
                    {
                        f = pendingFreq; m = pendingMode; ptt = pendingPtt; af = pendingAf;
                        pendingFreq = 0; pendingMode = null; pendingPtt = -1; pendingAf = -1;
                    }
                    if (af >= 0)
                    {
                        rig.SetAfGain(af);
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
                            int a = rig.ReadAfGain();
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
                    if (rig != null) { try { rig.Close(); } catch { } rig = null; }
                    for (int i = 0; i < 30 && running; i++) Thread.Sleep(100);
                    continue;
                }
                Thread.Sleep(pollMs);
            }
            if (rig != null)
            {
                // Dernier réglage de volume demandé juste avant l'arrêt (rétablissement après "Mute TX").
                int lastAfRequest;
                lock (pendingLock) { lastAfRequest = pendingAf; pendingAf = -1; }
                if (lastAfRequest >= 0) { try { rig.SetAfGain(lastAfRequest); Thread.Sleep(100); } catch { } }
                if (pttCommandedOn)
                {
                    try { rig.SetPtt(false); Log.Write("Bouton PTT : retour en réception avant déconnexion"); Thread.Sleep(100); } catch { }
                }
                try { rig.Close(); } catch { }
            }
            Status(Lang.T("Déconnecté", "Disconnected"), false);
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
