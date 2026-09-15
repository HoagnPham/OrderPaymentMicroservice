/*
 * Lab 2 - PIN Validator (module AUTH cua he thong SecureSys)
 * ===========================================================
 * Chuong: 1.3 & 1.4 - Kiem thu va kiem chung phan mem (Software Testing
 * and Verification - Part I & II).
 *
 * Ham can kiem thu: validate_pin()
 *   Dac ta:
 *     - PIN hop le neu: do dai dung 6 ky tu, TAT CA la chu so ('0'-'9'),
 *       va KHONG duoc la day so lien tiep tang/giam (vi du "123456",
 *       "654321") vi day la PIN yeu (weak PIN policy).
 *     - Tai khoan bi khoa (LOCKED) sau 3 lan nhap sai lien tiep.
 *
 * Ham nay co nhieu nhanh (branch) => phu hop lam vi du cho ca kiem thu
 * hop den (black-box, dua tren dac ta) va kiem thu hop trang (white-box,
 * dua tren cau truc code / branch coverage o Lab 7).
 *
 * Bien dich thanh thu vien dung chung (de Python goi qua ctypes) VA thanh
 * chuong trinh CLI doc lap:
 *   gcc -shared -fPIC -o libpinvalidator.so pin_validator.c   (cho ctypes)
 *   gcc -o pin_validator_cli pin_validator.c -DPIN_CLI_MAIN   (CLI + test)
 */

#define _POSIX_C_SOURCE 200809L
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define PIN_LEN 6

typedef enum {
    PIN_OK = 0,
    PIN_ERR_WRONG_LENGTH = 1,
    PIN_ERR_NON_DIGIT = 2,
    PIN_ERR_SEQUENTIAL = 3,
    PIN_ERR_ACCOUNT_LOCKED = 4,
    PIN_ERR_NULL_INPUT = 5
} PinResult;

/* Kiem tra day so co phai la day tang/giam lien tiep hay khong.
 * Vi du: "123456" (tang), "654321" (giam) -> tra ve 1 (yeu). */
static int is_sequential(const char *pin) {
    int ascending = 1, descending = 1;
    for (int i = 0; i < PIN_LEN - 1; i++) {
        if (pin[i + 1] - pin[i] != 1) ascending = 0;
        if (pin[i] - pin[i + 1] != 1) descending = 0;
    }
    return ascending || descending;
}

/* Ham chinh can kiem thu. Tra ve ma PinResult.
 * failed_attempts: so lan nhap sai lien tiep TRUOC do (dau vao),
 *                  dung de mo phong chinh sach khoa tai khoan. */
PinResult validate_pin(const char *pin, int failed_attempts) {
    if (pin == NULL) {
        return PIN_ERR_NULL_INPUT;
    }
    if (failed_attempts >= 3) {
        return PIN_ERR_ACCOUNT_LOCKED;
    }
    size_t len = strnlen(pin, PIN_LEN + 1);
    if (len != PIN_LEN) {
        return PIN_ERR_WRONG_LENGTH;
    }
    for (int i = 0; i < PIN_LEN; i++) {
        if (pin[i] < '0' || pin[i] > '9') {
            return PIN_ERR_NON_DIGIT;
        }
    }
    if (is_sequential(pin)) {
        return PIN_ERR_SEQUENTIAL;
    }
    return PIN_OK;
}

const char *pin_result_to_string(PinResult r) {
    switch (r) {
        case PIN_OK: return "OK";
        case PIN_ERR_WRONG_LENGTH: return "WRONG_LENGTH";
        case PIN_ERR_NON_DIGIT: return "NON_DIGIT";
        case PIN_ERR_SEQUENTIAL: return "SEQUENTIAL_WEAK";
        case PIN_ERR_ACCOUNT_LOCKED: return "ACCOUNT_LOCKED";
        case PIN_ERR_NULL_INPUT: return "NULL_INPUT";
        default: return "UNKNOWN";
    }
}

#ifdef PIN_CLI_MAIN
/* CLI: doc file CSV test case (pin,failed_attempts,expected) va cham diem.
 * Dinh dang moi dong: pin,failed_attempts,expected_result_name */
int main(int argc, char *argv[]) {
    if (argc < 2) {
        fprintf(stderr, "Cach dung: %s <test_cases.csv>\n", argv[0]);
        return 1;
    }
    FILE *fp = fopen(argv[1], "r");
    if (!fp) {
        fprintf(stderr, "[LOI] Khong mo duoc file: %s\n", argv[1]);
        return 1;
    }
    char line[256];
    int total = 0, passed = 0;
    fgets(line, sizeof(line), fp); /* bo qua header */
    while (fgets(line, sizeof(line), fp)) {
        line[strcspn(line, "\r\n")] = '\0';
        if (line[0] == '\0') continue;
        char pin[32] = {0};
        int failed = 0;
        char expected[32] = {0};

        /* Dinh dang CSV dung dau ',' lam phan cach; truong pin co the rong
         * (truong hop test bien "chuoi rong"), duoc bieu dien boi nhan
         * "EMPTYSTR" de tranh nhap nhang khi strtok bo qua cac dau phan
         * cach lien tiep. */
        char *tok = strtok(line, ",");
        if (!tok) continue;
        if (strcmp(tok, "EMPTYSTR") == 0) {
            pin[0] = '\0';
        } else {
            snprintf(pin, sizeof(pin), "%s", tok);
        }
        tok = strtok(NULL, ",");
        if (!tok) continue;
        failed = atoi(tok);
        tok = strtok(NULL, ",");
        if (!tok) continue;
        snprintf(expected, sizeof(expected), "%s", tok);

        const char *pin_arg = (strcmp(pin, "NULL") == 0) ? NULL : pin;
        PinResult r = validate_pin(pin_arg, failed);
        const char *actual = pin_result_to_string(r);

        total++;
        int ok = (strcmp(actual, expected) == 0);
        if (ok) passed++;
        printf("[%s] pin=%-8s failed=%d expected=%-16s actual=%-16s\n",
               ok ? "PASS" : "FAIL",
               pin_arg ? pin_arg : "NULL", failed, expected, actual);
    }
    fclose(fp);
    printf("\nTong: %d/%d test PASS\n", passed, total);
    return (passed == total) ? 0 : 2;
}
#endif
