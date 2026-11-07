"""Open a scene in the running Editor through the bridge (saves a dirty active scene first so no dialog blocks).

Usage: python tools/ui_capture/open_scene.py [Assets/_Game/Scenes/Bootstrap.unity]
"""
import sys, os, json
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge

path = sys.argv[1] if len(sys.argv) > 1 else "Assets/_Game/Scenes/Bootstrap.unity"
code = f"""
if (UnityEditor.EditorApplication.isPlaying) UnityEditor.EditorApplication.isPlaying = false;
var s = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
string before = s.path + " dirty=" + s.isDirty;
if (s.isDirty && !string.IsNullOrEmpty(s.path)) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s);
var opened = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("{path}", UnityEditor.SceneManagement.OpenSceneMode.Single);
return "was " + before + " -> now " + opened.path + " playing=" + UnityEditor.EditorApplication.isPlaying;
"""
bridge.init()
m = bridge.call("script-execute", {"csharpCode": code}, timeout=180)
print(json.dumps(m.get("result", {}))[:600])
