using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// One-click launcher for the Ability Lab dev scene, under <c>Cluck Wars / Test / …</c>.
    /// Opens <c>AbilityLab.unity</c> and enters play mode in a single action.
    /// </summary>
    /// <remarks>
    /// Lives in an <c>Editor/</c> folder, so Unity never compiles it into a player build —
    /// same arrangement as <see cref="CluckWarsBuildMenu"/>, and no assembly definition
    /// needed. The lab's runtime scripts are separately fenced behind <c>#if UNITY_EDITOR</c>.
    /// </remarks>
    public static class AbilityLabMenu
    {
        private const string ScenePath = "Assets/_Game/Scenes/AbilityLab.unity";
        private const string MenuPath = "Cluck Wars/Test/Ability Lab";

        [MenuItem(MenuPath, priority = 200)]
        public static void OpenAbilityLab()
        {
            if (EditorApplication.isPlaying)
            {
                // Opening a scene mid-play would be discarded on exit. Stop first and let the
                // user press the menu item again rather than fighting the play-state machine.
                EditorApplication.isPlaying = false;
                Debug.Log("[AbilityLab] Stopped play mode. Run 'Cluck Wars / Test / Ability Lab' again to launch the lab.");
                return;
            }

            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("Ability Lab",
                    $"{ScenePath} is missing.\n\nIt should be committed alongside the lab scripts — " +
                    "restore it from git rather than recreating it by hand, or the scene's " +
                    "SceneContext / GameInstaller wiring will be lost.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }
    }
}
