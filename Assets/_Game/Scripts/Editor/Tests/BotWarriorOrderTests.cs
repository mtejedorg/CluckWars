using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// The game's bot plays the Warrior kits in the order the simulator's P0 measures them (design 2026-11-25, section 16, slice G6): the four branches
    /// (<c>WorthAHit</c> and <c>WorthAFight</c> inside the perception pass, then <c>HitForFood</c> and <c>EnforcerOverlay</c> in the think) must be reached in the
    /// same order in <c>BotController</c> as in <c>ScriptedPolicy</c>, or P0's numbers describe a bot nobody plays.
    /// </summary>
    public sealed class BotWarriorOrderTests
    {
        private const string BotPath = "Assets/_Game/Scripts/Gameplay/BotController.cs";
        private const string P0Path = "Assets/_Game/Scripts/Balance/ScriptedPolicy.cs";
        private static readonly string[] Branches = { "WorthAHit", "WorthAFight", "HitForFood", "EnforcerOverlay" };

        // The branch calls in the order a think reaches them: the perception pass runs first, then the think's own body.
        private static string[] CallOrder(string path, string perceiveSignature, string thinkSignature)
        {
            string perceive = AssassinExecuteCreditTests.MethodBody(path, perceiveSignature);
            string think = AssassinExecuteCreditTests.MethodBody(path, thinkSignature);
            string reached = perceive + "\n" + think;
            var hits = new List<(int at, string name)>();
            foreach (var name in Branches)
            {
                int from = 0;
                while (true)
                {
                    int at = reached.IndexOf(name + "(", from, StringComparison.Ordinal);
                    if (at < 0) break;
                    hits.Add((at, name));
                    from = at + name.Length;
                }
            }
            return hits.OrderBy(h => h.at).Select(h => h.name).ToArray();
        }

        [Test]
        public void BotController_WarriorBranches_RunInP0sOrder()
        {
            var p0 = CallOrder(P0Path, "private void Perceive(MatchView v, in SeatView me)", "public MatchIntent Think(");
            var bot = CallOrder(BotPath, "private void Perceive()", "private void Think()");
            CollectionAssert.AreEqual(Branches, p0, "P0 reaches the branches in the design's order (the scan saw all four, once each, in this order).");
            CollectionAssert.AreEqual(p0, bot, "the game's bot must reach the Warrior branches in the order P0 does.");
        }

        [Test]
        public void BotController_ReadsTheKitFacts_BeforeItPerceives()
        {
            string think = AssassinExecuteCreditTests.MethodBody(BotPath, "private void Think()");
            int read = think.IndexOf("ReadWarriorFacts();", StringComparison.Ordinal);
            int perceive = think.IndexOf("Perceive();", StringComparison.Ordinal);
            Assert.Greater(read, 0, "the facts are read each think");
            Assert.Less(read, perceive, "before the perception pass, which asks WorthAHit and WorthAFight (as ScriptedPolicy.Think reads them first)");
        }

        [Test]
        public void BotController_FreeCasts_StandDownAfterOne()
        {
            // One cast per think, as P0's single _cast: the ordinary casts do not follow a free one in the same think.
            string cast = AssassinExecuteCreditTests.MethodBody(BotPath, "private bool TryCast(");
            StringAssert.Contains("_freeCast", cast);
            string passing = AssassinExecuteCreditTests.MethodBody(BotPath, "private bool TryStealInPassing(");
            StringAssert.Contains("_freeCast", passing);
            string free = AssassinExecuteCreditTests.MethodBody(BotPath, "private bool FreeCast(");
            StringAssert.Contains("_freeCast = true;", free);
        }

        [Test]
        public void BotController_SeesRivalsOnlyThroughSight()
        {
            // ARCH-DATA-15: a rival is considered through what the bot sees (BotSightRules), never through its transform.
            string actionable = AssassinExecuteCreditTests.MethodBody(BotPath, "private bool Actionable(");
            StringAssert.Contains("Sight(rival)", actionable);
            StringAssert.Contains("GivenUp", actionable);
            StringAssert.Contains("Hidden", actionable);
            string src = RuntimeSourceGuardTests.Code(BotPath);
            foreach (var branch in new[] { "private void HitForFood(", "private bool WorthAHit(", "private bool WorthAFight(", "private void EnforcerOverlay(" })
                StringAssert.DoesNotContain("rival.transform.position", AssassinExecuteCreditTests.MethodBody(BotPath, branch), $"{branch} reads a rival's position through its sighting");
            StringAssert.Contains("class BotController", src);
        }
    }
}
