using System;
using System.Collections.Generic;
using CluckWars.Logging;
using UnityEngine;
using Zenject;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// Collects the auto-test keys from wherever this platform carries them: the process command line (Windows player,
    /// Editor), the launching intent's extras (Android: <c>adb shell am start -S -n &lt;pkg&gt;/&lt;activity&gt;
    /// -e cw_autojoin ABC123 -e cw_class Speedy</c>), and in the Editor the spec stored by
    /// <see cref="AutoTest.EditorConfigure"/> (survives the domain reload into Play Mode).
    /// </summary>
    /// <remarks>
    /// Android extras are read ONCE per process and then removed from the intent, so a recreated activity in the same
    /// process cannot hand them over again; an activity launched from Recents (the system restoring the old launch
    /// intent, <see cref="FlagActivityLaunchedFromHistory"/>) ignores them. Always launch a test run with
    /// <c>am start -S</c> (force-stop first) so each run is a fresh process with fresh extras.
    /// </remarks>
    public static class AutoTestArgs
    {
        private const string Source = "AutoTest";

        /// <summary><c>Intent.FLAG_ACTIVITY_LAUNCHED_FROM_HISTORY</c>: the user (or the system) re-opened the app from Recents.</summary>
        public const int FlagActivityLaunchedFromHistory = 0x00100000;

        /// <summary>True for a launch intent that is a restore from Recents, whose extras belong to an older test run.</summary>
        public static bool IsRestoredLaunch(int intentFlags) => (intentFlags & FlagActivityLaunchedFromHistory) != 0;

#if UNITY_ANDROID && !UNITY_EDITOR
        // The intent's cw_* extras, read once per process (they are removed from the intent after the read).
        private static Dictionary<string, string> _intentExtras;
#endif
        // What the one read had to say, logged by the first caller that has a logger.
        private static readonly List<string> IntentNotes = new List<string>();
        private static bool _intentNotesLogged;

        public static AutoTestConfig Read(ILogService log)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AutoTestConfig.AddCommandLine(Environment.GetCommandLineArgs(), map);
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_intentExtras == null)
            {
                _intentExtras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                ReadIntentExtrasOnce(_intentExtras, IntentNotes);
            }
            foreach (var kv in _intentExtras) map[kv.Key] = kv.Value;
#endif
#if UNITY_EDITOR
            AutoTestConfig.AddSpec(AutoTest.EditorSpec, map);
#endif
            if (log != null && !_intentNotesLogged && IntentNotes.Count > 0)
            {
                _intentNotesLogged = true;
                foreach (var note in IntentNotes) log.Warn(Source, note);
            }
            return AutoTestConfig.FromMap(map);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// The activity is <c>UnityPlayerGameActivity</c>; <c>AndroidApplication.currentActivity</c> (Unity 6) returns it
        /// for GameActivity and the classic activity alike, with the old <c>UnityPlayer.currentActivity</c> static as the
        /// fallback. Every <c>cw_*</c> extra is read through <c>Bundle.get(key).toString()</c>, so <c>-e</c>, <c>--ei</c>
        /// and <c>--ez</c> all work, then removed from the intent. A JNI failure becomes a Warn note and no keys.
        /// </summary>
        private static void ReadIntentExtrasOnce(IDictionary<string, string> map, List<string> notes)
        {
            try
            {
                var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                AndroidJavaClass player = null;
                if (activity == null)
                {
                    player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                }
                if (activity == null) { notes.Add("No current activity; intent extras not read."); player?.Dispose(); return; }

                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                using var extras = intent?.Call<AndroidJavaObject>("getExtras");
                if (extras != null)
                {
                    var found = new List<string>();
                    using (var keys = extras.Call<AndroidJavaObject>("keySet"))
                    using (var it = keys.Call<AndroidJavaObject>("iterator"))
                    {
                        while (it.Call<bool>("hasNext"))
                        {
                            using var keyObj = it.Call<AndroidJavaObject>("next");
                            var key = keyObj?.Call<string>("toString");
                            if (key != null && key.StartsWith(AutoTestConfig.KeyPrefix, StringComparison.OrdinalIgnoreCase)) found.Add(key);
                        }
                    }

                    bool restored = IsRestoredLaunch(intent.Call<int>("getFlags"));
                    foreach (var key in found)
                    {
                        if (!restored)
                        {
                            using var valueObj = extras.Call<AndroidJavaObject>("get", key);
                            map[key] = valueObj != null ? valueObj.Call<string>("toString") : "1";
                        }
                        // Consumed: a recreated activity in this process must not see them again.
                        intent.Call("removeExtra", key);
                    }
                    if (restored && found.Count > 0)
                        notes.Add($"Launched from Recents: {found.Count} cw_* extra(s) from an earlier launch ignored. " +
                                  "Start test runs with 'adb shell am start -S ...'.");
                }
                if (player != null) { activity.Dispose(); player.Dispose(); }
            }
            catch (Exception e)
            {
                notes.Add($"Reading the intent extras failed ({e.GetType().Name}: {e.Message}); Android auto-test keys ignored.");
            }
        }
#endif
    }

    /// <summary>
    /// Editor entry points for driving an auto-test from <c>script-execute</c> (the Unity MCP) without a command line:
    /// <c>CluckWars.Diagnostics.AutoTest.EditorConfigure("autohost=1 class=Warrior autostart=2")</c> before entering Play
    /// Mode. The spec lives in <c>SessionState</c>, so it survives the domain reload into Play Mode; it applies to that
    /// one Play Mode session and is cleared on exiting Play Mode (<c>AutoTestEditorHooks</c>). <see cref="EditorClear"/>
    /// clears it earlier.
    /// </summary>
    public static class AutoTest
    {
        public const string SessionKey = "CluckWars.AutoTest.Spec";

#if UNITY_EDITOR
        public static string EditorSpec => UnityEditor.SessionState.GetString(SessionKey, string.Empty);

        public static void EditorConfigure(string spec) => UnityEditor.SessionState.SetString(SessionKey, spec ?? string.Empty);

        public static void EditorClear() => UnityEditor.SessionState.EraseString(SessionKey);
#endif
    }

    /// <summary>
    /// Production builds (no dev tools): if anyone passes <c>cw_*</c> keys, say once that they are ignored. Nothing else.
    /// </summary>
    public sealed class AutoTestGuard : IInitializable
    {
        private readonly ILogService _log;

        public AutoTestGuard(ILogService log) { _log = log; }

        public void Initialize()
        {
            if (AutoTestArgs.Read(_log).AnyKey)
                _log.Warn("AutoTest", "AutoTest ignored: dev tools not available in this build");
        }
    }
}
