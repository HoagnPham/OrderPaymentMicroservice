# Hợp đồng hiện thực Order/Payment

Cập nhật 13/10/2026. Quy tắc dưới đây phân biệt hành vi đã hiện thực với phần mở
rộng thiết kế. Chúng không là proof refinement đầy đủ.

| Chính sách | Source | Model / evidence | Trạng thái |
| --- | --- | --- | --- |
| Cancel khi payment UNKNOWN phải bị từ chối | OrderService.CancelOrderAsync | PAYMENT_RETRY / cancel_during_retry → PAYMENT_RETRY; P14; unit exhausted-unknown | IMPLEMENTED_POLICY |
| Giữ dấu vết successful charge qua restart | DurableFakePaymentGateway; PaymentRecoveryWorker | ProcessRestartTests; process_restart.results.json | VERIFIED_LOCAL_PROCESS_KILL |
| Reconciliation không tăng attempt hoặc tạo khoản thu | PaymentService.ReconcilePaymentAsync | Worker recovery sau restart; một journal entry không đổi; AttemptCount=1 | VERIFIED_IN_TEST_SCOPE |
| Confirmation retry tự động với budget | Chưa có counter/scheduler confirmation | CONFIRMATION_RETRY và các event tương ứng | DESIGN_EXTENSION |
| Gateway callback được xác thực | Chưa có adapter callback thật | Assumption trong recovery model | EXTERNAL_ASSUMPTION |
| UNKNOWN cuối cùng luôn được resolve | Phụ thuộc definitive result từ provider | Audit raw AG AF=false; progress-only=true | CONDITIONAL_LIVENESS |

## Gateway mô phỏng bền vững

`PaymentGateway:Mode=DurableFake` là mặc định. `StoreDirectory` mặc định là
`App_Data/payment-gateway`, tương đối với ContentRoot. Không đặt journal trong
wwwroot. MemoryFake vẫn có thể được chọn rõ ràng; integration fixture thông thường
chọn mode này để không chia sẻ ledger giữa test.

Mỗi operation/key dùng tên file SHA-256, không dùng key làm đường dẫn. Journal
bind PaymentId, OrderId, Amount và key; refund còn bind transaction của charge.
File lock tuần tự hóa access trong mô phỏng; mỗi entry được ghi file tạm, flush
rồi rename. Request lặp trả cùng result; mismatch bị từ chối. Status missing
trả UNKNOWN, không tự phát charge. Thời gian chờ lock giới hạn 10 giây và có
CancellationToken. Giới hạn này không phải timeout policy của gateway mạng thật.

Dữ liệu tồn tại qua việc dừng process API khi giữ nguyên thư mục ledger. Đây là
mô phỏng local filesystem; chưa chứng minh power loss, distributed filesystem,
shared-volume semantics hay bảo mật provider. API và ledger không tạo một distributed
transaction; khi local finalize lỗi, recovery hỏi ledger bằng cùng key.

## Thí nghiệm dừng process

`OrderPaymentMicroservice.CrashProbe` là executable test riêng, sử dụng cùng
Program.BuildApplication và middleware/service/database của API. Decorator test
chỉ chặn sau khi DurableFake đã publish journal; API sản phẩm không có endpoint
hay cấu hình làm crash theo yêu cầu client.

Test tạo user/order, phát payment, đợi marker được publish rồi xác nhận database
còn PROCESSING/PAYMENT_PENDING và stock đã reserve. Test kill OS process, đặt
payment stale trong database tạm, rồi chạy một OS process mới với worker bật,
cùng database và ledger. Worker phải tự chuyển SUCCESS/PAID, giữ transaction code,
AttemptCount=1, đúng một payment và đúng một journal entry không đổi; stock vẫn
reserve. Setting stale phục vụ chọn candidate, không thay đổi outcome gateway.

Evidence: [process_restart.results.json](results/process_restart.results.json).
Đây là thí nghiệm kill API thật; không là test mất điện hoặc provider sandbox.
Refund instance restart có unit test; crash giữa refund và finalize chưa có thí
nghiệm process kill tương tự. Gateway treo, lỗi disk và mọi crash window còn mở.

## Tái lập trong phạm vi project

```bash
dotnet restore OrderPaymentMicroservice.slnx
dotnet test OrderPaymentMicroservice.slnx --no-restore --nologo --logger 'trx;LogFilePrefix=verification' --results-directory security-analysis/results/dotnet --blame-hang-timeout 60s
python3 security-analysis/verify_recovery_model.py
python3 security-analysis/audit_liveness.py
python3 security-analysis/create_order_payment_spec.py
python3 security-analysis/verify_teacher_spec.py
python3 security-analysis/package_evidence.py
python3 security-analysis/build_report.py
python3 security-analysis/package_evidence.py
```

Ledger test nằm trong thư mục tạm và được cleanup sau khi process dừng; không
thay đổi database phát triển. Các lệnh trên không truy cập ATPM.

