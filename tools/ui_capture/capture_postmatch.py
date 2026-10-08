"""Render the post-match overlay off-screen at desktop, phone and tablet sizes, for several outcomes.

Usage (Editor open, Bootstrap.unity loaded and the only open scene, not in Play mode):
    python tools/ui_capture/capture_postmatch.py <output_dir> [static|live|both]

Per mode (Performance Mode ON = static hero renders, OFF = live MenuChickenStage podium) it enters Play
mode, walks Main -> Class -> Loadout -> Lobby -> START MATCH and then, in one session, plays these
rounds (PLAY AGAIN -> GET READY between them):
    human   the local player's base is pushed to the goal (real EvaluateWinCondition ends it), no KOs
    cpu     a bot's base is pushed to the goal, two knockouts credited (KO column shown)
    nowin   GameManager.EndMatch(None, -1) via reflection: the "MATCH ENDED" / no-winner state
    two     two bots despawned first, so only 2 chickens stand (hosted-2-player shape: 3rd step hidden)
Each round saves postmatch__<mode>__<round>__<size>.png. Live mode also logs Camera / RenderTexture
counts before the round, on the post-match screen and after PLAY AGAIN (leak check).
Element names it drives: SoloBtn, NextBtn, ReadyBtn, StartBtn, MePlayAgainBtn.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
MODES = {"static": [True], "live": [False], "both": [True, False]}[sys.argv[2] if len(sys.argv) > 2 else "both"]
os.makedirs(OUT, exist_ok=True)
OUTCS = OUT.replace(chr(92), "/")

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using System.IO; using CluckWars.Gameplay; using Fusion;
public class Pm {
  static UIDocument Doc(string btn) { return Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None).FirstOrDefault(x => x.rootVisualElement != null && x.rootVisualElement.Q<Button>(btn) != null); }
  public static string Press(string name) { var d = Doc(name); if (d == null) return "NOBTN " + name; var b = d.rootVisualElement.Q<Button>(name);
    var c = typeof(Button).GetField("m_Clickable", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(b);
    ((System.Action)typeof(Clickable).GetField("clicked", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(c)).Invoke(); return "clicked " + name; }
  public static string Fill() { var r = Doc("ReadyBtn").rootVisualElement; int taps = 0;
    for (int i = 0; i < 14; i++) { var rb = r.Q<Button>("ReadyBtn"); if (rb != null && rb.enabledSelf) break;
      var card = r.Query<VisualElement>(className: "cw-ability-card").ToList().FirstOrDefault(c => !c.ClassListContains("cw-ability-card--picked")); if (card == null) break;
      using (var e = ClickEvent.GetPooled()) { e.target = card; card.SendEvent(e); } taps++; }
    return "taps=" + taps; }
  public static string State() { var gm = GameManager.Instance; return gm == null ? "nogm" : gm.State.ToString() + (gm.IsIntroActive ? "+intro" : ""); }
  public static string PerfGet() { return CluckWars.Settings.PlayerPreferences.PerformanceModeEnabled ? "1" : "0"; }
  public static string Perf(string on) { CluckWars.Settings.PlayerPreferences.PerformanceModeEnabled = on == "1"; return "perf=" + on; }
  static ChickenController Local() { return ChickenController.ActiveControllers.FirstOrDefault(c => c != null && c.Object != null && c.Object.IsValid && !c.IsDecoy && c.HasInputAuthority); }
  static ChickenController Bot(int skip) { return ChickenController.ActiveControllers.Where(c => c != null && c.Object != null && c.Object.IsValid && !c.IsDecoy && c.IsBot).Skip(skip).FirstOrDefault(); }
  public static string Win(string who) {
    var c = who == "human" ? Local() : Bot(0); if (c == null) return "no chicken for " + who;
    var b = PlayerBase.ActiveBases.FirstOrDefault(x => x != null && x.CornerIndex == c.HomeCornerIndex); if (b == null) return "no base";
    b.FoodTotal = GameManager.Instance.FoodTargetToWin + 2f;
    var o = PlayerBase.ActiveBases.Where(x => x != null && x != b).ToList();
    for (int i = 0; i < o.Count; i++) o[i].FoodTotal = ChickenController.ActiveControllers.Any(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k.HomeCornerIndex == o[i].CornerIndex) ? 6f + 9f * i : 0f;
    return "pushed corner " + c.HomeCornerIndex + " (" + c.Class + (c.IsBot ? " bot" : " you") + ")"; }
  public static string Kos() { var s = ChickenMatchStats.ActiveStats.Where(x => x != null && x.Object != null && x.Object.IsValid).ToList(); if (s.Count < 2) return "no stats"; s[0].Kills = 2; s[1].Kills = 1; return "kos set"; }
  public static string NoWin() { var gm = GameManager.Instance;
    foreach (var b in PlayerBase.ActiveBases) if (b != null) b.FoodTotal = 0f;
    typeof(GameManager).GetMethod("EndMatch", BindingFlags.NonPublic|BindingFlags.Instance).Invoke(gm, new object[] { PlayerRef.None, -1, 0f, "capture probe" }); return "ended, no winner"; }
  public static string DropTwo() { var r = Object.FindFirstObjectByType<NetworkRunner>(); int n = 0;
    for (int i = 0; i < 2; i++) { var b = Bot(0); if (b == null) break; r.Despawn(b.Object); n++; } return "despawned " + n; }
  public static string Count() { return "cams=" + Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length
      + " rts=" + Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t => t.name.StartsWith("MenuStageRT"))
      + " lights=" + Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length
      + " stageRoots=" + Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(t => t.name == "MenuChickenStage"); }
  public static string Setup(string w, string h) { var ps = Doc("MePlayAgainBtn").panelSettings; var rt = new RenderTexture(int.Parse(w), int.Parse(h), 24, RenderTextureFormat.ARGB32); rt.Create();
    ps.targetTexture = rt; ps.clearColor = true; ps.colorClearValue = new Color(0.12f, 0.16f, 0.1f, 1f); return "rt"; }
  public static string Grab(string path) { var rt = Doc("MePlayAgainBtn").panelSettings.targetTexture; RenderTexture.active = rt;
    var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false); t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); t.Apply(); RenderTexture.active = null;
    File.WriteAllBytes(path, t.EncodeToPNG()); return "saved"; }
  public static string Restore() { var ps = Doc("MePlayAgainBtn").panelSettings; var rt = ps.targetTexture; ps.targetTexture = null; ps.clearColor = false; if (rt != null) rt.Release(); return "restored"; }
  public static string Banner() { var r = Doc("MePlayAgainBtn").rootVisualElement; return r.Q<Label>("MeWinRibbonLabel").text + " | " + r.Q<Label>("MeWinSub").text + " | "
      + string.Join(",", r.Query<Label>(className: "cw-me-rowname").ToList().Select(l => l.text)) + " | noKo=" + r.Q("MeRows").ClassListContains("cw-me-rows--no-ko"); }
}"""

