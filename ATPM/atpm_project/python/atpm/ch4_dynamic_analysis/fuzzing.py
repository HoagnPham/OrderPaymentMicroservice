"""
Chuong 4 - Phat hien lo hong: Phan tich dong.
Module nay cung cap:
  1. Cong cu do code coverage bang gcov (muc 4.2)
  2. Mot black-box fuzzer thuan tuy ngau nhien (muc 4.5)
  3. Mot coverage-guided fuzzer don gian (muc 4.6, "white-box" theo nghia
     don gian hoa: dung code coverage lam tin hieu huong dan mutation)

Muc dich la MINH HOA nguyen ly, khong phai thay the AFL++/libFuzzer trong
thuc te san xuat.
"""

from __future__ import annotations
import subprocess
import random
import os
import re
import glob
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional, List


@dataclass
class FuzzResult:
    crashed: bool
    crashing_input: Optional[bytes]
    iterations_used: int
    coverage_history: List[float] = field(default_factory=list)


def compile_with_coverage(source: str, output_binary: str) -> None:
    """Bien dich file C voi flag --coverage (sinh .gcno, dung cho gcov).
    Bien dich hai buoc (compile roi link) de .gcno/.gcda mang ten TRUNG VOI
    file nguon (VD: fuzz_target.gcno). Copy ca file .c vao work_dir vi gcov
    can doc lai source cung thu muc voi .gcno."""
    import shutil
    work_dir = os.path.dirname(output_binary) or "."
    source_abs = os.path.abspath(source)
    source_basename = os.path.basename(source)

    dest_source = os.path.join(work_dir, source_basename)
    if os.path.abspath(dest_source) != source_abs:
        shutil.copy(source_abs, dest_source)

    object_name = os.path.splitext(source_basename)[0] + ".o"
    subprocess.run(
        ["gcc", "--coverage", "-O0", "-c", source_basename, "-o", object_name],
        check=True, capture_output=True, cwd=work_dir,
    )
    subprocess.run(
        ["gcc", "--coverage", "-o", os.path.basename(output_binary), object_name],
        check=True, capture_output=True, cwd=work_dir,
    )


def _reset_coverage_counters(work_dir: str) -> None:
    for f in glob.glob(os.path.join(work_dir, "*.gcda")):
        os.remove(f)


def measure_coverage_percent(source_basename: str, work_dir: str) -> float:
    """Chay gcov va tra ve % dong lenh da duoc thuc thi (cumulative)."""
    proc = subprocess.run(
        ["gcov", source_basename],
        cwd=work_dir, capture_output=True, text=True,
    )
    match = re.search(r"Lines executed:([\d.]+)% of \d+", proc.stdout)
    return float(match.group(1)) if match else 0.0


def _run_target(binary_path: str, input_bytes: bytes, work_dir: str) -> int:
    """Chay target voi input (ghi ra file tam), tra ve returncode.
    Returncode am (VD: -11) nghia la bi kill boi signal (VD: SIGSEGV)."""
    binary_abs = os.path.abspath(binary_path)
    input_path = os.path.join(work_dir, "_fuzz_input.bin")
    with open(input_path, "wb") as f:
        f.write(input_bytes)
    try:
        proc = subprocess.run([binary_abs, input_path], capture_output=True, timeout=2)
        return proc.returncode
    except subprocess.TimeoutExpired:
        return -999  # hang/timeout


def black_box_fuzz(binary_path: str, max_iterations: int = 5000,
                    input_len: int = 4, seed: int = 1) -> FuzzResult:
    """Fuzzing hop den THUAN TUY: sinh byte NGAU NHIEN, KHONG dung feedback
    tu chuong trinh. Tuong ung muc 4.5."""
    rng = random.Random(seed)
    work_dir = str(Path(binary_path).parent)

    for i in range(1, max_iterations + 1):
        candidate = bytes(rng.randint(0, 255) for _ in range(input_len))
        rc = _run_target(binary_path, candidate, work_dir)
        if rc < 0:  # bi kill boi signal -> crash
            return FuzzResult(True, candidate, i)
    return FuzzResult(False, None, max_iterations)


def coverage_guided_fuzz(binary_path: str, source_basename: str,
                          max_iterations: int = 5000, input_len_max: int = 8,
                          seed: int = 1) -> FuzzResult:
    """Fuzzing huong dan boi code coverage (don gian hoa nguyen ly cua
    AFL/libFuzzer): giu lai mutant nao lam TANG coverage tich luy, dung no
    lam diem xuat phat cho mutation tiep theo. Tuong ung muc 4.6."""
    rng = random.Random(seed)
    work_dir = str(Path(binary_path).parent)
    _reset_coverage_counters(work_dir)

    corpus: List[bytes] = [bytes([rng.randint(65, 90)])]  # seed 1 byte ngau nhien
    best_coverage = 0.0
    history: List[float] = []

    for i in range(1, max_iterations + 1):
        base = rng.choice(corpus)
        candidate = bytearray(base)
        # Mutation: hoac flip 1 byte, hoac them 1 byte moi (toi da input_len_max)
        if len(candidate) < input_len_max and rng.random() < 0.3:
            candidate.append(rng.randint(0, 255))
        else:
            idx = rng.randrange(len(candidate))
            candidate[idx] = rng.randint(0, 255)
        candidate = bytes(candidate)

        rc = _run_target(binary_path, candidate, work_dir)
        if rc < 0:
            return FuzzResult(True, candidate, i, history)

        cov = measure_coverage_percent(source_basename, work_dir)
        history.append(cov)
        if cov > best_coverage:
            best_coverage = cov
            corpus.append(candidate)  # "them vao corpus" - giong AFL giu seed moi

    return FuzzResult(False, None, max_iterations, history)


if __name__ == "__main__":
    import sys
    work_dir = os.path.dirname(os.path.abspath(__file__))
    src = os.path.join(work_dir, "..", "..", "..", "c", "ch4_dynamic", "fuzz_target.c")
    binary = os.path.join(work_dir, "_fuzz_target_bin")

    print("Bien dich target voi --coverage...")
    compile_with_coverage(src, binary)

    print("\n== Black-box fuzzing (thuan random, khong feedback) ==")
    r1 = black_box_fuzz(binary, max_iterations=3000)
    print(f"Crashed: {r1.crashed}, iterations: {r1.iterations_used}, input: {r1.crashing_input}")

    print("\n== Coverage-guided fuzzing (co feedback tu gcov) ==")
    r2 = coverage_guided_fuzz(binary, "fuzz_target.c", max_iterations=3000)
    print(f"Crashed: {r2.crashed}, iterations: {r2.iterations_used}, input: {r2.crashing_input}")
