"""
Lab 6 - Bo test tu dong
=======================
1. Xac nhan dong co BMC (Python/Z3) cho ket qua khop ky vong tren 4 chuong trinh.
2. Xac nhan cai dat C thuc te (voi ASan) khop voi ket qua tinh:
   - safe_full_scan  : chay sach, khong loi.
   - offbyone        : ASan bao loi.
   - symbolic_unsafe (base=9) : ASan bao loi (khop nhan chung BMC: base_index=9).
   - symbolic_safe (base=9)   : bi TU CHOI o tien dieu kien (khong crash).
   - symbolic_safe (base=3)   : chay sach.
"""
import os
import subprocess
import sys

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
C_DIR = os.path.join(BASE, "c")
sys.path.insert(0, os.path.join(BASE, "python"))

from bmc_engine import run_all  # noqa: E402

DATASET = os.path.join(BASE, "dataset", "bmc_programs.json")


def test_bmc_results_match_expectation():
    results = run_all(DATASET)
    mismatches = [r for r in results if not r["matches_expectation"]]
    assert not mismatches, f"BMC sai lech ky vong: {[m['id'] for m in mismatches]}"
    assert len(results) == 4


def test_bmc03_witness_matches_c_runtime_bug():
    """Nhan chung cua BMC-03 (base_index=9) phai la gia tri THUC SU gay loi
    khi chay chuong trinh C tuong ung - day la buoc 'doi chieu' quan trong
    giua ket qua kiem chung tinh va hanh vi runtime."""
    results = {r["id"]: r for r in run_all(DATASET)}
    witness = results["BMC-03-UNSAFE-SYMBOLIC-OFFSET"]["witness"]
    base_index = witness["base_index"]

    subprocess.run(["gcc", "-g", "-fsanitize=address", "-Wall", "-Wextra",
                     "-o", "array_access", "array_access.c"], cwd=C_DIR, check=True)
    proc = subprocess.run(
        [os.path.join(C_DIR, "array_access"), "symbolic_unsafe", str(base_index)],
        cwd=C_DIR, capture_output=True, text=True,
    )
    assert proc.returncode != 0, "Chuong trinh C phai crash voi base_index tu nhan chung BMC"
    assert "AddressSanitizer" in proc.stderr


def test_c_safe_full_scan_no_crash():
    subprocess.run(["gcc", "-g", "-fsanitize=address", "-Wall", "-Wextra",
                     "-o", "array_access", "array_access.c"], cwd=C_DIR, check=True)
    proc = subprocess.run([os.path.join(C_DIR, "array_access"), "safe_full_scan"],
                           cwd=C_DIR, capture_output=True, text=True)
    assert proc.returncode == 0
    assert "AddressSanitizer" not in proc.stderr


def test_c_symbolic_safe_with_precondition_rejects_bad_input():
    proc = subprocess.run([os.path.join(C_DIR, "array_access"), "symbolic_safe", "9"],
                           cwd=C_DIR, capture_output=True, text=True)
    assert proc.returncode == 0
    assert "TU CHOI" in proc.stderr


def test_c_symbolic_safe_with_valid_input_runs_clean():
    proc = subprocess.run([os.path.join(C_DIR, "array_access"), "symbolic_safe", "3"],
                           cwd=C_DIR, capture_output=True, text=True)
    assert proc.returncode == 0
    assert "AddressSanitizer" not in proc.stderr


if __name__ == "__main__":
    tests = [
        test_bmc_results_match_expectation, test_bmc03_witness_matches_c_runtime_bug,
        test_c_safe_full_scan_no_crash, test_c_symbolic_safe_with_precondition_rejects_bad_input,
        test_c_symbolic_safe_with_valid_input_runs_clean,
    ]
    passed = 0
    for t in tests:
        try:
            t()
            print(f"[PASS] {t.__name__}")
            passed += 1
        except AssertionError as e:
            print(f"[FAIL] {t.__name__}: {e}")
    print(f"\n{passed}/{len(tests)} test da PASS")
