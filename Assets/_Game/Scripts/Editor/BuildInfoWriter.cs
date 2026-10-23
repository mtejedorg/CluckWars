using System;
using System.Diagnostics;
using System.IO;
using CluckWars.Diagnostics;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Stamps every player build with its identity for the runtime <c>Session</c> line: writes
    /// <see cref="AssetPath"/> (short SHA, dirty flag, UTC build time, dev-tools flag) before the build and deletes it
    /// after, so the working tree stays clean. <c>CluckWarsBuildMenu</c> also calls <see cref="Cleanup"/> after every
    /// build, because Unity skips the postprocess callback when a build fails.
    /// </summary>
    /// <remarks>The path is listed for <c>.gitignore</c> (with its .meta) in case an Editor crash interrupts a build.</remarks>
    public sealed class BuildInfoWriter : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string AssetPath = "Assets/_Game/Resources/" + BuildInfo.ResourceName + ".json";
        private const string LogTag = "[Build]";

        /// <summary>Set by <c>CluckWarsBuildMenu</c> around a build compiled with <c>CLUCKWARS_DEV_TOOLS</c>.</summary>
        public static bool DevToolsForNextBuild;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            bool devTools = DevToolsForNextBuild || (report.summary.options & BuildOptions.Development) != 0;
            var status = RunGit("status --porcelain", out var statusError);
            if (status == null)
                Debug.LogWarning($"{LogTag} BuildInfo: git status failed ({statusError}); the dirty flag is unknown and recorded as clean.");
            var data = new BuildInfoData
            {
                sha = RunGit("rev-parse --short HEAD", out var shaError)?.Trim(),
                dirty = !string.IsNullOrWhiteSpace(status),
                builtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                devTools = devTools,
            };
            if (string.IsNullOrEmpty(data.sha))
            {
                data.sha = "unknown";
                Debug.LogWarning($"{LogTag} BuildInfo: git rev-parse failed ({shaError}); this build's Session line will say sha=unknown.");
            }

            File.WriteAllText(AssetPath, JsonUtility.ToJson(data));
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"{LogTag} BuildInfo: sha={BuildInfo.ShaLabel(data.sha, data.dirty)} built={data.builtUtc} devtools={(devTools ? 1 : 0)} → {AssetPath}");
        }

        public void OnPostprocessBuild(BuildReport report) => Cleanup();

        /// <summary>Deletes the generated asset (and its .meta) if it exists. Safe to call any time.</summary>
        public static void Cleanup()
        {
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath) != null)
            {
                AssetDatabase.DeleteAsset(AssetPath);
                return;
            }
            if (File.Exists(AssetPath)) File.Delete(AssetPath);
            if (File.Exists(AssetPath + ".meta")) File.Delete(AssetPath + ".meta");
        }

        /// <summary>Runs git in the project root; null (with the reason) when git is missing or fails.</summary>
        private static string RunGit(string args, out string error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = Directory.GetParent(Application.dataPath)?.FullName ?? ".",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) { error = "git did not start"; return null; }
                var output = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(10000)) { error = "git timed out"; return null; }
                if (p.ExitCode != 0) { error = $"git {args} exited {p.ExitCode}: {stderr.Trim()}"; return null; }
                return output;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException)
            {
                error = e.Message;
                return null;
            }
        }
    }
}
