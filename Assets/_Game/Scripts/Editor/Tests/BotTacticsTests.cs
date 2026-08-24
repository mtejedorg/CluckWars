using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Abilities;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// Bot judgement, exercised without a <c>NetworkRunner</c>, a scene, or Play Mode.
    /// Everything <c>BotController</c> decides routes through <see cref="BotTactics"/>
    /// precisely so it can be pinned here — the old bot's decisions lived inside
    /// <c>FixedUpdateNetwork</c> and were only observable by watching a match.
    /// </summary>
    public sealed class BotTacticsTests
    {
        private static readonly ChickenClass[] AllClasses =
        {
            ChickenClass.Warrior, ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin,
        };

        // ---- Strategy coverage ----------------------------------------------

        /// <summary>
        /// Each class runs a different strategy. If two ever collapse onto one, the classes
        /// stop feeling different to play against, which is the entire point of this layer.
        /// </summary>
        [Test]
        public void EveryClass_RunsItsOwnStrategy()
        {
            var seen = new Dictionary<BotStrategy, ChickenClass>();
            foreach (var cls in AllClasses)
            {
                var strategy = BotTactics.ProfileFor(cls).Strategy;
                Assert.IsFalse(seen.TryGetValue(strategy, out var clash),
                    $"{cls} and {clash} both run {strategy}. Four classes sharing three strategies " +
                    "means at least two bots play identically.");
                seen[strategy] = cls;
            }
        }

        /// <summary>
        /// PROVEN GAP, closed here. The Fatty shipped with <c>huntRadius = 0</c>,
        /// <c>engageUnloaded = false</c> and no third path to a cast, so it could never
        /// enter the one state that fired offensive abilities. Belly Flop, Ground Quake and
        /// Roll Push were authored, registered, balance-tuned, class-gated to Fatty — and
        /// unreachable by any Fatty bot for the whole match. Nothing failed; the abilities
        /// simply never happened.
        /// </summary>
        private static readonly BotRole[] OffensiveRoles =
            { BotRole.Control, BotRole.Offense, BotRole.Steal };

        /// <summary>
        /// The three situations every profile reaches unconditionally — none of them read
        /// <see cref="BotProfile.HuntRadius"/>/<see cref="BotProfile.EngageRadius"/>/
        /// <see cref="BotProfile.GuardRadius"/>/<see cref="BotProfile.DangerRadius"/> to
        /// decide reachability. <see cref="EveryClass_CanReachAnOffensiveCast_OrItsControlKitIsDeadWeight"/>
        /// excludes them for exactly that reason — see its remarks.
        /// </summary>
        private static readonly BotSituation[] UnconditionalSituations =
            { BotSituation.Banking, BotSituation.Transiting, BotSituation.Contesting };

        /// <summary>
        /// FOUND VACUOUS, closed here (2026-08-24 review pass). This test's own doc comment
        /// says it closes the Fatty bug — but it silently could not: <c>ReachableSituations</c>
        /// puts <see cref="BotSituation.Contesting"/> in the list UNCONDITIONALLY, and
        /// <c>CastPlan</c>'s <c>Contesting</c> case returns <c>ContestPlan</c> — which contains
        /// <see cref="BotRole.Control"/> — for every strategy with no gate at all. So
        /// <c>Assert.IsNotEmpty(reached)</c> was true by construction for any class, any
        /// profile, forever. Zero out Hunt/Engage/Guard on any class (the literal shape of the
        /// original bug) and this still passed.
        ///
        /// Restricting the scan to <see cref="BotSituation.Retreating"/>/<c>Engaging</c>/
        /// <c>Guarding</c> — the situations actually gated by the profile's own fields — closes
        /// the hole: verified by hand against all four current profiles that each still passes
        /// through at least one of these three (Warrior and Fatty through more than one), so
        /// this is not a looser bar dressed up as a fix.
        /// </summary>
        [Test]
        public void EveryClass_CanReachAnOffensiveCast_OrItsControlKitIsDeadWeight()
        {
            foreach (var cls in AllClasses)
            {
                var profile = BotTactics.ProfileFor(cls);
                var reached = new List<string>();

                foreach (var sit in BotTactics.ReachableSituations(profile))
                {
                    if (Array.IndexOf(UnconditionalSituations, sit) >= 0) continue;
                    foreach (bool loaded in new[] { true, false })
                        foreach (var role in BotTactics.CastPlan(profile.Strategy, sit, loaded))
                            if (Array.IndexOf(OffensiveRoles, role) >= 0) reached.Add($"{sit}:{role}");
                }

                Assert.IsNotEmpty(reached,
                    $"{cls} ({profile.Strategy}): none of its PROFILE-GATED situations " +
                    "(Retreating/Engaging/Guarding — excluding Banking/Transiting/Contesting, " +
                    "which every class reaches regardless of tuning) produce a Control, Offense " +
                    "or Steal cast. Every offensive ability the class can equip is dead weight on " +
                    "the bot — the exact shape of the Fatty bug, where Belly Flop, Ground Quake " +
                    "and Roll Push were authored, registered, balance-tuned and unreachable.");
            }
        }

        /// <summary>
        /// Companion to the test above: proves <c>Contesting</c> really is an unconditional
        /// safety net (so excluding it above is deliberate, not an oversight) and that no
        /// strategy has silently lost it.
        /// </summary>
        [Test]
        public void Contesting_AlwaysCarriesAnOffensiveRole_RegardlessOfStrategy()
        {
            foreach (BotStrategy strategy in Enum.GetValues(typeof(BotStrategy)))
            {
                var plan = BotTactics.CastPlan(strategy, BotSituation.Contesting, targetIsLoaded: false);
                Assert.IsTrue(Array.Exists(plan, r => Array.IndexOf(OffensiveRoles, r) >= 0),
                    $"{strategy}'s Contesting plan lost its offensive role — that is the safety " +
                    "net the profile-gated test above relies on being unconditional.");
            }
        }

        /// <summary>
        /// Direct regression pin for the two abilities found dead-weight in the 2026-08-24
        /// review, distinct from the structural check above: that test proves SOME offensive
        /// role is reachable, not that THESE SPECIFIC roles are. Ruffle (Warrior's only
        /// <see cref="BotRole.Escape"/> ability) and Immovable (Fatty's only
        /// <see cref="BotRole.Defense"/> ability, whose own doc comment demands it be
        /// reachable BEFORE contact, not only as a Retreat reflex) both had zero coverage —
        /// reverting either fix would leave every existing test green.
        /// </summary>
        [Test]
        public void RuffleAndImmovable_AreReachableByTheRoleTheirClassNeeds()
        {
            Assert.Contains(BotRole.Escape,
                BotTactics.CastPlan(BotStrategy.Brawler, BotSituation.Retreating, targetIsLoaded: false),
                "Ruffle (Warrior, BotRole.Escape) has no reachable CastPlan branch — dead weight, " +
                "the same shape as the original Fatty bug.");

            Assert.Contains(BotRole.Defense,
                BotTactics.CastPlan(BotStrategy.Bunker, BotSituation.Guarding, targetIsLoaded: false),
                "Immovable (Fatty, BotRole.Defense) is not reachable from Guarding, the one " +
                "genuinely anticipatory situation — it would only ever fire as a Retreat " +
                "reflex, backwards from its own design intent (\"spent in anticipation, not as " +
                "a reflex\").");
        }

        /// <summary>
        /// The two classes that hold position must still reach their offensive kit, and the
        /// route matters: the Fatty's is the guard overlay, the Speedy's is retreat and
        /// contest. A tuning pass that zeroes GuardRadius "because the Fatty does not hunt"
        /// would silently restore the original bug, so the route is asserted, not just the
        /// outcome.
        /// </summary>
        [Test]
        public void TheNonChasingClasses_StillReachTheirKit_AndByTheExpectedRoute()
        {
            var fatty = BotTactics.ProfileFor(ChickenClass.Fatty);
            Assert.IsFalse(fatty.Chases, "The Fatty is defined by not chasing; if that changed, revisit this.");
            Assert.IsTrue(fatty.Guards,
                "The Fatty reaches Belly Flop / Ground Quake / Roll Push ONLY through the guard overlay. " +
                "With GuardRadius at 0 it has no offensive path at all.");

            // The Speedy owns Feather Trap and Feather Aura, both BotRole.Control. It never
            // chases and never guards, so retreat and contest are its only routes to them.
            var speedy = BotTactics.ProfileFor(ChickenClass.Speedy);
            Assert.IsFalse(speedy.Chases, "The Runner refuses fights by design.");
            Assert.Greater(speedy.DangerRadius, 0f,
                "A zero danger radius would cut off Retreating, one of the Speedy's only two routes to " +
                "the Control abilities it can equip.");

            // Contesting deliberately excluded here: it is BotSituation-unconditional and
            // ContestPlan carries Control for every strategy (see
            // Contesting_AlwaysCarriesAnOffensiveRole_RegardlessOfStrategy), so checking it
            // would prove nothing Speedy-specific — Retreating is the route that actually
            // depends on the Runner's own tuning (DangerRadius > 0, checked above).
            Assert.GreaterOrEqual(
                Array.IndexOf(BotTactics.CastPlan(speedy.Strategy, BotSituation.Retreating, false), BotRole.Control), 0,
                "The Runner's Retreating plan dropped Control, which is how a Speedy bot uses Feather " +
                "Trap and Feather Aura at all.");
        }

        // ---- Reach ------------------------------------------------------------

        /// <summary>
        /// Reach comes from the aim shape, per <see cref="AbilityBaseSO.AimForwardOffset"/>'s
        /// documented contract: offset + radius for the shapes that project forward, radius
        /// alone for the caster-centred ones.
        /// </summary>
        [Test]
        public void EffectiveReach_MatchesTheShapesOwnGeometryContract()
        {
            Assert.AreEqual(4.5f, BotTactics.EffectiveReach(AbilityAimShape.SelfCircle, 4.5f, 99f), 0.001f,
                "A caster-centred circle reaches its radius; the forward offset is meaningless to it.");
            Assert.AreEqual(3.6f, BotTactics.EffectiveReach(AbilityAimShape.Cone, 3.6f, 99f), 0.001f,
                "A cone reaches its radius.");
            Assert.AreEqual(8.0f, BotTactics.EffectiveReach(AbilityAimShape.SingleTarget, 8.0f, 0f), 0.001f,
                "A single-target pick reaches its radius.");
            Assert.AreEqual(9.1f, BotTactics.EffectiveReach(AbilityAimShape.Capsule, 1.9f, 7.2f), 0.001f,
                "A capsule's far cap sits at offset + radius — Roll Push's real 9.1 m lane.");
            Assert.AreEqual(8.1f, BotTactics.EffectiveReach(AbilityAimShape.ForwardCircle, 3.6f, 4.5f), 0.001f,
                "A forward circle's far edge sits at offset + radius — Feather Trap's real 8.1 m.");
        }

        /// <summary>
        /// A self-buff must not read as "out of reach". Returning 0 here would be
        /// indistinguishable from an ability that cannot reach anything, and every Speed
        /// Burst, Turtle Mode, Egg Shell and Immovable in the game would become uncastable.
        /// </summary>
        [Test]
        public void ASelfBuff_IsAlwaysInReach_NotNeverInReach()
        {
            float reach = BotTactics.EffectiveReach(AbilityAimShape.None, 0f, 0f);
            Assert.IsTrue(float.IsPositiveInfinity(reach),
                $"AimShape.None returned {reach}. A finite value (especially 0) makes every self-buff " +
                "in the roster fail the bot's reach gate and never fire.");
        }

        /// <summary>
        /// The flat 4 m the bot used before this was wrong in BOTH directions across the
        /// shipped roster, which is why it whiffed short abilities and never used long ones.
        /// This walks the real assets rather than restating numbers that will drift.
        /// </summary>
        [Test]
        public void TheOldFlatFourMetreRange_WasWrongForMostOfTheShippedRoster()
        {
            const float oldFlatRange = 4.0f;
            var all = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir);
            Assert.IsNotEmpty(all, "No ability assets found — the fixture is broken, not the bot.");

            int tooShort = 0, tooLong = 0, targeted = 0;
            foreach (var a in all)
            {
                if (a.AimShape == AbilityAimShape.None) continue;
                float reach = BotTactics.EffectiveReach(a);
                if (reach <= 0f) continue;
                targeted++;
                if (reach < oldFlatRange - 0.25f) tooShort++;
                if (reach > oldFlatRange + 0.25f) tooLong++;
            }

            Assert.Greater(targeted, 0, "No targeted abilities in the roster — fixture problem.");
            Assert.Greater(tooShort + tooLong, targeted / 2,
                $"Only {tooShort + tooLong} of {targeted} targeted abilities differ meaningfully from the " +
                "retired flat 4 m gate. If the roster has genuinely converged on 4 m, per-ability reach " +
                "buys nothing and this test should be retired deliberately rather than left passing.");
        }

        // ---- Aim ---------------------------------------------------------------

        /// <summary>
        /// Facing is irrelevant to a rotation-invariant shape, and gating a cast on it would
        /// make the bot stand there turning before every Cluck Shock for no reason.
        /// </summary>
        [Test]
        public void RadialShapes_NeverBlockACastOnFacing()
        {
            foreach (var shape in new[] { AbilityAimShape.None, AbilityAimShape.SelfCircle,
                                          AbilityAimShape.Aura, AbilityAimShape.SingleTarget })
            {
                Assert.AreEqual(180f, BotTactics.AimToleranceDeg(shape, 90f, 4f, 4f), 0.001f,
                    $"{shape} is rotation-invariant (see AbilityBaseSO.IsDirectionalAim), so its aim " +
                    "tolerance must admit every heading.");
            }
        }

        /// <summary>
        /// A cone's tolerance is its own half-arc with a margin, so a 120 degree Wing Slam
        /// tolerates roughly 52 degrees of error and a 140 degree Snatch roughly 62.
        /// </summary>
        [Test]
        public void ACones_Tolerance_IsItsOwnHalfArcMinusAMargin()
        {
            Assert.AreEqual(52f, BotTactics.AimToleranceDeg(AbilityAimShape.Cone, 120f, 4.5f, 0f), 0.5f);
            Assert.AreEqual(62f, BotTactics.AimToleranceDeg(AbilityAimShape.Cone, 140f, 4.3f, 0f), 0.5f);

            Assert.Less(BotTactics.AimToleranceDeg(AbilityAimShape.Cone, 60f, 4f, 0f),
                        BotTactics.AimToleranceDeg(AbilityAimShape.Cone, 140f, 4f, 0f),
                        "A narrower cone must demand tighter aim.");
        }

        /// <summary>
        /// The longer a forward-projected shape, the less angular error it forgives — at
        /// 9 m a 20 degree miss is a 3 m miss. A tolerance that ignored length would have
        /// the bot confidently firing Roll Push past everyone.
        /// </summary>
        [Test]
        public void ALongerLane_ForgivesLessAngularError()
        {
            float shortLane = BotTactics.AimToleranceDeg(AbilityAimShape.Capsule, 360f, 2.05f, 3.25f);
            float longLane  = BotTactics.AimToleranceDeg(AbilityAimShape.Capsule, 360f, 1.90f, 7.20f);

            Assert.Less(longLane, shortLane,
                $"Roll Push's 9.1 m lane tolerates {longLane:0.0} degrees and Roll Trample's 5.3 m lane " +
                $"{shortLane:0.0}. The long one must be the stricter of the two.");

            // Sanity: a target dead ahead is inside the lane; one at right angles is not.
            Assert.LessOrEqual(BotTactics.FacingErrorDeg(Vector3.forward, Vector3.forward), longLane);
            Assert.Greater(BotTactics.FacingErrorDeg(Vector3.forward, Vector3.right), longLane);
        }

        [Test]
        public void FacingError_IsPlanar_AndIgnoresHeight()
        {
            float flat  = BotTactics.FacingErrorDeg(Vector3.forward, new Vector3(1f, 0f, 1f));
            float raised = BotTactics.FacingErrorDeg(Vector3.forward, new Vector3(1f, 9f, 1f));
            Assert.AreEqual(flat, raised, 0.001f,
                "Every aim shape in the game is a flat footprint (see AbilityAim), so a target standing " +
                "on a ramp must not change the aim verdict.");
            Assert.AreEqual(45f, flat, 0.001f);
        }

        // ---- Cast scoring -------------------------------------------------------

        /// <summary>
        /// Role preference is the plan and must never be overturned by a cooldown tiebreak —
        /// otherwise a Warrior with a cheap Escape would flee instead of stealing.
        /// </summary>
        [Test]
        public void RolePreference_AlwaysBeatsTheCooldownTiebreak()
        {
            float firstChoiceExpensive = BotTactics.ScoreCastCandidate(0, 30f);
            float secondChoiceFree     = BotTactics.ScoreCastCandidate(1, 0f);

            Assert.Less(firstChoiceExpensive, secondChoiceFree,
                "A 30-second first-choice ability still outranks a free second-choice one. If it does " +
                "not, the cooldown penalty has grown past one role step and the cast plan no longer " +
                "means what it says.");
        }

        /// <summary>
        /// Within one role, the cheap tool wins. This is what stops a Warrior opening every
        /// skirmish with a 12-second Wing Slam when a 4-second Headbutt does the same job,
        /// then standing empty-handed for the next ten seconds.
        /// </summary>
        [Test]
        public void WithinOneRole_TheCheaperCooldownWins()
        {
            Assert.Less(BotTactics.ScoreCastCandidate(0, 4f), BotTactics.ScoreCastCandidate(0, 12f),
                "Headbutt (4 s) must outrank Wing Slam (12 s) when both satisfy the same role.");
        }

        // ---- Pile choice ---------------------------------------------------------

        /// <summary>
        /// The Runner plans a route; the Brawler plans a step. Same two piles, opposite
        /// answers — that difference is the class identity, not a tuning nicety.
        /// </summary>
        [Test]
        public void RoundTripBias_IsWhatSeparatesARouteFromAStep()
        {
            // Pile A: close to the bot, but on the far side of the map from its base.
            // Pile B: a little further out, but sitting on the way home.
            //
            // These two swap places at roundTripBias 3/22 = 0.136, so the pair is a genuine
            // assertion about the Brawler: it passes only while that class is near enough to
            // proximity-only to sit below that line. The 22-unit base-distance spread is
            // ordinary rather than contrived — opposite corners of the 51.3 m arena are
            // routinely that far apart.
            //
            // This fixture caught a real inconsistency once and should not be widened to make
            // it stop. The Warrior shipped at 0.25, which lands the far pile, contradicting
            // the "does not care about the walk home" comment sitting directly above the
            // value. The first reading of that red was "the fixture is too tight" and the
            // response was to stretch the gap until 0.25 passed — which would have deleted the
            // assertion instead of the bug. Keep it tight.
            const float aToBot = 5f, aToBase = 26f;
            const float bToBot = 8f, bToBase = 4f;
            const float stock  = 12f;

            var brawler = BotTactics.ProfileFor(ChickenClass.Warrior);
            var runner  = BotTactics.ProfileFor(ChickenClass.Speedy);

            float brawlerA = BotTactics.PileCost(aToBot, aToBase, stock, brawler.RoundTripBias);
            float brawlerB = BotTactics.PileCost(bToBot, bToBase, stock, brawler.RoundTripBias);
            float runnerA  = BotTactics.PileCost(aToBot, aToBase, stock, runner.RoundTripBias);
            float runnerB  = BotTactics.PileCost(bToBot, bToBase, stock, runner.RoundTripBias);

            Assert.Less(brawlerA, brawlerB,
                "The Brawler takes the near pile — it wants to be where the traffic is and does not " +
                "care about the walk home.");
            Assert.Less(runnerB, runnerA,
                "The Runner takes the pile on the way home. Ignoring the return leg is what had every " +
                "bot converge on the centre island and then walk the full diagonal back.");
        }

        /// <summary>
        /// "Does not care about the walk home" is a property, not a data point: for the
        /// Brawler the nearer pile must win at <i>every</i> base distance, not merely at the
        /// one pair the fixture above happens to use.
        /// </summary>
        /// <remarks>
        /// A single two-pile comparison can only ever prove the bias sits on one side of that
        /// pair's crossover. This sweeps the return leg across the whole arena instead, so the
        /// test states the design claim directly and cannot be satisfied by nudging a fixture.
        ///
        /// Its worst case is the near pile a full arena from base and the far pile on top of
        /// it, so it holds while the Brawler bias stays under 3/52 ≈ 0.058 — not literally
        /// zero. That tolerance is deliberate: the claim being pinned is "proximity-dominant",
        /// and a hair of round-trip awareness would not contradict the class comment, whereas
        /// the 0.25 this caught (66 of the 196 swept pairs inverted) plainly did. If the class
        /// is ever meant to weigh the walk home properly, argue with this test first.
        /// </remarks>
        [Test]
        public void TheBrawler_TakesTheNearerPile_AtEveryBaseDistance()
        {
            var brawler = BotTactics.ProfileFor(ChickenClass.Warrior);
            const float near = 5f, far = 8f, stock = 12f;

            for (float nearToBase = 0f; nearToBase <= 52f; nearToBase += 4f)
            for (float farToBase  = 0f; farToBase  <= 52f; farToBase  += 4f)
            {
                float costNear = BotTactics.PileCost(near, nearToBase, stock, brawler.RoundTripBias);
                float costFar  = BotTactics.PileCost(far,  farToBase,  stock, brawler.RoundTripBias);

                Assert.Less(costNear, costFar,
                    $"With the near pile {near} m out ({nearToBase} m from base) and the far pile " +
                    $"{far} m out ({farToBase} m from base), the Brawler preferred the FAR one. Its " +
                    $"RoundTripBias is {brawler.RoundTripBias}; anything above 0 lets a big enough " +
                    "base-distance spread overwhelm the proximity advantage, which contradicts the " +
                    "class comment and sends a bot that wants to be where the traffic is to the quiet " +
                    "corner instead.");
            }
        }

        /// <summary>An almost-drained pile is not worth the same trip as a full one.</summary>
        [Test]
        public void ARichPile_IsCheaperToVisitThanAnAlmostDrainedOne()
        {
            float rich = BotTactics.PileCost(10f, 10f, available: 20f, roundTripBias: 0.5f);
            float bare = BotTactics.PileCost(10f, 10f, available: 1f,  roundTripBias: 0.5f);
            Assert.Less(rich, bare, "Richness must discount the trip, or a bot will keep walking to a " +
                "pile with two pecks left in it because it happens to be underfoot.");
        }

        [Test]
        public void PileCost_NeverGoesNegative_EvenForAnAbsurdlyRichPile()
        {
            Assert.GreaterOrEqual(BotTactics.PileCost(1f, 1f, available: 100000f, roundTripBias: 1f), 0f,
                "A negative cost would make the richest pile beat a pile the bot is standing on, forever.");
        }

        // ---- Target choice ---------------------------------------------------------

        /// <summary>
        /// PROVEN GAP. Proximity used to be the entire rule, so a hunting bot always went
        /// for whoever was closest — normally the bot farming quietly next door rather than
        /// the human two seconds from winning.
        /// </summary>
        [Test]
        public void ALoadedLeaderFarAway_OutranksAnEmptyNeighbour()
        {
            var predator = BotTactics.ProfileFor(ChickenClass.Assassin);

            float neighbour = BotTactics.TargetPriority(
                dist: 2f, cargoFraction: 0f, bankedShare: 0.05f,
                reachRadius: predator.HuntRadius, leaderFocus: predator.LeaderFocus);

            float leader = BotTactics.TargetPriority(
                dist: 13f, cargoFraction: 0.9f, bankedShare: 0.85f,
                reachRadius: predator.HuntRadius, leaderFocus: predator.LeaderFocus);

            Assert.Greater(leader, neighbour,
                $"The Assassin scored the empty neighbour {neighbour:0.00} and the loaded leader " +
                $"{leader:0.00}. Its entire income is theft — going for the empty one is a wasted trip.");
        }

        /// <summary>
        /// A class with no leader focus must not develop one. The Brawler picks fights with
        /// whoever is in front of it; that is the read the player should get.
        /// </summary>
        [Test]
        public void WithoutLeaderFocus_TheScoreboardIsIgnored()
        {
            float ignoresBoard = BotTactics.TargetPriority(6f, 0.3f, bankedShare: 0.0f, reachRadius: 10f, leaderFocus: 0f);
            float sameButAhead = BotTactics.TargetPriority(6f, 0.3f, bankedShare: 1.0f, reachRadius: 10f, leaderFocus: 0f);
            Assert.AreEqual(ignoresBoard, sameButAhead, 0.0001f,
                "leaderFocus 0 must mean the banked score contributes nothing at all.");
        }

        [Test]
        public void ARivalBeyondReach_ScoresZero_AndSoIsNeverChosen()
        {
            Assert.AreEqual(0f, BotTactics.TargetPriority(11f, 1f, 1f, reachRadius: 10f, leaderFocus: 1f), 0.0001f);
            Assert.AreEqual(0f, BotTactics.TargetPriority(1f, 1f, 1f, reachRadius: 0f, leaderFocus: 1f), 0.0001f,
                "A profile that does not chase (reach 0) must score nobody, or it will walk off its post.");
        }

        [Test]
        public void AllElseEqual_TheCloserRivalWins()
        {
            float near = BotTactics.TargetPriority(2f, 0.5f, 0.3f, 10f, 0.5f);
            float far  = BotTactics.TargetPriority(9f, 0.5f, 0.3f, 10f, 0.5f);
            Assert.Greater(near, far);
        }

        // ---- The clock ---------------------------------------------------------------

        [Test]
        public void MatchPhase_TracksTheFractionRemaining_NotAbsoluteSeconds()
        {
            // The shipped match is 45 s; the phase rules must hold for any duration.
            foreach (float duration in new[] { 45f, 180f })
            {
                Assert.AreEqual(MatchPhase.Opening, BotTactics.ResolvePhase(duration * 0.95f, duration));
                Assert.AreEqual(MatchPhase.Mid,     BotTactics.ResolvePhase(duration * 0.50f, duration));
                Assert.AreEqual(MatchPhase.Endgame, BotTactics.ResolvePhase(duration * 0.10f, duration));
                Assert.AreEqual(MatchPhase.Endgame, BotTactics.ResolvePhase(0f, duration));
            }
        }

        /// <summary>
        /// PROVEN GAP. Bots had no concept of the clock, so one would start a fresh trip to
        /// the far pile with four seconds left and finish the match holding a full load worth
        /// exactly nothing.
        /// </summary>
        [Test]
        public void AFullBeakAndNoTime_MeansGoHomeNow()
        {
            const float speed = 12f;

            Assert.IsTrue(BotTactics.MustBankNow(cargo: 8f, distToBase: 30f, moveSpeed: speed,
                                                 timeRemaining: 4f, depositSeconds: 0.9f),
                "30 m at 12 m/s is 2.5 s of walking before any detour, plus the deposit. With 4 s left " +
                "the bot must already be moving.");

            Assert.IsFalse(BotTactics.MustBankNow(cargo: 8f, distToBase: 30f, moveSpeed: speed,
                                                  timeRemaining: 25f, depositSeconds: 0.9f),
                "With 25 s left there is time for another pile; panicking early throws away foraging.");
        }

        [Test]
        public void AnEmptyBeak_NeverTriggersThePanicBank()
        {
            Assert.IsFalse(BotTactics.MustBankNow(cargo: 0f, distToBase: 40f, moveSpeed: 1f,
                                                  timeRemaining: 0.5f, depositSeconds: 0f),
                "Carrying nothing means there is nothing to lose — the bot should keep foraging until " +
                "the whistle rather than sprint home empty.");
        }

        /// <summary>
        /// The margin has to cover what a straight line cannot: navmesh detours round walls
        /// and piles, the pile slow, and the deposit itself. Being early costs seconds of
        /// foraging; being late costs the whole haul.
        /// </summary>
        [Test]
        public void TheTravelEstimate_LeavesRealHeadroom()
        {
            Assert.Greater(BotTactics.BankTravelSafetyFactor, 1f,
                "A safety factor of 1 assumes the bot walks home in a straight line at full speed " +
                "through the arena's walls and piles.");

            // A trip that exactly fills the remaining time on a perfect straight line must
            // still read as "too late" once the margin is applied.
            const float speed = 10f, dist = 30f;
            float straightLine = dist / speed; // 3.0 s
            Assert.IsTrue(BotTactics.MustBankNow(4f, dist, speed, straightLine + 0.1f, 0f),
                "A trip with a tenth of a second to spare on paper is not a trip the bot should still " +
                "be deferring.");
        }

        [Test]
        public void AStuckBot_AssumesTheWorst_RatherThanDividingByZero()
        {
            Assert.IsTrue(BotTactics.MustBankNow(cargo: 5f, distToBase: 10f, moveSpeed: 0f,
                                                 timeRemaining: 100f, depositSeconds: 0f),
                "A zero speed makes the arrival estimate meaningless. Heading home is the safe answer; " +
                "an infinity or a NaN leaking into the FSM is not.");
        }

        // ---- Endgame thresholds -------------------------------------------------------

        /// <summary>
        /// Leading in the endgame means banking early and giving the human nothing to rob.
        /// Trailing means holding a bigger load and taking the risk, because a tidy loss is
        /// still a loss.
        /// </summary>
        [Test]
        public void TheEndgame_SplitsLeadersFromTrailers()
        {
            foreach (var cls in AllClasses)
            {
                var p = BotTactics.ProfileFor(cls);

                float mid      = BotTactics.ReturnThreshold(p, MatchPhase.Mid,     leading: false);
                float leading  = BotTactics.ReturnThreshold(p, MatchPhase.Endgame, leading: true);
                float trailing = BotTactics.ReturnThreshold(p, MatchPhase.Endgame, leading: false);

                Assert.AreEqual(Mathf.Clamp01(p.ReturnThreshold), mid, 0.0001f,
                    $"{cls}: outside the endgame the profile's own threshold must be used verbatim.");
                Assert.Less(leading, mid,
                    $"{cls}: a leader in the endgame must bank EARLIER than normal, not later.");
                Assert.GreaterOrEqual(trailing, mid,
                    $"{cls}: a trailing bot in the endgame should be holding at least as much, not " +
                    "conceding by banking small.");
            }
        }

        [Test]
        public void EveryReturnThreshold_StaysAFraction()
        {
            foreach (var cls in AllClasses)
            foreach (MatchPhase phase in Enum.GetValues(typeof(MatchPhase)))
            foreach (bool leading in new[] { true, false })
            {
                float t = BotTactics.ReturnThreshold(BotTactics.ProfileFor(cls), phase, leading);
                Assert.IsTrue(t >= 0f && t <= 1f,
                    $"{cls}/{phase}/leading={leading} produced {t}. A cargo FRACTION above 1 is never " +
                    "reached, so the bot would forage until the timer ran out and bank nothing.");
            }
        }

        // ---- Cast plans ------------------------------------------------------------------

        /// <summary>
        /// Robbing an empty rival does nothing but burn the cooldown. The empty-target plan
        /// must therefore not lead with Steal.
        /// </summary>
        [Test]
        public void AgainstAnEmptyRival_NoStrategyOpensWithASteal()
        {
            foreach (BotStrategy s in Enum.GetValues(typeof(BotStrategy)))
            {
                var plan = BotTactics.CastPlan(s, BotSituation.Engaging, targetIsLoaded: false);
                Assert.IsNotEmpty(plan, $"{s} has no plan for engaging an empty rival.");
                Assert.AreNotEqual(BotRole.Steal, plan[0],
                    $"{s} opens on an empty-handed rival with Steal, which transfers nothing and puts " +
                    "the only tool that could have mattered on cooldown.");
            }
        }

        /// <summary>Against a carrier, taking the cargo is the point.</summary>
        [Test]
        public void AgainstACarrier_TheStealComesFirst()
        {
            foreach (BotStrategy s in Enum.GetValues(typeof(BotStrategy)))
            {
                var plan = BotTactics.CastPlan(s, BotSituation.Engaging, targetIsLoaded: true);
                Assert.AreEqual(BotRole.Steal, plan[0],
                    $"{s} engages a loaded rival without leading on Steal. Cargo on the ground is cargo " +
                    "nobody banks.");
            }
        }

        /// <summary>
        /// Banking is the only situation where a deposit accelerator does anything, and it
        /// must not be reachable from any other one — Quick Drop wearing BotRole.Forage is
        /// exactly how a Speedy bot ended up firing it into a food pile.
        /// </summary>
        [Test]
        public void TheBankRole_IsReachableOnlyWhileBanking()
        {
            foreach (BotStrategy s in Enum.GetValues(typeof(BotStrategy)))
            foreach (BotSituation sit in Enum.GetValues(typeof(BotSituation)))
            foreach (bool loaded in new[] { true, false })
            {
                var plan     = BotTactics.CastPlan(s, sit, loaded);
                bool hasBank = Array.IndexOf(plan, BotRole.Bank) >= 0;

                if (sit == BotSituation.Banking)
                    Assert.IsTrue(hasBank, $"{s}/{sit}: nothing fires the deposit accelerator at the base.");
                else
                    Assert.IsFalse(hasBank,
                        $"{s}/{sit}: a deposit accelerator is reachable away from the base, where it " +
                        "does nothing but spend a cooldown.");
            }
        }

        /// <summary>
        /// Foraging is driven by the pile-arrival path, never by a cast plan. If Forage ever
        /// appeared in one, the bot would try to Peck at a rival's face.
        /// </summary>
        [Test]
        public void TheForageRole_NeverAppearsInACombatPlan()
        {
            foreach (BotStrategy s in Enum.GetValues(typeof(BotStrategy)))
            foreach (BotSituation sit in Enum.GetValues(typeof(BotSituation)))
            foreach (bool loaded in new[] { true, false })
            {
                Assert.Less(Array.IndexOf(BotTactics.CastPlan(s, sit, loaded), BotRole.Forage), 0,
                    $"{s}/{sit} can fire the Forage role, which resolves against a FoodPile and not a " +
                    "chicken. Peck belongs to BotController's pile-arrival path alone.");
            }
        }

        /// <summary>
        /// Only the Runner treats mobility as a travel tool. Everyone else saving it for the
        /// moment it is needed is a deliberate difference in how the classes read.
        /// </summary>
        [Test]
        public void OnlyTheRunner_BurnsMobilityJustToTravel()
        {
            foreach (BotStrategy s in Enum.GetValues(typeof(BotStrategy)))
            {
                var plan  = BotTactics.CastPlan(s, BotSituation.Transiting, targetIsLoaded: false);
                bool uses = Array.IndexOf(plan, BotRole.Escape) >= 0;

                if (s == BotStrategy.Runner)
                    Assert.IsTrue(uses, "Speed Burst on the open road IS the Speedy. A Runner that only " +
                        "sprints when frightened is not playing the class.");
                else
                    Assert.IsFalse(uses, $"{s} spends its escape walking between piles, so it has nothing " +
                        "left the moment it is actually caught.");
            }
        }

        /// <summary>
        /// Being chased while loaded is the fight the Brawler wanted. It turning to control
        /// its pursuer instead of running is the whole read of the class.
        /// </summary>
        [Test]
        public void TheBrawlerAndTheRunner_AnswerBeingChasedDifferently()
        {
            var brawler = BotTactics.CastPlan(BotStrategy.Brawler, BotSituation.Retreating, false);
            var runner  = BotTactics.CastPlan(BotStrategy.Runner,  BotSituation.Retreating, false);

            Assert.AreEqual(BotRole.Control, brawler[0],
                "A cornered Brawler turns and fights; leading on Escape would make it read as a Speedy.");
            Assert.AreEqual(BotRole.Escape, runner[0],
                "A cornered Runner runs. It cannot win the trade and should not try.");
        }

        [Test]
        public void EveryStrategyAndSituation_ProducesANonNullPlan()
        {
            foreach (BotStrategy s in Enum.GetValues(typeof(BotStrategy)))
            foreach (BotSituation sit in Enum.GetValues(typeof(BotSituation)))
            foreach (bool loaded in new[] { true, false })
                Assert.IsNotNull(BotTactics.CastPlan(s, sit, loaded),
                    $"{s}/{sit}/loaded={loaded} returned null; the caster loop would throw inside " +
                    "FixedUpdateNetwork.");
        }

        /// <summary>
        /// The plans are static, shared instances read every think tick by every bot. A
        /// freshly-allocated array per call would be hundreds of allocations a second on a
        /// four-bot match, on a platform budgeted for 30 fps on a 2021 mid-range Android.
        /// </summary>
        [Test]
        public void CastPlans_AreSharedInstances_NotPerTickAllocations()
        {
            var a = BotTactics.CastPlan(BotStrategy.Brawler, BotSituation.Engaging, true);
            var b = BotTactics.CastPlan(BotStrategy.Brawler, BotSituation.Engaging, true);
            Assert.AreSame(a, b,
                "CastPlan allocated a new array. This runs inside the think tick for every bot.");
        }

        // ---- Roster wiring ------------------------------------------------------------------

        /// <summary>
        /// PROVEN GAP, and the nastiest one in this pass. Quick Drop shipped as
        /// <c>BotRole.Forage</c>. <c>BotController.TryPeckAtPile</c> fires the Forage role
        /// while parked at a pile and <c>TryGetReadySlotForRole</c> returns the FIRST match,
        /// so a Speedy bot holding both would burn a 12-second deposit-rate buff into the
        /// dirt and — whenever Quick Drop held the lower slot index — never Peck at all.
        /// Zero income, no error, nothing logged.
        /// </summary>
        [Test]
        public void ForageIsPeckAndOnlyPeck_OrABotStandsAtAPileFiringTheWrongThing()
        {
            var all = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir);
            Assert.IsNotEmpty(all, "No ability assets found — the fixture is broken.");

            foreach (var a in all)
            {
                if (a.ResolveBotRole() != BotRole.Forage) continue;
                Assert.IsInstanceOf<PeckAbilitySO>(a,
                    $"'{a.name}' resolves BotRole.Forage but is not a Peck. BotController fires Forage " +
                    "while standing at a food pile and takes the first ready match, so this ability will " +
                    "be cast into the dirt and can shadow the real Peck out of the bot's rotation " +
                    "entirely.");
            }
        }

        /// <summary>
        /// A deposit accelerator only does something at the base, so it must carry the role
        /// the Banking plan actually asks for. Detected structurally — via the controller
        /// state the ability writes — rather than by naming Quick Drop, so the next one
        /// inherits the check.
        /// </summary>
        [Test]
        public void EveryDepositAccelerator_CarriesTheBankRole()
        {
            var all = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir);
            foreach (var a in all)
            {
                if (!(a is QuickDropAbilitySO)) continue;
                Assert.AreEqual(BotRole.Bank, a.ResolveBotRole(),
                    $"'{a.name}' moves DepositRateMultiplier, so it is only worth anything standing on " +
                    $"the base — but it resolves BotRole.{a.ResolveBotRole()}, which the Banking cast " +
                    "plan does not ask for.");
            }
        }

        /// <summary>
        /// <see cref="BotRole"/> is byte-serialised into ability .asset files, so the two
        /// roles appended in this pass must keep the values they were authored with. A
        /// reordering would silently turn every Bank ability into something else.
        /// </summary>
        [Test]
        public void TheAppendedBotRoles_KeepTheirSerialisedValues()
        {
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(BotRole)));
            Assert.AreEqual(6, (byte)BotRole.Forage, "Forage was authored as 6 in existing .asset files.");
            Assert.AreEqual(7, (byte)BotRole.Bank,   "Bank was appended as 7; renumbering rewrites assets.");
        }

        /// <summary>
        /// Every role a cast plan can ask for must be reachable by at least one shipped
        /// ability, or the plan silently falls through to its next entry forever.
        /// </summary>
        [Test]
        public void EveryRoleAPlanAsksFor_ExistsSomewhereInTheRoster()
        {
            var all      = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir);
            var present  = new HashSet<BotRole>();
            foreach (var a in all) present.Add(a.ResolveBotRole());

            var asked = new HashSet<BotRole>();
            foreach (BotStrategy s in Enum.GetValues(typeof(BotStrategy)))
            foreach (BotSituation sit in Enum.GetValues(typeof(BotSituation)))
            foreach (bool loaded in new[] { true, false })
                foreach (var role in BotTactics.CastPlan(s, sit, loaded)) asked.Add(role);

            foreach (var role in asked)
                Assert.IsTrue(present.Contains(role),
                    $"A cast plan asks for BotRole.{role} but no shipped ability resolves it, so that " +
                    "entry can never fire and the plan quietly means something narrower than it reads.");
        }
    }
}
