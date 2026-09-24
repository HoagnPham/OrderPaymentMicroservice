# c/ch2_memory -- Mã nguồn Chương 2 (Lập trình C an toàn)

| File | Nội dung | Dùng trong slide |
|---|---|---|
| `vulnerable.c` | 7 hàm minh họa lỗi bộ nhớ có chủ đích (buffer overflow, format string, integer overflow, UAF, double-free, memory leak, NULL deref, padding leak) | 2.2, 2.3, 2.4 |
| `fixed.c` | Phiên bản đã vá tương ứng từng hàm trong `vulnerable.c` | 2.3 |
| `safe_string.h/.c` | Thư viện chuỗi an toàn (mini-project gợi ý cuối chương) | 2.3, 2.6 |
| `test_safe_string.c` | Test cho `safe_string`, bao gồm edge case (chuỗi rỗng, NULL, chuỗi dài hơn buffer) | 2.6 |
| `Makefile` | Quy trình QA: warnings → cppcheck → ASan/UBSan → Valgrind | 2.5 |

## Chạy thử

```bash
make check         # compiler warnings + cppcheck trên toàn bộ file
make test           # build + chạy test_safe_string với ASan/UBSan
make test-valgrind  # (cần cài valgrind) chạy lại dưới Valgrind
```

`vulnerable.c` KHÔNG được build tự động trong `make all`/`make test` (chỉ dùng cho `make check`) vì các hàm `bug_*` bên trong cố ý gây lỗi bộ nhớ nghiêm trọng (double-free, UAF) -- chỉ nên phân tích tĩnh, không nên thực thi trực tiếp.
