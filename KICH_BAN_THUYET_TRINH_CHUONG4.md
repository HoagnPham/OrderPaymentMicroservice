# Kịch bản thuyết trình Chương 4 — Fuzzing, oracle, coverage, crash

Tài liệu này là **runbook bảo vệ Chương 4**: nói gì, chạy gì, show gì. Thời lượng
gợi ý **8 phút**. Chạy tất cả lệnh từ **gốc repository**:

```bash
cd "$HOME/OrderPaymentMicroservice"
```

Quy ước:

- Dòng `# →` là **kết quả mong đợi**, không phải lệnh.
- Chỉ nói số liệu gắn với file evidence tương ứng.
- Không đọc lại toàn bộ lý thuyết; mỗi lớp chỉ nói 1–3 câu rồi chiếu evidence.

---

## 0. Chuẩn bị máy

Cần có sẵn: `dotnet` (.NET 10 SDK), `python3`, PostgreSQL binaries, Chrome/Chromium,
`gcc`. Kiểm tra nhanh:

```bash
dotnet --version
python3 --version
command -v google-chrome || command -v chromium
command -v gcc
```

Nếu thiếu Chrome hoặc PostgreSQL thì các bước 3.5 (browser CDP) và 3.4 (HTTP fuzz)
sẽ không chạy được; phần còn lại vẫn độc lập.

---

## 1. Pre-run (chạy trước khi lên lớp, 1 lần)

Chạy để evidence mới, tránh vừa nói vừa chờ:

```bash
# Build executable để http_fuzz / browser dùng lại
dotnet build OrderPaymentMicroservice/OrderPaymentMicroservice.csproj --no-restore --nologo

# 1) Fuzz .NET có cấu trúc + coverage được include
python3 security-analysis/run_fuzzing.py

# 2) Coverage line/branch riêng cho validator
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~InputValidatorTests|FullyQualifiedName~MoneyPrecisionTests' \
  /p:CollectCoverage=true \
  /p:CoverletOutput=../security-analysis/results/input-validator \
  /p:CoverletOutputFormat=cobertura \
  '/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator%2c[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.MoneyAmounts'
python3 security-analysis/collect_validator_coverage.py

# 3) Black-box HTTP fuzz + DB oracle
python3 security-analysis/http_fuzz.py --iterations 500 --no-build

# 4) Browser: checkout mocked + CDP network fault
python3 security-analysis/verify_checkout.py
python3 security-analysis/browser/network_fuzz.py

# 5) Coverage-guided trên C fixture
python3 security-analysis/run_c_coverage_fuzz.py --iterations 20000
```

Nếu một bước fail, **không tự nhận PASS**; đọc `results/*.json` tương ứng để biết
lý do (thiếu Chrome/PostgreSQL, chưa build, timeout…).

---

## 2. Bảng tổng — timeline 8 phút

| Phút | Nội dung | Nói gì (1 câu) | Chạy | Show |
| --- | --- | --- | --- | --- |
| 0:00–0:30 | Mở khung | “Bốn lớp fuzz: structured .NET → HTTP black-box → browser CDP → coverage-guided C fixture; mỗi lớp một phạm vi.” | — | — |
| 0:30–2:00 | Fuzz .NET | 5 target, seed cố định, oracle, 25.000 vòng/campaign + 2.000 chuỗi stateful. | `python3 security-analysis/run_fuzzing.py` | `results/application_fuzzing.results.json` |
| 2:00–3:00 | Coverage line/branch | Coverlet line/branch cho 5 target; 91,05% / 97,50%; line/branch ≠ path/edge. | lệnh mục 3.3 | `results/coverage_summary.json`, 2 file Cobertura |
| 3:00–4:30 | HTTP black-box | Executable + PostgreSQL thật; 601 request; oracle response + DB; 0 HTTP 5xx. | `python3 security-analysis/http_fuzz.py --iterations 500 --no-build` | `results/http_fuzz.results.json` |
| 4:30–5:30 | Browser CDP | Chrome thật chặn `/api/products`; control 8 card, 7/7 fault hiện error state + retry. | `python3 security-analysis/browser/network_fuzz.py` | `results/browser_network_fuzz.results.json` |
| 5:30–7:00 | Coverage-guided | GCC `trace-pc`, edge map, queue; 20.000 mutation, 81 → 148 edge, 0 crash; chỉ C fixture. | `python3 security-analysis/run_c_coverage_fuzz.py --iterations 20000` | `results/c_coverage_fuzz.results.json` |
| 7:00–7:40 | Crash | Crash do input chạm bug, không do nhiều dữ liệu; demo C chỉ để thấy harness đếm crash. | `python3 -c ...` + mục 4 (optional) | `crash_policy`, `/tmp/fuzz-crash` |
| 7:40–8:00 | Chốt | Nói rõ 25.000/601/148/97,50% đều có phạm vi riêng. | — | — |

