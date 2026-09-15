/*
 * Lab 7 - Module Order Processing (Xu ly Don hang)
 * ====================================================
 * Chuong 4: 4.1 Gioi thieu kiem thu bao mat, 4.2 Phu ma nguon (code coverage),
 *           4.3 Theo doi thuc thi chuong trinh, 4.4 Tao duong thuc thi chuong trinh
 *
 * Ham can phan tich dong: process_order()
 *   Dac ta don gian hoa:
 *     - So luong (quantity) phai > 0 va <= 100, neu khong -> ORDER_INVALID_QTY.
 *     - Neu is_member (thanh vien) VA quantity >= 10 -> giam gia 15%.
 *     - Neu is_member (khong du dieu kien tren) -> giam gia 5%.
 *     - Neu KHONG la thanh vien VA quantity >= 20 -> giam gia 10% (khuyen
 *       mai mua si cho khach vang lai).
 *     - Neu unit_price <= 0 -> ORDER_INVALID_PRICE.
 *     - Tong tien = quantity * unit_price * (1 - discount).
 *     - Neu tong tien (sau giam gia) > 10,000,000 (VND) -> yeu cau
 *       APPROVAL_REQUIRED (canh bao gian lan/don hang bat thuong).
 *
 * Ham nay CO NHIEU NHANH LONG NHAU, la vi du dien hinh de minh hoa code
 * coverage: mot bo test co the PASS toan bo nhung khong phu het cac to
 * hop nhanh (branch combination) - dan den lo hong kiem thu.
 *
 * Bien dich co --coverage de sinh du lieu cho gcov:
 *   gcc --coverage -DORDER_CLI_MAIN -o order_processor order_processor.c
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef enum {
    ORDER_OK = 0,
    ORDER_INVALID_QTY = 1,
    ORDER_INVALID_PRICE = 2,
    ORDER_APPROVAL_REQUIRED = 3
} OrderStatus;

typedef struct {
    int quantity;
    double unit_price;
    int is_member;
    double discount_applied;
    double total_amount;
} OrderResult;

#define APPROVAL_THRESHOLD 10000000.0

OrderStatus process_order(int quantity, double unit_price, int is_member, OrderResult *out) {
    if (out == NULL) return ORDER_INVALID_QTY; /* phong thu (defensive) */

    if (quantity <= 0 || quantity > 100) {
        return ORDER_INVALID_QTY;
    }
    if (unit_price <= 0.0) {
        return ORDER_INVALID_PRICE;
    }

    double discount = 0.0;
    if (is_member) {
        if (quantity >= 10) {
            discount = 0.15;
        } else {
            discount = 0.05;
        }
    } else {
        if (quantity >= 20) {
            discount = 0.10;
        }
        /* else: discount = 0.0 (khach vang lai, mua le) */
    }

    double total = quantity * unit_price * (1.0 - discount);

    out->quantity = quantity;
    out->unit_price = unit_price;
    out->is_member = is_member;
    out->discount_applied = discount;
    out->total_amount = total;

    if (total > APPROVAL_THRESHOLD) {
        return ORDER_APPROVAL_REQUIRED;
    }
    return ORDER_OK;
}

const char *order_status_to_string(OrderStatus s) {
    switch (s) {
        case ORDER_OK: return "OK";
        case ORDER_INVALID_QTY: return "INVALID_QTY";
        case ORDER_INVALID_PRICE: return "INVALID_PRICE";
        case ORDER_APPROVAL_REQUIRED: return "APPROVAL_REQUIRED";
        default: return "UNKNOWN";
    }
}

#ifdef ORDER_CLI_MAIN
/* CLI doc file CSV dinh dang: quantity,unit_price,is_member,expected_status
 * Chay tung dong nhu mot test case, in ket qua PASS/FAIL. */
int main(int argc, char *argv[]) {
    if (argc < 2) {
        fprintf(stderr, "Cach dung: %s <test_inputs.csv>\n", argv[0]);
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

        char *tok = strtok(line, ",");
        if (!tok) continue;
        int quantity = atoi(tok);
        tok = strtok(NULL, ",");
        if (!tok) continue;
        double unit_price = atof(tok);
        tok = strtok(NULL, ",");
        if (!tok) continue;
        int is_member = atoi(tok);
        tok = strtok(NULL, ",");
        if (!tok) continue;
        char expected[32];
        snprintf(expected, sizeof(expected), "%s", tok);

        OrderResult result;
        memset(&result, 0, sizeof(result));
        OrderStatus status = process_order(quantity, unit_price, is_member, &result);
        const char *actual = order_status_to_string(status);

        total++;
        int ok = (strcmp(actual, expected) == 0);
        if (ok) passed++;
        printf("[%s] qty=%-4d price=%-10.1f member=%d expected=%-18s actual=%-18s "
               "discount=%.2f total=%.1f\n",
               ok ? "PASS" : "FAIL", quantity, unit_price, is_member,
               expected, actual, result.discount_applied, result.total_amount);
    }
    fclose(fp);
    printf("\nTong: %d/%d test PASS\n", passed, total);
    return (passed == total) ? 0 : 2;
}
#endif
