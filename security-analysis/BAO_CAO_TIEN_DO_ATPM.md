# BÁO CÁO TIẾN ĐỘ BÀI TẬP LỚN AN TOÀN PHẦN MỀM

> Bản tổng hợp mới ngày 12/10/2026: [Báo cáo](report/BAO_CAO.md) và
> [Phụ lục A–G](report/PHU_LUC_A_G.md). 108 unit + 8 integration PASS;
> inventory SMT 3/3, payment SMT 7/7, recovery P1–P15 PASS.
> Số liệu 09/10/2026 bên dưới là lịch sử, không phải kết quả chạy mới.
> Quy trình đợt này không truy cập ATPM; JSON workbook nằm tại
> `security-analysis/order_payment_pipeline_spec.json`.


## Đề tài: Kiểm chứng an toàn hệ thống Order–Payment Microservice

**Ngày cập nhật:** 09/10/2026

**Phạm vi đánh giá:** mã nguồn, mô hình, test và bằng chứng hiện có trong repository

**Thư mục thực hiện lệnh:** thư mục gốc `OrderPaymentMicroservice`

---

## 1. Mục tiêu, phạm vi và cách đánh giá tiến độ

Đề tài sử dụng hệ thống quản lý đơn hàng và thanh toán để áp dụng tuần tự kiến
thức của bốn chương:

1. Chương 1: mô hình hệ thống, threat model, Kripke và thuộc tính an toàn.
2. Chương 2: lỗi bộ nhớ, ownership, lifetime và biểu diễn dữ liệu trong C.
3. Chương 3: phân tích tĩnh, đánh giá rủi ro và kiểm chứng hình thức bằng SMT.
4. Chương 4: kiểm thử động, coverage, sanitizer, integration test và fuzzing.

Quy trình chung của đề tài là:

```text
Yêu cầu/bất biến
    → mô hình hoặc ca kiểm thử
    → chạy công cụ
    → thu bằng chứng/counterexample
    → sửa thiết kế hoặc mã nguồn
    → kiểm chứng và kiểm thử lại
```

Báo cáo phân biệt bốn mức bằng chứng:

- **Model PASS:** mô hình hữu hạn đã khai báo thỏa thuộc tính; không tự động chứng
  minh implementation C# đúng.
- **Test PASS:** mã đã chạy đúng trên các ca test đã khai báo; không chứng minh mọi
  input hoặc mọi môi trường đều an toàn.
- **Mutation/BUGGY detected:** công cụ tìm được lỗi cố ý đưa vào; đây là kiểm tra
  năng lực phát hiện lỗi của bộ kiểm chứng.
- **Runtime/E2E PASS:** API, PostgreSQL và các thành phần tích hợp thực sự chạy
  cùng nhau trong test host; môi trường không có Docker dùng cluster PostgreSQL
  cục bộ tạm thời.

### 1.1. Tổng hợp tiến độ hiện tại

| Chương | Sản phẩm chính | Kết quả hiện tại | Trạng thái |
|---|---|---|---|
| Chương 1 | Threat model, Kripke cơ sở, Kripke phục hồi, model BUGGY | Threat model PASS; model cơ sở 4/4; model phục hồi 15/15; 6/6 mutation bị phát hiện | Hoàn thành trong phạm vi mô hình |
| Chương 2 | Năm module C BUGGY/FIXED, bảng ownership, layout và sanitizer | Static rule phân biệt 5/5 lỗi; ASan/UBSan/LSan 22/22 ca đúng kỳ vọng | Hoàn thành trong phạm vi mô phỏng C |
| Chương 3 | Dataset rủi ro, static analysis, SMT/Z3, TLA+/TLC | 5 finding được xếp hạng; static analysis PASS; Z3 7/7; TLC duyệt 20 state phân biệt và không lỗi | Hoàn thành trong phạm vi model đã khai báo |
| Chương 4 | xUnit, branch coverage, sanitizer, integration test, fuzzing | 87/87 unit; 5/5 E2E; 3/3 fuzz; branch fuzz target 99,07%; sanitizer 22/22 | Hoàn thành trong phạm vi test target hiện tại |

Không gán một phần trăm hoàn thành duy nhất vì chưa có rubric nghiệm thu tổng thể.
Mỗi kết luận bên dưới đều gắn với artifact và lệnh tái lập cụ thể.

---

# CHƯƠNG 1. MÔ HÌNH HỆ THỐNG, THREAT MODEL VÀ KRIPKE

## 2. Mục tiêu của Chương 1

Chương 1 chuyển yêu cầu nghiệp vụ Order–Payment thành mô hình có thể kiểm tra.
Các câu hỏi chính là:

- Tài sản nào cần bảo vệ, dữ liệu đi qua ranh giới tin cậy nào?
- Trạng thái nào của Order và Payment được phép tồn tại?
- Từ một trạng thái, event nào được phép xảy ra và phải đi đến đâu?
- Có đường đi nào hồi sinh đơn đã hủy, thanh toán sai tiền hoặc làm mất dấu vết
  đã thu tiền không?
