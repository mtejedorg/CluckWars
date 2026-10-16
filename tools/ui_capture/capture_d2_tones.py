"""Phase 8 D2: PC vs Mobile dark tones (fence wood, shadow, grass, pile) at the MATCH camera, both tiers, with render-pair masks.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_d2_tones.py <output_dir>
    env D2_CANDS="exp,contrast,shadowsLift,ssao;..."  extra PC variants (temp global Volume + runtime SSAO intensity, both restored)

Solo START, bots frozen, animators frozen, UI hidden, every pile full, local chicken pinned to SEAT 0 (match camera yaw 225, so the
sunlit faces are the same every run; D1 tool rule). Spots: "corner" (local at its base, the outer fence + an arm wall in frame) and
"pile" (beside corner 0's home pile). Per tier and spot, match-camera renders:
    <tier>_<spot>.png            the frame as shipped                       (all2 = the same frame again: diff = noise, masked out)
    <tier>_<spot>_nofence.png    every fence renderer ShadowsOnly           -> fence mask
    <tier>_<spot>_noground.png   GeneratedGround ShadowsOnly                -> ground mask
    <tier>_<spot>_nopile.png     every FoodPile renderer ShadowsOnly        -> pile mask
    <tier>_<spot>_nosun.png      the directional sun disabled               -> "shaded" = full ~= no-sun (no direct light reaches it)
PC only (D7 4-way): pcnopost_<spot>.png (camera post off), pcraw_<spot>.png (post on, the profile's compensation neutralised by a
temp volume), pccand<i>_<spot>.png per D2_CANDS entry. Then runs d2_tone_metrics.py (shadow = ground & shaded, grass = ground & lit,
fence shaded faces = fence & shaded). Performance Mode OFF (capture_common.perf_mode). Leaves Play mode at the end.
"""
import sys, os, time, subprocess
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge
import capture_common

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "d2")
os.makedirs(OUT, exist_ok=True)
W, H = 1920, 1080

