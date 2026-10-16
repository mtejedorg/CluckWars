"""Phase 8 C1: match-camera fence captures at the 4 base corners. Color + white-on-black coverage mask.
    python tools/ui_capture/capture_c1_fence.py <outdir> <tag>
"""
import sys, os, time
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge
import capture_common

OUT = os.path.abspath(sys.argv[1]); TAG = sys.argv[2]
os.makedirs(OUT, exist_ok=True)
CS = r"""using UnityEngine; using UnityEngine.UIElements; using UnityEditor; using System.Linq; using System.Reflection; using System.Text; using System.Collections.Generic; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class C1 {
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
  public static string FreezeBots() { foreach (var k in ChickenController.ActiveControllers) { if (k == null || k.HasInputAuthority) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot")) m.enabled = false; } return "froze"; }
  public static string HideUi(string on) { foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) if (d.rootVisualElement != null) d.rootVisualElement.style.display = on == "1" ? DisplayStyle.None : DisplayStyle.Flex; return "ui"; }
  public static string Spots() { var sb = new StringBuilder(); foreach (var b in Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None)) sb.Append("B" + b.transform.position.x.ToString("0.0", CI) + ";" + b.transform.position.z.ToString("0.0", CI) + "|"); return sb.ToString(); }
  static IEnumerable<MeshRenderer> Fence() { return Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name == "wall_segment" ); }
  static float SX = 1, SZ = 1;
  public static string Corner(string sx, string sz) { SX = float.Parse(sx, CI); SZ = float.Parse(sz, CI); return "corner " + SX + "," + SZ; }
  static void Pose(Camera cam) { cam.transform.position = new Vector3(SX * 21.8f + SX * 9.44f, 8.38f, SZ * 21.8f + SZ * 9.16f); cam.transform.rotation = Quaternion.Euler(30f, Mathf.Atan2(-SX, -SZ) * Mathf.Rad2Deg, 0f); }
  static void Render(Camera cam, int W, int H, string path, int aa) {
    var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = aa }; Pose(cam); var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
  public static string Shot(string w, string h, string path) { Render(MatchCamera.Instance.Camera, int.Parse(w), int.Parse(h), path, CapTier.Msaa()); return "color " + path; }
  public static string Mask(string w, string h, string path) {
    var cam = MatchCamera.Instance.Camera; var rs = Fence().ToList();
    var sh = Shader.Find("Universal Render Pipeline/Unlit"); var mat = new Material(sh); mat.SetColor("_BaseColor", Color.white);
    var oldMats = rs.Select(r => r.sharedMaterials).ToList(); var oldLayer = rs.Select(r => r.gameObject.layer).ToList(); var oldMask = cam.cullingMask; var oldFlags = cam.clearFlags; var oldBg = cam.backgroundColor;
    var uacd = cam.GetComponent("UniversalAdditionalCameraData"); var ppProp = uacd.GetType().GetProperty("renderPostProcessing"); var oldPp = ppProp.GetValue(uacd);
    for (int i = 0; i < rs.Count; i++) { rs[i].gameObject.layer = 30; var arr = new Material[rs[i].sharedMaterials.Length]; for (int j = 0; j < arr.Length; j++) arr[j] = mat; rs[i].sharedMaterials = arr; }
    cam.cullingMask = 1 << 30; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; ppProp.SetValue(uacd, false);
    Render(cam, int.Parse(w), int.Parse(h), path, 1);
    for (int i = 0; i < rs.Count; i++) { rs[i].gameObject.layer = oldLayer[i]; rs[i].sharedMaterials = oldMats[i]; }
    cam.cullingMask = oldMask; cam.clearFlags = oldFlags; cam.backgroundColor = oldBg; ppProp.SetValue(uacd, oldPp); Object.DestroyImmediate(mat);
    return "mask " + rs.Count + " fence renderers " + path; }
  public static string Info() { var cam = MatchCamera.Instance.Camera; var m = Fence().FirstOrDefault(); var mf = m ? m.GetComponent<MeshFilter>() : null; return "cam ortho=" + cam.orthographic + " size=" + cam.orthographicSize + " pos=" + cam.transform.position + " rot=" + cam.transform.eulerAngles + " fence=" + Fence().Count() + " mesh=" + (mf ? mf.sharedMesh.name + " tris=" + mf.sharedMesh.triangles.Length / 3 : "none"); }
}""" + capture_common.CAP_TIER_CS
SIG = {"Press": ["name"], "Move": ["x", "z"], "Corner": ["sx", "sz"], "Shot": ["w", "h", "path"], "Mask": ["w", "h", "path"], "HideUi": ["on"]}


def cs(method, *args, timeout=300):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "C1", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:800]


def P(n):
    return os.path.join(OUT, n).replace("\\", "/")


bridge.init()
_perf = capture_common.perf_mode(); _perf.__enter__()   # Performance Mode explicit + logged + restored; before Play
bridge.call("editor-application-set-state", {"isPlaying": True})
for _ in range(60):
    if str(cs("Press", "SoloBtn")).startswith("clicked"):
        break
    time.sleep(1)
time.sleep(0.8); print(cs("Press", "NextBtn")); time.sleep(1.0); print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5); print(cs("Press", "StartBtn"))
try:
    for _ in range(80):
        if str(cs("State")) == "Active":
            break
        time.sleep(0.5)
    time.sleep(2.5); print(cs("FreezeBots")); print(cs("HideUi", "1")); print("PASS " + capture_common.cap_info())
    spots = [s for s in str(cs("Spots")).split("|") if s]; spots.sort(); print(spots)
    print(cs("Info"))
    for i, s in enumerate(spots):
        x, z = [float(v) for v in s[1:].split(";")]
        print(cs("Move", f"{x:.2f}", f"{z:.2f}")); time.sleep(1.0); print(cs("Corner", str(1 if x > 0 else -1), str(1 if z > 0 else -1)))
        print(cs("Shot", "3840", "2160", P(f"{TAG}_corner{i}_color.png")))
        print(cs("Mask", "3840", "2160", P(f"{TAG}_corner{i}_mask.png")))
finally:
    cs("HideUi", "0"); bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4); _perf.__exit__(None, None, None)
