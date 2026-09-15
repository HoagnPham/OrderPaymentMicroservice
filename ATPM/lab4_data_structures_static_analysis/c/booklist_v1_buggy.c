/*
 * Lab 4 - Cau truc du lieu dong: Danh sach lien ket (Linked List) Book Catalog
 * ==============================================================================
 * Chuong 2: 2.2 Cau truc dong, 2.5 Cong cu phan tich tinh tim loi bao mat
 *
 * Module Inventory (tiep noi Lab 3): quan ly catalog sach bang danh sach
 * lien ket don (singly linked list) cap phat dong.
 *
 * File nay (booklist_v1_buggy.c) CO CHU Y chua mot so loi tinh vi de cong
 * cu phan tich tinh (cppcheck) phat hien, phuc vu Lab 4-Python
 * (static_analysis_runner.py):
 *   - Memory leak khi xoa node (quen free node bi go bo)
 *   - NULL pointer dereference tiem an (khong kiem tra malloc / head NULL)
 *   - Doc bien chua khoi tao (uninitialized read) trong 1 nhanh hiem gap
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define TITLE_LEN 48

typedef struct BookNode {
    char title[TITLE_LEN];
    int  year;
    struct BookNode *next;
} BookNode;

/* Chen node moi vao dau danh sach */
BookNode *list_insert_front(BookNode *head, const char *title, int year) {
    BookNode *node = (BookNode *)malloc(sizeof(BookNode));
    /* LOI TINH VI #1: khong kiem tra node == NULL truoc khi ghi -> neu
     * malloc that bai, day la NULL pointer dereference (CWE-476). */
    snprintf(node->title, TITLE_LEN, "%s", title);
    node->year = year;
    node->next = head;
    return node;
}

/* Xoa node dau tien co tieu de trung khop */
BookNode *list_remove_by_title(BookNode *head, const char *title) {
    BookNode *cur = head, *prev = NULL;
    while (cur != NULL) {
        if (strcmp(cur->title, title) == 0) {
            if (prev == NULL) {
                head = cur->next;
            } else {
                prev->next = cur->next;
            }
            /* LOI TINH VI #2 (CWE-401 memory leak): thieu free(cur) truoc
             * khi thoat khoi ham -> node bi "mo coi", khong con con tro nao
             * tro toi, khong the giai phong -> ro ri bo nho. */
            return head;
        }
        prev = cur;
        cur = cur->next;
    }
    return head;
}

/* Dem so node trong danh sach, minh hoa loi doc bien chua khoi tao trong
 * nhanh hiem gap (list rong VA con tro dau vao khong duoc gan gia tri mac
 * dinh o noi goi ham - chi de minh hoa, khong luon kich hoat duoc). */
int list_count_UNSAFE(BookNode *head) {
    int count;              /* LOI TINH VI #3: thieu khoi tao count = 0 */
    BookNode *cur = head;
    while (cur != NULL) {
        count++;             /* neu list rong, vong lap khong chay -> tra ve
                                 gia tri rac (garbage) thay vi 0 */
        cur = cur->next;
    }
    if (head == NULL) {
        return count;         /* CWE-457: Use of Uninitialized Variable */
    }
    return count;
}

void list_print(BookNode *head) {
    for (BookNode *cur = head; cur != NULL; cur = cur->next) {
        printf("  - %s (%d)\n", cur->title, cur->year);
    }
}

void list_free_all(BookNode *head) {
    BookNode *cur = head;
    while (cur != NULL) {
        BookNode *next = cur->next;
        free(cur);
        cur = next;
    }
}

#ifndef BOOKLIST_NO_MAIN
int main(void) {
    BookNode *catalog = NULL;
    catalog = list_insert_front(catalog, "Clean Code", 2008);
    catalog = list_insert_front(catalog, "The Pragmatic Programmer", 1999);
    catalog = list_insert_front(catalog, "Refactoring", 1999);

    printf("Catalog hien tai:\n");
    list_print(catalog);

    printf("So luong sach (ham UNSAFE, danh sach khong rong nen the hien dung): %d\n",
           list_count_UNSAFE(catalog));

    catalog = list_remove_by_title(catalog, "The Pragmatic Programmer");
    printf("\nSau khi xoa 'The Pragmatic Programmer' (co memory leak an duoi):\n");
    list_print(catalog);

    list_free_all(catalog);
    return 0;
}
#endif
