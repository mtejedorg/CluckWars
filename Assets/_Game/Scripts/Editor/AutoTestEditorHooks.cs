using CluckWars.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Editor side of the test-session tools. (1) An <c>AutoTest.EditorConfigure</c> spec lasts for ONE Play Mode
    /// session: it is cleared when Play Mode is exited, so a forgotten config cannot auto-host the next time someone
    /// presses Play. (2) "Cluck Wars / Diagnostics / Heavy Probes In Editor" turns the automatic Visual scans and the Sync
    /// heartbeat on in the Editor without an auto-test config (they are off there by default: <see cref="HeavyProbes"/>).
    /// </summary>
    [InitializeOnLoad]
    public static class AutoTestEditorHooks
    {
        private const string HeavyMenuPath = "Cluck Wars/Diagnostics/Heavy Probes In Editor";

        static AutoTestEditorHooks()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingPlayMode || string.IsNullOrEmpty(AutoTest.EditorSpec)) return;
            AutoTest.EditorClear();
            Debug.Log("[AutoTest] Editor auto-test config cleared on exiting Play Mode (EditorConfigure again for the next run).");
        }

        [MenuItem(HeavyMenuPath, priority = 40)]
        public static void ToggleHeavyProbes()
        {
            bool on = !EditorPrefs.GetBool(HeavyProbes.EditorPrefKey, false);
            EditorPrefs.SetBool(HeavyProbes.EditorPrefKey, on);
            Debug.Log($"[Diagnostics] Heavy probes in the Editor {(on ? "ON" : "OFF")} (applies from the next Play Mode entry).");
        }

        [MenuItem(HeavyMenuPath, true)]
        public static bool ValidateToggleHeavyProbes()
        {
            Menu.SetChecked(HeavyMenuPath, EditorPrefs.GetBool(HeavyProbes.EditorPrefKey, false));
            return true;
        }
    }
}
