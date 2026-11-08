"""Run the EditMode suite through the Unity bridge and print a one-line verdict plus failures.

Usage (Editor open, every scene saved, not in Play mode):
    python tools/ui_capture/run_editmode_tests.py [filter]
Optional filter = a substring of the test full name (class, namespace or method), sent to the bridge as an RQL
`query` (the bridge's tests-run has no `testFilter` parameter; its own params are testClass/testNamespace/testMethod/query).
Exit code 0 when green.
For the FULL suite use run_suite_api.py instead: the bridge tests-run times out at 5 minutes on ~3000 tests.
"""
import sys, os, json
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge

args = {"testMode": "EditMode"}
if len(sys.argv) > 1:
    flt = sys.argv[1].replace('"', "")
    args["query"] = 'tests | where fullName contains "%s" and mode == "EditMode"' % flt
else:
    print("hint: this runs through the bridge tests-run, which times out at 5 min on the full suite (~3000 tests); "
          "use tools/ui_capture/run_suite_api.py for a full run.")
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
summary = ((sc or {}).get("result") or {}).get("Summary") or {}
ok = summary.get("Status") == "Passed" and summary.get("FailedTests") == 0 and (summary.get("TotalTests") or 0) > 0
print("SUMMARY:", json.dumps(summary))
print("VERDICT:", "GREEN" if ok else "CHECK OUTPUT")
sys.exit(0 if ok else 1)
