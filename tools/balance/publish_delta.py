#!/usr/bin/env python
"""Publish, fetch or roll back the balance delta in UGS Remote Config key `balance` (with an audit trail).

Usage (run with `python -I`):
  publish_delta.py publish [delta.json|-] [--env dev|production] [--note TEXT] [--apply] [--confirm-production]
  publish_delta.py fetch --env dev|production
  publish_delta.py rollback tools/balance/history/<file>.json [--env ...] [--apply] [--confirm-production]

`publish` and `rollback` are DRY RUNS unless --apply is given: they print the target environment, the delta, the expected
`bal=` hash and a diff against what is published now. --confirm-production is a human flag (production needs it with
--apply); an agent never passes it.

Before anything is sent the delta is fed through make_delta.py (run as a subprocess, so the checks are the same code); it is
refused when make_delta reports PROBLEMS or when its output differs from the input (a value equal to shipped, an unknown
field, a wrong type...). A delta that equals the shipped assets has nothing to publish and is refused too.

After --apply: the key is read back and compared with what was sent (canonical JSON, sorted keys, no whitespace); a history
entry tools/balance/history/<UTC yyyyMMddTHHmmssZ>-<env>.json is written (delta, env, note, bal hash, git short SHA, who ran
it) and latest-<env>.json is overwritten. Then confirm `bal=<hash>` in the Session log line or the F1 HUD of a build.

API (verified 2026-11-17 against https://docs.unity.com/en-us/oas-remote-config-admin/1.0.0.md, Remote Config Admin API v1):
  base      https://services.api.unity.com/remote-config/v1
  auth      HTTP Basic, base64("<service account key id>:<secret>"); the service account needs a role with scopes
            remote_config.configs.list (list environments), remote_config.configs.get (read) and
            remote_config.configs.update (write)
  GET  /projects/{projectId}/environments                          -> {"environments":[{"id","name"}]}
  GET  /projects/{projectId}/environments/{environmentId}/configs  -> {"configs":[{"id","environmentId","type":"settings",
                                                                       "value":[{"key","type","value"}]}]}
  PUT  /projects/{projectId}/configs/{configId}  body {"type":"settings","value":[<ALL settings>]}  ("Updates the value of
       an existing Config"; the array REPLACES every setting of the environment, so this tool reads the whole array and
       changes only the `balance` entry)
  setting   {"key":"balance","type":"json","value":<delta object>}  (json type: an object, limited to 10000 characters)
Not verified live (no credentials were available when this was written): the PUT response code, that a PUT is effective
without a separate publish step, and the form of the json value in the GET response (both an object and a JSON string are
accepted here).

Secrets come ONLY from environment variables and are never printed, logged or stored:
  UGS_SERVICE_KEY_ID, UGS_SERVICE_SECRET, UGS_PROJECT_ID, and optionally UGS_ENV_DEV_ID / UGS_ENV_PRODUCTION_ID
  (without them the environment id is looked up by name through the environments endpoint).
"""
import argparse
import base64
import getpass
import json
import os
import platform
import subprocess
import sys
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
HISTORY_DIR = HERE / "history"
API = "https://services.api.unity.com/remote-config/v1"
KEY = "balance"
ENVS = ("dev", "production")
BASELINE_HASH = "5465b825"   # hash of the canonical empty delta {}, BalanceBook.BaselineHash


class PublishError(Exception):
    pass


# ---- hash (mirror of BalanceBook.Canonicalize / HashOf) -----------------------------------------------------------------

def _float32_token(v):
    import struct
    f = struct.unpack("<f", struct.pack("<f", float(v)))[0]
    for n in range(1, 10):
        s = f"{f:.{n}g}"
        if struct.unpack("<f", struct.pack("<f", float(s)))[0] == f:
            break
    if "e" in s:
        s = format(float(s), "f").rstrip("0").rstrip(".") if abs(float(s)) >= 1e-5 else s
    return s[:-2] if s.endswith(".0") else s


def canonical(delta):
    """BalanceBook's canonical JSON: sorted keys, no whitespace, note left out. Returns None when it cannot be computed
    offline (an enum given by member name)."""
    def token(x):
        if isinstance(x, bool):
            return "true" if x else "false"
        if isinstance(x, int):
            return str(x)
        if isinstance(x, float):
            return str(int(x)) if x == int(x) and abs(x) < 1e15 else _float32_token(x)
        raise ValueError(x)
    parts = []
    assets = delta.get("assets") or {}
    if assets:
        body = []
        for name in sorted(assets, key=lambda s: s.encode("utf-8")):
            fields = []
            for f in sorted(assets[name], key=lambda s: s.encode("utf-8")):
                v = assets[name][f]
                if isinstance(v, str):
                    return None
                fields.append(json.dumps(f) + ":" + token(v))
            body.append(json.dumps(name) + ":{" + ",".join(fields) + "}")
        parts.append('"assets":{' + ",".join(body) + "}")
    kits = delta.get("kits") or {}
    if kits:
        body = [json.dumps(n) + ":[" + ",".join(json.dumps(x) for x in kits[n]) + "]" for n in sorted(kits)]
        parts.append('"kits":{' + ",".join(body) + "}")
    parts.append('"schema":' + str(int(delta.get("schema", 1))))
    return "{" + ",".join(parts) + "}"