- Mọi đường tiến triển có kết thúc hay có thể retry vô hạn?

## 3. Threat model đã xây dựng

Threat model áp dụng chuỗi:

```text
Asset → Threat → Vulnerability → Attack → Risk → Control → Evidence
```

Artifact chuẩn là `security-analysis/threat_model.json`; bản diễn giải nằm tại
`security-analysis/THREAT_MODEL_CHUONG1.md`.

### 3.1. Kết quả threat model

- 6 asset: danh tính/credential, Order/Cart, Payment, Product/Inventory,
  secret cấu hình và audit trail.
- 4 trust boundary: Internet–API, process–PostgreSQL, service–payment gateway và
  host config–process.
- 9 threat, phủ đủ Confidentiality, Integrity, Availability và 6/6 STRIDE.
- Script kiểm tra ID, tham chiếu, công thức điểm rủi ro, coverage STRIDE/CIA và
  liên kết từ threat sang control/evidence.

Kết quả hiện tại là **PASS**, nhưng PASS chỉ có nghĩa file threat model tự nhất
quán. Ba nguy cơ T3 (audit/repudiation), T5 (DoS/rate limiting), T8 (xác thực
gateway thật) còn mở; T9 (secret) mới được giảm thiểu một phần.

### 3.2. Lệnh chạy threat model

```bash
python3 security-analysis/verify_threat_model.py
```

Kết quả mong đợi:

```text
Threat model: PASS (6 assets, 4 boundaries, 9 threats, 6/6 STRIDE)
```

Bằng chứng được ghi vào
`security-analysis/results/threat_model_results.json`.

## 4. Kripke cơ sở và những hạn chế ban đầu

Model đầu tiên ở `security-analysis/order_state_model.json` mô tả cấu trúc
Kripke `M = (S, S0, R, L)`:

- `S`: 8 trạng thái Order.
- `S0`: `{CREATED}`.
- `R`: 14 chuyển tiếp nghiệp vụ và self-loop terminal.
- `L`: các nhãn như `paid`, `payment_success`, `payment_pending`, `terminal` và
  `invalid_transition`.

Bốn thuộc tính ban đầu:

| ID | Công thức/ý nghĩa | Kết quả |
|---|---|---|
| P1 | `AG NOT invalid_transition`: không bỏ bước, quay ngược hoặc hồi sinh terminal | PASS |
| P2 | `AG AF terminal`: từ mọi trạng thái reachable, mọi đường cuối cùng đến terminal | PASS |
| P3 | `AG(paid → payment_success)` | PASS |
| P4 | `AG(payment_pending → NOT payment_success)` | PASS |

Model cơ sở giúp kiểm tra happy path nhưng có ba giới hạn:

1. Chỉ nhìn nhãn state thì khó phát hiện lỗi phụ thuộc lịch sử, ví dụ PAID sau
   khi đã CANCELLED.
2. Chưa mô tả Payment `PROCESSING/UNKNOWN`, retry, reconciliation, refund và
   inventory compensation.
3. Chưa kiểm tra quan hệ giữa event và trách nhiệm của từng chức năng; một graph
   có thể PASS vài mệnh đề nhưng vẫn thiếu event hoặc gán sai state đích.

## 5. Các cải tiến quan trọng của mô hình Kripke

Đây là phần thay đổi lớn nhất của Chương 1. Mô hình không chỉ được thêm state mà
được cải tiến cả về ngữ nghĩa, thuộc tính, phản ví dụ và khả năng truy vết.

### 5.1. Bổ sung `AG AF terminal` đúng nghĩa

Checker Lab 1 chỉ hỗ trợ trực tiếp `AG_NOT`, `AG_IMPLIES` và `EF`. `EF terminal`
chỉ chứng minh *tồn tại* một đường kết thúc, không loại trừ đường khác chờ vô
hạn. `run_analysis.py` đã bổ sung phép tính điểm cố định nhỏ nhất cho
`AF terminal`:

1. Khởi tạo `good` bằng các state có nhãn `terminal`.
2. Thêm state nếu tất cả successor của nó đã thuộc `good`.
3. Lặp đến khi không còn state mới.
4. Yêu cầu mọi state reachable đều thuộc `good`.

Nhờ vậy P2 kiểm tra `AG AF terminal`, mạnh hơn kiểm tra reachability bằng `EF`.

### 5.2. Dùng history monitor để bắt lỗi hồi sinh terminal

Nếu chỉ thêm cạnh `CANCELLED → PAID`, checker dựa trên nhãn state có thể không
biết PAID đến từ lịch sử bất hợp lệ. Model BUGGY tạo state giám sát
`PAID_AFTER_CANCELLED`, gắn nhãn `invalid_transition` nhưng vẫn mang ngữ nghĩa
nghiệp vụ PAID.

Counterexample thu được:

```text
CREATED → CANCELLED → PAID_AFTER_CANCELLED
```

Đây là attack trace của T7/VULN-003 và chứng minh P1 thực sự phát hiện được lỗi
đã mô hình hóa, không chỉ PASS trên model đúng.

### 5.3. Mở rộng từ Order đơn lẻ sang aggregate Order–Payment–Inventory

