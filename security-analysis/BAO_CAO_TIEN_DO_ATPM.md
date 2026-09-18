# BÁO CÁO TIẾN ĐỘ DỰ ÁN ORDERPAYMENTMICROSERVICE

**Định hướng: An toàn phần mềm trong quản lý đơn hàng và thanh toán**  
Ngày lập báo cáo: 17/09/2026  
Phạm vi: mã nguồn và các kết quả kiểm thử, kiểm chứng hiện có trong repository.

## 1. Tổng quan và mục tiêu

OrderPaymentMicroservice là dự án xây dựng dịch vụ quản lý đơn hàng và thanh toán bằng ASP.NET Core. Mã nguồn được tổ chức theo các lớp Controller, Service, DTO, Model và lớp truy cập dữ liệu sử dụng Entity Framework Core với PostgreSQL. Các module hiện có gồm người dùng, sản phẩm, giỏ hàng, đơn hàng và thanh toán.

Ngoài chức năng nghiệp vụ, dự án được phát triển theo định hướng an toàn phần mềm: kiểm soát dữ liệu đầu vào, xác định các bất biến về tiền và trạng thái, kiểm chứng luồng xử lý, kiểm thử các trường hợp biên và đánh giá rủi ro từ mã nguồn. Mục tiêu là phát hiện sớm các hành vi có thể làm sai lệch đơn hàng, thanh toán hoặc quyền truy cập dữ liệu.

Tại thời điểm báo cáo, dự án đã hoàn thành phần bổ sung mô hình Kripke, kiểm thử InputValidator và dataset đánh giá rủi ro theo tài liệu ATPM. Các kết quả này tạo nền tảng cho giai đoạn khắc phục và kiểm chứng lại; chưa đồng nghĩa với việc hệ thống đã đủ an toàn để triển khai thực tế.

## 2. Nền tảng chức năng đã xây dựng

### 2.1. Tổ chức kiến trúc và dữ liệu

Dự án đã có các Controller và Service riêng cho từng nhóm chức năng, các DTO trao đổi dữ liệu và các model nghiệp vụ. Cấu hình dependency injection giúp tách phần tiếp nhận HTTP khỏi logic xử lý. Dự án cũng có cấu hình kết nối PostgreSQL và migration cơ sở dữ liệu.

Về an toàn phần mềm, cách tổ chức này giúp xác định rõ nơi cần đặt các kiểm tra: dữ liệu đầu vào tại validator, quyền sở hữu tại lớp nghiệp vụ, ràng buộc trạng thái và số tiền trước khi ghi dữ liệu. Tuy nhiên, kiến trúc phân lớp chỉ hỗ trợ việc triển khai kiểm soát, không tự bảo đảm các kiểm soát đã đầy đủ.

### 2.2. Đơn hàng, thanh toán và giỏ hàng

Mã nguồn đã có xử lý tạo, xem và hủy đơn hàng; tạo và xem thanh toán; xem, thêm, cập nhật và xóa sản phẩm trong giỏ hàng. Order có tám trạng thái: CREATED, VALIDATED, PAYMENT_PENDING, PAID, CONFIRMED, COMPLETED, CANCELLED và FAILED.

Phần thanh toán hiện còn mang tính mô phỏng: PaymentService tạo kết quả SUCCESS thay vì xác minh qua cổng thanh toán. Các bước chuyển trạng thái trung gian chưa được hiện thực đầy đủ theo luồng kỳ vọng. Vì vậy, tiến độ chức năng ở đây được ghi nhận ở mức **đã có mã xử lý**, không phải đã kiểm thử tích hợp đầy đủ hoặc sẵn sàng vận hành.

## 3. Các công việc an toàn phần mềm đã hoàn thành

### 3.1. Phân tích luồng nghiệp vụ và bất biến

Dự án đã có tài liệu `WorkflowAnalysis.md` phân tích quan hệ giữa đơn hàng, thanh toán và các ràng buộc cần bảo vệ. Những yêu cầu trọng tâm gồm:

- Đơn hàng phải chuyển trạng thái theo quy tắc, không bỏ bước hoặc quay ngược trái phép.
- Đơn hàng cuối cùng phải hoàn tất, bị hủy hoặc thất bại trong các giả định của mô hình.
- Trạng thái PAID phải đi kèm thanh toán thành công.
- Tổng tiền đơn hàng phải nhất quán với tổng tiền các item; thanh toán cần được đối chiếu với giá trị đơn hàng.

Việc xác định các yêu cầu này giúp chuyển khái niệm “an toàn” thành những thuộc tính cụ thể có thể kiểm tra, thay vì chỉ đánh giá dựa trên việc chức năng chạy được.

### 3.2. Mô hình hóa và kiểm chứng bằng Kripke

