"""
Lab 3 - Bo test tu dong
=======================
1. Bien dich ca 2 phien ban (vulnerable / secure) voi AddressSanitizer.
2. Xac nhan vulnerable_inventory PHAT HIEN du 3 loai loi (overflow/uaf/leak).
3. Xac nhan secure_inventory KHONG co bat ky loi nao trong ca 3 kich ban.
4. Xac nhan risk_assessor.py cho ra ket qua khop voi baseline da luu.
"""
import json
import os
import subprocess
import sys

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
C_DIR = os.path.join(BASE, "c")
sys.path.insert(0, os.path.join(BASE, "python"))

from risk_assessor import assess_all  # noqa: E402


def _build(src_name: str, bin_name: str):
    subprocess.run(
        ["gcc", "-g", "-fsanitize=address", "-Wall", "-Wextra", "-o", bin_name, src_name],
        cwd=C_DIR, check=True,
    )


def _run(bin_name: str, args: list[str]):
    return subprocess.run(
        [os.path.join(C_DIR, bin_name)] + args,
        cwd=C_DIR, capture_output=True, text=True,
    )


def setup_module(_module=None):
    _build("vulnerable_inventory.c", "vulnerable_inventory")
    _build("secure_inventory.c", "secure_inventory")


def test_vulnerable_overflow_detected():
    proc = _run("vulnerable_inventory", ["overflow", "A" * 39])
    assert proc.returncode != 0
    assert "heap-buffer-overflow" in proc.stderr


def test_vulnerable_uaf_detected():
    proc = _run("vulnerable_inventory", ["uaf"])
    assert proc.returncode != 0
    assert "heap-use-after-free" in proc.stderr


def test_vulnerable_leak_detected():
    env = dict(os.environ, ASAN_OPTIONS="detect_leaks=1")
    proc = subprocess.run(
        [os.path.join(C_DIR, "vulnerable_inventory"), "leak"],
        cwd=C_DIR, capture_output=True, text=True, env=env,
    )
    assert "leaked" in proc.stderr


def test_secure_overflow_clean():
    proc = _run("secure_inventory", ["overflow", "A" * 39])
    assert proc.returncode == 0
    assert "ERROR" not in proc.stderr


def test_secure_uaf_clean():
    proc = _run("secure_inventory", ["uaf"])
    assert proc.returncode == 0
    assert "ERROR" not in proc.stderr


def test_secure_leak_clean():
    env = dict(os.environ, ASAN_OPTIONS="detect_leaks=1")
    proc = subprocess.run(
        [os.path.join(C_DIR, "secure_inventory"), "leak"],
        cwd=C_DIR, capture_output=True, text=True, env=env,
    )
    assert proc.returncode == 0
    assert "leaked" not in proc.stderr


def test_risk_assessment_matches_baseline():
    results = assess_all(os.path.join(BASE, "dataset", "vulnerabilities.json"))
    with open(os.path.join(BASE, "dataset", "expected_risk_baseline.json"), encoding="utf-8") as f:
        expected = json.load(f)
    assert len(results) == len(expected)
    for r, e in zip(results, expected):
        assert r["id"] == e["id"]
        assert r["severity"] == e["severity"]
        assert abs(r["base_score"] - e["base_score"]) < 0.05


def test_critical_vuln_is_the_buffer_overflow():
    results = assess_all(os.path.join(BASE, "dataset", "vulnerabilities.json"))
    top = results[0]
    assert top["severity"] == "CRITICAL"
    assert top["cwe"] == "CWE-121"


if __name__ == "__main__":
    setup_module()
    tests = [
        test_vulnerable_overflow_detected, test_vulnerable_uaf_detected,
        test_vulnerable_leak_detected, test_secure_overflow_clean,
        test_secure_uaf_clean, test_secure_leak_clean,
        test_risk_assessment_matches_baseline, test_critical_vuln_is_the_buffer_overflow,
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
