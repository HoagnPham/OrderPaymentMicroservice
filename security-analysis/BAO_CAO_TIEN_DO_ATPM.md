# BÁO CÁO TIẾN ĐỘ DỰ ÁN ORDERPAYMENTMICROSERVICE

**Định hướng: An toàn phần mềm trong quản lý đơn hàng và thanh toán**  
Ngày cập nhật báo cáo: 08/10/2026

**Bổ sung ngày 06/10/2026:** hoàn thiện model xử lý sự cố và phục hồi cho
luồng Order-Payment, gồm bounded retry, serialization payment-cancel và
compensation refund.

**Cập nhật ngày 08/10/2026:** chạy lại solution đạt 84/84 unit test; 5 integration test PostgreSQL tồn tại nhưng bị skip vì Docker không khả dụng. Chương 1 được bổ sung threat model gồm 6 asset, 4 trust boundary, 9 threat, đủ CIA và 6/6 STRIDE; recovery Kripke được mở rộng thành 7 hợp đồng chức năng, 15 state, 37 transition và P1–P15 PASS. Chương 2 được bổ sung static analysis, bảng ownership/lifetime, đo alignment thực tế và snapshot bằng chứng sanitizer. Đặc tả SMT được sửa từ logic sai `QF_LIA` sang `QF_NIA`; Z3 5.1.0 chạy 7/7 truy vấn SAT/UNSAT đúng kỳ vọng. Các tài liệu học phần trong `ATPM/` chỉ dùng làm chuẩn đối chiếu, không được tính là sản phẩm của dự án.

Phạm vi: mã nguồn và các kết quả kiểm thử, kiểm chứng hiện có trong repository.

## 1. Tổng quan và mục tiêu

OrderPaymentMicroservice là dự án xây dựng dịch vụ quản lý đơn hàng và thanh toán bằng ASP.NET Core. Mã nguồn được tổ chức theo các lớp Controller, Service, DTO, Model và lớp truy cập dữ liệu sử dụng Entity Framework Core với PostgreSQL. Các module hiện có gồm người dùng, sản phẩm, giỏ hàng, đơn hàng và thanh toán.

Ngoài chức năng nghiệp vụ, dự án được phát triển theo định hướng an toàn phần mềm: kiểm soát dữ liệu đầu vào, xác định các bất biến về tiền và trạng thái, kiểm chứng luồng xử lý, kiểm thử các trường hợp biên và đánh giá rủi ro từ mã nguồn. Mục tiêu là phát hiện sớm các hành vi có thể làm sai lệch đơn hàng, thanh toán hoặc quyền truy cập dữ liệu.

Tại thời điểm báo cáo, dự án đã có threat model và truy vết yêu cầu, mô hình Kripke, bộ test xUnit, dataset đánh giá rủi ro, static/dynamic analysis cho mô hình C và kiểm chứng SMT số học. Năm phát hiện ưu tiên đã có thay đổi khắc phục cùng unit test hồi quy. Giai đoạn tiếp theo vẫn cần TLC/BMC, fuzzing và bằng chứng HTTP/PostgreSQL thực chạy; các kết quả hiện có chưa đồng nghĩa hệ thống đủ an toàn để triển khai thực tế.

## 2. Nền tảng chức năng đã xây dựng

### 2.1. Tổ chức kiến trúc và dữ liệu

Dự án đã có các Controller và Service riêng cho từng nhóm chức năng, các DTO trao đổi dữ liệu và các model nghiệp vụ. Cấu hình dependency injection giúp tách phần tiếp nhận HTTP khỏi logic xử lý. Dự án cũng có cấu hình kết nối PostgreSQL và migration cơ sở dữ liệu.

Về an toàn phần mềm, cách tổ chức này giúp xác định rõ nơi cần đặt các kiểm tra: dữ liệu đầu vào tại validator, quyền sở hữu tại lớp nghiệp vụ, ràng buộc trạng thái và số tiền trước khi ghi dữ liệu. Tuy nhiên, kiến trúc phân lớp chỉ hỗ trợ việc triển khai kiểm soát, không tự bảo đảm các kiểm soát đã đầy đủ.

### 2.2. Đơn hàng, thanh toán và giỏ hàng

Mã nguồn đã có xử lý tạo, xem và hủy đơn hàng; tạo và xem thanh toán; xem, thêm, cập nhật và xóa sản phẩm trong giỏ hàng. Enum Order runtime có mười trạng thái: CREATED, VALIDATED, PAYMENT_PENDING, PAID, CONFIRMED, COMPLETED, CANCELLED, FAILED, REFUND_PENDING và REFUNDED. Model Kripke cơ sở ở mục 3.2 cố ý chỉ dùng tám trạng thái để tập trung vào luồng Order ban đầu.

Phần thanh toán đã được tách qua `IPaymentGateway`; adapter mặc định `FakePaymentGateway` lưu kết quả theo idempotency key và hỗ trợ success, decline, unknown, reconciliation và refund ở mức thiết kế/test. Đây vẫn là gateway mô phỏng, chưa có chữ ký callback hay đối soát với nhà cung cấp thật, nên chưa được xem là sẵn sàng vận hành.

## 3. Các công việc an toàn phần mềm đã hoàn thành

### 3.1. Phân tích luồng nghiệp vụ và bất biến

Dự án đã có tài liệu `WorkflowAnalysis.md` phân tích quan hệ giữa đơn hàng, thanh toán và các ràng buộc cần bảo vệ. Những yêu cầu trọng tâm gồm:

- Đơn hàng phải chuyển trạng thái theo quy tắc, không bỏ bước hoặc quay ngược trái phép.
- Đơn hàng cuối cùng phải hoàn tất, bị hủy hoặc thất bại trong các giả định của mô hình.
- Trạng thái PAID phải đi kèm thanh toán thành công.
- Tổng tiền đơn hàng phải nhất quán với tổng tiền các item; thanh toán cần được đối chiếu với giá trị đơn hàng.

Việc xác định các yêu cầu này giúp chuyển khái niệm “an toàn” thành những thuộc tính cụ thể có thể kiểm tra, thay vì chỉ đánh giá dựa trên việc chức năng chạy được.

#### 3.1.1. Threat model theo CIA, trust boundary và STRIDE

Threat model Chương 1 được lưu trong `threat_model.json` và diễn giải tại
`THREAT_MODEL_CHUONG1.md`. Phạm vi xác định 6 asset, 4 trust boundary và 9
threat. Script `verify_threat_model.py` kiểm tra ID/tham chiếu, công thức điểm
rủi ro, chuỗi Asset → Threat → Vulnerability → Attack → Control → Evidence,
đồng thời yêu cầu đủ Confidentiality, Integrity, Availability và 6/6 nhóm
STRIDE. Kết quả hiện tại là **PASS**.

Các threat được truy vết sang yêu cầu, hợp đồng chức năng và thuộc tính Kripke
thay vì chỉ liệt kê độc lập. Test chỉ dùng ở lớp đối chiếu implementation,
không thay thế chứng minh trên model. T3 (repudiation), T5 (DoS/rate limit) và T8 (xác thực gateway
thật) vẫn OPEN; T9 về secret mới được giảm thiểu một phần. Kết quả PASS của
script chỉ chứng minh threat model tự nhất quán, không chứng minh control
runtime đã hoạt động.

### 3.2. Mô hình hóa và kiểm chứng bằng Kripke

Đã tạo `order_state_model.json` theo schema của Lab 1, biểu diễn cấu trúc Kripke M = (S, S0, R, L): tập trạng thái, trạng thái khởi tạo, quan hệ chuyển và nhãn mệnh đề. Model kỳ vọng gồm tám trạng thái Order, với CREATED là trạng thái khởi tạo.

Đã tích hợp trực tiếp bộ kiểm tra `kripke_model.py` của Lab 1 vào `run_analysis.py`. Do script mẫu chưa hỗ trợ AF, phần tích hợp bổ sung thuật toán điểm cố định để kiểm tra yêu cầu kết thúc trên mọi đường đi.

| Thuộc tính | Nội dung kiểm chứng | Kết quả model kỳ vọng |
|---|---|---|
| P1 — Safety | Không chuyển trạng thái trái phép hoặc hồi sinh đơn đã kết thúc | PASS |
| P2 — Liveness | Từ mọi trạng thái reachable, mọi đường đi cuối cùng đến trạng thái kết thúc | PASS |
| P3 — Consistency | PAID phải có payment SUCCESS | PASS |
| P4 — Consistency | PAYMENT_PENDING chưa có payment SUCCESS | PASS |

Kết quả là **4/4 thuộc tính PASS trên model kỳ vọng**. Model không có vòng chờ ở trạng thái chưa kết thúc; vì vậy kết quả liveness phụ thuộc vào giả định tiến triển này. Model chưa phải bản trích xuất đầy đủ từ mã thực thi, nên không thể dùng kết quả PASS để khẳng định API hiện tại đúng.

### 3.3. Xây dựng model lỗi và chứng minh phản ví dụ

Đã tạo `order_state_model_buggy.json` để mô phỏng việc cho phép thanh toán đơn đã hủy. Model sử dụng trạng thái `PAID_AFTER_CANCELLED` để lưu lịch sử đi vào PAID từ CANCELLED và gắn nhãn chuyển trạng thái bất hợp lệ.

Bộ kiểm tra phát hiện P1 bị vi phạm và trả về phản ví dụ:

```text
CREATED → CANCELLED → PAID_AFTER_CANCELLED
```

Phản ví dụ thể hiện một đơn đã hủy vẫn có thể trở thành đơn đã thanh toán. Đây là nguy cơ từng được nhận diện trong PaymentService trước khi bổ sung `OrderStateMachine` và guard trạng thái nguồn. Kết quả cho thấy phương pháp kiểm chứng có thể phát hiện lỗi đã mô hình hóa; unit test hiện tại còn xác nhận đơn CANCELLED không thể được thanh toán.