Model phục hồi `order_payment_recovery_model.json` có:

- 15 trạng thái;
- 37 chuyển tiếp;
- 7 hợp đồng chức năng F1–F7;
- 15 thuộc tính P1–P15;
- mô tả sự cố, detection, solution và next state cho từng state.

Các state thiết kế bổ sung gồm `PAYMENT_PROCESSING`, `PAYMENT_RETRY`,
`PAYMENT_PROCESSING_FINAL`, `CONFIRMATION_RETRY`, `REFUND_PENDING`, `REFUNDED`
và `PRE_PAYMENT_REJECTED`.

`PRE_PAYMENT_REJECTED` được tách khỏi `FAILED`: trường hợp đầu chưa tạo Payment,
còn `FAILED` chỉ được dùng khi gateway trả decline cuối cùng. Việc tách này tránh
gắn sai metadata `payment_status=FAILED` cho một giao dịch chưa tồn tại.

### 5.4. Biến retry mô tả bằng lời thành bounded retry trong graph

Phiên bản đầu cho phép vòng:

```text
PAYMENT_PROCESSING → PAYMENT_RETRY → PAYMENT_PROCESSING → ...
```

P10 phát hiện đường tiến triển có thể không bao giờ tới terminal. Model được sửa
bằng state `PAYMENT_PROCESSING_FINAL`; retry cuối phải reconcile ra kết quả có
thẩm quyền hoặc giữ trạng thái UNKNOWN theo policy, không còn vòng retry vô hạn
trong graph. P10 xét các cạnh tiến triển sau khi loại riêng self-loop `stutter`
và nêu rõ giả định weak fairness.

### 5.5. Thêm hợp đồng chức năng độc lập với graph

Bảy hợp đồng F1–F7 bao phủ:

1. tạo và lưu Order;
2. quyết định payment hoặc cancel;
3. thực thi/reconcile payment;
4. xác nhận hoặc compensation;
5. hoàn tất hoặc compensation;
6. refund;
7. bảo vệ terminal state.

Mỗi hợp đồng khai báo state nguồn, tiền điều kiện, event, state đích và guarantee.
P11 so khớp từng khóa `(state,event)` giữa hợp đồng và graph. Vì vậy checker phát
hiện được cạnh thiếu, cạnh thừa, event thuộc nhiều chức năng hoặc state đích sai.

### 5.6. Mở rộng thuộc tính từ P1–P4 lên P1–P15

| Nhóm | Thuộc tính chính |
|---|---|
| Safety/consistency | Không unsafe; paid/completed phải có payment evidence; cancelled không có successful charge; pending chưa paid |
| Resource/concurrency | Payment và cancellation không cùng sở hữu critical resource |
| Completeness | Mọi state reachable; mọi incident có detection, solution, next state |
| Liveness | Mọi đường tiến triển cuối cùng đến terminal dưới giả định đã nêu |
| Contract | Mọi transition thuộc đúng một chức năng và đúng outcome |
| Terminal | Terminal chỉ được no-op/reject và self-loop |
| Compensation | Sau successful payment không được đi FAILED/CANCELLED mà làm mất bằng chứng charge; phải giữ paid evidence hoặc refund |
| UNKNOWN safety | Gateway UNKNOWN không được xem là terminal, success hoặc giải phóng inventory |
| Determinism | Một `(state,event)` chỉ có một quyết định |

Kết quả model đúng là **15/15 PASS**.

### 5.7. Thêm mutation testing cho thuộc tính mô hình

Sáu mutation được chèn độc lập:

- thanh toán Order đã CANCELLED;
- chấp nhận sai amount;
- tạo hai Payment SUCCESS;
- payment và cancellation cùng giữ critical resource;
- PAID đi thẳng FAILED, bỏ qua refund;
- coi UNKNOWN là thất bại cuối và giải phóng inventory.

Cả **6/6 mutation đều bị phát hiện** và có thuộc tính/witness tương ứng. Điều này
giảm nguy cơ các thuộc tính PASS hình thức nhưng không đủ mạnh để bắt lỗi mục tiêu.

## 6. Quy trình kiểm chứng Chương 1

```text
Đọc WorkflowAnalysis + source state machine
    → xác định state/event/invariant
    → sinh/soát JSON model
    → kiểm tra reachable states
    → chạy P1–P15
    → chèn mutation
    → yêu cầu mutation tạo counterexample
    → lưu JSON và ma trận CSV
    → đối chiếu lại unit/integration test của implementation
```

### 6.1. Lệnh chạy toàn bộ Kripke và risk pipeline

```bash
python3 security-analysis/run_analysis.py
```

Lệnh này chạy model cơ sở, model BUGGY, risk assessment, threat model, recovery
model, SMT và đọc coverage đã lưu. Các dấu hiệu thành công cần kiểm tra:

```text
Model cơ sở: P1, P2, P3, P4 PASS
Model BUGGY: P1 FAIL với counterexample mong đợi
Recovery model: 15 states, 37 transitions, P1-P15 PASS
6 negative mutation: DETECTED
```

