import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

import pytest
from atpm.ch2_secure_c.static_check import (
    run_cppcheck, scan_directory, CppcheckNotFoundError, CppcheckReport
)

C_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "c", "ch2_memory")


def _cppcheck_available():
    import shutil
    return shutil.which("cppcheck") is not None


@pytest.mark.skipif(not _cppcheck_available(), reason="cppcheck khong duoc cai dat")
def test_scan_vulnerable_c_finds_known_bugs():
    report = scan_directory(C_DIR, pattern="vulnerable.c")
    ids = {f.check_id for f in report.findings}
    assert "deallocuse" in ids       # use-after-free
    assert "doubleFree" in ids       # double free
    assert "memleak" in ids          # memory leak
    assert "nullPointer" in ids      # null pointer dereference


@pytest.mark.skipif(not _cppcheck_available(), reason="cppcheck khong duoc cai dat")
def test_fixed_c_has_no_error_severity_findings():
    report = scan_directory(C_DIR, pattern="fixed.c")
    errors = [f for f in report.findings if f.severity == "error"]
    assert errors == [], f"fixed.c khong duoc co loi severity=error, nhung tim thay: {errors}"


def test_empty_xml_returns_empty_report():
    from atpm.ch2_secure_c.static_check import _parse_cppcheck_xml
    report = _parse_cppcheck_xml("")
    assert report.findings == []
    assert report.summary() == "Khong co canh bao nao duoc phat hien."


def test_report_summary_counts_by_severity():
    report = CppcheckReport()
    from atpm.ch2_secure_c.static_check import CppcheckFinding
    report.findings = [
        CppcheckFinding("a.c", 1, "error", None, "msg1", "id1"),
        CppcheckFinding("a.c", 2, "warning", None, "msg2", "id2"),
        CppcheckFinding("a.c", 3, "warning", None, "msg3", "id3"),
    ]
    counts = report.by_severity()
    assert counts["error"] == 1
    assert counts["warning"] == 2
