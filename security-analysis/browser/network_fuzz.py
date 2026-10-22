"""Browser network fault fuzzing via Chrome DevTools Protocol.

This is intentionally a browser/network-layer harness, not the mocked-fetch
checkout test.  A real API process and a real temporary PostgreSQL cluster serve
the page; Chrome's Fetch domain pauses product responses and injects malformed
bodies, error statuses or failed requests.  The oracle requires the control to
load product cards and every fault scenario to render an in-grid error state;
zero uncaught JS alone is not sufficient.  It is not a proxy/WAN test and it
does not provide code coverage.

Run:
    python3 security-analysis/browser/network_fuzz.py
"""
from __future__ import annotations

import base64
import hashlib
import json
import shutil
import struct
import subprocess
import sys
import tempfile
import time
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SECURITY = ROOT / "security-analysis"
sys.path.insert(0, str(SECURITY))
from http_fuzz import Api, Postgres, canonical_json, request  # noqa: E402

BASE = Path(__file__).resolve().parent
RESULTS = SECURITY / "results"


class FuzzCDP:
    """Minimal CDP client copied from live_smoke, with Fetch event handling."""

    def __init__(self, url: str) -> None:
        from urllib.parse import urlsplit
        target = urlsplit(url)
        import socket
        self.socket = socket.create_connection((target.hostname, target.port), timeout=15)
        key = base64.b64encode(__import__("os").urandom(16)).decode()
        self.socket.sendall(
            (f"GET {target.path} HTTP/1.1\r\nHost: {target.netloc}\r\nUpgrade: websocket\r\n"
             f"Connection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n").encode()
        )
        response = bytearray()
        while not response.endswith(b"\r\n\r\n"):
            response.extend(self.socket.recv(1))
        if b" 101 " not in response:
            raise RuntimeError("CDP websocket handshake failed")
        self.sequence = 0
        self.errors: list[str] = []
        self.scenario = "control"
        self.paused = 0
        self.product_paused = 0

    def receive(self, count: int) -> bytes:
        data = bytearray()
        while len(data) < count:
            chunk = self.socket.recv(count - len(data))
            if not chunk:
                raise RuntimeError("CDP connection closed")
            data.extend(chunk)
        return bytes(data)

    def frame(self) -> dict:
        head = self.receive(2)
        length = head[1] & 127
        if length == 126:
            length = struct.unpack("!H", self.receive(2))[0]
        elif length == 127:
            length = struct.unpack("!Q", self.receive(8))[0]
        mask = self.receive(4) if head[1] & 128 else None
        data = self.receive(length)
        if mask:
            data = bytes(value ^ mask[index % 4] for index, value in enumerate(data))
        if head[0] & 15 == 8:
            raise RuntimeError("CDP websocket closed")
        return json.loads(data)

    def send(self, method: str, params: dict | None = None) -> int:
        """Send a CDP command without waiting; needed for Fetch event handlers."""
        self.sequence += 1
        identifier = self.sequence
        data = json.dumps({"id": identifier, "method": method, "params": params or {}}).encode()
        length = len(data)
        if length < 126:
            head = bytes([0x81, 0x80 | length])
        elif length < 65536:
            head = bytes([0x81, 0x80 | 126]) + struct.pack("!H", length)
        else:
            head = bytes([0x81, 0x80 | 127]) + struct.pack("!Q", length)
        mask = __import__("os").urandom(4)
        self.socket.sendall(head + mask + bytes(value ^ mask[index % 4] for index, value in enumerate(data)))
        return identifier

    def pause(self, params: dict) -> None:
        self.paused += 1
        request_id = params["requestId"]
        url = params.get("request", {}).get("url", "")
        if not url.endswith("/api/products") and "/api/products?" not in url:
            self.send("Fetch.continueRequest", {"requestId": request_id})
            return
        self.product_paused += 1
        scenario = self.scenario
        if scenario == "control":
            self.send("Fetch.continueRequest", {"requestId": request_id})
            return
        if scenario == "network-error":
            self.send("Fetch.failRequest", {"requestId": request_id, "errorReason": "Failed"})
            return
        status = 500 if scenario == "http-500" else 503 if scenario == "http-503" else 200
        bodies = {
            "malformed-json": b'{"products":',
            "wrong-shape": b'{}',
            "empty-body": b'',
            "truncated-json": b'[{"id":1,',
            "http-500": b'{"message":"fuzz"}',
            "http-503": b'{"message":"fuzz"}',
        }
        body = bodies.get(scenario, b'{"message":"fuzz"}')
        self.send("Fetch.fulfillRequest", {
            "requestId": request_id,
            "responseCode": status,
            "responseHeaders": [{"name": "Content-Type", "value": "application/json"}],
            "body": base64.b64encode(body).decode("ascii"),
        })

    def call(self, method: str, params: dict | None = None) -> dict:
        identifier = self.send(method, params)
        while True:
            result = self.frame()
            if result.get("method") == "Runtime.exceptionThrown":
                details = result["params"]["exceptionDetails"]
                self.errors.append(details.get("exception", {}).get("description", details.get("text", "JavaScript error")).split("\n")[0])
            if result.get("method") == "Fetch.requestPaused":
                self.pause(result["params"])
                continue
            if result.get("id") == identifier:
                if "error" in result:
                    raise RuntimeError("CDP command failed: " + method)
                return result.get("result", {})

    def evaluate(self, expression: str):
        result = self.call("Runtime.evaluate", {"expression": expression, "awaitPromise": True, "returnByValue": True})
        if "exceptionDetails" in result:
            details = result["exceptionDetails"]
            raise RuntimeError(details.get("exception", {}).get("description", details.get("text", "evaluation failed")))
        return result.get("result", {}).get("value")


