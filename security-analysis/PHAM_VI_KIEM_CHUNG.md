# Phạm vi nghiệm thu Order–Payment

Cập nhật 14/10/2026. Đề tài Group 14: **Xây dựng và kiểm chứng an toàn hệ thống
xử lý đơn hàng và thanh toán trực tuyến**. Mục tiêu là kiểm chứng những thuộc tính
đã xác định trong môi trường thử nghiệm, không cam kết toàn bộ hệ thống an toàn.

| Nhóm | Nội dung | Bằng chứng cần có |
| --- | --- | --- |
| Bắt buộc | Idempotency, crash recovery, consistency Order–Payment–Inventory | Regression và assertion dữ liệu PostgreSQL |
| Bắt buộc | PostgreSQL ngắt rồi kết nối lại; API sống và worker phục hồi | ready 200 → 503 → 200, cùng API process, recovery_scan_failures và recovery_resolved |
| Bắt buộc | Gateway đã charge nhưng local finalize thất bại | Một lần gọi charge; worker lookup; Payment SUCCESS, Order PAID, kho giữ reservation |
| Bắt buộc | Gateway đã refund nhưng local finalize thất bại | Một lần gọi refund; giữ charge reference; REFUNDED; tồn kho trả đúng một lần |
| Bắt buộc | Demo tái lập và báo cáo khớp evidence | Lệnh chạy, JSON, source SHA-256, giới hạn, kết quả toàn suite riêng |
| Tùy thời gian | Lặp load, đổi thứ tự scenario, đo p95/success/retry cost | Raw samples và mô tả workload; chưa suy thống kê từ một sample |
| Future work | Tách migration/runtime DB role; retention job; webhook thật; shared Redis limiter; gateway thật; Kubernetes | Ngoài tiêu chí nghiệm thu hiện tại; không tuyên bố đã hoàn thiện |

Giữ JWT/ownership và giới hạn endpoint Admin hiện có. Không thay startup/migration
để thêm DB RBAC cho demo này. Audit append-only đã kiểm trong PostgreSQL; quyền DBA
và quản trị retention vẫn là hạn chế, không phải phần đã chứng minh.

## Chạy demo recovery

```bash
python3 security-analysis/run_recovery_demo.py
```

Cần .NET 10 SDK, dependency đã restore và PostgreSQL binaries cục bộ
(`initdb`, `pg_ctl`, `createdb`; có thể đặt POSTGRES_BIN). Mỗi case tạo cluster và
API riêng trong thư mục tạm; ngắt/bật lại cùng cluster, giữ API process và pool
kết nối của API. Oracle đọc DB bằng kết nối mới để quan sát độc lập. Sau test dọn
process/cluster tạm, không sử dụng database phát triển.

Test executable CrashProbe giữ response gateway sau khi journal ghi bền vững,
cho phép ngắt DB trước finalize rồi thả response khi DB còn tắt. Bộ đếm nằm ở
adapter test, đếm cả lời gọi charge/refund và lookup; hash journal bổ sung đối
chiếu. Production executable không có fault switch/endpoint. Worker dùng thời
gian thật; không sửa timestamp payment để thúc recovery.

Kết quả: [charge](results/postgres_reconnect_charge.results.json),
[refund](results/postgres_reconnect_refund.results.json). Script từ chối thiếu
bằng chứng hoặc hash source không khớp, không ghi đè summary toàn suite bằng hai
test demo. Chạy toàn suite và đóng gói bằng lệnh trong README để chốt báo cáo.

Giới hạn: một cluster PostgreSQL local stop/start và durable fake provider; chưa
network partition, PostgreSQL failover, disk/power loss, provider thật hoặc mọi
crash window. PASS chỉ áp dụng các oracle và kịch bản đã thực thi.
