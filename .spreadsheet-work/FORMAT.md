# Dinh dang Dac ta Yeu cau — `dataset/specs/*.json`

Moi file `.json` trong thu muc nay la mot **dac ta yeu cau** ma
`core/spec_pipeline.py` co the nap va chay qua toan bo pipeline
`Specs -> Kripke -> Code -> CWE/CVE -> Ch1-4`. Tai lieu nay mo ta tung
truong (field) trong dinh dang, de sinh vien co the tu viet dac ta moi.

Nap thu mot dac ta bang lenh:

```bash
python3 -c "
from core.spec_pipeline import parse_specification_file, run_pipeline
spec = parse_specification_file('dataset/specs/<ten_file>.json')
print(run_pipeline(spec).to_text())
"
```

hoac mo GUI (`python3 main.py`) -> tab **Đặc tả -> Kripke -> Sinh mã -> CWE/CVE**.

> **Dung Excel thay vi JSON?** Xem `dataset/specs_excel/FORMAT_EXCEL.md`
> — cung mot dac ta, nhung dien qua file `.xlsx` voi moi sheet ung voi 1 loai
> noi dung (yeu cau chuc nang, phi chuc nang, rang buoc, test case, mo hinh
> trang thai, ham sinh ma, kiem tra so hoc). Ca hai dinh dang duoc chuyen ve
> **cung mot** doi tuong `Specification` nen pipeline chay giong het nhau.

## 1. Cac truong cap cao nhat

| Truong | Bat buoc | Kieu | Y nghia |
|---|---|---|---|
| `name` | co | string | Ten dac ta (dinh danh, khong dau/khoang trang khuyen dung) |
| `description` | khong | string | Mo ta ngan gon he thong dang dac ta |
| `requirements` | khong | list[Requirement] | Danh sach yeu cau bang ngon ngu tu nhien |
| `state_machine` | khong | dict hoac `null` | Mo hinh trang thai de sinh Kripke (Chuong 1) |
| `additional_flows` | khong | dict[string, dict] | Cac luong Kripke PHU (tuy chon) — xem muc 3.1 |
| `safety_properties` | khong | list[string] | Danh sach cong thuc CTL can kiem chung tren `state_machine` |
| `mutual_exclusion` | khong | dict hoac `null` | Cap menh de nguyen tu can kiem tra loai tru lan nhau |
| `functions` | khong | list[FunctionSpec] | Cac ham can SINH MA (C hoac Python) |
| `arithmetic_checks` | khong | list[ArithmeticCheck] | Cac kiem tra SMT/overflow bang Z3 (Chuong 3) |

Neu `state_machine` la `null`/vang mat, pipeline se **bo qua Chuong 1**
(chi canh bao, khong loi). Tuong tu, neu `functions`/`arithmetic_checks`
rong thi Chuong 2/3/4 tuong ung se khong co gi de chay.

## 2. `requirements[]` — Requirement

```json
{"id": "REQ-1", "text": "Mo ta yeu cau bang tieng Viet/Anh",
 "category": "safety", "priority": "HIGH"}
```

- `category`: mot trong `"safety" | "liveness" | "memory" | "functional"`
  (chi mang tinh tai lieu hoa, khong anh huong logic pipeline).
- `priority`: `"HIGH" | "MEDIUM" | "LOW"` (chi mang tinh tai lieu hoa).

## 3. `state_machine` — sinh mo hinh Kripke (Chuong 1)

Dung dung dinh dang ma `core.kripke.KripkeStructure.from_dict()` doc:

```json
{
  "states": ["s0", "s1", "s2"],
  "initial": ["s0"],
  "labeling": {"s0": ["safe"], "s1": ["p1"], "s2": ["p1", "p2", "violation"]},
  "transitions": [["s0", "s1"], ["s1", "s0"], ["s1", "s2"]]
}
```

- **Moi trang thai NEN co it nhat 1 dich chuyen di ra** — neu khong,
  `verify_specification()` se bao no la mot **deadlock state**.
- Nhan `"violation"` la quy uoc (khong bat buoc) — GUI to mau do cho
  trang thai co nhan nay khi ve do thi.

### `safety_properties[]`
Danh sach cong thuc CTL kiem tra tren mo hinh tren, cu phap ho tro:
`AG(p)`, `EF(p)`, `AF(p)`, `EG(p)`, `AX(p)`, `EX(p)`, va phu dinh don
gian `AG(!p)`. Vi du: `"AG(!violation)"`.

### `mutual_exclusion`
```json
{"atom1": "p1", "atom2": "p2"}
```
Kiem tra `AG(!(p1 & p2))` — hai menh de nguyen tu khong duoc cung dung
tai mot trang thai co the toi duoc tu trang thai khoi tao.

## 3.1. `additional_flows` — nhieu luong Kripke PHU (tach hinh rieng)

`state_machine` la mo hinh CHINH (thuong la he thong da KET HOP/product
automaton, dung de `verify_specification()` kiem chung). Khi he thong
gom NHIEU tien trinh/thanh phan doc lap, ban co the khai bao THEM cac
luong phu — thuong la TUNG THANH PHAN RIENG LE truoc khi ket hop — de
sinh vien de doi chieu:

```json
{
  "additional_flows": {
    "Tien trinh 1 (rieng le)": {
      "states": ["idle", "crit1"], "initial": ["idle"],
      "labeling": {"idle": ["safe"], "crit1": ["p1"]},
      "transitions": [["idle", "crit1"], ["crit1", "idle"]]
    },
    "Tien trinh 2 (rieng le)": { "...": "..." }
  }
}
```

