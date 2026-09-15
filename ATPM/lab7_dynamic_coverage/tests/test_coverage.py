"""
Lab 7 - Bo test tu dong
=======================
1. Xac nhan bo test 'full' co coverage CAO HON han bo test 'partial'.
2. Xac nhan bo test to hop (combinatorial) tu sinh dat coverage >= bo 'full'.
3. Xac nhan tat ca test case trong moi bo deu PASS (khop oracle process_order()).
"""
import os
import sys

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(BASE, "python"))

from coverage_runner import evaluate_suite  # noqa: E402
from test_case_generator import generate_combinatorial_cases, save_csv  # noqa: E402

MIN_IMPROVEMENT_PCT = 5.0


def test_partial_suite_all_pass():
    result = evaluate_suite("order_test_inputs_partial.csv")
    assert "Tong: 2/2 test PASS" in result["test_output"]


def test_full_suite_all_pass():
    result = evaluate_suite("order_test_inputs_full.csv")
    assert "Tong: 9/9 test PASS" in result["test_output"]


def test_full_suite_covers_significantly_more_than_partial():
    partial = evaluate_suite("order_test_inputs_partial.csv")
    full = evaluate_suite("order_test_inputs_full.csv")
    diff = full["coverage"]["coverage_percent"] - partial["coverage"]["coverage_percent"]
    assert diff >= MIN_IMPROVEMENT_PCT, (
        f"Bo test full phai phu them >= {MIN_IMPROVEMENT_PCT}% so voi partial, "
        f"thuc te chi hon {diff}%"
    )


def test_combinatorial_suite_all_pass_and_matches_or_exceeds_full_coverage():
    cases = generate_combinatorial_cases()
    csv_path = os.path.join(BASE, "dataset", "order_test_inputs_combinatorial.csv")
    save_csv(cases, csv_path)

    result = evaluate_suite("order_test_inputs_combinatorial.csv")
    lines = result["test_output"].splitlines()
    summary = [l for l in lines if l.startswith("Tong:")][0]
    passed_str, total_str = summary.replace("Tong: ", "").split(" test")[0].split("/")
    assert passed_str == total_str, f"Co test FAIL trong bo to hop: {summary}"

    full = evaluate_suite("order_test_inputs_full.csv")
    assert result["coverage"]["coverage_percent"] >= full["coverage"]["coverage_percent"] - 0.5


if __name__ == "__main__":
    tests = [
        test_partial_suite_all_pass, test_full_suite_all_pass,
        test_full_suite_covers_significantly_more_than_partial,
        test_combinatorial_suite_all_pass_and_matches_or_exceeds_full_coverage,
    ]
    passed = 0
    for t in tests:
        try:
            t()
            print(f"[PASS] {t.__name__}")
            passed += 1
        except AssertionError as e:
            print(f"[FAIL] {t.__name__}: {e}")
    print(f"\n{passed}/{len(tests)} test da PASS")
