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
    /// One-button Windows + Android build menu under <c>Cluck Wars / Build / …</c>,
    /// plus an opt-in iOS export. Reads the scene list from
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
    /// provisioning, xcodebuild) is deliberately out of scope here. Since
    /// there's no Mac to finish the job on, iOS is greyed out by default
    /// (toggle via <c>Build/Enable iOS Export</c>) and <c>Build/All</c> never
    /// includes it — an iOS export nobody can act on shouldn't hold up or
    /// clutter a routine Windows+Android build.
    /// </remarks>
    public static class CluckWarsBuildMenu
    {
        private const string BuildsRoot      = "Builds";
        private const string WindowsSubdir   = "Windows";
        private const string AndroidSubdir   = "Android";
        private const string IosSubdir       = "iOS";
        private const string WindowsFileName = "CluckWars.exe";
        private const string LogTag          = "[Build]";
        private const string EnableIosMenuPath = "Cluck Wars/Build/Enable iOS Export (needs a Mac)";
        private const string EnableIosPrefKey  = "CluckWars.Build.EnableIos";

        /// <summary>Reverse-DNS application id pinned for Android and iOS (see <see cref="BuildGuards.AppIdentifier"/>).</summary>
        /// <remarks>
        /// The project came from Unity's URP template, so the identifiers were never set: headless builds fell back to
        /// <c>com.DefaultCompany.CluckWars</c> next to older <c>com.UnityTechnologies.*</c> installs (CW-201). It is now pinned
        /// in ProjectSettings and re-asserted here on every Android/iOS build. Changing it again makes sideloaded installs a
        /// *different* app (no in-place upgrade, separate PlayerPrefs), so treat it as frozen.
        /// </remarks>
        private const string AppIdentifier = BuildGuards.AppIdentifier;
        private const string IosBundleIdentifier = AppIdentifier;

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
        public static void BuildWindows() => WithBuildNumber(DoWindows);

        [MenuItem("Cluck Wars/Build/Android %#a", priority = 2)]
        public static void BuildAndroid() => WithBuildNumber(DoAndroid);

        [MenuItem("Cluck Wars/Build/iOS", priority = 3)]
        public static void BuildIos()
        {
            // Belt-and-braces: the validate function below greys this item out
            // when disabled, but guard the entry point too in case it's ever
            // invoked another way (script-execute, a remapped shortcut).
            if (!IsIosEnabled())
            {
                Debug.LogError($"{LogTag} iOS build is disabled. Enable it via " +
                                $"\"{EnableIosMenuPath}\" first — remember the output still " +
                                "needs a Mac with Xcode to become an installable app.");
                return;
            }
            WithBuildNumber(DoIos);
        }

        [MenuItem("Cluck Wars/Build/iOS", true)]
        public static bool ValidateBuildIos() => IsIosEnabled();

        [MenuItem(EnableIosMenuPath, priority = 6)]
        public static void ToggleIosEnabled()
        {
            var enabled = !IsIosEnabled();
            EditorPrefs.SetBool(EnableIosPrefKey, enabled);
            Debug.Log(enabled
                ? $"{LogTag} iOS build enabled. Output is still an Xcode project export, not an " +
                  "installable app — finishing it needs a Mac with Xcode."
                : $"{LogTag} iOS build disabled — Build/iOS is greyed out and Build/All will skip it.");
        }

        [MenuItem(EnableIosMenuPath, true)]
        public static bool ValidateToggleIosEnabled()
        {
            Menu.SetChecked(EnableIosMenuPath, IsIosEnabled());
            return true;
        }

        [MenuItem("Cluck Wars/Build/All %#b", priority = 4)]
        public static void BuildAll() => WithBuildNumber(BuildAllCore);

        private static bool BuildAllCore()
        {
            // Validate once up front rather than letting all targets fail with
            // the same scene-list error.
            if (!ValidateScenes()) return false;

            // iOS is deliberately never part of Build All, regardless of the
            // Enable iOS Export toggle — an export nobody on this machine can
            // finish (no Mac) shouldn't hold up or clutter a routine build.
            bool windows = false, android = false;
            try
            {
                // A failed target does not abort the rest: the two toolchains
                // fail independently, and before a test session it's more useful
                // to learn both verdicts at once than just the first.
                windows = DoWindows();
                android = DoAndroid();
            }
            finally
            {
                // Must happen even if a build threw. Leaving the Editor on the
                // Android target causes the domain-reload stall that breaks
                // Play Mode and the MCP bridge.
                RestoreWindowsTarget();
            }

            var summary = $"{LogTag} Build All — Windows: {Verdict(windows)}, Android: {Verdict(android)}";
            if (windows && android) Debug.Log(summary);
            else Debug.LogError(summary + "  One or more targets did not succeed; see the errors above.");
            return windows || android;
        }

        /// <summary>
        /// Test-session build (docs/TEST_SESSION_PLAN_2026-10.md §2): Windows then Android, both compiled with
        /// <c>CLUCKWARS_DEV_TOOLS</c> so the instrumentation (PerfProbe, Sync heartbeat, Visual probe, net line,
        /// auto-test, touch overlay) is in. Still <c>BuildOptions.None</c>: a non-development player, so the frame
        /// times it logs are the ones a player would get. Restores the Windows target afterwards, like Build All.
        /// </summary>
        [MenuItem(TestSessionMenuPath, priority = 5)]
        public static void BuildTestSession() => WithBuildNumber(BuildTestSessionCore);

        private static bool BuildTestSessionCore()
        {
            if (!ValidateScenes()) return false;
            bool windows = false, android = false;
            _forceDevTools = true;
            try
            {
                windows = DoWindows();
                android = DoAndroid();
            }
            finally
            {
                _forceDevTools = false;
                RestoreWindowsTarget();
            }

            var summary = $"{LogTag} Test Session build (dev tools) — Windows: {Verdict(windows)}, Android: {Verdict(android)}";
            if (windows && android) Debug.Log(summary);
            else Debug.LogError(summary + "  One or more targets did not succeed; see the errors above.");
            return windows || android;
        }

        private const string TestSessionMenuPath = "Cluck Wars/Build/Test Session (Windows + Android, dev tools)";

        /// <summary>Command-line switch that turns the dev-tools define on for any build menu entry.</summary>
        public const string DevToolsArg = "-cwDevTools";

        /// <summary>Environment variable that does the same (value <c>1</c>).</summary>
        public const string DevToolsEnvVar = "CW_DEV_TOOLS";

        // Set only for the duration of BuildTestSession.
        private static bool _forceDevTools;

        /// <summary>
        /// Whether a build gets <c>CLUCKWARS_DEV_TOOLS</c>: the Test Session menu item, <see cref="DevToolsArg"/> on the
        /// Editor command line (case-insensitive), or <see cref="DevToolsEnvVar"/>=1. Anything else is a production build.
        /// Pure so a test pins it.
        /// </summary>
        public static bool WantsDevTools(bool menuForced, string[] commandLine, string envValue, out string reason)
        {
            if (menuForced) { reason = "Test Session menu"; return true; }
            if (commandLine != null)
                foreach (var a in commandLine)
                    if (string.Equals(a, DevToolsArg, StringComparison.OrdinalIgnoreCase)) { reason = DevToolsArg; return true; }
            if (envValue != null && envValue.Trim() == "1") { reason = DevToolsEnvVar + "=1"; return true; }
            reason = "production";
            return false;
        }

        // ---- Play Store bundle (internal testing) -------------------------------

        private const string PlayStoreMenuPath = "Cluck Wars/Build/Android Play Store bundle (.aab)";

        /// <summary>Upload-keystore path. The keystore and its passwords never live in the repo or in ProjectSettings.</summary>
        public const string KeystorePathEnvVar = "CW_KEYSTORE_PATH";
        /// <summary>Upload-keystore password.</summary>
        public const string KeystorePassEnvVar = "CW_KEYSTORE_PASS";
        /// <summary>Key alias inside the keystore; defaults to <see cref="DefaultKeyAlias"/>.</summary>
        public const string KeyAliasEnvVar = "CW_KEY_ALIAS";
        /// <summary>Key password; defaults to the keystore password (keytool's PKCS12 default makes them equal).</summary>
        public const string KeyPassEnvVar = "CW_KEY_PASS";
        public const string DefaultKeyAlias = "upload";

        /// <summary>Google Play's ceiling for versionCode.</summary>
        public const int MaxPlayVersionCode = 2100000000;

        /// <summary>Upload-key settings resolved from the environment.</summary>
        public readonly struct PlaySigning
        {
            public readonly string KeystorePath, KeystorePass, KeyAlias, KeyPass;
            public PlaySigning(string path, string pass, string alias, string keyPass)
            { KeystorePath = path; KeystorePass = pass; KeyAlias = alias; KeyPass = keyPass; }
        }

        /// <summary>
        /// Builds a signed Android App Bundle for Google Play internal testing: <c>Builds/Android/CluckWars-&lt;version&gt;-&lt;code&gt;.aab</c>.
        /// The upload key comes from <see cref="KeystorePathEnvVar"/> and friends; the versionCode is the build number
        /// (<see cref="PlayVersionCode"/>), so each upload is accepted as an update. ProjectSettings is left as it was.
        /// In -batchmode a failure throws, so the Editor exits non-zero.
        /// </summary>
        [MenuItem(PlayStoreMenuPath, priority = 7)]
        public static void BuildPlayStore() => WithBuildNumber(BuildPlayStoreCore);

        private static bool BuildPlayStoreCore()
        {
            bool ok = false;
            try
            {
                ok = ValidateScenes() && DoPlayStore();
            }
            finally
            {
                RestoreWindowsTarget();
            }

            var summary = $"{LogTag} Play Store bundle — {Verdict(ok)}";
            if (ok) Debug.Log(summary);
            else
            {
                Debug.LogError(summary + "  See the errors above.");
                if (Application.isBatchMode) throw new BuildFailedException(summary);
            }
            return ok;
        }

        // ---- Build number (Maestro, 2026-11-14) ---------------------------------

        /// <summary>
        /// The build number: two-digit UTC year, then the minute of that year zero-padded to six digits, so always 8
        /// digits (2026-11-14 13:05 UTC → <c>26457265</c>). One number per build run, the same in the git tag
        /// (<c>v&lt;version&gt;+&lt;number&gt;</c>), the Android versionCode and the in-game build info. A leap year's
        /// last minute is 527039, so it stays far below <see cref="MaxPlayVersionCode"/> until 2099. Pure so a test pins it.
        /// </summary>
        public static int BuildNumber(DateTime utcNow)
        {
            var u = utcNow.ToUniversalTime();
            int minuteOfYear = (u.DayOfYear - 1) * 1440 + u.Hour * 60 + u.Minute;
            return (u.Year % 100) * 1_000_000 + minuteOfYear;
        }

        /// <summary>
        /// The Android versionCode for a build: the build number, but always above <paramref name="current"/> (Play only
        /// accepts a higher code) and never above <see cref="MaxPlayVersionCode"/>. Pure so a test pins it.
        /// </summary>
        public static int PlayVersionCode(int buildNumber, int current)
        {
            long code = Math.Max(buildNumber, (long)current + 1);
            return (int)Math.Min(Math.Max(code, 1), MaxPlayVersionCode);
        }

        /// <summary>The git tag for a build: <c>v&lt;version&gt;+&lt;build number&gt;</c>. Pure so a test pins it.</summary>
        public static string TagName(string version, int buildNumber) =>
            $"v{(string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim())}+{buildNumber}";

        // Set for the duration of one menu run, so every target in it (Build All: Windows and Android) shares one number.
        private static int _runBuildNumber;

        /// <summary>This run's build number; a build started outside a menu run gets the current minute's.</summary>
        internal static int CurrentBuildNumber => _runBuildNumber != 0 ? _runBuildNumber : BuildNumber(DateTime.UtcNow);

        /// <summary>
        /// Runs one menu entry under a single build number, then prints the tag line as the run's last line when
        /// anything was built.
        /// </summary>
        private static void WithBuildNumber(Func<bool> run)
        {
            int number = BuildNumber(DateTime.UtcNow);
            _runBuildNumber = number;
            bool built;
            try
            {
                built = run();
            }
            finally
            {
                _runBuildNumber = 0;
            }
            if (built)
                Debug.Log($"{LogTag} Build number {number} - tag this commit as {TagName(PlayerSettings.bundleVersion, number)}");
        }

        /// <summary>
        /// The versionCode for the Android build about to run, from this run's build number. Warns when ProjectSettings
        /// holds a code at or above it, because then the versionCode and the build number differ.
        /// </summary>
        private static int AndroidVersionCode(int current)
        {
            int number = CurrentBuildNumber;
            int code = PlayVersionCode(number, current);
            if (code != number)
                Debug.LogWarning($"{LogTag} ProjectSettings holds Android versionCode {current}, at or above build number {number}; " +
                                 $"this build uses versionCode {code}, so it no longer matches the build number.");
            return code;
        }

        /// <summary>
        /// Reads the upload-key settings through <paramref name="env"/>. Returns false with a reason naming the
        /// missing variable. Pure so a test pins it.
        /// </summary>
        public static bool TryResolvePlaySigning(Func<string, string> env, out PlaySigning signing, out string error)
        {
            signing = default;
            string path = env(KeystorePathEnvVar)?.Trim();
            string pass = env(KeystorePassEnvVar);
            if (string.IsNullOrEmpty(path)) { error = $"{KeystorePathEnvVar} is not set (path to the upload keystore)."; return false; }
            if (string.IsNullOrEmpty(pass)) { error = $"{KeystorePassEnvVar} is not set (upload keystore password)."; return false; }
            string alias = env(KeyAliasEnvVar)?.Trim();
            string keyPass = env(KeyPassEnvVar);
            signing = new PlaySigning(path, pass,
                string.IsNullOrEmpty(alias) ? DefaultKeyAlias : alias,
                string.IsNullOrEmpty(keyPass) ? pass : keyPass);
            error = null;
            return true;
        }

        private static bool DoPlayStore()
        {
            if (!TryResolvePlaySigning(Environment.GetEnvironmentVariable, out var signing, out var error))
            {
                Debug.LogError($"{LogTag} Play Store bundle needs an upload key: {error} " +
                               "Google Play rejects bundles signed with the debug key. See docs/TESTING.md, \"Play Store internal testing\".");
                return false;
            }
            if (!File.Exists(signing.KeystorePath))
            {
                Debug.LogError($"{LogTag} Upload keystore not found at '{signing.KeystorePath}' ({KeystorePathEnvVar}).");
                return false;
            }

            ConfigureAndroidPlayerSettings();

            // Everything below is restored afterwards: the key path and versionCode must not become a ProjectSettings diff,
            // and the plain Android build stays an unsigned-by-us .apk.
            int codeWas = PlayerSettings.Android.bundleVersionCode;
            bool customWas = PlayerSettings.Android.useCustomKeystore;
            string storeWas = PlayerSettings.Android.keystoreName, aliasWas = PlayerSettings.Android.keyaliasName;
            string storePassWas = PlayerSettings.Android.keystorePass, aliasPassWas = PlayerSettings.Android.keyaliasPass;

            int code = AndroidVersionCode(codeWas);
            try
            {
                PlayerSettings.Android.bundleVersionCode = code;
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = Path.GetFullPath(signing.KeystorePath);
                PlayerSettings.Android.keystorePass = signing.KeystorePass;
                PlayerSettings.Android.keyaliasName = signing.KeyAlias;
                PlayerSettings.Android.keyaliasPass = signing.KeyPass;
                EditorUserBuildSettings.buildAppBundle = true;

                Debug.Log($"{LogTag} Play Store bundle: versionCode {code}, versionName {PlayerSettings.bundleVersion}, key alias '{signing.KeyAlias}'.");
                var file = $"CluckWars-{SanitizedVersion()}-{code}.aab";
                bool ok = Build(BuildTarget.Android, AndroidSubdir, file);
                if (ok)
                    Debug.Log($"{LogTag} Upload {Path.GetFullPath(Path.Combine(BuildsRoot, AndroidSubdir, file))} in Play Console → " +
                              "Test and release → Testing → Internal testing → Create new release. Testers get it as a Play update.");
                return ok;
            }
            finally
            {
                PlayerSettings.Android.bundleVersionCode = codeWas;
                PlayerSettings.Android.useCustomKeystore = customWas;
                PlayerSettings.Android.keystoreName = storeWas;
                PlayerSettings.Android.keystorePass = storePassWas;
                PlayerSettings.Android.keyaliasName = aliasWas;
                PlayerSettings.Android.keyaliasPass = aliasPassWas;
                EditorUserBuildSettings.buildAppBundle = false;
            }
        }

        private static bool IsIosEnabled() => EditorPrefs.GetBool(EnableIosPrefKey, false);

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
            // The versionCode is set for this build only: ProjectSettings keeps its own value, so no diff.
            int codeWas = PlayerSettings.Android.bundleVersionCode;
            int code = AndroidVersionCode(codeWas);
            try
            {
                PlayerSettings.Android.bundleVersionCode = code;
                Debug.Log($"{LogTag} Android .apk: versionCode {code}, versionName {PlayerSettings.bundleVersion}.");
                return Build(BuildTarget.Android, AndroidSubdir, $"CluckWars-{SanitizedVersion()}.apk");
            }
            finally
            {
                PlayerSettings.Android.bundleVersionCode = codeWas;
            }
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
            // CW-200: a build without a cloud project id is silently multiplayer-dead, so fail instead of building.
            // Throws BuildFailedException (non-zero exit in -batchmode); runs before the slow target switch.
            var injectedId = EnsureCloudProjectId(out var previousId);
            try
            {
                return BuildCore(target, subfolder, filename);
            }
            finally
            {
                // Don't leave an injected id in ProjectSettings.asset as an unrelated git diff.
                if (injectedId != null && string.Equals(ReadCloudProjectId(), injectedId, StringComparison.Ordinal))
                    WriteCloudProjectId(previousId);
            }
        }

        private static bool BuildCore(BuildTarget target, string subfolder, string filename)
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

            bool devTools = WantsDevTools(_forceDevTools, Environment.GetCommandLineArgs(),
                Environment.GetEnvironmentVariable(DevToolsEnvVar), out var devToolsReason);
            if (devTools) options.extraScriptingDefines = new[] { CluckWars.Diagnostics.DevTools.Define };

            // A dev-tools build also turns on Frame Timing Stats so PerfProbe can print CPU/GPU frame times
            // (FrameTimingManager reports 0 without it). Restored after the build: production keeps its setting.
            bool frameTimingWas = PlayerSettings.enableFrameTimingStats;
            if (devTools) PlayerSettings.enableFrameTimingStats = true;
            BuildInfoWriter.DevToolsForNextBuild = devTools;
            BuildInfoWriter.BuildNumberForNextBuild = CurrentBuildNumber;

            Debug.Log($"{LogTag} {target} → {Path.GetFullPath(location)}  (scenes: {options.scenes.Length}, " +
                      $"devtools={(devTools ? "on" : "off")} [{devToolsReason}]" +
                      $"{(devTools ? $", define {CluckWars.Diagnostics.DevTools.Define}, frame timing stats on" : string.Empty)})");
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                PlayerSettings.enableFrameTimingStats = frameTimingWas;
                BuildInfoWriter.DevToolsForNextBuild = false;
                BuildInfoWriter.BuildNumberForNextBuild = 0;
                // The postprocess callback is skipped when a build fails: never leave the generated asset behind.
                BuildInfoWriter.Cleanup();
            }
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

        // ---- Cloud project id (CW-200) -----------------------------------------

        private const string PlayerSettingsAssetPath = "ProjectSettings/ProjectSettings.asset";
        private const string CloudProjectIdProperty  = "cloudProjectId";

        /// <summary>
        /// Guarantees PlayerSettings carries a valid cloud project id for the build about to run: keeps a valid one, else
        /// injects the env override / <see cref="BuildGuards.UgsProjectId"/>. Application.cloudProjectId in the player is baked
        /// from this field, and only a signed-in Editor populates it on its own.
        /// </summary>
        /// <returns>The injected id (so the caller can restore the previous value), or null when nothing was injected.</returns>
        /// <exception cref="BuildFailedException">The id is still invalid after resolution.</exception>
        private static string EnsureCloudProjectId(out string previousId)
        {
            previousId = ReadCloudProjectId();
            var wanted = BuildGuards.ResolveCloudProjectId(
                previousId, Environment.GetEnvironmentVariable(BuildGuards.CloudProjectIdEnvVar), out var source);

            var invalid = BuildGuards.ValidateCloudProjectId(wanted);
            if (invalid != null) throw CloudIdFailure(invalid);
            if (string.Equals(wanted, previousId, StringComparison.Ordinal)) return null;

            Debug.Log($"{LogTag} cloudProjectId was empty/invalid in ProjectSettings; injecting {wanted} from {source} for this build.");
            WriteCloudProjectId(wanted);

            // Read it back: the guard must check what the player will actually be built with.
            var applied = ReadCloudProjectId();
            invalid = BuildGuards.ValidateCloudProjectId(applied);
            if (invalid != null || !string.Equals(applied, wanted, StringComparison.Ordinal))
                throw CloudIdFailure(invalid ?? $"injection did not stick (PlayerSettings holds '{applied}', expected '{wanted}').");
            return wanted;
        }

        private static BuildFailedException CloudIdFailure(string reason)
        {
            var message = $"{LogTag} Build aborted (CW-200): {reason}";
            Debug.LogError(message);
            return new BuildFailedException(message);
        }

        private static SerializedObject LoadPlayerSettings()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath(PlayerSettingsAssetPath).FirstOrDefault(o => o is PlayerSettings);
            if (settings == null)
                throw CloudIdFailure($"could not load PlayerSettings from {PlayerSettingsAssetPath}.");
            return new SerializedObject(settings);
        }

        private static string ReadCloudProjectId()
        {
            var prop = LoadPlayerSettings().FindProperty(CloudProjectIdProperty);
            if (prop == null)
                throw CloudIdFailure($"PlayerSettings has no '{CloudProjectIdProperty}' property in this Unity version.");
            return prop.stringValue;
        }

        private static void WriteCloudProjectId(string id)
        {
            var so = LoadPlayerSettings();
            var prop = so.FindProperty(CloudProjectIdProperty);
            if (prop == null)
                throw CloudIdFailure($"PlayerSettings has no '{CloudProjectIdProperty}' property in this Unity version.");
            prop.stringValue = id ?? string.Empty;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

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

            // CW-201: pin the identifier (also pinned in ProjectSettings; this covers a reverted/edited asset).
            if (PlayerSettings.GetApplicationIdentifier(named) != AppIdentifier)
            {
                Debug.Log($"{LogTag} Setting Android application identifier → {AppIdentifier} " +
                          $"(was '{PlayerSettings.GetApplicationIdentifier(named)}').");
                PlayerSettings.SetApplicationIdentifier(named, AppIdentifier);
            }

            // .apk for sideloading. The Play Store bundle (BuildPlayStore) flips this to AAB for its own build only.
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