#### 3.3.1. Hoàn thiện model xử lý sự cố và phục hồi

Đã bổ sung `order_payment_recovery_model.json` để mô tả cả happy path và đường
xử lý sự cố tại từng state. Model có 15 state và 37 transition, bao gồm các
state thiết kế `PAYMENT_PROCESSING`, `PAYMENT_RETRY`,
`PAYMENT_PROCESSING_FINAL`, `CONFIRMATION_RETRY`, `REFUND_PENDING` và
`REFUNDED`.

Source hiện biểu diễn các trạng thái kết hợp này bằng hai máy trạng thái riêng:
Order lưu vòng đời nghiệp vụ, Payment lưu `PROCESSING`, `UNKNOWN`, `SUCCESS`,
`FAILED`, `REFUND_PENDING`, `REFUNDED`. Các endpoint retry/reconcile/refund và
inventory compensation đã được hiện thực; `CONFIRMATION_RETRY` vẫn là trạng
thái quan sát khi Order giữ `PAID`, không phải enum bền vững riêng.

Model còn tách `PRE_PAYMENT_REJECTED` khỏi `FAILED`. State thứ nhất là terminal
quan sát khi validation/create/commit bị từ chối hoặc rollback, có
`payment_status=NONE` và row Order có thể chưa được lưu. `FAILED` chỉ dùng cho
gateway decline đã xác thực, có `payment_status=FAILED`. Việc tách này loại bỏ
sự nhập nhằng “chưa có Payment” nhưng lại gắn metadata Payment thất bại.

Mỗi incident khai báo đủ vấn đề, event, cách phát hiện, giải pháp và state tiếp
theo. Quan trọng hơn, model định nghĩa độc lập 7 hợp đồng chức năng F1–F7:
tạo/lưu Order, chọn payment hoặc cancel, thực thi và reconcile payment, xác nhận
hoặc bù trừ, hoàn tất hoặc bù trừ, refund, và bảo vệ terminal state. Mỗi hợp
đồng nêu state nguồn, tiền điều kiện, toàn bộ event/state đích được phép và
guarantee phải giữ.

Bộ kiểm chứng duyệt vét cạn toàn bộ state graph. P11 so khớp từng khóa
`(state,event)` giữa graph và hợp đồng chức năng; P12 yêu cầu terminal chỉ có
self-loop stutter; P13 cấm làm mất bằng chứng đã thu tiền mà không đi refund;
P14 giữ an toàn khi payment UNKNOWN; P15 cấm một event có nhiều quyết định trong
cùng state. Cùng P1–P10 về safety, consistency, reachability, incident
completeness và liveness dưới weak fairness, model đúng đạt **15/15 PASS**.

Trong lần chạy đầu, P10 phát hiện vòng retry vô hạn
`PAYMENT_PROCESSING → PAYMENT_RETRY → PAYMENT_PROCESSING`. Model được sửa bằng
state `PAYMENT_PROCESSING_FINAL`, khiến retry có bound thật trong graph thay vì
chỉ được mô tả bằng lời.

Sáu mutation được chèn độc lập để kiểm tra sức phát hiện của thuộc tính:

- thanh toán Order đã CANCELLED;
- chấp nhận payment sai amount;
- tạo hai Payment SUCCESS;
- payment và cancellation cùng sở hữu critical resource;
- bỏ qua refund bằng cách chuyển PAID thẳng sang FAILED;
- coi gateway UNKNOWN là thất bại cuối và giải phóng inventory.

Cả sáu mutation đều sinh lỗi thuộc tính và witness tương ứng; hai mutation mới
xác nhận riêng P13 và P14 không PASS một cách hình thức/vô hiệu. Kết quả nằm trong
`results/order_payment_recovery_model.results.json`; diễn giải từng state nằm
trong `MO_HINH_XU_LY_SU_CO.md`. Trong lần rà soát này còn phát hiện metadata
của PAID/FAILED/REFUNDED từng ánh xạ sai sang `order_status` nhưng checker cũ
vẫn PASS vì chỉ kiểm labels. Dữ liệu đã được sửa và verifier hiện kiểm trực
tiếp cặp `order_status/payment_status` mong đợi cho cả 15 state trước khi xét
P1–P15. Kết quả JSON còn có summary theo từng chức năng; ma trận CSV ghi
`function, state, event/problem, guard/detection, solution, next_state,
guarantees`, nên trả lời trực tiếp “đang ở state này, gặp sự cố kia thì xử lý
thế nào”. Các endpoint retry, reconcile, refund và state
REFUND_PENDING/REFUNDED đã có trong source; riêng CONFIRMATION_RETRY là state
quan sát khi Order vẫn PAID.

#### 3.3.2. Kiểm chứng quyết định chức năng tại từng state

Recovery model không mô phỏng việc gọi API theo từng test case. Mỗi dòng trong
ma trận là một nghĩa vụ hành vi: nếu aggregate đang ở state nguồn và event được
quan sát, hệ thống chỉ được thực hiện action đã khai báo, đi tới state đích của
hợp đồng và giữ các guarantee tương ứng. P11 đối chiếu độc lập hợp đồng với
graph; vì vậy thiếu event, thừa event, gán event cho hai chức năng hoặc chọn sai
state đích đều tạo counterexample.

| State nguồn | Sự kiện/sự cố | Quyết định bắt buộc | State đích | Bất biến được giữ |
|---|---|---|---|---|
| CREATED/VALIDATED | Input sai, lỗi DB, mismatch hoặc commit lỗi | Reject/rollback nguyên tử; không tạo Payment | PRE_PAYMENT_REJECTED | `payment_status=NONE`, inventory không bị giữ một phần |
| PAYMENT_PENDING | Cancel thắng transaction | Hủy Order và giải phóng reservation | CANCELLED | Không có `payment_success` |
| PAYMENT_PROCESSING | Timeout hoặc kết quả gateway chưa rõ | Giữ idempotency key, chuyển sang bounded retry/reconcile | PAYMENT_RETRY | Không coi timeout là decline; inventory vẫn reserved |
| PAYMENT_PROCESSING | Gateway decline xác thực | Kết thúc payment và giải phóng inventory | FAILED | Chỉ outcome cuối đã xác thực mới được FAILED |
| PAID/CONFIRMED | Không thể fulfillment | Khởi tạo compensation, không xóa dấu vết đã charge | REFUND_PENDING | `payment_success` được giữ cho tới khi refund có bằng chứng |
| REFUND_PENDING | Refund timeout | Query/retry cùng refund key và cảnh báo vận hành | REFUND_PENDING | Không hoàn tiền hai lần, không suy diễn timeout là chưa refund |
| Terminal | Callback/request đến muộn | No-op hoặc reject và audit | Giữ nguyên state | Không hồi sinh aggregate |

State `PRE_PAYMENT_REJECTED` là điểm tinh chỉnh quan trọng: lỗi trước khi tồn tại
Payment không còn bị nhập chung với `FAILED`, là state dành cho payment đã nhận
decline cuối. Ma trận đầy đủ 37 quyết định được sinh trực tiếp tại
`results/order_payment_state_response_matrix.csv`, không chép tay vào báo cáo.

P1–P15 PASS chỉ kết luận thiết kế hữu hạn đã khai báo thỏa các thuộc tính. Nó
không chứng minh controller/service/database tự động phù hợp model. Unit và
integration test là bằng chứng đối chiếu implementation riêng; 5 integration
test hiện vẫn SKIP vì thiếu Docker nên chưa có kết luận end-to-end runtime.

### 3.4. Kiểm thử dữ liệu đầu vào bằng xUnit

Đã xây dựng project `OrderPaymentMicroservice.Tests` và bộ test `InputValidatorTests.cs`. Các test chạy độc lập với PostgreSQL và không cần khởi động API.

Các nhóm trường hợp đã kiểm tra gồm null request, null item, danh sách item null hoặc rỗng; ID không hợp lệ; số tiền âm, bằng 0 hoặc vượt giới hạn; số lượng dưới/trên biên; giá âm hoặc vượt mức tối đa; các giá trị biên hợp lệ; nhiều item; sai lệch tổng tiền trong và ngoài tolerance; decimal.MaxValue và phép nhân lớn nhất sau validation.

Riêng InputValidator có **34 trường hợp kiểm thử**. Sau khi bổ sung test hồi quy cho hai state machine, lifecycle Order, gateway timeout/decline, bounded retry, crash reconciliation, refund, hoàn tồn kho và race payment–cancel, toàn bộ unit test suite trong lần xác minh ngày 08/10/2026 đạt **84 PASS, 0 FAIL, 0 SKIP**. Khi chạy solution còn có 5 integration test Testcontainers bị SKIP vì môi trường không có Docker; không tính các test này là PASS.

Các test từng ghi nhận tổng tiền sai hoặc giá dưới mức tối thiểu được chuyển thành test yêu cầu từ chối. Test mới còn kiểm tra giá phía server, giảm tồn kho, chặn giá client bị sửa, chặn thanh toán đơn CANCELLED, đối chiếu số tiền, chuyển PAYMENT_PENDING → PAID, idempotency, password hashing và phát hành JWT.

### 3.5. Đo độ bao phủ và phân tích nhánh thiếu

Đã sử dụng Coverlet để đo coverage, giới hạn báo cáo vào lớp InputValidator và đặt ngưỡng branch coverage tối thiểu 90%.

| Phạm vi | Branch coverage |
|---|---|
| Toàn bộ InputValidator | 31/32 nhánh, khoảng 96,87% |
| ValidateCreateOrderRequest() | 100% |
| ValidateOrderItem() | Khoảng 92,85% |

