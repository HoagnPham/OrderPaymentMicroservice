#!/usr/bin/env python3
"""Reproducible Chapter-2 static checks for BUGGY and FIXED configurations."""
from __future__ import annotations

import argparse
import json
import platform
import re
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MODULES = ("order", "payment", "cart", "product", "user")
EXPECTED = {
    "order": "owned-allocation-leak",
    "payment": "double-free",
    "cart": "use-after-free",
    "product": "unbounded-string-copy",
    "user": "unbounded-string-copy",
}
CWE = {
    "owned-allocation-leak": "CWE-401",
    "double-free": "CWE-415",
    "use-after-free": "CWE-416",
    "unbounded-string-copy": "CWE-120",
}


def active_lines(path: Path, fixed: bool) -> list[tuple[int, str]]:
    """Resolve the simple FIXED conditional used by these teaching modules."""
    active = True
    stack: list[bool] = []
    result: list[tuple[int, str]] = []
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        directive = line.strip()
        if directive == "#ifdef FIXED":
            stack.append(active)
            active = active and fixed
        elif directive == "#ifndef FIXED":
            stack.append(active)
            active = active and not fixed
        elif directive == "#else":
            if not stack:
                raise ValueError(f"unmatched #else in {path}:{number}")
            active = stack[-1] and not active
        elif directive == "#endif":
            if not stack:
                raise ValueError(f"unmatched #endif in {path}:{number}")
            active = stack.pop()
        elif active:
            result.append((number, line))
    if stack:
        raise ValueError(f"unclosed conditional in {path}")
    return result


def finding(rule: str, line: int, detail: str) -> dict:
    return {"rule": rule, "cwe": CWE[rule], "line": line, "detail": detail}


def pattern_scan(path: Path, fixed: bool) -> list[dict]:
    """Small syntax/data-flow rules; deliberately not a general C parser."""
    lines = active_lines(path, fixed)
    findings: list[dict] = []

    # A child allocation must be released before its owning parent.
    for index, (line_no, text) in enumerate(lines):
        match = re.search(r"(\w+)->(\w+)\s*=\s*allocate\s*\(", text)
        if not match:
            continue
        owner, member = match.groups()
        tail = lines[index + 1 :]
        parent_frees = [n for n, s in tail if re.search(rf"free\s*\(\s*{owner}\s*\)", s)]
        child_frees = [n for n, s in tail if re.search(rf"free\s*\(\s*{owner}->{member}\s*\)", s)]
        if parent_frees and not child_frees:
            findings.append(finding("owned-allocation-leak", parent_frees[0],
                                    f"{owner}->{member} remains owned when {owner} is freed"))

    # Track repeated free(expr), allowing the conventional assignment-to-NULL reset.
    last_free: dict[str, tuple[int, int]] = {}
    nulled: set[str] = set()
    for index, (line_no, text) in enumerate(lines):
        null_match = re.search(r"([A-Za-z_]\w*(?:\.\w+|->\w+)*)\s*=\s*NULL\s*;", text)
        if null_match:
            nulled.add(null_match.group(1))
        free_match = re.search(r"free\s*\(\s*([A-Za-z_]\w*(?:\.\w+|->\w+)*)\s*\)", text)
        if free_match:
            expr = free_match.group(1)
            previous = last_free.get(expr)
            # A return between two textual frees means the earlier branch cannot
            # fall through to the later cleanup (product rejection path).
            returned = previous is not None and any(
                re.search(r"\breturn\b", candidate)
                for _, candidate in lines[previous[1] + 1:index]
            )
            if previous is not None and expr not in nulled and not returned:
                findings.append(finding("double-free", line_no,
                                        f"{expr} was already freed at line {previous[0]}"))
            last_free[expr] = (line_no, index)
            nulled.discard(expr)

    # Dereferencing a simple pointer after free is a use-after-free.
    freed: dict[str, int] = {}
    for line_no, text in lines:
        free_match = re.search(r"free\s*\(\s*(\w+)\s*\)", text)
        if free_match:
            freed[free_match.group(1)] = line_no
            continue
        for variable, free_line in freed.items():
            if re.search(rf"\b{variable}\s*->", text):
                findings.append(finding("use-after-free", line_no,
                                        f"{variable} was freed at line {free_line}"))

    # In these straight-line demos, a prior strlen guard that returns rejects overflow.
    prior = ""
    for line_no, text in lines:
        copy = re.search(r"strcpy\s*\(\s*([^,]+),\s*([^\)]+)\)", text)
        if copy:
            source = copy.group(2).strip()
            guarded = re.search(rf"strlen\s*\(\s*{re.escape(source)}\s*\)\s*>=", prior) is not None
            if not guarded:
                findings.append(finding("unbounded-string-copy", line_no,
                                        "strcpy destination capacity is not guarded"))
        prior += text + "\n"
    return findings


