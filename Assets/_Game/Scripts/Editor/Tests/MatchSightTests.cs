using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Balance;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Hide through the game's bot sight rule: seat 0 watches, seat 1 hides. Every position a policy is shown for a hidden rival must
    /// be the one <see cref="BotSightRules"/> keeps, never the rival's real one. Synthetic map and kits; every number is the test's own.
    /// </summary>
    public sealed class MatchSightTests
    {
        private const float Half = 40f;
        private const float Tick = 1f / 30f;
        private const float HideAt = 0.3f;
        private const float Arrival = 1f;

        [TearDown]
        public void TearDown() => MatchTestKit.Release();

        private struct Look
        {
            public float Time;
            public bool Hidden, Seen, Known;
            public Vector2 Pos;
            public Vector2 ObserverPos;
        }

        private sealed class Run
        {
            public readonly List<Look> Looks = new List<Look>();
            public readonly Dictionary<int, Vector2> HiderAt = new Dictionary<int, Vector2>();
            public float CastTime = -1f;
            public OracleMapSO Map;
            public MatchResult Result;

            public Vector2 TrueHiderPos(float time) => HiderAt[Mathf.RoundToInt(time / Tick)];
        }

        private static int Key(float time) => Mathf.RoundToInt(time / Tick);

        /// <summary>The hider stands or walks west; it casts Hide at <paramref name="castAt"/>. The observer stands or chases what it believes.</summary>
        private static Run Scenario(float hideSeconds, bool hiderWalks, bool observerChases, float clock, float castAt = HideAt)
        {
            var run = new Run { Map = MatchTestKit.OpenMap(Half) };
            var observer = MatchTestKit.Policy((v, s) =>
            {
                var h = v.Seats[1];
                run.Looks.Add(new Look { Time = v.Time, Hidden = h.Hidden, Seen = h.Seen, Known = h.Known, Pos = h.Pos, ObserverPos = v.Seats[s].Pos });
                if (observerChases && h.Known) return new MatchIntent { Goal = h.Pos, PeckPile = -1, CastSlot = -1, CastTarget = -1 };
                return MatchTestKit.StandAt(v, s);
            });
            bool cast = false;
            var hider = MatchTestKit.Policy((v, s) =>
            {
                run.HiderAt[Key(v.Time)] = v.Seats[s].Pos;
                var intent = hiderWalks
                    ? new MatchIntent { Goal = new Vector2(-30f, v.Seats[s].Pos.y), PeckPile = -1, CastSlot = -1, CastTarget = -1 }
                    : MatchTestKit.StandAt(v, s);
                if (!cast && v.Time + 1e-4f >= castAt) { cast = true; run.CastTime = v.Time; intent.CastSlot = 0; }
                return intent;
            });
            var watcher = MatchTestKit.Fighter("observer", observerChases ? 20f : 5f, new MatchAbility[0]);
            var hiding = MatchTestKit.Fighter("hider", 5f, new[] { MatchTestKit.Hide(0.2f, hideSeconds, 99f) });
            var setup = MatchTestKit.Duel(run.Map, watcher, hiding, observer, hider, Half, clock);
            setup.Rules.SightArrivalRadius = Arrival;
            run.Result = MatchSim.Run(setup, 11);
            return run;
        }

        [Test]
        public void HiddenRival_IsSeenAtItsLastSeenPosition()
        {
            var run = Scenario(hideSeconds: 10f, hiderWalks: true, observerChases: false, clock: 2f);
            Vector2 spotWhenItHid = run.TrueHiderPos(run.CastTime);

            var hidden = run.Looks.Where(l => l.Hidden).ToList();
            Assert.IsNotEmpty(hidden, "the observer never saw the rival fade");
            foreach (var l in hidden)
            {
                Assert.IsTrue(l.Known, "the observer still believes in the rival");
                Assert.IsFalse(l.Seen);
                Assert.AreEqual(0f, Vector2.Distance(l.Pos, spotWhenItHid), 1e-3f, "it is shown where it was last seen, not where it is");
            }
            Assert.Greater(Vector2.Distance(run.TrueHiderPos(hidden.Last().Time), spotWhenItHid), 1f, "the rival really walked away, so the test can tell");
        }

        [Test]
        public void HiddenRival_IsDropped_WhenTheObserverReachesTheSpot()
        {
            var run = Scenario(hideSeconds: 10f, hiderWalks: false, observerChases: true, clock: 4f);
            Vector2 spot = run.TrueHiderPos(run.CastTime);   // it stands still, so the remembered spot is where it stands

            int drop = run.Looks.FindIndex(l => l.Hidden && !l.Known);
            Assert.Greater(drop, 0, "the observer never gave the rival up");
            Assert.LessOrEqual(Vector2.Distance(run.Looks[drop].ObserverPos, spot), Arrival + 1e-3f, "it gave up on arriving");
            var before = run.Looks[drop - 1];
            Assert.IsTrue(before.Known);
            Assert.Greater(Vector2.Distance(before.ObserverPos, spot), Arrival, "and not before");
        }

        [Test]
        public void HiddenRival_IsDropped_AfterGiveUpSeconds()
        {
            var run = Scenario(hideSeconds: 10f, hiderWalks: false, observerChases: false, clock: 6f);
            float since = run.Looks.First(l => l.Hidden).Time;
            var dropped = run.Looks.First(l => l.Hidden && !l.Known);
            Assert.AreEqual(since + BotSightRules.GiveUpSeconds, dropped.Time, 2f * Tick + 1e-3f);
        }

        [Test]
        public void HideEnds_RivalIsSeenAtItsTruePosition()
        {
            const float hideSeconds = 1f;
            var run = Scenario(hideSeconds, hiderWalks: true, observerChases: false, clock: 2.5f);
            var after = run.Looks.Where(l => l.Time >= HideAt + hideSeconds + 0.15f).ToList();
            Assert.IsNotEmpty(after);
            foreach (var l in after)
            {
                Assert.IsTrue(l.Seen && l.Known && !l.Hidden);
                Assert.AreEqual(0f, Vector2.Distance(l.Pos, run.TrueHiderPos(l.Time)), 1e-3f, "visible again: shown where it really is");
            }
        }

        [Test]
        public void FirstLook_HappensOnTickZero_SoAnEarlyHideIsRemembered()
        {
            var run = Scenario(hideSeconds: 10f, hiderWalks: false, observerChases: false, clock: 1f, castAt: 0f);
            Assert.AreEqual(0f, run.CastTime, 1e-6f, "the hider casts on its first think");

            var first = run.Looks[0];
            Assert.IsTrue(first.Seen && first.Known, "tick 0: every observer has looked at every rival before any cast resolves");
            var second = run.Looks[1];
            Assert.IsTrue(second.Hidden && second.Known, "a rival that hid at once is remembered, not dropped for never having been seen");
            Assert.AreEqual(0f, Vector2.Distance(second.Pos, MatchSim.Spawn(run.Map, 1)), 1e-3f, "at the spot it stood on tick 0");
        }

        [Test]
        public void Policy_NeverReceivesAHiddenRivalsTruePosition()
        {
            var run = Scenario(hideSeconds: 10f, hiderWalks: true, observerChases: false, clock: 3f);
            var hidden = run.Looks.Where(l => l.Hidden).ToList();
            Assert.IsNotEmpty(hidden);
            Vector2 remembered = hidden[0].Pos;
            foreach (var l in hidden)
            {
                Assert.AreEqual(0f, Vector2.Distance(l.Pos, remembered), 1e-3f, "the rule's position: frozen while the rival is hidden");
                if (l.Time >= HideAt + 0.4f)
                    Assert.Greater(Vector2.Distance(l.Pos, run.TrueHiderPos(l.Time)), 0.5f, "never the true position of a rival that moved");
            }
        }
    }
}
