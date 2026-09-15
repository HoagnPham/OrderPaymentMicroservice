# Lab 7 — Phân tích Động: Phủ mã (Code Coverage) & Giám sát Thực thi

**Chủ đề xuyên suốt:** SecureSys – Hệ thống Quản lý Truy cập và Giao dịch An toàn
**Module con:** Order Processing (Xử lý đơn hàng)
**Ánh xạ đề cương:** Chương 4 — 4.1 Giới thiệu kiểm thử bảo mật, 4.2 Phủ mã nguồn, 4.3 Theo dõi thực thi chương trình, 4.4 Tạo đường thực thi chương trình

## 1. Mục tiêu học tập

- Đo độ phủ mã (statement/branch coverage) bằng `gcov` cho một hàm nhiều nhánh lồng nhau.
- Hiểu rõ khoảng cách giữa "test PASS 100%" và "coverage cao" — hai khái niệm hoàn toàn độc lập.
- Áp dụng kỹ thuật sinh test tổ hợp (combinatorial testing) dựa trên lớp tương đương để chủ động tăng độ phủ, thay vì viết test ngẫu nhiên.
- Xây dựng một **oracle độc lập** (hàm dự đoán kết quả mong đợi viết lại bằng Python, tách biệt khỏi code C) để tự động sinh nhãn cho test case mới.

## 2. Bối cảnh bài toán

`process_order()` (module Order Processing) tính giảm giá và tổng tiền đơn hàng dựa trên số lượng, đơn giá, và trạng thái thành viên — có nhiều nhánh lồng nhau và một ngưỡng phê duyệt đặc biệt (đơn hàng > 10 triệu VND cần `APPROVAL_REQUIRED`, một dạng kiểm soát gian lận đơn giản).

## 3. Các bước thực hành

### Bước 1 — Đọc đặc tả, tự vẽ cây quyết định (decision tree) của các nhánh
Trước khi chạy công cụ, vẽ tay toàn bộ các nhánh của `process_order()` và liệt kê số lượng "đường thực thi" (execution path) khác nhau lý thuyết.

### Bước 2 — Chạy với bộ test KHÔNG đầy đủ, quan sát coverage thấp
```bash
python3 python/coverage_runner.py
```
Bộ `order_test_inputs_partial.csv` chỉ có 2 test case nhưng **cả 2 đều PASS** — đây là điểm mấu chốt: PASS 100% không nói lên điều gì về chất lượng kiểm thử.

### Bước 3 — So sánh với bộ test đầy đủ
Quan sát log của bộ `order_test_inputs_full.csv` (9 test case) — coverage tăng từ ~80% lên hơn 92%. Đối chiếu các dòng "chưa phủ" còn lại (chủ yếu là nhánh xử lý lỗi CLI, không thuộc logic nghiệp vụ).

### Bước 4 — Tự sinh test tổ hợp
```bash
python3 python/test_case_generator.py
```
Đọc `python/test_case_generator.py::expected_status()` — đây là một "oracle" độc lập. Giải thích tại sao viết oracle độc lập (thay vì so sánh chương trình với chính nó) là nguyên tắc quan trọng trong kiểm thử.

### Bước 5 — Thêm một lớp tương đương mới
Bổ sung `QUANTITY_CLASSES` một giá trị biên mới (ví dụ `qty = 9`, ngay dưới ngưỡng thành viên 10), sinh lại bộ test tổ hợp, chạy lại coverage.

### Bước 6 — Chạy bộ test tự động
```bash
make test
```

## 4. Yêu cầu hoàn thiện (Deliverables)

1. Cây quyết định tự vẽ tay (Bước 1), đối chiếu với log coverage thực tế.
2. Bảng so sánh % coverage của 3 bộ test: partial / full / combinatorial.
3. Giải thích các dòng KHÔNG THỂ đạt 100% coverage dù đã thử mọi tổ hợp nghiệp vụ hợp lệ (gợi ý: nhánh lỗi CLI như `argc < 2`, `fopen` thất bại — cần test riêng ở tầng khác).
4. Bổ sung lớp tương đương mới (Bước 5) kèm log coverage mới.

## 5. Yêu cầu kết quả

- `tests/test_coverage.py` toàn bộ 4 test PASS.
- Bộ test `full` phải đạt coverage cao hơn bộ `partial` ít nhất 5 điểm phần trăm.
- Bộ test tổ hợp tự sinh phải đạt coverage **bằng hoặc cao hơn** bộ `full`.

## 6. Sơ đồ luồng dữ liệu (Dataflow)

```
 c/order_processor.c  --gcc --coverage--> Binary co instrumentation
            │
            ▼ (chay voi tung bo du lieu test)
 dataset/order_test_inputs_{partial,full,combinatorial}.csv
            │
            ▼
 python/coverage_runner.py  --gcov-->  % coverage + danh sach dong chua phu
            │
            ▲
 python/test_case_generator.py (sinh test moi tu lop tuong duong + oracle doc lap)
            │
            ▼
 dataset/coverage_comparison_results.json
            │
            ▼
 tests/test_coverage.py (doi chieu tu dong 3 bo test)
```

## 7. Tài liệu tham khảo
- Đề cương CSE703093 — An toàn phần mềm, Chương 4 (4.1–4.4).
- P. Ammann, J. Offutt, *Introduction to Software Testing* — Combinatorial Testing, Equivalence Partitioning.
