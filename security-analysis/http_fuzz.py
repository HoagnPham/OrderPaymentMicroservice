"""Black-box HTTP fuzzing against the real local API and PostgreSQL.

This harness is deliberately separate from the in-process .NET fuzz campaign.
It starts the normal application executable, talks to it only over HTTP, and
uses PostgreSQL queries only as a durable-state oracle.  The feedback signal is
semantic (HTTP status/error/response shape + database delta/path edge), not
code coverage.  It therefore does not claim line/branch/path coverage.

Run:
    python3 security-analysis/http_fuzz.py --iterations 500

The harness is opt-in, uses a temporary PostgreSQL cluster and ledger, and
writes security-analysis/results/http_fuzz.results.json.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
import random
import re
import shutil
import signal
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from decimal import Decimal
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
RESULTS = ROOT / "security-analysis" / "results"
APP_PROJECT = ROOT / "OrderPaymentMicroservice" / "OrderPaymentMicroservice.csproj"
APP_DLL = ROOT / "OrderPaymentMicroservice" / "bin" / "Debug" / "net10.0" / "OrderPaymentMicroservice.dll"
DB_NAME = "order_payment_http_fuzz"
USER_AGENT = "orderpayment-http-fuzz/1"
MAX_BODY = 1_000_000


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def reserve_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def run(command: list[str], *, cwd: Path = ROOT, timeout: int = 60, check: bool = True) -> subprocess.CompletedProcess[str]:
    completed = subprocess.run(command, cwd=cwd, text=True, capture_output=True, timeout=timeout, check=False)
    if check and completed.returncode != 0:
        raise RuntimeError(f"command failed ({completed.returncode}): {' '.join(command)}\n{completed.stdout}{completed.stderr}")
    return completed


def postgres_bin() -> Path:
    configured = os.environ.get("POSTGRES_BIN")
    if configured and (Path(configured) / "postgres").exists():
        return Path(configured)
    candidates = sorted(Path("/usr/lib/postgresql").glob("*/bin"), reverse=True)
    for candidate in candidates:
        if (candidate / "postgres").exists():
            return candidate
    raise RuntimeError("PostgreSQL server binaries not found; set POSTGRES_BIN")


class Postgres:
    def __init__(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="orderpayment-http-fuzz-pg-"))
        self.data = self.root / "data"
        self.socket = self.root / "socket"
        self.log = self.root / "postgres.log"
        self.bin = postgres_bin()
        self.port = reserve_port()
        self.user = os.environ.get("USER") or os.environ.get("LOGNAME") or "postgres"
        self.started = False

    @property
    def connection_string(self) -> str:
        return f"Host=127.0.0.1;Port={self.port};Database={DB_NAME};Username={self.user};Pooling=false"

    def _run(self, executable: str, *arguments: str, timeout: int = 60) -> None:
        run([str(self.bin / executable), *arguments], timeout=timeout)

    def start(self) -> "Postgres":
        self.socket.mkdir(parents=True, exist_ok=True)
        self._run("initdb", "-D", str(self.data), "-A", "trust", "--no-locale", "--encoding=UTF8")
        self._run(
            "pg_ctl", "-D", str(self.data), "-w", "start", "-l", str(self.log), "-o",
            f"-h 127.0.0.1 -p {self.port} -k {self.socket}",
        )
        self.started = True
        self._run("createdb", "-h", "127.0.0.1", "-p", str(self.port), "-U", self.user, DB_NAME)
        return self

    def psql(self, sql: str) -> list[list[str]]:
        completed = run(
            [str(self.bin / "psql"), "-h", "127.0.0.1", "-p", str(self.port), "-U", self.user,
             "-d", DB_NAME, "-At", "-F", "|", "-v", "ON_ERROR_STOP=1", "-c", sql],
            timeout=30,
        )
        return [line.split("|") for line in completed.stdout.splitlines() if line != ""]

    def stop(self) -> None:
        if self.started:
            try:
                self._run("pg_ctl", "-D", str(self.data), "-w", "stop", "-m", "fast", timeout=30)
            except Exception:
                pass
            self.started = False


class Api:
    def __init__(self, postgres: Postgres) -> None:
        self.postgres = postgres
        self.port = reserve_port()
        self.base_url = f"http://127.0.0.1:{self.port}"
        self.root = Path(tempfile.mkdtemp(prefix="orderpayment-http-fuzz-app-"))
        self.ledger = self.root / "ledger"
        self.ledger.mkdir(parents=True, exist_ok=True)
        self.stdout = self.root / "api.stdout.log"
        self.stderr = self.root / "api.stderr.log"
        self.process: subprocess.Popen[bytes] | None = None
        self._stdout = None
        self._stderr = None

    def start(self, *, build: bool) -> "Api":
        if build or not APP_DLL.exists():
            run(["dotnet", "build", str(APP_PROJECT), "--nologo", "--no-restore"], timeout=180)
        environment = os.environ.copy()
        environment.update({
            "ASPNETCORE_ENVIRONMENT": "Development",
            "ASPNETCORE_URLS": self.base_url,
            "ConnectionStrings__DefaultConnection": self.postgres.connection_string,
            "Jwt__Key": "http-fuzz-key-at-least-32-characters-long",
            "Jwt__Issuer": "HttpFuzz",
            "Jwt__Audience": "HttpFuzz",
            "PaymentGateway__Mode": "DurableFake",
            "PaymentGateway__StoreDirectory": str(self.ledger),
            "PaymentRecovery__Enabled": "false",
            "ApiAdmission__ApiPermitLimit": "100000",
            "ApiAdmission__AuthenticationPermitLimit": "10000",
            "ApiAdmission__WindowSeconds": "60",
            "Logging__LogLevel__Default": "Warning",
        })
        self._stdout = self.stdout.open("wb")
        self._stderr = self.stderr.open("wb")
        self.process = subprocess.Popen(
            ["dotnet", str(APP_DLL)], cwd=ROOT / "OrderPaymentMicroservice",
            env=environment, stdout=self._stdout, stderr=self._stderr, start_new_session=True,
        )
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                diagnostic = self.stderr.read_text(errors="replace")[-4000:] if self.stderr.exists() else ""
                raise RuntimeError(f"API exited during startup ({self.process.returncode}); see {self.stderr}\n{diagnostic}")
            try:
                response = request("GET", self.base_url + "/health/ready", timeout=2)
                if response.status == 200:
                    return self
            except Exception:
                pass
            time.sleep(0.1)
        raise RuntimeError("API did not become ready within 30 seconds")

    def stop(self) -> None:
        if self.process is not None and self.process.poll() is None:
            try:
                os.killpg(self.process.pid, signal.SIGTERM)
                self.process.wait(timeout=10)
            except Exception:
                try:
                    os.killpg(self.process.pid, signal.SIGKILL)
                except Exception:
                    pass
        for stream in (self._stdout, self._stderr):
            if stream is not None:
                stream.close()
        self._stdout = self._stderr = None
        self.process = None

    def cleanup(self) -> None:
        self.stop()
        if self.root.exists():
            shutil.rmtree(self.root, ignore_errors=True)


class Response:
    def __init__(self, status: int, headers: dict[str, str], body: bytes, elapsed_ms: float) -> None:
        self.status = status
        self.headers = headers
        self.body = body
        self.elapsed_ms = elapsed_ms

    def json(self) -> Any:
        try:
            return json.loads(self.body.decode("utf-8"))
        except Exception:
            return None

    def text(self) -> str:
        return self.body.decode("utf-8", errors="replace")


_OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def request(method: str, url: str, body: bytes | None = None, headers: dict[str, str] | None = None,
            timeout: float = 10) -> Response:
    started = time.monotonic()
    req = urllib.request.Request(url, data=body, method=method, headers={"User-Agent": USER_AGENT, **(headers or {})})
    try:
        with _OPENER.open(req, timeout=timeout) as response:
            payload = response.read(MAX_BODY + 1)
            if len(payload) > MAX_BODY:
                payload = payload[:MAX_BODY]
            return Response(response.status, dict(response.headers), payload, (time.monotonic() - started) * 1000)
    except urllib.error.HTTPError as error:
        payload = error.read(MAX_BODY + 1)
        if len(payload) > MAX_BODY:
            payload = payload[:MAX_BODY]
        return Response(error.code, dict(error.headers), payload, (time.monotonic() - started) * 1000)


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False, default=str)


def number(value: float | int) -> int | float:
    if isinstance(value, int) or float(value).is_integer():
        return int(value)
    return float(value)


def route_template(path: str) -> str:
    path = path.split("?", 1)[0]
    return re.sub(r"/-?\d+(?=/|$)", "/{id}", path)


def error_code(body: Any, text: str) -> str:
    if isinstance(body, dict):
        for key in ("code", "errorCode", "error"):
            value = body.get(key)
            if isinstance(value, str) and value:
                return value[:80]
        message = body.get("message")
        if isinstance(message, str) and message:
            text = message
    value = " ".join(text.split())
    if not value:
        return "EMPTY"
    value = re.sub(r"\b\d+\b", "#", value)
    return value[:80]


class InvariantError(RuntimeError):
    pass


class Harness:
    def __init__(self, postgres: Postgres, api: Api, *, seed: int, iterations: int) -> None:
        self.postgres = postgres
        self.api = api
        self.seed = seed
        self.iterations = iterations
        self.random = random.Random(seed)
        self.user_token = ""
        self.admin_token = ""
        self.user_id = 0
        self.admin_id = 0
        self.product_id = 0
        self.product_price = Decimal("0")
        self.cart_id = 0
        self.cart_item_id = 0
        self.order_id = 0
        self.order_total = Decimal("0")
        self.payment_id = 0
        self.context: dict[str, Any] = {}
        self.corpus: list[dict[str, Any]] = []
        self.features: set[str] = set()
        self.edges: set[str] = set()
        self.route_status: set[str] = set()
        self.traces: list[dict[str, Any]] = []
        self.previous_feature: str | None = None
        self.failures: list[dict[str, Any]] = []
        self.server_errors = 0
        self.rate_limited = 0
        self.invariant_checks = 0
        self.requests_sent = 0
        self.setup_requests = 0
        self.business_requests = 0
        self.last_snapshot: tuple[str, ...] = ()

    # ---------- HTTP helpers ----------
    def call(self, method: str, path: str, *, token: str | None = None, body: Any = None,
             raw_body: bytes | None = None, headers: dict[str, str] | None = None) -> Response:
        if body is not None and raw_body is not None:
            raise ValueError("body and raw_body are mutually exclusive")
        if raw_body is not None:
            payload = raw_body
            request_headers = {"Content-Type": "application/json", **(headers or {})}
        elif body is not None:
            payload = canonical_json(body).encode("utf-8")
            request_headers = {"Content-Type": "application/json", **(headers or {})}
        else:
            payload = None
            request_headers = dict(headers or {})
        if token:
            request_headers["Authorization"] = f"Bearer {token}"
        response = request(method, self.api.base_url + path, payload, request_headers)
        self.requests_sent += 1
        if response.status >= 500:
            self.server_errors += 1
        if response.status == 429:
            self.rate_limited += 1
        self.route_status.add(f"{method} {route_template(path)} -> {response.status}")
        return response

    def json_call(self, method: str, path: str, **kwargs: Any) -> tuple[Response, Any]:
        response = self.call(method, path, **kwargs)
        body = response.json()
        if response.status >= 500:
            self.record_failure("HTTP_5XX", {"method": method, "path": path, "status": response.status,
                                             "body": response.text()[:500]})
        return response, body

    def record_failure(self, kind: str, detail: dict[str, Any]) -> None:
        self.failures.append({"kind": kind, **detail})

    # ---------- DB oracle ----------
    def snapshot(self) -> tuple[str, ...]:
        rows = self.postgres.psql("""
