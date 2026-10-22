/* CBMC harness: run the real order module's entry point. */
#define main module_main
#include "../../modules/order.c"
#undef main

int main(void) {
    return module_main();
}
