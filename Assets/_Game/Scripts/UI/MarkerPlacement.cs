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
            Rect youVp = default;
            Vector3 pos = target.transform.position;
            Vector3 vp3 = cam.WorldToViewportPoint(pos);
            var feet = new Vector2(vp3.x, vp3.y);
            var indicator = target.GetComponent<RivalIndicator>();
            bool hidden = vp3.z < 0f || (indicator != null && indicator.HiddenFromClearViewport) ||
                          !ScreenEdgeProjection.IsInClearViewport(feet, info);

            if (!hidden)
            {
                var plate = target.GetComponent<ChickenNameplate>();
                float lift = plate != null ? plate.HeadLift : 0f;
                Vector3 head = cam.WorldToViewportPoint(pos + Vector3.up * (FeedbackTuning.MarkMarkerClearHeight + lift));
                float anchorY = head.y;
                if (plate != null && plate.TryGetPlateViewport(cam, out Rect pr)) anchorY = Mathf.Max(anchorY, pr.yMax);

                float above = FeedbackTuning.MarkMarkerAnchorGapPx + FeedbackTuning.MarkPointerTipBeyondRingPx + FeedbackTuning.MarkMarkerHalfPx;
                var centre = new Vector2(head.x * panel.x, (1f - anchorY) * panel.y - above);

                var localPlate = local != null ? local.GetComponent<ChickenNameplate>() : null;
                bool hasYou = localPlate != null && local != target && localPlate.TryGetPlateViewport(cam, out youVp);
                Rect you = hasYou ? ToPanel(youVp, panel) : default;
                if (hasYou) centre = AvoidPlate(centre, you, FeedbackTuning.MarkMarkerYouPlateMaxRisePx);

                var topVp = new Vector2(centre.x / panel.x, 1f - (centre.y - FeedbackTuning.MarkMarkerHalfPx) / panel.y);
                if (!ScreenEdgeProjection.IsInClearViewport(topVp, info))
                {
                    // It would sit under the top band: pinned to the band's lower edge straight above the target instead. The
                    // target is in view, so the pointer stays down and no distance shows.
                    var edgeClearance = new Vector2(FeedbackTuning.MarkMarkerEdgeClearancePx / panel.x,
                        (FeedbackTuning.MarkMarkerEdgeClearancePx + FeedbackTuning.MarkMarkerTopBandOverhangPx) / panel.y);
                    Rect band = ScreenEdgeProjection.ClearBand(FeedbackTuning.RivalChevronSafeArea, info, edgeClearance);
                    centre.y = Mathf.Max(centre.y, (1f - band.yMax) * panel.y);
                    // Pushed back down: no more rising, so it slides off the "You" plate sideways instead.
                    if (hasYou) centre = AvoidPlate(centre, you, 0f);
                }
                at = centre;
                return true;
            }

            float c = FeedbackTuning.MarkMarkerEdgeClearancePx;
            var clearance = new Vector2(c / panel.x, (c + FeedbackTuning.MarkMarkerTopBandOverhangPx) / panel.y);
            if (!ScreenEdgeProjection.TryProject(cam, pos, FeedbackTuning.RivalChevronSafeArea, info, HudControlZones.CurrentAvoid(cam),
                    clearance, out Vector2 clamped, out bearing))
            {
                // The indicator calls it hidden but the clamp finds it in the clear viewport (a frame of lag): pin to the
                // band's lower edge straight above it, pointing down at it.
                Rect clear = ScreenEdgeProjection.ClearBand(FeedbackTuning.RivalChevronSafeArea, info, clearance);
                clamped = new Vector2(Mathf.Clamp(feet.x, clear.xMin, clear.xMax), clear.yMax);
                bearing = ScreenEdgeProjection.BearingToward(clamped, feet);
            }
            offScreen = true;
            at = new Vector2(clamped.x * panel.x, (1f - clamped.y) * panel.y);
            return true;
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
