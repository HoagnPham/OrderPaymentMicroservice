# Hướng dẫn vận hành và nghiệm thu

Cập nhật 13/10/2026. Đây là runbook cho project hiện tại; chưa thực hiện backup,
restore hoặc migration trên database phát triển của người dùng. Gateway hiện là
mô phỏng local, chưa có provider thu tiền thật.

## 1. Trước khi triển khai migration

- Chạy toàn suite trên PostgreSQL tạm; kiểm tra migration từ phiên bản đang dùng.
- Sao lưu database và gateway journal, lưu connection/JWT secret ngoài repository.
- Kiểm tra stock/quantity/price cũ trước các migration CHECK; không tự xóa dữ liệu lỗi.
- Migration mới thêm ba cột receipt, unique customer/key và audit row triggers.
  Đơn cũ có key null; không backfill audit hoặc idempotency cho lịch sử.
- Tách tài khoản migration có quyền DDL và runtime chỉ có quyền nghiệp vụ cần thiết.
  Runtime không được ALTER/DROP trigger, TRUNCATE AuditEvents hoặc sở hữu schema.
  Mặc định project chưa tự provision các PostgreSQL roles này.

Ứng dụng hiện gọi Database.Migrate khi startup. Muốn chạy với runtime role ít quyền,
cần áp dụng migration bằng migration role trước, rồi khởi động runtime trên schema
đã cập nhật. Không đặt credential của migration role vào mọi replica.

## 2. Sao lưu nhất quán

Đích sao lưu gồm PostgreSQL (orders/payments/audit/receipt), cấu hình secrets được
quản lý riêng và thư mục `PaymentGateway:StoreDirectory`. Chỉ backup database sẽ mất
nguồn đối soát của DurableFake khi gateway đã charge nhưng local finalize chưa xong.

Trong mô phỏng, dừng API và recovery worker ở mọi instance, chờ chúng thoát, rồi
sao lưu DB cùng journal bất biến. Không sao chép ledger đang ghi để gọi đó là một
snapshot nhất quán. Không copy `App_Data` vào wwwroot. Backup phải có quyền truy cập
hạn chế và mã hóa khi lưu/chuyển vì chứa thông tin giao hàng cùng dữ liệu giao dịch.

Ví dụ chạy thủ công sau khi đặt biến môi trường riêng (không ghi password vào lịch sử
shell; dùng cơ chế credential PostgreSQL của môi trường):

```bash
pg_dump --dbname="$ORDERPAYMENT_DB_URL" --format=custom --file="$ORDERPAYMENT_BACKUP_FILE"
pg_restore --list "$ORDERPAYMENT_BACKUP_FILE"
tar --create --file="$ORDERPAYMENT_LEDGER_BACKUP" --directory="$ORDERPAYMENT_LEDGER_DIRECTORY" .
sha256sum "$ORDERPAYMENT_BACKUP_FILE" "$ORDERPAYMENT_LEDGER_BACKUP"
```

`pg_restore --list` chỉ kiểm tra archive đọc được; không thay thế restore drill.
Lưu manifest/checksum, phiên bản application/migrations, cấu hình mode, thời điểm
snapshot và đường dẫn ledger cùng backup. Chỉ khởi động lại sau khi hoàn tất.

## 3. Khôi phục thử vào môi trường riêng

Tạo database và thư mục ledger mới, không ghi đè dữ liệu đang phục vụ. Restore bằng
role có quyền tạo schema/functions/triggers; xác minh owner/runtime roles sau đó.

```bash
pg_restore --exit-on-error --dbname="$ORDERPAYMENT_RESTORE_DB_URL" "$ORDERPAYMENT_BACKUP_FILE"
tar --extract --file="$ORDERPAYMENT_LEDGER_BACKUP" --directory="$ORDERPAYMENT_RESTORE_LEDGER_DIRECTORY"
```

