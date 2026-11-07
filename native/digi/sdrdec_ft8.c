/* Décodeur FT8 de PhoneSDRLink : enveloppe autour de ft8_lib (Kārlis Goba, licence MIT, voir ft8lib/LICENSE).
 * Dérivé de demo/decode_ft8.c : table de hachage des indicatifs et boucle de décodage. */
#include "sdrdec_ft8.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdbool.h>
#include <stdint.h>
#include <math.h>
#include "ft8lib/ft8/decode.h"
#include "ft8lib/ft8/message.h"
#include "ft8lib/common/monitor.h"

#define MIN_SCORE 10
#define MAX_CANDIDATES 140
#define LDPC_ITER 25
#define MAX_DECODED 50
#define FREQ_OSR 2
#define TIME_OSR 2
#define HASH_SIZE 256

static struct { char callsign[12]; uint32_t hash; } ht[HASH_SIZE];

void sdrdec_ft8_reset(void) { memset(ht, 0, sizeof(ht)); }

static void ht_cleanup(uint8_t max_age) {
    for (int i = 0; i < HASH_SIZE; ++i) {
        if (ht[i].callsign[0] != '\0') {
            uint8_t age = (uint8_t)(ht[i].hash >> 24);
            if (age > max_age) { ht[i].callsign[0] = '\0'; ht[i].hash = 0; }
            else ht[i].hash = (((uint32_t)age + 1u) << 24) | (ht[i].hash & 0x3FFFFFu);
        }
    }
}

static void ht_add(const char *callsign, uint32_t hash) {
    uint16_t h10 = (hash >> 12) & 0x3FFu;
    int idx = (h10 * 23) % HASH_SIZE;
    int guard = 0;
    while (ht[idx].callsign[0] != '\0' && guard++ < HASH_SIZE) {
        if (((ht[idx].hash & 0x3FFFFFu) == hash) && (0 == strcmp(ht[idx].callsign, callsign))) { ht[idx].hash &= 0x3FFFFFu; return; }
        idx = (idx + 1) % HASH_SIZE;
    }
    if (guard >= HASH_SIZE) return;
    strncpy(ht[idx].callsign, callsign, 11);
    ht[idx].callsign[11] = '\0';
    ht[idx].hash = hash;
}

static bool ht_lookup(ftx_callsign_hash_type_t type, uint32_t hash, char *callsign) {
    uint8_t shift = (type == FTX_CALLSIGN_HASH_10_BITS) ? 12 : (type == FTX_CALLSIGN_HASH_12_BITS ? 10 : 0);
    uint16_t h10 = (hash >> (12 - shift)) & 0x3FFu;
    int idx = (h10 * 23) % HASH_SIZE;
    int guard = 0;
    while (ht[idx].callsign[0] != '\0' && guard++ < HASH_SIZE) {
        if (((ht[idx].hash & 0x3FFFFFu) >> shift) == hash) { strcpy(callsign, ht[idx].callsign); return true; }
        idx = (idx + 1) % HASH_SIZE;
    }
    callsign[0] = '\0';
    return false;
}

static ftx_callsign_hash_interface_t hash_if = { .lookup_hash = ht_lookup, .save_hash = ht_add };

int sdrdec_ft8_decode(const float *signal, int n, int sample_rate, int ft4, char *out, int out_size) {
    monitor_t mon;
    /* ft4 : 0 = FT8, 1 = FT4, 2 = FT2 (FT4 dont les symboles durent 24 ms au lieu de 48 ; tranche de 3,75 s) */
    monitor_config_t cfg = { .f_min = 200, .f_max = 3000, .sample_rate = sample_rate, .time_osr = TIME_OSR, .freq_osr = FREQ_OSR, .protocol = ft4 ? FTX_PROTOCOL_FT4 : FTX_PROTOCOL_FT8 };
    if (ft4 == 2) { cfg.symbol_period = 0.024f; cfg.slot_time = 3.75f; cfg.scan_max = 56; }
    monitor_init(&mon, &cfg);
    for (int pos = 0; pos + mon.block_size <= n; pos += mon.block_size) monitor_process(&mon, signal + pos);

    ftx_candidate_t cand_list[MAX_CANDIDATES];
    int num_cand = ftx_find_candidates(&mon.wf, MAX_CANDIDATES, cand_list, MIN_SCORE);
    ftx_message_t decoded[MAX_DECODED];
    ftx_message_t *decoded_ht[MAX_DECODED];
    for (int i = 0; i < MAX_DECODED; ++i) decoded_ht[i] = NULL;
    int num_decoded = 0, used = 0;
    if (out_size > 0) out[0] = '\0';

    for (int idx = 0; idx < num_cand && num_decoded < MAX_DECODED; ++idx) {
        const ftx_candidate_t *c = &cand_list[idx];
        float freq_hz = (mon.min_bin + c->freq_offset + (float)c->freq_sub / mon.wf.freq_osr) / mon.symbol_period;
        float time_sec = (c->time_offset + (float)c->time_sub / mon.wf.time_osr) * mon.symbol_period;
        ftx_message_t message;
        ftx_decode_status_t status;
        if (!ftx_decode_candidate(&mon.wf, c, LDPC_ITER, &message, &status)) continue;

        int h = message.hash % MAX_DECODED, tries = 0;
        bool empty = false, dup = false;
        while (!empty && !dup && tries++ < MAX_DECODED) {
            if (decoded_ht[h] == NULL) empty = true;
            else if (decoded_ht[h]->hash == message.hash && 0 == memcmp(decoded_ht[h]->payload, message.payload, sizeof(message.payload))) dup = true;
            else h = (h + 1) % MAX_DECODED;
        }
        if (!empty) continue;
        memcpy(&decoded[h], &message, sizeof(message));
        decoded_ht[h] = &decoded[h];
        ++num_decoded;

        char text[FTX_MAX_MESSAGE_LENGTH];
        ftx_message_offsets_t offsets;
        if (ftx_message_decode(&message, &hash_if, text, &offsets) != FTX_MESSAGE_RC_OK) continue;
        float snr = c->score * 0.5f;   /* approximation grossière (comme ft8_lib) */
        int w = snprintf(out + used, out_size - used, "%.0f|%.2f|%.0f|%s\n", snr, time_sec, freq_hz, text);
        if (w < 0 || w >= out_size - used) break;
        used += w;
    }
    ht_cleanup(10);
    monitor_free(&mon);
    return num_decoded;
}
