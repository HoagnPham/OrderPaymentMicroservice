"""
Lab 6 - Dong co Bounded Model Checking (BMC) tu viet, dung Z3
================================================================
Chuong 3: 3.5 Kiem chung mo hinh phan mem (Bounded Model Checking),
          3.6 Mo hinh bo nho, 3.7 Sap xep mo hinh bo nho

Y tuong BMC (Bounded Model Checking):
  1. "Mo cuon" (unwind) vong lap toi da k buoc (k = "bound").
  2. Tai moi buoc i = 0..k-1, sinh RA MOT BAN SAO cac dieu kien duong di
     (path condition) va cac assertion (khang dinh an toan, o day la
     "0 <= index < array_size") duoi dang cong thuc SMT.
  3. Dung SMT solver kiem tra: TON TAI mot phep gan gia tri bien dau vao
     (o day: base_index) sao cho MOT assertion nao do bi VI PHAM hay khong?
     - SAT  => tim thay loi (BUG), solver tra ve counterexample cu the.
     - UNSAT (voi TOAN BO cac buoc unwind) => AN TOAN TRONG PHAM VI BOUND
       da cho - LUU Y: day KHONG phai chung minh an toan tuyet doi cho MOI
       so lan lap, chi la "an toan cho toi k buoc dau tien". Day la GIOI
       HAN CO BAN cua BMC, khac voi model checking day du (unbounded).

Mo hinh bo nho (memory model) don gian dung o day: mang catalog duoc bieu
dien tuong trung boi khoang gia tri hop le [0, array_size), va viec "truy
cap catalog[index]" duoc mo hinh hoa boi rang buoc "index phai thuoc
khoang nay" - day la mot dang don gian hoa cua Z3 Array theory (co the mo
rong dung z3.Array that su neu can mo hinh hoa gia tri phan tu, khong chi
tinh hop le cua chi so).
"""
from __future__ import annotations
import json
import os
from z3 import Int, Solver, sat, unsat, And, Not

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def run_bmc(program: dict) -> dict:
    array_size = program["array_size"]
    unwind_bound = program["unwind_bound"]
    symbolic_base = program["symbolic_base"]
    precondition_max_base = program.get("precondition_max_base")

    for i in range(unwind_bound):
        solver = Solver()

        if symbolic_base:
            base_index = Int("base_index")
            solver.add(base_index >= 0, base_index < array_size)  # gia dinh dau vao hop le
            if precondition_max_base is not None:
                # Tien dieu kien bo sung: gioi han base_index de dam bao an toan
                # trong pham vi unwind (day la "fix" tuong duong voi kiem tra
                # rang buoc truoc vong lap trong code C that).
                solver.add(base_index <= precondition_max_base)
        else:
            base_index = program["fixed_base"]

        index_expr = base_index + i  # buoc unwind thu i: index = base_index + i
        assertion_safe = And(index_expr >= 0, index_expr < array_size)

        # BMC hoi: TON TAI phep gan nao lam assertion SAI (vi pham) khong?
        solver.add(Not(assertion_safe))
        result = solver.check()

        if result == sat:
            model = solver.model()
            witness = {}
            if symbolic_base:
                witness["base_index"] = model[base_index].as_long() if model[base_index] is not None else None
            witness["unwind_step_i"] = i
            witness["index_accessed"] = (
                (witness.get("base_index", base_index) if symbolic_base else base_index) + i
            )
            return {
                "id": program["id"],
                "label": "UNSAFE",
                "violated_at_step": i,
                "witness": witness,
                "explanation": f"Tai buoc unwind i={i}, ton tai gia tri lam truy cap "
                                f"catalog[index] voi index nam ngoai [0, {array_size}).",
            }

    return {
        "id": program["id"],
        "label": "SAFE",
        "violated_at_step": None,
        "witness": None,
        "explanation": f"Khong tim thay vi pham trong {unwind_bound} buoc unwind dau tien "
                        f"(SAFE-UP-TO-BOUND, khong phai chung minh an toan vo han).",
    }


def run_all(dataset_path: str) -> list[dict]:
    with open(dataset_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    results = []
    for prog in data["programs"]:
        r = run_bmc(prog)
        r["expected_label"] = prog["expected_label"]
        r["matches_expectation"] = (r["label"] == prog["expected_label"])
        r["description"] = prog["description"]
        results.append(r)
    return results


def print_report(results: list[dict]):
    for r in results:
        mark = "OK" if r["matches_expectation"] else "SAI LECH KY VONG!"
        print(f"[{r['id']}] {r['description']}")
        print(f"  Ket qua BMC : {r['label']}  (ky vong: {r['expected_label']})  [{mark}]")
        print(f"  Giai thich  : {r['explanation']}")
        if r["witness"]:
            print(f"  Nhan chung  : {r['witness']}")
        print()


if __name__ == "__main__":
    dataset_path = os.path.join(BASE, "dataset", "bmc_programs.json")
    results = run_all(dataset_path)
    print_report(results)

    out_path = os.path.join(BASE, "dataset", "bmc_results.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2)
    n_ok = sum(1 for r in results if r["matches_expectation"])
    print(f"Tong ket: {n_ok}/{len(results)} chuong trinh khop ky vong.")
    print(f"Ket qua da luu: {out_path}")
