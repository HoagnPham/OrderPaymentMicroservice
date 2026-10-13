"""Summarize project Cobertura after test coverage; does not import course tooling."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

BASE = Path(__file__).resolve().parent
REPORT = BASE / 'results/input-validator.cobertura.xml'

def main():
    tree = ET.parse(REPORT)
    name = 'OrderPaymentMicroservice.Security.InputValidator'
    node = next(c for c in tree.findall('.//class') if c.get('name') == name)
    summary = {
        'class': name, 'branch_coverage_percent': round(float(node.get('branch-rate')) * 100, 2),
        'line_coverage_percent': round(float(node.get('line-rate')) * 100, 2),
        'uncovered_branches': [{'line': int(l.get('number')), 'hits': int(l.get('hits')), 'condition_coverage': l.get('condition-coverage')}
                              for l in node.findall('./lines/line') if l.get('branch') == 'true' and l.get('condition-coverage','').split('%')[0] != '100'],
        'threshold_percent': 90, 'passed': float(node.get('branch-rate')) >= .9,
        'methods': [{'name': m.get('name'), 'branch_coverage_percent': round(float(m.get('branch-rate'))*100,2)} for m in node.findall('./methods/method')],
        'run_at_utc': datetime.now(timezone.utc).isoformat(), 'report_sha256': hashlib.sha256(REPORT.read_bytes()).hexdigest(),
        'included_helper': {'class': 'OrderPaymentMicroservice.Security.MoneyAmounts',
                            'branch_coverage_percent': round(float(next(c for c in tree.findall('.//class') if c.get('name') == 'OrderPaymentMicroservice.Security.MoneyAmounts').get('branch-rate'))*100,2)},
        'limitations': ['InputValidator and cents helper only; not whole-application coverage.'],
    }
    (BASE/'results/coverage_summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2)+'\n')
    print('InputValidator branch:',summary['branch_coverage_percent'],'MoneyAmounts branch:',summary['included_helper']['branch_coverage_percent'])
    if not summary['passed']: raise SystemExit(1)

if __name__ == '__main__': main()
