import sys, os, shutil
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

import pytest
from atpm.ch4_dynamic_analysis.fuzzing import (
    compile_with_coverage, black_box_fuzz, coverage_guided_fuzz,
    measure_coverage_percent, _reset_coverage_counters,
)

WORK_DIR = os.path.join(os.path.dirname(__file__), "_ch4_work")
SRC = os.path.join(os.path.dirname(__file__), "..", "..", "c", "ch4_dynamic", "fuzz_target.c")


def _gcc_available():
    return shutil.which("gcc") is not None


@pytest.fixture(scope="module")
def compiled_binary():
    if not _gcc_available():
        pytest.skip("gcc khong duoc cai dat")
    os.makedirs(WORK_DIR, exist_ok=True)
    binary = os.path.join(WORK_DIR, "_fuzz_target_bin")
    compile_with_coverage(SRC, binary)
    yield binary
    shutil.rmtree(WORK_DIR, ignore_errors=True)


def test_coverage_increases_with_partial_magic_match(compiled_binary):
    from atpm.ch4_dynamic_analysis.fuzzing import _run_target
    _reset_coverage_counters(WORK_DIR)
    coverages = []
    for inp in [b"A", b"F", b"FU", b"FUZ"]:
        _run_target(compiled_binary, inp, WORK_DIR)
        coverages.append(measure_coverage_percent("fuzz_target.c", WORK_DIR))
    # Coverage phai tang DAN (moi byte khop them mo ra mot nhanh moi)
    assert coverages == sorted(coverages)
    assert coverages[-1] > coverages[0]


def test_black_box_fuzz_fails_within_small_budget(compiled_binary):
    # Voi ngan sach nho (1000 lan thu ngau nhien 4 byte), xac suat tim
    # duoc "FUZZ" chinh xac la khong dang ke (~1/2^32).
    result = black_box_fuzz(compiled_binary, max_iterations=1000, seed=7)
    assert result.crashed is False


def test_coverage_guided_fuzz_finds_crash(compiled_binary):
    result = coverage_guided_fuzz(compiled_binary, "fuzz_target.c",
                                   max_iterations=5000, seed=1)
    assert result.crashed is True
    assert result.crashing_input == b"FUZZ"
    assert result.iterations_used < 5000
