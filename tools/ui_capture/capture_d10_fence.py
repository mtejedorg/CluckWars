"""Phase 8 D10 captures: the fence hole's CORE is fully clear (no Bayer survivors over the chicken's legs / ring).

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_d10_fence.py <output_dir> pc|mobile

Solo START, bots frozen, UI hidden, MATCH camera rendered into a RenderTexture (same as capture_e2_fence), Performance Mode OFF
(capture_common.perf_mode). Shots, per tier:
  getready      the intro countdown (GET READY): local chicken at spawn behind its near fence / coop
  spawn         Active, chicken still at spawn
  rival         a rival behind the near OUTER fence next to the local bird (D4 hole)
  carry_pad     carrying food, own pad behind the near fence (E2 fixed-target hole)
For every shot and every FULLY open hole (strength >= 0.999: the local bird, D4 chicken slots, E2 fixed targets) it counts the
surviving FENCE pixels inside the hole's inner radius: the frame is rendered twice in the same editor tick, once as shipped and
once with every fence-fade renderer switched to ShadowsOnly (fence gone, its shadow kept); a pixel inside the inner radius that
differs is fence that survived the clip. The per-pixel Bayer cell histogram ((x mod 4) + 4 (y mod 4)) separates a dither
survivor (concentrated in one cell) from solid fence behind the hole's subject (spread over all 16). Writes
<tier>_<shot>.png, <tier>_<shot>__core_<hole>.png (4x nearest-neighbour crop of the core: shipped | survivors in red) and
<tier>_core_counts.json. Leaves Play mode (and the quality level) as it found them.
"""
import sys, os, time, json
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge
import capture_common

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
TIER = sys.argv[2] if len(sys.argv) > 2 else "pc"
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using UnityEngine.Rendering; using UnityEditor; using System.Linq; using System.Reflection; using System.Text; using System.Collections.Generic; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class D10X {
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
  static ChickenController[] Rivals() { var l = Local(); return ChickenController.ActiveControllers.Where(k => k != null && k != l && k.Object != null && k.Object.IsValid).OrderBy(k => k.name).ToArray(); }
  public static string FreezeBots() { var l = Local(); foreach (var k in ChickenController.ActiveControllers) { if (k == null || k == l) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot")) m.enabled = false; } return "froze bots"; }
  public static string HideUi(string on) { foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) if (d.rootVisualElement != null) d.rootVisualElement.style.display = on == "1" ? DisplayStyle.None : DisplayStyle.Flex; return "ui hidden=" + on; }
  static void Put(ChickenController k, Vector3 pos) { pos.y = k.transform.position.y; var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else k.transform.position = pos; }
  public static string MoveLocal(string x, string z) { var l = Local(); if (l == null) return "ERROR no local"; Put(l, new Vector3(float.Parse(x, CI), 0, float.Parse(z, CI))); return "local -> " + x + "," + z; }
  public static string MoveRival(string i, string x, string z) { var r = Rivals(); int k = int.Parse(i); if (k >= r.Length) return "ERROR no rival " + i; Put(r[k], new Vector3(float.Parse(x, CI), 0, float.Parse(z, CI))); return r[k].name + " -> " + x + "," + z; }
  public static string Park() { var r = Rivals(); for (int k = 0; k < r.Length; k++) Put(r[k], new Vector3(-3f + 3f * k, 0, 0)); return "parked " + r.Length; }
  public static string LocalXZ() { var l = Local(); return l == null ? "ERROR no local" : l.transform.position.x.ToString("0.00", CI) + ";" + l.transform.position.z.ToString("0.00", CI); }
  public static string SetCargo(string v) { var l = Local(); if (l == null) return "ERROR no local"; var c = l.GetComponent<ChickenCargo>(); if (c == null) return "ERROR no cargo"; c.Cargo = float.Parse(v, CI); return "cargo=" + c.Cargo.ToString("0.0", CI); }
  static PlayerBase OwnBase() { var l = Local(); if (l == null) return null; return PlayerBase.ActiveBases.FirstOrDefault(b => b != null && BaseDepositRules.IsAllegianceMatch(l.HomeCornerIndex, b.CornerIndex)); }
  static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
  static Vector3 CamForward() { var f = MatchCamera.Instance.Camera.transform.forward; f.y = 0; return f.normalized; }
  public static string PadPose(string dist) { var l = Local(); var pad = OwnBase(); if (l == null || pad == null) return "ERROR no local/pad";
    var spot = Flat(pad.transform.position) + CamForward() * float.Parse(dist, CI); Put(l, spot); return "local -> " + spot.ToString("F2") + " (pad " + pad.transform.position.ToString("F2") + ")"; }
  public static string Info() { var d = Object.FindFirstObjectByType<FenceFadeDriver>(); var l = Local(); var cg = l ? l.GetComponent<ChickenCargo>() : null;
    var sb = new StringBuilder("local=" + (l ? l.transform.position.ToString("F2") : "-") + " cargo=" + (cg ? cg.Cargo.ToString("F1", CI) : "-") + " state=" + State() + " kw=" + Shader.IsKeywordEnabled("_CW_FENCE_FADE")
      + " localStrength=" + (d ? d.Strength.ToString("0.00", CI) : "-") + " slots=" + (d ? d.SlotCount.ToString() : "-") + " maxSlot=" + (d ? d.MaxSlotStrength.ToString("0.00", CI) : "-") + " targets=" + (d ? d.FixedTargetCount.ToString() : "-"));
    if (d) for (int i = 0; i < d.FixedTargetCount; i++) sb.Append(" t" + i + "=" + d.FixedTargetStrength(i).ToString("0.00", CI));
    sb.Append(" params=" + Shader.GetGlobalVector("_CW_FenceFadeParams").ToString("F3")); return sb.ToString(); }

  static Texture2D Grab(Camera cam, int W, int H) {
    var rt = CapTier.NewRt(W, H); var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    rt.Release(); Object.DestroyImmediate(rt); return tex; }
  struct Hole { public string Label; public Vector3 Pos; public float Strength, Shift; }
  static List<Hole> Holes() { var list = new List<Hole>();
    var lp = Shader.GetGlobalVector("_CW_LocalBirdPos"); if (lp.w > 0f) list.Add(new Hole { Label = "local", Pos = lp, Strength = lp.w, Shift = 0f });
    int nb = Mathf.RoundToInt(Shader.GetGlobalFloat("_CW_FenceBirdCount")); var birds = Shader.GetGlobalVectorArray("_CW_FenceBirds");
    for (int i = 0; birds != null && i < nb && i < birds.Length; i++) list.Add(new Hole { Label = "bird" + i, Pos = birds[i], Strength = birds[i].w, Shift = 0f });
    int nt = Mathf.RoundToInt(Shader.GetGlobalFloat("_CW_FenceTargetCount")); var tg = Shader.GetGlobalVectorArray("_CW_FenceTargets"); var sh = Shader.GetGlobalVector("_CW_FenceTargetShift");
    for (int i = 0; tg != null && i < nt && i < tg.Length; i++) list.Add(new Hole { Label = "target" + i, Pos = tg[i], Strength = tg[i].w, Shift = sh[i] });
    return list; }
  static bool Differs(Color32 a, Color32 b) { return Mathf.Abs(a.r - b.r) > 6 || Mathf.Abs(a.g - b.g) > 6 || Mathf.Abs(a.b - b.b) > 6; }
  // Renders the shipped frame and a fence-less frame in the same tick; counts fence survivors inside every fully open hole's inner radius.
  public static string Core(string w, string h, string prefix) {
    var cam = MatchCamera.Instance.Camera; int W = int.Parse(w), H = int.Parse(h);
    var a = Grab(cam, W, H);
    var fences = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == FenceFadeRules.ShaderName).ToList();
    var modes = fences.Select(r => r.shadowCastingMode).ToArray(); Texture2D b;
    try { foreach (var r in fences) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; b = Grab(cam, W, H); }
    finally { for (int i = 0; i < fences.Count; i++) fences[i].shadowCastingMode = modes[i]; }
    System.IO.File.WriteAllBytes(prefix + ".png", a.EncodeToPNG());
    var pa = a.GetPixels32(); var pb = b.GetPixels32();
    var sb = new StringBuilder("["); bool first = true;
    foreach (var hole in Holes()) {
      var vp = cam.WorldToViewportPoint(hole.Pos); float cx = vp.x * W, cy = vp.y * H, rad = (FenceFadeRules.InnerRadius + hole.Shift) * H;
      int inside = 0, fence = 0; var hist = new int[16];
      int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rad)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + rad)), y0 = Mathf.Max(0, Mathf.FloorToInt(cy - rad)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + rad));
      for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) { float dx = x + 0.5f - cx, dy = y + 0.5f - cy; if (dx * dx + dy * dy > rad * rad) continue; inside++;
        if (Differs(pa[y * W + x], pb[y * W + x])) { fence++; hist[(x & 3) + 4 * (y & 3)]++; } }
      int min = hist.Min(), max = hist.Max(); int excess = hist.Sum() - 16 * min;
      // 4x crop of the core (a square inscribed in the inner circle): shipped | survivors in red over the darkened frame.
      int half = Mathf.Max(8, Mathf.Min(120, Mathf.FloorToInt(rad / 1.4142f))); int s = 2 * half; const int Z = 4;
      var crop = new Texture2D(2 * s * Z + 8, s * Z, TextureFormat.RGB24, false); var cp = new Color32[crop.width * crop.height];
      for (int i = 0; i < cp.Length; i++) cp[i] = new Color32(255, 255, 255, 255);
      for (int yy = 0; yy < s; yy++) for (int xx = 0; xx < s; xx++) { int sx = Mathf.RoundToInt(cx) - half + xx, sy = Mathf.RoundToInt(cy) - half + yy; Color32 c1, c2;
        if (sx < 0 || sy < 0 || sx >= W || sy >= H) { c1 = c2 = new Color32(0, 0, 0, 255); }
        else { c1 = pa[sy * W + sx]; c2 = Differs(pa[sy * W + sx], pb[sy * W + sx]) ? new Color32(255, 0, 0, 255) : new Color32((byte)(c1.r / 3), (byte)(c1.g / 3), (byte)(c1.b / 3), 255); }
        for (int zy = 0; zy < Z; zy++) for (int zx = 0; zx < Z; zx++) { int py = yy * Z + zy; cp[py * crop.width + xx * Z + zx] = c1; cp[py * crop.width + s * Z + 8 + xx * Z + zx] = c2; } }
      crop.SetPixels32(cp); crop.Apply(); System.IO.File.WriteAllBytes(prefix + "__core_" + hole.Label + ".png", crop.EncodeToPNG()); Object.DestroyImmediate(crop);
      if (!first) sb.Append(","); first = false;
      sb.Append("{\"hole\":\"" + hole.Label + "\",\"strength\":" + hole.Strength.ToString("0.000", CI) + ",\"shift\":" + hole.Shift.ToString("0.000", CI) + ",\"centrePx\":[" + cx.ToString("0.0", CI) + "," + cy.ToString("0.0", CI) + "]"
        + ",\"innerRadiusPx\":" + rad.ToString("0.0", CI) + ",\"onScreen\":" + (vp.z > 0 && cx >= 0 && cy >= 0 && cx < W && cy < H ? "true" : "false") + ",\"pixelsInside\":" + inside + ",\"fencePixelsInside\":" + fence
        + ",\"bayerCellMin\":" + min + ",\"bayerCellMax\":" + max + ",\"stippleExcess\":" + excess + ",\"bayerHist\":[" + string.Join(",", hist) + "]}"); }
    Object.DestroyImmediate(a); Object.DestroyImmediate(b);
    return sb.Append("]").ToString(); }
  public static string Quality(string mode) {
    string how = ""; int mob = System.Array.IndexOf(QualitySettings.names, "Mobile");
    if (mode == "capture") { if (mob < 0) return "ERROR no Mobile quality level"; int prev = QualitySettings.GetQualityLevel();
      SessionState.SetString("e3_pc_snap", Snap());
      try { QualitySettings.SetQualityLevel(mob, true); SessionState.SetString("e3_mobile_snap", Snap()); } finally { QualitySettings.SetQualityLevel(prev, true); } how = "snapshotted Mobile level"; }
    else if (mode == "mobile") { if (SessionState.GetString("e3_prev_level", "") == "") SessionState.SetString("e3_prev_level", QualitySettings.names[QualitySettings.GetQualityLevel()]);
      if (mob >= 0) { QualitySettings.SetQualityLevel(mob, true); SessionState.SetString("e3_tier", ""); how = "SetQualityLevel(" + mob + ")"; }
      else { string snap = SessionState.GetString("e3_mobile_snap", ""); if (snap == "") return "ERROR no Mobile snapshot"; ApplySnap(snap); SessionState.SetString("e3_tier", "Mobile"); how = "Mobile snapshot applied"; } }
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

SIG = {"Press": ["name"], "HideUi": ["on"], "MoveLocal": ["x", "z"], "MoveRival": ["i", "x", "z"], "SetCargo": ["v"], "PadPose": ["dist"],
       "Core": ["w", "h", "prefix"], "Quality": ["mode"], "PassInfo": ["label"]}


def cs(method, *args, timeout=300, strict=True):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "D10X", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        value = m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        value = None
    if value is None or str(value).startswith("ERROR"):
        detail = str(value) if value is not None else bridge.text(m)[:800]
        if strict:
            raise RuntimeError(f"D10X.{method} failed: {detail}")
        return detail
    return value


RESULTS = {}


def shot(tag, w=1920, h=1080):
    info = cs("Info")
    print("  ", tag, info)
    prefix = os.path.join(OUT, f"{TIER}_{tag}").replace("\\", "/")
    holes = json.loads(cs("Core", str(w), str(h), prefix))
    RESULTS[tag] = {"info": info, "holes": holes}
    for hd in holes:
        print(f"      {hd['hole']:8s} s={hd['strength']:.2f} r={hd['innerRadiusPx']:.0f}px inside={hd['pixelsInside']} fence={hd['fencePixelsInside']} "
              f"stippleExcess={hd['stippleExcess']} cells min/max={hd['bayerCellMin']}/{hd['bayerCellMax']}")


ROT = 0


def R(x, z):
    """Poses authored for the SE base (21.8, -21.8); quarter-turn to the real spawn corner (map is 4-fold symmetric)."""
    x, z = float(x), float(z)
    for _ in range(ROT):
        x, z = -z, x
    return f"{x:.2f}", f"{z:.2f}"


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
    global ROT
    try:
        print(cs("Quality", "capture"))
        bridge.call("editor-application-set-state", {"isPlaying": True})
        for _ in range(60):
            if str(cs("Press", "SoloBtn", strict=False)).startswith("clicked"):
                break
            time.sleep(1)
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
                time.sleep(0.6); print(cs("HideUi", "1")); shot("getready"); got = True; break
            time.sleep(0.2)
        if not got:
            print("WARNING: never saw GET READY")
        for _ in range(80):
            if str(cs("State", strict=False)) == "Active":
                break
            time.sleep(0.5)
        time.sleep(2.0)
        print(cs("FreezeBots")); print(cs("HideUi", "1"))
        lx, lz = [float(v) for v in str(cs("LocalXZ")).split(";")]
        corner = (1 if lx > 0 else -1, 1 if lz > 0 else -1)
        ROT = [(1, -1), (1, 1), (-1, 1), (-1, -1)].index(corner)
        print("spawn corner", corner, "quarter turns", ROT)
        time.sleep(1.0)
        shot("spawn")
        # D4: a rival right behind the near SOUTH run (authored SE frame), the local bird out in the open next to it.
        print(cs("Park")); print(cs("MoveLocal", *R("18", "-20"))); print(cs("MoveRival", "0", *R("16", "-24.2"))); time.sleep(1.3)
        shot("rival")
        print(cs("Park"))
        print(cs("PadPose", "4.5")); print(cs("SetCargo", "3")); time.sleep(1.4)
        shot("carry_pad")
        print(cs("SetCargo", "0"))
    finally:
        with open(os.path.join(OUT, f"{TIER}_core_counts.json"), "w", encoding="utf-8") as f:
            json.dump({"tier": TIER, "shots": RESULTS}, f, indent=1)
        cleanup()


if __name__ == "__main__":
    bridge.init()
    with capture_common.perf_mode():
        main()
