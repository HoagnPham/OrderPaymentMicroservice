# Dinh dang Dac ta Yeu cau bang Excel — `dataset/specs_excel/*.xlsx`

Day la **phien ban thay the** cho dinh dang JSON mo ta trong
`dataset/specs/FORMAT.md`. Ca hai dinh dang deu duoc chuyen doi ve **cung
mot** doi tuong `core.spec_pipeline.Specification`, nen toan bo pipeline
(`Specs -> Kripke -> Code -> CWE/CVE -> Ch1-4`) chay **giong het nhau** du
ban nap dac ta tu `.json` hay `.xlsx`.

Nap 1 file Excel bang lenh:

```bash
python3 -c "
from core.spec_excel import parse_specification_excel
from core.spec_pipeline import run_pipeline
spec = parse_specification_excel('dataset/specs_excel/<ten_file>.xlsx')
print(run_pipeline(spec).to_text())
"
```

hoac `python3 -m core.spec_excel dataset/specs_excel/<ten_file>.xlsx`,
hoac mo GUI (`python3 main.py`) -> tab **Đặc tả -> Kripke -> Sinh mã -> CWE/CVE** -> muc "Bo dac ta mau Excel".

Can cai `openpyxl` truoc khi dung (`pip install -r requirements.txt`).

## Tai sao dung Excel?

Dac ta JSON gon nhe nhung kho doc/viet doi voi nguoi chua quen JSON, va
khong co cho danh rieng cho **yeu cau phi chuc nang**, **rang buoc**, hay
**test case truy vet** — nhung noi dung quen thuoc trong quy trinh
Requirements Engineering (IEEE 830 / IEEE 29148) va tai lieu kiem thu
(IEEE 829). Template Excel danh **moi sheet cho mot LOAI noi dung dac ta**,
giup sinh vien dien du lieu bang bang tinh quen thuoc thay vi viet JSON tay.

## 1. Danh sach sheet

| Sheet | Bat buoc | Noi dung |
|---|---|---|
| `ThongTin` | **co** | Ten (`name`) va mo ta (`description`) chung cua he thong |
| `YeuCauChucNang` | khong | Yeu cau **chuc nang** (functional) va cac yeu cau hinh thuc lien quan (an toan/song con/bo nho) |
| `YeuCauPhiChucNang` | khong | Yeu cau **phi chuc nang** (non-functional): hieu nang, do tin cay, kha nang mo rong, v.v. |
| `RangBuoc` | khong | **Rang buoc** (constraint) ve cong nghe, thiet ke, tai nguyen ap dung cho he thong |
| `MoHinhTrangThai` | khong | Mo hinh trang thai (state machine) de sinh Kripke — Chuong 1 |
| `ThuocTinhAnToan` | khong | Danh sach cong thuc CTL can kiem chung tren mo hinh trang thai |
| `LoaiTruLanNhau` | khong | Cap menh de nguyen tu can kiem tra loai tru lan nhau |
| `HamSinhMa` | khong | Cac ham can SINH MA (C hoac Python) — Chuong 2-4 |
| `KiemTraSoHoc` | khong | Cac kiem tra SMT/overflow bang Z3 — Chuong 3 |
| `TestCase` | khong | **Test case** truy vet tu yeu cau (chi tai lieu hoa, khong chay tu dong) |

Neu thieu mot sheet (ngoai `ThongTin`), phan tuong ung cua dac ta duoc coi la
rong/vang mat — giong het hanh vi cua truong JSON tuong ung (vi du: khong co
sheet `MoHinhTrangThai` -> `state_machine = None` -> bo qua kiem chung
Chuong 1, chi canh bao, khong loi).

## 2. Sheet `ThongTin`

Bang 2 cot, moi dong la 1 cap (Truong, GiaTri):

| Truong | GiaTri |
|---|---|
| `name` | `session_manager` |
| `description` | Mo ta he thong bang 1-2 cau |

`name` la **bat buoc**. Neu thieu, pipeline bao loi giong het khi JSON
thieu truong `name`.

## 3. Sheet `YeuCauChucNang` — Yeu cau chuc nang

| ID | MoTa | DanhMuc | MucUuTien |
|---|---|---|---|
| REQ-1 | He thong khong duoc de 2 tien trinh cung vao vung tranh chap | safety | HIGH |
| REQ-8 | Ham chia ty le token khong duoc gay loi khong kiem soat | functional | MEDIUM |