def cppcheck(mode: str) -> dict:
    define = "-DFIXED=1" if mode == "fixed" else "-UFIXED"
    command = ["cppcheck", "--enable=all", "--inconclusive", "--std=c11",
               "--max-configs=1", define, "--suppress=missingIncludeSystem",
               "--xml", "--xml-version=2", *[f"modules/{m}.c" for m in MODULES]]
    process = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=60)
    if process.returncode not in (0, 1):
        raise RuntimeError(f"cppcheck failed ({process.returncode}): {process.stderr}")
    start = process.stderr.find("<?xml")
    if start < 0:
        raise RuntimeError("cppcheck did not emit XML")
    root = ET.fromstring(process.stderr[start:])
    items = []
    for error in root.findall("./errors/error"):
        if error.get("id") == "checkersReport":
            continue
        location = error.find("location")
        items.append({
            "id": error.get("id"), "severity": error.get("severity"),
            "cwe": error.get("cwe"), "message": error.get("msg"),
            "file": location.get("file") if location is not None else error.get("file0"),
            "line": int(location.get("line", "0")) if location is not None else 0,
        })
    return {"command": command, "return_code": process.returncode, "findings": items}


def compiler_check(mode: str) -> dict:
    define = ["-DFIXED=1"] if mode == "fixed" else []
    command = ["gcc", "-std=c11", "-Wall", "-Wextra", "-Wpedantic", "-fsyntax-only",
               *define, *[f"modules/{m}.c" for m in MODULES]]
    process = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=60)
    return {"command": command, "return_code": process.returncode,
            "diagnostics": process.stderr.splitlines()}


def layout() -> dict:
    with tempfile.TemporaryDirectory(prefix="c-memory-layout-") as directory:
        binary = Path(directory) / "layout_probe"
        command = ["gcc", "-std=c11", "-w", str(ROOT / "tests" / "layout_probe.c"), "-o", str(binary)]
        compile_result = subprocess.run(command, capture_output=True, text=True, timeout=60)
        if compile_result.returncode:
            raise RuntimeError(compile_result.stderr)
        run_result = subprocess.run([str(binary)], capture_output=True, text=True, timeout=10, check=True)
        return json.loads(run_result.stdout)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=ROOT / "evidence" / "static-analysis.json")
    args = parser.parse_args()
    if not shutil.which("cppcheck"):
        raise SystemExit("cppcheck not found; install cppcheck 2.13+ first")

    modes = {}
    for mode, fixed in (("buggy", False), ("fixed", True)):
        scans = {module: pattern_scan(ROOT / "modules" / f"{module}.c", fixed)
                 for module in MODULES}
        modes[mode] = {"pattern_scanner": scans, "cppcheck": cppcheck(mode),
                       "compiler": compiler_check(mode)}

    failures = []
    for module, expected_rule in EXPECTED.items():
        buggy_rules = {item["rule"] for item in modes["buggy"]["pattern_scanner"][module]}
        fixed_rules = {item["rule"] for item in modes["fixed"]["pattern_scanner"][module]}
        if expected_rule not in buggy_rules:
            failures.append(f"{module}: BUGGY did not trigger {expected_rule}")
        if expected_rule in fixed_rules:
            failures.append(f"{module}: FIXED still triggers {expected_rule}")
        if fixed_rules:
            failures.append(f"{module}: FIXED has unexpected findings: {sorted(fixed_rules)}")
    for mode in modes:
        if modes[mode]["compiler"]["return_code"] != 0:
            failures.append(f"{mode}: compiler syntax check failed")

    report = {
        "schema_version": 1,
        "scope": list(MODULES),
        "expected_buggy_rule": EXPECTED,
        "environment": {
            "platform": platform.platform(),
            "gcc": subprocess.run(["gcc", "--version"], capture_output=True, text=True).stdout.splitlines()[0],
            "cppcheck": subprocess.run(["cppcheck", "--version"], capture_output=True, text=True).stdout.strip(),
        },
        "method_note": "Pattern rules are project-specific teaching checks, not a replacement for a C parser or sound static analyzer.",
        "modes": modes,
        "layout": layout(),
        "assertions": {"passed": not failures, "failures": failures},
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"{'PASS' if not failures else 'FAIL'} static analysis: {args.output}")
    for failure in failures:
        print(f"  - {failure}")
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
