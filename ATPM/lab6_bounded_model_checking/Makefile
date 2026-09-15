CC = gcc
CFLAGS = -g -Wall -Wextra -fsanitize=address

.PHONY: all clean test bmc-report

all: c/array_access

c/array_access: c/array_access.c
	$(CC) $(CFLAGS) -o c/array_access c/array_access.c

bmc-report:
	python3 python/bmc_engine.py

test: all
	python3 tests/test_bmc.py

clean:
	rm -f c/array_access
