# Threat model Chương 1 — OrderPaymentMicroservice

## 1. Phạm vi và phương pháp

Phân tích này áp dụng đúng chuỗi khái niệm của Chương 1:

> **Asset → Threat → Vulnerability → Attack → Risk → Control → Evidence**

STRIDE được áp dụng **theo từng trust boundary**, không chỉ gắn nhãn chung cho
hệ thống. Rủi ro ban đầu dùng thang giáo dục `Likelihood (1–5) × Impact (1–5)`:
1–4 LOW, 5–9 MEDIUM, 10–16 HIGH, 17–25 CRITICAL. Đây không phải điểm CVSS.
Artifact chuẩn để máy đọc và kiểm tra là `threat_model.json`.

Phạm vi gồm REST API, business logic User/Cart/Order/Payment, PostgreSQL, JWT,
payment gateway abstraction và recovery worker. Bảo mật thiết bị client, hạ
tầng cloud/network không có trong repository và bảo đảm của gateway thật nằm
ngoài phạm vi. Vì vậy control như TLS, WAF hay secret manager là giả định/yêu
cầu triển khai, không được tuyên bố là đã chứng minh bởi source.

## 2. Data flow và trust boundary

```text
[Anonymous / Customer / Admin]
             |
      TB1: Internet -> API
      JWT, route id, JSON; authenticate, authorize, validate
             v
   [Controllers + Services] <---- TB4 ---- [Host config / secrets]
             |
      TB2: Process -> PostgreSQL
      entities, transaction, credentials
             v
        [PostgreSQL]

   [PaymentService] ---- TB3 ----> [Payment gateway]
                  charge/refund/status
```

- **TB1** là nơi dữ liệu không tin cậy lần đầu vào hệ thống: phải kiểm JWT,
  quyền sở hữu và miền giá trị trước khi gọi business logic.
- **TB2** mang dữ liệu có giá trị cao qua process/network boundary: cần query
  tham số hóa, transaction, tài khoản DB tối thiểu quyền và secret ngoài source.
- **TB3** là ranh giới chưa được chứng minh với `FakePaymentGateway`: response
  của gateway thật phải được xác thực, bind với merchant/order/amount và chống replay.
- **TB4** đưa secret/config vào process: startup validation không thay thế secret
  manager, rotation và kiểm soát quyền đọc trên host.

## 3. Asset và CIA

| ID | Asset | Phân loại | C | I | A |
|---|---|---|---|---|---|
| A1 | Danh tính và credential khách hàng | RESTRICTED | H | H | M |
| A2 | Order, cart và ownership | CONFIDENTIAL | H | H | M |
| A3 | Payment state, amount, idempotency | RESTRICTED | M | H | H |
| A4 | Giá sản phẩm và tồn kho | INTERNAL | L | H | H |
| A5 | JWT key và database secret | RESTRICTED | H | H | H |
| A6 | Security/transaction audit trail | CONFIDENTIAL | M | H | M |

Ưu tiên integrity cao cho A2–A5 vì một thay đổi nhỏ có thể tạo đơn sai giá,
double charge, hoàn tiền sai hoặc chiếm quyền. Availability cao cho Payment và
Inventory vì gián đoạn có thể để lại giao dịch chưa xác định cần reconciliation.

## 4. STRIDE và risk register

