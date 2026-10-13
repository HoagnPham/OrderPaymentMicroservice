## 1. Mục tiêu, phương pháp và mô hình đe dọa

### 1.1. Bài toán và lý do lựa chọn

Một giao dịch mua hàng đi qua ít nhất ba quyết định độc lập: chấp nhận đơn,
xác định kết quả thanh toán và thực hiện hoặc hủy fulfillment. Sai sót giữa các
quyết định có thể gây mất tiền, bán vượt tồn kho hoặc hoàn hàng khi khoản thu vẫn
chưa rõ kết quả. Vì vậy đối tượng đánh giá là tính nhất quán giữa Order, Payment
và inventory reservation, ngoài việc API trả mã thành công.

Ví dụ, khi gateway timeout, ứng dụng chưa biết tiền đã bị trừ hay chưa. Nếu coi
mọi timeout là FAILED rồi hoàn kho, một đơn đã thu tiền có thể mất reservation.
Ngược lại, nếu retry bằng một key mới, một yêu cầu có thể tạo hai khoản thu. Đây
là lý do cần state machine, idempotency và reconciliation cùng bằng chứng kiểm
chứng cho điều kiện chuyển, thay vì chỉ một happy path.

### 1.2. Câu hỏi nghiên cứu và tiêu chí nghiệm thu

| Mã | Câu hỏi | Tiêu chí có thể kiểm tra | Phép kiểm phù hợp |
| --- | --- | --- | --- |
| Q1 | Client có thể quyết định giá hoặc dùng tài nguyên của user khác không? | Giá tính từ Product; route ID phải đối chiếu subject; Customer bị chặn ở endpoint Admin | Source, regression và HTTP authorization |
| Q2 | Đơn terminal có bị hồi sinh hoặc payment-cancel cùng thắng không? | Không có cạnh chuyển terminal trái phép; kết quả cạnh tranh giữ một aggregate nhất quán | Recovery mutation, state machine và PostgreSQL |
| Q3 | Timeout và lỗi sau charge có làm mất dấu vết tiền không? | UNKNOWN giữ reservation; lỗi sau charge giữ evidence hoặc đi refund | Recovery P13/P14, gateway giả lập và unit test |
| Q4 | Kiểm tra từng dòng sản phẩm có đủ bảo vệ kho không? | Stock ≥ tổng quantity cùng ProductId; reservation sau trừ không âm | Witness SMT, regression, CHECK database |
| Q5 | Tool có phát hiện được lỗi cố ý và bản sửa tương ứng không? | BUGGY phải có đúng diagnostic; FIXED cùng input không có diagnostic; biên chấp nhận/từ chối đúng | Static, sanitizer và test oracle |
| Q6 | Người đọc có thể tái lập và truy vết kết luận không? | Có source/model, command, tool version, outcome, hash và giới hạn từng evidence | Manifest, phụ lục và verifier |

Các tiêu chí này giới hạn kết luận theo phép kiểm. “Đủ sáu nhóm STRIDE” là coverage
của hoạt động phân tích đe dọa; “@@UNIT_COUNT@@ unit PASS” là outcome của các ca thực thi;
“UNSAT” là kết luận trên tập ràng buộc. Ba loại kết quả không thay thế nhau.

### 1.3. Phạm vi và giả định vận hành

Repository hiện có một host ASP.NET Core, chung PostgreSQL, các Controller/Service
User, Product, Cart, Order và Payment. Tên đề tài dùng microservice theo dịch vụ
Order/Payment cung cấp REST API; các module trong host chưa được triển khai độc
lập hoặc có database riêng. C# là implementation nghiệp vụ. Năm executable C là
mô hình học tập ownership/lifetime, không phải bản dịch của implementation.

