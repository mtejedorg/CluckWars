"""Phase 8 D7 (pennant): match-camera renders of every base's pennant from every seat's camera, plus flag-vs-ring colour and facing metrics.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_d7_pennant.py <output_dir> [pc|mobile|both] [own]

Solo START, bots frozen, UI hidden, MATCH camera (RenderTexture of the live MatchCamera, same as capture_env3d), Performance Mode OFF
(capture_common.perf_mode). The local chicken is pinned to seat V (HomeCornerIndex = V, runtime only, so the real match camera turns to seat V's
yaw: 225 / 135 / 45 / 315 for seat 0..3) and teleported onto base B, so base B is centred under seat V's camera. All 16 (viewer V, base B) pairs are
rendered per tier; V == B is the "own base" view every player sees, V != B the "rival base" view. Every other chicken is parked at the arena centre.

Per tier, per (V, B):
    <tier>_v<V>_b<B>.png        the frame
    <tier>_v<V>_b<B>.json       projected flag triangles + ring sample points (pixels), world facing of the flag vs the camera, palette colour
Then d7_pennant_metrics.py reads them: rendered flag colour (median of the eroded flag mask) vs rendered ring colour (median of the ring samples),
CIEDE2000 between them, and the angle between the flag plane and the camera view plane.
"""
import sys, os, time, subprocess
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge
import capture_common

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "d7")
TIERS = {"pc": ["pc"], "mobile": ["mobile"], "both": ["pc", "mobile"]}[sys.argv[2] if len(sys.argv) > 2 else "both"]
OWN_ONLY = len(sys.argv) > 3 and sys.argv[3] == "own"   # tuning runs: only the 4 own-base views (viewer V on base V)
os.makedirs(OUT, exist_ok=True)
W, H = 1920, 1080

