"""
Lab 2 - Kiem thu hop trang (White-box Testing): do phu nhanh (branch coverage)
cho ham validate_pin() bang gcc --coverage + gcov.

Y tuong su pham: sinh vien se thay bo test hop den (Equivalence Partitioning +
BVA) o black_box_tester.py co dat duoc 100% branch coverage hay khong. Neu
chua, phai bo sung them test case hop trang dua tren cau truc code (vi du
nhanh failed_attempts >= 3 PHAI duoc kich hoat truoc nhanh do dai/chu so
de dam bao thu tu uu tien kiem tra dung dac ta).
"""
from __future__ import annotations
import os
import re
import subprocess

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
C_DIR = os.path.join(BASE, "c")


def build_instrumented_cli():
    """Bien dich pin_validator voi co --coverage (sinh file .gcno) va driver
    CLI (PIN_CLI_MAIN) doc truc tiep tu file test-case CSV.
    Luu y: bien dich theo 2 buoc (compile -> link) de file .gcno/.gcda mang
    ten 'pin_validator.gcno/.gcda' (khop voi 'gcov pin_validator.c'), thay vi
    bi gcc dat ten theo binary dau ra khi compile+link trong 1 lenh."""
    subprocess.run(
        ["gcc", "-Wall", "-Wextra", "-std=c11", "--coverage", "-DPIN_CLI_MAIN",
         "-c", "pin_validator.c", "-o", "pin_validator.o"],
        cwd=C_DIR, check=True,
    )
    subprocess.run(
        ["gcc", "--coverage", "-o", "pin_validator_cli", "pin_validator.o"],
        cwd=C_DIR, check=True,
    )


def run_cli_with_dataset(csv_path: str) -> str:
    proc = subprocess.run(
        [os.path.join(C_DIR, "pin_validator_cli"), csv_path],
        cwd=C_DIR, capture_output=True, text=True,
    )
    return proc.stdout


def compute_branch_coverage() -> dict:
    """Chay gcov, phan tich phan tram branch coverage cho pin_validator.c."""
    subprocess.run(["gcov", "pin_validator.c"], cwd=C_DIR, capture_output=True, text=True)
    gcov_file = os.path.join(C_DIR, "pin_validator.c.gcov")
    lines_total = lines_covered = 0
    uncovered_lines = []
    if os.path.exists(gcov_file):
        with open(gcov_file, "r", encoding="utf-8", errors="ignore") as f:
            for raw in f:
                m = re.match(r"\s*(\S+):\s*(\d+):(.*)", raw)
                if not m:
                    continue
                count_str, lineno, code = m.group(1), m.group(2), m.group(3)
                if count_str in ("-", ""):
                    continue  # dong khong the thuc thi (khai bao, comment)
                lines_total += 1
                if count_str == "#####":
                    uncovered_lines.append((int(lineno), code.strip()))
                else:
                    lines_covered += 1
    pct = (100.0 * lines_covered / lines_total) if lines_total else 0.0
    return {
        "lines_total": lines_total,
        "lines_covered": lines_covered,
        "coverage_percent": round(pct, 2),
        "uncovered_lines": uncovered_lines,
    }


def clean_coverage_artifacts():
    for ext in (".gcda", ".gcno", ".gcov"):
        p = os.path.join(C_DIR, f"pin_validator{ext}")
        if os.path.exists(p):
            os.remove(p)


if __name__ == "__main__":
    from black_box_tester import generate_black_box_cases
    import csv as csvmod

    clean_coverage_artifacts()
    build_instrumented_cli()

    # Ghi lai dataset hop den ra CSV dung dinh dang CLI mong doi
    tmp_csv = os.path.join(BASE, "dataset", "pin_test_cases.csv")
    with open(tmp_csv, "w", newline="", encoding="utf-8") as f:
        w = csvmod.writer(f)
        w.writerow(["pin", "failed_attempts", "expected"])
        for c in generate_black_box_cases():
            pin_field = "EMPTYSTR" if c["pin"] == "" else c["pin"]
            w.writerow([pin_field, c["failed_attempts"], c["expected"]])

    output = run_cli_with_dataset(tmp_csv)
    print(output)

    cov = compute_branch_coverage()
    print("=== KET QUA DO PHU MA (Statement Coverage qua gcov) ===")
    print(f"So dong co the thuc thi : {cov['lines_total']}")
    print(f"So dong da duoc chay    : {cov['lines_covered']}")
    print(f"Ty le phu (coverage)    : {cov['coverage_percent']}%")
    if cov["uncovered_lines"]:
        print("\nCac dong CHUA duoc kiem thu (can bo sung test case hop trang):")
        for lineno, code in cov["uncovered_lines"]:
            print(f"  Dong {lineno}: {code}")
    else:
        print("\n=> Dat 100% coverage voi bo test hop den hien tai.")
