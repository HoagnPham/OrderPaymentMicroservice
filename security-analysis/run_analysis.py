"""Reproducible integration with the original ATPM labs; no third-party Python packages."""
import importlib.util
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import sys

sys.dont_write_bytecode = True

BASE = Path(__file__).resolve().parent
ROOT = BASE.parent
OUT = BASE / 'results'


def lab(name, relative):
    import sys
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def save(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def verify(path, checker):
    data = json.loads(path.read_text(encoding='utf-8'))
    # Lab 1 handles state predicates, not temporal AF. Run its supported properties directly.
    supported = dict(data, properties_to_check=[p for p in data['properties_to_check'] if p['type'] != 'AF'])
    temp = OUT / (path.stem + '.lab1.json')
    save(temp, supported)
    result = checker.run_verification(str(temp))
    model = checker.KripkeStructure.from_json(str(path))
    # Least fixed point for AF terminal: include p-states, then states whose ALL successors
    # belong to the set. Models are total (terminal self-loops); no fairness assumptions.
    good = {s for s in model.states if 'terminal' in model.states[s]}
    while True:
        expanded = good | {s for s, successors in model.transitions.items()
                           if successors and all(t in good for t in successors)}
        if expanded == good:
            break
        good = expanded
    holds = model.reachable_states() <= good
    result['properties'].append({'id': 'P2', 'formula': 'AG AF terminal', 'holds': holds,
                                  'description': 'Every reachable state inevitably terminates',
                                  'states_satisfying_AF': sorted(good)})
    result['properties'].sort(key=lambda p: p['id'])
    print(f'[P2] AG AF terminal: {"PASS" if holds else "FAIL"}')
    save(OUT / (path.stem + '.results.json'), result)
    return result


def coverage():
    path = OUT / 'input-validator.cobertura.xml'
    if not path.exists():
        print('Coverage not measured yet; run the documented dotnet test command.')
        return
    tree = ET.parse(path).getroot()
    classes = [c for c in tree.findall('.//class') if c.get('name') == 'OrderPaymentMicroservice.Security.InputValidator']
    assert classes, 'InputValidator is missing from coverage report'
    lines = classes[0].findall('./lines/line')
    missed = [{'line': int(line.get('number')), 'hits': int(line.get('hits')),
               'condition_coverage': line.get('condition-coverage')}
              for line in lines if line.get('branch', '').lower() == 'true' and
              line.get('condition-coverage', '').split('%')[0] != '100']
    rate = float(classes[0].get('branch-rate'))
    summary = {'class': classes[0].get('name'), 'branch_coverage_percent': round(rate * 100, 2),
               'line_coverage_percent': round(float(classes[0].get('line-rate')) * 100, 2),
               'uncovered_branches': missed, 'threshold_percent': 90, 'passed': rate >= .9}
    summary['methods'] = [{'name': m.get('name'), 'branch_coverage_percent': round(float(m.get('branch-rate')) * 100, 2)}
                          for m in classes[0].findall('./methods/method')]
    save(OUT / 'coverage_summary.json', summary)
    print(summary)
    assert rate >= .9, 'Branch coverage below 90%'


def main():
    checker = lab('atpm_kripke', 'ATPM/lab1/lab1_kripke_access_model/python/kripke_model.py')
    risk = lab('atpm_risk', 'ATPM/lab3_secure_memory/python/risk_assessor.py')
    correct = verify(BASE / 'order_state_model.json', checker)
    buggy = verify(BASE / 'order_state_model_buggy.json', checker)
    assert all(p['holds'] for p in correct['properties'])
    p1 = next(p for p in buggy['properties'] if p['id'] == 'P1')
    assert not p1['holds']
    assert p1['witness_states'] == ['CREATED', 'CANCELLED', 'PAID_AFTER_CANCELLED']
    results = risk.assess_all(str(BASE / 'vulnerabilities.json'))
    risk.print_report(results)
    save(OUT / 'risk_assessment_results.json', results)
    coverage()


if __name__ == '__main__':
    main()
