"""
Chuong 3 - Phat hien lo hong: Phan tich tinh.
Module nay hien thuc hoa cac vi du SAT/SMT trong slide 3.4 bang Z3, va
cung cap wrapper goi CBMC (3.5) tu Python.
"""

from __future__ import annotations
from dataclasses import dataclass
from typing import Optional
import subprocess
import re

from z3 import (
    Solver, BitVec, BitVecVal, Or, UGT, ULT, sat, unsat,
    Int, Bool, Bools, Ints,
)


@dataclass
class SmtCheckResult:
    property_holds: bool          # True neu KHONG tim thay vi pham (an toan)
    counterexample: Optional[str] # mo ta counterexample neu co


def check_index_bounds(index_min: int, index_max_exclusive: int,
                        assume_checked: bool = False) -> SmtCheckResult:
    """Tuong ung slide 'kiem tra Buffer Overflow bang Z3'.

    Neu assume_checked=False: mo phong code CHUA co kiem tra bien -> tim
    counterexample (nhu ham bug_* trong Chuong 2).
    Neu assume_checked=True: gia thiet da co if (index>=0 && index<size)
    truoc do -> ky vong unsat (an toan).
    """
    index = BitVec('index', 32)
    s = Solver()
    if assume_checked:
        s.add(index >= index_min, index < index_max_exclusive)
    s.add(Or(index < index_min, index >= index_max_exclusive))

    if s.check() == sat:
        m = s.model()
        return SmtCheckResult(False, f"index = {m[index].as_signed_long()}")
    return SmtCheckResult(True, None)


def check_integer_overflow_multiply(bits: int = 32, elem_size: int = 4) -> SmtCheckResult:
    """Tuong ung slide 'Integer Overflow -- ma hoa bang Bitvector'.
    Tim gia tri count > 0 sao cho count * elem_size 'wrap around' nho hon count.
    """
    count = BitVec('count', bits)
    elem = BitVecVal(elem_size, bits)
    s = Solver()
    product = count * elem
    s.add(UGT(count, 0), ULT(product, count))

    if s.check() == sat:
        m = s.model()
        return SmtCheckResult(False, f"count = {m[count].as_long()}")
    return SmtCheckResult(True, None)


def check_division_safety(assume_nonzero: bool = False) -> SmtCheckResult:
    """Tuong ung bai tap tong hop 3.7: kiem tra 'chia cho 0' bang Z3
    (song song voi vi du CBMC --div-by-zero-check)."""
    x = Int('x')
    s = Solver()
    if assume_nonzero:
        s.add(x != 0)
    s.add(x == 0)   # dieu kien VI PHAM: x bang 0

    if s.check() == sat:
        return SmtCheckResult(False, "x = 0 (chia cho 0)")
    return SmtCheckResult(True, None)


# ---------------------------------------------------------------------
# Wrapper CBMC (Bounded Model Checking, muc 3.5)
# ---------------------------------------------------------------------

@dataclass
class CbmcResult:
    verification_successful: bool
    raw_output: str
    failed_properties: list[str]


class CbmcNotFoundError(RuntimeError):
    pass


def run_cbmc(c_file: str, function: str, extra_flags: Optional[list[str]] = None,
             unwind: int = 10, timeout: int = 60) -> CbmcResult:
    """Chay CBMC tren mot file C, tra ve CbmcResult da phan tich.

    Vi du:
        >>> run_cbmc("c/ch3_static/bmc_demo.c", "check_sum", unwind=6)
    """
    cmd = ["cbmc", "--unwind", str(unwind), "--unwinding-assertions",
           "--function", function]
    if extra_flags:
        cmd.extend(extra_flags)
    cmd.append(c_file)

    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    except FileNotFoundError as exc:
        raise CbmcNotFoundError(
            "Khong tim thay 'cbmc' trong PATH. Cai dat bang 'apt-get install cbmc'."
        ) from exc

    output = proc.stdout + proc.stderr
    success = "VERIFICATION SUCCESSFUL" in output
    failed = re.findall(r"\[([\w.\-]+)\] .*: FAILURE", output)
    return CbmcResult(success, output, failed)


if __name__ == "__main__":
    print("== check_index_bounds (chua kiem tra bien) ==")
    r1 = check_index_bounds(0, 64, assume_checked=False)
    print(r1)

    print("== check_index_bounds (DA kiem tra bien) ==")
    r2 = check_index_bounds(0, 64, assume_checked=True)
    print(r2)

    print("== check_integer_overflow_multiply ==")
    r3 = check_integer_overflow_multiply()
    print(r3)

    print("== check_division_safety (khong gia thiet) ==")
    r4 = check_division_safety(assume_nonzero=False)
    print(r4)

    print("== check_division_safety (co gia thiet x != 0) ==")
    r5 = check_division_safety(assume_nonzero=True)
    print(r5)
