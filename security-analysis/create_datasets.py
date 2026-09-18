"""Generate checked-in Lab-compatible datasets. Run only to intentionally regenerate models."""
from copy import deepcopy
from run_analysis import BASE, save

statuses = ['CREATED', 'VALIDATED', 'PAYMENT_PENDING', 'PAID', 'CONFIRMED', 'COMPLETED', 'CANCELLED', 'FAILED']
model = {
    'system_name': 'Order - expected workflow (not a proof of implementation)',
    'kripke_mapping': {'S': 'states[].id', 'S0': 'initial_states', 'R': 'transitions', 'L': 'states[].labels'},
    'assumptions': ['Finite progress steps; no nonterminal waiting self-loops.',
                    'Payment success is a modeled proposition, not gateway verification.',
                    'Terminal states are absorbing; no fairness assumption required.'],
    'states': [{'id': s, 'name': s, 'labels': [s.lower()] +
               (['payment_success'] if s in ['PAID', 'CONFIRMED', 'COMPLETED'] else []) +
               (['terminal'] if s in ['COMPLETED', 'CANCELLED', 'FAILED'] else [])} for s in statuses],
    'initial_states': ['CREATED'], 'transitions': [],
    'properties_to_check': [
        {'id': 'P1', 'type': 'AG_NOT', 'formula': 'invalid_transition', 'description': 'No skipped/backward transitions; no terminal revival'},
        {'id': 'P2', 'type': 'AF', 'formula': 'terminal', 'description': 'AG AF terminal: every order inevitably completes, cancels or fails'},
        {'id': 'P3', 'type': 'AG_IMPLIES', 'formula': 'paid -> payment_success', 'description': 'PAID requires a successful payment'},
        {'id': 'P4', 'type': 'AG_IMPLIES', 'formula': 'payment_pending -> NOT payment_success', 'description': 'PAYMENT_PENDING has no successful payment yet'}]
}
def edge(a, b, event):
    model['transitions'].append({'from': a, 'to': b, 'event': event})
for a, b in zip(statuses[:5], statuses[1:6]):
    edge(a, b, f'{a.lower()}_to_{b.lower()}')
for s in ['CREATED', 'VALIDATED', 'PAYMENT_PENDING', 'CONFIRMED']:
    edge(s, 'CANCELLED', 'cancel')
for s in ['PAYMENT_PENDING', 'PAID']:
    edge(s, 'FAILED', 'fail')
for s in ['COMPLETED', 'CANCELLED', 'FAILED']:
    edge(s, s, 'terminal_stutter')
save(BASE / 'order_state_model.json', model)
buggy = deepcopy(model)
buggy['system_name'] = 'Order BUGGY - cancelled order can be paid'
buggy['states'].append({'id': 'PAID_AFTER_CANCELLED', 'name': 'PAID (after CANCELLED)',
                         'labels': ['paid', 'payment_success', 'invalid_transition']})
buggy['transitions'] += [
    {'from': 'CANCELLED', 'to': 'PAID_AFTER_CANCELLED', 'event': 'CreatePaymentAsync: missing order status guard'},
    {'from': 'PAID_AFTER_CANCELLED', 'to': 'CONFIRMED', 'event': 'confirm'}]
buggy['history_monitor'] = 'PAID_AFTER_CANCELLED has business status PAID; history distinguishes illegal entry from legal PAID.'
save(BASE / 'order_state_model_buggy.json', buggy)

findings = [
    ('001', 'CWE-306', 'Unauthenticated order access', 'Controllers/OrdersController.cs; Program.cs',
     'No authentication middleware or Authorize attribute; clients can list, read and cancel orders without proving ownership.', 'HIGH', 'HIGH', 'NONE'),
    ('002', 'CWE-602', 'Client-controlled order prices and totals', 'Services/OrderService.cs: CreateOrderAsync',
     'UnitPrice and TotalAmount are persisted directly from the client rather than recalculated from trusted product prices.', 'NONE', 'HIGH', 'NONE'),
    ('003', 'CWE-841', 'Cancelled order revival through payment', 'Services/PaymentService.cs: CreatePaymentAsync',
     'Any existing order is marked PAID, including CANCELLED; simulated SUCCESS is not verified by a payment gateway.', 'NONE', 'HIGH', 'NONE'),
    ('004', 'CWE-20', 'Total mismatch silently accepted', 'Security/InputValidator.cs: tolerance conditional',
     'Differences greater than 0.01 enter an empty block. Unit tests demonstrate acceptance of total 0.01 versus item price 999999.99.', 'NONE', 'HIGH', 'NONE'),
    ('005', 'CWE-639', 'Cross-user cart modification (new finding)', 'Controllers/CartController.cs: user/{userId}/add and {cartId}/item/{itemId}',
     'Caller supplies userId/cartId/itemId without an authenticated identity or ownership check. A reachable API lets an attacker read another cart and modify or delete its items. HIGH integrity and LOW confidentiality; no demonstrated server availability loss.', 'LOW', 'HIGH', 'NONE')]
data = {'module': 'OrderPaymentMicroservice', 'scope': 'Static review plus validator characterization; not live exploitation', 'vulnerabilities': []}
for id, cwe, name, location, description, c, i, a in findings:
    data['vulnerabilities'].append(dict(id=f'VULN-{id}', cwe=cwe, name=name,
        location='OrderPaymentMicroservice/' + location, description=description,
        attack_vector='NETWORK', attack_complexity='LOW', privileges_required='NONE', user_interaction='NONE',
        confidentiality_impact=c, integrity_impact=i, availability_impact=a,
        exploitability='HIGH', detected_by='Static code review' + (' and xUnit characterization' if id == '004' else ''),
        deployment_assumption='API network-reachable; no external reverse-proxy authentication assumed'))
save(BASE / 'vulnerabilities.json', data)
