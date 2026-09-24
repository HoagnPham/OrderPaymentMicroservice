"""
Chuong 1 - Gioi thieu ve an toan phan mem
Mo hinh hoa Kripke Structure de bieu dien he thong / chuong trinh
va kiem tra cac thuoc tinh an toan co ban (invariant, reachability).

Kripke Structure K = (S, S0, R, L)
  S  : tap huu han cac trang thai (states)
  S0 : tap trang thai khoi tao (initial states) subset S
  R  : quan he chuyen trang thai (transition relation) R subset S x S,
       total (moi trang thai co it nhat mot trang thai ke tiep)
  L  : ham gan nhan (labeling function) L: S -> 2^AP, voi AP la tap
       cac menh de nguyen tu (atomic propositions), vi du {"authenticated",
       "admin", "unsafe_buffer"}.
"""

from __future__ import annotations
from dataclasses import dataclass, field
from typing import Dict, Iterable, Set, FrozenSet, List, Optional
from collections import deque


class KripkeStructureError(ValueError):
    """Loi cau truc: vi pham tinh 'total' cua quan he chuyen trang thai,
    hoac tham chieu toi trang thai khong ton tai."""


@dataclass
class KripkeStructure:
    states: Set[str] = field(default_factory=set)
    initial_states: Set[str] = field(default_factory=set)
    transitions: Dict[str, Set[str]] = field(default_factory=dict)
    labeling: Dict[str, FrozenSet[str]] = field(default_factory=dict)

    def add_state(self, s: str, labels: Iterable[str] = ()) -> None:
        self.states.add(s)
        self.labeling.setdefault(s, frozenset())
        self.labeling[s] = frozenset(labels) | self.labeling[s]
        self.transitions.setdefault(s, set())

    def set_initial(self, *states: str) -> None:
        for s in states:
            if s not in self.states:
                raise KripkeStructureError(f"Trang thai khoi tao '{s}' chua duoc khai bao")
            self.initial_states.add(s)

    def add_transition(self, src: str, dst: str) -> None:
        if src not in self.states or dst not in self.states:
            raise KripkeStructureError(f"Chuyen tiep ({src} -> {dst}) tham chieu trang thai khong ton tai")
        self.transitions.setdefault(src, set()).add(dst)

    def validate_total(self) -> None:
        """Theo dinh nghia hinh thuc, R phai la 'total': moi trang thai
        phai co it nhat mot trang thai ke tiep. Neu mo hinh mot 'deadlock'
        that su, thong le la them self-loop tai trang thai do."""
        for s in self.states:
            if not self.transitions.get(s):
                raise KripkeStructureError(
                    f"Trang thai '{s}' khong co chuyen tiep di ra - vi pham tinh total cua R. "
                    f"Them self-loop neu day la trang thai ket thuc hop le."
                )

    def labels_of(self, s: str) -> FrozenSet[str]:
        return self.labeling.get(s, frozenset())

    # ---- Kiem tra thuoc tinh (mot dang rat don gian cua CTL model checking) ----

    def reachable_from(self, start_states: Optional[Iterable[str]] = None) -> Set[str]:
        """BFS: tap trang thai co the toi duoc tu tap trang thai khoi dau."""
        frontier = deque(start_states if start_states is not None else self.initial_states)
        visited: Set[str] = set(frontier)
        while frontier:
            s = frontier.popleft()
            for t in self.transitions.get(s, ()):
                if t not in visited:
                    visited.add(t)
                    frontier.append(t)
        return visited

    def check_invariant(self, unsafe_label: str) -> Dict[str, object]:
        """Kiem tra thuoc tinh AG(not unsafe_label): 'tren moi duong thi hanh,
        tu moi trang thai khoi tao, khong bao gio dat toi trang thai mang
        nhan unsafe_label'. Day la dang pho bien nhat khi mo hinh hoa loi
        bao mat: unsafe_label co the la 'buffer_overflow', 'use_after_free', ...

        Tra ve dict: {"holds": bool, "counterexample_path": List[str] | None}
        """
        # BFS tim duong di ngan nhat den mot trang thai vi pham, neu co
        parent: Dict[str, Optional[str]] = {s: None for s in self.initial_states}
        frontier = deque(self.initial_states)
        visited: Set[str] = set(self.initial_states)

        for s in self.initial_states:
            if unsafe_label in self.labels_of(s):
                return {"holds": False, "counterexample_path": [s]}

        while frontier:
            s = frontier.popleft()
            for t in self.transitions.get(s, ()):
                if t in visited:
                    continue
                visited.add(t)
                parent[t] = s
                if unsafe_label in self.labels_of(t):
                    return {"holds": False, "counterexample_path": self._rebuild_path(parent, t)}
                frontier.append(t)

        return {"holds": True, "counterexample_path": None}

    @staticmethod
    def _rebuild_path(parent: Dict[str, Optional[str]], target: str) -> List[str]:
        path = [target]
        while parent[path[-1]] is not None:
            path.append(parent[path[-1]])
        path.reverse()
        return path

    def check_reachability(self, goal_label: str) -> Dict[str, object]:
        """Kiem tra thuoc tinh EF(goal_label): 'ton tai it nhat mot duong
        thi hanh dan toi trang thai mang nhan goal_label'. Dung de mo hinh
        hoa cau hoi dang 'lieu ke tan cong co the dat duoc quyen admin?'."""
        reachable = self.reachable_from()
        witnesses = [s for s in reachable if goal_label in self.labels_of(s)]
        return {"holds": len(witnesses) > 0, "witness_states": witnesses}

    def to_dot(self) -> str:
        """Xuat sang dinh dang Graphviz DOT de truc quan hoa (dung trong slide)."""
        lines = ["digraph Kripke {", "  rankdir=LR;", "  node [shape=ellipse];"]
        for s in self.states:
            lbl = ", ".join(sorted(self.labels_of(s))) or "-"
            shape = "doublecircle" if s in self.initial_states else "ellipse"
            lines.append(f'  "{s}" [label="{s}\\n{{{lbl}}}", shape={shape}];')
        for src, dsts in self.transitions.items():
            for dst in dsts:
                lines.append(f'  "{src}" -> "{dst}";')
        lines.append("}")
        return "\n".join(lines)


