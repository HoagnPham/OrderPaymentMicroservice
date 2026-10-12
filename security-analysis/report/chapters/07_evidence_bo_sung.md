### 7.1. Mức bằng chứng và quy tắc diễn giải

| Mức | Điều đã biết | Cách ghi trong kết luận |
| --- | --- | --- |
| SOURCE_CONTROL | Có guard/cấu hình trong source | “Đã hiện thực”, kèm đường code và chưa suy runtime |
| MODEL_PASS | Checker thỏa thuộc tính trong model/assumption | “Model PASS trong phạm vi...” |
| TEST_PASS | Ca thực thi có oracle đạt | “Test PASS trên input và môi trường...” |
| HISTORICAL | Artifact được giữ từ lần chạy trước | Nêu lần chạy trước và không đổi thành vừa chạy |
| OPEN / MODEL_ONLY | Chưa có implementation/evidence phù hợp | Nêu đầu ra cần bổ sung để đóng khoảng trống |

Hash đóng gói ghi lại bytes hiện có; provenance của một lần chạy phải liên kết
input hash, tool version, command và outcome. Với metadata Chapter 4 ghi sau lệnh,
không có timestamp bắt đầu/kết thúc chính xác: report nêu rõ giới hạn đó. Base
commit cùng dirty working tree không tương đương kết quả trên commit sạch.

### 7.2. Các thay đổi có thể truy vết

Chuỗi inventory bắt đầu từ điều kiện sai theo từng dòng, witness 10−6−6, guard
kiểm tổng lượng, SMT cho phép trừ mới, unit test biên 5+5 và HTTP/database
regression. Chuỗi liveness bắt đầu từ assumption stutter, audit raw graph, lasso
counterexample và giới hạn fair-CTL. Chuỗi memory bắt đầu từ ownership allocation,
static finding hoặc false negative, diagnostic sanitizer và bản FIXED cùng input.

Ma trận truy vết giữ SEC-* cho yêu cầu bổ sung, REQ-* cho workbook gốc. Tên property
luôn cần namespace model khi P1..P4 trùng ID. Không tự gán risk score residual khi
chỉ có source guard hoặc vài test PASS.

### 7.3. Điều kiện tái lập

Đọc phụ lục bằng source/model đã hash, chạy đúng tool version/command nếu có thể,
và kiểm lại oracle. Khi môi trường hoặc input thay đổi, lưu run mới và phân loại
kết quả mới thay vì ghi đè kết luận lịch sử không giải thích. Không cần đưa local
binary vào source control; manifest lưu hash và raw log có thể tạo lại bằng các
lệnh thuộc project. Không lệnh tái lập trong bộ báo cáo mới cần truy cập ATPM.
