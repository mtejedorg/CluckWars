"""3D environment audit captures: match-camera views, free-camera overviews and close-ups of props / piles / bases /
chickens, plus a JSON inventory of the loaded scenes (renderers, tris, materials, shaders, textures, lights, URP settings).

    python tools/ui_capture/capture_env3d.py <outdir>

Runs a solo match (bots frozen once Active) so the arena is fully spawned.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Captures", "env3d"))
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using UnityEditor; using System.Linq; using System.Reflection; using System.Text; using System.Collections.Generic; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class E3 {
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
  public static string Move(string x, string z) { var l = Local(); if (l == null) return "no local";
    var pos = new Vector3(float.Parse(x, CI), l.transform.position.y, float.Parse(z, CI)); var nt = l.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else l.transform.position = pos; return "local -> " + pos; }
  public static string FreezeBots() { string o = ""; foreach (var k in ChickenController.ActiveControllers) { if (k == null || k.HasInputAuthority) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot") && m.enabled) { m.enabled = false; o += m.GetType().Name + " "; } } return "froze " + o; }
  public static string HideUi(string on) { foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) if (d.rootVisualElement != null) d.rootVisualElement.style.display = on == "1" ? DisplayStyle.None : DisplayStyle.Flex; return "ui hidden=" + on; }

  static void Save(Camera cam, int W, int H, string path) {
    var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
  public static string CamShot(string w, string h, string path) { Save(MatchCamera.Instance.Camera, int.Parse(w), int.Parse(h), path); return "saved " + path; }

  static Camera Free() { var go = GameObject.Find("__E3Cam"); if (go == null) { go = new GameObject("__E3Cam"); var c = go.AddComponent<Camera>(); c.enabled = false;
      var src = MatchCamera.Instance.Camera; c.CopyFrom(src); c.targetTexture = null;
      var srcData = src.GetComponent("UniversalAdditionalCameraData"); var dst = go.AddComponent(srcData.GetType());
      foreach (var p in new[] { "renderPostProcessing", "antialiasing" }) { var pi = srcData.GetType().GetProperty(p); if (pi != null) pi.SetValue(dst, pi.GetValue(srcData)); } }
    var cam = go.GetComponent<Camera>(); cam.enabled = false; return cam; }
  // Shot from a direction (yaw/pitch degrees) framing a world bounds.
  static string Frame(Bounds b, float yaw, float pitch, float pad, int W, int H, string path, bool ortho) {
    var cam = Free(); cam.orthographic = ortho; cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 500f; cam.aspect = (float)W / H;
    var rot = Quaternion.Euler(pitch, yaw, 0f); float r = b.extents.magnitude * pad;
    if (ortho) { cam.orthographicSize = r; cam.transform.position = b.center - rot * Vector3.forward * 200f; }
    else { float d = r / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Min(1f, cam.aspect); cam.transform.position = b.center - rot * Vector3.forward * d; }
    cam.transform.rotation = rot; Save(cam, W, H, path); return "saved " + path; }
  static Bounds BoundsOf(GameObject g) { var rs = g.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray(); if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.one);
    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
  static Bounds Arena() { var plane = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).FirstOrDefault(r => r.name == "GeneratedGround");
    return plane != null ? plane.bounds : new Bounds(Vector3.zero, new Vector3(50, 1, 50)); }

  public static string Overviews(string dir) { var sb = new StringBuilder(); var a = Arena(); sb.Append("arena=" + a + "\n");
    sb.Append(Frame(a, 0, 90, 0.75f, 2048, 2048, dir + "/ov_topdown.png", true) + "\n");
    for (int i = 0; i < 4; i++) sb.Append(Frame(a, 45 + 90 * i, 38, 0.62f, 1920, 1080, dir + "/ov_threequarter_" + i + ".png", false) + "\n");
    sb.Append(Frame(a, 0, 12, 0.6f, 1920, 1080, dir + "/ov_lowangle.png", false) + "\n");
    var big = a; big.Expand(70f); sb.Append(Frame(big, 30, 30, 0.6f, 1920, 1080, dir + "/ov_wide_with_surround.png", false) + "\n");
    return sb.ToString(); }

  public static string Subjects(string dir) { var sb = new StringBuilder(); int n = 0;
    foreach (var p in Object.FindObjectsByType<FoodPile>(FindObjectsSortMode.None).OrderBy(p => p.transform.position.sqrMagnitude).Take(3)) { var b = BoundsOf(p.gameObject); b.Expand(1.5f);
      sb.Append(Frame(b, 35, 32, 0.9f, 1600, 1000, dir + "/pile_" + (n++) + "_" + p.name + ".png", false) + "\n"); }
    n = 0; foreach (var p in Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None)) { var b = BoundsOf(p.gameObject); b.Expand(2f);
      sb.Append(Frame(b, 225 - 90 * n, 35, 0.9f, 1600, 1000, dir + "/base_" + (n++) + "_" + p.name + ".png", false) + "\n"); }
    // props: one per unique name stem (strip "(1)", digits)
    var seen = new HashSet<string>(); var props = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Select(r => r.transform).Select(t => { while (t.parent != null && t.parent.GetComponent<MeshRenderer>() != null) t = t.parent; return t; }).Distinct();
    foreach (var t in props) { string stem = System.Text.RegularExpressions.Regex.Replace(t.name, @"[\s_]*(\(\d+\)|\d+)$", "");
      if (System.Array.IndexOf(PropNames, stem) < 0) continue;
      if (!seen.Add(stem)) continue; var b = BoundsOf(t.gameObject); if (b.size.magnitude > 25f) continue; b.Expand(0.6f);
      sb.Append(Frame(b, 30, 25, 0.85f, 1200, 900, dir + "/prop_" + stem.Replace(" ", "_") + ".png", false) + "\n"); }
    n = 0; foreach (var k in ChickenController.ActiveControllers.Where(k => k != null && !k.IsDecoy)) { var b = BoundsOf(k.gameObject); string nm = "chicken_" + (n++) + "_" + k.name.Replace(" ", "_").Replace("(", "").Replace(")", "");
      foreach (var v in new[] { new[] { 0f, 8f, 0f }, new[] { 90f, 8f, 1f }, new[] { 180f, 8f, 2f }, new[] { 40f, 55f, 3f } }) {
        float yaw = k.transform.eulerAngles.y + 180f + v[0]; sb.Append(Frame(b, yaw, v[1], 1.15f, 1000, 1000, dir + "/" + nm + "_v" + (int)v[2] + ".png", false) + "\n"); } }
    // all chickens together, gameplay-ish pitch
    var all = ChickenController.ActiveControllers.Where(k => k != null && !k.IsDecoy).ToList(); if (all.Count > 0) { var lb = BoundsOf(all[0].gameObject); foreach (var k in all) lb.Encapsulate(BoundsOf(k.gameObject)); sb.Append(Frame(lb, 0, 50, 0.7f, 1920, 1080, dir + "/chickens_group.png", false) + "\n"); }
    return sb.ToString(); }

  static string[] PropNames = { "wall_segment", "oak_tree", "hay_bale", "barrel", "bush_round", "nest_basket", "crate", "feed_sack", "milk_churn", "chicken_coop", "feed_silo", "scarecrow", "wheelbarrow", "trough", "big_barn", "Hedge_N", "Cube" };
  public static string PropsOnly(string dir) { var sb = new StringBuilder(); var seen = new HashSet<string>();
    foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && !(r is ParticleSystemRenderer))) { string stem = System.Text.RegularExpressions.Regex.Replace(r.name, @"[\s_]*(\(\d+\)|\d+)$", "");
      if (System.Array.IndexOf(PropNames, stem) < 0 || !seen.Add(stem)) continue; var b = r.bounds; if (b.size.magnitude > 40f) continue; b.Expand(0.8f);
      sb.Append(Frame(b, 30, 25, 0.9f, 1200, 900, dir + "/prop_" + stem + "_" + (r.transform.parent ? r.transform.parent.name : "") + ".png", false) + "\n"); }
    return sb.ToString(); }
  public static string Assets(string path) { var sb = new StringBuilder();
    foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Generated", "Assets/_Game" })) { var ap = AssetDatabase.GUIDToAssetPath(guid); var go = AssetDatabase.LoadAssetAtPath<GameObject>(ap); if (go == null) continue; int tris = 0, verts = 0, sub = 0;
      foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) { tris += mf.sharedMesh.triangles.Length / 3; verts += mf.sharedMesh.vertexCount; sub += mf.sharedMesh.subMeshCount; }
      foreach (var sm in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (sm.sharedMesh) { tris += sm.sharedMesh.triangles.Length / 3; verts += sm.sharedMesh.vertexCount; sub += sm.sharedMesh.subMeshCount; }
      var imp = AssetImporter.GetAtPath(ap) as ModelImporter; sb.Append(ap + " tris=" + tris + " verts=" + verts + " sub=" + sub + (imp != null ? " scale=" + imp.globalScale + " readable=" + imp.isReadable + " meshComp=" + imp.meshCompression + " optimize=" + imp.optimizeMeshVertices + " rig=" + imp.animationType + " normals=" + imp.importNormals : "") + "\n"); }
    foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Generated", "Assets/_Game/Art" })) { var ap = AssetDatabase.GUIDToAssetPath(guid); var ti = AssetImporter.GetAtPath(ap) as TextureImporter; var t = AssetDatabase.LoadAssetAtPath<Texture2D>(ap); if (t == null || ap.Contains("/UI/")) continue;
      var andr = ti != null ? ti.GetPlatformTextureSettings("Android") : null; sb.Append("TEX " + ap + " " + t.width + "x" + t.height + " fmt=" + t.format + " mips=" + t.mipmapCount + (ti != null ? " max=" + ti.maxTextureSize + " android=" + (andr.overridden ? andr.format + "/" + andr.maxTextureSize : "default") + " crunch=" + ti.crunchedCompression : "") + "\n"); }
    System.IO.File.WriteAllText(path, sb.ToString()); return "assets " + path; }
  public static string LineUp() { var all = ChickenController.ActiveControllers.Where(k => k != null && !k.IsDecoy).ToList(); int i = 0;
    foreach (var k in all) { var pos = new Vector3(-3f + 2f * i++, k.transform.position.y, -2f); var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else k.transform.position = pos; k.transform.rotation = Quaternion.Euler(0, 180, 0); }
    return "lined " + all.Count; }

  public static string Inventory(string path) { var sb = new StringBuilder(); sb.Append("{\n");
    var rp = QualitySettings.renderPipeline; sb.Append("\"quality\":\"" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "\",\"rp\":\"" + (rp ? rp.name : "null") + "\",\n");
    if (rp != null) { var t = rp.GetType(); sb.Append("\"urp\":{");
      foreach (var p in new[] { "shadowDistance", "shadowCascadeCount", "msaaSampleCount", "renderScale", "supportsHDR", "mainLightShadowmapResolution", "additionalLightsRenderingMode", "maxAdditionalLightsCount", "supportsSoftShadows", "supportsCameraDepthTexture", "supportsCameraOpaqueTexture" }) { var pi = t.GetProperty(p); if (pi != null) sb.Append("\"" + p + "\":\"" + pi.GetValue(rp) + "\","); }
      sb.Append("\"_\":0},\n"); }
    sb.Append("\"lights\":["); foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) sb.Append("{\"n\":\"" + l.name + "\",\"type\":\"" + l.type + "\",\"mode\":\"" + l.lightmapBakeType + "\",\"int\":" + l.intensity.ToString(CI) + ",\"col\":\"" + l.color + "\",\"shadows\":\"" + l.shadows + "\",\"rot\":\"" + l.transform.eulerAngles + "\"},"); sb.Append("{}],\n");
    sb.Append("\"ambient\":\"" + RenderSettings.ambientMode + " sky=" + RenderSettings.ambientSkyColor + " eq=" + RenderSettings.ambientEquatorColor + " gr=" + RenderSettings.ambientGroundColor + " int=" + RenderSettings.ambientIntensity + "\",\"fog\":\"" + RenderSettings.fog + " " + RenderSettings.fogColor + "\",\"skybox\":\"" + (RenderSettings.skybox ? RenderSettings.skybox.name + "/" + RenderSettings.skybox.shader.name : "none") + "\",\n");
    var vols = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(m => m.GetType().Name == "Volume"); sb.Append("\"volumes\":[");
    foreach (var v in vols) { var prof = v.GetType().GetProperty("sharedProfile").GetValue(v) as ScriptableObject; sb.Append("{\"n\":\"" + v.name + "\",\"profile\":\"" + (prof ? prof.name : "null") + "\",\"fx\":\"");
      if (prof != null) { var comps = prof.GetType().GetField("components").GetValue(prof) as System.Collections.IList; foreach (var c in comps) { var co = (ScriptableObject)c; var act = c.GetType().GetField("active").GetValue(c); sb.Append(co.GetType().Name + (true.Equals(act) ? "" : "(off)") + " "); } }
      sb.Append("\"},"); } sb.Append("{}],\n");
    sb.Append("\"renderers\":[\n"); var texSeen = new HashSet<Texture>(); long texBytes = 0; int totalTris = 0;
    foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).OrderBy(r => r.name)) {
      int tris = 0; string mesh = ""; if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) { mesh = mf.sharedMesh.name; tris = mf.sharedMesh.triangles.Length / 3; } }
      else if (r is SkinnedMeshRenderer smr && smr.sharedMesh) { mesh = smr.sharedMesh.name + " bones=" + smr.bones.Length; tris = smr.sharedMesh.triangles.Length / 3; }
      else if (r is ParticleSystemRenderer) continue;
      totalTris += tris; string path2 = r.name; var tp = r.transform.parent; int depth = 0; while (tp != null && depth++ < 3) { path2 = tp.name + "/" + path2; tp = tp.parent; }
      sb.Append("{\"p\":\"" + path2.Replace("\"", "'") + "\",\"mesh\":\"" + mesh + "\",\"tris\":" + tris + ",\"static\":" + (r.gameObject.isStatic ? 1 : 0) + ",\"cast\":\"" + r.shadowCastingMode + "\",\"recv\":" + (r.receiveShadows ? 1 : 0) + ",\"lod\":" + (r.GetComponentInParent<LODGroup>() ? 1 : 0) + ",\"col\":" + (r.GetComponentInParent<Collider>() ? 1 : 0) + ",\"size\":\"" + r.bounds.size + "\",\"mats\":[");
      foreach (var m in r.sharedMaterials) { if (m == null) { sb.Append("\"NULL\","); continue; } string tx = ""; foreach (var tn in m.GetTexturePropertyNames()) { var tex = m.GetTexture(tn); if (tex == null) continue; tx += tn + "=" + tex.name + "(" + tex.width + "x" + tex.height + ") ";
          if (texSeen.Add(tex)) texBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex); }
        string col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : (m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "");
        sb.Append("\"" + m.name + " | " + m.shader.name + " | " + col + " | " + tx.Trim() + "\","); }
      sb.Append("\"\"]},\n"); }
    sb.Append("{}],\n\"totalTris\":" + totalTris + ",\"textureMB\":" + (texBytes / 1048576.0).ToString("0.0", CI) + "\n}"); System.IO.File.WriteAllText(path, sb.ToString()); return "inventory " + path + " tris=" + totalTris; }

  public static string Quality(string mode) {
    if (mode == "mobile") { SessionState.SetString("e3_pc_path", AssetDatabase.GetAssetPath(QualitySettings.renderPipeline));
      QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset"); QualitySettings.lodBias = 1f; QualitySettings.skinWeights = SkinWeights.TwoBones; QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable; }
    else { QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(SessionState.GetString("e3_pc_path", "Assets/Settings/PC_RPAsset.asset")); QualitySettings.lodBias = 2f; QualitySettings.skinWeights = SkinWeights.FourBones; QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable; }
    return "rp=" + QualitySettings.renderPipeline.name; }
  public static string Spots() { var sb = new StringBuilder();
    foreach (var b in Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None)) sb.Append("B" + b.transform.position.x.ToString("0.0", CI) + ";" + b.transform.position.z.ToString("0.0", CI) + "|");
    foreach (var p in Object.FindObjectsByType<FoodPile>(FindObjectsSortMode.None)) sb.Append("P" + p.transform.position.x.ToString("0.0", CI) + ";" + p.transform.position.z.ToString("0.0", CI) + "|");
    return sb.ToString(); }
}"""

