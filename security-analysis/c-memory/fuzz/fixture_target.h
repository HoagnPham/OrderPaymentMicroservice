#ifndef FIXTURE_TARGET_H
#define FIXTURE_TARGET_H
#include <stddef.h>
#include <stdint.h>

int fuzz_target(const uint8_t *data, size_t size);
size_t fixture_seed_count(void);
const uint8_t *fixture_seed(size_t index, size_t *length);

#endif
