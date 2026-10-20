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

        [Test]
        public void HeavyProbes_AlwaysInPlayers_InTheEditorOnlyWhenAsked()
        {
            Assert.IsTrue(HeavyProbes.Decide(isEditor: false, autoTestActive: false, editorToggle: false));
            Assert.IsFalse(HeavyProbes.Decide(isEditor: true, autoTestActive: false, editorToggle: false), "no Editor noise by default");
            Assert.IsTrue(HeavyProbes.Decide(isEditor: true, autoTestActive: true, editorToggle: false));
            Assert.IsTrue(HeavyProbes.Decide(isEditor: true, autoTestActive: false, editorToggle: true));
        }

        [Test]
        public void AndroidRestoreFromRecents_IgnoresTheOldExtras()
        {
            Assert.IsTrue(AutoTestArgs.IsRestoredLaunch(0x10000000 | AutoTestArgs.FlagActivityLaunchedFromHistory));
            Assert.IsFalse(AutoTestArgs.IsRestoredLaunch(0x10000000), "FLAG_ACTIVITY_NEW_TASK alone: a fresh am start");
            Assert.AreEqual(0x00100000, AutoTestArgs.FlagActivityLaunchedFromHistory, "Intent.FLAG_ACTIVITY_LAUNCHED_FROM_HISTORY");
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

        [Test]
        public void DebugHudScale_FitsThePanelOnAPhoneScreen()
        {
            const float pixel9Height = 1080f, content = 32f + 34 * 16f;   // ~34 lines with the probe readouts
            float s = CluckWars.UI.DebugHud.FitScale(3f, pixel9Height, content);
            Assert.Less(s, 3f, "the 3x density scale would push the panel off a 1080 px landscape screen");
            Assert.LessOrEqual((CluckWars.UI.DebugHud.PanelTop + content) * s, pixel9Height, "the whole panel fits");
            Assert.AreEqual(1.5f, CluckWars.UI.DebugHud.FitScale(1.5f, 4000f, content), 1e-4f, "room to spare: density wins");
            Assert.AreEqual(CluckWars.UI.DebugHud.MinScale, CluckWars.UI.DebugHud.FitScale(3f, 100f, content), 1e-4f, "never below the floor");
        }

        // ---- auto-play --------------------------------------------------------------------------------------------

        private static AutoPlayPile[] Piles(params (float x, float z, float amount)[] p)
        {
            var a = new AutoPlayPile[p.Length];
            for (int i = 0; i < p.Length; i++) a[i] = new AutoPlayPile { Position = new Vector2(p[i].x, p[i].z), Amount = p[i].amount };
            return a;
        }

        private static AutoPlayView View(Vector2 self, float cargo, float capacity, AutoPlayPile[] piles,
            bool hasBase = true, Vector2 basePos = default, AutoPlayRival[] rivals = null) => new AutoPlayView
        {
            Self = self, Cargo = cargo, Capacity = capacity, HasBase = hasBase, BasePos = basePos, BaseRadius = 3f,
            Piles = piles, PileCount = piles.Length, Rivals = rivals ?? new AutoPlayRival[0], RivalCount = rivals?.Length ?? 0,
        };

        [Test]
        public void AutoPlay_FullLoop_ForageToReturnFraction_BankAtOwnBaseUntilEmpty_Repeat()
        {
            var brain = new AutoPlayBrain(AutoPlayPersonality.Runner);   // returns at 60 %
            var piles = Piles((10f, 0f, 30f), (-30f, 0f, 30f), (2f, 0f, 0f));
            var home = new Vector2(0f, -20f);

            var dir = brain.Tick(0f, View(Vector2.zero, 0f, 40f, piles, basePos: home), 0.1f);
            Assert.AreEqual(AutoPlayState.Forage, brain.State);
            Assert.AreEqual(AutoPlayGoal.ToPile, brain.Goal);
            Assert.AreEqual(0, brain.TargetPile, "the empty pile is skipped, the near one wins");
            Assert.Greater(dir.x, 0.9f);

            dir = brain.Tick(1f, View(new Vector2(9.5f, 0f), 21f, 40f, piles, basePos: home), 0.1f);
            Assert.AreEqual(AutoPlayGoal.AtPile, brain.Goal, "below 60 %: keeps pecking");
            Assert.AreEqual(Vector2.zero, dir);

            dir = brain.Tick(2f, View(new Vector2(9.5f, 0f), 24f, 40f, piles, basePos: home), 0.1f);
            Assert.AreEqual(AutoPlayState.Deposit, brain.State, "60 % of capacity: go bank");
            Assert.AreEqual(AutoPlayGoal.ToBase, brain.Goal);
            Assert.Less(dir.y, -0.4f, "heads to the own base");

            dir = brain.Tick(3f, View(new Vector2(0f, -19f), 24f, 40f, piles, basePos: home), 0.1f);
            Assert.AreEqual(AutoPlayGoal.AtBase, brain.Goal, "inside the deposit zone: stands still");
            Assert.AreEqual(Vector2.zero, dir);
            brain.Tick(4f, View(new Vector2(0f, -19f), 6f, 40f, piles, basePos: home), 0.1f);
            Assert.AreEqual(AutoPlayState.Deposit, brain.State, "stays until cargo is 0");

            brain.Tick(5f, View(new Vector2(0f, -19f), 0f, 40f, piles, basePos: home), 0.1f);
            Assert.AreEqual(AutoPlayState.Forage, brain.State, "banked everything: back to foraging");
            Assert.AreEqual(AutoPlayGoal.ToPile, brain.Goal);
        }

        [Test]
        public void AutoPlay_ACapacityPeckCannotFillExactly_StillBanks()
        {
            // The first live run: Peck = 3, capacity 40, so cargo topped out at 39 and the old "full" test never fired.
            var brain = new AutoPlayBrain(AutoPlayPersonality.Balanced);
            var piles = Piles((1f, 0f, 30f));
            brain.Tick(0f, View(Vector2.zero, 39f, 40f, piles, basePos: new Vector2(0f, -20f)), 0.5f);
            Assert.AreEqual(AutoPlayState.Deposit, brain.State);
        }

        [Test]
        public void AutoPlay_PileRunsDry_WithCargo_GoesToBank()
        {
            var brain = new AutoPlayBrain(AutoPlayPersonality.Aggressive);
            var piles = Piles((1f, 0f, 5f), (25f, 0f, 30f));
            brain.Tick(0f, View(new Vector2(1f, 0f), 3f, 40f, piles, basePos: new Vector2(0f, -20f)), 0.5f);
            Assert.AreEqual(AutoPlayGoal.AtPile, brain.Goal);
            piles[0].Amount = 0f;
            brain.Tick(0.5f, View(new Vector2(1f, 0f), 6f, 40f, piles, basePos: new Vector2(0f, -20f)), 0.5f);
            Assert.AreEqual(AutoPlayState.Deposit, brain.State, "the pecked pile ran dry: bank what we have");
        }

        [Test]
        public void AutoPlay_NoProgressFor2s_RetargetsAndStrafes()
        {
            var brain = new AutoPlayBrain(AutoPlayPersonality.Runner);
            var piles = Piles((10f, 0f, 10f), (0f, 14f, 10f));
            var self = Vector2.zero;
            brain.Tick(0f, View(self, 0f, 10f, piles, hasBase: false), 0.2f);
            Assert.AreEqual(0, brain.TargetPile);
            brain.Tick(1f, View(self, 0f, 10f, piles, hasBase: false), 0.2f);
            var dir = brain.Tick(2.1f, View(self, 0f, 10f, piles, hasBase: false), 0.2f);   // stuck
            Assert.AreEqual(1f, dir.magnitude, 1e-3f);
            Assert.Less(Mathf.Abs(Vector2.Dot(dir, Vector2.right)), 0.01f, "strafes sideways out of the jam");
            brain.Tick(3.5f, View(self, 0f, 10f, piles, hasBase: false), 0.2f);
            Assert.AreEqual(1, brain.TargetPile, "the stuck pile is skipped for a while");
        }

        [Test]
        public void AutoPlay_NoFoodAnywhere_BringsCargoHome_OrIdles()
        {
            var brain = new AutoPlayBrain(AutoPlayPersonality.Runner);
            var dry = Piles((5f, 5f, 0f));
            brain.Tick(0f, View(Vector2.zero, 3f, 40f, dry, basePos: new Vector2(-10f, 0f)), 0.5f);
            Assert.AreEqual(AutoPlayGoal.ToBase, brain.Goal);
            var dir = brain.Tick(0.1f, View(Vector2.zero, 0f, 40f, dry, basePos: new Vector2(-10f, 0f)), 0.5f);
            Assert.AreEqual(AutoPlayGoal.Idle, brain.Goal);
            Assert.AreEqual(Vector2.zero, dir);
        }

        [Test]
        public void AutoPlay_AggressiveHuntsALoadedRival_RunnerNever()
        {
            var piles = Piles((20f, 0f, 30f));
            var rivals = new[] { new AutoPlayRival { Position = new Vector2(0f, 6f), Cargo = 10f },
                                 new AutoPlayRival { Position = new Vector2(0f, 2f), Cargo = 0f } };
            var brain = new AutoPlayBrain(AutoPlayPersonality.Aggressive);
            brain.Tick(1f, View(Vector2.zero, 0f, 40f, piles, rivals: rivals), 0.5f);
            Assert.AreEqual(AutoPlayState.Forage, brain.State, "no hunt before the first interval");
            var dir = brain.Tick(6f, View(Vector2.zero, 0f, 40f, piles, rivals: rivals), 0.5f);
            Assert.AreEqual(AutoPlayState.Attack, brain.State);
            Assert.AreEqual(0, brain.TargetRival, "the loaded rival, not the closer empty-handed one");
            Assert.Greater(dir.y, 0.9f, "closes on it");
            Assert.IsFalse(brain.TakeAttackCast());
            brain.Tick(6.5f, View(new Vector2(0f, 4f), 0f, 40f, piles, rivals: rivals), 0.5f);
            Assert.IsTrue(brain.TakeAttackCast(), "in cast range: one offensive cast");
            Assert.IsFalse(brain.TakeAttackCast(), "once");
            Assert.AreEqual(AutoPlayState.Forage, brain.State, "then back to foraging");

            var runner = new AutoPlayBrain(AutoPlayPersonality.Runner);
            for (float t = 0f; t < 30f; t += 1f) runner.Tick(t, View(Vector2.zero, 0f, 40f, piles, rivals: rivals), 0.5f);
            Assert.AreNotEqual(AutoPlayState.Attack, runner.State, "a runner only forages and banks");
        }

        [Test]
        public void AutoPlay_PersonalityFromPlayerId_AndWhatEachCasts()
        {
            Assert.AreEqual("aggressive", AutoPlayPersonality.For(3).Name);
            Assert.AreEqual("runner", AutoPlayPersonality.For(1).Name);
            Assert.AreEqual("balanced", AutoPlayPersonality.For(2).Name);
            Assert.AreEqual("runner", AutoPlayPersonality.For(-2).Name, "never throws on odd ids");
            Assert.IsTrue(AutoPlayPersonality.Aggressive.Hunts);
            Assert.IsFalse(AutoPlayPersonality.Runner.Hunts);
            Assert.AreEqual(0.7f, AutoPlayPersonality.Balanced.ReturnFraction, 1e-4f);
            Assert.IsTrue(AutoPlayCasting.IsOffensive(CluckWars.Abilities.AbilityCategory.Steal));
            Assert.IsTrue(AutoPlayCasting.IsOffensive(CluckWars.Abilities.AbilityCategory.Control));
            Assert.IsFalse(AutoPlayCasting.Allowed(CluckWars.Abilities.AbilityCategory.Steal, castsOffense: false));
            Assert.IsTrue(AutoPlayCasting.Allowed(CluckWars.Abilities.AbilityCategory.Utility, castsOffense: false));
            Assert.IsTrue(AutoPlayCasting.Allowed(CluckWars.Abilities.AbilityCategory.Control, castsOffense: true));
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
