/*
 * Lab 1 - Access Control State Machine Simulator (C)
 * ===================================================
 * Chu de: SecureSys - module Access Control (Kiem soat truy cap cua ra vao)
 *
 * Chuong trinh nay la ban "cai dat that" (runtime implementation) tuong ung
 * voi mo hinh Kripke duoc kiem chung tinh (static verification) o file
 * python/kripke_model.py. Muc tieu su pham:
 *   - Minh hoa rang mo hinh (model) va cai dat (implementation) la hai thu
 *     khac nhau: mo hinh dung o Lab1-Python khong co nghia cai dat C se
 *     dung neu lap trinh sai.
 *   - Thuc hanh doc du lieu tu file (dataset) an toan trong C (kiem tra do
 *     dai buffer, khong dung gets(), kiem tra fopen tra ve NULL, ...).
 *   - Sinh ra "execution trace" tu mot chuoi su kien (event sequence) de
 *     doi chieu voi ket qua kiem chung mo hinh.
 *
 * Bien dich:
 *   gcc -Wall -Wextra -std=c11 -o access_simulator access_simulator.c
 *
 * Su dung:
 *   ./access_simulator <states.csv> <transitions.csv> <init.txt> <events...>
 *   vi du:
 *   ./access_simulator ../dataset/access_safe_states.csv \
 *                       ../dataset/access_safe_transitions.csv \
 *                       ../dataset/access_safe_init.txt \
 *                       insert_card auth_success
 */

#define _POSIX_C_SOURCE 200809L
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define MAX_STATES      64
#define MAX_TRANSITIONS 256
#define MAX_LINE        512
#define MAX_ID_LEN      32
#define MAX_NAME_LEN    64
#define MAX_LABELS_LEN  128

typedef struct {
    char id[MAX_ID_LEN];
    char name[MAX_NAME_LEN];
    char labels[MAX_LABELS_LEN];   /* labels separated by '|' */
} State;

typedef struct {
    char from[MAX_ID_LEN];
    char event[MAX_NAME_LEN];
    char to[MAX_ID_LEN];
} Transition;

static State states[MAX_STATES];
static int n_states = 0;

static Transition transitions[MAX_TRANSITIONS];
static int n_transitions = 0;

/* Doc an toan mot dong CSV (khong dung gets()), loai bo newline cuoi dong */
static int safe_read_line(FILE *fp, char *buf, size_t bufsize) {
    if (fgets(buf, (int)bufsize, fp) == NULL) {
        return 0;
    }
    size_t len = strnlen(buf, bufsize);
    while (len > 0 && (buf[len - 1] == '\n' || buf[len - 1] == '\r')) {
        buf[len - 1] = '\0';
        len--;
    }
    return 1;
}

/* Tach chuoi theo dau phay, gioi han so truong toi da (chong tran bo dem) */
static int split_csv(char *line, char *fields[], int max_fields) {
    int count = 0;
    char *tok = strtok(line, ",");
    while (tok != NULL && count < max_fields) {
        fields[count++] = tok;
        tok = strtok(NULL, ",");
    }
    return count;
}

static int load_states(const char *path) {
    FILE *fp = fopen(path, "r");
    if (!fp) {
        fprintf(stderr, "[LOI] Khong the mo file trang thai: %s\n", path);
        return -1;
    }
    char line[MAX_LINE];
    safe_read_line(fp, line, sizeof(line)); /* bo qua header */
    while (safe_read_line(fp, line, sizeof(line))) {
        if (line[0] == '\0') continue;
        if (n_states >= MAX_STATES) {
            fprintf(stderr, "[LOI] Vuot qua so trang thai toi da (%d)\n", MAX_STATES);
            break;
        }
        char *fields[3];
        int nf = split_csv(line, fields, 3);
        if (nf < 2) continue;
        snprintf(states[n_states].id, MAX_ID_LEN, "%s", fields[0]);
        snprintf(states[n_states].name, MAX_NAME_LEN, "%s", fields[1]);
        snprintf(states[n_states].labels, MAX_LABELS_LEN, "%s", nf > 2 ? fields[2] : "");
        n_states++;
    }
    fclose(fp);
    return 0;
}

static int load_transitions(const char *path) {
    FILE *fp = fopen(path, "r");
    if (!fp) {
        fprintf(stderr, "[LOI] Khong the mo file chuyen trang thai: %s\n", path);
        return -1;
    }
    char line[MAX_LINE];
    safe_read_line(fp, line, sizeof(line)); /* bo qua header */
    while (safe_read_line(fp, line, sizeof(line))) {
        if (line[0] == '\0') continue;
        if (n_transitions >= MAX_TRANSITIONS) {
            fprintf(stderr, "[LOI] Vuot qua so chuyen tiep toi da (%d)\n", MAX_TRANSITIONS);
            break;
        }
        char *fields[3];
        int nf = split_csv(line, fields, 3);
        if (nf < 3) continue;
        snprintf(transitions[n_transitions].from, MAX_ID_LEN, "%s", fields[0]);
        snprintf(transitions[n_transitions].event, MAX_NAME_LEN, "%s", fields[1]);
        snprintf(transitions[n_transitions].to, MAX_ID_LEN, "%s", fields[2]);
        n_transitions++;
    }
    fclose(fp);
    return 0;
}

