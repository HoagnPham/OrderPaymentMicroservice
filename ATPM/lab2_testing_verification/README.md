# Lab 2 — Kiểm thử và Kiểm chứng Phần mềm: Hộp đen & Hộp trắng

**Chủ đề xuyên suốt:** SecureSys – Hệ thống Quản lý Truy cập và Giao dịch An toàn
**Module con:** Auth (Xác thực mã PIN)
**Ánh xạ đề cương:** Chương 1 — 1.3 Kiểm thử và kiểm chứng phần mềm I, 1.4 Kiểm thử và kiểm chứng phần mềm II

## 1. Mục tiêu học tập

- Phân biệt **kiểm thử hộp đen (black-box)** dựa trên đặc tả và **kiểm thử hộp trắng (white-box)** dựa trên cấu trúc mã nguồn.
- Áp dụng hai kỹ thuật thiết kế test kinh điển: **Equivalence Partitioning (EP)** và **Boundary Value Analysis (BVA)**.
- Đo độ phủ mã (code coverage) bằng `gcov` và diễn giải ý nghĩa của các dòng "chưa được kiểm thử".
- Gọi hàm C từ Python bằng `ctypes` để xây dựng công cụ kiểm thử tự động đa ngôn ngữ.
- Hiểu vì sao 100% test PASS **không đồng nghĩa** với 100% coverage.

## 2. Bối cảnh bài toán

Hàm `validate_pin()` (module Auth) kiểm tra mã PIN 6 chữ số, từ chối PIN yếu (dãy tăng/giảm liên tục) và khóa tài khoản sau 3 lần nhập sai liên tiếp. Đây là hàm nhiều nhánh (multi-branch), điển hình cho các lỗi bảo mật thực tế: sai thứ tự kiểm tra điều kiện, quên xử lý input rỗng/NULL, off-by-one tại biên.

## 3. Các bước thực hành

### Bước 1 — Phân tích đặc tả, tự thiết kế bảng test (trước khi xem code mẫu)
Liệt kê các **lớp tương đương (equivalence class)** của đầu vào `pin`: hợp lệ / sai độ dài / có ký tự không phải số / tuần tự. Xác định **giá trị biên**: độ dài 5, 6, 7; số lần sai 2, 3, 4.

### Bước 2 — Chạy bộ test hộp đen mẫu
```bash
make lib
python3 python/black_box_tester.py
```
Đối chiếu bảng test tự thiết kế ở Bước 1 với `python/black_box_tester.py::generate_black_box_cases()`.

### Bước 3 — Đo độ phủ mã hộp trắng
```bash
python3 python/white_box_coverage.py
```
Quan sát các dòng **CHƯA được kiểm thử** (ví dụ nhánh lỗi mở file, nhánh `default` switch). Thảo luận: các dòng này có cần test riêng không? Vì sao coverage chỉ đạt ~92% dù test hộp đen đã PASS 100%?

### Bước 4 — Bổ sung test case để tăng coverage
Thêm ít nhất 2 test case mới trong `generate_black_box_cases()` nhắm trúng các dòng chưa phủ (ví dụ: gọi CLI thiếu tham số để phủ nhánh `argc < 2`). Chạy lại Bước 3, xác nhận coverage tăng.

### Bước 5 — Chạy bộ test tự động
```bash
make test
```

## 4. Yêu cầu hoàn thiện (Deliverables)

1. Bảng thiết kế test tự làm (EP + BVA) trước khi đối chiếu code mẫu.
2. Log chạy `black_box_tester.py` (18/18 PASS) và `white_box_coverage.py` (báo cáo % coverage + danh sách dòng chưa phủ).
3. Diff code cho ít nhất 2 test case bổ sung nhằm tăng coverage, kèm log coverage mới.
4. Trả lời câu hỏi: nếu đảo thứ tự kiểm tra `failed_attempts >= 3` xuống **sau** kiểm tra độ dài PIN, hệ thống có còn an toàn không? Giải thích bằng một test case cụ thể.

## 5. Yêu cầu kết quả

- `tests/test_pin_validator.py` toàn bộ PASS (bao gồm ngưỡng coverage tối thiểu 90%).
- Báo cáo trình bày rõ bảng ánh xạ: *Equivalence Class / Boundary → Test Case → Kết quả mong đợi → Kết quả thực tế*.

## 6. Sơ đồ luồng dữ liệu (Dataflow)

```
 Đặc tả validate_pin() (docstring trong pin_validator.c)
            │
            ▼ (thiết kế test theo EP + BVA)
 python/black_box_tester.py::generate_black_box_cases()
            │                             │
            ▼ (ctypes gọi .so)            ▼ (ghi CSV)
 c/libpinvalidator.so            dataset/pin_test_cases.csv
            │                             │
            ▼                             ▼ (CLI đọc CSV)
 dataset/black_box_test_results.csv   c/pin_validator_cli (--coverage)
                                            │
                                            ▼ gcov
                                   Báo cáo % coverage + dòng chưa phủ
                                            │
                                            ▼
                              tests/test_pin_validator.py (đối chiếu tự động)
```

## 7. Tài liệu tham khảo
- Đề cương CSE703093 — An toàn phần mềm, Chương 1 (1.3, 1.4).
- ISTQB Foundation Level Syllabus — Equivalence Partitioning, Boundary Value Analysis.
