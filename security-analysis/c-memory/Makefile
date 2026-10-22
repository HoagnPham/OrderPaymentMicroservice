CC = gcc
CFLAGS = -std=c11 -g -O0 -Wall -Wextra -fno-omit-frame-pointer
SANFLAGS = -fsanitize=address,undefined -fno-sanitize-recover=all -fno-pie -no-pie
MODULES = order payment cart product user
BUGGY = $(addprefix build/,$(addsuffix _buggy,$(MODULES)))
FIXED = $(addprefix build/,$(addsuffix _fixed,$(MODULES)))
.PHONY: all test static-check bmc-check valgrind-check deep-check clean
all: $(BUGGY) $(FIXED)
build:
	mkdir -p build
build/%_buggy: modules/%.c modules/demo.h | build
	$(CC) $(CFLAGS) $(SANFLAGS) $< -o $@
build/%_fixed: modules/%.c modules/demo.h | build
	$(CC) $(CFLAGS) $(SANFLAGS) -DFIXED $< -o $@
test: all
	python3 tests/check_memory.py
static-check:
	python3 tests/check_static.py --output evidence/static-analysis.json
bmc-check:
	python3 tests/check_bmc.py
valgrind-check:
	python3 tests/check_valgrind.py
deep-check: bmc-check valgrind-check
clean:
	rm -rf build results
