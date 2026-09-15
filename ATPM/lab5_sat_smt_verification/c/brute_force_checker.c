/*
 * Lab 5 - Kiem chung cheo bang Liet ke Vet can (Brute-force Enumeration)
 * ==========================================================================
 * Chuong 3: 3.4 SAT and SMT solving
 *
 * Voi cac dieu kien CHI gom bien boolean (mien gia tri huu han nho), ta co
 * the kiem chung tinh tuong duong bang cach LIET KE TAT CA to hop gia tri
 * co the (2^n voi n bien) va so sanh ket qua P va Q tren tung to hop. Day
 * la phuong phap "brute-force" doi lap voi SMT solving (dung thuat toan
 * DPLL/CDCL) - minh hoa ro rang tai sao SMT can thiet: voi n lon (vi du
 * n = 50 bien boolean), 2^50 to hop la BAT KHA THI de liet ke, trong khi
 * SMT solver co the giai quyet trong thoi gian hop ly nho cat tia thong
 * minh (khong can duyet toan bo khong gian).
 *
 * File nay CHI xu ly 2 dieu kien boolean thuan tuy tu dataset (COND-01,
 * COND-03, COND-04, COND-05 - moi dieu kien duoc "hard-code" thanh 1 ham
 * rieng vi C khong co eval() bieu thuc dong nhu Python). Muc tieu su pham:
 * doi chieu ket qua "thu cong" (C) voi ket qua "SMT" (Python/Z3).
 *
 * Bien dich: gcc -Wall -Wextra -std=c11 -o brute_force_checker brute_force_checker.c
 */

#include <stdio.h>
#include <stdbool.h>

/* ---- COND-01: dieu kien mo khoa cua (3 bien boolean) ---- */
static bool cond01_original(bool authenticated, bool unlocked_requested, bool alarm_active) {
    return authenticated && unlocked_requested && !alarm_active;
}
static bool cond01_optimized(bool authenticated, bool unlocked_requested, bool alarm_active) {
    return !(!authenticated || !unlocked_requested) && !alarm_active;
}

/* ---- COND-03: dieu kien PIN hop le (3 bien boolean) ---- */
static bool cond03_original(bool len_ok, bool all_digits, bool not_sequential) {
    return len_ok && all_digits && not_sequential;
}
static bool cond03_optimized(bool len_ok, bool all_digits, bool not_sequential) {
    return all_digits && (len_ok && not_sequential);
}

/* ---- COND-04: dieu kien canh bao tamper -> alarm (2 bien boolean) ---- */
static bool cond04_original(bool tamper, bool alarm) {
    return (!tamper) || alarm;   /* a -> b == !a || b */
}
static bool cond04_optimized(bool tamper, bool alarm) {
    return (!tamper) || (alarm && tamper);
}

/* ---- COND-05: dieu kien bypass xac thuc (3 bien boolean) ---- */
static bool cond05_original(bool authenticated, bool is_admin, bool bypass_flag) {
    (void)is_admin; (void)bypass_flag;
    return authenticated;
}
static bool cond05_optimized(bool authenticated, bool is_admin, bool bypass_flag) {
    return authenticated || (is_admin && bypass_flag);
}

/* Ham tien ich: liet ke vet can 3 bien boolean, dem so to hop KHAC nhau
 * giua ham original va optimized, in ra phan vi du dau tien neu co. */
static int enumerate3(const char *name,
                       bool (*orig)(bool, bool, bool),
                       bool (*opt)(bool, bool, bool)) {
    int mismatches = 0;
    for (int a = 0; a < 2; a++) {
        for (int b = 0; b < 2; b++) {
            for (int c = 0; c < 2; c++) {
                bool ra = orig((bool)a, (bool)b, (bool)c);
                bool rb = opt((bool)a, (bool)b, (bool)c);
                if (ra != rb) {
                    if (mismatches == 0) {
                        printf("  [%s] KHONG TUONG DUONG - phan vi du dau tien: "
                               "(%d,%d,%d) -> original=%d, optimized=%d\n",
                               name, a, b, c, ra, rb);
                    }
                    mismatches++;
                }
            }
        }
    }
    if (mismatches == 0) {
        printf("  [%s] TUONG DUONG tren toan bo 2^3 = 8 to hop\n", name);
    } else {
        printf("  [%s] -> %d/8 to hop cho ket qua khac nhau\n", name, mismatches);
    }
    return mismatches;
}

static int enumerate2(const char *name,
                       bool (*orig)(bool, bool),
                       bool (*opt)(bool, bool)) {
    int mismatches = 0;
    for (int a = 0; a < 2; a++) {
        for (int b = 0; b < 2; b++) {
            bool ra = orig((bool)a, (bool)b);
            bool rb = opt((bool)a, (bool)b);
            if (ra != rb) {
                printf("  [%s] KHONG TUONG DUONG tai (%d,%d) -> original=%d, optimized=%d\n",
                       name, a, b, ra, rb);
                mismatches++;
            }
        }
    }
    if (mismatches == 0) {
        printf("  [%s] TUONG DUONG tren toan bo 2^2 = 4 to hop\n", name);
    }
    return mismatches;
}

int main(void) {
    printf("=== Kiem chung cheo bang liet ke vet can (Brute-force) ===\n\n");

    printf("COND-01 (mo khoa cua):\n");
    enumerate3("COND-01", cond01_original, cond01_optimized);

    printf("\nCOND-03 (PIN hop le):\n");
    enumerate3("COND-03", cond03_original, cond03_optimized);

    printf("\nCOND-04 (tamper -> alarm):\n");
    enumerate2("COND-04", cond04_original, cond04_optimized);

    printf("\nCOND-05 (bypass xac thuc):\n");
    int mismatches05 = enumerate3("COND-05", cond05_original, cond05_optimized);

    printf("\n=== Doi chieu voi ket qua Python/Z3 (dataset/smt_verification_results.json) ===\n");
    printf("Ky vong: COND-01 tuong duong, COND-03 tuong duong, COND-04 tuong duong,\n");
    printf("         COND-05 KHONG tuong duong (co %d/8 to hop khac nhau - chinh la\n", mismatches05);
    printf("         'backdoor' bypass_flag && is_admin).\n");

    return 0;
}
