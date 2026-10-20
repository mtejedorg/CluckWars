using System.Collections.Generic;
using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using CluckWars.Input;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Test-session instrumentation items 6-7: the auto-test key parsing (Android extras map, command line, Editor spec),
    /// the start / play-again / join-retry decisions, the five-tap corner gesture, and the auto-play brain and stick maths.
    /// </summary>
    public sealed class InstrumentationAutoTestTests
    {
        // ---- key parsing ------------------------------------------------------------------------------------------

        [Test]
        public void CommandLine_PairsAndBareFlags()
        {
            var cfg = AutoTestConfig.FromCommandLine(new[]
            {
                "CluckWars.exe", "-screen-width", "1280", "-cw_autohost", "-cw_class", "speedy", "-CW_AUTOSTART", "2",
                "-cw_autoloop", "3", "-cw_autoplay", "-cw_devmode", "-cw_hud", "1",
            });
            Assert.IsTrue(cfg.AnyKey);
            Assert.IsTrue(cfg.AutoHost, "a bare flag is 1");
            Assert.AreEqual(ChickenClass.Speedy, cfg.Class, "class is case-insensitive");
            Assert.AreEqual(2, cfg.AutoStart, "keys are case-insensitive");
            Assert.AreEqual(3, cfg.AutoLoop);
            Assert.IsTrue(cfg.AutoPlay);
            Assert.IsTrue(cfg.DevMode, "a bare flag followed by another switch");
            Assert.IsTrue(cfg.Hud);
            Assert.IsNull(cfg.AutoJoin);
            Assert.AreEqual(AutoTestConfig.DefaultAutoStartTimeout, cfg.AutoStartTimeout);
            CollectionAssert.IsEmpty(cfg.Problems);
            Assert.AreEqual("AutoTest config autohost=1 autojoin=- class=Speedy autostart=2 autoloop=3 autoplay=1 timeout=90 devmode=1 hud=1",
                cfg.Describe());
        }

        [Test]
        public void AndroidExtras_JoinCodeIsUpperCased_BadValuesWarnAndFallBack()
        {
            var cfg = AutoTestConfig.FromMap(new Dictionary<string, string>
            {
                ["cw_autojoin"] = " abc123 ",
                ["cw_class"] = "Ninja",
                ["cw_autostart_timeout"] = "45",
                ["cw_autoplay"] = "true",
                ["cw_hud"] = "maybe",
                ["cw_autojion"] = "x",
                ["other_extra"] = "ignored",
            });
            Assert.AreEqual("ABC123", cfg.AutoJoin);
            Assert.IsTrue(cfg.ClassGiven);
            Assert.AreEqual(AutoTestConfig.DefaultClass, cfg.Class, "invalid class -> default");
            Assert.AreEqual(45f, cfg.AutoStartTimeout);
            Assert.IsTrue(cfg.AutoPlay);
            Assert.IsFalse(cfg.Hud);
            Assert.AreEqual(3, cfg.Problems.Count, string.Join(" | ", cfg.Problems));
            Assert.IsTrue(cfg.WantsMenuFlow);
        }

        [Test]
        public void JoinCode_MustBeWhole_ElseAutoJoinIsOff()
        {
            var shortCode = AutoTestConfig.FromMap(new Dictionary<string, string> { ["cw_autojoin"] = "AB12" });
            Assert.IsNull(shortCode.AutoJoin, "a partial code would sit on THE COOP forever");
            Assert.AreEqual(1, shortCode.Problems.Count);
            Assert.IsFalse(shortCode.WantsMenuFlow);
            var bare = AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_autojoin" });
            Assert.IsNull(bare.AutoJoin, "a bare flag has no code");
            Assert.IsFalse(AutoTestConfig.IsJoinCode(new string('A', 17)), "longer than the join field");
            Assert.IsTrue(AutoTestConfig.IsJoinCode("abc123"));
            Assert.AreEqual(60f, AutoTestRules.JoinAttemptTimeoutSeconds);
        }

        [Test]
        public void HostAndJoinTogether_Hosts_AndNumbersAreRefusedAsClasses()
        {
            var cfg = AutoTestConfig.FromMap(new Dictionary<string, string> { ["cw_autohost"] = "1", ["cw_autojoin"] = "ABC123" });
            Assert.IsTrue(cfg.AutoHost);
            Assert.IsNull(cfg.AutoJoin);
            Assert.AreEqual(1, cfg.Problems.Count);
            Assert.IsFalse(AutoTestConfig.TryParseClass("2", out _));
            Assert.IsTrue(AutoTestConfig.TryParseClass("ASSASSIN", out var c));
            Assert.AreEqual(ChickenClass.Assassin, c);
        }

        [Test]
        public void NoKeys_NothingToDo()
        {
            var cfg = AutoTestConfig.FromCommandLine(new[] { "CluckWars.exe", "-batchmode", "-logFile", "x.log" });
            Assert.IsFalse(cfg.AnyKey);
            Assert.IsFalse(cfg.WantsMenuFlow);
        }

        [Test]
        public void EditorSpec_KeyValueTokens_PrefixOptional()
        {
            var map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            AutoTestConfig.AddSpec("autohost=1 class=Warrior  cw_autostart=2 autoplay", map);
            var cfg = AutoTestConfig.FromMap(map);
            Assert.IsTrue(cfg.AutoHost);
            Assert.AreEqual(ChickenClass.Warrior, cfg.Class);
            Assert.AreEqual(2, cfg.AutoStart);
            Assert.IsTrue(cfg.AutoPlay, "a bare key is 1");
            CollectionAssert.IsEmpty(cfg.Problems);
        }

        // ---- decisions --------------------------------------------------------------------------------------------

        [Test]
        public void AutoStart_OneIsImmediate_ElseWaitForHumansOrTimeout()
        {
            Assert.IsFalse(AutoTestRules.ShouldStart(0, 4, 999f, 90f, out _), "0 = auto-start off");
            Assert.IsTrue(AutoTestRules.ShouldStart(1, 1, 0f, 90f, out var why));
            StringAssert.Contains("autostart=1", why);
            Assert.IsFalse(AutoTestRules.ShouldStart(3, 2, 30f, 90f, out _));
            Assert.IsTrue(AutoTestRules.ShouldStart(3, 3, 1f, 90f, out why));
            StringAssert.Contains("3/3", why);
            Assert.IsTrue(AutoTestRules.ShouldStart(3, 2, 90f, 90f, out why));
            StringAssert.Contains("timeout", why);
        }

        [Test]
        public void AutoLoop_AndJoinRetry()
        {
            Assert.IsFalse(AutoTestRules.ShouldPlayAgain(0, 0));
            Assert.IsTrue(AutoTestRules.ShouldPlayAgain(0, 2));
            Assert.IsTrue(AutoTestRules.ShouldPlayAgain(1, 2));
            Assert.IsFalse(AutoTestRules.ShouldPlayAgain(2, 2), "N extra rounds, then stay on the results");
            Assert.IsTrue(AutoTestRules.ShouldRetryJoin(0f));
            Assert.IsTrue(AutoTestRules.ShouldRetryJoin(119.9f));
            Assert.IsFalse(AutoTestRules.ShouldRetryJoin(120f), "2 min");
        }

        // ---- five-tap corner ----------------------------------------------------------------------------------------

        [Test]
        public void CornerTaps_FiveInTheTopLeftWithinTwoSeconds()
        {
            const float w = 2424f, h = 1080f;
            Assert.AreEqual(120f, CornerTapDetector.CornerSize(w, h), "8% of 1080 = 86 px < the 120 px floor");
            Assert.AreEqual(160f, CornerTapDetector.CornerSize(4000f, 2000f));
            var corner = new Vector2(30f, h - 30f);   // Input System: origin bottom-left
            Assert.IsTrue(CornerTapDetector.InTopLeftCorner(corner, w, h));
            Assert.IsFalse(CornerTapDetector.InTopLeftCorner(new Vector2(30f, 30f), w, h), "bottom-left is not it");

            var d = new CornerTapDetector();
            for (int i = 0; i < 4; i++) Assert.IsFalse(d.Register(corner, w, h, i * 0.3f));
            Assert.IsTrue(d.Register(corner, w, h, 1.2f), "fifth tap within 2 s");
            Assert.IsFalse(d.Register(corner, w, h, 1.3f), "the count restarts after a toggle");
        }

        [Test]
        public void CornerTaps_SlowOrStrayTapsDoNotCount()
        {
            const float w = 1920f, h = 1080f;
            var corner = new Vector2(10f, h - 10f);
            var d = new CornerTapDetector();
            for (int i = 0; i < 4; i++) d.Register(corner, w, h, i * 0.6f);   // 0, .6, 1.2, 1.8
            Assert.IsFalse(d.Register(corner, w, h, 2.5f), "the first tap fell out of the 2 s window");
            Assert.IsTrue(d.Register(corner, w, h, 2.6f));

            d.Reset();
            for (int i = 0; i < 4; i++) d.Register(corner, w, h, i * 0.1f);
            Assert.IsFalse(d.Register(new Vector2(900f, 500f), w, h, 0.5f), "a tap elsewhere resets the count");
            Assert.IsFalse(d.Register(corner, w, h, 0.6f));
        }

        // ---- auto-play --------------------------------------------------------------------------------------------

        private static AutoPlayPile[] Piles(params (float x, float z, float amount)[] p)
        {
            var a = new AutoPlayPile[p.Length];
            for (int i = 0; i < p.Length; i++) a[i] = new AutoPlayPile { Position = new Vector2(p[i].x, p[i].z), Amount = p[i].amount };
            return a;
        }

        [Test]
        public void AutoPlay_ToNearestFoodWhenEmpty_HomeWhenFull()
        {
            var brain = new AutoPlayBrain();
            var piles = Piles((10f, 0f, 10f), (-30f, 0f, 10f), (2f, 0f, 0f));
            var dir = brain.Tick(0f, Vector2.zero, 0f, 10f, true, new Vector2(0f, -20f), 3f, piles, piles.Length, 0.1f);
            Assert.AreEqual(AutoPlayGoal.ToPile, brain.Goal);
            Assert.AreEqual(0, brain.TargetPile, "the empty pile is skipped, the near one wins");
            Assert.Greater(dir.x, 0.9f);

            dir = brain.Tick(0.1f, new Vector2(9.5f, 0f), 2f, 10f, true, new Vector2(0f, -20f), 3f, piles, piles.Length, 0.1f);
            Assert.AreEqual(AutoPlayGoal.AtPile, brain.Goal);
            Assert.AreEqual(Vector2.zero, dir, "stands still to peck");

            dir = brain.Tick(0.2f, new Vector2(9.5f, 0f), 10f, 10f, true, new Vector2(0f, -20f), 3f, piles, piles.Length, 0.1f);
            Assert.AreEqual(AutoPlayGoal.ToBase, brain.Goal);
            Assert.Less(dir.y, -0.4f, "full: heads home");

            brain.Tick(0.3f, new Vector2(0f, -19f), 10f, 10f, true, new Vector2(0f, -20f), 3f, piles, piles.Length, 0.1f);
            Assert.AreEqual(AutoPlayGoal.AtBase, brain.Goal, "inside the deposit zone: stands still");
        }

        [Test]
        public void AutoPlay_NoProgressFor2s_RetargetsAndStrafes()
        {
            var brain = new AutoPlayBrain();
            var piles = Piles((10f, 0f, 10f), (0f, 14f, 10f));
            var self = Vector2.zero;
            brain.Tick(0f, self, 0f, 10f, false, default, 3f, piles, piles.Length, 0.2f);
            Assert.AreEqual(0, brain.TargetPile);
            brain.Tick(1f, self, 0f, 10f, false, default, 3f, piles, piles.Length, 0.2f);
            var dir = brain.Tick(2.1f, self, 0f, 10f, false, default, 3f, piles, piles.Length, 0.2f);   // stuck
            Assert.AreEqual(1f, dir.magnitude, 1e-3f);
            Assert.Less(Mathf.Abs(Vector2.Dot(dir, Vector2.right)), 0.01f, "strafes sideways out of the jam");
            brain.Tick(3.5f, self, 0f, 10f, false, default, 3f, piles, piles.Length, 0.2f);
            Assert.AreEqual(1, brain.TargetPile, "the stuck pile is skipped for a while");
        }

        [Test]
        public void AutoPlay_NoFoodAnywhere_BringsCargoHome_OrIdles()
        {
            var brain = new AutoPlayBrain();
            var dry = Piles((5f, 5f, 0f));
            brain.Tick(0f, Vector2.zero, 3f, 10f, true, new Vector2(-10f, 0f), 3f, dry, dry.Length, 0.5f);
            Assert.AreEqual(AutoPlayGoal.ToBase, brain.Goal);
            var dir = brain.Tick(0.1f, Vector2.zero, 0f, 10f, true, new Vector2(-10f, 0f), 3f, dry, dry.Length, 0.5f);
            Assert.AreEqual(AutoPlayGoal.Idle, brain.Goal);
            Assert.AreEqual(Vector2.zero, dir);
        }

        [Test]
        public void AutoPlay_StickIsTheInverseOfStickToWorld()
        {
            // The provider hands OnInput StickToWorld(world, -yaw); OnInput applies StickToWorld(stick, yaw).
            var world = new Vector2(0.6f, -0.8f);
            foreach (var yaw in new[] { 0f, 45f, 135f, -90f })
            {
                var back = AimRules.StickToWorld(AimRules.StickToWorld(world, -yaw), yaw);
                Assert.AreEqual(world.x, back.x, 1e-4f, $"yaw {yaw}");
                Assert.AreEqual(world.y, back.y, 1e-4f, $"yaw {yaw}");
            }
        }

        [Test]
        public void AutoPlayPress_LatchesUntilRead_OnceOnly_AndExpires()
        {
            float t = 100f;
            var p = new AutoPlayInputProvider(() => t);
            p.SetActive(true);

            p.Press(1, AutoPlayInputProvider.HoldSeconds);
            t += 0.05f;   // a later frame: the reader ran before the driver in the press frame
            Assert.IsTrue(p.GetAbility2Pressed(), "the press survives until it is read");
            Assert.IsFalse(p.GetAbility2Pressed(), "read once: a second read is not a second press");
            Assert.IsTrue(p.GetAbilityHeld(1), "still held (range preview)");
            t += AutoPlayInputProvider.HoldSeconds;
            Assert.IsFalse(p.GetAbilityHeld(1), "released (cast)");

            p.Press(3, 0.1f);
            t += AutoPlayInputProvider.PressLatchSeconds + 0.01f;
            Assert.IsFalse(p.GetAbility4Pressed(), "an unread press expires");
            Assert.IsFalse(p.GetAbility1Pressed(), "other slots untouched");

            p.Press(0, 0.1f);
            p.SetActive(false);
            Assert.IsFalse(p.GetAbility1Pressed(), "deactivating drops pending presses");
        }

        [Test]
        public void AutoPlayProvider_IsDormantUntilActivated_AndNeverClaimsTheDevice()
        {
            var p = new AutoPlayInputProvider();
            Assert.AreEqual(Vector2.zero, p.GetMovement());
            Assert.IsFalse(p.GetAbility1Pressed());
            Assert.IsFalse(p.GetAbilityHeld(0));
            Assert.AreEqual(InputDeviceKind.None, ((IInputProvider)p).Device);
            Assert.IsTrue(float.IsNegativeInfinity(((IInputProvider)p).LastActiveTime));
            p.SetActive(true);
            p.Tick(null, 0f);   // no local chicken: no stick
            Assert.AreEqual(Vector2.zero, p.GetMovement());
        }
    }
}
