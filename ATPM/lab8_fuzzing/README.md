# Lab 8 — Kiểm thử Mờ: Hộp đen (Black-box) và Hộp trắng (White-box) Fuzzing

**Chủ đề xuyên suốt:** SecureSys – Hệ thống Quản lý Truy cập và Giao dịch An toàn
**Module con:** Order Processing / kênh lệnh quản trị ẩn (ADMIN debug channel)
**Ánh xạ đề cương:** Chương 4 — 4.5 Kiểm thử hộp đen (black-box fuzzing), 4.6 Kiểm thử hộp trắng (white-box fuzzing)

## 1. Mục tiêu học tập

- Hiểu nguyên lý fuzzing: sinh đầu vào tự động (thường qua đột biến — mutation) để tìm input gây crash.
- Cài đặt fuzzer **hộp đen**: không dùng thông tin nội bộ chương trình, chỉ quan sát crash/không-crash.
- Cài đặt fuzzer **hộp trắng (coverage-guided)**: dùng phản hồi độ phủ mã (từ `gcov`) để **dẫn dắt** tìm kiếm về phía các nhánh mã mới — mô phỏng đơn giản ý tưởng cốt lõi của AFL.
- Đo lường định lượng: **cùng ngân sách** (số lần thử), fuzzer nào tìm crash nhanh hơn, và vì sao.
- Hiểu vì sao "backdoor" ẩn sau điều kiện kích hoạt hẹp (magic string) là mục tiêu khó cho fuzzing hộp đen thuần túy.

## 2. Bối cảnh bài toán

`c/fuzz_target.c` chứa một "kênh lệnh quản trị" (`ADMIN:` prefix) với lỗi tràn bộ đệm CWE-121, nhưng lỗi này chỉ kích hoạt khi đầu vào khớp **chính xác** chuỗi `ADMIN:X` ở đầu, theo sau là payload đủ dài. Đây là mẫu lỗi thực tế: các "easter egg"/kênh debug bị bỏ quên trong code production là nguồn gốc của nhiều lỗ hổng nghiêm trọng.

## 3. Các bước thực hành

### Bước 1 — Xác nhận lỗi thủ công trước khi fuzzing
```bash
make
./c/fuzz_target "ADMIN:X$(python3 -c 'print("A"*30)')"
```
Quan sát báo cáo AddressSanitizer.

### Bước 2 — Chạy fuzzer hộp đen với ngân sách thấp
```bash
make fuzz-blackbox
```
Ghi lại số crash tìm được sau 3000 lần thử (thường là **0** — vì xác suất một chuỗi ngẫu nhiên khớp chính xác "ADMIN:" + "X" + đủ dài là cực thấp).

### Bước 3 — Chạy fuzzer hộp trắng (coverage-guided) với ngân sách thấp hơn
```bash
make fuzz-whitebox
```
Đọc log "sự kiện coverage mới" — quan sát cách fuzzer "khám phá" tuần tự: trước tiên tới nhánh `ADMIN:`, rồi tới nhánh `ADMIN:X`, rồi mới tìm ra input gây crash — đây chính là cơ chế feedback loop.

### Bước 4 — So sánh công bằng (cùng ngân sách)
Chạy cả 2 fuzzer với **cùng số lần thử** (ví dụ 500–1000), ghi lại bảng so sánh số crash tìm được.

### Bước 5 — Chứng minh hộp đen KHÔNG PHẢI "không bao giờ" tìm ra, chỉ là "chậm hơn nhiều"
Tăng ngân sách hộp đen lên 5000+ lần thử, quan sát tại một lúc nào đó nó cũng có thể tìm ra (do có "từ điển" (dictionary) hỗ trợ). Viết nhận xét: vì sao trong thực tế (không có dictionary hỗ trợ, input phức tạp hơn nhiều), khoảng cách này còn lớn hơn nữa.

### Bước 6 — Chạy bộ test tự động
```bash
make test
```

## 4. Yêu cầu hoàn thiện (Deliverables)

1. Log đầy đủ Bước 2, 3 (kèm số lần thử và crash tìm được).
2. Bảng so sánh công bằng (Bước 4): cùng ngân sách, kết quả 2 fuzzer.
3. Kết quả thí nghiệm mở rộng ngân sách hộp đen (Bước 5) và nhận xét.
4. Trả lời: nếu chương trình mục tiêu có kênh debug với điều kiện kích hoạt là **10 ký tự** cụ thể (thay vì 6), ước lượng fuzzing hộp đen thuần túy (không dictionary) cần trung bình bao nhiêu lần thử để tìm ra (giả sử bảng chữ cái 68 ký tự in được)? So sánh với ngân sách thực tế khả thi trong CI/CD (vài phút chạy).

## 5. Yêu cầu kết quả

- `tests/test_fuzzing.py` toàn bộ 4 test PASS (bao gồm điều kiện: fuzzer hộp trắng PHẢI tìm được crash trong 500 lần thử, fuzzer hộp đen KHÔNG được tìm thấy trong 1000 lần thử — với seed cố định để tái lập).
- File crash tìm được phải được lưu lại trong thư mục `crashes/` kèm nội dung input gây lỗi.

## 6. Sơ đồ luồng dữ liệu (Dataflow)

```
 seed_corpus/*.txt  (dau vao hat giong ban dau)
            │
            ├─────────────────────────────┐
            ▼                              ▼
 python/blackbox_fuzzer.py         python/whitebox_fuzzer.py
 (dot bien ngau nhien,              (dot bien + do gcov coverage
  KHONG dung feedback)               moi lan chay, uu tien seed
            │                        mo ra nhanh moi)
            ▼                              ▼
 c/fuzz_target (--fsanitize=address --coverage)
            │                              │
            ▼                              ▼
 crashes/blackbox_crash_*.txt      crashes/whitebox_crash_*.txt
            │                              │
            └──────────────┬───────────────┘
                            ▼
              dataset/fuzzing_comparison_results.json
                            │
                            ▼
              tests/test_fuzzing.py (doi chieu tu dong)
```

## 7. Tài liệu tham khảo
- Đề cương CSE703093 — An toàn phần mềm, Chương 4 (4.5, 4.6).
- M. Zalewski, *American Fuzzy Lop (AFL) — Technical Whitepaper*.
- CWE-121 (Stack-based Buffer Overflow), CWE-489 (Active Debug Code).
