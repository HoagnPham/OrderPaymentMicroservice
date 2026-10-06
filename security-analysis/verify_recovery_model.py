"""Exhaustive checker for the bounded Order-Payment recovery model.

This is model verification, not an API test.  It explores every declared
transition, checks state predicates, validates incident-response completeness,
and injects documented negative mutations to require counterexamples.
"""
from __future__ import annotations

import copy
import csv
import json
from collections import deque
from pathlib import Path


BASE = Path(__file__).resolve().parent
MODEL_PATH = BASE / "order_payment_recovery_model.json"
RESULT_PATH = BASE / "results" / "order_payment_recovery_model.results.json"
MATRIX_PATH = BASE / "results" / "order_payment_state_response_matrix.csv"


def transitions(model: dict) -> list[dict]:
    edges: list[dict] = []
    for state in model["states"]:
        for item in state.get("normal", []):
            edges.append({"from": state["id"], "kind": "progress", **item})
        for item in state.get("incidents", []):
            edges.append({
                "from": state["id"],
                "event": item["event"],
                "to": item["to"],
                "kind": item.get("kind", "progress"),
                "action": item["solution"],
            })
    edges.extend(model.get("extra_transitions", []))
    return edges


def adjacency(model: dict, *, progress_only: bool = False) -> dict[str, list[str]]:
    graph = {state["id"]: [] for state in model["states"]}
    for edge in transitions(model):
        if not progress_only or edge.get("kind") != "stutter":
            graph[edge["from"]].append(edge["to"])
    return graph


def shortest_paths(model: dict, initial: str = "CREATED") -> dict[str, list[str]]:
    graph = adjacency(model)
    paths = {initial: [initial]}
    queue = deque([initial])
    while queue:
        current = queue.popleft()
        for target in graph[current]:
            if target not in paths:
                paths[target] = paths[current] + [target]
                queue.append(target)
    return paths


def property_result(prop: dict, model: dict, paths: dict[str, list[str]]) -> dict:
    states = {state["id"]: state for state in model["states"]}
    reachable = set(paths)
    failures: list[dict] = []

    if prop["type"] == "AG_NOT":
        for state_id in sorted(reachable):
            if prop["label"] in states[state_id]["labels"]:
                failures.append({"state": state_id, "witness": paths[state_id]})
    elif prop["type"] == "AG_IMPLIES":
        for state_id in sorted(reachable):
            labels = set(states[state_id]["labels"])
            if prop["if"] in labels and prop["then"] not in labels:
                failures.append({"state": state_id, "witness": paths[state_id]})
    elif prop["type"] == "AG_EXCLUDES":
        for state_id in sorted(reachable):
            labels = set(states[state_id]["labels"])
            if prop["left"] in labels and prop["right"] in labels:
                failures.append({"state": state_id, "witness": paths[state_id]})
    elif prop["type"] == "ALL_REACHABLE":
        for state_id in sorted(set(states) - reachable):
            failures.append({"state": state_id, "reason": "unreachable"})
    elif prop["type"] == "INCIDENT_COMPLETE":
        required = {"problem", "event", "detection", "solution", "to"}
        for state in states.values():
            if not state.get("incidents"):
                failures.append({"state": state["id"], "reason": "no incident documented"})
            for incident in state.get("incidents", []):
                missing = sorted(key for key in required if not incident.get(key))
                if missing:
                    failures.append({"state": state["id"], "event": incident.get("event"), "missing": missing})
                if incident.get("to") not in states:
                    failures.append({"state": state["id"], "event": incident.get("event"), "reason": "unknown target"})
    elif prop["type"] == "AF_TERMINAL_FAIR":
        graph = adjacency(model, progress_only=True)
        terminal = {state_id for state_id, state in states.items() if "terminal" in state["labels"]}
        good = set(terminal)
        while True:
            expanded = good | {
                state_id for state_id, successors in graph.items()
                if state_id not in terminal and successors and all(target in good for target in successors)
            }
            if expanded == good:
                break
            good = expanded
        for state_id in sorted(reachable - good):
            failures.append({"state": state_id, "witness": paths[state_id], "reason": "may avoid terminal on progress transitions"})
    else:
        raise ValueError(f"Unsupported property type: {prop['type']}")

    return {
        "id": prop["id"],
        "type": prop["type"],
        "description": prop["description"],
        "holds": not failures,
        "counterexamples": failures,
    }


