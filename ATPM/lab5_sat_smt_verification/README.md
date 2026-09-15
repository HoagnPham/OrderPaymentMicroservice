# Lab 5 — Phân tích Tĩnh Nâng cao: SAT/SMT Solving

**Chủ đề xuyên suốt:** SecureSys – Hệ thống Quản lý Truy cập và Giao dịch An toàn
**Module con:** Auth + Access Control (kiểm chứng điều kiện logic)
**Ánh xạ đề cương:** Chương 3 — 3.1 Giới thiệu kiểm chứng và thẩm định phần mềm, 3.4 SAT and SMT solving

## 1. Mục tiêu học tập

- Hiểu bài toán SAT (satisfiability) và mở rộng SMT (Satisfiability Modulo Theories).
- Áp dụng kỹ thuật kiểm chứng tương đương logic: `P ≡ Q ⟺ UNSAT(P XOR Q)`.
- Dùng thư viện **Z3** (Python) để mã hóa điều kiện chương trình thành công thức SMT và kiểm tra.
- Đọc hiểu **phản ví dụ (counterexample)** do solver trả về — công cụ debug mạnh hơn nhiều so với test case đơn lẻ.
- Hiểu vì sao SAT/SMT solving vượt trội so với liệt kê vét cạn (brute-force) khi số biến lớn.
- Nhận diện lỗi bảo mật kinh điển qua kiểm chứng logic: lỗi off-by-one, backdoor ẩn trong điều kiện "tối ưu hóa".

## 2. Bối cảnh bài toán

Trong quá trình bảo trì, lập trình viên thường "viết lại cho gọn" các điều kiện phức tạp — nhưng bản viết lại có thể **không tương đương logic** với bản gốc, tạo ra lỗi bảo mật nghiêm trọng (ví dụ: off-by-one tại ngưỡng khóa tài khoản, hoặc chèn thêm một nhánh bypass ẩn). `dataset/condition_pairs.json` chứa 6 cặp điều kiện (original / optimized) trích từ các module đã xây dựng ở Lab 1–2.

## 3. Các bước thực hành

### Bước 1 — Tự đoán bằng bảng chân trị (truth table) trên giấy
Với COND-01 và COND-04, tự vẽ bảng chân trị 2^n dòng, xác định tương đương hay không TRƯỚC khi chạy công cụ.

### Bước 2 — Chạy kiểm chứng SMT
```bash
python3 python/smt_equivalence_checker.py
```
Đọc kỹ **phản ví dụ (counterexample)** ở COND-02 và COND-05 — đây chính là dữ liệu đầu vào cụ thể làm lộ lỗi.

### Bước 3 — Đối chiếu bằng liệt kê vét cạn (brute-force) trong C
```bash
make
./c/brute_force_checker
```
So sánh kết quả với Bước 2. Vì sao COND-02 (biến `Int`, miền vô hạn) **không thể** kiểm chứng bằng liệt kê vét cạn nhưng SMT solver vẫn giải được?

### Bước 4 — Phân tích lỗi bảo mật COND-05 (quan trọng nhất)
Đọc kỹ phản ví dụ của COND-05: `{authenticated: False, is_admin: True, bypass_flag: True}`. Giải thích bằng lời đây là dạng lỗi bảo mật gì (gợi ý: CWE-288 Authentication Bypass Using an Alternate Path).

### Bước 5 — Tự thêm một cặp điều kiện mới
Thêm `COND-07` vào dataset, mô phỏng một điều kiện thực tế từ module Order (Lab 7) — ví dụ điều kiện áp dụng giảm giá. Chạy lại và giải thích kết quả.

### Bước 6 — Chạy bộ test tự động
```bash
make test
```

## 4. Yêu cầu hoàn thiện (Deliverables)

1. Bảng chân trị tự vẽ tay (Bước 1), đối chiếu kết quả Z3.
2. Giải thích chi tiết (≥ 5 câu) cho lỗ hổng COND-05, kèm CWE tương ứng.
3. Cặp điều kiện `COND-07` tự thêm, kèm phân tích kết quả.
4. Trả lời: SMT solving thuộc lớp bài toán **NP-complete** (SAT) hoặc phức tạp hơn (SMT). Vì sao trên thực tế các solver hiện đại (Z3, CVC5) vẫn giải được các bài toán "khó" này trong thời gian hợp lý cho phần lớn trường hợp thực tế?

## 5. Yêu cầu kết quả

- `tests/test_smt_verification.py` toàn bộ 3 test PASS.
- Kết quả brute-force (C) và SMT (Python/Z3) phải **nhất quán tuyệt đối** trên các điều kiện boolean thuần túy.

## 6. Sơ đồ luồng dữ liệu (Dataflow)

```
 dataset/condition_pairs.json  (P original, Q optimized, ky vong)
            │
            ├──────────────────────────────┐
            ▼                               ▼
 python/smt_equivalence_checker.py   c/brute_force_checker.c
 (ma hoa Z3, kiem tra UNSAT(P XOR Q))  (liet ke vet can 2^n to hop)
            │                               │
            ▼                               ▼
 dataset/smt_verification_results.json  <--doi chieu-->  stdout C
            │
            ▼
 tests/test_smt_verification.py (kiem tra tinh nhat quan + phan vi du hop le)
```

## 7. Tài liệu tham khảo
- Đề cương CSE703093 — An toàn phần mềm, Chương 3 (3.1, 3.4).
- L. de Moura, N. Bjørner, *Z3: An Efficient SMT Solver*, TACAS 2008.
- CWE-288 — Authentication Bypass Using an Alternate Path or Channel.
