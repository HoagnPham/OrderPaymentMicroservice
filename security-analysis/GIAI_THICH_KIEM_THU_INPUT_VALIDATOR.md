# Giải thích kiểm thử InputValidator bằng xUnit

## 1. Mục tiêu và phạm vi

Bộ test trả lời câu hỏi: **InputValidator xử lý các loại dữ liệu đầu vào như thế nào, và hành vi đó có đúng với điều kiện mà test yêu cầu không?**

Mã test nằm trong [InputValidatorTests.cs](../OrderPaymentMicroservice.Tests/InputValidatorTests.cs). Đối tượng được kiểm tra là `InputValidator`, chủ yếu thông qua `ValidateCreateOrderRequest()`; hàm này cũng gọi phần kiểm tra item bên trong.

Đây là **unit test**: không khởi động API, không cần PostgreSQL, không tạo giao dịch thanh toán thực tế. Các kết quả dưới đây là kết quả đã chạy và ghi nhận trong báo cáo coverage của project, không phải kết quả kiểm thử toàn hệ thống.

## 2. Test quan sát kết quả bằng cách nào?

Validator được khởi tạo trong lớp test:

```csharp
private readonly InputValidator validator = new();
```

Mỗi test gọi:

```csharp
validator.ValidateCreateOrderRequest(request);
```

Hàm trả về `void`, nên test quan sát hai hành vi:

- Request được chấp nhận: hàm chạy xong, không ném exception.
- Request bị từ chối: hàm ném exception.

Vì vậy, test có thể yêu cầu một exception cụ thể hoặc chỉ gọi hàm và yêu cầu nó kết thúc bình thường. Test PASS nghĩa là hành vi thực tế khớp với assertion của test, **không tự động có nghĩa là dữ liệu an toàn**.

## 3. Vai trò của request nền hợp lệ

```csharp
private static CreateOrderRequest Valid() => new()
{
    CustomerId = 1,
    TotalAmount = 10m,
    Items = [new() { ProductId = 1, Quantity = 1, UnitPrice = 10m }]
};
```

`Valid()` tạo request nền có một item, tổng tiền `1 × 10 = 10`. Hậu tố `m` biểu thị literal kiểu decimal trong C#.

Mỗi test lấy request này rồi thay đổi trường cần kiểm tra:

```csharp
var r = Valid();
r.Items[0].Quantity = 0;
```

Các trường khác vẫn hợp lệ. Nhờ vậy, validator có thể đi tới điều kiện Quantity thay vì bị chặn trước bởi CustomerId hoặc TotalAmount. Đây là cách cô lập nguyên nhân lỗi: tránh đưa nhiều lỗi không liên quan vào cùng một request.

Lưu ý: ID bằng 1 chỉ hợp lệ về miền giá trị của validator; test không chứng minh ID đó tồn tại trong database.

## 4. Cấu trúc Arrange – Act – Assert

Ví dụ test CustomerId:

```csharp
public void InvalidCustomer(int id)
{
    var r = Valid();
    r.CustomerId = id;
    Assert.Equal("CustomerId",
        Assert.Throws<ArgumentException>(
            () => validator.ValidateCreateOrderRequest(r)
        ).ParamName);
}
```

Ba bước thực hiện:

1. **Arrange:** tạo request và đặt CustomerId cần kiểm tra.
2. **Act:** gọi validator thông qua lambda `() => ...`.
3. **Assert:** yêu cầu có `ArgumentException`, đồng thời `ParamName` phải là `CustomerId`.

`Assert.Throws` nhận một hành động để thực thi và kiểm tra exception. Nó trả lại exception được bắt để test có thể kiểm tra thêm thông tin. Trong xUnit, assertion này yêu cầu đúng kiểu exception được chỉ định.

Kiểm tra `ParamName` giúp tránh PASS vì một lỗi ở trường khác: nếu validator từ chối do Items thay vì CustomerId, assertion sẽ không đạt.

## 5. Fact, Theory và InlineData

`[Fact]` dùng cho một tình huống đơn lẻ:

```csharp
[Fact]
public void NullRequest() =>
    Assert.Throws<ArgumentNullException>(
        () => validator.ValidateCreateOrderRequest(null!));
```