SELECT
  (SELECT count(*) FROM "Users")::text,
  (SELECT count(*) FROM "Products")::text,
  (SELECT count(*) FROM "ShoppingCarts")::text,
  (SELECT count(*) FROM "CartItems")::text,
  (SELECT count(*) FROM "Orders")::text,
  (SELECT count(*) FROM "OrderItems")::text,
  (SELECT count(*) FROM "Payments")::text,
  (SELECT COALESCE(sum("Stock"),0)::text FROM "Products"),
  (SELECT COALESCE(sum("TotalPrice"),0)::text FROM "ShoppingCarts"),
  (SELECT COALESCE(sum("TotalAmount"),0)::text FROM "Orders"),
  (SELECT COALESCE(string_agg("Status"::text || '=' || c, ',' ORDER BY "Status"),'') FROM
     (SELECT "Status", count(*) c FROM "Orders" GROUP BY "Status") s),
  (SELECT COALESCE(string_agg("Status"::text || '=' || c, ',' ORDER BY "Status"),'') FROM
     (SELECT "Status", count(*) c FROM "Payments" GROUP BY "Status") s)
""")
        return tuple(rows[0]) if rows else ()

    def check_invariants(self) -> None:
        self.invariant_checks += 1
        rows = self.postgres.psql("""
