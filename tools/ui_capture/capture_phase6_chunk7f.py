"""Phase 6 chunk 7f captures: see-through fence near the local bird + the GET READY YOU ring.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_phase6_chunk7f.py <output_dir>

Solo START. During the GET READY countdown ("3") the bird stands at its spawn, behind the near fence corner:
fence_fade__getready.png (effect on), fence_fade__getready_before.png (FenceFadeDriver.EffectEnabled = false).
In play the bird is teleported just inside the middle of a near-side fence run:
fence_fade__play.png / fence_fade__play_before.png. Then against each base coop: fence_fade__corner{0..3}.png.
Leaves Play mode at the end.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class P7 {
  static UIDocument Doc(string el) { return Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None).FirstOrDefault(x => x.rootVisualElement != null && x.rootVisualElement.Q(el) != null); }
  public static string Press(string name) { var d = Doc(name); if (d == null) return "NOBTN " + name; var b = d.rootVisualElement.Q<Button>(name);
    var c = typeof(Button).GetField("m_Clickable", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(b);
    ((System.Action)typeof(Clickable).GetField("clicked", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(c)).Invoke(); return "clicked " + name; }
  public static string Fill() { var r = Doc("ReadyBtn").rootVisualElement; int taps = 0;
    for (int i = 0; i < 14; i++) { var rb = r.Q<Button>("ReadyBtn"); if (rb != null && rb.enabledSelf) break;
      var card = r.Query<VisualElement>(className: "cw-ability-card").ToList().FirstOrDefault(c => !c.ClassListContains("cw-ability-card--picked")); if (card == null) break;
      using (var e = ClickEvent.GetPooled()) { e.target = card; card.SendEvent(e); } taps++; }
    return "taps=" + taps; }
  public static string State() { var gm = GameManager.Instance; return gm == null ? "nogm" : gm.State.ToString() + (gm.IsIntroActive ? "+intro" : ""); }
  static ChickenController Local() { return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k.HasInputAuthority); }
  public static string Effect(string on) { FenceFadeDriver.EffectEnabled = on == "1"; return "effect " + on; }
  public static string Info() { var l = Local(); var d = Object.FindFirstObjectByType<FenceFadeDriver>();
    return "local=" + (l == null ? "-" : l.transform.position.ToString()) + " strength=" + (d == null ? "no driver" : d.Strength.ToString("0.00"))
      + " global=" + Shader.GetGlobalVector("_CW_LocalBirdPos") + " params=" + Shader.GetGlobalVector("_CW_FenceFadeParams") + " state=" + State(); }
  // Teleports the local bird against base coop N (0..3), on the coop's arena-side corner, like the spawn.
  public static string Coop(string n) { var l = Local(); if (l == null) return "no local";
    var coop = GameObject.Find("Base" + n + "_Coop"); if (coop == null) return "no coop " + n;
    var r = coop.GetComponentInChildren<Renderer>(); var b = r.bounds; var p = b.ClosestPoint(Vector3.zero); p.y = l.transform.position.y;
    var pos = p + (-p).normalized * 0.25f; pos.y = l.transform.position.y;
    var nt = l.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else l.transform.position = pos;
    return "local -> coop " + n + " " + pos; }
  // Teleports the local bird just inside the middle of the fence run on the camera's near side (along X).
  public static string NearSide() { var l = Local(); if (l == null) return "no local"; var cam = MatchCamera.Instance.Camera.transform;
    var toCam = -cam.forward; toCam.y = 0f; float sx = Mathf.Sign(toCam.x);
    var post = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Wall_E_0"); float line = post != null ? Mathf.Abs(post.position.x) : 25f;
    var pos = new Vector3(sx * (line - 0.75f), l.transform.position.y, 0f); var nt = l.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else l.transform.position = pos;
    return "local -> " + pos + " (fence line " + line + ")"; }
}"""

SIG = {"Press": ["name"], "Effect": ["on"], "Coop": ["n"]}


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "P7", "methodName": method, "parameters": params}, timeout=90)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:400]


def wait(prefixes, tries=120, dt=0.25):
    s = ""
    for _ in range(tries):
        s = str(cs("State"))
        if s.startswith(prefixes):
            return s
        time.sleep(dt)
    raise SystemExit("timed out waiting for " + str(prefixes) + " (last: " + s + ")")


def shot(name):
    p = bridge.shot("_p7_" + name)
    if p:
        dest = os.path.join(OUT, name + ".png")
        os.replace(p, dest)
        print("  saved", dest)


def main():
    bridge.init()
    bridge.call("editor-application-set-state", {"isPlaying": True})
    try:
        for _ in range(60):
            if str(cs("Press", "SoloBtn")).startswith("clicked"):
                break
            time.sleep(1)
        time.sleep(0.8)
        print(cs("Press", "NextBtn")); time.sleep(1.0)
        print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5)
        print(cs("Press", "StartBtn"))
        # GET READY itself sits under the intro scrim; the countdown ("3") is where the bird must be findable.
        wait(("Active+intro",), tries=200, dt=0.1)
        time.sleep(0.3)
        print("  getready:", cs("Info"))
        shot("fence_fade__getready")
        print(cs("Effect", "0")); time.sleep(0.4)
        shot("fence_fade__getready_before")
        print(cs("Effect", "1"))

        wait(("Active",)); s = wait(("Active",))
        while str(cs("State")) != "Active":
            time.sleep(0.3)
        time.sleep(1.0)
        print("  play at spawn:", cs("Info"))
        shot("fence_fade__play_spawn")
        print(cs("NearSide")); time.sleep(1.2)
        print("  play near side:", cs("Info"))
        shot("fence_fade__play")
        print(cs("Effect", "0")); time.sleep(0.4)
        shot("fence_fade__play_before")
        print(cs("Effect", "1"))
        # Every base corner: the bird against its coop, effect on.
        for n in ("0", "1", "2", "3"):
            print(cs("Coop", n)); time.sleep(1.0)
            print("  coop", n, ":", cs("Info"))
            shot("fence_fade__corner" + n)
    finally:
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


main()