`null!` chỉ tắt cảnh báo nullable tại vị trí đó; giá trị lúc chạy vẫn là null. Nó không tạo đối tượng và không thay đổi logic runtime.

`[Theory]` chạy cùng logic với nhiều bộ dữ liệu:

```csharp
[Theory]
[InlineData(0)]
[InlineData(-1)]
[InlineData(int.MinValue)]
public void InvalidCustomer(int id)
```

xUnit chạy ba case riêng: `InvalidCustomer(0)`, `InvalidCustomer(-1)` và `InvalidCustomer(int.MinValue)`. Vì vậy, **34 test là 34 trường hợp chạy, không phải 34 hàm test**.

Các số decimal được truyền dưới dạng chuỗi rồi parse bằng `InvariantCulture`, giúp cách đọc dấu thập phân không phụ thuộc thiết lập ngôn ngữ của máy chạy test.

## 6. Chi tiết 34 trường hợp kiểm thử

| Nhóm test | Số case | Mục đích |
|---|---:|---|
| NullRequest | 1 | Request null |
| InvalidCustomer | 3 | CustomerId không dương |
| InvalidTotal | 5 | TotalAmount ngoài miền hợp lệ |
| MissingItems | 2 | Items null hoặc rỗng |
| NullItem | 1 | Phần tử item null |
| InvalidProduct | 2 | ProductId không dương |
| InvalidQuantity | 4 | Quantity ngoài giới hạn |
| InvalidPrice | 3 | UnitPrice âm hoặc quá lớn |
| ValidBoundariesAndTolerance | 6 | Biên hợp lệ và tolerance |
| KnownDefect_MismatchOrBelowMinimumPriceIsAccepted | 5 | Ghi nhận hành vi lỗi đang tồn tại |
| MultipleItems | 1 | Cộng tổng nhiều item |
| MaximumValidatedMultiplicationDoesNotOverflow | 1 | Phép nhân lớn nhất sau guard |
| **Tổng** | **34** | |

### 6.1. Null và danh sách rỗng

Test phân biệt bốn dạng:

```csharp
request = null;
request.Items = null;
request.Items = [];
request.Items = [null!];
```

- Request null: không có đối tượng đơn hàng.
- Items null: có đơn hàng nhưng không có danh sách item.
- Items rỗng: danh sách tồn tại nhưng không có phần tử.
- Item null: danh sách có phần tử nhưng phần tử không có đối tượng item.

Request/item null được kiểm tra bằng `ArgumentNullException`. Danh sách null/rỗng được kiểm tra bằng `ArgumentException` với ParamName là Items.

### 6.2. ID không hợp lệ

CustomerId được kiểm tra với 0, -1 và int.MinValue. ProductId được kiểm tra với 0 và -1. Các đầu vào này đại diện cho nhóm ID không dương.

Kiến thức áp dụng là **phân lớp tương đương**: chia miền input thành các nhóm có chung hành vi mong muốn, như ID dương và ID không dương. Test chọn dữ liệu đại diện cho từng nhóm, không thử mọi số nguyên có thể có.

### 6.3. Tổng tiền không hợp lệ

Các giá trị TotalAmount: `-1`, `0`, `0.009`, `1000000`, `decimal.MaxValue`.

Chúng đi qua các điều kiện khác nhau:

```text
TotalAmount <= 0
TotalAmount < 0.01
TotalAmount > 999999.99
```

`0.009` lớn hơn 0 nên vượt qua guard đầu, nhưng nhỏ hơn mức tối thiểu 0.01. Đây là case cần thiết để đi tới guard thứ hai.

Các giá trị hợp lệ 0.01 và 999999.99 được kiểm tra ở nhóm biên hợp lệ, giúp phát hiện lỗi dùng `<` thay vì `<=` hoặc ngược lại. Kiến thức áp dụng là **phân tích giá trị biên**.

### 6.4. Số lượng và đơn giá

Quantity hợp lệ trong khoảng `1..10000`. Test dùng 0, -1, 10001 và int.MaxValue để kiểm tra giới hạn dưới/trên; Quantity = 10000 được kiểm tra ở nhóm hợp lệ.

UnitPrice không hợp lệ được kiểm tra bằng -0.01, 1000000 và decimal.MaxValue. Lưu ý: case decimal.MaxValue bị từ chối tại guard giá tối đa, không đi tới phép nhân. Do đó, test này **không chứng minh catch overflow đã được chạy**.