Mục tiêu **≥ 90% branch coverage đã đạt**. Line coverage toàn lớp là 84,31%, một chỉ số khác với branch coverage.

Nhánh tolerance đã được test cả hai hướng và hiện ném `ArgumentException` khi sai lệch lớn hơn 0,01. Giá thấp hơn `MIN_PRICE` cũng bị từ chối. Lần đo lại ngày 24/09/2026 vẫn đạt **31/32 nhánh = 96,87%**, line coverage tăng lên **84,31%**.

Nhánh chưa được phủ là hướng true của `subtotal < 0` tại dòng 101 của source hiện tại. Với Quantity từ 1 đến 10000 và UnitPrice từ 0,01 đến 999999,99 sau các guard, subtotal không thể âm. Catch OverflowException cũng chưa chạy do tích tối đa vẫn nằm trong miền decimal. Báo cáo đã giải thích các đường đi bất khả thi này thay vì sửa mã hoặc ép dữ liệu để tăng coverage hình thức.

### 3.6. Nhận diện và đánh giá rủi ro

Đã xây dựng `vulnerabilities.json` theo cấu trúc dataset Lab 3, mô tả vị trí, nguyên nhân, điều kiện khai thác và tác động của năm phát hiện. Đã chạy hàm đánh giá của `risk_assessor.py` để tính điểm và sắp xếp ưu tiên.

| ID | Phát hiện ban đầu | CVSS-lite | Trạng thái source hiện tại |
|---|---|---:|---|
| VULN-001 | API Order thiếu xác thực và bảo vệ truy cập | 9,1 | Đã thêm JWT, `[Authorize]`, role Admin và identity từ claim; integration test đã có nhưng chưa chạy PASS do thiếu Docker |
| VULN-005 | Thiếu kiểm tra quyền sở hữu giỏ hàng | 8,1 | Đã đối chiếu user/cart với principal; integration test 401/403 đã có nhưng chưa chạy PASS |
| VULN-002 | Giá và tổng tiền đơn hàng do client quyết định | 7,5 | Đã lấy giá Product phía server, tính lại total và có test hồi quy |
| VULN-003 | Đơn CANCELLED có thể được chuyển sang PAID | 7,5 | Đã thêm state machine/guard và test chặn thanh toán đơn hủy |
| VULN-004 | Tổng tiền sai lệch vẫn được validator chấp nhận | 7,5 | Đã từ chối mismatch và giá dưới tối thiểu; test cũ đã đổi thành test an toàn |

Các điểm CVSS-lite trong bảng phản ánh mức rủi ro **tại thời điểm phát hiện**, không phải điểm rủi ro còn lại sau sửa. Source hiện đã có biện pháp khắc phục cho cả năm phát hiện. VULN-001 và VULN-005 đã có integration test khởi động API/PostgreSQL để kiểm tra 401/403 và ownership, nhưng lần xác minh hiện tại bị SKIP do Docker không khả dụng nên chưa được đóng ở mức hệ thống chạy thật.

Điểm 8,1 HIGH được tính với tác động confidentiality LOW, integrity HIGH và availability NONE. Chưa có bằng chứng về mất khả dụng toàn dịch vụ nên không nâng tác động availability. Toàn bộ điểm số là **CVSS-lite phục vụ học tập**, không phải CVSS chính thức; các phát hiện quyền truy cập dựa trên review mã, chưa phải khai thác trực tiếp API đang chạy.

### 3.7. Mô phỏng và kiểm chứng lỗi bộ nhớ bằng C

Đã xây dựng bộ chương trình C trong `security-analysis/c-memory`, lấy ngữ cảnh từ năm module của OrderPaymentMicroservice để minh họa việc quản lý bộ nhớ thủ công. Đây là mô hình phục vụ học tập, không phải bản dịch đầy đủ của ứng dụng và không phải bằng chứng rằng source C# đang có các lỗi `malloc/free` tương ứng. Ứng dụng chính sử dụng C# và garbage collector; phần C được dùng để quan sát rõ ownership, lifetime, cấp phát và giải phóng bộ nhớ.

| Module C | Ngữ cảnh nghiệp vụ | Lỗi cố ý | CWE | Cách sửa được kiểm chứng |
|---|---|---|---|---|
| `order.c` | Order sở hữu danh sách OrderItem | Quên giải phóng mảng item trước khi giải phóng Order | CWE-401 Memory Leak | `free(order->items)` trước `free(order)` |
| `payment.c` | Dọn dẹp mã giao dịch khi gateway thất bại | Hai nhánh cùng giải phóng transaction code | CWE-415 Double Free | Xóa con trỏ sở hữu sau lần `free` đầu; cleanup nhận `NULL` |
| `cart.c` | Xóa CartItem rồi ghi audit log | Đọc `product_id` sau khi item đã bị giải phóng | CWE-416 Use After Free | Sao chép giá trị cần log trước khi `free` |
| `product.c` | Nhập tên sản phẩm vào buffer heap | `strcpy` chuỗi dài vào vùng nhớ 32 byte | CWE-122 Heap Buffer Overflow | Kiểm tra độ dài, dành một byte cho ký tự kết thúc `\0` |
| `user.c` | Nhập email vào mảng cục bộ | `strcpy` chuỗi dài vào buffer stack 32 byte | CWE-121 Stack Buffer Overflow | Từ chối input từ 32 byte trở lên |

Mỗi source được biên dịch thành hai executable. Bản `*_buggy` giữ lỗi cố ý; bản `*_fixed` được biên dịch với macro `-DFIXED` để đưa phần sửa vào chương trình. Cả hai bản đều bật AddressSanitizer và UndefinedBehaviorSanitizer; LeakSanitizer được bật khi chạy test. Vì cùng một source tạo ra hai phiên bản, phần khác biệt giữa lỗi và sửa có thể được đối chiếu trực tiếp tại các khối `#ifdef FIXED`.

Bộ test tự động chạy **22 trường hợp và đạt 22/22 PASS**:

- Năm bản lỗi phải thoát khác 0 và log phải có đúng chẩn đoán mong đợi: leak, double-free, heap-use-after-free, heap-buffer-overflow hoặc stack-buffer-overflow.
- Năm bản sửa chạy cùng kịch bản phải thoát mã 0 và không có báo cáo sanitizer.
- Product và User được kiểm tra các độ dài 0, 1, 31, 32 và 128 byte. Với buffer 32 byte, 31 byte nội dung là biên hợp lệ vì cần một byte cuối cho `\0`; input từ 32 byte trở lên phải bị từ chối.
- Bản Product/User có lỗi còn được thử với input ngắn `demo` để chứng minh code lỗi có thể vẫn chạy với dữ liệu thuận lợi; chạy thành công một lần không chứng minh chương trình an toàn.

Trong kết quả này, `PASS cart_buggy` có nghĩa test đã kích hoạt và nhận diện đúng lỗi use-after-free, không có nghĩa bản buggy an toàn. Các log lưu vị trí cấp phát, vị trí giải phóng và vị trí truy cập sai, giúp truy nguyên vòng đời vùng nhớ. Bản fixed PASS mang ý nghĩa khác: chương trình thoát bình thường và không có dấu hiệu sanitizer trong ca đã chạy.

Chương 2 hiện còn có pipeline static analysis tái lập bằng `make static-check`.
Project-specific pattern scanner phát hiện đúng cả năm loại lỗi trên BUGGY và
không còn finding tương ứng trên FIXED. Cppcheck 2.13.0 chỉ phát hiện trực tiếp
stack overflow của `user.c` và một cảnh báo style; bốn lỗi còn lại bị bỏ sót.
Kết quả này được giữ nguyên để minh họa false negative, không diễn giải thành
“cppcheck chứng minh an toàn”. GCC syntax check chạy cho cả hai cấu hình.

`layout_probe.c` đo layout thực tế trên x86-64: CartItem 16/8, OrderItem 16/8,
Order 24/8, Payment 16/8 và Product 16/8 (size/alignment). Phân tích padding và
offset được dùng để nối ownership/lifetime với kiến thức alignment, nhưng không
coi tối ưu kích thước là bằng chứng memory safety. Snapshot static/layout nằm ở
`c-memory/evidence/static-analysis.json`; snapshot 22 ca sanitizer nằm ở
`c-memory/evidence/dynamic-analysis.json`. Raw stack trace trong `results/` là
artifact cục bộ và bị ignore.

### 3.8. Kiểm chứng số học bằng SMT/Z3

`PaymentVerification.z3` biểu diễn tiền bằng số nguyên cent và dùng `QF_NIA`,
vì biểu thức `Quantity × UnitPriceCents` là phép nhân hai biến tượng trưng.
Phiên bản cũ khai báo `QF_LIA`, khiến Z3 từ chối assertion phi tuyến; các kết
quả SAT sau lỗi đó không có giá trị. Script `verify_payment_smt.py` hiện yêu cầu
Z3 thoát mã 0 và đối chiếu chính xác kết quả mong đợi của từng truy vấn.

| Truy vấn | Kỳ vọng/kết quả | Ý nghĩa |
|---|---|---|
| Order PAID hợp lệ | SAT | Có model tổng tiền/payment nhất quán |
| Total khác tổng item | UNSAT | Invariant loại mismatch |
| PAID nhưng payment thiếu | UNSAT | PAID cần đủ payment evidence |
| Payment âm | UNSAT | Miền số tiền loại giá trị âm |
| Payment vượt total | UNSAT | Không cho overpayment trong model |
| Tràn subtotal Int32 trong miền item | SAT | Counterexample `10000 × 99999999 = 999999990000` |
| Total bound nhưng subtotal vượt max | UNSAT | Bound toàn đơn loại trường hợp đó khỏi order hợp lệ |