static const State *find_state(const char *id) {
    for (int i = 0; i < n_states; i++) {
        if (strncmp(states[i].id, id, MAX_ID_LEN) == 0) return &states[i];
    }
    return NULL;
}

static int has_label(const State *s, const char *label) {
    /* labels duoc phan cach boi '|' */
    char buf[MAX_LABELS_LEN];
    snprintf(buf, sizeof(buf), "%s", s->labels);
    char *tok = strtok(buf, "|");
    while (tok) {
        if (strcmp(tok, label) == 0) return 1;
        tok = strtok(NULL, "|");
    }
    return 0;
}

/* Tim trang thai ke tiep theo su kien; tra ve NULL neu khong co canh hop le
 * (su kien khong duoc dinh nghia tai trang thai hien tai -> "khong hop le",
 * day la mot dang kiem tra an toan: tu choi su kien khong xac dinh thay vi
 * gay hanh vi khong xac dinh - "fail closed" thay vi "fail open"). */
static const char *next_state(const char *cur, const char *event) {
    for (int i = 0; i < n_transitions; i++) {
        if (strcmp(transitions[i].from, cur) == 0 &&
            strcmp(transitions[i].event, event) == 0) {
            return transitions[i].to;
        }
    }
    return NULL;
}

int main(int argc, char *argv[]) {
    if (argc < 4) {
        fprintf(stderr,
            "Cach dung: %s <states.csv> <transitions.csv> <init.txt> [event1 event2 ...]\n",
            argv[0]);
        return 1;
    }

    if (load_states(argv[1]) != 0) return 1;
    if (load_transitions(argv[2]) != 0) return 1;

    FILE *fi = fopen(argv[3], "r");
    if (!fi) {
        fprintf(stderr, "[LOI] Khong the mo file trang thai khoi tao: %s\n", argv[3]);
        return 1;
    }
    char init_id[MAX_ID_LEN];
    if (!safe_read_line(fi, init_id, sizeof(init_id))) {
        fprintf(stderr, "[LOI] File trang thai khoi tao rong\n");
        fclose(fi);
        return 1;
    }
    fclose(fi);
    /* Neu co nhieu trang thai khoi tao (phan cach boi ','), lay cai dau tien */
    char *comma = strchr(init_id, ',');
    if (comma) *comma = '\0';

    printf("=== Access Simulator (C) - da nap %d trang thai, %d chuyen tiep ===\n",
           n_states, n_transitions);

    const char *cur = init_id;
    const State *scur = find_state(cur);
    if (!scur) {
        fprintf(stderr, "[LOI] Trang thai khoi tao '%s' khong ton tai trong dataset\n", cur);
        return 1;
    }
    printf("Trang thai khoi tao: %s (%s) [%s]\n", scur->id, scur->name, scur->labels);

    int unsafe_event_count = 0;
    for (int i = 4; i < argc; i++) {
        const char *event = argv[i];
        const char *nxt = next_state(cur, event);
        if (!nxt) {
            printf("  [BI TU CHOI] su kien '%s' khong hop le tai trang thai '%s' "
                   "(fail-closed: giu nguyen trang thai)\n", event, cur);
            unsafe_event_count++;
            continue;
        }
        const State *snxt = find_state(nxt);
        printf("  --(%s)--> %s (%s) [%s]\n", event,
               snxt ? snxt->id : nxt,
               snxt ? snxt->name : "?",
               snxt ? snxt->labels : "?");

        /* Kiem tra runtime don gian tuong ung thuoc tinh P1: canh bao neu
         * he thong roi vao trang thai 'unlocked' ma khong co nhan
         * 'authenticated' - day la mot "runtime assertion" bo sung cho
         * ket qua kiem chung tinh o Lab1-Python. */
        if (snxt && has_label(snxt, "unlocked") && !has_label(snxt, "authenticated")) {
            printf("  [CANH BAO AN TOAN] Trang thai '%s' la 'unlocked' nhung "
                   "KHONG 'authenticated' - vi pham thuoc tinh P1!\n", snxt->id);
        }
        cur = nxt;
    }

    printf("=== Trang thai cuoi cung: %s ===\n", cur);
    printf("So su kien bi tu choi (khong hop le): %d\n", unsafe_event_count);
    return 0;
}
