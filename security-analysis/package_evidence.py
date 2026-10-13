"""Package project evidence without accessing ATPM or rerunning course tools.

Run after dotnet test ... --logger 'trx;LogFilePrefix=verification'
--results-directory security-analysis/results/dotnet.
Historical evidence is explicitly distinguished from current verification.
"""
from datetime import datetime, timezone
import csv
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
RESULTS = ROOT / 'security-analysis/results'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def collect_tests():
    suites = {}
    for path in sorted((RESULTS / 'dotnet').glob('*.trx'), key=lambda p: p.stat().st_mtime):
        tree = ET.parse(path)
        methods = tree.findall('.//t:TestMethod', NS)
        projects = {Path(m.get('codeBase', m.get('className', ''))).name for m in methods}
        suite = 'integration' if any('IntegrationTests' in name for name in projects) else 'unit'
        cases = [{'name': node.get('testName'), 'outcome': node.get('outcome'), 'duration': node.get('duration')}
                 for node in tree.findall('.//t:UnitTestResult', NS)]
        counters = tree.find('.//t:Counters', NS)
        if counters is None or not cases:
            raise ValueError(f'Incomplete test evidence: {path}')
        suites[suite] = {
            'run_outcome': tree.find('t:ResultSummary', NS).get('outcome'),
            'total': int(counters.get('total', 0)), 'passed': int(counters.get('passed', 0)),
            'failed': int(counters.get('failed', 0)),
            'skipped': sum(case['outcome'] != 'Passed' and case['outcome'] != 'Failed' for case in cases),
            'times': tree.find('t:Times', NS).attrib,
            'trx_local_path': str(path.relative_to(ROOT)), 'trx_sha256': digest(path),
            'cases': sorted(cases, key=lambda c: c['name']),
        }
    if set(suites) != {'unit', 'integration'}:
        raise ValueError('Both complete unit and integration TRX runs are required')
    return {'passed': all(s['run_outcome'] in {'Completed', 'Passed'} and s['total'] == s['passed'] and s['skipped'] == 0 for s in suites.values()),
            'suites': suites,
            'command': "dotnet test OrderPaymentMicroservice.slnx --no-restore --nologo --logger 'trx;LogFilePrefix=verification' --results-directory security-analysis/results/dotnet --blame-hang-timeout 60s",
            'limitations': ['TRX raw logs remain local; this file retains every case outcome and the raw log hash.',
                            'ProcessRestartTests covers one abrupt API kill window with a local durable simulator, not power loss or a real provider.']}