Z3 5.1.0 chạy **7/7 đúng kỳ vọng**. Kết quả được lưu tại
`results/payment_verification.results.json`. Đây là kiểm chứng mô hình số học
trên mathematical integers; không thay thế kiểm thử EF Core, decimal runtime,
gateway hoặc concurrency.

## 4. Minh chứng và khả năng tái lập

Các sản phẩm bàn giao đã được lưu trong repository:

- [Model Order kỳ vọng](order_state_model.json) và [model BUGGY](order_state_model_buggy.json).
- [Bộ test xUnit](../OrderPaymentMicroservice.Tests/InputValidatorTests.cs).
- [Báo cáo coverage XML](results/input-validator.cobertura.xml) và [tổng hợp coverage](results/coverage_summary.json).
- [Dataset lỗ hổng](vulnerabilities.json) và [kết quả xếp hạng rủi ro](results/risk_assessment_results.json).
- [Kết quả model kỳ vọng](results/order_state_model.results.json) và [kết quả model BUGGY](results/order_state_model_buggy.results.json).
- [Script tích hợp](run_analysis.py) và [hướng dẫn chạy lại](README.md).
- [Threat model](threat_model.json), [diễn giải Chương 1](THREAT_MODEL_CHUONG1.md) và [kết quả kiểm tra](results/threat_model_results.json).
- [Recovery model](order_payment_recovery_model.json), [kết quả](results/order_payment_recovery_model.results.json) và [ma trận state/sự cố](results/order_payment_state_response_matrix.csv).
- [Mã mô phỏng lỗi bộ nhớ bằng C](c-memory/modules), [hướng dẫn và ánh xạ module](c-memory/README.md).
- [Static/layout evidence](c-memory/evidence/static-analysis.json) và [dynamic sanitizer evidence](c-memory/evidence/dynamic-analysis.json).
- [Đặc tả SMT](../OrderPaymentMicroservice/Security/PaymentVerification.z3), [runner](verify_payment_smt.py) và [kết quả Z3](results/payment_verification.results.json).

Dự án còn có đặc tả TLA+ và config trong thư mục Security. Chưa có output TLC/BMC được lưu; không dùng kết quả explicit-state hoặc SMT số học để thay thế bằng chứng này.

## 5. Hạn chế và công việc chưa hoàn thành

Năm phát hiện ưu tiên đã có biện pháp khắc phục trong source và một phần đã có unit test hồi quy. Phần còn thiếu tập trung vào **bằng chứng tích hợp, dịch vụ bên ngoài và mở rộng phạm vi kiểm chứng**, thay vì chỉ thêm các guard đơn lẻ.

| Hạng mục chưa hoàn thành | Hiện trạng | Khoảng trống cần xử lý |
|---|---|---|
| Payment gateway thật | Có `IPaymentGateway` và adapter giả lập idempotent; service xử lý success/decline/unknown/retry/reconcile/refund | Chưa có adapter nhà cung cấp, xác minh callback và integration test sandbox |
| Integration/E2E với PostgreSQL | Đã có 5 test Testcontainers tự khởi động API/database sạch và bao phủ luồng chính | Lần xác minh 08/10 bị skip do Docker không khả dụng; cần chạy lại đủ 5 test và lưu log PASS |
| Kiểm thử authentication/authorization | Đã có JWT, `[Authorize]`, role, ownership check và integration test cho 401/403 | Test đã được viết nhưng chưa có kết quả chạy trong lần xác minh vì Docker không khả dụng |
| Đồng thời và idempotency trên database thật | Có transaction `Serializable`, idempotency key, unique index và test PostgreSQL cho payment/cancel cạnh tranh | Hai test concurrency bị skip cùng integration suite; chưa có log PASS từ môi trường hiện tại |
| Vòng đời Order đầy đủ | Đã có các state chính, inventory RESERVED/RELEASED/CONSUMED và `PaymentRecoveryWorker` tự quét giao dịch stale | Cần kiểm chứng worker qua restart thật và chạy integration suite trên Docker |
| SMT/Z3 | **Đã hoàn thành trong phạm vi mô hình số học:** QF_NIA, Z3 5.1.0, 7/7 đúng kỳ vọng và có JSON evidence | Không chứng minh implementation, database hoặc gateway; giả định tiền được biểu diễn bằng integer cents |
| TLA+/BMC | `OrderPayment.tla` và `OrderPayment.cfg` đã được đồng bộ với hai state machine, cùng Kripke/recovery model | Chưa chạy TLC và chưa lưu output/counterexample; model chưa sinh tự động từ implementation |
| Fuzzing | Source có đánh dấu target cho validator/payment | Chưa có harness, corpus, seed, crash artifact, timeout policy hoặc số liệu coverage của fuzzing |
| Coverage toàn hệ thống | InputValidator đạt 96,87% branch coverage | Chưa có báo cáo coverage riêng cho Controller, Service, state machine và các nhánh lỗi database |
| Hardening vận hành | Secret nằm ngoài source, CORS Development có allowlist | Chưa có rate limiting, rotation/revocation JWT, audit log bảo mật, health check, metrics/cảnh báo và cấu hình production được kiểm chứng |
| Script demo và tài liệu API | Frontend, `test-api.ps1`, contract JWT và idempotency đã được đồng bộ | Cần chạy lại script trên database sạch và lưu bằng chứng demo |

Phần C đã có dynamic analysis bằng sanitizer cho năm lỗi bộ nhớ mô phỏng. Tuy nhiên, bộ C không sử dụng PostgreSQL, gateway, request HTTP hoặc dữ liệu thật. Kết quả 22/22 chỉ là bằng chứng cho các executable và input đã chạy; nó không chứng minh mọi input an toàn và không chứng minh ứng dụng C# đã được kiểm tra an toàn bộ nhớ native. Bộ mô phỏng chưa bao quát race condition, mọi nhánh lỗi cấp phát, alias phức tạp hoặc tài nguyên unmanaged trong .NET.

Không đưa ra phần trăm hoàn thành toàn dự án vì chưa có rubric và tiêu chí nghiệm thu tổng thể. Mỗi hạng mục chỉ được đánh dấu hoàn thành khi có source, test hoặc log chạy tái lập và giải thích được giới hạn của bằng chứng.

## 6. Kế hoạch giai đoạn tiếp theo

1. **Chạy và lưu bằng chứng integration test:** bật Docker, chạy đủ 5 test Testcontainers từ database sạch và lưu log xác nhận luồng chính, authorization và concurrency đều PASS.
2. **Mở rộng kiểm chứng quyền truy cập:** bổ sung token hết hạn/sai chữ ký và xác nhận phản hồi 401, 403, 404 không làm rò rỉ dữ liệu nhạy cảm.
3. **Kiểm chứng phục hồi sau restart:** chạy `PaymentRecoveryWorker` với payment/refund stale trên PostgreSQL thật, restart API và xác nhận retry/reconcile giữ đúng idempotency.
4. **Tích hợp payment gateway ở môi trường thử nghiệm:** xác minh chữ ký webhook/callback, đối chiếu order/amount/currency, xử lý timeout và kết quả FAILED, lưu transaction code thật. Tiêu chí đạt là Order chỉ chuyển PAID sau bằng chứng thành công hợp lệ từ gateway.
5. **Kiểm thử đồng thời trên PostgreSQL:** gửi nhiều request cùng idempotency key và nhiều request khác key cho một Order; kiểm tra unique index, transaction và tồn kho. Tiêu chí đạt là chỉ có một Payment SUCCESS, không bán âm kho và retry trả lại cùng kết quả.
6. **Hoàn thiện TLC/BMC:** SMT số học đã có bằng chứng 7/7; bước còn lại là chạy TLC hoặc bounded checker cho state-transition model, lưu bound/completeness threshold, version, output và counterexample nếu có.
7. **Bổ sung fuzzing:** chọn InputValidator, PaymentValidator hoặc parser request làm target; tạo seed corpus từ các ca biên hiện có, đặt timeout và lưu input gây lỗi. Tiêu chí đạt là có harness, corpus, thời lượng chạy, coverage và crash report hoặc kết luận không tìm thấy crash trong phạm vi thử.
8. **Mở rộng coverage và hardening:** đo coverage Service/Controller, bổ sung rate limiting, audit log và cấu hình production; cập nhật script demo theo JWT và idempotency. Tiêu chí đạt là có báo cáo coverage mới và một kịch bản demo E2E chạy thành công từ môi trường sạch.

Thứ tự ưu tiên đề xuất là integration/authorization trước, sau đó vòng đời Order và gateway, rồi concurrency, formal verification, fuzzing và hardening. Cách sắp xếp này tạo bằng chứng rằng các biện pháp đã có trong source thực sự hoạt động trên hệ thống chạy thật trước khi mở rộng sang các kỹ thuật kiểm chứng khác.

## 7. Kết luận

Phần 8 dưới đây trình bày chi tiết quá trình thực hiện, kiến thức áp dụng và các lỗi gặp phải để phục vụ thuyết trình hoặc báo cáo tiến độ.

Dự án đã xây dựng được nền tảng đánh giá an toàn phần mềm gồm threat modeling, kiểm chứng model, kiểm thử có coverage, static/dynamic memory analysis và SMT. Kết quả nổi bật gồm threat model PASS với 6 asset/4 boundary/9 threat/6 STRIDE; model Order 4/4 PASS; recovery model 15/15 PASS trên 7 hợp đồng chức năng, 15 state và 37 transition; sáu mutation bị phát hiện; Z3 7/7 đúng kỳ vọng; 84 unit test PASS; branch coverage InputValidator 96,87%; static scanner phân biệt đủ 5 BUGGY/FIXED và sanitizer 22/22 PASS. Năm integration test có harness nhưng đều SKIP do thiếu Docker.

