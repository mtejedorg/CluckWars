"""Phase 6 chunk 6 captures: settings page, desktop info HUD, touch HUD at phone size, first-time hold hint.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_phase6_chunk6.py <repo_captures_dir>

Writes into <repo_captures_dir> (Captures/phase6):
  static/settings_page__desktop.png   settings sheet rendered off-screen at 1920x1080 (Buzz When Hit hidden: no vibrator)
  static/settings_page__phone.png     the same at 2424x1080 with the Buzz row shown, as it is on a phone
  live/desktop_info_hud.png           keyboard / mouse layout in a solo match: bottom-centre strip, Q/E/R/F badges,
                                      a cooling hex with its number, an in-range pip
  live/touch_hud_phone.png            touch layout on a 2424x1080 Game view (when the Game view can be resized)
  live/hint_first_hold.png            touch layout, slot 1 held, hint line above the cluster
  live/pad_info_hud.png               gamepad layout (RB / RT / LB / LT)
The bridge cannot move a real mouse or press a pad, so the layout is forced through TouchControlsController.DebugForceMode
(what the last-used-device switch resolves to) and the hold through the controller's own per-slot held flag. Leaves Play
mode, the Game view size and the Hold Hint counter as it found them.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

ARGS = sys.argv[1:]
OUT = os.path.abspath(ARGS[0]) if ARGS else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(os.path.join(OUT, "static"), exist_ok=True)
os.makedirs(os.path.join(OUT, "live"), exist_ok=True)
OUTCS = OUT.replace("\\", "/")

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using System.IO; using CluckWars.Gameplay; using CluckWars.Abilities; using CluckWars.Input; using CluckWars.Settings; using CluckWars.Visuals; using Fusion;
public class P6 {
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
  public static string MenuReady() { return Doc("SoloBtn") != null ? "ready" : "no"; }
  static ChickenController Local() { return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && !k.IsBot && k.HasInputAuthority); }
  static ChickenController Rival() { return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && !k.HasInputAuthority); }
  static AbilityBaseSO Find(string t) { var g = UnityEditor.AssetDatabase.FindAssets("t:" + t).FirstOrDefault(); return g == null ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<AbilityBaseSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)); }

  // ---- settings sheet (menu) ----
  public static string ShowBuzz(string on) { var d = Doc("SettingsSheet"); if (d == null) return "nosheet"; var row = d.rootVisualElement.Q("BuzzWhenHitRow"); if (row == null) return "norow";
    string before = row.resolvedStyle.display.ToString(); if (on == "1") row.style.display = DisplayStyle.Flex; return "buzz row was " + before; }
  public static string ScrollSheet() { var d = Doc("SettingsSheet"); var sv = d.rootVisualElement.Q<ScrollView>(className: "cw-sheet__body"); if (sv == null) return "nosv"; sv.scrollOffset = new Vector2(0, 99999f); return "scrolled"; }
  public static string SetRT(string w, string h) { var ps = Doc("SoloBtn").panelSettings; var old = ps.targetTexture;
    if (old != null && old.width == int.Parse(w) && old.height == int.Parse(h)) return "rt kept";
    var rt = new RenderTexture(int.Parse(w), int.Parse(h), 24, RenderTextureFormat.ARGB32); rt.name = "AuditRT"; rt.Create();
    ps.targetTexture = rt; ps.clearColor = true; ps.colorClearValue = Color.black;
    if (old != null && old.name == "AuditRT") { old.Release(); Object.Destroy(old); } return "rt " + w + "x" + h; }
  public static string Grab(string path) { var rt = Doc("SoloBtn").panelSettings.targetTexture; RenderTexture.active = rt;
    var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false); t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); t.Apply(); RenderTexture.active = null;
    File.WriteAllBytes(path, t.EncodeToPNG()); return "saved " + path; }
  public static string RestoreRT() { var ps = Doc("SoloBtn").panelSettings; var rt = ps.targetTexture; ps.targetTexture = null; ps.clearColor = false; if (rt != null) { rt.Release(); Object.Destroy(rt); } return "restored"; }

  // ---- live HUD ----
  public static string SetSlots() { var l = Local(); if (l == null) return "no local";
    l.Abilities.SetSlots(null, Find("WingSlamAbilitySO"), Find("HeadbuttAbilitySO"), Find("SnatchAbilitySO"), Find("PeckAbilitySO"));
    for (int i = 0; i < 4; i++) l.Abilities.TriggerCooldown(i, 0.01f);
    return string.Join(",", Enumerable.Range(0, 4).Select(i => l.Abilities.GetSlot(i) == null ? "-" : l.Abilities.GetSlot(i).name)); }
  public static string Cool(string slot, string secs) { var l = Local(); l.Abilities.TriggerCooldown(int.Parse(slot), float.Parse(secs, System.Globalization.CultureInfo.InvariantCulture)); return "cooling " + slot; }
  public static string Place(string dist) { var l = Local(); var r = Rival(); if (r == null) return "no rival"; var bot = r.GetComponent<BotController>(); if (bot != null) bot.enabled = false;
    Vector3 pos = l.transform.position; Vector3 fwd = l.transform.forward; fwd.y = 0; fwd.Normalize(); Vector3 rp = pos + fwd * float.Parse(dist, System.Globalization.CultureInfo.InvariantCulture); rp.y = pos.y;
    var nt = r.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(rp); else r.transform.position = rp; return "rival ahead"; }
  public static string Force(string mode) { TouchControlsController.DebugForceMode = mode == "none" ? (HudDeviceMode?)null : (HudDeviceMode)int.Parse(mode); return "mode=" + (TouchControlsController.Instance == null ? "?" : TouchControlsController.Instance.Mode.ToString()); }
  public static string Mode() { var h = TouchControlsController.Instance; return h == null ? "nohud" : h.Mode.ToString(); }
  public static string Hold(string slot, string on) { var h = TouchControlsController.Instance; var f = typeof(TouchControlsController).GetField("_held", BindingFlags.NonPublic|BindingFlags.Instance);
    ((bool[])f.GetValue(h))[int.Parse(slot)] = on == "1"; return "held[" + slot + "]=" + on; }
  public static string HintCount(string n) { int prev = PlayerPreferences.HoldHintCount; if (n != "get") PlayerPreferences.HoldHintCount = int.Parse(n); return prev.ToString(); }
  public static string Hint() { var h = TouchControlsController.Instance; var l = (Label)typeof(TouchControlsController).GetField("_hint", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(h); return "on=" + l.ClassListContains("cw-hold-hint--on") + " text=" + l.text; }
  public static string Board() { var d = Doc("LeaderboardPanel"); if (d == null) return "noboard"; return string.Join(",", Enumerable.Range(0, 4).Select(i => d.rootVisualElement.Q("LbRow" + i).resolvedStyle.display == DisplayStyle.Flex ? "1" : "0")); }

  // ---- Game view size ----
  public static string GameSize(string w, string h) {
    var ed = typeof(UnityEditor.Editor).Assembly; var gvT = ed.GetType("UnityEditor.GameView"); var gv = UnityEditor.EditorWindow.GetWindow(gvT);
    var sizesT = ed.GetType("UnityEditor.GameViewSizes"); var singT = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesT);
    var inst = singT.GetProperty("instance").GetValue(null); var group = sizesT.GetProperty("currentGroup").GetValue(inst);
    var sizeT = ed.GetType("UnityEditor.GameViewSize"); var typeT = ed.GetType("UnityEditor.GameViewSizeType");
    var size = System.Activator.CreateInstance(sizeT, System.Enum.ToObject(typeT, 1), int.Parse(w), int.Parse(h), "P6 " + w + "x" + h);
    group.GetType().GetMethod("AddCustomSize").Invoke(group, new object[] { size });
    int idx = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null) - 1;
    var prop = gvT.GetProperty("selectedSizeIndex", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
    prev = (int)prop.GetValue(gv); prop.SetValue(gv, idx); gv.Repaint(); return "game view " + w + "x" + h + " (index " + idx + ", was " + prev + ")"; }
  static int prev;
  public static string GameSizeBack(string idx) { var ed = typeof(UnityEditor.Editor).Assembly; var gvT = ed.GetType("UnityEditor.GameView"); var gv = UnityEditor.EditorWindow.GetWindow(gvT);
    var prop = gvT.GetProperty("selectedSizeIndex", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance); prop.SetValue(gv, int.Parse(idx)); gv.Repaint(); return "back to " + idx; }
  public static string GameSizeIndex() { var ed = typeof(UnityEditor.Editor).Assembly; var gvT = ed.GetType("UnityEditor.GameView"); var gv = UnityEditor.EditorWindow.GetWindow(gvT);
    return ((int)gvT.GetProperty("selectedSizeIndex", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(gv)).ToString(); }
}"""

