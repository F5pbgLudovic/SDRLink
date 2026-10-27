namespace SDRLink
{
    // Script injecté dans chaque page : détecte le type de WebSDR, signale ses changements
    // (fréquence/mode) à l'application et expose window.__sdrlink.set(hz, mode).
    public static class WebSdrBridge
    {
        public const string Script = @"
(function () {
  if (window.top !== window || window.__sdrlink) return;
  var L = window.__sdrlink = { last: '', volume: 1, gains: [] };

  // Volume propre au WebSDR : tout ce que la page envoie vers la sortie audio passe par un gain réglable.
  // Installé avant les scripts de la page, donc valable pour tous les types de WebSDR.
  if (window.AudioNode && window.AudioDestinationNode) {
    var origConnect = AudioNode.prototype.connect, origDisconnect = AudioNode.prototype.disconnect;
    var master = function (ctx) {
      if (!ctx.__sdrlinkGain) {
        var g = ctx.createGain();
        g.gain.value = L.volume;
        origConnect.call(g, ctx.destination);
        ctx.__sdrlinkGain = g;
        L.gains.push(g);
      }
      return ctx.__sdrlinkGain;
    };
    AudioNode.prototype.connect = function (dest) {
      if (dest instanceof AudioDestinationNode) arguments[0] = master(dest.context);
      return origConnect.apply(this, arguments);
    };
    AudioNode.prototype.disconnect = function (dest) {
      if (dest instanceof AudioDestinationNode && dest.context.__sdrlinkGain) arguments[0] = dest.context.__sdrlinkGain;
      return origDisconnect.apply(this, arguments);
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
