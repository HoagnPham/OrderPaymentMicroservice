"""
Lab 4 - Cong cu chay Phan tich Tinh (Static Analysis Runner) dung cppcheck
=============================================================================
Chuong 2: 2.5 Cong cu phan tich tinh tim loi bao mat

cppcheck la mot cong cu phan tich tinh (static analysis) ma nguon mo cho
C/C++, khong can bien dich chuong trinh. No duyet AST/CFG cua ma nguon de
tim cac mau loi da biet (uninitialized variable, null pointer dereference,
memory leak, buffer overflow tiem an, ...) MA KHONG CAN THUC THI chuong
trinh - day la diem khac biet co ban voi kiem thu dong (dynamic testing,
xem Lab 7 va Lab 8).

Module nay:
  1. Chay cppcheck --enable=all --xml tren 1 file C, tra ve danh sach loi
     co cau truc (id, severity, cwe, dong, mo ta).
  2. Loc bo cac thong bao "information" khong lien quan bao mat
     (missingIncludeSystem, checkersReport, ...).
  3. So sanh so luong loi bao mat thuc su (severity in {error, warning})
     giua 2 phien ban file de dinh luong "cai thien bao mat".
"""
from __future__ import annotations
import os
import subprocess
import xml.etree.ElementTree as ET

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
C_DIR = os.path.join(BASE, "c")

# Cac id thong bao KHONG lien quan bao mat, chi mang tinh thong tin chung
IGNORE_IDS = {"missingIncludeSystem", "checkersReport", "missingInclude"}


def run_cppcheck(c_file: str) -> list[dict]:
    """Chay cppcheck tren 1 file C, tra ve danh sach finding da duoc loc."""
    proc = subprocess.run(
        ["cppcheck", "--enable=all", "--inconclusive",
         "--xml", "--xml-version=2", c_file],
        cwd=C_DIR, capture_output=True, text=True,
    )
    root = ET.fromstring(proc.stderr)
    findings = []
    for err in root.findall(".//error"):
        eid = err.get("id")
        if eid in IGNORE_IDS:
            continue
        loc = err.find("location")
        findings.append({
            "id": eid,
            "severity": err.get("severity"),
            "cwe": err.get("cwe", ""),
            "message": err.get("msg"),
            "line": loc.get("line") if loc is not None else None,
        })
    return findings


def security_relevant(findings: list[dict]) -> list[dict]:
    """Chi giu lai cac finding co muc do error/warning (loai bo style/info
    thuan tuy khong lien quan an toan bo nho)."""
    return [f for f in findings if f["severity"] in ("error", "warning")]


def compare_versions(buggy_file: str, fixed_file: str) -> dict:
    buggy_findings = run_cppcheck(buggy_file)
    fixed_findings = run_cppcheck(fixed_file)
    return {
        "buggy_file": buggy_file,
        "fixed_file": fixed_file,
        "buggy_findings": buggy_findings,
        "fixed_findings": fixed_findings,
        "buggy_security_count": len(security_relevant(buggy_findings)),
        "fixed_security_count": len(security_relevant(fixed_findings)),
    }


def print_report(result: dict):
    print(f"=== cppcheck: {result['buggy_file']} (PHIEN BAN CO LOI) ===")
    for f in result["buggy_findings"]:
        cwe = f" [CWE-{f['cwe']}]" if f["cwe"] else ""
        print(f"  [{f['severity'].upper()}]{cwe} dong {f['line']}: {f['message']}")
    print(f"  -> {result['buggy_security_count']} finding lien quan bao mat (error/warning)\n")

    print(f"=== cppcheck: {result['fixed_file']} (PHIEN BAN DA SUA) ===")
    for f in result["fixed_findings"]:
        cwe = f" [CWE-{f['cwe']}]" if f["cwe"] else ""
        print(f"  [{f['severity'].upper()}]{cwe} dong {f['line']}: {f['message']}")
    print(f"  -> {result['fixed_security_count']} finding lien quan bao mat (error/warning)\n")

    improvement = result["buggy_security_count"] - result["fixed_security_count"]
    print(f"=== KET LUAN: giam {improvement} finding bao mat sau khi sua loi ===")
    print("Luu y su pham: cppcheck la phan tich TINH (khong chay chuong trinh),")
    print("phat hien tot loi 'uninitialized variable/data' nhung KHONG luon phat")
    print("hien duoc loi 'memory leak theo luong dieu khien phuc tap' (data-flow")
    print("giua nhieu nhanh return) - day la ly do Lab 3 da dung AddressSanitizer")
    print("(phan tich DONG) de bat loai loi nay. Hai cong cu BO SUNG cho nhau.")


if __name__ == "__main__":
    result = compare_versions("booklist_v1_buggy.c", "booklist_v2_fixed.c")
    print_report(result)

    import json
    out_path = os.path.join(BASE, "dataset", "cppcheck_comparison_results.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    print(f"\nKet qua da luu: {out_path}")
