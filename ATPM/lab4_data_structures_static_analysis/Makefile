CC = gcc
CFLAGS = -Wall -Wextra -std=c11 -O2

.PHONY: all clean test cppcheck-report

all: c/booklist_v1 c/booklist_v2

c/booklist_v1: c/booklist_v1_buggy.c
	$(CC) $(CFLAGS) -o c/booklist_v1 c/booklist_v1_buggy.c

c/booklist_v2: c/booklist_v2_fixed.c
	$(CC) $(CFLAGS) -o c/booklist_v2 c/booklist_v2_fixed.c

cppcheck-report:
	python3 python/static_analysis_runner.py

test:
	python3 tests/test_static_analysis.py

clean:
	rm -f c/booklist_v1 c/booklist_v2 c/*.xml
