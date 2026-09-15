"""
Lab 7 - Do Do phu Ma (Code Coverage) va Theo doi Thuc thi Chuong trinh
=========================================================================
Chuong 4: 4.2 Phu ma nguon (code coverage), 4.3 Theo doi thuc thi chuong trinh,
          4.4 Tao duong thuc thi chuong trinh

Module nay:
  1. Bien dich order_processor.c voi co --coverage (sinh .gcno/.gcda).
  2. Chay CLI voi mot bo du lieu test dau vao (CSV), thu duoc PASS/FAIL cho
     tung test case.
  3. Chay gcov de tinh % dong lenh duoc thuc thi (statement coverage) VA
     bao cao cac dong CHUA duoc phu (candidate cho "duong thuc thi" chua
     duoc kiem thu - la muc tieu chinh cua 4.4).
  4. So sanh coverage giua bo test "partial" (khong day du) va "full" (day
     du) de minh hoa truc quan tam quan trong cua thiet ke test co y thuc
     ve cau truc nhanh, khong chi dua vao "test cho co PASS".
"""
from __future__ import annotations
import os
import re
import subprocess

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
C_DIR = os.path.join(BASE, "c")


def clean_coverage_artifacts():
    for fname in os.listdir(C_DIR):
        if fname.endswith((".gcda", ".gcno", ".gcov", ".o")) or fname == "order_processor_cli":
            os.remove(os.path.join(C_DIR, fname))


def build_instrumented():
    subprocess.run(
        ["gcc", "-Wall", "-Wextra", "-std=c11", "--coverage", "-DORDER_CLI_MAIN",
         "-c", "order_processor.c", "-o", "order_processor.o"],
        cwd=C_DIR, check=True,
    )
    subprocess.run(
        ["gcc", "--coverage", "-o", "order_processor_cli", "order_processor.o"],
        cwd=C_DIR, check=True,
    )


def run_test_suite(csv_path: str) -> str:
    proc = subprocess.run(
        [os.path.join(C_DIR, "order_processor_cli"), csv_path],
        cwd=C_DIR, capture_output=True, text=True,
    )
    return proc.stdout


def compute_coverage() -> dict:
    subprocess.run(["gcov", "order_processor.c"], cwd=C_DIR, capture_output=True, text=True)
    gcov_file = os.path.join(C_DIR, "order_processor.c.gcov")
    total = covered = 0
    uncovered = []
    if os.path.exists(gcov_file):
        with open(gcov_file, encoding="utf-8", errors="ignore") as f:
            for raw in f:
                m = re.match(r"\s*(\S+):\s*(\d+):(.*)", raw)
                if not m:
                    continue
                count_str, lineno, code = m.groups()
                if count_str in ("-", ""):
                    continue
                total += 1
                if count_str == "#####":
                    uncovered.append((int(lineno), code.strip()))
                else:
                    covered += 1
    pct = round(100.0 * covered / total, 2) if total else 0.0
    return {"lines_total": total, "lines_covered": covered,
            "coverage_percent": pct, "uncovered_lines": uncovered}


def evaluate_suite(csv_name: str) -> dict:
    clean_coverage_artifacts()
    build_instrumented()
    csv_path = os.path.join(BASE, "dataset", csv_name)
    output = run_test_suite(csv_path)
    cov = compute_coverage()
    return {"suite": csv_name, "test_output": output, "coverage": cov}


if __name__ == "__main__":
    print("############ BO TEST 'PARTIAL' (khong day du) ############")
    partial = evaluate_suite("order_test_inputs_partial.csv")
    print(partial["test_output"])
    print(f"Coverage: {partial['coverage']['coverage_percent']}% "
          f"({partial['coverage']['lines_covered']}/{partial['coverage']['lines_total']} dong)")
    if partial["coverage"]["uncovered_lines"]:
        print("Cac dong CHUA duoc kiem thu:")
        for lineno, code in partial["coverage"]["uncovered_lines"]:
            print(f"  Dong {lineno}: {code}")

    print("\n############ BO TEST 'FULL' (day du) ############")
    full = evaluate_suite("order_test_inputs_full.csv")
    print(full["test_output"])
    print(f"Coverage: {full['coverage']['coverage_percent']}% "
          f"({full['coverage']['lines_covered']}/{full['coverage']['lines_total']} dong)")
    if full["coverage"]["uncovered_lines"]:
        print("Cac dong CHUA duoc kiem thu:")
        for lineno, code in full["coverage"]["uncovered_lines"]:
            print(f"  Dong {lineno}: {code}")

    print(f"\n=== SO SANH: partial={partial['coverage']['coverage_percent']}%  "
          f"vs  full={full['coverage']['coverage_percent']}% ===")

    import json
    out = {"partial": {"coverage": partial["coverage"]}, "full": {"coverage": full["coverage"]}}
    out_path = os.path.join(BASE, "dataset", "coverage_comparison_results.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    print(f"Ket qua da luu: {out_path}")
