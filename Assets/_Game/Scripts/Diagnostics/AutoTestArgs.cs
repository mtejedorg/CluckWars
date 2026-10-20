using System;
using System.Collections.Generic;
using CluckWars.Logging;
using UnityEngine;
using Zenject;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// Collects the auto-test keys from wherever this platform carries them: the process command line (Windows player,
    /// Editor), the launching intent's string extras (Android: <c>adb shell am start -n &lt;pkg&gt;/&lt;activity&gt;
    /// -e cw_autojoin ABC123 -e cw_class Speedy</c>), and in the Editor the spec stored by
    /// <see cref="AutoTest.EditorConfigure"/> (survives the domain reload into Play Mode).
    /// </summary>
    public static class AutoTestArgs
    {
        private const string Source = "AutoTest";

        public static AutoTestConfig Read(ILogService log)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AutoTestConfig.AddCommandLine(Environment.GetCommandLineArgs(), map);
#if UNITY_ANDROID && !UNITY_EDITOR
            AddIntentExtras(map, log);
#endif
#if UNITY_EDITOR
            AutoTestConfig.AddSpec(AutoTest.EditorSpec, map);
#endif
            return AutoTestConfig.FromMap(map);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// The activity is <c>UnityPlayerGameActivity</c>; <c>AndroidApplication.currentActivity</c> (Unity 6) returns it
        /// for GameActivity and the classic activity alike, with the old <c>UnityPlayer.currentActivity</c> static as the
        /// fallback. Every <c>cw_*</c> extra is read through <c>Bundle.get(key).toString()</c>, so <c>-e</c>, <c>--ei</c>
        /// and <c>--ez</c> all work. A JNI failure is logged at Warn and leaves the map as it was.
        /// </summary>
        private static void AddIntentExtras(IDictionary<string, string> map, ILogService log)
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
                if (activity == null) { log?.Warn(Source, "No current activity; intent extras not read."); player?.Dispose(); return; }

                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                using var extras = intent?.Call<AndroidJavaObject>("getExtras");
                if (extras != null)
                {
                    using var keys = extras.Call<AndroidJavaObject>("keySet");
                    using var it = keys.Call<AndroidJavaObject>("iterator");
                    while (it.Call<bool>("hasNext"))
                    {
                        using var keyObj = it.Call<AndroidJavaObject>("next");
                        var key = keyObj?.Call<string>("toString");
                        if (key == null || !key.StartsWith(AutoTestConfig.KeyPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        using var valueObj = extras.Call<AndroidJavaObject>("get", key);
                        map[key] = valueObj != null ? valueObj.Call<string>("toString") : "1";
                    }
                }
                if (player != null) { activity.Dispose(); player.Dispose(); }
            }
            catch (Exception e)
            {
                log?.Warn(Source, $"Reading the intent extras failed ({e.GetType().Name}: {e.Message}); Android auto-test keys ignored.");
            }
        }
#endif
    }

    /// <summary>
    /// Editor entry points for driving an auto-test from <c>script-execute</c> (the Unity MCP) without a command line:
    /// <c>CluckWars.Diagnostics.AutoTest.EditorConfigure("autohost=1 class=Warrior autostart=2")</c> before entering Play
    /// Mode, <see cref="EditorClear"/> afterwards. The spec lives in <c>SessionState</c>, so it survives the domain reload
    /// into Play Mode and ends with the Editor session; it applies to every Play Mode entry until cleared.
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
