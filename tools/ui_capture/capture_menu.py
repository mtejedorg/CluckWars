"""Render every menu screen off-screen at desktop, phone and tablet sizes via the Unity MCP bridge.

Usage (Editor open, Bootstrap.unity loaded and the only open scene, not in Play mode):
    python tools/ui_capture/capture_menu.py <output_dir>

It enters Play mode, walks Main (with PLAY AGAIN if a last setup is stored) -> Settings -> Main (fresh:
the stored last setup is CLEARED, then rewritten by READY later in the run) -> PICK YOUR BIRD (default,
then Assassin tile + its second perk badge) -> GEAR UP (fresh, then filled; the last card tapped shows
its details) -> THE COOP, and for each screen sets the menu PanelSettings.targetTexture to a
RenderTexture (1920x1080, 2424x1080, 2048x1536), saves a PNG, then restores the PanelSettings. Page
changes animate (120 ms out + 200 ms in), so every navigation is followed by a SETTLE pause. Element
names it drives (SoloBtn, HomeBtn, SettingsBtn/SettingsCloseBtn, ClassAssassin, PerkBadgeB, NextBtn,
ReadyBtn, the .cw-ability-card class, the PageHost container) must be updated if the UXML/controller
renames them.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(OUT, exist_ok=True)
OUTCS = OUT.replace("\\", "/")

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using System.IO;
public class Cap {
  static UIDocument Doc() { return Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None).FirstOrDefault(x => x.rootVisualElement != null && x.rootVisualElement.Q<Button>("SoloBtn") != null); }
  static VisualElement Menu() { var d = Doc(); return d == null ? null : d.rootVisualElement; }
  public static string Pages() { var r = Menu(); if (r == null) return "NOMENU"; var host = r.Q<VisualElement>("PageHost") ?? r; return "pages=[" + string.Join(",", host.Children().Select((c,i)=> c.resolvedStyle.display==DisplayStyle.Flex ? i.ToString() : "-")) + "]"; }
  public static string Info() { var ps = Doc().panelSettings; return "ps=" + ps.name + " target=" + (ps.targetTexture==null?"null":ps.targetTexture.name) + " clear=" + ps.clearColor + " scaleMode=" + ps.scaleMode + " ref=" + ps.referenceResolution + " match=" + ps.match; }
  public static string Setup(string w, string h) { var ps = Doc().panelSettings; var rt = new RenderTexture(int.Parse(w), int.Parse(h), 24, RenderTextureFormat.ARGB32); rt.name = "AuditRT"; rt.Create();
    ps.targetTexture = rt; ps.clearColor = true; ps.colorClearValue = Color.black; return "rt " + w + "x" + h; }
  public static string Grab(string path) { var rt = Doc().panelSettings.targetTexture; RenderTexture.active = rt;
    var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false); t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); t.Apply(); RenderTexture.active = null;
    File.WriteAllBytes(path, t.EncodeToPNG()); return "saved " + path; }
  public static string Restore() { var ps = Doc().panelSettings; var rt = ps.targetTexture; ps.targetTexture = null; ps.clearColor = false; if (rt != null) rt.Release(); return "restored"; }
  static void Click(Button b) { var c = typeof(Button).GetField("m_Clickable", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(b);
    ((System.Action)typeof(Clickable).GetField("clicked", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(c)).Invoke(); }
  public static string Press(string name) { var b = Menu().Q<Button>(name); if (b == null) return "NOBTN " + name; Click(b); return "clicked " + name; }
  public static string Tap(string parent, string child) { var p = Menu().Q<VisualElement>(parent); var t = p == null ? null : p.Q<VisualElement>(child); if (t == null) return "NOTGT";
    using (var e = ClickEvent.GetPooled()) { e.target = t; t.SendEvent(e); } return "tapped " + parent + "/" + child; }
  public static string TapEl(string name) { var t = Menu().Q<VisualElement>(name); if (t == null) return "NOTGT " + name;
    using (var e = ClickEvent.GetPooled()) { e.target = t; t.SendEvent(e); } return "tapped " + name; }
  public static string JoinLobby() { var c = Object.FindFirstObjectByType<CluckWars.UI.MenuUiController>();
    var sel = Zenject.ProjectContext.Instance.Container.Resolve<CluckWars.Services.ISessionSelectionService>(); sel.Mode = CluckWars.Services.SessionMode.Join;
    typeof(CluckWars.UI.MenuUiController).GetMethod("ShowLobby", BindingFlags.NonPublic|BindingFlags.Instance).Invoke(c, null); return "join-mode lobby"; }
  public static string ClearLastSetup() { CluckWars.Settings.PlayerPreferences.LastSetup = null; return "last setup cleared"; }
  public static string TapCard(string label) { var r = Menu(); var c = r.Query<VisualElement>(className: "cw-ability-card").ToList().FirstOrDefault(x => x.Query<Label>().ToList().Any(l => l.text != null && l.text.ToUpperInvariant() == label));
    if (c == null) return "NOCARD " + label; using (var e = ClickEvent.GetPooled()) { e.target = c; c.SendEvent(e); } return "card " + label; }
  public static string Fill() { var r = Menu(); int taps = 0;
    for (int i = 0; i < 14; i++) { var rb = r.Q<Button>("ReadyBtn"); if (rb != null && rb.enabledSelf) break;
      var card = r.Query<VisualElement>(className: "cw-ability-card").ToList().FirstOrDefault(c => !c.ClassListContains("cw-ability-card--picked")); if (card == null) break;
      using (var e = ClickEvent.GetPooled()) { e.target = card; card.SendEvent(e); } taps++; }
    return "taps=" + taps; }
}"""

