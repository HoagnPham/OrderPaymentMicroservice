"""Build the report and detailed appendices from project evidence, without ATPM access."""
import json
from pathlib import Path
import importlib.util
from datetime import datetime
from zoneinfo import ZoneInfo

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / 'security-analysis'
OUT = BASE / 'report'

def read(relative):
    return json.loads((BASE / relative).read_text(encoding='utf-8'))

def table(headers, rows):
    def cell(value):
        return str(value).replace('|', '\\|').replace('\n', '<br>')
    return '\n'.join(['| ' + ' | '.join(headers) + ' |', '| ' + ' | '.join(['---'] * len(headers)) + ' |'] +
                     ['| ' + ' | '.join(cell(value) for value in row) + ' |' for row in rows]) + '\n'

def chapter_tokens():
    threat = read('threat_model.json')
    audit = read('results/liveness_audit.results.json')
    memory = read('results/chapter4_run.results.json')
    static = read('c-memory/evidence/static-analysis.json')
    recovery = read('results/order_payment_recovery_model.results.json')
    assets = table(['Asset', 'Tên', 'Phân loại', 'C / I / A'], [
        [a['id'], a['name'], a['classification'], ' / '.join(a['cia'][k] for k in ('confidentiality','integrity','availability'))]
        for a in threat['assets']])
    risks = table(['Threat', 'STRIDE / boundary', 'Kịch bản', 'L×I', 'Trạng thái'], [
        [t['id'], t['stride']+' / '+t['boundary'], t['threat'], f"{t['likelihood']}×{t['impact']}={t['risk_score']}", t['status']]
        for t in threat['threats']])
    liveness = table(['Phép kiểm', 'Kết quả', 'Nghĩa'], [
        ['AG AF terminal trên graph gốc', audit['unconditional_AG_AF_terminal'], 'Có đường infinite nonterminal'],
        ['AG AF terminal trên graph chỉ progress', audit['progress_only_AG_AF_terminal'], 'Kết quả của policy lọc cạnh'],
        ['Audit đối chiếu hai semantics', audit['passed'], 'PASS xác nhận sự khác biệt, không phủ nhận counterexample'],
    ])
    mutations = table(['Mutation', 'Property FAIL thực tế', 'Detected'], [
        [m['id'], ', '.join('recovery:'+p for p in m['actual_failed_properties']), m['detected']]
        for m in recovery['negative_scenarios']])
    memory_table = table(['Phép kiểm', 'Outcome', 'Điều kiện'], [
        ['GCC + cppcheck + scanner + layout', static['assertions']['passed'], static['environment']['gcc']+'; '+static['environment']['cppcheck']],
        ['Sanitizer case oracle', '22/22 PASS', 'Ngoài sandbox, ASan/UBSan/LSan; detect_leaks=1'],
        ['Đóng gói evidence', memory['passed'], 'Hash source/binary/evidence; timestamp ghi sau lệnh'],
    ])
    static_table = table(['Module', 'Scanner BUGGY', 'Scanner FIXED', 'Cppcheck trực tiếp memory finding BUGGY'], [
        [module, ', '.join(f['rule'] for f in static['modes']['buggy']['pattern_scanner'][module]),
         ', '.join(f['rule'] for f in static['modes']['fixed']['pattern_scanner'][module]) or 'Không có',
         'bufferAccessOutOfBounds' if module == 'user' else 'Không báo loại lỗi kỳ vọng']
        for module in static['scope']])
    layout_table = table(['Struct', 'sizeof', '_Alignof', 'Môi trường'], [
        [name, value['size'], value['alignment'], static['environment']['platform']]
        for name,value in static['layout'].items() if name != 'offsets'])
    backup = read('results/backup_restore.results.json')
    load = read('results/load_probe.results.json')
    operational_rows = [
        ['Backup/restore local', str(len(backup['table_hashes']))+' bảng có hash khớp; 4 audit triggers; receipt và identity sequence PASS',
         f"Backup {backup['backup_ms']:.1f} ms; restore {backup['restore_ms']:.1f} ms; API recovery {backup['restored_api_recovery_ms']:.1f} ms"],
    ]
    for name in ('read', 'write', 'rate_limit'):
        run = load[name]
        counts = ', '.join(code+': '+str(count) for code,count in sorted(run['status_counts'].items()))
        operational_rows.append([name, str(run['requests'])+' requests; concurrency '+str(run['concurrency'])+'; '+counts,
                                 f"{run['requests_per_second']:.2f} req/s; successful {run['successful_requests_per_second']:.2f}/s; p95 {run['p95_ms']:.2f} ms"])
    refund = read('results/refund_restart.results.json')
    operational_rows.append(['Refund process kill', str(refund['journal_entries'])+' journal entries giữ hash; charge reference giữ nguyên; stock release một lần', 'PASS; DB outage live=200, ready/status=503'])
    operational = table(['Kịch bản', 'Phạm vi / outcome', 'Timing trong lần chạy'], operational_rows)
    return {'@@OPERATIONAL_TABLE@@':operational, '@@UNIT_COUNT@@':str(read('results/application_tests.results.json')['suites']['unit']['passed']), '@@PIPELINE_PAIRS@@':str(read('results/model_projection.results.json')['pipeline_state_pairs']), '@@ASSET_TABLE@@':assets, '@@THREAT_TABLE@@':risks, '@@LIVENESS_TABLE@@':liveness,
            '@@MUTATION_TABLE@@':mutations, '@@MEMORY_RUN_TABLE@@':memory_table,
            '@@STATIC_DETECTION_TABLE@@':static_table, '@@LAYOUT_TABLE@@':layout_table}


