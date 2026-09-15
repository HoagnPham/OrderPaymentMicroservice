# Lab 4 — Cấu trúc Dữ liệu Động & Công cụ Phân tích Tĩnh

**Chủ đề xuyên suốt:** SecureSys – Hệ thống Quản lý Truy cập và Giao dịch An toàn
**Module con:** Inventory (Catalog sách dạng danh sách liên kết)
**Ánh xạ đề cương:** Chương 2 — 2.2 Cấu trúc động, 2.5 Công cụ phân tích tĩnh tìm lỗi bảo mật

## 1. Mục tiêu học tập

- Cài đặt và thao tác trên danh sách liên kết đơn (singly linked list) cấp phát động trong C.
- Sử dụng công cụ phân tích tĩnh **cppcheck** để phát hiện lỗi mà **không cần chạy chương trình**.
- Đọc hiểu báo cáo XML của công cụ phân tích tĩnh, phân biệt mức độ `error` / `warning` / `style` / `information`.
- Hiểu rõ **giới hạn** của phân tích tĩnh: vì sao cppcheck phát hiện tốt "uninitialized variable" nhưng khó phát hiện memory leak có luồng điều khiển phức tạp — từ đó thấy được vai trò bổ sung của phân tích động (ASan ở Lab 3, fuzzing ở Lab 8).

## 2. Bối cảnh bài toán

`booklist_v1_buggy.c` cài đặt catalog sách bằng linked list, chứa 3 lỗi tinh vi: thiếu kiểm tra `malloc` trả về NULL, thiếu `free()` khi xóa node, và biến đếm không được khởi tạo. `booklist_v2_fixed.c` là bản đã sửa.

## 3. Các bước thực hành

### Bước 1 — Tự tìm lỗi bằng mắt (code review) trước khi chạy công cụ
Đọc `booklist_v1_buggy.c`, đánh dấu nghi ngờ tại mỗi hàm.

### Bước 2 — Biên dịch và chạy thử cả 2 phiên bản
```bash
make
./c/booklist_v1
./c/booklist_v2
```
So sánh output — lỗi "biến chưa khởi tạo" thể hiện rõ qua số lượng sách vô lý ở bản buggy.

### Bước 3 — Chạy cppcheck và đọc báo cáo
```bash
make cppcheck-report
```
Đối chiếu các finding với dự đoán ở Bước 1. Với mỗi finding, tra cứu mã CWE tương ứng trên cwe.mitre.org.

### Bước 4 — Tìm lỗi mà cppcheck KHÔNG phát hiện được
Bản buggy còn lỗi memory leak (`list_remove_by_title` thiếu `free(cur)`), nhưng cppcheck với cấu hình mặc định không luôn báo lỗi này. Thử bật thêm `--enable=all --inconclusive` (đã bật sẵn trong script) và kiểm tra output đầy đủ (không lọc) — quan sát cppcheck có gợi ý gì khác không.

### Bước 5 — Sửa lỗi thủ công (không nhìn bản mẫu) rồi so sánh
Copy `booklist_v1_buggy.c` sang file mới, tự sửa từng lỗi, chạy lại cppcheck cho đến khi 0 finding bảo mật, rồi so sánh với `booklist_v2_fixed.c`.

### Bước 6 — Chạy bộ test tự động
```bash
make test
```

## 4. Yêu cầu hoàn thiện (Deliverables)

1. Bảng đối chiếu: lỗi tự tìm bằng mắt (Bước 1) vs. lỗi cppcheck phát hiện (Bước 3) — chỉ ra lỗi nào công cụ bỏ sót và giải thích tại sao.
2. Bản tự sửa lỗi (Bước 5) kèm log cppcheck xác nhận 0 finding bảo mật.
3. Trả lời: nếu phải chọn CHỈ MỘT công cụ (cppcheck tĩnh hoặc AddressSanitizer động — Lab 3) để đưa vào pipeline CI/CD, bạn chọn công cụ nào? Vì sao? (Gợi ý: không có đáp án đúng tuyệt đối — hãy lập luận theo trade-off tốc độ / độ phủ / yêu cầu chạy chương trình.)

## 5. Yêu cầu kết quả

- `tests/test_static_analysis.py` toàn bộ 5 test PASS.
- Bản fixed đạt 0 finding cppcheck mức `error`/`warning`.
- Bản buggy có ít nhất 2 finding mức `error`/`warning` bị phát hiện.

## 6. Sơ đồ luồng dữ liệu (Dataflow)

```
 c/booklist_v1_buggy.c , c/booklist_v2_fixed.c
            │
            ▼ (khong bien dich, chi phan tich AST/CFG)
 cppcheck --enable=all --inconclusive --xml
            │
            ▼
 python/static_analysis_runner.py  (parse XML, loc theo severity)
            │
            ▼
 dataset/cppcheck_comparison_results.json  <--so sanh-->  expected_cppcheck_baseline.json
            │
            ▼
 tests/test_static_analysis.py (doi chieu tu dong)
```

## 7. Tài liệu tham khảo
- Đề cương CSE703093 — An toàn phần mềm, Chương 2 (2.2, 2.5).
- cppcheck.sourceforge.io — tài liệu chính thức.
- CWE-457 (Use of Uninitialized Variable), CWE-401 (Memory Leak), CWE-476 (NULL Pointer Dereference).
