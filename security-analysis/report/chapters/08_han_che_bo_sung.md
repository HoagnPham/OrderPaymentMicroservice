### 8.1. Phân loại giới hạn theo nguyên nhân

| Loại | Ví dụ hiện tại | Hệ quả |
| --- | --- | --- |
| Abstraction | Model không có stock cụ thể, identity hoặc durable gateway | Không suy safety của các phần bị lược bỏ |
| Thiết kế chưa hiện thực | CONFIRMATION_RETRY tự động; callback authenticity | Cần mapping rõ hoặc thay model để khớp policy source |
| Environment assumption | Gateway cuối cùng trả definitive outcome | Liveness có điều kiện, không cam kết thời hạn hoàn tất |
| Audit scope | COMMIT nguyên tử với Orders/Payments; BEGIN/RESULT vẫn riêng; DBA có thể disable trigger | Retention/least-privilege và outbox nếu thêm external sink |
| Tool capability | Scanner syntax-specific; cppcheck false negative | Kết hợp review và dynamic, không lấy zero finding làm proof |
| Test coverage | Đã kill/restart sau charge; đã có refund crash sau journal; chưa network/commit fault đa dạng | PASS hiện tại không bao phủ crash windows ngoài kịch bản |
| Provenance | Hash đóng gói của evidence lịch sử | Không biết source tại thời điểm cũ nếu chưa có run-time hash |

### 8.2. Ưu tiên cải thiện theo tiêu chí đóng vấn đề

Ưu tiên một đã có kết quả local: cancel UNKNOWN đồng bộ model/source,
confirmation tự động được đánh dấu DESIGN_EXTENSION; gateway journal bền vững
và process-kill recovery sau charge PASS. Refund crash sau journal đã có kiểm chứng; phần tiếp theo là cửa sổ
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
và consistency. OS-process probe bổ sung đo throughput/p95 trên workload ngắn;
chưa là saturation/soak benchmark hoặc kiểm chứng cluster nhiều máy. Rate quota vẫn local từng process. Runbook backup/restore và chính sách
retention đề xuất nằm trong [OPERATIONS.md](../OPERATIONS.md). Restore drill đã PASS
trên cluster tạm, chưa thực hiện trên database phát triển/production của người dùng.

Restore local xác minh paired snapshot khi writers dừng, không backup online/PITR,
external provider, encryption-at-rest job hoặc cam kết RPO/RTO. Load sample còn một
user và product hot spot, quota write/read nâng cao; chưa capacity sweep, nhiều
customer/product mix, repetitions hoặc đo payment recovery trong tải liên tục.
Bounded same-key retry/backoff cho create 409 đã có contract và comparison.
Ưu tiên tiếp theo: nhiều lần đo/crossover, quan sát backlog/503,
mở rộng refund crash windows và network/disk faults, rồi đo lại với workload đã công bố.

Health/aggregate metrics và log alert worker đã có, nhưng chưa external exporter,
notification, least-privilege provisioning hoặc dashboard. PendingSince có durable
age từ migration trở đi, lịch sử backfill chỉ lower bound. Metrics reset qua restart,
audit failure counter không bao phủ mọi writer/DB-trigger failure. Dừng DB được kiểm
chứng live/ready, chưa bật lại DB để đo reconnect/failover và backlog recovery.

Retry tăng số HTTP attempts, audit writes và thời gian chờ; 4-attempt/8s bound tránh
loop vô hạn nhưng không đảm bảo mọi request thắng contention. Metadata retryable
chỉ áp dụng create order; các client dùng plaintext 409 cần cập nhật. Workload
mixed-user/product là một sample local, chưa fairness hoặc stability dưới tải dài.
Tiếp theo đo reconnect PostgreSQL, payment backlog dưới tải, shared quota và exporter.