Đã tạo `order_state_model.json` theo schema của Lab 1, biểu diễn cấu trúc Kripke M = (S, S0, R, L): tập trạng thái, trạng thái khởi tạo, quan hệ chuyển và nhãn mệnh đề. Model kỳ vọng gồm tám trạng thái Order, với CREATED là trạng thái khởi tạo.

Đã tích hợp trực tiếp bộ kiểm tra `kripke_model.py` của Lab 1 vào `run_analysis.py`. Do script mẫu chưa hỗ trợ AF, phần tích hợp bổ sung thuật toán điểm cố định để kiểm tra yêu cầu kết thúc trên mọi đường đi.

| Thuộc tính | Nội dung kiểm chứng | Kết quả model kỳ vọng |
|---|---|---|
| P1 — Safety | Không chuyển trạng thái trái phép hoặc hồi sinh đơn đã kết thúc | PASS |
| P2 — Liveness | Từ mọi trạng thái reachable, mọi đường đi cuối cùng đến trạng thái kết thúc | PASS |
| P3 — Consistency | PAID phải có payment SUCCESS | PASS |
| P4 — Consistency | PAYMENT_PENDING chưa có payment SUCCESS | PASS |

Kết quả là **4/4 thuộc tính PASS trên model kỳ vọng**. Model không có vòng chờ ở trạng thái chưa kết thúc; vì vậy kết quả liveness phụ thuộc vào giả định tiến triển này. Model chưa phải bản trích xuất đầy đủ từ mã thực thi, nên không thể dùng kết quả PASS để khẳng định API hiện tại đúng.

### 3.3. Xây dựng model lỗi và chứng minh phản ví dụ

Đã tạo `order_state_model_buggy.json` để mô phỏng việc cho phép thanh toán đơn đã hủy. Model sử dụng trạng thái `PAID_AFTER_CANCELLED` để lưu lịch sử đi vào PAID từ CANCELLED và gắn nhãn chuyển trạng thái bất hợp lệ.

Bộ kiểm tra phát hiện P1 bị vi phạm và trả về phản ví dụ:

```text
CREATED → CANCELLED → PAID_AFTER_CANCELLED
```

Phản ví dụ thể hiện một đơn đã hủy vẫn có thể trở thành đơn đã thanh toán. Đây cũng là nguy cơ được nhận diện trong PaymentService: mã hiện tại cập nhật Order sang PAID mà chưa kiểm tra trạng thái nguồn. Kết quả cho thấy phương pháp kiểm chứng có thể phát hiện lỗi đã mô hình hóa, không chỉ trả về PASS cho thiết kế đúng.

### 3.4. Kiểm thử dữ liệu đầu vào bằng xUnit

Đã xây dựng project `OrderPaymentMicroservice.Tests` và bộ test `InputValidatorTests.cs`. Các test chạy độc lập với PostgreSQL và không cần khởi động API.

Các nhóm trường hợp đã kiểm tra gồm null request, null item, danh sách item null hoặc rỗng; ID không hợp lệ; số tiền âm, bằng 0 hoặc vượt giới hạn; số lượng dưới/trên biên; giá âm hoặc vượt mức tối đa; các giá trị biên hợp lệ; nhiều item; sai lệch tổng tiền trong và ngoài tolerance; decimal.MaxValue và phép nhân lớn nhất sau validation.

Kết quả thực tế: **34 test PASS, 0 FAIL, 0 SKIP**.

Một số test được viết để ghi nhận hành vi lỗi hiện tại: tổng tiền không khớp, giá bằng 0 hoặc dưới mức tối thiểu khai báo vẫn được chấp nhận. Các test này PASS vì xác nhận đúng hành vi đang tồn tại, không phải vì hành vi đó đáp ứng yêu cầu an toàn.

### 3.5. Đo độ bao phủ và phân tích nhánh thiếu

Đã sử dụng Coverlet để đo coverage, giới hạn báo cáo vào lớp InputValidator và đặt ngưỡng branch coverage tối thiểu 90%.

| Phạm vi | Branch coverage |
|---|---|
| Toàn bộ InputValidator | 31/32 nhánh, khoảng 96,87% |
| ValidateCreateOrderRequest() | 100% |
| ValidateOrderItem() | Khoảng 92,85% |

Mục tiêu **≥ 90% branch coverage đã đạt**. Line coverage toàn lớp là 83,67%, một chỉ số khác với branch coverage.

Nhánh tolerance dòng 61 đã được test cả hai hướng. Tuy nhiên, khi sai lệch lớn hơn 0,01, block xử lý hiện rỗng nên không từ chối request. Điều này minh họa rằng coverage cao chưa đủ chứng minh validator an toàn.

Nhánh chưa được phủ là hướng true của `subtotal < 0` tại dòng 99. Với Quantity từ 1 đến 10000 và UnitPrice từ 0 đến 999999,99 sau các guard, subtotal không thể âm. Catch OverflowException cũng chưa chạy do tích tối đa vẫn nằm trong miền decimal. Báo cáo đã giải thích các đường đi bất khả thi này thay vì sửa mã hoặc ép dữ liệu để tăng coverage hình thức.

