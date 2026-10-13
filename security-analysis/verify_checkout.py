"""Verify checkout retry logic in local Chrome with mocked HTTP; no production API calls."""
import hashlib
import json
import re
import shutil
import subprocess
import tempfile
from datetime import datetime, timezone
from pathlib import Path

BASE = Path(__file__).resolve().parent
ROOT = BASE.parent


def main():
    browser = shutil.which('google-chrome') or shutil.which('chromium')
    if not browser:
        raise SystemExit('Chrome/Chromium is required for the local checkout verification.')
    source_path = ROOT / 'OrderPaymentMicroservice/wwwroot/js/app.js'
    source = source_path.read_text()
    block = source[source.index('// Checkout & Orders'):source.index('function goToPayment')]
    template = BASE / 'browser/checkout_retry.template.html'
    html = template.read_text().replace('@@SOURCE@@', json.dumps(source).replace('</script', '<\\/script')).replace('@@BLOCK@@', block)
    with tempfile.TemporaryDirectory(prefix='checkout-verification-') as directory:
        page = Path(directory) / 'test.html'
        page.write_text(html)
        result = subprocess.run([browser, '--headless', '--no-sandbox', '--disable-gpu',
                                 '--disable-background-networking', f'--user-data-dir={directory}/profile',
                                 '--virtual-time-budget=1000', '--dump-dom', page.as_uri()],
                                capture_output=True, text=True, timeout=45)
        match = re.search(r'<pre id="result">(.*?)</pre>', result.stdout, re.S)
        message = match.group(1) if match else 'Browser did not report a result'
        passed = result.returncode == 0 and message.startswith('PASS:')
    evidence = {
        'passed': passed, 'run_at_utc': datetime.now(timezone.utc).isoformat(),
        'browser': subprocess.check_output([browser, '--version'], text=True).strip(),
        'source_sha256': {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                          for p in (source_path, template, Path(__file__))},
        'result': message,
        'limits': 'Browser executes real checkout functions with mocked fetch/sessionStorage; live HTTP/PostgreSQL is verified by separate integration tests.'
    }
    (BASE / 'results/checkout_retry.results.json').write_text(json.dumps(evidence, indent=2) + '\n')
    print(message)
    if not passed:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
