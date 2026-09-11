using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The rule that decides whether a victim's impact is allowed to claim a
    /// <i>direction</i> — the §3.2 impact motion lines and the screen-edge bearing arc.
    /// Pure state machine over floats and positions, deliberately free of Fusion and
    /// MonoBehaviour types so the EditMode suite can exercise every branch without a live
    /// <c>NetworkRunner</c>; the same split <see cref="CluckWars.Gameplay.StealRules"/> and
    /// <see cref="CluckWars.Gameplay.BaseDepositRules"/> already use.
    /// </summary>
    /// <remarks>
    /// <b>Attribution is pushed by the cause, never pulled by proximity.</b> This type has
    /// exactly one way to learn who an attacker is: something that actually knows calls
    /// <see cref="RecordAttribution"/>. There is no fallback, no search, no nearest-rival
    /// guess — so "nobody told us" and "we could not work it out" are the same state, and
    /// that state declines.
    /// <para>
    /// This replaced a nearest-live-rival scan inside <c>HitFeedback</c> that treated the
    /// closest chicken within 12 m as the attacker with no evidence at all. At
    /// <c>MapGenerator.ArenaHalfSize</c> = 25.65 that circle is about 17% of the arena, so
    /// walking into a Feather Trap reliably drew a bearing arc at whichever rival happened to
    /// be standing nearest. In a 4-player free-for-all, manufactured blame is worse than no
    /// feedback: the victim turns on someone who did nothing, and the player who actually
    /// laid the trap is never even implicated.
    /// </para>
    /// <para>
    /// <b>The window exists because the two halves of one hit do not arrive together.</b>
    /// A victim learns it was hit from its own replicated state (a bumped
    /// <c>KnockbackEventId</c>, a control-state rising edge, a <c>Cargo</c> drop); the
    /// attacker is named from a different peer's replicated cast event, or from an RPC that
    /// lands in a different phase of the same frame. Either can be observed first, by a
    /// frame or three of jitter, so both orders have to work: an impact consumes a fresh
    /// attribution (<see cref="TryClaimAtImpact"/>), and a late attribution retro-claims an
    /// impact that already played without a direction (<see cref="RecordAttribution"/>).
    /// A <c>default</c> instance has a zero window, so nothing it is told survives even one
    /// observation — the correct failure mode for a type whose whole job is refusing to
    /// guess. (Freshness is inclusive at both ends, so a zero window would still admit two
    /// halves carrying an identical timestamp; that is unreachable when they are observed
    /// from different objects on different frames. <c>HitAttributionTests</c> pins both.)
    /// </para>
    /// </remarks>
    public struct HitAttribution
    {
        private readonly float _windowSeconds;

        private bool    _hasSource;
        private Vector3 _sourcePosition;
        private float   _sourceTime;

        /// <summary>An impact has played its flash and shake but has been given no direction
        /// yet, and is still young enough for a late attribution to claim.</summary>
        private bool  _impactPending;
        private float _impactTime;

        public HitAttribution(float windowSeconds)
        {
            _windowSeconds  = Mathf.Max(0f, windowSeconds);
            _hasSource      = false;
            _sourcePosition = default;
            _sourceTime     = 0f;
            _impactPending  = false;
            _impactTime     = 0f;
        }

        /// <summary>How long a named attacker and a directionless impact each stay claimable.</summary>
        public float WindowSeconds => _windowSeconds;

        /// <summary>
        /// A victim impact just happened at <paramref name="now"/>. Returns true when a
        /// still-fresh attribution names the attacker, in which case the caller may play the
        /// directional beat immediately.
        /// </summary>
        /// <remarks>
        /// A false return is a decision, not an error: the impact is remembered as pending so
        /// an attribution arriving inside the window can still claim it, and if none does the
        /// hit simply never gets a direction. The attribution is deliberately <i>not</i>
        /// consumed on a successful claim — one cast routinely lands a knockback and a stun in
        /// the same frame, and both impacts belong to the same attacker.
        /// </remarks>
        public bool TryClaimAtImpact(float now, out Vector3 attackerPosition)
        {
            if (IsFresh(_hasSource, _sourceTime, now))
            {
                attackerPosition = _sourcePosition;
                _impactPending   = false;
                return true;
            }

            attackerPosition = default;
            _hasSource       = false;   // a stale source must not serve a later impact
            _impactPending   = true;
            _impactTime      = now;
            return false;
        }

        /// <summary>
        /// Something that actually knows who the attacker was says so. Returns true when this
        /// arrives after an impact that is still waiting for a direction, in which case the
        /// caller should play the directional beat now.
        /// </summary>
        public bool RecordAttribution(Vector3 attackerPosition, float now, out Vector3 claimed)
        {
            _sourcePosition = attackerPosition;
            _sourceTime     = now;
            _hasSource      = true;

            claimed = attackerPosition;
            if (!_impactPending) return false;

            bool inWindow  = IsFresh(true, _impactTime, now);
            _impactPending = false;     // too old to claim is still resolved, just not claimed
            return inWindow;
        }

        /// <summary>
        /// Planar (XZ) bearing from <paramref name="attackerPosition"/> to
        /// <paramref name="victimPosition"/> — the direction the hit came <i>from</i>, which
        /// is what both the motion-line fan and the screen-edge arc encode.
        /// </summary>
        /// <remarks>
        /// Returns false when the two are closer together than
        /// <paramref name="minSeparation"/>. An attacker standing on top of its victim has no
        /// meaningful bearing, and normalising a near-zero vector would produce an arbitrary
        /// one — the same manufactured claim this whole type exists to prevent, arriving
        /// through the geometry instead of through the search.
        /// </remarks>
        public static bool TryBearing(
            Vector3 victimPosition, Vector3 attackerPosition, float minSeparation, out Vector3 direction)
        {
            float dx = victimPosition.x - attackerPosition.x;
            float dz = victimPosition.z - attackerPosition.z;
            float sqr = dx * dx + dz * dz;

            // The boundary is INCLUSIVE: a separation of exactly minSeparation still carries a
            // usable direction (see FeedbackTuning.HitAttributionMinSeparation — "below this"
            // is the noise floor, not "at this"). Comparing `sqr` against `min * min` raw does
            // not express that reliably: the two are built by different expressions, and the
            // JIT may contract `dx * dx + dz * dz` into an FMA that rounds once instead of
            // twice. At exactly the boundary they then differ by an ULP and the inclusive case
            // flips to rejected — which is codegen deciding a design question. The relative
            // slack below is ~16 ULPs at the authored 0.35 m, i.e. a distance difference of
            // ~2e-8 m: far under any separation the game can produce, and enough that the
            // documented boundary holds whichever way the arithmetic is emitted.
            float min    = Mathf.Max(0f, minSeparation);
            float minSqr = min * min;
            if (sqr < minSqr - minSqr * 1e-6f || sqr <= 0f)
            {
                direction = Vector3.zero;
                return false;
            }

            float inv = 1f / Mathf.Sqrt(sqr);
            direction = new Vector3(dx * inv, 0f, dz * inv);
            return true;
        }

        private bool IsFresh(bool present, float stamp, float now)
        {
            if (!present) return false;
            float age = now - stamp;
            return age >= 0f && age <= _windowSeconds;
        }
    }
}
