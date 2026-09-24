"""
Chuong 2 - Lap trinh C an toan: wrapper Python quanh Cppcheck.

Module nay hien thuc hoa noi dung slide 2.5 ("Tich hop static analysis vao
workflow project Python"): chay Cppcheck tren mot hoac nhieu file C, phan
tich output XML va tong hop thanh bao cao co cau truc (dict / dataclass) de
de dang mo rong thanh cong cu CI rieng trong Bai tap lon.
"""

from __future__ import annotations
import subprocess
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path
from typing import List, Dict
from collections import Counter


@dataclass
class CppcheckFinding:
    file: str
    line: int
    severity: str   # error, warning, style, performance, portability, information
    cwe: str | None
    message: str
    check_id: str


@dataclass
class CppcheckReport:
    findings: List[CppcheckFinding] = field(default_factory=list)

    def by_severity(self) -> Dict[str, int]:
        return dict(Counter(f.severity for f in self.findings))

    def errors_and_warnings(self) -> List[CppcheckFinding]:
        return [f for f in self.findings if f.severity in ("error", "warning")]

    def summary(self) -> str:
        counts = self.by_severity()
        if not counts:
            return "Khong co canh bao nao duoc phat hien."
        parts = [f"{sev}: {n}" for sev, n in sorted(counts.items())]
        return f"Tong {len(self.findings)} canh bao ({', '.join(parts)})"


class CppcheckNotFoundError(RuntimeError):
    """Cppcheck khong duoc cai dat hoac khong tim thay trong PATH."""


def run_cppcheck(paths: List[str] | str, std: str = "c11",
                  enable: str = "all") -> CppcheckReport:
    """Chay cppcheck tren danh sach file/thu muc va tra ve CppcheckReport.

    Vi du:
        >>> report = run_cppcheck("c/ch2_memory/vulnerable.c")
        >>> report.summary()
    """
    if isinstance(paths, str):
        paths = [paths]

    cmd = [
        "cppcheck",
        f"--std={std}",
        f"--enable={enable}",
        "--suppress=missingIncludeSystem",
        "--xml",
        "--xml-version=2",
        *paths,
    ]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
    except FileNotFoundError as exc:
        raise CppcheckNotFoundError(
            "Khong tim thay 'cppcheck' trong PATH. Cai dat bang "
            "'apt-get install cppcheck' hoac tuong duong."
        ) from exc

    # Cppcheck ghi XML report ra STDERR (khong phai stdout)
    return _parse_cppcheck_xml(proc.stderr)


def _parse_cppcheck_xml(xml_text: str) -> CppcheckReport:
    report = CppcheckReport()
    if not xml_text.strip():
        return report
    try:
        root = ET.fromstring(xml_text)
    except ET.ParseError:
        return report

    errors_el = root.find("errors")
    if errors_el is None:
        return report

    for err in errors_el.findall("error"):
        location = err.find("location")
        finding = CppcheckFinding(
            file=location.get("file") if location is not None else "?",
            line=int(location.get("line", "0")) if location is not None else 0,
            severity=err.get("severity", "unknown"),
            cwe=err.get("cwe"),
            message=err.get("msg", ""),
            check_id=err.get("id", ""),
        )
        report.findings.append(finding)
    return report


def scan_directory(directory: str, pattern: str = "*.c") -> CppcheckReport:
    """Chay cppcheck tren toan bo file khop pattern trong mot thu muc."""
    files = [str(p) for p in Path(directory).glob(pattern)]
    if not files:
        return CppcheckReport()
    return run_cppcheck(files)


if __name__ == "__main__":
    import sys
    target = sys.argv[1] if len(sys.argv) > 1 else "../../c/ch2_memory"
    print(f"Dang quet: {target}")
    rep = scan_directory(target) if Path(target).is_dir() else run_cppcheck(target)
    print(rep.summary())
    for f in rep.errors_and_warnings():
        print(f"  [{f.severity}] {f.file}:{f.line} ({f.check_id}) {f.message}")
