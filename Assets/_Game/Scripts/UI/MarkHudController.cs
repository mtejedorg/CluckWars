using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Settings;
using CluckWars.Visuals;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>
    /// Draws the Death Mark's screen-space HUD (<c>Assets/UI/MarkHud.uxml</c>, sortingOrder 85) from
    /// <see cref="MarkFeedback.Current"/>: the Assassin's hunt marker (over the target, pointer down; or pinned to the
    /// clear-viewport edge, pointer at it, with the distance), and the victim's skull badge on the hunter's rival arrow.
    /// </summary>
    /// <remarks>
    /// Presentation only: every decision is <see cref="MarkPresentationModel"/>'s; this places and tints. It runs after
    /// <see cref="FloorRivalArrows"/> (the badge sits on the arrow painted this frame) and after every
    /// <see cref="RivalIndicator"/> (the marker uses the target's own hidden / visible verdict).
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(FloorRivalArrows.ExecutionOrder + 10)]
    public sealed class MarkHudController : MonoBehaviour
    {
        private const string Source = "MarkHud";

        /// <summary>Diagnostics (the <c>-cw_markshots</c> harness): what this frame drew.</summary>
        public static bool MarkerShownNow { get; private set; }
        public static bool MarkerOffScreenNow { get; private set; }
        public static bool HunterBadgeShownNow { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => MarkerShownNow = MarkerOffScreenNow = HunterBadgeShownNow = false;

        private ILogService _log;

        private VisualElement _root, _marker, _pointer, _ring, _skull, _lock, _shell, _hunterSkull;
        private Label _distance;
        private bool _bound, _reportedMissing, _missingIndicatorReported;

        // Ring / pointer paint state.
        private float _fill;
        private Color _stateColour = Color.white;

        // Last placement, kept through the end fade when the target is gone.
        private Vector2 _markerPanel;
        private bool _markerPlaced, _markerOffScreen;
        private Vector2 _markerBearing = Vector2.down;
        private int _shownMetres = -1;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
        }

        private void OnEnable() => TryBind();

        private void OnDisable()
        {
            // A disabled HUD must leave nothing on screen; the screen mood is MarkFeedback's.
            HideAll();
        }

        private void TryBind()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc != null ? doc.rootVisualElement : null;
            if (root == null) return;
            _root = root.Q<VisualElement>("MarkHudRoot");
            _marker = root.Q<VisualElement>("HuntMarker");
            _pointer = root.Q<VisualElement>("MarkPointer");
            _ring = root.Q<VisualElement>("MarkRing");
            _skull = root.Q<VisualElement>("MarkSkull");
            _lock = root.Q<VisualElement>("MarkLock");
            _shell = root.Q<VisualElement>("MarkShell");
            _distance = root.Q<Label>("MarkDistance");
            _hunterSkull = root.Q<VisualElement>("HunterSkull");
            if (_root == null || _marker == null || _pointer == null || _ring == null || _skull == null || _lock == null || _shell == null ||
                _distance == null || _hunterSkull == null)
            {
                if (!_reportedMissing)
                {
                    _reportedMissing = true;
                    _log?.Error(Source, "MarkHud.uxml is missing an element (MarkHudRoot, HuntMarker, MarkPointer, MarkRing, MarkSkull, " +
                        "MarkLock, MarkShell, MarkDistance or HunterSkull): the Death Mark hunt marker and the victim's hunter badge are not drawn.");
                }
                return;
            }
            // Shape classes on the root (the HUD probe's U6 rule; the sheet has no orientation overrides today).
            LayoutClasses.Bind(root, _root);
            _ring.generateVisualContent += DrawRing;
            _pointer.generateVisualContent += DrawPointer;
            _bound = true;
            HideAll();
        }

        private void LateUpdate()
        {
            if (!_bound)
            {
                TryBind();
                if (!_bound) return;
            }

            var o = MarkFeedback.Current;
            var cam = MatchCamera.Instance != null ? MatchCamera.Instance.Camera : null;
            Vector2 panel = PanelSize();
            if (cam == null || panel.x <= 0f || panel.y <= 0f)
            {
                HideAll();
                return;
            }

            UpdateMarker(o, cam, panel);
            UpdateHunterSkull(o, cam, panel);
            MarkerShownNow = _marker.style.display == DisplayStyle.Flex;
            MarkerOffScreenNow = MarkerShownNow && _markerOffScreen;
            HunterBadgeShownNow = _hunterSkull.style.display == DisplayStyle.Flex;
        }

        private Vector2 PanelSize()
        {
            var r = _root.layout;
            return float.IsNaN(r.width) || float.IsNaN(r.height) ? Vector2.zero : new Vector2(r.width, r.height);
        }

        // ---- Hunt marker ---------------------------------------------------------------------------------------------

        private void UpdateMarker(in MarkOutputs o, Camera cam, Vector2 panel)
        {
            if (o.Role != MarkRole.Assassin || o.MarkerAlpha <= 0f)
            {
                SetShown(_marker, false);
                _markerPlaced = false;
                return;
            }

            // Placed only while the mark lives: in the end fade it stays where it was (a respawned target is at its base).
            var target = MarkFeedback.Target;
            if (target != null && o.AssassinMarkActive &&
                MarkerPlacement.Place(cam, panel, target, LocalChicken(), out Vector2 at, out bool off, out Vector2 dir))
            {
                _markerPanel = at;
                _markerOffScreen = off;
                _markerBearing = dir;
                _markerPlaced = true;
            }
            bool offScreen = _markerOffScreen;
            Vector2 bearing = _markerBearing;
            if (!_markerPlaced)
            {
                SetShown(_marker, false);
                return;
            }

            SetShown(_marker, true);
            float boxHalf = _marker.resolvedStyle.width > 0f ? _marker.resolvedStyle.width * 0.5f : 48f;
            _marker.style.translate = new Translate(_markerPanel.x - boxHalf, _markerPanel.y - boxHalf);
            _marker.style.opacity = o.MarkerAlpha;

            float scale = o.MarkerPop;
            if (o.MarkerMode == MarkMarkerMode.Kill && !PlayerPreferences.ReducedMotionEnabled)
                scale *= 1f + FeedbackTuning.MarkMarkerKillPulse * 0.5f *
                         (1f + Mathf.Sin(Time.unscaledTime * FeedbackTuning.MarkMarkerKillPulseHz * 2f * Mathf.PI));
            _marker.style.scale = new Scale(new Vector3(scale, scale, 1f));

            Color c = o.MarkerMode == MarkMarkerMode.Kill ? FeedbackTuning.MarkKillColor
                    : o.MarkerMode == MarkMarkerMode.Walled ? FeedbackTuning.MarkWalledColor
                    : o.MarkerMode == MarkMarkerMode.Shelled ? FeedbackTuning.MarkCream
                    : PlayerPalette.ForCorner(o.TargetCorner);
            if (c != _stateColour || Mathf.Abs(o.MarkerFill01 - _fill) >= 0.004f)
            {
                _stateColour = c;
                _fill = o.MarkerFill01;
                _ring.MarkDirtyRepaint();
                _pointer.MarkDirtyRepaint();
            }
            _skull.style.unityBackgroundImageTintColor = c;
            SetShown(_lock, o.MarkerMode == MarkMarkerMode.Walled);
            SetShown(_shell, o.MarkerMode == MarkMarkerMode.Shelled);

            // Pointer: down over the target, toward it at the edge. UITK rotates clockwise; the art points down (+y).
            // The bearing is the pointer's in every placement (down above, up below, sideways beside, at the target off screen).
            float angle = Mathf.Atan2(-bearing.x, -bearing.y) * Mathf.Rad2Deg;
            _pointer.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));

            // Distance: off screen only, on the inward vertical side of the disc.
            SetShown(_distance, offScreen);
            if (offScreen)
            {
                _distance.EnableInClassList("cw-mark-distance--above", MarkerPlacement.DistanceGoesAbove(_markerPanel.y, panel.y, bearing));
                if (o.MarkerMetres != _shownMetres)
                {
                    _shownMetres = o.MarkerMetres;
                    _distance.text = UiText.Format(UiKeys.MarkDistance, ("m", _shownMetres));
                }
            }
        }

        private static ChickenController LocalChicken() => MatchCamera.Instance != null ? MatchCamera.Instance.LocalChicken : null;

        private void DrawRing(MeshGenerationContext mgc)
        {
            Rect r = _ring.contentRect;
            if (r.width <= 0f) return;
            Vector2 c = r.center;
            float radius = FeedbackTuning.MarkRingRadiusPx;
            var p = mgc.painter2D;

            var ink = FeedbackTuning.MarkInk;
            ink.a = FeedbackTuning.MarkRingTrackAlpha;
            p.strokeColor = ink;
            p.lineWidth = FeedbackTuning.MarkRingTrackWidthPx;
            p.BeginPath();
            p.Arc(c, radius, 0f, 360f);
            p.Stroke();

            if (_fill <= 0f) return;
            p.strokeColor = _stateColour;
            p.lineWidth = FeedbackTuning.MarkRingArcWidthPx;
            p.lineCap = LineCap.Butt;
            p.BeginPath();
            // From 12 o'clock (-90 degrees, y down), clockwise for the remaining fraction: it drains toward 12.
            p.Arc(c, radius, -90f, -90f + 360f * Mathf.Clamp01(_fill));
            p.Stroke();
        }

        private void DrawPointer(MeshGenerationContext mgc)
        {
            Rect r = _pointer.contentRect;
            if (r.width <= 0f) return;
            Vector2 c = r.center;
            float half = FeedbackTuning.MarkPointerBasePx * 0.5f;
            float baseY = c.y + FeedbackTuning.MarkRingRadiusPx - FeedbackTuning.MarkRingTrackWidthPx;   // tucked under the disc
            float tipY = c.y + FeedbackTuning.MarkRingRadiusPx + FeedbackTuning.MarkPointerTipBeyondRingPx;
            var p = mgc.painter2D;
            p.lineJoin = LineJoin.Miter;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x - half, baseY));
            p.LineTo(new Vector2(c.x + half, baseY));
            p.LineTo(new Vector2(c.x, tipY));
            p.ClosePath();
            p.fillColor = _stateColour;
            p.Fill();
            p.strokeColor = FeedbackTuning.MarkInk;
            p.lineWidth = FeedbackTuning.MarkPointerInkPx;
            p.Stroke();
        }

        // ---- Victim: the hunter's arrow badge ------------------------------------------------------------------------

        private void UpdateHunterSkull(in MarkOutputs o, Camera cam, Vector2 panel)
        {
            var hunter = MarkFeedback.Hunter;
            if (o.Role != MarkRole.Victim || o.HunterBadgeAlpha <= 0f || hunter == null)
            {
                SetShown(_hunterSkull, false);
                return;
            }
            var indicator = hunter.Indicator;
            if (indicator == null && !_missingIndicatorReported)
            {
                _missingIndicatorReported = true;
                _log?.Error(Source, $"{hunter.name} has no RivalIndicator: the victim's hunter badge cannot be placed (prefab wiring).");
            }
            if (indicator == null || !HunterBadgeViewport(cam, indicator, out Vector2 vp, out Vector2 outward))
            {
                SetShown(_hunterSkull, false);
                return;
            }
            SetShown(_hunterSkull, true);
            var colour = PlayerPalette.ForCorner(o.HunterCorner);
            _hunterSkull.style.borderLeftColor = _hunterSkull.style.borderRightColor =
                _hunterSkull.style.borderTopColor = _hunterSkull.style.borderBottomColor = colour;
            _hunterSkull.style.opacity = o.HunterBadgeAlpha;
            float half = _hunterSkull.resolvedStyle.width > 0f ? _hunterSkull.resolvedStyle.width * 0.5f : 22f;
            var at = new Vector2(vp.x * panel.x, (1f - vp.y) * panel.y);
            // Off the "You" plate: slid along the arrow, toward its tip (outward), until clear.
            var you = LocalChicken() != null ? LocalChicken().Nameplate : null;
            if (you != null && you.TryGetPlateViewport(cam, out Rect yvp))
                at = MarkerPlacement.SlideOff(at, half, MarkerPlacement.ToPanel(yvp, panel), new Vector2(outward.x, -outward.y));
            _hunterSkull.style.translate = new Translate(at.x - half, at.y - half);
        }

        /// <summary>The badge's viewport point: the floor arrow's midpoint (tail + half its length along the bearing, at the
        /// floor), else 0.9 chevron sizes inward from the edge chevron's centre. False when the hunter has no arrow this frame.</summary>
        private static bool HunterBadgeViewport(Camera cam, RivalIndicator indicator, out Vector2 vp, out Vector2 outward)
        {
            vp = default;
            outward = Vector2.up;
            if (FloorRivalArrows.TryGetArrow(indicator.GetInstanceID(), out Vector3 tail, out Vector3 dir, out float length))
            {
                Vector3 mid = tail + dir * (length * 0.5f);
                Vector3 v = cam.WorldToViewportPoint(mid);
                Vector3 tip = cam.WorldToViewportPoint(mid + dir * length);
                vp = new Vector2(v.x, v.y);
                var d = new Vector2(tip.x - v.x, tip.y - v.y);
                outward = d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
                return true;
            }
            if (indicator.TryGetChevronViewport(out Vector2 at, out Vector2 bearing))
            {
                outward = bearing;
                float inset = FeedbackTuning.MarkHunterBadgeChevronInset * FeedbackTuning.RivalChevronSizeFraction;
                vp = at - new Vector2(bearing.x * inset / Mathf.Max(0.1f, cam.aspect), bearing.y * inset);
                return true;
            }
            return false;
        }

        // ---- Helpers -------------------------------------------------------------------------------------------------

        private void HideAll()
        {
            MarkerShownNow = MarkerOffScreenNow = HunterBadgeShownNow = false;
            SetShown(_marker, false);
            SetShown(_hunterSkull, false);
            _markerPlaced = false;
        }

        private static void SetShown(VisualElement e, bool shown)
        {
            if (e == null) return;
            var d = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
        }
    }
}
