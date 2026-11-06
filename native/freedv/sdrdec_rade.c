/* Récepteur FreeDV RADE V1 de PhoneSDRLink : enveloppe autour de radae_nopy (David Rowe et Peter B Marks, BSD 2 clauses,
 * voir rade/LICENSE) et du vocodeur FARGAN d'Opus (BSD, voir opus/COPYING).
 * Chaîne : audio réel 8 kHz → signal analytique (filtre de Hilbert) → récepteur RADE → traits du vocodeur (36 flottants par
 * trame de 10 ms) → FARGAN → parole 16 kHz. */
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

/* Entrée du récepteur : 1 = signal réel (partie imaginaire nulle), comme FreeDV 2.4.1 (RADEReceiveStep : int16 / 32767, imag = 0) ;
   0 = signal analytique (filtre de Hilbert), ancienne méthode de PhoneSDRLink 1.9 à 1.11. */
#ifndef RADE_REAL_INPUT
#define RADE_REAL_INPUT 1
#endif

#define HILBERT_NTAPS 127
#define HILBERT_DELAY ((HILBERT_NTAPS - 1) / 2)
#define IQ_CAP 8192

struct sdrdec_rade {
    rade_rx_state rx;
    rade_rx_v2_state rx2;          /* récepteur RADE V2 (FreeDV 3.0, en développement) */
    int v2;
    float features2[RADE_V2_FEATURES_OUT];
    FARGANState fargan;
    int fargan_primed, prime_n, was_sync;
    float prime[5 * NB_FEATURES];
    float hil[HILBERT_NTAPS];
    float hist[HILBERT_NTAPS];   /* dernières entrées (la plus récente en [0]) */
    RADE_COMP iq[IQ_CAP];
    int iq_n;
    float features[RADE_NZMF * RADE_FRAMES_PER_STEP * RADE_NB_TOTAL_FEATURES];
    float eoo[512];
    int nin_max;
    RADE_COMP *rx_in;
};

sdrdec_rade *sdrdec_rade_new_v(int v2) {
    sdrdec_rade *s = (sdrdec_rade *)calloc(1, sizeof(*s));
    if (!s) return NULL;
    s->v2 = v2 ? 1 : 0;
    if (s->v2) {
        if (rade_rx_v2_init(&s->rx2, 1) != 0) { free(s); return NULL; }
        s->rx2.verbose = 0;
    } else {
        if (rade_rx_init(&s->rx, NULL, 3, 1, 1) != 0) { free(s); return NULL; }
        s->rx.verbose = 0;
    }
    fargan_init(&s->fargan);
    s->nin_max = s->v2 ? rade_rx_v2_nin_max() : rade_rx_nin_max(&s->rx);
    s->rx_in = (RADE_COMP *)malloc(sizeof(RADE_COMP) * (size_t)s->nin_max);
    if (!s->rx_in) { free(s); return NULL; }
    for (int i = 0; i < HILBERT_NTAPS; i++) {
        int n = i - HILBERT_DELAY;
        if (n == 0 || n % 2 == 0) s->hil[i] = 0.0f;
        else s->hil[i] = (2.0f / ((float)M_PI * n)) * (0.54f - 0.46f * cosf(2.0f * (float)M_PI * i / (HILBERT_NTAPS - 1)));
    }
    return s;
}

sdrdec_rade *sdrdec_rade_new(void) { return sdrdec_rade_new_v(0); }

void sdrdec_rade_free(sdrdec_rade *s) {
    if (!s) return;
    free(s->rx_in);
    free(s);
}

int sdrdec_rade_process(sdrdec_rade *s, const float *in, int n, short *out, int out_max, int *sync, int *snr_db, float *foff) {
    int written = 0;
    for (int k = 0; k < n; k++) {
        /* signal analytique : I = entrée retardée, Q = sortie du filtre de Hilbert */
        memmove(&s->hist[1], &s->hist[0], sizeof(float) * (HILBERT_NTAPS - 1));
        s->hist[0] = in[k];
#if RADE_REAL_INPUT
        if (s->iq_n < IQ_CAP) { s->iq[s->iq_n].real = in[k]; s->iq[s->iq_n].imag = 0.0f; s->iq_n++; }
#else
        float q = 0.0f;

        for (int j = 0; j < HILBERT_NTAPS; j++) q += s->hil[j] * s->hist[j];
        if (s->iq_n < IQ_CAP) { s->iq[s->iq_n].real = s->hist[HILBERT_DELAY]; s->iq[s->iq_n].imag = q; s->iq_n++; }
#endif

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
            if (sy && !s->was_sync) { s->fargan_primed = 0; s->prime_n = 0; }   /* nouvelle synchronisation : le vocodeur repart */
            s->was_sync = sy;
            if (r & 1) {
                for (int f = 0; f < nf; f++) {
                    const float *ft = &base[f * stride];
                    if (!s->fargan_primed) {
                        memcpy(&s->prime[s->prime_n * NB_FEATURES], ft, sizeof(float) * NB_FEATURES);
                        if (++s->prime_n == 5) {
                            float zeros[320] = { 0 };
                            fargan_cont(&s->fargan, zeros, s->prime);
                            s->fargan_primed = 1;
                        }
                    } else if (written + LPCNET_FRAME_SIZE <= out_max) {
                        float fpcm[LPCNET_FRAME_SIZE], feat[NB_FEATURES];
                        memcpy(feat, ft, sizeof(float) * NB_FEATURES);
                        fargan_synthesize(&s->fargan, fpcm, feat);
                        for (int i = 0; i < LPCNET_FRAME_SIZE; i++) {
                            float v = floorf(0.5f + 32768.0f * fpcm[i]);
                            out[written++] = (short)(v > 32767.0f ? 32767 : (v < -32767.0f ? -32767 : v));
                        }
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
