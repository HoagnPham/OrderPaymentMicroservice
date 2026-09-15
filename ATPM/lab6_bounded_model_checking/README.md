# Lab 6 — Kiểm chứng Mô hình Giới hạn (Bounded Model Checking) & Mô hình Bộ nhớ

**Chủ đề xuyên suốt:** SecureSys – Hệ thống Quản lý Truy cập và Giao dịch An toàn
**Module con:** Inventory (Truy cập mảng catalog sách)
**Ánh xạ đề cương:** Chương 3 — 3.5 Kiểm chứng mô hình phần mềm (BMC), 3.6 Mô hình bộ nhớ, 3.7 Sắp xếp mô hình bộ nhớ

## 1. Mục tiêu học tập

- Hiểu nguyên lý **Bounded Model Checking**: mở cuộn (unwind) vòng lặp k bước, mã hóa thành công thức SMT, kiểm tra tồn tại vi phạm.
- Phân biệt rõ **"SAFE up to bound k"** với **"an toàn tuyệt đối"** — giới hạn nền tảng của BMC.
- Tự viết một động cơ BMC đơn giản bằng Z3 (không dùng công cụ có sẵn như CBMC) để hiểu cơ chế bên trong.
- Đối chiếu kết quả kiểm chứng tĩnh (BMC) với hành vi runtime thực tế (AddressSanitizer) trên C.
- Thiết kế và áp dụng **tiền điều kiện (precondition)** để sửa lỗi truy cập mảng ngoài biên.

## 2. Bối cảnh bài toán

`dataset/bmc_programs.json` mô tả 4 chương trình truy cập mảng catalog (kích thước 10): quét đúng, lỗi off-by-one kinh điển, truy cập với offset đầu vào không kiểm tra (tương tự lỗi phân trang - pagination offset), và bản đã sửa với tiền điều kiện.

## 3. Các bước thực hành

### Bước 1 — Tự phân tích từng chương trình trên giấy
Với mỗi mục trong dataset, tự dự đoán SAFE/UNSAFE trước khi chạy công cụ, giải thích lý do bằng lời.

### Bước 2 — Chạy động cơ BMC
```bash
python3 python/bmc_engine.py
```
Đọc kỹ phần "Nhân chứng" (witness) — đây là bộ giá trị SMT solver tìm ra khiến khẳng định an toàn bị vi phạm.

### Bước 3 — Đối chiếu với runtime thực tế (C + ASan)
```bash
make
./c/array_access safe_full_scan
./c/array_access offbyone
./c/array_access symbolic_unsafe 9
./c/array_access symbolic_safe 9
./c/array_access symbolic_safe 3
```
So sánh giá trị `base_index=9` gây lỗi ở `symbolic_unsafe` với "Nhân chứng" mà BMC tìm ra ở Bước 2 cho `BMC-03`.

### Bước 4 — Thử nghiệm giới hạn của BMC
Sửa tạm `unwind_bound` của `BMC-01-SAFE-FULL-SCAN` xuống còn 5 (nhỏ hơn `array_size=10`), chạy lại. Giải thích tại sao BMC vẫn báo SAFE dù chương trình thật (C) không có lỗi ở phần này — và tại sao điều đó **không đủ để kết luận an toàn** nếu vòng lặp thực tế chạy đến 10 bước.

### Bước 5 — Mở rộng: thêm một chương trình mới vào dataset
Thêm `BMC-05` mô tả truy cập `catalog[array_size - i]` (đếm ngược) — có an toàn không? Ở bước unwind nào (nếu có) xảy ra vi phạm?

### Bước 6 — Chạy bộ test tự động
```bash
make test
```

## 4. Yêu cầu hoàn thiện (Deliverables)

1. Bảng dự đoán tay (Bước 1) đối chiếu kết quả công cụ.
2. Giải thích chi tiết thí nghiệm Bước 4 (giới hạn nền tảng của BMC).
3. `BMC-05` tự thêm (Bước 5) kèm giải thích kết quả và bản cài đặt C tương ứng (không bắt buộc phải sửa `array_access.c`, có thể viết hàm mới `symbolic_reverse` mô tả trong báo cáo).
4. Log `make test` toàn bộ PASS.

## 5. Yêu cầu kết quả

- `tests/test_bmc.py` toàn bộ 5 test PASS.
- Giá trị `base_index` trong nhân chứng BMC-03 phải **thực sự** làm chương trình C sụp đổ (crash) khi chạy với ASan — đây là điều kiện bắt buộc để tests pass.

## 6. Sơ đồ luồng dữ liệu (Dataflow)

```
 dataset/bmc_programs.json (dac ta array_size, unwind_bound, symbolic_base, ...)
            │
            ▼ (unwind vong lap, ma hoa SMT tung buoc)
 python/bmc_engine.py  (Z3: Solver + Not(assertion) SAT?)
            │
            ▼
 dataset/bmc_results.json  (SAFE / UNSAFE + nhan chung cu the)
            │
            ▼ (lay nhan chung lam tham so dau vao thuc te)
 c/array_access.c  --gcc -fsanitize=address-->  Bao cao loi runtime (ASan)
            │
            ▼
 tests/test_bmc.py  (doi chieu ket qua tinh <-> dong)
```

## 7. Tài liệu tham khảo
- Đề cương CSE703093 — An toàn phần mềm, Chương 3 (3.5, 3.6, 3.7).
- E. Clarke, D. Kroening, F. Lerda, *A Tool for Checking ANSI-C Programs* (nền tảng của CBMC), TACAS 2004.
