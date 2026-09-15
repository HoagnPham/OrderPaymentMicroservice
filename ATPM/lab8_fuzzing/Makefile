CC = gcc
CFLAGS = -g -Wall -Wextra -fsanitize=address --coverage

.PHONY: all clean test fuzz-blackbox fuzz-whitebox

all: c/fuzz_target

c/fuzz_target: c/fuzz_target.c
	$(CC) $(CFLAGS) -o c/fuzz_target c/fuzz_target.c

fuzz-blackbox: all
	python3 python/blackbox_fuzzer.py

fuzz-whitebox: all
	python3 python/whitebox_fuzzer.py

test: all
	python3 tests/test_fuzzing.py

clean:
	rm -f c/fuzz_target c/*.gcda c/*.gcno c/*.gcov
	rm -f crashes/*.txt
