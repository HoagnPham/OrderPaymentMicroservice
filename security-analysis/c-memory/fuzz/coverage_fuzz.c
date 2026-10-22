#define _GNU_SOURCE
#include "cov_runtime.h"
#include "fixture_target.h"

#include <errno.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/wait.h>
#include <unistd.h>

#define MAX_INPUT 512u
#define MAX_QUEUE 4096u

typedef struct {
    uint8_t *data;
    size_t length;
} Input;

static uint64_t rng_state;

static uint64_t next_random(void) {
    rng_state ^= rng_state << 13;
    rng_state ^= rng_state >> 7;
    rng_state ^= rng_state << 17;
    return rng_state;
}

static size_t random_below(size_t limit) {
    return limit ? (size_t)(next_random() % limit) : 0;
}

static Input copy_input(const uint8_t *data, size_t length) {
    Input input;
    input.length = length;
    input.data = malloc(length ? length : 1);
    if (!input.data) {
        perror("malloc input");
        exit(2);
    }
    if (length) memcpy(input.data, data, length);
    return input;
}

static void free_input(Input *input) {
    free(input->data);
    input->data = NULL;
    input->length = 0;
}

static const uint8_t DICTIONARY[] = {
    0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x07, 0x0a,
    0x10, 0x27, 0x28, 0x5a, 0x7f, 0x80, 0xdb, 0xff,
};

static void mutate(const Input *base, Input *out) {
    out->length = base->length;
    out->data = malloc(MAX_INPUT);
    if (!out->data) {
        perror("malloc mutation");
        exit(2);
    }
    if (base->length) memcpy(out->data, base->data, base->length);

    unsigned rounds = 1u + (unsigned)random_below(4);
    for (unsigned round = 0; round < rounds; ++round) {
        unsigned operation = (unsigned)random_below(7);
        if (operation == 0 && out->length) {
            size_t position = random_below(out->length);
            out->data[position] ^= (uint8_t)(1u << random_below(8));
        } else if (operation == 1 && out->length) {
            out->data[random_below(out->length)] = (uint8_t)next_random();
        } else if (operation == 2 && out->length < MAX_INPUT) {
            size_t position = random_below(out->length + 1);
            memmove(out->data + position + 1, out->data + position,
                    out->length - position);
            out->data[position] = (uint8_t)next_random();
            ++out->length;
        } else if (operation == 3 && out->length) {
            size_t position = random_below(out->length);
            memmove(out->data + position, out->data + position + 1,
                    out->length - position - 1);
            --out->length;
        } else if (operation == 4 && out->length < MAX_INPUT) {
            size_t position = random_below(out->length + 1);
            uint8_t token = DICTIONARY[random_below(sizeof(DICTIONARY))];
            memmove(out->data + position + 1, out->data + position,
                    out->length - position);
            out->data[position] = token;
            ++out->length;
        } else if (operation == 5 && out->length) {
            out->data[random_below(out->length)] =
                DICTIONARY[random_below(sizeof(DICTIONARY))];
        } else if (out->length > 1) {
            size_t position = random_below(out->length - 1);
            out->data[position] = out->data[position + 1];
        }
    }
}

static int execute_one(const uint8_t *data, size_t length, int *crashed) {
    cov_reset();
    pid_t pid = fork();
    if (pid < 0) {
        perror("fork");
        exit(2);
    }
    if (pid == 0) {
        int result = fuzz_target(data, length);
        _exit(result & 0xff);
    }

    int status = 0;
    if (waitpid(pid, &status, 0) < 0) {
        perror("waitpid");
        exit(2);
    }
    *crashed = WIFSIGNALED(status) ? 1 : 0;
    return WIFEXITED(status) ? WEXITSTATUS(status) : -1;
}

static void add_queue(Input *queue, size_t *queue_size, const uint8_t *data,
                      size_t length) {
    if (*queue_size >= MAX_QUEUE) return;
    queue[*queue_size] = copy_input(data, length);
    ++*queue_size;
}

