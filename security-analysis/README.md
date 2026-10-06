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
ghi trong model. Đây là kiểm chứng model hữu hạn, chưa chứng minh source ASP.NET
tuân thủ model.

## Case test và nhánh chưa được phủ

Test bao gồm null request/item/list, list rỗng; CustomerId/ProductId không dương; total âm/0/dưới min/trên max; quantity dưới/trên biên; price âm/trên max; biên hợp lệ; nhiều item; tolerance ±0,01 và vượt tolerance cả hai hướng; decimal.MaxValue; phép nhân lớn nhất được validator cho phép.

- Dòng **61**: `Math.Abs(...) > 0.01` có **100% (2/2)** nhánh. Test vượt tolerance PASS vì block hiện rỗng. Đây là characterization của lỗi, **không** xác nhận input đó an toàn.
- Nhánh duy nhất chưa phủ: dòng **99**, `subtotal < 0`, hướng true. Quantity đã bị giới hạn 1..10000 và UnitPrice 0..999999,99 nên subtotal không thể âm.
- Dòng **100**, và catch OverflowException **102–104** chưa chạy. Tích tối đa 9.999.999.900 thấp hơn rất nhiều decimal.MaxValue; overflow không thể xảy ra sau các guard hiện tại. Không dùng reflection hoặc thay validator để ép nhánh bất khả thi.
- Các dòng khởi tạo hằng decimal cũng được Coverlet ghi nhận chưa phủ; chúng không làm thay đổi kết luận về branch coverage.
- `MIN_PRICE` khai báo 0,01 nhưng không được dùng: giá 0 và 0,001 hiện vẫn được chấp nhận, đã có test ghi nhận.

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

`CartController` nhận userId/cartId/itemId từ route mà không có Authorize hay kiểm tra principal. `CartService.GetOrCreateCartAsync` chọn cart theo userId do client cung cấp; `UpdateCartItemAsync`/`RemoveItemFromCartAsync` chỉ kiểm tra item thuộc cart, **không** kiểm tra cart thuộc caller. `Program.cs` không cấu hình authentication.

Vì vậy, khi API có thể truy cập qua mạng và không được lớp ngoài bảo vệ, người không đăng nhập biết/đoán các ID có thể xem hoặc sửa/xóa item của người khác. Vector NETWORK, complexity LOW, privileges NONE, interaction NONE; confidentiality LOW, integrity HIGH, availability NONE → **8,1 HIGH** theo script Lab 3. Chưa có bằng chứng về mất availability toàn dịch vụ nên không nâng impact A.

Phát hiện dựa trên review code, không khai thác API đang chạy. Khuyến nghị: authentication, kiểm tra cart.UserId với user identity đáng tin cậy cho mọi thao tác; không coi ID từ route là bằng chứng quyền sở hữu. Tương tự, giá phải lấy từ product phía server, mismatch phải bị reject, payment phải kiểm tra trạng thái/amount và kết quả gateway. Các thay đổi nghiệp vụ này chưa được thực hiện trong phạm vi bổ sung bài lab.

## Lưu ý build

Restore báo NU1603: project yêu cầu Npgsql.EntityFrameworkCore.PostgreSQL 8.0.7 nhưng NuGet chọn 8.0.8. Test vẫn chạy thành công; không sửa dependency ứng dụng trong bài này. Repository đang track bin/obj nên build tạo thay đổi ở các file đó; giữ nguyên các thay đổi có sẵn của người dùng.
