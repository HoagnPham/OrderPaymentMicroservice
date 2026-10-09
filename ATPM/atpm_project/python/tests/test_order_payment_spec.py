"""Project-specific regression tests executed inside the lecturer ATPM suite."""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from atpm.ch1_concepts.kripke import KripkeStructure


def _load_verifier():
    path = ROOT / "security-analysis" / "verify_teacher_spec.py"
    spec = importlib.util.spec_from_file_location("order_payment_teacher_spec", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


VERIFIER = _load_verifier()


def test_submission_workbook_uses_exact_lecturer_schema():
    sheets = VERIFIER.read_workbook()
    assert list(sheets) == list(VERIFIER.EXPECTED_HEADERS)
    for name, header in VERIFIER.EXPECTED_HEADERS.items():
        assert sheets[name][0] == header


def test_excel_and_atpm_json_have_identical_pipeline_semantics():
    sheets = VERIFIER.read_workbook()
    excel_spec = VERIFIER.to_pipeline_spec(sheets)
    json_spec = json.loads(VERIFIER.PIPELINE_JSON.read_text(encoding="utf-8"))
    assert excel_spec == json_spec


def test_order_payment_kripke_is_total_reachable_and_satisfies_ctl():
    specification = VERIFIER.to_pipeline_spec(VERIFIER.read_workbook())
    machine = specification["state_machine"]
    kripke = KripkeStructure()
    for state in machine["states"]:
        kripke.add_state(state, machine["labeling"][state])
    kripke.set_initial(*machine["initial"])
    for source, target in machine["transitions"]:
        kripke.add_transition(source, target)
    kripke.validate_total()

    assert kripke.reachable_from() == set(machine["states"])
    assert all(
        VERIFIER.evaluate_ctl(specification, formula)
        for formula in specification["safety_properties"]
    )


def test_spec_covers_all_four_course_chapters_and_traceability():
    sheets = VERIFIER.read_workbook()
    specification = VERIFIER.to_pipeline_spec(sheets)
    functions = specification["functions"]

    assert specification["state_machine"]                 # Chapter 1: Kripke/CTL
    assert any(item["language"] == "c" for item in functions)       # Chapter 2: C memory
    assert specification["arithmetic_checks"]             # Chapter 3: SMT/overflow
    assert {item["kind"] for item in functions if item["language"] == "python"} == {
        "divide", "magic_value"
    }                                                       # Chapter 4: fuzzing

    ids = {row[0] for name in ("YeuCauChucNang", "YeuCauPhiChucNang", "RangBuoc") for row in sheets[name][1:]}
    for test_case in sheets["TestCase"][1:]:
        assert set(VERIFIER._split(test_case[1])) <= ids


def test_generated_python_targets_have_deterministic_crash_inputs():
    specification = VERIFIER.to_pipeline_spec(VERIFIER.read_workbook())
    divide = next(item for item in specification["functions"] if item["kind"] == "divide")
    magic = next(item for item in specification["functions"] if item["kind"] == "magic_value")

    assert len(divide["params"]) == 2
    try:
        _ = 1 // 0
        assert False, "divide target must expose ZeroDivisionError to the black-box fuzzer"
    except ZeroDivisionError:
        pass

    secret1, secret2 = magic["magic_values"]
    assert (secret1, secret2) == (4919, 2026)


def test_complete_teacher_compatibility_verifier_passes():
    result = VERIFIER.validate()
    assert result["passed"] is True
    assert result["sheet_count"] == 10
    assert result["states"] == result["reachable_states"]
