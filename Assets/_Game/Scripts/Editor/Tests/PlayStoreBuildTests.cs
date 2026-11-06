using System;
using System.Collections.Generic;
using System.IO;
using CluckWars.EditorTools;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Play Store bundle build (Cluck Wars / Build / Android Play Store bundle): the versionCode rule that lets every
    /// upload replace the last one on testers' phones, and the upload-key lookup that must never fall back to the debug key.
    /// </summary>
    public sealed class PlayStoreBuildTests
    {
        [Test]
        public void VersionCode_IsMinutesSinceEpoch()
        {
            var t = CluckWarsBuildMenu.VersionCodeEpochUtc.AddDays(1).AddMinutes(5);
            Assert.AreEqual(24 * 60 + 5, CluckWarsBuildMenu.PlayVersionCode(t, 1));
        }

        [Test]
        public void VersionCode_LaterBuildIsHigher()
        {
            var t = new DateTime(2026, 11, 6, 14, 0, 0, DateTimeKind.Utc);
            int first = CluckWarsBuildMenu.PlayVersionCode(t, 1);
            Assert.Greater(CluckWarsBuildMenu.PlayVersionCode(t.AddMinutes(1), 1), first);
        }

        [Test]
        public void VersionCode_AlwaysAboveCurrent_EvenIfClockIsBehind()
        {
            var t = new DateTime(2026, 11, 6, 14, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(5_000_001, CluckWarsBuildMenu.PlayVersionCode(t, 5_000_000));
            Assert.AreEqual(1, CluckWarsBuildMenu.PlayVersionCode(CluckWarsBuildMenu.VersionCodeEpochUtc.AddYears(-1), 0));
        }

        [Test]
        public void VersionCode_NeverExceedsPlayCeiling()
        {
            Assert.AreEqual(CluckWarsBuildMenu.MaxPlayVersionCode,
                CluckWarsBuildMenu.PlayVersionCode(DateTime.UtcNow, int.MaxValue));
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
