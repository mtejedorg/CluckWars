using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Logging;
using UnityEngine;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// How a chicken is marked inside a live aim preview (FEEDBACK.md §2.2).
    /// Produced by <see cref="AbilityTelegraph.TryClassify"/>, consumed by
    /// <see cref="TargetHighlight"/>.
    /// </summary>
    public enum TargetMark : byte
    {
        /// <summary>Not inside the charging ability's aim shape — draw nothing.</summary>
        None = 0,
        /// <summary>Inside the shape, selected by <c>GatherTargets</c>, and the effect will
        /// actually land — this chicken will be hit, and it will matter.</summary>
        Valid = 1,
        /// <summary>Inside the shape, but nothing will come of it. Two ways to land here, and
        /// the bracket deliberately does not distinguish them because the player's question is
        /// the same either way: <c>GatherTargets</c> did not select this chicken (no cargo to
        /// steal, wrong arc, not the nearest single target), or it did and
        /// <c>AbilityBaseSO.IsEffectNullified</c> says the effect is refused on arrival
        /// (Immovable). The second case is why this is no longer simply the gap between
        /// <c>IsInAimShape</c> and eligibility.</summary>
        NoEffect = 2,
    }

    /// <summary>
    /// Pure, MonoBehaviour-free outline geometry for every <see cref="AbilityAimShape"/>,
    /// shared by the hold-to-aim preview (<see cref="AbilityTelegraph"/>) and the impact
    /// cast flash (<see cref="AbilityRangeIndicator"/>) so the two can never disagree
    /// about what an ability's area looks like (FEEDBACK.md §4's "one descriptor, four
    /// consumers"). Positions only — no colour, no timing, no allocation beyond growing
    /// a caller-owned buffer when the required point count changes.
    /// </summary>
    /// <remarks>
    /// Deliberately mirrors <see cref="AbilityAim"/>'s planar-XZ convention: the arena
    /// is flat, so every outline is written at a fixed <see cref="GroundY"/> and the
    /// caster's forward is flattened before use. Every shape is emitted as a *closed*
    /// polyline (<c>LineRenderer.loop = true</c>) — a circle closes on itself, a cone
    /// closes back through the caster's own position.
    /// </remarks>
    public static class TelegraphShapes
    {
        /// <summary>Segment count for a full circle. Matches <c>AbilityRangeIndicator</c>'s
        /// original ring resolution so the preview and the flash have identical smoothness.</summary>
        public const int CircleSegments = 48;

        /// <summary>Draw height, just above the y=0 arena plane to avoid z-fighting.
        /// Same value <c>AbilityRangeIndicator</c> has always used.</summary>
        public const float GroundY = 0.06f;

        /// <summary>
        /// Normalises an ability's aim descriptor into drawable parameters. Collapses the
        /// two cases where the authored descriptor is not directly drawable:
        /// <list type="bullet">
        ///   <item><c>Jump</c> draws a landing ring at the fixed
        ///   <see cref="AbilityBaseSO.JumpLandingRadius"/>, not at <c>AimRadius</c> —
        ///   the jump's *length* is what <c>AimForwardOffset</c> describes.</item>
        ///   <item>A non-positive radius (self-buffs, or an ability that declares a shape
        ///   but no reach) degrades to a caster-centred self-ring at
        ///   <see cref="FeedbackTuning.SelfRingRadius"/> rather than a zero-size line.</item>
        /// </list>
        /// </summary>
        public static void Resolve(AbilityBaseSO ability, out AbilityAimShape shape,
                                   out float radius, out float forwardOffset, out float coneAngleDeg)
        {
            shape         = AbilityAimShape.None;
            radius        = FeedbackTuning.SelfRingRadius;
            forwardOffset = 0f;
            coneAngleDeg  = 360f;
            if (ability == null) return;

            shape         = ability.AimShape;
            forwardOffset = ability.AimForwardOffset;
            coneAngleDeg  = ability.AimConeAngle;

            radius = shape == AbilityAimShape.Jump ? AbilityBaseSO.JumpLandingRadius : ability.AimRadius;

            if (shape == AbilityAimShape.None || radius <= 0f)
            {
                shape         = AbilityAimShape.None;
                radius        = FeedbackTuning.SelfRingRadius;
                forwardOffset = 0f;
                coneAngleDeg  = 360f;
            }
        }

        /// <summary>Points per cap on a <see cref="AbilityAimShape.Capsule"/> outline. Two
        /// caps at 24 sum to <see cref="CircleSegments"/>, so a capsule costs exactly what a
        /// circle costs and <see cref="Apply"/> never has to resize its buffer when an
        /// ability switches between the two.</summary>
        private const int CapsuleCapSegments = CircleSegments / 2;

        /// <summary>
        /// Does this shape need the extra "stalk" line from the caster out to the shape's
        /// centre? Only the *detached* offset shapes do, and only when the offset is big
        /// enough to actually read as an offset. Deliberately excludes
        /// <see cref="AbilityAimShape.Capsule"/>: its near cap already touches the caster,
        /// so a connector to its own midpoint would be a line drawn inside the outline —
        /// pure clutter, and it would read as a second, narrower area.
        /// </summary>
        public static bool NeedsStalk(AbilityAimShape shape, float forwardOffset) =>
            (shape == AbilityAimShape.ForwardCircle || shape == AbilityAimShape.Jump) &&
            forwardOffset > 0.01f;

        /// <summary>Number of positions <see cref="Write"/> will emit for this shape.
        /// Everything except a narrow cone — the capsule included — emits exactly
        /// <see cref="CircleSegments"/>.</summary>
        public static int PointCount(AbilityAimShape shape, float coneAngleDeg)
        {
            if (shape == AbilityAimShape.Cone && coneAngleDeg < 360f)
                return 1 + ConeArcSegments(coneAngleDeg) + 1; // caster + inclusive arc ends
            return CircleSegments;
        }

        /// <summary>
        /// Writes the closed outline into <paramref name="buf"/>, which must be at least
        /// <see cref="PointCount"/> long. Every shape except a narrow cone is a circle;
        /// a cone at &gt;= 360 degrees degenerates to a plain circle exactly as
        /// <see cref="AbilityAim.InShape"/> does (Ambush depends on this).
        /// </summary>
        public static void Write(Vector3[] buf, AbilityAimShape shape, Vector3 casterPos, Vector3 casterForward,
                                 float radius, float forwardOffset, float coneAngleDeg)
        {
            if (buf == null) return;

            Vector3 fwd    = PlanarForward(casterForward);
            Vector3 center = AbilityAim.ShapeCenter(shape, casterPos, casterForward, forwardOffset);
            center.y = GroundY;

            if (shape == AbilityAimShape.Cone && coneAngleDeg < 360f)
            {
                int   arcSeg = ConeArcSegments(coneAngleDeg);
                float half   = coneAngleDeg * 0.5f;

                Vector3 apex = casterPos; apex.y = GroundY;
                buf[0] = apex;
                for (int i = 0; i <= arcSeg; i++)
                {
                    float deg = -half + coneAngleDeg * (i / (float)arcSeg);
                    buf[1 + i] = center + RotateY(fwd, deg) * radius;
                }
                return;
            }

            if (shape == AbilityAimShape.Capsule)
            {
                // One closed polyline, not two circles plus two segments: half a turn around
                // the far cap sweeping through +forward, then half a turn around the near cap
                // sweeping through -forward. The two straight sides fall out for free as the
                // edges joining the caps — the last far point and the first near point share a
                // bearing, and loop = true closes the other side.
                float   length    = Mathf.Max(0f, forwardOffset);
                Vector3 nearCap   = casterPos; nearCap.y = GroundY;
                Vector3 farCap    = nearCap + fwd * length;
                float   lastIndex = CapsuleCapSegments - 1;

                for (int i = 0; i < CapsuleCapSegments; i++)
                {
                    float k = i / lastIndex;                    // 0..1 inclusive at both ends
                    buf[i]                        = farCap  + RotateY(fwd, -90f + 180f * k) * radius;
                    buf[CapsuleCapSegments + i]   = nearCap + RotateY(fwd,  90f + 180f * k) * radius;
                }
                return;
            }

            for (int i = 0; i < CircleSegments; i++)
            {
                float a = (i / (float)CircleSegments) * Mathf.PI * 2f;
                buf[i] = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            }
        }

        /// <summary>
        /// Sizes, fills and pushes the outline into <paramref name="lr"/>. Grows
        /// <paramref name="buf"/> only when the required point count changes — which
        /// happens when the drawn shape changes (i.e. a new charge begins), never on a
        /// steady frame, so a hold costs zero allocation per frame (FEEDBACK.md §10).
        /// </summary>
        public static void Apply(LineRenderer lr, ref Vector3[] buf, AbilityAimShape shape,
                                 Vector3 casterPos, Vector3 casterForward,
                                 float radius, float forwardOffset, float coneAngleDeg)
        {
            if (lr == null) return;

            int n = PointCount(shape, coneAngleDeg);
            if (buf == null || buf.Length != n) buf = new Vector3[n];

            Write(buf, shape, casterPos, casterForward, radius, forwardOffset, coneAngleDeg);

            if (lr.positionCount != n) lr.positionCount = n;
            if (!lr.loop) lr.loop = true; // every emitted outline is closed
            lr.SetPositions(buf);
        }

        /// <summary>Two-point line from the caster out to an offset shape's centre, so the
        /// offset itself reads instead of a circle floating with no visible owner.</summary>
        public static void ApplyStalk(LineRenderer lr, Vector3[] buf2, Vector3 casterPos, Vector3 center)
        {
            if (lr == null || buf2 == null || buf2.Length < 2) return;

            casterPos.y = GroundY;
            center.y    = GroundY;
            buf2[0] = casterPos;
            buf2[1] = center;

            if (lr.positionCount != 2) lr.positionCount = 2;
            if (lr.loop) lr.loop = false;
            lr.SetPositions(buf2);
        }

        /// <summary>Unlit, vertex-coloured, transparent line material. Identical fallback
        /// chain to <c>AbilityRangeIndicator</c> / <c>ControlStateVFX</c> — Sprites/Default
        /// is present in every project and honours per-vertex alpha under URP.</summary>
        public static Material BuildLineMaterial(ILogService log = null) => CreateLineMaterial(Shader.Find, log);

        private static bool _reportedNoLineShader;

        /// <summary>Test seam: <paramref name="find"/> stands in for <c>Shader.Find</c>. A missing shader is a build /
        /// wiring problem (every telegraph line would draw magenta or not at all), so it is logged as an Error ONCE per
        /// session, however many lines ask.</summary>
        public static Material CreateLineMaterial(System.Func<string, Shader> find, ILogService log)
        {
            Shader sh = find("Sprites/Default");
            if (sh == null) sh = find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            if (sh != null) return new Material(sh);

            if (!_reportedNoLineShader)
            {
                _reportedNoLineShader = true;
                log?.Error("TelegraphShapes", "No line shader found (Sprites/Default, URP Particles/Unlit and the legacy " +
                    "fallback are all missing from the build): ability telegraphs, range rings and zone rings will not draw. " +
                    "Add one to Always Included Shaders.");
            }
            return null;
        }

        private static Material _sharedSolidLine;

        /// <summary>One unlit line material shared by every telegraph chicken (they all use it unmodified; colour is
        /// per-vertex). Replaces a per-instance material that was never destroyed.</summary>
        public static Material SharedSolidLineMaterial(ILogService log)
        {
            if (_sharedSolidLine == null) _sharedSolidLine = BuildLineMaterial(log);
            return _sharedSolidLine;
        }

        /// <summary>URP's own unlit sprite shader: one pass with no LightMode tag (drawn by the Universal Renderer's
        /// SRPDefaultUnlit pass), transparent, reads <c>_MainTex</c> (which SpriteRenderer binds) and the vertex colour (which
        /// SpriteRenderer.color and LineRenderer colours write). It ships with URP and is the project's default sprite / unlit
        /// material in <c>UniversalRenderPipelineGlobalSettings</c>.</summary>
        public const string UrpUnlitSpriteShader = "Universal Render Pipeline/2D/Sprite-Unlit-Default";

        private static Material _sharedUnlitSprite;
        private static bool _reportedSpriteFallback;

        /// <summary>
        /// One shared URP unlit transparent material for flat world sprites and lines that take their colour per vertex
        /// (<see cref="FloorRivalArrows"/>). Prefers <see cref="UrpUnlitSpriteShader"/>; if a player build stripped it, falls back
        /// to <see cref="BuildLineMaterial"/>'s chain (Sprites/Default, what every telegraph already draws with) and says so
        /// once as a Warn; if that is missing too, its own once-per-session Error fires and this returns null.
        /// </summary>
        public static Material SharedUnlitSpriteMaterial(ILogService log)
        {
            if (_sharedUnlitSprite == null) _sharedUnlitSprite = CreateUnlitSpriteMaterial(Shader.Find, log);
            return _sharedUnlitSprite;
        }

        /// <summary>Test seam of <see cref="SharedUnlitSpriteMaterial"/>: <paramref name="find"/> stands in for <c>Shader.Find</c>.</summary>
        public static Material CreateUnlitSpriteMaterial(System.Func<string, Shader> find, ILogService log)
        {
            Shader urp = find(UrpUnlitSpriteShader);
            if (urp != null) return new Material(urp);

            if (!_reportedSpriteFallback)
            {
                _reportedSpriteFallback = true;
                log?.Warn("TelegraphShapes", $"'{UrpUnlitSpriteShader}' is not in this build: floor arrows fall back to the " +
                    "telegraph line material (Sprites/Default). Add it to Always Included Shaders to use the URP shader.");
            }
            return CreateLineMaterial(find, log);
        }

        /// <summary>Clears the statics between Editor play sessions (domain reload may be off): a destroyed shared material
        /// would otherwise be handed out, and the once-per-session report would never fire again.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLineStatics()
        {
            _reportedNoLineShader = false;
            _sharedSolidLine = null;
            _reportedSpriteFallback = false;
            _sharedUnlitSprite = null;
        }

        /// <summary>Test seam: forget the once-per-session report and the shared material.</summary>
        public static void ResetLineMaterialStateForTests()
        {
            _reportedNoLineShader = false;
            if (_sharedSolidLine != null) Object.DestroyImmediate(_sharedSolidLine);
            _sharedSolidLine = null;
            _reportedSpriteFallback = false;
            if (_sharedUnlitSprite != null) Object.DestroyImmediate(_sharedUnlitSprite);
            _sharedUnlitSprite = null;
        }

        /// <summary>Builds a world-space ground LineRenderer, mirroring
        /// <c>AbilityRangeIndicator.BuildRing</c> exactly.</summary>
        public static LineRenderer BuildLine(Transform parent, string goName, Material mat, float width, bool loop)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, worldPositionStays: false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true; // absolute points, so chicken rotation never warps the outline
            lr.loop              = loop;
            lr.positionCount     = loop ? CircleSegments : 2;
            lr.widthMultiplier   = width;
            lr.numCapVertices    = 0;
            lr.numCornerVertices = 0;
            lr.alignment         = LineAlignment.View; // ribbon faces camera — readable on the iso view
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.textureMode       = LineTextureMode.Stretch;
            if (mat != null) lr.material = mat;
            return lr;
        }

        /// <summary>Arc resolution for a cone, proportional to its angle so a 120 degree
        /// cone doesn't pay for 48 segments.</summary>
        private static int ConeArcSegments(float coneAngleDeg)
        {
            float a = Mathf.Clamp(coneAngleDeg, 1f, 360f);
            return Mathf.Max(4, Mathf.CeilToInt(CircleSegments * (a / 360f)));
        }

        /// <summary>Rotates a planar vector by <paramref name="deg"/> around +Y — the same
        /// result as <c>Quaternion.Euler(0, deg, 0) * v</c>, without building a quaternion.</summary>
        private static Vector3 RotateY(Vector3 v, float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector3(v.x * c + v.z * s, 0f, -v.x * s + v.z * c);
        }

        private static Vector3 PlanarForward(Vector3 forward)
        {
            var flat = new Vector3(forward.x, 0f, forward.z);
            return flat.sqrMagnitude < 0.0001f ? Vector3.forward : flat.normalized;
        }
    }

    /// <summary>
    /// Beat 1 of the feedback system: the hold-to-aim ground preview (FEEDBACK.md §2.1)
    /// and the target classification that feeds <see cref="TargetHighlight"/> (§2.2).
    /// <b>Press always previews (Phase 6, A2):</b> the preview for the LOCAL player starts on the
    /// press frame, driven by the local input provider's held bit, not by the networked
    /// <c>AbilityController.ChargingSlot</c> (which only goes live after the 125 ms hold threshold and
    /// still drives the rival-visible wind-up glow and the aim-rotate movement lock — unchanged).
    /// It draws the ability's *true* aim shape in its <c>AccentColor</c>; with nobody in the shape it
    /// is a cream DASHED outline (a legal aim, not an error); it washes to
    /// <see cref="FeedbackTuning.IllegalCastTintColor"/> only for a real refusal (stunned, cooling,
    /// another ability running — see <see cref="AbilityPreviewRules.IsRealRefusal"/>).
    /// </summary>
    /// <remarks>
    /// <b>Local-caster only, by design (§1.6).</b> This component does nothing unless the
    /// chicken it sits on has input authority on this peer — opponents must see only the
    /// wind-up tell (<see cref="ControlStateVFX"/>'s foot glow), never the area, or the
    /// arena becomes a solved puzzle. Because at most one chicken per peer is the local
    /// player, the live instance is published as a single static
    /// <see cref="Active"/> so every <see cref="TargetHighlight"/> can ask "am I marked?"
    /// instead of each chicken independently re-scanning the ability.
    ///
    /// Purely local VFX, observed from replicated state in <c>LateUpdate</c> — no RPCs
    /// and no new networked properties, consistent with <see cref="AbilityRangeIndicator"/>
    /// and <see cref="ControlStateVFX"/>.
    ///
    /// <b>Maestro:</b> add to the Chicken prefab; no Inspector wiring needed (the lines
    /// are built procedurally in <c>Awake</c>).
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class AbilityTelegraph : MonoBehaviour
    {
        private const string Source = "AbilityTelegraph";

        /// <summary>Outline width — matches <c>AbilityRangeIndicator</c>'s cast-flash weight
        /// (<see cref="FeedbackTuning.TelegraphShapeLineWidth"/>, 0.11) rather than its dimmer persistent
        /// ring, because during a hold the preview *is* the primary ground element.</summary>
        private const float ShapeLineWidth = FeedbackTuning.TelegraphShapeLineWidth;

        /// <summary>The offset stalk is deliberately thinner than the outline: it is a
        /// connector, not part of the area itself.</summary>
        private const float StalkLineWidth = 0.05f;

        /// <summary>Most chickens a preview can mark at once. The arena is 4-player FFA
        /// (<c>ChickenController.ActiveControllers</c> holds at most 4), so 8 is generous
        /// headroom that still lets <see cref="TryClassify"/> stay a fixed-size linear scan.</summary>
        private const int MaxMarks = 8;

        /// <summary>
        /// The local player's currently-charging telegraph, or null when nothing is being
        /// aimed on this peer. Set the frame a charge is first observed, cleared the frame
        /// it ends (and on disable/destroy). Read-only to everyone else — only
        /// <see cref="TargetHighlight"/> is expected to consume it.
        /// </summary>
        public static AbilityTelegraph Active { get; private set; }

        /// <summary>The ability being aimed right now, or null when <see cref="Active"/>
        /// is not this instance. Never cached across frames by callers.</summary>
        public AbilityBaseSO ChargingAbility { get; private set; }

        /// <summary>
        /// The preview's current colour: the charging ability's <c>AccentColor</c>, already
        /// lerped toward <see cref="FeedbackTuning.IllegalCastTintColor"/> by however far
        /// the illegal-cast wash has progressed (§2.5). <see cref="TargetHighlight"/> tints
        /// its *valid*-target brackets with this so brackets and ground shape desaturate
        /// together; "no effect" brackets keep their own neutral grey, which is a different
        /// story and must not collide with the illegal wash.
        /// </summary>
        public Color PreviewColor { get; private set; } = Color.white;

        [Tooltip("Art/UI/Fx/Fx_DashedRing.png — the cream dashed strip tiled along the no-target preview " +
                 "outline. Wired on Chicken.prefab; if it is missing the preview falls back to a solid " +
                 "cream line and logs an error once.")]
        [SerializeField] private Texture2D _dashedRingTexture;

        private ChickenController _controller;
        private AbilityController _abilities;
        private ILogService _log;
        private IInputProvider _input;

        private Material _solidMat;
        private Material _dashedMat;
        private bool _dashedApplied;      // the shape line currently wears the dashed material
        private bool _missingTextureLogged;

        // Scratch for AbilityPreviewRules.SelectLocalPreviewSlot — instance arrays, no per-frame allocation.
        private readonly bool[] _heldScratch  = new bool[AbilityController.SlotCount];
        private readonly bool[] _availScratch = new bool[AbilityController.SlotCount];

        private LineRenderer _shapeLine;
        private LineRenderer _stalkLine;

        // Phase 7 B1: the held move reads on grass. A near-opaque ink line sits under the outline (solid, dashed or grey) and a
        // cream fill (~25%) covers the shape. Both follow the outline's own points every frame.
        private LineRenderer _inkLine;
        private MeshFilter _fillFilter;
        private MeshRenderer _fillRenderer;
        private Mesh _fillMesh;
        private Vector3[] _fillVerts;
        private int[] _fillTris;
        private Color[] _fillColors;

        private Vector3[] _shapeBuf;                        // grown only on shape change
        private readonly Vector3[] _stalkBuf = new Vector3[2];

        // Charge observation.
        private byte  _observedSlot;      // 1-based encoding, mirrors AbilityController.ChargingSlot
        private float _illegal01;         // 0 = fully accent, 1 = fully illegal-washed
        private bool  _noTargetPreview;   // nothing in the shape yet: draw the cream dashed outline
        private bool  _cancelArmed;       // touch edge-band cancel armed: releasing now cancels (grey + dashed)

        // 10 Hz target classification (§10) — the geometry still redraws every frame.
        private float _pollTimer;
        private readonly ChickenController[] _marked     = new ChickenController[MaxMarks];
        private readonly TargetMark[]        _markKinds  = new TargetMark[MaxMarks];
        private int _markedCount;

        /// <summary>Buffer for the one <see cref="AbilityBaseSO.GatherTargets"/> call each
        /// poll makes. An instance field, sized to the 4-chicken arena so a poll never
        /// allocates — and deliberately NOT <c>AbilityBaseSO._scratch</c>, which is the
        /// protected buffer ability subclasses consume inside their own <c>OnActivate</c>.
        /// Sharing it would let a telegraph poll clobber a cast mid-resolution.</summary>
        private readonly System.Collections.Generic.List<ChickenController> _gathered = new(4);

        [Inject]
        public void Construct(IInputProvider input, ILogService log)
        {
            _input = input;
            _log   = log;
        }

        private void Awake()
        {
            // Plain MonoBehaviour spawned inside a Fusion prefab: Zenject never injects it, so
            // self-inject (CONVENTIONS "Self-injection"). Never gate on ProjectContext.HasInstance.
            if (_input == null) ProjectContext.Instance.Container.Inject(this);

            _controller = GetComponent<ChickenController>();
            _abilities  = GetComponent<AbilityController>();

            _solidMat = TelegraphShapes.SharedSolidLineMaterial(_log);   // shared, never destroyed per instance
            _shapeLine = TelegraphShapes.BuildLine(transform, "AbilityTelegraphShape", _solidMat, ShapeLineWidth, loop: true);
            _stalkLine = TelegraphShapes.BuildLine(transform, "AbilityTelegraphStalk", _solidMat, StalkLineWidth, loop: false);
            _shapeLine.enabled = false;
            _stalkLine.enabled = false;
            BuildUnderlays();

            if (_dashedRingTexture != null)
            {
                _dashedMat = TelegraphShapes.BuildLineMaterial(_log);   // per instance (texture scale is set below): destroyed in OnDestroy
                if (_dashedMat != null)
                {
                    _dashedMat.mainTexture = _dashedRingTexture;
                    // LineTextureMode.Tile spans ONE texture repeat over (line width / scale.x) world units, so the
                    // scale that gives N repeats per world unit is width * N.
                    _dashedMat.mainTextureScale = new Vector2(
                        FeedbackTuning.NoTargetPreviewLineWidth * FeedbackTuning.NoTargetPreviewRepeatsPerUnit, 1f);
                }
            }
        }

        /// <summary>Builds the ink under-stroke and the cream fill. Both use the same unlit transparent material as the
        /// outline and sort under it (fill below ink below the coloured line).</summary>
        private void BuildUnderlays()
        {
            _inkLine = TelegraphShapes.BuildLine(transform, "AbilityTelegraphInk", _solidMat, ShapeLineWidth, loop: true);
            _inkLine.sortingOrder = -2;
            _inkLine.startColor = _inkLine.endColor = FeedbackTuning.PreviewInkUnderStrokeColor;
            _inkLine.enabled = false;

            var go = new GameObject("AbilityTelegraphFill");
            go.transform.SetParent(transform, worldPositionStays: false);
            _fillFilter = go.AddComponent<MeshFilter>();
            _fillRenderer = go.AddComponent<MeshRenderer>();
            _fillRenderer.sharedMaterial = _solidMat;
            _fillRenderer.sortingOrder = -3;
            _fillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _fillRenderer.receiveShadows = false;
            _fillMesh = new Mesh { name = "AbilityTelegraphFill" };
            _fillMesh.MarkDynamic();
            _fillFilter.sharedMesh = _fillMesh;
            _fillRenderer.enabled = false;
        }

        /// <summary>Copies the outline into the ink under-stroke and rebuilds the cream fan fill (the shapes are convex
        /// outlines, so a fan from their centroid covers them exactly).</summary>
        private void UpdateUnderlays(Vector3[] outline, bool dashed)
        {
            if (_inkLine == null || outline == null) return;

            float strokeWidth = dashed
                ? FeedbackTuning.NoTargetPreviewLineWidth * FeedbackTuning.NoTargetDashHeightFraction
                : ShapeLineWidth;
            _inkLine.widthMultiplier = strokeWidth + FeedbackTuning.PreviewInkUnderStrokeMargin;
            if (_inkLine.positionCount != outline.Length) _inkLine.positionCount = outline.Length;
            if (!_inkLine.loop) _inkLine.loop = true;
            _inkLine.SetPositions(outline);
            if (!_inkLine.enabled) _inkLine.enabled = true;

            int n = outline.Length;
            if (_fillVerts == null || _fillVerts.Length != n + 1)
            {
                _fillVerts = new Vector3[n + 1];
                _fillColors = new Color[n + 1];
                _fillTris = new int[n * 3];
                for (int i = 0; i < n; i++)
                {
                    _fillTris[i * 3]     = 0;
                    _fillTris[i * 3 + 1] = 1 + i;
                    _fillTris[i * 3 + 2] = 1 + (i + 1) % n;
                }
                _fillMesh.Clear();
                _fillMesh.vertices = _fillVerts;
                _fillMesh.triangles = _fillTris;
            }

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < n; i++) centroid += outline[i];
            centroid /= n;

            _fillVerts[0] = transform.InverseTransformPoint(centroid);
            for (int i = 0; i < n; i++) _fillVerts[1 + i] = transform.InverseTransformPoint(outline[i]);

            var fill = FeedbackTuning.HeldFillColor;
            fill.a = _cancelArmed ? FeedbackTuning.HeldFillAlphaCancelArmed : FeedbackTuning.HeldFillAlpha;
            for (int i = 0; i <= n; i++) _fillColors[i] = fill;

            _fillMesh.vertices = _fillVerts;
            _fillMesh.colors = _fillColors;
            _fillMesh.RecalculateBounds();
            if (!_fillRenderer.enabled) _fillRenderer.enabled = true;
        }

        private void OnDisable() => Deactivate();

        private void OnDestroy()
        {
            Deactivate();
            if (_fillMesh != null) Destroy(_fillMesh);
            if (_dashedMat != null) Destroy(_dashedMat);
        }

        /// <summary>
        /// Clears the static handle between Editor play sessions. Needed because "Enter
        /// Play Mode Options" can suppress the domain reload that would otherwise reset it,
        /// leaving <see cref="Active"/> pointing at a destroyed chicken from the last run.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;

        /// <summary>
        /// The slot this chicken's preview is showing right now, or -1: the LOCAL held slot from the press
        /// frame, else the networked charge. Computed fresh (not cached from <c>LateUpdate</c>) so
        /// <see cref="AbilitySlotOverlay"/>, whose own <c>LateUpdate</c> may run first, never lags a frame
        /// behind and double-draws the slot being aimed.
        /// </summary>
        public int LocalAimedSlot => TryGetCharge(out _, out int slot) ? slot : AbilityController.InvalidSlot;

        private void LateUpdate()
        {
            if (!TryGetCharge(out var ability, out int slot))
            {
                Deactivate();
                return;
            }

            Active          = this;
            ChargingAbility = ability;

            TelegraphShapes.Resolve(ability, out var shape, out float radius,
                                    out float forwardOffset, out float coneAngle);

            ResolveLocalAim(ability);
            UpdateIllegalWash(ability, slot);
            UpdateGeometry(shape, radius, forwardOffset, coneAngle, isSelfRing: shape == AbilityAimShape.None);
            UpdateMarks(ability);
        }

        // Phase 6 chunk 5 (A7): the aim the local player is holding right now, from the input provider (mouse cursor /
        // right stick), snapped to the same byte the state authority will receive so the preview is exactly the cast.
        // Zero = no aim, draw along the bird's heading. Local only; nothing here is networked.
        private Vector3 _aimDirection;

        // Phase 7 B3: the direction the release will use (explicit aim, else the touch / pad soft-lock pick, else the
        // facing), from the same function the state authority calls at fire time. The shape, the marks and the pip use it.
        private Vector3 _previewForward;
        private bool _previewAimed;
        private readonly Vector2[] _softLockScratch = new Vector2[8];

        private void ResolveLocalAim(AbilityBaseSO ability)
        {
            _aimDirection = Vector3.zero;
            Vector2 explicitAim = ResolveExplicitAim(ability);
            if (explicitAim != Vector2.zero) _aimDirection = new Vector3(explicitAim.x, 0f, explicitAim.y);

            _previewForward = ResolvePreviewForward(ability, explicitAim, out _previewAimed);
        }

        /// <summary>The mouse cursor / right stick aim for <paramref name="ability"/> in world XZ, snapped to the byte the
        /// state authority receives; zero when the device gives none or the move does not use aim.</summary>
        private Vector2 ResolveExplicitAim(AbilityBaseSO ability)
        {
            if (_input == null || ability == null || !ability.UsesAim) return Vector2.zero;

            var pos = _controller.transform.position;
            float yaw = MatchCamera.Instance != null ? MatchCamera.Instance.CurrentYaw : 0f;
            return AimQuantizer.Snap(AimRules.Resolve(_input.GetAim(pos.y), pos, yaw));
        }

        private Vector3 ResolvePreviewForward(AbilityBaseSO ability, Vector2 explicitAim, out bool aimed)
        {
            float half = _input != null ? AimSoftLock.HalfAngleFor(_input.Device) : 0f;
            return AbilityAimForward.Resolve(ability, _controller, explicitAim, half, _softLockScratch, out aimed);
        }

        /// <summary>
        /// The forward the local player's release would use for <paramref name="ability"/> right now. Used by
        /// <see cref="AbilitySlotOverlay"/> so the in-range pip asks the same question the preview and the cast do.
        /// </summary>
        public Vector3 FireForward(AbilityBaseSO ability)
        {
            if (ability == null) return _controller != null ? _controller.transform.forward : Vector3.forward;
            return ResolvePreviewForward(ability, ResolveExplicitAim(ability), out _);
        }

        /// <summary>
        /// Is this chicken the local player AND currently charging something drawable?
        /// Every failure mode (no components, NetworkObject not spawned yet, remote peer,
        /// idle, empty slot) collapses to a single "no" so <see cref="LateUpdate"/> has one
        /// exit path.
        /// </summary>
        private bool TryGetCharge(out AbilityBaseSO ability, out int slot)
        {
            ability = null;
            slot    = AbilityController.InvalidSlot;

            if (_controller == null || _abilities == null) return false;

            // Reading [Networked] properties before Spawned / after Despawned is not safe.
            var obj = _abilities.Object;
            if (obj == null || !obj.IsValid) return false;

            // §1.6: the area is caster-private. Opponents get the wind-up tell only.
            if (!_controller.HasInputAuthority) return false;

            // Press = preview, from frame 0, for the local player: the held bit is the local input
            // provider's own state, so it does not wait for the 125 ms networked ChargingSlot.
            int local = SelectLocalPreviewSlot();
            if (local != AbilityPreviewRules.NoSlot)
            {
                ability = _abilities.GetSlot(local);
                if (ability == null) return false;
                slot = local;
                return true;
            }

            // Fallback: a charge the state authority is still holding (e.g. the release has not
            // reached a tick yet) keeps drawing until it fires or cancels, exactly as before.
            byte encoded = _abilities.ChargingSlot;
            if (encoded == 0) return false;

            ability = _abilities.ChargingAbility;
            if (ability == null) return false;

            slot = encoded - 1;
            return true;
        }

        /// <summary>Lowest held slot, if it is available (equipped + off cooldown) — the state machine's
        /// "lowest slot wins" rule, see <see cref="AbilityPreviewRules.SelectLocalPreviewSlot"/>.</summary>
        private int SelectLocalPreviewSlot()
        {
            if (_input == null) return AbilityPreviewRules.NoSlot;

            // "Quick Moves" fires on the press tick: there is no hold to aim, so no preview.
            if (CluckWars.Settings.PlayerPreferences.QuickMovesEnabled) return AbilityPreviewRules.NoSlot;

            for (int i = 0; i < _heldScratch.Length; i++)
            {
                // A slot that was cancelled / switched away from (key still down) or is re-arming after a fizzle
                // is not a live hold, so it must not draw an aim the state machine has already dropped.
                _heldScratch[i]  = _input.GetAbilityHeld(i) && !_abilities.IsHoldSuppressed(i);
                _availScratch[i] = _abilities.IsReady(i) && !_abilities.IsRearming(i) && !_abilities.IsSlotActive(i);
            }
            return AbilityPreviewRules.SelectLocalPreviewSlot(_heldScratch, _availScratch);
        }

        private void Deactivate()
        {
            if (Active == this) Active = null;
            ChargingAbility = null;
            _aimDirection = Vector3.zero;
            _previewAimed = false;

            _observedSlot = 0;
            _illegal01    = 0f;
            _cancelArmed  = false;
            _markedCount  = 0;
            _pollTimer    = 0f;
            for (int i = 0; i < MaxMarks; i++) _marked[i] = null;
            _gathered.Clear(); // don't keep despawned chickens referenced between holds

            if (_shapeLine != null && _shapeLine.enabled) _shapeLine.enabled = false;
            if (_stalkLine != null && _stalkLine.enabled) _stalkLine.enabled = false;
            if (_inkLine != null && _inkLine.enabled) _inkLine.enabled = false;
            if (_fillRenderer != null && _fillRenderer.enabled) _fillRenderer.enabled = false;
            ApplyLineStyle(dashed: false);
        }

        /// <summary>
        /// §2.5 live legality. <c>EvaluateRefusal</c> is the same precedence table
        /// <c>TryActivate</c> gates on. Only REAL refusals wash the preview to the illegal tint —
        /// the caster gets stunned, the slot is cooling, another ability starts
        /// (<see cref="AbilityPreviewRules.IsRealRefusal"/>). <c>NoTarget</c> is NOT one of them: a
        /// shape with nobody in it is a legal aim, drawn as the dashed no-target outline
        /// (<see cref="UpdateGeometry"/>), and it flips back to the accent line the moment a rival
        /// walks in.
        /// </summary>
        private void UpdateIllegalWash(AbilityBaseSO ability, int slot)
        {
            bool freshCharge = _observedSlot != (byte)(slot + 1);
            if (freshCharge)
            {
                _observedSlot = (byte)(slot + 1);
                _illegal01    = 0f;
                _pollTimer    = 0f; // classify immediately on the first frame of a new hold
                _markedCount  = 0;
            }

            var refusal = _abilities.EvaluateRefusal(slot);
            bool illegal = AbilityPreviewRules.IsRealRefusal(refusal);
            _noTargetPreview = AbilityPreviewRules.IsNoTargetPreview(refusal);
            float step = FeedbackTuning.PreviewIllegalDesaturateSeconds > 0f
                ? Time.deltaTime / FeedbackTuning.PreviewIllegalDesaturateSeconds
                : 1f;
            _illegal01 = Mathf.MoveTowards(_illegal01, illegal ? 1f : 0f, step);

            Color accent = ability.AccentColor;
            accent.a = FeedbackTuning.TelegraphPreviewAlpha;
            PreviewColor = Color.Lerp(accent, FeedbackTuning.IllegalCastTintColor, _illegal01);

            // Edge-band cancel armed (touch): the preview greys out and goes dashed, so "let go to cancel" reads
            // without relying on colour. Level-triggered from the input provider; no networked state.
            _cancelArmed = _input != null && _input.IsAbilityCancelArmed();
            if (_cancelArmed) PreviewColor = FeedbackTuning.CancelArmedPreviewColor;
        }

        private void UpdateGeometry(AbilityAimShape shape, float radius, float forwardOffset,
                                    float coneAngle, bool isSelfRing)
        {
            Vector3 pos = _controller.transform.position;
            // A directional shape draws along the forward the release will use (explicit aim, soft-lock pick, heading).
            Vector3 fwd = _previewForward;

            Color c = PreviewColor;
            if (isSelfRing)
            {
                // §2.2: an ability with no area marks nobody — it pulses the caster's own
                // ring instead. Pulsed (not static) so a self-buff still reads as "armed",
                // at the same rate as the valid-target brackets so the whole telegraph beat
                // beats in time.
                float phase = Mathf.Sin(Time.time * FeedbackTuning.ValidTargetPulseHz * Mathf.PI * 2f) * 0.5f + 0.5f;
                c.a *= Mathf.Lerp(0.6f, 1f, phase);
            }
            // No target yet: the cream dashed ribbon (texture carries cream + ink edge; tint is white).
            // Falls back to a solid cream line if the texture is not wired, and says so once.
            bool wantDashed = _noTargetPreview || _cancelArmed;
            bool dashed = wantDashed && _dashedMat != null;
            if (wantDashed && _dashedMat == null)
            {
                c = _cancelArmed ? FeedbackTuning.CancelArmedPreviewColor : FeedbackTuning.NoTargetPreviewStalkColor;
                if (!_missingTextureLogged)
                {
                    _missingTextureLogged = true;
                    _log?.Error(Source, "AbilityTelegraph._dashedRingTexture is not wired on Chicken.prefab; " +
                                        "the no-target preview falls back to a solid cream line.");
                }
            }
            else if (dashed)
            {
                // The dash texture carries cream + ink; a grey tint over it reads as the armed (grey) outline.
                c = _cancelArmed ? FeedbackTuning.CancelArmedPreviewColor : FeedbackTuning.NoTargetPreviewTint;
            }
            ApplyLineStyle(dashed);
            _shapeLine.startColor = _shapeLine.endColor = c;

            TelegraphShapes.Apply(_shapeLine, ref _shapeBuf, shape, pos, fwd, radius, forwardOffset, coneAngle);
            if (!_shapeLine.enabled)
            {
                _shapeLine.enabled = true;
                CluckWars.Diagnostics.FirstUseMarkers.Note(CluckWars.Diagnostics.FirstUseKind.Telegraph);   // breadcrumb only
            }
            UpdateUnderlays(_shapeBuf, dashed);

            bool stalk = TelegraphShapes.NeedsStalk(shape, forwardOffset);
            if (stalk)
            {
                _stalkLine.startColor = _stalkLine.endColor =
                    _cancelArmed ? FeedbackTuning.CancelArmedPreviewColor
                    : _noTargetPreview ? FeedbackTuning.NoTargetPreviewStalkColor : PreviewColor;
                TelegraphShapes.ApplyStalk(_stalkLine, _stalkBuf, pos,
                    AbilityAim.ShapeCenter(shape, pos, fwd, forwardOffset));
                if (!_stalkLine.enabled) _stalkLine.enabled = true;
            }
            else if (_stalkLine.enabled)
            {
                _stalkLine.enabled = false;
            }
        }

        /// <summary>Switches the shape line between the solid accent outline and the dashed ribbon. Only touches
        /// the renderer when the state actually changes.</summary>
        private void ApplyLineStyle(bool dashed)
        {
            if (_shapeLine == null || dashed == _dashedApplied) return;
            _dashedApplied = dashed;

            if (dashed)
            {
                _shapeLine.sharedMaterial = _dashedMat;
                _shapeLine.textureMode    = LineTextureMode.Tile;
                _shapeLine.widthMultiplier = FeedbackTuning.NoTargetPreviewLineWidth;
            }
            else
            {
                _shapeLine.sharedMaterial = _solidMat;
                _shapeLine.textureMode    = LineTextureMode.Stretch;
                _shapeLine.widthMultiplier = ShapeLineWidth;
            }
        }

        /// <summary>
        /// Re-classifies every rival against the charging ability at
        /// <see cref="FeedbackTuning.TargetScanPollHz"/> (§10). Deliberately rate-limited
        /// while the geometry above stays per-frame: a target crossing the boundary reads
        /// as instant at 10 Hz, but the shape must track the caster's own movement and
        /// rotation smoothly or aiming feels broken.
        /// </summary>
        /// <remarks>
        /// <b>One gather, then a membership test.</b> This used to classify each candidate
        /// independently as <c>IsInAimShape</c> + <c>WouldAffect</c>, which is per-candidate
        /// <i>eligibility</i> — so a single-target ability lit up every carrier in range and
        /// then robbed one of them. Running <see cref="AbilityBaseSO.GatherTargets"/> once
        /// and marking Valid iff the candidate came back in the buffer makes the preview
        /// literally consume the scan <c>OnActivate</c> consumes, which is what
        /// <c>GatherTargets</c>' own contract claims. It fixes every shape at once rather
        /// than special-casing <c>SingleTarget</c> here, and any future selection rule added
        /// to <c>GatherTargets</c> is picked up by the preview for free.
        /// </remarks>
        private void UpdateMarks(AbilityBaseSO ability)
        {
            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = FeedbackTuning.TargetScanPollHz > 0f ? 1f / FeedbackTuning.TargetScanPollHz : 0.1f;

            // Rotating shapes are scanned along the resolved forward (incl. the soft-lock pick); a single-target pick by
            // angle only ever uses the explicit aim, exactly as the cast does.
            ability.GatherTargets(_controller, _gathered, _previewAimed ? _previewForward : _aimDirection);

            _markedCount = 0;
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count && _markedCount < MaxMarks; i++)
            {
                var candidate = all[i];
                if (candidate == null) continue;

                // §2.2 "Self — never marked". The caster's own feedback is the self-ring
                // above, not a threat bracket on their own body.
                if (candidate == _controller) continue;

                // Still the geometric gate: a chicken outside the shape gets no bracket at
                // all, which is the difference between "no mark" and the grey "no effect" one.
                if (!ability.IsInAimShape(_controller, candidate, AimedForward())) continue;

                // Selected by the scan AND the effect survives arrival. The second half is
                // the only thing GatherTargets cannot answer: a control-immune rival
                // (Immovable) is deliberately still gathered and still counted — dropping it
                // from the scan would change whether the ability is usable at all and whether
                // the cast styles as a whiff — so without this the telegraph drew the accent
                // "will be hit" bracket over a chicken the cast was about to bounce off.
                // See AbilityBaseSO.IsEffectNullified for the eligible-vs-affected split.
                bool willLand = WillBeHit(candidate) && !ability.IsEffectNullified(_controller, candidate);

                _marked[_markedCount]    = candidate;
                _markKinds[_markedCount] = willLand ? TargetMark.Valid : TargetMark.NoEffect;
                _markedCount++;
            }

            for (int i = _markedCount; i < MaxMarks; i++) _marked[i] = null;
        }

        private Vector3 AimedForward() => _previewForward;

        /// <summary>Did this poll's <see cref="AbilityBaseSO.GatherTargets"/> actually select
        /// <paramref name="candidate"/>? A linear scan over at most 4 entries — no
        /// <c>List.Contains</c>, so the identity comparison stays explicit.</summary>
        private bool WillBeHit(ChickenController candidate)
        {
            for (int i = 0; i < _gathered.Count; i++)
                if (ReferenceEquals(_gathered[i], candidate)) return true;
            return false;
        }

        /// <summary>
        /// Is <paramref name="candidate"/> inside this live preview, and how? Returns false
        /// (with <paramref name="mark"/> = <see cref="TargetMark.None"/>) when it isn't.
        /// Reads the cached 10 Hz classification — cheap enough to call every frame from
        /// every chicken, and guarantees all brackets agree with the ground shape they
        /// belong to instead of each re-deriving it at a slightly different moment.
        /// </summary>
        public bool TryClassify(ChickenController candidate, out TargetMark mark)
        {
            mark = TargetMark.None;
            if (candidate == null || Active != this) return false;

            for (int i = 0; i < _markedCount; i++)
            {
                if (_marked[i] == candidate)
                {
                    mark = _markKinds[i];
                    return mark != TargetMark.None;
                }
            }
            return false;
        }
    }
}