int main(int argc, char **argv) {
    unsigned long seed = 20261022ul;
    unsigned long iterations = 4000ul;
    const char *mode = "guided";

    for (int i = 1; i < argc; ++i) {
        if (!strcmp(argv[i], "--seed") && i + 1 < argc) {
            seed = strtoul(argv[++i], NULL, 10);
        } else if (!strcmp(argv[i], "--iterations") && i + 1 < argc) {
            iterations = strtoul(argv[++i], NULL, 10);
        } else if (!strcmp(argv[i], "--mode") && i + 1 < argc) {
            mode = argv[++i];
        } else {
            fprintf(stderr, "usage: %s [--seed N] [--iterations N] [--mode guided|random]\n",
                    argv[0]);
            return 2;
        }
    }
    if (strcmp(mode, "guided") && strcmp(mode, "random")) {
        fputs("mode must be guided or random\n", stderr);
        return 2;
    }
    if (iterations > 1000000ul) iterations = 1000000ul;

    rng_state = seed ? seed : 1ul;
    cov_init();
    uint8_t *global_map = calloc(COV_MAP_SIZE, 1);
    if (!global_map) {
        perror("calloc global map");
        return 2;
    }

    Input queue[MAX_QUEUE];
    size_t queue_size = 0;
    size_t seed_count = fixture_seed_count();
    Input seed_copies[32];
    if (seed_count > 32) seed_count = 32;

    size_t crashed = 0;
    size_t executed = 0;
    size_t max_length = 0;
    size_t seed_new_coverage = 0;

    for (size_t i = 0; i < seed_count; ++i) {
        size_t length = 0;
        const uint8_t *data = fixture_seed(i, &length);
        seed_copies[i] = copy_input(data, length);
        if (mode[0] == 'g') add_queue(queue, &queue_size, data, length);
        int crashed_now = 0;
        (void)execute_one(data, length, &crashed_now);
        size_t new_edges = cov_merge_new(global_map);
        if (new_edges) ++seed_new_coverage;
        if (crashed_now) ++crashed;
        ++executed;
        if (length > max_length) max_length = length;
    }

    size_t initial_edges = cov_count_global(global_map);
    size_t new_coverage_inputs = 0;
    size_t mutations = 0;

    for (unsigned long i = 0; i < iterations; ++i) {
        const Input *base;
        if (mode[0] == 'g') {
            base = &queue[random_below(queue_size)];
        } else {
            base = &seed_copies[random_below(seed_count)];
        }

        Input mutated;
        mutate(base, &mutated);
        ++mutations;
        if (mutated.length > max_length) max_length = mutated.length;
        int crashed_now = 0;
        (void)execute_one(mutated.data, mutated.length, &crashed_now);
        size_t new_edges = cov_merge_new(global_map);
        if (mode[0] == 'g' && new_edges && !crashed_now) {
            ++new_coverage_inputs;
            add_queue(queue, &queue_size, mutated.data, mutated.length);
        }
        if (crashed_now) ++crashed;
        ++executed;
        free_input(&mutated);
    }

    size_t final_edges = cov_count_global(global_map);
    printf("mode=%s\n", mode);
    printf("seed=%lu\n", seed);
    printf("iterations=%lu\n", iterations);
    printf("mutations=%zu\n", mutations);
    printf("executed=%zu\n", executed);
    printf("seed_inputs=%zu\n", seed_count);
    printf("seed_new_coverage=%zu\n", seed_new_coverage);
    printf("new_coverage_inputs=%zu\n", new_coverage_inputs);
    printf("queue_size=%zu\n", queue_size);
    printf("initial_edges=%zu\n", initial_edges);
    printf("final_edges=%zu\n", final_edges);
    printf("new_edges=%zu\n", final_edges - initial_edges);
    printf("crashes=%zu\n", crashed);
    printf("max_input_length=%zu\n", max_length);

    for (size_t i = 0; i < seed_count; ++i) free_input(&seed_copies[i]);
    for (size_t i = 0; i < queue_size; ++i) free_input(&queue[i]);
    free(global_map);
    return 0;
}
