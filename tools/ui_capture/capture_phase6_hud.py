"""Phase 6 chunk 1 live capture: touch-HUD hex states + the instant local aim preview, in a real solo match.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_phase6_hud.py <output_dir> [slot]

Drives a solo match (like capture_phase5_extra.py), then simulates a touch hold by setting the HUD's own per-slot
held flag (TouchControlsController._held, the exact state a real pointer-down sets) and shoots the Game view:
  hud_rest.png          resting hexes (no pip unless a rival happens to be in range)
  hold_no_target.png    holding <slot> with nobody in its shape: held hex scaled + cream rim, others dim, dashed cream preview
  rest_pip.png          a rival teleported into <slot>'s shape, nothing held: the gold in-range pip + the hot guide
  hold_with_target.png  holding <slot> with the rival inside: accent preview
Leaves Play mode at the end.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

ARGS = sys.argv[1:]
OUT = os.path.abspath(ARGS[0]) if ARGS else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
SLOT = ARGS[1] if len(ARGS) > 1 else "0"
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using CluckWars.Gameplay; using CluckWars.Input; using CluckWars.Visuals; using CluckWars.UI; using Fusion;
public class P6 {
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
  public static string Hold(string slot, string on) { var h = TouchControlsController.Instance; if (h == null) return "no hud";
    var arr = (bool[])typeof(TouchControlsController).GetField("_held", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(h); arr[int.Parse(slot)] = on == "1"; return "held[" + slot + "]=" + (on == "1"); }
  // Teleports the nearest rival 1.2 m ahead of the local bird (or far away when d <= 0).
  public static string Rival(string d) { var l = Local(); if (l == null) return "no local"; float m = float.Parse(d, System.Globalization.CultureInfo.InvariantCulture);
    var r = ChickenController.ActiveControllers.Where(k => k != null && k != l && !k.IsDecoy && k.Object != null && k.Object.IsValid).OrderBy(k => Vector3.Distance(k.transform.position, l.transform.position)).FirstOrDefault(); if (r == null) return "no rival";
    var kcc = r.GetComponent<NetworkTransform>(); var pos = m > 0 ? l.transform.position + l.transform.forward * m : l.transform.position + new Vector3(30f, 0f, 30f);
    if (kcc != null) kcc.Teleport(pos); else r.transform.position = pos; return "rival " + r.name + " -> " + pos + " (local at " + l.transform.position + ")"; }
  // Teleports the local bird out of its fenced base to (x, z) so the ground preview is not hidden by the walls.
  public static string Move(string x, string z) { var l = Local(); if (l == null) return "no local"; var ci = System.Globalization.CultureInfo.InvariantCulture;
    var pos = new Vector3(float.Parse(x, ci), l.transform.position.y, float.Parse(z, ci)); var nt = l.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else l.transform.position = pos; return "local -> " + pos; }
  public static string Info() { var l = Local(); if (l == null) return "no local"; var a = l.Abilities; var ov = l.GetComponent<AbilitySlotOverlay>(); var tg = l.GetComponent<AbilityTelegraph>();
    string s = "rivalDist=" + string.Join("/", ChickenController.ActiveControllers.Where(k => k != null && k != l).Select(k => Vector3.Distance(k.transform.position, l.transform.position).ToString("0.0"))) + " aimed=" + (tg != null ? tg.LocalAimedSlot.ToString() : "-") + " charging=" + a.ChargingSlot + " | ";
    for (int i = 0; i < 4; i++) { var ab = a.GetSlot(i); s += i + ":" + (ab == null ? "-" : ab.name + "/" + ab.AimShape) + " ref=" + a.EvaluateRefusal(i) + " hot=" + (ov != null && ov.IsHot(i)) + "; "; }
    var lines = l.GetComponentsInChildren<LineRenderer>(true).Where(x => x.enabled).Select(x => x.name + "(w" + x.widthMultiplier.ToString("0.00") + ",a" + x.startColor.a.ToString("0.00") + ")"); return s + " | lines: " + string.Join(", ", lines); }
}"""

SIG = {"Press": ["name"], "Hold": ["slot", "on"], "Rival": ["d"], "Move": ["x", "z"]}


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "P6", "methodName": method, "parameters": params}, timeout=90)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:400]


def wait(prefixes, tries=90):
    s = ""
    for _ in range(tries):
        s = cs("State")
        if str(s).startswith(prefixes):
            return s
        time.sleep(1)
    raise SystemExit("timed out waiting for " + str(prefixes) + " (last: " + str(s) + ")")


def shot(name):
    p = bridge.shot("_p6_" + name)
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
    wait(("Active",)); time.sleep(4.5)
    try:
        print(cs("Move", "-6", "-6")); time.sleep(1.0)
        print(cs("Rival", "0")); time.sleep(0.8)
        print(cs("Info"))
        shot("hud_rest")

        print(cs("Hold", SLOT, "1")); time.sleep(0.05)
        print("  50 ms after press:", cs("Info"))
        shot("hold_no_target_50ms"); time.sleep(0.45)
        print("  500 ms:", cs("Info"))
        shot("hold_no_target")
        print(cs("Hold", SLOT, "0")); time.sleep(1.0)

        print(cs("Rival", "1.4")); time.sleep(0.7)
        print(cs("Info"))
        shot("rest_pip")
        print(cs("Hold", SLOT, "1")); time.sleep(0.4)
        print(cs("Rival", "1.4")); time.sleep(0.3)
        print("  holding with rival:", cs("Info"))
        shot("hold_with_target")
        print(cs("Hold", SLOT, "0")); time.sleep(0.5)
    finally:
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


main()
