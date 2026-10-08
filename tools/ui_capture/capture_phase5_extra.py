"""Phase 5 chunk 2 captures: solo PLAY AGAIN -> GET READY, the multiplayer waiting room, the session-end screen.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_phase5_extra.py <output_dir> [solo|host|all]

solo  Solo START, the round won by pushing the local base to the goal, PLAY AGAIN: the Game view is shot while the
      state is Starting (solo_playagain_getready.png - GET READY, no waiting room), then a forced
      session end (PhotonCloudTimeout via MatchOverlaysController.HandleShutdown) is rendered off-screen
      (session_end__<size>.png) - it must read COOP CLOSED / "Lost the signal to the barn.", never the enum name.
cpuwin  A real solo round the CPUs win while the local bird idles (solo_cpu_win_real.png).
host  HOST GAME -> START: a real Shared-mode session with one player sits in the waiting room; the overlay panel is
      rendered off-screen at desktop and phone size (mp_waiting_room__<size>.png), again with a forced 12-letter
      invite code (mp_waiting_room_longcode__<size>.png), then START from the waiting room and the HUD
      (host_hud__<size>.png: no leaderboard rows for the unclaimed corners). Needs Photon cloud + UGS.
Leaves Play mode at the end of each session.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

ARGS = [a for a in sys.argv[1:]]
OUT = os.path.abspath(ARGS[0]) if ARGS else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
WHAT = ARGS[1] if len(ARGS) > 1 else "all"
os.makedirs(OUT, exist_ok=True)
OUTCS = OUT.replace(chr(92), "/")

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using System.IO; using CluckWars.Gameplay; using CluckWars.UI; using Fusion;
public class P5 {
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
  public static string Win() { var c = ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k.HasInputAuthority);
    if (c == null) return "no local chicken"; var b = PlayerBase.ActiveBases.FirstOrDefault(x => x != null && x.CornerIndex == c.HomeCornerIndex); if (b == null) return "no base";
    b.FoodTotal = GameManager.Instance.FoodTargetToWin + 2f; return "pushed corner " + c.HomeCornerIndex; }
  public static string PlayAgainAndProbe() { var r = Press("MePlayAgainBtn"); return r + " -> " + State(); }
  public static string Overlay() { var d = Doc("LobbyOverlay"); if (d == null) return "no overlay"; var r = d.rootVisualElement;
    System.Func<string,string> vis = n => { var e = r.Q(n); return e == null ? "null" : e.resolvedStyle.display.ToString(); };
    return "lobby=" + vis("LobbyOverlay") + " getready=" + vis("IntroGetReady") + " intro=" + vis("IntroOverlay") + " session=" + vis("SessionEndOverlay")
      + " | count=" + r.Q<Label>("LobbyStatusCount").text + " status=" + r.Q<Label>("LobbyStatusText").text
      + " | seats=" + string.Join(" / ", r.Query<VisualElement>(className: "cw-seat").ToList().Select(s => (s.ClassListContains("cw-seat--empty") ? "empty" : s.Q<Label>(className: "cw-seat__name").text + "[" + s.Q<Label>(className: "cw-seat__tag").text + "] " + s.Q<Label>(className: "cw-seat__class").text)))
      + " | pill bg=" + (r.Q(className: "cw-seat__state") != null ? r.Q(className: "cw-seat__state").resolvedStyle.backgroundColor.ToString() : "-")
      + " label=" + (r.Q<Label>(className: "cw-seat__state-label") != null ? r.Q<Label>(className: "cw-seat__state-label").resolvedStyle.color.ToString() : "-")
      + " startBtnImg=" + (r.Q("LobbyStartBtn") != null ? r.Q("LobbyStartBtn").resolvedStyle.backgroundImage.ToString() : "-"); }
  // Chunk 5: force a long invite code into the waiting room's tile row (the fit / wrap check), and read the HUD rows.
  public static string ForceCode(string code) { var ctrl = Object.FindFirstObjectByType<MatchOverlaysController>(); if (ctrl == null) return "no controller";
    Zenject.ProjectContext.Instance.Container.Resolve<CluckWars.Services.ISessionSelectionService>().SessionName = code;   // the room re-fills from it every poll
    typeof(MatchOverlaysController).GetMethod("SetCodeTiles", BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ctrl, new object[] { code }); return "code " + code; }
  public static string HudRows() { var d = Doc("LbRow0"); if (d == null) return "no hud"; var r = d.rootVisualElement;
    return string.Join(" ", Enumerable.Range(0, 4).Select(i => { var e = r.Q("LbRow" + i); return "row" + i + "=" + (e == null ? "null" : e.resolvedStyle.display.ToString()); })); }
  public static string Session() { var d = Doc("SessionEndOverlay"); var r = d.rootVisualElement; return r.Q<Label>("SessionEndTitle").text + " | " + r.Q<Label>("SessionEndReason").text + " | " + r.Q<Label>("SessionEndCountdown").text; }
  public static string ForceShutdown() { var ctrl = Object.FindFirstObjectByType<MatchOverlaysController>(); if (ctrl == null) return "no controller";
    typeof(MatchOverlaysController).GetField("_disconnectReturnDelay", BindingFlags.NonPublic|BindingFlags.Instance).SetValue(ctrl, 30f); // time for both sizes
    typeof(MatchOverlaysController).GetMethod("HandleShutdown", BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ctrl, new object[] { ShutdownReason.PhotonCloudTimeout });
    return "forced PhotonCloudTimeout"; }
  public static string Setup(string el, string w, string h) { var ps = Doc(el).panelSettings; var rt = new RenderTexture(int.Parse(w), int.Parse(h), 24, RenderTextureFormat.ARGB32); rt.Create();
    ps.targetTexture = rt; ps.clearColor = true; ps.colorClearValue = new Color(0.12f, 0.16f, 0.1f, 1f); return "rt"; }
  public static string Grab(string el, string path) { var rt = Doc(el).panelSettings.targetTexture; RenderTexture.active = rt;
    var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false); t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); t.Apply(); RenderTexture.active = null;
    File.WriteAllBytes(path, t.EncodeToPNG()); return "saved " + path; }
  public static string Restore(string el) { var ps = Doc(el).panelSettings; var rt = ps.targetTexture; ps.targetTexture = null; ps.clearColor = false; ps.colorClearValue = Color.clear; if (rt != null) rt.Release(); return "restored"; }
}"""

