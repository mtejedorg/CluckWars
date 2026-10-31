using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>One claimed base's no-entry disc on the arena plane (world XZ).</summary>
    public readonly struct KeepOutCircle
    {
        public readonly Vector2 Center;
        public readonly float Radius;
        /// <summary>The base's corner: the one chicken whose <c>HomeCornerIndex</c> matches walks in freely.</summary>
        public readonly int Corner;

        public KeepOutCircle(Vector2 center, float radius, int corner)
        {
            Center = center;
            Radius = radius;
            Corner = corner;
        }

        public KeepOutCircle WithRadius(float radius) => new KeepOutCircle(Center, radius, Corner);
    }

    /// <summary>
    /// The base wall, pure: a chicken cannot stand inside another chicken's claimed base. This is the body half (abilities
    /// are <see cref="BaseWallRules"/>), and nothing here touches the deposit radius or the SCT trip. Deterministic and allocation-free.
    /// </summary>
    /// <remarks>
    /// The correction is the nearest point outside every rival disc, so walking at a base keeps the tangential part of the
    /// step (the bird slides round the wall) and loses the radial part. A point already on the edge is left alone, which
    /// is what stops a bird pressing against the wall from jittering.
    /// </remarks>
    public static class BaseKeepOutRules
    {
        /// <summary>
        /// Slack on "inside". A corrected bird lands exactly on the edge and float error must not read as a fresh intrusion
        /// next tick. 1 mm: far below anything visible.
        /// </summary>
        public const float EdgeSlack = 0.001f;

        /// <summary>How far outside a rival wall a bot aims and steers, so it stops short rather than grinding the edge.</summary>
        public const float BotClearance = 0.4f;

        private static bool Applies(in KeepOutCircle c, int ownCorner) => c.Corner != ownCorner && c.Radius > 0f;

        private static bool IsInside(Vector2 p, KeepOutCircle[] circles, int count, int ownCorner)
        {
            for (int i = 0; i < count; i++)
            {
                if (!Applies(circles[i], ownCorner)) continue;
                float r = circles[i].Radius - EdgeSlack;
                if ((p - circles[i].Center).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        /// <summary>
        /// True and the corrected position when <paramref name="position"/> is inside a rival disc; false when it may stay.
        /// Y is untouched. Overlapping discs: the nearest point outside all of them is either a radial projection or a point
        /// where two edges cross, so those are the only candidates. The exact centre has no radial direction and uses the
        /// way to the arena's middle, the one side of a corner base that is always open.
        /// </summary>
        public static bool TryCorrect(Vector3 position, KeepOutCircle[] circles, int count, int ownCorner, out Vector3 corrected)
        {
            corrected = position;
            var p = new Vector2(position.x, position.z);
            if (!IsInside(p, circles, count, ownCorner)) return false;

            float bestSqr = float.MaxValue;
            var best = p;
            void Offer(Vector2 candidate)
            {
                float d = (candidate - p).sqrMagnitude;
                if (d < bestSqr && !IsInside(candidate, circles, count, ownCorner)) { bestSqr = d; best = candidate; }
            }

            for (int i = 0; i < count; i++)
            {
                if (!Applies(circles[i], ownCorner)) continue;
                Offer(circles[i].Center + AwayFrom(p, circles[i].Center) * circles[i].Radius);
                for (int j = i + 1; j < count; j++)
                {
                    if (!Applies(circles[j], ownCorner)) continue;
                    if (!TryCrossing(circles[i], circles[j], out var a, out var b)) continue;
                    Offer(a);
                    Offer(b);
                }
            }

            corrected = new Vector3(best.x, position.y, best.y);
            return true;
        }

        private static Vector2 AwayFrom(Vector2 p, Vector2 center)
        {
            var away = p - center;
            if (away.sqrMagnitude > 1e-8f) return away.normalized;
            return center.sqrMagnitude > 1e-8f ? -center.normalized : Vector2.right;
        }

        private static bool TryCrossing(in KeepOutCircle a, in KeepOutCircle b, out Vector2 first, out Vector2 second)
        {
            first = second = default;
            var between = b.Center - a.Center;
            float d = between.magnitude;
            if (d < 1e-5f || d > a.Radius + b.Radius || d < Mathf.Abs(a.Radius - b.Radius)) return false;
            float along = (d * d + a.Radius * a.Radius - b.Radius * b.Radius) / (2f * d);
            float high = Mathf.Sqrt(Mathf.Max(0f, a.Radius * a.Radius - along * along));
            var dir = between / d;
            var mid = a.Center + dir * along;
            var normal = new Vector2(-dir.y, dir.x);
            first = mid + normal * high;
            second = mid - normal * high;
            return true;
        }

        /// <summary>
        /// Where a bot should steer on the way to <paramref name="target"/>: the target itself when the straight line is clear,
        /// else the tangent of the first rival disc the line crosses, on the shorter side that stays in the arena. Keeps a base
        /// out of a path the shared NavMesh knows nothing about. Each bot passes discs inflated by <see cref="BotClearance"/>.
        /// </summary>
        public static Vector3 SteerAround(Vector3 self, Vector3 target, KeepOutCircle[] circles, int count, int ownCorner, float arenaLimit)
        {
            var from = new Vector2(self.x, self.z);
            var to = new Vector2(target.x, target.z);
            var step = to - from;
            float length = step.magnitude;
            if (length < 1e-4f) return target;
            var dir = step / length;

            int hit = -1;
            float hitAlong = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!Applies(circles[i], ownCorner)) continue;
                var toCenter = circles[i].Center - from;
                float along = Vector2.Dot(toCenter, dir);
                if (along <= 0f || along >= length + circles[i].Radius) continue;
                float off = toCenter.sqrMagnitude - along * along;
                if (off < circles[i].Radius * circles[i].Radius && along < hitAlong) { hit = i; hitAlong = along; }
            }
            if (hit < 0) return target;

            var c = circles[hit];
            var out0 = from - c.Center;
            float dist = out0.magnitude;
            Vector2 left, right;
            if (dist <= c.Radius)
            {
                // Already on the ring: walk along it.
                var radial = dist > 1e-5f ? out0 / dist : AwayFrom(from, c.Center);
                var tangent = new Vector2(-radial.y, radial.x);
                left = from + tangent * c.Radius;
                right = from - tangent * c.Radius;
            }
            else
            {
                float baseAngle = Mathf.Atan2(out0.y, out0.x);
                float spread = Mathf.Acos(Mathf.Clamp01(c.Radius / dist));
                float reach = c.Radius * 1.02f;
                left = c.Center + new Vector2(Mathf.Cos(baseAngle + spread), Mathf.Sin(baseAngle + spread)) * reach;
                right = c.Center + new Vector2(Mathf.Cos(baseAngle - spread), Mathf.Sin(baseAngle - spread)) * reach;
            }

            bool leftOpen = InArena(left, arenaLimit), rightOpen = InArena(right, arenaLimit);
            float leftCost = (left - from).magnitude + (to - left).magnitude;
            float rightCost = (right - from).magnitude + (to - right).magnitude;
            bool useLeft = leftOpen == rightOpen ? leftCost <= rightCost : leftOpen;
            var way = useLeft ? left : right;
            return new Vector3(way.x, target.y, way.y);
        }

        private static bool InArena(Vector2 p, float limit) => Mathf.Abs(p.x) <= limit && Mathf.Abs(p.y) <= limit;
    }
}