Validator khai báo MIN_PRICE = 0.01 nhưng chưa áp dụng nó: giá 0 hoặc 0.001 vẫn có thể vượt qua kiểm tra giá hiện tại. Các case này được ghi nhận trong nhóm KnownDefect.

### 6.5. Tổng nhiều item

`MultipleItems` thêm item thứ hai có Quantity = 2 và UnitPrice = 5 vào request nền. Tổng đúng là:

```text
1 × 10 + 2 × 5 = 20
```

Test đặt TotalAmount = 20 và gọi validator. Case này thực hiện nhiều lượt của vòng lặp cộng tổng. Tuy nhiên, do validator chưa từ chối mismatch, việc không ném exception riêng trong case này chưa đủ chứng minh phép cộng tổng luôn đúng; coverage cũng không thay thế assertion kiểm tra giá trị tính toán.

## 7. Giải thích tolerance và lỗi tổng tiền

Validator tính tổng item rồi kiểm tra:

```csharp
Math.Abs(request.TotalAmount - calculatedTotal) > 0.01m
```

Với tổng item = 10:

| TotalAmount | Sai lệch tuyệt đối | Điều kiện > 0.01 |
|---:|---:|---|
| 10 | 0 | False |
| 10.01 | 0.01 | False |
| 9.99 | 0.01 | False |
| 10.011 | 0.011 | True |
| 9.989 | 0.011 | True |

Test kiểm tra cả hai phía tăng/giảm vì Math.Abs lấy độ lớn sai lệch. Đúng ngưỡng 0.01 được chấp nhận theo điều kiện hiện tại; vượt ngưỡng đi vào block tolerance nhưng block đang rỗng.

Vì vậy, ngay cả trường hợp TotalAmount = 0.01 và tổng item = 999999.99 cũng không gây exception sau khi vượt qua các guard riêng lẻ. Đây là **lỗi tính nhất quán nghiệp vụ**, không phải lỗi kiểu dữ liệu.

## 8. Tại sao test ghi nhận lỗi vẫn PASS?

Test `KnownDefect_MismatchOrBelowMinimumPriceIsAccepted` chỉ gọi validator và yêu cầu không phát sinh exception:

```csharp
validator.ValidateCreateOrderRequest(r);
```

Nó xác nhận hành vi đang tồn tại: input sai nghiệp vụ vẫn được chấp nhận. Đây là **characterization test**, giúp ghi lại hiện trạng trước khi sửa.

Kết luận đúng là: “Test xác nhận validator vẫn chấp nhận request sai.” Không phải: “Test xác nhận request này an toàn.”

Sau khi sửa validator để reject mismatch, test hồi quy cần yêu cầu exception, ví dụ:

```csharp
Assert.Throws<ArgumentException>(
    () => validator.ValidateCreateOrderRequest(r));
```

Đoạn trên là hướng viết test **sau khi sửa nghiệp vụ**, không phải hành vi mã hiện tại. Chính sách giá 0/giá tối thiểu cũng cần được xác định rõ trước khi đổi assertion tương ứng.

## 9. Coverage: đo như thế nào và đọc ra sao?

Project sử dụng `coverlet.msbuild`. Chạy từ thư mục gốc repository:

```powershell
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj /p:CollectCoverage=true /p:CoverletOutput=../security-analysis/results/input-validator /p:CoverletOutputFormat=cobertura '/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator' /p:Threshold=90 /p:ThresholdType=branch
python security-analysis/run_analysis.py
```

- CollectCoverage: bật đo coverage.
- Include: chỉ đo lớp InputValidator trong assembly ứng dụng.
- CoverletOutputFormat: xuất Cobertura XML.
- Threshold/ThresholdType: yêu cầu branch coverage tối thiểu 90%.
- Script Python đọc XML để sinh [coverage_summary.json](results/coverage_summary.json); script không tự chạy bộ test .NET.

Kết quả đã ghi nhận: **34 PASS, 0 FAIL, 0 SKIP**; toàn lớp **31/32 nhánh = 96.875%**, bảng CLI hiển thị khoảng **96.87%**. Riêng ValidateCreateOrderRequest đạt **100% branch coverage**. Line coverage toàn lớp là **83.67%**.

