/*
 * bmc_demo.c -- Cac ham dung cho Chuong 3, muc 3.5 (Bounded Model Checking)
 * va lam target cho wrapper atpm.ch3_static_analysis.run_cbmc().
 */
#include <assert.h>
#include <stdlib.h>

/* ---- Vi du 1: vong lap co gioi han ro rang -> BMC sound voi unwind du ---- */
int sum_array(int *arr, int n) {
    int total = 0;
    for (int i = 0; i < n; i++) {
        total += arr[i];
    }
    return total;
}

void check_sum(void) {
    int arr[5] = {1, 2, 3, 4, 5};
    int result = sum_array(arr, 5);
    assert(result == 15);
}

/* ---- Vi du 2: off-by-one gay tran mang (phat hien bang --bounds-check) ---- */
void write_n(int *buffer, int n) {
    for (int i = 0; i <= n; i++) {   /* BUG: <=  thay vi < */
        buffer[i] = i;
    }
}

void check_overflow(void) {
    int buffer[10];
    write_n(buffer, 10);
}

/* ---- Vi du 3: phien ban DA SUA cua off-by-one ---- */
void write_n_fixed(int *buffer, int n) {
    for (int i = 0; i < n; i++) {
        buffer[i] = i;
    }
}

void check_overflow_fixed(void) {
    int buffer[10];
    write_n_fixed(buffer, 10);
}

/* ---- Vi du 4: chia cho 0 (dung --div-by-zero-check) ---- */
int safe_divide(int a, int b) {
    return a / b;
}

int nondet_int(void);  /* CBMC built-in: gia tri "bat ky" */

void check_divide(void) {
    int x = nondet_int();
    int r = safe_divide(10, x);
    (void)r;
}

void check_divide_guarded(void) {
    int x = nondet_int();
    __CPROVER_assume(x != 0);
    int r = safe_divide(10, x);
    (void)r;
}

/* ---- Vi du 5: heap - malloc/free hop le ---- */
void check_heap(void) {
    int *p = malloc(sizeof(int) * 5);
    if (p != NULL) {
        p[4] = 100;
        free(p);
    }
}
