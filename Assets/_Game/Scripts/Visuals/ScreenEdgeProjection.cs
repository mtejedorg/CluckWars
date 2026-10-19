using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The project's <b>one and only</b> "where does this world point sit relative to the
    /// edge of the screen" implementation. Two features need exactly this maths and must
    /// never grow a second copy of it:
    ///
    /// <list type="bullet">
    ///   <item><see cref="RivalIndicator"/> — the off-screen rival chevron pinned to the
    ///   view edge, pointing at the rival (playtest note 2 of 5, 2026-08-14).</item>
    ///   <item>FEEDBACK.md §3.2 case 13's screen-edge damage-direction arc, whose publisher
    ///   (<see cref="HitFeedback.OnLocalPlayerHit"/>) already ships but whose HUD-side
    ///   subscriber does not exist yet. When it is written it must call this, not
    ///   re-derive it — see the remarks on that event.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Split into a pure part (<see cref="ClampToEdge"/> — plain vector maths, no Unity
    /// scene state, EditMode-testable) and a thin camera glue part
    /// (<see cref="TryProject"/>, <see cref="ViewportWorldHeight"/>). Same split, and same
    /// reason, as <c>HudFeedbackStyle</c>: the decision that can go subtly wrong is the one
    /// that gets pinned by tests, and it can only be pinned if it does not need a live
    /// <c>Camera</c> to run.
    ///
    /// <b>On-screen and off-screen are decided against the full unit rect, not the inset
    /// one.</b> A rival whose feet are at viewport (0.97, 0.5) is genuinely visible, so it
    /// keeps its ground ring; the chevron only takes over once the rival has actually left
    /// the frame. The margin exists solely so that the chevron — which is a chunky piece of
    /// geometry with its own width — is drawn far enough inside the frame not to be half
    /// clipped by it. Conflating the two would make the ring wink out while its owner was
    /// still plainly on screen.
    /// </remarks>
    public static class ScreenEdgeProjection
    {
        /// <summary>Upper bound on the edge margin. Half the viewport minus a sliver, so a
        /// nonsense margin degenerates to "pinned at the centre" rather than inverting the
        /// clamp range and returning NaN-adjacent garbage.</summary>
        private const float MaxMargin = 0.49f;

        /// <summary>Below this squared length a direction is treated as degenerate rather
        /// than normalised — normalising a near-zero vector yields either zero or a wildly
        /// amplified rounding error, and both would aim the chevron at nothing.</summary>
        private const float DegenerateSqr = 1e-8f;

        private static readonly Vector2 ViewportCentre = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// Pure form. Decides whether <paramref name="viewport"/> is outside the frame and,
        /// if so, where on the inset edge rect an indicator for it belongs and which way
        /// that indicator should point.
        /// </summary>
        /// <param name="viewport">Camera-projected point, viewport units, origin bottom-left.
        /// Values outside [0,1] mean "off screen in that axis" — that is the whole input.</param>
        /// <param name="forceOffScreen">Set by the caller for the cases the x/y pair alone
        /// cannot express — principally a point behind the camera, which can land dead centre
        /// of the viewport rect while being nowhere the player can see. See
        /// <see cref="TryProject"/> for who sets it and why the mirror correction lives there.</param>
        /// <param name="margin">Inset from each edge, viewport units, for the returned point
        /// only. Clamped internally to [0, <see cref="MaxMargin"/>].</param>
        /// <param name="clamped">Point on (or inside) the inset rect. Meaningful only when
        /// this method returns true.</param>
        /// <param name="bearing">Unit direction, in viewport axes, from <paramref name="clamped"/>
        /// toward the real target — i.e. the way an arrow drawn at <paramref name="clamped"/>
        /// should point. Never zero-length; falls back to the centre-outward direction, and
        /// then to +Y, rather than returning a degenerate vector a caller could normalise again.</param>
        /// <returns>True when the point lies outside the unit viewport rect.</returns>
        public static bool ClampToEdge(Vector2 viewport, bool forceOffScreen, float margin,
                                       out Vector2 clamped, out Vector2 bearing)
            => ClampToEdge(viewport, forceOffScreen, UniformSafeArea(margin), out clamped, out bearing);

        /// <summary>
        /// A uniformly inset band, in viewport units — the simple case of the
        /// <see cref="Rect"/> overload below.
        /// </summary>
        public static Rect UniformSafeArea(float margin)
        {
            margin = Mathf.Clamp(margin, 0f, MaxMargin);
            return Rect.MinMaxRect(margin, margin, 1f - margin, 1f - margin);
        }

        /// <summary>
        /// As the <c>float</c> overload, but the band an indicator may be drawn in is given
        /// per-edge rather than uniformly.
        /// </summary>
        /// <remarks>
        /// <b>Why per-edge exists.</b> A uniform margin puts every off-screen indicator on
        /// the same inset rect, and on this game's HUD the top strip is occupied — the
        /// scoreboard sits top-left and the match timer top-right. Measured in a live match
        /// (2026-08-15): with the player in a corner, all three rival chevrons clamped to
        /// y = 0.955 and two of them landed under HUD panels, so the one case the feature
        /// exists for — "where did everyone go" — was also the case it was invisible in.
        ///
        /// The alternative, raising the uniform margin until it cleared the scoreboard,
        /// would have pushed the side and bottom chevrons pointlessly far inboard and made
        /// them read as floating in the play field rather than pinned to the frame.
        /// </remarks>
        public static bool ClampToEdge(Vector2 viewport, bool forceOffScreen, Rect safeArea,
                                       out Vector2 clamped, out Vector2 bearing)
        {
            Rect band = NormaliseBand(safeArea);

            bool offScreen = forceOffScreen
                             || viewport.x < 0f || viewport.x > 1f
                             || viewport.y < 0f || viewport.y > 1f;

            clamped = new Vector2(
                Mathf.Clamp(viewport.x, band.xMin, band.xMax),
                Mathf.Clamp(viewport.y, band.yMin, band.yMax));

            bearing = BearingFrom(clamped, viewport);

            return offScreen;
        }

        /// <summary>A degenerate or inverted band would invert the clamp range and return NaN-adjacent garbage; it is
        /// collapsed to its centre instead, and every edge is clamped into the unit rect.</summary>
        private static Rect NormaliseBand(Rect safeArea)
        {
            float xMin = Mathf.Clamp01(safeArea.xMin);
            float xMax = Mathf.Clamp01(safeArea.xMax);
            float yMin = Mathf.Clamp01(safeArea.yMin);
            float yMax = Mathf.Clamp01(safeArea.yMax);
            if (xMin > xMax) xMin = xMax = 0.5f * (xMin + xMax);
            if (yMin > yMax) yMin = yMax = 0.5f * (yMin + yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>Unit direction from <paramref name="from"/> toward <paramref name="target"/>; centre-outward, then +Y, when
        /// the two coincide. Prefers from -> target: for a rival off the top-right this points into the corner, which is a truer
        /// "that way" than a bearing measured from screen centre. The centre fallback covers the forceOffScreen case, where the
        /// target can sit inside the inset rect and the first difference is therefore zero.</summary>
        private static Vector2 BearingFrom(Vector2 from, Vector2 target)
        {
            Vector2 d = target - from;
            if (d.sqrMagnitude < DegenerateSqr) d = target - ViewportCentre;
            return d.sqrMagnitude < DegenerateSqr ? Vector2.up : d.normalized;
        }

        // ── On-screen test (body bounds, hysteresis) ───────────────────────────────────────────

        /// <summary>
        /// Whether a rival whose body covers <paramref name="bodyViewportBounds"/> (viewport units) counts as ON SCREEN: any
        /// overlap with the frame, not just the feet. A chicken is tall; at the bottom edge its feet are below the frame while
        /// its body is plainly in view, and an arrow drawn then sits on top of the bird it points at.
        /// </summary>
        /// <param name="wasOnScreen">The previous frame's answer. Hysteresis is applied only on the way OUT: a rival that was
        /// on screen stays on screen until it is fully <paramref name="hysteresis"/> beyond the frame, so a rival hugging the
        /// border does not flicker between ring and arrow. Coming IN needs only a hair of real overlap.</param>
        public static bool IsPartlyOnScreen(Rect bodyViewportBounds, bool wasOnScreen, float hysteresis)
        {
            float m = wasOnScreen ? Mathf.Max(0f, hysteresis) : 0f;
            return bodyViewportBounds.xMax > -m && bodyViewportBounds.xMin < 1f + m
                && bodyViewportBounds.yMax > -m && bodyViewportBounds.yMin < 1f + m;
        }

        /// <summary>Viewport-space bounding rect of a world AABB. False when nothing of it is in front of a perspective camera
        /// (an orthographic camera, which <see cref="MatchCamera"/> forces, projects linearly so every corner counts).</summary>
        public static bool TryProjectBounds(Camera cam, Bounds worldBounds, out Rect viewportBounds)
        {
            viewportBounds = default;
            if (cam == null) return false;

            Vector3 c = worldBounds.center, e = worldBounds.extents;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 vp = cam.WorldToViewportPoint(corner);
                if (vp.z < 0f && !cam.orthographic) continue;
                any = true;
                if (vp.x < minX) minX = vp.x;
                if (vp.x > maxX) maxX = vp.x;
                if (vp.y < minY) minY = vp.y;
                if (vp.y > maxY) maxY = vp.y;
            }
            if (!any) return false;
            viewportBounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        // ── HUD control zones ──────────────────────────────────────────────────────────────────

        /// <summary>Tolerance for "on the boundary of a zone counts as outside it" and for rounding in the edge-band test.</summary>
        private const float ZoneEpsilon = 1e-5f;

        /// <summary>
        /// As the <see cref="Rect"/> overload, but the returned point also stays out of every rectangle in
        /// <paramref name="avoidZones"/> (HUD controls: <see cref="HudControlZones"/>), grown by <paramref name="clearance"/>
        /// (half the arrow's extent, viewport units) so the whole arrow clears them, not just its centre.
        /// </summary>
        /// <remarks>
        /// When the edge point is inside a zone the arrow SLIDES ALONG THE EDGE BAND to the nearest allowed point: candidates
        /// are the band's corners, the band edges at each zone boundary, and the true target projected onto each edge; the
        /// closest free one to the target wins. <paramref name="bearing"/> is then re-aimed from the new point at the real
        /// target, so the arrow still points at the off-screen rival. If no edge point is free (cannot happen with the shipped
        /// layout) the plain clamp is kept rather than hiding the arrow.
        /// </remarks>
        public static bool ClampToEdge(Vector2 viewport, bool forceOffScreen, Rect safeArea,
                                       IReadOnlyList<Rect> avoidZones, Vector2 clearance,
                                       out Vector2 clamped, out Vector2 bearing)
        {
            bool off = ClampToEdge(viewport, forceOffScreen, safeArea, out clamped, out bearing);
            if (!off || avoidZones == null || avoidZones.Count == 0) return off;
            if (!IsInsideAnyZone(clamped, avoidZones, clearance)) return off;

            Rect band = NormaliseBand(safeArea);
            Vector2 best = clamped;
            float bestSqr = float.MaxValue;
            bool found = false;

            // Band corners and the target projected onto each edge.
            Consider(new Vector2(band.xMin, band.yMin), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
            Consider(new Vector2(band.xMax, band.yMin), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
            Consider(new Vector2(band.xMin, band.yMax), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
            Consider(new Vector2(band.xMax, band.yMax), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
            float tx = Mathf.Clamp(viewport.x, band.xMin, band.xMax), ty = Mathf.Clamp(viewport.y, band.yMin, band.yMax);
            Consider(new Vector2(tx, band.yMin), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
            Consider(new Vector2(tx, band.yMax), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
            Consider(new Vector2(band.xMin, ty), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
            Consider(new Vector2(band.xMax, ty), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);

            // The band's edges at every zone boundary: where a blocked stretch of edge ends.
            for (int i = 0; i < avoidZones.Count; i++)
            {
                Rect z = avoidZones[i];
                float x0 = z.xMin - clearance.x, x1 = z.xMax + clearance.x;
                float y0 = z.yMin - clearance.y, y1 = z.yMax + clearance.y;
                Consider(new Vector2(x0, band.yMin), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
                Consider(new Vector2(x1, band.yMin), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
                Consider(new Vector2(x0, band.yMax), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
                Consider(new Vector2(x1, band.yMax), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
                Consider(new Vector2(band.xMin, y0), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
                Consider(new Vector2(band.xMin, y1), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
                Consider(new Vector2(band.xMax, y0), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
                Consider(new Vector2(band.xMax, y1), viewport, band, avoidZones, clearance, ref best, ref bestSqr, ref found);
            }

            if (!found) return off;
            clamped = best;
            bearing = BearingFrom(clamped, viewport);
            return off;
        }

        /// <summary>True when <paramref name="p"/> is strictly inside any zone grown by <paramref name="clearance"/>.
        /// Exactly on a grown zone's boundary is outside (the slide ends there).</summary>
        public static bool IsInsideAnyZone(Vector2 p, IReadOnlyList<Rect> zones, Vector2 clearance)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                Rect z = zones[i];
                if (p.x > z.xMin - clearance.x + ZoneEpsilon && p.x < z.xMax + clearance.x - ZoneEpsilon &&
                    p.y > z.yMin - clearance.y + ZoneEpsilon && p.y < z.yMax + clearance.y - ZoneEpsilon)
                    return true;
            }
            return false;
        }

        private static void Consider(Vector2 c, Vector2 target, Rect band, IReadOnlyList<Rect> zones, Vector2 clearance,
                                     ref Vector2 best, ref float bestSqr, ref bool found)
        {
            if (c.x < band.xMin - ZoneEpsilon || c.x > band.xMax + ZoneEpsilon ||
                c.y < band.yMin - ZoneEpsilon || c.y > band.yMax + ZoneEpsilon) return;
            if (IsInsideAnyZone(c, zones, clearance)) return;
            float d = (c - target).sqrMagnitude;
            if (!found || d < bestSqr) { best = c; bestSqr = d; found = true; }
        }

        /// <summary>
        /// Camera glue over <see cref="ClampToEdge"/>. Returns true when
        /// <paramref name="worldPos"/> is off screen, with the viewport point an indicator
        /// should be drawn at and the direction it should point.
        /// </summary>
        /// <remarks>
        /// <b>The mirror correction is projection-dependent, which is why it lives here and
        /// not in the pure function.</b> Under perspective, a point behind the camera
        /// projects to x/y mirrored through the centre, so it has to be flipped back before
        /// the clamp means anything. Under an orthographic projection — which is what
        /// <see cref="MatchCamera"/> forces, unconditionally, every frame in
        /// <c>ApplyCamera</c> — projection is linear and a point behind the camera still
        /// reports the correct x/y; flipping it there would send the chevron to the opposite
        /// edge. Reading <c>Camera.orthographic</c> is the only place that distinction is
        /// knowable, so it is settled here and the pure function is handed a plain bool.
        /// </remarks>
        public static bool TryProject(Camera cam, Vector3 worldPos, float margin,
                                      out Vector2 clamped, out Vector2 bearing)
            => TryProject(cam, worldPos, UniformSafeArea(margin), out clamped, out bearing);

        /// <inheritdoc cref="TryProject(Camera, Vector3, float, out Vector2, out Vector2)"/>
        public static bool TryProject(Camera cam, Vector3 worldPos, Rect safeArea,
                                      out Vector2 clamped, out Vector2 bearing)
        {
            if (cam == null)
            {
                clamped = ViewportCentre;
                bearing = Vector2.up;
                return false;
            }

            Vector3 vp = cam.WorldToViewportPoint(worldPos);
            bool behind = vp.z < 0f;

            var point = new Vector2(vp.x, vp.y);
            if (behind && !cam.orthographic) point = new Vector2(1f - point.x, 1f - point.y);

            return ClampToEdge(point, behind, safeArea, out clamped, out bearing);
        }

        /// <summary>As the <see cref="Rect"/> overload, also keeping the point out of the HUD control zones
        /// (see <see cref="ClampToEdge(Vector2, bool, Rect, IReadOnlyList{Rect}, Vector2, out Vector2, out Vector2)"/>).</summary>
        public static bool TryProject(Camera cam, Vector3 worldPos, Rect safeArea,
                                      IReadOnlyList<Rect> avoidZones, Vector2 clearance,
                                      out Vector2 clamped, out Vector2 bearing)
        {
            if (cam == null)
            {
                clamped = ViewportCentre;
                bearing = Vector2.up;
                return false;
            }

            Vector3 vp = cam.WorldToViewportPoint(worldPos);
            bool behind = vp.z < 0f;

            var point = new Vector2(vp.x, vp.y);
            if (behind && !cam.orthographic) point = new Vector2(1f - point.x, 1f - point.y);

            return ClampToEdge(point, behind, safeArea, avoidZones, clearance, out clamped, out bearing);
        }

        /// <summary>
        /// World-space height of one full viewport at <paramref name="distanceFromCamera"/>.
        /// The unit that screen-anchored world geometry should be sized in: expressing a
        /// chevron as a fraction of this makes it a constant fraction of the frame, so it
        /// survives both a change to <c>MatchCamera._orthoSize</c> and the arena rescales
        /// that keep happening (38 m to 51.3 m on 2026-08-14). A size in bare world units
        /// would not.
        /// </summary>
        public static float ViewportWorldHeight(Camera cam, float distanceFromCamera)
        {
            if (cam == null) return 0f;
            if (cam.orthographic) return cam.orthographicSize * 2f;
            return 2f * Mathf.Max(0f, distanceFromCamera) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }
    }
}