Chạy riêng recovery model:

```bash
python3 security-analysis/verify_recovery_model.py
```

Artifact kết quả:

- `results/order_state_model.results.json`;
- `results/order_state_model_buggy.results.json`;
- `results/order_payment_recovery_model.results.json`;
- `results/order_payment_state_response_matrix.csv`.

## 7. Giới hạn và việc còn lại của Chương 1

- Kripke là model thiết kế viết tay, chưa trích xuất tự động từ C#.
- `payment_success` là proposition; P3 không chứng minh response đến từ gateway
  thật đã được xác thực chữ ký.
- Identity/role không nằm trong state space nên Kripke hiện tại không chứng minh
  Spoofing, IDOR hay Elevation of Privilege.
- Đặc tả TLA+ đã được TLC duyệt toàn bộ state space hữu hạn hiện tại; kết quả này
  vẫn phụ thuộc abstraction, fairness và bound của model.
- Integration PostgreSQL đã kiểm tra payment–cancel, idempotency và lifecycle;
  chưa kiểm chứng gateway thật hoặc lỗi mạng/khởi động lại process.

---

# CHƯƠNG 2. AN TOÀN BỘ NHỚ VÀ BIỂU DIỄN DỮ LIỆU TRONG C

## 8. Mục tiêu và phạm vi Chương 2

Ứng dụng chính viết bằng C#, vì vậy nhóm xây dựng năm module C học tập theo ngữ
cảnh nghiệp vụ. Đây không phải bản dịch của ứng dụng và kết quả không được suy
rộng thành “C# đã an toàn bộ nhớ”. Mục tiêu là minh họa ownership, lifetime,
allocation/deallocation, copy có giới hạn, alignment và padding.

| Module | Lỗi BUGGY | Cách sửa FIXED |
|---|---|---|
| Order | Leak vùng nhớ `items` do chỉ free struct cha | Xác định Order sở hữu `items`, free vùng con trước vùng cha |
| Payment | Double-free con trỏ `transaction_code` | Một owner, free đúng một lần và vô hiệu hóa con trỏ sau giải phóng |
| Cart | Use-after-free sau khi xóa item | Không dereference alias sau free; lưu dữ liệu cần thiết trước khi giải phóng |
| Product | Heap-buffer-overflow do copy chuỗi không giới hạn | Kiểm tra độ dài, cấp phát đủ `length + 1`, copy kèm terminator |
| User | Stack-buffer-overflow do `strcpy` vào buffer cố định | Từ chối chuỗi không vừa buffer và copy có bound |

## 9. Ownership, lifetime và layout

Mỗi vùng cấp phát được xác định owner và điểm kết thúc lifetime. Bản FIXED phải
giữ ba quy tắc:

1. một vùng nhớ có owner chịu trách nhiệm release;
2. không dùng pointer sau khi lifetime kết thúc;
3. mọi phép copy phải tính cả byte kết thúc `\0`.

`tests/layout_probe.c` include trực tiếp khai báo struct từ năm module. Trên ABI
x86-64 đã đo:

| Kiểu | `sizeof` | `_Alignof` | Nhận xét |
|---|---:|---:|---|
| `CartItem` | 16 | 8 | Hai `int` trước pointer, không có lỗ giữa field |
| `OrderItem` | 16 | 8 | Hai `int` ghép đủ 8 byte trước `long` |
| `Order` | 24 | 8 | Có padding trước `size_t` |
| `Payment` | 16 | 8 | Có padding trước pointer |
| `Product` | 16 | 8 | Có padding trước pointer |

Không dùng `packed` vì có thể tạo truy cập pointer/`long` lệch alignment. Các số
đo phụ thuộc ABI và compiler, không phải hằng số cho mọi nền tảng.

## 10. Quy trình kiểm chứng Chương 2

### 10.1. Phân tích tĩnh

```bash
make -C security-analysis/c-memory static-check
```

Pipeline chạy:

- GCC `-fsyntax-only -Wall -Wextra -Wpedantic`;
- Cppcheck cho riêng nhánh BUGGY và FIXED;
- project-specific rule scanner cho allocation/free, double-free, use-after-free
  và copy không giới hạn;
- layout probe đo size/alignment/offset thực tế.

Kết quả hiện tại: `PASS static analysis`. Scanner bắt đủ năm lỗi BUGGY và không
còn finding tương ứng trên FIXED. Cppcheck chỉ bắt trực tiếp buffer overflow ở
User và bỏ sót một số lỗi ownership; false negative này được ghi nhận thay vì
dùng “không có warning” làm bằng chứng an toàn.

### 10.2. Phân tích động bằng sanitizer

```bash
make -C security-analysis/c-memory test
```

Compiler flags chính:

```text
-fsanitize=address,undefined
-fno-sanitize-recover=all
-fno-omit-frame-pointer
```

Bộ test có 22 ca:

- 5 bản BUGGY phải bị sanitizer phát hiện đúng loại lỗi;
- 5 bản FIXED phải thoát thành công, không có sanitizer diagnostic;
- các ca độ dài 0, 1, 31, 32, 128 kiểm tra biên buffer Product/User;
- input ngắn trên một số bản BUGGY chứng minh lỗi động phụ thuộc đường chạy/input.

