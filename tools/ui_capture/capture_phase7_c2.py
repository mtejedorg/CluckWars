"""Phase 7 chunk C2 captures: settings with the themed scrollbar, the invite card on narrow layouts, PLAY AGAIN sub-label.
Reuses the C# helper + snap() of capture_menu.py (everything before bridge.init()). Usage: python tools/ui_capture/capture_phase7_c2.py <out_dir>
"""
import sys, os, time
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "capture_menu.py"), encoding="utf-8").read()
head = src.split("bridge.init()")[0]
sys.argv = [sys.argv[0]] + sys.argv[1:2]
exec(compile(head, "capture_menu_head", "exec"))

EXTRA = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq;
public class Cap2 {
  static VisualElement Menu() { return Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None).Select(x => x.rootVisualElement).FirstOrDefault(r => r != null && r.Q<Button>("SoloBtn") != null); }
  public static string ShowInvite() { var r = Menu(); var inv = r.Q<VisualElement>("InviteCard"); var join = r.Q<VisualElement>("JoinCard");
    if (inv == null) return "NOINVITE"; inv.style.display = DisplayStyle.Flex; if (join != null) join.style.display = DisplayStyle.None;
    var tiles = r.Q<VisualElement>("CodeTiles"); if (tiles != null && tiles.childCount == 0) foreach (var ch in "K7QX2M") { var l = new Label(ch.ToString()); l.AddToClassList("cw-code-tile"); tiles.Add(l); }
    return "invite shown"; }
  public static string ScrollInfo() { var r = Menu(); var sv = r.Q<ScrollView>(className: "cw-sheet__body"); if (sv == null) return "NOSV";
    var d = sv.verticalScroller.Q(className: "unity-base-slider__dragger"); var t = sv.verticalScroller.Q(className: "unity-base-slider__tracker");
    return "scrollerW=" + sv.verticalScroller.resolvedStyle.width + " dragger=" + (d == null ? "null" : d.resolvedStyle.backgroundColor + " w" + d.resolvedStyle.width) + " tracker=" + (t == null ? "null" : t.resolvedStyle.backgroundColor + " w" + t.resolvedStyle.width) + " low=" + sv.verticalScroller.lowButton.resolvedStyle.display; }
}"""
EXTRA = EXTRA[:-1] + """  public static string PlayAgain() { var r = Menu(); var l = r.Q<Label>("PlayAgainSub"); return l == null ? "NOSUB" : "sub=" + l.text; }
}"""
def cs2(method):
    r = bridge.text(bridge.call("script-execute", {"csharpCode": EXTRA, "className": "Cap2", "methodName": method, "parameters": []}, timeout=90))
    try: return json.loads(r)["result"]["value"]
    except Exception: return r[:300]

bridge.init()
bridge.call("editor-application-set-state", {"isPlaying": True})
for _ in range(60):
    s = cs("Pages")
    if s.startswith("pages="): break
    time.sleep(1)
perf_prev = cs("SetPerf", "1"); time.sleep(1.5)
print(cs("Press", "SettingsBtn")); time.sleep(0.6)
print(cs2("ScrollInfo"))
snap("01d_settings_scrollbar")
print(cs("Press", "SettingsCloseBtn")); time.sleep(0.3)
print(cs("ClearLastSetup"), cs("Press", "SoloBtn")); time.sleep(0.9)
print(cs("TapEl", "ClassFatty"), cs("Press", "NextBtn")); time.sleep(0.9)
print(cs("Fill")); time.sleep(0.4)
print(cs("Press", "ReadyBtn")); time.sleep(1.6)
print(cs("JoinLobby")); time.sleep(0.6); print(cs2("ShowInvite")); time.sleep(0.4)
snap("06c_lobby_invite_card")
print(cs("Press", "BackBtn")); time.sleep(1.2)
for _ in range(3):
    print(cs("Press", "HomeBtn"), cs("Pages")); time.sleep(1.2)
print(cs2("PlayAgain"))
snap("01e_main_menu_play_again")
print(cs("RestorePerf", perf_prev)); print(cs("Restore"))
bridge.call("editor-application-set-state", {"isPlaying": False})
