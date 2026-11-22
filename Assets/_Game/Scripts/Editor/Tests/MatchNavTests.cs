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

        [Test]
        public void For_SameFingerprintAndHalf_ReturnsTheSameInstance()
        {
            var map = MatchTestKit.OpenMap(Half);
            Assert.AreSame(OracleNav.For(map, Half), OracleNav.For(map, Half));
            Assert.AreNotSame(OracleNav.For(map, Half), OracleNav.For(map, Half * 0.5f));
        }
    }
}
