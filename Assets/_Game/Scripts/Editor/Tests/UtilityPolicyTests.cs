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
    /// P1, the utility chooser. The decisions are asserted on hand-built views (every number the test's own); the last tests run it over the
    /// shipped kits and read its source. No rate is asserted: P1 is only shown to play legally, to see rivals through the sight rule and to
    /// settle ties by a draw, never by seat order.
    /// </summary>
    public sealed class UtilityPolicyTests
    {
        private const float Half = 20f;
        private const float Tick = 1f / 30f;

        private OracleMapSO _map;

        [SetUp]
        public void SetUp() => _map = MatchTestKit.OpenMap(Half);

        [TearDown]
        public void TearDown() => MatchTestKit.Release();

        private static MatchAbility Steal(float radius, float amount, float cooldown)
        {
            var a = MatchTestKit.Steal(radius, amount, cooldown);
            a.IsStealer = true;
            a.Reach = radius;
            return a;
        }

        private static MatchAbility Shield()
        {
            var a = MatchTestKit.Shield(1f, 0.5f, 2f, 8f);
            a.IsStealShield = true;
            a.Reach = float.PositiveInfinity;
            return a;
        }

        private static MatchKit Kit(string label, params MatchAbility[] abilities) => MatchTestKit.Fighter(label, 6f, abilities);

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

        private static MatchIntent Think(MatchView view, int seat = 0, PolicyParams? p = null, ulong seed = 1UL)
        {
            var policy = UtilityPolicy.Factory(seat, p ?? PolicyParams.Default);
            var rng = MatchRng.Create(seed, (ulong)seat);
            return policy.Think(view, ref rng);
        }

        private Vector2 Home(int seat) => _map.Places[OracleMapSO.BaseIndex(seat)].Pos;

        private static MatchKit[] Four(MatchKit me, MatchKit rival) => new[] { me, rival, null, null };

        [Test]
        public void StunThenSteal_OnALoadedRivalInRange()
        {
            // Slot 0 Steal (reach 5), slot 1 Stun (reach 5). No pile has food, so stealing is the only plan.
            var me = Kit("thief", Steal(5f, 5f, 3f), MatchTestKit.Stun(5f, 1.5f, 6f));
            var view = View(Four(me, Kit("farmer")));
            view.Seats[0].Pos = Vector2.zero;
            view.Seats[1].Pos = new Vector2(4f, 0f);
            view.Seats[1].Cargo = 10f;

            Assert.AreEqual(1, Think(view).CastSlot, "A loaded rival near the edge of the reach is stunned first: a stunned rival cannot shell up or walk out of it.");

            // The stun landed: it is on cooldown and the rival cannot move or cast.
            view.Seats[0].CooldownLeft[2] = 6f;
            view.Seats[1].Stunned = true;
            Assert.AreEqual(0, Think(view).CastSlot, "Then the steal, on the very next think.");
        }

        [Test]
        public void Steal_GoesFirst_WhenTheRivalCanShellUp()
        {
            var me = Kit("thief", Steal(5f, 5f, 3f), MatchTestKit.Stun(5f, 1.5f, 6f));
            var view = View(Four(me, Kit("farmer", Shield())));
            view.Seats[0].Pos = Vector2.zero;
            view.Seats[1].Pos = new Vector2(4f, 0f);
            view.Seats[1].Cargo = 10f;

            Assert.AreEqual(0, Think(view).CastSlot, "A shell is faster than a stun-then-steal: the steal is cast at once.");
        }

        [Test]
        public void ShellsUp_OnAnIncomingStealer()
        {
            var me = Kit("me", Shield());
            var view = View(Four(me, Kit("thief", Steal(5f, 5f, 3f))));
            view.Seats[0].Pos = Vector2.zero;
            view.Seats[0].Cargo = 10f;
            view.Seats[1].Pos = new Vector2(6f, 0f);   // outside the reach, inside a think's walk of it

            Assert.AreEqual(0, Think(view).CastSlot, "A loaded chicken shells up before the stealer is in reach.");

            view.Seats[1].CooldownLeft[1] = 3f;   // the Snatch is on cooldown: no threat
            Assert.AreEqual(-1, Think(view).CastSlot, "A stealer that cannot steal for seconds is no reason to shell up.");

            view.Seats[1].CooldownLeft[1] = 0f;
            view.Seats[0].Cargo = 1f;
            Assert.AreEqual(-1, Think(view).CastSlot, "Nothing worth guarding is carried.");
        }

        [Test]
        public void Shoves_ARivalMidCastOffAContestedPile()
        {
            int pile = OracleMapSO.ContestedIndex(1);
            var view = View(Four(Kit("me", MatchTestKit.Shove(5f, 10f, 5f)), Kit("rival")));
            view.PileFood[pile] = 10f;
            view.Seats[0].Pos = _map.Places[pile].Pos;
            view.Seats[1].Pos = _map.Places[pile].Pos + new Vector2(1.5f, 0f);
            view.Seats[1].Pecking = true;

            var intent = Think(view);
            Assert.AreEqual(0, intent.CastSlot, "The rival is mid-Peck beside me: a shove cuts it.");
            Assert.AreEqual(pile, intent.PeckPile, "And I keep working the pile.");

            view.Seats[1].Pecking = false;
            Assert.AreEqual(-1, Think(view).CastSlot, "A rival that is not pecking is not worth a shove.");

            view.Seats[1].Pecking = true;
            view.Seats[0].Pecking = true;
            Assert.AreEqual(-1, Think(view).CastSlot, "A cast would cut my own Peck: not while pecking.");

            view.Seats[0].Pecking = false;
            view.Seats[0].CooldownLeft[1] = 2f;
            Assert.AreEqual(-1, Think(view).CastSlot, "A shove on cooldown is not cast.");
        }

        [Test]
        public void Collect_ATie_IsDrawnNotSettledBySeatOrder()
        {
            int a = OracleMapSO.ContestedIndex(0), b = OracleMapSO.ContestedIndex(3);
            var view = View(Four(Kit("me"), Kit("rival")));
            view.PileFood[a] = 10f;
            view.PileFood[b] = 10f;
            view.Seats[0].Pos = Home(0);   // on the line of symmetry between its two contested piles
            view.Seats[1].Pos = Home(2);

            int pickedA = 0, pickedB = 0;
            for (ulong seed = 1; seed <= 64; seed++)
            {
                int pile = Think(view, 0, null, seed).PeckPile;
                if (pile == a) pickedA++;
                else if (pile == b) pickedB++;
                else Assert.Fail($"Picked pile {pile}, neither of the two tied piles.");
            }
            Assert.Greater(pickedA, 0);
            Assert.Greater(pickedB, 0, "A tie is drawn from the seat's stream, so both piles come up; an order by seat or index would pick one every time.");

            // A step toward one pile breaks the tie by far more than the tie's width.
            view.Seats[0].Pos = Home(0) + (_map.Places[a].Pos - Home(0)).normalized * 2f;
            for (ulong seed = 1; seed <= 16; seed++) Assert.AreEqual(a, Think(view, 0, null, seed).PeckPile);
        }

        [Test]
        public void Mirror_ARotatedView_GivesTheRotatedIntent()
        {
            // The open map is four-fold symmetric. Turn the whole table one seat (positions a quarter turn, piles to the next seat's, the seat's
            // number up by one): a policy with no seat-relative rule answers with the turned intent.
            var kit = Kit("k", MatchTestKit.Stun(5f, 1f, 6f), Steal(5f, 4f, 3f));
            var kits = new[] { kit, kit, kit, kit };
            var v0 = View(kits);
            v0.PileFood[OracleMapSO.PersonalIndex(0)] = 3f;
            v0.PileFood[OracleMapSO.ContestedIndex(0)] = 9f;
            v0.PileFood[OracleMapSO.ContestedIndex(2)] = 6f;
            v0.PileFood[OracleMapSO.CentreIndex] = 14f;
            v0.Seats[0].Pos = Home(0) + new Vector2(-3f, 1f);
            v0.Seats[0].Cargo = 4f;
            v0.Seats[1].Pos = new Vector2(-9f, 6f);
            v0.Seats[1].Cargo = 12f;
            v0.Seats[2].Pos = new Vector2(2f, -12f);
            v0.Seats[3].Pos = new Vector2(10f, 3f);
            v0.Seats[3].Cargo = 7f;

            var v1 = View(kits);
            for (int p = 0; p < OracleMapSO.PileCount; p++) v1.PileFood[Turn(p)] = v0.PileFood[p];
            for (int s = 0; s < 4; s++)
            {
                var src = v0.Seats[s];
                src.Pos = Rotate(src.Pos);
                src.CooldownLeft = new float[3];
                v1.Seats[(s + 1) % 4] = src;
            }

            var i0 = Think(v0, 0);
            var i1 = Think(v1, 1);

            Assert.AreEqual(i0.PeckPile < 0 ? -1 : Turn(i0.PeckPile), i1.PeckPile, "The pile it works is the turned pile.");
            Assert.AreEqual(i0.CastSlot, i1.CastSlot);
            Assert.AreEqual(i0.CastTarget < 0 ? -1 : (i0.CastTarget + 1) % 4, i1.CastTarget);
            Assert.Less(Vector2.Distance(Rotate(i0.Goal), i1.Goal), 1e-3f, "And it walks to the turned point.");
        }

        private static Vector2 Rotate(Vector2 v) => new Vector2(-v.y, v.x);

        private static int Turn(int pile)
        {
            if (pile == OracleMapSO.CentreIndex) return pile;
            if (pile < OracleMapSO.ContestedIndex(0)) return OracleMapSO.PersonalIndex((pile - OracleMapSO.PersonalIndex(0) + 1) % 4);
            return OracleMapSO.ContestedIndex((pile - OracleMapSO.ContestedIndex(0) + 1) % 4);
        }

        [Test]
        public void P1_HasNoPerClassProfile()
        {
            var abilities = new[] { Steal(5f, 4f, 3f), MatchTestKit.Stun(5f, 1.5f, 6f), Shield() };
            var warrior = Kit("a", abilities);
            warrior.Class = ChickenClass.Warrior;
            warrior.Bot = new BotProfile(BotStrategy.Brawler, 0.85f, 0.60f, 5.5f, 11f, 0.10f, 9f, 6f, 0f, 0.35f, 0.28f);
            var assassin = Kit("a", abilities);
            assassin.Class = ChickenClass.Assassin;
            assassin.Bot = new BotProfile(BotStrategy.Predator, 0.3f, 0.2f, 9f, 3f, 0.9f, 2f, 0f, 1f, 0.9f, 0.1f);

            foreach (var other in new[] { Kit("rival"), Kit("rival", Steal(5f, 4f, 3f)) })
            {
                var viewA = View(Four(warrior, other));
                var viewB = View(Four(assassin, other));
                foreach (var v in new[] { viewA, viewB })
                {
                    v.PileFood[OracleMapSO.PersonalIndex(0)] = 10f;
                    v.Seats[0].Cargo = 10f;
                    v.Seats[0].Pos = new Vector2(1f, 1f);
                    v.Seats[1].Pos = new Vector2(5f, 1f);
                    v.Seats[1].Cargo = 8f;
                }
                Assert.IsTrue(Think(viewA).SameAs(Think(viewB)), "The same kit, the same view, another class: the same intent. P1 plays the kit.");
            }
        }

        [Test]
        public void ARivalGivenUp_IsNotChasedOrCastAt()
        {
            var me = Kit("thief", Steal(5f, 5f, 3f));
            var view = View(Four(me, Kit("farmer")));
            view.Seats[0].Pos = Vector2.zero;
            view.Seats[1].Pos = new Vector2(30f, 0f);
            view.Seats[1].Cargo = 10f;
            Assert.AreEqual(view.Seats[1].Pos, Think(view).Goal, "A loaded rival the thief believes is there is walked up to.");

            view.Seats[1].Known = false;   // the sight rule dropped it: its position means nothing
            view.Seats[1].Pos = new Vector2(3f, 0f);
            var intent = Think(view);
            Assert.AreNotEqual(view.Seats[1].Pos, intent.Goal, "A rival that is not known is not walked to.");
            Assert.AreEqual(-1, intent.CastSlot, "Nor cast at.");
        }

        [Test]
        public void Bank_WhenTheClockLeavesJustTimeToWalkHome()
        {
            var view = View(Four(Kit("me"), Kit("rival")), timeLeft: 2f);
            view.Seats[0].Pos = new Vector2(-10f, 0f);
            view.Seats[0].Cargo = 8f;
            var intent = Think(view);
            Assert.AreEqual(Home(0), intent.Goal);
            Assert.AreEqual(-1, intent.PeckPile);
        }

        [Test]
        public void MarkLoop_PressesTheMarkOnlyWhenTheStunWillBeBack()
        {
            var mark = MatchTestKit.DeathMark(3f, 4f, 4f, 20f, 4f, 4f);
            var stun = MatchTestKit.Stun(5f, 1.3f, 7f);
            var view = View(Four(Kit("spoiler", mark, stun), Kit("victim")));
            view.Seats[0].Pos = Vector2.zero;
            view.Seats[1].Pos = new Vector2(3f, 0f);
            view.Seats[1].Cargo = 6f;

            view.Seats[0].CooldownLeft[2] = 2f;   // the stun is back before the mark arms (3 s)
            Assert.AreEqual(0, Think(view).CastSlot, "Press the mark.");

            view.Seats[0].CooldownLeft[2] = 9f;   // it would not be
            Assert.AreEqual(-1, Think(view).CastSlot, "No point in a mark the stun cannot finish.");
        }

        [Test]
        public void MarkLoop_KeepsTheStunBack_WhenTheMarkIsAboutToBeReady()
        {
            var steal = Steal(5f, 5f, 3f);
            var stun = MatchTestKit.Stun(5f, 1.3f, 7f);
            var mark = MatchTestKit.DeathMark(3f, 4f, 4f, 20f, 4f, 4f);
            var view = View(Four(Kit("spoiler", steal, stun, mark), Kit("victim")));
            view.Seats[0].Pos = Vector2.zero;
            view.Seats[1].Pos = new Vector2(4f, 0f);
            view.Seats[1].Cargo = 10f;

            view.Seats[0].CooldownLeft[3] = 30f;   // the mark is far off: the stun is free for a stun-then-steal
            Assert.AreEqual(1, Think(view).CastSlot);

            view.Seats[0].CooldownLeft[3] = 3f;    // the mark is back before the stun would be: the stun is the kill's
            Assert.AreEqual(0, Think(view).CastSlot, "The steal goes alone; the stun waits.");
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
        public void P1_NeverHasARejectedIntent()
        {
            var context = MatchContext.From(Inputs(), OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius());
            foreach (var population in new[] { PolicyPopulation.P1, PolicyPopulation.Mixed, PolicyPopulation.Split })
                foreach (var design in new[] { MatchDesignKind.Mixed, MatchDesignKind.OnePerClass, MatchDesignKind.SeatCheck })
                {
                    var run = MatchRunner.Run(context, new MatchRunSpec { Design = design, Replicates = 1, Population = population, RunSeed = 11UL, Threads = 4, CellLimit = 20 });
                    for (int i = 0; i < run.Results.Length; i++)
                        Assert.AreEqual(0, run.Results[i].RejectedIntents, $"{population} {design} cell {i}: an intent the rules refuse is a policy bug (casts lost to the reaction delay are counted apart).");
                }
        }

        [Test]
        public void P1_ReadsRivalsOnlyFromTheView_AndHasNoClassOrProfile()
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Scripts/Balance/UtilityPolicy.cs"));
            foreach (var forbidden in new[] { "BotSightRules", "Kit.Bot", ".Bot.", "BotProfile", "ProfileFor", ".Class" })
                StringAssert.DoesNotContain(forbidden, source, "P1 plays the kit; rivals come from the view, which the sight rule fills.");
        }

        [Test]
        public void P1_Plays_SameSeedSameResult()
        {
            var context = MatchContext.From(Inputs(), OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius());
            var spec = new MatchRunSpec { Design = MatchDesignKind.Mixed, Replicates = 1, Population = PolicyPopulation.P1, RunSeed = 3UL, Threads = 1, CellLimit = 12 };
            var a = MatchRunner.Run(context, spec);
            var b = MatchRunner.Run(context, spec);
            for (int i = 0; i < a.Results.Length; i++)
            {
                Assert.AreEqual(a.Results[i].Winner, b.Results[i].Winner, $"cell {i}");
                CollectionAssert.AreEqual(a.Results[i].Banked, b.Results[i].Banked, $"cell {i}");
            }
        }
    }
}
