.PHONY: all clean test coverage-partial coverage-full generate-combinatorial

all:
	gcc -Wall -Wextra -std=c11 --coverage -DORDER_CLI_MAIN -c c/order_processor.c -o c/order_processor.o
	gcc --coverage -o c/order_processor_cli c/order_processor.o

coverage-partial: all
	python3 python/coverage_runner.py

generate-combinatorial:
	python3 python/test_case_generator.py

test:
	python3 tests/test_coverage.py

clean:
	rm -f c/order_processor_cli c/*.o c/*.gcda c/*.gcno c/*.gcov
