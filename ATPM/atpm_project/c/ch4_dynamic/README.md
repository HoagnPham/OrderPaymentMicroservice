# c/ch4_dynamic -- Mã nguồn Chương 4 (Phân tích động)

`fuzz_target.c` là target dùng cho `atpm.ch4_dynamic_analysis.fuzzing`: hàm `check_magic()` chỉ crash (NULL deref) khi input CHÍNH XÁC bằng 4 byte "FUZZ".

Biên dịch với coverage:
```bash
gcc --coverage -O0 -c fuzz_target.c -o fuzz_target.o
gcc --coverage -o fuzz_target fuzz_target.o
```

Dùng làm target thực nghiệm so sánh black-box vs coverage-guided fuzzing (xem slide mục 4.5-4.6):
```bash
cd ../../python
python3 atpm/ch4_dynamic_analysis/fuzzing.py
```
