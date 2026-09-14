using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.Installers;
using CluckWars.Progression;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Zenject;
using Assert = NUnit.Framework.Assert;
using LogLevel = CluckWars.Logging.LogLevel;
using Kind = CluckWars.Tests.RecordingMatchEventSink.Kind;

namespace CluckWars.Tests
{
    /// <summary>
    /// Slice 1 — the announcement sites. The round lifecycle (<see cref="RoundEdgeDetector"/>,
    /// <see cref="RoundAnnouncer"/>) and ranking (<see cref="RoundStandingsBuilder"/>) are plain C#
    /// and tested behaviourally. The per-tick emit sites live in <c>NetworkBehaviour</c>s whose
    /// <c>[Networked]</c> properties throw on an unspawned object, and EditMode has no runner, so
    /// those are pinned at source level instead — one assertion per site.
    /// </summary>
    public sealed class MatchEventSinkTests
    {
        private const string GameplayDir  = SourceScan.ScriptsRoot + "/Gameplay";
        private const string AbilitiesDir = SourceScan.ScriptsRoot + "/Abilities";

        private const string ChickenCargoPath      = GameplayDir + "/ChickenCargo.cs";
        private const string ChickenMatchStatsPath = GameplayDir + "/ChickenMatchStats.cs";
        private const string AbilityControllerPath = GameplayDir + "/AbilityController.cs";
        private const string GameManagerPath       = GameplayDir + "/GameManager.cs";
        private const string ChickenControllerPath = GameplayDir + "/ChickenController.cs";

        // ==== RoundEdgeDetector =========================================================

        private static RoundEdge[] Observe(params MatchState[] states)
        {
            var detector = new RoundEdgeDetector();
            return states.Select(detector.Observe).ToArray();
        }

        [Test]
        public void Edges_FirstObservationActive_IsStarted()
        {
            CollectionAssert.AreEqual(new[] { RoundEdge.Started }, Observe(MatchState.Active),
                "A peer that first sees an Active round (late join, or solo's instant start) must still announce it.");
        }

        [TestCase(MatchState.WaitingForPlayers)]
        [TestCase(MatchState.Ended)]
        public void Edges_FirstObservationNotActive_IsNone(MatchState first)
        {
            CollectionAssert.AreEqual(new[] { RoundEdge.None }, Observe(first));
        }

        [Test]
        public void Edges_LateJoinDuringEnded_StartsOnlyWhenTheNextRoundDoes()
        {
            CollectionAssert.AreEqual(
                new[] { RoundEdge.None, RoundEdge.Started, RoundEdge.Ended },
                Observe(MatchState.Ended, MatchState.Active, MatchState.Ended),
                "Joining during the results screen must not produce an Ended for a round this peer never saw start.");
        }

        [Test]
        public void Edges_RestartCycle_YieldsStartEndStartEnd()
        {
            CollectionAssert.AreEqual(
                new[] { RoundEdge.None, RoundEdge.Started, RoundEdge.Ended, RoundEdge.Started, RoundEdge.Ended },
                Observe(MatchState.WaitingForPlayers, MatchState.Active, MatchState.Ended, MatchState.Active, MatchState.Ended),
                "RestartMatch moves Ended → Active; every round must get its own Started and Ended.");
        }

        [Test]
        public void Edges_RepeatedStates_FireExactlyOncePerTransition()
        {
            CollectionAssert.AreEqual(
                new[] { RoundEdge.Started, RoundEdge.None, RoundEdge.None, RoundEdge.Ended, RoundEdge.None, RoundEdge.None },
                Observe(MatchState.Active, MatchState.Active, MatchState.Active, MatchState.Ended, MatchState.Ended, MatchState.Ended));
        }

        [Test]
        public void Edges_AbandonedRound_EmitsNoEnded()
        {
            CollectionAssert.AreEqual(
                new[] { RoundEdge.Started, RoundEdge.None, RoundEdge.None, RoundEdge.Started },
                Observe(MatchState.Active, MatchState.WaitingForPlayers, MatchState.Ended, MatchState.Active),
                "Active → WaitingForPlayers abandons the round: no Ended then, and none for the later Ended either.");
        }

        [Test]
        public void Edges_Reset_RestoresTheFirstObservationRule()
        {
            var detector = new RoundEdgeDetector();
            Assert.AreEqual(RoundEdge.Started, detector.Observe(MatchState.Active));
            Assert.AreEqual(RoundEdge.None, detector.Observe(MatchState.Active));

            detector.Reset();
            Assert.AreEqual(RoundEdge.Started, detector.Observe(MatchState.Active),
                "After Reset the next observation is a first observation again.");

            detector.Reset();
            Assert.AreEqual(RoundEdge.None, detector.Observe(MatchState.Ended),
                "After Reset an Ended first observation must not produce an Ended edge.");
        }

        /// <summary>
        /// Every sequence of up to six symbols over {WaitingForPlayers, Active, Ended, Reset}
        /// (5,460 sequences), checked against an independent oracle and against the invariant that
        /// no Ended edge is ever returned without a Started edge opening that round.
        /// </summary>
        [Test]
        public void Edges_Exhaustive_MatchTheOracle_AndNeverEndAnUnstartedRound()
        {
            const int Symbols = 4; // 0..2 = MatchState, 3 = Reset
            int sequences = 0;

            for (int length = 1; length <= 6; length++)
            {
                int total = (int)Math.Pow(Symbols, length);
                for (int code = 0; code < total; code++)
                {
                    var detector = new RoundEdgeDetector();
                    var trace = new List<string>();
                    bool observed = false, roundOpen = false;
                    var previous = MatchState.WaitingForPlayers;

                    int c = code;
                    for (int step = 0; step < length; step++, c /= Symbols)
                    {
                        int symbol = c % Symbols;
                        if (symbol == 3)
                        {
                            detector.Reset();
                            observed = false;
                            roundOpen = false;
                            trace.Add("Reset");
                            continue;
                        }

                        var state = (MatchState)symbol;
                        var edge = detector.Observe(state);
                        trace.Add($"{state}->{edge}");

                        RoundEdge expected;
                        if (!observed) expected = state == MatchState.Active ? RoundEdge.Started : RoundEdge.None;
                        else if (state == previous) expected = RoundEdge.None;
                        else if (state == MatchState.Active) expected = RoundEdge.Started;
                        else if (previous == MatchState.Active && state == MatchState.Ended) expected = RoundEdge.Ended;
                        else expected = RoundEdge.None;

                        Assert.AreEqual(expected, edge, "Sequence: " + string.Join(", ", trace));

                        if (edge == RoundEdge.Ended)
                            Assert.IsTrue(roundOpen, "Ended without a preceding Started. Sequence: " + string.Join(", ", trace));

                        if (edge == RoundEdge.Started) roundOpen = true;
                        else if (state != MatchState.Active) roundOpen = false;

                        observed = true;
                        previous = state;
                    }
                    sequences++;
                }
            }

            Assert.AreEqual(5460, sequences, "The exhaustive sweep did not cover every sequence.");
        }

