#ifndef SDRDEC_FT8_H
#define SDRDEC_FT8_H
#ifdef __cplusplus
extern "C" {
#endif
/* Décode une tranche FT8 (ft4 = 0) ou FT4 (ft4 = 1) : signal en flottants [-1,1], cadence sample_rate (12000 conseillé), n échantillons (15 s au plus).
 * Écrit dans out une ligne par message : "snr|dt|freq|texte\n" ; renvoie le nombre de messages. */
int sdrdec_ft8_decode(const float *signal, int n, int sample_rate, int ft4, char *out, int out_size);
void sdrdec_ft8_reset(void);   /* vide la table des indicatifs « hachés » */
#ifdef __cplusplus
}
#endif
#endif