Trỏ bản chạy riêng tới DB/ledger vừa restore, không dùng original StoreDirectory.
Kiểm tra migration history và trigger có mặt trước khi bật worker. Bật worker với
gateway snapshot, kiểm tra UNKNOWN/PROCESSING được đối soát bằng key cũ, không phát
charge mới. Replay create key cũ phải trả cùng order ID/receipt; GET trả live state.
Kiểm tra stock không âm và mutation mới có COMMIT audit. Ghi thời gian phục hồi và
đối chiếu counts/checksums. Restore drill trong fixture tạm đã đo timing; chưa có
RPO/RTO được đo/cam kết trên dữ liệu và hạ tầng production.

## 4. Giám sát và xử lý sự cố

Các query sau chỉ đọc, dành cho operator có quyền phù hợp:

```sql
-- Processing/UNKNOWN/refund pending chờ kết quả quá 300 giây.
SELECT "Id", "OrderId", "Status", "UpdatedAt"
FROM "Payments"
WHERE "Status" IN (4, 5, 6)
  AND "PendingSince" < now() - interval '300 seconds';

-- HTTP observation thiếu RESULT; COMMIT riêng có thể đã tồn tại.
SELECT b."CorrelationId", b."Action", b."CreatedAt"
FROM "AuditEvents" b
WHERE b."Phase" = 'BEGIN' AND b."CreatedAt" < now() - interval '5 minutes'
  AND NOT EXISTS (SELECT 1 FROM "AuditEvents" r
                  WHERE r."CorrelationId" = b."CorrelationId" AND r."Phase" = 'RESULT');

SELECT pg_size_pretty(pg_total_relation_size('"AuditEvents"')) AS audit_size;
SELECT count(*) AS invalid_stock FROM "Products" WHERE "Stock" < 0;
SELECT tgname, tgenabled FROM pg_trigger
WHERE tgname IN ('orders_atomic_audit', 'payments_atomic_audit', 'audit_append_only', 'audit_no_truncate');
```

Thiết lập cảnh báo cho audit persistence errors/HTTP 503, backlog không giảm qua nhiều
scan, disk/database gần đầy, trigger bị disable và gateway deadline tăng. Project có health/status endpoint và worker ghi alert-code qua log khi trạng thái
cảnh báo đổi. Chưa có Prometheus exporter, dashboard hay notification ra ngoài.
Đối soát payment bằng status/key cũ, không suy timeout thành FAILED. Với order 409
hoặc mất response, gửi lại đúng creation key/payload; không tạo key mới khi chưa rõ.
Một BEGIN thiếu RESULT không đủ để kết luận nghiệp vụ thất bại: xem COMMIT và state.

## 5. Retention và phân quyền

Hiện giữ audit/receipt, không purge tự động. Không disable append-only triggers để
xóa log định kỳ. Chốt retention theo yêu cầu học phần/chính sách dữ liệu trước khi
triển khai archive/partition lifecycle; chưa coi runbook là một retention job đã chạy.
Archive cần giữ actor/correlation/resource/state/time, mã hóa, checksum và khả năng
restore/query. Receipt chứa địa chỉ nên phải theo cùng chính sách privacy của order.
Không xóa receipt của order còn có thể retry hoặc có giao dịch cần đối soát.

Đề xuất quản lý backup: giữ 7 bản ngày và 4 bản tuần trên storage có kiểm soát truy cập,
thực hiện restore drill hàng tháng, xác nhận archive mới phục hồi được trước khi loại
bỏ bản cũ. Đây là đề xuất vận hành, chưa có scheduled task tự động. DBA có thể sửa
schema/disable triggers; muốn chống tampering từ DBA cần external immutable sink.

## 6. Nghiệm thu và demo

```bash
dotnet test OrderPaymentMicroservice.slnx --no-restore --nologo
python3 security-analysis/verify_checkout.py
python3 security-analysis/package_evidence.py
python3 security-analysis/verify_threat_model.py
python3 security-analysis/build_report.py
python3 security-analysis/package_evidence.py
```

Demo trên dữ liệu test: cùng key/payload trả cùng order; đổi payload trả 409; đọc
state sau cancel; xem COMMIT audit cùng correlation; tái lập audit insert failure
rollback qua test; cho hai host tranh 10 stock; chạy process-kill recovery. Các test
fault injection dùng DB tạm, không bật failure trigger trên database vận hành.

