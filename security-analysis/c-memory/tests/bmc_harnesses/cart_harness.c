/* CBMC harness: run the real cart module's entry point. */
#define main module_main
#include "../../modules/cart.c"
#undef main

int main(void) {
    return module_main();
}
