using System;
using System.Collections.Generic;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// Wall-aware walking for the match simulator: the visibility graph of <see cref="OraclePaths"/> (ring nodes round every
    /// wall end, capsule clearance), with all-pairs node distances and a distance field per baked place computed once.
    /// Built from an <see cref="OracleMapSO"/> and the arena half side; read-only afterwards, so any number of threads may share one.
    /// </summary>
    public sealed class OracleNav
    {
        private static readonly Dictionary<string, OracleNav> Cache = new Dictionary<string, OracleNav>();

        /// <summary>Goals closer than this to a baked place centre use that place's cached field.</summary>
        private const float PlaceMatch = 1e-3f;

        /// <summary>A node this close to the walker counts as the spot it stands on.</summary>
        private const float HereMetres = 1e-4f;

        [ThreadStatic] private static float[] _scratch;

        private readonly Vector2[] _wallA;
        private readonly Vector2[] _wallB;
        private readonly float _halfWidth;
        private readonly Vector2[] _nodes;
        private readonly float[] _all;           // n x n shortest distances between ring nodes
        private readonly Vector2[] _places;
        private readonly float[][] _placeField;  // per place: distance from each node to the place centre

        private OracleNav(OracleMapSO map, float arenaHalf)
        {
            _wallA = map.WallA ?? Array.Empty<Vector2>();
            _wallB = map.WallB ?? Array.Empty<Vector2>();
            _halfWidth = map.WallHalfWidth;
            _nodes = OraclePaths.RingNodes(_wallA, _wallB, _halfWidth, arenaHalf).ToArray();

            int n = _nodes.Length;
            _all = new float[n * n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) _all[i * n + j] = i == j ? 0f : float.PositiveInfinity;
                for (int j = 0; j < i; j++)
                {
                    if (!IsClear(_nodes[i], _nodes[j])) continue;
                    float d = Vector2.Distance(_nodes[i], _nodes[j]);
                    _all[i * n + j] = _all[j * n + i] = d;
                }
            }
            for (int k = 0; k < n; k++)
                for (int i = 0; i < n; i++)
                {
                    float ik = _all[i * n + k];
                    if (float.IsPositiveInfinity(ik)) continue;
                    for (int j = 0; j < n; j++)
                    {
                        float via = ik + _all[k * n + j];
                        if (via < _all[i * n + j]) _all[i * n + j] = via;
                    }
                }

            int places = map.Places == null ? 0 : map.Places.Length;
            _places = new Vector2[places];
            _placeField = new float[places][];
            for (int p = 0; p < places; p++)
            {
                _places[p] = map.Places[p].Pos;
                _placeField[p] = BuildField(_places[p]);
            }
        }

        /// <summary>
        /// The navigation of one map at one arena size, built on first use and shared after. The key is the scene fingerprint
        /// plus a hash of the walls, so two synthetic maps that carry no fingerprint never share a graph.
        /// </summary>
        public static OracleNav For(OracleMapSO map, float arenaHalf)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            string key = map.SceneFingerprint + "|" + arenaHalf.ToString("R") + "|" + WallHash(map);
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out var nav))
                {
                    nav = new OracleNav(map, arenaHalf);
                    Cache[key] = nav;
                }
                return nav;
            }
        }

        private static string WallHash(OracleMapSO map)
        {
            ulong h = 14695981039346656037UL;
            void Mix(float v)
            {
                int bits = BitConverter.SingleToInt32Bits(v);
                for (int i = 0; i < 4; i++) { h ^= (byte)(bits >> (8 * i)); h *= 1099511628211UL; }
            }
            Mix(map.WallHalfWidth);
            if (map.WallA != null) foreach (var v in map.WallA) { Mix(v.x); Mix(v.y); }
            if (map.WallB != null) foreach (var v in map.WallB) { Mix(v.x); Mix(v.y); }
            if (map.Places != null) foreach (var p in map.Places) { Mix(p.Pos.x); Mix(p.Pos.y); }
            return h.ToString("x16");
        }

        /// <summary>True when the straight segment a-b keeps the wall clearance everywhere.</summary>
        public bool IsClear(Vector2 a, Vector2 b) => OraclePaths.Clear(a, b, _wallA, _wallB, _halfWidth);

        /// <summary>Half the wall's own thickness: the clearance <see cref="OracleMapSO.WallHalfWidth"/> minus the chicken's body radius (the bake adds it).</summary>
        private float WallBodyHalf => Math.Max(0f, _halfWidth - PinwheelLayout.ChickenRadius);

        /// <summary>
        /// True when a ball of <paramref name="radius"/> standing at <paramref name="p"/> touches no wall: the test of the game's
        /// <c>Physics.CheckSphere</c> for a spot, over the walls' own thickness (a wall's end is taken as round, a hair stricter than its box).
        /// </summary>
        public bool SpotClear(Vector2 p, float radius)
        {
            float need = WallBodyHalf + radius;
            for (int w = 0; w < _wallA.Length; w++)
                if (OraclePaths.PointSegment(p, _wallA[w], _wallB[w]) < need) return false;
            return true;
        }

        /// <summary>
        /// The nearest point a chicken's centre can stand at: <paramref name="p"/> when it is clear, else pushed out of every wall's
        /// clearance, else (a junction the push cannot resolve) the nearest ring node. Never throws, so a goal inside a wall still has a way to it.
        /// </summary>
        public Vector2 NearestClear(Vector2 p)
        {
            Vector2 q = PushOut(p, p);
            if (IsClear(q, q)) return q;
            if (_nodes.Length == 0) return p;
            int best = 0;
            for (int i = 1; i < _nodes.Length; i++)
                if ((_nodes[i] - p).sqrMagnitude < (_nodes[best] - p).sqrMagnitude) best = i;
            return _nodes[best];
        }

        /// <summary>
        /// Where a walker ends when it tries to go from <paramref name="from"/> to <paramref name="to"/>: <paramref name="to"/> when the
        /// way is clear; when it runs into a wall, the point of the wall's clearance edge nearest to <paramref name="to"/> (it slides
        /// along the wall, as the game's <c>CharacterController</c> does); <paramref name="from"/> when even that cannot be reached.
        /// </summary>
        public Vector2 Slide(Vector2 from, Vector2 to)
        {
            if (IsClear(from, to)) return to;
            Vector2 slid = PushOut(to, from);
            return IsClear(from, slid) ? slid : from;
        }

        // Moves p out of every wall's clearance, to the clearance edge along the line from the wall's nearest axis point. A few passes
        // settle a corner between two walls. `side` decides the direction when p lies exactly on an axis.
        private Vector2 PushOut(Vector2 p, Vector2 side)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                for (int w = 0; w < _wallA.Length; w++)
                {
                    Vector2 c = OraclePaths.ClosestOnSegment(p, _wallA[w], _wallB[w]);
                    Vector2 away = p - c;
                    float d = away.magnitude;
                    if (d >= _halfWidth) continue;
                    if (d < 1e-5f)
                    {
                        Vector2 axis = _wallB[w] - _wallA[w];
                        Vector2 normal = new Vector2(-axis.y, axis.x).normalized;
                        away = Vector2.Dot(side - c, normal) >= 0f ? normal : -normal;
                        d = 1f;
                    }
                    p = c + away / d * (_halfWidth + 1e-3f);
                    moved = true;
                }
                if (!moved) break;
            }
            return p;
        }

        /// <summary>Length of the shortest wall-aware walk from <paramref name="from"/> to <paramref name="to"/>; <c>+Infinity</c> when there is none.</summary>
        public float PathLength(Vector2 from, Vector2 to)
        {
            if (IsClear(from, to)) return Vector2.Distance(from, to);
            return BestHop(from, FieldFor(to), false, out _);
        }

        /// <summary>
        /// The point to walk to next: <paramref name="to"/> itself when the straight segment is clear, otherwise the ring node
        /// visible from <paramref name="from"/> that minimises the walk to <paramref name="to"/>. Throws when no route exists.
        /// </summary>
        public Vector2 NextPoint(Vector2 from, Vector2 to)
        {
            if (IsClear(from, to)) return to;
            float len = BestHop(from, FieldFor(to), true, out int node);
            if (node < 0 || float.IsPositiveInfinity(len))
                throw new InvalidOperationException($"OracleNav: no walkable route from ({from.x:0.##}, {from.y:0.##}) to ({to.x:0.##}, {to.y:0.##}).");
            return _nodes[node];
        }

        private float[] FieldFor(Vector2 to)
        {
            for (int p = 0; p < _places.Length; p++)
                if ((_places[p] - to).sqrMagnitude <= PlaceMatch * PlaceMatch) return _placeField[p];
            return BuildField(to);
        }

        // Per node: the shortest walk from the node to g (direct when visible, else through other nodes).
        private float[] BuildField(Vector2 g)
        {
            int n = _nodes.Length;
            var seen = new float[n];
            for (int j = 0; j < n; j++)
                seen[j] = IsClear(_nodes[j], g) ? Vector2.Distance(_nodes[j], g) : float.PositiveInfinity;
            var field = new float[n];
            for (int i = 0; i < n; i++)
            {
                float best = float.PositiveInfinity;
                for (int j = 0; j < n; j++)
                {
                    float v = _all[i * n + j] + seen[j];
                    if (v < best) best = v;
                }
                field[i] = best;
            }
            return field;
        }

        // min over nodes visible from `from` of |from - node| + field[node]. Candidates are tried best first by their
        // optimistic cost, so the first visible one is the answer and most calls test one or two segments.
        private float BestHop(Vector2 from, float[] field, bool skipHere, out int node)
        {
            int n = _nodes.Length;
            node = -1;
            if (n == 0) return float.PositiveInfinity;
            var cost = _scratch;
            if (cost == null || cost.Length < n) cost = _scratch = new float[Math.Max(n, 256)];
            for (int i = 0; i < n; i++)
            {
                float d = Vector2.Distance(from, _nodes[i]);
                // A walker standing on a node must move on to another one, or it would pick the node it is already on for ever.
                cost[i] = skipHere && d < HereMetres ? float.PositiveInfinity : d + field[i];
            }

            while (true)
            {
                int best = -1;
                float min = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                    if (cost[i] < min) { min = cost[i]; best = i; }
                if (best < 0) return float.PositiveInfinity;
                if (IsClear(from, _nodes[best])) { node = best; return min; }
                cost[best] = float.PositiveInfinity;
            }
        }
    }
}