Quan trọng hơn, năm phát hiện ưu tiên đã được chuyển thành guard và unit test hồi quy trong source hiện tại. Tiến độ hiện nay được đánh giá là **đã củng cố Chương 1, Chương 2 và SMT; còn thiếu TLC/BMC, fuzzing và bằng chứng integration/E2E chạy thật**. Vì vậy dự án vẫn chưa hoàn tất chuỗi end-to-end bốn chương và chưa đủ cơ sở kết luận an toàn để vận hành thực tế.

## 8. Chi tiết thực hiện và kiến thức an toàn phần mềm đã áp dụng

### 8.1. Phân tích mã nguồn để xác định yêu cầu an toàn

**Tôi đã làm gì?**

Tôi đối chiếu luồng xử lý Order và Payment với các yêu cầu trong tài liệu ATPM và `WorkflowAnalysis.md`, xác định những điều kiện phải luôn đúng khi hệ thống vận hành. Tôi tập trung vào trạng thái đơn hàng, tính nhất quán của số tiền và quyền truy cập tài nguyên.

**Tôi làm như thế nào?**

Tôi xem `OrderStatus.cs` để xác định tập trạng thái; xem `OrderService.CreateOrderAsync()` để xác định cách tiếp nhận và lưu đơn hàng; xem `CancelOrderAsync()` để biết điều kiện hủy; xem `PaymentService.CreatePaymentAsync()` để biết cách tạo thanh toán và cập nhật trạng thái. Với dữ liệu đầu vào, tôi đọc các điều kiện trong InputValidator và DTO thay vì chỉ dựa vào giao diện nhập liệu. Với quyền truy cập, tôi đối chiếu Controller, Service và cấu hình middleware trong `Program.cs`.

**Tôi sử dụng kiến thức gì?**

Tôi áp dụng kiến thức về bất biến nghiệp vụ, máy trạng thái hữu hạn và ranh giới tin cậy. Bất biến là điều kiện không được phép bị phá vỡ, chẳng hạn một đơn PAID phải có thanh toán thành công. Ranh giới tin cậy giúp nhận ra rằng ID, giá và số tiền từ client chưa thể được coi là dữ liệu đáng tin cậy chỉ vì chúng đúng kiểu dữ liệu.

**Kết quả và vấn đề gặp phải**

Tôi chuyển yêu cầu nghiệp vụ thành bốn thuộc tính của model Order cơ sở, sau đó
mở rộng recovery model thành 7 hợp đồng chức năng và 15 thuộc tính P1–P15. Mỗi
hợp đồng trả lời rõ ở state hiện tại, khi event hoặc sự cố xảy ra thì hệ thống
phải xử lý và chuyển đi đâu. Tôi cũng tách rõ kiểm chứng thiết kế khỏi test dùng
để đối chiếu mã chạy thực tế; số test PASS không được dùng thay cho chứng minh
trên Kripke.

### 8.2. Xây dựng model Order theo cấu trúc Kripke

**Tôi đã làm gì?**

Tôi tạo `order_state_model.json` để biểu diễn luồng Order thành dữ liệu mà công cụ Lab 1 có thể đọc và kiểm tra.

**Tôi làm như thế nào?**

Tôi giữ schema JSON của Lab 1 và ánh xạ từng thành phần:

- **S:** tám trạng thái từ CREATED đến FAILED, lưu trong `states`.
- **S0:** tập trạng thái ban đầu `{CREATED}`, lưu trong `initial_states`.
- **R:** danh sách cạnh `from`, `to`, `event`, mô tả các bước tiến triển, hủy và thất bại.
- **L:** nhãn của từng trạng thái, ví dụ `paid`, `payment_success`, `terminal`.

Tôi gắn `payment_success` vào PAID, CONFIRMED và COMPLETED trong model kỳ vọng. COMPLETED, CANCELLED và FAILED được gắn `terminal` và có self-loop để trạng thái kết thúc vẫn có bước kế tiếp theo ngữ nghĩa Kripke. Script `create_datasets.py` giúp sinh lại model theo cùng quy tắc; `run_analysis.py` đọc các model đã lưu để kiểm chứng.

**Tôi sử dụng kiến thức gì?**

Tôi sử dụng cấu trúc Kripke M = (S, S0, R, L), mệnh đề nguyên tử và logic thời gian CTL. Tôi phân biệt safety — không có hành vi xấu — với liveness — cuối cùng có sự tiến triển mong muốn. Tôi cũng sử dụng khái niệm trạng thái reachable để chỉ kiểm tra những trạng thái có thể đi đến từ trạng thái ban đầu.

**Kết quả và giới hạn**

Model đúng có tám trạng thái và cả tám đều reachable. Các nhãn thanh toán là giả định của thiết kế, chưa phải dữ liệu lấy từ gateway hoặc database. Do đó, P3/P4 PASS xác nhận sự nhất quán của nhãn trong model, không xác nhận payment thực tế đã được đối soát.

### 8.3. Chạy kiểm chứng và bổ sung thuộc tính liveness

**Tôi đã làm gì?**

Tôi viết phần tích hợp để sử dụng trực tiếp công cụ Lab 1 trên model Order, đồng thời bổ sung phép kiểm tra mà script mẫu chưa hỗ trợ.

**Tôi làm như thế nào?**

Trong `run_analysis.py`, tôi dùng `importlib` để nạp module gốc theo đường dẫn. Tôi chuyển các thuộc tính được hỗ trợ vào `run_verification()`: P1 dùng AG_NOT, P3 và P4 dùng AG_IMPLIES. Công cụ mẫu duyệt BFS để tìm trạng thái reachable và đường đi đến trạng thái vi phạm.

Với P2, tôi tính tập trạng thái thỏa `AF terminal` bằng điểm cố định nhỏ nhất:

1. Khởi tạo tập `good` bằng các trạng thái đã có nhãn terminal.
2. Thêm một trạng thái nếu nó có successor và **tất cả** successor đều nằm trong `good`.
3. Lặp cho đến khi không thêm được trạng thái nào nữa.
4. Kiểm tra mọi trạng thái reachable có nằm trong `good` hay không; đây là phép kiểm tra `AG AF terminal`.

**Tôi sử dụng kiến thức gì?**

Tôi sử dụng CTL: AG nghĩa là trên mọi đường đi, tại mọi trạng thái; AF nghĩa là trên mọi đường đi, cuối cùng; EF nghĩa là tồn tại một đường đi cuối cùng đạt điều kiện. Thuật toán điểm cố định cho phép phân biệt việc *có thể kết thúc* với việc *chắc chắn cuối cùng kết thúc* trong model hữu hạn.

**Tôi gặp khó khăn gì và xử lý ra sao?**

Lab 1 chỉ hỗ trợ AG_NOT, AG_IMPLIES và EF. Nếu dùng EF terminal thay cho liveness thì kết luận sẽ yếu hơn yêu cầu: một đường đi kết thúc không loại trừ đường đi khác chờ vô hạn. Tôi giải quyết bằng cách bổ sung AF ở script tích hợp, không sửa script Lab gốc và không tuyên bố Lab 1 nguyên bản đã hỗ trợ đủ bốn thuộc tính.

**Kết quả**

Model kỳ vọng PASS bốn thuộc tính. Kết quả được lưu trong `results/order_state_model.results.json`. Script có assertion yêu cầu toàn bộ thuộc tính của model này phải PASS, giúp phát hiện thay đổi ngoài ý muốn trong lần chạy sau. Liveness được chứng minh trong model không có vòng chờ nonterminal; chưa bao quát tình huống gateway không phản hồi vô hạn.

### 8.4. Tạo model BUGGY và giải thích counterexample

**Tôi đã làm gì?**

Tôi cố ý tạo một model có lỗi để kiểm tra công cụ có thực sự nhận ra vi phạm safety hay không. Lỗi được chọn là cho phép một đơn CANCELLED được thanh toán và chuyển sang PAID.

**Tôi làm như thế nào?**

Tôi sao chép model kỳ vọng và thêm trạng thái `PAID_AFTER_CANCELLED`, có các nhãn `paid`, `payment_success` và `invalid_transition`. Tôi thêm cạnh từ CANCELLED đến trạng thái này, với sự kiện mô tả `CreatePaymentAsync: missing order status guard`.

Trạng thái mới không phải một giá trị enum được thêm vào ứng dụng. Nó là trạng thái của model dùng để phân biệt PAID hợp lệ với PAID có lịch sử đi qua CANCELLED.

**Tôi sử dụng kiến thức gì?**

Tôi sử dụng counterexample trong model checking và kỹ thuật history monitor: bổ sung thông tin lịch sử vào trạng thái để biến lỗi của một bước chuyển thành điều kiện có thể kiểm tra trên nhãn trạng thái. BFS của Lab 1 cung cấp đường đi ngắn tới trạng thái vi phạm.

**Tôi gặp giới hạn gì?**

Nếu chỉ thêm cạnh CANCELLED → PAID vào model cũ, nhãn PAID vẫn không đổi. Bộ kiểm tra AG trên mệnh đề trạng thái sẽ không tự hiểu cạnh đó bất hợp lệ. Tôi giải quyết bằng history monitor và nhãn `invalid_transition`. Nhãn này được thiết lập khi xây dựng model, không phải công cụ tự suy ra tính hợp lệ của mọi cạnh.

**Kết quả**

P1 FAIL với đường đi `CREATED → CANCELLED → PAID_AFTER_CANCELLED`; P2, P3 và P4 vẫn PASS. Điều này có ý nghĩa: model lỗi vi phạm quy tắc trạng thái dù vẫn có nhãn payment_success. Thành công của kiểm chứng ở đây chính là tìm được vi phạm dự kiến. Script còn assert đúng chuỗi counterexample để kiểm tra kết quả tái lập.

