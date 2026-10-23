using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules of the floor-arrow prototype (<see cref="FloorRivalArrows"/>, dev switch <c>-cw_floorarrows</c>): Maestro
    /// 2026-10-22, "for the arrows, the best and more immersive way is to put them in the outer circle at the floor." A ring on
    /// the ground round the LOCAL chicken, with one arrow per hidden rival PAINTED on it (pass 3, Maestro: "like they were
    /// painted on the floor"): a flat chevron decal whose tail sits on the ring line at the bearing to that rival, tip outward,
    /// foreshortened by the camera exactly like the ring. The edge chevrons (<see cref="RivalIndicator"/>) stay the default.
    /// </summary>
    /// <remarks>
    /// Everything that can go subtly wrong lives here so EditMode tests pin it without a Camera or a scene: the ring radius per
    /// framing, bearing to ring point, decal size / placement / yaw / shape, the arc-length de-overlap, and the "does this rival
    /// get a floor arrow" decision. Bearings are degrees clockwise from world +Z seen from above (Unity's yaw convention), in [0, 360).
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
        /// top HUD band (290 panel px since decision 1a; 330 before) leaves 5.19 m above the chicken at 16:9 and 4.48 m at 20:9, so a 4.5 m ring would lay the
        /// arrows of rivals "up" the screen under the band, the one place a hidden rival's arrow must not be. The same 0.8 share
        /// of every clear extent keeps the whole ring in the clear viewport: 4.15 m at 16:9, 3.58 m at 20:9 (3.48 / 2.84 with the old 330 px band)
        /// (<c>FloorRivalArrowsTests.RingRadius_PerAspectTable</c>).</para>
        /// <para>Iteration 2 (qa): the corner CONTROL zones (joystick, hex buttons, desktop strip) are a fourth extent,
        /// <paramref name="controlDistance"/> = ground distance from the chicken to the nearest one, under the same 0.8 share, so
        /// the ring never runs under a thumb. On phones it does not bind (>= 6.7 m at 16:9, >= 10 m in portrait); on a 4:3
        /// touch tablet the hex cluster is 4.63 m away and it does (3.70 m instead of 4.5). The status strip is NOT in it:
        /// its rows come and go, and the radius must not jump with them.</para>
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

        /// <summary>Ground depth covered by one full viewport height of a pitched orthographic camera: 2s / sin(pitch).</summary>
        public static float GroundMetresPerViewportHeight(float orthoSize, float pitchDegrees) =>
            2f * orthoSize / Mathf.Sin(Mathf.Clamp(pitchDegrees, 1f, 89f) * Mathf.Deg2Rad);

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

        // ── Painted arrow decal: size, placement, orientation, shape ───────────────────────────

        /// <summary>0 at the ring, 1 at <see cref="FeedbackTuning.FloorArrowFarDistance"/> and beyond.</summary>
        public static float FarFraction(float planarDistance, float ringRadius)
        {
            float span = Mathf.Max(0.01f, FeedbackTuning.FloorArrowFarDistance - ringRadius);
            return Mathf.Clamp01((planarDistance - ringRadius) / span);
        }

        /// <summary>Arrow length along the bearing, metres of floor: <see cref="FeedbackTuning.FloorArrowNearLength"/> at the
        /// ring down to <see cref="FeedbackTuning.FloorArrowFarLength"/> far away. Monotonic non-increasing, clamped.</summary>
        public static float ArrowLength(float planarDistance, float ringRadius) =>
            Mathf.Lerp(FeedbackTuning.FloorArrowNearLength, FeedbackTuning.FloorArrowFarLength, FarFraction(planarDistance, ringRadius));

        /// <summary>Arrow width across the bearing at its tail (the widest part, lying on the ring).</summary>
        public static float ArrowWidth(float planarDistance, float ringRadius) =>
            ArrowLength(planarDistance, ringRadius) * FeedbackTuning.FloorArrowWidthRatio;

        /// <summary>Where the decal's origin goes: its tail centre, ON the ring line at the bearing.</summary>
        public static Vector3 ArrowTail(Vector3 centre, float ringRadius, float bearingDeg) => RingPoint(centre, ringRadius, bearingDeg);

        /// <summary>The decal's tip: <paramref name="length"/> further out along the bearing.</summary>
        public static Vector3 ArrowTip(Vector3 centre, float ringRadius, float bearingDeg, float length) =>
            RingPoint(centre, ringRadius + length, bearingDeg);

        /// <summary>The decal's only rotation: a yaw about world up equal to the bearing, so its local +Z (the tip) points along
        /// <see cref="Direction"/>. Never a billboard, never pitched toward the camera.</summary>
        public static float ArrowYaw(float bearingDeg) => Normalise(bearingDeg);

        /// <summary>Pure form of <c>Quaternion.Euler(0, yaw, 0) * v</c> (rotation about world up), for tests.</summary>
        public static Vector3 RotateYaw(Vector3 v, float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
            return new Vector3(v.x * c + v.z * sn, v.y, -v.x * sn + v.z * c);
        }

        /// <summary>Points of the chevron decal outline.</summary>
        public const int ChevronPointCount = 4;

        /// <summary>
        /// The chevron outline in decal space (x across, y = local Z along the bearing), length 1, counter-clockwise seen from
        /// above: tail-right, tip, tail-left, notch. The tail corners sit on the ring line (y = 0); the notch cuts the tail.
        /// </summary>
        public static void ChevronPolygon(Vector2[] into)
        {
            float hw = 0.5f * FeedbackTuning.FloorArrowWidthRatio;
            into[0] = new Vector2(hw, 0f);
            into[1] = new Vector2(0f, 1f);
            into[2] = new Vector2(-hw, 0f);
            into[3] = new Vector2(0f, FeedbackTuning.FloorArrowNotch);
        }

        /// <summary>Offsets a counter-clockwise polygon by <paramref name="distance"/> (positive = outward) with mitred corners:
        /// each edge moves along its outward normal and neighbouring edges are re-intersected. Used for the dark rim
        /// (outward) and the soft fade's inner edge (inward).</summary>
        public static void OffsetPolygon(Vector2[] src, int n, float distance, Vector2[] into)
        {
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = src[(i + n - 1) % n], p = src[i], next = src[(i + 1) % n];
                Vector2 e0 = (p - prev).normalized, e1 = (next - p).normalized;
                Vector2 n0 = new Vector2(e0.y, -e0.x), n1 = new Vector2(e1.y, -e1.x);
                Vector2 a = p + n0 * distance, b = p + n1 * distance;
                float cross = e0.x * e1.y - e0.y * e1.x;
                if (Mathf.Abs(cross) < 1e-6f) { into[i] = a; continue; }
                Vector2 d = b - a;
                float t = (d.x * e1.y - d.y * e1.x) / cross;
                into[i] = a + e0 * t;
            }
        }

        /// <summary>Shoelace area: positive for counter-clockwise.</summary>
        public static float SignedArea(Vector2[] p, int n)
        {
            float a = 0f;
            for (int i = 0; i < n; i++) { Vector2 u = p[i], v = p[(i + 1) % n]; a += u.x * v.y - v.x * u.y; }
            return 0.5f * a;
        }

        /// <summary>The thin rim: the tint's own hue, darkened to <see cref="FeedbackTuning.FloorArrowOutlineValue"/> of its value
        /// (near-black for dark seats), at <see cref="FeedbackTuning.FloorArrowOutlineAlpha"/>.</summary>
        public static Color OutlineFor(Color tint)
        {
            Color.RGBToHSV(tint, out float h, out float sat, out float v);
            Color c = Color.HSVToRGB(h, sat, v * FeedbackTuning.FloorArrowOutlineValue);
            c.a = FeedbackTuning.FloorArrowOutlineAlpha;
            return c;
        }

        // ── Angular de-overlap ─────────────────────────────────────────────────────────────────

        /// <summary>Minimum angular separation, degrees, between two arrows on a ring of <paramref name="ringRadius"/>, from
        /// the ring ARC: the widest tail (<see cref="FeedbackTuning.FloorArrowNearLength"/> x
        /// <see cref="FeedbackTuning.FloorArrowWidthRatio"/> = 1.45 m) times <see cref="FeedbackTuning.FloorArrowSpacing"/> =
        /// 1.66 m of arc: ~21 degrees at 4.48 m, ~27 at 3.58 m. Painted tails on the ring line then never touch.</summary>
        public static float MinSeparationDegrees(float ringRadius) =>
            MinSeparationArc() / Mathf.Max(0.5f, ringRadius) * Mathf.Rad2Deg;

        /// <summary>The arc (metres along the ring) two neighbouring arrow centres keep.</summary>
        public static float MinSeparationArc() =>
            FeedbackTuning.FloorArrowNearLength * FeedbackTuning.FloorArrowWidthRatio * FeedbackTuning.FloorArrowSpacing;

        /// <summary>Arc length along a ring of <paramref name="ringRadius"/> for <paramref name="degrees"/>.</summary>
        public static float ArcLength(float degrees, float ringRadius) => degrees * Mathf.Deg2Rad * ringRadius;

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
