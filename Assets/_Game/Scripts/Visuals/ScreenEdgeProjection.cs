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
            // A degenerate or inverted band would invert the clamp range and return
            // NaN-adjacent garbage; collapse it to the centre instead.
            float xMin = Mathf.Clamp01(safeArea.xMin);
            float xMax = Mathf.Clamp01(safeArea.xMax);
            float yMin = Mathf.Clamp01(safeArea.yMin);
            float yMax = Mathf.Clamp01(safeArea.yMax);
            if (xMin > xMax) xMin = xMax = 0.5f * (xMin + xMax);
            if (yMin > yMax) yMin = yMax = 0.5f * (yMin + yMax);

            bool offScreen = forceOffScreen
                             || viewport.x < 0f || viewport.x > 1f
                             || viewport.y < 0f || viewport.y > 1f;

            clamped = new Vector2(
                Mathf.Clamp(viewport.x, xMin, xMax),
                Mathf.Clamp(viewport.y, yMin, yMax));

            // Prefer clamped -> target: for a rival off the top-right this points into the
            // corner, which is a truer "that way" than a bearing measured from screen centre.
            // The centre fallback covers the forceOffScreen case, where the target can sit
            // inside the inset rect and the first difference is therefore zero.
            Vector2 d = viewport - clamped;
            if (d.sqrMagnitude < DegenerateSqr) d = viewport - ViewportCentre;
            bearing = d.sqrMagnitude < DegenerateSqr ? Vector2.up : d.normalized;

            return offScreen;
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