### 8.5. Thiết kế và chạy bộ test InputValidator

**Tôi đã làm gì?**

Tôi tạo project xUnit riêng và xây dựng 34 trường hợp kiểm thử cho `ValidateCreateOrderRequest()` cùng phần kiểm tra item được gọi bên trong.

**Tôi làm như thế nào?**

Tôi tạo hàm `Valid()` trả về request nền hợp lệ: CustomerId = 1, TotalAmount = 10, một item có ProductId = 1, Quantity = 1 và UnitPrice = 10. Mỗi test chỉ thay đổi dữ liệu liên quan đến điều kiện cần kiểm tra. Cách này tránh trường hợp test muốn kiểm tra giá nhưng lại bị chặn sớm vì CustomerId sai.

Tôi dùng `[Fact]` cho trường hợp đơn lẻ và `[Theory]`/`[InlineData]` cho nhóm dữ liệu. Với input bị từ chối, tôi dùng `Assert.Throws` và kiểm tra `ParamName` để xác nhận đúng loại lỗi và đúng trường dữ liệu. Các số decimal được parse bằng InvariantCulture để không phụ thuộc dấu thập phân của máy chạy test.

**Tôi sử dụng kiến thức gì?**

Tôi áp dụng phân lớp tương đương, phân tích giá trị biên, kiểm thử đường đi và test oracle — tiêu chí để quyết định hành vi đúng/sai. Tôi kiểm tra cả dữ liệu hợp lệ và dữ liệu không hợp lệ, thay vì chỉ kiểm tra luồng tạo đơn thành công.

| Nhóm | Dữ liệu tiêu biểu | Mục đích |
|---|---|---|
| Null/rỗng | Request null, item null, Items null hoặc [] | Kiểm tra xử lý thiếu dữ liệu |
| ID | 0, -1, CustomerId = int.MinValue | Kiểm tra miền ID dương |
| Total | -1, 0, 0.009, 1000000, decimal.MaxValue | Kiểm tra dưới/trên giới hạn |
| Quantity | 0, -1, 10001, int.MaxValue | Kiểm tra miền số lượng |
| Price | -0.01, 1000000, decimal.MaxValue | Kiểm tra giá âm hoặc quá lớn |
| Biên hợp lệ | Total 0.01/999999.99, Quantity 10000 | Kiểm tra biên được chấp nhận |
| Tolerance | Total 10.01/9.99 so với tổng item 10 | Kiểm tra đúng ngưỡng ±0.01 |
| Input phải bị từ chối | Total 10.011/9.989 so với 10; giá 0/0.001 | Xác nhận validator hiện từ chối mismatch vượt tolerance và giá dưới `MIN_PRICE` |

**Kết quả và lỗi được phát hiện**

34 trường hợp riêng cho InputValidator đều PASS. Các test trước đây dùng để ghi nhận lỗi đã giúp xác nhận tổng tiền sai và giá dưới mức tối thiểu từng được chấp nhận. Sau khi sửa validator, chúng được đổi thành `MismatchOrBelowMinimumPriceIsRejected` và hiện yêu cầu `ArgumentException`. Điều này biến characterization test thành regression test: nếu lỗi xuất hiện lại, test sẽ FAIL.

Toàn unit test suite hiện có 84 trường hợp PASS. Ngoài 34 test InputValidator, các test còn lại kiểm tra giá server/tồn kho, transition hợp lệ và bị cấm của Order/Payment, amount/idempotency, timeout/unknown, bounded retry, crash reconciliation, refund, hoàn kho, race payment–cancel, password hashing, JWT và nâng cấp tài khoản SHA-256 cũ. Năm integration test Testcontainers tồn tại nhưng bị SKIP trong lần xác minh hiện tại, nên không được cộng vào 84 PASS.

### 8.6. Đo coverage và giải thích vì sao chưa đạt 100% toàn lớp

**Tôi đã làm gì?**

Tôi sử dụng `coverlet.msbuild` để đo branch coverage của InputValidator và xuất báo cáo Cobertura XML, kèm ngưỡng tối thiểu 90%.

**Tôi làm như thế nào?**

Tôi chạy `dotnet test` với `CollectCoverage=true`, lọc Include vào đúng lớp InputValidator và đặt `ThresholdType=branch`. Sau đó, script Python đọc XML, lấy coverage từng method và liệt kê các dòng có nhánh chưa được phủ. Báo cáo tóm tắt được lưu thành JSON để dễ tra cứu.

**Tôi sử dụng kiến thức gì?**

Tôi phân biệt line coverage — dòng mã đã chạy — với branch coverage — kết quả true/false của điều kiện đã được thực hiện. Tôi cũng áp dụng phân tích miền giá trị để xác định một đường đi có khả thi hay không.

**Kết quả cụ thể**

Toàn lớp có 31/32 nhánh được phủ, đạt 96,875% theo số đếm; CLI/XML hiển thị xấp xỉ 96,87%. Riêng `ValidateCreateOrderRequest()` đạt 100% branch coverage. Nhánh tolerance có 2/2 hướng được phủ và hiện ném `ArgumentException` khi mismatch vượt 0,01; đây là regression test cho lỗi lịch sử, không còn là hành vi lỗi hiện tại.

Nhánh true của `subtotal < 0` chưa được phủ. Sau guard, Quantity ≥ 1 và UnitPrice ≥ 0 nên tích không âm. Tích lớn nhất là `10000 × 999999.99 = 9.999.999.900`, nằm trong miền decimal; catch overflow chưa được chạy vì không có input đi qua guard hiện tại gây tràn phép nhân này. Kết luận này chỉ áp dụng cho phép nhân item đã giới hạn, không phải chứng minh mọi phép cộng/tính tiền của toàn hệ thống không thể overflow.

**Lỗi công cụ tôi gặp và cách xử lý**

Ban đầu phần đọc XML so sánh thuộc tính `branch` với chuỗi `true`, trong khi Coverlet xuất `True`. Vì vậy danh sách nhánh thiếu bị rỗng dù branch coverage dưới 100%. Tôi sửa thành so sánh sau khi `.lower()`; báo cáo hiện xác định đúng dòng 101 có coverage 50% (1/2).

Bài học là không chỉ tin vào file tổng hợp do mình sinh ra; cần đối chiếu trực tiếp với dữ liệu XML của công cụ đo.

### 8.7. Tạo dataset lỗ hổng và tính CVSS-lite

**Tôi đã làm gì?**

Tôi xây dựng năm bản ghi VULN-001 đến VULN-005 cho chính project, thay vì sử dụng nguyên các lỗi bộ nhớ C trong dataset mẫu Lab 3.

**Tôi làm như thế nào?**

Tôi ghi lại tên, CWE, vị trí mã, mô tả, phương pháp phát hiện và các yếu tố AV/AC/PR/UI/C/I/A. Vì CLI Lab 3 dùng đường dẫn dataset cố định, tôi gọi trực tiếp `assess_all()` với `security-analysis/vulnerabilities.json`. Công cụ tính điểm, gán severity và sắp xếp giảm dần; kết quả được lưu riêng trong project.

**Tôi sử dụng kiến thức gì?**

Tôi sử dụng CWE để phân loại điểm yếu, bộ ba confidentiality/integrity/availability để mô tả tác động, và nguyên tắc đánh giá rủi ro dựa trên khả năng khai thác kết hợp tác động. Tôi phân biệt authentication — người gọi là ai — với authorization — người đó được phép thao tác tài nguyên nào.

**Ví dụ tính điểm VULN-005**

Theo công thức giáo dục của Lab 3:

```text
Exploitability = 8.22 × AV × AC × PR × UI
               = 8.22 × 0.85 × 0.77 × 0.85 × 0.85 ≈ 3.89
Impact = 6.42 × [1 − (1 − C)(1 − I)(1 − A)]
       = 6.42 × [1 − (1 − 0.22)(1 − 0.56)(1 − 0)] ≈ 4.22
Base score = round(min(Exploitability + Impact, 10), 1) = 8.1
Severity = HIGH
```

Đây là công thức thực tế của script Lab 3, không phải công thức CVSS chính thức. Mức điểm phụ thuộc đánh giá tác động và giả định API truy cập được qua mạng, không có lớp xác thực bên ngoài.

**Kết quả và vấn đề phát hiện**

VULN-001 được xếp cao nhất, 9,1 CRITICAL; VULN-005 là 8,1 HIGH; ba phát hiện còn lại cùng 7,5 HIGH. Các điểm bằng nhau không tạo ra ưu tiên khác biệt theo số điểm. Đây là điểm tại thời điểm phát hiện. Source hiện đã có JWT/authorization, ownership check, server-side pricing, state machine và validation hồi quy; cần integration test để đánh giá rủi ro còn lại thay vì dùng nguyên điểm ban đầu.

### 8.8. Phân tích riêng VULN-005: quyền sở hữu giỏ hàng

**Tôi đã làm gì và làm như thế nào?**

Ở lần review ban đầu, tôi lần theo dữ liệu từ route `user/{userId}` và `{cartId}/item/{itemId}` trong CartController đến CartService. Khi đó Service tìm giỏ theo userId do client đưa vào; khi sửa hoặc xóa, Service kiểm tra item thuộc cartId nhưng chưa đối chiếu chủ giỏ với danh tính người gọi.

Ví dụ về thiếu kiểm soát: một người biết cartId và itemId của giỏ khác có thể gửi yêu cầu thao tác lên các ID đó. Điều kiện `cartItem.CartId == cartId` chỉ xác nhận hai tài nguyên liên quan đúng với nhau; nó không xác nhận người gửi yêu cầu được phép truy cập chúng.

