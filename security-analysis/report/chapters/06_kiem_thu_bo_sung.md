### 6.1. Thiết kế oracle và lớp test

| Lớp | Điều quan sát | Oracle | Không kết luận được |
| --- | --- | --- | --- |
| Validator unit | Chấp nhận/từ chối miền input | Exception có kiểm soát hoặc response hợp lệ | Middleware, SQL constraints và lỗi mạng |
| Service unit với EF InMemory | Aggregate và thay đổi kho | State/amount/stock đúng, từ chối không đổi dữ liệu | Serializable PostgreSQL và semantics SQL |
| HTTP/PostgreSQL | Authentication, route, DTO, migration và persistence | Status code + query dữ liệu durable | Production proxy, TLS deployment, gateway thật |
| Model mutation | Graph có vi phạm chủ động | Thuộc tính phải FAIL, có witness | Exploit thực tế của app hiện tại |
| Sanitizer C | Memory access/lifetime ở executable | Đúng diagnostic BUGGY; FIXED sạch | C# hoặc toàn bộ input chưa chạy |

Không gọi chỉ status 200 là end-to-end consistency. Khi Order thiếu stock, test
query lại Product và Order để thấy stock không đổi và không có Order persisted.
Khi xóa item bằng quantity=0, test query Cart/CartItems để thấy total=0 và không
còn item. Test constraint kiểm SQLSTATE 23514 và tên CHECK, nên không nhận một
foreign-key error bất kỳ như bằng chứng chặn quantity.

### 6.2. Corpus, budget và mức độ đa dạng

Seed PRNG cố định `20261009` giúp tái lập failure; corpus bao gồm trường hợp biên
và payload lỗi. Budget 25.000 iteration cho từng random campaign là số lần thử,
không phải số input khác nhau hay phần trăm miền input. State transition fuzz có
oracle từ bảng chuyển và invariant; chưa chứng minh vượt qua mọi chuỗi
gateway/database. Không gọi campaign này là coverage-guided fuzzing nếu thuật toán
không dùng coverage để chọn mutation tiếp theo.

### 6.3. Phạm vi test authorization sau mở rộng

Test ownership đã thêm read/retry/reconcile Payment của owner khác; test role
thêm Customer gọi global Order listing, confirm/complete, refund/refund retry và
Product create/update/delete. Các đường này phải trả 403 trước thay đổi nghiệp
vụ. Token hết hạn, forged issuer/audience/signature và việc revoke role khi JWT
còn hạn chưa có đầy đủ scenario. Fixture HTTP thường tắt worker; test process-kill riêng bật worker trong OS process
khởi động lại. PASS đó xác nhận một stale charge được reconcile tự động, không
chứng minh scheduler trong mọi tải hoặc mọi lỗi mạng.

### 6.4. Coverage và ca lỗi môi trường

Nhánh subtotal<0 của validator không reachable sau guard quantity và price dương;
không sửa implementation chỉ để làm số coverage đẹp hơn. Coverage target cần
kèm Include filter và tool command. Với sanitizer, lần sandbox thất bại và lần
ngoài sandbox PASS được phân biệt bằng môi trường thực thi, giữ detect_leaks.
Nếu timeout, tool error hoặc build error xuất hiện, lưu outcome đó riêng; không
chuyển sang controlled rejection bằng catch quá rộng trong harness. CBMC/Valgrind
được chạy trong chuỗi `run_all_checks.py` và có evidence riêng; chúng không làm
thay đổi oracle 22 ca sanitizer.

### 6.5. Process-kill recovery đã thực hiện

Test chặn sau journal charge, xác nhận database còn PROCESSING/PAYMENT_PENDING,
kill process đầu rồi start process mới cùng PostgreSQL và ledger. Worker phải tự
khôi phục SUCCESS/PAID; transaction code giữ nguyên, AttemptCount=1, một payment,
một journal entry byte-for-byte không đổi và stock không được hoàn.

[Evidence restart](../results/process_restart.results.json) ghi verdict, PID khác
nhau và cửa sổ kill. Test host riêng chèn barrier, API sản phẩm không có crash
endpoint. Test chủ động đặt UpdatedAt stale để worker chọn candidate; chưa là
thí nghiệm crash giữa refund và finalize hay lỗi disk/power loss.

