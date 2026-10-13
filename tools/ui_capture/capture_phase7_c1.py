"""Phase 7 chunk C1 live captures: GET READY countdown with the YOU marker visible, rival nameplates on grass and on the coop
floor, the hold hint on touch and on desktop / pad, and a Defense hex close-up.

    python tools/ui_capture/capture_phase7_c1.py <output_dir>

The C# helper class is the chunk B one (read from capture_phase7_chunk_b.py) plus a few probes.
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
  public static string Mode(string m) { TouchControlsController.DebugForceMode = m == "touch" ? HudDeviceMode.Touch : m == "pad" ? HudDeviceMode.Gamepad : HudDeviceMode.KeyboardMouse; return "mode " + m; }
  public static string ResetHint() { CluckWars.Settings.PlayerPreferences.HoldHintCount = 0; CluckWars.Settings.PlayerPreferences.QuickMovesEnabled = false; return "hint reset"; }
  public static string Cats() { var l = Local(); string s = ""; for (int i = 0; i < 4; i++) { var a = l.Abilities.GetSlot(i); s += i + ":" + (a == null ? "-" : a.name + "/" + a.Category) + " "; } return s; }
  public static string CoopRival() { var l = Local(); var coop = GameObject.Find("Base1_Coop"); if (coop == null) return "no coop"; var r = coop.GetComponentInChildren<Renderer>(); var c = r.bounds.center; c.y = l.transform.position.y;
    var rv = ChickenController.ActiveControllers.Where(k => k != null && k != l && !k.IsDecoy && k.Object != null && k.Object.IsValid).FirstOrDefault(); if (rv == null) return "no rival";
    var nt = l.GetComponent<NetworkTransform>(); var nr = rv.GetComponent<NetworkTransform>(); nt.Teleport(c + new Vector3(1.2f, 0, -1.2f)); nr.Teleport(c); return "coop at " + c; }
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
    p = bridge.shot("_c1_" + name)
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
        # GET READY / countdown: shoot while the digit shows (the Editor runs slowly, so try a few times)
        for i in range(60):
            s = str(cs("State"))
            if s.startswith("Active+intro"):
                time.sleep(0.3)
                shot("getready_countdown_you_visible"); break
            time.sleep(0.25)
        for i in range(60):
            if str(cs("State")) == "Active":
                break
            time.sleep(0.5)
        time.sleep(2.0)
        print(cs("Cats"))

        # nameplates on grass: local out of the base, rival frozen 2.5 m ahead
        print(cs("Move", "-6", "-6")); time.sleep(0.8)
        print(cs("FreezeBots")); print(cs("Rival", "2.5")); time.sleep(1.5)
        shot("nameplate_rival_on_grass")
        # nameplates on the coop floor
        print(cs("CoopRival")); time.sleep(1.5)
        shot("nameplate_rival_on_coop_floor")
        print(cs("Move", "-6", "-6")); time.sleep(0.8)

        # hold hint: touch then desktop then pad
        gated = str(cs("GatedSlot"))
        slot = gated if gated != "-1" else "0"
        for mode in ("touch", "desktop", "pad"):
            print(cs("Mode", mode)); print(cs("ResetHint")); time.sleep(1.0)
            print(cs("Hold", slot, "1")); time.sleep(1.5)
            shot("hold_hint__" + mode)
            print(cs("Hold", slot, "0")); time.sleep(0.8)
        print(cs("Mode", "touch")); time.sleep(1.0)
        shot("hud_touch_hexes_for_defense_closeup")
    finally:
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


main()
