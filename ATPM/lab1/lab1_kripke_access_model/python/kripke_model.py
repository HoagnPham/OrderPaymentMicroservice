"""
Lab 1 - Mo hinh hoa An toan Phan mem bang Cau truc Kripke (Kripke Structure)
============================================================================
Chu de xuyen suot du an: SecureSys - He thong Quan ly Truy cap va Giao dich An toan
Module nay xu ly module con: ACCESS CONTROL (kiem soat truy cap cua ra vao).

Noi dung ly thuyet lien quan (Chuong 1 - de cuong CSE703093):
  1.1 Cac khai niem ve an toan phan mem
  1.2 Mot so van de ve an toan phan mem
  1.5 Cau truc Kripke (Kripke Structure)

Mot cau truc Kripke la mot bo M = (S, S0, R, L) trong do:
  - S  : tap huu han cac trang thai (states)
  - S0 : tap trang thai khoi tao (initial states), S0 con S
  - R  : quan he chuyen trang thai (transition relation) R con S x S
  - L  : ham gan nhan (labeling function) L: S -> 2^AP, voi AP la tap
         cac menh de nguyen thuy (atomic propositions)

Module nay cai dat:
  - Doc mo hinh he thong tu file JSON (dataset)
  - Sinh khong gian trang thai bang duyet BFS
  - Kiem tra 3 dang thuoc tinh don gian (simple explicit-state model checking):
      * AG !p      (safety - "khong bao gio")
      * AG (p -> q) (safety co dieu kien)
      * EF p       (reachability - "co the xay ra")
  - Neu vi pham thuoc tinh AG, tra ve mot "counterexample trace" (duong di
    phan chung) tu trang thai khoi tao den trang thai vi pham.
"""

from __future__ import annotations
import json
from collections import deque
from dataclasses import dataclass, field
from typing import Dict, List, Set, Optional


