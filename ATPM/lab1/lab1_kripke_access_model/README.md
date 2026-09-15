# Lab 1 — Mô hình hóa An toàn Phần mềm bằng Cấu trúc Kripke (Kripke Structure)

**Chủ đề xuyên suốt:** SecureSys – Hệ thống Quản lý Truy cập và Giao dịch An toàn
**Module con:** Access Control (Kiểm soát truy cập cửa ra vào)
**Ánh xạ đề cương:** Chương 1 — 1.1 Các khái niệm về an toàn phần mềm, 1.2 Một số vấn đề về an toàn phần mềm, 1.5 Cấu trúc Kripke

## 1. Mục tiêu học tập

Sau bài thực hành, sinh viên có khả năng:
- Định nghĩa được cấu trúc Kripke M = (S, S0, R, L) và biểu diễn một hệ thống thực (hệ thống kiểm soát cửa) dưới dạng mô hình này.
- Cài đặt thuật toán duyệt không gian trạng thái (state-space exploration) bằng BFS.
- Kiểm chứng (model checking) các thuộc tính an toàn cơ bản: AG !p (an toàn), AG (p → q) (an toàn có điều kiện), EF p (tính khả thi/reachability).
- Phân biệt được lỗi trong **mô hình/đặc tả** (phát hiện bằng kiểm chứng tĩnh ở Python) với lỗi trong **cài đặt thực thi** (phát hiện bằng cảnh báo runtime ở C).
- Đọc/ghi dữ liệu an toàn trong C (không dùng `gets`, kiểm tra `fopen`, giới hạn buffer).

## 2. Bối cảnh bài toán

Module Access Control điều khiển một khóa cửa điện tử với các sự kiện: `insert_card`, `auth_success`, `auth_fail`, `auth_fail_3rd_time`, `door_closed_timeout`, `tamper_sensor`, `operator_reset`. Hệ thống phải đảm bảo:

- **P1 (an toàn):** không bao giờ ở trạng thái `unlocked` mà chưa `authenticated`.
- **P2 (an toàn):** mỗi khi có `tamper`, hệ thống phải ở trạng thái `alarm`.
- **P3 (khả thi):** phải tồn tại ít nhất một đường đi hợp lệ dẫn đến `unlocked` (nếu không, hệ thống vô dụng).

Bài thực hành cung cấp **hai bộ dữ liệu (dataset)**:
- `dataset/access_system_model.json` — phiên bản đúng đặc tả (SAFE).
- `dataset/access_system_model_BUGGY.json` — phiên bản có một cạnh chuyển tiếp sai (`timeout_glitch`) mô phỏng lỗi logic thực tế (ví dụ điều kiện đua — race condition — bỏ qua bước xác thực), và một lỗi thứ hai khiến trạng thái xử lý tamper trở thành không thể tới được (unreachable).

Dữ liệu CSV tương ứng (`access_safe_*.csv`, `access_buggy_*.csv`) được sinh ra cho chương trình C.

## 3. Các bước thực hành

### Bước 1 — Đọc và hiểu mô hình JSON
Mở `dataset/access_system_model.json`, vẽ tay đồ thị trạng thái trên giấy/whiteboard trước khi chạy công cụ.

### Bước 2 — Kiểm chứng bằng Python (model checking)
```bash
python3 python/kripke_model.py dataset/access_system_model.json
python3 python/kripke_model.py dataset/access_system_model_BUGGY.json
```
So sánh kết quả: bản SAFE cả 3 thuộc tính PASS; bản BUGGY thuộc tính **P1 FAIL** với một *counterexample trace* cụ thể.

### Bước 3 — Đọc và cài đặt hàm `check_AG_implies` (tự làm)
Thử tự viết lại hàm `check_EF` mà không nhìn code mẫu, sau đó so sánh.

### Bước 4 — Biên dịch và chạy mô phỏng C
```bash
make            # biên dịch access_simulator
make run-safe   # chạy chuỗi sự kiện hợp lệ
make run-buggy  # kích hoạt lỗi timeout_glitch, quan sát CẢNH BÁO AN TOÀN runtime
```

### Bước 5 — Thử các chuỗi sự kiện của riêng bạn
```bash
./c/access_simulator dataset/access_safe_states.csv dataset/access_safe_transitions.csv \
    dataset/access_safe_init.txt insert_card auth_fail insert_card auth_fail insert_card auth_fail_3rd_time
```
Quan sát chương trình xử lý sự kiện không hợp lệ theo nguyên tắc **fail-closed** (từ chối, giữ nguyên trạng thái) thay vì **fail-open**.

### Bước 6 — Chạy bộ test tự động
```bash
make test
# hoặc
python3 tests/test_kripke.py
```

## 4. Yêu cầu hoàn thiện (Deliverables)

1. File trả lời (nộp kèm báo cáo) mô tả bằng lời cấu trúc Kripke của module Access Control (S, S0, R, L).
2. Ảnh chụp màn hình (hoặc log) kết quả chạy Bước 2 cho cả hai dataset.
3. Đoạn code tự viết cho ít nhất 1 thuộc tính CTL mới (ví dụ: "không bao giờ đồng thời `unlocked` và `alarm`") và kết quả kiểm chứng.
4. Log chạy `make test` với toàn bộ test PASS.
5. Trả lời câu hỏi phân tích trong báo cáo (`docs/lab1.pdf`, mục "Câu hỏi thảo luận").

## 5. Yêu cầu kết quả

- Tất cả 5 test trong `tests/test_kripke.py` phải PASS.
- Phát hiện chính xác đường đi phản chứng (counterexample) `S0 -> S1 -> S5` cho vi phạm P1 trên bộ BUGGY.
- Báo cáo nộp phải trình bày rõ **mô hình hóa (dataflow): Dataset JSON → Kripke Structure (Python) → Kết quả kiểm chứng → Đối chiếu Runtime (C)**.

## 6. Sơ đồ luồng dữ liệu (Dataflow) của bài thực hành

```
 dataset/access_system_model*.json
            │
            ▼
 python/kripke_model.py  --(BFS + đánh giá công thức)-->  Kết quả kiểm chứng
            │                                              (PASS / FAIL + trace)
            ▼
 dataset/access_*_states.csv, access_*_transitions.csv  (dữ liệu tương đương cho C)
            │
            ▼
 c/access_simulator.c  --(mô phỏng runtime theo chuỗi sự kiện)-->  Cảnh báo an toàn runtime
            │
            ▼
 tests/test_kripke.py  (đối chiếu tự động Python <-> C, SAFE <-> BUGGY)
```

## 7. Tài liệu tham khảo
- Đề cương CSE703093 — An toàn phần mềm, Chương 1.
- E. Clarke, O. Grumberg, D. Peled, *Model Checking*, MIT Press.
