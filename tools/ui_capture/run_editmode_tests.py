"""Run the EditMode suite through the Unity bridge, print a summary + failures, then reopen Bootstrap.
usage: python tools/ui_capture/run_editmode_tests.py [filter-substring-for-class-or-method]"""
import sys, json, time, re
sys.path.insert(0, "C:/Users/MARCO/Documents/GitHub/CluckWars/tools/ui_capture")
sys.stdout.reconfigure(encoding="utf-8")
import bridge
bridge.init()
args = {"testMode": "EditMode"}
if len(sys.argv) > 1:
    args["testClass"] = sys.argv[1]
m = bridge.call("tests-run", args, timeout=1200)
res = (m or {}).get("result", {})
sc = res.get("structuredContent")
txt = " ".join(c.get("text", "") for c in res.get("content", []) if c.get("type") == "text")
data = sc if sc else None
if data is None:
    try: data = json.loads(txt)
    except Exception: data = None
if data is None:
    print(txt[:4000] or json.dumps(m)[:4000])
else:
    s = json.dumps(data)
    summ = data.get("result", data).get("Summary") if isinstance(data.get("result", data), dict) else None
    print("SUMMARY", json.dumps(summ) if summ else s[:1500])
    fails = re.findall(r'\{[^{}]*"(?:Status|status)"\s*:\s*"(?:Failed|Error)"[^{}]*\}', s)
    for f in fails[:40]: print("FAIL", f[:700])
CS = 'using UnityEditor.SceneManagement; public class R { public static string Go() { EditorSceneManager.OpenScene("Assets/_Game/Scenes/Bootstrap.unity", OpenSceneMode.Single); return "ok"; } }'
for attempt in range(6):
    time.sleep(4)
    print(bridge.text(bridge.call("script-execute", {"csharpCode": CS, "className": "R", "methodName": "Go", "parameters": []}, timeout=120))[:200])
    time.sleep(3)
    if "Bootstrap.unity" in bridge.text(bridge.call("scene-list-opened", {}, timeout=30)):
        print("reopened Bootstrap (verified)"); break
else:
    print("WARNING: Bootstrap NOT open after tests")