- `DanhMuc` (tuy chon): `safety` \| `liveness` \| `memory` \| `functional`
  (mac dinh `functional` neu de trong). Day la lop tuong thich voi 4 pham
  tru dac ta hinh thuc goc cua JSON (`Requirement.category`) — cho phep
  ban viet ca yeu cau chuc nang thuan tuy LAN yeu cau lien quan truc tiep
  toi kiem chung Kripke/CTL (Chuong 1) hay bo nho (Chuong 2) trong cung
  1 sheet.
- `MucUuTien`: `HIGH` \| `MEDIUM` \| `LOW` (chi tai lieu hoa).

## 4. Sheet `YeuCauPhiChucNang` — Yeu cau phi chuc nang

| ID | MoTa | MucUuTien |
|---|---|---|
| NFR-1 | Ham xu ly phai tra ve trong vong 10ms voi dau vao <= 256 ky tu | MEDIUM |

Dung de dac ta cac thuoc tinh chat luong (hieu nang, do tin cay, kha nang
bao tri, kha nang mo rong, ...) khong the kiem chung truc tiep bang cac ky
thuat hinh thuc/tinh cua khung (Chuong 1-4), nhung can duoc ghi lai de
lam tai lieu dac ta day du va lam co so viet test case thu cong.

## 5. Sheet `RangBuoc` — Rang buoc

| ID | MoTa | MucUuTien |
|---|---|---|
| CON-1 | Chi duoc dung thu vien chuan C (khong phu thuoc thu vien ngoai) | HIGH |

Rang buoc ve cong nghe/thiet ke/tai nguyen (vd: ngon ngu bat buoc, kich
thuoc bo nho co dinh, khong duoc dung thu vien ngoai, gioi han phan cung...).

## 6. Sheet `MoHinhTrangThai` — Mo hinh trang thai (Chuong 1)

Moi dong la 1 trang thai:

| TrangThai | KhoiTao | Nhan | ChuyenDenCacTrangThai |
|---|---|---|---|
| s0 | x | safe | s1 |
| s1 | | p1 | s0, s2 |
| s2 | | p1, p2, violation | s0 |

- `KhoiTao`: danh dau `x` neu day la trang thai khoi tao (co the co nhieu
  trang thai khoi tao — danh dau `x` o nhieu dong).
- `Nhan`: danh sach nhan (atomic propositions) phan cach boi dau phay.
- `ChuyenDenCacTrangThai`: danh sach cac trang thai co the chuyen toi TU
  trang thai nay, phan cach boi dau phay (moi gia tri sinh ra 1 cap chuyen
  tiep `(TrangThai, dich)` trong `transitions`).
- **Moi trang thai NEN co it nhat 1 dich chuyen di ra** — neu khong,
  `verify_specification()` se bao no la mot deadlock state (giong JSON).
- Nhan `"violation"` la quy uoc (khong bat buoc) — to mau do khi ve do thi.

## 7. Sheet `ThuocTinhAnToan` — Cong thuc CTL

Moi dong 1 cong thuc, 1 cot `CongThucCTL`:

| CongThucCTL |
|---|
| AG(!violation) |
| EF(p1) |

Cu phap ho tro giong JSON: `AG(p)`, `EF(p)`, `AF(p)`, `EG(p)`, `AX(p)`,
`EX(p)`, va phu dinh don gian `AG(!p)`.

## 8. Sheet `LoaiTruLanNhau` — Mutual exclusion

Toi da 1 dong du lieu:

| MenhDe1 | MenhDe2 |
|---|---|
| p1 | p2 |

Tuong duong `mutual_exclusion: {"atom1": "p1", "atom2": "p2"}` trong JSON.
De trong (khong co dong nao) neu khong can kiem tra.

## 9. Sheet `HamSinhMa` — Sinh ma nguon (Chuong 2-4)

| Ten | NgonNgu | Loai | ThamSo | KichThuocBuffer | Loi | GiaTriBiMat1 | GiaTriBiMat2 |
|---|---|---|---|---|---|---|---|
| handle_input | c | buffer_copy | user_input | 16 | buffer-overflow | | |
| risky_divide | python | divide | a, b | | | | |
| check_magic | python | magic_value | x, y | | | 1234 | 5678 |

- `NgonNgu`: `c` \| `python`.
- `Loai` (`kind`): xem bang day du trong `dataset/specs/FORMAT.md` muc
  4 — vd `buffer_copy`, `alloc_release`, `arithmetic` (C); `divide`,
  `magic_value` (Python).
