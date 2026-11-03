using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// Where the Death Mark hunt marker goes this frame, in panel px (y down). On screen (feet in frame): above the target's
    /// plate, else below its feet pointing up, else beside it (<see cref="TryPlaceAround"/>), never on its body or any plate.
    /// Off screen, or every slot blocked: pinned to the clear-viewport edge by the rival chevron's own clamp
    /// (<see cref="ScreenEdgeProjection.TryProject(Camera, Vector3, Rect, System.Collections.Generic.IReadOnlyList{Rect}, System.Collections.Generic.IReadOnlyList{Rect}, Vector2, out Vector2, out Vector2)"/>,
    /// <see cref="HudControlZones"/>), pointing at it, with the distance.
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

            Rect you = default;
            var localPlate = local != null && local != target ? local.Nameplate : null;
            bool hasYou = localPlate != null && localPlate.TryGetPlateViewport(cam, out Rect youVp) && Assign(out you, ToPanel(youVp, panel));

            // On screen (the feet in frame, in front of the camera): never an edge pin. Above the plate, else below the feet,
            // else beside the body, whichever slot is clear of the body, every plate, the top band and the HUD zones.
            bool onScreen = vp3.z > 0f && feet.x >= 0f && feet.x <= 1f && feet.y >= 0f && feet.y <= 1f;
            if (onScreen)
            {
                var scene = BuildScene(cam, panel, target, local, info, hasYou, you);
                var plate = target.Nameplate;
                float lift = plate != null ? plate.HeadLift : 0f;
                Vector3 head = cam.WorldToViewportPoint(pos + Vector3.up * (FeedbackTuning.MarkMarkerClearHeight + lift));
                float anchorY = head.y;
                if (plate != null && plate.TryGetPlateViewport(cam, out Rect pr)) anchorY = Mathf.Max(anchorY, pr.yMax);
                float above = FeedbackTuning.MarkMarkerAnchorGapPx + FeedbackTuning.MarkPointerTipBeyondRingPx + FeedbackTuning.MarkMarkerHalfPx;
                var aboveCentre = new Vector2(head.x * panel.x, (1f - anchorY) * panel.y - above);
                if (TryPlaceAround(scene, aboveCentre, out at, out var slot))
                {
                    bearing = PointerFor(slot);
                    return true;
                }
                // Every slot round it is blocked: the edge pin below is the last resort.
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

        /// <summary>Where the marker may sit round an on-screen target.</summary>
        public enum MarkerSlot : byte { Above, Below, Left, Right }

        /// <summary>Everything a slot must stay clear of, panel px (y down).</summary>
        public struct MarkerScene
        {
            public Vector2 Panel;
            /// <summary>The target's body footprint on screen.</summary>
            public Rect Body;
            public bool HasPlate;
            public Rect TargetPlate;
            public bool HasYou;
            public Rect You;
            /// <summary>Other rivals' plates (third parties).</summary>
            public List<Rect> Others;
            /// <summary>The top of the clear area: below the top information band.</summary>
            public float ClearTop;
            /// <summary>HUD control and island information zones (joystick, hexes, status strip).</summary>
            public List<Rect> Avoid;
        }

        /// <summary>The pointer of a slot as a viewport bearing (y up): down from above, up from below, sideways from the sides.</summary>
        public static Vector2 PointerFor(MarkerSlot slot)
        {
            switch (slot)
            {
                case MarkerSlot.Below: return Vector2.up;
                case MarkerSlot.Left:  return Vector2.right;
                case MarkerSlot.Right: return Vector2.left;
                default:               return Vector2.down;
            }
        }

        /// <summary>The marker's footprint for a slot: the disc plus the pointer reaching toward the target.</summary>
        public static Rect BoxFor(Vector2 centre, MarkerSlot slot)
        {
            float r = FeedbackTuning.MarkMarkerHalfPx, reach = FeedbackTuning.MarkMarkerBoxHeightPx - r;
            switch (slot)
            {
                case MarkerSlot.Below: return Rect.MinMaxRect(centre.x - r, centre.y - reach, centre.x + r, centre.y + r);
                case MarkerSlot.Left:  return Rect.MinMaxRect(centre.x - r, centre.y - r, centre.x + reach, centre.y + r);
                case MarkerSlot.Right: return Rect.MinMaxRect(centre.x - reach, centre.y - r, centre.x + r, centre.y + r);
                default:               return Box(centre);
            }
        }

        /// <summary>
        /// Pure: the first clear slot round an on-screen target. Above (at <paramref name="aboveCentre"/>), then below its feet
        /// pointing up, then beside its body (the side with more room first). A slot is clear when its box stays on the panel and
        /// under the top band, out of every HUD zone, and off the body, the target's plate, the "You" plate (inflated) and every
        /// third-party plate. False when all four are blocked.
        /// </summary>
        public static bool TryPlaceAround(in MarkerScene s, Vector2 aboveCentre, out Vector2 centre, out MarkerSlot slot)
        {
            float gap = FeedbackTuning.MarkMarkerAnchorGapPx;
            float tip = FeedbackTuning.MarkMarkerBoxHeightPx - FeedbackTuning.MarkMarkerHalfPx;   // centre to pointer tip
            var below = new Vector2(s.Body.center.x, s.Body.yMax + gap + tip);
            var left = new Vector2(s.Body.xMin - gap - tip, s.Body.center.y);
            var right = new Vector2(s.Body.xMax + gap + tip, s.Body.center.y);
            bool rightFirst = s.Panel.x - s.Body.xMax >= s.Body.xMin;

            centre = aboveCentre;
            slot = MarkerSlot.Above;
            if (Fits(s, aboveCentre, MarkerSlot.Above)) return true;
            centre = below;
            slot = MarkerSlot.Below;
            if (Fits(s, below, MarkerSlot.Below)) return true;
            centre = rightFirst ? right : left;
            slot = rightFirst ? MarkerSlot.Right : MarkerSlot.Left;
            if (Fits(s, centre, slot)) return true;
            centre = rightFirst ? left : right;
            slot = rightFirst ? MarkerSlot.Left : MarkerSlot.Right;
            if (Fits(s, centre, slot)) return true;
            centre = default;
            slot = MarkerSlot.Above;
            return false;
        }

        public static bool Fits(in MarkerScene s, Vector2 centre, MarkerSlot slot)
        {
            var box = BoxFor(centre, slot);
            if (box.xMin < 0f || box.xMax > s.Panel.x || box.yMax > s.Panel.y || box.yMin < s.ClearTop) return false;
            if (box.Overlaps(s.Body)) return false;
            if (s.HasPlate && box.Overlaps(s.TargetPlate)) return false;
            float m = FeedbackTuning.MarkMarkerYouPlateMarginPx;
            if (s.HasYou && box.Overlaps(Rect.MinMaxRect(s.You.xMin - m, s.You.yMin - m, s.You.xMax + m, s.You.yMax + m))) return false;
            if (s.Others != null) for (int i = 0; i < s.Others.Count; i++) if (box.Overlaps(s.Others[i])) return false;
            if (s.Avoid != null) for (int i = 0; i < s.Avoid.Count; i++) if (box.Overlaps(s.Avoid[i])) return false;
            return true;
        }

        private static readonly List<Rect> OthersScratch = new List<Rect>(4);
        private static readonly List<Rect> AvoidScratch = new List<Rect>(8);

        private static MarkerScene BuildScene(Camera cam, Vector2 panel, ChickenController target, ChickenController local,
                                              IReadOnlyList<Rect> info, bool hasYou, Rect you)
        {
            var s = new MarkerScene { Panel = panel, HasYou = hasYou, You = you, Others = OthersScratch, Avoid = AvoidScratch };
            OthersScratch.Clear();
            AvoidScratch.Clear();

            // The body: the CharacterController's bounds projected (a 1 m box at the feet without one).
            var cc = target.Body;
            Bounds b = cc != null && cc.enabled ? cc.bounds : new Bounds(target.transform.position + Vector3.up * 0.5f, Vector3.one);
            s.Body = ScreenEdgeProjection.TryProjectBounds(cam, b, out Rect bvp) ? ToPanel(bvp, panel) : new Rect(-1e5f, -1e5f, 0f, 0f);

            var plate = target.Nameplate;
            s.HasPlate = plate != null && plate.TryGetPlateViewport(cam, out Rect tvp) && Assign(out s.TargetPlate, ToPanel(tvp, panel));

            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == target || c == local || c.IsDecoy) continue;
                var p = c.Nameplate;
                if (p != null && p.isActiveAndEnabled && p.TryGetPlateViewport(cam, out Rect vp)) OthersScratch.Add(ToPanel(vp, panel));
            }

            // The full-width top band sets the top of the clear area; island info zones and the control zones are avoided.
            s.ClearTop = 0f;
            if (info != null)
                for (int i = 0; i < info.Count; i++)
                {
                    var z = info[i];
                    if (z.xMin <= 0.001f && z.xMax >= 0.999f && z.yMax >= 0.999f) s.ClearTop = Mathf.Max(s.ClearTop, (1f - z.yMin) * panel.y);
                    else AvoidScratch.Add(ToPanel(z, panel));
                }
            var controls = HudControlZones.Current(cam);
            if (controls != null) for (int i = 0; i < controls.Count; i++) AvoidScratch.Add(ToPanel(controls[i], panel));
            return s;
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
