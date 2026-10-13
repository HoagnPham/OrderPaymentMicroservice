### 5.1. Nghĩa vụ chứng minh và miền biểu diễn

TotalAmount bằng tổng Quantity×UnitPrice là một invariant số học; Payment.Amount
bằng Order.TotalAmount là invariant liên aggregate. Integer cents cho phép mô
hình hóa equality không qua số thực. Trong runtime, Order chấp nhận tolerance 0,01
ở boundary client nhưng lưu serverTotal; Payment yêu cầu equality chính xác với
số tiền đã lưu. Vì vậy tolerance của Order không phải quyền trả thiếu ở Payment.

Mọi price/total/payment amount ở boundary hiện phải là bội của 0,01. MoneyAmounts
so sánh giá trị với decimal.Round(value,2) để từ chối fractional cent mà không
nhân gây overflow; MoneyAmountAttribute áp vào Product DTO và service validation.
25,0100 hợp lệ vì có cùng giá trị số với 25,01; 25,005 bị từ chối, không round
âm thầm. Order còn kiểm Product.Price ở service để guard dữ liệu bất thường.

HTTP/PostgreSQL test xác minh Product 25,0100 lưu 25,01; update 25,005 bị 400 và
không đổi giá; Order total sai scale không được lưu; payment sai scale không tạo
record; amount hợp lệ round-trip 25,01. Database vẫn có khả năng round khi một
writer ngoài API gửi trực tiếp numeric nhiều scale; policy này bảo vệ các boundary
application đã kiểm, không thay quyền kiểm soát mọi database writer.

### 5.2. SAT/UNSAT theo mục đích truy vấn

| Loại query | Điều kiện cần hỏi solver | Cách diễn giải |
| --- | --- | --- |
| Input hợp lệ | Invariant ∧ sample hợp lệ | SAT là witness chương trình có trường hợp hợp lệ |
| Vi phạm invariant | Guard ∧ negated invariant | UNSAT loại vi phạm trong tập giả định |
| Baseline lỗi | Guard cũ ∧ bad-state | SAT chỉ ra counterexample cần regression |
| Bound overflow | Miền Int32 hoặc total bound | Không áp UNSAT toán học nguyên cho machine integer nếu thiếu bound |

TEST6 tìm Int32 subtotal overflow nhưng source nghiệp vụ dùng checked decimal;
đó là ví dụ nguy cơ của một miền biểu diễn khác, không là lỗ hổng overflow C# đã
xác nhận. Inventory SMT dùng integer quantity không giới hạn máy; source cộng
bằng long và mỗi dòng có bound. Proof hiện cho hai dòng cùng sản phẩm, không tự
chứng minh mọi collection size hay transaction interleaving.

### 5.3. Traceability và khoảng trống

Mỗi query phải liên kết assumption, guard source, sample test và outcome. SMT
UNSAT không kiểm entity tracking, rollback hoặc phản hồi gateway. Precision/scale boundary đã có regression;
subtotal consistency trong database, refund amount và retry counter arithmetic
cần phép kiểm riêng nếu được đưa thành yêu cầu nghiệm thu. Danh sách query và
model solver được giữ tại Phụ lục D, không đưa verdict suy đoán vào bảng tổng hợp.
