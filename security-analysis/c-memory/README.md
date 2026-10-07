# Mô phỏng lỗi bộ nhớ của OrderPaymentMicroservice bằng C

Bộ thực hành lấy ngữ cảnh User, Product, Cart, Order và Payment của dự án để mô phỏng quản lý bộ nhớ thủ công. Cách tổ chức kế thừa ý tưởng ownership/lifetime từ lab 3; toàn bộ source cần chạy đã được đặt trong thư mục này để không phụ thuộc toolkit hoặc binary bên ngoài.

Đây là lỗi **cố ý đưa vào mô hình C**, không phải bản dịch đầy đủ của service và không chứng minh ứng dụng C# mắc các lỗi tương ứng. C# dùng garbage collector; `malloc/free` trong bộ này dùng để học ownership, lifetime và kiểm chứng động. Không cần PostgreSQL, .NET hoặc dữ liệu thật.

## Ánh xạ module và lỗi

| File | Ngữ cảnh trong source C# | Tình huống lỗi C | Cách sửa |
|---|---|---|---|
| [order.c](modules/order.c) | `OrderService`, `Order.OrderItems` | CWE-401: hủy đơn, giải phóng Order nhưng bỏ quên mảng OrderItem | Giải phóng mảng con trước struct cha |
| [payment.c](modules/payment.c) | `PaymentService`, `Payment.TransactionCode` | CWE-415: nhánh gateway thất bại và cleanup cùng free transaction code | Xóa con trỏ sở hữu sau free; cleanup chấp nhận NULL |
| [cart.c](modules/cart.c) | `CartService.RemoveItemFromCartAsync`, `CartItem` | CWE-416: ghi log ProductId sau khi free item đã xóa | Chụp giá trị trước khi free |
| [product.c](modules/product.c) | `ProductService`, `Product.Name` | CWE-122: sao chép tên dài vào buffer heap 32 byte | Từ chối chuỗi từ 32 byte trở lên, giải phóng cả nhánh từ chối |
| [user.c](modules/user.c) | `UserService`, `User.Email` | CWE-121: sao chép email dài vào buffer stack 32 byte | Kiểm tra độ dài, dành một byte cho NUL |

Mỗi file biên dịch thành hai chương trình: `*_buggy` (mặc định) và `*_fixed` (có `-DFIXED`). Khối `#ifdef FIXED` thể hiện trực tiếp thay đổi sửa lỗi; không phải bật/tắt sanitizer. Buffer chuỗi là allocation/biến riêng để sanitizer có thể phát hiện vượt biên, thay vì chỉ ghi đè field lân cận trong cùng struct.

### Ownership và lifetime

Mỗi vùng cấp phát có đúng một chủ sở hữu chịu trách nhiệm kết thúc lifetime:

| Module | Vùng nhớ | Chủ sở hữu | Lifetime đúng |
|---|---|---|---|
| Order | `Order` và `Order.items` | `cancel_order()`; `Order` sở hữu mảng con | `items` được free trước `Order` |
| Payment | `transaction_code` | `Payment` trong `main()` | chỉ một cleanup path free; sau free đặt con trỏ chủ sở hữu về `NULL` |
| Cart | node `removed` | linked list, sau đó nhánh remove nhận ownership | lấy dữ liệu cần log trước khi free; không dereference sau đó |
| Product | `Product.name` | biến `product` | mọi nhánh, kể cả từ chối input, đều free đúng một lần |
| User | `email[32]` trên stack | activation record của `register_user()` | tự kết thúc khi hàm trả về; không gọi `free` |

`free(parent)` không tự giải phóng child allocation trong C. Ngược lại, gán một con trỏ về `NULL` sau `free` chỉ bảo vệ chính biến con trỏ đó; các alias khác vẫn có thể dangling. Quy tắc này giải thích vì sao bản sửa Order và Payment khác nhau dù đều liên quan cleanup.

## Chạy trên Ubuntu

