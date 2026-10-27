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
        ctx.__sdrlinkIn = input;
        ctx.__sdrlinkOut = g;
        L.gains.push(g);
        L.ctxs.push(ctx);
        if (L.tapOn) tap(ctx);
        if (L.notchOn) notch(ctx, true);
      }
      return ctx.__sdrlinkIn;
    };

    // Notch automatique (comme le DNF des Yaesu) : un analyseur repère les raies stables (porteuses, sifflements)
    // et jusqu'à 4 filtres coupe-bande étroits les suivent. Le chant et la parole bougent trop pour être retenus.
    // Seul le haut-parleur est filtré : le câble SDRLINK reçoit le son brut, pour les logiciels de décodage.
    var NOTCHES = 4, HOLD = 6, RELEASE = 10, ABOVE = 18;
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
      if (on) {
        origDisconnect.call(input, out);
        origConnect.call(input, n.f[0]);
        origConnect.call(n.f[NOTCHES - 1], out);
      } else {
        origDisconnect.call(input, n.f[0]);
        origDisconnect.call(n.f[NOTCHES - 1], out);
        origConnect.call(input, out);
        for (var j = 0; j < NOTCHES; j++) release(ctx, n, j);
      }
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
      for (var s = 0; s < NOTCHES; s++) {
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
        for (var t = 0; t < NOTCHES; t++) {
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
      n.f[s].Q.value = Math.max(5, f / 40);
      n.slots[s].placed = true;
    };
    L.notchOn = false;
    L.setNotch = function (on) {
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
      origConnect.call(ctx.__sdrlinkIn, sp);
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
    L.setTap = function (on) {
      L.tapOn = !!on;
      if (on) for (var i = 0; i < L.ctxs.length; i++) tap(L.ctxs[i]);
    };
  }
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
    if (!L.uberBusy) {
      L.uberBusy = true;
      UberSDR.get('tuning').then(
        function (t) { L.uberTuning = t; L.uberBusy = false; },
        function () { L.uberBusy = false; });
    }
    return L.uberTuning;
  }

  L.get = function () {
    var k = detect();
    try {
      if (k === 'ubersdr') { var t = uberTuning(); if (t && t.frequency) return { k: k, f: t.frequency, m: t.mode }; return null; }
      if (k === 'phantom') {
        // Fréquence tenue à jour par la page à chaque changement ; avant le premier, on lit l'afficheur (MHz).
        var st = window.__catsync_state, hz = st && st.hz;
        if (!hz) { var inp = document.querySelector('input.font-mono'); hz = inp ? parseFloat(inp.value) * 1e6 : 0; }
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
        if (mode) set_mode(mode.toUpperCase());
        if (hz) setfreq(hz / 1000);
      } else if (k === 'ubersdr') {
        var args = { ensureVisible: true };
        if (hz) args.frequency = hz;
        if (mode) args.mode = mode === 'fm' ? 'nfm' : mode === 'cw' ? 'cwu' : mode;
        UberSDR.command('tune', args).catch(function () { });
      } else if (k === 'kiwi') {
        var km = mode === 'fm' ? 'nbfm' : mode;
        ext_tune(hz ? hz / 1000 : null, km || ext_get_mode());
      } else if (k === 'websdr') {
        if (mode && mode.toUpperCase() !== String(window.mode).toUpperCase()) set_mode(mode);
        if (hz) setfreqb(hz / 1000);
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
    L.poll();
  };

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
    var msg = s.k + '|' + Math.round(s.f) + '|' + String(s.m || '');
    if (msg !== L.last) {
      L.last = msg;
      try { window.chrome.webview.postMessage(msg); } catch (e) { }
    }
  };

  setInterval(L.poll, 250);
})();
";
    }
}
