#ifndef SDRDEC_RADE_H
#define SDRDEC_RADE_H
#ifdef __cplusplus
extern "C" {
#endif
typedef struct sdrdec_rade sdrdec_rade;
sdrdec_rade *sdrdec_rade_new(void);          /* RADE V1 (FreeDV 2.x) */
sdrdec_rade *sdrdec_rade_new_v(int v2);       /* v2 = 1 : RADE V2 (FreeDV 3.0, en développement) */
void sdrdec_rade_free(sdrdec_rade *s);
/* in : n échantillons réels, flottants [-1,1], 8000 Hz (signal RADE en bande de base audio, ~300-2700 Hz).
 * out : parole 16 bits 16000 Hz ; renvoie le nombre d'échantillons écrits (0 tant que le décodeur n'est pas synchronisé).
 * sync : 1 si synchronisé ; snr_db : estimation du rapport signal/bruit (dB) ; foff : décalage de fréquence (Hz). */
int sdrdec_rade_process(sdrdec_rade *s, const float *in, int n, short *out, int out_max, int *sync, int *snr_db, float *foff);
#ifdef __cplusplus
}
#endif
#endif