| ID | STRIDE / boundary | Kịch bản | Điểm | Trạng thái |
|---|---|---|---:|---|
| T1 | Spoofing / TB1 | Không có/giả mạo identity hoặc dùng route id của user khác | 20 CRITICAL | Đã xác minh trong các đường HTTP test |
| T2 | Tampering / TB1 | Sửa price, total hoặc payment amount | 25 CRITICAL | Đã giảm thiểu |
| T3 | Repudiation / TB1 | Phủ nhận payment/retry/cancel khi log không bền vững | 12 HIGH | Giảm thiểu một phần: COMMIT audit nguyên tử, còn retention/DBA |
| T4 | Information disclosure / TB1 | IDOR đọc aggregate của user khác | 16 HIGH | Đã xác minh trong các đường HTTP test |
| T5 | Denial of service / TB1 | Flood endpoint hoặc order có quá nhiều item | 16 HIGH | Giảm thiểu một phần: local rate/body/item limits |
| T6 | Elevation of privilege / TB1 | Customer gọi endpoint refund/list của Admin | 15 HIGH | Đã xác minh trong các đường HTTP test |
| T7 | Tampering / TB2 | Race payment-cancel, replay hoặc chuyển trạng thái sai | 20 CRITICAL | Đã xác minh trong PostgreSQL race test |
| T8 | Spoofing / TB3 | Giả/replay kết quả SUCCESS từ gateway thật | 15 HIGH | **OPEN cho gateway thật** |
| T9 | Information disclosure / TB4 | Lộ JWT/DB secret qua config, log hoặc host | 15 HIGH | Giảm thiểu một phần |

Risk score là **inherent risk** của kịch bản trước control. Trạng thái không được
suy ra chỉ từ điểm số: `VERIFIED_IN_TEST_SCOPE` nghĩa là có HTTP/PostgreSQL evidence cho các đường test đã chạy,
không phải tất cả endpoint, token scenario hay mọi interleaving; `OPEN` không được
ghi thành “đã sửa”. Property ID phải đi kèm namespace `base` hoặc `recovery`; trường `property_refs`
trong JSON là tham chiếu chuẩn, `kripke_properties` giữ để tương thích metadata cũ.

Chi tiết threat agent, vulnerability, attack, control,
recommendation và evidence nằm trong JSON để tránh bảng tài liệu làm mất dữ liệu.

STRIDE coverage đầy đủ cả sáu nhóm. Một threat có thể ảnh hưởng nhiều thuộc tính
CIA, nhưng phải có ít nhất một asset và một trust boundary cụ thể.

## 5. Cầu nối threat → yêu cầu → hợp đồng chức năng → Kripke

| Threat | Security requirement/control | Hợp đồng/thuộc tính hình thức | Kết luận trong phạm vi model |
|---|---|---|---|
| T1, T4, T6 | REQ-1/2; JWT, ownership, role | Ngoài model Order hiện tại | Không kết luận từ Kripke; cần model identity/role riêng |
| T2 | REQ-3/4/6; giá server, equality | F1, F2; base:P3/P4, recovery:P2/P6/P11 | Chỉ amount đã xác minh mới đi vào payment; sai amount giữ PAYMENT_PENDING |
| T7 | REQ-5/7/18; state guard, serializable, idempotency | F2, F3, F7; base:P1, recovery:P1/P4/P7/P11–P15 | Cancel/payment có quyết định duy nhất; terminal không hồi sinh; callback trùng là no-op |
| T5 | REQ-16; bounded retry | F3; recovery:P10/P14 với giả định fairness đã nêu | UNKNOWN giữ reservation và reconcile; model không chứng minh rate limit/load runtime |
| T8 | REQ-6/7; xác thực gateway | F3, F6; recovery:P2/P13/P14 | Chứng minh cách xử lý proposition gateway; không chứng minh gateway thật đã xác thực |

Điểm quan trọng: `base:P3: AG(paid → payment_success)` chỉ chứng minh consistency
trong mô hình. Nó **không** chứng minh proposition `payment_success` đến từ một
gateway xác thực. Vì vậy T8 vẫn mở dù P3 PASS. Tương tự, model Kripke Order không
mang identity/role nên không dùng P1–P4 để tuyên bố T1/T4/T6 đã được chứng minh.

Counterexample của model BUGGY `CREATED → CANCELLED → PAID_AFTER_CANCELLED`
chính là attack trace của T7/VULN-003. History monitor biến lịch sử “đã terminal
rồi lại PAID” thành proposition `invalid_transition`, để P1 phát hiện thay vì
chỉ nhìn nhãn trạng thái cuối.