### 6.6. Timeout, shutdown và precision đã bổ sung

GatewayTimeoutTests kiểm cả bốn lời gọi với adapter không tuân cancellation,
late charge success chỉ được áp qua reconciliation, và refund timeout chỉ hoàn
kho sau definitive refund. WorkerContinuesAfterHungStatusLookupAndStopsPromptly
xác nhận một candidate treo không chặn candidate tiếp theo. Test shutdown trong
lookup đang chạy xác nhận dừng trước budget shutdown và giữ PROCESSING cho lượt
khôi phục sau, không invent outcome.

MoneyPrecisionTests và HTTP/PostgreSQL regression kiểm fractional cent, trailing
zeroes và persistence. Fuzz random bổ sung 25,005 và oracle kiểm amount là cent;
coverage campaign được đo lại, bao gồm MoneyAmounts. Wrapper PostgreSQL 40001
được quan sát trong race test; classifier có regression riêng cho 40001/40P01,
không nhận 23514/23505 là loại concurrency này.

### 6.7. Admission và audit regression

Test rate dùng host riêng permit nhỏ: login lần hai trả 429/Retry-After, verified
user A bị throttle nhưng user B vẫn dùng bucket riêng. Body tests kiểm cả header
Content-Length và stream thiếu length; exact cap được rewind cho reader. Item
count bị chặn trước vòng xử lý và không có Order persisted.

Audit regression kiểm payment/cancel/refund, correlation server sinh, sentinel
secret không xuất hiện, và SQL trigger chặn UPDATE/DELETE/TRUNCATE với P0001.
Begin-failure test xác nhận không có business record; result-failure test xác nhận
503 dù Order đã commit và còn BEGIN durable. Restart test chờ cả worker SUCCESS
và SYSTEM audit RESULT, không suy chỉ state change là audit đã lưu.

### 6.8. Atomic audit, order replay và concurrency qua hai host

AtomicAuditIdempotencyTests kiểm chứng original receipt sau cancel và qua host mới,
payload conflict, namespace JWT, malformed/multiple keys, tám request cùng key,
COMMIT insert failure rollback stock/order, raw SQL commit/rollback và hai host
cạnh tranh 10 stock với 16 request. AdmissionAuditTests còn mô phỏng RESULT lỗi:
503 sau business commit vẫn có COMMIT audit và retry key cũ trả đơn ban đầu.
Browser checkout regression kiểm syntax toàn app.js và thực thi hàm checkout với
mocked fetch cho network loss/503/double submit/payload change/payment cleanup.
Không gọi browser test là end-to-end HTTP hoặc kết quả chịu tải production.

### 6.9. Restore drill và tải HTTP qua hai OS process

@@OPERATIONAL_TABLE@@

Backup drill khởi tạo PostgreSQL tạm, tạo đơn có receipt, dừng API sau khi gateway
journal có charge nhưng payment còn PROCESSING. Không còn writer, pg_dump custom
và tar ledger tạo snapshot cặp. pg_restore vào database tên khác, ledger giải nén
vào thư mục khác. So sánh hash rows của 9 bảng trước khi bật worker: dữ liệu nghiệp
vụ, user/product/cart, audit và migration history khớp. Bốn audit triggers enabled.
Replay trả receipt cũ, tạo đơn mới kiểm tra identity sequence. Worker chốt SUCCESS
bằng transaction cũ, AttemptCount=1, chỉ một journal entry không đổi; DB nguồn
không đổi sau restore. Archive có dữ liệu test được cleanup, evidence chỉ giữ hash.

Load probe dùng hai OS process API thật trên loopback, chung PostgreSQL và ledger.
Đọc 200 request, tạo đơn 96 request cùng một product hot spot, concurrency=8; không
retry 409 ở workload đo. Quota nâng lên 100000/process để cô lập DB contention.
Scenario rate riêng dùng host quota=5/phút/subject, 20 requests concurrency=4,
assert 5 HTTP 200 + 15 HTTP 429. Created order count, stock deduction và ORDER_INSERT
COMMIT audit khớp. Không có 500/503 trong các scenario đạt assertion.

