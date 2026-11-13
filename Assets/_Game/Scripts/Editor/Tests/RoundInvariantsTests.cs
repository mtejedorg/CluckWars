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
        public void RoundStart_DeathMarksOwnRoundStartCooldown_IsNotALeftover()
        {
            Assert.AreEqual(0f, RoundInvariants.CooldownLeftover(6.5f, 4f, 2.5f), 1e-4f, "intro 2.5 s + 4 s after GO: as asked");
            Assert.AreEqual(4f, RoundInvariants.CooldownLeftover(4f, 0f, 2.5f), 1e-4f, "any other ability: all of it is left over");
            Assert.AreEqual(2.5f, RoundInvariants.CooldownLeftover(4f, 4f, 2.5f), 1e-4f, "the rule not applied: reported");
        }

        [Test]
        public void RoundStart_ARemoteSlotOfUnknownAbility_MayHoldNothingOrTheOpeningCooldown()
        {
            // A proxy sees the networked cooldown but not the loadout (shared2 run 2026-11-03: client2 saw the host's Death Mark).
            Assert.AreEqual(0f, RoundInvariants.CooldownLeftoverUnknownSlot(6.5f, 4f, 2.5f), 1e-4f, "the opening cooldown: fine");
            Assert.AreEqual(0f, RoundInvariants.CooldownLeftoverUnknownSlot(0f, 4f, 2.5f), 1e-4f, "nothing: fine");
            Assert.AreEqual(1.5f, RoundInvariants.CooldownLeftoverUnknownSlot(1.5f, 4f, 2.5f), 1e-4f, "a stale cooldown: reported");
            Assert.AreEqual(3f, RoundInvariants.CooldownLeftoverUnknownSlot(3f, 0f, 2.5f), 1e-4f, "no ability opens on a cooldown: all of it");
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

        // ---- BL-145: the aura exclusion rule ---------------------------------------------------------------------------

        /// <summary>Feeds <paramref name="frames"/> settled frames at one excuse, past the settle window.</summary>
        private static RoundInvariants.AuraVictimTally Samples(int frames, bool snared, RoundInvariants.AuraExcuse excuse)
        {
            var t = new RoundInvariants.AuraVictimTally();
            t = RoundInvariants.SampleAuraVictim(t, true, snared, excuse, 0f);          // opens the run
            float settled = RoundInvariants.AuraSettleSeconds + 0.01f;
            for (int i = 0; i < frames; i++) t = RoundInvariants.SampleAuraVictim(t, true, snared, excuse, settled);
            return t;
        }

        [Test]
        public void AuraExcuse_IsOnlyAnOutrightRefusal_NotAResistance()
        {
            Assert.AreEqual(RoundInvariants.AuraExcuse.None, RoundInvariants.ExcuseAuraSlow(false, false),
                "a plain victim deep inside is evidence");
            Assert.AreEqual(RoundInvariants.AuraExcuse.SlowImmune, RoundInvariants.ExcuseAuraSlow(true, false),
                "Egg Shell / the Shield: the slow does not land, so not-snared is correct");
            Assert.AreEqual(RoundInvariants.AuraExcuse.BaseWall, RoundInvariants.ExcuseAuraSlow(false, true),
                "a claimed base's wall between the centre and the victim refuses the zone slow");
            Assert.AreEqual(RoundInvariants.AuraExcuse.SlowImmune, RoundInvariants.ExcuseAuraSlow(true, true),
                "immunity is reported ahead of the wall when both hold");
        }

        [Test]
        public void AnImmuneVictim_IsExcused_NotFailed()
        {
            // BL-145's live case: the victim cast Egg Shell 0.13 s after the aura landed and stood in it, unslowed.
            var t = Samples(40, snared: false, RoundInvariants.AuraExcuse.SlowImmune);
            Assert.AreEqual(0, t.Deep, "an excused frame is not judged");
            Assert.AreEqual(40, t.ImmuneSamples);
            Assert.AreEqual(40, t.Excused);
            Assert.AreEqual(RoundInvariants.AuraVerdict.TooFew, RoundInvariants.JudgeAuraVictim(t),
                "no evidence either way is too-few, never not-snared");
        }

        [Test]
        public void AWalledVictim_IsExcused_AndTheCountIsPrinted()
        {
            var t = Samples(30, snared: false, RoundInvariants.AuraExcuse.BaseWall);
            Assert.AreEqual(0, t.Deep);
            Assert.AreEqual(30, t.WalledSamples);
            StringAssert.Contains("excused=30 immune=0 wall=30", RoundInvariants.AuraVictimLine("P2", "P1", true, t, 3),
                "an excluded sample is reported, not dropped");
        }

        [Test]
        public void AnUnexcusedVictimThatIsNotSnared_StillFails()
        {
            var t = Samples(40, snared: false, RoundInvariants.AuraExcuse.None);
            Assert.AreEqual(40, t.Deep);
            Assert.AreEqual(0, t.Snared);
            Assert.AreEqual(0, t.Excused);
            Assert.AreEqual(RoundInvariants.AuraVerdict.NotSnared, RoundInvariants.JudgeAuraVictim(t),
                "the exclusion must not weaken the invariant for a victim nothing protected");
        }

        [Test]
        public void TheVerdictLine_KeepsTheShapeMergeReads()
        {
            var t = Samples(12, snared: true, RoundInvariants.AuraExcuse.None);
            string line = RoundInvariants.AuraVictimLine("P2", "P1", true, t, 2);
            StringAssert.StartsWith("Invariant aura owner=P2 victim=P1 remote=1 deep=12 snared=12 verdict=ok round=2", line,
                "merge.ps1 matches deep/snared/verdict in this order; the excused counts go after round=");
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
