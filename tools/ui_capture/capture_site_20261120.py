"""Retake of the five compendium-site captures (BL-204): hero/preview, hold, lobby, loadout.

Usage (Editor open on Bootstrap, not in Play mode):
    python tools/ui_capture/capture_site_20261120.py <group> [<out_dir>]      groups: match, hold, menus
Reuses the C# helper class and python helpers of capture_controls_20261120.py (read from its source, not imported:
that module runs a group on import). Shots are 1920x1080 PNG; downscaling to the 1280x720 JPEGs is done separately.
"""
import sys, os, time, re
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(HERE, "capture_controls_20261120.py"), encoding="utf-8").read()
ns = {"__name__": "ctl"}
head = src.split("GROUPS = {")[0]
head = head.replace("GROUP = sys.argv[1] if len(sys.argv) > 1 else \"probe\"", "GROUP = 'x'")
head = head.replace("OUT = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else", "OUT = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else")
# extra C# helpers
EXTRA = r'''
  static ChickenController Rival() { var l = Local(); return ChickenController.ActiveControllers.Where(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k != l).OrderBy(k => (k.transform.position - l.transform.position).sqrMagnitude).FirstOrDefault(); }
  public static string ToRival() { var l = Local(); var r = Rival(); if (r == null) return "no rival"; var d = (l.transform.position - r.transform.position); d.y = 0; d = d.normalized * 2.0f;
    var pos = r.transform.position + d; pos.y = l.transform.position.y; var nt = l.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else l.transform.position = pos; return "to rival " + r.name + " dist=" + Vector3.Distance(l.transform.position, r.transform.position).ToString("0.0"); }
  public static string ToRivalPile() { var l = Local(); var r = Rival(); var piles = Object.FindObjectsByType<FoodPile>(FindObjectsSortMode.None); if (r == null || piles.Length == 0) return "none";
    var p = piles.OrderBy(f => Vector3.Distance(f.transform.position, r.transform.position)).First(); var d = r.transform.position - p.transform.position; d.y = 0; var side = Vector3.Cross(Vector3.up, d.sqrMagnitude < 0.01f ? Vector3.forward : d.normalized).normalized * 1.6f;
    var pos = p.transform.position + side; pos.y = l.transform.position.y; var nt = l.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else l.transform.position = pos; return "rival " + r.name + " at " + r.transform.position + " pile " + p.transform.position + " me " + pos; }
  public static string HideGlow() { var d = Doc("EdgeGlowLeft"); if (d == null) return "noglow"; int n = 0; foreach (var nm in new[] { "EdgeGlowLeft", "EdgeGlowRight", "EdgeGlowTop", "EdgeGlowBottom" }) { var e = d.rootVisualElement.Q(nm); if (e != null) { e.style.display = DisplayStyle.None; n++; } } return "hid " + n; }
  public static string PileDist() { var l = Local(); var piles = Object.FindObjectsByType<FoodPile>(FindObjectsSortMode.None); return piles.Length == 0 ? "-" : piles.Min(f => Vector3.Distance(f.transform.position, l.transform.position)).ToString("0.0"); }
  public static string ToPileN(string n) { var l = Local(); var r = Rival(); var piles = Object.FindObjectsByType<FoodPile>(FindObjectsSortMode.None).OrderBy(f => f.transform.position.x).ThenBy(f => f.transform.position.z).ToArray(); var p = piles[int.Parse(n) % piles.Length];
    var d = r == null ? Vector3.right : (r.transform.position - p.transform.position); d.y = 0; d = d.normalized * -0.9f; var pos = p.transform.position + d; pos.y = l.transform.position.y; var nt = l.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else l.transform.position = pos; return "pile " + n + "/" + piles.Length; }
  public static string Dist() { var l = Local(); var r = Rival(); return r == null ? "no rival" : Vector3.Distance(l.transform.position, r.transform.position).ToString("0.0"); }
  public static string Hold(string slot, string on) { var h = TouchControlsController.Instance; if (h == null) return "nohud"; var f = typeof(TouchControlsController).GetField("_held", BindingFlags.NonPublic|BindingFlags.Instance); var a = (bool[])f.GetValue(h); a[int.Parse(slot)] = on == "1";
    var p = typeof(TouchControlsController).GetField("_pressed", BindingFlags.NonPublic|BindingFlags.Instance); if (p != null) { var pa = (bool[])p.GetValue(h); pa[int.Parse(slot)] = on == "1"; } return "held " + slot + "=" + on; }
}'''
head = head.rstrip()
assert head.count('}"""') == 1
head = head.replace('}"""', EXTRA + '"""', 1)
head = head.replace('"Loadout": ["a", "b", "c", "d"]}', '"Loadout": ["a", "b", "c", "d"], "Hold": ["slot", "on"], "ToPileN": ["n"]}')
GROUP_REQ = sys.argv[1]
_out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, "out", "site-20261120")
sys.argv = [sys.argv[0], "x", _out]
ns["__file__"] = os.path.join(HERE, "capture_controls_20261120.py")
exec(compile(head, "ctl", "exec"), ns)
globals().update({k: v for k, v in ns.items() if k not in ("__name__",)})
OUTDIR = ns["OUT"]


def g_match():
    start_match(); size_idx = cs("GameSizeIndex")
    print(cs("Force", "0")); print(cs("GameSize", "1920", "1080")); time.sleep(2.0)
    print(cs("Auto", "0")); print(cs("Kit", "Warrior_Bully")); time.sleep(0.8)
    for i in range(12):
        cs("ToPileN", str(i)); time.sleep(0.9); d0 = cs("Dist"); shot("hero_%d" % i); print("  attempt", i, "rival dist before/after", d0, cs("Dist"), "pile", cs("PileDist"), cs("State"))
        if cs("State") != "Active":
            break
    print(cs("GameSizeBack", size_idx)); end_play()


def g_hold():
    start_match(); wait_no_intro(); size_idx = cs("GameSizeIndex")
    print(cs("Force", "0")); print(cs("GameSize", "1920", "1080")); time.sleep(2.0)
    print(cs("Auto", "0")); print(cs("Kit", "Warrior_Bully")); time.sleep(0.8)
    print(cs("Hold", "2", "1")); print(cs("HideGlow"))
    for i in range(10):
        cs("ToRival"); cs("HideGlow"); d0 = cs("Dist"); shot("hold_%d" % i); print("  attempt", i, "dist before/after", d0, cs("Dist"))
        if cs("State") != "Active":
            break
    print(cs("Hold", "2", "0")); print(cs("GameSizeBack", size_idx)); end_play()


def g_menus():
    print("compile:", cs("Compile")); print(cs("OpenBootstrap")); time.sleep(1)
    print(cs("Play", "1"))
    for _ in range(90):
        if str(cs("Press", "SoloBtn")).startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(0.9)
    print(cs("GameSize", "1920", "1080")); time.sleep(1.5)
    print(cs("Press", "NextBtn")); time.sleep(1.2)
    shot("gearup_fresh")
    print(cs("Fill")); time.sleep(1.0)
    shot("loadout")
    print(cs("Press", "ReadyBtn")); time.sleep(2.0)
    shot("lobby")
    end_play()


bridge.init()
try:
    {"match": g_match, "hold": g_hold, "menus": g_menus}[GROUP_REQ]()
except BaseException:
    try:
        end_play()
    except Exception:
        pass
    raise
