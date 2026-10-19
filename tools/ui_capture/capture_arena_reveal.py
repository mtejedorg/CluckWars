"""Arena-reveal capture + analysis: does the Game scene ever show the player a half-ready arena?

Drives a real Solo match from Bootstrap (same menu path as intro_probe.py), records a frame sequence (~100 ms apart, Game view resolution,
via ScreenCapture) from the FIRST Game-scene frame to GO + 2 s, with one telemetry line per frame (GameManager state, intro
digit, #ArenaCover visibility/opacity, the reveal latch, the ArenaReadiness gates), then analyses it:

  * PLACEHOLDERS: for every frame where the world is visible (not hidden by the opaque cover / the GET READY card) count async-compile
    cyan and error magenta by HUE (the intro scrim dims the placeholder: cyan (0,255,255) becomes (0,140,140) under it):
    cyan = G >= 90, R <= G/5, B within 85-120 % of G; magenta = R >= 90, G <= R/5, B within 85-120 % of R. Exit code 1 if any
    visible frame exceeds 0.2 % of its pixels. Honest exclusion: none needed - the game's UI has no pure teal/magenta (hex icons and
    avatar dots keep red or green well above these ratios); the covered frames (UI only, no world) are measured the same way and
    reported as the baseline, and the warm-cache runs (before_cold_run1/2) measured 0.000 % with the same rule.
  * COVER: mean luma of the covered frames (ink rgb(20,10,4) is luma ~12; the gold GET READY plate and digit raise it).
  * A contact sheet PNG of the sequence and a zoom sheet around the reveal.

usage (Editor open, Bootstrap the only open scene, Edit mode):
    python tools/ui_capture/capture_arena_reveal.py <out_dir> [--cold] [--mobile] [--analyze-only]
      --cold          quit the Editor, relaunch it, wait for the bridge, then run: the FIRST Play pass after a relaunch is the cold pass
      --clear-shader-cache  with --cold: also move Library/ShaderCache(.db) aside first (a relaunch alone keeps the on-disk compiled
                      variants, so it does NOT reproduce the async-compile cyan; STATE.md: ~3 Play passes on PC before the variants compile)
      --mobile        switch the Quality level to Mobile for the pass (restored afterwards) - the Mobile render tier
      --analyze-only  just re-run the analysis on an existing <out_dir>
      --playagain     round 1, forced win, PLAY AGAIN, round 2 (<out_dir>/round1, round2): the cover must not come back
Performance Mode follows capture_common.perf_mode (default OFF, PERF_MODE=1 for ON) and is restored afterwards.
Writes <out_dir>/{f####.png, frames.jsonl, report.txt, contact_sheet.png, reveal_zoom.png, editor_log_arena.txt}.
"""
import sys, os, json, time, subprocess, shutil, glob, re
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bridge
import capture_common

PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
UNITY = r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe"
EDITOR_LOG = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Unity", "Editor", "Editor.log")
THRESHOLD = 0.002   # 0.2 % of the pixels
CS = open(os.path.join(HERE, "capture_arena_reveal.cs.txt"), encoding="utf-8").read()
SIG = {"Arm": ["outDir", "sceneName", "interval", "postGo", "maxSecs"], "Press": ["name"], "Quality": ["mode"]}
QUALITY_CS = r"""
public class Q { public static string Go(string mode) { int mob = System.Array.IndexOf(UnityEngine.QualitySettings.names, "Mobile");
  if (mode == "get") return UnityEngine.QualitySettings.names[UnityEngine.QualitySettings.GetQualityLevel()];
  int i = mode == "mobile" ? mob : System.Array.IndexOf(UnityEngine.QualitySettings.names, mode); if (i < 0) return "ERROR no level " + mode;
  UnityEngine.QualitySettings.SetQualityLevel(i, true); return "level=" + UnityEngine.QualitySettings.names[UnityEngine.QualitySettings.GetQualityLevel()] + " rp=" + UnityEngine.QualitySettings.renderPipeline.name; } }
"""
LOG = []


def say(*a):
    s = " ".join(str(x) for x in a)
    print(s, flush=True)
    LOG.append(s)


