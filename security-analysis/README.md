# Bổ sung ATPM cho OrderPaymentMicroservice

## Kết quả đã chạy

- Model kỳ vọng: **4/4 PASS**.
- Model BUGGY: **P1 FAIL**, counterexample `CREATED → CANCELLED → PAID_AFTER_CANCELLED` (trạng thái nghiệp vụ PAID).
- Model phục hồi Order-Payment: **10/10 PASS**, 14 trạng thái và 35 chuyển tiếp.
- Bốn mutation về hồi sinh đơn hủy, sai amount, thanh toán trùng và xung đột payment-cancel đều bị phát hiện kèm witness.
- xUnit: **34 PASS, 0 FAIL, 0 SKIP**.
- Coverlet toàn lớp InputValidator: **31/32 nhánh = 96,875%** (bảng CLI hiển thị 96,87%).
- `ValidateCreateOrderRequest()`: **100% branch coverage**, **100% line coverage**.
- Toàn lớp: line coverage **83,67%**; đây không phải chỉ số branch coverage.

## Chạy lại

Từ thư mục gốc repository, dùng Python 3.10+ và .NET 10 SDK:

```powershell
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj /p:CollectCoverage=true /p:CoverletOutput=../security-analysis/results/input-validator /p:CoverletOutputFormat=cobertura '/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator' /p:Threshold=90 /p:ThresholdType=branch
python security-analysis/run_analysis.py
```

Có thể chạy riêng model phục hồi bằng:

```powershell
python security-analysis/verify_recovery_model.py
```

Đặc tả máy đọc được nằm trong `order_payment_recovery_model.json`; bảng giải
thích từng state, sự cố, giải pháp và state tiếp theo nằm trong
`MO_HINH_XU_LY_SU_CO.md`. Kết quả được lưu tại
`results/order_payment_recovery_model.results.json`; ma trận state/sự cố/giải
pháp nằm trong `results/order_payment_state_response_matrix.csv`.

NuGet cần mạng trong lần restore đầu. Test không cần PostgreSQL và không khởi động API.
`run_analysis.py` import trực tiếp `kripke_model.py` Lab 1 và `risk_assessor.py` Lab 3, không sao chép hay sửa script mẫu. Kết quả nằm trong `results/`.
Lab 3 CLI gốc dùng dataset cố định, vì vậy integration gọi `assess_all()` với dataset của project.

## Model Kripke và 4 thuộc tính

Schema giữ nguyên Lab 1: S = `states[].id`, S0 = `initial_states`, R = `transitions`, L = `states[].labels`.
WorkflowAnalysis.md liệt kê safety, liveness và hai điều kiện consistency; chúng được tách thành bốn thuộc tính:

| ID | Thuộc tính | Cách kiểm tra |
|---|---|---|
| P1 | Không bỏ bước/quay ngược/hồi sinh trạng thái terminal | Lab 1 `AG NOT invalid_transition`, với history monitor |
| P2 | Từ mọi trạng thái reachable, mọi đường đi cuối cùng đến terminal | `AG AF terminal`, least fixed point bổ sung trong integration |
| P3 | PAID phải có payment SUCCESS | Lab 1 `AG (paid → payment_success)` |
| P4 | PAYMENT_PENDING chưa có payment SUCCESS | Lab 1 `AG (payment_pending → NOT payment_success)` |

Lab 1 chỉ hỗ trợ AG_NOT, AG_IMPLIES và EF. EF terminal **không** chứng minh liveness trên mọi đường đi, nên không dùng EF thay AF.
Model kỳ vọng không có vòng chờ ở trạng thái nonterminal; terminal có self-loop để R là total. Nếu bổ sung vòng chờ PAYMENT_PENDING → PAYMENT_PENDING thì liveness không còn được bảo đảm nếu không có giả định fairness.

`PAID_AFTER_CANCELLED` là trạng thái PAID được gắn lịch sử và nhãn `invalid_transition`. Chỉ thêm cạnh CANCELLED → PAID trên tập trạng thái cũ sẽ không khiến bộ kiểm tra mệnh đề trạng thái của Lab 1 phát hiện lỗi. Monitor khắc phục giới hạn đó. Nhãn này phải được duy trì khi mở rộng model với các cạnh bất hợp lệ khác.

Đây là **model thiết kế kỳ vọng**, không phải trích xuất đầy đủ từ implementation. Implementation còn thiếu các bước VALIDATED/CONFIRMED/COMPLETED và PaymentService thực tế có thể bỏ bước CREATED → PAID. Kết quả PASS không chứng minh API hiện tại đúng. BUGGY minh họa riêng lỗi CANCELLED → PAID; không cố mô hình hóa toàn bộ lỗi runtime.

