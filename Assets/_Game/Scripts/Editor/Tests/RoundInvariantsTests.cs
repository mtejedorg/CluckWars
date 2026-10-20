using System.Collections.Generic;
using CluckWars.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>The in-game Invariant probe's pure rules (round start, round end, the order-independent world hash).</summary>
    public sealed class RoundInvariantsTests
    {
        private static readonly Vector3[] Spawns =
        {
            new Vector3(21.8f, 0f, 21.8f), new Vector3(-21.8f, 0f, 21.8f), new Vector3(-21.8f, 0f, -21.8f), new Vector3(21.8f, 0f, -21.8f),
        };

        private static InvariantChicken Bird(string id, int home, Vector2 pos, bool bot = false) =>
            new InvariantChicken { Id = id, Home = home, Position = pos, IsBot = bot };

        private static List<(int, float)> Bases(params float[] food)
        {
            var l = new List<(int, float)>();
            for (int i = 0; i < food.Length; i++) l.Add((i, food[i]));
            return l;
        }

        [Test]
        public void RoundStart_CleanSoloRound_Passes()
        {
            var chickens = new List<InvariantChicken>
            {
                Bird("P1", 0, new Vector2(21.8f, 21.6f)), Bird("B1", 1, new Vector2(-21.8f, 21.8f), true),
                Bird("B2", 2, new Vector2(-21.8f, -21.8f), true), Bird("B3", 3, new Vector2(21.8f, -21.8f), true),
            };
            var fails = new List<InvariantFailure>();
            int n = RoundInvariants.CheckRoundStart(chickens, Spawns, Bases(0, 0, 0, 0),
                new List<(float, float)> { (20f, 20f), (5f, 5f) }, 45f, 45f, singleMode: true, fails);
            CollectionAssert.IsEmpty(fails, string.Join(" | ", fails.ConvertAll(f => f.Line())));
            Assert.Greater(n, 20);
        }

        [Test]
        public void RoundStart_CW221_TheHostChickenWhereTheLastRoundEnded_Fails()
        {
            // Session 203530, round 2 at rem=45.0: P1 still at (0.1, 19.3), the others at their corners.
            var chickens = new List<InvariantChicken>
            {
                Bird("P1", 0, new Vector2(0.1f, 19.3f)), Bird("P2", 1, new Vector2(-21.8f, 21.8f)),
                Bird("P3", 2, new Vector2(-21.8f, -21.8f)), Bird("P4", 3, new Vector2(21.8f, -21.8f)),
            };
            var fails = new List<InvariantFailure>();
            RoundInvariants.CheckRoundStart(chickens, Spawns, Bases(0, 0, 0, 0), new List<(float, float)>(), 45f, 45f, false, fails);
            Assert.AreEqual(1, fails.Count);
            StringAssert.StartsWith("Invariant FAIL respawn expected=<=2.50m actual=21.", fails[0].Line());
            StringAssert.EndsWith("who=P1", fails[0].Line());
        }

        [Test]
        public void RoundStart_LeftoversFromTheLastRound_AllReported()
        {
            var bird = Bird("P1", 0, new Vector2(21.8f, 21.8f));
            bird.Cargo = 6f;
            bird.MaxCooldown = 2.5f;
            bird.ControlActive = true;
            bird.ActiveSlots = 1;
            var fails = new List<InvariantFailure>();
            RoundInvariants.CheckRoundStart(new List<InvariantChicken> { bird }, Spawns, Bases(12, 0, 0, 0),
                new List<(float, float)> { (3f, 20f) }, 30f, 45f, singleMode: true, fails);
            var names = fails.ConvertAll(f => f.Name);
            CollectionAssert.AreEquivalent(new[] { "cargo-zero", "cooldowns-zero", "control-free", "no-active-ability", "base-zero",
                "pile-full", "timer", "bots" }, names);
        }

        [Test]
        public void RoundEnd_WinnerMustLead_NoWinnerOnlyOnATie()
        {
            var fails = new List<InvariantFailure>();
            RoundInvariants.CheckRoundEnd(2, Bases(10, 3, 12, 0), fails);
            CollectionAssert.IsEmpty(fails);
            RoundInvariants.CheckRoundEnd(1, Bases(10, 3, 12, 0), fails);
            Assert.AreEqual("winner-leads", fails[0].Name);
            fails.Clear();
            RoundInvariants.CheckRoundEnd(-1, Bases(5, 5, 0, 0), fails);
            CollectionAssert.IsEmpty(fails, "a tie at the top: no winner is right");
            RoundInvariants.CheckRoundEnd(-1, Bases(9, 5, 0, 0), fails);
            Assert.AreEqual("winner-named", fails[0].Name);
        }

        [Test]
        public void RoundOrdinal_MatchesTheMatchStartedCount()
        {
            // Shared: waiting room -> GET READY -> round -> results -> PLAY AGAIN (waiting room) -> GET READY ...
            int r = 0;
            r = RoundOrdinal.Next(r, false, CluckWars.Gameplay.MatchState.WaitingForPlayers, CluckWars.Gameplay.MatchState.WaitingForPlayers);
            Assert.AreEqual(0, r, "the first waiting room is no match yet");
            r = RoundOrdinal.Next(r, true, CluckWars.Gameplay.MatchState.WaitingForPlayers, CluckWars.Gameplay.MatchState.Starting);
            r = RoundOrdinal.Next(r, true, CluckWars.Gameplay.MatchState.Starting, CluckWars.Gameplay.MatchState.Active);
            r = RoundOrdinal.Next(r, true, CluckWars.Gameplay.MatchState.Active, CluckWars.Gameplay.MatchState.Ended);
            Assert.AreEqual(1, r);
            r = RoundOrdinal.Next(r, true, CluckWars.Gameplay.MatchState.Ended, CluckWars.Gameplay.MatchState.WaitingForPlayers);
            Assert.AreEqual(1, r, "PLAY AGAIN's waiting room still belongs to match 1");
            r = RoundOrdinal.Next(r, true, CluckWars.Gameplay.MatchState.WaitingForPlayers, CluckWars.Gameplay.MatchState.Starting);
            Assert.AreEqual(2, r);
            // Solo: results -> GET READY straight away.
            r = RoundOrdinal.Next(r, true, CluckWars.Gameplay.MatchState.Ended, CluckWars.Gameplay.MatchState.Starting);
            Assert.AreEqual(3, r);
            // A late joiner whose first look is a running round.
            Assert.AreEqual(1, RoundOrdinal.Next(0, false, CluckWars.Gameplay.MatchState.WaitingForPlayers, CluckWars.Gameplay.MatchState.Active));
        }

        [Test]
        public void WorldHash_IsOrderIndependent_AndSeesAMovedBase()
        {
            var a = new List<(string, Vector2)> { ("PlayerBase", new Vector2(21.8f, 21.8f)), ("FoodPile", Vector2.zero) };
            var b = new List<(string, Vector2)> { ("FoodPile", new Vector2(0.1f, -0.1f)), ("PlayerBase", new Vector2(21.9f, 21.85f)) };
            Assert.AreEqual(RoundInvariants.WorldHash(a, out int na), RoundInvariants.WorldHash(b, out int nb), "same objects, other order, sub-0.25 m noise");
            Assert.AreEqual(2, na);
            Assert.AreEqual(2, nb);
            // CW-216: a client with its base at the prefab root (9.47, 9.38) instead of the corner.
            var client = new List<(string, Vector2)> { ("PlayerBase", new Vector2(9.47f, 9.38f)), ("FoodPile", Vector2.zero) };
            Assert.AreNotEqual(RoundInvariants.WorldHash(a, out _), RoundInvariants.WorldHash(client, out _));
        }
    }
}
