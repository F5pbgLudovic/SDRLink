using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace SDRLink
{
    // Base des décodeurs à tranches longues (WSPR 120 s, JT9 60 s, JS8) (repris de PhoneSDRLink, SlotDecoder.java) : le son (12 kHz)
    // arrive par morceaux dans un tampon circulaire, avec l'heure d'arrivée ; quand la tranche UTC est assez avancée, une fenêtre
    // d'analyse (un peu avant le début de la tranche) est décodée dans le fil du décodeur. Le décalage médian des signaux décodés
    // recale la fenêtre.
    public abstract class SlotDecoder : DigiEngine
    {
        public const int RATE = 12000;
        public override int Rate { get { return RATE; } }

        public readonly int SlotMs, PreMs, ReadyMs, WinN;
        public readonly string Name;
        readonly short[] ring = new short[RATE * 150];
        long total, anchorMs, lastSlot = long.MinValue;
        bool anchored;
        volatile bool running = true;
        double shiftSec;
        int slotsDone, totalMsgs;
        Timer timer;
        readonly object sync = new object();

        /** Horloge (ms UTC depuis 1970) ; remplaçable pour les essais. */
        public Func<long> NowMs = UtcNowMs;
        /** Faux pour les essais : pas de minuterie, Check() est appelé à la main. */
        public bool UseTimer = true;

        public static long UtcNowMs()
        {
            return (DateTime.UtcNow.Ticks - 621355968000000000L) / 10000;
        }

        /** Résultat d'un décodage : lignes d'affichage et décalages (s) des signaux trouvés. */
        public sealed class Result
        {
            public readonly List<string> Lines = new List<string>();
            public readonly List<double> Dts = new List<double>();
        }

        protected SlotDecoder(string name, int slotMs, int preMs, int readyMs)
        {
            Name = name;
            SlotMs = slotMs;
            PreMs = preMs;
            ReadyMs = readyMs;
            WinN = (int)((long)RATE * (preMs + readyMs) / 1000);
        }

        /** Décode la fenêtre (début = début de tranche − PreMs). slotStartMs : début de tranche (ms UTC), t : texte de l'heure. */
        public abstract Result Decode(short[] win, int n, long slotStartMs, string t);

        protected abstract string TimeText(DateTime utc);

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
                    if (now - expected > 2500) anchorMs += now - expected;   // le son s'est arrêté un moment : on recale
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
            for (int i = 0; i < n; i++) f[i] = (s[i] + (s[i] > 0 ? 0.25f : s[i] < 0 ? -0.25f : 0f)) / 32767f;   // Feed retrouve exactement s[i]
            Feed(f, n);
        }

        void Tick(object o)
        {
            if (!running) return;
            try { Check(); } catch { }
        }

        /** Réglage de l'ancrage (essais) : instant (ms UTC) du premier échantillon reçu. */
        public void SetAnchor(long ms)
        {
            lock (sync) { anchorMs = ms; anchored = true; lastSlot = long.MinValue; shiftSec = 0; }
        }

        public void Check()
        {
            long now = NowMs();
            short[] w;
            long s;
            lock (sync)
            {
                if (!anchored || !running) return;
                long shiftMs = (long)(shiftSec * 1000);
                s = FloorDiv(now - shiftMs - ReadyMs, (long)SlotMs);
                if (s <= lastSlot) return;
                long startIdx = ((s * SlotMs + shiftMs - PreMs) - anchorMs) * RATE / 1000;
                lastSlot = s;
                if (startIdx < 0 || startIdx + WinN > total || total - startIdx > ring.Length) return;   // pas assez de son pour cette tranche
                w = new short[WinN];
                for (int i = 0; i < WinN; i++) w[i] = ring[(int)((startIdx + i) % ring.Length)];
            }
            short[] win = w;
            long slotStart = s * SlotMs;
            DateTime c = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(slotStart * 10000);
            string t = TimeText(c);
            int parity = (int)FloorMod(s, 2L);
            Post(delegate
            {
                Result r;
                try { r = Decode(win, win.Length, slotStart, t); } catch (Exception e) { Log.Write("Décodeur " + Name + " : " + e.Message); r = new Result(); }
                if (!running) return;
                slotsDone++;
                totalMsgs += r.Lines.Count;
                if (r.Dts.Count > 0)
                {
                    double[] a = r.Dts.ToArray();
                    Array.Sort(a);
                    double med = a[a.Length / 2];
                    lock (sync) { shiftSec = Math.Max(-1.5, Math.Min(1.5, shiftSec + 0.5 * med)); }
                }
                RaiseLines(r.Lines, parity);
                RaiseStatus(Lang.F("{3} : {0} tranches, {1} messages, retard {2} s", "{3}: {0} slots, {1} messages, delay {2} s",
                    slotsDone, totalMsgs, Fixed(shiftSec, 1), Name));
            });
        }

        public override void Stop()
        {
            running = false;
            lock (sync) { if (timer != null) { timer.Dispose(); timer = null; } }
            base.Stop();
        }

        // ---------------------------------------------------------------- équivalents des fonctions Java
        internal static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if (a % b != 0 && ((a < 0) != (b < 0))) q--;
            return q;
        }

        internal static long FloorMod(long a, long b) { return a - FloorDiv(a, b) * b; }

        /** Math.round de Java (demi vers +∞). */
        internal static long JRound(double x)
        {
            if (double.IsNaN(x)) return 0;
            double f = Math.Floor(x + 0.5);
            if (f >= 9.2233720368547758E18) return long.MaxValue;
            if (f <= -9.2233720368547758E18) return long.MinValue;
            return (long)f;
        }

        /** String.format(Locale.US, "%.Nf") (signe « - » gardé pour une valeur négative arrondie à zéro). */
        internal static string Fixed(double v, int dec)
        {
            bool neg = v < 0 || (v == 0 && 1 / v < 0);
            return (neg ? "-" : "") + Math.Abs(v).ToString("F" + dec, CultureInfo.InvariantCulture);
        }

        /** "%+Wd" */
        internal static string SignedInt(long v, int width) { return ((v >= 0 ? "+" : "") + v.ToString(CultureInfo.InvariantCulture)).PadLeft(width); }

        /** "%+W.Nf" */
        internal static string SignedFixed(double v, int width, int dec)
        {
            string s = Fixed(v, dec);
            if (s[0] != '-') s = "+" + s;
            return s.PadLeft(width);
        }

        /** Ligne d'affichage commune (WSPR, JT9, JS8) : "%s %+4d %+5.1f %4d  %s". */
        internal static string FormatLine(string t, int snr, double dt, double freq, string text)
        {
            return t + " " + SignedInt(snr, 4) + " " + SignedFixed(dt, 5, 1) + " " + JRound(freq).ToString(CultureInfo.InvariantCulture).PadLeft(4) + "  " + text;
        }

        internal static string Two(int v) { return v.ToString("00", CultureInfo.InvariantCulture); }

        internal static int BitCount(int v)
        {
            uint u = (uint)v;
            u = u - ((u >> 1) & 0x55555555u);
            u = (u & 0x33333333u) + ((u >> 2) & 0x33333333u);
            return (int)((((u + (u >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24);
        }

        internal static int Reverse(int v)
        {
            uint u = (uint)v, r = 0;
            for (int i = 0; i < 32; i++) { r = (r << 1) | (u & 1); u >>= 1; }
            return (int)r;
        }

        internal static int TrailingZeros(int v)
        {
            if (v == 0) return 32;
            int n = 0;
            while ((v & 1) == 0) { v >>= 1; n++; }
            return n;
        }

        /** String.trim de Java : enlève les caractères <= ' ' aux deux bouts. */
        internal static string JTrim(string s)
        {
            int a = 0, b = s.Length;
            while (a < b && s[a] <= ' ') a++;
            while (b > a && s[b - 1] <= ' ') b--;
            return s.Substring(a, b - a);
        }
    }
}
