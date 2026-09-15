CC = gcc
CFLAGS = -Wall -Wextra -std=c11 -O2
TARGET = c/access_simulator

.PHONY: all clean run-safe run-buggy test

all: $(TARGET)

$(TARGET): c/access_simulator.c
	$(CC) $(CFLAGS) -o $(TARGET) c/access_simulator.c

run-safe: $(TARGET)
	./$(TARGET) dataset/access_safe_states.csv dataset/access_safe_transitions.csv dataset/access_safe_init.txt insert_card auth_success door_closed_timeout

run-buggy: $(TARGET)
	./$(TARGET) dataset/access_buggy_states.csv dataset/access_buggy_transitions.csv dataset/access_buggy_init.txt insert_card timeout_glitch

verify-python:
	python3 python/kripke_model.py dataset/access_system_model.json
	python3 python/kripke_model.py dataset/access_system_model_BUGGY.json

test: $(TARGET)
	python3 tests/test_kripke.py

clean:
	rm -f $(TARGET)