### 3.6. Nhận diện và đánh giá rủi ro

Đã xây dựng `vulnerabilities.json` theo cấu trúc dataset Lab 3, mô tả vị trí, nguyên nhân, điều kiện khai thác và tác động của năm phát hiện. Đã chạy hàm đánh giá của `risk_assessor.py` để tính điểm và sắp xếp ưu tiên.

| ID | Phát hiện | CVSS-lite | Mức độ |
|---|---|---|---|
| VULN-001 | API Order thiếu xác thực và bảo vệ truy cập | 9,1 | CRITICAL |
| VULN-005 | Thiếu kiểm tra quyền sở hữu giỏ hàng | 8,1 | HIGH |
| VULN-002 | Giá và tổng tiền đơn hàng do client quyết định | 7,5 | HIGH |
| VULN-003 | Đơn CANCELLED có thể được chuyển sang PAID | 7,5 | HIGH |
| VULN-004 | Tổng tiền sai lệch vẫn được validator chấp nhận | 7,5 | HIGH |

VULN-005 là phát hiện bổ sung từ review CartController và CartService. Client cung cấp userId/cartId/itemId nhưng mã chưa đối chiếu với danh tính người gọi. Kiểm tra item thuộc cart không thay thế cho kiểm tra cart thuộc người dùng hiện tại. Khi API truy cập được qua mạng và không được lớp ngoài bảo vệ, vấn đề này có thể dẫn đến xem hoặc sửa/xóa item trong giỏ hàng của người khác.

Điểm 8,1 HIGH được tính với tác động confidentiality LOW, integrity HIGH và availability NONE. Chưa có bằng chứng về mất khả dụng toàn dịch vụ nên không nâng tác động availability. Toàn bộ điểm số là **CVSS-lite phục vụ học tập**, không phải CVSS chính thức; các phát hiện quyền truy cập dựa trên review mã, chưa phải khai thác trực tiếp API đang chạy.

## 4. Minh chứng và khả năng tái lập

Các sản phẩm bàn giao đã được lưu trong repository:

- [Model Order kỳ vọng](order_state_model.json) và [model BUGGY](order_state_model_buggy.json).
- [Bộ test xUnit](../OrderPaymentMicroservice.Tests/InputValidatorTests.cs).
- [Báo cáo coverage XML](results/input-validator.cobertura.xml) và [tổng hợp coverage](results/coverage_summary.json).
- [Dataset lỗ hổng](vulnerabilities.json) và [kết quả xếp hạng rủi ro](results/risk_assessment_results.json).
- [Kết quả model kỳ vọng](results/order_state_model.results.json) và [kết quả model BUGGY](results/order_state_model_buggy.results.json).
- [Script tích hợp](run_analysis.py) và [hướng dẫn chạy lại](README.md).

Dự án còn có các tệp đặc tả TLA+ và ràng buộc Z3  trong thư mục Security. Báo cáo này chỉ ghi nhận chúng là tài liệu/mã đặc tả hiện có; chưa có kết quả chạy TLC hoặc Z3 được xác nhận trong phạm vi công việc vừa thực hiện.

## 5. Hạn chế và công việc chưa hoàn thành

Các phát hiện đã được phân tích và ghi nhận nhưng **chưa được khắc phục trong mã nghiệp vụ**. Những phần còn thiếu gồm xác thực và phân quyền API; kiểm tra quyền sở hữu đơn hàng/giỏ hàng; tính giá đáng tin cậy phía server; từ chối mismatch; kiểm soát chuyển trạng thái; đối chiếu số tiền thanh toán và xác minh kết quả gateway.

Coverage mới đo cho InputValidator, chưa đại diện cho toàn project. Bộ test hiện tại là unit test, chưa xác nhận an toàn truy cập hoặc tính nhất quán giao dịch khi API và cơ sở dữ liệu chạy thực tế. Chưa có kết quả kiểm thử đồng thời, fuzzing hoặc kiểm thử bảo mật động được xác nhận trong đợt bổ sung này.

Các model kỳ vọng, đặc tả TLA+ và implementation cũng cần được đồng bộ quy tắc trước khi dùng làm cơ sở kiểm chứng rộng hơn. Không đưa ra phần trăm hoàn thành toàn dự án vì chưa có kế hoạch tổng thể và tiêu chí nghiệm thu đầy đủ để đối chiếu.

## 6. Kế hoạch giai đoạn tiếp theo

