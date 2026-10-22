/*
 * Synthetic target for the coverage-guided C harness demonstration.
 *
 * This file is deliberately NOT part of the ASP.NET service. It models a few
 * validator branches so the harness can prove that runtime feedback drives
 * corpus growth. The production application remains out of scope for this
 * particular engine; see the evidence/report limitations.
 */
#include "fixture_target.h"

static uint16_t read_u16(const uint8_t *p) {
    return (uint16_t)(p[0] | ((uint16_t)p[1] << 8));
}

static uint32_t read_u32(const uint8_t *p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) |
           ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

static int parse_order(const uint8_t *d, size_t n) {
    if (n < 5) return 10;
    uint8_t kind = d[0] & 3u;
    uint16_t quantity = read_u16(d + 1);
    uint32_t price = read_u32(d + 3);
    if (quantity == 0) return 11;
    if (quantity > 100) return 12;
    if (price < 1000) return 13;
    if (price > 100000000u) return 14;
    if (kind == 0) return quantity > 10 ? 15 : 0;
    if (kind == 1) return (price % 500u) ? 16 : 0;
    if (kind == 2) return ((uint64_t)quantity * price > 5000000u) ? 17 : 0;
    if (quantity == 7 && price == 777000u) return 18;
    return 19;
}

static int parse_payment(const uint8_t *d, size_t n) {
    if (n < 6) return 20;
    uint8_t state = d[0];
    uint32_t amount = read_u32(d + 1);
    uint8_t retry = d[5];
    if (amount == 0) return 21;
    if (amount > 100000000u) return 22;
    if (state == 0) return 23;
    if (state == 1) return (amount % 100u) ? 24 : 0;
    if (state == 2) return retry > 3 ? 25 : 0;
    if (state == 3 && retry == 2) return 26;
    return 27;
}

static int parse_product(const uint8_t *d, size_t n) {
    if (n < 4) return 30;
    uint8_t name_length = d[0];
    uint8_t category = d[1];
    uint16_t stock = read_u16(d + 2);
    if (name_length == 0) return 31;
    if (name_length > 31) return 32;
    if (category == 0) return 33;
    if (stock == 0) return 34;
    if (stock > 500) return 35;
    if (category == 1 && name_length == 31) return 36;
    return 0;
}

static int parse_user(const uint8_t *d, size_t n) {
    if (n < 3) return 40;
    uint8_t role = d[0];
    uint8_t length = d[1];
    uint8_t flags = d[2];
    if (length == 0) return 41;
    if (length > 32) return 42;
    if (role > 2) return 43;
    if (role == 0) return (flags & 1u) ? 44 : 0;
    if (role == 1 && length == 1) return 45;
    if (flags == 0x80 && length == 32) return 46;
    return 0;
}

static int parse_health(const uint8_t *d, size_t n) {
    if (n < 2) return 50;
    if (d[0] == 0x7f) return d[1] == 0x5a ? 51 : 52;
    return 0;
}

int fuzz_target(const uint8_t *data, size_t size) {
    if (size < 2) return 0;
    switch (data[0] % 5u) {
        case 0: return parse_order(data + 1, size - 1);
        case 1: return parse_payment(data + 1, size - 1);
        case 2: return parse_product(data + 1, size - 1);
        case 3: return parse_user(data + 1, size - 1);
        default: return parse_health(data + 1, size - 1);
    }
}

typedef struct {
    uint8_t length;
    uint8_t data[24];
} Seed;

static const Seed SEEDS[] = {
    {8, {0, 0, 5, 0, 0x10, 0x27, 0, 0}},          /* order, normal path */
    {8, {0, 1, 12, 0, 0x88, 0x13, 0, 0}},         /* order, quantity > 10 */
    {8, {0, 2, 100, 0, 0x60, 0xea, 0, 0}},        /* order, total > 5m */
    {7, {1, 1, 0x10, 0x27, 0, 0, 1}},             /* payment, success */
    {7, {1, 2, 0x10, 0x27, 0, 0, 4}},             /* payment, retry branch */
    {5, {2, 31, 1, 10, 0}},                       /* product, long name */
    {4, {2, 0, 1, 1, 0}},                         /* product, empty name */
    {4, {3, 0, 5, 1}},                            /* user, normal path */
    {4, {3, 1, 1, 0}},                            /* user, one-char name */
    {3, {4, 0x7f, 0x00}},                         /* health, wrong magic */
};

size_t fixture_seed_count(void) {
    return sizeof(SEEDS) / sizeof(SEEDS[0]);
}

const uint8_t *fixture_seed(size_t index, size_t *length) {
    if (index >= fixture_seed_count()) return NULL;
    if (length) *length = SEEDS[index].length;
    return SEEDS[index].data;
}