Percentile dùng nearest-rank; latency đo từ sau khi lấy semaphore đến đọc hết body,
không bao gồm queue wait. Throughput tính theo wall time cả batch; successful/s
loại response >=400. Evidence giữ từng sample status/elapsed_ms để tính lại p95.
Đây là lần đo ngắn có cold-start requests, không benchmark saturation/soak hoặc SLA
nhiều máy. Recovery timing là riêng, không đo payment backlog dưới tải đồng thời.
409 cao trong hot spot cho thấy contention và nhu cầu retry bằng key cũ, không là
bằng chứng bán vượt kho hay một capacity bound của production.

Nguồn: [backup_restore.results.json](../results/backup_restore.results.json),
[load_probe.results.json](../results/load_probe.results.json),
[OperationalDrillTests](../../OrderPaymentMicroservice.IntegrationTests/OperationalDrillTests.cs).

### 6.10. Monitoring, pending age và refund process kill

OperationsTests kiểm anonymous live/ready chỉ có status, unauthenticated 401 và
customer 403 trên operational detail, Admin 200/no-store/không secrets. Clock quan
sát tiến 10 phút tạo oracle stuck mà không sleep. Lookup UNKNOWN hai lần và raw SQL
UpdatedAt refresh không đổi PendingSince. BEGIN thiếu RESULT sinh cảnh báo; lỗi
BEGIN insert tăng process audit counter và không tạo đơn.

RefundRestartTests kill API khi gateway đã ghi refund journal nhưng local chưa
finalize; worker process mới đối soát thành REFUNDED, preserve charge reference,
clear PendingSince, release kho một lần. Refund replay giữ hai journal hashes.
Telemetry recovery_resolved được kiểm chứng. Sau đó dừng PostgreSQL tạm: live 200,
ready/status 503 không lộ connection string. Cửa sổ kill refund và shutdown DB không
chứng minh disk failure, network partition, provider thật hoặc reconnect recovery.
Evidence: [refund_restart.results.json](../results/refund_restart.results.json).

### 6.10.1. PostgreSQL disconnect → reconnect, giữ nguyên API process

@@RECONNECT_TABLE@@

PostgresReconnectTests giữ response gateway sau khi journal charge/refund ghi xong,
dừng cluster PostgreSQL tạm rồi thả response khi DB còn tắt: finalize trả 503.
Replay cùng key trong outage cũng 503. Worker scan lỗi thật, API vẫn live=200.
Bật lại cùng cluster và giữ nguyên API process/pool: ready trở lại 200, worker
lookup đúng key rồi reconcile thành SUCCESS/PAID hoặc REFUNDED. Không sửa timestamp.

Oracle kiểm một payment, AttemptCount=1, keys/charge reference/transaction đúng,
SYSTEM audit RESULT, stock và InventoryStatus khớp. Adapter test đếm lời gọi
charge/refund thực tế; journal hashes giữ nguyên sau recovery và replay. Refund
kiểm đúng một ORDER_UPDATE COMMIT sang REFUNDED và stock trở về giá trị ban đầu.
Quan sát DB dùng kết nối mới độc lập, API dùng pooling suốt outage.

Đây là hai case stop/start local với durable fake provider; không chứng minh
network partition, failover, disk/power loss, mọi crash window hoặc provider thật.
Barrier/counters chỉ nằm ở test executable. Demo: `python3 security-analysis/run_recovery_demo.py`.
Evidence: [charge](../results/postgres_reconnect_charge.results.json),
[refund](../results/postgres_reconnect_refund.results.json),
[phạm vi nghiệm thu](../PHAM_VI_KIEM_CHUNG.md).

### 6.11. So sánh retry có giới hạn với gửi một lần

@@ORDER_RETRY_TABLE@@

Cùng 96 logical requests/scenario, 8 users và 4 products, concurrency=8 qua hai OS
process API. Phases dùng product sets riêng stock 1000/product, request pattern
phân bố mỗi batch qua bốn products; baseline chạy trước. Retry driver giữ key/body,
4 attempts/8 giây, jitter seed 20261013+i. Raw samples giữ final/attempt status,
network latency, backoff và logical latency để tính lại percentile nearest-rank.
Logical p95 bao gồm backoff, throughput order success khác HTTP attempts/s.

