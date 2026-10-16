"""Phase 8 D9 captures + runtime checks: piles collide on their visible oval, outer ovals face the centre.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_d9_piles.py <output_dir> [bots]

Solo START, MATCH camera. With no second argument (bots frozen, UI hidden):
  verify.txt                 every spawned pile: yaw / scale / blocker (convex MeshCollider + carve boxes), and for every
                             base: the bot approach point (FoodPile.SurfaceApproachPoint, BotController standoff) sampled on
                             the LIVE carved NavMesh, a complete path, and that the path end is inside collect range
  d9_contact_diag.png        MATCH camera: the local chicken pressed against its personal pile on the oval's diagonal
                             (placed one CharacterController radius + skin off the collider, checked with ComputePenetration)
  d9_contact_diag_top.png    straight-down close-up of the same pose (oval, disc and chicken)
  d9_overview_top.png        straight-down orthographic overview of the whole arena (outer ovals point at the centre)
With "physics": slide.txt - the local chicken is CharacterController.Move'd straight at every stocked pile along its 4 axis
ends and 4 diagonals; reports where the collider stops it vs the oval (and vs the old bounding box).
With "bots": bots run for ~38 s; bots.txt logs each pile's amount and how many bot-ticks were spent stuck near a pile.
Leaves Play mode at the end.
"""
import sys, os, time
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge
import capture_common

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
BOTS = len(sys.argv) > 2 and sys.argv[2] == "bots"
PHYS = len(sys.argv) > 2 and sys.argv[2] == "physics"
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.AI; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using System.Text; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class D9 {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  static string F(float v) { return v.ToString("0.000", CI); }
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
  public static string FreezeBots() { var l = Local(); int n = 0; foreach (var k in ChickenController.ActiveControllers) { if (k == null || k == l) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot")) { m.enabled = false; n++; } } return "froze " + n; }
  public static string HideUi(string on) { foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) if (d.rootVisualElement != null) d.rootVisualElement.style.display = on == "1" ? DisplayStyle.None : DisplayStyle.Flex; return "ui " + on; }
  static void Put(ChickenController k, Vector3 pos) { pos.y = k.transform.position.y; var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else k.transform.position = pos; }
  static FoodPile[] Piles() { return FoodPile.ActivePiles.Where(p => p != null && p.Object != null && p.Object.IsValid).OrderBy(p => p.transform.position.x).ThenBy(p => p.transform.position.z).ToArray(); }

  public static string Verify(string path) {
    var sb = new StringBuilder(); var piles = Piles(); var bases = Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None).OrderBy(b => b.CornerIndex).ToArray();
    var proto = ChickenController.ActiveControllers.First(k => k != null && k.GetComponent<BotController>() != null).GetComponent<BotController>();
    float standoff = (float)typeof(BotController).GetField("_pileStandoff", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(proto);
    sb.AppendLine("piles=" + piles.Length + " bases=" + bases.Length + " standoff=" + F(standoff) + " agentRadius=" + F(NavMesh.GetSettingsByID(0).agentRadius));
    int bad = 0; var np = new NavMeshPath();
    foreach (var p in piles) {
      var t = p.transform; var blocker = t.Find("Blocker"); var hull = blocker ? blocker.GetComponent<MeshCollider>() : null;
      var carves = blocker ? blocker.GetComponentsInChildren<NavMeshObstacle>() : new NavMeshObstacle[0];
      var radial = t.position.sqrMagnitude > 0.01f ? Mathf.Acos(Mathf.Abs(Vector3.Dot(t.right, -t.position.normalized))) * Mathf.Rad2Deg : 0f;
      sb.AppendLine(string.Format(CI, "{0} pos=({1:F2},{2:F2}) yaw={3:F1} longAxisOffCentre={4:F2}deg scale=({5:F2},{6:F2},{7:F2}) amount={8}/{9} hull={10} convex={11} boxColliders={12} carves={13} carving={14}",
        p.name, t.position.x, t.position.z, t.eulerAngles.y, radial, t.localScale.x, t.localScale.y, t.localScale.z, p.Amount, p.MaxAmount,
        hull != null, hull != null && hull.convex, blocker ? blocker.GetComponents<BoxCollider>().Length : -1, carves.Length, carves.All(c => c.carving)));
      foreach (var b in bases) {
        foreach (var src in new[] { b.transform.position, 2f * t.position - b.transform.position }) {
          var target = p.SurfaceApproachPoint(src, standoff);
          bool ok = NavMesh.SamplePosition(b.transform.position, out var from, 2f, NavMesh.AllAreas) && NavMesh.SamplePosition(target, out var to, 2.5f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, np) && np.status == NavMeshPathStatus.PathComplete;
          var end = ok ? np.corners[np.corners.Length - 1] : target;
          bool collect = ok && p.IsWithinCollectRange(end);
          bool inCarve = !NavMesh.SamplePosition(target, out var exact, 0.05f, NavMesh.AllAreas);
          if (!ok || !collect) bad++;
          sb.AppendLine(string.Format(CI, "   from base{0} {1}: path={2} endToSurface={3:F2} collect={4} targetInsideCarve={5}", b.CornerIndex, src == b.transform.position ? "near" : "far ", ok ? np.status.ToString() : "FAIL", p.DistanceToSurface(end), collect, inCarve));
        }
      }
    }
    sb.AppendLine("FAILURES=" + bad);
    System.IO.File.WriteAllText(path, sb.ToString()); return "verify bad=" + bad + " -> " + path; }

  // Put the local chicken against its own personal pile on the oval's local 45-degree diagonal.
  public static string Contact() {
    var l = Local(); var cc = l.GetComponent<CharacterController>(); var home = Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None).First(b => b.CornerIndex == l.HomeCornerIndex);
    var pile = Piles().Where(p => p.transform.position.sqrMagnitude > 1f).OrderBy(p => (p.transform.position - home.transform.position).sqrMagnitude).First();
    var t = pile.transform; var hull = t.Find("Blocker").GetComponent<MeshCollider>();
    // local diagonal, on the side facing the base's camera (toward the base, rotated 45 degrees off the long axis)
    var dirs = new[] { 45f, 135f, 225f, 315f }.Select(a => t.rotation * new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad))).ToArray();
    var toBase = (home.transform.position - t.position); toBase.y = 0; toBase.Normalize();
    var dir = dirs.OrderByDescending(d => Vector3.Dot(d, toBase) + 0.3f * Vector3.Dot(d, Vector3.Cross(Vector3.up, toBase))).First();
    var origin = t.position + dir * 20f + Vector3.up * 0.5f;
    hull.Raycast(new Ray(origin, -dir), out var hit, 40f);
    var surface = new Vector3(hit.point.x, 0f, hit.point.z);
    float gap = cc.radius * l.transform.lossyScale.x + cc.skinWidth;
    var stand = surface + dir * gap;
    Put(l, stand); Physics.SyncTransforms();
    // Physical check: no penetration where it stands, penetration 0.1 m further in.
    var center = l.transform.TransformPoint(cc.center);
    bool overlapAt = Physics.ComputePenetration(cc, l.transform.position, l.transform.rotation, hull, hull.transform.position, hull.transform.rotation, out _, out float d0);
    bool overlapIn = Physics.ComputePenetration(cc, l.transform.position - dir * 0.1f, l.transform.rotation, hull, hull.transform.position, hull.transform.rotation, out _, out float d1);
    // Old bounding-box corner along the same ray, for the caption.
    var fp = pile.CurrentFootprint; var ld = Quaternion.Inverse(t.rotation) * dir; float boxT = Mathf.Min(fp.x * 0.5f / Mathf.Max(1e-4f, Mathf.Abs(ld.x)), fp.y * 0.5f / Mathf.Max(1e-4f, Mathf.Abs(ld.z)));
    return string.Format(CI, "pile {0} yaw {1:F1} dir {2} surface r={3:F2} (old box r={4:F2}, {5:F2} m further out) stand {6} gap {7:F2} overlapAtStand={8} ({9:F3}) overlap0.1mIn={10} ({11:F3}) distToSurface={12:F2} collect={13}",
      pile.name, t.eulerAngles.y, dir.ToString("F2"), (surface - new Vector3(t.position.x, 0, t.position.z)).magnitude, boxT, boxT - (surface - new Vector3(t.position.x, 0, t.position.z)).magnitude, stand.ToString("F2"), gap,
      overlapAt, d0, overlapIn, d1, pile.DistanceToSurface(l.transform.position), pile.IsWithinCollectRange(l.transform.position)); }

  static void Save(Camera cam, int W, int H, string path) {
    var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = CapTier.Msaa() }; var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
  public static string CamShot(string path) { Save(MatchCamera.Instance.Camera, 1920, 1080, path); var c = MatchCamera.Instance.Camera.transform; return "saved " + path + " cam " + c.position.ToString("F1") + " " + c.eulerAngles.ToString("F1"); }
  static Camera Free() { var go = GameObject.Find("__D9Cam"); if (go == null) { var src = MatchCamera.Instance.Camera; var data = src.GetComponent("UniversalAdditionalCameraData");
      go = new GameObject("__D9Cam"); var c = go.AddComponent<Camera>(); c.enabled = false; c.CopyFrom(src); c.targetTexture = null; if (data != null) go.AddComponent(data.GetType()); }
    var fc = go.GetComponent<Camera>(); CapTier.ApplyPost(fc); return fc; }
  public static string Top(string x, string z, string size, string path) { var cam = Free(); cam.orthographic = true; cam.orthographicSize = float.Parse(size, CI); cam.nearClipPlane = 0.1f; cam.farClipPlane = 300f;
    cam.transform.position = new Vector3(float.Parse(x, CI), 120f, float.Parse(z, CI)); cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); cam.aspect = 1f; Save(cam, 1600, 1600, path); return "saved " + path; }
  public static string LocalXZ() { var l = Local(); return l.transform.position.x.ToString("0.00", CI) + ";" + l.transform.position.z.ToString("0.00", CI); }

  // Physical check: from 2.5 m out along a direction, CharacterController.Move the local chicken 4 m straight at the pile
  // centre and report where the collider stopped it, for the 4 local diagonals and the 4 axis ends of every stocked pile.
  public static string Slide() { var l = Local(); var cc = l.GetComponent<CharacterController>(); var sb = new StringBuilder(); float worst = 0f, worstBox = 0f;
    foreach (var pile in Piles().Where(p => p.Amount > 0f)) { var t = pile.transform; var fp = pile.CurrentFootprint;
      foreach (float a in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f }) {
        var ld = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)); var dir = t.rotation * ld;
        float er = fp.x * 0.5f * fp.y * 0.5f / Mathf.Sqrt(Mathf.Pow(fp.y * 0.5f * ld.x, 2) + Mathf.Pow(fp.x * 0.5f * ld.z, 2));
        float br = Mathf.Min(fp.x * 0.5f / Mathf.Max(1e-4f, Mathf.Abs(ld.x)), fp.y * 0.5f / Mathf.Max(1e-4f, Mathf.Abs(ld.z)));
        cc.enabled = false; var start = t.position + dir * (er + 2.5f); start.y = l.transform.position.y; l.transform.position = start; cc.enabled = true; Physics.SyncTransforms();
        cc.Move(-dir * 4f); var p = l.transform.position; float r = new Vector2(p.x - t.position.x, p.z - t.position.z).magnitude;
        float capsule = cc.radius * Mathf.Max(l.transform.lossyScale.x, l.transform.lossyScale.z);
        float gapOval = r - er - capsule, gapBox = r - br - capsule; worst = Mathf.Max(worst, Mathf.Abs(gapOval)); worstBox = Mathf.Max(worstBox, Mathf.Abs(r - capsule - br));
        sb.AppendLine(string.Format(CI, "{0} ({1:F1},{2:F1}) local {3,3} deg: stopped r={4:F3} -> capsule edge {5:+0.000;-0.000} m from the oval (old box would be {6:+0.000;-0.000}) collect={7}",
          pile.name, t.position.x, t.position.z, a, r, gapOval, gapBox, pile.IsWithinCollectRange(p))); } }
    sb.AppendLine(string.Format(CI, "worst |capsule edge - oval| = {0:F3} m", worst)); return sb.ToString(); }

  // Bot observation: amounts + stuck counter (a bot in CollectFood that has not moved 0.05 m in a tick while not in collect range of any pile).
  public static string BotTick() { var sb = new StringBuilder(); int stuck = 0;
    foreach (var k in ChickenController.ActiveControllers) { if (k == null || k.IsDecoy || k.HasInputAuthority) continue; var bc = k.GetComponent<BotController>(); if (bc == null) continue;
      var key = "d9_last_" + k.GetInstanceID(); var lastS = UnityEditor.SessionState.GetString(key, ""); var pos = k.transform.position; UnityEditor.SessionState.SetString(key, pos.x.ToString(CI) + ";" + pos.z.ToString(CI));
      if (lastS == "") continue; var a = lastS.Split(';'); var last = new Vector3(float.Parse(a[0], CI), 0, float.Parse(a[1], CI));
      bool moved = (new Vector3(pos.x, 0, pos.z) - last).magnitude > 0.05f; bool atPile = Piles().Any(p => p.IsWithinCollectRange(pos));
      bool nearPile = Piles().Any(p => p.DistanceToSurface(pos) < 2.5f);
      if (!moved && !atPile && nearPile) stuck++; }
    var am = string.Join(" ", Piles().Select(p => p.name.Replace("FoodPile", "P") + "@" + p.transform.position.x.ToString("0", CI) + "," + p.transform.position.z.ToString("0", CI) + "=" + p.Amount.ToString("0.#", CI)));
    return "stuckNearPile=" + stuck + " | " + am + " | state=" + State(); }
}""" + capture_common.CAP_TIER_CS

SIG = {"Press": ["name"], "HideUi": ["on"], "Verify": ["path"], "CamShot": ["path"], "Top": ["x", "z", "size", "path"]}


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "D9", "methodName": method, "parameters": params}, timeout=180)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return "ERROR " + bridge.text(m)[:800]


def P(name):
    return os.path.join(OUT, name).replace("\\", "/")


def start():
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
    for _ in range(240):
        if str(cs("State")) == "Active":
            return
        time.sleep(0.25)
    raise SystemExit("match never reached Active: " + str(cs("State")))


def main():
    try:
        start()
        print("PASS " + capture_common.cap_info())
        if PHYS:
            time.sleep(1.0); print(cs("FreezeBots"))
            r = cs("Slide"); print(r); open(P("slide.txt"), "w", encoding="utf-8").write(r)
            return
        if BOTS:
            log = []
            for i in range(19):
                time.sleep(2.0)
                s = cs("BotTick"); log.append(f"t+{2 * (i + 1)}s {s}"); print(log[-1])
                if "state=Active" not in s:
                    break
            open(P("bots.txt"), "w", encoding="utf-8").write("\n".join(log) + "\n")
            return
        time.sleep(1.0)
        print(cs("FreezeBots")); print(cs("HideUi", "1"))
        print(cs("Verify", P("verify.txt")))
        print(cs("Contact")); time.sleep(1.5)
        print(cs("CamShot", P("d9_contact_diag.png")))
        x, z = cs("LocalXZ").split(";")
        print(cs("Top", x, z, "4.5", P("d9_contact_diag_top.png")))
        print(cs("Top", "0", "0", "27", P("d9_overview_top.png")))
    finally:
        try:
            cs("HideUi", "0")
        except Exception as e:
            print("show UI failed:", e)
        bridge.call("editor-application-set-state", {"isPlaying": False})
        time.sleep(4)


if __name__ == "__main__":
    bridge.init()
    with capture_common.perf_mode():
        main()