---

## 3. Lời thoại chi tiết

### 3.1. Mở khung (0:00–0:30)

**Nói:**

> “Chương 4 của em gồm bốn lớp fuzz với mức bằng chứng khác nhau: structured .NET
> in-process, HTTP black-box đập vào executable + PostgreSQL thật, browser CDP
> interception, và coverage-guided trên C fixture. Mỗi lớp có oracle riêng; em sẽ
> nói luôn đâu là giới hạn để không hiểu sai kết quả.”

### 3.2. Fuzz .NET có cấu trúc (0:30–2:00)

**Chạy:**

```bash
python3 security-analysis/run_fuzzing.py
# → 4/4 PASS, seed 20261009
# → line 91.05%, branch 97.50%
# → results/application_fuzzing.results.json
```

**Nói:**

> “Đây là fuzzing có cấu trúc, in-process, PRNG seed cố định, có oracle. Em fuzz 5
> target: `InputValidator`, `PaymentValidator`, `MoneyAmounts`, `OrderStateMachine`,
> `PaymentStateMachine`. Random campaign chạy 25.000 vòng mỗi target; thêm 2.000
> chuỗi stateful 20 bước kiểm invariant Order + Payment + Inventory.”

**Show `results/application_fuzzing.results.json`:**

- `seed = 20261009`
- `tests = {passed: 4, failed: 0, total: 4}`
- `crash_policy = "ArgumentException is a controlled rejection; timeout, any other exception, invariant mismatch or test failure is a crash."`
- `coverage = {line_percent: 91.05, branch_percent: 97.5}`

**Câu chốt:**

> “`ArgumentException` là input sai bị từ chối **hợp lệ**, không tính crash. Crash
> thật chỉ là timeout, exception ngoài dự kiến, invariant mismatch hoặc test fail.
> Khi fail, xUnit in `FUZZ FAILURE campaign=...; seed=20261009; iteration=n; input={...}`
> nên tái lập được.”

### 3.3. Coverage line/branch (2:00–3:00)

**Chạy** (nếu chưa chạy ở pre-run):

```bash
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~InputValidatorTests|FullyQualifiedName~MoneyPrecisionTests' \
  /p:CollectCoverage=true \
  /p:CoverletOutput=../security-analysis/results/input-validator \
  /p:CoverletOutputFormat=cobertura \
  '/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator%2c[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.MoneyAmounts'

python3 security-analysis/collect_validator_coverage.py
# → InputValidator branch 97.22, MoneyAmounts branch 100.0
# → results/coverage_summary.json
```

**Nói:**

> “Coverlet đo **line/branch**, không đo path/edge. Con số 91,05% line và 97,50%
> branch ở phần fuzz chỉ áp cho 5 target được include, không phải toàn app.
> Riêng lượt unit của validator thì InputValidator đạt 97,22% branch, helper
> `MoneyAmounts` đạt 100%.”

**Show:** `results/coverage_summary.json` (khóa `branch_coverage_percent`,
`uncovered_branches`, `limitations`) và `input-validator.cobertura.xml`.

