## 3. Mô hình trạng thái, kiểm chứng và đối chiếu implementation

### 3.1. Kripke và mức trừu tượng

Mô hình Kripke được viết M=(S,S₀,R,L): S là tập trạng thái hữu hạn, S₀ là trạng
thái khởi tạo, R⊆S×S là quan hệ chuyển, L gán tập proposition cho từng state. Một
proposition như payment_success là nhãn của model; nó chưa phải chữ ký xác thực
gateway hay kết quả đo trực tiếp từ database. Việc kiểm nhãn đúng chỉ có ý nghĩa
khi cách ánh xạ nhãn sang nghiệp vụ được giải thích và đối chiếu.

| Model | Đối tượng quan sát | Mục đích | Phần bị trừu tượng hóa |
| --- | --- | --- | --- |
| Order cơ sở | Order state và nhãn payment | Guard terminal, pending/paid và liveness của thiết kế ngắn | Identity, stock số học, retry và transaction |
| Recovery | Aggregate Order/Payment, event, incident, solution | Hợp đồng F1–F7, safety, compensation và tiến triển | Gateway/network cụ thể, nhiều order và dữ liệu tiền thực |
| Workbook graph | Cặp state nguồn–đích và proposition | Tương thích schema và CTL subset | Event/action/kind và hợp đồng chi tiết |
| TLA+ | Order/Payment/Inventory status và attemptCount | Safety/liveness trên tổ hợp biến hữu hạn | Product stock cụ thể, identity, key values và gateway state bền vững |

P1..P4 của Order cơ sở khác P1..P15 của recovery. Trong chương và phụ lục, dùng
`base:P3` hoặc `recovery:P3` để tránh hiểu sai: base:P3 là “PAID có payment SUCCESS”,
còn recovery:P3 là “COMPLETED có payment evidence”. Không ghép kết quả chỉ bằng ID.

### 3.2. Aggregate state và invariant

Recovery có 14 state; source Order có 10 trạng thái, Payment có 7. Ví dụ
PAYMENT_PROCESSING=(Order PAYMENT_PENDING, Payment PROCESSING),
PAYMENT_RETRY=(PAYMENT_PENDING, UNKNOWN), PAID=(PAID,SUCCESS).
Không còn state confirmation-retry vì source không có scheduler/counter tự động;
PAID ở lại PAID để Admin confirm hoặc refund. PRE_PAYMENT_REJECTED là
quan sát việc reject trước persist, không phải tên enum trong implementation.

Safety được diễn giải thành điều kiện: paid phải có evidence; cancelled không
chứa successful charge; failure sau thu tiền giữ evidence hoặc đi compensation;
UNKNOWN không là terminal và vẫn RESERVED; terminal không chuyển sang aggregate
mới. Tập state và nhãn được liệt kê đầy đủ trong phụ lục, thay vì coi một sơ đồ
happy path là toàn bộ model.

### 3.3. Hợp đồng chức năng và kiểm tra tính nhất quán

Bảy hợp đồng F1–F7 định nghĩa độc lập responsibility, precondition và bảng
(state,event)→next_state. Checker recovery:P11 đối chiếu graph với bảng hợp đồng;
P15 từ chối cùng một event trong cùng state được khai báo nhiều lần. P9 chỉ kiểm
incident có đầy đủ mô tả/detection/solution/target, không tự chứng minh detection
đó có implementation hoặc phát hiện được sự cố thật.

Checker dùng BFS từ CREATED để tính reachability và lưu shortest witness.
P8 kiểm mọi state khai báo có reachable. State không reachable chưa chắc là lỗi
runtime; nó có thể là state thừa hoặc trường hợp bị guard loại bỏ. Với label safety,
checker duyệt các state reachable và lưu path tới state vi phạm.

### 3.4. Safety, EF và AF

`AG p` nghĩa là p đúng ở mọi state trên mọi đường reachable. `EF p` chứng minh tồn
tại một đường cuối cùng gặp p. `AF p` yêu cầu mọi đường cuối cùng gặp p. Vì vậy
workbook `EF(terminal)` PASS không đủ để kết luận mọi request hoàn tất. Ví dụ graph
có cạnh tới terminal và self-loop ở PAYMENT_RETRY: EF terminal có thể đúng trong
khi AF terminal sai.

