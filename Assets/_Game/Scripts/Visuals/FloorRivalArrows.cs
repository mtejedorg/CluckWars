using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// <b>DEV PROTOTYPE</b> (the default in dev-tools builds; <c>-cw_edgearrows</c> switches back): rival arrows PAINTED on a
    /// ring on the floor round the LOCAL chicken instead of at the screen edge. Maestro 2026-10-22: "for the arrows, the best
    /// and more immersive way is to put them in the outer circle at the floor", then (pass 3) "imagine they were painted on the
    /// floor". Production (no dev tools) keeps the edge chevrons exactly as before: nothing of this exists there and
    /// <see cref="RivalIndicator"/> behaves as it always did.
    /// </summary>
    /// <remarks>
    /// <para><b>Who decides.</b> Each <see cref="RivalIndicator"/> still makes its own "is this rival hidden from the clear
    /// viewport" decision (off the frame, or mostly under a HUD information zone: <see cref="ScreenEdgeProjection"/>). Where it
    /// would draw a chevron it first offers the rival here (<see cref="TryRepresent"/>); if this accepts, the chevron is
    /// suppressed and an arrow is painted on the ring instead. A rival this cannot represent (local chicken removed / absent,
    /// cover up, rival inside the ring, pool full) keeps its edge chevron, so a rival is never left with no arrow at all.</para>
    /// <para><b>Painted marking.</b> The ring and the arrows are flat meshes in the world XZ plane, just above the floor, in one
    /// paint family (<see cref="TelegraphShapes.SharedUnlitSpriteMaterial"/>, colour per vertex). Each arrow is a chevron decal
    /// whose tail sits ON the ring line at the bearing to its rival, tip outward, rotated about world up only (never a
    /// billboard) and foreshortened by the camera exactly like the ring: no screen-space fitting or compensation. Fill = the
    /// rival's player colour at paint alpha with a soft inner fade, rim = the same hue darkened. Ring drawn only while an
    /// arrow shows.</para>
    /// <para><b>Lifetime.</b> One instance, created EAGERLY after the first scene loads in a dev build
    /// (<see cref="CreateForDevBuilds"/>; removed again by <see cref="EdgeArrowsRequested"/>) and kept across scene loads. Never parented under a chicken: <c>ChickenVisuals</c>' renderer scan
    /// and ghost material swap must never find these renderers. Pooled: one ring mesh and <see cref="FloorArrowRules.MaxArrows"/>
    /// arrow meshes built in <see cref="Awake"/>; per frame only transforms move and, when a tint changes, vertex colours are
    /// rewritten from fixed buffers. Purely local presentation: no networked state, no RPCs. Nothing animates.</para>
    /// </remarks>
    [DefaultExecutionOrder(ExecutionOrder)]
    public sealed class FloorRivalArrows : MonoBehaviour
    {
        /// <summary>After <see cref="RivalIndicator"/> (0) and <see cref="ChickenNameplate"/> (1): every offer of this frame is in.</summary>
        public const int ExecutionOrder = 50;

        private const string Source = "FloorRivalArrows";

        /// <summary>Paint layers: ring under arrows, both above the rival ring (1). With the y offsets below, no z-fighting.</summary>
        private const int RingSortingOrder = 2;
        private const int ArrowSortingOrder = 3;

        /// <summary>Paint heights above the y = 0 arena floor (the telegraph outlines sit at <see cref="TelegraphShapes.GroundY"/>).</summary>
        private const float RingY = TelegraphShapes.GroundY + 0.005f;
        private const float ArrowY = TelegraphShapes.GroundY + 0.012f;

        /// <summary>Ring tessellation (segments round the circle).</summary>
        private const int RingSegments = 72;

        // ── Dev switch ─────────────────────────────────────────────────────────────────────────

        private static bool _edgeArrowsRequested;

        /// <summary>
        /// Dev comparison switch back to the shipped edge chevrons: set by <c>AutoTestDriver</c> from <c>-cw_edgearrows</c>
        /// (after its dev-tools gate). In a dev build (<see cref="DevTools.Available"/>) the floor arrows are the DEFAULT
        /// (2026-10-22: Maestro kept seeing the chevrons in the test window); in production they never exist
        /// (<see cref="FloorArrowsActive"/>). Setting it in Play creates or removes the instance at once.
        /// </summary>
        public static bool EdgeArrowsRequested
        {
            get => _edgeArrowsRequested;
            set
            {
                _edgeArrowsRequested = value;
                if (DevEnabled && Application.isPlaying) EnsureInstance();
                else if (value && _instance != null) Destroy(_instance.gameObject);
            }
        }

        /// <summary>The floor arrows are on: a dev build that did not ask for the edge chevrons. Production: edge chevrons.</summary>
        public static bool DevEnabled => FloorArrowsActive(DevTools.Available, _edgeArrowsRequested);

        /// <summary>Pure form of the gate, pinned by tests: dev builds default to the floor arrows, <c>-cw_edgearrows</c>
        /// restores the chevrons, production is always the chevrons.</summary>
        public static bool FloorArrowsActive(bool devToolsAvailable, bool edgeArrowsRequested) =>
            devToolsAvailable && !edgeArrowsRequested;

        private static FloorRivalArrows _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _edgeArrowsRequested = false;   // with domain reload off the last Play session's switch would survive
            _instance = null;
        }

        /// <summary>Dev default: the instance exists from the first scene on, with or without any <c>-cw_*</c> key (the
        /// auto-test driver only runs with one). Production: nothing is created.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateForDevBuilds()
        {
            if (DevEnabled) EnsureInstance();
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
        private bool _missingMaterialReported, _missingCombatReported;

        // Ring: a flat annulus mesh (4 vertex rings: soft inner edge, paint, paint, soft outer edge), rebuilt only on a radius change.
        private Mesh _ringMesh;
        private MeshRenderer _ringRenderer;
        private readonly Vector3[] _ringVerts = new Vector3[RingSegments * 4];
        private float _ringBuiltRadius = -1f;

        // Arrows: one flat chevron mesh each, unit length along local +Z, built once; only vertex colours change (on a tint change).
        private const int ArrowVerts = FloorArrowRules.ChevronPointCount * 4;   // rim outer, rim inner, fill edge, fill core
        private readonly Mesh[] _arrowMeshes = new Mesh[FloorArrowRules.MaxArrows];
        private readonly MeshRenderer[] _arrowRenderers = new MeshRenderer[FloorArrowRules.MaxArrows];
        private readonly Color[] _arrowColors = new Color[ArrowVerts];
        private readonly Color[] _shownTint = new Color[FloorArrowRules.MaxArrows];

        // This frame's offers (fixed pool).
        private int _queueFrame = -1;
        private int _count;
        private readonly int[] _keys = new int[FloorArrowRules.MaxArrows];
        private readonly float[] _bearings = new float[FloorArrowRules.MaxArrows];
        private readonly float[] _spread = new float[FloorArrowRules.MaxArrows];
        private readonly float[] _distances = new float[FloorArrowRules.MaxArrows];
        private readonly Color[] _tints = new Color[FloorArrowRules.MaxArrows];
        private readonly float[] _opacities = new float[FloorArrowRules.MaxArrows];

        // This frame's context, resolved once (PrepareFrame).
        private int _preparedFrame = -1;
        private bool _active;
        private Vector3 _centre;
        private float _radius = FeedbackTuning.FloorArrowPreferredRadius;
        private float _loggedRadius = -1f;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            // Self-injection per CONVENTIONS (created with AddComponent, so nothing injected it). Never gate on HasInstance.
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            var mat = TelegraphShapes.SharedUnlitSpriteMaterial(_log);   // shared URP unlit material, never destroyed here
            if (mat == null) ReportMissingMaterial();

            _ringMesh = new Mesh { name = "FloorArrowRing" };
            _ringMesh.MarkDynamic();
            _ringRenderer = BuildPaintLayer("FloorArrowRing", _ringMesh, mat, RingSortingOrder);
            _ringRenderer.transform.localPosition = new Vector3(0f, RingY, 0f);
            BuildRingTopology();

            for (int i = 0; i < _arrowMeshes.Length; i++)
            {
                _arrowMeshes[i] = BuildArrowMesh(i);
                _arrowRenderers[i] = BuildPaintLayer("FloorArrow" + i, _arrowMeshes[i], mat, ArrowSortingOrder);
                _shownTint[i] = new Color(-1f, -1f, -1f, -1f);   // forces the first colour write
                _arrowRenderers[i].gameObject.SetActive(false);
            }
            _ringRenderer.gameObject.SetActive(false);

            _log?.Info(Source, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Floor rival arrows ON (dev default, painted; -cw_edgearrows for the chevrons): ring R = min({0:0.0} m, {1:0.0} x smallest clear " +
                "ground extent), arrows {2:0.00}-{3:0.00} m long, edge chevrons suppressed for the rivals the ring represents.",
                FeedbackTuning.FloorArrowPreferredRadius, FeedbackTuning.FloorArrowLateralFraction,
                FeedbackTuning.FloorArrowNearLength, FeedbackTuning.FloorArrowFarLength));
        }

        private MeshRenderer BuildPaintLayer(string name, Mesh mesh, Material mat, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = sortingOrder;
            return r;
        }

        /// <summary>Ring triangles and colours: fixed for the session (only the vertex positions follow the radius).</summary>
        private void BuildRingTopology()
        {
            var colors = new Color[_ringVerts.Length];
            Color paint = FeedbackTuning.FloorArrowRingColor;
            paint.a = FeedbackTuning.FloorArrowRingAlpha;
            Color clear = paint;
            clear.a = 0f;
            var tris = new int[RingSegments * 3 * 6];
            int t = 0;
            for (int s = 0; s < RingSegments; s++)
            {
                for (int k = 0; k < 4; k++) colors[s * 4 + k] = k == 0 || k == 3 ? clear : paint;
                int n = (s + 1) % RingSegments;
                for (int k = 0; k < 3; k++)   // three bands: soft inner, paint, soft outer
                {
                    int a = s * 4 + k, b = s * 4 + k + 1, c = n * 4 + k + 1, d = n * 4 + k;
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = a; tris[t++] = c; tris[t++] = d;
                }
            }
            SetRingRadius(_radius);
            _ringMesh.colors = colors;
            _ringMesh.triangles = tris;
        }

        private void SetRingRadius(float radius)
        {
            float half = 0.5f * FeedbackTuning.FloorArrowRingWidth, soft = FeedbackTuning.FloorArrowRingSoftEdge;
            for (int s = 0; s < RingSegments; s++)
            {
                Vector3 d = FloorArrowRules.Direction(s * (360f / RingSegments));
                _ringVerts[s * 4 + 0] = d * (radius - half - soft);
                _ringVerts[s * 4 + 1] = d * (radius - half);
                _ringVerts[s * 4 + 2] = d * (radius + half);
                _ringVerts[s * 4 + 3] = d * (radius + half + soft);
            }
            _ringMesh.vertices = _ringVerts;   // copies the fixed buffer; no allocation of ours
            _ringMesh.RecalculateBounds();
            _ringBuiltRadius = radius;
        }

        /// <summary>
        /// One chevron decal, unit length along local +Z (scaled by the transform to the arrow's length), flat in local XZ:
        /// vertices 0-3 rim outer edge, 4-7 rim inner edge (= the fill's edge, rim colour), 8-11 the fill's edge (fill colour),
        /// 12-15 the fill's core (inset, fainter: the soft inner fade).
        /// </summary>
        private Mesh BuildArrowMesh(int index)
        {
            const int n = FloorArrowRules.ChevronPointCount;
            var shape = new Vector2[n];
            var rim = new Vector2[n];
            var core = new Vector2[n];
            FloorArrowRules.ChevronPolygon(shape);
            FloorArrowRules.OffsetPolygon(shape, n, FeedbackTuning.FloorArrowOutlineRatio, rim);
            FloorArrowRules.OffsetPolygon(shape, n, -FeedbackTuning.FloorArrowFadeInset, core);

            var verts = new Vector3[ArrowVerts];
            for (int i = 0; i < n; i++)
            {
                verts[i] = new Vector3(rim[i].x, 0f, rim[i].y);
                verts[n + i] = new Vector3(shape[i].x, 0f, shape[i].y);
                verts[2 * n + i] = verts[n + i];
                verts[3 * n + i] = new Vector3(core[i].x, 0f, core[i].y);
            }

            var tris = new int[(n * 2 * 2 + 2) * 3];
            int t = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                // rim band (outer 0..3 -> inner 4..7)
                tris[t++] = i; tris[t++] = n + i; tris[t++] = n + j;
                tris[t++] = i; tris[t++] = n + j; tris[t++] = j;
                // fill fade band (edge 8..11 -> core 12..15)
                tris[t++] = 2 * n + i; tris[t++] = 3 * n + i; tris[t++] = 3 * n + j;
                tris[t++] = 2 * n + i; tris[t++] = 3 * n + j; tris[t++] = 2 * n + j;
            }
            // core: concave chevron (tail-right 0, tip 1, tail-left 2, notch 3) as two triangles about the tip
            tris[t++] = 3 * n + 1; tris[t++] = 3 * n + 2; tris[t++] = 3 * n + 3;
            tris[t++] = 3 * n + 1; tris[t++] = 3 * n + 3; tris[t++] = 3 * n + 0;

            var mesh = new Mesh { name = "FloorArrow" + index, vertices = verts, triangles = tris };
            mesh.colors = new Color[ArrowVerts];
            mesh.RecalculateBounds();
            mesh.MarkDynamic();
            return mesh;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_ringMesh != null) Destroy(_ringMesh);
            for (int i = 0; i < _arrowMeshes.Length; i++) if (_arrowMeshes[i] != null) Destroy(_arrowMeshes[i]);
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
            if (!_active || count == 0)
            {
                HideAll();
                return;
            }

            transform.SetPositionAndRotation(new Vector3(_centre.x, 0f, _centre.z), Quaternion.identity);
            if (!Mathf.Approximately(_ringBuiltRadius, _radius)) SetRingRadius(_radius);
            SetActive(_ringRenderer, true);

            FloorArrowRules.Spread(_bearings, _keys, count, FloorArrowRules.MinSeparationDegrees(_radius), _spread);
            for (int i = 0; i < _arrowRenderers.Length; i++)
            {
                if (i >= count) { SetActive(_arrowRenderers[i], false); continue; }
                PaintArrow(i);
            }
        }

        /// <summary>Lays arrow <paramref name="i"/> flat on the floor: tail centre on the ring line at its (spread) bearing, a
        /// yaw about world up only, uniform scale = its length.</summary>
        private void PaintArrow(int i)
        {
            float b = _spread[i];
            Vector3 tail = FloorArrowRules.ArrowTail(_centre, _radius, b);
            tail.y = ArrowY;
            var t = _arrowRenderers[i].transform;
            t.SetPositionAndRotation(tail, Quaternion.Euler(0f, FloorArrowRules.ArrowYaw(b), 0f));
            t.localScale = Vector3.one * FloorArrowRules.ArrowLength(_distances[i], _radius);

            Color tint = _tints[i];
            tint.a = Mathf.Clamp01(_opacities[i]);   // the rival's own fade rides in alpha; the paint alphas multiply it
            if (tint != _shownTint[i]) WriteArrowColors(i, tint);
            SetActive(_arrowRenderers[i], true);
        }

        /// <summary>Rewrites one arrow's vertex colours from the fixed buffer (only when its tint or opacity changed).</summary>
        private void WriteArrowColors(int i, Color tintAndOpacity)
        {
            const int n = FloorArrowRules.ChevronPointCount;
            float opacity = tintAndOpacity.a;
            Color rim = FloorArrowRules.OutlineFor(tintAndOpacity);
            rim.a *= opacity;
            Color edge = tintAndOpacity;
            edge.a = FeedbackTuning.FloorArrowPaintAlpha * opacity;
            Color core = edge;
            core.a *= FeedbackTuning.FloorArrowCoreAlphaShare;
            for (int k = 0; k < n; k++)
            {
                _arrowColors[k] = rim;
                _arrowColors[n + k] = rim;
                _arrowColors[2 * n + k] = edge;
                _arrowColors[3 * n + k] = core;
            }
            _arrowMeshes[i].colors = _arrowColors;   // copies the fixed buffer; no allocation of ours
            _shownTint[i] = tintAndOpacity;
        }

        private void HideAll()
        {
            SetActive(_ringRenderer, false);
            for (int i = 0; i < _arrowRenderers.Length; i++) SetActive(_arrowRenderers[i], false);
        }

        private static void SetActive(Renderer r, bool on)
        {
            if (r != null && r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
        }

        /// <summary>No material at all (URP sprite shader AND the Sprites/Default chain missing; the helper's own Error may have
        /// gone to an earlier caller with no logger), so this says it once too: the painted ring and arrows would draw magenta.</summary>
        private void ReportMissingMaterial()
        {
            if (_missingMaterialReported) return;
            _missingMaterialReported = true;
            _log?.Error(Source, "No unlit sprite / line material in the build: the painted floor ring and arrows will draw with " +
                "Unity's default material (magenta).");
        }
    }
}