**Line coverage** cho biết dòng nào đã chạy; **branch coverage** cho biết các hướng của điều kiện đã được thực hiện. Ví dụ `CustomerId <= 0` cần cả input làm điều kiện True và input làm điều kiện False.

Điều kiện `Items == null || Items.Count == 0` có short-circuit: nếu Items null thì không đọc Count. Các case null, rỗng và có item giúp đi qua các đường đánh giá khác nhau.

Coverage cao không bảo đảm assertion đủ mạnh hoặc nghiệp vụ đúng. Nhánh tolerance dòng 61 đã phủ 2/2 hướng nhưng vẫn có lỗi vì không thực hiện hành động từ chối.

## 10. Nhánh chưa được phủ và đường đi bất khả thi

Nhánh còn thiếu là hướng True tại dòng 99 của InputValidator:

```csharp
if (subtotal < 0)
```

Trước đó, các guard đã đảm bảo Quantity ≥ 1 và UnitPrice ≥ 0. Vì vậy `Quantity × UnitPrice` không thể âm. Đưa Quantity hoặc UnitPrice âm vào request cũng không đi tới nhánh này vì validator từ chối sớm hơn.

Phép nhân lớn nhất sau guard:

```text
10000 × 999999.99 = 9,999,999,900
```

Giá trị này nằm trong miền decimal nên catch overflow của phép nhân item chưa được chạy. Test `MaximumValidatedMultiplicationDoesNotOverflow` thực hiện đúng phép nhân ở biên trên nhưng request vẫn có total 10: nó đồng thời đi vào nhánh mismatch vốn chưa reject.

Không cần dùng reflection hoặc sửa validator để ép các đường đi bất khả thi chỉ nhằm đạt 100% coverage. Cần ghi rõ nguyên nhân và phạm vi kết luận: phân tích này áp dụng cho phép nhân một item sau guard, không chứng minh mọi phép cộng tiền của toàn hệ thống không thể overflow.

## 11. Lỗi khi chạy và tổng hợp báo cáo

Trong lần thiết lập test, restore NuGet bị chặn bởi môi trường hạn chế, báo NU1301 và lỗi socket tới api.nuget.org:443. Sau khi chạy lại với quyền truy cập được phê duyệt, các gói xUnit/Coverlet được tải và test chạy thành công.

Restore còn báo NU1603: project yêu cầu Npgsql 8.0.7 nhưng resolve thành 8.0.8. Đây là warning dependency, không phải một test thất bại; dependency ứng dụng chưa được thay đổi trong phần bổ sung lab.

Phần parser coverage ban đầu so sánh `branch` với `true`, trong khi XML chứa `True`, dẫn tới danh sách nhánh thiếu bị rỗng. Script đã sửa bằng `.lower()` và xác định đúng dòng 99 có condition coverage 50% (1/2). Việc đối chiếu với XML gốc giúp tránh kết luận sai từ file tổng hợp.

## 12. Bộ test chưa chứng minh điều gì?

Bộ test chưa xác nhận ID tồn tại trong database, quyền sở hữu đơn/giỏ hàng, giá khớp catalog phía server, HTTP status của Controller, kết quả payment gateway, thanh toán lặp/vượt tổng tiền hoặc an toàn khi nhiều request chạy đồng thời.

Các mục tiêu đó cần test ở Service, integration test và kiểm thử bảo mật tương ứng. Coverage hiện chỉ thuộc InputValidator, không đại diện cho toàn project.

## 13. Cách trình bày trong báo cáo hoặc thuyết trình

> Tôi xây dựng 34 trường hợp unit test bằng xUnit, áp dụng phân lớp tương đương và phân tích giá trị biên để kiểm tra InputValidator. Các test xác nhận hành vi từ chối dữ liệu không hợp lệ, đồng thời ghi nhận lỗi chấp nhận tổng tiền sai và giá dưới mức tối thiểu khai báo. Coverlet đo được branch coverage toàn lớp khoảng 96.87%, vượt mục tiêu 90%; riêng ValidateCreateOrderRequest đạt 100%. Tuy nhiên, test PASS và coverage cao không đồng nghĩa validator đã an toàn. Các lỗi nghiệp vụ được phát hiện vẫn cần sửa và bổ sung test hồi quy yêu cầu từ chối hành vi sai.
