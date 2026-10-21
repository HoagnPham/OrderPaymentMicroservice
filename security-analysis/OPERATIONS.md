# Hướng dẫn vận hành

Tài liệu dành cho việc chạy demo, kiểm tra sức khoẻ hệ thống, backup/restore và xử lý
sự cố trong phạm vi local. Không phải runbook production.

## 1. Chuẩn bị

| Thành phần | Phiên bản đã dùng | Ghi chú |
| --- | --- | --- |
| .NET SDK | 10.0.112 | build và test |
| PostgreSQL | 16 | integration, reconnect, backup/restore |
| Python | 3.12 | script phân tích |
| Z3 | trên `PATH` | SMT |
| Chrome/Chromium | một bản desktop | live smoke |

`pg_dump`, `pg_restore`, `pg_ctl`, `initdb`, `createdb` phải có trên `PATH`, hoặc đặt
`POSTGRES_BIN` trỏ tới thư mục `bin` của PostgreSQL.

```bash
dotnet restore OrderPaymentMicroservice.slnx
dotnet build OrderPaymentMicroservice.slnx --no-restore --nologo
```

## 2. Cấu hình

Không commit credential. Dùng user secrets hoặc biến môi trường.

| Khoá | Bắt buộc | Mặc định | Ý nghĩa |
| --- | --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | có | rỗng | PostgreSQL của môi trường demo |
| `Jwt__Key` | có | rỗng | ≥ 32 ký tự; đổi key làm token cũ mất hiệu lực |
| `Jwt__Issuer` | không | `OrderPaymentMicroservice` | issuer |
| `Jwt__Audience` | không | `OrderPaymentMicroservice.Client` | audience |
| `PaymentGateway__Mode` | không | `DurableFake` | chế độ gateway |
| `PaymentGateway__StoreDirectory` | không | `App_Data/payment-gateway` | journal của DurableFake |
| `PaymentGateway__RequestTimeoutMilliseconds` | không | `5000` | timeout caller (50–60.000) |
| `PaymentRecovery__Enabled` | không | `true` | bật worker đối soát |
| `PaymentRecovery__ScanIntervalSeconds` | không | `30` | chu kỳ quét (≥ 5) |
| `PaymentRecovery__StaleAfterSeconds` | không | `60` | tuổi tối thiểu để coi là treo |
| `PaymentRecovery__BatchSize` | không | `50` | số bản ghi mỗi lượt (1–500) |
| `ApiAdmission__MaxBodyBytes` | không | `65536` | giới hạn body |
| `ApiAdmission__MaxOrderItems` | không | `100` | số dòng tối đa |
| `ApiAdmission__ApiPermitLimit` | không | `120` | request / cửa sổ |
| `ApiAdmission__WindowSeconds` | không | `60` | độ dài cửa sổ |
| `Operations__StuckAfterSeconds` | không | `300` | ngưỡng cảnh báo treo |
| `Operations__QueryTimeoutMilliseconds` | không | `2000` | timeout truy vấn status |

Ví dụ:

```bash
read -r -s -p 'Connection string: ' ConnectionStrings__DefaultConnection
export ConnectionStrings__DefaultConnection
export Jwt__Key="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
```

## 3. Chạy ứng dụng

```bash
dotnet run --project OrderPaymentMicroservice/OrderPaymentMicroservice.csproj \
  --no-restore --launch-profile http
```

- Giao diện: `http://localhost:5246`
- OpenAPI (Development): `/openapi/v1.json`
- Startup chạy `Database.Migrate` và seed trên database đã cấu hình.

Cảnh báo: startup migrate/seed vào database được trỏ tới. Không trỏ vào database
dùng chung hoặc dữ liệu thật.

## 4. Health và trạng thái vận hành

### 4.1 Health endpoints (anonymous)

| Endpoint | Khi khoẻ | Khi lỗi | Cache |
| --- | --- | --- | --- |
| `GET /health/live` | `200 {"status":"alive"}` | — | `no-store` |
| `GET /health/ready` | `200 {"status":"ready"}` | `503 {"status":"unavailable"}` | `no-store` |

`ready` chạy `SELECT 1` với timeout `Operations__QueryTimeoutMilliseconds`. Nó phản
ánh khả năng truy vấn DB, không phản ánh gateway hay worker.

### 4.2 Operations status (Admin)

```bash
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5246/api/operations/status
```

Trả về:

| Trường | Ý nghĩa |
| --- | --- |
| `observedAt` | thời điểm đọc |
| `pendingCount` | payment `PROCESSING` / `UNKNOWN` / `REFUND_PENDING` |
| `stuckCount` | pending lâu hơn `StuckAfterSeconds` |
| `oldestPendingSeconds` | tuổi bản ghi pending lâu nhất |
| `incompleteAuditCount` | `BEGIN` không có `RESULT` quá hạn |
| `recoveryEnabled` | worker có bật |
| `alerts` | danh sách mã cảnh báo |
| `process` | counter trong process hiện tại |

Mã cảnh báo:

- `PAYMENT_STUCK`
- `AUDIT_RESULT_MISSING`
- `RECOVERY_DISABLED_WITH_PENDING`
- `AUDIT_APPEND_FAILURE_SEEN`
- `OPERATIONS_QUERY_UNAVAILABLE` (chỉ trong log worker)

`OperationsAlertWorker` poll mỗi 30 giây và chỉ log khi tập cảnh báo **thay đổi**, tránh
spam. Counter là theo process; nhiều process thì mỗi process báo phần của mình.

## 5. Backup và restore

