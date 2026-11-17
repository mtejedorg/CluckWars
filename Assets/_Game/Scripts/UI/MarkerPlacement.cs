using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// Where the Death Mark hunt marker goes this frame, in panel px (y down). On screen (feet in frame): above the target's
    /// plate, else (hunter's bird on screen) the side away from it, else below its feet pointing up, else beside it, else slid outward
    /// off the hunter (<see cref="TryPlaceAround"/>), never on the target's body, the hunter's own bird or any plate.
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
            var localPlate = local != null && local != target ? local.Nameplate() : null;
            bool hasYou = localPlate != null && localPlate.TryGetPlateViewport(cam, out Rect youVp) && Assign(out you, ToPanel(youVp, panel));

            // On screen (the feet in frame, in front of the camera): never an edge pin. Above the plate, else below the feet,
            // else beside the body, whichever slot is clear of the body, every plate, the top band and the HUD zones.
            bool onScreen = vp3.z > 0f && feet.x >= 0f && feet.x <= 1f && feet.y >= 0f && feet.y <= 1f;
            if (onScreen)
            {
                var scene = BuildScene(cam, panel, target, local, info, hasYou, you);
                var plate = target.Nameplate();
                float lift = plate != null ? plate.HeadLift : 0f;
                // The floor, raised to the top of whatever badge rows the target shows right now (read from the cached sibling).
                float clear = FeedbackTuning.MarkMarkerClearHeight + lift;
                var badges = target.StatusBadges();
                if (badges != null) clear = Mathf.Max(clear, badges.CurrentStackTopLocalY);
                Vector3 head = cam.WorldToViewportPoint(pos + Vector3.up * clear);
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
            var tp = target.Nameplate();
            if (tp != null && tp.TryGetPlateViewport(cam, out Rect tvp)) at = AvoidPlate(at, ToPanel(tvp, panel), 0f);
            // The clamp's edge band is a viewport fraction of the chevron's; the marker and its distance label are wider: keep
            // the whole of them on the panel.
            at = ClampPin(at, panel);
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
            /// <summary>K5r-c: the hunter's own bird on screen (its body footprint); the marker never lands on it.</summary>
            public bool HasLocalBody;
            public Rect LocalBody;
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
            // The side with more room first; with the hunter's bird on screen, the side AWAY from it first (K5r-c).
            bool rightFirst = s.HasLocalBody ? s.LocalBody.center.x <= s.Body.center.x
                                             : s.Panel.x - s.Body.xMax >= s.Body.xMin;

            centre = aboveCentre;
            slot = MarkerSlot.Above;
            if (Fits(s, aboveCentre, MarkerSlot.Above)) return true;
            if (!s.HasLocalBody)
            {
                centre = below;
                slot = MarkerSlot.Below;
                if (Fits(s, below, MarkerSlot.Below)) return true;
            }
            centre = rightFirst ? right : left;
            slot = rightFirst ? MarkerSlot.Right : MarkerSlot.Left;
            if (Fits(s, centre, slot)) return true;
            if (s.HasLocalBody)
            {
                centre = below;
                slot = MarkerSlot.Below;
                if (Fits(s, below, MarkerSlot.Below)) return true;
            }
            centre = rightFirst ? left : right;
            slot = rightFirst ? MarkerSlot.Left : MarkerSlot.Right;
            if (Fits(s, centre, slot)) return true;

            // Nothing is free round the target and the hunter stands in the way: slide outward, along the line from the hunter's
            // bird to the target (the way the on-arrow badge slides), from the slot that faces that way.
            if (s.HasLocalBody && TrySlideOutward(s, aboveCentre, below, left, right, out centre, out slot)) return true;
            centre = default;
            slot = MarkerSlot.Above;
            return false;
        }

        private static bool TrySlideOutward(in MarkerScene s, Vector2 above, Vector2 below, Vector2 left, Vector2 right,
                                            out Vector2 centre, out MarkerSlot slot)
        {
            Vector2 dir = s.Body.center - s.LocalBody.center;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up * -1f;
            dir.Normalize();
            Vector2 from;
            if (Mathf.Abs(dir.y) >= Mathf.Abs(dir.x))
            {
                slot = dir.y < 0f ? MarkerSlot.Above : MarkerSlot.Below;
                from = dir.y < 0f ? above : below;
            }
            else
            {
                slot = dir.x > 0f ? MarkerSlot.Right : MarkerSlot.Left;
                from = dir.x > 0f ? right : left;
            }
            for (float moved = 0f; moved <= SlideOutwardMaxPx; moved += 8f)
            {
                centre = KeepOnPanel(s, from + dir * moved, slot);
                if (Fits(s, centre, slot)) return true;
            }
            centre = default;
            return false;
        }

        /// <summary>Shifts a slot's centre the least that puts its box, and the chip above it, whole on the panel and under the top band.</summary>
        private static Vector2 KeepOnPanel(in MarkerScene s, Vector2 centre, MarkerSlot slot)
        {
            var box = BoxFor(centre, slot);
            float top = Mathf.Max(s.ClearTop, ChipAboveBoxPx);
            if (box.xMin < 0f) centre.x -= box.xMin;
            else if (box.xMax > s.Panel.x) centre.x -= box.xMax - s.Panel.x;
            if (box.yMin < top) centre.y += top - box.yMin;
            else if (box.yMax > s.Panel.y) centre.y -= box.yMax - s.Panel.y;
            return centre;
        }

        /// <summary>How far the over-head marker may slide outward off the hunter's own bird, panel px.</summary>
        public const float SlideOutwardMaxPx = 480f;

        public static bool Fits(in MarkerScene s, Vector2 centre, MarkerSlot slot)
        {
            var box = BoxFor(centre, slot);
            if (box.xMin < 0f || box.xMax > s.Panel.x || box.yMax > s.Panel.y || box.yMin < s.ClearTop) return false;
            if (box.yMin - ChipAboveBoxPx < 0f) return false;   // the chip above the disc stays on the panel
            if (box.Overlaps(s.Body)) return false;
            if (s.HasPlate && box.Overlaps(s.TargetPlate)) return false;
            float m = FeedbackTuning.MarkMarkerYouPlateMarginPx;
            if (s.HasYou && box.Overlaps(Rect.MinMaxRect(s.You.xMin - m, s.You.yMin - m, s.You.xMax + m, s.You.yMax + m))) return false;
            if (s.HasLocalBody && box.Overlaps(s.LocalBody)) return false;
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
            s.Body = BodyRect(cam, panel, target, out _);
            if (local != null && local != target)
            {
                s.LocalBody = BodyRect(cam, panel, local, out bool shown);
                s.HasLocalBody = shown;
            }

            var plate = target.Nameplate();
            s.HasPlate = plate != null && plate.TryGetPlateViewport(cam, out Rect tvp) && Assign(out s.TargetPlate, ToPanel(tvp, panel));

            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == target || c == local || c.IsDecoy) continue;
                var p = c.Nameplate();
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

        /// <summary>A chicken's body footprint on screen, panel px: its CharacterController bounds projected (a 1 m box without one).
        /// <paramref name="projected"/> is false when it has no footprint (behind the camera); the rect is then far off screen.</summary>
        private static Rect BodyRect(Camera cam, Vector2 panel, ChickenController c, out bool projected)
        {
            var cc = c.Body;
            Bounds b = cc != null && cc.enabled ? cc.bounds : new Bounds(c.transform.position + Vector3.up * 0.5f, Vector3.one);
            projected = ScreenEdgeProjection.TryProjectBounds(cam, b, out Rect vp);
            return projected ? ToPanel(vp, panel) : new Rect(-1e5f, -1e5f, 0f, 0f);
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

        /// <summary>The distance chip's lowest extent below the marker centre (MarkHud.uss: 144 px box, chip top 152, height 42: 152 + 42 - 72).</summary>
        public const float DistanceBelowExtentPx = 122f;

        /// <summary>How far the chip's top, when it sits above the disc, rises over the marker box's top (MarkHud.uss: chip top -48).</summary>
        public const float ChipAboveBoxPx = 48f;

        /// <summary>Pure: a pinned marker's centre kept far enough from every panel edge for the whole drawn marker: the 144 px box, the
        /// pointer that reaches 84 px from the centre toward the target, and the 120 px chip (the clearance is the larger of them). The
        /// safe-area band the edge projection pins to was sized for the old, smaller marker and bounds only the chevron.</summary>
        public static Vector2 ClampPin(Vector2 at, Vector2 panel)
        {
            float c = FeedbackTuning.MarkMarkerEdgeClearancePx;
            float top = Mathf.Max(c, FeedbackTuning.MarkMarkerHalfPx + ChipAboveBoxPx);   // the chip may sit above the disc at the top edge
            return new Vector2(Mathf.Clamp(at.x, c, Mathf.Max(c, panel.x - c)), Mathf.Clamp(at.y, top, Mathf.Max(top, panel.y - c)));
        }

        /// <summary>The distance label goes above the disc when the target is below it (the inward side), and whenever the
        /// label under the disc would leave the panel. Pure.</summary>
        public static bool DistanceGoesAbove(float centreY, float panelHeight, Vector2 bearing) =>
            bearing.y < 0f || centreY + DistanceBelowExtentPx > panelHeight;

        // ---- On the target's floor arrow (K5r) ---------------------------------------------------------------------------

        /// <summary>The on-arrow badge's visible radius, panel px: the ring's centre line plus half its ink track, at the badge scale.</summary>
        public static float OnArrowRadiusPx =>
            (FeedbackTuning.MarkRingRadiusPx + FeedbackTuning.MarkRingTrackWidthPx * 0.5f) * FeedbackTuning.MarkMarkerOnArrowScale;

        /// <summary>The chip's centre from the marker box centre in box px (MarkHud.uss: the 144 px box, chip 42 tall: top -48 above,
        /// top 152 below).</summary>
        public const float ChipBaseAboveCentrePx = -99f, ChipBaseBelowCentrePx = 101f;

        /// <summary>The chip's own scale: full size on the arrow badge (the marker box is never scaled, so its text stays at the HUD
        /// floor or above), the body's scale elsewhere (the appear pop, as before).</summary>
        public static float ChipScale(bool onArrow, float bodyScale) => onArrow ? 1f : bodyScale;

        /// <summary>The chip's centre from the marker box centre, px: a fixed offset on the arrow badge, the stylesheet's offset
        /// scaled with the body elsewhere.</summary>
        public static float ChipOffsetPx(bool onArrow, bool above, float bodyScale) =>
            onArrow ? (above ? -1f : 1f) * FeedbackTuning.MarkOnArrowChipOffsetPx
                    : (above ? ChipBaseAboveCentrePx : ChipBaseBelowCentrePx) * bodyScale;

        /// <summary>The chip's top edge in the marker box (px from the box's top), where it puts the chip's centre
        /// <see cref="ChipOffsetPx"/> from the box centre. It is the chip's LAYOUT place (<c>top</c>), never a translate: the UI probe
        /// reads an element's own translate as juice and judges the box where layout put it, so a pill moved by translate was
        /// measured up to 30 px below where it is drawn and flagged "partly off the panel" while it fitted.</summary>
        public static float ChipTopPx(bool onArrow, bool above, float bodyScale) =>
            MarkerBoxHalfPx + ChipOffsetPx(onArrow, above, bodyScale) - ChipHalfHeightPx;

        /// <summary>Half the marker box (MarkHud.uss: 144 px square).</summary>
        public const float MarkerBoxHalfPx = 72f;

        /// <summary>The chip's half extents, panel px ("999 m" is ~134 px wide, the pill 42 tall).</summary>
        public const float ChipHalfWidthPx = 67f, ChipHalfHeightPx = 21f;

        /// <summary>Pure: the badge centre on the arrow's tip, panel px (y down). <paramref name="outward"/> is the arrow's direction
        /// on screen (panel axes, away from the hunter's bird). The badge's visible edge stands
        /// <see cref="FeedbackTuning.MarkOnArrowGapPx"/> beyond the tip, slid further outward until it clears the hunter's own "You"
        /// plate (inflated), then kept whole on the panel.</summary>
        public static Vector2 OnArrowCentre(Vector2 tip, Vector2 outward, Vector2 panel, bool hasYou, Rect you)
        {
            if (outward.sqrMagnitude < 1e-8f) outward = Vector2.up;
            outward.Normalize();
            float r = OnArrowRadiusPx;
            Vector2 at = tip + outward * (FeedbackTuning.MarkOnArrowGapPx + r);
            if (hasYou) at = SlideOff(at, r, you, outward);
            return new Vector2(Mathf.Clamp(at.x, r, Mathf.Max(r, panel.x - r)), Mathf.Clamp(at.y, r, Mathf.Max(r, panel.y - r)));
        }

        /// <summary>Pure: whether the metres chip goes above the on-arrow badge. It takes the vertical side away from the hunter's bird
        /// (above when the arrow points up the screen), unless that side leaves the panel or lands on the hunter's "You" plate while
        /// the other side does not.</summary>
        public static bool OnArrowChipAbove(Vector2 centre, Vector2 outward, Vector2 panel, bool hasYou, Rect you)
        {
            bool preferAbove = outward.y < 0f;   // panel y is down: negative = up the screen
            bool preferred = ChipFits(centre, preferAbove, panel, hasYou, you);
            return preferred || !ChipFits(centre, !preferAbove, panel, hasYou, you) ? preferAbove : !preferAbove;
        }

        /// <summary>How far the on-arrow badge may slide back along its arrow to make the metres chip fit, panel px.</summary>
        public const float OnArrowInwardMaxPx = 240f;

        /// <summary>
        /// K5r-d, pure: settles the on-arrow badge and its metres chip so the WHOLE group (badge box and the chip's real box on the
        /// chosen side) lies inside the panel. Starting from <paramref name="centre"/> (<see cref="OnArrowCentre"/>), the chip takes
        /// the side away from the hunter's bird when it fits, else the other side; when neither fits (a tip at an edge, or the
        /// "You" plate on the other side), the badge slides back along its arrow, in 4 px steps, until one side does. The badge
        /// never slides back onto the "You" plate. If the plate cannot be respected the panel still wins. A chip wider than the badge
        /// is kept off the left and right edges too (x clamped by the chip's half width).
        /// </summary>
        public static Vector2 SettleOnArrow(Vector2 centre, Vector2 outward, Vector2 panel, bool hasYou, Rect you, out bool chipAbove)
        {
            if (outward.sqrMagnitude < 1e-8f) outward = Vector2.up;
            outward.Normalize();
            bool prefer = outward.y < 0f;
            float r = OnArrowRadiusPx;
            for (int pass = 0; pass < 2; pass++)
            {
                bool respectYou = pass == 0 && hasYou;
                for (float moved = 0f; moved <= OnArrowInwardMaxPx; moved += 4f)
                {
                    Vector2 c = ClampBadge(centre - outward * moved, panel, r);
                    if (respectYou && moved > 0f && BadgeOverlaps(c, r, you)) continue;
                    if (ChipFits(c, prefer, panel, respectYou, you)) { chipAbove = prefer; return c; }
                    if (ChipFits(c, !prefer, panel, respectYou, you)) { chipAbove = !prefer; return c; }
                }
            }
            // No room even for the panel alone (a panel smaller than the group): the preferred side, nudged whole onto the panel.
            Vector2 last = ClampBadge(centre, panel, r);
            chipAbove = prefer;
            float off = (prefer ? -1f : 1f) * FeedbackTuning.MarkOnArrowChipOffsetPx;
            float cy = last.y + off;
            if (cy - ChipHalfHeightPx < 0f) last.y += ChipHalfHeightPx - cy;
            else if (cy + ChipHalfHeightPx > panel.y) last.y -= cy + ChipHalfHeightPx - panel.y;
            return last;
        }

        private static Vector2 ClampBadge(Vector2 c, Vector2 panel, float r)
        {
            float hx = Mathf.Max(r, ChipHalfWidthPx);
            return new Vector2(Mathf.Clamp(c.x, hx, Mathf.Max(hx, panel.x - hx)), Mathf.Clamp(c.y, r, Mathf.Max(r, panel.y - r)));
        }

        private static bool BadgeOverlaps(Vector2 c, float r, Rect plate)
        {
            float m = FeedbackTuning.MarkMarkerYouPlateMarginPx;
            return Rect.MinMaxRect(c.x - r, c.y - r, c.x + r, c.y + r)
                       .Overlaps(Rect.MinMaxRect(plate.xMin - m, plate.yMin - m, plate.xMax + m, plate.yMax + m));
        }

        private static bool ChipFits(Vector2 centre, bool above, Vector2 panel, bool hasYou, Rect you)
        {
            float cy = centre.y + (above ? -1f : 1f) * FeedbackTuning.MarkOnArrowChipOffsetPx;
            var chip = Rect.MinMaxRect(centre.x - ChipHalfWidthPx, cy - ChipHalfHeightPx, centre.x + ChipHalfWidthPx, cy + ChipHalfHeightPx);
            if (chip.yMin < 0f || chip.yMax > panel.y || chip.xMin < 0f || chip.xMax > panel.x) return false;
            float m = FeedbackTuning.MarkMarkerYouPlateMarginPx;
            return !hasYou || !chip.Overlaps(Rect.MinMaxRect(you.xMin - m, you.yMin - m, you.xMax + m, you.yMax + m));
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
