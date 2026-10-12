## 4. Phân tích mã nguồn, ownership và an toàn bộ nhớ

### 4.1. Vai trò của mô hình C trong một project C#

ASP.NET implementation dùng C# và garbage collector; mô hình C trong c-memory
đưa vào malloc/free, buffer cố định và linked list để quan sát ownership/lifetime
bằng công cụ học phần. Liên hệ là ngữ cảnh nghiệp vụ và trách nhiệm quản lý tài
nguyên, không phải sự tương đương mã hoặc bằng chứng C# có CWE-415/CWE-416.

GC quản lý memory của object không thay trách nhiệm dispose DbContext/transaction,
stream hoặc cancellation của tác vụ. Trong source C#, `await using` đóng transaction
và scope; các lỗi logic như hoàn kho sai, stale entity hay gateway UNKNOWN phải
được kiểm ở service/test, không được kỳ vọng ASan của executable C phát hiện.
Chương này phân biệt kết quả C trực tiếp với nhận xét source C# cần phép kiểm khác.

### 4.2. Phương pháp static và dynamic

Static pipeline biên dịch syntax với GCC, chạy cppcheck riêng từng cấu hình và
scanner theo pattern/data-flow đơn giản. BUGGY dùng `-UFIXED`, FIXED dùng
`-DFIXED=1`, max-configs=1 để tránh tool trộn hai nhánh tiền xử lý. Compiler exit 0
chỉ nói nguồn qua syntax check, không nói không có undefined behavior.

Dynamic pipeline build bằng `-g -O0 -fno-omit-frame-pointer`, AddressSanitizer và
UndefinedBehaviorSanitizer; LeakSanitizer bật detect_leaks. Mỗi executable chạy
trong process riêng để crash ở một case không làm mất case còn lại. Oracle cần
exit khác 0 **và** diagnostic mong đợi với BUGGY, hoặc exit 0 và không có diagnostic
với FIXED. Chuỗi REJECTED là validation được xử lý, không được tính là crash.

@@MEMORY_RUN_TABLE@@

Lần sandbox đầu không đạt oracle ở nhiều case; lần chạy ngoài sandbox đạt 22/22.
Không tắt detect_leaks để biến outcome thành PASS. Điều kiện thực thi và hash
source/binary được giữ trong chapter4_run.results.json. Outcome hợp lệ là của lần
chạy ngoài sandbox; hạn chế môi trường được giữ riêng, không chuyển thành finding
memory của source mà chưa có đúng diagnostic.

### 4.3. Phân tích từng lớp lỗi và cách sửa

| Module | Ownership/lifetime đúng | Nguyên nhân BUGGY | Bản sửa và lý do |
| --- | --- | --- | --- |
| Order | Order sở hữu mảng OrderItem cấp phát riêng | free(parent) bỏ quên child allocation | Free items trước Order; một parent pointer không tự giải phóng allocation khác |
| Payment | Một Payment chịu trách nhiệm transaction_code | Cleanup nhánh lỗi và cleanup chung cùng free | Sau free đặt pointer sở hữu NULL; free(NULL) không release lần hai |
| Cart | List sở hữu node; sau unlink nhánh remove kết thúc lifetime | Log dereference removed sau free | Chụp ProductId trước free, sau đó log giá trị đã sao chép |
| Product | Product sở hữu name trên heap | strcpy không kiểm độ dài với buffer 32 byte | Reject strlen≥32 và free cả nhánh reject; chừa byte NUL |
| User | Activation record chứa email[32] | Copy email dài vào stack buffer | Guard strlen<input capacity trước copy; không free stack memory |

Product/User có cùng family unbounded-copy CWE-120 nhưng vị trí vùng nhớ khác:
Product là heap buffer overflow CWE-122, User là stack buffer overflow CWE-121.
Cppcheck có thể gắn CWE-788 cho finding bufferAccessOutOfBounds; đó là taxonomy
của tool, không nên sửa tên tool finding để ép giống taxonomy phân tích của tác giả.

Bản sửa Payment chỉ bảo vệ biến pointer đã đặt NULL, không chữa dangling alias
nếu tồn tại con trỏ khác tới cùng allocation. Bản sửa Cart tránh dereference sau
free; gán NULL bổ trợ nhưng không hồi sinh lifetime. Buffer guard theo byte,
không theo số ký tự Unicode hoặc độ hợp lệ email. Những điểm này xác định đúng
điều bản sửa thực sự bảo đảm.

### 4.4. Diễn giải diagnostic và test biên

Order lặp cancel năm lần, mỗi lần cấp ba OrderItem. Trên ABI đã đo, mảng mỗi lần
48 byte; leak được mong đợi là allocation con không được giải phóng. Số byte
runtime của diagnostic phải lấy từ log, không áp công thức này cho mọi ABI.
Payment báo attempting double-free; Cart báo heap-use-after-free; Product/User
với input dài báo đúng loại heap/stack-buffer-overflow.

