"""Drive the actual shop in Chrome via CDP using only Python's standard library."""
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import socket
import struct
import subprocess
import sys
import tempfile
import time
import urllib.request
from urllib.parse import urlsplit
from datetime import datetime, timezone

BASE = Path(__file__).resolve().parent


class CDP:
    def __init__(self, url):
        target = urlsplit(url)
        self.socket = socket.create_connection((target.hostname, target.port), timeout=15)
        key = base64.b64encode(os.urandom(16)).decode()
        self.socket.sendall((f'GET {target.path} HTTP/1.1\r\nHost: {target.netloc}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n').encode())
        response = bytearray()
        while not response.endswith(b'\r\n\r\n'):
            response.extend(self.socket.recv(1))
        assert b' 101 ' in response, 'CDP websocket handshake failed'
        expected = base64.b64encode(hashlib.sha1((key+'258EAFA5-E914-47DA-95CA-C5AB0DC85B11').encode()).digest())
        assert expected.lower() in response.lower(), 'Invalid websocket handshake'
        self.sequence = 0
        self.errors = []

    def receive(self, count):
        data = bytearray()
        while len(data) < count:
            chunk = self.socket.recv(count-len(data))
            if not chunk:
                raise RuntimeError('CDP connection closed')
            data.extend(chunk)
        return bytes(data)

    def frame(self):
        head = self.receive(2)
        length = head[1] & 127
        if length == 126:
            length = struct.unpack('!H', self.receive(2))[0]
        elif length == 127:
            length = struct.unpack('!Q', self.receive(8))[0]
        mask = self.receive(4) if head[1] & 128 else None
        data = self.receive(length)
        if mask:
            data = bytes(v ^ mask[i % 4] for i,v in enumerate(data))
        if head[0] & 15 == 8:
            raise RuntimeError('CDP websocket closed')
        return json.loads(data)

    def call(self, method, params=None):
        self.sequence += 1
        identifier = self.sequence
        data = json.dumps({'id': identifier, 'method': method, 'params': params or {}}).encode()
        length = len(data)
        head = bytes([0x81, 0x80 | length]) if length < 126 else bytes([0x81, 0x80 | 126])+struct.pack('!H', length) if length < 65536 else bytes([0x81, 0x80 | 127])+struct.pack('!Q', length)
        mask = os.urandom(4)
        self.socket.sendall(head+mask+bytes(v ^ mask[i % 4] for i,v in enumerate(data)))
        while True:
            result = self.frame()
            if result.get('method') == 'Runtime.exceptionThrown':
                details = result['params']['exceptionDetails']
                self.errors.append(details.get('exception', {}).get('description', details.get('text','JavaScript error')).split('\n')[0])
            if result.get('id') == identifier:
                if 'error' in result:
                    raise RuntimeError('CDP command failed: '+method)
                return result.get('result', {})

    def evaluate(self, expression):
        result = self.call('Runtime.evaluate', {'expression': expression, 'awaitPromise': True, 'returnByValue': True})
        if 'exceptionDetails' in result:
            details = result['exceptionDetails']
            raise RuntimeError(details.get('exception', {}).get('description', details.get('text','Scenario failed')).split('\n')[0])
        return result['result'].get('value')


def main():
    address = sys.argv[1]
    assert urlsplit(address).hostname in ('localhost', '127.0.0.1'), 'Smoke test must use a local temporary API'
    chrome = shutil.which('google-chrome') or shutil.which('chromium')
    if not chrome:
        raise SystemExit('Chrome/Chromium required')
    result = {'passed': False, 'run_at_utc': datetime.now(timezone.utc).isoformat(),
              'application': 'Normal OrderPaymentMicroservice executable in Development with temporary PostgreSQL/ledger',
              'limitations': ['One Chrome desktop flow over localhost; no real card/provider or production deployment.', 'External font resources are blocked; no mobile/accessibility or full cross-browser coverage.']}
    with tempfile.TemporaryDirectory(prefix='live-shop-browser-') as directory:
        browser_log = (Path(directory)/'chrome-startup.log').open('w+')
        process = subprocess.Popen([chrome, '--headless', '--no-sandbox', '--disable-gpu', '--disable-background-networking',
                                    '--remote-debugging-port=0', f'--user-data-dir={directory}', 'about:blank'],
                                   stdout=subprocess.DEVNULL, stderr=browser_log)
        cdp = None
        try:
            port_file = Path(directory)/'DevToolsActivePort'
            deadline = time.monotonic()+15
            while not port_file.exists():
                if process.poll() is not None or time.monotonic()>deadline:
                    browser_log.seek(0)
                    diagnostic = browser_log.read()[-3000:]
                    raise RuntimeError(f'Chrome startup failed (exit={process.poll()}): {diagnostic}')
                time.sleep(.05)
            port = int(port_file.read_text().splitlines()[0])
            with urllib.request.urlopen(f'http://127.0.0.1:{port}/json/list') as response:
                pages = json.load(response)
            cdp = CDP(next(p for p in pages if p['type']=='page')['webSocketDebuggerUrl'])
            cdp.call('Runtime.enable'); cdp.call('Page.enable'); cdp.call('Network.enable')
            cdp.call('Network.setBlockedURLs', {'urls':['https://fonts.googleapis.com/*','https://fonts.gstatic.com/*']})
            init = cdp.call('Page.addScriptToEvaluateOnNewDocument', {'source':"localStorage.setItem('user','{broken cached session');"})
            cdp.call('Page.navigate', {'url': address})
            deadline = time.monotonic()+15
            while cdp.evaluate('document.readyState') != 'complete':
                if time.monotonic()>deadline:
                    raise RuntimeError('Shop did not finish loading')
                time.sleep(.05)
            cdp.call('Page.removeScriptToEvaluateOnNewDocument', {'identifier': init['identifier']})
            result['flow'] = cdp.evaluate((BASE/'live_smoke.js').read_text())
            result['javascript_errors'] = cdp.errors
            assert not cdp.errors, 'Uncaught JavaScript exception during live flow'
            previous_origin = cdp.evaluate('performance.timeOrigin')
            cdp.call('Page.reload')
            reload_deadline=time.monotonic()+10
            while not cdp.evaluate(f"performance.timeOrigin !== {previous_origin} && document.readyState==='complete' && typeof currentUser!=='undefined' && currentUser!==null"):
                if time.monotonic()>reload_deadline:
                    raise RuntimeError('Authenticated session did not reload')
                time.sleep(.05)
            result['session_reload'] = True
            assert not cdp.errors, 'Uncaught JavaScript exception on reload'
            assert cdp.evaluate("document.getElementById('logoutBtn').click(); currentUser===null && document.getElementById('userInfo').style.display==='none' && document.getElementById('authSection').style.display!=='none' && document.getElementById('auth').classList.contains('active')"), 'Logout did not reset navigation'
            result['logout'] = True
            result['passed'] = True
        except Exception as error:
            result['error'] = str(error)
        finally:
            if cdp:
                cdp.socket.close()
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill();process.wait()
            browser_log.close()
    (BASE.parent/'results/application_smoke.results.json').write_text(json.dumps(result,indent=2)+'\n')
    print('Live shop smoke: '+('PASS' if result['passed'] else 'FAIL: '+result.get('error','unknown')))
    if not result['passed']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
