# Báo cáo kiểm chứng an toàn OrderPaymentMicroservice

Ngày tổng hợp: 12/10/2026. Các thời điểm thực thi cụ thể nằm trong evidence.

## 1. Mục tiêu và phạm vi

Đề tài xây dựng REST API xử lý đơn hàng và thanh toán, rồi áp dụng mô hình hóa,
kiểm chứng số học và kiểm thử để đánh giá tính an toàn. Đối tượng chính là Order,
Payment và inventory reservation; User/Product/Cart hỗ trợ luồng demo.

Phạm vi thực thi gồm ASP.NET Core .NET 10, EF Core, PostgreSQL và gateway mô phỏng.
Ứng dụng được triển khai trong một host và dùng chung database. Vì vậy báo cáo mô tả
một dịch vụ Order/Payment có các module hỗ trợ, không tuyên bố các module là những
microservice được triển khai độc lập.

Cách tổ chức chương và phụ lục được tham khảo từ BAOCAO.html về Smart EV Charging.
Các requirement, số trạng thái và evidence của Smart EV không được đưa sang đề tài này.
Thư mục ATPM được loại khỏi mọi thao tác trong đợt hoàn thiện này.

## 2. Chương trình mục tiêu

Client gọi Controller qua HTTP; JWT xác định người dùng và role. Service kiểm tra
điều kiện nghiệp vụ, đọc giá Product từ database và sử dụng state machine để thay đổi
Order/Payment. Order reservation trừ stock khi tạo đơn; cancel/decline/refund hợp lệ
hoàn lại stock, còn complete chuyển reservation sang CONSUMED.

Gateway được gọi sau khi lưu trạng thái PROCESSING, bên ngoài transaction database.
Điều này tránh giữ transaction trong lúc chờ mạng. Tuy nhiên hệ thống phải đối soát
kết quả không rõ ràng, thay vì coi timeout là decline. Recovery worker thực hiện
reconciliation cho PROCESSING/UNKNOWN/REFUND_PENDING bị cũ.

Attack surface gồm register/login, payload Product/Cart/Order, ID trên route,
Idempotency-Key, các lệnh lifecycle và phản hồi gateway. Client không có quyền quyết
định giá thanh toán hay truy cập tài nguyên của người dùng khác.

