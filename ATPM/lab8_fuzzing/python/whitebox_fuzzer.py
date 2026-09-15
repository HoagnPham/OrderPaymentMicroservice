"""
Lab 8 - Fuzzing Hop trang (White-box / Coverage-guided Fuzzing)
==================================================================
Chuong 4: 4.6 Kiem thu hop trang (white-box fuzzing)

Y tuong (don gian hoa tu AFL - American Fuzzy Lop): moi khi mot dau vao
moi lam chuong trinh CHAY QUA MOT DONG LENH CHUA TUNG DUOC PHU truoc do
("new coverage"), dau vao do duoc GIU LAI trong "hang doi" (queue) de tiep
tuc dot bien - vi no da chung minh la mot "huong di" co gia tri. Nguoc lai,
neu khong tao ra coverage moi, dau vao bi loai bo. Day chinh la co che
"feedback loop" bien fuzzing tu "mo mam" (blackbox) thanh "co dinh huong"
(whitebox/greybox) - hieu qua hon han khi chuong trinh co dieu kien kich
hoat sau nhieu lop nhanh (nhu "ADMIN:" -> "X" -> do dai > 15 trong
fuzz_target.c).

Ky thuat do coverage tung lan chay rieng le: XOA file .gcda TRUOC moi lan
chay (khong tich luy), sau do goi gcov NGAY SAU do de lay CHINH XAC tap
dong lenh duoc thuc thi TRONG LAN CHAY DO (khong bi lan voi cac lan truoc).
"""
from __future__ import annotations
import os
import random
import re
import string
import subprocess
from collections import deque

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
C_DIR = os.path.join(BASE, "c")
SEED_DIR = os.path.join(BASE, "seed_corpus")
CRASH_DIR = os.path.join(BASE, "crashes")

DICTIONARY = ["ADMIN:", "ORDER:", "X", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"]


def load_seeds() -> list[str]:
    seeds = []
    for fname in sorted(os.listdir(SEED_DIR)):
        with open(os.path.join(SEED_DIR, fname), encoding="utf-8") as f:
            seeds.append(f.read().strip())
    return seeds


def mutate(s: str, rng: random.Random) -> str:
    s = list(s)
    op = rng.choice(["insert_char", "delete_char", "replace_char",
                      "splice_token", "splice_token", "append_block"])
    if op == "insert_char":
        pos = rng.randint(0, len(s))
        s.insert(pos, rng.choice(string.ascii_letters + string.digits + ":,"))
    elif op == "delete_char" and s:
        pos = rng.randint(0, len(s) - 1)
        del s[pos]
    elif op == "replace_char" and s:
        pos = rng.randint(0, len(s) - 1)
        s[pos] = rng.choice(string.ascii_letters + string.digits + ":,")
    elif op == "splice_token":
        pos = rng.randint(0, len(s))
        token = rng.choice(DICTIONARY)
        s[pos:pos] = list(token)
    elif op == "append_block":
        # Dot bien "tang truong" (growth mutation): noi them mot khoi ky tu
        # ngau nhien dai 5-30 ky tu vao CUOI chuoi - lam tang nhanh do dai
        # dau vao, giup fuzzer "tinh co" cham nguong tran bo dem nhanh hon
        # nhieu so voi chi chen tung ky tu don le.
        block_len = rng.randint(5, 30)
        block = "".join(rng.choice(string.ascii_uppercase) for _ in range(block_len))
        s = s + list(block)
    return "".join(s)


def build_target():
    for fname in os.listdir(C_DIR):
        if fname.endswith((".gcda", ".gcno")):
            os.remove(os.path.join(C_DIR, fname))
    subprocess.run(
        ["gcc", "-g", "-fsanitize=address", "-Wall", "-Wextra", "--coverage",
         "-o", "fuzz_target", "fuzz_target.c"],
        cwd=C_DIR, check=True,
    )


def run_and_get_coverage(input_str: str) -> tuple[subprocess.CompletedProcess, frozenset]:
    """Chay 1 lan (co lap coverage - xoa .gcda truoc do), tra ve
    (ket qua tien trinh, tap hop cac dong lenh DUOC THUC THI trong lan nay)."""
    gcda = os.path.join(C_DIR, "fuzz_target.gcda")
    if os.path.exists(gcda):
        os.remove(gcda)

    try:
        proc = subprocess.run(
            [os.path.join(C_DIR, "fuzz_target"), input_str],
            cwd=C_DIR, capture_output=True, text=True, timeout=5,
        )
    except subprocess.TimeoutExpired:
        return None, frozenset()

    covered_lines = set()
    if os.path.exists(gcda):
        subprocess.run(["gcov", "fuzz_target.c"], cwd=C_DIR, capture_output=True, text=True)
        gcov_file = os.path.join(C_DIR, "fuzz_target.c.gcov")
        if os.path.exists(gcov_file):
            with open(gcov_file, encoding="utf-8", errors="ignore") as f:
                for raw in f:
                    m = re.match(r"\s*(\S+):\s*(\d+):", raw)
                    if not m:
                        continue
                    count_str, lineno = m.groups()
                    if count_str not in ("-", "", "#####"):
                        covered_lines.add(int(lineno))
    return proc, frozenset(covered_lines)


def fuzz(iterations: int = 500, seed: int = 7) -> dict:
    build_target()
    rng = random.Random(seed)

    queue = deque(load_seeds())
    global_covered: set = set()
    crashes = []
    new_coverage_events = []

    it = 0
    while it < iterations and queue:
        parent = queue.pop()   # LIFO: uu tien khai thac SAU (dept-first) tu seed MOI phat hien
        # Sinh vai con tu 1 cha (energy scheduling don gian: 3 con moi cha)
        for _ in range(8):
            it += 1
            if it > iterations:
                break
            child = mutate(parent, rng)
            proc, lines = run_and_get_coverage(child)
            if proc is None:
                continue

            if proc.returncode != 0 and "AddressSanitizer" in proc.stderr:
                crash_id = len(crashes)
                crash_path = os.path.join(CRASH_DIR, f"whitebox_crash_{crash_id}.txt")
                with open(crash_path, "w", encoding="utf-8") as f:
                    f.write(child)
                crashes.append({"iteration": it, "input": child, "saved_to": crash_path})
                continue  # dau vao gay crash khong can dua vao queue de dot bien tiep

            new_lines = lines - global_covered
            if new_lines:
                global_covered |= lines
                queue.append(child)   # "thu vien" (interesting): giu lai de dot bien tiep
                new_coverage_events.append({
                    "iteration": it, "input": child, "new_lines": sorted(new_lines),
                })
        # Neu queue rong nhung chua het iteration, nap lai seed ban dau de tiep tuc
        if not queue and it < iterations:
            queue.extend(load_seeds())

    return {
        "iterations_used": it,
        "total_lines_covered": len(global_covered),
        "new_coverage_events": new_coverage_events,
        "crashes": crashes,
    }


if __name__ == "__main__":
    result = fuzz(iterations=500, seed=7)
    print(f"=== Fuzzing Hop trang (coverage-guided): {result['iterations_used']} lan thu ===")
    print(f"Tong so dong lenh da phu: {result['total_lines_covered']}")
    print(f"So su kien 'coverage moi' (dan duong tim kiem):")
    for e in result["new_coverage_events"]:
        print(f"  Lan thu {e['iteration']}: input={e['input']!r} -> phu them dong {e['new_lines']}")
    print(f"\nSo crash tim duoc: {len(result['crashes'])}")
    for c in result["crashes"]:
        print(f"  Lan thu {c['iteration']}: input={c['input']!r} -> luu tai {c['saved_to']}")
