## 2. Mô tả chương trình mục tiêu và hợp đồng nghiệp vụ

### 2.1. Kiến trúc thực thi và kiến trúc kiểm chứng

```mermaid
flowchart TD
    Client[Shop / Customer / Admin] -->|TB1: HTTP, JWT, JSON| Auth[Routing / JWT authentication]
    Auth --> Admission[Rate limiter / Body limit]
    Admission --> API[Audit middleware / Authorization / Controllers]
    API --> Services[User / Product / Cart / Order / Payment Services]
    Services --> Guards[Validation / State machines]
    API --> Audit[AuditWriter: independent DbContext]
    Audit --> DB
    Worker --> Audit
    Services -->|TB2: EF Core, transaction| DB[(PostgreSQL)]
    Services -->|TB3: charge, refund, status| Gateway[IPaymentGateway / FakePaymentGateway]
    Config[Host config / Secrets] -->|TB4| API
    Worker[PaymentRecoveryWorker] --> Services
    Model[Recovery graph / TLA+ / SMT] -. đặc tả và đối chiếu .-> Services
    Tests[Unit / HTTP test host / C analysis] -. kiểm tra trong phạm vi riêng .-> Services
```

Các cạnh nét liền mô tả tương tác runtime; nét đứt là hoạt động kiểm chứng ngoài
runtime. Z3, checker Kripke và sanitizer không phải thành phần mà API gọi để xử lý
mỗi request. State machine chỉ kiểm cặp trạng thái; service bổ sung điều kiện như
payment SUCCESS, amount equality và inventory RESERVED.

`Program.cs` đăng ký service scoped theo request, DbContext scoped, gateway mô phỏng
singleton và recovery worker. Khởi động yêu cầu connection string và JWT key dài
ít nhất 32 ký tự; `Data.DbInitializer` chạy migration và seed Product nếu chưa có.
Môi trường Development có OpenAPI và developer exception page; test host dùng
Testing. Các điều kiện này là cấu hình của source, không phải xác nhận môi trường
triển khai ngoài test đã được harden.

### 2.2. Mô hình dữ liệu và lớp bảo vệ

| Aggregate | Dữ liệu quan trọng | Ràng buộc hiện thực | Ý nghĩa và giới hạn |
| --- | --- | --- | --- |
| User | Email, PasswordHash, Role, IsActive | Email unique; hash password; JWT role | Role trong token đã phát hành không tự cập nhật khi database đổi |
| Product | Price, Stock, UpdatedAt, IsActive | CHECK Price>0/Stock≥0; UpdatedAt concurrency token | Application giới hạn giá; CHECK database không thay toàn bộ DTO validation |
| ShoppingCart/CartItem | UserId, Quantity, PriceAtAdd, SubTotal, TotalPrice | Ownership; CHECK Quantity>0; transaction khi add/update/remove | Cart không reserve; giá tại lúc thêm có thể khác giá lúc checkout |
| Order/OrderItem | CustomerId/UserId, TotalAmount, Status, InventoryStatus, Version | Giá server; tổng quantity theo ProductId; CHECK item quantity; Version token | Một request có thể có nhiều dòng trùng; kiểm kho dùng tổng nhưng giữ dòng riêng |
| Payment | OrderId, Amount, Status, key, refund key, AttemptCount, Version | Unique OrderId/key/refund key; exact amount; Version token | Một Order có một logical Payment record trong schema hiện tại |

Tiền lưu `numeric(19,2)`; runtime dùng `decimal`. Request Order vẫn mang UnitPrice
và TotalAmount để validation và kiểm mismatch, nhưng giá được ghi vào OrderItem
lấy từ Product. Request CustomerId bị Controller ghi đè bằng JWT subject. Vì vậy
một client truyền CustomerId khác không có quyền thay owner của đơn.

### 2.3. Tạo đơn và giữ tồn kho

