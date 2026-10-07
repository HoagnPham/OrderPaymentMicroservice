"""Run the project SMT-LIB specification and persist checked evidence."""

from __future__ import annotations

import json
from pathlib import Path
import shutil
import subprocess


ROOT = Path(__file__).resolve().parents[1]
SPEC = ROOT / "OrderPaymentMicroservice" / "Security" / "PaymentVerification.z3"
RESULT = ROOT / "security-analysis" / "results" / "payment_verification.results.json"

EXPECTED = {
    "TEST1_VALID_PAID": "sat",
    "TEST2_TOTAL_MISMATCH": "unsat",
    "TEST3_PAID_UNDERPAYMENT": "unsat",
    "TEST4_NEGATIVE_PAYMENT": "unsat",
    "TEST5_PAYMENT_EXCEEDS_TOTAL": "unsat",
    "TEST6_INT32_SUBTOTAL_OVERFLOW_EXISTS": "sat",
    "TEST7_TOTAL_BOUND_BLOCKS_OVERSIZED_SUBTOTAL": "unsat",
}


def main() -> None:
    executable = shutil.which("z3")
    if executable is None:
        raise SystemExit("z3 was not found on PATH")

    version = subprocess.run(
        [executable, "-version"], check=True, capture_output=True, text=True
    ).stdout.strip()
    completed = subprocess.run(
        [executable, str(SPEC)], capture_output=True, text=True, check=False
    )
    if completed.returncode != 0:
        raise SystemExit(
            f"Z3 failed with exit code {completed.returncode}:\n{completed.stderr}{completed.stdout}"
        )

    lines = [line.strip() for line in completed.stdout.splitlines() if line.strip()]
    cases: list[dict[str, object]] = []
    case_ids = list(EXPECTED)
    for index, (case_id, expected) in enumerate(EXPECTED.items()):
        try:
            marker = lines.index(case_id)
        except ValueError as error:
            raise AssertionError(f"Missing Z3 marker {case_id}") from error
        next_markers = [lines.index(other) for other in case_ids[index + 1 :] if other in lines]
        end = min(next_markers) if next_markers else len(lines)
        block = lines[marker + 1 : end]
        actual = next((line for line in block if line in {"sat", "unsat", "unknown"}), None)
        if actual != expected:
            raise AssertionError(f"{case_id}: expected {expected}, got {actual}; output={block}")
        cases.append(
            {
                "id": case_id,
                "expected": expected,
                "actual": actual,
                "passed": True,
                "details": block[1:],
            }
        )

    evidence = {
        "tool": version,
        "command": "z3 OrderPaymentMicroservice/Security/PaymentVerification.z3",
        "logic": "QF_NIA",
        "integer_model": "monetary values in cents; mathematical integers are unbounded",
        "cases": cases,
        "passed": all(case["passed"] for case in cases),
        "limitations": [
            "The SMT model verifies arithmetic constraints, not the EF Core or gateway implementation.",
            "TEST6 is an existence counterexample for signed 32-bit arithmetic; production C# uses checked decimal.",
        ],
    }
    RESULT.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print(f"SMT verification: {len(cases)}/{len(EXPECTED)} PASS")
    for case in cases:
        print(f"[{case['id']}] {case['actual']} (expected {case['expected']})")
    print(f"Result: {RESULT}")


if __name__ == "__main__":
    main()
