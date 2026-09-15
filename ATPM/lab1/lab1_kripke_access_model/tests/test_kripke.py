"""
Lab 1 - Bo test tu dong
=======================
Kiem tra rang:
  1) Mo hinh SAFE thoa man ca 3 thuoc tinh an toan.
  2) Mo hinh BUGGY phat hien VI PHAM thuoc tinh P1 (unlock khong xac thuc),
     va tao ra duong di phan chung (counterexample) chinh xac.
  3) Chuong trinh C mo phong (compiled binary) tao canh bao runtime tuong ung.

Chay: python3 -m pytest tests/test_kripke.py -v
      hoac: python3 tests/test_kripke.py
"""
import json
import os
import subprocess
import sys

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(BASE, "python"))

from kripke_model import run_verification  # noqa: E402

DATASET = os.path.join(BASE, "dataset")
C_BIN = os.path.join(BASE, "c", "access_simulator")


def test_safe_model_all_properties_hold():
    result = run_verification(os.path.join(DATASET, "access_system_model.json"), verbose=False)
    for prop in result["properties"]:
        assert prop["holds"] is True, f"Thuoc tinh {prop['id']} phai THOA MAN tren mo hinh SAFE"


def test_buggy_model_violates_p1():
    result = run_verification(os.path.join(DATASET, "access_system_model_BUGGY.json"), verbose=False)
    props = {p["id"]: p for p in result["properties"]}
    assert props["P1_SAFETY_NO_UNLOCK_WITHOUT_AUTH"]["holds"] is False, \
        "Mo hinh BUGGY phai VI PHAM thuoc tinh P1"
    witness = props["P1_SAFETY_NO_UNLOCK_WITHOUT_AUTH"]["witness_states"]
    assert witness[0] == "S0" and witness[-1] == "S5", \
        f"Duong di phan chung khong dung: {witness}"


def test_buggy_model_reachability_still_holds():
    result = run_verification(os.path.join(DATASET, "access_system_model_BUGGY.json"), verbose=False)
    props = {p["id"]: p for p in result["properties"]}
    assert props["P3_REACHABILITY_UNLOCK"]["holds"] is True


def _ensure_c_binary_built():
    if not os.path.exists(C_BIN):
        subprocess.run(
            ["gcc", "-Wall", "-Wextra", "-std=c11", "-o", "access_simulator", "access_simulator.c"],
            cwd=os.path.join(BASE, "c"),
            check=True,
        )


def test_c_simulator_detects_runtime_violation():
    _ensure_c_binary_built()
    proc = subprocess.run(
        [C_BIN,
         os.path.join(DATASET, "access_buggy_states.csv"),
         os.path.join(DATASET, "access_buggy_transitions.csv"),
         os.path.join(DATASET, "access_buggy_init.txt"),
         "insert_card", "timeout_glitch"],
        capture_output=True, text=True, check=True,
    )
    assert "CANH BAO AN TOAN" in proc.stdout
    assert "vi pham thuoc tinh P1" in proc.stdout


def test_c_simulator_safe_sequence_no_warning():
    _ensure_c_binary_built()
    proc = subprocess.run(
        [C_BIN,
         os.path.join(DATASET, "access_safe_states.csv"),
         os.path.join(DATASET, "access_safe_transitions.csv"),
         os.path.join(DATASET, "access_safe_init.txt"),
         "insert_card", "auth_success", "door_closed_timeout"],
        capture_output=True, text=True, check=True,
    )
    assert "CANH BAO AN TOAN" not in proc.stdout


if __name__ == "__main__":
    tests = [
        test_safe_model_all_properties_hold,
        test_buggy_model_violates_p1,
        test_buggy_model_reachability_still_holds,
        test_c_simulator_detects_runtime_violation,
        test_c_simulator_safe_sequence_no_warning,
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
