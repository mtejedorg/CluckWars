"""Phase 8 D3: the food mound at the MATCH camera, full / mid / low, home + centre pile, both tiers (built on capture_d1_mound.py).

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_d3_mound.py <output_dir> [<probe_png>]

Same staging as capture_d1_mound.py (solo START, bots frozen, UI hidden, local pinned to SEAT 0 so the real match camera sits
at yaw 225, Performance Mode OFF). Per tier x pile (home | centre) x fill (full 100 % | mid 50 % | low 10 %; env D3_FILLS="1,0.5,0.1,0" adds empty):
    <tier>_<pile><fill>.png            shipped frame (fill suffix: "" full, "mid", "low", "empty")
    ..._nomound.png / _all2.png        mound ShadowsOnly (mask) / repeat frame (motion noise), as in D1
    ..._nofatty.png                    full fill only (E4 Fatty vs mound)
    ..._white.png                      mound drawn with a white untextured matte Lit material (tint MPB kept): pure lighting,
                                       its darkest / brightest 30 % = the shaded / lit side of the mound
    ..._probe.png                      (only when <probe_png> is given) mound drawn UNLIT with the probe texture at the
                                       mound's unique-UV tiling (0.25), property block cleared: R = baked dip term, G = height, B = kernels
Then runs d3_mound_metrics.py on the folder. Leaves Play mode at the end.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge
import capture_common

PROBE = os.path.abspath(sys.argv[2]).replace("\\", "/") if len(sys.argv) > 2 else ""
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "d3")
os.makedirs(OUT, exist_ok=True)
W, H = 1920, 1080

CS = r"""using UnityEngine; using UnityEngine.UIElements; using UnityEditor; using System.Linq; using System.Reflection; using System.Collections.Generic; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class D1 {
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
  static ChickenController Fatty() { var l = Local(); if (l != null && l.Class == ChickenClass.Fatty) return l;
    return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k.Class == ChickenClass.Fatty); }
  public static string FreezeBots() { int n = 0; foreach (var k in ChickenController.ActiveControllers) { if (k == null || k.HasInputAuthority) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot") && m.enabled) { m.enabled = false; n++; } } return "froze " + n; }
  public static string FreezeAnim(string on) { int n = 0; foreach (var k in ChickenController.ActiveControllers) { if (k == null) continue; foreach (var a in k.GetComponentsInChildren<Animator>()) { a.speed = on == "1" ? 0f : 1f; n++; } } return "animators speed " + (on == "1" ? 0 : 1) + " x" + n; }
  public static string HideUi(string on) { foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) if (d.rootVisualElement != null) d.rootVisualElement.style.display = on == "1" ? DisplayStyle.None : DisplayStyle.Flex; return "ui hidden=" + on; }
  static FoodPile[] Piles() { return FoodPile.ActivePiles.Where(p => p != null && p.Object != null && p.Object.IsValid).ToArray(); }
  // Diagnostics only (env D1_EMPTY="r,g,b"): a per-instance FoodPileVisuals._emptyColorOverride on every live pile (runtime, never the ColorScheme
  // asset) to preview a different empty endpoint of the fill-tint lerp; the tint is re-written on the next LateUpdate.
  public static string EmptyOverride(string rgb) { var f = rgb.Split(',').Select(x => float.Parse(x, CI)).ToArray(); var c = new Color(f[0], f[1], f[2], 1f); int n = 0;
    var fo = typeof(FoodPileVisuals).GetField("_emptyColorOverride", BindingFlags.NonPublic | BindingFlags.Instance); var fl = typeof(FoodPileVisuals).GetField("_lastTintRatio", BindingFlags.NonPublic | BindingFlags.Instance);
    if (fo == null || fl == null) return "ERROR FoodPileVisuals fields not found";
    foreach (var v in Object.FindObjectsByType<FoodPileVisuals>(FindObjectsSortMode.None)) { fo.SetValue(v, c); fl.SetValue(v, float.NaN); n++; }
    return "empty override " + c + " on " + n + " piles -> floor " + FoodPileVisuals.TintFor(c, new Color(1f, 0.85f, 0.3f), 0f); }
  public static string FillPiles() { string o = ""; foreach (var p in Piles()) { if (p.Object.HasStateAuthority) p.Amount = p.MaxAmount; o += p.name + "=" + p.Amount.ToString("0.#", CI) + "/" + p.MaxAmount.ToString("0.#", CI) + " "; } return o; }
  static FoodPile Subject(string which) { var l = Local(); var ps = Piles();
    if (which == "centre" || which == "centrelow") return ps.OrderBy(p => p.transform.position.sqrMagnitude).First();
    var home = Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None).First(b => b.CornerIndex == 0);
    return ps.Where(p => p.transform.position.sqrMagnitude > 1f).OrderBy(p => (p.transform.position - home.transform.position).sqrMagnitude).First(); }
  static void Put(ChickenController k, Vector3 pos) { pos.y = k.transform.position.y; var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else k.transform.position = pos; }
  // Local beside the pile on the camera side; the Fatty (if it is a bot) beside the pile 40 deg further round, so both read in one frame.
  // The seat is shuffled per match and the match camera's yaw follows the local seat (MatchCamera: bearing from the local chicken's HomeCornerIndex
  // to the centre), which would change which faces are sunlit run to run. The capture pins the local chicken to SEAT 0 (HomeCornerIndex = 0, runtime
  // only, bots frozen, the match is thrown away) so the REAL match camera turns to seat 0's yaw (225 deg) and the billboards follow it.
  public const float Seat0Yaw = 225f;
  // The Fatty faces the camera, turned 30 deg to show its 3/4 (front + flank), so the body read does not depend on where the bot was walking.
  public const float FattyTurn = 30f;
  // "centrelow": the centre pile at 10 % (below FoodPileVisuals.TintFloor, so the floor colour shows) to check the darker tile keeps the low state readable.
  public const float LowFill = 0.1f;
  static Vector3 Seat0Fwd() { return Quaternion.Euler(0f, Seat0Yaw, 0f) * Vector3.forward; }
  public static string PinSeat0() { var l = Local(); if (!l.Object.HasStateAuthority) return "ERROR local has no state authority"; int was = l.HomeCornerIndex; l.HomeCornerIndex = 0; return "local HomeCornerIndex " + was + " -> 0"; }
  public static string Stage(string which, string frac) { var l = Local(); var pile = Subject(which); var fwd = Seat0Fwd();
    pile.Amount = pile.MaxAmount * float.Parse(frac, CI);   // runtime only (the match is thrown away)
    var side = Vector3.Cross(Vector3.up, fwd);
    Put(l, pile.SurfaceApproachPoint(pile.transform.position - (fwd + side * 0.9f).normalized * 100f, 1.3f));
    var f = Fatty(); string fs = "no Fatty in the match";
    if (f != null && f != l) { Put(f, pile.SurfaceApproachPoint(pile.transform.position - (fwd - side * 0.9f).normalized * 100f, 1.3f)); f.transform.rotation = Quaternion.Euler(0f, Seat0Yaw + 180f + FattyTurn, 0f); fs = "Fatty bot " + f.name + " facing " + (Seat0Yaw + 180f + FattyTurn) + " deg"; }
    else if (f == l) fs = "local is Fatty";
    SessionState.SetInt("d1_pile_id", pile.GetInstanceID());
    return "pile " + pile.name + " pos " + pile.transform.position.ToString("F2") + " amount " + pile.Amount + "/" + pile.MaxAmount + " centre=" + pile.IsCentre + " | local " + l.Class + " | " + fs; }
  static Renderer[] Targets(string mode) { int id = SessionState.GetInt("d1_pile_id", 0); var pile = Piles().FirstOrDefault(p => p.GetInstanceID() == id); if (pile == null) throw new System.InvalidOperationException("staged pile " + id + " gone");
    if (mode == "nomound") { var m = pile.transform.Find("Mound/MoundMesh"); if (m == null) throw new System.InvalidOperationException("no Mound/MoundMesh on " + pile.name); return new[] { m.GetComponent<Renderer>() }; }
    if (mode == "nofatty") { var f = Fatty(); if (f == null) return new Renderer[0]; var v = f.GetComponentInChildren<ChickenVisuals>(); var rs = v != null && v.TintedRenderers != null ? v.TintedRenderers : f.GetComponentsInChildren<Renderer>(); return rs.Where(r => r != null && r.enabled).ToArray(); }
    return new Renderer[0]; }
  static Renderer MoundR() { int id = SessionState.GetInt("d1_pile_id", 0); var pile = Piles().First(p => p.GetInstanceID() == id); return pile.transform.Find("Mound/MoundMesh").GetComponent<Renderer>(); }
  public static string Shot(string mode, string path, string probe) { var ts = (mode == "white" || mode == "probe") ? new Renderer[0] : Targets(mode); var old = ts.Select(r => r.shadowCastingMode).ToArray();
    foreach (var r in ts) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    Renderer mr = null; Material[] keep = null; MaterialPropertyBlock mpb = null; Material tmp = null; Texture2D ptex = null;
    if (mode == "white" || mode == "probe") { mr = MoundR(); keep = mr.sharedMaterials; var ms = (Material[])keep.Clone();
      if (mode == "white") { tmp = new Material(keep[0]); tmp.SetTexture("_BaseMap", null); tmp.SetTexture("_MainTex", null); }
      else { ptex = new Texture2D(2, 2, TextureFormat.RGB24, false, true); ptex.LoadImage(System.IO.File.ReadAllBytes(probe)); ptex.wrapMode = TextureWrapMode.Clamp;
        tmp = new Material(Shader.Find("Universal Render Pipeline/Unlit")); tmp.SetTexture("_BaseMap", ptex); tmp.SetTextureScale("_BaseMap", new Vector2(0.25f, 0.25f)); tmp.SetColor("_BaseColor", Color.white);
        mpb = new MaterialPropertyBlock(); mr.GetPropertyBlock(mpb); mr.SetPropertyBlock(null); }
      ms[0] = tmp; mr.sharedMaterials = ms; }
    try { var cam = MatchCamera.Instance.Camera; var rt = CapTier.NewRt(""" + str(W) + ", " + str(H) + r"""); var prevT = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prevT;
      var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
      System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
    finally { for (int i = 0; i < ts.Length; i++) ts[i].shadowCastingMode = old[i];
      if (mr != null) { mr.sharedMaterials = keep; if (mpb != null) mr.SetPropertyBlock(mpb); Object.DestroyImmediate(tmp); if (ptex != null) Object.DestroyImmediate(ptex); } }
    var mc = MatchCamera.Instance.Camera; float dy = Mathf.Abs(Mathf.DeltaAngle(mc.transform.eulerAngles.y, Seat0Yaw)); if (dy > 1f) throw new System.InvalidOperationException("match camera yaw " + mc.transform.eulerAngles.y + " not at seat 0 (" + Seat0Yaw + ")");
    return "saved " + path + " matchCam yaw " + mc.transform.eulerAngles.y.ToString("0.0", CI) + " ortho " + mc.orthographicSize.ToString("0.00", CI) + " (" + mode + ": " + string.Join(",", ts.Select(r => r.name)) + ")"; }
  // Tier switch, same scheme as capture_env3d.py E3.Quality (Mobile level is not selectable in Play mode on Standalone: snapshot it in Edit mode).
  static string Snap() { var rp = QualitySettings.renderPipeline; return string.Join("|", new[] { AssetDatabase.GetAssetPath(rp), QualitySettings.vSyncCount.ToString(), QualitySettings.lodBias.ToString("R", CI),
      ((int)QualitySettings.skinWeights).ToString(), ((int)QualitySettings.anisotropicFiltering).ToString(), QualitySettings.pixelLightCount.ToString(), QualitySettings.maximumLODLevel.ToString(),
      QualitySettings.softParticles ? "1" : "0", QualitySettings.realtimeReflectionProbes ? "1" : "0" }); }
  static void ApplySnap(string snap) { var f = snap.Split('|');
    QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(f[0]); QualitySettings.vSyncCount = int.Parse(f[1]); QualitySettings.lodBias = float.Parse(f[2], CI);
    QualitySettings.skinWeights = (SkinWeights)int.Parse(f[3]); QualitySettings.anisotropicFiltering = (AnisotropicFiltering)int.Parse(f[4]); QualitySettings.pixelLightCount = int.Parse(f[5]);
    QualitySettings.maximumLODLevel = int.Parse(f[6]); QualitySettings.softParticles = f[7] == "1"; QualitySettings.realtimeReflectionProbes = f[8] == "1"; }
  public static string Quality(string mode) { int mob = System.Array.IndexOf(QualitySettings.names, "Mobile");
    if (mode == "capture") { if (mob < 0) return "ERROR no Mobile level (Play mode?)"; int prev = QualitySettings.GetQualityLevel(); SessionState.SetString("d1_pc_snap", Snap()); SessionState.SetString("d1_prev_level", QualitySettings.names[prev]);
      try { QualitySettings.SetQualityLevel(mob, true); SessionState.SetString("d1_mobile_snap", Snap()); } finally { QualitySettings.SetQualityLevel(prev, true); } return "snapshotted Mobile"; }
    if (mode == "mobile") { if (mob >= 0) QualitySettings.SetQualityLevel(mob, true); else ApplySnap(SessionState.GetString("d1_mobile_snap", "")); return "mobile rp=" + QualitySettings.renderPipeline.name; }
    string pl = SessionState.GetString("d1_prev_level", ""); if (pl != "") { int i = System.Array.IndexOf(QualitySettings.names, pl); if (i >= 0) QualitySettings.SetQualityLevel(i, true); }
    string pc = SessionState.GetString("d1_pc_snap", ""); if (pc != "") ApplySnap(pc); return "restored rp=" + QualitySettings.renderPipeline.name; }
  public static string Info() { return CapTier.Info(); }
}""" + capture_common.CAP_TIER_CS

SIG = {"EmptyOverride": ["rgb"], "Press": ["name"], "HideUi": ["on"], "FreezeAnim": ["on"], "Stage": ["which", "frac"], "Shot": ["mode", "path", "probe"], "Quality": ["mode"]}


def cs(method, *args, strict=True):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "D1", "methodName": method, "parameters": params}, timeout=180)
    try:
        v = m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        v = None
    if v is None or str(v).startswith("ERROR"):
        if strict:
            raise RuntimeError(f"D1.{method} failed: " + (str(v) if v is not None else bridge.text(m)[:800]))
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


FILL_TAGS = {1.0: "", 0.5: "mid", 0.1: "low", 0.0: "empty"}
# env D3_FILLS="1,0.5,0.1,0" picks the fills (default full / mid / low; 0 = empty -> the food_mound_empty mesh swap)
FILLS = tuple((FILL_TAGS.get(float(f), "f" + f.strip().replace(".", "")), float(f)) for f in os.environ.get("D3_FILLS", "1,0.5,0.1").split(","))


def shoot(tier):
    print(f"PASS {tier}: " + cs("Info"))
    for which in ("home", "centre"):
        for tag, frac in FILLS:
            print(cs("FillPiles"))
            print(cs("FreezeAnim", "0")); print(cs("Stage", which, frac)); time.sleep(1.6); print(cs("FreezeAnim", "1")); time.sleep(0.3)
            modes = ["all", "nomound", "white"] + (["nofatty"] if frac == 1.0 else []) + (["probe"] if PROBE else []) + ["all2"]
            for mode in modes:
                suffix = "" if mode == "all" else "_" + mode
                print(cs("Shot", mode, P(f"{tier}_{which}{tag}{suffix}.png"), PROBE))


def main():
    try:
        print(cs("Quality", "capture"))
        start(); time.sleep(2.0)
        print(cs("FreezeBots")); print(cs("HideUi", "1")); print(cs("PinSeat0")); time.sleep(2.0)
        if os.environ.get("D1_EMPTY"):
            print(cs("EmptyOverride", os.environ["D1_EMPTY"]))
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
    with capture_common.perf_mode():
        main()
    import subprocess
    subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "d3_mound_metrics.py"), OUT], check=False)
