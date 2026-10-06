# README – MỤC TIÊU BTL CSE703093

## 1. Tên đề tài

Xây dựng và kiểm chứng an toàn hệ thống xử lý đơn hàng và thanh toán trực tuyến.

## 2. Mục tiêu chính

Xây dựng một hệ thống xử lý đơn hàng và thanh toán trực tuyến dưới dạng
microservice, có khả năng tích hợp với một website/shop bán hàng thông qua REST API.

Trọng tâm của BTL không phải xây dựng một hệ thống thương mại điện tử lớn,
mà là nghiên cứu, phân tích, kiểm chứng và kiểm thử tính an toàn của
module xử lý đơn hàng và thanh toán.

## 3. Mục tiêu về hệ thống

Hệ thống cần có khả năng:

- Tiếp nhận và xử lý đơn hàng.
- Tính toán và kiểm tra thông tin đơn hàng.
- Xử lý thanh toán.
- Quản lý trạng thái đơn hàng và thanh toán.
- Lưu trữ và truy xuất dữ liệu.
- Cung cấp REST API để tích hợp với shop bán hàng.
- Có thể chạy thực tế và có demo.

## 4. Mục tiêu về An toàn phần mềm

Sau khi hệ thống hoạt động ổn định, áp dụng các kỹ thuật đã học trong học phần
để phân tích và kiểm chứng an toàn, tập trung vào:

### Chương 1
Mô hình hóa trạng thái của đơn hàng/thanh toán bằng Kripke và kiểm chứng
các thuộc tính an toàn/liveness bằng BMC.

### Chương 2
Phân tích các vấn đề liên quan đến an toàn bộ nhớ hoặc các thành phần phù hợp
với kỹ thuật đã học.

### Chương 3
Sử dụng SMT/Z3 để kiểm chứng các thuộc tính số học và điều kiện an toàn,
đặc biệt đối với việc tính toán số lượng, đơn giá và tổng tiền.

### Chương 4
Thực hiện fuzzing/dynamic testing đối với input hoặc API/payload và thu thập
kết quả thực nghiệm.

## 5. Mục tiêu cuối cùng

Không chỉ phát hiện lỗi mà phải thể hiện được quy trình:

Phát hiện
→ Phân tích nguyên nhân
→ Kiểm chứng / tạo counterexample
→ Kiểm thử thực tế
→ Sửa lỗi
→ Kiểm chứng và kiểm thử lại.

## 6. Nguyên tắc phạm vi

- Tập trung vào Order & Payment.
- Không mở rộng thành một hệ thống thương mại điện tử lớn.
- Ưu tiên phân tích sâu một module/hệ thống nhỏ hơn là xây dựng quá nhiều chức năng.
- Không thêm các công nghệ hoặc thành phần không cần thiết nếu chúng không phục vụ mục tiêu BTL.
- Web, Database, Microservice và Docker là phần hỗ trợ cho hệ thống; trọng tâm đánh giá là
  mô hình hóa, kiểm chứng, kiểm thử và bằng chứng thực nghiệm.

## 7. Mục tiêu đầu ra

Cuối BTL cần có:

- Một hệ thống Order & Payment chạy được.
- API có thể tích hợp với shop.
- Database hoạt động.
- Demo hệ thống.
- Mô hình và thuộc tính kiểm chứng.
- Kết quả kiểm chứng/kiểm thử có bằng chứng.
- Các lỗi/vấn đề được phát hiện và phân tích.
- Phiên bản sau khi sửa lỗi và kết quả kiểm chứng lại.
- Báo cáo kỹ thuật giải thích rõ phương pháp, kết quả và kết luận.
- AI chỉ được đề xuất hoặc thực hiện những thay đổi phục vụ trực tiếp các mục tiêu BTL ở trên.
Không tự mở rộng phạm vi dự án hoặc chuyển sang các vấn đề ngoài mục tiêu hiện tại.


