/* CartService.RemoveItemFromCartAsync modeled as a C linked list.
 * CWE-416: audit logging reads the deleted item after free. */
#include "demo.h"
typedef struct CartItem { int product_id, quantity; struct CartItem *next; } CartItem;
int main(void) {
    CartItem *head = allocate(sizeof(*head));
    *head = (CartItem){101, 2, NULL};
    CartItem *removed = head;
    head = removed->next;
#ifdef FIXED
    int product_id = removed->product_id; 
    free(removed);
    removed = NULL;
    printf("Removed product %d\n", product_id);
#else
    free(removed);
    printf("Removed product %d\n", removed->product_id); /* BUG */
#endif
    return head != NULL;
}