**Câu chốt:**

> “97,50% branch không có nghĩa 97,50% đường thực thi đã được kiểm. Muốn nói path
> phải đo path; muốn nói app-wide phải include toàn bộ assembly.”

### 3.4. Black-box HTTP fuzz + DB oracle (3:00–4:30)

**Chạy:**

```bash
dotnet build OrderPaymentMicroservice/OrderPaymentMicroservice.csproj --no-restore --nologo
python3 security-analysis/http_fuzz.py --iterations 500 --no-build
# → passed=true, 601 request, 108 feature, 500 semantic edge, 0 lỗi 5xx
# → DB invariant sau mỗi request
# → results/http_fuzz.results.json
```

**Nói:**

> “Đây là black-box thật: script khởi động executable bình thường và một PostgreSQL
> tạm, rồi chỉ nói chuyện qua HTTP. Nó chạy 500 mutation + corpus, tổng 601 request.
> Mỗi request có oracle: kiểm status/response và kiểm invariant DB sau đó.”

**Show `results/http_fuzz.results.json`:**

- `requests_executed = 601`
- `coverage.kind = "SEMANTIC_FEEDBACK_NOT_CODE_COVERAGE"`
- `coverage.unique_features = 108`, `coverage.unique_semantic_edges = 500`
- `database.invariant_checks = 272`, `database.violations = []`
- `http.5xx = 0`

**Câu chốt:**

> “108 feature và 500 semantic edge là **semantic feedback**, không phải code
> coverage. 601 request chỉ là budget local đã chạy; chưa có TLS/proxy/WAN nên không
> được nói là đã phủ toàn API.”

### 3.5. Browser CDP network fault (4:30–5:30)

**Chạy:**

```bash
python3 security-analysis/browser/network_fuzz.py
# → 8 scenario (control + malformed JSON, wrong shape, empty, truncated,
#   HTTP 500, HTTP 503, network error)
# → 7/7 fault hiện error state + nút thử lại, 0 uncaught JS
# → results/browser_network_fuzz.results.json
```

**Nói:**

> “Em dùng Chrome thật và CDP để chặn response `/api/products` ở tầng network, rồi
> kiểm **UI có xử lý lỗi hay không**. Control tải đủ 8 card; 7 fault scenario đều
> phải hiện error state trong `#productsGrid` kèm nút thử lại. Harness fail nếu UI
> không hiện lỗi, nên đây là oracle thật chứ không chỉ chụp ảnh.”

**Show `results/browser_network_fuzz.results.json`:**

- `passed = true`
- `checks.passed = true`, `checks.all_javascript_errors = []`
- `checks.outcomes[*]`: control có `product_cards = 8`; 7 fault có
  `ui_handles_fault = true` và `product_state_class = "products-state products-state-error"`
- `scope.not_claimed = "real proxy/WAN, TLS, mobile/browser matrix, code coverage, production deployment"`

**Show UI trực tiếp (optional, nếu có thời gian):** mở app, Chrome DevTools →
Network → **Block request URL** → `*/api/products` → reload → chỉ ra error state +
nút thử lại.

**Câu chốt:**

> “Đây là CDP fetch interception, không phải proxy/WAN thật. Mới fault path
> `products`; login/cart/payment network faults là việc còn lại.”

### 3.6. Coverage-guided trên C fixture (5:30–7:00)

**Chạy:**

```bash
python3 security-analysis/run_c_coverage_fuzz.py --iterations 20000
# → guided: edges 81 → 148, queue 46, crashes 0
# → so với random baseline cùng seed
# → results/c_coverage_fuzz.results.json
```

**Nói:**

> “Đây là coverage-guided đúng nghĩa nhưng trên **C fixture tổng hợp**. GCC
> `trace-pc`, shared edge map, fork từng input, queue theo coverage mới. 20.000
> mutation, 81 → 148 map entry, queue 46, 0 crash; cùng seed tái lập đúng.”

