"""
Lab 5 - Kiem chung Tuong duong Logic bang SAT/SMT Solving (Z3)
=================================================================
Chuong 3: 3.1 Gioi thieu kiem chung va tham dinh phan mem,
          3.4 SAT and SMT solving

Bai toan: cho 2 bieu thuc boolean/so hoc P (original) va Q (optimized),
kiem tra P va Q co TUONG DUONG LOGIC hay khong, tuc la:
        forall x1, x2, ..., xn:  P(x1,...,xn) <=> Q(x1,...,xn)

Ky thuat SMT chuan: P khong tuong duong Q  <=>  (P XOR Q) la THOA MAN (SAT).
  - Neu solver tra ve UNSAT cho (P XOR Q)  => P va Q TUONG DUONG (khong co
    phan gan gia tri nao lam chung khac nhau).
  - Neu solver tra ve SAT                  => P va Q KHONG tuong duong, va
    solver cung cho ta mot "counterexample" (phan vi du) cu the: bo gia tri
    bien lam P va Q cho ket qua khac nhau -> cuc ky huu ich de debug.

Day chinh la nen tang ly thuyet cua cac cong cu kiem chung hinh thuc
(formal verification) dung trong cong nghiep (vd: CBMC o Lab 6).
"""
from __future__ import annotations
import json
import os
from z3 import Bool, Int, Solver, sat, unsat, Not, Xor  # noqa: F401
# Cac ham And/Or/Implies duoc "eval" truc tiep tu chuoi bieu thuc trong
# dataset, nen can import vao namespace hien tai:
from z3 import And, Or, Implies  # noqa: F401

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def make_vars(var_types: dict) -> dict:
    """Tao cac bien Z3 tuong ung khai bao kieu trong dataset."""
    env = {}
    for name, vtype in var_types.items():
        if vtype == "Bool":
            env[name] = Bool(name)
        elif vtype == "Int":
            env[name] = Int(name)
        else:
            raise ValueError(f"Kieu bien khong ho tro: {vtype}")
    return env


def eval_expr(expr_str: str, env: dict):
    """Danh gia mot chuoi bieu thuc Z3 (vi du 'And(a, Not(b))') trong ngu
    canh cac bien da khai bao. CHI dung cho dataset noi bo, tin cay (KHONG
    dung eval() voi input tu nguoi dung khong tin cay trong san pham that)."""
    allowed_names = {**env, "And": And, "Or": Or, "Not": Not, "Implies": Implies}
    return eval(expr_str, {"__builtins__": {}}, allowed_names)  # noqa: S307


def check_equivalence(pair: dict) -> dict:
    env = make_vars(pair["var_types"])
    P = eval_expr(pair["original"], env)
    Q = eval_expr(pair["optimized"], env)

    solver = Solver()
    solver.add(Xor(P, Q))   # tim gia tri bien lam P va Q KHAC NHAU
    result = solver.check()

    is_equivalent = (result == unsat)
    counterexample = None
    if result == sat:
        model = solver.model()
        counterexample = {str(d): str(model[d]) for d in model.decls()}

    return {
        "id": pair["id"],
        "context": pair["context"],
        "original": pair["original"],
        "optimized": pair["optimized"],
        "is_equivalent": is_equivalent,
        "expected_equivalent": pair["expected_equivalent"],
        "counterexample": counterexample,
        "note": pair["note"],
        "matches_expectation": is_equivalent == pair["expected_equivalent"],
    }


def run_all(dataset_path: str) -> list[dict]:
    with open(dataset_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    return [check_equivalence(p) for p in data["condition_pairs"]]


def print_report(results: list[dict]):
    for r in results:
        verdict = "TUONG DUONG (UNSAT tren P XOR Q)" if r["is_equivalent"] else "KHONG TUONG DUONG (SAT)"
        mark = "OK" if r["matches_expectation"] else "SAI LECH KY VONG!"
        print(f"[{r['id']}] {r['context']}")
        print(f"  P (original) : {r['original']}")
        print(f"  Q (optimized): {r['optimized']}")
        print(f"  Ket qua      : {verdict}   [{mark}]")
        if r["counterexample"]:
            print(f"  Phan vi du   : {r['counterexample']}")
        print(f"  Ghi chu      : {r['note']}\n")


if __name__ == "__main__":
    dataset_path = os.path.join(BASE, "dataset", "condition_pairs.json")
    results = run_all(dataset_path)
    print_report(results)

    out_path = os.path.join(BASE, "dataset", "smt_verification_results.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2)
    n_ok = sum(1 for r in results if r["matches_expectation"])
    print(f"Tong ket: {n_ok}/{len(results)} cap dieu kien khop du doan ky vong.")
    print(f"Ket qua da luu: {out_path}")
