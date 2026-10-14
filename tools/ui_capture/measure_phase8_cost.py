"""Phase 8: Mobile-tier frame cost of the CURRENT committed arena at ONE fixed spawn corner (3 rounds, nothing changed or saved).

    python tools/ui_capture/measure_phase8_cost.py <tag>     -> Captures/env3d_cost/<tag>.json

Derived from capture_phase7_arena_fair.py (same Mobile-tier method, same corner) without the in-memory config swaps.

Same Mobile-tier method as capture_phase7_arena.py (runtime Mobile_RPAsset etc., 300 sampled editor ticks + 300 Camera.Render CPU
samples). Asset values are changed in memory and put back afterwards.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))

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
  // Arena-cost configs, applied to the loaded assets in memory only (nothing is saved; Cfg("restore") puts every value back).
  //   orig  : GrassFloor base map = Grass.png tiling 10, Surround_Grass (0.22,0.34,0.16), ArenaHedge inactive
  //   hedge : GrassFloor as orig, Surround_Grass dark (0.15,0.22,0.14), ArenaHedge active
  //   cur   : the committed state (GrassCalm.png tiling 7, dark surround, hedge on)
  public static string Cfg(string mode) {
    var grass = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Materials/GrassFloor.mat");
    var sur = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/Surround_Grass.mat");
    var oldTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Game/Art/Grass.png");
    var hedge = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "ArenaHedge" && t.gameObject.scene.IsValid());
    if (grass == null || sur == null || oldTex == null || hedge == null) return "MISSING grass=" + (grass != null) + " sur=" + (sur != null) + " tex=" + (oldTex != null) + " hedge=" + (hedge != null);
    if (SessionState.GetString("cfg_saved", "") == "") {
      SessionState.SetString("cfg_base", AssetDatabase.GetAssetPath(grass.GetTexture("_BaseMap"))); SessionState.SetFloat("cfg_tile", grass.GetTextureScale("_BaseMap").x);
      var c = sur.GetColor("_BaseColor"); SessionState.SetString("cfg_sur", c.r.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ";" + c.g.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ";" + c.b.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); SessionState.SetInt("cfg_hedge", hedge.gameObject.activeSelf ? 1 : 0); SessionState.SetString("cfg_saved", "1"); }
    var calm = AssetDatabase.LoadAssetAtPath<Texture2D>(SessionState.GetString("cfg_base", ""));
    var ci = System.Globalization.CultureInfo.InvariantCulture; var sp = SessionState.GetString("cfg_sur", "0.15;0.22;0.14").Split(';');
    var darkSur = new Color(float.Parse(sp[0], ci), float.Parse(sp[1], ci), float.Parse(sp[2], ci), 1f);
    bool useOldGrass = mode == "orig" || mode == "hedge"; Color surC = mode == "orig" ? new Color(0.22f, 0.34f, 0.16f, 1f) : darkSur; bool hedgeOn = mode != "orig";
    if (mode == "restore") { useOldGrass = false; surC = darkSur; hedgeOn = SessionState.GetInt("cfg_hedge", 1) == 1; }
    float tile = useOldGrass ? 10f : SessionState.GetFloat("cfg_tile", 7f); var tex = useOldGrass ? oldTex : calm;
    foreach (var p in new[] { "_BaseMap", "_MainTex" }) { if (!grass.HasProperty(p)) continue; grass.SetTexture(p, tex); grass.SetTextureScale(p, new Vector2(tile, tile)); }
    foreach (var p in new[] { "_BaseColor", "_Color" }) if (sur.HasProperty(p)) sur.SetColor(p, surC);
    hedge.gameObject.SetActive(hedgeOn);
    if (mode == "restore") SessionState.SetString("cfg_saved", "");
    return mode + ": grassTex=" + AssetDatabase.GetAssetPath(grass.GetTexture("_BaseMap")) + " tile=" + grass.GetTextureScale("_BaseMap").x + " sur=" + sur.GetColor("_BaseColor") + " hedge=" + hedge.gameObject.activeSelf; }
}"""

SIG = {"Press": ["name"], "Move": ["x", "z"], "Quality": ["mode"], "StartSample": ["n"], "RenderBench": ["n"], "Cfg": ["mode"]}
CORNER = ("-21.6", "21.8")


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "PA", "methodName": method, "parameters": params}, timeout=180)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:600]


def measure(mode):
    print(cs("Move", *CORNER)); time.sleep(2.5); print("at", cs("LocalPos"))
    print(cs("StartSample", "300"))
    for _ in range(240):
        if str(cs("SampleProgress")).startswith("done"):
            break
        time.sleep(1)
    r = {"sampler": json.loads(cs("SampleResult")), "render_cpu_ms": json.loads(cs("RenderBench", "300"))}
    print(mode, json.dumps(r)); return r


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
            if str(cs("State")) == "Active":
                break
            time.sleep(0.5)
        time.sleep(2.0)
        print(cs("FreezeBots"))
        print(cs("Quality", "mobile")); time.sleep(3.0)
        for rnd in (1, 2, 3):
            stats["cur_r" + str(rnd)] = measure("cur")
    finally:
        print(cs("Quality", "restore")); time.sleep(1.0)
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)
    print("STATS", json.dumps(stats))
    out = os.path.join(ROOT, "Captures", "env3d_cost"); os.makedirs(out, exist_ok=True)
    open(os.path.join(out, TAG + ".json"), "w", encoding="utf-8").write(json.dumps(stats, indent=2))


TAG = sys.argv[1] if len(sys.argv) > 1 else "run"
main()
