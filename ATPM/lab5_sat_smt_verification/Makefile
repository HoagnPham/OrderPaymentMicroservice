CC = gcc
CFLAGS = -Wall -Wextra -std=c11 -O2

.PHONY: all clean test smt-report

all: c/brute_force_checker

c/brute_force_checker: c/brute_force_checker.c
	$(CC) $(CFLAGS) -o c/brute_force_checker c/brute_force_checker.c

smt-report:
	python3 python/smt_equivalence_checker.py

test: all
	python3 tests/test_smt_verification.py

clean:
	rm -f c/brute_force_checker
