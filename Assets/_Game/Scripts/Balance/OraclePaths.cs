using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// Shortest walkable path between two points among wall segments, for the Oracle map bake.
    /// Pure geometry, no Unity objects. Each wall is a capsule of radius <c>halfWidth</c> round its axis
    /// (half the wall thickness plus the chicken's body radius, so the chicken's centre never enters it).
    /// </summary>
    public static class OraclePaths
    {
        /// <summary>Vertices per wall end; the polygon circumscribes the capsule's round end, so a path round it errs by under 2 % of the radius.</summary>
        private const int VerticesPerEnd = 16;

        /// <summary>Slack for a path that touches a capsule exactly (tangent lines, polygon chords).</summary>
        private const float Tolerance = 1e-3f;

        /// <summary>
        /// Straight distance when the segment a-b is clear; otherwise the shortest route over a visibility
        /// graph whose nodes ring each wall end. <paramref name="arenaHalf"/> is the half side of the box the
        /// chicken's centre may use. Returns <c>+Infinity</c> when no route exists.
        /// </summary>
        public static float ShortestPath(Vector2 a, Vector2 b, Vector2[] wallA, Vector2[] wallB, float halfWidth, float arenaHalf)
        {
            int walls = wallA == null ? 0 : wallA.Length;
            if (Clear(a, b, wallA, wallB, walls, halfWidth)) return Vector2.Distance(a, b);

            var nodes = new System.Collections.Generic.List<Vector2>(2 + walls * 2 * VerticesPerEnd) { a, b };
            nodes.AddRange(RingNodes(wallA, wallB, halfWidth, arenaHalf));

            int n = nodes.Count;
            var dist = new float[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) dist[i] = float.PositiveInfinity;
            dist[0] = 0f;

            for (int step = 0; step < n; step++)
            {
                int u = -1;
                for (int i = 0; i < n; i++)
                    if (!done[i] && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0 || float.IsPositiveInfinity(dist[u])) break;
                if (u == 1) return dist[1];
                done[u] = true;

                for (int v = 0; v < n; v++)
                {
                    if (done[v]) continue;
                    float nd = dist[u] + Vector2.Distance(nodes[u], nodes[v]);
                    if (nd >= dist[v]) continue;
                    if (!Clear(nodes[u], nodes[v], wallA, wallB, walls, halfWidth)) continue;
                    dist[v] = nd;
                }
            }
            return dist[1];
        }

        /// <summary>
        /// The ring vertices round every wall end, inside the arena box and outside every wall capsule: the nodes a path may bend at.
        /// Shared by <see cref="ShortestPath"/> and <c>OracleNav</c>, so the bake and the simulator walk the same graph.
        /// </summary>
        internal static System.Collections.Generic.List<Vector2> RingNodes(Vector2[] wallA, Vector2[] wallB, float halfWidth, float arenaHalf)
        {
            int walls = wallA == null ? 0 : wallA.Length;
            var nodes = new System.Collections.Generic.List<Vector2>(walls * 2 * VerticesPerEnd);
            float ring = halfWidth / Mathf.Cos(Mathf.PI / VerticesPerEnd);
            for (int w = 0; w < walls; w++)
            {
                for (int end = 0; end < 2; end++)
                {
                    Vector2 c = end == 0 ? wallA[w] : wallB[w];
                    for (int k = 0; k < VerticesPerEnd; k++)
                    {
                        float ang = 2f * Mathf.PI * k / VerticesPerEnd;
                        Vector2 p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ring;
                        if (Mathf.Abs(p.x) > arenaHalf || Mathf.Abs(p.y) > arenaHalf) continue;
                        if (!Clear(p, p, wallA, wallB, walls, halfWidth)) continue;
                        nodes.Add(p);
                    }
                }
            }
            return nodes;
        }

        /// <summary>True when the segment p-q stays at least <paramref name="halfWidth"/> from every wall axis.</summary>
        internal static bool Clear(Vector2 p, Vector2 q, Vector2[] wallA, Vector2[] wallB, float halfWidth) =>
            Clear(p, q, wallA, wallB, wallA == null ? 0 : wallA.Length, halfWidth);

        /// <summary>True when the segment p-q stays at least <paramref name="halfWidth"/> from every wall axis.</summary>
        private static bool Clear(Vector2 p, Vector2 q, Vector2[] wallA, Vector2[] wallB, int walls, float halfWidth)
        {
            float need = halfWidth - Tolerance;
            for (int w = 0; w < walls; w++)
                if (SegmentDistance(p, q, wallA[w], wallB[w]) < need) return false;
            return true;
        }

        private static float SegmentDistance(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
        {
            if (SegmentsIntersect(p1, q1, p2, q2)) return 0f;
            return Mathf.Min(
                Mathf.Min(PointSegment(p1, p2, q2), PointSegment(q1, p2, q2)),
                Mathf.Min(PointSegment(p2, p1, q1), PointSegment(q2, p1, q1)));
        }

        private static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-12f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        private static bool SegmentsIntersect(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
        {
            float d1 = Cross(p2, q2, p1), d2 = Cross(p2, q2, q1);
            float d3 = Cross(p1, q1, p2), d4 = Cross(p1, q1, q2);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }
}
