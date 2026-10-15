"""Create the OrderPaymentMicroservice security specification using the existing project workbook layout."""

from __future__ import annotations

from copy import deepcopy
from datetime import datetime, timezone
import json
from pathlib import Path
from tempfile import NamedTemporaryFile
from xml.etree import ElementTree as ET
from zipfile import ZIP_DEFLATED, ZipFile


ROOT = Path(__file__).resolve().parents[1]
TEMPLATE = ROOT / "OrderPaymentMicroservice_Security_Spec.xlsx"
OUTPUT = ROOT / "OrderPaymentMicroservice_Security_Spec.xlsx"
PIPELINE_JSON_OUTPUT = ROOT / "security-analysis" / "order_payment_pipeline_spec.json"

NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
REL_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
CORE_NS = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
DC_NS = "http://purl.org/dc/elements/1.1/"
DCTERMS_NS = "http://purl.org/dc/terms/"
XSI_NS = "http://www.w3.org/2001/XMLSchema-instance"
XML_NS = "http://www.w3.org/XML/1998/namespace"

ET.register_namespace("", NS)
ET.register_namespace("r", REL_NS)
ET.register_namespace("cp", CORE_NS)
ET.register_namespace("dc", DC_NS)
ET.register_namespace("dcterms", DCTERMS_NS)
ET.register_namespace("xsi", XSI_NS)


