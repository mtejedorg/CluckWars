using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The geometry of a food pile's footprint (Phase 8 D9): an ELLIPSE with the authored
    /// X,Z size, yawed about Y. The visible mound, the solid collider, the NavMesh carve and
    /// every collect / approach distance are all derived from this one shape, so what a
    /// player sees, what they bump into and where they can peck from cannot disagree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until D9 the pile blocked with a <c>BoxCollider</c> the size of the footprint's bounding
    /// box: 0.7 m (personal) to 1.5 m (centre) larger than the visible oval at the diagonals,
    /// so a chicken stopped in mid-air beside the mound. Everything here is pure (no
    /// transform, no runner) so it is EditMode-testable; <see cref="FoodPile"/> feeds it its
    /// centre, yaw and current footprint.
    /// </para>
    /// <para>
    /// <b>Local frame.</b> Local +X is the footprint's first component (the LONG axis for every
    /// shipped pile), local +Z the second. World = <c>Quaternion.Euler(0, yaw, 0) * local</c>.
    /// </para>
    /// </remarks>
    public static class PileFootprintShape
    {
        /// <summary>Sides of the collider prism. 48 keeps the polygon within 0.2 % of the ellipse (8 mm on the 7.8 m centre pile).</summary>
        public const int PrismSides = 48;

        /// <summary>
        /// Axis-aligned (pile-local) boxes whose union approximates the ellipse for the NavMesh
        /// carve. <see cref="UnityEngine.AI.NavMeshObstacle"/> only offers a box or a round
        /// capsule (a capsule under the pile's non-uniform scale snaps to its larger axis), so
        /// the ellipse is a staircase of nested boxes. 10 boxes at <see cref="CarveOvershoot"/>
        /// keep the two-sided deviation to 3.7 % of the semi-major axis: 0.15 m on the centre
        /// pile, 0.08 m contested, 0.07 m personal. Well inside the bot standoff margin
        /// (standoff 0.7 - agent radius 0.5 = 0.2 m).
        /// </summary>
        public const int CarveBoxCount = 10;

        /// <summary>Radial scale of the carve boxes' corners, solved to minimise the two-sided (Hausdorff) error.</summary>
        public const float CarveOvershoot = 1.0375f;

        private static Mesh _unitPrism;

        /// <summary>
        /// Shared closed prism: a regular <see cref="PrismSides"/>-gon of radius 0.5 (the unit
        /// circle the pile root's X,Z scale stretches into the footprint ellipse), y in [0, 1].
        /// Lazily built, process-wide.
        /// </summary>
        public static Mesh UnitPrism
        {
            get
            {
                if (_unitPrism == null)
                {
                    _unitPrism = BuildUnitPrism(PrismSides);
                    _unitPrism.name = "PileFootprintPrism";
                    _unitPrism.hideFlags = HideFlags.HideAndDontSave;
                }
                return _unitPrism;
            }
        }

        /// <summary>
        /// Builds the collider prism. The polygon's vertices sit on radius
        /// <c>0.5 / sqrt(cos(pi/n))</c>, half-way (in radius) between inscribed and
        /// circumscribed, so the faces straddle the true ellipse instead of all lying inside it.
        /// </summary>
        public static Mesh BuildUnitPrism(int sides)
        {
            int n = Mathf.Max(3, sides);
            float r = 0.5f / Mathf.Sqrt(Mathf.Cos(Mathf.PI / n));

            var v = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                v[i] = p;                       // bottom ring
                v[n + i] = p + Vector3.up;      // top ring
            }

            // Caps as fans (n-2 tris each) + sides (2 per quad). Wound outward (Unity: clockwise seen from outside).
            var t = new int[(n - 2) * 3 * 2 + n * 6];
            int k = 0;
            for (int i = 1; i < n - 1; i++)
            {
                t[k++] = n; t[k++] = n + i + 1; t[k++] = n + i;     // top, seen from above
                t[k++] = 0; t[k++] = i; t[k++] = i + 1;             // bottom, seen from below
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                t[k++] = i; t[k++] = n + i; t[k++] = n + j;
                t[k++] = i; t[k++] = n + j; t[k++] = j;
            }

            var mesh = new Mesh { vertices = v, triangles = t };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Half extents, in the unit (radius 0.5) footprint space, of the nested carve boxes.
        /// Box k has its corner on the scaled unit circle at angle <c>(k + 1) * 90 / (count + 1)</c>
        /// degrees; the root's X,Z scale stretches the staircase onto the ellipse.
        /// </summary>
        public static Vector2[] CarveBoxHalfExtents(int count = CarveBoxCount, float overshoot = CarveOvershoot)
        {
            int n = Mathf.Max(1, count);
            var result = new Vector2[n];
            for (int k = 0; k < n; k++)
            {
                float a = (k + 1) * (0.5f * Mathf.PI) / (n + 1);
                result[k] = new Vector2(0.5f * overshoot * Mathf.Cos(a), 0.5f * overshoot * Mathf.Sin(a));
            }
            return result;
        }

        /// <summary>
        /// Yaw (degrees, in [0, 360)) that points an outer pile's long axis (local +X) at the
        /// arena centre. 0 for a pile at the centre, where no direction is preferred.
        /// </summary>
        /// <remarks>
        /// <c>Euler(0, yaw, 0) * right = (cos yaw, 0, -sin yaw)</c>, so the yaw that maps local +X
        /// onto the radial direction (x, z) is <c>atan2(-z, x)</c>. The ellipse itself (collider,
        /// NavMesh carve, every distance) is symmetric under a half turn, but the mound's and the
        /// disc's irregular outline is NOT, so since Phase 8 D15 the yaw keeps its full turn
        /// instead of folding into [0, 180): piles at 90-degree rotations of each other get yaws
        /// exactly 90 degrees apart, so the four seats' piles are rotated copies (the map is
        /// 4-fold rotationally symmetric) and opposite piles are NOT translated copies that look
        /// mirrored from their own seat. The value is only a stamped float: the networked
        /// <c>FoodPile.Yaw</c> semantics (degrees about Y, applied to the root on every peer) are unchanged.
        /// </remarks>
        public static float RadialLongAxisYaw(Vector3 position)
        {
            if (position.x * position.x + position.z * position.z < 1e-8f) return 0f;
            float yaw = Mathf.Atan2(-position.z, position.x) * Mathf.Rad2Deg;
            if (yaw < 0f) yaw += 360f;
            return yaw >= 360f ? 0f : yaw;
        }

        /// <summary>True when the pile-local point lies inside (or on) the ellipse with semi-axes a, b.</summary>
        public static bool InsideEllipse(float x, float z, float a, float b)
        {
            a = Mathf.Max(a, 1e-4f);
            b = Mathf.Max(b, 1e-4f);
            float u = x / a, w = z / b;
            return u * u + w * w <= 1f;
        }

        /// <summary>
        /// Nearest point ON the ellipse (semi-axes a, b, centred at the origin) to a pile-local
        /// point, inside or outside. Trig-free fixed-point iteration on the evolute; converges to
        /// well under a millimetre at these sizes in 6 steps. Works for either a &gt;= b or a &lt; b.
        /// </summary>
        public static Vector2 ClosestPointOnEllipse(float px, float pz, float a, float b)
        {
            a = Mathf.Max(a, 1e-4f);
            b = Mathf.Max(b, 1e-4f);
            float x = Mathf.Abs(px), z = Mathf.Abs(pz);
            float tx = 0.70710678f, tz = 0.70710678f;

            for (int i = 0; i < 6; i++)
            {
                float ex = (a * a - b * b) * tx * tx * tx / a;
                float ez = (b * b - a * a) * tz * tz * tz / b;
                float rx = a * tx - ex, rz = b * tz - ez;
                float qx = x - ex, qz = z - ez;
                float r = Mathf.Sqrt(rx * rx + rz * rz);
                float q = Mathf.Sqrt(qx * qx + qz * qz);
                if (q < 1e-6f) break;
                tx = Mathf.Clamp01((qx * r / q + ex) / a);
                tz = Mathf.Clamp01((qz * r / q + ez) / b);
                float t = Mathf.Sqrt(tx * tx + tz * tz);
                if (t < 1e-6f) { tx = 1f; tz = 0f; break; }
                tx /= t; tz /= t;
            }

            float cx = a * tx, cz = b * tz;
            return new Vector2(px < 0f ? -cx : cx, pz < 0f ? -cz : cz);
        }

        /// <summary>World point -> pile-local XZ (centre removed, yaw undone).</summary>
        public static Vector2 ToLocal(Vector3 worldPos, Vector3 centre, float yawDeg)
        {
            var d = Quaternion.Euler(0f, -yawDeg, 0f) * new Vector3(worldPos.x - centre.x, 0f, worldPos.z - centre.z);
            return new Vector2(d.x, d.z);
        }

        /// <summary>Pile-local XZ -> world point at <paramref name="y"/>.</summary>
        public static Vector3 ToWorld(Vector2 local, Vector3 centre, float yawDeg, float y)
        {
            var d = Quaternion.Euler(0f, yawDeg, 0f) * new Vector3(local.x, 0f, local.y);
            return new Vector3(centre.x + d.x, y, centre.z + d.z);
        }

        /// <summary>
        /// Radius of the footprint ellipse from its centre along a WORLD XZ direction
        /// (<paramref name="dirXZ"/> need not be normalised; a zero vector gives the first semi-axis).
        /// </summary>
        public static float RadiusToward(Vector2 dirXZ, float yawDeg, Vector2 footprint)
        {
            float a = footprint.x * 0.5f, b = footprint.y * 0.5f;
            if (a <= 0f || b <= 0f) return 0f;
            if (dirXZ.sqrMagnitude < 1e-12f) return a;
            var local = ToLocal(new Vector3(dirXZ.x, 0f, dirXZ.y), Vector3.zero, yawDeg);
            float c = local.x, s = local.y;
            float len2 = c * c + s * s;
            // r(theta) = ab / sqrt(b^2 cos^2 + a^2 sin^2), with cos/sin from the unnormalised local vector.
            return a * b * Mathf.Sqrt(len2) / Mathf.Sqrt(b * b * c * c + a * a * s * s);
        }

        /// <summary>
        /// Shortest XZ distance from <paramref name="worldPos"/> to the footprint ellipse's
        /// surface; 0 inside. This is what makes collection surface-relative (a chicken pressed
        /// against any side of a pile is the same ~0.58 m away) now that the surface is an oval.
        /// </summary>
        public static float DistanceToSurfaceXZ(Vector3 worldPos, Vector3 centre, float yawDeg, Vector2 footprint)
        {
            float a = footprint.x * 0.5f, b = footprint.y * 0.5f;
            var p = ToLocal(worldPos, centre, yawDeg);
            if (InsideEllipse(p.x, p.y, a, b)) return 0f;
            var c = ClosestPointOnEllipse(p.x, p.y, a, b);
            return Vector2.Distance(p, c);
        }

        /// <summary>
        /// The walkable point a mover should head for: the footprint point nearest
        /// <paramref name="from"/>, pushed <paramref name="standoff"/> outward along the surface
        /// normal. Never the centre and never NaN; a mover inside the footprint is sent out
        /// through the nearest point of the rim.
        /// </summary>
        public static Vector3 ApproachPoint(Vector3 from, Vector3 centre, float yawDeg, Vector2 footprint, float standoff)
        {
            float a = Mathf.Max(footprint.x * 0.5f, 0.001f), b = Mathf.Max(footprint.y * 0.5f, 0.001f);
            var p = ToLocal(from, centre, yawDeg);
            var c = ClosestPointOnEllipse(p.x, p.y, a, b);

            Vector2 outward;
            var off = p - c;
            if (!InsideEllipse(p.x, p.y, a, b) && off.sqrMagnitude > 1e-8f)
            {
                outward = off.normalized;
            }
            else
            {
                // Inside (or exactly on) the rim: the ellipse normal at c.
                outward = new Vector2(c.x / (a * a), c.y / (b * b));
                if (outward.sqrMagnitude < 1e-12f) outward = new Vector2(0f, 1f);
                outward.Normalize();
            }

            return ToWorld(c + outward * Mathf.Max(0f, standoff), centre, yawDeg, centre.y);
        }
    }
}