CS = r"""using UnityEngine; using UnityEngine.UIElements; using UnityEditor; using System.Linq; using System.Reflection; using System.Collections.Generic; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
using UnityEngine.Rendering; using UnityEngine.Rendering.Universal;
public class D2 {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
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
  public static string FreezeBots() { int n = 0; foreach (var k in ChickenController.ActiveControllers) { if (k == null || k.HasInputAuthority) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot") && m.enabled) { m.enabled = false; n++; } } return "froze " + n; }
  public static string FreezeAnim(string on) { int n = 0; foreach (var k in ChickenController.ActiveControllers) { if (k == null) continue; foreach (var a in k.GetComponentsInChildren<Animator>()) { a.speed = on == "1" ? 0f : 1f; n++; } } return "animators speed " + (on == "1" ? 0 : 1) + " x" + n; }
  public static string HideUi(string on) { foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) if (d.rootVisualElement != null) d.rootVisualElement.style.display = on == "1" ? DisplayStyle.None : DisplayStyle.Flex; return "ui hidden=" + on; }
  static FoodPile[] Piles() { return FoodPile.ActivePiles.Where(p => p != null && p.Object != null && p.Object.IsValid).ToArray(); }
  public static string FillPiles() { int n = 0; foreach (var p in Piles()) { if (p.Object.HasStateAuthority) { p.Amount = p.MaxAmount; n++; } } return "filled " + n; }
  public const float Seat0Yaw = 225f;
  static Vector3 Seat0Fwd() { return Quaternion.Euler(0f, Seat0Yaw, 0f) * Vector3.forward; }
  public static string PinSeat0() { var l = Local(); if (!l.Object.HasStateAuthority) return "ERROR local has no state authority"; int was = l.HomeCornerIndex; l.HomeCornerIndex = 0; return "local HomeCornerIndex " + was + " -> 0"; }
  static void Put(ChickenController k, Vector3 pos) { pos.y = k.transform.position.y; var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else k.transform.position = pos; }
  // Every non-local chicken is parked far off-frame (opposite corner) so the frames hold only the scene + the local bird.
  public static string Stage(string which) { var l = Local(); var home = Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None).First(b => b.CornerIndex == 0);
    var others = ChickenController.ActiveControllers.Where(k => k != null && k != l).ToList(); int i = 0;
    foreach (var k in others) Put(k, -home.transform.position.normalized * 11f + new Vector3(i++ * 1.6f, 0, 0));
    if (which == "corner") { Put(l, home.transform.position); return "local at base " + home.transform.position.ToString("F2"); }
    var pile = Piles().Where(p => p.transform.position.sqrMagnitude > 1f).OrderBy(p => (p.transform.position - home.transform.position).sqrMagnitude).First();
    var fwd = Seat0Fwd(); var side = Vector3.Cross(Vector3.up, fwd);
    Put(l, pile.SurfaceApproachPoint(pile.transform.position - (fwd + side * 0.9f).normalized * 100f, 1.3f));
    return "local beside " + pile.name + " " + pile.transform.position.ToString("F2"); }
  static bool IsFence(Renderer r) { for (var t = r.transform; t != null; t = t.parent) if (t.name.StartsWith("Wall_") || t.name.StartsWith("InnerWall_")) return true; return r.name.StartsWith("wall_segment"); }
  static Renderer[] Targets(string mode) {
    var all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy);
    if (mode == "nofence") return all.Where(IsFence).ToArray();
    if (mode == "noground") return all.Where(r => r.name == "GeneratedGround").ToArray();
    if (mode == "nopile") return all.Where(r => r.GetComponentInParent<FoodPile>() != null).ToArray();
    return new Renderer[0]; }
  static Light Sun() { return Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Directional && l.enabled).OrderByDescending(l => l.intensity).FirstOrDefault(); }
  // Variant flags for the PC-only renders: "nopost" | "raw" | "cand:exp,contrast,lift,ssao" | "" (as shipped).
  public static string Shot(string mode, string variant, string path) {
    var ts = Targets(mode); var old = ts.Select(r => r.shadowCastingMode).ToArray(); if (mode != "all" && mode != "all2" && mode != "nosun" && ts.Length == 0) return "ERROR no targets for " + mode;
    var cam = MatchCamera.Instance.Camera; var uacd = cam.GetComponent<UniversalAdditionalCameraData>(); bool postWas = uacd.renderPostProcessing;
    var sun = Sun(); if (mode == "nosun" && sun == null) return "ERROR no sun";
    GameObject vol = null; float ssaoWas = float.NaN; object ssaoSet = null; FieldInfo ssaoField = null;
    foreach (var r in ts) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
    try {
      if (mode == "nosun") sun.enabled = false;
      if (variant == "nopost") uacd.renderPostProcessing = false;
      if (variant == "raw" || variant.StartsWith("cand:")) {
        vol = new GameObject("__D2Vol"); var v = vol.AddComponent<Volume>(); v.isGlobal = true; v.priority = 100; v.weight = 1; var p = ScriptableObject.CreateInstance<VolumeProfile>(); v.sharedProfile = p;
        var ca = p.Add<ColorAdjustments>(false); var lgg = p.Add<LiftGammaGain>(false);
        if (variant == "raw") { ca.postExposure.Override(0f); ca.contrast.Override(0f); lgg.lift.Override(new Vector4(1, 1, 1, 0)); }
        else { var a = variant.Substring(5).Split(',').Select(x => float.Parse(x, CI)).ToArray(); ca.postExposure.Override(a[0]); ca.contrast.Override(a[1]); lgg.lift.Override(new Vector4(1, 1, 1, a[2]));
          if (a.Length > 3) { var ssao = Ssao(); if (ssao == null) return "ERROR no SSAO feature"; ssaoField = ssao.GetType().GetField("m_Settings", BindingFlags.NonPublic | BindingFlags.Instance); ssaoSet = ssaoField.GetValue(ssao);
            var fi = ssaoSet.GetType().GetField("Intensity", BindingFlags.NonPublic | BindingFlags.Instance); ssaoWas = (float)fi.GetValue(ssaoSet); fi.SetValue(ssaoSet, a[3]); } } }
      VolumeManager.instance.Update(cam.transform, uacd.volumeLayerMask);
      var rt = CapTier.NewRt(""" + str(W) + ", " + str(H) + r"""); var prevT = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.Render(); cam.targetTexture = prevT;
      var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
      System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
    finally { for (int i = 0; i < ts.Length; i++) ts[i].shadowCastingMode = old[i]; if (sun != null) sun.enabled = true; uacd.renderPostProcessing = postWas;
      if (vol != null) Object.DestroyImmediate(vol);
      if (ssaoSet != null) ssaoSet.GetType().GetField("Intensity", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ssaoSet, ssaoWas); }
    float dy = Mathf.Abs(Mathf.DeltaAngle(cam.transform.eulerAngles.y, Seat0Yaw)); if (dy > 1f) return "ERROR match camera yaw " + cam.transform.eulerAngles.y + " not at seat 0";
    return "saved " + System.IO.Path.GetFileName(path) + " (" + mode + "/" + variant + ", " + ts.Length + " rs, post=" + uacd.renderPostProcessing + ")"; }
  static ScriptableRendererFeature Ssao() { var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset; if (rp == null) return null;
    var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rp) as ScriptableRendererData[];
    return list.Where(d => d != null).SelectMany(d => d.rendererFeatures).FirstOrDefault(f => f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion"); }
  static string Snap() { var rp = QualitySettings.renderPipeline; return string.Join("|", new[] { AssetDatabase.GetAssetPath(rp), QualitySettings.vSyncCount.ToString(), QualitySettings.lodBias.ToString("R", CI),
      ((int)QualitySettings.skinWeights).ToString(), ((int)QualitySettings.anisotropicFiltering).ToString(), QualitySettings.pixelLightCount.ToString(), QualitySettings.maximumLODLevel.ToString(),
      QualitySettings.softParticles ? "1" : "0", QualitySettings.realtimeReflectionProbes ? "1" : "0" }); }
  static void ApplySnap(string snap) { var f = snap.Split('|');
    QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(f[0]); QualitySettings.vSyncCount = int.Parse(f[1]); QualitySettings.lodBias = float.Parse(f[2], CI);
    QualitySettings.skinWeights = (SkinWeights)int.Parse(f[3]); QualitySettings.anisotropicFiltering = (AnisotropicFiltering)int.Parse(f[4]); QualitySettings.pixelLightCount = int.Parse(f[5]);
    QualitySettings.maximumLODLevel = int.Parse(f[6]); QualitySettings.softParticles = f[7] == "1"; QualitySettings.realtimeReflectionProbes = f[8] == "1"; }
  public static string Quality(string mode) { int mob = System.Array.IndexOf(QualitySettings.names, "Mobile");
    if (mode == "capture") { if (mob < 0) return "ERROR no Mobile level (Play mode?)"; int prev = QualitySettings.GetQualityLevel(); SessionState.SetString("d2_pc_snap", Snap()); SessionState.SetString("d2_prev_level", QualitySettings.names[prev]);
      try { QualitySettings.SetQualityLevel(mob, true); SessionState.SetString("d2_mobile_snap", Snap()); } finally { QualitySettings.SetQualityLevel(prev, true); } return "snapshotted Mobile"; }
    if (mode == "mobile") { if (mob >= 0) QualitySettings.SetQualityLevel(mob, true); else ApplySnap(SessionState.GetString("d2_mobile_snap", "")); return "mobile rp=" + QualitySettings.renderPipeline.name; }
    string pl = SessionState.GetString("d2_prev_level", ""); if (pl != "") { int i = System.Array.IndexOf(QualitySettings.names, pl); if (i >= 0) QualitySettings.SetQualityLevel(i, true); }
    string pc = SessionState.GetString("d2_pc_snap", ""); if (pc != "") ApplySnap(pc); return "restored rp=" + QualitySettings.renderPipeline.name; }
  public static string Info() { var s = Ssao(); string ss = "none"; if (s != null) { var st = s.GetType().GetField("m_Settings", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(s); ss = st.GetType().GetField("Intensity", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(st) + (s.isActive ? "" : " (inactive)"); }
    return CapTier.Info() + " ssao=" + ss; }
}""" + capture_common.CAP_TIER_CS

