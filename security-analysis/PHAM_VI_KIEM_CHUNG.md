# Phạm vi kiểm chứng và tiêu chí nghiệm thu

Tài liệu này chốt **cái gì được coi là đã xong**, **cái gì chỉ là tuỳ thời gian** và
**cái gì nằm ngoài phạm vi**. Mọi kết luận trong báo cáo phải quy được về một dòng
trong tài liệu này và một tệp evidence tương ứng.

Nguyên tắc: chỉ kết luận trong đúng kịch bản đã chạy. Một mô hình đúng không chứng
minh source đúng; một test PASS không chứng minh mọi interleaving; một hash lúc đóng
gói không chứng minh source tại thời điểm chạy lịch sử.

## 1. Phạm vi bắt buộc

Các mục dưới đây phải chạy lại được trên máy demo và phải có evidence lưu trong
`security-analysis/results/`.

| # | Hạng mục | Tiêu chí đạt | Evidence |
| --- | --- | --- | --- |
| B1 | Build | `dotnet build OrderPaymentMicroservice.slnx --no-restore` không lỗi | log build |
| B2 | Unit/regression | 149/149 PASS, 0 fail, 0 skip | `application_tests.results.json` |
| B3 | Integration | 35/35 PASS, 0 fail, 0 skip | `application_tests.results.json` |
| B4 | Recovery model | 15/15 property PASS; 6/6 mutation bị phát hiện | `order_payment_recovery_model.results.json` |
| B5 | Liveness | Raw `AG AF terminal = false`; progress-only `= true`; audit PASS | `liveness_audit.results.json` |
| B6 | SMT payment | 7/7 query đúng verdict kỳ vọng | `payment_verification.results.json` |
| B7 | SMT inventory | 3/3 query đúng verdict kỳ vọng | `inventory_verification.results.json` |
| B8 | C memory | 5 module BUGGY/FIXED; 22/22 case động PASS dưới ASan/UBSan | `c-memory/evidence/dynamic-analysis.json` |
| B9 | Fuzz | 4 campaign, seed cố định, invariant giữ; coverage chỉ trên target | `application_fuzzing.results.json` |
| B10 | Coverage validator | Branch `InputValidator` ≥ 90% (hiện 97,22%); `MoneyAmounts` 100% | `coverage_summary.json` |
| B11 | Crash/reconnect | Charge và refund phục hồi không charge/refund/release lần hai | `postgres_reconnect_*.results.json`, `process_restart.results.json`, `refund_restart.results.json` |
| B12 | Vận hành | Backup/restore và load probe có oracle, có limitations | `backup_restore.results.json`, `load_probe.results.json` |
| B13 | Giao diện thật | Chrome + PostgreSQL + API thật hoàn tất flow, không lỗi JS chưa bắt | `application_smoke.results.json` |
| B14 | Truy vết | Mỗi requirement có test/check và mức evidence | `traceability_matrix.csv` |
| B15 | Đóng gói | Manifest có base commit, dirty flag và hash source/evidence | `evidence_manifest.json` |
| B16 | Workbook ATPM | 10 sheet; 18 state; 10 CTL (9 đúng, 1 vi phạm cố ý); 6 cặp mutex; có Python `divide` + `magic_value` | `teacher_spec_verification.json`; GUI Windows vẫn là xác nhận cuối |
| B17 | C BMC | CBMC 10/10 case: 5 BUGGY FAILED đúng lỗi, 5 FIXED SUCCESSFUL; bound unwind=64, input 48 byte | `c-memory/evidence/bmc-analysis.json` |
| B18 | C Valgrind | Memcheck 10/10 case: 5 BUGGY có lỗi đúng loại, 5 FIXED sạch | `c-memory/evidence/valgrind-analysis.json` |
| B19 | End-to-end | Chuỗi kiểm chứng nhanh PASS, artifact check PASS; GUI Windows vẫn BLOCKED | `end_to_end.results.json` |
| B20 | C SAT/CNF/DRAT | CBMC xuất CNF DIMACS cho 10 case: 5 BUGGY SAT có model thỏa CNF, 5 FIXED UNSAT; 5 DRAT proof `VERIFIED`, 5 UNSAT core kiểm lại UNSAT; bound unwind=64 | `c-memory/evidence/sat-pipeline.json`; mẫu `c-memory/evidence/sat/` |

## 2. Tiêu chí chất lượng của evidence

Một tệp evidence chỉ được dùng để kết luận khi:

