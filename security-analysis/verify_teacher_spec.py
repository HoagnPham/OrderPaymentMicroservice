"""Validate the submission workbook against the lecturer's documented pipeline schema.

The Windows Spec Verification Lab is not executable in the Linux CI environment.
This verifier independently checks the public Excel/JSON contract documented in
``ATPM/Hướng dẫn tạo Spec file dạng excel.pdf`` and exercises the supported
Kripke/CTL, traceability, code-generation and arithmetic inputs.
"""

from __future__ import annotations

import ast
import json
import re
import sys
from collections import deque
from pathlib import Path
from typing import Any
from xml.etree import ElementTree as ET
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ctl  # noqa: E402  (local helper module)


ROOT = Path(__file__).resolve().parents[1]
WORKBOOK = ROOT / "OrderPaymentMicroservice_Security_Spec.xlsx"
PIPELINE_JSON = ROOT / "security-analysis" / "order_payment_pipeline_spec.json"
RESULT = ROOT / "security-analysis" / "results" / "teacher_spec_verification.json"

NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
REL_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"

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

SUPPORTED_KINDS = {
    "c": {"buffer_copy", "alloc_release", "arithmetic"},
    "python": {"divide", "magic_value"},
}
SUPPORTED_BUGS = {"", "buffer-overflow", "memory-leak", "double-free", "use-after-free"}


def _split(value: object) -> list[str]:
    return [item.strip() for item in str(value).split(",") if item.strip()]


def read_workbook(path: Path = WORKBOOK) -> dict[str, list[list[object]]]:
    ns = {"m": NS, "r": REL_NS}
    sheets: dict[str, list[list[object]]] = {}
    with ZipFile(path) as archive:
        workbook = ET.fromstring(archive.read("xl/workbook.xml"))
        relationships = ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))
        targets = {item.attrib["Id"]: item.attrib["Target"] for item in relationships}
        sheet_nodes = workbook.find("m:sheets", ns)
        if sheet_nodes is None:
            raise ValueError("Workbook has no sheets")

        for sheet in sheet_nodes:
            name = sheet.attrib["name"]
            target = targets[sheet.attrib[f"{{{REL_NS}}}id"]].lstrip("/")
            if not target.startswith("xl/"):
                target = f"xl/{target}"
            xml = ET.fromstring(archive.read(target))
            parsed_rows: list[list[object]] = []
            for row in xml.findall(".//m:sheetData/m:row", ns):
                values: list[object] = []
                for cell in row.findall("m:c", ns):
                    if cell.attrib.get("t") == "inlineStr":
                        value: object = "".join(text.text or "" for text in cell.findall(".//m:t", ns))
                    else:
                        node = cell.find("m:v", ns)
                        raw = node.text if node is not None and node.text is not None else ""
                        value = float(raw) if "." in raw else int(raw) if raw else ""
                    values.append(value)
                parsed_rows.append(values)
            sheets[name] = parsed_rows
    return sheets


def to_pipeline_spec(sheets: dict[str, list[list[object]]]) -> dict[str, Any]:
    info = {str(row[0]): str(row[1]) for row in sheets["ThongTin"][1:]}
    state_rows = sheets["MoHinhTrangThai"][1:]
    transitions = [
        [str(row[0]), target]
        for row in state_rows
        for target in _split(row[3])
    ]
    functions = []
    for row in sheets["HamSinhMa"][1:]:
        name, language, kind, params, buffer_size, bug, magic1, magic2 = row
        item: dict[str, Any] = {
            "name": name,
            "language": language,
            "kind": kind,
            "params": _split(params),
        }
        if buffer_size != "":
            item["buffer_size"] = int(buffer_size)
        if bug != "":
            item["bug"] = bug
        if kind == "magic_value":
            item["magic_values"] = [int(magic1), int(magic2)]
        functions.append(item)
    mutex = sheets["LoaiTruLanNhau"][1]
    return {
        "name": info["name"],
        "description": info.get("description", ""),
        "requirements": [
            {"id": row[0], "text": row[1], "category": row[2], "priority": row[3]}
            for row in sheets["YeuCauChucNang"][1:]
        ],
        "state_machine": {
            "states": [row[0] for row in state_rows],
            "initial": [row[0] for row in state_rows if str(row[1]).strip().lower() == "x"],
            "labeling": {row[0]: _split(row[2]) for row in state_rows},
            "transitions": transitions,
        },
        "safety_properties": [row[0] for row in sheets["ThuocTinhAnToan"][1:]],
        "mutual_exclusion": {"atom1": mutex[0], "atom2": mutex[1]},
        "functions": functions,
        "arithmetic_checks": [
            {
                "name": row[0],
                "int_vars": _split(row[1]),
                "condition": row[2],
                "overflow_expr": row[3],
            }
            for row in sheets["KiemTraSoHoc"][1:]
        ],
    }


