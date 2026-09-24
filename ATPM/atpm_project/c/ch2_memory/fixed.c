/*
 * fixed.c -- Phien ban DA VA cua vulnerable.c, tuong ung tung ham.
 * Dung de doi chieu truoc/sau khi ap dung cac ky thuat trong 2.3.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

/* ---- Fix: buffer overflow -> dung snprintf voi kich thuoc buffer ---- */
void fixed_buffer_overflow(const char *user_input) {
    char buffer[64];
    snprintf(buffer, sizeof(buffer), "%s", user_input);
    printf("buffer = %s\n", buffer);
}

/* ---- Fix: format string -> luon truyen "%s" lam format co dinh ---- */
void fixed_format_string(const char *user_input) {
    printf("%s", user_input);
}

/* ---- Fix: integer overflow -> kiem tra truoc khi nhan, hoac dung calloc ---- */
int *fixed_integer_overflow_alloc(size_t count) {
    if (count != 0 && count > SIZE_MAX / sizeof(int)) {
        return NULL;  /* se tran neu nhan - tu choi cap phat */
    }
    return calloc(count, sizeof(int));  /* calloc: co kiem tra overflow noi bo */
}

/* ---- Fix: use-after-free -> gan NULL ngay sau free, khong dung lai ---- */
void fixed_use_after_free(void) {
    int *p = malloc(sizeof(int));
    if (!p) return;
    *p = 42;
    free(p);
    p = NULL;              /* tranh dangling pointer */
    /* KHONG con thao tac nao voi p sau day */
}

/* ---- Fix: double free -> gan NULL sau free, kiem tra truoc khi free lai ---- */
void fixed_double_free(void) {
    int *p = malloc(sizeof(int));
    if (!p) return;
    free(p);
    p = NULL;
    if (p != NULL) {       /* free(p) lan hai se khong bao gio chay */
        free(p);
    }
}

/* ---- Fix: memory leak -> free vung cu truoc khi gan lai con tro ---- */
void fixed_memory_leak(void) {
    int *p = malloc(100 * sizeof(int));
    if (!p) return;
    free(p);                         /* giai phong TRUOC khi gan lai */
    p = malloc(200 * sizeof(int));
    if (!p) return;
    free(p);
}

/* ---- Fix: null deref -> luon kiem tra ket qua malloc ---- */
void fixed_null_deref(void) {
    int *p = malloc(sizeof(int));
    if (p == NULL) {
        fprintf(stderr, "Cap phat that bai\n");
        return;
    }
    *p = 1;
    free(p);
}

/* ---- Fix: padding leak -> memset toan bo struct truoc khi dien du lieu ---- */
struct PaddedRecord {
    char  flag;
    int   value;
};

void fixed_padding_leak(char *out_buffer) {
    struct PaddedRecord rec;
    memset(&rec, 0, sizeof(rec));   /* xoa CA padding truoc */
    rec.flag = 1;
    rec.value = 100;
    memcpy(out_buffer, &rec, sizeof(rec));
}

int main(void) {
    fixed_buffer_overflow("test");
    fixed_format_string("100%% an toan\n");
    fixed_use_after_free();
    fixed_double_free();
    fixed_memory_leak();
    fixed_null_deref();
    printf("Tat ca ham fixed_* da chay khong loi bo nho.\n");
    return 0;
}