SHEETS = {
    "ThongTin": {
        "widths": [18, 92],
        "rows": [
            ["Truong", "GiaTri"],
            ["name", "order_payment_microservice_security"],
            [
                "description",
                "Đặc tả an toàn cho REST API OrderPaymentMicroservice (.NET 10/PostgreSQL): "
                "xác thực và phân quyền theo chủ sở hữu, toàn vẹn giá/đơn hàng/thanh toán, "
                "mô hình trạng thái Order, kiểm tra số học, và 5 mô-đun C mô phỏng lỗi bộ nhớ "
                "User/Product/Cart/Order/Payment.",
            ],
        ],
    },
    "YeuCauChucNang": {
        "widths": [13, 76, 18, 14],
        "rows": [
            ["ID", "MoTa", "DanhMuc", "MucUuTien"],
            ["REQ-1", "Mọi API Cart, Order và Payment phải yêu cầu JWT hợp lệ; chỉ Admin được liệt kê toàn bộ đơn hàng.", "safety", "HIGH"],
            ["REQ-2", "Người dùng chỉ được đọc hoặc thay đổi User, Cart, Order và Payment thuộc chính mình; quyền Admin là ngoại lệ được kiểm soát.", "safety", "HIGH"],
            ["REQ-3", "Khi tạo đơn, giá sản phẩm phải lấy từ cơ sở dữ liệu phía server, sản phẩm phải tồn tại/đang hoạt động và tồn kho phải đủ.", "functional", "HIGH"],
            ["REQ-4", "TotalAmount phải dương, trong giới hạn cho phép và khớp tổng Quantity × UnitPrice với sai số tối đa 0,01; dữ liệu sai phải bị từ chối.", "safety", "HIGH"],
            ["REQ-5", "Order chỉ được chuyển theo chuỗi CREATED → VALIDATED → PAYMENT_PENDING → PAID → CONFIRMED → COMPLETED; không bỏ bước, quay ngược hoặc hồi sinh trạng thái terminal.", "safety", "HIGH"],
            ["REQ-6", "Order chỉ được chuyển từ PAYMENT_PENDING sang PAID khi có Payment SUCCESS với Amount đúng bằng Order.TotalAmount.", "safety", "HIGH"],
            ["REQ-7", "Mỗi yêu cầu thanh toán phải có IdempotencyKey; gửi lại cùng khóa và cùng Amount phải trả về giao dịch cũ, còn tái dùng khóa với Amount khác phải bị từ chối.", "functional", "HIGH"],
            ["REQ-8", "Mật khẩu mới phải được băm bằng PasswordHasher có salt/work factor; tài khoản SHA-256 cũ chỉ được xác minh để nâng cấp một lần.", "safety", "HIGH"],
            ["REQ-9", "register_user() không được sao chép email dài từ 32 byte trở lên vào buffer stack 32 byte.", "memory", "HIGH"],
            ["REQ-10", "Product name không được vượt biên buffer heap 32 byte; nhánh từ chối vẫn phải giải phóng vùng nhớ đã cấp phát.", "memory", "HIGH"],
            ["REQ-11", "Cart audit log không được đọc CartItem sau khi phần tử đã được free.", "memory", "HIGH"],
            ["REQ-12", "Khi hủy Order trong mô hình C, mảng OrderItem thuộc sở hữu của Order phải được free trước struct cha.", "memory", "MEDIUM"],
            ["REQ-13", "Payment cleanup không được free TransactionCode hai lần; con trỏ sở hữu phải được đặt NULL sau lần free đầu.", "memory", "HIGH"],
            ["REQ-14", "Liveness có điều kiện: checker progress-only đạt terminal khi lọc các cạnh đánh dấu stutter. Raw AG AF terminal trên graph đầy đủ là false; không suy ra runtime luôn kết thúc khi provider còn UNKNOWN.", "liveness", "HIGH"],
        ],
    },
    "YeuCauPhiChucNang": {
        "widths": [13, 82, 14],
        "rows": [
            ["ID", "MoTa", "MucUuTien"],
            ["NFR-1", "Các mutation aggregate Order–Payment–Inventory dùng transaction Serializable trên PostgreSQL. Lưu PROCESSING/REFUND_PENDING trước khi gọi gateway; finalize là transaction riêng. Gateway và DB không có distributed transaction, khoảng hở được reconciliation xử lý.", "HIGH"],
            ["NFR-2", "Branch coverage tối thiểu 90% cho target đã đo: InputValidator và tập target fuzz được chọn. Coverage không đại diện toàn ứng dụng; ghi số đo và thời điểm từ coverage_summary.json/application_fuzzing.results.json.", "HIGH"],
            ["NFR-3", "Log lỗi phải đủ OrderId/UserId để truy vết nhưng không được ghi mật khẩu, JWT, Jwt:Key hoặc connection string.", "HIGH"],
            ["NFR-4", "Secret và connection string phải được cung cấp ngoài source control; Jwt:Key phải có ít nhất 32 ký tự.", "HIGH"],
            ["NFR-5", "Các bản C biên dịch với AddressSanitizer/UndefinedBehaviorSanitizer phải phát hiện đúng bản lỗi và không báo lỗi với bản FIXED trên cùng ca kiểm thử.", "MEDIUM"],
        ],
    },
    "RangBuoc": {
        "widths": [13, 82, 14],
        "rows": [
            ["ID", "MoTa", "MucUuTien"],
            ["CON-1", "Ứng dụng dùng ASP.NET Core/.NET 10, Entity Framework Core và PostgreSQL; REST JSON. Các module hiện chạy chung một host và một database, chưa triển khai thành các microservice độc lập.", "MEDIUM"],
            ["CON-2", "Order/Payment Amount nằm trong [0,01; 999999,99], Quantity nằm trong [1; 10000], và UnitPrice nằm trong [0,01; 999999,99].", "HIGH"],
            ["CON-3", "IdempotencyKey bắt buộc, không trắng và dài tối đa 128 ký tự; khóa là duy nhất trong phạm vi một Order.", "HIGH"],
            ["CON-4", "Order terminal trong source gồm COMPLETED, CANCELLED, FAILED, REFUNDED. PRE_PAYMENT_REJECTED là trạng thái trừu tượng của model, không lưu Order khi tạo thất bại. Self-loop terminal trong Kripke biểu diễn no-op/reject, không là mutation được source cho phép.", "HIGH"],
            ["CON-5", "Buffer email và product name trong mô hình C có 32 byte, nên chuỗi hợp lệ dài tối đa 31 byte để dành chỗ cho NUL.", "HIGH"],
            ["CON-6", "Năm mô-đun C là mô hình học tập về ownership/lifetime, không phải bằng chứng rằng ứng dụng C# managed mắc cùng lỗi bộ nhớ.", "MEDIUM"],
        ],
    },
    "MoHinhTrangThai": {
        "widths": [22, 12, 42, 46],
        "rows": [
            ["TrangThai", "KhoiTao", "Nhan", "ChuyenDenCacTrangThai"],
            ["CREATED", "x", "created", "VALIDATED, CANCELLED"],
            ["VALIDATED", "", "validated", "PAYMENT_PENDING, CANCELLED"],
            ["PAYMENT_PENDING", "", "payment_pending", "PAID, CANCELLED, FAILED"],
            ["PAID", "", "paid, payment_success", "CONFIRMED"],
            ["CONFIRMED", "", "confirmed, payment_success", "COMPLETED"],
            ["COMPLETED", "", "completed, payment_success, terminal", "COMPLETED"],
            ["CANCELLED", "", "cancelled, terminal", "CANCELLED"],
            ["FAILED", "", "failed, terminal", "FAILED"],
        ],
    },
    "ThuocTinhAnToan": {
        "widths": [48],
        "rows": [
            ["CongThucCTL"],
            ["AG(no_invalid_transition)"],
            ["EF(payment_processing)"],
            ["EF(payment_success)"],
            ["EF(refund_pending)"],
            ["EF(refunded)"],
            ["EF(terminal)"],
        ],
    },
    "LoaiTruLanNhau": {
        "widths": [34, 34],
        "rows": [
            ["MenhDe1", "MenhDe2"],
            ["payment_pending", "payment_success"],
        ],
    },
    "HamSinhMa": {
        "widths": [26, 12, 20, 30, 20, 24, 16, 16],
        "rows": [
            ["Ten", "NgonNgu", "Loai", "ThamSo", "KichThuocBuffer", "Loi", "GiaTriBiMat1", "GiaTriBiMat2"],
            ["register_user", "c", "buffer_copy", "input", 32, "buffer-overflow", "", ""],
            ["import_product_name", "c", "buffer_copy", "input", 32, "buffer-overflow", "", ""],
            ["remove_cart_item", "c", "alloc_release", "removed", 16, "use-after-free", "", ""],
            ["cancel_order", "c", "alloc_release", "order, order->items", 48, "memory-leak", "", ""],
            ["cleanup_payment", "c", "alloc_release", "transaction_code", 32, "double-free", "", ""],
        ],
    },
    "KiemTraSoHoc": {
        "widths": [32, 42, 92, 46],
        "rows": [
            ["Ten", "BienSoNguyen", "DieuKien", "BieuThucTran"],
            ["order_item_subtotal_cents", "quantity, unit_price_cents", "1 <= quantity <= 10000 and 1 <= unit_price_cents <= 99999999 and quantity * unit_price_cents > 2147483647", "quantity * unit_price_cents"],
            ["two_payment_total_cents", "payment1_cents, payment2_cents", "payment1_cents >= 0 and payment2_cents >= 0 and payment1_cents + payment2_cents > 2147483647", "payment1_cents + payment2_cents"],
        ],
    },
    "TestCase": {
        "widths": [12, 22, 38, 48, 68, 64, 14],
        "rows": [
            ["ID", "YeuCauLienQuan", "TieuDe", "DieuKienTienQuyet", "CacBuoc", "KetQuaMongDoi", "MucUuTien"],
            ["TC-1", "REQ-1", "Từ chối truy cập API được bảo vệ khi không có JWT", "Ứng dụng chạy, request không có Authorization header", "Gọi GET /api/orders/mine\nGọi GET /api/cart/user/1", "HTTP 401 cho cả hai request; không trả dữ liệu nghiệp vụ", "HIGH"],
            ["TC-2", "REQ-2", "Chặn truy cập chéo người dùng", "Có JWT user A và Order/Cart thuộc user B", "Dùng JWT A đọc order B\nDùng JWT A sửa hoặc xóa cart item của B", "HTTP 403; Order/Cart của B không thay đổi", "HIGH"],
            ["TC-3", "REQ-3, REQ-4", "Không chấp nhận giá do client sửa", "Product giá 25, stock 10", "Tạo Order quantity=2, UnitPrice=1, TotalAmount=2", "ArgumentException/HTTP 400; không tạo Order và stock vẫn là 10", "HIGH"],
            ["TC-4", "REQ-3, REQ-5", "Tạo đơn hợp lệ bằng giá server", "Product giá 25, stock 10", "Tạo Order quantity=2, UnitPrice=25, TotalAmount=50", "Order ở PAYMENT_PENDING, TotalAmount=50, UnitPrice=25, stock còn 8", "HIGH"],
            ["TC-5", "REQ-5, REQ-6", "Không thanh toán Order đã CANCELLED", "Order CANCELLED, TotalAmount=100", "Tạo Payment Amount=100 với idempotency key mới", "Bị từ chối; không có Payment và Order vẫn CANCELLED", "HIGH"],
            ["TC-6", "REQ-6", "Payment phải bằng đúng tổng Order", "Order PAYMENT_PENDING, TotalAmount=100", "Tạo Payment Amount=99", "Bị từ chối; Order vẫn PAYMENT_PENDING", "HIGH"],
            ["TC-7", "REQ-6", "Payment thành công chuyển Order sang PAID", "Order PAYMENT_PENDING, TotalAmount=100", "Tạo Payment SUCCESS Amount=100", "Tạo đúng một Payment và Order chuyển sang PAID", "HIGH"],
            ["TC-8", "REQ-7", "Retry payment có tính idempotent", "Order PAYMENT_PENDING, request có khóa stable-client-request-id", "Gửi cùng request hai lần\nSau đó thử cùng khóa với Amount khác", "Hai lần đầu trả cùng Payment/TransactionCode và chỉ lưu một bản ghi; Amount khác bị từ chối", "HIGH"],
            ["TC-9", "REQ-5, REQ-14, NFR-6", "Đối chiếu model Order ban đầu và model recovery", "Có evidence model ban đầu và recovery model", "Đọc results/order_state_model.results.json\nChạy python3 security-analysis/verify_recovery_model.py và python3 security-analysis/audit_liveness.py", "Model Order ban đầu có P1–P4 PASS trong phạm vi cũ; recovery 15 state là phạm vi mở rộng. Raw liveness recovery=false, progress-only=true theo policy lọc cạnh; không nhập nhằng hai model.", "HIGH"],
            ["TC-10", "REQ-5", "Counterexample hồi sinh Order", "Model BUGGY có cạnh CANCELLED → PAID_AFTER_CANCELLED", "Đọc results/order_state_model_buggy.results.json\nChạy python3 security-analysis/verify_recovery_model.py để kiểm mutation tương ứng", "P1 FAIL với witness CREATED → CANCELLED → PAID_AFTER_CANCELLED; mutation phải được phát hiện, đây là kết quả âm mong đợi.", "HIGH"],
            ["TC-11", "REQ-9, REQ-10, REQ-11, REQ-12, REQ-13", "Sanitizer phát hiện đủ 5 bản C lỗi", "GCC, Make, ASan/UBSan khả dụng", "cd security-analysis/c-memory\nmake\nmake test", "Bản buggy lần lượt báo stack-buffer-overflow, heap-buffer-overflow, heap-use-after-free, memory leak và double-free", "HIGH"],
            ["TC-12", "REQ-9, REQ-10, REQ-11, REQ-12, REQ-13", "Bản FIXED sạch sanitizer và đúng biên chuỗi", "Đã build cả *_fixed", "Chạy cùng kịch bản lỗi\nThử Product/User với độ dài 0, 1, 31, 32, 128 byte", "Bản FIXED không có báo cáo sanitizer; nhận tối đa 31 byte và từ chối từ 32 byte; toàn bộ 22/22 case PASS", "HIGH"],
            ["TC-13", "REQ-4, NFR-2", "Unit/coverage biên InputValidator", "InputValidatorTests và coverage evidence", "Kiểm null/rỗng, amount, quantity, price, mismatch, nhiều item và giá trị biên\nĐọc results/coverage_summary.json", "Snapshot 14/10/2026: InputValidator branch 97,22%, MoneyAmounts 100%; threshold 90% PASS. Đây là unit/coverage; random fuzz được ghi riêng tại TC-26.", "MEDIUM"],
            ["TC-14", "REQ-8", "Băm mật khẩu và nâng cấp hash cũ", "Có user mới và user chứa SHA-256 legacy", "Đăng ký user mới\nĐăng nhập user legacy đúng mật khẩu\nĐăng nhập với mật khẩu sai", "Hash mới dùng PasswordHasher; hash legacy được thay bằng hash mới sau lần đăng nhập đúng; mật khẩu sai bị từ chối", "HIGH"],
            ["TC-15", "NFR-1", "Rollback khi tạo Order thất bại", "PostgreSQL test database, stock ban đầu đã biết", "Gây lỗi sau khi kiểm tra giá/tồn kho nhưng trước commit\nĐọc lại Order và Product", "Không có Order/OrderItem dở dang; stock không bị giảm một phần", "HIGH"],
        ],
    },
}

