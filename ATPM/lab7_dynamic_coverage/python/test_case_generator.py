"""
Lab 7 - Sinh Test Case Tu dong theo To hop Nhanh (Combinatorial Branch Coverage)
===================================================================================
Chuong 4: 4.4 Tao duong thuc thi chuong trinh

Ky thuat: liet ke cac "lop tuong duong" (equivalence classes) cho tung
tham so dau vao, sau do sinh TICH DECARTES (Cartesian product) co chon loc
giua cac lop de dam bao MOI TO HOP NHANH (branch combination) quan trong
deu duoc it nhat 1 test case dai dien - day chinh la each "duong thuc thi"
(execution path) can duoc kiem thu theo tinh than 4.4.
"""
from __future__ import annotations
import csv
import os

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Lop tuong duong cho tung tham so (dai dien + expected_status_hint)
QUANTITY_CLASSES = [
    ("invalid_low", 0),
    ("invalid_high", 101),
    ("valid_low", 5),
    ("valid_member_threshold", 10),
    ("valid_nonmember_threshold", 20),
    ("valid_max", 100),
]
PRICE_CLASSES = [
    ("invalid_zero", 0),
    ("invalid_negative", -1),
    ("valid_low", 50000),
    ("valid_high_triggers_approval", 20000000),
]
MEMBER_CLASSES = [0, 1]


def expected_status(qty: int, price: float, is_member: int) -> str:
    """Mo phong lai dac ta cua process_order() (oracle doc lap voi C code,
    dung de sinh nhan mong doi cho test case moi)."""
    if qty <= 0 or qty > 100:
        return "INVALID_QTY"
    if price <= 0:
        return "INVALID_PRICE"
    if is_member:
        discount = 0.15 if qty >= 10 else 0.05
    else:
        discount = 0.10 if qty >= 20 else 0.0
    total = qty * price * (1 - discount)
    if total > 10_000_000:
        return "APPROVAL_REQUIRED"
    return "OK"


def generate_combinatorial_cases() -> list[dict]:
    cases = []
    for qlabel, qty in QUANTITY_CLASSES:
        for plabel, price in PRICE_CLASSES:
            for member in MEMBER_CLASSES:
                status = expected_status(qty, price, member)
                cases.append({
                    "quantity": qty, "unit_price": price, "is_member": member,
                    "expected_status": status,
                    "equivalence_classes": f"qty={qlabel}, price={plabel}, member={member}",
                })
    return cases


def save_csv(cases: list[dict], path: str):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["quantity", "unit_price", "is_member", "expected_status"])
        for c in cases:
            w.writerow([c["quantity"], c["unit_price"], c["is_member"], c["expected_status"]])


if __name__ == "__main__":
    cases = generate_combinatorial_cases()
    out_path = os.path.join(BASE, "dataset", "order_test_inputs_combinatorial.csv")
    save_csv(cases, out_path)
    print(f"Da sinh {len(cases)} test case to hop (6 x 4 x 2 lop tuong duong).")
    for c in cases:
        print(f"  [{c['equivalence_classes']}] -> {c['expected_status']}")
    print(f"\nDa luu: {out_path}")
