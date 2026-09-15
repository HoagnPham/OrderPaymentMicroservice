"""
Lab 4 - Bo test tu dong
=======================
1. Xac nhan ca 2 file C bien dich thanh cong va chay dung logic.
2. Xac nhan cppcheck phat hien >=2 finding bao mat tren ban buggy.
3. Xac nhan cppcheck khong con finding bao mat nao tren ban fixed.
4. Doi chieu voi baseline da luu.
"""
import json
import os
import subprocess
import sys

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
C_DIR = os.path.join(BASE, "c")
sys.path.insert(0, os.path.join(BASE, "python"))

from static_analysis_runner import compare_versions, security_relevant  # noqa: E402


def test_buggy_compiles_and_runs():
    subprocess.run(["gcc", "-Wall", "-Wextra", "-o", "booklist_v1", "booklist_v1_buggy.c"],
                    cwd=C_DIR, check=True)
    proc = subprocess.run([os.path.join(C_DIR, "booklist_v1")], capture_output=True, text=True)
    assert proc.returncode == 0
    assert "Refactoring" in proc.stdout


def test_fixed_compiles_and_runs_correct_count():
    subprocess.run(["gcc", "-Wall", "-Wextra", "-o", "booklist_v2", "booklist_v2_fixed.c"],
                    cwd=C_DIR, check=True)
    proc = subprocess.run([os.path.join(C_DIR, "booklist_v2")], capture_output=True, text=True)
    assert proc.returncode == 0
    assert "So luong sach: 3" in proc.stdout
    assert "So luong sach: 2" in proc.stdout   # sau khi xoa 1 cuon


def test_cppcheck_detects_issues_in_buggy_version():
    result = compare_versions("booklist_v1_buggy.c", "booklist_v2_fixed.c")
    assert result["buggy_security_count"] >= 2, "cppcheck phai phat hien >= 2 loi bao mat o ban buggy"


def test_cppcheck_clean_on_fixed_version():
    result = compare_versions("booklist_v1_buggy.c", "booklist_v2_fixed.c")
    assert result["fixed_security_count"] == 0, "Ban da sua khong duoc con finding bao mat nao"


def test_matches_baseline():
    result = compare_versions("booklist_v1_buggy.c", "booklist_v2_fixed.c")
    with open(os.path.join(BASE, "dataset", "expected_cppcheck_baseline.json"), encoding="utf-8") as f:
        expected = json.load(f)
    assert result["buggy_security_count"] == expected["buggy_security_count"]
    assert result["fixed_security_count"] == expected["fixed_security_count"]
    buggy_ids = {f["id"] for f in security_relevant(result["buggy_findings"])}
    expected_ids = {f["id"] for f in expected["buggy_findings"] if f["severity"] in ("error", "warning")}
    assert buggy_ids == expected_ids


if __name__ == "__main__":
    tests = [
        test_buggy_compiles_and_runs, test_fixed_compiles_and_runs_correct_count,
        test_cppcheck_detects_issues_in_buggy_version, test_cppcheck_clean_on_fixed_version,
        test_matches_baseline,
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