# Keep the generated workbook aligned with the executable recovery model.  The
# base rows above document the original project; this overlay adds the formal
# recovery scope without duplicating the state graph by hand.
RECOVERY_MODEL = ROOT / "security-analysis" / "order_payment_recovery_model.json"


def apply_recovery_model() -> None:
    import json

    model = json.loads(RECOVERY_MODEL.read_text(encoding="utf-8"))
    SHEETS["ThongTin"]["rows"][2][1] = (
        "Group 14 — Xây dựng và kiểm chứng an toàn hệ thống xử lý đơn hàng và thanh toán trực tuyến. "
        "Phạm vi đã làm: JWT/ownership/Admin, giá server, idempotency Order/Payment, "
        "consistency Order–Payment–Inventory, recovery charge/refund qua kill process và "
        "PostgreSQL stop/start, audit nguyên tử, admission, health/status, backup/restore, "
        "checkout/browser, model/SMT/fuzz và 5 module C minh họa an toàn bộ nhớ."
    )
    SHEETS["ThongTin"]["rows"].extend([
        ["updated_date", "2026-10-15 (Asia/Bangkok)"],
        ["evidence_baseline", "Evidence hiện lưu ngày 14/10/2026: 148/148 unit + 35/35 integration PASS, 0 SKIP (results/application_tests.results.json). Đây là kết quả đã lưu, không phải lần chạy mới khi cập nhật Excel."],
        ["coverage_baseline", "InputValidator branch 97,22%; MoneyAmounts 100%; fuzz target branch 96,66%. Nguồn: security-analysis/results/coverage_summary.json và application_fuzzing.results.json; chỉ áp dụng target được đo."],
        ["model_scope", "MoHinhTrangThai là recovery model trừu tượng 15 state, 37 event, 7 contract, không phải enum OrderStatus. PAYMENT_PROCESSING/PAYMENT_RETRY/PAYMENT_PROCESSING_FINAL kết hợp Order và Payment; CONFIRMATION_RETRY thuộc mở rộng thiết kế, chưa có scheduler/counter runtime. Xem IMPLEMENTATION_CONTRACTS.md."],
        ["liveness_scope", "P10 progress-only lọc cạnh stutter; audit_liveness.py xác nhận raw AG AF terminal=false, progress-only=true. Đây không phải thuật toán fair-CTL đầy đủ; kết thúc còn phụ thuộc provider và hành động confirm/complete."],
        ["generator_scope", "HamSinhMa mô tả 5 module C BUGGY/FIXED học tập; không phải lỗi bộ nhớ của C# managed. KiemTraSoHoc là các bài toán số nguyên/cents, không phải phép toán Int32 trực tiếp trong production; app dùng decimal/guard, SMT dùng Z3 ngoài runtime."],
        ["verification_sources", "Nguồn trong security-analysis/: PHAM_VI_KIEM_CHUNG.md; IMPLEMENTATION_CONTRACTS.md; OPERATIONS.md; report/BAO_CAO.md; results/*.json và results/traceability_matrix.csv. Các tham chiếu rút gọn results/... trong workbook tính từ security-analysis/; lệnh chạy từ gốc repository."],
        ["limitations", "Gateway là DurableFake local, chưa provider thu tiền thật. Chưa chứng minh mọi interleaving/crash window, partition, failover, power loss hoặc SLA production. Evidence/hash lúc đóng gói không tự chứng minh source của lần chạy lịch sử."],
        ["future_work", "Ngoài nghiệm thu: tách DB migration/runtime role, retention job, webhook thật, shared Redis limiter, gateway thật, Kubernetes. Confirmation retry tự động còn là thiết kế; load chưa crossover/lặp để suy thống kê."],
    ])
    requirements = {row[0]: row for row in SHEETS["YeuCauChucNang"]["rows"][1:]}
    requirements["REQ-3"][1] = "Tạo Order lấy UnitPrice/TotalAmount từ catalog server; kiểm sản phẩm tồn tại/active và cộng dồn quantity theo ProductId trước kiểm stock. Payload giá/tổng không hợp lệ bị từ chối; client UnitPrice không là nguồn giá có thẩm quyền."
    requirements["REQ-4"][1] = "Price/TotalAmount/Payment.Amount phải là bội 0,01 trong giới hạn; fractional cent bị từ chối, không round âm thầm. InputValidator/OrderService chấp nhận lệch tổng tối đa 0,01; Order lưu tổng giá server, Payment phải bằng chính xác tổng đã lưu."
    requirements["REQ-5"][1] = "Order đi CREATED → VALIDATED → PAYMENT_PENDING → PAID → CONFIRMED → COMPLETED; cho phép cancel trước charge, FAILED khi decline, PAID/CONFIRMED → REFUND_PENDING → REFUNDED. Guard cấm hồi sinh terminal; model có các state trừu tượng bổ sung, không đồng nhất với enum runtime."
    SHEETS["YeuCauChucNang"]["rows"].extend([
        ["REQ-15", "Mỗi trạng thái Order-Payment phải đặc tả sự cố, cách phát hiện, biện pháp xử lý và trạng thái tiếp theo; không được có sự cố không có đường phục hồi hoặc kết thúc.", "safety", "HIGH"],
        ["REQ-16", "PaymentService giới hạn charge tối đa 2 attempts cho cùng logical payment/key. Hết budget mà UNKNOWN vẫn giữ reservation và chỉ lookup/reconcile; không suy thành FAILED. Bounded confirmation retry chỉ có trong model, chưa hiện thực scheduler/counter tự động.", "safety", "HIGH"],
        ["REQ-17", "Charge đã xảy ra nhưng finalize PAID lỗi phải được lookup bằng cùng key và hoàn tất SUCCESS/PAID, không charge mù hoặc đổi FAILED. Khi yêu cầu refund cho Order PAID/CONFIRMED, dùng REFUND_PENDING → REFUNDED, giữ dấu vết charge và release kho một lần.", "safety", "HIGH"],
        ["REQ-18", "Payment và cancellation trên cùng Order phải được serialize; không được đồng thời sở hữu critical resource hoặc tạo hai Payment SUCCESS.", "safety", "HIGH"],
        ["REQ-19", "PROCESSING/UNKNOWN phải được worker reconciliation hỏi trạng thái charge cũ; REFUND_PENDING hỏi trạng thái refund cũ. Lookup không tăng AttemptCount, không tự phát charge/refund; UNKNOWN giữ reservation và chặn cancel.", "safety", "HIGH"],
        ["REQ-20", "Với DurableFake journal đã publish trước local finalize, API kill/restart hoặc PostgreSQL stop/start phải phục hồi đúng charge/refund. Reconnect giữ nguyên API process/pool; giữ key, transaction reference và journal, không duplicate operation hoặc release kho.", "safety", "HIGH"],
        ["REQ-21", "POST /api/orders hỗ trợ Idempotency-Key tùy chọn, namespace theo customer từ JWT. Cùng key/payload trả 201 và receipt gốc kể cả sau cancel/catalog đổi; payload khác trả 409. Unique key hash, reserve, order và receipt commit cùng transaction; client không key vẫn có thể tạo Order mới.", "functional", "HIGH"],
        ["REQ-22", "Mutation Orders/Payments phải có COMMIT audit cùng transaction bằng DB trigger; audit lỗi phải rollback nghiệp vụ. BEGIN/RESULT HTTP dùng correlation server và allowlist không có secrets; BEGIN lỗi trả 503 trước action, RESULT lỗi sau action trả 503 với requiresReconciliation=true.", "safety", "HIGH"],
        ["REQ-23", "Admission mặc định body ≤65536 byte, tối đa 100 order items, auth 10 request/60s theo connection IP, API 120 request/60s theo subject hoặc IP. Quá quota trả 429/Retry-After; body quá lớn trả 413 cả khi thiếu Content-Length, trước JSON binding.", "safety", "HIGH"],
        ["REQ-24", "Checkout giữ attempt/key/payload theo tài khoản trong sessionStorage; chặn double submit và thay payload khi outcome chưa rõ. Create-order retry tối đa 4 HTTP attempts/8s, chỉ cho 409 ORDER_CONCURRENT_WRITE với retryable=true; 503/network giữ key để đối soát.", "functional", "HIGH"],
        ["REQ-25", "Health live/ready trả thông tin tối thiểu; ready phản ánh DB outage. /api/operations/status chỉ Admin, no-store và không lộ secret; PendingSince giữ tuổi unresolved qua status lookup, có chỉ số recovery/audit failure và cảnh báo pending quá lâu.", "functional", "HIGH"],
        ["REQ-26", "Backup/restore trên môi trường tạm phải giữ database, receipt, audit triggers, identity sequence và gateway ledger nhất quán; worker phục hồi pending charge sau restore không charge lại. Không dùng phép đo drill làm cam kết RPO/RTO.", "functional", "HIGH"],
        ["REQ-27", "Shop chạy được register/login → cart → order → payment giả lập → history qua HTTP/PostgreSQL thật; reload/logout đúng, không thu/gửi card fields. Browser smoke và browser mocked-fetch có phạm vi bằng chứng riêng.", "functional", "MEDIUM"],
    ])
    SHEETS["YeuCauPhiChucNang"]["rows"].append([
        "NFR-6",
        "Pipeline kiểm chứng phải duyệt toàn bộ model hữu hạn, lưu counterexample cho model lỗi và ghi rõ bound, fairness, assumptions và giới hạn kết luận.",
        "HIGH",
    ])
    SHEETS["YeuCauPhiChucNang"]["rows"].extend([
        ["NFR-7", "Timeout wrapper charge/refund/status mặc định 5000ms, startup validate 50..60000ms. Caller không chờ vô hạn; late result không tự ghi state. Adapter không hợp tác chưa có cơ chế cưỡng chế dừng, backpressure vẫn là giới hạn.", "HIGH"],
        ["NFR-8", "AuditEvents append-only chặn UPDATE/DELETE/TRUNCATE bằng PostgreSQL trigger; COMMIT audit chỉ bảo vệ Orders/Payments. Không chống DBA đổi schema/disable trigger; chưa có retention job hoặc chữ ký chống sửa log.", "HIGH"],
        ["NFR-9", "Kết quả nghiệm thu phải ghi total/pass/fail/skip, thời điểm, source/artifact hash khi có và giới hạn. Demo filtered tests không ghi đè summary toàn suite; snapshot evidence không là lần chạy mới.", "HIGH"],
        ["NFR-10", "Load/race phải kiểm no duplicate, conservation stock, bounded attempts và COMMIT count. Báo raw samples/p95/p99/status; workload hai API process hữu hạn và limiter local không chứng minh shared quota hoặc SLA production.", "MEDIUM"],
    ])
    SHEETS["RangBuoc"]["rows"].extend([
        ["CON-7", "Runtime MaxChargeAttempts=2. Model PAYMENT_PROCESSING_FINAL vẫn có cạnh final_payment_unknown/final_charged_but_commit_failed → PAYMENT_RETRY để giữ UNKNOWN; graph không mã hóa đầy đủ counter runtime và không là proof bound mọi đường thực thi.", "HIGH"],
        ["CON-8", "Weak fairness là assumption được khai báo trong model. Checker hiện bỏ cạnh metadata stutter để kiểm progress-only, kể cả vài cạnh đổi state; raw AG AF terminal=false. Không diễn giải PASS thành fair-CTL hoặc bảo đảm provider cuối cùng trả kết quả.", "HIGH"],
        ["CON-9", "Refund có idempotency key riêng, bind charge transaction reference; replay/lookup không refund lần hai. Gateway callback được xác thực chỉ là assumption của model, chưa có webhook adapter thật.", "HIGH"],
        ["CON-10", "Order Idempotency-Key: một header value, 1..128 ký tự visible ASCII; DB lưu SHA-256 key/payload và original receipt, unique(CustomerId, CreationKeyHash). Khác Payment.IdempotencyKey trong JSON; không suy hai namespace là một.", "HIGH"],
        ["CON-11", "PostgreSQL CHECK chặn Stock âm và Quantity không dương; bound Quantity≤10000 thuộc application guard. Concurrency token/unique index/Serializable hỗ trợ chống stale write và oversell. Application cents guard không ngăn raw SQL tự round numeric(18,2).", "HIGH"],
        ["CON-12", "DurableFake mặc định là journal local ngoài wwwroot, file name SHA-256 và publish temp/flush/rename; chỉ mô phỏng provider bền qua process restart. MemoryFake dùng cho một số fixture, không có độ bền qua restart.", "HIGH"],
        ["CON-13", "Runtime retry create order chỉ allowlist DbUpdateConcurrencyException, 40001/40P01 hoặc 23505 đúng index creation key; retryable=true cần key hợp lệ. Generic 23505/23514, business conflict và audit failure không tự retry.", "HIGH"],
        ["CON-14", "Local PostgreSQL stop/start, OS process kill, backup/restore và browser được kiểm trên tài nguyên test tạm. DB RBAC tách role, retention, webhook/gateway thật, distributed limiter và Kubernetes nằm ngoài nghiệm thu.", "MEDIUM"],
    ])

    state_rows = [["TrangThai", "KhoiTao", "Nhan", "ChuyenDenCacTrangThai"]]
    for state in model["states"]:
        targets = []
        for transition in state["normal"] + state["incidents"]:
            if transition["to"] not in targets:
                targets.append(transition["to"])
        labels = [*state["labels"], "no_invalid_transition", "no_resource_conflict"]
        state_rows.append([
            state["id"],
            "x" if state["id"] == "CREATED" else "",
            ", ".join(labels),
            ", ".join(targets),
        ])
    SHEETS["MoHinhTrangThai"]["rows"] = state_rows
    SHEETS["MoHinhTrangThai"]["widths"] = [28, 12, 68, 66]

    SHEETS["ThuocTinhAnToan"]["rows"] = [
        ["CongThucCTL"],
            ["AG(no_invalid_transition)"],
            ["AG(no_resource_conflict)"],
        ["EF(payment_processing)"],
        ["EF(payment_success)"],
        ["EF(refund_pending)"],
        ["EF(refunded)"],
        ["EF(terminal)"],
    ]
    SHEETS["ThuocTinhAnToan"]["widths"] = [75]
    SHEETS["LoaiTruLanNhau"]["rows"] = [
        ["MenhDe1", "MenhDe2"],
        ["payment_pending", "payment_success"],
    ]
    SHEETS["HamSinhMa"]["rows"] = [
        ["Ten", "NgonNgu", "Loai", "ThamSo", "KichThuocBuffer", "Loi", "GiaTriBiMat1", "GiaTriBiMat2"],
        ["register_user", "c", "buffer_copy", "input", 32, "buffer-overflow", 31, 32],
        ["import_product_name", "c", "buffer_copy", "input", 32, "buffer-overflow", 31, 32],
        ["remove_cart_item", "c", "alloc_release", "removed", 16, "use-after-free", 101, 1],
        ["cancel_order", "c", "alloc_release", "order, order->items", 48, "memory-leak", 3, 5],
        ["cleanup_payment", "c", "alloc_release", "transaction_code", 32, "double-free", 1, 2],
    ]
    SHEETS["KiemTraSoHoc"]["rows"] = [
        ["Ten", "BienSoNguyen", "DieuKien", "BieuThucTran"],
        ["order_item_subtotal_cents", "quantity, unit_price_cents", "1 <= quantity <= 10000 and 1 <= unit_price_cents <= 99999999 and quantity * unit_price_cents > 2147483647", "quantity * unit_price_cents"],
        ["order_total_cents", "subtotal1, subtotal2", "subtotal1 >= 0 and subtotal2 >= 0 and subtotal1 + subtotal2 > 2147483647", "subtotal1 + subtotal2"],
        ["two_payment_total_cents", "payment1_cents, payment2_cents", "payment1_cents >= 0 and payment2_cents >= 0 and payment1_cents + payment2_cents > 2147483647", "payment1_cents + payment2_cents"],
        ["inventory_after_reserve", "stock, quantity", "0 <= stock <= 2147483647 and 1 <= quantity <= 10000 and stock - quantity < 0", "stock - quantity"],
        ["retry_counter_increment", "retry_count, one", "0 <= retry_count <= 2147483647 and one == 1 and retry_count + one > 2147483647", "retry_count + one"],
        ["refund_total_cents", "paid_cents, refunded_cents", "paid_cents >= 0 and refunded_cents >= 0 and paid_cents + refunded_cents > 2147483647", "paid_cents + refunded_cents"],
    ]
    SHEETS["TestCase"]["rows"].extend([
        ["TC-16", "REQ-15, REQ-16, NFR-6", "Kiểm chứng recovery model hữu hạn", "order_payment_recovery_model.json hợp lệ", "Chạy python3 security-analysis/verify_recovery_model.py\nĐọc results/order_payment_recovery_model.results.json", "7 contract, 15 state, 37 event; P1–P15 PASS theo semantics của checker, mọi state reachable và 6/6 negative mutations được phát hiện. P10 chỉ progress-only; CONFIRMATION_RETRY thuộc thiết kế.", "HIGH"],
        ["TC-17", "REQ-5, REQ-15", "Phản ví dụ CANCELLED bị thanh toán", "Mutation BUG-CANCELLED-PAID được bật", "Thêm cạnh CANCELLED → PAID_AFTER_CANCELLED\nChạy lại toàn bộ thuộc tính", "P1 và P4 FAIL; witness đi từ CREATED tới CANCELLED rồi PAID_AFTER_CANCELLED", "HIGH"],
        ["TC-18", "REQ-6, REQ-15", "Phản ví dụ payment sai amount", "Mutation BUG-WRONG-AMOUNT được bật", "Bỏ amount guard tại PAYMENT_PROCESSING\nChuyển tới PAID_WRONG_AMOUNT", "P1 FAIL và trả witness tới state amount_mismatch", "HIGH"],
        ["TC-19", "REQ-7, REQ-18", "Phản ví dụ hai payment SUCCESS", "Mutation BUG-DUPLICATE-SUCCESS được bật", "Bỏ idempotency guard tại PAID\nTạo state PAID_TWICE", "P1 FAIL và trả witness tới state two_success_payments", "HIGH"],
        ["TC-20", "REQ-18, NFR-1", "Phản ví dụ payment-cancel conflict", "Mutation BUG-RESOURCE-CONFLICT được bật", "Cho payment và cancel cùng sở hữu resource\nChạy kiểm chứng", "P1 và P7 FAIL với witness tới PAY_CANCEL_CONFLICT", "HIGH"],
        ["TC-21", "REQ-16, REQ-17, REQ-19", "Retry hết budget vẫn giữ UNKNOWN an toàn", "PaymentServiceTests; provider trả UNKNOWN", "Retry đến AttemptCount=2 rồi retry/reconcile tiếp; thử cancel\nĐọc source PaymentService và results/application_tests.results.json", "Không charge attempt thứ ba; UNKNOWN và reservation được giữ, cancel bị chặn; definitive result mới cho finalize. Confirmation retry tự động chưa hiện thực.", "HIGH"],
        ["TC-22", "REQ-4, REQ-6", "Kiểm chứng SMT cho tổng tiền và thanh toán", "Z3 có trên PATH", "Chạy python security-analysis/verify_payment_smt.py", "7/7 ca SAT/UNSAT đúng kỳ vọng; có counterexample tràn Int32 và bằng chứng giới hạn tổng tiền", "HIGH"],
        ["TC-23", "REQ-1, REQ-2, REQ-3, REQ-7, REQ-18", "Kiểm tra threat model Chương 1", "threat_model.json hợp lệ", "Chạy python security-analysis/verify_threat_model.py", "ID và tham chiếu hợp lệ; đủ CIA, 6/6 STRIDE, risk score và liên kết evidence/property", "HIGH"],
        ["TC-24", "REQ-9, REQ-10, REQ-11, REQ-12, REQ-13, NFR-5", "Static analysis BUGGY/FIXED và alignment", "GCC và cppcheck có trên PATH", "Chạy make -C security-analysis/c-memory static-check", "Scanner bắt đủ 5 lỗi BUGGY, FIXED sạch theo rule; lưu finding cppcheck và layout thực tế", "HIGH"],
        ["TC-25", "REQ-1, REQ-2, REQ-7, REQ-18, NFR-1", "Integration HTTP với PostgreSQL thật", "Docker hoặc PostgreSQL binaries cục bộ khả dụng", "Chạy dotnet test cho OrderPaymentMicroservice.IntegrationTests", "Tất cả test PASS, không SKIP; số lượng và verdict lấy từ application_tests.results.json. Kiểm tra quyền, idempotency, payment-cancel, validation và migration trên PostgreSQL", "HIGH"],
        ["TC-26", "REQ-4, REQ-5, REQ-6, NFR-2", "Fuzzing xác định validator và state machine", "Corpus; seed=20261009; timeout=120s", "Chạy python3 security-analysis/run_fuzzing.py\nĐọc results/application_fuzzing.results.json và application-fuzzing-coverage.cobertura.xml", "Snapshot 14/10/2026: 3/3 campaign PASS, 25.000 iteration mỗi random campaign, branch target 96,66%, không crash/timeout. Target: InputValidator, PaymentValidator, OrderStateMachine, PaymentStateMachine, MoneyAmounts; không là coverage toàn app.", "HIGH"],
        ["TC-27", "REQ-5, REQ-14, REQ-16, REQ-17, NFR-6", "TLC kiểm chứng đặc tả TLA+ trong bound", "Có tla2tools.jar; đây là model TLA+ riêng", "Chạy python3 security-analysis/run_tlc.py --jar /duong-dan/tla2tools.jar\nĐọc results/tlc/OrderPayment.tlc.results.json", "Evidence TLC lịch sử: 35 state sinh, 20 phân biệt, độ sâu 9, không lỗi trong model/bound/assumptions đã khai báo. Không đổi nhãn thành lượt mới; không thay kết quả raw liveness của recovery graph hoặc proof runtime.", "HIGH"],
        ["TC-28", "REQ-5, REQ-14, NFR-6", "Tương thích bộ công cụ ATPM của giảng viên", "Workbook và JSON pipeline đã sinh", "Chạy python security-analysis/verify_teacher_spec.py", "Checker độc lập: schema đúng 10 sheet; Excel/JSON đồng nghĩa; Kripke total và reachable; 7/7 CTL PASS. Không thay thế lần chạy công cụ Windows của giảng viên.", "HIGH"],
        ["TC-29", "REQ-14, CON-8, NFR-6", "Audit riêng semantics liveness", "Recovery graph đầy đủ có cạnh stutter", "Chạy python3 security-analysis/audit_liveness.py\nĐọc results/liveness_audit.results.json và counterexample_lasso", "Raw AG AF terminal=false và có lasso; progress-only=true sau lọc cạnh. Verdict audit PASS là phát hiện đúng khác biệt này, không là proof fair-CTL hoặc runtime luôn kết thúc.", "HIGH"],
        ["TC-30", "REQ-17, REQ-20", "Kill API sau charge journal, trước finalize", "ProcessRestartTests; PostgreSQL/ledger tạm", "Kill OS process tại marker sau journal publish; khởi động process mới cùng DB/ledger\nĐọc results/process_restart.results.json", "Worker mới tự phục hồi SUCCESS/PAID, một payment/journal charge, AttemptCount=1, journal không đổi và kho vẫn reserved. Chỉ bao phủ crash window đã chọn.", "HIGH"],
        ["TC-31", "REQ-17, REQ-20, CON-9", "Kill API sau refund journal, trước finalize", "RefundRestartTests; charge thành công, refund pending", "Kill process sau refund publish; restart cùng DB/ledger và đợi worker\nĐọc results/refund_restart.results.json", "REFUNDED, charge reference giữ nguyên, journal refund không đổi, PendingSince xóa và stock release đúng một lần; replay không tạo refund mới.", "HIGH"],
        ["TC-32", "REQ-19, REQ-20, REQ-25", "Charge recovery sau PostgreSQL stop/start", "PostgresReconnectTests; local PostgreSQL binaries; DurableFake", "Chạy python3 security-analysis/run_recovery_demo.py\nĐọc results/postgres_reconnect_charge.results.json", "Cùng API process/pool, ready 200→503→200, live=200 khi DB tắt; finalize=503, worker scan failure rồi recovery. Charge=1, payment=1, AttemptCount=1, SUCCESS/PAID/RESERVED; reserve=1, release=0, stock 50→49, journal giữ hash.", "HIGH"],
        ["TC-33", "REQ-17, REQ-20, REQ-25", "Refund recovery sau PostgreSQL stop/start", "PostgresReconnectTests; charge thành công; local PostgreSQL", "Chạy python3 security-analysis/run_recovery_demo.py\nĐọc results/postgres_reconnect_refund.results.json", "Cùng API process/pool, ready 200→503→200; charge=1/refund=1, giữ charge reference và key/hash journal. Payment/Order REFUNDED, inventory RELEASED, release=1, stock trở về 50; replay không release lại.", "HIGH"],
        ["TC-34", "REQ-19, NFR-7", "Timeout gateway và late result", "GatewayTimeoutTests với adapter không hợp tác cancellation", "Kiểm charge/refund/charge-status/refund-status, late success, worker progress và shutdown\nĐọc results/application_tests.results.json", "Caller waiting có bound; timeout charge giữ UNKNOWN/reservation, timeout refund giữ REFUND_PENDING; late result không tự finalize. Worker tiếp tục candidate khác và shutdown không invent outcome.", "HIGH"],
        ["TC-35", "REQ-21, CON-10", "Replay receipt qua hai host và bind payload/customer", "AtomicAuditIdempotencyTests, hai host dùng chung PostgreSQL", "Tạo Order có key, replay qua host khác sau cancel/catalog đổi; đổi payload hoặc JWT customer; thử key sai/đa trị", "Cùng customer/key/payload trả 201, cùng ID/Location/original receipt; payload đổi trả 409, customer khác có namespace riêng. Key sai trả 400; đọc trạng thái mới qua GET, không dùng receipt cũ làm live state.", "HIGH"],
        ["TC-36", "REQ-3, REQ-18, REQ-21, CON-11", "Concurrent same-key và không oversell", "AtomicAuditIdempotencyTests; PostgreSQL thật", "Gửi same-key đồng thời; retry cùng payload/key sau conflict\nCho hai host tranh 10 đơn vị tồn kho; kiểm duplicate ProductId lines", "Chỉ một reserve/Order cho cùng key; tổng stock được bảo toàn, không âm hoặc oversell. Conflict có thể trả 409; duplicate lines phải được cộng dồn trước stock guard.", "HIGH"],
        ["TC-37", "REQ-22, NFR-1, NFR-8", "COMMIT audit nguyên tử và direct SQL rollback", "AtomicAuditIdempotencyTests; DB audit triggers", "Làm audit INSERT thất bại khi tạo Order; kiểm stock/Order rollback\nThử direct SQL mutation rồi rollback/commit", "Audit COMMIT lỗi rollback cả nghiệp vụ; rollback nghiệp vụ bỏ audit tương ứng; commit lưu audit resource ID thật. Request metadata không bị lẫn qua pooled connections, SQL không có request actor ghi SYSTEM.", "HIGH"],
        ["TC-38", "REQ-22, NFR-3, NFR-8", "BEGIN/RESULT fail và audit append-only", "AdmissionAuditTests/AuditMiddlewareTests", "Gây lỗi BEGIN, lỗi RESULT sau commit; kiểm correlation/redaction\nThử UPDATE/DELETE/TRUNCATE AuditEvents", "BEGIN lỗi: 503 trước business action; RESULT lỗi: 503 requiresReconciliation=true dù business có thể đã commit. Trigger chặn sửa/xóa audit; không có password/JWT/raw key/body. DBA vẫn nằm ngoài bảo đảm append-only.", "HIGH"],
        ["TC-39", "REQ-23", "Rate/body/item admission boundaries", "AdmissionAuditTests, BodyLimitTests, AdmissionBoundaryTests", "Kiểm auth/API bucket riêng, verified-user buckets, 429/Retry-After\nThử body có/không Content-Length, streaming quá cap và vượt item count", "Quota vượt trả 429 không queue; body vượt trả 413 trước JSON binding, item vượt bị từ chối trước persistence. 429/413 trước audit không ghi BEGIN/RESULT; limiter chỉ local mỗi process.", "HIGH"],
        ["TC-40", "REQ-24, CON-13", "Checkout retry/key/payload qua mocked browser", "Chrome/Chromium; browser/checkout_retry.template.html; OrderRetryContractTests", "Chạy python3 security-analysis/verify_checkout.py\nKiểm metadata 409, wrapped SQLSTATE, stable body/key, jitter, abort và double submit", "Tối đa 4 attempts/8s; chỉ ORDER_CONCURRENT_WRITE + retryable=true mới auto retry với backoff. 503/network/401/403/429 hoặc hết budget giữ key; không retry generic DB/business errors. Evidence: results/checkout_retry.results.json; mocked fetch không là live API.", "HIGH"],
        ["TC-41", "REQ-25", "Health, quyền operations và pending age", "OperationsTests; PendingAgeTests", "Gọi /health/live, /health/ready, /api/operations/status với anonymous/user/Admin\nReconcile UNKNOWN nhiều lần, tiến clock quan sát và gây audit append failure", "Health tối thiểu, status chỉ Admin/no-store/redacted; UpdatedAt refresh không reset PendingSince, stuck vẫn được phát hiện; audit failure có counter/alert. Clock thử chỉ phục vụ quan sát, không thay outcome provider.", "HIGH"],
        ["TC-42", "REQ-26", "Backup/restore DB cùng gateway ledger", "OperationalDrillTests; local PostgreSQL; tài nguyên tạm", "Restore snapshot sang cluster riêng rồi khởi động API/worker\nĐọc results/backup_restore.results.json", "Snapshot đã lưu PASS: hash dữ liệu 9 bảng khớp, 4 audit triggers hoạt động, sequence cấp ID mới, receipt replay được, pending charge recovered với charge=1/AttemptCount=1; DB nguồn không đổi. Timing không là SLA/RPO/RTO.", "HIGH"],
        ["TC-43", "REQ-24, NFR-10", "Đo bounded retry qua hai API process", "OrderRetryLoadTests; 8 users, 4 products, concurrency=8", "So sánh one-shot và retry policy: 96 logical requests/scenario\nĐọc raw samples/backoff trong results/order_retry_load.results.json", "PASS các invariant bounded attempts, no duplicate, conservation stock, COMMIT count; driver C# mô phỏng policy, JS được kiểm riêng. Baseline chạy trước, chưa crossover/repetitions/statistical confidence; không suy SLA.", "MEDIUM"],
        ["TC-44", "REQ-27", "Live browser với normal application và PostgreSQL thật", "ApplicationSmokeTests; Chrome CDP; PostgreSQL/ledger tạm", "Chạy NormalApplicationServesShopAndCompletesRealBrowserFlow\nĐọc results/application_smoke.results.json", "Register/login→cart→order→fake payment→history PASS; reload/session và logout đúng; double submit được kiểm, không gửi card fields. Đây là HTTP/DB thật, khác browser mocked-fetch retry.", "HIGH"],
        ["TC-45", "REQ-4, CON-2, CON-11", "Fractional cents và DB CHECK", "MoneyPrecisionTests; PostgreSQL HTTP regression", "Gửi 25,001/25,005 và cent hợp lệ 25,01/25,0100; thử raw SQL Stock âm/Quantity không dương\nĐọc results/application_tests.results.json", "Fractional cent bị từ chối trước persistence; cent hợp lệ round-trip, trailing zeroes được nhận. DB CHECK chặn Stock<0/Quantity≤0; bound Quantity≤10000 do app kiểm. Không suy cents guard application ngăn raw SQL tự round numeric.", "HIGH"],
        ["TC-46", "REQ-17, NFR-6", "Mutation paid thất bại không compensation", "BUG-PAID-FAILED-WITHOUT-REFUND trong recovery checker", "Chạy python3 security-analysis/verify_recovery_model.py\nĐọc negative_scenarios trong results/order_payment_recovery_model.results.json", "Phát hiện mutation chuyển paid sang FAILED bỏ refund; trả counterexample/property failure. Không dùng kết quả này để khẳng định tự động confirmation retry đã có trong source.", "HIGH"],
        ["TC-47", "REQ-19, NFR-6", "Mutation UNKNOWN trả kho sớm", "BUG-UNKNOWN-RELEASED trong recovery checker", "Chạy python3 security-analysis/verify_recovery_model.py\nĐọc negative_scenarios trong results/order_payment_recovery_model.results.json", "Phát hiện UNKNOWN bị terminal hóa hoặc release reservation trước definitive outcome; có counterexample vi phạm policy UNKNOWN an toàn.", "HIGH"],
        ["TC-48", "NFR-9", "Đối chiếu snapshot nghiệm thu toàn suite", "results/application_tests.results.json; không thay summary bằng filtered tests", "Đọc suites.unit/suites.integration và thời điểm TRX; đối chiếu report/BAO_CAO.md\nKhi chạy lại dùng lệnh toàn suite trong README.md", "Snapshot 14/10/2026: unit 148/148, integration 35/35 PASS, fail=0, skip=0. Lần cập nhật workbook không là lần chạy lại .NET; source/hash và giới hạn lịch sử được ghi riêng.", "HIGH"],
        ["TC-49", "NFR-10, REQ-23", "Load probe latency/conflict/throttle", "OperationalDrillTests; hai real API process", "Đọc raw samples/status counts/nearest-rank p95/p99 trong results/load_probe.results.json", "Load probe đã lưu PASS các oracle stock và admission; hot-product one-shot có thể 409. Successful/s và latency chỉ là quan sát workload; quota local không thành shared quota.", "MEDIUM"],
        ["TC-50", "REQ-3, CON-11", "SMT tổng quantity của các dòng cùng sản phẩm", "Z3; Security/InventoryVerification.z3", "Chạy python3 security-analysis/verify_inventory_smt.py\nĐọc results/inventory_verification.results.json", "3/3 ca SAT/UNSAT đúng kỳ vọng: baseline stock=10, q1=q2=6 có oversell; aggregated guard không cho stock âm, nhận đúng stock. Model hai dòng một sản phẩm với số nguyên toán học, không chứng minh EF/interleaving/API đầy đủ.", "HIGH"],
    ])