`CreateOrderAsync` kiểm input, mở transaction Serializable, lấy các Product cần
thiết và tính tổng quantity bằng `long` theo ProductId. Service kiểm Product tồn
tại, active và stock đủ tổng lượng; tính serverTotal và kiểm chênh lệch với total
client không vượt 0,01. Chỉ sau các guard mới trừ kho, tạo OrderItem bằng giá server
và chuyển CREATED→VALIDATED→PAYMENT_PENDING trước khi lưu/commit.

CREATED và VALIDATED trong thao tác tạo mới thường là trạng thái trong bộ nhớ,
không phải hai bản ghi intermediate đã commit để endpoint đọc được. Khi bị reject
trước persist, không có Order durable tương ứng với PRE_PAYMENT_REJECTED của model.
Reservation được biểu diễn bằng InventoryStatus và việc giảm Product.Stock;
không có bảng reservation riêng. Đây là điều cần nêu rõ khi vẽ ERD hoặc mô tả rollback.

Ví dụ Product stock=10/price=25: hai dòng quantity=6 bị từ chối dù từng dòng đều
nhỏ hơn 10. Hai dòng quantity=5 được chấp nhận, stock về 0; cancel hợp lệ trả stock
về 10 và lần cancel thứ hai không hoàn thêm. Database CHECK là lớp cuối nếu một
đường code khác bỏ qua guard, nhưng không thay thế kiểm tổng lượng ở service.

### 2.4. Thanh toán và cửa sổ lỗi

```mermaid
sequenceDiagram
    participant C as Client
    participant S as PaymentService
    participant D as PostgreSQL
    participant G as Gateway
    C->>S: OrderId, Amount, Idempotency-Key
    S->>D: Tx1: validate + persist PROCESSING
    D-->>S: Commit
    S->>G: Charge cùng key
    G-->>S: Succeeded / Declined / Unknown
    S->>D: Tx2: finalize Payment + Order + inventory
    D-->>S: Commit
    S-->>C: PaymentResponse
```

| Vị trí lỗi | Dữ liệu durable có thể còn lại | Quy tắc xử lý |
| --- | --- | --- |
| Trước commit Tx1 | Chưa có payment mới | Rollback; chưa gọi charge |
| Sau Tx1, trước charge | Payment PROCESSING | Đối soát trước khi quyết định gọi lại cùng key |
| Gateway đã charge, Tx2 lỗi | Payment có thể vẫn PROCESSING; gateway đã thu | Không suy diễn FAILED; reconcile bằng key |
| Gateway timeout | Payment UNKNOWN nếu finalize lưu được | Giữ Order PAYMENT_PENDING và inventory RESERVED |
| Decline cuối cùng | Payment FAILED, Order FAILED | Release inventory trong transaction finalize |
| Success lưu thành công | Payment SUCCESS, Order PAID | Không cho cancel; fulfillment hoặc refund |

Idempotency ràng buộc request lặp vào cùng logical payment. Key cũ cùng amount trả
record cũ; key cũ nhưng amount khác bị conflict; Order đã có logical payment không
nhận record mới chỉ vì client tạo key khác. MaxChargeAttempts=2 gồm lần đầu và
một lần retry, không phải hai lần retry bổ sung. Hết budget vẫn UNKNOWN thì chỉ
reconcile; không được tự release reservation.

### 2.5. Hủy, fulfillment và compensation

Cancel cần state nguồn hợp lệ và không có Payment PROCESSING/UNKNOWN/SUCCESS/
REFUND_PENDING. Điều kiện service mạnh hơn bảng CanTransition: chỉ thấy Order
PAYMENT_PENDING chưa đủ để kết luận được hủy. PAID→CONFIRMED→COMPLETED yêu cầu
Admin và payment evidence; complete đổi reservation thành CONSUMED.

Refund được bắt đầu từ PAID/CONFIRMED, tạo REFUND_PENDING cho cả Order/Payment
trước lời gọi gateway. Refund thành công chuyển REFUNDED và release reservation.
Nếu refund bị decline hoặc unknown, source giữ REFUND_PENDING và lưu error;
không có một terminal “refund failed” làm mất nghĩa vụ bù trừ. Flow hiện không
hoàn tiền cho Order đã COMPLETED; không suy rộng sang chính sách trả hàng sau giao.