def http_json(base_url: str, path: str, body: dict, token: str | None = None) -> tuple[int, dict]:
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    response = request("POST", base_url + path, canonical_json(body).encode(), headers)
    try:
        parsed = json.loads(response.body.decode("utf-8"))
    except Exception:
        parsed = {}
    return response.status, parsed


def start_chrome(directory: str) -> tuple[subprocess.Popen, FuzzCDP]:
    chrome = shutil.which("google-chrome") or shutil.which("chromium")
    if not chrome:
        raise RuntimeError("Chrome/Chromium required")
    browser_log = (Path(directory) / "chrome.log").open("w+")
    process = subprocess.Popen(
        [chrome, "--headless", "--no-sandbox", "--disable-gpu", "--disable-background-networking",
         "--remote-debugging-port=0", f"--user-data-dir={directory}", "about:blank"],
        stdout=subprocess.DEVNULL, stderr=browser_log,
    )
    port_file = Path(directory) / "DevToolsActivePort"
    deadline = time.monotonic() + 15
    while not port_file.exists():
        if process.poll() is not None or time.monotonic() > deadline:
            browser_log.seek(0)
            raise RuntimeError("Chrome startup failed: " + browser_log.read()[-2000:])
        time.sleep(0.05)
    port = int(port_file.read_text().splitlines()[0])
    with urllib.request.urlopen(f"http://127.0.0.1:{port}/json/list") as response:
        pages = json.load(response)
    cdp = FuzzCDP(next(page for page in pages if page["type"] == "page")["webSocketDebuggerUrl"])
    cdp.call("Runtime.enable")
    cdp.call("Page.enable")
    cdp.call("Network.enable")
    cdp.call("Network.setCacheDisabled", {"cacheDisabled": True})
    cdp.call("Network.clearBrowserCache")
    cdp.call("Fetch.enable", {"patterns": [{"urlPattern": "*", "requestStage": "Response"}]})
    return process, cdp


def scenario_evidence(cdp: FuzzCDP, base_url: str, user: dict) -> dict:
    cdp.call("Page.addScriptToEvaluateOnNewDocument", {
        "source": f"localStorage.setItem('user', {json.dumps(json.dumps(user))});"
    })
    cdp.call("Page.navigate", {"url": base_url})
    time.sleep(0.8)
    outcomes = []
    for scenario in ("control", "malformed-json", "wrong-shape", "empty-body",
                     "truncated-json", "http-500", "http-503", "network-error"):
        cdp.scenario = scenario
        before_errors = len(cdp.errors)
        before_paused = cdp.product_paused
        cdp.call("Page.reload", {"ignoreCache": True})
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            ready = cdp.evaluate("document.readyState")
            cards = cdp.evaluate("document.querySelectorAll('#productsGrid .product-card').length")
            state = cdp.evaluate("(() => { const node = document.querySelector('#productsGrid .products-state'); return node ? node.innerText : ''; })()")
            if ready == "complete" and (cards > 0 or "Lỗi khi tải sản phẩm" in state):
                break
            time.sleep(0.05)
        time.sleep(0.2)
        text = cdp.evaluate("document.body ? document.body.innerText : ''")
        alert = cdp.evaluate("(() => { const items = Array.from(document.querySelectorAll('.alert-container .alert')); return items.length ? items[items.length - 1].textContent : ''; })()")
        product_cards = cdp.evaluate("document.querySelectorAll('#productsGrid .product-card').length")
        diagnostics = cdp.evaluate("""(() => {
            const state = document.querySelector('#productsGrid .products-state');
            return {
                activeSection: (document.querySelector('.section.active') || {}).id || '',
                alerts: Array.from(document.querySelectorAll('.alert-container .alert')).map((node) => node.textContent),
                productStateText: state ? state.innerText : '',
                productStateClass: state ? state.className : '',
                productGridHtml: ((document.querySelector('#productsGrid') || {}).innerHTML || '').slice(0, 240),
                hasUser: Boolean(localStorage.getItem('user')),
            };
        })()""")
        state_text = diagnostics.get('productStateText', '') if isinstance(diagnostics, dict) else ''
        state_class = diagnostics.get('productStateClass', '') if isinstance(diagnostics, dict) else ''
        fault_text = 'Lỗi khi tải sản phẩm'
        fault_visible = fault_text in alert or fault_text in state_text
        if scenario == 'control':
            scenario_ok = product_cards > 0 and not state_class
        else:
            scenario_ok = product_cards == 0 and fault_visible
        outcomes.append({
            "scenario": scenario,
            "javascript_errors": cdp.errors[before_errors:],
            "product_responses_intercepted": cdp.product_paused - before_paused,
            "ready_state": cdp.evaluate("document.readyState"),
            "product_cards": product_cards,
            "product_state_text": state_text,
            "product_state_class": state_class,
            "ui_alert": alert,
            "diagnostics": diagnostics,
            "ui_handles_fault": scenario == 'control' or fault_visible,
            "scenario_ok": scenario_ok,
            "body_has_error_or_empty_state": any(
                token in text for token in ("Lỗi khi tải sản phẩm", "Chưa có sản phẩm", "Đăng nhập")
            ),
        })
    return {
        "outcomes": outcomes,
        "passed": all(not item["javascript_errors"] for item in outcomes)
                  and all(item["product_responses_intercepted"] > 0 for item in outcomes)
                  and all(item["scenario_ok"] for item in outcomes),
        "all_javascript_errors": cdp.errors,
    }


