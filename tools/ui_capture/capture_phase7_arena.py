"""Phase 7 chunk E: arena framing before/after captures + Mobile-tier frame-time / rendering stats in a real solo match.

    python tools/ui_capture/capture_phase7_arena.py <before|after>

Writes Captures/phase7/extra/arena/<phase>_*.png and prints a JSON stats line (also saved as <phase>_stats.json).
Captures are taken at the PC quality tier (what the earlier phase-7 captures used); the measurement switches to the
"Mobile" tier, samples, and switches back.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

PHASE = sys.argv[1] if len(sys.argv) > 1 else "before"
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "Captures", "phase7", "extra", "arena")
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using UnityEditor; using System.Linq; using System.Reflection; using System.Collections.Generic; using System.Diagnostics; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class PA {
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
  static ChickenController Local() { return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k.HasInputAuthority); }
  public static string Move(string x, string z) { var l = Local(); if (l == null) return "no local"; var ci = System.Globalization.CultureInfo.InvariantCulture;
    var pos = new Vector3(float.Parse(x, ci), l.transform.position.y, float.Parse(z, ci)); var nt = l.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else l.transform.position = pos; return "local -> " + pos; }
  public static string LocalPos() { var l = Local(); return l == null ? "no local" : l.transform.position.x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ";" + l.transform.position.z.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); }
  public static string FreezeBots() { string o = ""; foreach (var k in ChickenController.ActiveControllers) { if (k == null || k.HasInputAuthority) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot") && m.enabled) { m.enabled = false; o += m.GetType().Name + " "; } } return "froze " + o; }
  public static string CamShot(string w, string h, string path) {
    var cam = MatchCamera.Instance.Camera; int W = int.Parse(w), H = int.Parse(h);
    var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32); var old = cam.targetTexture; var oldA = cam.aspect; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); return "saved " + path; }

  // Each script-execute compiles a fresh assembly, so statics do not survive between calls: persist through SessionState.
  // The "Mobile" quality LEVEL is excluded from the Standalone platform, so in play mode in the Editor it cannot be selected by
  // SetQualityLevel. The Mobile level differs from PC in: render pipeline asset (Mobile_RPAsset), lodBias 1/2, skinWeights 2/4,
  // anisotropic 1/2 - apply those four at runtime instead, and put the PC values back on restore.
  public static string Quality(string mode) {
    string before = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline.name : "null";
    if (mode == "mobile") {
      var pc = QualitySettings.renderPipeline; SessionState.SetString("pa_pc_path", AssetDatabase.GetAssetPath(pc));
      var mob = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
      if (mob == null) return "Mobile_RPAsset not found";
      QualitySettings.renderPipeline = mob; QualitySettings.lodBias = 1f; QualitySettings.skinWeights = SkinWeights.TwoBones; QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
    } else if (mode == "restore") {
      var pc = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(SessionState.GetString("pa_pc_path", "Assets/Settings/PC_RPAsset.asset"));
      QualitySettings.renderPipeline = pc; QualitySettings.lodBias = 2f; QualitySettings.skinWeights = SkinWeights.FourBones; QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
    }
    return "mode=" + mode + " before=" + before + " now=" + QualitySettings.renderPipeline.name + " lodBias=" + QualitySettings.lodBias + " skin=" + QualitySettings.skinWeights; }

  // Per-editor-tick sampler: unscaled delta + UnityStats (last Game View frame).
  static List<float> _dt = new List<float>(); static List<float> _batches = new List<float>(), _setpass = new List<float>(), _tris = new List<float>(), _verts = new List<float>();
  static bool _on; static int _target; static double _last;
  static void Finish() { SessionState.SetString("pa_result", SampleResultLocal()); SessionState.SetString("pa_state", "done"); }
  static float Stat(string n) { var t = typeof(Editor).Assembly.GetType("UnityEditor.UnityStats"); var p = t.GetProperty(n, BindingFlags.Public|BindingFlags.Static|BindingFlags.NonPublic); return System.Convert.ToSingle(p.GetValue(null)); }
  static void Tick() { if (!_on) return; double now = EditorApplication.timeSinceStartup; if (_last > 0) _dt.Add((float)((now - _last) * 1000.0)); _last = now;
    _batches.Add(Stat("batches")); _setpass.Add(Stat("setPassCalls")); _tris.Add(Stat("triangles")); _verts.Add(Stat("vertices"));
    if (_dt.Count >= _target) { _on = false; EditorApplication.update -= Tick; Finish(); } }
  public static string StartSample(string n) { _dt.Clear(); _batches.Clear(); _setpass.Clear(); _tris.Clear(); _verts.Clear(); _target = int.Parse(n); _last = 0; _on = true; SessionState.SetString("pa_state", "running"); EditorApplication.update -= Tick; EditorApplication.update += Tick; return "sampling " + n; }
  public static string SampleProgress() { return SessionState.GetString("pa_state", "none"); }
  static string Sum(List<float> l) { if (l.Count == 0) return "n=0"; var s = l.OrderBy(x => x).ToList(); return "{\"mean\":" + l.Average().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ",\"median\":" + s[s.Count/2].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ",\"p95\":" + s[(int)(s.Count*0.95)].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "}"; }
  public static string SampleResult() { return SessionState.GetString("pa_result", "{}"); }
  static string SampleResultLocal() { return "{\"frames\":" + _dt.Count + ",\"editorTickMs\":" + Sum(_dt) + ",\"batches\":" + Sum(_batches) + ",\"setPass\":" + Sum(_setpass) + ",\"tris\":" + Sum(_tris) + ",\"verts\":" + Sum(_verts) + "}"; }

  // CPU time to submit one full camera render of the match camera (deterministic, not capped by Editor vsync / focus).
  public static string RenderBench(string n) { var cam = MatchCamera.Instance.Camera; int N = int.Parse(n); var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); var old = cam.targetTexture; cam.targetTexture = rt;
    for (int i = 0; i < 10; i++) cam.Render(); var ts = new List<double>(); var sw = new Stopwatch();
    for (int i = 0; i < N; i++) { sw.Restart(); cam.Render(); sw.Stop(); ts.Add(sw.Elapsed.TotalMilliseconds); }
    cam.targetTexture = old; rt.Release(); Object.DestroyImmediate(rt); var s = ts.OrderBy(x => x).ToList(); var ci = System.Globalization.CultureInfo.InvariantCulture;
    return "{\"n\":" + N + ",\"mean\":" + ts.Average().ToString("0.000", ci) + ",\"median\":" + s[s.Count/2].ToString("0.000", ci) + ",\"p95\":" + s[(int)(s.Count*0.95)].ToString("0.000", ci) + "}"; }
}"""