SIG = {"PropsOnly": ["dir"], "Assets": ["path"], "Press": ["name"], "Move": ["x", "z"], "CamShot": ["w", "h", "path"], "Quality": ["mode"], "HideUi": ["on"],
       "Overviews": ["dir"], "Subjects": ["dir"], "Inventory": ["path"]}


def cs(method, *args, timeout=300):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "E3", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:800]


def P(*parts):
    return os.path.join(OUT, *parts).replace("\\", "/")


def match_shots(tag):
    for name, w, h in (("desktop", 1920, 1080), ("phone", 2400, 1080), ("tablet", 2048, 1536)):
        print(cs("CamShot", str(w), str(h), P(f"mc_{tag}_{name}.png")))


def main2():
    bridge.init()
    print(cs("Assets", P("assets.txt")))
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(60):
        if str(cs("Press", "SoloBtn")).startswith("clicked"): break
        time.sleep(1)
    time.sleep(0.8); print(cs("Press", "NextBtn")); time.sleep(1.0); print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5); print(cs("Press", "StartBtn"))
    try:
        for _ in range(80):
            if str(cs("State")) == "Active": break
            time.sleep(0.5)
        time.sleep(2.5); print(cs("FreezeBots")); print(cs("HideUi", "1"))
        print(cs("Overviews", OUT.replace("\\", "/")))
        os.makedirs(os.path.join(OUT, "props"), exist_ok=True); print(cs("PropsOnly", (OUT + "/props").replace("\\", "/")))
        print(cs("Quality", "mobile")); time.sleep(2.5); os.makedirs(os.path.join(OUT, "mobile"), exist_ok=True)
        print(cs("Overviews", (OUT + "/mobile").replace("\\", "/"))); print(cs("Quality", "pc"))
    finally:
        cs("HideUi", "0"); bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