def enrich_report(report):
    import re
    tokens = chapter_tokens()
    parts = re.split(r'(?=^## [1-9]\. )', report, flags=re.M)
    templates = sorted((OUT / 'chapters').glob('[0-9][0-9]_*.md'))
    by_chapter = {int(p.name[:2]):p for p in templates}
    for index,part in enumerate(parts):
        match = re.match(r'## ([1-9])\.', part)
        if match is None:
            continue
        number = int(match.group(1))
        if number not in by_chapter:
            raise ValueError(f'Missing chapter template {number}')
        content = by_chapter[number].read_text(encoding='utf-8')
        for name,value in tokens.items():
            content = content.replace(name,value)
        if '@@' in content:
            raise ValueError(f'Unresolved token in chapter {number}')
        parts[index] = content+'\n\n' if number <= 4 else part.rstrip()+'\n\n'+content+'\n\n'
    return ''.join(parts)


def enrich_annex(annex):
    audit = read('results/liveness_audit.results.json')
    static = read('c-memory/evidence/static-analysis.json')
    model = read('order_payment_recovery_model.json')
    recovery = read('results/order_payment_recovery_model.results.json')
    threat = read('threat_model.json')
    additions = {}
    additions['A'] = '''### A.3. Ma trận endpoint/quyền

| Method/route | Anonymous | Customer | Admin | Điều kiện |
| --- | --- | --- | --- | --- |
| POST users/register, users/login | Cho phép | Cho phép | Cho phép | DTO/service validation |
| GET products, category, id | Cho phép | Cho phép | Cho phép | Active product |
| POST/PUT/DELETE products | 401 | 403 | Cho phép | Validation và state data |
| GET/PUT users/id | 401 | Owner | Cho phép | Claim→route user |
| GET cart/user/id; POST add | 401 | Owner | Cho phép | Quantity/product guard |
| PUT/DELETE cart/id/item/id; DELETE clear | 401 | Owner | Cho phép | Cart.UserId; CartItem.CartId |
| POST orders | 401 | Cho phép | Cho phép | CustomerId từ subject; server price/stock |
| GET orders | 401 | 403 | Cho phép | Global listing |
| GET orders/mine; GET id; POST cancel | 401 | Owner | Theo policy endpoint | mine luôn dùng subject; cancel có payment guard |
| POST orders/id/confirm, complete | 401 | 403 | Cho phép | Paid evidence và state nguồn |
| POST payments; GET id; POST retry, reconcile | 401 | Owner | Cho phép | Order owner; amount/key/state guard |
| POST payments/id/refund, refund/retry | 401 | 403 | Cho phép | Refund key và trạng thái hợp lệ |

“Cho phép” là qua lớp quyền, không cam kết nghiệp vụ trả 2xx. Test hiện kiểm một
phần các đường trong bảng; không dùng bảng chính sách source thay danh sách test.

### A.4. Asset, boundary và qualified property references

'''+table(['Threat','Asset','Boundary','Model-qualified properties','Control status'],[
        [t['id'],', '.join(t['assets']),t['boundary'],', '.join(ref['model']+':'+ref['id'] for ref in t.get('property_refs',[])) or 'Ngoài model lifecycle',t['status']]
        for t in threat['threats']])+'''
### A.5. Chính sách data ownership

UserId/CustomerId trong request không tự cấp quyền. PriceAtAdd là snapshot của Cart;
Order dùng Product.Price hiện tại. Payment.Amount bind với Order.TotalAmount.
InventoryStatus biểu diễn reservation cùng stock đã giảm; không có bảng reservation
riêng. Hash/authenticity của gateway response chưa được mock chứng minh.
'''
    b = ['### B.6. Chi tiết hợp đồng F1–F7\n']
    for contract in model['functional_contracts']:
        b += [f"#### {contract['id']}\n", contract['responsibility']+'\n',
              'Precondition: '+contract['precondition']+'\n',
              table(['State','Event','Đích'], [[source,event,target] for source,outcomes in contract['state_outcomes'].items() for event,target in outcomes.items()]),
              '\n'.join('- '+g for g in contract['guarantees'])+'\n']
    b += ['### B.7. Counterexample recovery và property namespace\n']
    for mutation in recovery['negative_scenarios']:
        b += [f"#### {mutation['id']}\n",'Expected FAIL: '+', '.join(mutation['expected_failed_properties'])+'; actual FAIL: '+', '.join(mutation['actual_failed_properties'])+'.\n',
              '```json\n'+json.dumps(mutation['witnesses'],ensure_ascii=False,indent=2)+'\n```\n']
    b += ['### B.8. Audit liveness\n', table(['Raw AG AF','Progress-only AG AF','Audit PASS'],[[audit['unconditional_AG_AF_terminal'],audit['progress_only_AG_AF_terminal'],audit['passed']]]),
          'Lasso là prefix hữu hạn nối cycle lặp vô hạn; cycle không chứa terminal.\n',
          '```json\n'+json.dumps(audit['counterexample_lasso'],ensure_ascii=False,indent=2)+'\n```\n',
          'Các cạnh được checker loại dù thay đổi state:\n',table(['Nguồn','Event','Đích','Kind'],[[e['source'],e['event'],e['to'],e['kind']]for e in audit['excluded_edges_with_state_change']]),
          'Fixed-point rounds của cả graph nằm trong [liveness audit](../results/liveness_audit.results.json). Policy lọc cạnh không tự chứng minh fair-CTL.\n']
    additions['B'] = '\n'.join(b)
    c = ['### C.3. Compiler, configuration và ground truth\n',chapter_tokens()['@@STATIC_DETECTION_TABLE@@'],
         'Cppcheck return_code=0 không đồng nghĩa không finding; XML vẫn có error/style. BUGGY và FIXED được chạy theo cấu hình preprocessor riêng.\n',
         '### C.4. Source minh họa ownership/lifetime\n']
    for module in static['scope']:
        path = BASE/'c-memory/modules'/f'{module}.c'
        c += [f'#### {module}.c\n','```c\n'+'\n'.join(line.rstrip() for line in path.read_text().splitlines())+'\n```\n']
    c += ['### C.5. Diagnostic thực thi\n']
    for module in static['scope']:
        path = BASE/'c-memory/results'/f'{module}_buggy.log'
        if path.exists():
            lines = path.read_text(errors='replace').splitlines()
            excerpt = '\n'.join(line for line in lines if any(key in line for key in ['ERROR:','SUMMARY:','#0 ','#1 ','Direct leak','Indirect leak']))
            c += [f'#### {module}_buggy\n','```text\n'+excerpt[:3500]+'\n```\n']
        else:
            c += [f'{module}: raw log không có tại thời điểm sinh; dùng summary evidence, không tạo stack trace giả.\n']
    c += ['### C.6. ABI và provenance\n',chapter_tokens()['@@LAYOUT_TABLE@@'],
          'Offsets đo được: '+json.dumps(static['layout']['offsets'])+'.\n',
          'Metadata source/binary/environment: [chapter4 run](../results/chapter4_run.results.json). Không có timestamp start/finish chính xác; make dùng binary hiện có nếu còn mới hơn source.\n',
          '### C.7. Tái lập chỉ bằng project\n',
          '```bash\nmake -C security-analysis/c-memory static-check\nmake -C security-analysis/c-memory test\n```\n',
          'Không chạy make clean nếu muốn giữ raw log để đọc. Khi sandbox làm LSan thất bại, giữ failure riêng và chạy trong môi trường được phép; không tắt detect_leaks để đạt PASS.\n']
    additions['C'] = '\n'.join(c)
    import re
    chunks = re.split(r'(?=^## Phụ lục [A-G] —)',annex,flags=re.M)
    for index,chunk in enumerate(chunks):
        match=re.match(r'## Phụ lục ([A-G]) —',chunk)
        if match and match.group(1) in additions:
            chunks[index]=chunk.rstrip()+'\n\n'+additions[match.group(1)]+'\n\n'
    return ''.join(chunks)


