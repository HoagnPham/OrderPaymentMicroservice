# Lệnh chạy demo 4 chương — Ubuntu

Tài liệu này **chỉ chứa lệnh chạy** cho máy Ubuntu đã cài đủ tool. Nội dung bám
theo 4 chương của học phần (ATPM slide) nhưng **không đọc và không phụ thuộc**
thư mục `ATPM/`. Chạy từ gốc repository; ví dụ đường dẫn
`$HOME/OrderPaymentMicroservice` — đổi lại nếu clone chỗ khác.

Quy ước:

- Mỗi block chạy tuần tự trong `bash`.
- Giữ **cùng một terminal** để dùng lại các biến `export` ở mục 0.
- Dòng bắt đầu bằng `# →` là **kết quả mong đợi**, không phải lệnh.
- Không chạy song song các lệnh `dotnet test`/coverage/fuzz (dùng chung assembly).
- Các script ghi lại `security-analysis/results/*.json`; đó là evidence mới, không
  phải lỗi.

Thứ tự gợi ý: mục 0 → chương 1 → 2 → 3 → 4 → mục 5 (recovery/vận hành) → mục 6
(full suite + report). Tổng khoảng 20–25 phút.

---

## 0. Chuẩn bị (một lần)

```bash
cd "$HOME/OrderPaymentMicroservice"

sudo apt-get update
sudo apt-get install -y build-essential python3 cppcheck cbmc valgrind z3 \
  postgresql postgresql-client default-jre curl git

# Chrome/Chromium cho test browser (cài một trong hai nếu chưa có)
command -v google-chrome || command -v chromium || sudo snap install chromium
```

Kiểm tra tool:

```bash
dotnet --version            # → 10.x
python3 --version           # → 3.10+
gcc --version; make --version
cppcheck --version; cbmc --version; valgrind --version; z3 -version
java -version; psql --version
command -v google-chrome || command -v chromium
```

SAT pipeline (chương 3):

```bash
sudo apt-get install -y cadical || true
if ! command -v drat-trim >/dev/null 2>&1; then
  git clone --depth 1 https://github.com/marijnheule/drat-trim.git /tmp/drat-trim
  make -C /tmp/drat-trim
  sudo install -m755 /tmp/drat-trim/drat-trim /usr/local/bin/drat-trim
fi
command -v cadical; command -v drat-trim
```

TLC (chương 1/3), JAR không nằm trong Git:

```bash
mkdir -p tmp/tools
curl -L --fail -o tmp/tools/tla2tools.jar \
  https://github.com/tlaplus/tlaplus/releases/download/v1.7.4/tla2tools.jar
java -jar tmp/tools/tla2tools.jar -version
export TLA2TOOLS_JAR="$PWD/tmp/tools/tla2tools.jar"
```

PostgreSQL binaries cho integration/recovery (nếu không dùng Docker):

```bash
export POSTGRES_BIN="$(ls -d /usr/lib/postgresql/*/bin | sort -V | tail -1)"
export PATH="$POSTGRES_BIN:$PATH"
"$POSTGRES_BIN/initdb" --version
```

Build:

```bash
dotnet restore OrderPaymentMicroservice.slnx
dotnet build OrderPaymentMicroservice.slnx --no-restore --nologo
# → Build succeeded. 0 Warning(s), 0 Error(s) — 4 project
```

---

## 1. Chương 1 — Mô hình hóa, verification, validation

### 1.1. Đặc tả workbook: requirement, state, CTL, mutex, sinh mã, số học

```bash
python3 security-analysis/verify_teacher_spec.py
# → Teacher spec compatibility: PASS (10 sheets, 18 states, 10 CTL properties, 50 test cases)
# → Intentional detections: CWE-667, CWE-693 at ['conflict'] (CTL false: AG(!violation))
# → security-analysis/results/teacher_spec_verification.json
```

Xem chi tiết mutex / hàm sinh mã / kiểm tra số học:

```bash
python3 - <<'PY'
import json
d = json.load(open('security-analysis/order_payment_pipeline_spec.json'))
print('mutex:', d['mutual_exclusion'])
for f in d['functions']:
    print(f"function: {f['name']} | {f['language']} {f['kind']} | buffer={f['buffer_size']} | bug={f['bug']}")
for a in d['arithmetic_checks']:
    print(f"arithmetic: {a['name']} | {a['condition']} | overflow={a['overflow_expr']}")
PY
# → mutex 2 lock atom: main_locked / log_locked
# → 7 hàm C sinh mã; 6 kiểm tra số học
```

Sinh lại workbook (tùy chọn — ghi đè file .xlsx):

```bash
python3 security-analysis/create_order_payment_spec.py
python3 security-analysis/verify_teacher_spec.py
```

### 1.2. Recovery model: toàn bộ state space, property, mutation

```bash
python3 security-analysis/verify_recovery_model.py
# → 14 state, 35 transition, 7 functional contract
# → P1..P15 PASS (an toàn, bằng chứng thanh toán, inventory, terminal, determinism)
# → 6 mutation DETECTED: BUG-CANCELLED-PAID, BUG-WRONG-AMOUNT,
#   BUG-DUPLICATE-SUCCESS, BUG-RESOURCE-CONFLICT,
#   BUG-PAID-FAILED-WITHOUT-REFUND, BUG-UNKNOWN-RELEASED
# → order_payment_recovery_model.results.json + order_payment_state_response_matrix.csv
```

Xem ma trận state → response:

```bash
head -5 security-analysis/results/order_payment_state_response_matrix.csv
```

### 1.3. Liveness — đọc đúng ngữ nghĩa

```bash
python3 security-analysis/audit_liveness.py
# → Liveness audit: PASS
# → Raw AG AF terminal: False
# → Progress-only AG AF terminal: True
```

### 1.4. Threat model

```bash
python3 security-analysis/verify_threat_model.py
# → Threat model: PASS (6 assets, 4 boundaries, 9 threats, 6/6 STRIDE)
```

### 1.5. TLA+ / TLC (OrderPayment.tla + Mutex/CTL trong workbook)

```bash
python3 security-analysis/run_tlc.py --jar "$TLA2TOOLS_JAR" --timeout 120
# → Model checking completed. No error has been found.
# → 35 states generated, 20 distinct states, depth 9
# → OrderPayment.cfg kiểm 5 INVARIANT + PROPERTY Termination
```

### 1.6. Validation động: unit + integration

```bash
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore --nologo
# → Passed! - Failed: 0, Passed: 149, Skipped: 0, Total: 149

dotnet test OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj \
  --no-restore --nologo --blame-hang-timeout 90s
# → Passed! - Failed: 0, Passed: 35, Skipped: 0, Total: 35
```

---

## 2. Chương 2 — Stack, heap, lỗi bộ nhớ (5 module C)

### 2.1. Build GCC + sanitizer và chạy 22 case

```bash
make -C security-analysis/c-memory clean all
# → 5 binary _buggy + 5 _fixed, flags: -Wall -Wextra + ASan/UBSan/LSan

make -C security-analysis/c-memory test
# → 22/22 PASS: 10 case BUGGY/FIXED + 12 case biên product/user
# → c-memory/evidence/dynamic-analysis.json
```

5 lỗi cố ý:

| Module  | Lỗi                      | Diagnostic mong đợi                        |
| ------- | ------------------------- | -------------------------------------------- |
| order   | rò rỉ bộ nhớ          | `LeakSanitizer: detected memory leaks`     |
| payment | double free               | `AddressSanitizer: attempting double-free` |
| cart    | use-after-free            | `AddressSanitizer: heap-use-after-free`    |
| product | tràn heap do `strcpy`  | `AddressSanitizer: heap-buffer-overflow`   |
| user    | tràn stack do `strcpy` | `AddressSanitizer: stack-buffer-overflow`  |

Chạy riêng một cặp BUGGY/FIXED để xem report (BUGGY exit khác 0):

