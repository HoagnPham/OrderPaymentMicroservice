# OrderPaymentMicroservice — Bài tập lớn An toàn phần mềm

> Admission/audit bổ sung 13/10/2026: body 64 KiB, tối đa 100 item; auth quota
> 10/phút/IP và API quota 120/phút/subject hoặc connection IP. PostgreSQL giữ
> audit BEGIN/RESULT và chặn UPDATE/DELETE/TRUNCATE. Audit error trả JSON phân biệt
> action chưa chạy với operation có thể đã commit, cần reconcile.
> Test mới nhất: 140 unit + 17 integration PASS, không SKIP; fuzz 3/3 PASS.
> T3/T5 giảm thiểu một phần; outbox/retention/distributed quota còn mở.


> Bổ sung 13/10/2026: gateway deadline mặc định 5 giây; worker truyền cancellation,
> tiếp tục sau lookup treo và không invent outcome khi shutdown. Input tiền phải
> là bội 0,01; không round âm thầm. Test: 131 unit + 11 integration PASS;
> fuzz 3/3, branch coverage target 99,09% (gồm MoneyAmounts).
> Các số liệu trước đó bên dưới là lịch sử; xem báo cáo/evidence mới nhất.


> Cập nhật 13/10/2026: gateway mặc định `DurableFake` giữ journal qua restart;
> test kill API sau charge xác minh worker khôi phục mà không charge/release lại.
> Cancel UNKNOWN đã đồng bộ giữa model/source. Chi tiết và cấu hình:
> [Hợp đồng hiện thực](security-analysis/IMPLEMENTATION_CONTRACTS.md).
> Số test hiện hành lấy từ [evidence](security-analysis/results/application_tests.results.json);
> các số liệu ngày 12/10 bên dưới giữ như kết quả lịch sử.


> Cập nhật 12/10/2026: đã sửa tồn kho khi Order có dòng sản phẩm trùng, validation
> Cart/User/Product và transaction giỏ hàng; migration bổ sung CHECK stock/price/quantity.
> Kết quả mới: 108 unit + 8 PostgreSQL integration PASS, không SKIP.
> [Báo cáo 9 chương](security-analysis/report/BAO_CAO.md),
> [Phụ lục A–G](security-analysis/report/PHU_LUC_A_G.md),
> [traceability](security-analysis/results/traceability_matrix.csv) và
> [manifest](security-analysis/results/evidence_manifest.json).
> Evidence TLC/static/sanitizer/coverage trước đây được giữ như kết quả lịch sử.
> Workbook generator và checker hiện dùng artifact của project, không truy cập ATPM.


## 1. Mục tiêu dự án

Đề tài xây dựng và kiểm chứng an toàn hệ thống xử lý đơn hàng và thanh toán trực tuyến dưới dạng REST API. Trọng tâm không phải phát triển một website thương mại điện tử lớn mà là dùng Order và Payment làm đối tượng để thực hành quy trình:

```text
Phát hiện → Phân tích nguyên nhân → Kiểm chứng/counterexample
→ Kiểm thử thực tế → Sửa lỗi → Kiểm chứng và kiểm thử lại
```

Các mục tiêu gốc được mô tả tại [README của source](OrderPaymentMicroservice/readme.md). File này là tài liệu tổng hợp ở cấp repository: giải thích cấu trúc, cách chạy, kết quả hiện có và phần việc còn thiếu.

## 2. Cấu trúc repository

```text
OrderPaymentMicroservice/
├── ATPM/                         Tài liệu/lab tham chiếu; hai module được pipeline import
├── OrderPaymentMicroservice/     Source ASP.NET Core, REST API và giao diện demo
├── OrderPaymentMicroservice.Tests/ Unit test xUnit và cấu hình Coverlet
├── OrderPaymentMicroservice.IntegrationTests/ HTTP E2E test với PostgreSQL container/local
├── security-analysis/            Model, dataset, script và kết quả áp dụng ATPM
├── OrderPaymentMicroservice.slnx Solution .NET
└── test-api.ps1                  Script E2E thủ công có JWT và idempotency
```

### `ATPM/`

Pipeline phân tích chỉ import trực tiếp hai module source sau; các nội dung ATPM còn lại là tài liệu/công cụ tham chiếu và không được tính là sản phẩm của dự án:

- `lab1/.../python/kripke_model.py` để kiểm tra model Kripke.
- `lab3_secure_memory/python/risk_assessor.py` để tính CVSS-lite.

Kết quả riêng của dự án được lưu dưới `security-analysis/`, không ghi đè dataset gốc trong `ATPM/`.

### `OrderPaymentMicroservice/`

Source code ASP.NET Core trên .NET 10, Entity Framework Core và PostgreSQL. Các module hiện có: User, Product, Cart, Order và Payment. Thư mục gồm Controller, Service, DTO, Model, Data/Migration, lớp validation và giao diện tĩnh trong `wwwroot`.

### `OrderPaymentMicroservice.Tests/`

Project xUnit hiện có 108 unit test, trong đó 34 trường hợp tập trung vào InputValidator và 3 fuzz campaign xác định; phần còn lại kiểm tra User, Order, hai state machine, Payment, retry/reconciliation, refund, inventory compensation và race payment–cancel. Project integration test bổ sung 8 luồng HTTP chạy trên PostgreSQL thật qua Docker hoặc cluster cục bộ tạm thời.

### `security-analysis/`

Chứa phần áp dụng kiến thức lab vào đề tài:

- Threat model CIA/STRIDE, trust boundary và truy vết evidence.
- Model Order đúng và model BUGGY.
- Recovery model 7 hợp đồng chức năng, 15 state/37 transition, P1–P15 và negative mutations.
- Static/dynamic memory analysis cùng alignment evidence cho năm module C.
- SMT/Z3 cho bất biến tổng tiền và payment.
- Script tích hợp Lab 1/Lab 3.
- Dataset năm lỗ hổng và kết quả xếp hạng.
- Báo cáo coverage, báo cáo tiến độ và tài liệu giải thích test.

## 3. Chức năng hệ thống đã có

| Hạng mục | Hiện trạng | Nhận xét |
|---|---|---|
| REST API | Đã có Controller cho User, Product, Cart, Order, Payment | Có JWT/role/ownership và integration test 401/403 |
| Dữ liệu | Có DbContext, PostgreSQL, migration và seed | Test tự dùng Testcontainer khi có Docker, nếu không dùng PostgreSQL binaries cục bộ |
| Order | Có tạo, hủy, xác nhận, hoàn tất, thất bại và compensation/refund | Có test PostgreSQL cho hai payment request cạnh tranh |
| Payment | Có processing, success, failed, unknown, bounded retry, reconciliation và refund | Adapter mặc định vẫn là FakePaymentGateway |
| Cart/Product/User | Có các chức năng hỗ trợ demo | Đã có ownership check và test HTTP xuyên suốt |
| Giao diện demo | Có nội dung tĩnh trong `wwwroot` | Chưa đánh giá E2E trong đợt này |
| API script | Có `test-api.ps1` | Đã khớp JWT, DTO, endpoint và idempotency contract hiện tại |

`test-api.ps1` tạo tài khoản riêng cho mỗi lần chạy, đăng nhập lấy JWT, dùng giá sản phẩm từ server, tạo đơn, thanh toán với idempotency key, kiểm tra trạng thái `PAID` và xác nhận không thể hủy sau khi charge thành công.

### 3.1. Vòng đời trạng thái hiện thực

Order và Payment là hai state machine riêng. Trạng thái gateway không được dùng
thay cho trạng thái nghiệp vụ của Order.

```text
Order:
CREATED → VALIDATED → PAYMENT_PENDING → PAID → CONFIRMED → COMPLETED
              └──────────────→ CANCELLED
                         └────→ FAILED
PAID/CONFIRMED → REFUND_PENDING → REFUNDED

Payment:
PENDING → PROCESSING → SUCCESS → REFUND_PENDING → REFUNDED
                    ├→ FAILED
                    └→ UNKNOWN → PROCESSING (retry/reconcile cùng key)

Inventory:
RESERVED → CONSUMED  (Order COMPLETED)
RESERVED → RELEASED  (Order CANCELLED/FAILED/REFUNDED)
```

Các endpoint lifecycle bổ sung:

- `POST /api/payments/{id}/retry`: retry bounded bằng cùng payment/idempotency key.
- `POST /api/payments/{id}/reconcile`: hỏi lại gateway sau timeout hoặc process crash.
- `POST /api/payments/{id}/refund`: Admin bắt đầu compensation idempotent.
- `POST /api/payments/{id}/refund/retry`: Admin retry refund đang pending.
- `POST /api/orders/{id}/confirm`: Admin xác nhận fulfillment.
- `POST /api/orders/{id}/complete`: Admin hoàn tất và consume reservation.

Nếu hết retry nhưng gateway vẫn chưa trả kết quả cuối cùng, hệ thống giữ Payment
`UNKNOWN`, Order `PAYMENT_PENDING` và inventory `RESERVED`; không được suy diễn
timeout là thất bại rồi hoàn kho. Chi tiết bất biến và xử lý sự cố xem
[`WorkflowAnalysis.md`](OrderPaymentMicroservice/Security/WorkflowAnalysis.md).

## 4. Phần an toàn phần mềm đã hoàn thành

### 4.1. Phân tích và mô hình hóa Order

Đã phân tích luồng trạng thái Order, các bất biến số tiền và quan hệ với Payment. [Model kỳ vọng](security-analysis/order_state_model.json) định nghĩa Kripke M = (S, S0, R, L) với tám trạng thái Order.

Bốn thuộc tính được kiểm tra:

| ID | Thuộc tính | Kết quả model kỳ vọng |
|---|---|---|
| P1 | Không bỏ bước, quay ngược hoặc hồi sinh trạng thái terminal | PASS |
| P2 | Từ mọi trạng thái reachable, mọi đường đi cuối cùng đến terminal | PASS |
| P3 | PAID phải có payment SUCCESS | PASS |
| P4 | PAYMENT_PENDING chưa có payment SUCCESS | PASS |

Lab 1 không hỗ trợ trực tiếp AF. [Script tích hợp](security-analysis/run_analysis.py) dùng checker gốc cho AG_NOT/AG_IMPLIES và bổ sung phép tính điểm cố định cho `AG AF terminal`. Model này mô tả **thiết kế kỳ vọng**, không phải bằng chứng rằng implementation hiện tại thực thi đúng mọi cạnh.

### 4.2. Counterexample trên model BUGGY

[Model BUGGY](security-analysis/order_state_model_buggy.json) cố ý cho phép đơn CANCELLED chuyển sang PAID. History monitor `PAID_AFTER_CANCELLED` giúp checker trạng thái nhận diện lịch sử chuyển sai.

P1 FAIL với counterexample:

```text
CREATED → CANCELLED → PAID_AFTER_CANCELLED
```

Kết quả chứng minh công cụ phát hiện được lỗi đã mô hình hóa. Đây là lỗi của baseline ban đầu. PaymentService hiện đã kiểm tra trạng thái nguồn; regression test chặn thanh toán đơn CANCELLED.

### 4.3. Unit test và coverage

Đã xây dựng 34 test xUnit riêng cho InputValidator, gồm null/rỗng, ID không dương, biên TotalAmount/Quantity/UnitPrice, nhiều item, tolerance và decimal.MaxValue. Toàn solution hiện có 108 unit test và 8 integration test PostgreSQL, bổ sung fuzzing validator/state machine, test transition Order/Payment, timeout/unknown, retry giới hạn, crash reconciliation, refund, hoàn tồn kho, authorization và cạnh tranh payment/cancel.

Kết quả chạy lại ngày **12/10/2026**:

```text
Unit: Passed 108, Failed 0, Skipped 0
Integration: Passed 8, Failed 0, Skipped 0 (PostgreSQL local fallback)
```

Coverage đã lưu:

| Phạm vi | Branch coverage |
|---|---:|
| InputValidator | 31/32 = 96,875% |
| ValidateCreateOrderRequest() | 100% |
| ValidateOrderItem() | 92,85% |

Nhánh chưa phủ là `subtotal < 0` theo hướng true. Sau các guard, Quantity ≥ 1 và UnitPrice ≥ 0 nên đường này bất khả thi với input hợp lệ đi tới vị trí đó. Coverage cao không tự chứng minh logic đúng; nhánh tolerance hiện có regression test xác nhận mismatch vượt 0,01 bị từ chối.

