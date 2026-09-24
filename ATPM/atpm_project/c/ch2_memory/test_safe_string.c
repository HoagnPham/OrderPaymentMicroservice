/*
 * test_safe_string.c -- Test don gian (khong dung framework ngoai) cho
 * safe_string.h, bao gom cac edge case: chuoi rong, NULL, chuoi dai hon buffer.
 */
#include "safe_string.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <assert.h>

static int failures = 0;

#define CHECK(cond, msg) do { \
    if (!(cond)) { printf("[FAIL] %s\n", msg); failures++; } \
    else { printf("[ OK ] %s\n", msg); } \
} while (0)

void test_ss_copy_normal(void) {
    char buf[10];
    size_t len = ss_copy(buf, sizeof(buf), "hello");
    CHECK(strcmp(buf, "hello") == 0, "ss_copy: copy binh thuong");
    CHECK(len == 5, "ss_copy: tra ve dung do dai nguon");
}

void test_ss_copy_truncate(void) {
    char buf[5];  /* chi chua 4 ky tu + null */
    size_t len = ss_copy(buf, sizeof(buf), "hello world");
    CHECK(strcmp(buf, "hell") == 0, "ss_copy: cat bot dung cho khi qua dai");
    CHECK(buf[4] == '\0', "ss_copy: van null-terminate khi bi cat");
    CHECK(len == strlen("hello world"), "ss_copy: tra ve do dai goc de phat hien truncation");
}

void test_ss_copy_empty_src(void) {
    char buf[10] = "xxxxxxxxx";
    size_t len = ss_copy(buf, sizeof(buf), "");
    CHECK(strcmp(buf, "") == 0, "ss_copy: chuoi rong");
    CHECK(len == 0, "ss_copy: do dai 0 cho chuoi rong");
}

void test_ss_copy_null_src(void) {
    char buf[10] = "xxxxxxxxx";
    size_t len = ss_copy(buf, sizeof(buf), NULL);
    CHECK(buf[0] == '\0', "ss_copy: NULL src khong crash, dst rong");
    CHECK(len == 0, "ss_copy: NULL src tra ve 0");
}

void test_ss_copy_zero_size(void) {
    char buf[1] = {'z'};
    size_t len = ss_copy(buf, 0, "abc");
    CHECK(len == 0, "ss_copy: dst_size=0 khong ghi gi, tra ve 0");
}

void test_ss_concat_normal(void) {
    char buf[20] = "Hello, ";
    ss_concat(buf, sizeof(buf), "world!");
    CHECK(strcmp(buf, "Hello, world!") == 0, "ss_concat: noi chuoi binh thuong");
}

void test_ss_concat_truncate(void) {
    char buf[10] = "Hello";
    ss_concat(buf, sizeof(buf), ", world!");
    CHECK(strlen(buf) == 9, "ss_concat: cat bot khi vuot qua buffer");
    CHECK(buf[9] == '\0', "ss_concat: van null-terminate");
}

void test_ss_alloc_checked_normal(void) {
    int *arr = ss_alloc_checked(10, sizeof(int));
    CHECK(arr != NULL, "ss_alloc_checked: cap phat binh thuong thanh cong");
    if (arr) free(arr);
}

void test_ss_alloc_checked_overflow(void) {
    void *p = ss_alloc_checked((size_t)-1, 2);  /* chac chan tran */
    CHECK(p == NULL, "ss_alloc_checked: phat hien overflow, tra ve NULL");
}

int main(void) {
    test_ss_copy_normal();
    test_ss_copy_truncate();
    test_ss_copy_empty_src();
    test_ss_copy_null_src();
    test_ss_copy_zero_size();
    test_ss_concat_normal();
    test_ss_concat_truncate();
    test_ss_alloc_checked_normal();
    test_ss_alloc_checked_overflow();

    printf("\n%d failure(s).\n", failures);
    return failures == 0 ? 0 : 1;
}