**Show `results/c_coverage_fuzz.results.json`:**

- `guided.initial_edges = 81`, `guided.final_edges = 148`, `guided.new_edges = 67`
- `guided.queue_size = 46`, `guided.crashes = 0`
- phần `random` / `comparison` để thấy baseline random

**Câu chốt:**

> “148 map entry là map entry của C fixture, **không phải** source-level edge của
> ASP.NET. App ASP.NET vẫn chưa có code-coverage-guided/path-edge.”

### 3.7. Crash (7:00–7:40)

**Chạy:**

```bash
python3 -c "import json; print(json.load(open('security-analysis/results/application_fuzzing.results.json'))['crash_policy'])"
# → ArgumentException is a controlled rejection; timeout, any other exception,
#   invariant mismatch or test failure is a crash.
```

**Nói:**

> “Crash xảy ra khi input chạm bug, không phải vì nhiều dữ liệu. Với .NET, input
> sai bị `ArgumentException` là reject hợp lệ. Nếu muốn thấy harness đếm crash, em
> có demo C cố ý SIGSEGV; fuzzer fork từng input nên vẫn chạy hết và báo `crashes>0`
> nhưng process không sập.”

Nếu muốn chạy demo C đầy đủ, xem **mục 4** dưới đây.

### 3.8. Chốt (7:40–8:00)

> “Tóm lại, bốn lớp fuzz là structured .NET, HTTP black-box, browser CDP và
> coverage-guided C fixture. Mỗi lớp có phạm vi riêng. 25.000 iteration, 601 HTTP
> request, 148 map entry C fixture và 97,50% branch của 5 target không dùng để kết
> luận toàn API, database hay frontend. App ASP.NET chưa code-coverage-guided,
> chưa symbolic/concolic, chưa taint tự động.”

---

## 4. Demo crash C đầy đủ (optional, không sửa repo)

```bash
REPO="$PWD"
mkdir -p /tmp/fuzz-crash && cd /tmp/fuzz-crash
cp "$REPO"/security-analysis/c-memory/fuzz/{cov_runtime.c,cov_runtime.h,coverage_fuzz.c,fixture_target.h} .

cat > crash_target.c <<'EOF'
#include <stddef.h>
#include <stdint.h>

int fuzz_target(const uint8_t *data, size_t size) {
    if (size == 0) return 0;
    if (data[0] & 1u) {                 /* một nửa input đi vào nhánh crash */
        volatile int *p = (volatile int *)0;
        *p = 1;                         /* SIGSEGV */
    }
    if (size > 1 && data[1] == 0x7f) return 2;
    return 1;
}

size_t fixture_seed_count(void) { return 1; }

const uint8_t *fixture_seed(size_t index, size_t *length) {
    static const uint8_t seed[] = {0x02, 0x01, 0x03, 0x04};
    if (index != 0) return NULL;
    if (length) *length = sizeof(seed);
    return seed;
}
EOF

gcc -std=c11 -O0 -g -Wall -Wextra -fno-pie -c cov_runtime.c -o cov_runtime.o
gcc -std=c11 -O0 -g -Wall -Wextra -fno-pie -c coverage_fuzz.c -o coverage_fuzz.o
gcc -std=c11 -O0 -g -Wall -Wextra -fno-pie -fno-omit-frame-pointer \
    -fsanitize-coverage=trace-pc -c crash_target.c -o crash_target.o
gcc -no-pie cov_runtime.o coverage_fuzz.o crash_target.o -o cov_fuzz_crash

./cov_fuzz_crash --seed 20261022 --iterations 500 --mode guided | grep -E 'crashes|edges'
# → crashes=<n> với n > 0; fuzzer vẫn chạy hết 500 iteration, process exit 0

cd "$REPO"
rm -rf /tmp/fuzz-crash
```

**Nói:** demo này chỉ để thấy harness **đếm** crash; chưa lưu/minimize input gây
crash, và app ASP.NET chưa có coverage-guided crash fuzzing.

---

