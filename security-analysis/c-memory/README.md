# Mô phỏng lỗi bộ nhớ của OrderPaymentMicroservice bằng C

Bộ thực hành lấy ngữ cảnh User, Product, Cart, Order và Payment của dự án để mô phỏng quản lý bộ nhớ thủ công. Cách tổ chức lấy ý tưởng từ `ATPM/lab3_secure_memory` và các ví dụ `ATPM/Soft-tookit/softsec-toolkit/ch2_memsafe`.

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

## Chạy trên Ubuntu

Cần GCC, Make và Python 3 (nếu chưa có: `sudo apt install build-essential python3`). Từ thư mục gốc repository:

```bash
cd security-analysis/c-memory
make
make test
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

`PASS order_buggy` nghĩa là công cụ đã bắt được lỗi cố ý, **không** có nghĩa mã lỗi an toàn. Xem stack trace trong `results/<case>.log` và tổng hợp `results/summary.json`. Kết quả được tạo tại máy khi chạy, không commit binary/log. `make clean` xóa riêng `build/` và `results/` của bộ này.

Đã kiểm tra trong môi trường thực hiện: **22/22 PASS**. Đây là bằng chứng cho các ca đã chạy, không phải chứng minh mọi input đều an toàn. Chưa mô phỏng cạnh tranh luồng, database, giao dịch thật hoặc mọi nhánh lỗi cấp phát. Nếu malloc thất bại, demo thoát mã 2 và test không coi đó là phát hiện đúng lỗi.

Nếu thấy `LeakSanitizer does not work under ptrace` hoặc `LeakSanitizer has encountered a fatal error`, chạy từ terminal bình thường ngoài debugger/sandbox. Không tắt kiểm tra leak để biến kết quả thành PASS.

## Gợi ý trình bày báo cáo

Với mỗi module: mô tả luồng nghiệp vụ → chỉ ra chủ sở hữu vùng nhớ → chạy bản lỗi → trích stack trace → giải thích bản sửa → chạy lại cùng input và ca biên. Với Order, phân biệt việc free struct cha với việc free vùng nhớ con; với Payment, lưu ý gán NULL chỉ xử lý con trỏ sở hữu đó, không tự sửa các alias khác trong hệ thống lớn hơn.