Cần GCC, Make, Python 3 và Cppcheck 2.13+ (nếu chưa có: `sudo apt install build-essential python3 cppcheck`). Từ thư mục gốc repository:

```bash
cd security-analysis/c-memory
make
make test
make static-check
```

Build dùng AddressSanitizer, UndefinedBehaviorSanitizer, debug symbols và `-O0`. LeakSanitizer bật qua `ASAN_OPTIONS=detect_leaks=1`. `-fno-pie -no-pie` dùng cho môi trường GCC/Linux của bài lab.

Chạy từng lỗi trong các tiến trình riêng (mỗi lỗi có thể dừng chương trình ngay):

```bash
ASAN_OPTIONS=detect_leaks=1 ./build/order_buggy
./build/payment_buggy
./build/cart_buggy
./build/product_buggy
./build/user_buggy
```

So sánh bản sửa:

```bash
ASAN_OPTIONS=detect_leaks=1 ./build/order_fixed
./build/payment_fixed
./build/cart_fixed
./build/product_fixed
./build/user_fixed
```

Product/User mặc định dùng chuỗi dài kích hoạt lỗi. Có thể truyền dữ liệu riêng:

```bash
./build/product_buggy 'Com tam'
./build/product_fixed 'Product-name-that-is-much-longer-than-the-allocated-buffer'
./build/user_fixed 'demo@example.test'
```

Bản sửa in `REJECTED:` với chuỗi quá dài, trả mã 0 vì đây là nhánh validation được xử lý chủ động. Kiểm tra độ dài tính theo **byte**, không theo số ký tự Unicode. Ví dụ User chỉ minh họa an toàn bộ nhớ khi sao chép, không xác thực định dạng email.

## Đọc kết quả kiểm chứng

`make test` chạy 22 trường hợp:

- 5 bản lỗi phải thoát khác 0 **và có đúng chẩn đoán sanitizer tương ứng**; crash bất kỳ không được tính là phát hiện thành công.
- 5 bản sửa với cùng kịch bản phải kết thúc thành công, không có báo cáo sanitizer.
- Product/User được thử độ dài 0, 1, 31, 32, 128 byte để kiểm tra biên chấp nhận/từ chối.
- 2 bản lỗi vẫn chạy được với input ngắn hợp lệ để minh họa lỗi phụ thuộc đường chạy/input.

`PASS order_buggy` nghĩa là công cụ đã bắt được lỗi cố ý, **không** có nghĩa mã lỗi an toàn. Xem stack trace trong `results/<case>.log` và tổng hợp `results/summary.json`. Raw log/binary được tạo cục bộ và không commit; snapshot kết quả ổn định để bàn giao nằm tại `evidence/dynamic-analysis.json`. `make clean` chỉ xóa `build/` và `results/`, không xóa evidence đã lưu.

Đã kiểm tra trong môi trường thực hiện: **22/22 PASS**. Đây là bằng chứng cho các ca đã chạy, không phải chứng minh mọi input đều an toàn. Chưa mô phỏng cạnh tranh luồng, database, giao dịch thật hoặc mọi nhánh lỗi cấp phát. Nếu malloc thất bại, demo thoát mã 2 và test không coi đó là phát hiện đúng lỗi.

Nếu thấy `LeakSanitizer does not work under ptrace` hoặc `LeakSanitizer has encountered a fatal error`, chạy từ terminal bình thường ngoài debugger/sandbox. Không tắt kiểm tra leak để biến kết quả thành PASS.

## Phân tích tĩnh tái lập

`make static-check` chạy ba lớp kiểm tra mà không thực thi năm chương trình demo:

1. GCC `-fsyntax-only -Wall -Wextra -Wpedantic` cho cả cấu hình BUGGY và FIXED.
2. Cppcheck `--enable=all --inconclusive --max-configs=1` riêng cho từng cấu hình. `--max-configs=1` cùng `-UFIXED`/`-DFIXED=1` tránh trộn finding của hai nhánh tiền xử lý.
3. Rule scanner nhỏ trong [tests/check_static.py](tests/check_static.py), theo dõi ownership allocation/free, double-free, dereference sau free và `strcpy` thiếu guard độ dài. Đây là rule giáo dục, theo cú pháp của năm module, **không phải** C parser hay bộ phân tích tĩnh sound cho C tổng quát.

