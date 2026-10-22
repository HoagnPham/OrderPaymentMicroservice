# Giải thích kiểm thử InputValidator

Tài liệu giải thích `Security/InputValidator.cs` được kiểm thử thế nào, oracle là gì,
các biên nằm ở đâu và đọc coverage ra sao.

## 1. Vai trò

`InputValidator` chặn dữ liệu đơn hàng không hợp lệ **trước khi** service chạm DB.
Nó là lớp phòng thủ thứ nhất, không thay thế:

- kiểm tra quyền sở hữu ở controller,
- kiểm tra tồn kho và giá ở `OrderService`,
- ràng buộc `CHECK`/unique index ở PostgreSQL,
- kiểm tra số học ở SMT.

Nếu validator bị bỏ qua, DB constraint vẫn là lớp cuối; nhưng response và audit sẽ
khác, nên test kiểm cả hai lớp.

## 2. Quy tắc và biên

| Trường | Quy tắc | Giá trị |
| --- | --- | --- |
| `CustomerId` | `> 0` | `0`, `-1`, `int.MinValue` bị từ chối |
| `TotalAmount` | `> 0` | `0`, `-1` bị từ chối |
| `TotalAmount` | `>= 0.01` | `0.009` bị từ chối |
| `TotalAmount` | `<= 999999.99` | `1000000` bị từ chối |
| `TotalAmount` | đúng cent | `10.011` bị từ chối |
| `Items` | không null, không rỗng | `null`, `[]` bị từ chối |
| `Items.Count` | `<= MaxOrderItems` | mặc định 100, cấu hình 1–1000 |
| `item` | không null | `[null]` bị từ chối |
| `ProductId` | `> 0` | `0`, `-1` bị từ chối |
| `Quantity` | `>= 1` | `0`, `-1` bị từ chối |
| `Quantity` | `<= 10000` | `10001`, `int.MaxValue` bị từ chối |
| `UnitPrice` | `>= 0.01` | `0`, `-0.01`, `0.001` bị từ chối |
| `UnitPrice` | `<= 999999.99` | `1000000` bị từ chối |
| `UnitPrice` | đúng cent | `0.001` bị từ chối |
| Tổng | `|TotalAmount - Σ(Quantity × UnitPrice)| <= 0.01` | lệch quá 1 cent bị từ chối |
| Tràn số | `checked(Quantity × UnitPrice)` | tràn/âm → `ArgumentException` |

`MaxOrderItems` đọc từ `ApiAdmissionOptions`, nên test admission dùng
`MaxOrderItems = 3` để kiểm biên mà không tạo 100 dòng.

## 3. Oracle

Oracle không phải "không ném exception là đúng". Với mỗi ca, test kiểm **loại
exception** và **tên tham số**:

```csharp
Assert.Equal("Quantity",
    Assert.Throws<ArgumentException>(() => validator.ValidateCreateOrderRequest(r)).ParamName);
```

Vì sao chặt như vậy:

1. Nếu validator ném `NullReferenceException` thay vì `ArgumentException`, đó là bug,
   dù input vẫn bị chặn.
2. Nếu từ chối **sai trường** (ví dụ báo `TotalAmount` khi lỗi ở `Quantity`), client
   sửa sai chỗ.
3. Nếu input hợp lệ bị từ chối, đó là false positive — cũng là lỗi.

Với ca hợp lệ, test chỉ gọi hàm; không ném exception là đạt. Với ca biên, test dùng
`[Theory]` + `[InlineData]` để mỗi giá trị là một case riêng trong TRX.

## 4. Bộ case tiêu biểu

`OrderPaymentMicroservice.Tests/InputValidatorTests.cs`

| Test | Nội dung | Kỳ vọng |
| --- | --- | --- |
| `NullRequest` | request null | `ArgumentNullException` |
| `InvalidCustomer` | `0`, `-1`, `int.MinValue` | `ArgumentException("CustomerId")` |
| `InvalidTotal` | `-1`, `0`, `0.009`, `1000000`, `decimal.MaxValue` | `ArgumentException("TotalAmount")` |
| `MissingItems` | `null`, `[]` | `ArgumentException("Items")` |
| `NullItem` | `[null]` | `ArgumentNullException("item")` |
| `InvalidProduct` | `0`, `-1` | `ArgumentException("ProductId")` |
| `InvalidQuantity` | `0`, `-1`, `10001`, `int.MaxValue` | `ArgumentException("Quantity")` |
| `InvalidPrice` | `-0.01`, `1000000`, `decimal.MaxValue` | `ArgumentException("UnitPrice")` |
| `ValidBoundariesAndTolerance` | `0.01`, `999999.99`, `10000 × 0.01`, lệch 1 cent | không ném |
| `MismatchOrBelowMinimumPriceIsRejected` | lệch 1,1 cent, giá 0, giá 0,001 | `ArgumentException` |
| `MultipleItems` | hai dòng hợp lệ | không ném |
| `ItemSubtotalAboveMaximumOrderTotalIsRejected` | `10000 × 999999.99` | `ArgumentException("TotalAmount")` |