def compare_models():
    path = ROOT / 'security-analysis/verify_recovery_model.py'
    spec = importlib.util.spec_from_file_location('recovery_checker', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    recovery = json.loads((ROOT / 'security-analysis/order_payment_recovery_model.json').read_text())
    pipeline = json.loads((ROOT / 'security-analysis/order_payment_pipeline_spec.json').read_text())
    edges = module.transitions(recovery)
    detailed = {(e['from'], e['to']) for e in edges}
    projected = {tuple(edge) for edge in pipeline['state_machine']['transitions']}
    return {'passed': detailed == projected,
            'recovery_event_transitions': len(edges), 'recovery_unique_state_pairs': len(detailed),
            'pipeline_state_pairs': len(projected),
            'only_recovery': sorted(detailed - projected), 'only_pipeline': sorted(projected - detailed),
            'projection': 'Drop event/action/kind metadata and deduplicate source-target pairs.',
            'limitations': ['Matching edges does not make the seven workbook CTL properties equivalent to all fifteen recovery properties.']}

TRACE = [
    ('SEC-ADMISSION-01', 'Reject oversized body/items; auth IP and verified-user quotas', 'ApiAdmissionOptions; RequestBodyLimitMiddleware; ASP.NET RateLimiter; InputValidator', 'AdmissionBoundaryTests; BodyLimitTests; RateLimitsUseSeparateAuthBucketAndSeparateVerifiedUserBuckets; BodyAndItemLimitsRejectBeforeOrderPersistence; StreamingBodyWithoutLengthIsRejectedBeforeJsonBinding', 'application_tests.results.json', 'UNIT_HTTP_POSTGRES', 'Single-process fixed windows; no load/distributed guarantees'),
    ('SEC-AUDIT-01', 'Durable allowlisted audit; mutation forbidden; audit failure cannot masquerade as completed success', 'AuditWriter; AuditTrailMiddleware; BufferedAuditResponse; audit triggers', 'AuditMiddlewareTests; PaymentCancelRefundAuditPersistsWithoutSecretsAndRejectsMutation; MissingAuditStorePreventsBusinessOperation; MissingAuditResultReturns503WithDurableBeginAndCommittedOrder', 'application_tests.results.json', 'UNIT_HTTP_POSTGRES', 'COMMIT atomic with Orders/Payments; HTTP BEGIN/RESULT separate; DBA/retention remain open'),
    ('SEC-ATOMIC-AUDIT-01', 'Business mutation and COMMIT audit share transaction; audit failure rolls back stock/order', 'audit_business_commit trigger; AppDbContext transaction metadata', 'AtomicAuditInsertFailureRollsBackOrderAndInventory; DirectSqlMutationIsAuditedAndTransactionRollbackRemovesItsAudit', 'application_tests.results.json', 'HTTP_POSTGRES_FAULT', 'Orders/Payments only; BEGIN/RESULT separate; DBA can disable triggers'),
    ('SEC-ORDER-IDEMPOTENCY-01', 'Order creation key bound to authenticated user/payload and original receipt', 'OrderService; unique customer/key hash; checkout sessionStorage', 'AtomicAuditIdempotencyTests; MissingAuditResultReturns503WithDurableBeginAndCommittedOrder', 'application_tests.results.json; checkout_retry.results.json', 'HTTP_POSTGRES_BROWSER_MOCK', 'Optional header; 409 requires same-key retry; no cross-tab or receipt deletion proof'),
    ('SEC-MULTIHOST-01', 'Two API hosts allocate ten units under finite concurrent load without overselling', 'Serializable OrderService; stock CHECK; customer/key index', 'TwoApiHostsCompeteForTenUnitsWithoutOverselling', 'application_tests.results.json', 'MULTIHOST_HTTP_POSTGRES', 'Same machine; bounded retry workload; not throughput/p95/soak or distributed quota proof'),
    ('OPS-RESTORE-01', 'Paired stopped-writer snapshot restores DB/ledger/receipts/audit and recovers one charge', 'OperationalDrillTests; pg_dump/pg_restore; tar; PaymentRecoveryWorker', 'BackupRestoresDatabaseReceiptsAuditAndGatewayJournalThenRecoversPendingCharge', 'backup_restore.results.json; application_tests.results.json', 'OS_PROCESS_POSTGRES_ARCHIVE', 'Local stopped writers; not online backup/PITR or production RPO/RTO'),
    ('OPS-LOAD-01', 'Record HTTP latency/status and inventory consistency through two real API processes', 'OperationalDrillTests; Serializable OrderService; rate limiter', 'TwoRealApiProcessesRecordBoundedLoadLatencyConflictsAndRateLimits', 'load_probe.results.json; application_tests.results.json', 'OS_PROCESS_HTTP_POSTGRES_LOAD', 'One short same-machine hot-product sample; no retries/warmup exclusion/soak; shared quota not verified'),
    ('OPS-MONITOR-01', 'Minimal live/ready, Admin-only aggregate status, durable pending age and allowlisted alerts', 'OperationsController; OperationsSnapshot; OperationsAlertWorker; PendingSince trigger', 'OperationsTests; PendingAgeTests; KilledApiRecoversRefundWithoutRefundingOrReleasingInventoryTwice', 'application_tests.results.json; refund_restart.results.json', 'UNIT_HTTP_POSTGRES_OUTAGE', 'Counters per process; historical pending age lower bound; no external exporter/notification or reconnect proof'),
    ('SEC-REFUND-RESTART-01', 'Refund after API kill reconciles once, preserving charge and inventory', 'CrashProbe refund barrier; DurableFake; PaymentRecoveryWorker', 'KilledApiRecoversRefundWithoutRefundingOrReleasingInventoryTwice', 'refund_restart.results.json; application_tests.results.json', 'OS_PROCESS_HTTP_POSTGRES', 'One refund crash window; local fake, not power loss/provider/network partition'),
    ('SEC-ORDER-RETRY-01', 'Retry only allowlisted create conflicts with stable key/body, four attempts and bounded jitter', 'OrdersController typed conflict JSON; createOrderWithRetry; exact key index classification', 'OrderRetryContractTests; StockAndInactiveProductConflictsAreNotRetryable; AtomicAuditIdempotencyTests; browser retry assertions', 'application_tests.results.json; checkout_retry.results.json', 'UNIT_HTTP_POSTGRES_BROWSER_MOCK', 'Only create order; no automatic network/503/payment retry; optional key clients cannot auto retry'),
    ('OPS-RETRY-COMPARISON-01', 'Measure one-shot versus bounded retry across eight users/four products', 'OrderRetryLoadTests C# policy driver', 'CompareBoundedSameKeyRetryWithOneShotAcrossUsersAndProducts', 'order_retry_load.results.json; application_tests.results.json', 'OS_PROCESS_HTTP_POSTGRES_LOAD', 'One sequential same-machine comparison; no statistical/SLA claim; JS verified separately'),
    ('SEC-TIMEOUT-01', 'Gateway deadline giữ UNKNOWN/refund pending; worker tiếp tục và shutdown đúng', 'TimeoutPaymentGateway; PaymentService; PaymentRecoveryWorker', 'GatewayTimeoutTests', 'application_tests.results.json', 'UNIT_WORKER', 'Bound caller waiting, không cưỡng chế adapter hay provider outcome'),
    ('SEC-MONEY-01', 'Reject fractional cents; cent values round-trip PostgreSQL', 'MoneyAmounts; MoneyAmountAttribute; request validators', 'MoneyPrecisionTests; Fractional_cents_are_rejected_before_persistence_and_cent_values_round_trip', 'application_tests.results.json', 'UNIT_HTTP_POSTGRES', 'Không kiểm soát raw SQL writer ngoài API'),
    ('SEC-RECOVERY-01', 'Khôi phục charge sau kill API mà không charge/release lại', 'DurableFakePaymentGateway; PaymentRecoveryWorker', 'Killed_API_recovers_committed_gateway_charge_without_charging_or_releasing_again', 'process_restart.results.json; application_tests.results.json', 'OS_PROCESS_HTTP_POSTGRES', 'Charge crash window; refund có evidence riêng; không power loss/provider thật'),
    ('REQ-1 / REQ-2', 'JWT, role và ownership', 'Controller authorization', 'Authentication_and_resource_ownership_are_enforced', 'application_tests.results.json', 'HTTP_POSTGRES', 'Không chứng minh role thu hồi tức thời'),
    ('REQ-3', 'Giá server và kho đủ cho tổng dòng cùng sản phẩm', 'OrderService.CreateOrderAsync', 'DuplicateProductLinesRespectCombinedStock; Invalid_payloads_are_rejected_without_changing_persisted_data', 'application_tests.results.json', 'UNIT_HTTP_POSTGRES', 'Model lifecycle không mã hóa stock số học'),
    ('SEC-STOCK-01', 'Stock sau reserve không âm', 'InventoryVerification.z3; CK_Products_Stock_Nonnegative', 'AGGREGATED_STOCK_CANNOT_BE_NEGATIVE; Database_rejects_negative_stock_and_nonpositive_quantity', 'inventory_verification.results.json; application_tests.results.json', 'SMT_POSTGRES', 'SMT hai dòng cùng sản phẩm, không chứng minh concurrency'),
    ('SEC-CART-01', 'Quantity hợp lệ, tổng cộng không vượt stock', 'CartService.AddItemToCartAsync / UpdateCartItemAsync', 'InvalidCartAdditionDoesNotCreateCartOrItems; CartAdditionChecksCombinedQuantityAndLeavesTotalUnchangedOnRejection; InvalidCartUpdatePreservesItemAndTotal', 'application_tests.results.json', 'UNIT', 'Cart không giữ chỗ tồn kho; Order kiểm tra lại'),
    ('SEC-CART-02', 'Item và tổng giỏ hàng commit cùng transaction', 'CartService; Serializable transaction', 'Cart_updates_and_removal_commit_consistent_totals_on_PostgreSQL', 'application_tests.results.json', 'HTTP_POSTGRES', 'Chưa có fault injection giữa hai SaveChanges'),
    ('SEC-INPUT-01', 'User/Product từ chối dữ liệu không hợp lệ', 'RequestValidation; DTO annotations', 'InvalidRegistrationDoesNotPersistUser; InvalidProductCreateAndUpdateDoNotPersistChanges; Invalid_payloads_are_rejected_without_changing_persisted_data', 'application_tests.results.json', 'UNIT_HTTP_POSTGRES', 'Không thay thế rate limit hay audit'),
    ('REQ-4 / REQ-6', 'Bất biến tiền', 'PaymentVerification.z3; InputValidator; PaymentValidator', 'TEST1..TEST7; PaymentAmountMustEqualOrderTotal', 'payment_verification.results.json; application_tests.results.json', 'SMT_UNIT', 'Integer cents và decimal có miền biểu diễn khác nhau'),
    ('REQ-5 / REQ-14', 'Chuyển trạng thái đúng và liveness có giả định', 'Recovery P1/P10/P12; OrderPayment.tla', '6 negative mutations; state-machine tests', 'order_payment_recovery_model.results.json; tlc/OrderPayment.tlc.results.json; application_tests.results.json', 'MODEL_UNIT', 'TLC là evidence cũ; không chạy lại trong đợt này'),
    ('REQ-7 / REQ-18', 'Một logical payment và payment-cancel nhất quán', 'PaymentService; unique indexes; Version', 'Concurrent_payment_requests_create_only_one_charge_record; Concurrent_payment_and_cancellation_end_in_one_consistent_outcome', 'application_tests.results.json', 'HTTP_POSTGRES', 'Gateway mock trong process'),
    ('REQ-16 / REQ-17', 'UNKNOWN giữ kho; refund giữ dấu vết charge', 'Recovery P13/P14; PaymentService', 'ExhaustedUnknownPaymentRemainsReservedUntilOutcomeIsKnown; SuccessfulRefundCompensatesOrderAndReleasesInventory', 'order_payment_recovery_model.results.json; application_tests.results.json', 'MODEL_UNIT', 'Process-kill charge recovery đã có; refund process-kill sau journal đã xác minh; crash windows khác/gateway thật chưa xác minh'),
    ('REQ-9..REQ-13', 'Ownership/lifetime trong mô hình C', 'c-memory/modules/*.c', 'BUGGY/FIXED; scanner; sanitizer', 'c-memory/evidence/static-analysis.json; c-memory/evidence/dynamic-analysis.json', 'HISTORICAL_C', 'Không chạy lại; không chứng minh C# an toàn bộ nhớ'),
    ('NFR-2', 'Coverage và fuzz cho target cụ thể', 'SecurityFuzzingTests', '3 fuzz campaigns', 'application_fuzzing.results.json; coverage_summary.json', 'TARGET_COVERAGE', 'Chỉ target fuzz và validator; không phải toàn API'),
]

def main():
    tests = collect_tests()
    comparison = compare_models()
    write(RESULTS / 'application_tests.results.json', tests)
    write(RESULTS / 'model_projection.results.json', comparison)
    with (RESULTS / 'traceability_matrix.csv').open('w', newline='', encoding='utf-8') as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(['requirement', 'property', 'implementation_or_model', 'test_or_check', 'evidence', 'level', 'limitations'])
        writer.writerows(TRACE)
    artifacts = []
    # Only these project roots are traversed; ATPM is never visited.
    for folder in ('OrderPaymentMicroservice', 'OrderPaymentMicroservice.Tests', 'OrderPaymentMicroservice.IntegrationTests', 'OrderPaymentMicroservice.CrashProbe', 'security-analysis'):
        for path in sorted((ROOT / folder).rglob('*')):
            relative = path.relative_to(ROOT)
            if not path.is_file() or any(part in {'bin', 'obj', '__pycache__', 'dotnet', 'build', 'TestResults'} for part in relative.parts):
                continue
            if path.name == 'evidence_manifest.json' or path.suffix not in {'.cs', '.csproj', '.c', '.h', '.py', '.json', '.tla', '.cfg', '.z3', '.csv', '.md', '.xml', '.log', '.js', '.html'}:
                continue
            artifacts.append({'path': str(relative), 'sha256': digest(path), 'bytes': path.stat().st_size})
    workbook = ROOT / 'OrderPaymentMicroservice_Security_Spec.xlsx'
    artifacts.append({'path': workbook.name, 'sha256': digest(workbook), 'bytes': workbook.stat().st_size})
    revision = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=ROOT, capture_output=True, text=True, check=True).stdout.strip()
    dirty = bool(subprocess.run(['git', 'status', '--porcelain'], cwd=ROOT, capture_output=True, text=True, check=True).stdout.strip())
    write(RESULTS / 'evidence_manifest.json', {
        'schema_version': 1, 'packaged_at_utc': datetime.now(timezone.utc).isoformat(),
        'git_base_commit': revision, 'working_tree_dirty': dirty,
        'dotnet_version': subprocess.run(['dotnet', '--version'], capture_output=True, text=True, check=True).stdout.strip(),
        'fresh_checks': ['application_tests.results.json', 'checkout_retry.results.json', 'backup_restore.results.json', 'load_probe.results.json', 'refund_restart.results.json', 'order_retry_load.results.json', 'application_fuzzing.results.json', 'coverage_summary.json', 'model_projection.results.json', 'inventory_verification.results.json', 'payment_verification.results.json', 'order_payment_recovery_model.results.json', 'teacher_spec_verification.json', 'liveness_audit.results.json', 'process_restart.results.json', 'threat_model_results.json', 'chapter4_run.results.json', 'c-memory/evidence/static-analysis.json', 'c-memory/evidence/dynamic-analysis.json'],
        'historical_checks_not_rerun': ['tlc/OrderPayment.tlc.results.json'],
        'artifacts': artifacts,
        'limitations': ['Hash captures packaging-time bytes, not historical tool-run provenance.',
                        'Base commit plus dirty working-tree artifact hashes identify uncommitted changes.',
                        'No course tools or files in ATPM were accessed.'],
    })
    for name, suite in tests['suites'].items():
        print(f"{name}: {suite['passed']}/{suite['total']} passed, {suite['skipped']} skipped")
    print(f"Model projection: {'PASS' if comparison['passed'] else 'FAIL'}; {comparison['recovery_event_transitions']} events -> {comparison['pipeline_state_pairs']} state pairs")
    if not tests['passed'] or not comparison['passed']:
        raise SystemExit(1)

if __name__ == '__main__':
    main()
