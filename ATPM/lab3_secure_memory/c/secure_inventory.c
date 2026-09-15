/*
 * Lab 3 - Module Inventory (Quan ly kho sach thu vien) - PHIEN BAN AN TOAN
 * ==========================================================================
 * Ban sua loi tuong ung vulnerable_inventory.c, ap dung cac nguyen tac
 * lap trinh C an toan (Chuong 2 - 2.3):
 *   - Dung strncpy/snprintf thay strcpy, LUON dam bao null-terminator.
 *   - Dat con tro ve NULL ngay sau free() (tranh dangling pointer).
 *   - Giai phong moi vung nho da cap phat (khong con memory leak).
 *   - Kiem tra gia tri tra ve cua malloc/tham so dau vao.
 *
 * Bien dich VOI AddressSanitizer de xac nhan KHONG con loi:
 *   gcc -g -fsanitize=address -o secure_inventory secure_inventory.c
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define TITLE_MAX  32
#define AUTHOR_MAX 32
#define TITLE_MAX_HEAP 32

typedef struct {
    char title[TITLE_MAX];
    char author[AUTHOR_MAX];
    int  year;
    int  copies_available;
} Book;

/* (A) Sua CWE-121: dung snprintf, luon gioi han do dai va dam bao ket thuc
 * bang '\0'. Tra ve 1 neu tieu de bi cat bot (truncated) de goi cap tren
 * biet va co the canh bao nguoi dung. */
/* (A-2) Ban sua cua ham cap phat dong: dung malloc + snprintf, luon dam bao
 * null-terminator va khong bao gio ghi vuot qua vung da cap phat. */
char *add_book_title_SAFE_HEAP(const char *title) {
    char *buf = (char *)malloc(TITLE_MAX_HEAP);
    if (!buf) return NULL;
    snprintf(buf, TITLE_MAX_HEAP, "%s", title);
    return buf;
}

int add_book_title_SAFE(Book *b, const char *title) {
    if (b == NULL || title == NULL) {
        fprintf(stderr, "[LOI] Tham so NULL truyen vao add_book_title_SAFE\n");
        return -1;
    }
    int written = snprintf(b->title, TITLE_MAX, "%s", title);
    /* snprintf tra ve so ky tu SE duoc ghi neu du cho -> neu >= TITLE_MAX
     * nghia la chuoi goc da bi cat bot. */
    return (written >= TITLE_MAX) ? 1 : 0;
}

/* (B) Sua CWE-416: dat con tro ve NULL ngay sau free(); moi lan doc/ghi sau
 * do se bi phat hien ngay (NULL deref de debug) thay vi UB nguy hiem. */
void demo_use_after_free_FIXED(void) {
    Book *b = (Book *)malloc(sizeof(Book));
    if (!b) {
        fprintf(stderr, "[LOI] malloc that bai\n");
        return;
    }
    add_book_title_SAFE(b, "Clean Code");
    b->year = 2008;
    int year_copy = b->year;   /* sao chep du lieu can dung TRUOC khi free */
    free(b);
    b = NULL;                  /* tranh dangling pointer */
    printf("  [FIXED] nam xuat ban (da sao chep truoc khi free): %d\n", year_copy);
}

/* (C) Sua CWE-401: giai phong day du trong vong lap, khong con ro ri. */
void demo_memory_leak_FIXED(int count) {
    Book **books = (Book **)malloc(sizeof(Book *) * (size_t)count);
    if (!books) {
        fprintf(stderr, "[LOI] malloc mang con tro that bai\n");
        return;
    }
    for (int i = 0; i < count; i++) {
        books[i] = (Book *)malloc(sizeof(Book));
        if (!books[i]) continue;
        books[i]->year = 2000 + i;
    }
    for (int i = 0; i < count; i++) {
        free(books[i]);       /* giai phong tung phan tu */
        books[i] = NULL;
    }
    free(books);               /* giai phong mang con tro */
}

int main(int argc, char *argv[]) {
    Book b;
    memset(&b, 0, sizeof(b));

    const char *mode = (argc > 1) ? argv[1] : "safe_input";
    const char *payload = (argc > 2) ? argv[2] : "Clean Architecture";

    if (strcmp(mode, "overflow") == 0) {
        printf("Demo Heap Buffer Overflow (DA SUA), payload dai %zu ky tu:\n", strlen(payload));
        char *title = add_book_title_SAFE_HEAP(payload);
        printf("  Tieu de (heap, an toan, co the bi cat bot): %s\n", title);
        free(title);
        return 0;
    }
    if (strcmp(mode, "uaf") == 0) {
        printf("Demo Use-After-Free (DA SUA):\n");
        demo_use_after_free_FIXED();
        return 0;
    }
    if (strcmp(mode, "leak") == 0) {
        printf("Demo Memory Leak (DA SUA):\n");
        demo_memory_leak_FIXED(5);
        printf("  (da cap phat va giai phong day du 5 Book)\n");
        return 0;
    }

    int truncated = add_book_title_SAFE(&b, payload);
    printf("Tieu de sach da luu: %s%s\n", b.title, truncated ? " [DA BI CAT BOT]" : "");
    return 0;
}