- `ThamSo`: danh sach tham so ham, phan cach boi dau phay (de trong de
  dung mac dinh).
- `KichThuocBuffer`: chi dung cho `buffer_copy`/`alloc_release` (mac dinh
  16 neu de trong).
- `Loi` (`bug`): de trong (khong tiem loi) hoac mot trong `buffer-overflow`
  \| `memory-leak` \| `double-free` \| `use-after-free` (chi C).
- `GiaTriBiMat1`/`GiaTriBiMat2`: chi dung cho `kind = magic_value`
  (Python) — tuong ung `magic_values: [m1, m2]` trong JSON.

## 10. Sheet `KiemTraSoHoc` — Kiem tra SMT/Z3 (Chuong 3)

| Ten | BienSoNguyen | DieuKien | BieuThucTran |
|---|---|---|---|
| counter_sum | x, y | x > 0 and x + y == 100 | x + y |

- `BienSoNguyen`: danh sach bien nguyen, phan cach boi dau phay.
- `DieuKien` (tuy chon): dieu kien logic kiem tra SAT/UNSAT.
- `BieuThucTran` (tuy chon): bieu thuc so hoc thuan tuy (`+ - *`) kiem tra
  tran so int32. Co the khai bao ca hai hoac chi mot trong hai, giong JSON.

## 11. Sheet `TestCase` — Test case truy vet

| ID | YeuCauLienQuan | TieuDe | DieuKienTienQuyet | CacBuoc | KetQuaMongDoi | MucUuTien |
|---|---|---|---|---|---|---|
| TC-1 | REQ-1, REQ-2 | Hai tien trinh cung xin vao vung tranh chap | He thong o trang thai idle | Buoc 1: ...\nBuoc 2: ... | AG(!violation) phai DUNG | HIGH |

- `YeuCauLienQuan`: danh sach ID yeu cau (tu sheet `YeuCauChucNang`,
  `YeuCauPhiChucNang`, hoac `RangBuoc`) ma test case nay **truy vet toi**
  (requirement traceability) — phan cach boi dau phay.
- `CacBuoc`: moi buoc kiem thu tren **1 dong** trong cung 1 o Excel (xuong
  dong bang Alt+Enter khi nhap lieu).
- Sheet nay **chi mang tinh tai lieu hoa/bao cao**: `run_pipeline()` KHONG
  tu dong thuc thi cac test case nay — muc dich la ren luyen ky nang viet
  test case truy vet tu dac ta yeu cau, mot buoc quan trong cua quy trinh
  Requirements Engineering va lien he truc tiep toi cau truc tai lieu
  kiem thu IEEE 829 duoc gioi thieu trong bai giang Chuong 8 (CSE703184).
  Bao cao pipeline (`report.to_text()`) van liet ke lai day du cac test
  case nay o phan "[Test Case] Truy vết từ đặc tả".

## 12. Cac dac ta mau co san

| File | Minh hoa |
|---|---|
| `session_manager_spec.xlsx` | Tuong duong `session_manager_spec.json`: mo hinh mutex CO LOI + ham buffer bi tran, bo sung 2 yeu cau phi chuc nang, 2 rang buoc, 3 test case truy vet |
| `resource_pool_spec.xlsx` | Tuong duong `resource_pool_spec.json`: tap trung Chuong 2-4 (memory-leak, double-free, tran so, 2 muc tieu fuzzing), khong co state machine, bo sung yeu cau phi chuc nang/rang buoc/4 test case |
| `iot_gateway_spec.xlsx` | Tuong duong `iot_gateway_spec.json`: dac ta day du nhat, dung ca 4 chuong dau, bo sung 3 yeu cau phi chuc nang, 3 rang buoc, 6 test case truy vet |

Ca 3 file da duoc kiem chung: chay `run_pipeline()` cho ra **dung so luong
phat hien CWE/CVE** nhu ban JSON tuong ung (3 / 5 / 9 phat hien).

Sinh vien co the sao chep 1 trong 3 file tren lam diem bat dau, sua lai cac
sheet cho bai tap cua minh, roi nap lai qua GUI hoac dong lenh de xem bao
cao CWE/CVE moi — hoac dung `core.spec_excel.export_specification_to_excel()`
de xuat mot dac ta JSON da co san sang dinh dang Excel de chinh sua.
