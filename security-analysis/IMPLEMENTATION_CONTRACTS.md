# Hợp đồng hiện thực

Tài liệu mô tả các hợp đồng mà source đang thực thi: gateway, timeout, payment
recovery, idempotency của order/payment, audit và retry phía client. Mỗi hợp đồng
nêu rõ **bên nào bảo đảm**, **được test ở đâu** và **giới hạn**.

Quy ước: "bảo đảm" nghĩa là có code + test/check chạy được; "giả định" nghĩa là
điều kiện môi trường phải đúng; "giới hạn" nghĩa là không được suy rộng.

## 1. Gateway thanh toán

### 1.1 Hợp đồng interface

`OrderPaymentMicroservice/Services/Payments/IPaymentGateway.cs`

```csharp
Task<GatewayResult> ChargeAsync(GatewayPaymentRequest request, CancellationToken ct = default);
Task<GatewayResult> RefundAsync(GatewayPaymentRequest request, CancellationToken ct = default);
Task<GatewayResult> GetChargeStatusAsync(GatewayPaymentRequest request, CancellationToken ct = default);
Task<GatewayResult> GetRefundStatusAsync(GatewayPaymentRequest request, CancellationToken ct = default);
```

`GatewayOutcome` chỉ có ba giá trị: `Succeeded`, `Declined`, `Unknown`.

Hợp đồng bắt buộc:

1. Mỗi lời gọi mang `IdempotencyKey` (1–128 ký tự) và định danh payment/order.
2. Cùng key + cùng payload trả lại **cùng kết quả**, không tạo giao dịch mới.
3. Cùng key + payload khác phải **bị từ chối**, không được ghi đè.
4. Không biết kết quả (timeout, lỗi mạng, adapter không hợp tác) trả `Unknown`,
   **không** trả `Declined`.
5. `Get*StatusAsync` chỉ đọc, không tạo giao dịch mới.

### 1.2 DurableFakePaymentGateway

`Services/Payments/DurableFakePaymentGateway.cs`

| Khía cạnh | Cách làm |
| --- | --- |
| Lưu trữ | Mỗi operation + key là một tệp JSON trong thư mục journal |
| Khoá | File lock `FileShare.None`, chờ 25 ms giữa các lần thử |
| Ghi bền | Ghi tệp tạm với `WriteThrough`, `Flush(flushToDisk: true)`, rồi `File.Move` |
| Ràng buộc payload | So `Operation`, `PaymentId`, `OrderId`, `Amount`, `IdempotencyKey`, `ChargeReference` |
| Key trùng payload khác | `InvalidOperationException("Gateway key is already bound to a different payment payload")` |
| Status khi chưa có | `Unknown` + `ErrorCode = NOT_FOUND` |
| Budget | 10 giây cho mỗi lần ghi/đọc journal |
| Mã giao dịch | `DURABLE-FAKE-{operation}-{guid}` |

Đây là **mô phỏng provider bền cục bộ**, không phải provider thật và không phải
giao thức phân tán. Nó sống qua restart của API vì journal nằm ngoài process.

### 1.3 TimeoutPaymentGateway

`Services/Payments/TimeoutPaymentGateway.cs`

| Khía cạnh | Giá trị |
| --- | --- |
| Timeout mặc định | 5.000 ms (`GatewayCallTimeoutOptions.RequestTimeoutMilliseconds`) |
| Miền cấu hình | 50–60.000 ms |
| Cơ chế | `CancellationTokenSource` liên kết với token của caller + `CancelAfter` |
| Khi hết hạn | Ném `TimeoutException` với thông điệp "outcome requires reconciliation" |
| Kết quả đến muộn | Bị quan sát để tránh unobserved exception nhưng **không được áp dụng** |

Hợp đồng: timeout chỉ giới hạn **thời gian chờ của caller**, không khẳng định
provider đã thất bại. Vì vậy `PaymentService` map timeout thành `Unknown` với
`ErrorCode = GATEWAY_TIMEOUT` / `REFUND_GATEWAY_TIMEOUT` / `RECONCILIATION_TIMEOUT`.

## 2. Payment lifecycle, retry và reconciliation

### 2.1 State machine

`Security/PaymentStateMachine.cs`

```text
PENDING      -> PROCESSING
PROCESSING   -> SUCCESS | FAILED | UNKNOWN
UNKNOWN      -> PROCESSING | FAILED
SUCCESS      -> REFUND_PENDING
REFUND_PENDING -> REFUNDED
```

`IsTerminal` chỉ gồm `FAILED` và `REFUNDED`. **`UNKNOWN` không terminal**: nó giữ
reservation và chờ đối soát.

Mỗi transition tăng `Version`; trạng thái chờ đặt/giữ `PendingSince` để worker và
operations status phát hiện giao dịch treo.