Kết quả đã chạy trên GCC 13.3.0 và Cppcheck 2.13.0:

| Module | Rule trên BUGGY | Rule trên FIXED | Cppcheck 2.13.0 |
|---|---|---|---|
| Order | `owned-allocation-leak` | sạch | bỏ sót lỗi ownership |
| Payment | `double-free` | sạch | bỏ sót double-free |
| Cart | `use-after-free` | sạch | bỏ sót use-after-free |
| Product | `unbounded-string-copy` | sạch | bỏ sót heap overflow phụ thuộc input |
| User | `unbounded-string-copy` | sạch | bắt `bufferAccessOutOfBounds` ở BUGGY; FIXED sạch |

Cppcheck còn báo `unusedStructMember` cho `Payment.order_id` ở cả hai cấu hình; đây là finding mức `style`, không phải bằng chứng lỗi an toàn bộ nhớ. Việc công cụ bỏ sót không chứng minh code an toàn: static rule, Cppcheck và ASan/UBSan bổ sung cho nhau. Scanner kiểm tra đúng rule kỳ vọng trên từng BUGGY, không còn rule đó trên FIXED, rồi trả mã khác 0 nếu regression xuất hiện.

Báo cáo máy đọc được được lưu tại [evidence/static-analysis.json](evidence/static-analysis.json). File ghi version công cụ, lệnh đầy đủ, finding kèm file/dòng, compiler diagnostics, assertions và layout đo từ chính khai báo struct. Chạy lại `make static-check` để tái tạo bằng chứng; không cần tin vào số liệu viết tay trong README.

### Alignment và padding

`tests/layout_probe.c` include trực tiếp năm module (đổi tên `main` lúc biên dịch) để tránh khai báo struct bản sao bị lệch khỏi source. Trên ABI x86-64 của lần chạy đã lưu:

| Kiểu | `sizeof` | `_Alignof` | Giải thích |
|---|---:|---:|---|
| `CartItem` | 16 | 8 | hai `int` 4 byte đứng trước pointer 8 byte, không có lỗ giữa field |
| `OrderItem` | 16 | 8 | hai `int` ghép đủ 8 byte trước `long` |
| `Order` | 24 | 8 | `int id` cần 4 byte padding trước `size_t`; pointer ở offset 16 |
| `Payment` | 16 | 8 | 4 byte padding cần thiết trước pointer ở offset 8 |
| `Product` | 16 | 8 | 4 byte padding cần thiết trước pointer ở offset 8 |

Các struct đều có `sizeof` là bội số của alignment lớn nhất, nên phần tử trong mảng tiếp theo vẫn aligned. Không dùng `packed`: tiết kiệm vài byte có thể tạo truy cập pointer/`long` lệch alignment trên kiến trúc nghiêm ngặt. Đổi thứ tự field của `Order`, `Payment` hoặc `Product` trên ABI này cũng không giảm `sizeof`, nên giữ thứ tự theo nghĩa nghiệp vụ rõ ràng hơn. Các số đo phụ thuộc ABI; JSON là bằng chứng của đúng môi trường đã chạy, không phải hằng số của mọi nền tảng.

## Gợi ý trình bày báo cáo

Với mỗi module: mô tả luồng nghiệp vụ → chỉ ra chủ sở hữu vùng nhớ → chạy bản lỗi → trích stack trace → giải thích bản sửa → chạy lại cùng input và ca biên. Với Order, phân biệt việc free struct cha với việc free vùng nhớ con; với Payment, lưu ý gán NULL chỉ xử lý con trỏ sở hữu đó, không tự sửa các alias khác trong hệ thống lớn hơn.
