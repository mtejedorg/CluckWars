using System;
using System.Collections.Generic;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Tests;
using UnityEditor;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Bakes <c>Game.unity</c>'s arena into <see cref="OracleMapSO"/> (spec 5.1). Reads the scene through the
    /// same accessors <c>ShippedMap</c> gives the tests, so the bake and the suite cannot disagree about the arena.
    /// </summary>
    public static class OracleMapBaker
    {
        public const string AssetPath = "Assets/_Game/Data/Balance/OracleMap.asset";
        public const string MenuPath = "Cluck Wars/Balance/Oracle/Bake map";

        /// <summary>The generator's per-match wall seed does not move the arms while the pinwheel jitter is 0 (asserted by a test).</summary>
        private const int WallSeed = 0;

        /// <summary>Every scene value the bake depends on.</summary>
        public struct Inputs
        {
            public float PlaneSize;
            public int ArmCount;
            public float WallThickness;
            public float CornerDistance;
            public float PersonalInset;
            public float ContestedInset;
            public float PileJitter;
            public Vector2 CentreFootprint;
            public Vector2 PersonalFootprint;
            public Vector2 ContestedFootprint;
            public float DepositRadius;
            public float CollectReach;
            public float PersonalFood;
            public float ContestedFood;
            public float CentreFood;
        }

        public static Inputs ReadInputs() => new Inputs
        {
            PlaneSize = ShippedMap.PlaneSize,
            ArmCount = ShippedMap.ArmCount,
            WallThickness = ShippedMap.WallThickness,
            CornerDistance = ShippedMap.CornerDistance,
            PersonalInset = ShippedMap.PersonalPileInset,
            ContestedInset = ShippedMap.ContestedEdgeInset,
            PileJitter = ShippedMap.PilePositionJitter,
            CentreFootprint = ShippedMap.CentrePileFootprint,
            PersonalFootprint = ShippedMap.PersonalPileFootprint,
            ContestedFootprint = ShippedMap.ContestedPileFootprint,
            DepositRadius = ShippedMap.BaseDepositRadius,
            CollectReach = ShippedMap.PileCollectReach,
            PersonalFood = TestAssets.SceneFloat("_personalPileAmount"),
            ContestedFood = TestAssets.SceneFloat("_contestedPileAmount"),
            CentreFood = TestAssets.SceneFloat("_centerPileAmount"),
        };

        /// <summary>The fingerprint of the live scene (inputs plus the arms the generator builds from them).</summary>
        public static string Fingerprint() => Fingerprint(ReadInputs(), ShippedMap.Arms(WallSeed));

        /// <summary>Hash of the inputs and the arm endpoints. FNV-1a over exact float bits: no culture, no rounding.</summary>
        public static string Fingerprint(Inputs inputs, WallSegment[] arms)
        {
            ulong h = 14695981039346656037UL;
            void Mix(float v)
            {
                int bits = BitConverter.SingleToInt32Bits(v);
                for (int i = 0; i < 4; i++)
                {
                    h ^= (byte)(bits >> (8 * i));
                    h *= 1099511628211UL;
                }
            }
            void MixV(Vector2 v) { Mix(v.x); Mix(v.y); }

            Mix(inputs.PlaneSize); Mix(inputs.ArmCount); Mix(inputs.WallThickness); Mix(inputs.CornerDistance);
            Mix(inputs.PersonalInset); Mix(inputs.ContestedInset); Mix(inputs.PileJitter);
            MixV(inputs.CentreFootprint); MixV(inputs.PersonalFootprint); MixV(inputs.ContestedFootprint);
            Mix(inputs.DepositRadius); Mix(inputs.CollectReach);
            Mix(inputs.PersonalFood); Mix(inputs.ContestedFood); Mix(inputs.CentreFood);
            Mix(arms.Length);
            foreach (var arm in arms) { MixV(arm.A); MixV(arm.B); }
            return h.ToString("x16");
        }

        [MenuItem(MenuPath, priority = 60)]
        private static void BakeFromMenu()
        {
            var map = Bake();
            Debug.Log($"[Balance] Oracle map baked to {AssetPath} (fingerprint {map.SceneFingerprint}).");
        }

        /// <summary>Bakes the live scene into <see cref="AssetPath"/>, creating or overwriting the asset, and returns it.</summary>
        public static OracleMapSO Bake()
        {
            var inputs = ReadInputs();
            var arms = ShippedMap.Arms(WallSeed);

            var wallA = new List<Vector2>();
            var wallB = new List<Vector2>();
            foreach (var arm in arms)
            {
                if (arm.A == arm.B) continue; // clipped to nothing; the generator builds no husk either
                wallA.Add(arm.A);
                wallB.Add(arm.B);
            }

            var places = BuildPlaces(inputs);
            float halfWidth = inputs.WallThickness * 0.5f + PinwheelLayout.ChickenRadius;
            float limit = PinwheelLayout.ArenaClampLimit(inputs.PlaneSize * 0.5f);

            int n = OracleMapSO.PlaceCount;
            var walk = new float[n * n];
            var jump = new float[n * n];
            var wa = wallA.ToArray();
            var wb = wallB.ToArray();
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    float straight = Vector2.Distance(places[i].Pos, places[j].Pos);
                    float path = OraclePaths.ShortestPath(places[i].Pos, places[j].Pos, wa, wb, halfWidth, limit);
                    if (float.IsInfinity(path))
                        throw new InvalidOperationException(
                            $"[OracleMapBaker] No walkable route between place {i} and place {j}: the walls seal the map. Nothing was baked.");

                    float rims = Radius(places[i], places[j].Pos - places[i].Pos, inputs)
                               + Radius(places[j], places[i].Pos - places[j].Pos, inputs);
                    walk[i * n + j] = Mathf.Max(0f, path - rims);
                    jump[i * n + j] = Mathf.Max(0f, straight - rims);
                }
            }

            var map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(AssetPath);
            bool isNew = map == null;
            if (isNew) map = ScriptableObject.CreateInstance<OracleMapSO>();

            map.Places = places;
            map.WallA = wa;
            map.WallB = wb;
            map.WallHalfWidth = halfWidth;
            map.BaseRadius = inputs.DepositRadius;
            map.CollectReach = inputs.CollectReach;
            map.PersonalFood = inputs.PersonalFood;
            map.ContestedFood = inputs.ContestedFood;
            map.CentreFood = inputs.CentreFood;
            map.WalkLegs = walk;
            map.JumpLegs = jump;
            map.SceneFingerprint = Fingerprint(inputs, arms);

            if (isNew)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AssetPath));
                AssetDatabase.CreateAsset(map, AssetPath);
            }
            else
            {
                EditorUtility.SetDirty(map);
            }
            AssetDatabase.SaveAssets();
            return map;
        }

        private static OraclePlace[] BuildPlaces(Inputs inputs)
        {
            var piles = ShippedMap.ShippedPiles(Vector2.zero, Vector2.zero); // centre, personal 0-3, contested 0-3
            var corners = ShippedMap.Corners();
            var places = new List<OraclePlace>(OracleMapSO.PlaceCount);

            for (int i = 0; i < piles.Count; i++)
            {
                OraclePlaceKind kind = i == 0 ? OraclePlaceKind.Centre : i <= 4 ? OraclePlaceKind.Personal : OraclePlaceKind.Contested;
                int seat = i == 0 ? -1 : (i - 1) % OracleMapSO.SeatCount;
                places.Add(new OraclePlace
                {
                    Kind = kind, Seat = seat,
                    Pos = new Vector2(piles[i].Pos.x, piles[i].Pos.z),
                    Footprint = piles[i].Footprint, Yaw = piles[i].Yaw,
                });
            }
            for (int s = 0; s < OracleMapSO.SeatCount; s++)
            {
                var spawn = MapGenerator.SpawnPointNominal(corners[s]);
                places.Add(new OraclePlace { Kind = OraclePlaceKind.Base, Seat = s, Pos = new Vector2(spawn.x, spawn.z) });
            }
            return places.ToArray();
        }

        /// <summary>Distance from a place's centre that need not be walked: the base deposit radius, or the pile's oval toward <paramref name="toward"/> plus peck reach (same rule as <c>BalanceOracle</c>).</summary>
        private static float Radius(OraclePlace place, Vector2 toward, Inputs inputs) =>
            place.Kind == OraclePlaceKind.Base
                ? inputs.DepositRadius
                : PileFootprintShape.RadiusToward(toward, place.Yaw, place.Footprint) + inputs.CollectReach;
    }
}
