CC = gcc
CFLAGS = -Wall -Wextra -std=c11 -O2

.PHONY: all clean lib cli coverage test

all: lib cli

lib: c/pin_validator.c
	$(CC) -shared -fPIC -o c/libpinvalidator.so c/pin_validator.c

cli: c/pin_validator.c
	$(CC) $(CFLAGS) -DPIN_CLI_MAIN -o c/pin_validator_cli c/pin_validator.c

black-box: lib
	python3 python/black_box_tester.py

coverage:
	python3 python/white_box_coverage.py

test: lib
	python3 tests/test_pin_validator.py

clean:
	rm -f c/libpinvalidator.so c/pin_validator_cli c/*.gcda c/*.gcno c/*.gcov c/*.o
