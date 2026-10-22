"""Run CBMC bounded model checking on the five C teaching modules.

The Windows-only ATPM GUI is not available on this machine, so this script is
the Chapter-3 (SAT/SMT/BMC) evidence for the small C models: CBMC must find the
injected defect in BUGGY and prove the same property on FIXED within the stated
bound. It is not a proof for the ASP.NET implementation.
"""
from __future__ import annotations

import json
import platform
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
HARNESSES = ROOT / "tests" / "bmc_harnesses"
RESULTS = ROOT / "results"
EVIDENCE = ROOT / "evidence"
RESULTS.mkdir(exist_ok=True)
EVIDENCE.mkdir(exist_ok=True)

MODULES = {
    "user": "pointer outside object bounds in dst",
    "product": "pointer outside object bounds in dst",
    "cart": "deallocated dynamic object in removed->product_id",
    "order": "dynamically allocated memory never freed",
    "payment": "double free",
}
UNWIND = 64
COMMON = [
    "--function", "main",
    "--bounds-check",
    "--pointer-check",
    "--memory-leak-check",
    "--unwind", str(UNWIND),
    "--unwinding-assertions",
]


def require_cbmc() -> str:
    binary = shutil.which("cbmc")
    if binary is None:
        raise SystemExit("cbmc is required for the BMC evidence; install cbmc first")
    version = subprocess.run([binary, "--version"], capture_output=True, text=True, check=True)
    return version.stdout.strip().splitlines()[0]


def run_case(module: str, variant: str) -> dict:
    harness = HARNESSES / f"{module}_harness.c"
    command = ["cbmc", str(harness), *COMMON]
    if variant == "fixed":
        command.append("-DFIXED")
    result = subprocess.run(command, capture_output=True, text=True, timeout=180)
    log = result.stdout + result.stderr
    (RESULTS / f"bmc_{module}_{variant}.log").write_text(log, encoding="utf-8")
    failures = [line for line in log.splitlines() if line.rstrip().endswith("FAILURE")]
    if variant == "buggy":
        expected = MODULES[module]
        passed = "VERIFICATION FAILED" in log and any(expected in line for line in failures)
    else:
        passed = "VERIFICATION SUCCESSFUL" in log and not failures
    return {
        "module": module,
        "variant": variant,
        "passed": passed,
        "exit_code": result.returncode,
        "failures": failures,
        "expected_failure": MODULES[module] if variant == "buggy" else "VERIFICATION SUCCESSFUL",
        "command": " ".join(command),
    }


def main() -> None:
    version = require_cbmc()
    cases = []
    for module in MODULES:
        cases.append(run_case(module, "buggy"))
        cases.append(run_case(module, "fixed"))
    passed = all(case["passed"] for case in cases)
    evidence = {
        "schema_version": 1,
        "tool": f"cbmc ({version})",
        "command": "python3 security-analysis/c-memory/tests/check_bmc.py",
        "platform": platform.platform(),
        "bound": {
            "unwind": UNWIND,
            "symbolic_input_bytes": 48,
            "note": "The bound is stated explicitly; a proof outside this bound is not claimed.",
        },
        "checks": ["bounds-check", "pointer-check", "memory-leak-check", "unwinding-assertions"],
        "cases": cases,
        "passed": passed,
        "scope_limit": (
            "CBMC checks the five C teaching modules, not the ASP.NET service. "
            "FIXED success is bounded by the harness and unwind limit."
        ),
    }
    (EVIDENCE / "bmc-analysis.json").write_text(
        json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    for case in cases:
        print(f"{'PASS' if case['passed'] else 'FAIL'} {case['module']}_{case['variant']}")
    print(f"CBMC: {'PASS' if passed else 'FAIL'} ({len(cases)} cases, unwind={UNWIND})")
    raise SystemExit(0 if passed else 1)


if __name__ == "__main__":
    main()
