using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Source guards over the runtime scripts (architecture audit 2026-11-12, batch 3): the rules in
    /// docs/CONVENTIONS.md that a compiler cannot enforce. "Runtime" is every .cs under Assets/_Game/Scripts that is
    /// not in an Editor folder. Comments and string literals are not code and are stripped before matching.
    /// </summary>
    public sealed class RuntimeSourceGuardTests
    {
        private const string ScriptsRoot = "Assets/_Game/Scripts";

        internal static IEnumerable<string> RuntimeFiles() =>
            Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !p.Contains("/Editor/"));

        internal static string Code(string path)
        {
            string src = File.ReadAllText(path);
            src = Regex.Replace(src, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            src = Regex.Replace(src, @"//[^\n]*", string.Empty);
            return Regex.Replace(src, "\"(\\\\.|[^\"\\\\])*\"", "\"\"");
        }

        private static List<string> FilesMatching(Regex pattern, System.Func<string, bool> exempt)
        {
            var hits = new List<string>();
            foreach (var path in RuntimeFiles())
            {
                if (exempt(path)) continue;
                if (pattern.IsMatch(Code(path))) hits.Add(path);
            }
            return hits;
        }

        [Test]
        public void TheScan_SeesTheRuntimeScripts()
        {
            // A wrong root or working directory would make every guard below pass on nothing.
            var files = RuntimeFiles().ToList();
            Assert.Greater(files.Count, 150, "the runtime scan found too few files to mean anything");
            Assert.IsTrue(files.Any(p => p.EndsWith("/Logging/UnityLogService.cs")));
        }

        // ---- DI-6: all log output goes through ILogService --------------------------------------------------------

        private static readonly Regex UnityDebugLog = new Regex(@"\bDebug\s*\.\s*(Log\w*|Assert\w*)\s*\(");

        [Test]
        public void NoRuntimeFile_CallsDebugLog_OutsideTheLoggingService()
        {
            var hits = FilesMatching(UnityDebugLog, p => p.Contains("/Logging/"));
            Assert.IsEmpty(hits,
                "UnityEngine.Debug.Log* outside Scripts/Logging. Use ILogService with a Source tag (docs/CONVENTIONS.md):\n" +
                string.Join("\n", hits));
        }

        [Test]
        public void TheDebugLogGuard_MatchesWhatItIsMeantToCatch()
        {
            Assert.IsTrue(UnityDebugLog.IsMatch("Debug.LogWarning(x);"));
            Assert.IsTrue(UnityDebugLog.IsMatch("UnityEngine.Debug.Log(x);"));
            Assert.IsFalse(UnityDebugLog.IsMatch("_log.Debug(Source, x);"), "ILogService.Debug is the service, not Unity's");
            Assert.IsTrue(UnityDebugLog.IsMatch(Code("Assets/_Game/Scripts/Logging/UnityLogService.cs")),
                "the logging service itself is the one place that calls Unity's logger");
        }
    }
}
