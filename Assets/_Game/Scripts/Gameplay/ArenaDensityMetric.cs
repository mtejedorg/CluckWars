using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Measures how OPEN the arena is, as free-run length: from a walkable point, in a given
    /// direction, how far can you run in a straight line before terrain or the boundary
    /// stops you?
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because "the walls don't affect gameplay enough" is a claim about
    /// distance, and distance is measurable. It is the metric <c>level-designer</c> used to
    /// quantify the 2026-08-14 arena rescale, reimplemented here as reusable, deterministic,
    /// EditMode-testable logic rather than a one-off script, so the next person to change
    /// arena density can reproduce the number instead of re-deriving the method.
    /// </para>
    /// <para>
    /// <b>What is modelled, and what deliberately is not.</b> Food piles are excluded —
    /// they are consumable (ADR 0003 Decision 2), so a density figure that counts them
    /// flatters the late game, which is precisely the phase where openness bites. Only
    /// permanent terrain counts: pinwheel arms, scatter cover, and the boundary. The ray is
    /// a POINT ray, not a chicken-width capsule: a body-width sweep answers "can I run
    /// there", while this answers "how much map is in one straight line", which is the
    /// sightline-and-commitment quantity the complaint is about.
    /// </para>
    /// <para>
    /// <b>The 38 m arena is the reference, not a floor to beat.</b> Denser than the 38 m
    /// figures is not automatically better — it means the arena has been over-corrected and
    /// the scatter counts should come down.
    /// </para>
    /// </remarks>
    public static class ArenaDensityMetric
    {
        /// <summary>
        /// Spacing of the origin grid. 1.5 m over a 51.3 m arena is ~1 200 origins, which
        /// with <see cref="DirectionCount"/> is ~43 000 rays - enough that mean and p90 are
        /// stable to two decimals, cheap enough to run inside the EditMode suite.
        /// </summary>
        public const float OriginStep = 1.5f;

        /// <summary>Directions sampled per origin, evenly spaced over the full circle (10 degrees apart).</summary>
        public const int DirectionCount = 36;

        /// <summary>March step. Matches the 0.25 m quantisation of the published reference figures.</summary>
        public const float MarchStep = 0.25f;

        /// <summary>Runs at or above this count as "a straight line still works here".</summary>
        public const float LongRunThreshold = 30f;

        /// <summary>An oriented, axis-agnostic terrain footprint on the XZ plane.</summary>
        public readonly struct Box
        {
            public readonly Vector2 Center;
            public readonly float Span;
            public readonly float Depth;
            public readonly float YawRadians;

            public Box(Vector2 center, float span, float depth, float yawRadians)
            {
                Center = center;
                Span = span;
                Depth = depth;
                YawRadians = yawRadians;
                _circumRadiusSq = 0.25f * (span * span + depth * depth);
            }

            /// <summary>Squared circumradius, cached so the hot loop rejects distant boxes without trig.</summary>
            private readonly float _circumRadiusSq;

            public bool Contains(Vector2 p)
            {
                float dx = p.x - Center.x, dz = p.y - Center.y;
                if (dx * dx + dz * dz > _circumRadiusSq) return false;   // cheap reject

                float c = Mathf.Cos(YawRadians), s = Mathf.Sin(YawRadians);
                float lx =  dx * c + dz * s;
                float lz = -dx * s + dz * c;
                return Mathf.Abs(lx) <= Span * 0.5f && Mathf.Abs(lz) <= Depth * 0.5f;
            }
        }

        /// <summary>The distribution of free-run lengths over every sampled (origin, direction).</summary>
        public readonly struct Reading
        {
            public readonly float Mean;
            public readonly float P90;
            /// <summary>Fraction of samples at or above <see cref="LongRunThreshold"/>, 0..1.</summary>
            public readonly float LongRunFraction;
            public readonly int SampleCount;

            public Reading(float mean, float p90, float longRunFraction, int sampleCount)
            {
                Mean = mean;
                P90 = p90;
                LongRunFraction = longRunFraction;
                SampleCount = sampleCount;
            }

            public override string ToString() =>
                $"mean {Mean,6:0.00}   p90 {P90,6:0.00}   >{LongRunThreshold:0}m {LongRunFraction * 100f,5:0.0}%   (n={SampleCount})";
        }

        /// <summary>Converts the shipped geometry types into the metric's box list.</summary>
        public static List<Box> CollectTerrain(WallSegment[] arms, float armThickness, ScatterBox[] scatter)
        {
            var boxes = new List<Box>();

            if (arms != null)
            {
                foreach (var arm in arms)
                {
                    if (arm.A == arm.B) continue;   // collapsed arm — nothing was built
                    Vector2 delta = arm.B - arm.A;
                    boxes.Add(new Box(
                        arm.A + delta * 0.5f,
                        delta.magnitude,
                        armThickness,
                        Mathf.Atan2(delta.y, delta.x)));
                }
            }

            if (scatter != null)
            {
                foreach (var s in scatter)
                    boxes.Add(new Box(s.Center, s.Span, s.Depth, s.YawDegrees * Mathf.Deg2Rad));
            }

            return boxes;
        }

        public static Reading Measure(float arenaHalfSize, IReadOnlyList<Box> terrain)
        {
            var samples = new List<float>();

            // Half-step direction offset so no ray runs exactly along a world axis. The
            // boundary is axis-aligned and the arms sit on 22.5° multiples, so axis-aligned
            // rays land on degenerate grazing cases and skew the tail.
            float dirStep = 2f * Mathf.PI / DirectionCount;
            var directions = new Vector2[DirectionCount];
            for (int d = 0; d < DirectionCount; d++)
            {
                float a = (d + 0.5f) * dirStep;
                directions[d] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            int steps = Mathf.CeilToInt(arenaHalfSize / OriginStep);
            for (int ix = -steps; ix <= steps; ix++)
            for (int iz = -steps; iz <= steps; iz++)
            {
                var origin = new Vector2(ix * OriginStep, iz * OriginStep);
                if (Mathf.Abs(origin.x) > arenaHalfSize || Mathf.Abs(origin.y) > arenaHalfSize) continue;
                if (IsBlocked(origin, terrain)) continue;

                for (int d = 0; d < DirectionCount; d++)
                    samples.Add(RunLength(origin, directions[d], arenaHalfSize, terrain));
            }

            return Summarise(samples);
        }

        /// <summary>
        /// Distance from <paramref name="origin"/> along <paramref name="direction"/> before
        /// the first blocked step. Marched rather than solved analytically: the terrain set
        /// is a handful of boxes and marching keeps the definition of "blocked" identical to
        /// the one used to reject origins, so the two cannot disagree.
        /// </summary>
        public static float RunLength(Vector2 origin, Vector2 direction, float arenaHalfSize, IReadOnlyList<Box> terrain)
        {
            direction = direction.normalized;
            float travelled = 0f;

            while (true)
            {
                float next = travelled + MarchStep;
                Vector2 p = origin + direction * next;

                if (Mathf.Abs(p.x) > arenaHalfSize || Mathf.Abs(p.y) > arenaHalfSize) return travelled;
                if (IsBlocked(p, terrain)) return travelled;

                travelled = next;
            }
        }

        private static bool IsBlocked(Vector2 p, IReadOnlyList<Box> terrain)
        {
            for (int i = 0; i < terrain.Count; i++)
                if (terrain[i].Contains(p)) return true;
            return false;
        }

        private static Reading Summarise(List<float> samples)
        {
            if (samples.Count == 0) return new Reading(0f, 0f, 0f, 0);

            double total = 0d;
            int longRuns = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                total += samples[i];
                if (samples[i] >= LongRunThreshold) longRuns++;
            }

            samples.Sort();
            int p90Index = Mathf.Clamp(Mathf.CeilToInt(0.9f * samples.Count) - 1, 0, samples.Count - 1);

            return new Reading(
                (float)(total / samples.Count),
                samples[p90Index],
                longRuns / (float)samples.Count,
                samples.Count);
        }
    }
}
