"""Export CBMC CNF to DIMACS, solve it, and verify UNSAT proofs with drat-trim.

This is an optional deep check for the five C teaching modules. It does not
replace CBMC's own BMC run: it makes the SAT layer explicit by writing CNF,
solving it with CaDiCaL, checking a DRAT proof for every UNSAT case, extracting
an UNSAT core, and checking that the core is still UNSAT.

The result is still bounded by CBMC's unwind limit and the harness input. A
DRAT certificate proves UNSAT of the generated CNF, not safety of the ASP.NET
service or of unbounded executions.
"""
from __future__ import annotations

import hashlib
import json
import platform
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HARNESSES = ROOT / "tests" / "bmc_harnesses"
RESULTS = ROOT / "results" / "sat"
EVIDENCE = ROOT / "evidence"
SAMPLE = "cart_fixed"
MODULES = ("order", "payment", "cart", "product", "user")
UNWIND = 64
COMMON = [
    "--function", "main",
    "--bounds-check",
    "--pointer-check",
    "--memory-leak-check",
    "--unwind", str(UNWIND),
    "--unwinding-assertions",
]


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(command: list[str], timeout: int = 300) -> subprocess.CompletedProcess:
    return subprocess.run(command, capture_output=True, text=True, timeout=timeout)


def require_tool(name: str) -> str:
    path = shutil.which(name)
    if path is None:
        raise SystemExit(
            f"{name} is required for the SAT/CNF/DIMACS pipeline; install it and put it on PATH"
        )
    return path


def tool_info(name: str, path: str) -> dict:
    version_text = "source build (no --version support)"
    if name != "drat-trim":
        version = run([path, "--version"])
        lines = (version.stdout or version.stderr).strip().splitlines()
        if lines:
            version_text = lines[0]
    return {
        "name": name,
        "path": path,
        "version": version_text,
        "sha256": digest(Path(path)),
    }


def parse_dimacs_header(path: Path) -> dict:
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("p cnf "):
            _, _, variables, clauses = line.split()
            return {"variables": int(variables), "clauses": int(clauses)}
    raise ValueError(f"missing DIMACS header: {path}")


def parse_model(log: str) -> set[int]:
    model: set[int] = set()
    for line in log.replace("\r", "\n").splitlines():
        if not line.startswith("v "):
            continue
        for token in line[2:].split():
            literal = int(token)
            if literal == 0:
                break
            model.add(literal)
    return model


def cnf_is_satisfied(path: Path, model: set[int]) -> bool:
    clause: list[int] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("c") or line.startswith("p"):
            continue
        for token in line.split():
            literal = int(token)
            if literal == 0:
                if clause and not any(item in model for item in clause):
                    return False
                clause = []
            else:
                clause.append(literal)
    return not clause


def run_case(module: str, variant: str, tools: dict) -> dict:
    tag = f"{module}_{variant}"
    harness = HARNESSES / f"{module}_harness.c"
    cnf = RESULTS / f"{tag}.cnf"
    cbmc_log = RESULTS / f"{tag}.cbmc.log"
    solver_log = RESULTS / f"{tag}.cadical.log"
    proof = RESULTS / f"{tag}.drat"
    core = RESULTS / f"{tag}.core.cnf"
    drat_log = RESULTS / f"{tag}.drat.log"

    cbmc_command = [
        tools["cbmc"]["path"], str(harness), *COMMON,
        "--dimacs", "--outfile", str(cnf),
    ]
    if variant == "fixed":
        cbmc_command.append("-DFIXED")
    cbmc = run(cbmc_command)
    cbmc_log.write_text(cbmc.stdout + cbmc.stderr, encoding="utf-8")
    if cbmc.returncode != 0 or not cnf.exists():
        return {
            "module": module,
            "variant": variant,
            "passed": False,
            "reason": "CBMC failed to generate DIMACS CNF",
            "cbmc_exit": cbmc.returncode,
        }

    header = parse_dimacs_header(cnf)
    base = {
        "module": module,
        "variant": variant,
        "cnf": str(cnf.relative_to(ROOT)),
        "cnf_sha256": digest(cnf),
        "cnf_variables": header["variables"],
        "cnf_clauses": header["clauses"],
        "cbmc_command": " ".join(cbmc_command),
    }

    if variant == "buggy":
        solver_command = [tools["cadical"]["path"], str(cnf)]
        solver = run(solver_command)
        solver_log.write_text(solver.stdout + solver.stderr, encoding="utf-8")
        model = parse_model(solver.stdout + solver.stderr)
        model_ok = bool(model) and cnf_is_satisfied(cnf, model)
        passed = solver.returncode == 10 and model_ok
        return {
            **base,
            "expected": "sat",
            "solver_exit": solver.returncode,
            "solver_verdict": "sat" if solver.returncode == 10 else "unexpected",
            "model_literals": len(model),
            "model_satisfies_cnf": model_ok,
            "solver_command": " ".join(solver_command),
            "passed": passed,
        }

    solver_command = [
        tools["cadical"]["path"], "--no-binary", "--checkproof=true",
        str(cnf), str(proof),
    ]
    solver = run(solver_command)
    solver_log.write_text(solver.stdout + solver.stderr, encoding="utf-8")
    if solver.returncode != 20 or not proof.exists():
        return {
            **base,
            "expected": "unsat",
            "solver_exit": solver.returncode,
            "solver_verdict": "unexpected",
            "solver_command": " ".join(solver_command),
            "passed": False,
        }

    drat_command = [
        tools["drat_trim"]["path"], str(cnf), str(proof),
        "-c", str(core), "-w",
    ]
    drat = run(drat_command)
    drat_text = (drat.stdout + drat.stderr).replace("\r", "\n")
    drat_log.write_text(drat_text, encoding="utf-8")
    drat_verified = drat.returncode == 0 and "s VERIFIED" in drat_text

    core_check = run([tools["cadical"]["path"], str(core)]) if core.exists() else None
    core_unsat = core_check is not None and core_check.returncode == 20
    core_header = parse_dimacs_header(core) if core.exists() else None

    return {
        **base,
        "expected": "unsat",
        "solver_exit": solver.returncode,
        "solver_verdict": "unsat",
        "solver_command": " ".join(solver_command),
        "proof": str(proof.relative_to(ROOT)),
        "proof_sha256": digest(proof),
        "proof_bytes": proof.stat().st_size,
        "drat_verified": drat_verified,
        "drat_trim_output": drat_text.strip().splitlines()[-6:],
        "drat_command": " ".join(drat_command),
        "core": str(core.relative_to(ROOT)) if core.exists() else None,
        "core_sha256": digest(core) if core.exists() else None,
        "core_clauses": core_header["clauses"] if core_header else None,
        "core_unsat_verified": core_unsat,
        "passed": drat_verified and core_unsat,
    }


