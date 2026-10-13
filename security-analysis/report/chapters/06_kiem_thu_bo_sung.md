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

Seed xác định giúp tái lập failure; corpus bao gồm trường hợp biên và payload
lỗi. Budget 25.000 iteration cho từng random campaign là số lần thử, không phải
số input khác nhau hay phần trăm miền input. State transition fuzz có oracle từ
bảng chuyển và invariant; chưa chứng minh vượt qua mọi chuỗi gateway/database.
Không gọi campaign này là coverage-guided fuzzing nếu thuật toán không dùng
coverage để chọn mutation tiếp theo.

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
chuyển sang controlled rejection bằng catch quá rộng trong harness.

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
