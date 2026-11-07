"""Run the EditMode suite through the Unity bridge and print a one-line verdict plus failures.

Usage (Editor open, every scene saved, not in Play mode):
    python tools/ui_capture/run_editmode_tests.py [filter]
Optional filter = a test class or namespace name (passed as testFilter). Exit code 0 when green.
"""
import sys, os, json
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge

args = {"testMode": "EditMode"}
if len(sys.argv) > 1:
    args["testFilter"] = sys.argv[1]
try:
    bridge.init()
    m = bridge.call("tests-run", args, timeout=1800)
except Exception as e:  # bridge down (Editor closed?) is a hard failure, not a green run
    print("BRIDGE ERROR:", e)
    print("VERDICT: BRIDGE DOWN")
    sys.exit(2)
res = (m or {}).get("result", {})
text = " ".join(c.get("text", "") for c in res.get("content", []) if c.get("type") == "text")
sc = res.get("structuredContent")
if sc:
    s = json.dumps(sc)
    print(s[:4000])
print(text[:6000])
ok = ("failed: 0" in (text + json.dumps(sc or {})).lower()) or ("\"failed\": 0" in json.dumps(sc or {}).lower())
print("VERDICT:", "GREEN" if ok else "CHECK OUTPUT")
sys.exit(0 if ok else 1)
