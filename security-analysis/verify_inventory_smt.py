"""Check stock reservation with duplicate product lines; never imports course tools."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
SPEC = ROOT / 'OrderPaymentMicroservice/Security/InventoryVerification.z3'
RESULT = ROOT / 'security-analysis/results/inventory_verification.results.json'
EXPECTED = {
    'DUPLICATE_LINES_BASELINE_OVERSOLD': 'sat',
    'AGGREGATED_STOCK_CANNOT_BE_NEGATIVE': 'unsat',
    'AGGREGATED_EXACT_STOCK_ACCEPTED': 'sat',
}

def main():
    executable = shutil.which('z3')
    if executable is None:
        raise SystemExit('z3 is required')
    completed = subprocess.run([executable, str(SPEC)], capture_output=True, text=True, timeout=30)
    lines = [line.strip() for line in completed.stdout.splitlines() if line.strip()]
    cases = []
    for name, expected in EXPECTED.items():
        actual = lines[lines.index(name) + 1] if name in lines else 'missing'
        cases.append({'id': name, 'expected': expected, 'actual': actual,
                      'passed': completed.returncode == 0 and actual == expected})
    result = {
        'passed': all(case['passed'] for case in cases), 'cases': cases,
        'tool': subprocess.run([executable, '-version'], capture_output=True, text=True, check=True).stdout.strip(),
        'run_at_utc': datetime.now(timezone.utc).isoformat(),
        'command': 'z3 OrderPaymentMicroservice/Security/InventoryVerification.z3',
        'spec_sha256': hashlib.sha256(SPEC.read_bytes()).hexdigest(),
        'stdout': completed.stdout, 'stderr': completed.stderr,
        'limitations': ['Two lines of one product; mathematical integers.',
                        'No proof of EF transactions, interleavings, or all API executions.'],
    }
    RESULT.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print(f"Inventory SMT: {sum(c['passed'] for c in cases)}/{len(cases)} PASS")
    if not result['passed']:
        raise SystemExit(1)

if __name__ == '__main__':
    main()
