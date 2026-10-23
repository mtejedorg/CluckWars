using System.Collections.Generic;
using System.IO;
using CluckWars.Diagnostics;
using CluckWars.Input;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The dev-tools screenshot kit: <c>-cw_shots</c>, <c>-cw_shottag</c>, <c>-cw_band</c>, <c>-cw_zones</c> parsing (pure), the
    /// shot schedule and file naming, the band override's effect on the info zones, and that all of it stays behind
    /// <see cref="DevTools.Available"/> (the Editor has it on, so the gate is pinned in the source).
    /// </summary>
    public sealed class HudShotToolsTests
    {
        private static string Root() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [TearDown]
        public void ResetOverride() => HudControlZones.DevBandOverride = 0;

        [Test]
        public void Parse_ShotsBandZonesTag()
        {
            var cfg = AutoTestConfig.FromCommandLine(new[]
            {
                "CluckWars.exe", "-cw_shots", "36,15,22,15,29", "-cw_band", "290", "-cw_zones", "-cw_shottag", "band 290!",
            });
            CollectionAssert.AreEqual(new[] { 15, 22, 29, 36 }, cfg.Shots, "sorted, distinct");
            Assert.AreEqual(290, cfg.BandOverride);
            Assert.IsTrue(cfg.Zones, "bare flag");
            Assert.AreEqual("band290", cfg.ShotTag, "sanitised like a profile name");
            Assert.IsTrue(cfg.AnyKey);
            CollectionAssert.IsEmpty(cfg.Problems);
        }

        [Test]
        public void Parse_Defaults_AndBadValues()
        {
            var none = AutoTestConfig.FromCommandLine(new[] { "CluckWars.exe", "-cw_hud" });
            Assert.IsEmpty(none.Shots);
            Assert.AreEqual(0, none.BandOverride);
            Assert.IsFalse(none.Zones);
            Assert.IsNull(none.ShotTag);

            var bad = AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_shots", "5,abc,-3,99999,7", "-cw_band", "300" });
            CollectionAssert.AreEqual(new[] { 5, 7 }, bad.Shots);
            Assert.AreEqual(0, bad.BandOverride, "only 290 / 330 are accepted");
            Assert.AreEqual(4, bad.Problems.Count);

            var off = AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_zones", "0" });
            Assert.IsFalse(off.Zones);
            CollectionAssert.Contains(AutoTestConfig.Keys, AutoTestConfig.ShotsKey);
            CollectionAssert.Contains(AutoTestConfig.Keys, AutoTestConfig.BandKey);
        }

        [Test]
        public void Describe_LineIsUnchangedByTheNewKeys()
        {
            var cfg = AutoTestConfig.FromCommandLine(new[] { "CluckWars.exe", "-cw_autohost", "-cw_shots", "15", "-cw_band", "330", "-cw_zones", "-cw_shottag", "t" });
            StringAssert.DoesNotContain("shot", cfg.Describe());
            StringAssert.DoesNotContain("band", cfg.Describe());
            StringAssert.DoesNotContain("zones", cfg.Describe());
        }

        [Test]
        public void ShotSchedule_FiresInOrder_OnceEach()
        {
            var shots = new[] { 15, 22 };
            Assert.IsFalse(AutoTestRules.ShouldTakeShot(shots, 0, 14.9f));
            Assert.IsTrue(AutoTestRules.ShouldTakeShot(shots, 0, 15f));
            Assert.IsFalse(AutoTestRules.ShouldTakeShot(shots, 1, 15f));
            Assert.IsTrue(AutoTestRules.ShouldTakeShot(shots, 1, 22.4f));
            Assert.IsFalse(AutoTestRules.ShouldTakeShot(shots, 2, 99f), "all taken");
            Assert.IsFalse(AutoTestRules.ShouldTakeShot(new int[0], 0, 99f));
            Assert.IsFalse(AutoTestRules.ShouldTakeShot(null, 0, 99f));
        }

        [Test]
        public void ShotDirectory_IsTheLogFilesFolder_PlusShots()
        {
            string log = Path.Combine(Root(), "Captures", "s1", "client1.log");
            string dir = AutoTestRules.ShotDirectoryFromArgs(new[] { "x.exe", "-logFile", log, "-cw_autoplay" });
            Assert.AreEqual(Path.Combine(Root(), "Captures", "s1", "shots"), dir);
            Assert.IsNull(AutoTestRules.ShotDirectoryFromArgs(new[] { "x.exe", "-logFile", "-" }), "stdout log");
            Assert.IsNull(AutoTestRules.ShotDirectoryFromArgs(new[] { "x.exe", "-logFile", "client1.log" }), "no folder");
            Assert.IsNull(AutoTestRules.ShotDirectoryFromArgs(new[] { "x.exe" }));
            Assert.IsNull(AutoTestRules.ShotDirectoryFromArgs(null));
        }

        [Test]
        public void ShotFileName_ProfileSecondTag()
        {
            Assert.AreEqual("client1-t15-band290.png", AutoTestRules.ShotFileName("client1", 15, "band290"));
            Assert.AreEqual("default-t7.png", AutoTestRules.ShotFileName(null, 7, null));
        }

        [Test]
        public void BandOverride_ChangesTheLandscapeBandOnly_AndDefaultIsTheShippedValue()
        {
            float land = 16f / 9f, port = 1080f / 1920f;
            Assert.AreEqual(0, HudControlZones.DevBandOverride);
            Assert.AreEqual(HudControlZones.LandscapeTopBandIncludesFade ? 330f : 290f, HudControlZones.TopBandLandscapeZonePx);

            HudControlZones.DevBandOverride = 290;
            Assert.AreEqual(290f, HudControlZones.TopBandLandscapeZonePx);
            var z = new List<Rect>();
            HudControlZones.BuildInfoZones(z, HudDeviceMode.Touch, land, Vector4.zero, 0);
            Assert.AreEqual(1f - 290f / 1080f, z[0].yMin, 1e-4f);

            HudControlZones.DevBandOverride = 330;
            HudControlZones.BuildInfoZones(z, HudDeviceMode.Touch, land, Vector4.zero, 0);
            Assert.AreEqual(1f - 330f / 1080f, z[0].yMin, 1e-4f);

            var p290 = new List<Rect>(); var p330 = new List<Rect>();
            HudControlZones.DevBandOverride = 290;
            HudControlZones.BuildInfoZones(p290, HudDeviceMode.Touch, port, Vector4.zero, 0);
            HudControlZones.DevBandOverride = 330;
            HudControlZones.BuildInfoZones(p330, HudDeviceMode.Touch, port, Vector4.zero, 0);
            Assert.AreEqual(p290[0], p330[0], "portrait ignores the landscape band");
        }

        [Test]
        public void EverythingIsBehindTheDevToolsGate()
        {
            string zones = File.ReadAllText(Path.Combine(Root(), "Assets/_Game/Scripts/Visuals/HudControlZones.cs"));
            StringAssert.Contains("_devBandOverride > 0 && DevTools.Available", zones, "the override is read only in dev tools");

            string driver = File.ReadAllText(Path.Combine(Root(), "Assets/_Game/Scripts/Diagnostics/AutoTestDriver.cs"));
            int apply = driver.IndexOf("private void ApplyScreenshotTools()");
            int gate = driver.IndexOf("if (!DevTools.Available)", apply);
            int use = driver.IndexOf("DevBandOverride =", apply);
            Assert.That(apply, Is.GreaterThan(0));
            Assert.That(gate, Is.GreaterThan(apply).And.LessThan(use), "the gate returns before the override or the overlay flag is touched");
            StringAssert.Contains("_shotsDone = true;\n                return;", driver.Replace("\r\n", "\n"), "no screenshot ticks without dev tools");

            string hud = File.ReadAllText(Path.Combine(Root(), "Assets/_Game/Scripts/UI/DebugHud.cs"));
            StringAssert.Contains("DevTools.Available && DiagnosticsReadout.ZonesOverlay", hud);
        }
    }
}
