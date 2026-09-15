"""
Lab 8 - Fuzzing Hop den (Black-box Fuzzing)
=============================================
Chuong 4: 4.5 Kiem thu hop den (black-box fuzzing)

Dac diem cua fuzzing hop den: bo fuzzer KHONG nhin thay ma nguon hay thong
tin coverage cua chuong trinh muc tieu - no chi quan sat "chuong trinh co
crash hay khong" tu ben ngoai (hop den). O day ta cai dat mot bien the
THUC TE hon "random tuyet doi": dung mot corpus hat giong (seed corpus)
va mot tu dien token (dictionary) roi DOT BIEN NGAU NHIEN (random mutation:
chen/xoa/thay ky tu, hoac ghep token tu dictionary vao vi tri ngau nhien).
Day van la ky thuat hop den vi KHONG dua vao coverage feedback de dan
duong tim kiem - chi dua vao may man + kho ngu.
"""
from __future__ import annotations
import os
import random
import string
import subprocess

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
C_DIR = os.path.join(BASE, "c")
SEED_DIR = os.path.join(BASE, "seed_corpus")
CRASH_DIR = os.path.join(BASE, "crashes")

# Tu dien token: mo phong kien thuc "chung" ma mot tester hop den co the co
# (vi du tu tai lieu API), KHONG phai thong tin lay tu doc ma nguon.
DICTIONARY = ["ADMIN:", "ORDER:", "X", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"]


def load_seeds() -> list[str]:
    seeds = []
    for fname in sorted(os.listdir(SEED_DIR)):
        with open(os.path.join(SEED_DIR, fname), encoding="utf-8") as f:
            seeds.append(f.read().strip())
    return seeds


def mutate(s: str, rng: random.Random) -> str:
    s = list(s)
    op = rng.choice(["insert_char", "delete_char", "replace_char", "splice_token"])
    if op == "insert_char" and True:
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
    return "".join(s)


def run_target(input_str: str) -> subprocess.CompletedProcess:
    return subprocess.run(
        [os.path.join(C_DIR, "fuzz_target"), input_str],
        cwd=C_DIR, capture_output=True, text=True, timeout=5,
    )


def build_target():
    subprocess.run(
        ["gcc", "-g", "-fsanitize=address", "-Wall", "-Wextra", "--coverage",
         "-o", "fuzz_target", "fuzz_target.c"],
        cwd=C_DIR, check=True,
    )


def fuzz(iterations: int = 2000, seed: int = 42) -> dict:
    build_target()
    rng = random.Random(seed)
    corpus = load_seeds()
    crashes = []

    for i in range(iterations):
        base = rng.choice(corpus)
        candidate = mutate(base, rng)
        try:
            proc = run_target(candidate)
        except subprocess.TimeoutExpired:
            continue
        if proc.returncode != 0 and "AddressSanitizer" in proc.stderr:
            crash_id = len(crashes)
            crash_path = os.path.join(CRASH_DIR, f"blackbox_crash_{crash_id}.txt")
            with open(crash_path, "w", encoding="utf-8") as f:
                f.write(candidate)
            crashes.append({"iteration": i, "input": candidate, "saved_to": crash_path})
        # Doi luc them ket qua khong crash vao corpus (co ban) de tang da dang
        if rng.random() < 0.05:
            corpus.append(candidate)

    return {"iterations": iterations, "corpus_final_size": len(corpus), "crashes": crashes}


if __name__ == "__main__":
    result = fuzz(iterations=3000, seed=42)
    print(f"=== Fuzzing Hop den: {result['iterations']} lan thu ===")
    print(f"Corpus cuoi cung: {result['corpus_final_size']} mau")
    print(f"So crash tim duoc: {len(result['crashes'])}")
    for c in result["crashes"][:10]:
        print(f"  Lan thu {c['iteration']}: input={c['input']!r} -> luu tai {c['saved_to']}")