@dataclass
class KripkeStructure:
    states: Dict[str, Set[str]]                 # state_id -> set(labels)
    state_names: Dict[str, str]                  # state_id -> ten mo ta
    initial_states: Set[str]
    transitions: Dict[str, List[str]]            # state_id -> [state_id, ...]
    transition_events: Dict[str, List[str]] = field(default_factory=dict)  # (from,to)->event

    @classmethod
    def from_json(cls, path: str) -> "KripkeStructure":
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)

        states = {s["id"]: set(s["labels"]) for s in data["states"]}
        state_names = {s["id"]: s["name"] for s in data["states"]}
        initial_states = set(data["initial_states"])

        transitions: Dict[str, List[str]] = {sid: [] for sid in states}
        events: Dict[str, str] = {}
        for t in data["transitions"]:
            transitions.setdefault(t["from"], []).append(t["to"])
            events[f'{t["from"]}->{t["to"]}'] = t["event"]

        return cls(states, state_names, initial_states, transitions, events)

    # ------------------------------------------------------------------
    # Duyet khong gian trang thai (state-space exploration) bang BFS
    # ------------------------------------------------------------------
    def reachable_states(self) -> Set[str]:
        """Tra ve tap tat ca trang thai co the den duoc tu cac trang thai khoi tao."""
        visited: Set[str] = set()
        queue = deque(self.initial_states)
        visited.update(self.initial_states)
        while queue:
            cur = queue.popleft()
            for nxt in self.transitions.get(cur, []):
                if nxt not in visited:
                    visited.add(nxt)
                    queue.append(nxt)
        return visited

    def _find_path(self, target_predicate) -> Optional[List[str]]:
        """BFS tim duong di ngan nhat tu init den mot trang thai thoa target_predicate.
        Tra ve danh sach state_id (path) hoac None neu khong ton tai."""
        visited: Set[str] = set()
        queue = deque([(s, [s]) for s in self.initial_states])
        visited.update(self.initial_states)
        while queue:
            cur, path = queue.popleft()
            if target_predicate(cur):
                return path
            for nxt in self.transitions.get(cur, []):
                if nxt not in visited:
                    visited.add(nxt)
                    queue.append((nxt, path + [nxt]))
        return None

    # ------------------------------------------------------------------
    # Danh gia menh de logic don gian tren nhan cua mot trang thai
    # Ho tro: "p", "NOT p", "p AND q", "p -> q" (mot cap don gian)
    # ------------------------------------------------------------------
    def _eval_formula(self, state_id: str, formula: str) -> bool:
        labels = self.states[state_id]

        def atom(tok: str) -> bool:
            tok = tok.strip()
            neg = False
            while tok.upper().startswith("NOT "):
                neg = not neg
                tok = tok[4:].strip()
            val = tok in labels
            return (not val) if neg else val

        if "->" in formula:
            lhs, rhs = formula.split("->", 1)
            return (not self._eval_and(lhs, atom)) or self._eval_and(rhs, atom)
        return self._eval_and(formula, atom)

    @staticmethod
    def _eval_and(expr: str, atom_fn) -> bool:
        parts = [p.strip() for p in expr.split(" AND ")]
        return all(atom_fn(p) for p in parts)

    # ------------------------------------------------------------------
    # Kiem chung cac dang thuoc tinh CTL don gian
    # ------------------------------------------------------------------
    def check_AG_not(self, formula: str):
        """AG !formula : khong trang thai nao dat duoc thoa 'formula'."""
        reachable = self.reachable_states()
        violating = [s for s in reachable if self._eval_formula(s, formula)]
        if not violating:
            return True, None
        path = self._find_path(lambda s: self._eval_formula(s, formula))
        return False, path

    def check_AG_implies(self, formula: str):
        """AG (p -> q): tren MOI trang thai reachable, neu p dung thi q phai dung."""
        lhs, rhs = formula.split("->", 1)
        reachable = self.reachable_states()
        for s in reachable:
            p_val = self._eval_formula(s, lhs.strip())
            q_val = self._eval_formula(s, rhs.strip())
            if p_val and not q_val:
                path = self._find_path(lambda x: x == s)
                return False, path
        return True, None

    def check_EF(self, formula: str):
        """EF formula: ton tai duong di den mot trang thai thoa 'formula'."""
        path = self._find_path(lambda s: self._eval_formula(s, formula))
        return (path is not None), path

    def check_property(self, prop: dict):
        ptype = prop["type"]
        formula = prop["formula"]
        if ptype == "AG_NOT":
            return self.check_AG_not(formula)
        elif ptype == "AG_IMPLIES":
            return self.check_AG_implies(formula)
        elif ptype == "EF":
            return self.check_EF(formula)
        raise ValueError(f"Loai thuoc tinh khong ho tro: {ptype}")

    def path_to_events(self, path: List[str]) -> List[str]:
        events = []
        for a, b in zip(path, path[1:]):
            events.append(self.transition_events.get(f"{a}->{b}", "?"))
        return events


def load_full_spec(path: str) -> dict:
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def run_verification(model_path: str, verbose: bool = True) -> dict:
    """Chay toan bo kiem chung cho mot mo hinh, tra ve dict ket qua co cau truc
    (dung cho ca CLI, unit test, va sinh du lieu cho bao cao LaTeX)."""
    spec = load_full_spec(model_path)
    model = KripkeStructure.from_json(model_path)
    results = {"system_name": spec["system_name"], "properties": []}

    if verbose:
        print(f"=== He thong: {spec['system_name']} ===")
        print(f"So trang thai khai bao: {len(spec['states'])}")
        reach = model.reachable_states()
        print(f"So trang thai co the den duoc (reachable): {len(reach)} / {len(spec['states'])}")

    for prop in spec["properties_to_check"]:
        ok, path = model.check_property(prop)
        entry = {
            "id": prop["id"],
            "formula": prop["formula"],
            "description": prop["description"],
            "holds": ok,
        }
        if path:
            entry["witness_states"] = path
            entry["witness_events"] = model.path_to_events(path)
        results["properties"].append(entry)

        if verbose:
            status = "THOA MAN (PASS)" if ok else "VI PHAM (FAIL)"
            print(f"\n[{prop['id']}] {prop['description']}")
            print(f"  Cong thuc : {prop['formula']}")
            print(f"  Ket qua   : {status}")
            if path:
                names = " -> ".join(model.state_names[s] for s in path)
                print(f"  Duong di  : {names}")
                print(f"  Su kien   : {' , '.join(model.path_to_events(path))}")

    return results


if __name__ == "__main__":
    import sys
    import os

    base = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    default_model = os.path.join(base, "dataset", "access_system_model.json")
    model_path = sys.argv[1] if len(sys.argv) > 1 else default_model
    run_verification(model_path)
