using System.Collections.Generic;
using System.IO;
using CluckWars.Diagnostics;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>cw_timescale (balance oracle v2, Task 0): a dev flag that speeds up solo matches, never shared ones.</summary>
    public class TimeScaleFlagTests
    {
        private static float Parse(string value) =>
            AutoTestConfig.FromMap(new Dictionary<string, string> { { AutoTestConfig.TimeScaleKey, value } }).TimeScale;

        [Test]
        public void Parse_ClampsToOneToTwenty()
        {
            Assert.AreEqual(1f, Parse("0.5"), 1e-6f);
            Assert.AreEqual(10f, Parse("10"), 1e-6f);
            Assert.AreEqual(20f, Parse("99"), 1e-6f);
        }

        [Test]
        public void Parse_DefaultIsOne_AndGarbageIsReported()
        {
            Assert.AreEqual(1f, AutoTestConfig.FromMap(new Dictionary<string, string>()).TimeScale, 1e-6f);
            var cfg = AutoTestConfig.FromMap(new Dictionary<string, string> { { AutoTestConfig.TimeScaleKey, "fast" } });
            Assert.AreEqual(1f, cfg.TimeScale, 1e-6f);
            Assert.IsNotEmpty(cfg.Problems);
        }

        [Test]
        public void Flag_IsReadOnlyInsideTheSingleModeBranch()
        {
            string src = File.ReadAllText(Path.Combine(TestSources.ProjectRoot, "Assets/_Game/Scripts/App/FusionNetworkService.cs"));
            int reads = System.Text.RegularExpressions.Regex.Matches(src, @"\.TimeScale\b").Count;
            Assert.AreEqual(1, reads, "the flag must be read in exactly one place");
            int read = src.IndexOf(".TimeScale", System.StringComparison.Ordinal);
            int lineStart = src.LastIndexOf('\n', read) + 1;
            string line = src.Substring(lineStart, src.IndexOf('\n', read) - lineStart);
            StringAssert.Contains("DevTools.Available", line, "the flag is read only with dev tools");
            int single = src.IndexOf("mode == GameMode.Single ?", read, System.StringComparison.Ordinal);
            Assert.Greater(single, read, "the value must pass through the Single-mode check");
            Assert.Less(single - read, 300, "the Single-mode check must directly follow the read");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(src, @"SimulationTimeMode\.DeltaTime").Count);
        }
    }
}
