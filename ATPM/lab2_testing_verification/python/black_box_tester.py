"""
Lab 2 - Sinh test hop den (Black-box Testing) bang Equivalence Partitioning
va Boundary Value Analysis, sau do chay qua thu vien C (validate_pin) qua
ctypes de kiem tra.

Ky thuat ap dung (theo ISTQB Foundations of Software Testing):
  - Equivalence Partitioning (EP): chia mien dau vao thanh cac lop tuong
    duong (vd: dung do dai/chu so vs sai do dai vs co ky tu khong phai so).
  - Boundary Value Analysis (BVA): kiem tra tai bien (do dai = 5, 6, 7 ky tu).
  - Ket hop voi kiem tra dac thu nghiep vu: PIN tuan tu (sequential) va
    chinh sach khoa tai khoan sau 3 lan sai.
"""
from __future__ import annotations
import ctypes
import csv
import os
import subprocess

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
C_DIR = os.path.join(BASE, "c")
LIB_PATH = os.path.join(C_DIR, "libpinvalidator.so")

RESULT_NAMES = {
    0: "OK",
    1: "WRONG_LENGTH",
    2: "NON_DIGIT",
    3: "SEQUENTIAL_WEAK",
    4: "ACCOUNT_LOCKED",
    5: "NULL_INPUT",
}


def build_shared_lib():
    subprocess.run(
        ["gcc", "-shared", "-fPIC", "-o", "libpinvalidator.so", "pin_validator.c"],
        cwd=C_DIR, check=True,
    )


def load_lib():
    if not os.path.exists(LIB_PATH):
        build_shared_lib()
    lib = ctypes.CDLL(LIB_PATH)
    lib.validate_pin.argtypes = [ctypes.c_char_p, ctypes.c_int]
    lib.validate_pin.restype = ctypes.c_int
    return lib


def generate_black_box_cases() -> list[dict]:
    """Sinh test case theo Equivalence Partitioning + Boundary Value Analysis.
    Tra ve danh sach dict {pin, failed_attempts, expected, technique, rationale}."""
    cases = []

    def add(pin, failed, expected, technique, rationale):
        cases.append({"pin": pin, "failed_attempts": failed, "expected": expected,
                       "technique": technique, "rationale": rationale})

    # --- Equivalence Partitioning: lop hop le ---
    add("482913", 0, "OK", "EP-valid", "PIN 6 so, khong tuan tu -> hop le")
    add("739284", 1, "OK", "EP-valid", "PIN hop le, con duoi nguong khoa (1 lan sai)")

    # --- EP: lop sai do dai ---
    add("482", 0, "WRONG_LENGTH", "EP-invalid-length", "Qua ngan (< 6 ky tu)")
    add("48291378", 0, "WRONG_LENGTH", "EP-invalid-length", "Qua dai (> 6 ky tu)")
    add("", 0, "WRONG_LENGTH", "EP-invalid-length", "Chuoi rong")

    # --- BVA: bien do dai (5, 6, 7 ky tu) ---
    add("48291", 0, "WRONG_LENGTH", "BVA-length-boundary", "Bien duoi: 5 ky tu (thieu 1)")
    add("482913", 0, "OK", "BVA-length-boundary", "Dung bien: 6 ky tu")
    add("4829137", 0, "WRONG_LENGTH", "BVA-length-boundary", "Bien tren: 7 ky tu (thua 1)")

    # --- EP: lop ky tu khong phai chu so ---
    add("48a913", 0, "NON_DIGIT", "EP-invalid-charclass", "Chua ky tu chu")
    add("48 913", 0, "NON_DIGIT", "EP-invalid-charclass", "Chua khoang trang")
    add("-89213", 0, "NON_DIGIT", "EP-invalid-charclass", "Chua dau '-'")

    # --- Nghiep vu: PIN tuan tu (weak) ---
    add("123456", 0, "SEQUENTIAL_WEAK", "business-rule", "Day tang lien tiep")
    add("654321", 0, "SEQUENTIAL_WEAK", "business-rule", "Day giam lien tiep")
    add("112233", 0, "OK", "business-rule", "Lap cap so nhung KHONG tuan tu -> hop le")

    # --- Nghiep vu: khoa tai khoan (BVA tren so lan sai: 2,3,4) ---
    add("482913", 2, "OK", "BVA-lockout-boundary", "Bien duoi: 2 lan sai truoc -> chua khoa")
    add("482913", 3, "ACCOUNT_LOCKED", "BVA-lockout-boundary", "Dung bien: lan sai thu 3 -> khoa")
    add("482913", 4, "ACCOUNT_LOCKED", "BVA-lockout-boundary", "Tren bien: van khoa")

    # --- Dau vao dac biet ---
    add("NULL", 0, "NULL_INPUT", "special-value", "Con tro NULL (kiem thu phong thu - defensive)")

    return cases


def run_black_box_suite(save_csv: str | None = None) -> tuple[int, int, list]:
    lib = load_lib()
    cases = generate_black_box_cases()
    passed = 0
    detail = []
    for c in cases:
        pin_arg = None if c["pin"] == "NULL" else c["pin"].encode("utf-8")
        code = lib.validate_pin(pin_arg, c["failed_attempts"])
        actual = RESULT_NAMES.get(code, f"UNKNOWN({code})")
        ok = (actual == c["expected"])
        passed += int(ok)
        detail.append({**c, "actual": actual, "pass": ok})

    if save_csv:
        with open(save_csv, "w", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=["pin", "failed_attempts", "expected",
                                               "technique", "rationale", "actual", "pass"])
            w.writeheader()
            w.writerows(detail)

    return passed, len(cases), detail


if __name__ == "__main__":
    out_csv = os.path.join(BASE, "dataset", "black_box_test_results.csv")
    passed, total, detail = run_black_box_suite(save_csv=out_csv)
    for d in detail:
        status = "PASS" if d["pass"] else "FAIL"
        print(f"[{status}] ({d['technique']:<22}) pin={d['pin']:<8} "
              f"failed={d['failed_attempts']} expected={d['expected']:<16} actual={d['actual']}")
    print(f"\nTong: {passed}/{total} test PASS")
    print(f"Ket qua da luu: {out_csv}")