**Kiến thức sử dụng và kết quả**

Tôi áp dụng kiến thức kiểm soát truy cập mức đối tượng và CWE-639: quyền truy cập không được quyết định chỉ bằng khóa do người dùng cung cấp. Tôi ghi nhận nguy cơ đọc dữ liệu giỏ và sửa/xóa item của người khác, xếp HIGH theo công cụ Lab 3. Chưa thực hiện yêu cầu khai thác trên API đang chạy, nên ví dụ trên là kịch bản phân tích từ code, không phải log tấn công thực nghiệm.

Source hiện đã lấy identity từ claim JWT qua `ClaimsPrincipalExtensions`, đặt `[Authorize]` trên CartController và đối chiếu `cart.UserId` trước thao tác. Phần còn thiếu là integration test gửi token của hai người dùng khác nhau để chứng minh toàn pipeline trả 403 và không thay đổi database. Chỉ nhìn thấy guard trong code chưa thay thế cho bằng chứng chạy xuyên suốt.

### 8.9. Xây dựng mô hình C và kiểm chứng lỗi bộ nhớ

**Tôi đã làm gì?**

Tôi xây dựng năm chương trình C tương ứng với các ngữ cảnh Order, Payment, Cart, Product và User. Mỗi chương trình có một bản cố ý chứa lỗi và một bản áp dụng cách sửa. Tôi thêm Makefile để biên dịch nhất quán và script Python để chạy, phân loại kết quả, lưu log và kiểm tra các giá trị biên.

**Tôi làm như thế nào?**

Tôi xác định vùng nhớ nào thuộc quyền sở hữu của đối tượng, thời điểm vùng nhớ được tạo bằng `malloc`, thời điểm phải giải phóng bằng `free`, và mọi vị trí còn đọc hoặc ghi qua con trỏ. Sau đó tôi chọn một tình huống gần với nghiệp vụ của từng module:

1. Order cấp phát riêng struct cha và mảng OrderItem. Bản lỗi chỉ giải phóng struct cha nên mảng con trở thành vùng nhớ không còn con trỏ tham chiếu.
2. Payment có nhánh xử lý gateway thất bại và nhánh cleanup chung. Bản lỗi để cả hai nhánh giải phóng cùng transaction code.
3. Cart tháo một item khỏi danh sách liên kết. Bản lỗi giải phóng item trước rồi mới đọc ProductId để ghi log.
4. Product cấp phát buffer tên trên heap. Bản lỗi dùng `strcpy` mà không kiểm tra kích thước input.
5. User dùng mảng email cục bộ trên stack. Bản lỗi cũng dùng `strcpy` với input có thể dài hơn buffer.

Trong Makefile, bản lỗi được biên dịch bình thường, còn bản sửa có thêm `-DFIXED`. Chỉ thị tiền xử lý `#ifdef FIXED` đưa đoạn sửa vào executable fixed. Tôi bật `-g`, `-O0`, `-Wall`, `-Wextra`, `-fsanitize=address,undefined` và giữ frame pointer để log chỉ rõ dòng source. Leak detection được bật bằng `ASAN_OPTIONS=detect_leaks=1`.

Lệnh tái lập từ thư mục gốc repository:

```bash
cd security-analysis/c-memory
make
make test
```

Có thể chạy riêng từng cặp để thuyết trình:

```bash
ASAN_OPTIONS=detect_leaks=1 ./build/order_buggy
ASAN_OPTIONS=detect_leaks=1 ./build/order_fixed
./build/cart_buggy
./build/cart_fixed
./build/product_buggy "$(python3 -c 'print("A" * 100)')"
./build/product_fixed "$(python3 -c 'print("A" * 100)')"
```

**Tôi sử dụng kiến thức gì?**

Tôi áp dụng ownership và lifetime của vùng nhớ, phân biệt stack với heap, quy tắc một vùng nhớ chỉ được giải phóng đúng một lần, và yêu cầu không được dereference con trỏ sau `free`. Với chuỗi C, tôi tính cả byte `\0`: buffer 32 byte chỉ chứa tối đa 31 byte nội dung. Tôi cũng dùng phân tích giá trị biên và dynamic analysis; sanitizer chỉ phát hiện lỗi trên đường chạy đã thực hiện, không phải chứng minh tĩnh cho mọi đường chạy.

**Kết quả cụ thể**

| Ca kiểm chứng chính | Kết quả bản buggy | Kết quả bản fixed |
|---|---|---|
| Order | LeakSanitizer phát hiện memory leak, exit code khác 0 | Không có leak, exit code 0 |
| Payment | AddressSanitizer phát hiện double-free | Cleanup với con trỏ NULL kết thúc bình thường |
| Cart | AddressSanitizer phát hiện heap-use-after-free | Log giá trị đã sao chép trước `free` |
| Product | AddressSanitizer phát hiện heap-buffer-overflow | Input dài bị từ chối có kiểm soát |
| User | AddressSanitizer phát hiện stack-buffer-overflow | Input dài bị từ chối có kiểm soát |

Toàn bộ **22/22 test PASS**. Script không chấp nhận một crash bất kỳ là thành công: bản lỗi phải có đúng chuỗi chẩn đoán mong đợi. Bản fixed phải thoát mã 0, không chứa `Sanitizer` hoặc `runtime error`, đồng thời phải in đúng kết quả chấp nhận/từ chối đối với input biên.

**Cách thay đổi tham số khi demo**

Product và User nhận input từ dòng lệnh, nên có thể thay tên/email mà không sửa source:

```bash
./build/product_fixed "Com tam"
./build/user_fixed "student@example.com"
./build/product_fixed "$(python3 -c 'print("A" * 31)')"
./build/product_fixed "$(python3 -c 'print("A" * 32)')"
```

Muốn đổi dung lượng buffer thì sửa `NAME_CAPACITY` hoặc `EMAIL_CAPACITY`; ví dụ đổi 32 thành 64 thì biên hợp lệ lớn nhất là 63 byte và cần đổi tập độ dài trong `check_memory.py` thành các giá trị quanh 64. Với Order, có thể đổi `order->count` để thay số item và đổi số vòng lặp trong `main` để tăng số lần rò rỉ. Với Cart, có thể đổi `{101, 2, NULL}` để thay ProductId và quantity. Sau khi sửa source, chạy lại `make test`; Make sẽ biên dịch lại executable phụ thuộc vào file đã thay đổi.

**Lỗi môi trường và giới hạn**

LeakSanitizer có thể báo không hoạt động dưới `ptrace`, debugger hoặc một số sandbox. Khi đó tôi chạy test từ terminal thông thường ngoài debugger, thay vì tắt leak detection. Kết quả sanitizer phụ thuộc vào việc đường lỗi được thực thi; input ngắn vẫn có thể làm bản buggy chạy thành công. Mô hình C không kết nối database và không chứng minh lỗi tương ứng đang tồn tại trong C#.

### 8.10. Lỗi môi trường và trạng thái xử lý

| Vấn đề | Biểu hiện/nguyên nhân | Xử lý hoặc trạng thái hiện tại |
|---|---|---|
| Restore NuGet bị chặn | NU1301, socket tới api.nuget.org:443 bị từ chối trong môi trường hạn chế | Chạy lại với quyền truy cập được phê duyệt; tải được xUnit/Coverlet và chạy test thành công |
| Phiên bản dependency lịch sử | Từng có NU1603 khi yêu cầu Npgsql 8.0.7 nhưng resolve 8.0.8 | Project hiện pin 8.0.8; không còn trình bày warning cũ như lỗi hiện tại |
| Lab 1 thiếu AF | Không thể dùng checker gốc để kiểm tra trực tiếp liveness đã yêu cầu | Bổ sung fixed-point AF trong integration |
| Nhãn không lưu lịch sử | PAID cũ không phân biệt thanh toán hợp lệ và thanh toán sau hủy | Thêm PAID_AFTER_CANCELLED làm history monitor |
| Parser coverage sai chữ hoa/thường | `True` khác `true`, làm mất danh sách nhánh chưa phủ | Chuẩn hóa `.lower()`; nhận diện đúng dòng 101 |
| Tổng tiền sai được chấp nhận | Nhánh tolerance từng rỗng | Đã sửa validator và đổi test thành yêu cầu từ chối mismatch |
| Đơn hủy vẫn thanh toán được | PaymentService từng thiếu guard trạng thái nguồn | Đã thêm OrderStateMachine/guard và test `CancelledOrderCannotBePaid` |
| Quyền truy cập chưa đầy đủ | API trước đây thiếu authentication/ownership | Đã thêm JWT, `[Authorize]`, ownership check và integration test HTTP; lần xác minh 08/10 chưa chạy được test vì Docker không khả dụng |
| LeakSanitizer bị giới hạn bởi ptrace/sandbox | Báo `LeakSanitizer has encountered a fatal error` thay vì phân tích leak | Chạy bộ test từ terminal bình thường ngoài debugger/sandbox; xác nhận 22/22 PASS |
| Integration test bị skip | 5 test Testcontainers yêu cầu Docker nhưng Docker không khả dụng trong lần chạy 08/10/2026 | Unit test vẫn đạt 84/84; chưa ghi nhận integration PASS cho lần xác minh này |
| Secret trong lịch sử Git | Connection string ở commit cũ từng chứa mật khẩu PostgreSQL dù HEAD hiện để trống | Phải rotate mật khẩu và rewrite history trước khi public; chỉ xóa file hiện tại là chưa đủ |
| Phạm vi tài liệu học phần | `ATPM/` còn chứa slide, lab, binary và archive tham chiếu | Loại chúng khỏi phạm vi sản phẩm được đánh giá; không tuyên bố đã xóa khỏi working tree |