        // ==== RoundStandingsBuilder =====================================================

        private static RoundStandings Build(params (int actorId, string roleKey, float total)[] results) =>
            RoundStandingsBuilder.Build(results);

        private static int[] Actors(RoundStandings s) => s.Entries.Select(e => e.ActorId).ToArray();
        private static int[] Placements(RoundStandings s) => s.Entries.Select(e => e.Placement).ToArray();

        [Test]
        public void Standings_SortByTotalDescending_WithStrictPlacements()
        {
            var s = Build((1, "a", 5f), (2, "b", 20f), (3, "c", 10f));
            CollectionAssert.AreEqual(new[] { 2, 3, 1 }, Actors(s));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, Placements(s));
        }

        [Test]
        public void Standings_TiesShareTheBetterPlacement()
        {
            var s = Build((1, "a", 10f), (2, "b", 10f), (3, "c", 5f));
            CollectionAssert.AreEqual(new[] { 1, 1, 3 }, Placements(s),
                "Competition ranking: 10, 10, 5 places 1, 1, 3 — the next placement skips the shared one.");
        }

        [Test]
        public void Standings_AllTied_AllPlaceFirst()
        {
            var s = Build((4, "a", 7f), (2, "b", 7f), (9, "c", 7f), (1, "d", 7f));
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, Placements(s));
            CollectionAssert.AreEqual(new[] { 1, 2, 4, 9 }, Actors(s), "Equal totals order by actor id ascending.");
        }

        [Test]
        public void Standings_ApproximatelyEqualTotals_AreATie_LikeEndOnTimerExpiry()
        {
            float nearlyTen = 10.000001f;
            Assert.AreNotEqual(10f, nearlyTen, "Precondition: the two totals must differ bit-for-bit.");
            Assert.IsTrue(Mathf.Approximately(10f, nearlyTen), "Precondition: Mathf.Approximately treats them as equal.");

            var s = Build((1, "a", 10f), (2, "b", nearlyTen), (3, "c", 9f));
            CollectionAssert.AreEqual(new[] { 2, 1, 3 }, Actors(s), "Sort order still uses the exact totals.");
            CollectionAssert.AreEqual(new[] { 1, 1, 3 }, Placements(s),
                "GameManager.EndOnTimerExpiry treats Mathf.Approximately totals as a tie; standings must agree.");

            var clearlyApart = Build((1, "a", 10f), (2, "b", 10.1f));
            CollectionAssert.AreEqual(new[] { 1, 2 }, Placements(clearlyApart));
        }

        [Test]
        public void Standings_AreIndependentOfInputOrder()
        {
            var input = new[] { (7, "w", 12f), (3, "x", 30f), (5, "y", 12f), (1, "z", 0f) };
            var reference = Build(input);

            foreach (var permutation in Permutations(input))
            {
                var s = Build(permutation.ToArray());
                CollectionAssert.AreEqual(Actors(reference), Actors(s), "Order depends on input order.");
                CollectionAssert.AreEqual(Placements(reference), Placements(s), "Placements depend on input order.");
            }

            CollectionAssert.AreEqual(new[] { 3, 5, 7, 1 }, Actors(reference));
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 4 }, Placements(reference));
        }

        [Test]
        public void Standings_CarryActorRoleAndTotalThrough()
        {
            var s = Build((42, "class.fatty", 17.5f), (-3, "class.speedy", 2.25f));
            Assert.AreEqual(2, s.Entries.Length);

            var fatty = s.Entries[0];
            Assert.AreEqual(42, fatty.ActorId);
            Assert.AreEqual("class.fatty", fatty.RoleKey);
            Assert.AreEqual(17.5f, fatty.ResourceTotal);
            Assert.AreEqual(1, fatty.Placement);

            var speedy = s.Entries[1];
            Assert.AreEqual(-3, speedy.ActorId, "An actor id that wrapped negative (unchecked uint→int) must pass through.");
            Assert.AreEqual("class.speedy", speedy.RoleKey);
            Assert.AreEqual(2.25f, speedy.ResourceTotal);
            Assert.AreEqual(2, speedy.Placement);
        }

        [Test]
        public void Standings_EmptyInput_GivesAnEmptyArrayNeverNull()
        {
            var s = Build();
            Assert.IsNotNull(s);
            Assert.IsNotNull(s.Entries, "Entries must never be null — JsonUtility and the tracker both read it.");
            Assert.AreEqual(0, s.Entries.Length);
        }

        [Test]
        public void Standings_SingleEntry_PlacesFirst()
        {
            var s = Build((5, "class.warrior", 0f));
            Assert.AreEqual(1, s.Entries.Length);
            Assert.AreEqual(1, s.Entries[0].Placement);
        }

        [Test]
        public void Standings_NullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => RoundStandingsBuilder.Build(null));
        }

        [Test]
        public void Standings_OrphanedBases_KeepTheirRows_RankedByTotal_InADeterministicOrder()
        {
            // Two claimed bases whose players left mid-round: both rows carry MatchActorId.None and
            // a null role. They still rank by what they banked, so every remaining actor's placement
            // counts them — and the output order must not depend on the input order.
            var s = Build((MatchActorId.None, null, 8f), (12, "class.warrior", 20f),
                          (MatchActorId.None, null, 8f), (13, "class.fatty", 3f));

            CollectionAssert.AreEqual(new[] { 12, MatchActorId.None, MatchActorId.None, 13 }, Actors(s));
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 4 }, Placements(s),
                "Orphaned rows rank by total like any other; the two equal ones share P2.");
            Assert.IsNull(s.Entries[1].RoleKey);
            Assert.IsNull(s.Entries[2].RoleKey);

            var reordered = Build((13, "class.fatty", 3f), (MatchActorId.None, null, 8f),
                                  (MatchActorId.None, null, 8f), (12, "class.warrior", 20f));
            CollectionAssert.AreEqual(Actors(s), Actors(reordered));
            CollectionAssert.AreEqual(Placements(s), Placements(reordered));
        }

        private static IEnumerable<List<T>> Permutations<T>(IList<T> items)
        {
            if (items.Count <= 1) { yield return items.ToList(); yield break; }
            for (int i = 0; i < items.Count; i++)
            {
                var rest = items.Where((_, j) => j != i).ToList();
                foreach (var tail in Permutations(rest))
                {
                    tail.Insert(0, items[i]);
                    yield return tail;
                }
            }
        }

        // ==== RoundAnnouncer =============================================================

        private sealed class FakeSnapshotSource : IRoundSnapshotSource
        {
            public RoundRuleset Ruleset = new RoundRuleset { ResourceTargetToWin = 40f, RoundDurationSeconds = 45f, MaxActors = 4 };
            public RoundStandings Standings = new RoundStandings { Entries = new RoundStandingEntry[0] };
            public int LocalActorId = 77;
            public int RulesetCalls;
            public int SnapshotCalls;

            public RoundRuleset CaptureRuleset() { RulesetCalls++; return Ruleset; }

            public (RoundStandings standings, int localActorId) CaptureEndSnapshot()
            {
                SnapshotCalls++;
                return (Standings, LocalActorId);
            }
        }

        private static void Feed(RoundAnnouncer announcer, params (MatchState state, float timeRemaining)[] frames)
        {
            foreach (var (state, time) in frames) announcer.Tick(state, time);
        }

        [Test]
        public void Announcer_EmitsTheRulesetAtStart_AndAsksForItOnlyOnTheStartedEdge()
        {
            var sink = new RecordingMatchEventSink();
            var source = new FakeSnapshotSource();
            var announcer = new RoundAnnouncer(sink, source);

            Feed(announcer,
                (MatchState.WaitingForPlayers, 0f), (MatchState.WaitingForPlayers, 0f),
                (MatchState.Active, 45f), (MatchState.Active, 45f), (MatchState.Active, 44f));

            CollectionAssert.AreEqual(new[] { Kind.RoundStarted }, sink.Kinds);
            Assert.AreEqual(source.Ruleset, sink.Calls[0].Ruleset);
            Assert.AreEqual(1, source.RulesetCalls, "The ruleset provider must be called once, on the Started edge only.");
            Assert.AreEqual(0, source.SnapshotCalls, "The end snapshot must not be taken while the round is running.");
        }

        [Test]
        public void Announcer_Duration_IsRulesetDurationMinusTheLastActiveTimeRemaining()
        {
            var sink = new RecordingMatchEventSink();
            var announcer = new RoundAnnouncer(sink, new FakeSnapshotSource());

            // 45 held through the intro, then counting down; TimeRemaining reads 0 once Ended.
            Feed(announcer,
                (MatchState.Active, 45f), (MatchState.Active, 45f), (MatchState.Active, 30f),
                (MatchState.Active, 12.5f), (MatchState.Ended, 0f), (MatchState.Ended, 0f));

            var ended = sink.OfKind(Kind.RoundEnded).Single();
            Assert.AreEqual(45f - 12.5f, ended.DurationSeconds, 1e-4f,
                "Duration must come from the last TimeRemaining seen while Active, not the 0 read after Ended.");
        }

        [Test]
        public void Announcer_Duration_NeverNegative()
        {
            var sink = new RecordingMatchEventSink();
            var announcer = new RoundAnnouncer(sink, new FakeSnapshotSource());

            Feed(announcer, (MatchState.Active, 50f), (MatchState.Ended, 0f));

            Assert.AreEqual(0f, sink.OfKind(Kind.RoundEnded).Single().DurationSeconds);
        }

        [Test]
        public void Announcer_TakesTheEndSnapshotExactlyOnce_OnTheEdge_AndPassesItThrough()
        {
            var sink = new RecordingMatchEventSink();
            var source = new FakeSnapshotSource
            {
                Standings = new RoundStandings { Entries = new[] { new RoundStandingEntry { ActorId = 77, Placement = 1 } } },
                LocalActorId = 77,
            };
            var announcer = new RoundAnnouncer(sink, source);

            Feed(announcer, (MatchState.Active, 45f), (MatchState.Active, 20f));
            Assert.AreEqual(0, source.SnapshotCalls);

            Feed(announcer, (MatchState.Ended, 0f));
            Assert.AreEqual(1, source.SnapshotCalls, "The end snapshot must be taken on the Ended edge itself.");

            Feed(announcer, (MatchState.Ended, 0f), (MatchState.Ended, 0f), (MatchState.Ended, 0f));
            Assert.AreEqual(1, source.SnapshotCalls,
                "The end snapshot must be taken once: RestartMatch zeroes the bases later in Ended.");

            var ended = sink.OfKind(Kind.RoundEnded).Single();
            Assert.AreSame(source.Standings, ended.Standings);
            Assert.AreEqual(77, ended.LocalActorId);
        }

        [Test]
        public void Announcer_RestartPath_YieldsTwoCompleteRounds()
        {
            var sink = new RecordingMatchEventSink();
            var source = new FakeSnapshotSource();
            var announcer = new RoundAnnouncer(sink, source);

            Feed(announcer,
                (MatchState.Active, 45f), (MatchState.Active, 5f), (MatchState.Ended, 0f), (MatchState.Ended, 0f),
                (MatchState.Active, 45f), (MatchState.Active, 40f), (MatchState.Active, 33f), (MatchState.Ended, 0f));

            CollectionAssert.AreEqual(
                new[] { Kind.RoundStarted, Kind.RoundEnded, Kind.RoundStarted, Kind.RoundEnded }, sink.Kinds);
            Assert.AreEqual(2, source.RulesetCalls);
            Assert.AreEqual(2, source.SnapshotCalls);

            var durations = sink.OfKind(Kind.RoundEnded).Select(c => c.DurationSeconds).ToArray();
            Assert.AreEqual(40f, durations[0], 1e-4f);
            Assert.AreEqual(12f, durations[1], 1e-4f, "The second round's duration must not inherit the first's.");
        }

        [Test]
        public void Announcer_LateJoin_AnnouncesTheRunningRound()
        {
            var sink = new RecordingMatchEventSink();
            var announcer = new RoundAnnouncer(sink, new FakeSnapshotSource());

            Feed(announcer, (MatchState.Active, 20f), (MatchState.Active, 10f), (MatchState.Ended, 0f));

            CollectionAssert.AreEqual(new[] { Kind.RoundStarted, Kind.RoundEnded }, sink.Kinds);
            Assert.AreEqual(35f, sink.OfKind(Kind.RoundEnded).Single().DurationSeconds, 1e-4f,
                "A late joiner reports the round's elapsed time, not how long it watched.");
        }

        [Test]
        public void Announcer_EmitsNothing_OutsideARound()
        {
            var sink = new RecordingMatchEventSink();
            var source = new FakeSnapshotSource();
            var announcer = new RoundAnnouncer(sink, source);

            Feed(announcer,
                (MatchState.WaitingForPlayers, 0f), (MatchState.WaitingForPlayers, 0f),
                (MatchState.Ended, 0f), (MatchState.Ended, 0f), (MatchState.WaitingForPlayers, 0f));

            Assert.IsEmpty(sink.Calls);
            Assert.AreEqual(0, source.RulesetCalls);
            Assert.AreEqual(0, source.SnapshotCalls);
        }

        [Test]
        public void Announcer_RejectsNullCollaborators()
        {
            Assert.Throws<ArgumentNullException>(() => new RoundAnnouncer(null, new FakeSnapshotSource()));
            Assert.Throws<ArgumentNullException>(() => new RoundAnnouncer(new RecordingMatchEventSink(), null));
        }

        // ==== RoundSnapshotSelector =========================================================

        [Test]
        public void Selector_OneRowPerClaimedBase_InBaseOrder()
        {
            var (rows, _) = RoundSnapshotSelector.Select(
                new[] { (2, 10f), (0, 5f), (1, 0f) },
                new[]
                {
                    (0, false, false, 100, "class.speedy"),
                    (1, false, true,  101, "class.warrior"),
                    (2, false, false, 102, "class.fatty"),
                    (3, false, false, 103, "class.assassin"), // corner 3 has no claimed base: no row
                });

            CollectionAssert.AreEqual(
                new[] { (102, "class.fatty", 10f), (100, "class.speedy", 5f), (101, "class.warrior", 0f) }, rows,
                "One row per claimed base, in base order, carrying that corner's actor, role and total.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Selector_ADecoyAtItsCastersCorner_NeverOwnsTheBase_NorIsLocal(bool decoyListedFirst)
        {
            var real  = (corner: 1, isDecoy: false, isLocal: true, actorId: 201, roleKey: "class.assassin");
            var decoy = (corner: 1, isDecoy: true,  isLocal: true, actorId: 202, roleKey: "class.assassin");
            var chickens = decoyListedFirst ? new[] { decoy, real } : new[] { real, decoy };

            var (rows, local) = RoundSnapshotSelector.Select(new[] { (1, 7f) }, chickens);

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(201, rows[0].actorId,
                "A Doppelganger decoy stands at its caster's corner; it must never be picked as the base's actor.");
            Assert.AreEqual(201, local,
                "A decoy shares its caster's input authority; the local actor is isLocal && !isDecoy.");
        }

        [Test]
        public void Selector_LocalActor_IsTheLocalNonDecoyChicken()
        {
            var (_, local) = RoundSnapshotSelector.Select(new[] { (0, 0f), (1, 0f) }, new[]
            {
                (corner: 0, isDecoy: true,  isLocal: true,  actorId: 301, roleKey: "class.assassin"), // the human's decoy, listed first
                (corner: 1, isDecoy: false, isLocal: false, actorId: 302, roleKey: "class.speedy"),   // a bot
                (corner: 0, isDecoy: false, isLocal: true,  actorId: 303, roleKey: "class.assassin"), // the human
            });

            Assert.AreEqual(303, local);
        }

        [Test]
        public void Selector_NoLocalChicken_GivesNone_AndALocalDecoyAloneIsNotLocal()
        {
            var bases = new[] { (0, 0f) };

            Assert.AreEqual(MatchActorId.None, RoundSnapshotSelector.Select(bases, new[]
            {
                (corner: 0, isDecoy: false, isLocal: false, actorId: 401, roleKey: "class.fatty"),
            }).localActorId, "No chicken this peer has input authority over: the local actor is None.");

            Assert.AreEqual(MatchActorId.None, RoundSnapshotSelector.Select(bases, new[]
            {
                (corner: 0, isDecoy: true, isLocal: true, actorId: 402, roleKey: "class.fatty"),
            }).localActorId, "A decoy is never the local actor, even when it is the only locally-owned chicken left.");
        }

        [Test]
        public void Selector_OrphanedBase_KeepsARowWithNoActor()
        {
            var (rows, _) = RoundSnapshotSelector.Select(new[] { (0, 12f), (3, 4f) }, new[]
            {
                (corner: 0, isDecoy: false, isLocal: true,  actorId: 501, roleKey: "class.warrior"),
                (corner: 3, isDecoy: true,  isLocal: false, actorId: 502, roleKey: "class.assassin"), // only a decoy left at corner 3
            });

            Assert.AreEqual(2, rows.Count, "A claimed base keeps its row even when its player left.");
            Assert.AreEqual((501, "class.warrior", 12f), rows[0]);
            Assert.AreEqual(MatchActorId.None, rows[1].actorId, "An orphaned base carries MatchActorId.None, never the decoy.");
            Assert.IsNull(rows[1].roleKey);
            Assert.AreEqual(4f, rows[1].total, "The orphaned base's banked total still counts toward everyone's placement.");
        }

        // ==== GuardedMatchEventSink ======================================================

        /// <summary>An inner sink that throws on every verb, counting the calls it received.</summary>
        private sealed class ThrowingSink : IMatchEventSink
        {
            public int Calls;

            private void Fail()
            {
                Calls++;
                throw new InvalidOperationException("inner sink failure");
            }

            public void RoundStarted(RoundRuleset ruleset) => Fail();
            public void ResourceBanked(int actorId, float amount) => Fail();
            public void ResourceStolen(int actorId, int victimActorId, float amount) => Fail();
            public void OpponentDisabled(int actorId) => Fail();
            public void AbilityResolved(int actorId, string abilityKey, bool connected) => Fail();
            public void RoundEnded(RoundStandings standings, int localActorId, float durationSeconds) => Fail();
        }

        [Test]
        public void Guard_AThrowingInnerSink_NeverThrows_AndLogsExactlyOneErrorPerKind()
        {
            var inner = new ThrowingSink();
            var log = new RecordingLogService();
            var guard = new GuardedMatchEventSink(inner, log);

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 5; i++) guard.ResourceStolen(1, 2, 3f);
            }, "Nothing a sink throws may reach the gameplay code that announced the event.");

            Assert.AreEqual(5, inner.Calls, "The guard must still forward every call to the inner sink.");
            var errors = log.OfLevel(LogLevel.Error);
            Assert.AreEqual(1, errors.Count, "One Error for the first failure, then counting only — never an Error per steal.");
            StringAssert.Contains("ResourceStolen", errors[0].Message, "The Error must name the verb that failed.");
            Assert.IsInstanceOf<InvalidOperationException>(errors[0].Exception, "The Error must carry the exception.");
            Assert.AreEqual(5, guard.FailureCount(GuardedMatchEventSink.Verb.ResourceStolen));

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    guard.RoundStarted(default);
                    guard.ResourceBanked(1, 2f);
                    guard.OpponentDisabled(1);
                    guard.AbilityResolved(1, "ability.peck", true);
                    guard.RoundEnded(new RoundStandings { Entries = new RoundStandingEntry[0] }, 1, 45f);
                }
            });

            Assert.AreEqual(6, log.OfLevel(LogLevel.Error).Count, "Exactly one Error per event kind, six kinds in all.");
            foreach (GuardedMatchEventSink.Verb verb in Enum.GetValues(typeof(GuardedMatchEventSink.Verb)))
            {
                int expected = verb == GuardedMatchEventSink.Verb.ResourceStolen ? 5 : 3;
                Assert.AreEqual(expected, guard.FailureCount(verb), $"{verb} failures must be counted.");
            }
        }

        [Test]
        public void Guard_AHealthyInnerSink_ReceivesEveryCallUnchanged_AndNothingIsLogged()
        {
            var inner = new RecordingMatchEventSink();
            var log = new RecordingLogService();
            var guard = new GuardedMatchEventSink(inner, log);
            var ruleset = new RoundRuleset { ResourceTargetToWin = 40f, RoundDurationSeconds = 45f, MaxActors = 4 };
            var standings = new RoundStandings { Entries = new RoundStandingEntry[0] };

            guard.RoundStarted(ruleset);
            guard.ResourceBanked(7, 2.25f);
            guard.ResourceStolen(7, 8, 4f);
            guard.OpponentDisabled(7);
            guard.AbilityResolved(7, "ability.snatch", true);
            guard.RoundEnded(standings, 7, 44.5f);

            CollectionAssert.AreEqual(
                new[] { Kind.RoundStarted, Kind.ResourceBanked, Kind.ResourceStolen, Kind.OpponentDisabled, Kind.AbilityResolved, Kind.RoundEnded },
                inner.Kinds);
            Assert.AreEqual(ruleset, inner.Calls[0].Ruleset);
            var stolen = inner.OfKind(Kind.ResourceStolen).Single();
            Assert.AreEqual(7, stolen.ActorId);
            Assert.AreEqual(8, stolen.VictimActorId);
            Assert.AreEqual(4f, stolen.Amount);
            var resolved = inner.OfKind(Kind.AbilityResolved).Single();
            Assert.AreEqual("ability.snatch", resolved.AbilityKey);
            Assert.IsTrue(resolved.Connected);
            var ended = inner.OfKind(Kind.RoundEnded).Single();
            Assert.AreSame(standings, ended.Standings);
            Assert.AreEqual(44.5f, ended.DurationSeconds);
            Assert.IsEmpty(log.Entries, "A healthy sink produces no log lines.");
            Assert.AreSame(inner, guard.Inner);
        }

        [Test]
        public void Guard_RejectsNullCollaborators()
        {
            Assert.Throws<ArgumentNullException>(() => new GuardedMatchEventSink(null, new RecordingLogService()));
            Assert.Throws<ArgumentNullException>(() => new GuardedMatchEventSink(new NullMatchEventSink(), null));
        }

        // ==== MatchActorId ================================================================

        [Test]
        public void ActorId_None_IsZero_BecauseFusionNeverIssuesRawZero()
        {
            Assert.AreEqual(0, MatchActorId.None);
            Assert.AreEqual(0u, default(NetworkId).Raw);
            Assert.IsFalse(default(NetworkId).IsValid,
                "MatchActorId.None relies on NetworkId raw 0 being invalid. If Fusion changes that, pick a new sentinel.");
        }

        [Test]
        public void ActorId_OfNothing_IsNone()
        {
            Assert.AreEqual(MatchActorId.None, MatchActorId.Of((NetworkObject)null));
            Assert.AreEqual(MatchActorId.None, MatchActorId.Of((NetworkBehaviour)null));
        }

        // ==== Emit sites (source level) ====================================================

        [Test]
        public void EmitSite_FlushBaseDeposit_AnnouncesBanked_OnlyWhenSent_AfterTheFlushStateIsReset()
        {
            var body = SourceScan.MethodBody(ChickenCargoPath, "void FlushBaseDeposit(");
            const string Where = "FlushBaseDeposit";

            // The amount is captured before the reset, under a name the emit then uses.
            var capture = body.Select(l => Regex.Match(l.Code, @"^float\s+(\w+)\s*=\s*_pendingBaseFood\s*;$"))
                              .FirstOrDefault(m => m.Success);
            Assert.IsNotNull(capture, $"{Where} must capture 'float <name> = _pendingBaseFood;' before resetting the flush state.");
            string capturedName = capture.Groups[1].Value;
            int captured = body.FindIndex(l => Regex.IsMatch(l.Code, $@"^float\s+{capturedName}\s*=\s*_pendingBaseFood\s*;$"));

            int rpc     = SourceScan.IndexOf(body, "RPC_AddDeposit(", Where);
            int zeroed  = SourceScan.IndexOf(body, "_pendingBaseFood = 0f", Where);
            int cleared = SourceScan.IndexOf(body, "_activeBaseTarget = null", Where);
            int ticks   = SourceScan.IndexOf(body, "_baseDepositTicks = 0", Where);
            int banked  = SourceScan.IndexOf(body, "ResourceBanked(", Where);

            Assert.Less(captured, rpc, $"{Where} must capture the amount before sending it.");
            Assert.Greater(banked, zeroed,  $"{Where}: ResourceBanked must come after '_pendingBaseFood = 0f'.");
            Assert.Greater(banked, cleared, $"{Where}: ResourceBanked must come after '_activeBaseTarget = null'.");
            Assert.Greater(banked, ticks,   $"{Where}: ResourceBanked must come after '_baseDepositTicks = 0'. " +
                "The flush state is fully reset before any external call, so no listener can observe or re-enter a half-flushed batch.");

            StringAssert.IsMatch($@"ResourceBanked\(.*,\s*{capturedName}\s*\)", body[banked].Code,
                $"{Where} must announce the captured amount '{capturedName}', not the already-zeroed _pendingBaseFood.");

            // Only when the deposit was actually sent: a flag is raised in the RPC_AddDeposit block
            // and the emit is conditioned on it.
            var sentFlag = SourceScan.EnclosingBlock(body, rpc)
                .Select(l => Regex.Match(l.Code, @"^(\w+)\s*=\s*true\s*;$")).FirstOrDefault(m => m.Success);
            Assert.IsNotNull(sentFlag, $"{Where} must raise a 'sent' flag in the block that calls RPC_AddDeposit.");
            string guardAndEmit = (banked > 0 ? body[banked - 1].Code + " " : "") + body[banked].Code;
            StringAssert.IsMatch($@"\b{sentFlag.Groups[1].Value}\b", guardAndEmit,
                $"{Where} must announce ResourceBanked only when the flush was actually sent (conditioned on '{sentFlag.Groups[1].Value}').");
        }

        [Test]
        public void EmitSite_ReceiveStolen_AnnouncesStolen()
        {
            var body = SourceScan.MethodBody(ChickenCargoPath, "void ReceiveStolen(");
            Assert.IsTrue(body.Any(l => l.Code.Contains("ResourceStolen(")),
                "ChickenCargo.ReceiveStolen is the steal chokepoint and must announce ResourceStolen.");
        }

        [Test]
        public void EmitSite_RpcCreditKill_AnnouncesOpponentDisabled()
        {
            var body = SourceScan.MethodBody(ChickenMatchStatsPath, "void RPC_CreditKill(");
            int kills = SourceScan.IndexOf(body, "Kills++", "RPC_CreditKill");
            int disabled = SourceScan.IndexOf(body, "OpponentDisabled(", "RPC_CreditKill");
            Assert.Greater(disabled, kills, "OpponentDisabled must be announced after the kill is credited.");
        }

        [Test]
        public void EmitSite_TryActivate_AnnouncesAbilityResolved_BesideTheCastEvent()
        {
            var body = SourceScan.MethodBody(AbilityControllerPath, "void TryActivate(");
            int castEvent = SourceScan.IndexOf(body, "LastCastEventId++", "TryActivate");
            var block = SourceScan.EnclosingBlock(body, castEvent);

            var resolved = block.FirstOrDefault(l => l.Code.Contains("AbilityResolved("));
            Assert.IsNotNull(resolved,
                "AbilityController.TryActivate must announce AbilityResolved beside LastCastEventId++, so a " +
                "cast is announced exactly when every peer learns about it.");
            StringAssert.Contains("UnlockKey", resolved.Code, "abilityKey must be AbilityBaseSO.UnlockKey.");
            StringAssert.Contains("hitCount > 0", resolved.Code, "connected must be the cast-time hitCount > 0.");

            int resolvedAt = body.IndexOf(resolved);
            int onActivate = SourceScan.IndexOf(body, "ability.OnActivate(", "TryActivate");
            Assert.Greater(resolvedAt, castEvent,
                "AbilityResolved must be announced after LastCastEventId++, once the cast is committed.");
            Assert.Greater(resolvedAt, onActivate,
                "AbilityResolved must be announced after ability.OnActivate, so a steal cast's ResourceStolen " +
                "precedes its AbilityResolved and a cast that threw in OnActivate is never announced.");
        }

        [Test]
        public void EmitSite_GameManagerLateUpdate_TicksTheAnnouncer_OnEveryPeer_AndNeverUsesAChangeDetector()
        {
            var body = SourceScan.MethodBody(GameManagerPath, "void LateUpdate(");
            Assert.IsTrue(body.Any(l => l.Code.Contains("_roundAnnouncer.Tick(")),
                "GameManager.LateUpdate must poll State into the RoundAnnouncer.");
            Assert.IsFalse(body.Any(l => l.Code.Contains("HasStateAuthority")),
                "The round poll runs on every peer — each peer's progression hears its own round.");

            Assert.IsFalse(SourceScan.CodeLines(GameManagerPath).Any(l => l.Code.Contains("GetChangeDetector(")),
                "GameManager must not use a ChangeDetector. Solo runs GameMode.Single, where DetectChanges can " +
                "skip a locally-written property (PlayerBase.LateUpdate), and a missed Ended edge silently " +
                "loses a round's rewards — only in solo, i.e. the pitch build. Poll in LateUpdate instead.");
        }

        // ==== ReceiveStolen credits exactly what the raw write did =========================

        [Test]
        public void ReceiveStolen_CreditsFirst_Unconditionally()
        {
            var body = SourceScan.MethodBody(ChickenCargoPath, "void ReceiveStolen(");
            Assert.AreEqual("Cargo += amount;", body[0].Code,
                "ReceiveStolen's first statement must be the unconditional credit 'Cargo += amount;' — " +
                "exactly the raw write it replaced. No clamp, guard or return may precede it.");
            Assert.AreEqual(0, body[0].Depth, "The credit must sit at the method's top level, not inside a condition.");
            Assert.IsFalse(body.Any(l => Regex.IsMatch(l.Code, @"\breturn\b")),
                "ReceiveStolen must not return early; the credit is the whole of its gameplay effect.");
        }

        /// <summary>
        /// The five steal sites. Each must pass <c>ReceiveStolen</c> the same amount variable its
        /// adjacent <c>RPC_DrainStolen</c> uses, on the same side of it as the raw write was
        /// (credit-then-drain for the four abilities, drain-then-credit for Spine Coat), and must name
        /// as the victim exactly the chicken whose cargo is being drained. For Spine Coat the defender
        /// is the thief, so the victim is the attacker, <c>other</c>.
        /// </summary>
        private static readonly (string Path, bool CreditBeforeDrain, string Victim)[] StealSites =
        {
            (AbilitiesDir + "/SnatchAbilitySO.cs",       true,  "targetCtrl"),
            (AbilitiesDir + "/SneakyStealAbilitySO.cs",  true,  "_scratch[0]"),
            (AbilitiesDir + "/ScrapAbilitySO.cs",        true,  "_scratch[0]"),
            (AbilitiesDir + "/RollTrampleAbilitySO.cs",  true,  "_scratch[0]"),
            (ChickenControllerPath,                      false, "other"), // Spine Coat steal-back
        };

        /// <summary>Any call to ReceiveStolen, whatever its arguments — used to find callers.</summary>
        private static readonly Regex AnyCreditCall = new Regex(@"\bReceiveStolen\(");
        /// <summary>ReceiveStolen(amount, victim) with simple arguments: group 1 = amount, group 2 = victim.</summary>
        private static readonly Regex CreditCall = new Regex(@"\bReceiveStolen\(\s*(\w+)\s*,\s*([^()]+?)\s*\)");
        /// <summary>receiver.RPC_DrainStolen(amount, …): group 1 = receiver, group 2 = amount.</summary>
        private static readonly Regex DrainCall  = new Regex(@"([\w\.\[\]]+)\.RPC_DrainStolen\(\s*(\w+)\s*,");

        [Test]
        public void StealSites_CreditTheDrainedAmount_ToTheDrainedVictim_OnTheOriginalSideOfTheDrain()
        {
            foreach (var (path, creditBeforeDrain, victim) in StealSites)
            {
                var lines = SourceScan.CodeLines(path);
                var credits = lines.Select((l, i) => (l, i)).Where(x => AnyCreditCall.IsMatch(x.l.Code)).ToList();
                Assert.AreEqual(1, credits.Count, $"{path}: expected exactly one ReceiveStolen call.");

                int ci = credits[0].i;
                var credit = CreditCall.Match(lines[ci].Code);
                Assert.IsTrue(credit.Success,
                    $"{path}:{lines[ci].Number}: expected ReceiveStolen(amount, victim) with simple arguments, got: {lines[ci].Code}");

                int di = creditBeforeDrain ? ci + 1 : ci - 1;
                string side = creditBeforeDrain ? "immediately after" : "immediately before";
                Assert.IsTrue(di >= 0 && di < lines.Count && DrainCall.IsMatch(lines[di].Code),
                    $"{path}:{lines[ci].Number}: RPC_DrainStolen must be the code line {side} ReceiveStolen, " +
                    "preserving the original credit/drain order.");
                var drain = DrainCall.Match(lines[di].Code);

                Assert.AreEqual(drain.Groups[2].Value, credit.Groups[1].Value,
                    $"{path}:{lines[ci].Number}: the thief must be credited the same amount the victim is asked to drain.");

                string creditVictim = credit.Groups[2].Value;
                Assert.AreEqual(victim, creditVictim,
                    $"{path}:{lines[ci].Number}: ReceiveStolen must name '{victim}' as the victim, not '{creditVictim}'.");

                // The drained cargo must be the victim's: either '<victim>.Cargo' directly, or a local
                // assigned from '<victim>.Cargo' earlier in the file.
                string receiver = drain.Groups[1].Value;
                bool drainsTheVictim = receiver == victim + ".Cargo" ||
                    lines.Take(di).Any(l => Regex.IsMatch(l.Code,
                        $@"\b{Regex.Escape(receiver)}\s*=\s*{Regex.Escape(victim)}\.Cargo\s*;"));
                Assert.IsTrue(drainsTheVictim,
                    $"{path}:{lines[di].Number}: RPC_DrainStolen is called on '{receiver}', which is not '{victim}.Cargo' " +
                    $"nor a local assigned from it — the announced victim would not be the chicken that was robbed.");
            }
        }

        [Test]
        public void StealSites_AreExactlyTheFivePinnedOnes()
        {
            var callers = SourceScan.AllRuntimeScripts()
                .Where(p => !p.EndsWith("/ChickenCargo.cs"))
                .Where(p => SourceScan.CodeLines(p).Any(l => AnyCreditCall.IsMatch(l.Code)))
                .OrderBy(p => p)
                .ToArray();

            CollectionAssert.AreEquivalent(StealSites.Select(s => s.Path).ToArray(), callers,
                "A new ReceiveStolen caller must be added to StealSites here, with its credit/drain order pinned.");
        }

        // ==== The sink binding resolves ======================================================

        /// <summary>
        /// Proves <see cref="IMatchEventSink"/> actually <b>resolves</b> from the container the shipped
        /// <c>ProjectInstaller</c> builds — not merely that a Bind line exists. This is the first slice
        /// that injects the sink, and slice 0 left the binding untested.
        /// </summary>
        /// <remarks>
        /// The installer runs against a fresh <see cref="DiContainer"/>, so ProjectContext and the
        /// editor's state are untouched. One side effect needs undoing: <c>InstallBindings</c> runs
        /// <c>ProceduralAudioBank.FillMissing</c> on the real <c>AudioRegistry</c> asset, whose clip slots
        /// are all empty on disk. The generated clips are removed again in <c>finally</c>, so the asset
        /// is left exactly as loaded. (Play mode does the same fill on every run; the test must not
        /// leave its own behind.)
        /// </remarks>
        [Test]
        public void ProjectInstaller_BindsTheMatchEventSink_AndItResolves()
        {
            var prefab = TestAssets.Load<GameObject>(TestAssets.ProjectContextPrefabPath);
            var installer = prefab.GetComponentInChildren<ProjectInstaller>(true);
            Assert.IsNotNull(installer, $"{TestAssets.ProjectContextPrefabPath} has no ProjectInstaller.");

            var audioRegistry = TestAssets.PrivateField<ScriptableObject>(installer, "_audioRegistry");
            var restoreAudio = SnapshotEmptyClipSlots(audioRegistry);
            var containerProperty = typeof(MonoInstallerBase).GetProperty("Container",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(containerProperty, "Zenject's MonoInstallerBase.Container property was not found.");
            var previousContainer = containerProperty.GetValue(installer);

            try
            {
                var container = new DiContainer();
                container.Inject(installer);
                Assert.AreSame(container, containerProperty.GetValue(installer),
                    "Precondition: the installer must be installing into the test's container.");

                installer.InstallBindings();

                var sink = container.Resolve<IMatchEventSink>();
                var guard = sink as GuardedMatchEventSink;
                Assert.IsNotNull(guard,
                    $"ProjectInstaller must bind IMatchEventSink behind GuardedMatchEventSink (got {sink?.GetType().Name}). " +
                    "Every announcement is an inline call mid-effect, so nothing a sink throws may reach gameplay.");
                var tracker = guard.Inner as MatchTracker;
                Assert.IsNotNull(tracker,
                    $"Since slice 2 the guard must wrap the MatchTracker (got {guard.Inner?.GetType().Name}).");
                Assert.AreSame(sink, container.Resolve<IMatchEventSink>(),
                    "The sink must be a single instance: every consumer has to talk to the same tracker.");
                Assert.AreSame(tracker, container.Resolve<MatchTracker>(),
                    "The tracker inside the guard must be the one bound MatchTracker.");

                // The progression service: one instance, listening to that same tracker, and never
                // initialized here — a bare container runs no IInitializable, so the real journal under
                // persistentDataPath is not touched.
                var service = container.Resolve<IProgressionService>();
                Assert.IsInstanceOf<ProgressionService>(service);
                Assert.AreSame(service, container.Resolve<IProgressionService>(), "IProgressionService must be a single instance.");
                Assert.AreSame(service, container.Resolve<ProgressionService>(),
                    "BindInterfacesAndSelfTo must yield one instance for the interface and the concrete type.");
                Assert.IsInstanceOf<IInitializable>(service,
                    "The project kernel loads the journal through IInitializable; without it progression never becomes ready.");
                Assert.AreSame(tracker, TestAssets.PrivateField<MatchTracker>(service, "_tracker"),
                    "The service must listen to the tracker the sink feeds, or no round is ever recorded.");
                Assert.IsFalse(service.IsReady, "Resolving the service must not load the journal; only Initialize() does.");
                Assert.AreEqual(
                    System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.persistentDataPath, "progression", JournalStore.FileName)),
                    System.IO.Path.GetFullPath(((ProgressionService)service).JournalPath),
                    "The journal lives at persistentDataPath/progression/journal.jsonl.");
            }
            finally
            {
                containerProperty.SetValue(installer, previousContainer);
                restoreAudio();
            }
        }

        // ==== The Assassin execute: the sixth steal site ===================================

        private const string AssassinExecutePath = GameplayDir + "/AssassinExecute.cs";

        /// <summary>AnnounceExecuteSteal(amount, victim) with simple arguments.</summary>
        private static readonly Regex ExecuteAnnounceCall = new Regex(@"\bAnnounceExecuteSteal\(\s*(\w+)\s*,\s*(\w+)\s*\)");

        /// <summary>
        /// Maestro's ruling (2026-09-12): the execute counts as a steal of the cargo actually transferred,
        /// excluding <c>ExecuteBounty</c>. The transfer RPC runs on the <b>victim's</b> authority, so the
        /// announcement must come from <c>Press</c>, on the assassin's — with the victim's cargo read
        /// <b>before</b> the RPC, which in solo invokes locally and synchronously and zeroes it.
        /// </summary>
        [Test]
        public void StealSite_AssassinExecute_AnnouncesTheCapturedVictimCargo_BeforeTheTransfer_NeverTheBounty()
        {
            const string Where = "AssassinExecute.Press";
            var body = SourceScan.MethodBody(AssassinExecutePath, "bool Press(");
            int announce = SourceScan.IndexOf(body, "AnnounceExecuteSteal(", Where);
            int transfer = SourceScan.IndexOf(body, "RPC_TransferAllToBountyBag(", Where);

            Assert.Less(announce, transfer,
                $"{Where} must announce before RPC_TransferAllToBountyBag: in solo the RPC runs synchronously and zeroes the victim's cargo.");

            var call = ExecuteAnnounceCall.Match(body[announce].Code);
            Assert.IsTrue(call.Success, $"{Where}: expected AnnounceExecuteSteal(amount, victim) with simple arguments, got: {body[announce].Code}");
            string amount = call.Groups[1].Value;
            Assert.AreEqual("target", call.Groups[2].Value, $"{Where}: the announced victim must be the executed target.");
            StringAssert.DoesNotMatch("(?i)bounty", body[announce].Code, $"{Where}: ExecuteBounty is income, never a steal.");
            StringAssert.IsMatch(@"\b_cargo\.AnnounceExecuteSteal\(", body[announce].Code,
                $"{Where}: the assassin's own ChickenCargo announces, on the assassin's authority.");
            StringAssert.DoesNotContain("?.AnnounceExecuteSteal", body[announce].Code,
                $"{Where}: a missing ChickenCargo is a wiring Error (silent-failure rule), not a silent '?.' skip.");
            int missingCargo = body.FindIndex(announce, l => l.Code.Contains("_log?.Error(") && l.Code.Contains("ChickenCargo"));
            Assert.IsTrue(missingCargo > announce && missingCargo < transfer,
                $"{Where}: when the assassin has no ChickenCargo, an Error naming it must be logged before the transfer.");

            int capture = body.FindIndex(l => Regex.IsMatch(l.Code, $@"^float\s+{Regex.Escape(amount)}\s*=\s*target\.Cargo\.Cargo\s*;$"));
            Assert.GreaterOrEqual(capture, 0, $"{Where}: '{amount}' must be a local read from target.Cargo.Cargo (the victim's replicated cargo).");
            Assert.Less(capture, announce, $"{Where}: the victim's cargo must be read before it is announced.");
            for (int i = capture + 1; i <= transfer; i++)
            {
                StringAssert.DoesNotMatch($@"\b{Regex.Escape(amount)}\s*([-+*/]?=)(?!=)", body[i].Code,
                    $"{Where}: '{amount}' must not be reassigned between the read and the transfer.");
            }
            Assert.IsFalse(body.Any(l => Regex.IsMatch(l.Code, $@"\b{Regex.Escape(amount)}\b") && Regex.IsMatch(l.Code, @"(?i)bounty")),
                $"{Where}: the bounty must never flow into the announced amount.");
        }

        [Test]
        public void AnnounceExecuteSteal_AnnouncesOnlyAPositiveAmount_OnForwardTicks_AndWritesNoCargo()
        {
            const string Where = "ChickenCargo.AnnounceExecuteSteal";
            var body = SourceScan.MethodBody(ChickenCargoPath, "void AnnounceExecuteSteal(");
            Assert.IsTrue(body.Any(l => l.Code.Contains("ResourceStolen(")), $"{Where} must announce ResourceStolen.");
            Assert.IsTrue(body.Any(l => l.Code.Contains("Runner.IsForward")), $"{Where} must be guarded by Runner.IsForward, like ReceiveStolen.");
            Assert.IsTrue(body.Any(l => Regex.IsMatch(l.Code, @"\bamount\s*>\s*0f")),
                $"{Where}: an execute on an empty-handed rival robs nothing and must not be announced.");
            Assert.IsFalse(body.Any(l => Regex.IsMatch(l.Code, @"\b(Cargo|BountyBag)\s*[-+]?=(?!=)")),
                $"{Where} is announce-only: the transfer is RPC_TransferAllToBountyBag on the victim's authority.");
        }

        [Test]
        public void AnnounceExecuteSteal_IsCalledOnlyByTheExecute()
        {
            var callers = SourceScan.AllRuntimeScripts()
                .Where(p => !p.EndsWith("/ChickenCargo.cs"))
                .Where(p => SourceScan.CodeLines(p).Any(l => l.Code.Contains("AnnounceExecuteSteal(")))
                .ToArray();
            CollectionAssert.AreEquivalent(new[] { AssassinExecutePath }, callers,
                "AnnounceExecuteSteal credits a steal without moving cargo; only the Assassin execute may call it.");
        }

        [TestCase(typeof(ChickenCargo))]
        [TestCase(typeof(ChickenMatchStats))]
        [TestCase(typeof(AbilityController))]
        [TestCase(typeof(GameManager))]
        public void Consumer_HasAnInjectMethodTakingTheSink(Type consumer)
        {
            bool takesSink = consumer
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.GetCustomAttributes(typeof(InjectAttribute), true).Length > 0)
                .Any(m => m.GetParameters().Any(p => p.ParameterType == typeof(IMatchEventSink)));

            Assert.IsTrue(takesSink,
                $"{consumer.Name} announces match events, so one of its [Inject] methods must take " +
                $"{nameof(IMatchEventSink)}. Without it the sink stays null and every announcement is lost silently.");
        }

        /// <summary>
        /// Records which <see cref="AudioClip"/> slots on <paramref name="registry"/> are empty, and
        /// returns an action that empties them again, destroying whatever was generated into them.
        /// </summary>
        internal static Action SnapshotEmptyClipSlots(ScriptableObject registry)
        {
            Assert.IsNotNull(registry, "ProjectInstaller._audioRegistry is empty on ProjectContext.prefab.");

            var emptyFields = registry.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.FieldType == typeof(AudioClip) && (AudioClip)f.GetValue(registry) == null)
                .ToList();

            return () =>
            {
                foreach (var field in emptyFields)
                {
                    var generated = (AudioClip)field.GetValue(registry);
                    if (generated == null) continue;
                    field.SetValue(registry, null);
                    UnityEngine.Object.DestroyImmediate(generated);
                }
            };
        }
    }
}
