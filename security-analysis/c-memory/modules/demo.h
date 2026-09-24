#ifndef DEMO_H
#define DEMO_H
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
/* Allocation failure is a separate failure, never a successful demonstration. */
static inline void *allocate(size_t bytes) {
    void *p = malloc(bytes);
    if (!p) { fputs("allocation failed\n", stderr); exit(2); }
    return p;
}
#endif