SIG = {"Press": ["name"], "Move": ["x", "z"], "CamShot": ["w", "h", "path"], "Quality": ["mode"], "StartSample": ["n"], "RenderBench": ["n"]}


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "PA", "methodName": method, "parameters": params}, timeout=180)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:600]


def cam_shots(tag):
    for name, w, h in (("desktop", 1920, 1080), ("phone", 2400, 1080)):
        print(cs("CamShot", str(w), str(h), os.path.join(OUT, f"{PHASE}_{tag}_{name}.png")))


def game_shot(tag):
    p = bridge.shot("_arena_" + tag)
    if p:
        dest = os.path.join(OUT, f"{PHASE}_{tag}_gameview.png")
        os.replace(p, dest)
        print("  saved", dest)


def main():
    stats = {}
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
        for _ in range(80):
            if str(cs("State")).startswith("Active+intro"):
                time.sleep(0.3)
                cam_shots("getready"); game_shot("getready"); break
            time.sleep(0.25)
        for _ in range(80):
            if str(cs("State")) == "Active":
                break
            time.sleep(0.5)
        time.sleep(2.0)
        print(cs("FreezeBots"))
        pos = str(cs("LocalPos")); print("local at", pos)
        sx = 1 if float(pos.split(";")[0]) >= 0 else -1
        sz = 1 if float(pos.split(";")[1]) >= 0 else -1

        # --- Mobile-tier measurement at the spawn corner (worst-case void in view)
        print(cs("Quality", "mobile")); time.sleep(3.0); print(cs("Quality", "check"))
        print(cs("StartSample", "300"))
        for _ in range(240):
            if str(cs("SampleProgress")).startswith("done"):
                break
            time.sleep(1)
        stats["mobile_spawn_sampler"] = json.loads(cs("SampleResult"))
        stats["mobile_spawn_render_cpu_ms"] = json.loads(cs("RenderBench", "300"))
        print(cs("Quality", "restore")); time.sleep(2.0)
        print(json.dumps(stats))

        # --- visual captures at the original tier
        cam_shots("spawn_active")
        print(cs("Move", "-6", "-6")); time.sleep(1.5)
        cam_shots("mid")
        print(cs("Move", str(sx * 22.5), "0")); time.sleep(1.5)
        cam_shots("edge")
        game_shot("edge")
    finally:
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)
    with open(os.path.join(OUT, f"{PHASE}_stats.json"), "w", encoding="utf-8") as f:
        json.dump(stats, f, indent=2)
    print("STATS", json.dumps(stats))


main()
