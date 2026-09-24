# Makefile -- c/ch2_memory
# Quy trinh QA theo dung Chuong 2, muc 2.5:
#   make check        -> compiler warnings + cppcheck (nhanh, chay moi commit)
#   make test         -> build + chay test_safe_string voi ASan/UBSan
#   make test-valgrind-> chay lai duoi Valgrind (day du, cham hon)

CC = gcc
CFLAGS = -Wall -Wextra -Wpedantic -std=c11 -g
SAN_FLAGS = -fsanitize=address,undefined -fstack-protector-strong

.PHONY: all check test test-valgrind clean

all: fixed test_safe_string

fixed: fixed.c
	$(CC) $(CFLAGS) -o fixed fixed.c

test_safe_string: test_safe_string.c safe_string.c safe_string.h
	$(CC) $(CFLAGS) $(SAN_FLAGS) -o test_safe_string test_safe_string.c safe_string.c

check:
	@echo "== Compiler warnings (-Wall -Wextra -Wpedantic) =="
	$(CC) $(CFLAGS) -fsyntax-only vulnerable.c fixed.c safe_string.c test_safe_string.c
	@echo ""
	@echo "== Cppcheck (--enable=all) =="
	cppcheck --enable=all --std=c11 --suppress=missingIncludeSystem \
	         vulnerable.c fixed.c safe_string.c test_safe_string.c

test: test_safe_string
	@echo "== Chay test_safe_string voi ASan/UBSan =="
	./test_safe_string

test-valgrind: test_safe_string
	@echo "== Chay test_safe_string duoi Valgrind (can cai valgrind) =="
	valgrind --leak-check=full --show-leak-kinds=all --error-exitcode=1 ./test_safe_string

clean:
	rm -f fixed test_safe_string *.o