def fnv1a32(text):
    h = 2166136261
    for b in text.encode("utf-8"):
        h = ((h ^ b) * 16777619) & 0xFFFFFFFF
    return f"{h:08x}"


def expected_hash(delta):
    c = canonical(delta)
    return fnv1a32(c) if c is not None else None


# ---- validation through make_delta.py -----------------------------------------------------------------------------------

def validate_with_make_delta(delta):
    """Run make_delta.py on the delta; return (ok, message). ok only when it prints exactly the same delta."""
    p = subprocess.run([sys.executable, "-I", str(HERE / "make_delta.py"), "-"], input=json.dumps(delta),
                       capture_output=True, text=True, cwd=str(ROOT))
    if p.returncode != 0:
        return False, p.stderr.strip() or "make_delta.py failed"
    if not p.stdout.strip():
        return False, "no difference from the shipped assets: nothing to publish"
    try:
        out = json.loads(p.stdout)
    except ValueError:
        return False, "make_delta.py printed something that is not JSON"
    if out != delta:
        return False, ("make_delta.py would publish a different delta (a value equal to shipped, an unknown key...):\n"
                       + json.dumps(out, indent=2))
    return True, ""


# ---- HTTP layer (injectable) -------------------------------------------------------------------------------------------

class UrllibHttp:
    def request(self, method, url, headers, body=None):
        req = urllib.request.Request(url, data=body, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                return r.status, r.read()
        except urllib.error.HTTPError as e:
            return e.code, e.read()


class RemoteConfig:
    def __init__(self, http, env, environ):
        missing = [k for k in ("UGS_SERVICE_KEY_ID", "UGS_SERVICE_SECRET", "UGS_PROJECT_ID") if not environ.get(k)]
        if missing:
            raise PublishError("missing environment variable(s): " + ", ".join(missing) + " (see the one-time setup in the "
                               "balance skill; never paste them into chat)")
        token = base64.b64encode(f"{environ['UGS_SERVICE_KEY_ID']}:{environ['UGS_SERVICE_SECRET']}".encode()).decode()
        self._headers = {"Authorization": "Basic " + token, "Content-Type": "application/json", "Accept": "application/json"}
        self.http, self.env, self.project = http, env, environ["UGS_PROJECT_ID"]
        self.env_id_override = environ.get("UGS_ENV_" + env.upper() + "_ID")
        self._config = None

    def _call(self, method, path, payload=None):
        body = json.dumps(payload).encode() if payload is not None else None
        status, raw = self.http.request(method, API + path, self._headers, body)
        if status >= 300:
            snippet = raw[:300].decode("utf-8", "replace") if raw else ""
            raise PublishError(f"{method} {path} -> HTTP {status} {snippet}")
        return json.loads(raw) if raw else {}

    def env_id(self):
        if self.env_id_override:
            return self.env_id_override
        for e in self._call("GET", f"/projects/{self.project}/environments").get("environments", []):
            if e.get("name") == self.env:
                return e["id"]
        raise PublishError(f"no Remote Config environment named '{self.env}' in the project (create it in the Dashboard "
                           f"or set UGS_ENV_{self.env.upper()}_ID)")

    def config(self):
        cfgs = self._call("GET", f"/projects/{self.project}/environments/{self.env_id()}/configs").get("configs", [])
        for c in cfgs:
            if c.get("type") == "settings":
                return c
        raise PublishError(f"environment '{self.env}' has no settings config yet: create the JSON key '{KEY}' in the Dashboard first")

    def fetch(self):
        """The published delta (dict), or None when the key does not exist."""
        for s in self.config().get("value", []):
            if s.get("key") == KEY:
                v = s.get("value")
                return json.loads(v) if isinstance(v, str) else v
        return None

    def put(self, delta):
        cfg = self.config()
        settings = [s for s in cfg.get("value", []) if s.get("key") != KEY]
        settings.append({"key": KEY, "type": "json", "value": delta})
        self._call("PUT", f"/projects/{self.project}/configs/{cfg['id']}", {"type": "settings", "value": settings})


# ---- helpers ------------------------------------------------------------------------------------------------------------

def flatten(delta):
    out = {}
    if not delta:
        return out
    for a, fields in (delta.get("assets") or {}).items():
        for f, v in fields.items():
            out[f"{a}.{f}"] = v
    for k, kit in (delta.get("kits") or {}).items():
        out[f"kits.{k}"] = kit
    return out


def diff_lines(current, new):
    a, b = flatten(current), flatten(new)
    lines = []
    for k in sorted(set(a) | set(b)):
        if k not in a:
            lines.append(f"  + {k} = {json.dumps(b[k])}")
        elif k not in b:
            lines.append(f"  - {k} (was {json.dumps(a[k])}, back to shipped)")
        elif a[k] != b[k]:
            lines.append(f"  ~ {k}: {json.dumps(a[k])} -> {json.dumps(b[k])}")
    return lines or ["  (no change in values)"]


def git_short_sha():
    try:
        return subprocess.run(["git", "rev-parse", "--short", "HEAD"], capture_output=True, text=True, cwd=str(ROOT),
                              timeout=10).stdout.strip() or "unknown"
    except Exception:
        return "unknown"


def write_history(history_dir, entry):
    history_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    text = json.dumps(entry, indent=2) + "\n"
    path = history_dir / f"{stamp}-{entry['env']}.json"
    path.write_text(text, encoding="utf-8")
    (history_dir / f"latest-{entry['env']}.json").write_text(text, encoding="utf-8")
    return path


def _canon_dump(x):
    return json.dumps(x, sort_keys=True, separators=(",", ":"))


def _load_delta(src):
    text = sys.stdin.read() if src in (None, "-") else Path(src).read_text(encoding="utf-8")
    try:
        d = json.loads(text)
    except ValueError as e:
        raise PublishError(f"the delta is not valid JSON: {e}")
    if not isinstance(d, dict):
        raise PublishError("the delta must be a JSON object")
    return d


# ---- commands -----------------------------------------------------------------------------------------------------------

def do_publish(args, delta, action, rollback_of, deps):
    out, http, environ = deps["out"], deps["http"], deps["environ"]
    if args.env == "production" and args.apply and not args.confirm_production:
        raise PublishError("production needs --confirm-production (a human flag, never passed by an agent)")
    if args.note:
        delta = dict(delta, note=args.note)
    ok, msg = deps["validate"](delta)
    if not ok:
        raise PublishError("the delta did not pass make_delta.py:\n" + msg)
    h = expected_hash(delta)
    out(f"Target environment: {args.env}" + ("  (PRODUCTION)" if args.env == "production" else ""))
    out("Delta:\n" + json.dumps(delta, indent=2))
    out(f"Expected bal= {h if h else 'unknown offline (an enum is given by name); read it from the Session log line'}")
    rc = RemoteConfig(http, args.env, environ)
    current = rc.fetch()
    out(f"Currently published ({args.env}): " + ("nothing (key missing)" if current is None else
                                                  f"bal={expected_hash(current) or '?'}"))
    out("Diff vs published:\n" + "\n".join(diff_lines(current, delta)))
    if not args.apply:
        out("DRY RUN: nothing was published. Re-run with --apply to publish.")
        return 0
    rc.put(delta)
    back = rc.fetch()
    verified = back is not None and _canon_dump(back) == _canon_dump(delta)
    entry = {"env": args.env, "action": action, "delta": delta, "note": delta.get("note", ""), "bal": h,
             "git": git_short_sha(), "by": f"{getpass.getuser()}@{platform.node()} via publish_delta.py",
             "published_at_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"), "verified": verified}
    if rollback_of:
        entry["rollback_of"] = str(rollback_of)
    path = write_history(deps["history_dir"], entry)
    out(f"History: {path}")
    if not verified:
        raise PublishError("published, but the value read back differs from what was sent: check the Dashboard")
    out(f"Published and read back identically. Now confirm bal={h or '<see log>'} in the Session log line or the F1 HUD.")
    return 0


