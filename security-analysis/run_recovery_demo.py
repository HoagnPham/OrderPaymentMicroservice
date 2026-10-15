"""Run only Order/Payment reconnect acceptance checks, without ATPM access."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    started = datetime.now(timezone.utc)
    # No TRX in results/dotnet: a filtered demo must not replace full-suite evidence.
    subprocess.run([
        'dotnet', 'test',
        'OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj',
        '--no-restore', '--nologo', '--filter', 'FullyQualifiedName~PostgresReconnectTests',
        '--blame-hang-timeout', '90s',
    ], cwd=ROOT, check=True, timeout=240)
    for operation in ('charge', 'refund'):
        path = ROOT / f'security-analysis/results/postgres_reconnect_{operation}.results.json'
        result = json.loads(path.read_text(encoding='utf-8'))
        if datetime.fromisoformat(result['run_at_utc'].replace('Z', '+00:00')) < started:
            raise SystemExit(f'Stale evidence; demo may have been skipped: {path}')
        if not result['passed'] or result['api_restarted'] or result['health_ready_sequence'] != [200, 503, 200]:
            raise SystemExit(f'Invalid recovery evidence: {path}')
        for relative, expected in result['source_sha256'].items():
            actual = hashlib.sha256((ROOT / relative).read_bytes()).hexdigest()
            if actual.upper() != expected.upper():
                raise SystemExit(f'Source changed since test: {relative}')
        calls = result['gateway_calls']
        print(f"{operation}: PASS; ready 200 -> 503 -> 200; same API process; "
              f"charge={calls['charge_calls']}, refund={calls['refund_calls']}, "
              f"inventory_release={result['inventory_release_count']}")
        print(path.relative_to(ROOT))


if __name__ == '__main__':
    main()
