"""Shared test fixtures: a temporary copy of Assets/_Game/Data that schema.DATA is pointed at, and a fake Remote Config."""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

BAL = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BAL))
import schema  # noqa: E402

REAL_DATA = schema.DATA


def OK(delta):
    return True, ""


class DataCopyCase(unittest.TestCase):
    """setUp copies the shipped Data folder to a temp dir (read from the repo, never written there) and points schema.DATA at it."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="balance-test-"))
        self.data = self.tmp / "Data"
        shutil.copytree(REAL_DATA, self.data)
        schema.DATA = self.data
        self.addCleanup(self._cleanup)

    def _cleanup(self):
        schema.DATA = REAL_DATA
        shutil.rmtree(self.tmp, ignore_errors=True)

    def git_init(self):
        def g(*a):
            subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", *a],
                           cwd=str(self.tmp), check=True, capture_output=True)
        g("init", "-q")
        g("add", "-A")
        g("commit", "-q", "-m", "fixture")


class FakeHttp:
    """In-memory Remote Config: one project, environments dev/production, one settings config each."""

    def __init__(self, other_settings=None, balance=None):
        self.calls = []
        self.configs = {}
        for name in ("dev", "production"):
            value = list(other_settings or [])
            if balance is not None:
                value.append({"key": "balance", "type": "json", "value": balance})
            self.configs[name] = {"id": "cfg-" + name, "environmentId": "env-" + name, "type": "settings", "value": value}

    def request(self, method, url, headers, body=None):
        self.calls.append((method, url, headers, body))
        path = url.split("/remote-config/v1", 1)[1]
        if method == "GET" and path.endswith("/environments"):
            return 200, json.dumps({"environments": [{"id": "env-" + n, "name": n} for n in self.configs]}).encode()
        if method == "GET" and path.endswith("/configs"):
            eid = path.split("/environments/")[1].split("/")[0]
            cfg = [c for c in self.configs.values() if c["environmentId"] == eid]
            return 200, json.dumps({"configs": cfg}).encode()
        if method == "PUT" and "/configs/" in path:
            cid = path.rsplit("/", 1)[1]
            for c in self.configs.values():
                if c["id"] == cid:
                    c["value"] = json.loads(body)["value"]
                    return 200, b""
        return 404, b"not found"

    def puts(self):
        return [c for c in self.calls if c[0] == "PUT"]


ENVIRON = {"UGS_SERVICE_KEY_ID": "kid", "UGS_SERVICE_SECRET": "sekret", "UGS_PROJECT_ID": "proj"}
DELTA = {"schema": 1, "note": "t", "assets": {"Snatch": {"Cooldown": 3.5}}}
