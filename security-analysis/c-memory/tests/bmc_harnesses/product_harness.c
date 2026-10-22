/* CBMC harness: call the real product module with a bounded symbolic argv[1]. */
#define main module_main
#include "../../modules/product.c"
#undef main

int main(void) {
    char input[48];
    unsigned length;
    __CPROVER_assume(length < sizeof(input));
    input[length] = 0;
    char *argv[2];
    argv[0] = "prog";
    argv[1] = input;
    return module_main(2, argv);
}