Model BUGGY dùng state PAID_AFTER_CANCELLED để giữ dấu vết lịch sử. Nếu chỉ nhìn
state PAID hợp lệ với payment_success, property kiểm nhãn có thể bỏ sót việc đi
qua CANCELLED trước đó. History monitor gắn invalid_transition làm base:P1 phát
hiện witness CREATED→CANCELLED→PAID_AFTER_CANCELLED.

### 3.5. Phân tích liveness có điều kiện

Để kiểm AF terminal trên graph, bắt đầu T₀ là tập terminal rồi lặp
Tᵢ₊₁=Tᵢ∪{s: Succ(s)≠∅ và Succ(s)⊆Tᵢ} đến điểm cố định. Một state ngoài tập cuối
có khả năng tránh terminal. Điều kiện Succ(s)≠∅ ngăn dead end không terminal được
chấp nhận do phép “mọi successor” trên tập rỗng.

Checker recovery:P10 trước hết xóa cạnh có kind=stutter rồi áp dụng phép tính trên.
Phép audit mới kiểm cả graph gốc và graph đã lọc:

@@LIVENESS_TABLE@@

Counterexample dạng prefix+cycle đi tới PAYMENT_RETRY rồi lặp sự kiện
retry_exhausted_unknown vô hạn. Audit PASS nghĩa là đã xác nhận sự khác biệt mong
đợi giữa hai graph, không phải unconditional AF đã PASS. Trong runtime, gateway
có thể không trả kết quả cuối và UNKNOWN tiếp tục được giữ là lựa chọn safety.

Hai cạnh gắn stutter từ PAYMENT_PROCESSING_FINAL về PAYMENT_RETRY còn đổi aggregate
state. Từ “stutter” ở JSON là nhãn phân loại của project, không đồng nghĩa bước
không đổi mọi biến theo ngữ nghĩa TLA+. Xóa các cạnh này không phải thuật toán
fair-CTL tổng quát. Muốn kết luận fair liveness cần mô hình hóa rõ điều kiện môi
trường và fairness của từng hành động tiến triển.

TLA+ dùng Init∧[][Next]_vars∧WF_vars(Next). Fairness ở Next yêu cầu một bước được
enable liên tục cuối cùng tiến hành, nhưng không tự buộc mọi action riêng lẻ được
chọn, cũng không bảo đảm gateway thật cho ra outcome. Mô hình hiện cho phép chọn
reconcile success/decline khi UNKNOWN; đây là abstraction cần assumption môi
trường. Evidence TLC 20 state phân biệt là kết quả model lịch sử, chưa được chạy
lại trong đợt mở rộng chương.

### 3.6. Mutation và năng lực phát hiện lỗi

@@MUTATION_TABLE@@

Sáu mutation dùng lỗi chủ động để kiểm độ nhạy của property. Một mutation gây
FAIL đúng kỳ vọng là kết quả tốt của checker. Bảng không dùng số lượng mutation
để suy ra mọi lỗi đã bị phát hiện. Witness đi tới state vi phạm là bằng chứng của
model mutated, chưa phải trace HTTP/PostgreSQL của ứng dụng đang triển khai.

### 3.7. Phép chiếu recovery sang workbook

Graph recovery có 35 event transition. Workbook dùng **progress projection**: bỏ các
cạnh stutter/retry self-loop để CTL AF có nghĩa, giữ self-loop ở terminal theo luật
no-deadlock, rồi gộp các cặp (from,to) trùng còn @@PIPELINE_PAIRS@@ cặp vòng đời.
Workbook thêm đúng 6 cạnh của cụm khóa minh họa free/main_holds/log_holds/conflict,
nên tổng cộng 28 cặp nguồn–đích. Phép đối chiếu 22 cặp vòng đời cho kết quả bằng nhau.
Nhiều event như request trùng và amount sai cùng giữ PAYMENT_PENDING sẽ hợp thành
một cạnh self-loop. Do đó phép chiếu giữ quan hệ state đích nhưng làm mất lý do,
biện pháp xử lý và assumption gắn trên event.

