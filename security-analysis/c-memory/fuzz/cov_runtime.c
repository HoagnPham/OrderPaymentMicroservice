#define _GNU_SOURCE
#include "cov_runtime.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>

static volatile uint8_t *coverage_map;
static uintptr_t previous_pc;

void cov_init(void) {
    coverage_map = mmap(NULL, COV_MAP_SIZE, PROT_READ | PROT_WRITE,
                        MAP_SHARED | MAP_ANONYMOUS, -1, 0);
    if (coverage_map == MAP_FAILED) {
        perror("mmap coverage map");
        exit(2);
    }
    memset((void *)coverage_map, 0, COV_MAP_SIZE);
    previous_pc = 0;
}

void cov_reset(void) {
    if (!coverage_map) return;
    memset((void *)coverage_map, 0, COV_MAP_SIZE);
    previous_pc = 0;
}

void __sanitizer_cov_trace_pc(void) {
    if (!coverage_map) return;
    uintptr_t pc = (uintptr_t)__builtin_return_address(0);
    uint32_t current = (uint32_t)((pc >> 4) ^ (pc << 8));
    uint32_t index = (current ^ (uint32_t)(previous_pc >> 4)) & (COV_MAP_SIZE - 1);
    coverage_map[index] = 1;
    previous_pc = pc;
}

size_t cov_unique_edges(void) {
    size_t count = 0;
    for (size_t i = 0; i < COV_MAP_SIZE; ++i) {
        if (coverage_map[i]) ++count;
    }
    return count;
}

size_t cov_count_global(const uint8_t *global_map) {
    size_t count = 0;
    for (size_t i = 0; i < COV_MAP_SIZE; ++i) {
        if (global_map[i]) ++count;
    }
    return count;
}

size_t cov_merge_new(uint8_t *global_map) {
    size_t new_edges = 0;
    for (size_t i = 0; i < COV_MAP_SIZE; ++i) {
        if (coverage_map[i] && !global_map[i]) {
            global_map[i] = 1;
            ++new_edges;
        }
    }
    return new_edges;
}
