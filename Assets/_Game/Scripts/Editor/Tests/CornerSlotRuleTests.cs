using System.Collections.Generic;
using System.Linq;
using CluckWars.Gameplay;
using CluckWars.Services;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// CW-233 (session 20261022-100224, shared4): two joiners spawned on the SAME home corner because the
    /// preferred slot was the player's index in its own, timing-dependent view of <c>runner.ActivePlayers</c>.
    /// The slot now comes from the PlayerId (<see cref="CornerSlotRules"/>).
    /// </summary>
    public sealed class CornerSlotRuleTests
    {
        private const string BootstrapperPath = "Assets/_Game/Scripts/Gameplay/MatchBootstrapper.cs";

        /// <summary>The corner permutation of that run (session code B8P7WK): "Corner permutation [2,1,0,3]".</summary>
        private static readonly int[] RunPermutation = { 2, 1, 0, 3 };

        private static int NewRule(int playerId, int[] perm, int occupiedMask) =>
            CornerSlotRules.Resolve(CornerSlotRules.PreferredSlot(playerId), perm, occupiedMask);

        /// <summary>The pre-CW-233 rule, inlined: index of the player in the PlayerId-sorted roster this peer had seen.</summary>
        private static int OldRosterIndexRule(int playerId, IEnumerable<int> rosterSeen, int[] perm, int occupiedMask)
        {
            var sorted = rosterSeen.OrderBy(id => id).ToList();
            return CornerSlotRules.Resolve(System.Math.Max(0, sorted.IndexOf(playerId)), perm, occupiedMask);
        }

        private static IEnumerable<int[]> AllOrders(int[] items)
        {
            if (items.Length <= 1) { yield return items; yield break; }
            for (int i = 0; i < items.Length; i++)
            {
                var rest = items.Where((_, k) => k != i).ToArray();
                foreach (var tail in AllOrders(rest))
                    yield return new[] { items[i] }.Concat(tail).ToArray();
            }
        }

        private static readonly int[][] AllCornerPermutations = AllOrders(new[] { 0, 1, 2, 3 }).ToArray();

        [Test]
        public void PreferredSlot_IsPlayerIdMinusOne_WrappingForRejoins()
        {
            Assert.AreEqual(new[] { 0, 1, 2, 3, 0, 1, 2, 3, 0 },
                Enumerable.Range(1, 9).Select(CornerSlotRules.PreferredSlot).ToArray());
            Assert.AreEqual(3, CornerSlotRules.PreferredSlot(0), "never negative");
        }

        [Test]
        public void ThisRunsCase_P3SeesOneAndThree_P2SeesOneTwoThree_GetDifferentCorners()
        {
            // P1 (host) already stamped corner perm[0] = 2 and replicated; P2 and P3 joined in the same instant, so
            // neither saw the other's chicken. P3's join callback saw roster {1,3}, P2's saw {1,2,3}.
            int seen = 1 << RunPermutation[0];
            int p3 = NewRule(3, RunPermutation, seen);
            int p2 = NewRule(2, RunPermutation, seen);
            Assert.AreNotEqual(p2, p3, "two joiners racing must not share a corner");
            Assert.AreEqual(1, p2, "P2 = slot 1 = corner 1");
            Assert.AreEqual(0, p3, "P3 = slot 2 = corner 0");

            // The old rule reproduces the bug (both on corner 1), so this case discriminates.
            Assert.AreEqual(1, OldRosterIndexRule(3, new[] { 1, 3 }, RunPermutation, seen));
            Assert.AreEqual(1, OldRosterIndexRule(2, new[] { 1, 2, 3 }, RunPermutation, seen));
        }

        [Test]
        public void FreshSession_AllFourCornersDistinct_ForEveryArrivalOrder_AndEveryPermutation()
        {
            foreach (var perm in AllCornerPermutations)
            foreach (var order in AllOrders(new[] { 1, 2, 3, 4 }))
            {
                // Worst case: every join lands before any chicken has replicated (each peer sees nothing occupied).
                var simultaneous = order.Select(id => NewRule(id, perm, 0)).ToArray();
                CollectionAssert.AllItemsAreUnique(simultaneous, $"simultaneous joins, perm [{string.Join(",", perm)}] order [{string.Join(",", order)}]");

                // Ordered case: each joiner sees the chickens of everyone who arrived before it.
                int mask = 0;
                var sequential = new List<int>();
                foreach (int id in order)
                {
                    int c = NewRule(id, perm, mask);
                    Assert.GreaterOrEqual(c, 0);
                    sequential.Add(c);
                    mask |= 1 << c;
                }
                CollectionAssert.AllItemsAreUnique(sequential, $"sequential joins, perm [{string.Join(",", perm)}] order [{string.Join(",", order)}]");
                CollectionAssert.AreEqual(simultaneous, sequential, "timing must not change anyone's corner in a fresh session");
            }
        }

        [Test]
        public void OldRule_CollidesForSomeArrivalOrder()
        {
            // Guard for the guard: the matrix above would have caught the old rule (it is not vacuous).
            bool collided = false;
            foreach (var order in AllOrders(new[] { 1, 2, 3, 4 }))
            {
                var corners = new List<int>();
                for (int k = 0; k < order.Length; k++)
                    corners.Add(OldRosterIndexRule(order[k], order.Take(k + 1), RunPermutation, 0));
                if (corners.Distinct().Count() < corners.Count) { collided = true; break; }
            }
            Assert.IsTrue(collided);
        }

        [Test]
        public void Rejoin_PlayerIdFivePlus_LandsOnAFreeCorner_WhenOneExists()
        {
            foreach (var perm in AllCornerPermutations)
            {
                // P1..P4 spawned, P2 left (its corner perm[1] is free again), P5 rejoins: slot 0 is taken, scan finds perm[1].
                int mask = 0xF & ~(1 << perm[1]);
                Assert.AreEqual(perm[1], NewRule(5, perm, mask));

                for (int occupied = 0; occupied < 0xF; occupied++)
                for (int id = 5; id <= 12; id++)
                {
                    int c = NewRule(id, perm, occupied);
                    Assert.GreaterOrEqual(c, 0, $"P{id} mask {occupied}: a free corner exists");
                    Assert.AreEqual(0, occupied & (1 << c), $"P{id} mask {occupied}: picked occupied corner {c}");
                }
                Assert.AreEqual(-1, NewRule(5, perm, 0xF), "all four taken: spawn refused");
            }
        }

        [Test]
        public void NullPermutation_IsIdentity()
        {
            Assert.AreEqual(2, CornerSlotRules.Resolve(2, null, 0));
            Assert.AreEqual(3, CornerSlotRules.Resolve(2, null, 1 << 2));
        }

        [Test]
        public void HostLobbySeats_MatchTheSpawnRule_InAFreshSession()
        {
            // The lobby colours seat i with slot i; in a fresh session seat i is PlayerId i + 1.
            foreach (string code in new[] { "B8P7WK", "ABC123", "cluck-lan" })
            {
                var perm = CornerAssignment.Permutation(CornerAssignment.SeedFor(SessionMode.Host, code, 0));
                for (int seat = 0; seat < 4; seat++)
                    Assert.AreEqual(CornerAssignment.LobbySeatCorner(SessionMode.Host, seat, perm), NewRule(seat + 1, perm, 0), $"{code} seat {seat}");
            }
        }

        [Test]
        public void PickSpawnCorner_DerivesTheSlotFromPlayerId_NotFromTheActivePlayersIndex()
        {
            string body = AssassinExecuteCreditTests.MethodBody(BootstrapperPath, "private int PickSpawnCorner(");
            StringAssert.DoesNotContain("ActivePlayers", body, "the roster view differs per peer while joins are in flight (CW-233)");
            StringAssert.DoesNotContain("IndexOf", body);
            StringAssert.Contains("CornerSlotRules.PreferredSlot(player.PlayerId)", body);
            StringAssert.Contains("CornerSlotRules.Resolve(", body);
            StringAssert.Contains("OccupiedCornerMask()", body, "keep the occupied check for leaves/rejoins");
        }
    }
}
