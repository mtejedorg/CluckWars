"""Phase 8 E2 captures: fixed fence-fade slots for the LOCAL player's own pad + its 2 home-side piles.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_e2_fence.py <output_dir> pc|mobile [explore]

Solo START, bots frozen, UI hidden, MATCH camera (RenderTexture of the live MatchCamera, same as capture_env3d), Performance Mode OFF
(capture_common.perf_mode). Shots, per tier:
  getready       the intro countdown (GET READY), chicken at spawn
  spawn          Active, chicken still at spawn, not carrying
  carry_pad / carry_pile_a / carry_pile_b   carrying food, 5-6.5 m from the target in open ground, target low on screen behind the near fence
  near_pile_a_nocargo   not carrying, 5 m from the pile (< 8 m): open by distance
  far_pad_control / far_pile_a_control   NOT carrying, 9.5 m away, target still in frame: the hole must stay closed
Leaves Play mode (and the quality level) as it found them.
"""
import sys, os, time
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge
import capture_common

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
TIER = sys.argv[2] if len(sys.argv) > 2 else "pc"
EXPLORE = len(sys.argv) > 3 and sys.argv[3] == "explore"
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using UnityEditor; using System.Linq; using System.Reflection; using System.Text; using System.Collections.Generic; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class E2X {
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
  public static string FreezeBots() { var l = Local(); foreach (var k in ChickenController.ActiveControllers) { if (k == null || k == l) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot")) m.enabled = false; } return "froze bots"; }
  public static string HideUi(string on) { foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) if (d.rootVisualElement != null) d.rootVisualElement.style.display = on == "1" ? DisplayStyle.None : DisplayStyle.Flex; return "ui hidden=" + on; }
  static void Put(ChickenController k, Vector3 pos) { pos.y = k.transform.position.y; var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else k.transform.position = pos; }
  public static string Move(string x, string z) { var l = Local(); if (l == null) return "ERROR no local"; Put(l, new Vector3(float.Parse(x, CI), 0, float.Parse(z, CI))); return "local -> " + x + "," + z; }
  public static string SetCargo(string v) { var l = Local(); if (l == null) return "ERROR no local"; var c = l.GetComponent<ChickenCargo>(); if (c == null) return "ERROR no cargo"; c.Cargo = float.Parse(v, CI); return "cargo=" + c.Cargo.ToString("0.0", CI); }
  // The local seat's own pad.
  static PlayerBase OwnBase() { var l = Local(); if (l == null) return null; return PlayerBase.ActiveBases.FirstOrDefault(b => b != null && BaseDepositRules.IsAllegianceMatch(l.HomeCornerIndex, b.CornerIndex)); }
  static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
  // Camera screen-right on the ground, from the live match camera.
  static Vector3 CamRight() { var r = MatchCamera.Instance.Camera.transform.right; r.y = 0; return r.normalized; }
  static Vector3 CamForward() { var f = MatchCamera.Instance.Camera.transform.forward; f.y = 0; return f.normalized; }
  public static string Data() { var sb = new StringBuilder(); var cam = MatchCamera.Instance.Camera; var l = Local(); var pad = OwnBase();
    sb.Append("cam pos=" + cam.transform.position.ToString("F2") + " rot=" + cam.transform.eulerAngles.ToString("F1") + " ortho=" + cam.orthographicSize + " aspect=" + cam.aspect.ToString("F3") + "\n");
    sb.Append("local=" + (l ? l.transform.position.ToString("F2") : "-") + " ownPad=" + (pad ? pad.transform.position.ToString("F2") + " r=" + pad.DepositRadius + " corner=" + pad.CornerIndex : "-") + "\n");
    foreach (var f in FoodPile.ActivePiles) sb.Append("pile " + f.name + " pos=" + f.transform.position.ToString("F2") + " fp=" + f.CurrentFootprint.ToString("F2") + " yaw=" + f.Yaw.ToString("F0") + " centre=" + f.IsCentre + " amt=" + f.Amount.ToString("F1")
        + (pad ? " dPad=" + Vector3.Distance(Flat(f.transform.position), Flat(pad.transform.position)).ToString("F2") : "") + "\n");
    var outer = new List<Bounds>(); foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) { var m = r.sharedMaterial; if (r.enabled && m != null && m.shader != null && m.shader.name == FenceFadeRules.ShaderName && FenceFadeRules.FadesForAllBirds(m)) outer.Add(r.bounds); }
    sb.Append("outer fence bounds=" + outer.Count + "\n");
    Vector3 toward = -cam.transform.forward;
    Vector3[] pts = pad ? new[] { pad.transform.position } : new Vector3[0];
    foreach (var p in pts) sb.Append("pad centre occluded(h0.1)=" + FenceFadeRules.IsOccluded(p, 0.1f, toward, 100f, outer) + " (h1.4)=" + FenceFadeRules.IsOccluded(p, 1.4f, toward, 100f, outer) + "\n");
    foreach (var f in FoodPile.ActivePiles) sb.Append(f.name + " occluded(h1.4)=" + FenceFadeRules.IsOccluded(Flat(f.transform.position), 1.4f, toward, 100f, outer) + " (h0.1)=" + FenceFadeRules.IsOccluded(Flat(f.transform.position), 0.1f, toward, 100f, outer) + "\n");
    return sb.ToString(); }
  public static string Probe() { var sb = new StringBuilder(); var cam = MatchCamera.Instance.Camera; var pad = OwnBase();
    var outer = new List<Bounds>(); foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) { var m = r.sharedMaterial; if (r.enabled && m != null && m.shader != null && m.shader.name == FenceFadeRules.ShaderName && FenceFadeRules.FadesForAllBirds(m)) outer.Add(r.bounds); }
    float topY = 0; foreach (var b in outer) topY = Mathf.Max(topY, b.max.y); sb.Append("outer fence top y=" + topY.ToString("F2") + " first=" + outer[0].center.ToString("F1") + "/" + outer[0].size.ToString("F2") + "\n");
    Vector3 toward = -cam.transform.forward; var p0 = Flat(pad.transform.position);
    foreach (var f in FoodPile.ActivePiles.OrderBy(f => (Flat(f.transform.position) - p0).sqrMagnitude).Take(4)) {
      var fp = f.CurrentFootprint; int hit = 0, tot = 0; string rims = "";
      for (int a = -1; a < 8; a++) { var off = a < 0 ? Vector3.zero : Quaternion.Euler(0, f.Yaw + a * 45f, 0) * new Vector3(fp.x * 0.5f, 0, 0); // rough ellipse rim in pile frame
        if (a >= 0) { off = new Vector3(Mathf.Cos(a * 45f * Mathf.Deg2Rad) * fp.x * 0.5f, 0, Mathf.Sin(a * 45f * Mathf.Deg2Rad) * fp.y * 0.5f); off = Quaternion.Euler(0, -f.Yaw, 0) * off; }
        var pt = Flat(f.transform.position) + off; foreach (var h in new[] { 0.05f, 0.5f }) { tot++; bool o = FenceFadeRules.IsOccluded(pt, h, toward, 100f, outer); if (o) { hit++; rims += a + "@" + h + " "; } } }
      sb.Append(f.name + " " + f.transform.position.ToString("F1") + " dPad=" + Vector3.Distance(Flat(f.transform.position), p0).ToString("F1") + " occluded " + hit + "/" + tot + " [" + rims + "]\n"); }
    int ph = 0, pt2 = 0; for (int a = -1; a < 16; a++) { var off = a < 0 ? Vector3.zero : new Vector3(Mathf.Cos(a * 22.5f * Mathf.Deg2Rad), 0, Mathf.Sin(a * 22.5f * Mathf.Deg2Rad)) * pad.DepositRadius; pt2++; if (FenceFadeRules.IsOccluded(p0 + off, 0.05f, toward, 100f, outer)) ph++; }
    sb.Append("pad ring/centre points occluded " + ph + "/" + pt2 + "\n"); return sb.ToString(); }
  // Named poses, all derived from live data (nothing hard-coded to a seat): the chicken stands `d` metres from the named target toward the arena
  // centre side as seen by the camera (so the target stays lower on screen, near the fence, and the chicken is out in the open ground).
  // home piles = the non-centre piles ranked by distance to the pad, skipping the nearest (the personal island deep inside the arena).
  public static string Pose(string name, string dist, string side) { var l = Local(); var pad = OwnBase(); if (l == null || pad == null) return "ERROR no local/pad";
    var p0 = Flat(pad.transform.position); var fwd = CamForward(); float d = float.Parse(dist, CI), sd = float.Parse(side, CI);
    var piles = FoodPile.ActivePiles.Where(f => !f.IsCentre).OrderBy(f => (Flat(f.transform.position) - p0).sqrMagnitude).ToList();
    Vector3 target;
    if (name == "pad") target = p0;
    else if (name == "pile_a") { if (piles.Count < 2) return "ERROR no home pile a"; target = Flat(piles[1].transform.position); }
    else if (name == "pile_b") { if (piles.Count < 3) return "ERROR no home pile b"; target = Flat(piles[2].transform.position); }
    else return "ERROR pose " + name;
    var spot = target + fwd * d + CamRight() * sd; Put(l, spot); return "local -> " + spot.ToString("F2") + " target " + name + " " + target.ToString("F2") + " dist " + Vector3.Distance(spot, target).ToString("F2"); }
  public static string Info() { var sb = new StringBuilder(); var d = Object.FindFirstObjectByType<FenceFadeDriver>(); var l = Local(); var pad = OwnBase(); var cg = l ? l.GetComponent<ChickenCargo>() : null;
    sb.Append("local=" + (l ? l.transform.position.ToString("F2") : "-") + " cargo=" + (cg ? cg.Cargo.ToString("F1", CI) : "-") + " padDist=" + (l && pad ? Vector3.Distance(Flat(l.transform.position), Flat(pad.transform.position)).ToString("F2", CI) : "-")
      + " state=" + State() + " kw=" + Shader.IsKeywordEnabled("_CW_FENCE_FADE") + " localStrength=" + (d ? d.Strength.ToString("0.00", CI) : "-") + " slots=" + (d ? d.SlotCount.ToString() : "-"));
    foreach (var n in new[] { "FixedTargetCount", "FixedTargetStrengths" }) { var pi = typeof(FenceFadeDriver).GetProperty(n); if (pi != null && d != null) { var v = pi.GetValue(d); sb.Append(" " + n + "=" + (v is float[] fa ? string.Join("/", fa.Select(x => x.ToString("0.00", CI))) : v)); } }
    return sb.ToString(); }
  static void Save(Camera cam, int W, int H, string path) {
    var rt = CapTier.NewRt(W, H); var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
  public static string CamShot(string w, string h, string path) { Save(MatchCamera.Instance.Camera, int.Parse(w), int.Parse(h), path); return "saved " + path; }
  public static string Quality(string mode) {
    string how = ""; int mob = System.Array.IndexOf(QualitySettings.names, "Mobile");
    if (mode == "capture") { if (mob < 0) return "ERROR no Mobile quality level in the level list (is the Editor in Play mode?)"; int prev = QualitySettings.GetQualityLevel();
      SessionState.SetString("e3_pc_snap", Snap());
      try { QualitySettings.SetQualityLevel(mob, true); SessionState.SetString("e3_mobile_snap", Snap()); } finally { QualitySettings.SetQualityLevel(prev, true); } how = "snapshotted Mobile level"; }
    else if (mode == "mobile") { if (SessionState.GetString("e3_prev_level", "") == "") SessionState.SetString("e3_prev_level", QualitySettings.names[QualitySettings.GetQualityLevel()]);
      if (mob >= 0) { QualitySettings.SetQualityLevel(mob, true); SessionState.SetString("e3_tier", ""); how = "SetQualityLevel(" + mob + ")"; }
      else { string snap = SessionState.GetString("e3_mobile_snap", ""); if (snap == "") return "ERROR no Mobile snapshot: run Quality(capture) in Edit mode first"; ApplySnap(snap); SessionState.SetString("e3_tier", "Mobile"); how = "Mobile snapshot applied"; } }
    else { string prev = SessionState.GetString("e3_prev_level", "");
      if (prev != "") { int i = System.Array.IndexOf(QualitySettings.names, prev); QualitySettings.SetQualityLevel(i >= 0 ? i : QualitySettings.GetQualityLevel(), true);
        string pcSnap = SessionState.GetString("e3_pc_snap", ""); if (pcSnap != "") ApplySnap(pcSnap); SessionState.SetString("e3_prev_level", ""); SessionState.SetString("e3_tier", ""); how = "restored " + prev; } }
    return "tier=" + SessionState.GetString("e3_tier", "PC") + " (" + how + ") rp=" + QualitySettings.renderPipeline.name; }
  static string Snap() { var rp = QualitySettings.renderPipeline; return string.Join("|", new[] { AssetDatabase.GetAssetPath(rp), QualitySettings.vSyncCount.ToString(), QualitySettings.lodBias.ToString("R", CI),
      ((int)QualitySettings.skinWeights).ToString(), ((int)QualitySettings.anisotropicFiltering).ToString(), QualitySettings.pixelLightCount.ToString(), QualitySettings.maximumLODLevel.ToString(),
      QualitySettings.softParticles ? "1" : "0", QualitySettings.realtimeReflectionProbes ? "1" : "0" }); }
  static void ApplySnap(string snap) { var f = snap.Split('|');
    QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(f[0]); QualitySettings.vSyncCount = int.Parse(f[1]); QualitySettings.lodBias = float.Parse(f[2], CI);
    QualitySettings.skinWeights = (SkinWeights)int.Parse(f[3]); QualitySettings.anisotropicFiltering = (AnisotropicFiltering)int.Parse(f[4]); QualitySettings.pixelLightCount = int.Parse(f[5]);
    QualitySettings.maximumLODLevel = int.Parse(f[6]); QualitySettings.softParticles = f[7] == "1"; QualitySettings.realtimeReflectionProbes = f[8] == "1"; }
  public static string PassInfo(string label) { return label + ": " + CapTier.Info(); }
}""" + capture_common.CAP_TIER_CS

SIG = {"Press": ["name"], "HideUi": ["on"], "Move": ["x", "z"], "SetCargo": ["v"], "Pose": ["name", "dist", "side"], "CamShot": ["w", "h", "path"], "Quality": ["mode"], "PassInfo": ["label"]}


def cs(method, *args, timeout=300, strict=True):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "E2X", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        value = m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        value = None
    if value is None or str(value).startswith("ERROR"):
        detail = str(value) if value is not None else bridge.text(m)[:800]
        if strict:
            raise RuntimeError(f"E2X.{method} failed: {detail}")
        return detail
    return value


def P(name):
    return os.path.join(OUT, name).replace("\\", "/")


def shot(tag, w=1920, h=1080):
    print("  ", tag, cs("Info"))
    print("  ", cs("CamShot", str(w), str(h), P(f"{TIER}_{tag}.png")))


def start_match():
    for _ in range(60):
        if str(cs("Press", "SoloBtn", strict=False)).startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(0.8)
    print(cs("Press", "NextBtn")); time.sleep(1.0)
    print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5)
    print(cs("Press", "StartBtn"))


def pose(name, dist, cargo, side="0", settle=1.4):
    print(cs("Pose", name, dist, side))
    print(cs("SetCargo", cargo))
    time.sleep(settle)


def cleanup():
    for label, step in (("restore quality", lambda: print(cs("Quality", "restore"))),
                        ("show UI", lambda: cs("HideUi", "0")),
                        ("stop Play mode", lambda: bridge.call("editor-application-set-state", {"isPlaying": False}))):
        try:
            step()
        except Exception as e:
            print(f"CLEANUP FAILED ({label}): {e}")
    time.sleep(4)


def main():
    try:
        print(cs("Quality", "capture"))
        bridge.call("editor-application-set-state", {"isPlaying": True})
        for _ in range(60):
            if str(cs("Press", "SoloBtn", strict=False)).startswith(("clicked",)):
                break
            time.sleep(1)
        # the menu is up: switch tier BEFORE the match so GET READY is captured on it
        if TIER == "mobile":
            print(cs("Quality", "mobile")); time.sleep(2.0)
        time.sleep(0.8)
        print(cs("Press", "NextBtn")); time.sleep(1.0)
        print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5)
        print(cs("Press", "StartBtn"))
        print(cs("PassInfo", "PASS " + TIER.upper()))
        got = False
        for _ in range(120):
            if str(cs("State", strict=False)).startswith("Active+intro"):
                time.sleep(0.45); print(cs("HideUi", "1")); shot("getready"); got = True; break
            time.sleep(0.2)
        if not got:
            print("WARNING: never saw GET READY")
        for _ in range(80):
            if str(cs("State", strict=False)) == "Active":
                break
            time.sleep(0.5)
        time.sleep(2.0)
        print(cs("FreezeBots")); print(cs("HideUi", "1"))
        print(cs("Data")); print(cs("Probe"))
        if EXPLORE:
            return
        time.sleep(1.0)
        shot("spawn")
        pose("pad", "4.5", "3"); shot("carry_pad")
        pose("pile_a", "5", "3"); shot("carry_pile_a")
        pose("pile_b", "5", "3"); shot("carry_pile_b")
        pose("pile_a", "5", "0"); shot("near_pile_a_nocargo")
        pose("pad", "8.6", "0", side="3.4"); shot("far_pad_control")
        pose("pile_a", "9.5", "0"); shot("far_pile_a_control")
    finally:
        cleanup()


if __name__ == "__main__":
    bridge.init()
    with capture_common.perf_mode():
        main()