```bash
ASAN_OPTIONS=detect_leaks=1:halt_on_error=1 security-analysis/c-memory/build/order_buggy; echo "exit=$?"
# → LeakSanitizer report + exit khác 0

ASAN_OPTIONS=detect_leaks=1:halt_on_error=1 security-analysis/c-memory/build/order_fixed; echo "exit=$?"
# → exit=0

ASAN_OPTIONS=detect_leaks=1:halt_on_error=1 security-analysis/c-memory/build/cart_buggy; echo "exit=$?"
# → heap-use-after-free
```

### 2.2. Static analysis: GCC `-Wall -Wextra -Wpedantic` + Cppcheck + layout

```bash
make -C security-analysis/c-memory static-check
# → pattern scanner + cppcheck --enable=all + gcc -fsyntax-only + layout probe
# → BUGGY vi phạm đúng rule, FIXED sạch
# → c-memory/evidence/static-analysis.json
```

Xem finding:

```bash
python3 -m json.tool security-analysis/c-memory/evidence/static-analysis.json | head -60
```

### 2.3. CBMC có bound (unwind 64)

```bash
make -C security-analysis/c-memory bmc-check
# → 10/10 PASS
# → 5 BUGGY: VERIFICATION FAILED đúng lỗi
# → 5 FIXED: VERIFICATION SUCCESSFUL trong bound
# → c-memory/evidence/bmc-analysis.json
```

### 2.4. Valgrind Memcheck

```bash
make -C security-analysis/c-memory valgrind-check
# → 10/10 PASS (5 BUGGY có lỗi đúng loại, 5 FIXED sạch)
# → c-memory/evidence/valgrind-analysis.json
```

### 2.5. Chạy cả 3 lớp chuyên sâu

```bash
make -C security-analysis/c-memory deep-check
# → bmc-check + valgrind-check + sat-check (mục 3.4) + coverage-fuzz (mục 4.5)
```

---

## 3. Chương 3 — Static analysis, SAT, SMT, BMC, Z3

### 3.1. Z3 cho payment

```bash
z3 OrderPaymentMicroservice/Security/PaymentVerification.z3 | tail -30
# → TEST1_VALID_PAID sat
# → TEST2..TEST5 unsat; TEST6_INT32_SUBTOTAL_OVERFLOW_EXISTS sat; TEST7 unsat

python3 security-analysis/verify_payment_smt.py
# → SMT verification: 7/7 PASS
# → results/payment_verification.results.json
```

### 3.2. Z3 cho tồn kho / duplicate lines

```bash
z3 OrderPaymentMicroservice/Security/InventoryVerification.z3
# → DUPLICATE_LINES_BASELINE_OVERSOLD sat (stock=10, q1=6, q2=6)
# → AGGREGATED_STOCK_CANNOT_BE_NEGATIVE unsat
# → AGGREGATED_EXACT_STOCK_ACCEPTED sat

python3 security-analysis/verify_inventory_smt.py
# → Inventory SMT: 3/3 PASS
# → results/inventory_verification.results.json
```

### 3.3. TLA+ / TLC

```bash
python3 security-analysis/run_tlc.py --jar "$TLA2TOOLS_JAR" --timeout 120
# → 35 states generated, 20 distinct states, depth 9
```

### 3.4. CNF/DIMACS + CaDiCaL + DRAT + UNSAT core

```bash
make -C security-analysis/c-memory sat-check
# → 10/10 PASS
# → BUGGY: CaDiCaL SAT; FIXED: UNSAT + DRAT "s VERIFIED" + UNSAT core kiểm lại
# → c-memory/evidence/sat-pipeline.json
```

Xem một proof:

```bash
ls -lh security-analysis/c-memory/evidence/sat/
head -3 security-analysis/c-memory/evidence/sat/cart_fixed.drat.log
```

---

## 4. Chương 4 — Fuzzing, oracle, coverage, crash

### 4.1. Bốn test fuzz của `SecurityFuzzingTests.cs`

