using System.Collections.Generic;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// The arena as <c>Game.unity</c> actually configures it, rebuilt in pure C#.
    /// </summary>
    /// <remarks>
    /// Every value is READ from the scene through <see cref="TestAssets"/>, never restated.
    /// Three separate fixtures used to hand-mirror these numbers as literals and all three
    /// had gone stale before anyone noticed (2026-08-14: a 12x10 centre pile against a
    /// shipped 7.8x6.5, a 1.5 jitter against a shipped 0.4) — the suite was rigorously
    /// proving properties of a map nobody plays. Having ONE place that reconstructs the
    /// shipped map is what stops that recurring, and it is the same source
    /// <see cref="MapGenerator"/> itself uses.
    /// </remarks>
    internal static class ShippedMap
    {
        public static float PlaneSize => TestAssets.SceneFloat("_planeSize");
        public static float HalfSize => PlaneSize * 0.5f;
        public static float WallThickness => TestAssets.SceneFloat("_wallThickness");
        public static int ArmCount => Mathf.RoundToInt(TestAssets.SceneFloat("_standardObstacleCount"));
        public static float InteriorWallHeight => TestAssets.SceneFloat("_interiorWallHeight");
        public static float CornerDistance => TestAssets.SceneFloat("_baseCornerDistance");

        public static Vector2 CentrePileFootprint => TestAssets.SceneVector2("_centerPileFootprint");
        public static Vector2 PersonalPileFootprint => TestAssets.SceneVector2("_personalPileFootprint");
        public static Vector2 ContestedPileFootprint => TestAssets.SceneVector2("_contestedPileFootprint");
        public static float PersonalPileInset => TestAssets.SceneFloat("_personalPileInset");
        public static float ContestedEdgeInset => TestAssets.SceneFloat("_contestedEdgeInset");
        public static float PilePositionJitter => TestAssets.SceneFloat("_pilePositionJitter");

        public static int CoverPerSector => Mathf.RoundToInt(TestAssets.SceneFloat("_coverPerSector"));
        public static Vector2 CoverSpanRange => TestAssets.SceneVector2("_coverSpanRange");
        public static int BarrierPerSector => Mathf.RoundToInt(TestAssets.SceneFloat("_barrierPerSector"));
        public static Vector2 BarrierSpanRange => TestAssets.SceneVector2("_barrierSpanRange");
        public static float ScatterClearance => TestAssets.SceneFloat("_scatterClearance");
        public static int PlacementAttempts => Mathf.RoundToInt(TestAssets.SceneFloat("_obstaclePlacementAttempts"));

        /// <summary>Same circumscribed-radius convention <see cref="MapGenerator"/> uses.</summary>
        public static float FootprintRadius(Vector2 size) =>
            0.5f * Mathf.Sqrt(size.x * size.x + size.y * size.y);

        public static float CentreKeepClear => FootprintRadius(CentrePileFootprint);

        public static Vector3[] Corners()
        {
            float d = CornerDistance;
            return new[]
            {
                new Vector3(+d, 0f, +d),
                new Vector3(-d, 0f, +d),
                new Vector3(-d, 0f, -d),
                new Vector3(+d, 0f, -d),
            };
        }

        /// <summary>Mirrors <c>MapGenerator.ComputeBaseAndPileKeepClearDiscs</c>.</summary>
        public static KeepClearDisc[] KeepClearDiscs()
        {
            float personalRadius = FootprintRadius(PersonalPileFootprint) + PilePositionJitter + PinwheelLayout.PileArmBuffer;
            float contestedRadius = FootprintRadius(ContestedPileFootprint) + PilePositionJitter + PinwheelLayout.PileArmBuffer;
            float personalInset = PersonalPileInset;
            float contestedInset = ContestedEdgeInset;

            var corners = Corners();
            var discs = new List<KeepClearDisc>();
            for (int i = 0; i < corners.Length; i++)
            {
                var spawn = MapGenerator.SpawnPointNominal(corners[i]);
                discs.Add(new KeepClearDisc(new Vector2(spawn.x, spawn.z), MapGenerator.BaseKeepClearRadius));

                var personal = MapGenerator.PersonalPileNominal(corners[i], personalInset);
                discs.Add(new KeepClearDisc(new Vector2(personal.x, personal.z), personalRadius));

                var contested = MapGenerator.ContestedPileNominal(
                    corners[i], corners[(i + 1) % corners.Length], contestedInset);
                discs.Add(new KeepClearDisc(new Vector2(contested.x, contested.z), contestedRadius));
            }
            return discs.ToArray();
        }

        public static ScatterSettings ScatterSettings() => new ScatterSettings(
            CoverPerSector, CoverSpanRange,
            BarrierPerSector, BarrierSpanRange, WallThickness,
            ScatterClearance, PlacementAttempts);

        public static WallSegment[] Arms(int seed) =>
            PinwheelLayout.Build(HalfSize, ArmCount, seed, CentreKeepClear, WallThickness, KeepClearDiscs());

        public static SectorScatter.Result Scatter(int seed, WallSegment[] arms) =>
            SectorScatter.Build(HalfSize, seed, ScatterSettings(), arms, WallThickness,
                                CentreKeepClear, KeepClearDiscs());
    }
}