def main():
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
                time.sleep(0.3); match_shots("getready"); break
            time.sleep(0.25)
        for _ in range(80):
            if str(cs("State")) == "Active":
                break
            time.sleep(0.5)
        time.sleep(2.5)
        print(cs("FreezeBots"))
        print(cs("HideUi", "1"))
        spots = str(cs("Spots")); print("spots", spots)
        print(cs("Inventory", P("inventory_pc.json")))
        match_shots("spawn")
        # gameplay camera at each base and pile
        for i, s in enumerate(x for x in spots.split("|") if x):
            kind, xy = s[0], s[1:].split(";")
            x, z = float(xy[0]), float(xy[1])
            # stand just inside the base / beside the pile
            ox, oz = (x * 0.85, z * 0.85) if kind == "B" else (x + 1.6, z - 1.6)
            print(cs("Move", f"{ox:.2f}", f"{oz:.2f}")); time.sleep(1.4)
            for name, w, h in (("desktop", 1920, 1080), ("phone", 2400, 1080)):
                print(cs("CamShot", str(w), str(h), P(f"mc_{kind}{i}_{name}.png")))
        print(cs("Move", "0", "0")); time.sleep(1.4); match_shots("center")
        print(cs("Move", "21", "21")); time.sleep(1.4); match_shots("corner_edge")
        print(cs("Overviews", OUT.replace("\\", "/")))
        print(cs("Subjects", OUT.replace("\\", "/")))
        print(cs("LineUp")); time.sleep(1.2)
        lin = os.path.join(OUT, "lineup"); os.makedirs(lin, exist_ok=True)
        print(cs("Subjects", lin.replace("\\", "/")))
        # Mobile tier
        print(cs("Quality", "mobile")); time.sleep(2.5)
        print(cs("Inventory", P("inventory_mobile.json")))
        print(cs("Move", "0", "0")); time.sleep(1.4); match_shots("center_MOBILE")
        print(cs("Overviews", (OUT + "/mobile").replace("\\", "/")) if os.makedirs(os.path.join(OUT, "mobile"), exist_ok=True) is None else "")
        print(cs("Quality", "pc")); time.sleep(1.5)
    finally:
        cs("HideUi", "0")
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


main2() if os.environ.get("E3_PASS") == "2" else main()
