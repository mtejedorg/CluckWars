"""Phase 3B review: samples the in-game intro (3-2-1-GO juiced by MatchOverlaysController.RefreshIntro) after a real Solo START.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/intro_probe.py <output_dir>
Runs two Play sessions (Reduced Motion OFF, then ON); writes <output_dir>/intro_log.txt and intro_*_mid_2.png.
Clears the stored Reduced Motion preference and leaves Play mode.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

OUT = os.path.abspath([a for a in sys.argv[1:] if not a.startswith("--")][0]) if [a for a in sys.argv[1:] if not a.startswith("--")] else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(OUT, exist_ok=True)
OUTCS = OUT.replace("\\", "/")
CS = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "intro_probe.cs.txt"), encoding="utf-8").read()
SIG = {"Install": ["prefix", "dur"], "Arm": ["prefix", "dur"], "SetReduced": ["on"], "Dump": ["path"], "Press": ["name"]}
LOG = []

def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    r = bridge.text(bridge.call("script-execute", {"csharpCode": CS, "className": "Intro", "methodName": method, "parameters": params}, timeout=90))
    try:
        return json.loads(r)["result"]["value"]
    except Exception:
        return r[:200]

def say(*a):
    s = " ".join(str(x) for x in a); print(s); LOG.append(s)

def session(tag, reduced):
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(60):
        if cs("Press", "SoloBtn").startswith("clicked"): break
        time.sleep(1)
    say(f"##### {tag}:", cs("SetReduced", "1" if reduced else "0"))
    time.sleep(1.0); say(cs("Press", "NextBtn")); time.sleep(1.2)
    say(cs("FillQuiet")); time.sleep(0.5); say(cs("Press", "ReadyBtn")); time.sleep(1.8)
    say(cs("Arm", f"{OUTCS}/intro_{tag}", "14"))   # installs itself as soon as the Game scene's overlays exist
    say("real START:", cs("Press", "StartBtn"))
    time.sleep(22)
    p = f"{OUTCS}/_tmp_{tag}.txt"; cs("Dump", p); time.sleep(0.3)
    LOG.append(open(p, encoding="utf-8").read()); os.remove(p)
    say("state:", cs("State"))
    cs("ClearReduced")
    bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)

def playagain(tag, reduced):
    """Solo match -> forced win -> PLAY AGAIN -> in-session lobby START: a fresh intro with no scene load in front of it."""
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(60):
        if cs("Press", "SoloBtn").startswith("clicked"): break
        time.sleep(1)
    say(f"##### {tag}:", cs("SetReduced", "1" if reduced else "0"))
    time.sleep(1.0); cs("Press", "NextBtn"); time.sleep(1.2)
    cs("FillQuiet"); time.sleep(0.5); cs("Press", "ReadyBtn"); time.sleep(1.8)
    say(cs("Arm", f"{OUTCS}/intro_{tag}", "80"))
    say("START:", cs("Press", "StartBtn"))
    for _ in range(90):
        if cs("State") == "Active": break
        time.sleep(1)
    time.sleep(6)
    say("win:", cs("Win"))
    for _ in range(60):
        if cs("State") == "Ended": break
        time.sleep(1)
    time.sleep(1.5); say("state:", cs("State"), cs("Press", "MePlayAgainBtn"))
    time.sleep(3); say("state after play again:", cs("State"))
    say("lobby START:", cs("Press", "LobbyStartBtn"))
    time.sleep(10)
    p = f"{OUTCS}/_tmp_{tag}.txt"; cs("Dump", p); time.sleep(0.3)
    LOG.append(open(p, encoding="utf-8").read()); os.remove(p)
    say("state:", cs("State"))
    cs("ClearReduced")
    bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)

bridge.init()
try:
    if "--playagain" in sys.argv:
        playagain("PLAYAGAIN_OFF", False)
        playagain("PLAYAGAIN_RM", True)
    else:
        session("OFF", False)
        session("RM", True)
finally:
    open(os.path.join(OUT, "intro_playagain_log.txt" if "--playagain" in sys.argv else "intro_log.txt"), "w", encoding="utf-8").write(chr(10).join(LOG))
    try: cs("ClearReduced")
    except Exception: pass
    bridge.call("editor-application-set-state", {"isPlaying": False})