Kết quả chạy lại ngày 09/10/2026: **22/22 PASS**. “PASS order_buggy” nghĩa là harness
đã bắt được leak mong đợi, không có nghĩa bản BUGGY an toàn.

Nếu LeakSanitizer báo lỗi `ptrace`/sandbox, phải chạy lệnh trong terminal thường
ngoài debugger. Không tắt leak detection rồi coi đó là kết quả đạt.

### 10.3. Demo trực tiếp Cart BUGGY/FIXED

```bash
make -C security-analysis/c-memory all
./security-analysis/c-memory/build/cart_buggy
./security-analysis/c-memory/build/cart_fixed
```

Bản BUGGY dự kiến dừng với `AddressSanitizer: heap-use-after-free`; bản FIXED in
`Removed product 101` và thoát thành công.

### 10.4. Bằng chứng Chương 2

- `security-analysis/c-memory/evidence/static-analysis.json`;
- `security-analysis/c-memory/evidence/dynamic-analysis.json`;
- log cục bộ trong `security-analysis/c-memory/results/` sau khi chạy test.

## 11. Giới hạn Chương 2

- Rule scanner chỉ hiểu pattern của năm module, không phải C parser sound.
- 22 ca không bao phủ mọi input, alias phức tạp, race hoặc mọi nhánh malloc fail.
- Bộ C không chạy HTTP, PostgreSQL hoặc payment gateway.
- Kết quả sanitizer không thay thế code review và static analysis; ngược lại,
  static analysis sạch cũng không bảo đảm không có lỗi runtime.

---

# CHƯƠNG 3. PHÂN TÍCH TĨNH, ĐÁNH GIÁ RỦI RO VÀ KIỂM CHỨNG SMT

## 12. Đánh giá lỗ hổng và ưu tiên khắc phục

`vulnerabilities.json` lưu năm phát hiện theo cấu trúc dataset Lab 3. Điểm là
CVSS-lite giáo dục, không phải CVSS chính thức.

| Ưu tiên | ID | Phát hiện ban đầu | Điểm | Trạng thái source hiện tại |
|---:|---|---|---:|---|
| 1 | VULN-001 | Order API thiếu authentication/authorization | 9,1 CRITICAL | Đã có JWT/role/claim và E2E 401/403 PASS |
| 2 | VULN-005 | Truy cập/sửa giỏ hàng chéo user | 8,1 HIGH | Ownership và E2E truy cập chéo PASS |
| 3 | VULN-002 | Client quyết định giá/tổng tiền | 7,5 HIGH | Đã lấy giá server và tính lại total |
| 4 | VULN-003 | CANCELLED có thể bị chuyển PAID | 7,5 HIGH | Đã có state guard và regression test |
| 5 | VULN-004 | Validator chấp nhận total mismatch | 7,5 HIGH | Đã reject mismatch và giá dưới min |

Lệnh tính lại bảng ưu tiên nằm trong pipeline:

```bash
python3 security-analysis/run_analysis.py
```

Kết quả lưu tại `security-analysis/results/risk_assessment_results.json`.

## 13. Phân tích tĩnh source và giới hạn công cụ

Phân tích tĩnh được thực hiện theo hai lớp:

- review source C#/C để tìm trust boundary, guard, transition, ownership và input
  không tin cậy;
- tool-assisted analysis trên C bằng GCC, Cppcheck và rule scanner.

Một kết luận quan trọng là không được đồng nhất “tool không báo lỗi” với “code
an toàn”. Cppcheck bỏ sót leak Order, double-free Payment, use-after-free Cart và
heap overflow Product trong cấu hình hiện tại; sanitizer và project rule lại bắt
được. Vì vậy báo cáo giữ cả true positive lẫn false negative của từng công cụ.

## 14. Kiểm chứng bất biến tiền bằng SMT/Z3

Đặc tả `OrderPaymentMicroservice/Security/PaymentVerification.z3` dùng integer
cent và logic `QF_NIA`. Phiên bản trước từng khai báo `QF_LIA`, không phù hợp vì
`Quantity × UnitPriceCents` là phép nhân hai biến tượng trưng. Z3 có thể báo lỗi
assertion phi tuyến; các dòng SAT xuất hiện sau lỗi đó không được xem là bằng
chứng. Phiên bản hiện tại yêu cầu process exit code 0 và đối chiếu từng marker.

### 14.1. Bảy truy vấn SMT

| Truy vấn | Kết quả mong đợi/hiện tại | Ý nghĩa |
|---|---|---|
| Order PAID hợp lệ | SAT | Tồn tại model thỏa toàn bộ ràng buộc |
| Total khác tổng item | UNSAT | Invariant loại total mismatch |
| PAID nhưng thiếu payment | UNSAT | PAID cần đủ payment evidence |
| Payment âm | UNSAT | Miền tiền loại giá trị âm |
| Payment vượt total | UNSAT | Không cho overpayment trong model |
| Có Int32 subtotal overflow | SAT | Sinh counterexample `10000 × 99999999` |
| Total bound nhưng subtotal vượt max | UNSAT | Bound đơn hàng loại trường hợp quá lớn |