Chi tiết xem [Giải thích kiểm thử InputValidator](security-analysis/GIAI_THICH_KIEM_THU_INPUT_VALIDATOR.md).

### 4.4. Đánh giá lỗ hổng

Đã tạo [dataset lỗ hổng](security-analysis/vulnerabilities.json) theo cấu trúc Lab 3 và chạy `risk_assessor.py`:

| Ưu tiên | ID | Phát hiện | CVSS-lite | Mức độ |
|---:|---|---|---:|---|
| 1 | VULN-001 | Order API thiếu authentication/authorization | 9,1 | CRITICAL |
| 2 | VULN-005 | Truy cập/sửa giỏ hàng chéo người dùng | 8,1 | HIGH |
| 3 | VULN-002 | Giá và tổng tiền do client kiểm soát | 7,5 | HIGH |
| 4 | VULN-003 | Đơn CANCELLED có thể chuyển sang PAID | 7,5 | HIGH |
| 5 | VULN-004 | Validator chấp nhận tổng tiền sai | 7,5 | HIGH |

Đây là CVSS-lite giáo dục của Lab 3, không phải điểm CVSS chính thức. Các phát hiện dựa trên static review và characterization test; chưa phải kết quả pentest trên môi trường triển khai.

## 5. Đánh giá mức hoàn thiện

Không nên gán một phần trăm hoàn thành duy nhất khi chưa có rubric và tiêu chí nghiệm thu định lượng. Theo từng đầu ra trong README mục tiêu:

| Đầu ra yêu cầu | Trạng thái | Bằng chứng/khoảng trống |
|---|---|---|
| Hệ thống Order & Payment chạy được | **Một phần** | Có lifecycle, test mức service và bộ E2E PostgreSQL; gateway mặc định còn mô phỏng |
| REST API tích hợp shop | **Một phần** | Có JWT/role/ownership và HTTP integration test; script đã đồng bộ contract nhưng chưa có transcript demo hiện hành |
| Database hoạt động | **Đạt trong integration suite** | Tạo PostgreSQL sạch, apply migration/seed và chạy 8/8 test qua Docker hoặc local fallback |
| Demo hệ thống | **Một phần** | Có frontend và script; chưa có kết quả demo E2E hiện hành |
| Model và thuộc tính | **Đạt trong phạm vi model** | Kripke đúng + BUGGY + bốn kết quả kiểm chứng |
| Bằng chứng kiểm thử | **Đạt cho InputValidator** | 34 test, Cobertura, branch coverage > 90% |
| Phát hiện và phân tích lỗi | **Đạt giai đoạn phát hiện** | Năm vulnerability, counterexample, xếp hạng CVSS-lite |
| SMT/Z3 | **Đạt trong phạm vi số học** | QF_NIA, Z3 5.1.0, 7/7 truy vấn đúng kỳ vọng và có JSON evidence |
| TLA+/TLC | **Đạt trong phạm vi model** | TLC: 35 state sinh, 20 state phân biệt, depth 9, không phát hiện lỗi safety/liveness |
| Fuzzing/dynamic testing | **Đạt cho target đã chọn** | 3/3 campaign, seed corpus cố định, 50.000 iteration, timeout/crash policy, branch 99,07% |
| Phân tích an toàn bộ nhớ/phương án phù hợp | **Đạt trên mô hình C học tập** | Static scanner phân biệt 5 BUGGY/FIXED, có cppcheck/layout evidence và sanitizer 22/22; không suy rộng sang C# |
| Sửa lỗi và kiểm chứng lại | **Đạt trong suite hiện tại** | Guard đã có; 108 unit và 8 PostgreSQL integration test đều PASS |
| Báo cáo kỹ thuật | **Đang hoàn thiện** | Đã có báo cáo tiến độ và tài liệu chi tiết; cần cập nhật sau sửa lỗi |

**Kết luận:** Bốn chương đều đã có artifact và bằng chứng tái lập, gồm TLC, fuzzing và PostgreSQL integration. Hệ thống vẫn chưa được coi là production-ready vì gateway thật, hardening vận hành và demo triển khai ngoài test host còn nằm ngoài phạm vi đã xác minh.