## Timeout và precision — bổ sung 13/10/2026

`PaymentGateway:RequestTimeoutMilliseconds` mặc định 5000, được validate 50..60000
khi startup. TimeoutPaymentGateway bọc cả charge/refund/status. Deadline giữ kết
quả chưa rõ ở UNKNOWN hoặc REFUND_PENDING, không suy timeout thành decline.
Cancellation của worker được truyền qua status lookup và thao tác database;
shutdown không invent outcome. Candidate tiếp theo được xử lý với tracker sạch.

Wrapper giới hạn caller waiting kể cả khi adapter không tuân cancellation; late
fault được observe và late result không tự ghi state. Không có cơ chế cưỡng chế
dừng adapter, nên backpressure và resource budget cho task không hợp tác vẫn là
khoảng trống. GatewayTimeoutTests có oracle cho bốn operations, late success,
refund timeout, worker progress và shutdown trong lookup đang chạy.

Price/TotalAmount/Payment.Amount phải là bội của 0,01. Fractional cent bị từ chối,
không round âm thầm; trailing zeroes như 25,0100 được chấp nhận. API/service guard
và HTTP/PostgreSQL test xác minh dữ liệu không bị ghi khi sai scale và amount
25,01 round-trip. PostgreSQL vẫn có thể round raw SQL ngoài application policy.

Race test quan sát PostgreSQL SQLSTATE 40001 được Npgsql execution strategy bọc
trong InvalidOperationException. PersistenceConflicts nhận 40001/40P01 trong
exception chain để controller trả 409 ở đường xử lý tương ứng, không retry tự
động charge. Regression không nhận 23514/23505 là loại concurrency này.

## Admission và audit — bổ sung 13/10/2026

`ApiAdmission` mặc định: MaxBodyBytes=65536, MaxOrderItems=100,
AuthenticationPermitLimit=10, ApiPermitLimit=120, WindowSeconds=60. Pipeline:
routing → authentication → rate limiting → body limit → audit → authorization →
controller. Rate limit chạy trước buffering/JSON để request bị throttled không
đẩy chi phí parsing hoặc ghi audit. Body cap áp dụng cả khi thiếu Content-Length;
Kestrel còn có server limit cùng cấu hình. Item guard ở service bảo vệ lời gọi
trực tiếp ngoài MVC.

Auth login/register dùng connection-IP bucket; API dùng subject đã xác thực hoặc
connection IP cho anonymous. Quá quota trả 429 kèm Retry-After, không queue. Không
nhận X-Forwarded-For từ caller làm identity; deployment proxy cần cấu hình trusted
forwarders riêng. Fixed-window limiter là local mỗi process, không distributed
quota hoặc chứng minh chịu tải. Test host thông thường tăng quota để không trộn
throttle với test khác; test rate dùng host riêng quota nhỏ.

Audit lưu BEGIN và RESULT với correlation GUID do server sinh, actor numeric ID
hoặc SYSTEM/ANONYMOUS, action allowlist, resource ID/type, phase, outcome, status và
time. Không có body/header/key/password/JWT/exception-message columns. Resource
ID cho create được lấy từ Location trong RESULT; BEGIN create có thể chưa biết ID.
HTTP_SUCCESS là HTTP outcome, không tự khẳng định charge SUCCESS khi provider UNKNOWN.
Worker RESULT dùng state từ reconciliation.

AuditWriter dùng scope/DbContext riêng, không commit entity thay đổi chưa lưu của
service lỗi. Mỗi write có budget 5 giây. Migration thêm bảng/index và trigger chặn
UPDATE/DELETE/TRUNCATE. DBA vẫn có thể disable trigger hoặc đổi schema; chưa có
retention/purge policy hoặc cryptographic integrity. Không có public audit endpoint.

BufferedAuditResponse giữ cả transport Start/Complete cho các mutation được audit.
Nếu BEGIN lỗi, action không chạy và API trả 503. Nếu RESULT lỗi sau business commit,
API trả 503 và chỉ có BEGIN; caller phải reconcile trước retry, nhất là Order create
chưa có request idempotency. Correlation không tự làm hai commit atomic: transactional
outbox và durable binding với business transaction vẫn cần bổ sung. Không audit
các request 429/413 trước admission để tránh log amplification; bị authorization
reject trong đường mutation sau admission vẫn có audit.

Evidence gồm HTTP/PostgreSQL audit payment/cancel/refund, redaction sentinel, trigger
P0001, lỗi BEGIN trước action, lỗi RESULT sau commit và worker SYSTEM event trong
process-restart test. Migration chỉ đã chạy trên database tạm của test.

Lỗi admission/audit trả JSON `message`; audit 503 thêm `requiresReconciliation`
và correlationId. Flag false khi BEGIN thất bại trước action, true khi RESULT
thất bại sau action. CORS expose X-Correlation-Id/Retry-After cho origin được phép.
