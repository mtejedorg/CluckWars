using System.Collections.Generic;
using System.IO;
using CluckWars.Abilities;
using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// BL-208 slice 1: the fighter personality's target choice (<see cref="FighterTactics"/>), the tick-driven decision cadence
    /// (<see cref="TickCadence"/>, <see cref="SimClock"/>), the flags and the telemetry lines. Pure inputs only: every reach, cargo
    /// and time below is an input the test states, not a value read back from a shipped asset.
    /// </summary>
    public class FighterAutoPlayTests
    {
        private const float Reach = 5f;

        private static FighterAttack Attack(int slot, AbilityJob job, bool ready = true, float reach = Reach, float control = 0f) =>
            new FighterAttack { Slot = slot, Job = job, Ready = ready, Reach = reach, ControlSeconds = control };

        private static FighterRival Rival(float x, float cargo = 0f, bool peck = false, bool stunned = false, bool untouchable = false, bool marked = false) =>
            new FighterRival { Position = new Vector2(x, 0f), Cargo = cargo, CastingPeck = peck, Stunned = stunned, Untouchable = untouchable, Marked = marked };

        private static FighterView View(FighterRival[] rivals, params FighterAttack[] attacks) => new FighterView
        {
            Self = Vector2.zero, Rivals = rivals, RivalCount = rivals.Length, Attacks = attacks, AttackCount = attacks.Length, MarkSlot = -1,
        };

        // ---- Stun / Shove / Steal -------------------------------------------------------------------------------------

        [Test]
        public void Stun_GoesToTheNearestRivalInReach()
        {
            var d = FighterTactics.Choose(View(new[] { Rival(4f), Rival(2f), Rival(Reach + 1f) }, Attack(1, AbilityJob.Stun)));
            Assert.AreEqual(1, d.Slot);
            Assert.AreEqual(-1, d.Chase, "something is in reach: cast, do not chase");
        }

        [Test]
        public void ARivalCastingPeck_BeatsANearerOneThatIsNot_ForStunAndShove()
        {
            var pecking = Rival(Reach - 0.5f, peck: true);
            var near = Rival(1f);
            foreach (var job in new[] { AbilityJob.Stun, AbilityJob.Shove })
            {
                var view = View(new[] { near, pecking }, Attack(2, job));
                Assert.AreEqual(2, FighterTactics.Choose(view).Slot, job.ToString());
            }
        }

        [Test]
        public void StunIsPreferredToShove_OnTheSameRival()
        {
            var d = FighterTactics.Choose(View(new[] { Rival(3f) }, Attack(0, AbilityJob.Shove), Attack(3, AbilityJob.Stun)));
            Assert.AreEqual(3, d.Slot);
        }

        [Test]
        public void NothingIsSpentOnAStunnedOrUntouchableRival_OrOnOneOutOfReach()
        {
            var rivals = new[] { Rival(2f, stunned: true), Rival(3f, untouchable: true), Rival(Reach + 0.1f) };
            var d = FighterTactics.Choose(View(rivals, Attack(1, AbilityJob.Stun), Attack(2, AbilityJob.Shove)));
            Assert.AreEqual(-1, d.Slot);
        }

        [Test]
        public void AMoveOnCooldown_IsNotPressed_ButKeepsTheChase()
        {
            var d = FighterTactics.Choose(View(new[] { Rival(Reach + 4f) }, Attack(1, AbilityJob.Stun, ready: false)));
            Assert.AreEqual(-1, d.Slot);
            Assert.AreEqual(0, d.Chase, "it walks at the rival so the move is back when it arrives");
        }

        [Test]
        public void Steal_NeedsALoadedRivalInReach()
        {
            var empty = View(new[] { Rival(2f, cargo: 0f) }, Attack(1, AbilityJob.Steal));
            Assert.AreEqual(-1, FighterTactics.Choose(empty).Slot);

            var loaded = View(new[] { Rival(2f, cargo: 0f), Rival(4f, cargo: 3f) }, Attack(1, AbilityJob.Steal));
            Assert.AreEqual(1, FighterTactics.Choose(loaded).Slot);
        }

        [Test]
        public void ChaseTarget_PrefersAPeckerThenALoadedRivalForAStealKit_AndIgnoresTheFarOnes()
        {
            var steal = View(new[] { Rival(8f), Rival(10f, cargo: 4f) }, Attack(1, AbilityJob.Steal, ready: false));
            Assert.AreEqual(1, FighterTactics.Choose(steal).Chase, "a loaded rival draws a thief");

            var pecker = View(new[] { Rival(7f), Rival(9f, peck: true) }, Attack(1, AbilityJob.Stun, ready: false));
            Assert.AreEqual(1, FighterTactics.Choose(pecker).Chase);

            var far = View(new[] { Rival(FighterTactics.ChaseMetres + 1f) }, Attack(1, AbilityJob.Stun, ready: false));
            Assert.AreEqual(-1, FighterTactics.Choose(far).Chase);
        }

        [Test]
        public void AKitWithoutAnyAttack_NeverFights()
        {
            var d = FighterTactics.Choose(View(new[] { Rival(1f, cargo: 5f, peck: true) }, Attack(1, AbilityJob.Speed)));
            Assert.AreEqual(-1, d.Slot);
            Assert.AreEqual(-1, d.Chase);
        }

        // ---- Death Mark -----------------------------------------------------------------------------------------------

        private static FighterView MarkView(FighterMarkPhase phase, FighterRival[] rivals, params FighterAttack[] attacks)
        {
            var v = View(rivals, attacks);
            v.MarkSlot = 0;
            v.Mark = phase;
            return v;
        }

        [Test]
        public void ReadyMark_IsPressedAtOnce_WhenARivalExists()
        {
            Assert.AreEqual(0, FighterTactics.Choose(MarkView(FighterMarkPhase.Ready, new[] { Rival(30f) })).Slot);
            Assert.AreEqual(-1, FighterTactics.Choose(MarkView(FighterMarkPhase.Ready, new FighterRival[0])).Slot);
        }

        [Test]
        public void MarkedRival_IsChased_AndKillIsPressedTheMomentItLands()
        {
            var v = MarkView(FighterMarkPhase.Marked, new[] { Rival(1f), Rival(20f, marked: true) }, Attack(1, AbilityJob.Stun));
            v.ArmRemaining = 3f;
            v.WindowRemaining = 4f;
            var chase = FighterTactics.Choose(v);
            Assert.AreEqual(1, chase.Chase, "the marked rival, not the nearest");
            Assert.AreEqual(-1, chase.Slot);

            v.CanKillNow = true;
            Assert.AreEqual(0, FighterTactics.Choose(v).Slot, "Kill is the Death Mark slot pressed again");
        }

        [Test]
        public void TheStun_WaitsUntilItWillStillBeRunningWhenTheArmEnds()
        {
            const float stunSeconds = 2f;
            var rivals = new[] { Rival(3f, marked: true) };
            var v = MarkView(FighterMarkPhase.Marked, rivals, Attack(1, AbilityJob.Stun, control: stunSeconds));
            v.WindowRemaining = 3.5f;

            v.ArmRemaining = stunSeconds + 0.5f;     // too early: the stun would be over before the arm ends
            Assert.AreEqual(-1, FighterTactics.Choose(v).Slot);

            v.ArmRemaining = stunSeconds - FighterTactics.StunLeadMarginSeconds;
            Assert.AreEqual(1, FighterTactics.Choose(v).Slot);
        }

        [Test]
        public void TheStun_IsNotWastedOnAnAlreadyStunnedMark_OrAtTheEndOfTheWindow()
        {
            var v = MarkView(FighterMarkPhase.Marked, new[] { Rival(3f, marked: true, stunned: true) }, Attack(1, AbilityJob.Stun, control: 2f));
            v.ArmRemaining = 0f;
            v.WindowRemaining = 3f;
            Assert.AreEqual(-1, FighterTactics.Choose(v).Slot);

            v = MarkView(FighterMarkPhase.Marked, new[] { Rival(3f, marked: true) }, Attack(1, AbilityJob.Stun, control: 2f));
            v.ArmRemaining = 0f;
            v.WindowRemaining = FighterTactics.MinWindowForStunSeconds * 0.5f;
            Assert.AreEqual(-1, FighterTactics.Choose(v).Slot);
        }

        [Test]
        public void WhileMarked_OtherRivalsGetNoCasts()
        {
            var v = MarkView(FighterMarkPhase.Marked, new[] { Rival(1f, peck: true), Rival(15f, marked: true) }, Attack(1, AbilityJob.Stun, control: 2f));
            v.ArmRemaining = 3f;
            v.WindowRemaining = 4f;
            Assert.AreEqual(-1, FighterTactics.Choose(v).Slot, "the stun is kept for the marked rival");
        }

        // ---- personality + flags --------------------------------------------------------------------------------------

        [Test]
        public void FighterIsSelectedByName_NeverByPlayerId()
        {
            Assert.IsTrue(AutoPlayPersonality.TryParse("Fighter", out var p));
            Assert.IsTrue(p.Fights);
            for (int id = -3; id < 12; id++) Assert.IsFalse(AutoPlayPersonality.For(id).Fights, "PlayerId " + id);
            Assert.IsFalse(AutoPlayPersonality.TryParse("berserk", out _));
            foreach (var name in AutoPlayPersonality.Names.Split('|')) Assert.IsTrue(AutoPlayPersonality.TryParse(name, out _), name);
        }

        [Test]
        public void PersonalityFlag_IsParsedAndAnUnknownNameIsReported()
        {
            var ok = AutoTestConfig.FromMap(new Dictionary<string, string> { { AutoTestConfig.PersonalityKey, "FIGHTER" } });
            Assert.AreEqual("fighter", ok.Personality);
            Assert.IsEmpty(ok.Problems);

            var bad = AutoTestConfig.FromMap(new Dictionary<string, string> { { AutoTestConfig.PersonalityKey, "berserk" } });
            Assert.IsNull(bad.Personality);
            Assert.IsNotEmpty(bad.Problems);
        }

        [Test]
        public void SeedFlag_IsReadFromTheCommandLine_AndGarbageIsIgnored()
        {
            Assert.IsTrue(DevSeed.TryFromArgs(new[] { "game.exe", "-cw_autoplay", "1", "-CW_SEED", "-77" }, out int seed));
            Assert.AreEqual(-77, seed);
            Assert.IsFalse(DevSeed.TryFromArgs(new[] { "game.exe", "-cw_seed", "abc" }, out _));
            Assert.IsFalse(DevSeed.TryFromArgs(new[] { "game.exe", "-cw_seed" }, out _));
            Assert.IsFalse(DevSeed.TryFromArgs(null, out _));
            var cfg = AutoTestConfig.FromMap(new Dictionary<string, string> { { AutoTestConfig.SeedKey, "abc" } });
            Assert.IsNotEmpty(cfg.Problems);
        }

        // ---- tick-driven cadence --------------------------------------------------------------------------------------

        private static List<float> DecisionTimes(float frameSimSeconds, float totalSimSeconds)
        {
            var cadence = new TickCadence();
            var times = new List<float>();
            for (float t = 0f; t <= totalSimSeconds; t += frameSimSeconds)
                if (cadence.TryAdvance(t, AutoPlayInputProvider.DecisionStepSeconds, out float at)) times.Add(at);
            return times;
        }

        [Test]
        public void Cadence_DecidesAtMostOncePerStep_WhateverTheFrameRate()
        {
            float step = AutoPlayInputProvider.DecisionStepSeconds;
            var fast = DecisionTimes(step / 4f, 10f);        // 4 frames per step (a 1x run at a high frame rate)
            for (int i = 1; i < fast.Count; i++) Assert.AreEqual(step, fast[i] - fast[i - 1], 1e-4f);
            Assert.AreEqual(Mathf.FloorToInt(10f / step) + 1, fast.Count, 1);
        }

        [Test]
        public void Cadence_AFrameLongerThanAStep_DecidesOnceAndLandsOnABoundary()
        {
            float step = AutoPlayInputProvider.DecisionStepSeconds;
            var slow = DecisionTimes(step * 5.3f, 10f);      // a x10 run: every frame spans five steps
            for (int i = 0; i < slow.Count; i++)
                Assert.AreEqual(0f, Mathf.Repeat(slow[i] / step + 0.001f, 1f) - 0.001f, 1e-3f, "decision times are step boundaries");
            Assert.Greater(slow.Count, 10f / (step * 5.3f) - 1);
        }

        [Test]
        public void Cadence_ResetStartsAgain_AndTimeNeverRunsBackwards()
        {
            var c = new TickCadence();
            Assert.IsTrue(c.TryAdvance(1f, 0.1f, out _));
            Assert.IsFalse(c.TryAdvance(1.05f, 0.1f, out _));
            Assert.IsFalse(c.TryAdvance(0.5f, 0.1f, out float held), "an earlier time never decides");
            Assert.AreEqual(1f, held, 1e-4f);
            c.Reset();
            Assert.IsTrue(c.TryAdvance(0.5f, 0.1f, out _));
        }

        [Test]
        public void TheDriversAndProbes_ReadSimulationTime_NotTheWallClock()
        {
            string dir = Path.Combine(Application.dataPath, "_Game/Scripts/Diagnostics/");
            string provider = File.ReadAllText(dir + "AutoPlayInputProvider.cs");
            StringAssert.DoesNotContain("Time.unscaledTime", System.Text.RegularExpressions.Regex.Replace(provider, @"///.*", ""),
                "the provider clock is SimClock.Now");
            StringAssert.Contains("clock ?? SimClock.Now", provider);

            string probe = File.ReadAllText(dir + "InvariantProbe.cs");
            StringAssert.DoesNotContain("Time.realtimeSinceStartup", probe);
            StringAssert.DoesNotContain("Time.unscaledTime", probe);

            string driver = File.ReadAllText(dir + "AutoTestDriver.cs");
            int at = driver.IndexOf("private void TickAutoPlay(", System.StringComparison.Ordinal);
            int end = driver.IndexOf("private AutoPlayLog.Snapshot Snapshot()", at, System.StringComparison.Ordinal);
            string body = driver.Substring(at, end - at);
            StringAssert.DoesNotContain("Time.unscaledTime", body);
            StringAssert.DoesNotContain("Time.realtimeSinceStartup", body);
        }

        // ---- telemetry lines ------------------------------------------------------------------------------------------

        [Test]
        public void TelemetryLines_AreKeyValueTokens_InInvariantCulture()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            try
            {
                Assert.AreEqual("steal tick=12 thief=P1 victim=B2 amount=1.50", MatchTelemetry.Steal(12, "P1", "B2", 1.5f));
                Assert.AreEqual("peckcut tick=7 pecker=B0 class=Fatty cause=execution", MatchTelemetry.PeckCut(7, "B0", "Fatty", MatchTelemetry.CauseExecution));
                Assert.AreEqual("contact tick=3 seat=P1 rival=B2 round=1 t=4.25 dist=3.10", MatchTelemetry.Contact(3, "P1", "B2", 1, 4.25f, 3.1f));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
        }

        [Test]
        public void KillLine_PayoutIsTheFlatBountyPlusTheVictimCargo()
        {
            const float bounty = 4f, cargo = 2.5f;
            string line = MatchTelemetry.Kill(9, "P2", "B1", bounty, cargo, victimPecking: true);
            StringAssert.Contains("killer=P2 victim=B1", line);
            StringAssert.Contains("payout=6.50 bounty=4.00 victim_cargo=2.50 victim_pecking=1", line);
            StringAssert.Contains("payout=4.00 bounty=4.00 victim_cargo=0.00 victim_pecking=0", MatchTelemetry.Kill(9, "P2", "B1", bounty, 0f, false));
        }

        [Test]
        public void EveryTelemetryCause_IsDistinct()
        {
            var causes = new HashSet<string> { MatchTelemetry.CauseStun, MatchTelemetry.CauseShove, MatchTelemetry.CauseExecution, MatchTelemetry.CauseEmptyPile };
            Assert.AreEqual(4, causes.Count);
        }
    }
}
