# Bổ sung ATPM cho OrderPaymentMicroservice

## Kết quả đã chạy

- Model kỳ vọng: **4/4 PASS**.
- Threat model Chương 1: **PASS**, 6 asset, 4 trust boundary, 9 threat và **6/6 STRIDE**.
- Model BUGGY: **P1 FAIL**, counterexample `CREATED → CANCELLED → PAID_AFTER_CANCELLED` (trạng thái nghiệp vụ PAID).
- Model phục hồi Order-Payment: **15/15 PASS**, 15 trạng thái, 37 chuyển tiếp và 7 hợp đồng chức năng; metadata aggregate được kiểm tra consistency.
- Sáu mutation về hồi sinh đơn hủy, sai amount, thanh toán trùng, xung đột payment-cancel, bỏ qua refund sau PAID và đóng sai kết quả UNKNOWN đều bị phát hiện kèm witness.
- xUnit: **87 unit PASS, 0 FAIL, 0 SKIP**; PostgreSQL integration **5/5 PASS** qua local fallback khi Docker không có.
- Coverlet toàn lớp InputValidator: **31/32 nhánh = 96,875%** (bảng CLI hiển thị 96,87%).
- `ValidateCreateOrderRequest()`: **100% branch coverage**, **100% line coverage**.
- Toàn lớp: line coverage **84,31%**; đây không phải chỉ số branch coverage.
- Threat model: **PASS**, đủ CIA và **6/6 STRIDE**.
- Static analysis C: **PASS**, scanner bắt đủ 5 BUGGY và FIXED sạch trong phạm vi rule.
- ASan/UBSan/LSan: **22/22 PASS** ngoài sandbox/debugger.
- SMT/Z3 5.1.0: **7/7 SAT/UNSAT đúng kỳ vọng**.
- Application fuzzing: **3/3 PASS**, 50.000 iteration validator/state transition, branch coverage **99,07%**.
- TLC: **không phát hiện lỗi**, 35 state sinh, 20 state phân biệt, độ sâu 9.
- Đặc tả ATPM: đúng **10 sheet**, 15 state, 7 CTL, 28 test case; **6/6 test đặc tả** và toàn suite ATPM **27/27 PASS**.

## Chạy lại

Từ thư mục gốc repository, dùng Python 3.10+ và .NET 10 SDK:

```powershell
python security-analysis/verify_threat_model.py
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj /p:CollectCoverage=true /p:CoverletOutput=../security-analysis/results/input-validator /p:CoverletOutputFormat=cobertura '/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator' /p:Threshold=90 /p:ThresholdType=branch
python security-analysis/run_analysis.py
make -C security-analysis/c-memory static-check
make -C security-analysis/c-memory test
python security-analysis/run_fuzzing.py
python security-analysis/run_tlc.py --jar /path/to/tla2tools.jar
python security-analysis/create_order_payment_spec.py
python security-analysis/verify_teacher_spec.py
pytest -q ATPM/atpm_project/python/tests/test_order_payment_spec.py
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

## Threat model Chương 1: CIA, STRIDE và trust boundary

Threat model có cấu trúc nằm trong `threat_model.json`; bản giải thích, data-flow
diagram, risk register và bảng truy vết nằm trong `THREAT_MODEL_CHUONG1.md`.
Mỗi threat ghi rõ asset, trust boundary, threat agent, vulnerability, attack,
ảnh hưởng CIA, likelihood × impact, control, trạng thái và evidence. Script
`verify_threat_model.py` kiểm tra tự động ID/tham chiếu, đủ 6 nhóm STRIDE và 3 thuộc
tính CIA, phép tính risk, recommendation cho threat mở và property Kripke được
tham chiếu; kết quả lưu tại `results/threat_model_results.json`.

Các threat còn mở được giữ rõ ràng thay vì coi control trong source là bằng
chứng runtime: repudiation/audit trail (T3), DoS/rate limit (T5), xác thực
gateway thật (T8); quản lý secret (T9) mới giảm thiểu một phần. Các control
authentication/ownership/role và transaction đã được đối chiếu thêm bằng HTTP/PostgreSQL E2E; gateway thật vẫn nằm ngoài model.

Liên kết hình thức quan trọng: counterexample
`CREATED → CANCELLED → PAID_AFTER_CANCELLED` là attack trace của T7/VULN-003;
P1 phát hiện bằng history monitor. P3/P4 hỗ trợ T2/T7/T8 về consistency, nhưng
P3 không chứng minh nguồn của `payment_success` là gateway thật đã xác thực.
Model Order hiện không chứa identity/role, nên không dùng nó để tuyên bố đã
chứng minh các threat Spoofing/IDOR/Elevation of Privilege.

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

Đây là **model thiết kế kỳ vọng**, không phải model tự động trích xuất từ implementation. Source hiện chuyển CREATED → VALIDATED → PAYMENT_PENDING và bảo vệ PAID/CONFIRMED/COMPLETED bằng state machine cùng payment evidence, nhưng kết quả PASS của model vẫn không tự chứng minh API thực thi đúng. BUGGY minh họa riêng lỗi CANCELLED → PAID và không cố mô hình hóa toàn bộ lỗi runtime.

## Model xử lý sự cố và phục hồi

Model mở rộng không chỉ ghi happy path của `OrderStatus`. Mỗi state còn khai
báo trạng thái Order/Payment, sự cố, cách phát hiện, biện pháp xử lý và state
tiếp theo.

Đây là kiểm chứng chức năng trên mô hình, không phải danh sách test. Bảy hợp
đồng F1–F7 định nghĩa độc lập state nguồn, tiền điều kiện, event và state đích
được phép. P11 đối chiếu toàn bộ 37 cạnh với hợp đồng; P12 đóng terminal, P13
bắt buộc compensation sau khi đã thu tiền, P14 giữ an toàn khi gateway UNKNOWN
và P15 cấm một event có hai quyết định trong cùng state. Ma trận CSV ghi rõ
`function → state → event/problem → detection → solution → next_state → guarantee`.

Các state thiết kế bổ sung gồm `PAYMENT_PROCESSING`, `PAYMENT_RETRY`,
`PAYMENT_PROCESSING_FINAL`, `CONFIRMATION_RETRY`, `REFUND_PENDING` và
`REFUNDED`. Retry được bounded bằng state `PAYMENT_PROCESSING_FINAL`. Phiên bản
model đầu tiên cho phép vòng `PAYMENT_PROCESSING ↔ PAYMENT_RETRY` vô hạn và đã
bị thuộc tính P10 phát hiện trước khi model được sửa.

P10 kiểm tra mọi đường tiến triển cuối cùng tới terminal sau khi loại riêng các
cạnh được đánh dấu `stutter`. Kết luận phụ thuộc vào giả định weak fairness đã
ghi trong model. Source hiện đã có hai state machine Order/Payment và các nhánh
retry, reconciliation, refund tương ứng, nhưng model hữu hạn vẫn không thay thế
việc đối chiếu implementation. Integration test HTTP/PostgreSQL là bằng chứng
phụ cho sự phù hợp đó, không phải nội dung chứng minh Kripke.

## Case test và nhánh chưa được phủ

Test bao gồm null request/item/list, list rỗng; CustomerId/ProductId không dương; total âm/0/dưới min/trên max; quantity dưới/trên biên; price âm/trên max; biên hợp lệ; nhiều item; tolerance ±0,01 và vượt tolerance cả hai hướng; decimal.MaxValue; phép nhân lớn nhất được validator cho phép.

- Nhánh `Math.Abs(...) > 0.01` có **100% (2/2)** coverage và hiện ném `ArgumentException` khi mismatch vượt tolerance.
- Nhánh duy nhất chưa phủ: dòng **101**, `subtotal < 0`, hướng true. Quantity đã bị giới hạn 1..10000 và UnitPrice 0,01..999999,99 nên subtotal không thể âm.
- Dòng **102**, và catch OverflowException **104–107** chưa chạy. Tích tối đa 9.999.999.900 thấp hơn rất nhiều decimal.MaxValue; overflow không thể xảy ra sau các guard hiện tại. Không dùng reflection hoặc thay validator để ép nhánh bất khả thi.
- Các dòng khởi tạo hằng decimal cũng được Coverlet ghi nhận chưa phủ; chúng không làm thay đổi kết luận về branch coverage.
- `MIN_PRICE` 0,01 hiện được áp dụng; giá 0 và 0,001 bị từ chối bằng regression test.

## Chương 2: static analysis, ownership và alignment

`c-memory/tests/check_static.py` chạy project-specific pattern/data-flow rules,
GCC syntax check, cppcheck XML và layout probe cho năm module C. Scanner phát
hiện đúng leak, double-free, use-after-free và hai unbounded copy trên BUGGY;
FIXED không còn các finding tương ứng. Cppcheck chỉ bắt trực tiếp lỗi buffer của
User, minh họa false negative thay vì được dùng như bằng chứng an toàn.

`make static-check` lưu tool version, finding và size/alignment vào
`c-memory/evidence/static-analysis.json`. `make test` chạy 22 ca sanitizer và
lưu snapshot bàn giao tại `c-memory/evidence/dynamic-analysis.json`; raw log
chi tiết trong `c-memory/results/` chỉ là artifact cục bộ. Các mô-đun này là mô
hình C học tập theo ngữ cảnh nghiệp vụ, không phải bản dịch của ứng dụng C#.

## SMT/Z3 cho bất biến tiền

`PaymentVerification.z3` dùng `QF_NIA` và integer cents. Runner
`verify_payment_smt.py` yêu cầu Z3 thoát thành công, kiểm tra 7 kết quả mong đợi
và lưu `results/payment_verification.results.json`. Kết quả gồm một model PAID
hợp lệ SAT; mismatch, underpayment, payment âm và overpayment UNSAT; một
counterexample Int32 overflow SAT; cùng chứng minh total bound loại subtotal
quá lớn UNSAT. Đây là bằng chứng cho mô hình số học, không thay thế kiểm thử
implementation/database/gateway.

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

Điểm 8,1 HIGH mô tả rủi ro tại thời điểm phát hiện, không phải residual risk sau sửa. HTTP integration test 401/403 đã PASS trên PostgreSQL cục bộ trong lần xác minh 09/10/2026, vì vậy control ownership đã có bằng chứng runtime trong test host.

Source hiện đã có authentication, ownership check, server-side pricing, mismatch rejection, hai state machine, payment amount/idempotency, bounded retry, reconciliation và refund. Phần còn thiếu đáng kể là adapter gateway thật và hardening production.

## Lưu ý build

Project hiện tham chiếu Npgsql.EntityFrameworkCore.PostgreSQL 8.0.8. Repository từng track một số file bin/obj nên build có thể làm chúng thay đổi; các artefact sinh tự động không phải source hoặc bằng chứng chính.
