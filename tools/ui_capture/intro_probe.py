"""Phase 3B review: samples the in-game intro (3-2-1-GO juiced by MatchOverlaysController.RefreshIntro) after a real Solo START.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/intro_probe.py <output_dir>
Runs two Play sessions (Reduced Motion OFF, then ON); writes <output_dir>/intro_log.txt and intro_*_mid_2.png.
Clears the stored Reduced Motion preference and leaves Play mode.

    python tools/ui_capture/intro_probe.py <output_dir> --item4
Re-audit item 4: ONE Play session (run it as the first Play after a script recompile = domain reload). Real Solo
START from THE COOP, then: the menu's GET READY card (getready_card.png), the scene swap, every GameManager state,
every intro digit with timestamps (intro_3/2/1/go.png), the GameManager's audio (+ the music sources after GO and after the event), the in-game GET READY card's on-screen time, the round
running out on the TIMER (scores capped below the goal; the comeback banner in its last 10 s -> event_banner.png), PLAY AGAIN -> in-session START and the second intro
(intro_r2_*.png). Writes <output_dir>/intro_item4_log.txt and leaves Play mode.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

OUT = os.path.abspath([a for a in sys.argv[1:] if not a.startswith("--")][0]) if [a for a in sys.argv[1:] if not a.startswith("--")] else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(OUT, exist_ok=True)
OUTCS = OUT.replace("\\", "/")
CS = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "intro_probe.cs.txt"), encoding="utf-8").read()
SIG = {"Arm4": ["outDir", "dur"], "Install": ["prefix", "dur"], "Arm": ["prefix", "dur"], "SetReduced": ["on"], "Dump": ["path"], "Press": ["name"]}
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

def item4():
    """See the module docstring (--item4)."""
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(60):
        if cs("Press", "SoloBtn").startswith("clicked"): break
        time.sleep(1)
    say("##### ITEM4:", cs("SetReduced", "0"))
    time.sleep(1.0); cs("Press", "NextBtn"); time.sleep(1.2)
    say(cs("FillQuiet")); time.sleep(0.5); say(cs("Press", "ReadyBtn")); time.sleep(1.8)
    say(cs("Arm4", OUTCS, "150"))
    say("real START:", cs("Press", "StartBtn"))
    # No bridge calls through the load + intro: every cs() call compiles the probe on the main thread
    # (a 0.5-0.8 s hitch of its own) and would pollute the timings being measured.
    time.sleep(16)
    for _ in range(45):
        if cs("State") == "Ended": break
        time.sleep(2)
    say("state after round 1:", cs("State"))
    time.sleep(2.5); say(cs("Press", "MePlayAgainBtn"))
    time.sleep(2.5); say("state after PLAY AGAIN:", cs("State"))
    say("lobby START:", cs("Press", "LobbyStartBtn"))
    time.sleep(9)
    say("state:", cs("State"))
    p = f"{OUTCS}/_tmp_item4.txt"; cs("Dump", p); time.sleep(0.3)
    LOG.append(open(p, encoding="utf-8").read()); os.remove(p)
    cs("ClearReduced")
    bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)

bridge.init()
try:
    if "--item4" in sys.argv:
        item4()
    elif "--playagain" in sys.argv:
        playagain("PLAYAGAIN_OFF", False)
        playagain("PLAYAGAIN_RM", True)
    else:
        session("OFF", False)
        session("RM", True)
finally:
    open(os.path.join(OUT, "intro_item4_log.txt" if "--item4" in sys.argv else "intro_playagain_log.txt" if "--playagain" in sys.argv else "intro_log.txt"), "w", encoding="utf-8").write(chr(10).join(LOG))
    try: cs("ClearReduced")
    except Exception: pass
    bridge.call("editor-application-set-state", {"isPlaying": False})
