"""Phase 3B mid-animation evidence: drives each menu effect in Play mode, logs per-frame style values + audio cues
(a recording IAudioService is swapped in) and takes timed Game-view screenshots, with Reduced Motion OFF then ON.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/juice_probe.py <output_dir>
Writes <output_dir>/midanim_log.txt and <output_dir>/*.png (frames named <scenario>_<seconds>.png).
Leaves Play mode and clears the stored Reduced Motion preference.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(OUT, exist_ok=True)
OUTCS = OUT.replace("\\", "/")
CS = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "juice_probe.cs.txt"), encoding="utf-8").read()
SIG = {"Press": ["btn", "names", "dur", "shots", "prefix", "every"], "PressQuiet": ["btn"], "TapEl": ["name", "names", "dur", "shots", "prefix", "every"],
       "TapFreeCards": ["n", "names", "dur", "shots", "prefix", "every"], "StartThenBack": ["names", "dur", "shots", "prefix", "every", "backAfterMs"],
       "StartFull": ["names", "dur", "shots", "prefix", "every"], "SetReduced": ["on"], "Dump": ["path"]}

DEFERRED = {"Press", "TapEl", "TapFreeCards", "StartThenBack", "StartFull"}
def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    r = bridge.text(bridge.call("script-execute", {"csharpCode": CS, "className": "Rec", "methodName": method, "parameters": params}, timeout=90))
    try:
        v = json.loads(r)["result"]["value"]
    except Exception:
        v = r[:300]
    if method in DEFERRED: time.sleep(1.5)   # the action runs 1.2 s later, from the UI scheduler
    return v

LOG = []
def say(*a):
    s = " ".join(str(x) for x in a); print(s); LOG.append(s)

def dump(tag):
    p = f"{OUTCS}/_tmp_{tag}.txt"
    cs("Dump", p)
    time.sleep(0.2)
    txt = open(p, encoding="utf-8").read()
    os.remove(p)
    return txt

def settle(s=1.0): time.sleep(s)

def run_scenarios(rm):
    tag = "RM_" if rm else ""
    say(f"\n########## REDUCED MOTION {'ON' if rm else 'OFF'}: ", cs("SetReduced", "1" if rm else "0"))
    # --- stagger: class select
    say(cs("ClearLastSetup"))
    say(cs("Press", "SoloBtn", "ClassWarrior,ClassAssassin", "0.7", "0.30,0.42", f"{OUTCS}/{tag}1_stagger_class", "3"))
    settle(1.2); say(dump(tag + "s1"))
    # --- tile pop
    say(cs("TapEl", "ClassSpeedy", "ClassSpeedy", "0.4", "0.07,0.13", f"{OUTCS}/{tag}2_tilepop", "1"))
    settle(0.8); say(dump(tag + "s2"))
    # --- loadout entry stagger
    say(cs("Press", "NextBtn", "SlotBox0,SlotBox3", "0.8", "0.30,0.42", f"{OUTCS}/{tag}3_stagger_loadout", "3"))
    settle(1.2); say(dump(tag + "s3"))
    # --- equip fly: one card
    say(cs("TapFreeCards", "1", "SlotBox0,SlotBox1,SlotBox2", "0.7", "0.10,0.17,0.24", f"{OUTCS}/{tag}4_fly", "1"))
    settle(1.0); say(dump(tag + "s4"))
    say("after single equip:", cs("State"), "|", cs("Slots"))
    # --- rapid equips (3 taps in one frame)
    say(cs("TapFreeCards", "3", "", "0.9", "0.12,0.20", f"{OUTCS}/{tag}4b_rapid", "2"))
    pass
    settle(1.0); say(dump(tag + "s5"))
    say("after rapid equips:", cs("State"), "|", cs("Slots"))
    # --- READY stamp
    say(cs("FillQuiet")); settle(0.3)
    say(cs("Press", "ReadyBtn", "ReadyBanner", "1.4", "0.55,0.62,0.75", f"{OUTCS}/{tag}5_readystamp", "1"))
    settle(2.0); say(dump(tag + "s6"))
    say(cs("Pages"), cs("State"))

def lobby_ready():
    say(cs("ClearLastSetup"))
    say(cs("PressQuiet", "SoloBtn")); time.sleep(1.0)
    say(cs("PressQuiet", "NextBtn")); time.sleep(1.0)
    say(cs("FillQuiet")); time.sleep(0.4)
    say(cs("PressQuiet", "ReadyBtn")); time.sleep(1.8)

def start_play():
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(60):
        s = cs("Pages")
        if s.startswith("pages="): break
        time.sleep(1)
    say("menu:", s)
    time.sleep(2)
    say(cs("Init"))

bridge.init()
try:
    # Session 1: Reduced Motion OFF, every effect.
    start_play(); run_scenarios(False)
    bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)
    # Session 2: Reduced Motion ON, same walk.
    start_play(); run_scenarios(True)
    say(cs("ClearReducedPref"))
    bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)
finally:
    try: say(cs("ClearReducedPref"))
    except Exception: pass
    open(os.path.join(OUT, "midanim_log.txt"), "w", encoding="utf-8").write(chr(10).join(LOG))
    bridge.call("editor-application-set-state", {"isPlaying": False})
