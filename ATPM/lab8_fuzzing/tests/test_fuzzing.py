"""
Lab 8 - Bo test tu dong
=======================
1. Xac nhan fuzz_target chay dung cho cac input binh thuong (khong crash).
2. Xac nhan input crash da biet (ADMIN:X + payload dai) lam ASan bao loi.
3. Xac nhan fuzzer HOP TRANG (coverage-guided) tim duoc it nhat 1 crash
   trong ngan sach 500 lan thu (voi seed co dinh de tai lap duoc).
4. Xac nhan fuzzer HOP DEN, VOI CUNG ngan sach (hoac thap hon), KHONG tim
   duoc crash nao - minh hoa truc tiep uu the cua coverage-guided fuzzing.
"""
import os
import subprocess
import sys

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
C_DIR = os.path.join(BASE, "c")
sys.path.insert(0, os.path.join(BASE, "python"))

from blackbox_fuzzer import fuzz as blackbox_fuzz  # noqa: E402
from whitebox_fuzzer import fuzz as whitebox_fuzz  # noqa: E402


def _build():
    subprocess.run(
        ["gcc", "-g", "-fsanitize=address", "-Wall", "-Wextra", "--coverage",
         "-o", "fuzz_target", "fuzz_target.c"],
        cwd=C_DIR, check=True,
    )


def test_normal_inputs_do_not_crash():
    _build()
    for arg in ["ORDER:5,100000,1", "ADMIN:hello", "UNKNOWNTEXT"]:
        proc = subprocess.run([os.path.join(C_DIR, "fuzz_target"), arg],
                               cwd=C_DIR, capture_output=True, text=True)
        assert proc.returncode == 0, f"Input hop le khong duoc crash: {arg}"


def test_known_crash_input_triggers_asan():
    _build()
    payload = "ADMIN:X" + "A" * 30
    proc = subprocess.run([os.path.join(C_DIR, "fuzz_target"), payload],
                           cwd=C_DIR, capture_output=True, text=True)
    assert proc.returncode != 0
    assert "AddressSanitizer" in proc.stderr


def test_whitebox_fuzzer_finds_crash_within_budget():
    result = whitebox_fuzz(iterations=500, seed=7)
    assert len(result["crashes"]) >= 1, "Fuzzer hop trang phai tim duoc >= 1 crash trong 500 lan thu"
    for c in result["crashes"]:
        assert c["input"].startswith("ADMIN:X")


def test_blackbox_fuzzer_fails_within_same_budget():
    result = blackbox_fuzz(iterations=1000, seed=42)
    assert len(result["crashes"]) == 0, (
        "Fuzzer hop den (khong co coverage feedback) KHONG duoc tim thay crash "
        "trong ngan sach thap (1000 lan) - day la diem minh hoa chinh cua bai thuc hanh"
    )


if __name__ == "__main__":
    tests = [
        test_normal_inputs_do_not_crash, test_known_crash_input_triggers_asan,
        test_whitebox_fuzzer_finds_crash_within_budget, test_blackbox_fuzzer_fails_within_same_budget,
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