def build_login_example() -> KripkeStructure:
    """Vi du kinh dien dung trong bai giang: mo hinh hoa mot he thong dang
    nhap don gian de minh hoa khai niem 'unsafe state' - trang thai ke tan
    cong dat duoc quyen admin ma khong xac thuc (broken access control)."""
    k = KripkeStructure()
    k.add_state("s0_khoi_dong", labels=["init"])
    k.add_state("s1_cho_dang_nhap", labels=["waiting_auth"])
    k.add_state("s2_da_xac_thuc", labels=["authenticated"])
    k.add_state("s3_admin_hop_le", labels=["authenticated", "admin"])
    k.add_state("s4_admin_bypass", labels=["admin", "unsafe_no_auth"])  # LO HONG

    k.set_initial("s0_khoi_dong")
    k.add_transition("s0_khoi_dong", "s1_cho_dang_nhap")
    k.add_transition("s1_cho_dang_nhap", "s2_da_xac_thuc")
    k.add_transition("s2_da_xac_thuc", "s3_admin_hop_le")
    k.add_transition("s3_admin_hop_le", "s3_admin_hop_le")  # self-loop, trang thai on dinh
    # LO HONG: co duong tat tu s1 sang s4 (bo qua xac thuc - vi du do thieu
    # kiem tra quyen tren API endpoint quan tri)
    k.add_transition("s1_cho_dang_nhap", "s4_admin_bypass")
    k.add_transition("s4_admin_bypass", "s4_admin_bypass")

    k.validate_total()
    return k


if __name__ == "__main__":
    model = build_login_example()
    result = model.check_invariant("unsafe_no_auth")
    print("Thuoc tinh AG(not unsafe_no_auth) co giu khong?", result["holds"])
    if not result["holds"]:
        print("Duong phan chung (counterexample):", " -> ".join(result["counterexample_path"]))
    print()
    print(model.to_dot())
