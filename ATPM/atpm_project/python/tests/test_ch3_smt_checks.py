import sys, os, shutil
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

import pytest
from atpm.ch3_static_analysis.smt_checks import (
    check_index_bounds, check_integer_overflow_multiply,
    check_division_safety, run_cbmc,
)

C_FILE = os.path.join(os.path.dirname(__file__), "..", "..", "c", "ch3_static", "bmc_demo.c")


def _cbmc_available():
    return shutil.which("cbmc") is not None


# ---- Z3 / SMT tests (khong can tool ngoai) ----

def test_index_bounds_finds_violation_without_check():
    result = check_index_bounds(0, 64, assume_checked=False)
    assert result.property_holds is False
    assert result.counterexample is not None


def test_index_bounds_safe_with_check():
    result = check_index_bounds(0, 64, assume_checked=True)
    assert result.property_holds is True
    assert result.counterexample is None


def test_integer_overflow_multiply_detected():
    result = check_integer_overflow_multiply(bits=32, elem_size=4)
    assert result.property_holds is False


def test_division_safety_without_guard():
    result = check_division_safety(assume_nonzero=False)
    assert result.property_holds is False


def test_division_safety_with_guard():
    result = check_division_safety(assume_nonzero=True)
    assert result.property_holds is True


# ---- CBMC tests (skip neu cbmc khong cai dat) ----

@pytest.mark.skipif(not _cbmc_available(), reason="cbmc khong duoc cai dat")
def test_cbmc_check_sum_succeeds_with_enough_unwind():
    r = run_cbmc(C_FILE, "check_sum", unwind=6)
    assert r.verification_successful is True


@pytest.mark.skipif(not _cbmc_available(), reason="cbmc khong duoc cai dat")
def test_cbmc_detects_off_by_one_overflow():
    r = run_cbmc(C_FILE, "check_overflow",
                 extra_flags=["--bounds-check", "--pointer-check"], unwind=12)
    assert r.verification_successful is False
    assert any("pointer_dereference" in p for p in r.failed_properties)


@pytest.mark.skipif(not _cbmc_available(), reason="cbmc khong duoc cai dat")
def test_cbmc_fixed_version_passes():
    r = run_cbmc(C_FILE, "check_overflow_fixed",
                 extra_flags=["--bounds-check", "--pointer-check"], unwind=12)
    assert r.verification_successful is True


@pytest.mark.skipif(not _cbmc_available(), reason="cbmc khong duoc cai dat")
def test_cbmc_detects_division_by_zero():
    r = run_cbmc(C_FILE, "check_divide", extra_flags=["--div-by-zero-check"], unwind=5)
    assert r.verification_successful is False
    assert any("division-by-zero" in p for p in r.failed_properties)


@pytest.mark.skipif(not _cbmc_available(), reason="cbmc khong duoc cai dat")
def test_cbmc_guarded_division_passes():
    r = run_cbmc(C_FILE, "check_divide_guarded", extra_flags=["--div-by-zero-check"], unwind=5)
    assert r.verification_successful is True
