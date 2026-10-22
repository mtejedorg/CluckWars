using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules of the floor-arrow prototype (<see cref="FloorRivalArrows"/>, dev switch <c>-cw_floorarrows</c>): Maestro
    /// 2026-10-22, "for the arrows, the best and more immersive way is to put them in the outer circle at the floor." A ring on
    /// the ground round the LOCAL chicken, with one arrow per hidden rival lying on it at the bearing to that rival, pointing
    /// outward. The edge chevrons (<see cref="RivalIndicator"/>) stay the default; this is compared side by side.
    /// </summary>
    /// <remarks>
    /// Everything that can go subtly wrong lives here so EditMode tests pin it without a Camera or a scene: the ring radius per
    /// framing, bearing to ring point, distance to size / alpha, the angular de-overlap, and the "does this rival get a floor
    /// arrow" decision. Bearings are degrees clockwise from world +Z seen from above (Unity's yaw convention), in [0, 360).
    /// </remarks>
    public static class FloorArrowRules
    {
        // ── Ring radius ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Ring radius in metres: R = min(<see cref="FeedbackTuning.FloorArrowPreferredRadius"/>,
        /// <see cref="FeedbackTuning.FloorArrowLateralFraction"/> x the smallest clear ground extent from the chicken), never
        /// below <see cref="FeedbackTuning.FloorArrowMinRadius"/>. The extents are the lateral half-width (chicken to the left /
        /// right screen edge) and the clear depth up (to the top HUD band's lower edge) and down (to the bottom of the clear
        /// band), all ground metres. A non-positive or NaN lateral extent (no camera yet) gives the preferred radius.
        /// </summary>
        /// <remarks>
        /// <para>The brief's rule was lateral only: min(4.5, 0.8 x lateral). That holds in portrait (lateral 5.6 binds: 4.48 m),
        /// but in landscape the DEPTH binds: at 30 degrees pitch one viewport height is 2s / sin(pitch) = 22.4 m of ground, and the
        /// top HUD band (330 panel px) leaves 4.36 m above the chicken at 16:9 and 3.55 m at 20:9, so a 4.5 m ring would lay the
        /// arrows of rivals "up" the screen under the band, the one place a hidden rival's arrow must not be. The same 0.8 share
        /// of every clear extent keeps the whole ring in the clear viewport: 3.48 m at 16:9, 2.84 m at 20:9
        /// (<c>FloorRivalArrowsTests.RingRadius_PerAspectTable</c>).</para>
        /// <para>Iteration 2 (qa): the corner CONTROL zones (joystick, hex buttons, desktop strip) are a fourth extent,
        /// <paramref name="controlDistance"/> = ground distance from the chicken to the nearest one, under the same 0.8 share, so
        /// the ring never runs under a thumb. On phones it does not bind (>= 6.7 m at 16:9, >= 10 m in portrait); on a 4:3
        /// touch tablet the hex cluster is 4.63 m away and it does (3.70 m instead of 4.22). The status strip is NOT in it:
        /// its rows come and go, and the radius must not jump with them (arrows avoid it one by one, <see cref="FitAlongRay"/>).</para>
        /// </remarks>
        public static float RingRadius(float lateralHalfExtent, float clearDepthUp, float clearDepthDown,
                                       float controlDistance = float.PositiveInfinity)
        {
            if (!(lateralHalfExtent > 0f)) return FeedbackTuning.FloorArrowPreferredRadius;
            float up = clearDepthUp >= 0f ? clearDepthUp : 0f;       // NaN / negative: no room
            float down = clearDepthDown >= 0f ? clearDepthDown : 0f;
            float controls = controlDistance >= 0f ? controlDistance : 0f;
            float extent = Mathf.Min(Mathf.Min(lateralHalfExtent, controls), Mathf.Min(up, down));
            float r = Mathf.Min(FeedbackTuning.FloorArrowPreferredRadius, FeedbackTuning.FloorArrowLateralFraction * extent);
            return Mathf.Max(FeedbackTuning.FloorArrowMinRadius, r);
        }

        /// <summary>
        /// The ring radius for the match camera's framing: orthographic size <paramref name="orthoSize"/> at
        /// <paramref name="aspect"/> and <paramref name="pitchDegrees"/>, the chicken drawn at the framing's own focus height
        /// (<see cref="OrientationFraming.PortraitFocusViewportYAt"/> in portrait, 0.5 in landscape: the nominal value, so the
        /// radius does not wobble while the follow smooths), the clear band left by <paramref name="infoZones"/>
        /// (<see cref="ScreenEdgeProjection.ClearBand"/>, the same cut the edge chevrons use) and the HUD
        /// <paramref name="controlZones"/> (<see cref="HudControlZones.Current"/>), all viewport rects.
        /// </summary>
        public static float FramingRadius(float aspect, float orthoSize, float pitchDegrees,
                                          System.Collections.Generic.IReadOnlyList<Rect> infoZones,
                                          System.Collections.Generic.IReadOnlyList<Rect> controlZones = null)
        {
            if (!(aspect > 0f) || !(orthoSize > 0f)) return FeedbackTuning.FloorArrowPreferredRadius;
            float fy = aspect < 1f ? OrientationFraming.PortraitFocusViewportYAt(aspect) : 0.5f;
            Rect clear = ScreenEdgeProjection.ClearBand(new Rect(0f, 0f, 1f, 1f), infoZones, Vector2.zero);
            float metresPerViewport = GroundMetresPerViewportHeight(orthoSize, pitchDegrees);
            float lateral = orthoSize * aspect;

            float controls = float.PositiveInfinity;
            if (controlZones != null)
            {
                for (int i = 0; i < controlZones.Count; i++)
                {
                    Rect z = controlZones[i];
                    // The zone in ground metres round the chicken: x lateral (2L per viewport width), y depth.
                    var g = Rect.MinMaxRect((z.xMin - 0.5f) * 2f * lateral, (z.yMin - fy) * metresPerViewport,
                                            (z.xMax - 0.5f) * 2f * lateral, (z.yMax - fy) * metresPerViewport);
                    float dx = Mathf.Max(0f, Mathf.Max(g.xMin, -g.xMax));
                    float dy = Mathf.Max(0f, Mathf.Max(g.yMin, -g.yMax));
                    controls = Mathf.Min(controls, Mathf.Sqrt(dx * dx + dy * dy));
                }
            }
            return RingRadius(lateral, (clear.yMax - fy) * metresPerViewport, (fy - clear.yMin) * metresPerViewport, controls);
        }

        // ── Screen space ("screen metres") ─────────────────────────────────────────────────────
        // The arrows are sized and fitted on SCREEN, in screen metres: x = lateral ground metres, y = ground depth x sin(pitch),
        // origin at the local chicken's feet, so one unit is the same number of pixels both ways (H / 2s). A ground ring of
        // radius R is the ellipse (R, R sin(pitch)) here, and an arrow drawn undistorted here is what the player sees.

        /// <summary>Unit screen direction of a ground bearing <paramref name="relativeBearingDeg"/> measured from the camera's
        /// ground-forward (bearing minus camera yaw): (sin b, cos b x sin(pitch)), normalised. The arrow is drawn along this, so it
        /// points where the rival really is on screen, not along the unprojected ground angle.</summary>
        public static Vector2 ScreenDirection(float relativeBearingDeg, float pitchDegrees)
        {
            float b = relativeBearingDeg * Mathf.Deg2Rad;
            var d = new Vector2(Mathf.Sin(b), Mathf.Cos(b) * SinPitch(pitchDegrees));
            return d.sqrMagnitude < 1e-12f ? Vector2.up : d.normalized;
        }

        /// <summary>The ring point at <paramref name="relativeBearingDeg"/>, screen metres from the chicken's feet.</summary>
        public static Vector2 ScreenRingPoint(float relativeBearingDeg, float radius, float pitchDegrees)
        {
            float b = relativeBearingDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(b) * radius, Mathf.Cos(b) * radius * SinPitch(pitchDegrees));
        }

        /// <summary>Screen metres back to ground metres: (lateral, depth along the camera's ground-forward).</summary>
        public static Vector2 ScreenToGround(Vector2 screen, float pitchDegrees) =>
            new Vector2(screen.x, screen.y / SinPitch(pitchDegrees));

        /// <summary>The ground stretch along the camera's ground-forward that makes a flat arrow read undistorted: 1 / sin(pitch)
        /// (2 at 30 degrees). Without it an arrow pointing up or down the screen looks half as long as a sideways one.</summary>
        public static float DepthStretch(float pitchDegrees) => 1f / SinPitch(pitchDegrees);

        /// <summary>A viewport rect in screen metres round a chicken drawn at viewport <paramref name="focus"/>.</summary>
        public static Rect ViewportToScreenMetres(Rect viewport, Vector2 focus, float orthoSize, float aspect)
        {
            float k = 2f * orthoSize;   // screen metres per viewport height
            return Rect.MinMaxRect((viewport.xMin - focus.x) * k * aspect, (viewport.yMin - focus.y) * k,
                                   (viewport.xMax - focus.x) * k * aspect, (viewport.yMax - focus.y) * k);
        }

        /// <summary>
        /// Fits an arrow on the screen ray p(t) = <paramref name="centre"/> + t <paramref name="dir"/> (screen metres, unit dir):
        /// the largest length up to <paramref name="desiredSize"/> whose whole extent stays inside <paramref name="frame"/> and
        /// out of every obstacle (HUD zones, the chicken's own body), centred as close to the ring point (t = 0) as the free
        /// stretch allows. Returns the length; <paramref name="offset"/> is the centre's t. Never below
        /// <see cref="FeedbackTuning.FloorArrowMinSize"/>: with no room the arrow overlaps rather than disappears, and a ring
        /// point inside an obstacle keeps it at t = 0 at the minimum size.
        /// </summary>
        public static float FitAlongRay(Vector2 centre, Vector2 dir, float desiredSize, Rect frame,
                                        Rect[] obstacles, int obstacleCount, out float offset)
        {
            offset = 0f;
            float min = Mathf.Min(FeedbackTuning.FloorArrowMinSize, desiredSize);
            if (!Slab(centre, dir, frame, out float tMin, out float tMax)) return min;

            for (int i = 0; i < obstacleCount; i++)
            {
                if (!Slab(centre, dir, obstacles[i], out float enter, out float exit)) continue;
                if (enter > 0f) tMax = Mathf.Min(tMax, enter);
                else if (exit < 0f) tMin = Mathf.Max(tMin, exit);
                else return min;   // the ring point itself is covered
            }

            float room = tMax - tMin;
            if (room <= min) { offset = 0.5f * (tMin + tMax); return min; }
            float size = Mathf.Min(desiredSize, room);
            float h = 0.5f * size;
            offset = Mathf.Clamp(0f, tMin + h, tMax - h);
            return size;
        }

        /// <summary>Parameter interval [enter, exit] of the line p(t) = c + t d inside rect <paramref name="r"/>; false if it misses.</summary>
        private static bool Slab(Vector2 c, Vector2 d, Rect r, out float enter, out float exit)
        {
            enter = float.NegativeInfinity;
            exit = float.PositiveInfinity;
            if (!Axis(c.x, d.x, r.xMin, r.xMax, ref enter, ref exit)) return false;
            if (!Axis(c.y, d.y, r.yMin, r.yMax, ref enter, ref exit)) return false;
            return enter <= exit;
        }

        private static bool Axis(float c, float d, float min, float max, ref float enter, ref float exit)
        {
            if (Mathf.Abs(d) < 1e-9f) return c >= min && c <= max;
            float t1 = (min - c) / d, t2 = (max - c) / d;
            enter = Mathf.Max(enter, Mathf.Min(t1, t2));
            exit = Mathf.Min(exit, Mathf.Max(t1, t2));
            return true;
        }

        private static float SinPitch(float pitchDegrees) => Mathf.Sin(Mathf.Clamp(pitchDegrees, 1f, 89f) * Mathf.Deg2Rad);

        /// <summary>Ground depth covered by one full viewport height of a pitched orthographic camera: 2s / sin(pitch).</summary>
        public static float GroundMetresPerViewportHeight(float orthoSize, float pitchDegrees) =>
            2f * orthoSize / SinPitch(pitchDegrees);

        // ── Bearing and ring point ─────────────────────────────────────────────────────────────

        /// <summary>Planar (XZ) bearing from <paramref name="from"/> to <paramref name="to"/>, degrees in [0, 360); 0 when the two coincide.</summary>
        public static float Bearing(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            if (dx * dx + dz * dz < 1e-10f) return 0f;
            return Normalise(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg);
        }

        /// <summary>Unit planar direction for a bearing (0 = +Z, 90 = +X).</summary>
        public static Vector3 Direction(float bearingDeg)
        {
            float r = bearingDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        /// <summary>The point on a ring of <paramref name="radius"/> round <paramref name="centre"/> at <paramref name="bearingDeg"/>,
        /// at the centre's height (the caller lifts it to the draw height).</summary>
        public static Vector3 RingPoint(Vector3 centre, float radius, float bearingDeg) =>
            centre + Direction(bearingDeg) * radius;

        /// <summary>Planar (XZ) distance, the same measure abilities and zones use.</summary>
        public static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ── Visibility ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether a rival gets a floor arrow. <paramref name="hiddenFromClearViewport"/> is exactly the edge chevron's own
        /// decision (<see cref="RivalIndicator"/>: off the frame, behind the camera, or mostly under a HUD information zone,
        /// through <see cref="ScreenEdgeProjection.ClampToClearViewport"/>), so an on-screen rival never gets one and a rival
        /// under the top HUD band does. It must also stand beyond the ring: an arrow on the ring pointing at a bird inside it
        /// would point the wrong way past it.
        /// </summary>
        public static bool WantsArrow(bool hiddenFromClearViewport, float planarDistance, float ringRadius) =>
            hiddenFromClearViewport && planarDistance > ringRadius;

        // ── Distance to size / alpha ───────────────────────────────────────────────────────────

        /// <summary>0 at the ring, 1 at <see cref="FeedbackTuning.FloorArrowFarDistance"/> and beyond.</summary>
        public static float FarFraction(float planarDistance, float ringRadius)
        {
            float span = Mathf.Max(0.01f, FeedbackTuning.FloorArrowFarDistance - ringRadius);
            return Mathf.Clamp01((planarDistance - ringRadius) / span);
        }

        /// <summary>Arrow length in metres: <see cref="FeedbackTuning.FloorArrowNearSize"/> at the ring down to
        /// <see cref="FeedbackTuning.FloorArrowFarSize"/> far away. Monotonic non-increasing in distance, clamped.</summary>
        public static float ArrowSize(float planarDistance, float ringRadius) =>
            Mathf.Lerp(FeedbackTuning.FloorArrowNearSize, FeedbackTuning.FloorArrowFarSize, FarFraction(planarDistance, ringRadius));

        /// <summary>Arrow alpha before the rival's own opacity: near = strong, far = faint. Monotonic non-increasing, clamped.</summary>
        public static float ArrowAlpha(float planarDistance, float ringRadius) =>
            Mathf.Lerp(FeedbackTuning.FloorArrowNearAlpha, FeedbackTuning.FloorArrowFarAlpha, FarFraction(planarDistance, ringRadius));

        // ── Angular de-overlap ─────────────────────────────────────────────────────────────────

        /// <summary>Minimum angular separation, degrees, between two arrow centres on a ring of <paramref name="ringRadius"/>:
        /// the largest arrow's width (<see cref="FeedbackTuning.FloorArrowWidthRatio"/> of its length) times
        /// <see cref="FeedbackTuning.FloorArrowSpacing"/>, as an arc: ~26 degrees at 4.48 m, ~41 at 2.84 m.</summary>
        public static float MinSeparationDegrees(float ringRadius) =>
            FeedbackTuning.FloorArrowNearSize * FeedbackTuning.FloorArrowWidthRatio * FeedbackTuning.FloorArrowSpacing
            / Mathf.Max(0.5f, ringRadius) * Mathf.Rad2Deg;

        /// <summary>Largest count <see cref="Spread"/> accepts (the 4-seat ceiling's three rivals).</summary>
        public const int MaxArrows = 3;

        /// <summary>
        /// Spreads <paramref name="count"/> bearings (degrees) so no two are closer than <paramref name="minSepDeg"/>, into
        /// <paramref name="result"/> (same order). Deterministic and order-independent: inputs are sorted by (bearing, key), so
        /// the bearing each input ends with depends only on the set of (bearing, key) pairs, never on the order they were
        /// submitted in (which is LateUpdate order). An arrow that overlaps nobody keeps its bearing bit-for-bit. Overlapping
        /// arrows form a cluster laid out <paramref name="minSepDeg"/> apart round the mean of their own bearings, so a crowd
        /// in one direction fans out symmetrically about that direction. No allocation (fixed scratch, <see cref="MaxArrows"/>).
        /// </summary>
        /// <remarks>The circle is cut at its largest empty gap and the 1-D cluster merge runs on the unwrapped line. With
        /// n &lt;= 3 that gap is at least 120 degrees, and a full cluster spans 2 x minSep, so the result never wraps into the cut
        /// as long as minSep stays under 60 degrees (it is ~15 at the shipped radius; clamped here to keep the guarantee).</remarks>
        public static void Spread(float[] bearings, int[] keys, int count, float minSepDeg, float[] result)
        {
            int n = Mathf.Min(count, MaxArrows);
            for (int i = 0; i < n; i++) result[i] = Normalise(bearings[i]);
            if (n < 2) return;
            float sep = Mathf.Clamp(minSepDeg, 0f, 59f);
            if (sep <= 0f) return;

            // Sort indices by (bearing, key): insertion sort into scratch.
            var order = _order;
            for (int i = 0; i < n; i++)
            {
                int j = i;
                while (j > 0 && Before(result, keys, i, order[j - 1])) { order[j] = order[j - 1]; j--; }
                order[j] = i;
            }

            // Cut at the largest gap: the element after it starts the line. Ties: the first such gap in sorted order.
            int start = 0;
            float bestGap = -1f;
            for (int k = 0; k < n; k++)
            {
                float a = result[order[k]], b = result[order[(k + 1) % n]];
                float gap = k + 1 < n ? b - a : b + 360f - a;
                if (gap > bestGap) { bestGap = gap; start = (k + 1) % n; }
            }

            // Unwrapped line positions.
            var line = _line;
            float origin = result[order[start]];
            for (int k = 0; k < n; k++)
            {
                float v = result[order[(start + k) % n]];
                line[k] = v < origin ? v + 360f : v;
            }

            // 1-D cluster merge: a stack of clusters (first index, count, sum of original positions).
            var cFirst = _cFirst; var cCount = _cCount; var cSum = _cSum;
            int clusters = 0;
            for (int k = 0; k < n; k++)
            {
                cFirst[clusters] = k; cCount[clusters] = 1; cSum[clusters] = line[k]; clusters++;
                while (clusters >= 2)
                {
                    int p = clusters - 2, c = clusters - 1;
                    float prevEnd = cSum[p] / cCount[p] + 0.5f * (cCount[p] - 1) * sep;
                    float curStart = cSum[c] / cCount[c] - 0.5f * (cCount[c] - 1) * sep;
                    if (curStart - prevEnd >= sep) break;
                    cCount[p] += cCount[c]; cSum[p] += cSum[c]; clusters--;
                }
            }

            for (int c = 0; c < clusters; c++)
            {
                if (cCount[c] == 1) continue;   // a lone arrow keeps its input exactly
                float centre = cSum[c] / cCount[c];
                for (int m = 0; m < cCount[c]; m++)
                {
                    int k = cFirst[c] + m;
                    result[order[(start + k) % n]] = Normalise(centre + (m - 0.5f * (cCount[c] - 1)) * sep);
                }
            }
        }

        /// <summary>Degrees into [0, 360).</summary>
        public static float Normalise(float deg)
        {
            float d = deg % 360f;
            if (d < 0f) d += 360f;
            return d >= 360f ? 0f : d;
        }

        private static bool Before(float[] bearing, int[] keys, int a, int b)
        {
            if (bearing[a] != bearing[b]) return bearing[a] < bearing[b];
            int ka = keys != null ? keys[a] : a, kb = keys != null ? keys[b] : b;
            return ka < kb;
        }

        // Static scratch (main thread only): Spread runs every frame while rivals are hidden, so it must not allocate.
        private static readonly int[] _order = new int[MaxArrows];
        private static readonly float[] _line = new float[MaxArrows];
        private static readonly int[] _cFirst = new int[MaxArrows];
        private static readonly int[] _cCount = new int[MaxArrows];
        private static readonly float[] _cSum = new float[MaxArrows];
    }
}
