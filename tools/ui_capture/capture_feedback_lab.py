"""Ability-feedback evidence: opens AbilityLab.unity, enters Play, equips loadouts and
screenshots hold / cast / impact / aftermath for every active ability and state.

Usage (Editor open, bridge up, not in Play mode): python tools/ui_capture/capture_feedback_lab.py [out_dir] [--only a,b]
  --only <ability>[,<ability>]  runs only those abilities' shots plus the 00_rest frame. Names: steal, stun, shove, slow
                                (loadout A), speed, jump, shield, hide (loadout B), peck (C), hunt (D, Assassin).
                                Without the flag the whole run is unchanged.
Every ability's sequence is also probed for particle counts (CluckWars.EditorTools.ParticleProbe): the peaks per ability,
per chicken (live particles, ParticleSystem count) and for the whole scene, are written to <out_dir>/particles.txt.
Note: Fusion tick timers ignore Time.timeScale, so the TimeScale calls only slow the visuals; the
ability timings in capture.log are real-time. Shoot at timeScale 1 when frame timing matters."""
import sys, os, time, json
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge

ONLY = None
_args = sys.argv[1:]
if "--only" in _args:
    _i = _args.index("--only")
    if _i + 1 >= len(_args):
        sys.exit("--only needs a value, e.g. --only steal")
    ONLY = {n.strip().lower() for n in _args[_i + 1].split(",") if n.strip()}
    _bad = ONLY - {"steal", "stun", "shove", "slow", "speed", "jump", "shield", "hide", "peck", "hunt"}
    if _bad:
        sys.exit("unknown --only ability: " + ",".join(sorted(_bad)))
    del _args[_i:_i + 2]


def want(*names):
    """True when the full run is on, or any of the named abilities was asked for with --only."""
    return ONLY is None or any(n in ONLY for n in names)


