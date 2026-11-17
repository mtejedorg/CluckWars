using CluckWars.Balance;
using UnityEditor;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Editor side of the live-ops balance layer. (1) "Cluck Wars / Balance / Remote Config In Editor" lets Play Mode read
    /// the dev environment's <c>balance</c> key and this machine's cache; off by default, so day-to-day Play Mode runs the
    /// assets as they are on disk (<see cref="BalanceSourceChoice.Decide"/>). (2) A guard: in the Editor a delta writes to
    /// the real ability / class / match assets, so the live <see cref="BalanceBook"/> restores them before a domain reload
    /// and when Play Mode ends, even if the ProjectContext teardown did not run.
    /// </summary>
    [InitializeOnLoad]
    public static class BalanceEditorHooks
    {
        private const string MenuPath = "Cluck Wars/Balance/Remote Config In Editor";

        static BalanceEditorHooks()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= RestoreLiveBook;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreLiveBook;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode) RestoreLiveBook();
        }

        private static void RestoreLiveBook() => BalanceBook.Live?.RestoreBaseline();

        [MenuItem(MenuPath, priority = 50)]
        public static void ToggleRemoteConfigInEditor()
        {
            bool on = !EditorPrefs.GetBool(BalanceSourceChoice.EditorPrefKey, false);
            EditorPrefs.SetBool(BalanceSourceChoice.EditorPrefKey, on);
            UnityEngine.Debug.Log($"[Balance] Remote Config in the Editor {(on ? "ON (dev environment, cache)" : "OFF (assets as on disk)")}; applies from the next Play Mode entry.");
        }

        [MenuItem(MenuPath, true)]
        public static bool ValidateToggleRemoteConfigInEditor()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(BalanceSourceChoice.EditorPrefKey, false));
            return true;
        }
    }
}