CS = r"""using UnityEngine; using UnityEngine.UIElements; using UnityEditor; using System.Linq; using System.Reflection; using System.Text; using System.Collections.Generic; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class D7 {
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
  public static string FreezeAnim(string on) { int n = 0; foreach (var k in ChickenController.ActiveControllers) { if (k == null) continue; foreach (var a in k.GetComponentsInChildren<Animator>()) { a.speed = on == "1" ? 0f : 1f; n++; } } return "animators " + n; }
  public static string HideUi(string on) { foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) if (d.rootVisualElement != null) d.rootVisualElement.style.display = on == "1" ? DisplayStyle.None : DisplayStyle.Flex; return "ui hidden=" + on; }
  static void Put(ChickenController k, Vector3 pos) { pos.y = k.transform.position.y; var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else k.transform.position = pos; }
  static PlayerBase BaseAt(int corner) { return Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None).First(b => b.CornerIndex == corner); }
  // Pin the local seat (camera yaw follows HomeCornerIndex) and stand the local bird on base `b`; everyone else is parked at the arena centre.
  public static string Stage(string viewer, string b) { var l = Local(); if (l == null) return "ERROR no local"; if (!l.Object.HasStateAuthority) return "ERROR local has no state authority";
    l.HomeCornerIndex = int.Parse(viewer); var others = ChickenController.ActiveControllers.Where(k => k != null && k != l).ToList(); int i = 0;
    foreach (var k in others) Put(k, new Vector3(-2.4f + 1.6f * i++, 0, 0));
    Put(l, BaseAt(int.Parse(b)).transform.position); return "viewer seat " + viewer + " on base " + b; }
  // yaw + position of the live match camera, for the settle poll.
  public static string Cam() { var c = MatchCamera.Instance.Camera.transform; return c.eulerAngles.y.ToString("F3", CI) + " " + c.position.x.ToString("F3", CI) + " " + c.position.y.ToString("F3", CI) + " " + c.position.z.ToString("F3", CI); }

  // Tuning sweep: set the flag lift on every base at runtime and force the tint to re-apply (PlayerBase.LateUpdate re-runs it when _tintInitialized is false).
  public static string Lift(string v) { float lift = float.Parse(v, CI); int n = 0; var t = typeof(PlayerBase);
    foreach (var b in Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None)) { t.GetField("_flagLift", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(b, lift); t.GetField("_tintInitialized", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(b, false); n++; }
    return "lift " + lift.ToString("0.00", CI) + " on " + n + " bases"; }
  static string Hex(Color c) { return ColorUtility.ToHtmlStringRGB(c); }
  static Vector2 Px(Camera cam, Vector3 w, int W, int H) { var p = cam.worldToCameraMatrix.MultiplyPoint(w); float a = (float)W / H; float os = cam.orthographicSize;
    return new Vector2((p.x / (os * a) * 0.5f + 0.5f) * W, (1f - (p.y / os * 0.5f + 0.5f)) * H); }
  static string P2(Vector2 v) { return "[" + v.x.ToString("F1", CI) + "," + v.y.ToString("F1", CI) + "]"; }
  public static string Shot(string w, string h, string pngPath, string jsonPath) { int W = int.Parse(w), H = int.Parse(h); var cam = MatchCamera.Instance.Camera;
    if (!cam.orthographic) return "ERROR match camera is not orthographic: the pixel projection below assumes it";
    var rt = CapTier.NewRt(W, H); var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(pngPath, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
    var sb = new StringBuilder(); sb.Append("{\"camYaw\":" + cam.transform.eulerAngles.y.ToString("F2", CI) + ",\"camPitch\":" + cam.transform.eulerAngles.x.ToString("F2", CI) + ",\"bases\":[");
    bool first = true; var fwd = cam.transform.forward;
    foreach (var b in Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None).OrderBy(x => x.CornerIndex)) {
      var flag = b.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "Pennant_Flag"); if (flag == null) return "ERROR base " + b.CornerIndex + " has no Pennant_Flag";
      var mesh = flag.GetComponent<MeshFilter>().sharedMesh; var tr = flag.transform; var tris = mesh.triangles; var vs = mesh.vertices;
      // The flag's spanning axes in world space (mesh -x = out from the pole, mesh +z = up on the cloth): its plane normal is their cross product.
      var u = tr.TransformDirection(Vector3.left).normalized; var v = tr.TransformDirection(Vector3.forward).normalized; var n = Vector3.Cross(u, v).normalized;
      float ang = Mathf.Acos(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(n, -fwd)))) * Mathf.Rad2Deg;
      var nf = n; nf.y = 0; var ff = fwd; ff.y = 0; float az = Mathf.Acos(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(nf.normalized, -ff.normalized)))) * Mathf.Rad2Deg; // yaw-only: the camera pitch (30) adds to the 3D angle of an upright flag
      var mpb = new MaterialPropertyBlock(); flag.GetPropertyBlock(mpb); Color bc = mpb.GetColor("_BaseColor");
      if (!first) sb.Append(","); first = false;
      sb.Append("{\"corner\":" + b.CornerIndex + ",\"palette\":\"" + Hex(CluckWars.UI.PlayerPalette.ForCorner(b.CornerIndex)) + "\",\"flagBaseColor\":[" + bc.r.ToString("F4", CI) + "," + bc.g.ToString("F4", CI) + "," + bc.b.ToString("F4", CI) + "]"
        + ",\"flagNormal\":[" + n.x.ToString("F3", CI) + "," + n.y.ToString("F3", CI) + "," + n.z.ToString("F3", CI) + "],\"flagVsCameraDeg\":" + ang.ToString("F2", CI) + ",\"flagAzimuthDeg\":" + az.ToString("F2", CI)
        + ",\"flagShader\":\"" + flag.sharedMaterial.shader.name + "\",\"pole\":" + P2(Px(cam, tr.parent.position, W, H)) + ",\"tris\":[");
      for (int t = 0; t < tris.Length; t += 3) { if (t > 0) sb.Append(","); sb.Append("[" + P2(Px(cam, tr.TransformPoint(vs[tris[t]]), W, H)) + "," + P2(Px(cam, tr.TransformPoint(vs[tris[t + 1]]), W, H)) + "," + P2(Px(cam, tr.TransformPoint(vs[tris[t + 2]]), W, H)) + "]"); }
      sb.Append("],\"ring\":[");
      for (int a = 0; a < 90; a++) { float th = a * 4f * Mathf.Deg2Rad; var wp = b.transform.position + new Vector3(Mathf.Cos(th), 0.02f, Mathf.Sin(th)) * 2.5f; if (a > 0) sb.Append(","); sb.Append(P2(Px(cam, wp, W, H))); }
      sb.Append("]}"); }
    sb.Append("]}"); System.IO.File.WriteAllText(jsonPath, sb.ToString()); return "saved " + System.IO.Path.GetFileName(pngPath); }

  static string Snap() { var rp = QualitySettings.renderPipeline; return string.Join("|", new[] { AssetDatabase.GetAssetPath(rp), QualitySettings.vSyncCount.ToString(), QualitySettings.lodBias.ToString("R", CI),
      ((int)QualitySettings.skinWeights).ToString(), ((int)QualitySettings.anisotropicFiltering).ToString(), QualitySettings.pixelLightCount.ToString(), QualitySettings.maximumLODLevel.ToString(),
      QualitySettings.softParticles ? "1" : "0", QualitySettings.realtimeReflectionProbes ? "1" : "0" }); }
  static void ApplySnap(string snap) { var f = snap.Split('|');
    QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(f[0]); QualitySettings.vSyncCount = int.Parse(f[1]); QualitySettings.lodBias = float.Parse(f[2], CI);
    QualitySettings.skinWeights = (SkinWeights)int.Parse(f[3]); QualitySettings.anisotropicFiltering = (AnisotropicFiltering)int.Parse(f[4]); QualitySettings.pixelLightCount = int.Parse(f[5]);
    QualitySettings.maximumLODLevel = int.Parse(f[6]); QualitySettings.softParticles = f[7] == "1"; QualitySettings.realtimeReflectionProbes = f[8] == "1"; }
  public static string Quality(string mode) { int mob = System.Array.IndexOf(QualitySettings.names, "Mobile");
    if (mode == "capture") { if (mob < 0) return "ERROR no Mobile level (Play mode?)"; int prev = QualitySettings.GetQualityLevel(); SessionState.SetString("d7_pc_snap", Snap()); SessionState.SetString("d7_prev_level", QualitySettings.names[prev]);
      try { QualitySettings.SetQualityLevel(mob, true); SessionState.SetString("d7_mobile_snap", Snap()); } finally { QualitySettings.SetQualityLevel(prev, true); } return "snapshotted Mobile"; }
    if (mode == "mobile") { if (mob >= 0) QualitySettings.SetQualityLevel(mob, true); else ApplySnap(SessionState.GetString("d7_mobile_snap", "")); return "mobile rp=" + QualitySettings.renderPipeline.name; }
    string pl = SessionState.GetString("d7_prev_level", ""); if (pl != "") { int i = System.Array.IndexOf(QualitySettings.names, pl); if (i >= 0) QualitySettings.SetQualityLevel(i, true); }
    string pc = SessionState.GetString("d7_pc_snap", ""); if (pc != "") ApplySnap(pc); return "restored rp=" + QualitySettings.renderPipeline.name; }
  public static string Info() { return CapTier.Info(); }
}""" + capture_common.CAP_TIER_CS