Năm BUGGY phải có diagnostic, năm FIXED cùng kịch bản phải sạch. Product và User
thử length 0,1,31,32,128; 31 chấp nhận, 32 từ chối vì cần một byte cho NUL. Hai
BUGGY với input ngắn vẫn chạy được, minh họa việc không crash ở một input không
chứng minh chương trình không có lỗi. Tổng là 5+5+10+2=22 case.

Đọc log cần xác định operation bị lỗi, vị trí source và allocation/free liên quan.
Một nonzero exit do hết memory hoặc lỗi tool không phải bằng chứng bắt đúng CWE.
Helper allocate thoát 2 khi malloc thất bại; chưa có campaign fault injection
cho mọi nhánh allocation failure, alias và concurrency.

### 4.5. Đánh giá khả năng static analyzer

@@STATIC_DETECTION_TABLE@@

Đối với tập BUGGY nhỏ có ground truth, scanner riêng bắt 5/5 loại lỗi kỳ vọng,
cppcheck chỉ bắt trực tiếp buffer của User. Đây là quan sát trên đúng năm source,
không là recall chung của hai tool. Scanner được viết cho syntax của demo nên có
nguy cơ khớp pattern mà không hiểu đầy đủ control flow hoặc alias.

Ví dụ rule strcpy hiện tìm guard strlen ở các dòng trước, không chứng minh guard
thống trị mọi đường đến copy hoặc capacity được so sánh đúng. Rule leak dựa vào
sự xuất hiện free(child), không phải chứng minh mọi nhánh đều free. Compiler không
báo warning trong các case vẫn không chứng minh memory safety. Finding style
unusedStructMember ở Payment xuất hiện cả BUGGY và FIXED, không tính là lỗi memory.

### 4.6. Alignment, padding và ảnh hưởng ABI

@@LAYOUT_TABLE@@

Layout probe include source struct thật thay vì khai báo bản sao. Padding trước
pointer/size_t giữ alignment đúng; sizeof là bội alignment giúp phần tử kế tiếp
trong mảng vẫn aligned. Không dùng packed chỉ để giảm byte khi chưa đánh giá truy
cập lệch alignment. Số đo thuộc GCC/Linux/ABI của evidence, không là hằng của mọi
compiler/kiến trúc. Padding không giải quyết leak, UAF hay copy vượt allocation.

### 4.7. Đối chiếu với application và tiêu chí chấp nhận

| Quan sát trong C | Điểm cần kiểm tra trong C# | Evidence phù hợp |
| --- | --- | --- |
| Ownership cleanup | Transaction/scope được dispose khi exception | Source `await using`; cần fault injection để kiểm mọi cửa sổ rollback |
| Double cleanup | Duplicate request không tạo double charge/refund/release | Idempotency, Version, regression và PostgreSQL |
| Sử dụng dữ liệu sau remove | Ownership/cartItemId và dữ liệu cache sau mutation | Service guard và HTTP test; không gọi đây là native UAF |
| Chuỗi quá dài | DTO StringLength, body/item limits, database lengths | Validation unit/HTTP; body/item limits còn mở |
| Tràn phép tính/miền dữ liệu | Quantity/price/amount và stock không âm | SMT và regression Chương 5–6 |

Trong phạm vi demo C, tiêu chí đạt là đúng diagnostic trên BUGGY và sạch sanitizer
trên FIXED với input và biên công bố. Không yêu cầu zero finding style. Với C#,
chỉ đưa kết luận đã có test tương ứng; không suy từ sanitizer C sang an toàn API.

### 4.8. Tái lập và giới hạn kết luận

Các lệnh `make -C security-analysis/c-memory static-check` và `make -C
security-analysis/c-memory test` chỉ dùng source/tool của project, không truy cập
ATPM. Report giữ tool version, compiler command, case oracle, environment và hash;
raw sanitizer log/binary là artifact cục bộ. Clang-Tidy và Clang Static Analyzer
chưa chạy, không được ghi như kết quả có sẵn.

Static và dynamic bổ sung cho nhau: finding static giúp định vị trước execution;
sanitizer xác nhận đường đã chạy; code review giải thích ownership; regression
kiểm bản sửa không làm hỏng input hợp lệ. Chưa có proof memory safety tổng quát,
coverage mọi nhánh, đa luồng hoặc kiểm mọi allocation failure.

Nguồn: [module C](../c-memory/README.md),
[static evidence](../c-memory/evidence/static-analysis.json),
[dynamic evidence](../c-memory/evidence/dynamic-analysis.json),
[Phụ lục C](PHU_LUC_A_G.md#phụ-lục-c--static-và-bộ-nhớ).
