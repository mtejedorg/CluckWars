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
                    Capacity = kits[s]?.Farm.CargoCapacity ?? 0, Present = kits[s] != null, Seen = true, Known = true, MarkTarget = -1, MarkedBy = -1,
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

        private static MatchKit WarriorKit(params MatchAbility[] abilities)
        {
            var kit = MatchTestKit.Fighter("warrior", 6f, abilities, capacity: 20);
            kit.Bot = BotTactics.ProfileFor(ChickenClass.Warrior);
            return kit;
        }

        [Test]
        public void TheWarrior_GoesHomeAtTheReturnAmount_NotAtAFractionOfABigBeak()
        {
            var view = View(Four(WarriorKit(), Kit("rival")));
            view.Seats[0].Pos = Vector2.zero;
            view.Seats[1].Pos = new Vector2(30f, 0f);
            float amount = view.Kits[0].Bot.ReturnAmount;
            Assert.Less(amount, view.Kits[0].Farm.CargoCapacity * view.Kits[0].Bot.ReturnThreshold, "the set-up needs a beak where the amount is the earlier rule");

            view.Seats[0].Cargo = amount - 1f;
            Assert.AreNotEqual(Home(0), Think(view).Goal, "below the amount it keeps foraging");

            view.Seats[0].Cargo = amount;
            Assert.AreEqual(Home(0), Think(view).Goal, "at the amount it goes home");
        }

        [Test]
        public void TheWarrior_OnTheWayHome_StealsFromALoadedRivalInReach_WithoutLeavingItsPath()
        {
            var stun = Make(MatchTestKit.Stun(5f, 2f, 6f), BotRole.Control, 5f, true);
            var steal = Make(MatchTestKit.Steal(5f, 4f, 3f), BotRole.Steal, 5f, true);
            var kits = new[] { WarriorKit(stun, steal), Kit("empty"), Kit("loaded"), null };
            var view = View(kits);
            var policy = ScriptedPolicy.Factory(0, PolicyParams.Default);
            var rng = MatchRng.Create(1UL, 0UL);

            view.Seats[0].Pos = Vector2.zero;
            view.Seats[0].Cargo = view.Kits[0].Bot.ReturnAmount;
            view.Seats[1].Pos = new Vector2(30f, 0f);
            view.Seats[2].Pos = new Vector2(30f, 5f);
            Assert.AreEqual(Home(0), policy.Think(view, ref rng).Goal, "loaded to the amount: home");

            // Now an empty rival is the nearest one and a loaded one is in reach behind it.
            view.Seats[1].Pos = new Vector2(2f, 0f);
            view.Seats[2].Pos = new Vector2(0f, 3f);
            view.Seats[2].Cargo = 8f;
            var passing = policy.Think(view, ref rng);
            Assert.AreEqual(1, passing.CastSlot, "Steal goes first, ahead of the guard overlay's stun");
            Assert.AreEqual(Home(0), passing.Goal, "no detour: the goal is still home");

            view.Seats[0].CooldownLeft[2] = 3f;   // Steal on cooldown
            Assert.AreNotEqual(1, policy.Think(view, ref rng).CastSlot, "a Steal on cooldown is not cast");

            view.Seats[0].CooldownLeft[2] = 0f;
            view.Seats[2].Pos = new Vector2(0f, 9f);   // out of Steal's reach
            Assert.AreNotEqual(1, policy.Think(view, ref rng).CastSlot, "a rival out of reach is not chased");

            view.Seats[2].Pos = new Vector2(0f, 3f);
            view.Seats[0].Cargo = view.Kits[0].Farm.CargoCapacity;   // beak full
            Assert.AreNotEqual(1, policy.Think(view, ref rng).CastSlot, "no room: nothing to take");
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
                "BotTactics.LoadedToBank(", "BotLootRules.BankNow(", "BotLootRules.Pursue(", "BotLootRules.HoldSteal(", "BotLootRules.BehindWall(", "BotLootRules.Retreat(",
                "BotHitStealRules.Gain(", "BotHitStealRules.CastForFood(", "BotHitStealRules.PickHitter(", "BotHitStealRules.WorthHunting(",
                "BotEnforcerRules.FreshLeft(", "BotEnforcerRules.HitFresh(", "BotEnforcerRules.PickBuilder(", "BotEnforcerRules.Cash(", "BotEnforcerRules.StealGain(",
                "BotEnforcerRules.Fight(", "BotEngageRules.MayEngage(", "BotEngageRules.HitPays(",
            })
                StringAssert.Contains(call, source, "P0 must call the game's own decision, not restate it.");

            // And the game's bot calls the same ones (BL-217 slice A): P0 mirrors the bot's priority list, so a drift is a bot that no longer plays what P0 measures.
            string bot = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Scripts/Gameplay/BotController.cs"));
            foreach (var call in new[] { "BotTactics.LoadedToBank(", "BotLootRules.BankNow(", "BotLootRules.Pursue(", "BotLootRules.HoldSteal(", "BotLootRules.BehindWall(", "BotLootRules.Retreat(",
                "BotEngageRules.MayEngage(", "BotEngageRules.HitPays(",
                // The Warrior kits (BL-226 G6): the bot decides the Bully's hits and the Enforcer's debts with the functions P0 calls.
                "BotHitStealRules.Gain(", "BotHitStealRules.CastForFood(", "BotHitStealRules.PickHitter(", "BotHitStealRules.WorthHunting(",
                "BotEnforcerRules.FreshLeft(", "BotEnforcerRules.HitFresh(", "BotEnforcerRules.PickBuilder(", "BotEnforcerRules.Cash(", "BotEnforcerRules.StealGain(",
                "BotEnforcerRules.Fight(" })
                StringAssert.Contains(call, bot, "the game's bot must call the decision P0 calls.");
            StringAssert.DoesNotContain("BotSightRules", source, "Sight goes through MatchSight, the only caller of the rule.");
            StringAssert.DoesNotContain("BotBreakRules", source, "the Peck break is gone from the design.");
        }

        // A Warrior kit under test: a profile that neither hunts, engages nor guards, so only the kit's own rule can make it cast or press.
        private static readonly BotProfile QuietWarrior = new BotProfile(BotStrategy.Brawler, 0.85f, 0.60f, 5.5f, 0f, 0.10f, 0f, 0f, 0f, 0.35f, 0.28f);

        [Test]
        public void P0Bully_CastsStunAtALoadedRival_NotAtAnEmptyOne()
        {
            var stun = Make(MatchTestKit.Stun(5f, 1.3f, 5.25f), BotRole.Control, 5f, true);
            var bully = MatchTestKit.Fighter("bully", 6f, new[] { stun }, capacity: 20);
            bully.Bot = QuietWarrior;
            bully.Passive.HitSteal = new HitStealFacts { Stun = 2f, Shove = 2f, LockSeconds = 3f };
            var view = View(Four(bully, Kit("rival")));
            view.Seats[0].Pos = new Vector2(30f, 30f);   // away from every pile
            view.Seats[1].Pos = new Vector2(32f, 30f);
            for (int p = 0; p < view.PileFood.Length; p++) view.PileFood[p] = 0f;

            view.Seats[1].Cargo = 6f;
            Assert.AreEqual(0, Think(view).CastSlot, "a loaded rival in reach: a hit that takes food");

            view.Seats[1].Cargo = 0f;
            Assert.AreEqual(-1, Think(view).CastSlot, "an empty rival gives nothing: no cast");

            view.Seats[1].Cargo = 6f;
            view.Seats[0].HitLockLeft = new float[4];
            view.Seats[0].HitLockLeft[1] = 2f;
            Assert.AreEqual(-1, Think(view).CastSlot, "a locked rival gives nothing either");

        }

        // A Brawler (the Warrior's strategy) that hunts and engages, with the kit given: the defect case of BL-224.
        private static MatchKit Brawler(params MatchAbility[] abilities)
        {
            var kit = MatchTestKit.Fighter("brawler", 6f, abilities, capacity: 20);
            kit.Bot = Profile;
            return kit;
        }

        private MatchIntent BrawlerThink(MatchKit kit)
        {
            var view = View(Four(kit, Kit("rival")));
            view.Seats[0].Pos = new Vector2(30f, 30f);
            view.Seats[1].Pos = new Vector2(38f, 30f);   // 8 m: inside the engage band of the profile
            view.Seats[1].Cargo = 0f;                     // an empty rival: only the engage band applies
            return Think(view);
        }

        [Test]
        public void P0Brawler_WithNoStealAndNoHitThatPays_ForagesInsteadOfChasing()
        {
            var stun = Make(MatchTestKit.Stun(5f, 1.3f, 7f), BotRole.Control, 5f, true);
            var shove = Make(MatchTestKit.Shove(5f, 5f, 5f), BotRole.Control, 5f, true);
            var intent = BrawlerThink(Brawler(stun, shove));
            Assert.GreaterOrEqual(intent.PeckPile, 0, "nothing to take from a rival: it goes to a pile");
            Assert.AreNotEqual(new Vector2(38f, 30f), intent.Goal);
        }

        [Test]
        public void P0Brawler_WithAStealOrAHitThatPays_StillChases()
        {
            var stun = Make(MatchTestKit.Stun(5f, 1.3f, 7f), BotRole.Control, 5f, true);
            var steal = Make(MatchTestKit.Steal(5f, 5f, 4f), BotRole.Steal, 5f, true);
            var withSteal = BrawlerThink(Brawler(stun, steal));
            Assert.AreEqual(-1, withSteal.PeckPile, "a Steal in the kit: the shipped Bully and Relentless behave exactly as before");
            Assert.AreEqual(new Vector2(38f, 30f), withSteal.Goal);

            var hitKit = Brawler(stun);
            hitKit.Passive.HitSteal = new HitStealFacts { Stun = 2f, LockSeconds = 3f };
            hitKit.Passive.Bite = BiteFacts.None;
            var view = View(Four(hitKit, Kit("rival")));
            view.Seats[0].Pos = new Vector2(30f, 30f);
            view.Seats[1].Pos = new Vector2(38f, 30f);
            view.Seats[1].Cargo = 20f;
            Assert.AreEqual(-1, Think(view).PeckPile, "an active hit-steal on a held Stun pays: it chases a loaded rival");
        }

        // An Enforcer with the bite on: Stun (slot 0, 7 s) and Steal (slot 1, 5 m, 5 food); the quiet profile never engages, so only the kit's own rule casts.
        private static MatchKit Enforcer(float peckCooldown = 0.71f, BotProfile? profile = null)
        {
            var stun = Make(MatchTestKit.Stun(5f, 1.3f, 7f), BotRole.Control, 5f, true);
            var steal = Make(MatchTestKit.Steal(5f, 5f, 4f), BotRole.Steal, 5f, true);
            var kit = MatchTestKit.Fighter("enforcer", 6f, new[] { stun, steal }, peckAmount: 3f, peckCooldown: peckCooldown, castSeconds: 0.4f, capacity: 20);
            kit.Bot = profile ?? QuietWarrior;
            kit.Passive.Bite = new BiteFacts { FreshSeconds = 1f };
            kit.Passive.Debt = new DebtFacts { IsEnforcer = true, Steal = 2f, Cap = 3, Seconds = 6f };
            return kit;
        }

        private static MatchView AwayFromPiles(MatchView view)
        {
            view.Seats[0].Pos = new Vector2(30f, 30f);
            view.Seats[1].Pos = new Vector2(32f, 30f);
            for (int p = 0; p < view.PileFood.Length; p++) view.PileFood[p] = 0f;
            view.Seats[0].DebtOn = new int[4];
            view.Seats[0].DebtLeft = new float[4];
            return view;
        }

        [Test]
        public void P0Enforcer_HitsAFreshRivalInReach_NotAStaleOne()
        {
            var view = AwayFromPiles(View(Four(Enforcer(), Kit("rival"))));
            view.Seats[1].Pecking = true;
            view.Seats[1].PeckCastLeft = 0.4f;
            Assert.AreEqual(0, Think(view).CastSlot, "a rival mid-cast in reach: the builder (the Stun is the only one ready)");

            view.Seats[1].Pecking = false;
            view.Seats[1].PeckCastLeft = 0f;
            view.Seats[1].LandedWithFood = true;
            view.Seats[1].SinceLanded = 0.5f;
            Assert.AreEqual(0, Think(view).CastSlot, "landed half a second ago with food: still fresh");

            view.Seats[1].SinceLanded = 1.5f;
            Assert.AreEqual(-1, Think(view).CastSlot, "past the window: nothing to bite, no cast");

            view.Seats[1].SinceLanded = 0.5f;
            view.Seats[1].FreshBitten = true;
            Assert.AreEqual(-1, Think(view).CastSlot, "the state was already bitten");

            view.Seats[1].FreshBitten = false;
            view.Seats[1].Shelled = true;
            Assert.AreEqual(-1, Think(view).CastSlot, "an egg refuses the hit");

            // With the bite off the Enforcer is a plain kit: the same fresh rival gets no cast.
            var off = Enforcer();
            off.Passive.Bite = BiteFacts.None;
            off.Passive.Debt = new DebtFacts { IsEnforcer = true, Steal = 0f, Cap = 3, Seconds = 6f };
            var offView = AwayFromPiles(View(Four(off, Kit("rival"))));
            offView.Seats[1].Pecking = true;
            offView.Seats[1].PeckCastLeft = 0.4f;
            Assert.AreEqual(-1, Think(offView).CastSlot, "shipped off: the plain kit by the ordinary rules");
        }

        [Test]
        public void P0Enforcer_CashesAtTheCap_AndBeforeTheDecay()
        {
            var view = AwayFromPiles(View(Four(Enforcer(), Kit("rival"))));
            view.Seats[1].Cargo = 20f;
            view.Seats[0].DebtOn[1] = 3;
            view.Seats[0].DebtLeft[1] = 5f;
            Assert.AreEqual(1, Think(view).CastSlot, "at the cap: cash the Steal");

            view.Seats[0].DebtOn[1] = 2;
            Assert.AreEqual(-1, Think(view).CastSlot, "a builder is ready before the count decays: build first, not cash");

            view.Seats[0].CooldownLeft[1] = 6f;   // the Stun is back only after the count decays
            Assert.AreEqual(1, Think(view).CastSlot, "no builder is back in time: cash now");

            view.Seats[0].DebtOn[1] = 0;
            Assert.AreEqual(-1, Think(view).CastSlot, "count 0 is the ordinary Steal rule, and the quiet profile never engages");
        }

        [Test]
        public void P0Enforcer_KeepsPeckingWhenNoFightBeatsThePeck()
        {
            // The same rival, the same layout: at the shipped Peck the pile wins; with a Peck slowed to 5 s the fight pays and the Enforcer goes for him.
            MatchIntent Run(float peckCooldown)
            {
                var view = View(Four(Enforcer(peckCooldown, Profile), Kit("rival")));
                view.Seats[0].Pos = new Vector2(30f, 30f);
                view.Seats[1].Pos = new Vector2(38f, 30f);
                view.Seats[1].Cargo = 2f;
                view.Seats[0].DebtOn = new int[4];
                view.Seats[0].DebtLeft = new float[4];
                return Think(view);
            }
            Assert.GreaterOrEqual(Run(0.71f).PeckPile, 0, "a 2-food take is worth less than the Peck's rate: collect");
            var fight = Run(5f);
            Assert.AreEqual(-1, fight.PeckPile, "the slow Peck makes the fight worth it");
            Assert.AreEqual(new Vector2(38f, 30f), fight.Goal);
        }

    }
}
