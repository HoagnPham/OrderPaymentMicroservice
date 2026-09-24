"""Require the specific sanitizer finding, not merely a nonzero exit code."""
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
RESULTS = ROOT / "results"
RESULTS.mkdir(exist_ok=True)
ENV = dict(os.environ, ASAN_OPTIONS="detect_leaks=1:halt_on_error=1",
           UBSAN_OPTIONS="halt_on_error=1:print_stacktrace=1")
checks = []


def run(name, binary, args=(), expected=None, output=None):
    result = subprocess.run([str(ROOT / "build" / binary), *args],
                            capture_output=True, text=True, env=ENV, timeout=20)
    log = result.stdout + result.stderr
    (RESULTS / f"{name}.log").write_text(log)
    if expected:
        passed = result.returncode != 0 and expected in log
    else:
        passed = (result.returncode == 0 and "Sanitizer" not in log
                  and "runtime error:" not in log)
    if output is not None:
        passed = passed and output in result.stdout
    checks.append(dict(case=name, passed=passed, exit_code=result.returncode))
    print(f"{'PASS' if passed else 'FAIL'} {name}")


for module, diagnostic in {
    "order": "LeakSanitizer: detected memory leaks",
    "payment": "AddressSanitizer: attempting double-free",
    "cart": "AddressSanitizer: heap-use-after-free",
    "product": "AddressSanitizer: heap-buffer-overflow",
    "user": "AddressSanitizer: stack-buffer-overflow",
}.items():
    run(f"{module}_buggy", f"{module}_buggy", expected=diagnostic)
    run(f"{module}_fixed", f"{module}_fixed")

for module, prefix in (("product", "Product 101: "), ("user", "Registered email: ")):
    for length in (0, 1, 31, 32, 128):
        value = "A" * length
        run(f"{module}_fixed_length_{length}", f"{module}_fixed", [value],
            output="REJECTED:" if length >= 32 else prefix + value + "\n")
    run(f"{module}_buggy_valid", f"{module}_buggy", ["demo"], output=prefix + "demo")

(RESULTS / "summary.json").write_text(json.dumps(checks, indent=2) + "\n")
raise SystemExit(0 if all(check["passed"] for check in checks) else 1)