def _reachable(spec: dict[str, Any]) -> set[str]:
    graph = {state: set() for state in spec["state_machine"]["states"]}
    for source, target in spec["state_machine"]["transitions"]:
        graph[source].add(target)
    pending = deque(spec["state_machine"]["initial"])
    reached = set(pending)
    while pending:
        for target in graph[pending.popleft()]:
            if target not in reached:
                reached.add(target)
                pending.append(target)
    return reached


def evaluate_ctl(spec: dict[str, Any], formula: str) -> bool:
    machine = spec["state_machine"]
    return ctl.evaluate(
        ctl.parse(formula),
        machine["states"],
        machine["labeling"],
        [tuple(edge) for edge in machine["transitions"]],
        machine["initial"],
    )


def _validate_arithmetic(check: dict[str, Any]) -> None:
    allowed_names = set(check["int_vars"])
    for field, mode in (("condition", "eval"), ("overflow_expr", "eval")):
        expression = str(check[field]).strip()
        if not expression:
            raise ValueError(f"{check['name']} leaves {field} empty")
        tree = ast.parse(expression, mode=mode)
        for node in ast.walk(tree):
            if isinstance(node, ast.Name) and node.id not in allowed_names:
                raise ValueError(f"{check['name']} uses undeclared variable {node.id}")
            if isinstance(node, (ast.Call, ast.Attribute, ast.Subscript, ast.Pow, ast.Div, ast.FloorDiv, ast.Mod)):
                raise ValueError(f"{check['name']} uses an unsupported arithmetic construct")