SIG = {"Setup": ["w", "h"], "Grab": ["path"], "Press": ["name"], "Perf": ["on"], "Win": ["who"]}


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    r = bridge.text(bridge.call("script-execute", {"csharpCode": CS, "className": "Pm", "methodName": method, "parameters": params}, timeout=90))
    try:
        return json.loads(r)["result"]["value"]
    except Exception:
        return r[:300]


def wait(prefix, tries=90):
    s = ""
    for _ in range(tries):
        s = cs("State")
        if s.startswith(prefix):
            return s
        time.sleep(1)
    raise SystemExit("timed out waiting for " + str(prefix) + " (last: " + s + ")")


SIZES = [("desktop_1920x1080", "1920", "1080"), ("phone_2424x1080", "2424", "1080"), ("tablet_2048x1536", "2048", "1536")]


def grab_all(mode, rnd):
    for tag, w, h in SIZES:
        cs("Setup", w, h); time.sleep(1.2)  # live stage re-sizes its textures to the new layout
        print(" ", tag, cs("Grab", f"{OUTCS}/postmatch__{mode}__{rnd}__{tag}.png")); cs("Restore"); time.sleep(0.3)


def round_(mode, rnd, first):
    if not first:
        # Solo PLAY AGAIN goes straight to GET READY (round 2, decision 3): no waiting room, no START.
        print(cs("Press", "MePlayAgainBtn")); wait(("Starting", "Active")); time.sleep(0.6)
        if mode == "live": print("  after PLAY AGAIN:", cs("Count"))
    wait("Active"); time.sleep(4.5)  # intro + a moment of play
    if mode == "live": print("  before end:", cs("Count"))
    if rnd == "human": print(cs("Win", "human"))
    elif rnd == "cpu": print(cs("Kos")); print(cs("Win", "cpu"))
    elif rnd == "nowin": print(cs("NoWin"))
    elif rnd == "two": print(cs("DropTwo")); time.sleep(0.5); print(cs("Win", "human"))
    wait("Ended"); time.sleep(1.5)
    print("  banner:", cs("Banner"))
    if mode == "live": print("  on post-match:", cs("Count"))
    grab_all(mode, rnd)


bridge.init()
original_perf = cs("PerfGet")
for perf in MODES:
    mode = "static" if perf else "live"
    print("== mode", mode, cs("Perf", "1" if perf else "0"))
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(60):
        if cs("Press", "SoloBtn").startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(0.5)
    print(cs("Press", "NextBtn")); time.sleep(0.9)  # page transitions settle
    print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(0.8)
    print(cs("Press", "StartBtn"))
    for i, rnd in enumerate(["human", "cpu", "nowin", "two"]):
        print("-- round", rnd)
        round_(mode, rnd, first=(i == 0))
    if mode == "live":
        print(cs("Press", "MePlayAgainBtn")); wait(("Starting", "Active")); time.sleep(0.8)
        print("  final after PLAY AGAIN:", cs("Count"))
    bridge.call("editor-application-set-state", {"isPlaying": False})
    time.sleep(4)
cs("Perf", original_perf)  # put the user's Performance Mode back