### 2.6. Recovery worker và giới hạn vận hành

Worker mặc định quét mỗi 30 giây, chọn payment cũ ít nhất 60 giây, batch 50; source
áp dụng mức tối thiểu 5 giây và giới hạn batch 1..500. Worker chỉ hỏi lại charge
hoặc refund, không tự phát charge retry. TimeoutPaymentGateway áp deadline cho
cả charge/refund/status; mặc định 5.000 ms, cấu hình hợp lệ 50..60.000 ms.

Status reconciliation nhận stopping token của worker qua gateway lookup, load,
save và finalize transaction. Shutdown cancellation được truyền ra ngoài, không
biến thành UNKNOWN mới. Worker xóa tracker trước mỗi candidate để không mang
mutation chưa commit của candidate lỗi sang candidate tiếp theo.

WaitAsync giới hạn thời gian chờ ngay cả khi adapter không tuân cancellation;
late task fault được observe và late result không tự finalize. Timeout vẫn có
thể đã charge ở provider nên giữ UNKNOWN/reservation; refund giữ REFUND_PENDING.
Deadline giới hạn caller waiting, không thể cưỡng chế dừng adapter hoặc chứng
minh provider không thu tiền. Task không hợp tác và overload/backpressure còn cần
kiểm thêm cho môi trường thực tế.

Fixture HTTP thông thường tắt worker để test xác định. ProcessRestartTests dùng
hai OS process riêng: process đầu bị kill sau khi gateway publish journal nhưng
trước finalize, process mới bật worker và tự chuyển payment SUCCESS/Order PAID,
không tăng attempt hay release stock. Đây là evidence cho một cửa sổ crash với
local ledger; chưa chứng minh mọi scheduler/network fault hoặc provider thật.

DurableFake là mode mặc định, lưu journal trong App_Data/payment-gateway và bind
key với payload; ghi file tạm, flush rồi rename dưới file lock. Unit test kiểm
replay/mismatch và instance restart cho cả charge/refund. Không coi atomic file
publication là distributed transaction hoặc bảo đảm mất điện.

### 2.7. Input contract, attack surface và HTTP outcome

Quantity Order 1..10000; UnitPrice/amount theo guard 0,01..999999,99. Cart add nhận
1..10000; update nhận 0..10000, zero nghĩa là xóa. Registration yêu cầu email hợp
lệ, password 8..128 và tên; login vẫn cho mật khẩu legacy ngắn hợp lệ để tương
thích. Product create/update giới hạn price và stock; nullable update field nghĩa
là không thay đổi, không phải đã có chính sách xóa mọi field.

401 biểu thị thiếu identity hợp lệ; 403 biểu thị thiếu quyền; 400 là input sai;
409 là conflict nghiệp vụ hoặc persistence cạnh tranh được xử lý; 500 vẫn là lỗi
ngoài hợp đồng xử lý của endpoint. Classifier mới nhận SQLSTATE 40001/40P01 cả khi Npgsql execution strategy bọc
exception, trả 409 thay vì 500 ở catch tương ứng. Các SQLSTATE/network exception
khác không được tự coi là conflict cạnh tranh.