1. Có `run_at_utc` (hoặc `times` của TRX) và khớp với lần chạy được nhắc trong báo cáo.
2. Có `passed`/`failed` ở mức tổng, không chỉ ở mức từng case.
3. Có `limitations` nêu rõ môi trường giả lập (DurableFake, một máy, một cluster local).
4. Không trộn kết quả của filtered test vào summary toàn suite.
5. Đọc được bằng máy (JSON/CSV/TRX), không phải ảnh chụp màn hình.

Số liệu chép tay trong `.md`/`.xlsx` là **bản sao**, không phải nguồn. Khi lệch, lấy
JSON/TRX làm chuẩn và sửa bản sao.

## 3. Việc tuỳ thời gian (làm được thì tốt, không dùng để kết luận)

| # | Hạng mục | Vì sao chưa bắt buộc | Nếu làm thì cần |
| --- | --- | --- | --- |
| T1 | BMC/CBMC trên C | Đã thực hiện cho 5 module C (B17); mở rộng sang C# vẫn ngoài scope | CBMC + bound + unwinding assertion |
| T2 | SAT/CNF/DIMACS độc lập | Đã thực hiện cho 5 module C (B20); vẫn là bounded CNF, không phải proof vô hạn/C# | CBMC DIMACS + CaDiCaL + drat-trim + core |
| T3 | Coverage-guided fuzz | **Đã có bản opt-in cho C fixture**: `run_c_coverage_fuzz.py` dùng GCC `trace-pc`, shared edge map, fork/input và queue theo coverage mới; seed `20261022`, 20.000 mutation, 81 → 148 map entry. **Chưa cho ASP.NET**; .NET vẫn structured fixed-seed, HTTP dùng semantic feedback | Instrumentation C#/HTTP, source-level edge/path, corpus/minimization nâng cao |
| T4 | HTTP black-box fuzz | **Đã có bản opt-in**: `http_fuzz.py` chạy executable thật + PostgreSQL local, seed `20261022`, 500 mutation/601 request; không phải coverage-guided | RESTler/Schemathesis hoặc mở rộng mutation/oracle/replay |
| T5 | Valgrind/clang-tidy/clang analyzer | Valgrind đã thực hiện (B18); clang-tidy/analyzer chưa có trên PATH | log từng công cụ, gồm cả false negative |
| T6 | Taint analysis tự động | Hiện lập luận thủ công theo luồng dữ liệu | Semgrep/CodeQL rule |
| T7 | So sánh Cppcheck với sanitizer | Đã ghi nhận Cppcheck bỏ sót 4/5 | bảng đối chiếu theo từng bug |
| T8 | Lặp load/crossover | Một mẫu ngắn chưa đủ suy thống kê | nhiều lượt, warmup, p95/p99 |
| T9 | Browser network fuzz | **Đã có bản CDP opt-in** (8 scenario, real API/PostgreSQL); chưa phải proxy/WAN/mobile | proxy thật, network partition, login/cart/payment fault matrix |

## 4. Ngoài phạm vi (không được claim)

- Provider thanh toán thật, webhook thật, tiền thật.
- Production SLA, HA, failover, network partition, power loss.
- Shared rate limiter (hiện process-local), Kubernetes, DB RBAC, retention job.
- Chứng minh mọi interleaving hoặc toàn bộ API an toàn.
- Chứng minh C# managed an toàn bộ nhớ; các module C chỉ là demo học tập.
- Kết luận từ một filtered test thay cho toàn suite.
- Chạy GUI `SpecVerificationLab.exe`/`SecLabFramework.exe` trên Windows trong môi trường Linux hiện tại. Đây là mục **BLOCKED về môi trường**, không phải PASS: checker độc lập đã kiểm schema/CTL/mutex, nhưng lần nạp Excel và chạy 4 chương bằng EXE của giảng viên vẫn phải thực hiện thủ công theo hướng dẫn trong `KICH_BAN_THUYET_TRINH_VA_DEMO.md` mục 2.

## 5. Bốn chương học phần và mức evidence

