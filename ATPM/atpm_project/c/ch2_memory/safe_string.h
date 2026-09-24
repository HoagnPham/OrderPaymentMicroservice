/*
 * safe_string.h -- Thu vien wrapper an toan quanh cac ham xu ly chuoi
 * chuan C, xay dung theo huong dan "Mini-project goi y chuong 2".
 */
#ifndef SAFE_STRING_H
#define SAFE_STRING_H

#include <stddef.h>

/* ss_copy: copy toi da (dst_size - 1) ky tu tu src sang dst, luon
 * null-terminate. Tra ve do dai THUC SU cua src (giong strlcpy) de
 * caller phat hien truong hop bi cat bot. */
size_t ss_copy(char *dst, size_t dst_size, const char *src);

/* ss_concat: noi src vao cuoi dst (toi da dst_size-1 ky tu), luon
 * null-terminate. Tra ve do dai mong muon cua chuoi ket qua. */
size_t ss_concat(char *dst, size_t dst_size, const char *src);

/* ss_alloc_checked: wrapper quanh calloc, in canh bao ra stderr va
 * tra ve NULL neu cap phat that bai (khong bao gio abort chuong trinh). */
void *ss_alloc_checked(size_t count, size_t elem_size);

#endif /* SAFE_STRING_H */
