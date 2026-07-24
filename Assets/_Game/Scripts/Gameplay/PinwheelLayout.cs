using UnityEngine;

namespace CluckWars.Gameplay
{
    public struct WallSegment
    {
        public Vector2 A;
        public Vector2 B;
        public ObstacleClass Class;

        public WallSegment(Vector2 a, Vector2 b, ObstacleClass cls)
        {
            A = a;
            B = b;
            Class = cls;
        }
    }

    public static class PinwheelLayout
    {
        public static WallSegment[] Build(float arenaHalfSize, int wedges, int seed, float centerKeepClear, float baseKeepClear)
        {
            var segments = new WallSegment[wedges];
            var rng = new System.Random(seed);

            float baseDist = arenaHalfSize - 3f;
            if (baseDist < 0f) baseDist = 12f; // Fallback if arena is too small

            Vector2[] bases = new Vector2[]
            {
                new Vector2(baseDist, baseDist),
                new Vector2(-baseDist, baseDist),
                new Vector2(-baseDist, -baseDist),
                new Vector2(baseDist, -baseDist)
            };

            for (int i = 0; i < wedges; i++)
            {
                // Base angle for the wedge
                float angleStep = 2f * Mathf.PI / wedges;
                float baseAngle = i * angleStep;

                // Add a small jitter (e.g., up to 15% of the angle step in either direction)
                float jitter = (float)(rng.NextDouble() * 2.0 - 1.0) * angleStep * 0.15f;
                float angle = baseAngle + jitter;

                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                // We want to emit a radial segment
                // Start distance must be outside the center keep-clear disc
                // Let's add a small margin (e.g. 0.2f or 0.5f) to be safe
                float startDist = centerKeepClear + 0.2f;
                float endDist = arenaHalfSize - 1.0f; // Stay inside perimeter wall

                // Clip against each of the base keep-clear discs
                foreach (var b in bases)
                {
                    // Ray: P(t) = t * dir. We find intersection with circle centered at b with radius baseKeepClear.
                    // |t * dir - b|^2 = baseKeepClear^2
                    // t^2 - 2 * t * (dir . b) + |b|^2 - R^2 = 0
                    float dot = Vector2.Dot(dir, b);
                    float b2 = b.sqrMagnitude;
                    float r2 = baseKeepClear * baseKeepClear;
                    float c = b2 - r2;

                    // Discriminant of t^2 - 2*dot*t + c = 0
                    // t = [ 2*dot +/- sqrt(4*dot^2 - 4*c) ] / 2 = dot +/- sqrt(dot^2 - c)
                    float disc = dot * dot - c;
                    if (disc > 0f)
                    {
                        float sqrtDisc = Mathf.Sqrt(disc);
                        float t1 = dot - sqrtDisc;
                        float t2 = dot + sqrtDisc;

                        // We only care about intersections in front of the ray (t > 0)
                        if (t2 > 0f)
                        {
                            float entry = Mathf.Max(0f, t1);
                            // If the keep-clear disc overlaps our segment range [startDist, endDist], shorten it
                            if (entry > startDist && entry < endDist)
                            {
                                endDist = entry;
                            }
                            else if (entry <= startDist && t2 >= startDist)
                            {
                                // The keep-clear disc covers the start of our segment!
                                // Shorten from the other side, or shift the start
                                startDist = t2;
                            }
                        }
                    }
                }

                // If startDist is greater than or equal to endDist (highly unlikely, but possible if squeezed),
                // clamp them to a small valid segment outside the center KeepClear
                if (startDist >= endDist)
                {
                    startDist = centerKeepClear + 0.2f;
                    endDist = startDist + 0.5f;
                }

                Vector2 posA = dir * startDist;
                Vector2 posB = dir * endDist;

                // Assign ObstacleClass in a seeded pattern
                // We want to alternate: Low, Standard, and occasional Tall.
                // Let's use the rng to pick the class.
                double rVal = rng.NextDouble();
                ObstacleClass cls;
                if (rVal < 0.4) cls = ObstacleClass.Low;
                else if (rVal < 0.8) cls = ObstacleClass.Standard;
                else cls = ObstacleClass.Tall;

                segments[i] = new WallSegment(posA, posB, cls);
            }

            return segments;
        }
    }
}
