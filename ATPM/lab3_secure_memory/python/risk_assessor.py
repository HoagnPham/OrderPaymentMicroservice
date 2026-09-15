"""
Lab 3 - Cong cu Danh gia Rui ro (Risk Assessment) don gian hoa theo CVSS
=========================================================================
Chuong 2: 2.1 Danh gia rui ro (Risk Assessment)

QUAN TRONG: day la MO HINH GIAO DUC don gian hoa lay cam hung tu CVSS v3.1,
KHONG phai cong thuc CVSS chinh thuc (cong thuc that phuc tap hon nhieu va
can tra cuu bang chuyen doi chuan). Muc dich la giup sinh vien hieu NGUYEN
TAC: Risk = f(Exploitability, Impact), khong phai thay the cong cu CVSS
calculator that.

Cac buoc tinh diem:
  1. Exploitability sub-score: dua tren Attack Vector (AV), Attack Complexity
     (AC), Privileges Required (PR), User Interaction (UI).
  2. Impact sub-score: dua tren Confidentiality/Integrity/Availability (C/I/A).
  3. Base score = trung binh co trong so cua 2 sub-score, quy ve thang 0-10.
  4. Xep hang muc do nghiem trong (severity) theo thang CVSS chuan:
       0.0        -> NONE
       0.1 - 3.9  -> LOW
       4.0 - 6.9  -> MEDIUM
       7.0 - 8.9  -> HIGH
       9.0 - 10.0 -> CRITICAL
"""
from __future__ import annotations
import json
import os

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Trong so (weight) mo phong theo tinh than CVSS v3.1 (don gian hoa)
AV_WEIGHT = {"NETWORK": 0.85, "ADJACENT": 0.62, "LOCAL": 0.55, "PHYSICAL": 0.2}
AC_WEIGHT = {"LOW": 0.77, "HIGH": 0.44}
PR_WEIGHT = {"NONE": 0.85, "LOW": 0.62, "HIGH": 0.27}
UI_WEIGHT = {"NONE": 0.85, "REQUIRED": 0.62}
CIA_WEIGHT = {"NONE": 0.0, "LOW": 0.22, "HIGH": 0.56}


def exploitability_subscore(v: dict) -> float:
    av = AV_WEIGHT.get(v["attack_vector"], 0.5)
    ac = AC_WEIGHT.get(v["attack_complexity"], 0.5)
    pr = PR_WEIGHT.get(v["privileges_required"], 0.5)
    ui = UI_WEIGHT.get(v["user_interaction"], 0.5)
    # He so 8.22 tuong tu CVSS de dua ve thang gan 0-10 khi ket hop voi impact
    return 8.22 * av * ac * pr * ui


def impact_subscore(v: dict) -> float:
    c = CIA_WEIGHT.get(v["confidentiality_impact"], 0.0)
    i = CIA_WEIGHT.get(v["integrity_impact"], 0.0)
    a = CIA_WEIGHT.get(v["availability_impact"], 0.0)
    isc_base = 1 - ((1 - c) * (1 - i) * (1 - a))
    return 6.42 * isc_base


def base_score(v: dict) -> float:
    isc = impact_subscore(v)
    esc = exploitability_subscore(v)
    if isc <= 0:
        return 0.0
    score = min(isc + esc, 10.0)
    return round(score, 1)


def severity_label(score: float) -> str:
    if score == 0.0:
        return "NONE"
    if score < 4.0:
        return "LOW"
    if score < 7.0:
        return "MEDIUM"
    if score < 9.0:
        return "HIGH"
    return "CRITICAL"


def assess_all(dataset_path: str) -> list[dict]:
    with open(dataset_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    results = []
    for v in data["vulnerabilities"]:
        score = base_score(v)
        sev = severity_label(score)
        results.append({
            "id": v["id"], "cwe": v["cwe"], "name": v["name"],
            "exploitability_subscore": round(exploitability_subscore(v), 2),
            "impact_subscore": round(impact_subscore(v), 2),
            "base_score": score, "severity": sev,
            "detected_by": v["detected_by"],
        })
    # Sap xep theo diem rui ro giam dan -> uu tien khac phuc
    results.sort(key=lambda r: r["base_score"], reverse=True)
    return results


def print_report(results: list[dict]):
    print(f"{'ID':<10}{'CWE':<10}{'Ten loi':<38}{'Diem':<7}{'Muc do':<10}")
    print("-" * 80)
    for r in results:
        print(f"{r['id']:<10}{r['cwe']:<10}{r['name']:<38}{r['base_score']:<7}{r['severity']:<10}")
    print("\nThu tu uu tien khac phuc (theo diem rui ro giam dan):")
    for rank, r in enumerate(results, 1):
        print(f"  {rank}. [{r['severity']}] {r['id']} ({r['cwe']}) - {r['name']}")


if __name__ == "__main__":
    dataset_path = os.path.join(BASE, "dataset", "vulnerabilities.json")
    results = assess_all(dataset_path)
    print_report(results)

    out_path = os.path.join(BASE, "dataset", "risk_assessment_results.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2)
    print(f"\nKet qua da luu: {out_path}")
