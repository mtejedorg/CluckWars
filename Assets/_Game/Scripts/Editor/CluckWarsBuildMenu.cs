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
    /// One-button Windows + Android + iOS build menu under
    /// <c>Cluck Wars / Build / …</c>. Reads the scene list from
    /// <c>EditorBuildSettings</c>, applies per-platform player settings
    /// before kicking the build off, and drops output into <c>Builds/</c>
    /// (gitignored).
    /// </summary>
    /// <remarks>
    /// Lives in an <c>Editor/</c> folder so Unity only compiles it for the
    /// editor — never ships in a player build. No Editor assembly definition
    /// needed; the folder name is special-cased by Unity.
    ///
    /// iOS produces an <b>Xcode project directory</b>, not a signed .ipa —
    /// turning that into an installable build needs a Mac with Xcode, which
    /// this Windows workstation is not. Everything Mac-side (signing,
    /// provisioning, xcodebuild) is deliberately out of scope here.
    /// </remarks>
    public static class CluckWarsBuildMenu
    {
        private const string BuildsRoot      = "Builds";
        private const string WindowsSubdir   = "Windows";
        private const string AndroidSubdir   = "Android";
        private const string IosSubdir       = "iOS";
        private const string WindowsFileName = "CluckWars.exe";
        private const string LogTag          = "[Build]";

        /// <summary>Reverse-DNS bundle id applied to iOS builds.</summary>
        /// <remarks>
        /// The project was created from Unity's URP template and every platform
        /// still carries a <c>com.Unity-Technologies.com.unity.template.*</c>
        /// identifier. We fix iOS here because it has no installed builds to
        /// break yet. Android and Standalone are deliberately left alone —
        /// changing an Android applicationIdentifier makes existing sideloaded
        /// installs (the Pixel 9 test device) a *different* app: no in-place
        /// upgrade, separate PlayerPrefs. That's Maestro's call, not this
        /// script's.
        /// </remarks>
        private const string IosBundleIdentifier = "com.cluckwars.game";

        /// <summary>
        /// Minimum iOS version. 15.0 covers iPhone 6s and newer, which is well
        /// below the 2021+ device floor in the GDD — same reasoning as the
        /// Android minSdkVersion 24 block. Only ever raised, never lowered, so
        /// a deliberate bump in Player Settings survives a build.
        /// </summary>
        private static readonly Version MinIosVersion = new Version(15, 0);

        // Keyboard shortcuts: % = Ctrl/Cmd, # = Shift, & = Alt.
        // Ctrl+Shift+W / Ctrl+Shift+A / Ctrl+Shift+B.
        // iOS intentionally has no shortcut: Ctrl+Shift+I is claimed by Unity
        // and by browsers/devtools, and a collision is worse than a mouse trip.
        // Menu entry points must return void (Unity reserves bool-returning
        // MenuItem methods for validation), so each delegates to a Do* helper
        // that reports success for Build All to summarise.
        [MenuItem("Cluck Wars/Build/Windows %#w", priority = 1)]
        public static void BuildWindows() => DoWindows();

        [MenuItem("Cluck Wars/Build/Android %#a", priority = 2)]
        public static void BuildAndroid() => DoAndroid();

        [MenuItem("Cluck Wars/Build/iOS", priority = 3)]
        public static void BuildIos() => DoIos();

        [MenuItem("Cluck Wars/Build/All %#b", priority = 4)]
        public static void BuildAll()
        {
            // Validate once up front rather than letting all three targets fail
            // with the same scene-list error.
            if (!ValidateScenes()) return;

            bool windows = false, android = false, ios = false;
            try
            {
                // A failed target does not abort the rest: the three toolchains
                // fail independently, and before a test session it's more useful
                // to learn all three verdicts at once than just the first.
                windows = DoWindows();
                android = DoAndroid();
                ios     = DoIos();
            }
            finally
            {
                // Must happen even if a build threw. Leaving the Editor on the
                // Android or iOS target causes the domain-reload stall that
                // breaks Play Mode and the MCP bridge.
                RestoreWindowsTarget();
            }

            var summary = $"{LogTag} Build All — Windows: {Verdict(windows)}, " +
                          $"Android: {Verdict(android)}, iOS: {Verdict(ios)}";
            if (windows && android && ios) Debug.Log(summary);
            else Debug.LogError(summary + "  One or more targets did not succeed; see the errors above.");
        }

        [MenuItem("Cluck Wars/Build/Reveal Builds Folder", priority = 20)]
        public static void RevealBuilds()
        {
            var path = Path.GetFullPath(BuildsRoot);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        // ---- Per-target entry points (shared by the menu items and Build All) --

        private static bool DoWindows() =>
            Build(BuildTarget.StandaloneWindows64, WindowsSubdir, WindowsFileName);

        private static bool DoAndroid()
        {
            ConfigureAndroidPlayerSettings();
            return Build(BuildTarget.Android, AndroidSubdir, $"CluckWars-{SanitizedVersion()}.apk");
        }

        private static bool DoIos()
        {
            ConfigureIosPlayerSettings();
            // No filename: for iOS, locationPathName is the Xcode project
            // *directory*, so Builds/iOS is itself the output path.
            return Build(BuildTarget.iOS, IosSubdir, null);
        }

        // ---- Core build pipeline ----------------------------------------------

        /// <summary>
        /// Builds one target into <c>Builds/&lt;subfolder&gt;</c>.
        /// </summary>
        /// <param name="filename">
        /// Output file name, or null/empty when the target's output is a
        /// directory rather than a file (iOS Xcode project) — in that case
        /// <c>Builds/&lt;subfolder&gt;</c> is itself the build location.
        /// </param>
        /// <returns>True only if the build reported <c>Succeeded</c>.</returns>
        private static bool Build(BuildTarget target, string subfolder, string filename)
        {
            if (!ValidateScenes()) return false;
            if (!ValidateTargetSupported(target)) return false;

            // Switching build target re-imports assets for the new platform — slow
            // but necessary so the build doesn't fail with stale shader variants etc.
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                var group = BuildPipeline.GetBuildTargetGroup(target);
                Debug.Log($"{LogTag} Switching active build target → {target}…");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                {
                    Debug.LogError($"{LogTag} Failed to switch to {target}. Check the Editor Console for details.");
                    return false;
                }
            }

            var location = string.IsNullOrEmpty(filename)
                ? Path.Combine(BuildsRoot, subfolder)
                : Path.Combine(BuildsRoot, subfolder, filename);

            // When the location IS the directory, create it; otherwise create its parent.
            var dir = string.IsNullOrEmpty(filename) ? location : Path.GetDirectoryName(location);
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
                // Deliberately NOT AcceptExternalModificationsToPlayer (Xcode "append"
                // mode). Append preserves hand-edits to the generated Xcode project,
                // but we make none — no native plugins, no signing config, nothing
                // Mac-side. Append also goes stale when the Unity project structure
                // changes, which is the classic source of "works on my machine"
                // Xcode exports. A clean replace every time is reproducible.
                options = BuildOptions.None,
            };

            Debug.Log($"{LogTag} {target} → {Path.GetFullPath(location)}  (scenes: {options.scenes.Length})");
            var report = BuildPipeline.BuildPlayer(options);
            var succeeded = LogResult(target, location, report);

            if (succeeded && target == BuildTarget.iOS)
            {
                Debug.Log($"{LogTag} iOS output is an Xcode project, not an installable .ipa. " +
                          $"Copy {Path.GetFullPath(location)} to a Mac and build/sign it in Xcode.");
            }
            return succeeded;
        }

        /// <returns>True only for <c>BuildResult.Succeeded</c>.</returns>
        private static bool LogResult(BuildTarget target, string location, BuildReport report)
        {
            var s = report.summary;
            switch (s.result)
            {
                case BuildResult.Succeeded:
                    var mb = s.totalSize / (1024d * 1024d);
                    Debug.Log($"{LogTag} {target} OK. Size: {mb:0.0} MB, Time: {s.totalTime.TotalSeconds:0.0}s, Output: {Path.GetFullPath(location)}");
                    return true;
                case BuildResult.Cancelled:
                    Debug.LogWarning($"{LogTag} {target} cancelled.");
                    return false;
                case BuildResult.Failed:
                    Debug.LogError($"{LogTag} {target} FAILED. Errors: {s.totalErrors}, Warnings: {s.totalWarnings}. See Console for details.");
                    return false;
                default:
                    Debug.LogError($"{LogTag} {target} ended with unknown result '{s.result}'.");
                    return false;
            }
        }

        private static void RestoreWindowsTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64) return;

            Debug.Log($"{LogTag} Restoring build target → StandaloneWindows64.");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError($"{LogTag} Could not restore the active build target to StandaloneWindows64. " +
                               "Switch it back manually (File → Build Profiles) — leaving the Editor on a " +
                               "mobile target stalls domain reloads and breaks Play Mode and the MCP bridge.");
            }
        }

        private static string Verdict(bool ok) => ok ? "OK" : "FAILED";

        // ---- Validation -------------------------------------------------------

        /// <summary>
        /// Fails loudly when the platform module for <paramref name="target"/>
        /// isn't installed, instead of letting the target switch fail with a
        /// generic message.
        /// </summary>
        private static bool ValidateTargetSupported(BuildTarget target)
        {
            var group = BuildPipeline.GetBuildTargetGroup(target);
            if (BuildPipeline.IsBuildTargetSupported(group, target)) return true;

            Debug.LogError($"{LogTag} {target} is not supported by this Editor install — " +
                           $"the \"{ModuleName(target)}\" module is missing. Install it via Unity Hub → " +
                           $"Installs → {Application.unityVersion} → gear icon → Add modules, then restart the Editor.");
            return false;
        }

        /// <summary>Unity Hub's display name for the module backing a target.</summary>
        private static string ModuleName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64: return "Windows Build Support (IL2CPP)";
                case BuildTarget.Android:             return "Android Build Support";
                case BuildTarget.iOS:                 return "iOS Build Support";
                default:                              return target + " Build Support";
            }
        }

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
            // Android-only setting — deliberately absent from the iOS path.
            EditorUserBuildSettings.buildAppBundle = false;
        }

        // ---- iOS-specific tuning ---------------------------------------------

        private static void ConfigureIosPlayerSettings()
        {
            // Scripting backend is NOT set here: iOS is always IL2CPP (Apple bans
            // JIT, so Unity offers no choice). Nothing to assert.
            // Architecture is NOT set here either: iOS is ARM64-only in Unity 6,
            // and the surviving API is the untyped PlayerSettings.SetArchitecture
            // (NamedBuildTarget, int) with no iOS enum to name the value — writing
            // a magic int would be guesswork with no upside.
            // Graphics API is left on Automatic: Metal is the only iOS option.
            var named = NamedBuildTarget.iOS;

            if (PlayerSettings.GetApplicationIdentifier(named) != IosBundleIdentifier)
            {
                Debug.Log($"{LogTag} Setting iOS bundle identifier → {IosBundleIdentifier} " +
                          $"(was '{PlayerSettings.GetApplicationIdentifier(named)}').");
                PlayerSettings.SetApplicationIdentifier(named, IosBundleIdentifier);
            }

            // Cluck Wars is a phone game with a twin-stick touch HUD laid out for a
            // phone aspect; shipping an iPad binary would promise a tablet layout
            // nobody has tuned. iPhone-only until that's deliberately designed.
            if (PlayerSettings.iOS.targetDevice != iOSTargetDevice.iPhoneOnly)
            {
                Debug.Log($"{LogTag} Setting iOS target device {PlayerSettings.iOS.targetDevice} → iPhoneOnly.");
                PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            }

            // Only ever raise the floor — see MinIosVersion. An unparseable value
            // is treated as "too low" so we end up somewhere known-good.
            var current = PlayerSettings.iOS.targetOSVersionString;
            if (!Version.TryParse(current, out var parsed) || parsed < MinIosVersion)
            {
                Debug.Log($"{LogTag} Raising iOS target OS version '{current}' → {MinIosVersion}.");
                PlayerSettings.iOS.targetOSVersionString = MinIosVersion.ToString();
            }

            // Device SDK, not the simulator SDK — the export is meant for real hardware.
            if (PlayerSettings.iOS.sdkVersion != iOSSdkVersion.DeviceSDK)
            {
                Debug.Log($"{LogTag} Setting iOS SDK → DeviceSDK (was {PlayerSettings.iOS.sdkVersion}).");
                PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            }
        }

        // ---- Misc helpers ----------------------------------------------------

        private static string SanitizedVersion()
        {
            var v = PlayerSettings.bundleVersion ?? "0.0.0";
            return string.Concat(v.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_'));
        }
    }
}