def main(argv=None, http=None, environ=None, validate=None, history_dir=None, out=print):
    ap = argparse.ArgumentParser(prog="publish_delta.py")
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("publish", "rollback"):
        p = sub.add_parser(name)
        p.add_argument("source", nargs="?", default="-", help="delta file or '-' (publish); history file (rollback)")
        p.add_argument("--env", choices=ENVS)
        p.add_argument("--note")
        p.add_argument("--apply", action="store_true")
        p.add_argument("--confirm-production", action="store_true")
    f = sub.add_parser("fetch")
    f.add_argument("--env", choices=ENVS, required=True)
    args = ap.parse_args(argv)
    deps = {"http": http or UrllibHttp(), "environ": os.environ if environ is None else environ,
            "validate": validate or validate_with_make_delta, "history_dir": history_dir or HISTORY_DIR, "out": out}
    try:
        if args.cmd == "fetch":
            cur = RemoteConfig(deps["http"], args.env, deps["environ"]).fetch()
            if cur is None:
                out(f"nothing published in {args.env} (no '{KEY}' key): the shipped numbers play, bal={BASELINE_HASH}")
            else:
                out(json.dumps(cur, indent=2))
                out(f"bal={expected_hash(cur) or 'unknown offline'}")
            return 0
        if args.cmd == "rollback":
            entry = json.loads(Path(args.source).read_text(encoding="utf-8"))
            args.env = args.env or entry.get("env") or "dev"
            return do_publish(args, entry["delta"], "rollback", args.source, deps)
        args.env = args.env or "dev"
        return do_publish(args, _load_delta(args.source), "publish", None, deps)
    except PublishError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
