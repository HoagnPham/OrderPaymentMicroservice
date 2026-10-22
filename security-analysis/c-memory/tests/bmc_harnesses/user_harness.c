/* CBMC harness: exercise register_user() with a bounded symbolic input.
 * The harness includes the real teaching module (renaming its main) so the
 * property is checked on the same C code that ASan/UBSan execute. */
#define main module_main
#include "../../modules/user.c"
#undef main

int main(void) {
    char input[48];
    unsigned length;
    __CPROVER_assume(length < sizeof(input));
    input[length] = 0;
    register_user(input);
    return 0;
}
