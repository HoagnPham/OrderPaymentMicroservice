# Giải thích kiểm thử InputValidator bằng xUnit

Tài liệu này giải thích oracle và cấu trúc test, không thay thế báo cáo toàn suite.
Source: [InputValidatorTests.cs](../OrderPaymentMicroservice.Tests/InputValidatorTests.cs),
[InputValidator.cs](../OrderPaymentMicroservice/Security/InputValidator.cs).
Số liệu coverage đọc từ [coverage_summary.json](results/coverage_summary.json).

## 1. Unit test quan sát điều gì?

ValidateCreateOrderRequest trả void. Request hợp lệ phải kết thúc bình thường;
request sai phải ném exception đúng kỳ vọng. Assert.Throws nhận một lambda để
chạy validator, bắt exception và đối chiếu kiểu. Test PASS nghĩa là oracle khớp,
không có nghĩa mọi input hoặc toàn hệ thống an toàn.

```csharp
var r = Valid();
r.CustomerId = 0;
var error = Assert.Throws<ArgumentException>(
    () => validator.ValidateCreateOrderRequest(r));
Assert.Equal("CustomerId", error.ParamName);
```

Arrange tạo request nền hợp lệ rồi sửa một trường; Act gọi validator; Assert
kiểm exception và ParamName. Kiểm ParamName giúp tránh PASS do trường khác lỗi.
ID=1 trong request nền chỉ hợp lệ về miền giá trị; unit test không có database
để xác minh user/product tồn tại.

## 2. Fact, Theory và số case

Fact là một case. Theory với nhiều InlineData tạo nhiều case chạy cho cùng một
hàm test. Decimal truyền dưới dạng chuỗi và parse bằng InvariantCulture để tránh
phụ thuộc dấu thập phân của locale. null! chỉ tắt cảnh báo compile, vẫn là null
lúc chạy.

InputValidatorTests hiện có **34 case** phân bố như sau; các test admission,
precision và fuzz ở lớp khác cũng thực thi validator nhưng không cộng vào bảng này.

| Nhóm                                        | Case | Oracle                                                                |
| -------------------------------------------- | ---- | --------------------------------------------------------------------- |
| NullRequest                                  | 1    | ArgumentNullException                                                 |
| InvalidCustomer                              | 3    | ID 0/âm bị từ chối, ParamName=CustomerId                          |
| InvalidTotal                                 | 5    | Ngoài miền amount bị từ chối                                     |
| MissingItems                                 | 2    | Null/rỗng bị từ chối                                              |
| NullItem                                     | 1    | Item null bị từ chối                                               |
| InvalidProduct                               | 2    | ID không dương bị từ chối                                       |
| InvalidQuantity                              | 4    | Quantity ngoài 1..10000 bị từ chối                                |
| InvalidPrice                                 | 3    | Giá âm/quá max bị từ chối                                       |
| ValidBoundariesAndTolerance                  | 6    | Biên hợp lệ; chênh total một cent ở boundary này được nhận |
| MismatchOrBelowMinimumPriceIsRejected        | 5    | Request sai bị từ chối                                             |
| MultipleItems                                | 1    | Tổng nhiều item hợp lệ được nhận                              |
| ItemSubtotalAboveMaximumOrderTotalIsRejected | 1    | Subtotal quá lớn so với total bị từ chối, ParamName=TotalAmount |

## 3. Hành vi hiện tại và thứ tự guard

Validator kiểm request/customer, total trong 0,01..999999,99 và integer cents,
items không rỗng và không vượt item cap (mặc định 100), từng product/quantity/
unit price, rồi so sánh total với tổng items. Quantity nằm trong 1..10000,
unit price trong 0,01..999999,99 và cũng phải đúng cents.

Chênh lệch lớn hơn 0,01 giữa total và tổng items ném ArgumentException. Giá 0
hoặc 0,001 bị từ chối. Đây là **bản sau khi đã sửa**; mô tả trước kia về mismatch
vẫn được chấp nhận hoặc min-price không áp dụng không còn là hiện trạng.