Kết quả trên Z3 5.1.0: **7/7 đúng kỳ vọng**.

### 14.2. Lệnh chạy SMT

```bash
python3 security-analysis/verify_payment_smt.py
```

Hoặc chạy đặc tả trực tiếp:

```bash
z3 OrderPaymentMicroservice/Security/PaymentVerification.z3
```

Runner được khuyến nghị vì nó kiểm exit code, đủ marker, thứ tự SAT/UNSAT và lưu
`security-analysis/results/payment_verification.results.json`.

## 15. Quy trình kiểm chứng Chương 3

```text
Static review/rule scan
    → ghi finding và vị trí
    → ánh xạ CWE + điều kiện khai thác + tác động
    → tính điểm ưu tiên
    → viết invariant toán học
    → tạo truy vấn SAT (witness) và UNSAT (proof trong model)
    → chạy Z3 và đối chiếu expected result
    → sửa source/model
    → chạy regression test
```

## 16. Giới hạn và phần còn thiếu Chương 3

- SMT dùng mathematical integers; không tự chứng minh EF Core, decimal runtime,
  database hoặc gateway.
- SAT là witness tồn tại; UNSAT chỉ đúng trong tập assumption đã mã hóa.
- TLA+ đã được chạy bằng TLC với `SPECIFICATION Spec`: 35 state sinh, 20 state
  phân biệt, độ sâu 9 và không phát hiện lỗi safety/liveness. Log, hash model và
  kết quả máy đọc được nằm trong `security-analysis/results/tlc/`.
- Chưa có adapter gateway thật để kiểm chứng chữ ký callback, nonce, merchant,
  order, amount và replay protection.

---

# CHƯƠNG 4. KIỂM THỬ ĐỘNG, COVERAGE VÀ FUZZING

## 17. Unit test và regression test

Project `OrderPaymentMicroservice.Tests` có **87 test**, gồm:

- 34 trường hợp InputValidator;
- OrderService và PaymentService;
- OrderStateMachine và PaymentStateMachine;
- UserService/password hashing/JWT;
- timeout, decline, UNKNOWN, bounded retry và crash reconciliation;
- idempotency, refund, inventory compensation và race payment–cancel ở mức unit.
- 3 campaign fuzz xác định cho seed corpus, validator và transition Order/Payment.

### 17.1. Lệnh chạy unit test

```bash
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore \
  --verbosity minimal
```

Kết quả chạy lại ngày 09/10/2026:

```text
Failed: 0, Passed: 87, Skipped: 0, Total: 87
```

## 18. Thiết kế test InputValidator

Các lớp tương đương và biên đã kiểm tra:

- request/item/list null, list rỗng;
- CustomerId/ProductId không dương;
- total âm, bằng 0, dưới min, trên max;
- quantity dưới/trên biên;
- price âm, bằng 0, dưới min, trên max;
- giá trị biên hợp lệ và nhiều item;
- sai lệch total trong/ngoài tolerance `0,01` theo cả hai hướng;
- `decimal.MaxValue` và phép nhân lớn nhất sau validation.

Test regression xác nhận giá `0`/`0,001` và total mismatch lớn hơn `0,01` bị từ
chối, thay vì ghi nhận hành vi không an toàn cũ như kết quả mong muốn.

## 19. Đo branch coverage

Lệnh tái tạo coverage:

```bash
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore \
  /p:CollectCoverage=true \
  /p:CoverletOutput=../security-analysis/results/input-validator \
  /p:CoverletOutputFormat=cobertura \
  '/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator' \
  /p:Threshold=90 \
  /p:ThresholdType=branch

python3 security-analysis/run_analysis.py
```

Kết quả đã lưu:

| Phạm vi | Branch coverage | Line coverage |
|---|---:|---:|
| Toàn lớp InputValidator | 31/32 = 96,87% | 84,31% |
| `ValidateCreateOrderRequest()` | 100% | 100% |
| `ValidateOrderItem()` | 92,85% | Xem Cobertura |

Nhánh chưa phủ là hướng true của `subtotal < 0`. Sau guard, `Quantity` thuộc
1..10000 và `UnitPrice` thuộc 0,01..999999,99 nên subtotal không thể âm. Không
dùng reflection hoặc sửa validator chỉ để ép một đường bất khả thi và đạt 100%.

Coverage là thước đo code đã chạy, không phải chứng minh logic đúng. Vì vậy nó
được đọc cùng assertion của test và kết quả model/SMT.

## 20. Dynamic analysis bằng sanitizer

Phần sanitizer của Chương 2 đồng thời là bằng chứng dynamic analysis Chương 4:

```bash
make -C security-analysis/c-memory test
```

Kết quả: **22/22 ca đúng kỳ vọng**. ASan bắt overflow/UAF/double-free, LSan bắt
leak và UBSan kiểm undefined behavior trong phạm vi executable đã chạy.

