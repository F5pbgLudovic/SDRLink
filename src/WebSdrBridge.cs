namespace SDRLink
{
    // Script injecté dans chaque page : détecte le type de WebSDR, signale ses changements
    // (fréquence/mode) à l'application et expose window.__sdrlink.set(hz, mode).
    public static class WebSdrBridge
    {
        public const string Script = @"
(function () {
  if (window.top !== window || window.__sdrlink) return;
  var L = window.__sdrlink = { last: '', volume: 1, gains: [], ctxs: [], tapOn: false };

  // Son du WebSDR : tout ce que la page envoie vers la sortie audio passe par une entrée commune, puis
  //   - vers le haut-parleur, à travers le gain réglable (volume, Mute WebSDR) ;
  //   - en mode DIGI, vers une dérivation à plein niveau qui envoie les échantillons à SDRLink
  //     (recopiés vers le câble audio virtuel pour WSJT-X, même quand le haut-parleur est coupé).
  // Installé avant les scripts de la page, donc valable pour tous les types de WebSDR.
  if (window.AudioNode && window.AudioDestinationNode) {
    var origConnect = AudioNode.prototype.connect, origDisconnect = AudioNode.prototype.disconnect;
    var master = function (ctx) {
      if (!ctx.__sdrlinkIn) {
        var input = ctx.createGain(), g = ctx.createGain();
        g.gain.value = L.volume;
        origConnect.call(input, g);
        origConnect.call(g, ctx.destination);
        ctx.__sdrlinkLinks = [[input, g]];
        ctx.__sdrlinkIn = input;
        ctx.__sdrlinkOut = g;
        L.gains.push(g);
        L.ctxs.push(ctx);
        if (L.tapOn) tap(ctx);
        if (L.notchOn) { if (L.notchAlgo === 0) notch(ctx, true); nf(ctx, L.notchAlgo > 0); }
        if (L.dnrOn) dnr(ctx, true);
        watch(ctx);
      }
      return ctx.__sdrlinkIn;
    };

    // Notch automatique (comme le DNF des Yaesu) : un analyseur repère les raies stables (porteuses, sifflements)
    // et de 4 à 8 filtres coupe-bande étroits (selon le niveau) les suivent. Le chant et la parole bougent trop pour être retenus.
    // Seul le haut-parleur est filtré : le câble SDRLINK reçoit le son brut, pour les logiciels de décodage.
    // Niveaux 1 à 10 (repris de PhoneSDRLink) : nombre de filtres actifs, seuil au-dessus du bruit (dB), présence demandée
    // (analyses de 100 ms) et largeur de chaque coupure (Hz). Niveau 1 = le notch des versions d'avant.
    var NOTCHES = 8, RELEASE = 10;
    var NL_COUNT = [4, 4, 5, 5, 6, 6, 7, 7, 8, 8], NL_ABOVE = [18, 16, 14, 12, 10, 9, 8, 7, 6, 5], NL_HOLD = [6, 6, 5, 5, 4, 4, 3, 3, 3, 2], NL_WIDTH = [40, 48, 56, 64, 72, 80, 90, 100, 110, 120];
    var nAct = 4, HOLD = 6, ABOVE = 18, nWidth = 40;
    var notch = function (ctx, on) {
      var n = ctx.__sdrlinkNotch, input = ctx.__sdrlinkIn, out = ctx.__sdrlinkOut;
      if (!input || !ctx.createBiquadFilter || !ctx.createAnalyser) return;
      if (!n) {
        n = ctx.__sdrlinkNotch = { on: false, an: ctx.createAnalyser(), f: [], slots: [] };
        n.an.fftSize = 4096;
        n.an.smoothingTimeConstant = 0;
        origConnect.call(input, n.an);
        for (var i = 0; i < NOTCHES; i++) {
          var b = ctx.createBiquadFilter();
          b.type = 'notch';
          b.frequency.value = ctx.sampleRate * 0.49;   // au repos : tout en haut du spectre, hors de la bande audio utile
          b.Q.value = 30;
          if (i > 0) origConnect.call(n.f[i - 1], b);
          n.f.push(b);
          n.slots.push({ bin: -1, miss: 0 });
        }
      }
      if (on === n.on) return;
      n.on = on;
      if (!on) for (var j = 0; j < NOTCHES; j++) release(ctx, n, j);
      rewire(ctx);
    };
    // Chaîne du haut-parleur (1.46) : entrée → [notch d'origine ou notch adaptatif / spectral (1.50)] → [réducteur de bruit] →
    // gain de sortie ; liens refaits à chaque changement.
    var chainEnd = function (ctx) {
      var n = ctx.__sdrlinkNotch, q = ctx.__sdrlinkNf, d = ctx.__sdrlinkDnr, end = ctx.__sdrlinkIn;
      if (n && n.on) end = n.f[NOTCHES - 1];
      if (q && q.on && q.node) end = q.node;
      if (d && d.on && d.node) end = d.node;
      return end;
    };
    var rewire = function (ctx) {
      var input = ctx.__sdrlinkIn, out = ctx.__sdrlinkOut, n = ctx.__sdrlinkNotch, q = ctx.__sdrlinkNf, d = ctx.__sdrlinkDnr;
      if (!input) return;
      var links = ctx.__sdrlinkLinks || [];
      for (var i = 0; i < links.length; i++) { try { origDisconnect.call(links[i][0], links[i][1]); } catch (e) { } }
      links = ctx.__sdrlinkLinks = [];
      var prev = input;
      var link = function (a, b) { origConnect.call(a, b); links.push([a, b]); };
      if (n && n.on) { link(prev, n.f[0]); prev = n.f[NOTCHES - 1]; }
      if (q && q.on && q.node) { link(prev, q.node); prev = q.node; }
      if (d && d.on && d.node) { link(prev, d.node); prev = d.node; }
      link(prev, out);
      retap(ctx);
    };

    // Réducteur de bruit (DNR, 1.46) : filtre spectral de Wiener. Le son est découpé en tranches de 21 à 32 ms qui se recouvrent
    // de moitié ; dans chaque tranche, le plancher de bruit de chaque fréquence est le minimum de son niveau lissé sur 1,6 s
    // environ (il suit un bruit qui change, mais pas la parole, qui a toujours des creux), et chaque fréquence est atténuée selon
    // son rapport signal sur bruit (estimé « décision dirigée », ce qui évite les gazouillis du bruit musical). Niveau 1 à 10 :
    // plancher d'atténuation de -6 à -33 dB et sur-estimation du bruit de 1 à 3. Retard ajouté : une tranche (21 à 32 ms).
    // La même fonction sert dans un AudioWorklet (fil audio ; pages https seulement) ou sinon dans un ScriptProcessor (pages http,
    // la plupart des WebSDR : quelques dizaines de ms de retard en plus).
    function SdrlinkDnr(sr) {
      var N = sr <= 16000 ? 256 : sr <= 32000 ? 512 : 1024, H = N >> 1, K = H + 1, U = 8, BIAS = 2.0, i, b;
      var V = Math.max(4, Math.round(1.6 * sr / H / U));
      var win = new Float32Array(N), inb = new Float32Array(N), ola = new Float32Array(N), outq = new Float32Array(H);
      var re = new Float32Array(N), im = new Float32Array(N), cs = new Float32Array(H), sn = new Float32Array(H), rev = new Uint16Array(N);
      var psm = new Float32Array(K), cmin = new Float32Array(K), mins = new Float32Array(K * U), pmin = new Float32Array(K), spow = new Float32Array(K);
      var fill, opos, frames, sub, slot, floorG = 0.5, beta = 1;
      for (i = 0; i < N; i++) win[i] = Math.sqrt(0.5 - 0.5 * Math.cos(2 * Math.PI * i / N));
      for (i = 0; i < H; i++) { cs[i] = Math.cos(2 * Math.PI * i / N); sn[i] = -Math.sin(2 * Math.PI * i / N); }
      var bits = Math.round(Math.log(N) / Math.LN2);
      for (i = 0; i < N; i++) { var r = 0; for (b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b); rev[i] = r; }
      var fft = function (inv) {
        var j, k, t, len, half, step;
        for (j = 0; j < N; j++) { k = rev[j]; if (k > j) { t = re[j]; re[j] = re[k]; re[k] = t; t = im[j]; im[j] = im[k]; im[k] = t; } }
        for (len = 2; len <= N; len <<= 1) {
          half = len >> 1; step = N / len;
          for (j = 0; j < N; j += len) for (k = 0; k < half; k++) {
            var wr = cs[k * step], wi = inv ? -sn[k * step] : sn[k * step], p = j + k, q = p + half;
            var tr = re[q] * wr - im[q] * wi, ti = re[q] * wi + im[q] * wr;
            re[q] = re[p] - tr; im[q] = im[p] - ti; re[p] += tr; im[p] += ti;
          }
        }
      };
      var reset = this.reset = function () {
        inb.fill(0); ola.fill(0); outq.fill(0); spow.fill(0);
        fill = N - H; opos = 0; frames = 0; sub = 0; slot = 0;
      };
      this.setLevel = function (l) {
        l = Math.max(1, Math.min(10, l | 0 || 1)) - 1;
        floorG = Math.pow(10, -(6 + 3 * l) / 20);
        beta = 1 + 0.22 * l;
      };
      this.frameLen = N;
      var frame = function () {
        var j, k, m;
        for (j = 0; j < N; j++) { re[j] = inb[j] * win[j]; im[j] = 0; }
        fft(false);
        var turn = ++sub >= V;
        for (k = 0; k < K; k++) {
          var P = re[k] * re[k] + im[k] * im[k];
          if (frames === 0) {
            psm[k] = P; cmin[k] = P; pmin[k] = P;
            for (j = 0; j < U; j++) mins[k * U + j] = P;
          } else {
            psm[k] = 0.8 * psm[k] + 0.2 * P;
            if (psm[k] < cmin[k]) cmin[k] = psm[k];
            if (psm[k] < pmin[k]) pmin[k] = psm[k];
            if (turn) {
              mins[k * U + slot] = cmin[k];
              m = cmin[k];
              for (j = 0; j < U; j++) if (mins[k * U + j] < m) m = mins[k * U + j];
              pmin[k] = m;
              cmin[k] = psm[k];
            }
          }
          var nz = pmin[k] * BIAS * beta + 1e-20, post = P / nz;
          var xi = 0.98 * spow[k] / nz + 0.02 * (post > 1 ? post - 1 : 0);
          var g = xi / (1 + xi);
          if (!(g >= floorG)) g = floorG;
          spow[k] = g * g * P;
          re[k] *= g; im[k] *= g;
          if (k > 0 && k < H) { re[N - k] *= g; im[N - k] *= g; }
        }
        if (turn) { sub = 0; slot = (slot + 1) % U; }
        frames++;
        fft(true);
        for (j = 0; j < N; j++) ola[j] += re[j] * win[j] / N;
        for (j = 0; j < H; j++) outq[j] = ola[j];
        if (!(outq[0] === outq[0])) reset();   // jamais de NaN qui resterait pour toujours
        ola.copyWithin(0, H); ola.fill(0, N - H);
        inb.copyWithin(0, H);
      };
      this.process = function (x, y) {
        for (var s = 0; s < x.length; s++) {
          inb[fill++] = x[s];
          if (y) y[s] = outq[opos];
          opos++;
          if (fill === N) { frame(); fill = N - H; opos = 0; }
        }
      };
      reset();
    }
    // DNR amélioré (1.48) : même découpage que le DNR classique, mais estimation du bruit par probabilité de présence de la parole
    // (suit vite un bruit qui change, sans manger la voix), gain « log-spectral » (MMSE-LSA, Ephraim et Malah) qui déforme beaucoup
    // moins la voix que le filtre de Wiener, mélangé au plancher selon la probabilité que la fréquence contienne de la voix (OM-LSA),
    // puis relâchement doux du gain d'une tranche à l'autre. Niveau 1 à 10 : plancher de -8 à -30 dB, sans sur-soustraction.
    function SdrlinkLsa(sr) {
      var N = sr <= 16000 ? 256 : sr <= 32000 ? 512 : 1024, H = N >> 1, K = H + 1, i, b;
      var win = new Float32Array(N), inb = new Float32Array(N), ola = new Float32Array(N), outq = new Float32Array(H);
      var re = new Float32Array(N), im = new Float32Array(N), cs = new Float32Array(H), sn = new Float32Array(H), rev = new Uint16Array(N);
      var lam = new Float32Array(K), pbar = new Float32Array(K), gprev = new Float32Array(K), gam = new Float32Array(K), gs = new Float32Array(K);
      var hop = H / sr, aN = Math.exp(-hop / 0.072), rel = Math.pow(0.5, hop / 0.01), gmin = 0.3, fill, opos, frames;
      var XI1 = 31.62, G1 = XI1 / (1 + XI1), LX = Math.log(1 + XI1), XIMIN = 0.003;
      for (i = 0; i < N; i++) win[i] = Math.sqrt(0.5 - 0.5 * Math.cos(2 * Math.PI * i / N));
      for (i = 0; i < H; i++) { cs[i] = Math.cos(2 * Math.PI * i / N); sn[i] = -Math.sin(2 * Math.PI * i / N); }
      var bits = Math.round(Math.log(N) / Math.LN2);
      for (i = 0; i < N; i++) { var r = 0; for (b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b); rev[i] = r; }
      var fft = function (inv) {
        var j, k, t, len, half, step;
        for (j = 0; j < N; j++) { k = rev[j]; if (k > j) { t = re[j]; re[j] = re[k]; re[k] = t; t = im[j]; im[j] = im[k]; im[k] = t; } }
        for (len = 2; len <= N; len <<= 1) {
          half = len >> 1; step = N / len;
          for (j = 0; j < N; j += len) for (k = 0; k < half; k++) {
            var wr = cs[k * step], wi = inv ? -sn[k * step] : sn[k * step], p = j + k, q = p + half;
            var tr = re[q] * wr - im[q] * wi, ti = re[q] * wi + im[q] * wr;
            re[q] = re[p] - tr; im[q] = im[p] - ti; re[p] += tr; im[p] += ti;
          }
        }
      };
      // Intégrale exponentielle E1 (approximations d'Abramowitz et Stegun).
      var e1 = function (v) {
        if (v < 1e-8) v = 1e-8;
        if (v < 1) return -0.57721566 - Math.log(v) + v - v * v / 4 + v * v * v / 18 - v * v * v * v / 96;
        return Math.exp(-v) / v * (v * v + 2.334733 * v + 0.250621) / (v * v + 3.330657 * v + 1.681534);
      };
      var reset = this.reset = function () {
        inb.fill(0); ola.fill(0); outq.fill(0); lam.fill(0); pbar.fill(0); gprev.fill(1); gam.fill(1); gs.fill(0);
        fill = N - H; opos = 0; frames = 0;
      };
      this.setLevel = function (l) {
        l = Math.max(1, Math.min(10, l | 0 || 1)) - 1;
        gmin = Math.pow(10, -(8 + 2.45 * l) / 20);
      };
      this.frameLen = N;
      var frame = function () {
        var j, k;
        for (j = 0; j < N; j++) { re[j] = inb[j] * win[j]; im[j] = 0; }
        fft(false);
        var lg = Math.log(gmin);
        for (k = 0; k < K; k++) {
          var P = re[k] * re[k] + im[k] * im[k];
          if (frames < 8) lam[k] = (lam[k] * frames + P) / (frames + 1);   // départ : moyenne des premières tranches
          else {
            var ph = 1 / (1 + Math.exp(LX - P / (lam[k] + 1e-20) * G1));     // probabilité de parole (Gerkmann et Hendriks)
            pbar[k] = 0.9 * pbar[k] + 0.1 * ph;
            if (pbar[k] > 0.99 && ph > 0.99) ph = 0.99;                    // évite que l'estimation reste bloquée
            lam[k] = aN * lam[k] + (1 - aN) * ((1 - ph) * P + ph * lam[k]);
          }
          var n = lam[k] + 1e-20, g = P / n;
          if (g > 1e4) g = 1e4;
          var xi = 0.98 * gprev[k] * gprev[k] * gam[k] + 0.02 * (g > 1 ? g - 1 : 0);
          if (xi < XIMIN) xi = XIMIN;
          var v = xi * g / (1 + xi), G = xi / (1 + xi) * Math.exp(0.5 * e1(v));
          if (!(G <= 1)) G = 1;
          var p = 1 / (1 + (1 + xi) * Math.exp(-v));                        // présence de voix dans cette fréquence
          var Gf = Math.exp(p * Math.log(G + 1e-20) + (1 - p) * lg);
          if (Gf < gmin) Gf = gmin;
          gprev[k] = G; gam[k] = g;
          if (Gf < rel * gs[k]) Gf = rel * gs[k];                           // relâchement doux : pas de coupure sèche en fin de mot
          gs[k] = Gf;
          re[k] *= Gf; im[k] *= Gf;
          if (k > 0 && k < H) { re[N - k] *= Gf; im[N - k] *= Gf; }
        }
        frames++;
        fft(true);
        for (j = 0; j < N; j++) ola[j] += re[j] * win[j] / N;
        for (j = 0; j < H; j++) outq[j] = ola[j];
        if (!(outq[0] === outq[0])) reset();
        ola.copyWithin(0, H); ola.fill(0, N - H);
        inb.copyWithin(0, H);
      };
      this.process = function (x, y) {
        for (var s = 0; s < x.length; s++) {
          inb[fill++] = x[s];
          if (y) y[s] = outq[opos];
          opos++;
          if (fill === N) { frame(); fill = N - H; opos = 0; }
        }
      };
      reset();
    }

    // DNR IA (1.48) : RNNoise (Jean-Marc Valin, Xiph.Org / Mozilla, licence BSD), réseau de neurones entraîné à séparer la voix du
    // bruit, porté du C (version 0.1.1, poids d'origine) en JavaScript. Il travaille à 48 kHz par tranches de 10 ms : le son du WebSDR
    // est rééchantillonné à l'aller et au retour. Niveau 1 à 10 : plancher de -6 à -34 dB, 10 = sans plancher (RNNoise pur).
    // W = poids du réseau (Int8Array de 87503 octets). Retard ajouté : 20 à 25 ms environ.
    function SdrlinkRnn(sr, W) {
      var FS = 480, WS = 960, FQ = 481, NB = 22, PBS = 1728, CM = 8, i, j;
      var eb = [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 34, 40, 48, 60, 78, 100];
      // --- Poids : couches dans l'ordre du fichier rnn_data.c.
      // Chaque matrice est transposée au chargement (une ligne de poids contiguë par neurone : calcul plus rapide).
      var off = 0, take = function (M, Nn) {
        var a = new Float32Array(M * Nn);
        for (var c = 0; c < M; c++) for (var k = 0; k < Nn; k++) a[k * M + c] = W[off + c * Nn + k];
        off += M * Nn; return a;
      };
      var idW = take(42, 24), idB = take(1, 24), vgW = take(24, 72), vgR = take(24, 72), vgB = take(1, 72), ngW = take(90, 144), ngR = take(48, 144), ngB = take(1, 144);
      var dgW = take(114, 288), dgR = take(96, 288), dgB = take(1, 288), doW = take(96, 22), doB = take(1, 22), voW = take(24, 1), voB = take(1, 1);
      // --- Transformée de Fourier de 960 points (bases 4, 4, 4, 3, 5), tables de rotation précalculées par étage.
      var fac = [4, 4, 4, 3, 5], perm = new Int32Array(WS), stg = [];
      var fr = new Float64Array(WS), fi = new Float64Array(WS), tr = new Float64Array(WS), ti = new Float64Array(WS), ar = new Float64Array(5), ai = new Float64Array(5);
      var src = function (pos, n, s) { if (s === 0) return 0; var p = fac[s - 1], m = n / p; return Math.floor(pos / m) + p * src(pos % m, m, s - 1); };
      for (i = 0; i < WS; i++) perm[i] = src(i, WS, fac.length);
      (function () {
        var Lp = 1;
        for (var s = 0; s < fac.length; s++) {
          var P = fac[s], L = Lp * P, tc = new Float64Array(Lp * P), ts = new Float64Array(Lp * P), dc = new Float64Array(P * P), ds = new Float64Array(P * P), k, q;
          for (k = 0; k < Lp; k++) for (q = 0; q < P; q++) { tc[k * P + q] = Math.cos(2 * Math.PI * q * k / L); ts[k * P + q] = -Math.sin(2 * Math.PI * q * k / L); }
          for (k = 0; k < P; k++) for (q = 0; q < P; q++) { dc[k * P + q] = Math.cos(2 * Math.PI * k * q / P); ds[k * P + q] = -Math.sin(2 * Math.PI * k * q / P); }
          stg.push({ P: P, Lp: Lp, L: L, tc: tc, ts: ts, dc: dc, ds: ds });
          Lp = L;
        }
      })();
      var fft = function () {
        var p, k, b, s, q, C3 = 0.8660254037844386, c1 = 0.30901699437494745, c2 = -0.8090169943352474, s1 = 0.9510565162951535, s2 = 0.5877852522924731;
        for (p = 0; p < WS; p++) { tr[p] = fr[perm[p]]; ti[p] = fi[perm[p]]; }
        for (s = 0; s < stg.length; s++) {
          var S = stg[s], P = S.P, Lp = S.Lp, L = S.L, tc = S.tc, ts = S.ts;
          for (b = 0; b < WS; b += L) for (k = 0; k < Lp; k++) {
            var o = b + k, w = k * P;
            for (q = 0; q < P; q++) {
              var x = o + q * Lp, cq = tc[w + q], sq = ts[w + q];
              ar[q] = tr[x] * cq - ti[x] * sq; ai[q] = tr[x] * sq + ti[x] * cq;
            }
            if (P === 4) {
              var t0r = ar[0] + ar[2], t0i = ai[0] + ai[2], t1r = ar[0] - ar[2], t1i = ai[0] - ai[2], t2r = ar[1] + ar[3], t2i = ai[1] + ai[3], t3r = ar[1] - ar[3], t3i = ai[1] - ai[3];
              tr[o] = t0r + t2r; ti[o] = t0i + t2i;
              tr[o + Lp] = t1r + t3i; ti[o + Lp] = t1i - t3r;
              tr[o + 2 * Lp] = t0r - t2r; ti[o + 2 * Lp] = t0i - t2i;
              tr[o + 3 * Lp] = t1r - t3i; ti[o + 3 * Lp] = t1i + t3r;
            } else if (P === 3) {
              var sr3 = ar[1] + ar[2], si3 = ai[1] + ai[2], dr3 = ar[1] - ar[2], di3 = ai[1] - ai[2], mr = ar[0] - 0.5 * sr3, mi = ai[0] - 0.5 * si3;
              tr[o] = ar[0] + sr3; ti[o] = ai[0] + si3;
              tr[o + Lp] = mr + C3 * di3; ti[o + Lp] = mi - C3 * dr3;
              tr[o + 2 * Lp] = mr - C3 * di3; ti[o + 2 * Lp] = mi + C3 * dr3;
            } else {
              var b1r = ar[1] + ar[4], b1i = ai[1] + ai[4], b2r = ar[2] + ar[3], b2i = ai[2] + ai[3], d1r = ar[1] - ar[4], d1i = ai[1] - ai[4], d2r = ar[2] - ar[3], d2i = ai[2] - ai[3];
              var m1r = ar[0] + c1 * b1r + c2 * b2r, m1i = ai[0] + c1 * b1i + c2 * b2i, m2r = ar[0] + c2 * b1r + c1 * b2r, m2i = ai[0] + c2 * b1i + c1 * b2i;
              var n1r = s1 * d1r + s2 * d2r, n1i = s1 * d1i + s2 * d2i, n2r = s2 * d1r - s1 * d2r, n2i = s2 * d1i - s1 * d2i;
              tr[o] = ar[0] + b1r + b2r; ti[o] = ai[0] + b1i + b2i;
              tr[o + Lp] = m1r + n1i; ti[o + Lp] = m1i - n1r;
              tr[o + 4 * Lp] = m1r - n1i; ti[o + 4 * Lp] = m1i + n1r;
              tr[o + 2 * Lp] = m2r + n2i; ti[o + 2 * Lp] = m2i - n2r;
              tr[o + 3 * Lp] = m2r - n2i; ti[o + 3 * Lp] = m2i + n2r;
            }
          }
        }
        for (p = 0; p < WS; p++) { fr[p] = tr[p]; fi[p] = ti[p]; }
      };
      var hw = new Float64Array(FS), dctT = new Float64Array(NB * NB);
      for (i = 0; i < FS; i++) { var sv = Math.sin(0.5 * Math.PI * (i + 0.5) / FS); hw[i] = Math.sin(0.5 * Math.PI * sv * sv); }
      for (i = 0; i < NB; i++) for (j = 0; j < NB; j++) dctT[i * NB + j] = Math.cos((i + 0.5) * j * Math.PI / NB) * (j === 0 ? Math.sqrt(0.5) : 1);
      var dct = function (out, o, inp) { for (var a = 0; a < NB; a++) { var s = 0; for (var c = 0; c < NB; c++) s += inp[c] * dctT[c * NB + a]; out[o + a] = s * Math.sqrt(2 / 22); } };
      var win = function (x) { for (var a = 0; a < FS; a++) { x[a] *= hw[a]; x[WS - 1 - a] *= hw[a]; } };
      // Les deux transformées directes (signal et signal décalé d'une période) en une seule FFT complexe.
      var fwd2 = function (xa, xb) {
        var a, b2;
        for (a = 0; a < WS; a++) { fr[a] = xa[a]; fi[a] = xb[a]; }
        fft();
        for (a = 0; a < FQ; a++) {
          b2 = a === 0 ? 0 : WS - a;
          var zr = fr[a], zi = fi[a], wr = fr[b2], wi = fi[b2];
          Xr[a] = (zr + wr) / (2 * WS); Xi[a] = (zi - wi) / (2 * WS);
          Pr[a] = (zi + wi) / (2 * WS); Pi[a] = (wr - zr) / (2 * WS);
        }
      };
      var inv = function (out, Xr, Xi) {
        var a;
        for (a = 0; a < FQ; a++) { fr[a] = Xr[a]; fi[a] = -Xi[a]; }
        for (; a < WS; a++) { fr[a] = Xr[WS - a]; fi[a] = Xi[WS - a]; }
        fft();
        for (a = 0; a < WS; a++) out[a] = fr[a];
      };
      var bandE = function (out, Ar, Ai, Br, Bi) {
        var a, c; for (a = 0; a < NB; a++) out[a] = 0;
        for (a = 0; a < NB - 1; a++) {
          var bs = (eb[a + 1] - eb[a]) << 2, b0 = eb[a] << 2;
          for (c = 0; c < bs; c++) { var fr2 = c / bs, t = Ar[b0 + c] * Br[b0 + c] + Ai[b0 + c] * Bi[b0 + c]; out[a] += (1 - fr2) * t; out[a + 1] += fr2 * t; }
        }
        out[0] *= 2; out[NB - 1] *= 2;
      };
      var interp = function (g, bE) {
        var a, c; for (a = 0; a < FQ; a++) g[a] = 0;
        for (a = 0; a < NB - 1; a++) { var bs = (eb[a + 1] - eb[a]) << 2, b0 = eb[a] << 2; for (c = 0; c < bs; c++) { var fr2 = c / bs; g[b0 + c] = (1 - fr2) * bE[a] + fr2 * bE[a + 1]; } }
      };
      // --- Hauteur de la voix (pitch.c et celt_lpc.c, version flottante).
      var xlp = new Float64Array(PBS >> 1), ac = new Float64Array(5), lpc = new Float64Array(4), yyl = new Float64Array(385), xc4 = new Float64Array(147), xc = new Float64Array(294);
      var x4 = new Float64Array(240), y4 = new Float64Array(387), best = [0, 1];
      var down = function (x) {
        var h = PBS >> 1, a, k;
        for (a = 1; a < h; a++) xlp[a] = 0.5 * (0.5 * (x[2 * a - 1] + x[2 * a + 1]) + x[2 * a]);
        xlp[0] = 0.5 * (0.5 * x[1] + x[0]);
        for (k = 0; k <= 4; k++) { var s = 0; for (a = k; a < h; a++) s += xlp[a] * xlp[a - k]; ac[k] = s; }
        ac[0] *= 1.0001;
        for (a = 1; a <= 4; a++) ac[a] -= ac[a] * (0.008 * a) * (0.008 * a);
        var err = ac[0]; lpc.fill(0);
        if (ac[0] !== 0) for (a = 0; a < 4; a++) {
          var rr = 0; for (k = 0; k < a; k++) rr += lpc[k] * ac[a - k];
          rr += ac[a + 1];
          var r = -rr / err; lpc[a] = r;
          for (k = 0; k < (a + 1) >> 1; k++) { var t1 = lpc[k], t2 = lpc[a - 1 - k]; lpc[k] = t1 + r * t2; lpc[a - 1 - k] = t2 + r * t1; }
          err = err - r * r * err;
          if (err < 0.001 * ac[0]) break;
        }
        var tmp = 1; for (a = 0; a < 4; a++) { tmp *= 0.9; lpc[a] *= tmp; }
        var n0 = lpc[0] + 0.8, n1 = lpc[1] + 0.8 * lpc[0], n2 = lpc[2] + 0.8 * lpc[1], n3 = lpc[3] + 0.8 * lpc[2], n4 = 0.8 * lpc[3];
        var m0 = 0, m1 = 0, m2 = 0, m3 = 0, m4 = 0;
        for (a = 0; a < h; a++) {
          var xi0 = xlp[a], s2 = xi0 + n0 * m0 + n1 * m1 + n2 * m2 + n3 * m3 + n4 * m4;
          m4 = m3; m3 = m2; m2 = m1; m1 = m0; m0 = xi0; xlp[a] = s2;
        }
      };
      var findBest = function (xcr, y, yo, len, maxp) {
        var Syy = 1, bn0 = -1, bn1 = -1, bd0 = 0, bd1 = 0, a;
        best[0] = 0; best[1] = 1;
        for (a = 0; a < len; a++) Syy += y[yo + a] * y[yo + a];
        for (a = 0; a < maxp; a++) {
          if (xcr[a] > 0) {
            var x16 = xcr[a] * 1e-12, num = x16 * x16;
            if (num * bd1 > bn1 * Syy) {
              if (num * bd0 > bn0 * Syy) { bn1 = bn0; bd1 = bd0; best[1] = best[0]; bn0 = num; bd0 = Syy; best[0] = a; }
              else { bn1 = num; bd1 = Syy; best[1] = a; }
            }
          }
          Syy += y[yo + a + len] * y[yo + a + len] - y[yo + a] * y[yo + a];
          if (Syy < 1) Syy = 1;
        }
      };
      var search = function () {   // x = xlp + 384, y = xlp, len 960, max_pitch 588
        var a, k, X0 = 384;
        for (a = 0; a < 240; a++) x4[a] = xlp[X0 + 2 * a];
        for (a = 0; a < 387; a++) y4[a] = xlp[2 * a];
        for (k = 0; k < 147; k++) { var s = 0; for (a = 0; a < 240; a++) s += x4[a] * y4[k + a]; xc4[k] = s; }
        findBest(xc4, y4, 0, 240, 147);
        var b0 = best[0], b1 = best[1];
        for (k = 0; k < 294; k++) {
          xc[k] = 0;
          if (Math.abs(k - 2 * b0) > 2 && Math.abs(k - 2 * b1) > 2) continue;
          var s3 = 0; for (a = 0; a < 480; a++) s3 += xlp[X0 + a] * xlp[k + a];
          xc[k] = Math.max(-1, s3);
        }
        findBest(xc, xlp, 0, 480, 294);
        var o = 0;
        if (best[0] > 0 && best[0] < 293) {
          var pa = xc[best[0] - 1], pb = xc[best[0]], pc = xc[best[0] + 1];
          if (pc - pa > 0.7 * (pb - pa)) o = 1; else if (pa - pc > 0.7 * (pb - pc)) o = -1;
        }
        return 2 * best[0] - o;
      };
      var SC = [0, 0, 3, 2, 3, 2, 5, 2, 3, 2, 3, 2, 5, 2, 3, 2];
      var dot = function (o1, o2, n) { var s = 0; for (var a = 0; a < n; a++) s += xlp[o1 + a] * xlp[o2 + a]; return s; };
      var doubling = function (T0in, prevP, prevG) {   // x = xlp, maxperiod 768, minperiod 60, N 960
        var maxp = 384, minp = 30, Nn = 480, xo = 384, k, a;
        var T0 = Math.floor(T0in / 2), pp = Math.floor(prevP / 2);
        if (T0 >= maxp) T0 = maxp - 1;
        var T = T0, xx = dot(xo, xo, Nn), xy = dot(xo, xo - T0, Nn), yy = xx;
        yyl[0] = xx;
        for (a = 1; a <= maxp; a++) { yy = yy + xlp[xo - a] * xlp[xo - a] - xlp[xo + Nn - a] * xlp[xo + Nn - a]; yyl[a] = Math.max(0, yy); }
        yy = yyl[T0];
        var bxy = xy, byy = yy, g0 = xy / Math.sqrt(1 + xx * yy), g = g0;
        for (k = 2; k <= 15; k++) {
          var T1 = Math.floor((2 * T0 + k) / (2 * k)), T1b;
          if (T1 < minp) break;
          if (k === 2) T1b = T1 + T0 > maxp ? T0 : T0 + T1;
          else T1b = Math.floor((2 * SC[k] * T0 + k) / (2 * k));
          var xy1 = 0.5 * (dot(xo, xo - T1, Nn) + dot(xo, xo - T1b, Nn)), yy1 = 0.5 * (yyl[T1] + yyl[T1b]);
          var g1 = xy1 / Math.sqrt(1 + xx * yy1), cont = 0;
          if (Math.abs(T1 - pp) <= 1) cont = prevG;
          else if (Math.abs(T1 - pp) <= 2 && 5 * k * k < T0) cont = 0.5 * prevG;
          var th = Math.max(0.3, 0.7 * g0 - cont);
          if (T1 < 3 * minp) th = Math.max(0.4, 0.85 * g0 - cont);
          else if (T1 < 2 * minp) th = Math.max(0.5, 0.9 * g0 - cont);
          if (g1 > th) { bxy = xy1; byy = yy1; T = T1; g = g1; }
        }
        bxy = Math.max(0, bxy);
        var pg = byy <= bxy ? 1 : bxy / (byy + 1), c0 = dot(xo, xo - (T - 1), Nn), c1 = dot(xo, xo - T, Nn), c2 = dot(xo, xo - (T + 1), Nn), o = 0;
        if (c2 - c0 > 0.7 * (c1 - c0)) o = 1; else if (c0 - c2 > 0.7 * (c1 - c2)) o = -1;
        if (pg > g) pg = g;
        var T0o = 2 * T + o;
        if (T0o < 60) T0o = 60;
        return [T0o, pg];
      };
      // --- Réseau (rnn.c) : couches denses et GRU, poids entiers / 256.
      var sig = function (v) { return 1 / (1 + Math.exp(-v)); };
      var vdot = function (Wt, o, v, n) { var s = 0; for (var c = 0; c < n; c++) s += Wt[o + c] * v[c]; return s; };
      var dense = function (Wt, B, M, Nn, inp, out, act) {
        for (var a = 0; a < Nn; a++) { var s = (B[a] + vdot(Wt, a * M, inp, M)) / 256; out[a] = act === 0 ? Math.tanh(s) : sig(s); }
      };
      var gz = new Float64Array(96), gr = new Float64Array(96), gh = new Float64Array(96), gs = new Float64Array(96);
      var gru = function (Wi, Wr, B, M, Nn, st, inp) {
        var a, s;
        for (a = 0; a < Nn; a++) gz[a] = sig((B[a] + vdot(Wi, a * M, inp, M) + vdot(Wr, a * Nn, st, Nn)) / 256);
        for (a = 0; a < Nn; a++) { gr[a] = sig((B[Nn + a] + vdot(Wi, (Nn + a) * M, inp, M) + vdot(Wr, (Nn + a) * Nn, st, Nn)) / 256); gs[a] = st[a] * gr[a]; }
        for (a = 0; a < Nn; a++) {
          s = (B[2 * Nn + a] + vdot(Wi, (2 * Nn + a) * M, inp, M) + vdot(Wr, (2 * Nn + a) * Nn, gs, Nn)) / 256;
          if (s < 0) s = 0;
          gh[a] = gz[a] * st[a] + (1 - gz[a]) * s;
        }
        for (a = 0; a < Nn; a++) st[a] = gh[a];
      };
      // --- État.
      var amem, smem, pbuf, cmem, memid, lastG, lastP, hp0, hp1, lastg, vst, nst, dst;
      var Xr = new Float64Array(FQ), Xi = new Float64Array(FQ), Pr = new Float64Array(FQ), Pi = new Float64Array(FQ), xw = new Float64Array(WS), pw = new Float64Array(WS);
      var Ex = new Float64Array(NB), Ep = new Float64Array(NB), Exp = new Float64Array(NB), Ly = new Float64Array(NB), tmp = new Float64Array(NB), feat = new Float64Array(42);
      var g = new Float64Array(NB), gf = new Float64Array(FQ), rf = new Float64Array(FQ), dOut = new Float64Array(24), vad = new Float64Array(1);
      var nIn = new Float64Array(90), dIn = new Float64Array(114), rr2 = new Float64Array(NB), xin = new Float64Array(FS), floorG = 0;
      var features = function () {   // compute_frame_features ; renvoie true si silence
        var a, E = 0;
        for (a = 0; a < FS; a++) { xw[a] = amem[a]; xw[FS + a] = xin[a]; amem[a] = xin[a]; }
        win(xw);
        pbuf.copyWithin(0, FS); for (a = 0; a < FS; a++) pbuf[PBS - FS + a] = xin[a];
        down(pbuf);
        var pi = 768 - search(), rd = doubling(pi, lastP, lastG);
        pi = rd[0]; lastP = pi; lastG = rd[1];
        for (a = 0; a < WS; a++) pw[a] = pbuf[PBS - WS - pi + a];
        win(pw); fwd2(xw, pw);
        bandE(Ex, Xr, Xi, Xr, Xi); bandE(Ep, Pr, Pi, Pr, Pi); bandE(Exp, Xr, Xi, Pr, Pi);
        for (a = 0; a < NB; a++) Exp[a] = Exp[a] / Math.sqrt(0.001 + Ex[a] * Ep[a]);
        dct(tmp, 0, Exp);
        for (a = 0; a < 6; a++) feat[34 + a] = tmp[a];
        feat[34] -= 1.3; feat[35] -= 0.9; feat[40] = 0.01 * (pi - 300);
        var logMax = -2, follow = -2;
        for (a = 0; a < NB; a++) {
          Ly[a] = Math.log(1e-2 + Ex[a]) / Math.LN10;
          Ly[a] = Math.max(logMax - 7, Math.max(follow - 1.5, Ly[a]));
          logMax = Math.max(logMax, Ly[a]); follow = Math.max(follow - 1.5, Ly[a]); E += Ex[a];
        }
        if (E < 0.04) { feat.fill(0); return true; }
        dct(feat, 0, Ly);
        feat[0] -= 12; feat[1] -= 4;
        var c0 = memid * NB, c1 = ((memid + CM - 1) % CM) * NB, c2 = ((memid + CM - 2) % CM) * NB;
        for (a = 0; a < NB; a++) cmem[c0 + a] = feat[a];
        memid++;
        for (a = 0; a < 6; a++) {
          feat[a] = cmem[c0 + a] + cmem[c1 + a] + cmem[c2 + a];
          feat[NB + a] = cmem[c0 + a] - cmem[c2 + a];
          feat[NB + 6 + a] = cmem[c0 + a] - 2 * cmem[c1 + a] + cmem[c2 + a];
        }
        if (memid === CM) memid = 0;
        var sv2 = 0;
        for (a = 0; a < CM; a++) {
          var md = 1e15;
          for (var b2 = 0; b2 < CM; b2++) {
            if (b2 === a) continue;
            var d = 0; for (var k = 0; k < NB; k++) { var t = cmem[a * NB + k] - cmem[b2 * NB + k]; d += t * t; }
            if (d < md) md = d;
          }
          sv2 += md;
        }
        feat[41] = sv2 / CM - 2.1;
        return false;
      };
      var pfilter = function () {
        var a;
        for (a = 0; a < NB; a++) {
          var r;
          if (Exp[a] > g[a]) r = 1;
          else r = Exp[a] * Exp[a] * (1 - g[a] * g[a]) / (0.001 + g[a] * g[a] * (1 - Exp[a] * Exp[a]));
          r = Math.sqrt(Math.min(1, Math.max(0, r)));
          rr2[a] = r * Math.sqrt(Ex[a] / (1e-8 + Ep[a]));
        }
        interp(rf, rr2);
        for (a = 0; a < FQ; a++) { Xr[a] += rf[a] * Pr[a]; Xi[a] += rf[a] * Pi[a]; }
        bandE(tmp, Xr, Xi, Xr, Xi);
        for (a = 0; a < NB; a++) rr2[a] = Math.sqrt(Ex[a] / (1e-8 + tmp[a]));
        interp(rf, rr2);
        for (a = 0; a < FQ; a++) { Xr[a] *= rf[a]; Xi[a] *= rf[a]; }
      };
      var y480 = new Float64Array(FS);
      var frame = function () {   // rnnoise_process_frame : xin (échelle 16 bits) → y480
        var a;
        for (a = 0; a < FS; a++) { var xi0 = xin[a], yi = xi0 + hp0; hp0 = hp1 + (-2 * xi0 + 1.99599 * yi); hp1 = xi0 - 0.996 * yi; xin[a] = yi; }
        if (!features()) {
          dense(idW, idB, 42, 24, feat, dOut, 0);
          gru(vgW, vgR, vgB, 24, 24, vst, dOut);
          dense(voW, voB, 24, 1, vst, vad, 1);
          for (a = 0; a < 24; a++) { nIn[a] = dOut[a]; nIn[24 + a] = vst[a]; }
          for (a = 0; a < 42; a++) nIn[48 + a] = feat[a];
          gru(ngW, ngR, ngB, 90, 48, nst, nIn);
          for (a = 0; a < 24; a++) dIn[a] = vst[a];
          for (a = 0; a < 48; a++) dIn[24 + a] = nst[a];
          for (a = 0; a < 42; a++) dIn[72 + a] = feat[a];
          gru(dgW, dgR, dgB, 114, 96, dst, dIn);
          dense(doW, doB, 96, 22, dst, g, 1);
          pfilter();
          for (a = 0; a < NB; a++) { g[a] = Math.max(g[a], 0.6 * lastg[a]); lastg[a] = g[a]; if (g[a] < floorG) g[a] = floorG; }
          interp(gf, g);
          for (a = 0; a < FQ; a++) { Xr[a] *= gf[a]; Xi[a] *= gf[a]; }
        }
        inv(xw, Xr, Xi); win(xw);
        for (a = 0; a < FS; a++) { y480[a] = xw[a] + smem[a]; smem[a] = xw[FS + a]; }
      };
      this.vad = function () { return vad[0]; };
      // --- Rééchantillonneur polyphase (sinus cardinal fenêtré, coefficients précalculés pour chaque décalage).
      var Rs = function (fi0, fo) {
        if (fi0 === fo) return { lat: 0, push: function (x, n, out) { for (var a = 0; a < n; a++) out[a] = x[a]; return n; }, reset: function () { } };
        var gcd = function (u, v) { while (v) { var t = u % v; u = v; v = t; } return u; };
        var fc = 0.9 * Math.min(1, fo / fi0), half = Math.ceil(12 / fc), T = 2 * half, a, j;
        var gg = gcd(Math.round(fi0), Math.round(fo)), P = Math.round(fo) / gg, exact = P <= 1024 && gg > 0;
        if (!exact) P = 1024;
        var stepN = exact ? Math.round(fi0) / gg : 0, stepF = fi0 / fo, co = new Float32Array(P * T);
        for (var ph = 0; ph < P; ph++) {
          var sum = 0;
          for (j = 0; j < T; j++) {
            var u = Math.abs(ph / P + half - 1 - j), h = 0;
            if (u < half) h = fc * (u === 0 ? 1 : Math.sin(Math.PI * fc * u) / (Math.PI * fc * u)) * (0.42 + 0.5 * Math.cos(Math.PI * u / half) + 0.08 * Math.cos(2 * Math.PI * u / half));
            co[ph * T + j] = h; sum += h;
          }
          for (j = 0; j < T; j++) co[ph * T + j] /= sum;
        }
        var buf = new Float32Array(1 << 15), len, ip, fr0;   // position de la prochaine sortie : ip + fr0 / P (exact) ou ip + fr0 (sinon)
        var o = {
          lat: half,
          reset: function () { buf.fill(0); len = half; ip = half; fr0 = 0; },
          push: function (x, n, out) {
            var c = 0, a2;
            if (len + n > buf.length) { var nb = new Float32Array((len + n) * 2); nb.set(buf.subarray(0, len)); buf = nb; }
            for (a2 = 0; a2 < n; a2++) buf[len++] = x[a2];
            while (ip + half < len) {
              var phs = exact ? fr0 : Math.round(fr0 * P), i0 = ip - half + 1, s = 0, base;
              if (phs >= P) { phs -= P; i0++; }
              base = phs * T;
              for (a2 = 0; a2 < T; a2++) s += buf[i0 + a2] * co[base + a2];
              out[c++] = s;
              if (exact) { fr0 += stepN; while (fr0 >= P) { fr0 -= P; ip++; } }
              else { fr0 += stepF; var w = Math.floor(fr0); ip += w; fr0 -= w; }
            }
            var d = ip - half - 1;
            if (d > 0) { buf.copyWithin(0, d, len); len -= d; ip -= d; }
            return c;
          }
        };
        o.reset();
        return o;
      };
      var up = Rs(sr, 48000), dn = Rs(48000, sr), u48 = new Float32Array(1 << 15), dnb = new Float32Array(4096), f48 = new Float32Array(FS);
      var oq = new Float32Array(1 << 15), oqr, oqw, fpos, D = Math.ceil(FS * sr / 48000) + up.lat + Math.ceil(dn.lat * sr / 48000) + 4;
      this.delay = D;
      var reset = this.reset = function () {
        amem = new Float64Array(FS); smem = new Float64Array(FS); pbuf = new Float64Array(PBS); cmem = new Float64Array(CM * NB);
        memid = 0; lastG = 0; lastP = 0; hp0 = 0; hp1 = 0; lastg = new Float64Array(NB); vst = new Float64Array(24); nst = new Float64Array(48); dst = new Float64Array(96);
        up.reset(); dn.reset(); oq.fill(0); oqr = 0; oqw = D; fpos = 0;
      };
      this.setLevel = function (l) {
        l = Math.max(1, Math.min(10, l | 0 || 1));
        floorG = l >= 10 ? 0 : Math.pow(10, -(6 + 3.5 * (l - 1)) / 20);
      };
      this.frameLen = FS;
      this.frame480 = function (x, y) { for (var a = 0; a < FS; a++) xin[a] = x[a]; frame(); for (a = 0; a < FS; a++) y[a] = y480[a]; };   // pour les essais (48 kHz, échelle 16 bits)
      var M = oq.length - 1;
      this.process = function (x, y) {
        var n = x.length, p = 0;
        while (p < n) {
          var m = Math.min(n - p, 1024), c = up.push(x.subarray ? x.subarray(p, p + m) : x, m, u48), a;
          for (a = 0; a < c; a++) {
            f48[fpos++] = u48[a];
            if (fpos === FS) {
              for (var b2 = 0; b2 < FS; b2++) xin[b2] = f48[b2] * 32768;
              frame();
              for (b2 = 0; b2 < FS; b2++) f48[b2] = y480[b2] / 32768;
              var k = dn.push(f48, FS, dnb);
              for (b2 = 0; b2 < k; b2++) { oq[oqw & M] = dnb[b2]; oqw++; }
              fpos = 0;
            }
          }
          for (a = 0; a < m; a++) { if (y) y[p + a] = oqr < oqw ? oq[oqr & M] : 0; if (oqr < oqw) oqr++; }
          p += m;
        }
        if (!(oq[(oqw - 1) & M] === oq[(oqw - 1) & M])) reset();
      };
      this.setLevel(5);
      reset();
    }

    // Les trois DNR derrière un seul objet (1.48) : 0 = classique, 1 = amélioré, 2 = IA. Chaque type garde son niveau ; un type
    // n'est créé qu'à sa première utilisation (le réseau de RNNoise ne se charge donc que si on le choisit).
    function SdrlinkNr(sr, W) {
      var cores = [null, null, null], algo = 0, lv = [3, 5, 5];
      var get = function (a) {
        var c = cores[a];
        if (!c) { c = cores[a] = a === 2 ? new SdrlinkRnn(sr, W) : a === 1 ? new SdrlinkLsa(sr) : new SdrlinkDnr(sr); c.setLevel(lv[a]); }
        return c;
      };
      this.set = function (a, l) {
        a = a | 0;
        if (a < 0 || a > 2) a = 0;
        if (a === 2 && !(W && W.length === 87503)) a = 1;
        lv[a] = Math.max(1, Math.min(10, l | 0 || 1));
        var c = get(a);
        c.setLevel(lv[a]);
        if (a !== algo) { algo = a; c.reset(); }
      };
      this.algo = function () { return algo; };
      this.reset = function () { get(algo).reset(); };
      this.process = function (x, y) { get(algo).process(x, y); };
    }
    L.DnrCore = SdrlinkDnr;   // pour les essais
    L.NrCore = SdrlinkNr;
    // Poids de RNNoise (87503 octets, base64), décodés une seule fois, à la première mise en marche du DNR.
    var RNNB64 = '" + RnnoiseWeights.B64 + @"', rnnW = null;
    L.rnnW = function () {
      if (!rnnW) {
        try { var b = atob(RNNB64), u = new Int8Array(b.length); for (var i = 0; i < b.length; i++) u[i] = b.charCodeAt(i) << 24 >> 24; rnnW = u; }
        catch (e) { rnnW = new Int8Array(0); }
      }
      return rnnW;
    };
    var dnrUrl = null;
    var dnrModule = function () {
      if (!dnrUrl) {
        var src = SdrlinkDnr.toString() + '\n' + SdrlinkLsa.toString() + '\n' + SdrlinkRnn.toString() + '\n' + SdrlinkNr.toString() +
          '\nregisterProcessor(\'sdrlink-dnr\', class extends AudioWorkletProcessor {\n' +
          ' constructor(o) { super(); var p = o && o.processorOptions || {}, c = this.c = new SdrlinkNr(sampleRate, p.w); c.set(p.algo, p.lvl);\n' +
          '  this.port.onmessage = function (e) { if (e.data && e.data.lvl) c.set(e.data.algo, e.data.lvl); if (e.data && e.data.reset) c.reset(); }; }\n' +
          ' process(inp, out) { var x = inp[0] && inp[0][0], y = out[0] && out[0][0]; if (!x) { if (y) y.fill(0); return true; } this.c.process(x, y); return true; }\n});\n';
        dnrUrl = URL.createObjectURL(new Blob([src], { type: 'application/javascript' }));
      }
      return dnrUrl;
    };
    L.dnrLevel = 1;
    L.dnrAlgo = 0;
    var dnrScript = function (ctx, d) {
      // Tampon d'environ 20 à 45 ms (le ScriptProcessor ajoute environ deux tampons de retard).
      var bs = 256;
      while (bs * 2 <= ctx.sampleRate * 0.045 && bs < 4096) bs *= 2;
      var sp = ctx.createScriptProcessor(bs, 1, 1), core = new SdrlinkNr(ctx.sampleRate, L.rnnW());
      sp.channelCount = 1;
      sp.channelCountMode = 'explicit';
      core.set(L.dnrAlgo, L.dnrLevel);
      sp.onaudioprocess = function (e) { core.process(e.inputBuffer.getChannelData(0), e.outputBuffer.getChannelData(0)); };
      d.core = core; d.kind = 'script';
      return sp;
    };
    var dnr = function (ctx, on) {
      var d = ctx.__sdrlinkDnr;
      if (!ctx.__sdrlinkIn) return;
      if (!d) {
        if (!on) return;
        d = ctx.__sdrlinkDnr = { on: false, node: null, kind: '' };
        var ready = function (node) { d.node = node; if (d.on) rewire(ctx); };
        var fallback = function (why) { L.dnrWhy = String(why && why.message || why); try { if (ctx.createScriptProcessor) ready(dnrScript(ctx, d)); } catch (e) { } };
        try {
          if (!ctx.audioWorklet || !window.AudioWorkletNode) throw new Error('no AudioWorklet (page not secure)');
          ctx.audioWorklet.addModule(dnrModule()).then(function () {
            var node = new AudioWorkletNode(ctx, 'sdrlink-dnr', { numberOfInputs: 1, numberOfOutputs: 1, outputChannelCount: [1], channelCount: 1, channelCountMode: 'explicit',
              processorOptions: { w: L.rnnW(), algo: L.dnrAlgo, lvl: L.dnrLevel } });
            d.kind = 'worklet';
            node.port.postMessage({ algo: L.dnrAlgo, lvl: L.dnrLevel });
            ready(node);
          }).catch(fallback);
        } catch (e) { fallback(e); }
      }
      if (on === d.on) return;
      d.on = on;
      if (on && d.node) { if (d.kind === 'worklet') d.node.port.postMessage({ reset: 1 }); else if (d.core) d.core.reset(); }
      rewire(ctx);
    };
    L.dnrOn = false;
    // algo : 0 = classique, 1 = amélioré, 2 = IA (RNNoise) ; absent = classique (appel de la 1.46).
    L.setDnr = function (on, lvl, algo) {
      L.dnrLevel = Math.max(1, Math.min(10, lvl | 0 || 1));
      L.dnrAlgo = Math.max(0, Math.min(2, algo | 0));
      L.dnrOn = !!on;
      for (var i = 0; i < L.ctxs.length; i++) {
        var d = L.ctxs[i].__sdrlinkDnr;
        if (d && d.node) { if (d.kind === 'worklet') d.node.port.postMessage({ algo: L.dnrAlgo, lvl: L.dnrLevel }); else if (d.core) d.core.set(L.dnrAlgo, L.dnrLevel); }
        dnr(L.ctxs[i], L.dnrOn);
      }
    };
    L.dnrKind = function () {
      for (var i = 0; i < L.ctxs.length; i++) { var d = L.ctxs[i].__sdrlinkDnr; if (d && d.on && d.node) return d.kind; }
      return '';
    };
    // Notch automatique (DNF), deux types en plus du notch d'origine (1.50, repris de PhoneSDRLink 1.30).
    // Chaque fonction est autonome : elles sont recopiées telles quelles dans un AudioWorklet (toString), sans rien d'extérieur.
    //
    // SdrlinkAnf : annulateur de raies adaptatif (« line enhancer » NLMS). Un filtre prédit le son à partir du son d'il y a ~8 ms ;
    // ce qui est prévisible (sifflements, porteuses, battements stables) est retranché, ce qui ne l'est pas (la parole, le bruit)
    // passe. Il suit les raies qui dérivent et en traite autant qu'il y en a. Niveau 1 à 10 : vitesse d adaptation (0,0015 à 0,06).
    function SdrlinkAnf(sr) {
      var L = Math.max(32, Math.min(256, Math.round(sr * 0.008))), D = Math.max(16, Math.round(sr * 0.008));
      var M = 1; while (M < D + L + 2) M <<= 1;
      var mask = M - 1, buf = new Float32Array(M), w = new Float32Array(L), pos = 0, mu = 0.01;
      var reset = this.reset = function () { buf.fill(0); w.fill(0); pos = 0; };
      this.setLevel = function (l) { l = Math.max(1, Math.min(10, l | 0 || 1)); mu = 0.0015 * Math.pow(1.5, l - 1); };
      this.frameLen = 128;
      this.process = function (x, y) {
        var n = x.length, i, j, v, p, yy, en, e, g, s, bad = false;
        for (i = 0; i < n; i++) {
          s = x[i]; p = (pos - D) & mask; yy = 0; en = 1e-6;
          for (j = 0; j < L; j++) { v = buf[(p - j) & mask]; yy += w[j] * v; en += v * v; }
          e = s - yy;
          if (e !== e || e > 4 || e < -4) { bad = true; e = 0; }
          g = mu * e / en;
          for (j = 0; j < L; j++) w[j] = w[j] * 0.99995 + g * buf[(p - j) & mask];
          buf[pos] = s; pos = (pos + 1) & mask;
          if (y) y[i] = e;
        }
        if (bad) reset();
      };
      this.setLevel(5);
      reset();
    }

    // SdrlinkSpf : notch spectral. Tranches de 43 ms (fenêtre racine de Hann, recouvrement de moitié) ; pour chaque fréquence, le
    // niveau lissé est comparé au niveau médian des fréquences voisines. Une fréquence qui dépasse ses voisines d'un seuil pendant
    // plusieurs tranches de suite est une raie stable : elle est ramenée au niveau de ses voisines (jamais en dessous), avec ses
    // deux ou quatre voisines immédiates. La parole bouge trop pour être retenue. Niveau 1 à 10 : seuil de 15 à 7 dB, durée de
    // présence de 0,35 à 0,08 s, atténuation maximale de 20 à 40 dB, largeur traitée de 3 à 5 fréquences. Retard : une tranche.
    function SdrlinkSpf(sr) {
      var N = sr <= 12500 ? 512 : sr <= 24000 ? 1024 : 2048, H = N >> 1, K = H + 1, i, b;
      var win = new Float32Array(N), inb = new Float32Array(N), ola = new Float32Array(H), outq = new Float32Array(H);
      var re = new Float32Array(N), im = new Float32Array(N), cs = new Float32Array(H), sn = new Float32Array(H), rev = new Uint16Array(N);
      var sdb = new Float32Array(K + 16), cnt = new Uint8Array(K), ex = new Float32Array(K), gn = new Float32Array(K), tg = new Float32Array(K), tmp = new Float32Array(12);
      var fill = H, opos = 0, first = true, thr = 11, hold = 8, floorG = 0.03, wid = 1, frames = sr / H;
      for (i = 0; i < N; i++) win[i] = Math.sqrt(0.5 - 0.5 * Math.cos(2 * Math.PI * i / N));
      for (i = 0; i < H; i++) { cs[i] = Math.cos(2 * Math.PI * i / N); sn[i] = -Math.sin(2 * Math.PI * i / N); }
      var bits = Math.round(Math.log(N) / Math.LN2);
      for (i = 0; i < N; i++) { var r = 0; for (b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b); rev[i] = r; }
      var fft = function (inv) {
        var j, k, t, len, half, step;
        for (j = 0; j < N; j++) { k = rev[j]; if (k > j) { t = re[j]; re[j] = re[k]; re[k] = t; t = im[j]; im[j] = im[k]; im[k] = t; } }
        for (len = 2; len <= N; len <<= 1) {
          half = len >> 1; step = N / len;
          for (j = 0; j < N; j += len) for (k = 0; k < half; k++) {
            var wr = cs[k * step], wi = inv ? -sn[k * step] : sn[k * step], p = j + k, q = p + half;
            var tr = re[q] * wr - im[q] * wi, ti = re[q] * wi + im[q] * wr;
            re[q] = re[p] - tr; im[q] = im[p] - ti; re[p] += tr; im[p] += ti;
          }
        }
      };
      var reset = this.reset = function () {
        inb.fill(0); ola.fill(0); outq.fill(0); cnt.fill(0); ex.fill(0); gn.fill(1);
        fill = H; opos = 0; first = true;
      };
      this.setLevel = function (l) {
        l = Math.max(1, Math.min(10, l | 0 || 1)) - 1;
        thr = 15 - 0.9 * l;
        hold = Math.max(3, Math.round((0.35 - 0.03 * l) * frames));
        floorG = Math.pow(10, -(20 + 2.2 * l) / 20);
        wid = l < 4 ? 1 : 2;
      };
      this.frameLen = N;
      var frame = function () {
        var j, k, d, m, t, c, base, a, v;
        for (j = 0; j < N; j++) { re[j] = inb[j] * win[j]; im[j] = 0; }
        fft(false);
        for (k = 0; k < K; k++) {
          v = 10 * Math.log(re[k] * re[k] + im[k] * im[k] + 1e-10) / Math.LN10;
          sdb[k] = first ? v : sdb[k] + (v - sdb[k]) * 0.35;
        }
        first = false;
        for (k = 0; k < K; k++) tg[k] = 1;
        for (k = 2; k < K - 1; k++) {
          // niveau de fond : médiane des voisines à 3 à 8 fréquences de distance de chaque côté
          m = 0;
          for (d = 3; d <= 8; d++) {
            if (k - d >= 1) { v = sdb[k - d]; for (t = m; t > 0 && tmp[t - 1] > v; t--) tmp[t] = tmp[t - 1]; tmp[t] = v; m++; }
            if (k + d < K) { v = sdb[k + d]; for (t = m; t > 0 && tmp[t - 1] > v; t--) tmp[t] = tmp[t - 1]; tmp[t] = v; m++; }
          }
          if (m < 4) continue;
          base = (m & 1) ? tmp[m >> 1] : 0.5 * (tmp[(m >> 1) - 1] + tmp[m >> 1]);
          ex[k] = sdb[k] - base;
          if (ex[k] > thr) { if (cnt[k] < 250) cnt[k]++; } else cnt[k] = cnt[k] > 2 ? cnt[k] - 2 : 0;
        }
        for (k = 2; k < K - 1; k++) {
          if (cnt[k] < hold) continue;
          a = Math.max(floorG, Math.min(1, Math.pow(10, -ex[k] / 20)));
          if (a < tg[k]) tg[k] = a;
          for (d = 1; d <= wid; d++) {
            c = Math.pow(a, d === 1 ? 0.7 : 0.4);
            if (k - d >= 1 && c < tg[k - d]) tg[k - d] = c;
            if (k + d < K && c < tg[k + d]) tg[k + d] = c;
          }
        }
        for (k = 0; k < K; k++) {
          gn[k] += (tg[k] - gn[k]) * (tg[k] < gn[k] ? 0.7 : 0.1);
          re[k] *= gn[k]; im[k] *= gn[k];
          if (k > 0 && k < H) { re[N - k] *= gn[k]; im[N - k] *= gn[k]; }
        }
        fft(true);
        for (j = 0; j < H; j++) outq[j] = ola[j] + re[j] / N * win[j];
        for (j = 0; j < H; j++) ola[j] = re[H + j] / N * win[H + j];
      };
      this.process = function (x, y) {
        var n = x.length, i2;
        for (i2 = 0; i2 < n; i2++) {
          if (y) y[i2] = outq[opos];
          inb[fill++] = x[i2];
          opos++;
          if (fill === N) {
            frame();
            for (var q = 0; q < H; q++) inb[q] = inb[H + q];
            fill = H; opos = 0;
          }
        }
        if (!(outq[0] === outq[0])) reset();
      };
      this.setLevel(5);
      reset();
    }

    // Les deux types derrière un seul objet : 0 = adaptatif, 1 = spectral. Chaque type garde son niveau ; créé à la première utilisation.
    function SdrlinkNf(sr) {
      var cores = [null, null], algo = 0, lv = [5, 5];
      var get = function (a) {
        var c = cores[a];
        if (!c) { c = cores[a] = a === 1 ? new SdrlinkSpf(sr) : new SdrlinkAnf(sr); c.setLevel(lv[a]); }
        return c;
      };
      this.set = function (a, l) {
        a = a | 0;
        if (a < 0 || a > 1) a = 0;
        lv[a] = Math.max(1, Math.min(10, l | 0 || 1));
        var c = get(a);
        c.setLevel(lv[a]);
        if (a !== algo) { algo = a; c.reset(); }
      };
      this.reset = function () { get(algo).reset(); };
      this.process = function (x, y) { get(algo).process(x, y); };
    }

    // Types de notch (1.50) : 0 = d'origine (filtres coupe-bande du navigateur, ci-dessus), 1 = adaptatif (NLMS), 2 = spectral
    // (FFT). Les types 1 et 2 passent par SdrlinkNf (indices 0 et 1), dans un AudioWorklet (pages https) ou sinon un ScriptProcessor.
    L.notchAlgo = 0; L.notchLvl = 1;
    L.NfCore = SdrlinkNf;   // pour les essais
    var nfUrl = null;
    var nfModule = function () {
      if (!nfUrl) {
        var src = SdrlinkAnf.toString() + '\n' + SdrlinkSpf.toString() + '\n' + SdrlinkNf.toString() +
          '\nregisterProcessor(\'sdrlink-nf\', class extends AudioWorkletProcessor {\n' +
          ' constructor(o) { super(); var p = o && o.processorOptions || {}, c = this.c = new SdrlinkNf(sampleRate); c.set(p.algo, p.lvl);\n' +
          '  this.port.onmessage = function (e) { if (e.data && e.data.lvl) c.set(e.data.algo, e.data.lvl); if (e.data && e.data.reset) c.reset(); }; }\n' +
          ' process(inp, out) { var x = inp[0] && inp[0][0], y = out[0] && out[0][0]; if (!x) { if (y) y.fill(0); return true; } this.c.process(x, y); return true; }\n});\n';
        nfUrl = URL.createObjectURL(new Blob([src], { type: 'application/javascript' }));
      }
      return nfUrl;
    };
    var nfScript = function (ctx, q) {
      var bs = 256;
      while (bs * 2 <= ctx.sampleRate * 0.045 && bs < 4096) bs *= 2;
      var sp = ctx.createScriptProcessor(bs, 1, 1), core = new SdrlinkNf(ctx.sampleRate);
      sp.channelCount = 1;
      sp.channelCountMode = 'explicit';
      core.set(L.notchAlgo - 1, L.notchLvl);
      sp.onaudioprocess = function (e) { core.process(e.inputBuffer.getChannelData(0), e.outputBuffer.getChannelData(0)); };
      q.core = core; q.kind = 'script';
      return sp;
    };
    var nf = function (ctx, on) {
      var q = ctx.__sdrlinkNf;
      if (!ctx.__sdrlinkIn) return;
      if (!q) {
        if (!on) return;
        q = ctx.__sdrlinkNf = { on: false, node: null, kind: '' };
        var ready = function (node) { q.node = node; if (q.on) rewire(ctx); };
        var fallback = function (why) { L.nfWhy = String(why && why.message || why); try { if (ctx.createScriptProcessor) ready(nfScript(ctx, q)); } catch (e) { } };
        try {
          if (!ctx.audioWorklet || !window.AudioWorkletNode) throw new Error('no AudioWorklet (page not secure)');
          ctx.audioWorklet.addModule(nfModule()).then(function () {
            var node = new AudioWorkletNode(ctx, 'sdrlink-nf', { numberOfInputs: 1, numberOfOutputs: 1, outputChannelCount: [1], channelCount: 1, channelCountMode: 'explicit',
              processorOptions: { algo: L.notchAlgo - 1, lvl: L.notchLvl } });
            q.kind = 'worklet';
            node.port.postMessage({ algo: L.notchAlgo - 1, lvl: L.notchLvl });
            ready(node);
          }).catch(fallback);
        } catch (e) { fallback(e); }
      }
      if (on === q.on) return;
      q.on = on;
      if (on && q.node) { if (q.kind === 'worklet') q.node.port.postMessage({ reset: 1 }); else if (q.core) q.core.reset(); }
      rewire(ctx);
    };
    L.nfKind = function () {
      for (var i = 0; i < L.ctxs.length; i++) { var q = L.ctxs[i].__sdrlinkNf; if (q && q.on && q.node) return q.kind; }
      return '';
    };
    var release = function (ctx, n, i) {
      n.slots[i].bin = -1;
      n.slots[i].placed = false;   // le prochain placement saute directement à la nouvelle fréquence
      n.f[i].frequency.setTargetAtTime(ctx.sampleRate * 0.49, ctx.currentTime, 0.01);
    };
    var analyse = function (ctx, n) {
      var an = n.an, N = an.frequencyBinCount, hz = ctx.sampleRate / an.fftSize;
      if (!n.cur) { n.cur = new Float32Array(N); n.avg = new Float32Array(N); n.cnt = new Float32Array(N); n.first = true; }
      an.getFloatFrequencyData(n.cur);
      var lo = Math.max(2, Math.ceil(150 / hz)), hi = Math.min(N - 3, Math.floor(4000 / hz)), i, v;
      for (i = lo - 1; i <= hi + 1; i++) {
        v = n.cur[i] > -160 ? n.cur[i] : -160;
        n.avg[i] = n.first ? v : 0.6 * n.avg[i] + 0.4 * v;
      }
      n.first = false;
      var sorted = Array.prototype.slice.call(n.avg, lo, hi + 1).sort(function (a, b) { return a - b; });
      var floor = sorted[sorted.length >> 1];
      if (floor < -140) return;   // silence
      // Raie candidate : pic local nettement au-dessus du bruit ; compteur de présence, toléré à un bin près.
      var cnt = new Float32Array(N), peaks = [];
      for (i = lo; i <= hi; i++) {
        if (n.avg[i] > floor + ABOVE && n.avg[i] >= n.avg[i - 1] && n.avg[i] >= n.avg[i + 1]) {
          cnt[i] = Math.max(n.cnt[i - 1], n.cnt[i], n.cnt[i + 1]) + 1;
          if (cnt[i] >= HOLD) peaks.push(i);
        }
      }
      n.cnt = cnt;
      peaks.sort(function (a, b) { return n.avg[b] - n.avg[a]; });
      var used = [];
      for (var s = 0; s < nAct; s++) {
        var sl = n.slots[s];
        if (sl.bin < 0) continue;
        var best = -1;
        for (var p = 0; p < peaks.length; p++) if (Math.abs(peaks[p] - sl.bin) <= 3 && used.indexOf(peaks[p]) < 0) { best = peaks[p]; break; }
        if (best < 0) { if (++sl.miss > RELEASE) release(ctx, n, s); continue; }
        sl.miss = 0;
        used.push(best);
        place(ctx, n, s, best, hz);
      }
      for (var q = 0; q < peaks.length; q++) {
        if (used.indexOf(peaks[q]) >= 0) continue;
        for (var t = 0; t < nAct; t++) {
          if (n.slots[t].bin >= 0) continue;
          used.push(peaks[q]);
          place(ctx, n, t, peaks[q], hz);
          break;
        }
      }
    };
    // Fréquence affinée par interpolation entre bins voisins ; largeur d'environ 40 Hz.
    var place = function (ctx, n, s, bin, hz) {
      var a = n.avg[bin - 1], b = n.avg[bin], c = n.avg[bin + 1], d = a - 2 * b + c;
      var f = (bin + (d !== 0 ? 0.5 * (a - c) / d : 0)) * hz;
      n.slots[s].bin = bin;
      n.slots[s].miss = 0;
      n.f[s].frequency.setTargetAtTime(f, ctx.currentTime, n.slots[s].placed ? 0.05 : 0.005);
      n.f[s].Q.value = Math.max(5, f / nWidth);
      n.slots[s].placed = true;
    };
    L.notchOn = false;
    // algo : 0 = d'origine, 1 = adaptatif, 2 = spectral (1.50) ; absent = d'origine (appel d'avant la 1.50). Le niveau est celui
    // du type choisi ; le notch d'origine garde ses tables de niveau.
    L.setNotch = function (on, lvl, algo) {
      L.notchLvl = Math.max(1, Math.min(10, lvl | 0 || 1));
      L.notchAlgo = Math.max(0, Math.min(2, algo | 0));
      lvl = L.notchLvl - 1;
      var old = nAct;
      nAct = NL_COUNT[lvl]; ABOVE = NL_ABOVE[lvl]; HOLD = NL_HOLD[lvl]; nWidth = NL_WIDTH[lvl];
      L.notchLevel = lvl + 1;
      if (nAct < old) for (var c = 0; c < L.ctxs.length; c++) { var nn = L.ctxs[c].__sdrlinkNotch; if (nn) for (var j = nAct; j < NOTCHES; j++) release(L.ctxs[c], nn, j); }
      L.notchOn = !!on;
      for (var i = 0; i < L.ctxs.length; i++) {
        var cx = L.ctxs[i], q = cx.__sdrlinkNf;
        if (q && q.node && L.notchAlgo > 0) { if (q.kind === 'worklet') q.node.port.postMessage({ algo: L.notchAlgo - 1, lvl: L.notchLvl }); else if (q.core) q.core.set(L.notchAlgo - 1, L.notchLvl); }
        notch(cx, L.notchOn && L.notchAlgo === 0);
        nf(cx, L.notchOn && L.notchAlgo > 0);
      }
    };
    setInterval(function () {
      if (!L.notchOn) return;
      for (var i = 0; i < L.ctxs.length; i++) {
        var n = L.ctxs[i].__sdrlinkNotch;
        if (n && n.on && L.ctxs[i].state === 'running') { try { analyse(L.ctxs[i], n); } catch (e) { } }
      }
    }, 100);
    AudioNode.prototype.connect = function (dest) {
      if (dest instanceof AudioDestinationNode) arguments[0] = master(dest.context);
      return origConnect.apply(this, arguments);
    };
    AudioNode.prototype.disconnect = function (dest) {
      if (dest instanceof AudioDestinationNode && dest.context.__sdrlinkIn) arguments[0] = dest.context.__sdrlinkIn;
      return origDisconnect.apply(this, arguments);
    };

    // Dérivation DIGI : blocs de 4096 échantillons mono 16 bits, envoyés en base64 (pcm|fréquence|données).
    var tap = function (ctx) {
      if (ctx.__sdrlinkTap || !ctx.createScriptProcessor) return;
      var sp = ctx.createScriptProcessor(4096, 1, 1), silent = ctx.createGain();
      sp.channelCount = 1;
      sp.channelCountMode = 'explicit';
      silent.gain.value = 0;
      ctx.__sdrlinkTapSrc = tapSrc(ctx);
      origConnect.call(ctx.__sdrlinkTapSrc, sp);
      origConnect.call(sp, silent);
      origConnect.call(silent, ctx.destination);   // un ScriptProcessor ne tourne que s'il est relié à la sortie
      sp.onaudioprocess = function (e) {
        if (!L.tapOn) return;
        var d = e.inputBuffer.getChannelData(0), b = new Uint8Array(d.length * 2);
        for (var i = 0; i < d.length; i++) {
          var v = Math.max(-1, Math.min(1, d[i])) * 32767 | 0;
          b[2 * i] = v & 255;
          b[2 * i + 1] = (v >> 8) & 255;
        }
        var s = '';
        for (var j = 0; j < b.length; j += 8192) s += String.fromCharCode.apply(null, b.subarray(j, j + 8192));
        try { window.chrome.webview.postMessage('pcm|' + ctx.sampleRate + '|' + btoa(s)); } catch (x) { }
      };
      ctx.__sdrlinkTap = sp;
    };
    // Point de prélèvement : son brut (câble, décodeurs), ou après le notch et le réducteur de bruit quand seule l'écoute en
    // diversité le demande (post), pour que l'oreille du WebSDR reste filtrée comme le haut-parleur.
    var tapSrc = function (ctx) {
      return L.tapPost ? chainEnd(ctx) : ctx.__sdrlinkIn;
    };
    var retap = function (ctx) {
      var sp = ctx.__sdrlinkTap, s = tapSrc(ctx);
      if (!sp || s === ctx.__sdrlinkTapSrc) return;
      try { origDisconnect.call(ctx.__sdrlinkTapSrc, sp); } catch (e) { }
      origConnect.call(s, sp);
      ctx.__sdrlinkTapSrc = s;
    };
    L.tapPost = false;
    L.setTap = function (on, post) {
      L.tapOn = !!on;
      L.tapPost = !!post;
      for (var i = 0; i < L.ctxs.length; i++) { if (on) tap(L.ctxs[i]); retap(L.ctxs[i]); }
    };

    // Surveillance du son (Réinit Auto WebSDR, comme PhoneSDRLink) : un analyseur sur l'entrée commune (avant volume,
    // Mute WebSDR et notch) regarde chaque seconde si la page produit du son ; L.lastSound = dernier instant avec du son.
    // Une page dont le son n'a jamais démarré n'est pas en panne ; un son qui a tourné puis dont le contexte audio est
    // suspendu ou fermé (arrêt par le serveur, « session timed out ») compte comme un son perdu.
    L.lastSound = Date.now();
    var watch = function (ctx) {
      if (ctx.__sdrlinkWatch || !ctx.createAnalyser) return;
      var a = ctx.__sdrlinkWatch = ctx.createAnalyser();
      a.fftSize = 2048;
      origConnect.call(ctx.__sdrlinkIn, a);
      a.buf = new Float32Array(a.fftSize);
    };
    setInterval(function () {
      var running = false, now = Date.now();
      for (var i = 0; i < L.ctxs.length; i++) {
        var c = L.ctxs[i], a = c.__sdrlinkWatch;
        if (!a || c.state !== 'running') continue;
        running = true;
        L.everRan = true;
        try {
          a.getFloatTimeDomainData(a.buf);
          for (var k = 0; k < a.buf.length; k++) if (Math.abs(a.buf[k]) > 0.00005) { L.lastSound = now; L.heard = true; break; }
        } catch (e) { }
      }
      if (!running && !L.everRan) L.lastSound = now;
    }, 1000);
  }
  // Durée (ms) sans son ; -2 si le volume de la page est à zéro ou son bouton « mute » enfoncé (choix de l'opérateur) ;
  // -1 si rien à surveiller (pas de son dans la page).
  L.silentMs = function () {
    if (!L.ctxs.length) return -1;
    try {
      var k = detect(), el = null;
      if (k === 'websdr') el = document.getElementById('volumecontrol2');
      else if (k === 'owrx' || k === 'owrx+') el = document.getElementById('openwebrx-panel-volume');
      else if (k === 'kiwi') el = document.querySelector('input.id-input-volume');
      else if (k === 'phantom') {
        var s = document.querySelectorAll('input.glass-slider');
        for (var j = 0; j < s.length; j++) if (s[j].min === '0' && s[j].max === '100') { el = s[j]; break; }
      }
      if (el && parseFloat(el.value) <= parseFloat(el.min || 0)) { L.lastSound = Date.now(); return -2; }
      // KiwiSDR : mute signalé par l'icône « id-mute-yes » affichée (sans w3-hide) ; variable globale muted si présente.
      if (k === 'kiwi' && window.muted === true) { L.lastSound = Date.now(); return -2; }
      var y = document.querySelectorAll('[class*=mute-yes i]');
      for (var n = 0; n < y.length; n++) if (!/(^|\s)w3-hide(\s|$)/.test(String(y[n].className))) { L.lastSound = Date.now(); return -2; }
      var m = document.querySelectorAll('[id*=mute i],[class*=mute i],[name*=mute i],[aria-label*=mute i],[title*=mute i]');
      for (var i = 0; i < m.length; i++) {
        var e2 = m[i], cls = String(e2.className && e2.className.baseVal !== undefined ? e2.className.baseVal : e2.className);
        if (e2.checked === true || e2.getAttribute('aria-pressed') === 'true' || /(^|[\s_-])(muted|active|on|pressed|selected|checked)([\s_-]|$)/i.test(cls)) { L.lastSound = Date.now(); return -2; }
      }
    } catch (e) { }
    return Date.now() - L.lastSound;
  };
  // v : 0 à 1. Agit aussi sur les éléments <audio>/<video> éventuels.
  L.setVolume = function (v) {
    L.volume = v;
    for (var i = 0; i < L.gains.length; i++) L.gains[i].gain.value = v;
    var media = document.querySelectorAll('audio,video');
    for (var j = 0; j < media.length; j++) media[j].volume = Math.min(1, v);
  };

  function detect() {
    if (window.UberSDR && typeof UberSDR.command === 'function' && typeof UberSDR.state === 'function') return 'ubersdr';
    // PhantomSDR (avec module CATsync) : imite en partie Kiwi/websdr.org, à reconnaître avant eux.
    if (window.spectrumAudio && typeof window.__catsync_setfreq_impl === 'function' && typeof window.setfreq === 'function') return 'phantom';
    if (typeof window.ext_tune === 'function' && typeof window.ext_get_freq === 'function') return 'kiwi';
    if (typeof window.nominalfreq === 'function' && typeof window.set_mode === 'function' && typeof window.setfreqb === 'function') return 'websdr';
    if (window.UI && typeof UI.getFrequency === 'function' && typeof UI.setFrequency === 'function' && typeof UI.getModulation === 'function') return 'owrx+';
    if (window.jQuery && jQuery.fn && jQuery.fn.demodulatorPanel && typeof window.center_freq === 'number') return 'owrx';
    return '';
  }
  // OpenWebRX à plusieurs profils (un par bande) : une fréquence hors du profil actif ne peut pas être réglée.
  // On choisit le profil qui la couvre d'après son nom ('HF_7MHz', 'VHF_144-146MHz', '40m'...), on bascule,
  // puis on règle la fréquence une fois le nouveau profil chargé.
  var BANDS = { '160m': [1.8, 2.0], '80m': [3.5, 3.8], '60m': [5.3, 5.45], '40m': [7.0, 7.2], '30m': [10.1, 10.15],
    '20m': [14.0, 14.35], '17m': [18.068, 18.168], '15m': [21.0, 21.45], '12m': [24.89, 24.99], '10m': [28.0, 29.7],
    '6m': [50, 52], '4m': [70, 70.5], '2m': [144, 146], '70cm': [430, 440], '23cm': [1240, 1300] };
  function profileRange(name) {
    var n = (name || '').replace(/,/g, '.'), m = n.match(/(\d+(?:\.\d+)?)\s*-\s*(\d+(?:\.\d+)?)\s*MHz/i);
    if (m) return [parseFloat(m[1]), parseFloat(m[2])];
    m = n.match(/(\d+(?:\.\d+)?)\s*MHz/i);
    if (m) { var c = parseFloat(m[1]); return [c - 1, c + 2]; }
    m = n.toLowerCase().match(/(\d+)\s*(cm|m)\b/);
    if (m && BANDS[m[1] + m[2]]) return BANDS[m[1] + m[2]];
    return null;
  }
  function owrxInRange(hz) {
    return typeof window.center_freq === 'number' && typeof window.bandwidth === 'number'
      && Math.abs(hz - center_freq) <= bandwidth / 2 - 1000;
  }
  function findProfile(hz) {
    var mhz = hz / 1e6, best = null, bestWidth = 1e9;
    var opts = document.querySelectorAll('#openwebrx-sdr-profiles-listbox option');
    for (var i = 0; i < opts.length; i++) {
      var r = profileRange(opts[i].textContent);
      if (r && mhz >= r[0] && mhz <= r[1] && r[1] - r[0] < bestWidth) { best = opts[i].value; bestWidth = r[1] - r[0]; }
    }
    return best;
  }
  function owrxTune(k, hz, mode) {
    var m = mode === 'fm' ? 'nfm' : mode;
    if (k === 'owrx+') {
      if (m) UI.setModulation(m);
      if (hz) UI.setFrequency(hz, false);
    } else {
      if (m) jQuery('#openwebrx-panel-receiver').demodulatorPanel().setMode(m);
      var d = owrxDemod();
      if (hz && d) d.set_offset_frequency(hz - center_freq);
    }
  }

  function owrxDemod() {
    var p = jQuery('#openwebrx-panel-receiver').demodulatorPanel();
    return p ? p.getDemodulator() : null;
  }

  // UberSDR v2 : API de page window.UberSDR, asynchrone. On relit 'tuning' à chaque tour et on garde la dernière réponse
  // (un abonnement lancé pendant le chargement de la page peut ne jamais aboutir).
  function uberTuning() {
    // Abonnement : la page prévient à chaque changement (~30 ms) ; la lecture ci-dessous, une réponse en retard
    // (~300 ms), reste en secours.
    if (!L.uberSub) {
      L.uberSub = true;
      try {
        UberSDR.on('tuning', function (v) { if (v && v.frequency) { L.uberTuning = v; L.poll(); } });
        Promise.resolve(UberSDR.subscribe(['tuning'])).catch(function () { });
      } catch (e) { }
    }
    if (!L.uberBusy) {
      L.uberBusy = true;
      UberSDR.get('tuning').then(
        function (t) { L.uberTuning = t; L.uberBusy = false; },
        function () { L.uberBusy = false; });
    }
    return L.uberTuning;
  }
  // Session UberSDR démarrée (« Click to start » passé) depuis plus de 0,7 s. Avant, un réglage ne part pas vers le
  // serveur et la page le remplace au démarrage par sa propre fréquence : l'affichage et le son ne concordaient plus.
  function uberRunning() {
    if (!L.uberRunAt && !L.uberSessBusy) {
      L.uberSessBusy = true;
      UberSDR.get('session').then(
        function (s) { if (s && s.running && !L.uberRunAt) L.uberRunAt = Date.now(); L.uberSessBusy = false; },
        function () { L.uberSessBusy = false; });
    }
    return L.uberRunAt && Date.now() - L.uberRunAt > 700;
  }

  L.get = function () {
    var k = detect();
    try {
      if (k === 'ubersdr') { var t = uberTuning(); if (t && t.frequency) return { k: k, f: t.frequency, m: t.mode }; return null; }
      if (k === 'phantom') {
        // Fréquence réellement écoutée : position de la porteuse dans la bande reçue (audioM, en points). Elle suit tous
        // les réglages, y compris un clic dans le spectre ou la cascade, qui ne met pas à jour __catsync_state.
        // À défaut : l'afficheur (MHz), puis __catsync_state.
        var sa = spectrumAudio, hz = 0;
        if (sa && sa.audioMaxSize > 0 && sa.totalBandwidth > 0 && typeof sa.audioM === 'number' && sa.audioM > 0)
          hz = Math.round(sa.baseFreq + sa.audioM * sa.totalBandwidth / sa.audioMaxSize);
        if (!hz) { var inp = document.querySelector('input.font-mono'); hz = inp ? parseFloat(inp.value) * 1e6 : 0; }
        if (!hz) { var st = window.__catsync_state; hz = st && st.hz || 0; }
        return { k: k, f: hz, m: spectrumAudio.demodulation };
      }
      if (k === 'kiwi') return { k: k, f: ext_get_freq(), m: ext_get_mode() };
      if (k === 'websdr') return { k: k, f: nominalfreq() * 1000, m: window.mode };
      if (k === 'owrx+') return { k: k, f: UI.getFrequency(), m: UI.getModulation() };
      if (k === 'owrx') { var d = owrxDemod(); if (d) return { k: k, f: d.get_offset_frequency() + center_freq, m: d.get_modulation() }; }
    } catch (e) { }
    return null;
  };

  // hz = 0 : ne pas changer la fréquence ; mode = '' : ne pas changer le mode. mode : lsb usb cw am fm
  L.set = function (hz, mode) {
    var k = detect();
    try {
      if (k === 'phantom') {
        // setfreq : valeur en kHz (en dessous de 1e6 la page la prend pour des kHz) ; set_mode : nom en majuscules.
        // La page choisit elle-même le mode de la bande quand la fréquence change (LSB sur 40 m…) : fréquence
        // d'abord, puis le mode voulu, ou celui d'avant (sinon ce changement repartirait vers le poste).
        var keep = String(spectrumAudio.demodulation || '');
        if (hz) setfreq(hz / 1000);
        var want = mode ? mode.toUpperCase() : keep.toUpperCase();
        if (want && want !== String(spectrumAudio.demodulation || '').toUpperCase()) set_mode(want);
      } else if (k === 'ubersdr') {
        var args = { ensureVisible: true };
        if (hz) args.frequency = hz;
        if (mode) args.mode = mode === 'fm' ? 'nfm' : mode === 'cw' ? 'cwu' : mode;
        UberSDR.command('tune', args).catch(function () { });
      } else if (k === 'kiwi') {
        var km = mode === 'fm' ? 'nbfm' : mode;
        // Sans zoom donné, ext_tune remet la cascade en vue « bande » (zoom_step(TO_BAND)) : le zoom choisi sur la page
        // était perdu à chaque fréquence ou mode venus de SDRLink. Zoom actuel redonné tel quel : la page n'y touche pas
        // et recentre d'elle-même la cascade, au même zoom, si la nouvelle fréquence sort de la vue.
        if (window.ext_zoom && typeof ext_zoom.ABS === 'number' && typeof window.ext_get_zoom === 'function') ext_tune(hz ? hz / 1000 : null, km || ext_get_mode(), ext_zoom.ABS, ext_get_zoom());
        else ext_tune(hz ? hz / 1000 : null, km || ext_get_mode());
      } else if (k === 'websdr') {
        if (mode && mode.toUpperCase() !== String(window.mode).toUpperCase()) set_mode(mode);
        if (hz) {
          // Fréquence hors de la bande affichée : on change de bande comme le bouton de la page (80, 40, 20…).
          // En vue « une bande » (Bordeaux), setfreqb ne le fait pas : seul le chiffre changeait, la cascade restait
          // sur l'ancienne bande. Le changement de bande inverse LSB/USB : le mode d'avant est remis.
          var b = websdrBand(hz / 1000);
          if (b >= 0 && b !== window.band && typeof window.setband === 'function') {
            var before = String(window.mode);
            setband(b);
            if (typeof window.bandeButton === 'function') bandeButton(b);
            if (String(window.mode).toUpperCase() !== before.toUpperCase()) set_mode(before.toLowerCase());
          }
          setfreqb(hz / 1000);
        }
      } else if (k === 'owrx+' || k === 'owrx') {
        if (hz && !owrxInRange(hz)) {
          var prof = findProfile(hz), sel = document.getElementById('openwebrx-sdr-profiles-listbox');
          if (prof && sel && typeof window.sdr_profile_changed === 'function') {
            // Mode à remettre après la bascule (le nouveau profil a son propre mode par défaut).
            var cur = L.get();
            L.pending = { hz: hz, mode: mode || (cur ? Modes(cur.m) : ''), until: Date.now() + 12000 };
            if (sel.value !== prof) { sel.value = prof; sdr_profile_changed(); }
            return;
          }
        }
        owrxTune(k, hz, mode);
      }
    } catch (e) { }
    if (L.bw && mode) setTimeout(L.applyBw, 500);   // un changement de mode rend à la page son filtre d'origine
    L.poll();
  };

  // Bande passante de réception (Hz), reprise de PhoneSDRLink. BLU : bord à 300 Hz de la porteuse, du côté du mode ;
  // AM, FM, CW : centrée sur le centre actuel du filtre. L.bw = 0 : filtre d'origine de la page (non modifié).
  L.bw = 0;
  L.setBw = function (bw) { L.bw = bw > 0 ? Math.round(bw) : 0; if (L.bw) L.applyBw(); };
  // Retour au filtre d'origine de la page (choix « Filtre d'origine ») : filtre par défaut du mode pour KiwiSDR et
  // websdr.org (le mode remis à l'identique n'y change rien), le mode réappliqué pour les autres.
  L.resetBw = function () {
    L.bw = 0;
    var k = detect(), s = L.get();
    try {
      if (k === 'kiwi' && typeof window.kiwi_passbands === 'function') { var pb = kiwi_passbands(ext_get_mode()); if (pb) { ext_set_passband(pb.lo, pb.hi); return; } }
      if (k === 'websdr' && typeof window.set_mode === 'function') { set_mode(String(window.mode)); return; }
    } catch (e) { }
    if (s && s.m) L.set(0, String(s.m).toLowerCase());
  };
  L.applyBw = function () {
    var bw = L.bw, k = detect(), s = bw ? L.get() : null;
    if (!bw || !k || !s) return;
    var m = String(s.m || '').toLowerCase(), side = m === 'lsb' ? -1 : m === 'usb' ? 1 : 0, E = 300, cur = null, p;
    try {
      if (k === 'websdr') cur = [window.lo * 1000, window.hi * 1000];
      else if (k === 'kiwi') { var kp = ext_get_passband(); cur = [kp.low, kp.high]; }
      else if (k === 'owrx+' || k === 'owrx') { var bp = (k === 'owrx+' ? UI.getDemodulator() : owrxDemod()).getBandpass(); cur = [bp.low_cut, bp.high_cut]; }
      else if (k === 'ubersdr') { var ut = uberTuning(); if (ut && ut.bandwidthLow != null) cur = [ut.bandwidthLow, ut.bandwidthHigh]; }
    } catch (e) { }
    var centre = cur && isFinite(cur[0]) && isFinite(cur[1]) ? (cur[0] + cur[1]) / 2 : 0;
    p = side > 0 ? [E, E + bw] : side < 0 ? [-E - bw, -E] : [centre - bw / 2, centre + bw / 2];
    try {
      if (k === 'websdr') { window.lo = p[0] / 1000; window.hi = p[1] / 1000; updbw(); }
      else if (k === 'kiwi') ext_set_passband(Math.round(p[0]), Math.round(p[1]));
      else if (k === 'owrx+' || k === 'owrx') (k === 'owrx+' ? UI.getDemodulator() : owrxDemod()).setBandpass({ low_cut: Math.round(p[0]), high_cut: Math.round(p[1]) });
      else if (k === 'ubersdr') UberSDR.command('passband', { low: Math.round(p[0]), high: Math.round(p[1]) }).catch(function () { });
      else if (k === 'phantom') {
        // Largeur en points du spectre ; la page décale les trois valeurs de réserve de 200/750/200 Hz.
        var a = spectrumAudio, r = a.getAudioRange(), bin = a.totalBandwidth / a.audioMaxSize, n = bw / bin, l2, r2, mm = r[1];
        if (m === 'lsb') { l2 = mm - n; r2 = mm; } else if (m === 'usb') { l2 = mm; r2 = mm + n; } else { var c = (r[0] + r[2]) / 2; l2 = c - n / 2; r2 = c + n / 2; }
        a.setAudioRange(l2, mm, r2, l2 - 200 / bin, mm - 750 / bin, r2 - 200 / bin);
      }
    } catch (e) { }
  };

  // Bordeaux : rappel du mode à côté du S-mètre, tenu à jour seulement par ses boutons de mode (ni par ses boutons
  // de bande, qui inversent pourtant LSB/USB, ni par SDRLink) : remis d'accord avec le mode réel de la page.
  function modeLabel() {
    var el = document.getElementById('getmode'), m = String(window.mode || '').toLowerCase();
    if (!el || !m || typeof window.ModeID !== 'function') return;
    var want = m.charAt(0).toUpperCase() + m.substring(1);
    if (el.innerHTML !== want) ModeID(want);
  }

  // websdr.org : bande (index dans bi) qui contient la fréquence f en kHz, -1 si aucune.
  function websdrBand(f) {
    if (!window.bi || typeof window.nvbands !== 'number') return -1;
    for (var i = 0; i < nvbands; i++) {
      var e = bi[i];
      if (e && f > e.centerfreq - e.samplerate / 2 - 4 && f < e.centerfreq + e.samplerate / 2 + 4) return i;
    }
    return -1;
  }

  // Mode OpenWebRX ramené à la famille comprise par owrxTune.
  function Modes(m) {
    m = String(m || '').toLowerCase();
    return m === 'nfm' || m === 'wfm' ? 'fm' : m;
  }

  L.poll = function () {
    // Bascule de profil OpenWebRX en cours : rien n'est signalé à SDRLink (la fréquence transitoire du nouveau
    // profil ne doit pas être recopiée sur le poste) ; on règle la fréquence dès que le profil est chargé.
    if (L.pending) {
      var k = detect();
      if (Date.now() > L.pending.until) { L.pending = null; return; }
      var ready = k === 'owrx+' ? !!UI.getDemodulator() : k === 'owrx' && !!owrxDemod();
      if (!ready || !owrxInRange(L.pending.hz)) return;
      try { owrxTune(k, L.pending.hz, L.pending.mode); } catch (e) { }
      // Terminé seulement quand la page affiche bien la fréquence voulue (sinon nouvel essai au tour suivant).
      var now = L.get();
      if (!now || Math.abs(now.f - L.pending.hz) > 50) return;
      L.pending = null;
    }
    // Éléments <audio>/<video> créés après le dernier réglage (sortie vers un autre périphérique, par ex.).
    if (L.volume !== 1) {
      var media = document.querySelectorAll('audio,video'), mv = Math.min(1, L.volume);
      for (var i = 0; i < media.length; i++) if (media[i].volume !== mv) media[i].volume = mv;
    }
    var s = L.get();
    if (!s || !(s.f > 0)) return;
    // websdr.org : la page règle elle-même sa fréquence de départ à la fin du chargement (onload), puis au démarrage
    // du son ; signalée plus tôt, SDRLink la réglerait pour rien et recopierait ensuite ce saut sur le poste.
    if (s.k === 'websdr' && !(window.allloadeddone || (L.loadedAt && Date.now() - L.loadedAt > 4000))) return;
    if (s.k === 'ubersdr' && !uberRunning()) return;
    // PhantomSDR : réglé avant les premiers échantillons audio, il restait muet ; on attend que le son joue.
    if (s.k === 'phantom' && !(window.spectrumAudio && spectrumAudio.playTime)) return;
    squelch(s);
    if (s.k === 'websdr') modeLabel();
    var msg = s.k + '|' + Math.round(s.f) + '|' + String(s.m || '');
    if (msg !== L.last) {
      L.last = msg;
      // « |u » : l'utilisateur vient d'agir sur la page (clic, touche, molette). SDRLink suit alors ce changement même
      // pendant la mise en place de la page, où un changement venu de la page seule est remis sur le poste.
      var user = Date.now() - L.userAt < 1500;
      try { window.chrome.webview.postMessage(msg + (user ? '|u' : '')); } catch (e) { }
    }
  };
  // Bouton Réinit WebSDR (clic) : réglages de la page gardés pour le rechargement, dans sessionStorage (il survit au
  // rechargement de l'onglet) : filtre (bande passante) et zoom de la cascade de websdr.org et de KiwiSDR. La fréquence
  // et le mode sont remis par SDRLink (reprise du WebSDR quitté) ; OpenWebRX garde les siens dans l'adresse (#freq=…),
  // KiwiSDR et les autres dans leur propre stockage. Valable aussi quand la page a été arrêtée par son délai
  // d'inactivité (« Idle time out. » de websdr.org) : ses variables restent lisibles.
  L.keep = function () {
    try {
      var s = L.get(), k = detect(), x = { at: Date.now(), k: k, f: s ? Math.round(s.f) : 0, m: s ? String(s.m || '') : '' };
      if (k === 'websdr') {
        if (typeof window.lo === 'number' && typeof window.hi === 'number') { x.lo = window.lo; x.hi = window.hi; }
        var e = window.bi && window.bi[window.band];
        if (e && typeof e.zoom === 'number' && typeof e.effcenterfreq === 'number') { x.b = window.band; x.z = e.zoom; x.c = e.effcenterfreq; }
      } else if (k === 'kiwi') {
        if (typeof window.ext_get_passband === 'function') { var p = ext_get_passband(); if (p) { x.lo = p.low; x.hi = p.high; } }
        if (typeof window.ext_get_zoom === 'function') x.z = ext_get_zoom();
      }
      sessionStorage.setItem('__sdrlink_keep', JSON.stringify(x));
      return JSON.stringify(x);
    } catch (e) { return ''; }
  };
  // Après le rechargement : réglages remis une fois, quand la page est prête et stable depuis 1,5 s dans le mode d'avant
  // (le filtre dépend du mode). Une minute au plus ; rien si la page a changé de type de WebSDR.
  var kept = null;
  try { kept = JSON.parse(sessionStorage.getItem('__sdrlink_keep') || 'null'); sessionStorage.removeItem('__sdrlink_keep'); } catch (e) { }
  if (kept && Date.now() - kept.at < 60000 && (kept.lo != null || kept.z != null)) {
    var keptF = 0, keptSince = 0, keptTimer = setInterval(function () {
      if (Date.now() - kept.at > 60000) { clearInterval(keptTimer); return; }
      var s = L.get();
      var ready = s && s.k === kept.k && s.f > 0 && String(s.m || '').toUpperCase() === String(kept.m).toUpperCase();
      if (ready && s.k === 'websdr' && !(window.allloadeddone || (L.loadedAt && Date.now() - L.loadedAt > 4000))) ready = false;
      if (!ready || Math.abs(s.f - keptF) > 100) { keptF = ready ? s.f : 0; keptSince = ready ? Date.now() : 0; return; }
      if (Date.now() - keptSince < 1500) return;
      clearInterval(keptTimer);
      try {
        if (kept.k === 'websdr') {
          if (kept.lo != null && typeof window.setmf === 'function') setmf(String(window.mode), kept.lo, kept.hi);
          if (kept.z != null && kept.b === window.band && typeof window.wfset_freq === 'function') wfset_freq(kept.b, kept.z, kept.c);
        } else if (kept.k === 'kiwi') {
          if (kept.lo != null && typeof window.ext_set_passband === 'function') ext_set_passband(kept.lo, kept.hi);
          if (kept.z != null && typeof window.ext_set_zoom === 'function' && window.ext_zoom) ext_set_zoom(ext_zoom.ABS, kept.z);
        }
        try { window.chrome.webview.postMessage('kept'); } catch (x) { }
      } catch (e) { }
    }, 250);
  }

  // Seuls les gestes réels comptent (isTrusted) : les appuis simulés par SDRLink au démarrage ne comptent pas.
  L.userAt = 0;
  ['pointerdown', 'mousedown', 'pointerup', 'keydown', 'wheel', 'touchstart', 'touchend'].forEach(function (t) {
    window.addEventListener(t, function (e) { if (e.isTrusted) L.userAt = Date.now(); }, true);
  });
  window.addEventListener('pointermove', function (e) { if (e.isTrusted && e.buttons) L.userAt = Date.now(); }, true);
  // Appui de la souris dans la page, signalé tout de suite (avant le changement de fréquence qu'il peut faire) :
  // SDRLink arrête un scan en cours et garde la fréquence scannée à ce moment.
  window.addEventListener('pointerdown', function (e) {
    if (e.isTrusted) { try { window.chrome.webview.postMessage('click'); } catch (x) { } }
  }, true);

  window.addEventListener('load', function () { L.loadedAt = Date.now(); });
  setInterval(L.poll, 125);   // un changement fait sur la page est signalé à SDRLink en 60 ms en moyenne

  // Squelch ouvert (websdr.org : case « squelch », OpenWebRX : curseur au minimum), sauf en FM à partir de 30 MHz
  // (relais, FM VHF/UHF) où il est remis tel que la page l'avait. Appliqué au chargement puis seulement quand on entre
  // ou sort de la FM VHF : un squelch remis à la main par l'utilisateur est laissé tel quel.
  function squelch(s) {
    var want = /fm/i.test(String(s.m || '')) && s.f >= 30e6;
    if (want === L.sqWant) return;
    var el = s.k === 'websdr' ? document.getElementById('squelchcheckbox')
           : s.k === 'owrx+' || s.k === 'owrx' ? document.querySelector('.openwebrx-squelch-slider') : null;
    if (!el) return;
    L.sqWant = want;
    try {
      if (el.type === 'checkbox') {
        if (typeof window.setsquelch !== 'function') return;
        if (!want && el.checked) { L.sqWas = true; el.checked = false; window.setsquelch(false); }
        else if (want && L.sqWas === true && !el.checked) { L.sqWas = null; el.checked = true; window.setsquelch(true); }
      } else {
        var min = parseFloat(el.min), v = parseFloat(el.value);
        if (!want && v > min) { L.sqWas = el.value; slide(el, el.min); }
        else if (want && L.sqWas != null && v <= min) { slide(el, L.sqWas); L.sqWas = null; }
      }
    } catch (e) { }
  }
  function slide(el, v) {
    el.value = v;
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }

  // Démarrage sans clic : la lecture automatique étant permise, seul UberSDR attend encore un clic sur
  // « Click to start ». SDRLink clique une fois à la place de l'utilisateur, dans les 30 s qui suivent l'ouverture.
  var startAt = Date.now(), startTimer = setInterval(function () {
    var b = document.querySelector('button.start__go:not(.start__go--alt)');
    if (b && b.offsetParent !== null) { clearInterval(startTimer); try { b.click(); } catch (e) { } }
    else if (Date.now() - startAt > 30000) clearInterval(startTimer);
  }, 500);

  // PhantomSDR (French Alps…) : voile « Tap to enable audio » ; la page attend un appui de souris n'importe où
  // pour activer le son et retirer le voile. SDRLink le simule, toutes les 0,5 s tant que le voile est là (la page
  // peut l'afficher avant d'écouter l'appui), dans les 30 s qui suivent l'ouverture.
  var tapTimer = setInterval(function () {
    if (Date.now() - startAt > 30000) { clearInterval(tapTimer); return; }
    var v = document.getElementById('startaudio');
    if (!v || v.offsetParent === null) return;
    try { document.documentElement.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })); } catch (e) { }
  }, 500);

  // PhantomSDR : après un changement de fréquence (de SDRLink, du clavier ou d'un clic dans le spectre), le son
  // décodé tombe parfois à zéro 1 à 3 s plus tard et le reste, bien que les trames continuent d'arriver ; changer de
  // mode puis revenir le relance. SDRLink renvoie le mode courant au serveur dès qu'une demi-seconde entière de trames
  // reste muette (le squelch ou le mute de la page arrêtent les trames avant : ils ne la déclenchent pas). Trois
  // essais au plus, rendus quand le son revient.
  var ph = { fr: 0, pk: 0, at: 0, tries: 0 };
  setInterval(function () {
    var a = window.spectrumAudio;
    if (!a || typeof a.playPCM !== 'function' || typeof a.setAudioDemodulation !== 'function') return;
    if (!a.__sdrlinkPcm) {
      var play = a.playPCM;
      a.playPCM = function (e) {
        ph.fr++;
        try { for (var i = 0; i < e.length; i += 4) { var v = e[i] < 0 ? -e[i] : e[i]; if (v > ph.pk) ph.pk = v; } } catch (x) { }
        return play.apply(this, arguments);
      };
      a.__sdrlinkPcm = true;
      ph.at = Date.now();
      return;
    }
    if (a.audioM !== ph.m) { ph.m = a.audioM; ph.tries = 0; }   // nouvelle fréquence : nouveaux essais
    if (Date.now() - ph.at < 500) return;
    if (ph.fr >= 10 && ph.pk < 0.0005) {
      if (ph.tries < 3 && a.playTime) { ph.tries++; try { a.setAudioDemodulation(a.demodulation); } catch (e) { } }
    } else if (ph.pk >= 0.0005) ph.tries = 0;
    ph.fr = 0; ph.pk = 0; ph.at = Date.now();
  }, 250);

  // KiwiSDR complet : SDRLink appuie sur « Enter queue » (file d'attente) ; KiwiSDR recharge ensuite la page de
  // lui-même dès qu'un canal se libère. Avec des canaux à mot de passe, « Queue or Camp » mène d'abord à cet écran.
  // Une fois par ouverture, dans les 30 s : une sortie de file voulue par l'utilisateur n'est pas annulée.
  var queueTimer = setInterval(function () {
    if (Date.now() - startAt > 30000) { clearInterval(queueTimer); return; }
    if (!window.kiwi) return;
    var btns = document.querySelectorAll('button');
    for (var i = 0; i < btns.length; i++) {
      var b = btns[i], t = (b.textContent || '').trim();
      if (b.offsetParent === null) continue;
      if ((t === 'Enter queue' && !window.kiwi.queued) || t === 'Queue or Camp') { clearInterval(queueTimer); try { b.click(); } catch (e) { } return; }
    }
  }, 500);
})();
";

        // Pseudo donné aux WebSDR qui en demandent un avant le son : websdr.org le lit dans le cookie « username »,
        // KiwiSDR dans localStorage « ident » (deux caractères au moins : un pseudo d'un seul caractère y est doublé).
        // Un nom tapé directement sur le site est gardé ; celui que SDRLink avait mis
        // (repères __sdrlink_ident et __sdrlink_user) est remplacé quand le pseudo change.
        public static string Identity(string name)
        {
            name = Clean(name);
            if (name == "") return null;
            System.Text.StringBuilder js = new System.Text.StringBuilder("'");
            foreach (char c in name)
                if (c < 0x20 || c > 0x7E) js.AppendFormat("\\u{0:x4}", (int)c); else js.Append(c);
            js.Append("'");
            return @"
(function (name) {
  if (window.top !== window) return;
  try {
    var kiwi = name.length >= 2 ? name : name + name;
    var cur = localStorage.getItem('ident'), mine = localStorage.getItem('__sdrlink_ident');
    if (mine === null) mine = localStorage.getItem('__sdrlink_name');   // repère des premiers essais de la 1.20
    if (!cur || cur === mine) { localStorage.setItem('ident', kiwi); localStorage.setItem('__sdrlink_ident', kiwi); }
  } catch (e) { }
  try {
    // Repère du cookie sous un autre nom : KiwiSDR recopie les cookies du site dans son localStorage.
    var get = function (k) { var m = document.cookie.match(new RegExp('(?:^|;\\s*)' + k + '=([^;]*)')); return m ? m[1] : null; };
    var curC = get('username'), mineC = get('__sdrlink_user'), tail = '; max-age=157680000; path=/; SameSite=Lax';
    if (mineC === null) mineC = get('__sdrlink_name');
    if (!curC || curC === mineC) { document.cookie = 'username=' + name + tail; document.cookie = '__sdrlink_user=' + name + tail; }
  } catch (e) { }
})(" + js + ");";
        }

        // Pseudo utilisable tel quel dans un cookie et une chaîne JavaScript : sans ; , = \ ' "" ni caractères de contrôle,
        // 30 caractères au plus ; espaces et tabulations normalisés en U+00A0. Vide : pseudo refusé.
        public const char Nbsp = '\u00A0';

        public static string Clean(string name)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in name ?? "")
            {
                char x = c == ' ' || c == '\t' ? Nbsp : c;
                if (!char.IsControl(x) && ";,=\\'\"".IndexOf(x) < 0 && sb.Length < 30) sb.Append(x);
            }
            return sb.ToString();
        }
    }
}
