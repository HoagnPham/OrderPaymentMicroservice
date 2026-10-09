"""Run deterministic application fuzz campaigns and persist evidence."""

from __future__ import annotations

import json
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
RESULTS = ROOT / "security-analysis" / "results"
PROJECT = ROOT / "OrderPaymentMicroservice.Tests" / "OrderPaymentMicroservice.Tests.csproj"
COVERAGE_PREFIX = RESULTS / "application-fuzzing-coverage"
COVERAGE_FILE = RESULTS / "application-fuzzing-coverage.cobertura.xml"
RESULT_FILE = RESULTS / "application_fuzzing.results.json"


def main() -> None:
    RESULTS.mkdir(parents=True, exist_ok=True)
    command = [
        "dotnet", "test", str(PROJECT), "--no-restore", "--nologo",
        "--filter", "FullyQualifiedName~SecurityFuzzingTests",
        "--logger", "console;verbosity=minimal",
        "/p:CollectCoverage=true",
        f"/p:CoverletOutput={COVERAGE_PREFIX}",
        "/p:CoverletOutputFormat=cobertura",
        "/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator%2c[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.PaymentValidator%2c[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.OrderStateMachine%2c[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.PaymentStateMachine",
    ]
    started = time.monotonic()
    timed_out = False
    try:
        completed = subprocess.run(
            command,
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=120,
            check=False,
        )
        output = completed.stdout + completed.stderr
        exit_code = completed.returncode
    except subprocess.TimeoutExpired as exc:
        timed_out = True
        output = (exc.stdout or "") + (exc.stderr or "")
        exit_code = 124

    duration = round(time.monotonic() - started, 3)
    match = re.search(r"Passed:\s+(\d+).*Failed:\s+(\d+).*Total:\s+(\d+)", output)
    if match is None:
        # Current dotnet console format starts with Failed before Passed.
        match = re.search(r"Failed:\s+(\d+),\s+Passed:\s+(\d+).*Total:\s+(\d+)", output)
        failed, passed, total = (map(int, match.groups()) if match else (None, None, None))
    else:
        passed, failed, total = map(int, match.groups())

    coverage = None
    if COVERAGE_FILE.exists():
        root = ET.parse(COVERAGE_FILE).getroot()
        coverage = {
            "line_percent": round(float(root.attrib["line-rate"]) * 100, 2),
            "branch_percent": round(float(root.attrib["branch-rate"]) * 100, 2),
            "file": str(COVERAGE_FILE.relative_to(ROOT)),
        }

    coverage_threshold = 90.0
    passed_campaign = (
        exit_code == 0
        and not timed_out
        and passed == 3
        and failed == 0
        and coverage is not None
        and coverage["branch_percent"] >= coverage_threshold
    )
    result = {
        "passed": passed_campaign,
        "seed": 20261009,
        "iterations_per_random_campaign": 25000,
        "corpus": "OrderPaymentMicroservice.Tests/FuzzCorpus/order_requests.json",
        "targets": ["InputValidator", "PaymentValidator", "OrderStateMachine", "PaymentStateMachine"],
        "crash_policy": "ArgumentException is a controlled rejection; timeout, any other exception, invariant mismatch or test failure is a crash.",
        "timeout_seconds": 120,
        "timed_out": timed_out,
        "duration_seconds": duration,
        "tests": {"passed": passed, "failed": failed, "total": total},
        "coverage": coverage,
        "branch_coverage_threshold_percent": coverage_threshold,
        "exit_code": exit_code,
        "command": command,
    }
    RESULT_FILE.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(output, end="")
    print(f"Fuzzing evidence: {RESULT_FILE}")
    if not passed_campaign:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
