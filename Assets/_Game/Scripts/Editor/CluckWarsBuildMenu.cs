using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// One-button Windows + Android build menu under
    /// <c>Cluck Wars / Build / …</c>. Reads the scene list from
    /// <c>EditorBuildSettings</c>, configures Android for IL2CPP + ARM64
    /// before kicking the build off, and drops output into <c>Builds/</c>
    /// (gitignored).
    /// </summary>
    /// <remarks>
    /// Lives in an <c>Editor/</c> folder so Unity only compiles it for the
    /// editor — never ships in a player build. No Editor assembly definition
    /// needed; the folder name is special-cased by Unity.
    /// </remarks>
    public static class CluckWarsBuildMenu
    {
        private const string BuildsRoot      = "Builds";
        private const string WindowsSubdir   = "Windows";
        private const string AndroidSubdir   = "Android";
        private const string WindowsFileName = "CluckWars.exe";
        private const string LogTag          = "[Build]";

        // Keyboard shortcuts: % = Ctrl/Cmd, # = Shift, & = Alt.
        // Ctrl+Shift+W / Ctrl+Shift+A / Ctrl+Shift+B.
        [MenuItem("Cluck Wars/Build/Windows %#w", priority = 1)]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, WindowsSubdir, WindowsFileName);
        }

        [MenuItem("Cluck Wars/Build/Android %#a", priority = 2)]
        public static void BuildAndroid()
        {
            ConfigureAndroidPlayerSettings();
            var apkName = $"CluckWars-{SanitizedVersion()}.apk";
            Build(BuildTarget.Android, AndroidSubdir, apkName);
        }

        [MenuItem("Cluck Wars/Build/Android (Emulator)", priority = 4)]
        public static void BuildAndroidEmulator()
        {
            // Unity 6000.3+: AndroidArchitecture.X86_64 is internally "x86-64 (Magic Leap)"
            // and is rejected by BuildPipeline.BuildPlayer. ARM64-only APK runs on modern
            // Android 12+ emulators via the native ARM translation bridge.
            ConfigureAndroidPlayerSettings();
            Debug.Log($"{LogTag} Emulator build: ARM64 (x86_64 removed — Unity 6 treats it as Magic Leap).");
            var apkName = $"CluckWars-{SanitizedVersion()}-emu.apk";
            Build(BuildTarget.Android, AndroidSubdir, apkName);
        }

        [MenuItem("Cluck Wars/Build/Android + Restore Windows Target", priority = 3)]
        public static void BuildAndroidAndRestoreTarget()
        {
            BuildAndroid();
            // Switch back so subsequent Editor play-mode sessions and MCP calls
            // work without the domain-reload stall caused by the Android target switch.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                Debug.Log($"{LogTag} Restoring build target → StandaloneWindows64.");
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            }
        }

        [MenuItem("Cluck Wars/Build/Windows + Android %#b", priority = 5)]
        public static void BuildAll()
        {
            BuildWindows();
            BuildAndroid();
        }

        [MenuItem("Cluck Wars/Build/Reveal Builds Folder", priority = 20)]
        public static void RevealBuilds()
        {
            var path = Path.GetFullPath(BuildsRoot);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        // ---- Core build pipeline ----------------------------------------------

        private static void Build(BuildTarget target, string subfolder, string filename)
        {
            if (!ValidateScenes()) return;

            // Switching build target re-imports assets for the new platform — slow
            // but necessary so the build doesn't fail with stale shader variants etc.
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                var group = BuildPipeline.GetBuildTargetGroup(target);
                Debug.Log($"{LogTag} Switching active build target → {target}…");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                {
                    Debug.LogError($"{LogTag} Failed to switch to {target}. Check the Editor Console for details (typically: missing platform module).");
                    return;
                }
            }

            var location = Path.Combine(BuildsRoot, subfolder, filename);
            var dir = Path.GetDirectoryName(location);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes
                    .Where(s => s.enabled)
                    .Select(s => s.path)
                    .ToArray(),
                locationPathName = location,
                target = target,
                options = BuildOptions.None,
            };

            Debug.Log($"{LogTag} {target} → {Path.GetFullPath(location)}  (scenes: {options.scenes.Length})");
            var report = BuildPipeline.BuildPlayer(options);
            LogResult(target, location, report);
        }

        private static void LogResult(BuildTarget target, string location, BuildReport report)
        {
            var s = report.summary;
            switch (s.result)
            {
                case BuildResult.Succeeded:
                    var mb = s.totalSize / (1024d * 1024d);
                    Debug.Log($"{LogTag} {target} OK. Size: {mb:0.0} MB, Time: {s.totalTime.TotalSeconds:0.0}s, Output: {Path.GetFullPath(location)}");
                    break;
                case BuildResult.Cancelled:
                    Debug.LogWarning($"{LogTag} {target} cancelled.");
                    break;
                case BuildResult.Failed:
                    Debug.LogError($"{LogTag} {target} FAILED. Errors: {s.totalErrors}, Warnings: {s.totalWarnings}. See Console for details.");
                    break;
                default:
                    Debug.LogError($"{LogTag} {target} ended with unknown result.");
                    break;
            }
        }

        // ---- Validation -------------------------------------------------------

        private static bool ValidateScenes()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes == null || scenes.Length == 0)
            {
                Debug.LogError($"{LogTag} No scenes in Build Settings. Add Bootstrap.unity (index 0) and Game.unity (File → Build Profiles).");
                return false;
            }

            var firstEnabled = scenes.FirstOrDefault(s => s.enabled);
            if (firstEnabled == null)
            {
                Debug.LogError($"{LogTag} All scenes in Build Settings are disabled — nothing to build.");
                return false;
            }
            if (!firstEnabled.path.EndsWith("/Bootstrap.unity", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"{LogTag} First enabled scene is '{firstEnabled.path}', not Bootstrap.unity. The player will start in the wrong scene unless Bootstrap is index 0.");
            }
            return true;
        }

        // ---- Android-specific tuning -----------------------------------------

        private static void ConfigureAndroidPlayerSettings()
        {
            // IL2CPP + ARM64 is the demo target per ROADMAP Phase 8.
            // Skipping ARMv7 keeps the APK small and matches mid-range 2021+ hardware.
            var named = NamedBuildTarget.Android;
            if (PlayerSettings.GetScriptingBackend(named) != ScriptingImplementation.IL2CPP)
            {
                Debug.Log($"{LogTag} Setting Android scripting backend → IL2CPP.");
                PlayerSettings.SetScriptingBackend(named, ScriptingImplementation.IL2CPP);
            }
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                Debug.Log($"{LogTag} Setting Android target architectures → ARM64 only.");
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            }

            // Min API 24 (Android 7.0) covers any 2021+ device the GDD targets.
            var desiredMin = AndroidSdkVersions.AndroidApiLevel24;
            if (PlayerSettings.Android.minSdkVersion < desiredMin)
            {
                Debug.Log($"{LogTag} Raising Android minSdkVersion {PlayerSettings.Android.minSdkVersion} → {desiredMin}.");
                PlayerSettings.Android.minSdkVersion = desiredMin;
            }

            // .apk for sideloading the demo. Flip to AAB later if we go through Play Store.
            EditorUserBuildSettings.buildAppBundle = false;
        }

        // ---- Misc helpers ----------------------------------------------------

        private static string SanitizedVersion()
        {
            var v = PlayerSettings.bundleVersion ?? "0.0.0";
            return string.Concat(v.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_'));
        }
    }
}
