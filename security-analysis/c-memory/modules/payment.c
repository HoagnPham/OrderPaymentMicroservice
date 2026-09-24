/* PaymentService: gateway failure followed by common cleanup.
 * CWE-415: two cleanup paths both believe they own TransactionCode. */
#include "demo.h"
typedef struct { int order_id; char *transaction_code; } Payment;
int main(void) {
    Payment payment = {1, allocate(32)};
    strcpy(payment.transaction_code, "TX-DEMO-001");
    puts("Gateway failure: release transaction code");
    free(payment.transaction_code);
#ifdef FIXED
    payment.transaction_code = NULL; /* Clear owning pointer; free(NULL) is valid. */
#endif
    free(payment.transaction_code); /* BUG: common cleanup frees it again. */
    return 0;
}
