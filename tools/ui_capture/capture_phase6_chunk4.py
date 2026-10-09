"""Phase 6 chunk 4 live smoke: the Peck auto-chain, move-input stop, Auto-Peck, and the Peck hex chain ring.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_phase6_chunk4.py <output_dir>

Drives a solo match, parks the local bird beside a pile with food, then:
  1. one manual Peck press -> pecks repeat on their own (cargo climbs, PeckChainActive true) until cargo is full
  2. a manual Peck, then move input -> the chain stops and stays stopped
  3. AutoPeck preference on, standing still by the pile with an empty hold -> pecking starts without a press
  autopeck_chain.png  the Peck hex with the dashed chain ring while chaining
Leaves Play mode at the end and restores the AutoPeck preference.
"""
import sys, os, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

ARGS = sys.argv[1:]
OUT = os.path.abspath(ARGS[0]) if ARGS else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using CluckWars.Gameplay; using CluckWars.Abilities; using CluckWars.Input; using CluckWars.Settings; using Fusion;
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
  static int PeckSlot(AbilityController a) { for (int i = 0; i < 4; i++) if (a.GetSlot(i) is PeckAbilitySO) return i; return -1; }
  static FieldInfo F(string n) { return typeof(TouchControlsController).GetField(n, BindingFlags.NonPublic|BindingFlags.Instance); }
  // Parks the local bird at a pile with food, inside collect range.
  public static string Park() { var l = Local(); if (l == null) return "no local"; Vector3 p = l.transform.position;
    var pile = FoodPile.ActivePiles.Where(x => x != null && x.Object != null && x.Object.IsValid && x.HasCollectableFood).OrderByDescending(x => x.Available).FirstOrDefault(); if (pile == null) return "no pile";
    var dir = (Vector3.zero - pile.transform.position); dir.y = 0; dir = dir.sqrMagnitude < 0.01f ? Vector3.forward : dir.normalized;
    Vector3 pos = pile.transform.position; for (float d = 1.2f; d < 6f; d += 0.4f) { pos = pile.transform.position + dir * d; pos.y = l.transform.position.y; if (pile.IsWithinCollectRange(pos)) break; }
    var nt = l.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else l.transform.position = pos;
    return "parked at " + pos + " pile=" + pile.name + " inRange=" + pile.IsWithinCollectRange(pos) + " peckSlot=" + PeckSlot(l.Abilities) + " avail=" + pile.Available; }
  public static string SetCargo(string v) { var l = Local(); l.Cargo.Cargo = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); return "cargo=" + l.Cargo.Cargo; }
  public static string Pref(string on) { PlayerPreferences.AutoPeckEnabled = on == "1"; return "AutoPeck=" + PlayerPreferences.AutoPeckEnabled; }
  // A manual press on the Peck hex, the way the touch HUD latches it.
  public static string PressPeck() { var l = Local(); int s = PeckSlot(l.Abilities); var h = TouchControlsController.Instance;
    ((bool[])F("_pressed").GetValue(h))[s] = true; ((bool[])F("_held").GetValue(h))[s] = true; return "pressed slot " + s; }
  public static string ReleasePeck() { var l = Local(); int s = PeckSlot(l.Abilities); var h = TouchControlsController.Instance; ((bool[])F("_held").GetValue(h))[s] = false; return "released slot " + s; }
  public static string Stick(string x, string y) { var h = TouchControlsController.Instance; var ci = System.Globalization.CultureInfo.InvariantCulture;
    F("_movement").SetValue(h, new Vector2(float.Parse(x, ci), float.Parse(y, ci))); return "stick " + x + "," + y; }
  public static string Info() { var l = Local(); if (l == null) return "no local"; var a = l.Abilities; int s = PeckSlot(a);
    return "cargo=" + l.Cargo.Cargo.ToString("0.0") + "/" + l.Cargo.Capacity.ToString("0.0") + " chain=" + a.PeckChainActive + " peckSlot=" + s + " cd=" + (s >= 0 ? a.CooldownRemaining(s).ToString("0.00") : "-") + " castId=" + a.LastCastEventId + " control=" + l.CurrentControlState
    + " idle=" + typeof(AbilityController).GetField("_idleSeconds", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(a) + " pref=" + PlayerPreferences.AutoPeckEnabled + " usable=" + (s >= 0 && a.GetSlot(s).IsUsable(l)) + " matchRunning=" + GameManager.Instance.IsMatchRunning; }
  public static string ChainRing() { var h = TouchControlsController.Instance; var root = h.GetComponent<UIDocument>().rootVisualElement; var e = root.Query<VisualElement>(className: "cw-hex-chain").ToList();
    return "chainRings=" + e.Count + " shown=" + string.Join(",", e.Select(x => x.resolvedStyle.display.ToString())); }
}"""

SIG = {"Press": ["name"], "SetCargo": ["v"], "Pref": ["on"], "Stick": ["x", "y"]}


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


def log(tag, n=6, gap=0.7):
    for _ in range(n):
        print(" ", tag, cs("Info")); time.sleep(gap)


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
        print(cs("Park")); time.sleep(1.0)
        print(cs("SetCargo", "0")); print(cs("Info"))

        print("== 1. one manual peck -> chain")
        print(cs("PressPeck")); time.sleep(0.3); print(cs("ReleasePeck"))
        log("chain", n=8, gap=0.8)
        print(cs("ChainRing"))

        print("== 2. manual peck, then move input -> chain stops and stays stopped")
        print(cs("Park")); print(cs("SetCargo", "0")); time.sleep(1.0)
        print(cs("PressPeck")); time.sleep(0.3); print(cs("ReleasePeck")); time.sleep(0.3)
        print(" before move:", cs("Info"))
        print(cs("Stick", "1", "0")); time.sleep(0.8); print(cs("Stick", "0", "0"))
        log("after move", n=5, gap=0.8)

        print("== 3. AutoPeck on, standing still by the pile, empty hold -> pecks without a press")
        print(cs("Park")); print(cs("SetCargo", "0")); print(cs("Pref", "1"))
        log("autopeck", n=4, gap=0.8)
        print(cs("Park")); print(cs("SetCargo", "0"))
        for _ in range(30):
            if "chain=true" in str(cs("Info")):
                break
            time.sleep(0.25)
        print(cs("SetCargo", "0"))
        print(" chain for shot:", cs("Info"), cs("ChainRing"))
        shot("autopeck_chain")
        print(" after shot:", cs("Info"))
    finally:
        print(cs("Pref", "0"))
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


main()