### 2.2 Retry

| Khía cạnh | Giá trị |
| --- | --- |
| Số lần charge tối đa | `MaxChargeAttempts = 2` (`PaymentService`) |
| Điều kiện retry | Chỉ khi payment đang `UNKNOWN` |
| Khi hết lượt | `LastErrorCode = OUTCOME_STILL_UNKNOWN`; giữ `UNKNOWN` |
| Retry refund | Dùng `RefundIdempotencyKey`, không tạo refund thứ hai |

Retry **không** tự động charge mù: trước khi quyết định, service hỏi trạng thái cũ
qua `GetChargeStatusAsync` / `GetRefundStatusAsync`.

### 2.3 Reconciliation worker

`Services/Payments/PaymentRecoveryWorker.cs`

| Khía cạnh | Giá trị |
| --- | --- |
| Mặc định | `Enabled = true` |
| Chu kỳ quét | 30 giây (tối thiểu 5) |
| Tuổi tối thiểu | 60 giây (tối thiểu 5) |
| Batch | 50 (kẹp 1–500) |
| Ứng viên | `PROCESSING`, `UNKNOWN`, `REFUND_PENDING` quá hạn |
| Hành vi | Chỉ đối soát, **không** phát charge mới |

Worker chạy trong process API. Nếu API chết, lần khởi động sau sẽ quét lại các bản
ghi còn `PendingSince` trong PostgreSQL.

### 2.4 Bất biến

| Bất biến | Cơ chế | Test/evidence |
| --- | --- | --- |
| Không charge hai lần cho một payment | Gateway key + `AttemptCount` + `UNKNOWN` không terminal | `PaymentServiceTests`, `process_restart.results.json` |
| Không refund hai lần | `RefundIdempotencyKey` + unique index | `refund_restart.results.json` |
| Refund chỉ sau khi có charge | `PaymentStatus.SUCCESS -> REFUND_PENDING` | state-machine tests |
| UNKNOWN giữ kho | Không release reservation khi chưa có kết quả dứt điểm | recovery model P13/P14 |
| Không release kho hai lần | `InventoryStatus` + transition một chiều | `refund_restart.results.json` |

## 3. Idempotency của Order

`Services/OrderService.cs`, `Controllers/OrdersController.cs`

### 3.1 Hợp đồng key

| Khía cạnh | Giá trị |
| --- | --- |
| Header | `Idempotency-Key` |
| Độ dài | 1–128 |
| Ký tự | Visible ASCII `!`..`~` |
| Nhiều header | `400 Provide exactly one Idempotency-Key` |
| Không có header | Hợp lệ; không replay |
| Lưu | `CreationKeyHash = SHA256(key)` |

### 3.2 Hợp đồng payload

`CreationRequestHash = SHA256(JSON)` trên:

- `CustomerId`
- `TotalAmount` (chuẩn hoá `G29`, invariant culture)
- `Address`, `City`
- `Items[]`: `ProductId`, `Quantity`, `UnitPrice` (`G29`)

Quy tắc:

1. Cùng key + cùng hash → trả lại `CreationResponseJson` đã lưu (replay).
2. Cùng key + khác hash → `409` với `code = IDEMPOTENCY_PAYLOAD_MISMATCH`,
   `retryable = false`.
3. Receipt thiếu → `409 ORDER_RECEIPT_UNAVAILABLE`.
4. Key gắn với `CustomerId`, nên user khác dùng lại key không nhận được đơn của nhau.

### 3.3 Transaction và conflict

- Transaction `Serializable` khi chạy trên relational provider.
- Unique index trên `(CustomerId, CreationKeyHash)`.
- Lỗi cạnh tranh ghi trả `409` dạng:

```json
{ "code": "ORDER_CONCURRENT_WRITE", "retryable": true, "message": "..." }
```

`retryable = true` chỉ khi có đúng một key hợp lệ. Các conflict nghiệp vụ
(`OUT_OF_STOCK`, `PRODUCT_UNAVAILABLE`, payload mismatch) trả `retryable = false`.

## 4. Audit

`Auditing/AuditWriter.cs`, `Auditing/AuditTrailMiddleware.cs`

### 4.1 Hợp đồng ghi

| Khía cạnh | Giá trị |
| --- | --- |
| Hành động được audit | allowlist 10 action order/payment |
| Pha | `BEGIN` và `RESULT` |
| Correlation | `X-Correlation-Id` |
| Actor | user id từ claim, hoặc `ANONYMOUS` |
| Kết quả | `HTTP_SUCCESS` / `HTTP_REJECTED` / `HTTP_FAILURE` / `UNHANDLED_ERROR` |
| Trường nhạy cảm | Không ghi body, không ghi secret |
| DB context | Scope riêng, không `SaveChanges` trên business context |
| Budget | 5 giây mỗi lần ghi |