apply_recovery_model()


# Keep the lecturer's exact ten-sheet schema. Scope/evidence notes use the
# existing ThongTin key/value rows and TestCase fields, never extra columns.


EXPECTED_HEADERS = {
    "ThongTin": ["Truong", "GiaTri"],
    "YeuCauChucNang": ["ID", "MoTa", "DanhMuc", "MucUuTien"],
    "YeuCauPhiChucNang": ["ID", "MoTa", "MucUuTien"],
    "RangBuoc": ["ID", "MoTa", "MucUuTien"],
    "MoHinhTrangThai": ["TrangThai", "KhoiTao", "Nhan", "ChuyenDenCacTrangThai"],
    "ThuocTinhAnToan": ["CongThucCTL"],
    "LoaiTruLanNhau": ["MenhDe1", "MenhDe2"],
    "HamSinhMa": ["Ten", "NgonNgu", "Loai", "ThamSo", "KichThuocBuffer", "Loi", "GiaTriBiMat1", "GiaTriBiMat2"],
    "KiemTraSoHoc": ["Ten", "BienSoNguyen", "DieuKien", "BieuThucTran"],
    "TestCase": ["ID", "YeuCauLienQuan", "TieuDe", "DieuKienTienQuyet", "CacBuoc", "KetQuaMongDoi", "MucUuTien"],
}