## 21. Integration test HTTP/PostgreSQL

Project `OrderPaymentMicroservice.IntegrationTests` có 5 test PostgreSQL:

1. register → login → cart → order → payment và idempotent replay;
2. authentication và ownership trả 401/403 đúng;
3. hai payment request cạnh tranh chỉ tạo một charge record;
4. payment và cancellation cạnh tranh chỉ có một bên thắng;
5. Admin confirm và complete order, inventory chuyển `CONSUMED`.

Lệnh chạy:

```bash
dotnet test OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj \
  --no-restore \
  --verbosity normal
```

Fixture ưu tiên Testcontainers khi Docker khả dụng. Nếu Docker không có, fixture
tạo cluster tạm bằng PostgreSQL binaries của người dùng, chọn port riêng, apply
migration/seed rồi dừng và xóa cluster sau test. Kết quả ngày 09/10/2026 là
**5 PASS, 0 FAIL, 0 SKIP**; các ca này được báo riêng, không cộng vào 87 unit test.

## 22. Fuzzing

Fuzzing application được triển khai dưới dạng campaign xác định, tái lập được:

- seed corpus JSON chứa request hợp lệ và các biên nguy hiểm;
- 25.000 iteration cho `InputValidator`/`PaymentValidator`;
- 25.000 transition ngẫu nhiên cho Order/Payment, đối chiếu với bảng chuyển độc lập;
- seed cố định `20261009`, timeout 120 giây và policy coi crash/test failure là lỗi;
- Coverlet đo target được chọn: line 90,99%, branch 99,07%, method 81,81%.

Lệnh chạy và artifact:

```bash
python3 security-analysis/run_fuzzing.py
```

Kết quả **3/3 PASS**, lưu tại `results/application_fuzzing.results.json` và
`results/application-fuzzing-coverage.cobertura.xml`. Đây là deterministic
property fuzzing, không phải chứng minh mọi input hoặc fuzzing liên tục nhiều giờ.

## 23. Quy trình kiểm thử Chương 4

```text
Xác định requirement/invariant
    → phân lớp tương đương + giá trị biên
    → unit test
    → đo branch coverage
    → bổ sung test cho nhánh có ý nghĩa
    → sanitizer/fuzzing
    → integration test HTTP + PostgreSQL
    → lưu log và biến lỗi thành regression test
```

## 24. Giới hạn và phần còn lại Chương 4

- Integration hiện chạy trên test host/PostgreSQL cục bộ, chưa bao phủ restart
  process, partition mạng hoặc gateway sandbox thật.
- Fuzzing hiện giới hạn ở validator và pure state transition; chưa fuzz HTTP
  parser, EF/database hoặc background recovery worker.
- Coverage 99,07% chỉ áp dụng target fuzz đã include, không phải toàn ứng dụng.
- Chưa có transcript demo API trên database sạch.
- FakePaymentGateway không thay thế sandbox của nhà cung cấp thật.

---

# QUY TRÌNH CHẠY TOÀN BỘ VÀ KỊCH BẢN BÁO CÁO

## 25. Chuẩn bị môi trường

Các phiên bản dùng trong lần xác minh 09/10/2026:

```text
.NET SDK 10.0.112
Python 3.12.3
GCC 13.3.0
GNU Make 4.3
Cppcheck 2.13.0
Z3 5.1.0
```

Kiểm tra:

```bash
dotnet --version
python3 --version
gcc --version | head -n 1
make --version | head -n 1
cppcheck --version
z3 -version
```

Restore/build trước buổi demo:

```bash
dotnet restore OrderPaymentMicroservice.slnx
dotnet build OrderPaymentMicroservice.slnx --no-restore
make -C security-analysis/c-memory all
```

## 26. Pipeline tái lập đầy đủ

Chạy tuần tự từ thư mục gốc:

```bash
# Chương 1: threat model và Kripke
python3 security-analysis/verify_threat_model.py
python3 security-analysis/verify_recovery_model.py

# Chương 2 và phần dynamic C
make -C security-analysis/c-memory static-check
make -C security-analysis/c-memory test

# Chương 3: SMT
python3 security-analysis/verify_payment_smt.py
python3 security-analysis/run_tlc.py --jar /path/to/tla2tools.jar

# Chương 4: unit test và coverage
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore --verbosity minimal

dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore \
  /p:CollectCoverage=true \
  /p:CoverletOutput=../security-analysis/results/input-validator \
  /p:CoverletOutputFormat=cobertura \
  '/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator' \
  /p:Threshold=90 \
  /p:ThresholdType=branch

python3 security-analysis/run_fuzzing.py

# HTTP/PostgreSQL integration (Docker hoặc local PostgreSQL fallback)
dotnet test OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj \
  --no-restore --verbosity normal

# Sinh và kiểm tra đặc tả theo đúng schema ATPM của giảng viên
python3 security-analysis/create_order_payment_spec.py
python3 security-analysis/verify_teacher_spec.py
pytest -q ATPM/atpm_project/python/tests/test_order_payment_spec.py

# Tổng hợp model, risk, SMT và coverage
python3 security-analysis/run_analysis.py
```