Phạm vi xác minh gồm request validation, authorization ở đường đã test, lifecycle,
reservation, số học model và một số cạnh tranh database. Chưa bao gồm bảo mật
thiết bị client, cấu hình cloud, gateway thật, audit bền vững, throughput mục tiêu
hay tất cả interleaving. Gateway mặc định DurableFake trả thành công và lưu journal ngoài memory của process.
MemoryFake vẫn được chọn rõ ràng trong fixture thông thường; test gateway có kịch
bản riêng quan sát UNKNOWN. Process-kill recovery đã được xác minh với local journal,
chưa phải provider thật hoặc mất điện.

### 1.4. Asset và ưu tiên CIA

Confidentiality bảo vệ credential và dữ liệu riêng của khách hàng. Integrity bảo
vệ ownership, giá, payment evidence và tồn kho. Availability bảo đảm hệ thống
có thể xử lý và đối soát các trạng thái đang chờ. Giữ UNKNOWN có thể giảm tiến
triển trước mắt nhưng tránh kết luận sai về khoản thu; đây là đánh đổi giữa an
toàn nghiệp vụ và khả năng hoàn tất, không phải lỗi có thể giải quyết bằng cách
đặt terminal tùy ý.

@@ASSET_TABLE@@

### 1.5. Chuỗi đe dọa và trust boundary

Phân tích sử dụng chuỗi **asset → threat → vulnerability → attack → risk → control
→ evidence**. Ví dụ, asset là stock; threat là bán vượt kho; vulnerability là
so sánh stock với từng dòng riêng; attack là gửi hai dòng cùng sản phẩm; control
là kiểm tổng quantity và CHECK database; evidence là witness `10−6−6=−2`, SMT
cho guard mới và regression từ chối đơn mà không đổi stock.

Bốn trust boundary được phân biệt: TB1 Client→API, TB2 Process→PostgreSQL,
TB3 Service→Gateway và TB4 Host configuration→Process. JWT/ownership bảo vệ TB1;
transaction và database constraint bảo vệ TB2; idempotency hỗ trợ TB3 nhưng không
xác thực phản hồi gateway; startup validation hỗ trợ TB4 nhưng không thay thế
rotation hay quyền đọc secret trên host.

@@THREAT_TABLE@@

Risk dùng thang giáo dục likelihood×impact (mỗi thành phần 1–5). Điểm thể hiện
**inherent risk trước control**, không phải CVSS hay residual risk đã đo. Một threat
mang trạng thái VERIFIED_IN_TEST_SCOPE vẫn có giới hạn về endpoint và môi trường.
T3/T5 đã giảm thiểu một phần với audit/admission; retention, DBA governance, distributed
quota và load verification còn mở. T8 còn mở; T9 mới giảm thiểu một phần. Bộ test quyền đã được mở rộng để kiểm
Payment chéo user và Customer gọi các đường Admin; chưa kiểm token hết hạn, key
rotation hay mọi cách giả mạo identity.

### 1.6. Phương pháp và đóng góp

Quy trình gồm đặc tả điều kiện an toàn, mô hình hóa, tạo counterexample, đối chiếu
source, sửa lỗi và chạy regression. Model BUGGY là mutation chủ động; nó khác một
lỗ hổng được khai thác trên môi trường triển khai. Finding inventory được xác định
trong source và có test cho bản sửa; không được gọi witness SMT là kết quả pentest.

Đóng góp chính là liên kết lỗi nghiệp vụ với bất biến và evidence: trạng thái
terminal được bảo vệ bởi state machine; timeout được giữ riêng khỏi decline;
stock được kiểm theo tổng quantity; model recovery được đối chiếu với graph
workbook; static và dynamic analysis được dùng để chỉ ra cả năng lực và điểm bỏ
sót của tool. Cách tổ chức evidence cùng các giới hạn là một đầu ra của đề tài.

Nguồn: [mục tiêu gốc](../../OrderPaymentMicroservice/readme.md),
[threat model](../threat_model.json),
[traceability](../results/traceability_matrix.csv) và
[Phụ lục A](PHU_LUC_A_G.md#phụ-lục-a--hệ-thống).
