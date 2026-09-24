# ATPM Project -- CSE703093 An toàn phần mềm

Project xuyên suốt học phần, mở rộng dần qua từng chương. Gồm hai phần song song:

- `python/atpm/` -- bộ công cụ Python (mô hình hóa, phân tích, kiểm chứng, fuzzing)
- `c/` -- mã nguồn C minh họa (lỗ hổng và bản vá) dùng làm dữ liệu đầu vào cho công cụ Python

## Kiến trúc theo chương

| Chương | Module Python | Thư mục C | Chủ đề |
|---|---|---|---|
| 1 | `atpm.ch1_concepts` | -- | Mô hình hóa khái niệm an toàn, Kripke structure |
| 2 | `atpm.ch2_secure_c` | `c/ch2_memory` | Phân tích rủi ro bộ nhớ, wrapper kiểm tra secure-C |
| 3 | `atpm.ch3_static_analysis` | `c/ch3_static` | SAT/SMT (Z3), Bounded Model Checking mini |
| 4 | `atpm.ch4_dynamic_analysis` | `c/ch4_dynamic` | Code coverage, black/white-box fuzzing |

## Cài đặt

```bash
cd python
python3 -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
```

## Trạng thái triển khai (cập nhật theo từng gói)

- [x] Gói 0: khung project + `ch1_concepts` (mô hình Kripke, kiểm tra thuộc tính an toàn cơ bản)
- [x] Gói 1: mở rộng ch1 (bài tập, ví dụ đầy đủ) -- 100 slide Chương 1
- [x] Gói 2: `ch2_secure_c` + mẫu C lỗ hổng/đã vá -- 100 slide Chương 2, `safe_string` lib, Cppcheck wrapper
- [x] Gói 3: `ch3_static_analysis` (Z3 SAT/SMT, CBMC wrapper) -- 104 slide Chương 3, 10 test pytest
- [x] Gói 4: `ch4_dynamic_analysis` (gcov coverage, black-box + coverage-guided fuzzer) -- 100 slide Chương 4, 3 test pytest
- [ ] Gói 5: hướng dẫn bài tập lớn dùng toàn bộ project này làm khung

**Tổng cộng: 21/21 test pytest pass qua cả 4 chương** (`cd python && pytest tests/ -v`)

Mỗi chương sẽ được hoàn thiện dần trong các gói tiếp theo, cùng slide bài giảng tương ứng.
