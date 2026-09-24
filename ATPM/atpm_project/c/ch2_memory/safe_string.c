#define _POSIX_C_SOURCE 200809L
#include "safe_string.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

size_t ss_copy(char *dst, size_t dst_size, const char *src) {
    if (dst == NULL || dst_size == 0 || src == NULL) {
        if (dst != NULL && dst_size > 0) dst[0] = '\0';
        return 0;
    }
    size_t src_len = strlen(src);
    size_t copy_len = (src_len >= dst_size) ? dst_size - 1 : src_len;
    memcpy(dst, src, copy_len);
    dst[copy_len] = '\0';
    return src_len;  /* nguoi goi so sanh voi dst_size de biet co bi cat khong */
}

size_t ss_concat(char *dst, size_t dst_size, const char *src) {
    if (dst == NULL || dst_size == 0 || src == NULL) {
        return 0;
    }
    size_t dst_len = strnlen(dst, dst_size);
    if (dst_len == dst_size) {
        /* dst khong co null-terminator trong pham vi dst_size - du lieu loi */
        return dst_size + strlen(src);
    }
    size_t remaining = dst_size - dst_len - 1;
    size_t src_len = strlen(src);
    size_t copy_len = (src_len > remaining) ? remaining : src_len;
    memcpy(dst + dst_len, src, copy_len);
    dst[dst_len + copy_len] = '\0';
    return dst_len + src_len;
}

void *ss_alloc_checked(size_t count, size_t elem_size) {
    if (elem_size != 0 && count > (size_t)-1 / elem_size) {
        fprintf(stderr, "[ss_alloc_checked] loi: count*elem_size se tran\n");
        return NULL;
    }
    void *p = calloc(count, elem_size);
    if (p == NULL) {
        fprintf(stderr, "[ss_alloc_checked] loi: calloc that bai (count=%zu, elem_size=%zu)\n",
                count, elem_size);
    }
    return p;
}
