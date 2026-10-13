"""Phase 7 chunk B live captures: hold with a rival INSIDE the range, hold with no target, the cancel zone at hold start and armed,
and the fizzle puff, in a real solo match.

    python tools/ui_capture/capture_phase7_chunk_b.py <output_dir>
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

ARGS = sys.argv[1:]
OUT = os.path.abspath(ARGS[0]) if ARGS else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using CluckWars.Gameplay; using CluckWars.Abilities; using CluckWars.Input; using CluckWars.Visuals; using CluckWars.UI; using Fusion;
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
  static FieldInfo F(string n) { return typeof(TouchControlsController).GetField(n, BindingFlags.NonPublic|BindingFlags.Instance); }
  // First slot whose move is target-gated and currently has nobody in range (EvaluateRefusal == NoTarget).
  public static string GatedSlot() { var l = Local(); if (l == null) return "-1"; for (int i = 0; i < 4; i++) if (l.Abilities.GetSlot(i) != null && l.Abilities.GetSlot(i).AimShape != AbilityAimShape.None && l.Abilities.EvaluateRefusal(i) == AbilityRefusal.NoTarget) return i.ToString(); return "-1"; }
  public static string Slots() { var l = Local(); var a = l.Abilities; string s = ""; for (int i = 0; i < 4; i++) s += i + ":" + (a.GetSlot(i) == null ? "-" : a.GetSlot(i).name) + " ref=" + a.EvaluateRefusal(i) + " cd=" + a.CooldownRemaining(i).ToString("0.00") + " rearm=" + a.IsRearming(i) + "; "; return s + "active=" + a.MostRecentActiveSlot; }
  // Holds the flash on screen for the shot: stops the HUD's own 0.3 s timer where it is.
  public static string Pin(string slot) { var h = TouchControlsController.Instance; int s = int.Parse(slot);
    var t = (float[])F("_fizzleTimer").GetValue(h); string was = t[s].ToString("0.00"); if (t[s] >= 0f) t[s] = -1f;
    var slots = (System.Array)F("_slots").GetValue(h); var hs = slots.GetValue(s); var tp = hs.GetType();
    var slash = (VisualElement)tp.GetField("FizzleSlash").GetValue(hs); var puff = (VisualElement)tp.GetField("Puff").GetValue(hs);
    return "timer was " + was + " slash=" + slash.resolvedStyle.display + " puff=" + puff.resolvedStyle.display; }
  // The Editor runs at a few fps in the background, so the real 0.3 s flash is over before any shot can be taken. This puts the HUD in
  // the state UpdateFizzle leaves it in at t = 0 (slash + puff shown at full strength) and stops its timer, for the shot.
  public static string ForceFlash(string slot) { var h = TouchControlsController.Instance; int s = int.Parse(slot);
    typeof(TouchControlsController).GetMethod("SetFizzleVisible", BindingFlags.NonPublic|BindingFlags.Instance).Invoke(h, new object[] { s, true });
    ((float[])F("_fizzleTimer").GetValue(h))[s] = -1f; return "flash forced on slot " + slot; }
  public static string Dbg() { var l = Local(); var a = l.Abilities; var T = typeof(AbilityController); var bf = BindingFlags.NonPublic|BindingFlags.Instance;
    return "fizzlePending=" + T.GetField("_fizzlePending", bf).GetValue(a) + " rearm0=" + ((float[])T.GetField("_rearmRemaining", bf).GetValue(a))[0].ToString("0.00") + " pending=" + T.GetField("_pendingHoldSlot", bf).GetValue(a) + " charging=" + a.ChargingSlot + " denied=" + T.GetField("_deniedPressPending", bf).GetValue(a)
      + " matchRunning=" + GameManager.Instance.IsMatchRunning + " heldHud=" + TouchControlsController.Instance.IsAbilityHeld(0); }
  public static string Probe(string slot) { var h = TouchControlsController.Instance; int s = int.Parse(slot);
    var slots = (System.Array)F("_slots").GetValue(h); var hs = slots.GetValue(s); var tp = hs.GetType(); string o = "";
    foreach (var n in new[] { "CancelX", "FizzleSlash", "Puff", "Hex" }) { var e = (VisualElement)tp.GetField(n).GetValue(hs); o += n + "=" + (e == null ? "null" : e.resolvedStyle.display + "/" + e.worldBound + "/op" + e.resolvedStyle.opacity.ToString("0.0")) + "; "; }
    var root = h.GetComponent<UIDocument>().rootVisualElement; foreach (var n in new[] { "EdgeGlowLeft", "EdgeGlowRight", "EdgeGlowTop", "EdgeGlowBottom" }) { var e = root.Q(n); o += n + "=" + (e == null ? "null" : e.resolvedStyle.display + "/" + e.worldBound) + "; "; }
    return o + "root=" + root.worldBound; }
  public static string Arm(string slot, string on) { var h = TouchControlsController.Instance; int s = int.Parse(slot);
    ((bool[])F("_held").GetValue(h))[s] = on == "1"; ((bool[])F("_cancelArmed").GetValue(h))[s] = on == "1"; ((ScreenEdge[])F("_armedEdge").GetValue(h))[s] = on == "1" ? ScreenEdge.Right : ScreenEdge.None;
    return "armed[" + slot + "]=" + on + " IsCancelArmed=" + h.IsCancelArmed; }
  // The HUD's real release path: lift while armed -> cancel.
  public static string Lift(string slot) { var h = TouchControlsController.Instance; typeof(TouchControlsController).GetMethod("EndHexPointer", BindingFlags.NonPublic|BindingFlags.Instance).Invoke(h, new object[] { int.Parse(slot), HexPointerEnd.Up, -1 }); return "lifted " + slot; }
  public static string Info() { var l = Local(); if (l == null) return "no local"; var a = l.Abilities; var ov = l.GetComponent<AbilitySlotOverlay>(); var tg = l.GetComponent<AbilityTelegraph>();
    string s = "rivalDist=" + string.Join("/", ChickenController.ActiveControllers.Where(k => k != null && k != l).Select(k => Vector3.Distance(k.transform.position, l.transform.position).ToString("0.0"))) + " aimed=" + (tg != null ? tg.LocalAimedSlot.ToString() : "-") + " charging=" + a.ChargingSlot + " | ";
    for (int i = 0; i < 4; i++) { var ab = a.GetSlot(i); s += i + ":" + (ab == null ? "-" : ab.name + "/" + ab.AimShape) + " ref=" + a.EvaluateRefusal(i) + " hot=" + (ov != null && ov.IsHot(i)) + "; "; }
    var lines = l.GetComponentsInChildren<LineRenderer>(true).Where(x => x.enabled).Select(x => x.name + "(w" + x.widthMultiplier.ToString("0.00") + ",a" + x.startColor.a.ToString("0.00") + ")"); return s + " | lines: " + string.Join(", ", lines); }

  // Phase 7 B: first slot that uses an aim shape and is ready (refusal None) with the rival placed ahead.
  public static string ReadyAimSlot() { var l = Local(); if (l == null) return "-1"; for (int i = 0; i < 4; i++) { var ab = l.Abilities.GetSlot(i); if (ab != null && ab.AimShape != AbilityAimShape.None && l.Abilities.EvaluateRefusal(i) == AbilityRefusal.None) return i.ToString(); } return "-1"; }
  // Stops every bot brain so a rival teleported into the shape stays there for the shot.
  public static string FreezeBots() { string o = ""; foreach (var k in ChickenController.ActiveControllers) { if (k == null || k.HasInputAuthority) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot") && m.enabled) { m.enabled = false; o += m.GetType().Name + " "; } } return "froze " + o; }
  public static string ArmEdge(string slot, string edge) { var h = TouchControlsController.Instance; int s = int.Parse(slot);
    ((bool[])F("_held").GetValue(h))[s] = true; ((bool[])F("_cancelArmed").GetValue(h))[s] = true; ((ScreenEdge[])F("_armedEdge").GetValue(h))[s] = (ScreenEdge)int.Parse(edge); return "armed " + edge; }
  public static string Zone() { var h = TouchControlsController.Instance; var root = h.GetComponent<UIDocument>().rootVisualElement; string o = "";
    foreach (var n in new[] { "EdgeGlowLeft", "EdgeGlowRight", "EdgeGlowTop", "EdgeGlowBottom" }) { var e = root.Q(n); o += n + "=" + e.resolvedStyle.display + "/" + e.worldBound.width.ToString("0") + "x" + e.worldBound.height.ToString("0") + "/armed=" + e.ClassListContains("cw-edge-glow--armed") + "; "; } return o; }
}"""