## Model xử lý sự cố và phục hồi

Model mở rộng không chỉ ghi happy path của `OrderStatus`. Mỗi state còn khai
báo trạng thái Order/Payment, sự cố, cách phát hiện, biện pháp xử lý và state
tiếp theo.

Các state thiết kế bổ sung gồm `PAYMENT_PROCESSING`, `PAYMENT_RETRY`,
`PAYMENT_PROCESSING_FINAL`, `CONFIRMATION_RETRY`, `REFUND_PENDING` và
`REFUNDED`. Retry được bounded bằng state `PAYMENT_PROCESSING_FINAL`. Phiên bản
model đầu tiên cho phép vòng `PAYMENT_PROCESSING ↔ PAYMENT_RETRY` vô hạn và đã
bị thuộc tính P10 phát hiện trước khi model được sửa.

P10 kiểm tra mọi đường tiến triển cuối cùng tới terminal sau khi loại riêng các
cạnh được đánh dấu `stutter`. Kết luận phụ thuộc vào giả định weak fairness đã
ghi trong model. Source hiện đã có hai state machine Order/Payment và các nhánh
retry, reconciliation, refund tương ứng, nhưng model hữu hạn vẫn không thay thế
integration test HTTP/PostgreSQL.

## Case test và nhánh chưa được phủ

Test bao gồm null request/item/list, list rỗng; CustomerId/ProductId không dương; total âm/0/dưới min/trên max; quantity dưới/trên biên; price âm/trên max; biên hợp lệ; nhiều item; tolerance ±0,01 và vượt tolerance cả hai hướng; decimal.MaxValue; phép nhân lớn nhất được validator cho phép.

- Nhánh `Math.Abs(...) > 0.01` có **100% (2/2)** coverage và hiện ném `ArgumentException` khi mismatch vượt tolerance.
- Nhánh duy nhất chưa phủ: dòng **99**, `subtotal < 0`, hướng true. Quantity đã bị giới hạn 1..10000 và UnitPrice 0..999999,99 nên subtotal không thể âm.
- Dòng **100**, và catch OverflowException **102–104** chưa chạy. Tích tối đa 9.999.999.900 thấp hơn rất nhiều decimal.MaxValue; overflow không thể xảy ra sau các guard hiện tại. Không dùng reflection hoặc thay validator để ép nhánh bất khả thi.
- Các dòng khởi tạo hằng decimal cũng được Coverlet ghi nhận chưa phủ; chúng không làm thay đổi kết luận về branch coverage.
- `MIN_PRICE` 0,01 hiện được áp dụng; giá 0 và 0,001 bị từ chối bằng regression test.

## Dataset và ưu tiên rủi ro

`vulnerabilities.json` dùng các trường của dataset Lab 3; thêm phạm vi và giả định triển khai.

| Ưu tiên | ID | CVSS-lite | Mức độ |
|---|---|---|---|
| 1 | VULN-001 — API Order không xác thực | 9,1 | CRITICAL |
| 2 | VULN-005 — sửa giỏ hàng người khác | 8,1 | HIGH |
| 3 | VULN-002 — giá/tổng tiền từ client | 7,5 | HIGH |
| 4 | VULN-003 — hồi sinh đơn cancelled | 7,5 | HIGH |
| 5 | VULN-004 — bỏ qua mismatch | 7,5 | HIGH |

Các điểm bằng nhau giữ thứ tự dataset, không có khác biệt điểm ưu tiên giữa VULN-002/003/004. Đây là **CVSS-lite giáo dục**, không phải CVSS chính thức.

### VULN-005 tự phát hiện: truy cập giỏ hàng chéo người dùng

Đây là phát hiện tại baseline ban đầu. Source hiện có JWT, `[Authorize]` và đối chiếu `cart.UserId` với principal tại `CartController`; ID trên route không còn tự được coi là bằng chứng quyền sở hữu.

Điểm 8,1 HIGH mô tả rủi ro tại thời điểm phát hiện, không phải residual risk sau sửa. Chưa có HTTP integration test chứng minh đầy đủ phản hồi 401/403 nên phát hiện chưa được đóng ở mức hệ thống chạy thật.

Source hiện đã có authentication, ownership check, server-side pricing, mismatch rejection, hai state machine, payment amount/idempotency, bounded retry, reconciliation và refund. Phần còn thiếu là kiểm chứng lại qua HTTP/PostgreSQL và adapter gateway thật.

## Lưu ý build

Project hiện tham chiếu Npgsql.EntityFrameworkCore.PostgreSQL 8.0.8. Repository từng track một số file bin/obj nên build có thể làm chúng thay đổi; các artefact sinh tự động không phải source hoặc bằng chứng chính.
