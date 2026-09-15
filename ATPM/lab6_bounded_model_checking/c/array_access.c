/*
 * Lab 6 - Cai dat C thuc te tuong ung cac chuong trinh trong dataset BMC
 * ==========================================================================
 * Chuong 3: 3.5-3.7. Doi chieu ket qua BMC (tinh, Python/Z3) voi hanh vi
 * runtime thuc te (dong, AddressSanitizer) - cung mot nguyen tac doi chieu
 * da ap dung giua Lab 1 (Kripke) va giua Lab 3/Lab 4.
 *
 * Bien dich: gcc -g -fsanitize=address -o array_access array_access.c
 * Su dung  : ./array_access <mode> [base_index]
 *            mode: safe_full_scan | offbyone | symbolic_unsafe | symbolic_safe
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define ARRAY_SIZE 10

typedef struct { int id; int available; } CatalogEntry;

static void fill_catalog(CatalogEntry catalog[ARRAY_SIZE]) {
    for (int i = 0; i < ARRAY_SIZE; i++) {
        catalog[i].id = 1000 + i;
        catalog[i].available = 1;
    }
}

/* BMC-01: duyet dung, an toan */
static void safe_full_scan(CatalogEntry catalog[ARRAY_SIZE]) {
    for (int i = 0; i < ARRAY_SIZE; i++) {
        printf("  catalog[%d].id = %d\n", i, catalog[i].id);
    }
}

/* BMC-02: loi off-by-one, dieu kien dung sai '<=' */
static void offbyone_scan(CatalogEntry catalog[ARRAY_SIZE]) {
    for (int i = 0; i <= ARRAY_SIZE; i++) {   /* !!! LOI: phai la i < ARRAY_SIZE !!! */
        printf("  catalog[%d].id = %d\n", i, catalog[i].id);  /* ASan bat tai i == 10 */
    }
}

/* BMC-03: base_index tu tham so dong lenh, KHONG kiem tra truoc vong lap noi bo */
static void symbolic_unsafe(CatalogEntry catalog[ARRAY_SIZE], int base_index) {
    for (int i = 0; i < 5; i++) {
        int index = base_index + i;    /* !!! khong kiem tra index < ARRAY_SIZE !!! */
        printf("  catalog[%d].id = %d\n", index, catalog[index].id);
    }
}

/* BMC-04: da sua - kiem tra truoc vong lap (tuong duong precondition_max_base) */
static void symbolic_safe(CatalogEntry catalog[ARRAY_SIZE], int base_index) {
    if (base_index < 0 || base_index > ARRAY_SIZE - 5) {
        fprintf(stderr, "[TU CHOI] base_index=%d khong hop le (phai trong [0, %d])\n",
                base_index, ARRAY_SIZE - 5);
        return;
    }
    for (int i = 0; i < 5; i++) {
        int index = base_index + i;
        printf("  catalog[%d].id = %d\n", index, catalog[index].id);
    }
}

int main(int argc, char *argv[]) {
    CatalogEntry catalog[ARRAY_SIZE];
    fill_catalog(catalog);

    const char *mode = (argc > 1) ? argv[1] : "safe_full_scan";
    int base_index = (argc > 2) ? atoi(argv[2]) : 9;  /* mac dinh gia tri gay loi cho demo */

    if (strcmp(mode, "safe_full_scan") == 0) {
        printf("=== safe_full_scan (tuong ung BMC-01) ===\n");
        safe_full_scan(catalog);
    } else if (strcmp(mode, "offbyone") == 0) {
        printf("=== offbyone_scan (tuong ung BMC-02) ===\n");
        offbyone_scan(catalog);
    } else if (strcmp(mode, "symbolic_unsafe") == 0) {
        printf("=== symbolic_unsafe base_index=%d (tuong ung BMC-03) ===\n", base_index);
        symbolic_unsafe(catalog, base_index);
    } else if (strcmp(mode, "symbolic_safe") == 0) {
        printf("=== symbolic_safe base_index=%d (tuong ung BMC-04) ===\n", base_index);
        symbolic_safe(catalog, base_index);
    } else {
        fprintf(stderr, "Mode khong hop le: %s\n", mode);
        return 1;
    }
    return 0;
}
