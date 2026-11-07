// Décodeurs numériques natifs de SDRLink (sdrlink_digi.dll, 32 bits) : interface C simple (cdecl), sans JNI.
// - FT8 / FT4 / FT2 : ft8_lib de Kārlis Goba (licence MIT, voir ft8lib/LICENSE), enveloppe sdrdec_ft8.c reprise de PhoneSDRLink.
// - Olivia / Contestia : bibliothèque MFSK de Pawel Jalocha (jalocha/*.h, reprise de fldigi, GNU GPL), réception seule.
// - Aides d'essai : générateur de tranches FT8/FT4/FT2 (encodeur de ft8_lib, GFSK), essai de bouclage et fichier WAV Olivia.
// Portage de sdrdec_jni.c et olivia_jni.cpp (PhoneSDRLink).
#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#include <string>
#include <vector>
#include "sdrdec_ft8.h"
#include "jalocha/pj_mfsk.h"
extern "C" {
#include "ft8lib/ft8/encode.h"
#include "ft8lib/ft8/message.h"
#include "ft8lib/ft8/constants.h"
}

#define DIGI_API extern "C" __declspec(dllexport)

// ---------------------------------------------------------------- FT8 / FT4 / FT2

// Son 16 bits (n échantillons à rate Hz, 15 s au plus) -> lignes « snr|dt|freq|texte\n » dans out (terminé par 0) ; renvoie le nombre de messages.
DIGI_API int digi_ft8_decode(const short *pcm, int n, int rate, int mode, char *out, int outMax) {
    if (out && outMax > 0) out[0] = 0;
    if (!pcm || n <= 0 || !out || outMax <= 1) return 0;
    float *x = (float *)malloc(sizeof(float) * (size_t)n);
    if (!x) return 0;
    for (int i = 0; i < n; i++) x[i] = pcm[i] / 32768.0f;
    int k = sdrdec_ft8_decode(x, n, rate, mode, out, outMax);
    free(x);
    return k;
}

// Vide la table des indicatifs « hachés ».
DIGI_API void digi_ft8_reset(void) { sdrdec_ft8_reset(); }

namespace {
// Impulsion GFSK (comme demo/gen_ft8.c de ft8_lib).
void gfskPulse(int nsps, float bt, std::vector<float> &p) {
    const float K = 5.336446f;   // pi * sqrt(2 / ln 2)
    p.resize((size_t)(3 * nsps));
    for (int i = 0; i < 3 * nsps; i++) {
        float t = i / (float)nsps - 1.5f;
        p[i] = (erff(K * bt * (t + 0.5f)) - erff(K * bt * (t - 0.5f))) / 2;
    }
}

// Ajoute à sig (à partir de start) le signal GFSK des tons, amplitude amp, fréquence de base f0.
void synthAdd(const uint8_t *sym, int nsym, float f0, float bt, float period, int rate, double amp, std::vector<double> &sig, long start) {
    int nsps = (int)(rate * period + 0.5f);
    int nwave = nsym * nsps;
    std::vector<float> pulse;
    gfskPulse(nsps, bt, pulse);
    double peak = 2 * M_PI / nsps;
    std::vector<double> dphi((size_t)(nwave + 2 * nsps), 2 * M_PI * f0 / rate);
    for (int i = 0; i < nsym; i++) {
        int ib = i * nsps;
        for (int j = 0; j < 3 * nsps; j++) dphi[(size_t)(j + ib)] += peak * sym[i] * pulse[j];
    }
    for (int j = 0; j < 2 * nsps; j++) {
        dphi[(size_t)j] += peak * pulse[(size_t)(j + nsps)] * sym[0];
        dphi[(size_t)(j + nsym * nsps)] += peak * pulse[(size_t)j] * sym[nsym - 1];
    }
    int nramp = nsps / 8;
    double phi = 0;
    for (int k = 0; k < nwave; k++) {
        double v = sin(phi);
        if (k < nramp) v *= (1 - cos(2 * M_PI * k / (2 * nramp))) / 2;
        if (k >= nwave - nramp) v *= (1 - cos(2 * M_PI * (nwave - 1 - k) / (2 * nramp))) / 2;
        long idx = start + k;
        if (idx >= 0 && idx < (long)sig.size()) sig[(size_t)idx] += amp * v;
        phi = fmod(phi + dphi[(size_t)(k + nsps)], 2 * M_PI);
    }
}

struct Rng {
    unsigned long long s;
    double uni() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (double)(s >> 11) / 9007199254740992.0; }
    double gauss() { double g = 0; for (int k = 0; k < 12; k++) g += uni(); return g - 6.0; }
};
}