Moi luong la MOT dict o dung dinh dang `KripkeStructure.from_dict()` doc
(giong `state_machine`). `spec_to_kripke_flows(spec)` (`core/spec_pipeline.py`)
tra ve mot `OrderedDict` gom `"Mo hinh chinh: <ten dac ta>"` (tu
`state_machine`) roi den tung luong trong `additional_flows`, THEO DUNG
THU TU khai bao — GUI ve MOI LUONG thanh 1 TAB HINH
RIENG, co tieu de rieng, thay vi don chung vao 1 so do chuyen trang thai
duy nhat. Truong nay hoan toan tuy chon va KHONG anh huong ket qua kiem
chung/pipeline (chi anh huong cach hien thi).

> Muon dung dinh dang nhieu-luong ngay trong `dataset/ch1_verification/`
> (ngoai pham vi mo hinh trang thai chinh)? Dung truong `"flows"` o cap cao nhat
> cua file — xem `core.kripke.flows_from_dict()`/`flows_from_json_file()`
> va cac file mau `mutex_two_process.json`, `traffic_light.json`,
> `producer_consumer.json`, `readers_writers.json`.

## 4. `functions[]` — FunctionSpec (sinh ma nguon, Chuong 2-4)

```json
{"name": "ten_ham", "language": "c" | "python", "kind": "...",
 "params": ["..."], "buffer_size": 16, "bug": "buffer-overflow" | null,
 "magic_values": [1234, 5678]}
```

| `language` | `kind` ho tro | Dung de |
|---|---|---|
| `"c"` | `"buffer_copy"` | Sinh ham sao chep chuoi vao buffer co dinh (`buffer_size`). `bug: "buffer-overflow"` dung `strcpy()` (khong an toan); `bug: null` dung `strncpy()` (an toan). |
| `"c"` | `"alloc_release"` | Sinh ham `malloc()`/`free()`. `bug` co the la `"memory-leak"`, `"double-free"`, `"use-after-free"`, hoac `null` (khong loi). |
| `"c"` | `"arithmetic"` | Sinh ham `int ten(int a, int b) { return a + b; }` tu `params`, dung de trich xuat AST (Chuong 3). |
| `"python"` | `"divide"` | Sinh `def ten(a, b): return a // b` — muc tieu cho **black-box fuzzer** (Chuong 4). |
| `"python"` | `"magic_value"` | Sinh ham co nhanh `if a == m1: if b == m2: raise ...` voi `magic_values: [m1, m2]` — muc tieu cho **concolic/white-box fuzzer** (Chuong 4). |

Cac ham C sinh ra deu duoc gop lai va quet bang
`core.memory_analyzer.check_c_source()` (Chuong 2) + trich xuat AST
bang `core.static_smt.extract_c_statements()` (Chuong 3). Cac ham
Python duoc bien dich an toan (chi cho phep mot tap con cu phap rat
han che) roi chay qua `blackbox_fuzz()`/`concolic_fuzz()` (Chuong 4).

## 5. `arithmetic_checks[]` — ArithmeticCheck (Chuong 3, dung Z3)

```json
{"name": "ten_kiem_tra", "int_vars": ["x", "y"],
 "condition": "x > 0 and x + y == 100",
 "overflow_expr": "x + y"}
```

- `condition` (tuy chon): mot **dieu kien logic** (so sanh/and/or) —
  duoc kiem tra tinh **thoa duoc** (SAT/UNSAT) bang
  `check_satisfiability()`.
- `overflow_expr` (tuy chon): mot **bieu thuc so hoc thuan tuy**
  (chi `+ - *`, KHONG duoc chua so sanh) — duoc kiem tra kha nang
  **tran so int32** bang `check_int32_overflow()`.
- Co the khai bao ca hai truong, hoac chi mot trong hai.

## 6. Ket qua: CWE/CVE va bao cao

Moi phat hien tu Chuong 1-4 (vi pham CTL, mutex, deadlock, cac loai
loi bo nho, tran so, crash khi fuzzing) deu duoc `run_pipeline()` anh
xa tu dong sang mot ma **CWE** trong `CWE_TABLE`
(`core/spec_pipeline.py`), kem theo vi du **CVE** minh hoa (neu co)
trong `ILLUSTRATIVE_CVE_TABLE`. Goi `report.to_text()` de in bao cao
day du theo tung chuong.

## 7. Cac dac ta mau co san

| File | Minh hoa |
|---|---|
| `session_manager_spec.json` | Mo hinh mutex CO LOI thiet ke + ham buffer bi tran (de sinh vien tap phat hien); co `additional_flows` (tung tien trinh rieng le) |
| `session_manager_fixed_spec.json` | Ban DA SUA cua tren — mo hinh khong con vi pham, buffer dung `strncpy` |
| `resource_pool_spec.json` | Tap trung vao Chuong 2-4: memory-leak, double-free, tran so int32, 2 muc tieu fuzzing (khong co `state_machine`) |
| `iot_gateway_spec.json` | Dac ta day du nhat — dung DU CA 4 chuong: 2 cong thuc CTL, mutex, ca 4 loai loi bo nho kinh dien, 1 kiem tra SAT + overflow, ca 2 ky thuat fuzzing; co `additional_flows` (worker chinh/worker log rieng le) |
| `auth_rate_limit_spec.json` | Mo hinh dang nhap co gioi han so lan thu (rate limiting/lockout): vong doi phien + co che khoa tam thoi, buffer overflow + double-free, kiem tra tran bo dem so lan thu, ca 2 ky thuat fuzzing; co `additional_flows` tach rieng vong lap xac thuc va co che khoa |

Sinh vien co the sao chep 1 trong 5 file tren lam diem bat dau, sua lai
`name`/`description`/`requirements`/`state_machine`/`functions` cho
bai tap cua minh, roi chay lai pipeline de xem bao cao CWE/CVE moi.
