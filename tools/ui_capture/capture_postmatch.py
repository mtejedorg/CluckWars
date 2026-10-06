"""Render the post-match overlay (PLAY AGAIN / BACK TO LOBBY) off-screen at desktop, phone and tablet sizes.

Usage (Editor open, Bootstrap.unity loaded and the only open scene, not in Play mode):
    python tools/ui_capture/capture_postmatch.py <output_dir>

Enters Play mode, walks Main -> Class -> Loadout -> Lobby -> START MATCH, forces a win by pushing the first
claimed base's FoodTotal over the goal, waits for MatchState.Ended, then sets the match-overlay
PanelSettings.targetTexture to a RenderTexture per size, saves a PNG and restores it. Leaves Play mode.
Element names it drives: SoloBtn, NextBtn, ReadyBtn, StartBtn, MePlayAgainBtn.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(OUT, exist_ok=True)
OUTCS = OUT.replace(chr(92), "/")

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using System.IO; using CluckWars.Gameplay;
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
  public static string Win() { var b = PlayerBase.ActiveBases.FirstOrDefault(x => x != null && x.IsClaimed); if (b == null) return "no claimed base"; b.FoodTotal = 999f; return "ok"; }
  public static string Setup(string w, string h) { var ps = Doc("MePlayAgainBtn").panelSettings; var rt = new RenderTexture(int.Parse(w), int.Parse(h), 24, RenderTextureFormat.ARGB32); rt.Create();
    ps.targetTexture = rt; ps.clearColor = true; ps.colorClearValue = new Color(0.12f, 0.16f, 0.1f, 1f); return "rt"; }
  public static string Grab(string path) { var rt = Doc("MePlayAgainBtn").panelSettings.targetTexture; RenderTexture.active = rt;
    var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false); t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); t.Apply(); RenderTexture.active = null;
    File.WriteAllBytes(path, t.EncodeToPNG()); return "saved"; }
  public static string Restore() { var ps = Doc("MePlayAgainBtn").panelSettings; var rt = ps.targetTexture; ps.targetTexture = null; ps.clearColor = false; if (rt != null) rt.Release(); return "restored"; }
}"""

def cs(method, *args):
    sig = {"Setup": ["w", "h"], "Grab": ["path"], "Press": ["name"]}.get(method, [])
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(sig, args)]
    r = bridge.text(bridge.call("script-execute", {"csharpCode": CS, "className": "Pm", "methodName": method, "parameters": params}, timeout=90))
    try:
        return json.loads(r)["result"]["value"]
    except Exception:
        return r[:200]

def wait(method_value, tries=60):
    for _ in range(tries):
        s = cs("State")
        if s.startswith(method_value):
            return s
        time.sleep(1)
    raise SystemExit("timed out waiting for " + method_value + " (last: " + s + ")")

bridge.init()
bridge.call("editor-application-set-state", {"isPlaying": True})
for _ in range(60):
    if cs("Press", "SoloBtn").startswith("clicked"):
        break
    time.sleep(1)
time.sleep(0.5)
print(cs("Press", "NextBtn")); time.sleep(0.9)  # page transitions settle
print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(0.8)
print(cs("Press", "StartBtn"))
wait("Active"); time.sleep(5)
print(cs("Win")); wait("Ended"); time.sleep(1)
for tag, w, h in [("desktop_1920x1080", "1920", "1080"), ("phone_2424x1080", "2424", "1080"), ("tablet_2048x1536", "2048", "1536")]:
    cs("Setup", w, h); time.sleep(0.8)
    print(cs("Grab", f"{OUTCS}/postmatch__{tag}.png")); cs("Restore"); time.sleep(0.2)
bridge.call("editor-application-set-state", {"isPlaying": False})