```bash
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~SecurityFuzzingTests.SeedCorpusMatchesExpectedValidatorOutcome'
# → 1/1 PASS: corpus seed khớp oracle validator

dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~SecurityFuzzingTests.SeededOrderAndPaymentValidatorCampaignHasNoUnexpectedCrash'
# → 1/1 PASS: 25.000 iteration × 2 validator, seed 20261009

dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~SecurityFuzzingTests.SeededStateMachineCampaignMatchesIndependentTransitionTables'
# → 1/1 PASS: state machine đối chiếu bảng transition độc lập

dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~SecurityFuzzingTests.SeededStatefulOrderPaymentSequencesRespectAggregateInvariants'
# → 1/1 PASS: 2.000 sequence × 20 bước, invariant Order+Payment+Inventory
```

Chạy cả 4 + coverage trong một lệnh:

```bash
python3 security-analysis/run_fuzzing.py
# → 4/4 PASS, seed 20261009
# → line 91.05%, branch 97.50%
# → results/application_fuzzing.results.json + application-fuzzing-coverage.cobertura.xml
```

### 4.2. Coverage line/branch riêng cho validator

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

### 4.3. Black-box HTTP fuzz + DB oracle

```bash
dotnet build OrderPaymentMicroservice/OrderPaymentMicroservice.csproj --no-restore --nologo

python3 security-analysis/http_fuzz.py --iterations 500 --no-build
# → passed=true, 601 request, 108 feature, 500 semantic edge, 0 lỗi 5xx
# → DB invariant sau mỗi request
# → results/http_fuzz.results.json
```

### 4.4. Browser: checkout mocked + CDP network fault + smoke thật

```bash
python3 security-analysis/verify_checkout.py
# → PASS: mocked fetch, retry/key/payload/409/jitter/double-submit

python3 security-analysis/browser/network_fuzz.py
# → 8 scenario (control + malformed JSON, wrong shape, empty, truncated,
#   HTTP 500, HTTP 503, network error)
# → 7/7 fault hiện error state + nút thử lại, 0 uncaught JS
# → results/browser_network_fuzz.results.json

dotnet test OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~ApplicationSmokeTests' \
  --blame-hang-timeout 90s
# → Chrome headless + API + PostgreSQL thật: register → cart → order → payment → history
# → results/application_smoke.results.json
```

### 4.5. Coverage-guided trace-pc trên C fixture

```bash
python3 security-analysis/run_c_coverage_fuzz.py --iterations 20000
# → guided: edges 81 → 148, queue 46, new_coverage_inputs > 0, crashes 0
# → so với random baseline cùng seed
# → results/c_coverage_fuzz.results.json
```

### 4.6. Giả định fuzzing bị crash

**Chính sách crash của project** (in từ evidence):

```bash
python3 -c "import json; print(json.load(open('security-analysis/results/application_fuzzing.results.json'))['crash_policy'])"
# → ArgumentException is a controlled rejection; timeout, any other exception,
#   invariant mismatch or test failure is a crash.
```

Với .NET, khi campaign fail, xUnit in đúng một dòng dạng
`FUZZ FAILURE campaign=validator; seed=20261009; iteration=<n>; input={...}` —
seed cố định nên **tái lập được** bằng đúng lệnh ở mục 4.1.

Minh họa crash **không sửa repo**: dựng target C cố ý SIGSEGV rồi chạy đúng
harness `coverage_fuzz.c` (harness fork từng input nên crash không giết fuzzer):

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

./cov_fuzz_crash --seed 20261022 --iterations 500 --mode guided
# → crashes=<n> với n > 0; fuzzer vẫn chạy hết 500 iteration, process exit 0
# → input gây crash KHÔNG được đưa vào queue coverage

./cov_fuzz_crash --seed 20261022 --iterations 500 --mode guided | grep -E 'crashes|edges'
# → cùng seed ⇒ cùng crashes/edges (tái lập)

