# Tài liệu và công cụ kiểm chứng Order–Payment

Đọc [README gốc](../README.md) để build/run project. Dùng
[kịch bản thuyết trình và demo](KICH_BAN_THUYET_TRINH_VA_DEMO.md) khi bảo vệ.
[Báo cáo](report/BAO_CAO.md) và [Phụ lục A–G](report/PHU_LUC_A_G.md) là tài liệu
kỹ thuật chính; không giữ thêm một báo cáo tiến độ với số liệu cũ.

## Các tài liệu có vai trò riêng

| Tệp | Nội dung |
| --- | --- |
| [PHAM_VI_KIEM_CHUNG.md](PHAM_VI_KIEM_CHUNG.md) | Tiêu chí nghiệm thu và future work ngoài scope |
| [IMPLEMENTATION_CONTRACTS.md](IMPLEMENTATION_CONTRACTS.md) | Các hợp đồng source/API/gateway/audit/retry |
| [OPERATIONS.md](OPERATIONS.md) | Backup/restore, health/status, triển khai demo |
| [Giải thích InputValidator](GIAI_THICH_KIEM_THU_INPUT_VALIDATOR.md) | Unit oracle, biên, exception và cách đọc coverage |
| [README C](c-memory/README.md) | Module C BUGGY/FIXED, sanitizer/layout, CBMC/Valgrind, SAT/CNF/DRAT và cách chạy |
| report/chapters/*.md | Templates để build_report.py sinh báo cáo; không xóa vì trùng nội dung output |

## Bốn chương học phần và nguồn kiểm chứng

| Phần | Source/model | Evidence |
| --- | --- | --- |
| Threat model | [threat_model.json](threat_model.json) | [Threat consistency](results/threat_model_results.json) |
| State/contract/recovery | [Recovery model](order_payment_recovery_model.json) | [Properties/mutations](results/order_payment_recovery_model.results.json), [state-response CSV](results/order_payment_state_response_matrix.csv) |
| Liveness semantics | audit_liveness.py | [Raw/progress-only](results/liveness_audit.results.json) |
| Ownership/lifetime | c-memory/modules/*.c | [Static/layout](c-memory/evidence/static-analysis.json), [sanitizer](c-memory/evidence/dynamic-analysis.json), [CBMC](c-memory/evidence/bmc-analysis.json), [SAT/CNF/DRAT](c-memory/evidence/sat-pipeline.json), [Valgrind](c-memory/evidence/valgrind-analysis.json) |
| Số học | Security/PaymentVerification.z3 và InventoryVerification.z3 trong source app | [Payment SMT](results/payment_verification.results.json), [inventory SMT](results/inventory_verification.results.json) |
| Unit/integration | Các project Tests/IntegrationTests | [Toàn suite](results/application_tests.results.json) |
| Coverage/fuzz | Coverlet và SecurityFuzzingTests | [Coverage](results/coverage_summary.json), [fuzz](results/application_fuzzing.results.json) |
| Crash/reconnect | CrashProbe, OperationalProbe, PostgresReconnectTests | [Charge](results/postgres_reconnect_charge.results.json), [refund](results/postgres_reconnect_refund.results.json) |
| Giao diện thật | ApplicationSmokeTests và browser/live_smoke.py | [Live smoke](results/application_smoke.results.json) |
| Provenance | package_evidence.py | [Traceability](results/traceability_matrix.csv), [manifest](results/evidence_manifest.json) |

## Lệnh dùng trong nghiệm thu hiện tại

Chạy từ gốc repository, sau khi restore .NET dependencies:

```bash
python3 security-analysis/run_all_checks.py
python3 security-analysis/run_recovery_demo.py
python3 security-analysis/verify_recovery_model.py
python3 security-analysis/audit_liveness.py
python3 security-analysis/verify_threat_model.py
python3 security-analysis/verify_inventory_smt.py
python3 security-analysis/verify_payment_smt.py
```

`run_all_checks.py` là chuỗi nhanh mặc định: workbook/ATPM checker, recovery, liveness, SMT, threat, C static/sanitizer/CBMC/Valgrind, package và report. Nó ghi `results/end_to_end.results.json`; các nhóm .NET/PostgreSQL/TLC/fuzz/SAT là opt-in. `--with-sat` cần CaDiCaL và `drat-trim` trên `PATH`; mặc định vẫn kiểm evidence SAT đã lưu. Chuỗi mặc định không đọc `ATPM/`; `run_analysis.py`/`create_datasets.py` là script legacy có thể phụ thuộc toolkit học phần và không nằm trong chuỗi này. Hai lệnh SMT cần Z3. Demo reconnect cần PostgreSQL binaries local. Cần công cụ C (`gcc`, `cppcheck`, `cbmc`, `valgrind`) chỉ khi chạy các check C tương ứng; cần Chrome/Chromium chỉ khi chạy browser tests. Lệnh toàn suite/coverage/fuzz và cách cập nhật report nằm trong README gốc, tutorial validator và tài liệu demo. Không chạy đồng thời dotnet/coverage/fuzz.

TLC có evidence lịch sử trong results/tlc, không được đổi nhãn thành lượt mới
chỉ vì package. Các scripts legacy run_analysis.py/create_datasets.py có thể
phụ thuộc toolkit học phần; chúng không thuộc quy trình demo độc lập này và
không chạy khi đang giữ yêu cầu không truy cập ATPM. Không tạo lại workbook chỉ
để chạy demo recovery.

## Tổ chức và giới hạn

Models/datasets là source máy đọc được; JSON/CSV/Cobertura và report là bằng chứng
cần giữ. Generated BAO_CAO/PHU_LUC và templates có quan hệ input–output, không là
hai bộ tài liệu trùng cần xóa. Số liệu chính lấy từ evidence; lời thuyết trình
phân biệt raw model semantics, test oracle và kết quả lịch sử.

Kết luận chỉ trong các kịch bản đã chạy: local PostgreSQL, DurableFake provider,
finite races/load và target fuzz đã chọn. DB RBAC, retention, webhook thật,
shared limiter, gateway thật và Kubernetes ngoài nghiệm thu hiện tại.
