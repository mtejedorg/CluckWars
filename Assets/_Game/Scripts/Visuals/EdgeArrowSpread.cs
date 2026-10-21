using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// De-overlaps off-screen rival arrows (CW-022 / CW-008, Maestro: "spread stacked edge arrows = yes").
    /// Each arrow is first placed independently (<see cref="ScreenEdgeProjection"/>: clamp to the edge band, slide out of
    /// the HUD control zones). Two rivals pushed to the same edge spot by the same zone then land on one point and read as a
    /// single arrow. This pass walks the band's perimeter to give every arrow its own slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pure and deterministic (EditMode-testable, no Camera): the result depends only on the arguments, never on frame order.
    /// Arrows are processed in perimeter order (ties by input index); an arrow that does not collide with an already-placed
    /// one keeps its position bit-for-bit, so a lone arrow is never moved. A colliding arrow searches outward along the band
    /// perimeter, alternating direction (lower perimeter parameter first), for the nearest spot that is clear of every placed
    /// arrow AND outside every HUD zone, so spreading never re-introduces the overlap the zones exist to prevent.
    /// </para>
    /// <para>The status strip is deliberately not a zone here (CW-009: not included until a playtest shows overlap).</para>
    /// </remarks>
    public static class EdgeArrowSpread
    {
        /// <summary>Perimeter search resolution, viewport units.</summary>
        private const float StepLength = 0.005f;

        // Static scratch (main thread only): Spread runs every frame while rivals are off screen, so it must not allocate.
        private static int[] _order = new int[4];
        private static float[] _param = new float[4];
        private static Vector2[] _placed = new Vector2[4];

        /// <summary>
        /// Spreads <paramref name="desired"/> (viewport points, one per arrow) into <paramref name="result"/> (same length,
        /// same order). <paramref name="minSeparation"/> is the centre-to-centre distance below which two arrows overlap, per
        /// axis in viewport units (an ellipse: x is the chevron's size divided by aspect, y its size times a spacing factor).
        /// </summary>
        public static void Spread(IReadOnlyList<Vector2> desired, Rect band, IReadOnlyList<Rect> zones,
                                  Vector2 clearance, Vector2 minSeparation, Vector2[] result)
        {
            int n = desired.Count;
            for (int i = 0; i < n; i++) result[i] = desired[i];
            if (n < 2) return;

            band = NormaliseBand(band);
            float perimeter = 2f * (band.width + band.height);
            if (perimeter <= 0f) return;

            // Perimeter order, ties by index: insertion sort on a tiny array (<= 3 rivals), in static scratch buffers.
            if (_order.Length < n) { _order = new int[n]; _param = new float[n]; _placed = new Vector2[n]; }
            var order = _order;
            var param = _param;
            for (int i = 0; i < n; i++)
            {
                param[i] = ParamOf(band, desired[i]);
                int j = i;
                while (j > 0 && param[order[j - 1]] > param[i]) { order[j] = order[j - 1]; j--; }
                order[j] = i;
            }

            var placed = _placed;
            int placedCount = 0;
            for (int k = 0; k < n; k++)
            {
                int i = order[k];
                Vector2 p = desired[i];
                if (Collides(p, placed, placedCount, minSeparation))
                    p = FindSlot(band, param[i], perimeter, placed, placedCount, zones, clearance, minSeparation, p);
                result[i] = p;
                placed[placedCount++] = p;
            }
        }

        /// <summary>True when any two of <paramref name="points"/> are closer than <paramref name="minSeparation"/>.</summary>
        public static bool AnyOverlap(IReadOnlyList<Vector2> points, Vector2 minSeparation)
        {
            for (int i = 0; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
                if (Overlaps(points[i], points[j], minSeparation)) return true;
            return false;
        }

        private static Vector2 FindSlot(Rect band, float startParam, float perimeter, Vector2[] placed, int placedCount,
                                        IReadOnlyList<Rect> zones, Vector2 clearance, Vector2 minSep, Vector2 fallback)
        {
            int maxSteps = Mathf.CeilToInt(perimeter * 0.5f / StepLength);
            for (int s = 1; s <= maxSteps; s++)
            {
                float d = s * StepLength;
                for (int dir = 0; dir < 2; dir++)
                {
                    Vector2 c = PointAt(band, startParam + (dir == 0 ? -d : d), perimeter);
                    if (Collides(c, placed, placedCount, minSep)) continue;
                    if (zones != null && ScreenEdgeProjection.IsInsideAnyZone(c, zones, clearance)) continue;
                    return c;
                }
            }
            return fallback;   // no free perimeter slot (cannot happen with <= 3 rivals and the shipped layout): keep the overlap rather than hide an arrow
        }

        private static bool Collides(Vector2 p, Vector2[] placed, int count, Vector2 minSep)
        {
            for (int i = 0; i < count; i++)
                if (Overlaps(p, placed[i], minSep)) return true;
            return false;
        }

        private static bool Overlaps(Vector2 a, Vector2 b, Vector2 minSep)
        {
            float dx = (a.x - b.x) / Mathf.Max(1e-6f, minSep.x);
            float dy = (a.y - b.y) / Mathf.Max(1e-6f, minSep.y);
            return dx * dx + dy * dy < 1f;
        }

        // ── Band perimeter, parameterised clockwise from the bottom-left corner ───────────────────────

        /// <summary>Perimeter parameter of the band point nearest <paramref name="p"/>.</summary>
        private static float ParamOf(Rect b, Vector2 p)
        {
            float x = Mathf.Clamp(p.x, b.xMin, b.xMax), y = Mathf.Clamp(p.y, b.yMin, b.yMax);
            float dl = x - b.xMin, dr = b.xMax - x, db = y - b.yMin, dt = b.yMax - y;
            float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
            float w = b.width, h = b.height;
            if (m == dl) return y - b.yMin;                              // left edge, going up
            if (m == dt) return h + (x - b.xMin);                        // top edge, going right
            if (m == dr) return h + w + (b.yMax - y);                    // right edge, going down
            return h + w + h + (b.xMax - x);                             // bottom edge, going left
        }

        private static Vector2 PointAt(Rect b, float s, float perimeter)
        {
            s = ((s % perimeter) + perimeter) % perimeter;
            float w = b.width, h = b.height;
            if (s < h) return new Vector2(b.xMin, b.yMin + s);
            s -= h;
            if (s < w) return new Vector2(b.xMin + s, b.yMax);
            s -= w;
            if (s < h) return new Vector2(b.xMax, b.yMax - s);
            s -= h;
            return new Vector2(b.xMax - s, b.yMin);
        }

        private static Rect NormaliseBand(Rect r)
        {
            float xMin = Mathf.Clamp01(r.xMin), xMax = Mathf.Clamp01(r.xMax);
            float yMin = Mathf.Clamp01(r.yMin), yMax = Mathf.Clamp01(r.yMax);
            if (xMin > xMax) xMin = xMax = 0.5f * (xMin + xMax);
            if (yMin > yMax) yMin = yMax = 0.5f * (yMin + yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