1. **Ưu tiên quyền truy cập:** bổ sung authentication và kiểm tra ownership cho Order/Cart; viết test chứng minh người dùng không thể truy cập tài nguyên của người khác.
2. **Bảo vệ tính toàn vẹn tiền:** lấy giá từ dữ liệu sản phẩm phía server, tính lại tổng tiền, từ chối mismatch và áp dụng quy tắc giá tối thiểu rõ ràng.
3. **Hoàn thiện trạng thái và thanh toán:** triển khai bảng chuyển trạng thái hợp lệ, chặn thanh toán đơn đã hủy/kết thúc; xác định quy tắc thanh toán một phần và tích hợp xác minh gateway.
4. **Kiểm soát giao dịch đồng thời:** kiểm tra thanh toán lặp, vượt tổng tiền và xung đột cập nhật; thiết kế cơ chế transaction/idempotency phù hợp.
5. **Kiểm chứng lại sau sửa:** chuyển test ghi nhận lỗi thành test yêu cầu từ chối hành vi sai; duy trì branch coverage ≥ 90%; mở rộng integration test và model sát implementation.

Tiêu chí hoàn thành giai đoạn khắc phục là có thay đổi mã, test hồi quy và kết quả chạy xác nhận từng rủi ro được kiểm soát, không chỉ có mô tả khuyến nghị.

## 7. Kết luận

Phần 8 dưới đây trình bày chi tiết quá trình thực hiện, kiến thức áp dụng và các lỗi gặp phải để phục vụ thuyết trình hoặc báo cáo tiến độ.

Dự án đã xây dựng được nền tảng đánh giá an toàn phần mềm kết hợp ba hướng: **kiểm chứng mô hình, kiểm thử có đo coverage và đánh giá rủi ro từ mã nguồn**. Các kết quả nổi bật gồm model kỳ vọng PASS bốn thuộc tính, phản ví dụ cho lỗi hồi sinh đơn đã hủy, 34 test xUnit thành công và branch coverage InputValidator vượt mục tiêu 90%.

Quan trọng hơn, quá trình kiểm tra đã chỉ ra các khoảng trống về quyền truy cập và tính toàn vẹn nghiệp vụ dù test có thể PASS và coverage cao. Tiến độ hiện tại vì vậy được đánh giá là **đã hoàn thành phần bổ sung phân tích và kiểm chứng ATPM, đang chuẩn bị cho giai đoạn khắc phục và kiểm thử hồi quy**; chưa đủ cơ sở kết luận hệ thống đã an toàn để vận hành thực tế.

## 8. Chi tiết thực hiện và kiến thức an toàn phần mềm đã áp dụng

### 8.1. Phân tích mã nguồn để xác định yêu cầu an toàn

**Tôi đã làm gì?**

Tôi đối chiếu luồng xử lý Order và Payment với các yêu cầu trong tài liệu ATPM và `WorkflowAnalysis.md`, xác định những điều kiện phải luôn đúng khi hệ thống vận hành. Tôi tập trung vào trạng thái đơn hàng, tính nhất quán của số tiền và quyền truy cập tài nguyên.

**Tôi làm như thế nào?**

Tôi xem `OrderStatus.cs` để xác định tập trạng thái; xem `OrderService.CreateOrderAsync()` để xác định cách tiếp nhận và lưu đơn hàng; xem `CancelOrderAsync()` để biết điều kiện hủy; xem `PaymentService.CreatePaymentAsync()` để biết cách tạo thanh toán và cập nhật trạng thái. Với dữ liệu đầu vào, tôi đọc các điều kiện trong InputValidator và DTO thay vì chỉ dựa vào giao diện nhập liệu. Với quyền truy cập, tôi đối chiếu Controller, Service và cấu hình middleware trong `Program.cs`.

**Tôi sử dụng kiến thức gì?**

Tôi áp dụng kiến thức về bất biến nghiệp vụ, máy trạng thái hữu hạn và ranh giới tin cậy. Bất biến là điều kiện không được phép bị phá vỡ, chẳng hạn một đơn PAID phải có thanh toán thành công. Ranh giới tin cậy giúp nhận ra rằng ID, giá và số tiền từ client chưa thể được coi là dữ liệu đáng tin cậy chỉ vì chúng đúng kiểu dữ liệu.

**Kết quả và vấn đề gặp phải**

Tôi chuyển yêu cầu nghiệp vụ thành bốn thuộc tính kiểm chứng và các nhóm test cụ thể. Tôi cũng nhận thấy sự khác biệt giữa thiết kế và implementation: enum có đầy đủ trạng thái nhưng mã chưa thực hiện đầy đủ các bước trung gian. Vì vậy, tôi tách rõ kết quả kiểm chứng thiết kế khỏi kết luận về mã chạy thực tế.

### 8.2. Xây dựng model Order theo cấu trúc Kripke

**Tôi đã làm gì?**

Tôi tạo `order_state_model.json` để biểu diễn luồng Order thành dữ liệu mà công cụ Lab 1 có thể đọc và kiểm tra.

**Tôi làm như thế nào?**

Tôi giữ schema JSON của Lab 1 và ánh xạ từng thành phần:

- **S:** tám trạng thái từ CREATED đến FAILED, lưu trong `states`.
- **S0:** tập trạng thái ban đầu `{CREATED}`, lưu trong `initial_states`.
- **R:** danh sách cạnh `from`, `to`, `event`, mô tả các bước tiến triển, hủy và thất bại.
- **L:** nhãn của từng trạng thái, ví dụ `paid`, `payment_success`, `terminal`.

Tôi gắn `payment_success` vào PAID, CONFIRMED và COMPLETED trong model kỳ vọng. COMPLETED, CANCELLED và FAILED được gắn `terminal` và có self-loop để trạng thái kết thúc vẫn có bước kế tiếp theo ngữ nghĩa Kripke. Script `create_datasets.py` giúp sinh lại model theo cùng quy tắc; `run_analysis.py` đọc các model đã lưu để kiểm chứng.

**Tôi sử dụng kiến thức gì?**

Tôi sử dụng cấu trúc Kripke M = (S, S0, R, L), mệnh đề nguyên tử và logic thời gian CTL. Tôi phân biệt safety — không có hành vi xấu — với liveness — cuối cùng có sự tiến triển mong muốn. Tôi cũng sử dụng khái niệm trạng thái reachable để chỉ kiểm tra những trạng thái có thể đi đến từ trạng thái ban đầu.

**Kết quả và giới hạn**

Model đúng có tám trạng thái và cả tám đều reachable. Các nhãn thanh toán là giả định của thiết kế, chưa phải dữ liệu lấy từ gateway hoặc database. Do đó, P3/P4 PASS xác nhận sự nhất quán của nhãn trong model, không xác nhận payment thực tế đã được đối soát.

### 8.3. Chạy kiểm chứng và bổ sung thuộc tính liveness

**Tôi đã làm gì?**

Tôi viết phần tích hợp để sử dụng trực tiếp công cụ Lab 1 trên model Order, đồng thời bổ sung phép kiểm tra mà script mẫu chưa hỗ trợ.

**Tôi làm như thế nào?**

Trong `run_analysis.py`, tôi dùng `importlib` để nạp module gốc theo đường dẫn. Tôi chuyển các thuộc tính được hỗ trợ vào `run_verification()`: P1 dùng AG_NOT, P3 và P4 dùng AG_IMPLIES. Công cụ mẫu duyệt BFS để tìm trạng thái reachable và đường đi đến trạng thái vi phạm.

Với P2, tôi tính tập trạng thái thỏa `AF terminal` bằng điểm cố định nhỏ nhất:

1. Khởi tạo tập `good` bằng các trạng thái đã có nhãn terminal.
2. Thêm một trạng thái nếu nó có successor và **tất cả** successor đều nằm trong `good`.
3. Lặp cho đến khi không thêm được trạng thái nào nữa.
4. Kiểm tra mọi trạng thái reachable có nằm trong `good` hay không; đây là phép kiểm tra `AG AF terminal`.

**Tôi sử dụng kiến thức gì?**

Tôi sử dụng CTL: AG nghĩa là trên mọi đường đi, tại mọi trạng thái; AF nghĩa là trên mọi đường đi, cuối cùng; EF nghĩa là tồn tại một đường đi cuối cùng đạt điều kiện. Thuật toán điểm cố định cho phép phân biệt việc *có thể kết thúc* với việc *chắc chắn cuối cùng kết thúc* trong model hữu hạn.

**Tôi gặp khó khăn gì và xử lý ra sao?**

Lab 1 chỉ hỗ trợ AG_NOT, AG_IMPLIES và EF. Nếu dùng EF terminal thay cho liveness thì kết luận sẽ yếu hơn yêu cầu: một đường đi kết thúc không loại trừ đường đi khác chờ vô hạn. Tôi giải quyết bằng cách bổ sung AF ở script tích hợp, không sửa script Lab gốc và không tuyên bố Lab 1 nguyên bản đã hỗ trợ đủ bốn thuộc tính.

**Kết quả**

Model kỳ vọng PASS bốn thuộc tính. Kết quả được lưu trong `results/order_state_model.results.json`. Script có assertion yêu cầu toàn bộ thuộc tính của model này phải PASS, giúp phát hiện thay đổi ngoài ý muốn trong lần chạy sau. Liveness được chứng minh trong model không có vòng chờ nonterminal; chưa bao quát tình huống gateway không phản hồi vô hạn.

### 8.4. Tạo model BUGGY và giải thích counterexample

**Tôi đã làm gì?**

Tôi cố ý tạo một model có lỗi để kiểm tra công cụ có thực sự nhận ra vi phạm safety hay không. Lỗi được chọn là cho phép một đơn CANCELLED được thanh toán và chuyển sang PAID.

**Tôi làm như thế nào?**

Tôi sao chép model kỳ vọng và thêm trạng thái `PAID_AFTER_CANCELLED`, có các nhãn `paid`, `payment_success` và `invalid_transition`. Tôi thêm cạnh từ CANCELLED đến trạng thái này, với sự kiện mô tả `CreatePaymentAsync: missing order status guard`.

