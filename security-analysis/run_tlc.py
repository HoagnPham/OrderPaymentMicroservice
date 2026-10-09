"""Run TLC on the Order-Payment specification and persist reproducible evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time


ROOT = Path(__file__).resolve().parents[1]
SPEC_DIR = ROOT / "OrderPaymentMicroservice" / "Security"
SPEC = SPEC_DIR / "OrderPayment.tla"
CONFIG = SPEC_DIR / "OrderPayment.cfg"
RESULT_DIR = ROOT / "security-analysis" / "results" / "tlc"
LOG_FILE = RESULT_DIR / "OrderPayment.tlc.log"
RESULT_FILE = RESULT_DIR / "OrderPayment.tlc.results.json"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--jar",
        type=Path,
        default=Path(os.environ["TLA2TOOLS_JAR"]) if "TLA2TOOLS_JAR" in os.environ else None,
        help="Path to the official tla2tools.jar release",
    )
    parser.add_argument("--timeout", type=int, default=120)
    args = parser.parse_args()
    if args.jar is None or not args.jar.is_file():
        parser.error("provide --jar /path/to/tla2tools.jar or set TLA2TOOLS_JAR")

    command = [
        "java", "-XX:+UseParallelGC", "-jar", str(args.jar.resolve()),
        "-config", CONFIG.name, "-workers", "1", SPEC.name,
    ]
    started = time.monotonic()
    completed = subprocess.run(
        command,
        cwd=SPEC_DIR,
        capture_output=True,
        text=True,
        timeout=args.timeout,
        check=False,
    )
    duration = round(time.monotonic() - started, 3)
    output = completed.stdout + completed.stderr
    RESULT_DIR.mkdir(parents=True, exist_ok=True)
    LOG_FILE.write_text(output, encoding="utf-8")

    generated = re.search(r"(\d+) states generated, (\d+) distinct states found", output)
    depth = re.search(r"depth of the complete state graph search is (\d+)", output)
    version = re.search(r"^(TLC2 Version .+)$", output, re.MULTILINE)
    passed = completed.returncode == 0 and "Model checking completed. No error has been found." in output
    result = {
        "passed": passed,
        "tool": version.group(1) if version else "TLC2 (version not parsed)",
        "jar_sha256": sha256(args.jar),
        "spec": str(SPEC.relative_to(ROOT)),
        "spec_sha256": sha256(SPEC),
        "config": str(CONFIG.relative_to(ROOT)),
        "config_sha256": sha256(CONFIG),
        "workers": 1,
        "timeout_seconds": args.timeout,
        "duration_seconds": duration,
        "states_generated": int(generated.group(1)) if generated else None,
        "distinct_states": int(generated.group(2)) if generated else None,
        "graph_depth": int(depth.group(1)) if depth else None,
        "invariants": [
            "TypeOK",
            "PaidStatesHavePaymentEvidence",
            "CancelledHasNoSuccessfulCharge",
            "FailedHasNoSuccessfulCharge",
            "InventoryLifecycleIsConsistent",
        ],
        "temporal_properties": ["Termination"],
        "exit_code": completed.returncode,
        "log": str(LOG_FILE.relative_to(ROOT)),
        "command": command,
    }
    RESULT_FILE.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(output, end="")
    print(f"TLC evidence: {RESULT_FILE}")
    if not passed:
        raise SystemExit(completed.returncode or 1)


if __name__ == "__main__":
    main()
