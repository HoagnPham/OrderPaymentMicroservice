/*
 * Lab 4 - Book Catalog Linked List - PHIEN BAN DA SUA (v2_fixed)
 * =================================================================
 * Sua ca 3 loi tinh vi cua booklist_v1_buggy.c, doi chieu bang cppcheck.
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

BookNode *list_insert_front(BookNode *head, const char *title, int year) {
    BookNode *node = (BookNode *)malloc(sizeof(BookNode));
    if (node == NULL) {                       /* SUA loi #1 */
        fprintf(stderr, "[LOI] malloc that bai trong list_insert_front\n");
        return head;
    }
    snprintf(node->title, TITLE_LEN, "%s", title);
    node->year = year;
    node->next = head;
    return node;
}

BookNode *list_remove_by_title(BookNode *head, const char *title) {
    BookNode *cur = head, *prev = NULL;
    while (cur != NULL) {
        if (strcmp(cur->title, title) == 0) {
            if (prev == NULL) {
                head = cur->next;
            } else {
                prev->next = cur->next;
            }
            free(cur);                        /* SUA loi #2: giai phong node */
            return head;
        }
        prev = cur;
        cur = cur->next;
    }
    return head;
}

int list_count_SAFE(BookNode *head) {
    int count = 0;                             /* SUA loi #3: khoi tao ro rang */
    for (BookNode *cur = head; cur != NULL; cur = cur->next) {
        count++;
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
    printf("So luong sach: %d\n", list_count_SAFE(catalog));

    catalog = list_remove_by_title(catalog, "The Pragmatic Programmer");
    printf("\nSau khi xoa 'The Pragmatic Programmer' (da giai phong dung cach):\n");
    list_print(catalog);
    printf("So luong sach: %d\n", list_count_SAFE(catalog));

    list_free_all(catalog);
    return 0;
}
#endif
