using System.Collections.Generic;
using CluckWars.Balance;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Synthetic maps, kits and setups for the match simulator tests. Every number is the caller's: nothing here is read from a game
    /// asset. Maps created here are destroyed by <see cref="Release"/> (call it from a TearDown).
    /// </summary>
    internal static class MatchTestKit
    {
        public const float BaseRadius = 1.5f;
        public const float Reach = 1f;

        private static readonly List<Object> Made = new List<Object>();

        public static void Release()
        {
            foreach (var o in Made) if (o != null) Object.DestroyImmediate(o);
            Made.Clear();
        }

        /// <summary>13 places on a square of half side <paramref name="half"/>, no walls.</summary>
        public static OracleMapSO OpenMap(float half) => Build(half, null, null, 0f);

        /// <summary>The open map plus one wall segment a-b that a chicken centre keeps <paramref name="halfWidth"/> away from.</summary>
        public static OracleMapSO OneWallMap(float half, Vector2 a, Vector2 b, float halfWidth) =>
            Build(half, new[] { a }, new[] { b }, halfWidth);

        private static OracleMapSO Build(float half, Vector2[] wallA, Vector2[] wallB, float halfWidth)
        {
            var map = ScriptableObject.CreateInstance<OracleMapSO>();
            Made.Add(map);
            map.Places = new OraclePlace[OracleMapSO.PlaceCount];
            map.Places[OracleMapSO.CentreIndex] = new OraclePlace { Kind = OraclePlaceKind.Centre, Seat = -1, Pos = Vector2.zero };
            for (int s = 0; s < 4; s++)
            {
                var dir = new Vector2(Mathf.Cos(Mathf.PI * 0.5f * s), Mathf.Sin(Mathf.PI * 0.5f * s));
                var edge = new Vector2(Mathf.Cos(Mathf.PI * 0.5f * (s + 0.5f)), Mathf.Sin(Mathf.PI * 0.5f * (s + 0.5f)));
                map.Places[OracleMapSO.PersonalIndex(s)] = new OraclePlace { Kind = OraclePlaceKind.Personal, Seat = s, Pos = dir * half * 0.45f };
                map.Places[OracleMapSO.ContestedIndex(s)] = new OraclePlace { Kind = OraclePlaceKind.Contested, Seat = s, Pos = edge * half * 0.7f };
                map.Places[OracleMapSO.BaseIndex(s)] = new OraclePlace { Kind = OraclePlaceKind.Base, Seat = s, Pos = dir * half * 0.8f };
            }
            map.WallA = wallA ?? new Vector2[0];
            map.WallB = wallB ?? new Vector2[0];
            map.WallHalfWidth = halfWidth;
            map.BaseRadius = BaseRadius;
            map.CollectReach = Reach;
            map.SceneFingerprint = "synthetic";

            int n = OracleMapSO.PlaceCount;
            map.WalkLegs = new float[n * n];
            map.JumpLegs = new float[n * n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    var a = map.Places[i].Pos;
                    var b = map.Places[j].Pos;
                    float path = OraclePaths.ShortestPath(a, b, map.WallA, map.WallB, halfWidth, half);
                    float rims = MatchGeometry.Rim(map, i, b - a) + MatchGeometry.Rim(map, j, a - b);
                    map.WalkLegs[i * n + j] = Mathf.Max(0f, path - rims);
                    map.JumpLegs[i * n + j] = Mathf.Max(0f, Vector2.Distance(a, b) - rims);
                }
            return map;
        }

        public static MatchKit Farmer(string label, float speed, int capacity, float peckAmount, float peckCooldown, float castSeconds, float depositRate) =>
            new MatchKit
            {
                Label = label,
                Class = ChickenClass.Warrior,
                Farm = new OracleKit
                {
                    Label = label, Forages = true, MoveSpeed = speed, CargoCapacity = capacity, PeckAmount = peckAmount,
                    PeckCooldown = peckCooldown, PeckCastSeconds = castSeconds, DepositRate = depositRate,
                },
            };

        /// <summary>
        /// A setup with the given seats (null = empty seat), one policy for every present seat, no reaction delay, a think on every tick,
        /// a 30 s clock, a win at 40, piles of 10 / 15 / 40 and no pile slow. Change <see cref="MatchSetup.Rules"/> after the call to taste.
        /// </summary>
        public static MatchSetup Setup(OracleMapSO map, MatchKit[] seats, PolicyFactory policy, float arenaHalf)
        {
            const float tick = 1f / 30f;
            var policies = new PolicyFactory[4];
            var param = new PolicyParams[4];
            for (int s = 0; s < 4; s++)
            {
                policies[s] = seats[s] != null ? policy : null;
                param[s] = PolicyParams.Default;
                param[s].ReactionDelay = 0f;
            }
            return new MatchSetup
            {
                Map = map,
                Nav = OracleNav.For(map, arenaHalf),
                Food = new OracleFood { Personal = 10f, Contested = 15f, Centre = 40f },
                Rules = new MatchRules
                {
                    Farm = new FarmRules { WinTarget = 40f, ClockSeconds = 30f, TickSeconds = tick, PileSlowFactor = 1f },
                    ArenaHalf = arenaHalf,
                    ThinkSeconds = tick,
                },
                Seats = seats,
                Policies = policies,
                Params = param,
            };
        }
    }
}