// Essai : tranche synthétique FT8 (mode 0), FT4 (1) ou FT2 (2) à 12000 Hz dans out (outMax échantillons au plus, la durée d'une tranche).
// msgs : une ligne par message « freq|dt|snr|texte » (dt en s par rapport au début normal à 0,5 s ; snr en dB dans 2500 Hz).
// noise : écart-type du bruit (1 = pleine échelle ; 0 = sans bruit). Renvoie le nombre d'échantillons, < 0 en cas d'erreur (-k : message k illisible).
DIGI_API int digi_ft8_synth(int mode, const char *msgs, unsigned int seed, double noise, short *out, int outMax) {
    const int rate = 12000;
    double slot = mode == 2 ? 3.75 : mode == 1 ? 7.5 : 15.0;
    int n = (int)(slot * rate);
    if (n > outMax) n = outMax;
    if (n <= 0 || !out) return -1000;
    std::vector<double> sig((size_t)n, 0.0);
    std::string all(msgs ? msgs : "");
    size_t pos = 0;
    int idx = 0;
    while (pos < all.size()) {
        size_t e = all.find('\n', pos);
        if (e == std::string::npos) e = all.size();
        std::string ln = all.substr(pos, e - pos);
        pos = e + 1;
        if (ln.empty()) continue;
        idx++;
        float f0 = 0, dt = 0, snr = 0;
        int used = 0;
        if (sscanf(ln.c_str(), "%f|%f|%f|%n", &f0, &dt, &snr, &used) < 3 || used <= 0) return -idx;
        ftx_message_t m;
        if (ftx_message_encode(&m, NULL, ln.c_str() + used) != FTX_MESSAGE_RC_OK) return -idx;
        uint8_t tones[FT4_NN];
        int nsym;
        float bt, period;
        if (mode == 0) { ft8_encode(m.payload, tones); nsym = FT8_NN; bt = 2.0f; period = FT8_SYMBOL_PERIOD; }
        else { ft4_encode(m.payload, tones); nsym = FT4_NN; bt = 1.0f; period = mode == 2 ? 0.024f : FT4_SYMBOL_PERIOD; }
        // SNR dans 2500 Hz : (A²/2) / (σ² · 2500 / (rate/2))
        double sigma = noise > 0 ? noise : 0.01;
        double amp = sqrt(2.0 * pow(10.0, snr / 10.0) * sigma * sigma * 2500.0 / (rate / 2.0));
        synthAdd(tones, nsym, f0, bt, period, rate, amp, sig, (long)((0.5 + dt) * rate));
    }
    Rng r; r.s = 88172645463325252ULL ^ ((unsigned long long)seed * 2654435761ULL + 1);
    for (int i = 0; i < n; i++) {
        double v = (sig[(size_t)i] + (noise > 0 ? noise * r.gauss() : 0)) * 32767.0;
        if (v > 32767) v = 32767; if (v < -32768) v = -32768;
        out[i] = (short)v;
    }
    return n;
}

// ---------------------------------------------------------------- Olivia / Contestia