Tôi không ghi nhận các rủi ro dự kiến như race condition là lỗi đã tái hiện nếu chưa có kết quả kiểm thử đồng thời. SMT/Z3 đã có bằng chứng 7/7 trong phạm vi mô hình số học; TLC/BMC và fuzzing vẫn chưa có minh chứng hoàn thành. Phần dynamic analysis đã hoàn thành trong phạm vi năm lỗi bộ nhớ được chủ động đưa vào mô hình C.

### 8.11. Tổng hợp kiến thức và giá trị đạt được

Qua phần công việc này, tôi đã áp dụng CIA, trust boundary và STRIDE để threat modeling; Kripke, BFS và CTL để kiểm tra thiết kế; kiểm thử biên và phân lớp tương đương cho validator; coverage để xác định nhánh chưa chạy; CWE/CVSS-lite để ưu tiên phát hiện; ownership, lifetime, alignment, pattern scanning, cppcheck và sanitizer cho mô hình C; cùng SMT/QF_NIA để kiểm chứng ràng buộc số học và sinh counterexample Int32.

Kết quả quan trọng không chỉ là 84 unit test xUnit PASS, branch coverage InputValidator 96,87% hay 22/22 ca C PASS, mà là có minh chứng cụ thể cho những điều đã kiểm tra và giới hạn rõ ràng của kết luận. Tôi đã biết phân biệt bốn tình huống: **thiết kế thỏa thuộc tính**, **test ghi nhận đúng hành vi hiện tại**, **công cụ phát hiện đúng lỗi cố ý trong mô hình**, và **mã nghiệp vụ đã được sửa kèm kiểm thử hồi quy**. Cả bốn đã có kết quả trong phạm vi unit test/mô hình; integration HTTP/PostgreSQL đã có 5 test Testcontainers nhưng cần chạy khi Docker hoạt động để lưu kết quả, còn gateway thật vẫn là nhiệm vụ của giai đoạn tiếp theo.

## 9. Kịch bản lệnh chạy khi báo cáo tiến độ

### 9.1. Chuẩn bị trước buổi báo cáo

Chạy các lệnh sau từ thư mục gốc repository để xác nhận môi trường. Các phiên bản đã dùng khi cập nhật báo cáo gồm .NET SDK 10.0.112, Python 3.12.3, GCC 13.3.0, GNU Make 4.3, Cppcheck 2.13.0 và Z3 5.1.0.

```bash
cd ~/OrderPaymentMicroservice
dotnet --version
python3 --version
gcc --version | head -n 1
make --version | head -n 1
cppcheck --version
z3 -version
```

Restore và build trước buổi báo cáo để tránh dành thời gian tải package khi đang thuyết trình:

```bash
dotnet restore OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj
dotnet build OrderPaymentMicroservice/OrderPaymentMicroservice.csproj --no-restore
dotnet build OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj --no-restore
```

### 9.2. Kịch bản demo nhanh khoảng 5 phút

**Bước 1 — chạy toàn bộ test .NET:**

```bash
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj --no-restore
```

Kết quả mong đợi:

```text
Passed: 84, Failed: 0, Skipped: 0, Total: 84
```

Khi trình bày, giải thích rằng 34 ca thuộc InputValidator; các ca còn lại kiểm tra UserService, OrderService, PaymentService, hai state machine và các đường phục hồi/compensation.

**Bước 2 — chạy model checking và risk assessment:**

```bash
python3 security-analysis/run_analysis.py
```

Các dòng kết quả cần chỉ ra:

```text
Model kỳ vọng: P1, P2, P3, P4 PASS
Model BUGGY: P1 FAIL
Counterexample: CREATED → CANCELLED → PAID_AFTER_CANCELLED
Threat model: PASS (6 assets, 4 boundaries, 9 threats, 6/6 STRIDE)
Recovery model: 7 functional contracts, 15 states, 37 transitions, P1-P15 PASS
SMT verification: 7/7 PASS
InputValidator branch coverage: 96.87%
```

`P1 FAIL` ở model BUGGY là kết quả mong đợi vì công cụ đã phát hiện đúng lỗi cố ý. Không trình bày nó như lỗi chạy script.

**Bước 3 — chạy static analysis và layout Chương 2:**

```bash
make -C security-analysis/c-memory static-check
```

Kết quả mong đợi là `PASS static analysis`; mở
`c-memory/evidence/static-analysis.json` để chỉ ra 5 finding BUGGY, FIXED sạch
theo rule scanner, giới hạn false negative của cppcheck và số liệu alignment.

**Bước 4 — chạy toàn bộ mô phỏng lỗi bộ nhớ C:**

```bash
make -C security-analysis/c-memory test
```

Kết quả mong đợi là 22 dòng `PASS`: năm bản buggy được sanitizer phát hiện đúng lỗi, năm bản fixed không còn chẩn đoán sanitizer, và các ca còn lại kiểm tra biên chuỗi.

### 9.3. Demo chi tiết một cặp buggy/fixed

Nên chọn Cart vì log use-after-free thể hiện đầy đủ vị trí cấp phát, giải phóng và truy cập sai:

```bash
./security-analysis/c-memory/build/cart_buggy
./security-analysis/c-memory/build/cart_fixed
```

Bản buggy dự kiến dừng với:

```text
AddressSanitizer: heap-use-after-free
```

Bản fixed dự kiến in:

```text
Removed product 101
```

Có thể mở log đã lưu mà không chạy lại lỗi:

```bash
sed -n '1,35p' security-analysis/c-memory/results/cart_buggy.log
cat security-analysis/c-memory/results/summary.json
```

Nếu muốn demo kiểm tra giá trị biên của buffer 32 byte:

```bash
./security-analysis/c-memory/build/product_fixed "$(python3 -c 'print("A" * 31)')"
./security-analysis/c-memory/build/product_fixed "$(python3 -c 'print("A" * 32)')"
./security-analysis/c-memory/build/product_buggy "$(python3 -c 'print("A" * 100)')"
```

Kết quả cần giải thích: 31 byte được chấp nhận; 32 byte bị bản fixed từ chối vì cần thêm byte `\0`; 100 byte làm bản buggy phát sinh heap-buffer-overflow.

Demo memory leak riêng cần bật LeakSanitizer:

```bash
ASAN_OPTIONS=detect_leaks=1 ./security-analysis/c-memory/build/order_buggy
ASAN_OPTIONS=detect_leaks=1 ./security-analysis/c-memory/build/order_fixed
```

Nếu LeakSanitizer báo lỗi `ptrace` hoặc `fatal error`, chạy trực tiếp trong terminal thông thường ngoài debugger/sandbox. Không tắt leak detection rồi tuyên bố bản buggy đã được kiểm tra thành công.

### 9.4. Chạy lại coverage khi thầy yêu cầu

```bash
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore \
  /p:CollectCoverage=true \
  /p:CoverletOutput=../security-analysis/results/input-validator \
  /p:CoverletOutputFormat=cobertura \
  '/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator' \
  /p:Threshold=90 \
  /p:ThresholdType=branch
```

Sau đó chạy script tổng hợp và xem JSON:

```bash
python3 security-analysis/run_analysis.py
cat security-analysis/results/coverage_summary.json
```

Kết quả mong đợi: branch coverage 96,87%, 31/32 nhánh; nhánh còn thiếu là hướng true của `subtotal < 0` sau khi các guard đã bảo đảm quantity dương và price không âm.

### 9.5. Demo API và PostgreSQL — phần tùy chọn

Chỉ dùng phần này nếu PostgreSQL, connection string và JWT key đã được cấu hình trước. Không chạy `dotnet user-secrets list` trên màn hình trình chiếu vì lệnh đó có thể hiển thị secret.

Kiểm tra và khởi động PostgreSQL:

```bash
sudo systemctl start postgresql
pg_isready -h localhost -p 5432
```

Kết quả mong đợi:

```text
localhost:5432 - accepting connections
```

Khởi động API bằng HTTP để tránh vấn đề chứng chỉ HTTPS khi demo local:

```bash
dotnet run --project OrderPaymentMicroservice/OrderPaymentMicroservice.csproj --launch-profile http
```

Giữ terminal chạy API và mở terminal thứ hai:

```bash
curl -i http://localhost:5246/api/products
curl -i http://localhost:5246/api/orders/mine
curl -i http://localhost:5246/openapi/v1.json
```

Kỳ vọng: Products và tài liệu OpenAPI trả phản hồi thành công; `/api/orders/mine` không có JWT trả `401 Unauthorized`. Đây là demo tối thiểu cho thấy middleware authentication đang hoạt động, chưa thay thế integration test ownership 403 giữa hai tài khoản.

Dừng API bằng `Ctrl+C` trong terminal đang chạy `dotnet run`.

### 9.6. Thứ tự lệnh khuyến nghị khi đứng thuyết trình

Nếu thời gian ngắn, chỉ chạy bốn lệnh sau:

```bash
cd ~/OrderPaymentMicroservice
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj --no-restore
python3 security-analysis/run_analysis.py
make -C security-analysis/c-memory test
./security-analysis/c-memory/build/cart_buggy
```

Sau khi sanitizer dừng bản Cart buggy, chạy bản sửa:

```bash
./security-analysis/c-memory/build/cart_fixed
```

Trước buổi báo cáo nên chạy toàn bộ kịch bản một lần, giữ sẵn các file `results/*.json` và `c-memory/results/*.log`. Nếu một công cụ gặp lỗi môi trường, có thể mở bằng chứng đã lưu và giải thích lệnh đã dùng để sinh kết quả thay vì sửa môi trường trong lúc thuyết trình.
