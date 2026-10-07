import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const inputPath = "../OrderPaymentMicroservice_Security_Spec.xlsx";
const outputPath = "../OrderPaymentMicroservice_Security_Spec.xlsx";
const input = await FileBlob.load(inputPath);
const workbook = await SpreadsheetFile.importXlsx(input);

const info = workbook.worksheets.getItem("ThongTin");
info.getRange("B3").values = [[
  "Đặc tả Order-Payment tương thích Spec Verification Lab: kiểm soát truy cập, hai state machine Order/Payment, tồn kho, idempotency, UNKNOWN/reconciliation, bounded retry, refund compensation, concurrency, SMT số học, fuzzing và mô hình C về an toàn bộ nhớ."
]];

const functional = workbook.worksheets.getItem("YeuCauChucNang");
functional.getRange("C2:C19").values = [
  ["safety"], ["safety"], ["safety"], ["safety"], ["safety"], ["safety"],
  ["safety"], ["safety"], ["memory"], ["memory"], ["memory"], ["memory"],
  ["memory"], ["liveness"], ["safety"], ["liveness"], ["safety"], ["safety"]
];
functional.getRange("B15").values = [[
  "Từ mọi trạng thái Order có thể đạt tới, mọi đường tiến triển phải cuối cùng tới COMPLETED, CANCELLED, FAILED hoặc REFUNDED."
]];
functional.getRange("B17").values = [[
  "Charge có tối đa hai attempt (lần đầu và một retry). Khi hết ngân sách không được charge lại; hệ thống chỉ reconcile cho tới khi biết kết quả PAID, FAILED hoặc chuyển sang refund compensation."
]];
functional.tables.items[0].rows.add(null, [
  ["REQ-19", "Worker phục hồi phải quét Payment PROCESSING, UNKNOWN và REFUND_PENDING đã quá hạn; chỉ truy vấn trạng thái gateway, không được tạo charge mới.", "liveness", "HIGH"],
  ["REQ-20", "Inventory reservation phải được RELEASED đúng một lần khi Order CANCELLED, FAILED hoặc REFUNDED và được CONSUMED đúng một lần khi Order COMPLETED.", "safety", "HIGH"],
  ["REQ-21", "Chỉ Admin được khởi tạo/retry/reconcile refund; refund dùng idempotency key riêng và callback lặp không được hoàn tiền lần hai.", "safety", "HIGH"],
  ["REQ-22", "Order, Payment và Product phải phát hiện cập nhật cạnh tranh bằng concurrency token hoặc ràng buộc database và trả HTTP 409 cho xung đột có thể retry.", "safety", "HIGH"],
  ["REQ-23", "Gateway timeout hoặc exception phải đưa Payment về UNKNOWN; hệ thống không được suy diễn UNKNOWN là FAILED và không được hoàn kho trước khi reconcile.", "safety", "HIGH"],
  ["REQ-24", "Chỉ Admin được chuyển Order PAID sang CONFIRMED và CONFIRMED sang COMPLETED; hoàn tất phải consume inventory reservation.", "functional", "HIGH"]
]);

const nonFunctional = workbook.worksheets.getItem("YeuCauPhiChucNang");
nonFunctional.tables.items[0].rows.add(null, [
  ["NFR-7", "Bộ kiểm thử phải tái lập được trên PostgreSQL sạch bằng Testcontainers và không được dùng database phát triển.", "HIGH"],
  ["NFR-8", "Recovery worker phải cấu hình được chu kỳ quét, ngưỡng stale và batch size; một lỗi đơn lẻ không được dừng các lần quét sau.", "HIGH"],
  ["NFR-9", "Payment gateway call phải chạy ngoài database transaction để không giữ khóa trong thời gian chờ mạng.", "HIGH"]
]);

const constraints = workbook.worksheets.getItem("RangBuoc");
constraints.getRange("B4").values = [[
  "IdempotencyKey bắt buộc, không trắng, dài tối đa 128 ký tự và duy nhất toàn hệ thống cho một logical charge; RefundIdempotencyKey là khóa riêng."
]];
constraints.getRange("B5").values = [[
  "Order terminal gồm COMPLETED, CANCELLED, FAILED và REFUNDED; mỗi trạng thái terminal có self-loop để quan hệ chuyển là total."
]];
constraints.getRange("B9").values = [[
  "Recovery model JSON dùng weak fairness cho cạnh stutter; mô hình Excel dùng cho checker CTL cơ bản loại bỏ self-loop ở trạng thái chưa terminal để kiểm tra AF(terminal)."
]];
constraints.tables.items[0].rows.add(null, [
  ["CON-10", "Một logical Payment duy nhất cho mỗi Order; charge có tối đa 2 attempt và mọi retry phải dùng lại idempotency key ban đầu.", "HIGH"],
  ["CON-11", "Gateway charge/refund chạy ngoài transaction; database chỉ lưu intent trước call và finalize/reconcile kết quả trong lần ghi riêng.", "HIGH"]
]);

const state = workbook.worksheets.getItem("MoHinhTrangThai");
state.getRange("D4").values = [["PAYMENT_PROCESSING, CANCELLED"]];
state.getRange("D5").values = [["PAID, PAYMENT_RETRY, FAILED, REFUND_PENDING"]];
state.getRange("D8").values = [["CONFIRMED, CONFIRMATION_RETRY, REFUND_PENDING"]];
state.getRange("D11").values = [["REFUNDED"]];

