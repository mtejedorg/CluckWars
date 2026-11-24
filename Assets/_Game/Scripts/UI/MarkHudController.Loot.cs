using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Visuals;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// The Thief's loot-mark half of the mark HUD (BL-219, design 2026-11-23 section 11): the coin-bag marker over the marked rival (or on its floor arrow, or
    /// pinned to the edge), the marked rival's own badge over its nameplate and the bag on the Thief's arrow. Draws only <see cref="LootFeedback.Current"/>;
    /// every decision (who sees what, hidden, blocked, reach) is <see cref="LootPresentationRules"/>'s. The anchor, the placement and the on-arrow badge are the
    /// Hunt marker's own functions, so the two cannot disagree about where a mark sits.
    /// </summary>
    public sealed partial class MarkHudController
    {
        private VisualElement _lootMarker, _lootBody, _lootPointer, _lootRing, _lootBag, _lootLock, _lootShell, _lootBadge, _lootBadgeBag, _lootArrowBag, _lootArrowBagGlyph;
        private Label _lootBadgeLabel;
        private bool _lootBound, _reportedLootMissing, _missingThiefIndicatorReported;
        private Vector2 _lootPanel, _lootBearing = Vector2.down;
        private MarkAnchor _lootAnchor;

        private void BindLoot(VisualElement root)
        {
            _lootMarker = root.Q<VisualElement>("LootMarker");
            _lootBody = root.Q<VisualElement>("LootBody");
            _lootPointer = root.Q<VisualElement>("LootPointer");
            _lootRing = root.Q<VisualElement>("LootRing");
            _lootBag = root.Q<VisualElement>("LootBag");
            _lootLock = root.Q<VisualElement>("LootLock");
            _lootShell = root.Q<VisualElement>("LootShell");
            _lootBadge = root.Q<VisualElement>("LootBadge");
            _lootBadgeBag = root.Q<VisualElement>("LootBadgeBag");
            _lootBadgeLabel = root.Q<Label>("LootBadgeLabel");
            _lootArrowBag = root.Q<VisualElement>("LootArrowBag");
            _lootArrowBagGlyph = root.Q<VisualElement>("LootArrowBagGlyph");
            if (_lootMarker == null || _lootBody == null || _lootPointer == null || _lootRing == null || _lootBag == null || _lootLock == null || _lootShell == null ||
                _lootBadge == null || _lootBadgeBag == null || _lootBadgeLabel == null || _lootArrowBag == null || _lootArrowBagGlyph == null)
            {
                if (!_reportedLootMissing)
                {
                    _reportedLootMissing = true;
                    _log?.Error(Source, "MarkHud.uxml is missing a loot element (LootMarker, LootBody, LootPointer, LootRing, LootBag, LootLock, LootShell, LootBadge, " +
                        "LootBadgeBag, LootBadgeLabel, LootArrowBag or LootArrowBagGlyph): the Thief's coin-bag marker and the marked rival's badge are not drawn.");
                }
                return;
            }
            _lootBadgeLabel.text = UiText.Get(UiKeys.HudBadgeLoot);
            _lootRing.generateVisualContent += DrawLootRing;
            _lootPointer.generateVisualContent += DrawLootPointer;
            CoinBagGlyph.Attach(_lootBag);
            CoinBagGlyph.Attach(_lootBadgeBag);
            CoinBagGlyph.Attach(_lootArrowBagGlyph);
            _lootBound = true;
            HideLoot();
        }

        private void UpdateLoot(Camera cam, Vector2 panel)
        {
            if (!_lootBound) return;
            var lo = LootFeedback.Current;
            UpdateLootMarker(in lo, cam, panel);
            UpdateLootBadge(in lo, cam, panel);
            UpdateLootArrowBag(in lo, cam, panel);
        }

        private void HideLoot()
        {
            if (!_lootBound) return;
            UiElements.SetShown(_lootMarker, false);
            UiElements.SetShown(_lootBadge, false);
            UiElements.SetShown(_lootArrowBag, false);
        }

        // ---- The Thief: the bag on the marked rival ----------------------------------------------------------------------------

        private void UpdateLootMarker(in LootOutputs lo, Camera cam, Vector2 panel)
        {
            var target = LootFeedback.Target;
            if (!lo.MarkerShown || target == null)
            {
                UiElements.SetShown(_lootMarker, false);
                return;
            }

            // One anchor per frame, the Hunt marker's: on the rival's floor arrow outside the circle, else over its head, or pinned at the edge.
            var anchor = ResolveAnchor(cam, panel, target, out Vector2 arrowAt, out Vector2 arrowOutward, out Rect you, out bool hasYou);
            Vector2 at;
            Vector2 bearing = Vector2.down;
            if (anchor == MarkAnchor.OnArrow)
                at = MarkerPlacement.SettleOnArrow(arrowAt, arrowOutward, panel, hasYou, you, out _);
            else if (!MarkerPlacement.Place(cam, panel, target, LocalChicken(), out at, out _, out bearing))
            {
                UiElements.SetShown(_lootMarker, false);
                return;
            }
            _lootAnchor = anchor;
            _lootPanel = at;
            _lootBearing = bearing;

            UiElements.SetShown(_lootMarker, true);
            float boxHalf = _lootMarker.resolvedStyle.width > 0f ? _lootMarker.resolvedStyle.width * 0.5f : 72f;
            _lootMarker.style.translate = new Translate(at.x - boxHalf, at.y - boxHalf);

            float scale = HuntRules.MarkerScale(anchor, lo.MarkerPop, 1f, 1f);
            _lootBody.style.scale = new Scale(new Vector3(scale, scale, 1f));
            UiElements.SetShown(_lootPointer, HuntRules.PointerShown(anchor));
            float angle = Mathf.Atan2(-bearing.x, -bearing.y) * Mathf.Rad2Deg;
            _lootPointer.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));

            // The blocked state: the lock for a wall of its base, the egg for a shell (the Hunt's two corner badges).
            UiElements.SetShown(_lootLock, lo.Blocker == LootBlocker.Walled);
            UiElements.SetShown(_lootShell, lo.Blocker == LootBlocker.Shelled);
        }

        private void DrawLootRing(MeshGenerationContext mgc)
        {
            Rect r = _lootRing.contentRect;
            if (r.width <= 0f) return;
            var p = mgc.painter2D;
            p.strokeColor = FeedbackTuning.LootGoldColor;
            p.lineWidth = FeedbackTuning.MarkRingTrackWidthPx;
            p.BeginPath();
            p.Arc(r.center, FeedbackTuning.MarkRingRadiusPx, 0f, 360f);
            p.Stroke();
        }

        private void DrawLootPointer(MeshGenerationContext mgc)
        {
            Rect r = _lootPointer.contentRect;
            if (r.width <= 0f) return;
            Vector2 c = r.center;
            float half = FeedbackTuning.MarkPointerBasePx * 0.5f;
            float baseY = c.y + FeedbackTuning.MarkRingRadiusPx - FeedbackTuning.MarkRingTrackWidthPx;
            float tipY = c.y + FeedbackTuning.MarkRingRadiusPx + FeedbackTuning.MarkPointerTipBeyondRingPx;
            var p = mgc.painter2D;
            p.lineJoin = LineJoin.Miter;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x - half, baseY));
            p.LineTo(new Vector2(c.x + half, baseY));
            p.LineTo(new Vector2(c.x, tipY));
            p.ClosePath();
            p.fillColor = FeedbackTuning.LootGoldColor;
            p.Fill();
            p.strokeColor = FeedbackTuning.MarkInk;
            p.lineWidth = FeedbackTuning.MarkPointerInkPx;
            p.Stroke();
        }

        // ---- The marked rival: its own badge, and the bag on the Thief's arrow -------------------------------------------------------

        private void UpdateLootBadge(in LootOutputs lo, Camera cam, Vector2 panel)
        {
            var local = LocalChicken();
            var plate = local != null ? local.Nameplate() : null;
            if (lo.VictimAlpha <= 0f || plate == null || !plate.TryGetPlateViewport(cam, out Rect vp))
            {
                UiElements.SetShown(_lootBadge, false);
                return;
            }
            Rect you = MarkerPlacement.ToPanel(vp, panel);
            UiElements.SetShown(_lootBadge, true);
            var gold = FeedbackTuning.LootGoldColor;
            _lootBadge.style.borderLeftColor = _lootBadge.style.borderRightColor = _lootBadge.style.borderTopColor = _lootBadge.style.borderBottomColor = gold;
            _lootBadge.style.opacity = lo.VictimAlpha;
            float w = _lootBadge.resolvedStyle.width > 0f ? _lootBadge.resolvedStyle.width : 150f;
            float h = _lootBadge.resolvedStyle.height > 0f ? _lootBadge.resolvedStyle.height : 52f;
            // Centred over the nameplate, just above it, held inside the panel.
            Vector2 at = new Vector2(you.center.x - w * 0.5f, you.yMin - h - FeedbackTuning.LootBadgeGapPx);
            at.x = Mathf.Clamp(at.x, 0f, Mathf.Max(0f, panel.x - w));
            at.y = Mathf.Max(0f, at.y);
            _lootBadge.style.translate = new Translate(at.x, at.y);
        }

        private void UpdateLootArrowBag(in LootOutputs lo, Camera cam, Vector2 panel)
        {
            var thief = LootFeedback.Thief;
            if (lo.VictimAlpha <= 0f || thief == null)
            {
                UiElements.SetShown(_lootArrowBag, false);
                return;
            }
            var indicator = thief.Indicator();
            if (indicator == null && !_missingThiefIndicatorReported)
            {
                _missingThiefIndicatorReported = true;
                _log?.Error(Source, $"{thief.name} has no RivalIndicator: the marked rival's bag on the Thief's arrow cannot be placed (prefab wiring).");
            }
            if (indicator == null || !HunterBadgeViewport(cam, indicator, out Vector2 vp, out Vector2 outward))
            {
                UiElements.SetShown(_lootArrowBag, false);
                return;
            }
            UiElements.SetShown(_lootArrowBag, true);
            var gold = FeedbackTuning.LootGoldColor;
            _lootArrowBag.style.borderLeftColor = _lootArrowBag.style.borderRightColor = _lootArrowBag.style.borderTopColor = _lootArrowBag.style.borderBottomColor = gold;
            _lootArrowBag.style.opacity = lo.VictimAlpha;
            float half = _lootArrowBag.resolvedStyle.width > 0f ? _lootArrowBag.resolvedStyle.width * 0.5f : 22f;
            var at = new Vector2(vp.x * panel.x, (1f - vp.y) * panel.y);
            var local = LocalChicken();
            var you = local != null ? local.Nameplate() : null;
            if (you != null && you.TryGetPlateViewport(cam, out Rect yvp))
                at = MarkerPlacement.SlideOff(at, half, MarkerPlacement.ToPanel(yvp, panel), new Vector2(outward.x, -outward.y));
            _lootArrowBag.style.translate = new Translate(at.x - half, at.y - half);
        }
    }
}