## 27. Kịch bản demo khoảng 7–10 phút

### Bước 1 — Kripke đúng và phản ví dụ

```bash
python3 security-analysis/run_analysis.py
```

Trình bày ba điểm: model đúng 4/4, model BUGGY tạo trace
`CREATED → CANCELLED → PAID_AFTER_CANCELLED`, model phục hồi 15/15 và bắt 6/6
mutation. Nhấn mạnh cải tiến AF, history monitor, bounded retry và functional
contracts.

### Bước 2 — test implementation

```bash
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore --verbosity minimal
```

Chỉ ra 87 PASS và giải thích test đối chiếu implementation, không thay cho proof
trên model.

### Bước 3 — static và dynamic memory analysis

```bash
make -C security-analysis/c-memory static-check
make -C security-analysis/c-memory test
```

Mở `evidence/static-analysis.json` để chỉ finding và layout; mở
`evidence/dynamic-analysis.json` để chỉ expected diagnostic của 22 ca.

### Bước 4 — demo một lỗi trực quan

```bash
./security-analysis/c-memory/build/cart_buggy
./security-analysis/c-memory/build/cart_fixed
```

Giải thích stack trace UAF của bản BUGGY và thay đổi lifetime ở bản FIXED.

### Bước 5 — TLC, fuzzing và đặc tả ATPM

```bash
python3 security-analysis/run_tlc.py --jar /path/to/tla2tools.jar
python3 security-analysis/run_fuzzing.py
python3 security-analysis/verify_teacher_spec.py
```

Chỉ ra TLC duyệt 20 state phân biệt không lỗi, fuzz 3/3 với branch 99,07% và
workbook đúng 10 sheet/28 test case của template giảng viên.

### Bước 6 — nêu đúng phần chưa hoàn thành

Không tuyên bố toàn hệ thống đã được chứng minh an toàn. Nêu rõ còn thiếu:

- gateway thật và xác thực callback;
- rate limiting, audit log và hardening production.
- demo triển khai ngoài test host và kiểm thử lỗi mạng/restart process.

---

# KẾT LUẬN VÀ KẾ HOẠCH TIẾP THEO

## 28. Kết quả nổi bật

Đề tài đã tạo được chuỗi bằng chứng tương đối đầy đủ từ yêu cầu đến model và
test. Điểm nổi bật nhất là Kripke đã được nâng từ graph Order 8 trạng thái với
bốn thuộc tính thành model phục hồi Order–Payment–Inventory 15 trạng thái,
37 chuyển tiếp, 7 hợp đồng và P1–P15. Cải tiến này không chỉ tăng số lượng state:
nó sửa đúng liveness `AG AF`, theo dõi lịch sử terminal, chặn retry vô hạn, kiểm
tra compensation, bảo vệ UNKNOWN và kiểm tra tính đầy đủ/xác định của event.

Kết quả đã tái lập ngày 09/10/2026:

- threat model PASS: 6 asset, 4 boundary, 9 threat, đủ 6/6 STRIDE;
- Kripke cơ sở 4/4 PASS; model BUGGY sinh đúng counterexample;
- recovery Kripke 15/15 PASS; 6/6 mutation bị phát hiện;
- static analysis C PASS;
- ASan/UBSan/LSan 22/22 ca đúng kỳ vọng;
- SMT/Z3 7/7 truy vấn đúng kỳ vọng;
- xUnit 87/87 PASS và PostgreSQL integration 5/5 PASS;
- InputValidator đạt 96,87% branch coverage.
- fuzz application 3/3 PASS, 50.000 iteration và branch coverage target 99,07%;
- TLC không phát hiện lỗi: 35 state sinh, 20 state phân biệt, độ sâu 9;
- đặc tả ATPM đúng 10 sheet, 15 state, 7 CTL, 28 test case; toàn suite ATPM 27/27 PASS.

## 29. Thứ tự công việc tiếp theo

1. Kiểm chứng recovery worker qua restart thật với Payment/Refund stale.
2. Tích hợp gateway sandbox, xác thực signature/timestamp/nonce/order/amount.
3. Mở rộng fuzzing/coverage cho HTTP, Service/Controller và nhánh lỗi database.
4. Chạy demo ngoài test host và fault injection cho mạng/PostgreSQL/gateway.
5. Bổ sung rate limiting, audit trail bất biến, metrics/cảnh báo và secret
   rotation trước khi xem xét production.

## 30. Kết luận cuối

Chương 1 đã có Kripke, mutation và TLC; Chương 2 đã hoàn thành trên mô hình C
học tập; Chương 3 có static analysis và SMT; Chương 4 có unit, coverage,
sanitizer, PostgreSQL E2E và fuzzing. Vì vậy kết luận phù hợp là:

> Hệ thống đã thỏa bộ đặc tả và test hiện có, đồng thời có bằng chứng ở mức model,
> unit, fuzz và PostgreSQL integration; kết quả này chưa đồng nghĩa với an toàn
> tuyệt đối hoặc sẵn sàng production khi chưa có gateway thật và hardening vận hành.
