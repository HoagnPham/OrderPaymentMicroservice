/*
 * vulnerable.c -- Cac vi du LOI BAO MAT co chu dich, dung de:
 *   1) Minh hoa trong slide bai giang Chuong 2
 *   2) Lam target cho Cppcheck / Clang Static Analyzer / Valgrind / ASan (2.5)
 *   3) Lam target cho phan tich tinh hinh thuc (Chuong 3) va fuzzing (Chuong 4)
 *
 * CANH BAO: file nay CHU DICH chua loi, KHONG dung lam mau code production.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* ---- 2.3: Buffer overflow do dung strcpy khong kiem tra do dai ---- */
void bug_buffer_overflow(const char *user_input) {
    char buffer[64];
    strcpy(buffer, user_input);   /* CWE-120 */
    printf("buffer = %s\n", buffer);
}

/* ---- 2.3: Format string vulnerability ---- */
void bug_format_string(const char *user_input) {
    printf(user_input);           /* CWE-134 */
}

/* ---- 2.3: Integer overflow dan toi cap phat sai kich thuoc ---- */
int *bug_integer_overflow_alloc(unsigned int count) {
    /* Neu count rat lon, count * sizeof(int) co the wrap-around */
    int *buf = malloc(count * sizeof(int));   /* CWE-190 -> CWE-122 */
    return buf;
}

/* ---- 2.2: Use-After-Free ---- */
void bug_use_after_free(void) {
    int *p = malloc(sizeof(int));
    *p = 42;
    free(p);
    *p = 43;                      /* CWE-416 */
    printf("p = %d\n", *p);
}

/* ---- 2.2: Double Free ---- */
void bug_double_free(void) {
    int *p = malloc(sizeof(int));
    free(p);
    free(p);                      /* CWE-415 */
}

/* ---- 2.2: Memory Leak ---- */
void bug_memory_leak(void) {
    int *p = malloc(100 * sizeof(int));
    p = malloc(200 * sizeof(int)); /* CWE-401 - mat con tro vung dau */
    free(p);
}

/* ---- 2.2: NULL Pointer Dereference ---- */
void bug_null_deref(void) {
    int *p = NULL;
    if (rand() % 2 == 0) {
        p = malloc(sizeof(int));
    }
    *p = 1;                       /* CWE-476 - p co the la NULL */
    free(p);
}

/* ---- 2.4: Padding "ro ri" du lieu chua khoi tao khi memcpy toan bo struct ---- */
struct PaddedRecord {
    char  flag;   /* 1 byte  */
    int   value;  /* 4 byte, co the co 3 byte padding truoc no */
};

void bug_padding_leak(char *out_buffer) {
    struct PaddedRecord rec;
    rec.flag = 1;
    rec.value = 100;
    /* BUG: khong memset(&rec,0,sizeof(rec)) truoc -> padding chua "rac" */
    memcpy(out_buffer, &rec, sizeof(rec));  /* CWE-457 / CWE-200 */
}

int main(void) {
    printf("Day la file minh hoa loi - KHONG chay truc tiep cac ham bug_* "
           "trong production.\n");
    return 0;
}
