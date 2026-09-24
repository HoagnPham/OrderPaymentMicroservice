CC = gcc
CFLAGS = -std=c11 -g -O0 -Wall -Wextra -fno-omit-frame-pointer
SANFLAGS = -fsanitize=address,undefined -fno-sanitize-recover=all -fno-pie -no-pie
MODULES = order payment cart product user
BUGGY = $(addprefix build/,$(addsuffix _buggy,$(MODULES)))
FIXED = $(addprefix build/,$(addsuffix _fixed,$(MODULES)))
.PHONY: all test clean
all: $(BUGGY) $(FIXED)
build:
	mkdir -p build
build/%_buggy: modules/%.c modules/demo.h | build
	$(CC) $(CFLAGS) $(SANFLAGS) $< -o $@
build/%_fixed: modules/%.c modules/demo.h | build
	$(CC) $(CFLAGS) $(SANFLAGS) -DFIXED $< -o $@
test: all
	python3 tests/check_memory.py
clean:
	rm -rf build results