Số liệu concurrency gồm regression hữu hạn và OS-process load probe có throughput/p95.
Chưa có saturation sweep/repetitions/soak benchmark hoặc SLA production.
Hai host trong cùng máy không chứng minh cluster nhiều máy. Quota local vẫn nhân
theo số process; muốn quota toàn hệ thống cần thiết kế shared limiter/trusted proxy.
Các lệnh trong runbook không truy cập ATPM.

## 7. Kết quả drill cục bộ đã kiểm chứng

`OperationalDrillTests` tạo cluster PostgreSQL tạm và chạy executable CrashProbe
trên port loopback. Backup thử dùng pg_dump/pg_restore thật và tar ledger; so sánh
9 bảng trước worker, xác minh trigger/identity/receipt và recovery một charge đang
PROCESSING. DB nguồn không đổi, archive dữ liệu test được xóa sau assertions.
Không truy cập database phát triển, credential thật hoặc ATPM.

Load probe chạy hai process API thật, 200 GET và 96 create (concurrency 8), giữ
raw status/latency samples và nearest-rank percentiles. Quota read/write nâng lên
100000 để tách contention, scenario quota riêng 5/phút xác minh 5/20 accepted.
Không retry write 409 trong phép đo; successful requests/s được ghi riêng với
requests/s. Đọc số liệu lần mới nhất từ evidence; không lấy chúng làm capacity SLA.

