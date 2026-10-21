"""Small CTL parser/evaluator for the subset supported by the ATPM workbook.

The lecturer's pipeline supports atomic propositions combined with ``!``, ``&``,
``|`` and ``->`` plus the path quantifiers ``AG``, ``AF``, ``EG``, ``EF``,
``AX`` and ``EX``.  Nested formulas such as
``AG(payment_pending -> AF(paid | failed))`` are evaluated with the usual
fixed-point semantics on a finite Kripke structure.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable

QUANTIFIERS = {"AG", "AF", "EG", "EF", "AX", "EX"}


@dataclass(frozen=True)
class Atom:
    name: str


@dataclass(frozen=True)
class Not:
    child: "Formula"


@dataclass(frozen=True)
class Binary:
    operator: str
    left: "Formula"
    right: "Formula"


@dataclass(frozen=True)
class Quantified:
    quantifier: str
    child: "Formula"


Formula = Atom | Not | Binary | Quantified


class CtlSyntaxError(ValueError):
    pass


def tokenize(text: str) -> list[str]:
    tokens: list[str] = []
    index = 0
    while index < len(text):
        character = text[index]
        if character.isspace():
            index += 1
            continue
        if text.startswith("->", index):
            tokens.append("->")
            index += 2
            continue
        if character in "()!&|":
            tokens.append(character)
            index += 1
            continue
        if character.isalpha() or character == "_":
            end = index + 1
            while end < len(text) and (text[end].isalnum() or text[end] == "_"):
                end += 1
            tokens.append(text[index:end])
            index = end
            continue
        raise CtlSyntaxError(f"Unexpected character {character!r} in {text!r}")
    return tokens


class _Parser:
    def __init__(self, text: str) -> None:
        self.text = text
        self.tokens = tokenize(text)
        self.index = 0

    def peek(self) -> str | None:
        return self.tokens[self.index] if self.index < len(self.tokens) else None

    def take(self, expected: str | None = None) -> str:
        token = self.peek()
        if token is None:
            raise CtlSyntaxError(f"Unexpected end of formula {self.text!r}")
        if expected is not None and token != expected:
            raise CtlSyntaxError(f"Expected {expected!r} but found {token!r} in {self.text!r}")
        self.index += 1
        return token

    def parse(self) -> Formula:
        formula = self.implication()
        if self.peek() is not None:
            raise CtlSyntaxError(f"Trailing token {self.peek()!r} in {self.text!r}")
        return formula

    def implication(self) -> Formula:
        left = self.disjunction()
        if self.peek() == "->":
            self.take("->")
            return Binary("->", left, self.implication())
        return left

    def disjunction(self) -> Formula:
        formula = self.conjunction()
        while self.peek() == "|":
            self.take("|")
            formula = Binary("|", formula, self.conjunction())
        return formula

    def conjunction(self) -> Formula:
        formula = self.unary()
        while self.peek() == "&":
            self.take("&")
            formula = Binary("&", formula, self.unary())
        return formula

    def unary(self) -> Formula:
        if self.peek() == "!":
            self.take("!")
            return Not(self.unary())
        return self.primary()

    def primary(self) -> Formula:
        token = self.peek()
        if token is None:
            raise CtlSyntaxError(f"Unexpected end of formula {self.text!r}")
        if token == "(":
            self.take("(")
            formula = self.implication()
            self.take(")")
            return formula
        if token in QUANTIFIERS:
            self.take(token)
            self.take("(")
            child = self.implication()
            self.take(")")
            return Quantified(token, child)
        if token in {"->", ")", "!", "&", "|"}:
            raise CtlSyntaxError(f"Expected an atom but found {token!r} in {self.text!r}")
        return Atom(self.take())


def parse(text: str) -> Formula:
    return _Parser(text).parse()


def atoms(formula: Formula) -> set[str]:
    if isinstance(formula, Atom):
        return {formula.name}
    if isinstance(formula, Not):
        return atoms(formula.child)
    if isinstance(formula, Binary):
        return atoms(formula.left) | atoms(formula.right)
    if isinstance(formula, Quantified):
        return atoms(formula.child)
    raise AssertionError(formula)


def _successors(transitions: Iterable[tuple[str, str]]) -> dict[str, set[str]]:
    graph: dict[str, set[str]] = {}
    for source, target in transitions:
        graph.setdefault(source, set()).add(target)
        graph.setdefault(target, set())
    return graph


def evaluate(
    formula: Formula,
    states: Iterable[str],
    labeling: dict[str, Iterable[str]],
    transitions: Iterable[tuple[str, str]],
    initial: Iterable[str],
) -> bool:
    """Evaluate a CTL formula at the (possibly multiple) initial states."""
    state_set = set(states)
    graph = _successors(transitions)
    graph = {state: graph.get(state, set()) for state in state_set}
    labels = {state: set(values) for state, values in labeling.items()}
    start = set(initial)

    def sat(node: Formula) -> set[str]:
        if isinstance(node, Atom):
            return {state for state in state_set if node.name in labels.get(state, set())}
        if isinstance(node, Not):
            return state_set - sat(node.child)
        if isinstance(node, Binary):
            if node.operator == "&":
                return sat(node.left) & sat(node.right)
            if node.operator == "|":
                return sat(node.left) | sat(node.right)
            if node.operator == "->":
                return (state_set - sat(node.left)) | sat(node.right)
            raise AssertionError(node.operator)
        if isinstance(node, Quantified):
            inner = sat(node.child)
            if node.quantifier == "EX":
                return {state for state in state_set if graph[state] & inner}
            if node.quantifier == "AX":
                return {state for state in state_set if graph[state] and graph[state] <= inner}
            if node.quantifier == "EF":
                fixed = set(inner)
                while True:
                    expanded = fixed | {state for state in state_set if graph[state] & fixed}
                    if expanded == fixed:
                        return fixed
                    fixed = expanded
            if node.quantifier == "AF":
                fixed = set(inner)
                while True:
                    expanded = fixed | {
                        state for state in state_set
                        if graph[state] and graph[state] <= fixed
                    }
                    if expanded == fixed:
                        return fixed
                    fixed = expanded
            if node.quantifier == "EG":
                fixed = set(inner)
                while True:
                    reduced = {state for state in fixed if graph[state] & fixed}
                    if reduced == fixed:
                        return fixed
                    fixed = reduced
            if node.quantifier == "AG":
                fixed = set(state_set)
                while True:
                    reduced = {
                        state for state in fixed
                        if state in inner and graph[state] <= fixed
                    }
                    if reduced == fixed:
                        return fixed
                    fixed = reduced
            raise AssertionError(node.quantifier)
        raise AssertionError(node)

    if isinstance(formula, Quantified) and formula.quantifier in {"EF", "EG", "EX"}:
        return bool(start & sat(formula))
    return start <= sat(formula)
