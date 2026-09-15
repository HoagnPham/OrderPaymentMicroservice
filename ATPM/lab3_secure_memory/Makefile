CC = gcc
CFLAGS = -g -Wall -Wextra -fsanitize=address

.PHONY: all clean test risk-report

all: c/vulnerable_inventory c/secure_inventory

c/vulnerable_inventory: c/vulnerable_inventory.c
	$(CC) $(CFLAGS) -o c/vulnerable_inventory c/vulnerable_inventory.c

c/secure_inventory: c/secure_inventory.c
	$(CC) $(CFLAGS) -o c/secure_inventory c/secure_inventory.c

risk-report:
	python3 python/risk_assessor.py

test: all
	python3 tests/test_memory_safety.py

clean:
	rm -f c/vulnerable_inventory c/secure_inventory