| Chương | Nội dung | Mức evidence hiện có | Giới hạn phải nói |
| --- | --- | --- | --- |
| 1 | Kripke, CTL/LTL, model checking | Model + checker duyệt toàn bộ 14 state; workbook thêm cụm khóa 4 state; TLA+ có fairness | Chưa có fair-CTL đầy đủ; một số state là trừu tượng |
| 2 | C memory safety, static analysis | 5 module, ASan/UBSan 22/22; CBMC 10/10; Valgrind 10/10; CNF/DRAT 10/10 | Cppcheck false negative 4/5; chưa clang-tidy; CBMC/DRAT có bound 64/48 |
| 3 | SAT/SMT/BMC | 10 query Z3 đúng verdict; CBMC bounded; CNF DIMACS + 5 DRAT proof + 5 UNSAT core | Chưa BMC cho C#; proof chỉ cho CNF bounded |
| 4 | Fuzzing, coverage | 4 campaign seed `20261009`; 5 target; line 91,05%/branch 97,50%; HTTP black-box seed `20261022`, 601 request, 0 5xx/0 invariant violation; browser CDP 8 scenario, 0 uncaught JS; C fixture coverage-guided seed `20261022`, 81→148 trace-pc map entry | App chưa code-coverage-guided/path-edge; C fixture không phải ASP.NET; browser mới fault products fetch, chưa proxy/WAN; HTTP fuzz chỉ local và bounded |

## 6. Cách xác nhận nhanh

```bash
# Toàn suite (unit + integration)
dotnet test OrderPaymentMicroservice.slnx --no-restore --nologo \
  --logger 'trx;LogFilePrefix=verification' \
  --results-directory security-analysis/results/dotnet \
  --blame-hang-timeout 90s

# Mô hình, SMT, liveness, C depth
python3 security-analysis/run_all_checks.py

# Thêm CNF/DIMACS/DRAT (cần cadical + drat-trim trên PATH)
python3 security-analysis/run_all_checks.py --with-sat

# Hoặc chạy riêng
python3 security-analysis/verify_recovery_model.py
python3 security-analysis/audit_liveness.py
python3 security-analysis/verify_payment_smt.py
python3 security-analysis/verify_inventory_smt.py
make -C security-analysis/c-memory bmc-check
make -C security-analysis/c-memory valgrind-check
make -C security-analysis/c-memory sat-check

# Fuzz + coverage target
python3 security-analysis/run_fuzzing.py

# Đóng gói lại evidence và kiểm tra manifest
python3 security-analysis/package_evidence.py
```

Chạy tuần tự; không chạy fuzz/coverage song song với một lượt `dotnet test` khác vì
dùng chung assembly.

## 7. Điều kiện môi trường

| Thành phần | Dùng cho | Ghi chú |
| --- | --- | --- |
| .NET 10 SDK | build/test | đã dùng 10.0.112 |
| PostgreSQL 16 local | integration/reconnect/backup | cần `pg_dump`, `pg_restore`, `pg_ctl` |
| Python 3.10+ | script phân tích | đã dùng 3.12 |
| Z3 | SMT | phải có trên `PATH` |
| GCC + ASan/UBSan | C memory | GCC 13.3.0 |
| CBMC | BMC cho 5 module C | đã chạy; bound unwind=64 |
| CaDiCaL | Giải CNF DIMACS + sinh DRAT | đã dùng 1.7.3; `sudo apt install cadical` |
| drat-trim | Kiểm DRAT + xuất UNSAT core | build từ source; không có `--version` |
| Valgrind | Memcheck cho 5 module C | đã chạy; build không sanitizer |
| Cppcheck 2.13.0 | static analysis C | chỉ dùng để đối chiếu, không phải oracle |
| Chrome/Chromium | live smoke | một desktop browser |

Thiếu PostgreSQL thì B3/B11/B12 không chạy được; thiếu Z3 thì B6/B7 không chạy được;
thiếu CBMC/Valgrind thì B17/B18 không chạy lại được; thiếu CaDiCaL/drat-trim thì B20
không tái sinh được (default runner vẫn kiểm evidence đã lưu). Trong trường hợp đó phải
nói rõ là "không chạy lại", không được đọc evidence cũ như kết quả mới.

## 8. Liên hệ với tài liệu khác

- Hợp đồng chi tiết của gateway/timeout/audit/idempotency: `IMPLEMENTATION_CONTRACTS.md`.
- Cách vận hành, backup/restore, health/status: `OPERATIONS.md`.
- Oracle và coverage của validator: `GIAI_THICH_KIEM_THU_INPUT_VALIDATOR.md`.
- Kết quả và giới hạn đầy đủ: `report/BAO_CAO.md`.
- Kịch bản nói khi bảo vệ: `KICH_BAN_THUYET_TRINH_VA_DEMO.md`.
