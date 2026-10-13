### 9.1. Trả lời câu hỏi nghiên cứu

Q1 có source guard và HTTP evidence trên đường đã kiểm; chưa bao phủ toàn bộ vòng
đời token. Q2 có model mutation và PostgreSQL race test; không là proof mọi
interleaving. Q3 có safety cho UNKNOWN/compensation và một process-kill charge recovery với worker; liveness vẫn
phụ thuộc môi trường. Q4 có witness cụ thể, bản sửa theo tổng quantity và evidence
SMT/test/database. Q5 có 22 case memory cùng static false-negative analysis,
không suy rộng sang C#. Q6 có report, phụ lục, traceability và manifest; evidence
lịch sử còn giới hạn provenance tại thời điểm thực thi.

### 9.2. Giá trị và điều kiện mở rộng

Đầu ra có giá trị là kết luận có thể kiểm tra và bị bác bỏ bằng counterexample,
thay vì tuyên bố tuyệt đối về an toàn. Audit liveness bổ sung một kết quả FAIL
có nghĩa trên graph gốc; điều này làm rõ safety-vs-progress của UNKNOWN. Khi mở
rộng gateway hoặc deployment, cần cập nhật assumption, model mapping và scenario
test cùng nhau, rồi đóng gói run mới. Bộ tài liệu này là baseline kỹ thuật cho
việc đó, chưa là chứng nhận an toàn toàn hệ thống.