## 5. Câu hỏi nhanh (Q&A)

| Câu hỏi | Trả lời ngắn |
| --- | --- |
| Sao chạy 25.000 vòng không thấy crash? | Fuzzing không phải “càng nhiều dữ liệu càng chắc crash”. Chưa crash chỉ nói chưa chạm bug trong budget/oracle. Input sai bị `ArgumentException` là reject hợp lệ. |
| 601 request đã phủ toàn API chưa? | Chưa. Đó là black-box local trên một executable + PostgreSQL tạm, 500 mutation, có oracle response/DB; chưa TLS/proxy/WAN/network partition. |
| 97,50% branch là coverage toàn app? | Không. Chỉ 5 target được include; app ASP.NET chưa có path/edge coverage. |
| 148 map entry là coverage C#? | Không. Đó là map entry của C fixture qua `trace-pc`. |
| Browser đã test mọi network fault? | Chưa. Mới fault `api/products`; 8 scenario gồm control + 7 fault, harness fail nếu UI không hiện error state. |
| Có taint analysis tự động hay symbolic fuzzing? | Chưa. Luồng source→sink mới là review thủ công; Z3 kiểm số học riêng, chưa nối vào generator fuzz. |
| Long-email có phải fuzzer tự tìm ra? | Không. Đó là demo deterministic cho CWE-121: email 128 ký tự làm `user_buggy` báo `stack-buffer-overflow`; `user_fixed` reject trước `strcpy`. |
| Fuzz .NET có phải end-to-end? | Không. In-process, gọi trực tiếp target; stateful campaign kiểm aggregate trong process. |

---

## 6. Không được nói khi bảo vệ

- “25.000 HTTP request” — đó là iteration ở mức hàm/state machine.
- “601 request chứng minh toàn API an toàn” — chỉ là budget/corpus đã chạy.
- “97,50% branch toàn hệ thống” — chỉ 5 target được đo.
- “148 map entry là coverage ASP.NET” — chỉ là C fixture.
- “Browser là proxy/WAN thật” — chỉ là CDP interception cho products fetch.
- “Fuzzer tự tìm ra lỗi long-email” — đó là demo deterministic CWE-121.

---

## 7. Checklist 10 phút trước khi bảo vệ

- [ ] Đã chạy pre-run mục 1, tất cả script exit 0.
- [ ] `results/application_fuzzing.results.json` có `passed=true`, `tests=4/4`.
- [ ] `results/coverage_summary.json` có `passed=true` (InputValidator ≥ 90% branch).
- [ ] `results/http_fuzz.results.json` có `passed=true`, `http.5xx=0`, `database.violations=[]`.
- [ ] `results/browser_network_fuzz.results.json` có `passed=true`, `checks.passed=true`.
- [ ] `results/c_coverage_fuzz.results.json` có `guided.crashes=0`, `final_edges > initial_edges`.
- [ ] Đã mở sẵn 5 file JSON trên (tab/terminal) để chiếu nhanh.
- [ ] Đã đọc bảng “Không được nói” mục 6.
- [ ] Nếu demo UI trực tiếp: Chrome DevTools đã mở, biết cách Block `*/api/products`.
- [ ] Nếu định chạy demo C crash: `gcc` sẵn sàng, biết dọn `/tmp/fuzz-crash`.

---

## 8. Tệp liên quan

- Lệnh đầy đủ 4 chương: [`TonghopLenhCMD.md`](TonghopLenhCMD.md) mục 4.1–4.8.
- Giải thích lý thuyết Chương 4: [`HUONG_DAN_DU_AN_VA_KIEM_CHUNG_4_CHUONG.md`](HUONG_DAN_DU_AN_VA_KIEM_CHUNG_4_CHUONG.md) mục E.1–E.10.
- Kịch bản bảo vệ tổng: [`security-analysis/KICH_BAN_THUYET_TRINH_VA_DEMO.md`](security-analysis/KICH_BAN_THUYET_TRINH_VA_DEMO.md).