## 6. Các lỗi còn tồn tại và thứ tự xử lý

1. Thay `FakePaymentGateway` bằng adapter sandbox/production có xác minh callback; giữ nguyên hợp đồng reconciliation theo idempotency key.
2. Bổ sung transactional outbox cho side effect; background worker reconcile state bị kẹt đã có.
3. Chạy và lưu transcript `test-api.ps1` trên môi trường demo PostgreSQL sạch.
4. Bổ sung rate limiting, audit log, health check và metrics cho các state bị kẹt.
5. Mở rộng fuzz/integration sang lỗi mạng, restart process và gateway sandbox.

## 7. Cách chạy

### Yêu cầu

- .NET 10 SDK.
- Python 3.10 trở lên.
- PostgreSQL và connection string phù hợp nếu chạy API. Integration test dùng Docker nếu có, nếu không tự dùng PostgreSQL binaries cục bộ.
- Kết nối NuGet trong lần restore đầu.

### Build và test

```powershell
dotnet restore OrderPaymentMicroservice.slnx
dotnet build OrderPaymentMicroservice.slnx
dotnet test OrderPaymentMicroservice.slnx
```

Khi Docker khả dụng, Testcontainers tạo PostgreSQL riêng. Nếu Docker không có nhưng máy đã cài PostgreSQL binaries, test tạo cluster tạm trong thư mục hệ thống, apply migration/seed rồi dừng và xóa cluster sau khi chạy; không sử dụng database phát triển.

OpenAPI ở môi trường Development được cung cấp tại `/openapi/v1.json`.

### Đo coverage

```powershell
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj /p:CollectCoverage=true /p:CoverletOutput=../security-analysis/results/input-validator /p:CoverletOutputFormat=cobertura '/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator' /p:Threshold=90 /p:ThresholdType=branch
```

### Chạy phân tích ATPM

```powershell
python security-analysis/run_analysis.py
python security-analysis/run_fuzzing.py
python security-analysis/run_tlc.py --jar /path/to/tla2tools.jar
python security-analysis/create_order_payment_spec.py
python security-analysis/verify_teacher_spec.py
python security-analysis/verify_inventory_smt.py
python security-analysis/package_evidence.py
python security-analysis/build_report.py
```

Các script chạy threat model, model đúng/BUGGY, recovery model, risk assessment, SMT/Z3, fuzzing, TLC và kiểm tra workbook theo đúng schema ATPM của giảng viên. Static/dynamic analysis C chạy riêng bằng `make -C security-analysis/c-memory static-check` và `make -C security-analysis/c-memory test`. Muốn chủ động tạo lại dataset/model từ mã sinh:

```powershell
python security-analysis/create_datasets.py
```

Không chạy `create_datasets.py` nếu đã chỉnh JSON thủ công mà chưa đồng bộ các thay đổi đó vào script.

### Thiết lập PostgreSQL lần đầu trên Ubuntu

Nếu PostgreSQL đã cài nhưng chưa chạy:

```bash
sudo systemctl start postgresql
pg_isready -h localhost -p 5432
sudo -u postgres psql
```

Trong `psql`, tạo tài khoản riêng cho ứng dụng, đặt mật khẩu theo lời nhắc và tạo database do tài khoản đó sở hữu (chỉ chạy một lần):

```sql
CREATE ROLE orderpayment LOGIN;
\password orderpayment
CREATE DATABASE orderpaymentdb OWNER orderpayment;
\q
```

Không cần đặt mật khẩu cho tài khoản quản trị `postgres`: lệnh `sudo -u postgres psql` dùng xác thực local của Ubuntu. Kiểm tra tài khoản ứng dụng qua TCP:

```bash
psql -h localhost -U orderpayment -d orderpaymentdb -W -c 'SELECT current_user;'
```

Từ thư mục gốc repository, lưu cấu hình phát triển bằng Bash. Nhập cùng mật khẩu đã đặt ở trên; dấu nháy đơn trong connection string được escape để hỗ trợ ký tự đặc biệt:

```bash
read -rsp 'Mật khẩu PostgreSQL của orderpayment: ' db_password
printf '\n'
db_password_escaped=${db_password//\'/\'\'}
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=orderpaymentdb;Username=orderpayment;Password='$db_password_escaped'" --project OrderPaymentMicroservice/OrderPaymentMicroservice.csproj
unset db_password db_password_escaped
dotnet user-secrets set "Jwt:Key" "$(openssl rand -hex 32)" --project OrderPaymentMicroservice/OrderPaymentMicroservice.csproj
dotnet run --project OrderPaymentMicroservice/OrderPaymentMicroservice.csproj --launch-profile http
```

Mở `http://localhost:5246`. Ứng dụng gọi `Database.Migrate()` khi khởi động rồi seed sản phẩm theo cách idempotent. Database từng được tạo bằng `EnsureCreated()` nhưng không có lịch sử migration cần được backup và baseline/reset trước khi nâng cấp.

### Chạy API

Không lưu connection string hoặc JWT signing key trong repository. Cấu hình bằng User Secrets ở môi trường phát triển:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=orderpaymentdb;Username=orderpayment;Password=<your-password>" --project OrderPaymentMicroservice/OrderPaymentMicroservice.csproj
dotnet user-secrets set "Jwt:Key" "<random-secret-at-least-32-characters>" --project OrderPaymentMicroservice/OrderPaymentMicroservice.csproj
```

Sau đó:

```powershell
dotnet run --project OrderPaymentMicroservice/OrderPaymentMicroservice.csproj
```

Không dùng tài khoản/secret mẫu cho môi trường thật. `test-api.ps1` đã được cập nhật theo JWT, DTO và idempotency hiện tại; vẫn chỉ dùng cho môi trường demo với database riêng.

## 8. Tài liệu và bằng chứng

- [Mô phỏng lỗi bộ nhớ bằng C: Order, Payment, Cart, Product, User](security-analysis/c-memory/README.md)
- [Báo cáo tiến độ ATPM](security-analysis/BAO_CAO_TIEN_DO_ATPM.md)
- [Giải thích bộ test](security-analysis/GIAI_THICH_KIEM_THU_INPUT_VALIDATOR.md)
- [Hướng dẫn security-analysis](security-analysis/README.md)
- [Coverage tóm tắt](security-analysis/results/coverage_summary.json)
- [Cobertura XML](security-analysis/results/input-validator.cobertura.xml)
- [Kết quả model đúng](security-analysis/results/order_state_model.results.json)
- [Kết quả model BUGGY](security-analysis/results/order_state_model_buggy.results.json)
- [Threat model và kết quả](security-analysis/THREAT_MODEL_CHUONG1.md)
- [Recovery model và kết quả](security-analysis/results/order_payment_recovery_model.results.json)
- [Static/layout evidence Chương 2](security-analysis/c-memory/evidence/static-analysis.json)
- [Sanitizer evidence Chương 2](security-analysis/c-memory/evidence/dynamic-analysis.json)
- [SMT/Z3 evidence](security-analysis/results/payment_verification.results.json)
- [Kết quả risk assessment](security-analysis/results/risk_assessment_results.json)

## 9. Quy ước repository và dọn dẹp

Các thư mục `bin/`, `obj/`, `TestResults/`, `.vs/`, `.pytest_cache/`, `__pycache__/`, `tmp/`, `.spreadsheet-work/`, `node_modules/` và file `*.pyc` là sản phẩm sinh tự động, không phải source. Chúng được chặn trong `.gitignore` và có thể tái tạo bằng build/test hoặc công cụ tương ứng.

Các file kết quả JSON/Cobertura trong `security-analysis/results/` được giữ lại vì là bằng chứng thực nghiệm của BTL. Hai file trung gian `*.lab1.json` không được giữ; script tích hợp tự xóa sau khi checker chạy.

Build output và file tạm được loại khỏi phần bàn giao. Thư mục `ATPM/` vẫn chứa tài liệu học phần, lab, công cụ và archive tham chiếu; chúng không được tính là artifact của dự án. `security-analysis/run_analysis.py` chỉ import trực tiếp hai module Python cần thiết từ Lab 1 và Lab 3.

Việc xóa ở working tree không loại dữ liệu khỏi các commit cũ. Trước khi chuyển repository sang public cần đổi mật khẩu PostgreSQL từng xuất hiện trong lịch sử và rewrite history để loại secret cùng các blob lớn.
