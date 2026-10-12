### 8.1. Phân loại giới hạn theo nguyên nhân

| Loại | Ví dụ hiện tại | Hệ quả |
| --- | --- | --- |
| Abstraction | Model không có stock cụ thể, identity hoặc durable gateway | Không suy safety của các phần bị lược bỏ |
| Thiết kế chưa hiện thực | CONFIRMATION_RETRY tự động; cancel UNKNOWN nếu có proof chưa charge | Cần mapping rõ hoặc thay model để khớp policy source |
| Environment assumption | Gateway cuối cùng trả definitive outcome | Liveness có điều kiện, không cam kết thời hạn hoàn tất |
| Tool capability | Scanner syntax-specific; cppcheck false negative | Kết hợp review và dynamic, không lấy zero finding làm proof |
| Test coverage | Chưa kill/restart; worker tắt trong fixture; chưa fault injection commit | PASS hiện tại không bao phủ crash windows ngoài kịch bản |
| Provenance | Hash đóng gói của evidence lịch sử | Không biết source tại thời điểm cũ nếu chưa có run-time hash |

### 8.2. Ưu tiên cải thiện theo tiêu chí đóng vấn đề

Ưu tiên một là làm rõ contract thiết kế–source cho UNKNOWN/cancel/confirmation,
và kiểm restart với gateway bền vững hoặc sandbox. Tiêu chí đóng là charge trước
khi crash vẫn reconcile cùng key ra cùng kết quả sau restart, không phát khoản
thu mới và không release kho sai.

Ưu tiên hai là giới hạn body/item count, rate limit và audit bền vững: cần test
resource budget, actor/action/resource/outcome và redaction. Bounded charge attempt
không thay rate limit API; console logging không thay audit integrity/retention.

Ưu tiên ba là kiểm precision/scale tiền, network/commit fault injection, worker
scheduler và liên kết historical evidence với run-time hashes. BMC riêng chỉ bổ
sung nếu tiêu chí học phần yêu cầu, với bound và semantics công bố. Không thêm
Kafka/Kubernetes chỉ để tăng số công nghệ khi chưa có nghĩa vụ nghiệp vụ cần nó.

### 8.3. Rủi ro khi áp dụng migration

CHECK mới có thể không apply nếu dữ liệu cũ đã âm hoặc quantity không dương.
Không có script tự xóa dữ liệu vì việc đó có thể phá lịch sử giao dịch. Trước
nâng cấp môi trường có dữ liệu, cần đọc/query vi phạm, xác định nguồn và phương
án correction; giữ migration test database sạch riêng khỏi thao tác dữ liệu thật.
