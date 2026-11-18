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
        if (L.notchOn) notch(ctx, true);
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
    // Chaîne du haut-parleur (1.46) : entrée → [notch] → [réducteur de bruit] → gain de sortie ; liens refaits à chaque changement.
    var chainEnd = function (ctx) {
      var n = ctx.__sdrlinkNotch, d = ctx.__sdrlinkDnr, end = ctx.__sdrlinkIn;
      if (n && n.on) end = n.f[NOTCHES - 1];
      if (d && d.on && d.node) end = d.node;
      return end;
    };
    var rewire = function (ctx) {
      var input = ctx.__sdrlinkIn, out = ctx.__sdrlinkOut, n = ctx.__sdrlinkNotch, d = ctx.__sdrlinkDnr;
      if (!input) return;
      var links = ctx.__sdrlinkLinks || [];
      for (var i = 0; i < links.length; i++) { try { origDisconnect.call(links[i][0], links[i][1]); } catch (e) { } }
      links = ctx.__sdrlinkLinks = [];
      var prev = input;
      var link = function (a, b) { origConnect.call(a, b); links.push([a, b]); };
      if (n && n.on) { link(prev, n.f[0]); prev = n.f[NOTCHES - 1]; }
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
    L.DnrCore = SdrlinkDnr;   // pour les essais
    var dnrUrl = null;
    var dnrModule = function () {
      if (!dnrUrl) {
        var src = SdrlinkDnr.toString() + '\nregisterProcessor(\'sdrlink-dnr\', class extends AudioWorkletProcessor {\n' +
          ' constructor() { super(); var c = this.c = new SdrlinkDnr(sampleRate); this.port.onmessage = function (e) { if (e.data && e.data.lvl) c.setLevel(e.data.lvl); if (e.data && e.data.reset) c.reset(); }; }\n' +
          ' process(inp, out) { var x = inp[0] && inp[0][0], y = out[0] && out[0][0]; if (!x) { if (y) y.fill(0); return true; } this.c.process(x, y); return true; }\n});\n';
        dnrUrl = URL.createObjectURL(new Blob([src], { type: 'application/javascript' }));
      }
      return dnrUrl;
    };
    L.dnrLevel = 1;
    var dnrScript = function (ctx, d) {
      // Tampon d'environ 20 à 45 ms (le ScriptProcessor ajoute environ deux tampons de retard).
      var bs = 256;
      while (bs * 2 <= ctx.sampleRate * 0.045 && bs < 4096) bs *= 2;
      var sp = ctx.createScriptProcessor(bs, 1, 1), core = new SdrlinkDnr(ctx.sampleRate);
      sp.channelCount = 1;
      sp.channelCountMode = 'explicit';
      core.setLevel(L.dnrLevel);
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
            var node = new AudioWorkletNode(ctx, 'sdrlink-dnr', { numberOfInputs: 1, numberOfOutputs: 1, outputChannelCount: [1], channelCount: 1, channelCountMode: 'explicit' });
            d.kind = 'worklet';
            node.port.postMessage({ lvl: L.dnrLevel });
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
    L.setDnr = function (on, lvl) {
      L.dnrLevel = Math.max(1, Math.min(10, lvl | 0 || 1));
      L.dnrOn = !!on;
      for (var i = 0; i < L.ctxs.length; i++) {
        var d = L.ctxs[i].__sdrlinkDnr;
        if (d && d.node) { if (d.kind === 'worklet') d.node.port.postMessage({ lvl: L.dnrLevel }); else if (d.core) d.core.setLevel(L.dnrLevel); }
        dnr(L.ctxs[i], L.dnrOn);
      }
    };
    L.dnrKind = function () {
      for (var i = 0; i < L.ctxs.length; i++) { var d = L.ctxs[i].__sdrlinkDnr; if (d && d.on && d.node) return d.kind; }
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
    L.setNotch = function (on, lvl) {
      lvl = Math.max(0, Math.min(9, (lvl || 1) - 1));
      var old = nAct;
      nAct = NL_COUNT[lvl]; ABOVE = NL_ABOVE[lvl]; HOLD = NL_HOLD[lvl]; nWidth = NL_WIDTH[lvl];
      L.notchLevel = lvl + 1;
      if (nAct < old) for (var c = 0; c < L.ctxs.length; c++) { var nn = L.ctxs[c].__sdrlinkNotch; if (nn) for (var j = nAct; j < NOTCHES; j++) release(L.ctxs[c], nn, j); }
      L.notchOn = !!on;
      for (var i = 0; i < L.ctxs.length; i++) notch(L.ctxs[i], L.notchOn);
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