Order hiện giới hạn tối đa 100 item và body 64 KiB mặc định. Auth login/register
dùng IP quota 10/phút; API dùng subject đã xác thực hoặc connection IP quota
120/phút. Fixed windows nằm trong process, không distributed. Request 429/413
bị chặn trước JSON binding và trước audit để tránh log amplification. API đọc
danh sách vẫn chưa có pagination. Chi tiết endpoint và requirement
được giữ trong [Phụ lục A](PHU_LUC_A_G.md#phụ-lục-a--hệ-thống).

Nguồn: [Program](../../OrderPaymentMicroservice/Program.cs),
[OrderService](../../OrderPaymentMicroservice/Services/OrderService.cs),
[PaymentService](../../OrderPaymentMicroservice/Services/PaymentService.cs),
[DbContext](../../OrderPaymentMicroservice/Data/AppDbContext.cs),
[recovery worker](../../OrderPaymentMicroservice/Services/Payments/PaymentRecoveryWorker.cs).

### 2.8. Audit durable và chính sách khi ghi log lỗi

AuditWriter dùng DbContext riêng để insert BEGIN/RESULT, không SaveChanges trên
tracker nghiệp vụ. Middleware giữ transport response tới khi RESULT đã lưu;
worker reconciliation lưu SYSTEM events. Field allowlist không có body, header,
password, JWT, idempotency key hay exception message. HTTP_SUCCESS chỉ nói HTTP
outcome; worker RESULT nói trạng thái nghiệp vụ.

Trigger PostgreSQL chặn UPDATE/DELETE/TRUNCATE, nhưng DBA có thể disable trigger;
chưa có retention và audit access governance. BEGIN failure chặn action; RESULT
failure có thể sau business commit và trả 503 cần reconcile. Create BEGIN có thể
chưa biết resource ID. Trigger Orders/Payments ghi COMMIT với ID thật trong cùng
transaction nghiệp vụ; ghi audit lỗi làm rollback. BEGIN/RESULT vẫn là HTTP events
riêng, còn outbox chỉ cần khi chuyển audit tới một đích ngoài PostgreSQL.

Admission/audit error dùng JSON message. Audit 503 có requiresReconciliation=false
khi BEGIN lỗi trước action, true khi RESULT lỗi sau action; correlationId do server
sinh. CORS expose correlation/retry headers cho origin được cho phép.

### 2.9. Tạo đơn có idempotency và audit COMMIT

POST /api/orders nhận Idempotency-Key tùy chọn; frontend đã gửi key. Server bind
key hash với customer đã xác thực và payload hash, giữ original response trong
transaction Serializable cùng reserve/order. Unique index ngăn hai đơn cùng
customer/key. Cùng key/payload trả lại 201, cùng ID/receipt; khác payload trả 409.
Race 409 phải retry bằng key cũ. Client không gửi key không có bảo đảm chống trùng.
Sau cancel, replay vẫn trả receipt ban đầu; GET order trả trạng thái hiện tại.

AFTER INSERT/UPDATE/DELETE trigger trên Orders/Payments ghi COMMIT vào AuditEvents,
bao gồm resource ID và state integer, trong cùng transaction. Trigger bao phủ cả SQL
trực tiếp, không chỉ controller. Request có verified actor/correlation qua metadata
transaction-local; worker/direct SQL ghi SYSTEM khi không có request metadata.
Rollback cả audit và stock/order đã được fault-injection test. Chưa audit toàn bộ
Products/Cart/Users hay chứng minh DBA không sửa schema. Không backfill lịch sử cũ.

Checkout lưu key trước fetch trong sessionStorage, giữ khi mất response/503, chặn
đổi payload khi chưa rõ outcome và tránh double submit trong cùng tab. Browser
mocked-fetch regression PASS; HTTP/PostgreSQL tests kiểm chứng backend riêng.
Snapshot receipt chứa thông tin giao hàng; phải bảo vệ như dữ liệu order.

### 2.10. Quan sát vận hành và tuổi pending

Health live chỉ phản ánh process, ready chạy SELECT 1 có budget 2 giây. Endpoint
/api/operations/status yêu cầu Admin, trả aggregates và counters process-local,
không PII hoặc exception. OperationsAlertWorker đọc số liệu mỗi 30 giây và log
mã cảnh báo khi tập alert đổi; chưa external notification/Prometheus exporter.

PendingSince lưu thời điểm unresolved operation bắt đầu, độc lập UpdatedAt. Giữ mốc
qua unknown lookup và reconcile, clear khi SUCCESS/FAILED/REFUNDED; refund mới bắt
đầu window riêng. State machine và BEFORE Payments trigger bảo vệ mốc. Migration
backfill record cũ bằng UpdatedAt nên chỉ là lower-bound age của lịch sử. Chỉ số
stuck ngưỡng mặc định 300 giây, không hứa gateway giải quyết trong 300 giây.
Counters audit append failures và recovery outcomes reset khi process restart.
Count queries tuần tự có deadline, không là transactional global snapshot.