def cs(method, *args, timeout=90):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    r = bridge.call("script-execute", {"csharpCode": CS, "className": "Rev", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        return r["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(r)[:300]


def quality(mode):
    r = bridge.call("script-execute", {"csharpCode": QUALITY_CS, "className": "Q", "methodName": "Go",
                                      "parameters": [{"name": "mode", "typeName": "System.String", "value": mode}]}, timeout=60)
    try:
        return r["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(r)[:300]


# ---------------------------------------------------------------------------------------------------------------- cold restart
def unity_running():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Unity.exe", "/FO", "CSV", "/NH"], capture_output=True, text=True).stdout
    return "Unity.exe" in out


def cold_restart(scratch):
    st = bridge.text(bridge.call("scene-list-opened", {}))
    if '"IsDirty":true' in st:
        raise SystemExit("refusing to quit the Editor: an open scene is dirty: " + st)
    say("COLD: quitting the Editor ...")
    try:
        bridge.call("script-execute", {"csharpCode": "public class X { public static string Go() { UnityEditor.EditorApplication.Exit(0); return \"bye\"; } }",
                                      "className": "X", "methodName": "Go", "parameters": []}, timeout=20)
    except Exception as e:
        say("  (exit call ended with", type(e).__name__, "- expected while the Editor quits)")
    t0 = time.time()
    while unity_running():
        if time.time() - t0 > 240:
            raise SystemExit("Unity.exe did not exit within 240 s")
        time.sleep(2)
    say(f"  Unity.exe gone after {time.time() - t0:.0f} s")
    bk = os.path.join(PROJECT, "Temp", "__Backupscenes")
    if os.path.isdir(bk):
        dst = os.path.join(scratch, "Backupscenes_" + time.strftime("%H%M%S"))
        shutil.move(bk, dst)
        say("  moved Temp/__Backupscenes out of Temp ->", dst)
    if "--clear-shader-cache" in sys.argv:
        # A truly cold shader state: the Editor's on-disk compiled-variant cache (derived data, regenerated on demand) goes to the
        # scratch dir (moved, not deleted), so every variant the match draws is compiled asynchronously again (cyan placeholders).
        for name in ("ShaderCache", "ShaderCache.db"):
            src = os.path.join(PROJECT, "Library", name)
            if os.path.exists(src):
                dst = os.path.join(scratch, f"{name}_{time.strftime('%H%M%S')}")
                shutil.move(src, dst)
                say(f"  moved Library/{name} aside -> {dst}")
    time.sleep(3)
    say("COLD: relaunching Unity ...")
    subprocess.Popen([UNITY, "-projectPath", PROJECT], creationflags=0x00000008 | 0x00000200, close_fds=True)   # DETACHED_PROCESS | NEW_PROCESS_GROUP
    t0 = time.time()
    while True:
        if time.time() - t0 > 900:
            raise SystemExit("bridge did not answer within 15 min (a modal dialog? 'Recovering Scene Backups'?)")
        time.sleep(6)
        try:
            bridge._sid = None   # the relaunched Editor does not know the old MCP session id: initialize afresh
            bridge.init()
            s = bridge.text(bridge.call("editor-application-get-state", {}, timeout=20))
            if '"IsCompiling":false' in s and '"IsUpdating":false' in s:
                break
        except Exception:
            continue
    say(f"  bridge answers after {time.time() - t0:.0f} s; settling 10 s")
    time.sleep(10)
    sc = bridge.text(bridge.call("scene-list-opened", {}))
    say("  scenes:", sc[:200])
    if "Bootstrap" not in sc:
        raise SystemExit("Bootstrap is not the open scene after the relaunch: " + sc[:300])


# ---------------------------------------------------------------------------------------------------------------- the pass
def run_pass(out):
    if os.path.isdir(out):
        for f in glob.glob(os.path.join(out, "f*.png")) + glob.glob(os.path.join(out, "*.jsonl")):
            os.remove(f)
    os.makedirs(out, exist_ok=True)
    outcs = out.replace("\\", "/")
    log_from = os.path.getsize(EDITOR_LOG) if os.path.exists(EDITOR_LOG) else 0
    say("PASS: entering Play ->", out)
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(90):
        if str(cs("Press", "SoloBtn")).startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(1.0)
    say(cs("Press", "NextBtn"))
    time.sleep(1.2)
    say(cs("FillQuiet"))
    time.sleep(0.5)
    say(cs("Press", "ReadyBtn"))
    time.sleep(1.8)
    say(cs("Arm", outcs, "Game", "0.1", "2.0", "45"))
    say("real START:", cs("Press", "StartBtn"))
    # No bridge calls through the load + intro: every cs() call compiles on the main thread (a hitch of its own).
    time.sleep(34)
    st = ""
    for _ in range(60):
        st = str(cs("Status"))
        if st.startswith("done"):
            break
        time.sleep(3)
    say("recorder:", st, "| match state:", cs("State"))
    bridge.call("editor-application-set-state", {"isPlaying": False})
    time.sleep(4)
    # Editor.log slice for this pass: the readiness lines + shader-compile chatter.
    try:
        with open(EDITOR_LOG, "rb") as f:
            f.seek(log_from)
            txt = f.read().decode("utf-8", "replace")
        keep = [l for l in txt.splitlines() if re.match(r"\[\d\d:\d\d,\d+\]\[", l) and ("ArenaReadiness" in l or "Arena revealed" in l or "arena cover" in l.lower())]
        open(os.path.join(out, "editor_log_arena.txt"), "w", encoding="utf-8").write("\n".join(keep))
        for l in keep:
            say("  LOG", l[:260])
        if not keep:
            say("  LOG (no ArenaReadiness lines: pre-change build?)")
    except Exception as e:
        say("  could not read Editor.log:", e)
    return st.startswith("done")


def run_playagain(out):
    """Round 1 (recorded to <out>/round1), a forced win, PLAY AGAIN, then round 2 (recorded to <out>/round2): after the first reveal the cover
    must never come back (cover stays hidden), and round 2 keeps its GET READY card + 3-2-1 + GO."""
    r1, r2 = os.path.join(out, "round1"), os.path.join(out, "round2")
    for d in (r1, r2):
        if os.path.isdir(d):
            shutil.rmtree(d)
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(90):
        if str(cs("Press", "SoloBtn")).startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(1.0); cs("Press", "NextBtn"); time.sleep(1.2); cs("FillQuiet"); time.sleep(0.5); cs("Press", "ReadyBtn"); time.sleep(1.8)
    say(cs("Arm", r1.replace("\\", "/"), "Game", "0.1", "2.0", "45"))
    say("START:", cs("Press", "StartBtn"))
    time.sleep(22)
    say("round 1 recorder:", cs("Status"), "state:", cs("State"))
    say("forced win:", cs("Win"))
    for _ in range(40):
        if cs("State") == "Ended":
            break
        time.sleep(1)
    time.sleep(1.5)
    say("PLAY AGAIN:", cs("Press", "MePlayAgainBtn"))
    say(cs("Arm", r2.replace("\\", "/"), "Game", "0.1", "2.0", "45"))
    time.sleep(24)
    st = str(cs("Status"))
    say("round 2 recorder:", st, "state:", cs("State"))
    bridge.call("editor-application-set-state", {"isPlaying": False})
    time.sleep(4)
    return st.startswith("done")


# ---------------------------------------------------------------------------------------------------------------- analysis
def analyze(out):
    import numpy as np
    from PIL import Image, ImageDraw
    rows = []
    for line in open(os.path.join(out, "frames.jsonl"), encoding="utf-8"):
        line = line.strip()
        if line:
            rows.append(json.loads(line))
    shots = [r for r in rows if "i" in r]
    evs = [r for r in rows if "event" in r]
    go_t = next((e["t"] for e in evs if e["event"] == "go"), None)
    res = []
    for r in shots:
        p = os.path.join(out, "f%04d.png" % r["i"])
        if not os.path.exists(p):
            continue
        a = np.asarray(Image.open(p).convert("RGB")).astype(np.int16)
        R, G, B = a[..., 0], a[..., 1], a[..., 2]
        n = R.size
        # Placeholder colours survive the intro scrim (45 % black: cyan (0,255,255) -> (0,140,140)), so match HUE, not exact values:
        # cyan = no red, green ~ blue (grass is G >> B, sky/UI teal keeps some red); magenta = no green, red ~ blue.
        cyan = int(((G >= 90) & (R * 5 <= G) & (B * 100 >= G * 85) & (B * 100 <= G * 120)).sum())
        mag = int(((R >= 90) & (G * 5 <= R) & (B * 100 >= R * 85) & (B * 100 <= R * 120)).sum())
        luma = float((0.2126 * R + 0.7152 * G + 0.0722 * B).mean())
        cov_on = r.get("cover") == "1" and r.get("coverOpacity", -1) >= 0.95
        gr_on = r.get("getReady") == "1"
        # Telemetry is read in the editor tick and the screenshot lands at the end of the frame, so they can disagree around a
        # state change: a frame counts as hidden only if the telemetry says so AND the image really is a dark card (conservative:
        # any doubt makes it a visible-world frame, which is then checked for placeholders).
        hidden = (cov_on or gr_on) and luma < 45.0
        res.append({"i": r["i"], "t": r["t"], "state": r["state"], "digit": r["digit"], "hidden": hidden, "cyan": cyan / n, "mag": mag / n, "luma": luma,
                    "cover": r.get("cover"), "op": r.get("coverOpacity"), "latch": r.get("latch"), "gr": r.get("getReady"), "ov": r.get("introOverlay"),
                    "ready": r.get("readiness", {}), "dt": r.get("dt")})
    lines = []

    def L(s):
        lines.append(s)
        print(s)
    L(f"frames analysed: {len(res)} of {len(shots)} shots; GO at t={go_t}")
    hid = [x for x in res if x["hidden"]]
    vis = [x for x in res if not x["hidden"]]
    L(f"covered/hidden frames: {len(hid)}   visible-world frames: {len(vis)}")
    if hid:
        ml = [x["luma"] for x in hid]
        L(f"covered frames mean luma: avg {sum(ml)/len(ml):.1f} max {max(ml):.1f}  (ink rgb(20,10,4) = luma ~12; GET READY plate/digits add to it)")
        L(f"covered frames cyan max {max(x['cyan'] for x in hid)*100:.3f}%  magenta max {max(x['mag'] for x in hid)*100:.3f}%  (UI-only baseline)")
    first_vis = vis[0] if vis else None
    # "Revealed" frames: visible-world frames from the first one that comes AFTER the last covered frame (the world is on show for good).
    last_hid_i = max((x["i"] for x in hid), default=-1)
    revealed = [x for x in vis if x["i"] > last_hid_i]
    early_vis = [x for x in vis if x["i"] <= last_hid_i]
    if first_vis:
        L(f"first visible-world frame: #{first_vis['i']} t={first_vis['t']:.2f}s state={first_vis['state']} digit='{first_vis['digit']}' cover={first_vis['cover']}/{first_vis['op']} latch={first_vis['latch']}")
    if early_vis:
        L(f"WARNING {len(early_vis)} visible-world frame(s) BEFORE the last covered frame (the world flashed): " + ", ".join(f"#{x['i']}@{x['t']:.2f}" for x in early_vis[:12]))
    worst = max(vis, key=lambda x: max(x["cyan"], x["mag"]), default=None)
    bad = [x for x in vis if x["cyan"] > THRESHOLD or x["mag"] > THRESHOLD]
    if worst:
        L(f"visible frames: worst cyan {max(x['cyan'] for x in vis)*100:.3f}%  worst magenta {max(x['mag'] for x in vis)*100:.3f}%  (threshold {THRESHOLD*100:.1f}%)")
    for x in bad[:20]:
        L(f"  PLACEHOLDER frame #{x['i']} t={x['t']:.2f}s state={x['state']} digit='{x['digit']}' cyan={x['cyan']*100:.2f}% magenta={x['mag']*100:.2f}% cover={x['cover']}/{x['op']}")
    ready_rows = [x for x in res if isinstance(x["ready"], dict) and x["ready"].get("present")]
    if ready_rows:
        rd = next((x for x in ready_rows if x["ready"].get("ready")), None)
        L(f"readiness: driver present; first frame reporting ready: " + (f"#{rd['i']} t={rd['t']:.2f}s last gate={rd['ready'].get('last')} at={rd['ready'].get('at')} timedOut={rd['ready'].get('timedOut')}" if rd else "NEVER ready in the recording"))
    lat = next((x for x in res if x["latch"] == "1"), None)
    if lat:
        L(f"reveal latch set at frame #{lat['i']} t={lat['t']:.2f}s state={lat['state']} digit='{lat['digit']}'")
    worst_dt = max(((x["dt"] or 0), x["i"]) for x in res) if res else (0, 0)
    L(f"largest frame time in the recording: {worst_dt[0]*1000:.0f} ms (frame #{worst_dt[1]})")
    ok = not bad and not early_vis
    L("RESULT: " + ("PASS (no placeholder frame ever visible, no world flash before the cover)" if ok else "FAIL"))
    open(os.path.join(out, "report.txt"), "w", encoding="utf-8").write("\n".join(lines + [""] + LOG))
    # ---- contact sheets
    def sheet(sel, name, cols):
        if not sel:
            return
        tw, th = 160, 90
        rowsn = (len(sel) + cols - 1) // cols
        im = Image.new("RGB", (cols * tw, rowsn * (th + 14)), (30, 30, 30))
        d = ImageDraw.Draw(im)
        for k, x in enumerate(sel):
            p = os.path.join(out, "f%04d.png" % x["i"])
            t = Image.open(p).convert("RGB").resize((tw, th))
            cx, cy = (k % cols) * tw, (k // cols) * (th + 14)
            im.paste(t, (cx, cy + 14))
            tag = f"#{x['i']} {x['t']:.1f}s {x['state'].replace('+intro','+i')} {x['digit']}"
            col = (255, 90, 90) if (x["cyan"] > THRESHOLD or x["mag"] > THRESHOLD) else ((120, 220, 120) if x["hidden"] else (230, 230, 230))
            d.text((cx + 2, cy + 1), tag[:26], fill=col)
        im.save(os.path.join(out, name))
    step = max(1, len(res) // 96)
    sheet(res[::step], "contact_sheet.png", 12)
    if lat or first_vis:
        pivot = (lat or first_vis)["i"]
        sel = [x for x in res if pivot - 8 <= x["i"] <= pivot + 15]
        sheet(sel, "reveal_zoom.png", 8)
    return ok


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        raise SystemExit(__doc__)
    out = os.path.abspath(args[0])
    scratch = os.environ.get("SCRATCH", os.path.join(os.environ.get("TEMP", "."), "arena_reveal_scratch"))
    os.makedirs(scratch, exist_ok=True)
    if "--playagain" in sys.argv:
        bridge.init()
        with capture_common.perf_mode(say):
            ok = run_playagain(out)
        if not ok:
            say("RECORDER DID NOT FINISH"); sys.exit(2)
        say("=== round 1"); ok1 = analyze(os.path.join(out, "round1"))
        say("=== round 2 (after PLAY AGAIN: the cover must not return)"); ok2 = analyze(os.path.join(out, "round2"))
        sys.exit(0 if ok1 and ok2 else 1)
    if "--analyze-only" in sys.argv:
        sys.exit(0 if analyze(out) else 1)
    bridge.init()
    if "--cold" in sys.argv:
        cold_restart(scratch)
    prev_level = None
    try:
        with capture_common.perf_mode(say):
            if "--mobile" in sys.argv:
                prev_level = quality("get")
                say("quality: was", prev_level, "->", quality("mobile"))
            else:
                say("quality:", quality("get"))
            if not run_pass(out):
                say("RECORDER DID NOT FINISH")
                sys.exit(2)
    finally:
        if prev_level:
            say("quality restored:", quality(prev_level))
        try:
            bridge.call("editor-application-set-state", {"isPlaying": False})
        except Exception:
            pass
    sys.exit(0 if analyze(out) else 1)


main()