Trạng thái mới không phải một giá trị enum được thêm vào ứng dụng. Nó là trạng thái của model dùng để phân biệt PAID hợp lệ với PAID có lịch sử đi qua CANCELLED.

**Tôi sử dụng kiến thức gì?**

Tôi sử dụng counterexample trong model checking và kỹ thuật history monitor: bổ sung thông tin lịch sử vào trạng thái để biến lỗi của một bước chuyển thành điều kiện có thể kiểm tra trên nhãn trạng thái. BFS của Lab 1 cung cấp đường đi ngắn tới trạng thái vi phạm.

**Tôi gặp giới hạn gì?**

Nếu chỉ thêm cạnh CANCELLED → PAID vào model cũ, nhãn PAID vẫn không đổi. Bộ kiểm tra AG trên mệnh đề trạng thái sẽ không tự hiểu cạnh đó bất hợp lệ. Tôi giải quyết bằng history monitor và nhãn `invalid_transition`. Nhãn này được thiết lập khi xây dựng model, không phải công cụ tự suy ra tính hợp lệ của mọi cạnh.

**Kết quả**

P1 FAIL với đường đi `CREATED → CANCELLED → PAID_AFTER_CANCELLED`; P2, P3 và P4 vẫn PASS. Điều này có ý nghĩa: model lỗi vi phạm quy tắc trạng thái dù vẫn có nhãn payment_success. Thành công của kiểm chứng ở đây chính là tìm được vi phạm dự kiến. Script còn assert đúng chuỗi counterexample để kiểm tra kết quả tái lập.

### 8.5. Thiết kế và chạy bộ test InputValidator

**Tôi đã làm gì?**

Tôi tạo project xUnit riêng và xây dựng 34 trường hợp kiểm thử cho `ValidateCreateOrderRequest()` cùng phần kiểm tra item được gọi bên trong.

**Tôi làm như thế nào?**

Tôi tạo hàm `Valid()` trả về request nền hợp lệ: CustomerId = 1, TotalAmount = 10, một item có ProductId = 1, Quantity = 1 và UnitPrice = 10. Mỗi test chỉ thay đổi dữ liệu liên quan đến điều kiện cần kiểm tra. Cách này tránh trường hợp test muốn kiểm tra giá nhưng lại bị chặn sớm vì CustomerId sai.

Tôi dùng `[Fact]` cho trường hợp đơn lẻ và `[Theory]`/`[InlineData]` cho nhóm dữ liệu. Với input bị từ chối, tôi dùng `Assert.Throws` và kiểm tra `ParamName` để xác nhận đúng loại lỗi và đúng trường dữ liệu. Các số decimal được parse bằng InvariantCulture để không phụ thuộc dấu thập phân của máy chạy test.

**Tôi sử dụng kiến thức gì?**

Tôi áp dụng phân lớp tương đương, phân tích giá trị biên, kiểm thử đường đi và test oracle — tiêu chí để quyết định hành vi đúng/sai. Tôi kiểm tra cả dữ liệu hợp lệ và dữ liệu không hợp lệ, thay vì chỉ kiểm tra luồng tạo đơn thành công.

| Nhóm | Dữ liệu tiêu biểu | Mục đích |
|---|---|---|
| Null/rỗng | Request null, item null, Items null hoặc [] | Kiểm tra xử lý thiếu dữ liệu |
| ID | 0, -1, CustomerId = int.MinValue | Kiểm tra miền ID dương |
| Total | -1, 0, 0.009, 1000000, decimal.MaxValue | Kiểm tra dưới/trên giới hạn |
| Quantity | 0, -1, 10001, int.MaxValue | Kiểm tra miền số lượng |
| Price | -0.01, 1000000, decimal.MaxValue | Kiểm tra giá âm hoặc quá lớn |
| Biên hợp lệ | Total 0.01/999999.99, Quantity 10000 | Kiểm tra biên được chấp nhận |
| Tolerance | Total 10.01/9.99 so với tổng item 10 | Kiểm tra đúng ngưỡng ±0.01 |
| Hành vi lỗi | Total 10.011/9.989 so với 10; giá 0/0.001 | Ghi nhận validation chưa đầy đủ |

**Kết quả và lỗi được phát hiện**

34 test đều PASS. Nhưng các test `KnownDefect_MismatchOrBelowMinimumPriceIsAccepted` cho thấy request sai nghiệp vụ vẫn được chấp nhận. Ví dụ tổng khai báo 0,01 và tổng item 999999,99 không gây exception. Đây là lỗi logic, không phải lỗi biên dịch hoặc lỗi chạy test.

Tôi chưa sửa validator trong giai đoạn này. Sau khi khắc phục, các test ghi nhận việc chấp nhận phải được thay bằng assertion yêu cầu từ chối dữ liệu sai; nếu giữ nguyên assertion cũ thì test sẽ FAIL khi hành vi được sửa đúng.

