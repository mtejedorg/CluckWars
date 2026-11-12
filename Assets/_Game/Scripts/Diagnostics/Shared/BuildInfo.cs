using System;
using System.IO;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>What a build knows about itself: written by the Editor's <c>BuildInfoWriter</c> into a TextAsset.</summary>
    [Serializable]
    public sealed class BuildInfoData
    {
        public string sha;
        public bool dirty;
        public string builtUtc;
        public bool devTools;
    }

    /// <summary>
    /// Where the <c>Session</c> line's <c>sha=</c> / <c>built=</c> come from. A player reads the TextAsset the build
    /// preprocess step generated (<see cref="ResourceName"/>, deleted again after the build so the working tree stays
    /// clean); the Editor reads the commit straight from <c>.git</c> (HEAD → ref file or packed-refs), no process spawned.
    /// The parsing is pure and pinned by EditMode tests.
    /// </summary>
    public static class BuildInfo
    {
        /// <summary><c>Resources.Load</c> name of the generated asset (Assets/_Game/Resources/BuildInfo.json).</summary>
        public const string ResourceName = "BuildInfo";

        public const int ShortShaLength = 7;

        public static BuildInfoData Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var data = JsonUtility.FromJson<BuildInfoData>(json);
                return data != null && !string.IsNullOrEmpty(data.sha) ? data : null;
            }
            catch (ArgumentException)
            {
                // Malformed JSON: the caller reports sha=unknown with the reason.
                return null;
            }
        }

        public static string ShaLabel(string sha, bool dirty) =>
            (string.IsNullOrEmpty(sha) ? "unknown" : ShortSha(sha)) + (dirty ? "+dirty" : string.Empty);

        public static string ShortSha(string sha)
        {
            if (string.IsNullOrEmpty(sha)) return "unknown";
            sha = sha.Trim();
            return sha.Length > ShortShaLength ? sha.Substring(0, ShortShaLength) : sha;
        }

        /// <summary>The ref a <c>.git/HEAD</c> file points at (<c>refs/heads/x</c>), or null for a detached HEAD.</summary>
        public static string ParseHeadRef(string headContent)
        {
            if (string.IsNullOrEmpty(headContent)) return null;
            var line = headContent.Trim();
            const string prefix = "ref:";
            return line.StartsWith(prefix, StringComparison.Ordinal) ? line.Substring(prefix.Length).Trim() : null;
        }

        /// <summary>The commit <paramref name="refName"/> has in a <c>packed-refs</c> file, or null.</summary>
        public static string FindPackedRef(string packedRefs, string refName)
        {
            if (string.IsNullOrEmpty(packedRefs) || string.IsNullOrEmpty(refName)) return null;
            foreach (var raw in packedRefs.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == '^') continue;
                int space = line.IndexOf(' ');
                if (space <= 0) continue;
                if (string.Equals(line.Substring(space + 1).Trim(), refName, StringComparison.Ordinal))
                    return line.Substring(0, space);
            }
            return null;
        }

        /// <summary>
        /// Reads the checked-out commit from <paramref name="repoRoot"/>/.git without running git. Handles a worktree
        /// (<c>.git</c> is a file naming the gitdir, refs live in its <c>commondir</c>) and a detached HEAD.
        /// </summary>
        public static bool TryReadGitSha(string repoRoot, out string sha, out string problem)
        {
            sha = null;
            problem = null;
            try
            {
                var dotGit = Path.Combine(repoRoot, ".git");
                string gitDir;
                if (Directory.Exists(dotGit)) gitDir = dotGit;
                else if (File.Exists(dotGit))
                {
                    var content = File.ReadAllText(dotGit).Trim();
                    const string prefix = "gitdir:";
                    if (!content.StartsWith(prefix, StringComparison.Ordinal)) { problem = ".git file has no gitdir line"; return false; }
                    gitDir = content.Substring(prefix.Length).Trim();
                    if (!Path.IsPathRooted(gitDir)) gitDir = Path.GetFullPath(Path.Combine(repoRoot, gitDir));
                }
                else { problem = $"no .git under {repoRoot}"; return false; }

                var headPath = Path.Combine(gitDir, "HEAD");
                if (!File.Exists(headPath)) { problem = "no .git/HEAD"; return false; }
                var head = File.ReadAllText(headPath);
                var refName = ParseHeadRef(head);
                if (refName == null) { sha = head.Trim(); return sha.Length > 0; }

                // A worktree keeps HEAD in its own gitdir but the refs in the shared one.
                var commonDir = gitDir;
                var commonFile = Path.Combine(gitDir, "commondir");
                if (File.Exists(commonFile))
                {
                    var c = File.ReadAllText(commonFile).Trim();
                    commonDir = Path.IsPathRooted(c) ? c : Path.GetFullPath(Path.Combine(gitDir, c));
                }

                foreach (var dir in new[] { gitDir, commonDir })
                {
                    var refFile = Path.Combine(dir, refName.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(refFile)) { sha = File.ReadAllText(refFile).Trim(); return sha.Length > 0; }
                }
                var packed = Path.Combine(commonDir, "packed-refs");
                if (File.Exists(packed))
                {
                    sha = FindPackedRef(File.ReadAllText(packed), refName);
                    if (sha != null) return true;
                }
                problem = $"ref {refName} not found";
                return false;
            }
            catch (IOException e) { problem = e.Message; return false; }
            catch (UnauthorizedAccessException e) { problem = e.Message; return false; }
        }

        /// <summary>
        /// The build's identity: from .git in the Editor (built=editor), from the generated TextAsset in a player.
        /// False (with the reason) when neither is available; the caller prints <c>sha=unknown</c> and warns.
        /// </summary>
        public static bool TryLoad(out string shaLabel, out string built, out string problem)
        {
            problem = null;
            if (Application.isEditor)
            {
                built = "editor";
                var root = Directory.GetParent(Application.dataPath)?.FullName;
                if (root != null && TryReadGitSha(root, out var sha, out problem))
                {
                    // The Editor does not run `git status` at boot (a process spawn on the main thread); dirty is unknown here.
                    shaLabel = ShortSha(sha);
                    return true;
                }
                shaLabel = "unknown";
                return false;
            }

            var asset = Resources.Load<TextAsset>(ResourceName);
            var data = asset != null ? Parse(asset.text) : null;
            if (data == null)
            {
                shaLabel = "unknown";
                built = "unknown";
                problem = asset == null ? $"no Resources/{ResourceName} asset in this build" : $"Resources/{ResourceName} is not valid build info";
                return false;
            }
            shaLabel = ShaLabel(data.sha, data.dirty);
            built = string.IsNullOrEmpty(data.builtUtc) ? "unknown" : data.builtUtc;
            return true;
        }
    }
}
