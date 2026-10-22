"""Run Valgrind Memcheck on non-sanitized builds of the five C modules.

ASan/UBSan evidence already exists in ``dynamic-analysis.json``. This script
adds an independent dynamic checker (Chapter 2/4 evidence) and deliberately
builds without sanitizers so Memcheck sees the raw memory accesses.
"""
from __future__ import annotations

import json
import platform
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
RESULTS = ROOT / "results"
EVIDENCE = ROOT / "evidence"
BUILD = ROOT / "build" / "valgrind"
RESULTS.mkdir(exist_ok=True)
EVIDENCE.mkdir(exist_ok=True)
BUILD.mkdir(parents=True, exist_ok=True)

MODULES = {
    "user": ("Jump to the invalid address", "Invalid write"),
    "product": ("Invalid write",),
    "cart": ("Invalid read",),
    "order": ("definitely lost",),
    "payment": ("Invalid free",),
}
FORBIDDEN = ("Invalid write", "Invalid read", "Invalid free", "definitely lost", "Mismatched")
VALGRIND = [
    "valgrind",
    "--error-exitcode=99",
    "--leak-check=full",
    "--show-leak-kinds=definite",
    "--errors-for-leak-kinds=definite",
    "--quiet",
]


def require_tool(name: str) -> str:
    binary = shutil.which(name)
    if binary is None:
        raise SystemExit(f"{name} is required for the Valgrind evidence; install it first")
    return binary


def build(module: str, variant: str) -> Path:
    output = BUILD / f"{module}_{variant}"
    command = [
        "gcc", "-std=c11", "-g", "-O0", "-Wall", "-Wextra",
        "-fno-stack-protector",
    ]
    if variant == "fixed":
        command.append("-DFIXED")
    command.extend([str(ROOT / "modules" / f"{module}.c"), "-o", str(output)])
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"gcc failed for {module}_{variant}:\n{result.stderr}")
    return output


def run_case(module: str, variant: str) -> dict:
    binary = build(module, variant)
    command = [*VALGRIND, str(binary)]
    result = subprocess.run(command, capture_output=True, text=True, timeout=120)
    log = result.stdout + result.stderr
    (RESULTS / f"valgrind_{module}_{variant}.log").write_text(log, encoding="utf-8")
    if variant == "buggy":
        expected = MODULES[module]
        passed = result.returncode != 0 and any(token in log for token in expected)
    else:
        passed = result.returncode == 0 and not any(token in log for token in FORBIDDEN)
    return {
        "module": module,
        "variant": variant,
        "passed": passed,
        "exit_code": result.returncode,
        "expected": "memory error" if variant == "buggy" else "clean Memcheck run",
        "diagnostics": [line for line in log.splitlines() if any(token in line for token in FORBIDDEN)][:5],
        "command": " ".join(command),
    }


def main() -> None:
    require_tool("gcc")
    require_tool("valgrind")
    version = subprocess.run(["valgrind", "--version"], capture_output=True, text=True, check=True)
    cases = []
    for module in MODULES:
        cases.append(run_case(module, "buggy"))
        cases.append(run_case(module, "fixed"))
    passed = all(case["passed"] for case in cases)
    evidence = {
        "schema_version": 1,
        "tool": f"valgrind ({version.stdout.strip()})",
        "command": "python3 security-analysis/c-memory/tests/check_valgrind.py",
        "platform": platform.platform(),
        "build": "gcc -std=c11 -g -O0 -Wall -Wextra -fno-stack-protector (no sanitizers)",
        "options": VALGRIND[1:],
        "cases": cases,
        "passed": passed,
        "scope_limit": (
            "Memcheck covers the five C teaching modules and the executed inputs only; "
            "it is not a proof for the ASP.NET service or every possible input."
        ),
    }
    (EVIDENCE / "valgrind-analysis.json").write_text(
        json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    for case in cases:
        print(f"{'PASS' if case['passed'] else 'FAIL'} {case['module']}_{case['variant']}")
    print(f"Valgrind: {'PASS' if passed else 'FAIL'} ({len(cases)} cases)")
    raise SystemExit(0 if passed else 1)


if __name__ == "__main__":
    main()