cd "$REPO"
rm -rf /tmp/fuzz-crash
```

Giới hạn: harness C chỉ **đếm** crash trong tiến trình con, chưa lưu/minimize
input gây crash; app ASP.NET chưa có coverage-guided crash fuzzing. Crash của
process thật (kill API) được kiểm ở mục 5.1–5.2.

### 4.7. Kịch bản nói – chạy – show cho Chương 4

Bản runbook đầy đủ kèm timeline, lời thoại từng phần và checklist trước giờ bảo vệ:
[`KICH_BAN_THUYET_TRINH_CHUONG4.md`](KICH_BAN_THUYET_TRINH_CHUONG4.md).

Mục tiêu 6–8 phút: cho thấy có fuzzing thật, oracle thật và coverage thật, nhưng
đồng thời nói rõ đâu là giới hạn. Chỉ nói số liệu gắn với file evidence tương ứng.

| Thứ tự                     | Nói gì (ngắn)                                                                                                                                                                                                                 | Chạy                                                                                               | Show                                                                                                                                                                                                                                               |
| ---------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1. Fuzz .NET có cấu trúc  | “Em fuzz 5 target, seed `20261009`, oracle phân biệt input bị từ chối đúng bằng `ArgumentException` với crash thật; 25.000 vòng mỗi random campaign và 2.000 chuỗi stateful.”                                | `python3 security-analysis/run_fuzzing.py` (hoặc 4 lệnh filter ở mục 4.1 nếu ít thời gian) | `SecurityFuzzingTests.cs`, `FuzzCorpus/order_requests.json`, `results/application_fuzzing.results.json` (`tests`, `seed`, `crash_policy`, `coverage`)                                                                                |
| 2. Coverage line/branch      | “Coverlet chỉ đo line/branch cho 5 target; 91,05% line / 97,50% branch; InputValidator 97,22% branch là lượt unit riêng. Line/branch không phải path/edge.”                                                            | mục 4.2 (nếu còn thời gian)                                                                     | `results/coverage_summary.json`, `application-fuzzing-coverage.cobertura.xml`, `input-validator.cobertura.xml`                                                                                                                               |
| 3. HTTP black-box fuzz       | “Executable + PostgreSQL thật; 601 request; oracle kiểm response và DB invariant; 0 HTTP 5xx; đây là semantic feedback, **không phải** code-coverage-guided.”                                                    | `python3 security-analysis/http_fuzz.py --iterations 500 --no-build`                              | `results/http_fuzz.results.json`: `requests_executed=601`, `coverage.unique_features=108`, `coverage.unique_semantic_edges=500`, `database.invariant_checks=272`, `database.violations=[]`, `http.5xx=0`                             |
| 4. Browser CDP network fault | “Chrome thật chặn response `/api/products`; control tải 8 card, **7/7 fault** hiện error state + nút thử lại; harness fail nếu UI không hiện lỗi; đây là CDP interception, không phải proxy/WAN.”      | `python3 security-analysis/browser/network_fuzz.py`                                               | `results/browser_network_fuzz.results.json`: `passed=true`, `checks.outcomes[*].scenario_ok`, `product_state_text`, `all_javascript_errors=[]`; muốn show UI trực tiếp thì mở app rồi DevTools block `*/api/products` và reload |
| 5. Coverage-guided C fixture | “GCC `trace-pc`, shared edge map, fork/input, queue theo coverage mới; 20.000 mutation, 81 → 148 map entry, queue 46, 0 crash. Đây là engine trên **C fixture tổng hợp**, không phải coverage của ASP.NET.” | `python3 security-analysis/run_c_coverage_fuzz.py --iterations 20000`                             | `results/c_coverage_fuzz.results.json`: `guided.initial_edges=81`, `guided.final_edges=148`, `guided.queue_size=46`, `guided.crashes=0`, phần `random` để so baseline                                                               |
| 6. Crash và tái lập       | “Crash xảy ra khi input chạm bug, không phải vì nhiều dữ liệu. .NET in `seed/iteration/input` khi fail; demo C cố ý SIGSEGV chỉ để thấy harness đếm crash, không tự minimize.”                             | mục 4.6                                                                                            | `/tmp/fuzz-crash`: `crashes>0` nhưng process exit 0; `crash_policy` trong `application_fuzzing.results.json`                                                                                                                              |

Lời chuyển ý gợi ý:

> “Bốn lớp fuzz của em có mức bằng chứng khác nhau. Lớp .NET là structured,
> in-process, có oracle và coverage sau khi chạy. Lớp HTTP đập vào executable +
> PostgreSQL thật nhưng vẫn local/bounded. Lớp browser là CDP interception.
> Lớp coverage-guided mới chỉ chạy trên C fixture tổng hợp; app ASP.NET vẫn chưa
> có code-coverage-guided/path-edge hoặc symbolic fuzzing.”

### 4.8. Trình tự 8 phút và checklist trước khi bảo vệ

Chạy trước khi vào tiết để evidence mới, tránh vừa nói vừa chờ:

```bash
dotnet build OrderPaymentMicroservice/OrderPaymentMicroservice.csproj --no-restore --nologo
python3 security-analysis/run_fuzzing.py
python3 security-analysis/http_fuzz.py --iterations 500 --no-build
python3 security-analysis/browser/network_fuzz.py
python3 security-analysis/run_c_coverage_fuzz.py --iterations 20000
```

Thứ tự nói gợi ý:

1. Mở khung 20 giây: structured .NET → HTTP black-box → browser CDP → coverage-guided C fixture.
2. Chiếu `application_fuzzing.results.json`: `seed`, `tests`, `crash_policy`, `coverage`.
3. Chiếu `http_fuzz.results.json`: 601 request, 108 feature, 500 semantic edge, 0 HTTP 5xx, 0 DB violation.
4. Chiếu `browser_network_fuzz.results.json`: control 8 card, 7/7 fault có `products-state-error`, 0 uncaught JS.
5. Chiếu `c_coverage_fuzz.results.json`: 81 → 148 edge, queue 46, 0 crash.
6. Chốt bằng bảng giới hạn: 25.000/601/148/97,50% đều có phạm vi riêng, không phải proof toàn API/DB/UI.

Câu trả lời nhanh nếu thầy hỏi:

| Câu hỏi                                             | Trả lời ngắn                                                                                                                                                                                            |
| ----------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Sao chạy 25.000 vòng không thấy crash?            | Fuzzing không phải “càng nhiều dữ liệu càng chắc crash”; chưa crash chỉ nói chưa chạm bug trong budget/oracle. Input sai bị `ArgumentException` là reject hợp lệ, không tính crash. |
| 601 request đã phủ toàn API chưa?                | Chưa. Đó là black-box local trên một executable + PostgreSQL tạm, 500 mutation, có oracle response/DB; chưa TLS/proxy/WAN/network partition.                                                      |
| 97,50% branch là coverage toàn app?                 | Không. Chỉ 5 target được include; app ASP.NET chưa có path/edge coverage.                                                                                                                           |
| 148 map entry là coverage C#?                        | Không. Đó là map entry của C fixture qua `trace-pc`; không phải source-level edge của ASP.NET.                                                                                                   |
| Browser đã test mọi network fault?                 | Chưa. Mới fault `api/products`; 8 scenario gồm control + 7 fault, và harness fail nếu UI không hiện error state.                                                                                  |
| Có taint analysis tự động hoặc symbolic fuzzing? | Chưa. Luồng source→sink mới là review thủ công; Z3 kiểm số học riêng, chưa nối vào generator fuzz.                                                                                           |
| Long-email có phải fuzzer tự tìm ra?              | Không. Đó là demo deterministic cho CWE-121: input 128 ký tự làm `user_buggy` báo `stack-buffer-overflow`; `user_fixed` reject trước `strcpy`.                                           |

Không được nói khi bảo vệ:

- “25.000 HTTP request” — đó là iteration ở mức hàm/state machine.
- “601 request chứng minh toàn API an toàn” — chỉ là budget/corpus đã chạy.
- “97,50% branch toàn hệ thống” — chỉ 5 target được đo.
- “148 map entry là coverage ASP.NET” — chỉ là C fixture.
- “Browser là proxy/WAN thật” — chỉ là CDP interception cho products fetch.

---

## 5. Các phần chưa có trong danh sách slide

### 5.1. Recovery charge/refund khi PostgreSQL stop/start

```bash
python3 security-analysis/run_recovery_demo.py
# → charge: PASS; ready 200 -> 503 -> 200; same API process; charge=1, release=0
# → refund: PASS; charge=1, refund=1, release=1
# → results/postgres_reconnect_charge.results.json + ..._refund...
```

### 5.2. Kill API process (charge/refund)

```bash
dotnet test OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~ProcessRestartTests|FullyQualifiedName~RefundRestartTests' \
  --blame-hang-timeout 90s