SIG = {"Setup": ["el", "w", "h"], "Grab": ["el", "path"], "Press": ["name"], "Restore": ["el"], "ForceCode": ["code"]}
SIZES = [("desktop", "1920", "1080"), ("phone", "2424", "1080")]


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    r = bridge.text(bridge.call("script-execute", {"csharpCode": CS, "className": "P5", "methodName": method, "parameters": params}, timeout=90))
    try:
        return json.loads(r)["result"]["value"]
    except Exception:
        return r[:400]


def wait(prefixes, tries=90):
    s = ""
    for _ in range(tries):
        s = cs("State")
        if s.startswith(prefixes):
            return s
        time.sleep(1)
    raise SystemExit("timed out waiting for " + str(prefixes) + " (last: " + s + ")")


def render(el, name):
    for tag, w, h in SIZES:
        cs("Setup", el, w, h); time.sleep(1.0)
        print(" ", cs("Grab", el, f"{OUTCS}/{name}__{tag}.png")); cs("Restore", el); time.sleep(0.3)


def to_coop(mode_btn):
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(60):
        if cs("Press", mode_btn).startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(0.8)
    print(cs("Press", "NextBtn")); time.sleep(1.0)
    print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5)


def stop():
    bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


def solo():
    print("== solo PLAY AGAIN")
    to_coop("SoloBtn")
    print(cs("Press", "StartBtn"))
    wait(("Active",)); time.sleep(4.5)
    print(cs("Win")); wait(("Ended",)); time.sleep(1.5)
    print(cs("PlayAgainAndProbe"))
    p = bridge.shot("solo_playagain_getready")
    print("  state after shot:", cs("State"), "|", cs("Overlay"))
    if p:
        os.replace(p, os.path.join(OUT, "solo_playagain_getready.png")); print("  saved solo_playagain_getready.png")
    wait(("Active",)); time.sleep(3)
    print("  round 2 running:", cs("State"))
    print(cs("ForceShutdown")); time.sleep(0.8)
    print("  session end:", cs("Session"))
    render("SessionEndOverlay", "session_end")
    stop()  # before the (stretched) return delay runs out: a scene load under a live runner leaves a stale manager


def host():
    print("== host waiting room")
    to_coop("HostBtn")
    time.sleep(3)  # the host pre-creates its UGS lobby (join code tiles)
    print(cs("Press", "StartBtn"))
    time.sleep(10)
    wait(("WaitingForPlayers",), tries=60); time.sleep(2.5)
    print("  ", cs("Overlay"))
    render("LobbyOverlay", "mp_waiting_room")
    # Chunk 5: a 12-letter code must shrink (then wrap) its tiles instead of running under SHARE / COPY.
    print("  ", cs("ForceCode", "CLUCK-LAN-XL")); time.sleep(0.5)
    render("LobbyOverlay", "mp_waiting_room_longcode")
    # Chunk 5: START from the waiting room; a 1-player host session's HUD shows no rows for the empty corners.
    # (GET READY + countdown run first: wait without polling the Editor through them.)
    print(cs("Press", "LobbyStartBtn")); time.sleep(12)
    wait(("Active",), tries=30); time.sleep(2)
    print("  hud:", cs("HudRows"))
    render("LbRow0", "host_hud")
    stop()


def cpuwin():
    """A real solo round: the local bird idles, the CPUs play the full 45 s; the Game view is shot on the end screen."""
    print("== solo real CPU win (idle 45 s)")
    to_coop("SoloBtn")
    print(cs("Press", "StartBtn"))
    wait(("Active",)); time.sleep(4.5)
    wait(("Ended",), tries=80); time.sleep(3.0)
    p = bridge.shot("solo_cpu_win_real")
    if p:
        os.replace(p, os.path.join(OUT, "solo_cpu_win_real.png")); print("  saved solo_cpu_win_real.png")
    stop()


bridge.init()
if WHAT == "cpuwin":
    cpuwin()
if WHAT in ("solo", "all"):
    solo()
if WHAT in ("host", "all"):
    host()
