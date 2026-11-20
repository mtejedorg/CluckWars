using System.Linq;
using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The baked Oracle map (<see cref="OracleMapSO"/>) against the shipped scene. No scene value is restated:
    /// everything is read from the asset, <see cref="ShippedMap"/> or computed from the geometry in the test.
    /// </summary>
    public class OracleMapTests
    {
        private const float LegTolerance = 0.01f;
        private static readonly string FixMessage = $"The Oracle map is stale against Game.unity: run {OracleMapBaker.MenuPath} and commit {OracleMapBaker.AssetPath}.";

        private static OracleMapSO Map()
        {
            var map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            return map;
        }

        [Test]
        public void Bake_MatchesTheScene()
        {
            Assert.AreEqual(OracleMapBaker.Fingerprint(), Map().SceneFingerprint, FixMessage);
        }

        [Test]
        public void Bake_WallsAreTheGeneratorsArms()
        {
            var map = Map();
            var arms = ShippedMap.Arms(0).Where(a => a.A != a.B).ToArray();
            Assert.AreEqual(arms.Length, map.WallA.Length, FixMessage);
            for (int i = 0; i < arms.Length; i++)
            {
                Assert.AreEqual(0f, Vector2.Distance(arms[i].A, map.WallA[i]), 1e-4f, FixMessage);
                Assert.AreEqual(0f, Vector2.Distance(arms[i].B, map.WallB[i]), 1e-4f, FixMessage);
            }
        }

        [Test]
        public void Arms_DoNotDependOnTheMatchSeed()
        {
            var a = ShippedMap.Arms(0);
            var b = ShippedMap.Arms(123456);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].A, b[i].A, $"Arm {i} end A moved with the seed: the bake would depend on the seed. Bake the arms per seed or drop the jitter.");
                Assert.AreEqual(a[i].B, b[i].B, $"Arm {i} end B moved with the seed.");
            }
        }

        [Test]
        public void Fingerprint_SeesAMovedArmAndAChangedInput()
        {
            var inputs = OracleMapBaker.ReadInputs();
            var arms = ShippedMap.Arms(0);
            string baseline = OracleMapBaker.Fingerprint(inputs, arms);
            Assert.AreEqual(OracleMapBaker.Fingerprint(), baseline);

            var moved = (WallSegment[])arms.Clone();
            moved[1].B += new Vector2(0.5f, 0f);
            Assert.AreNotEqual(baseline, OracleMapBaker.Fingerprint(inputs, moved), "A moved arm end must change the fingerprint.");

            var thicker = inputs;
            thicker.WallThickness += 0.1f;
            Assert.AreNotEqual(baseline, OracleMapBaker.Fingerprint(thicker, arms), "A changed input must change the fingerprint.");
        }

        [Test]
        public void Places_FollowTheFixedOrder()
        {
            var map = Map();
            Assert.AreEqual(OracleMapSO.PlaceCount, map.Places.Length);
            Assert.AreEqual(OraclePlaceKind.Centre, map.Places[OracleMapSO.CentreIndex].Kind);
            Assert.AreEqual(-1, map.Places[OracleMapSO.CentreIndex].Seat);
            for (int s = 0; s < OracleMapSO.SeatCount; s++)
            {
                Assert.AreEqual(OraclePlaceKind.Personal, map.Places[OracleMapSO.PersonalIndex(s)].Kind);
                Assert.AreEqual(s, map.Places[OracleMapSO.PersonalIndex(s)].Seat);
                Assert.AreEqual(OraclePlaceKind.Contested, map.Places[OracleMapSO.ContestedIndex(s)].Kind);
                Assert.AreEqual(s, map.Places[OracleMapSO.ContestedIndex(s)].Seat);
                Assert.AreEqual(OraclePlaceKind.Base, map.Places[OracleMapSO.BaseIndex(s)].Kind);
                Assert.AreEqual(s, map.Places[OracleMapSO.BaseIndex(s)].Seat);
            }
            Assert.AreEqual(OracleMapSO.PileCount, OracleMapSO.BaseIndex(0), "Piles come first, then the bases.");

            var piles = ShippedMap.ShippedPiles(Vector2.zero, Vector2.zero);
            for (int i = 0; i < OracleMapSO.PileCount; i++)
                Assert.AreEqual(0f, Vector2.Distance(map.Places[i].Pos, new Vector2(piles[i].Pos.x, piles[i].Pos.z)), 1e-3f, $"Pile {i} moved: {FixMessage}");
        }

        [Test]
        public void WalkLeg_NeverShorterThanJumpLeg()
        {
            var map = Map();
            for (int i = 0; i < OracleMapSO.PlaceCount; i++)
                for (int j = 0; j < OracleMapSO.PlaceCount; j++)
                    Assert.GreaterOrEqual(map.Leg(i, j, false), map.Leg(i, j, true) - 1e-4f, $"Leg {i}->{j}: walking is shorter than jumping.");
        }

        [Test]
        public void BaseToOwnPersonalAndCentre_HaveNoDetour()
        {
            var map = Map();
            for (int s = 0; s < OracleMapSO.SeatCount; s++)
            {
                int b = OracleMapSO.BaseIndex(s);
                Assert.AreEqual(map.Leg(b, OracleMapSO.PersonalIndex(s), true), map.Leg(b, OracleMapSO.PersonalIndex(s), false), LegTolerance, $"Seat {s} personal pile has a detour.");
                Assert.AreEqual(map.Leg(b, OracleMapSO.CentreIndex, true), map.Leg(b, OracleMapSO.CentreIndex, false), LegTolerance, $"Seat {s} centre has a detour.");
            }
        }

        [Test]
        public void EachBase_HasOneFreeAndOneWalledContestedPile()
        {
            var map = Map();
            for (int s = 0; s < OracleMapSO.SeatCount; s++)
            {
                int b = OracleMapSO.BaseIndex(s);
                int[] edges = { s, (s + OracleMapSO.SeatCount - 1) % OracleMapSO.SeatCount };
                int walled = edges.Count(e =>
                {
                    int p = OracleMapSO.ContestedIndex(e);
                    return map.Leg(b, p, false) / Mathf.Max(map.Leg(b, p, true), 1e-4f) > 1.2f;
                });
                Assert.AreEqual(1, walled, $"Seat {s}: exactly one of the two adjacent contested piles must sit behind an arm.");
            }
        }

        [Test]
        public void Legs_AreFourFoldSymmetric()
        {
            var map = Map();
            int Rot(int place, int k)
            {
                if (place == OracleMapSO.CentreIndex) return place;
                var p = map.Places[place];
                int seat = (p.Seat + k) % OracleMapSO.SeatCount;
                return p.Kind == OraclePlaceKind.Personal ? OracleMapSO.PersonalIndex(seat)
                     : p.Kind == OraclePlaceKind.Contested ? OracleMapSO.ContestedIndex(seat)
                     : OracleMapSO.BaseIndex(seat);
            }

            // The centre pile's oval is not a circle and is not turned with the seats, so a leg that ends at the
            // centre only repeats under a half turn; every other leg repeats under each quarter turn.
            for (int k = 1; k < OracleMapSO.SeatCount; k++)
                for (int i = 0; i < OracleMapSO.PlaceCount; i++)
                    for (int j = 0; j < OracleMapSO.PlaceCount; j++)
                    {
                        bool touchesCentre = i == OracleMapSO.CentreIndex || j == OracleMapSO.CentreIndex;
                        if (touchesCentre && k % 2 != 0) continue;
                        Assert.AreEqual(map.Leg(i, j, false), map.Leg(Rot(i, k), Rot(j, k), false), LegTolerance, $"Walk leg {i}->{j} differs from its rotation by {k} seats.");
                        Assert.AreEqual(map.Leg(i, j, true), map.Leg(Rot(i, k), Rot(j, k), true), LegTolerance, $"Jump leg {i}->{j} differs from its rotation by {k} seats.");
                    }
        }

        [Test]
        public void ShortestPath_GoesRoundAWallEnd()
        {
            // One vertical wall on the y axis from (0,-l) to (0,l); a and b face each other across it.
            const float l = 6f, d = 3f, half = 0.8f, arena = 100f;
            var a = new Vector2(-d, 0f);
            var b = new Vector2(d, 0f);
            float path = OraclePaths.ShortestPath(a, b, new[] { new Vector2(0f, -l) }, new[] { new Vector2(0f, l) }, half, arena);

            // Round the top end: tangent from a to the circle (radius half) round (0,l), an arc over the top, a tangent down to b.
            float dist = Mathf.Sqrt(d * d + l * l);
            float tangent = Mathf.Sqrt(dist * dist - half * half);
            float alpha = Mathf.Atan2(d, l);
            float arc = 2f * Mathf.PI - 2f * alpha - 2f * Mathf.Acos(half / dist);
            float expected = 2f * tangent + half * arc;

            Assert.AreEqual(expected, path, 0.05f);
            Assert.Greater(path, Vector2.Distance(a, b), "The wall must cost a detour.");
        }

        [Test]
        public void ShortestPath_IsStraightWhenClear()
        {
            var a = new Vector2(-3f, 4f);
            var b = new Vector2(3f, 4f);
            float path = OraclePaths.ShortestPath(a, b, new[] { new Vector2(0f, -6f) }, new[] { new Vector2(0f, 2f) }, 0.8f, 100f);
            Assert.AreEqual(Vector2.Distance(a, b), path, 1e-4f);
        }
    }
}
