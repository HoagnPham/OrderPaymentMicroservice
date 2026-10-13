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
đối chiếu counts/checksums. Chưa có RPO/RTO được đo từ restore drill thực tế.

## 4. Giám sát và xử lý sự cố

Các query sau chỉ đọc, dành cho operator có quyền phù hợp:

```sql
-- Processing/UNKNOWN/refund pending quá stale window mặc định 60 giây.
SELECT "Id", "OrderId", "Status", "UpdatedAt"
FROM "Payments"
WHERE "Status" IN (4, 5, 6)
  AND "UpdatedAt" < now() - interval '60 seconds';

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
scan, disk/database gần đầy, trigger bị disable và gateway deadline tăng. Project
hiện có log và query hỗ trợ; chưa tích hợp exporter/dashboard/notification tự động.
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

Số liệu concurrency hiện là regression hữu hạn, chưa có throughput/p95/soak benchmark.
Hai host trong cùng máy không chứng minh cluster nhiều máy. Quota local vẫn nhân
theo số process; muốn quota toàn hệ thống cần thiết kế shared limiter/trusted proxy.
Các lệnh trong runbook không truy cập ATPM.
