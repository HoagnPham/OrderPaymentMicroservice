/*
 * Lab 3 - Module Inventory (Quan ly kho sach thu vien) - PHIEN BAN CO LOI
 * =========================================================================
 * Chuong 2: 2.1 Danh gia rui ro, 2.3 Lap trinh C an toan (Quan ly bo nho)
 *
 * File nay CHU Y chua 3 loai loi bo nho pho bien (CWE) MOT CACH CO CHU Y
 * de phuc vu muc dich giao duc (phat hien bang AddressSanitizer):
 *   (A) CWE-121 Stack-based Buffer Overflow  - ham add_book_title_UNSAFE()
 *   (B) CWE-416 Use After Free               - ham demo_use_after_free()
 *   (C) CWE-401 Memory Leak                  - ham demo_memory_leak()
 *
 * KHONG dung file nay trong san pham that. Muc dich: doi chieu voi
 * secure_inventory.c (ban da sua) va cong cu risk_assessor.py (Lab3-Python).
 *
 * Bien dich VOI AddressSanitizer de phat hien loi runtime:
 *   gcc -g -fsanitize=address -o vulnerable_inventory vulnerable_inventory.c
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define TITLE_MAX_HEAP 32

typedef struct {
    char title[32];      /* co dinh 32 byte -> de gay tran neu khong kiem tra */
    char author[32];
    int  year;
    int  copies_available;
} Book;

/* (A) CWE-121: strcpy KHONG kiem tra do dai nguon -> tran bo dem stack neu
 * 'title' dai hon 31 ky tu (+ '\0'). Day la loi rat pho bien trong C. */
void add_book_title_UNSAFE(Book *b, const char *title) {
    strcpy(b->title, title);   /* !!! KHONG BAO GIO dung strcpy voi input khong tin cay !!! */
}

/* (A-2) Bien the CAP PHAT DONG (heap) cua cung loi tren, dung de
 * AddressSanitizer PHAT HIEN RO RANG hon: khi 'title' nam trong mot khoi
 * cap phat rieng (khong chung struct voi field khac), ASan se chen "redzone"
 * (vung dem bao ve) ngay sau khoi, nen ghi vuot qua se bi bat ngay lap tuc.
 * (Ghi chu su pham: tran trong 1 truong cua struct - nhu ham tren - co the
 * chi de ghi de len field ke ben ma KHONG bi ASan bat, vi van nam trong
 * cung mot vung cap phat -> day la ly do vi sao "chay khong bao loi" KHONG
 * co nghia la "an toan"!) */
char *add_book_title_UNSAFE_HEAP(const char *title) {
    char *buf = (char *)malloc(TITLE_MAX_HEAP);
    if (!buf) return NULL;
    strcpy(buf, title);        /* !!! tran heap neu strlen(title) >= TITLE_MAX_HEAP !!! */
    return buf;
}


/* (B) CWE-416 Use-After-Free: giai phong bo nho roi van tiep tuc doc/ghi. */
void demo_use_after_free(void) {
    Book *b = (Book *)malloc(sizeof(Book));
    if (!b) return;
    strcpy(b->title, "Clean Code");
    b->year = 2008;
    free(b);
    /* LOI: truy cap b sau khi da free -> hanh vi khong xac dinh (UB) */
    printf("  [UAF] (khong an toan) nam xuat ban sau khi free: %d\n", b->year);
}

/* (C) CWE-401 Memory Leak: cap phat nhung khong bao gio giai phong. */
void demo_memory_leak(int count) {
    for (int i = 0; i < count; i++) {
        Book *b = (Book *)malloc(sizeof(Book));
        if (!b) continue;
        b->year = 2000 + i;
        /* LOI: thieu free(b); -> ro ri bo nho moi lan goi ham */
    }
}

int main(int argc, char *argv[]) {
    Book b;
    memset(&b, 0, sizeof(b));

    const char *mode = (argc > 1) ? argv[1] : "safe_input";
    const char *payload = (argc > 2) ? argv[2] : "Clean Architecture";

    if (strcmp(mode, "overflow") == 0) {
        printf("Demo Heap Buffer Overflow (CWE-121), payload dai %zu ky tu:\n", strlen(payload));
        char *title = add_book_title_UNSAFE_HEAP(payload);  /* ASan se bat neu payload qua dai */
        printf("  Tieu de (heap): %s\n", title);
        free(title);
        return 0;
    }
    if (strcmp(mode, "uaf") == 0) {
        printf("Demo Use-After-Free (CWE-416):\n");
        demo_use_after_free();
        return 0;
    }
    if (strcmp(mode, "leak") == 0) {
        printf("Demo Memory Leak (CWE-401):\n");
        demo_memory_leak(5);
        printf("  (da cap phat 5 Book, KHONG giai phong -> memory leak)\n");
        return 0;
    }

    /* mode == "safe_input" (mac dinh): chi minh hoa duong chay binh thuong,
     * KHONG kich hoat loi nao, dung de doi chieu voi phien ban secure. */
    add_book_title_UNSAFE(&b, payload);
    printf("Tieu de sach da luu (struct field, khong ASan-detectable neu chi tran nhe): %s\n", b.title);
    return 0;
}
