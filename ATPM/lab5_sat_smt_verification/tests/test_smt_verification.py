"""
Lab 5 - Bo test tu dong
=======================
1. Xac nhan ket qua Z3 khop du doan ky vong tren toan bo 6 cap dieu kien.
2. Xac nhan phan vi du (counterexample) cho cac cap KHONG tuong duong la
   chinh xac (thay the vao P, Q va kiem tra P != Q).
3. Xac nhan chuong trinh C brute-force cho ket qua NHAT QUAN voi Z3 tren
   4 dieu kien co the kiem chung bang liet ke.
"""
import os
import subprocess
import sys

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(BASE, "python"))

from smt_equivalence_checker import run_all, eval_expr, make_vars  # noqa: E402
from z3 import sat, unsat, Solver, Xor, BoolRef  # noqa: E402

DATASET = os.path.join(BASE, "dataset", "condition_pairs.json")


def test_all_pairs_match_expectation():
    results = run_all(DATASET)
    mismatches = [r for r in results if not r["matches_expectation"]]
    assert not mismatches, f"Cac cap sai lech ky vong: {[m['id'] for m in mismatches]}"
    assert len(results) == 6


def test_counterexample_is_valid_for_non_equivalent_pairs():
    import json
    with open(DATASET, encoding="utf-8") as f:
        data = json.load(f)
    results = run_all(DATASET)
    by_id = {r["id"]: r for r in results}
    for pair in data["condition_pairs"]:
        if pair["expected_equivalent"]:
            continue
        r = by_id[pair["id"]]
        assert r["counterexample"] is not None
        # Xac nhan phan vi du thuc su lam P va Q khac nhau
        env = make_vars(pair["var_types"])
        P = eval_expr(pair["original"], env)
        Q = eval_expr(pair["optimized"], env)
        s = Solver()
        for name, val in r["counterexample"].items():
            var = env[name]
            if isinstance(var, BoolRef) or str(var.sort()) == "Bool":
                s.add(var == (val == "True"))
            else:
                s.add(var == int(val))
        s.add(Xor(P, Q))
        assert s.check() == sat, f"Phan vi du cua {pair['id']} khong hop le"


def test_c_brute_force_matches_z3():
    subprocess.run(
        ["gcc", "-Wall", "-Wextra", "-std=c11", "-o", "brute_force_checker", "brute_force_checker.c"],
        cwd=os.path.join(BASE, "c"), check=True,
    )
    proc = subprocess.run(
        [os.path.join(BASE, "c", "brute_force_checker")],
        capture_output=True, text=True, check=True,
    )
    out = proc.stdout
    assert "[COND-01] TUONG DUONG" in out
    assert "[COND-03] TUONG DUONG" in out
    assert "[COND-04] TUONG DUONG" in out
    assert "[COND-05] KHONG TUONG DUONG" in out


if __name__ == "__main__":
    tests = [test_all_pairs_match_expectation, test_counterexample_is_valid_for_non_equivalent_pairs,
              test_c_brute_force_matches_z3]
    passed = 0
    for t in tests:
        try:
            t()
            print(f"[PASS] {t.__name__}")
            passed += 1
        except AssertionError as e:
            print(f"[FAIL] {t.__name__}: {e}")
    print(f"\n{passed}/{len(tests)} test da PASS")
