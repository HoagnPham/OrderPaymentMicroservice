import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from atpm.ch1_concepts.kripke import KripkeStructure, KripkeStructureError, build_login_example


def test_login_example_finds_vulnerability():
    model = build_login_example()
    result = model.check_invariant("unsafe_no_auth")
    assert result["holds"] is False
    assert result["counterexample_path"][0] == "s0_khoi_dong"
    assert result["counterexample_path"][-1] == "s4_admin_bypass"


def test_reachability_admin():
    model = build_login_example()
    result = model.check_reachability("admin")
    assert result["holds"] is True
    assert "s3_admin_hop_le" in result["witness_states"]
    assert "s4_admin_bypass" in result["witness_states"]


def test_total_transition_relation_enforced():
    k = KripkeStructure()
    k.add_state("a")
    k.add_state("b")
    k.set_initial("a")
    k.add_transition("a", "b")
    # 'b' khong co chuyen tiep di ra -> vi pham tinh total
    try:
        k.validate_total()
        assert False, "Le ra phai nem KripkeStructureError"
    except KripkeStructureError:
        pass


def test_safe_variant_without_bypass():
    k = KripkeStructure()
    k.add_state("s0", labels=["init"])
    k.add_state("s1", labels=["waiting_auth"])
    k.add_state("s2", labels=["authenticated", "admin"])
    k.set_initial("s0")
    k.add_transition("s0", "s1")
    k.add_transition("s1", "s2")
    k.add_transition("s2", "s2")
    k.validate_total()
    result = k.check_invariant("unsafe_no_auth")
    assert result["holds"] is True
