using System;
using System.IO;
using CluckWars.Diagnostics;
using CluckWars.EditorTools;
using CluckWars.Logging;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Test-session instrumentation, item 1 + the build gate (docs/TEST_SESSION_PLAN_2026-10.md §2): the wall-clock log
    /// line format and its parser, the build-info / .git readers, the Session line, and the dev-tools build decision.
    /// The merge script (tools/test-session) parses these lines, so the exact text is the contract.
    /// </summary>
    public sealed class InstrumentationSessionTests
    {
        // ---- log line format ---------------------------------------------------------------------------------

        [Test]
        public void LogLine_HasWallClockThenUptimeThenLevelAndSource()
        {
            var utc = new DateTime(2026, 10, 20, 14, 3, 7, 45, DateTimeKind.Utc);
            var line = LogLineFormat.Format(utc, 75.5, LogLevel.Info, "Perf", "hello world");
            Assert.AreEqual("[14:03:07.045][01:15.500][Info][Perf] hello world", line);
        }

        [Test]
        public void LogLine_RoundTripsThroughTheParser()
        {
            var utc = new DateTime(2026, 10, 20, 23, 59, 59, 999, DateTimeKind.Utc);
            var line = LogLineFormat.Format(utc, 6123.4567, LogLevel.Warn, "Visual", "a [bracket] = b");
            Assert.IsTrue(LogLineFormat.TryParse(line, out var p), line);
            Assert.AreEqual("23:59:59.999", p.WallClock);
            Assert.AreEqual("102:03.456", p.Uptime, "minutes are not capped at 99");
            Assert.AreEqual("Warn", p.Level);
            Assert.AreEqual("Visual", p.Source);
            Assert.AreEqual("a [bracket] = b", p.Message);
        }

        [Test]
        public void Uptime_TruncatesAndNeverPrintsSixtySeconds()
        {
            Assert.AreEqual("00:59.999", LogLineFormat.Uptime(59.9996));
            Assert.AreEqual("01:00.000", LogLineFormat.Uptime(60.0));
            Assert.AreEqual("00:00.000", LogLineFormat.Uptime(-3));
            Assert.AreEqual("00:00.000", LogLineFormat.Uptime(double.NaN));
        }

        [Test]
        public void Parser_RejectsTheOldUptimeOnlyFormat()
        {
            Assert.IsFalse(LogLineFormat.TryParse("[01:15.500][Info][Perf] hello", out _));
            Assert.IsFalse(LogLineFormat.TryParse(null, out _));
        }

        // ---- build info ---------------------------------------------------------------------------------------

        [Test]
        public void BuildInfo_ParsesTheGeneratedJson()
        {
            var data = BuildInfo.Parse("{\"sha\":\"3c522ba\",\"dirty\":true,\"builtUtc\":\"2026-10-20T10:00:00Z\",\"devTools\":true}");
            Assert.IsNotNull(data);
            Assert.AreEqual("3c522ba+dirty", BuildInfo.ShaLabel(data.sha, data.dirty));
            Assert.AreEqual("2026-10-20T10:00:00Z", data.builtUtc);
            Assert.IsTrue(data.devTools);
            Assert.IsNull(BuildInfo.Parse("not json"));
            Assert.IsNull(BuildInfo.Parse("{}"), "no sha = no build info");
            Assert.AreEqual("unknown", BuildInfo.ShaLabel(null, false));
        }

        [Test]
        public void GitHead_RefAndDetachedAndPackedRefs()
        {
            Assert.AreEqual("refs/heads/test/instrumentation-2026-10-20",
                BuildInfo.ParseHeadRef("ref: refs/heads/test/instrumentation-2026-10-20\n"));
            Assert.IsNull(BuildInfo.ParseHeadRef("3c522ba0123456789abcdef0123456789abcdef0\n"), "detached HEAD");
            const string packed = "# pack-refs with: peeled fully-peeled sorted\n" +
                                  "f0cdcbf0000000000000000000000000000000aa refs/heads/develop\n" +
                                  "^1111111111111111111111111111111111111111\n" +
                                  "3c522ba0000000000000000000000000000000bb refs/heads/main\n";
            Assert.AreEqual("3c522ba0000000000000000000000000000000bb", BuildInfo.FindPackedRef(packed, "refs/heads/main"));
            Assert.IsNull(BuildInfo.FindPackedRef(packed, "refs/heads/missing"));
            Assert.AreEqual("3c522ba", BuildInfo.ShortSha("3c522ba0000000000000000000000000000000bb"));
        }

        [Test]
        public void GitSha_ReadsLooseRefThenPackedRefs()
        {
            var root = Path.Combine(Path.GetTempPath(), "cw-git-" + Guid.NewGuid().ToString("N"));
            try
            {
                var git = Path.Combine(root, ".git");
                Directory.CreateDirectory(Path.Combine(git, "refs", "heads"));
                File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/dev\n");
                File.WriteAllText(Path.Combine(git, "packed-refs"), "abcdef1234567 refs/heads/dev\n");
                Assert.IsTrue(BuildInfo.TryReadGitSha(root, out var sha, out var problem), problem);
                Assert.AreEqual("abcdef1234567", sha, "packed-refs when there is no loose ref");

                File.WriteAllText(Path.Combine(git, "refs", "heads", "dev"), "1234567abcdef\n");
                Assert.IsTrue(BuildInfo.TryReadGitSha(root, out sha, out problem), problem);
                Assert.AreEqual("1234567abcdef", sha, "a loose ref wins over packed-refs");

                Assert.IsFalse(BuildInfo.TryReadGitSha(Path.Combine(root, "nope"), out _, out problem));
                StringAssert.Contains(".git", problem);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        // ---- Session line -------------------------------------------------------------------------------------

        [Test]
        public void SessionLine_HasEveryKeyInOrder_AndIsKeyValueSafe()
        {
            var f = new SessionFields
            {
                Sha = "3c522ba+dirty", Built = "2026-10-20T10:00:00Z", DevTools = true, Platform = "Android",
                Device = "Google Pixel 9", Gpu = "Mali-G715 MC7", Api = "Vulkan", Quality = "Mobile", PerfMode = true,
                Width = 2424, Height = 1080, RefreshHz = 60.0, Os = "Android OS 15 / API-35 (AP4A=1)", Unity = "6000.3.14f1",
                Date = "2026-10-20", Battery = SessionLine.Battery(0.8f),
            };
            Assert.AreEqual(
                "Session sha=3c522ba+dirty built=2026-10-20T10:00:00Z devtools=1 platform=Android device=Google_Pixel_9 " +
                "gpu=Mali-G715_MC7 api=Vulkan quality=Mobile perfMode=1 res=2424x1080 hz=60 os=Android_OS_15_/_API-35_(AP4A1) " +
                "unity=6000.3.14f1 date=2026-10-20 battery=80%",
                SessionLine.Format(SessionLine.BootPrefix, f));
            StringAssert.StartsWith("SessionChange sha=", SessionLine.Format(SessionLine.ChangePrefix, f));
        }

        [Test]
        public void SessionLine_MissingValuesPrintNa()
        {
            Assert.AreEqual("n/a", SessionLine.Sanitize(null));
            Assert.AreEqual("n/a", SessionLine.Sanitize("   "));
            Assert.AreEqual("n/a", SessionLine.Battery(-1f));
            StringAssert.Contains(" hz=n/a ", SessionLine.Format("Session", default));
        }

        // ---- the build gate -------------------------------------------------------------------------------------

        [Test]
        public void DevToolsDefine_OnlyForTheMenuItemTheSwitchOrTheEnvVar()
        {
            Assert.IsTrue(CluckWarsBuildMenu.WantsDevTools(true, null, null, out var why));
            StringAssert.Contains("Test Session", why);
            Assert.IsTrue(CluckWarsBuildMenu.WantsDevTools(false, new[] { "Unity.exe", "-batchmode", "-CWDEVTOOLS" }, null, out why));
            Assert.AreEqual("-cwDevTools", why);
            Assert.IsTrue(CluckWarsBuildMenu.WantsDevTools(false, null, " 1 ", out why));
            Assert.IsFalse(CluckWarsBuildMenu.WantsDevTools(false, new[] { "Unity.exe", "-cwDevToolsX" }, "0", out why));
            Assert.AreEqual("production", why);
            Assert.IsFalse(CluckWarsBuildMenu.WantsDevTools(false, null, null, out _));
        }

        [Test]
        public void DevTools_AreAvailableInTheEditor()
        {
            // Debug.isDebugBuild is true in the Editor: Developer Mode, the Ability Lab and the probes all exist there.
            Assert.IsTrue(DevTools.Available);
            Assert.AreEqual("CLUCKWARS_DEV_TOOLS", DevTools.Define);
        }
    }
}