- [Backup/restore evidence](results/backup_restore.results.json)
- [Load probe evidence](results/load_probe.results.json)
- [Bảng số liệu báo cáo](report/BAO_CAO.md#69-restore-drill-và-tải-http-qua-hai-os-process)

Chạy riêng: `dotnet test OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~OperationalDrillTests`.
Cần local PostgreSQL server/client binaries cùng major version và quyền mở socket
loopback/start process; test được skip rõ ràng nếu không có local tools. Lần kiểm
chứng hiện tại không skip. Test full có TRX mới rồi package/build report để đồng bộ.

## 8. Health, số liệu giám sát và cảnh báo

- GET /health/live: anonymous, 200 `{status:alive}` khi process phục vụ được request.
- GET /health/ready: anonymous, SELECT 1 có deadline Operations:QueryTimeoutMilliseconds
  (mặc định 2000); trả 200 ready hoặc 503 unavailable, không trả exception/connection.
  Chỉ kiểm chứng DB query, không chứng minh writable schema, gateway hoặc zero backlog.
- GET /api/operations/status: JWT role Admin, no-store. Chỉ trả aggregate pending,
  stuck, oldest pending seconds, incomplete audit count, recovery enabled và process
  counters. Không trả order/payment/user ID, key, token, amount hay exception text.

PendingSince bắt đầu khi PROCESSING/UNKNOWN/REFUND_PENDING, giữ nguyên qua status
lookup và chuyển UNKNOWN→PROCESSING, xóa khi có definitive outcome. Refund bắt đầu
window mới sau SUCCESS. State machine và BEFORE INSERT/UPDATE DB trigger bảo vệ mốc.
Migration backfill pending records bằng UpdatedAt: lịch sử chưa có start time thật,
nên age của record cũ là lower bound, không suy timestamp này là thời điểm charge.
UpdatedAt vẫn dùng để chọn candidate stale trong worker, không dùng làm stuck age.

Operations:StuckAfterSeconds=300, IncompleteAuditAfterSeconds=300,
AlertPollingEnabled=true, AlertPollSeconds=30. Worker chỉ đọc DB, log mã khi tập alert
đổi: PAYMENT_STUCK, AUDIT_RESULT_MISSING, RECOVERY_DISABLED_WITH_PENDING,
AUDIT_APPEND_FAILURE_SEEN hoặc OPERATIONS_QUERY_UNAVAILABLE. Log không chứa PII.
AUDIT_APPEND_FAILURE_SEEN là sự kiện đã xảy ra trong process, không khẳng định store
vẫn hỏng; counter reset khi restart. Audit counter đếm lỗi AppendAsync của AuditWriter,
không là bộ đếm mọi COMMIT-trigger failure hoặc mock writer. Worker counters có scan
failure, số reconciliation hoàn tất và số definitive outcomes; không phải số charge.

Counts là truy vấn tuần tự, không một snapshot transaction nhất quán khi có mutation
đồng thời. Deadline bảo vệ query chậm. Rate limit của endpoint status vẫn theo policy
API local. Polling có thể tắt trong test hoặc môi trường dùng external collector.
Cảnh báo qua log cần hệ thống thu log/notification của deployment để gửi đến operator;
project chưa tự gửi email/webhook.

RefundRestartTests dừng API sau khi refund journal được publish nhưng local còn
REFUND_PENDING/RESERVED. Restart worker đối soát đúng refund transaction, giữ charge
reference, release tồn kho một lần; replay cùng key không refund/release lại. Hai
journal entries có hash không đổi. Test sau đó dừng cluster tạm: live vẫn 200, ready
và protected status trả 503. Đây không là failover hoặc reconnect recovery test.
Evidence: [refund_restart.results.json](results/refund_restart.results.json).

## 9. Retry tạo đơn và đọc kết quả đo

Shop tự retry chỉ đối với 409 code ORDER_CONCURRENT_WRITE/retryable=true, tối đa
4 attempts/8 giây, luôn dùng cùng key/payload. Hết kho, inactive/missing product,
key-payload mismatch, database constraint khác không tự retry. 429/401/403 giữ
attempt để chờ hoặc login lại; 503/network giữ trạng thái chưa rõ để kiểm tra/replay.
Không dùng backoff của create order để tự charge/refund lại. Mỗi attempt dùng quota
và tạo HTTP audit; lượng log/request có thể tăng khi contention cao.

[order_retry_load.results.json](results/order_retry_load.results.json) giữ hai
scenario 96 logical requests với 8 users/4 products/hai OS processes. Logical p95
gồm network + backoff, không gồm semaphore queue. HTTP attempt latencies và delays
có raw samples. successful_orders/s tách khỏi HTTP attempts/s; tăng success ratio
không đồng nghĩa mọi loại latency giảm. API/auth quotas test nâng 100000/10000 để
đo contention, không chứng minh quota production hoặc cluster shared limiter.
Baseline chạy trước và dùng product set khác; một lần local không có confidence
interval hoặc ý nghĩa thống kê. So sánh trên dữ liệu cuối trong báo cáo, không giữ
số đo cũ làm SLA. Muốn capacity claim cần repetitions, đổi thứ tự phases, tăng tải,
soak test và workload/provider thật. Retry không thay thế điều tra 500/503 hoặc lỗi
storage; ngưỡng/alerts đang có vẫn cần được quan sát.

## 10. Chạy lại project và smoke test giao diện thật

`ApplicationSmokeTests.NormalApplicationServesShopAndCompletesRealBrowserFlow`
khởi chạy normal API executable, không dùng crash decorator, trên PostgreSQL tạm.
Chrome dùng HTTP thật để đăng ký/login/cart/checkout/payment/history, reload và
logout. API_BASE_URL=/api nên shop theo đúng origin/HTTPS/port; không phải đi tới
một API cố định tại localhost:5246. Gateway vẫn là local simulation, UI không yêu
cầu hoặc gửi dữ liệu thẻ. Cache session sai định dạng trở về signed-out.

```bash
dotnet test OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ApplicationSmokeTests
```

Cần local PostgreSQL binaries và Chrome/Chromium. Python CDP driver dùng standard
library, không cài browser dependencies mới. External fonts bị block, chỉ API/UI
trên origin test được dùng. Kết thúc test dọn processes/temp files, không để API
background chạy vào database của người dùng. Chạy thủ công ứng dụng trên database
riêng vẫn cần ConnectionStrings:DefaultConnection và Jwt:Key hợp lệ ngoài repo;
startup Database.Migrate có thể nâng schema của database cấu hình đó.
Evidence/biên bản: [application_smoke.results.json](results/application_smoke.results.json),
[REVIEW_2026_10_13.md](REVIEW_2026_10_13.md).
