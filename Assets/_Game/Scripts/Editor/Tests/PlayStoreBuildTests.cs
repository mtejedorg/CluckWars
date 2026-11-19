using System;
using System.Collections.Generic;
using System.IO;
using CluckWars.EditorTools;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Play Store bundle build (Cluck Wars / Build / Android Play Store bundle): the build number and the versionCode rule that lets every
    /// upload replace the last one on testers' phones, and the upload-key lookup that must never fall back to the debug key.
    /// </summary>
    public sealed class PlayStoreBuildTests
    {
        private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) =>
            new DateTime(y, mo, d, h, mi, s, DateTimeKind.Utc);

        [Test]
        public void BuildNumber_YearStartIsYearTimesAMillion()
        {
            Assert.AreEqual(26000000, CluckWarsBuildMenu.BuildNumber(Utc(2026, 1, 1)));
        }

        [Test]
        public void BuildNumber_IsYearThenMinuteOfYear()
        {
            // Day 318 of 2026: 317 whole days * 1440 + 13 * 60 + 5 = 457265; the seconds are dropped.
            Assert.AreEqual(26457265, CluckWarsBuildMenu.BuildNumber(Utc(2026, 11, 14, 13, 5, 54)));
        }

        [Test]
        public void BuildNumber_LastMinuteOfALeapYear()
        {
            Assert.AreEqual(28527039, CluckWarsBuildMenu.BuildNumber(Utc(2028, 12, 31, 23, 59)));
        }

        [Test]
        public void BuildNumber_IsAlwaysEightDigits()
        {
            foreach (var t in new[] { Utc(2026, 1, 1), Utc(2026, 6, 30, 12, 0), Utc(2027, 12, 31, 23, 59), Utc(2099, 12, 31, 23, 59) })
                Assert.AreEqual(8, CluckWarsBuildMenu.BuildNumber(t).ToString(System.Globalization.CultureInfo.InvariantCulture).Length, t.ToString("o"));
        }

        [Test]
        public void BuildNumber_LaterMinuteIsHigher_AcrossTheYearEnd()
        {
            var t = Utc(2026, 11, 6, 14, 0);
            Assert.Greater(CluckWarsBuildMenu.BuildNumber(t.AddMinutes(1)), CluckWarsBuildMenu.BuildNumber(t));
            var lastMinute = Utc(2026, 12, 31, 23, 59);
            Assert.Greater(CluckWarsBuildMenu.BuildNumber(lastMinute.AddMinutes(1)), CluckWarsBuildMenu.BuildNumber(lastMinute));
        }

        [Test]
        public void BuildNumberForCommit_IsTheCommitMinute_NotTheBuildMinute()
        {
            // 2026-11-14 13:05:54 UTC = 1794661554; the fallback (a different day) must be ignored.
            long unix = new DateTimeOffset(Utc(2026, 11, 14, 13, 5, 54)).ToUnixTimeSeconds();
            Assert.AreEqual(26457265, CluckWarsBuildMenu.BuildNumberForCommit(unix + "\n", Utc(2030, 1, 1)));
        }

        [Test]
        public void BuildNumberForCommit_FallsBackWhenGitGaveNothing()
        {
            Assert.AreEqual(CluckWarsBuildMenu.BuildNumber(Utc(2026, 11, 19, 10, 0)),
                CluckWarsBuildMenu.BuildNumberForCommit(null, Utc(2026, 11, 19, 10, 0)));
            Assert.AreEqual(CluckWarsBuildMenu.BuildNumber(Utc(2026, 11, 19, 10, 0)),
                CluckWarsBuildMenu.BuildNumberForCommit("not a number", Utc(2026, 11, 19, 10, 0)));
        }

        [Test]
        public void TryParseBuildNumber_AcceptsExactlyEightDigits()
        {
            Assert.IsTrue(CluckWarsBuildMenu.TryParseBuildNumber("26457265\n", out int n));
            Assert.AreEqual(26457265, n);
            foreach (var bad in new[] { null, "", "2645726", "264572650", "2645726x", "-6457265" })
                Assert.IsFalse(CluckWarsBuildMenu.TryParseBuildNumber(bad, out _), bad ?? "null");
        }

        [Test]
        public void BuildNumber_OutranksEveryOldMinutesSinceEpochCode()
        {
            // The old rule stamped minutes since 2026-01-01 (at most 525600 in 2026), so Play still sees an update.
            Assert.Greater(CluckWarsBuildMenu.BuildNumber(Utc(2026, 11, 14)), 525600);
        }

        [Test]
        public void VersionCode_IsTheBuildNumber()
        {
            Assert.AreEqual(26457265, CluckWarsBuildMenu.PlayVersionCode(26457265, 1));
        }

        [Test]
        public void VersionCode_AlwaysAboveCurrent_EvenIfClockIsBehind()
        {
            int behind = CluckWarsBuildMenu.BuildNumber(Utc(2026, 11, 6, 14, 0));
            Assert.AreEqual(30_000_001, CluckWarsBuildMenu.PlayVersionCode(behind, 30_000_000));
            Assert.AreEqual(1, CluckWarsBuildMenu.PlayVersionCode(0, 0));
        }

        [Test]
        public void VersionCode_NeverExceedsPlayCeiling()
        {
            Assert.AreEqual(CluckWarsBuildMenu.MaxPlayVersionCode, CluckWarsBuildMenu.PlayVersionCode(26457265, int.MaxValue));
            Assert.AreEqual(CluckWarsBuildMenu.MaxPlayVersionCode, CluckWarsBuildMenu.PlayVersionCode(int.MaxValue, 1));
        }

        [Test]
        public void TagName_IsVersionPlusBuildNumber()
        {
            Assert.AreEqual("v0.5.0-alpha+26457265", CluckWarsBuildMenu.TagName("0.5.0-alpha", 26457265));
        }

        [Test]
        public void Signing_MissingPathOrPassword_FailsAndNamesTheVariable()
        {
            Assert.IsFalse(CluckWarsBuildMenu.TryResolvePlaySigning(Env(), out _, out var error));
            StringAssert.Contains(CluckWarsBuildMenu.KeystorePathEnvVar, error);

            Assert.IsFalse(CluckWarsBuildMenu.TryResolvePlaySigning(
                Env((CluckWarsBuildMenu.KeystorePathEnvVar, "C:/keys/upload.jks")), out _, out error));
            StringAssert.Contains(CluckWarsBuildMenu.KeystorePassEnvVar, error);
        }

        [Test]
        public void Signing_DefaultsAliasAndKeyPassword()
        {
            Assert.IsTrue(CluckWarsBuildMenu.TryResolvePlaySigning(Env(
                (CluckWarsBuildMenu.KeystorePathEnvVar, " C:/keys/upload.jks "),
                (CluckWarsBuildMenu.KeystorePassEnvVar, "pw")), out var s, out var error));
            Assert.IsNull(error);
            Assert.AreEqual("C:/keys/upload.jks", s.KeystorePath);
            Assert.AreEqual(CluckWarsBuildMenu.DefaultKeyAlias, s.KeyAlias);
            Assert.AreEqual("pw", s.KeyPass);
        }

        [Test]
        public void Signing_ExplicitAliasAndKeyPasswordWin()
        {
            Assert.IsTrue(CluckWarsBuildMenu.TryResolvePlaySigning(Env(
                (CluckWarsBuildMenu.KeystorePathEnvVar, "k.jks"),
                (CluckWarsBuildMenu.KeystorePassEnvVar, "pw"),
                (CluckWarsBuildMenu.KeyAliasEnvVar, "cw"),
                (CluckWarsBuildMenu.KeyPassEnvVar, "kp")), out var s, out _));
            Assert.AreEqual("cw", s.KeyAlias);
            Assert.AreEqual("kp", s.KeyPass);
        }

        [Test]
        public void Keystores_AreGitIgnored()
        {
            var gitignore = File.ReadAllText(".gitignore");
            StringAssert.Contains("*.jks", gitignore);
            StringAssert.Contains("*.keystore", gitignore);
        }

        private static Func<string, string> Env(params (string key, string value)[] vars)
        {
            var map = new Dictionary<string, string>();
            foreach (var (k, v) in vars) map[k] = v;
            return k => map.TryGetValue(k, out var v) ? v : null;
        }
    }
}