def validate_teacher_schema() -> None:
    if list(SHEETS) != list(EXPECTED_HEADERS):
        raise ValueError("Workbook must contain exactly the ten lecturer-defined sheets in template order")
    for name, header in EXPECTED_HEADERS.items():
        if SHEETS[name]["rows"][0] != header:
            raise ValueError(f"{name} header must be exactly {header}")
        if len(SHEETS[name]["widths"]) != len(header):
            raise ValueError(f"{name} has a column width/header mismatch")

    requirements = SHEETS["YeuCauChucNang"]["rows"][1:]
    allowed_categories = {"safety", "liveness", "memory", "functional"}
    allowed_priorities = {"HIGH", "MEDIUM", "LOW"}
    requirement_ids = {row[0] for row in requirements}
    requirement_ids.update(row[0] for row in SHEETS["YeuCauPhiChucNang"]["rows"][1:])
    requirement_ids.update(row[0] for row in SHEETS["RangBuoc"]["rows"][1:])
    if any(row[2] not in allowed_categories for row in requirements):
        raise ValueError("Requirement category is not supported by Spec Verification Lab")
    for sheet_name in ("YeuCauChucNang", "YeuCauPhiChucNang", "RangBuoc"):
        if any(row[-1] not in allowed_priorities for row in SHEETS[sheet_name]["rows"][1:]):
            raise ValueError(f"Unsupported priority in {sheet_name}")
    for row in SHEETS["TestCase"]["rows"][1:]:
        references = {item.strip() for item in row[1].split(",")}
        missing = references - requirement_ids
        if missing:
            raise ValueError(f"{row[0]} references unknown requirements: {sorted(missing)}")

    if len(SHEETS["LoaiTruLanNhau"]["rows"]) != 2:
        raise ValueError("Spec Verification Lab accepts at most one mutual-exclusion pair")

    state_rows = SHEETS["MoHinhTrangThai"]["rows"][1:]
    states = {row[0] for row in state_rows}
    if len(states) != len(state_rows):
        raise ValueError("State names must be unique")
    initial = [row[0] for row in state_rows if str(row[1]).strip().lower() == "x"]
    if not initial:
        raise ValueError("At least one initial state is required")
    labels = set()
    for state, _, state_labels, targets in state_rows:
        labels.update(value.strip() for value in str(state_labels).split(",") if value.strip())
        parsed_targets = [value.strip() for value in str(targets).split(",") if value.strip()]
        if not parsed_targets:
            raise ValueError(f"{state} violates the total transition relation")
        unknown = set(parsed_targets) - states
        if unknown:
            raise ValueError(f"{state} transitions to unknown states: {sorted(unknown)}")

    import re
    ctl_pattern = re.compile(r"^(AG|EF|AF|EG|AX|EX)\((!?)([A-Za-z_][A-Za-z0-9_]*)\)$")
    for formula, in SHEETS["ThuocTinhAnToan"]["rows"][1:]:
        match = ctl_pattern.fullmatch(formula)
        if match is None or (match.group(2) and match.group(1) != "AG"):
            raise ValueError(f"Unsupported CTL formula for Spec Verification Lab: {formula}")
        if match.group(3) not in labels:
            raise ValueError(f"CTL formula references an undeclared label: {formula}")

    mutex = SHEETS["LoaiTruLanNhau"]["rows"][1]
    if any(atom not in labels for atom in mutex):
        raise ValueError("Mutual-exclusion atoms must both occur in the state model")


