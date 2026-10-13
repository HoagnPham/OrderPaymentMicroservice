### 8.1. Phân loại giới hạn theo nguyên nhân

| Loại | Ví dụ hiện tại | Hệ quả |
| --- | --- | --- |
| Abstraction | Model không có stock cụ thể, identity hoặc durable gateway | Không suy safety của các phần bị lược bỏ |
| Thiết kế chưa hiện thực | CONFIRMATION_RETRY tự động; callback authenticity | Cần mapping rõ hoặc thay model để khớp policy source |
| Environment assumption | Gateway cuối cùng trả definitive outcome | Liveness có điều kiện, không cam kết thời hạn hoàn tất |
| Audit scope | COMMIT nguyên tử với Orders/Payments; BEGIN/RESULT vẫn riêng; DBA có thể disable trigger | Retention/least-privilege và outbox nếu thêm external sink |
| Tool capability | Scanner syntax-specific; cppcheck false negative | Kết hợp review và dynamic, không lấy zero finding làm proof |
| Test coverage | Đã kill/restart sau charge; chưa refund crash, network/commit fault đa dạng | PASS hiện tại không bao phủ crash windows ngoài kịch bản |
| Provenance | Hash đóng gói của evidence lịch sử | Không biết source tại thời điểm cũ nếu chưa có run-time hash |

### 8.2. Ưu tiên cải thiện theo tiêu chí đóng vấn đề

Ưu tiên một đã có kết quả local: cancel UNKNOWN đồng bộ model/source,
confirmation tự động được đánh dấu DESIGN_EXTENSION; gateway journal bền vững
và process-kill recovery sau charge PASS. Phần tiếp theo là refund crash, cửa sổ
trước charge và provider sandbox; không suy từ test local sang xác thực gateway thật.

Admission và audit đã có regression: body/item cap, auth/user quotas, durable
BEGIN/RESULT, COMMIT cùng transaction, field allowlist và trigger append-only.
Order-create có idempotency tùy chọn và original receipt. Phần tiếp theo là
retention/least-privilege audit access, load/distributed quota và external outbox
nếu thêm một đích audit bên ngoài PostgreSQL. Audit RESULT failure có thể sau commit;
503 không tự chứng minh nghiệp vụ chưa thực hiện.

Precision/scale và timeout/cancellation worker đã có regression; phần tiếp theo
là network/commit fault injection đa dạng, worker scheduler trên tải thực tế và
backpressure khi provider không tuân cancellation. Historical evidence cần liên
kết run-time hashes khi chạy lại. BMC riêng chỉ bổ
sung nếu tiêu chí học phần yêu cầu, với bound và semantics công bố. Không thêm
Kafka/Kubernetes chỉ để tăng số công nghệ khi chưa có nghĩa vụ nghiệp vụ cần nó.

### 8.3. Rủi ro khi áp dụng migration

CHECK mới có thể không apply nếu dữ liệu cũ đã âm hoặc quantity không dương.
Không có script tự xóa dữ liệu vì việc đó có thể phá lịch sử giao dịch. Trước
nâng cấp môi trường có dữ liệu, cần đọc/query vi phạm, xác định nguồn và phương
án correction; giữ migration test database sạch riêng khỏi thao tác dữ liệu thật.

Hai host API đã có kiểm thử hữu hạn tranh 10 đơn vị tồn kho với 16 yêu cầu và retry
có giới hạn, cùng kiểm thử tám request cùng key. Đây là regression về concurrency
và consistency, chưa là benchmark throughput/p95, soak test hoặc kiểm chứng cluster
nhiều máy. Rate quota vẫn local từng process. Runbook backup/restore và chính sách
retention đề xuất nằm trong [OPERATIONS.md](../OPERATIONS.md); restore drill chưa
được thực hiện trên môi trường của người dùng.