Assertions: HTTP 201/409, không duplicate order, stock deduction và COMMIT insert
count khớp số success, attempt nằm trong bound. Không assert retry phải nhanh hơn
hoặc success phải cao hơn: kết quả là quan sát trong lần chạy local. Quota được
nâng để cô lập database contention. Chưa repetitions/order crossover/soak hoặc
confidence interval, không gọi số đo này là production capacity.

OrderRetryContractTests kiểm allowlist SQLSTATE/constraint, wrapped 40001 và legacy
no-key, redaction; HTTP kiểm stock/inactive/mismatch nonretryable. Browser thật
thực thi helper với mocked fetch, kiểm stable key/body, jitter, 4-attempt cap,
không retry business/503, cancel và giữ key sau 429. C# workload mô phỏng policy,
không phải browser-to-API end-to-end measurement.
Nguồn: [order_retry_load.results.json](../results/order_retry_load.results.json),
[checkout_retry.results.json](../results/checkout_retry.results.json).

### 6.12. Rà soát và chạy normal application với Chrome

Normal executable OrderPaymentMicroservice chạy Development, port động và PostgreSQL
tạm. Chrome thực thi signup/login/products/cart/checkout/payment/history qua HTTP
thật; backend và gateway journal cũng được assert. Kiểm cached user JSON hỏng,
reload document mới, logout navbar, chặn hai payment submit và payload không có dữ
liệu thẻ. Browser smoke có 0 uncaught JS exceptions trong flow đạt oracle.

Đã sửa API host/port cố định, cache parse không guard, error body double-read,
card fields không dùng, logout navbar và payment double-submit. Live test dùng
provider mô phỏng, không là provider/browser matrix hoặc production deployment.
External fonts bị block để không phụ thuộc mạng; dữ liệu/process test được cleanup.
Nguồn: [kịch bản thuyết trình và demo](../KICH_BAN_THUYET_TRINH_VA_DEMO.md),
[application_smoke.results.json](../results/application_smoke.results.json).

Với browser network fault, pass criterion không chỉ đếm uncaught JS exception:
control phải tải được card; mỗi fault response phải làm app rời skeleton và render
`#productsGrid .products-state-error` kèm nút thử lại. App dùng request token để
bỏ response cũ và không gọi `loadProducts()` trùng khi khởi tạo/chuyển section.
Harness dùng `Page.reload(ignoreCache)` và chờ error state thay vì chỉ ngủ cố định,
nên fault không hiện trong UI sẽ làm evidence fail dù không có JS exception.

### 6.13. Khoảng trống của Chương 4 — không claim quá mức

Bảng dưới đây chốt đúng trạng thái từng hạng mục. Các lớp test HTTP/PostgreSQL,
browser hoặc model khác **không tự thay thế** một hạng mục còn thiếu.

