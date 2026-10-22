#ifndef COV_RUNTIME_H
#define COV_RUNTIME_H
#include <stddef.h>
#include <stdint.h>

#define COV_MAP_SIZE 65536u

void cov_init(void);
void cov_reset(void);
size_t cov_unique_edges(void);
size_t cov_count_global(const uint8_t *global_map);
size_t cov_merge_new(uint8_t *global_map);

#endif
