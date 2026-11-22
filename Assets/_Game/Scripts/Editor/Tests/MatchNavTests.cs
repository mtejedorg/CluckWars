using CluckWars.Balance;
using CluckWars.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    public sealed class MatchNavTests
    {
        private const float Half = 20f;

        [TearDown]
        public void TearDown() => MatchTestKit.Release();

        [Test]
        public void NextPoint_ClearSegment_ReturnsTheTarget()
        {
            var nav = OracleNav.For(MatchTestKit.OpenMap(Half), Half);
            var a = new Vector2(-3f, -4f);
            var b = new Vector2(7f, 2f);
            Assert.IsTrue(nav.IsClear(a, b));
            Assert.AreEqual(b, nav.NextPoint(a, b));
            Assert.AreEqual(Vector2.Distance(a, b), nav.PathLength(a, b), 1e-4f);
        }

        [Test]
        public void NextPoint_RoundAWall_StepsToAVisibleRingNode()
        {
            var map = MatchTestKit.OneWallMap(Half, new Vector2(0f, -3f), new Vector2(0f, 3f), 0.5f);
            var nav = OracleNav.For(map, Half);
            var a = new Vector2(-4f, 0f);
            var b = new Vector2(4f, 0f);
            Assert.IsFalse(nav.IsClear(a, b), "the wall must block the straight line for this test to mean anything");

            Vector2 next = nav.NextPoint(a, b);
            Assert.AreNotEqual(b, next);
            Assert.IsTrue(nav.IsClear(a, next), "the first hop must be walkable");
            Assert.Greater(nav.PathLength(a, b), Vector2.Distance(a, b));
            Assert.AreEqual(OraclePaths.ShortestPath(a, b, map.WallA, map.WallB, map.WallHalfWidth, Half), nav.PathLength(a, b), 1e-3f);
        }

        [Test]
        public void PathLength_EqualsShortestPath_OnShippedWalls()
        {
            var map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            float half = OracleMapBaker.ArenaHalf();
            var nav = OracleNav.For(map, half);
            for (int i = 0; i < map.Places.Length; i++)
                for (int j = 0; j < map.Places.Length; j++)
                {
                    var a = map.Places[i].Pos;
                    var b = map.Places[j].Pos;
                    float expected = OraclePaths.ShortestPath(a, b, map.WallA, map.WallB, map.WallHalfWidth, half);
                    Assert.AreEqual(expected, nav.PathLength(a, b), 1e-3f, $"place {i} to place {j}");
                }
        }

        // A wall that pokes out of the arena (the shipped map's arms do): its clearance reaches the arena's edge.
        private OracleMapSO PokingWallMap() => MatchTestKit.OneWallMap(Half, new Vector2(2f, 0f), new Vector2(5f, Half + 4f), 0.8f);

        [Test]
        public void NearestClear_InsideTheArenaAndOutsideEveryWall_AlsoForAPointPastTheEdgeOrInsideAWall()
        {
            var nav = OracleNav.For(PokingWallMap(), Half);
            var points = new[]
            {
                new Vector2(3f, 5f),                  // inside the wall's clearance
                new Vector2(4.6f, Half),              // on the arena's edge, inside the clearance that pokes through it
                new Vector2(4f, Half + 6f),           // past the edge
                new Vector2(-30f, -30f),              // far outside
                new Vector2(-4f, 4f),                 // already fine
            };
            foreach (var p in points)
            {
                var q = nav.NearestClear(p);
                Assert.IsTrue(nav.IsClear(q, q), $"{p} -> {q} must be a standing point");
                Assert.LessOrEqual(Mathf.Abs(q.x), Half + 1e-3f, $"{p} -> {q}");
                Assert.LessOrEqual(Mathf.Abs(q.y), Half + 1e-3f, $"{p} -> {q}");
            }
            Assert.AreEqual(points[4], nav.NearestClear(points[4]), "A standing point stays where it is.");
        }

        [Test]
        public void Sim_AGoalInsideAWallOrOutsideTheArena_DoesNotStopTheMatch()
        {
            var map = PokingWallMap();
            var kit = MatchTestKit.Farmer("walker", 6f, 20, 3f, 1f, 0.5f, 5f);
            int tick = 0;
            var goals = new[] { new Vector2(3f, 5f), new Vector2(4.6f, Half + 2f), new Vector2(-40f, 40f), new Vector2(4.6f, Half) };
            var wanderer = MatchTestKit.Policy((v, s) => new MatchIntent { Goal = goals[(tick++ / 10) % goals.Length], PeckPile = -1, CastSlot = -1, CastTarget = -1 });
            var setup = MatchTestKit.Duel(map, kit, kit, wanderer, MatchTestKit.Stand, Half, 20f);

            Assert.DoesNotThrow(() => MatchSim.Run(setup, 1UL));
        }

        [Test]
        public void For_SameFingerprintAndHalf_ReturnsTheSameInstance()
        {
            var map = MatchTestKit.OpenMap(Half);
            Assert.AreSame(OracleNav.For(map, Half), OracleNav.For(map, Half));
            Assert.AreNotSame(OracleNav.For(map, Half), OracleNav.For(map, Half * 0.5f));
        }
    }
}
