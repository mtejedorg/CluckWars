"""Phase 6 chunk 5 live smoke: aim byte (mouse-style ground point), touch soft-lock, Quick Moves.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_phase6_chunk5.py <output_dir>

The bridge cannot move the real mouse or press a pad, so the aim is INJECTED: the local FusionNetworkService's and the
local AbilityTelegraph's IInputProvider are wrapped by a fake that forwards everything and returns a chosen
AimInput (a ground point, exactly what the mouse provider returns). Everything downstream (OnInput sampling, the aim byte,
the state authority's rotation on the fire tick, the telegraph preview) is the real code path.
  A. mouse-style aim 100 degrees off facing, Wing Slam, rival in the aimed direction -> bird turns to the aim, hits 1
  B. no aim, touch hex held, rival 20 degrees off facing -> touch soft-lock snaps to it, hits 1
  C. no aim, rival 55 degrees off facing (outside +-30) -> no snap, facing unchanged
  D. Quick Moves on, press and keep holding -> the move fires within ~0.25 s while still held (no charge)
Leaves Play mode at the end and restores the Quick Moves preference.
"""
import sys, os, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

ARGS = sys.argv[1:]
OUT = os.path.abspath(ARGS[0]) if ARGS else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using CluckWars.Gameplay; using CluckWars.Abilities; using CluckWars.Input; using CluckWars.Settings; using CluckWars.Networking; using CluckWars.Visuals; using Fusion;
public class FakeAim : IInputProvider {
  public IInputProvider Inner; public AimInput Aim;
  public Vector2 GetMovement() { return Inner.GetMovement(); }
  public bool GetAbility1Pressed() { return Inner.GetAbility1Pressed(); }
  public bool GetAbility2Pressed() { return Inner.GetAbility2Pressed(); }
  public bool GetAbility3Pressed() { return Inner.GetAbility3Pressed(); }
  public bool GetAbility4Pressed() { return Inner.GetAbility4Pressed(); }
  public bool GetAbilityHeld(int s) { return Inner.GetAbilityHeld(s); }
  public bool GetAbilityCancelPressed() { return Inner.GetAbilityCancelPressed(); }
  public bool GetBackPressed() { return Inner.GetBackPressed(); }
  public bool IsAbilityCancelArmed() { return Inner.IsAbilityCancelArmed(); }
  public InputDeviceKind Device { get { return Inner.Device; } }
  public float LastActiveTime { get { return Inner.LastActiveTime; } }
  public AimInput GetAim(float y) { return Aim; }
}
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
  static ChickenController Local() { return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && !k.IsBot && k.HasInputAuthority); }
  static ChickenController Rival() { return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && !k.HasInputAuthority); }
  static AbilityBaseSO Find(string t) { var g = UnityEditor.AssetDatabase.FindAssets("t:" + t).FirstOrDefault(); return g == null ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<AbilityBaseSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)); }
  static FieldInfo TF(string n) { return typeof(TouchControlsController).GetField(n, BindingFlags.NonPublic|BindingFlags.Instance); }
  static FieldInfo Priv(System.Type t, string n) { return t.GetField(n, BindingFlags.NonPublic|BindingFlags.Instance); }

  // Wraps the real provider on the network service and on the local telegraph; equips three cones in slots 0..2.
  public static string Setup() { var l = Local(); if (l == null) return "no local";
    var svc = Object.FindFirstObjectByType<FusionNetworkService>(); var f = Priv(typeof(FusionNetworkService), "_inputProvider");
    var inner = (IInputProvider)f.GetValue(svc); var fake = new FakeAim { Inner = inner }; f.SetValue(svc, fake);
    var tele = l.GetComponent<AbilityTelegraph>(); Priv(typeof(AbilityTelegraph), "_input").SetValue(tele, fake);
    l.Abilities.SetSlots(null, Find("WingSlamAbilitySO"), Find("HeadbuttAbilitySO"), Find("SnatchAbilitySO"), null);
    for (int i = 0; i < 4; i++) l.Abilities.TriggerCooldown(i, 0.01f);
    return "installed; slots=" + string.Join(",", Enumerable.Range(0, 4).Select(i => l.Abilities.GetSlot(i) == null ? "-" : l.Abilities.GetSlot(i).name)); }

  static object Fake() { var svc = Object.FindFirstObjectByType<FusionNetworkService>(); return Priv(typeof(FusionNetworkService), "_inputProvider").GetValue(svc); }
  static void SetAim(AimInput a) { var o = Fake(); o.GetType().GetField("Aim").SetValue(o, a); }
  public static string AimNone() { SetAim(AimInput.None); return "aim none"; }

  // Rival teleported to <deg> degrees off the bird's facing (+ = clockwise from above) at <dist> m; optionally aims a ground point <aimDeg> off facing.
  public static string Place(string deg, string dist, string aimDeg) { var ci = System.Globalization.CultureInfo.InvariantCulture; var l = Local(); var r = Rival(); if (r == null) return "no rival";
    Vector3 pos = l.transform.position; Vector3 fwd = l.transform.forward; fwd.y = 0; fwd.Normalize();
    Vector3 dir = Quaternion.Euler(0, float.Parse(deg, ci), 0) * fwd; Vector3 rp = pos + dir * float.Parse(dist, ci); rp.y = pos.y;
    var bot = r.GetComponent<BotController>(); if (bot != null) bot.enabled = false; // park the rival so it stays where it was placed
    var nt = r.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(rp); else r.transform.position = rp;
    float ad = float.Parse(aimDeg, ci);
    if (ad > -900f) { Vector3 ad3 = Quaternion.Euler(0, ad, 0) * fwd; SetAim(AimInput.FromGroundPoint(pos + ad3 * 6f)); } else SetAim(AimInput.None);
    return "facing=" + Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg + " rivalDeg=" + deg + " aimDeg=" + aimDeg; }

  public static string Hold(string slot, string on) { var h = TouchControlsController.Instance; int s = int.Parse(slot);
    if (on == "1") { ((bool[])TF("_pressed").GetValue(h))[s] = true; ((bool[])TF("_held").GetValue(h))[s] = true; } else ((bool[])TF("_held").GetValue(h))[s] = false;
    return "slot " + s + " held=" + on; }
  public static string Pref(string on) { PlayerPreferences.QuickMovesEnabled = on == "1"; return "QuickMoves=" + PlayerPreferences.QuickMovesEnabled; }
  public static string Ready(string slot) { var l = Local(); l.Abilities.TriggerCooldown(int.Parse(slot), 0.01f); return "cd reset"; }
  public static string Info() { var l = Local(); var a = l.Abilities; Vector3 f = l.transform.forward;
    return "facingDeg=" + (Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg).ToString("0.0") + " castId=" + a.LastCastEventId + " hits=" + a.LastCastHitCount + " lastSlot=" + a.LastCastSlot + " charging=" + a.ChargingSlot
      + " refusals=" + string.Join("/", Enumerable.Range(0, 3).Select(i => a.EvaluateRefusal(i).ToString())) + " rivalDist=" + (Rival() == null ? "-" : Vector3.Distance(Rival().transform.position, l.transform.position).ToString("0.0"))
      + " tele=" + (AbilityTelegraph.Active != null) + " device=" + ((IInputProvider)Fake()).Device; }
}"""

SIG = {"Press": ["name"], "Pref": ["on"], "Hold": ["slot", "on"], "Place": ["deg", "dist", "aimDeg"], "Ready": ["slot"]}


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "P6", "methodName": method, "parameters": params}, timeout=90)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:600]


def wait(prefixes, tries=90):
    s = ""
    for _ in range(tries):
        s = cs("State")
        if str(s).startswith(prefixes):
            return s
        time.sleep(1)
    raise SystemExit("timed out waiting for " + str(prefixes) + " (last: " + str(s) + ")")


def shot(name):
    p = bridge.shot("_p6c5_" + name)
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
        print(cs("Pref", "0"))
        print(cs("Setup")); time.sleep(0.5)

        print("== A. mouse-style aim 100 deg clockwise of facing, rival in the aimed direction (Wing Slam, slot 0)")
        print(" ", cs("Place", "100", "3", "100")); time.sleep(0.2)
        print("  before:", cs("Info"))
        print(" ", cs("Hold", "0", "1")); time.sleep(0.45)
        print("  while holding:", cs("Info")); shot("A_preview_along_aim")
        print(" ", cs("Hold", "0", "0")); time.sleep(0.5)
        print("  after:", cs("Info"))

        print("== B. no aim, touch hex held, rival 20 deg off facing (Headbutt, slot 1) -> soft-lock snaps to it")
        print(" ", cs("Place", "20", "3", "-999")); time.sleep(0.2)
        print("  before:", cs("Info"))
        print(" ", cs("Hold", "1", "1")); time.sleep(0.35); print(" ", cs("Hold", "1", "0")); time.sleep(0.5)
        print("  after:", cs("Info"))

        print("== C. no aim, rival 55 deg off facing (Snatch, slot 2) -> outside +-30, facing unchanged")
        print(" ", cs("Place", "55", "3", "-999")); time.sleep(0.2)
        print("  before:", cs("Info"))
        print(" ", cs("Hold", "2", "1")); time.sleep(0.35); print(" ", cs("Hold", "2", "0")); time.sleep(0.5)
        print("  after:", cs("Info"))

        print("== D. Quick Moves on: press and KEEP holding -> fires within ~0.25 s, no charge")
        print(cs("Pref", "1")); print(cs("Ready", "0")); print(" ", cs("Place", "0", "3", "-999")); time.sleep(0.2)
        print("  before:", cs("Info"))
        print(" ", cs("Hold", "0", "1")); time.sleep(0.25)
        print("  0.25 s into the hold:", cs("Info"))
        time.sleep(0.5); print("  0.75 s into the hold (must not fire again):", cs("Info")); shot("D_quick_no_preview")
        print(" ", cs("Hold", "0", "0")); time.sleep(0.3)
        print("  after release:", cs("Info"))
    finally:
        print(cs("Pref", "0"))
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


main()
