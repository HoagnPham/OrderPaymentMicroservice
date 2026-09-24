/* ProductService: import a product name into a fixed heap buffer.
 * CWE-122: unchecked copy crosses the allocation boundary. */
#include "demo.h"
#define NAME_CAPACITY 32
typedef struct { int id; char *name; } Product;
int main(int argc, char **argv) {
    const char *input = argc > 1 ? argv[1] :
        "Product-name-that-is-much-longer-than-the-allocated-buffer";
    Product product = {101, allocate(NAME_CAPACITY)};
#ifdef FIXED
    if (strlen(input) >= NAME_CAPACITY) {
        puts("REJECTED: product name too long");
        free(product.name);
        return 0; 
    }
#endif
    strcpy(product.name, input);
    printf("Product %d: %s\n", product.id, product.name);
    free(product.name);
    return 0;
}
