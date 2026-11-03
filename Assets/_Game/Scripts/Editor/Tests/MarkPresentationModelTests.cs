using System.Collections.Generic;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Death Mark presentation model (pure): the draw, the marker states, the hexes, the victim's toast / darkness /
    /// hysteresis / hunter badge, every ending (each one ends exactly clean), and host migration. Endings are driven through
    /// <see cref="MarkRoleRules"/> from chicken snapshots, the way the adapter feeds it.
    /// </summary>
    public class MarkPresentationModelTests
    {
        private const float Dt = 1f / 60f;
        private const long Me = 1, Rival = 2, Third = 3, Fourth = 4;
        private const float R = 4f;   // inner circle

        // ---- Fixture -----------------------------------------------------------------------------------------------

        private sealed class World
        {
            public readonly List<MarkChicken> Chickens = new List<MarkChicken>();
            public bool MatchRunning = true, ReducedMotion, BannerMayShow = true;
            public bool CanKillNow, AmbushReady, Walled, HunterInvisible, HunterHidden;
            public float Distance = 20f, HunterDistance = 20f, Remaining = 12f, Window = 12f, Arm = 0.6f;
            public readonly MarkPresentationModel Model = new MarkPresentationModel();
            private long _shownHunter;

            public World()
            {
                for (long id = 1; id <= 4; id++)
                    Chickens.Add(new MarkChicken { Id = id, Corner = (int)id - 1, IsLocal = id == Me, Position = new Vector2(id * 10f, 0f) });
            }

            public void SetMark(long hunter, long target, int windowEnd = 1000) => Edit(hunter, c => { c.MarkedTargetId = target; c.WindowEndTick = windowEnd; return c; });
            public void ClearMark(long hunter) => Edit(hunter, c => { c.MarkedTargetId = 0; return c; });
            public void SetRemoved(long id, bool removed) => Edit(id, c => { c.Removed = removed; return c; });
            public void Remove(long id) => Chickens.RemoveAll(c => c.Id == id);

            private void Edit(long id, System.Func<MarkChicken, MarkChicken> f)
            {
                int i = Chickens.FindIndex(c => c.Id == id);
                Chickens[i] = f(Chickens[i]);
            }

            public MarkOutputs Step(float dt = Dt)
            {
                var r = MarkRoleRules.Resolve(Chickens, _shownHunter);
                var input = new MarkInputs
                {
                    MatchRunning = MatchRunning, ReducedMotion = ReducedMotion, BannerMayShow = BannerMayShow,
                    Role = r.Role, MarkKey = r.Key, Remaining = Remaining, Window = Window, ArmSeconds = Arm,
                    Distance = Distance, CanKillNow = CanKillNow, AmbushReadyOnTarget = AmbushReady, Walled = Walled,
                    HunterInvisible = HunterInvisible, HunterHiddenFromView = HunterHidden, HunterDistance = HunterDistance, InnerRadius = R,
                };
                if (r.Role != MarkRole.None)
                {
                    input.TargetCorner = Chickens[r.Target].Corner;
                    input.HunterCorner = Chickens[r.Hunter].Corner;
                    _shownHunter = Chickens[r.Hunter].Id;
                }
                else _shownHunter = 0;
                return Model.Step(input, dt);
            }

            /// <summary>Steps for <paramref name="seconds"/>, OR-ing every event seen.</summary>
            public MarkEvents Run(float seconds, out MarkOutputs last)
            {
                var all = MarkEvents.None;
                last = default;
                int n = Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));
                for (int i = 0; i < n; i++) { last = Step(); all |= last.Events; }
                return all;
            }

            public int CountEvents(float seconds, MarkEvents which)
            {
                int count = 0;
                int n = Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));
                for (int i = 0; i < n; i++) if ((Step().Events & which) != 0) count++;
                return count;
            }
        }

        private static void AssertExactlyClean(MarkOutputs o, string why)
        {
            Assert.IsTrue(o.IsClean, why + ": not clean");
            Assert.AreEqual(0f, o.Dim, why + ": dim");
            Assert.AreEqual(0f, o.Cold, why + ": cold");
            Assert.AreEqual(0f, o.Red, why + ": red");
            Assert.AreEqual(MarkRole.None, o.Role, why);
        }

        /// <summary>Lets an ending run its fade out, checking the fade never re-arms anything, then asserts clean.</summary>
        private static void AssertEndsClean(World w, string why)
        {
            var o = w.Step();
            Assert.AreEqual(MarkEvents.None, o.Events, why + ": an ending fires no event");
            Assert.IsFalse(o.AssassinMarkActive || o.KillHex || o.AmbushLit || o.ForceShowHunter, why + ": booleans drop at once");
            var events = w.Run(FeedbackTuning.MarkEndFadeSeconds + 0.05f, out o);
            Assert.AreEqual(MarkEvents.None, events, why + ": the fade fires nothing");
            AssertExactlyClean(o, why);
        }

        private static World AssassinLocked()
        {
            var w = new World();
            w.SetMark(Me, Rival);
            w.Run(1f, out _);
            return w;
        }

        private static World VictimMarked(bool cold = false)
        {
            var w = new World();
            if (cold) w.HunterDistance = R - 1f;
            w.SetMark(Rival, Me);
            w.Run(2f, out _);
            return w;
        }

        // ---- The draw -----------------------------------------------------------------------------------------------

        [Test]
        public void Draw_StepsTicksThenLocks_InsideTheArm_MarkerAppearsAtLock()
        {
            var w = new World();
            w.SetMark(Me, Rival);
            int ticks = 0, lockAt = -1, frame = 0;
            MarkOutputs o = default;
            while (lockAt < 0 && frame < 120)
            {
                o = w.Step();
                if ((o.Events & MarkEvents.DrawTick) != 0) ticks++;
                if ((o.Events & MarkEvents.Lock) != 0) lockAt = frame;
                else Assert.AreEqual(0f, o.MarkerAlpha, "no marker before the lock");
                frame++;
            }
            Assert.AreEqual(MarkPresentationModel.DrawStepCount(false, 0.6f), ticks, "one tick per row step");
            Assert.AreEqual(6, ticks);
            Assert.LessOrEqual((lockAt + 1) * Dt, 0.6f + 2 * Dt, "the lock lands at the draw's end");
            Assert.AreEqual(1f, o.MarkerAlpha);
            Assert.AreEqual(o.DrawSteps - 1, o.DrawStep, "it lands on the last step: the target's row");
            Assert.AreEqual(1, w.CountEvents(1f, MarkEvents.Lock) + 1, "one lock per mark");
        }

        [TestCase(0.6f)]
        [TestCase(0.4f)]
        [TestCase(0.1f)]
        public void Draw_NeverOutlastsTheArm(float arm)
        {
            Assert.LessOrEqual(MarkPresentationModel.DrawDuration(false, arm), arm);
            Assert.LessOrEqual(MarkPresentationModel.DrawDuration(true, arm), arm);
            var w = new World { Arm = arm };
            w.SetMark(Me, Rival);
            int frames = 0;
            while ((w.Step().Events & MarkEvents.Lock) == 0) frames++;
            Assert.LessOrEqual(frames * Dt, arm + Dt);
        }

        [Test]
        public void ReducedMotion_NoSweepNoTicks_TheRowFadesIn_LockStillPlays()
        {
            var w = new World { ReducedMotion = true };
            w.SetMark(Me, Rival);
            var first = w.Step();
            Assert.AreEqual(1, first.DrawSteps, "no sweep: the target's row only");
            Assert.Less(first.DrawAlpha, 0.2f, "fades in");
            var events = w.Run(1f, out var o);
            Assert.AreEqual(MarkEvents.None, (events | first.Events) & MarkEvents.DrawTick);
            Assert.AreNotEqual(MarkEvents.None, (events | first.Events) & MarkEvents.Lock);
            Assert.AreEqual(1f, o.MarkerPop, "no pop under reduced motion");
        }

        [Test]
        public void TheSkull_ShowsOnTheTargetsRow_AtTheLock_ForTheAssassin()
        {
            var w = new World();
            w.SetMark(Me, Rival);
            Assert.AreEqual(0f, w.Step().SkullAlpha, "not during the draw");
            w.Run(1f, out var o);
            Assert.AreEqual(1, o.SkullCorner, "the target's corner");
            Assert.AreEqual(1f, o.SkullAlpha);
            Assert.IsTrue(o.AssassinMarkActive);
        }

        // ---- Marker states and hexes --------------------------------------------------------------------------------

        [Test]
        public void Marker_HuntWalledKill_FollowTheInputs_RingDrains()
        {
            var w = AssassinLocked();
            w.Remaining = 6f;
            var o = w.Step();
            Assert.AreEqual(MarkMarkerMode.Hunt, o.MarkerMode);
            Assert.AreEqual(0.5f, o.MarkerFill01, 1e-4f);
            Assert.AreEqual(20, o.MarkerMetres);
            w.Walled = true;
            Assert.AreEqual(MarkMarkerMode.Walled, w.Step().MarkerMode);
            w.Remaining = 3f;
            Assert.AreEqual(0.25f, w.Step().MarkerFill01, 1e-4f, "walled: the ring keeps draining");
            w.CanKillNow = true;
            Assert.AreEqual(MarkMarkerMode.Kill, w.Step().MarkerMode, "kill wins");
        }

        [Test]
        public void KillHex_ExactlyWhileCanKillNow_AmbushLitSuppressedByKill()
        {
            var w = AssassinLocked();
            w.AmbushReady = true;
            var o = w.Step();
            Assert.IsTrue(o.AmbushLit);
            Assert.IsFalse(o.KillHex);
            w.CanKillNow = true;
            o = w.Step();
            Assert.IsTrue(o.KillHex);
            Assert.IsFalse(o.AmbushLit, "suppressed while KILL shows");
            w.CanKillNow = false;
            o = w.Step();
            Assert.IsFalse(o.KillHex, "reverts the frame CanKillNow is false");
            Assert.IsTrue(o.AmbushLit);
        }

        [Test]
        public void AmbushLit_WaitsForTheLock()
        {
            var w = new World { AmbushReady = true };
            w.SetMark(Me, Rival);
            Assert.IsFalse(w.Step().AmbushLit, "the draw has not said who the target is yet");
        }

        [Test]
        public void KillReady_FiresOnEachRisingEdge_RateLimited()
        {
            var w = AssassinLocked();
            w.CanKillNow = true;
            Assert.AreNotEqual(MarkEvents.None, w.Step().Events & MarkEvents.KillReady);
            Assert.AreEqual(0, w.CountEvents(0.5f, MarkEvents.KillReady), "held: no repeat");
            w.CanKillNow = false; w.Step();
            w.CanKillNow = true;
            Assert.AreNotEqual(MarkEvents.None, w.Step().Events & MarkEvents.KillReady, "a new edge after 0.4 s fires");
            w.CanKillNow = false; w.Step();
            w.CanKillNow = true;
            Assert.AreEqual(MarkEvents.None, w.Step().Events & MarkEvents.KillReady, "flicker inside 0.4 s does not re-fire");
        }

        // ---- Victim -------------------------------------------------------------------------------------------------

        [Test]
        public void Victim_ToastAndSting_WaitForGo_Then15Seconds()
        {
            var w = new World { BannerMayShow = false };
            w.SetMark(Rival, Me);
            var events = w.Run(1f, out var o);
            Assert.AreEqual(MarkEvents.None, events & MarkEvents.Marked);
            Assert.AreEqual(0f, o.ToastAlpha);
            Assert.Greater(o.Dim, 0f, "the darkness does not wait");
            Assert.AreEqual(1f, o.ClockAlpha);
            Assert.AreEqual(12, o.ClockSeconds);
            Assert.AreEqual(0, o.SkullCorner, "own row");

            w.BannerMayShow = true;
            o = w.Step();
            Assert.AreNotEqual(MarkEvents.None, o.Events & MarkEvents.Marked);
            w.Run(0.5f, out o);
            Assert.AreEqual(1f, o.ToastAlpha, "holding");
            w.Run(1.1f, out o);
            Assert.AreEqual(0f, o.ToastAlpha, "gone after ~1.5 s");
            Assert.AreEqual(0, w.CountEvents(2f, MarkEvents.Marked), "once per mark");
        }

        [Test]
        public void Victim_DimFadesInAndStays()
        {
            var w = new World();
            w.SetMark(Rival, Me);
            w.Run(FeedbackTuning.MarkDimFadeInSeconds * 0.5f, out var o);
            Assert.AreEqual(0.5f, o.Dim, 0.05f);
            w.Run(5f, out o);
            Assert.AreEqual(1f, o.Dim);
            Assert.AreEqual(0f, o.Cold, "far hunter: no cold");
        }

        [Test]
        public void Hysteresis_OscillatingBetweenRAnd13R_NeverFlips()
        {
            var w = VictimMarked(cold: true);
            w.Run(1f, out var o);
            Assert.AreEqual(1f, o.Cold);
            for (int i = 0; i < 40; i++)
            {
                w.HunterDistance = i % 2 == 0 ? R * 1.29f : R * 1.01f;
                w.Run(0.25f, out o);
                Assert.AreEqual(1f, o.Cold, $"step {i}: inside the exit radius, cold holds");
            }
        }

        [Test]
        public void Hysteresis_BeyondExitBriefly_Holds_ContinuouslyForOneSecond_Leaves()
        {
            var w = VictimMarked(cold: true);
            w.HunterDistance = R * 1.5f;
            w.Run(0.9f, out var o);
            Assert.AreEqual(1f, o.Cold, "0.9 s beyond: still cold");
            w.HunterDistance = R * 1.2f;
            w.Step();
            w.HunterDistance = R * 1.5f;
            w.Run(0.9f, out o);
            Assert.AreEqual(1f, o.Cold, "the clock restarted when it came back inside 1.3 R");
            w.Run(0.2f, out o);
            Assert.Less(o.Cold, 1f, "1.0 s continuously beyond: leaving");
            w.Run(FeedbackTuning.MarkColdFadeOutSeconds, out o);
            Assert.AreEqual(0f, o.Cold, "faded out over 1.0 s");
        }

        [Test]
        public void Cold_EntersAtR_FadesInOver04()
        {
            var w = VictimMarked();
            w.HunterDistance = R + 0.01f;
            w.Run(1f, out var o);
            Assert.AreEqual(0f, o.Cold, "just outside R: no cold");
            w.HunterDistance = R;
            w.Run(FeedbackTuning.MarkColdFadeInSeconds * 0.5f, out o);
            Assert.AreEqual(0.5f, o.Cold, 0.06f);
            w.Run(FeedbackTuning.MarkColdFadeInSeconds * 0.6f, out o);
            Assert.AreEqual(1f, o.Cold);
        }

        [Test]
        public void Red_IsColdPlusInvisible_IdenticalColdEitherWay()
        {
            var w = VictimMarked(cold: true);
            w.Run(1f, out var visible);
            w.HunterInvisible = true;
            w.Run(1f, out var invisible);
            Assert.AreEqual(visible.Cold, invisible.Cold, "cold is the same visible or invisible");
            Assert.AreEqual(0f, visible.Red);
            Assert.AreEqual(1f, invisible.Red);
            w.HunterInvisible = false;
            w.Run(FeedbackTuning.MarkColdFadeOutSeconds * 0.5f, out var o);
            Assert.AreEqual(0.5f, o.Red, 0.05f, "red fades out over 1.0 s when the hunter shows");
        }

        [Test]
        public void Red_NeedsCold()
        {
            var w = VictimMarked();
            w.HunterInvisible = true;
            w.Run(2f, out var o);
            Assert.AreEqual(0f, o.Red, "invisible but far: no red");
        }

        [Test]
        public void HunterBadge_BeyondTheCircle_WhenHiddenOrInvisible_NeverInside()
        {
            var w = VictimMarked();
            w.HunterHidden = true;
            var o = w.Step();
            Assert.AreEqual(1f, o.HunterBadgeAlpha);
            Assert.IsFalse(o.ForceShowHunter, "visible-but-off-screen: its own arrow, nothing forced");

            w.HunterHidden = false;
            Assert.AreEqual(0f, w.Step().HunterBadgeAlpha, "on screen beyond the ring: you can see it");

            w.HunterInvisible = true;
            o = w.Step();
            Assert.AreEqual(1f, o.HunterBadgeAlpha);
            Assert.IsTrue(o.ForceShowHunter, "invisible: the arrow is forced (direction only)");

            w.HunterDistance = R - 0.5f;
            o = w.Step();
            Assert.AreEqual(0f, o.HunterBadgeAlpha, "inside the inner circle: no arrow");
            Assert.IsFalse(o.ForceShowHunter);
        }

        [Test]
        public void ThirdParty_OnlyTheSkull()
        {
            var w = new World();
            w.SetMark(Rival, Third);
            w.Run(1f, out var o);
            Assert.AreEqual(MarkRole.ThirdParty, o.Role);
            Assert.AreEqual(2, o.SkullCorner);
            Assert.AreEqual(1f, o.SkullAlpha);
            Assert.AreEqual(0f, o.MarkerAlpha);
            Assert.AreEqual(0f, o.Dim);
            Assert.AreEqual(0f, o.ToastAlpha);
            Assert.AreEqual(0f, o.ClockAlpha);
            Assert.IsFalse(o.AssassinMarkActive);
        }

        [Test]
        public void Roles_AssassinFirst_ThenVictim_NearestHunterFirst_ThenKeptWhileItLives()
        {
            var w = new World();
            w.SetMark(Third, Me);
            w.SetMark(Rival, Me);
            var r = MarkRoleRules.Resolve(w.Chickens, 0);
            Assert.AreEqual(MarkRole.Victim, r.Role);
            Assert.AreEqual(Rival, w.Chickens[r.Hunter].Id, "nearest hunter (Rival at 20 m, Third at 30 m)");
            Assert.AreEqual(Third, w.Chickens[MarkRoleRules.Resolve(w.Chickens, Third).Hunter].Id, "the one already shown is kept");
            w.SetMark(Me, Fourth);
            Assert.AreEqual(MarkRole.Assassin, MarkRoleRules.Resolve(w.Chickens, 0).Role, "your own mark comes first");
        }

        // ---- Endings: each one ends exactly clean ------------------------------------------------------------------

        [Test] public void Ending_Kill_Clean()
        {
            var w = AssassinLocked();
            w.CanKillNow = true; w.Step();
            w.SetRemoved(Rival, true);   // executed: removed, then the Assassin's authority clears the mark
            AssertEndsClean(w, "kill");
        }

        [Test] public void Ending_Expiry_Clean()
        {
            var w = AssassinLocked();
            w.ClearMark(Me);
            AssertEndsClean(w, "expiry / fizzle");
        }

        [Test] public void Ending_TargetDespawned_Clean()
        {
            var w = AssassinLocked();
            w.Remove(Rival);
            AssertEndsClean(w, "target not found");
        }

        [Test] public void Ending_TargetRemovedForRespawn_Clean()
        {
            var w = VictimMarked(cold: true);
            w.HunterInvisible = true; w.Run(1f, out _);
            w.SetRemoved(Me, true);
            AssertEndsClean(w, "victim removed (dies / respawns)");
        }

        [Test] public void Ending_AssassinRemoved_Clean()
        {
            var w = VictimMarked(cold: true);
            w.SetRemoved(Rival, true);
            AssertEndsClean(w, "Assassin removed");
        }

        [Test] public void Ending_AssassinLeaves_Clean()
        {
            var w = VictimMarked(cold: true);
            w.Remove(Rival);
            AssertEndsClean(w, "Assassin despawned / left");
        }

        [Test] public void Ending_VictimLeaves_Clean()
        {
            var w = AssassinLocked();
            w.Remove(Rival);
            AssertEndsClean(w, "victim left");
        }

        [Test] public void Ending_LocalPlayerDespawned_Clean()
        {
            var w = VictimMarked(cold: true);
            w.Remove(Me);
            AssertEndsClean(w, "local despawned");
        }

        [Test] public void Ending_RoundReset_Clean()
        {
            var w = AssassinLocked();
            w.ClearMark(Me);          // ResetForNewRound drops the mark
            w.MatchRunning = false;   // back to the waiting room
            AssertEndsClean(w, "PLAY AGAIN");
        }

        [Test] public void Ending_MatchEnd_Clean()
        {
            var w = VictimMarked(cold: true);
            w.MatchRunning = false;   // the mark is still replicated, the round is over
            AssertEndsClean(w, "match end");
        }

        [Test] public void Ending_ThirdParty_Clean()
        {
            var w = new World();
            w.SetMark(Rival, Third);
            w.Run(1f, out _);
            w.ClearMark(Rival);
            AssertEndsClean(w, "third party");
        }

        [Test]
        public void Ending_OverridesTheColdExitDelay_FadesIn03()
        {
            var w = VictimMarked(cold: true);
            w.Run(1f, out var before);
            Assert.AreEqual(1f, before.Cold);
            w.ClearMark(Rival);
            w.Run(FeedbackTuning.MarkEndFadeSeconds * 0.5f, out var mid);
            Assert.AreEqual(0.5f, mid.Cold, 0.07f, "linear 0.3 s fade, not the 1 s exit");
            w.Run(FeedbackTuning.MarkEndFadeSeconds * 0.6f, out var o);
            AssertExactlyClean(o, "after 0.3 s");
        }

        [Test]
        public void Ending_DuringTheDraw_Clean_AndNoLock()
        {
            var w = new World();
            w.SetMark(Me, Rival);
            w.Run(0.2f, out _);
            w.ClearMark(Me);
            AssertEndsClean(w, "mark gone mid-draw");
        }

        // ---- Host migration and new marks ---------------------------------------------------------------------------

        [Test]
        public void HostMigration_SameMarkFoundAgain_ReplaysNothing()
        {
            var w = VictimMarked();
            var saved = new List<MarkChicken>(w.Chickens);
            w.Chickens.Clear();                                   // inputs vanish
            w.Run(0.1f, out _);
            w.Chickens.AddRange(saved);                           // ...and come back: the same mark
            var events = w.Run(2f, out var o);
            Assert.AreEqual(MarkEvents.None, events, "no toast, no sting again");
            Assert.AreEqual(0f, o.ToastAlpha);
            Assert.AreEqual(1f, o.Dim);

            var a = AssassinLocked();
            var keep = new List<MarkChicken>(a.Chickens);
            a.Chickens.Clear();
            a.Run(1f, out var gone);                              // even fully faded out
            AssertExactlyClean(gone, "faded while away");
            a.Chickens.AddRange(keep);
            events = a.Run(1f, out o);
            Assert.AreEqual(MarkEvents.None, events, "no draw ticks, no lock again");
            Assert.AreEqual(1f, o.MarkerAlpha, "straight back to the settled marker");
        }

        [Test]
        public void HostMigration_ANewMark_AnnouncesAgain()
        {
            var w = VictimMarked();
            w.ClearMark(Rival);
            w.Run(1f, out _);
            w.SetMark(Rival, Me, windowEnd: 5000);               // a new mark: a new window end
            Assert.AreNotEqual(MarkEvents.None, w.Run(0.5f, out _) & MarkEvents.Marked);
        }

        [Test]
        public void ANewMark_DuringTheEndFade_StartsClean()
        {
            var w = VictimMarked(cold: true);
            w.HunterInvisible = true;
            w.Run(1f, out _);
            w.ClearMark(Rival);
            w.Run(0.1f, out _);                                   // mid-fade
            w.HunterInvisible = false;
            w.HunterDistance = 20f;
            w.SetMark(Third, Me, windowEnd: 7000);                // a different mark arrives
            var o = w.Step();
            Assert.AreNotEqual(MarkEvents.None, o.Events & MarkEvents.Marked, "announced");
            Assert.Less(o.Dim, 0.1f, "its darkness starts from zero, not the old one's");
            Assert.AreEqual(0f, o.Cold, "nothing of the old cold");
            Assert.AreEqual(0f, o.Red);
            Assert.AreEqual(2, o.HunterCorner, "the new hunter");
        }
    }
}