Test xUnit/HTTP được giữ ở lớp đối chiếu implementation, không được dùng thay
cho kết luận hình thức ở bảng trên. Nguồn `order_payment_recovery_model.json`
có 7 hợp đồng chức năng; verifier duyệt 37 transition và kiểm tra chính xác
state nguồn, event, state đích cùng các bất biến P1–P15.

## 6. Hành động còn mở theo ưu tiên

1. T5: đặt giới hạn body/item count, rate limit theo identity/IP và chạy load test.
2. T3: audit event append-only có actor, action, resource, correlation id, time,
   outcome; quy định integrity và retention.
3. T8: khi thay fake gateway, xác thực signature/timestamp/nonce, merchant,
   order và amount trước khi nhận SUCCESS.
4. T1/T4/T6/T7: các đường authorization/race đã có HTTP/PostgreSQL PASS;
   tiếp tục kiểm token hết hạn, forged issuer/audience/signature, role revocation
   và thêm interleaving. Không suy từ các đường test này sang mọi threat đã đóng.
5. T9: secret manager, rotation và secret scanning cả Git history.

## 7. Kiểm chứng tái lập

Chạy:

```bash
python security-analysis/verify_threat_model.py
```

Script kiểm tra ID duy nhất, tham chiếu asset/boundary hợp lệ, đủ sáu STRIDE,
đủ CIA, miền likelihood/impact, công thức risk score và liên kết tới property
Kripke. Kết quả được lưu tại `results/threat_model_results.json`. Đây là kiểm tra
tính nhất quán của threat model, không phải bằng chứng rằng mọi control runtime
đã hoạt động.

## 8. Evidence mở rộng ngày 12/10/2026

Test authorization kiểm Cart/Order chéo user, Payment read/retry/reconcile chéo
user, cùng Customer gọi Order listing/confirm/complete, Payment refund/refund retry
và Product create/update/delete. T1/T4/T6/T7 có trạng thái VERIFIED_IN_TEST_SCOPE
với runtime_evidence và limits; điểm inherent risk được giữ nguyên.

Audit liveness mới xác nhận graph gốc không thỏa unconditional AG AF terminal,
trong khi checker progress-only thỏa thuộc tính sau lọc cạnh. Xem
[Chương 3](report/BAO_CAO.md#3-mô-hình-trạng-thái-kiểm-chứng-và-đối-chiếu-implementation)
và [liveness audit](results/liveness_audit.results.json).

## 9. Admission và audit — cập nhật 13/10/2026

T3/T5 chuyển PARTIALLY_MITIGATED. Admission có body 64 KiB, tối đa 100 item,
auth IP quota 10/phút và verified-user API quota 120/phút. Fixed windows nằm trong
mỗi process, không lấy IP từ X-Forwarded-For tùy ý; proxy/distributed/load còn mở.

Audit HTTP và worker lưu field allowlist trong PostgreSQL. BEGIN có trước nghiệp
vụ, RESULT lưu trước transport response; UPDATE/DELETE/TRUNCATE bị trigger chặn.
BEGIN failure chặn action; RESULT failure có thể đi sau business commit và trả
503 cần reconcile; BEGIN/RESULT có thể chưa đủ. COMMIT mới ghi nguyên tử cùng
transaction nghiệp vụ, không bảo vệ khỏi DBA sửa schema. Retention và audit access governance còn mở.

Bổ sung COMMIT audit: trigger Orders/Payments ghi cùng transaction, lỗi insert audit
rollback mutation; rollback transaction cũng xóa audit chưa commit. Order create
có key/payload hash theo JWT user và original receipt trong transaction. T3 vẫn
PARTIALLY_MITIGATED do retention/DBA governance còn mở; không giảm risk score bằng
một residual score chưa có phương pháp đo. Direct SQL/worker thiếu metadata request
được ghi SYSTEM, không gán actor bằng owner của order.
