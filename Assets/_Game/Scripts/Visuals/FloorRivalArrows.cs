using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// <b>DEV PROTOTYPE</b> (<c>-cw_floorarrows</c>, dev tools only): rival arrows on a ring on the floor round the LOCAL
    /// chicken instead of at the screen edge. Maestro 2026-10-22: "for the arrows, the best and more immersive way is to put
    /// them in the outer circle at the floor." Built to be compared side by side with the edge chevrons, which stay the default:
    /// with the switch off nothing of this exists (no object, no per-frame cost) and <see cref="RivalIndicator"/> is unchanged.
    /// </summary>
    /// <remarks>
    /// <para><b>Who decides.</b> Each <see cref="RivalIndicator"/> still makes its own "is this rival hidden from the clear
    /// viewport" decision (off the frame, or mostly under a HUD information zone: <see cref="ScreenEdgeProjection"/>). Where it
    /// would draw a chevron it first offers the rival here (<see cref="TryRepresent"/>); if this accepts, the chevron is
    /// suppressed and an arrow lies on the ring instead. A rival this cannot represent (local chicken removed / absent, cover
    /// up, rival inside the ring, pool full) keeps its edge chevron, so a rival is never left with no arrow at all.</para>
    /// <para><b>Lifetime.</b> One instance, created EAGERLY when the switch is set (<see cref="DevRequested"/>, at boot by
    /// <c>AutoTestDriver</c>) and kept across scene loads, so the first offer in a match is drawn the same frame. Never
    /// parented under a chicken: <c>ChickenVisuals</c>' renderer scan and ghost material swap must never find these renderers.
    /// It follows the local chicken in <see cref="LateUpdate"/> (after every indicator has offered, <see cref="ExecutionOrder"/>).
    /// Pooled: one ring and <see cref="FloorArrowRules.MaxArrows"/> arrows built in <see cref="Awake"/>, no per-frame
    /// allocation. Purely local presentation: no networked state, no RPCs.</para>
    /// <para><b>Drawing (iteration 2).</b> Each arrow is sized and fitted on SCREEN (<see cref="FloorArrowRules.FitAlongRay"/>:
    /// out of the HUD zones and off the player's own bird), points along the rival's real screen direction, and lies on the
    /// floor under a parent stretched by 1 / sin(pitch) along the camera's ground-forward, so an arrow pointing up or down the
    /// screen reads as long as a sideways one. Material: URP's unlit sprite shader through
    /// <see cref="TelegraphShapes.SharedUnlitSpriteMaterial"/>.</para>
    /// <para><b>Motion.</b> Nothing animates (no pulse, no bob), so Reduced Motion needs no branch here.</para>
    /// </remarks>
    [DefaultExecutionOrder(ExecutionOrder)]
    public sealed class FloorRivalArrows : MonoBehaviour
    {
        /// <summary>After <see cref="RivalIndicator"/> (0) and <see cref="ChickenNameplate"/> (1): every offer of this frame is in.</summary>
        public const int ExecutionOrder = 50;

        private const string Source = "FloorRivalArrows";

        /// <summary>The ring sits under the arrows; both above the rival ring (1) and the ambient ability outlines.</summary>
        private const int RingSortingOrder = 2;
        private const int ArrowOutlineSortingOrder = 6;
        private const int ArrowSortingOrder = 7;

        /// <summary>Draw heights above the y = 0 arena floor (the telegraph outlines sit at <see cref="TelegraphShapes.GroundY"/>).</summary>
        private const float RingY = TelegraphShapes.GroundY + 0.005f;
        private const float ArrowY = TelegraphShapes.GroundY + 0.015f;

        // ── Dev switch ─────────────────────────────────────────────────────────────────────────

        private static bool _devRequested;

        /// <summary>Set by <c>AutoTestDriver</c> from <c>-cw_floorarrows</c> (after its dev-tools gate). Inert unless
        /// <see cref="DevTools.Available"/> (<see cref="IsEnabled"/>). Turning it on in Play creates the instance at once.</summary>
        public static bool DevRequested
        {
            get => _devRequested;
            set
            {
                _devRequested = value;
                if (DevEnabled && Application.isPlaying) EnsureInstance();
                else if (!value && _instance != null) Destroy(_instance.gameObject);
            }
        }

        /// <summary>The prototype is on: requested AND dev tools available. Production behaviour (edge chevrons) otherwise.</summary>
        public static bool DevEnabled => IsEnabled(_devRequested, DevTools.Available);

        /// <summary>Pure form of the gate, pinned by tests.</summary>
        public static bool IsEnabled(bool requested, bool devToolsAvailable) => requested && devToolsAvailable;

        private static FloorRivalArrows _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _devRequested = false;   // with domain reload off the last Play session's switch would survive
            _instance = null;
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("FloorRivalArrows");
            DontDestroyOnLoad(go);   // created at boot (menu scene); lives into every match
            _instance = go.AddComponent<FloorRivalArrows>();
        }

        /// <summary>
        /// Offered by a <see cref="RivalIndicator"/> that would draw an edge chevron for the rival at <paramref name="rivalPos"/>.
        /// True when a floor arrow will represent it this frame (the caller then suppresses its chevron). <paramref name="key"/>
        /// is a stable per-rival id (tie-break of the de-overlap). False at once while the prototype is off; never creates
        /// anything (the instance is made when the switch is set).
        /// </summary>
        public static bool TryRepresent(int key, Vector3 rivalPos, Color tint, float opacity)
        {
            if (!DevEnabled || _instance == null) return false;
            return _instance.Accept(key, rivalPos, tint, opacity);
        }

        // ── Instance ───────────────────────────────────────────────────────────────────────────

        private ILogService _log;

        private LineRenderer _ring;
        private readonly Vector3[] _ringPoints = new Vector3[TelegraphShapes.CircleSegments];
        private float _ringBuiltRadius = -1f;
        private bool _missingMaterialReported, _missingCombatReported;

        // Per arrow: a root lying flat in the camera's ground frame, depth-stretched; the fill (rotated in that plane) under it.
        private readonly Transform[] _roots = new Transform[FloorArrowRules.MaxArrows];
        private readonly SpriteRenderer[] _arrows = new SpriteRenderer[FloorArrowRules.MaxArrows];
        private readonly SpriteRenderer[] _outlines = new SpriteRenderer[FloorArrowRules.MaxArrows];

        // This frame's offers (fixed pool).
        private int _queueFrame = -1;
        private int _count;
        private readonly int[] _keys = new int[FloorArrowRules.MaxArrows];
        private readonly float[] _bearings = new float[FloorArrowRules.MaxArrows];
        private readonly float[] _spread = new float[FloorArrowRules.MaxArrows];
        private readonly float[] _distances = new float[FloorArrowRules.MaxArrows];
        private readonly Color[] _tints = new Color[FloorArrowRules.MaxArrows];
        private readonly float[] _opacities = new float[FloorArrowRules.MaxArrows];

        // HUD zones + the bird's body in screen metres, rebuilt each drawn frame (fixed scratch: body + <= 7 avoid zones).
        private readonly Rect[] _obstacles = new Rect[12];

        // This frame's context, resolved once (PrepareFrame).
        private int _preparedFrame = -1;
        private bool _active;
        private Camera _cam;
        private Vector3 _centre;
        private float _radius = FeedbackTuning.FloorArrowPreferredRadius;
        private float _loggedRadius = -1f;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            // Self-injection per CONVENTIONS (created with AddComponent, so nothing injected it). Never gate on HasInstance.
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            var mat = TelegraphShapes.SharedUnlitSpriteMaterial(_log);   // shared URP unlit sprite material, never destroyed here
            if (mat == null) ReportMissingMaterial();

            _ring = TelegraphShapes.BuildLine(transform, "FloorArrowRing", mat, FeedbackTuning.FloorArrowRingWidth, loop: true);
            _ring.useWorldSpace = false;   // points are fixed per radius; the root follows the chicken
            _ring.sortingOrder = RingSortingOrder;
            if (mat != null) _ring.sharedMaterial = mat;
            _ring.enabled = false;

            for (int i = 0; i < _arrows.Length; i++)
            {
                var root = new GameObject("FloorArrow" + i);
                root.transform.SetParent(transform, worldPositionStays: false);

                var fillGo = new GameObject("FloorArrowFill");
                fillGo.transform.SetParent(root.transform, worldPositionStays: false);
                var fill = fillGo.AddComponent<SpriteRenderer>();
                fill.sprite = RivalIndicator.SharedChevronSprite();
                fill.sortingOrder = ArrowSortingOrder;
                if (mat != null) fill.sharedMaterial = mat;

                var edgeGo = new GameObject("FloorArrowOutline");
                edgeGo.transform.SetParent(fillGo.transform, worldPositionStays: false);
                var edge = edgeGo.AddComponent<SpriteRenderer>();
                edge.sprite = RivalIndicator.SharedThickChevronOutlineSprite();
                edge.sortingOrder = ArrowOutlineSortingOrder;
                if (mat != null) edge.sharedMaterial = mat;

                _roots[i] = root.transform;
                _arrows[i] = fill;
                _outlines[i] = edge;
                root.SetActive(false);
            }

            _log?.Info(Source, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Floor rival arrows ON (dev prototype, -cw_floorarrows): ring R = min({0:0.0} m, {1:0.0} x smallest clear ground extent), " +
                "arrows {2:0.0}-{3:0.0} m, edge chevrons suppressed for the rivals the ring represents.",
                FeedbackTuning.FloorArrowPreferredRadius, FeedbackTuning.FloorArrowLateralFraction,
                FeedbackTuning.FloorArrowNearSize, FeedbackTuning.FloorArrowFarSize));
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OnDisable() => HideAll();

        private bool Accept(int key, Vector3 rivalPos, Color tint, float opacity)
        {
            PrepareFrame();
            if (!_active) return false;

            float distance = FloorArrowRules.PlanarDistance(_centre, rivalPos);
            // hidden = true: the caller only offers a rival it would have drawn an edge chevron for.
            if (!FloorArrowRules.WantsArrow(true, distance, _radius)) return false;

            if (_queueFrame != Time.frameCount) { _queueFrame = Time.frameCount; _count = 0; }
            if (_count >= FloorArrowRules.MaxArrows) return false;   // more hidden marks than arrows (enemy decoys): the rest keep their chevrons

            int i = _count++;
            _keys[i] = key;
            _bearings[i] = FloorArrowRules.Bearing(_centre, rivalPos);
            _distances[i] = distance;
            _tints[i] = tint;
            _opacities[i] = opacity;
            return true;
        }

        /// <summary>Resolves, once per frame, whether the ring is live and where: the local chicken present and not removed
        /// (dead / eliminated), the cover and the warm-up overview gone, a match camera present.</summary>
        private void PrepareFrame()
        {
            if (_preparedFrame == Time.frameCount) return;
            _preparedFrame = Time.frameCount;
            _active = false;

            var mc = MatchCamera.Instance;
            if (mc == null || mc.Camera == null || mc.WarmupFramingActive || RevealState.CoverShown) return;

            var local = mc.LocalChicken;
            if (local == null) return;                                   // spectating / not spawned yet
            var obj = local.Object;
            if (obj == null || !obj.IsValid) return;
            if (local.Combat == null)
            {
                if (!_missingCombatReported)
                {
                    _missingCombatReported = true;
                    _log?.Warn(Source, "Local chicken has no ChickenCombat: cannot tell whether it is removed, floor arrows stay hidden.");
                }
                return;
            }
            if (local.Combat.IsRemoved) return;                          // dead or eliminated (IsDead is the same flag)

            var cam = mc.Camera;
            _cam = cam;
            _radius = cam.orthographic   // MatchCamera forces orthographic every frame; anything else gets the preferred radius
                ? FloorArrowRules.FramingRadius(cam.aspect, cam.orthographicSize, cam.transform.eulerAngles.x,
                    HudControlZones.CurrentInfo(cam), HudControlZones.Current(cam))
                : FeedbackTuning.FloorArrowPreferredRadius;
            _centre = local.transform.position;
            _active = true;

            if (Mathf.Abs(_radius - _loggedRadius) > 0.01f)   // first live frame, and every orientation / framing change
            {
                _loggedRadius = _radius;
                _log?.Info(Source, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Floor ring radius={0:0.00} m (aspect {1:0.000}, ortho {2:0.00})", _radius, cam.aspect, cam.orthographicSize));
            }
        }

        private void LateUpdate()
        {
            PrepareFrame();
            int count = _queueFrame == Time.frameCount ? _count : 0;
            if (!_active || count == 0 || !_cam.orthographic)
            {
                HideAll();
                return;
            }

            transform.SetPositionAndRotation(new Vector3(_centre.x, 0f, _centre.z), Quaternion.identity);
            ShowRing();

            float yaw = _cam.transform.eulerAngles.y;
            float pitch = _cam.transform.eulerAngles.x;
            Rect frame = BuildScreenObstacles(out int obstacleCount);

            FloorArrowRules.Spread(_bearings, _keys, count, FloorArrowRules.MinSeparationDegrees(_radius), _spread);
            for (int i = 0; i < _arrows.Length; i++)
            {
                if (i >= count) { SetActive(_roots[i], false); continue; }
                PlaceArrow(i, yaw, pitch, frame, obstacleCount);
            }
        }

        /// <summary>The viewport (less <see cref="FeedbackTuning.FloorArrowEdgeMargin"/>) in screen metres round the chicken as it
        /// is drawn this frame, and the obstacles: the bird's own body, then every HUD avoid zone (controls, top band, status strip).</summary>
        private Rect BuildScreenObstacles(out int count)
        {
            Vector3 vp = _cam.WorldToViewportPoint(_centre);
            var focus = new Vector2(vp.x, vp.y);
            float s = _cam.orthographicSize, a = _cam.aspect;
            Rect frame = FloorArrowRules.ViewportToScreenMetres(new Rect(0f, 0f, 1f, 1f), focus, s, a);
            float m = FeedbackTuning.FloorArrowEdgeMargin;
            frame = Rect.MinMaxRect(frame.xMin + m, frame.yMin + m, frame.xMax - m, frame.yMax - m);

            count = 0;
            _obstacles[count++] = FeedbackTuning.FloorArrowBodyRect;
            var avoid = HudControlZones.CurrentAvoid(_cam);
            for (int i = 0; i < avoid.Count && count < _obstacles.Length; i++)
                _obstacles[count++] = FloorArrowRules.ViewportToScreenMetres(avoid[i], focus, s, a);
            return frame;
        }

        private void PlaceArrow(int i, float yaw, float pitch, Rect frame, int obstacleCount)
        {
            float rel = _spread[i] - yaw;
            Vector2 dir = FloorArrowRules.ScreenDirection(rel, pitch);
            Vector2 ringPoint = FloorArrowRules.ScreenRingPoint(rel, _radius, pitch);
            float size = FloorArrowRules.FitAlongRay(ringPoint, dir, FloorArrowRules.ArrowSize(_distances[i], _radius),
                frame, _obstacles, obstacleCount, out float offset);

            // Screen metres -> ground in the camera's frame -> world.
            Vector2 ground = FloorArrowRules.ScreenToGround(ringPoint + dir * offset, pitch);
            Quaternion camGround = Quaternion.Euler(0f, yaw, 0f);
            Vector3 p = _centre + camGround * new Vector3(ground.x, 0f, ground.y);
            p.y = ArrowY;

            // Root: flat in the camera's ground frame, stretched along its forward by 1 / sin(pitch). Fill: rotated inside that
            // plane to the SCREEN direction, uniform size, so after the stretch and the camera's foreshortening it reads undistorted.
            var root = _roots[i];
            root.SetPositionAndRotation(p, camGround);
            root.localScale = new Vector3(1f, 1f, FloorArrowRules.DepthStretch(pitch));
            var t = _arrows[i].transform;
            t.localRotation = Quaternion.LookRotation(Vector3.down, new Vector3(dir.x, 0f, dir.y));
            t.localScale = Vector3.one * size;

            float alpha = FloorArrowRules.ArrowAlpha(_distances[i], _radius) * _opacities[i];
            Color c = _tints[i];
            _arrows[i].color = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));
            Color ink = RivalIndicator.ChevronOutlineColor;   // full-strength ink rim at every distance (only the rival's own fade)
            _outlines[i].color = new Color(ink.r, ink.g, ink.b, Mathf.Clamp01(FeedbackTuning.RivalChevronOutlineAlpha * _opacities[i]));
            SetActive(root, true);
        }

        private void ShowRing()
        {
            if (_ring == null) return;
            if (!Mathf.Approximately(_ringBuiltRadius, _radius))
            {
                for (int i = 0; i < _ringPoints.Length; i++)
                {
                    float a = i * (360f / _ringPoints.Length);
                    Vector3 d = FloorArrowRules.Direction(a) * _radius;
                    _ringPoints[i] = new Vector3(d.x, RingY, d.z);
                }
                _ring.positionCount = _ringPoints.Length;
                _ring.SetPositions(_ringPoints);
                _ringBuiltRadius = _radius;
            }
            Color rc = FeedbackTuning.FloorArrowRingColor;
            rc.a = FeedbackTuning.FloorArrowRingAlpha;
            _ring.startColor = rc;
            _ring.endColor = rc;
            if (!_ring.enabled) _ring.enabled = true;
        }

        private void HideAll()
        {
            if (_ring != null && _ring.enabled) _ring.enabled = false;
            for (int i = 0; i < _roots.Length; i++) SetActive(_roots[i], false);
        }

        private static void SetActive(Transform t, bool on)
        {
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        /// <summary>No material at all (URP sprite shader AND the Sprites/Default chain missing; the helper's own Error may have
        /// gone to an earlier caller with no logger), so this says it once too: ring and arrows would draw magenta or not at all.</summary>
        private void ReportMissingMaterial()
        {
            if (_missingMaterialReported) return;
            _missingMaterialReported = true;
            _log?.Error(Source, "No unlit sprite / line material in the build: the floor-arrow ring will not draw and the arrows " +
                "fall back to the sprite default material.");
        }
    }
}
