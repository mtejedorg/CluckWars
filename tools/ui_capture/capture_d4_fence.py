"""Phase 8 D4 captures: every chicken behind an OUTER fence gets a hole; inner arm walls stay local-only.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_d4_fence.py <output_dir> [explore]

Solo START, bots frozen, UI hidden, MATCH camera (follows the local bird). Shots:
  d4_a_own_corner          local bird behind its near fence at its own base corner (rim visible)
  d4_b_rival_far           a rival behind the near-side fence, at the far end of the screen from the local bird
  d4_c_rival_near          a rival behind the near-side fence right next to the local bird
  d4_d_rival_inner_wall    a rival behind an inner arm wall panel (no hole: inner walls are local-only)
  *_before                 same pose with FenceFadeDriver.EffectEnabled = false
Leaves Play mode at the end.
"""
import sys, os, time
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge
import capture_common

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
EXPLORE = len(sys.argv) > 2 and sys.argv[2] == "explore"
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using System.Text; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class D4 {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
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
  static ChickenController[] Rivals() { var l = Local(); return ChickenController.ActiveControllers.Where(k => k != null && k != l && k.Object != null && k.Object.IsValid).OrderBy(k => k.name).ToArray(); }
  public static string FreezeBots() { var l = Local(); foreach (var k in ChickenController.ActiveControllers) { if (k == null || k == l) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot")) m.enabled = false; } return "froze " + Rivals().Length; }
  public static string HideUi(string on) { foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) if (d.rootVisualElement != null) d.rootVisualElement.style.display = on == "1" ? DisplayStyle.None : DisplayStyle.Flex; return "ui " + on; }
  public static string Effect(string on) { FenceFadeDriver.EffectEnabled = on == "1"; return "effect " + on; }
  static void Put(ChickenController k, Vector3 pos) { pos.y = k.transform.position.y; var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else k.transform.position = pos; }
  public static string MoveLocal(string x, string z) { var l = Local(); if (l == null) return "no local"; Put(l, new Vector3(float.Parse(x, CI), 0, float.Parse(z, CI))); return "local -> " + x + "," + z; }
  public static string MoveRival(string i, string x, string z) { var r = Rivals(); int k = int.Parse(i); if (k >= r.Length) return "no rival " + i; Put(r[k], new Vector3(float.Parse(x, CI), 0, float.Parse(z, CI))); return r[k].name + " -> " + x + "," + z; }
  public static string LocalXZ() { var l = Local(); return l == null ? "" : l.transform.position.x.ToString("0.00", CI) + ";" + l.transform.position.z.ToString("0.00", CI); }
  public static string Park() { var r = Rivals(); for (int k = 0; k < r.Length; k++) Put(r[k], new Vector3(-3f + 3f * k, 0, 0)); return "parked " + r.Length; }
  public static string Info() { var sb = new StringBuilder(); var cam = MatchCamera.Instance.Camera; var d = Object.FindFirstObjectByType<FenceFadeDriver>();
    sb.Append("cam pos=" + cam.transform.position.ToString("F2") + " rot=" + cam.transform.eulerAngles.ToString("F1") + " ortho=" + cam.orthographicSize + " | ");
    var l = Local(); sb.Append("local=" + (l ? l.transform.position.ToString("F2") : "-") + " | ");
    foreach (var r in Rivals()) sb.Append(r.name + (r.IsDecoy ? "(decoy)" : "") + "=" + r.transform.position.ToString("F2") + " op=" + r.VisualOpacity.ToString("0.00") + " ; ");
    sb.Append(" | local strength=" + (d ? d.Strength.ToString("0.00") : "-") + " slots=" + (d ? d.SlotCount.ToString() : "-") + " maxSlot=" + (d ? d.MaxSlotStrength.ToString("0.00") : "-")
      + " kw=" + Shader.IsKeywordEnabled("_CW_FENCE_FADE") + " count=" + Shader.GetGlobalFloat("_CW_FenceBirdCount") + " state=" + State());
    return sb.ToString(); }
  public static string Walls() { var sb = new StringBuilder();
    foreach (var n in new[] { "Wall_N_0", "Wall_N_6", "Wall_S_0", "Wall_S_6", "Wall_E_0", "Wall_E_6", "Wall_W_0", "Wall_W_6", "InnerWall_0_1", "InnerWall_1_1", "InnerWall_2_1", "InnerWall_3_1", "InnerWall_4_1", "InnerWall_5_1", "InnerWall_6_1", "InnerWall_7_1" }) {
      var t = GameObject.Find(n); if (t == null) { sb.Append(n + "=? "); continue; } var r = t.GetComponentInChildren<Renderer>(); sb.Append(n + "=" + r.bounds.center.ToString("F1") + "/" + r.bounds.size.ToString("F1") + " "); }
    foreach (var b in Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None)) sb.Append("Base " + b.transform.position.ToString("F1") + " ");
    return sb.ToString(); }
}"""

SIG = {"Press": ["name"], "Effect": ["on"], "HideUi": ["on"], "MoveLocal": ["x", "z"], "MoveRival": ["i", "x", "z"]}


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "D4", "methodName": method, "parameters": params}, timeout=120)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:600]


def wait(prefixes, tries=200, dt=0.25):
    s = ""
    for _ in range(tries):
        s = str(cs("State"))
        if s.startswith(prefixes):
            return s
        time.sleep(dt)
    raise SystemExit("timed out waiting for " + str(prefixes) + " (last: " + s + ")")


def shot(name, before=True):
    print("  ", name, cs("Info"))
    p = bridge.shot("_d4_" + name)
    if p:
        os.replace(p, os.path.join(OUT, name + ".png"))
    if before:
        cs("Effect", "0"); time.sleep(0.4)
        p = bridge.shot("_d4_" + name + "_before")
        if p:
            os.replace(p, os.path.join(OUT, name + "_before.png"))
        cs("Effect", "1"); time.sleep(0.4)


def start():
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
    wait(("Active",))
    while str(cs("State")) != "Active":
        time.sleep(0.3)
    time.sleep(1.0)
    print(cs("FreezeBots")); print(cs("HideUi", "1"))


ROT = 0  # quarter turns taking the authored SE-base frame to the local spawn base (set in main)


def R(x, z):
    """Authored poses assume the local base at (21.8, -21.8) (camera yaw 315). The map is 4-fold rotationally
    symmetric (pinwheel arms, base corners), so rotate by quarter turns R(x, z) = (-z, x) to the real spawn base."""
    x, z = float(x), float(z)
    for _ in range(ROT):
        x, z = -z, x
    return f"{x:.2f}", f"{z:.2f}"


def pose(local, rivals, settle=1.2):
    """local = (x, z); rivals = {index: (x, z)} in the authored frame; the others are parked in the arena centre."""
    print(cs("Park"))
    print(cs("MoveLocal", *R(*local)))
    for i, (x, z) in rivals.items():
        print(cs("MoveRival", i, *R(x, z)))
    time.sleep(settle)


def shots():
    # Authored for the SE base (21.8, -21.8), match camera yaw 315: the near fence is the SOUTH run (z = -25.2, inner
    # face ~-24.75) and the EAST run (x = 25.2); a bird ~0.5 m inside a near run is covered.
    pose(("21.8", "-21.8"), {})
    shot("d4_a_own_corner")
    pose(("18", "-20"), {0: ("11", "-24.2"), 1: ("24.2", "-15")})
    shot("d4_b_rival_far")
    pose(("18", "-20"), {0: ("16", "-24.2")})
    shot("d4_c_rival_near")
    # InnerWall_6 runs along Z at x = 4.7 (camera side = +x): a rival at x = 3.8 is behind it.
    pose(("7.5", "-9"), {0: ("3.8", "-11.4")})
    shot("d4_d_rival_inner_wall")
    # Same wall, the local bird behind it too: its own hole still opens on the inner wall, the rival's does not.
    pose(("3.8", "-12.6"), {0: ("3.8", "-10.2")})
    shot("d4_e_local_and_rival_inner_wall")


def main():
    start()
    try:
        print("PASS " + capture_common.cap_info())
        print(cs("Walls")); print(cs("Info"))
        global ROT
        lx, lz = [float(v) for v in str(cs("LocalXZ")).split(";")]
        corner = (1 if lx > 0 else -1, 1 if lz > 0 else -1)
        ROT = [(1, -1), (1, 1), (-1, 1), (-1, -1)].index(corner)
        print("spawn corner", corner, "quarter turns", ROT)
        if EXPLORE:
            p = bridge.shot("_d4_explore")
            if p:
                os.replace(p, os.path.join(OUT, "explore.png"))
            return
        shots()
    finally:
        cs("HideUi", "0"); cs("Effect", "1")
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


if __name__ == "__main__":
    bridge.init()
    with capture_common.perf_mode():
        main()
