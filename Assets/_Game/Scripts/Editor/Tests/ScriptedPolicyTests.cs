using System;
using System.IO;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// P0, the game's bot as a match policy. The decisions are asserted against the game's own pure functions (<see cref="BotTactics"/>) on
    /// hand-built views; every number is the test's own. The last tests run P0 over the shipped kits and pin what is mirrored.
    /// </summary>
    public sealed class ScriptedPolicyTests
    {
        private const float Half = 20f;
        private const float Tick = 1f / 30f;

        private OracleMapSO _map;

        [SetUp]
        public void SetUp() => _map = MatchTestKit.OpenMap(Half);

        [TearDown]
        public void TearDown() => MatchTestKit.Release();

        private static readonly BotProfile Profile = new BotProfile(BotStrategy.Brawler, 0.85f, 0.60f, 5.5f, 11f, 0.10f, 9f, 6f, 0f, 0.35f, 0.28f);

        private static MatchAbility Make(MatchAbility a, BotRole role, float reach, bool needsEnemy)
        {
            a.Role = role;
            a.Reach = reach;
            a.NeedsEnemyInRange = needsEnemy;
            return a;
        }

        private static MatchKit Kit(string label, params MatchAbility[] abilities)
        {
            var kit = MatchTestKit.Fighter(label, 6f, abilities);
            kit.Bot = Profile;
            return kit;
        }

        private MatchView View(MatchKit[] kits, float timeLeft = 45f)
        {
            var view = new MatchView
            {
                Self = 0, Time = 0f, TimeLeft = timeLeft,
                Seats = new SeatView[4], PileFood = new float[OracleMapSO.PileCount], Kits = kits, Map = _map, Nav = OracleNav.For(_map, Half),
                Rules = new MatchRules
                {
                    Farm = new FarmRules { WinTarget = 40f, ClockSeconds = 45f, TickSeconds = Tick, PileSlowFactor = 1f },
                    ArenaHalf = Half, KeepOutRadius = 2.5f, SightArrivalRadius = 1.5f,
                },
            };
            for (int p = 0; p < view.PileFood.Length; p++) view.PileFood[p] = 10f;
            for (int s = 0; s < 4; s++)
            {
                int slots = 1 + (kits[s]?.Abilities?.Length ?? 0);
                view.Seats[s] = new SeatView
                {
                    Present = kits[s] != null, Seen = true, Known = true, MarkTarget = -1, MarkedBy = -1,
                    Pos = _map.Places[OracleMapSO.BaseIndex(s)].Pos, CooldownLeft = new float[slots],
                };
            }
            return view;
        }

        private static MatchIntent Think(MatchView view, PolicyParams? p = null)
        {
            var policy = ScriptedPolicy.Factory(0, p ?? PolicyParams.Default);
            var rng = MatchRng.Create(1UL, 0UL);
            return policy.Think(view, ref rng);
        }

        private Vector2 Home(int seat) => _map.Places[OracleMapSO.BaseIndex(seat)].Pos;

        private static MatchKit[] Four(MatchKit me, MatchKit rival) => new[] { me, rival, null, null };

        [Test]
        public void MustBankNow_SendsTheChickenHome()
        {
            var view = View(Four(Kit("me"), Kit("rival")), timeLeft: 5f);
            view.Seats[0].Pos = new Vector2(-10f, 0f);
            view.Seats[0].Cargo = 10f;
            float dist = Vector2.Distance(view.Seats[0].Pos, Home(0));
            Assert.IsTrue(BotTactics.MustBankNow(10f, dist, view.Kits[0].Farm.MoveSpeed, view.TimeLeft, 10f / view.Kits[0].Farm.DepositRate),
                "The set-up must be one the game's own rule calls 'bank now'.");

            var intent = Think(view);

            Assert.AreEqual(Home(0), intent.Goal);
            Assert.AreEqual(-1, intent.PeckPile);
        }

        [Test]
        public void StealGuard_ShellsUpWhenAStealerIsInRange()
        {
            var shield = Make(MatchTestKit.Shield(1f, 0.5f, 2f, 8f), BotRole.Defense, float.PositiveInfinity, false);
            shield.IsStealShield = true;
            var steal = Make(MatchTestKit.Steal(5f, 4f, 3f), BotRole.Steal, 5f, true);
            steal.IsStealer = true;
            var view = View(Four(Kit("me", shield), Kit("thief", steal)));
            view.Seats[0].Pos = Vector2.zero;
            view.Seats[0].Cargo = 10f;
            view.Seats[1].Pos = new Vector2(3f, 0f);
            Assert.IsTrue(BotTactics.ShouldStealGuard(10f, 3f, false));

            Assert.AreEqual(0, Think(view).CastSlot, "A loaded bot with a stealer in reach and a shell ready shells up.");

            view.Seats[1].Pos = new Vector2(BotTactics.StealGuardRadius + 2f, 0f);
            Assert.AreEqual(-1, Think(view).CastSlot, "A stealer outside the guard radius is no reason to shell up.");
        }

        [Test]
        public void MarkFollowThrough_StunsThenKills()
        {
            const float stunSeconds = 2f;
            var stun = Make(MatchTestKit.Stun(5f, stunSeconds, 6f), BotRole.Control, 5f, true);
            var mark = Make(MatchTestKit.DeathMark(3f, 6f, 4f, 20f, 8f, 4f), BotRole.Offense, float.PositiveInfinity, false);
            var view = View(Four(Kit("spoiler", stun, mark), Kit("victim")));
            view.Seats[0].Pos = Vector2.zero;
            view.Seats[0].MarkTarget = 1;

            // Far away: the mark is hunted across the map and the stun is kept back.
            view.Seats[1].Pos = new Vector2(30f, 0f);
            view.Seats[0].ArmLeft = 3f; view.Seats[0].WindowLeft = 6f;
            Assert.AreEqual(BotMarkStep.Hunt, BotTactics.NextMarkStep(true, false, 3f, 6f, false, 0f));
            var hunt = Think(view);
            Assert.AreEqual(-1, hunt.CastSlot);
            Assert.AreEqual(view.Seats[1].Pos, hunt.Goal, "It walks to where it believes the mark is.");

            // In reach, with a stun that still runs when the arm ends: land it.
            view.Seats[1].Pos = new Vector2(3.5f, 0f);
            view.Seats[0].ArmLeft = 1f; view.Seats[0].WindowLeft = 5f;
            Assert.AreEqual(BotMarkStep.Stun, BotTactics.NextMarkStep(true, false, 1f, 5f, true, stunSeconds));
            Assert.AreEqual(0, Think(view).CastSlot, "The stun goes first.");

            // Armed, the mark stunned and in kill range: press again.
            view.Seats[0].ArmLeft = 0f; view.Seats[0].WindowLeft = 2f;
            view.Seats[1].Stunned = true;
            view.Seats[0].CooldownLeft[1] = 5f;   // the stun is on cooldown now
            Assert.AreEqual(BotMarkStep.Kill, BotTactics.NextMarkStep(true, true, 0f, 2f, false, 0f));
            Assert.AreEqual(1, Think(view).CastSlot, "Then the kill.");
        }

        [Test]
        public void ALoadedSpoiler_IsJustAChickenWithCargo_TheOrdinaryReturnRuleDecides_AndASmallLoadKeepsMarking()
        {
            // A kill's loot is ordinary cargo (no bag since 2026-11-24): above the return threshold the chicken banks first and marks nobody,
            // below it the Spoiler goes on hunting.
            var mark = Make(MatchTestKit.DeathMark(3f, 6f, 4f, 20f, 8f, 4f), BotRole.Offense, float.PositiveInfinity, false);
            var view = View(Four(Kit("spoiler", mark), Kit("rival")));
            view.Seats[0].Pos = Vector2.zero;
            view.Seats[1].Pos = new Vector2(30f, 0f);
            float capacity = view.Kits[0].Farm.CargoCapacity;

            view.Seats[0].Cargo = capacity * (Profile.ReturnThreshold + 0.05f);
            var loaded = Think(view);
            Assert.AreEqual(Home(0), loaded.Goal, "loaded past the return threshold: home, like any chicken");
            Assert.AreEqual(-1, loaded.CastSlot, "a loaded Spoiler banks before it marks again");

            view.Seats[0].Cargo = capacity * (Profile.ReturnThreshold - 0.05f);
            var light = Think(view);
            Assert.AreEqual(0, light.CastSlot, "a small load is no reason to stop marking");
        }

        [Test]
        public void Flee_AboveProtectThreshold_WithARivalInDanger()
        {
            var view = View(Four(Kit("me"), Kit("rival")));
            view.Seats[0].Pos = Vector2.zero;
            view.Seats[1].Pos = new Vector2(Profile.DangerRadius - 1f, 0f);
            float capacity = view.Kits[0].Farm.CargoCapacity;

            view.Seats[0].Cargo = capacity * (Profile.ProtectCargoThreshold + 0.05f);
            Assert.AreEqual(Home(0), Think(view).Goal, "Loaded past the protect threshold with a rival in danger range: home.");

            view.Seats[0].Cargo = capacity * (Profile.ProtectCargoThreshold - 0.05f);
            Assert.AreNotEqual(Home(0), Think(view).Goal, "Below the protect threshold the rival is no reason to run.");
        }

        [Test]
        public void Collect_PicksTheCheapestPileByPileCost()
        {
            var view = View(Four(Kit("me"), Kit("rival")));
            int near = OracleMapSO.PersonalIndex(2);
            view.Seats[0].Pos = _map.Places[near].Pos;
            view.Seats[1].Pos = Home(1);
            view.PileFood[OracleMapSO.CentreIndex] = 15f;   // richer, but far

            Assert.AreEqual(near, Think(view).PeckPile, "The pile underfoot is the cheapest.");

            view.PileFood[near] = 0f;
            int second = Think(view).PeckPile;
            Assert.AreNotEqual(near, second);
            Assert.GreaterOrEqual(second, 0);
            // The game's own cost decides which: the cheapest by PileCost over what is left.
            int expected = -1;
            float best = float.MaxValue;
            for (int k = 0; k < OracleMapSO.PileCount; k++)
            {
                int p = FarmOnlyPolicy.PileAtRank(0, k);
                if (view.PileFood[p] <= 0f) continue;
                float cost = BotTactics.PileCost(Vector2.Distance(view.Seats[0].Pos, _map.Places[p].Pos), Vector2.Distance(_map.Places[p].Pos, Home(0)),
                    view.PileFood[p], Profile.RoundTripBias);
                if (cost < best) { best = cost; expected = p; }
            }
            Assert.AreEqual(expected, second);
        }

        // Seat 0 stands on the line of symmetry between its two contested piles (the open map mirrors them across its base's axis), every
        // other pile empty: the two costs tie.
        private MatchView TiedBetweenTwoContestedPiles(out int a, out int b)
        {
            var view = View(Four(Kit("me"), Kit("rival")));
            a = OracleMapSO.ContestedIndex(0);
            b = OracleMapSO.ContestedIndex(3);
            for (int p = 0; p < view.PileFood.Length; p++) view.PileFood[p] = p == a || p == b ? 10f : 0f;
            view.Seats[0].Pos = Home(0);
            view.Seats[1].Pos = Home(2);
            float ca = BotTactics.PileCost(Vector2.Distance(Home(0), _map.Places[a].Pos), Vector2.Distance(_map.Places[a].Pos, Home(0)), 10f, Profile.RoundTripBias);
            float cb = BotTactics.PileCost(Vector2.Distance(Home(0), _map.Places[b].Pos), Vector2.Distance(_map.Places[b].Pos, Home(0)), 10f, Profile.RoundTripBias);
            Assert.AreEqual(ca, cb, ScriptedPolicy.PileTieCost, "The set-up must be a tie by the game's own pile cost.");
            return view;
        }

        [Test]
        public void Collect_ATieBetweenTwoPiles_IsNotSettledBySeatOrder()
        {
            var view = TiedBetweenTwoContestedPiles(out int a, out int b);
            int pickedA = 0, pickedB = 0;
            for (ulong seed = 1; seed <= 64; seed++)
            {
                var rng = MatchRng.Create(seed, 0UL);
                int pile = ScriptedPolicy.Factory(0, PolicyParams.Default).Think(view, ref rng).PeckPile;
                if (pile == a) pickedA++;
                else if (pile == b) pickedB++;
                else Assert.Fail($"Picked pile {pile}, neither of the two tied piles.");
            }
            Assert.Greater(pickedA, 0, "A tie is drawn, so either pile comes up.");
            Assert.Greater(pickedB, 0, "A tie is drawn, so either pile comes up; an order relative to the seat would pick the same one every time.");
        }

        [Test]
        public void Collect_ANearTieStillGoesToTheCheaperPile()
        {
            var view = TiedBetweenTwoContestedPiles(out int a, out int b);
            // A step toward pile a makes it cheaper by far more than a tie.
            view.Seats[0].Pos = Home(0) + (_map.Places[a].Pos - Home(0)).normalized * 0.1f;
            for (ulong seed = 1; seed <= 16; seed++)
            {
                var rng = MatchRng.Create(seed, 0UL);
                Assert.AreEqual(a, ScriptedPolicy.Factory(0, PolicyParams.Default).Think(view, ref rng).PeckPile);
            }
        }

        [Test]
        public void ParamsNotNaN_OverrideTheProfile()
        {
            var view = View(Four(Kit("me"), Kit("rival")));
            view.Seats[0].Pos = _map.Places[OracleMapSO.PersonalIndex(0)].Pos;
            view.Seats[0].Cargo = view.Kits[0].Farm.CargoCapacity * 0.5f;
            view.Seats[1].Pos = new Vector2(-Half, -Half);
            Assert.Less(0.5f, Profile.ReturnThreshold);

            Assert.GreaterOrEqual(Think(view).PeckPile, 0, "Half full, below the bot's threshold: still collecting.");

            var p = PolicyParams.Default;
            p.ReturnThreshold = 0.3f;
            var intent = Think(view, p);
            Assert.AreEqual(Home(0), intent.Goal, "A threshold in the params replaces the profile's.");
            Assert.AreEqual(-1, intent.PeckPile);
        }

        // ---- over the shipped kits ----

        private static OracleInputs Inputs() => new OracleInputs
        {
            Map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath),
            Abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
            Classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
            Match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath),
        };

        [Test]
        public void P0_NeverHasARejectedIntent()
        {
            var context = MatchContext.From(Inputs(), OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius());
            foreach (var design in new[] { MatchDesignKind.Mixed, MatchDesignKind.OnePerClass, MatchDesignKind.SeatCheck })
            {
                var run = MatchRunner.Run(context, new MatchRunSpec { Design = design, Replicates = 1, Population = PolicyPopulation.P0, RunSeed = 7UL, Threads = 4 });
                for (int i = 0; i < run.Results.Length; i++)
                    Assert.AreEqual(0, run.Results[i].RejectedIntents,
                        $"{design} cell {i}: a policy bug (an intent the rules refuse); casts lost to the reaction delay are counted apart.");
            }
        }

        [Test]
        public void BotController_Tunables_StillMatchP0sMirrors()
        {
            var prefab = TestAssets.Load<GameObject>(TestAssets.ChickenPrefabPath);
            var bot = prefab.GetComponentInChildren<BotController>(true);
            Assert.IsNotNull(bot);
            var so = new SerializedObject(bot);
            Assert.AreEqual(so.FindProperty("_stateExitRadiusFactor").floatValue, ScriptedPolicy.StateExitRadiusFactor, 1e-6f);
            Assert.AreEqual(so.FindProperty("_stalkStandoffFraction").floatValue, ScriptedPolicy.StalkStandoffFraction, 1e-6f);
        }

        [Test]
        public void P0_CallsTheGamesOwnFunctions_NotCopiesOfThem()
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Scripts/Balance/ScriptedPolicy.cs"));
            foreach (var call in new[]
            {
                "BotTactics.MustBankNow(", "BotTactics.ReturnThreshold(", "BotTactics.ResolvePhase(", "BotTactics.NextMarkStep(", "BotTactics.CastPlan(",
                "BotTactics.PileCost(", "BotTactics.TargetPriority(", "BotTactics.ShouldStealGuard(", "BotTactics.ScoreCastCandidate(",
            })
                StringAssert.Contains(call, source, "P0 must call the game's own decision, not restate it.");
            StringAssert.DoesNotContain("BotSightRules", source, "Sight goes through MatchSight, the only caller of the rule.");
        }
    }
}
