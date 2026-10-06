"""Create the OrderPaymentMicroservice security specification from the ATPM XLSX template."""

from __future__ import annotations

from copy import deepcopy
from datetime import datetime, timezone
from pathlib import Path
from tempfile import NamedTemporaryFile
from xml.etree import ElementTree as ET
from zipfile import ZIP_DEFLATED, ZipFile


ROOT = Path(__file__).resolve().parents[1]
TEMPLATE = ROOT / "ATPM" / "Template_Software_Security_iot_gateway_spec.xlsx"
OUTPUT = ROOT / "OrderPaymentMicroservice_Security_Spec.xlsx"

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
            ["REQ-1", "Mọi API Cart, Order và Payment phải yêu cầu JWT hợp lệ; chỉ Admin được liệt kê toàn bộ đơn hàng.", "authentication", "HIGH"],
            ["REQ-2", "Người dùng chỉ được đọc hoặc thay đổi User, Cart, Order và Payment thuộc chính mình; quyền Admin là ngoại lệ được kiểm soát.", "authorization", "HIGH"],
            ["REQ-3", "Khi tạo đơn, giá sản phẩm phải lấy từ cơ sở dữ liệu phía server, sản phẩm phải tồn tại/đang hoạt động và tồn kho phải đủ.", "integrity", "HIGH"],
            ["REQ-4", "TotalAmount phải dương, trong giới hạn cho phép và khớp tổng Quantity × UnitPrice với sai số tối đa 0,01; dữ liệu sai phải bị từ chối.", "validation", "HIGH"],
            ["REQ-5", "Order chỉ được chuyển theo chuỗi CREATED → VALIDATED → PAYMENT_PENDING → PAID → CONFIRMED → COMPLETED; không bỏ bước, quay ngược hoặc hồi sinh trạng thái terminal.", "safety", "HIGH"],
            ["REQ-6", "Order chỉ được chuyển từ PAYMENT_PENDING sang PAID khi có Payment SUCCESS với Amount đúng bằng Order.TotalAmount.", "consistency", "HIGH"],
            ["REQ-7", "Mỗi yêu cầu thanh toán phải có IdempotencyKey; gửi lại cùng khóa và cùng Amount phải trả về giao dịch cũ, còn tái dùng khóa với Amount khác phải bị từ chối.", "payment", "HIGH"],
            ["REQ-8", "Mật khẩu mới phải được băm bằng PasswordHasher có salt/work factor; tài khoản SHA-256 cũ chỉ được xác minh để nâng cấp một lần.", "credential", "HIGH"],
            ["REQ-9", "register_user() không được sao chép email dài từ 32 byte trở lên vào buffer stack 32 byte.", "memory", "HIGH"],
            ["REQ-10", "Product name không được vượt biên buffer heap 32 byte; nhánh từ chối vẫn phải giải phóng vùng nhớ đã cấp phát.", "memory", "HIGH"],
            ["REQ-11", "Cart audit log không được đọc CartItem sau khi phần tử đã được free.", "memory", "HIGH"],
            ["REQ-12", "Khi hủy Order trong mô hình C, mảng OrderItem thuộc sở hữu của Order phải được free trước struct cha.", "memory", "MEDIUM"],
            ["REQ-13", "Payment cleanup không được free TransactionCode hai lần; con trỏ sở hữu phải được đặt NULL sau lần free đầu.", "memory", "HIGH"],
            ["REQ-14", "Từ mọi trạng thái Order có thể đạt tới, mọi đường thực thi hữu hạn tiến triển phải cuối cùng tới COMPLETED, CANCELLED hoặc FAILED.", "liveness", "HIGH"],
        ],
    },
    "YeuCauPhiChucNang": {
        "widths": [13, 82, 14],
        "rows": [
            ["ID", "MoTa", "MucUuTien"],
            ["NFR-1", "Các thay đổi Order, tồn kho và Payment phải được lưu nguyên tử; luồng dùng PostgreSQL phải chạy trong transaction mức Serializable.", "HIGH"],
            ["NFR-2", "Branch coverage của lớp kiểm tra đầu vào và các guard trạng thái trọng yếu phải đạt tối thiểu 90% trong pipeline kiểm thử.", "HIGH"],
            ["NFR-3", "Log lỗi phải đủ OrderId/UserId để truy vết nhưng không được ghi mật khẩu, JWT, Jwt:Key hoặc connection string.", "HIGH"],
            ["NFR-4", "Secret và connection string phải được cung cấp ngoài source control; Jwt:Key phải có ít nhất 32 ký tự.", "HIGH"],
            ["NFR-5", "Các bản C biên dịch với AddressSanitizer/UndefinedBehaviorSanitizer phải phát hiện đúng bản lỗi và không báo lỗi với bản FIXED trên cùng ca kiểm thử.", "MEDIUM"],
        ],
    },
    "RangBuoc": {
        "widths": [13, 82, 14],
        "rows": [
            ["ID", "MoTa", "MucUuTien"],
            ["CON-1", "Ứng dụng dùng ASP.NET Core/.NET 10, Entity Framework Core và PostgreSQL; API trao đổi JSON qua REST.", "MEDIUM"],
            ["CON-2", "Order/Payment Amount nằm trong [0,01; 999999,99], Quantity nằm trong [1; 10000], và UnitPrice nằm trong [0,01; 999999,99].", "HIGH"],
            ["CON-3", "IdempotencyKey bắt buộc, không trắng và dài tối đa 128 ký tự; khóa là duy nhất trong phạm vi một Order.", "HIGH"],
            ["CON-4", "Order terminal gồm COMPLETED, CANCELLED và FAILED; mô hình Kripke dùng self-loop ở trạng thái terminal để quan hệ chuyển là total.", "HIGH"],
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
            ["AG(!invalid_transition)"],
            ["AG(AF(terminal))"],
            ["AG(paid -> payment_success)"],
            ["AG(payment_pending -> !payment_success)"],
        ],
    },
    "LoaiTruLanNhau": {
        "widths": [34, 34],
        "rows": [
            ["MenhDe1", "MenhDe2"],
            ["payment_pending", "payment_success"],
            ["cancelled", "paid"],
            ["cancelled", "completed"],
            ["failed", "paid"],
            ["failed", "completed"],
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
            ["TC-9", "REQ-5, REQ-14", "Kiểm chứng mô hình Order kỳ vọng", "order_state_model.json hợp lệ", "Chạy python security-analysis/run_analysis.py", "P1, P2, P3, P4 đều PASS", "HIGH"],
            ["TC-10", "REQ-5", "Counterexample hồi sinh Order", "order_state_model_buggy.json có cạnh lỗi", "Chạy checker trên model BUGGY", "P1 FAIL với CREATED → CANCELLED → PAID_AFTER_CANCELLED", "HIGH"],
            ["TC-11", "REQ-9, REQ-10, REQ-11, REQ-12, REQ-13", "Sanitizer phát hiện đủ 5 bản C lỗi", "GCC, Make, ASan/UBSan khả dụng", "cd security-analysis/c-memory\nmake\nmake test", "Bản buggy lần lượt báo stack-buffer-overflow, heap-buffer-overflow, heap-use-after-free, memory leak và double-free", "HIGH"],
            ["TC-12", "REQ-9, REQ-10, REQ-11, REQ-12, REQ-13", "Bản FIXED sạch sanitizer và đúng biên chuỗi", "Đã build cả *_fixed", "Chạy cùng kịch bản lỗi\nThử Product/User với độ dài 0, 1, 31, 32, 128 byte", "Bản FIXED không có báo cáo sanitizer; nhận tối đa 31 byte và từ chối từ 32 byte; toàn bộ 22/22 case PASS", "HIGH"],
            ["TC-13", "REQ-4", "Fuzz biên validator Order", "Có xUnit InputValidatorTests", "Chạy các ca null/rỗng, amount, quantity, price, mismatch, nhiều item và giá trị biên", "Input sai bị từ chối; input ở biên hợp lệ được chấp nhận; branch coverage đạt ngưỡng", "MEDIUM"],
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
        "Đặc tả kiểm chứng hình thức cho luồng Order-Payment: state machine có xử lý sự cố "
        "và phục hồi, safety/liveness, idempotency, serialization payment-cancel, bounded "
        "retry, compensation refund, SMT số học và mô hình C về an toàn bộ nhớ."
    )
    SHEETS["YeuCauChucNang"]["rows"].extend([
        ["REQ-15", "Mỗi trạng thái Order-Payment phải đặc tả sự cố, cách phát hiện, biện pháp xử lý và trạng thái tiếp theo; không được có sự cố không có đường phục hồi hoặc kết thúc.", "safety", "HIGH"],
        ["REQ-16", "Retry thanh toán và xác nhận phải có bound; khi hết ngân sách retry phải chuyển FAILED hoặc REFUND_PENDING thay vì lặp vô hạn.", "liveness", "HIGH"],
        ["REQ-17", "Nếu gateway đã charge nhưng không thể hoàn tất đơn hoặc commit PAID, hệ thống phải dùng REFUND_PENDING và REFUNDED để bù trừ; không được đổi thẳng sang FAILED.", "consistency", "HIGH"],
        ["REQ-18", "Payment và cancellation trên cùng Order phải được serialize; không được đồng thời sở hữu critical resource hoặc tạo hai Payment SUCCESS.", "safety", "HIGH"],
    ])
    SHEETS["YeuCauPhiChucNang"]["rows"].append([
        "NFR-6",
        "Pipeline kiểm chứng phải duyệt toàn bộ model hữu hạn, lưu counterexample cho model lỗi và ghi rõ bound, fairness, assumptions và giới hạn kết luận.",
        "HIGH",
    ])
    SHEETS["RangBuoc"]["rows"].extend([
        ["CON-7", "Model dùng tối đa một lần retry thanh toán cuối; PAYMENT_PROCESSING_FINAL không được quay lại PAYMENT_RETRY.", "HIGH"],
        ["CON-8", "Thuộc tính liveness dùng weak fairness: cạnh no-op/retry được đánh dấu stutter có thể lặp, nhưng transition tiến triển đang enable cuối cùng phải chạy.", "HIGH"],
        ["CON-9", "Refund dùng idempotency key riêng; callback refund trùng chỉ trả kết quả đã lưu và không hoàn tiền lần hai.", "HIGH"],
    ])

    state_rows = [["TrangThai", "KhoiTao", "Nhan", "ChuyenDenCacTrangThai"]]
    for state in model["states"]:
        targets = []
        for transition in state["normal"] + state["incidents"]:
            if transition["to"] not in targets:
                targets.append(transition["to"])
        state_rows.append([
            state["id"],
            "x" if state["id"] == "CREATED" else "",
            ", ".join(state["labels"]),
            ", ".join(targets),
        ])
    SHEETS["MoHinhTrangThai"]["rows"] = state_rows
    SHEETS["MoHinhTrangThai"]["widths"] = [28, 12, 68, 66]

    SHEETS["ThuocTinhAnToan"]["rows"] = [
        ["CongThucCTL"],
        ["AG(!violation)"],
        ["AG(paid -> payment_success)"],
        ["AG(completed -> payment_success)"],
        ["AG(cancelled -> !payment_success)"],
        ["AG(failed -> !payment_success)"],
        ["AG(payment_pending -> !payment_success)"],
        ["AG(!resource_conflict)"],
        ["AG(refund_pending -> payment_success)"],
        ["AG(refunded -> payment_compensated)"],
        ["AG(payment_processing -> AF(terminal))"],
    ]
    SHEETS["ThuocTinhAnToan"]["widths"] = [75]
    SHEETS["LoaiTruLanNhau"]["rows"] = [
        ["MenhDe1", "MenhDe2"],
        ["payment_pending", "payment_success"],
        ["cancelled", "payment_success"],
        ["failed", "payment_success"],
        ["resource_conflict", "payment_lock"],
        ["resource_conflict", "cancel_lock"],
        ["two_success_payments", "safe"],
        ["amount_mismatch", "amount_verified"],
        ["refunded", "refund_pending"],
    ]
    SHEETS["HamSinhMa"]["rows"] = [
        ["Ten", "NgonNgu", "Loai", "ThamSo", "KichThuocBuffer", "Loi", "GiaTriBiMat1", "GiaTriBiMat2"],
        ["register_user", "c", "buffer_copy", "input", 32, "buffer-overflow", 31, 32],
        ["import_product_name", "c", "buffer_copy", "input", 32, "buffer-overflow", 31, 32],
        ["remove_cart_item", "c", "alloc_release", "removed", 16, "use-after-free", 101, 1],
        ["cancel_order", "c", "alloc_release", "order, order->items", 48, "memory-leak", 3, 5],
        ["cleanup_payment", "c", "alloc_release", "transaction_code", 32, "double-free", 1, 2],
        ["safe_divide_retry_budget", "python", "divide", "remaining_attempts, elapsed_ms", 0, "divide-by-zero", 0, 1000],
        ["verify_gateway_callback", "python", "magic_value", "signature_code", 0, "authentication-bypass", 4919, 0],
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
        ["TC-16", "REQ-15, REQ-16, NFR-6", "Kiểm chứng model phục hồi đầy đủ", "order_payment_recovery_model.json hợp lệ", "Chạy python security-analysis/verify_recovery_model.py\nĐọc 10 thuộc tính và tập state reachable", "Model đúng có 14 state, 35 transition; P1-P10 PASS và tất cả state reachable", "HIGH"],
        ["TC-17", "REQ-5, REQ-15", "Phản ví dụ CANCELLED bị thanh toán", "Mutation BUG-CANCELLED-PAID được bật", "Thêm cạnh CANCELLED → PAID_AFTER_CANCELLED\nChạy lại toàn bộ thuộc tính", "P1 và P4 FAIL; witness đi từ CREATED tới CANCELLED rồi PAID_AFTER_CANCELLED", "HIGH"],
        ["TC-18", "REQ-6, REQ-15", "Phản ví dụ payment sai amount", "Mutation BUG-WRONG-AMOUNT được bật", "Bỏ amount guard tại PAYMENT_PROCESSING\nChuyển tới PAID_WRONG_AMOUNT", "P1 FAIL và trả witness tới state amount_mismatch", "HIGH"],
        ["TC-19", "REQ-7, REQ-18", "Phản ví dụ hai payment SUCCESS", "Mutation BUG-DUPLICATE-SUCCESS được bật", "Bỏ idempotency guard tại PAID\nTạo state PAID_TWICE", "P1 FAIL và trả witness tới state two_success_payments", "HIGH"],
        ["TC-20", "REQ-18, NFR-1", "Phản ví dụ payment-cancel conflict", "Mutation BUG-RESOURCE-CONFLICT được bật", "Cho payment và cancel cùng sở hữu resource\nChạy kiểm chứng", "P1 và P7 FAIL với witness tới PAY_CANCEL_CONFLICT", "HIGH"],
        ["TC-21", "REQ-16, REQ-17", "Bounded retry và compensation", "Model đúng, weak fairness được khai báo", "Đi PAYMENT_PROCESSING → PAYMENT_RETRY → PAYMENT_PROCESSING_FINAL\nThử timeout cuối và charged-but-commit-failed", "Không có vòng retry tiến triển vô hạn; nhánh cuối tới FAILED hoặc REFUND_PENDING → REFUNDED", "HIGH"],
    ])


apply_recovery_model()


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

    print(OUTPUT)


if __name__ == "__main__":
    build()
