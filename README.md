# OrderPaymentMicroservice — Kiểm chứng an toàn phần mềm

Đề tài Group 14: **Xây dựng và kiểm chứng an toàn hệ thống xử lý đơn hàng và
thanh toán trực tuyến**. Trọng tâm là idempotency, crash recovery và consistency
Order–Payment–Inventory, với bằng chứng từ model, SMT, regression và PostgreSQL.
Các module hiện chạy trong một host ASP.NET Core và dùng chung PostgreSQL.

Lượt kiểm chứng 22/10/2026: **149 unit + 35 integration PASS, không SKIP**.
Charge/refund khi PostgreSQL stop/start được đối soát với cùng API process;
`ready` chuyển 200 → 503 → 200, không gọi lại charge/refund và refund trả kho một lần.
Gateway là DurableFake local, chưa phải provider thu tiền thật hoặc production proof.

## Tài liệu nên đọc

| Tài liệu | Mục đích |
| --- | --- |
| [Kịch bản thuyết trình và demo](security-analysis/KICH_BAN_THUYET_TRINH_VA_DEMO.md) | Lời nói theo bốn chương học phần, thao tác demo, oracle và hỏi đáp |
| [Báo cáo kỹ thuật](security-analysis/report/BAO_CAO.md) | Phương pháp, phân tích, kết quả và giới hạn |
| [Phụ lục A–G](security-analysis/report/PHU_LUC_A_G.md) | State/contract, counterexample, diagnostics và từng testcase |
| [Phạm vi nghiệm thu](security-analysis/PHAM_VI_KIEM_CHUNG.md) | Việc bắt buộc, tùy thời gian và future work |
| [Index kiểm chứng](security-analysis/README.md) | Vị trí source/model/evidence và lệnh độc lập |
| [Hợp đồng hiện thực](security-analysis/IMPLEMENTATION_CONTRACTS.md) | Gateway, timeout, audit, order idempotency và retry |
| [Hướng dẫn vận hành](security-analysis/OPERATIONS.md) | Backup/restore, health/status và chạy project |

## Cấu trúc

```text
OrderPaymentMicroservice/                  API, services, DTOs, migrations, shop
OrderPaymentMicroservice.Tests/            Unit/regression/fuzz tests
OrderPaymentMicroservice.IntegrationTests/  HTTP/PostgreSQL/process/browser tests
OrderPaymentMicroservice.CrashProbe/        Executable điều khiển fault trong test
security-analysis/                         Models, scripts, evidence, report
OrderPaymentMicroservice.slnx               Solution .NET
```

Thư mục ATPM chứa tài liệu/lab/công cụ tham chiếu của học phần, được giữ nguyên.
Các lệnh nghiệm thu bên dưới không truy cập thư mục này. Templates
`security-analysis/report/chapters/` là đầu vào sinh report, cần được giữ.

## Chuẩn bị và build

Cần .NET 10 SDK và Python 3.10+. HTTP integration fixture dùng Docker hoặc
PostgreSQL local tùy môi trường; **demo reconnect cần PostgreSQL binaries local**
(`postgres`, `initdb`, `pg_ctl`, `createdb`). Có thể đặt POSTGRES_BIN tới thư mục bin.
Restore lần đầu có thể cần mạng NuGet.

```bash
dotnet restore OrderPaymentMicroservice.slnx
dotnet build OrderPaymentMicroservice.slnx --no-restore --nologo
```

## Demo safety Order–Payment

```bash
python3 security-analysis/run_recovery_demo.py
```

Script tự tạo cluster/API tạm, chạy hai tests charge/refund stop/start, kiểm
freshness và source hashes. Trong mỗi case, API giữ nguyên process/pool; gateway
journal có trước outage, finalize thất bại, worker scan gặp lỗi rồi phục hồi.
Test cleanup tài nguyên tạm, không dùng database phát triển. Subset demo không
thay summary toàn suite.

| Evidence | Kết quả cần đối chiếu |
| --- | --- |
| [Charge reconnect](security-analysis/results/postgres_reconnect_charge.results.json) | SUCCESS/PAID/RESERVED; charge=1; release=0 |
| [Refund reconnect](security-analysis/results/postgres_reconnect_refund.results.json) | REFUNDED/RELEASED; charge=1, refund=1; release=1 |
| [Process-kill charge](security-analysis/results/process_restart.results.json) | API process mới phục hồi charge sau journal |
| [Process-kill refund](security-analysis/results/refund_restart.results.json) | Giữ charge reference, refund và release một lần |

