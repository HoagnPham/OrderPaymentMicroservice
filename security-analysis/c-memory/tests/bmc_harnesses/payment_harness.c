/* CBMC harness: run the real payment module's entry point. */
#define main module_main
#include "../../modules/payment.c"
#undef main

int main(void) {
    return module_main();
}