const safety = workbook.worksheets.getItem("ThuocTinhAnToan");
safety.getRange("A2:A11").values = [
  ["AG(!violation)"],
  ["AG(!resource_conflict)"],
  ["EF(payment_pending)"],
  ["EF(payment_processing)"],
  ["EF(payment_lock)"],
  ["EF(paid)"],
  ["EF(payment_success)"],
  ["EF(completed)"],
  ["EF(cancelled)"],
  ["EF(failed)"]
];
safety.tables.items[0].rows.add(null, [
  ["EF(refund_pending)"],
  ["EF(refunded)"],
  ["EF(payment_compensated)"],
  ["EF(terminal)"],
  ["AF(terminal)"]
]);

const mutex = workbook.worksheets.getItem("LoaiTruLanNhau");
mutex.getRange("A2:B2").values = [["payment_lock", "cancelled"]];

const testCases = workbook.worksheets.getItem("TestCase");
testCases.getRange("F17").values = [[
  "Model đúng có 14 state, 36 transition; P1-P10 PASS và tất cả state reachable"
]];
testCases.tables.items[0].rows.add(null, [
  ["TC-22", "REQ-15, NFR-6", "Nạp workbook bằng pipeline Spec Verification Lab", "Python 3.14, openpyxl và z3-solver khả dụng", "Gọi parse_specification_excel() trên workbook\nChạy run_pipeline(..., fuzz_iterations=300, seed=1)\nĐọc báo cáo bốn chương", "Nạp đủ 10 sheet; không deadlock; toàn bộ công thức CTL hợp lệ báo DUNG; mutual exclusion OK; pipeline hoàn tất không exception", "HIGH"],
  ["TC-23", "REQ-18, REQ-22, NFR-1, NFR-7", "Race payment và cancellation trên PostgreSQL", "Docker hoạt động; PostgreSQL Testcontainer sạch; Order PAYMENT_PENDING", "Gửi POST /api/payments và POST /api/orders/{id}/cancel đồng thời\nĐọc lại Order, Payment và InventoryStatus", "Chỉ một thao tác thắng; kết quả cuối chỉ là PAID + SUCCESS + RESERVED hoặc CANCELLED + không Payment + RELEASED", "HIGH"],
  ["TC-24", "REQ-19, REQ-23, NFR-8", "Worker reconcile Payment UNKNOWN sau restart", "Payment UNKNOWN/PROCESSING đã stale; gateway lưu kết quả theo idempotency key", "Khởi động worker với ngưỡng stale đã đạt\nChờ một chu kỳ quét\nĐọc Payment và Order", "Worker chỉ gọi status query, không charge lần hai; kết quả được finalize hoặc giữ UNKNOWN để lần quét sau xử lý", "HIGH"],
  ["TC-25", "REQ-20, REQ-24", "Inventory chuyển RELEASED hoặc CONSUMED đúng một lần", "Có Order với inventory RESERVED", "Hủy một Order chưa charge và gọi hủy lại\nThanh toán Order khác, Admin confirm rồi complete và gọi complete lại", "Order hủy có RELEASED và stock chỉ hoàn một lần; Order hoàn tất có CONSUMED và không hoàn stock", "HIGH"],
  ["TC-26", "REQ-17, REQ-21", "Refund idempotent và reconcile sau crash", "Admin JWT; Payment SUCCESS; Order PAID hoặc CONFIRMED", "POST refund với key ổn định\nLặp request cùng key\nMô phỏng crash trước finalize rồi gọi refund/reconcile", "Chỉ một refund gateway; Payment/Order đi REFUND_PENDING → REFUNDED; inventory RELEASED đúng một lần", "HIGH"],
  ["TC-27", "REQ-7, REQ-22", "Hai yêu cầu payment đồng thời", "PostgreSQL Testcontainer; một Order PAYMENT_PENDING", "Gửi hai POST /api/payments với hai key khác nhau cùng lúc\nĐọc bảng Payments", "Một request HTTP 201, request còn lại HTTP 409; database chỉ có một Payment SUCCESS cho Order", "HIGH"],
  ["TC-28", "REQ-24", "Phân quyền và chuỗi confirm-complete", "Customer có Order PAID; có JWT Customer và Admin", "Customer gọi confirm/complete\nAdmin gọi confirm\nAdmin gọi complete", "Customer nhận 403; Admin nhận PAID → CONFIRMED → COMPLETED và InventoryStatus=CONSUMED", "HIGH"]
]);

for (const sheet of workbook.worksheets.items) {
  const used = sheet.getUsedRange();
  if (used) {
    used.format.font = { name: "Arial", size: 10 };
    used.format.verticalAlignment = "center";
  }
}

workbook.recalculate();

const checks = await workbook.inspect({
  kind: "workbook,sheet,table",
  maxChars: 12000,
  tableMaxRows: 8,
  tableMaxCols: 8,
  tableMaxCellChars: 120,
});
console.log(checks.ndjson);

const errors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!|#SPILL!|#CALC!",
  options: { useRegex: true, maxResults: 300 },
  summary: "final formula error scan",
});
console.log(errors.ndjson);

await fs.mkdir("final-previews", { recursive: true });
for (const sheetName of ["YeuCauChucNang", "MoHinhTrangThai", "ThuocTinhAnToan", "TestCase"]) {
  const preview = await workbook.render({ sheetName, autoCrop: "all", scale: 1, format: "png" });
  await fs.writeFile(`final-previews/${sheetName}.png`, new Uint8Array(await preview.arrayBuffer()));
}

const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);