def build_pipeline_json() -> dict[str, object]:
    rows = SHEETS["ThongTin"]["rows"][1:]
    info = {str(key): str(value) for key, value in rows}
    state_rows = SHEETS["MoHinhTrangThai"]["rows"][1:]
    transitions = []
    for state, _, _, targets in state_rows:
        transitions.extend([[state, target.strip()] for target in str(targets).split(",") if target.strip()])

    functions = []
    for name, language, kind, params, buffer_size, bug, magic1, magic2 in SHEETS["HamSinhMa"]["rows"][1:]:
        item = {
            "name": name,
            "language": language,
            "kind": kind,
            "params": [value.strip() for value in str(params).split(",") if value.strip()],
        }
        if buffer_size != "":
            item["buffer_size"] = int(buffer_size)
        if bug != "":
            item["bug"] = bug
        if kind == "magic_value":
            item["magic_values"] = [int(magic1), int(magic2)]
        functions.append(item)

    mutex = SHEETS["LoaiTruLanNhau"]["rows"][1]
    return {
        "name": info["name"],
        "description": info.get("description", ""),
        "requirements": [
            {"id": row[0], "text": row[1], "category": row[2], "priority": row[3]}
            for row in SHEETS["YeuCauChucNang"]["rows"][1:]
        ],
        "state_machine": {
            "states": [row[0] for row in state_rows],
            "initial": [row[0] for row in state_rows if str(row[1]).strip().lower() == "x"],
            "labeling": {
                row[0]: [value.strip() for value in str(row[2]).split(",") if value.strip()]
                for row in state_rows
            },
            "transitions": transitions,
        },
        "safety_properties": [row[0] for row in SHEETS["ThuocTinhAnToan"]["rows"][1:]],
        "mutual_exclusion": {"atom1": mutex[0], "atom2": mutex[1]},
        "functions": functions,
        "arithmetic_checks": [
            {
                "name": row[0],
                "int_vars": [value.strip() for value in str(row[1]).split(",") if value.strip()],
                "condition": row[2],
                "overflow_expr": row[3],
            }
            for row in SHEETS["KiemTraSoHoc"]["rows"][1:]
        ],
    }


