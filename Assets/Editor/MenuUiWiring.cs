#if UNITY_EDITOR
using CluckWars.Bootstrap;
using CluckWars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// One-click wiring for the UI Toolkit menu front-end. Creates (or updates) a
    /// <c>MenuUI</c> GameObject in the Bootstrap scene with a <see cref="UIDocument"/>
    /// (→ PanelSettings + the three menu UXML assigned to <see cref="MenuUiController"/>)
    /// and disables the broken legacy <c>MenuCanvas</c> + old controllers.
    ///
    /// Replaces hand-editing scene YAML — uses real Unity APIs so references resolve
    /// correctly. Run via <b>Cluck Wars ▸ UI ▸ Wire Menu (UI Toolkit)</b>, then press Play.
    /// </summary>
    public static class MenuUiWiring
    {
        private const string PanelSettingsPath = "Assets/Resources/PanelSettings.asset";
        private const string MainMenuPath      = "Assets/UI/MainMenu.uxml";
        private const string CharSelectPath    = "Assets/UI/CharacterSelect.uxml";
        private const string LobbyPath         = "Assets/UI/Lobby.uxml";

        [MenuItem("Cluck Wars/UI/Wire Menu (UI Toolkit)")]
        public static void WireMenu()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.name != "Bootstrap")
            {
                EditorUtility.DisplayDialog("Wire Menu",
                    "Open the Bootstrap scene first (Assets/_Game/Scenes/Bootstrap.unity), then run this again.",
                    "OK");
                return;
            }

            // Load assets.
            var panel      = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var mainMenu   = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainMenuPath);
            var charSelect = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CharSelectPath);
            var lobby      = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LobbyPath);

            if (panel == null || mainMenu == null || charSelect == null || lobby == null)
            {
                EditorUtility.DisplayDialog("Wire Menu",
                    $"Missing asset(s):\n" +
                    $"PanelSettings: {(panel != null)}\nMainMenu: {(mainMenu != null)}\n" +
                    $"CharacterSelect: {(charSelect != null)}\nLobby: {(lobby != null)}\n\n" +
                    "Let the AssetDatabase import finish and retry.",
                    "OK");
                return;
            }

            // Find or create MenuUI.
            var go = GameObject.Find("MenuUI");
            if (go == null) go = new GameObject("MenuUI");

            var doc = go.GetComponent<UIDocument>();
            if (doc == null) doc = go.AddComponent<UIDocument>();
            doc.panelSettings   = panel;
            doc.visualTreeAsset = mainMenu; // a non-null source VTA; controller swaps pages at runtime

            var ctrl = go.GetComponent<MenuUiController>();
            if (ctrl == null) ctrl = go.AddComponent<MenuUiController>();

            // Assign the three UXML refs via SerializedObject (private [SerializeField]s).
            var so = new SerializedObject(ctrl);
            so.FindProperty("_mainMenuUxml").objectReferenceValue        = mainMenu;
            so.FindProperty("_characterSelectUxml").objectReferenceValue = charSelect;
            so.FindProperty("_lobbyUxml").objectReferenceValue           = lobby;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Disable legacy UGUI menu + old controllers so they don't fight the new UI.
            var menuCanvas = GameObject.Find("MenuCanvas");
            if (menuCanvas != null) menuCanvas.SetActive(false);

            var bootstrap = GameObject.Find("Bootstrap");
            if (bootstrap != null)
            {
                var oldCsc = bootstrap.GetComponent<CharacterSelectController>();
                if (oldCsc != null) oldCsc.enabled = false;

                // Keep SceneLoader; MenuUiController suppresses its auto-load at runtime.
                var sl = bootstrap.GetComponent<SceneLoader>();
                if (sl == null) go.AddComponent<SceneLoader>(); // ensure one exists for LoadNext()
            }
            else
            {
                if (go.GetComponent<SceneLoader>() == null) go.AddComponent<SceneLoader>();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = go;

            Debug.Log("[MenuUiWiring] MenuUI wired (UIDocument + MenuUiController + 3 UXML + PanelSettings). " +
                      "Legacy MenuCanvas disabled. Press Play to test.");
        }

        [MenuItem("Cluck Wars/UI/Revert to Legacy MenuCanvas")]
        public static void RevertToLegacy()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != "Bootstrap") { Debug.LogWarning("[MenuUiWiring] Open Bootstrap first."); return; }

            var menuUi = GameObject.Find("MenuUI");
            if (menuUi != null) menuUi.SetActive(false);

            var menuCanvas = GameObject.Find("MenuCanvas");
            if (menuCanvas != null) menuCanvas.SetActive(true);

            var bootstrap = GameObject.Find("Bootstrap");
            var oldCsc = bootstrap != null ? bootstrap.GetComponent<CharacterSelectController>() : null;
            if (oldCsc != null) oldCsc.enabled = true;

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[MenuUiWiring] Reverted: MenuUI disabled, legacy MenuCanvas re-enabled.");
        }
    }
}
#endif