def copy_sample() -> dict:
    source = RESULTS
    target = EVIDENCE / "sat"
    target.mkdir(parents=True, exist_ok=True)
    names = [
        f"{SAMPLE}.cnf",
        f"{SAMPLE}.drat",
        f"{SAMPLE}.core.cnf",
        f"{SAMPLE}.drat.log",
    ]
    for name in names:
        origin = source / name
        if not origin.exists():
            raise SystemExit(f"missing sample artifact: {origin}")
        destination = target / name
        if name.endswith(".log"):
            lines = origin.read_text(encoding="utf-8").replace("\r\n", "\n").splitlines()
            destination.write_text("\n".join(line.rstrip() for line in lines) + "\n", encoding="utf-8")
        else:
            shutil.copy2(origin, destination)
    cnf = target / f"{SAMPLE}.cnf"
    proof = target / f"{SAMPLE}.drat"
    core = target / f"{SAMPLE}.core.cnf"
    return {
        "case": SAMPLE,
        "cnf": str(cnf.relative_to(ROOT)),
        "proof": str(proof.relative_to(ROOT)),
        "core": str(core.relative_to(ROOT)),
        "verify": (
            f"drat-trim {cnf.relative_to(ROOT)} {proof.relative_to(ROOT)} "
            f"-c /tmp/{SAMPLE}.core.cnf"
        ),
        "verify_core": f"cadical {core.relative_to(ROOT)}  # expected exit 20",
    }


def main() -> None:
    RESULTS.mkdir(parents=True, exist_ok=True)
    tools = {
        "cbmc": tool_info("cbmc", require_tool("cbmc")),
        "cadical": tool_info("cadical", require_tool("cadical")),
        "drat_trim": tool_info("drat-trim", require_tool("drat-trim")),
    }
    cases = []
    for module in MODULES:
        for variant in ("buggy", "fixed"):
            cases.append(run_case(module, variant, tools))

    sample = copy_sample()
    passed = all(case.get("passed") for case in cases)
    evidence = {
        "schema_version": 1,
        "tool": tools,
        "platform": platform.platform(),
        "pipeline": {
            "encoding": "CBMC --dimacs --outfile",
            "solver": "CaDiCaL DRAT proof for UNSAT",
            "proof_checker": "drat-trim -c core.cnf -w",
            "core_checker": "CaDiCaL on the emitted core",
        },
        "bound": {
            "unwind": UNWIND,
            "symbolic_input_bytes": 48,
            "note": "The DIMACS formula is CBMC's bounded encoding; no unbounded proof is claimed.",
        },
        "cases": cases,
        "sample_certificate": sample,
        "passed": passed,
        "scope_limit": (
            "The DRAT proof certifies UNSAT of the CBMC-generated bounded CNF for one harness. "
            "It does not prove the ASP.NET service or executions outside the bound."
        ),
    }
    (EVIDENCE / "sat-pipeline.json").write_text(
        json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    for case in cases:
        print(
            f"{'PASS' if case.get('passed') else 'FAIL'} "
            f"{case['module']}_{case['variant']} {case.get('solver_verdict', 'error')}"
        )
    print(f"SAT/CNF/DIMACS: {'PASS' if passed else 'FAIL'} ({len(cases)} cases)")
    raise SystemExit(0 if passed else 1)


if __name__ == "__main__":
    main()
