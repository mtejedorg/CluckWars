using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Makes the four control states (GDD §6.4) visibly readable on every affected
    /// chicken — the single biggest "abilities do nothing visible" gap. A coloured
    /// ground ring marks the dominant active state, a one-shot white shockwave
    /// fires the instant a knockback lands, and a growing foot glow tells every peer
    /// that this chicken is charging something:
    ///
    /// <list type="bullet">
    ///   <item><b>Stunned</b> → pulsing yellow ring (on top of the existing stun orbit),
    ///   draining over the stun's remaining time (FEEDBACK.md §5.1, case 18).</item>
    ///   <item><b>Rooted</b> → green ring, likewise draining.</item>
    ///   <item><b>Slowed</b> → cyan ring, full and undrained — see
    ///   <see cref="UpdateStatusRing"/> for why slow deliberately has no arc.</item>
    ///   <item><b>Knocked back</b> → white shockwave burst at the moment of impact.</item>
    ///   <item><b>Charging</b> → wind-up glow at the feet in the charging ability's
    ///   accent colour, growing with hold time (FEEDBACK.md §2.4).</item>
    ///   <item><b>Buff active</b> → a second, wider ring at the caster's feet in the
    ///   active ability's accent colour, draining with its remaining duration
    ///   (FEEDBACK.md §5, case 30).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Driven entirely by <b>replicated</b> state so it's correct on every peer:
    /// <c>ChickenController.ControlFlags</c> (Slowed/Rooted/Stunned),
    /// <c>StunRemaining</c>/<c>RootRemaining</c> (both <c>[Networked] TickTimer</c>-backed),
    /// <c>KnockbackEventId</c> (the one-shot knockback event), the already-networked
    /// <c>IsStunned</c>, and <c>AbilityController.ChargingSlot</c>/<c>ActiveSlot</c>.
    /// The particles / LineRenderers / sprites themselves are 100% local — only those
    /// compact triggers cross the wire. Same LineRenderer pattern as
    /// <see cref="AbilityRangeIndicator"/>.
    ///
    /// <b>Information asymmetry (§1.6) is load-bearing here.</b> The wind-up glow is the
    /// <i>only</i> thing an opponent sees while someone aims: it says "they are charging
    /// something," never <i>what</i> or <i>where</i>. Its size is a fixed authored
    /// diameter scaled by hold time — deliberately not derived from the ability's
    /// <c>AimRadius</c>, which would leak the area that <see cref="AbilityTelegraph"/>
    /// keeps private to the caster. The case-30 buff ring is the same deal: it is a fixed
    /// <see cref="FeedbackTuning.SelfBuffRingRadius"/>, never the ability's reach.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ControlStateVFX : MonoBehaviour
    {
        private const int   Segments = 40;
        private const float GroundY  = 0.05f;
        private const float RingRadius = FeedbackTuning.SelfRingRadius;

        // Canonical control-state colours. Previously duplicated as local literals here;
        // FeedbackTuning is now the single source so future tuning has one place to happen.
        private static readonly Color StunColor  = FeedbackTuning.CanonicalStunColor;
        private static readonly Color RootColor  = FeedbackTuning.CanonicalRootColor;
        private static readonly Color SlowColor  = FeedbackTuning.CanonicalSlowColor;
        private static readonly Color KnockColor = FeedbackTuning.CanonicalKnockColor;

        [Tooltip("World diameter of the wind-up foot glow at full charge. This is the " +
                 "\"1.0 = full authored scale\" that FeedbackTuning.WindupGlowMaxScale refers to. " +
                 "Deliberately independent of the ability's aim radius — the tell must not leak the area.")]
        [Min(0.1f)] [SerializeField] private float _windupGlowDiameter = 1.35f;

        /// <summary>Peak alpha of the wind-up glow at full charge. Matches the status ring's
        /// own base pulse alpha (0.55, see <see cref="UpdateStatusRing"/>) so a charging
        /// chicken and a stunned one read at the same visual weight rather than one
        /// shouting over the other.</summary>
        private const float WindupGlowPeakAlpha = 0.55f;

        /// <summary>Alpha of the case-30 buff ring's draining arc. Steady, not pulsed: the
        /// drain itself is already the motion channel, and a second pulsing ring next to
        /// the status ring's 1.43 Hz pulse would just read as visual noise.</summary>
        private const float SelfBuffRingAlpha = 0.80f;

        private static Sprite _glowSprite;

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private AbilityController _abilities;

        private DrainRing    _statusRing;  // §5.1 status ring + depleting arc
        private DrainRing    _buffRing;    // §5 case 30 "my buff is expiring"
        private LineRenderer _knockFlash;  // one-shot knockback shockwave
        private SpriteRenderer _windupGlow; // §2.4 caster wind-up tell (all peers)

        private Vector3[] _dirs;
        private Vector3[] _flashBuf;

        private float _knockTimer = -1f;
        private const float KnockDuration = 0.3f;
        private byte  _lastKnockEventId;
        private bool  _knockInitialized;

        private float _windupTimer;
        private byte  _lastChargingSlot;

        // Observed-maximum duration trackers — see TrackRemaining.
        private float _stunObservedMax;
        private float _rootObservedMax;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _abilities  = GetComponent<AbilityController>();

            _dirs     = new Vector3[Segments];
            _flashBuf = new Vector3[Segments];
            for (int i = 0; i < Segments; i++)
            {
                float a = (i / (float)Segments) * Mathf.PI * 2f;
                _dirs[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }

            var mat = TelegraphShapes.BuildLineMaterial();
            _statusRing = new DrainRing(transform, "ControlStateRing", mat, trackWidth: 0.07f, arcWidth: 0.11f);
            _buffRing   = new DrainRing(transform, "AbilityBuffRing",  mat, trackWidth: 0.05f, arcWidth: 0.08f);

            _knockFlash = BuildRing("KnockbackFlash", mat, 0.12f);
            _knockFlash.enabled = false;

            _windupGlow = BuildWindupGlow();
        }

        private void LateUpdate()
        {
            if (_controller == null) return;
            UpdateStatusRing();
            UpdateBuffRing();
            UpdateKnockback();
            UpdateWindupGlow();
        }

        // ---- §5.1 status ring + depleting arc (cases 17 / 18 / 21) ------------

        /// <summary>
        /// Draws the dominant control state as a coloured ground ring, and — for the two
        /// states that actually have a deadline — a bright arc on top of it that drains
        /// over the remaining duration, so "how much longer" is readable from across the
        /// arena with no number at all (§5.1, cases 17/18/21).
        ///
        /// <para><b>Where the fraction comes from.</b> Only the *remaining* time is
        /// replicated (<c>StunRemaining</c> / <c>RootRemaining</c>, both backed by
        /// <c>[Networked] TickTimer</c>s); the original duration is not, and adding it
        /// would mean a new networked property this stage is explicitly not allowed to
        /// add. So each state tracks the <b>maximum remaining time observed since the
        /// state was entered</b> and drains against that — see
        /// <see cref="TrackRemaining"/>. On the frame the state begins, the observed
        /// maximum is the full duration (minus at most one tick of observation latency),
        /// so the arc starts full; a refresh that extends the timer pushes the maximum up
        /// and the arc visibly refills, which is the correct read. A peer that joins or
        /// first renders <i>mid</i>-stun starts its maximum at whatever is left, so its
        /// arc drains from full over the remainder — it under-states elapsed time rather
        /// than inventing a duration it cannot know.</para>
        ///
        /// <para><b>Why slow gets no arc.</b> <c>SlowMultiplier</c> is re-derived from
        /// scratch every tick out of whatever zones / auras / piles are touching the
        /// chicken right now (see the remarks on <c>ChickenController.SlowMultiplier</c>).
        /// It has no end time and therefore nothing to drain toward — two overlapping slow
        /// sources with different remaining durations would make any single countdown a
        /// lie. Slow keeps a full, undrained ring plus its §1.3 shape coding (trailing
        /// streaks, <see cref="ChickenStateOverlays"/>). This is a carried-forward design
        /// decision: do not "fix" it by inventing a slow deadline, there isn't one.</para>
        /// </summary>
        private void UpdateStatusRing()
        {
            // Both trackers run every frame regardless of which state currently *wins* the
            // ring, so a root that was masked by a stun doesn't restart its arc at full
            // the moment the stun lifts.
            bool stunDrains = TrackRemaining(_controller.StunRemaining, ref _stunObservedMax, out float stunFrac);
            bool rootDrains = TrackRemaining(_controller.RootRemaining, ref _rootObservedMax, out float rootFrac);

            // Priority: stun > root > slow (most-incapacitating wins the ring colour).
            // All three read replicated state, so the ring is correct on every peer.
            var flags    = _controller.ControlFlags;
            bool removed = _combat != null && _combat.IsRemoved;
            bool stunned = removed || (flags & ControlVfx.Stunned) != 0;
            bool rooted  = (flags & ControlVfx.Rooted) != 0;
            bool slowed  = (flags & ControlVfx.Slowed) != 0;

            Color c;
            float fraction;
            bool  drain;
            if (stunned)
            {
                // A *removed* chicken (ChickenCombat.IsRemoved) also paints the stun ring,
                // as it always has — but removal is on its own respawn clock, not the
                // control-stun timer, so there is no fraction to drain and it falls back
                // to a full ring.
                c = StunColor; drain = stunDrains; fraction = stunFrac;
            }
            else if (rooted)
            {
                c = RootColor; drain = rootDrains; fraction = rootFrac;
            }
            else if (slowed)
            {
                c = SlowColor; drain = false; fraction = 1f; // no deadline — see remarks
            }
            else
            {
                _statusRing.Hide();
                return;
            }

            // Gentle pulse so it reads as "active effect", not a static decal. Stays
            // per-frame: it is one Sin() and a colour write, not geometry (the arc's
            // vertices are the expensive part and those are gated to
            // FeedbackTuning.StatusArcDrainUpdateHz inside DrainRing).
            c.a = 0.55f + 0.25f * Mathf.Sin(Time.time * 9f);

            Vector3 center = _controller.transform.position;
            center.y = GroundY;
            _statusRing.Show(center, RingRadius, c, fraction, drain);
        }

        /// <summary>
        /// Feeds a state's replicated remaining-time into its observed-maximum tracker and
        /// produces a 0..1 drain fraction. Returns false (and resets the tracker) when the
        /// state is not running, so the next entry into that state starts from a clean
        /// maximum. See <see cref="UpdateStatusRing"/> for why an observed maximum is used
        /// instead of the real authored duration.
        /// </summary>
        private static bool TrackRemaining(float remaining, ref float observedMax, out float fraction01)
        {
            if (remaining <= 0f)
            {
                observedMax = 0f;
                fraction01  = 0f;
                return false;
            }

            if (remaining > observedMax) observedMax = remaining;
            fraction01 = observedMax > 0f ? Mathf.Clamp01(remaining / observedMax) : 1f;
            return true;
        }

        // ---- §5 case 30: "my buff is active and expiring" ---------------------

        /// <summary>
        /// Draws a draining ring at this chicken's feet in the *active* ability's accent
        /// colour for as long as that ability is running, so a self-buff (Speed Burst,
        /// Turtle Mode, Invisibility, …) has a visible expiry instead of just silently
        /// stopping. Reuses the exact same <see cref="DrainRing"/> machinery as the §5.1
        /// status arc — there is deliberately only one arc implementation in the project.
        ///
        /// Drawn on all peers, not just the caster: an opponent seeing a rival's Turtle
        /// Mode about to lapse is exactly the counterplay read §1.1's "third vantage
        /// point" asks for, and it leaks nothing the ability's own visible effect doesn't
        /// already give away. Radius is a fixed <see cref="FeedbackTuning.SelfBuffRingRadius"/>
        /// and never the ability's reach (§1.6).
        /// </summary>
        private void UpdateBuffRing()
        {
            if (_abilities == null || _controller.IsDecoy) { _buffRing.Hide(); return; }

            var obj = _abilities.Object;
            if (obj == null || !obj.IsValid) { _buffRing.Hide(); return; }
            if (_abilities.ActiveSlot == AbilityController.InvalidSlot) { _buffRing.Hide(); return; }

            var ability = _abilities.ActiveAbility;
            if (ability == null) { _buffRing.Hide(); return; }

            // ActiveRemaining01 already returns 0 for an ability with no meaningful
            // duration, which is the same "nothing to drain" case as a finished one.
            float fraction = _abilities.ActiveRemaining01;
            if (fraction <= 0f) { _buffRing.Hide(); return; }

            Color c = ability.AccentColor;
            c.a = SelfBuffRingAlpha;

            Vector3 center = _controller.transform.position;
            center.y = GroundY;
            _buffRing.Show(center, FeedbackTuning.SelfBuffRingRadius, c, fraction, drain: true);
        }

        private void UpdateKnockback()
        {
            // Replicated one-shot: a changed KnockbackEventId means a knockback was
            // applied (on any peer). Fire the shockwave once. Seed the baseline on the
            // first frame so a non-zero starting id (late join) doesn't false-trigger.
            byte id = _controller.KnockbackEventId;
            if (!_knockInitialized)
            {
                _knockInitialized = true;
                _lastKnockEventId = id;
            }
            else if (id != _lastKnockEventId)
            {
                _lastKnockEventId = id;
                _knockTimer = 0f;
            }

            if (_knockTimer < 0f) return;

            _knockTimer += Time.deltaTime;
            float t = _knockTimer / KnockDuration;
            if (t >= 1f)
            {
                _knockTimer = -1f;
                if (_knockFlash.enabled) _knockFlash.enabled = false;
                return;
            }

            float eased  = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(0.3f, 1.3f, eased);
            Color c = KnockColor; c.a = (1f - t) * 0.8f;
            _knockFlash.startColor = _knockFlash.endColor = c;
            WriteCircle(_knockFlash, _flashBuf, transform.position, radius);
            if (!_knockFlash.enabled) _knockFlash.enabled = true;
        }

        // ---- §2.4 caster wind-up tell (visible on ALL peers) ------------------

        /// <summary>
        /// Grows an emissive-reading glow at the caster's feet for as long as
        /// <c>AbilityController.ChargingSlot</c> stays non-zero, ramping
        /// <see cref="FeedbackTuning.WindupGlowMinScale"/> →
        /// <see cref="FeedbackTuning.WindupGlowMaxScale"/> over
        /// <see cref="FeedbackTuning.WindupGlowRampSeconds"/>.
        ///
        /// The ramp clock is a purely local timer started when this peer first *observes*
        /// the charge, not a replicated one — every peer therefore sees the tell begin at
        /// its own observation instant, which is what "local VFX derived from replicated
        /// state" means here. Holding does not charge power (§2), so a peer whose ramp is
        /// a few tens of milliseconds out of step with the caster's is showing a tell, not
        /// a wrong gameplay value.
        /// </summary>
        private void UpdateWindupGlow()
        {
            if (_windupGlow == null) return;

            byte charging = 0;
            if (_abilities != null)
            {
                var obj = _abilities.Object;
                if (obj != null && obj.IsValid) charging = _abilities.ChargingSlot;
            }

            if (charging == 0)
            {
                _lastChargingSlot = 0;
                _windupTimer = 0f;
                if (_windupGlow.gameObject.activeSelf) _windupGlow.gameObject.SetActive(false);
                return;
            }

            // Restart the ramp when a *different* slot starts charging, so swapping which
            // ability you're aiming re-tells the wind-up instead of inheriting a full glow.
            if (charging != _lastChargingSlot)
            {
                _lastChargingSlot = charging;
                _windupTimer = 0f;
            }
            _windupTimer += Time.deltaTime;

            float t = FeedbackTuning.WindupGlowRampSeconds > 0f
                ? Mathf.Clamp01(_windupTimer / FeedbackTuning.WindupGlowRampSeconds)
                : 1f;
            float scale01 = Mathf.Lerp(FeedbackTuning.WindupGlowMinScale, FeedbackTuning.WindupGlowMaxScale, t);

            var ability = _abilities.ChargingAbility;
            Color c = ability != null ? ability.AccentColor : Color.white;
            c.a = WindupGlowPeakAlpha * t;

            _windupGlow.color = c;
            _windupGlow.transform.localScale = Vector3.one * (_windupGlowDiameter * scale01);
            if (!_windupGlow.gameObject.activeSelf) _windupGlow.gameObject.SetActive(true);
        }

        // ---- Helpers (mirror AbilityRangeIndicator) ---------------------------

        /// <summary>Soft radial disc lying flat at the feet — the wind-up glow. Same flat
        /// ground-sprite construction <c>ChickenStateOverlays.BuildBlob</c> uses.</summary>
        private SpriteRenderer BuildWindupGlow()
        {
            var go = new GameObject("AbilityWindupGlow");
            go.transform.SetParent(transform, worldPositionStays: false);
            // Below the status ring's ribbon (0.05) and the target bracket (0.045) so the
            // glow reads as light on the floor rather than a competing decal.
            go.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale    = Vector3.one * (_windupGlowDiameter * FeedbackTuning.WindupGlowMinScale);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = GlowSprite();
            sr.sortingOrder = 1; // under ChickenStateOverlays' slow/root blobs (2/3)
            go.SetActive(false);
            return sr;
        }

        /// <summary>Soft radial falloff disc, generated once per process. Hotter in the
        /// centre than <c>ChickenStateOverlays.DiscSprite</c>'s solid-core blob so it reads
        /// as a glow rather than a flat patch of colour.</summary>
        private static Sprite GlowSprite()
        {
            if (_glowSprite != null) return _glowSprite;

            const int S = 96;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f;
                float dy = (y + 0.5f) / S - 0.5f;
                float d  = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / 0.5f); // 0 centre → 1 edge
                float a  = 1f - d;
                a *= a; // quadratic falloff: bright core, long soft tail
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            _glowSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
            return _glowSprite;
        }

        private void WriteCircle(LineRenderer lr, Vector3[] buf, Vector3 center, float radius)
        {
            center.y = GroundY;
            for (int i = 0; i < Segments; i++)
                buf[i] = center + _dirs[i] * radius;
            lr.SetPositions(buf);
        }

        private LineRenderer BuildRing(string goName, Material mat, float width)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, worldPositionStays: false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.loop              = true;
            lr.positionCount     = Segments;
            lr.widthMultiplier   = width;
            lr.numCapVertices    = 0;
            lr.numCornerVertices = 0;
            lr.alignment         = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.textureMode       = LineTextureMode.Stretch;
            if (mat != null) lr.material = mat;
            return lr;
        }
    }

    /// <summary>
    /// The project's <b>one and only</b> depleting-ring implementation: a dim full-circle
    /// "track" with a bright arc drawn on top of it covering only the fraction that is
    /// left. Three FEEDBACK.md cases share it — the §5.1 status arc (cases 17/18/21), the
    /// §5 case-30 caster buff ring, and the case-23 zone expiry ring — precisely so a
    /// draining circle means exactly one thing to the player no matter what drew it.
    /// </summary>
    /// <remarks>
    /// <b>Not a Component.</b> A plain owned object, constructed by whichever
    /// MonoBehaviour needs one; it builds and owns two child <c>LineRenderer</c>s and
    /// nothing else. Deliberately lives next to <see cref="ControlStateVFX"/> (the
    /// component that introduced the pattern) rather than in a file of its own.
    ///
    /// <b>Mobile budget (§10).</b> Two things happen every frame and both are trivial: a
    /// transform write (position + an identity rotation) and a <c>startColor</c>/
    /// <c>endColor</c> write. The actual <i>geometry</i> — the per-vertex work the budget
    /// cares about — is regenerated only when the radius changes or the
    /// <see cref="FeedbackTuning.StatusArcDrainUpdateHz"/> gate opens, and each ring's
    /// gate is randomly phase-offset at construction so four chickens never rebuild on the
    /// same frame.
    ///
    /// <b>Why local space.</b> The two LineRenderers use <c>useWorldSpace = false</c> and
    /// hang off a root transform that is repositioned each frame. That is what lets the
    /// geometry be cached across frames while still following a moving chicken. The root's
    /// rotation is forced to identity every frame — a chicken turns to face its movement
    /// direction, and inheriting that rotation would spin the arc's start point around the
    /// circle, which reads as the timer rotating rather than draining.
    /// </remarks>
    public sealed class DrainRing
    {
        /// <summary>Segments in the full-circle track. Matches <c>ControlStateVFX</c>'s
        /// long-standing ring resolution so the track is exactly as smooth as the ring it
        /// replaces.</summary>
        public const int TrackSegments = 40;

        /// <summary>
        /// Segments in the draining arc. The arc's point count is <b>constant</b>
        /// (<c>ArcSegments + 1</c>) at every fraction — the points are simply spread over a
        /// shorter sweep as it drains — so the LineRenderer's vertex count never changes
        /// and a short arc is if anything smoother than a long one. Keeping the count
        /// fixed also means <c>SetPositions</c> can take one pre-allocated buffer forever.
        /// </summary>
        public const int ArcSegments = 32;

        /// <summary>Below this remaining fraction the arc is hidden rather than drawn as a
        /// sub-segment nub. The track alone then reads as "essentially over".</summary>
        private const float MinVisibleFraction = 0.004f;

        /// <summary>Height the arc rides above the track, so the two ribbons never
        /// z-fight where they overlap.</summary>
        private const float ArcLift = 0.004f;

        private readonly Transform    _root;
        private readonly LineRenderer _track;
        private readonly LineRenderer _arc;
        private readonly Vector3[]    _trackBuf;
        private readonly Vector3[]    _arcBuf;

        private float   _builtTrackRadius = -1f;
        private float   _builtArcRadius   = -1f;
        private float   _builtFraction    = -1f;
        private float   _nextArcRebuild;
        private Vector3 _appliedParentScale = Vector3.zero;

        public DrainRing(Transform parent, string goName, Material mat, float trackWidth, float arcWidth)
        {
            var root = new GameObject(goName);
            root.transform.SetParent(parent, worldPositionStays: false);
            _root = root.transform;

            _track = BuildLine(_root, goName + "Track", mat, trackWidth, loop: true,  points: TrackSegments,   lift: 0f);
            _arc   = BuildLine(_root, goName + "Arc",   mat, arcWidth,   loop: false, points: ArcSegments + 1, lift: ArcLift);

            _trackBuf = new Vector3[TrackSegments];
            _arcBuf   = new Vector3[ArcSegments + 1];

            // Phase-offset this ring's geometry gate so a 4-chicken scrum spreads its arc
            // rebuilds across frames instead of spiking one.
            _nextArcRebuild = Time.time + Random.value / Mathf.Max(1f, FeedbackTuning.StatusArcDrainUpdateHz);

            Hide();
        }

        /// <summary>
        /// Draws the ring this frame.
        /// </summary>
        /// <param name="groundCenter">World-space centre, already at the desired ground height.</param>
        /// <param name="radius">Ring radius in world units.</param>
        /// <param name="color">Arc colour, alpha included. The track reuses it scaled by
        /// <see cref="FeedbackTuning.DrainRingTrackAlphaMultiplier"/>.</param>
        /// <param name="fraction01">Remaining fraction, 0..1. Ignored when
        /// <paramref name="drain"/> is false.</param>
        /// <param name="drain">False draws a plain full ring at full colour — the state has
        /// no deadline to count down to (slow), so showing an arc would be a lie.</param>
        public void Show(Vector3 groundCenter, float radius, Color color, float fraction01, bool drain)
        {
            if (_root == null) return;

            _root.position = groundCenter;
            _root.rotation = Quaternion.identity;
            NeutralizeParentScale();

            EnsureTrackGeometry(radius);

            if (!drain)
            {
                _track.startColor = _track.endColor = color;
                SetEnabled(_track, true);
                SetEnabled(_arc, false);
                return;
            }

            Color trackColor = color;
            trackColor.a *= FeedbackTuning.DrainRingTrackAlphaMultiplier;
            _track.startColor = _track.endColor = trackColor;
            SetEnabled(_track, true);

            float f = Mathf.Clamp01(fraction01);
            if (f < MinVisibleFraction)
            {
                SetEnabled(_arc, false);
                return;
            }

            _arc.startColor = _arc.endColor = color;
            EnsureArcGeometry(radius, f);
            SetEnabled(_arc, true);
        }

        public void Hide()
        {
            // Drop the built-fraction sentinel so the next Show rebuilds the arc on its
            // first frame instead of flashing up to 1/StatusArcDrainUpdateHz of the
            // *previous* state's remaining time before the gate next opens.
            _builtFraction = -1f;
            SetEnabled(_track, false);
            SetEnabled(_arc, false);
        }

        /// <summary>
        /// Cancels the owner's world scale so <c>radius</c> always means world units.
        /// <c>ChickenController</c> scales the whole chicken root by its class's
        /// <c>ChickenStatsSO.Scale</c>, and the local-space geometry would otherwise
        /// inherit it — a Fatty's status ring would be a different size from everyone
        /// else's, and would no longer match <see cref="FeedbackTuning.SelfRingRadius"/>,
        /// which the telegraph draws in true world units. Cached, because class scale is
        /// set once at spawn and never per frame.
        /// </summary>
        private void NeutralizeParentScale()
        {
            var parent = _root.parent;
            if (parent == null) return;

            Vector3 ps = parent.lossyScale;
            if (ps == _appliedParentScale) return;
            _appliedParentScale = ps;

            _root.localScale = new Vector3(
                Mathf.Approximately(ps.x, 0f) ? 1f : 1f / ps.x,
                Mathf.Approximately(ps.y, 0f) ? 1f : 1f / ps.y,
                Mathf.Approximately(ps.z, 0f) ? 1f : 1f / ps.z);
        }

        // ---- Geometry ---------------------------------------------------------

        private void EnsureTrackGeometry(float radius)
        {
            if (Mathf.Approximately(radius, _builtTrackRadius)) return;
            _builtTrackRadius = radius;

            for (int i = 0; i < TrackSegments; i++)
            {
                float a = (i / (float)TrackSegments) * Mathf.PI * 2f;
                _trackBuf[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
            _track.SetPositions(_trackBuf);
        }

        /// <summary>
        /// Rebuilds the arc, gated to <see cref="FeedbackTuning.StatusArcDrainUpdateHz"/>.
        /// A radius change bypasses the gate (it means the ring changed identity, not that
        /// time passed). The sweep starts at +Z ("12 o'clock" on the iso view) and runs
        /// clockwise, which is the direction a player already reads a clock face in.
        /// </summary>
        private void EnsureArcGeometry(float radius, float fraction01)
        {
            bool radiusChanged = !Mathf.Approximately(radius, _builtArcRadius);
            if (!radiusChanged && Time.time < _nextArcRebuild && _builtFraction >= 0f) return;

            _nextArcRebuild   = Time.time + 1f / Mathf.Max(1f, FeedbackTuning.StatusArcDrainUpdateHz);
            _builtArcRadius   = radius;
            _builtFraction    = fraction01;

            float sweep = fraction01 * Mathf.PI * 2f;
            for (int i = 0; i <= ArcSegments; i++)
            {
                float a = sweep * (i / (float)ArcSegments);
                _arcBuf[i] = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
            }
            _arc.SetPositions(_arcBuf);
        }

        private static void SetEnabled(LineRenderer lr, bool on)
        {
            if (lr != null && lr.enabled != on) lr.enabled = on;
        }

        /// <summary>Local-space ground LineRenderer. Mirrors
        /// <c>TelegraphShapes.BuildLine</c>'s settings, but local-space and with an
        /// explicit point count so the arc can hold a constant vertex budget.</summary>
        private static LineRenderer BuildLine(Transform parent, string goName, Material mat,
                                              float width, bool loop, int points, float lift)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, lift, 0f);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = false; // geometry is cached across frames; the root moves
            lr.loop              = loop;
            lr.positionCount     = points;
            lr.widthMultiplier   = width;
            lr.numCapVertices    = 0;
            lr.numCornerVertices = 0;
            lr.alignment         = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.textureMode       = LineTextureMode.Stretch;
            if (mat != null) lr.material = mat;
            return lr;
        }
    }
}