namespace {
struct Oli {
    MFSK_Receiver<double> rx;
    int tones, bw, margin, integ, contestia, reverse;
    double thr;
    std::vector<double> buf;
};

int firstCarrierOffset(int tones, int bw, int contestia) {
    return contestia ? bw / 2 : (int)(bw * (1.0 - 0.5 / tones) / 2.0);
}

int oliPreset(Oli *o, int center) {
    MFSK_Receiver<double> &r = o->rx;
    r.bContestia = o->contestia != 0;
    r.Tones = (size_t)o->tones;
    r.Bandwidth = (size_t)o->bw;
    r.SyncMargin = (size_t)o->margin;
    r.SyncIntegLen = (size_t)o->integ;
    r.SyncThreshold = o->thr;
    r.SampleRate = 8000;
    r.InputSampleRate = 8000;
    int off = firstCarrierOffset(o->tones, o->bw, o->contestia);
    if (o->reverse) { r.FirstCarrierMultiplier = (float)((center + off) / 500.0); r.Reverse = 1; }
    else { r.FirstCarrierMultiplier = (float)((center - off) / 500.0); r.Reverse = 0; }
    return r.Preset();
}

// Émet text (Olivia ou Contestia) centré sur center Hz à 8000 Hz : signal sans bruit.
bool oliTransmit(int tones, int bw, int contestia, const char *text, int center, std::vector<double> &sig, int guardMax) {
    MFSK_Transmitter<double> t;
    t.Default();
    t.bContestia = contestia != 0;
    t.Tones = (size_t)tones; t.Bandwidth = (size_t)bw; t.SampleRate = 8000; t.OutputSampleRate = 8000;
    int off = firstCarrierOffset(tones, bw, contestia);
    t.FirstCarrierMultiplier = (float)((center - off) / 500.0); t.Reverse = 0;
    if (t.Preset() < 0) return false;
    std::vector<double> ob((size_t)t.MaxOutputLen + 16);
    std::string msg(text ? text : "");
    t.Start();
    t.PutChar(0);
    for (size_t i = 0; i < msg.size(); i++) t.PutChar((uint8_t)msg[i]);
    t.Stop();
    int guard = 0;
    while (t.Running() && guard++ < guardMax) { int len = t.Output(&ob[0]); for (int i = 0; i < len; i++) sig.push_back(ob[i]); }
    return true;
}
}

// Nouveau décodeur (centre en Hz audio, thr10 = seuil de synchro en dixièmes) ; NULL si les réglages sont refusés.
DIGI_API void *digi_olivia_new(int tones, int bw, int margin, int integ, int contestia, int center, int reverse, int thr10) {
    Oli *o = new Oli();
    o->tones = tones; o->bw = bw; o->margin = margin; o->integ = integ; o->contestia = contestia; o->reverse = reverse;
    o->thr = thr10 / 10.0;
    if (oliPreset(o, center) < 0) { delete o; return NULL; }
    return o;
}

DIGI_API void digi_olivia_free(void *h) { delete (Oli *)h; }

// Nouvelle fréquence centrale (le décodeur repart de zéro). Renvoie 0 si bon.
DIGI_API int digi_olivia_center(void *h, int center) {
    Oli *o = (Oli *)h;
    return o ? oliPreset(o, center) : -1;
}

// Son 8000 Hz (16 bits) -> caractères décodés dans out (outMax au plus) ; st = { rapport signal/bruit de la synchro, décalage de fréquence en Hz }.
DIGI_API int digi_olivia_process(void *h, const short *in, int n, unsigned char *out, int outMax, double *st) {
    Oli *o = (Oli *)h;
    if (!o || n <= 0 || !in) return 0;
    if ((int)o->buf.size() < n) o->buf.resize((size_t)n);
    for (int i = 0; i < n; i++) o->buf[(size_t)i] = in[i] / 32768.0;
    o->rx.Process(&o->buf[0], (size_t)n);
    int k = 0;
    uint8_t c;
    while (o->rx.GetChar(c) > 0) { if (out && k < outMax) out[k++] = c; }
    if (st) { st[0] = (double)o->rx.SignalToNoiseRatio(); st[1] = (double)o->rx.FrequencyOffset(); }
    return k;
}

// Essai : son Olivia / Contestia à 8000 Hz (16 bits) dans out : pre s de bruit seul, le texte centré sur center Hz, post s de bruit seul.
// snr10 : rapport signal/bruit en dixièmes de dB dans 2,5 kHz. Renvoie le nombre d'échantillons (tronqué à outMax), < 0 en cas d'erreur.
DIGI_API int digi_olivia_make(int tones, int bw, int contestia, const char *text, int snr10, int center, int pre, int post, unsigned int seed, short *out, int outMax) {
    std::vector<double> sig((size_t)(8000 * (pre > 0 ? pre : 0)), 0.0);
    size_t sigStart = sig.size();
    if (!oliTransmit(tones, bw, contestia, text, center, sig, 400000)) return -1;
    size_t sigEnd = sig.size();
    for (int i = 0; i < 8000 * (post > 0 ? post : 0); i++) sig.push_back(0);
    double ps = 0;
    for (size_t i = sigStart; i < sigEnd; i++) ps += sig[i] * sig[i];
    ps /= (double)(sigEnd > sigStart ? sigEnd - sigStart : 1);
    double sigma = sqrt(ps * 4000.0 / 2500.0 / pow(10.0, snr10 / 100.0));
    Rng r; r.s = 88172645463325252ULL ^ ((unsigned long long)seed * 2654435761ULL);
    int n = (int)sig.size();
    if (n > outMax) n = outMax;
    for (int i = 0; i < n; i++) {
        double v = (sig[(size_t)i] + sigma * r.gauss()) * 6000.0;
        if (v > 32000) v = 32000; if (v < -32000) v = -32000;
        out[i] = (short)v;
    }
    return n;
}

