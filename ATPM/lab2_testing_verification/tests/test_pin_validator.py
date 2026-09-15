"""
Lab 2 - Bo test tu dong: kiem tra bo test hop den PASS 100% va do phu ma
dat nguong toi thieu da dinh (>= 90%).
"""
import os
import sys

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(BASE, "python"))

from black_box_tester import run_black_box_suite  # noqa: E402
from white_box_coverage import (  # noqa: E402
    build_instrumented_cli, compute_branch_coverage, clean_coverage_artifacts,
    run_cli_with_dataset,
)

MIN_COVERAGE_PCT = 90.0


def test_black_box_suite_all_pass():
    passed, total, detail = run_black_box_suite()
    failed = [d for d in detail if not d["pass"]]
    assert not failed, f"Cac test hop den bi FAIL: {failed}"
    assert passed == total == 18


def test_white_box_coverage_meets_threshold():
    clean_coverage_artifacts()
    build_instrumented_cli()
    csv_path = os.path.join(BASE, "dataset", "pin_test_cases.csv")
    assert os.path.exists(csv_path), "Chay white_box_coverage.py truoc de sinh dataset CSV"
    run_cli_with_dataset(csv_path)
    cov = compute_branch_coverage()
    assert cov["coverage_percent"] >= MIN_COVERAGE_PCT, (
        f"Do phu ma {cov['coverage_percent']}% thap hon nguong yeu cau {MIN_COVERAGE_PCT}%"
    )


def test_account_lockout_boundary():
    """Kiem tra rieng bien 3 lan sai = khoa tai khoan (BVA)."""
    import ctypes
    from black_box_tester import load_lib
    lib = load_lib()
    assert lib.validate_pin(b"482913", 2) == 0        # OK
    assert lib.validate_pin(b"482913", 3) == 4         # ACCOUNT_LOCKED
    _ = ctypes  # tranh canh bao unused import


if __name__ == "__main__":
    tests = [test_black_box_suite_all_pass, test_white_box_coverage_meets_threshold,
              test_account_lockout_boundary]
    passed = 0
    for t in tests:
        try:
            t()
            print(f"[PASS] {t.__name__}")
            passed += 1
        except AssertionError as e:
            print(f"[FAIL] {t.__name__}: {e}")
    print(f"\n{passed}/{len(tests)} test da PASS")
