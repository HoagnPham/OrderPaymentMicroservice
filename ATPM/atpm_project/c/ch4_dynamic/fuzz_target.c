/*
 * fuzz_target.c -- Chuong 4: target don gian de minh hoa fuzzing.
 * check_magic() chi crash (NULL deref) khi input CHINH XAC la "FUZZ" (4 byte).
 * Bien dich voi --coverage de do code coverage (gcov) huong dan fuzzer.
 *
 * gcc --coverage -O0 -o fuzz_target fuzz_target.c
 */
#include <stdio.h>
#include <stdlib.h>

void check_magic(const char *buf, int len) {
    if (len < 1 || buf[0] != 'F') return;
    if (len < 2 || buf[1] != 'U') return;
    if (len < 3 || buf[2] != 'Z') return;
    if (len < 4 || buf[3] != 'Z') return;
    char *p = NULL;
    *p = 1;  // crash
}

int main(int argc, char **argv) {
    FILE *f = fopen(argv[1], "rb");
    if (!f) return 0;
    char buf[64];
    int len = fread(buf, 1, sizeof(buf), f);
    fclose(f);
    check_magic(buf, len);
    return 0;
}
