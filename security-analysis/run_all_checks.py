"""Run the reproducible verification chain and write an end-to-end summary.

Default mode runs the fast, dependency-light checks that are available in this
Linux environment: workbook/ATPM schema, recovery model, liveness, SMT, threat
model, C static/sanitizer/CBMC/Valgrind evidence, packaging and report. Checkout
retry needs Chrome/Chromium; C checks need gcc, cppcheck, cbmc and valgrind. The
CNF/DIMACS/DRAT certificate pipeline is opt-in with --with-sat because it also
needs CaDiCaL and drat-trim. The .NET suites and TLC are opt-in because they need
extra services or a jar. Black-box HTTP and browser network fault fuzzing are
opt-in with --with-http-fuzz/--with-browser-network-fuzz. The default chain does
not read the read-only ATPM reference tree.

This script does not replace the Windows-only ATPM GUI. It records exactly what
was executed and which artifacts were checked.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
RESULTS = ROOT / "security-analysis" / "results"
OUT = RESULTS / "end_to_end.results.json"
PY = sys.executable


def command(*parts: str) -> list[str]:
    return [PY, *parts]


FAST_STEPS = [
    ("workbook", command("security-analysis/verify_teacher_spec.py")),
    ("recovery_model", command("security-analysis/verify_recovery_model.py")),
    ("liveness", command("security-analysis/audit_liveness.py")),
    ("payment_smt", command("security-analysis/verify_payment_smt.py")),
    ("inventory_smt", command("security-analysis/verify_inventory_smt.py")),
    ("checkout_retry", command("security-analysis/verify_checkout.py")),
    ("threat_model", command("security-analysis/verify_threat_model.py")),
    (
        "c_static",
        command(
            "security-analysis/c-memory/tests/check_static.py",
            "--output",
            "security-analysis/c-memory/evidence/static-analysis.json",
        ),
    ),
    ("c_sanitizers", command("security-analysis/c-memory/tests/check_memory.py")),
    ("c_bmc", command("security-analysis/c-memory/tests/check_bmc.py")),
    ("c_valgrind", command("security-analysis/c-memory/tests/check_valgrind.py")),
]

ARTIFACTS = [
    ("results/teacher_spec_verification.json", "passed"),
    ("results/order_payment_recovery_model.results.json", "model.all_hold"),
    ("results/liveness_audit.results.json", "passed"),
    ("results/payment_verification.results.json", "passed"),
    ("results/inventory_verification.results.json", "passed"),
    ("results/checkout_retry.results.json", "passed"),
    ("results/threat_model_results.json", "passed"),
    ("c-memory/evidence/static-analysis.json", "assertions.passed"),
    ("c-memory/evidence/dynamic-analysis.json", "passed"),
    ("c-memory/evidence/bmc-analysis.json", "passed"),
    ("c-memory/evidence/valgrind-analysis.json", "passed"),
    ("c-memory/evidence/sat-pipeline.json", "passed"),
    ("results/application_tests.results.json", "passed"),
    ("results/http_fuzz.results.json", "passed"),
    ("results/browser_network_fuzz.results.json", "passed"),
]

PACKAGE_ARTIFACTS = [
    ("results/model_projection.results.json", "passed"),
    ("results/evidence_manifest.json", "exists"),
    ("report/BAO_CAO.md", "exists"),
    ("report/PHU_LUC_A_G.md", "exists"),
]


def get_path(data, dotted: str):
    current = data
    for part in dotted.split("."):
        if not isinstance(current, dict) or part not in current:
            return None
        current = current[part]
    return current


def check_artifact(relative: str, predicate: str) -> dict:
    path = ROOT / "security-analysis" / relative
    if not path.exists():
        return {"path": relative, "passed": False, "reason": "missing"}
    if predicate == "exists":
        return {"path": relative, "passed": True, "reason": "exists"}
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        return {"path": relative, "passed": False, "reason": f"invalid json: {error}"}
    value = get_path(data, predicate)
    return {
        "path": relative,
        "passed": value is True,
        "reason": f"{predicate}={value!r}",
    }


def run_step(name: str, arguments: list[str], timeout: int = 300) -> dict:
    started = time.monotonic()
    try:
        completed = subprocess.run(
            arguments,
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=timeout,
        )
        output = completed.stdout + completed.stderr
        exit_code = completed.returncode
        timed_out = False
    except subprocess.TimeoutExpired as error:
        output = (error.stdout or "") + (error.stderr or "")
        exit_code = 124
        timed_out = True
    duration = round(time.monotonic() - started, 3)
    lines = [line for line in output.splitlines() if line.strip()]
    tail = lines[-12:]
    print(f"[{'PASS' if exit_code == 0 else 'FAIL'}] {name} ({duration}s)")
    for line in tail[-4:]:
        print(f"    {line}")
    return {
        "name": name,
        "command": " ".join(arguments),
        "exit_code": exit_code,
        "timed_out": timed_out,
        "duration_s": duration,
        "passed": exit_code == 0,
        "tail": tail,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--with-dotnet-tests", action="store_true", help="run the unit test project")
    parser.add_argument("--with-integration", action="store_true", help="run the PostgreSQL integration project")
    parser.add_argument("--with-fuzz", action="store_true", help="run the deterministic .NET fuzz campaign")
    parser.add_argument("--with-http-fuzz", action="store_true", help="run black-box HTTP fuzz against real local API/PostgreSQL")
    parser.add_argument("--with-browser-network-fuzz", action="store_true", help="run Chrome CDP network fault fuzzing")
    parser.add_argument("--with-tlc", action="store_true", help="run TLC (requires TLA2TOOLS_JAR)")
    parser.add_argument("--with-sat", action="store_true", help="run CNF/DIMACS + DRAT proof checks (requires cadical and drat-trim)")
    parser.add_argument("--no-package", action="store_true", help="skip package_evidence and build_report")
    args = parser.parse_args()

    steps = list(FAST_STEPS)
    skipped = []
    if args.with_dotnet_tests:
        steps.insert(0, ("dotnet_build", ["dotnet", "build", "OrderPaymentMicroservice.slnx", "--no-restore"]))
        steps.insert(1, (
            "dotnet_unit_tests",
            ["dotnet", "test", "OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj",
             "--no-restore", "--nologo"],
        ))
    else:
        skipped.append("dotnet build/unit tests (use --with-dotnet-tests)")
    if args.with_integration:
        steps.insert(2, (
            "dotnet_integration_tests",
            ["dotnet", "test", "OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj",
             "--no-restore", "--nologo"],
        ))
    else:
        skipped.append("PostgreSQL integration tests (use --with-integration)")
    if args.with_fuzz:
        steps.append(("application_fuzz", command("security-analysis/run_fuzzing.py")))
    else:
        skipped.append("deterministic .NET fuzz campaign (use --with-fuzz)")
    if args.with_http_fuzz:
        steps.append(("http_fuzz", command("security-analysis/http_fuzz.py", "--iterations", "500", "--no-build")))
    else:
        skipped.append("black-box HTTP fuzz against real local API/PostgreSQL (use --with-http-fuzz)")
    if args.with_browser_network_fuzz:
        steps.append(("browser_network_fuzz", command("security-analysis/browser/network_fuzz.py")))
    else:
        skipped.append("Chrome CDP network fault fuzz (use --with-browser-network-fuzz)")
    if args.with_tlc:
        steps.append(("tlc", command("security-analysis/run_tlc.py")))
    else:
        skipped.append("TLC (use --with-tlc with TLA2TOOLS_JAR)")
    if args.with_sat:
        steps.append(("c_sat", command("security-analysis/c-memory/tests/check_sat_pipeline.py")))
    else:
        skipped.append("CNF/DIMACS + DRAT proof (use --with-sat with cadical and drat-trim)")
    if not args.no_package:
        steps.append(("package_evidence", command("security-analysis/package_evidence.py")))
        steps.append(("build_report", command("security-analysis/build_report.py")))
        steps.append(("package_final", command("security-analysis/package_evidence.py")))
    else:
        skipped.append("package_evidence/build_report (--no-package)")

    started_at = datetime.now(timezone.utc)
    results = [run_step(name, arguments) for name, arguments in steps]
    all_steps_passed = all(step["passed"] for step in results)

    artifact_checks = [check_artifact(path, predicate) for path, predicate in ARTIFACTS]
    if not args.no_package:
        artifact_checks.extend(check_artifact(path, predicate) for path, predicate in PACKAGE_ARTIFACTS)
    artifacts_passed = all(check["passed"] for check in artifact_checks)

    summary = {
        "schema_version": 1,
        "run_at_utc": started_at.isoformat(),
        "finished_at_utc": datetime.now(timezone.utc).isoformat(),
        "root": str(ROOT),
        "passed": all_steps_passed and artifacts_passed,
        "steps": results,
        "artifact_checks": artifact_checks,
        "skipped": skipped,
        "atpm_gui": (
            "BLOCKED: SpecVerificationLab.exe is a Windows GUI (PyInstaller/Python 3.14/Tkinter) "
            "and was not executed here; run the manual checklist in KICH_BAN_THUYET_TRINH_VA_DEMO.md."
        ),
        "limitations": [
            "This runner proves only that the listed commands exited successfully in this environment.",
            "Default mode validates stored .NET evidence; it does not rerun the 184 unit/integration tests unless --with-dotnet-tests/--with-integration is used.",
            "CBMC results are bounded by the harness and unwind limit.",
            "DRAT certificates prove UNSAT of the bounded CBMC CNF, not the ASP.NET service or unbounded executions.",
            "Artifact predicates check stored JSON flags, not a second independent implementation.",
            "HTTP fuzzing is black-box and uses semantic feedback; it is not code-coverage-guided and covers only the bounded local API/DB surface.",
            "Browser network fuzzing uses Chrome CDP response interception, not an external proxy or WAN; only the products fetch path is faulted.",
        ],
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    print()
    print("Artifact checks:")
    for check in artifact_checks:
        print(f"  [{'PASS' if check['passed'] else 'FAIL'}] {check['path']} ({check['reason']})")
    print()
    print(f"End-to-end: {'PASS' if summary['passed'] else 'FAIL'} -> {OUT.relative_to(ROOT)}")
    print(f"ATPM GUI: {summary['atpm_gui']}")
    raise SystemExit(0 if summary["passed"] else 1)


if __name__ == "__main__":
    main()