SELECT 'product_stock_or_price', count(*)::text FROM "Products" WHERE "Stock" < 0 OR "Price" <= 0
UNION ALL SELECT 'order_item_quantity_or_subtotal', count(*)::text FROM "OrderItems" WHERE "Quantity" <= 0 OR "SubTotal" < 0
UNION ALL SELECT 'cart_item_quantity_or_subtotal', count(*)::text FROM "CartItems" WHERE "Quantity" <= 0 OR "SubTotal" < 0
UNION ALL SELECT 'cart_total_negative', count(*)::text FROM "ShoppingCarts" WHERE "TotalPrice" < 0
UNION ALL SELECT 'order_total_nonpositive', count(*)::text FROM "Orders" WHERE "TotalAmount" <= 0
UNION ALL SELECT 'payment_amount_nonpositive', count(*)::text FROM "Payments" WHERE "Amount" <= 0
UNION ALL SELECT 'cart_total_mismatch', count(*)::text FROM "ShoppingCarts" c
  WHERE abs(c."TotalPrice" - COALESCE((SELECT sum(i."SubTotal") FROM "CartItems" i WHERE i."CartId"=c."Id"),0)) > 0.01
UNION ALL SELECT 'order_total_mismatch', count(*)::text FROM "Orders" o
  WHERE abs(o."TotalAmount" - COALESCE((SELECT sum(i."SubTotal") FROM "OrderItems" i WHERE i."OrderId"=o."Id"),0)) > 0.01