def col_name(index: int) -> str:
    result = ""
    while index:
        index, remainder = divmod(index - 1, 26)
        result = chr(65 + remainder) + result
    return result


def row_height(values: list[object], widths: list[int]) -> float:
    lines = 1
    for value, width in zip(values, widths):
        text = str(value)
        estimated = sum(max(1, (len(line) + max(width - 1, 1) - 1) // max(width - 1, 1)) for line in text.split("\n"))
        lines = max(lines, estimated)
    return min(144.0, max(18.0, 14.4 * lines))


def replace_sheet(xml_bytes: bytes, rows: list[list[object]], widths: list[int]) -> bytes:
    root = ET.fromstring(xml_bytes)
    q = lambda name: f"{{{NS}}}{name}"

    dimension = root.find(q("dimension"))
    assert dimension is not None
    dimension.set("ref", f"A1:{col_name(max(len(row) for row in rows))}{len(rows)}")

    cols = root.find(q("cols"))
    assert cols is not None
    cols.clear()
    styled = root.find(q("sheetData")) is not None and any(
        cell.get("s") == "1" for cell in root.findall(f".//{q('c')}")
    )
    for index, width in enumerate(widths, start=1):
        attributes = {"width": str(width), "customWidth": "1", "min": str(index), "max": str(index)}
        if styled:
            attributes["style"] = "1"
        ET.SubElement(cols, q("col"), attributes)

    sheet_data = root.find(q("sheetData"))
    assert sheet_data is not None
    sheet_data.clear()
    for row_number, values in enumerate(rows, start=1):
        attributes = {"r": str(row_number)}
        if styled:
            attributes.update({"ht": str(row_height(values, widths)), "customHeight": "1"})
        row_element = ET.SubElement(sheet_data, q("row"), attributes)
        for column_number, value in enumerate(values, start=1):
            reference = f"{col_name(column_number)}{row_number}"
            cell_attributes = {"r": reference}
            if styled:
                cell_attributes["s"] = "1"
            cell = ET.SubElement(row_element, q("c"), cell_attributes)
            if isinstance(value, (int, float)) and not isinstance(value, bool):
                cell.set("t", "n")
                ET.SubElement(cell, q("v")).text = str(value)
            else:
                cell.set("t", "inlineStr")
                inline = ET.SubElement(cell, q("is"))
                text = ET.SubElement(inline, q("t"))
                string_value = str(value)
                if string_value[:1].isspace() or string_value[-1:].isspace():
                    text.set(f"{{{XML_NS}}}space", "preserve")
                text.text = string_value

    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def update_core_properties(xml_bytes: bytes) -> bytes:
    root = ET.fromstring(xml_bytes)
    title = root.find(f"{{{DC_NS}}}title")
    if title is None:
        title = ET.SubElement(root, f"{{{DC_NS}}}title")
    title.text = "OrderPaymentMicroservice Security Specification"

    subject = root.find(f"{{{DC_NS}}}subject")
    if subject is None:
        subject = ET.SubElement(root, f"{{{DC_NS}}}subject")
    subject.text = "Software security requirements, formal model, memory safety and tests"

    modified = root.find(f"{{{DCTERMS_NS}}}modified")
    if modified is None:
        modified = ET.SubElement(root, f"{{{DCTERMS_NS}}}modified")
        modified.set(f"{{{XSI_NS}}}type", "dcterms:W3CDTF")
    modified.text = datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def build() -> None:
    if not TEMPLATE.exists():
        raise FileNotFoundError(f"Template not found: {TEMPLATE}")
    validate_teacher_schema()

    with ZipFile(TEMPLATE) as source:
        workbook = ET.fromstring(source.read("xl/workbook.xml"))
        relationships = ET.fromstring(source.read("xl/_rels/workbook.xml.rels"))
        relation_targets = {item.attrib["Id"]: item.attrib["Target"] for item in relationships}
        replacements: dict[str, bytes] = {}

        sheets_node = workbook.find(f"{{{NS}}}sheets")
        assert sheets_node is not None
        for sheet in sheets_node:
            name = sheet.attrib["name"]
            if name not in SHEETS:
                raise KeyError(f"No data configured for worksheet {name}")
            relation_id = sheet.attrib[f"{{{REL_NS}}}id"]
            target = relation_targets[relation_id].lstrip("/")
            if not target.startswith("xl/"):
                target = f"xl/{target}"
            spec = SHEETS[name]
            replacements[target] = replace_sheet(source.read(target), spec["rows"], spec["widths"])

        replacements["docProps/core.xml"] = update_core_properties(source.read("docProps/core.xml"))

        with NamedTemporaryFile(dir=OUTPUT.parent, suffix=".xlsx", delete=False) as temporary:
            temp_path = Path(temporary.name)
        try:
            with ZipFile(temp_path, "w", compression=ZIP_DEFLATED) as destination:
                for info in source.infolist():
                    copied_info = deepcopy(info)
                    destination.writestr(copied_info, replacements.get(info.filename, source.read(info.filename)))
            temp_path.replace(OUTPUT)
        finally:
            temp_path.unlink(missing_ok=True)

    PIPELINE_JSON_OUTPUT.write_text(
        json.dumps(build_pipeline_json(), ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(OUTPUT)
    print(PIPELINE_JSON_OUTPUT)


if __name__ == "__main__":
    build()
