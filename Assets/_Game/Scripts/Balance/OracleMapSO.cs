using System;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    public enum OraclePlaceKind : byte { Centre, Personal, Contested, Base }

    /// <summary>One place on the baked map. <see cref="Seat"/> is the owning corner for Personal and Base, the edge index for Contested, -1 for the Centre.</summary>
    [Serializable]
    public struct OraclePlace
    {
        public OraclePlaceKind Kind;
        public int Seat;
        public Vector2 Pos;
        public Vector2 Footprint;
        public float Yaw;
    }

    /// <summary>
    /// The shipped arena as data for the Balance Oracle: places, wall segments and the wall-aware leg
    /// tables between every pair of places. Baked from <c>Game.unity</c> by <c>Cluck Wars/Balance/Oracle/Bake map</c>;
    /// a test fails when the bake no longer matches the scene. Spec 5.1.
    /// </summary>
    /// <remarks>
    /// Place order is fixed: 0 centre, 1-4 personal piles by seat, 5-8 contested piles by edge
    /// (edge e lies between corner e and corner e+1), 9-12 the bases by seat. A leg is the walk
    /// between the two rims (base deposit radius, or the pile's oval toward the other end plus peck
    /// reach), so it is what a chicken actually travels. <see cref="JumpLegs"/> use the straight
    /// distance: a jumping kit crosses arms (an approximation, stated in the report).
    /// </remarks>
    public sealed class OracleMapSO : ScriptableObject
    {
        public const int CentreIndex = 0;
        public const int PileCount = 9;
        public const int SeatCount = 4;
        public const int PlaceCount = 13;

        public static int PersonalIndex(int seat) => 1 + seat;
        public static int ContestedIndex(int edge) => 5 + edge;
        public static int BaseIndex(int seat) => 9 + seat;

        public OraclePlace[] Places;

        /// <summary>Wall segments, end A and end B (same index = same segment).</summary>
        public Vector2[] WallA;
        public Vector2[] WallB;

        /// <summary>Half the wall thickness plus the chicken body radius: the clearance a chicken's centre keeps from a wall's axis.</summary>
        public float WallHalfWidth;

        public float BaseRadius;
        public float CollectReach;

        /// <summary>Starting food of each pile kind as <c>Game.unity</c> authors it. Read by <c>OracleKits.Food</c> until the amounts move to <c>MatchConfigSO</c>.</summary>
        public float PersonalFood;
        public float ContestedFood;
        public float CentreFood;

        /// <summary>13 x 13, row-major, metres between rims; walls respected.</summary>
        public float[] WalkLegs;

        /// <summary>13 x 13, row-major, metres between rims; straight line (a jumping kit crosses arms).</summary>
        public float[] JumpLegs;

        /// <summary>Hash of the scene inputs the bake was made from; compared by a test against the live scene.</summary>
        public string SceneFingerprint;

        public float Leg(int from, int to, bool jumps)
        {
            var table = jumps ? JumpLegs : WalkLegs;
            return table[from * PlaceCount + to];
        }
    }
}
