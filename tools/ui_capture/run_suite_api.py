"""Run the EditMode suite via Unity's TestRunnerApi and poll a result file.

Use this for the FULL suite: the bridge `tests-run` tool times out after 5 minutes on ~3000 tests
(see run_editmode_tests.py, which is fine for a filtered run).

Usage (Editor open, every scene saved, bridge up):
    python tools/ui_capture/run_suite_api.py [groupRegex]
Optional groupRegex is passed to Filter.groupNames (a regex matched against the full test name,
e.g. "CluckWars.Tests.Feedback" or "ChickenVFXTests"). Without it the whole EditMode suite runs.
Prints "PASSED n FAILED n ..." plus one line per failing test, then VERDICT: GREEN | RED | TIMEOUT.
Exit code 0 only on GREEN. The result file lives in tools/ui_capture/.cache/ (gitignored).
"""
import sys, os, time
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge

HERE = os.path.dirname(os.path.abspath(__file__))
CACHE = os.path.join(HERE, ".cache")
os.makedirs(CACHE, exist_ok=True)
OUT = os.path.join(CACHE, "suite_result.txt")
GROUP = sys.argv[1] if len(sys.argv) > 1 else ""
TIMEOUT_S = 1200

bridge.init()
if os.path.exists(OUT):
    os.remove(OUT)
code = r'''
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
public static class CwSuiteRun {
  class Cb : ICallbacks {
    public string path;
    public void RunStarted(ITestAdaptor t) {}
    public void RunFinished(ITestResultAdaptor r) {
      var sb = new System.Text.StringBuilder();
      sb.AppendLine("PASSED " + r.PassCount + " FAILED " + r.FailCount + " SKIPPED " + r.SkipCount + " INCONCLUSIVE " + r.InconclusiveCount);
      Collect(r, sb);
      System.IO.File.WriteAllText(path, sb.ToString());
    }
    void Collect(ITestResultAdaptor r, System.Text.StringBuilder sb) {
      if (!r.HasChildren) { if (r.TestStatus == TestStatus.Failed) sb.AppendLine("FAIL " + r.FullName + " :: " + (r.Message ?? "").Replace("\n"," ")); return; }
      foreach (var c in r.Children) Collect(c, sb);
    }
    public void TestStarted(ITestAdaptor t) {}
    public void TestFinished(ITestResultAdaptor r) {}
  }
  public static string Run(string path, string group) {
    if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    var api = ScriptableObject.CreateInstance<TestRunnerApi>();
    api.RegisterCallbacks(new Cb { path = path });
    var f = new Filter { testMode = TestMode.EditMode };
    if (!string.IsNullOrEmpty(group)) f.groupNames = new[] { group };
    api.Execute(new ExecutionSettings(f));
    return "started";
  }
}
'''
print(bridge.call("script-execute", {"csharpCode": code, "className": "CwSuiteRun", "methodName": "Run",
                                     "parameters": [{"name": "path", "typeName": "System.String", "value": OUT.replace("\\", "/")},
                                                    {"name": "group", "typeName": "System.String", "value": GROUP}]}))
t0 = time.time()
while time.time() - t0 < TIMEOUT_S:
    if os.path.exists(OUT):
        time.sleep(1)
        with open(OUT, encoding="utf-8") as f:
            txt = f.read()
        print(txt[:4000])
        green = " FAILED 0 " in txt.splitlines()[0] and not txt.startswith("PASSED 0 ")
        print("VERDICT: GREEN" if green else "VERDICT: RED")
        sys.exit(0 if green else 1)
    time.sleep(5)
print("VERDICT: TIMEOUT")
sys.exit(2)