Hai InlineData total=10.011 và 9.989 bị chặn bởi RequireCents trước khi tới phép
so sánh total. Vì vậy chỉ Assert.Throws của các case này không tự chứng minh
nhánh mismatch đã chạy. Case total=0.01/unitPrice=999999.99 có cents hợp lệ và
đi tới mismatch; khi giải thích coverage cần xét đúng thứ tự guard.

Tolerance một cent ở validator không cho phép client quyết định giá catalog.
OrderService còn lấy Product.Price phía server và kiểm hợp đồng riêng; Payment
phải khớp Order.TotalAmount. Validation giá trị, authorization và persistence
là các lớp khác nhau.

## 4. Biên tiền và đường bất khả thi

MoneyPrecisionTests kiểm fractional cents ở nhiều request boundaries, nhận
trailing zero hợp lệ như 25.0100. Các test này bổ sung cho 34 case ở trên.

Sau guard quantity/price dương, subtotal âm là đường bất khả thi trong phép
nhân một item. Tích lớn nhất 10000 × 999999,99 = 9.999.999.900 vẫn nằm trong
miền decimal. Request có tổng không khớp tích đó bị từ chối; điều này không
đồng nghĩa phép nhân decimal overflow.

Không ép đường bất khả thi bằng reflection hoặc sửa validator chỉ để tăng
coverage. Cũng không dùng phân tích này để chứng minh mọi phép cộng tiền,
transaction hoặc payment gateway đều an toàn.

## 5. Chạy riêng và đo coverage

Chạy từ gốc repository:

```bash
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj --no-restore --nologo --filter FullyQualifiedName~InputValidatorTests
```

Không ghi TRX subset vào results/dotnet đang dành cho summary toàn suite.
Muốn đo đúng target validator và helper cents, chạy toàn unit project:

```bash
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj --no-restore --nologo /p:CollectCoverage=true /p:CoverletOutput=../security-analysis/results/input-validator /p:CoverletOutputFormat=cobertura '/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator%2c[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.MoneyAmounts' /p:Threshold=90 /p:ThresholdType=branch
python3 security-analysis/collect_validator_coverage.py
```

Chỉ gọi collector sau khi dotnet thành công. Không dùng run_analysis.py cho
quy trình này vì nó có thể import toolkit ATPM. Không chạy hai lệnh dotnet hoặc
fuzz/coverage đồng thời dùng chung assemblies.

Evidence hiện tại: InputValidator **97,22% branch**, **85,71% line**;
ValidateCreateOrderRequest **100% branch**; ValidateOrderItem **92,85% branch**;
MoneyAmounts **100% branch**. Coverage này thu từ toàn unit suite, không phải
chỉ 34 case InputValidatorTests. Số liệu 96,87%/31-of-32 của phiên bản cũ không
được dùng như số hiện tại. Đọc Cobertura gốc để kiểm từng nhánh, không chỉ dựa
vào một danh sách uncovered_branches trong summary.

Line coverage đếm dòng thực thi; branch coverage đếm hướng điều kiện/short-circuit.
Include giới hạn target được đo; ThresholdType=branch yêu cầu branch coverage.
Không gọi các tỷ lệ trên là coverage toàn ứng dụng.

## 6. Phạm vi còn ngoài unit validator

Các test này không chứng minh ID tồn tại, ownership, JWT/role, catalog price,
HTTP status, transaction rollback, concurrency, charge/refund idempotency hoặc
worker recovery. Những phần đó có evidence service/HTTP/PostgreSQL/process riêng.

Khi nói: “Nhóm kiểm 34 case riêng cho InputValidator bằng phân lớp và giá trị
biên, dùng exception/ParamName làm oracle. Bản hiện tại reject mismatch và giá
ngoài miền, thêm guard cents và item cap. Coverage đo trên target riêng; kết
quả được đọc cùng assertion và các lớp test khác.”

Xem [kịch bản thuyết trình](KICH_BAN_THUYET_TRINH_VA_DEMO.md) và
[báo cáo](report/BAO_CAO.md) để đặt unit test trong chuỗi evidence toàn project.