Thứ tự: ghi `BEGIN` → chạy handler → buffer response → ghi `RESULT` → mới copy
response ra transport. Nếu `BEGIN` fail, trả `503` và **không** chạy nghiệp vụ.
Nếu `RESULT` fail, trả `503` thay vì để client tưởng thành công.

### 4.2 Hợp đồng DB

| Trigger | Tác dụng |
| --- | --- |
| `orders_atomic_audit` | Ghi audit `COMMIT` cùng transaction với mutation Order |
| `payments_atomic_audit` | Ghi audit `COMMIT` cùng transaction với mutation Payment |
| `audit_append_only` | Chặn `UPDATE`/`DELETE` trên `AuditEvents` |
| `audit_no_truncate` | Chặn `TRUNCATE` |

Vì trigger chạy trong cùng transaction, audit fail sẽ rollback cả mutation. Đây là
điểm khác với audit ở middleware (BEGIN/RESULT tách khỏi transaction nghiệp vụ).

## 5. Retry phía client

`OrderPaymentMicroservice/wwwroot/js/app.js`

| Khía cạnh | Giá trị |
| --- | --- |
| Hàm | `createOrderWithRetry` |
| Số lần | 4 (1 lần đầu + 3 retry) |
| Điều kiện | HTTP `409`, `code === ORDER_CONCURRENT_WRITE`, `retryable === true` |
| Jitter | Equal jitter: 100–200, 200–400, 400–800 ms |
| Key | Giữ nguyên trong `sessionStorage` (`order-create:{userId}`) |
| Deadline | 8.000 ms cho toàn bộ checkout |
| Payment key | Lưu riêng trong `sessionStorage` để retry không tạo payment mới |

Hợp đồng: chỉ retry khi server nói rõ là retryable. Không retry lỗi mạng, `503`,
`OUT_OF_STOCK`, `PRODUCT_UNAVAILABLE` hay payload mismatch.

## 6. Admission và rate limit

`Security/ApiAdmissionOptions.cs`, `Program.cs`

| Tham số | Mặc định | Miền |
| --- | --- | --- |
| `MaxBodyBytes` | 65.536 | 1.024–1.048.576 |
| `MaxOrderItems` | 100 | 1–1.000 |
| `AuthenticationPermitLimit` | 10 / cửa sổ | 1–10.000 |
| `ApiPermitLimit` | 120 / cửa sổ | 1–100.000 |
| `WindowSeconds` | 60 | 1–3.600 |

Partition key:

- Login/Register: `auth-ip:{remote IP}`
- API có JWT: `user:{id}`
- API chưa JWT: `api-ip:{remote IP}`
- Static file: không giới hạn

Giới hạn là **process-local**; nhiều API process không chia sẻ quota. Shared Redis
limiter là future work.

## 7. Bảng truy vết hợp đồng

| Hợp đồng | Source | Test/evidence |
| --- | --- | --- |
| Gateway idempotent | `DurableFakePaymentGateway.cs` | `PaymentServiceTests`, `process_restart.results.json` |
| Timeout → UNKNOWN | `TimeoutPaymentGateway.cs`, `PaymentService` | `GatewayTimeoutTests` |
| UNKNOWN giữ kho | `PaymentStateMachine.cs` | recovery model P13/P14 |
| Refund một lần | `PaymentService.RefundPaymentAsync` | `refund_restart.results.json` |
| Order replay | `OrderService.CreateOrderAsync` | `AtomicAuditIdempotencyTests` |
| Audit không giả thành công | `AuditTrailMiddleware.cs` | `MissingAuditResultReturns503...` |
| Audit nguyên tử | migration `audit_business_commit` | `AtomicAuditInsertFailureRollsBackOrderAndInventory` |
| Retry có biên | `app.js` | browser retry assertions |
| Admission | `ApiAdmissionOptions`, middleware | `AdmissionBoundaryTests` |

## 8. Giới hạn

1. Gateway là `DurableFake`, không phải PSP thật; không có webhook hay chữ ký.
2. Timeout không cưỡng chế được provider; chỉ giới hạn caller.
3. `MaxChargeAttempts = 2` là hằng số, chưa cấu hình theo môi trường.
4. Audit allowlist chỉ phủ order/payment; chưa phủ toàn bộ API.
5. Trigger DB có thể bị DBA tắt; không chứng minh được chống insider.
6. Rate limiter không chia sẻ giữa các process.
7. Client retry chỉ dành cho create order; payment retry do người dùng/worker quyết định.
8. Các state `PRE_PAYMENT_REJECTED`, `PAYMENT_RETRY`, `PAYMENT_PROCESSING_FINAL`
   trong workbook là trừu tượng, không phải enum runtime.