// Fichier WAV d'essai (8000 Hz, 16 bits, mono), comme oliviaMakeWav de PhoneSDRLink : 8 s de bruit avant, 12 s après. Renvoie le nombre d'échantillons.
DIGI_API int digi_olivia_make_wav(int tones, int bw, int contestia, const char *text, int snr10, int center, const char *path) {
    std::vector<short> s((size_t)8000 * 600);
    int n = digi_olivia_make(tones, bw, contestia, text, snr10, center, 8, 12, 0, &s[0], (int)s.size());
    if (n < 0) return n;
    FILE *f = fopen(path, "wb");
    if (!f) return -2;
    uint32_t dataLen = (uint32_t)n * 2, riff = 36 + dataLen, rate = 8000, brate = 16000, sixteen32 = 16;
    uint16_t one = 1, two = 2, sixteen = 16;
    fwrite("RIFF", 1, 4, f); fwrite(&riff, 4, 1, f); fwrite("WAVEfmt ", 1, 8, f); fwrite(&sixteen32, 4, 1, f); fwrite(&one, 2, 1, f); fwrite(&one, 2, 1, f);
    fwrite(&rate, 4, 1, f); fwrite(&brate, 4, 1, f); fwrite(&two, 2, 1, f); fwrite(&sixteen, 2, 1, f); fwrite("data", 1, 4, f); fwrite(&dataLen, 4, 1, f);
    fwrite(&s[0], 2, (size_t)n, f);
    fclose(f);
    return n;
}

// Essai de bouclage (comme oliviaLoopTest) : émet text à 1500 Hz, bruit snr10 (dixièmes de dB dans 2,5 kHz), réception centrée sur 1500 + dfreq Hz.
// Écrit « texte décodé|rapport max|décalage » dans out ; renvoie la longueur.
DIGI_API int digi_olivia_loop_test(int tones, int bw, int contestia, const char *text, int snr10, int dfreq, char *out, int outMax) {
    std::string res;
    std::vector<double> sig;
    if (!oliTransmit(tones, bw, contestia, text, 1500, sig, 200000)) res = "erreur Preset émetteur";
    else {
        for (int i = 0; i < 8000 * 6; i++) sig.push_back(0);
        double ps = 0;
        for (size_t i = 0; i < sig.size(); i++) ps += sig[i] * sig[i];
        ps /= sig.size();
        ps = ps * sig.size() / (sig.size() - 8000.0 * 6);
        double sigma = sqrt(ps * 4000.0 / 2500.0 / pow(10.0, snr10 / 100.0));
        Rng r; r.s = 88172645463325252ULL;
        Oli *o = new Oli();
        o->tones = tones; o->bw = bw; o->margin = 8; o->integ = 4; o->contestia = contestia; o->reverse = 0; o->thr = 3.0;
        if (oliPreset(o, 1500 + dfreq) < 0) res = "erreur Preset récepteur";
        else {
            double lastOff = 0, maxSnr = 0;
            std::vector<double> blk;
            for (size_t i = 0; i < sig.size(); i += 1024) {
                size_t n = sig.size() - i < 1024 ? sig.size() - i : 1024;
                blk.resize(n);
                for (size_t j = 0; j < n; j++) blk[j] = sig[i + j] + sigma * r.gauss();
                o->rx.Process(&blk[0], n);
                uint8_t c;
                while (o->rx.GetChar(c) > 0) if (c > 7) res.push_back((char)c);
                double sn = o->rx.SignalToNoiseRatio();
                lastOff = o->rx.FrequencyOffset();
                if (sn > maxSnr) maxSnr = sn;
            }
            char tail[96];
            snprintf(tail, sizeof tail, "|%.1f|%.1f", maxSnr, lastOff);
            res += tail;
        }
        delete o;
    }
    if (!out || outMax <= 0) return (int)res.size();
    int k = (int)res.size() < outMax - 1 ? (int)res.size() : outMax - 1;
    memcpy(out, res.data(), (size_t)k);
    out[k] = 0;
    return k;
}
