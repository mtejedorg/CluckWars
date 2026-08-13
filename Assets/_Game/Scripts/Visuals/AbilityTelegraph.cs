using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

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
        /// <summary>Inside the shape AND <c>WouldAffect</c> — this chicken will be hit.</summary>
        Valid = 1,
        /// <summary>Inside the shape but <c>WouldAffect</c> says no (immune, reflecting,
        /// already stunned, no cargo to steal). The gap between <c>IsInAimShape</c> and
        /// <c>WouldAffect</c>, exactly.</summary>
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
        public static Material BuildLineMaterial()
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            return sh != null ? new Material(sh) : null;
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
    /// While <c>AbilityController.ChargingSlot</c> is non-zero this draws the charging
    /// ability's *true* aim shape on the ground in its <c>AccentColor</c>, washing it to
    /// <see cref="FeedbackTuning.IllegalCastTintColor"/> if the cast goes illegal
    /// mid-hold (§2.5).
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
        /// <summary>Outline width — matches <c>AbilityRangeIndicator</c>'s cast-flash weight
        /// (0.11) rather than its dimmer persistent ring (0.06), because during a hold the
        /// preview *is* the primary ground element.</summary>
        private const float ShapeLineWidth = 0.11f;

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

        private ChickenController _controller;
        private AbilityController _abilities;

        private LineRenderer _shapeLine;
        private LineRenderer _stalkLine;

        private Vector3[] _shapeBuf;                        // grown only on shape change
        private readonly Vector3[] _stalkBuf = new Vector3[2];

        // Charge observation.
        private byte  _observedSlot;      // 1-based encoding, mirrors AbilityController.ChargingSlot
        private float _illegal01;         // 0 = fully accent, 1 = fully illegal-washed

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

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _abilities  = GetComponent<AbilityController>();

            var mat = TelegraphShapes.BuildLineMaterial();
            _shapeLine = TelegraphShapes.BuildLine(transform, "AbilityTelegraphShape", mat, ShapeLineWidth, loop: true);
            _stalkLine = TelegraphShapes.BuildLine(transform, "AbilityTelegraphStalk", mat, StalkLineWidth, loop: false);
            _shapeLine.enabled = false;
            _stalkLine.enabled = false;
        }

        private void OnDisable() => Deactivate();
        private void OnDestroy() => Deactivate();

        /// <summary>
        /// Clears the static handle between Editor play sessions. Needed because "Enter
        /// Play Mode Options" can suppress the domain reload that would otherwise reset it,
        /// leaving <see cref="Active"/> pointing at a destroyed chicken from the last run.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;

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

            UpdateIllegalWash(ability, slot);
            UpdateGeometry(shape, radius, forwardOffset, coneAngle, isSelfRing: shape == AbilityAimShape.None);
            UpdateMarks(ability);
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

            byte encoded = _abilities.ChargingSlot;
            if (encoded == 0) return false;

            ability = _abilities.ChargingAbility;
            if (ability == null) return false;

            slot = encoded - 1;
            return true;
        }

        private void Deactivate()
        {
            if (Active == this) Active = null;
            ChargingAbility = null;

            _observedSlot = 0;
            _illegal01    = 0f;
            _markedCount  = 0;
            _pollTimer    = 0f;
            for (int i = 0; i < MaxMarks; i++) _marked[i] = null;
            _gathered.Clear(); // don't keep despawned chickens referenced between holds

            if (_shapeLine != null && _shapeLine.enabled) _shapeLine.enabled = false;
            if (_stalkLine != null && _stalkLine.enabled) _stalkLine.enabled = false;
        }

        /// <summary>
        /// §2.5 live legality. <c>EvaluateRefusal</c> is the same precedence table
        /// <c>TryActivate</c> gates on, so the preview washes out for exactly the reasons a
        /// release would refuse — the caster gets stunned, another ability starts, the last
        /// valid target walks out of the shape.
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

            bool illegal = _abilities.EvaluateRefusal(slot) != AbilityRefusal.None;
            float step = FeedbackTuning.PreviewIllegalDesaturateSeconds > 0f
                ? Time.deltaTime / FeedbackTuning.PreviewIllegalDesaturateSeconds
                : 1f;
            _illegal01 = Mathf.MoveTowards(_illegal01, illegal ? 1f : 0f, step);

            Color accent = ability.AccentColor;
            accent.a = FeedbackTuning.TelegraphPreviewAlpha;
            PreviewColor = Color.Lerp(accent, FeedbackTuning.IllegalCastTintColor, _illegal01);
        }

        private void UpdateGeometry(AbilityAimShape shape, float radius, float forwardOffset,
                                    float coneAngle, bool isSelfRing)
        {
            Vector3 pos = _controller.transform.position;
            Vector3 fwd = _controller.transform.forward;

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
            _shapeLine.startColor = _shapeLine.endColor = c;

            TelegraphShapes.Apply(_shapeLine, ref _shapeBuf, shape, pos, fwd, radius, forwardOffset, coneAngle);
            if (!_shapeLine.enabled) _shapeLine.enabled = true;

            bool stalk = TelegraphShapes.NeedsStalk(shape, forwardOffset);
            if (stalk)
            {
                _stalkLine.startColor = _stalkLine.endColor = PreviewColor;
                TelegraphShapes.ApplyStalk(_stalkLine, _stalkBuf, pos,
                    AbilityAim.ShapeCenter(shape, pos, fwd, forwardOffset));
                if (!_stalkLine.enabled) _stalkLine.enabled = true;
            }
            else if (_stalkLine.enabled)
            {
                _stalkLine.enabled = false;
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

            ability.GatherTargets(_controller, _gathered);

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
                if (!ability.IsInAimShape(_controller, candidate)) continue;

                _marked[_markedCount]    = candidate;
                _markKinds[_markedCount] = WillBeHit(candidate) ? TargetMark.Valid : TargetMark.NoEffect;
                _markedCount++;
            }

            for (int i = _markedCount; i < MaxMarks; i++) _marked[i] = null;
        }

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