Không kết luận property preservation toàn bộ từ equality của tập cạnh: workbook
có 10 CTL (9 đúng, 1 vi phạm cố ý tại `conflict`), recovery có 15 checks gồm checks
cấu trúc và hợp đồng. TLA+ còn có attempt counter nên 20 trạng thái reachable của
TLC không mâu thuẫn 14 aggregate state.

### 3.8. Đối chiếu thiết kế với source

| Chi tiết model | Implementation hiện tại | Mức kết luận |
| --- | --- | --- |
| CREATED→VALIDATED→PAYMENT_PENDING | CreateOrderAsync chuyển trong cùng thao tác trước persist | Ánh xạ thiết kế; không phải ba commit riêng |
| Retry cuối có bound | MaxChargeAttempts=2; UNKNOWN giữ logical payment/key | Có unit; chưa chứng minh mọi crash window |
| Confirmation thất bại | Source giữ PAID; Admin gọi confirm/refund thủ công, không tự charge lại | IMPLEMENTED_POLICY; không có auto-retry counter |
| Cancel tại PAYMENT_RETRY khi outcome UNKNOWN | Model đã giữ PAYMENT_RETRY; CancelOrderAsync từ chối, giữ reservation | IMPLEMENTED_POLICY; regression cancel UNKNOWN |
| Gateway persistence và callback xác thực | DurableFake journal; process restart test có worker bật | Persistence local đã test; callback authenticity vẫn ASSUMPTION_ONLY |
| Atomic payment-cancel | Serializable, Version và unique payment per order | PostgreSQL test cho interleaving đã chạy; chưa refinement proof |
| Không vượt tồn kho | Model lifecycle chỉ có RESERVED/RELEASED/CONSUMED | Cần SMT quantity và regression riêng |

Những khoảng cách này được ghi là giới hạn thiết kế–implementation, không che bằng
câu “model và source hoàn toàn đồng nghĩa”. Một proof đầy đủ cần abstraction
function, relation giữa state cụ thể và trừu tượng, cùng nghĩa vụ chứng minh mọi
bước source mô phỏng bước model. Project hiện cung cấp đối chiếu và test hỗ trợ,
chưa có refinement proof.

### 3.9. Reproducibility và giới hạn BMC

`verify_recovery_model.py` và `audit_liveness.py` chạy độc lập với thư mục ATPM.
Kết quả gồm property, mutation, witness và hash model trong audit. Duyệt graph hữu
hạn và TLC không được gọi là thí nghiệm bounded model checking có bound k. BMC
cho 5 module C dùng CBMC, bound unwind=64/input 48 byte, nằm ở chương 4 và Phụ lục
C.3; UNSAT/SUCCESS trong bound không tự chứng minh safety vô hạn và không áp cho C#.
BMC cho C#/pipeline CNF vẫn cần encode transition/initial/bad, công bố k, solver và verdict.

Nguồn: [recovery model](../order_payment_recovery_model.json),
[checker](../verify_recovery_model.py), [audit](../results/liveness_audit.results.json),
[TLA+](../../OrderPaymentMicroservice/Security/OrderPayment.tla),
[Phụ lục B](PHU_LUC_A_G.md#phụ-lục-b--model-và-thuộc-tính).

### 3.10. Contract hiện thực và crash-recovery đã bổ sung

`implementation_scope` trong recovery JSON phân biệt IMPLEMENTED_POLICY,
DESIGN_EXTENSION và EXTERNAL_ASSUMPTION. Event cancel_during_retry giờ giữ
PAYMENT_RETRY khi outcome UNKNOWN, trùng policy source; regression xác minh
CancelOrderAsync không đổi stock/reservation. Không bổ sung một policy hủy dựa
trên outcome chưa rõ chỉ để đóng liveness.

Gateway DurableFake và process-kill test bổ sung evidence cho một crash window:
provider ledger đã ghi, API chưa finalize. RecoveryWorker trong process mới hỏi
lại cùng key, nhận transaction cũ và lưu PAID/SUCCESS. Evidence này hỗ trợ mapping
F3, không chứng minh chữ ký provider, mất điện hoặc mọi interleaving.

[Hợp đồng hiện thực](../IMPLEMENTATION_CONTRACTS.md) và
[evidence restart](../results/process_restart.results.json) ghi phạm vi cụ thể.