def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(["a", "b"], args)]
    code = CS
    if method in ("Setup", "Tap"):
        code = CS  # two-arg methods use (w,h) / (parent,child)
    sig = {"Setup": ["w", "h"], "Grab": ["path"], "Press": ["name"], "Tap": ["parent", "child"], "TapCard": ["label"], "TapEl": ["name"]}.get(method, [])
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(sig, args)]
    r = bridge.text(bridge.call("script-execute", {"csharpCode": code, "className": "Cap", "methodName": method, "parameters": params}, timeout=90))
    try:
        return json.loads(r)["result"]["value"]
    except Exception:
        return r[:200]

RES = [("desktop_1920x1080", "1920", "1080"), ("phone_2424x1080", "2424", "1080"), ("tablet_2048x1536", "2048", "1536")]

def snap(page):
    for tag, w, h in RES:
        cs("Setup", w, h)
        time.sleep(0.8)
        print("   ", tag, cs("Grab", f"{OUTCS}/{page}__{tag}.png").split("/")[-1])
        cs("Restore")
        time.sleep(0.2)

bridge.init()
bridge.call("editor-application-set-state", {"isPlaying": True})
for _ in range(60):
    s = cs("Pages")
    if s.startswith("pages="):
        break
    time.sleep(1)
print("menu:", s, "|", cs("Info"))
time.sleep(1.5)
SETTLE = 0.9  # page transitions: 120 ms out + 200 ms in, plus margin for the bridge round trip
print("01 main menu"); snap("01_main_menu")
print(cs("Press", "SettingsBtn")); time.sleep(0.5)
print("01b settings sheet"); snap("01b_settings")
print(cs("Press", "SettingsCloseBtn")); time.sleep(0.3)
print(cs("ClearLastSetup"), cs("Press", "SoloBtn")); time.sleep(SETTLE)
print(cs("Press", "HomeBtn"), cs("Pages")); time.sleep(SETTLE)
print("01c main menu (fresh, no last setup)"); snap("01c_main_menu_fresh")
print(cs("Press", "SoloBtn")); time.sleep(SETTLE); print(cs("Pages"))
print("02 class select (default)"); snap("02_class_select_default")
print(cs("TapEl", "ClassAssassin"), cs("Press", "PerkBadgeB")); time.sleep(0.5)
print("03 class select (assassin, second perk)"); snap("03_class_select_assassin")
print(cs("Press", "NextBtn")); time.sleep(SETTLE); print(cs("Pages"))
print("04 loadout (fresh)"); snap("04_loadout_fresh")
print(cs("Fill")); time.sleep(0.4)
print("05 loadout (filled, detail of the last card tapped)"); snap("05_loadout_filled")
print(cs("Press", "ReadyBtn")); time.sleep(1.6); print(cs("Pages"))
print("06 lobby"); snap("06_lobby")
# Join mode (join card + open seats, no READY! banner). Host mode is not captured: entering it
# creates a real UGS lobby.
print(cs("JoinLobby")); time.sleep(0.6)
print("06b lobby (join mode)"); snap("06b_lobby_join")
print("final:", cs("Info"))
bridge.call("editor-application-set-state", {"isPlaying": False})