### 8.6. Đo coverage và giải thích vì sao chưa đạt 100% toàn lớp

**Tôi đã làm gì?**

Tôi sử dụng `coverlet.msbuild` để đo branch coverage của InputValidator và xuất báo cáo Cobertura XML, kèm ngưỡng tối thiểu 90%.

**Tôi làm như thế nào?**

Tôi chạy `dotnet test` với `CollectCoverage=true`, lọc Include vào đúng lớp InputValidator và đặt `ThresholdType=branch`. Sau đó, script Python đọc XML, lấy coverage từng method và liệt kê các dòng có nhánh chưa được phủ. Báo cáo tóm tắt được lưu thành JSON để dễ tra cứu.

**Tôi sử dụng kiến thức gì?**

Tôi phân biệt line coverage — dòng mã đã chạy — với branch coverage — kết quả true/false của điều kiện đã được thực hiện. Tôi cũng áp dụng phân tích miền giá trị để xác định một đường đi có khả thi hay không.

**Kết quả cụ thể**

Toàn lớp có 31/32 nhánh được phủ, đạt 96,875% theo số đếm; CLI/XML hiển thị xấp xỉ 96,87%. Riêng `ValidateCreateOrderRequest()` đạt 100% branch coverage. Dòng tolerance 61 có 2/2 nhánh được phủ, nên vấn đề ở đây là thiếu hành động từ chối, không còn là thiếu test đi qua nhánh.

Nhánh true của `subtotal < 0` chưa được phủ. Sau guard, Quantity ≥ 1 và UnitPrice ≥ 0 nên tích không âm. Tích lớn nhất là `10000 × 999999.99 = 9.999.999.900`, nằm trong miền decimal; catch overflow chưa được chạy vì không có input đi qua guard hiện tại gây tràn phép nhân này. Kết luận này chỉ áp dụng cho phép nhân item đã giới hạn, không phải chứng minh mọi phép cộng/tính tiền của toàn hệ thống không thể overflow.

**Lỗi công cụ tôi gặp và cách xử lý**

Ban đầu phần đọc XML so sánh thuộc tính `branch` với chuỗi `true`, trong khi Coverlet xuất `True`. Vì vậy danh sách nhánh thiếu bị rỗng dù branch coverage dưới 100%. Tôi sửa thành so sánh sau khi `.lower()`; báo cáo sau đó xác định đúng dòng 99 có coverage 50% (1/2).

Bài học là không chỉ tin vào file tổng hợp do mình sinh ra; cần đối chiếu trực tiếp với dữ liệu XML của công cụ đo.

### 8.7. Tạo dataset lỗ hổng và tính CVSS-lite

**Tôi đã làm gì?**

Tôi xây dựng năm bản ghi VULN-001 đến VULN-005 cho chính project, thay vì sử dụng nguyên các lỗi bộ nhớ C trong dataset mẫu Lab 3.

**Tôi làm như thế nào?**

Tôi ghi lại tên, CWE, vị trí mã, mô tả, phương pháp phát hiện và các yếu tố AV/AC/PR/UI/C/I/A. Vì CLI Lab 3 dùng đường dẫn dataset cố định, tôi gọi trực tiếp `assess_all()` với `security-analysis/vulnerabilities.json`. Công cụ tính điểm, gán severity và sắp xếp giảm dần; kết quả được lưu riêng trong project.

**Tôi sử dụng kiến thức gì?**

Tôi sử dụng CWE để phân loại điểm yếu, bộ ba confidentiality/integrity/availability để mô tả tác động, và nguyên tắc đánh giá rủi ro dựa trên khả năng khai thác kết hợp tác động. Tôi phân biệt authentication — người gọi là ai — với authorization — người đó được phép thao tác tài nguyên nào.

**Ví dụ tính điểm VULN-005**

Theo công thức giáo dục của Lab 3:

```text
Exploitability = 8.22 × AV × AC × PR × UI
               = 8.22 × 0.85 × 0.77 × 0.85 × 0.85 ≈ 3.89
Impact = 6.42 × [1 − (1 − C)(1 − I)(1 − A)]
       = 6.42 × [1 − (1 − 0.22)(1 − 0.56)(1 − 0)] ≈ 4.22
Base score = round(min(Exploitability + Impact, 10), 1) = 8.1
Severity = HIGH
```

Đây là công thức thực tế của script Lab 3, không phải công thức CVSS chính thức. Mức điểm phụ thuộc đánh giá tác động và giả định API truy cập được qua mạng, không có lớp xác thực bên ngoài.

**Kết quả và vấn đề phát hiện**

VULN-001 được xếp cao nhất, 9,1 CRITICAL; VULN-005 là 8,1 HIGH; ba phát hiện còn lại cùng 7,5 HIGH. Các điểm bằng nhau không tạo ra ưu tiên khác biệt theo số điểm. Các lỗi này đang ở trạng thái đã phát hiện/ghi nhận, chưa được khắc phục.

