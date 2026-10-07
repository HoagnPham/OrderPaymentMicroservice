/* Measure the real declarations used by the five demos.  Renaming main lets
 * this translation unit include the modules without duplicating their types. */
#include <stddef.h>

#define main cart_demo_main
#include "../modules/cart.c"
#undef main
#define main order_demo_main
#include "../modules/order.c"
#undef main
#define main payment_demo_main
#include "../modules/payment.c"
#undef main
#define main product_demo_main
#include "../modules/product.c"
#undef main
#define main user_demo_main
#include "../modules/user.c"
#undef main

#define LAYOUT(type)                                                            \
    printf("\"%s\":{\"size\":%zu,\"alignment\":%zu}", #type,              \
           sizeof(type), _Alignof(type))

int main(void) {
    printf("{");
    LAYOUT(CartItem);
    printf(",");
    LAYOUT(OrderItem);
    printf(",");
    LAYOUT(Order);
    printf(",");
    LAYOUT(Payment);
    printf(",");
    LAYOUT(Product);
    printf(",\"offsets\":{");
    printf("\"Order.items\":%zu,", offsetof(Order, items));
    printf("\"Payment.transaction_code\":%zu,",
           offsetof(Payment, transaction_code));
    printf("\"Product.name\":%zu", offsetof(Product, name));
    printf("}}\n");
    return 0;
}
