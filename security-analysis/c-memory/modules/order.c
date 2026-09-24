/* OrderService: cancel an order and release its owned OrderItems.
 * CWE-401: freeing the parent does not free its child allocation in C. */
#include "demo.h"
typedef struct { int product_id, quantity; long unit_price; } OrderItem;
typedef struct { int id; size_t count; OrderItem *items; } Order;
static void cancel_order(void) {
    Order *order = allocate(sizeof(*order));
    order->id = 1;
    order->count = 3;
    order->items = allocate(order->count * sizeof(*order->items));
    for (size_t i = 0; i < order->count; ++i)
        order->items[i] = (OrderItem){(int)i + 1, 2, 45000};
    printf("Cancel order %d: %zu items\n", order->id, order->count);
#ifdef FIXED
    free(order->items); 
#endif
    free(order); 
}
int main(void) {
    for (int i = 0; i < 5; ++i) cancel_order();
    return 0;
}