Kiến trúc, API, input/output và danh mục requirement: [Phụ lục A](PHU_LUC_A_G.md#phụ-lục-a--hệ-thống).

## 3. Mô hình trạng thái và thuộc tính

Project có ba cách biểu diễn phục vụ mục đích khác nhau: Kripke Order cơ sở,
recovery model theo event và đặc tả TLA+. Kết quả model không tự chứng minh mọi hành
vi của implementation. Recovery model hiện có 15 trạng thái, 37 chuyển tiếp có event,
7 hợp đồng chức năng và 15 thuộc tính. Sáu mutation được dùng để kiểm tra khả năng
phát hiện sai trạng thái, sai tiền, trùng charge và compensation sai.

Workbook xuất graph còn 32 cặp nguồn–đích vì bỏ metadata event/action/kind và gộp
những cặp trùng. Checker đối chiếu xác nhận hai tập cạnh sau phép chiếu bằng nhau.
Bảy CTL trong workbook là tập thuộc tính riêng, không đồng nghĩa toàn bộ P1–P15.

Liveness phụ thuộc giả định tiến triển/fairness và cách xử lý stutter. Thực tế một
gateway có thể tiếp tục UNKNOWN; lúc đó hệ thống giữ reservation để tránh hoàn kho
trong khi đã thu tiền. TLC có evidence lịch sử với 20 trạng thái phân biệt, không
được chạy lại trong đợt này. Đây là TLC model checking; không gọi kết quả đó là một
thí nghiệm BMC có bound k.

Danh sách state, event, property và giả định: [Phụ lục B](PHU_LUC_A_G.md#phụ-lục-b--model-và-thuộc-tính).

## 4. Phân tích bộ nhớ và mã nguồn

Năm module C học tập tương ứng User/Product/Cart/Order/Payment có phiên bản BUGGY và
FIXED. Evidence lịch sử ghi nhận scanner/cppcheck/layout và 22 ca sanitizer đúng kỳ
vọng. Với BUGGY, test PASS nghĩa là diagnostic lỗi mong đợi đã được phát hiện; không
có nghĩa chương trình lỗi an toàn. Scanner riêng theo pattern không thay thế một
static analyzer đầy đủ.

Các module C không phải bản dịch của source C#. Kết quả chỉ chứng minh hành vi đã
quan sát trong executable và input đã chọn. Static/sanitizer không chạy lại trong
đợt này. Danh mục CWE và kết quả: [Phụ lục C](PHU_LUC_A_G.md#phụ-lục-c--static-và-bộ-nhớ).

## 5. Kiểm chứng số học

Payment SMT dùng integer cents, bảy truy vấn SAT/UNSAT đúng kỳ vọng. SAT trong truy
vấn lỗi là witness tồn tại; UNSAT chỉ áp dụng các assumption được mã hóa. Runtime
sử dụng decimal, database lưu tiền với precision 19,2; mô hình integer không tự
chứng minh rounding/overflow của mọi đường thực thi.

Đợt này bổ sung SMT inventory hai dòng cùng sản phẩm. Baseline stock=10, q1=q2=6
cho witness tồn kho âm dù từng dòng riêng lẻ hợp lệ. Với điều kiện mới stock ≥ q1+q2,
truy vấn tồn kho âm UNSAT; trường hợp đúng biên stock=10, q1=q2=5 vẫn SAT.

Kết quả inventory là chứng minh số học về phép trừ tổng lượng, chưa chứng minh
interleaving nhiều transaction. Evidence PostgreSQL bổ sung kiểm tra constraint và
đồng bộ schema. Các truy vấn: [Phụ lục D](PHU_LUC_A_G.md#phụ-lục-d--smt-và-biên).

## 6. Kiểm thử động và fuzzing

Bộ test ứng dụng mới nhất ghi nhận 108/108 unit test và
8/8 integration test PASS, không SKIP.
Ba campaign fuzz cũng nằm trong unit suite; đợt này không đo lại coverage campaign.
Evidence coverage lịch sử 99,07% chỉ áp dụng InputValidator, PaymentValidator và hai
state machine. InputValidator có branch coverage 96.87%,
không phải coverage toàn ứng dụng.

Regression mới kiểm tra sản phẩm trùng trong Order, quantity âm/0/quá lớn, cộng dồn
Cart vượt kho, sản phẩm inactive, đăng ký sai và giá/tồn kho Product sai. Test HTTP
xác minh status trả về và dữ liệu lưu; test PostgreSQL kiểm constraint 23514, schema
không lệch migration, và cập nhật/xóa giỏ giữ tổng tiền nhất quán.

Các campaign chưa fuzz toàn HTTP API, chưa dùng gateway sandbox và chưa thực hiện
kill/restart process. Cấu hình và danh sách test: [Phụ lục E](PHU_LUC_A_G.md#phụ-lục-e--fuzzing-và-test).

## 7. Kết quả và truy vết

| Phép kiểm | Kết quả | Phạm vi |
| --- | --- | --- |
| Unit | 108/108 PASS | Source hiện tại; gồm 21 regression mới |
| HTTP/PostgreSQL | 8/8 PASS | Database tạm, migration/seed và test host |
| Recovery | 15/15 thuộc tính; 6 mutation detected | Graph hữu hạn, giả định được khai báo |
| Payment SMT | 7/7 đúng kỳ vọng | Số học integer cents |
| Inventory SMT | 3/3 đúng kỳ vọng | Hai dòng của một sản phẩm |
| Workbook checker | 10 sheet; 7 CTL PASS | Checker độc lập, không chạy công cụ Windows giảng viên |
| Model projection | 37 event → 32 cặp | Tập cạnh sau phép chiếu bằng nhau |

Bảng truy vết máy đọc được nằm ở ../results/traceability_matrix.csv. Manifest ghi
hash artifact và base commit; working tree còn thay đổi chưa commit nên không gán
kết quả cho một commit sạch. Hash đóng gói không thay thế hash tại thời điểm thực
thi của evidence lịch sử. [Phụ lục F](PHU_LUC_A_G.md#phụ-lục-f--evidence-và-truy-vết)
liệt kê kết quả từng test và cách tái lập.

## 8. Phát hiện, sửa lỗi và giới hạn

Lỗi inventory phát sinh vì kiểm từng dòng trước khi trừ tổng lượng. Sửa bằng cộng
quantity theo ProductId với long, so sánh stock trước mọi mutation. Các dòng vẫn
được giữ riêng trong OrderItem; cancel hoàn lại tổng reservation đúng một lần.

Cart thêm guard quantity và tổng cộng, chặn inactive và kiểm stock khi update.
Add/update/remove dùng transaction Serializable trên PostgreSQL để các lần lưu item
và tổng không commit riêng. Quantity=0 ở update tiếp tục xóa item; quantity âm bị từ
chối. Cart chỉ báo lượng khả dụng, không giữ chỗ; Order vẫn kiểm tra lại kho.

DTO User/Product thêm annotation validation; service cũng kiểm tra để lời gọi trực
tiếp không bỏ qua guard MVC. Invalid input trả 400; Order thiếu kho trả 409.
Database bổ sung CHECK Stock≥0, Price>0 và Quantity>0 ở OrderItem/CartItem.

Migration không tự sửa/xóa dữ liệu cũ vi phạm constraint: database có dữ liệu sai
cần được kiểm tra và chỉnh có chủ đích trước khi nâng cấp. Kiểm chứng hiện dùng
database sạch, không thực hiện migration lên database phát triển của người dùng.

Gateway mặc định vẫn mất dữ liệu khi restart vì lưu trong memory. Audit trail,
rate limit, gateway xác thực callback, thí nghiệm restart và BMC có bound còn mở.
Không dùng PASS của suite hiện tại để gọi hệ thống production-ready.
[Phụ lục G](PHU_LUC_A_G.md#phụ-lục-g--hạn-chế-và-công-việc-còn-mở).

## 9. Kết luận

Project có chuỗi bằng chứng từ lỗi inventory đến witness SMT, guard source,
regression test và constraint PostgreSQL. Bộ báo cáo và phụ lục phân biệt rõ
implementation, mô hình học tập, kiểm tra độc lập và evidence lịch sử. Phần còn
mở được giữ rõ trong bảng truy vết để tránh suy rộng ngoài phạm vi đã xác minh.