Ngoài ra `MoneyPrecisionTests` kiểm phần cent: `25.001`, `25.005` bị từ chối;
`25.01`, `25.0100` được nhận.

## 5. Vì sao tolerance là 1 cent

`TotalAmount` và `UnitPrice` được lưu với hai chữ số thập phân. Khi client tính tổng
bằng số thực, sai số làm tròn tối đa là một cent. Vì vậy:

```csharp
const decimal tolerance = 0.01m;
if (Math.Abs(request.TotalAmount - calculatedTotal) > tolerance)
    throw new ArgumentException("TotalAmount must match the sum of order items", ...);
```

Hệ quả cần nói rõ:

- Tolerance **không** cho phép lệch tiền thật; nó hấp thụ sai số biểu diễn.
- Giá trị lệch đúng 1 cent được nhận; lệch 1,1 cent bị từ chối.
- Sau validator, `OrderService` **vẫn** lấy giá từ catalog server, nên client không
  thể dùng tolerance để hạ giá.

## 6. Fuzz

`SecurityFuzzingTests.cs` chạy 4 campaign với seed cố định:

| Campaign | Nội dung |
| --- | --- |
| `SeedCorpusMatchesExpectedValidatorOutcome` | corpus ca biên, so kết quả mong đợi |
| `SeededOrderAndPaymentValidatorCampaignHasNoUnexpectedCrash` | 25.000 vòng ngẫu nhiên cho order/payment |
| `SeededStateMachineCampaignMatchesIndependentTransitionTables` | 25.000 cặp trạng thái, so bảng chuyển độc lập |
| `SeededStatefulOrderPaymentSequencesRespectAggregateInvariants` | 2.000 chuỗi × tối đa 20 bước |

Oracle của fuzz: input xấu phải bị `ArgumentException` (không phải crash khác), input
tốt phải qua, và invariant aggregate phải giữ. Seed được in khi fail để tái lập.

Giới hạn: đây là generation-based fuzzing ở mức hàm/state machine, không phải
coverage-guided. HTTP fuzz là harness riêng `http_fuzz.py` (black-box, semantic
feedback), không phải campaign .NET này. Coverage-guided trace-pc là harness
C fixture riêng `run_c_coverage_fuzz.py`, cũng không phải campaign .NET này.
"25.000 vòng" không phải 25.000 request.

## 7. Coverage

### 7.1 Cách đo

```bash
dotnet test OrderPaymentMicroservice.Tests/OrderPaymentMicroservice.Tests.csproj \
  --no-restore --nologo \
  --filter 'FullyQualifiedName~InputValidatorTests|FullyQualifiedName~MoneyPrecisionTests' \
  /p:CollectCoverage=true \
  /p:CoverletOutput=../security-analysis/results/input-validator \
  /p:CoverletOutputFormat=cobertura \
  '/p:Include=[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.InputValidator%2c[OrderPaymentMicroservice]OrderPaymentMicroservice.Security.MoneyAmounts'
python3 security-analysis/collect_validator_coverage.py
```

`collect_validator_coverage.py` chỉ đọc Cobertura đã sinh; chạy riêng nó **không**
tạo phép đo mới.

### 7.2 Kết quả đang lưu

| Chỉ số | Giá trị | Ngưỡng |
| --- | --- | --- |
| `InputValidator` branch | 97,22% | ≥ 90% |
| `InputValidator` line | 85,71% | — |
| `ValidateCreateOrderRequest` branch | 100% | — |
| `ValidateOrderItem` branch | 92,85% | — |
| `MoneyAmounts` branch | 100% | — |
| `uncovered_branches` | rỗng | — |

### 7.3 Đọc số cho đúng

- **Branch 97,22%** là chỉ số chính; ngưỡng đặt ở 90%.
- **Line 85,71%** thấp hơn branch; line và branch là hai phép đo khác nhau, không
  đồng nghĩa thiếu nhánh logic.
- `uncovered_branches` (danh sách nhánh có `condition-coverage` dưới 100%) đang rỗng,
  nhưng **không** được suy ra rằng mọi tổ hợp điều kiện đã được kiểm.
- Coverage chỉ trên `InputValidator` và `MoneyAmounts`; **không** đại diện cho toàn
  controller/service/UI/DB.
- Coverage cao giúp tìm vùng chưa thử, không chứng minh không còn lỗi.

## 8. Điều không được kết luận

1. "Validator đúng" không có nghĩa toàn hệ thống đúng; còn quyền, kho, giá, DB.
2. Branch coverage 97,22% không phải độ phủ của toàn ứng dụng.
3. Fuzz 25.000 vòng không phải chứng minh không có input gây lỗi.
4. Tolerance 1 cent không phải kẽ hở để client điều khiển giá.
5. Test PASS trên SQLite/in-memory không thay thế test PostgreSQL.
