using System;
using System.IO;
using System.Linq;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>The base wall: the pure rule, its bot steering, the wiring, and the geometry checks on the shipped map.</summary>
    public class BaseKeepOutTests
    {
        private const float R = 2.5f;
        private const int Own = 2;

        private static readonly KeepOutCircle Rival = new KeepOutCircle(new Vector2(20f, 20f), R, 0);
        private static readonly KeepOutCircle[] One = { Rival };

        private static float DistanceTo(in KeepOutCircle c, Vector3 p) => (new Vector2(p.x, p.z) - c.Center).magnitude;

        [Test]
        public void OutsideIsLeftAlone() =>
            Assert.IsFalse(BaseKeepOutRules.TryCorrect(new Vector3(20f, 0f, 16f), One, 1, Own, out _));

        [Test]
        public void TheOwner_WalksInFreely() =>
            Assert.IsFalse(BaseKeepOutRules.TryCorrect(new Vector3(20f, 0f, 20f), One, 1, ownCorner: 0, out _));

        [Test]
        public void NoHomeCorner_HasNoExemption() =>
            Assert.IsTrue(BaseKeepOutRules.TryCorrect(new Vector3(20f, 0f, 19f), One, 1, ownCorner: -1, out _));

        [Test]
        public void Inside_MovesRadiallyToTheEdge_AndKeepsY()
        {
            Assert.IsTrue(BaseKeepOutRules.TryCorrect(new Vector3(20f, 0.7f, 19f), One, 1, Own, out var c));
            Assert.AreEqual(20f, c.x, 1e-4f);
            Assert.AreEqual(17.5f, c.z, 1e-4f);
            Assert.AreEqual(0.7f, c.y);
        }

        [Test]
        public void ThePointOnTheEdge_IsOutside_SoACorrectionNeverRepeats()
        {
            Assert.IsTrue(BaseKeepOutRules.TryCorrect(new Vector3(21f, 0f, 19f), One, 1, Own, out var c));
            Assert.IsFalse(BaseKeepOutRules.TryCorrect(c, One, 1, Own, out _));
        }

        [Test]
        public void PressingStraightIn_DoesNotJitter()
        {
            var p = new Vector3(20f, 0f, 17.5f);
            float? rest = null;
            for (int tick = 0; tick < 60; tick++)
            {
                p += Vector3.forward * 0.2f;
                if (BaseKeepOutRules.TryCorrect(p, One, 1, Own, out var c)) p = c;
                if (rest.HasValue) Assert.AreEqual(rest.Value, p.z, 1e-4f, $"tick {tick}");
                rest = p.z;
            }
        }

        [Test]
        public void WalkingAcross_SlidesRoundTheWall()
        {
            var p = new Vector3(14f, 0f, 19.5f);
            float minDistance = float.MaxValue;
            for (int tick = 0; tick < 200 && p.x < 26f; tick++)
            {
                p += Vector3.right * 0.2f;
                if (BaseKeepOutRules.TryCorrect(p, One, 1, Own, out var c)) p = c;
                minDistance = Mathf.Min(minDistance, DistanceTo(Rival, p));
            }
            Assert.GreaterOrEqual(p.x, 26f, "the wall must not stop a bird walking past it");
            Assert.GreaterOrEqual(minDistance, R - BaseKeepOutRules.EdgeSlack);
        }

        [Test]
        public void TheExactCentre_LeavesTowardTheArenaMiddle_Deterministically()
        {
            Assert.IsTrue(BaseKeepOutRules.TryCorrect(new Vector3(20f, 0f, 20f), One, 1, Own, out var a));
            Assert.IsTrue(BaseKeepOutRules.TryCorrect(new Vector3(20f, 0f, 20f), One, 1, Own, out var b));
            Assert.AreEqual(a, b);
            Assert.Less(a.x, 20f);
            Assert.Less(a.z, 20f);
            Assert.AreEqual(R, DistanceTo(Rival, a), 1e-4f);
        }

        [Test]
        public void OverlappingDiscs_EndOutsideBoth()
        {
            var two = new[] { new KeepOutCircle(new Vector2(0f, 0f), R, 0), new KeepOutCircle(new Vector2(3f, 0f), R, 1) };
            foreach (var p in new[] { new Vector3(1.5f, 0f, 0f), new Vector3(1.4f, 0f, 0.3f), new Vector3(0.1f, 0f, 2f), new Vector3(1.5f, 0f, 1.9f) })
            {
                Assert.IsTrue(BaseKeepOutRules.TryCorrect(p, two, 2, Own, out var c), p.ToString());
                Assert.IsFalse(BaseKeepOutRules.TryCorrect(c, two, 2, Own, out _), $"{p} -> {c} is still inside");
            }
        }

        [Test]
        public void OverlapWithTheOwnBase_OnlyTheRivalPushes()
        {
            var two = new[] { new KeepOutCircle(new Vector2(0f, 0f), R, Own), new KeepOutCircle(new Vector2(3f, 0f), R, 1) };
            Assert.IsTrue(BaseKeepOutRules.TryCorrect(new Vector3(1.5f, 0f, 0f), two, 2, Own, out var c));
            Assert.AreEqual(0.5f, c.x, 1e-4f);
        }

        [Test]
        public void ZeroRadius_TurnsTheRuleOff()
        {
            var off = new[] { Rival.WithRadius(0f) };
            Assert.IsFalse(BaseKeepOutRules.TryCorrect(new Vector3(20f, 0f, 20f), off, 1, Own, out _));
        }

        // ---- bots --------------------------------------------------------------------------------------------------

        [Test]
        public void SteerAround_AClearLine_KeepsTheTarget()
        {
            var target = new Vector3(0f, 0f, 16f);
            Assert.AreEqual(target, BaseKeepOutRules.SteerAround(new Vector3(20f, 0f, 16f), target, One, 1, Own, 25f));
        }

        [Test]
        public void SteerAround_TheOwnBase_IsNoObstacle()
        {
            var target = new Vector3(20f, 0f, 26f);
            Assert.AreEqual(target, BaseKeepOutRules.SteerAround(new Vector3(20f, 0f, 10f), target, One, 1, ownCorner: 0, 30f));
        }

        [Test]
        public void SteerAround_WalksABotPastAWall_WithoutEverTouchingIt()
        {
            var inflated = new[] { Rival.WithRadius(R + BaseKeepOutRules.BotClearance) };
            var p = new Vector3(20f, 0f, 12f);
            var target = new Vector3(20f, 0f, 24.5f);   // straight through the base, along the wall side
            float closest = float.MaxValue;
            for (int tick = 0; tick < 400 && (p - target).magnitude > 0.3f; tick++)
            {
                var steer = BaseKeepOutRules.SteerAround(p, target, inflated, 1, Own, 25f);
                var dir = steer - p;
                dir.y = 0f;
                p += dir.normalized * 0.15f;
                closest = Mathf.Min(closest, DistanceTo(Rival, p));
            }
            Assert.Less((p - target).magnitude, 0.5f, "the bot must get there");
            Assert.GreaterOrEqual(closest, R, "the bot must never lean on the wall");
        }

        [Test]
        public void SteerAround_PicksTheSideThatStaysInTheArena()
        {
            var nearWall = new[] { new KeepOutCircle(new Vector2(0f, 23.5f), R, 0) };
            var steer = BaseKeepOutRules.SteerAround(new Vector3(-8f, 0f, 24f), new Vector3(8f, 0f, 24f), nearWall, 1, Own, 25f);
            Assert.Less(steer.z, 25f);
        }

        // ---- the shipped map ---------------------------------------------------------------------------------------

        private static float Radius => TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath).BaseKeepOutRadius;

        private static Vector3[] Corners
        {
            get
            {
                float d = ShippedMap.CornerDistance;
                return new[] { new Vector3(d, 0f, d), new Vector3(-d, 0f, d), new Vector3(-d, 0f, -d), new Vector3(d, 0f, -d) };
            }
        }

        private static Vector3[] Bases => Corners.Select(MapGenerator.SpawnPointNominal).ToArray();

        [Test]
        public void ShippedRadius_MatchesTheDepositRadius_AndLeavesItUntouched()
        {
            var prefab = TestAssets.Load<GameObject>(TestAssets.PlayerBasePrefabPath).GetComponent<PlayerBase>();
            Assert.AreEqual(2.5f, prefab.DepositRadius, "the wall must not move the deposit radius, which the Oracle reads");
            Assert.AreEqual(prefab.DepositRadius, Radius);
        }

        [Test]
        public void BaseWalls_NeverReachEachOther()
        {
            var b = Bases;
            for (int i = 0; i < b.Length; i++)
                Assert.Greater((b[i] - b[(i + 1) % b.Length]).magnitude, 2f * Radius + PinwheelLayout.MinCorridorWidth);
        }

        [Test]
        public void NoLaneClosesForTheWidestChicken()
        {
            // Terrain stays outside the base's no-build disc, so the ring between wall and terrain must fit a body...
            Assert.GreaterOrEqual(MapGenerator.BaseKeepClearRadius - Radius, PinwheelLayout.ChickenDiameter,
                "a chicken must fit between a base wall and the nearest terrain");
            // ...and so must the strip between the wall and the arena edge (a body sliding round the back of a base).
            float gap = PinwheelLayout.ArenaClampLimit(ShippedMap.HalfSize) - (Mathf.Abs(Bases[0].x) + Radius);
            Assert.GreaterOrEqual(gap, 0f, "a corrected position must stay inside the arena clamp");
        }

        [Test]
        public void EveryPile_StaysCollectableByEveryRival()
        {
            var corners = Corners;
            var bases = Bases;
            var piles = new System.Collections.Generic.List<Vector3> { Vector3.zero };
            for (int i = 0; i < corners.Length; i++)
            {
                piles.Add(MapGenerator.PersonalPileNominal(corners[i], ShippedMap.PersonalPileInset));
                piles.Add(MapGenerator.ContestedPileNominal(corners[i], corners[(i + 1) % corners.Length], ShippedMap.ContestedEdgeInset));
            }

            foreach (var pile in piles)
                foreach (var b in bases)
                    Assert.Greater((pile - b).magnitude, Radius + PinwheelLayout.ChickenDiameter,
                        $"pile {pile} is walled in by the base at {b}: the base's rivals could not reach it");
        }

        [Test]
        public void EverySpawn_IsOutsideEveryRivalWall()
        {
            var bases = Bases;
            const float Jitter = 0.25f;   // MatchBootstrapper's spawn safety jitter
            for (int i = 0; i < bases.Length; i++)
                for (int j = 0; j < bases.Length; j++)
                    if (i != j) Assert.Greater((bases[i] - bases[j]).magnitude - Jitter, Radius);
        }

        // ---- wiring ------------------------------------------------------------------------------------------------

        private static string Src(string file) => File.ReadAllText("Assets/_Game/Scripts/Gameplay/" + file).Replace("\r\n", "\n");

        [Test]
        public void TheClampRuns_AfterEveryPositionWriter_OnTheChickensOwnAuthority()
        {
            Assert.IsTrue(typeof(Fusion.IAfterTick).IsAssignableFrom(typeof(ChickenController)));
            StringAssert.Contains("if (HasStateAuthority) KeepOutOfRivalBases();", Src("ChickenController.cs"));
        }

        [Test]
        public void TheClamp_GoesThroughTheController_NeverATransformWrite()
        {
            var src = Src("ChickenController.cs");
            int start = src.IndexOf("private void KeepOutOfRivalBases()", StringComparison.Ordinal);
            string body = src.Substring(start, src.IndexOf("\n        }", start, StringComparison.Ordinal) - start);
            StringAssert.Contains("_characterController.Move(", body);
            StringAssert.DoesNotContain("transform.position =", body);
            StringAssert.Contains("IsUnsticking", body);
        }

        [Test]
        public void Bots_SteerAroundWalls_OnEveryPathResult()
        {
            var src = Src("BotController.cs");
            StringAssert.Contains("BaseKeepOutRules.SteerAround(selfPos, ResolveSteerPoint(selfPos)", src);
            StringAssert.Contains("BaseKeepOutRules.TryCorrect(_moveTarget", src);
        }
    }
}