# → API process mới phục hồi từ journal, không charge/refund/release lần hai
```

### 5.3. API, admission, audit nguyên tử, idempotency

```bash
dotnet test OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~OrderPaymentApiTests|FullyQualifiedName~AdmissionAuditTests|FullyQualifiedName~AtomicAuditIdempotencyTests' \
  --blame-hang-timeout 90s
```

### 5.4. Backup/restore, load, health/status

```bash
dotnet test OrderPaymentMicroservice.IntegrationTests/OrderPaymentMicroservice.IntegrationTests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~OperationalDrillTests|FullyQualifiedName~OrderRetryLoadTests|FullyQualifiedName~OperationsTests' \
  --blame-hang-timeout 90s
```

### 5.5. Full suite (184 test) + TRX

```bash
dotnet test OrderPaymentMicroservice.slnx --no-restore --nologo \
  --logger 'trx;LogFilePrefix=verification' \
  --results-directory security-analysis/results/dotnet \
  --blame-hang-timeout 90s
# → Failed: 0, Passed: 184, Skipped: 0
```

### 5.6. Đóng gói evidence + sinh báo cáo

```bash
python3 security-analysis/package_evidence.py
python3 security-analysis/verify_threat_model.py
python3 security-analysis/build_report.py
python3 security-analysis/package_evidence.py
# → results/evidence_manifest.json, traceability_matrix.csv,
#   report/BAO_CAO.md, report/PHU_LUC_A_G.md
```

### 5.7. Chạy toàn bộ chuỗi bằng một lệnh

```bash
export TLA2TOOLS_JAR="$PWD/tmp/tools/tla2tools.jar"
python3 security-analysis/run_all_checks.py \
  --with-dotnet-tests --with-integration --with-fuzz --with-http-fuzz \
  --with-browser-network-fuzz --with-c-coverage-fuzz --with-tlc --with-sat