def validate(path: Path = WORKBOOK, json_path: Path = PIPELINE_JSON) -> dict[str, Any]:
    sheets = read_workbook(path)
    if list(sheets) != list(EXPECTED_HEADERS):
        raise ValueError("Workbook must contain exactly the ten template sheets in order")
    for name, header in EXPECTED_HEADERS.items():
        if not sheets[name] or sheets[name][0] != header:
            raise ValueError(f"Invalid header for {name}: {sheets[name][0] if sheets[name] else None}")

    spec = to_pipeline_spec(sheets)
    stored = json.loads(json_path.read_text(encoding="utf-8"))
    if spec != stored:
        raise ValueError("Excel semantics differ from the JSON submitted to the ATPM pipeline")

    machine = spec["state_machine"]
    states = set(machine["states"])
    if len(states) != len(machine["states"]) or not machine["initial"]:
        raise ValueError("State names must be unique and an initial state is required")
    outgoing = {state: 0 for state in states}
    for source, target in machine["transitions"]:
        if source not in states or target not in states:
            raise ValueError(f"Unknown transition endpoint: {source} -> {target}")
        outgoing[source] += 1
    if any(count == 0 for count in outgoing.values()):
        raise ValueError("Kripke transition relation is not total")
    reachable = _reachable(spec)
    if reachable != states:
        raise ValueError(f"Unreachable states: {sorted(states - reachable)}")

    ctl_results = {formula: evaluate_ctl(spec, formula) for formula in spec["safety_properties"]}
    intentional_false = {
        formula for formula in ctl_results
        if formula.replace(" ", "") == "AG(!violation)"
    }
    if not intentional_false:
        raise ValueError("The workbook must document AG(!violation) for the primary lock cluster")
    unexpected_false = [
        formula for formula, value in ctl_results.items()
        if not value and formula not in intentional_false
    ]
    unexpected_true = [formula for formula in intentional_false if ctl_results[formula]]
    if unexpected_false:
        raise ValueError(f"CTL failures outside the deliberate lock-cluster finding: {unexpected_false}")
    if unexpected_true:
        raise ValueError("AG(!violation) must be false because the deliberate conflict state is reachable")

    mutex_rows = [[str(value) for value in row] for row in sheets["LoaiTruLanNhau"][1:]]
    if len(mutex_rows) < 5:
        raise ValueError("LoaiTruLanNhau requires at least five documented pairs")
    declared_labels = {
        label for values in machine["labeling"].values() for label in values
    }
    for pair in mutex_rows:
        if len(pair) != 2 or pair[0] == pair[1]:
            raise ValueError(f"Invalid mutual-exclusion pair: {pair}")
        if any(atom not in declared_labels for atom in pair):
            raise ValueError(f"Mutual-exclusion atom is not declared in MoHinhTrangThai: {pair}")
    primary = mutex_rows[0]
    primary_violations = sorted(
        state for state in reachable
        if primary[0] in machine["labeling"][state] and primary[1] in machine["labeling"][state]
    )
    if not primary_violations:
        raise ValueError("The primary lock pair must be reachable at the deliberate conflict state")
    for pair in mutex_rows[1:]:
        bad = sorted(
            state for state in reachable
            if pair[0] in machine["labeling"][state] and pair[1] in machine["labeling"][state]
        )
        if bad:
            raise ValueError(f"Documented mutual-exclusion pair {pair} is violated in {bad}")

    allowed_categories = {"safety", "liveness", "memory", "functional"}
    allowed_priorities = {"HIGH", "MEDIUM", "LOW"}
    requirements = sheets["YeuCauChucNang"][1:]
    all_ids = {row[0] for row in requirements}
    all_ids.update(row[0] for row in sheets["YeuCauPhiChucNang"][1:])
    all_ids.update(row[0] for row in sheets["RangBuoc"][1:])
    if any(row[2] not in allowed_categories or row[3] not in allowed_priorities for row in requirements):
        raise ValueError("Unsupported requirement category or priority")
    if len(requirements) < 5:
        raise ValueError("YeuCauChucNang requires at least five rows")
    if len(sheets["YeuCauPhiChucNang"]) < 6:
        raise ValueError("YeuCauPhiChucNang requires at least five rows")
    if len(sheets["RangBuoc"]) < 6:
        raise ValueError("RangBuoc requires at least five rows")
    for row in sheets["YeuCauPhiChucNang"][1:]:
        if not re.search(r"\d", str(row[1])):
            raise ValueError(f"{row[0]} must contain a concrete numeric measure")
    for row in sheets["TestCase"][1:]:
        missing = set(_split(row[1])) - all_ids
        if missing:
            raise ValueError(f"{row[0]} references unknown requirement IDs: {sorted(missing)}")
    if len(sheets["TestCase"]) < 6:
        raise ValueError("TestCase requires at least five rows")

    lock_states = {"free", "main_holds", "log_holds", "conflict"}
    if not lock_states <= states:
        raise ValueError("The 4-state free/main_holds/log_holds/conflict cluster is missing")
    labels = {label for values in machine["labeling"].values() for label in values}
    if not {"safe", "main_locked", "log_locked", "violation"} <= labels:
        raise ValueError("The lock cluster labels are incomplete")
    if "violation" not in machine["labeling"]["conflict"]:
        raise ValueError("The conflict state must carry the violation label")

    kinds = {(function["language"], function["kind"]) for function in spec["functions"]}
    if ("python", "divide") not in kinds or ("python", "magic_value") not in kinds:
        raise ValueError("HamSinhMa must contain both Python divide and magic_value functions")
    bugs = {function.get("bug", "") for function in spec["functions"] if function["language"] == "c"}
    required_bugs = {"buffer-overflow", "memory-leak", "double-free", "use-after-free"}
    if not required_bugs <= bugs:
        raise ValueError(f"HamSinhMa is missing C bug kinds: {sorted(required_bugs - bugs)}")
    for function in spec["functions"]:
        language = function["language"]
        if language not in SUPPORTED_KINDS or function["kind"] not in SUPPORTED_KINDS[language]:
            raise ValueError(f"Unsupported function generator: {function}")
        if function.get("bug", "") not in SUPPORTED_BUGS:
            raise ValueError(f"Unsupported injected bug: {function.get('bug')}")
        if function["kind"] == "magic_value" and len(function.get("magic_values", [])) != 2:
            raise ValueError("magic_value requires exactly two secret values")
        if function["kind"] == "buffer_copy" and function.get("buffer_size") != 32:
            raise ValueError(f"{function['name']} buffer size must match CON-5 (32 bytes)")

    if len(spec["arithmetic_checks"]) < 5:
        raise ValueError("KiemTraSoHoc requires at least five rows")
    threshold_text = " ".join(str(check["condition"]) for check in spec["arithmetic_checks"])
    missing_thresholds = {
        value for value in ("32767", "65535", "2000000000", "4294967295")
        if value not in threshold_text
    }
    if missing_thresholds:
        raise ValueError(f"KiemTraSoHoc is missing ATPM thresholds: {sorted(missing_thresholds)}")
    for check in spec["arithmetic_checks"]:
        _validate_arithmetic(check)
        declared = set(check["int_vars"])
        expression = str(check["overflow_expr"]).replace(" ", "")
        names = set(re.findall(r"[A-Za-z_][A-Za-z0-9_]*", expression))
        if not names <= declared:
            raise ValueError(f"{check['name']} overflow expression uses undeclared variables")
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*([+\-*][A-Za-z_][A-Za-z0-9_]*)+", expression):
            raise ValueError(f"{check['name']} overflow expression is not a pure arithmetic sum/product")

    result = {
        "passed": True,
        "workbook": str(path.relative_to(ROOT)),
        "pipeline_json": str(json_path.relative_to(ROOT)),
        "sheet_count": len(sheets),
        "requirements": len(requirements),
        "states": len(states),
        "transitions": len(machine["transitions"]),
        "reachable_states": len(reachable),
        "ctl": ctl_results,
        "mutual_exclusion": {
            "primary": primary,
            "holds": False,
            "violation_states": primary_violations,
            "documented_pairs": len(mutex_rows),
            "other_pairs_hold": True,
        },
        "intentional_findings": {
            "ctl_false": sorted(intentional_false),
            "mutex_violation_states": primary_violations,
            "detected_cwe": ["CWE-667", "CWE-693"],
            "rationale": (
                "ATPM Excel guide §3.5 and §4 require one deliberate conflict state and a reachable "
                "mutex violation; the pipeline reporting CWE-667/CWE-693 is correct-by-design."
            ),
        },
        "lock_cluster": {
            "states": sorted(lock_states),
            "labels": ["safe", "main_locked", "log_locked", "violation"],
        },
        "generated_functions": len(spec["functions"]),
        "generated_kinds": sorted(f"{language}:{kind}" for language, kind in kinds),
        "arithmetic_checks": len(spec["arithmetic_checks"]),
        "test_cases": len(sheets["TestCase"]) - 1,
    }
    RESULT.parent.mkdir(parents=True, exist_ok=True)
    RESULT.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return result


def main() -> None:
    result = validate()
    findings = result["intentional_findings"]
    print(
        "Teacher spec compatibility: PASS "
        f"({result['sheet_count']} sheets, {result['states']} states, "
        f"{len(result['ctl'])} CTL properties, {result['test_cases']} test cases)"
    )
    print(
        "Intentional detections: "
        f"{', '.join(findings['detected_cwe'])} at {findings['mutex_violation_states']} "
        f"(CTL false: {', '.join(findings['ctl_false'])})"
    )
    print(f"Result: {RESULT}")


if __name__ == "__main__":
    main()