SIG = {"Press": ["name"], "Hold": ["slot", "on"], "Rival": ["d"], "Move": ["x", "z"], "Pin": ["slot"], "ForceFlash": ["slot"], "Probe": ["slot"], "Arm": ["slot", "on"], "Lift": ["slot"], "ArmEdge": ["slot", "edge"]}


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

        # 1. hold with NO target (rival far): dashed cream outline on ink, cream fill
        print(cs("Rival", "0")); time.sleep(0.8)
        gated = str(cs("GatedSlot")); print("gated slot:", gated, cs("Slots"))
        if gated != "-1":
            print(cs("Hold", gated, "1")); time.sleep(1.2)
            print(cs("Info")); print(cs("Zone"))
            shot("hold_no_target__cancel_zone_start")
            # 2b. armed on the right edge
            print(cs("ArmEdge", gated, "2")); time.sleep(0.8)
            print(cs("Zone"))
            shot("hold_cancel_armed")
            print(cs("Lift", gated)); print(cs("Hold", gated, "0")); time.sleep(0.8)
            print(cs("Rival", "0")); time.sleep(0.5)
            # fizzle puff
            print(cs("ForceFlash", gated)); time.sleep(1.4)
            shot("fizzle_puff")
            time.sleep(1.0)

        # 2. hold with a rival INSIDE the shape: solid accent outline, in-range pip
        print(cs("FreezeBots")); print(cs("Rival", "1.6")); time.sleep(1.5)
        slot = str(cs("ReadyAimSlot")); print("ready aim slot:", slot, cs("Slots"))
        if slot != "-1":
            print(cs("Hold", slot, "1")); time.sleep(1.5)
            print(cs("Info"))
            shot("hold_with_rival_in_range")
            print(cs("Hold", slot, "0")); time.sleep(0.5)
    finally:
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


main()