Đọc [hướng dẫn chi tiết](security-analysis/KICH_BAN_THUYET_TRINH_VA_DEMO.md#5-demo-chính-chargerefund-khi-postgresql-ngắt-và-phục-hồi)
để chạy riêng từng case, in timeline và giải thích các assertions.

## Chạy toàn suite và sinh báo cáo

```bash
dotnet test OrderPaymentMicroservice.slnx --no-restore --nologo --logger 'trx;LogFilePrefix=verification' --results-directory security-analysis/results/dotnet --blame-hang-timeout 90s
```

Chỉ sau khi toàn suite PASS, tiếp tục theo thứ tự; dừng nếu có lệnh thất bại:

```bash
python3 security-analysis/package_evidence.py &&
python3 security-analysis/verify_threat_model.py &&
python3 security-analysis/build_report.py &&
python3 security-analysis/package_evidence.py
```

Package dùng TRX mới nhất của từng suite trong results/dotnet. Không ghi TRX của
filtered tests vào thư mục này. Build report từ chối summary toàn suite chưa PASS.
Manifest ghi base commit, dirty working tree và hashes; hash đóng gói không tự
chứng minh source tại lần thực thi lịch sử. Không chạy các lệnh dotnet/coverage/fuzz
song song dùng chung assemblies.

## Chạy shop thủ công trên database demo riêng

Cấu hình `ConnectionStrings:DefaultConnection` và `Jwt:Key` ngoài source, qua
user secrets hoặc biến môi trường. Jwt key cần ít nhất 32 ký tự; PostgreSQL database
phải thuộc môi trường demo. Nếu chưa cấu hình, dùng hai test demo ở trên trước.

Ví dụ đặt cấu hình bằng user secrets, thay các placeholder bằng giá trị của mình:

```bash
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' 'Host=localhost;Port=5432;Database=orderpayment_demo;Username=<demo-user>;Password=<your-password>' --project OrderPaymentMicroservice/OrderPaymentMicroservice.csproj
dotnet user-secrets set 'Jwt:Key' '<random-secret-at-least-32-characters>' --project OrderPaymentMicroservice/OrderPaymentMicroservice.csproj
dotnet run --project OrderPaymentMicroservice/OrderPaymentMicroservice.csproj --no-restore --launch-profile http
```

Mở `http://localhost:5246`; OpenAPI Development tại `/openapi/v1.json`.
Startup gọi Database.Migrate và seed trên DB đã cấu hình; lệnh run thủ công
không tự tạo cluster tạm. Flow: register/login → cart → order → payment giả lập
→ history. Không thu thông tin thẻ thật. Smoke test Chrome headless trên cluster
tạm được mô tả trong tài liệu demo, với [evidence](security-analysis/results/application_smoke.results.json).

## Bất biến và phạm vi kết luận

Order/Payment có state machine riêng. Processing/key được lưu trước khi gọi
gateway; reconciliation hỏi kết quả cũ, không charge mù. UNKNOWN giữ reservation;
refund giữ dấu vết charge và release kho idempotent. Giá dùng catalog phía server,
quantity cùng ProductId được cộng dồn trước khi kiểm stock. Unique constraints,
Serializable transaction và CHECK bổ sung bảo vệ ở PostgreSQL.

Coverage InputValidator 97,22% branch và MoneyAmounts 100% chỉ thuộc target đã đo.
Model recovery có 15 states, 37 events, 7 contracts và 6 negative mutations.
Raw AG AF terminal trên graph đầy đủ là false; progress-only true phụ thuộc policy
lọc cạnh, không phải proof fair-CTL đầy đủ. Các kết luận đều có giới hạn trong report.

Giữ JWT/ownership/Admin hiện có. DB migration/runtime RBAC, retention job,
webhook thật, shared Redis limiter, gateway thật và Kubernetes là future work
ngoài nghiệm thu hiện tại. Nếu còn thời gian, ưu tiên lặp load/crossover và
quan sát recovery dưới tải. Không cam kết mọi interleaving, failover, partition,
power loss hoặc một hệ thống production an toàn hoàn toàn.

Source, models, JSON/CSV evidence và report cần được giữ trong hồ sơ. bin/obj,
TestResults và profiles/cache tạm là output có thể tái tạo. Không commit credentials,
JWT key hoặc dữ liệu cá nhân trong cấu hình/journal.