# → results/end_to_end.results.json: passed=true, từng bước PASS
```

---

## 6. Bảng đối chiếu slide → lệnh → evidence

Đường dẫn evidence trong bảng tính từ `security-analysis/`.

| Chương | Nội dung slide                             | Lệnh                                                                   | Evidence                                                                  |
| -------- | ------------------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| 1        | Recovery model, state space, 15 property    | `python3 security-analysis/verify_recovery_model.py`                  | `results/order_payment_recovery_model.results.json`                     |
| 1        | 6 mutation                                  | như trên                                                              | 6 dòng `[BUG-*] DETECTED`                                              |
| 1        | Mutex, CTL, sinh mã, số học, 50 testcase | `python3 security-analysis/verify_teacher_spec.py`                    | `results/teacher_spec_verification.json`                                |
| 1        | TLA+ `OrderPayment.tla`                   | `python3 security-analysis/run_tlc.py --jar ...`                      | `results/tlc/OrderPayment.tlc.results.json`                             |
| 1        | Liveness                                    | `python3 security-analysis/audit_liveness.py`                         | `results/liveness_audit.results.json`                                   |
| 1        | Threat model                                | `python3 security-analysis/verify_threat_model.py`                    | `results/threat_model_results.json`                                     |
| 1        | Validation động (149 + 35)                | 2 lệnh `dotnet test` mục 1.6                                        | `results/application_tests.results.json`                                |
| 2        | 5 module, 22 case ASan/UBSan                | `make -C security-analysis/c-memory test`                             | `c-memory/evidence/dynamic-analysis.json`                               |
| 2        | Cppcheck + GCC `-Wall -Wextra`            | `make -C security-analysis/c-memory static-check`                     | `c-memory/evidence/static-analysis.json`                                |
| 2        | CBMC + bound                                | `make -C security-analysis/c-memory bmc-check`                        | `c-memory/evidence/bmc-analysis.json`                                   |
| 2        | Valgrind Memcheck                           | `make -C security-analysis/c-memory valgrind-check`                   | `c-memory/evidence/valgrind-analysis.json`                              |
| 3        | PaymentVerification.z3                      | `python3 security-analysis/verify_payment_smt.py`                     | `results/payment_verification.results.json`                             |
| 3        | InventoryVerification.z3                    | `python3 security-analysis/verify_inventory_smt.py`                   | `results/inventory_verification.results.json`                           |
| 3        | TLA+                                        | `python3 security-analysis/run_tlc.py --jar ...`                      | `results/tlc/`                                                          |
| 3        | SAT/DIMACS/UNSAT core                       | `make -C security-analysis/c-memory sat-check`                        | `c-memory/evidence/sat-pipeline.json`                                   |
| 4        | `SecurityFuzzingTests.cs`, fixed seed     | 4 lệnh filter mục 4.1                                                 | 4 test PASS                                                               |
| 4        | Coverage line/branch                        | `python3 security-analysis/run_fuzzing.py` + mục 4.2                 | `application-fuzzing-coverage.cobertura.xml`, `coverage_summary.json` |
| 4        | Black-box fuzz                              | `python3 security-analysis/http_fuzz.py --iterations 500 --no-build`  | `results/http_fuzz.results.json`                                        |
| 4        | Browser/CDP                                 | `verify_checkout.py`, `browser/network_fuzz.py`                     | `checkout_retry`, `browser_network_fuzz`                              |
| 4        | Coverage-guided C                           | `python3 security-analysis/run_c_coverage_fuzz.py --iterations 20000` | `results/c_coverage_fuzz.results.json`                                  |
| 4        | Giả định crash                           | mục 4.6                                                                | `crash_policy` + demo `/tmp/fuzz-crash`                               |
| 5        | Reconnect PG                                | `python3 security-analysis/run_recovery_demo.py`                      | `postgres_reconnect_*.results.json`                                     |
| 5        | Kill process                                | filter `ProcessRestartTests` + `RefundRestartTests`                 | `process_restart`, `refund_restart`                                   |
| 5        | Audit/admission/idempotency                 | filter 3 class mục 5.3                                                 | `application_tests.results.json`                                        |
| 5        | Backup/load/status                          | filter mục 5.4                                                         | `backup_restore`, `load_probe`                                        |
| 5        | Full suite + report                         | mục 5.5–5.6                                                           | TRX, `evidence_manifest.json`, `BAO_CAO.md`                           |

---

## 7. Lưu ý khi đọc kết quả

- Model hiện có **14 state / 35 transition** (script in ra); slide ghi 15/37 có
  thể là bản nháp. **15** là số property, **18** là số state trong workbook
  (gồm state `conflict` cố ý reachable để pipeline bắt CWE-667/693).
- Workbook có **2 lock atom** (`main_locked`, `log_locked`) cho phần mutex;
  `OrderPayment.tla` kiểm 5 invariant + termination, không có invariant tên
  `Mutex`.
- Gateway thanh toán là **DurableFake local**, PostgreSQL là cluster tạm; kết quả
  chỉ đúng trong workload/seed/bound đã chạy, không phải chứng minh production.
- `make ... bmc-check` là BMC **có bound** (unwind 64, input tượng trưng 48 byte),
  không phải proof vô hạn cho C#.
- Fuzz .NET báo `seed/iteration/input` khi fail; harness C chỉ đếm crash, chưa lưu
  input. Không nối BUGGY với FIXED bằng `&&` (BUGGY exit khác 0).
- Không ghi TRX của test filter vào `security-analysis/results/dotnet`; chỉ full
  suite mới dùng thư mục đó.
