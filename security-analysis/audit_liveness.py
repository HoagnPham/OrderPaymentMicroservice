"""Contrast unconditional AF terminal with the recovery checker's progress-only graph.

No external model checker and no course imports. Passing this audit means the
expected distinction is observed, not that unconditional liveness holds.
"""
from collections import deque
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path

BASE = Path(__file__).resolve().parent
MODEL = BASE / 'order_payment_recovery_model.json'

def edges(model):
    return [{'source': s['id'], **edge, 'kind': edge.get('kind', 'progress')}
            for s in model['states'] for edge in s.get('normal', []) + s.get('incidents', [])]

def af_set(states, graph, terminal):
    good = set(terminal)
    rounds = [sorted(good)]
    while True:
        expanded = good | {s for s in states if graph[s] and all(t in good for t in graph[s])}
        if expanded == good:
            return good, rounds
        good = expanded
        rounds.append(sorted(good))

def reachable(graph, initial='CREATED'):
    paths = {initial: [initial]}
    queue = deque([initial])
    while queue:
        source = queue.popleft()
        for target in graph[source]:
            if target not in paths:
                paths[target] = paths[source] + [target]
                queue.append(target)
    return paths

def main():
    model = json.loads(MODEL.read_text())
    states = {s['id'] for s in model['states']}
    terminal = {s['id'] for s in model['states'] if 'terminal' in s['labels']}
    transitions = edges(model)
    raw = {s: [] for s in states}; progress = {s: [] for s in states}
    for edge in transitions:
        raw[edge['source']].append(edge['to'])
        if edge['kind'] != 'stutter':
            progress[edge['source']].append(edge['to'])
    paths = reachable(raw)
    raw_good, raw_rounds = af_set(states, raw, terminal)
    progress_good, progress_rounds = af_set(states, progress, terminal)
    # A concrete reachable infinite path disproves AF on the original graph.
    loop = next(e for e in transitions if e['source'] == e['to'] == 'PAYMENT_RETRY')
    witnesses = {'prefix': paths['PAYMENT_RETRY'], 'loop': ['PAYMENT_RETRY', 'PAYMENT_RETRY'],
                 'event': loop['event'], 'interpretation': 'Repeat the loop forever; terminal is never reached.'}
    nonidentity = [e for e in transitions if e['kind'] == 'stutter' and e['source'] != e['to']]
    result = {
        'passed': not (set(paths) <= raw_good) and set(paths) <= progress_good and bool(nonidentity),
        'run_at_utc': datetime.now(timezone.utc).isoformat(),
        'model_sha256': hashlib.sha256(MODEL.read_bytes()).hexdigest(),
        'reachable_count': len(paths),
        'unconditional_AG_AF_terminal': set(paths) <= raw_good,
        'unconditional_satisfying_states': sorted(raw_good),
        'progress_only_AG_AF_terminal': set(paths) <= progress_good,
        'raw_fixed_point_rounds': raw_rounds, 'progress_fixed_point_rounds': progress_rounds,
        'counterexample_lasso': witnesses,
        'excluded_edges_with_state_change': nonidentity,
        'limitations': [
            'Deleting edges marked stutter is the existing checker policy, not a general fair-CTL algorithm.',
            'Some edges marked stutter change state; this name is project metadata, not identity of all formal state variables.',
            'Fairness of a gateway result requires environment assumptions; no runtime guarantee is inferred.',
        ],
    }
    (BASE / 'results/liveness_audit.results.json').write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n')
    print('Liveness audit:', 'PASS' if result['passed'] else 'FAIL')
    print('Raw AG AF terminal:', result['unconditional_AG_AF_terminal'])
    print('Progress-only AG AF terminal:', result['progress_only_AG_AF_terminal'])
    if not result['passed']:
        raise SystemExit(1)

if __name__ == '__main__':
    main()
