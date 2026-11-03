using CluckWars.Gameplay;
using CluckWars.Visuals;
using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// Where the Death Mark hunt marker goes this frame, in panel px (y down). Hidden target (the target's own rival-indicator
    /// verdict, or its feet outside the clear viewport): pinned to the clear-viewport edge by the rival chevron's own clamp
    /// (<see cref="ScreenEdgeProjection.TryProject(Camera, Vector3, Rect, System.Collections.Generic.IReadOnlyList{Rect}, System.Collections.Generic.IReadOnlyList{Rect}, Vector2, out Vector2, out Vector2)"/>,
    /// <see cref="HudControlZones"/>), pointing at it. Visible: above the target's head and nameplate, pointer down, clear of
    /// the local "You" plate (<see cref="AvoidPlate"/>); one that would enter the top information zone counts as hidden.
    /// </summary>
    public static class MarkerPlacement
    {
        public static bool Place(Camera cam, Vector2 panel, ChickenController target, ChickenController local,
                                 out Vector2 at, out bool offScreen, out Vector2 bearing)
        {
            at = default;
            bearing = Vector2.down;
            offScreen = false;
            if (cam == null || target == null || panel.x <= 0f || panel.y <= 0f) return false;

            var info = HudControlZones.CurrentInfo(cam);
            Vector3 pos = target.transform.position;
            Vector3 vp3 = cam.WorldToViewportPoint(pos);
            var feet = new Vector2(vp3.x, vp3.y);
            var indicator = target.Indicator;
            bool hidden = vp3.z < 0f || (indicator != null && indicator.HiddenFromClearViewport) ||
                          !ScreenEdgeProjection.IsInClearViewport(feet, info);

            Rect you = default;
            var localPlate = local != null && local != target ? local.Nameplate : null;
            bool hasYou = localPlate != null && localPlate.TryGetPlateViewport(cam, out Rect youVp) && Assign(out you, ToPanel(youVp, panel));
            float half = FeedbackTuning.MarkMarkerBoxWidthPx * 0.5f;

            if (!hidden)
            {
                var plate = target.Nameplate;
                float lift = plate != null ? plate.HeadLift : 0f;
                Vector3 head = cam.WorldToViewportPoint(pos + Vector3.up * (FeedbackTuning.MarkMarkerClearHeight + lift));
                float anchorY = head.y;
                if (plate != null && plate.TryGetPlateViewport(cam, out Rect pr)) anchorY = Mathf.Max(anchorY, pr.yMax);

                float above = FeedbackTuning.MarkMarkerAnchorGapPx + FeedbackTuning.MarkPointerTipBeyondRingPx + FeedbackTuning.MarkMarkerHalfPx;
                var centre = new Vector2(head.x * panel.x, (1f - anchorY) * panel.y - above);
                if (hasYou) centre = AvoidPlate(centre, you, FeedbackTuning.MarkMarkerYouPlateMaxRisePx);
                centre = AvoidOtherPlates(cam, panel, centre, target, local);
                centre.x = Mathf.Clamp(centre.x, half, panel.x - half);

                // Entering the top information zone counts as hidden (the doc above): the edge pin below, pointer and distance.
                var topVp = new Vector2(centre.x / panel.x, 1f - (centre.y - FeedbackTuning.MarkMarkerHalfPx) / panel.y);
                if (ScreenEdgeProjection.IsInClearViewport(topVp, info) && centre.y + FeedbackTuning.MarkMarkerBoxHeightPx < panel.y)
                {
                    at = centre;
                    return true;
                }
            }

            float c = FeedbackTuning.MarkMarkerEdgeClearancePx;
            var clearance = new Vector2(c / panel.x, (c + FeedbackTuning.MarkMarkerTopBandOverhangPx) / panel.y);
            if (!ScreenEdgeProjection.TryProject(cam, pos, FeedbackTuning.RivalChevronSafeArea, info, HudControlZones.CurrentAvoid(cam),
                    clearance, out Vector2 clamped, out bearing))
            {
                // In the clear viewport, but its marker would sit under the top band (or the indicator lags a frame): pinned to
                // the band's lower edge straight above it, pointing down at it.
                Rect clear = ScreenEdgeProjection.ClearBand(FeedbackTuning.RivalChevronSafeArea, info, clearance);
                clamped = new Vector2(Mathf.Clamp(feet.x, clear.xMin, clear.xMax), clear.yMax);
                bearing = ScreenEdgeProjection.BearingToward(clamped, feet);
            }
            offScreen = true;
            at = new Vector2(clamped.x * panel.x, (1f - clamped.y) * panel.y);
            // Pinned: no rising (it is at an edge), so it slides off any plate sideways (the target's, just below the band).
            if (hasYou) at = AvoidPlate(at, you, 0f);
            var tp = target.Nameplate;
            if (tp != null && tp.TryGetPlateViewport(cam, out Rect tvp)) at = AvoidPlate(at, ToPanel(tvp, panel), 0f);
            // The clamp's edge band is a viewport fraction of the chevron's; the marker and its distance label are wider: keep
            // the whole of them on the panel.
            at.x = Mathf.Clamp(at.x, c, panel.x - c);
            return true;
        }

        private static bool Assign(out Rect r, Rect v) { r = v; return true; }

        /// <summary>Moves a round badge of <paramref name="half"/> px radius at <paramref name="at"/> (panel px, y down) along
        /// <paramref name="direction"/> (panel axes) until it clears <paramref name="plate"/> inflated by the plate margin, at
        /// most <see cref="FeedbackTuning.MarkMarkerYouPlateMaxRisePx"/>. Pure.</summary>
        public static Vector2 SlideOff(Vector2 at, float half, Rect plate, Vector2 direction)
        {
            float m = FeedbackTuning.MarkMarkerYouPlateMarginPx;
            var inflated = Rect.MinMaxRect(plate.xMin - m, plate.yMin - m, plate.xMax + m, plate.yMax + m);
            if (direction.sqrMagnitude < 1e-8f) direction = Vector2.down;
            direction.Normalize();
            for (float moved = 0f; moved <= FeedbackTuning.MarkMarkerYouPlateMaxRisePx; moved += 4f)
            {
                var p = at + direction * moved;
                if (!Rect.MinMaxRect(p.x - half, p.y - half, p.x + half, p.y + half).Overlaps(inflated)) return p;
            }
            return at + direction * FeedbackTuning.MarkMarkerYouPlateMaxRisePx;
        }

        /// <summary>Slides the marker off the other rivals' nameplates (not the target's, not the local player's), rising as for
        /// the "You" plate. At most three rivals, cached nameplates.</summary>
        private static Vector2 AvoidOtherPlates(Camera cam, Vector2 panel, Vector2 centre, ChickenController target, ChickenController local)
        {
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == target || c == local || c.IsDecoy) continue;
                var plate = c.Nameplate;
                if (plate == null || !plate.isActiveAndEnabled || !plate.TryGetPlateViewport(cam, out Rect vp)) continue;
                centre = AvoidPlate(centre, ToPanel(vp, panel), FeedbackTuning.MarkMarkerYouPlateMaxRisePx);
            }
            return centre;
        }

        /// <summary>A viewport rect as a panel rect (y down).</summary>
        public static Rect ToPanel(Rect vp, Vector2 panel) =>
            Rect.MinMaxRect(vp.xMin * panel.x, (1f - vp.yMax) * panel.y, vp.xMax * panel.x, (1f - vp.yMin) * panel.y);

        /// <summary>
        /// Keeps the marker's box (84 x 102: disc and pointer) off the local "You" plate inflated by
        /// <see cref="FeedbackTuning.MarkMarkerYouPlateMarginPx"/>: raised by the overlap depth, at most
        /// <see cref="FeedbackTuning.MarkMarkerYouPlateMaxRisePx"/>, then slid sideways away from the plate. Pure, panel px (y down).
        /// </summary>
        public static Vector2 AvoidPlate(Vector2 centre, Rect plate) =>
            AvoidPlate(centre, plate, FeedbackTuning.MarkMarkerYouPlateMaxRisePx);

        /// <inheritdoc cref="AvoidPlate(Vector2, Rect)"/>
        public static Vector2 AvoidPlate(Vector2 centre, Rect plate, float maxRise)
        {
            float m = FeedbackTuning.MarkMarkerYouPlateMarginPx;
            var inflated = Rect.MinMaxRect(plate.xMin - m, plate.yMin - m, plate.xMax + m, plate.yMax + m);
            Rect box = Box(centre);
            if (!box.Overlaps(inflated)) return centre;

            float rise = Mathf.Min(box.yMax - inflated.yMin, Mathf.Max(0f, maxRise));
            centre.y -= rise;
            box = Box(centre);
            if (!box.Overlaps(inflated)) return centre;

            float left = box.xMax - inflated.xMin, right = inflated.xMax - box.xMin;
            centre.x += centre.x < inflated.center.x ? -left : right;
            return centre;
        }

        /// <summary>The marker's footprint round its disc centre: 84 wide, from the disc top to the pointer tip.</summary>
        public static Rect Box(Vector2 centre)
        {
            float half = FeedbackTuning.MarkMarkerBoxWidthPx * 0.5f;
            float top = FeedbackTuning.MarkMarkerHalfPx;
            return Rect.MinMaxRect(centre.x - half, centre.y - top, centre.x + half,
                centre.y - top + FeedbackTuning.MarkMarkerBoxHeightPx);
        }
    }
}