SIG = {"Press": ["name"], "HideUi": ["on"], "FreezeAnim": ["on"], "Stage": ["viewer", "b"], "Lift": ["v"], "Shot": ["w", "h", "pngPath", "jsonPath"], "Quality": ["mode"]}


def cs(method, *args, strict=True):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "D7", "methodName": method, "parameters": params}, timeout=180)
    try:
        v = m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        v = None
    if v is None or str(v).startswith("ERROR"):
        if strict:
            raise RuntimeError(f"D7.{method} failed: " + (str(v) if v is not None else bridge.text(m)[:800]))
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


def settle(viewer):
    """Wait for the match camera to reach seat `viewer`'s yaw and stop moving (the yaw lerps at ~5/s, the focus smooth-damps onto the chicken)."""
    target = [225.0, 135.0, 45.0, 315.0][viewer]
    last = None
    for _ in range(60):
        yaw, x, y, z = (float(t) for t in cs("Cam").split())
        dyaw = abs(((yaw - target + 180) % 360) - 180)
        cur = (x, y, z)
        if dyaw < 0.2 and last is not None and max(abs(a - b) for a, b in zip(cur, last)) < 0.005:
            return
        last = cur
        time.sleep(0.4)
    raise RuntimeError(f"camera never settled for seat {viewer} (yaw {yaw:.2f} target {target}, pos {last})")


def shoot(tier):
    global OUT
    print(f"PASS {tier.upper()}: " + cs("Info"))
    lifts = [x for x in os.environ.get("D7_LIFTS", "").split(",") if x.strip()]   # tuning sweep: D7_LIFTS=1.0,1.15,1.3 (use with the "own" argument)
    if not lifts:
        shoot_pairs(tier)
        return
    base_out = OUT
    for lf in lifts:
        print(cs("Lift", lf)); time.sleep(0.6)
        OUT = os.path.join(base_out, "lift_" + lf); os.makedirs(OUT, exist_ok=True)
        shoot_pairs(tier)
    OUT = base_out


def shoot_pairs(tier):
    for v in range(4):
        for b in range(4):
            if OWN_ONLY and b != v:
                continue
            print(cs("Stage", v, b)); settle(v); time.sleep(0.3)
            print(cs("Shot", W, H, P(f"{tier}_v{v}_b{b}.png"), P(f"{tier}_v{v}_b{b}.json")))


def main():
    try:
        print(cs("Quality", "capture"))
        start(); time.sleep(2.0)
        print(cs("FreezeBots")); print(cs("FreezeAnim", "1")); print(cs("HideUi", "1")); time.sleep(1.0)
        for tier in TIERS:
            if tier == "mobile":
                print(cs("Quality", "mobile")); time.sleep(2.5)
            shoot(tier)
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
    with capture_common.perf_mode():
        main()
    subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "d7_pennant_metrics.py"), OUT], check=False)