### 8.8. Phân tích riêng VULN-005: quyền sở hữu giỏ hàng

**Tôi đã làm gì và làm như thế nào?**

Tôi lần theo dữ liệu từ route `user/{userId}` và `{cartId}/item/{itemId}` trong CartController đến CartService. Tôi thấy Service tìm giỏ theo userId do client đưa vào; khi sửa hoặc xóa, Service kiểm tra item thuộc cartId nhưng chưa đối chiếu chủ giỏ với danh tính người gọi.

Ví dụ về thiếu kiểm soát: một người biết cartId và itemId của giỏ khác có thể gửi yêu cầu thao tác lên các ID đó. Điều kiện `cartItem.CartId == cartId` chỉ xác nhận hai tài nguyên liên quan đúng với nhau; nó không xác nhận người gửi yêu cầu được phép truy cập chúng.

**Kiến thức sử dụng và kết quả**

Tôi áp dụng kiến thức kiểm soát truy cập mức đối tượng và CWE-639: quyền truy cập không được quyết định chỉ bằng khóa do người dùng cung cấp. Tôi ghi nhận nguy cơ đọc dữ liệu giỏ và sửa/xóa item của người khác, xếp HIGH theo công cụ Lab 3. Chưa thực hiện yêu cầu khai thác trên API đang chạy, nên ví dụ trên là kịch bản phân tích từ code, không phải log tấn công thực nghiệm.

Hướng sửa là lấy user identity từ cơ chế authentication đáng tin cậy rồi kiểm tra ownership cho mọi thao tác. Chỉ bổ sung đăng nhập hoặc token mà không kiểm tra ownership vẫn chưa xử lý đầy đủ vấn đề này.

### 8.9. Lỗi môi trường và trạng thái xử lý

| Vấn đề | Biểu hiện/nguyên nhân | Xử lý hoặc trạng thái hiện tại |
|---|---|---|
| Restore NuGet bị chặn | NU1301, socket tới api.nuget.org:443 bị từ chối trong môi trường hạn chế | Chạy lại với quyền truy cập được phê duyệt; tải được xUnit/Coverlet và chạy test thành công |
| Phiên bản dependency | NU1603: yêu cầu Npgsql 8.0.7 nhưng resolve thành 8.0.8 | Ghi nhận warning; không thay dependency của ứng dụng trong phần bổ sung lab |
| Lab 1 thiếu AF | Không thể dùng checker gốc để kiểm tra trực tiếp liveness đã yêu cầu | Bổ sung fixed-point AF trong integration |
| Nhãn không lưu lịch sử | PAID cũ không phân biệt thanh toán hợp lệ và thanh toán sau hủy | Thêm PAID_AFTER_CANCELLED làm history monitor |
| Parser coverage sai chữ hoa/thường | `True` khác `true`, làm mất danh sách nhánh chưa phủ | Chuẩn hóa `.lower()`; nhận diện đúng dòng 99 |
| Tổng tiền sai được chấp nhận | Nhánh tolerance rỗng | Đã có test và VULN-004; chưa sửa nghiệp vụ |
| Đơn hủy vẫn thanh toán được | PaymentService thiếu guard trạng thái nguồn | Đã review và mô hình hóa VULN-003; chưa sửa nghiệp vụ |
| Quyền truy cập chưa đầy đủ | API thiếu authentication/ownership tương ứng | Đã ghi nhận VULN-001 và VULN-005; chưa khắc phục |

Tôi không ghi nhận các rủi ro dự kiến như race condition là lỗi đã tái hiện nếu chưa có kết quả kiểm thử đồng thời. Tương tự, chưa tuyên bố TLA+/Z3 hoặc fuzzing đã chạy thành công khi chưa có minh chứng.

### 8.10. Tổng hợp kiến thức và giá trị đạt được

Qua phần công việc này, tôi đã áp dụng mô hình hóa hình thức để kiểm tra thiết kế; BFS và CTL để kiểm tra khả năng đạt tới và thuộc tính thời gian; kiểm thử biên và phân lớp tương đương để kiểm tra validator; coverage để xác định nhánh chưa chạy; và CWE/CVSS-lite để phân loại, ưu tiên các phát hiện.

Kết quả quan trọng không chỉ là 34 test PASS hay coverage vượt 90%, mà là có minh chứng cụ thể cho những điều đã kiểm tra và giới hạn rõ ràng của kết luận. Tôi đã biết phân biệt ba tình huống: **thiết kế thỏa thuộc tính**, **test ghi nhận đúng hành vi hiện tại**, và **mã nghiệp vụ thực sự đã được sửa an toàn**. Hai tình huống đầu đã có kết quả; tình huống thứ ba còn là nhiệm vụ của giai đoạn tiếp theo.
