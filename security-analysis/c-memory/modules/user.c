/* UserService: copy a registration email into a local buffer.
 * CWE-121: stack buffer overflow. This is not a full email validator. */
#include "demo.h"
#define EMAIL_CAPACITY 32
static void register_user(const char *input) {
    char email[EMAIL_CAPACITY];
#ifdef FIXED
    if (strlen(input) >= sizeof(email)) {
        puts("REJECTED: email too long");
        return;
    }
#endif
    strcpy(email, input); /* BUG without length validation. */
    printf("Registered email: %s\n", email);
}
int main(int argc, char **argv) {
    register_user(argc > 1 ? argv[1] :
        "very-long-email-address-over-32-characters@example.test");
    return 0;
}
