# Lab 3 — Lập trình C An toàn: Quản lý Bộ nhớ & Đánh giá Rủi ro

**Chủ đề xuyên suốt:** SecureSys – Hệ thống Quản lý Truy cập và Giao dịch An toàn
**Module con:** Inventory (Quản lý kho sách thư viện)
**Ánh xạ đề cương:** Chương 2 — 2.1 Đánh giá rủi ro, 2.3 Lập trình C an toàn (Quản lý bộ nhớ)

## 1. Mục tiêu học tập

- Nhận diện và tái hiện 3 lớp lỗi bộ nhớ phổ biến trong C: **Buffer Overflow (CWE-121)**, **Use-After-Free (CWE-416)**, **Memory Leak (CWE-401)**.
- Sử dụng **AddressSanitizer (ASan)** để phát hiện lỗi bộ nhớ tại runtime.
- Sửa lỗi theo các nguyên tắc lập trình C an toàn: `snprintf` thay `strcpy`, đặt con trỏ `NULL` sau `free`, đảm bảo mọi `malloc` có `free` tương ứng.
- Áp dụng một **mô hình đánh giá rủi ro** (risk assessment) đơn giản hóa theo tinh thần CVSS để định lượng và xếp hạng mức độ nghiêm trọng của lỗ hổng, phục vụ ra quyết định ưu tiên khắc phục.
- Hiểu rõ **giới hạn của công cụ**: vì sao lỗi tràn bộ đệm *bên trong* một struct có thể không bị ASan phát hiện, trong khi tràn bộ đệm heap độc lập thì luôn bị bắt.

## 2. Bối cảnh bài toán

Module Inventory quản lý thông tin sách (tiêu đề, tác giả, năm xuất bản). File `c/vulnerable_inventory.c` cố ý chứa 3 lỗi bộ nhớ kinh điển; `c/secure_inventory.c` là bản đã sửa. Cả hai đều hỗ trợ 3 chế độ dòng lệnh: `overflow`, `uaf`, `leak`.

## 3. Các bước thực hành

### Bước 1 — Đọc code, dự đoán lỗi trước khi chạy
Đọc `vulnerable_inventory.c`, với mỗi hàm, dự đoán: lỗi gì, điều kiện nào kích hoạt, hậu quả gì (CWE nào).

### Bước 2 — Biên dịch với AddressSanitizer và tái hiện từng lỗi
```bash
make
./c/vulnerable_inventory overflow "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
./c/vulnerable_inventory uaf
ASAN_OPTIONS=detect_leaks=1 ./c/vulnerable_inventory leak
```
Đọc kỹ output của ASan: địa chỉ lỗi, stack trace, loại lỗi.

### Bước 3 — Thí nghiệm quan trọng: tràn trong struct KHÔNG luôn bị bắt
Sửa tạm `add_book_title_UNSAFE` (tràn trong struct `Book`) để nhận input dài ~35 ký tự và chạy — so sánh với `add_book_title_UNSAFE_HEAP`. Giải thích trong báo cáo **vì sao** ASan không luôn phát hiện lỗi loại thứ nhất (gợi ý: redzone chỉ đặt ở biên vùng cấp phát, không đặt giữa các field của cùng một struct).

### Bước 4 — Đối chiếu bản đã sửa
```bash
./c/secure_inventory overflow "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
./c/secure_inventory uaf
ASAN_OPTIONS=detect_leaks=1 ./c/secure_inventory leak
```
Không có lỗi nào được báo. Với `overflow`, quan sát chuỗi bị **cắt bớt (truncated)** thay vì tràn.

### Bước 5 — Đánh giá rủi ro định lượng
```bash
make risk-report
```
Đọc `dataset/vulnerabilities.json`, hiểu ý nghĩa từng trường (Attack Vector, Attack Complexity, Privileges Required, User Interaction, C/I/A Impact) và cách `python/risk_assessor.py` tính điểm.

### Bước 6 — Bổ sung một lỗ hổng mới vào dataset
Thêm 1 mục `VULN-005` mới (ví dụ: format string bug) vào `dataset/vulnerabilities.json` với các thuộc tính tự đánh giá, chạy lại Bước 5, giải thích vị trí xếp hạng của nó.

### Bước 7 — Chạy bộ test tự động
```bash
make test
```

## 4. Yêu cầu hoàn thiện (Deliverables)

1. Bảng liệt kê 3 lỗi (địa chỉ, loại ASan report, stack trace rút gọn).
2. Giải thích bằng lời + ví dụ cụ thể cho hiện tượng ở Bước 3 (tràn trong struct không bị bắt).
3. Diff code sửa lỗi tương ứng của bạn (nếu được yêu cầu tự sửa một hàm không có sẵn bản mẫu).
4. Báo cáo đánh giá rủi ro đầy đủ 5 lỗ hổng (bao gồm `VULN-005` tự thêm), có thứ tự ưu tiên khắc phục.
5. Log `make test` toàn bộ PASS.

## 5. Yêu cầu kết quả

- ASan phải phát hiện đủ 3/3 lỗi trên bản vulnerable, và xác nhận 0/3 lỗi trên bản secure.
- `tests/test_memory_safety.py` toàn bộ 8 test PASS.
- Kết quả đánh giá rủi ro phải khớp với `dataset/expected_risk_baseline.json` (dung sai 0.05 điểm).

## 6. Sơ đồ luồng dữ liệu (Dataflow)

```
 c/vulnerable_inventory.c  --gcc -fsanitize=address-->  Binary co ASan
            │                                                 │
            │ (chay 3 kich ban: overflow / uaf / leak)         ▼
            │                                    Bao cao loi runtime (ASan)
            ▼
 dataset/vulnerabilities.json (mo ta CWE + thuoc tinh CVSS-lite)
            │
            ▼
 python/risk_assessor.py  (tinh Exploitability + Impact subscore)
            │
            ▼
 dataset/risk_assessment_results.json  <--so sanh-->  dataset/expected_risk_baseline.json
            │
            ▼
 tests/test_memory_safety.py  (doi chieu tu dong ASan + diem rui ro)
```

## 7. Tài liệu tham khảo
- Đề cương CSE703093 — An toàn phần mềm, Chương 2 (2.1, 2.3).
- CWE-121, CWE-416, CWE-401, CWE-476 (cwe.mitre.org).
- FIRST.org — Common Vulnerability Scoring System (CVSS) v3.1 Specification.
