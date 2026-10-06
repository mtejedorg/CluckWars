"""Minimal JSON-RPC client for the ai-game-developer MCP bridge (see .mcp.json for the URL)."""
import json, sys, time, base64, os, urllib.request

URL = "http://localhost:21325/p/2fc6da0d"
OUT = os.path.dirname(os.path.abspath(__file__))
_sid = None
_id = 0


def _post(payload, timeout=120):
    global _sid
    h = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
    if _sid:
        h["Mcp-Session-Id"] = _sid
    req = urllib.request.Request(URL, data=json.dumps(payload).encode(), headers=h, method="POST")
    with urllib.request.urlopen(req, timeout=timeout) as r:
        sid = r.headers.get("Mcp-Session-Id")
        if sid:
            _sid = sid
        body = r.read().decode("utf-8", "replace")
    msgs = []
    for line in body.splitlines():
        if line.startswith("data:"):
            try:
                msgs.append(json.loads(line[5:].strip()))
            except Exception:
                pass
    if not msgs and body.strip().startswith("{"):
        msgs.append(json.loads(body))
    return msgs


def init():
    global _id
    _id += 1
    _post({"jsonrpc": "2.0", "id": _id, "method": "initialize",
           "params": {"protocolVersion": "2025-03-26", "capabilities": {},
                      "clientInfo": {"name": "claude-probe", "version": "1"}}})
    _post({"jsonrpc": "2.0", "method": "notifications/initialized"})


def call(name, args=None, timeout=120):
    global _id
    _id += 1
    msgs = _post({"jsonrpc": "2.0", "id": _id, "method": "tools/call",
                  "params": {"name": name, "arguments": args or {}}}, timeout)
    for m in msgs:
        if m.get("id") == _id:
            return m
    return msgs[-1] if msgs else None


def shot(tag):
    m = call("screenshot-game-view", {})
    res = (m or {}).get("result", {})
    for c in res.get("content", []):
        if c.get("type") == "image":
            p = os.path.join(OUT, f"{tag}.png")
            open(p, "wb").write(base64.b64decode(c["data"]))
            return p
    return None


def text(m):
    res = (m or {}).get("result", {})
    return " ".join(c.get("text", "") for c in res.get("content", []) if c.get("type") == "text")[:400]