SIG = {"Press": ["name"], "HideUi": ["on"], "FreezeAnim": ["on"], "Stage": ["which"], "Shot": ["mode", "variant", "path"], "Quality": ["mode"]}


def cs(method, *args, strict=True):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "D2", "methodName": method, "parameters": params}, timeout=180)
    try:
        v = m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        v = None
    if v is None or str(v).startswith("ERROR"):
        if strict:
            raise RuntimeError(f"D2.{method} failed: " + (str(v) if v is not None else bridge.text(m)[:800]))
        return str(v)
    return v


def P(name):
    return os.path.join(OUT, name).replace("\\", "/")


def start():
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(60):
        if str(cs("Press", "SoloBtn", strict=False)).startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(0.8)
    print(cs("Press", "NextBtn")); time.sleep(1.0)
    print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5)
    print(cs("Press", "StartBtn"))
    for _ in range(240):
        if str(cs("State", strict=False)) == "Active":
            return
        time.sleep(0.25)
    raise RuntimeError("match never reached Active")


def cands():
    return [c for c in os.environ.get("D2_CANDS", "").split(";") if c.strip()]


def shoot(tier):
    print(f"PASS {tier.upper()}: " + cs("Info"))
    for spot in ("corner", "pile"):
        print(cs("FillPiles"))
        print(cs("FreezeAnim", "0")); print(cs("Stage", spot)); time.sleep(1.8); print(cs("FreezeAnim", "1")); time.sleep(0.3)
        for mode in ("all", "nofence", "noground", "nopile", "nosun", "all2"):
            print(cs("Shot", mode, "", P(f"{tier}_{spot}" + ("" if mode == "all" else "_" + mode) + ".png")))
        if tier == "pc":
            print(cs("Shot", "all", "nopost", P(f"pcnopost_{spot}.png")))
            print(cs("Shot", "all", "raw", P(f"pcraw_{spot}.png")))
            for i, c in enumerate(cands()):
                print(cs("Shot", "all", "cand:" + c, P(f"pccand{i}_{spot}.png")))


def main():
    try:
        print(cs("Quality", "capture"))
        start(); time.sleep(2.0)
        print(cs("FreezeBots")); print(cs("HideUi", "1")); print(cs("PinSeat0")); time.sleep(2.0)
        shoot("pc")
        print(cs("Quality", "mobile")); time.sleep(2.5)
        shoot("mobile")
    finally:
        for label, step in (("quality", lambda: print(cs("Quality", "restore", strict=False))), ("ui", lambda: cs("HideUi", "0", strict=False)),
                            ("play off", lambda: bridge.call("editor-application-set-state", {"isPlaying": False}))):
            try:
                step()
            except Exception as e:
                print(f"CLEANUP FAILED ({label}): {e}")
        time.sleep(4)


if __name__ == "__main__":
    bridge.init()
    with open(os.path.join(OUT, "cands.txt"), "w") as f:
        f.write("\n".join(cands()))
    with capture_common.perf_mode():
        main()
    subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "d2_tone_metrics.py"), OUT], check=False)
