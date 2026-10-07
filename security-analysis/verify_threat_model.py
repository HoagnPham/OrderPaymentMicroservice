"""Validate traceability and scoring of the Chapter 1 threat model."""

import json
from pathlib import Path


BASE = Path(__file__).resolve().parent
OUT = BASE / "results"


def main():
    path = BASE / "threat_model.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    assets = {item["id"] for item in data["assets"]}
    boundaries = {item["id"] for item in data["trust_boundaries"]}
    threats = data["threats"]
    threat_ids = [item["id"] for item in threats]
    assert len(threat_ids) == len(set(threat_ids)), "Duplicate threat ID"
    assert len(assets) == len(data["assets"]), "Duplicate asset ID"
    assert len(boundaries) == len(data["trust_boundaries"]), "Duplicate trust-boundary ID"

    expected_stride = {
        "SPOOFING", "TAMPERING", "REPUDIATION", "INFORMATION_DISCLOSURE",
        "DENIAL_OF_SERVICE", "ELEVATION_OF_PRIVILEGE",
    }
    actual_stride = {item["stride"] for item in threats}
    assert actual_stride == expected_stride, f"STRIDE coverage mismatch: {actual_stride}"

    known_properties = {"P1", "P2", "P3", "P4"}
    recovery = json.loads(
        (BASE / "order_payment_recovery_model.json").read_text(encoding="utf-8")
    )
    known_properties |= {item["id"] for item in recovery["properties"]}
    cia_covered = set()
    open_threats = []
    for item in threats:
        assert item["boundary"] in boundaries, f"{item['id']}: unknown boundary"
        assert item["assets"] and set(item["assets"]) <= assets, f"{item['id']}: unknown/missing asset"
        assert 1 <= item["likelihood"] <= 5 and 1 <= item["impact"] <= 5, f"{item['id']}: risk input out of range"
        expected_score = item["likelihood"] * item["impact"]
        assert item["risk_score"] == expected_score, f"{item['id']}: risk score must be likelihood x impact"
        assert item["threat"] and item["vulnerability"] and item["attack"], f"{item['id']}: incomplete threat chain"
        assert item["controls"] and item["evidence"], f"{item['id']}: missing control/evidence"
        assert set(item["kripke_properties"]) <= known_properties, f"{item['id']}: unknown Kripke property"
        cia_covered.update(item["cia"])
        if item["status"].startswith("OPEN"):
            assert item.get("recommendation"), f"{item['id']}: open threat needs recommendation"
            open_threats.append(item["id"])
    assert cia_covered == {"CONFIDENTIALITY", "INTEGRITY", "AVAILABILITY"}, "CIA coverage is incomplete"

    result = {
        "passed": True,
        "assets": len(assets),
        "trust_boundaries": len(boundaries),
        "threats": len(threats),
        "stride_categories": sorted(actual_stride),
        "cia_properties": sorted(cia_covered),
        "open_threats": open_threats,
        "checks": [
            "unique identifiers",
            "valid asset and boundary references",
            "complete STRIDE and CIA coverage",
            "risk = likelihood x impact",
            "complete threat chain and evidence",
            "valid Kripke property references",
            "recommendation for open threats",
        ],
    }
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "threat_model_results.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(
        f"Threat model: PASS ({len(assets)} assets, {len(boundaries)} boundaries, "
        f"{len(threats)} threats, 6/6 STRIDE)"
    )
    return result


if __name__ == "__main__":
    main()