def verify(model: dict) -> dict:
    state_ids = [state["id"] for state in model["states"]]
    if len(state_ids) != len(set(state_ids)):
        raise ValueError("Duplicate state id")
    known = set(state_ids)
    for edge in transitions(model):
        if edge["from"] not in known or edge["to"] not in known:
            raise ValueError(f"Transition references an unknown state: {edge}")

    paths = shortest_paths(model)
    results = [property_result(prop, model, paths) for prop in model["properties"]]
    return {
        "system_name": model["system_name"],
        "state_count": len(state_ids),
        "transition_count": len(transitions(model)),
        "reachable_states": sorted(paths),
        "properties": results,
        "all_hold": all(item["holds"] for item in results),
    }


def apply_negative_scenario(model: dict, scenario: dict) -> dict:
    mutated = copy.deepcopy(model)
    mutated["system_name"] = f"{model['system_name']} / {scenario['id']}"
    mutated["states"].append(copy.deepcopy(scenario["state"]))
    mutated.setdefault("extra_transitions", []).append({"kind": "progress", **scenario["transition"]})
    return mutated


def build_state_response_matrix(model: dict) -> list[dict]:
    """Flatten the model into a reviewable state/incident decision table."""
    rows: list[dict] = []
    for state in model["states"]:
        for transition in state.get("normal", []):
            rows.append({
                "state": state["id"],
                "order_status": state["order_status"],
                "payment_status": state["payment_status"],
                "case": "NORMAL",
                "event_or_problem": transition["event"],
                "detection": "Preconditions and invariants for the normal transition hold",
                "solution": transition["action"],
                "next_state": transition["to"],
                "kind": "progress",
            })
        for incident in state.get("incidents", []):
            rows.append({
                "state": state["id"],
                "order_status": state["order_status"],
                "payment_status": state["payment_status"],
                "case": "INCIDENT",
                "event_or_problem": incident["problem"],
                "detection": incident["detection"],
                "solution": incident["solution"],
                "next_state": incident["to"],
                "kind": incident.get("kind", "progress"),
            })
    return rows


def main() -> dict:
    model = json.loads(MODEL_PATH.read_text(encoding="utf-8"))
    correct = verify(model)
    if not correct["all_hold"]:
        failed = [item["id"] for item in correct["properties"] if not item["holds"]]
        raise AssertionError(f"Correct model failed: {failed}")

    negative_results = []
    for scenario in model["negative_scenarios"]:
        result = verify(apply_negative_scenario(model, scenario))
        failed = {item["id"] for item in result["properties"] if not item["holds"]}
        expected = set(scenario["expected_failed_properties"])
        if not expected <= failed:
            raise AssertionError(f"{scenario['id']} expected {sorted(expected)}, got {sorted(failed)}")
        witnesses = {
            item["id"]: item["counterexamples"]
            for item in result["properties"] if item["id"] in expected
        }
        negative_results.append({
            "id": scenario["id"],
            "description": scenario["description"],
            "expected_failed_properties": sorted(expected),
            "actual_failed_properties": sorted(failed),
            "witnesses": witnesses,
            "detected": True,
        })

    output = {
        "model": correct,
        "state_response_matrix": build_state_response_matrix(model),
        "negative_scenarios": negative_results,
        "interpretation": {
            "scope": "Exhaustive over the finite declared model only",
            "fairness": "AF_TERMINAL_FAIR removes only transitions explicitly marked stutter",
            "limitation": "A PASS does not prove the ASP.NET implementation conforms to this model"
        }
    }
    RESULT_PATH.parent.mkdir(parents=True, exist_ok=True)
    RESULT_PATH.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    matrix = output["state_response_matrix"]
    with MATRIX_PATH.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(matrix[0]))
        writer.writeheader()
        writer.writerows(matrix)

    print(f"Correct model: {correct['state_count']} states, {correct['transition_count']} transitions")
    for item in correct["properties"]:
        print(f"[{item['id']}] {'PASS' if item['holds'] else 'FAIL'} - {item['description']}")
    for item in negative_results:
        print(f"[{item['id']}] DETECTED - failed {', '.join(item['actual_failed_properties'])}")
    print(f"Result: {RESULT_PATH}")
    print(f"State response matrix: {MATRIX_PATH}")
    return output


if __name__ == "__main__":
    main()