def main():
    tests = read('results/application_tests.results.json')
    mapping = read('results/model_projection.results.json')
    model = read('order_payment_recovery_model.json')
    recovery = read('results/order_payment_recovery_model.results.json')
    payment = read('results/payment_verification.results.json')
    inventory = read('results/inventory_verification.results.json')
    workbook = read('results/teacher_spec_verification.json')
    fuzz = read('results/application_fuzzing.results.json')
    coverage = read('results/coverage_summary.json')
    dynamic = read('c-memory/evidence/dynamic-analysis.json')
    unit = tests['suites']['unit']; integration = tests['suites']['integration']
    day = datetime.now(ZoneInfo('Asia/Bangkok')).strftime('%d/%m/%Y')
    OUT.mkdir(exist_ok=True)
    report = f'''# Báo cáo kiểm chứng an toàn OrderPaymentMicroservice

Ngày tổng hợp: {day}. Các thời điểm thực thi cụ thể nằm trong evidence.

## 1. Mục tiêu và phạm vi

Đề tài xây dựng REST API xử lý đơn hàng và thanh toán, rồi áp dụng mô hình hóa,
kiểm chứng số học và kiểm thử để đánh giá tính an toàn. Đối tượng chính là Order,
Payment và inventory reservation; User/Product/Cart hỗ trợ luồng demo.

Phạm vi thực thi gồm ASP.NET Core .NET 10, EF Core, PostgreSQL và gateway mô phỏng.
Ứng dụng được triển khai trong một host và dùng chung database. Vì vậy báo cáo mô tả
một dịch vụ Order/Payment có các module hỗ trợ, không tuyên bố các module là những
microservice được triển khai độc lập.

Cách tổ chức chương và phụ lục được tham khảo từ BAOCAO.html về Smart EV Charging.
Các requirement, số trạng thái và evidence của Smart EV không được đưa sang đề tài này.
Thư mục ATPM được loại khỏi mọi thao tác trong đợt hoàn thiện này.

## 2. Chương trình mục tiêu

Client gọi Controller qua HTTP; JWT xác định người dùng và role. Service kiểm tra
điều kiện nghiệp vụ, đọc giá Product từ database và sử dụng state machine để thay đổi
Order/Payment. Order reservation trừ stock khi tạo đơn; cancel/decline/refund hợp lệ
hoàn lại stock, còn complete chuyển reservation sang CONSUMED.

Gateway được gọi sau khi lưu trạng thái PROCESSING, bên ngoài transaction database.
Điều này tránh giữ transaction trong lúc chờ mạng. Tuy nhiên hệ thống phải đối soát
kết quả không rõ ràng, thay vì coi timeout là decline. Recovery worker thực hiện
reconciliation cho PROCESSING/UNKNOWN/REFUND_PENDING bị cũ.

Attack surface gồm register/login, payload Product/Cart/Order, ID trên route,
Idempotency-Key, các lệnh lifecycle và phản hồi gateway. Client không có quyền quyết
định giá thanh toán hay truy cập tài nguyên của người dùng khác.

Kiến trúc, API, input/output và danh mục requirement: [Phụ lục A](PHU_LUC_A_G.md#phụ-lục-a--hệ-thống).

## 3. Mô hình trạng thái và thuộc tính

Project có ba cách biểu diễn phục vụ mục đích khác nhau: Kripke Order cơ sở,
recovery model theo event và đặc tả TLA+. Kết quả model không tự chứng minh mọi hành
vi của implementation. Recovery model hiện có 15 trạng thái, 37 chuyển tiếp có event,
7 hợp đồng chức năng và 15 thuộc tính. Sáu mutation được dùng để kiểm tra khả năng
phát hiện sai trạng thái, sai tiền, trùng charge và compensation sai.

Workbook xuất graph còn 32 cặp nguồn–đích vì bỏ metadata event/action/kind và gộp
những cặp trùng. Checker đối chiếu xác nhận hai tập cạnh sau phép chiếu bằng nhau.
Bảy CTL trong workbook là tập thuộc tính riêng, không đồng nghĩa toàn bộ P1–P15.

Liveness phụ thuộc giả định tiến triển/fairness và cách xử lý stutter. Thực tế một
gateway có thể tiếp tục UNKNOWN; lúc đó hệ thống giữ reservation để tránh hoàn kho
trong khi đã thu tiền. TLC có evidence lịch sử với 20 trạng thái phân biệt, không
được chạy lại trong đợt này. Đây là TLC model checking; không gọi kết quả đó là một
thí nghiệm BMC có bound k.

Danh sách state, event, property và giả định: [Phụ lục B](PHU_LUC_A_G.md#phụ-lục-b--model-và-thuộc-tính).

## 4. Phân tích bộ nhớ và mã nguồn

Năm module C học tập tương ứng User/Product/Cart/Order/Payment có phiên bản BUGGY và
FIXED. Evidence lịch sử ghi nhận scanner/cppcheck/layout và 22 ca sanitizer đúng kỳ
vọng. Với BUGGY, test PASS nghĩa là diagnostic lỗi mong đợi đã được phát hiện; không
có nghĩa chương trình lỗi an toàn. Scanner riêng theo pattern không thay thế một
static analyzer đầy đủ.

Các module C không phải bản dịch của source C#. Kết quả chỉ chứng minh hành vi đã
quan sát trong executable và input đã chọn. Static/sanitizer không chạy lại trong
đợt này. Danh mục CWE và kết quả: [Phụ lục C](PHU_LUC_A_G.md#phụ-lục-c--static-và-bộ-nhớ).

## 5. Kiểm chứng số học

Payment SMT dùng integer cents, bảy truy vấn SAT/UNSAT đúng kỳ vọng. SAT trong truy
vấn lỗi là witness tồn tại; UNSAT chỉ áp dụng các assumption được mã hóa. Runtime
sử dụng decimal, database lưu tiền với precision 19,2; mô hình integer không tự
chứng minh rounding/overflow của mọi đường thực thi.

Đợt này bổ sung SMT inventory hai dòng cùng sản phẩm. Baseline stock=10, q1=q2=6
cho witness tồn kho âm dù từng dòng riêng lẻ hợp lệ. Với điều kiện mới stock ≥ q1+q2,
truy vấn tồn kho âm UNSAT; trường hợp đúng biên stock=10, q1=q2=5 vẫn SAT.

Kết quả inventory là chứng minh số học về phép trừ tổng lượng, chưa chứng minh
interleaving nhiều transaction. Evidence PostgreSQL bổ sung kiểm tra constraint và
đồng bộ schema. Các truy vấn: [Phụ lục D](PHU_LUC_A_G.md#phụ-lục-d--smt-và-biên).

## 6. Kiểm thử động và fuzzing

Bộ test ứng dụng mới nhất ghi nhận {unit['passed']}/{unit['total']} unit test và
{integration['passed']}/{integration['total']} integration test PASS, không SKIP.
Ba campaign fuzz cũng nằm trong unit suite; đợt này đo lại coverage campaign.
Branch coverage {fuzz['coverage']['branch_percent']}% áp dụng InputValidator,
PaymentValidator, hai state machine và MoneyAmounts. InputValidator có branch coverage {coverage['branch_coverage_percent']}%,
không phải coverage toàn ứng dụng.

Regression mới kiểm tra sản phẩm trùng trong Order, quantity âm/0/quá lớn, cộng dồn
Cart vượt kho, sản phẩm inactive, đăng ký sai và giá/tồn kho Product sai. Test HTTP
xác minh status trả về và dữ liệu lưu; test PostgreSQL kiểm constraint 23514, schema
không lệch migration, và cập nhật/xóa giỏ giữ tổng tiền nhất quán.

Các campaign chưa fuzz toàn HTTP API, chưa dùng gateway sandbox và chưa thực hiện
mọi cửa sổ crash; process-kill sau charge đã có evidence riêng. Cấu hình và danh sách test: [Phụ lục E](PHU_LUC_A_G.md#phụ-lục-e--fuzzing-và-test).

## 7. Kết quả và truy vết

{table(['Phép kiểm', 'Kết quả', 'Phạm vi'], [
    ['Unit', f"{unit['passed']}/{unit['total']} PASS", 'Source hiện tại; regression lifecycle, timeout, precision và conflict'],
    ['HTTP/PostgreSQL', f"{integration['passed']}/{integration['total']} PASS", 'Database tạm, migration/seed và test host'],
    ['Recovery', '15/15 thuộc tính; 6 mutation detected', 'Graph hữu hạn, giả định được khai báo'],
    ['Payment SMT', '7/7 đúng kỳ vọng', 'Số học integer cents'],
    ['Inventory SMT', '3/3 đúng kỳ vọng', 'Hai dòng của một sản phẩm'],
    ['Workbook checker', f"{workbook['sheet_count']} sheet; {len(workbook['ctl'])} CTL PASS", 'Checker độc lập, không chạy công cụ Windows giảng viên'],
    ['Model projection', f"37 event → {mapping['pipeline_state_pairs']} cặp", 'Tập cạnh sau phép chiếu bằng nhau'],
])}
Bảng truy vết máy đọc được nằm ở ../results/traceability_matrix.csv. Manifest ghi
hash artifact và base commit; working tree còn thay đổi chưa commit nên không gán
kết quả cho một commit sạch. Hash đóng gói không thay thế hash tại thời điểm thực
thi của evidence lịch sử. [Phụ lục F](PHU_LUC_A_G.md#phụ-lục-f--evidence-và-truy-vết)
liệt kê kết quả từng test và cách tái lập.

## 8. Phát hiện, sửa lỗi và giới hạn

Lỗi inventory phát sinh vì kiểm từng dòng trước khi trừ tổng lượng. Sửa bằng cộng
quantity theo ProductId với long, so sánh stock trước mọi mutation. Các dòng vẫn
được giữ riêng trong OrderItem; cancel hoàn lại tổng reservation đúng một lần.

Cart thêm guard quantity và tổng cộng, chặn inactive và kiểm stock khi update.
Add/update/remove dùng transaction Serializable trên PostgreSQL để các lần lưu item
và tổng không commit riêng. Quantity=0 ở update tiếp tục xóa item; quantity âm bị từ
chối. Cart chỉ báo lượng khả dụng, không giữ chỗ; Order vẫn kiểm tra lại kho.

DTO User/Product thêm annotation validation; service cũng kiểm tra để lời gọi trực
tiếp không bỏ qua guard MVC. Invalid input trả 400; Order thiếu kho trả 409.
Database bổ sung CHECK Stock≥0, Price>0 và Quantity>0 ở OrderItem/CartItem.

Migration không tự sửa/xóa dữ liệu cũ vi phạm constraint: database có dữ liệu sai
cần được kiểm tra và chỉnh có chủ đích trước khi nâng cấp. Kiểm chứng hiện dùng
database sạch, không thực hiện migration lên database phát triển của người dùng.

Gateway mặc định DurableFake lưu journal bền vững; process-kill recovery sau charge
đã PASS. Audit/admission đã hiện thực và test trong phạm vi local; COMMIT audit đã nguyên tử và Order create có key tùy chọn.
External audit outbox, retention, distributed quota, callback thật, refund crash windows khác và BMC riêng còn mở.
Không dùng PASS của suite hiện tại để gọi hệ thống production-ready.
[Phụ lục G](PHU_LUC_A_G.md#phụ-lục-g--hạn-chế-và-công-việc-còn-mở).

## 9. Kết luận

Project có chuỗi bằng chứng từ lỗi inventory đến witness SMT, guard source,
regression test và constraint PostgreSQL. Bộ báo cáo và phụ lục phân biệt rõ
implementation, mô hình học tập, kiểm tra độc lập và evidence lịch sử. Phần còn
mở được giữ rõ trong bảng truy vết để tránh suy rộng ngoài phạm vi đã xác minh.
'''
    (OUT / 'BAO_CAO.md').write_text(enrich_report(report).rstrip()+'\n', encoding='utf-8')
    annex = ['# Phụ lục kỹ thuật OrderPaymentMicroservice\n',
             'Các bảng được sinh từ model và evidence của project. Không sao chép số liệu Smart EV.\n',
             '## Phụ lục A — Hệ thống\n', '''```mermaid
flowchart TD
    Client[Shop / Client] --> API[Controllers / JWT / Ownership]
    API --> Services[Order / Payment / Cart / Product / User Services]
    Services --> Guards[Input validation / State machines]
    Services --> DB[(PostgreSQL / Migration / Constraints)]
    Services --> Gateway[IPaymentGateway / FakePaymentGateway]
    Worker[PaymentRecoveryWorker] --> Services
    Checks[Unit / HTTP integration / SMT / Model checker] -. kiểm tra .-> Services
    Checks -. kiểm tra .-> DB
```

### A.1. Ranh giới và dữ liệu

| Giao diện | Đầu vào | Đầu ra | Guard |
| --- | --- | --- | --- |
| Register/Login | Email/password/name | User/JWT hoặc 400/401 | DTO + service validation; hash password |
| Product create/update | Name/price/stock | Product hoặc 400 | Admin, validation, CHECK price/stock |
| Cart add/update | ProductId/quantity | Cart hoặc 400/409 | Ownership, quantity, stock, transaction |
| Order create | Items/total/address | Order hoặc 400/409 | Claim customer, giá server, tổng quantity |
| Payment create | OrderId/amount/key | Payment hoặc 400/403/409 | Ownership, exact amount, idempotency |
| Confirm/Complete/Refund | ID/key | Lifecycle mới hoặc conflict | Admin, state guard, payment evidence |
| Reconcile | Payment ID | Kết quả gateway/UNKNOWN | Ownership hoặc recovery worker |

Chi tiết endpoint: các Controller trong source. User/Order/Payment/Cart là dữ liệu
cần kiểm tra ownership; gateway là trust boundary riêng. State của gateway không
thay thế trực tiếp state Order.

### A.2. Requirement đặc tả
''', table(['ID', 'Nội dung', 'Danh mục', 'Ưu tiên'], [[r['id'],r['text'],r['category'],r['priority']] for r in read('order_payment_pipeline_spec.json')['requirements']]),
             'SEC-STOCK-01, SEC-CART-01/02 và SEC-INPUT-01 trong traceability là yêu cầu bổ sung của đợt sửa lỗi, không thay số ID REQ gốc.\n',
             '## Phụ lục B — Model và thuộc tính\n', '### B.1. Giả định\n', '\n'.join('- '+a for a in model['assumptions'])+'\n',
             '### B.2. Toàn bộ trạng thái recovery\n', table(['Aggregate state','Order','Payment','Labels','Ý nghĩa'], [[s['id'],s['order_status'],s['payment_status'],', '.join(s['labels']),s['purpose']] for s in model['states']]),
             '### B.3. Toàn bộ chuyển tiếp có event\n']
    spec=importlib.util.spec_from_file_location('recovery',BASE/'verify_recovery_model.py');checker=importlib.util.module_from_spec(spec);spec.loader.exec_module(checker)
    annex += [table(['Nguồn','Event','Đích','Kind','Action'], [[e['from'],e['event'],e['to'],e['kind'],e.get('action','')] for e in checker.transitions(model)]),
              '### B.4. Thuộc tính\n', table(['ID','Loại','Nội dung'], [[p['id'],p['type'],p['description']] for p in model['properties']]),
              '### B.5. Đối chiếu và counterexample\n',
              'Model cơ sở có witness CREATED → CANCELLED → PAID_AFTER_CANCELLED. Sáu negative mutation của recovery và witness đầy đủ nằm trong ../results/order_payment_recovery_model.results.json.\n',
              f"37 event → {mapping['pipeline_state_pairs']} cặp state; hai tập cạnh sau phép chiếu bằng nhau. Property workbook có ngữ nghĩa riêng; EF(terminal) chỉ chứng minh tồn tại đường tới terminal.\n",
              '## Phụ lục C — Static và bộ nhớ\n',
              '### C.1. CWE và phạm vi\n', table(['Module','Lỗi BUGGY','CWE'], [['Order','Owned allocation leak','CWE-401'],['Payment','Double free','CWE-415'],['Cart','Use after free','CWE-416'],['Product','Heap buffer copy','CWE-120'],['User','Stack buffer copy','CWE-120']]),
              'GCC/cppcheck version, finding và layout: ../c-memory/evidence/static-analysis.json. Pattern rules là scanner riêng; cppcheck có false negative được ghi rõ trong ../c-memory/README.md. Không tuyên bố Clang-Tidy/Clang Analyzer đã chạy.\n',
              '### C.2. Toàn bộ case sanitizer (lần chạy ngoài sandbox mới nhất)\n', table(['Case','Test PASS','Exit code','Diagnostic mong đợi'], [[c['case'],c['passed'],c['exit_code'],c['expected_diagnostic']] for c in dynamic['cases']]),
              '## Phụ lục D — SMT và biên\n',
              '### D.1. Payment SMT\n', table(['Query','Expected','Actual','PASS'], [[c['id'],c['expected'],c['actual'],c['passed']] for c in payment['cases']]),
              'Đặc tả: ../../OrderPaymentMicroservice/Security/PaymentVerification.z3. Model integer cents; witness/model solver nằm trong ../results/payment_verification.results.json.\n',
              '### D.2. Inventory SMT\n', table(['Query','Expected','Actual','PASS'], [[c['id'],c['expected'],c['actual'],c['passed']] for c in inventory['cases']]),
              'Stock=10/q1=q2=6 minh họa baseline sai. Guard tổng quantity loại stock âm. Biên 5+5 vẫn được chấp nhận, và regression kiểm cancel trả lại đúng 10.\n',
              '### D.3. BMC\n', 'Chưa có thí nghiệm BMC riêng với bound k trong đợt này. Không thay bằng kết quả TLC hay gọi duyệt graph hữu hạn là BMC.\n',
              '## Phụ lục E — Fuzzing và test\n',
              table(['Cấu hình campaign mới nhất','Giá trị'], [['Seed',fuzz['seed']],['Iterations mỗi campaign random',fuzz['iterations_per_random_campaign']],['Timeout giây',fuzz['timeout_seconds']],['Targets',', '.join(fuzz['targets'])],['Crash policy',fuzz['crash_policy']],['Branch coverage target',fuzz['coverage']['branch_percent']]]),
              'Corpus: ../../OrderPaymentMicroservice.Tests/FuzzCorpus/order_requests.json. Oracle thực thi: ../../OrderPaymentMicroservice.Tests/SecurityFuzzingTests.cs. Coverage XML: ../results/application-fuzzing-coverage.cobertura.xml. Campaign có random xác định, không chứng minh mọi input hoặc coverage-guided native fuzzing.\n',
              '## Phụ lục F — Evidence và truy vết\n',
              '### F.1. Kết quả từng test (lần chạy mới nhất)\n']
    restart_path = BASE / 'results/process_restart.results.json'
    if restart_path.exists():
        restart = read('results/process_restart.results.json')
        annex += ['### F.1a. Process-kill charge recovery\n',
                  table(['Chỉ số', 'Kết quả'], [['PASS', restart['passed']], ['Kill window', restart['kill_window']],
                        ['Recovered by', restart['recovered_by']], ['Charge records', restart['charge_records']],
                        ['AttemptCount', restart['attempt_count']], ['Inventory released', restart['inventory_released']],
                        ['Journal unchanged', restart['journal_unchanged']]]),
                  '[Evidence restart](../results/process_restart.results.json); [Hợp đồng hiện thực](../IMPLEMENTATION_CONTRACTS.md).\n']

    for suite_name,suite in tests['suites'].items():
        annex += [f'#### {suite_name}: {suite["passed"]}/{suite["total"]}\n',table(['Case','Outcome','Duration'], [[c['name'],c['outcome'],c['duration']] for c in suite['cases']])]
    annex += ['### F.2. Ma trận truy vết\n',
              'Bản CSV đầy đủ: [traceability_matrix.csv](../results/traceability_matrix.csv). Mỗi dòng có requirement, property, implementation/model, testcase, evidence, level và limitations.\n',
              '### F.3. Manifest và cách tái lập\n',
              'Manifest: [evidence_manifest.json](../results/evidence_manifest.json), gồm SHA-256, byte size, base commit, trạng thái dirty, SDK và phân loại fresh/historical. Hash đóng gói chỉ xác nhận bytes hiện tại. TRX gốc cục bộ không đưa vào source; hash và tên từng case được giữ trong application_tests.results.json.\n',
              '''Từ thư mục gốc, sau khi restore dependency và cài PostgreSQL binaries hoặc Docker:

```bash
dotnet test OrderPaymentMicroservice.slnx --no-restore --nologo --logger 'trx;LogFilePrefix=verification' --results-directory security-analysis/results/dotnet --blame-hang-timeout 60s
python3 security-analysis/package_evidence.py
python3 security-analysis/verify_recovery_model.py
python3 security-analysis/audit_liveness.py
python3 security-analysis/verify_threat_model.py
python3 security-analysis/verify_payment_smt.py
python3 security-analysis/verify_inventory_smt.py
python3 security-analysis/create_order_payment_spec.py
python3 security-analysis/verify_teacher_spec.py
python3 security-analysis/package_evidence.py
python3 security-analysis/build_report.py
python3 security-analysis/package_evidence.py
```

Không lệnh nào trong quy trình này đọc hay sửa ATPM. Generator giữ layout workbook
của chính project và xuất pipeline JSON vào security-analysis/.
''',
              '## Phụ lục G — Hạn chế và công việc còn mở\n',
              table(['Hạng mục','Trạng thái','Điều kiện nghiệm thu'], [
                  ['Gateway bền vững/sandbox','LOCAL_PROCESS_KILL_PASS / REAL_PROVIDER_OPEN','Local charge/refund recovery sau journal PASS; crash windows khác/provider thật còn mở'],
                  ['BMC có bound','OPEN','Ghi tool, k, assumption, verdict và witness'],
                  ['Công cụ Windows giảng viên','NOT_RUN_THIS_SESSION','Lưu manifest/log/export thực tế; checker độc lập không thay thế'],
                  ['Rate limiting/audit','PARTIALLY_MITIGATED','Admission/audit test PASS; distributed quota, external sink outbox, retention và DBA protection còn mở'],
                  ['API fuzz/network fault injection','OPEN','Target HTTP, corpus, oracle và campaign log'],
                  ['Migration dữ liệu cũ','NOT_TESTED','Kiểm tra dữ liệu vi phạm trước khi apply; không tự xóa/sửa'],
                  ['TLC','HISTORICAL','Giữ source hash/tool/run timestamp khi chạy lại'],
                  ['Model→implementation','PARTIAL','Traceability và test hỗ trợ; chưa có refinement proof'],
              ]),
              'INCONCLUSIVE, TOOL_ERROR, BUILD_ERROR hoặc SKIP không được chuyển thành PASS. Evidence thiếu phải được ghi rõ, không điền ID/hash giả.\n']
    (OUT / 'PHU_LUC_A_G.md').write_text(enrich_annex('\n'.join(annex)),encoding='utf-8')
    print(OUT / 'BAO_CAO.md');print(OUT / 'PHU_LUC_A_G.md')

if __name__ == '__main__':
    main()
