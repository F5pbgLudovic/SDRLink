using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace SDRLink
{
    // Décodage FT8 (tranches de 15 s), FT4 (7,5 s) ou FT2 (3,75 s ; FT4 à symboles deux fois plus courts) en continu : le son (12 kHz)
    // arrive par morceaux ; à la fin de chaque tranche (alignée sur l'heure UTC) la tranche écoulée est décodée par ft8_lib (DigiNative)
    // dans le fil du décodeur. Le retard du son (réseau, WebSDR) est appris à partir du DT des messages décodés : la fenêtre d'analyse
    // est décalée d'autant. La parité de la tranche (paire / impaire) permet d'afficher les deux périodes en deux couleurs.
    // (Portage de Ft8Engine.java de PhoneSDRLink.)
    public class Ft8Engine : DigiEngine
    {
        public const int RATE = 12000;
        public override int Rate { get { return RATE; } }

        public readonly bool Ft4;      // vrai pour FT4 et FT2 : l'heure est affichée avec le dixième de seconde
        public readonly int Mode;      // 0 FT8, 1 FT4, 2 FT2 (même numérotation que le décodeur C)
        public readonly int SlotMs, SlotN;
        public readonly string Name;

        /** Horloge (ms UTC depuis 1970) ; remplaçable pour les essais. */
        public Func<long> NowMs = UtcNowMs;
        /** Faux pour les essais : pas de minuterie, Check() est appelé à la main. */
        public bool UseTimer = true;

        readonly short[] ring = new short[RATE * 45];
        readonly object sync = new object();
        long total, anchorMs, lastSlot = long.MinValue;
        bool anchored;
        volatile bool running = true;
        double shiftSec;
        int slotsDone, totalMsgs;   // fil du décodeur
        Timer timer;

        public static long UtcNowMs()
        {
            return (DateTime.UtcNow.Ticks - 621355968000000000L) / 10000;
        }

        public Ft8Engine(int mode)
        {
            Mode = mode;
            Ft4 = mode != 0;
            SlotMs = mode == 2 ? 3750 : mode == 1 ? 7500 : 15000;
            SlotN = RATE * SlotMs / 1000;
            Name = mode == 2 ? "FT2" : mode == 1 ? "FT4" : "FT8";
            Post(delegate
            {
                string err = DigiNative.Load();
                if (err != null) RaiseStatus(Name + " : " + err);
                else DigiNative.Ft8Reset();
            });
        }

        /** Retard appris (s) entre l'heure UTC et le son reçu. */
        public double ShiftSec { get { lock (sync) return shiftSec; } }

        // Morceau de son (fil du son) : noté avec son heure d'arrivée.
        public override void Feed(float[] x, int n)
        {
            long now = NowMs();
            bool startTimer = false;
            lock (sync)
            {
                if (!running) return;
                if (!anchored) { anchorMs = now - n * 1000L / RATE; anchored = true; }
                else
                {
                    long expected = anchorMs + total * 1000L / RATE + n * 1000L / RATE;
                    if (now - expected > 2500) anchorMs += now - expected;   // le son s'est arrêté un moment (changement de page…) : on recale
                }
                for (int i = 0; i < n; i++)
                {
                    double v = x[i] * 32767.0;
                    ring[(int)(total % ring.Length)] = (short)(v > 32767 ? 32767 : v < -32768 ? -32768 : v);
                    total++;
                }
                if (timer == null && UseTimer) startTimer = true;
            }
            if (startTimer)
            {
                lock (sync)
                {
                    if (timer == null && running) timer = new Timer(Tick, null, 500, 500);
                }
            }
        }

        // Pour les essais : morceau déjà en 16 bits.
        public void FeedShorts(short[] s, int n)
        {
            float[] f = new float[n];
            for (int i = 0; i < n; i++) f[i] = s[i] / 32767f;
            Feed(f, n);
        }

        void Tick(object o)
        {
            if (!running) return;
            try { Check(); } catch { }
        }

        static long FloorDiv(long a, long b) { long q = a / b; if ((a % b != 0) && ((a < 0) != (b < 0))) q--; return q; }
        static long FloorMod(long a, long b) { return a - FloorDiv(a, b) * b; }

        // Toutes les 0,5 s : si une tranche est terminée (plus 0,3 s), elle est copiée et décodée dans le fil du décodeur.
        public void Check()
        {
            long now = NowMs();
            short[] slot0;   // le tampon n'est alloué que lorsqu'une tranche est à décoder (pas toutes les 0,5 s)
            long s;
            lock (sync)
            {
                if (!anchored || !running) return;
                long shiftMs = (long)(shiftSec * 1000);
                s = FloorDiv(now - shiftMs - SlotMs - 300, (long)SlotMs);
                if (s <= lastSlot) return;
                long startIdx = (s * SlotMs + shiftMs - anchorMs) * RATE / 1000;
                if (startIdx < 0 || startIdx + SlotN > total || total - startIdx > ring.Length) { lastSlot = s; return; }   // pas assez de son pour cette tranche
                slot0 = new short[SlotN];
                for (int i = 0; i < SlotN; i++) slot0[i] = ring[(int)((startIdx + i) % ring.Length)];
                lastSlot = s;
            }
            short[] slot = slot0;
            long slotStart = s * SlotMs;
            int parity = (int)FloorMod(s, 2L);
            Post(delegate { DecodeSlot(slot, slotStart, parity); });
        }

        // Fil du décodeur.
        void DecodeSlot(short[] slot, long slotStart, int parity)
        {
            string res = DigiNative.Ft8Decode(slot, SlotN, RATE, Mode);
            List<string> lines = new List<string>();
            List<double> dts = new List<double>();
            DateTime c = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(slotStart * 10000);
            CultureInfo inv = CultureInfo.InvariantCulture;
            string t = Ft4
                ? string.Format(inv, "{0:00}{1:00}{2:00}.{3}", c.Hour, c.Minute, c.Second, c.Millisecond / 100)
                : string.Format(inv, "{0:00}{1:00}{2:00}", c.Hour, c.Minute, c.Second);
            foreach (string ln in res.Split('\n'))
            {
                string[] p = ln.Split(new char[] { '|' }, 4);
                if (p.Length < 4) continue;
                double dt = 0;
                double v;
                if (double.TryParse(p[1], NumberStyles.Float, inv, out v)) dt = v - 0.5;
                dts.Add(dt);
                lines.Add(t + " " + dt.ToString("+0.0;-0.0", inv).PadLeft(5) + " " + p[2].PadLeft(4) + "  " + p[3]);
            }
            if (!running) return;
            slotsDone++;
            totalMsgs += lines.Count;
            double sh;
            lock (sync)
            {
                if (dts.Count >= 2)   // on cale la fenêtre sur le retard médian observé
                {
                    double[] a = dts.ToArray();
                    Array.Sort(a);
                    double med = a[a.Length / 2];
                    shiftSec = Math.Max(-0.5, Math.Min(SlotMs / 1000.0 / 2, shiftSec + 0.7 * med));
                }
                sh = shiftSec;
            }
            RaiseLines(lines, parity);
            RaiseStatus(Lang.F("{3} : {0} tranches, {1} messages, retard {2} s", "{3}: {0} slots, {1} messages, delay {2} s",
                slotsDone, totalMsgs, sh.ToString("0.0", inv), Name));
        }

        public override void Stop()
        {
            Timer tm;
            lock (sync) { running = false; tm = timer; timer = null; }
            if (tm != null) tm.Dispose();
            base.Stop();
        }
    }
}
