# c/ch3_static -- Mã nguồn Chương 3 (Phân tích tĩnh)

`bmc_demo.c` chứa 5 cặp hàm minh họa dùng cho `atpm.ch3_static_analysis.run_cbmc()` và slide mục 3.5:

| Hàm | Mục đích | Cờ CBMC cần dùng |
|---|---|---|
| `check_sum` | Vòng lặp giới hạn rõ ràng, BMC sound với unwind đủ | (mặc định) |
| `check_overflow` / `check_overflow_fixed` | Off-by-one gây tràn mảng / bản đã sửa | `--bounds-check --pointer-check` |
| `check_divide` / `check_divide_guarded` | Chia cho 0 chưa/đã có `__CPROVER_assume` | `--div-by-zero-check` |
| `check_heap` | malloc/free hợp lệ | `--bounds-check --pointer-check --malloc-may-fail --malloc-fail-null` |

Chạy thử trực tiếp:
```bash
cbmc --unwind 12 --unwinding-assertions --bounds-check --pointer-check \
     --function check_overflow bmc_demo.c
```