UNION ALL SELECT 'payment_order_amount_mismatch', count(*)::text FROM "Payments" p
  JOIN "Orders" o ON o."Id"=p."OrderId" WHERE abs(p."Amount" - o."TotalAmount") > 0.01
UNION ALL SELECT 'duplicate_payment_per_order', count(*)::text FROM
  (SELECT "OrderId" FROM "Payments" GROUP BY "OrderId" HAVING count(*) > 1) d
""")
        violations = {name: int(count) for name, count in rows if int(count) != 0}
        if violations:
            self.record_failure("DB_INVARIANT", {"violations": violations})
            raise InvariantError(str(violations))

    # ---------- feature feedback ----------
    def feature(self, method: str, path: str, response: Response, before: tuple[str, ...], after: tuple[str, ...]) -> str:
        body = response.json()
        delta = tuple(f"{index}:{a}->{b}" for index, (a, b) in enumerate(zip(before, after)) if a != b)
        shape = tuple(sorted(body.keys())) if isinstance(body, dict) else (type(body).__name__,)
        signature = canonical_json({
            "route": route_template(path),
            "method": method,
            "status": response.status,
            "error": error_code(body, response.text()),
            "shape": shape,
            "delta": delta,
        })
        return signature

    def touch(self, method: str, path: str, response: Response, before: tuple[str, ...]) -> None:
        after = self.snapshot()
        signature = self.feature(method, path, response, before, after)
        self.features.add(signature)
        if self.previous_feature is not None:
            self.edges.add(canonical_json([self.previous_feature, signature]))
        self.previous_feature = signature
        self.last_snapshot = after
        if method != "GET":
            self.check_invariants()

    # ---------- setup ----------
    def register_and_login(self, label: str) -> tuple[int, str]:
        email = f"http-fuzz-{label}-{self.random.getrandbits(48):012x}@example.test"
        password = "HttpFuzz-Password!123"
        response, body = self.json_call("POST", "/api/users/register", body={
            "email": email, "password": password, "fullName": f"HTTP Fuzz {label}",
        })
        if response.status not in (200, 201):
            raise RuntimeError(f"register failed: {response.status} {response.text()}")
        login, login_body = self.json_call("POST", "/api/users/login", body={"email": email, "password": password})
        if login.status != 200 or not isinstance(login_body, dict) or not login_body.get("token"):
            raise RuntimeError(f"login failed: {login.status} {login.text()}")
        return int(login_body["id"]), str(login_body["token"])

    def setup_valid_flow(self) -> None:
        products, products_body = self.json_call("GET", "/api/products")
        if products.status != 200 or not isinstance(products_body, list) or not products_body:
            raise RuntimeError("no seed products")
        product = products_body[0]
        self.product_id = int(product["id"])
        self.product_price = Decimal(str(product["price"]))
        self.user_id, self.user_token = self.register_and_login("customer")
        self.admin_id, _ = self.register_and_login("admin")
        self.postgres.psql(f'UPDATE "Users" SET "Role"=\'Admin\' WHERE "Id"={self.admin_id}')
        admin_email = self.postgres.psql(f'SELECT "Email" FROM "Users" WHERE "Id"={self.admin_id}')[0][0]
        login, login_body = self.json_call("POST", "/api/users/login", body={
            "email": admin_email, "password": "HttpFuzz-Password!123",
        })
        if login.status != 200 or not isinstance(login_body, dict):
            raise RuntimeError(f"admin login failed: {login.status} {login.text()}")
        self.admin_token = str(login_body["token"])

        cart_response, cart_body = self.json_call("GET", f"/api/cart/user/{self.user_id}", token=self.user_token)
        if cart_response.status != 200 or not isinstance(cart_body, dict):
            raise RuntimeError(f"cart setup failed: {cart_response.status} {cart_response.text()}")
        self.cart_id = int(cart_body["id"])

        add_response, add_body = self.json_call("POST", f"/api/cart/user/{self.user_id}/add", token=self.user_token,
                                                body={"productId": self.product_id, "quantity": 1})
        if add_response.status != 200 or not isinstance(add_body, dict) or not add_body.get("items"):
            raise RuntimeError(f"cart add failed: {add_response.status} {add_response.text()}")
        self.cart_item_id = int(add_body["items"][0]["id"])

        total = number(self.product_price)
        order_response, order_body = self.json_call("POST", "/api/orders", token=self.user_token, body={
            "customerId": self.user_id, "totalAmount": total, "address": "HTTP Fuzz Street", "city": "Test City",
            "items": [{"productId": self.product_id, "quantity": 1, "unitPrice": total}],
        }, headers={"Idempotency-Key": f"http-fuzz-order-{self.random.getrandbits(48):012x}"})
        if order_response.status != 201 or not isinstance(order_body, dict):
            raise RuntimeError(f"order setup failed: {order_response.status} {order_response.text()}")
        self.order_id = int(order_body["id"])
        self.order_total = Decimal(str(order_body["totalAmount"]))

        payment_response, payment_body = self.json_call("POST", "/api/payments", token=self.user_token, body={
            "orderId": self.order_id, "amount": number(self.order_total),
        }, headers={"Idempotency-Key": f"http-fuzz-payment-{self.random.getrandbits(48):012x}"})
        if payment_response.status != 201 or not isinstance(payment_body, dict):
            raise RuntimeError(f"payment setup failed: {payment_response.status} {payment_response.text()}")
        self.payment_id = int(payment_body["id"])
        self.context = {
            "user_id": self.user_id, "admin_id": self.admin_id, "product_id": self.product_id,
            "cart_id": self.cart_id, "cart_item_id": self.cart_item_id,
            "order_id": self.order_id, "payment_id": self.payment_id,
        }

    # ---------- seed corpus ----------
    def render(self, value: Any) -> Any:
        if isinstance(value, str):
            match = re.fullmatch(r"\$([a-z_]+)", value)
            if match:
                return self.context.get(match.group(1), value)
            return re.sub(
                r"\$([a-z_]+)",
                lambda item: str(self.context.get(item.group(1), item.group(0))),
                value,
            )
        if isinstance(value, list):
            return [self.render(item) for item in value]
        if isinstance(value, dict):
            return {key: self.render(item) for key, item in value.items()}
        return value

    def seed_requests(self) -> list[dict[str, Any]]:
        user = self.user_token
        admin = self.admin_token
        item = {"productId": self.product_id, "quantity": 1}
        order_body = {
            "customerId": "$user_id", "totalAmount": number(self.product_price),
            "address": "Seed address", "city": "Seed city",
            "items": [{"productId": "$product_id", "quantity": 1, "unitPrice": number(self.product_price)}],
        }
        return [
            {"name": "products", "method": "GET", "path": "/api/products", "token": None},
            {"name": "product-valid", "method": "GET", "path": "/api/products/$product_id", "token": None},
            {"name": "product-zero", "method": "GET", "path": "/api/products/0", "token": None},
            {"name": "product-negative", "method": "GET", "path": "/api/products/-1", "token": None},
            {"name": "product-large", "method": "GET", "path": "/api/products/2147483647", "token": None},
            {"name": "category-valid", "method": "GET", "path": "/api/products/category/Food", "token": None},
            {"name": "category-empty", "method": "GET", "path": "/api/products/category/", "token": None},
            {"name": "cart-own", "method": "GET", "path": "/api/cart/user/$user_id", "token": user},
            {"name": "cart-other", "method": "GET", "path": "/api/cart/user/$admin_id", "token": user},
            {"name": "cart-anonymous", "method": "GET", "path": "/api/cart/user/$user_id", "token": None},
            {"name": "cart-add", "method": "POST", "path": "/api/cart/user/$user_id/add", "token": user, "body": item},
            {"name": "cart-add-zero", "method": "POST", "path": "/api/cart/user/$user_id/add", "token": user,
             "body": {"productId": self.product_id, "quantity": 0}},
            {"name": "cart-add-negative", "method": "POST", "path": "/api/cart/user/$user_id/add", "token": user,
             "body": {"productId": self.product_id, "quantity": -1}},
            {"name": "cart-add-missing", "method": "POST", "path": "/api/cart/user/$user_id/add", "token": user,
             "body": {"productId": 2147483647, "quantity": 1}},
            {"name": "cart-update", "method": "PUT", "path": "/api/cart/$cart_id/item/$cart_item_id", "token": user,
             "body": {"quantity": 2}},
            {"name": "cart-update-zero", "method": "PUT", "path": "/api/cart/$cart_id/item/$cart_item_id", "token": user,
             "body": {"quantity": 0}},
            {"name": "cart-update-invalid", "method": "PUT", "path": "/api/cart/$cart_id/item/$cart_item_id", "token": user,
             "body": {"quantity": -1}},
            {"name": "orders-own", "method": "GET", "path": "/api/orders/mine", "token": user},
            {"name": "order-valid", "method": "GET", "path": "/api/orders/$order_id", "token": user},
            {"name": "order-other", "method": "GET", "path": "/api/orders/$order_id", "token": admin},
            {"name": "order-zero", "method": "GET", "path": "/api/orders/0", "token": user},
            {"name": "order-create-valid", "method": "POST", "path": "/api/orders", "token": user,
             "body": order_body, "headers": {"Idempotency-Key": "seed-order-valid"}},
            {"name": "order-create-zero", "method": "POST", "path": "/api/orders", "token": user,
             "body": {"customerId": self.user_id, "totalAmount": 0, "address": "x", "city": "y",
                      "items": [{"productId": self.product_id, "quantity": 0, "unitPrice": 0}]}},
            {"name": "order-create-missing-item", "method": "POST", "path": "/api/orders", "token": user,
             "body": {"customerId": self.user_id, "totalAmount": 1, "address": "x", "city": "y", "items": []}},
            {"name": "payment-own", "method": "GET", "path": "/api/payments/$payment_id", "token": user},
            {"name": "payment-other", "method": "GET", "path": "/api/payments/$payment_id", "token": admin},
            {"name": "payment-zero", "method": "GET", "path": "/api/payments/0", "token": user},
            {"name": "payment-create-invalid-amount", "method": "POST", "path": "/api/payments", "token": user,
             "body": {"orderId": self.order_id, "amount": 0}, "headers": {"Idempotency-Key": "seed-payment-invalid"}},
            {"name": "admin-confirm", "method": "POST", "path": "/api/orders/$order_id/confirm", "token": admin},
            {"name": "admin-complete", "method": "POST", "path": "/api/orders/$order_id/complete", "token": admin},
            {"name": "customer-confirm", "method": "POST", "path": "/api/orders/$order_id/confirm", "token": user},
            {"name": "admin-refund", "method": "POST", "path": "/api/payments/$payment_id/refund", "token": admin,
             "headers": {"Idempotency-Key": "seed-refund"}},
            {"name": "operations-status", "method": "GET", "path": "/api/operations/status", "token": admin},
            {"name": "operations-customer", "method": "GET", "path": "/api/operations/status", "token": user},
            {"name": "unknown-route", "method": "GET", "path": "/api/does-not-exist", "token": None},
            {"name": "malformed-json", "method": "POST", "path": "/api/users/register", "token": None,
             "raw_body": b'{"email":'},
            {"name": "null-json", "method": "POST", "path": "/api/users/register", "token": None,
             "raw_body": b"null"},
            {"name": "array-json", "method": "POST", "path": "/api/orders", "token": user, "raw_body": b"[]"},
            {"name": "wrong-type", "method": "POST", "path": "/api/cart/user/$user_id/add", "token": user,
             "body": {"productId": "not-a-number", "quantity": []}},
            {"name": "bad-auth", "method": "GET", "path": "/api/orders/mine", "token": "not-a-jwt"},
        ]

    # ---------- valid business paths ----------
    def business_flow(self, mode: str) -> None:
        """Drive a real create/pay/transition flow so the fuzzer also covers 2xx paths."""
        quantity = 1
        total = number(self.product_price * quantity)
        key = f"http-fuzz-flow-{self.random.getrandbits(64):016x}"
        order = {
            "customerId": self.user_id, "totalAmount": total,
            "address": "Flow address", "city": "Flow city",
            "items": [{"productId": self.product_id, "quantity": quantity, "unitPrice": total}],
        }
        create = self.execute({
            "method": "POST", "path": "/api/orders", "token": self.user_token,
            "body": order, "headers": {"Idempotency-Key": key},
        })
        if create is None or create.status != 201:
            return
        order_body = create.json()
        if not isinstance(order_body, dict):
            return
        order_id = int(order_body["id"])
        order_total = Decimal(str(order_body["totalAmount"]))
        self.execute({"method": "GET", "path": f"/api/orders/{order_id}", "token": self.user_token})
        if mode == "cancel":
            self.execute({"method": "POST", "path": f"/api/orders/{order_id}/cancel", "token": self.user_token})
            self.execute({"method": "GET", "path": f"/api/orders/{order_id}", "token": self.user_token})
            return
        payment = self.execute({
            "method": "POST", "path": "/api/payments", "token": self.user_token,
            "body": {"orderId": order_id, "amount": number(order_total)},
            "headers": {"Idempotency-Key": f"http-fuzz-flow-pay-{self.random.getrandbits(64):016x}"},
        })
        if payment is None or payment.status != 201:
            return
        payment_body = payment.json()
        if not isinstance(payment_body, dict):
            return
        payment_id = int(payment_body["id"])
        self.execute({"method": "GET", "path": f"/api/payments/{payment_id}", "token": self.user_token})
        if mode == "refund":
            self.execute({
                "method": "POST", "path": f"/api/payments/{payment_id}/refund", "token": self.admin_token,
                "headers": {"Idempotency-Key": f"http-fuzz-flow-refund-{self.random.getrandbits(64):016x}"},
            })
            self.execute({"method": "GET", "path": f"/api/payments/{payment_id}", "token": self.user_token})
        elif mode == "complete":
            self.execute({"method": "POST", "path": f"/api/orders/{order_id}/confirm", "token": self.admin_token})
            self.execute({"method": "POST", "path": f"/api/orders/{order_id}/complete", "token": self.admin_token})
            self.execute({"method": "GET", "path": f"/api/orders/{order_id}", "token": self.user_token})

    # ---------- mutations ----------
    def boundary_values(self) -> list[Any]:
        return [
            0, -1, 1, 2, 2147483647, -2147483648, 999999999, 10000, 10001,
            "", " ", "A" * 256, "A" * 5000, "💥", "%00", "' OR 1=1 --", "<script>",
            None, True, False, [], {}, {"unexpected": "object"},
        ]

    def mutate_request(self, base: dict[str, Any]) -> dict[str, Any]:
        request = copy.deepcopy(base)
        request.pop("name", None)
        has_body = isinstance(request.get("body"), dict) or "raw_body" in request
        choice = self.random.randrange(5)
        if choice <= 1 and has_body:
            if "raw_body" in request:
                request["raw_body"] = self.random.choice([
                    b"{", b"[]", b"null", b'"string"', b'{"email":"x"}', b"\xff\xfe\x00",
                    b'{"quantity":-1}', b'{"quantity":1e999}',
                ])
            else:
                body = request["body"]
                if body and self.random.random() < 0.65:
                    key = self.random.choice(list(body))
                    body[key] = copy.deepcopy(self.random.choice(self.boundary_values()))
                elif body:
                    body.pop(self.random.choice(list(body)), None)
                else:
                    body["fuzz"] = self.random.choice(self.boundary_values())
        elif choice == 2 or (choice >= 3 and not has_body):
            path = str(self.render(request.get("path", "/api/products")))
            parts = path.split("/")
            numeric = [index for index, part in enumerate(parts) if part.lstrip("-").isdigit()]
            if numeric:
                parts[self.random.choice(numeric)] = self.random.choice(
                    ["0", "-1", "1", "2", "2147483647", "999999999"]
                )
            else:
                # Preserve the route shape; replace the last non-empty segment with a
                # URL-safe boundary token.  Do not append an arbitrary suffix.
                for index in range(len(parts) - 1, -1, -1):
                    if parts[index]:
                        parts[index] = self.random.choice(["0", "-1", "Food", "null", "%2e%2e"])
                        break
            request["path"] = "/".join(parts)
        elif choice == 3:
            request["headers"] = dict(request.get("headers") or {})
            request["headers"]["Idempotency-Key"] = self.random.choice([
                "", "A", "A" * 128, "A" * 129, "space key", "quote'key", "' OR 1=1 --",
            ])
        else:
            request["raw_body"] = self.random.choice([
                b"{", b"[]", b"null", b'"string"', b'{"email":"x"}', b"\xff\xfe\x00",
                b'{"quantity":-1}', b'{"quantity":1e999}',
            ])
            request.pop("body", None)
        return request

    def execute_seed(self, seed_request: dict[str, Any]) -> None:
        self.execute(seed_request)

    def execute(self, request_spec: dict[str, Any]) -> Response | None:
        method = str(request_spec.get("method", "GET"))
        path = self.render(str(request_spec.get("path", "/")))
        token = request_spec.get("token")
        if token == "$user":
            token = self.user_token
        elif token == "$admin":
            token = self.admin_token
        before = self.snapshot()
        try:
            response = self.call(method, path, token=token, body=self.render(request_spec.get("body")),
                                 raw_body=request_spec.get("raw_body"), headers=request_spec.get("headers"))
        except Exception as error:
            self.record_failure("TRANSPORT", {"method": method, "path": path, "error": repr(error)})
            return None
        self.traces.append({
            "method": method, "path": path, "status": response.status,
            "elapsed_ms": round(response.elapsed_ms, 3),
            "body_bytes": len(response.body),
        })
        self.touch(method, path, response, before)
        return response

    def run(self) -> dict[str, Any]:
        self.setup_valid_flow()
        self.setup_requests = self.requests_sent
        seeds = self.seed_requests()
        for seed_request in seeds:
            self.execute_seed(seed_request)
            self.corpus.append({key: value for key, value in seed_request.items() if key != "name"})
        # Deterministic 2xx/3xx business paths: cancel, refund and completion.
        business_start = self.requests_sent
        for index in range(9):
            self.business_flow(("cancel", "refund", "complete")[index % 3])
        self.business_requests = self.requests_sent - business_start
        for _ in range(self.iterations):
            base = copy.deepcopy(self.random.choice(self.corpus))
            candidate = self.mutate_request(base)
            before_features = len(self.features)
            before_edges = len(self.edges)
            response = self.execute(candidate)
            useful_status = response is not None and response.status != 405
            if useful_status and (len(self.features) > before_features or len(self.edges) > before_edges):
                candidate.pop("name", None)
                self.corpus.append(candidate)
                if len(self.corpus) > 500:
                    # Keep the original seeds and the most recently useful mutations.
                    self.corpus = self.corpus[:len(seeds)] + self.corpus[-(500 - len(seeds)):]
        self.check_invariants()
        final = self.snapshot()
        return self.evidence(final)

    def evidence(self, final: tuple[str, ...]) -> dict[str, Any]:
        stderr = self.api.stderr.read_text(errors="replace") if self.api.stderr.exists() else ""
        stdout = self.api.stdout.read_text(errors="replace") if self.api.stdout.exists() else ""
        combined = stdout + "\n" + stderr
        unhandled = "Unhandled exception" in combined or "Application startup exception" in combined
        process_alive = self.api.process is not None and self.api.process.poll() is None
        passed = not self.failures and self.server_errors == 0 and not unhandled and process_alive
        status_counts: dict[str, int] = {}
        for trace in self.traces:
            key = str(trace["status"])
            status_counts[key] = status_counts.get(key, 0) + 1
        return {
            "schema_version": 1,
            "passed": passed,
            "run_at_utc": utc_now(),
            "seed": self.seed,
            "iterations_requested": self.iterations,
            "requests_executed": self.requests_sent,
            "scope": {
                "target": "normal OrderPaymentMicroservice executable over HTTP",
                "database": "temporary local PostgreSQL cluster",
                "transport": "HTTP/1.1 via urllib; no in-process WebApplicationFactory",
                "feedback": "semantic features: route/status/error/response shape/DB delta + semantic path edges",
                "not_claimed": "code line/branch/path coverage; coverage-guided engine; production deployment; real payment provider",
            },
            "corpus": {
                "seed_requests": len(self.seed_requests()),
                "setup_requests": self.setup_requests,
                "business_flows": 9,
                "business_requests": self.business_requests,
                "retained_corpus": len(self.corpus),
                "mutated_requests": self.iterations,
                "total_requests": self.requests_sent,
            },
            "coverage": {
                "kind": "SEMANTIC_FEEDBACK_NOT_CODE_COVERAGE",
                "unique_features": len(self.features),
                "unique_semantic_edges": len(self.edges),
                "unique_route_status_pairs": len(self.route_status),
                "routes": sorted({item.split(" -> ")[0] for item in self.route_status}),
            },
            "database": {
                "invariant_checks": self.invariant_checks,
                "final_snapshot": list(final),
                "final_order_statuses": final[10] if len(final) > 10 else "",
                "final_payment_statuses": final[11] if len(final) > 11 else "",
                "violations": [failure for failure in self.failures if failure["kind"] == "DB_INVARIANT"],
            },
            "http": {
                "5xx": self.server_errors,
                "429": self.rate_limited,
                "status_counts": status_counts,
                "failures": self.failures,
            },
            "process": {
                "alive_at_end": process_alive,
                "unhandled_exception_in_log": unhandled,
                "logs": {
                    "stdout_sha256": hashlib.sha256(stdout.encode("utf-8")).hexdigest(),
                    "stderr_sha256": hashlib.sha256(stderr.encode("utf-8")).hexdigest(),
                    "stdout_bytes": len(stdout.encode("utf-8")),
                    "stderr_bytes": len(stderr.encode("utf-8")),
                },
            },
            "trace_tail": self.traces[-40:],
            "source_sha256": {
                str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
                for path in [
                    ROOT / "security-analysis/http_fuzz.py",
                    ROOT / "OrderPaymentMicroservice/OrderPaymentMicroservice.csproj",
                    ROOT / "OrderPaymentMicroservice/Program.cs",
                    ROOT / "OrderPaymentMicroservice/Controllers/UsersController.cs",
                    ROOT / "OrderPaymentMicroservice/Controllers/CartController.cs",
                    ROOT / "OrderPaymentMicroservice/Controllers/OrdersController.cs",
                    ROOT / "OrderPaymentMicroservice/Controllers/PaymentsController.cs",
                ]
            },
            "limitations": [
                "Semantic feedback is not code coverage: the harness does not instrument assemblies or map basic blocks/edges.",
                "One local desktop/process topology; no distributed deployment, real gateway, TLS/proxy or network partition.",
                "DB checks are invariant queries after mutating requests, not a complete concurrency or transaction proof.",
                "The corpus and mutation set are bounded and deterministic; PASS is only for this seed/budget/API surface.",
            ],
        }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--iterations", type=int, default=500)
    parser.add_argument("--seed", type=int, default=20261022)
    parser.add_argument("--no-build", action="store_true")
    args = parser.parse_args()
    if args.iterations < 0:
        parser.error("--iterations must be >= 0")
    RESULTS.mkdir(parents=True, exist_ok=True)
    postgres = Postgres()
    api = Api(postgres)
    started = time.monotonic()
    try:
        postgres.start()
        api.start(build=not args.no_build)
        harness = Harness(postgres, api, seed=args.seed, iterations=args.iterations)
        evidence = harness.run()
        evidence["duration_seconds"] = round(time.monotonic() - started, 3)
        (RESULTS / "http_fuzz.results.json").write_text(
            json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
        print(json.dumps({
            "passed": evidence["passed"],
            "requests": evidence["requests_executed"],
            "features": evidence["coverage"]["unique_features"],
            "edges": evidence["coverage"]["unique_semantic_edges"],
            "5xx": evidence["http"]["5xx"],
            "failures": len(evidence["http"]["failures"]),
            "evidence": "security-analysis/results/http_fuzz.results.json",
        }, ensure_ascii=False, indent=2))
        if not evidence["passed"]:
            raise SystemExit(1)
    finally:
        api.cleanup()
        postgres.stop()
        if postgres.root.exists():
            shutil.rmtree(postgres.root, ignore_errors=True)


if __name__ == "__main__":
    main()
