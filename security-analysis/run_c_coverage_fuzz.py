#!/usr/bin/env python3
"""Run the opt-in coverage-guided C demonstration.

The target is a synthetic validator fixture, not the ASP.NET service. GCC's
trace-pc instrumentation feeds a shared edge map; the harness keeps inputs that
add coverage and compares them with a no-retention random baseline.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FUZZ = ROOT / "security-analysis" / "c-memory" / "fuzz"
BUILD = ROOT / "security-analysis" / "c-memory" / "build" / "cov-fuzz"
RESULT = ROOT / "security-analysis" / "results" / "c_coverage_fuzz.results.json"
SEED = 20261022
ITERATIONS = 20000


def run_checked(command: list[str]) -> None:
    subprocess.run(command, cwd=ROOT, check=True)


def build() -> Path:
    BUILD.mkdir(parents=True, exist_ok=True)
    common = ["gcc", "-std=c11", "-O0", "-g", "-Wall", "-Wextra", "-fno-pie"]
    runtime = BUILD / "cov_runtime.o"
    engine = BUILD / "coverage_fuzz.o"
    target = BUILD / "fixture_target.o"
    binary = BUILD / "cov_fuzz"
    run_checked([*common, "-c", str(FUZZ / "cov_runtime.c"), "-o", str(runtime)])
    run_checked([*common, "-c", str(FUZZ / "coverage_fuzz.c"), "-o", str(engine)])
    run_checked([
        *common,
        "-fno-omit-frame-pointer",
        "-fsanitize-coverage=trace-pc",
        "-c",
        str(FUZZ / "fixture_target.c"),
        "-o",
        str(target),
    ])
    run_checked(["gcc", "-no-pie", str(runtime), str(engine), str(target), "-o", str(binary)])
    return binary


def parse_run(stdout: str) -> dict[str, int | str]:
    result: dict[str, int | str] = {}
    for line in stdout.splitlines():
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        try:
            result[key] = int(value)
        except ValueError:
            result[key] = value
    return result


def run(binary: Path, mode: str, seed: int, iterations: int) -> dict[str, int | str]:
    completed = subprocess.run(
        [str(binary), "--seed", str(seed), "--iterations", str(iterations), "--mode", mode],
        cwd=ROOT,
        check=True,
        text=True,
        capture_output=True,
    )
    return parse_run(completed.stdout)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seed", type=int, default=SEED)
    parser.add_argument("--iterations", type=int, default=ITERATIONS)
    parser.add_argument("--no-build", action="store_true")
    args = parser.parse_args()

    started = time.monotonic()
    binary = BUILD / "cov_fuzz"
    if not args.no_build or not binary.exists():
        binary = build()
    if not binary.exists():
        print("coverage fuzz binary was not built", file=sys.stderr)
        return 2

    guided = run(binary, "guided", args.seed, args.iterations)
    random = run(binary, "random", args.seed, args.iterations)
    passed = (
        int(guided.get("final_edges", 0)) > int(guided.get("initial_edges", 0))
        and int(guided.get("new_coverage_inputs", 0)) > 0
        and int(guided.get("queue_size", 0)) > int(guided.get("seed_inputs", 0))
        and int(guided.get("crashes", 0)) == 0
    )
    evidence = {
        "schema_version": 1,
        "passed": passed,
        "run_at_utc": datetime.now(timezone.utc).isoformat(),
        "seed": args.seed,
        "iterations": args.iterations,
        "duration_seconds": round(time.monotonic() - started, 3),
        "engine": {
            "kind": "coverage-guided",
            "instrumentation": "GCC -fsanitize-coverage=trace-pc callbacks",
            "feedback": "hashed basic-block PC transitions in a shared 64 KiB map",
            "isolation": "fork per input",
            "retention": "keep input when it adds at least one unseen edge",
            "baseline": "same mutations without coverage retention",
            "not_claimed": "source-level edge coverage, LLVM libFuzzer, ASP.NET code coverage, or production service",
        },
        "target": {
            "path": "security-analysis/c-memory/fuzz/fixture_target.c",
            "scope": "synthetic C validator fixture; not the ASP.NET application",
            "seed_inputs": int(guided.get("seed_inputs", 0)),
        },
        "guided": guided,
        "random": random,
        "comparison": {
            "guided_minus_random_final_edges": int(guided.get("final_edges", 0))
            - int(random.get("final_edges", 0)),
            "note": "The comparison is diagnostic only; coverage guidance does not guarantee winning every short campaign.",
        },
        "limitations": [
            "The instrumented target is a synthetic C fixture, not the ASP.NET service.",
            "trace-pc transitions are a compact proxy for code edges, not a source-level gcov/LLVM edge report.",
            "The random baseline uses the same mutations and seed but no coverage retention; it is not a proof of optimality.",
            "No memory-safety claim is made for the fixture or the service; ASan/CBMC/Valgrind evidence is separate.",
        ],
        "source_sha256": {
            str(path.relative_to(ROOT)): sha256(path)
            for path in [
                ROOT / "security-analysis/run_c_coverage_fuzz.py",
                FUZZ / "fixture_target.c",
                FUZZ / "fixture_target.h",
                FUZZ / "coverage_fuzz.c",
                FUZZ / "cov_runtime.c",
                FUZZ / "cov_runtime.h",
            ]
        },
    }
    RESULT.parent.mkdir(parents=True, exist_ok=True)
    RESULT.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "passed": passed,
        "guided_edges": guided.get("final_edges"),
        "random_edges": random.get("final_edges"),
        "guided_queue": guided.get("queue_size"),
        "crashes": guided.get("crashes"),
        "evidence": str(RESULT.relative_to(ROOT)),
    }, ensure_ascii=False, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