| Hạng mục | Trạng thái | Bằng chứng / cách đọc | Việc còn thiếu |
| --- | --- | --- | --- |
| Fixed-seed | **Có** | `.NET`: `seed=20261009`, 25.000/campaign, 2.000 chuỗi stateful. HTTP: `seed=20261022`, 10 setup + 40 seed + 9 business flow (51 request) + 500 mutation = 601 request. Seed chỉ tái lập khi cùng source/runtime. | Không có; đây là fuzz sinh dữ liệu có cấu trúc, không phải random byte stream. |
| Coverage-guided | **Có cho C fixture; chưa cho ASP.NET** | `run_c_coverage_fuzz.py` dùng GCC `-fsanitize-coverage=trace-pc`, shared edge map 64 KiB, fork/input và queue theo coverage mới; seed `20261022`, 20.000 mutation, 10 seed, 81 → 148 map entry, 46 input trong queue, 0 crash. Target là `c-memory/fuzz/fixture_target.c`, **không phải** service ASP.NET. Coverlet và HTTP harness vẫn không phải code-coverage-guided. | Instrumentation cho C#/ASP.NET hoặc HTTP thật; source-level edge/path report; mutation/minimization nâng cao. |
| Model-level end-to-end | **Không** | Campaign stateful .NET chạy aggregate Order–Payment–Inventory trong process; trường `scope` ghi rõ không fuzz HTTP/PostgreSQL concurrency/gateway. `http_fuzz.py` là harness riêng, không biến campaign model thành end-to-end. | Harness model gọi service/API thật, kiểm DB và transaction sau từng bước. |
| Đập vào DB + HTTP service thật | **Có trong HTTP fuzz opt-in** | `http_fuzz.results.json`: 601 request qua executable thật, PostgreSQL local thật; 0 HTTP 5xx, 0 vi phạm invariant DB; snapshot cuối có order PAYMENT_PENDING/COMPLETED/CANCELLED/REFUNDED và payment SUCCESS/REFUNDED. Integration/load/reconnect tests vẫn là lớp riêng. | Chưa phải production/TLS/proxy/network partition; chưa chứng minh concurrency toàn cục. |
| Black-box HTTP fuzz | **Có (opt-in)** | `security-analysis/http_fuzz.py`; 10 setup + 40 seed + 9 business flow (51 request) + 500 mutation; auth, corpus, oracle response/DB, replay trace; evidence `http_fuzz.results.json`. | Chưa coverage-guided, chưa RESTler/Schemathesis; mutation set còn bounded. |
| Path/edge coverage chuyên sâu | **Có edge map cho C fixture; chưa code path/edge cho app** | C fixture có transition map từ `trace-pc` (148 map entry), không phải source-level gcov/LLVM edge. App .NET vẫn chỉ có line 91,05%/branch 97,50% cho 5 target; HTTP harness có 108 semantic feature/500 semantic edge, **không phải** code path/edge. | Instrumentation basic-block/edge cho app, đo path bounded và lưu coverage map. |
| Frontend fuzz qua mạng | **Có một phần (CDP opt-in)** | `browser/network_fuzz.py`: 8 scenario (control, malformed JSON, wrong shape, empty, truncated, 500, 503, network error) trên Chrome + API/PostgreSQL thật; control tải 8 card, **7/7 fault scenario** hiển thị `#productsGrid .products-state-error` và nút thử lại, 0 uncaught JS. Pass criterion fail nếu fault không có error state. Vẫn là interception, không phải proxy/WAN. | Mở rộng sang login/cart/payment network faults, proxy thật, mobile/browser matrix. |
| Taint analysis tự động | **Chưa** | Luồng source→sink được review thủ công; sanitizer/ASan không gắn nhãn taint. | DFSan/Semgrep/CodeQL rule và báo cáo source→sink. |

**Coverage-guided C fixture (opt-in).** `run_c_coverage_fuzz.py` biên dịch target
`c-memory/fuzz/fixture_target.c` bằng GCC `-fsanitize-coverage=trace-pc`, dùng
shared map 64 KiB, chạy mỗi input trong process con và chỉ giữ input khi thêm map
entry mới. Lần chạy seed `20261022`, 20.000 mutation, 10 seed: 81 → 148 map entry,
queue 46, 0 crash; baseline random cùng seed/mutation không giữ coverage đạt 146
map entry. Đây là bằng chứng cho feedback loop của engine, **không phải** code
coverage của ASP.NET; map entry là transition basic-block đã băm, không phải báo
cáo source-level edge. Xem
[`c_coverage_fuzz.results.json`](../results/c_coverage_fuzz.results.json) và
[`run_c_coverage_fuzz.py`](../run_c_coverage_fuzz.py).

Cách nói chính xác khi bảo vệ: **“Fuzz hiện tại gồm bốn lớp: structured fixed-seed
in-process cho 5 target .NET, black-box HTTP fixed-seed với PostgreSQL thật,
Chrome CDP network fault fuzzing cho products fetch (có oracle UI error state), và coverage-guided trace-pc
trên một C fixture tổng hợp. Lớp C fixture chứng minh feedback loop/queue theo
edge map hoạt động, nhưng không phải code coverage của ASP.NET. HTTP harness dùng
semantic feedback, chưa phải code-coverage-guided; app chưa có code path/edge
coverage; browser fuzz là interception, không phải proxy/WAN.”** Các test
HTTP/PostgreSQL, load và browser smoke là bằng chứng riêng cho tầng tương ứng;
HTTP fuzz không nâng campaign model thành end-to-end. Vì vậy 25.000 iteration,
601 HTTP request, 148 map entry của C fixture hoặc 97,50% branch không được dùng
để kết luận toàn API, database, frontend hay mọi path đã được kiểm.