def main() -> None:
    RESULTS.mkdir(parents=True, exist_ok=True)
    postgres = Postgres()
    api = Api(postgres)
    browser = None
    cdp = None
    chrome_directory = tempfile.mkdtemp(prefix="network-fuzz-chrome-")
    started = time.monotonic()
    evidence: dict = {
        "schema_version": 1,
        "passed": False,
        "run_at_utc": datetime.now(timezone.utc).isoformat(),
        "scope": {
            "layer": "Chrome CDP Fetch interception over a real local API/PostgreSQL",
            "not_claimed": "real proxy/WAN, TLS, mobile/browser matrix, code coverage, production deployment",
        },
        "limitations": [
            "One headless Chrome process and one local API process.",
            "Malformed/error responses are injected at the browser network layer; this is not an external proxy or real network partition.",
            "Only the products fetch path is faulted; login/cart/payment network faults remain future work.",
            "The UI oracle checks the products grid error state and retry control, not every visual/browser matrix.",
        ],
    }
    try:
        postgres.start()
        api.start(build=False)
        email = f"network-fuzz-{__import__('secrets').token_hex(8)}@example.test"
        register_status, _ = http_json(api.base_url, "/api/users/register", {
            "email": email, "password": "Network-Fuzz-Password!123", "fullName": "Network Fuzz",
        })
        login_status, user = http_json(api.base_url, "/api/users/login", {
            "email": email, "password": "Network-Fuzz-Password!123",
        })
        if register_status not in (200, 201) or login_status != 200 or not user.get("token"):
            raise RuntimeError("could not create browser fuzz user")
        browser, cdp = start_chrome(chrome_directory)
        evidence["browser"] = "Google Chrome/Chromium headless via CDP"
        evidence["checks"] = scenario_evidence(cdp, api.base_url + "/", user)
        evidence["passed"] = bool(evidence["checks"]["passed"])
        evidence["duration_seconds"] = round(time.monotonic() - started, 3)
        evidence["source_sha256"] = {
            str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in [
                ROOT / "security-analysis/browser/network_fuzz.py",
                ROOT / "OrderPaymentMicroservice/wwwroot/js/app.js",
                ROOT / "OrderPaymentMicroservice/wwwroot/index.html",
                ROOT / "OrderPaymentMicroservice/Program.cs",
            ]
        }
        (RESULTS / "browser_network_fuzz.results.json").write_text(
            json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
        print(json.dumps({
            "passed": evidence["passed"],
            "scenarios": len(evidence["checks"]["outcomes"]),
            "js_errors": len(evidence["checks"]["all_javascript_errors"]),
            "evidence": "security-analysis/results/browser_network_fuzz.results.json",
        }, ensure_ascii=False, indent=2))
        if not evidence["passed"]:
            raise SystemExit(1)
    finally:
        if cdp is not None:
            try:
                cdp.socket.close()
            except Exception:
                pass
        if browser is not None:
            browser.terminate()
            try:
                browser.wait(timeout=5)
            except subprocess.TimeoutExpired:
                browser.kill()
        api.cleanup()
        postgres.stop()
        if postgres.root.exists():
            shutil.rmtree(postgres.root, ignore_errors=True)
        shutil.rmtree(chrome_directory, ignore_errors=True)


if __name__ == "__main__":
    main()