OUT = os.path.abspath(_args[0]) if len(_args) > 0 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "Captures", "feedback-lab")
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using System.Linq; using System.Collections.Generic; using System.Reflection; using System.Globalization;
using CluckWars.Gameplay; using CluckWars.Abilities; using CluckWars.Input; using CluckWars.AbilityLab; using Fusion;
public class FB {
  static AbilityLabBootstrapper Lab() { return Object.FindFirstObjectByType<AbilityLabBootstrapper>(); }
  static ChickenController Local() { return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k.HasInputAuthority); }
  static IEnumerable<ChickenController> Others() { var l = Local(); return ChickenController.ActiveControllers.Where(k => k != null && k != l && k.Object != null && k.Object.IsValid); }
  public static string Open() { UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Game/Scenes/AbilityLab.unity", UnityEditor.SceneManagement.OpenSceneMode.Single); return "opened"; }
  public static string Play() { UnityEditor.EditorApplication.isPlaying = true; return "play"; }
  static object GV(out System.Reflection.PropertyInfo p) { var t = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView"); p = t.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); return UnityEditor.EditorWindow.GetWindow(t); }
  public static string Res(string w, string h) { PropertyInfo p; var gv = GV(out p);
    if (w == "0") { int idx = UnityEditor.SessionState.GetInt("fb_gv_idx", -1); if (idx >= 0) p.SetValue(gv, idx); return "res restored idx=" + idx; }
    UnityEditor.SessionState.SetInt("fb_gv_idx", (int)p.GetValue(gv));
    UnityEditor.PlayModeWindow.SetCustomRenderingResolution(uint.Parse(w), uint.Parse(h), "capture"); return "res " + w + "x" + h; }
  public static string Stop() { UnityEditor.EditorApplication.isPlaying = false; return "stop"; }
  public static string State() { var lab = Lab(); var l = Local(); return "playing=" + Application.isPlaying + " lab=" + (lab != null) + " player=" + (l != null) + (l != null ? " class=" + l.Class + " ctrl=" + l.CurrentControlState : "") + " others=" + Others().Count(); }
  public static string Loadout(string a, string b, string c, string d) { var lab = Lab(); lab.IgnoreLegality = true; lab.DummyResetSeconds = 120f;
    var picks = new List<AbilityBaseSO>(); foreach (var n in new[]{a,b,c,d}) picks.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<AbilityBaseSO>("Assets/_Game/Data/Abilities/" + n + ".asset"));
    lab.ApplyLoadout(null, picks); var l = Local(); return "slots=" + string.Join(",", Enumerable.Range(0,4).Select(i => l.Abilities.GetSlot(i) != null ? l.Abilities.GetSlot(i).DisplayName : "-")); }
  static FieldInfo F(string n) { return typeof(TouchControlsController).GetField(n, BindingFlags.NonPublic|BindingFlags.Instance); }
  public static string Hold(string slot) { int s = int.Parse(slot); var h = TouchControlsController.Instance; if (h == null) return "no touch hud"; ((bool[])F("_pressed").GetValue(h))[s] = true; ((bool[])F("_held").GetValue(h))[s] = true; return "hold " + s; }
  public static string Release(string slot) { int s = int.Parse(slot); var h = TouchControlsController.Instance; if (h == null) return "no touch hud"; ((bool[])F("_held").GetValue(h))[s] = false; return "release " + s; }
  public static string Cast(string slot) { var l = Local(); return "cast=" + l.Abilities.BotTryActivate(int.Parse(slot)); }
  public static string TimeScale(string v) { Time.timeScale = float.Parse(v, CultureInfo.InvariantCulture); return "ts=" + Time.timeScale; }
  public static string Stick(string x, string y) { var h = TouchControlsController.Instance; var ci = CultureInfo.InvariantCulture; F("_movement").SetValue(h, new Vector2(float.Parse(x, ci), float.Parse(y, ci))); return "stick " + x + "," + y; }
  public static string RespawnAs(string cls) { Lab().RespawnPlayerAs((ChickenClass)System.Enum.Parse(typeof(ChickenClass), cls)); return "respawn " + cls; }
  public static string ResetDummies() { foreach (var d in Lab().Dummies) d.ResetNow("capture"); return "reset"; }
  public static string ClearCooldowns() { var l = Local(); for (int i = 0; i < 4; i++) l.Abilities.TriggerCooldown(i, 0.01f); return "cd cleared"; }
  public static string ProbeBegin(string tag) { return CluckWars.EditorTools.ParticleProbe.Begin(tag); }
  public static string ProbeEnd(string tag) { return CluckWars.EditorTools.ParticleProbe.End(); }
  public static string Info() { var l = Local(); if (l == null) return "no local"; var a = l.Abilities;
    string self = "SELF ctrl=" + l.CurrentControlState + " stunned=" + l.IsStunned + " opacity=" + l.VisualOpacity.ToString("0.00") + " stealProof=" + l.IsStealProof + " cargo=" + l.Cargo.Cargo.ToString("0.0")
      + " active=" + string.Join("", Enumerable.Range(0,4).Select(i => a.IsSlotActive(i) ? "1" : "0")) + " cd=" + string.Join("/", Enumerable.Range(0,4).Select(i => a.CooldownRemaining(i).ToString("0.0"))) + " charging=" + a.ChargingSlot + " lastHits=" + a.LastCastHitCount;
    string oth = string.Join(" | ", Others().Select(c => "DUMMY ctrl=" + c.CurrentControlState + " stunned=" + c.IsStunned + " cargo=" + c.Cargo.Cargo.ToString("0.0") + " dist=" + Vector3.Distance(c.transform.position, l.transform.position).ToString("0.0")));
    return self + " || " + oth + " ts=" + Time.timeScale; }
}"""
SIG = {"Res": ["w", "h"], "RespawnAs": ["cls"], "Loadout": ["a", "b", "c", "d"], "Hold": ["slot"], "Release": ["slot"], "Cast": ["slot"], "TimeScale": ["v"], "Stick": ["x", "y"], "ProbeBegin": ["tag"], "ProbeEnd": ["tag"]}

LOG = open(os.path.join(OUT, "capture.log"), "a", encoding="utf-8")
PEAKS = {}   # ability tag -> the ParticleProbe line for its sequence (written to particles.txt)


def log(*a):
    s = " ".join(str(x) for x in a)
    print(s)
    LOG.write(s + "\n")
    LOG.flush()


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "FB", "methodName": method, "parameters": params}, timeout=120)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:600]


def shot(name):
    p = bridge.shot("_fb_" + name)
    if p:
        dest = os.path.join(OUT, name + ".png")
        os.replace(p, dest)
        log("  shot", name, "|", cs("Info"))
    else:
        log("  SHOT FAILED", name)


def probe_begin(tag):
    cs("ProbeBegin", tag)


def probe_end(tag):
    PEAKS[tag] = str(cs("ProbeEnd", tag))
    log("  particles", tag, "|", PEAKS[tag])


def write_particles():
    with open(os.path.join(OUT, "particles.txt"), "w", encoding="utf-8") as f:
        f.write("# live particle peaks per ability sequence (ParticleSystem.particleCount, sampled every Editor update)\n")
        f.write("# caps: <= 64 live and <= 7 ParticleSystems per chicken, <= 400 for the arena (FeedbackTuning)\n")
        for tag, line in PEAKS.items():
            f.write(tag + ": " + line + "\n")


def ability(slot, tag, after=((0.5, "3_impact"), (1.5, "4_after")), ts="0.3"):
    log("==", tag, "slot", slot)
    probe_begin(tag)
    cs("TimeScale", ts)
    log(" ", cs("Hold", str(slot)))
    time.sleep(0.7)
    shot(tag + "_1_hold")
    log(" ", cs("Release", str(slot)))
    shot(tag + "_2_cast")
    t0 = time.time()
    for delay, suffix in after:
        while time.time() - t0 < delay:
            time.sleep(0.05)
        shot(tag + "_" + suffix)
    probe_end(tag)
    cs("TimeScale", "1")
    time.sleep(0.5)
    log(" ", cs("ResetDummies"))
    log(" ", cs("ClearCooldowns"))
    time.sleep(1.5)


def stun_then_slow(tag):
    """Both control states on the same dummies at once (the world badge stack should show STUN + SLOW rows)."""
    log("==", tag, "slow then stun")
    log(" ", cs("Hold", "3"))
    time.sleep(0.4)
    log(" ", cs("Release", "3"))
    time.sleep(0.4)
    log(" ", cs("Hold", "1"))
    time.sleep(0.4)
    log(" ", cs("Release", "1"))
    time.sleep(0.25)
    shot(tag)
    time.sleep(0.4)
    shot(tag + "_b")
    log(" ", cs("ResetDummies"))
    log(" ", cs("ClearCooldowns"))
    time.sleep(1.5)


def wait_player(cls):
    for _ in range(60):
        s = str(cs("State"))
        if "player=True" in s and "class=" + cls in s:
            break
        time.sleep(1)
    time.sleep(2)


def main():
    bridge.init()
    if os.environ.get("CAPTURE_PHONE") == "1":
        log(cs("Res", "2400", "1080"))   # phone-size Game View for badge / glyph size checks
    log("state:", cs("State"))
    if "playing=True" not in str(cs("State")):
        log(cs("Open"))
        time.sleep(2)
        log(cs("Play"))
    for _ in range(90):
        s = str(cs("State"))
        if "player=True" in s and "others=" in s and not s.endswith("others=0"):
            break
        time.sleep(1)
    log("ready:", cs("State"))
    time.sleep(3)
    shot("00_rest")
    # Loadout A: the four targeted moves (every rival in range)
    if want("steal", "stun", "shove", "slow"):
        log(cs("Loadout", "Snatch", "WingSlam", "CluckShock", "FeatherAura"))
        time.sleep(2)
        shot("01_rest_loadoutA")
        if want("steal"):
            ability(0, "steal", after=((0.4, "3_impact"), (1.2, "4_after")))
        if want("stun"):
            ability(1, "stun", after=((0.4, "3_impact"), (1.5, "4_after"), (3.5, "5_late")))
        if want("shove"):
            ability(2, "shove", after=((0.3, "3_impact"), (0.8, "4_after"), (2.0, "5_late")))
        if want("slow"):
            ability(3, "slow", after=((0.6, "3_impact"), (3.0, "4_after"), (7.0, "5_late")))
        if want("stun"):
            stun_then_slow("badges_stun_slow")
    # Loadout B: the self moves
    if want("speed", "jump", "shield", "hide"):
        log(cs("Loadout", "SpeedBurst", "Shadowstep", "TurtleMode", "Invisibility"))
        time.sleep(2)
        shot("02_rest_loadoutB")
        if want("speed"):
            cs("Stick", "1", "0")
            time.sleep(0.3)
            ability(0, "speed", after=((0.6, "3_impact"), (2.5, "4_after")))
            cs("Stick", "0", "0")
        if want("jump"):
            ability(1, "jump", after=((0.25, "3_impact"), (1.0, "4_after")))
        if want("shield"):
            cs("Stick", "0", "1")
            time.sleep(0.2)
            # The Shield is a 1 s walk then a 2 s egg then the hatch (3 s run): frames land on walk, close, idle egg, jitter, hatch, over.
            ability(2, "shield", after=((0.45, "3_walk"), (1.05, "4_walkend"), (2.0, "5_egg"), (2.75, "5b_egg_jitter"),
                                        (3.1, "6_eggend"), (3.3, "6b_hatch"), (4.5, "7_over")))
            cs("Stick", "0", "0")
        if want("hide"):
            ability(3, "hide", after=((0.8, "3_hidden"), (3.5, "4_mid"), (7.5, "5_end")))
    # Loadout C: Peck on the Warrior (Death Mark needs the Assassin; separate pass)
    if want("peck"):
        log(cs("Loadout", "Peck", "WingSlam", "Snatch", "SpeedBurst"))
        time.sleep(2)
        ability(0, "peck", after=((0.5, "3_impact"), (1.5, "4_after")))
    # Loadout D: the Assassin hunt (Death Mark). Paw marker + HUNT! pop on the hunter side.
    if want("hunt"):
        log(cs("RespawnAs", "Assassin"))
        wait_player("Assassin")
        log(cs("Loadout", "MarkKill", "WingSlam", "Shadowstep", "Invisibility"))
        time.sleep(2)
        cs("TimeScale", "1")
        probe_begin("hunt")
        log(" ", cs("Hold", "0"))
        time.sleep(0.7)
        shot("hunt_1_hold")
        log(" ", cs("Release", "0"))
        shot("hunt_2_cast")
        time.sleep(3)
        shot("hunt_3_live")
        probe_end("hunt")
        log(cs("ResetDummies"))
        log(cs("RespawnAs", "Warrior"))
        wait_player("Warrior")
        cs("TimeScale", "1")
    write_particles()
    log("done:", cs("Info"))


try:
    main()
finally:
    cs("TimeScale", "1")
    if os.environ.get("CAPTURE_PHONE") == "1":
        cs("Res", "0", "0")
    LOG.close()
