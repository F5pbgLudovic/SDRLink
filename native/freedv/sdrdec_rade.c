/* Récepteur FreeDV RADE de SDRLink : enveloppe autour de radae_nopy / rade_c (David Rowe et Peter B Marks, BSD 2 clauses,
 * voir rade/LICENSE) et du vocodeur FARGAN d'Opus (BSD, voir opus/COPYING).
 * Chaîne, comme FreeDV 2.4.1 (freedv-backend RADEReceiveStep) : audio réel 8 kHz → complexe (imag = 0) → récepteur RADE
 * (bottleneck 3, données auxiliaires, filtre passe-bande) → traits du vocodeur (36 flottants par trame de 10 ms) → FARGAN
 * (amorcé une fois avec des zéros, jamais remis à zéro) → parole 16 kHz.
 * Possibilité gardée mais NON UTILISÉE par SDRLink (1.56 : il écoute la bande latérale choisie, LSB → LSB, USB → USB) : un récepteur « miroir » décode le signal reçu sur l'autre bande latérale (poste en LSB alors que
 * FreeDV émet toujours en USB) : x[n]·e^{jωn}, ω = 2π·fm/8000, ramène la composante −f en fm − f, c'est-à-dire le spectre
 * retourné autour du centre des porteuses (fm = 2950 Hz en V1 : porteuses 750-2200 Hz ; 2937,5 Hz en V2 : 1062,5-1875 Hz). */
#include "sdrdec_rade.h"
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include "rade/rade_rx.h"
#include "rade2/rade_rx_v2.h"
#include "rade/rade_dsp.h"
#include "opus/dnn/fargan.h"
#include "opus/dnn/lpcnet.h"

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

#define IQ_CAP 8192
#define MIX_N 160          /* période du mélangeur : 2950/8000 = 59/160 (V1) ; 2937,5/8000 = 47/128 (V2) */

struct sdrdec_rade {
    rade_rx_state rx;
    rade_rx_v2_state rx2;          /* récepteur RADE V2 (FreeDV 3.0, en développement) */
    int v2, mirror, mix_i, mix_n;
    RADE_COMP mix[MIX_N];
    float features2[RADE_V2_FEATURES_OUT];
    FARGANState fargan;
    int was_sync;
    RADE_COMP iq[IQ_CAP];
    int iq_n;
    float features[RADE_NZMF * RADE_FRAMES_PER_STEP * RADE_NB_TOTAL_FEATURES];
    float eoo[512];
    int nin_max;
    RADE_COMP *rx_in;
};

sdrdec_rade *sdrdec_rade_new_m(int v2, int mirror) {
    sdrdec_rade *s = (sdrdec_rade *)calloc(1, sizeof(*s));
    if (!s) return NULL;
    s->v2 = v2 ? 1 : 0;
    s->mirror = mirror ? 1 : 0;
    if (s->v2) {
        if (rade_rx_v2_init(&s->rx2, 1) != 0) { free(s); return NULL; }
        s->rx2.verbose = 0;
    } else {
        if (rade_rx_init(&s->rx, NULL, 3, 1, 1) != 0) { free(s); return NULL; }
        s->rx.verbose = 0;
    }
    /* FARGAN amorcé une fois avec des traits nuls, comme FreeDV 2.4.1 (RADEReceiveStep) */
    fargan_init(&s->fargan);
    {
        float zeros[320] = { 0 };
        float in_features[5 * NB_TOTAL_FEATURES] = { 0 };
        fargan_cont(&s->fargan, zeros, in_features);
    }
    int k = s->v2 ? 47 : 59;   /* fm = k × 8000 / mix_n */
    s->mix_n = s->v2 ? 128 : 160;
    for (int i = 0; i < s->mix_n; i++) {
        double ph = 2.0 * M_PI * k * i / s->mix_n;
        s->mix[i].real = (float)cos(ph);
        s->mix[i].imag = (float)sin(ph);
    }
    s->nin_max = s->v2 ? rade_rx_v2_nin_max() : rade_rx_nin_max(&s->rx);
    s->rx_in = (RADE_COMP *)malloc(sizeof(RADE_COMP) * (size_t)s->nin_max);
    if (!s->rx_in) { free(s); return NULL; }
    return s;
}

sdrdec_rade *sdrdec_rade_new_v(int v2) { return sdrdec_rade_new_m(v2, 0); }
sdrdec_rade *sdrdec_rade_new(void) { return sdrdec_rade_new_m(0, 0); }

void sdrdec_rade_free(sdrdec_rade *s) {
    if (!s) return;
    free(s->rx_in);
    free(s);
}

int sdrdec_rade_process(sdrdec_rade *s, const float *in, int n, short *out, int out_max, int *sync, int *snr_db, float *foff) {
    int written = 0;
    for (int k = 0; k < n; k++) {
        if (s->iq_n < IQ_CAP) {
            if (s->mirror) {
                RADE_COMP m = s->mix[s->mix_i];
                s->iq[s->iq_n].real = in[k] * m.real;
                s->iq[s->iq_n].imag = in[k] * m.imag;
            } else {
                s->iq[s->iq_n].real = in[k];
                s->iq[s->iq_n].imag = 0.0f;
            }
            s->iq_n++;
        }
        if (++s->mix_i == s->mix_n) s->mix_i = 0;

        int nin = s->v2 ? rade_rx_v2_nin(&s->rx2) : rade_rx_nin(&s->rx);
        while (s->iq_n >= nin) {
            memcpy(s->rx_in, s->iq, sizeof(RADE_COMP) * (size_t)nin);
            memmove(s->iq, s->iq + nin, sizeof(RADE_COMP) * (size_t)(s->iq_n - nin));
            s->iq_n -= nin;
            int r, sy, nf, stride;
            const float *base;
            if (s->v2) {
                r = rade_rx_v2_process(&s->rx2, s->features2, s->rx_in);
                sy = s->rx2.state == RADE_RX_V2_SYNC;
                base = s->features2; nf = RADE_V2_FRAMES_PER_STEP; stride = RADE_V2_NB_TOTAL_FEATURES;
            } else {
                r = rade_rx_process(&s->rx, s->features, s->eoo, s->rx_in);
                sy = rade_rx_sync(&s->rx);
                base = s->features; nf = RADE_NZMF * RADE_FRAMES_PER_STEP; stride = RADE_NB_TOTAL_FEATURES;
            }
            s->was_sync = sy;
            if (r & 1) {
                for (int f = 0; f < nf; f++) {
                    if (written + LPCNET_FRAME_SIZE > out_max) break;
                    float fpcm[LPCNET_FRAME_SIZE], feat[NB_FEATURES];
                    memcpy(feat, &base[f * stride], sizeof(float) * NB_FEATURES);
                    fargan_synthesize(&s->fargan, fpcm, feat);
                    for (int i = 0; i < LPCNET_FRAME_SIZE; i++) {
                        float v = floorf(0.5f + 32768.0f * fpcm[i]);
                        out[written++] = (short)(v > 32767.0f ? 32767 : (v < -32767.0f ? -32767 : v));
                    }
                }
            }
            nin = s->v2 ? rade_rx_v2_nin(&s->rx2) : rade_rx_nin(&s->rx);
        }
    }
    if (sync) *sync = s->was_sync;
    if (snr_db) *snr_db = (int)lroundf(s->v2 ? s->rx2.snr_est_dB : rade_rx_snrdB_3k_est(&s->rx));
    if (foff) *foff = s->v2 ? s->rx2.freq_offset : rade_rx_freq_offset(&s->rx);
    return written;
}