### 5.1 Điều kiện

- Writer đã dừng (backup "stopped-writer"), không phải online backup.
- Cùng máy, cùng cluster local.
- Có đủ `pg_dump` / `pg_restore`.

### 5.2 Drill tự động

```bash
dotnet test OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~OperationalDrillTests.BackupRestoresDatabaseReceiptsAuditAndGatewayJournalThenRecoversPendingCharge' \
  --blame-hang-timeout 120s
```

Test làm:

1. Tạo database nguồn, ghi order/payment/audit/receipt và journal gateway.
2. `pg_dump --format=custom`.
3. `pg_restore --list` để kiểm archive.
4. Restore sang database đích với `--exit-on-error`.
5. Khởi động API trên database đích, để `PaymentRecoveryWorker` đối soát.
6. Ghi `backup_restore.results.json` gồm `backup_ms`, `restore_ms`, `restored_api_recovery_ms`
   và số charge/refund/release.

Kỳ vọng: dữ liệu và audit khôi phục đủ; payment pending được đối soát **một lần**,
không charge thêm.

### 5.3 Giới hạn

- Không phải PITR, không có WAL archiving.
- Không có RPO/RTO production.
- Không chứng minh backup nhất quán khi writer vẫn chạy.
- Gateway journal phải được backup cùng thư mục `PaymentGateway__StoreDirectory`.

## 6. Diễn tập mất kết nối PostgreSQL

```bash
python3 security-analysis/run_recovery_demo.py
```

Script tự tạo cluster và API tạm, chạy hai test:

| Case | Kỳ vọng |
| --- | --- |
| Charge reconnect | `SUCCESS/PAID/RESERVED`; `charge=1`, `release=0`; `ready` 200 → 503 → 200 |
| Refund reconnect | `REFUNDED/RELEASED`; `charge=1`, `refund=1`, `release=1` |

Điểm quan trọng: **cùng một API process** giữ nguyên pool; chỉ PostgreSQL bị dừng và
khởi động lại. Đây không phải failover, không phải network partition.

## 7. Diễn tập kill process

| Kịch bản | Lệnh | Evidence |
| --- | --- | --- |
| Kill API sau khi đã có charge trong journal | `dotnet test ... --filter 'FullyQualifiedName~ProcessRestartTests'` | `process_restart.results.json` |
| Kill API trong lúc refund | `dotnet test ... --filter 'FullyQualifiedName~RefundRestartTests'` | `refund_restart.results.json` |

Kỳ vọng: process mới đọc journal/gateway cũ và hoàn tất đối soát, không charge hoặc
refund lần hai.

## 8. Log và audit

- Log ứng dụng: console (provider mặc định), level cấu hình trong `Logging`.
- Audit nghiệp vụ: bảng `AuditEvents`, có `CorrelationId`, `Phase`, `Outcome`,
  `HttpStatusCode`.
- Mỗi response được audit có header `X-Correlation-Id`; dùng mã này để đối chiếu
  `BEGIN` và `RESULT`.
- Audit là append-only ở tầng DB; sửa/xoá trực tiếp sẽ bị trigger chặn.

Truy vấn gợi ý:

```sql
SELECT "CorrelationId", "Action", "Phase", "Outcome", "HttpStatusCode", "CreatedAt"
FROM "AuditEvents"
WHERE "CorrelationId" = '...'
ORDER BY "CreatedAt";
```

## 9. Sự cố thường gặp

| Hiện tượng | Nguyên nhân thường gặp | Xử lý |
| --- | --- | --- |
| `Jwt:Key is not configured` | thiếu `Jwt__Key` | đặt key ≥ 32 ký tự |
| `ready` trả 503 | DB chưa lên, sai connection string, hết timeout | kiểm PostgreSQL, tăng timeout có kiểm soát |
| Payment kẹt `UNKNOWN` | gateway timeout hoặc process chết | xem `alerts`, kiểm journal, chạy reconcile |
| `PAYMENT_STUCK` | pending quá `StuckAfterSeconds` | kiểm worker có bật và DB có ghi được |
| `AUDIT_RESULT_MISSING` | process chết giữa `BEGIN` và `RESULT` | đối chiếu correlation, không sửa audit |
| 429 | vượt rate limit | chờ hết cửa sổ hoặc tăng limit có chủ đích |
| `409 ORDER_CONCURRENT_WRITE` | cạnh tranh ghi | retry với **cùng** key và payload |
| `409 IDEMPOTENCY_PAYLOAD_MISMATCH` | key cũ dùng với payload mới | dùng key mới, không retry |
| Test integration fail vì thiếu `pg_ctl` | thiếu PostgreSQL binaries | đặt `POSTGRES_BIN` |

## 10. Dừng và dọn dẹp

- Dừng API bằng `Ctrl+C`; worker nhận `stoppingToken` và dừng theo host.
- Các test tự tạo cluster/database tạm và dọn trong `finally`; nếu bị kill, kiểm tra
  thư mục tạm và tiến trình `postgres` còn sót.
- Không xoá `security-analysis/results/` nếu cần giữ bằng chứng.
- `bin/`, `obj/`, `TestResults/` là output tái tạo được.

## 11. Giới hạn vận hành

1. Một máy, một cluster local; không HA.
2. DurableFake thay cho provider thật.
3. Rate limit process-local.
4. Không có metrics exporter, tracing phân tán hay alerting ra ngoài.
5. Không có retention job cho audit.
6. Không có DB RBAC tách biệt; app dùng một role.
7. Không có Kubernetes/rolling deploy.