SIG = {"Press": ["name"], "ShowBuzz": ["on"], "SetRT": ["w", "h"], "Grab": ["path"], "Cool": ["slot", "secs"], "Place": ["dist"],
       "Force": ["mode"], "Hold": ["slot", "on"], "HintCount": ["n"], "GameSize": ["w", "h"], "GameSizeBack": ["idx"]}


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "P6", "methodName": method, "parameters": params}, timeout=90)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:600]


def wait_for(method, prefixes, tries=90):
    s = ""
    for _ in range(tries):
        s = cs(method)
        if str(s).startswith(prefixes):
            return s
        time.sleep(1)
    raise SystemExit("timed out waiting for " + str(prefixes) + " (last: " + str(s) + ")")


def shot(sub, name, tag="p6c6"):
    time.sleep(0.5)
    p = bridge.shot("_" + tag + "_" + name)
    if p:
        dest = os.path.join(OUT, sub, name + ".png")
        os.replace(p, dest)
        print("  saved", dest)


def main():
    bridge.init()
    bridge.call("editor-application-set-state", {"isPlaying": True})
    wait_for("MenuReady", ("ready",))
    time.sleep(1.0)
    size_idx = cs("GameSizeIndex")
    hint_prev = cs("HintCount", "get")
    try:
        # ---- static: settings sheet, off-screen ----
        print(cs("Press", "SettingsBtn")); time.sleep(0.6)
        print(cs("SetRT", "1920", "1080")); time.sleep(1.2)
        print(cs("Grab", f"{OUTCS}/static/settings_page__desktop.png"))
        print(cs("ShowBuzz", "1")); print(cs("SetRT", "2424", "1080")); time.sleep(1.2)
        print(cs("Grab", f"{OUTCS}/static/settings_page__phone.png"))
        print(cs("ScrollSheet")); time.sleep(1.0)
        print(cs("Grab", f"{OUTCS}/static/settings_page__phone_scrolled.png"))
        print(cs("RestoreRT")); print(cs("Press", "SettingsCloseBtn")); time.sleep(0.4)

        # ---- live: solo match ----
        print(cs("Press", "SoloBtn")); time.sleep(0.9)
        print(cs("Press", "NextBtn")); time.sleep(1.0)
        print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5)
        print(cs("Press", "StartBtn"))
        wait_for("State", ("Active",)); time.sleep(4.5)
        print(cs("SetSlots")); time.sleep(0.4)
        print("mode at match start (no input yet):", cs("Mode"))

        # keyboard / mouse layout: a cooling hex with its number, an in-range pip on slot 1
        print(cs("Force", "1")); print(cs("Cool", "1", "6")); print(cs("Place", "2.2")); time.sleep(0.8)
        print("  mode:", cs("Mode"))
        shot("live", "desktop_info_hud")

        # gamepad layout
        print(cs("Force", "2")); time.sleep(0.6); print("  mode:", cs("Mode"))
        shot("live", "pad_info_hud")

        # touch layout at phone size, then the first-hold hint
        print(cs("Force", "0")); time.sleep(0.5)
        print(cs("GameSize", "2424", "1080")); time.sleep(1.5)
        print("  board rows (leader+you on phone):", cs("Board"))
        shot("live", "touch_hud_phone")
        print("  hint counter was", cs("HintCount", "0")); time.sleep(0.2)
        print(cs("Hold", "0", "1")); time.sleep(0.7)
        print("  hint:", cs("Hint"))
        shot("live", "hint_first_hold")
        print(cs("Hold", "0", "0")); time.sleep(0.5)
        print("  after release:", cs("Hint"), "| counter", cs("HintCount", "get"))
    finally:
        try:
            print(cs("Force", "none"))
            print(cs("GameSizeBack", size_idx))
            print("hint counter restored from", cs("HintCount", str(hint_prev)));
        except Exception as e:
            print("cleanup failed:", e)
        bridge.call("editor-application-set-state", {"isPlaying": False})
        time.sleep(4)


main()
