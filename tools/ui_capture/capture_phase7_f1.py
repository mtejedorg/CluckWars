"""Phase 7 chunk F1 live captures: the GET READY digit at its WORST-CASE slam scale (the YOU pill must stay clear) and the touch hold hint
beside the cancel zone.

    python tools/ui_capture/capture_phase7_f1.py <output_dir>
"""
import sys, os, re, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(HERE, "out")
os.makedirs(OUT, exist_ok=True)

_src = open(os.path.join(HERE, "capture_phase7_chunk_b.py"), encoding="utf-8").read()
_cs = re.search(r'CS = r"""(.*?)\n\}"""', _src, re.S).group(1)
CS = _cs + r'''
  static UIDocument OverlayDoc() { return Doc("IntroNumber"); }
  public static string Digit() { var d = OverlayDoc(); if (d == null) return ""; var l = d.rootVisualElement.Q<Label>("IntroNumber"); return l == null ? "" : l.text; }
  // Pins the digit at the peak slam scale and reports its bounds against the YOU marker row.
  public static string Peak() { var d = OverlayDoc(); var l = d.rootVisualElement.Q<Label>("IntroNumber"); float s = CluckWars.UI.MenuJuicePolicy.CountdownPeakScale; l.style.scale = new Scale(new Vector3(s, s, 1f));
    var r = d.rootVisualElement.worldBound; var b = l.worldBound; return "digit '" + l.text + "' scale " + s + " bottom=" + ((b.yMax / r.height) * 100f).ToString("0.0") + "% of panel height (unscaled box); box " + b; }
  public static string Unpin() { var d = OverlayDoc(); var l = d.rootVisualElement.Q<Label>("IntroNumber"); l.style.scale = StyleKeyword.Null; return "unpinned"; }
  public static string Mode(string m) { TouchControlsController.DebugForceMode = m == "touch" ? HudDeviceMode.Touch : m == "pad" ? HudDeviceMode.Gamepad : HudDeviceMode.KeyboardMouse; return "mode " + m; }
  public static string ResetHint() { CluckWars.Settings.PlayerPreferences.HoldHintCount = 0; CluckWars.Settings.PlayerPreferences.QuickMovesEnabled = false; return "hint reset"; }
}'''

SIG = {"Press": ["name"], "Hold": ["slot", "on"], "Rival": ["d"], "Move": ["x", "z"], "Mode": ["m"]}


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "P6", "methodName": method, "parameters": params}, timeout=90)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:400]


def shot(name):
    p = bridge.shot("_f1_" + name)
    if p:
        dest = os.path.join(OUT, name + ".png")
        os.replace(p, dest)
        print("  saved", dest)


def main():
    bridge.init()
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(60):
        if str(cs("Press", "SoloBtn")).startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(0.8)
    print(cs("Press", "NextBtn")); time.sleep(1.0)
    print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5)
    print(cs("Press", "StartBtn"))
    try:
        seen = set()
        for _ in range(400):
            d = str(cs("Digit"))
            if d in ("3", "2", "1", "GO!") and d not in seen:
                info = cs("Peak"); shot("getready_digit_%s_at_peak_scale" % d.replace("!", ""))
                seen.add(d); print("digit", d, info); print(cs("Unpin"))
            if "GO!" in seen or (not d and len(seen) >= 3):
                break
        for _ in range(60):
            if str(cs("State")) == "Active":
                break
            time.sleep(0.5)
        time.sleep(2.0)
        print(cs("Move", "-6", "-6")); time.sleep(1.0)
        gated = str(cs("GatedSlot")); slot = gated if gated != "-1" else "0"
        for mode in ("touch", "desktop"):
            print(cs("Mode", mode)); print(cs("ResetHint")); time.sleep(1.0)
            print(cs("Hold", slot, "1")); time.sleep(1.5)
            shot("hold_on_calm_grass" if mode == "touch" else "hold_hint_desktop_f1")
            print(cs("Hold", slot, "0")); time.sleep(0.8)
    finally:
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


main()
