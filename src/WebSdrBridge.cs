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
        ext_tune(hz ? hz / 1000 : null, km || ext_get_mode());
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
    L.poll();
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
  // Seuls les gestes réels comptent (isTrusted) : les appuis simulés par SDRLink au démarrage ne comptent pas.
  L.userAt = 0;
  ['pointerdown', 'mousedown', 'pointerup', 'keydown', 'wheel', 'touchstart', 'touchend'].forEach(function (t) {
    window.addEventListener(t, function (e) { if (e.isTrusted) L.userAt = Date.now(); }, true);
  });
  window.addEventListener('pointermove', function (e) { if (e.isTrusted && e.buttons) L.userAt = Date.now(); }, true);

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
        // KiwiSDR dans localStorage « ident » (deux caractères au moins : un pseudo d'un seul caractère, comme l'espace
        // insécable d'Alt+255, y est doublé). Un nom tapé directement sur le site est gardé ; celui que SDRLink avait mis
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
        // 30 caractères au plus. Seuls les espaces ordinaires sont ôtés autour : l'espace insécable (Alt+255) est un
        // pseudo permis, pour qui ne veut pas donner de nom. Vide : pseudo refusé.
        public static string Clean(string name)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in (name ?? "").Trim(' ', '\t'))
                if (!char.IsControl(c) && ";,=\\'\"".IndexOf(c) < 0 && sb.Length < 30) sb.Append(c);
            return sb.ToString().Trim(' ', '\t');
        }
    }
}
